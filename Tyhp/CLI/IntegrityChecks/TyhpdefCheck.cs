using Microsoft.Extensions.FileSystemGlobbing;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder.BuiltIn;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.CLI.IntegrityChecks
{
    /// <summary>
    /// Discovers and parses project tyhpdef files.
    /// </summary>
    public sealed class TyhpdefCheck : IIntegrityCheck
    {
        private readonly Project _project;

        public TyhpdefCheck(Project project)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public string Name => Message.Localize("CLI_IntegrityCheckNameTyhpdef");

        public Task<IntegrityCheckResult> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var details = new List<string>();
            var projectPath = this._project.GetProjectPath();

            // Without a project config and without explicit tyhpdef includes, do not walk the
            // entire cwd (e.g. `/tmp`) looking for `**/*.tyhpdef`.
            if (this._project.TyhpdefIncludePaths.Count == 0
                && !this._project.HasConfigFile()
                && !this._project.IncludePaths.Any(static p =>
                    p.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)))
            {
                details.Add(Message.Localize("CLI_IntegrityTyhpdefNoneFound"));
                return Task.FromResult(IntegrityCheckResult.Pass(
                    Message.Localize("CLI_IntegrityTyhpdefNoneOk"),
                    details));
            }

            List<string> paths;
            try
            {
                paths = DiscoverTyhpdefFiles(projectPath, this._project).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
            {
                return Task.FromResult(IntegrityCheckResult.Fail(
                    Message.Localize("CLI_IntegrityTyhpdefDiscoverFailed", ex.Message),
                    details));
            }

            if (paths.Count == 0)
            {
                details.Add(Message.Localize("CLI_IntegrityTyhpdefNoneFound"));
                return Task.FromResult(IntegrityCheckResult.Pass(
                    Message.Localize("CLI_IntegrityTyhpdefNoneOk"),
                    details));
            }

            var parsedOk = 0;
            var errorCount = 0;
            var problems = new List<string>();
            var tagless = this._project.Tagless;

            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();

                string content;
                try
                {
                    content = File.ReadAllText(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errorCount++;
                    problems.Add(Message.Localize("CLI_IntegrityTyhpdefReadFailed", path, ex.Message));
                    continue;
                }

                // A bag per file keeps each file's diagnostics separable for reporting.
                var diagnostics = new DiagnosticBag();
                try
                {
                    var ast = Tyhpdef.ParseContent(
                        content,
                        path,
                        ParseMode.Tyhpdef,
                        diagnostics,
                        tagless);

                    if (ast == null || diagnostics.HasErrors)
                    {
                        errorCount++;
                        problems.Add(Message.Localize("CLI_IntegrityTyhpdefParseFailed", path));
                        foreach (var diagnostic in diagnostics.Errors)
                        {
                            problems.Add(Message.Localize(
                                "CLI_IntegrityTyhpdefParseErrorDetail",
                                diagnostic.Line,
                                diagnostic.Column,
                                (int)diagnostic.Code,
                                diagnostic.Message));
                        }
                    }
                    else
                    {
                        parsedOk++;
                        details.Add(Message.Localize("CLI_IntegrityTyhpdefParseOk", path));
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                {
                    errorCount++;
                    problems.Add(Message.Localize("CLI_IntegrityTyhpdefParseException", path, ex.Message));
                }
            }

            // PLACEHOLDER_STORY_02: Check for duplicate declarations across tyhpdef files (requires binder)

            details.Insert(0, Message.Localize(
                "CLI_IntegrityTyhpdefSummary",
                paths.Count,
                parsedOk,
                errorCount));

            if (errorCount > 0)
            {
                return Task.FromResult(IntegrityCheckResult.Fail(
                    Message.Localize("CLI_IntegrityTyhpdefFailed", errorCount, paths.Count),
                    details,
                    problems));
            }

            return Task.FromResult(IntegrityCheckResult.Pass(
                Message.Localize("CLI_IntegrityTyhpdefPassed", parsedOk),
                details));
        }

        private static IEnumerable<string> DiscoverTyhpdefFiles(string projectPath, Project project)
        {
            var includePatterns = project.TyhpdefIncludePaths.Count > 0
                ? project.TyhpdefIncludePaths
                : (IReadOnlyList<string>)["**/*.tyhpdef"];

            var fromInclude = project.IncludePaths
                .Where(static p => p.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase));

            var matcher = new Matcher();
            matcher.AddIncludePatterns(includePatterns.Concat(fromInclude));
            if (project.TyhpdefExcludePaths.Count > 0)
            {
                matcher.AddExcludePatterns(project.TyhpdefExcludePaths);
            }

            if (project.ExcludePaths.Count > 0)
            {
                matcher.AddExcludePatterns(project.ExcludePaths);
            }

            return matcher.GetResultsInFullPath(projectPath)
                .Where(static p => p.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        }
    }
}
