using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Checker.Rules
{
    /// <summary>
    /// <c>async { ... }</c> is a Promise-valued expression. Inference (not default child
    /// traversal) is the single body-check path so await/returns run in an async closure scope.
    /// </summary>
    public sealed class AsyncBlockRule : ICheckerRule
    {
        public IEnumerable<Type> HandledNodeTypes => [typeof(TyhpAsyncBlockAst)];

        public bool SuppressChildTraversal(IBase2Ast node) => true;

        public void Check(IBase2Ast node, CheckerState state, CheckerRuleContext context, DiagnosticBag diagnostics)
        {
            if (node is TyhpAsyncBlockAst block)
            {
                context.ResolveExpressionType(block, state);
            }
        }
    }
}
