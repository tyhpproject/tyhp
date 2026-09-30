using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Extensions;

namespace Tyhp.CLI
{
    /// <summary>
    /// CLI verb <c>install</c>: download toolchain tools. First target is Composer
    /// (<c>tyhp install composer</c>). Distinct from <c>tyhp composer install</c>.
    /// </summary>
    public class InstallAction : ActionRunnerBase
    {
        internal const string ComposerTarget = "composer";
        internal const string LocalFileName = "composer.phar";
        internal const string GlobalFileName = "composer";

        private readonly Project _project;

        /// <summary>Post-verb argv. Tests inject this; production reads <see cref="ActionConfigProvider.RemainingArgs"/>.</summary>
        internal IReadOnlyList<string>? Args { get; init; }

        internal IComposerInstallerTransport? Transport { get; init; }

        internal IComposerSetupRunner? SetupRunner { get; init; }

        /// <summary>Returns the PHP executable path, or null when PHP is missing.</summary>
        internal Func<string?>? FindPhp { get; init; }

        /// <summary>Probes a Composer binary (destination or PATH). Defaults to <see cref="ExternalToolLocator.TryProbeVersion"/>.</summary>
        internal Func<string, bool>? ProbeComposerExecutable { get; init; }

        /// <summary>Override for <c>~/.local/bin</c> so tests never write the real global prefix.</summary>
        internal string? GlobalInstallDirectory { get; init; }

        /// <summary>Last diagnostic code when the action failed; null on success or help.</summary>
        internal MessageCode? LastError { get; private set; }

        public InstallAction(Project project)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            this.LastError = null;
            cancellationToken.ThrowIfCancellationRequested();

            var args = this.Args ?? ActionConfigProvider.RemainingArgs;
            var parsed = ParseArgs(args, this._project);

            if (parsed.ShowHelp)
            {
                DisplayHelp.InstallHelp();
                Environment.ExitCode = (int)ExitCode.Success;
                return null;
            }

            if (parsed.ConflictingLocationFlags)
            {
                this.Fail(MessageCode.InstallConflictingLocationFlags);
                return null;
            }

            if (string.IsNullOrEmpty(parsed.Target))
            {
                DisplayHelp.InstallHelp();
                Environment.ExitCode = (int)ExitCode.Success;
                return null;
            }

            if (!string.Equals(parsed.Target, ComposerTarget, StringComparison.OrdinalIgnoreCase))
            {
                this.Fail(MessageCode.InstallUnknownTarget, parsed.Target, ComposerTarget);
                return null;
            }

            this.InstallComposer(parsed, cancellationToken);
            return null;
        }

