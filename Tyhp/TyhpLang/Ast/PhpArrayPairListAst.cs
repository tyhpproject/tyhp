using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpArrayPairListAst : NodeListAst<PhpArrayPairAst, PhpArrayPairListAst>, IExpression, IForeachVariable, IDereferenceableBase, IScalar
    {
    }
} 