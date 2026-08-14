using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpCallAst : Base2Ast, IDereferenceableSuffix
    {
        public PhpArgumentListAst? Arguments => Children.ElementAtOrDefault(0) as PhpArgumentListAst;

        public static PhpCallAst Create(PhpArgumentListAst arguments, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpCallAst
            {
                Children = [arguments],
            };

            result.SetContext(context, languageMode);
            return result;
        }

        internal static PhpCallAst CreateFromContext(PhpArgumentListAst? arguments, Base2Ast context)
        {
            var result = new PhpCallAst
            {
                Children = [arguments],
            };
            result.SetContext(context);
            return result;
        }
    }
} 