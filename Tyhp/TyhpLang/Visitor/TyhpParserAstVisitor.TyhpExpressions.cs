namespace Tyhp.TyhpLang.Visitor
{
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;
    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {


        /// <summary>
        /// Visits a tyhpWithList: { arrayPairList }
        /// Used as the right-hand side of the 'with' binary operator with curly brace syntax.
        /// </summary>
        public override PhpArrayPairListAst VisitTyhpWithList([NotNull] TyhpParser.TyhpWithListContext context)
            => this.VisitArrayPairList(context.ArrayPairList);


    }
}