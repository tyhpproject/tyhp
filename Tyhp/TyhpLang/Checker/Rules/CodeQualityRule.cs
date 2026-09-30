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
                case PhpConditionalAst conditional when conditional.IsMatchSyntax:
                    // Arm reachability (TYHP4204 / TYHP4208) runs from InferMatch so return
                    // expressions that never CheckNode this node still report. Skip the subject
                    // so match (true) is not an if (true) warning.
                    break;
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
                    CheckUnusedVariablesInBody(function.Body, state, diagnostics, function.Parameters);
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
            CheckUnusedVariablesInBody(method.Body, state, diagnostics, method.Parameters);

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

        /// <summary>
        /// Match subjects are values, not boolean conditions: <c>match (true)</c> is the
        /// idiomatic first-true-arm form and must not TYHP4204. An arm that statically always
        /// matches the subject is TYHP4204, and later non-default arms are TYHP4208, only when
        /// those later arms exist. <c>default</c> after an always-matching arm is allowed.
        /// Invoked from <c>TypeInferrer.InferMatch</c> so return/assignment matches that never
        /// <c>CheckNode</c> the <c>PhpConditionalAst</c> still report.
        /// </summary>
        internal static void CheckMatchArmReachability(
            PhpConditionalAst conditional,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            var arms = conditional.Arms?.GetAllNotNull().ToList() ?? [];
            if (arms.Count == 0)
            {
                return;
            }

            var subject = conditional.Expression;
            var subjectType = subject is IExpression subjectExpr
                ? context.ResolveExpressionType(subjectExpr, state)
                : CheckedTypes.Unresolved;
            var subjectAlwaysTrue = subject is IExpression alwaysTrueSubject
                && (IsStaticallyAlwaysTrue(alwaysTrueSubject, context, state)
                    || TypeComparer.IsTrueType(subjectType));
            var subjectAlwaysFalse = subject is IExpression alwaysFalseSubject
                && (IsStaticallyAlwaysFalse(alwaysFalseSubject, context, state)
                    || TypeComparer.IsFalseType(subjectType));

            for (var i = 0; i < arms.Count; i++)
            {
                var arm = arms[i];
                if (arm.IsDefault || arm.Conditions is null)
                {
                    continue;
                }

                IExpression? matchingCondition = null;
                foreach (var condition in arm.Conditions.GetAllNotNull())
                {
                    if (ArmConditionAlwaysMatchesSubject(
                        condition,
                        subject,
                        subjectType,
                        subjectAlwaysTrue,
                        subjectAlwaysFalse,
                        state,
                        context))
                    {
                        matchingCondition = condition;
                        break;
                    }
                }

                if (matchingCondition is null || !HasLaterNonDefaultArm(arms, i))
                {
                    continue;
                }

                CheckerHelpers.ReportWarning(
                    diagnostics,
                    state,
                    matchingCondition,
                    MessageCode.CheckerConditionAlwaysTrueFalse,
                    "true");

                for (var j = i + 1; j < arms.Count; j++)
                {
                    if (arms[j].IsDefault)
                    {
                        continue;
                    }

                    CheckerHelpers.ReportWarning(
                        diagnostics, state, arms[j], MessageCode.CheckerUnreachableArm);
                }

                return;
            }
        }

        private static bool HasLaterNonDefaultArm(IReadOnlyList<PhpConditionalArmAst> arms, int index)
        {
            for (var i = index + 1; i < arms.Count; i++)
            {
                if (!arms[i].IsDefault)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ArmConditionAlwaysMatchesSubject(
            IExpression condition,
            IExpression? subject,
            ICheckedType subjectType,
            bool subjectAlwaysTrue,
            bool subjectAlwaysFalse,
            CheckerState state,
            CheckerRuleContext context)
        {
            if (subjectAlwaysTrue && IsStaticallyAlwaysTrue(condition, context, state))
            {
                return true;
            }

            if (subjectAlwaysFalse && IsStaticallyAlwaysFalse(condition, context, state))
            {
                return true;
            }

            var conditionType = context.ResolveExpressionType(condition, state);
            if (ArrayAccessShapeSupport.ArmMatchesLiteralSubject(subjectType, [conditionType])
                || TypeComparer.AreEquivalentIdentityLiterals(subjectType, conditionType))
            {
                return true;
            }

            if (subject is null)
            {
                return false;
            }

            var subjectLiteral = GetLiteralValue(subject);
            var conditionLiteral = GetLiteralValue(condition);
            return subjectLiteral is not null
                && conditionLiteral is not null
                && string.Equals(subjectLiteral, conditionLiteral, StringComparison.Ordinal);
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
            // Unresolved is mixed-like recovery and assignable to/from every type, so a
            // "redundant" (float)$unresolved warning hides the real failure. Mixed is the
            // same gradual hole: (int)$mixed is a conversion, not a no-op.
            if (TypeComparer.IsUnresolvedType(operandType) || TypeComparer.IsMixedType(operandType))
            {
                return;
            }

            var castType = InferCastType(unary.Operator, operandType, context);
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
            DiagnosticBag diagnostics,
            PhpParameterListAst? parameters)
        {
            if (body is null)
            {
                return;
            }

            var declared = new Dictionary<string, IBase2Ast>(StringComparer.Ordinal);
            var reads = new HashSet<string>(StringComparer.Ordinal);
            var refParams = CollectRefParameterNames(parameters);
            CollectVariableUsage(body, declared, reads, refParams);

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

        private static HashSet<string> CollectRefParameterNames(PhpParameterListAst? parameters)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            AddRefParameterNames(parameters, names);
            return names;
        }

        private static void AddRefParameterNames(PhpParameterListAst? parameters, HashSet<string> names)
        {
            if (parameters is null)
            {
                return;
            }

            foreach (var param in parameters.GetAllNotNull())
            {
                if (!param.IsRef)
                {
                    continue;
                }

                var name = param.Name?.TrimStart('$');
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }
        }

        private static void AddRefLexicalVarNames(PhpVariableListAst lexical, HashSet<string> names)
        {
            foreach (var variable in lexical.GetAllNotNull())
            {
                if (!variable.IsRef)
                {
                    continue;
                }

                var name = CheckerHelpers.GetVariableName(variable);
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }
        }

        private static void CollectVariableUsage(
            IBase2Ast node,
            Dictionary<string, IBase2Ast> declared,
            HashSet<string> reads,
            HashSet<string> refParameters)
        {
            if (node is PhpInlineFunctionAst or PhpFunctionDeclAst)
            {
                // Nested callables have their own `&` write-outs. Do not inherit the outer
                // callable's ref names — a nested local of the same name is still unused.
                var nestedRefs = node switch
                {
                    PhpInlineFunctionAst fn => CollectRefParameterNames(fn.Parameters),
                    PhpFunctionDeclAst fn => CollectRefParameterNames(fn.Parameters),
                    _ => new HashSet<string>(StringComparer.Ordinal),
                };
                if (node is PhpInlineFunctionAst { LexicalVars: { } lexical })
                {
                    AddRefLexicalVarNames(lexical, nestedRefs);
                }

                foreach (var child in node.AstChildren)
                {
                    if (child is not null)
                    {
                        CollectVariableUsage(child, declared, reads, nestedRefs);
                    }
                }

                return;
            }

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
                    if (!string.IsNullOrEmpty(name) && !refParameters.Contains(name))
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
                    if (name is not null && !refParameters.Contains(name))
                    {
                        declared.TryAdd(name, assign);
                    }
                    declarationTarget = left;
                    break;
                }
                case PhpLoopAst { LoopType: PhpLoopType.Foreach, ValueVariable: PhpVariableAst foreachVar }:
                {
                    var name = CheckerHelpers.GetVariableName(foreachVar);
                    if (name is not null && !refParameters.Contains(name))
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
                    CollectVariableUsage(child, declared, reads, refParameters);
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

        private static IExpression UnwrapParentheses(IExpression expression)
        {
            while (expression is PhpUnaryOpAst { Operator.ValueString: "(", Operand: IExpression inner })
            {
                expression = inner;
            }

            return expression;
        }

        private static bool IsStaticallyAlwaysTrue(IExpression expression, CheckerRuleContext context, CheckerState state)
        {
            expression = UnwrapParentheses(expression);
            return expression switch
            {
                TokenValueAst { ValueString: "true" } => true,
                PhpNameAst name when string.Equals(name.ValueString, "true", StringComparison.OrdinalIgnoreCase) => true,
                PhpBinaryOpAst binary => IsLiteralIdentityComparison(binary, alwaysTrue: true, context, state),
                _ => TypeComparer.IsTrueType(context.ResolveExpressionType(expression, state)),
            };
        }

        private static bool IsStaticallyAlwaysFalse(IExpression expression, CheckerRuleContext context, CheckerState state)
        {
            expression = UnwrapParentheses(expression);
            return expression switch
            {
                TokenValueAst { ValueString: "false" } => true,
                PhpNameAst name when string.Equals(name.ValueString, "false", StringComparison.OrdinalIgnoreCase) => true,
                PhpBinaryOpAst binary => IsLiteralIdentityComparison(binary, alwaysTrue: false, context, state),
                _ => TypeComparer.IsFalseType(context.ResolveExpressionType(expression, state)),
            };
        }

        private static bool IsIdenticalOperator(TokenValueAst? op)
        {
            if (op is null)
            {
                return false;
            }

            if (op.ValueString is "===" or "!==")
            {
                return true;
            }

            var token = GetTokenType(op);
            return token is TyhpParser.T_IS_IDENTICAL or TyhpParser.T_IS_NOT_IDENTICAL;
        }

        private static bool IsLiteralIdentityComparison(
            PhpBinaryOpAst binary,
            bool alwaysTrue,
            CheckerRuleContext context,
            CheckerState state)
        {
            if (!IsIdenticalOperator(binary.Operator))
            {
                return false;
            }

            var isTrueBranch = binary.Operator?.ValueString == "==="
                || GetTokenType(binary.Operator) == TyhpParser.T_IS_IDENTICAL;

            var leftAst = GetLiteralValue(binary.Left);
            var rightAst = GetLiteralValue(binary.Right);
            if (leftAst is not null && rightAst is not null)
            {
                var identical = string.Equals(leftAst, rightAst, StringComparison.Ordinal);
                return alwaysTrue ? identical == isTrueBranch : identical != isTrueBranch;
            }

            if (binary.Left is not IExpression left || binary.Right is not IExpression right)
            {
                return false;
            }

            var leftType = context.ResolveExpressionType(left, state);
            var rightType = context.ResolveExpressionType(right, state);
            if (leftType is not LiteralCheckedType leftLiteral
                || rightType is not LiteralCheckedType rightLiteral)
            {
                return false;
            }

            var literalsEqual = ArrayAccessShapeSupport.LiteralsEqual(leftLiteral.Value, rightLiteral.Value);
            return alwaysTrue ? literalsEqual == isTrueBranch : literalsEqual != isTrueBranch;
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
            if (TrySpellCast(op?.ValueString, out _))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(op?.ValueString))
            {
                return false;
            }

            var token = GetTokenType(op);
            return token is TyhpParser.T_INT_CAST
                or TyhpParser.T_BOOL_CAST
                or TyhpParser.T_STRING_CAST
                or TyhpParser.T_DOUBLE_CAST
                or TyhpParser.T_DECIMAL_CAST
                or TyhpParser.T_ARRAY_CAST
                or TyhpParser.T_OBJECT_CAST;
        }

        /// <summary>
        /// PHP cast tokens spell as <c>(int)</c>. The spelling wins over the parser id so a
        /// <c>TyhpdefParser</c> cast is not classified by a shifted <c>TyhpParser</c> number.
        /// </summary>
        private static bool TrySpellCast(string? text, out string typeName)
        {
            typeName = "";
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var spelled = text.Trim().Trim('(', ')').Trim().ToLowerInvariant();
            typeName = spelled switch
            {
                "int" or "integer" => "int",
                "bool" or "boolean" => "bool",
                "string" or "binary" => "string",
                "float" or "double" or "real" => "float",
                "decimal" => "decimal",
                "array" => "array",
                "object" => "object",
                _ => "",
            };
            return typeName.Length > 0;
        }

        private static int GetTokenType(TokenValueAst? token) =>
            token?.ValueInt64 is long value ? (int)value : 0;

        private static ICheckedType InferCastType(TokenValueAst? op, ICheckedType operand, CheckerRuleContext context)
        {
            if (TrySpellCast(op?.ValueString, out var spelled))
            {
                return spelled switch
                {
                    "int" => CheckedTypes.Int,
                    "bool" => CheckedTypes.Bool,
                    "string" => CheckedTypes.String,
                    "float" => CheckedTypes.Float,
                    "decimal" => CheckedTypes.FromSymbol(new Binder.Symbols.BuiltInTypeSymbol("decimal")),
                    "array" => CheckedTypes.FromSymbol(new Binder.Symbols.BuiltInTypeSymbol("array")),
                    "object" => InferObjectCastType(operand, context),
                    _ => CheckedTypes.Unresolved,
                };
            }

            var token = GetTokenType(op);
            return token switch
            {
                TyhpParser.T_INT_CAST => CheckedTypes.Int,
                TyhpParser.T_BOOL_CAST => CheckedTypes.Bool,
                TyhpParser.T_STRING_CAST => CheckedTypes.String,
                TyhpParser.T_DOUBLE_CAST => CheckedTypes.Float,
                TyhpParser.T_DECIMAL_CAST => CheckedTypes.FromSymbol(new Binder.Symbols.BuiltInTypeSymbol("decimal")),
                TyhpParser.T_ARRAY_CAST => CheckedTypes.FromSymbol(new Binder.Symbols.BuiltInTypeSymbol("array")),
                // Must match TypeInferrer: (object) on an already-object operand is a no-op
                // (identity) — PHP never re-wraps an existing object in a new \stdClass. Only a
                // real array/scalar/null → object conversion resolves the engine \stdClass.
                TyhpParser.T_OBJECT_CAST => InferObjectCastType(operand, context),
                _ => CheckedTypes.Unresolved,
            };
        }

        private static ICheckedType InferObjectCastType(ICheckedType operand, CheckerRuleContext context)
        {
            if (IsDefinitelyObject(operand))
            {
                return operand;
            }

            return CheckerHelpers.ResolveNamedType("stdClass", context.SymbolTree, context.GlobalScope);
        }

        /// <summary>
        /// True when every possible runtime value of <paramref name="type"/> is already an object,
        /// so a <c>(object)</c> cast on it is a no-op and not a redundant-identity narrowing to
        /// <c>\stdClass</c>. Kept in sync with <c>TypeInferrer.InferObjectCastType</c>.
        /// </summary>
        private static bool IsDefinitelyObject(ICheckedType type) =>
            type switch
            {
                NullableCheckedType => false,
                UnionCheckedType union => union.Members.Count > 0 && union.Members.All(IsDefinitelyObject),
                _ => CheckerHelpers.TryGetObjectDeclaration(type) is not null
                    || CheckerHelpers.IsBuiltInName(type, "object"),
            };
    }
}
