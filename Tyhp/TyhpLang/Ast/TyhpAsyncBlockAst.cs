using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Async block expression: <c>async { ... }</c>.
    /// Evaluates to a running <c>Promise&lt;T&gt;</c> (not a callable). Inner <c>return</c>
    /// completes the promise; inner <c>await</c> is legal. Distinct from
    /// <c>async function () { }</c> / <c>async fn() =&gt;</c>, which remain callables.
    /// </summary>
    public class TyhpAsyncBlockAst : Base2Ast, IExpression
    {
        public PhpStatementBlockAst? Body => Children.ElementAtOrDefault(0) as PhpStatementBlockAst;

        public static TyhpAsyncBlockAst Create(
            PhpStatementBlockAst body,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpAsyncBlockAst
            {
                Children = [body],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
