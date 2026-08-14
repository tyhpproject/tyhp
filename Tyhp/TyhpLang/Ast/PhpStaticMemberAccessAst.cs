using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpStaticMemberAccessAst : Base2Ast, IDereferenceableSuffix
    {
        public IExpression? Member => Children.ElementAtOrDefault(0) as IExpression;

        public static PhpStaticMemberAccessAst Create(IExpression member, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpStaticMemberAccessAst
            {
                Children = [member],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        internal static PhpStaticMemberAccessAst CreateFromContext(IExpression member, Base2Ast context)
        {
            var result = new PhpStaticMemberAccessAst
            {
                Children = [member],
            };
            result.SetContext(context);
            return result;
        }
    }
}
