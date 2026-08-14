using System.Collections.Concurrent;
using System.Diagnostics;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Parser;
using Tyhp.TyhpLang.Visitor;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Shared compilation pipeline service for parsing Tyhp, PHP, and Tyhpdef files.
    /// Handles multi-threaded parsing, AST caching, error collection, and progress reporting.
    /// Used by DebugAction, BuildAction, LintAction, and the language server.
    /// Implements IDisposable to properly clean up thread-local resources.
    /// </summary>
    public class CompilationService : IDisposable
    {
        // Pre-read is fast (just file I/O), so report less frequently to reduce overhead
        private const int PreReadProgressReportInterval = 100;
        // Parsing is slower (lexing + parsing + AST construction), so report more frequently for responsiveness
        private const int ParseProgressReportInterval = 10;

        private readonly ThreadLocal<TyhpLexer> _threadLexer;
        private readonly ThreadLocal<TyhpParser> _threadParser;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="CompilationService"/> class.
        /// </summary>
        public CompilationService()
        {
            // Initialize thread-local lexer and parser instances
            // Each thread gets its own instances to avoid synchronization overhead
            this._threadLexer = new ThreadLocal<TyhpLexer>(() =>
            {
                var lexer = new TyhpLexer(new AntlrInputStream(new StringReader("")));
                lexer.RemoveErrorListeners();
                // Error listener is set per-file in ParseFile method
                return lexer;
            }, trackAllValues: true);

            this._threadParser = new ThreadLocal<TyhpParser>(() =>
            {
                var parser = new TyhpParser(
                    new CommonTokenStream(this._threadLexer.Value),
                    TextWriter.Null,
                    TextWriter.Null);
                parser.RemoveErrorListeners();
                // Error listener is set per-file in ParseFile method
                return parser;
            }, trackAllValues: true);
        }

        /// <summary>
        /// Parses a collection of source files using the configured options.
        /// </summary>
        /// <param name="filePaths">The paths to the files to parse (.php, .tyhp, .tyhpdef).</param>
        /// <param name="options">Compilation options controlling threading, caching, profiling, etc.</param>
        /// <param name="cancellationToken">Token for canceling the compilation.</param>
        /// <returns>A CompilationResult containing all parsed ASTs and diagnostics.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filePaths or options is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the service has been disposed.</exception>
        public CompilationResult ParseFiles(
            IEnumerable<string> filePaths,
            CompilationOptions options,
            CancellationToken cancellationToken = default)
        {
            if (this._disposed)
            {
                throw new ObjectDisposedException(nameof(CompilationService));
            }

            if (filePaths == null)
            {
                throw new ArgumentNullException(nameof(filePaths));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var result = new CompilationResult();
            var parseStopwatch = Stopwatch.StartNew();

            var fileList = filePaths.ToList();
            var totalFiles = fileList.Count;

            // Handle empty file list - return early with empty result
            if (totalFiles == 0)
            {
                result.ParsedFiles = new List<SrcFileAst>();
                result.ParseDuration = TimeSpan.Zero;
                return result;
            }
            var filesProcessed = 0;
            var threadsRunning = 0;

            // Calculate total bytes with error handling for inaccessible files
            long totalBytesRead = 0;
            foreach (var filePath in fileList)
            {
                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Exists)
                    {
                        totalBytesRead += fileInfo.Length;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // File exists but can't access - skip in size calculation
                    // Will be handled during actual parsing
                }
                catch (IOException)
                {
                    // I/O error accessing file metadata - skip in size calculation
                    // Will be handled during actual parsing
                }
                catch (Exception)
                {
                    // Other exceptions (security, path too long, etc.) - skip
                    // Will be handled during actual parsing
                }
            }

            var allFileData = new ConcurrentDictionary<string, char[]?>();
            var parsedAsts = new ConcurrentBag<SrcFileAst>();
            var astCacheHits = 0;
            var astCacheMisses = 0;

            // Pre-read files into memory if conditions are met
            if (totalBytesRead < options.PreReadThreshold && totalFiles >= options.PreReadMinFiles)
            {
                // Read all files into memory for better cache locality
                Parallel.ForEach(fileList, new ParallelOptions { MaxDegreeOfParallelism = options.MaxThreads }, (filePath, state) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        state.Stop();
                        return;
                    }

                    try
                    {
                        var fileContent = File.ReadAllText(filePath);
                        allFileData.TryAdd(filePath, fileContent.ToCharArray());
                    }
                    catch (FileNotFoundException)
                    {
                        // File was deleted between enumeration and read - mark as null for on-demand read
                        allFileData.TryAdd(filePath, null);
                        result.Diagnostics.AddWarning(
                            MessageCode.ParserUnknownError,
                            filePath,
                            0,
                            0,
                            "File not found during pre-read, will retry during parsing");
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Access denied - mark as null for on-demand read (will error during parse if still denied)
                        allFileData.TryAdd(filePath, null);
                    }
                    catch (IOException ex)
                    {
                        // I/O error - mark as null for on-demand read
                        allFileData.TryAdd(filePath, null);
                        result.Diagnostics.AddWarning(
                            MessageCode.ParserUnknownError,
                            filePath,
                            0,
                            0,
                            $"I/O error during pre-read: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        // Unexpected error - mark as null and log warning
                        allFileData.TryAdd(filePath, null);
                        result.Diagnostics.AddWarning(
                            MessageCode.ParserUnknownError,
                            filePath,
                            0,
                            0,
                            $"Unexpected error during pre-read: {ex.Message}");
                    }

                    // Report progress for file reading
                    var processed = Interlocked.Increment(ref filesProcessed);
                    if (processed % PreReadProgressReportInterval == 0)
                    {
                        ReportProgress(options.Progress, processed, totalFiles, result, "Reading files...");
                    }
                });

                // Reset for parsing phase
                filesProcessed = 0;
            }
            else
            {
                // Don't pre-read - files will be read on-demand during parsing
                foreach (var filePath in fileList)
                {
                    allFileData.TryAdd(filePath, null);
                }
            }

            // Parse all files in parallel
            if (options.MaxThreads == 1)
            {
                // Single-threaded mode for easier debugging
                foreach (var fileData in allFileData)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    ParseFile(
                        fileData,
                        null,
                        0,
                        0,
                        result.Diagnostics,
                        parsedAsts,
                        options,
                        ref filesProcessed,
                        ref threadsRunning,
                        totalFiles,
                        ref astCacheHits,
                        ref astCacheMisses,
                        cancellationToken);
                }
            }
            else
            {
                // Multi-threaded parsing
                // Uses the thread-local overload so that localInit/localFinally can track
                // threadsRunning for progress reporting; the localData value itself is unused.
                Parallel.ForEach(
                    allFileData,
                    new ParallelOptions { MaxDegreeOfParallelism = options.MaxThreads },
                    () =>
                    {
                        Interlocked.Increment(ref threadsRunning);
                        return 0;
                    },
                    (fileData, state, index, localData) =>
                    {
                        ParseFile(
                            fileData,
                            state,
                            index,
                            localData,
                            result.Diagnostics,
                            parsedAsts,
                            options,
                            ref filesProcessed,
                            ref threadsRunning,
                            totalFiles,
                            ref astCacheHits,
                            ref astCacheMisses,
                            cancellationToken);
                        return 0;
                    },
                    (localData) =>
                    {
                        Interlocked.Decrement(ref threadsRunning);
                    });
            }

            // Populate parsed files in result
            result.ParsedFiles = parsedAsts.OrderBy(ast => ast.FileName).ToList();
            result.AstCacheHits = astCacheHits;
            result.AstCacheMisses = astCacheMisses;

            // Record parse duration (preserving parse-phase timing)
            parseStopwatch.Stop();
            result.ParseDuration = parseStopwatch.Elapsed;
            result.ParseErrorCount = result.Diagnostics.ErrorCount;

            if (!result.Diagnostics.HasErrors)
            {
                var bindStopwatch = Stopwatch.StartNew();
                result.GlobalScope = BindParsedFiles(
                    result.ParsedFiles ?? Array.Empty<SrcFileAst>(),
                    result.Diagnostics,
                    options);
                bindStopwatch.Stop();
                result.BindDuration = bindStopwatch.Elapsed;
                result.BindErrorCount = result.Diagnostics.ErrorCount - result.ParseErrorCount;
            }
            else
            {
                result.BindDuration = TimeSpan.Zero;
                result.BindErrorCount = 0;
            }

            if (!result.Diagnostics.HasErrors && result.GlobalScope is not null && !options.SkipChecking)
            {
                var errorsBeforeCheck = result.Diagnostics.ErrorCount;
                var checkStopwatch = Stopwatch.StartNew();
                CheckParsedFiles(result, options);
                checkStopwatch.Stop();
                result.CheckDuration = checkStopwatch.Elapsed;
                result.CheckErrorCount = result.Diagnostics.ErrorCount - errorsBeforeCheck;
            }
            else
            {
                result.CheckDuration = TimeSpan.Zero;
                result.CheckErrorCount = 0;
            }

            // Flush after bind so tyhpdef ASTs cached during LoadTyhpdefSymbols are written too.
            // Guard filesystem errors — a failed flush must not discard a successful compile.
            try
            {
                AstCacheService.FlushMemory();
            }
            catch (IOException ex)
            {
                result.Diagnostics.AddError(
                    MessageCode.ParserCompileAborted,
                    "",
                    0,
                    0,
                    $"Failed to flush AST cache to disk: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Diagnostics.AddError(
                    MessageCode.ParserCompileAborted,
                    "",
                    0,
                    0,
                    $"Access denied writing AST cache: {ex.Message}");
            }

            // PLACEHOLDER_STORY_09: Emitter reads checker diagnostics for conditional emit paths
            // PLACEHOLDER_STORY_19: LSP integration — publishDiagnostics from checker

            // Final progress report
            ReportProgress(options.Progress, filesProcessed, totalFiles, result, "Parsing complete");

            return result;
        }

        /// <summary>
        /// Parses a single file and adds the resulting AST to the collection.
        /// </summary>
        private int ParseFile(
            KeyValuePair<string, char[]?> fileData,
            ParallelLoopState? state,
            long index,
            int localData,
            DiagnosticBag diagnostics,
            ConcurrentBag<SrcFileAst> parsedAsts,
            CompilationOptions options,
            ref int filesProcessed,
            ref int threadsRunning,
            int totalFiles,
            ref int astCacheHits,
            ref int astCacheMisses,
            CancellationToken cancellationToken)
        {
            // Check for cancellation
            if (cancellationToken.IsCancellationRequested)
            {
                state?.Stop();
                return 0;
            }

            // Report progress periodically
            var currentProcessed = filesProcessed;
            if (currentProcessed % ParseProgressReportInterval == 0)
            {
                ReportProgress(
                    options.Progress,
                    currentProcessed,
                    totalFiles,
                    diagnostics,
                    fileData.Key,
                    threadsRunning);
            }

            // Trigger garbage collection at specified intervals
            if (options.GarbageCollectInterval > 0 &&
                currentProcessed % options.GarbageCollectInterval == 0 &&
                currentProcessed > 0)
            {
                GC.Collect();
            }

            // Get thread-local lexer and parser instances
            TyhpLexer? lexer = null;
            TyhpParser? parser = null;

            try
            {
                lexer = this._threadLexer.Value;
                parser = this._threadParser.Value;
            }
            catch (ObjectDisposedException)
            {
                diagnostics.AddError(
                    MessageCode.ParserCompileAborted,
                    fileData.Key,
                    0,
                    0,
                    "Internal error: compilation service has been disposed");
                return 0;
            }

            if (lexer == null || parser == null)
            {
                diagnostics.AddError(
                    MessageCode.ParserCompileAborted,
                    fileData.Key,
                    0,
                    0,
                    "Internal error: thread-local lexer or parser is null");
                return 0;
            }

            // Reset parser state to prevent leakage between files
            parser.Profile = options.EnableProfiling;

            // Reset prediction mode to default before applying options
            parser.Interpreter.PredictionMode = PredictionMode.SLL;

            // Declare error listeners outside try block so they can be disposed in finally
            TyhpAntlrErrorListener<int>? lexerErrorListener = null;
            TyhpAntlrErrorListener<IToken>? parserErrorListener = null;

            try
            {
                // Read file content once if not pre-read - avoid duplicate reads
                string? fileContent = null;
                char[]? fileChars = fileData.Value;

                if (fileChars == null)
                {
                    // File was not pre-read, read it now
                    try
                    {
                        fileContent = File.ReadAllText(fileData.Key);
                        fileChars = fileContent.ToCharArray();
                    }
                    catch (FileNotFoundException)
                    {
                        diagnostics.AddError(
                            MessageCode.ParserCompileAborted,
                            fileData.Key,
                            0,
                            0,
                            $"File not found: {fileData.Key}");
                        return 0;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        diagnostics.AddError(
                            MessageCode.ParserCompileAborted,
                            fileData.Key,
                            0,
                            0,
                            $"Access denied: {fileData.Key}");
                        return 0;
                    }
                    catch (IOException ex)
                    {
                        diagnostics.AddError(
                            MessageCode.ParserCompileAborted,
                            fileData.Key,
                            0,
                            0,
                            $"I/O error reading file: {ex.Message}");
                        return 0;
                    }
                }
                else
                {
                    fileContent = new string(fileChars);
                }

                // Set up input stream using already-read content
                var inputStream = new AntlrInputStream(fileChars, fileChars.Length);

                var taglessEnabled = false;
                var taglessLanguageMode = string.Empty;
                if (fileData.Key.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
                {
                    taglessEnabled = options.Tagless;
                    taglessLanguageMode = "tyhpdef";
                }
                else if (fileData.Key.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
                {
                    taglessEnabled = options.Tagless;
                    taglessLanguageMode = "tyhp";
                }

                lexer.SetInputStream(inputStream);
                lexer.Reset();
                lexer.ConfigureTagless(taglessEnabled, taglessLanguageMode, diagnostics, fileData.Key);
                (parser.TokenStream as CommonTokenStream)?.SetTokenSource(lexer);
                parser.Reset();

                // Set up error listeners for this file
                // Create fresh listeners for each file to avoid cross-contamination
                lexerErrorListener = new TyhpAntlrErrorListener<int>(diagnostics);
                parserErrorListener = new TyhpAntlrErrorListener<IToken>(diagnostics);

                lexer.RemoveErrorListeners();
                lexer.AddErrorListener(lexerErrorListener);
                lexerErrorListener.SetFileName(fileData.Key);

                parser.RemoveErrorListeners();
                parser.AddErrorListener(parserErrorListener);
                parserErrorListener.SetFileName(fileData.Key);

                // Configure ambiguity detection if enabled
                if (options.ReportAmbiguities)
                {
                    parser.Interpreter.PredictionMode = PredictionMode.LL_EXACT_AMBIG_DETECTION;
                }

                // Compute file hash for cache lookup using already-read content.
                // Include tagless mode so identical bytes lex differently when the setting toggles.
                string fileDataHash = AstCacheService.ComputeContentHash(fileContent, taglessEnabled);

                // Try to get from cache if enabled
                SrcFileAst? ast = null;
                if (options.EnableAstCache)
                {
                    ast = AstCacheService.Get(fileData.Key, fileDataHash);
                    if (ast != null)
                    {
                        Interlocked.Increment(ref astCacheHits);
                    }
                }

                // Parse if not cached
                if (ast == null)
                {
                    if (options.EnableAstCache)
                    {
                        Interlocked.Increment(ref astCacheMisses);
                    }

                    // Snapshot before lex/parse/visit so we can refuse to cache a recoverable
                    // error tree (ANTLR still yields a non-null AST; diagnostics are not serialized).
                    var errorsBeforeParse = diagnostics.CountErrorsForFile(fileData.Key);

                    ParserRuleContext ctx;

                    // Determine entry point based on file extension (case-insensitive).
                    // Check .tyhpdef before .tyhp because .tyhpdef ends with .tyhp.
                    // When source.tagless is enabled, use the dedicated tagless entry rules
                    // (optional open tag, no inline output / closing tag).
                    if (fileData.Key.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx = taglessEnabled ? parser.tyhpdefTaglessSrcFile() : parser.tyhpdefSrcFile();
                    }
                    else if (fileData.Key.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx = taglessEnabled ? parser.tyhpTaglessSrcFile() : parser.tyhpSrcFile();
                    }
                    else
                    {
                        // Default to PHP parser for .php files and any other extensions
                        ctx = parser.phpSrcFile();
                    }

                    if (taglessEnabled && parser.TokenStream is CommonTokenStream taglessTokenStream)
                    {
                        taglessTokenStream.Fill();
                    }

                    // Visit the parse tree to build AST
                    var visitor = new TyhpParserAstVisitor(
                        parser.TokenStream as CommonTokenStream,
                        fileData.Key,
                        fileDataHash,
                        diagnostics);

                    // Visit the parse tree - may return null if parsing fails catastrophically
                    var visitResult = visitor.Visit(ctx);
                    ast = visitResult as SrcFileAst;

                    if (ast == null && visitResult != null)
                    {
                        // Visitor returned a non-null result but it's not a SrcFileAst
                        // This indicates a visitor implementation error
                        diagnostics.AddError(
                            MessageCode.VisitorUnexpectedAlternative,
                            fileData.Key,
                            0,
                            0,
                            "Visitor returned unexpected type",
                            visitResult.GetType().Name);
                    }

                    // Cache only error-free parses. A broken file still produces a partial AST via
                    // ANTLR recovery; caching it would make the next run a silent success.
                    var parseProducedErrors =
                        diagnostics.CountErrorsForFile(fileData.Key) > errorsBeforeParse;
                    if (options.EnableAstCache && ast != null && !parseProducedErrors)
                    {
                        try
                        {
                            AstCacheService.AddOrUpdate(ast);
                        }
                        catch (IOException ex)
                        {
                            // Cache write failed - log but continue
                            diagnostics.AddWarning(
                                MessageCode.ParserUnknownError,
                                fileData.Key,
                                0,
                                0,
                                $"Failed to cache AST: {ex.Message}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            // Cache write failed due to permissions - log but continue
                            diagnostics.AddWarning(
                                MessageCode.ParserUnknownError,
                                fileData.Key,
                                0,
                                0,
                                $"Access denied writing AST cache: {ex.Message}");
                        }
                    }
                }
                else if (taglessEnabled && parser.TokenStream is CommonTokenStream cachedTaglessTokenStream)
                {
                    cachedTaglessTokenStream.Seek(0);
                    cachedTaglessTokenStream.Fill();
                }

                // Add to results if successfully parsed
                if (ast != null)
                {
                    parsedAsts.Add(ast);
                }
                else
                {
                    // AST was null - either from cache miss + parse failure or cache hit with null
                    // Diagnostics should already have been added by parser/visitor
                    // No need to add another error here
                }
            }
            catch (ParseCanceledException)
            {
                // Parsing was cancelled - ignore
            }
            catch (Exception ex)
            {
                diagnostics.AddError(
                    MessageCode.ParserCompileAborted,
                    fileData.Key,
                    0,
                    0,
                    $"Error ({ex.GetType().Name}): {ex.Message}");
            }
            finally
            {
                // Dispose error listeners to clean up ThreadLocal resources
                lexerErrorListener?.Dispose();
                parserErrorListener?.Dispose();

                Interlocked.Increment(ref filesProcessed);
            }

            return 0;
        }

        /// <summary>
        /// Reports progress to the callback if one is configured.
        /// Thread-safe: captures diagnostic counts at call time to avoid race conditions.
        /// </summary>
        private static void ReportProgress(
            IProgress<CompilationProgress>? progress,
            int filesProcessed,
            int totalFiles,
            DiagnosticBag diagnostics,
            string currentFile,
            int threadsRunning = 0,
            int astCacheHits = 0,
            int astCacheMisses = 0)
        {
            if (progress == null)
            {
                return;
            }

            var errorCount = diagnostics.ErrorCount;
            var warningCount = diagnostics.WarningCount;
            var memoryUsage = Process.GetCurrentProcess().WorkingSet64;

            progress.Report(new CompilationProgress
            {
                FilesProcessed = filesProcessed,
                TotalFiles = totalFiles,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                CurrentFile = currentFile ?? string.Empty,
                MemoryUsage = memoryUsage,
                AstCacheHits = astCacheHits,
                AstCacheMisses = astCacheMisses,
            });
        }

        /// <summary>
        /// Overload for reporting progress with a simple message.
        /// </summary>
        private static void ReportProgress(
            IProgress<CompilationProgress>? progress,
            int filesProcessed,
            int totalFiles,
            CompilationResult result,
            string currentFile,
            int astCacheHits = 0,
            int astCacheMisses = 0)
        {
            ReportProgress(
                progress,
                filesProcessed,
                totalFiles,
                result.Diagnostics,
                currentFile,
                astCacheHits: astCacheHits,
                astCacheMisses: astCacheMisses);
        }

        private static GlobalScope? BindParsedFiles(
            IReadOnlyList<SrcFileAst> parsedFiles,
            DiagnosticBag diagnostics,
            CompilationOptions options)
        {
            try
            {
                var binder = new TyhpBinder(diagnostics, options);
                return binder.Bind(parsedFiles);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.AddError(
                    MessageCode.BinderUnknownError,
                    "",
                    0,
                    0,
                    $"Binder invocation failed: {ex.GetType().Name} - {ex.Message}");
            }

            return null;
        }

        private static void CheckParsedFiles(CompilationResult result, CompilationOptions options)
        {
            try
            {
                var symbolTree = new SymbolTree(result.GlobalScope!);
                // PLACEHOLDER_STORY_07: Unit tests for checker pipeline integration
                var checker = new TyhpChecker(
                    result.Diagnostics,
                    symbolTree,
                    result.GlobalScope!,
                    options.Checker);
                checker.Check(result.ParsedFiles ?? Array.Empty<SrcFileAst>());
                result.NarrowedTypes = checker.NarrowedTypes;
                result.RequiresRuntimeGenericTracking = checker.RequiresRuntimeGenericTracking;
                result.RequiresGenericVariant = checker.RequiresGenericVariant;
                result.GenericCallTargets = checker.GenericCallTargets;
                result.RequiresWeakReferenceCapture = checker.RequiresWeakReferenceCapture;
                result.InferredClosureSignatures = checker.InferredClosureSignatures;
                result.ExpressionTypes = checker.ExpressionTypes;
                result.RequiresDisposableTryFinally = checker.RequiresDisposableTryFinally;
                result.AsyncForeachKinds = checker.AsyncForeachKinds;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                result.Diagnostics.AddError(
                    MessageCode.CheckerUnknownError,
                    "",
                    0,
                    0,
                    $"Checker invocation failed: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the parser instances for each thread (for profiling purposes).
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the service has been disposed.</exception>
        public IEnumerable<TyhpParser> GetThreadParsers()
        {
            if (this._disposed)
            {
                throw new ObjectDisposedException(nameof(CompilationService));
            }

            return this._threadParser.Values.Where(p => p != null)!;
        }

        /// <summary>
        /// Disposes of thread-local resources.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected implementation of Dispose pattern.
        /// </summary>
        /// <param name="disposing">True if called from Dispose(), false if called from finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this._disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                    this._threadLexer?.Dispose();
                    this._threadParser?.Dispose();
                }

                this._disposed = true;
            }
        }
    }
}
