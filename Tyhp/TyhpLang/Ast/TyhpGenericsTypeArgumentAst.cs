using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class TyhpGenericsTypeArgumentAst : Base2Ast
    {
        public PhpNameAst? Name => Children.ElementAtOrDefault(0) as PhpNameAst;
        public ITypeExpression? TypeConstraint => Children.ElementAtOrDefault(1) as ITypeExpression;
        public ITypeExpression? DefaultType => Children.ElementAtOrDefault(2) as ITypeExpression;

        public static TyhpGenericsTypeArgumentAst Create(PhpNameAst name, ITypeExpression? typeConstraint, ITypeExpression? defaultType, ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpGenericsTypeArgumentAst
            {
                Identifier = name?.ValueString ?? "",
                Children = [name, typeConstraint, defaultType],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
