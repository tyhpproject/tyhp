using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a Tyhp extension declaration.
    ///
    /// Grammar:
    ///   tyhpExtensionDeclarationStatement
    ///     : T_TYHP_EXTENSION Identifier=T_STRING Extends=extendsFrom
    ///         FindDocComment=T_OPEN_CURLY_BRACE FunctionList=tyhpExtensionFunctionList
    ///         T_CLOSE_CURLY_BRACE
    ///     ;
    ///
    /// Used as a top-level statement via topStatementGrammarAddon #tyhpExtensionDecl.
    /// </summary>
    public class TyhpExtensionDeclAst : Base2Ast, ITopStatement
    {
        /// <summary>
        /// The class/type being extended (from the extendsFrom clause).
        /// </summary>
        public IClassName? Extends => Children.ElementAtOrDefault(0) as IClassName;

        /// <summary>
        /// Extension body members (functions and operator overloads).
        /// </summary>
        public TyhpExtensionFunctionListAst? FunctionList => Children.ElementAtOrDefault(1) as TyhpExtensionFunctionListAst;

        public static TyhpExtensionDeclAst Create(
            string name,
            IClassName? extends,
            TyhpExtensionFunctionListAst functionList,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpExtensionDeclAst
            {
                Identifier = name,
                Children = [extends, functionList],
                DocComment = docComment,
            };
            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder for error recovery when ANTLR left required
        /// children null (e.g. truncated <c>extension Foo</c> with no
        /// <c>extends &lt;type&gt; { … }</c>).
        /// </summary>
        public static TyhpExtensionDeclAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpExtensionDeclAst
            {
                Identifier = "<error>",
                Children = [null, TyhpExtensionFunctionListAst.Create(null, context, languageMode)],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
