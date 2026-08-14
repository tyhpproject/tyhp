using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpTraitAliasAst : Base2Ast, ITraitAdaptation
    {
        private const short NEW_MODIFIER_OFFSET = 15000;
        
        public PhpModifier? NewModifier 
        {
            get
            {
                var enumFlags = GetEnumFlags<PhpModifier>(NEW_MODIFIER_OFFSET);
                return enumFlags.Any() ? enumFlags.First() : null;
            }
        }

        public PhpTraitMemberRefAst? MethodReference => Children.ElementAtOrDefault(0) as PhpTraitMemberRefAst;

        public static PhpTraitAliasAst Create(PhpTraitMemberRefAst methodReference, string newName, ParserRuleContext context, string? languageMode = null)
            => Create(methodReference, newName, null, context, languageMode);

        public static PhpTraitAliasAst Create(PhpTraitMemberRefAst methodReference, TokenValueAst newName, ParserRuleContext context, string? languageMode = null)
            => Create(methodReference, newName.ValueString ?? "", null, context, languageMode);
        
        public static PhpTraitAliasAst Create(PhpTraitMemberRefAst methodReference, string newName, PhpModifier? newModifier, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpTraitAliasAst
            {
                Children = [methodReference],
                Identifier = newName,
            };

            if (newModifier.HasValue) {
                result.SetFlag(NEW_MODIFIER_OFFSET, newModifier.Value);
            }

            result.SetContext(context, languageMode);

            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpTraitAliasAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpTraitAliasAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpTraitAliasAst
            {
                Children = [null],
                Identifier = "<error>",
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 