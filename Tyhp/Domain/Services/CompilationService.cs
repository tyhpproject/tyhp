using System.Collections.Concurrent;
using System.Diagnostics;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Misc;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
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
    /// Used by DebugAction, BuildAction, LintAction, SymbolTreeAction, and the language server.
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
        private readonly ThreadLocal<TyhpdefLexer> _threadTyhpdefLexer;
        private readonly ThreadLocal<TyhpdefParser> _threadTyhpdefParser;
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

            this._threadTyhpdefLexer = new ThreadLocal<TyhpdefLexer>(() =>
            {
                var lexer = new TyhpdefLexer(new AntlrInputStream(new StringReader("")));
                lexer.RemoveErrorListeners();
                return lexer;
            }, trackAllValues: true);

            this._threadTyhpdefParser = new ThreadLocal<TyhpdefParser>(() =>
            {
                var parser = new TyhpdefParser(
                    new CommonTokenStream(this._threadTyhpdefLexer.Value),
                    TextWriter.Null,
                    TextWriter.Null);
                parser.RemoveErrorListeners();
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

            options.ApplyMissingPhpVersionDefault();

            var result = new CompilationResult(options.SuppressedWarnings);
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
            }
            else
            {
                // Don't pre-read - files will be read on-demand during parsing
                foreach (var filePath in fileList)
                {
                    allFileData.TryAdd(filePath, null);
                }
            }

            return this.FinishParseBindCheck(
                allFileData,
                options,
                cancellationToken,
                result,
                parseStopwatch,
                totalFiles);
        }

        /// <summary>
        /// Same pipeline as <see cref="ParseFiles(IEnumerable{string}, CompilationOptions, CancellationToken)"/>
        /// using caller-supplied source text instead of reading paths from disk.
        /// </summary>
        public CompilationResult ParseFiles(
            IReadOnlyDictionary<string, string> inMemoryFiles,
            CompilationOptions options,
            CancellationToken cancellationToken = default)
        {
            if (this._disposed)
            {
                throw new ObjectDisposedException(nameof(CompilationService));
            }

            ArgumentNullException.ThrowIfNull(inMemoryFiles);
            ArgumentNullException.ThrowIfNull(options);

            options.ApplyMissingPhpVersionDefault();

            var result = new CompilationResult(options.SuppressedWarnings);
            var parseStopwatch = Stopwatch.StartNew();
            var totalFiles = inMemoryFiles.Count;
            if (totalFiles == 0)
            {
                result.ParsedFiles = new List<SrcFileAst>();
                result.ParseDuration = TimeSpan.Zero;
                return result;
            }

            var allFileData = new ConcurrentDictionary<string, char[]?>();
            foreach (var pair in inMemoryFiles)
            {
                allFileData.TryAdd(pair.Key, pair.Value.ToCharArray());
            }

            return this.FinishParseBindCheck(
                allFileData,
                options,
                cancellationToken,
                result,
                parseStopwatch,
                totalFiles);
        }

        private CompilationResult FinishParseBindCheck(
            ConcurrentDictionary<string, char[]?> allFileData,
            CompilationOptions options,
            CancellationToken cancellationToken,
            CompilationResult result,
            Stopwatch parseStopwatch,
            int totalFiles)
        {
            var filesProcessed = 0;
            var threadsRunning = 0;
            var parsedAsts = new ConcurrentBag<SrcFileAst>();
            var astCacheHits = 0;
            var astCacheMisses = 0;

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

            // Parse errors skip bind/check for that file only. Sibling files that parsed
            // still bind and check; parse diagnostics stay on the broken file.
            var filesToBind = SelectFilesWithoutParseErrors(
                result.ParsedFiles ?? Array.Empty<SrcFileAst>(),
                result.Diagnostics);

            if (filesToBind.Count > 0)
            {
                var bindStopwatch = Stopwatch.StartNew();
                result.GlobalScope = BindParsedFiles(
                    filesToBind,
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

            // Bind errors (including tyhpdef) must not skip checking: Mechanism D flags and
            // generic call-site routing live on the checker. User files are still checked.
            if (result.GlobalScope is not null && !options.SkipChecking)
            {
                var errorsBeforeCheck = result.Diagnostics.ErrorCount;
                var checkStopwatch = Stopwatch.StartNew();
                CheckParsedFiles(result, options, filesToBind);
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
            // Skip when user-file cache is off (language-rule tests): tyhpdef hits stay in the
            // in-memory map, and GetCacheDir does not need Project.Singleton.
            if (options.EnableAstCache)
            {
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
            }

            // PLACEHOLDER_STORY_09: Emitter reads checker diagnostics for conditional emit paths
            // Language-server publishDiagnostics is owned by AnalysisService (Story 19 Phase 3),
            // not this batch pipeline.

            // Final progress report
            ReportProgress(options.Progress, filesProcessed, totalFiles, result, "Parsing complete");

            return result;
        }

        /// <summary>
        /// Parses in-memory source using the same lexer, parser, visitor, error listeners,
        /// and language-mode detection as <see cref="ParseFiles"/>, without reading from disk.
        /// </summary>
        /// <param name="content">Full document text.</param>
        /// <param name="filePath">Path used for diagnostics, cache keys, and entry-point selection.</param>
        /// <param name="diagnostics">Bag that receives parse/lexer/visitor diagnostics.</param>
        /// <param name="options">Compilation options; cache is off when omitted.</param>
        /// <returns>The parsed <see cref="SrcFileAst"/>, or null if parsing produced no tree.</returns>
        public SrcFileAst? ParseFromContent(
            string content,
            string filePath,
            DiagnosticBag? diagnostics = null,
            CompilationOptions? options = null)
        {
            if (this._disposed)
            {
                throw new ObjectDisposedException(nameof(CompilationService));
            }

            ArgumentNullException.ThrowIfNull(content);
            ArgumentNullException.ThrowIfNull(filePath);

            diagnostics ??= new DiagnosticBag();
            options ??= new CompilationOptions { EnableAstCache = false };
            return this.ParseContentCore(content, filePath, diagnostics, options, out _, out _);
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

            string? fileContent;
            char[]? fileChars = fileData.Value;
            if (fileChars == null)
            {
                try
                {
                    fileContent = File.ReadAllText(fileData.Key);
                }
                catch (FileNotFoundException)
                {
                    diagnostics.AddError(
                        MessageCode.ParserCompileAborted,
                        fileData.Key,
                        0,
                        0,
                        $"File not found: {fileData.Key}");
                    Interlocked.Increment(ref filesProcessed);
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
                    Interlocked.Increment(ref filesProcessed);
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
                    Interlocked.Increment(ref filesProcessed);
                    return 0;
                }
            }
            else
            {
                fileContent = new string(fileChars);
            }

            SrcFileAst? ast = this.ParseContentCore(
                fileContent,
                fileData.Key,
                diagnostics,
                options,
                out bool cacheHit,
                out bool cacheMiss);

            if (cacheHit)
            {
                Interlocked.Increment(ref astCacheHits);
            }

            if (cacheMiss)
            {
                Interlocked.Increment(ref astCacheMisses);
            }

            if (ast != null)
            {
                parsedAsts.Add(ast);
            }

            Interlocked.Increment(ref filesProcessed);
            return 0;
        }

        /// <summary>
        /// Shared parse pipeline for disk and in-memory content.
        /// </summary>
        private SrcFileAst? ParseContentCore(
            string content,
            string filePath,
            DiagnosticBag diagnostics,
            CompilationOptions options,
            out bool cacheHit,
            out bool cacheMiss)
        {
            cacheHit = false;
            cacheMiss = false;

            var isTyhpdef = filePath.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase);
            Lexer? lexer;
            Parser? parser;
            try
            {
                if (isTyhpdef)
                {
                    lexer = this._threadTyhpdefLexer.Value;
                    parser = this._threadTyhpdefParser.Value;
                }
                else
                {
                    lexer = this._threadLexer.Value;
                    parser = this._threadParser.Value;
                }
            }
            catch (ObjectDisposedException)
            {
                diagnostics.AddError(
                    MessageCode.ParserCompileAborted,
                    filePath,
                    0,
                    0,
                    "Internal error: compilation service has been disposed");
                return null;
            }

            if (lexer == null || parser == null)
            {
                diagnostics.AddError(
                    MessageCode.ParserCompileAborted,
                    filePath,
                    0,
                    0,
                    "Internal error: thread-local lexer or parser is null");
                return null;
            }

            parser.Profile = options.EnableProfiling;
            parser.Interpreter.PredictionMode = PredictionMode.SLL;

            TyhpAntlrErrorListener<int>? lexerErrorListener = null;
            TyhpAntlrErrorListener<IToken>? parserErrorListener = null;

            try
            {
                char[] fileChars = content.ToCharArray();
                var inputStream = new AntlrInputStream(fileChars, fileChars.Length);

                var taglessEnabled = false;
                var taglessLanguageMode = string.Empty;
                if (filePath.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
                {
                    taglessEnabled = options.Tagless;
                    taglessLanguageMode = "tyhpdef";
                }
                else if (filePath.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
                {
                    taglessEnabled = options.Tagless;
                    taglessLanguageMode = "tyhp";
                }

                lexer.SetInputStream(inputStream);
                lexer.Reset();
                if (lexer is TyhpdefLexer tyhpdefLexer)
                {
                    tyhpdefLexer.ConfigureTagless(taglessEnabled, taglessLanguageMode, diagnostics, filePath);
                }
                else
                {
                    ((TyhpLexer)lexer).ConfigureTagless(taglessEnabled, taglessLanguageMode, diagnostics, filePath);
                }
                (parser.TokenStream as CommonTokenStream)?.SetTokenSource(lexer);
                parser.Reset();

                lexerErrorListener = new TyhpAntlrErrorListener<int>(diagnostics);
                parserErrorListener = new TyhpAntlrErrorListener<IToken>(diagnostics);

                lexer.RemoveErrorListeners();
                lexer.AddErrorListener(lexerErrorListener);
                lexerErrorListener.SetFileName(filePath);

                parser.RemoveErrorListeners();
                parser.AddErrorListener(parserErrorListener);
                parserErrorListener.SetFileName(filePath);

                if (options.ReportAmbiguities)
                {
                    parser.Interpreter.PredictionMode = PredictionMode.LL_EXACT_AMBIG_DETECTION;
                }

                string fileDataHash = AstCacheService.ComputeContentHash(content, taglessEnabled);

                SrcFileAst? ast = null;
                if (options.EnableAstCache)
                {
                    try
                    {
                        ast = AstCacheService.Get(filePath, fileDataHash);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                    {
                        diagnostics.AddWarning(
                            MessageCode.ParserUnknownError,
                            filePath,
                            0,
                            0,
                            $"Failed to read AST cache: {ex.Message}");
                        AstCacheService.DeleteCacheFile(filePath);
                        ast = null;
                    }

                    if (ast != null)
                    {
                        cacheHit = true;
                    }
                }

                if (ast == null)
                {
                    if (options.EnableAstCache)
                    {
                        cacheMiss = true;
                    }

                    var errorsBeforeParse = diagnostics.CountErrorsForFile(filePath);

                    ParserRuleContext ctx;
                    if (parser is TyhpdefParser tyhpdefParser)
                    {
                        ctx = taglessEnabled ? tyhpdefParser.tyhpdefTaglessSrcFile() : tyhpdefParser.tyhpdefSrcFile();
                    }
                    else
                    {
                        var tyhpParser = (TyhpParser)parser;
                        if (filePath.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
                        {
                            ctx = taglessEnabled ? tyhpParser.tyhpTaglessSrcFile() : tyhpParser.tyhpSrcFile();
                        }
                        else
                        {
                            ctx = tyhpParser.phpSrcFile();
                        }
                    }

                    if (taglessEnabled && parser.TokenStream is CommonTokenStream taglessTokenStream)
                    {
                        taglessTokenStream.Fill();
                    }

                    var tokenStream = parser.TokenStream as CommonTokenStream;
                    IBase2Ast? visitResult = isTyhpdef
                        ? new TyhpdefParserAstVisitor(tokenStream, filePath, fileDataHash, diagnostics).Visit(ctx) as IBase2Ast
                        : new TyhpParserAstVisitor(tokenStream, filePath, fileDataHash, diagnostics).Visit(ctx) as IBase2Ast;
                    ast = visitResult as SrcFileAst;

                    if (ast == null && visitResult != null)
                    {
                        diagnostics.AddError(
                            MessageCode.VisitorUnexpectedAlternative,
                            filePath,
                            0,
                            0,
                            "Visitor returned unexpected type",
                            visitResult.GetType().Name);
                    }

                    var parseProducedErrors =
                        diagnostics.CountErrorsForFile(filePath) > errorsBeforeParse;
                    if (options.EnableAstCache && ast != null && !parseProducedErrors)
                    {
                        try
                        {
                            AstCacheService.AddOrUpdate(ast);
                        }
                        catch (IOException ex)
                        {
                            diagnostics.AddWarning(
                                MessageCode.ParserUnknownError,
                                filePath,
                                0,
                                0,
                                $"Failed to cache AST: {ex.Message}");
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            diagnostics.AddWarning(
                                MessageCode.ParserUnknownError,
                                filePath,
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

                return ast;
            }
            catch (ParseCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                diagnostics.AddError(
                    MessageCode.ParserCompileAborted,
                    filePath,
                    0,
                    0,
                    $"Error ({ex.GetType().Name}): {ex.Message}");
                return null;
            }
            finally
            {
                lexerErrorListener?.Dispose();
                parserErrorListener?.Dispose();
            }
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

        private static IReadOnlyList<SrcFileAst> SelectFilesWithoutParseErrors(
            IReadOnlyList<SrcFileAst> parsedFiles,
            DiagnosticBag diagnostics)
        {
            if (!diagnostics.HasErrors || parsedFiles.Count == 0)
            {
                return parsedFiles;
            }

            var errorFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var error in diagnostics.Errors)
            {
                AddPathVariants(errorFiles, error.FileName);
            }

            if (errorFiles.Count == 0)
            {
                return parsedFiles;
            }

            var selected = new List<SrcFileAst>(parsedFiles.Count);
            foreach (var file in parsedFiles)
            {
                if (FileHasParseError(file, errorFiles))
                {
                    continue;
                }

                selected.Add(file);
            }

            return selected;
        }

        private static bool FileHasParseError(SrcFileAst file, HashSet<string> errorFiles)
        {
            if (errorFiles.Contains(file.Identifier) || errorFiles.Contains(file.FileName))
            {
                return true;
            }

            var relativeFull = TryGetFullPath(file.FileName);
            return relativeFull is not null && errorFiles.Contains(relativeFull);
        }

        private static void AddPathVariants(HashSet<string> paths, string? fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return;
            }

            paths.Add(fileName);
            var full = TryGetFullPath(fileName);
            if (full is not null)
            {
                paths.Add(full);
            }
        }

        private static string? TryGetFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (PathTooLongException)
            {
                return null;
            }
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

        private static void CheckParsedFiles(
            CompilationResult result,
            CompilationOptions options,
            IReadOnlyList<SrcFileAst> filesToCheck)
        {
            try
            {
                if (options.PhpVersionWasDefaulted)
                {
                    options.Checker.PhpVersionWasDefaulted = true;
                    if (string.IsNullOrWhiteSpace(options.Checker.PhpVersion))
                    {
                        options.Checker.PhpVersion = CompilationOptions.DefaultPhpVersionWhenUnset;
                    }
                }

                var symbolTree = new SymbolTree(result.GlobalScope!);
                // PLACEHOLDER_STORY_07: Unit tests for checker pipeline integration
                var checker = new TyhpChecker(
                    result.Diagnostics,
                    symbolTree,
                    result.GlobalScope!,
                    options.Checker);
                checker.Check(filesToCheck);
                result.NarrowedTypes = checker.NarrowedTypes;
                result.RequiresRuntimeGenericTracking = checker.RequiresRuntimeGenericTracking;
                result.RequiresGenericVariant = checker.RequiresGenericVariant;
                result.GenericCallTargets = checker.GenericCallTargets;
                result.RequiresWeakReferenceCapture = checker.RequiresWeakReferenceCapture;
                result.InferredClosureSignatures = checker.InferredClosureSignatures;
                result.ExpressionTypes = checker.ExpressionTypes;
                result.NativeTypeTests = checker.NativeTypeTests;
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
        /// Gets the parser instances for each thread (for profiling purposes), including both the
        /// PHP/Tyhp <see cref="TyhpParser"/> pool and the <see cref="TyhpdefParser"/> pool used for
        /// <c>.tyhpdef</c> files, so <c>--profile</c> output covers every file kind.
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown when the service has been disposed.</exception>
        public IEnumerable<Parser> GetThreadParsers()
        {
            if (this._disposed)
            {
                throw new ObjectDisposedException(nameof(CompilationService));
            }

            return this._threadParser.Values.Where(p => p != null).Cast<Parser>()
                .Concat(this._threadTyhpdefParser.Values.Where(p => p != null).Cast<Parser>());
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
                    this._threadTyhpdefLexer?.Dispose();
                    this._threadTyhpdefParser?.Dispose();
                }

                this._disposed = true;
            }
        }
    }
}
