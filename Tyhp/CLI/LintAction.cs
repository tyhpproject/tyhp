using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Lint;

namespace Tyhp.CLI
{
    /// <summary>
    /// Implements the lint action for the Tyhp compiler.
    /// Pipeline: parse → bind → check → report (no emit or write).
    /// Optionally applies auto-fixes when <c>--fix</c> is set.
    /// </summary>
    public class LintAction : ActionRunnerBase
    {
        private readonly Tyhp.Config.Project _project;
        private readonly IDiagnosticFormatter _formatter;
        private readonly bool _machineReadable;
        private readonly bool _quiet;

        protected CancellationToken? CancelToken { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="LintAction"/> class.
        /// </summary>
        /// <param name="project">The project configuration containing source file paths and options.</param>
        /// <param name="formatter">Formatter selected from <c>--format</c> / <c>lint.format</c>.</param>
        public LintAction(Tyhp.Config.Project project, IDiagnosticFormatter formatter)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
            this._formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            this._machineReadable = UsesMachineReadableOutput(project.LintFormat);
            this._quiet = project.BeQuiet;
        }

        /// <summary>
        /// Creates the diagnostic formatter for the given lint output format.
        /// Unknown values fall back to the console (text) formatter.
        /// </summary>
        /// <param name="format">Lint format name (<c>text</c>, <c>json</c>, <c>sarif</c>).</param>
        /// <param name="quiet">When true, text output skips rustc-style source underlines.</param>
        public static IDiagnosticFormatter CreateFormatter(string? format, bool quiet = false)
        {
            var normalized = String.IsNullOrWhiteSpace(format)
                ? "text"
                : format.Trim().ToLowerInvariant();

            return normalized switch
            {
                "json" => new JsonDiagnosticFormatter(),
                "sarif" => new SarifDiagnosticFormatter(),
                _ => new ConsoleDiagnosticFormatter(quiet),
            };
        }

