using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Validates generic type argument arity and constraints during type expression resolution.
    /// </summary>
    internal static class GenericTypeArgumentValidator
    {
        public static ICheckedType ValidateInstantiation(
            ICheckedType baseType,
            IReadOnlyList<ICheckedType> typeArguments,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (baseType is SimpleCheckedType { ResolvedSymbol: BuiltInUtilityTypeSymbol utility })
            {
                return UtilityTypeResolver.Resolve(
                    utility, typeArguments, reportNode, state, symbolTree, globalScope, diagnostics, resolveType);
            }

            if (IsBuiltInCallable(baseType))
            {
                return ValidateCallableArguments(typeArguments, reportNode, state, symbolTree, globalScope, diagnostics, resolveType);
            }

            if (baseType is SimpleCheckedType { ResolvedSymbol: BuiltInTypeSymbol builtIn }
                && builtIn.GenericParameterRequirements is { } requirements)
            {
                return ValidateBuiltInArguments(
                    baseType, typeArguments, requirements, reportNode, state, symbolTree, globalScope, diagnostics, resolveType);
            }

            if (baseType is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                var resolved = ResolveAndValidateUserTypeArguments(
                    obj, obj.GenericParameters, typeArguments, reportNode, state, symbolTree, globalScope,
                    diagnostics, resolveType);
                return resolved.Count == 0 ? baseType : new GenericCheckedType(baseType, resolved);
            }

            if (baseType is SimpleCheckedType { ResolvedSymbol: TypeAliasSymbol alias })
            {
                var resolved = ResolveAndValidateUserTypeArguments(
                    alias, alias.GenericParameters, typeArguments, reportNode, state, symbolTree, globalScope,
                    diagnostics, resolveType);
                return resolved.Count == 0 ? baseType : new GenericCheckedType(baseType, resolved);
            }

            return typeArguments.Count == 0 ? baseType : new GenericCheckedType(baseType, typeArguments);
        }

        /// <summary>
        /// Validates per-parameter generic constraints for built-in utility types after arity checks.
        /// </summary>
        public static void ValidateUtilityConstraints(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> typeArguments,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics)
        {
            var requirements = utility.GenericParameterRequirements;
            if (typeArguments.Count < requirements.MinArity || typeArguments.Count > requirements.MaxArity)
            {
                return;
            }

            var normalizedArgs = NormalizeArrayLikeArguments(typeArguments, requirements);
            for (var i = 0; i < normalizedArgs.Count; i++)
            {
                var arg = normalizedArgs[i];
                var isReturnPosition = requirements.UsesReturnLastConvention && i == normalizedArgs.Count - 1;
                ValidateRestrictedType(arg, isReturnPosition, reportNode, state, diagnostics);

                if (requirements.Parameters is { } specs && i < specs.Count)
                {
                    ValidateBuiltInConstraint(arg, specs[i].Constraint, reportNode, state, symbolTree, globalScope, diagnostics);
                }
            }
        }

        private static ICheckedType ValidateCallableArguments(
            IReadOnlyList<ICheckedType> typeArguments,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            var requirements = GenericParameterRequirements.Callable();
            if (!ValidateArity(typeArguments, requirements, reportNode, state, diagnostics, "callable"))
            {
                return new CallableCheckedType([], CheckedTypes.Mixed);
            }

            for (var i = 0; i < typeArguments.Count; i++)
            {
                var arg = typeArguments[i];
                var isReturnPosition = i == typeArguments.Count - 1;
                ValidateRestrictedType(arg, isReturnPosition, reportNode, state, diagnostics);
            }

            var returnType = typeArguments[^1];
            var parameterTypes = typeArguments.Take(typeArguments.Count - 1).ToList();
            return new CallableCheckedType(parameterTypes, returnType);
        }

        private static ICheckedType ValidateBuiltInArguments(
            ICheckedType baseType,
            IReadOnlyList<ICheckedType> typeArguments,
            GenericParameterRequirements requirements,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (!ValidateArity(typeArguments, requirements, reportNode, state, diagnostics, baseType.DisplayName))
            {
                return new GenericCheckedType(baseType, typeArguments);
            }

            var normalizedArgs = NormalizeArrayLikeArguments(typeArguments, requirements);
            for (var i = 0; i < normalizedArgs.Count; i++)
            {
                var arg = normalizedArgs[i];
                var isReturnPosition = requirements.UsesReturnLastConvention && i == normalizedArgs.Count - 1;
                ValidateRestrictedType(arg, isReturnPosition, reportNode, state, diagnostics);

                if (requirements.Parameters is { } specs && i < specs.Count)
                {
                    ValidateBuiltInConstraint(arg, specs[i].Constraint, reportNode, state, symbolTree, globalScope, diagnostics);
                }
            }

            return new GenericCheckedType(baseType, normalizedArgs);
        }

        /// <summary>
        /// Fills omitted trailing type arguments from parameter defaults and validates arity /
        /// constraints. Returns an empty list when the declaration should stay a bare
        /// <see cref="SimpleCheckedType"/> (not generic, or open/raw with no defaults and no args).
        /// </summary>
        internal static IReadOnlyList<ICheckedType> ResolveAndValidateUserTypeArguments(
            IBaseSymbol declaringSymbol,
            IReadOnlyList<GenericTypeParameterSymbol> genericParams,
            IReadOnlyList<ICheckedType> typeArguments,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (genericParams.Count == 0)
            {
                if (typeArguments.Count > 0)
                {
                    Report(reportNode, state, diagnostics, MessageCode.CheckerGenericArgumentCountMismatch,
                        declaringSymbol.Name, "0", typeArguments.Count.ToString());
                }

                return [];
            }

            // `\Tyhp\Expression` follows the callable return-last convention
            // (`Expression<R>`, `Expression<T, R>`, `Expression<T1, T2, R>`, …) even though
            // the runtime class is declared with two type parameters.
            if (PropertyPathSupport.IsTyhpExpressionDeclaration(declaringSymbol))
            {
                return ResolveExpressionCallableArityArguments(
                    genericParams,
                    typeArguments,
                    reportNode,
                    state,
                    symbolTree,
                    globalScope,
                    diagnostics,
                    resolveType);
            }

            var requiredCount = genericParams.Count(p => !p.HasDefault);
            if (typeArguments.Count < requiredCount || typeArguments.Count > genericParams.Count)
            {
                Report(reportNode, state, diagnostics, MessageCode.CheckerGenericArgumentCountMismatch,
                    declaringSymbol.Name, genericParams.Count.ToString(), typeArguments.Count.ToString());
                return typeArguments.Count == 0 ? [] : typeArguments;
            }

            // Bare reference to a generic with no defaults: keep the open/raw form.
            if (typeArguments.Count == 0 && requiredCount == genericParams.Count)
            {
                return [];
            }

            var resolved = new List<ICheckedType>(genericParams.Count);
            var substitutions = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);

            for (var i = 0; i < genericParams.Count; i++)
            {
                var param = genericParams[i];
                ICheckedType arg;
                if (i < typeArguments.Count)
                {
                    arg = typeArguments[i];
                }
                else
                {
                    arg = ResolveDefaultTypeArgument(
                        param, genericParams, substitutions, state, symbolTree, globalScope, resolveType);
                }

                resolved.Add(arg);
                substitutions[param.Name] = arg;

                // `void`/`never` are normally banned in non-return generic positions, but a parameter
                // whose constraint explicitly admits them (e.g. `TReturn extends void|mixed`) opts in.
                if (!ConstraintPermitsRestrictedType(arg, param, state, resolveType))
                {
                    ValidateRestrictedType(arg, isReturnPosition: false, reportNode, state, diagnostics);
                }

                ValidateUserConstraint(
                    arg, param, reportNode, state, symbolTree, globalScope, diagnostics, resolveType);
            }

            return resolved;
        }

        /// <summary>
        /// <c>Expression&lt;TArgs…, TReturn&gt;</c> uses callable arity: one or more type
        /// arguments, last is the return type, earlier arguments are parameter types.
        /// Zero arguments keep the open/raw <c>Expression</c> form.
        /// </summary>
        private static IReadOnlyList<ICheckedType> ResolveExpressionCallableArityArguments(
            IReadOnlyList<GenericTypeParameterSymbol> genericParams,
            IReadOnlyList<ICheckedType> typeArguments,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (typeArguments.Count == 0)
            {
                return [];
            }

            var tSource = genericParams.Count > 0 ? genericParams[0] : null;
            var tReturn = genericParams.Count > 1 ? genericParams[^1] : tSource;

            for (var i = 0; i < typeArguments.Count; i++)
            {
                var arg = typeArguments[i];
                var isReturnPosition = i == typeArguments.Count - 1;
                ValidateRestrictedType(arg, isReturnPosition, reportNode, state, diagnostics);

                if (isReturnPosition && tReturn is not null)
                {
                    ValidateUserConstraint(
                        arg, tReturn, reportNode, state, symbolTree, globalScope, diagnostics, resolveType);
                }
                else if (i == 0 && typeArguments.Count >= 2 && tSource is not null)
                {
                    ValidateUserConstraint(
                        arg, tSource, reportNode, state, symbolTree, globalScope, diagnostics, resolveType);
                }
            }

            return typeArguments.ToList();
        }

        private static ICheckedType ResolveDefaultTypeArgument(
            GenericTypeParameterSymbol param,
            IReadOnlyList<GenericTypeParameterSymbol> genericParams,
            Dictionary<string, ICheckedType> substitutions,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (param.DefaultType is null)
            {
                return CheckedTypes.Unresolved;
            }

            // Defaults resolve in the declaring generic scope so they can mention earlier parameters.
            var defaultState = state.Fork();
            defaultState.ObjectGenerics = genericParams;
            defaultState.FunctionGenerics = genericParams;

            var defaultType = resolveType(param.DefaultType, defaultState, false, true);
            if (substitutions.Count > 0)
            {
                defaultType = TypeComparer.ResolveGenericType(
                    defaultType, substitutions, symbolTree, globalScope);
            }

            return defaultType;
        }

        /// <summary>
        /// Declaration-time checks for generic parameter defaults: trailing-only rule, cycles, and
        /// default-vs-constraint (Story 28 / TYHP4310–4312).
        /// </summary>
        public static void ValidateGenericParameterDefaults(
            IReadOnlyList<GenericTypeParameterSymbol> genericParams,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (genericParams.Count == 0)
            {
                return;
            }

            string? lastDefaultedName = null;
            foreach (var param in genericParams)
            {
                if (param.HasDefault)
                {
                    lastDefaultedName = param.Name;
                }
                else if (lastDefaultedName is not null)
                {
                    Report(reportNode, state, diagnostics, MessageCode.CheckerGenericNonDefaultAfterDefault,
                        param.Name, lastDefaultedName);
                }
            }

            foreach (var param in genericParams)
            {
                if (param.DefaultType is null)
                {
                    continue;
                }

                if (DefaultReferencesCycle(param, genericParams, []))
                {
                    Report(reportNode, state, diagnostics, MessageCode.CheckerGenericDefaultCircularReference,
                        param.Name);
                    continue;
                }

                if (param.Constraint is null)
                {
                    continue;
                }

                var substitutions = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
                var defaultType = ResolveDefaultTypeArgument(
                    param, genericParams, substitutions, state, symbolTree, globalScope, resolveType);
                if (TypeComparer.IsUnresolvedType(defaultType))
                {
                    continue;
                }

                var constraintType = resolveType(param.Constraint, state, false, true);
                if (TypeComparer.IsUnresolvedType(constraintType))
                {
                    continue;
                }

                if (!TypeComparer.IsAssignableTo(defaultType, constraintType, symbolTree, globalScope)
                    && !TypeComparer.IsSubtypeOf(defaultType, constraintType, symbolTree, globalScope))
                {
                    Report(
                        reportNode, state, diagnostics,
                        MessageCode.CheckerGenericDefaultDoesNotSatisfyConstraint,
                        defaultType.DisplayName, constraintType.DisplayName, param.Name);
                }
            }
        }

        private static bool DefaultReferencesCycle(
            GenericTypeParameterSymbol param,
            IReadOnlyList<GenericTypeParameterSymbol> allParams,
            HashSet<string> visiting)
        {
            if (param.DefaultType is null)
            {
                return false;
            }

            if (!visiting.Add(param.Name))
            {
                return true;
            }

            foreach (var referenced in CollectReferencedParameterNames(param.DefaultType))
            {
                var other = allParams.FirstOrDefault(p =>
                    p.Name.Equals(referenced, StringComparison.Ordinal));
                if (other is null)
                {
                    continue;
                }

                if (other.Name.Equals(param.Name, StringComparison.Ordinal)
                    || DefaultReferencesCycle(other, allParams, visiting))
                {
                    visiting.Remove(param.Name);
                    return true;
                }
            }

            visiting.Remove(param.Name);
            return false;
        }

        private static IEnumerable<string> CollectReferencedParameterNames(ITypeExpression typeExpr)
        {
            switch (typeExpr)
            {
                case PhpNamedTypeAst { Name: PhpNameAst nameAst }
                    when !string.IsNullOrEmpty(nameAst.ValueString):
                    yield return nameAst.ValueString;
                    break;
                case PhpNamedTypeAst { Name: TyhpGenericIdentifierAst genericId }
                    when !string.IsNullOrEmpty(genericId.ValueString):
                    yield return genericId.ValueString;
                    break;
                case PhpTypeExpressionAst { Types: { } members }:
                    foreach (var member in members.GetAllNotNull())
                    {
                        foreach (var name in CollectReferencedParameterNames(member))
                        {
                            yield return name;
                        }
                    }

                    break;
            }
        }

        private static bool ConstraintPermitsRestrictedType(
            ICheckedType arg,
            GenericTypeParameterSymbol param,
            CheckerState state,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (param.Constraint is null)
            {
                return false;
            }

            var constraintType = resolveType(param.Constraint, state, false, true);
            return ConstraintAllowsVoidOrNever(constraintType, arg);
        }

        private static bool ValidateArity(
            IReadOnlyList<ICheckedType> typeArguments,
            GenericParameterRequirements requirements,
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics,
            string typeName)
        {
            var count = typeArguments.Count;
            if (count < requirements.MinArity || count > requirements.MaxArity)
            {
                var expected = requirements.MinArity == requirements.MaxArity
                    ? requirements.MinArity.ToString()
                    : $"{requirements.MinArity}-{requirements.MaxArity}";
                Report(reportNode, state, diagnostics, MessageCode.CheckerGenericArgumentCountMismatch,
                    typeName, expected, count.ToString());
                return false;
            }

            return true;
        }

        private static List<ICheckedType> NormalizeArrayLikeArguments(
            IReadOnlyList<ICheckedType> typeArguments,
            GenericParameterRequirements requirements)
        {
            if (!requirements.SupportsSingleArgumentShorthand || typeArguments.Count != 1)
            {
                return typeArguments.ToList();
            }

            return [CheckedTypes.UnionTypes(CheckedTypes.Int, CheckedTypes.String), typeArguments[0]];
        }

        private static void ValidateRestrictedType(
            ICheckedType arg,
            bool isReturnPosition,
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (isReturnPosition)
            {
                return;
            }

            if (IsVoidType(arg))
            {
                Report(reportNode, state, diagnostics, MessageCode.CheckerVoidInNonReturnPosition);
            }
            else if (IsNeverType(arg))
            {
                Report(reportNode, state, diagnostics, MessageCode.CheckerNeverInNonReturnPosition);
            }
        }

        private static bool IsVoidType(ICheckedType type) =>
            type.IsVoid
            || type.Kind == CheckedTypeKind.Void
            || Rules.CheckerHelpers.IsBuiltInName(type, "void");

        private static bool IsNeverType(ICheckedType type) =>
            type.IsNever
            || type.Kind == CheckedTypeKind.Never
            || Rules.CheckerHelpers.IsBuiltInName(type, "never");

        private static void ValidateBuiltInConstraint(
            ICheckedType arg,
            BuiltInGenericParameterConstraint constraint,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics)
        {
            var satisfied = constraint switch
            {
                BuiltInGenericParameterConstraint.None or BuiltInGenericParameterConstraint.AnyType => true,
                BuiltInGenericParameterConstraint.KeyIntOrString =>
                    IsIntOrStringKeyType(arg),
                BuiltInGenericParameterConstraint.ClassInterfaceOrStruct =>
                    IsClassInterfaceOrStruct(arg, symbolTree, globalScope),
                BuiltInGenericParameterConstraint.ClassOrStruct =>
                    IsClassOrStruct(arg, symbolTree, globalScope),
                BuiltInGenericParameterConstraint.UnionType =>
                    arg is UnionCheckedType,
                BuiltInGenericParameterConstraint.Callable =>
                    SatisfiesCallableConstraint(arg),
                BuiltInGenericParameterConstraint.StringLiteralUnion =>
                    IsStringLiteralUnion(arg),
                BuiltInGenericParameterConstraint.ReturnTypeRestricted => true,
                BuiltInGenericParameterConstraint.EnumOnly =>
                    Rules.CheckerHelpers.TryGetObjectDeclaration(arg) is { ObjectKind: PhpTypeDeclType.Enum },
                BuiltInGenericParameterConstraint.Object =>
                    IsObjectConstraint(arg, symbolTree, globalScope),
                _ => true,
            };

            if (!satisfied)
            {
                Report(reportNode, state, diagnostics, MessageCode.CheckerGenericConstraintNotSatisfied,
                    arg.DisplayName, constraint.ToString());
            }
        }

        private static void ValidateUserConstraint(
            ICheckedType arg,
            GenericTypeParameterSymbol param,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (param.Constraint is null)
            {
                return;
            }

            // Unbound / error-recovery type arguments must not cascade constraint failures
            // (Story 11 audit #5 — same policy as KeyIntOrString for unresolved).
            if (arg.Kind == CheckedTypeKind.Unresolved)
            {
                return;
            }

            var constraintType = resolveType(param.Constraint, state, false, true);
            if (ConstraintAllowsVoidOrNever(constraintType, arg))
            {
                return;
            }

            if (!TypeComparer.IsAssignableTo(arg, constraintType, symbolTree, globalScope)
                && !TypeComparer.IsSubtypeOf(arg, constraintType, symbolTree, globalScope))
            {
                Report(reportNode, state, diagnostics, MessageCode.CheckerGenericConstraintNotSatisfied,
                    arg.DisplayName, constraintType.DisplayName);
            }
        }

        private static bool ConstraintAllowsVoidOrNever(ICheckedType constraint, ICheckedType arg)
        {
            // Use the robust void/never detection (which also recognizes the built-in-named forms,
            // e.g. a SimpleCheckedType named "void") rather than only the IsVoid/IsNever flags.
            var argVoid = IsVoidType(arg);
            var argNever = IsNeverType(arg);
            if (!argVoid && !argNever)
            {
                return false;
            }

            if (constraint is UnionCheckedType union)
            {
                return union.Members.Any(member =>
                    (argVoid && IsVoidType(member)) || (argNever && IsNeverType(member)));
            }

            return (argVoid && IsVoidType(constraint)) || (argNever && IsNeverType(constraint));
        }

        // `\Closure<...>` is the nominal generic form of a callable (e.g. `\Closure<TValue, void>`),
        // so it shares the callable arity/return-last convention rather than being a 0-generic class.
        private static bool IsBuiltInCallable(ICheckedType type) =>
            type is SimpleCheckedType { ResolvedSymbol.Name: "callable" or "Closure" };

        /// <summary>
        /// Shared <c>Callable</c> constraint used by utility types (<c>\Tyhp\ReturnType</c> /
        /// <c>\Tyhp\Parameters</c>, <c>__CallableReturnType</c>,
        /// <c>__CallableParametersRest</c>, and peers) and other built-ins.
        /// Accepts structural callables, bare <c>callable</c>/<c>\Closure</c>, generic forms with
        /// at least one type argument (return-last), nullable wrappers of those, unions of
        /// callables, intersections that include a callable, unresolved recovery types, and
        /// in-scope generic type parameters (their <c>extends callable</c> bound is checked at
        /// declaration). Rejects empty <c>callable&lt;&gt;</c> /
        /// <c>\Closure&lt;&gt;</c> so those shapes are not silently accepted after utility
        /// resolvers stop emitting ad-hoc <c>CheckerUtilityTypeInvalidArgument</c>.
        /// </summary>
        internal static bool SatisfiesCallableConstraint(ICheckedType arg)
        {
            while (arg is NullableCheckedType nullable)
            {
                arg = nullable.InnerType;
            }

            if (arg.Kind == CheckedTypeKind.Callable || arg.Kind == CheckedTypeKind.Unresolved)
            {
                return true;
            }

            // Unbound type parameters (e.g. `TCallable extends callable`) must not fail the
            // constraint at the generic declaration; instantiation substitutes a concrete callable.
            if (arg is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol })
            {
                return true;
            }

            // Check GenericCheckedType before bare-name matching: empty type-argument lists still
            // display as "callable" / "Closure", which would otherwise look like a bare type.
            if (arg is GenericCheckedType generic)
            {
                return IsBareCallableOrClosureName(generic.BaseType)
                    && generic.TypeArguments.Count > 0;
            }

            if (arg is UnionCheckedType union)
            {
                var sawCallable = false;
                foreach (var member in union.Members)
                {
                    if (TypeComparer.IsNullLiteral(member) || TypeComparer.IsBuiltInName(member, "null"))
                    {
                        continue;
                    }

                    if (!SatisfiesCallableConstraint(member))
                    {
                        return false;
                    }

                    sawCallable = true;
                }

                return sawCallable;
            }

            if (arg is IntersectionCheckedType intersection)
            {
                return intersection.Members.Any(SatisfiesCallableConstraint);
            }

            return IsBareCallableOrClosureName(arg);
        }

        private static bool IsBareCallableOrClosureName(ICheckedType type) =>
            Rules.CheckerHelpers.IsBuiltInName(type, "callable")
            || Rules.CheckerHelpers.IsBuiltInName(type, "Closure")
            || type is SimpleCheckedType { ResolvedSymbol.Name: "callable" or "Closure" };

        private static bool IsIntOrStringKeyType(ICheckedType type) =>
            // Unresolved is the error-recovery / unbound-inference marker (Story 11 audit #5):
            // when a tyhpdef generic like `array_values<TKey extends int|string, …>` cannot yet
            // infer `TKey` from arguments, do not cascade TYHP4035 on top of the missing inference.
            // Same allowance as <see cref="IsObjectConstraint"/> for unresolved / in-scope params.
            type.Kind == CheckedTypeKind.Unresolved
            || type is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol }
            || Rules.CheckerHelpers.IsBuiltInName(type, "int")
            || Rules.CheckerHelpers.IsBuiltInName(type, "string")
            || (type is UnionCheckedType union
                && union.Members.All(m =>
                    m.Kind == CheckedTypeKind.Unresolved
                    || m is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol }
                    || Rules.CheckerHelpers.IsBuiltInName(m, "int")
                    || Rules.CheckerHelpers.IsBuiltInName(m, "string")));

        private static bool IsClassInterfaceOrStruct(ICheckedType type, SymbolTree symbolTree, GlobalScope globalScope) =>
            IsClassOrStruct(type, symbolTree, globalScope)
            || Rules.CheckerHelpers.TryGetObjectDeclaration(type) is { ObjectKind: PhpTypeDeclType.Interface };

        private static bool IsClassOrStruct(ICheckedType type, SymbolTree symbolTree, GlobalScope globalScope)
        {
            var obj = Rules.CheckerHelpers.TryGetObjectDeclaration(type);
            return obj is not null && (obj.IsStruct || obj.ObjectKind == PhpTypeDeclType.Class);
        }

        /// <summary>
        /// <c>T extends object</c>: built-in <c>object</c>, classes, interfaces, enums, structs,
        /// and in-scope generic type parameters (their own bounds are checked at declaration).
        /// </summary>
        private static bool IsObjectConstraint(ICheckedType type, SymbolTree symbolTree, GlobalScope globalScope) =>
            type is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol }
            || type.Kind == CheckedTypeKind.Unresolved
            || Rules.CheckerHelpers.IsBuiltInName(type, "object")
            || IsClassInterfaceOrStruct(type, symbolTree, globalScope)
            || Rules.CheckerHelpers.TryGetObjectDeclaration(type) is
            {
                ObjectKind: PhpTypeDeclType.Enum or PhpTypeDeclType.Trait,
            };

        private static bool IsStringLiteralUnion(ICheckedType type) =>
            type is LiteralCheckedType { UnderlyingType: var underlying }
                && Rules.CheckerHelpers.IsBuiltInName(underlying, "string")
            || type is UnionCheckedType union && union.Members.All(IsStringLiteralUnion);

        private static void Report(
            IBase2Ast node,
            CheckerState state,
            DiagnosticBag diagnostics,
            MessageCode code,
            params object[] args)
        {
            diagnostics.AddErrorFromAst(
                code,
                node,
                state.CurrentFileName ?? node.OwningFile?.FileName ?? string.Empty,
                args);
        }
    }
}
