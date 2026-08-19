namespace Tyhp.LanguageServer.Configuration
{
    using System.Runtime.Serialization;
    using Microsoft.VisualStudio.LanguageServer.Protocol;
    using Newtonsoft.Json;
    using Tyhp.LanguageServer.Analysis;

    /// <summary>
    /// Declares the LSP <see cref="ServerCapabilities"/> advertised in the
    /// <c>initialize</c> response.
    /// </summary>
    public static class CapabilityRegistration
    {
        /// <summary>
        /// Creates the current capability set: incremental document sync (open/close/save),
        /// completion (with trigger characters and resolve), hover, definition,
        /// references, rename (with prepare), document highlight, document symbols,
        /// signature help, folding ranges, code actions, formatting, selection range,
        /// semantic tokens (full + delta), and workspace symbols.
        /// </summary>
        public static ServerCapabilities Create()
        {
            return new TyhpServerCapabilities
            {
                TextDocumentSync = new TextDocumentSyncOptions
                {
                    OpenClose = true,
                    Change = TextDocumentSyncKind.Incremental,
                    Save = new SaveOptions { IncludeText = false },
                },
                CompletionProvider = new CompletionOptions
                {
                    ResolveProvider = true,
                    TriggerCharacters = ["$", ">", ":", "\\", "<", "("],
                },
                HoverProvider = true,
                DefinitionProvider = true,
                ReferencesProvider = true,
                DocumentHighlightProvider = true,
                RenameProvider = new RenameOptions { PrepareProvider = true },
                DocumentSymbolProvider = true,
                SignatureHelpProvider = new SignatureHelpOptions
                {
                    TriggerCharacters = ["(", ","],
                    RetriggerCharacters = [","],
                },
                FoldingRangeProvider = true,
                CodeActionProvider = new CodeActionOptions
                {
                    CodeActionKinds =
                    [
                        CodeActionKind.QuickFix,
                        CodeActionKind.SourceOrganizeImports,
                    ],
                },
                DocumentFormattingProvider = true,
                DocumentRangeFormattingProvider = true,
                SelectionRangeProvider = true,
                SemanticTokensOptions = new SemanticTokensOptions
                {
                    Legend = SemanticTokenCollector.Legend,
                    Range = false,
                    Full = new SemanticTokensFullOptions { Delta = true },
                },
                WorkspaceSymbolProvider = true,
            };
        }
    }

    /// <summary>
    /// Extends the 17.2.8 <see cref="ServerCapabilities"/> with
    /// <c>selectionRangeProvider</c>, which that package does not declare.
    /// </summary>
    /// <remarks>
    /// <see cref="ServerCapabilities"/> is a <see cref="DataContractAttribute"/> type whose
    /// members are opted into serialization individually via <see cref="DataMemberAttribute"/>
    /// / <see cref="JsonPropertyAttribute"/>. A plain auto-property on a derived class is
    /// silently dropped by <c>Newtonsoft.Json</c>'s data-contract resolver — it does not just
    /// serialize with the wrong (PascalCase) name, it does not appear in the payload at all.
    /// Both attributes are required (matching every other property on the base type) for
    /// <c>initialize</c> to actually advertise this capability to real LSP clients.
    /// </remarks>
    public sealed class TyhpServerCapabilities : ServerCapabilities
    {
        /// <summary>Whether <c>textDocument/selectionRange</c> is supported.</summary>
        [DataMember(Name = "selectionRangeProvider")]
        [JsonProperty("selectionRangeProvider")]
        public bool SelectionRangeProvider { get; set; }
    }
}
