using System.Diagnostics;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.CLI
{
    /// <summary>
    /// Implements the build action for the Tyhp compiler.
    /// Full pipeline: parse → bind → check → (tyhpdef) → optimize → emit → write.
    /// </summary>
    public class BuildAction : ActionRunnerBase
    {
        private readonly Project _project;
        protected CancellationToken? CancelToken { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BuildAction"/> class.
        /// </summary>
        /// <param name="project">The project configuration containing source file paths and options.</param>
        public BuildAction(Project project)
        {
            this._project = project;
        }

        /// <summary>
        /// Executes the full build pipeline.
        /// </summary>
        /// <param name="cancellationToken">Token for canceling the build operation.</param>
        /// <returns>A CompilationResult containing all diagnostics and parsed files.</returns>
        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            this.CancelToken = cancellationToken;

            Message.Info("CLI_StartingBuild");

            if (this._project.Build.Watch)
            {
                // PLACEHOLDER_STORY_19: File watcher for --watch mode
                Message.Info("CLI_WatchModeNotImplemented");
            }

            if (!this._project.HasConfigFile())
            {
                Message.Info("CLI_NoTyhpJsonUsingDefaults");
            }

            // Step 1: Configuration validation and setup
            var result = new CompilationResult();
            if (!this.ValidateOutputPath(result))
            {
                this.DisplaySummary(result, filesWritten: 0, directoriesCreated: 0);
                return result;
            }

            if (this._project.Build.CleanBeforeBuild)
            {
                Message.Info("CLI_CleaningOutputDirectory");
                if (!BuildOutputCleaner.TryClean(this._project, result.Diagnostics))
                {
                    Message.Error("CLI_CleanOutputFailed");
                    this.DisplaySummary(result, filesWritten: 0, directoriesCreated: 0);
                    return result;
                }

                Message.Success("CLI_CleanOutputComplete");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return this.HandleCancellation(result);
            }

            // Step 2: File discovery
            List<string> sourceFiles;
            try
            {
                sourceFiles = this._project.GetProjectSourceFiles().ToList();
            }
            catch (UnauthorizedAccessException ex)
            {
                return this.HandleDiscoveryFailure(result, MessageCode.ParserCompileAborted, "CLI_AccessDeniedDiscoveringSourceFiles", ex.Message);
            }
            catch (IOException ex)
            {
                return this.HandleDiscoveryFailure(result, MessageCode.ParserCompileAborted, "CLI_IoErrorDiscoveringSourceFiles", ex.Message);
            }
            catch (Exception ex)
            {
                return this.HandleDiscoveryFailure(result, MessageCode.ParserCompileAborted, "CLI_UnexpectedErrorDiscoveringSourceFiles", ex.Message);
            }

            if (sourceFiles.Count == 0)
            {
                Message.Info("CLI_NoSourceFilesCheckConfig");
                result.Diagnostics.AddInfo(
                    MessageCode.BuildNoSourceFiles,
                    "",
                    0,
                    0);
                this.DisplaySummary(result, filesWritten: 0, directoriesCreated: 0);
                return result;
            }

            Message.Success("CLI_FoundSourceFilesToCompile", sourceFiles.Count);
            result.SourceFileCount = sourceFiles.Count;
            this.LogVerbose("CLI_VerboseSourceFileDiscovery", sourceFiles.Count, ComputeTotalFileSize(sourceFiles));

            // Incremental build: skip when nothing changed since last successful build
            var incrementalService = new IncrementalBuildService();
            var buildStatePath = IncrementalBuildService.GetBuildStatePath(this._project);
            var previousState = incrementalService.LoadBuildState(buildStatePath);
            var fileChanges = incrementalService.DetermineChangedFiles(sourceFiles, previousState);

            if (!fileChanges.HasChanges
                && incrementalService.IsStateValid(previousState, this._project)
                && incrementalService.AllOutputFilesExist(previousState)
                && !this._project.Build.CleanBeforeBuild
                && !this._project.Build.DryRun)
            {
                Message.Info("CLI_NothingToBuild");
                result.IncrementalBuildSkipped = true;
                this.DisplaySummary(result, filesWritten: 0, directoriesCreated: 0);
                return result;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return this.HandleCancellation(result);
            }

            // Step 3: Parse source files (binding and checking are integrated in CompilationService)
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

            Message.Info("CLI_ParsingSourceFiles");
            var compilationService = new CompilationService();

            try
            {
                result = compilationService.ParseFiles(sourceFiles, compilationOptions, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return this.HandleCancellation(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return this.HandleParseFailure(result, "CLI_AccessDeniedDuringParsing", ex.Message);
            }
            catch (IOException ex)
            {
                return this.HandleParseFailure(result, "CLI_IoErrorDuringParsing", ex.Message);
            }
            catch (Exception ex)
            {
                return this.HandleParseFailure(result, "CLI_UnexpectedErrorDuringParsing", ex.Message);
            }
            finally
            {
                compilationService.Dispose();
            }

            // ParseFiles returns a fresh CompilationResult/DiagnosticBag, so pending config
            // warnings must be folded in here (not before) or the reassignment above would
            // silently drop them. Doing it now — rather than only in DisplaySummary — lets
            // ShouldContinueToEmit see them for the --strict emit-skip gate below.
            this._project.TransferPendingConfigWarningsTo(result.Diagnostics);

            Message.Success(
                "CLI_ParsedFilesInSeconds",
                result.ParsedFiles?.Count ?? 0,
                result.ParseDuration.TotalSeconds);
            this.LogVerbose(
                "CLI_VerboseParseSummary",
                result.ParsedFiles?.Count ?? 0,
                result.ParseErrorCount,
                result.ParseDuration.TotalSeconds);

            if (this._project.Build.Verbose && (result.AstCacheHits > 0 || result.AstCacheMisses > 0))
            {
                var parsedCount = result.AstCacheMisses;
                var cachedCount = result.AstCacheHits;
                var totalCount = parsedCount + cachedCount;
                Message.Display("CLI_VerboseAstCacheStats", parsedCount, totalCount, cachedCount);
            }

            if (result.ParseErrorCount > 0)
            {
                Message.Error("CLI_ParsePhaseCompletedWithErrors", result.ParseErrorCount);
            }

            // Step 4: Binding (handled by CompilationService)
            DisplayBinderSummary(result);
            this.LogVerboseBinderSummary(result);

            // Step 5: Checking (handled by CompilationService)
            DisplayCheckerSummary(result);
            this.LogVerboseCheckerSummary(result);

            if (result.CheckErrorCount > 0)
            {
                Message.Error("CLI_CheckPhaseCompletedWithErrors", result.CheckErrorCount);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return this.HandleCancellation(result);
            }

            // Step 5.5: Library entrypoint validation. Skip when earlier phases already reported errors:
            // partially-broken ASTs should not be driven through emit-tree construction.
            if (!result.Diagnostics.HasErrors && result.ParsedFiles is { Count: > 0 } parsedFiles)
            {
                BuildEntryPointValidator.ValidateLibraryProject(this._project, parsedFiles, result.Diagnostics);
            }

            // Step 6: Error gate — decide whether to continue to emission
            WriteResult? writeResult = null;
            EmitContext? emitContext = null;
            if (!this.ShouldContinueToEmit(result))
            {
                if (this._project.Build.StrictMode && result.Diagnostics.HasWarnings && !result.Diagnostics.HasErrors)
                {
                    Message.Info("CLI_StrictModeSkippingEmit");
                }
                else
                {
                    Message.Info("CLI_EmitterSkippedDueToErrors");
                }

                result.EmitDuration = TimeSpan.Zero;
            }
            else
            {
                // Step 6.5: Tyhpdef Track C (Story 20)
                if (this._project.Build.GenerateTyhpdef == true)
                {
                    // PLACEHOLDER_STORY_20: Generate tyhpdef for compiled code (Track C)
                }

                // Step 7: Optimizer (Story 23) — no-op until TyhpOptimizer is available
                result.OptimizeDuration = TimeSpan.Zero;

                if (cancellationToken.IsCancellationRequested)
                {
                    return this.HandleCancellation(result);
                }

                // Step 8: Run emitter
                if (result.GlobalScope == null || result.ParsedFiles == null)
                {
                    Message.Info("CLI_EmitterSkippedDueToErrors");
                    result.EmitDuration = TimeSpan.Zero;
                }
                else
                {
                    Message.Info("CLI_RunningEmitter");
                    var emitStopwatch = Stopwatch.StartNew();
                    emitContext = EmitContext.Create(
                        result.GlobalScope,
                        result.Diagnostics,
                        this._project,
                        result.RequiresRuntimeGenericTracking,
                        result.RequiresWeakReferenceCapture,
                        result.RequiresDisposableTryFinally,
                        result.AsyncForeachKinds,
                        result.RequiresGenericVariant,
                        result.GenericCallTargets,
                        result.InferredClosureSignatures,
                        result.ExpressionTypes);
                    var emitter = new TyhpEmitter(emitContext);
                    result.OutputFiles = emitter.Emit(result.ParsedFiles);
                    emitStopwatch.Stop();
                    result.EmitDuration = emitStopwatch.Elapsed;

                    var outputFiles = result.OutputFiles;
                    var totalBytes = outputFiles.Sum(f => f.GeneratedContent?.Length ?? 0);
                    Message.Success(
                        "CLI_EmitPhaseCompleted",
                        result.EmitDuration.TotalSeconds,
                        outputFiles.Count,
                        totalBytes);
                    Message.Display("CLI_EmitOutputDirectory", emitContext.Config.OutputPath.TrimEnd('/'));
                    this.LogVerbose("CLI_VerboseEmitSummary", outputFiles.Count, totalBytes);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return this.HandleCancellation(result);
                }

                // Step 9: Write output files
                if (result.OutputFiles is { Count: > 0 } outputFilesToWrite && emitContext != null)
                {
                    if (this._project.Build.DryRun)
                    {
                        Message.Info("CLI_DryRunSkippingWrite");
                    }
                    else
                    {
                        Message.Info("CLI_WritingOutputFiles");
                    }

                    var writer = new OutputWriterService(this._project, result.Diagnostics, emitContext);
                    writeResult = writer.WriteAll(outputFilesToWrite, dryRun: this._project.Build.DryRun);

                    if (!this._project.Build.DryRun)
                    {
                        Message.Success(
                            "CLI_OutputWriteSummary",
                            writeResult.FilesWritten,
                            writeResult.DirectoriesCreated);
                        this.LogVerboseWrittenFiles(writeResult);
                    }

                    if (this._project.Build.UpdateComposer)
                    {
                        var outputDir = BuildOutputCleaner.ResolveOutputDirectory(
                            Path.GetFullPath(this._project.GetProjectPath()),
                            this._project.Output.Path);
                        new ComposerJsonService(result.Diagnostics).GenerateOrUpdate(
                            outputDir,
                            this._project,
                            outputFilesToWrite,
                            dryRun: this._project.Build.DryRun,
                            emitContext: emitContext);
                    }

                    if (!this._project.Build.DryRun
                        && !result.Diagnostics.HasErrors
                        && !(this._project.Build.StrictMode && result.Diagnostics.HasWarnings))
                    {
                        var outputDir = BuildOutputCleaner.ResolveOutputDirectory(
                            Path.GetFullPath(this._project.GetProjectPath()),
                            this._project.Output.Path);
                        new TyhpLibDistributionService(result.Diagnostics).AddRuntimePackageDependencies(
                            outputDir,
                            this._project,
                            outputFilesToWrite,
                            emitContext,
                            dryRun: false);
                    }
                    else if (this._project.Build.DryRun && emitContext != null)
                    {
                        new TyhpLibDistributionService(result.Diagnostics).AddRuntimePackageDependencies(
                            BuildOutputCleaner.ResolveOutputDirectory(
                                Path.GetFullPath(this._project.GetProjectPath()),
                                this._project.Output.Path),
                            this._project,
                            outputFilesToWrite,
                            emitContext,
                            dryRun: true);
                    }

                    if (!this._project.Build.DryRun
                        && !result.Diagnostics.HasErrors
                        && !(this._project.Build.StrictMode && result.Diagnostics.HasWarnings))
                    {
                        try
                        {
                            incrementalService.SaveBuildState(
                                buildStatePath,
                                sourceFiles,
                                this._project,
                                writeResult.WrittenPaths);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            result.Diagnostics.AddWarning(
                                MessageCode.BuildFileWriteError,
                                "",
                                0,
                                0,
                                buildStatePath,
                                ex.Message);
                        }
                    }
                }
            }

            if (result.Diagnostics.HasErrors)
            {
                Message.Info("CLI_OutputNotWrittenDueToErrors");
            }

            // Step 10: Display summary
            this.DisplaySummary(
                result,
                writeResult?.FilesWritten ?? 0,
                writeResult?.DirectoriesCreated ?? 0,
                dryRun: this._project.Build.DryRun && writeResult != null);

            return result;
        }

        private bool ShouldContinueToEmit(CompilationResult result)
        {
            if (result.Diagnostics.HasErrors)
            {
                return false;
            }

            if (this._project.Build.StrictMode && result.Diagnostics.HasWarnings)
            {
                return false;
            }

            return true;
        }

        private bool ValidateOutputPath(CompilationResult result)
        {
            var projectPath = Path.GetFullPath(this._project.GetProjectPath());
            var outputPath = BuildOutputCleaner.ResolveOutputDirectory(projectPath, this._project.Output.Path);
            this.LogVerbose("CLI_VerboseOutputPath", outputPath);

            try
            {
                Directory.CreateDirectory(outputPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Diagnostics.AddError(
                    MessageCode.ConfigOutputPathNotWritable,
                    "",
                    0,
                    0,
                    outputPath,
                    ex.Message);
                Message.Error("CLI_OutputPathNotWritable", outputPath, ex.Message);
                return false;
            }
        }

        private void DisplaySummary(
            CompilationResult result,
            int filesWritten,
            int directoriesCreated,
            bool dryRun = false)
        {
            // Safety net for return paths that never reach the post-parse transfer above
            // (invalid output path, clean failure, cancellation, no source files, incremental
            // skip). No-ops if already transferred — the pending list is cleared on transfer.
            this._project.TransferPendingConfigWarningsTo(result.Diagnostics);

            var sourceFileCount = result.SourceFileCount > 0
                ? result.SourceFileCount
                : result.ParsedFiles?.Count ?? 0;
            var totalDuration = result.ParseDuration
                + result.BindDuration
                + result.CheckDuration
                + result.OptimizeDuration
                + result.EmitDuration;
            var peakMemoryMb = GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024.0);
            var outputPath = BuildOutputCleaner.ResolveOutputDirectory(
                Path.GetFullPath(this._project.GetProjectPath()),
                this._project.Output.Path);

            Message.Display("");

            if (result.IncrementalBuildSkipped)
            {
                Message.Success("CLI_BuildUpToDate");
                Message.Display("CLI_BuildSummaryFilesIncremental", sourceFileCount);
                return;
            }

            if (result.Diagnostics.HasErrors)
            {
                Message.Error("CLI_BuildFailed", result.Diagnostics.ErrorCount);
            }
            else if (this._project.Build.StrictMode && result.Diagnostics.HasWarnings)
            {
                Message.Error("CLI_BuildFailedStrictMode", result.Diagnostics.WarningCount);
            }
            else if (result.Diagnostics.HasWarnings)
            {
                Message.Success("CLI_BuildCompletedWithWarnings", result.Diagnostics.WarningCount);
            }
            else
            {
                Message.Success("CLI_BuildCompletedSuccess");
            }

            Message.Display("");

            if (dryRun)
            {
                Message.Display("CLI_BuildSummaryFilesDryRun", sourceFileCount, filesWritten);
            }
            else if (filesWritten > 0)
            {
                Message.Display("CLI_BuildSummaryFilesWithOutput", sourceFileCount, filesWritten);
            }
            else
            {
                Message.Display("CLI_FilesCompiled", sourceFileCount);
            }

            Message.Display(
                "CLI_BuildSummaryDuration",
                totalDuration.TotalSeconds,
                result.ParseDuration.TotalSeconds,
                result.BindDuration.TotalSeconds,
                result.CheckDuration.TotalSeconds,
                result.EmitDuration.TotalSeconds);

            if (result.Diagnostics.HasErrors)
            {
                Message.Display("CLI_ErrorsCount", result.Diagnostics.ErrorCount);
            }

            if (result.Diagnostics.HasWarnings)
            {
                Message.Display("CLI_WarningsCount", result.Diagnostics.WarningCount);
            }

            Message.Display("CLI_PeakMemoryUsage", $"{peakMemoryMb:F2} MB");

            if (result.Diagnostics.HasErrors || result.Diagnostics.HasWarnings)
            {
                Message.Display("");
                result.Diagnostics.DisplayAll(new ConsoleDiagnosticFormatter(this._project.BeQuiet));
            }

            if (!result.Diagnostics.HasErrors
                && !(this._project.Build.StrictMode && result.Diagnostics.HasWarnings)
                && filesWritten > 0
                && !dryRun)
            {
                Message.Display("");
                Message.Display("CLI_BuildOutputWrittenTo", outputPath);
            }
            else if (dryRun && filesWritten > 0)
            {
                Message.Display("");
                Message.Display("CLI_BuildDryRunWouldWriteTo", outputPath);
            }

            if (this._project.Build.Verbose)
            {
                Message.Display("");
                Message.Display("CLI_PhaseTimingsHeader");
                Message.Display("CLI_ParseTiming", result.ParseDuration.TotalSeconds);
                Message.Display("CLI_BindTiming", result.BindDuration.TotalSeconds);
                Message.Display("CLI_CheckTiming", result.CheckDuration.TotalSeconds);

                if (result.OptimizeDuration > TimeSpan.Zero)
                {
                    Message.Display("CLI_OptimizeTiming", result.OptimizeDuration.TotalSeconds);
                }

                if (result.EmitDuration > TimeSpan.Zero)
                {
                    Message.Display("CLI_EmitTiming", result.EmitDuration.TotalSeconds);
                }

                if (directoriesCreated > 0)
                {
                    Message.Display("CLI_DirectoriesCreated", directoriesCreated);
                }
            }
        }

        private CompilationResult HandleCancellation(CompilationResult result)
        {
            Message.Warn("CLI_BuildCancelled");
            result.Diagnostics.AddError(
                MessageCode.ParserCompileAborted,
                "",
                0,
                0,
                Message.Localize("CLI_BuildCancelled"));
            this.DisplaySummary(result, filesWritten: 0, directoriesCreated: 0);
            return result;
        }

        private CompilationResult HandleDiscoveryFailure(
            CompilationResult result,
            MessageCode code,
            string messageKey,
            string detail)
        {
            result.Diagnostics.AddError(code, "", 0, 0, Message.Localize(messageKey, detail));
            Message.Error(messageKey, detail);
            this.DisplaySummary(result, filesWritten: 0, directoriesCreated: 0);
            return result;
        }

        private CompilationResult HandleParseFailure(CompilationResult result, string messageKey, string detail)
        {
            Message.Error(messageKey, detail);
            result.Diagnostics.AddError(
                MessageCode.ParserCompileAborted,
                "",
                0,
                0,
                Message.Localize(messageKey, detail));
            this.DisplaySummary(result, filesWritten: 0, directoriesCreated: 0);
            return result;
        }

        private void LogVerbose(string messageKey, params object[] args)
        {
            if (this._project.Build.Verbose)
            {
                Message.Display(messageKey, args);
            }
        }

        private void LogVerboseBinderSummary(CompilationResult result)
        {
            if (!this._project.Build.Verbose || result.GlobalScope == null)
            {
                return;
            }

            var (symbolCount, scopeCount) = result.GlobalScope.GetCounts();
            this.LogVerbose(
                "CLI_VerboseBinderSummary",
                result.GlobalScope.FileScopeCount,
                symbolCount,
                scopeCount,
                result.BindDuration.TotalSeconds);
        }

        private void LogVerboseCheckerSummary(CompilationResult result)
        {
            if (!this._project.Build.Verbose || result.CheckDuration <= TimeSpan.Zero)
            {
                return;
            }

            this.LogVerbose(
                "CLI_VerboseCheckerSummary",
                result.CheckErrorCount,
                result.Diagnostics.WarningCount,
                result.CheckDuration.TotalSeconds);
        }

        private void LogVerboseWrittenFiles(WriteResult writeResult)
        {
            if (!this._project.Build.Verbose)
            {
                return;
            }

            foreach (var writtenPath in writeResult.WrittenPaths)
            {
                this.LogVerbose("CLI_VerboseFileWritten", writtenPath);
            }
        }

        private static long ComputeTotalFileSize(IEnumerable<string> sourceFiles)
        {
            long totalBytes = 0;
            foreach (var sourceFile in sourceFiles)
            {
                try
                {
                    var fileInfo = new FileInfo(sourceFile);
                    if (fileInfo.Exists)
                    {
                        totalBytes += fileInfo.Length;
                    }
                }
                catch (Exception)
                {
                    // Ignore inaccessible files; parsing will report errors later.
                }
            }

            return totalBytes;
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
