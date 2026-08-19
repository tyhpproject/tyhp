using System.Text;
using System.Text.Json;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class SourceMapWriterTests
{
    [Fact]
    public void WriteSourceMapFile_WritesMapAlongsidePhpFile()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var phpPath = Path.Combine(tempDir, "App", "User.php");
            Directory.CreateDirectory(Path.GetDirectoryName(phpPath)!);
            File.WriteAllText(phpPath, "<?php\n");

            var diagnostics = new DiagnosticBag();
            SourceMapWriter.WriteSourceMapFile(phpPath, "{\"version\":3}", diagnostics);

            var mapPath = phpPath + ".map";
            File.Exists(mapPath).Should().BeTrue();
            File.ReadAllText(mapPath).Should().Be("{\"version\":3}");
            diagnostics.HasWarnings.Should().BeFalse();
            diagnostics.HasErrors.Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteSourceMapFile_IoFailure_AddsWarning5021()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var phpPath = Path.Combine(tempDir, "User.php");
            File.WriteAllText(phpPath, "<?php\n");
            Directory.CreateDirectory(phpPath + ".map");

            var diagnostics = new DiagnosticBag();
            SourceMapWriter.WriteSourceMapFile(phpPath, "{}", diagnostics);

            diagnostics.HasErrors.Should().BeFalse();
            var warning = diagnostics.ToList().Should().ContainSingle(d =>
                d.Code == MessageCode.EmitterSourceMapWriteFailed).Subject;
            warning.Severity.Should().Be(DiagnosticSeverity.Warning);
            warning.FormatParams.Should().Contain(phpPath + ".map");
            File.Exists(phpPath).Should().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void AppendSourceMappingUrl_AppendsCommentOnItsOwnLine()
    {
        SourceMapWriter.AppendSourceMappingUrl("<?php\necho 1;\n", "User.php.map")
            .Should().Be("<?php\necho 1;\n//# sourceMappingURL=User.php.map\n");

        SourceMapWriter.AppendSourceMappingUrl("<?php\necho 1;", "User.php.map")
            .Should().Be("<?php\necho 1;\n//# sourceMappingURL=User.php.map\n");
    }

    [Fact]
    public void AppendSourceMappingUrl_LeavesExistingCommentUnchanged()
    {
        const string already = "<?php\n//# sourceMappingURL=User.php.map\n";
        SourceMapWriter.AppendSourceMappingUrl(already, "Other.php.map").Should().Be(already);
    }

    [Fact]
    public void CreateInlineSourceMap_ProducesDecodableBase64DataUrl()
    {
        const string json = "{\"version\":3,\"file\":\"User.php\"}";
        var comment = SourceMapWriter.CreateInlineSourceMap(json);

        comment.Should().StartWith("//# sourceMappingURL=data:application/json;charset=utf-8;base64,");
        var base64 = comment["//# sourceMappingURL=data:application/json;charset=utf-8;base64,".Length..];
        Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Should().Be(json);
    }

    [Fact]
    public void WriteAllSourceMaps_WritesMapAndMutatesGeneratedContent()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var file = ManualEchoFile(Provider(1, 0));
            file.SourceFileName = "src/App.tyhp";
            file.OutputFilePath = "out/App.php";
            file.SourceMapCollector = new SourceMapCollector();
            file.Generate(EmptyContext());

            var diagnostics = new DiagnosticBag();
            var options = new SourceMapOptions
            {
                Enabled = true,
                AppendSourceMappingUrl = true,
            };
            SourceMapWriter.WriteAllSourceMaps([file], tempDir, options, diagnostics);

            var phpPath = Path.Combine(tempDir, "out", "App.php");
            File.Exists(phpPath + ".map").Should().BeTrue();
            file.GeneratedContent.Should().Contain("//# sourceMappingURL=App.php.map");

            using var document = JsonDocument.Parse(File.ReadAllText(phpPath + ".map"));
            document.RootElement.GetProperty("version").GetInt32().Should().Be(3);
            document.RootElement.GetProperty("file").GetString().Should().Be("App.php");
            diagnostics.HasErrors.Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteAllSourceMaps_Disabled_IsNoOp()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var file = ManualEchoFile(Provider(1, 0));
            file.OutputFilePath = "out/App.php";
            file.SourceMapCollector = new SourceMapCollector();
            file.Generate(EmptyContext());
            var original = file.GeneratedContent;

            SourceMapWriter.WriteAllSourceMaps(
                [file],
                tempDir,
                new SourceMapOptions { Enabled = false },
                new DiagnosticBag());

            file.GeneratedContent.Should().Be(original);
            File.Exists(Path.Combine(tempDir, "out", "App.php.map")).Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteAllSourceMaps_Inline_EmbedsDataUrlAndSkipsMapFile()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var file = ManualEchoFile(Provider(1, 0));
            file.SourceFileName = "src/App.tyhp";
            file.OutputFilePath = "out/App.php";
            file.SourceMapCollector = new SourceMapCollector();
            file.Generate(EmptyContext());

            SourceMapWriter.WriteAllSourceMaps(
                [file],
                tempDir,
                new SourceMapOptions { Enabled = true, InlineSourceMap = true },
                new DiagnosticBag());

            file.GeneratedContent.Should().Contain(
                "//# sourceMappingURL=data:application/json;charset=utf-8;base64,");
            File.Exists(Path.Combine(tempDir, "out", "App.php.map")).Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TryGenerateSourceMapJson_ProviderThrow_AddsWarning5020()
    {
        var file = ManualEchoFile(Provider(1, 0));
        file.SourceFileName = "src/App.tyhp";
        file.SourceMapCollector = new SourceMapCollector();
        file.Generate(EmptyContext());

        var diagnostics = new DiagnosticBag();
        var json = SourceMapWriter.TryGenerateSourceMapJson(
            file,
            new SourceMapOptions
            {
                Enabled = true,
                IncludeSourcesContent = true,
                SourceContentProvider = _ => throw new InvalidOperationException("boom"),
            },
            diagnostics);

        json.Should().BeNull();
        diagnostics.ToList().Should().Contain(d =>
            d.Code == MessageCode.EmitterSourceMapGenerationFailed
            && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void TryGenerateSourceMapJson_SourceIndexOutOfRange_AddsWarning5022()
    {
        var file = ManualEchoFile(Provider(1, 0));
        file.SourceMapCollector = new SourceMapCollector();
        file.Generate(EmptyContext());
        file.SourceMapCollector.GetMappings().Should().NotBeEmpty();
        file.SourceMapCollector.GetSourceFiles().Should().BeEmpty();

        var diagnostics = new DiagnosticBag();
        var json = SourceMapWriter.TryGenerateSourceMapJson(
            file,
            new SourceMapOptions { Enabled = true },
            diagnostics);

        json.Should().NotBeNullOrEmpty();
        diagnostics.ToList().Should().Contain(d =>
            d.Code == MessageCode.EmitterSourceMapInvalidMapping
            && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void SourceRootPrefixFor_PairsWithRegisteredPath()
    {
        SourceMapWriter.SourceRootPrefixFor("src/App.tyhp").Should().Be("src/");
        SourceMapWriter.SourceRootPrefixFor("src/Models/User.tyhp").Should().Be("src/Models/");
        SourceMapWriter.SourceRootPrefixFor("App.tyhp").Should().BeNull();
        SourceMapWriter.SourceRootPrefixFor(null).Should().BeNull();
        SourceMapWriter.SourceRootPrefixFor("../src/App.tyhp").Should().Be("../src/");
    }

    [Fact]
    public void CreateFileContentProvider_ReadsProjectRelativePath()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var sourceDir = Path.Combine(tempDir, "src");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "App.tyhp"), "<?tyhp echo 1;");

            var provider = SourceMapWriter.CreateFileContentProvider(tempDir);
            provider("src/App.tyhp").Should().Be("<?tyhp echo 1;");
            provider("missing.tyhp").Should().BeNull();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
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

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-sourcemap-writer-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }
}
