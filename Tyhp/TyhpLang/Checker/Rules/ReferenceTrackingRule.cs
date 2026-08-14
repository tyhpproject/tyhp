using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>Tracks pass-by-reference assignments and call-site reference arguments.</summary>
    public sealed class ReferenceTrackingRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes =>
        [
            typeof(PhpBinaryOpAst),
            typeof(PhpDereferenceableAst),
        ];

        public bool SuppressChildTraversal(IBase2Ast node) => false;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            switch (node)
            {
                case PhpBinaryOpAst binary:
                    CheckReferenceAssignment(binary, state, diagnostics);
                    break;
                case PhpDereferenceableAst deref when deref.Suffix is PhpCallAst call:
                    CheckReferenceArguments(deref, call, state, context, diagnostics);
                    break;
            }
        }

        private static void CheckReferenceAssignment(
            PhpBinaryOpAst binary,
            CheckerState state,
            DiagnosticBag diagnostics)
        {
            if (!string.Equals(binary.Operator?.ValueString, "=", StringComparison.Ordinal))
            {
                return;
            }

            if (binary.Left is not PhpVariableAst leftVar || binary.Right is not PhpVariableAst rightVar)
            {
                return;
            }

            var leftName = CheckerHelpers.GetVariableName(leftVar);
            var rightName = CheckerHelpers.GetVariableName(rightVar);
            if (leftName is null || rightName is null)
            {
                return;
            }

            if (!IsReferenceAssignment(binary.Right))
            {
                return;
            }

            var leftState = state.LookupVariable(leftName);
            var rightState = state.LookupVariable(rightName);
            if (leftState is not null && rightState is not null)
            {
                leftState.JoinReferenceGroup(rightState, leftName, rightName);
            }
        }

        private static bool IsReferenceAssignment(IExpression expression) =>
            expression is PhpUnaryOpAst { Operator.ValueString: "&" };

        private static void CheckReferenceArguments(
            PhpDereferenceableAst deref,
            PhpCallAst call,
            CheckerState state,
            CheckerRuleContext context,
            DiagnosticBag diagnostics)
        {
            IReadOnlyList<ParameterInfo>? parameters = null;
            if (deref.Base is PhpNameAst nameAst)
            {
                // Same call-site resolution as TypeCompatibilityRule.CheckCall: BoundSymbol is
                // null for free-function names inside bodies.
                if (CheckerHelpers.ResolveFreeFunction(
                        nameAst, state, context.SymbolTree, context.GlobalScope) is { } function)
                {
                    parameters = function.Parameters;
                }
            }
            else if (deref.Base is PhpDereferenceableAst chain
                && chain.Base is not null
                && chain.Suffix is PhpInstanceMemberAccessAst memberAccess)
            {
                var receiverType = context.ResolveExpressionType(chain.Base, state);
                var methodName = memberAccess.MemberName switch
                {
                    PhpNameAst name => name.ValueString,
                    TokenValueAst token => token.ValueString,
                    IExpression expr => expr.Identifier,
                    _ => memberAccess.MemberName?.Identifier,
                };
                if (methodName is not null
                    && CheckerHelpers.TryGetObjectDeclaration(receiverType) is { } objectDecl
                    && context.SymbolTree.ResolveMember(methodName, objectDecl, new DiagnosticBag())
                        is ObjectMethodSymbol method)
                {
                    parameters = method.Parameters;
                }
            }

            if (parameters is null || call.Arguments is null)
            {
                return;
            }

            var positionalIndex = 0;
            foreach (var arg in call.Arguments.GetAllNotNull())
            {
                if (arg.IsVariadic)
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
                    param = parameters[positionalIndex];
                    positionalIndex++;
                }

                if (param is not { IsByReference: true })
                {
                    continue;
                }

                if (!IsValidRefArgument(arg.Expression))
                {
                    CheckerHelpers.ReportError(
                        diagnostics, state, arg, MessageCode.CheckerRefArgMustBeVariable);
                }
            }
        }

        private static bool IsValidRefArgument(IExpression? expression) =>
            expression is PhpVariableAst
            || expression is PhpDereferenceableAst { Suffix: PhpArrayAccessAst }
            || expression is PhpDereferenceableAst { Suffix: PhpInstanceMemberAccessAst };
    }
}
