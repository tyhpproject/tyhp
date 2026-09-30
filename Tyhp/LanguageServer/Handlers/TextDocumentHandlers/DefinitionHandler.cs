namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;

    /// <summary>
    /// LSP <c>textDocument/definition</c> handler on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Navigate to the declaration of the symbol under the cursor.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentDefinitionName, UseSingleObjectParameterDeserialization = true)]
        public async Task<Location[]> HandleDefinition(
            TextDocumentPositionParams arg,
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

                SymbolLookupResult lookup = resolved.Value.Lookup;

                // Compiler builtins (built-in types/functions, magic constants, superglobals,
                // ...) have neither a declaring AST node nor a source file — there is nowhere
                // to navigate. Without this check, ToSourceUri below falls back to the request
                // URI and ToDefinitionRange falls back to the symbol's zeroed Line/Column,
                // sending F12 to a bogus (0,0) location in the file the cursor is already in.
                if (lookup.DeclaringNode is null && string.IsNullOrEmpty(lookup.SourceFile))
                {
                    return [];
                }

                Uri? uri = ToSourceUri(lookup.SourceFile, this._workspace, resolved.Value.State.Uri);
                if (uri is null)
                {
                    return [];
                }

                return
                [
                    new Location
                    {
                        Uri = uri,
                        Range = ToDefinitionRange(lookup),
                    },
                ];
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/definition", ex);
                return [];
            }
        }
    }
}
