using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            File.WriteAllText(driver, BuildDriver(tempDir, entryPoint));

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

    private static string BuildDriver(string tempDir, string entryPoint)
    {
        var coreDir = ResolveCoreTyhpAutoloadDirectory().Replace("\\", "/");
        var emittedDir = tempDir.Replace("\\", "/");

        return $$"""
            <?php

            declare(strict_types=1);

            \spl_autoload_register(function (string $class): void {
                // Emitted files are one class per file, named after the class. Autoloading them rather
                // than relying on require order lets a class reference a base declared in a file that
                // sorts after it.
                $base = \str_starts_with($class, 'Tyhp\\')
                    ? '{{coreDir}}/' . \str_replace('\\', '/', \substr($class, \strlen('Tyhp\\')))
                    : '{{emittedDir}}/' . \substr($class, \strrpos($class, '\\') + 1);

                if (\is_file($base . '.php')) {
                    require_once $base . '.php';
                }
            });

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
