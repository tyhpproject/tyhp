using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class TyhpdefOutputLayoutTests
{
    private static TyhpdefFile MapJsonFixture(out TyhpdefGenerationOptions baseOptions)
    {
        var json = File.ReadAllText(TyhpdefGenFixtures.JsonSchemaPath);
        var runtime = new PhpRuntimeInfo { Path = "/tmp/php", Version = "8.3.11", IsManaged = true };
        baseOptions = new TyhpdefGenerationOptions
        {
            Mode = TyhpdefGenerationMode.PhpExtension,
            ExtensionName = "json",
            IncludeDocComments = false,
            IncludeDeprecated = true,
        };
        return PhpReflectionMapper.Map(json, baseOptions, runtime);
    }

    [Fact]
    public void Split_File_WritesSingleExtensionFile()
    {
        var file = MapJsonFixture(out var baseOptions);
        var options = baseOptions with { OutputDirectory = "/tmp/out", Split = "file" };

        var outputs = TyhpdefOutputLayout.Split(file, options, "json");

        outputs.Should().HaveCount(1);
        outputs[0].Path.Should().Be(Path.Combine("/tmp/out", "ExtJson.tyhpdef"));
    }

    [Fact]
    public void Split_Namespace_AllGlobalsGoIntoTheExtensionFile()
    {
        var file = MapJsonFixture(out var baseOptions);
        var options = baseOptions with { OutputDirectory = "/tmp/out", Split = "namespace" };

        var outputs = TyhpdefOutputLayout.Split(file, options, "json");

        outputs.Should().HaveCount(1);
        outputs[0].Path.Should().Be(Path.Combine("/tmp/out", "ExtJson.tyhpdef"));
        var text = TyhpdefOutputWriter.Write(outputs[0].File, includeDocComments: false);
        text.Should().Contain("function json_encode");
        text.Should().Contain("class JsonException");
        AssertParses(text);
    }

    [Fact]
    public void Split_Namespace_SplitsNamespacedTypesIntoOwnFiles()
    {
        var runtime = new PhpRuntimeInfo { Path = "/tmp/php", Version = "8.3.11", IsManaged = true };
        var file = new TyhpdefFile
        {
            Namespaces =
            [
                new TyhpdefNamespace
                {
                    Name = "Foo\\Bar",
                    Classes = [new TyhpdefClassDeclaration { Kind = "class", Name = "Widget" }],
                    Functions = [new TyhpdefFunction { Name = "helper", ReturnType = "void" }],
                },
            ],
        };
        var options = new TyhpdefGenerationOptions { OutputDirectory = "/tmp/out", Split = "namespace" };

        var outputs = TyhpdefOutputLayout.Split(file, options, "widget");

        outputs.Should().ContainSingle(o => o.Path == Path.Combine("/tmp/out", "Foo.Bar.tyhpdef"));
        var text = TyhpdefOutputWriter.Write(outputs[0].File, includeDocComments: false);
        text.Should().Contain("namespace Foo\\Bar {");
        text.Should().Contain("class Widget");
        text.Should().Contain("function helper");
        AssertParses(text);
        _ = runtime;
    }

    [Fact]
    public void Split_Type_SplitsGlobalsAndEachClassSeparately()
    {
        var file = MapJsonFixture(out var baseOptions);
        var options = baseOptions with { OutputDirectory = "/tmp/out", Split = "type" };

        var outputs = TyhpdefOutputLayout.Split(file, options, "json");

        outputs.Should().Contain(o => o.Path == Path.Combine("/tmp/out", "_global.tyhpdef"));
        outputs.Should().Contain(o => o.Path == Path.Combine("/tmp/out", "JsonException.tyhpdef"));
        outputs.Should().Contain(o => o.Path == Path.Combine("/tmp/out", "JsonSerializable.tyhpdef"));

        foreach (var (_, part) in outputs)
        {
            var text = TyhpdefOutputWriter.Write(part, includeDocComments: false);
            AssertParses(text);
        }
    }

    [Fact]
    public void Split_SourceDefaultFile_WritesSourceTyhpdef()
    {
        var file = new TyhpdefFile
        {
            GlobalTypes = [new TyhpdefClassDeclaration { Kind = "class", Name = "Widget" }],
        };
        var options = new TyhpdefGenerationOptions { OutputDirectory = "/tmp/out", Split = "file" };

        var outputs = TyhpdefOutputLayout.SplitWithPrimaryFile(file, options, TyhpdefOutputLayout.SourceFileName());

        outputs.Should().HaveCount(1);
        outputs[0].Path.Should().Be(Path.Combine("/tmp/out", "source.tyhpdef"));
    }

    [Fact]
    public void PackageFileName_ReplacesSlashWithDot()
    {
        TyhpdefOutputLayout.PackageFileName("acme/http").Should().Be("acme.http.tyhpdef");
        TyhpdefOutputLayout.PackageFileName("acme/widget").Should().Be("acme.widget.tyhpdef");
    }

    [Fact]
    public void StubOverlayFileName_MatchesOutputFileWhenSet()
    {
        var options = new TyhpdefGenerationOptions
        {
            OutputDirectory = "/tmp/out",
            OutputFileName = "/tmp/out/Ext.Xsl.tyhpdef",
        };
        TyhpdefOutputLayout.StubOverlayFileName(options, "xsl").Should().Be("Ext.Xsl.tyhpdef");
        TyhpdefOutputLayout.StubOverlayFileName(
            new TyhpdefGenerationOptions(),
            "xsl").Should().Be("ExtXsl.tyhpdef");
    }

    [Fact]
    public void Split_OutputFileOverridesSplitMode()
    {
        var file = MapJsonFixture(out var baseOptions);
        var options = baseOptions with
        {
            OutputDirectory = "/tmp/out",
            OutputFileName = "/tmp/out/custom.tyhpdef",
            Split = "type",
        };

        var outputs = TyhpdefOutputLayout.Split(file, options, "json");

        outputs.Should().HaveCount(1);
        outputs[0].Path.Should().Be("/tmp/out/custom.tyhpdef");
    }

    private static void AssertParses(string tyhpdef)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(tyhpdef);
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)) + "\n" + tyhpdef);
    }
}
