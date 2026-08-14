using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpPropertyHookAst : Base2Ast
    {
        private const short RETURNS_REF_FLAG = -15;
        private const short EXPRESSION_BODY_FLAG = -16;

        public bool ReturnsRef => HasFlag(RETURNS_REF_FLAG);

        /// <summary>
        /// True when the hook used arrow syntax (<c>get/set =&gt; expr</c>). The visitor rewrites
        /// that as a one-statement block with <c>return expr;</c>; set hooks must then treat the
        /// returned value as the written property value (not a void method return).
        /// </summary>
        public bool IsExpressionBody => HasFlag(EXPRESSION_BODY_FLAG);

        public PhpModifierListAst? Modifiers => Children.ElementAtOrDefault(0) as PhpModifierListAst;
        public PhpParameterListAst? Parameters => Children.ElementAtOrDefault(1) as PhpParameterListAst;
        public IBase2Ast? Body => Children.ElementAtOrDefault(2);

        public static PhpPropertyHookAst Create(
            string? name,
            bool returnsRef,
            PhpModifierListAst? modifiers,
            PhpParameterListAst? parameters,
            IBase2Ast? body,
            ParserRuleContext context,
            string? languageMode = null,
            bool isExpressionBody = false)
        {
            var result = new PhpPropertyHookAst
            {
                Identifier = name ?? "",
                Children = [modifiers, parameters, body],
            };

            result.SetFlag(RETURNS_REF_FLAG, returnsRef);
            result.SetFlag(EXPRESSION_BODY_FLAG, isExpressionBody);
            result.SetContext(context, languageMode);

            return result;
        }
    }
} 