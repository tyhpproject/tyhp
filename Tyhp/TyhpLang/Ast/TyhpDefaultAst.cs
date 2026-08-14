using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents the compile-time construct: default(type)
    /// Returns the default value for a given type.
    /// Especially useful with generic type parameters.
    /// Examples:
    ///   default(int)      => 0
    ///   default(string)   => ""
    ///   default(bool)     => false
    ///   default(array)    => []
    ///   default(?string)  => null
    /// </summary>
    public class TyhpDefaultAst : Base2Ast, IExpression
    {
        public ITypeExpression? TypeExpression => Children.ElementAtOrDefault(0) as ITypeExpression;

        public static TyhpDefaultAst Create(ITypeExpression typeExpression, ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpDefaultAst
            {
                Children = [typeExpression],
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
}
