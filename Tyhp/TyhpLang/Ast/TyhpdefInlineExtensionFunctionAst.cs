using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Tyhpdef class-body <c>extension fn</c> member: wraps the lowered <see cref="PhpMethodDeclAst"/>.
    /// Also implements <see cref="IExtensionMemberAst"/> so the same shape can represent a function member of a
    /// standalone tyhpdef <c>extension Name { }</c> block (Story 20), which is a list of <see cref="IExtensionMemberAst"/>.
    /// </summary>
    public class TyhpdefInlineExtensionFunctionAst : Base2Ast, IClassMember, IExtensionMemberAst
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
