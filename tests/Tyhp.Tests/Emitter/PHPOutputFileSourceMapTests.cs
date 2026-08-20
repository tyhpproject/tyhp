using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class PHPOutputFileSourceMapTests
{
    [Fact]
    public void SourceMap_WithoutCollector_ReturnsEmptyString()
    {
        var file = new PHPOutputFile { OutputFilePath = "build/App.php" };
        file.Generate(EmptyContext());

        file.SourceMap().Should().BeEmpty();
        file.SourceMap(includeSourcesContent: true).Should().BeEmpty();
    }

    [Fact]
    public void SourceFileName_IsSettableAndAssignedByFromAstTree()
    {
        var file = new PHPOutputFile();
        file.SourceFileName.Should().BeNull();
        file.SourceFileName = "src/App.tyhp";
        file.SourceFileName.Should().Be("src/App.tyhp");

        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            namespace App;
            class Foo {}
            """);
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmptyContext();
        var emitted = new TyhpEmitter(context).Emit([srcFile]).Should().ContainSingle().Subject;

        emitted.SourceFileName.Should().Be(srcFile.FileName);
        emitted.SourceFileName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Generate_WithCollector_ProducesTheSamePhpAsTheFastPath()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            namespace App;
            class Foo
            {
                public function bar(): int
                {
                    return 1;
                }
            }
            """);
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmptyContext();
        var file = new TyhpEmitter(context).Emit([srcFile]).Should().ContainSingle().Subject;
        var fastPhp = file.GeneratedContent;
        fastPhp.Should().NotBeNullOrEmpty();

        file.SourceMapCollector = new SourceMapCollector();
        var trackedPhp = file.Generate(context);

        trackedPhp.Should().Be(fastPhp);
        file.GeneratedContent.Should().Be(fastPhp);
    }

    [Fact]
    public void Generate_WithoutCollector_DoesNotAllocateOrTouchACollector()
    {
        var file = ManualEchoFile(provider: Provider(1, 0));
        file.SourceMapCollector.Should().BeNull();

        var php = file.Generate(EmptyContext());

        php.Should().StartWith("<?php\n");
        php.Should().Contain("echo 1;");
        file.SourceMapCollector.Should().BeNull();
        file.SourceMap().Should().BeEmpty();
    }

    [Fact]
    public void SourceMap_AfterTrackingGenerate_ReturnsValidSourceMapV3Json()
    {
        var provider = Provider(1, 0);
        var file = ManualEchoFile(provider);
        file.SourceFileName = "src/App.tyhp";
        file.SourceRoot = "src/";
        file.OutputFilePath = "build/App.php";
        file.SourceMapCollector = new SourceMapCollector();

        var php = file.Generate(EmptyContext());
        var json = file.SourceMap();

        json.Should().NotBeNullOrEmpty();
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        root.GetProperty("version").GetInt32().Should().Be(3);
        root.GetProperty("file").GetString().Should().Be("App.php");
        root.GetProperty("sourceRoot").GetString().Should().Be("src/");
        root.GetProperty("sources").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("App.tyhp");
        root.GetProperty("names").GetArrayLength().Should().Be(0);
        root.TryGetProperty("sourcesContent", out _).Should().BeFalse();
        root.GetProperty("mappings").GetString().Should().NotBeNullOrEmpty();

        file.SourceMapCollector!.CurrentGeneratedLine.Should().Be(php.Count(c => c == '\n'));
    }

    [Fact]
    public void SourceMap_PreambleIsUnmapped_BodyProviderIsMapped()
    {
        var provider = Provider(line: 4, column: 0);
        var file = ManualEchoFile(provider);
        file.SourceFileName = "src/App.tyhp";
        file.SourceMapCollector = new SourceMapCollector();

        var php = file.Generate(EmptyContext());
        php.Should().Contain("echo 1;");

        var mappings = file.SourceMapCollector.GetMappings();
        mappings.Should().NotBeEmpty();
        mappings.Should().OnlyContain(m => m.OriginalLine == 3 && m.OriginalColumn == 0);

        int echoLine = php.Split('\n').ToList().FindIndex(l => l.Contains("echo 1;", StringComparison.Ordinal));
        echoLine.Should().BeGreaterThan(0);
        mappings.Should().Contain(m => m.GeneratedLine == echoLine);
        mappings.Should().NotContain(m => m.GeneratedLine == 0);
    }

    [Fact]
    public void SourceMap_IncludeSourcesContent_EmbedsProviderResultForOriginalPath()
    {
        var file = ManualEchoFile(Provider(1, 0));
        file.SourceFileName = "src/App.tyhp";
        file.SourceRoot = "src/";
        file.SourceMapCollector = new SourceMapCollector();
        file.Generate(EmptyContext());

        string? providerPath = null;
        var json = file.SourceMap(
            includeSourcesContent: true,
            sourceContentProvider: path =>
            {
                providerPath = path;
                return "<?tyhp\necho 1;\n";
            });

        providerPath.Should().Be("src/App.tyhp");
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("sourcesContent").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Equal("<?tyhp\necho 1;\n");
    }

    [Fact]
    public void SourceMap_SourceRootFilesystemPrefix_RelativizesRegisteredPath()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "tyhp-sm-" + Guid.NewGuid().ToString("N"), "src");
        Directory.CreateDirectory(sourceRoot);
        try
        {
            var sourcePath = Path.Combine(sourceRoot, "Models", "User.tyhp");
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);

            var file = ManualEchoFile(Provider(1, 0));
            file.SourceFileName = sourcePath;
            file.SourceRoot = sourceRoot;
            file.OutputFilePath = "build/User.php";
            file.SourceMapCollector = new SourceMapCollector();
            file.Generate(EmptyContext());

            using var document = JsonDocument.Parse(file.SourceMap());
            document.RootElement.GetProperty("sourceRoot").GetString().Should().Be(sourceRoot);
            document.RootElement.GetProperty("sources").EnumerateArray().Select(e => e.GetString())
                .Should().Equal("Models/User.tyhp");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(sourceRoot)!, recursive: true);
        }
    }

    [Fact]
    public void SourceMap_UrlStyleSourceRoot_DoesNotRelativizeProjectRelativePath()
    {
        var file = ManualEchoFile(Provider(1, 0));
        file.SourceFileName = "src/App.tyhp";
        file.SourceRoot = "../src/";
        file.SourceMapCollector = new SourceMapCollector();
        file.Generate(EmptyContext());

        using var document = JsonDocument.Parse(file.SourceMap());
        document.RootElement.GetProperty("sourceRoot").GetString().Should().Be("../src/");
        document.RootElement.GetProperty("sources").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("src/App.tyhp");
    }

    /// <summary>
    /// Regression test: when the last body item's own content ends with embedded trailing
    /// newlines (e.g. <c>"echo 2;\n\n"</c>), <see cref="PHPOutputFile.Generate"/> trims that
    /// trailing whitespace from the written PHP via <c>body.TrimEnd()</c>. Before the fix, the
    /// collector had already advanced through the untrimmed text, so its final
    /// <see cref="SourceMapCollector.CurrentGeneratedLine"/> stayed ahead of the actual line count
    /// of the generated content.
    /// </summary>
    [Fact]
    public void Generate_TrimsTrailingWhitespaceFromLastBodyItem_CollectorPositionStaysInSync()
    {
        var provider1 = Provider(1, 0);
        var provider2 = Provider(2, 0);
        var file = new PHPOutputFile { OutputFilePath = "build/App.php" };
        file.RootEmitItem = EmitItem.Empty(provider1, EmitType.FileHeader);
        EmitItem.Line(provider1, EmitType.RootStatement, "echo 1;", file.RootEmitItem);
        EmitItem.Line(provider2, EmitType.RootStatement, "echo 2;\n\n", file.RootEmitItem);

        file.SourceMapCollector = new SourceMapCollector();
        var php = file.Generate(EmptyContext());

        file.SourceMapCollector.CurrentGeneratedLine.Should().Be(
            php.Count(c => c == '\n'),
            "the collector's final position must match the actual generated content once trailing "
            + "whitespace already tracked during body emission is trimmed from the written PHP");
    }

    /// <summary>
    /// Regression test: <see cref="PHPOutputFile.Generate"/> appends a PSR-12 blank-line separator
    /// between the namespace/import preamble and the body <em>after</em> the body's text has
    /// already been computed. Before the fix, that separator was reported to the collector after
    /// tracking body content, so every body mapping was recorded one generated line too early
    /// whenever the separator was needed (i.e. almost any namespaced file).
    /// </summary>
    [Fact]
    public void Generate_WithNamespacePreambleSeparator_BodyMappingsAlignWithActualLines()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            namespace App;
            class Foo
            {
                public function bar(): int
                {
                    return 1;
                }
            }
            """);
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmptyContext();
        var file = new TyhpEmitter(context).Emit([srcFile]).Should().ContainSingle().Subject;

        file.SourceMapCollector = new SourceMapCollector();
        var php = file.Generate(context);

        var classLineActual = php.Split('\n').ToList().FindIndex(l => l.Contains("class Foo", StringComparison.Ordinal));
        classLineActual.Should().BeGreaterThan(0);

        // "class Foo" is source line 3 (1-based) => OriginalLine 2 (0-based).
        var classMapping = file.SourceMapCollector.GetMappings()
            .Where(m => m.OriginalLine == 2)
            .OrderBy(m => m.GeneratedLine)
            .First();

        classMapping.GeneratedLine.Should().Be(
            classLineActual,
            "the mapping must point at the actual line of `class Foo` in the generated output");
    }

    [Fact]
    public void SourceMapCodes_AreAllocated5020Through5022()
    {
        ((int)MessageCode.EmitterSourceMapGenerationFailed).Should().Be(5020);
        ((int)MessageCode.EmitterSourceMapWriteFailed).Should().Be(5021);
        ((int)MessageCode.EmitterSourceMapInvalidMapping).Should().Be(5022);
    }

    [Fact]
    public void Generate_CalledTwiceWithSameCollector_DoesNotDuplicateMappings()
    {
        var file = ManualEchoFile(Provider(1, 0));
        file.SourceFileName = "src/App.tyhp";
        file.SourceMapCollector = new SourceMapCollector();
        var context = EmptyContext();

        file.Generate(context);
        var firstCount = file.SourceMapCollector.GetMappings().Count;
        firstCount.Should().BeGreaterThan(0);

        file.Generate(context);
        file.SourceMapCollector.GetMappings().Count.Should().Be(firstCount);
    }

    [Fact]
    public void Merge_ReconcilesCollectorSourceFileNameAndSourceRoot()
    {
        var context = EmptyContext();
        var leftProvider = Provider(1, 0);
        var rightProvider = Provider(2, 0);
        var left = ManualEchoFile(leftProvider);
        left.SourceFileName = "src/Left.tyhp";
        left.SourceRoot = "src/";
        left.SourceMapCollector = new SourceMapCollector("src/Left.tyhp");
        left.Generate(context);

        var right = new PHPOutputFile
        {
            OutputFilePath = "build/App.php",
            SourceFileName = "src/Right.tyhp",
            SourceRoot = "other/",
            SourceMapCollector = new SourceMapCollector("src/Right.tyhp"),
            RootEmitItem = EmitItem.Empty(rightProvider, EmitType.FileHeader),
        };
        right.RootEmitItem.Children.Add(
            EmitItem.Line(rightProvider, EmitType.RootStatement, "echo 2;", right.RootEmitItem));
        right.Generate(context);

        var leftMappingsBeforeMerge = left.SourceMapCollector.GetMappings().Count;
        leftMappingsBeforeMerge.Should().BeGreaterThan(0);

        left.Merge(right, context);

        left.SourceFileName.Should().Be("src/Left.tyhp");
        left.SourceRoot.Should().Be("src/");
        left.SourceMapCollector.Should().NotBeNull();
        left.SourceMapCollector!.GetMappings().Should().BeEmpty(
            "Merge replaces the collector so a subsequent Generate cannot double-track");

        var php = left.Generate(context);
        php.Should().Contain("echo 1;");
        php.Should().Contain("echo 2;");
        left.SourceMapCollector.GetMappings().Should().NotBeEmpty();
    }

    [Fact]
    public void Emit_WithGenerateSourcemap_AssignsCollectorAndSourceRootPrefix()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            namespace App;
            class Foo {}
            """, fileName: "src/App.tyhp");
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
        file.SourceMap().Should().NotBeNullOrEmpty();
        using var document = JsonDocument.Parse(file.SourceMap());
        document.RootElement.GetProperty("sourceRoot").GetString().Should().Be("src/");
        document.RootElement.GetProperty("sources").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("App.tyhp");
    }

    [Fact]
    public void Emit_WithoutGenerateSourcemap_LeavesCollectorNull()
    {
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            namespace App;
            class Foo {}
            """);
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var file = new TyhpEmitter(EmptyContext()).Emit([srcFile]).Should().ContainSingle().Subject;

        file.SourceMapCollector.Should().BeNull();
        file.SourceMap().Should().BeEmpty();
    }

    private static EmitContext EmptyContext()
        => new(new GlobalScope(), new DiagnosticBag(), new EmitConfig("build/"));

    private static PHPOutputFile ManualEchoFile(IBase2Ast provider)
    {
        var file = new PHPOutputFile
        {
            OutputFilePath = "build/App.php",
            RootEmitItem = EmitItem.Empty(provider, EmitType.FileHeader),
        };
        file.RootEmitItem.Children.Add(
            EmitItem.Line(provider, EmitType.RootStatement, "echo 1;", file.RootEmitItem));
        return file;
    }

    private static IBase2Ast Provider(int line, int column) => new TestAst(line, column);

    private sealed class TestAst : Base2Ast
    {
        public TestAst(int line, int column)
        {
            Line = line;
            Column = column;
        }
    }
}
