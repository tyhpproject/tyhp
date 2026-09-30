using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.CLI;

[Trait("Category", "Build")]
public class OutputWriterServiceTests
{
    [Fact]
    public void WriteAll_WritesGeneratedContentWithUtf8NoBom()
    {
        var tempDir = CreateTempDirectory();
        var outputPath = Path.Combine(tempDir, "out");

        try
        {
            var project = CreateProject(tempDir, outputPath: "out/");
            var diagnostics = new DiagnosticBag();
            var emitContext = new EmitContext(new GlobalScope(), diagnostics, new EmitConfig(outputPath + "/"));
            var outputFile = new PHPOutputFile
            {
                OutputFilePath = "out/App/Example.php",
                GeneratedContent = "<?php\necho 'hello';\n",
                IsEntryPoint = true,
                SourceFileAst = TyhpSrcFileAst.Create("test.tyhp", "hash"),
            };

            var result = new OutputWriterService(project, diagnostics, emitContext).WriteAll([outputFile]);

            result.FilesWritten.Should().Be(1);
            result.DirectoriesCreated.Should().Be(1);
            var writtenPath = result.WrittenPaths.Single();
            File.Exists(writtenPath).Should().BeTrue();

            var bytes = File.ReadAllBytes(writtenPath);
            if (bytes.Length >= 3)
            {
                (bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF).Should().BeFalse();
            }

            File.ReadAllText(writtenPath).Should().Be("<?php\necho 'hello';\n");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteAll_DryRun_DoesNotWriteFiles()
    {
        var tempDir = CreateTempDirectory();
        var outputPath = Path.Combine(tempDir, "out");

        try
        {
            var project = CreateProject(tempDir, outputPath: "out/", verbose: true);
            var diagnostics = new DiagnosticBag();
            var emitContext = new EmitContext(new GlobalScope(), diagnostics, new EmitConfig(outputPath + "/"));
            var outputFile = new PHPOutputFile
            {
                OutputFilePath = "out/App/Example.php",
                GeneratedContent = "<?php\necho 'hello';\n",
                IsEntryPoint = true,
                SourceFileAst = TyhpSrcFileAst.Create("test.tyhp", "hash"),
            };

            var result = new OutputWriterService(project, diagnostics, emitContext).WriteAll([outputFile], dryRun: true);

            result.FilesWritten.Should().Be(1);
            Directory.Exists(outputPath).Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteAll_DetectsPsr4PathConflict()
    {
        var tempDir = CreateTempDirectory();
        var outputPath = Path.Combine(tempDir, "build");

        try
        {
            var project = CreateProject(tempDir, outputPath: "build/");
            var diagnostics = new DiagnosticBag();
            var emitContext = new EmitContext(new GlobalScope(), diagnostics, new EmitConfig(outputPath + "/"));
            var first = new PHPOutputFile
            {
                OutputFilePath = "build/App/User.php",
                GeneratedContent = "<?php\nclass User {}\n",
                IsPSR4ObjectDeclaration = true,
                Statements = [new PhpNopStatementAst()],
                SourceFileAst = TyhpSrcFileAst.Create("a.tyhp", "hash"),
            };
            var second = new PHPOutputFile
            {
                OutputFilePath = "build/App/User.php",
                GeneratedContent = "<?php\nclass User2 {}\n",
                IsPSR4ObjectDeclaration = true,
                Statements = [new PhpNopStatementAst()],
                SourceFileAst = TyhpSrcFileAst.Create("b.tyhp", "hash"),
            };

            var result = new OutputWriterService(project, diagnostics, emitContext).WriteAll([first, second]);

            result.FilesWritten.Should().Be(1);
            result.Conflicts.Should().ContainSingle();
            diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.BuildOutputPathConflict);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteAll_DoesNotAppendSourceMappingUrlWhenNoMapProduced()
    {
        var tempDir = CreateTempDirectory();
        var outputPath = Path.Combine(tempDir, "out");

        try
        {
            var project = CreateProject(tempDir, outputPath: "out/", generateSourcemap: true);
            var diagnostics = new DiagnosticBag();
            var emitContext = new EmitContext(new GlobalScope(), diagnostics, new EmitConfig(outputPath + "/"));
            var outputFile = new PHPOutputFile
            {
                OutputFilePath = "out/App/Example.php",
                GeneratedContent = "<?php\necho 'hello';\n",
                IsEntryPoint = true,
                SourceFileAst = TyhpSrcFileAst.Create("test.tyhp", "hash"),
            };

            var result = new OutputWriterService(project, diagnostics, emitContext).WriteAll([outputFile]);
            var writtenPath = result.WrittenPaths.Single();
            var content = File.ReadAllText(writtenPath);

            // Source maps are not produced when PHPOutputFile.SourceMapCollector is null
            // (tracking emit is not enabled), so no dangling //# sourceMappingURL= comment
            // should be written and no .map file should exist.
            content.Should().NotContain("sourceMappingURL=");
            File.Exists(writtenPath + ".map").Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteAll_WithCollector_WritesMapFileAndSourceMappingUrl()
    {
        var tempDir = CreateTempDirectory();
        var outputPath = Path.Combine(tempDir, "out");

        try
        {
            var project = CreateProject(tempDir, outputPath: "out/", generateSourcemap: true);
            var diagnostics = new DiagnosticBag();
            var emitContext = new EmitContext(new GlobalScope(), diagnostics, new EmitConfig(outputPath + "/"));
            var provider = new SourceMapTestAst(1, 0);
            var outputFile = new PHPOutputFile
            {
                OutputFilePath = "out/App/Example.php",
                IsEntryPoint = true,
                SourceFileName = "src/Example.tyhp",
                SourceRoot = "src/",
                SourceFileAst = TyhpSrcFileAst.Create("src/Example.tyhp", "hash"),
                SourceMapCollector = new SourceMapCollector(),
                RootEmitItem = EmitItem.Empty(provider, EmitType.FileHeader),
            };
            outputFile.RootEmitItem.Children.Add(
                EmitItem.Line(provider, EmitType.RootStatement, "echo 'hello';", outputFile.RootEmitItem));
            outputFile.Generate(emitContext);

            var result = new OutputWriterService(project, diagnostics, emitContext).WriteAll([outputFile]);

            result.FilesWritten.Should().Be(1);
            var writtenPath = result.WrittenPaths.Single();
            File.Exists(writtenPath + ".map").Should().BeTrue();
            File.ReadAllText(writtenPath).Should().Contain("//# sourceMappingURL=Example.php.map");

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(writtenPath + ".map"));
            document.RootElement.GetProperty("version").GetInt32().Should().Be(3);
            document.RootElement.GetProperty("file").GetString().Should().Be("Example.php");
            diagnostics.HasErrors.Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteAll_DryRunWithGenerateSourcemap_DoesNotWritePhpOrMapFiles()
    {
        var tempDir = CreateTempDirectory();
        var outputPath = Path.Combine(tempDir, "out");

        try
        {
            var project = CreateProject(tempDir, outputPath: "out/", generateSourcemap: true, verbose: true);
            var diagnostics = new DiagnosticBag();
            var emitContext = new EmitContext(new GlobalScope(), diagnostics, new EmitConfig(outputPath + "/"));
            var provider = new SourceMapTestAst(1, 0);
            var outputFile = new PHPOutputFile
            {
                OutputFilePath = "out/App/Example.php",
                IsEntryPoint = true,
                SourceFileName = "src/Example.tyhp",
                SourceRoot = "src/",
                SourceFileAst = TyhpSrcFileAst.Create("src/Example.tyhp", "hash"),
                SourceMapCollector = new SourceMapCollector(),
                RootEmitItem = EmitItem.Empty(provider, EmitType.FileHeader),
            };
            outputFile.RootEmitItem.Children.Add(
                EmitItem.Line(provider, EmitType.RootStatement, "echo 'hello';", outputFile.RootEmitItem));
            outputFile.Generate(emitContext);

            var result = new OutputWriterService(project, diagnostics, emitContext)
                .WriteAll([outputFile], dryRun: true);

            result.FilesWritten.Should().Be(1);
            var writtenPath = result.WrittenPaths.Single();

            // A dry run must never touch disk, even when sourcemap generation is enabled and
            // would otherwise produce both a `.php` write (with sourceMappingURL appended) and a
            // companion `.map` write.
            File.Exists(writtenPath).Should().BeFalse();
            File.Exists(writtenPath + ".map").Should().BeFalse();
            Directory.Exists(outputPath).Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteAll_MergeThenRegenerate_DoesNotDuplicateMappings()
    {
        var tempDir = CreateTempDirectory();
        var outputPath = Path.Combine(tempDir, "out");

        try
        {
            var project = CreateProject(tempDir, outputPath: "out/", generateSourcemap: true);
            var diagnostics = new DiagnosticBag();
            var emitContext = new EmitContext(new GlobalScope(), diagnostics, new EmitConfig(outputPath + "/"));
            var first = CreateTrackedEchoFile("out/App/Merged.php", "echo 1;", line: 1);
            first.Generate(emitContext);
            var second = CreateTrackedEchoFile("out/App/Merged.php", "echo 2;", line: 2);
            second.Generate(emitContext);

            var result = new OutputWriterService(project, diagnostics, emitContext)
                .WriteAll([first, second]);

            result.FilesWritten.Should().Be(1);
            diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.EmitterMergeConflict);

            var writtenPath = result.WrittenPaths.Single();
            var php = File.ReadAllText(writtenPath);
            php.Should().Contain("echo 1;");
            php.Should().Contain("echo 2;");
            File.Exists(writtenPath + ".map").Should().BeTrue();

            first.SourceMapCollector.Should().NotBeNull();
            first.SourceMapCollector!.CurrentGeneratedLine.Should().Be(
                first.GeneratedContent!.Count(c => c == '\n'),
                "re-Generate after merge must reset the collector; a stale cursor would run past the PHP line count");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void WriteAll_IncludeSourcesContent_EmbedsOriginalFile()
    {
        var tempDir = CreateTempDirectory();
        var outputPath = Path.Combine(tempDir, "out");
        var sourceDir = Path.Combine(tempDir, "src");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "Example.tyhp"), "<?tyhp echo 1;\n");

        try
        {
            var project = CreateProject(
                tempDir,
                outputPath: "out/",
                generateSourcemap: true,
                sourceMapIncludeContent: true);
            var diagnostics = new DiagnosticBag();
            var emitContext = new EmitContext(new GlobalScope(), diagnostics, new EmitConfig(outputPath + "/"));
            var provider = new SourceMapTestAst(1, 0);
            var outputFile = new PHPOutputFile
            {
                OutputFilePath = "out/App/Example.php",
                IsEntryPoint = true,
                SourceFileName = "src/Example.tyhp",
                SourceFileAst = TyhpSrcFileAst.Create("src/Example.tyhp", "hash"),
                SourceMapCollector = new SourceMapCollector(),
                RootEmitItem = EmitItem.Empty(provider, EmitType.FileHeader),
            };
            outputFile.RootEmitItem.Children.Add(
                EmitItem.Line(provider, EmitType.RootStatement, "echo 1;", outputFile.RootEmitItem));
            outputFile.Generate(emitContext);

            var result = new OutputWriterService(project, diagnostics, emitContext).WriteAll([outputFile]);
            var mapJson = File.ReadAllText(result.WrittenPaths.Single() + ".map");
            using var document = System.Text.Json.JsonDocument.Parse(mapJson);
            document.RootElement.GetProperty("sourcesContent").EnumerateArray()
                .Select(e => e.GetString())
                .Should().Equal("<?tyhp echo 1;\n");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static Project CreateProject(
        string projectPath,
        string outputPath,
        bool verbose = false,
        bool generateSourcemap = false,
        bool sourceMapIncludeContent = false)
    {
        var projectFile = Path.Combine(projectPath, "tyhp.json");
        File.WriteAllText(projectFile, "{}");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["*project_file_path"] = projectFile,
                ["output:path"] = outputPath,
                ["verbose"] = verbose.ToString().ToLowerInvariant(),
                ["build:generateSourcemap"] = generateSourcemap.ToString().ToLowerInvariant(),
                ["build:sourcemapIncludeContent"] = sourceMapIncludeContent.ToString().ToLowerInvariant(),
            })
            .Build();

        return new Project(configuration);
    }

    private static PHPOutputFile CreateTrackedEchoFile(string outputFilePath, string echoLine, int line)
    {
        var provider = new SourceMapTestAst(line, 0);
        var file = new PHPOutputFile
        {
            OutputFilePath = outputFilePath,
            IsEntryPoint = true,
            SourceFileName = "src/Merged.tyhp",
            SourceMapCollector = new SourceMapCollector(),
            RootEmitItem = EmitItem.Empty(provider, EmitType.FileHeader),
        };
        file.RootEmitItem.Children.Add(
            EmitItem.Line(provider, EmitType.RootStatement, echoLine, file.RootEmitItem));
        return file;
    }

    private sealed class SourceMapTestAst : Base2Ast
    {
        public SourceMapTestAst(int line, int column)
        {
            Line = line;
            Column = column;
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-output-writer-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }
}

[Trait("Category", "Build")]
public class ComposerJsonServiceTests
{
    [Fact]
    public void GenerateOrUpdate_CreatesComposerJsonWithPsr4AndFunctionFiles()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(tempDir, "build");
        Directory.CreateDirectory(outputDir);

        try
        {
            var projectFile = Path.Combine(tempDir, "tyhp.json");
            File.WriteAllText(projectFile, """
                {
                    "build": { "updateComposer": true },
                    "psr4": { "App\\": "src/" }
                }
                """);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["*project_file_path"] = projectFile,
                    ["output:path"] = "build/",
                    ["build:updateComposer"] = "true",
                    ["psr4:App\\"] = "src/",
                })
                .Build();

            var project = new Project(configuration);
            var diagnostics = new DiagnosticBag();
            var outputFiles = new List<PHPOutputFile>
            {
                new()
                {
                    OutputFilePath = "build/src/Models/User.php",
                    GeneratedContent = "<?php\nnamespace App\\Models;\nclass User {}\n",
                    IsPSR4ObjectDeclaration = true,
                },
                new()
                {
                    OutputFilePath = "build/src/Helpers/_functions.php",
                    GeneratedContent = "<?php\nnamespace App\\Helpers;\nfunction helper(): void {}\n",
                },
            };

            new ComposerJsonService(diagnostics).GenerateOrUpdate(outputDir, project, outputFiles);

            var composerPath = Path.Combine(outputDir, "composer.json");
            File.Exists(composerPath).Should().BeTrue();

            var json = File.ReadAllText(composerPath);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var autoload = document.RootElement.GetProperty("autoload");

            autoload.GetProperty("psr-4").GetProperty("App\\").GetString().Should().Be("src/");
            autoload.GetProperty("files").EnumerateArray()
                .Select(element => element.GetString())
                .Should().Contain("src/Helpers/_functions.php");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GenerateOrUpdate_MergesExistingComposerJson()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(tempDir, "build");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "composer.json"), """
            {
                "name": "vendor/existing",
                "require": { "php": "^8.4" },
                "autoload": {
                    "psr-4": { "Legacy\\": "legacy/" },
                    "files": [ "legacy/bootstrap.php" ]
                }
            }
            """);

        try
        {
            var projectFile = Path.Combine(tempDir, "tyhp.json");
            File.WriteAllText(projectFile, "{}");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["*project_file_path"] = projectFile,
                    ["output:path"] = "build/",
                    ["build:updateComposer"] = "true",
                    ["psr4:App\\"] = "src/",
                })
                .Build();

            var project = new Project(configuration);
            var diagnostics = new DiagnosticBag();
            var outputFiles = new List<PHPOutputFile>
            {
                new()
                {
                    OutputFilePath = "build/src/Utils/_functions.php",
                    GeneratedContent = "<?php\n",
                },
            };

            new ComposerJsonService(diagnostics).GenerateOrUpdate(outputDir, project, outputFiles);

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "composer.json")));
            var root = document.RootElement;

            root.GetProperty("name").GetString().Should().Be("vendor/existing");
            root.GetProperty("require").GetProperty("php").GetString().Should().Be("^8.4");
            root.GetProperty("autoload").GetProperty("psr-4").GetProperty("Legacy\\").GetString().Should().Be("legacy/");
            root.GetProperty("autoload").GetProperty("files").EnumerateArray()
                .Select(element => element.GetString())
                .Should().BeEquivalentTo(["legacy/bootstrap.php", "src/Utils/_functions.php"]);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void DetermineRequiredPackages_DetectsRuntimePackagesFromContent()
    {
        var outputFiles = new List<PHPOutputFile>
        {
            new()
            {
                GeneratedContent = "<?php\nuse Tyhp\\Async\\Promise;\n",
            },
            new()
            {
                GeneratedContent = "<?php\n\\decimal('1.0');\n",
            },
        };

        ComposerJsonService.DetermineRequiredPackages(outputFiles)
            .Should().BeEquivalentTo(["tyhp/async", "tyhp/decimal", "tyhpdef/php"]);
    }

    [Fact]
    public void EncodeRuntimePackageVersion_KeepsMinorPatchAndMapsPhpMajor()
    {
        ComposerJsonService.EncodeRuntimePackageVersion("8.2", "0.0")
            .Should().Be("802.0.0");
        ComposerJsonService.EncodeRuntimePackageVersion("8.3", "0.0")
            .Should().Be("803.0.0");
        ComposerJsonService.EncodeRuntimePackageVersion("8.4", "1.2")
            .Should().Be("804.1.2");
        ComposerJsonService.EncodeRuntimePackageVersion("8.5", "0.0")
            .Should().Be("805.0.0");
        ComposerJsonService.PhpConstraintForPhpVersion("8.3").Should().Be("~8.3.0");
        ComposerJsonService.PhpConstraintForPhpVersion("9.9").Should().Be(">=8.2");
    }

    [Fact]
    public void ResolvePackagistConstraint_TyhpdefUsesSourceVersion_CompiledUsesPhpTarget()
    {
        ComposerJsonService.ResolvePackagistConstraint("tyhpdef/acme-stubs", "8.4", "0.0.1")
            .Should().Be("0.0.1");
        ComposerJsonService.ResolvePackagistConstraint("tyhpdef/acme-pack", "8.2", "0.1")
            .Should().Be("0.1");
        ComposerJsonService.ResolvePackagistConstraint("tyhp/gadget", "8.4", "0.2")
            .Should().Be("804.0.2");
        ComposerJsonService.ResolvePackagistConstraint("tyhp/spark", "8.5", "0.1")
            .Should().Be("805.0.1");
    }

    [Fact]
    public void EnsureRequireEntries_WritesLiteralGreaterThanInVersionConstraints()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var composerPath = Path.Combine(tempDir, "composer.json");
            File.WriteAllText(composerPath, """
                {
                    "name": "tyhpdef/example",
                    "require": {
                        "php": ">=8.2"
                    }
                }
                """);

            ComposerJsonService.EnsureRequireEntries(
                composerPath,
                new Dictionary<string, string> { ["tyhpdef/acme-log"] = "^3.0.0" });

            var json = File.ReadAllText(composerPath);
            json.Should().Contain(">=8.2");
            json.Should().NotContain("\\u003E");
            json.Should().Contain("tyhpdef/acme-log");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void EnsureTyhpdefRequireDevEntries_MovesPhpStubsOutOfRequireOntoDevAndExtras()
    {
        var tempDir = CreateTempDirectory();
        try
        {
            var composerPath = Path.Combine(tempDir, "composer.json");
            File.WriteAllText(composerPath, """
                {
                    "name": "tyhpdef/example",
                    "require": {
                        "php": ">=8.2",
                        "tyhpdef/acme-stubs": "0.0.1"
                    }
                }
                """);

            ComposerJsonService.EnsureTyhpdefRequireDevEntries(
                composerPath,
                ["tyhpdef/acme-stubs"]);

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(composerPath));
            var root = document.RootElement;
            var require = root.GetProperty("require");
            require.GetProperty("php").GetString().Should().Be(">=8.2");
            require.TryGetProperty("tyhpdef/acme-stubs", out _).Should().BeFalse();
            root.GetProperty("require-dev").GetProperty("tyhpdef/acme-stubs").GetString().Should().Be("@dev");
            root.GetProperty("extra").GetProperty("tyhp").GetProperty("require")
                .GetProperty("tyhpdef/acme-stubs").GetString().Should().Be("@dev");
            File.ReadAllText(composerPath).Should().NotContain("\\u003E");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GenerateOrUpdate_SkipsWhenUpdateComposerDisabled()
    {
        var tempDir = CreateTempDirectory();
        var outputDir = Path.Combine(tempDir, "build");
        Directory.CreateDirectory(outputDir);

        try
        {
            var project = new Project(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["*project_file_path"] = Path.Combine(tempDir, "tyhp.json"),
                    ["build:updateComposer"] = "false",
                })
                .Build());
            File.WriteAllText(Path.Combine(tempDir, "tyhp.json"), "{}");

            new ComposerJsonService(new DiagnosticBag()).GenerateOrUpdate(outputDir, project, []);

            File.Exists(Path.Combine(outputDir, "composer.json")).Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void GenerateOrUpdate_AutoloadPathsAreRelativeToComposerDirectory()
    {
        var tempDir = CreateTempDirectory();
        var publishDir = Path.Combine(tempDir, "publish");
        Directory.CreateDirectory(Path.Combine(publishDir, "src"));

        try
        {
            var projectFile = Path.Combine(tempDir, "tyhp.json");
            File.WriteAllText(projectFile, "{}");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["*project_file_path"] = projectFile,
                    ["output:path"] = "publish/src",
                    ["output:publishPath"] = "publish",
                    ["build:updateComposer"] = "true",
                    ["psr4:App\\"] = "src/",
                })
                .Build();

            var project = new Project(configuration);
            var outputFiles = new List<PHPOutputFile>
            {
                new()
                {
                    OutputFilePath = "publish/src/Models/User.php",
                    GeneratedContent = "<?php\nnamespace App\\Models;\nclass User {}\n",
                    IsPSR4ObjectDeclaration = true,
                },
                new()
                {
                    OutputFilePath = "publish/src/Helpers/_functions.php",
                    GeneratedContent = "<?php\n",
                },
            };

            new ComposerJsonService(new DiagnosticBag()).GenerateOrUpdate(publishDir, project, outputFiles);

            File.Exists(Path.Combine(publishDir, "composer.json")).Should().BeTrue();
            File.Exists(Path.Combine(publishDir, "src", "composer.json")).Should().BeFalse();

            using var document = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(Path.Combine(publishDir, "composer.json")));
            var autoload = document.RootElement.GetProperty("autoload");
            autoload.GetProperty("psr-4").GetProperty("App\\").GetString().Should().Be("src/");
            autoload.GetProperty("files").EnumerateArray()
                .Select(element => element.GetString())
                .Should().Contain("src/Helpers/_functions.php");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-composer-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }
}
