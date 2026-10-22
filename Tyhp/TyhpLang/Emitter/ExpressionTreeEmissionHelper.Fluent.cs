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

        private sealed class RewriteScope
        {
            public Dictionary<string, ParameterInfo> Parameters { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public bool IncludeIndex { get; init; }
            public ParameterInfo? Primary { get; init; }
            public IReadOnlyDictionary<IBase2Ast, ICheckedType>? ExpressionTypes { get; init; }
            public Func<string?, string?, string> FormatClassFqn { get; init; } = null!;
            public Base2Ast Context { get; init; } = null!;
        }

        private static FluentChain? RewriteFluent(
            IExpression body,
            Dictionary<string, ParameterInfo> parameters,
            IReadOnlyDictionary<IBase2Ast, Tyhp.TyhpLang.Checker.ICheckedType>? expressionTypes,
            Func<string?, string?, string> formatClassFqn,
            Base2Ast context)
        {
            var scope = new RewriteScope
            {
                Parameters = parameters,
                IncludeIndex = parameters.Count > 1 || parameters.Values.Any(p => p.Index != 0),
                Primary = parameters.Count == 0
                    ? null
                    : parameters.Values.OrderBy(p => p.Index).First(),
                ExpressionTypes = expressionTypes,
                FormatClassFqn = formatClassFqn,
                Context = context,
            };
            return AsChain(Rewrite(body, scope), scope);
        }

        private static FluentChain? AsChain(FluentChain? chain, RewriteScope scope)
        {
            return chain;
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

                default:
                    return null;
            }
        }

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

            var whenTrue = ternary.TrueExpr is null
                ? (object)EmittedPhpExprAst.Create("null", scope.Context)
                : RewriteCore(ternary.TrueExpr, scope);
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
                var target = SpellInstanceofTarget(binary.Right, scope.FormatClassFqn, scope.Context);
                if (operand is null || target is null)
                {
                    return null;
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
                return null;
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

            if (!unary.IsPrefix)
            {
                return null;
            }

            var method = NormalizeUnaryOperator(unary.Operator?.ValueString ?? "", token) switch
            {
                "!" => "not",
                "-" => "neg",
                "+" => "pos",
                "~" => "bitNot",
                _ => null,
            };
            if (method is null)
            {
                return null;
            }

            return ApplyTip(operand, method, [], scope);
        }

        private static FluentChain? RewriteNewFluent(PhpNewAst newExpr, RewriteScope scope)
        {
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
                if (deref.Base is PhpDereferenceableAst inner
                    && inner.Suffix is PhpInstanceMemberAccessAst member)
                {
                    if (IsNullSafeAccessor(member.Accessor))
                    {
                        return null;
                    }

                    var objectChain = Rewrite(inner.Base as IExpression, scope);
                    var methodName = GetMemberName(member.MemberName);
                    var args = RewriteCallArgs(call.Arguments, scope);
                    if (objectChain is null || methodName is null || args is null)
                    {
                        return null;
                    }

                    return ApplyTip(
                        objectChain,
                        "call",
                        [PhpScalarAst.CreateStringFromContext(scope.Context, methodName), ..args],
                        scope);
                }

                if (deref.Base is PhpDereferenceableAst staticInner
                    && TryGetStaticMemberName(staticInner.Suffix, out var staticMethodName))
                {
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

                return null;
            }

            if (deref.Suffix is PhpInstanceMemberAccessAst property)
            {
                var objectNode = RewriteCore(deref.Base as IExpression, scope);
                var propertyName = GetMemberName(property.MemberName);
                if (objectNode is null || propertyName is null)
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
                return deref;
            }

            if (deref.Suffix is null)
            {
                return RewriteCore(deref.Base as IExpression, scope);
            }

            return null;
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
        /// <c>expr</c> so the operator applies to that whole result.
        /// </summary>
        private static FluentChain ApplyTip(
            FluentChain chain,
            string method,
            IReadOnlyList<IExpression> args,
            RewriteScope scope)
        {
            if (!chain.HasCombiner)
            {
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
