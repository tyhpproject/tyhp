using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Members of a Tyhp <c>extension { ... }</c> body: functions and/or extension operator overloads.
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
