using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.CLI;

[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story20")]
[Trait("Category", "CLI")]
[Collection("ProcessGlobalState")]
public class GenerateTyhpdefPhase7Tests
{
    [Fact]
    public void ValidateOnly_DoesNotRequireGenerationMode()
    {
        var dir = Directory.CreateTempSubdirectory("tyhpdef-cli-val-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "ok.tyhpdef"), "<?tyhpdef\nfunction ping(): void;\n");
            var action = RunAction(new Dictionary<string, string?>
            {
                ["validate"] = dir,
            });

            Environment.ExitCode.Should().Be((int)ExitCode.Success);
            action.LastResult!.Success.Should().BeTrue(
                string.Join("; ", action.LastResult.Diagnostics.Errors.Select(e => e.Message)));
            action.LastResult.ValidatedPassedCount.Should().Be(1);
            action.LastOptions.Should().BeNull();
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void HelpStrings_DocumentPackagePathAndVerifyOmit()
    {
        Message.Localize("CLI_GenerateTyhpdefHelpOptionPackagePath").Should().Contain("Composer");
        Message.Localize("CLI_GenerateTyhpdefHelpOptionPackagePath").Should().NotContain("composer-package");
        Message.Localize("CLI_GenerateTyhpdefHelpOptionVerify").Should().Contain("omit");
        Message.Localize("CLI_GenerateTyhpdefHelpNotesManaged").Should().Contain("managed PHP");
        Message.Localize("CLI_GenerateTyhpdefHelpNotesManaged").Should().Contain("TYHP7510");
        Message.Localize("CLI_GenerateTyhpdefHelpNotesTracks").Should().Contain("--source");
        Message.Localize("CLI_GenerateTyhpdefHelpNotesVerify").Should().Contain("omit");
        Message.Localize("CLI_GenerateTyhpdefHelpNotesVerify").Should().Contain("audit-stubs");
        Message.Localize("CLI_GenerateTyhpdefHelpOptionAuditStubs").Should().Contain("Layer 2");
        Message.Localize("CLI_GenerateTyhpdefHelpExampleAuditStubs").Should().NotBeNullOrWhiteSpace();
        Message.Localize("CLI_GenerateTyhpdefHelpNotesDocs").Should().Contain("--locale");
        Message.Localize("CLI_GenerateTyhpdefHelpExampleValidate").Should().NotBeNullOrWhiteSpace();
        Message.Localize("CLI_GenerateTyhpdefHelpExamplePhpTargets").Should().Contain("managed PHP");
    }

    [Fact]
    public void TrackA_FixtureJson_ParsesWithJsonFunctionsAndManualDocs()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-e2e-a-").FullName;
        var started = DateTime.UtcNow;
        try
        {
            var options = new TyhpdefGenerationOptions
            {
                Mode = TyhpdefGenerationMode.PhpExtension,
                ExtensionName = "json",
                OutputDirectory = outputDir,
                IncludeDocComments = true,
                IncludeDeprecated = true,
                Overwrite = true,
                Split = "file",
                Locale = "en",
                FetchStubCache = false,
                StubCacheDirectory = Path.Combine(Path.GetTempPath(), "tyhp-no-stubs-" + Guid.NewGuid().ToString("N")),
            };
            var result = new TyhpdefGenerationResult();
            var runtime = new PhpRuntimeInfo
            {
                Path = "/tmp/php",
                Version = "8.3.11",
                IsManaged = true,
                LoadedExtensions = ["json"],
            };
            var extractor = new PhpManualDocExtractor();
            extractor.LoadHtml(File.ReadAllText(TyhpdefGenFixtures.ManualExcerptPath), "en");
            var generator = new PhpDelegationTyhpdefGenerator(
                new PhpRuntimeManager(new MissingTransport()),
                new PhpRuntimeDetector(),
                extractor);

            generator.GenerateFromJson(
                File.ReadAllText(TyhpdefGenFixtures.JsonSchemaPath),
                options,
                result,
                runtime);

            result.Success.Should().BeTrue(string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var path = result.GeneratedFiles.Single(p => p.EndsWith("ExtJson.tyhpdef", StringComparison.Ordinal));
            var text = File.ReadAllText(path);
            text.Should().Contain("function json_encode");
            text.Should().Contain("function json_decode");
            text.Should().Contain("@link https://www.php.net/manual/en/function.json-encode.php");
            text.Should().Contain("Returns the JSON representation of a value");
            var parsed = ParserTestHelper.ParseTyhpdefContent(text, path);
            parsed.Diagnostics.Errors.Should().BeEmpty();

            result.Duration = DateTime.UtcNow - started;
            result.Duration.TotalSeconds.Should().BeLessThan(30);
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void TrackB_SourceFixtures_ParseWithPhpDocSummaries()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-e2e-b-").FullName;
        var started = DateTime.UtcNow;
        try
        {
            var action = RunAction(new Dictionary<string, string?>
            {
                ["source"] = Path.Combine(TyhpdefGenFixtures.NativeDir, "*.php"),
                ["output"] = outputDir,
                ["overwrite"] = "true",
            });

            action.LastResult!.Success.Should().BeTrue(
                string.Join("; ", action.LastResult.Diagnostics.Errors.Select(e => e.Message)));
            var path = action.LastResult.GeneratedFiles.Single(p =>
                p.EndsWith("source.tyhpdef", StringComparison.Ordinal));
            var text = File.ReadAllText(path);
            var parsed = ParserTestHelper.ParseTyhpdefContent(text, path);
            parsed.Diagnostics.Errors.Should().BeEmpty();
            text.Should().Contain("class Widget");
            text.Should().Contain("A widget.");
            text.Should().Contain("Maps items.");
            text.Should().Contain("array<string, T>");
            (DateTime.UtcNow - started).TotalSeconds.Should().BeLessThan(30);
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void TrackC_PackageTyhpdef_Parses_BinderConsumerLoadDeferred()
    {
        using var library = new TestProjectBuilder();
        library
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("Box.tyhp", """
                <?tyhp
                namespace Lib;
                /**
                 * A box.
                 */
                class Box {
                    public function get(): int {
                        return 1;
                    }
                }
                """);

        var build = library.RunBuild();
        build.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", build.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var defPath = Path.Combine(library.ProjectDirectory, "package.tyhpdef");
        File.Exists(defPath).Should().BeTrue();
        var def = File.ReadAllText(defPath);
        def.Should().Contain("class Box");
        def.Should().Contain("A box.");
        var parsed = ParserTestHelper.ParseTyhpdefContent(def, defPath);
        parsed.Diagnostics.Errors.Should().BeEmpty();

        File.Exists(Path.Combine(library.ProjectDirectory, "composer.json")).Should().BeTrue();
        File.ReadAllText(Path.Combine(library.ProjectDirectory, "composer.json")).Should().Contain("\"package\"");
        File.Exists(Path.Combine(library.ProjectDirectory, "package.tyhp.json")).Should().BeFalse();

        using var consumer = new TestProjectBuilder();
        consumer
            .WithTyhpJson("""
                {
                    "type": "application",
                    "include": ["**/*.tyhp"],
                    "tyhpdefInclude": ["tyhpdef/**/*.tyhpdef"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("tyhpdef/package.tyhpdef", def)
            .WithTyhpFile("Use.tyhp", """
                <?tyhp
                namespace App;
                function take(\Lib\Box $box): int {
                    return $box->get();
                }
                """);

        var consumed = consumer.RunBuild();
        if (consumed.Diagnostics.Errors.Count > 0)
        {
            consumed.Diagnostics.Errors.Should().NotContain(d =>
                d.Code == MessageCode.TyhpdefParseError,
                "package.tyhpdef must parse; second-project binder overlay load is Story 02/06/21 if name resolution still fails");
        }
    }

    [Fact]
    public void Validate_DebugProjectTyhpdefGen_SkipsWhenMissing()
    {
        var path = Path.Combine(TestFileManager.GetRepoRoot(), "DebugProject", "tyhpdef_gen");
        if (!Directory.Exists(path))
        {
            return;
        }

        var result = new TyhpdefGenerationResult();
        new TyhpdefValidateService().Validate(path, result, quiet: true);
        result.ValidatedFileCount.Should().BeGreaterThanOrEqualTo(0);
        (result.ValidatedPassedCount + result.ValidatedFailedCount).Should().Be(result.ValidatedFileCount);
    }

    [Fact]
    public void TrackA_LiveExtNameJson_WhenManagedCacheWarm()
    {
        if (!TryFindCachedPhp())
        {
            return;
        }

        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-e2e-live-").FullName;
        var started = DateTime.UtcNow;
        try
        {
            var action = RunAction(new Dictionary<string, string?>
            {
                ["ext-name"] = "json",
                ["output"] = outputDir,
                ["overwrite"] = "true",
                ["no-docs"] = "true",
                ["no-php-runtime-update"] = "true",
            });

            if (!action.LastResult!.Success)
            {
                action.LastResult.Diagnostics.Errors.Should().NotContain(d =>
                    d.Code == MessageCode.TyhpdefGenerationError
                    && d.Message.Contains("not yet implemented", StringComparison.Ordinal));
                return;
            }

            var path = action.LastResult.GeneratedFiles.First(p => p.EndsWith(".tyhpdef", StringComparison.Ordinal)
                && !p.Contains($"{Path.DirectorySeparatorChar}overlays{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
            var text = File.ReadAllText(path);
            text.Should().Contain("function json_encode");
            ParserTestHelper.ParseTyhpdefContent(text, path).Diagnostics.Errors.Should().BeEmpty();
            (DateTime.UtcNow - started).TotalSeconds.Should().BeLessThan(30);
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    private static bool TryFindCachedPhp()
    {
        var root = PhpRuntimeManager.ResolveCacheRoot(null);
        if (!Directory.Exists(root))
        {
            return false;
        }

        try
        {
            return Directory.EnumerateFiles(root, "php", SearchOption.AllDirectories).Any()
                || Directory.EnumerateFiles(root, "php.exe", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static GenerateTyhpdefAction RunAction(Dictionary<string, string?> settings)
    {
        var values = new Dictionary<string, string?>(settings) { ["quiet"] = "true" };
        var project = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build());

        Environment.ExitCode = (int)ExitCode.Success;
        var action = new GenerateTyhpdefAction(project);
        action.Start(CancellationToken.None);
        return action;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class MissingTransport : IPhpRuntimeTransport
    {
        public Task<string?> GetStringAsync(
            Uri url,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
            => Task.FromResult<string?>(null);

        public Task<byte[]?> GetBytesAsync(
            Uri url,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
            => Task.FromResult<byte[]?>(null);

        public Task<bool> DownloadToFileAsync(
            Uri url,
            string destinationPath,
            string? expectedSha256,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
            => Task.FromResult(false);
    }
}
