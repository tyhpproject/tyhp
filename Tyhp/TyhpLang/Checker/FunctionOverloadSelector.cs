using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Checker
{
    /// <summary>
    /// Call-site tyhpdef overload pick: arity first, then argument/parameter compatibility
    /// among same-arity candidates. Distinguishes <c>__CallableParametersStruct</c> vs
    /// <c>__CallableParametersTuple</c> bags (and other same-arity type differences) so
    /// <c>call_user_func_array</c> named vs positional arrays select the matching signature.
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
        }

        public static FunctionDeclarationSymbol Select(
            FunctionDeclarationSymbol primary,
            PhpCallAst? call,
            Context? typeContext)
        {
            var aritySelected = CheckerHelpers.SelectFunctionOverloadForCall(primary, call?.Arguments);
            if (primary.Overloads.Count == 0 || call?.Arguments is null || typeContext is null)
            {
                return aritySelected;
            }

            var args = call.Arguments.GetAllNotNull().ToList();
            if (args.Count == 0 || args.Any(a => a.IsVariadic))
            {
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

            FunctionDeclarationSymbol? best = null;
            var bestScore = int.MinValue;
            foreach (var candidate in arityMatches)
            {
                var score = Score(candidate, call, args, typeContext);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best ?? aritySelected;
        }

        private static int Score(
            FunctionDeclarationSymbol candidate,
            PhpCallAst call,
            IReadOnlyList<PhpArgumentAst> args,
            Context context)
        {
            Dictionary<GenericTypeParameterSymbol, ICheckedType>? bindings = null;
            if (candidate.GenericParameters.Count > 0)
            {
                bindings = context.InferBindings(candidate, call);
            }

            var score = 0;
            var positionalIndex = 0;
            var parameters = candidate.Parameters;
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

                if (param?.DeclaredType is null)
                {
                    continue;
                }

                var expected = context.ResolveParameterType(candidate, param.DeclaredType);
                expected = ApplyCallableUtilityBindings(
                    expected, bindings, context.SymbolTree, context.GlobalScope);

                if (UtilityTypeResolver.TryGetCallableParametersRest(expected, out _))
                {
                    score += 10;
                    break;
                }

                score += ScoreArgument(arg.Expression, expected, context);
            }

            return score;
        }

        private static int ScoreArgument(
            IExpression expression,
            ICheckedType expected,
            Context context)
        {
            expected = UnwrapNullable(expected);
            var bagScore = ScoreBagLiteral(expression, expected);
            if (bagScore is not null)
            {
                return bagScore.Value;
            }

            var actual = UnwrapNullable(context.InferArgumentType(expression));
            var actualShape = TypeComparer.TryGetStructShapeForAssignability(
                actual, context.SymbolTree, context.GlobalScope);
            var expectedShape = TypeComparer.TryGetStructShapeForAssignability(
                expected, context.SymbolTree, context.GlobalScope);
            if (actualShape is not null && expectedShape is not null)
            {
                if (actualShape.HasIntegerKeyAliases == expectedShape.HasIntegerKeyAliases)
                {
                    return 80;
                }

                return -80;
            }

            if (SymbolNameTypeAssignability.IsAssignableTo(
                    actual, expected, context.SymbolTree, context.GlobalScope, context.State))
            {
                return 10;
            }

            return -20;
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
                    && utility.Behavior is UtilityBehavior.CallableParametersStruct
                        or UtilityBehavior.CallableParametersTuple
                        or UtilityBehavior.CallableParametersRest
                        or UtilityBehavior.CallableReturnType
                        or UtilityBehavior.ReturnType)
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
                StructCheckedType s =>
                    s.Properties.Values.Any(p => ContainsDeferredCallableUtility(p.Type)),
                _ => false,
            };
        }
    }
}
