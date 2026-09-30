using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Validates generic type argument arity and constraints during type expression resolution.
    /// </summary>
    internal static class GenericTypeArgumentValidator
    {
        // Recursion guard for the lazy `ResolvedConstraint` fill in `ValidateUserConstraint`
        // (thread-static: checking can run on multiple threads across independent compilations,
        // e.g. parallel test collections, and must not share this state across them).
        [ThreadStatic]
        private static HashSet<GenericTypeParameterSymbol>? _resolvingConstraints;

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
            if (!IsBuiltInCallable(baseType)
                && typeArguments.Any(static arg => arg is CallableArityWildcardCheckedType))
            {
                Report(reportNode, state, diagnostics, MessageCode.CheckerCallableEllipsisNotAllowed);
                return CheckedTypes.Unresolved;
            }

            if (!IsBuiltInCallable(baseType)
                && typeArguments.Any(static arg => arg is HomogeneousVariadicCheckedType))
            {
                Report(reportNode, state, diagnostics, MessageCode.CheckerCallablePostfixEllipsisNotAllowed);
                return CheckedTypes.Unresolved;
            }

            if (baseType is SimpleCheckedType { ResolvedSymbol: BuiltInUtilityTypeSymbol utility })
            {
                return UtilityTypeResolver.Resolve(
                    utility, typeArguments, reportNode, state, symbolTree, globalScope, diagnostics, resolveType);
            }

            if (IsBuiltInCallable(baseType))
            {
                if (typeArguments.Count > 0)
                {
                    Report(
                        reportNode,
                        state,
                        diagnostics,
                        MessageCode.CheckerGenericArgumentCountMismatch,
                        "callable",
                        "0",
                        typeArguments.Count.ToString());
                }

                return baseType;
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

            if (baseType is SimpleCheckedType { ResolvedSymbol: ObjectTypeAliasSymbol objectAlias })
            {
                var resolved = ResolveAndValidateUserTypeArguments(
                    objectAlias, objectAlias.GenericParameters, typeArguments, reportNode, state, symbolTree, globalScope,
                    diagnostics, resolveType);
                return resolved.Count == 0 ? baseType : new GenericCheckedType(baseType, resolved);
            }

            return typeArguments.Count == 0 ? baseType : new GenericCheckedType(baseType, typeArguments);
        }

        /// <summary>
        /// After argument-driven inference, check each inferred type argument against its
        /// <c>extends</c> bound — the same <see cref="ValidateUserConstraint"/> path explicit
        /// <c>foo&lt;Bad&gt;()</c> already uses.
        /// </summary>
        internal static void ValidateInferredBindings(
            IReadOnlyDictionary<GenericTypeParameterSymbol, ICheckedType> bindings,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            IReadOnlyList<ParameterInfo>? calleeParameters = null)
        {
            if (bindings.Count == 0 || genericParameters.Count == 0)
            {
                return;
            }

            var constraintState = state.Fork();
            constraintState.ObjectGenerics = genericParameters;
            constraintState.FunctionGenerics = state.FunctionGenerics.Count == 0
                ? genericParameters
                : [.. genericParameters, .. state.FunctionGenerics];

            var substitutions = new Dictionary<string, ICheckedType>(StringComparer.Ordinal);
            foreach (var param in genericParameters)
            {
                if (bindings.TryGetValue(param, out var bound))
                {
                    substitutions[param.Name] = bound;
                }
            }

            foreach (var param in genericParameters)
            {
                if (!bindings.TryGetValue(param, out var arg))
                {
                    continue;
                }

                if (IsPhpCallableEncoding(arg) && ConstraintAstIsBareCallable(param.Constraint))
                {
                    continue;
                }

                ValidateUserConstraint(
                    arg, param, reportNode, constraintState, symbolTree, globalScope, diagnostics, resolveType,
                    substitutions, calleeParameters);
            }
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
                ValidateRestrictedType(arg, isReturnPosition, reportNode, state, diagnostics, allowNever: true);

                if (requirements.Parameters is { } specs && i < specs.Count)
                {
                    ValidateBuiltInConstraint(arg, specs[i].Constraint, reportNode, state, symbolTree, globalScope, diagnostics);
                }
            }
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
                ValidateRestrictedType(arg, isReturnPosition, reportNode, state, diagnostics, allowNever: true);

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

            // `\Closure`'s `TCallableShape` has no default (Story 21.6 Decision 3), but a fully
            // bare `\Closure` (no type arguments at all) must still stay open/gradual rather than
            // reporting a missing-required-argument error. This is a carve-out for that one
            // built-in, not a general "required-before-defaults" policy — see
            // `Check_MissingRequiredBeforeDefault_StillReportsArity` for the general case, which
            // still reports arity errors for ordinary user generics shaped the same way.
            if (typeArguments.Count == 0 && CallableArityFacetBuilder.IsClosureDeclaration(declaringSymbol))
            {
                return [];
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

            // Constraints resolve in the declaring generic scope, same as defaults just below —
            // a sibling reference like Story 21.6's `TScope extends __ClosureScope<TThis>` needs
            // `TThis` visible as an in-scope generic parameter, not the call site's own
            // class/function generics (which usually do not share that name at all).
            var constraintState = state.Fork();
            constraintState.ObjectGenerics = genericParams;
            // A type *argument* can itself be a foreign generic parameter from a completely
            // different scope — e.g. `Closure::bindTo`'s own `TNewScope extends
            // __ClosureScope<TNewThis>` when its return type instantiates `\Closure<TCallableShape,
            // TNewThis, TNewScope>`. `genericParams` here is only the *declaring* symbol's list
            // (`\Closure`'s `[TCallableShape, TThis, TScope]`), which does not contain `TNewThis`.
            // `ValidateUserConstraint`'s lazy `ResolvedConstraint` fill below resolves that
            // argument's own constraint using this state, so append the caller's incoming
            // `FunctionGenerics` (already in scope when these type arguments were resolved,
            // e.g. `bindTo`'s method generics) as a fallback rather than discarding them —
            // `genericParams` still wins on a name clash since it is checked first.
            constraintState.FunctionGenerics = state.FunctionGenerics.Count == 0
                ? genericParams
                : [.. genericParams, .. state.FunctionGenerics];

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
                if (!ConstraintPermitsRestrictedType(
                        arg, param, constraintState, resolveType, substitutions, symbolTree, globalScope))
                {
                    ValidateRestrictedType(arg, isReturnPosition: false, reportNode, state, diagnostics, allowNever: true);
                }

                ValidateUserConstraint(
                    arg, param, reportNode, constraintState, symbolTree, globalScope, diagnostics, resolveType,
                    substitutions);
            }

            if (IsPhpArrayAccess(declaringSymbol) && resolved.Count > 0
                && GenericInheritanceBindings.IsIllegalArrayAccessKeyType(resolved[0]))
            {
                Report(
                    reportNode,
                    state,
                    diagnostics,
                    MessageCode.CheckerArrayAccessKeyNotOffset,
                    resolved[0].DisplayName);
            }

            if (CallableArityFacetBuilder.IsClosureDeclaration(declaringSymbol)
                && resolved.Count >= 3
                && ClosureBindSupport.IsStaticScopeSentinel(resolved[2]))
            {
                Report(
                    reportNode,
                    state,
                    diagnostics,
                    MessageCode.CheckerClosureStaticScopeStored);
            }

            return resolved;
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
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            IReadOnlyDictionary<string, ICheckedType>? substitutions,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (param.Constraint is null)
            {
                return false;
            }

            var constraintType = resolveType(param.Constraint, state, false, true);

            // Inspect the declared bound *before* sibling substitution. `ResolveGenericType`
            // rebuilds unions through `UnionTypesCore`, which collapses `void|mixed` /
            // `never|mixed` to `mixed` and would drop the void/never member that
            // `ConstraintAllowsVoidOrNever` looks for (`Promise<void>` with
            // `TReturn extends void|mixed`).
            if (ConstraintAllowsVoidOrNever(constraintType, arg))
            {
                return true;
            }

            if (substitutions is { Count: > 0 } knownArgs)
            {
                constraintType = TypeComparer.ResolveGenericType(
                    constraintType, new Dictionary<string, ICheckedType>(knownArgs, StringComparer.Ordinal),
                    symbolTree, globalScope);
                return ConstraintAllowsVoidOrNever(constraintType, arg);
            }

            return false;
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

        /// <summary>
        /// <c>void</c> outside a return position, and <c>never</c> on a callable-shape
        /// parameter. Generic type arguments accept <c>never</c> (the bottom type:
        /// <c>PromiseInterface&lt;never&gt;</c>, <c>array&lt;never&gt;</c>).
        /// <c>callable(…): R</c> shape parameters
        /// (<see cref="Tyhp.TyhpLang.Checker.TypeInferrer.ResolveCallableShape"/>) keep both
        /// bans so a shape parameter is exactly as restricted as the old
        /// <c>callable(void): R</c> generic-argument spelling was.
        /// </summary>
        internal static void ValidateRestrictedType(
            ICheckedType arg,
            bool isReturnPosition,
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics,
            bool allowNever = false)
        {
            if (isReturnPosition)
            {
                return;
            }

            if (IsVoidType(arg))
            {
                Report(reportNode, state, diagnostics, MessageCode.CheckerVoidInNonReturnPosition);
            }
            else if (!allowNever && IsNeverType(arg))
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
                BuiltInGenericParameterConstraint.NonNegativeIntLiteral =>
                    ParameterPack.TryReadNonNegativeInt(arg, out _),
                _ => true,
            };

            if (!satisfied)
            {
                if (constraint == BuiltInGenericParameterConstraint.NonNegativeIntLiteral)
                {
                    Report(reportNode, state, diagnostics, MessageCode.CheckerCallableSliceIndexNotIntLiteral,
                        arg.DisplayName);
                    return;
                }

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
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType,
            IReadOnlyDictionary<string, ICheckedType>? substitutions = null,
            IReadOnlyList<ParameterInfo>? calleeParameters = null)
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

            // Overlay `fromCallable` returns
            // `\Closure<C, __CallableThis<C>, __CallableScope<C>>`. Those utilities inhabit
            // TThis / TScope by definition (Story 21.6 Decision 7) and stay deferred while C
            // is still an open method generic. Checking them against `__ClosureThis` /
            // `__ClosureScope<TThis>` here would 4035 every fromCallable call site.
            if (IsCallableThisOrScopeUtility(arg))
            {
                return;
            }

            // The argument can be `param` itself — e.g. `bindTo`'s "keep old scope" overload
            // returns `\Closure<TCallableShape, TNewThis, TScope>`, passing Closure's own
            // `TScope` back as its own third type argument. A type parameter trivially
            // satisfies its own declared constraint by definition, so short-circuit before the
            // checks below: `constraintType` few lines down is `param.Constraint` with
            // `substitutions` applied (e.g. `TThis` → `TNewThis` for this same instantiation),
            // but the fallback fill just below resolves `typeParam.Constraint` **without** that
            // substitution — comparing `TScope`'s unsubstituted bound (`__ClosureScope<TThis>`)
            // against the substituted `__ClosureScope<TNewThis>` would spuriously fail since
            // `TThis` and `TNewThis` are unrelated symbols to the type comparer.
            if (arg is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol sameParam }
                && ReferenceEquals(sameParam, param))
            {
                return;
            }

            // Tyhpdef / not-yet-visited declarations never run DeclarationRule, so
            // `ResolvedConstraint` stays null. Fill it from the constraint AST before asking
            // whether `TIn extends object` satisfies `WeakReference<T extends object>`. Do not
            // cache an `Unresolved` result: `ResolvedConstraint` is read elsewhere (TypeComparer
            // subtyping/assignability, CallableSignatureReflection, ControlFlowRule) as ground
            // truth for this symbol for the rest of the compilation, and `Unresolved` acts as a
            // wildcard there — permanently caching it would make the type parameter look like a
            // subtype of anything and could hide a real constraint failure on a later call site
            // that shares the same symbol. The `_resolvingConstraints` guard mirrors
            // `GenericConstraintResolver.EnsureResolved`'s cycle protection: a self-referential
            // bound (e.g. a future `create<TIn extends Box<TIn>>`) would otherwise recurse back
            // into this same fill for the same symbol before the outer call returns.
            if (arg is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol typeParam }
                && typeParam.ResolvedConstraint is null
                && typeParam.Constraint is not null)
            {
                var visiting = _resolvingConstraints ??= [];
                if (!visiting.Add(typeParam))
                {
                    // Cyclic constraint (e.g. a hypothetical `TIn extends Box<TIn>`): treat the
                    // bound as not-yet-known here rather than recursing again, same policy as an
                    // unresolved constraint below.
                    return;
                }

                try
                {
                    var resolvedTypeParamConstraint = resolveType(typeParam.Constraint, state, false, true);
                    if (TypeComparer.IsUnresolvedType(resolvedTypeParamConstraint))
                    {
                        return;
                    }

                    typeParam.ResolvedConstraint = resolvedTypeParamConstraint;
                }
                finally
                {
                    visiting.Remove(typeParam);
                }
            }

            var constraintType = resolveType(param.Constraint, state, false, true);

            // A constraint mentioning an earlier sibling parameter (e.g. Story 21.6's
            // `TScope extends __ClosureScope<TThis>`) resolves that sibling as its own open
            // `GenericTypeParameterSymbol` (see the `constraintState` fork above `param.Constraint`
            // is resolved with), not the concrete argument this instantiation already chose for it
            // (explicit or defaulted). Substitute those already-resolved siblings in before
            // comparing, the same way `ResolveDefaultTypeArgument` substitutes them into the
            // default it produces — otherwise a defaulted `TScope` (which is itself substituted)
            // never satisfies its own still-open-`TThis` constraint.
            if (substitutions is { Count: > 0 } knownArgs)
            {
                constraintType = TypeComparer.ResolveGenericType(
                    constraintType, new Dictionary<string, ICheckedType>(knownArgs, StringComparer.Ordinal),
                    symbolTree, globalScope);
            }

            // `resolveType` here is `ResolveTypeExpressionCore`, which does not expand aliases.
            // Bounds like `__ClosureThis` (`object|null`) and `__ClosureScope<TThis>` must be
            // expanded after sibling substitution so `\Closure<C, Host>` can satisfy them.
            // Expand the argument as well: a defaulted TScope is still `__ClosureScope<Host>`
            // until expansion, which would otherwise fail against the expanded union.
            constraintType = TypeComparer.ExpandTypeAliases(
                constraintType,
                symbolTree,
                globalScope,
                (ast, alias) => resolveType(ast, CheckerHelpers.WithAliasBodyContext(state, alias), false, true));
            arg = TypeComparer.ExpandTypeAliases(
                arg,
                symbolTree,
                globalScope,
                (ast, alias) => resolveType(ast, CheckerHelpers.WithAliasBodyContext(state, alias), false, true));

            if (ConstraintAllowsVoidOrNever(constraintType, arg))
            {
                return;
            }

            // PHP callable encodings (`['Class', 'method']`, `'Class::method'`, function-name
            // strings) are not assignable to bare `callable` (most arrays/strings are not
            // callables), but `fromCallable` and other `T extends callable` parameters accept
            // them so inference can recover a Closure. Typed facets (`callable(...): bool`)
            // still reject encodings that have no known return.
            if (IsBareCallableName(constraintType) && IsPhpCallableEncoding(arg))
            {
                return;
            }

            if (!TypeComparer.IsAssignableTo(arg, constraintType, symbolTree, globalScope)
                && !TypeComparer.IsSubtypeOf(arg, constraintType, symbolTree, globalScope)
                && !SatisfiesHomogeneousVariadicExtends(
                    arg, constraintType, param, calleeParameters, symbolTree, globalScope))
            {
                if (CheckerHelpers.TryReportNewConstraintFailure(
                        diagnostics,
                        state,
                        reportNode,
                        arg,
                        constraintType,
                        symbolTree,
                        globalScope))
                {
                    return;
                }

                Report(reportNode, state, diagnostics, MessageCode.CheckerGenericConstraintNotSatisfied,
                    arg.DisplayName, constraintType.DisplayName);
            }
        }

        /// <summary>
        /// Homogeneous <c>T...</c> in an <c>extends</c> bound: without Rest/Slice feeding
        /// arguments, the argument must be a PHP variadic after the prefix. With Rest/Slice,
        /// every source parameter after the prefix must accept the element type (invocability).
        /// </summary>
        private static bool SatisfiesHomogeneousVariadicExtends(
            ICheckedType arg,
            ICheckedType constraintType,
            GenericTypeParameterSymbol param,
            IReadOnlyList<ParameterInfo>? calleeParameters,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (!TryAsHomogeneousVariadicBound(constraintType, out var bound)
                || bound is null)
            {
                return false;
            }

            var restOrSliceFeeds = ParameterPack.TypeParamHasRestOrSliceFeed(param, calleeParameters);
            var facets = CallableArityFacetBuilder.GetCallableFacets(arg);
            if (facets.Count == 0)
            {
                return false;
            }

            foreach (var facet in facets)
            {
                if (facet.IsAnyArity)
                {
                    continue;
                }

                if (restOrSliceFeeds)
                {
                    if (FacetInvocableWithHomogeneousBound(facet, bound, symbolTree, globalScope))
                    {
                        return true;
                    }
                }
                else if (FacetIsPhpVariadicMatchingBound(facet, bound, symbolTree, globalScope))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Exposed to <see cref="TypeInferrer"/> so argument-driven inference can bind a sibling
        /// generic parameter that appears only as the return slot of another parameter's
        /// homogeneous <c>T...</c> <c>extends</c> bound (e.g. <c>int_map</c>'s bare <c>TReturn</c>
        /// in <c>TCallableShape extends callable(int ...): TReturn</c>), once that sibling is
        /// itself bound to a concrete callable.
        /// </summary>
        internal static bool TryAsHomogeneousVariadicBound(
            ICheckedType constraintType,
            out CallableCheckedType? bound)
        {
            bound = null;
            while (constraintType is NullableCheckedType nullable)
            {
                constraintType = nullable.InnerType;
            }

            if (constraintType is CallableCheckedType { LastParameterIsVariadic: true, IsAnyArity: false } direct)
            {
                bound = direct;
                return true;
            }

            var facets = CallableArityFacetBuilder.GetCallableFacets(constraintType);
            foreach (var facet in facets)
            {
                if (facet is { LastParameterIsVariadic: true, IsAnyArity: false })
                {
                    bound = facet;
                    return true;
                }
            }

            return false;
        }

        private static bool FacetIsPhpVariadicMatchingBound(
            CallableCheckedType source,
            CallableCheckedType bound,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (!source.LastParameterIsVariadic
                || source.ParameterTypes.Count != bound.ParameterTypes.Count)
            {
                return false;
            }

            return TypeComparer.IsAssignableTo(source, bound, symbolTree, globalScope)
                || TypeComparer.IsSubtypeOf(source, bound, symbolTree, globalScope);
        }

        private static bool FacetInvocableWithHomogeneousBound(
            CallableCheckedType source,
            CallableCheckedType bound,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (!TypeComparer.IsAssignableTo(source.ReturnType, bound.ReturnType, symbolTree, globalScope)
                && !TypeComparer.IsSubtypeOf(source.ReturnType, bound.ReturnType, symbolTree, globalScope))
            {
                return false;
            }

            var prefixCount = bound.ParameterTypes.Count - 1;
            if (prefixCount < 0)
            {
                return false;
            }

            if (source.ParameterTypes.Count < prefixCount)
            {
                return false;
            }

            for (var i = 0; i < prefixCount; i++)
            {
                if (!TypeComparer.IsAssignableTo(
                        bound.ParameterTypes[i], source.ParameterTypes[i], symbolTree, globalScope))
                {
                    return false;
                }
            }

            var element = bound.ParameterTypes[^1];
            for (var i = prefixCount; i < source.ParameterTypes.Count; i++)
            {
                if (!TypeComparer.IsAssignableTo(element, source.ParameterTypes[i], symbolTree, globalScope))
                {
                    return false;
                }
            }

            return true;
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

            return ConstraintBoundMentionsRestricted(constraint, argVoid, argNever);
        }

        /// <summary>
        /// True when <paramref name="constraint"/> is void/never (or a union/nullable wrapping
        /// one). Does not treat <c>mixed</c> as opt-in — <c>T extends mixed</c> still rejects
        /// <c>Foo&lt;void&gt;</c>. Nested unions are walked so a bound that has not been
        /// flattened still counts.
        /// </summary>
        private static bool ConstraintBoundMentionsRestricted(
            ICheckedType constraint,
            bool wantVoid,
            bool wantNever)
        {
            while (constraint is NullableCheckedType nullable)
            {
                constraint = nullable.InnerType;
            }

            if (wantVoid && IsVoidType(constraint))
            {
                return true;
            }

            if (wantNever && IsNeverType(constraint))
            {
                return true;
            }

            if (constraint is UnionCheckedType union)
            {
                return union.Members.Any(member =>
                    ConstraintBoundMentionsRestricted(member, wantVoid, wantNever));
            }

            return false;
        }

        // Bare `callable` never takes `<>` arguments; signatures are `callable(…): R` shapes.
        // `\Closure` is a class; its type arguments go through `ObjectDeclarationSymbol`
        // generics (`TCallableShape`, …).
        private static bool IsBuiltInCallable(ICheckedType type) =>
            type is SimpleCheckedType { ResolvedSymbol.Name: "callable" };

        private static bool IsCallableThisOrScopeUtility(ICheckedType arg) =>
            SymbolNameTypeHelper.TryGetUtilitySymbol(arg, out var utility)
            && utility.Behavior is UtilityBehavior.CallableThis or UtilityBehavior.CallableScope;

        /// <summary>
        /// Array / string values PHP accepts as callables at runtime. Not assignable to
        /// <c>callable</c> in general (most arrays and strings are not callables).
        /// </summary>
        private static bool IsPhpCallableEncoding(ICheckedType arg)
        {
            while (arg is NullableCheckedType nullable)
            {
                arg = nullable.InnerType;
            }

            if (arg is LiteralCheckedType { Value: string })
            {
                return true;
            }

            if (SymbolNameTypeHelper.TryGetUtilitySymbol(arg, out var utility)
                && utility.Behavior == UtilityBehavior.FunctionName)
            {
                return true;
            }

            var display = arg.DisplayName;
            if (display.StartsWith("array", StringComparison.OrdinalIgnoreCase)
                || display.StartsWith("string", StringComparison.OrdinalIgnoreCase)
                || display.StartsWith('\'')
                || display.StartsWith('"'))
            {
                return true;
            }

            var unwrapped = arg is GenericCheckedType generic ? generic.BaseType : arg;
            return Rules.CheckerHelpers.IsBuiltInName(unwrapped, "array")
                || Rules.CheckerHelpers.IsBuiltInName(unwrapped, "string");
        }

        private static bool ConstraintAstIsBareCallable(ITypeExpression? constraint)
        {
            if (constraint is null)
            {
                return false;
            }

            if (TyhpCallableShapeAst.Find(constraint) is not null)
            {
                return false;
            }

            if (constraint is PhpBuiltinTypeAst builtin
                && string.Equals(builtin.Identifier, "callable", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (constraint is PhpNamedTypeAst named)
            {
                if (named.Name is TyhpGenericIdentifierAst)
                {
                    return false;
                }

                var spelling = named.Name?.ValueString ?? named.Name?.Identifier ?? named.Identifier;
                spelling = spelling.TrimStart('\\');
                return string.Equals(spelling, "callable", StringComparison.OrdinalIgnoreCase);
            }

            var text = (constraint.ValueString ?? constraint.Identifier).TrimStart('\\');
            return string.Equals(text, "callable", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Shared <c>Callable</c> constraint used by utility types (<c>__CallableReturnType</c>,
        /// <c>__CallableParametersRest</c>, and peers) and other built-ins.
        /// Accepts structural callables, bare <c>callable</c>/<c>\Closure</c> (Closure via
        /// <c>__invoke</c>), <c>callable(…): R</c> facets, generic <c>\Closure&lt;C, …&gt;</c>
        /// when <c>C</c> itself satisfies Callable, nullable wrappers of those, unions of
        /// callables, intersections that include a callable, unresolved recovery types, and
        /// in-scope generic type parameters (their <c>extends callable</c> bound is checked at
        /// declaration). Rejects empty <c>\Closure&lt;&gt;</c> so those shapes are not silently
        /// accepted after utility resolvers stop emitting ad-hoc
        /// <c>CheckerUtilityTypeInvalidArgument</c>. Does not treat
        /// <c>\Closure&lt;int, string&gt;</c> as a signature.
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
                if (IsBareCallableName(generic.BaseType))
                {
                    return false;
                }

                // `\Closure<C, …>` is callable when C (TCallableShape) is — not when the remaining
                // args look like return-last params. Bare Closure (no type args) is handled below
                // via the class name / `__invoke`.
                return CallableArityFacetBuilder.IsClosureTypeName(generic.BaseType)
                    && generic.TypeArguments.Count > 0
                    && SatisfiesCallableConstraint(generic.TypeArguments[0]);
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

            return IsBareCallableName(arg)
                || CallableArityFacetBuilder.IsClosureTypeName(arg);
        }

        private static bool IsBareCallableName(ICheckedType type) =>
            Rules.CheckerHelpers.IsBuiltInName(type, "callable")
            || type is SimpleCheckedType { ResolvedSymbol.Name: "callable" };

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
            // In-scope / unbound type parameters are allowed here the same way
            // <see cref="IsObjectConstraint"/> allows them: `__PropertyName<T>` in a generic
            // signature is valid; T's own bound is checked at the declaration. Unresolved is
            // the inference placeholder and must not cascade TYHP4035.
            type is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol }
            || type.Kind == CheckedTypeKind.Unresolved
            || IsClassOrStruct(type, symbolTree, globalScope)
            || Rules.CheckerHelpers.TryGetObjectDeclaration(type) is { ObjectKind: PhpTypeDeclType.Interface };

        private static bool IsClassOrStruct(ICheckedType type, SymbolTree symbolTree, GlobalScope globalScope)
        {
            var obj = Rules.CheckerHelpers.TryGetObjectDeclaration(type);
            return obj is not null && (obj.IsStruct || obj.ObjectKind == PhpTypeDeclType.Class);
        }

        /// <summary>
        /// <c>T extends object</c>: built-in <c>object</c>, classes, interfaces, enums, structs,
        /// object-shape aliases, <c>__New&lt;Shape&gt;</c>, in-scope generic type parameters
        /// (their own bounds are checked at declaration), and deferred <c>__SuperType&lt;T&gt;</c>
        /// (always an object type; see <see cref="MagicUtilityTypeResolver.IsSuperTypeUtility"/>).
        /// </summary>
        private static bool IsObjectConstraint(ICheckedType type, SymbolTree symbolTree, GlobalScope globalScope) =>
            type is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol }
            || type.Kind == CheckedTypeKind.Unresolved
            || Rules.CheckerHelpers.IsBuiltInName(type, "object")
            || MagicUtilityTypeResolver.IsSuperTypeUtility(type)
            || TypeComparer.IsNewUtilityType(type)
            || TypeComparer.IsObjectShapeTypeArgument(type)
            || IsClassInterfaceOrStruct(type, symbolTree, globalScope)
            || Rules.CheckerHelpers.TryGetObjectDeclaration(type) is
            {
                ObjectKind: PhpTypeDeclType.Enum or PhpTypeDeclType.Trait,
            };

        private static bool IsStringLiteralUnion(ICheckedType type) =>
            type is LiteralCheckedType { UnderlyingType: var underlying }
                && Rules.CheckerHelpers.IsBuiltInName(underlying, "string")
            || type is UnionCheckedType union && union.Members.All(IsStringLiteralUnion);

        private static bool IsPhpArrayAccess(IBaseSymbol declaringSymbol)
        {
            var fqn = declaringSymbol.FullyQualifiedName.TrimStart('\\');
            return string.Equals(fqn, "ArrayAccess", StringComparison.OrdinalIgnoreCase);
        }

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
                CheckerHelpers.ResolveDiagnosticFileName(state, node),
                args);
        }
    }
}
