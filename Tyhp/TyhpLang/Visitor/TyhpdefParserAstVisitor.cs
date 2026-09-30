namespace Tyhp.TyhpLang.Visitor
{
    using Tyhp.Domain.Diagnostics;

    /// <summary>
    /// Walks a <see cref="Parser.TyhpdefParser"/> tree.
    /// PHP closure visits live on <see cref="TyhpdefIncludedPhpVisits"/>, which extends
    /// <see cref="Parser.TyhpdefParserBaseVisitor{T}"/>, so a Tyhp override can call
    /// <c>base.Visit…</c> and still run the PHP method.
    /// </summary>
    public partial class TyhpdefParserAstVisitor : TyhpdefIncludedPhpVisits
    {
        public TyhpdefParserAstVisitor(Antlr4.Runtime.CommonTokenStream? tokens, string filename, string fileHash, DiagnosticBag diagnostics)
            : base(tokens, filename, fileHash, diagnostics)
        {
        }
    }
}
