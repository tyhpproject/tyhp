using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpFunctionDeclAst : Base2Ast, IAttributedStatement, IExtensionMemberAst
    {
        private const short RETURNS_REF_FLAG = -10;
        public bool ReturnsRef => HasFlag(RETURNS_REF_FLAG);
        public PhpParameterListAst? Parameters => Children.ElementAtOrDefault(0) as PhpParameterListAst;
        public ITypeExpression? ReturnType => Children.ElementAtOrDefault(1) as ITypeExpression;
        public PhpStatementBlockAst? Body => Children.ElementAtOrDefault(2) as PhpStatementBlockAst;
        
        public static PhpFunctionDeclAst Create(string? name, bool returnsRef, PhpParameterListAst parameters, ITypeExpression? returnType, PhpStatementBlockAst? body, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpFunctionDeclAst
            {
                Identifier = name ?? "",
                Children = [parameters, returnType, body],
            };

            result.SetFlag(RETURNS_REF_FLAG, returnsRef);

            result.SetContext(context, languageMode);
            return result;
        }
    }
} 