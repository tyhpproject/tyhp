using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
[Trait("Category", "EndToEnd")]
public class SourceMapEndToEndTests
{
    private const string TyhpSource = """
        <?tyhp
        namespace App\Models;
        class User {
            public function greet(): void {
                $name = 'hello';
                echo $name;
            }
        }
        """;

    [Fact]
    public void Emit_TyhpToSourceMap_ValidatesAndMapsClassMethodAndAssignment()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent(TyhpSource, fileName: "src/User.tyhp");
        parseResult.Diagnostics.Errors.Should().BeEmpty();
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;

        var project = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["build:generateSourcemap"] = "true",
            })
            .Build());
        var context = EmitContext.Create(new GlobalScope(), new DiagnosticBag(), project);
        var file = new TyhpEmitter(context).Emit([srcFile]).Should().ContainSingle().Subject;

        file.SourceMapCollector.Should().NotBeNull();
        file.SourceRoot.Should().Be("src/");
        var php = file.GeneratedContent.Should().NotBeNullOrEmpty().And.Subject;
        var json = file.SourceMap();
        json.Should().NotBeNullOrEmpty();

        var diagnostics = new DiagnosticBag();
        var result = SourceMapValidator.Validate(
            json,
            php,
            diagnostics,
            coverageThreshold: 0,
            sourceContentProvider: path => path == "src/User.tyhp" ? TyhpSource : null);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.DecodedMappings.Should().NotBeEmpty();
        diagnostics.ToList().Should().NotContain(d =>
            d.Code == MessageCode.EmitterSourceMapGenerationFailed
            || d.Code == MessageCode.EmitterSourceMapWriteFailed
            || d.Code == MessageCode.EmitterSourceMapInvalidMapping);

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        root.GetProperty("version").GetInt32().Should().Be(3);
        root.GetProperty("file").GetString().Should().Be(Path.GetFileName(file.OutputFilePath));
        root.GetProperty("sourceRoot").GetString().Should().Be("src/");
        root.GetProperty("sources").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("User.tyhp");
        root.TryGetProperty("sourcesContent", out _).Should().BeFalse();

        AssertOriginalLineMapped(php, result.DecodedMappings, "class User", originalLine0Based: 2);
        AssertOriginalLineMapped(php, result.DecodedMappings, "function greet", originalLine0Based: 3);
        AssertOriginalLineMapped(php, result.DecodedMappings, "$name", originalLine0Based: 4);
    }

    [Fact]
    public void Build_WritesMapAlongsidePhp_AndValidatorAcceptsOnDiskFiles()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" },
                    "build": { "generateSourcemap": true }
                }
                """)
            .WithConfigValue("build:generateSourcemap", "true")
            .WithTyhpFile("src/User.tyhp", TyhpSource);

        var buildResult = project.RunBuild();
        buildResult.Diagnostics.Errors.Should().BeEmpty();
        buildResult.Diagnostics.ToList().Should().NotContain(d =>
            d.Code == MessageCode.EmitterSourceMapGenerationFailed
            || d.Code == MessageCode.EmitterSourceMapWriteFailed
            || d.Code == MessageCode.EmitterSourceMapInvalidMapping);

        var phpPath = Path.Combine(project.ProjectDirectory, "build", "App", "Models", "User.php");
        var mapPath = phpPath + ".map";
        File.Exists(phpPath).Should().BeTrue();
        File.Exists(mapPath).Should().BeTrue();

        string php = File.ReadAllText(phpPath);
        string json = File.ReadAllText(mapPath);
        php.Should().Contain("//# sourceMappingURL=User.php.map");
        php.Should().NotContain("sourceMappingURL=data:");

        var result = SourceMapValidator.Validate(
            json,
            php,
            new DiagnosticBag(),
            coverageThreshold: 0,
            sourceContentProvider: path =>
                path.Replace('\\', '/').EndsWith("User.tyhp", StringComparison.Ordinal)
                    ? TyhpSource
                    : null);

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
        result.DecodedMappings.Should().NotBeEmpty();

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        root.GetProperty("version").GetInt32().Should().Be(3);
        root.GetProperty("file").GetString().Should().Be("User.php");
        root.GetProperty("sourceRoot").GetString().Should().Be("src/");
        root.GetProperty("sources").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("User.tyhp");

        AssertOriginalLineMapped(php, result.DecodedMappings, "class User", originalLine0Based: 2);
        AssertOriginalLineMapped(php, result.DecodedMappings, "function greet", originalLine0Based: 3);
        AssertOriginalLineMapped(php, result.DecodedMappings, "$name", originalLine0Based: 4);
    }

    [Fact]
    public void Build_WithoutGenerateSourcemap_DoesNotWriteMapOrUrlComment()
    {
        using var project = new TestProjectBuilder();
        project
            .WithDefaultTyhpJson()
            .WithTyhpFile("src/User.tyhp", TyhpSource);

        var buildResult = project.RunBuild();
        buildResult.Diagnostics.Errors.Should().BeEmpty();

        var phpPath = Path.Combine(project.ProjectDirectory, "build", "App", "Models", "User.php");
        File.Exists(phpPath).Should().BeTrue();
        File.Exists(phpPath + ".map").Should().BeFalse();
        File.ReadAllText(phpPath).Should().NotContain("sourceMappingURL=");
    }

    private static void AssertOriginalLineMapped(
        string php,
        IReadOnlyList<SourceMapping> mappings,
        string generatedSnippet,
        int originalLine0Based)
    {
        int generatedLine = IndexOfLineContaining(php, generatedSnippet);
        generatedLine.Should().BeGreaterThanOrEqualTo(
            0,
            $"generated PHP should contain '{generatedSnippet}'");

        mappings.Should().Contain(
            m => m.GeneratedLine == generatedLine && m.OriginalLine == originalLine0Based,
            $"generated line {generatedLine} ('{generatedSnippet}') should map to original line {originalLine0Based}");
    }

    private static int IndexOfLineContaining(string php, string snippet)
    {
        string stripped = SourceMapValidator.StripSourceMappingUrlLines(php);
        string[] lines = stripped.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(snippet, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
