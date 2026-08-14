global using Tyhp.TyhpLang.Parser;

using System;
using System.Text;
using Antlr4.Runtime;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Tree;
using Konsole;
using Antlr4.Runtime.Misc;
using Tyhp.TyhpLang.Visitor;
using Tyhp.TyhpLang.Ast;
using Tyhp.Domain.Services;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.CLI
{
    public class DebugAction : ActionRunnerBase
    {
        protected CancellationToken? CancelToken { get; set; }

        /// <summary>
        /// Whether the operation has been cancelled
        /// </summary>
        protected bool IsCancelled { get; set; } = false;

        /// <summary>
        /// Whether to do thread profiling
        /// </summary>
        private bool doThreadProfiling = false;

        /// <summary>
        /// Whether to report ambiguities
        /// </summary>
        private bool reportAmbiguities = false;

        /// <summary>
        /// Whether to check hashes
        /// </summary>
        private bool checkHashes = false;

        /// <summary>
        /// Whether to display binder output (scope tree, symbol counts)
        /// </summary>
        private bool doBind = false;

        private bool doSingleFile => !String.IsNullOrEmpty(this.specialDebugFile);
        private string specialDebugFile = ""; // PLACEHOLDER_STORY_0: Wire from --file <path> CLI option for single-file debug mode
        private bool dumpCtxTree => this.doSingleFile;
        private int totalFilesCount = 0;
        private readonly string directoryPath = "./DebugProject/"; // TODO: set this to a path with lots of php files
        private long totalBytesRead = 0L;
        private int threadsRunning = 0;
        private ConcurrentDictionary<string, string> debugHashes;
        public int MaxThreads
        {
            get => 1; // this.doSingleFile ? 1 : -1; // Math.Max(1, Environment.ProcessorCount * 2);
        }
        private ProgressBar? pb = null;

        private CompilationService? compilationService;

        public DebugAction(bool doBind = false)
        {
            this.doBind = doBind;
            Console.TreatControlCAsInput = false;
            Console.CancelKeyPress += new ConsoleCancelEventHandler(cancelHandler);

            if (this.checkHashes) {
                this.debugHashes = LoadHashes();
            } else {
                this.debugHashes = new ConcurrentDictionary<string, string>();
            }

            // Initialize compilation service
            this.compilationService = new CompilationService();
        }

        private void cancelHandler(object? sender, ConsoleCancelEventArgs e)
        {
            this.IsCancelled = true;
            e.Cancel = true;
        }

        static ConcurrentDictionary<string, string> LoadHashes()
        {
            if (File.Exists("debugHashes.txt")) {
                var lines = File.ReadAllLines("debugHashes.txt");
                var hashes = new ConcurrentDictionary<string, string>();
                foreach (var line in lines) {
                    var parts = line.Split(' ');
                    if (parts.Length == 2) {
                        hashes.TryAdd(parts[0].Replace("|||", " "), parts[1]);
                    }
                }
                return hashes;
            }

            return new ConcurrentDictionary<string, string>();
        }

        static void SaveHashes(ConcurrentDictionary<string, string> hashes)
        {
            File.WriteAllLines("debugHashes.txt", hashes.Select(h => $"{h.Key.Replace(" ", "|||")} {h.Value}"));
        }

        private void CompareOrUpdateHash(string filePath, string input, string addlText = "")
        {
            if (!this.checkHashes) {
                return;
            }
            var hash = Hash(input);
            if (this.debugHashes.TryGetValue(filePath, out var existingHash)) {
                if (existingHash != hash) {
                    Message.Error("CLI_HashMismatch", filePath, input, addlText);
                }
            } else {
                this.debugHashes.TryAdd(filePath, hash);
            }
        }

        static string Hash(string input)
            => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(input))).ToLower();

        public override CompilationResult? Start(CancellationToken cancellationToken)
        {
            this.CancelToken = cancellationToken;

            // Discover files using the existing WalkSync method
            var allFiles = WalkSync(directoryPath)
                .Where(f => (f.EndsWith(".php") || f.EndsWith(".tyhp") || f.EndsWith(".tyhpdef")) && !f.Contains("/stubs/"))
                .ToList();

            allFiles.ForEach(f => this.totalBytesRead += new FileInfo(f).Length);
            this.totalFilesCount = allFiles.Count;

            Message.Success("CLI_TotalFiles", this.totalFilesCount);
            Message.Success(
                "CLI_MaxConcurrentTasks",
                this.MaxThreads == -1
                    ? Message.Localize("CLI_Unlimited")
                    : this.MaxThreads.ToString("N0"));
            Message.Success("CLI_TotalFileSize", BytesToString(this.totalBytesRead));

            // Create compilation options from debug settings
            var compilationOptions = CompilationOptions.FromProject(Tyhp.Config.Project.Singleton, options =>
            {
                options.MaxThreads = this.MaxThreads;
                // EnableAstCache is derived from the project (--no-cache) in FromProject.
                options.ReportAmbiguities = this.reportAmbiguities;
                options.EnableProfiling = this.doThreadProfiling;
                options.GarbageCollectInterval = 1000;
                options.PreReadThreshold = 1024L * 1024L * 1024L;
                options.PreReadMinFiles = 1000;
            });

            // Set up progress reporting callback
            this.pb = new(PbStyle.SingleLine, this.totalFilesCount);
            compilationOptions.Progress = new Progress<CompilationProgress>(progress =>
            {
                var message = Message.Localize(
                    "CLI_ParsingProgress",
                    progress.FilesProcessed,
                    progress.TotalFiles,
                    progress.ErrorCount,
                    BytesToString(progress.MemoryUsage));
                this.pb?.Refresh(progress.FilesProcessed, message);
            });

            // Perform hash checking if enabled (debug-specific feature)
            // This is done before compilation if we need parse trees
            if (this.checkHashes || this.dumpCtxTree)
            {
                // For hash checking or tree dumping, we need to use the low-level parser
                // This is debug-specific functionality not part of the main compilation pipeline
                PerformDebugSpecificParsing(allFiles, cancellationToken);
            }

            // Call CompilationService to parse all files
            this.pb?.Refresh(0, Message.Localize("CLI_StartingCompilation"));
            var startTime = DateTime.Now;

            CompilationResult? result = null;
            try
            {
                result = this.compilationService!.ParseFiles(allFiles, compilationOptions, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                this.IsCancelled = true;
            }

            if (this.IsCancelled) {
                this.pb?.Refresh(0, Message.Localize("CLI_Cancelled"));
            } else {
                this.pb?.Refresh(this.totalFilesCount, Message.Localize("CLI_Done"));
            }
            Message.Display("");

            var endTime = DateTime.Now;
            var duration = (endTime - startTime).TotalSeconds;

            // Display parser profiling if enabled (debug-specific)
            if (this.doThreadProfiling && result != null)
            {
                int i = 0;
                foreach (var parser in this.compilationService!.GetThreadParsers())
                {
                    DisplayParserProfile(parser, i);
                    i++;
                }
            }

            // Display results
            if (result != null)
            {
                if (result.Diagnostics.HasErrors)
                {
                    result.Diagnostics.DisplayAll();
                }

                Message.Success("CLI_TimeTaken", duration);
                Message.Success("CLI_TotalFilesProcessed", result.ParsedFiles?.Count ?? 0);
                Message.Success("CLI_TotalErrors", result.Diagnostics.ErrorCount);
            }

            if (this.doBind && result?.GlobalScope != null)
            {
                Message.Display("");
                Message.Display("CLI_BinderDebugOutputHeader");
                Message.Display("CLI_BindDuration", result.BindDuration.TotalSeconds);
                Message.Display("CLI_FilesBoundDebug", result.GlobalScope.FileScopeCount);
                Message.Display("CLI_Namespaces", result.GlobalScope.NamespaceScopeCount);
                Message.Display("CLI_TotalSymbols", result.GlobalScope.TotalSymbolCount);
                Message.Display("CLI_TotalScopes", result.GlobalScope.TotalScopeCount);
                Message.Display("CLI_ErrorsDebug", result.Diagnostics.ErrorCount);
                Message.Display("CLI_WarningsDebug", result.Diagnostics.WarningCount);

                if (result.Diagnostics.HasErrors || result.Diagnostics.HasWarnings)
                {
                    result.Diagnostics.DisplayAll();
                }
            }
            else if (this.doBind && result != null)
            {
                Message.Info("CLI_BinderNoScopeTreeAbove");
            }
            else if (this.doBind)
            {
                Message.Info("CLI_CompilationNoResult");
            }

            if (this.checkHashes) {
                SaveHashes(this.debugHashes);
            }

            Message.Success(
                "CLI_PeakMemoryUsage",
                BytesToString(System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64));

            // Debug action does not produce a compilation result for the pipeline
            return null;
        }

        /// <summary>
        /// Performs debug-specific parsing for hash checking and tree dumping.
        /// This is separate from the main compilation pipeline.
        /// </summary>
        private void PerformDebugSpecificParsing(List<string> allFiles, CancellationToken cancellationToken)
        {
            var diagnostics = new DiagnosticBag();

            foreach (var filePath in allFiles)
            {
                if (cancellationToken.IsCancellationRequested || this.IsCancelled)
                {
                    break;
                }

                TyhpAntlrErrorListener<int>? lexerErrorListener = null;
                TyhpAntlrErrorListener<IToken>? parserErrorListener = null;

                try
                {
                    var (lexer, parser, lexerListener, parserListener) = GetLexerAndParser(filePath);
                    lexerErrorListener = lexerListener;
                    parserErrorListener = parserListener;

                    ParserRuleContext ctx;
                    if (filePath.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx = parser.tyhpdefSrcFile();
                    }
                    else if (filePath.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx = parser.tyhpSrcFile();
                    }
                    else
                    {
                        ctx = parser.phpSrcFile();
                    }

                    if (this.checkHashes)
                    {
                        this.CompareOrUpdateHash(filePath, ctx.ToStringTree(parser));
                    }

                    if (this.dumpCtxTree)
                    {
                        string tree = this.buildTree(ctx, parser);
                        Message.Debug(tree);
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.AddError(
                        MessageCode.ParserUnknownError,
                        filePath,
                        0,
                        0,
                        ex.Message);
                    Message.TyhpError(filePath, 0, 0, MessageCode.ParserUnknownError, ex.Message);
                }
                finally
                {
                    lexerErrorListener?.Dispose();
                    parserErrorListener?.Dispose();
                }
            }

            // Display diagnostics collected during debug parsing
            if (diagnostics.HasErrors)
            {
                Message.Display("");
                Message.Error("CLI_DebugParsingErrors");
                diagnostics.DisplayAll();
            }
        }

        private static string BytesToString(long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            if (byteCount == 0)
                return "0" + suf[0];
            long bytes = Math.Abs(byteCount);
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString() + suf[place];
        }

        private string buildTree(IParseTree ctx, TyhpParser parser, int depth = 0)
        {
            var sb = new StringBuilder();
            this.buildTreeInternal(ctx, parser, sb, depth);
            return sb.ToString();
        }

        private void buildTreeInternal(IParseTree ctx, TyhpParser parser, StringBuilder sb, int depth = 0)
        {
            string indent = new string(' ', depth * 4);

            string text = "";
            string name;
            if (ctx is ITerminalNode terminalNode) {
                text = terminalNode.Symbol.Text;
                name = parser.Vocabulary.GetDisplayName(terminalNode.Symbol.Type);
            } else {
                name = ctx.GetType().Name;
            }
            sb.Append(indent).Append(name);
            if (!String.IsNullOrEmpty(text)) {
                sb.Append($" \"{text.Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t")}\"");
            }
            sb.Append(" {");
            for (int i = 0; i < ctx.ChildCount; i++) {
                var child = ctx.GetChild(i);
                sb.Append('\n');
                this.buildTreeInternal(child, parser, sb, depth + 1);
            }
            if (ctx.ChildCount > 0) {
                sb.Append('\n').Append(indent);
            }
            sb.Append('}');
        }

        protected (TyhpLexer, TyhpParser, TyhpAntlrErrorListener<int>, TyhpAntlrErrorListener<IToken>) GetLexerAndParser(string? fileName = null)
        {
            var diagnostics = new DiagnosticBag();

            AntlrInputStream inputStream;

            if (fileName == null) {
                inputStream = new AntlrInputStream(String.Empty);
            } else {
                using StreamReader streamReader = new StreamReader(fileName, Encoding.UTF8, true, 4096);
                inputStream = new AntlrInputStream(streamReader);
            }

            var lexer = new TyhpLexer(inputStream);
            lexer.RemoveErrorListeners();
            var lexerErrorListener = new TyhpAntlrErrorListener<int>(diagnostics);
            lexer.AddErrorListener(lexerErrorListener);

            var parser = new TyhpParser(new CommonTokenStream(lexer));
            parser.RemoveErrorListeners();
            var parserErrorListener = new TyhpAntlrErrorListener<IToken>(diagnostics);
            parser.AddErrorListener(parserErrorListener);
            if (this.reportAmbiguities) {
                parser.AddErrorListener(new DiagnosticErrorListener());
                parser.Interpreter.PredictionMode = PredictionMode.LL_EXACT_AMBIG_DETECTION;
            }

            parser.Profile = this.doThreadProfiling;

            lexerErrorListener.SetFileName(fileName ?? "");
            parserErrorListener.SetFileName(fileName ?? "");

            return (lexer, parser, lexerErrorListener, parserErrorListener);
        }

        private void DisplayParserProfile(TyhpParser parser, int threadId)
        {
            string profileOutput = $"Thread {threadId} parser profile:\n";
            string headerLine = "| rule".PadRight(66) + "| " +
                "pred. time".PadRight(15) + "| " +
                "invocat".PadRight(7) + "| " +
                "SSL LA".PadRight(7) + "| " +
                "SSL +LA".PadRight(7) + "| " +
                "LL LA".PadRight(7) + "| " +
                "LL +LA".PadRight(7) + "| " +
                "LL FB".PadRight(7) + "| " +
                "ambig".PadRight(7) + "| " +
                "errors".PadRight(7) + "| " +
                "sens.".PadRight(7) + "| " +
                "prdeval".PadRight(7)+"|";
            profileOutput += headerLine + "\n";
            profileOutput += new string('=', headerLine.Length) + "\n";

            long totalTime = parser.ParseInfo?.getTotalTimeInPrediction() ?? 0L;


            foreach (var decisionInfo in parser.ParseInfo?.getDecisionInfo() ?? Array.Empty<DecisionInfo>()) {
                var ds = parser.Atn.GetDecisionState(decisionInfo.decision);
                var rule = parser.RuleNames[ds.ruleIndex] + "(" + ds.ruleIndex + ":" + ds.decision + ":" + (decisionInfo.SLL_MaxLookEvent?.predictedAlt ?? -1) + ":" + (decisionInfo.LL_MaxLookEvent?.predictedAlt ?? -1) + ")";
                if (this.doSingleFile || decisionInfo.timeInPrediction >= (10000 * 500) /* 500ms */) {
                    string line = $"|{rule.PadRight(65)}" + "| " +
                        $"{DisplayNumber(decisionInfo.timeInPrediction / 10000.0, 13)}ms" + "| " +
                        $"{DisplayNumber(decisionInfo.invocations, 7)}" + "| " +
                        $"{DisplayNumber(decisionInfo.SLL_TotalLook, 7)}" + "| " +
                        $"{DisplayNumber(decisionInfo.SLL_MaxLook, 7)}" + "| " +
                        $"{DisplayNumber(decisionInfo.LL_TotalLook, 7)}" + "| " +
                        $"{DisplayNumber(decisionInfo.LL_MaxLook, 7)}" + "| " +
                        $"{DisplayNumber(decisionInfo.LL_Fallback, 7)}" + "| " +
                        $"{DisplayNumber(decisionInfo.ambiguities.Count, 7)}" + "| " +
                        $"{DisplayNumber(decisionInfo.errors.Count, 7)}" + "| " +
                        $"{DisplayNumber(decisionInfo.contextSensitivities.Count, 7)}" + "| " +
                        $"{DisplayNumber(decisionInfo.predicateEvals.Count, 7)}" + "|";
                    profileOutput += line + "\n";
                }
            }

            profileOutput += $"Total time: {DisplayNumber(totalTime / 10000.0, 13)}ms\n";
            Message.Debug(profileOutput);
        }

        private string DisplayNumber(int number, int padLeft)
        {
            var numString = number != 0 ? number.ToString() : "";
            return numString.PadLeft(padLeft);
        }

        private string DisplayNumber(long number, int padLeft)
        {
            var numString = number != 0L ? number.ToString() : "";
            return numString.PadLeft(padLeft);
        }

        private string DisplayNumber(double number, int padLeft)
        {
            var numString = number.ToString("F2");
            return numString.PadLeft(padLeft);
        }

        private IEnumerable<string> WalkSync(string dir)
        {
            if (doSingleFile)
            {
                yield return specialDebugFile;
                yield break;
            }

            foreach (var file in Directory.GetFiles(dir))
            {
                yield return file;
            }

            foreach (var directory in Directory.GetDirectories(dir))
            {
                foreach (var file in WalkSync(directory))
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        /// Disposes of managed resources including the CompilationService.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.compilationService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
