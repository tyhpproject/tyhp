using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpConditionalArmAst : Base2Ast, ICaseItem
    {
        private const short IS_DEFAULT_FLAG = -8;
        
        public PhpExpressionListAst? Conditions => Children.ElementAtOrDefault(0) as PhpExpressionListAst;
        public PhpStatementBlockAst? Body => Children.ElementAtOrDefault(1) as PhpStatementBlockAst;
        
        public bool IsDefault => HasFlag(IS_DEFAULT_FLAG);
        
        public static PhpConditionalArmAst Create(PhpExpressionListAst? conditions, PhpStatementBlockAst body, bool isDefault, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpConditionalArmAst
            {
                Children = [conditions, body],
            };

            result.SetContext(context, languageMode);

            result.SetFlag(IS_DEFAULT_FLAG, isDefault);

            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpConditionalArmAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpConditionalArmAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpConditionalArmAst
            {
                Children = [null, PhpStatementBlockAst.CreateError(context, languageMode)],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 