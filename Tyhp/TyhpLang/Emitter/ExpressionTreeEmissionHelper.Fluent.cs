using System.Linq;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Emitter
{
    internal static partial class ExpressionTreeEmissionHelper
    {
        /// <summary>
        /// Builder methods. A property with one of these names is emitted as <c>prop('name')</c>.
        /// </summary>
        private static readonly HashSet<string> ChainMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            "and", "or", "xor", "then", "else", "nullSafe", "expr", "prop",
            "gte", "gt", "lte", "lt", "eq", "neq", "looseEq", "looseNeq",
            "same", "notSame", "isNull", "isNotNull", "coalesce", "call", "staticCall",
            "backedBy", "plus", "minus", "times", "dividedBy", "mod", "pow", "concat",
            "spaceship", "bitAnd", "bitOr", "bitXor", "shiftLeft", "shiftRight",
            "not", "neg", "pos", "bitNot", "value", "literal", "is", "at", "cast", "make",
        };

        private static readonly HashSet<string> CombinerMethods = new(StringComparer.Ordinal)
        {
            "and", "or", "xor", "then", "else", "coalesce", "expr",
        };

        private sealed class FluentChain
        {
            public IExpression Base { get; set; } = null!;
            public List<FluentStep> Steps { get; } = [];
            public string? RootName { get; set; }
            public int RootIndex { get; set; } = -1;
            public bool IsParamRoot { get; set; }
            public bool HasCombiner { get; set; }
        }

        private abstract class FluentStep;

        private sealed class PropStep : FluentStep
        {
            public string Name { get; init; } = "";
        }

        private sealed class CallStep : FluentStep
        {
            public string Method { get; init; } = "";
            public List<IExpression> Args { get; init; } = [];
        }

        /// <summary>
        /// The first node a rewrite could not express. Children are rewritten before their parent
        /// gives up, so this is the innermost failing node.
        /// </summary>
        private sealed class RewriteFailure
        {
            public IBase2Ast? Node { get; set; }

            public string? Kind { get; set; }
        }

        private sealed class RewriteScope
        {
            public RewriteFailure Failure { get; init; } = new();
            public Dictionary<string, ParameterInfo> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public bool IncludeIndex { get; init; }
            public ParameterInfo? Primary { get; init; }
            public IReadOnlyDictionary<IBase2Ast, ICheckedType>? ExpressionTypes { get; init; }
            public Func<string?, string?, string> FormatClassFqn { get; init; } = null!;
            public Func<ITypeExpression?, Binder.Symbols.Interfaces.IBaseSymbol?> ResolveTypeSymbol { get; init; } = null!;
            public Func<PhpInlineFunctionAst, InferredClosureSignature?>? GetInferredSignature { get; init; }
            public Base2Ast Context { get; init; } = null!;
        }

        private static FluentChain? RewriteFluent(
            IExpression body,
            Dictionary<string, ParameterInfo> parameters,
            IReadOnlyDictionary<IBase2Ast, Tyhp.TyhpLang.Checker.ICheckedType>? expressionTypes,
            Func<string?, string?, string> formatClassFqn,
            Func<ITypeExpression?, Binder.Symbols.Interfaces.IBaseSymbol?> resolveTypeSymbol,
            Func<PhpInlineFunctionAst, InferredClosureSignature?>? getInferredSignature,
            Base2Ast context,
            RewriteFailure failure)
        {
            var scope = new RewriteScope
            {
                Failure = failure,
                Parameters = parameters,
                IncludeIndex = parameters.Count > 1 || parameters.Values.Any(p => p.Index != 0),
                Primary = parameters.Count == 0
                    ? null
                    : parameters.Values.OrderBy(p => p.Index).First(),
                ExpressionTypes = expressionTypes,
                FormatClassFqn = formatClassFqn,
                ResolveTypeSymbol = resolveTypeSymbol,
                GetInferredSignature = getInferredSignature,
                Context = context,
            };
            return Rewrite(body, scope);
        }

        /// <summary>
        /// Records <paramref name="node"/> as the construct the tree cannot express (unless an inner
        /// node was already recorded) and returns null. <paramref name="kind"/> names the construct
        /// when the node's own description would be misleading.
        /// </summary>
        private static FluentChain? Fail(IBase2Ast? node, RewriteScope scope, string? kind = null)
        {
            if (scope.Failure.Node is null && node is not null)
            {
                scope.Failure.Node = node;
                scope.Failure.Kind = kind;
            }

            return null;
        }

        /// <summary>
        /// Returns a chain, or null when the node cannot be expressed.
        /// A raw value is anchored on the primary parameter (<c>param(...)->value(...)</c>)
        /// or on <c>Expr::literal</c> when the lambda has no parameters.
        /// </summary>
        private static FluentChain? Rewrite(IExpression? expression, RewriteScope scope)
        {
            var rewritten = RewriteCore(expression, scope);
            if (rewritten is null)
            {
                return null;
            }

            if (rewritten is FluentChain chain)
            {
                return chain;
            }

            return AnchorRaw((IExpression)rewritten, scope);
        }

        private static object? RewriteCore(IExpression? expression, RewriteScope scope)
        {
            if (expression is null)
            {
                return null;
            }

            if (expression is IBase2Ast node && RewrittenInlineFns.TryGetValue(node, out var sourceFn))
            {
                return RewriteNestedLambda(sourceFn, scope);
            }

            switch (expression)
            {
                case PhpDereferenceableExpressionAst paren when paren.Expression is IExpression inner:
                    return RewriteCore(inner, scope);

                case PhpVariableAst variable:
                    return RewriteVariableFluent(variable, scope);

                case PhpScalarAst:
                case PhpMagicConstantAst:
                case PhpEncapsListAst encapsLiteral when ExpressionTreeSupport.IsConstantStringLiteral(encapsLiteral):
                case PhpEncapsStringAst:
                case PhpNameAst literalName when ExpressionTreeSupport.IsLiteralConstantName(literalName):
                    return expression;

                case PhpTernaryOpAst ternary:
                    return RewriteTernary(ternary, scope);

                case PhpBinaryOpAst binary:
                    return RewriteBinaryFluent(binary, scope);

                case PhpUnaryOpAst unary:
                    return RewriteUnaryFluent(unary, scope);

                case PhpNewAst newExpr:
                    return RewriteNewFluent(newExpr, scope);

                case PhpDereferenceableAst deref:
                    return RewriteDerefFluent(deref, scope);

                case PhpInlineFunctionAst nested:
                    return RewriteNestedLambda(nested, scope);

                case PhpArrayAst array:
                    return RewriteArrayLiteral(array.ArrayPairs, scope);

                case PhpArrayPairListAst pairList:
                    return RewriteArrayLiteral(pairList, scope);

                default:
                    return Fail(expression, scope);
            }
        }

        /// <summary>
        /// A nested arrow fn becomes <c>Expr::closure([param builders], body, returnType)</c>.
        /// Its parameters shadow same-named outer parameters inside its body; outer parameters
        /// and captures stay visible.
        /// </summary>
        private static FluentChain? RewriteNestedLambda(PhpInlineFunctionAst nested, RewriteScope scope)
        {
            if (!nested.IsArrowFunction)
            {
                return Fail(nested, scope, "closure");
            }

            if (!PropertyPathSupport.TryGetArrowBodyExpression(nested, out var body))
            {
                return Fail(nested, scope, "statement body");
            }

            var own = BuildParameterInfos(
                nested,
                scope.GetInferredSignature?.Invoke(nested),
                scope.ResolveTypeSymbol,
                scope.FormatClassFqn,
                scope.Context);
            var ordered = own.Values.OrderBy(p => p.Index).ToList();
            var parameters = new Dictionary<string, ParameterInfo>(scope.Parameters, StringComparer.OrdinalIgnoreCase);
            foreach (var info in ordered)
            {
                parameters[info.Name] = info;
            }

            var nestedScope = new RewriteScope
            {
                Failure = scope.Failure,
                Parameters = parameters,
                IncludeIndex = scope.IncludeIndex || ordered.Count > 1,
                Primary = ordered.Count > 0 ? ordered[0] : scope.Primary,
                ExpressionTypes = scope.ExpressionTypes,
                FormatClassFqn = scope.FormatClassFqn,
                ResolveTypeSymbol = scope.ResolveTypeSymbol,
                GetInferredSignature = scope.GetInferredSignature,
                Context = scope.Context,
            };

            var bodyChain = Rewrite(body, nestedScope);
            if (bodyChain is null)
            {
                return null;
            }

            var args = new List<IExpression>
            {
                BuildArray(ordered.Select(p => RenderChain(ParamChain(p, nestedScope), scope.Context)), scope.Context),
                RenderChain(bodyChain, scope.Context),
            };
            var returnType = LookupTypeSpelling(body, scope.ExpressionTypes, scope.FormatClassFqn);
            if (returnType != "mixed")
            {
                args.Add(PhpScalarAst.CreateStringFromContext(scope.Context, returnType));
            }

            return StaticExprCall("closure", args, scope);
        }

        /// <summary>
        /// An array literal becomes <c>Expr::arrayOf([values], [keys])</c>. Keys are a list when
        /// every item is keyed, otherwise a position-to-key map for the keyed items only.
        /// </summary>
        private static FluentChain? RewriteArrayLiteral(PhpArrayPairListAst? pairs, RewriteScope scope)
        {
            var values = new List<IExpression>();
            var keys = new List<(int Position, IExpression Key)>();
            foreach (var pair in pairs?.GetAllTrimmingTrailingSkippedSlots() ?? [])
            {
                if (pair.IsExpansion || pair.IsSkippedSlot || pair.ValueExpr is null)
                {
                    return Fail(pair, scope, pair.IsExpansion ? "array spread" : "array literal");
                }

                if (pair.KeyExpr is { } keyExpr)
                {
                    var key = RewriteCore(keyExpr, scope);
                    if (key is null)
                    {
                        return null;
                    }

                    keys.Add((values.Count, RenderOperand(key, scope)));
                }

                var value = RewriteCore(pair.ValueExpr, scope);
                if (value is null)
                {
                    return null;
                }

                values.Add(RenderOperand(value, scope));
            }

            var args = new List<IExpression> { BuildArray(values, scope.Context) };
            if (keys.Count == values.Count && keys.Count > 0)
            {
                args.Add(BuildArray(keys.Select(k => k.Key), scope.Context));
            }
            else if (keys.Count > 0)
            {
                var keyPairs = keys
                    .Select(k => PhpArrayPairAst.CreateFromContext(
                        keyExpr: EmittedPhpExprAst.Create(k.Position.ToString(), scope.Context),
                        valueExpr: k.Key,
                        isExpansion: false,
                        scope.Context))
                    .ToList();
                args.Add(PhpArrayAst.CreateFromContext(
                    PhpArrayPairListAst.Create(keyPairs, scope.Context),
                    isShortSyntax: true,
                    scope.Context));
            }

            return StaticExprCall("arrayOf", args, scope);
        }

        private static FluentChain StaticExprCall(string method, IReadOnlyList<IExpression> args, RewriteScope scope)
            => new()
            {
                Base = PropertyPathEmissionHelper.BuildLambdaCall(
                    @"\Tyhp\Expr",
                    method,
                    args.Select(a => PhpArgumentAst.CreateFromContext(a, scope.Context)).ToList(),
                    scope.Context),
            };

        private static object? RewriteVariableFluent(PhpVariableAst variable, RewriteScope scope)
        {
            var name = CheckerHelpers.GetVariableName(variable);
            if (!string.IsNullOrEmpty(name)
                && scope.Parameters.TryGetValue(name, out var parameter))
            {
                return ParamChain(parameter, scope);
            }

            return variable;
        }

        private static FluentChain ParamChain(ParameterInfo info, RewriteScope scope)
        {
            var args = new List<IExpression>
            {
                PhpScalarAst.CreateStringFromContext(scope.Context, info.Name),
                CloneTypeArg(info.TypeArg, scope.Context),
            };
            if (scope.IncludeIndex)
            {
                args.Add(EmittedPhpExprAst.Create(info.Index.ToString(), scope.Context));
            }

            return new FluentChain
            {
                Base = PropertyPathEmissionHelper.BuildLambdaCall(
                    @"\Tyhp\Expr",
                    "param",
                    args.Select(a => PhpArgumentAst.CreateFromContext(a, scope.Context)).ToList(),
                    scope.Context),
                RootName = info.Name,
                RootIndex = info.Index,
                IsParamRoot = true,
            };
        }

        private static FluentChain AnchorRaw(IExpression raw, RewriteScope scope)
        {
            if (scope.Primary is { } primary)
            {
                var chain = ParamChain(primary, scope);
                chain.Steps.Add(new CallStep
                {
                    Method = "value",
                    Args = [raw],
                });
                return chain;
            }

            return new FluentChain
            {
                Base = PropertyPathEmissionHelper.BuildLambdaCall(
                    @"\Tyhp\Expr",
                    "literal",
                    [PhpArgumentAst.CreateFromContext(raw, scope.Context)],
                    scope.Context),
            };
        }

        private static FluentChain? RewriteTernary(PhpTernaryOpAst ternary, RewriteScope scope)
        {
            var cond = Rewrite(ternary.Condition, scope);
            if (cond is null)
            {
                return null;
            }

            // `$a ?: $b` yields `$a` when it is truthy, so the true branch is the condition again.
            var whenTrue = RewriteCore(ternary.TrueExpr ?? ternary.Condition, scope);
            var whenFalse = RewriteCore(ternary.FalseExpr, scope);
            if (whenTrue is null || whenFalse is null)
            {
                return null;
            }

            if (TryInlineSteps(whenTrue, cond, out var trueSteps))
            {
                cond.Steps.Add(new CallStep { Method = "then" });
                cond.Steps.AddRange(trueSteps);
            }
            else if (!TryAppendCall(cond, "then", [RenderOperand(whenTrue, scope)], scope))
            {
                return null;
            }

            if (!TryAppendCall(cond, "else", [RenderOperand(whenFalse, scope)], scope))
            {
                return null;
            }

            cond.HasCombiner = true;
            return cond;
        }

        private static object? RewriteBinaryFluent(PhpBinaryOpAst binary, RewriteScope scope)
        {
            var opText = binary.Operator?.ValueString ?? "";
            var token = (int)(binary.Operator?.ValueInt64 ?? -1);
            if (PhpBinaryOperatorExtensions.FromToken(token, opText) == PhpBinaryOperator.InstanceOf
                || IsInstanceOfText(opText))
            {
                var operand = Rewrite(binary.Left, scope);
                if (operand is null)
                {
                    return null;
                }

                var target = SpellInstanceofTarget(binary.Right, scope.FormatClassFqn, scope.Context);
                if (target is null)
                {
                    return Fail(binary.Right ?? (IBase2Ast)binary, scope, "instanceof");
                }

                return ApplyTip(operand, "is", [target], scope);
            }

            if (PhpBinaryOperatorExtensions.FromToken(token, opText) == PhpBinaryOperator.Coalesce
                || opText == "??")
            {
                return RewriteJump(binary.Left, binary.Right, "coalesce", scope);
            }

            var method = BinaryMethod(opText);
            if (method is null)
            {
                return Fail(binary, scope, opText.Length > 0 ? opText : null);
            }

            if (method is "and" or "or" or "xor")
            {
                return RewriteJump(binary.Left, binary.Right, method, scope);
            }

            var left = Rewrite(binary.Left, scope);
            var right = RewriteCore(binary.Right, scope);
            if (left is null || right is null)
            {
                return null;
            }

            return ApplyTip(left, method, [RenderOperand(right, scope)], scope);
        }

        private static FluentChain? RewriteJump(
            IExpression? leftExpr,
            IExpression? rightExpr,
            string method,
            RewriteScope scope)
        {
            var left = Rewrite(leftExpr, scope);
            var right = RewriteCore(rightExpr, scope);
            if (left is null || right is null)
            {
                return null;
            }

            if (TryInlineSteps(right, left, out var steps))
            {
                left.Steps.Add(new CallStep { Method = method });
                left.Steps.AddRange(steps);
                left.HasCombiner = true;
                return left;
            }

            if (!TryAppendCall(left, method, [RenderOperand(right, scope)], scope))
            {
                return null;
            }

            left.HasCombiner = true;
            return left;
        }

        private static FluentChain? RewriteUnaryFluent(PhpUnaryOpAst unary, RewriteScope scope)
        {
            var operand = Rewrite(unary.Operand, scope);
            if (operand is null)
            {
                return null;
            }

            var token = (int)(unary.Operator?.ValueInt64 ?? -1);
            var castText = unary.Operator?.ValueString;
            if (IsCastToken(token, castText))
            {
                return ApplyTip(
                    operand,
                    "cast",
                    [PhpScalarAst.CreateStringFromContext(scope.Context, CastTokenToTypeName(token, castText))],
                    scope);
            }

            var opText = NormalizeUnaryOperator(unary.Operator?.ValueString ?? "", token);
            if (!unary.IsPrefix)
            {
                return Fail(unary, scope, opText);
            }

            var method = opText switch
            {
                "!" => "not",
                "-" => "neg",
                "+" => "pos",
                "~" => "bitNot",
                _ => null,
            };
            if (method is null)
            {
                return Fail(unary, scope, opText);
            }

            return ApplyTip(operand, method, [], scope);
        }

        private static FluentChain? RewriteNewFluent(PhpNewAst newExpr, RewriteScope scope)
        {
            if (newExpr.AnonymousClass is not null)
            {
                return Fail(newExpr, scope, "anonymous class");
            }

            if (ExpressionTreeSupport.IsDynamicClassReference(newExpr.ClassName))
            {
                return Fail(newExpr.ClassName, scope, "dynamic class");
            }

            var args = RewriteCallArgs(newExpr.Arguments, scope);
            if (args is null)
            {
                return null;
            }

            var chain = scope.Primary is { } primary
                ? ParamChain(primary, scope)
                : AnchorRaw(EmittedPhpExprAst.Create("null", scope.Context), scope);
            chain.Steps.Add(new CallStep
            {
                Method = "make",
                Args = [SpellClassNameAsCtorArg(newExpr.ClassName, scope.FormatClassFqn, scope.Context), ..args],
            });
            return chain;
        }

        private static object? RewriteDerefFluent(PhpDereferenceableAst deref, RewriteScope scope)
        {
            if (deref.Base is PhpDereferenceableExpressionAst paren && deref.Suffix is null)
            {
                return RewriteCore(paren.Expression, scope);
            }

            if (deref.Suffix is PhpCallAst call)
            {
                if (CheckerHelpers.IsFirstClassCallableArgumentList(call.Arguments))
                {
                    return Fail(call, scope, "first-class callable");
                }

                if (deref.Base is PhpDereferenceableAst inner
                    && inner.Suffix is PhpInstanceMemberAccessAst member)
                {
                    var methodName = GetMemberName(member.MemberName);
                    if (methodName is null)
                    {
                        return Fail(member, scope, "dynamic member");
                    }

                    var objectChain = Rewrite(inner.Base as IExpression, scope);
                    var args = RewriteCallArgs(call.Arguments, scope);
                    if (objectChain is null || args is null)
                    {
                        return null;
                    }

                    return ApplyTip(
                        objectChain,
                        "call",
                        [PhpScalarAst.CreateStringFromContext(scope.Context, methodName), ..args],
                        scope,
                        nullSafe: IsNullSafeAccessor(member.Accessor));
                }

                if (deref.Base is PhpDereferenceableAst staticInner
                    && TryGetStaticMemberName(staticInner.Suffix, out var staticMethodName))
                {
                    if (ExpressionTreeSupport.IsDynamicClassReference(staticInner.Base))
                    {
                        return Fail(staticInner.Base, scope, "dynamic class");
                    }

                    var args = RewriteCallArgs(call.Arguments, scope);
                    if (staticMethodName is null || args is null)
                    {
                        return null;
                    }

                    var chain = scope.Primary is { } primary
                        ? ParamChain(primary, scope)
                        : AnchorRaw(EmittedPhpExprAst.Create("null", scope.Context), scope);
                    chain.Steps.Add(new CallStep
                    {
                        Method = "staticCall",
                        Args =
                        [
                            SpellClassNameAsCtorArg(staticInner.Base as IClassNameReference, scope.FormatClassFqn, scope.Context),
                            PhpScalarAst.CreateStringFromContext(scope.Context, staticMethodName),
                            ..args,
                        ],
                    });
                    return chain;
                }

                return Fail(deref, scope, "function call");
            }

            if (deref.Suffix is PhpInstanceMemberAccessAst property)
            {
                var propertyName = GetMemberName(property.MemberName);
                if (propertyName is null)
                {
                    return Fail(property, scope, "dynamic member");
                }

                var objectNode = RewriteCore(deref.Base as IExpression, scope);
                if (objectNode is null)
                {
                    return null;
                }

                var chain = objectNode as FluentChain ?? AnchorRaw((IExpression)objectNode, scope);
                var nullSafe = IsNullSafeAccessor(property.Accessor);
                if (nullSafe)
                {
                    chain.Steps.Add(new CallStep { Method = "nullSafe" });
                }

                if (ChainMethods.Contains(propertyName))
                {
                    chain.Steps.Add(new CallStep
                    {
                        Method = "prop",
                        Args = [PhpScalarAst.CreateStringFromContext(scope.Context, propertyName)],
                    });
                }
                else
                {
                    chain.Steps.Add(new PropStep { Name = propertyName });
                }

                return chain;
            }

            if (deref.Suffix is PhpArrayAccessAst arrayAccess)
            {
                if (arrayAccess.IndexExpression is null)
                {
                    return Fail(arrayAccess, scope, "array append");
                }

                var arrayChain = Rewrite(deref.Base as IExpression, scope);
                var index = RewriteCore(arrayAccess.IndexExpression, scope);
                if (arrayChain is null || index is null)
                {
                    return null;
                }

                return ApplyTip(arrayChain, "at", [RenderOperand(index, scope)], scope);
            }

            if (deref.Suffix is PhpStaticMemberAccessAst or PhpClassConstantAccessAst)
            {
                // Read when the chain is built, so the class must be written by name.
                return ExpressionTreeSupport.IsDynamicClassReference(deref.Base)
                    ? Fail(deref.Base, scope, "dynamic class")
                    : deref;
            }

            if (deref.Suffix is null)
            {
                return RewriteCore(deref.Base as IExpression, scope);
            }

            return Fail(deref, scope, "dereference");
        }

        private static List<IExpression>? RewriteCallArgs(PhpArgumentListAst? arguments, RewriteScope scope)
        {
            var result = new List<IExpression>();
            if (arguments is null)
            {
                return result;
            }

            foreach (var arg in arguments.GetAllNotNull())
            {
                if (arg.Expression is null)
                {
                    continue;
                }

                if (arg.IsVariadic)
                {
                    Fail(arg, scope, "argument unpacking");
                    return null;
                }

                if (arg.Name is not null)
                {
                    Fail(arg, scope, "named argument");
                    return null;
                }

                var rewritten = RewriteCore(arg.Expression, scope);
                if (rewritten is null)
                {
                    return null;
                }

                result.Add(RenderOperand(rewritten, scope));
            }

            return result;
        }

        /// <summary>
        /// Tip operators (<c>eq</c>, <c>not</c>, <c>plus</c>, …) apply to the current value.
        /// A chain that already contains <c>and</c>/<c>or</c>/<c>then</c> is wrapped in
        /// <c>expr</c> so the operator applies to that whole result. With
        /// <paramref name="nullSafe"/>, <c>nullSafe()</c> is placed directly before the tip.
        /// </summary>
        private static FluentChain ApplyTip(
            FluentChain chain,
            string method,
            IReadOnlyList<IExpression> args,
            RewriteScope scope,
            bool nullSafe = false)
        {
            if (!chain.HasCombiner)
            {
                if (nullSafe)
                {
                    chain.Steps.Add(new CallStep { Method = "nullSafe" });
                }

                chain.Steps.Add(new CallStep { Method = method, Args = [..args] });
                return chain;
            }

            var host = chain.IsParamRoot && chain.RootName is not null
                && scope.Parameters.TryGetValue(chain.RootName, out var root)
                ? ParamChain(root, scope)
                : scope.Primary is { } primary
                    ? ParamChain(primary, scope)
                    : AnchorRaw(EmittedPhpExprAst.Create("null", scope.Context), scope);
            host.Steps.Add(new CallStep
            {
                Method = "expr",
                Args = [RenderChain(chain, scope.Context)],
            });
            if (nullSafe)
            {
                host.Steps.Add(new CallStep { Method = "nullSafe" });
            }

            host.Steps.Add(new CallStep { Method = method, Args = [..args] });
            host.HasCombiner = CombinerMethods.Contains(method);
            return host;
        }

        private static bool TryAppendCall(
            FluentChain chain,
            string method,
            IReadOnlyList<IExpression> args,
            RewriteScope scope)
        {
            chain.Steps.Add(new CallStep { Method = method, Args = [..args] });
            if (CombinerMethods.Contains(method))
            {
                chain.HasCombiner = true;
            }

            _ = scope;
            return true;
        }

        private static bool TryInlineSteps(object right, FluentChain left, out List<FluentStep> steps)
        {
            steps = [];
            if (right is not FluentChain chain
                || !chain.IsParamRoot
                || chain.HasCombiner
                || chain.Steps.Count == 0
                || !left.IsParamRoot
                || !string.Equals(chain.RootName, left.RootName, StringComparison.OrdinalIgnoreCase)
                || chain.RootIndex != left.RootIndex)
            {
                return false;
            }

            steps = chain.Steps;
            return true;
        }

        private static IExpression RenderOperand(object rewritten, RewriteScope scope)
        {
            if (rewritten is FluentChain chain)
            {
                return RenderChain(chain, scope.Context);
            }

            return (IExpression)rewritten;
        }

        private static IExpression RenderChain(FluentChain chain, Base2Ast context)
        {
            IExpression current = chain.Base;
            foreach (var step in chain.Steps)
            {
                current = step switch
                {
                    PropStep prop => AccessProperty(current, prop.Name, context),
                    CallStep call => CallInstance(current, call.Method, call.Args, context),
                    _ => current,
                };
            }

            return current;
        }

        internal static IExpression AccessProperty(IExpression target, string name, Base2Ast context)
        {
            if (target is not IDereferenceableBase deref)
            {
                throw new InvalidOperationException($"Cannot read ->{name} on {target.GetType().Name}.");
            }

            var accessor = TokenValueAst.CreateFromContext("->", TyhpParser.T_OBJECT_OPERATOR, context);
            var member = PhpInstanceMemberAccessAst.CreateFromContext(
                accessor,
                PhpNameAst.CreateFromContext(name, context),
                context);
            return PhpDereferenceableAst.CreateFromContext(deref, member, context);
        }

        internal static bool IsFluentMethodName(string name) => ChainMethods.Contains(name);

        internal static IExpression CallInstance(
            IExpression target,
            string method,
            IReadOnlyList<IExpression> args,
            Base2Ast context)
        {
            if (target is not IDereferenceableBase deref)
            {
                throw new InvalidOperationException($"Cannot call ->{method}() on {target.GetType().Name}.");
            }

            var accessor = TokenValueAst.CreateFromContext("->", TyhpParser.T_OBJECT_OPERATOR, context);
            var member = PhpInstanceMemberAccessAst.CreateFromContext(
                accessor,
                PhpNameAst.CreateFromContext(method, context),
                context);
            var accessed = PhpDereferenceableAst.CreateFromContext(deref, member, context);
            var call = PhpCallAst.CreateFromContext(
                PhpArgumentListAst.Create(
                    args.Select(a => PhpArgumentAst.CreateFromContext(a, context)),
                    context),
                context);
            return PhpDereferenceableAst.CreateFromContext(accessed, call, context);
        }

        private static string? BinaryMethod(string op) => op switch
        {
            ">" => "gt",
            ">=" => "gte",
            "<" => "lt",
            "<=" => "lte",
            "===" => "eq",
            "!==" => "neq",
            "==" => "looseEq",
            "!=" or "<>" => "looseNeq",
            "<=>" => "spaceship",
            "+" => "plus",
            "-" => "minus",
            "*" => "times",
            "/" => "dividedBy",
            "%" => "mod",
            "**" => "pow",
            "." => "concat",
            "&&" or "and" => "and",
            "||" or "or" => "or",
            "xor" => "xor",
            "??" => "coalesce",
            "&" => "bitAnd",
            "|" => "bitOr",
            "^" => "bitXor",
            "<<" => "shiftLeft",
            ">>" => "shiftRight",
            _ => null,
        };
    }
}
