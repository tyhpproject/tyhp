using System.Diagnostics;

namespace Tyhp.Domain.Services
{
    public sealed record ComposerCommandResult(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>
    /// Runs Composer in a project directory. Tests inject a fake; production uses PATH / composer.phar.
    /// </summary>
    public interface IComposerCommandRunner
    {
        ComposerCommandResult Run(
            string projectRoot,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? extraEnvironment);
    }

    public sealed class ComposerCommandRunner : IComposerCommandRunner
    {
        internal Func<string, string?>? ResolveComposer { get; init; }

        public ComposerCommandResult Run(
            string projectRoot,
            IReadOnlyList<string> arguments,
            IReadOnlyDictionary<string, string>? extraEnvironment)
        {
            var resolve = this.ResolveComposer
                ?? (root => ExternalToolLocator.TryResolveComposerExecutable(root, out var path) ? path : null);
            var composerExecutable = resolve(projectRoot);
            if (string.IsNullOrWhiteSpace(composerExecutable))
            {
                return new ComposerCommandResult(1, "", "composer not found");
            }

            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = projectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (composerExecutable.EndsWith(".phar", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = "php";
                startInfo.ArgumentList.Add(composerExecutable);
            }
            else
            {
                startInfo.FileName = composerExecutable;
            }

            foreach (var arg in arguments)
            {
                startInfo.ArgumentList.Add(arg);
            }

            if (extraEnvironment is not null)
            {
                foreach (var (key, value) in extraEnvironment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return new ComposerCommandResult(1, "", "failed to start composer");
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                return new ComposerCommandResult(
                    process.ExitCode,
                    stdoutTask.GetAwaiter().GetResult(),
                    stderrTask.GetAwaiter().GetResult());
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                return new ComposerCommandResult(1, "", ex.Message);
            }
        }
    }
}
