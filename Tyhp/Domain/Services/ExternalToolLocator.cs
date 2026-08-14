using System.Diagnostics;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Locates and probes external CLI tools (<c>php</c>, <c>composer</c>) on PATH or as a local
    /// <c>composer.phar</c>. Shared by Composer proxying, runtime package install, and integrity checks.
    /// </summary>
    public static class ExternalToolLocator
    {
        /// <summary>
        /// Resolves <paramref name="command"/> to an absolute path on <c>PATH</c> (and Windows PATHEXT).
        /// </summary>
        public static bool TryFindExecutable(string command, out string path)
        {
            path = command;
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathEnv))
            {
                return false;
            }

            foreach (var directory in pathEnv.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(directory, command);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }

                if (OperatingSystem.IsWindows())
                {
                    foreach (var extension in new[] { ".exe", ".bat", ".cmd" })
                    {
                        var withExt = candidate + extension;
                        if (File.Exists(withExt))
                        {
                            path = withExt;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Runs <c>--version</c> on <paramref name="executablePath"/> (or <c>php path.phar --version</c>
        /// for a <c>.phar</c>). Drains stdout/stderr concurrently before waiting to avoid pipe deadlocks.
        /// </summary>
        public static bool TryProbeVersion(string executablePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                if (executablePath.EndsWith(".phar", StringComparison.OrdinalIgnoreCase))
                {
                    startInfo.FileName = "php";
                    startInfo.ArgumentList.Add(executablePath);
                    startInfo.ArgumentList.Add("--version");
                }
                else
                {
                    startInfo.FileName = executablePath;
                    startInfo.ArgumentList.Add("--version");
                }

                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return false;
                }

                // Drain both pipes concurrently before WaitForExit to avoid pipe-buffer deadlock.
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                _ = stdoutTask.GetAwaiter().GetResult();
                _ = stderrTask.GetAwaiter().GetResult();
                return process.ExitCode == 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Prefers a project-local <c>composer.phar</c> that responds to <c>--version</c>, otherwise
        /// a <c>composer</c> executable on PATH that does the same.
        /// </summary>
        public static bool TryResolveComposerExecutable(string projectPath, out string path)
        {
            var localPhar = Path.Combine(projectPath, "composer.phar");
            if (File.Exists(localPhar) && TryProbeVersion(localPhar))
            {
                path = localPhar;
                return true;
            }

            if (!TryFindExecutable("composer", out path))
            {
                return false;
            }

            return TryProbeVersion(path);
        }
    }
}
