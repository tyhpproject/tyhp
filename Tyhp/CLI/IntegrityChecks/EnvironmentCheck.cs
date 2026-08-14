using System.Reflection;
using System.Runtime.InteropServices;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;

namespace Tyhp.CLI.IntegrityChecks
{
    /// <summary>
    /// Reports runtime environment details (informational probes for PHP/Composer).
    /// </summary>
    public sealed class EnvironmentCheck : IIntegrityCheck
    {
        /// <summary>Warn when free space on the output volume falls below this threshold.</summary>
        private const long LowDiskSpaceBytes = 50L * 1024L * 1024L;

        private readonly Project _project;

        public EnvironmentCheck(Project project)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public string Name => Message.Localize("CLI_IntegrityCheckNameEnvironment");

        public Task<IntegrityCheckResult> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var details = new List<string>();
            var warnings = new List<string>();

            var tyhpVersion = new Message.VersionHelper().GetAssemblyVersion();
            details.Add(Message.Localize("CLI_IntegrityEnvTyhpVersion", tyhpVersion));
            details.Add(Message.Localize(
                "CLI_IntegrityEnvDotNetVersion",
                RuntimeInformation.FrameworkDescription));
            details.Add(Message.Localize(
                "CLI_IntegrityEnvAntlrVersion",
                GetAntlrRuntimeVersion()));
            details.Add(Message.Localize(
                "CLI_IntegrityEnvOs",
                RuntimeInformation.OSDescription));

            ct.ThrowIfCancellationRequested();

            if (ExternalToolLocator.TryFindExecutable("php", out var phpPath)
                && ExternalToolLocator.TryProbeVersion(phpPath))
            {
                details.Add(Message.Localize("CLI_IntegrityEnvPhpFound", phpPath));
            }
            else
            {
                // Informational only — PHP is not required for compilation.
                details.Add(Message.Localize("CLI_IntegrityEnvPhpMissing"));
            }

            if (ExternalToolLocator.TryResolveComposerExecutable(
                    this._project.GetProjectPath(),
                    out var composerPath))
            {
                details.Add(Message.Localize("CLI_IntegrityEnvComposerFound", composerPath));
            }
            else
            {
                details.Add(Message.Localize("CLI_IntegrityEnvComposerMissing"));
            }

            ct.ThrowIfCancellationRequested();

            TryCheckDiskSpace(details, warnings);

            if (warnings.Count > 0)
            {
                return Task.FromResult(IntegrityCheckResult.Fail(
                    Message.Localize("CLI_IntegrityEnvFailed", warnings.Count),
                    details,
                    warnings,
                    isWarning: true));
            }

            return Task.FromResult(IntegrityCheckResult.Pass(
                Message.Localize("CLI_IntegrityEnvPassed"),
                details));
        }

        private void TryCheckDiskSpace(List<string> details, List<string> warnings)
        {
            try
            {
                var projectPath = PathCanonicalizer.GetCanonicalFullPath(this._project.GetProjectPath());
                var outputRelative = this._project.Output.Path;
                var outputPath = Path.IsPathRooted(outputRelative)
                    ? PathCanonicalizer.GetCanonicalFullPath(outputRelative)
                    : PathCanonicalizer.GetCanonicalFullPath(Path.Combine(projectPath, outputRelative));

                var probePath = Directory.Exists(outputPath)
                    ? outputPath
                    : (Directory.Exists(projectPath) ? projectPath : Path.GetTempPath());

                var root = Path.GetPathRoot(PathCanonicalizer.GetCanonicalFullPath(probePath));
                if (string.IsNullOrEmpty(root))
                {
                    details.Add(Message.Localize("CLI_IntegrityEnvDiskUnknown"));
                    return;
                }

                var drive = new DriveInfo(root);
                if (!drive.IsReady)
                {
                    details.Add(Message.Localize("CLI_IntegrityEnvDiskNotReady", root));
                    return;
                }

                var freeBytes = drive.AvailableFreeSpace;
                details.Add(Message.Localize(
                    "CLI_IntegrityEnvDiskFree",
                    root,
                    FormatBytes(freeBytes)));

                if (freeBytes < LowDiskSpaceBytes)
                {
                    warnings.Add(Message.Localize(
                        "CLI_IntegrityEnvDiskLow",
                        root,
                        FormatBytes(freeBytes),
                        FormatBytes(LowDiskSpaceBytes)));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                details.Add(Message.Localize("CLI_IntegrityEnvDiskCheckFailed", ex.Message));
            }
        }

        private static string FormatBytes(long bytes)
        {
            const double mb = 1024d * 1024d;
            if (bytes >= mb * 1024d)
            {
                return $"{bytes / (mb * 1024d):F1} GB";
            }

            return $"{bytes / mb:F1} MB";
        }

        private static string GetAntlrRuntimeVersion()
        {
            var assembly = typeof(Antlr4.Runtime.Lexer).Assembly;
            var version = assembly.GetName().Version?.ToString();
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }

            var informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            return string.IsNullOrWhiteSpace(informational) ? "unknown" : informational;
        }
    }
}
