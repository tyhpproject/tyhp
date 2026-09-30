using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Resolves built-in global <c>__</c> utility types at compile time into concrete checked types.
    /// </summary>
    internal static class UtilityTypeResolver
    {
        private static readonly DiagnosticBag ExpandDiagnostics = new();

        public static ICheckedType Resolve(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> typeArguments,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            ValidateUtilityArity(utility, typeArguments, reportNode, state, diagnostics);
            GenericTypeArgumentValidator.ValidateUtilityConstraints(
                utility, typeArguments, reportNode, state, symbolTree, globalScope, diagnostics);

            return utility.Behavior switch
            {
                UtilityBehavior.Readonly => ResolveReadonly(
                    typeArguments, reportNode, state, symbolTree, globalScope, diagnostics, resolveType),
                UtilityBehavior.Partial => ResolvePartial(
                    typeArguments, reportNode, state, symbolTree, globalScope, diagnostics, resolveType),
                UtilityBehavior.Required => ResolveRequired(
                    typeArguments, reportNode, state, symbolTree, globalScope, diagnostics, resolveType),
                UtilityBehavior.Pick => ResolvePick(
                    typeArguments, reportNode, state, symbolTree, globalScope, diagnostics, resolveType),
                UtilityBehavior.Omit => ResolveOmit(
                    typeArguments, reportNode, state, symbolTree, globalScope, diagnostics, resolveType),
                UtilityBehavior.Record => ResolveRecord(typeArguments, reportNode, state, diagnostics),
                UtilityBehavior.Exclude => ResolveExclude(typeArguments, symbolTree, globalScope),
                UtilityBehavior.Extract => ResolveExtract(typeArguments, symbolTree, globalScope),
                UtilityBehavior.NonNullable => ResolveAsNotNullable(typeArguments),
                UtilityBehavior.Nullable => ResolveNullable(typeArguments),
                UtilityBehavior.ReturnType => ResolveReturnType(utility, typeArguments),
                UtilityBehavior.Parameters => ResolveParameters(typeArguments),
                UtilityBehavior.CallableReturnType => ResolveReturnType(utility, typeArguments),
                UtilityBehavior.CallableParametersStruct => ResolveCallableParametersStruct(utility, typeArguments),
                UtilityBehavior.CallableParametersTuple => ResolveCallableParametersTuple(utility, typeArguments),
                UtilityBehavior.CallableParametersRest => ResolveCallableParametersRest(utility, typeArguments),
                UtilityBehavior.CallableParametersSlice => ResolveCallableParametersSlice(utility, typeArguments),
                UtilityBehavior.Awaited => ResolveAwaited(typeArguments, symbolTree, globalScope),
                UtilityBehavior.StructKey => ResolveStructKey(typeArguments, state, symbolTree, globalScope, resolveType),
                UtilityBehavior.StructRecord => ResolveStructRecord(utility, typeArguments),
                UtilityBehavior.StructDef => ResolveStructDef(typeArguments, state, symbolTree, globalScope, resolveType),
                UtilityBehavior.StructPartial => ResolveStructPartial(typeArguments, state, symbolTree, globalScope, resolveType),
                UtilityBehavior.Properties => ResolveProperties(
                    utility, typeArguments, symbolTree, globalScope, state, resolveType),
                UtilityBehavior.FunctionReturnType => ResolveFunctionReturnTypeByName(
                    typeArguments, state, symbolTree, globalScope, resolveType),
                UtilityBehavior.MethodReturnType => ResolveMethodReturnTypeByName(
                    typeArguments, state, symbolTree, globalScope, resolveType),
                UtilityBehavior.TypeDiff => ResolveTypeDiff(typeArguments, symbolTree, globalScope),
                UtilityBehavior.AsNotNullable => ResolveAsNotNullable(typeArguments),
                UtilityBehavior.AsNullable => ResolveNullable(typeArguments),
                UtilityBehavior.AsReadOnly => ResolveReadonly(
                    typeArguments, reportNode, state, symbolTree, globalScope, diagnostics, resolveType),
                UtilityBehavior.New => ResolveNew(utility, typeArguments, reportNode, state, diagnostics),
                _ when MagicUtilityTypeResolver.IsMagicBehavior(utility.Behavior) =>
                    MagicUtilityTypeResolver.Resolve(
                        utility, typeArguments, state, symbolTree, globalScope, resolveType),
                _ when TypeNameAlgebraResolver.IsTypeNameAlgebraBehavior(utility.Behavior) =>
                    TypeNameAlgebraResolver.Resolve(utility.Behavior, typeArguments, globalScope),
                _ when SymbolNameTypeHelper.IsSymbolNameBehavior(utility.Behavior) =>
                    ResolveSymbolNameType(utility, typeArguments, globalScope),
                _ => CheckedTypes.Unresolved,
            };
        }

        private static ICheckedType ResolveSymbolNameType(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> typeArguments,
            GlobalScope globalScope)
        {
            if (utility.Behavior == UtilityBehavior.TyhpInternal && typeArguments.Count > 0)
            {
                return typeArguments[0];
            }

            // Delegate to MakeSymbolNameType so optional-single brands normalize bare → <object>.
            return SymbolNameTypeHelper.MakeSymbolNameType(
                utility.Behavior, globalScope, typeArguments.Count == 0 ? null : typeArguments);
        }

        /// <summary>
        /// <c>__New&lt;T&gt;</c> stays a wrapper: values are instances of the shape whose class is
        /// constructable as the shape. <c>T</c> must be an object-shape alias (TYHP4351).
        /// </summary>
        private static ICheckedType ResolveNew(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> typeArguments,
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (typeArguments.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var arg = typeArguments[0];
            if (!TypeComparer.IsUnresolvedType(arg) && !TypeComparer.IsObjectShapeTypeArgument(arg))
            {
                Report(
                    reportNode,
                    state,
                    diagnostics,
                    MessageCode.CheckerNewTypeArgumentNotObjectShape,
                    arg.DisplayName);
            }

            return new GenericCheckedType(CheckedTypes.FromSymbol(utility), typeArguments);
        }

        private static ICheckedType ResolveReadonly(
            IReadOnlyList<ICheckedType> args,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var input = args[0];
            var structShape = StructTypeHelper.TryGetPropertyShape(input, state, symbolTree, globalScope, resolveType);
            if (structShape is null)
            {
                return input;
            }

            if (structShape.Properties.Values.All(property => property.IsReadonly))
            {
                return structShape;
            }

            var readonlyProps = structShape.Properties.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { IsReadonly = true });
            return new StructCheckedType(readonlyProps);
        }

        private static ICheckedType ResolvePartial(
            IReadOnlyList<ICheckedType> args,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var structShape = StructTypeHelper.TryGetPropertyShape(
                args[0], state, symbolTree, globalScope, resolveType);
            if (structShape is null)
            {
                return args[0];
            }

            // Nullable values *and* optional keys — otherwise Partial would still require
            // every field to be present (IsStructAssignableToStruct treats IsOptional as
            // key presence, distinct from nullability). Named struct/class declarations
            // arrive as SimpleCheckedType; without TryGetPropertyShape this was a no-op.
            var nullableProps = structShape.Properties.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with
                {
                    Type = pair.Value.Type.IsNullable
                        ? pair.Value.Type
                        : new NullableCheckedType(pair.Value.Type),
                    IsOptional = true,
                });
            return new StructCheckedType(nullableProps);
        }

        private static ICheckedType ResolveRequired(
            IReadOnlyList<ICheckedType> args,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var structShape = StructTypeHelper.TryGetPropertyShape(
                args[0], state, symbolTree, globalScope, resolveType);
            if (structShape is null)
            {
                return args[0];
            }

            var requiredProps = structShape.Properties.ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var required = pair.Value.Type is NullableCheckedType nullable
                        ? pair.Value.WithType(nullable.InnerType)
                        : pair.Value;
                    return required with { IsOptional = false };
                });
            return new StructCheckedType(requiredProps);
        }

        private static ICheckedType ResolvePick(
            IReadOnlyList<ICheckedType> args,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count < 2)
            {
                return args.Count > 0 ? args[0] : CheckedTypes.Unresolved;
            }

            var structType = StructTypeHelper.TryGetPropertyShape(
                args[0], state, symbolTree, globalScope, resolveType);
            if (structType is null)
            {
                return args[0];
            }

            var keys = ExtractStringLiteralKeys(args[1]);
            var picked = new Dictionary<string, StructPropertyInfo>();
            foreach (var key in keys)
            {
                if (!TryResolveStructProperty(structType, key, out var propertyKey, out var propInfo))
                {
                    Report(reportNode, state, diagnostics, MessageCode.CheckerUtilityTypeInvalidKey, key);
                    continue;
                }

                picked[propertyKey] = propInfo;
            }

            return new StructCheckedType(picked);
        }

        private static ICheckedType ResolveOmit(
            IReadOnlyList<ICheckedType> args,
            IBase2Ast reportNode,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            DiagnosticBag diagnostics,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count < 2)
            {
                return args.Count > 0 ? args[0] : CheckedTypes.Unresolved;
            }

            var structType = StructTypeHelper.TryGetPropertyShape(
                args[0], state, symbolTree, globalScope, resolveType);
            if (structType is null)
            {
                return args[0];
            }

            var omitPropertyKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in ExtractStringLiteralKeys(args[1]))
            {
                if (!TryResolveStructProperty(structType, key, out var propertyKey, out _))
                {
                    Report(reportNode, state, diagnostics, MessageCode.CheckerUtilityTypeInvalidKey, key);
                    continue;
                }

                omitPropertyKeys.Add(propertyKey);
            }

            var remaining = new Dictionary<string, StructPropertyInfo>();
            foreach (var (key, propInfo) in structType.Properties)
            {
                if (omitPropertyKeys.Contains(key))
                {
                    continue;
                }

                remaining[key] = propInfo;
            }

            return new StructCheckedType(remaining);
        }

        private static ICheckedType ResolveRecord(IReadOnlyList<ICheckedType> args, IBase2Ast reportNode, CheckerState state, DiagnosticBag diagnostics)
        {
            if (args.Count < 2)
            {
                return CheckedTypes.Unresolved;
            }

            return new GenericCheckedType(
                CheckedTypes.FromSymbol(new BuiltInTypeSymbol("array")),
                [args[0], args[1]]);
        }

        private static ICheckedType ResolveExclude(
            IReadOnlyList<ICheckedType> args,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (args.Count < 2)
            {
                return CheckedTypes.Unresolved;
            }

            var source = args[0];
            var exclude = args[1];
            if (source is not UnionCheckedType union)
            {
                return TypeComparer.IsAssignableTo(source, exclude, symbolTree, globalScope)
                    ? CheckedTypes.Never
                    : source;
            }

            var remaining = union.Members
                .Where(member => !TypeComparer.IsAssignableTo(member, exclude, symbolTree, globalScope))
                .ToList();
            return remaining.Count == 0 ? CheckedTypes.Never : CheckedTypes.UnionTypes(remaining);
        }

        private static ICheckedType ResolveExtract(
            IReadOnlyList<ICheckedType> args,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (args.Count < 2)
            {
                return CheckedTypes.Unresolved;
            }

            var source = args[0];
            var extract = args[1];
            if (source is not UnionCheckedType union)
            {
                return TypeComparer.IsAssignableTo(source, extract, symbolTree, globalScope)
                    ? source
                    : CheckedTypes.Never;
            }

            var matching = union.Members
                .Where(member => TypeComparer.IsAssignableTo(member, extract, symbolTree, globalScope))
                .ToList();
            return matching.Count == 0 ? CheckedTypes.Never : CheckedTypes.UnionTypes(matching);
        }

        private static ICheckedType ResolveNullable(IReadOnlyList<ICheckedType> args)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var inner = args[0];
            if (ParameterPack.IsPack(inner) || inner is ParameterPackCheckedType)
            {
                return ParameterPack.ApplyNullable(inner);
            }

            return inner.IsNullable ? inner : new NullableCheckedType(inner);
        }

        /// <summary>
        /// Shared by <c>__CallableReturnType</c> and the internal <see cref="UtilityBehavior.ReturnType"/>
        /// dispatch. Concrete callables collapse to the reflected return type. An unbound
        /// <c>TCallable</c> stays a <see cref="GenericCheckedType"/> of this utility so call-site
        /// substitution can fill the argument and <see cref="ExpandAfterSubstitution"/> can
        /// re-resolve. Shapes that cannot be reflected (a non-callable already reported as
        /// TYHP4035) resolve to the unresolved recovery type rather than <c>mixed</c>, so
        /// narrowing diagnostics do not pile on top of the original failure.
        /// </summary>
        private static ICheckedType ResolveReturnType(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> args)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            // Invalid args are reported once by ValidateUtilityConstraints (Callable → 4035).
            // Do not also emit CheckerUtilityTypeInvalidArgument here.
            if (CallableSignatureReflection.TryGetReturnType(args[0], out var returnType))
            {
                return returnType;
            }

            if (CallableSignatureReflection.IsUnboundTypeParameter(args[0]))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), args);
            }

            return CheckedTypes.Unresolved;
        }

        /// <summary>
        /// True when a parameter type should receive call-site generic bindings so a deferred
        /// wrapper can expand once the sibling argument has filled the type parameter.
        /// </summary>
        /// <summary>
        /// True when a parameter type should receive call-site generic bindings so a deferred
        /// wrapper can expand once the sibling argument has filled the type parameter.
        /// Rest / Tuple / Struct / return-type / Properties skip argument-driven binding of
        /// <c>TCallable</c> (the callback argument fills it). Slice is not in this set:
        /// <c>TZip</c> is inferred from array element types.
        /// </summary>
        internal static bool IsCallSiteExpandingUtility(UtilityBehavior behavior) =>
            behavior is UtilityBehavior.CallableParametersStruct
                or UtilityBehavior.CallableParametersTuple
                or UtilityBehavior.CallableParametersRest
                or UtilityBehavior.CallableReturnType
                or UtilityBehavior.ReturnType
                or UtilityBehavior.Properties;

        /// <summary>
        /// Utilities that stay deferred until substitution fills their type argument, including
        /// Slice (so <c>array&lt;Slice&lt;TZip, 0&gt;&gt;</c> expands after inference).
        /// </summary>
        internal static bool IsDeferredExpandingUtility(UtilityBehavior behavior) =>
            IsCallSiteExpandingUtility(behavior)
            || behavior == UtilityBehavior.CallableParametersSlice;

        /// <summary>
        /// True when <paramref name="type"/> is still the deferred <c>__Properties&lt;T&gt;</c>
        /// wrapper (T not yet a concrete object/struct).
        /// </summary>
        internal static bool IsDeferredPropertiesType(ICheckedType type) =>
            type is GenericCheckedType
            && SymbolNameTypeHelper.TryGetUtilitySymbol(type, out var utility)
            && utility.Behavior == UtilityBehavior.Properties;

        /// <summary>
        /// Re-resolves a deferred <c>__CallableReturnType</c> /
        /// <c>__Properties</c> wrapper after generic substitution has filled the type argument.
        /// Unbound wrappers are left intact. Called from <see cref="TypeComparer"/> substitution
        /// so every binding path (call-site inference, explicit type arguments, facet
        /// substitution) expands.
        /// </summary>
        internal static ICheckedType ExpandAfterSubstitution(ICheckedType type) =>
            ExpandAfterSubstitution(type, symbolTree: null, globalScope: null);

        /// <summary>
        /// Same as <see cref="ExpandAfterSubstitution(ICheckedType)"/>, with a symbol tree so
        /// <c>__SuperType</c> / index utilities can walk parents and struct members after
        /// substitution.
        /// </summary>
        internal static ICheckedType ExpandAfterSubstitution(
            ICheckedType type,
            SymbolTree? symbolTree,
            GlobalScope? globalScope)
        {
            if (type is not GenericCheckedType { TypeArguments.Count: > 0 } generic
                || !SymbolNameTypeHelper.TryGetUtilitySymbol(type, out var utility))
            {
                return type;
            }

            return utility.Behavior switch
            {
                UtilityBehavior.ReturnType or UtilityBehavior.CallableReturnType =>
                    ExpandReturnTypeAfterSubstitution(generic),
                UtilityBehavior.CallableParametersStruct =>
                    ExpandCallableParametersStructAfterSubstitution(generic),
                UtilityBehavior.CallableParametersTuple =>
                    ExpandCallableParametersTupleAfterSubstitution(generic),
                UtilityBehavior.CallableParametersRest =>
                    ExpandCallableParametersRestAfterSubstitution(generic),
                UtilityBehavior.CallableParametersSlice =>
                    ExpandCallableParametersSliceAfterSubstitution(generic),
                UtilityBehavior.Properties =>
                    ExpandPropertiesAfterSubstitution(generic, utility, symbolTree, globalScope),
                _ when MagicUtilityTypeResolver.IsMagicBehavior(utility.Behavior) =>
                    MagicUtilityTypeResolver.ExpandAfterSubstitution(generic, utility, symbolTree, globalScope),
                _ => type,
            };
        }

        private static ICheckedType ExpandPropertiesAfterSubstitution(
            GenericCheckedType generic,
            BuiltInUtilityTypeSymbol utility,
            SymbolTree? symbolTree,
            GlobalScope? globalScope)
        {
            if (globalScope is null)
            {
                return generic;
            }

            var tree = symbolTree ?? new SymbolTree(globalScope);
            return ResolveProperties(
                utility,
                generic.TypeArguments,
                tree,
                globalScope,
                new CheckerState(),
                (typeExpr, state, _, _) => ResolveTypeFromBoundSymbol(typeExpr, state, tree, globalScope));
        }

        private static ICheckedType ResolveTypeFromBoundSymbol(
            ITypeExpression typeExpr,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (typeExpr.BoundSymbol is BuiltInTypeSymbol builtin)
            {
                return CheckedTypes.FromSymbol(builtin);
            }

            if (typeExpr.BoundSymbol is IBaseSymbol bound)
            {
                return CheckedTypes.FromSymbol(bound);
            }

            var scope = state.NameResolutionScope
                ?? state.EnclosingObject?.ContainingScope
                ?? globalScope;
            var resolved = symbolTree.ResolveType(typeExpr, scope, ExpandDiagnostics);
            return resolved is null ? CheckedTypes.Mixed : CheckedTypes.FromSymbol(resolved);
        }

        private static ICheckedType ExpandReturnTypeAfterSubstitution(GenericCheckedType generic)
        {
            var callableArg = generic.TypeArguments[0];
            if (CallableSignatureReflection.TryGetReturnType(callableArg, out var returnType))
            {
                return returnType;
            }

            // Still open (or already a recovery type): keep the wrapper so a later substitution
            // can fill it. A concrete non-callable must not leak as `__CallableReturnType<int>`.
            if (CallableSignatureReflection.IsUnboundTypeParameter(callableArg)
                || TypeComparer.IsUnresolvedType(callableArg))
            {
                return generic;
            }

            return CheckedTypes.Unresolved;
        }

        private static ICheckedType ExpandCallableParametersStructAfterSubstitution(
            GenericCheckedType generic)
        {
            var callableArg = generic.TypeArguments[0];
            if (CallableSignatureReflection.TryBuildNamedParametersStruct(callableArg, out var bag)
                && bag is not null)
            {
                return bag;
            }

            if (CallableSignatureReflection.IsUnboundTypeParameter(callableArg)
                || TypeComparer.IsUnresolvedType(callableArg))
            {
                return generic;
            }

            return CheckedTypes.Unresolved;
        }

        private static ICheckedType ExpandCallableParametersTupleAfterSubstitution(
            GenericCheckedType generic)
        {
            var callableArg = generic.TypeArguments[0];
            if (CallableSignatureReflection.TryBuildPositionalParametersStruct(callableArg, out var bag)
                && bag is not null)
            {
                return bag;
            }

            if (CallableSignatureReflection.IsUnboundTypeParameter(callableArg)
                || TypeComparer.IsUnresolvedType(callableArg))
            {
                return generic;
            }

            return CheckedTypes.Unresolved;
        }

        /// <summary>
        /// Rest unpack marker. Unlike Struct/Tuple, this does <em>not</em> collapse to a synthetic
        /// struct: call-site checking needs the wrapper to unpack trailing arguments against
        /// <c>TCallable</c>'s parameter list. Callable-ish arguments (including unions whose
        /// arities cannot merge) keep the wrapper so unpack can stay gradual. Non-callables
        /// recover to unresolved so <c>__CallableParametersRest&lt;int&gt;</c> does not leak.
        /// </summary>
        private static ICheckedType ExpandCallableParametersRestAfterSubstitution(
            GenericCheckedType generic)
        {
            var callableArg = generic.TypeArguments[0];
            if (CallableSignatureReflection.IsUnboundTypeParameter(callableArg)
                || TypeComparer.IsUnresolvedType(callableArg)
                || CallableSignatureReflection.TryReflect(callableArg, out _)
                || CallableSignatureReflection.TryGetReturnType(callableArg, out _))
            {
                return generic;
            }

            return CheckedTypes.Unresolved;
        }

        /// <summary>
        /// Checker-only carrier for "the return type of this type-parameter callable", used when
        /// invoking <c>TCallable $cb</c> inside a generic wrapper. Matches the deferred form
        /// <c>ResolveReturnType</c> produces for <c>__CallableReturnType&lt;TCallable&gt;</c> so
        /// <c>return $cb()</c> type-checks against that annotation.
        /// </summary>
        internal static ICheckedType MakeDeferredCallableReturnType(
            ICheckedType callableType,
            GlobalScope globalScope)
        {
            if (((IBaseScope)globalScope).FindChildSymbolByName("__CallableReturnType")
                is not BuiltInUtilityTypeSymbol utility)
            {
                return CheckedTypes.Unresolved;
            }

            return new GenericCheckedType(CheckedTypes.FromSymbol(utility), [callableType]);
        }

        private static ICheckedType ResolveParameters(IReadOnlyList<ICheckedType> args)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            // Invalid args are reported once by ValidateUtilityConstraints (Callable → 4035).
            if (!CallableSignatureReflection.TryReflect(args[0], out var signature) || signature is null)
            {
                return CheckedTypes.Unresolved;
            }

            var paramTypes = signature.Parameters.Select(p => p.Type).ToList();
            var valueType = paramTypes.Count == 0
                ? CheckedTypes.Mixed
                : CheckedTypes.UnionTypes(paramTypes);
            return new GenericCheckedType(
                CheckedTypes.FromSymbol(new BuiltInTypeSymbol("array")),
                [CheckedTypes.Int, valueType]);
        }

        /// <summary>
        /// True when <paramref name="type"/> is (or unwraps to) a
        /// <c>__CallableParametersRest&lt;TCallable&gt;</c> wrapper. The wrapper is kept even after
        /// <c>TCallable</c> is bound so call-site unpack can distinguish rest from a Tuple bag.
        /// </summary>
        internal static bool TryGetCallableParametersRest(
            ICheckedType type,
            out ICheckedType callableArg)
        {
            callableArg = type;
            while (type is NullableCheckedType nullable)
            {
                type = nullable.InnerType;
            }

            if (type is GenericCheckedType { TypeArguments.Count: > 0 } generic
                && SymbolNameTypeHelper.TryGetUtilitySymbol(generic, out var utility)
                && utility.Behavior == UtilityBehavior.CallableParametersRest)
            {
                callableArg = generic.TypeArguments[0];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Named-arg bag for <c>__CallableParametersStruct&lt;TCallable&gt;</c>. Concrete callables
        /// with parameter names collapse to a synthetic struct. Defaulted parameters become
        /// optional fields (required-key assignability). An unbound <c>TCallable</c> stays a
        /// <see cref="GenericCheckedType"/> of this utility so call-site substitution can fill
        /// the argument and <see cref="ExpandAfterSubstitution"/> can re-resolve. Facets without
        /// names degrade to an empty struct (no string keys). Non-callables recover to unresolved.
        /// </summary>
        private static ICheckedType ResolveCallableParametersStruct(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> args)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            if (CallableSignatureReflection.TryBuildNamedParametersStruct(args[0], out var bag)
                && bag is not null)
            {
                return bag;
            }

            if (CallableSignatureReflection.IsUnboundTypeParameter(args[0]))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), args);
            }

            return CheckedTypes.Unresolved;
        }

        /// <summary>
        /// Positional-arg bag for <c>__CallableParametersTuple&lt;TCallable&gt;</c>. Concrete
        /// callables collapse to a synthetic struct with int key aliases <c>0 as $_1</c>,
        /// <c>1 as $_2</c>, … matching <c>CallableArgs*</c>. Defaulted parameters become optional
        /// fields so a shorter list literal can omit trailing keys. An unbound <c>TCallable</c>
        /// stays a <see cref="GenericCheckedType"/> of this utility so call-site substitution can
        /// fill the argument and <see cref="ExpandAfterSubstitution"/> can re-resolve.
        /// Non-callables recover to unresolved.
        /// </summary>
        private static ICheckedType ResolveCallableParametersTuple(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> args)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            if (CallableSignatureReflection.TryBuildPositionalParametersStruct(args[0], out var bag)
                && bag is not null)
            {
                return bag;
            }

            if (CallableSignatureReflection.IsUnboundTypeParameter(args[0]))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), args);
            }

            return CheckedTypes.Unresolved;
        }

        /// <summary>
        /// Rest-unpack marker for <c>__CallableParametersRest&lt;TCallable&gt;</c>. Stays a
        /// <see cref="GenericCheckedType"/> of this utility for unbound <c>TCallable</c>, concrete
        /// callables, and callable-ish unions (even when arities cannot merge) so call-site
        /// checking can unpack trailing arguments against the reflected parameter list.
        /// Non-callables recover to unresolved.
        /// </summary>
        private static ICheckedType ResolveCallableParametersRest(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> args)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            if (CallableSignatureReflection.IsUnboundTypeParameter(args[0])
                || CallableSignatureReflection.TryReflect(args[0], out _)
                || CallableSignatureReflection.TryGetReturnType(args[0], out _))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), args);
            }

            return CheckedTypes.Unresolved;
        }

        private static ICheckedType ExpandCallableParametersSliceAfterSubstitution(
            GenericCheckedType generic)
        {
            var callableArg = generic.TypeArguments[0];
            if (CallableSignatureReflection.IsUnboundTypeParameter(callableArg)
                || TypeComparer.IsUnresolvedType(callableArg)
                || CallableSignatureReflection.TryReflect(callableArg, out _)
                || CallableSignatureReflection.TryGetReturnType(callableArg, out _))
            {
                return generic;
            }

            return CheckedTypes.Unresolved;
        }

        /// <summary>
        /// True when <paramref name="type"/> is (or unwraps to) a
        /// <c>__CallableParametersSlice&lt;TCallable, …&gt;</c> wrapper.
        /// </summary>
        internal static bool TryGetCallableParametersSlice(
            ICheckedType type,
            out ICheckedType callableArg)
        {
            callableArg = type;
            if (!ParameterPack.TryGetSlice(type, out var info))
            {
                return false;
            }

            callableArg = info.CallableArg;
            return true;
        }

        /// <summary>
        /// Slice-unpack marker. Stays a <see cref="GenericCheckedType"/> of this utility for
        /// unbound <c>TCallable</c> and concrete callables so call-site unpack can see
        /// <c>TStart</c> / <c>TMin</c>. Non-callables recover to unresolved.
        /// </summary>
        private static ICheckedType ResolveCallableParametersSlice(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> args)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            if (CallableSignatureReflection.IsUnboundTypeParameter(args[0])
                || CallableSignatureReflection.TryReflect(args[0], out _)
                || CallableSignatureReflection.TryGetReturnType(args[0], out _))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), args);
            }

            return CheckedTypes.Unresolved;
        }

        private static ICheckedType ResolveAwaited(
            IReadOnlyList<ICheckedType> args,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var current = args[0];
            while (current is GenericCheckedType generic
                && generic.BaseType.DisplayName.Contains("Promise", StringComparison.OrdinalIgnoreCase)
                && generic.TypeArguments.Count > 0)
            {
                current = generic.TypeArguments[0];
            }

            return current;
        }

        private static ICheckedType ResolveStructKey(
            IReadOnlyList<ICheckedType> args,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var structShape = StructTypeHelper.TryGetStructShape(args[0], state, symbolTree, globalScope, resolveType);
            return structShape is null
                ? CheckedTypes.String
                : StructTypeHelper.BuildStructKeyUnion(structShape);
        }

        // Preserve the __StructRecord<TStructType, TKeys, TValueType> carrier so __StructDef can read its
        // type arguments. Collapsing it to its `void` erasure here would make __StructDef unable to unpack it.
        private static ICheckedType ResolveStructRecord(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> args) =>
            args.Count >= 3
                ? new GenericCheckedType(CheckedTypes.FromSymbol(utility), args)
                : CheckedTypes.Unresolved;

        private static ICheckedType ResolveStructDef(
            IReadOnlyList<ICheckedType> args,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            if (!TryUnpackStructRecord(args[0], out var structType, out var keysArg, out var valueType))
            {
                return CheckedTypes.Unresolved;
            }

            var keys = ExtractStringLiteralKeys(keysArg).ToList();
            if (keys.Count == 0)
            {
                return StructTypeHelper.TryGetPropertyShape(structType, state, symbolTree, globalScope, resolveType)
                    ?? StructCheckedType.FromMutableProperties(new Dictionary<string, ICheckedType>());
            }

            var properties = keys.ToDictionary(
                key => key,
                _ => new StructPropertyInfo(valueType));
            return new StructCheckedType(properties);
        }

        private static ICheckedType ResolveStructPartial(
            IReadOnlyList<ICheckedType> args,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count < 3)
            {
                return CheckedTypes.Unresolved;
            }

            var structShape = StructTypeHelper.TryGetPropertyShape(args[0], state, symbolTree, globalScope, resolveType);
            if (structShape is null)
            {
                return CheckedTypes.Unresolved;
            }

            var includeKeys = ExtractNullableStringLiteralKeys(args[1]);
            var excludeKeys = ExtractNullableStringLiteralKeys(args[2]);

            if (includeKeys is null && excludeKeys is null)
            {
                return StructCheckedType.FromMutableProperties(new Dictionary<string, ICheckedType>());
            }

            IEnumerable<KeyValuePair<string, StructPropertyInfo>> selected;
            if (includeKeys is not null && excludeKeys is null)
            {
                selected = structShape.Properties.Where(pair => includeKeys.Contains(pair.Key));
            }
            else if (includeKeys is null && excludeKeys is not null)
            {
                selected = structShape.Properties.Where(pair => !excludeKeys.Contains(pair.Key));
            }
            else
            {
                var include = includeKeys ?? new HashSet<string>(StringComparer.Ordinal);
                var exclude = excludeKeys ?? new HashSet<string>(StringComparer.Ordinal);
                selected = structShape.Properties.Where(pair => include.Contains(pair.Key) && !exclude.Contains(pair.Key));
            }

            return new StructCheckedType(selected.ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        private static ICheckedType ResolveProperties(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> args,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            CheckerState state,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var owner = args[0];
            while (owner is NullableCheckedType nullable)
            {
                owner = nullable.InnerType;
            }

            if (CallableSignatureReflection.IsUnboundTypeParameter(owner)
                || TypeComparer.IsUnresolvedType(owner))
            {
                return new GenericCheckedType(CheckedTypes.FromSymbol(utility), args);
            }

            if (owner is UnionCheckedType union)
            {
                return CheckedTypes.UnionTypes(
                    union.Members
                        .Select(member => ResolveProperties(
                            utility, [member], symbolTree, globalScope, state, resolveType))
                        .ToList());
            }

            var structShape = StructTypeHelper.TryGetPropertyShape(
                owner,
                state,
                symbolTree,
                globalScope,
                resolveType,
                instancePropertiesOnly: true);
            if (structShape is null)
            {
                return CheckedTypes.Unresolved;
            }

            var optional = structShape.Properties.ToDictionary(
                pair => pair.Key,
                pair => pair.Value with { IsOptional = true });
            return new StructCheckedType(optional);
        }

        private static ICheckedType ResolveFunctionReturnTypeByName(
            IReadOnlyList<ICheckedType> args,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count == 0 || !TryExtractFunctionName(args[0], out var fnName))
            {
                return CheckedTypes.Mixed;
            }

            var function = ResolveFunctionSymbol(fnName, state, symbolTree, globalScope);
            if (function?.ReturnType is null)
            {
                return CheckedTypes.Mixed;
            }

            // Bind under the callee's FunctionGenerics (Story 11 audit #5) so return annotations
            // like `array<TKey, TValue>` do not collapse type parameters to unresolved.
            var resolveState = state;
            if (function.GenericParameters.Count > 0)
            {
                resolveState = state.Fork();
                resolveState.FunctionGenerics = function.GenericParameters;
            }

            return resolveType(function.ReturnType, resolveState, true, true);
        }

        private static ICheckedType ResolveMethodReturnTypeByName(
            IReadOnlyList<ICheckedType> args,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, CheckerState, bool, bool, ICheckedType> resolveType)
        {
            if (args.Count < 2)
            {
                return CheckedTypes.Mixed;
            }

            var owner = args[0];
            var methodKeys = ExtractStringLiteralKeys(args[1]).ToList();
            if (methodKeys.Count != 1)
            {
                return CheckedTypes.Mixed;
            }

            var methodName = methodKeys[0];
            if (!TryResolveMethodOnOwner(owner, methodName, out var method))
            {
                return CheckedTypes.Mixed;
            }

            if (method?.ReturnType is null)
            {
                return CheckedTypes.Mixed;
            }

            // Method params shadow class params; also expose the owner's ObjectGenerics when the
            // return annotation names a class type parameter (Story 11 audit #5 / item 39).
            var resolveState = state;
            var needsFunctionGenerics = method.GenericParameters.Count > 0;
            ObjectDeclarationSymbol? ownerObj = null;
            if (owner is SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol obj })
            {
                ownerObj = obj;
            }
            else if (owner is GenericCheckedType
                     {
                         BaseType: SimpleCheckedType { ResolvedSymbol: ObjectDeclarationSymbol genericObj }
                     })
            {
                ownerObj = genericObj;
            }

            var needsObjectGenerics = ownerObj is { GenericParameters.Count: > 0 };
            if (needsFunctionGenerics || needsObjectGenerics)
            {
                resolveState = state.Fork();
                if (needsFunctionGenerics)
                {
                    resolveState.FunctionGenerics = method.GenericParameters;
                }

                if (needsObjectGenerics)
                {
                    resolveState.ObjectGenerics = ownerObj!.GenericParameters;
                    resolveState.EnclosingObject = ownerObj;
                    resolveState.EnclosingObjectType = owner;
                }
            }

            return resolveType(method.ReturnType, resolveState, true, true);
        }

        private static ICheckedType ResolveTypeDiff(
            IReadOnlyList<ICheckedType> args,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (args.Count < 2)
            {
                return CheckedTypes.Unresolved;
            }

            var source = args[0];
            var exclude = args[1];
            if (source is UnionCheckedType union)
            {
                var remaining = union.Members
                    .Where(member => !TypeComparer.IsAssignableTo(member, exclude, symbolTree, globalScope))
                    .ToList();
                return remaining.Count == 0 ? CheckedTypes.Void : CheckedTypes.UnionTypes(remaining);
            }

            return TypeComparer.IsAssignableTo(source, exclude, symbolTree, globalScope)
                ? CheckedTypes.Void
                : source;
        }

        private static ICheckedType ResolveAsNotNullable(IReadOnlyList<ICheckedType> args)
        {
            if (args.Count == 0)
            {
                return CheckedTypes.Unresolved;
            }

            var input = args[0];
            if (input is LiteralCheckedType { Value: null } || CheckedTypes.AreTypesEqual(input, CheckedTypes.Null))
            {
                return CheckedTypes.Void;
            }

            if (ParameterPack.IsPack(input) || input is ParameterPackCheckedType)
            {
                var mapped = ParameterPack.ApplyNonNullable(input);
                if (mapped is not null)
                {
                    return mapped;
                }
            }

            return RemoveNull(input);
        }

        private static bool TryUnpackStructRecord(
            ICheckedType recordSet,
            out ICheckedType structType,
            out ICheckedType keys,
            out ICheckedType valueType)
        {
            structType = CheckedTypes.Unresolved;
            keys = CheckedTypes.Unresolved;
            valueType = CheckedTypes.Unresolved;

            if (recordSet is not GenericCheckedType { TypeArguments.Count: 3 } generic
                || !SymbolNameTypeHelper.TryGetUtilitySymbol(generic.BaseType, out var utility)
                || utility.Behavior != UtilityBehavior.StructRecord)
            {
                return false;
            }

            structType = generic.TypeArguments[0];
            keys = generic.TypeArguments[1];
            valueType = generic.TypeArguments[2];
            return true;
        }

        private static bool TryExtractFunctionName(ICheckedType type, out string functionName)
        {
            if (SymbolNameTypeHelper.TryGetStringLiteral(type, out functionName))
            {
                return true;
            }

            functionName = string.Empty;
            return false;
        }

        private static FunctionDeclarationSymbol? ResolveFunctionSymbol(
            string functionName,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            var scope = GetResolutionScope(state, globalScope);
            var resolver = new NameResolver(symbolTree, new DiagnosticBag());
            var symbol = resolver.ResolveSymbol(functionName, scope)
                ?? resolver.ResolveRelativeName(
                    functionName.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    scope);
            return symbol as FunctionDeclarationSymbol;
        }

        private static bool TryResolveMethodOnOwner(
            ICheckedType owner,
            string methodName,
            out ObjectMethodSymbol? method)
        {
            method = null;
            var obj = Rules.CheckerHelpers.TryGetObjectDeclaration(owner);
            if (obj is null || !obj.Members.TryGetValue(methodName, out var member))
            {
                return false;
            }

            method = member as ObjectMethodSymbol;
            return method is not null;
        }

        private static HashSet<string>? ExtractNullableStringLiteralKeys(ICheckedType type)
        {
            if (type is LiteralCheckedType { Value: null })
            {
                return null;
            }

            var keys = ExtractStringLiteralKeys(type).ToHashSet(StringComparer.Ordinal);
            return keys.Count == 0 ? null : keys;
        }

        private static IBaseScope GetResolutionScope(CheckerState state, GlobalScope globalScope)
        {
            if (state.EnclosingFunction?.ContainingScope is IBaseScope functionScope)
            {
                return functionScope;
            }

            if (state.EnclosingObject?.ContainingScope is IBaseScope objectScope)
            {
                return objectScope;
            }

            return globalScope;
        }

        private static ICheckedType RemoveNull(ICheckedType type)
        {
            if (type is NullableCheckedType nullable)
            {
                return nullable.InnerType;
            }

            if (type is UnionCheckedType union)
            {
                var withoutNull = union.Members
                    .Where(member => member.Kind != CheckedTypeKind.Literal
                        || member is not LiteralCheckedType { Value: null })
                    .ToList();
                return CheckedTypes.UnionTypes(withoutNull);
            }

            return type;
        }

        /// <summary>
        /// Struct properties are keyed <c>$name</c>; utility type arguments are written as
        /// <c>'name'</c>. Accept either spelling so <c>__Pick&lt;Point, 'x'&gt;</c> matches
        /// <c>$x</c>.
        /// </summary>
        private static bool TryResolveStructProperty(
            StructCheckedType structType,
            string key,
            out string propertyKey,
            out StructPropertyInfo propInfo)
        {
            if (structType.Properties.TryGetValue(key, out propInfo!))
            {
                propertyKey = key;
                return true;
            }

            if (!key.StartsWith('$'))
            {
                propertyKey = "$" + key;
                return structType.Properties.TryGetValue(propertyKey, out propInfo);
            }

            propertyKey = key;
            propInfo = null!;
            return false;
        }

        private static IEnumerable<string> ExtractStringLiteralKeys(ICheckedType type)
        {
            if (type is LiteralCheckedType { Value: string s })
            {
                yield return s;
                yield break;
            }

            if (type is UnionCheckedType union)
            {
                foreach (var member in union.Members)
                {
                    foreach (var key in ExtractStringLiteralKeys(member))
                    {
                        yield return key;
                    }
                }
            }
        }

        private static void ValidateUtilityArity(
            BuiltInUtilityTypeSymbol utility,
            IReadOnlyList<ICheckedType> typeArguments,
            IBase2Ast reportNode,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var requirements = utility.GenericParameterRequirements;
            if (typeArguments.Count < requirements.MinArity || typeArguments.Count > requirements.MaxArity)
            {
                var expected = requirements.MinArity == requirements.MaxArity
                    ? requirements.MinArity.ToString()
                    : $"{requirements.MinArity}-{requirements.MaxArity}";
                Report(reportNode, state, diagnostics, MessageCode.CheckerGenericArgumentCountMismatch,
                    utility.Name, expected, typeArguments.Count.ToString());
            }
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
