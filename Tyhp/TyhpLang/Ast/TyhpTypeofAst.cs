using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents the compile-time construct: typeof(type)
    /// Materializes a runtime <c>\Tyhp\Type</c> value for the given type expression.
    /// Examples:
    ///   typeof(int)              => \Tyhp\Type::int()
    ///   typeof(User)             => \Tyhp\Type::fromClassName(User::class)
    ///   typeof(UserId)           => UserId()  (source alias factory)
    ///   typeof(int|string)       => \Tyhp\Type::union(...)
    ///   typeof(?int)             => \Tyhp\Type::nullable(...)
    ///   typeof(Optional&lt;int&gt;)  => Optional(\Tyhp\Type::int())
    /// </summary>
    public class TyhpTypeofAst : Base2Ast, IExpression
    {
        public ITypeExpression? TypeExpression => Children.ElementAtOrDefault(0) as ITypeExpression;

        public static TyhpTypeofAst Create(ITypeExpression typeExpression, ParserRuleContext context, string? languageMode = null)
        {
            var result = new TyhpTypeofAst
            {
                Children = [typeExpression],
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
}
