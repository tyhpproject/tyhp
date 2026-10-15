using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Call resolution for <c>.tyhp</c> overload sets (bodyless signatures followed by one
    /// declaration). The first signature, in declaration order, whose parameters accept the call
    /// is selected. Acceptance covers arity, named arguments, defaults, spreads, generic
    /// parameters (an open generic accepts any argument) and closures — a closure literal is
    /// matched on its parameter count and explicit type annotations, so an unannotated closure
    /// never rules a signature out, and is then checked against the selected signature's callable
    /// type like any other contextually typed closure.
    /// </summary>
    internal static class SourceOverloadSelector
    {
        public static bool IsSourceSet(FunctionDeclarationSymbol primary) =>
            primary.IsSourceOverloadSet && primary.Overloads.Count > 0;

        public static bool IsSourceSet(ObjectMethodSymbol primary) =>
            primary.IsSourceOverloadSet && primary.Overloads.Count > 0;

        public static FunctionDeclarationSymbol SelectFunction(
            FunctionDeclarationSymbol primary,
            PhpCallAst? call,
            FunctionOverloadSelector.Context? context,
            out bool matched) =>
            SelectFunction(primary, call, context, out matched, out _);

        /// <summary>
        /// Two-pass selection. Pass 1 takes the first declared signature that accepts the call as
        /// written; only when none does, pass 2 takes the best signature that accepts it through
        /// implicit <c>operator convert</c> (see <see cref="TryPick{T}"/>).
        /// <paramref name="ambiguous"/> lists the signatures a call that needs conversions
        /// cannot choose between; the first of them is returned.
        /// </summary>
        public static FunctionDeclarationSymbol SelectFunction(
            FunctionDeclarationSymbol primary,
            PhpCallAst? call,
            FunctionOverloadSelector.Context? context,
            out bool matched,
            out IReadOnlyList<FunctionDeclarationSymbol>? ambiguous)
        {
            ambiguous = null;
            matched = true;
            if (call is null || context is null)
            {
                return CheckerHelpers.SelectFunctionOverloadForCall(primary, call?.Arguments);
            }

            var candidates = CheckerHelpers.EnumerateFunctionSignatures(primary).ToList();
            if (TryPick(candidates, (c, profile) => Evaluate(c, call, context, profile), out var selected, out ambiguous))
            {
                return selected;
            }

            matched = false;
            return CheckerHelpers.SelectFunctionOverloadForCall(primary, call.Arguments);
        }

        /// <summary>
        /// True when <paramref name="candidate"/> accepts <paramref name="call"/> without any
        /// implicit conversion.
        /// </summary>
        public static bool Accepts(
            FunctionDeclarationSymbol candidate,
            PhpCallAst call,
            FunctionOverloadSelector.Context context) =>
            Evaluate(candidate, call, context, null);

        private static bool Evaluate(
            FunctionDeclarationSymbol candidate,
            PhpCallAst call,
            FunctionOverloadSelector.Context context,
            List<string?>? conversions)
        {
            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings = null;
            if (candidate.GenericParameters.Count > 0)
            {
                bindings = context.InferBindings(candidate, call);
            }

            return Applies(
                candidate.Parameters,
                call.Arguments?.GetAllNotNull().ToList() ?? [],
                param => ResolveExpected(
                    context.ResolveParameterType(candidate, param.DeclaredType!),
                    bindings,
                    context.SymbolTree,
                    context.GlobalScope),
                context.State,
                context.SymbolTree,
                context.GlobalScope,
                context.InferArgumentType,
                context.ResolveCallSiteType,
                conversions);
        }

        /// <summary>
        /// True when <paramref name="candidate"/> accepts <paramref name="call"/> without any
        /// implicit conversion.
        /// </summary>
        public static bool Accepts(
            ObjectMethodSymbol candidate,
            PhpCallAst call,
            FunctionOverloadSelector.MethodContext context) =>
            Evaluate(candidate, call, context, null);

        private static bool Evaluate(
            ObjectMethodSymbol candidate,
            PhpCallAst call,
            FunctionOverloadSelector.MethodContext context,
            List<string?>? conversions)
        {
            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings = null;
            if (candidate.GenericParameters.Count > 0)
            {
                bindings = context.InferBindings(candidate, call);
            }

            return Applies(
                CheckerHelpers.GetCallSiteParameters(candidate),
                call.Arguments?.GetAllNotNull().ToList() ?? [],
                param => ResolveExpected(
                    context.ResolveParameterType(candidate, param.DeclaredType!),
                    bindings,
                    context.SymbolTree,
                    context.GlobalScope),
                context.State,
                context.SymbolTree,
                context.GlobalScope,
                context.InferArgumentType,
                context.ResolveCallSiteType,
                conversions);
        }

        public static ObjectMethodSymbol SelectMethod(
            ObjectMethodSymbol primary,
            PhpCallAst? call,
            FunctionOverloadSelector.MethodContext? context,
            out bool matched) =>
            SelectMethod(primary, call, context, out matched, out _);

        /// <inheritdoc cref="SelectFunction(FunctionDeclarationSymbol, PhpCallAst?, FunctionOverloadSelector.Context?, out bool, out IReadOnlyList{FunctionDeclarationSymbol}?)"/>
        public static ObjectMethodSymbol SelectMethod(
            ObjectMethodSymbol primary,
            PhpCallAst? call,
            FunctionOverloadSelector.MethodContext? context,
            out bool matched,
            out IReadOnlyList<ObjectMethodSymbol>? ambiguous)
        {
            ambiguous = null;
            matched = true;
            if (call is null || context is null)
            {
                return CheckerHelpers.SelectMethodOverloadForCall(primary, call?.Arguments);
            }

            var candidates = CheckerHelpers.EnumerateMethodSignatures(primary).ToList();
            if (TryPick(candidates, (c, profile) => Evaluate(c, call, context, profile), out var selected, out ambiguous))
            {
                return selected;
            }

            matched = false;
            return CheckerHelpers.SelectMethodOverloadForCall(primary, call.Arguments);
        }

        /// <summary>
        /// The two-pass pick over <paramref name="candidates"/> (declaration order).
        /// <para>
        /// <b>Pass 1 (exact):</b> the first candidate that accepts the arguments as written wins; a
        /// conversion-only candidate declared earlier never beats it.
        /// </para>
        /// <para>
        /// <b>Pass 2 (conversion):</b> reached only when pass 1 finds nothing. Every candidate that
        /// accepts the arguments through implicit <c>operator convert</c> gets a <i>conversion
        /// profile</i>: for each call argument, the type it is converted to, or null when it is
        /// passed as written. A candidate whose profile converts a subset of another's (the rest of
        /// its arguments equal, the others passed as written) dominates it. If every undominated
        /// candidate has the same profile the first is picked; otherwise the call is ambiguous and
        /// <paramref name="ambiguous"/> lists the undominated candidates (the first is returned).
        /// </para>
        /// </summary>
        private static bool TryPick<T>(
            IReadOnlyList<T> candidates,
            Func<T, List<string?>?, bool> accepts,
            out T selected,
            out IReadOnlyList<T>? ambiguous)
            where T : class
        {
            ambiguous = null;
            foreach (var candidate in candidates)
            {
                if (accepts(candidate, null))
                {
                    selected = candidate;
                    return true;
                }
            }

            var accepted = new List<(T Candidate, List<string?> Profile)>();
            foreach (var candidate in candidates)
            {
                var profile = new List<string?>();
                if (accepts(candidate, profile))
                {
                    accepted.Add((candidate, profile));
                }
            }

            if (accepted.Count == 0)
            {
                selected = null!;
                return false;
            }

            var best = accepted
                .Where(a => !accepted.Any(o =>
                    !ReferenceEquals(o.Candidate, a.Candidate) && Dominates(o.Profile, a.Profile)))
                .ToList();
            selected = best[0].Candidate;
            if (best.Skip(1).Any(b => !b.Profile.SequenceEqual(best[0].Profile, StringComparer.Ordinal)))
            {
                ambiguous = best.Select(b => b.Candidate).ToList();
            }

            return true;
        }

        /// <summary>
        /// True when <paramref name="winner"/> converts strictly fewer arguments than
        /// <paramref name="other"/> and agrees with it on every argument it does convert.
        /// </summary>
        private static bool Dominates(List<string?> winner, List<string?> other)
        {
            if (winner.Count != other.Count)
            {
                return false;
            }

            var strictlyBetter = false;
            for (var i = 0; i < winner.Count; i++)
            {
                if (string.Equals(winner[i], other[i], StringComparison.Ordinal))
                {
                    continue;
                }

                if (winner[i] is null)
                {
                    strictlyBetter = true;
                    continue;
                }

                return false;
            }

            return strictlyBetter;
        }

        /// <summary>Human-readable overload list for the no-match diagnostic.</summary>
        public static string DescribeOverloads(
            string name,
            IEnumerable<ObjectMethodSymbol> signatures,
            Func<ObjectMethodSymbol, ITypeExpression, ICheckedType> resolveType) =>
            string.Join("; ", signatures.Select(s => Describe(
                name, s.Parameters, s.ReturnType, typeAst => resolveType(s, typeAst))));

        public static string DescribeOverloads(
            string name,
            IEnumerable<FunctionDeclarationSymbol> signatures,
            Func<FunctionDeclarationSymbol, ITypeExpression, ICheckedType> resolveType) =>
            string.Join("; ", signatures.Select(s => Describe(
                name, s.Parameters, s.ReturnType, typeAst => resolveType(s, typeAst))));

        private static string Describe(
            string name,
            IReadOnlyList<ParameterInfo> parameters,
            ITypeExpression? returnType,
            Func<ITypeExpression, ICheckedType> resolveType)
        {
            string Spell(ITypeExpression? type)
            {
                if (type is null)
                {
                    return "";
                }

                var resolved = resolveType(type);
                return TypeComparer.IsUnresolvedType(resolved)
                    ? PhpVersionGatedCallableSignature.SpellType(type)
                    : resolved.DisplayName;
            }

            var parts = parameters
                .Where(p => !string.Equals(p.Name, "$this", StringComparison.Ordinal))
                .Select(p =>
                {
                    var type = Spell(p.DeclaredType);
                    var prefix = p.IsVariadic ? "..." : "";
                    var text = type.Length > 0 ? type + " " + prefix + p.Name : prefix + p.Name;
                    return p.DefaultValue is not null ? text + " = …" : text;
                });
            var ret = Spell(returnType);
            return name + "(" + string.Join(", ", parts) + ")" + (ret.Length > 0 ? ": " + ret : "");
        }

        /// <summary>
        /// True when a callable facet (one declared signature of an overload-set value, or one
        /// arity sibling of it) accepts the arguments: every positional and named argument fits its
        /// slot, a variadic tail takes the rest, every slot is covered unless a spread is present.
        /// </summary>
        public static bool FacetAccepts(
            CallableCheckedType facet,
            IReadOnlyList<PhpArgumentAst> args,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IExpression, ICheckedType> inferArgumentType)
        {
            if (facet.IsAnyArity)
            {
                return false;
            }

            var count = facet.ParameterTypes.Count;
            var covered = new bool[count];
            var hasSpread = false;
            var positional = 0;
            foreach (var arg in args)
            {
                if (arg.IsVariadic)
                {
                    hasSpread = true;
                    continue;
                }

                int slot;
                if (arg.Name is not null)
                {
                    slot = CallableArityFacetBuilder.FindParameterIndexByName(facet, arg.Name.ValueString);
                    if (slot < 0)
                    {
                        return false;
                    }
                }
                else
                {
                    slot = positional++;
                    if (slot >= count)
                    {
                        if (!facet.LastParameterIsVariadic || count == 0)
                        {
                            return false;
                        }

                        slot = count - 1;
                    }
                }

                covered[slot] = true;
                if (arg.Expression is not null
                    && !ArgumentAccepted(
                        arg.Expression,
                        facet.ParameterTypes[slot],
                        state,
                        symbolTree,
                        globalScope,
                        inferArgumentType,
                        null))
                {
                    return false;
                }
            }

            if (hasSpread)
            {
                return true;
            }

            for (var i = 0; i < count; i++)
            {
                if (!covered[i] && !(facet.LastParameterIsVariadic && i == count - 1))
                {
                    return false;
                }
            }

            return true;
        }

        private static ICheckedType ResolveExpected(
            ICheckedType expected,
            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings,
            SymbolTree symbolTree,
            GlobalScope globalScope) =>
            bindings is { Count: > 0 }
                ? TypeComparer.ResolveGenericTypeBySymbol(expected, WidenLiteralBindings(bindings), symbolTree, globalScope)
                : expected;

        /// <summary>
        /// A type parameter inferred from a literal argument (<c>'a'</c>) stands for the literal's
        /// base type, so a second argument for the same parameter (<c>'b'</c>) still fits.
        /// </summary>
        private static Dictionary<GenericTypeParameterSymbol, ICheckedType> WidenLiteralBindings(
            Dictionary<GenericTypeParameterSymbol, ICheckedType> bindings) =>
            bindings.ToDictionary(pair => pair.Key, pair => GeneratorBodyInference.WidenLiteral(pair.Value));

        private static bool Applies(
            IReadOnlyList<ParameterInfo> parameters,
            IReadOnlyList<PhpArgumentAst> args,
            Func<ParameterInfo, ICheckedType> resolveExpected,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IExpression, ICheckedType> inferArgumentType,
            Func<ITypeExpression, ICheckedType>? resolveCallSiteType,
            List<string?>? conversions)
        {
            var restIndex = parameters.Count > 0 && parameters[^1].IsVariadic ? parameters.Count - 1 : -1;
            var covered = new HashSet<int>();
            var hasSpread = false;
            var positionalCount = 0;

            foreach (var arg in args)
            {
                // One entry per call argument: the type it is converted to, or null.
                conversions?.Add(null);
                if (arg.IsVariadic)
                {
                    hasSpread = true;
                    continue;
                }

                int index;
                if (arg.Name?.ValueString is { } named)
                {
                    index = -1;
                    for (var i = 0; i < parameters.Count; i++)
                    {
                        if (string.Equals(
                                parameters[i].Name.TrimStart('$'),
                                named.TrimStart('$'),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }

                    if (index < 0)
                    {
                        return false;
                    }
                }
                else
                {
                    index = positionalCount++;
                    if (restIndex >= 0 && index >= restIndex)
                    {
                        index = restIndex;
                    }
                    else if (index >= parameters.Count)
                    {
                        return false;
                    }
                }

                covered.Add(index);
                var param = parameters[index];
                if (arg.Expression is null || param.DeclaredType is null)
                {
                    continue;
                }

                var expected = resolveExpected(param);
                if (ArgumentAccepted(
                        arg.Expression,
                        expected,
                        state,
                        symbolTree,
                        globalScope,
                        inferArgumentType,
                        resolveCallSiteType))
                {
                    continue;
                }

                // Pass 2 only: the argument may still reach the parameter through the implicit
                // `operator convert` a plain (non-overloaded) call would apply.
                if (conversions is not null
                    && TryConvertArgument(
                        arg.Expression, expected, symbolTree, globalScope, inferArgumentType, out var target))
                {
                    conversions[^1] = target;
                    continue;
                }

                return false;
            }

            if (hasSpread)
            {
                return true;
            }

            for (var i = 0; i < parameters.Count; i++)
            {
                if (parameters[i].IsVariadic || parameters[i].DefaultValue is not null || covered.Contains(i))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// True when <paramref name="expression"/> is accepted by <paramref name="expected"/> only
        /// through an implicit <c>operator convert</c> (<see cref="TypeComparer.IsAssignableViaOperatorConvert"/>,
        /// the rule every ordinary call argument follows); <paramref name="target"/> is the single
        /// type it is converted to. Closure literals are never converted.
        /// </summary>
        private static bool TryConvertArgument(
            IExpression expression,
            ICheckedType expected,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IExpression, ICheckedType> inferArgumentType,
            out string? target)
        {
            target = null;
            if (expression is PhpInlineFunctionAst)
            {
                return false;
            }

            var actual = inferArgumentType(expression);
            if (!TypeComparer.IsAssignableViaOperatorConvert(actual, expected, symbolTree, globalScope)
                || !TypeComparer.TryGetConvertTargetKey(expected, out var key))
            {
                return false;
            }

            target = key;
            return true;
        }

        private static bool ArgumentAccepted(
            IExpression expression,
            ICheckedType expected,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IExpression, ICheckedType> inferArgumentType,
            Func<ITypeExpression, ICheckedType>? resolveCallSiteType)
        {
            if (expected is UnionCheckedType union)
            {
                return union.Members.Any(member => ArgumentAccepted(
                    expression, member, state, symbolTree, globalScope, inferArgumentType, resolveCallSiteType));
            }

            if (expected is NullableCheckedType nullable)
            {
                // `?T` accepts `null` as well as T.
                return IsNullLiteral(expression)
                    || ArgumentAccepted(
                        expression, nullable.InnerType, state, symbolTree, globalScope, inferArgumentType, resolveCallSiteType);
            }

            if (TypeComparer.IsUnresolvedType(expected) || TypeComparer.IsMixedType(expected))
            {
                return true;
            }

            var hasOpenGeneric = CallableGenericInference.ContainsUnboundGeneric(expected);
            if (expression is PhpInlineFunctionAst closure)
            {
                if (expected is CallableCheckedType callable)
                {
                    return ClosureAccepted(closure, callable, symbolTree, globalScope, resolveCallSiteType);
                }

                // An inline `fn` is also written for an `Expression<callable(…): R>` /
                // `PropertyPath<…>` parameter: the emitter turns it into the tree, so it is matched
                // on the callable shape those types carry.
                if (ExpressionTreeSupport.TryMapToCallable(expected, out var expressionCallable)
                    || PropertyPathSupport.TryMapToCallable(expected, out expressionCallable))
                {
                    return ClosureAccepted(closure, expressionCallable, symbolTree, globalScope, resolveCallSiteType);
                }

                return hasOpenGeneric || IsClosureHolderType(expected);
            }

            if (hasOpenGeneric)
            {
                // An open generic (`T`, `callable(int): TResult`) is inferred from the argument.
                return true;
            }

            if (FunctionOverloadSelector.IntegerFlagBitsCoverParameter(
                    expression, expected, state, symbolTree, globalScope))
            {
                return true;
            }

            var actual = inferArgumentType(expression);
            return SymbolNameTypeAssignability.IsAssignableTo(actual, expected, symbolTree, globalScope, state);
        }

        private static bool IsNullLiteral(IExpression expression)
        {
            if (expression is PhpNameAst name)
            {
                return string.Equals(name.ValueString ?? name.Identifier, "null", StringComparison.OrdinalIgnoreCase);
            }

            return expression is PhpDereferenceableAst { Suffix: null, Base: PhpNameAst baseName }
                && string.Equals(baseName.ValueString ?? baseName.Identifier, "null", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Non-callable-shape types a closure literal may still be passed as.</summary>
        private static bool IsClosureHolderType(ICheckedType expected)
        {
            var name = expected.DisplayName.TrimStart('\\');
            return string.Equals(name, "Closure", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "callable", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "object", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ClosureAccepted(
            PhpInlineFunctionAst closure,
            CallableCheckedType callable,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<ITypeExpression, ICheckedType>? resolveCallSiteType)
        {
            var closureParameters = closure.Parameters?.GetAllNotNull().ToList() ?? [];
            var required = closureParameters.Count(p => !p.IsVariadic && p.DefaultValue is null);
            if (required > callable.ParameterTypes.Count)
            {
                return false;
            }

            if (resolveCallSiteType is null)
            {
                return true;
            }

            var pairs = Math.Min(closureParameters.Count, callable.ParameterTypes.Count);
            for (var i = 0; i < pairs; i++)
            {
                if (closureParameters[i].Type is not { } declared
                    || CallableGenericInference.ContainsUnboundGeneric(callable.ParameterTypes[i]))
                {
                    continue;
                }

                var closureParam = resolveCallSiteType(declared);
                if (TypeComparer.IsUnresolvedType(closureParam)
                    || !TypeComparer.IsAssignableTo(callable.ParameterTypes[i], closureParam, symbolTree, globalScope))
                {
                    return false;
                }
            }

            if (closure.ReturnType is { } returnType
                && !CallableGenericInference.ContainsUnboundGeneric(callable.ReturnType))
            {
                var closureReturn = resolveCallSiteType(returnType);
                if (!TypeComparer.IsUnresolvedType(closureReturn)
                    && !TypeComparer.IsUnresolvedType(callable.ReturnType)
                    && !TypeComparer.IsAssignableTo(closureReturn, callable.ReturnType, symbolTree, globalScope))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
