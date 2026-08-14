using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpPropertyDeclAst : Base2Ast, IClassMember
    {
        public PhpModifierListAst? Modifiers => Children.ElementAtOrDefault(0) as PhpModifierListAst;
        public ITypeExpression? Type => Children.ElementAtOrDefault(1) as ITypeExpression;
        public PhpPropertyListAst? Properties => Children.ElementAtOrDefault(2) as PhpPropertyListAst;
        
        public static PhpPropertyDeclAst Create(PhpModifierListAst? modifiers, ITypeExpression? type, PhpPropertyListAst properties, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpPropertyDeclAst
            {
                Children = [modifiers, type, properties],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 