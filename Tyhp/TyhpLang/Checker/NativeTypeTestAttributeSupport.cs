using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Compile-time <c>#[\Tyhp\NativeTypeTest]</c>: free functions and concrete static methods
    /// with a type-guard return <c>$firstParam is T</c> are the native lowering for
    /// <c>$x is T</c> / <c>$x instanceof T</c>.
    /// </summary>
    internal static class NativeTypeTestAttributeSupport
    {
        private const string TyhpNativeTypeTestAttributeFqn = "\\Tyhp\\NativeTypeTest";

        public static bool IsNativeTypeTestAttribute(PhpAttributeAst attribute)
        {
            if (attribute.Name is PhpNameAst { BoundSymbol: ObjectDeclarationSymbol bound }
                && IsNativeTypeTestFullyQualifiedName(bound.FullyQualifiedName))
            {
                return true;
            }

            return IsNativeTypeTestFullyQualifiedName(GetAttributeNameText(attribute.Name));
        }

        public static bool IsNativeTypeTestFullyQualifiedName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            return trimmed.Equals(TyhpNativeTypeTestAttributeFqn, StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Tyhp\\NativeTypeTest", StringComparison.OrdinalIgnoreCase)
                || trimmed.EndsWith("\\Tyhp\\NativeTypeTest", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsLegalAttributeHost(IBase2Ast target) =>
            target is PhpFunctionDeclAst or TyhpdefImportFunctionDeclAst or PhpMethodDeclAst;

        public static PhpAttributeAst? FindNativeTypeTestAttribute(IBase2Ast? host)
        {
            if (host is null)
            {
                return null;
            }

            foreach (var attributeNode in host.AstAttributes)
            {
                if (attributeNode is PhpAttributeAst attribute && IsNativeTypeTestAttribute(attribute))
                {
                    return attribute;
                }
            }

            return null;
        }

        /// <summary>
        /// Canonical map key for a NativeTypeTest <c>T</c> after alias expansion: builtin names
        /// (<c>string</c>, <c>null</c>) or a leading-<c>\</c> class FQN. Null when <paramref name="type"/>
        /// is not a single (non-union, non-intersection, non-nullable, non-generic) type.
        /// </summary>
        public static bool TryGetSingleTypeKey(ICheckedType type, out string key)
        {
            key = "";
            switch (type)
            {
                case LiteralCheckedType { Value: null }:
                    key = "null";
                    return true;

                case SimpleCheckedType simple:
                    if (simple.ResolvedSymbol is BuiltInTypeSymbol builtin)
                    {
                        var name = builtin.Name.Trim();
                        if (IsRejectedBuiltinName(name))
                        {
                            return false;
                        }

                        key = name.ToLowerInvariant();
                        return key.Length > 0;
                    }

                    key = NormalizeObjectKey(simple.ResolvedSymbol.FullyQualifiedName);
                    return key.Length > 1;

                default:
                    return false;
            }
        }

        public static string NormalizeKey(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "";
            }

            var trimmed = name.Trim();
            if (trimmed.Contains('|', StringComparison.Ordinal)
                || trimmed.Contains('&', StringComparison.Ordinal)
                || trimmed.Contains('<', StringComparison.Ordinal)
                || trimmed.StartsWith('?'))
            {
                return "";
            }

            if (!trimmed.Contains('\\', StringComparison.Ordinal))
            {
                return trimmed.ToLowerInvariant();
            }

            return trimmed.StartsWith('\\') ? trimmed : "\\" + trimmed;
        }

        public static string NormalizeObjectKey(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "";
            }

            var trimmed = name.Trim();
            if (trimmed.Contains('|', StringComparison.Ordinal)
                || trimmed.Contains('&', StringComparison.Ordinal)
                || trimmed.Contains('<', StringComparison.Ordinal)
                || trimmed.StartsWith('?'))
            {
                return "";
            }

            return trimmed.StartsWith('\\') ? trimmed : "\\" + trimmed;
        }

        public static string SpellCall(IBaseSymbol callable, string argumentText)
        {
            if (callable is ObjectMethodSymbol method)
            {
                return SpellMethodCall(method, argumentText);
            }

            var function = (FunctionDeclarationSymbol)callable;
            var phpName = !string.IsNullOrEmpty(function.OriginalPhpName)
                ? function.OriginalPhpName
                : function.FullyQualifiedName;
            if (string.IsNullOrEmpty(phpName))
            {
                phpName = function.Name;
            }

            phpName = phpName.Trim();
            if (!phpName.StartsWith('\\'))
            {
                phpName = "\\" + phpName;
            }

            return phpName + "(" + argumentText + ")";
        }

        /// <summary>
        /// Looks up the NativeTypeTest lowering for an <c>is</c>/<c>instanceof</c> RHS after
        /// expanding transparent aliases. Parameterized names and generic type parameters miss.
        /// </summary>
        public static bool TryLookup(
            IBase2Ast? right,
            IReadOnlyDictionary<string, IBaseSymbol> map,
            out IBaseSymbol callable) =>
            TryLookup(right, map, resolveName: null, out callable);

        public static bool TryLookup(
            IBase2Ast? right,
            IReadOnlyDictionary<string, IBaseSymbol> map,
            Func<string, IBaseSymbol?>? resolveName,
            out IBaseSymbol callable)
        {
            callable = null!;
            if (right is null || map.Count == 0)
            {
                return false;
            }

            if (HasTypeArguments(right))
            {
                return false;
            }

            if (!TryGetKeyFromNode(right, [], resolveName, out var key))
            {
                return false;
            }

            return map.TryGetValue(key, out callable!);
        }

        /// <summary>
        /// Validates every free function or concrete static method that carries
        /// <c>#[\Tyhp\NativeTypeTest]</c> (source and tyhpdef, including overlays that are bound
        /// but not walked) and returns the <c>T → callable</c> map for emit.
        /// </summary>
        public static Dictionary<string, IBaseSymbol> IndexAndValidate(
            GlobalScope globalScope,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var map = new Dictionary<string, IBaseSymbol>(StringComparer.OrdinalIgnoreCase);
            var overlapIndex = new List<(IBaseSymbol Callable, ICheckedType GuardedType)>();
            var seenNodes = new HashSet<IBase2Ast>();

            foreach (var callable in EnumerateCallables(globalScope))
            {
                var declaring = callable.DeclaringAstNode;
                if (declaring is null || !seenNodes.Add(declaring))
                {
                    continue;
                }

                var attribute = FindNativeTypeTestAttribute(declaring);
                if (attribute is null)
                {
                    continue;
                }

                TryRegister(callable, declaring, attribute, map, overlapIndex, context, diagnostics);
            }

            return map;
        }

        private static void TryRegister(
            IBaseSymbol callable,
            IBase2Ast declaring,
            PhpAttributeAst attribute,
            Dictionary<string, IBaseSymbol> map,
            List<(IBaseSymbol Callable, ICheckedType GuardedType)> overlapIndex,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var state = CreateCheckerState(callable);

            if (!TryValidateHost(callable, declaring, attribute, state, diagnostics, out var parameters))
            {
                return;
            }

            var returnType = GetReturnType(callable, declaring);
            if (returnType is not TyhpReturnTypeGuardAst guard
                || guard.TypeExpression is null
                || !IsBareFirstParameterGuard(guard, parameters))
            {
                var firstName = parameters.Count > 0
                    ? "$" + parameters[0].Name.TrimStart('$')
                    : "$value";
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    attribute,
                    MessageCode.CheckerNativeTypeTestRequiresTypeGuard,
                    firstName);
                return;
            }

            if (!ExtraParametersAreDefaulted(parameters))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    attribute,
                    MessageCode.CheckerNativeTypeTestExtraParamsNeedDefaults);
                return;
            }

            var guardedType = context.ResolveTypeAnnotation(
                guard.TypeExpression,
                state,
                isReturnTypePosition: true,
                isUserTypeDeclaration: false);
            if (TypeComparer.IsUnresolvedType(guardedType))
            {
                return;
            }

            var overlapped = false;
            foreach (var existing in overlapIndex)
            {
                if (SameCallable(existing.Callable, callable)
                    || !GuardedTypesOverlap(guardedType, existing.GuardedType))
                {
                    continue;
                }

                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    attribute,
                    MessageCode.CheckerNativeTypeTestDuplicate,
                    guardedType.DisplayName,
                    existing.GuardedType.DisplayName,
                    SpellCallableName(existing.Callable));
                overlapped = true;
                break;
            }

            overlapIndex.Add((callable, guardedType));

            if (!TryGetSingleTypeKey(guardedType, out var key))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    attribute,
                    MessageCode.CheckerNativeTypeTestTypeNotSingle,
                    guardedType.DisplayName);
                return;
            }

            if (overlapped)
            {
                return;
            }

            map[key] = callable;
        }

        private static bool TryValidateHost(
            IBaseSymbol callable,
            IBase2Ast declaring,
            PhpAttributeAst attribute,
            CheckerState state,
            DiagnosticBag diagnostics,
            out IReadOnlyList<ParameterInfo> parameters)
        {
            parameters = [];
            if (callable is FunctionDeclarationSymbol function
                && declaring is PhpFunctionDeclAst or TyhpdefImportFunctionDeclAst)
            {
                parameters = function.Parameters;
                return true;
            }

            if (callable is ObjectMethodSymbol method && declaring is PhpMethodDeclAst)
            {
                var invalidKind = DescribeInvalidMethod(method);
                if (invalidKind is not null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        attribute,
                        MessageCode.CheckerNativeTypeTestInvalidTarget,
                        invalidKind);
                    return false;
                }

                parameters = method.Parameters;
                return true;
            }

            CheckerHelpers.ReportError(
                diagnostics,
                state,
                attribute,
                MessageCode.CheckerNativeTypeTestInvalidTarget,
                DescribeTarget(declaring));
            return false;
        }

        private static string? DescribeInvalidMethod(ObjectMethodSymbol method)
        {
            var owner = GetOwningObject(method);
            if (owner?.ObjectKind == PhpTypeDeclType.Interface)
            {
                return "interface method";
            }

            if (method.IsAbstract)
            {
                return "abstract method";
            }

            if (!method.IsStatic)
            {
                return "instance method";
            }

            return null;
        }

        private static CheckerState CreateCheckerState(IBaseSymbol callable)
        {
            if (callable is FunctionDeclarationSymbol function)
            {
                return new CheckerState
                {
                    ScopeType = ScopeType.FunctionDeclaration,
                    CurrentFileName = function.SourceFile ?? "",
                    EnclosingFunction = function,
                    EnclosingCallable = function,
                    FunctionGenerics = function.GenericParameters,
                };
            }

            var method = (ObjectMethodSymbol)callable;
            var owner = GetOwningObject(method);
            return new CheckerState
            {
                ScopeType = method.IsStatic
                    ? ScopeType.StaticMethodDeclaration
                    : ScopeType.InstanceMethodDeclaration,
                CurrentFileName = method.SourceFile ?? "",
                EnclosingObject = owner,
                EnclosingCallable = method,
                ObjectGenerics = owner?.GenericParameters ?? [],
                FunctionGenerics = method.GenericParameters,
            };
        }

        private static ITypeExpression? GetReturnType(IBaseSymbol callable, IBase2Ast declaring) =>
            callable switch
            {
                FunctionDeclarationSymbol function => function.ReturnType
                    ?? (declaring as PhpFunctionDeclAst)?.ReturnType
                    ?? (declaring as TyhpdefImportFunctionDeclAst)?.ReturnType,
                ObjectMethodSymbol method => method.ReturnType
                    ?? (declaring as PhpMethodDeclAst)?.ReturnType,
                _ => null,
            };

        private static bool IsBareFirstParameterGuard(
            TyhpReturnTypeGuardAst guard,
            IReadOnlyList<ParameterInfo> parameters)
        {
            if (parameters.Count == 0)
            {
                return false;
            }

            if (guard.GuardSubject is PhpDereferenceableAst)
            {
                return false;
            }

            var names = TypeGuardValidation.CollectGuardSubjectParameterNames(guard);
            if (names.Count != 1)
            {
                return false;
            }

            var first = parameters[0].Name.TrimStart('$');
            return string.Equals(names[0], first, StringComparison.Ordinal);
        }

        private static bool ExtraParametersAreDefaulted(IReadOnlyList<ParameterInfo> parameters)
        {
            for (var i = 1; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (parameter.DefaultValue is null && !parameter.IsVariadic)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Two guarded <c>T</c>s overlap only when they could be confused for the same
        /// <c>$x is T</c> registration key: exact equality (after flattening unions/nullables to
        /// atoms) or either side being <c>mixed</c>. Nominal subtyping (e.g. any class vs the
        /// builtin <c>object</c>, or a subclass vs its base class) is deliberately <em>not</em>
        /// overlap — <see cref="TryLookup"/> keys on the exact written <c>T</c>, so a test
        /// registered for <c>object</c> (e.g. <c>is_object</c>) never competes with one
        /// registered for a specific class, even though every class is a subtype of
        /// <c>object</c>. Using <c>IsSubtypeOf</c> here previously made it impossible to mark a
        /// class-specific test at all whenever <c>is_object</c> was also marked.
        /// </summary>
        private static bool GuardedTypesOverlap(ICheckedType left, ICheckedType right)
        {
            if (TypeComparer.AreTypesEqual(left, right))
            {
                return true;
            }

            if (TypeComparer.IsMixedType(left) || TypeComparer.IsMixedType(right))
            {
                return true;
            }

            foreach (var leftAtom in FlattenGuardedType(left))
            {
                foreach (var rightAtom in FlattenGuardedType(right))
                {
                    if (TypeComparer.IsMixedType(leftAtom) || TypeComparer.IsMixedType(rightAtom))
                    {
                        return true;
                    }

                    if (TypeComparer.AreTypesEqual(leftAtom, rightAtom))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<ICheckedType> FlattenGuardedType(ICheckedType type)
        {
            switch (type)
            {
                case UnionCheckedType union:
                    foreach (var member in union.Members)
                    {
                        foreach (var inner in FlattenGuardedType(member))
                        {
                            yield return inner;
                        }
                    }

                    yield break;

                case NullableCheckedType nullable:
                    foreach (var inner in FlattenGuardedType(nullable.InnerType))
                    {
                        yield return inner;
                    }

                    yield return CheckedTypes.Null;
                    yield break;

                default:
                    yield return type;
                    yield break;
            }
        }

        /// <summary>
        /// True when <paramref name="left"/> and <paramref name="right"/> denote the same free
        /// function or method (same emitted PHP callable), even across separate symbol instances
        /// bound in different passes. Used at check time (duplicate/overlap detection) and at
        /// emit time so a NativeTypeTest host's own body never lowers <c>$param is T</c> back
        /// into a self-call — see <c>TyhpEmitter.TryBuildReifiedInstanceofCheck</c>.
        /// </summary>
        public static bool SameCallable(IBaseSymbol? left, IBaseSymbol? right)
        {
            if (left is null || right is null)
            {
                return false;
            }

            if (ReferenceEquals(left, right))
            {
                return true;
            }

            var leftName = SpellCallableName(left);
            var rightName = SpellCallableName(right);
            return string.Equals(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        }

        private static string SpellCallableName(IBaseSymbol callable)
        {
            if (callable is ObjectMethodSymbol method)
            {
                var ownerName = SpellObjectName(GetOwningObject(method));
                var methodName = !string.IsNullOrEmpty(method.OriginalPhpName)
                    ? method.OriginalPhpName
                    : method.Name;
                return ownerName + "::" + methodName.Trim();
            }

            var function = (FunctionDeclarationSymbol)callable;
            var name = !string.IsNullOrEmpty(function.OriginalPhpName)
                ? function.OriginalPhpName
                : function.FullyQualifiedName;
            if (string.IsNullOrEmpty(name))
            {
                name = function.Name;
            }

            name = name.Trim();
            return name.StartsWith('\\') ? name : "\\" + name;
        }

        private static string SpellMethodCall(ObjectMethodSymbol method, string argumentText)
        {
            var ownerName = SpellObjectName(GetOwningObject(method));
            var methodName = !string.IsNullOrEmpty(method.OriginalPhpName)
                ? method.OriginalPhpName
                : method.Name;
            return ownerName + "::" + methodName.Trim() + "(" + argumentText + ")";
        }

        private static string SpellObjectName(ObjectDeclarationSymbol? owner)
        {
            var name = !string.IsNullOrEmpty(owner?.OriginalPhpName)
                ? owner!.OriginalPhpName
                : owner?.FullyQualifiedName ?? owner?.Name ?? "";
            name = name.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return "\\";
            }

            return name.StartsWith('\\') ? name : "\\" + name;
        }

        private static ObjectDeclarationSymbol? GetOwningObject(ObjectMethodSymbol method)
        {
            for (var scope = method.ContainingScope; scope is not null; scope = scope.ParentScope)
            {
                if (scope.DeclarationSymbol is ObjectDeclarationSymbol owner)
                {
                    return owner;
                }
            }

            return null;
        }

        private static bool IsRejectedBuiltinName(string name) =>
            name.Equals("mixed", StringComparison.OrdinalIgnoreCase)
            || name.Equals("void", StringComparison.OrdinalIgnoreCase)
            || name.Equals("never", StringComparison.OrdinalIgnoreCase);

        private static string DescribeTarget(IBase2Ast target) =>
            target switch
            {
                PhpMethodDeclAst => "method",
                PhpInlineFunctionAst => "closure",
                PhpPropertyDeclAst => "property",
                PhpParameterAst => "parameter",
                PhpObjectTypeDeclAst objectType =>
                    objectType.DeclType?.ValueString?.ToLowerInvariant() ?? "class",
                _ => target.GetType().Name,
            };

        private static IEnumerable<IBaseSymbol> EnumerateCallables(IBaseScope scope)
        {
            var pending = new Stack<IBaseScope>();
            pending.Push(scope);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var symbol in current.GetAllChildSymbols())
                {
                    if (symbol is FunctionDeclarationSymbol function)
                    {
                        yield return function;
                        foreach (var overload in function.Overloads)
                        {
                            yield return overload;
                        }
                    }
                    else if (symbol is ObjectMethodSymbol method)
                    {
                        yield return method;
                        foreach (var overload in method.Overloads)
                        {
                            yield return overload;
                        }
                    }
                }

                foreach (var child in current.GetAllChildScopes())
                {
                    pending.Push(child);
                }
            }
        }

        private static string? GetAttributeNameText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => !string.IsNullOrEmpty(name.ValueString) ? name.ValueString : name.Identifier,
                TokenValueAst token => !string.IsNullOrEmpty(token.ValueString) ? token.ValueString : token.Identifier,
                IExpression expr => expr.Identifier,
                _ => null,
            };

        private static bool TryGetKeyFromNode(
            IBase2Ast node,
            HashSet<IBaseSymbol> visiting,
            Func<string, IBaseSymbol?>? resolveName,
            out string key)
        {
            key = "";
            switch (node)
            {
                case PhpBuiltinTypeAst builtin:
                    return TryKeyFromBuiltinSpelling(builtin.Identifier, out key);

                case PhpNameAst name:
                    return TryGetKeyFromName(name, visiting, resolveName, out key);

                case PhpTypeExpressionAst composite:
                    if (composite.IsNullable || composite.TypeKind is PhpTypeKind.Union or PhpTypeKind.Intersection)
                    {
                        return false;
                    }

                    var members = composite.Types?.GetAllNotNull().ToList();
                    if (members is not { Count: 1 })
                    {
                        return false;
                    }

                    return TryGetKeyFromNode(members[0], visiting, resolveName, out key);

                case PhpNamedTypeAst named:
                    // A wrapped class/builtin name (`PhpNamedTypeAst` carries the bound symbol;
                    // its `Identifier` is empty, so recurse into the inner `Name` expression).
                    return named.Name is IBase2Ast namedInner
                        && TryGetKeyFromNode(namedInner, visiting, resolveName, out key);

                case TyhpObjectShapeAst:
                    // Object shapes erase to `object` in PHP but are not NativeTypeTest `object`.
                    return false;

                case TyhpCallableShapeAst:
                    // Callable shapes erase to `callable` in PHP but are not NativeTypeTest `callable`.
                    return false;

                case TyhpStructShapeAst:
                    // Struct shapes erase to `array` in PHP but are not NativeTypeTest `array`:
                    // a struct guard must reify to `Type::struct(...)` for field/key validation,
                    // never collide with a plain `is_array` registration. This node is reached
                    // for an unnamed / inline struct shape. A named `type Name = struct { }`
                    // (file-level or class-member) binds as a struct class instead and never
                    // recurses here (see `TryGetKeyFromName`'s `ObjectDeclarationSymbol` branch).
                    return false;

                default:
                    return TryKeyFromBuiltinSpelling(node.Identifier, out key);
            }
        }

        private static bool TryGetKeyFromName(
            PhpNameAst name,
            HashSet<IBaseSymbol> visiting,
            Func<string, IBaseSymbol?>? resolveName,
            out string key)
        {
            key = "";
            if (HasTypeArguments(name))
            {
                return false;
            }

            var written = (name.ValueString ?? name.Identifier ?? "").Trim();
            var symbol = name.BoundSymbol
                ?? (written.Length > 0 ? resolveName?.Invoke(written) : null);

            if (symbol is GenericTypeParameterSymbol)
            {
                return false;
            }

            if (symbol is TypeAliasSymbol or ObjectTypeAliasSymbol)
            {
                if (!visiting.Add(symbol))
                {
                    return false;
                }

                var body = symbol is TypeAliasSymbol typeAlias
                    ? typeAlias.AliasedType
                    : ((ObjectTypeAliasSymbol)symbol).AliasedType;
                return body is IBase2Ast bodyNode
                    && TryGetKeyFromNode(bodyNode, visiting, resolveName, out key);
            }

            if (symbol is ObjectDeclarationSymbol objectDecl)
            {
                key = NormalizeObjectKey(objectDecl.FullyQualifiedName);
                return key.Length > 1;
            }

            if (symbol is BuiltInTypeSymbol builtin)
            {
                return TryKeyFromBuiltinSpelling(builtin.Name, out key);
            }

            return TryKeyFromBuiltinSpelling(written, out key);
        }

        private static bool TryKeyFromBuiltinSpelling(string? spelling, out string key)
        {
            key = "";
            if (string.IsNullOrWhiteSpace(spelling) || spelling.Contains('\\'))
            {
                return false;
            }

            var trimmed = spelling.Trim().TrimStart('\\');
            if (IsRejectedBuiltinName(trimmed) || trimmed.StartsWith('?')
                || trimmed.Contains('|', StringComparison.Ordinal)
                || trimmed.Contains('&', StringComparison.Ordinal)
                || trimmed.Contains('<', StringComparison.Ordinal))
            {
                return false;
            }

            key = trimmed.ToLowerInvariant();
            return key.Length > 0;
        }

        private static bool HasTypeArguments(IBase2Ast node)
        {
            foreach (var addonKey in (string[])["typeName", "identifier"])
            {
                if (node.AstGrammarAddons.TryGetValue(addonKey, out var addon)
                    && addon is PhpTypeExpressionListAst list
                    && list.GetAllNotNull().Any())
                {
                    return true;
                }
            }

            return node is TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst args }
                && args.GetAllNotNull().Any();
        }
    }
}
