using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpModifierListAst : Base2Ast
    {
        private const short MODIFIER_OFFSET = 13000;
        
        public IEnumerable<PhpModifier> Modifiers => GetEnumFlags<PhpModifier>(MODIFIER_OFFSET);
        
        public static PhpModifierListAst Create(IEnumerable<PhpModifier> modifiers, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpModifierListAst();

            result.SetContext(context, languageMode);
            result.SetFlag(MODIFIER_OFFSET, modifiers);

            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpModifierListAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpModifierListAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpModifierListAst();
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 