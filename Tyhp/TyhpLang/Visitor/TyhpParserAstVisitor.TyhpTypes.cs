namespace Tyhp.TyhpLang.Visitor
{
    using System.Linq;
    using Antlr4.Runtime.Misc;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Parser;

    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {







        private static string ScalarTypeSpelling(TyhpParser.TyhpScalarTypeContext context)
        {
            var text = context.GetText();
            return string.IsNullOrEmpty(text)
                ? context.Start?.Text ?? "<unknown>"
                : text;
        }
    }
}
