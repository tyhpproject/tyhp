using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpObjectTypeDeclAst : Base2Ast, IAttributedStatement, ITopStatement
    {
        private const short IS_ANONYMOUS_CLASS_FLAG = -1;
        
        public TokenValueAst? DeclType => Children.ElementAtOrDefault(0) as TokenValueAst;
        public PhpModifierListAst? Modifiers => Children.ElementAtOrDefault(1) as PhpModifierListAst;
        public IClassName? Extends => Children.ElementAtOrDefault(2) as IClassName;
        public PhpClassNameListAst? Implements => Children.ElementAtOrDefault(3) as PhpClassNameListAst;
        public ITypeExpression? BackingType => Children.ElementAtOrDefault(4) as ITypeExpression;
        public PhpClassBodyAst? Body => Children.ElementAtOrDefault(5) as PhpClassBodyAst;

        public bool IsAnonymousClass => HasFlag(IS_ANONYMOUS_CLASS_FLAG);
        
        public static PhpObjectTypeDeclAst Create(TokenValueAst declType, string? name, PhpModifierListAst? modifiers, IClassName? extends, PhpClassNameListAst? implements, ITypeExpression? backingType, PhpClassBodyAst? body, string? docComment, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpObjectTypeDeclAst
            {
                Identifier = name ?? "",
                Children = [declType, modifiers, extends, implements, backingType, body],
                DocComment = docComment,
            };
            if (String.IsNullOrWhiteSpace(name)) {
                result.SetFlag(IS_ANONYMOUS_CLASS_FLAG);
                result.Identifier = "anonClass@" + Guid.NewGuid().ToString("N");
            }

            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpObjectTypeDeclAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpObjectTypeDeclAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpObjectTypeDeclAst
            {
                Identifier = "<error>",
                Children = [TokenValueAst.CreateError(context, languageMode), null, null, null, null, null],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 