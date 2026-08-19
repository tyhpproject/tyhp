namespace Tyhp.LanguageServer
{
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using StreamJsonRpc;
    using Tyhp.LanguageServer.Analysis;

    /// <summary>
    /// LSP <c>textDocument/references</c> handler on <see cref="TyhpLanguageServer"/>.
    /// </summary>
    public sealed partial class TyhpLanguageServer
    {
        /// <summary>
        /// Find all project-wide usages of the symbol under the cursor.
        /// </summary>
        [JsonRpcMethod(Methods.TextDocumentReferencesName, UseSingleObjectParameterDeserialization = true)]
        public async Task<Location[]> HandleReferences(
            ReferenceParams arg,
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

                bool includeDeclaration = arg.Context?.IncludeDeclaration ?? true;
                string currentName = CurrentSymbolName(resolved.Value.Lookup);
                IReadOnlyList<SymbolReference> occurrences = this.FindProjectReferences(
                    resolved.Value.Lookup,
                    cancellationToken);

                var locations = new List<Location>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (SymbolReference occurrence in occurrences)
                {
                    if (!includeDeclaration && occurrence.IsDeclaration)
                    {
                        continue;
                    }

                    Location? location = this.ToReferenceLocation(
                        occurrence,
                        resolved.Value.State.Uri,
                        currentName);
                    if (location is null)
                    {
                        continue;
                    }

                    string key = LocationKey(location);
                    if (seen.Add(key))
                    {
                        locations.Add(location);
                    }
                }

                return [.. locations];
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                this.LogFeatureFailure("textDocument/references", ex);
                return [];
            }
        }

        private static string LocationKey(Location location)
        {
            string uri = location.Uri is null
                ? string.Empty
                : (location.Uri.IsAbsoluteUri ? location.Uri.AbsoluteUri : location.Uri.ToString());
            int startLine = location.Range?.Start?.Line ?? 0;
            int startChar = location.Range?.Start?.Character ?? 0;
            int endLine = location.Range?.End?.Line ?? 0;
            int endChar = location.Range?.End?.Character ?? 0;
            return $"{uri}:{startLine}:{startChar}:{endLine}:{endChar}";
        }
    }
}
