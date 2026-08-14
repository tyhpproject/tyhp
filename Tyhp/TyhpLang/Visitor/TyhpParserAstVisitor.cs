namespace Tyhp.TyhpLang.Visitor
{
    using Tyhp.Domain.Diagnostics;

    public partial class TyhpParserAstVisitor : PhpParserAstVisitor
    {
        public TyhpParserAstVisitor(Antlr4.Runtime.CommonTokenStream? tokens, string filename, string fileHash, DiagnosticBag diagnostics): base(tokens, filename, fileHash, diagnostics)
        {
            // ctor
        }
    }
}