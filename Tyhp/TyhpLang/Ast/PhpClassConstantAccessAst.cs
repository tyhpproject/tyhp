using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpClassConstantAccessAst : Base2Ast, IDereferenceableSuffix
    {
        public IExpression? Member => Children.ElementAtOrDefault(0) as IExpression;

        public static PhpClassConstantAccessAst Create(IExpression member, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpClassConstantAccessAst
            {
                Children = [member],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
