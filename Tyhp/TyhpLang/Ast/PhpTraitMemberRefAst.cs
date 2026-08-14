using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpTraitMemberRefAst : Base2Ast
    {
        public IClassName? TraitName => Children.ElementAtOrDefault(0) as IClassName;
        public IClassMemberName? MemberName => Children.ElementAtOrDefault(1) as IClassMemberName;
        
        public static PhpTraitMemberRefAst Create(IClassMemberName memberName, ParserRuleContext context, string? languageMode = null)
            => Create(null, memberName, context, languageMode);

        public static PhpTraitMemberRefAst Create(IClassName? traitName, IClassMemberName memberName, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpTraitMemberRefAst
            {
                Children = [traitName, memberName],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 