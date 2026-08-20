using Microsoft.VisualStudio.LanguageServer.Protocol;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.LanguageServer.Analysis;
using Tyhp.TyhpLang.Ast;
using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;
using TyhpDiagnostic = Tyhp.Domain.Diagnostics.Diagnostic;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class PositionUtilitiesTests
{
    [Fact]
    public void ToLspPosition_ConvertsOneBasedLine()
    {
        Position position = PositionUtilities.ToLspPosition(4, 10);
        position.Line.Should().Be(3);
        position.Character.Should().Be(10);
    }

    [Fact]
    public void ToLspPosition_ClampsInvalidCoordinates()
    {
        Position position = PositionUtilities.ToLspPosition(0, -3);
        position.Line.Should().Be(0);
        position.Character.Should().Be(0);
    }

    [Fact]
    public void FromLspPosition_ConvertsToAntlrCoordinates()
    {
        (int line, int column) = PositionUtilities.FromLspPosition(new Position { Line = 3, Character = 10 });
        line.Should().Be(4);
        column.Should().Be(10);
    }

    [Fact]
    public void GetOffsetAndGetPosition_RoundTripUtf16AndCrlf()
    {
        const string text = "a😀b\r\ncd";
        PositionUtilities.GetOffset(text, new Position { Line = 0, Character = 1 }).Should().Be(1);
        PositionUtilities.GetOffset(text, new Position { Line = 0, Character = 3 }).Should().Be(3);
        PositionUtilities.GetOffset(text, new Position { Line = 1, Character = 0 }).Should().Be(6);

        PositionUtilities.GetPosition(text, 0).Should().BeEquivalentTo(new Position { Line = 0, Character = 0 });
        PositionUtilities.GetPosition(text, 3).Should().BeEquivalentTo(new Position { Line = 0, Character = 3 });
        PositionUtilities.GetPosition(text, 6).Should().BeEquivalentTo(new Position { Line = 1, Character = 0 });
        PositionUtilities.GetPosition(text, text.Length).Line.Should().Be(1);
    }

    [Fact]
    public void GetOffset_ClampsPastEndOfLineAndDocument()
    {
        PositionUtilities.GetOffset("ab", new Position { Line = 0, Character = 50 }).Should().Be(2);
        PositionUtilities.GetOffset("ab\ncd", new Position { Line = 9, Character = 0 }).Should().Be(5);
        PositionUtilities.GetOffset("", new Position { Line = 0, Character = 0 }).Should().Be(0);
    }

    [Fact]
    public void ToLspRange_FromParsedFunction_HasNonZeroSpan()
    {
        using var compilation = new CompilationService();
        var diagnostics = new DiagnosticBag();
        SrcFileAst? ast = compilation.ParseFromContent(
            "<?tyhp\nfunction greet(): void {}\n",
            "span.tyhp",
            diagnostics);
        ast.Should().NotBeNull();
        PhpFunctionDeclAst? function = FindFirst<PhpFunctionDeclAst>(ast!);
        function.Should().NotBeNull();
        ProtocolRange range = PositionUtilities.ToLspRange(function!);
        range.Start.Line.Should().BeGreaterThanOrEqualTo(0);
        (range.End.Line > range.Start.Line
            || range.End.Character >= range.Start.Character).Should().BeTrue();
    }

    [Fact]
    public void ToIdentifierRange_OnFunctionDeclaration_CoversNameNotBody()
    {
        const string source = "<?tyhp\nfunction greet(): void { return; }\n";
        using var compilation = new CompilationService();
        var diagnostics = new DiagnosticBag();
        SrcFileAst? ast = compilation.ParseFromContent(source, "ident-span.tyhp", diagnostics);
        ast.Should().NotBeNull();
        PhpFunctionDeclAst? function = FindFirst<PhpFunctionDeclAst>(ast!);
        function.Should().NotBeNull();

        ProtocolRange nameRange = PositionUtilities.ToIdentifierRange(function!, "greet", source);
        int start = PositionUtilities.GetOffset(source, nameRange.Start);
        int end = PositionUtilities.GetOffset(source, nameRange.End);
        source[start..end].Should().Be("greet");
    }

    [Fact]
    public void ToLspRange_DiagnosticWithoutEnd_IsZeroWidth()
    {
        var diagnostic = TyhpDiagnostic.Error(MessageCode.ParserUnknownError, "a.tyhp", 4, 6, ["x"]);
        ProtocolRange range = PositionUtilities.ToLspRange(diagnostic);
        range.Start.Line.Should().Be(3);
        range.Start.Character.Should().Be(6);
        range.End.Line.Should().Be(3);
        range.End.Character.Should().Be(6);
    }

    [Fact]
    public void ToLspRange_DiagnosticWithEnd_SpansRange()
    {
        var diagnostic = TyhpDiagnostic.Error(
            MessageCode.ParserUnknownError,
            "a.tyhp",
            2,
            0,
            ["x"],
            endLine: 2,
            endColumn: 8);
        ProtocolRange range = PositionUtilities.ToLspRange(diagnostic);
        range.Start.Should().BeEquivalentTo(new Position { Line = 1, Character = 0 });
        range.End.Should().BeEquivalentTo(new Position { Line = 1, Character = 8 });
    }

    private static T? FindFirst<T>(Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast node) where T : class, Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast
    {
        if (node is T match)
        {
            return match;
        }

        foreach (Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast? child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            T? found = FindFirst<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
