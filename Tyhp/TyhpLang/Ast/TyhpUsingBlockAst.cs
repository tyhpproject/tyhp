using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// AST node for a Tyhp using block statement.
    /// Provides deterministic resource disposal via try/finally compilation.
    /// Examples:
    ///   using ($db = new DatabaseConnection()) { ... }
    ///   using (DatabaseConnection $db = new DatabaseConnection()) { ... }
    ///   using await ($conn = new AsyncConnection()) { ... }
    /// </summary>
    public class TyhpUsingBlockAst : Base2Ast, IStatement
    {
        private const short IS_ASYNC_FLAG = 7100;

        /// <summary>Whether this is an async using block (`using await`).</summary>
        public bool IsAsync => HasFlag(IS_ASYNC_FLAG);

        /// <summary>The resource declarations in this using block.</summary>
        public IEnumerable<TyhpUsingResourceAst> Resources =>
            Children.OfType<TyhpUsingResourceAst>();

        /// <summary>The body statement of the using block.</summary>
        public IStatement? Body => Children.LastOrDefault() as IStatement;

        /// <summary>Creates a new TyhpUsingBlockAst.</summary>
        public static TyhpUsingBlockAst Create(
            bool isAsync,
            IEnumerable<TyhpUsingResourceAst> resources,
            IStatement body,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var children = new List<IBase2Ast>();
            children.AddRange(resources);
            children.Add(body);

            var result = new TyhpUsingBlockAst
            {
                Children = children,
            };
            result.SetContext(context, languageMode);
            if (isAsync) result.SetFlag(IS_ASYNC_FLAG, true);
            return result;
        }
    }
}
