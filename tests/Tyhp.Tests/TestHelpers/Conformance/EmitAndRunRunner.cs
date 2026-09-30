using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.TestHelpers.Conformance;

/// <summary>
/// Story 07-style conformance runner for the emit-and-run beaten-path corpus.
/// Compiles each fixture, optionally runs the emitted PHP with locked CLI flags,
/// and classifies stderr. Distinct from golden-PHP suites under <c>storyNN/</c>.
/// </summary>
public static class EmitAndRunRunner
{
    public const string EmitAndRunAction = "emit-and-run";
    public const string RunPhpAction = "run-php";

    public static IEnumerable<object[]> DiscoverAllCases()
    {
        foreach (var manifestPath in TestFileManager.GetEmitAndRunManifests())
        {
            var manifest = ConformanceRunner.LoadManifest(manifestPath);
            foreach (var testCase in manifest.Cases)
            {
                if (!string.IsNullOrWhiteSpace(testCase.Skip))
                {
                    continue;
                }

                yield return new object[] { manifest.Suite, testCase.Id };
            }
        }
    }

    public static void RunAndAssert(string suiteId, string caseId)
    {
        var manifestPath = FindManifestPath(suiteId);
        var manifest = ConformanceRunner.LoadManifest(manifestPath);
        var testCase = manifest.Cases.Single(c => string.Equals(c.Id, caseId, StringComparison.Ordinal));
        var suiteDirectory = Path.GetDirectoryName(manifestPath)!;
        var action = testCase.Action ?? manifest.Defaults?.Action ?? EmitAndRunAction;
        var expect = testCase.Expect ?? new ConformanceExpectation();

        if (string.Equals(action, RunPhpAction, StringComparison.OrdinalIgnoreCase))
        {
            RunPlantedPhpAndAssert(suiteDirectory, testCase, expect);
            return;
        }

        if (!string.Equals(action, EmitAndRunAction, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported emit-and-run action '{action}' for case '{caseId}'.");
        }

        CompileEmitAndMaybeRun(suiteDirectory, testCase, expect);
    }

    private static string FindManifestPath(string suiteId)
    {
        var path = Path.Combine(
            TestFileManager.GetConformanceDirectory(),
            suiteId.Replace('/', Path.DirectorySeparatorChar),
            "manifest.json");
        File.Exists(path).Should().BeTrue($"emit-and-run manifest should exist for suite '{suiteId}'");
        return path;
    }

    private static void RunPlantedPhpAndAssert(
        string suiteDirectory,
        ConformanceCase testCase,
        ConformanceExpectation expect)
    {
        DiagnosticAssertions.AssertExpectations(new DiagnosticBag(), expect);

        var phpPath = Path.Combine(suiteDirectory, testCase.File);
        File.Exists(phpPath).Should().BeTrue($"planted PHP should exist for case '{testCase.Id}'");
        EnsurePhpAvailable();
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        var result = PhpToolchain.RunPhpScriptStrict(phpPath);
        var failed = PhpHarnessClassifier.IsFailure(result.ExitCode, result.StandardError);
        if (expect.HarnessFail == true)
        {
            failed.Should().BeTrue(
                $"planted case '{testCase.Id}' should fail the harness (exit {result.ExitCode}, stderr: {result.StandardError})");
            return;
        }

        AssertPhpPass(testCase.Id, expect, result);
    }

    private static void CompileEmitAndMaybeRun(
        string suiteDirectory,
        ConformanceCase testCase,
        ConformanceExpectation expect)
    {
        var primaryPath = Path.Combine(suiteDirectory, testCase.File);
        File.Exists(primaryPath).Should().BeTrue($"emit-and-run input should exist for case '{testCase.Id}'");

        var extraPaths = (testCase.ExtraFiles ?? [])
            .Select(relative => Path.Combine(suiteDirectory, relative))
            .ToList();
        foreach (var extra in extraPaths)
        {
            File.Exists(extra).Should().BeTrue($"extra file should exist for case '{testCase.Id}': {extra}");
        }

        using var builder = CreateCompilationProject(suiteDirectory, primaryPath, extraPaths);
        var project = builder.BuildProject();
        var srcRoot = Path.Combine(builder.ProjectDirectory, "src");
        var tyhpFiles = Directory.GetFiles(srcRoot, "*.tyhp", SearchOption.AllDirectories);
        tyhpFiles.Should().NotBeEmpty($"case '{testCase.Id}' should copy at least one .tyhp file");

        using var compilationService = new CompilationService();
        var options = CompilationOptions.FromProject(project, o =>
        {
            o.EnableAstCache = false;
            o.SkipChecking = false;
            o.SuppressedWarnings = IsolatedCompilation.MissingPackageWarnings;
        });

        var result = compilationService.ParseFiles(tyhpFiles, options);
        var userDiagnostics = FilterUserDiagnostics(result.Diagnostics);
        DiagnosticAssertions.AssertExpectations(userDiagnostics, expect);
        AssertNoUnlistedWarnings(userDiagnostics, expect, testCase.Id);

        if (ExpectsCheckerErrors(expect))
        {
            result.Diagnostics.HasErrors.Should().BeTrue(
                $"case '{testCase.Id}' must error at check and not reach PHP");
            return;
        }

        result.Diagnostics.HasErrors.Should().BeFalse(
            $"case '{testCase.Id}' should type-check with 0 errors: {Describe(result.Diagnostics)}");
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var context = EmitContext.Create(
            result.GlobalScope,
            result.Diagnostics,
            project,
            result.RequiresRuntimeGenericTracking,
            result.RequiresWeakReferenceCapture,
            result.RequiresDisposableTryFinally,
            result.AsyncForeachKinds,
            result.RequiresGenericVariant,
            result.GenericCallTargets,
            result.InferredClosureSignatures,
            result.ExpressionTypes,
            result.NativeTypeTests);
        var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
        var emitted = string.Join(
            '\n',
            outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        AssertEmittedContains(testCase.Id, expect, emitted);

        EnsurePhpAvailable();
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        var phpResult = RunEmittedPhp(outputFiles);
        var failed = PhpHarnessClassifier.IsFailure(phpResult.ExitCode, phpResult.StandardError);
        if (expect.HarnessFail == true)
        {
            failed.Should().BeTrue(
                $"case '{testCase.Id}' should fail the harness (exit {phpResult.ExitCode}, stderr: {phpResult.StandardError})");
            return;
        }

        AssertPhpPass(testCase.Id, expect, phpResult);
    }

    private static TestProjectBuilder CreateCompilationProject(
        string suiteDirectory,
        string primaryPath,
        IReadOnlyList<string> extraPaths)
    {
        var overlayPath = Path.Combine(
            TestFileManager.GetEmitAndRunDirectory(),
            "_tyhpdef",
            "native-type-tests.tyhpdef");
        File.Exists(overlayPath).Should().BeTrue(
            $"emit-and-run NativeTypeTest overlay should exist at {overlayPath}");

        var builder = new TestProjectBuilder();
        builder.WithTyhpJson("""
            {
                "include": ["src/**/*.tyhp"],
                "output": { "path": "build/", "phpVersion": "8.4" },
                "build": { "entryPointAutoloader": { "composer": "none" } }
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg/composer.json");
        builder.WithConfigValue("build:entryPointAutoloader:composer", "none");
        builder.WithTyhpdefPackageComposer("pkg", """
            {
                "include": ["./_tyhpdef/*.tyhpdef"],
                "overlay": [
                    "./_tyhpdef/overlays/*.tyhpdef"
                ]
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/baseline.tyhpdef", SyntheticPhpStubs.MinimalPhp);
        builder.WithTyhpFile(
            "pkg/_tyhpdef/overlays/native-type-tests.tyhpdef",
            File.ReadAllText(overlayPath));

        CopyTyhpIntoSrc(builder, suiteDirectory, primaryPath);
        foreach (var extra in extraPaths)
        {
            CopyTyhpIntoSrc(builder, suiteDirectory, extra);
        }

        return builder;
    }

    private static void CopyTyhpIntoSrc(TestProjectBuilder builder, string suiteDirectory, string sourcePath)
    {
        var relative = Path.GetRelativePath(suiteDirectory, sourcePath).Replace('\\', '/');
        builder.WithTyhpFile("src/" + relative, File.ReadAllText(sourcePath));
    }

    private static PhpToolchain.ProcessResult RunEmittedPhp(IReadOnlyList<PHPOutputFile> files)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var index = 0;
            foreach (var file in files)
            {
                var name = Path.GetFileName(file.OutputFilePath);
                if (string.IsNullOrEmpty(name) || name == "run.php")
                {
                    name = $"emitted-{index}.php";
                }

                File.WriteAllText(Path.Combine(tempDir, name), file.GeneratedContent ?? string.Empty);
                index++;
            }

            var driver = Path.Combine(tempDir, "run.php");
            File.WriteAllText(driver, BuildDriver(tempDir));
            return PhpToolchain.RunPhpScriptStrict(driver);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string BuildDriver(string tempDir)
    {
        var coreDir = EmittedPhpRunner.ResolveCoreTyhpAutoloadDirectory().Replace("\\", "/");
        var emittedDir = tempDir.Replace("\\", "/");

        return $$"""
            <?php

            declare(strict_types=1);

            \spl_autoload_register(function (string $class): void {
                $base = \str_starts_with($class, 'Tyhp\\')
                    ? '{{coreDir}}/' . \str_replace('\\', '/', \substr($class, \strlen('Tyhp\\')))
                    : '{{emittedDir}}/' . \substr($class, \strrpos($class, '\\') + 1);

                if (\is_file($base . '.php')) {
                    require_once $base . '.php';
                }
            });

            foreach (\glob('{{emittedDir}}/*.php') as $emitted) {
                if (\basename($emitted) !== 'run.php') {
                    require_once $emitted;
                }
            }
            """;
    }

    private static void AssertPhpPass(
        string caseId,
        ConformanceExpectation expect,
        PhpToolchain.ProcessResult result)
    {
        PhpHarnessClassifier.IsFailure(result.ExitCode, result.StandardError).Should().BeFalse(
            $"case '{caseId}' should pass emit-and-run (exit {result.ExitCode}, stderr: {result.StandardError})");
        result.ExitCode.Should().Be(0, result.StandardError);
        result.StandardError.Should().BeEmpty($"case '{caseId}' must have empty stderr");

        if (expect.Stdout is null)
        {
            return;
        }

        NormalizeOutput(result.StandardOutput).Should().Be(
            NormalizeOutput(expect.Stdout),
            $"case '{caseId}' stdout should match the declared output");
    }

    private static void AssertEmittedContains(string caseId, ConformanceExpectation expect, string emitted)
    {
        if (expect.EmittedContains is { Count: > 0 } contains)
        {
            foreach (var needle in contains)
            {
                emitted.Should().Contain(
                    needle,
                    $"case '{caseId}' emitted PHP should contain {needle}");
            }
        }

        if (expect.EmittedNotContains is { Count: > 0 } notContains)
        {
            foreach (var needle in notContains)
            {
                emitted.Should().NotContain(
                    needle,
                    $"case '{caseId}' emitted PHP should not contain {needle}");
            }
        }
    }

    private static void AssertNoUnlistedWarnings(
        DiagnosticBag diagnostics,
        ConformanceExpectation expect,
        string caseId)
    {
        var allowed = expect.WarningCodes ?? [];
        var unexpected = diagnostics.Warnings
            .Where(d => !allowed.Contains((int)d.Code))
            .ToList();
        unexpected.Should().BeEmpty(
            $"case '{caseId}' has checker warnings that are not listed: {Describe(diagnostics)}");
    }

    private static bool ExpectsCheckerErrors(ConformanceExpectation expect)
        => expect.ErrorCountExact is > 0
            || expect.ErrorCountMin is > 0
            || (expect.Codes is { Count: > 0 } && expect.ErrorCountExact != 0);

    private static DiagnosticBag FilterUserDiagnostics(DiagnosticBag source)
    {
        var filtered = new DiagnosticBag();
        foreach (var diagnostic in source.All)
        {
            var fileName = diagnostic.FileName ?? string.Empty;
            if (fileName.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            filtered.Add(diagnostic);
        }

        return filtered;
    }

    private static string NormalizeOutput(string value)
        => value.Replace("\r\n", "\n").TrimEnd() + "\n";

    private static string Describe(DiagnosticBag diagnostics)
        => string.Join("; ", diagnostics.All.Select(d => $"{d.Code}: {d.Message}"));

    private static void EnsurePhpAvailable()
    {
        if (PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
        {
            throw new InvalidOperationException(
                "emit-and-run requires php on PATH in CI (install PHP in .github/workflows/tests.yml).");
        }
    }
}
