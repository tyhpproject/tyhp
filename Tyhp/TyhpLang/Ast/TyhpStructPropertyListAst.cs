namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Represents a list of struct property declarations.
    ///
    /// Grammar:
    ///   tyhpStructPropertyList
    ///     : Items+=tyhpStructProperty*
    ///     ;
    /// </summary>
    public class TyhpStructPropertyListAst : NodeListAst<TyhpStructPropertyAst, TyhpStructPropertyListAst>
    {
    }
}
