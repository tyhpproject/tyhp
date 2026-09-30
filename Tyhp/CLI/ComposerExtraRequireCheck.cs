using System.Text.Json;
using System.Text.Json.Nodes;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;

namespace Tyhp.CLI
{
    /// <summary>
    /// Applies the local extras stale check to build / lint / <c>tyhp composer sync</c>.
    /// </summary>
    internal sealed class ComposerExtraRequireCheck
    {
        /// <summary>
        /// Optional Composer process runner. Tests inject this so the check never talks to
        /// Packagist or a real Composer binary.
        /// </summary>
        internal Func<string, IReadOnlyList<string>, string, int>? RunComposerProcess { get; init; }

        internal Func<string, string?>? ResolveComposer { get; init; }

        /// <summary>
        /// Writes diagnostics (and optionally root <c>require-dev</c> + Composer update).
        /// Returns <see langword="false"/> when the compile must stop (missing extras, disjoint
        /// pins, or a write/parse failure). A compiler-only gap on the native CLI is a warning
        /// and returns <see langword="true"/>.
        /// </summary>
        public bool TryApply(
            Project project,
            DiagnosticBag diagnostics,
            bool fix,
            bool dryRun)
        {
            var projectRoot = project.GetProjectPath();
            var report = ComposerExtraRequireGraph.Analyze(projectRoot);
            return this.ApplyReport(project, diagnostics, report, fix, dryRun);
        }

        internal bool ApplyReport(
            Project project,
            DiagnosticBag diagnostics,
            ComposerExtraRequireGraph.Report report,
            bool fix,
            bool dryRun)
        {
            if (report.NoComposerJson)
            {
                return true;
            }

            var help = Message.Localize("CLI_ComposerExtrasSyncHint");

            if (report.ParseError is not null)
            {
                diagnostics.Add(
                    Diagnostic.Error(
                            MessageCode.ComposerRootJsonWriteFailed,
                            report.ComposerJsonPath,
                            0,
                            0,
                            [report.ComposerJsonPath, report.ParseError])
                        .WithHelp(help));
                return false;
            }

            foreach (var pin in report.Disjoint)
            {
                diagnostics.Add(
                    Diagnostic.Error(
                            MessageCode.ComposerDisjointExtraRequire,
                            report.ComposerJsonPath,
                            0,
                            0,
                            [pin.ExtraConstraint, pin.Package, pin.ExistingConstraint])
                        .WithHelp(help));
            }

            if (report.Disjoint.Count > 0)
            {
                return false;
            }

            if (fix && !dryRun)
            {
                return this.TryFix(project, diagnostics, report, help);
            }

            foreach (var name in report.MissingExtras)
            {
                diagnostics.Add(
                    Diagnostic.Error(
                            MessageCode.ComposerStaleExtraRequire,
                            report.ComposerJsonPath,
                            0,
                            0,
                            [name])
                        .WithHelp(help));
            }

            foreach (var name in report.UninstalledNamedPackages)
            {
                diagnostics.Add(
                    Diagnostic.Error(
                            MessageCode.ComposerStaleExtraRequire,
                            report.ComposerJsonPath,
                            0,
                            0,
                            [name])
                        .WithHelp(help));
            }

            if (report.CompilerGap)
            {
                diagnostics.Add(
                    Diagnostic.Warning(
                            MessageCode.ComposerMissingCompilerPin,
                            report.ComposerJsonPath,
                            0,
                            0,
                            [ComposerExtraRequireGraph.CorePackage])
                        .WithHelp(help));
            }

            return !report.HasBlockingErrors;
        }

        private bool TryFix(
            Project project,
            DiagnosticBag diagnostics,
            ComposerExtraRequireGraph.Report report,
            string help)
        {
            if (!TryWriteRequireDev(report.ComposerJsonPath, report.MergedRequireDev, out var writeError))
            {
                diagnostics.Add(
                    Diagnostic.Error(
                            MessageCode.ComposerRootJsonWriteFailed,
                            report.ComposerJsonPath,
                            0,
                            0,
                            [report.ComposerJsonPath, writeError ?? ""])
                        .WithHelp(help));
                return false;
            }

            if (report.UninstalledNamedPackages.Count > 0)
            {
                Message.Info(
                    "CLI_ComposerExtrasUnknownUntilInstall",
                    string.Join(", ", report.UninstalledNamedPackages));
            }

            if (report.PackagesToUpdate.Count == 0)
            {
                var afterWrite = ComposerExtraRequireGraph.Analyze(project.GetProjectPath());
                return this.ApplyReport(project, diagnostics, afterWrite, fix: false, dryRun: true);
            }

            if (!this.TryInvokeComposerUpdate(
                    project,
                    report.PackagesToUpdate,
                    diagnostics,
                    report.ComposerJsonPath,
                    help))
            {
                return false;
            }

            var after = ComposerExtraRequireGraph.Analyze(project.GetProjectPath());
            return this.ApplyReport(project, diagnostics, after, fix: false, dryRun: true);
        }

