using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Tyhpdef class-body <c>extension function</c> / <c>extension fn</c> member: wraps the lowered <see cref="PhpMethodDeclAst"/>.
    /// </summary>
    public class TyhpdefInlineExtensionFunctionAst : Base2Ast, IClassMember
    {
        public PhpMethodDeclAst? Method => Children.ElementAtOrDefault(0) as PhpMethodDeclAst;

        public static TyhpdefInlineExtensionFunctionAst Create(
            PhpMethodDeclAst method,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var result = new TyhpdefInlineExtensionFunctionAst
            {
                Identifier = method.Identifier,
                Children = [method],
            };
            result.SetContext(context, languageMode);
            return result;
        }
    }
}
