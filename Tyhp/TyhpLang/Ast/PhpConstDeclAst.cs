using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpConstDeclAst : Base2Ast
    {
        public IExpression? Value => Children.ElementAtOrDefault(0) as IExpression;

        /// <summary>
        /// Visibility / final modifiers from the enclosing class <c>const</c> statement.
        /// Null for top-level (file-scope) constants, which have no modifiers in PHP.
        /// </summary>
        public PhpModifierListAst? Modifiers => Children.ElementAtOrDefault(1) as PhpModifierListAst;

        /// <summary>
        /// Optional type annotation from a typed class constant (<c>const string X = …</c>, PHP 8.3+).
        /// Null for untyped class constants and for top-level (file-scope) constants.
        /// </summary>
        public ITypeExpression? Type => Children.ElementAtOrDefault(2) as ITypeExpression;

        public static PhpConstDeclAst Create(
            string name,
            IExpression value,
            string? docComment,
            ParserRuleContext context,
            string? languageMode = null,
            PhpModifierListAst? modifiers = null,
            ITypeExpression? type = null)
        {
            var result = new PhpConstDeclAst
            {
                Identifier = name,
                Children = [value, modifiers, type],
                DocComment = docComment,
            };

            result.SetContext(context, languageMode);

            return result;
        }
    }
}
