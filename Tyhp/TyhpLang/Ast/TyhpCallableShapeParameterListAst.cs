namespace Tyhp.TyhpLang.Ast
{
    /// <summary>
    /// Parameter list of a <c>callable(…): R</c> shape (not unknown-arity <c>...</c>).
    /// </summary>
    public class TyhpCallableShapeParameterListAst
        : NodeListAst<TyhpCallableShapeParameterAst, TyhpCallableShapeParameterListAst>
    {
    }
}