        private void InstallComposer(ParsedInstallArgs parsed, CancellationToken cancellationToken)
        {
            var projectPath = this._project.GetProjectPath();
            var destination = parsed.Global
                ? Path.Combine(this.ResolveGlobalInstallDirectory(), GlobalFileName)
                : Path.Combine(projectPath, LocalFileName);

            if (File.Exists(destination) && !parsed.Force)
            {
                if (!this._project.BeQuiet)
                {
                    Message.Info("CLI_InstallComposerAlreadyPresent", destination);
                }

                Environment.ExitCode = (int)ExitCode.Success;
                return;
            }

            var phpPath = this.ResolvePhp();
            if (phpPath is null)
            {
                this.Fail(MessageCode.InstallPhpNotFound);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var transport = this.Transport ?? new ComposerInstallerTransport();
            byte[]? installer;
            string? signature;
            try
            {
                installer = transport.DownloadInstallerAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                signature = transport.DownloadInstallerSignatureAsync(cancellationToken)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                this.Fail(MessageCode.InstallComposerDownloadFailed, ex.Message);
                return;
            }

            if (installer is null || installer.Length == 0)
            {
                this.Fail(MessageCode.InstallComposerDownloadFailed, "installer");
                return;
            }

            if (string.IsNullOrWhiteSpace(signature))
            {
                this.Fail(MessageCode.InstallComposerDownloadFailed, "signature");
                return;
            }

            if (!ComposerInstallerChecksum.Matches(installer, signature))
            {
                this.Fail(MessageCode.InstallComposerChecksumMismatch);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var setupScriptPath = Path.Combine(
                Path.GetTempPath(),
                "tyhp-composer-setup-" + Guid.NewGuid().ToString("N") + ".php");
            var stagingDir = Path.Combine(
                Path.GetTempPath(),
                "tyhp-composer-install-" + Guid.NewGuid().ToString("N"));
            var fileName = parsed.Global ? GlobalFileName : LocalFileName;

            try
            {
                File.WriteAllBytes(setupScriptPath, installer);
                Directory.CreateDirectory(stagingDir);

                var runner = this.SetupRunner ?? new ComposerSetupRunner();
                var exitCode = runner.Run(phpPath, setupScriptPath, stagingDir, fileName, cancellationToken);
                if (exitCode != 0)
                {
                    this.Fail(MessageCode.InstallComposerSetupFailed, exitCode);
                    return;
                }

                var staged = Path.Combine(stagingDir, fileName);
                if (!File.Exists(staged))
                {
                    this.Fail(MessageCode.InstallComposerSetupFailed, "missing-output");
                    return;
                }

                var destDir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                File.Move(staged, destination);
                TryMakeExecutable(destination);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or NotSupportedException)
            {
                this.Fail(MessageCode.InstallComposerSetupFailed, ex.Message);
                return;
            }
            finally
            {
                TryDeleteFile(setupScriptPath);
                TryDeleteDirectory(stagingDir);
            }

            if (!this.ComposerLooksInstalled(projectPath, destination))
            {
                TryDeleteFile(destination);
                this.Fail(MessageCode.InstallComposerNotFoundAfterInstall, destination);
                return;
            }

            if (!this._project.BeQuiet)
            {
                Message.Success("CLI_InstallComposerSuccess", destination);
            }

            Environment.ExitCode = (int)ExitCode.Success;
        }

        /// <summary>
        /// Re-probes the destination, then <see cref="ExternalToolLocator.TryResolveComposerExecutable"/>
        /// in production (local <c>composer.phar</c> is the locator's first choice).
        /// </summary>
        private bool ComposerLooksInstalled(string projectPath, string destination)
        {
            var probe = this.ProbeComposerExecutable ?? ExternalToolLocator.TryProbeVersion;
            if (File.Exists(destination) && probe(destination))
            {
                return true;
            }

            if (this.ProbeComposerExecutable != null)
            {
                return false;
            }

            return ExternalToolLocator.TryResolveComposerExecutable(projectPath, out _);
        }

        private string? ResolvePhp()
        {
            if (this.FindPhp != null)
            {
                return this.FindPhp();
            }

            if (ExternalToolLocator.TryFindExecutable("php", out var phpPath)
                && ExternalToolLocator.TryProbeVersion(phpPath))
            {
                return phpPath;
            }

            return null;
        }

        private string ResolveGlobalInstallDirectory()
        {
            if (!string.IsNullOrWhiteSpace(this.GlobalInstallDirectory))
            {
                return this.GlobalInstallDirectory;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "bin");
        }

        private void Fail(MessageCode code, params object[] args)
        {
            this.LastError = code;
            Message.TyhpError("install", 0, 0, (int)code, args);
            Environment.ExitCode = (int)ExitCode.GenericError;
        }

        internal static ParsedInstallArgs ParseArgs(IReadOnlyList<string> args, Project project)
        {
            string? target = null;
            var force = project.GetConfigValue("force").ParseBool();
            var global = project.GetConfigValue("global").ParseBool();
            var local = project.GetConfigValue("local").ParseBool();
            var showHelp = false;

            foreach (var arg in args)
            {
                if (TryGetLongFlagName(arg, out var flagName, out var inlineValue))
                {
                    if (string.Equals(flagName, "help", StringComparison.OrdinalIgnoreCase))
                    {
                        showHelp = inlineValue is null || inlineValue.ParseBool();
                        continue;
                    }

                    if (string.Equals(flagName, "force", StringComparison.OrdinalIgnoreCase))
                    {
                        force = inlineValue is null || inlineValue.ParseBool();
                        continue;
                    }

                    if (string.Equals(flagName, "global", StringComparison.OrdinalIgnoreCase))
                    {
                        global = inlineValue is null || inlineValue.ParseBool();
                        continue;
                    }

                    if (string.Equals(flagName, "local", StringComparison.OrdinalIgnoreCase))
                    {
                        local = inlineValue is null || inlineValue.ParseBool();
                        continue;
                    }

                    continue;
                }

                if (arg.StartsWith('-'))
                {
                    continue;
                }

                target ??= arg;
            }

            return new ParsedInstallArgs(
                target,
                force,
                global,
                local,
                showHelp,
                global && local);
        }

        private static bool TryGetLongFlagName(string arg, out string flagName, out string? inlineValue)
        {
            flagName = string.Empty;
            inlineValue = null;
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                return false;
            }

            var body = arg.AsSpan(2);
            var eq = body.IndexOf('=');
            if (eq >= 0)
            {
                flagName = body[..eq].ToString();
                inlineValue = body[(eq + 1)..].ToString();
            }
            else
            {
                flagName = body.ToString();
            }

            return flagName.Length > 0;
        }

        private static void TryMakeExecutable(string path)
        {
            if (OperatingSystem.IsWindows() || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // The official installer also chmod's; a later php composer.phar invoke still works.
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        internal readonly record struct ParsedInstallArgs(
            string? Target,
            bool Force,
            bool Global,
            bool Local,
            bool ShowHelp,
            bool ConflictingLocationFlags);
    }
}
