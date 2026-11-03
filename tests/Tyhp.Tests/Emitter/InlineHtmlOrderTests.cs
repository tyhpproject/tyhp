using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Parser;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class InlineHtmlOrderTests
{
    [Fact]
    public void Emit_TemplateEchoes_StayBetweenHtml()
    {
        var php = Compile("""
            <?tyhp
            string $title = 'Hello';
            int $count = 3;
            ?>
            <h1><?= $title ?></h1>
            <p><?= $count ?></p>
            """).Php;

        AppearsInOrder(
            php,
            "$title = 'Hello';",
            "$count = 3;",
            "<h1>",
            "echo $title;",
            "</h1>",
            "<p>",
            "echo $count;",
            "</p>");
    }

    [Fact]
    public void Emit_ReopenAfterHtml_ParsesAndKeepsOrder()
    {
        var compiled = Compile("""
            <?tyhp
            string $title = 'Hello';
            ?>
            <h1>x</h1>
            <?tyhp
            echo $title;
            """);

        compiled.Result.Diagnostics.Errors.Should().BeEmpty();
        AppearsInOrder(compiled.Php, "$title = 'Hello';", "<h1>x</h1>", "echo $title;");
    }

    [Fact]
    public void Emit_InlineHtmlInsideFunctionAndIf_KeepsSourceOrder()
    {
        var php = Compile("""
            <?tyhp
            function render(string $title, bool $show): void {
                if ($show) {
                    ?>
                    <h1><?= $title ?></h1>
                    <?tyhp
                }
            }
            """).Php;

        AppearsInOrder(php, "if ($show) {", "<h1>", "echo $title;", "</h1>", "}");
    }

    [Fact]
    public void PhpOpenTagInTyhpFile_ReportsTyhp2005_AndDoesNotEmitTheBlock()
    {
        var compiled = Compile("""
            <?tyhp
            string $title = 'Hello';
            ?>
            <h1>x</h1>
            <?php
            echo $title->missing;
            ?>
            """);

        var error = compiled.Result.Diagnostics.Errors
            .Should().ContainSingle(d => d.Code == MessageCode.PhpOpenTagNotAllowedInTyhpFile)
            .Which;
        error.Line.Should().BeGreaterThan(0);
        compiled.Result.Diagnostics.Errors
            .Should().NotContain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
        compiled.Php.Should().Contain("<h1>x</h1>");
        compiled.Php.Should().NotContain("missing");
    }

    [Fact]
    public void PhpOpenTagInsideFunction_ReportsTyhp2005()
    {
        var compiled = Compile("""
            <?tyhp
            function render(): void {
                ?>
                <p>ok</p>
                <?php
                echo 1;
                ?>
                <b>y</b>
                <?tyhp
            }
            """);

        compiled.Result.Diagnostics.Errors
            .Should().Contain(d => d.Code == MessageCode.PhpOpenTagNotAllowedInTyhpFile);
        compiled.Php.Should().Contain("<p>ok</p>");
        compiled.Php.Should().Contain("<b>y</b>");
        compiled.Php.Should().NotContain("echo 1;");
    }

    [Fact]
    public void EchoTag_StillTypeChecks()
    {
        var compiled = Compile("""
            <?tyhp
            int $count = 3;
            ?>
            <p><?= $count->foo ?></p>
            """);

        compiled.Result.Diagnostics.Errors
            .Should().Contain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void PhpFile_StillAcceptsPhpBlocksAndInlineHtml()
    {
        var compiled = Compile(
            """
            <?php
            $title = 'Hello';
            ?>
            <h1><?= $title ?></h1>
            <?php
            echo $title;
            ?>
            """,
            fileName: "page.php");

        compiled.Result.Diagnostics.Errors
            .Should().NotContain(d => d.Code == MessageCode.PhpOpenTagNotAllowedInTyhpFile);
        compiled.Result.Diagnostics.Errors
            .Should().NotContain(d => d.Code == MessageCode.ParserUnexpectedError);
        compiled.Php.Should().Contain("$title = 'Hello';");
        compiled.Php.Should().Contain("echo $title;");
        compiled.Php.Should().Contain("<h1>");
    }

    [Theory]
    [InlineData("<?tyhp ?>")]
    [InlineData("<?tyhp?>")]
    [InlineData("<?tyhp\n?>")]
    public void EmptyFirstSection_ThenHtmlEchoAndReopen_KeepsSourceOrder(string opening)
    {
        foreach (var tagless in new[] { false, true })
        {
            var compiled = Compile(
                opening + """

                <h1>Hello</h1>
                <?= 1 + 2 ?>
                <?tyhp
                string $name = 'x';
                ?>
                <p><?= $name ?></p>
                """,
                tagless: tagless);

            compiled.Result.Diagnostics.Errors.Should().BeEmpty($"tagless: {tagless}");
            AppearsInOrder(
                compiled.Php,
                "<h1>Hello</h1>",
                "echo 1 + 2;",
                "$name = 'x';",
                "<p>",
                "echo $name;",
                "</p>");
        }
    }

    [Fact]
    public void Tagless_FileStartingWithOpenTag_IsTaggedAndKeepsSourceOrder()
    {
        var compiled = Compile(
            """
            <?tyhp
            string $title = 'Hello';
            int $count = 3;
            ?>
            <h1><?= $title ?></h1>
            <?tyhp
            echo $count;
            ?>
            <p><?= $count ?></p>
            """,
            tagless: true);

        compiled.Result.Diagnostics.Errors.Should().BeEmpty();
        AppearsInOrder(
            compiled.Php,
            "$title = 'Hello';",
            "$count = 3;",
            "<h1>",
            "echo $title;",
            "</h1>",
            "echo $count;",
            "<p>",
            "echo $count;",
            "</p>");
    }

    [Fact]
    public void Tagless_FileStartingWithOpenTag_InlineHtmlInsideFunctionAndIf_KeepsSourceOrder()
    {
        var compiled = Compile(
            """
            <?tyhp
            function render(string $title, bool $show): void {
                if ($show) {
                    ?>
                    <h1><?= $title ?></h1>
                    <?tyhp
                }
            }
            """,
            tagless: true);

        compiled.Result.Diagnostics.Errors.Should().BeEmpty();
        AppearsInOrder(compiled.Php, "if ($show) {", "<h1>", "echo $title;", "</h1>", "}");
    }

    [Theory]
    [InlineData("<?tyhp ?>")]
    [InlineData("<?tyhp?>")]
    [InlineData("<?tyhp\nint $count = 3;\n?>")]
    public void Tagless_FileStartingWithOpenTag_EchoStillTypeChecks(string opening)
    {
        var compiled = Compile(
            opening + """

            <?tyhp
            int $count = 3;
            ?>
            <p><?= $count->foo ?></p>
            """,
            tagless: true);

        compiled.Result.Diagnostics.Errors
            .Should().Contain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Theory]
    [InlineData("<?tyhp ?>")]
    [InlineData("<?tyhp\nstring $title = 'Hello';\n?>")]
    public void Tagless_FileStartingWithOpenTag_PhpOpenTagReportsTyhp2005(string opening)
    {
        var compiled = Compile(
            opening + """

            <h1>x</h1>
            <?php
            echo 1;
            ?>
            """,
            tagless: true);

        compiled.Result.Diagnostics.Errors
            .Should().ContainSingle(d => d.Code == MessageCode.PhpOpenTagNotAllowedInTyhpFile);
        compiled.Php.Should().NotContain("echo 1;");
    }

    [Fact]
    public void Tagless_FileStartingWithOpenTag_PhpOpenTagInsideFunctionReportsTyhp2005()
    {
        var compiled = Compile(
            """
            <?tyhp
            function render(): void {
                ?>
                <p>ok</p>
                <?php
                echo 1;
                ?>
                <?tyhp
            }
            """,
            tagless: true);

        compiled.Result.Diagnostics.Errors
            .Should().Contain(d => d.Code == MessageCode.PhpOpenTagNotAllowedInTyhpFile);
        compiled.Php.Should().NotContain("echo 1;");
    }

    [Fact]
    public void Tagless_FileStartingWithOpenTag_NeverClosed_IsCode()
    {
        var compiled = Compile("<?tyhp\nint $a = 1;\necho $a;\n", tagless: true);

        compiled.Result.Diagnostics.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("$a = 1;");
    }

    [Fact]
    public void Tagless_FileWithoutOpenTag_IsCode()
    {
        var compiled = Compile("int $a = 1;\necho $a;\n", tagless: true);

        compiled.Result.Diagnostics.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("$a = 1;");
    }

    [Theory]
    [InlineData("<h1>Hi</h1>\n")]
    [InlineData("<p><?= 1 ?></p>\n")]
    [InlineData("<h1>Hi</h1>\n<?tyhp\nint $a = 1;\n")]
    [InlineData("<?= 1 ?>\n<?tyhp\nint $a = 1;\n")]
    public void Tagless_FileNotStartingWithOpenTag_IsNotATemplate(string source)
    {
        var result = IsolatedCompilation.ParseSnippet(
            source,
            fileName: "page.tyhp",
            configure: o => o.Tagless = true);

        result.Diagnostics.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Tagless_CloseTagAfterCode_WithoutLeadingOpenTag_IsRejected()
    {
        var result = IsolatedCompilation.ParseSnippet(
            "int $a = 1;\n?>\n<h1>x</h1>\n",
            fileName: "page.tyhp",
            configure: o => o.Tagless = true);

        result.Diagnostics.Errors.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(true, "<?tyhp", false)]
    [InlineData(true, "<?tyhp ?>", false)]
    [InlineData(true, "<?tyhp?>", false)]
    [InlineData(true, "<?tyhp\nint $a = 1;", false)]
    [InlineData(true, "int $a = 1;", true)]
    [InlineData(true, "\n<?tyhp\nint $a = 1;", true)]
    [InlineData(true, "<div>", true)]
    [InlineData(true, "<?= 1 ?>", true)]
    [InlineData(true, "<?ty", true)]
    [InlineData(true, "", true)]
    [InlineData(false, "<?tyhp\n", false)]
    [InlineData(false, "int $a = 1;", false)]
    [InlineData(false, "<div>", false)]
    public void TaglessEntryPoint_ChoosesByFirstSixCharacters(bool projectTagless, string content, bool expected)
    {
        TaglessEntryPoint.UsesTaglessEntryPoint(projectTagless, content).Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tagless_CodeFileAndTemplateInOneProject_ParseIndependently(bool codeFirst)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var code = ("code.tyhp", "namespace App;\n\nfunction add(int $a, int $b): int {\n    return $a + $b;\n}\n");
        var template = ("page.tyhp", "<?tyhp ?>\n<h1>Hello</h1>\n<?= 1 + 2 ?>\n");
        foreach (var (name, source) in codeFirst ? new[] { code, template } : new[] { template, code })
        {
            files[name] = source;
        }

        var options = IsolatedCompilation.CreateOptions(
            Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N")),
            configure: o => o.Tagless = true);
        using var service = new Tyhp.Domain.Services.CompilationService();
        var result = service.ParseFiles(files, options);

        result.Diagnostics.Errors.Should().BeEmpty();
        result.ParsedFiles.Should().HaveCount(2);
    }

    [Fact]
    public void NotTagless_FileWithoutOpenTag_KeepsTaggedEntryPoint()
    {
        // Without `source.tagless`, text before `<?tyhp` is inline HTML.
        var compiled = Compile("<h1>Hi</h1>\n<?tyhp\nint $a = 1;\necho $a;\n");

        compiled.Result.Diagnostics.Errors.Should().BeEmpty();
        AppearsInOrder(compiled.Php, "<h1>Hi</h1>", "$a = 1;", "echo $a;");
    }

    private static Compiled Compile(string content, string fileName = "emit.tyhp", bool tagless = false)
    {
        var result = IsolatedCompilation.ParseSnippet(
            content,
            fileName: fileName,
            configure: tagless ? o => o.Tagless = true : null);
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}@{e.Line}:{e.Column} {e.Message}")));
        var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
        var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
        var php = string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        return new Compiled(result, php);
    }

    private static void AppearsInOrder(string php, params string[] parts)
    {
        var index = -1;
        foreach (var part in parts)
        {
            var next = php.IndexOf(part, index + 1, StringComparison.Ordinal);
            next.Should().BeGreaterThan(index, $"expected `{part}` after position {index} in:{System.Environment.NewLine}{php}");
            index = next;
        }
    }

    private sealed record Compiled(CompilationResult Result, string Php);
}
