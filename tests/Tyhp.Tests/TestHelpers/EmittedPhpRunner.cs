using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.TestHelpers;

/// <summary>
/// Executes emitted PHP so a test can assert on runtime behavior rather than on the generated text.
/// The generated files are written to a scratch directory alongside a driver that autoloads the
/// test fixture under <c>Fixtures/php-runtime/Tyhp</c>.
/// </summary>
public static class EmittedPhpRunner
{
    /// <summary>
    /// Writes <paramref name="files"/> to a scratch directory, runs <paramref name="entryPoint"/> (a
    /// PHP statement, e.g. <c>\Probe\run();</c>) against them, and returns what the script printed.
    /// Fails the test if PHP exits non-zero.
    /// </summary>
    public static string Run(IReadOnlyList<PHPOutputFile> files, string entryPoint)
        => RunWithDriver(
            files,
            tempDir => BuildDriver([ResolveCoreTyhpAutoloadDirectory()], [], tempDir, entryPoint));

    /// <summary>
    /// Like <see cref="Run"/>, but autoloads the built runtime packages
    /// (<c>packages/dist/tyhp-&lt;name&gt;/&lt;php&gt;.latest</c> under the runtime packages root) for
    /// the PHP on <c>PATH</c> instead of the core-only fixture. <paramref name="packages"/> are the
    /// short names, e.g. <c>core</c>, <c>lambda</c>. Returns null when PHP or one of those builds is
    /// not available, so the caller can skip.
    /// </summary>
    public static string? RunAgainstRuntimeDist(
        IReadOnlyList<PHPOutputFile> files,
        string entryPoint,
        params string[] packages)
    {
        var packageDirs = ResolveRuntimeDistDirectories(packages);
        if (packageDirs is null)
        {
            return null;
        }

        var autoloadDirs = packageDirs.Select(d => Path.Combine(d, "src", "Tyhp")).ToList();
        var requires = packageDirs
            .SelectMany(d => new[] { Path.Combine(d, "src", "_functions.php") }
                .Concat(Directory.Exists(Path.Combine(d, "php"))
                    ? Directory.EnumerateFiles(Path.Combine(d, "php"), "*.php")
                    : []))
            .Where(File.Exists)
            .ToList();
        return RunWithDriver(files, tempDir => BuildDriver(autoloadDirs, requires, tempDir, entryPoint));
    }

    private static List<string>? ResolveRuntimeDistDirectories(IReadOnlyList<string> packages)
    {
        if (!PhpToolchain.IsPhpAvailable()
            || ComposerJsonService.TryResolveRuntimePackagesRoot() is not { } root)
        {
            return null;
        }

        var versionDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(versionDir);
        string version;
        try
        {
            var script = Path.Combine(versionDir, "version.php");
            File.WriteAllText(script, "<?php echo PHP_MAJOR_VERSION . \\str_pad((string) PHP_MINOR_VERSION, 2, '0', STR_PAD_LEFT);");
            var result = PhpToolchain.RunPhpScript(script);
            if (result.ExitCode != 0)
            {
                return null;
            }

            version = result.StandardOutput.Trim();
        }
        finally
        {
            try { Directory.Delete(versionDir, recursive: true); } catch { /* best effort */ }
        }

        var dirs = packages
            .Select(p => Path.Combine(root, "dist", "tyhp-" + p, version + ".latest"))
            .ToList();
        return dirs.All(d => Directory.Exists(Path.Combine(d, "src", "Tyhp"))) ? dirs : null;
    }

    private static string RunWithDriver(IReadOnlyList<PHPOutputFile> files, Func<string, string> buildDriver)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var index = 0;
            foreach (var file in files)
            {
                var name = Path.GetFileName(file.OutputFilePath);
                // Two namespaces each emit a `_functions.php`; keep both rather than overwrite.
                if (string.IsNullOrEmpty(name) || name == "run.php" || File.Exists(Path.Combine(tempDir, name)))
                {
                    name = $"emitted-{index}.php";
                }

                File.WriteAllText(Path.Combine(tempDir, name), file.GeneratedContent ?? string.Empty);
                index++;
            }

            var driver = Path.Combine(tempDir, "run.php");
            File.WriteAllText(driver, buildDriver(tempDir));

            var result = PhpToolchain.RunPhpScript(driver);
            result.ExitCode.Should().Be(0, result.CombinedOutput);
            return result.StandardOutput.ReplaceLineEndings("\n");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Test-only PHP autoload root for <c>Tyhp\*</c> symbols used by executed emit tests.
    /// </summary>
    internal static string ResolveCoreTyhpAutoloadDirectory()
    {
        var coreDir = Path.Combine(
            TestFileManager.GetTestProjectDirectory(),
            "Fixtures",
            "php-runtime",
            "Tyhp");
        if (!Directory.Exists(coreDir)
            || !Directory.EnumerateFileSystemEntries(coreDir).Any())
        {
            throw new DirectoryNotFoundException(
                $"EmittedPhpRunner: missing PHP runtime fixture at '{coreDir}'.");
        }

        return coreDir;
    }

    private static string BuildDriver(
        IReadOnlyList<string> tyhpAutoloadDirs,
        IReadOnlyList<string> requires,
        string tempDir,
        string entryPoint)
    {
        var tyhpDirs = string.Join(", ", tyhpAutoloadDirs.Select(d => $"'{d.Replace("\\", "/")}'"));
        var requireLines = string.Concat(requires.Select(r => $"require_once '{r.Replace("\\", "/")}';\n"));
        var emittedDir = tempDir.Replace("\\", "/");

        return $$"""
            <?php

            declare(strict_types=1);

            \spl_autoload_register(function (string $class): void {
                // Emitted files are one class per file, named after the class. Autoloading them rather
                // than relying on require order lets a class reference a base declared in a file that
                // sorts after it.
                $bases = \str_starts_with($class, 'Tyhp\\')
                    ? \array_map(
                        static fn (string $dir): string => $dir . '/' . \str_replace('\\', '/', \substr($class, \strlen('Tyhp\\'))),
                        [{{tyhpDirs}}])
                    : ['{{emittedDir}}/' . \substr($class, \strrpos($class, '\\') + 1)];

                foreach ($bases as $base) {
                    if (\is_file($base . '.php')) {
                        require_once $base . '.php';
                        return;
                    }
                }
            });

            {{requireLines}}
            // Free functions are not autoloadable, so every emitted file still gets required.
            foreach (\glob('{{emittedDir}}/*.php') as $emitted) {
                if (\basename($emitted) !== 'run.php') {
                    require_once $emitted;
                }
            }

            {{entryPoint}}
            """;
    }
}
