using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Story 21.6 Phase 5 — extra <c>bind</c> / <c>bindTo</c> / <c>call</c> rules that tyhpdef
    /// overloads cannot express: leftover-scope <c>instanceof</c>, object vs
    /// <c>__SuperTypeName</c> <c>$newScope</c>, non-rebindable producers, internal-class
    /// <c>$newThis</c>, and <c>call(null)</c>.
    /// </summary>
    internal static class ClosureBindSupport
    {
        public static bool IsStaticScopeSentinel(ICheckedType type) =>
            SymbolNameTypeHelper.TryGetStringLiteral(Unwrap(type), out var value)
            && string.Equals(value, "static", StringComparison.Ordinal);

        public static bool IsKeepOldScopeOverload(ObjectMethodSymbol method)
        {
            foreach (var param in method.Parameters)
            {
                if (string.Equals(param.Name.TrimStart('$'), "newScope", StringComparison.OrdinalIgnoreCase)
                    && param.DefaultValue is not null)
                {
                    return true;
                }
            }

            foreach (var generic in method.GenericParameters)
            {
                if (string.Equals(generic.Name, "TNewScope", StringComparison.Ordinal)
                    && ConstraintIsStaticSentinel(generic.Constraint))
                {
                    return true;
                }
            }

            return false;
        }

        public static void Check(
            ObjectMethodSymbol method,
            ICheckedType receiverType,
            PhpCallAst call,
            IBase2Ast reportNode,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (CheckerHelpers.IsFirstClassCallableArgumentList(call.Arguments))
            {
                return;
            }

            var isBind = IsBindMethod(method);
            var isBindTo = IsBindToMethod(method);
            var isCall = IsCallMethod(method);
            if (!isBind && !isBindTo && !isCall)
            {
                return;
            }

            ICheckedType closureType;
            IExpression? newThisExpr;
            IExpression? newScopeExpr;
            if (isBind)
            {
                var closureExpr = GetArgument(call, method.Parameters, 0);
                closureType = closureExpr is null
                    ? CheckedTypes.Unresolved
                    : context.ResolveExpressionType(closureExpr, state);
                newThisExpr = GetArgument(call, method.Parameters, 1);
                newScopeExpr = GetArgument(call, method.Parameters, 2);
            }
            else
            {
                closureType = receiverType;
                newThisExpr = GetArgument(call, method.Parameters, 0);
                newScopeExpr = isCall ? null : GetArgument(call, method.Parameters, 1);
            }

            var newThisType = ClosureProducerInference.UnwrapLateStatic(
                newThisExpr is null
                    ? CheckedTypes.Null
                    : context.ResolveExpressionType(newThisExpr, state));
            var newScopeType = newScopeExpr is null
                ? null
                : ClosureProducerInference.UnwrapLateStatic(
                    context.ResolveExpressionType(newScopeExpr, state));
            var site = newThisExpr ?? newScopeExpr ?? reportNode;

            if (IsNonRebindable(closureType))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, site, MessageCode.CheckerClosureNonRebindable);
                return;
            }

            // PHP only rejects an internal class as `newScope` (`Cannot bind closure to scope of
            // internal class …`, enforced since PHP 7.0) — `newThis` may legally be an instance
            // of an internal class such as `stdClass`. Checking `newThisType` here would reject
            // legal binds like `$closure->bindTo(new \stdClass())`.
            if (newScopeType is not null && IsPhpEngineClass(newScopeType))
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, site, MessageCode.CheckerClosureNonRebindable);
                return;
            }

            if (isCall && IsKnownNull(newThisType))
            {
                ReportIncompatible(diagnostics, state, site, newThisType, "object");
                return;
            }

            ClosureProducerInference.TryGetClosureSlots(
                closureType,
                out _,
                out var thisType,
                out var scopeType,
                out var thisSpecified,
                out var scopeSpecified);

            var keepOldScope = isCall || newScopeExpr is null || IsKeepOldScopeOverload(method);
            if (keepOldScope)
            {
                CheckLeftoverScope(
                    newThisType,
                    thisType,
                    scopeType,
                    thisSpecified,
                    scopeSpecified,
                    site,
                    state,
                    context,
                    diagnostics);
                return;
            }

            if (newScopeType is null || IsGradualThis(thisType, thisSpecified))
            {
                return;
            }

            CheckExplicitScope(
                newThisType,
                newScopeType,
                site,
                state,
                context,
                diagnostics);
        }

        private static void CheckLeftoverScope(
            ICheckedType newThisType,
            ICheckedType thisType,
            ICheckedType scopeType,
            bool thisSpecified,
            bool scopeSpecified,
            IBase2Ast site,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var gradualThis = IsGradualThis(thisType, thisSpecified);
            var scopeClass = TryGetScopeClass(scopeType, scopeSpecified)
                ?? (thisSpecified ? TryGetScopeClass(thisType, thisSpecified: true) : null);

            if (IsKnownNull(thisType) && thisSpecified)
            {
                if (!IsKnownNull(newThisType)
                    && scopeSpecified
                    && TryGetScopeClass(scopeType, scopeSpecified) is not null)
                {
                    ReportIncompatible(diagnostics, state, site, newThisType, "null");
                }

                return;
            }

            if (gradualThis && (scopeClass is null || IsGradualScope(scopeType, scopeSpecified)))
            {
                return;
            }

            if (IsKnownNull(newThisType) && thisSpecified && !IsKnownNull(thisType))
            {
                ReportIncompatible(
                    diagnostics,
                    state,
                    site,
                    newThisType,
                    thisType.DisplayName);
                return;
            }

            if (scopeClass is null)
            {
                return;
            }

            if (CanProveNotInstanceOf(newThisType, scopeClass, context))
            {
                ReportIncompatible(
                    diagnostics,
                    state,
                    site,
                    newThisType,
                    scopeClass.DisplayName);
            }
        }

        private static void CheckExplicitScope(
            ICheckedType newThisType,
            ICheckedType newScopeType,
            IBase2Ast site,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            newScopeType = Unwrap(newScopeType);
            if (IsStaticScopeSentinel(newScopeType))
            {
                return;
            }

            if (IsKnownNull(newScopeType))
            {
                if (!IsKnownNull(newThisType) && !IsGradualThis(newThisType, thisSpecified: true))
                {
                    ReportIncompatible(diagnostics, state, site, newThisType, "null");
                }

                return;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(newScopeType) is not null)
            {
                if (CanProveNotInstanceOf(newThisType, newScopeType, context))
                {
                    ReportIncompatible(
                        diagnostics, state, site, newThisType, newScopeType.DisplayName);
                }

                return;
            }

            if (TryClassFromNameBrand(newScopeType) is { } branded)
            {
                if (CanProveNotInstanceOf(newThisType, branded, context))
                {
                    ReportIncompatible(
                        diagnostics, state, site, newThisType, branded.DisplayName);
                }

                return;
            }

            if (!SymbolNameTypeHelper.TryGetStringLiteral(newScopeType, out var literal)
                || string.IsNullOrEmpty(literal))
            {
                return;
            }

            if (!IsSuperTypeNameOf(literal, newThisType, state, context))
            {
                ReportIncompatible(
                    diagnostics,
                    state,
                    site,
                    new LiteralCheckedType(
                        literal,
                        new SimpleCheckedType(new BuiltInTypeSymbol("string"))),
                    $"__SuperTypeName<{newThisType.DisplayName}>");
            }
        }

        private static bool IsSuperTypeNameOf(
            string literal,
            ICheckedType newThisType,
            CheckerState state,
            CheckerRuleContext context)
        {
            var brandType = CheckerHelpers.ResolveNamedType(
                "__SuperTypeName", context.SymbolTree, context.GlobalScope);
            if (brandType.Kind == CheckedTypeKind.Unresolved)
            {
                brandType = CheckerHelpers.ResolveNamedType(
                    "\\__SuperTypeName", context.SymbolTree, context.GlobalScope);
            }

            if (brandType.Kind == CheckedTypeKind.Unresolved)
            {
                var named = ClosureProducerInference.ResolveClassByName(
                    literal, context.SymbolTree, context.GlobalScope);
                return named is not null
                    && TypeComparer.IsAssignableTo(
                        newThisType,
                        CheckedTypes.FromSymbol(named),
                        context.SymbolTree,
                        context.GlobalScope);
            }

            var brand = new GenericCheckedType(brandType, [newThisType]);
            return SymbolNameExistenceVerifier.VerifyLiteral(
                literal, brand, state, context.SymbolTree, context.GlobalScope);
        }

        private static bool CanProveNotInstanceOf(
            ICheckedType newThisType,
            ICheckedType scopeClass,
            CheckerRuleContext context)
        {
            newThisType = Unwrap(newThisType);
            scopeClass = Unwrap(scopeClass);
            if (TypeComparer.IsUnresolvedType(newThisType)
                || TypeComparer.IsMixedType(newThisType)
                || TypeComparer.IsUnresolvedType(scopeClass)
                || TypeComparer.IsMixedType(scopeClass))
            {
                return false;
            }

            if (newThisType is UnionCheckedType union)
            {
                return union.Members.Count > 0
                    && union.Members.All(m => CanProveNotInstanceOf(m, scopeClass, context));
            }

            if (TypeComparer.IsBuiltInName(newThisType, "object")
                || IsWideObjectNullUnion(newThisType))
            {
                return false;
            }

            return !TypeComparer.IsAssignableTo(
                newThisType, scopeClass, context.SymbolTree, context.GlobalScope);
        }

        private static void ReportIncompatible(
            DiagnosticBag diagnostics,
            CheckerState state,
            IBase2Ast site,
            ICheckedType actual,
            string expected) =>
            CheckerHelpers.ReportError(
                diagnostics,
                state,
                site,
                MessageCode.CheckerClosureBindIncompatible,
                actual.DisplayName,
                expected);

        /// <summary>
        /// Phase 4 stamps enclosing <c>TThis</c> as the declaring class, not late-static
        /// <c>static</c>. Bind inference must do the same so <c>bindTo($this)</c> keeps
        /// <c>TThis</c> as that class rather than substituting the <c>$this</c> expression's
        /// <c>static</c> type.
        /// </summary>
        public static void UnwrapLateStaticBindBindings(
            ObjectMethodSymbol? method,
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings)
        {
            if (method is null
                || bindings.Count == 0
                || (!IsBindMethod(method) && !IsBindToMethod(method) && !IsCallMethod(method)))
            {
                return;
            }

            foreach (var key in bindings.Keys.ToList())
            {
                bindings[key] = ClosureProducerInference.UnwrapLateStatic(bindings[key]);
            }
        }

        private static bool IsBindMethod(ObjectMethodSymbol method) =>
            method.IsStatic
            && string.Equals(method.Name, "bind", StringComparison.Ordinal)
            && IsClosureOwner(method);

        private static bool IsBindToMethod(ObjectMethodSymbol method) =>
            !method.IsStatic
            && string.Equals(method.Name, "bindTo", StringComparison.Ordinal)
            && IsClosureOwner(method);

        private static bool IsCallMethod(ObjectMethodSymbol method) =>
            !method.IsStatic
            && string.Equals(method.Name, "call", StringComparison.Ordinal)
            && IsClosureOwner(method);

        private static bool IsClosureOwner(ObjectMethodSymbol method) =>
            CallableArityFacetBuilder.IsClosureDeclaration(
                ClosureProducerInference.TryGetOwningObject(method));

        private static bool IsNonRebindable(ICheckedType type)
        {
            type = Unwrap(type);
            return type is GenericCheckedType { IsNonRebindableClosure: true };
        }

        private static bool IsGradualThis(ICheckedType thisType, bool thisSpecified)
        {
            if (!thisSpecified)
            {
                return true;
            }

            thisType = Unwrap(thisType);
            if (TypeComparer.IsUnresolvedType(thisType) || TypeComparer.IsMixedType(thisType))
            {
                return true;
            }

            if (TypeComparer.TryGetNominalSymbol(thisType) is TypeAliasSymbol alias)
            {
                var name = alias.Name.TrimStart('\\');
                return name.Equals("__ClosureThis", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("ClosureThis", StringComparison.OrdinalIgnoreCase);
            }

            return TypeComparer.IsBuiltInName(thisType, "object")
                || IsWideObjectNullUnion(thisType);
        }

        private static bool IsGradualScope(ICheckedType scopeType, bool scopeSpecified)
        {
            if (!scopeSpecified)
            {
                return true;
            }

            scopeType = Unwrap(scopeType);
            if (TypeComparer.IsUnresolvedType(scopeType) || TypeComparer.IsMixedType(scopeType))
            {
                return true;
            }

            if (TypeComparer.TryGetNominalSymbol(scopeType) is TypeAliasSymbol alias)
            {
                var name = alias.Name.TrimStart('\\');
                return name.Equals("__ClosureScope", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("ClosureScope", StringComparison.OrdinalIgnoreCase);
            }

            return scopeType is UnionCheckedType union
                && union.Members.Any(m =>
                    TypeComparer.IsNullLiteral(m) || TypeComparer.IsBuiltInName(m, "null"));
        }

        private static ICheckedType? TryGetScopeClass(ICheckedType type, bool thisSpecified)
        {
            if (!thisSpecified)
            {
                return null;
            }

            type = Unwrap(type);
            if (IsKnownNull(type)
                || TypeComparer.IsUnresolvedType(type)
                || TypeComparer.IsMixedType(type)
                || TypeComparer.IsBuiltInName(type, "object")
                || IsWideObjectNullUnion(type))
            {
                return null;
            }

            if (TypeComparer.TryGetNominalSymbol(type) is TypeAliasSymbol)
            {
                return null;
            }

            if (CheckerHelpers.TryGetObjectDeclaration(type) is not null)
            {
                return type;
            }

            if (TryClassFromNameBrand(type) is { } branded)
            {
                return branded;
            }

            if (type is UnionCheckedType union)
            {
                ICheckedType? found = null;
                foreach (var member in union.Members)
                {
                    if (TypeComparer.IsNullLiteral(member) || TypeComparer.IsBuiltInName(member, "null"))
                    {
                        continue;
                    }

                    var extracted = TryGetScopeClass(member, thisSpecified: true);
                    if (extracted is null)
                    {
                        return null;
                    }

                    if (found is null)
                    {
                        found = extracted;
                    }
                    else if (!TypeComparer.AreTypesEqual(found, extracted))
                    {
                        return null;
                    }
                }

                return found;
            }

            return null;
        }

        private static ICheckedType? TryClassFromNameBrand(ICheckedType type) =>
            ClosureProducerInference.TryClassFromClassNameBrand(Unwrap(type));

        private static bool IsKnownNull(ICheckedType type)
        {
            type = Unwrap(type);
            return TypeComparer.IsNullLiteral(type) || TypeComparer.IsBuiltInName(type, "null");
        }

        private static bool IsWideObjectNullUnion(ICheckedType type) =>
            type is UnionCheckedType union
            && union.Members.Count > 0
            && union.Members.All(m =>
                TypeComparer.IsNullLiteral(m)
                || TypeComparer.IsBuiltInName(m, "null")
                || TypeComparer.IsBuiltInName(m, "object"));

        private static bool IsPhpEngineClass(ICheckedType type)
        {
            type = Unwrap(type);
            if (TryClassFromNameBrand(type) is { } branded)
            {
                type = branded;
            }

            var obj = CheckerHelpers.TryGetObjectDeclaration(type);
            if (obj is null)
            {
                return false;
            }

            var path = (obj.SourceFile ?? string.Empty).Replace('\\', '/');
            if (path.Length == 0)
            {
                return false;
            }

            return path.Contains("packages/php/", StringComparison.OrdinalIgnoreCase)
                || path.Contains("packages/php-ext-", StringComparison.OrdinalIgnoreCase)
                || path.Contains("php/_tyhpdef/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ConstraintIsStaticSentinel(ITypeExpression? constraint)
        {
            if (constraint is PhpBuiltinTypeAst builtin
                && StaticValueTypeHelper.TryParse(builtin.Identifier, out var value, out _)
                && value is string s
                && string.Equals(s, "static", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        private static IExpression? GetArgument(
            PhpCallAst call,
            IReadOnlyList<ParameterInfo> parameters,
            int positionalIndex)
        {
            if (call.Arguments is null || positionalIndex < 0)
            {
                return null;
            }

            var param = positionalIndex < parameters.Count ? parameters[positionalIndex] : null;
            var bare = param?.Name.TrimStart('$');
            var args = call.Arguments.GetAllNotNull().ToList();
            foreach (var arg in args)
            {
                if (arg.IsVariadic || arg.Expression is null || arg.Name?.ValueString is not { } named)
                {
                    continue;
                }

                if (string.Equals(named.TrimStart('$'), bare, StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Expression;
                }
            }

            var positional = 0;
            foreach (var arg in args)
            {
                if (arg.IsVariadic || arg.Expression is null || arg.Name is not null)
                {
                    continue;
                }

                if (positional == positionalIndex)
                {
                    return arg.Expression;
                }

                positional++;
            }

            return null;
        }

        private static ICheckedType Unwrap(ICheckedType type)
        {
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            return type;
        }
    }
}
