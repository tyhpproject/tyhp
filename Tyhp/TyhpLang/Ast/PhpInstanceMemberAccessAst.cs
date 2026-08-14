using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpInstanceMemberAccessAst : Base2Ast, IDereferenceableSuffix
    {
        public TokenValueAst? Accessor => Children.ElementAtOrDefault(0) as TokenValueAst;
        public IExpression? MemberName => Children.ElementAtOrDefault(1) as IExpression;

        public static PhpInstanceMemberAccessAst Create(TokenValueAst accessor, IExpression memberName, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpInstanceMemberAccessAst
            {
                Children = [accessor, memberName],
            };
            result.SetContext(context, languageMode);
            return result;
        }

        internal static PhpInstanceMemberAccessAst CreateFromContext(
            TokenValueAst accessor,
            IExpression memberName,
            Base2Ast context)
        {
            var result = new PhpInstanceMemberAccessAst
            {
                Children = [accessor, memberName],
            };
            result.SetContext(context);
            return result;
        }
    }
}
