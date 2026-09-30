using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Classifies a Tyhp <c>extension { }</c> member body the same way Phase 5 does:
    /// <see cref="PhpFunctionDeclAst.IsShortSyntax"/> vs a single <c>return expr;</c> vs
    /// multi-statement. Compiled library tyhpdef uses this to choose the mapping and backer stub.
    /// </summary>
    internal static class TyhpdefExtensionBody
    {
        internal enum Form
        {
            ShortArrow,
            SingleReturnBrace,
            MultiStatement,
        }

        internal static Form Classify(IBase2Ast? declaringNode)
        {
            if (IsShortSyntax(declaringNode))
            {
                return Form.ShortArrow;
            }

            return TryGetSingleReturnExpression(declaringNode, out _)
                ? Form.SingleReturnBrace
                : Form.MultiStatement;
        }

        internal static bool IsShortSyntax(IBase2Ast? declaringNode) =>
            declaringNode switch
            {
                PhpFunctionDeclAst function => function.IsShortSyntax,
                PhpMethodDeclAst method => method.IsShortSyntax,
                TyhpOperatorOverloadAst op => op.IsShortSyntax,
                _ => false,
            };

        /// <summary>
        /// True when PHP keeps a backer method for this form: brace bodies always, short
        /// <c>=&gt;</c> members never. Mirrors <c>SpliceAst.ExtensionMemberEmitsPhpBacker</c>
        /// (Phase 5) exactly — the emitted PHP backer never depends on whether library tyhpdef generation could
        /// copy the member's expression, so this must not either.
        /// </summary>
        internal static bool HasPhpBacker(Form form) => form != Form.ShortArrow;

        internal static bool TryGetCopyableExpression(IBase2Ast? declaringNode, out IExpression expression)
        {
            expression = null!;
            var form = Classify(declaringNode);
            if (form == Form.MultiStatement)
            {
                return false;
            }

            return TryGetSingleReturnExpression(declaringNode, out expression);
        }

        /// <summary>
        /// Same shape test as the splice engine: ignore empty/nop statements, then require a
        /// single <c>return expr</c> (statement, jump, or unary <c>return</c>).
        /// </summary>
        internal static bool TryGetSingleReturnExpression(IBase2Ast? declaringNode, out IExpression expression)
        {
            expression = null!;
            var body = GetBody(declaringNode);
            if (body is null)
            {
                return false;
            }

            var statements = body.GetAllNotNull()
                .Where(s => s is not PhpEmptyStatementAst and not PhpNopStatementAst)
                .ToList();
            if (statements.Count != 1)
            {
                return false;
            }

            switch (statements[0])
            {
                case PhpReturnStatementAst { Expression: IExpression expr }:
                    expression = expr;
                    return true;
                case PhpJumpStatementAst jump
                    when jump.JumpType == PhpJumpType.Return && jump.Expression is IExpression jumpExpr:
                    expression = jumpExpr;
                    return true;
                case PhpUnaryOpAst unary
                    when string.Equals(unary.Operator?.ValueString, "return", StringComparison.OrdinalIgnoreCase)
                    && unary.Operand is IExpression operand:
                    expression = operand;
                    return true;
                default:
                    return false;
            }
        }

        internal static PhpStatementBlockAst? GetBody(IBase2Ast? declaringNode) =>
            declaringNode switch
            {
                PhpFunctionDeclAst function => function.Body,
                PhpMethodDeclAst method => method.Body,
                TyhpOperatorOverloadAst op => op.Body,
                TyhpdefInlineExtensionFunctionAst inline => inline.Method?.Body,
                PhpInlineFunctionAst closure => closure.Body,
                PhpStatementBlockAst block => block,
                _ => null,
            };
    }
}