        /// <summary>
        /// Returns whether the given lint format emits machine-readable output on stdout
        /// (banner must be suppressed; progress goes to stderr).
        /// </summary>
        public static bool UsesMachineReadableOutput(string? format)
            => String.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
                || String.Equals(format, "sarif", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Executes the lint pipeline through parse, bind, and check — skipping emit/write.
        /// </summary>
        /// <param name="cancellationToken">Token for canceling the lint operation.</param>
        /// <returns>A CompilationResult containing all diagnostics and parsed files.</returns>
        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            this.CancelToken = cancellationToken;

            this.LogProgress("CLI_StartingLint");

            // Step 1: Validate lint-specific configuration (--format / --file)
            var configDiagnostics = new DiagnosticBag();
            if (!this._project.ValidateLintConfig(configDiagnostics))
            {
                var invalidConfigResult = new CompilationResult();
                invalidConfigResult.Diagnostics.AddRange(configDiagnostics);
                this.DisplayDiagnostics(invalidConfigResult);
                return invalidConfigResult;
            }

            // Step 2: Discover source files from --file, explicit paths, or project configuration
            List<string> sourceFiles;
            var discoveryDiagnostics = new DiagnosticBag();
            try
            {
                if (!String.IsNullOrWhiteSpace(this._project.LintFile))
                {
                    // Simple --file mode: lint only the validated single file.
                    // PLACEHOLDER_STORY_12: Full single-file mode with cross-file resolution
                    // (parse all project files for symbol resolution, check only the target file).
                    sourceFiles = [this._project.LintFile];
                }
                else if (this._project.ExplicitPaths.Count > 0)
                {
                    sourceFiles = Tyhp.Config.SourceFileDiscovery.FromExplicitPaths(
                        this._project.ExplicitPaths,
                        discoveryDiagnostics).ToList();
                }
                else
                {
                    sourceFiles = this._project.GetProjectSourceFiles().ToList();
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                var errorResult = new CompilationResult();
                errorResult.Diagnostics.AddError(
                    MessageCode.LintAccessDenied,
                    "",
                    0,
                    0,
                    ex.Message);
                this.LogProgressError("CLI_AccessDeniedDiscoveringSourceFiles", ex.Message);
                this.DisplayDiagnostics(errorResult);
                return errorResult;
            }
            catch (IOException ex)
            {
                var errorResult = new CompilationResult();
                errorResult.Diagnostics.AddError(
                    MessageCode.LintIoError,
                    "",
                    0,
                    0,
                    ex.Message);
                this.LogProgressError("CLI_IoErrorDiscoveringSourceFiles", ex.Message);
                this.DisplayDiagnostics(errorResult);
                return errorResult;
            }
            catch (Exception ex)
            {
                var errorResult = new CompilationResult();
                errorResult.Diagnostics.AddError(
                    MessageCode.LintUnexpectedError,
                    "",
                    0,
                    0,
                    ex.GetType().Name,
                    ex.Message);
                this.LogProgressError("CLI_UnexpectedErrorDiscoveringSourceFiles", ex.Message);
                this.DisplayDiagnostics(errorResult);
                return errorResult;
            }

            if (sourceFiles.Count == 0)
            {
                var emptyResult = new CompilationResult();
                emptyResult.Diagnostics.AddRange(discoveryDiagnostics);

                if (this._project.ExplicitPaths.Count > 0)
                {
                    // Explicit paths resolved to nothing — treat as a warning (exit CompileWarning).
                    this.LogProgressWarn("CLI_NoLintableSourceFilesExplicit");
                    emptyResult.Diagnostics.AddWarning(
                        MessageCode.LintNoSourceFiles,
                        "",
                        0,
                        0);
                }
                else
                {
                    // Empty project include set — info only (exit Success per Phase 5).
                    this.LogProgress("CLI_NoSourceFilesCheckConfig");
                    emptyResult.Diagnostics.AddInfo(
                        MessageCode.LintNoSourceFiles,
                        "",
                        0,
                        0);
                }

                this.DisplayDiagnostics(emptyResult);
                this.DisplayTextSummary(emptyResult, filesChecked: 0);
                return emptyResult;
            }

            if (this._project.ExplicitPaths.Count > 0)
            {
                this.LogProgressSuccess("CLI_FoundFilesToLintExplicit", sourceFiles.Count);
            }
            else if (!String.IsNullOrWhiteSpace(this._project.LintFile))
            {
                this.LogProgressSuccess(
                    "CLI_FoundSingleFileToLint",
                    this.GetLintFileDisplayPath());
            }
            else
            {
                this.LogProgressSuccess("CLI_FoundSourceFilesToLint", sourceFiles.Count);
            }

            // Step 3: Create CompilationOptions from project config
            var compilationOptions = CompilationOptions.FromProject(this._project, options =>
            {
                options.MaxThreads = -1;
                // EnableAstCache is derived from the project (--no-cache) in FromProject.
                options.ReportAmbiguities = false;
                options.EnableProfiling = false;
                options.GarbageCollectInterval = 1000;
                options.PreReadThreshold = 1024L * 1024L * 1024L;
                options.PreReadMinFiles = 1000;
            });

            // Step 4: Parse (+ bind + check) via CompilationService
            this.LogProgress("CLI_ParsingSourceFiles");
            CompilationResult result;

            try
            {
                result = this.RunPipeline(sourceFiles, compilationOptions, discoveryDiagnostics, cancellationToken);

                // CompilationService stops mid-parse on cancel without throwing — surface partial results.
                if (cancellationToken.IsCancellationRequested || result.WasCancelled)
                {
                    return this.HandleCancellation(result);
                }
            }
            catch (OperationCanceledException)
            {
                result = new CompilationResult
                {
                    SourceFileCount = sourceFiles.Count,
                    LintTargetFile = String.IsNullOrWhiteSpace(this._project.LintFile)
                        ? null
                        : this.GetLintFileDisplayPath(),
                };
                result.Diagnostics.AddRange(discoveryDiagnostics);
                return this.HandleCancellation(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                this.LogProgressError("CLI_AccessDeniedDuringParsing", ex.Message);
                result = new CompilationResult
                {
                    SourceFileCount = sourceFiles.Count,
                };
                result.Diagnostics.AddError(
                    MessageCode.LintAccessDenied,
                    "",
                    0,
                    0,
                    ex.Message);
                this.DisplayDiagnostics(result);
                return result;
            }
            catch (IOException ex)
            {
                this.LogProgressError("CLI_IoErrorDuringParsing", ex.Message);
                result = new CompilationResult
                {
                    SourceFileCount = sourceFiles.Count,
                };
                result.Diagnostics.AddError(
                    MessageCode.LintIoError,
                    "",
                    0,
                    0,
                    ex.Message);
                this.DisplayDiagnostics(result);
                return result;
            }
            catch (Exception ex)
            {
                this.LogProgressError("CLI_UnexpectedErrorDuringParsing", ex.Message);
                result = new CompilationResult
                {
                    SourceFileCount = sourceFiles.Count,
                };
                result.Diagnostics.AddError(
                    MessageCode.LintUnexpectedError,
                    "",
                    0,
                    0,
                    ex.GetType().Name,
                    ex.Message);
                this.DisplayDiagnostics(result);
                return result;
            }

            // Step 5: Log parse results (bind/check already ran inside CompilationService unless fatal)
            this.LogProgressSuccess(
                "CLI_ParsedFilesInSeconds",
                result.ParsedFiles?.Count ?? 0,
                result.ParseDuration.TotalSeconds);
            if (result.ParseErrorCount > 0)
            {
                this.LogProgressError("CLI_ParsePhaseCompletedWithErrors", result.ParseErrorCount);
            }

            // Steps 6–9: Binding and checking are integrated in CompilationService.ParseFiles()
            if (!this._machineReadable && !this._quiet)
            {
                DisplayBinderSummary(result);
                DisplayCheckerSummary(result);
            }

            if (result.CheckErrorCount > 0)
            {
                this.LogProgressError("CLI_CheckPhaseCompletedWithErrors", result.CheckErrorCount);
            }

            // Step 9.5: Auto-fix engine when --fix is specified
            if (this._project.LintFix && !cancellationToken.IsCancellationRequested)
            {
                result = this.ApplyAutoFixes(
                    result,
                    sourceFiles,
                    compilationOptions,
                    discoveryDiagnostics,
                    cancellationToken);

                if (cancellationToken.IsCancellationRequested || result.WasCancelled)
                {
                    return this.HandleCancellation(result);
                }
            }

            // Lint mode: skip emit and write steps entirely
            this.LogProgress("CLI_LintEmitSkipped");
            result.EmitDuration = TimeSpan.Zero;

            // Step 10: Display all diagnostics using the selected formatter
            this.DisplayDiagnostics(result);

            // Step 11: Display text summary (JSON/SARIF summaries come from the formatter)
            this.DisplayTextSummary(result, result.SourceFileCount > 0
                ? result.SourceFileCount
                : result.ParsedFiles?.Count ?? 0);

            // Step 12: Exit code is set by TyhpHostedService from CompilationResult.GetExitCode(Strict)
            if (!this._machineReadable && !this._quiet)
            {
                Message.Display("");
                if (result.Diagnostics.HasErrors)
                {
                    Message.Error("CLI_LintFailed", result.Diagnostics.ErrorCount);
                }
                else if (this._project.Build.StrictMode && result.Diagnostics.HasWarnings)
                {
                    Message.Error("CLI_LintFailedStrictMode", result.Diagnostics.WarningCount);
                }
                else if (result.Diagnostics.HasWarnings)
                {
                    Message.Warn("CLI_LintCompletedWithWarnings", result.Diagnostics.WarningCount);
                }
                else if (!String.IsNullOrWhiteSpace(result.LintTargetFile)
                    && result.Diagnostics.InfoCount == 0)
                {
                    // No errors/warnings and no infos → clean file.
                    Message.Success("CLI_LintNoIssuesInFile", result.LintTargetFile);
                }
                else
                {
                    Message.Success("CLI_LintCompletedSuccess");
                }
            }

            return result;
        }

        /// <summary>
        /// Runs the auto-fix engine with loop detection and max-iteration guarding.
        /// Re-runs parse/bind/check after successful file modifications.
        /// </summary>
        private CompilationResult ApplyAutoFixes(
            CompilationResult result,
            List<string> sourceFiles,
            CompilationOptions compilationOptions,
            DiagnosticBag discoveryDiagnostics,
            CancellationToken cancellationToken)
        {
            var engine = LintFixEngine.CreateDefault();
            var previouslyApplied = new HashSet<LintFixLocationKey>();
            var fixDiagnostics = new DiagnosticBag();
            var maxIterations = Math.Max(1, this._project.Checker.MaxFixIterations);
            var totalApplied = 0;
            var totalFailed = 0;
            var iterationsRun = 0;

            for (var iteration = 1; iteration <= maxIterations; iteration++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.WasCancelled = true;
                    break;
                }

                this.LogProgress("CLI_LintFixApplying", iteration, maxIterations);
                iterationsRun = iteration;

                var pass = engine.ApplyFixes(result, previouslyApplied);
                if (pass.LoopDetected)
                {
                    var loc = pass.LoopLocation!.Value;
                    this.LogProgressError(
                        "CLI_LintFixLoopDetected",
                        loc.Code.ToString(),
                        loc.FileName,
                        loc.Line,
                        loc.Column);
                    fixDiagnostics.AddError(
                        MessageCode.LintUnexpectedError,
                        loc.FileName,
                        loc.Line,
                        loc.Column,
                        "AutoFixLoop",
                        Message.Localize(
                            "CLI_LintFixLoopDetected",
                            loc.Code.ToString(),
                            loc.FileName,
                            loc.Line,
                            loc.Column));
                    break;
                }

                if (pass.Applications.Count == 0)
                {
                    if (iteration == 1)
                    {
                        this.LogProgress("CLI_LintNoAutoFixesAvailable");
                    }

                    break;
                }

                var anySucceeded = false;
                foreach (var application in pass.Applications)
                {
                    var diag = application.Diagnostic;
                    var fileName = diag.FileName ?? string.Empty;
                    if (application.Result.Success)
                    {
                        totalApplied++;
                        anySucceeded = true;
                        previouslyApplied.Add(new LintFixLocationKey(
                            fileName,
                            diag.Code,
                            diag.Line,
                            diag.Column));
                        fixDiagnostics.AddInfo(
                            MessageCode.LintFixApplied,
                            fileName,
                            diag.Line,
                            diag.Column,
                            application.Fix.Description);
                    }
                    else
                    {
                        totalFailed++;
                        fixDiagnostics.AddWarning(
                            MessageCode.LintFixFailed,
                            fileName,
                            diag.Line,
                            diag.Column,
                            application.Fix.Description,
                            application.Result.FailureReason ?? String.Empty);
                    }
                }

                if (!anySucceeded)
                {
                    // All applicable fixes failed (e.g. stubs) — no point re-running.
                    break;
                }

                if (iteration == maxIterations)
                {
                    this.LogProgressWarn("CLI_LintFixMaxIterationsReached", maxIterations);
                    break;
                }

                // Re-run parse/bind/check so subsequent iterations see the updated sources.
                this.LogProgress("CLI_ParsingSourceFiles");
                result = this.RunPipeline(
                    sourceFiles,
                    compilationOptions,
                    discoveryDiagnostics,
                    cancellationToken);

                if (cancellationToken.IsCancellationRequested || result.WasCancelled)
                {
                    break;
                }
            }

            if (iterationsRun > 0 && (totalApplied > 0 || totalFailed > 0))
            {
                this.LogProgress("CLI_LintFixSummary", totalApplied, totalFailed, iterationsRun);
            }

            // Attach fix-engine diagnostics to the latest compilation result for display/exit code.
            result.Diagnostics.AddRange(fixDiagnostics);
            return result;
        }

        private CompilationResult RunPipeline(
            List<string> sourceFiles,
            CompilationOptions compilationOptions,
            DiagnosticBag discoveryDiagnostics,
            CancellationToken cancellationToken)
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(sourceFiles, compilationOptions, cancellationToken);
            result.Diagnostics.AddRange(discoveryDiagnostics);
            result.SourceFileCount = sourceFiles.Count;
            if (!String.IsNullOrWhiteSpace(this._project.LintFile))
            {
                result.LintTargetFile = this.GetLintFileDisplayPath();
            }

            return result;
        }