        internal static bool TryWriteRequireDev(
            string composerJsonPath,
            IReadOnlyDictionary<string, string> mergedRequireDev,
            out string? error)
        {
            error = null;
            try
            {
                var json = File.ReadAllText(composerJsonPath);
                var status = InitComposerManifest.TryParse(json, out var root, out var jsonError);
                if (status != InitComposerManifest.ParseStatus.Ok || root is null)
                {
                    error = jsonError ?? "root is not a JSON object";
                    return false;
                }

                var existing = root["require-dev"] as JsonObject ?? new JsonObject();
                var ordered = OrderRequireDev(existing, mergedRequireDev);
                var written = new JsonObject();
                foreach (var (name, constraint) in ordered)
                {
                    written[name] = constraint;
                }

                root["require-dev"] = written;
                InitComposerManifest.MergeAllowPlugins(root);
                File.WriteAllText(composerJsonPath, InitComposerManifest.ToJson(root));
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Keep existing require-dev key order, then append new names sorted. Same as the PHP plugin.
        /// </summary>
        internal static List<KeyValuePair<string, string>> OrderRequireDev(
            JsonObject existing,
            IReadOnlyDictionary<string, string> merged)
        {
            var ordered = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in existing)
            {
                var name = ComposerExtraRequireGraph.NormalizeName(property.Key);
                if (!merged.TryGetValue(name, out var constraint))
                {
                    continue;
                }

                ordered.Add(new KeyValuePair<string, string>(name, constraint));
                seen.Add(name);
            }

            var appended = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, constraint) in merged)
            {
                var normalized = ComposerExtraRequireGraph.NormalizeName(name);
                if (!seen.Contains(normalized))
                {
                    appended[normalized] = constraint;
                }
            }

            foreach (var pair in appended)
            {
                ordered.Add(pair);
            }

            return ordered;
        }

        private bool TryInvokeComposerUpdate(
            Project project,
            IReadOnlyList<string> packages,
            DiagnosticBag diagnostics,
            string composerJsonPath,
            string help)
        {
            var workingDirectory = project.GetProjectPath();
            var composerArgs = new List<string> { "update" };
            composerArgs.AddRange(packages);

            string composerExecutable;
            if (this.RunComposerProcess is not null)
            {
                composerExecutable = this.ResolveComposer?.Invoke(workingDirectory) ?? "composer";
            }
            else if (this.ResolveComposer is not null)
            {
                composerExecutable = this.ResolveComposer(workingDirectory) ?? "";
            }
            else
            {
                ExternalToolLocator.TryResolveComposerExecutable(workingDirectory, out composerExecutable);
            }

            if (string.IsNullOrWhiteSpace(composerExecutable))
            {
                Message.Error("CLI_ComposerActionNotFound");
                diagnostics.Add(
                    Diagnostic.Error(
                            MessageCode.ComposerRootJsonWriteFailed,
                            composerJsonPath,
                            0,
                            0,
                            [composerJsonPath, Message.Localize("CLI_ComposerActionNotFound")])
                        .WithHelp(help));
                return false;
            }

            var exit = this.RunComposerProcess is not null
                ? this.RunComposerProcess(composerExecutable, composerArgs, workingDirectory)
                : InvokeComposerProcess(composerExecutable, composerArgs, workingDirectory);

            if (exit != 0)
            {
                diagnostics.Add(
                    Diagnostic.Error(
                            MessageCode.ComposerRootJsonWriteFailed,
                            composerJsonPath,
                            0,
                            0,
                            [composerJsonPath, Message.Localize("CLI_ComposerProxyFailed", exit.ToString())])
                        .WithHelp(help));
                return false;
            }

            return true;
        }

        internal static int InvokeComposerProcess(
            string composerExecutable,
            IReadOnlyList<string> composerArgs,
            string workingDirectory)
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
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

            foreach (var arg in composerArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }

            try
            {
                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process is null)
                {
                    return 1;
                }

                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Message.Error("CLI_ComposerProxyFailed", ex.Message);
                return 1;
            }
        }
    }
}
