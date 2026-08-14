using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpTraitPrecedenceAst : Base2Ast, ITraitAdaptation
    {
        public PhpTraitMemberRefAst? MethodReference => Children.ElementAtOrDefault(0) as PhpTraitMemberRefAst;
        public PhpClassNameListAst? InsteadOfTraits => Children.ElementAtOrDefault(1) as PhpClassNameListAst;
        
        
        public static PhpTraitPrecedenceAst Create(PhpTraitMemberRefAst methodReference, PhpClassNameListAst insteadOfTraits, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpTraitPrecedenceAst
            {
                Children = [methodReference, insteadOfTraits],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 