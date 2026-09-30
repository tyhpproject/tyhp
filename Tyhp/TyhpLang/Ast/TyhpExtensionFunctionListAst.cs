using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Members of a Tyhp <c>extension { ... }</c> body: functions, operator overloads,
    /// and one-level <c>extends Type { }</c> groups.
    ///
    /// Grammar:
    ///   tyhpExtensionFunctionList
    ///     : Items+=tyhpExtensionMember*
    ///     ;
    /// </summary>
    public class TyhpExtensionFunctionListAst : NodeListAst<IExtensionMemberAst, TyhpExtensionFunctionListAst>
    {
    }
}
