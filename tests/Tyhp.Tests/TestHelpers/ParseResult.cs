using Tyhp.Domain.Diagnostics;
using Tyhp.TyhpLang.Ast;

namespace Tyhp.Tests.TestHelpers;

public sealed class ParseResult
{
    public SrcFileAst? Ast { get; set; }

    public DiagnosticBag Diagnostics { get; } = new();

    public bool Success => !this.Diagnostics.HasErrors;
}
