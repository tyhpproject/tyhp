using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// Pragmatic code-quality warnings: unused variables, suspicious conditions, redundant casts.
    /// </summary>
    public sealed class CodeQualityRule : ICheckerRule
    {
        // PhpMethodDeclAst is intentionally absent: CheckObjectBody calls CheckMethod directly
        // (not CheckNode), so unused-variable scans run via CheckMethodBody from that path.
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpIfAst),
            typeof(PhpLoopAst),
            typeof(PhpConditionalAst),
            typeof(PhpUnaryOpAst),
            typeof(PhpVariableAst),
            typeof(PhpFunctionDeclAst),
        ];

        public bool Handles(IBase2Ast node) =>
            node is not PhpUnaryOpAst unary || IsCastOperator(unary.Operator);

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpIfAst ifAst:
                    CheckConditionQuality(ifAst.Condition, state, context, diagnostics);
                    CheckAssignmentInCondition(ifAst.Condition, state, diagnostics);
                    break;
                case PhpLoopAst loop when loop.LoopType is PhpLoopType.While or PhpLoopType.DoWhile:
                    CheckConditionQuality(loop.Condition, state, context, diagnostics);
                    CheckAssignmentInCondition(loop.Condition, state, diagnostics);
                    break;
                case PhpLoopAst loop when loop.LoopType == PhpLoopType.For:
                {
                    var tests = loop.TestExpressions?.GetAllNotNull().ToList() ?? [];
                    // Only the last for-condition item is the boolean condition (php-src
                    // for_cond_exprs); preceding items may be `(void)` discards.
                    if (tests.Count > 0)
                    {
                        var last = tests[^1];
                        CheckConditionQuality(last, state, context, diagnostics);
                        CheckAssignmentInCondition(last, state, diagnostics);
                    }
                    break;
                }
                case PhpConditionalAst conditional:
                    CheckConditionQuality(conditional.Expression, state, context, diagnostics);
                    break;
                case PhpUnaryOpAst unary when IsCastOperator(unary.Operator):
                    CheckRedundantCast(unary, state, context, diagnostics);
                    break;
                case PhpVariableAst variable:
                    MarkVariableRead(variable, state);
                    break;
                case PhpFunctionDeclAst function:
                    CheckUnusedVariablesInBody(function.Body, state, diagnostics);
                    break;
            }
        }

        /// <summary>
        /// Scans a method body for unused locals. Invoked explicitly from
        /// <c>DeclarationRule.CheckMethod</c> because class members bypass <c>CheckNode</c>.
        /// </summary>
        public static void CheckMethodBody(
            PhpMethodDeclAst method,
            CheckerState state,
            DiagnosticBag diagnostics) =>
            CheckUnusedVariablesInBody(method.Body, state, diagnostics);

        private static void CheckAssignmentInCondition(
            IExpression? condition,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (condition is null || HasSuppressingParentheses(condition))
            {
                return;
            }

            if (ContainsTopLevelAssignment(condition))
            {
                CheckerHelpers.ReportWarning(
                    diagnostics, state, condition, MessageCode.CheckerAssignmentInCondition);
            }
        }

        private static void CheckConditionQuality(
            IExpression? condition,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (condition is null)
            {
                return;
            }

            if (IsStaticallyAlwaysTrue(condition, context, state))
            {
                CheckerHelpers.ReportWarning(
                    diagnostics, state, condition, MessageCode.CheckerConditionAlwaysTrueFalse, "true");
            }
            else if (IsStaticallyAlwaysFalse(condition, context, state))
            {
                CheckerHelpers.ReportWarning(
                    diagnostics, state, condition, MessageCode.CheckerConditionAlwaysTrueFalse, "false");
            }
        }

        private static void CheckRedundantCast(
            PhpUnaryOpAst unary,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            if (unary.Operand is null)
            {
                return;
            }

            var operandType = context.ResolveExpressionType(unary.Operand, state);
            var castType = InferCastType(GetTokenType(unary.Operator));
            if (string.Equals(operandType.DisplayName, castType.DisplayName, StringComparison.OrdinalIgnoreCase)
                || (context.IsAssignable(operandType, castType) && context.IsAssignable(castType, operandType)))
            {
                CheckerHelpers.ReportWarning(
                    diagnostics, state, unary, MessageCode.CheckerRedundantCast, castType.DisplayName);
            }
        }

        private static void CheckUnusedVariablesInBody(
            PhpStatementBlockAst? body,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (body is null)
            {
                return;
            }

            var declared = new Dictionary<string, IBase2Ast>(StringComparer.Ordinal);
            var reads = new HashSet<string>(StringComparer.Ordinal);
            CollectVariableUsage(body, declared, reads);

            foreach (var (name, declaration) in declared)
            {
                if (reads.Contains(name) || IsIntentionallyUnused(name))
                {
                    continue;
                }

                CheckerHelpers.ReportWarning(
                    diagnostics, state, declaration, MessageCode.CheckerUnusedVariable, name);
            }
        }

        private static void MarkVariableRead(PhpVariableAst variable, CheckerState state)
        {
            var name = CheckerHelpers.GetVariableName(variable);
            if (name is null)
            {
                return;
            }

            var varState = state.LookupVariable(name);
            if (varState is not null)
            {
                varState.IsRead = true;
            }
        }

        private static void CollectVariableUsage(
            IBase2Ast node,
            Dictionary<string, IBase2Ast> declared,
            HashSet<string> reads)
        {
            // When a node declares or assigns a variable, the variable node on the
            // left-hand side is a write target, not a read. Track it so the generic
            // child traversal below skips it; otherwise every declared variable would
            // also be recorded as read, defeating unused-variable detection entirely.
            IBase2Ast? declarationTarget = null;

            switch (node)
            {
                case TyhpTypedVarExprAst typedVar:
                {
                    var name = typedVar.Variable is not null
                        ? CheckerHelpers.GetVariableName(typedVar.Variable)
                        : null;
                    if (!string.IsNullOrEmpty(name))
                    {
                        declared.TryAdd(name, typedVar);
                    }
                    declarationTarget = typedVar.Variable;
                    break;
                }
                case PhpVariableAst variable:
                {
                    var name = CheckerHelpers.GetVariableName(variable);
                    if (name is not null)
                    {
                        reads.Add(name);
                    }
                    break;
                }
                case PhpBinaryOpAst { Operator.ValueString: "=", Left: PhpVariableAst left } assign:
                {
                    var name = CheckerHelpers.GetVariableName(left);
                    if (name is not null)
                    {
                        declared.TryAdd(name, assign);
                    }
                    declarationTarget = left;
                    break;
                }
                case PhpLoopAst { LoopType: PhpLoopType.Foreach, ValueVariable: PhpVariableAst foreachVar }:
                {
                    var name = CheckerHelpers.GetVariableName(foreachVar);
                    if (name is not null)
                    {
                        declared.TryAdd(name, foreachVar);
                    }
                    declarationTarget = foreachVar;
                    break;
                }
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null && !ReferenceEquals(child, declarationTarget))
                {
                    CollectVariableUsage(child, declared, reads);
                }
            }
        }

        private static bool ContainsTopLevelAssignment(IExpression expression) =>
            expression switch
            {
                PhpBinaryOpAst { Operator.ValueString: "=", Left: PhpVariableAst } => true,
                PhpBinaryOpAst binary => ContainsTopLevelAssignmentInBinary(binary),
                PhpUnaryOpAst { Operator.ValueString: "!" or "not" } unary => ContainsTopLevelAssignment(unary.Operand!),
                PhpTernaryOpAst ternary =>
                    (ternary.Condition is IExpression cond && ContainsTopLevelAssignment(cond))
                    || (ternary.TrueExpr is IExpression t && ContainsTopLevelAssignment(t))
                    || (ternary.FalseExpr is IExpression f && ContainsTopLevelAssignment(f)),
                _ => false,
            };

        private static bool ContainsTopLevelAssignmentInBinary(PhpBinaryOpAst binary)
        {
            var op = binary.Operator?.ValueString ?? string.Empty;
            if (op is "&&" or "||" or "and" or "or" or "xor")
            {
                return (binary.Left is IExpression left && ContainsTopLevelAssignment(left))
                    || (binary.Right is IExpression right && ContainsTopLevelAssignment(right));
            }

            return false;
        }

        private static bool HasSuppressingParentheses(IExpression expression) =>
            expression is PhpUnaryOpAst { Operator.ValueString: "(" };

        private static bool IsStaticallyAlwaysTrue(IExpression expression, CheckerRuleContext context, CheckerState state) =>
            expression switch
            {
                TokenValueAst { ValueString: "true" } => true,
                PhpBinaryOpAst binary => IsLiteralIdentityComparison(binary, alwaysTrue: true),
                _ => false,
            };

        private static bool IsStaticallyAlwaysFalse(IExpression expression, CheckerRuleContext context, CheckerState state) =>
            expression switch
            {
                TokenValueAst { ValueString: "false" } => true,
                PhpBinaryOpAst binary => IsLiteralIdentityComparison(binary, alwaysTrue: false),
                _ => false,
            };

        private static bool IsLiteralIdentityComparison(PhpBinaryOpAst binary, bool alwaysTrue)
        {
            if (binary.Operator?.ValueString is not "===" and not "!==")
            {
                return false;
            }

            var left = GetLiteralValue(binary.Left);
            var right = GetLiteralValue(binary.Right);
            if (left is null || right is null)
            {
                return false;
            }

            var identical = string.Equals(left, right, StringComparison.Ordinal);
            var isTrueBranch = binary.Operator.ValueString == "===";
            return alwaysTrue ? identical == isTrueBranch : identical != isTrueBranch;
        }

        private static string? GetLiteralValue(IExpression? expression) =>
            expression switch
            {
                TokenValueAst token => token.ValueString,
                PhpScalarAst scalar => scalar.ValueString ?? scalar.ValueInt64?.ToString(),
                _ => null,
            };

        private static bool IsIntentionallyUnused(string name) =>
            string.Equals(name, "_", StringComparison.Ordinal)
            || name.StartsWith('_');

        private static bool IsCastOperator(TokenValueAst? op)
        {
            var token = GetTokenType(op);
            return token is TyhpParser.T_INT_CAST
                or TyhpParser.T_BOOL_CAST
                or TyhpParser.T_STRING_CAST
                or TyhpParser.T_DOUBLE_CAST
                or TyhpParser.T_DECIMAL_CAST
                or TyhpParser.T_ARRAY_CAST
                or TyhpParser.T_OBJECT_CAST;
        }

        private static int GetTokenType(TokenValueAst? token) =>
            token?.ValueInt64 is long value ? (int)value : 0;

        private static ICheckedType InferCastType(int token) =>
            token switch
            {
                TyhpParser.T_INT_CAST => CheckedTypes.Int,
                TyhpParser.T_BOOL_CAST => CheckedTypes.Bool,
                TyhpParser.T_STRING_CAST => CheckedTypes.String,
                TyhpParser.T_DOUBLE_CAST => CheckedTypes.Float,
                TyhpParser.T_DECIMAL_CAST => CheckedTypes.FromSymbol(new Binder.Symbols.BuiltInTypeSymbol("decimal")),
                TyhpParser.T_ARRAY_CAST => CheckedTypes.FromSymbol(new Binder.Symbols.BuiltInTypeSymbol("array")),
                TyhpParser.T_OBJECT_CAST => CheckedTypes.FromSymbol(new Binder.Symbols.BuiltInTypeSymbol("object")),
                _ => CheckedTypes.Unresolved,
            };
    }
}
