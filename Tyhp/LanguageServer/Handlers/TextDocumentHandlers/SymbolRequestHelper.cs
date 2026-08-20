namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.CLI;
    using Tyhp.Domain.Services;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.LanguageServer.Workspace;
    using Tyhp.TyhpLang.Ast;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using Tyhp.TyhpLang.Binder.Symbols;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// Shared lookup used by definition and hover handlers.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Ensures the document is analyzed, then resolves the symbol under the LSP cursor.
        /// Returns null when the URI is unknown, not Tyhp, or has no semantic symbol.
        /// </summary>
        private async Task<(DocumentState State, SymbolLookupResult Lookup)?> LookupSymbolAsync(
            TextDocumentPositionParams? arg,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (arg?.TextDocument?.Uri is null || arg.Position is null)
            {
                return null;
            }

            Uri uri = arg.TextDocument.Uri;
            DocumentState? state = await this.EnsureAnalyzedAsync(uri, cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                return null;
            }

            SrcFileAst? ast;
            lock (state.SyncRoot)
            {
                ast = state.ParsedAst;
            }

            if (ast is null)
            {
                return null;
            }

            var (line, column) = PositionUtilities.FromLspPosition(arg.Position);
            SymbolLookupResult? lookup = this._symbolFinder.LookupAtPosition(
                ast,
                this._analysis.GetGlobalScope(),
                this._analysis.GetSymbolTree(),
                line,
                column);
            if (lookup is null)
            {
                return null;
            }

            return (state, lookup);
        }

        /// <summary>
        /// Analyzes <paramref name="uri"/> immediately when it is dirty or has no AST yet.
        /// </summary>
        private async Task<DocumentState?> EnsureAnalyzedAsync(Uri uri, CancellationToken cancellationToken)
        {
            DocumentState? state = this._workspace.GetDocument(uri);
            if (state is null)
            {
                return null;
            }

            if (!AnalysisService.IsAnalyzableLanguage(state.LanguageMode))
            {
                return state;
            }

            bool needsAnalysis;
            lock (state.SyncRoot)
            {
                needsAnalysis = state.ParsedAst is null || state.IsDirty || state.LastAnalysisTime is null;
            }

            if (needsAnalysis)
            {
                this._incrementalAnalyzer.Cancel(uri);
                await this._analysis.AnalyzeDocumentAsync(uri, cancellationToken).ConfigureAwait(false);
                state = this._workspace.GetDocument(uri);
            }

            return state;
        }

        /// <summary>
        /// Analyzes <paramref name="uri"/> and returns the parsed AST plus content when
        /// the document is a Tyhp/tyhpdef file that parsed successfully.
        /// </summary>
        private async Task<(DocumentState State, SrcFileAst Ast, string Content)?> GetAnalyzedAstAsync(
            Uri uri,
            CancellationToken cancellationToken)
        {
            DocumentState? state = await this.EnsureAnalyzedAsync(uri, cancellationToken).ConfigureAwait(false);
            if (state is null || !AnalysisService.IsAnalyzableLanguage(state.LanguageMode))
            {
                return null;
            }

            SrcFileAst? ast;
            string content;
            lock (state.SyncRoot)
            {
                ast = state.ParsedAst;
                content = state.Content;
            }

            if (ast is null)
            {
                return null;
            }

            return (state, ast, content);
        }

        /// <summary>
        /// Converts a declaring source path to an LSP URI, preferring an already-open document.
        /// </summary>
        internal static Uri? ToSourceUri(string? sourceFile, WorkspaceManager workspace, Uri requestUri)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                return requestUri;
            }

            DocumentState? suffixMatch = null;
            int suffixHits = 0;
            DocumentState? fileNameMatch = null;
            int fileNameHits = 0;
            string sourceFileName = Path.GetFileName(sourceFile);

            foreach (DocumentState document in workspace.GetAllDocuments())
            {
                if (SourcePathsEqual(document.FilePath, sourceFile))
                {
                    return document.Uri;
                }

                if (IsPathSuffix(document.FilePath, sourceFile))
                {
                    suffixMatch = document;
                    suffixHits++;
                }

                if (!string.IsNullOrEmpty(sourceFileName)
                    && string.Equals(
                        Path.GetFileName(document.FilePath),
                        sourceFileName,
                        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                {
                    fileNameMatch = document;
                    fileNameHits++;
                }
            }

            if (suffixHits == 1)
            {
                return suffixMatch!.Uri;
            }

            if (fileNameHits == 1)
            {
                return fileNameMatch!.Uri;
            }

            try
            {
                if (File.Exists(sourceFile) || Path.IsPathRooted(sourceFile))
                {
                    return new Uri(Path.GetFullPath(sourceFile));
                }
            }
            catch (Exception ex) when (
                ex is ArgumentException
                or NotSupportedException
                or PathTooLongException
                or UriFormatException
                or IOException)
            {
            }

            return requestUri;
        }

        private static bool SourcePathsEqual(string documentPath, string sourceFile)
        {
            StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(CanonicalSourcePath(documentPath), CanonicalSourcePath(sourceFile), comparison))
            {
                return true;
            }

            string relativeDocument = AstCacheService.GetRelativePath(documentPath);
            string relativeSource = AstCacheService.GetRelativePath(sourceFile);
            return string.Equals(relativeDocument, relativeSource, comparison)
                || string.Equals(relativeDocument, sourceFile, comparison)
                || string.Equals(documentPath, sourceFile, comparison);
        }

        private static bool IsPathSuffix(string documentPath, string sourceFile)
        {
            if (string.IsNullOrEmpty(sourceFile) || sourceFile.IndexOfAny(['/', '\\']) < 0)
            {
                return false;
            }

            StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string normDoc = documentPath.Replace('\\', '/');
            string normSrc = sourceFile.Replace('\\', '/').TrimStart('/');
            return normDoc.EndsWith('/' + normSrc, comparison);
        }

        private IReadOnlyList<SymbolReference> FindProjectReferences(
            SymbolLookupResult lookup,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SrcFileAst> asts = this._analysis.GetProjectAsts();
            return this._symbolFinder.FindReferences(
                lookup,
                asts,
                this._analysis.GetGlobalScope(),
                this._analysis.GetSymbolTree());
        }

        private Location? ToReferenceLocation(SymbolReference occurrence, Uri requestUri, string currentName)
        {
            string sourceFile = FirstNonEmpty(occurrence.File.Identifier, occurrence.File.FileName);
            Uri? uri = ToSourceUri(sourceFile, this._workspace, requestUri);
            if (uri is null)
            {
                return null;
            }

            string? content = this.GetDocumentContent(uri, sourceFile);
            IBase2Ast nameNode = SymbolFinder.PreferIdentifierNode(occurrence.Node);
            return new Location
            {
                Uri = uri,
                Range = PositionUtilities.ToIdentifierRange(nameNode, currentName, content),
            };
        }

        private string? GetDocumentContent(Uri uri, string? sourceFile)
        {
            DocumentState? state = this._workspace.GetDocument(uri);
            if (state is not null)
            {
                lock (state.SyncRoot)
                {
                    return state.Content;
                }
            }

            if (!string.IsNullOrEmpty(sourceFile))
            {
                foreach (DocumentState document in this._workspace.GetAllDocuments())
                {
                    if (SourcePathsEqual(document.FilePath, sourceFile))
                    {
                        lock (document.SyncRoot)
                        {
                            return document.Content;
                        }
                    }
                }

                try
                {
                    if (File.Exists(sourceFile))
                    {
                        return File.ReadAllText(sourceFile);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }

            return null;
        }

        private static string CurrentSymbolName(SymbolLookupResult lookup)
        {
            if (lookup.Symbol is BaseSymbol symbol && !string.IsNullOrEmpty(symbol.Name))
            {
                return symbol.Name;
            }

            // Untyped locals resolve with a null Symbol (the binder never records a
            // VariableSymbol for a plain $name = expr; local — see SymbolFinder's
            // FindLocalVariableDeclaration remarks). PhpVariableAst does not override
            // ValueString/Identifier, so the name must be read off its VariableToken
            // (SymbolFinder.GetDisplayName), or callers such as RenameHandler.IsRenameable
            // see an empty name and refuse to rename the variable at all.
            return FirstNonEmpty(
                SymbolFinder.GetDisplayName(lookup.Node),
                SymbolFinder.GetDisplayName(lookup.DeclaringNode));
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        internal static ProtocolRange ToDefinitionRange(SymbolLookupResult lookup)
        {
            if (lookup.DeclaringNode is not null && lookup.DeclaringNode.Line >= 1)
            {
                return PositionUtilities.ToLspRange(lookup.DeclaringNode);
            }

            if (lookup.Symbol is not null)
            {
                return PositionUtilities.ToLspRange(lookup.Symbol);
            }

            return PositionUtilities.ToLspRange(lookup.Node);
        }

        private static string CanonicalSourcePath(string path)
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

        private void LogFeatureFailure(string feature, Exception exception)
        {
            this.LogMessage(
                MessageType.Log,
                Message.Localize("CLI_LanguageServerFeatureFailed", feature, exception.GetType().Name, exception.Message));
        }
    }
}
