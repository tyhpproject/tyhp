using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.TyhpLang.Ast
{
    public class PhpArgumentListAst : NodeListAst<PhpArgumentAst, PhpArgumentListAst>, IExpression, IDereferenceableSuffix
    {
        // TODO: sometimes this can hold the function call grammar addon under "functionCall", and we need to transfer that to the function call AST
    }
} 