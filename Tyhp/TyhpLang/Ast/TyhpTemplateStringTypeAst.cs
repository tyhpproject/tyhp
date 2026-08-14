using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// A template string type in type position (Story 08.5 Phase 6), e.g. <c>"prefix-${T}-suffix"</c>.
    /// </summary>
    public class TyhpTemplateStringTypeAst : Base2Ast, ITypeExpression
    {
        public PhpEncapsListAst? EncapsList => Children.ElementAtOrDefault(0) as PhpEncapsListAst;

        public static TyhpTemplateStringTypeAst Create(
            PhpEncapsListAst encapsList,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpTemplateStringTypeAst
            {
                Children = [encapsList],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
