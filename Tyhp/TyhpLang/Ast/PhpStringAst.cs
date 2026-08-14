using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpStringAst : Base2Ast, IExpression
    {
        private const short STRING_TYPE_OFFSET = 9000;
        
        // Parts are mix of string literals and expressions for interpolation
        public PhpExpressionListAst? Parts => Children.ElementAtOrDefault(0) as PhpExpressionListAst;
        
        public PhpStringType StringType => GetEnumFlags<PhpStringType>(STRING_TYPE_OFFSET).FirstOrDefault();
        
        public static PhpStringAst Create(PhpExpressionListAst parts, PhpStringType stringType, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpStringAst
            {
                Children = [parts],
            };
            
            result.SetFlag(STRING_TYPE_OFFSET, stringType);
            result.SetContext(context, languageMode);
            
            return result;
        }
    }
} 