        private CompilationResult HandleCancellation(CompilationResult result)
        {
            result.WasCancelled = true;
            this.LogProgressWarn("CLI_LintCancelled");
            result.Diagnostics.AddError(
                MessageCode.LintCancelled,
                "",
                0,
                0);

            // Display whatever diagnostics were collected so far (partial results).
            this.DisplayDiagnostics(result);
            this.DisplayTextSummary(result, result.SourceFileCount > 0
                ? result.SourceFileCount
                : result.ParsedFiles?.Count ?? 0);
            return result;
        }

        private void DisplayDiagnostics(CompilationResult result)
        {
            // Fold deferred tyhp.json warnings into this run's bag so text/JSON/SARIF include them
            // and Message never wrote them to stdout during Project construction.
            this._project.TransferPendingConfigWarningsTo(result.Diagnostics);

            // Machine-readable formatters (JSON / SARIF) need timings and file counts for summaries.
            this._formatter.SetContext(result);

            // Quiet text mode: emit diagnostics only — skip the formatter's count summary
            // ("N warning(s)") which is non-diagnostic output covered by --quiet.
            if (this._quiet && !this._machineReadable)
            {
                foreach (var diagnostic in result.Diagnostics.All)
                {
                    this._formatter.Format(diagnostic);
                }

                return;
            }

            // DisplayAll also invokes FormatSummary on the formatter.
            result.Diagnostics.DisplayAll(this._formatter);
        }

