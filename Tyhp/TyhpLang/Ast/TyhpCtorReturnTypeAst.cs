using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a Tyhp constructor return type annotation.
    ///
    /// Grammar:
    ///   tyhpCtorReturnType
    ///     : T_SYM_COLON TokenValue=T_TYHP_VOID
    ///     | T_SYM_COLON TokenValue=T_TYHP_PARENT ArgumentsList=argumentList
    ///     ;
    ///
    /// The constructor may declare `: void` (no return) or `: parent(args)`
    /// (delegating to the parent constructor).
    /// </summary>
    public class TyhpCtorReturnTypeAst : Base2Ast
    {
        /// <summary>
        /// The return type token ("void" or "parent").
        /// </summary>
        public TokenValueAst? TypeToken => Children.ElementAtOrDefault(0) as TokenValueAst;

        /// <summary>
        /// Optional argument list for parent constructor delegation.
        /// Only present when the return type is `: parent(args)`.
        /// </summary>
        public PhpArgumentListAst? Arguments => Children.ElementAtOrDefault(1) as PhpArgumentListAst;

        public static TyhpCtorReturnTypeAst Create(
            TokenValueAst typeToken,
            PhpArgumentListAst? arguments,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpCtorReturnTypeAst
            {
                Identifier = typeToken.ValueString ?? "",
                Children = [typeToken, arguments],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
