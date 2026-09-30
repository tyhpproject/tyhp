namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    
    public partial class PhpParserAstVisitor : TyhpParserBaseVisitor<Ast.Interfaces.IBase2Ast?>, ITyhpParserVisitor<Ast.Interfaces.IBase2Ast?>
    {























        private static PhpIfAst GetIfChainTail(PhpIfAst head)
        {
            var current = head;
            while (current.ElseStatement is PhpIfAst elseif)
            {
                current = elseif;
            }

            return current;
        }












    }
}