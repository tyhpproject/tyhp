using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    public sealed partial class TypeInferrer
    {
        private ICheckedType ResolveTypeExpressionCore(
            ITypeExpression typeAst,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration)
        {
            switch (typeAst)
            {
                case TyhpTemplateStringTypeAst template:
                    return ResolveTemplateStringType(template, state, isReturnTypePosition, isUserTypeDeclaration);

                case PhpBuiltinTypeAst builtin:
                    return ResolveBuiltinType(builtin, state, isReturnTypePosition, isUserTypeDeclaration);

                case PhpNamedTypeAst named:
                    return ResolveNamedType(named, state, isReturnTypePosition, isUserTypeDeclaration);

                case PhpTypeExpressionAst composite:
                    return ResolveCompositeType(composite, state, isReturnTypePosition, isUserTypeDeclaration);

                default:
                    return CheckedTypes.Unresolved;
            }
        }

        private ICheckedType ResolveBuiltinType(
            PhpBuiltinTypeAst builtin,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration)
        {
            var name = builtin.Identifier ?? string.Empty;
            if (isUserTypeDeclaration &&
                string.Equals(name, "resource", StringComparison.OrdinalIgnoreCase))
            {
                ReportResourceRestriction(builtin, state);
            }

            // In-scope class/method generics (`T` in `PropertyPath<T, R>` on `select<R>`) must
            // win before SymbolTree.ResolveType, which reports TYHP3003 via `_diagnostics`.
            if (TryResolveInScopeGenericParameter(name, state, out var builtinGenericParam))
            {
                return builtinGenericParam;
            }

            // Static-value literals (`'red'`, `42`, …) must stay precise LiteralCheckedType even
            // when the binder widened BoundSymbol to the underlying scalar for 3019/3020.
            if (StaticValueTypeHelper.TryParse(name, out var literalValue, out var underlyingName)
                && literalValue is not null)
            {
                return new LiteralCheckedType(
                    literalValue,
                    new SimpleCheckedType(new BuiltInTypeSymbol(underlyingName)));
            }

            var fromScope = GetResolutionScope(state);
            var symbol = _symbolTree.ResolveType(builtin, fromScope, _diagnostics);

            if (builtin.AstGrammarAddons.TryGetValue("typeName", out var genericAddon)
                && genericAddon is PhpTypeExpressionListAst genericList)
            {
                var args = ResolveGenericTypeArgumentList(
                    genericList, state, isReturnTypePosition, isUserTypeDeclaration);

                if (symbol is BuiltInUtilityTypeSymbol utilityWithArgs)
                {
                    return UtilityTypeResolver.Resolve(
                        utilityWithArgs, args, builtin, state, _symbolTree, _globalScope, _diagnostics,
                        ResolveTypeExpressionCore);
                }

                // `static<T>` is banned; `self<T>` / `parent<T>` use the enclosing object as base.
                if (IsRelativeTypeName(name))
                {
                    if (string.Equals(name, "static", StringComparison.OrdinalIgnoreCase))
                    {
                        ReportDiagnostic(builtin, state, MessageCode.CheckerParameterizedStaticForbidden);
                        return CheckedTypes.Unresolved;
                    }

                    var relativeBase = ResolveRelativeType(name, builtin, state, isReturnTypePosition);
                    return GenericTypeArgumentValidator.ValidateInstantiation(
                        relativeBase, args, builtin, state, _symbolTree, _globalScope, _diagnostics,
                        ResolveTypeExpressionCore);
                }

                var genericBaseType = symbol is null
                    ? (TryGetBuiltinSingleton(name, out var genericSingleton) ? genericSingleton! : CheckedTypes.Unresolved)
                    : CheckedTypes.FromSymbol(symbol);

                return GenericTypeArgumentValidator.ValidateInstantiation(
                    genericBaseType, args, builtin, state, _symbolTree, _globalScope, _diagnostics,
                    ResolveTypeExpressionCore);
            }

            if (symbol is BuiltInUtilityTypeSymbol utilitySymbol &&
                utilitySymbol.GenericParameterRequirements.MinArity == 0)
            {
                return UtilityTypeResolver.Resolve(
                    utilitySymbol, [], builtin, state, _symbolTree, _globalScope, _diagnostics,
                    ResolveTypeExpressionCore);
            }

            // Bare `self` / `static` / `parent` (no type-argument list): do not trust binder
            // ResolveType alone — `static` must stay a distinct late-bound type, and `self`/
            // `parent` must use EnclosingObject even when NameResolutionScope is a file scope.
            if (IsRelativeTypeName(name))
            {
                return ResolveRelativeType(name, builtin, state, isReturnTypePosition);
            }

            var baseType = symbol is null
                ? (TryGetBuiltinSingleton(name, out var singleton) ? singleton! : CheckedTypes.Unresolved)
                : CheckedTypes.FromSymbol(symbol);

            if (TryGetBuiltinSingleton(name, out var singletonType))
            {
                return singletonType!;
            }

            return baseType;
        }

        private ICheckedType ResolveNamedType(
            PhpNamedTypeAst named,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration)
        {
            var nameExpr = named.Name;
            var typeName = GetExpressionText(nameExpr);
            var typeArgList = GetNamedTypeArgumentList(named, nameExpr);
            var hasTypeArgs = typeArgList is not null && typeArgList.GetAllNotNull().Any();

            if (IsRelativeTypeName(typeName) && !hasTypeArgs)
            {
                return ResolveRelativeType(typeName!, named, state, isReturnTypePosition);
            }

            // In-scope class/method generics must win before generic-instantiation /
            // SymbolTree.ResolveType — including when a spurious empty-or-nested typeName addon
            // would otherwise send `T` in `Expression<T, R>` through ResolveNamedTypeWithArguments
            // and report TYHP3003.
            if (!IsRelativeTypeName(typeName)
                && TryResolveInScopeGenericParameter(typeName, state, out var genericParamType))
            {
                return genericParamType;
            }

            if (nameExpr is TyhpGenericIdentifierAst genericName)
            {
                return ResolveGenericNamedType(
                    genericName, named, state, isReturnTypePosition, isUserTypeDeclaration);
            }

            if (typeArgList is not null && hasTypeArgs)
            {
                return ResolveNamedTypeWithArguments(
                    named, typeArgList, state, isReturnTypePosition, isUserTypeDeclaration);
            }

            var fromScope = GetResolutionScope(state);
            var symbol = _symbolTree.ResolveType(named, fromScope, _diagnostics);
            // Bare reference to a 0-min-arity utility (including optional-single forms like
            // `__ClassName` ≡ `__ClassName<object>`): resolve through the utility path.
            if (symbol is BuiltInUtilityTypeSymbol utilitySymbol &&
                utilitySymbol.GenericParameterRequirements.MinArity == 0)
            {
                return UtilityTypeResolver.Resolve(
                    utilitySymbol, [], named, state, _symbolTree, _globalScope, _diagnostics,
                    ResolveTypeExpressionCore);
            }

            if (symbol is null)
            {
                ReportUnresolvedNamedTypeIfNeeded(named, typeName, state, isUserTypeDeclaration);
                return CheckedTypes.Unresolved;
            }

            return ApplyDefaultsForBareGenericReference(symbol, named, state);
        }

        /// <summary>
        /// Bare <c>Box</c> / <c>new Box()</c> where every omitted type parameter has a default becomes
        /// <c>Box&lt;default…&gt;</c>. Inside the open generic itself, keep the bare form so
        /// <c>ObjectGenerics</c> stay meaningful (same rule as <see cref="GenericInheritanceBindings"/>).
        /// </summary>
        private ICheckedType ApplyDefaultsForBareGenericReference(
            IBaseSymbol symbol,
            IBase2Ast reportNode,
            CheckerState state)
        {
            IReadOnlyList<GenericTypeParameterSymbol> genericParams = symbol switch
            {
                ObjectDeclarationSymbol obj => obj.GenericParameters,
                TypeAliasSymbol alias => alias.GenericParameters,
                _ => [],
            };

            if (genericParams.Count == 0 || !genericParams.Any(p => p.HasDefault))
            {
                return FromResolvedTypeSymbol(symbol);
            }

            if (symbol is ObjectDeclarationSymbol enclosingObj
                && ReferenceEquals(state.EnclosingObject, enclosingObj))
            {
                return FromResolvedTypeSymbol(symbol);
            }

            return GenericTypeArgumentValidator.ValidateInstantiation(
                CheckedTypes.FromSymbol(symbol),
                [],
                reportNode,
                state,
                _symbolTree,
                _globalScope,
                _diagnostics,
                ResolveTypeExpressionCore);
        }

        /// <summary>
        /// Maps resolved type symbols to checked types, preserving precise <c>true</c>/<c>false</c>/<c>null</c>
        /// literal types instead of leaving them as bare <see cref="SimpleCheckedType"/> builtins.
        /// </summary>
        private static ICheckedType FromResolvedTypeSymbol(IBaseSymbol symbol)
        {
            if (symbol is BuiltInTypeSymbol builtIn)
            {
                return builtIn.Name.ToLowerInvariant() switch
                {
                    "true" => new LiteralCheckedType(true, new SimpleCheckedType(builtIn)),
                    "false" => new LiteralCheckedType(false, new SimpleCheckedType(builtIn)),
                    "null" => CheckedTypes.Null,
                    // Match ResolveBuiltinType / TryGetBuiltinSingleton so named `mixed`/`void`/
                    // `never` (PhpNamedTypeAst path) share the SpecialCheckedType singletons.
                    // Without this, union members stay SimpleCheckedType and `.IsMixed` stays false
                    // — ValidateUnion / UnionCheckedType.IsMixed miss them (FOUND #1g).
                    "mixed" => CheckedTypes.Mixed,
                    "void" => CheckedTypes.Void,
                    "never" => CheckedTypes.Never,
                    _ => CheckedTypes.FromSymbol(symbol),
                };
            }

            return CheckedTypes.FromSymbol(symbol);
        }

        private ICheckedType ResolveGenericNamedType(
            TyhpGenericIdentifierAst genericName,
            PhpNamedTypeAst named,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration)
        {
            if (genericName.GenericArguments is not PhpTypeExpressionListAst argList
                || !argList.GetAllNotNull().Any())
            {
                var typeName = GetExpressionText(genericName);
                if (TryResolveInScopeGenericParameter(typeName, state, out var genericParamType))
                {
                    return genericParamType;
                }

                var fromScope = GetResolutionScope(state);
                var baseSymbol = _symbolTree.ResolveType(named, fromScope, _diagnostics);
                if (baseSymbol is null)
                {
                    ReportUnresolvedNamedTypeIfNeeded(
                        named, typeName, state, isUserTypeDeclaration);
                    return CheckedTypes.Unresolved;
                }

                return CheckedTypes.FromSymbol(baseSymbol);
            }

            return ResolveNamedTypeWithArguments(
                named, argList, state, isReturnTypePosition, isUserTypeDeclaration);
        }

        /// <summary>
        /// Type-argument list on a named type: <see cref="TyhpGenericIdentifierAst.GenericArguments"/>
        /// or the <c>typeName</c> grammar addon. Empty lists are treated as absent so a bare
        /// in-scope generic parameter is not forced through instantiation.
        /// </summary>
        private static PhpTypeExpressionListAst? GetNamedTypeArgumentList(
            PhpNamedTypeAst named,
            IExpression? nameExpr)
        {
            if (nameExpr is TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst fromName }
                && fromName.GetAllNotNull().Any())
            {
                return fromName;
            }

            if (named.AstGrammarAddons.TryGetValue("typeName", out var addon)
                && addon is PhpTypeExpressionListAst fromAddon
                && fromAddon.GetAllNotNull().Any())
            {
                return fromAddon;
            }

            return null;
        }

        private ICheckedType ResolveNamedTypeWithArguments(
            PhpNamedTypeAst named,
            PhpTypeExpressionListAst argList,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration)
        {
            var typeName = GetExpressionText(named.Name);
            if (IsRelativeTypeName(typeName))
            {
                if (string.Equals(typeName, "static", StringComparison.OrdinalIgnoreCase))
                {
                    ReportDiagnostic(named, state, MessageCode.CheckerParameterizedStaticForbidden);
                    return CheckedTypes.Unresolved;
                }

                var relativeBase = ResolveRelativeType(typeName!, named, state, isReturnTypePosition);
                // Parameterized `self`/`parent` need the underlying class symbol, not late-bound
                // `static` (which is never parameterized).
                if (relativeBase is StaticCheckedType staticBase)
                {
                    relativeBase = staticBase.DeclaringType;
                }

                var relativeArgs = ResolveGenericTypeArgumentList(
                    argList, state, isReturnTypePosition, isUserTypeDeclaration);
                return GenericTypeArgumentValidator.ValidateInstantiation(
                    relativeBase, relativeArgs, named, state, _symbolTree, _globalScope, _diagnostics,
                    ResolveTypeExpressionCore);
            }

            var fromScope = GetResolutionScope(state);
            var baseSymbol = _symbolTree.ResolveType(named, fromScope, _diagnostics);
            if (baseSymbol is null
                && TryResolveInScopeGenericParameter(typeName, state, out var genericParamBase))
            {
                return genericParamBase;
            }

            if (baseSymbol is null)
            {
                ReportUnresolvedNamedTypeIfNeeded(
                    named, GetExpressionText(named.Name), state, isUserTypeDeclaration);
            }

            var baseType = baseSymbol is null ? CheckedTypes.Unresolved : CheckedTypes.FromSymbol(baseSymbol);

            var args = ResolveGenericTypeArgumentList(
                argList, state, isReturnTypePosition, isUserTypeDeclaration);

            if (baseSymbol is BuiltInUtilityTypeSymbol utility)
            {
                return UtilityTypeResolver.Resolve(
                    utility, args, named, state, _symbolTree, _globalScope, _diagnostics,
                    ResolveTypeExpressionCore);
            }

            return GenericTypeArgumentValidator.ValidateInstantiation(
                baseType, args, named, state, _symbolTree, _globalScope, _diagnostics,
                ResolveTypeExpressionCore);
        }

        /// <summary>
        /// Resolves generic type arguments with parameter/property position flags cleared so a
        /// nested bare <c>static</c> (e.g. <c>ReflectionClass&lt;static&gt;</c> on a property)
        /// is not rejected as if <c>static</c> itself were the declared property/parameter type.
        /// Marks <see cref="CheckerState.IsGenericTypeArgumentPosition"/> so undeclared names
        /// inside <c>callable&lt;…&gt;</c> / <c>array&lt;…&gt;</c> / user generics are diagnosed.
        /// </summary>
        private List<ICheckedType> ResolveGenericTypeArgumentList(
            PhpTypeExpressionListAst argList,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration)
        {
            var savedParam = state.IsParameterTypePosition;
            var savedProp = state.IsPropertyTypePosition;
            var savedGenericArg = state.IsGenericTypeArgumentPosition;
            state.IsParameterTypePosition = false;
            state.IsPropertyTypePosition = false;
            state.IsGenericTypeArgumentPosition = true;
            try
            {
                return argList
                    .GetAllNotNull()
                    .Select(arg => ResolveTypeExpressionCore(
                        arg, state, isReturnTypePosition, isUserTypeDeclaration))
                    .ToList();
            }
            finally
            {
                state.IsParameterTypePosition = savedParam;
                state.IsPropertyTypePosition = savedProp;
                state.IsGenericTypeArgumentPosition = savedGenericArg;
            }
        }

        /// <summary>
        /// Reports an undeclared type name spelled as a generic type argument (e.g. <c>TResult</c>
        /// inside <c>callable&lt;?TResult, …&gt;</c>). Top-level parameter/return unresolved names
        /// remain binder TYHP3019/3020 so we do not double-diagnose those sites.
        /// </summary>
        private void ReportUnresolvedNamedTypeIfNeeded(
            PhpNamedTypeAst named,
            string? typeName,
            CheckerState state,
            bool isUserTypeDeclaration)
        {
            if (!isUserTypeDeclaration || !state.IsGenericTypeArgumentPosition)
            {
                return;
            }

            var display = typeName?.TrimStart('\\');
            if (string.IsNullOrEmpty(display))
            {
                return;
            }

            var reportNode = named.Name as IBase2Ast ?? named;
            var fromScope = GetResolutionScope(state);
            CheckerHelpers.ReportErrorWithDidYouMean(
                _diagnostics,
                state,
                reportNode,
                MessageCode.BinderSymbolNotFound,
                display,
                InScopeNameCandidates.CollectTypeNames(fromScope),
                display);
        }

        private ICheckedType ResolveCompositeType(
            PhpTypeExpressionAst composite,
            CheckerState state,
            bool isReturnTypePosition,
            bool isUserTypeDeclaration)
        {
            if (composite.Types is null)
            {
                return CheckedTypes.Unresolved;
            }

            var members = composite.Types
                .GetAllNotNull()
                .Select(member => ResolveTypeExpressionCore(member, state, isReturnTypePosition, isUserTypeDeclaration))
                .ToList();

            ICheckedType result = composite.TypeKind switch
            {
                PhpTypeKind.Union => CheckedTypes.UnionTypes(members),
                PhpTypeKind.Intersection => new IntersectionCheckedType(members),
                _ => members.Count switch
                {
                    0 => CheckedTypes.Unresolved,
                    1 => members[0],
                    _ => CheckedTypes.UnionTypes(members),
                },
            };

            // PHP/Story 08: `static` is illegal as a parameter or property type. Nested uses
            // (generic args, locals, instanceof, `new` type args) are allowed — same as return.
            if (composite.IsStatic
                && (state.IsParameterTypePosition || state.IsPropertyTypePosition))
            {
                ReportDiagnostic(
                    composite,
                    state,
                    MessageCode.CheckerStaticNotReturnType);
            }

            if (composite.IsNullable)
            {
                result = new NullableCheckedType(result);
            }

            return result;
        }

        private ICheckedType ResolveRelativeType(
            string typeName,
            IBase2Ast typeAst,
            CheckerState state,
            bool isReturnTypePosition)
        {
            if (string.Equals(typeName, "static", StringComparison.OrdinalIgnoreCase))
            {
                // Reject only as a parameter/property type (PHP + Story 08). Elsewhere —
                // return types, locals, generic args (`ReflectionClass<static>`),
                // `instanceof`, etc. — keep a distinct late-bound `static` type.
                if (state.IsParameterTypePosition || state.IsPropertyTypePosition)
                {
                    ReportDiagnostic(typeAst, state, MessageCode.CheckerStaticNotReturnType);
                    return CheckedTypes.Unresolved;
                }

                var declaring = ResolveEnclosingObjectType(state, typeAst);
                return TypeComparer.IsUnresolvedType(declaring)
                    ? CheckedTypes.Unresolved
                    : new StaticCheckedType(declaring);
            }

            if (state.EnclosingObject is null)
            {
                ReportDiagnostic(typeAst, state, MessageCode.CheckerRelativeTypeOutsideClass);
                return CheckedTypes.Unresolved;
            }

            if (string.Equals(typeName, "self", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveEnclosingObjectType(state, typeAst);
            }

            if (string.Equals(typeName, "parent", StringComparison.OrdinalIgnoreCase))
            {
                if (state.EnclosingObject.ExtendsType is null)
                {
                    ReportDiagnostic(typeAst, state, MessageCode.CheckerParentWithoutParent, state.EnclosingObject.Name);
                    return CheckedTypes.Unresolved;
                }

                var parentType = ResolveTypeExpressionCore(
                    state.EnclosingObject.ExtendsType,
                    state,
                    isReturnTypePosition,
                    isUserTypeDeclaration: true);
                return parentType;
            }

            return CheckedTypes.Unresolved;
        }

        private ICheckedType ResolveEnclosingObjectType(CheckerState state, IBase2Ast typeAst)
        {
            if (state.EnclosingObjectType is not null)
            {
                return state.EnclosingObjectType;
            }

            if (state.EnclosingObject is null)
            {
                return CheckedTypes.Unresolved;
            }

            return CheckedTypes.FromSymbol(state.EnclosingObject);
        }

        private static bool IsRelativeTypeName(string? typeName) =>
            typeName is "self" or "parent" or "static"
            || string.Equals(typeName, "self", StringComparison.OrdinalIgnoreCase)
            || string.Equals(typeName, "parent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(typeName, "static", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Resolves a bare type name to an in-scope method/function or enclosing-object
        /// generic type parameter (e.g. <c>T</c> in <c>of&lt;T extends object&gt;(__ClassName&lt;T&gt;)</c>).
        /// Also consults <see cref="CheckerState.EnclosingObject"/> so a call-site rebind that
        /// set the declaring class but left <see cref="CheckerState.ObjectGenerics"/> empty
        /// (chained <c>: static</c> receivers) still finds the class parameter.
        /// </summary>
        private static bool TryResolveInScopeGenericParameter(
            string? typeName,
            CheckerState state,
            out ICheckedType type)
        {
            type = CheckedTypes.Unresolved;
            var simpleName = typeName?.TrimStart('\\');
            if (string.IsNullOrEmpty(simpleName))
            {
                return false;
            }

            GenericTypeParameterSymbol? param =
                state.FunctionGenerics.FirstOrDefault(gp =>
                    string.Equals(gp.Name, simpleName, StringComparison.Ordinal))
                ?? state.ObjectGenerics.FirstOrDefault(gp =>
                    string.Equals(gp.Name, simpleName, StringComparison.Ordinal))
                ?? state.EnclosingObject?.GenericParameters.FirstOrDefault(gp =>
                    string.Equals(gp.Name, simpleName, StringComparison.Ordinal));

            if (param is null)
            {
                return false;
            }

            type = CheckedTypes.FromSymbol(param);
            return true;
        }

        private static bool TryGetBuiltinSingleton(string name, out ICheckedType? type)
        {
            type = name.ToLowerInvariant() switch
            {
                "void" => CheckedTypes.Void,
                "never" => CheckedTypes.Never,
                "mixed" => CheckedTypes.Mixed,
                "null" => CheckedTypes.Null,
                // Preserve precise true/false literal types — do not collapse to bool.
                "true" => new LiteralCheckedType(true, new SimpleCheckedType(new BuiltInTypeSymbol("true"))),
                "false" => new LiteralCheckedType(false, new SimpleCheckedType(new BuiltInTypeSymbol("false"))),
                "bool" => CheckedTypes.Bool,
                "int" => CheckedTypes.Int,
                "float" => CheckedTypes.Float,
                "string" => CheckedTypes.String,
                _ => null,
            };
            return type is not null;
        }

        private void ReportResourceRestriction(IBase2Ast typeAst, CheckerState state)
        {
            ReportDiagnostic(typeAst, state, MessageCode.CheckerResourceNotAllowed);
        }

        private void ReportDiagnostic(IBase2Ast ast, CheckerState state, MessageCode code, params object[] args)
        {
            _diagnostics.AddErrorFromAst(
                code,
                ast,
                CheckerHelpers.ResolveDiagnosticFileName(state, ast),
                args);
        }

        private static string? GetExpressionText(IExpression? expression) =>
            expression switch
            {
                PhpNameAst name => name.ValueString,
                TokenValueAst token => token.ValueString,
                PhpVariableAst variable => CheckerHelpers.GetVariableName(variable),
                _ => expression?.ValueString,
            };
    }
}
