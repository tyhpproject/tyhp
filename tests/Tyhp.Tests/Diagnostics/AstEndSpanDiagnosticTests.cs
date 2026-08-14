using Microsoft.Extensions.Localization;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Checker.Rules;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class AstEndSpanDiagnosticTests : IDisposable
{
    private sealed class FakeLocalizer : IStringLocalizer<TyhpHostedService>
    {
        private readonly Dictionary<string, string> _templates = new(StringComparer.Ordinal)
        {
            ["CLI_DiagnosticLocationArrow"] = "  --> {0}:{1}:{2}",
            ["CLI_DiagnosticGutterEmpty"] = "{0} |",
            ["CLI_DiagnosticGutterLine"] = "{0} | {1}",
            ["CLI_DiagnosticGutterUnderline"] = "{0} | {1}",
            ["ERROR_TYHP1001"] = "unknown error",
            ["error"] = "error",
        };

        public LocalizedString this[string name]
            => this._templates.TryGetValue(name, out var value)
                ? new LocalizedString(name, value)
                : new LocalizedString(name, name);

        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(this[name].Value, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => this._templates.Select(kv => new LocalizedString(kv.Key, kv.Value));

        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    public AstEndSpanDiagnosticTests()
    {
        Message.SetLocalizer(new FakeLocalizer());
    }

    public void Dispose() => Message.ResetLocalizer();

    [Fact]
    public void Parse_NameAst_HasExclusiveEndColumnMatchingIdentifierLength()
    {
        const string name = "IdentifierName";
        var result = ParserTestHelper.ParseTyhpContent($"<?tyhp\n$x = {name};\n");

        result.Success.Should().BeTrue(because: string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        var nameAst = FindName(result.Ast!, name);
        nameAst.Should().NotBeNull();
        nameAst!.EndLine.Should().Be(nameAst.Line);
        nameAst.EndColumn.Should().Be(nameAst.Column + name.Length);
        nameAst.EndIndex.Should().BeGreaterThanOrEqualTo(nameAst.StartIndex);
    }

    [Fact]
    public void SerializeDeserialize_PreservesEndPositions()
    {
        const string name = "RoundTripName";
        var parse = ParserTestHelper.ParseTyhpContent($"<?tyhp\n$x = {name};\n");
        parse.Success.Should().BeTrue();
        var original = FindName(parse.Ast!, name)!;

        var bytes = parse.Ast!.Serialize();
        Base2Ast.TryDeserialize(bytes, out var roundTripped).Should().BeTrue();
        var restored = FindName(roundTripped!, name);

        restored.Should().NotBeNull();
        restored!.Line.Should().Be(original.Line);
        restored.Column.Should().Be(original.Column);
        restored.StartIndex.Should().Be(original.StartIndex);
        restored.EndLine.Should().Be(original.EndLine);
        restored.EndColumn.Should().Be(original.EndColumn);
        restored.EndIndex.Should().Be(original.EndIndex);
    }

    [Fact]
    public void CheckerHelpers_ReportError_ForwardsAstEndColumn()
    {
        const string name = "MissingSymbol";
        var parse = ParserTestHelper.ParseTyhpContent($"<?tyhp\n$x = {name};\n");
        var nameAst = FindName(parse.Ast!, name)!;
        var bag = new DiagnosticBag();
        var state = new CheckerState { CurrentFileName = "test.tyhp" };

        CheckerHelpers.ReportError(bag, state, nameAst, MessageCode.ParserUnknownError);

        var diagnostic = bag.Errors.Should().ContainSingle().Subject;
        diagnostic.Line.Should().Be(nameAst.Line);
        diagnostic.Column.Should().Be(nameAst.Column);
        diagnostic.EndLine.Should().Be(nameAst.EndLine);
        diagnostic.EndColumn.Should().Be(nameAst.EndColumn);
    }

    [Fact]
    public void RichSnippet_NameBearingDiagnostic_UnderlinesFullIdentifier()
    {
        const string name = "IdentifierName";
        var parse = ParserTestHelper.ParseTyhpContent($"<?tyhp\n$x = {name};\n");
        var nameAst = FindName(parse.Ast!, name)!;
        var sourceLine = $"$x = {name};";

        var diagnostic = Diagnostic.Error(
            MessageCode.ParserUnknownError,
            "test.tyhp",
            nameAst.Line,
            nameAst.Column,
            Array.Empty<object>(),
            nameAst.EndLine,
            nameAst.EndColumn);

        var lines = RichDiagnosticRenderer.BuildSnippetLines(
            diagnostic,
            (_, line) => line == nameAst.Line ? sourceLine : null);

        lines.Should().Contain(l => l.Contains(sourceLine, StringComparison.Ordinal));
        lines.Should().Contain(l => l.Contains(new string('^', name.Length), StringComparison.Ordinal));
    }

    private static PhpNameAst? FindName(IBase2Ast root, string value)
    {
        foreach (var node in Walk(root))
        {
            if (node is PhpNameAst name && name.ValueString == value)
            {
                return name;
            }
        }

        return null;
    }

    private static IEnumerable<IBase2Ast> Walk(IBase2Ast node)
    {
        yield return node;
        foreach (var child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var descendant in Walk(child))
            {
                yield return descendant;
            }
        }

        foreach (var attribute in node.AstAttributes)
        {
            foreach (var descendant in Walk(attribute))
            {
                yield return descendant;
            }
        }

        foreach (var addon in node.AstGrammarAddons.Values)
        {
            foreach (var descendant in Walk(addon))
            {
                yield return descendant;
            }
        }
    }
}
