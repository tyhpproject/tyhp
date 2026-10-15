using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;

namespace Tyhp.CLI.IntegrityChecks
{
    /// <summary>
    /// Validates <c>tyhp.json</c> and related project configuration.
    /// </summary>
    public sealed class ConfigurationCheck : IIntegrityCheck
    {
        private readonly Project _project;

        public ConfigurationCheck(Project project)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public string Name => Message.Localize("CLI_IntegrityCheckNameConfiguration");

        public Task<IntegrityCheckResult> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var details = new List<string>();
            var errors = new List<string>();
            var projectPath = this._project.GetProjectPath();
            var tyhpJsonPath = Path.Combine(projectPath, "tyhp.json");

            if (!this._project.HasConfigFile() && !File.Exists(tyhpJsonPath))
            {
                return Task.FromResult(IntegrityCheckResult.Fail(
                    Message.Localize("CLI_IntegrityConfigMissing"),
                    details,
                    [Message.Localize("CLI_IntegrityConfigMissingDetail", tyhpJsonPath)],
                    isWarning: true));
            }

            var configPath = this._project.HasConfigFile()
                ? (this._project.GetConfigValue("*project_file_path") ?? tyhpJsonPath)
                : tyhpJsonPath;

            if (File.Exists(configPath))
            {
                try
                {
                    var json = File.ReadAllText(configPath);
                    using var _ = JsonDocument.Parse(json);
                    details.Add(Message.Localize("CLI_IntegrityConfigJsonOk", configPath));
                }
                catch (JsonException ex)
                {
                    errors.Add(Message.Localize("CLI_IntegrityConfigJsonInvalid", configPath, ex.Message));
                }
                catch (IOException ex)
                {
                    errors.Add(Message.Localize("CLI_IntegrityConfigReadFailed", configPath, ex.Message));
                }
            }

            ct.ThrowIfCancellationRequested();

            if (this._project.IncludePaths.Count == 0)
            {
                errors.Add(Message.Localize("CLI_IntegrityConfigIncludeEmpty"));
            }
            else
            {
                details.Add(Message.Localize(
                    "CLI_IntegrityConfigIncludeCount",
                    this._project.IncludePaths.Count));
            }

            ValidateGlobPatterns(this._project.IncludePaths, "include", errors, details);
            ValidateGlobPatterns(this._project.ExcludePaths, "exclude", errors, details);
            foreach (var entry in this._project.Output.PublishContent)
            {
                ValidateGlobPatterns(
                    entry.Src.Where(PublishContentService.IsGlobPattern),
                    "output.publishContent.src",
                    errors,
                    details);
                ValidateGlobPatterns(entry.Exclude, "output.publishContent.exclude", errors, details);
            }

            var outputPath = this._project.Output.Path;
            if (string.IsNullOrWhiteSpace(outputPath)
                || outputPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                errors.Add(Message.Localize("CLI_IntegrityConfigOutputPathInvalid", outputPath ?? ""));
            }
            else
            {
                details.Add(Message.Localize("CLI_IntegrityConfigOutputPath", outputPath));
            }

            var configuredPhpVersion = this._project.GetConfigValue("output:phpVersion")
                ?? this._project.GetConfigValue("phpVersion");
            if (!string.IsNullOrWhiteSpace(configuredPhpVersion)
                && !OutputConfig.IsSupportedPhpVersion(configuredPhpVersion))
            {
                errors.Add(Message.Localize(
                    "CLI_IntegrityConfigPhpVersionInvalid",
                    configuredPhpVersion,
                    string.Join(", ", OutputConfig.SupportedPhpVersionNames)));
            }
            else
            {
                details.Add(Message.Localize(
                    "CLI_IntegrityConfigPhpVersion",
                    configuredPhpVersion ?? this._project.Output.PhpVersion));
            }

            ct.ThrowIfCancellationRequested();

            if (this._project.IncludePaths.Count > 0)
            {
                try
                {
                    var sources = this._project.GetProjectSourceFiles().ToList();
                    if (sources.Count == 0)
                    {
                        errors.Add(Message.Localize("CLI_IntegrityConfigNoSourceFiles"));
                    }
                    else
                    {
                        details.Add(Message.Localize("CLI_IntegrityConfigSourceFileCount", sources.Count));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    errors.Add(Message.Localize("CLI_IntegrityConfigSourceScanFailed", ex.Message));
                }
            }

            if (errors.Count > 0)
            {
                return Task.FromResult(IntegrityCheckResult.Fail(
                    Message.Localize("CLI_IntegrityConfigFailed", errors.Count),
                    details,
                    errors));
            }

            return Task.FromResult(IntegrityCheckResult.Pass(
                Message.Localize("CLI_IntegrityConfigPassed"),
                details));
        }

        private static void ValidateGlobPatterns(
            IEnumerable<string> patterns,
            string kind,
            List<string> errors,
            List<string> details)
        {
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    errors.Add(Message.Localize("CLI_IntegrityConfigGlobEmpty", kind));
                    continue;
                }

                try
                {
                    var matcher = new Matcher();
                    matcher.AddInclude(pattern);
                    details.Add(Message.Localize("CLI_IntegrityConfigGlobOk", kind, pattern));
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    errors.Add(Message.Localize("CLI_IntegrityConfigGlobInvalid", kind, pattern, ex.Message));
                }
            }
        }
    }
}
