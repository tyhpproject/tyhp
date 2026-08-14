using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpNamedTypeAst : Base2Ast, ITypeExpression
    {
        public IExpression? Name => Children.ElementAtOrDefault(0) as IExpression;
        
        public static PhpNamedTypeAst Create(IExpression? name, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpNamedTypeAst
            {
                Children = [name],
            };
            
            result.SetContext(context, languageMode);
            
            return result;
        }

        /// <summary>
        /// Wraps a class-name AST (<see cref="PhpNameAst"/> / <see cref="IClassName"/>) as a
        /// <see cref="ITypeExpression"/> so binder lists that store type expressions
        /// (<c>ImplementsTypes</c>, trait uses) can retain the name. Trait / interface name
        /// lists are typed as <see cref="IClassName"/>, which does not implement
        /// <see cref="ITypeExpression"/>.
        /// </summary>
        public static PhpNamedTypeAst WrapClassName(IExpression name, Base2Ast contextSource)
        {
            var result = new PhpNamedTypeAst
            {
                Children = [name],
            };
            result.SetContext(contextSource);
            return result;
        }
    }
} 