using System.Diagnostics;

namespace Tyhp.Tests.TestHelpers;

public static class PhpToolchain
{
    public static bool IsAvailable()
        => TryFindExecutable("php", out _) && TryFindExecutable("composer", out _);

    public static string GetRuntimeDirectory()
        => Path.Combine(TestFileManager.GetRepoRoot(), "runtime");

    public static ProcessResult RunComposerTest(string workingDirectory, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromMinutes(5);
        var vendorPhpUnit = Path.Combine(workingDirectory, "vendor", "bin", "phpunit");
        if (File.Exists(vendorPhpUnit))
        {
            return RunProcess(vendorPhpUnit, "--no-coverage", workingDirectory, timeout.Value);
        }

        return RunProcess("composer", "test", workingDirectory, timeout.Value);
    }

    public static ProcessResult RunComposerInstall(string workingDirectory, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromMinutes(5);
        return RunProcess("composer", "install", workingDirectory, timeout.Value);
    }

    public static ProcessResult RunPhpLint(string phpFilePath, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        return RunProcess("php", $"-l \"{phpFilePath}\"", Path.GetDirectoryName(phpFilePath) ?? ".", timeout.Value);
    }

    /// <summary>
    /// Runs a PHP script and returns its output. Lets a test assert on what emitted code actually does
    /// at runtime rather than only on the shape of the generated text.
    /// </summary>
    public static ProcessResult RunPhpScript(string phpFilePath, TimeSpan? timeout = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        return RunProcess("php", $"\"{phpFilePath}\"", Path.GetDirectoryName(phpFilePath) ?? ".", timeout.Value);
    }

    public static bool IsPhpAvailable() => TryFindExecutable("php", out _);

    private static ProcessResult RunProcess(string fileName, string arguments, string workingDirectory, TimeSpan timeout)
    {
        if (!TryFindExecutable(fileName, out var executablePath))
        {
            return new ProcessResult(-1, string.Empty, $"{fileName} was not found on PATH.");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            return new ProcessResult(-1, stdout, $"Process timed out after {timeout.TotalSeconds:F0}s.\n{stderr}");
        }

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private static bool TryFindExecutable(string command, out string path)
    {
        path = command;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, command);
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }

            if (OperatingSystem.IsWindows())
            {
                var withExe = candidate + ".exe";
                if (File.Exists(withExe))
                {
                    path = withExe;
                    return true;
                }

                var withCmd = candidate + ".cmd";
                if (File.Exists(withCmd))
                {
                    path = withCmd;
                    return true;
                }
            }
        }

        return false;
    }

    public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput
        {
            get
            {
                if (string.IsNullOrWhiteSpace(StandardError))
                {
                    return StandardOutput;
                }

                if (string.IsNullOrWhiteSpace(StandardOutput))
                {
                    return StandardError;
                }

                return StandardOutput + Environment.NewLine + StandardError;
            }
        }
    }
}
