namespace Tyhp.LanguageServer.Analysis
{
    using System.Collections.Concurrent;
    using Microsoft.Extensions.Configuration;
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Tyhp.CLI;
    using Tyhp.Config;
    using Tyhp.Domain.Diagnostics;
    using Tyhp.Domain.Exceptions;
    using Tyhp.Domain.Services;
    using Tyhp.LanguageServer.Configuration;
    using Tyhp.LanguageServer.Handlers;
    using Tyhp.LanguageServer.Workspace;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder;
    using Tyhp.TyhpLang.Binder.Scopes;
    using Tyhp.TyhpLang.Checker;

    /// <summary>
    /// Coordinates parse/bind/check for language-server documents and publishes diagnostics.
    /// </summary>
    public sealed class AnalysisService : IDisposable
    {
        private static readonly StringComparer PathComparer =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private readonly WorkspaceManager _workspace;
        private readonly CompilationService _compilation;
        private readonly DiagnosticsPublisher _publisher;
        private readonly ServerConfiguration _configuration;
        private readonly SymbolFinder _symbolFinder;
        private readonly ConcurrentDictionary<string, SrcFileAst> _projectAsts = new(PathComparer);
        private readonly SemaphoreSlim _analysisGate = new(1, 1);
        private readonly object _scopeLock = new();
        private readonly List<FileSystemWatcher> _watchers = [];
        private readonly ConcurrentDictionary<string, byte> _pendingDiskChanges = new(PathComparer);
        private GlobalScope? _globalScope;
        private SymbolTree? _symbolTree;
        private IReadOnlyDictionary<IBase2Ast, ICheckedType>? _expressionTypes;
        private IReadOnlyDictionary<IBase2Ast, ICheckedType>? _narrowedTypes;
        private Project? _project;
        private CompilationOptions _options;
        private CancellationTokenSource? _diskDebounce;
        private int _disposed;
        private Action<MessageType, string>? _log;

        public AnalysisService(
            WorkspaceManager workspace,
            CompilationService compilation,
            DiagnosticsPublisher publisher,
            ServerConfiguration configuration,
            SymbolFinder symbolFinder)
        {
            this._workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            this._compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
            this._publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
            this._configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            this._symbolFinder = symbolFinder ?? throw new ArgumentNullException(nameof(symbolFinder));
            this._project = configuration.Project;
            this._options = CloneOptions(configuration.CompilationOptions);
        }

        /// <summary>Latest project-wide scope from the last successful bind, or null.</summary>
        public GlobalScope? GetGlobalScope()
        {
            lock (this._scopeLock)
            {
                return this._globalScope;
            }
        }

        /// <summary>Latest symbol tree from the last successful bind, or null.</summary>
        internal SymbolTree? GetSymbolTree()
        {
            lock (this._scopeLock)
            {
                return this._symbolTree;
            }
        }

        /// <summary>
        /// Checker-inferred type for <paramref name="node"/> from the last analysis cycle,
        /// preferring a narrowed type when both are present.
        /// </summary>
        internal ICheckedType? GetInferredType(IBase2Ast node)
        {
            ArgumentNullException.ThrowIfNull(node);
            lock (this._scopeLock)
            {
                if (this._narrowedTypes is not null
                    && this._narrowedTypes.TryGetValue(node, out ICheckedType? narrowed))
                {
                    return narrowed;
                }

                if (this._expressionTypes is not null
                    && this._expressionTypes.TryGetValue(node, out ICheckedType? inferred))
                {
                    return inferred;
                }
            }

            return null;
        }

        /// <summary>
        /// Parsed project ASTs (disk cache plus open documents) used for
        /// project-wide queries such as find-references.
        /// </summary>
        public IReadOnlyList<SrcFileAst> GetProjectAsts()
        {
            return this.CollectBindAsts(this._workspace.GetAllDocuments());
        }

        /// <summary>Symbol finder shared with later feature handlers.</summary>
        internal SymbolFinder SymbolFinder => this._symbolFinder;

        /// <summary>Optional LSP logger (window/logMessage).</summary>
        internal Action<MessageType, string>? Log
        {
            get => this._log;
            set => this._log = value;
        }

