using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Validates Tyhp operator overload declarations.</summary>
    public sealed class OperatorOverloadRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes => [typeof(TyhpOperatorOverloadAst)];

        public bool SuppressChildTraversal(IBase2Ast node) => true;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            if (node is not TyhpOperatorOverloadAst overload)
            {
                return;
            }

            ValidateExtensionTarget(overload, state, diagnostics);

            var opToken = overload.Op;
            var isUnary = overload.RightParameter is null;
            var opEnum = opToken is null
                ? OverloadableOperator.Invalid
                : OverloadableOperatorHelper.FromToken(
                    (int)(opToken.ValueInt64 ?? 0L),
                    opToken.ValueString ?? string.Empty,
                    isAlternateKind: isUnary);

            if (opEnum == OverloadableOperator.Invalid)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    overload,
                    MessageCode.CheckerInvalidOperatorForType,
                    opToken?.ValueString ?? "?",
                    "operator",
                    "declaration");
                return;
            }

            ValidateParameterCount(overload, isUnary, opEnum, state, diagnostics);
            ValidateSelfParameter(overload, opEnum, state, diagnostics);
            ValidateConvertNotSelfToSelf(overload, opEnum, state, diagnostics);

            if (overload.ReturnType is not null)
            {
                var returnType = context.ResolveTypeAnnotation(overload.ReturnType, state, isReturnTypePosition: true);
                ValidateReturnType(overload, opEnum, returnType, state, diagnostics);
                // OperatorOverloadRule suppresses child traversal, so the return type is never
                // CheckNode'd — still count import usage for TYHP4130.
                context.MarkImportNames(overload.ReturnType, state);
            }

            foreach (var operand in new[] { overload.LeftParameter, overload.RightParameter })
            {
                if (operand?.Type is not null)
                {
                    context.MarkImportNames(operand.Type, state);
                }
            }

            if (overload.Body is not null)
            {
                var bodyState = state.Split(ScopeType.StaticMethodDeclaration);
                context.CheckStatementBlock(overload.Body, bodyState);
            }
        }

        private static void ValidateExtensionTarget(
            TyhpOperatorOverloadAst overload,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (overload.IsExtensionOperator)
            {
                return;
            }

            if (overload.ExtensionTargetType is not null && !overload.IsInlineExtension)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    overload.ExtensionTargetType,
                    MessageCode.ExtensionOperatorTargetNotAllowed,
                    "Operator target type is only allowed inside extension declarations.");
            }
        }

        private static void ValidateParameterCount(
            TyhpOperatorOverloadAst overload,
            bool isUnary,
            OverloadableOperator opEnum,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            var opName = overload.Op?.ValueString ?? overload.Identifier ?? "operator";

            if (isUnary)
            {
                if (overload.RightParameter is not null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, overload, MessageCode.CheckerMagicMethodSignature, opName,
                        "a unary operator cannot declare a second operand");
                }

                if (overload.LeftParameter is null)
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, overload, MessageCode.CheckerMagicMethodSignature, opName,
                        "a unary operator must declare its operand");
                }

                return;
            }

            if (overload.LeftParameter is null || overload.RightParameter is null)
            {
                CheckerHelpers.ReportError(
                    diagnostics, state, overload, MessageCode.CheckerMagicMethodSignature, opName,
                    "a binary operator must declare both operands");
            }

            if (opEnum == OverloadableOperator.Convert)
            {
                return;
            }

            if (overload.LeftParameter is null || overload.RightParameter is null)
            {
                return;
            }
        }

        private static void ValidateSelfParameter(
            TyhpOperatorOverloadAst overload,
            OverloadableOperator opEnum,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            // Conversion operators legitimately operate between 'self' and another type: a
            // convert-to overload (`operator convert(self $v): int`) has a 'self' operand, while a
            // convert-from overload (`operator convert(int $v)`) takes the source type and produces
            // 'self' implicitly. Neither form should be forced to declare a 'self' parameter.
            if (opEnum == OverloadableOperator.Convert)
            {
                return;
            }

            var leftIsSelf = IsSelfParameter(overload.LeftParameter);
            var rightIsSelf = IsSelfParameter(overload.RightParameter);

            if (!leftIsSelf && !rightIsSelf)
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    overload,
                    MessageCode.CheckerMagicMethodSignature,
                    overload.Op?.ValueString ?? overload.Identifier ?? "operator",
                    "at least one operand must be of type 'self'");
            }
        }

        private static void ValidateReturnType(
            TyhpOperatorOverloadAst overload,
            OverloadableOperator opEnum,
            ICheckedType returnType,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (!IsComparisonOperator(opEnum))
            {
                return;
            }

            if (opEnum == OverloadableOperator.CompareSpaceship)
            {
                if (!CheckerHelpers.IsBuiltInName(returnType, "int"))
                {
                    CheckerHelpers.ReportError(
                        diagnostics,
                        state,
                        overload.ReturnType!,
                        MessageCode.CheckerIncompatibleReturnType,
                        returnType.DisplayName,
                        "int");
                }

                return;
            }

            if (!CheckerHelpers.IsBoolType(returnType))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    overload.ReturnType!,
                    MessageCode.CheckerIncompatibleReturnType,
                    returnType.DisplayName,
                    "bool");
            }
        }

        private static void ValidateConvertNotSelfToSelf(
            TyhpOperatorOverloadAst overload,
            OverloadableOperator opEnum,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (opEnum != OverloadableOperator.Convert)
            {
                return;
            }

            // A convert-to overload has a `self` operand; converting `self` → `self`/`static` is
            // meaningless (the identity case is a plain hand-written method, not an operator).
            // Reject it.
            if (IsSelfParameter(overload.LeftParameter)
                && overload.ReturnType is not null
                && GetTypeName(overload.ReturnType) is { } returnName
                && (string.Equals(returnName, "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(returnName, "static", StringComparison.OrdinalIgnoreCase)))
            {
                CheckerHelpers.ReportError(
                    diagnostics,
                    state,
                    overload,
                    MessageCode.CheckerMagicMethodSignature,
                    "convert",
                    "a convert operator cannot convert 'self' to 'self'");
            }
        }

        private static bool IsSelfParameter(PhpParameterAst? parameter)
        {
            if (parameter?.Type is null)
            {
                return false;
            }

            return GetTypeName(parameter.Type) is { } name
                && (string.Equals(name, "self", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "static", StringComparison.OrdinalIgnoreCase));
        }

        private static string? GetTypeName(ITypeExpression typeExpr) =>
            typeExpr switch
            {
                PhpBuiltinTypeAst builtin => builtin.Identifier,
                PhpNamedTypeAst named => named.Name?.ValueString ?? named.Name?.Identifier,
                PhpTypeExpressionAst composite =>
                    composite.Types?.GetAllNotNull().FirstOrDefault() is ITypeExpression inner
                        ? GetTypeName(inner)
                        : null,
                _ => null,
            };

        private static bool IsComparisonOperator(OverloadableOperator opEnum) =>
            opEnum is OverloadableOperator.CompareGreaterThan
                or OverloadableOperator.CompareLessThan
                or OverloadableOperator.CompareGreaterThanOrEqualTo
                or OverloadableOperator.CompareLessThanOrEqualTo
                or OverloadableOperator.CompareEqual
                or OverloadableOperator.CompareNotEqual
                or OverloadableOperator.CompareIdentical
                or OverloadableOperator.CompareNotIdentical
                or OverloadableOperator.CompareSpaceship
                or OverloadableOperator.IsEmpty;
    }
}
