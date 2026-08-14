using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpMethodDeclAst : Base2Ast, IClassMember
    {
        private const short RETURNS_REF_FLAG = -14;
        
        public bool ReturnsRef => HasFlag(RETURNS_REF_FLAG);
        
        public PhpModifierListAst? Modifiers => Children.ElementAtOrDefault(0) as PhpModifierListAst;
        public PhpParameterListAst? Parameters => Children.ElementAtOrDefault(1) as PhpParameterListAst;
        public ITypeExpression? ReturnType => Children.ElementAtOrDefault(2) as ITypeExpression;
        public PhpStatementBlockAst? Body => Children.ElementAtOrDefault(3) as PhpStatementBlockAst;
        
        public static PhpMethodDeclAst Create(string? name, bool returnsRef, PhpModifierListAst? modifiers, PhpParameterListAst? parameters, ITypeExpression? returnType, PhpStatementBlockAst? body, string? docComment, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpMethodDeclAst
            {
                Identifier = name ?? "",
                Children = [modifiers, parameters, returnType, body],
                DocComment = docComment,
            };

            result.SetFlag(RETURNS_REF_FLAG, returnsRef);
            result.SetContext(context, languageMode);

            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpMethodDeclAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpMethodDeclAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpMethodDeclAst
            {
                Identifier = "<error>",
                Children = [null, null, null, null],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 