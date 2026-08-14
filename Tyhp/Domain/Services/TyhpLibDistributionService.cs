using System.Diagnostics;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Interop;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Manages Tyhp runtime Composer package dependencies for compiled output.
    /// Distribution is Composer-only — packages are published from <c>runtime/packages/</c> (Story 04).
    /// </summary>
    public sealed class TyhpLibDistributionService
    {
        private readonly DiagnosticBag _diagnostics;

        public TyhpLibDistributionService(DiagnosticBag diagnostics)
        {
            this._diagnostics = diagnostics;
        }

        /// <summary>
        /// Determines required runtime packages from emitter context and output content.
        /// </summary>
        public static List<string> DetermineRequiredPackages(
            IReadOnlyList<PHPOutputFile> outputFiles,
            EmitContext? emitContext = null)
        {
            if (emitContext?.RequiredPackages.Count > 0)
            {
                var packages = new HashSet<string>(emitContext.RequiredPackages, StringComparer.Ordinal)
                {
                    "tyhp/php",
                };
                return packages.OrderBy(p => p, StringComparer.Ordinal).ToList();
            }

            return ComposerJsonService.DetermineRequiredPackages(outputFiles);
        }

        /// <summary>
        /// Adds required runtime packages to <c>composer.json</c> and optionally runs <c>composer install</c>.
        /// </summary>
        public void AddRuntimePackageDependencies(
            string outputDirectory,
            Project project,
            IReadOnlyList<PHPOutputFile> outputFiles,
            EmitContext? emitContext = null,
            bool dryRun = false)
        {
            var requiredPackages = DetermineRequiredPackages(outputFiles, emitContext);
            if (requiredPackages.Count == 0)
            {
                return;
            }

            if (!project.Build.UpdateComposer)
            {
                Message.Info("CLI_RuntimePackagesNeeded", string.Join(", ", requiredPackages));
                this.ValidateInteropContractVersions(requiredPackages, outputDirectory);
                return;
            }

            if (dryRun)
            {
                Message.Info("CLI_DryRunRuntimePackagesNeeded", string.Join(", ", requiredPackages));
                this.ValidateInteropContractVersions(requiredPackages, outputDirectory);
                return;
            }

            var composerService = new ComposerJsonService(this._diagnostics);
            composerService.MergeRuntimePackages(outputDirectory, project, requiredPackages);
            _ = this.TryRunComposerInstall(outputDirectory, project);

            // After install so a freshly populated vendor/ is checked on this build, not the next.
            this.ValidateInteropContractVersions(requiredPackages, outputDirectory);
        }

        /// <summary>
        /// Ensures each required runtime package stamps
        /// <c>extra.tyhp.interopContractVersion</c> equal to
        /// <see cref="InteropContract.CurrentVersion"/>.
        /// </summary>
        /// <param name="runtimePackagePathMap">
        /// Optional override of the runtime path map (tests); when null, uses
        /// <see cref="ComposerJsonService.GetRuntimePackagePathMap"/>.
        /// </param>
        public void ValidateInteropContractVersions(
            IReadOnlyList<string> requiredPackages,
            string? outputDirectory = null,
            IReadOnlyDictionary<string, string>? runtimePackagePathMap = null)
        {
            if (requiredPackages.Count == 0)
            {
                return;
            }

            var pathMap = runtimePackagePathMap ?? ComposerJsonService.GetRuntimePackagePathMap();
            foreach (var packageName in requiredPackages.Distinct(StringComparer.Ordinal))
            {
                // tyhp/php ships tyhpdefs only — no emitted PHP interop surface to stamp.
                if (string.Equals(packageName, "tyhp/php", StringComparison.Ordinal))
                {
                    continue;
                }

                var composerJson = InteropContract.ResolvePackageComposerJson(
                    packageName,
                    pathMap,
                    outputDirectory);
                if (composerJson == null)
                {
                    // Package not on disk yet — Composer install / missing-package paths handle that.
                    continue;
                }

                var hasVersion = InteropContract.TryReadVersionFromComposerJson(composerJson, out var found);
                if (hasVersion && found == InteropContract.CurrentVersion)
                {
                    continue;
                }

                this._diagnostics.AddError(
                    MessageCode.EmitterInteropContractMismatch,
                    composerJson,
                    1,
                    0,
                    packageName,
                    hasVersion ? found.ToString() : "missing",
                    InteropContract.CurrentVersion);
            }
        }

        internal bool TryRunComposerInstall(string outputDirectory, Project project)
        {
            var composerExecutable = ExternalToolLocator.TryResolveComposerExecutable(
                project.GetProjectPath(),
                out var resolved)
                ? resolved
                : null;
            if (composerExecutable == null)
            {
                Message.Warn("CLI_ComposerNotFound");
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = composerExecutable,
                    Arguments = "install --no-interaction",
                    WorkingDirectory = outputDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                if (composerExecutable.EndsWith(".phar", StringComparison.OrdinalIgnoreCase))
                {
                    startInfo.FileName = "php";
                    startInfo.Arguments = $"\"{composerExecutable}\" install --no-interaction";
                }

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return false;
                }

                // Drain both redirected pipes concurrently before waiting. composer install
                // can emit a large amount of output; if we block on WaitForExit() without
                // reading stdout, the child can fill the OS pipe buffer and deadlock.
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                _ = stdoutTask.GetAwaiter().GetResult();
                var stderr = stderrTask.GetAwaiter().GetResult();

                if (process.ExitCode != 0 && project.Build.Verbose)
                {
                    Message.Display("CLI_VerboseComposerInstallFailed", stderr.Trim());
                }

                return process.ExitCode == 0;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                if (project.Build.Verbose)
                {
                    Message.Display("CLI_VerboseComposerInstallFailed", ex.Message);
                }

                return false;
            }
        }
    }
}
