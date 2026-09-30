using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;

namespace Tyhp.Tests.Domain.Services;

internal static class TyhpdefGenFixtures
{
    public static string JsonSchemaPath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "TyhpdefGen", "json.schema.json");

    public static string ManualExcerptPath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "TyhpdefGen", "json_encode.excerpt.html");

    public static string PixframeClassExcerptPath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "TyhpdefGen", "class.pixframe.excerpt.html");

    public static string JsonStubsPath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "TyhpdefGen", "stubs");

    public static string DisagreeStubsPath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "TyhpdefGen", "stubs-disagree");

    public static string NativeDir
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "TyhpdefGen", "native");

    public static string PackageMiniDir
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "TyhpdefGen", "package-mini");

    public static string Snapshot82MiniPath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "TyhpdefGen", "snapshots", "8.2", "mini.json");

    public static string Snapshot84MiniPath
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "TyhpdefGen", "snapshots", "8.4", "mini.json");
}

[Trait("Category", "Tyhpdef")]
public class PhpManualLocaleTests
{
    [Theory]
    [InlineData("en-US", "en", false)]
    [InlineData("en", "en", false)]
    [InlineData("de-DE", "de", false)]
    [InlineData("pt-BR", "pt_BR", false)]
    [InlineData("ja-JP", "ja", false)]
    [InlineData("zh-CN", "zh", false)]
    [InlineData("xx-YY", "en", true)]
    public void Normalize_MapsProjectLocaleToPhpNet(string input, string expected, bool fallback)
    {
        var actual = PhpManualLocale.Normalize(input, out var fellBack);
        actual.Should().Be(expected);
        fellBack.Should().Be(fallback);
    }
}

[Trait("Category", "Tyhpdef")]
public class PhpManualDocExtractorTests
{
    [Fact]
    public void Attach_VendoredExcerpt_AssemblesJsonEncodeComment()
    {
        var html = File.ReadAllText(TyhpdefGenFixtures.ManualExcerptPath);
        var extractor = new PhpManualDocExtractor();
        extractor.LoadHtml(html, "en");

        var file = new TyhpdefFile
        {
            GlobalFunctions =
            [
                new TyhpdefFunction { Name = "json_encode", ReturnType = "string|false" },
            ],
            GlobalTypes =
            [
                new TyhpdefClassDeclaration { Kind = "class", Name = "JsonException" },
            ],
        };

        extractor.Attach(
            file,
            "en-US",
            "8.3.11",
            "json",
            "8.3.11",
            new DiagnosticBag(),
            CancellationToken.None);

        file.GlobalFunctions[0].DocComment.Should().NotBeNullOrWhiteSpace();
        file.GlobalFunctions[0].DocComment.Should().Contain("Returns the JSON representation of a value");
        file.GlobalFunctions[0].DocComment.Should().Contain("@link https://www.php.net/manual/en/function.json-encode.php");
        file.GlobalFunctions[0].DocComment.Should().Contain("@generated from PHP v8.3.11, EXT: json v8.3.11");
        file.GlobalFunctions[0].DocComment.Should().Contain("@param $value");
        file.GlobalTypes[0].DocComment.Should().Contain("Exception thrown if JSON_THROW_ON_ERROR is set");
    }

    [Fact]
    public void Attach_ClassPageWithNestedMethodRefentries_DoesNotCopyMethodParamsOntoClass()
    {
        var html = File.ReadAllText(TyhpdefGenFixtures.PixframeClassExcerptPath);
        var extractor = new PhpManualDocExtractor();
        extractor.LoadHtml(html, "en");

        var file = new TyhpdefFile
        {
            GlobalTypes =
            [
                new TyhpdefClassDeclaration
                {
                    Kind = "class",
                    Name = "Pixframe",
                    Methods =
                    [
                        new TyhpdefMethod { Name = "adaptiveBlurImage" },
                        new TyhpdefMethod { Name = "clone" },
                    ],
                },
            ],
        };

        extractor.Attach(
            file,
            "en-US",
            "8.5.8",
            "pixframe",
            "3.8.1",
            new DiagnosticBag(),
            CancellationToken.None);

        var classDoc = file.GlobalTypes[0].DocComment;
        classDoc.Should().NotBeNullOrWhiteSpace();
        classDoc.Should().Contain("The Pixframe class");
        classDoc.Should().Contain("The Pixframe class has the ability to hold and operate on multiple images simultaneously");
        classDoc.Should().Contain("@link https://www.php.net/manual/en/class.pixframe.php");
        classDoc.Should().Contain("@generated from PHP v8.5.8, EXT: pixframe v3.8.1");
        classDoc.Should().NotContain("@param");
        classDoc.Should().NotContain("@return");
        classDoc.Should().NotContain("@deprecated");
        classDoc.Should().NotContain("Adds adaptive blur filter to image");
        classDoc.Should().NotContain("$radius");
        classDoc.Should().NotContain("$sigma");
        classDoc.Should().NotContain("$this");

        // Predefined class constants are documented as <dl> entries directly in the class
        // page (no refentry of their own); their per-constant descriptions and deprecation
        // notices must not bleed into the class-level doc comment.
        classDoc.Should().NotContain("default channel mode used by image methods");
        classDoc.Should().NotContain("COMPRESSION_UNDEFINED");

        var blurDoc = file.GlobalTypes[0].Methods[0].DocComment;
        blurDoc.Should().NotBeNullOrWhiteSpace();
        blurDoc.Should().Contain("Adds adaptive blur filter to image");
        blurDoc.Should().Contain("@param $radius The radius of the Gaussian, in pixels.");
        blurDoc.Should().Contain("@param $sigma The standard deviation of the Gaussian, in pixels.");
        blurDoc.Should().Contain("@return Returns true on success.");
        blurDoc.Should().Contain("@deprecated");
        blurDoc.Should().Contain("@link https://www.php.net/manual/en/pixframe.adaptiveblurimage.php");

        var cloneDoc = file.GlobalTypes[0].Methods[1].DocComment;
        cloneDoc.Should().NotBeNullOrWhiteSpace();
        cloneDoc.Should().Contain("Makes a copy of a Pixframe object");
        cloneDoc.Should().Contain("@deprecated");
        cloneDoc.Should().Contain("clone keyword");
        cloneDoc.Should().NotContain("@param $radius");
    }

    [Fact]
    public void HtmlToMarkdown_FormatsParameterCode()
    {
        var markdown = PhpManualDocExtractor.HtmlFragmentToMarkdown("<p>Uses <code class=\"parameter\">flags</code>.</p>");
        markdown.Should().Contain("`$flags`");
    }
}
