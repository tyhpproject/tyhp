namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;
    using Tyhp.TyhpLang.Ast.Interfaces;
    using ProtocolRange = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

    /// <summary>
    /// LSP <c>textDocument/documentHighlight</c> handler on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Highlights read/write occurrences of the symbol under the cursor in the
        /// current document only.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentDocumentHighlightName, UseSingleObjectParameterDeserialization = true)]
        public async Task<DocumentHighlight[]> HandleDocumentHighlight(
            DocumentHighlightParams arg,
            CancellationToken cancellationToken)
        {
            try
            {
                (Workspace.DocumentState State, SymbolLookupResult Lookup)? resolved =
                    await this.LookupSymbolAsync(arg, cancellationToken).ConfigureAwait(false);
                if (resolved is null)
                {
                    return [];
                }

                string currentName = CurrentSymbolName(resolved.Value.Lookup);
                IReadOnlyList<SymbolReference> occurrences = this.FindProjectReferences(
                    resolved.Value.Lookup,
                    cancellationToken);

                string requestPath = resolved.Value.State.FilePath;
                string? content;
                lock (resolved.Value.State.SyncRoot)
                {
                    content = resolved.Value.State.Content;
                }

                var highlights = new List<DocumentHighlight>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (SymbolReference occurrence in occurrences)
                {
                    if (!IsSameDocument(occurrence, requestPath, resolved.Value.State.Uri))
                    {
                        continue;
                    }

                    IBase2Ast nameNode = SymbolFinder.PreferIdentifierNode(occurrence.Node);
                    ProtocolRange range = PositionUtilities.ToIdentifierRange(nameNode, currentName, content);
                    string key = $"{range.Start?.Line}:{range.Start?.Character}:{range.End?.Line}:{range.End?.Character}";
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    highlights.Add(new DocumentHighlight
                    {
                        Range = range,
                        Kind = ToHighlightKind(occurrence.Kind),
                    });
                }

                return [.. highlights];
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/documentHighlight", ex);
                return [];
            }
        }

        private bool IsSameDocument(SymbolReference occurrence, string requestPath, Uri requestUri)
        {
            string sourceFile = FirstNonEmpty(occurrence.File.Identifier, occurrence.File.FileName);
            if (SourcePathsEqual(requestPath, sourceFile))
            {
                return true;
            }

            Uri? uri = ToSourceUri(sourceFile, this._workspace, requestUri);
            if (uri is null)
            {
                return false;
            }

            string left = requestUri.IsAbsoluteUri ? requestUri.AbsoluteUri : requestUri.ToString();
            string right = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        private static DocumentHighlightKind ToHighlightKind(SymbolReferenceKind kind)
        {
            return kind switch
            {
                SymbolReferenceKind.Write => DocumentHighlightKind.Write,
                SymbolReferenceKind.Read => DocumentHighlightKind.Read,
                _ => DocumentHighlightKind.Text,
            };
        }
    }
}
