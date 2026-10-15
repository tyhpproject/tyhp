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
    /// Call-site tyhpdef overload pick: arity first, then argument/parameter compatibility
    /// among same-arity candidates. Distinguishes <c>__CallableParametersStruct</c> vs
    /// <c>__CallableParametersTuple</c> bags (and other same-arity type differences) so
    /// <c>call_user_func_array</c> named vs positional arrays select the matching signature.
    /// Omitted optionals are scored as the implementation's default value so a literal-typed
    /// default (<c>0 $format = 0</c>) wins over the <c>int</c> catch-all when the call omits
    /// that argument, including zero-argument calls such as <c>wordCount()</c>.
    /// A bitwise <c>|</c>/<c>&amp;</c>/<c>^</c>/<c>~</c> of known integer constants matches a
    /// positive integer-literal parameter when every bit of that literal is set; the literal
    /// with the most of those bits wins, and two equal matches fall through to <c>int</c>.
    /// </summary>
    internal static class FunctionOverloadSelector
    {
        internal sealed class Context
        {
            public required CheckerState State { get; init; }

            public required SymbolTree SymbolTree { get; init; }

            public required GlobalScope GlobalScope { get; init; }

            public required Func<IExpression, ICheckedType> InferArgumentType { get; init; }

            public required Func<FunctionDeclarationSymbol, ITypeExpression, ICheckedType>
                ResolveParameterType
            { get; init; }

            public required Func<
                FunctionDeclarationSymbol,
                PhpCallAst,
                Dictionary<GenericTypeParameterSymbol, ICheckedType>?> InferBindings
            { get; init; }

            /// <summary>
            /// Resolves a type annotation written at the call site (a closure argument's
            /// parameter or return type). Used only to match closure arguments against
            /// <c>.tyhp</c> overload signatures; without it closures are matched on parameter count.
            /// </summary>
            public Func<ITypeExpression, ICheckedType>? ResolveCallSiteType { get; init; }
        }

        internal sealed class MethodContext
        {
            public required CheckerState State { get; init; }

            public required SymbolTree SymbolTree { get; init; }

            public required GlobalScope GlobalScope { get; init; }

            public required Func<IExpression, ICheckedType> InferArgumentType { get; init; }

            public required Func<ObjectMethodSymbol, ITypeExpression, ICheckedType>
                ResolveParameterType
            { get; init; }

            public required Func<
                ObjectMethodSymbol,
                PhpCallAst,
                Dictionary<GenericTypeParameterSymbol, ICheckedType>?> InferBindings
            { get; init; }

            /// <inheritdoc cref="Context.ResolveCallSiteType"/>
            public Func<ITypeExpression, ICheckedType>? ResolveCallSiteType { get; init; }
        }

        /// <summary>
        /// True when <paramref name="primary"/> heads a <c>.tyhp</c> overload set and none of its
        /// overload signatures accepts the call.
        /// </summary>
        public static bool HasNoApplicableOverload(
            FunctionDeclarationSymbol primary,
            PhpCallAst call,
            Context typeContext)
        {
            if (!SourceOverloadSelector.IsSourceSet(primary))
            {
                return false;
            }

            SourceOverloadSelector.SelectFunction(primary, call, typeContext, out var matched);
            return !matched;
        }

        /// <inheritdoc cref="HasNoApplicableOverload(FunctionDeclarationSymbol, PhpCallAst, Context)"/>
        public static bool HasNoApplicableOverload(
            ObjectMethodSymbol primary,
            PhpCallAst call,
            MethodContext typeContext)
        {
            if (!SourceOverloadSelector.IsSourceSet(primary))
            {
                return false;
            }

            SourceOverloadSelector.SelectMethod(primary, call, typeContext, out var matched);
            return !matched;
        }

        /// <summary>
        /// The signatures of a <c>.tyhp</c> overload set that a call needing implicit
        /// <c>operator convert</c> cannot choose between (none accepts it as written, and the
        /// undominated ones convert arguments to different types); null otherwise.
        /// </summary>
        public static IReadOnlyList<FunctionDeclarationSymbol>? FindAmbiguousOverloads(
            FunctionDeclarationSymbol primary,
            PhpCallAst call,
            Context typeContext)
        {
            if (!SourceOverloadSelector.IsSourceSet(primary))
            {
                return null;
            }

            SourceOverloadSelector.SelectFunction(primary, call, typeContext, out _, out var ambiguous);
            return ambiguous;
        }

        /// <inheritdoc cref="FindAmbiguousOverloads(FunctionDeclarationSymbol, PhpCallAst, Context)"/>
        public static IReadOnlyList<ObjectMethodSymbol>? FindAmbiguousOverloads(
            ObjectMethodSymbol primary,
            PhpCallAst call,
            MethodContext typeContext)
        {
            if (!SourceOverloadSelector.IsSourceSet(primary))
            {
                return null;
            }

            SourceOverloadSelector.SelectMethod(primary, call, typeContext, out _, out var ambiguous);
            return ambiguous;
        }

        public static FunctionDeclarationSymbol Select(
            FunctionDeclarationSymbol primary,
            PhpCallAst? call,
            Context? typeContext)
        {
            if (SourceOverloadSelector.IsSourceSet(primary))
            {
                return SourceOverloadSelector.SelectFunction(primary, call, typeContext, out _);
            }

            var aritySelected = CheckerHelpers.SelectFunctionOverloadForCall(primary, call?.Arguments);
            if (primary.Overloads.Count == 0 || call is null || typeContext is null)
            {
                return aritySelected;
            }

            var args = call.Arguments?.GetAllNotNull().ToList() ?? [];
            if (args.Any(a => a.IsVariadic))
            {
                // Spreads are not a static arity; SelectFunctionOverloadForCall prefers a
                // trailing-variadic signature (e.g. max(T, T, T...) over max(array<T>)).
                return aritySelected;
            }

            var arityMatches = new List<FunctionDeclarationSymbol>();
            foreach (var candidate in CheckerHelpers.EnumerateFunctionSignatures(primary))
            {
                var (min, max) = CheckerHelpers.GetParameterArityRange(candidate.Parameters);
                if (args.Count >= min && args.Count <= max)
                {
                    arityMatches.Add(candidate);
                }
            }

            if (arityMatches.Count <= 1)
            {
                return arityMatches.Count == 1 ? arityMatches[0] : aritySelected;
            }

            var scored = new List<(FunctionDeclarationSymbol Candidate, ArgumentScore Score)>();
            foreach (var candidate in arityMatches)
            {
                scored.Add((candidate, Score(candidate, primary, call, args, typeContext)));
            }

            var best = PickScored(scored);

            // A scored winner that cannot accept the call (a closure passed where it declares
            // `null`, say) yields to the best-scoring signature that can.
            if (best is not null && !SourceOverloadSelector.Accepts(best, call, typeContext))
            {
                foreach (var (candidate, _) in scored.OrderByDescending(s => s.Score.Value))
                {
                    if (!ReferenceEquals(candidate, best)
                        && SourceOverloadSelector.Accepts(candidate, call, typeContext))
                    {
                        return candidate;
                    }
                }
            }

            return best ?? aritySelected;
        }

        /// <summary>
        /// Same-arity type pick among <see cref="ObjectMethodSymbol.Overloads"/>. Scores
        /// generic-parameter slots against their <em>constraint</em> (not the inferred argument
        /// type) so <c>bindTo($x, 'static')</c> selects <c>TNewScope extends 'static'</c> instead
        /// of the explicit <c>__ClosureScope</c> overload.
        /// </summary>
        public static ObjectMethodSymbol SelectMethod(
            ObjectMethodSymbol primary,
            PhpCallAst? call,
            MethodContext? typeContext)
        {
            if (SourceOverloadSelector.IsSourceSet(primary))
            {
                return SourceOverloadSelector.SelectMethod(primary, call, typeContext, out _);
            }

            var aritySelected = CheckerHelpers.SelectMethodOverloadForCall(primary, call?.Arguments);
            if (primary.Overloads.Count == 0 || call is null || typeContext is null)
            {
                return aritySelected;
            }

            var args = call.Arguments?.GetAllNotNull().ToList() ?? [];
            if (args.Any(a => a.IsVariadic))
            {
                return aritySelected;
            }

            var arityMatches = new List<ObjectMethodSymbol>();
            foreach (var candidate in CheckerHelpers.EnumerateMethodSignatures(primary))
            {
                var (min, max) = CheckerHelpers.GetParameterArityRange(
                    CheckerHelpers.GetCallSiteParameters(candidate));
                if (args.Count >= min && args.Count <= max)
                {
                    arityMatches.Add(candidate);
                }
            }

            if (arityMatches.Count <= 1)
            {
                return arityMatches.Count == 1 ? arityMatches[0] : aritySelected;
            }

            var scored = new List<(ObjectMethodSymbol Candidate, ArgumentScore Score)>();
            foreach (var candidate in arityMatches)
            {
                scored.Add((candidate, ScoreMethod(candidate, primary, call, args, typeContext)));
            }

            var best = PickScored(scored);

            // A scored winner that cannot accept the call (a closure passed where it declares
            // `null`, say) yields to the best-scoring signature that can.
            if (best is not null && !SourceOverloadSelector.Accepts(best, call, typeContext))
            {
                foreach (var (candidate, _) in scored.OrderByDescending(s => s.Score.Value))
                {
                    if (!ReferenceEquals(candidate, best)
                        && SourceOverloadSelector.Accepts(candidate, call, typeContext))
                    {
                        return candidate;
                    }
                }
            }

            return best ?? aritySelected;
        }

        /// <summary>
        /// One argument/parameter contribution. <see cref="BitContains"/> is set when the
        /// points came from a bitmask superset rather than an exact literal or ordinary
        /// assignability, so a tie between two masks can fall through to <c>int</c>.
        /// </summary>
        private readonly struct ArgumentScore
        {
            public int Value { get; init; }

            public bool BitContains { get; init; }

            public static ArgumentScore From(int value) => new() { Value = value };

            public ArgumentScore Add(int value) => new()
            {
                Value = Value + value,
                BitContains = BitContains,
            };

            public ArgumentScore Add(ArgumentScore other) => new()
            {
                Value = Value + other.Value,
                BitContains = BitContains || other.BitContains,
            };
        }

        /// <summary>
        /// Highest score wins. When several bitmask matches tie, and a signature that did
        /// not rely on a bitmask scored lower, that wider signature is used instead.
        /// An exact literal tied with a bitmask of the same score keeps the exact literal.
        /// </summary>
        private static TSymbol? PickScored<TSymbol>(
            List<(TSymbol Candidate, ArgumentScore Score)> scored)
            where TSymbol : class
        {
            if (scored.Count == 0)
            {
                return null;
            }

            var bestValue = int.MinValue;
            foreach (var entry in scored)
            {
                if (entry.Score.Value > bestValue)
                {
                    bestValue = entry.Score.Value;
                }
            }

            var winners = new List<(TSymbol Candidate, ArgumentScore Score)>();
            foreach (var entry in scored)
            {
                if (entry.Score.Value == bestValue)
                {
                    winners.Add(entry);
                }
            }

            if (winners.Count > 1 && winners.TrueForAll(winner => winner.Score.BitContains))
            {
                TSymbol? fallback = null;
                var fallbackScore = int.MinValue;
                foreach (var entry in scored)
                {
                    if (entry.Score.BitContains || entry.Score.Value >= bestValue)
                    {
                        continue;
                    }

                    if (entry.Score.Value > fallbackScore)
                    {
                        fallbackScore = entry.Score.Value;
                        fallback = entry.Candidate;
                    }
                }

                if (fallback is not null)
                {
                    return fallback;
                }
            }

            foreach (var winner in winners)
            {
                if (!winner.Score.BitContains)
                {
                    return winner.Candidate;
                }
            }

            return winners[0].Candidate;
        }

        private static ArgumentScore Score(
            FunctionDeclarationSymbol candidate,
            FunctionDeclarationSymbol implementation,
            PhpCallAst call,
            IReadOnlyList<PhpArgumentAst> args,
            Context context)
        {
            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings = null;
            if (candidate.GenericParameters.Count > 0)
            {
                bindings = context.InferBindings(candidate, call);
            }

            var score = default(ArgumentScore);
            var positionalIndex = 0;
            var parameters = candidate.Parameters;
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var restIndex = parameters.Count > 0 && parameters[^1].IsVariadic
                ? parameters.Count - 1
                : -1;

            // 1-array `array_map` (exact arity, no variadic) must beat the zip
            // overlay when there is no extra array — zip can still enter the
            // arity set if Slice `TMin` was unread as 0.
            if (restIndex < 0 && parameters.Count == args.Count)
            {
                score = score.Add(80);
            }
            else if (restIndex >= 0 && args.Count <= restIndex)
            {
                score = score.Add(-40);
            }

            foreach (var arg in args)
            {
                if (arg.Expression is null)
                {
                    continue;
                }

                ParameterInfo? param = null;
                if (arg.Name?.ValueString is { } named)
                {
                    param = parameters.FirstOrDefault(p =>
                        string.Equals(
                            p.Name.TrimStart('$'),
                            named.TrimStart('$'),
                            StringComparison.OrdinalIgnoreCase));
                }
                else if (positionalIndex < parameters.Count)
                {
                    param = parameters[positionalIndex++];
                }
                else if (restIndex >= 0)
                {
                    param = parameters[restIndex];
                }

                if (param is not null)
                {
                    usedNames.Add(NormalizeParamName(param.Name));
                }

                if (param?.DeclaredType is null)
                {
                    continue;
                }

                var expected = context.ResolveParameterType(candidate, param.DeclaredType);
                expected = ApplyCallableUtilityBindings(
                    expected, bindings, context.SymbolTree, context.GlobalScope);

                if (UtilityTypeResolver.TryGetCallableParametersRest(expected, out _))
                {
                    score = score.Add(10);
                    break;
                }

                score = score.Add(ScoreValue(
                    arg.Expression,
                    expected,
                    context.State,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.InferArgumentType));
            }

            score = score.Add(ScoreOmittedOptionals(
                parameters,
                implementation.Parameters,
                usedNames,
                param =>
                {
                    if (param.DeclaredType is null)
                    {
                        return CheckedTypes.Unresolved;
                    }

                    return ApplyCallableUtilityBindings(
                        context.ResolveParameterType(candidate, param.DeclaredType),
                        bindings,
                        context.SymbolTree,
                        context.GlobalScope);
                },
                context.State,
                context.SymbolTree,
                context.GlobalScope,
                context.InferArgumentType));

            // Prefer the type-guard return so `\is_callable($x)` uses the overlay that
            // narrows, not the catch-all `: bool`, when omitted-default scoring ties.
            if (candidate.ReturnType is TyhpReturnTypeGuardAst)
            {
                score = score.Add(5);
            }

            return score;
        }

        private static ArgumentScore ScoreMethod(
            ObjectMethodSymbol candidate,
            ObjectMethodSymbol implementation,
            PhpCallAst call,
            IReadOnlyList<PhpArgumentAst> args,
            MethodContext context)
        {
            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings = null;
            if (candidate.GenericParameters.Count > 0)
            {
                bindings = context.InferBindings(candidate, call);
            }

            var score = default(ArgumentScore);
            var positionalIndex = 0;
            var parameters = CheckerHelpers.GetCallSiteParameters(candidate);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var restIndex = parameters.Count > 0 && parameters[^1].IsVariadic
                ? parameters.Count - 1
                : -1;

            foreach (var arg in args)
            {
                if (arg.Expression is null)
                {
                    continue;
                }

                ParameterInfo? param = null;
                if (arg.Name?.ValueString is { } named)
                {
                    param = parameters.FirstOrDefault(p =>
                        string.Equals(
                            p.Name.TrimStart('$'),
                            named.TrimStart('$'),
                            StringComparison.OrdinalIgnoreCase));
                }
                else if (positionalIndex < parameters.Count)
                {
                    param = parameters[positionalIndex++];
                }
                else if (restIndex >= 0)
                {
                    param = parameters[restIndex];
                }

                if (param is not null)
                {
                    usedNames.Add(NormalizeParamName(param.Name));
                }

                if (param?.DeclaredType is null)
                {
                    continue;
                }

                var expected = ExpectedForConstraintScoring(
                    context.ResolveParameterType(candidate, param.DeclaredType),
                    candidate.GenericParameters,
                    bindings,
                    typeAst => context.ResolveParameterType(candidate, typeAst),
                    context.SymbolTree,
                    context.GlobalScope);

                if (UtilityTypeResolver.TryGetCallableParametersRest(expected, out _))
                {
                    score = score.Add(10);
                    break;
                }

                score = score.Add(ScoreValue(
                    arg.Expression,
                    expected,
                    context.State,
                    context.SymbolTree,
                    context.GlobalScope,
                    context.InferArgumentType));
            }

            score = score.Add(ScoreOmittedOptionals(
                parameters,
                CheckerHelpers.GetCallSiteParameters(implementation),
                usedNames,
                param =>
                {
                    if (param.DeclaredType is null)
                    {
                        return CheckedTypes.Unresolved;
                    }

                    return ExpectedForConstraintScoring(
                        context.ResolveParameterType(candidate, param.DeclaredType),
                        candidate.GenericParameters,
                        bindings,
                        typeAst => context.ResolveParameterType(candidate, typeAst),
                        context.SymbolTree,
                        context.GlobalScope);
                },
                context.State,
                context.SymbolTree,
                context.GlobalScope,
                context.InferArgumentType));

            return score;
        }

        /// <summary>
        /// Score trailing optionals the call omitted. Runtime uses the
        /// <paramref name="implementationParams"/> defaults (only that body is emitted), so
        /// <c>wordCount()</c> is scored as if the implementation's <c>$format = 0</c> were
        /// passed — selecting <c>0 $format = 0</c> over the <c>int</c> catch-all.
        /// </summary>
        private static ArgumentScore ScoreOmittedOptionals(
            IReadOnlyList<ParameterInfo> candidateParams,
            IReadOnlyList<ParameterInfo> implementationParams,
            HashSet<string> usedNames,
            Func<ParameterInfo, ICheckedType> resolveExpected,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IExpression, ICheckedType> inferArgumentType)
        {
            var score = default(ArgumentScore);
            for (var i = 0; i < candidateParams.Count; i++)
            {
                var param = candidateParams[i];
                if (param.IsVariadic
                    || param.DeclaredType is null
                    || usedNames.Contains(NormalizeParamName(param.Name)))
                {
                    continue;
                }

                var defaultExpr = FindImplementationDefault(implementationParams, param, i)
                    ?? param.DefaultValue;
                if (defaultExpr is null)
                {
                    continue;
                }

                var expected = resolveExpected(param);
                if (UtilityTypeResolver.TryGetCallableParametersRest(expected, out _))
                {
                    continue;
                }

                score = score.Add(ScoreValue(
                    defaultExpr,
                    expected,
                    state,
                    symbolTree,
                    globalScope,
                    inferArgumentType));
            }

            return score;
        }

        private static IExpression? FindImplementationDefault(
            IReadOnlyList<ParameterInfo> implementationParams,
            ParameterInfo candidateParam,
            int index)
        {
            var name = NormalizeParamName(candidateParam.Name);
            foreach (var impl in implementationParams)
            {
                if (string.Equals(NormalizeParamName(impl.Name), name, StringComparison.OrdinalIgnoreCase))
                {
                    return impl.DefaultValue;
                }
            }

            return index < implementationParams.Count
                ? implementationParams[index].DefaultValue
                : null;
        }

        private static string NormalizeParamName(string name) => name.TrimStart('$');

        /// <summary>
        /// When the declared parameter type is a callee generic, score against that parameter's
        /// constraint (with sibling bindings) rather than the inferred argument type. Otherwise
        /// <c>TNewScope</c> infers as <c>'static'</c> on both bind overloads and the pick ties.
        /// </summary>
        private static ICheckedType ExpectedForConstraintScoring(
            ICheckedType expected,
            IReadOnlyList<GenericTypeParameterSymbol> genericParameters,
            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings,
            Func<ITypeExpression, ICheckedType> resolveType,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (expected is SimpleCheckedType { ResolvedSymbol: GenericTypeParameterSymbol gp }
                && genericParameters.Any(p => ReferenceEquals(p, gp))
                && gp.Constraint is not null)
            {
                var constraint = resolveType(gp.Constraint);
                if (bindings is { Count: > 0 })
                {
                    var siblings = new Dictionary<GenericTypeParameterSymbol, ICheckedType>();
                    foreach (var pair in bindings)
                    {
                        if (!ReferenceEquals(pair.Key, gp))
                        {
                            siblings[pair.Key] = pair.Value;
                        }
                    }

                    if (siblings.Count > 0)
                    {
                        constraint = TypeComparer.ResolveGenericTypeBySymbol(
                            constraint, siblings, symbolTree, globalScope);
                    }
                }

                return constraint;
            }

            if (bindings is { Count: > 0 })
            {
                return TypeComparer.ResolveGenericTypeBySymbol(
                    expected, bindings, symbolTree, globalScope);
            }

            return expected;
        }

        private static ArgumentScore ScoreValue(
            IExpression expression,
            ICheckedType expected,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            Func<IExpression, ICheckedType> inferArgumentType)
        {
            expected = UnwrapNullable(expected);
            var bagScore = ScoreBagLiteral(expression, expected);
            if (bagScore is not null)
            {
                return ArgumentScore.From(bagScore.Value);
            }

            var actual = UnwrapNullable(inferArgumentType(expression));
            var fromBitwise = false;
            if (TryFoldIntegerExpression(
                    expression, state, symbolTree, globalScope, out var folded, out fromBitwise))
            {
                actual = new LiteralCheckedType(
                    folded,
                    new SimpleCheckedType(new BuiltInTypeSymbol("int")));
            }
            else if (TryGetScoringIntLiteral(expression, state, symbolTree, globalScope, out var scoringLiteral))
            {
                actual = UnwrapNullable(scoringLiteral);
            }

            if (TyhpdefConstIntLiteral.TryGetIntegerLiteralValue(actual, out var actualInt)
                && TyhpdefConstIntLiteral.TryGetIntegerLiteralValue(expected, out var expectedInt)
                && actualInt == expectedInt)
            {
                return ArgumentScore.From(50);
            }

            // A bitwise combination of known ints is not the literal itself. It still
            // selects a positive literal parameter when every bit of that literal is set
            // (`JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES` → `4194304 $flags`).
            // More bits score higher. A tie between two such masks is dropped later so
            // the plain `int` signature wins.
            if (fromBitwise
                && TyhpdefConstIntLiteral.TryGetIntegerLiteralValue(expected, out var mask)
                && mask > 0
                && TyhpdefConstIntLiteral.TryGetIntegerLiteralValue(actual, out var combined)
                && (combined & mask) == mask)
            {
                var bits = System.Numerics.BitOperations.PopCount(unchecked((ulong)mask));
                return new ArgumentScore { Value = 30 + bits, BitContains = true };
            }

            if (TypeComparer.AreTypesEqual(actual, expected))
            {
                return ArgumentScore.From(50);
            }
            var actualShape = TypeComparer.TryGetStructShapeForAssignability(
                actual, symbolTree, globalScope);
            var expectedShape = TypeComparer.TryGetStructShapeForAssignability(
                expected, symbolTree, globalScope);
            if (actualShape is not null && expectedShape is not null)
            {
                if (actualShape.HasIntegerKeyAliases == expectedShape.HasIntegerKeyAliases)
                {
                    return ArgumentScore.From(80);
                }

                return ArgumentScore.From(-80);
            }

            if (ClosureBindSupport.IsStaticScopeSentinel(actual)
                && ClosureBindSupport.IsStaticScopeSentinel(expected))
            {
                return ArgumentScore.From(100);
            }

            if (SymbolNameTypeAssignability.IsAssignableTo(
                    actual, expected, symbolTree, globalScope, state))
            {
                return ArgumentScore.From(10);
            }

            return ArgumentScore.From(-20);
        }

        /// <summary>
        /// True when <paramref name="expression"/> is a bitwise combination of known
        /// integers and every bit of a positive integer-literal <paramref name="expected"/>
        /// is set. Used so the selected literal overload is accepted as an argument,
        /// not only preferred while scoring.
        /// </summary>
        internal static bool IntegerFlagBitsCoverParameter(
            IExpression expression,
            ICheckedType expected,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            expected = UnwrapNullable(expected);
            if (!TyhpdefConstIntLiteral.TryGetIntegerLiteralValue(expected, out var mask) || mask <= 0)
            {
                return false;
            }

            return TryFoldIntegerExpression(
                    expression, state, symbolTree, globalScope, out var actual, out var fromBitwise)
                && fromBitwise
                && (actual & mask) == mask;
        }

        private static ICheckedType UnwrapNullable(ICheckedType type) =>
            type is NullableCheckedType nullable
                ? UnwrapNullable(nullable.InnerType)
                : type;

        private static int? ScoreBagLiteral(IExpression expression, ICheckedType expected)
        {
            expected = UnwrapNullable(expected);
            if (expected is not StructCheckedType structType)
            {
                return null;
            }

            return StructBagLiteralChecker.Classify(expression) switch
            {
                StructBagLiteralChecker.LiteralShape.NotALiteral => null,
                StructBagLiteralChecker.LiteralShape.Empty => 40,
                StructBagLiteralChecker.LiteralShape.Positional =>
                    structType.HasIntegerKeyAliases ? 100 : -100,
                StructBagLiteralChecker.LiteralShape.Named =>
                    structType.HasIntegerKeyAliases ? -100 : 100,
                StructBagLiteralChecker.LiteralShape.Other => 0,
                _ => null,
            };
        }

        private static bool TryFoldIntegerExpression(
            IExpression? expression,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out long value,
            out bool fromBitwise)
        {
            value = 0;
            fromBitwise = false;
            while (expression is PhpDereferenceableExpressionAst { Expression: IExpression inner })
            {
                expression = inner;
            }

            if (expression is null)
            {
                return false;
            }

            if (expression is PhpUnaryOpAst { Operand: { } unaryOperand })
            {
                var op = expression is PhpUnaryOpAst unary ? unary.Operator?.ValueString : null;
                if (op is "~" or "+" or "-")
                {
                    if (!TryFoldIntegerExpression(
                            unaryOperand, state, symbolTree, globalScope, out var innerValue, out var innerBitwise))
                    {
                        return false;
                    }

                    value = op switch
                    {
                        "~" => ~innerValue,
                        "-" => -innerValue,
                        _ => innerValue,
                    };
                    fromBitwise = op == "~" || innerBitwise;
                    return true;
                }
            }

            if (expression is PhpBinaryOpAst { Left: { } left, Right: { } right } binary)
            {
                var op = PhpBinaryOperatorExtensions.FromToken(
                    binary.Operator?.TokenValue ?? 0,
                    binary.Operator?.ValueString);
                if (op is PhpBinaryOperator.BitwiseOr
                    or PhpBinaryOperator.BitwiseAnd
                    or PhpBinaryOperator.BitwiseXor)
                {
                    if (!TryFoldIntegerExpression(left, state, symbolTree, globalScope, out var leftValue, out _)
                        || !TryFoldIntegerExpression(right, state, symbolTree, globalScope, out var rightValue, out _))
                    {
                        return false;
                    }

                    value = op switch
                    {
                        PhpBinaryOperator.BitwiseOr => leftValue | rightValue,
                        PhpBinaryOperator.BitwiseAnd => leftValue & rightValue,
                        _ => leftValue ^ rightValue,
                    };
                    fromBitwise = true;
                    return true;
                }
            }

            if (TyhpdefConstIntLiteral.TryGetIntegerFromExpression(expression, out value))
            {
                return true;
            }

            if (TryGetScoringIntLiteral(expression, state, symbolTree, globalScope, out var literal)
                && TyhpdefConstIntLiteral.TryGetIntegerLiteralValue(literal, out value))
            {
                return true;
            }

            return false;
        }

        private static bool TryGetScoringIntLiteral(
            IExpression expression,
            CheckerState state,
            SymbolTree symbolTree,
            GlobalScope globalScope,
            out ICheckedType literal)
        {
            literal = CheckedTypes.Unresolved;
            while (expression is PhpDereferenceableExpressionAst { Expression: IExpression inner })
            {
                expression = inner;
            }

            if (TyhpdefConstIntLiteral.TryGetIntegerFromExpression(expression, out var fromAst))
            {
                literal = new LiteralCheckedType(
                    fromAst,
                    new SimpleCheckedType(new BuiltInTypeSymbol("int")));
                return true;
            }

            var name = expression as PhpNameAst
                ?? (expression as PhpDereferenceableAst is { Suffix: null, Base: PhpNameAst baseName }
                    ? baseName
                    : null);
            if (name is null)
            {
                return TyhpdefConstIntLiteral.TryGetLiteralType(expression.BoundSymbol, out literal);
            }

            var symbol = (IBaseSymbol?)CheckerHelpers.ResolveFreeConstant(
                    name, state, symbolTree, globalScope)
                ?? expression.BoundSymbol
                ?? name.BoundSymbol;
            return TyhpdefConstIntLiteral.TryGetLiteralType(symbol, out literal);
        }

        private static ICheckedType ApplyCallableUtilityBindings(
            ICheckedType type,
            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings,
            SymbolTree symbolTree,
            GlobalScope globalScope)
        {
            if (bindings is not { Count: > 0 } || !ContainsDeferredCallableUtility(type))
            {
                return type;
            }

            return TypeComparer.ResolveGenericTypeBySymbol(type, bindings, symbolTree, globalScope);
        }

        private static bool ContainsDeferredCallableUtility(ICheckedType type)
        {
            if (type is GenericCheckedType generic)
            {
                if (SymbolNameTypeHelper.TryGetUtilitySymbol(generic.BaseType, out var utility)
                    && UtilityTypeResolver.IsDeferredExpandingUtility(utility.Behavior))
                {
                    return true;
                }

                return generic.TypeArguments.Any(ContainsDeferredCallableUtility);
            }

            return type switch
            {
                NullableCheckedType n => ContainsDeferredCallableUtility(n.InnerType),
                UnionCheckedType u => u.Members.Any(ContainsDeferredCallableUtility),
                IntersectionCheckedType i => i.Members.Any(ContainsDeferredCallableUtility),
                CallableCheckedType c =>
                    ContainsDeferredCallableUtility(c.ReturnType)
                    || c.ParameterTypes.Any(ContainsDeferredCallableUtility),
                ParameterPackCheckedType p =>
                    (p.SourceCallable is not null && ContainsDeferredCallableUtility(p.SourceCallable))
                    || p.Members.Any(ContainsDeferredCallableUtility),
                HomogeneousVariadicCheckedType h => ContainsDeferredCallableUtility(h.ElementType),
                StructCheckedType s =>
                    s.Properties.Values.Any(p => ContainsDeferredCallableUtility(p.Type)),
                _ => false,
            };
        }
    }
}
