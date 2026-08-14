using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpEncapsStringAst : Base2Ast, IDereferenceableBase, IEncapsVarOrString
    {
        public TokenValueAst? TokenValue => Children.ElementAtOrDefault(0) as TokenValueAst;
        
        public static PhpEncapsStringAst Create(TokenValueAst tokenValue, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpEncapsStringAst {
                Children = [tokenValue],
                ValueString = tokenValue.ValueString ?? string.Empty,
            };
            
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 