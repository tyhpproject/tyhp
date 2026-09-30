using System.Diagnostics;
using System.Text;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Runs a PHP executable with captured stdout/stderr and a timeout.
    /// Never uses <c>php -r</c>.
    /// </summary>
    public static class PhpProcessRunner
    {
        public sealed record Result(
            int ExitCode,
            string StandardOutput,
            string StandardError,
            bool TimedOut);

        /// <summary>
        /// Invoke <paramref name="phpPath"/> with optional isolated <c>-n -c ini</c> (managed only).
        /// </summary>
        public static Result Run(
            PhpRuntimeInfo runtime,
            IReadOnlyList<string> arguments,
            int timeoutMs,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? extraEnvironment = null)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            var args = new List<string>();
            if (runtime.IsManaged && !string.IsNullOrWhiteSpace(runtime.IniPath))
            {
                args.Add("-n");
                args.Add("-c");
                args.Add(runtime.IniPath);
            }

            args.AddRange(arguments);
            return Run(runtime.Path, args, timeoutMs, cancellationToken, extraEnvironment, runtime);
        }

        public static Result Run(
            string phpPath,
            IReadOnlyList<string> arguments,
            int timeoutMs,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? extraEnvironment = null,
            PhpRuntimeInfo? runtime = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(phpPath);
            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = phpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (runtime?.IsManaged == true)
            {
                startInfo.Environment["PHP_INI_SCAN_DIR"] = "";
            }

            if (extraEnvironment is not null)
            {
                foreach (var (key, value) in extraEnvironment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            ApplyLibrarySearchPath(startInfo, runtime);

            using var process = new Process { StartInfo = startInfo };
            try
            {
                if (!process.Start())
                {
                    return new Result(-1, "", "failed to start PHP process", TimedOut: false);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return new Result(-1, "", ex.Message, TimedOut: false);
            }

            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The child may have already exited.
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var timeout = timeoutMs <= 0 ? 60_000 : timeoutMs;

            try
            {
                if (!process.WaitForExit(timeout))
                {
                    TryKill(process);
                    return new Result(-1, "", "PHP process timed out", TimedOut: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or SystemException)
            {
                return new Result(-1, "", ex.Message, TimedOut: false);
            }

            string stdout;
            string stderr;
            try
            {
                stdout = stdoutTask.GetAwaiter().GetResult();
                stderr = stderrTask.GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or AggregateException)
            {
                return new Result(process.HasExited ? process.ExitCode : -1, "", ex.Message, TimedOut: false);
            }

            return new Result(process.ExitCode, stdout ?? "", stderr ?? "", TimedOut: false);
        }

        private static void ApplyLibrarySearchPath(ProcessStartInfo startInfo, PhpRuntimeInfo? runtime)
        {
            if (runtime is null || string.IsNullOrWhiteSpace(runtime.ExtensionDir))
            {
                return;
            }

            var libDir = Path.GetDirectoryName(runtime.ExtensionDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(libDir) || !Directory.Exists(libDir))
            {
                libDir = runtime.ExtensionDir;
            }

            if (OperatingSystem.IsMacOS())
            {
                PrependEnv(startInfo, "DYLD_FALLBACK_LIBRARY_PATH", libDir);
            }
            else if (OperatingSystem.IsLinux())
            {
                PrependEnv(startInfo, "LD_LIBRARY_PATH", libDir);
            }
        }

        private static void PrependEnv(ProcessStartInfo startInfo, string name, string value)
        {
            var existing = startInfo.Environment.TryGetValue(name, out var current) ? current : "";
            startInfo.Environment[name] = string.IsNullOrWhiteSpace(existing)
                ? value
                : value + Path.PathSeparator + existing;
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                // Best-effort timeout abort.
            }
        }
    }
}
