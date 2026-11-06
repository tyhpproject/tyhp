using Antlr4.Runtime;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpNewAst : Base2Ast, IExpression, IDereferenceableBase
    {
        private const short IS_LAZY_FLAG = -1;

        /// <summary>
        /// True for <c>lazy new Class(args)</c>. Arguments are still evaluated at the expression.
        /// Anonymous classes and dynamic <c>new</c> do not set this.
        /// </summary>
        public bool IsLazy
        {
            get => HasFlag(IS_LAZY_FLAG);
            internal set => SetFlag(IS_LAZY_FLAG, value);
        }

        public IClassNameReference? ClassName => Children.ElementAtOrDefault(0) as IClassNameReference;
        public PhpArgumentListAst? Arguments => Children.ElementAtOrDefault(1) as PhpArgumentListAst;
        public PhpObjectTypeDeclAst? AnonymousClass => Children.ElementAtOrDefault(2) as PhpObjectTypeDeclAst;

        public static PhpNewAst Create(IClassNameReference? className, PhpArgumentListAst? arguments, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpNewAst
            {
                Children = [className, arguments],
            };

            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>Creates a <c>new ClassName(...)</c> AST node for emitter synthesis (no parse context).</summary>
        internal static PhpNewAst CreateFromContext(
            IClassNameReference? className,
            PhpArgumentListAst? arguments,
            Base2Ast context)
        {
            var result = new PhpNewAst
            {
                Children = [className, arguments],
            };
            result.SetContext(context);
            return result;
        }

        public static PhpNewAst CreateAnonymous(PhpObjectTypeDeclAst anonymousClass, PhpArgumentListAst? arguments, ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpNewAst
            {
                Children = [null, arguments, anonymousClass],
            };

            result.SetContext(context, languageMode);
            return result;
        }

        /// <summary>
        /// Creates an error placeholder PhpNewAst for error recovery.
        /// This allows parsing to continue after encountering an error.
        /// </summary>
        public static PhpNewAst CreateError(ParserRuleContext context, string? languageMode = null)
        {
            var result = new PhpNewAst
            {
                Children = [PhpNameAst.CreateError(context, languageMode), null],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
} 