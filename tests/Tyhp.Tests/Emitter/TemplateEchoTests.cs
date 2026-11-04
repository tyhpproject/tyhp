using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Parser;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class TemplateEchoTests
{
    [Fact]
    public void EchoTag_ErrorLine_MapsToTyhpSource()
    {
        var path = Fixture("member_missing.tyhp");
        var source = File.ReadAllText(path);
        var marker = "<?= $count->foo ?>";
        var line = source.Replace("\r\n", "\n").Split('\n')
            .Select((text, index) => (text, line: index + 1))
            .Single(pair => pair.text.Contains(marker, StringComparison.Ordinal))
            .line;

        var result = IsolatedCompilation.ParseExistingFiles([path]);
        var error = result.Diagnostics.Errors
            .Should().ContainSingle(d => d.Code == MessageCode.CheckerSymbolNameNotFound)
            .Which;
        error.Line.Should().Be(line);
        error.FileName.Should().EndWith("member_missing.tyhp");
    }

    [Fact]
    public void StructCard_RendersTemplate()
    {
        var php = Emit(Fixture("struct_card.tyhp"));
        var joined = Join(php);
        joined.Should().Contain("echo $user['name'];");
        joined.Should().Contain("<h1>");
        AppearsInOrder(joined, "<h1>", "echo $user['name'];", "</h1>");

        // The template is `    <h1>` + echo + `</h1>\n`; the pretty-printer adds nothing.
        AssertRendered(php, "    <h1>Ada</h1>\n");
    }

    [Fact]
    public void ClassCard_RendersTemplate()
    {
        var php = Emit(Fixture("class_card.tyhp"));
        var joined = Join(php);
        joined.Should().Contain("echo $user->name;");
        joined.Should().NotContain("echo $user['name'];");

        AssertRendered(php, "    <h1>Ada</h1>\n");
    }

    [Fact]
    public void NestedHtml_RendersExactlyTheTemplateText()
    {
        var php = Emit(Fixture("nested_html.tyhp"));

        AssertRendered(
            php,
            "    <h1>Ada</h1>\n    "
            + "        <p>shown</p>\n        "
            + "        <li>a</li>\n        "
            + "        <li>b</li>\n        "
            + "        <footer>Ada</footer>\n        "
            + "    <p>AdaAda</p>\n    ");
    }

    [Fact]
    public void MultiLineHtml_KeepsLinesAndLeadingBlankLine()
    {
        // The first newline after `?>` is the close tag's; the second is output. Lines of the
        // HTML keep their own indentation instead of the printer's.
        var source = "<?tyhp\n"
            + "function page(string $title): void {\n"
            + "    ?>\n"
            + "\n"
            + "<div>\n"
            + "  <h1><?= $title ?></h1>\n"
            + "    <p>text</p>\n"
            + "</div>\n"
            + "<?tyhp\n"
            + "}\n"
            + "\n"
            + "page('Ada');\n";

        var compiled = Compile(source);

        AssertRendered(
            compiled.Files,
            "\n<div>\n  <h1>Ada</h1>\n    <p>text</p>\n</div>\n");
    }

    [Fact]
    public void InlineHtml_IsEmittedWithoutPrinterIndent()
    {
        var php = Emit(Fixture("nested_html.tyhp"));
        var joined = Join(php);

        joined.Should().Contain("?></h1>\n    <?php");
        joined.Should().NotContain("?></h1>\n        <?php");
        joined.Should().Contain("?></p>\n    <?php");
    }

    [Fact]
    public void EchoKeywords_IsWithAndNameof_Render()
    {
        var php = Emit(Fixture("echo_keywords.tyhp"));
        var joined = Join(php);
        joined.Should().Contain("\\Tyhp\\Type::is($x, \\Tyhp\\Type::string())");
        joined.Should().Contain("echo 'User';");
        joined.Should().Contain("['name']");
        AppearsInOrder(
            joined,
            "echo (\\Tyhp\\Type::is($x, \\Tyhp\\Type::string()))",
            "echo ",
            "echo 'User';");

        AssertRendered(php, rendered =>
        {
            rendered.Should().Contain("<span>Ada</span>");
            rendered.Should().Contain("<b>Ada</b>");
            rendered.Should().Contain("<i>User</i>");
        });
    }

    [Fact]
    public void AltSyntax_RendersEachItem()
    {
        var php = Emit(Fixture("alt_syntax.tyhp"));
        var joined = Join(php);
        AppearsInOrder(joined, "if ($show)", "<p>shown</p>", "foreach", "echo $item;", "</li>");

        AssertRendered(php, rendered =>
        {
            rendered.Should().Contain("<p>shown</p>");
            rendered.Should().Contain("<li>a</li>");
            rendered.Should().Contain("<li>b</li>");
        });
    }

    [Fact]
    public void NestedHtml_KeepsSourceOrderAndRenders()
    {
        var php = Emit(Fixture("nested_html.tyhp"));
        var joined = Join(php);
        AppearsInOrder(
            joined,
            "function render(",
            "<h1>",
            "echo $title;",
            "if ($show)",
            "<p>shown</p>",
            "foreach",
            "echo $item;",
            "function () use ($title)",
            "<footer>",
            "echo $title;",
            "echo $title, $title;");

        AssertRendered(php, rendered =>
        {
            rendered.Should().Contain("<h1>Ada</h1>");
            rendered.Should().Contain("<p>shown</p>");
            rendered.Should().Contain("<li>a</li>");
            rendered.Should().Contain("<li>b</li>");
            rendered.Should().Contain("<footer>Ada</footer>");
            rendered.Should().Contain("<p>AdaAda</p>");
        });
    }

    [Fact]
    public void PhpFile_EchoPassesThrough()
    {
        var php = Emit(Fixture("passthrough.php"));
        var joined = Join(php);
        joined.Should().Contain("echo $title;");
        joined.Should().Contain("<h1>");
        joined.Should().Contain("</h1>");
        joined.Should().NotContain("\\Tyhp\\Type::is");
        joined.Should().NotContain("\\is_string");
    }

    [Fact]
    public void TypeofInsideEcho_IsTheTyhpOperator()
    {
        var compiled = Compile("""
            <?tyhp
            class User {}
            function label(): void { ?>
            <?= typeof(User) ?>
            <?tyhp }
            """);

        compiled.Result.Diagnostics.Errors
            .Should().NotContain(d => d.Code == MessageCode.ParserUnexpectedError);
        compiled.Result.Diagnostics.Errors
            .Should().NotContain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
        Join(compiled.Files).Should().Contain("\\Tyhp\\Type::");
    }

    private static string Fixture(string name)
        => Path.Combine(TestFileManager.GetConformanceDirectory(), "story22.1", "templates", name);

    private static Compiled Compile(string content, string fileName = "emit.tyhp")
    {
        var result = IsolatedCompilation.ParseSnippet(content, fileName: fileName);
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}@{e.Line}:{e.Column} {e.Message}")));
        var files = new TyhpEmitter(CreateEmitContext(result)).Emit(result.ParsedFiles!);
        return new Compiled(result, files);
    }

    private static IReadOnlyList<PHPOutputFile> Emit(string path)
    {
        var result = IsolatedCompilation.ParseExistingFiles([path]);
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}@{e.Line}:{e.Column} {e.Message}")));
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
        return new TyhpEmitter(CreateEmitContext(result)).Emit(result.ParsedFiles!);
    }

    private static EmitContext CreateEmitContext(CompilationResult result)
        => EmitContext.Create(
            result.GlobalScope,
            result.Diagnostics,
            project: null,
            result.RequiresRuntimeGenericTracking,
            result.RequiresWeakReferenceCapture,
            result.RequiresDisposableTryFinally,
            result.AsyncForeachKinds,
            result.RequiresGenericVariant,
            result.GenericCallTargets,
            result.InferredClosureSignatures,
            result.ExpressionTypes,
            result.NativeTypeTests);

    private static void AssertRendered(IReadOnlyList<PHPOutputFile> files, string expected)
        => AssertRendered(files, rendered => rendered.Should().Be(expected));

    private static void AssertRendered(IReadOnlyList<PHPOutputFile> files, Action<string> assert)
    {
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        assert(EmittedPhpRunner.Run(files, ""));
    }

    private static string Join(IReadOnlyList<PHPOutputFile> files)
        => string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));

    private static void AppearsInOrder(string php, params string[] parts)
    {
        var index = -1;
        foreach (var part in parts)
        {
            var next = php.IndexOf(part, index + 1, StringComparison.Ordinal);
            next.Should().BeGreaterThan(index, $"expected `{part}` after position {index} in:{Environment.NewLine}{php}");
            index = next;
        }
    }

    private sealed record Compiled(CompilationResult Result, IReadOnlyList<PHPOutputFile> Files);
}