        private void DisplayTextSummary(CompilationResult result, int filesChecked)
        {
            if (this._machineReadable || this._quiet)
            {
                return;
            }

            Message.Display("");
            if (!String.IsNullOrWhiteSpace(result.LintTargetFile))
            {
                Message.Display(
                    "CLI_LintCompleteSummarySingleFile",
                    filesChecked,
                    result.LintTargetFile,
                    result.Diagnostics.ErrorCount,
                    result.Diagnostics.WarningCount,
                    result.Diagnostics.InfoCount,
                    result.ParseDuration.TotalSeconds,
                    result.BindDuration.TotalSeconds,
                    result.CheckDuration.TotalSeconds);
            }
            else
            {
                Message.Display(
                    "CLI_LintCompleteSummary",
                    filesChecked,
                    result.Diagnostics.ErrorCount,
                    result.Diagnostics.WarningCount,
                    result.Diagnostics.InfoCount,
                    result.ParseDuration.TotalSeconds,
                    result.BindDuration.TotalSeconds,
                    result.CheckDuration.TotalSeconds);
            }
        }

        /// <summary>
        /// Display path for <c>--file</c> mode: project-root-relative when possible.
        /// </summary>
        private string GetLintFileDisplayPath()
        {
            var file = this._project.LintFile;
            if (String.IsNullOrWhiteSpace(file))
            {
                return string.Empty;
            }

            try
            {
                var root = Tyhp.Config.PathCanonicalizer.GetCanonicalFullPath(this._project.GetProjectPath());
                var absoluteFile = Tyhp.Config.PathCanonicalizer.GetCanonicalFullPath(file);
                var relative = Path.GetRelativePath(root, absoluteFile);
                if (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
                    && relative != "..")
                {
                    return relative.Replace('\\', '/');
                }
            }
            catch (Exception)
            {
                // Fall through to the absolute path.
            }

            return file.Replace('\\', '/');
        }

        private void LogProgress(string key, params object[] args)
        {
            if (this._quiet)
            {
                return;
            }

            if (this._machineReadable)
            {
                Console.Error.WriteLine(Message.Localize(key, args));
                return;
            }

            Message.Info(key, args);
        }

        private void LogProgressSuccess(string key, params object[] args)
        {
            if (this._quiet)
            {
                return;
            }

            if (this._machineReadable)
            {
                Console.Error.WriteLine(Message.Localize(key, args));
                return;
            }

            Message.Success(key, args);
        }

        private void LogProgressWarn(string key, params object[] args)
        {
            if (this._quiet)
            {
                return;
            }

            if (this._machineReadable)
            {
                Console.Error.WriteLine(Message.Localize(key, args));
                return;
            }

            Message.Warn(key, args);
        }

        private void LogProgressError(string key, params object[] args)
        {
            if (this._quiet)
            {
                return;
            }

            if (this._machineReadable)
            {
                Console.Error.WriteLine(Message.Localize(key, args));
                return;
            }

            Message.Error(key, args);
        }

        /// <summary>
        /// Disposes of managed resources.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }
    }
}
