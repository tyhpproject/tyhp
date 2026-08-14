using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpTraitUseAst : Base2Ast, IClassMember
    {
        public PhpClassNameListAst? TraitNames => Children.ElementAtOrDefault(0) as PhpClassNameListAst;
        public PhpTraitAdaptationListAst? Adaptations => Children.ElementAtOrDefault(1) as PhpTraitAdaptationListAst;
        
        public static PhpTraitUseAst Create(PhpClassNameListAst? traitNames, PhpTraitAdaptationListAst? adaptations, ParserRuleContext context, string? languageMode = null)
        {           
            var result = new PhpTraitUseAst
            {
                Children = [traitNames, adaptations],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 