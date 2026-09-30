using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpFunctionDeclAst : Base2Ast, IAttributedStatement, IExtensionMemberAst
    {
        private const short RETURNS_REF_FLAG = -10;
        private const short IS_SHORT_SYNTAX_FLAG = -11;
        public bool ReturnsRef => HasFlag(RETURNS_REF_FLAG);
        /// <summary>
        /// True when the declaration was authored as <c>fn name(...) => expr;</c>
        /// (desugared at visit time to a <c>{ return expr; }</c> body).
        /// </summary>
        public bool IsShortSyntax => HasFlag(IS_SHORT_SYNTAX_FLAG);
        public PhpParameterListAst? Parameters => Children.ElementAtOrDefault(0) as PhpParameterListAst;
        public ITypeExpression? ReturnType => Children.ElementAtOrDefault(1) as ITypeExpression;
        public PhpStatementBlockAst? Body => Children.ElementAtOrDefault(2) as PhpStatementBlockAst;
        
        public static PhpFunctionDeclAst Create(string? name, bool returnsRef, PhpParameterListAst parameters, ITypeExpression? returnType, PhpStatementBlockAst? body, ParserRuleContext context, string? languageMode = null, string? docComment = null, bool isShortSyntax = false)
        {
            var result = new PhpFunctionDeclAst
            {
                Identifier = name ?? "",
                Children = [parameters, returnType, body],
                DocComment = docComment,
            };

            result.SetFlag(RETURNS_REF_FLAG, returnsRef);
            result.SetFlag(IS_SHORT_SYNTAX_FLAG, isShortSyntax);

            result.SetContext(context, languageMode);
            return result;
        }
    }
} 