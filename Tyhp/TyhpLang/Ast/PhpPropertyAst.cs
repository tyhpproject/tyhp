using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpPropertyAst : Base2Ast
    {
        public IExpression? DefaultValue => Children.ElementAtOrDefault(0) as IExpression;
        public PhpPropertyHookListAst? Hooks => Children.ElementAtOrDefault(1) as PhpPropertyHookListAst;
        
        public static PhpPropertyAst Create(string? name, IExpression? defaultValue, PhpPropertyHookListAst? hooks, string? docComment, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpPropertyAst
            {
                Identifier = name ?? "",
                Children = [defaultValue, hooks],
                DocComment = docComment,
            };

            result.SetContext(context, languageMode);

            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpPropertyAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpPropertyAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpPropertyAst
            {
                Identifier = "<error>",
                Children = [null, null],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 