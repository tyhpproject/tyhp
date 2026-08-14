using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpParameterAst : Base2Ast
    {
        private const short IS_REF_FLAG = -9;
        private const short IS_VARIADIC_FLAG = -10;
        
        // Child order: [0]Type, [1]DefaultValue, [2]Modifiers, [3]PropertyHooks
        public ITypeExpression? Type => Children.ElementAtOrDefault(0) as ITypeExpression;
        public IExpression? DefaultValue => Children.ElementAtOrDefault(1) as IExpression;
        public PhpModifierListAst? Modifiers => Children.ElementAtOrDefault(2) as PhpModifierListAst; // for constructor property promotion
        public IBase2Ast? PropertyHooks => Children.ElementAtOrDefault(3); // PhpPropertyHookListAst - will be created later
        
        public string Name => ValueString ?? "";
        
        public bool IsRef => HasFlag(IS_REF_FLAG);
        
        public bool IsVariadic => HasFlag(IS_VARIADIC_FLAG);
        
        public static PhpParameterAst Create(
            string name,
            ITypeExpression? type,
            bool isRef,
            bool isVariadic,
            IExpression? defaultValue,
            PhpModifierListAst? modifiers,
            IBase2Ast? propertyHooks,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new PhpParameterAst
            {
                Children = [type, defaultValue, modifiers, propertyHooks],
                ValueString = name,
            };
            
            result.SetContext(context, languageMode);
            
            result.SetFlag(IS_REF_FLAG, isRef);
            result.SetFlag(IS_VARIADIC_FLAG, isVariadic);
            
            return result;
        }
    }
} 