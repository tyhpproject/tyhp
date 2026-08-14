using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents the compile-time construct: nameof(expr)
    /// Returns the string name of a symbol (variable, property, method, class, etc.)
    /// at compile time for refactoring-safe string references.
    /// Examples:
    ///   nameof($this->name)    => "name"
    ///   nameof(self::getName)  => "getName"
    ///   nameof(User)           => "User"
    ///   nameof(fn (User $u) => $u->firstName) => "firstName"
    /// </summary>
    public class TyhpNameofAst : Base2Ast, IExpression
    {
        public IExpression? Expression => Children.ElementAtOrDefault(0) as IExpression;

        public static TyhpNameofAst Create(IExpression expression, ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpNameofAst
            {
                Children = [expression],
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
}