        /// <summary>
        /// True when the document at <paramref name="uri"/> is a Tyhp or tyhpdef file.
        /// PHP and unknown files are not analyzed.
        /// </summary>
        public bool IsAnalyzableUri(Uri uri)
        {
            DocumentState? state = this._workspace.GetDocument(uri);
            if (state is not null)
            {
                return IsAnalyzableLanguage(state.LanguageMode);
            }

            string path = WorkspaceManager.ResolveFilePath(uri);
            return IsAnalyzableLanguage(WorkspaceManager.DetectLanguageMode(path));
        }

        /// <summary>True for <c>tyhp</c> and <c>tyhpdef</c> language modes.</summary>
        public static bool IsAnalyzableLanguage(string languageMode)
        {
            return string.Equals(languageMode, WorkspaceManager.LanguageModeTyhp, StringComparison.Ordinal)
                || string.Equals(languageMode, WorkspaceManager.LanguageModeTyhpdef, StringComparison.Ordinal);
        }

        /// <summary>
        /// Parses project files from disk (plus any already-open documents) to seed the
        /// AST cache, then starts watching the workspace for non-open file changes.
        /// </summary>
        public async Task ScanWorkspaceAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            await this._analysisGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                this.RefreshProject();
                await this.ScanProjectFilesAsync(cancellationToken).ConfigureAwait(false);
                this.StartWatchers();
            }
            finally
            {
                this._analysisGate.Release();
            }
        }

        /// <summary>
        /// Analyzes every open Tyhp/tyhpdef document (used after initialize / project reload).
        /// </summary>
        public async Task AnalyzeAllAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            await this._analysisGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await this.RunAnalysisCycleAsync(changedUri: null, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.ReportAnalysisFailure(uri: null, ex);
            }
            finally
            {
                this._analysisGate.Release();
            }
        }

        /// <summary>
        /// Re-parses <paramref name="uri"/> (when open), rebuilds the global scope from
        /// cached project ASTs plus open documents, re-checks open documents, and publishes
        /// diagnostics.
        /// </summary>
        public async Task AnalyzeDocumentAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(uri);
            this.ThrowIfDisposed();

            if (!this.IsAnalyzableUri(uri))
            {
                return;
            }

            await this._analysisGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await this.RunAnalysisCycleAsync(uri, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.ReportAnalysisFailure(uri, ex);
            }
            finally
            {
                this._analysisGate.Release();
            }
        }

        /// <summary>
        /// Logs and records an analysis failure without crashing the server.
        /// </summary>
        internal void ReportAnalysisFailure(Uri? uri, Exception exception)
        {
            string detail = $"{exception.GetType().Name}: {exception.Message}";
            string message = Message.LocalizeErrorCode((int)MessageCode.LspAnalysisError, detail);
            this._log?.Invoke(MessageType.Error, message);

            if (uri is not null && this._configuration.EnableDiagnostics)
            {
                DocumentState? state = this._workspace.GetDocument(uri);
                string fileName = state?.FilePath ?? uri.ToString();
                var diagnostic = Tyhp.Domain.Diagnostics.Diagnostic.Error(
                    MessageCode.LspAnalysisError,
                    fileName,
                    1,
                    0,
                    [detail]);
                this._publisher.PublishDiagnostics(uri, [diagnostic]);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref this._disposed, 1) != 0)
            {
                return;
            }

            this.StopWatchers();
            CancellationTokenSource? diskDebounce = Interlocked.Exchange(ref this._diskDebounce, null);
            if (diskDebounce is not null)
            {
                try
                {
                    diskDebounce.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                diskDebounce.Dispose();
            }

            this._analysisGate.Dispose();
            this._projectAsts.Clear();
        }

        private async Task RunAnalysisCycleAsync(Uri? changedUri, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.RefreshProject();

            DocumentState? changed = changedUri is null ? null : this._workspace.GetDocument(changedUri);
            if (changedUri is not null && changed is null)
            {
                return;
            }

            if (changed is not null && !IsAnalyzableLanguage(changed.LanguageMode))
            {
                return;
            }

            CompilationOptions parseOptions = this.CreateParseOptions();
            IReadOnlyList<DocumentState> openDocs = this._workspace.GetAllDocuments()
                .Where(document => IsAnalyzableLanguage(document.LanguageMode))
                .ToList();

            var diagnostics = new DiagnosticBag();
            if (changed is not null)
            {
                this.ParseOpenDocument(changed, parseOptions, diagnostics, cancellationToken);
            }

            foreach (DocumentState document in openDocs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (changed is not null && ReferenceEquals(document, changed))
                {
                    continue;
                }

                bool needsParse;
                lock (document.SyncRoot)
                {
                    needsParse = document.ParsedAst is null || document.IsDirty;
                }

                if (needsParse)
                {
                    this.ParseOpenDocument(document, parseOptions, diagnostics, cancellationToken);
                }
                else
                {
                    // Not re-parsed this cycle, so ParseFromContent won't re-report its syntax
                    // errors. Re-add the diagnostics from its last real parse so a cycle triggered
                    // by a different document doesn't silently clear this one's error squiggles.
                    lock (document.SyncRoot)
                    {
                        diagnostics.AddRange(document.ParseDiagnostics);
                    }
                }
            }

            List<SrcFileAst> allAsts = this.CollectBindAsts(openDocs);
            GlobalScope? scope = null;
            SymbolTree? tree = null;
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? expressionTypes = null;
            IReadOnlyDictionary<IBase2Ast, ICheckedType>? narrowedTypes = null;

            if (allAsts.Count > 0)
            {
                try
                {
                    var binder = new TyhpBinder(diagnostics, this._options);
                    scope = binder.Bind(allAsts);
                    if (scope is not null)
                    {
                        tree = new SymbolTree(scope);
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                {
                    diagnostics.AddError(
                        MessageCode.LspAnalysisError,
                        changed?.FilePath ?? "<workspace>",
                        0,
                        0,
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            if (scope is not null && tree is not null)
            {
                try
                {
                    var checker = new TyhpChecker(diagnostics, tree, scope, this._options.Checker);
                    IEnumerable<SrcFileAst> checkTargets = openDocs
                        .Select(document =>
                        {
                            lock (document.SyncRoot)
                            {
                                return document.ParsedAst;
                            }
                        })
                        .Where(ast => ast is not null)
                        .Cast<SrcFileAst>();
                    checker.Check(checkTargets);
                    expressionTypes = new Dictionary<IBase2Ast, ICheckedType>(checker.ExpressionTypes);
                    narrowedTypes = new Dictionary<IBase2Ast, ICheckedType>(checker.NarrowedTypes);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
                {
                    diagnostics.AddError(
                        MessageCode.CheckerUnknownError,
                        changed?.FilePath ?? "<workspace>",
                        0,
                        0,
                        $"Checker invocation failed: {ex.GetType().Name} - {ex.Message}");
                }
            }

            lock (this._scopeLock)
            {
                if (scope is not null)
                {
                    this._globalScope = scope;
                    this._symbolTree = tree;
                    this._expressionTypes = expressionTypes;
                    this._narrowedTypes = narrowedTypes;
                }
            }

            Dictionary<DocumentState, List<IDiagnostic>> byDocument =
                GroupDiagnosticsByDocument(diagnostics.All, openDocs, changed);

            DateTime analyzedAt = DateTime.UtcNow;
            foreach (DocumentState document in openDocs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // The document may have closed (or been replaced by a newer didOpen) while this
                // cycle was running. didClose already published an empty diagnostics list for it;
                // publishing again here would resurrect diagnostics for a file the client no
                // longer has open.
                if (!ReferenceEquals(this._workspace.GetDocument(document.Uri), document))
                {
                    continue;
                }

                IReadOnlyList<IDiagnostic> forDocument = byDocument.TryGetValue(document, out List<IDiagnostic>? list)
                    ? list
                    : [];
                lock (document.SyncRoot)
                {
                    document.Diagnostics = forDocument;
                    document.IsDirty = false;
                    document.LastAnalysisTime = analyzedAt;
                }

                this._publisher.PublishDiagnostics(document.Uri, forDocument);
            }
        }

        /// <summary>
        /// Notifies the analysis service that <paramref name="uri"/> closed. If the file still
        /// exists on disk, re-parses it so the project-wide AST cache reflects the last saved
        /// content instead of a discarded in-memory buffer (unsaved edits must not keep
        /// influencing other files' cross-file diagnostics after the buffer is gone); otherwise
        /// removes it from the cache. Then re-runs the analysis cycle so remaining open documents
        /// see the corrected state.
        /// </summary>
        public void NotifyDocumentClosed(Uri uri, string filePath)
        {
            ArgumentNullException.ThrowIfNull(uri);
            ArgumentNullException.ThrowIfNull(filePath);
            if (this._disposed != 0 || !IsAnalyzableLanguage(WorkspaceManager.DetectLanguageMode(filePath)))
            {
                return;
            }

            _ = this.RefreshAfterCloseAsync(uri, filePath);
        }

        private async Task RefreshAfterCloseAsync(Uri uri, string filePath)
        {
            try
            {
                await this._analysisGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (File.Exists(filePath))
                    {
                        this.ParseDiskFile(filePath, CancellationToken.None);
                    }
                    else
                    {
                        this._projectAsts.TryRemove(CanonicalPath(filePath), out _);
                    }

                    await this.RunAnalysisCycleAsync(changedUri: null, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    this._analysisGate.Release();
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.ReportAnalysisFailure(uri, ex);
            }
        }

        private void ParseOpenDocument(
            DocumentState document,
            CompilationOptions parseOptions,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content;
            string filePath;
            CancellationTokenSource analysisCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            document.CancelAnalysis();
            lock (document.SyncRoot)
            {
                document.AnalysisCancellation = analysisCts;
                content = document.Content;
                filePath = document.FilePath;
            }

            if (!ReferenceEquals(this._workspace.GetDocument(document.Uri), document))
            {
                // Closed (or replaced by a newer didOpen) between the workspace snapshot taken
                // at the start of this cycle and the read above. WorkspaceManager.CloseDocument
                // clears Content under this same document lock, so without this check a close
                // racing this parse would write an empty-buffer AST into the shared project
                // cache under the file's real path and corrupt cross-file diagnostics for other
                // open documents until the next edit or disk change touches this path again.
                analysisCts.Dispose();
                return;
            }

            var parseDiagnostics = new DiagnosticBag();
            SrcFileAst? ast;
            try
            {
                ast = this._compilation.ParseFromContent(content, filePath, parseDiagnostics, parseOptions);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                parseDiagnostics.AddError(
                    MessageCode.LspAnalysisError,
                    filePath,
                    0,
                    0,
                    $"{ex.GetType().Name}: {ex.Message}");
                ast = null;
            }

            diagnostics.AddRange(parseDiagnostics.All);
            lock (document.SyncRoot)
            {
                document.ParsedAst = ast;
                document.ParseDiagnostics = parseDiagnostics.All;
            }

            string cacheKey = CanonicalPath(filePath);
            if (ast is not null)
            {
                this._projectAsts[cacheKey] = ast;
            }
            else
            {
                this._projectAsts.TryRemove(cacheKey, out _);
            }
        }

        private List<SrcFileAst> CollectBindAsts(IReadOnlyList<DocumentState> openDocs)
        {
            var byPath = new Dictionary<string, SrcFileAst>(PathComparer);
            foreach (KeyValuePair<string, SrcFileAst> pair in this._projectAsts)
            {
                byPath[pair.Key] = pair.Value;
            }

            foreach (DocumentState document in openDocs)
            {
                SrcFileAst? ast;
                lock (document.SyncRoot)
                {
                    ast = document.ParsedAst;
                }

                if (ast is not null)
                {
                    byPath[CanonicalPath(document.FilePath)] = ast;
                }
            }

            return [.. byPath.Values];
        }

        private async Task ScanProjectFilesAsync(CancellationToken cancellationToken)
        {
            IReadOnlyCollection<string> files = this.CollectProjectSourceFiles();
            HashSet<string> openPaths = this._workspace.GetAllDocuments()
                .Select(document => CanonicalPath(document.FilePath))
                .ToHashSet(PathComparer);

            int dop = Math.Max(1, this._configuration.MaxConcurrentAnalysis);
            using var parseGate = new SemaphoreSlim(dop, dop);
            var tasks = new List<Task>();

            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string canonical = CanonicalPath(file);
                if (openPaths.Contains(canonical))
                {
                    continue;
                }

                if (!IsAnalyzableLanguage(WorkspaceManager.DetectLanguageMode(file)))
                {
                    continue;
                }

                await parseGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                tasks.Add(Task.Run(
                    () =>
                    {
                        try
                        {
                            this.ParseDiskFile(file, cancellationToken);
                        }
                        finally
                        {
                            parseGate.Release();
                        }
                    },
                    cancellationToken));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        private void ParseDiskFile(string filePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string canonical = CanonicalPath(filePath);
            string content;
            try
            {
                content = File.ReadAllText(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this._projectAsts.TryRemove(canonical, out _);
                this._log?.Invoke(
                    MessageType.Warning,
                    Message.Localize("CLI_LanguageServerWorkspaceScanFailed", ex.GetType().Name, ex.Message));
                return;
            }

            var diagnostics = new DiagnosticBag();
            SrcFileAst? ast = this._compilation.ParseFromContent(
                content,
                filePath,
                diagnostics,
                this.CreateParseOptions());
            if (ast is not null)
            {
                this._projectAsts[canonical] = ast;
            }
            else
            {
                this._projectAsts.TryRemove(canonical, out _);
            }
        }

        private IReadOnlyCollection<string> CollectProjectSourceFiles()
        {
            var files = new HashSet<string>(PathComparer);
            try
            {
                if (this._project is not null)
                {
                    foreach (string path in this._project.GetProjectSourceFiles())
                    {
                        files.Add(CanonicalPath(path));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                this._log?.Invoke(
                    MessageType.Warning,
                    Message.Localize("CLI_LanguageServerWorkspaceScanFailed", ex.GetType().Name, ex.Message));
            }

            foreach (DocumentState document in this._workspace.GetAllDocuments())
            {
                if (IsAnalyzableLanguage(document.LanguageMode))
                {
                    files.Add(CanonicalPath(document.FilePath));
                }
            }

            return files;
        }

        private void RefreshProject()
        {
            if (this._configuration.Project is not null)
            {
                this._project = this._configuration.Project;
                this._options = CloneOptions(this._configuration.CompilationOptions);
                return;
            }

            string? projectFile = this.ResolveProjectFilePath();
            if (string.IsNullOrEmpty(projectFile) || !File.Exists(projectFile))
            {
                return;
            }

            try
            {
                IConfiguration configuration = new ConfigurationBuilder()
                    .AddJsonFile(projectFile, optional: false, reloadOnChange: false)
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["*project_file_path"] = projectFile,
                    })
                    .Build();
                this._project = new Project(configuration);
                this._options = CompilationOptions.FromProject(this._project);
                this._configuration.Project = this._project;
                this._configuration.TyhpProjectPath = projectFile;
                this._configuration.CompilationOptions = this._options;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this._log?.Invoke(
                    MessageType.Warning,
                    Message.Localize("CLI_LanguageServerWorkspaceScanFailed", ex.GetType().Name, ex.Message));
            }
        }

        private string? ResolveProjectFilePath()
        {
            string? configured = this._configuration.TyhpProjectPath;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (File.Exists(configured)
                    && configured.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    return configured;
                }

                string nested = Path.Combine(configured, "tyhp.json");
                if (File.Exists(nested))
                {
                    return nested;
                }
            }

            string? root = this._workspace.WorkspaceRoot;
            if (!string.IsNullOrWhiteSpace(root))
            {
                string nested = Path.Combine(root, "tyhp.json");
                if (File.Exists(nested))
                {
                    return nested;
                }
            }

            return this._project?.GetConfigValue("*project_file_path");
        }

        private CompilationOptions CreateParseOptions()
        {
            CompilationOptions options = CloneOptions(this._options);
            options.EnableAstCache = false;
            options.SkipChecking = true;
            return options;
        }

        private void StartWatchers()
        {
            this.StopWatchers();
            string? root = this._workspace.WorkspaceRoot;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                string? projectPath = this._project?.GetProjectPath();
                root = !string.IsNullOrWhiteSpace(projectPath) && Directory.Exists(projectPath)
                    ? projectPath
                    : null;
            }

            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.CreationTime
                        | NotifyFilters.Size
                        | NotifyFilters.DirectoryName,
                    EnableRaisingEvents = true,
                };
                watcher.Changed += this.OnDiskChange;
                watcher.Created += this.OnDiskChange;
                watcher.Deleted += this.OnDiskChange;
                watcher.Renamed += this.OnDiskRename;
                this._watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                this._log?.Invoke(
                    MessageType.Warning,
                    Message.Localize("CLI_LanguageServerWorkspaceScanFailed", ex.GetType().Name, ex.Message));
            }
        }

        private void StopWatchers()
        {
            foreach (FileSystemWatcher watcher in this._watchers)
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Changed -= this.OnDiskChange;
                    watcher.Created -= this.OnDiskChange;
                    watcher.Deleted -= this.OnDiskChange;
                    watcher.Renamed -= this.OnDiskRename;
                    watcher.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            this._watchers.Clear();
        }

        private void OnDiskRename(object sender, RenamedEventArgs e)
        {
            this.HandleDiskPath(e.OldFullPath);
            this.HandleDiskPath(e.FullPath);
        }

        private void OnDiskChange(object sender, FileSystemEventArgs e)
            => this.HandleDiskPath(e.FullPath);

        private void HandleDiskPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || this._disposed != 0)
            {
                return;
            }

            string name = Path.GetFileName(path);
            bool isProjectConfig = string.Equals(name, "tyhp.json", StringComparison.OrdinalIgnoreCase);
            bool isSource = path.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase);
            if (!isProjectConfig && !isSource)
            {
                return;
            }

            if (!isProjectConfig)
            {
                string canonical = CanonicalPath(path);
                foreach (DocumentState document in this._workspace.GetAllDocuments())
                {
                    if (PathComparer.Equals(CanonicalPath(document.FilePath), canonical))
                    {
                        return;
                    }
                }
            }

            this._pendingDiskChanges[CanonicalPath(path)] = 0;
            CancellationTokenSource cts = new();
            CancellationTokenSource? previous = Interlocked.Exchange(ref this._diskDebounce, cts);
            if (previous is not null)
            {
                try
                {
                    previous.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                previous.Dispose();
            }

            _ = this.DebouncedDiskReloadAsync(cts);
        }

        private async Task DebouncedDiskReloadAsync(CancellationTokenSource cts)
        {
            try
            {
                int delay = Math.Max(0, this._configuration.DebounceDelay);
                if (delay > 0)
                {
                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                }

                List<string> changed = [.. this._pendingDiskChanges.Keys];
                this._pendingDiskChanges.Clear();

                bool configChanged = changed.Exists(path =>
                    string.Equals(Path.GetFileName(path), "tyhp.json", StringComparison.OrdinalIgnoreCase));
                if (configChanged)
                {
                    this._configuration.Project = null;
                }

                await this._analysisGate.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    this.RefreshProject();
                    if (configChanged)
                    {
                        this._projectAsts.Clear();
                        await this.ScanProjectFilesAsync(cts.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        foreach (string path in changed)
                        {
                            if (File.Exists(path))
                            {
                                this.ParseDiskFile(path, cts.Token);
                            }
                            else
                            {
                                this._projectAsts.TryRemove(CanonicalPath(path), out _);
                            }
                        }
                    }

                    await this.RunAnalysisCycleAsync(changedUri: null, cts.Token).ConfigureAwait(false);
                }
                finally
                {
                    this._analysisGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.ReportAnalysisFailure(uri: null, ex);
            }
            finally
            {
                Interlocked.CompareExchange(ref this._diskDebounce, null, cts);
                cts.Dispose();
            }
        }

        /// <summary>
        /// Assigns every diagnostic to at most one open document. Diagnostics are matched by
        /// full (canonicalized) path first; the bare-filename fallback below is only used — and
        /// only when it is unambiguous — so that two open files sharing a name in different
        /// folders (e.g. <c>src/foo/Utils.tyhp</c> and <c>src/bar/Utils.tyhp</c>) never both
        /// receive the same diagnostic.
        /// </summary>
        private static Dictionary<DocumentState, List<IDiagnostic>> GroupDiagnosticsByDocument(
            IReadOnlyList<IDiagnostic> all,
            IReadOnlyList<DocumentState> openDocs,
            DocumentState? trigger)
        {
            var byPath = new Dictionary<string, DocumentState>(PathComparer);
            var byFileName = new Dictionary<string, List<DocumentState>>(PathComparer);
            var result = new Dictionary<DocumentState, List<IDiagnostic>>();
            foreach (DocumentState document in openDocs)
            {
                result[document] = [];
                byPath.TryAdd(CanonicalPath(document.FilePath), document);
                byPath.TryAdd(document.FilePath, document);

                string fileName = Path.GetFileName(document.FilePath);
                if (string.IsNullOrEmpty(fileName))
                {
                    continue;
                }

                if (!byFileName.TryGetValue(fileName, out List<DocumentState>? matches))
                {
                    matches = [];
                    byFileName[fileName] = matches;
                }

                matches.Add(document);
            }

            foreach (IDiagnostic diagnostic in all)
            {
                DocumentState? target = ResolveDocumentForDiagnostic(diagnostic, byPath, byFileName, trigger);
                if (target is not null && result.TryGetValue(target, out List<IDiagnostic>? bucket))
                {
                    bucket.Add(diagnostic);
                }
            }

            return result;
        }

        private static DocumentState? ResolveDocumentForDiagnostic(
            IDiagnostic diagnostic,
            Dictionary<string, DocumentState> byPath,
            Dictionary<string, List<DocumentState>> byFileName,
            DocumentState? trigger)
        {
            string name = diagnostic.FileName ?? string.Empty;
            if (string.IsNullOrEmpty(name)
                || name is "<input>" or "<resolution>" or "<unknown>" or "<workspace>")
            {
                return trigger;
            }

            if (byPath.TryGetValue(name, out DocumentState? exact))
            {
                return exact;
            }

            try
            {
                if (byPath.TryGetValue(CanonicalPath(name), out DocumentState? canonical))
                {
                    return canonical;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }

            // No document's path matches: fall back to a filename-only match, but only when it
            // is unambiguous (exactly one open document shares that filename).
            string fileName = Path.GetFileName(name);
            if (!string.IsNullOrEmpty(fileName)
                && byFileName.TryGetValue(fileName, out List<DocumentState>? matches)
                && matches.Count == 1)
            {
                return matches[0];
            }

            return null;
        }

        private static CompilationOptions CloneOptions(CompilationOptions? source)
        {
            source ??= new CompilationOptions { EnableAstCache = false };
            return new CompilationOptions
            {
                MaxThreads = source.MaxThreads,
                EnableAstCache = source.EnableAstCache,
                ReportAmbiguities = source.ReportAmbiguities,
                EnableProfiling = source.EnableProfiling,
                PhpVersion = source.PhpVersion,
                TyhpdefIncludePaths = source.TyhpdefIncludePaths,
                TyhpdefExcludePaths = source.TyhpdefExcludePaths,
                ProjectPath = source.ProjectPath,
                Tagless = source.Tagless,
                Checker = source.Checker,
                SkipChecking = source.SkipChecking,
                GarbageCollectInterval = source.GarbageCollectInterval,
                PreReadThreshold = source.PreReadThreshold,
                PreReadMinFiles = source.PreReadMinFiles,
            };
        }

        private static string CanonicalPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (
                ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or IOException)
            {
                return path;
            }
        }

        private void ThrowIfDisposed()
            => ObjectDisposedException.ThrowIf(this._disposed != 0, this);
    }
}
