# Implementation Plan: Story 19 — Language Server (LSP)

> **Roadmap position:** Story 19 — **Tier 2 — DX & Ecosystem**
> **Direct dependencies (new numbering):** 01, 02, 08, 10
> **Renumbered from:** legacy Story 12
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** `TODO.md` — Story 19: Language Server (LSP)
> **Generated:** 2026-02-16
> **Branch:** TBD

---

## Project Context

The Tyhp compiler is a C# (.NET 9.0) application that compiles `.tyhp` source files into PHP. The project uses ANTLR4 for parsing, has a complete AST system (~100+ node classes), a visitor system with 30+ partial class files, and an existing CLI infrastructure built on `Microsoft.Extensions.Hosting`.

Story 19 implements a Language Server Protocol (LSP) server for the Tyhp language. The LSP server enables IDE features such as autocomplete, go-to-definition, hover information, live diagnostics, find references, and rename refactoring. It reuses the existing parser, binder (Story 02), and checker (Story 08) infrastructure and is wired into the existing `language_server` CLI action.

### Prerequisites (from earlier stories)

Story 19 depends on:

- **Story 01** — Diagnostic system (`DiagnosticBag`, `CompilationResult`, `CompilationService`), build endpoint skeleton
- **Story 02** — Binder (symbols, scope tree, name resolution via `SymbolTree`)
- **Story 08** — Checker (type checking, validation via `TyhpChecker`)
- **Story 10** — Build action (full pipeline wired together)

This plan assumes those stories are complete. Where binder/checker APIs are referenced, the plan uses the interfaces and patterns described in `TODO.md` Stories 01–08 (foundation through checker).

### Key Existing Infrastructure

| Component | Location | Relevance |
|-----------|----------|-----------|
| CLI host | `Program.cs`, `Tyhp/CLI/TyhpHostedService.cs` | DI container, hosted service lifecycle, `language_server` action case |
| Action system | `Tyhp/CLI/ActionRunnerBase.cs`, `Tyhp/Config/Action.cs` | Base class for actions, `language_server` enum value exists |
| Parser | `Tyhp/TyhpLang/Parser/` | ANTLR-generated `TyhpLexer`/`TyhpParser` |
| AST | `Tyhp/TyhpLang/Ast/` | `Base2Ast`, `SrcFileAst`, 100+ node types |
| Visitor | `Tyhp/TyhpLang/Visitor/` | `TyhpParserAstVisitor` produces ASTs |
| Binder | `Tyhp/TyhpLang/Binder/` | `TyhpBinder`, `GlobalScope`, `SymbolTree`, 50+ symbol types |
| Checker | `Tyhp/TyhpLang/Checker/` | `TyhpChecker`, `CheckerState`, `VariableState` |
| Diagnostics | `Tyhp/Domain/Diagnostics/` (Story 01) | `DiagnosticBag`, `IDiagnostic`, `CompilationResult` |
| Compilation | `Tyhp/Domain/Services/CompilationService.cs` (Story 01) | Shared parse pipeline |
| AST cache | `Tyhp/Domain/Services/AstCacheService.cs` | In-memory + file-based, SHA256 invalidation |
| Config | `Tyhp/Config/Project.cs` | Project configuration, include/exclude globs |
| Message codes | `Tyhp/Domain/Exceptions/MessageCode.cs` | Numbered error/warning/info codes |
| Project file | `tyhp.csproj` | .NET 9.0, C# 13, nullable enabled, existing NuGet packages |

---

## Architecture Overview

### Overall Design

The Language Server is structured as a long-running hosted service that communicates over stdin/stdout using the JSON-RPC 2.0 protocol as specified by the Language Server Protocol. It is organized into the following layers:

1. **Transport Layer** — JSON-RPC message reading/writing over stdin/stdout
2. **Protocol Layer** — LSP message type definitions, capability negotiation, request/response routing
3. **Workspace Layer** — Document management, incremental file tracking, project-wide state
4. **Analysis Layer** — Integration with the existing parser, binder, checker to provide semantic data
5. **Feature Handlers** — Individual LSP feature implementations (diagnostics, completion, hover, etc.)

### Technology Stack

- **LSP Libraries:**
  - `StreamJsonRpc` (NuGet: `StreamJsonRpc`, v2.24.84) — Microsoft's actively maintained JSON-RPC 2.0 library. Handles stdin/stdout transport with Content-Length framing via `HeaderDelimitedMessageHandler`. This is the same library Roslyn's own language server uses internally.
  - `Microsoft.VisualStudio.LanguageServer.Protocol` (NuGet) — Microsoft's LSP type definitions (DTOs for all LSP requests, responses, notifications). Provides `InitializeParams`, `CompletionParams`, `CompletionList`, `Hover`, `Location`, `Diagnostic`, `ServerCapabilities`, etc.
- **Runtime:** .NET 9.0 (matches existing project)
- **Communication:** stdin/stdout (standard LSP transport)
- **Concurrency:** `async/await` with `CancellationToken` support; document analysis uses the existing `CompilationService` patterns

### Directory Structure

```
Tyhp/LanguageServer/
    LanguageServerAction.cs              — CLI action entry point
    TyhpLanguageServer.cs                — Server setup, capability registration, lifecycle
    Configuration/
        ServerConfiguration.cs           — LSP-specific configuration options
        CapabilityRegistration.cs        — Server capability declaration
    Workspace/
        WorkspaceManager.cs              — Tracks open documents, project files
        DocumentState.cs                 — Per-document state (content, AST, diagnostics)
        DocumentSyncHandler.cs           — textDocument/didOpen, didChange, didClose, didSave
    Analysis/
        AnalysisService.cs               — Coordinates parse/bind/check for single files
        IncrementalAnalyzer.cs           — Incremental re-analysis on document changes
        PositionUtilities.cs             — Line/column/offset conversion utilities
        SymbolFinder.cs                  — Find symbol at a given position in AST
    Handlers/
        DiagnosticsPublisher.cs          — Publishes diagnostics to client
        TextDocumentHandlers/
            CompletionHandler.cs         — textDocument/completion
            HoverHandler.cs              — textDocument/hover
            DefinitionHandler.cs         — textDocument/definition
            ReferencesHandler.cs         — textDocument/references
            RenameHandler.cs             — textDocument/rename, prepareRename
            DocumentSymbolHandler.cs     — textDocument/documentSymbol
            SignatureHelpHandler.cs       — textDocument/signatureHelp
            FormattingHandler.cs         — textDocument/formatting
            CodeActionHandler.cs         — textDocument/codeAction
            DocumentHighlightHandler.cs  — textDocument/documentHighlight
            FoldingRangeHandler.cs       — textDocument/foldingRange
            SelectionRangeHandler.cs     — textDocument/selectionRange
            SemanticTokensHandler.cs     — textDocument/semanticTokens
        WorkspaceHandlers/
            WorkspaceSymbolHandler.cs    — workspace/symbol
```

> **Handler-ownership model (authoritative — applies to every `*Handler.cs` above):** There are **no separate handler classes or handler interfaces**. Each `*Handler.cs` file is a **`partial class TyhpLanguageServer`** that contributes the `[JsonRpcMethod(...)]`-decorated methods for that feature. StreamJsonRpc discovers them through the single `jsonRpc.AddLocalRpcTarget(server)` call, where `server` is the one `TyhpLanguageServer` instance. Services (`WorkspaceManager`, `AnalysisService`, `SymbolFinder`, etc.) are constructor-injected into `TyhpLanguageServer` and accessed as fields from these methods. The file names group related methods for readability only — they are all the same class. Wherever a later phase says "Create `XHandler.cs`", read it as "add an `XHandler.cs` partial-class file of `TyhpLanguageServer` containing the relevant `[JsonRpcMethod]` methods".

### Patterns and Conventions

- **Handler pattern:** All LSP request/notification handlers are methods on the `TyhpLanguageServer` class (or a small set of partial classes), decorated with `[JsonRpcMethod("lsp/method/name", UseSingleObjectParameterDeserialization = true)]` attributes. StreamJsonRpc routes incoming JSON-RPC messages to the matching method automatically. There are no handler interfaces to implement — each handler is simply a method with the correct attribute and parameter types from `Microsoft.VisualStudio.LanguageServer.Protocol`.
- **Dependency injection:** Services (e.g., `WorkspaceManager`, `AnalysisService`) are injected via constructor into `TyhpLanguageServer` and accessed directly by handler methods. The `JsonRpc` instance is stored as a field on the server class for sending notifications to the client.
- **Debouncing:** Document change analysis is debounced (configurable, default ~300ms) to avoid unnecessary re-parsing on rapid keystrokes.
- **Cancellation:** All handlers accept `CancellationToken` and honor cancellation requests from the client.
- **Thread safety:** The `WorkspaceManager` uses concurrent collections for document state. Analysis operations acquire per-document locks to prevent concurrent modification.
- **Diagnostic mapping:** The existing `IDiagnostic` / `DiagnosticBag` from Story 01 maps directly to LSP `Diagnostic` objects.
- **Symbol resolution:** The existing binder's `SymbolTree` and `BaseSymbol` hierarchy provides all data needed for go-to-definition, hover, find references, and rename.

### Integration Points

- **ANTLR Parser Pipeline** (`TyhpLexer`, `TyhpParser`, `TyhpParserAstVisitor`) — Used by `AnalysisService` to parse documents from in-memory content
- **TyhpBinder** (Story 02) — Used by `AnalysisService` to build scope/symbol trees
- **TyhpChecker** (Story 08) — Used by `AnalysisService` to validate and produce diagnostics
- **AstCacheService** — Used to cache parsed ASTs for incremental re-analysis
- **DiagnosticBag** (Story 01) — Collected diagnostics are mapped to LSP diagnostic objects
- **TyhpHostedService** — The `language_server` case instantiates `LanguageServerAction`

### Placeholder Strategy

- Use `// PLACEHOLDER_PHASE_N: description` for functionality belonging to later phases of this plan
- Use `// PLACEHOLDER_STORY_N: description` for functionality belonging to other stories (e.g., `// PLACEHOLDER_STORY_11: emit feature expansion`)
- When starting a phase, search for `PLACEHOLDER_PHASE_N` and implement/remove as needed

---

## Phase 1: Project Setup, NuGet Dependencies, and LSP Skeleton

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Set up the foundational infrastructure for the language server: add the `StreamJsonRpc` and `Microsoft.VisualStudio.LanguageServer.Protocol` NuGet packages, create the directory structure, implement the CLI action entry point, wire it into `TyhpHostedService`, and establish the basic LSP lifecycle (initialize/shutdown).

### Deliverables

- Modify `Tyhp/TyhpLang/Ast/Interfaces/IBase2Ast.cs` — Add `EndLine`, `EndColumn`, `EndIndex` properties
- Modify `Tyhp/TyhpLang/Ast/Base2Ast.cs` — Add `EndLine`, `EndColumn`, `EndIndex` properties and populate from `context.Stop` in `SetContext()`
- Modify `Tyhp/TyhpLang/Binder/Symbols/BaseSymbol.cs` — Store `EndLine` and `EndColumn` from the declaring AST node
- Modify `Tyhp/Domain/Exceptions/MessageCode.cs` — Add language server error codes in the 7300-7399 range
- Add `StreamJsonRpc` (v2.24.84) and `Microsoft.VisualStudio.LanguageServer.Protocol` NuGet packages to `tyhp.csproj`
- Create `Tyhp/LanguageServer/` directory structure
- Create `Tyhp/LanguageServer/LanguageServerAction.cs` — CLI action class
- Create `Tyhp/LanguageServer/TyhpLanguageServer.cs` — Server setup and lifecycle
- Create `Tyhp/LanguageServer/Configuration/ServerConfiguration.cs` — Configuration options
- Create `Tyhp/LanguageServer/Configuration/CapabilityRegistration.cs` — Server capabilities
- Modify `Tyhp/CLI/TyhpHostedService.cs` — Wire up `language_server` action case
- Modify `Tyhp/Config/DisplayHelp.cs` — Add basic `LanguageServerHelp()` content

### Implementation Details

**AST End Position Properties (`IBase2Ast` and `Base2Ast`):**
- LSP requires `Range` objects (start + end positions) but `Base2Ast` only has `Line`, `Column`, `StartIndex` — no end position. ANTLR's `ParserRuleContext` has a `Stop` token with this info.
- Add `EndLine` (int, default -1), `EndColumn` (int, default -1), `EndIndex` (int, default -1) to both `IBase2Ast` and `Base2Ast`
- In `SetContext(ParserRuleContext context, string? languageMode = null)`, add:
  - `this.EndLine = context.Stop?.Line ?? this.Line;`
  - `this.EndColumn = context.Stop?.Column ?? this.Column;`
  - `this.EndIndex = context.Stop?.StopIndex ?? this.StartIndex;`
- In `SetContext(Base2Ast context)`, copy the end position properties too: `this.EndLine = context.EndLine;`, `this.EndColumn = context.EndColumn;`, `this.EndIndex = context.EndIndex;`
- Update `BaseSymbol.cs` constructor to store `EndLine` and `EndColumn` from the declaring AST node
- This change is backward-compatible — existing code ignores the new properties

Adding `EndLine`, `EndColumn`, and `EndIndex` properties to `Base2Ast` gives every AST node the storage for end positions. To populate these values:

1. **Update `Base2Ast.SetContext()`:** Modify the `SetContext()` method (which currently extracts start position from the ANTLR `ParserRuleContext`) to ALSO extract end position from `context.Stop` token: `EndLine = context.Stop.Line`, `EndColumn = context.Stop.Column`, `EndIndex = context.Stop.StopIndex`.

2. **Default values:** Properties default to `-1` (indicating "not set"). Synthetic nodes (created by the binder), `ErrorAst` nodes from error recovery, and nodes created without an ANTLR context will retain the `-1` default.

3. **`SymbolFinder` fallback strategy:** When `EndLine == -1` for a node, estimate the end position from the last child node's end position (recursive). If no children have end positions either, fall back to start-position-only matching: match the deepest node whose start position is at or before the cursor position. This handles all edge cases gracefully.

**Language Server Message Codes (`MessageCode.cs`):**
- `LspUnknownError = 7300` — Generic language server error
- `LspServerStartupFailed = 7301` — Failed to start the LSP server
- `LspAnalysisError = 7302` — Error during document analysis
- `LspSourceMapLoadError = 7303` — Error loading sourcemap for workspace analysis

**NuGet Package Addition:**
- Add two NuGet packages:
  - `StreamJsonRpc` (v2.24.84) — Microsoft's actively maintained JSON-RPC 2.0 library. Provides `JsonRpc`, `HeaderDelimitedMessageHandler`, and `JsonMessageFormatter` classes for stdin/stdout transport with Content-Length framing. This is the same library Roslyn's own language server uses internally.
  - `Microsoft.VisualStudio.LanguageServer.Protocol` — Microsoft's LSP type definitions. Provides all the DTO classes for LSP requests, responses, and notifications (`InitializeParams`, `InitializeResult`, `ServerCapabilities`, `CompletionParams`, `CompletionList`, `Hover`, `Location`, `Diagnostic`, etc.).

**`LanguageServerAction` class:**
- Extends `ActionRunnerBase`
- Override `Start(CancellationToken)` — call `this.RunAsync(cancellationToken).Wait()` to bridge to async (following the established pattern from `IntegrityCheckAction`). Return `null` since the language server does not produce a `CompilationResult`.
- The `StartAsync` method delegates to `TyhpLanguageServer.RunAsync()`
- This is a long-running action — `.Wait()` blocks until the client sends `shutdown`/`exit`. The `_isLongRunning` flag is already set to `true` in `TyhpHostedService`.

**`TyhpLanguageServer` class:**
- Contains a static `RunAsync(CancellationToken)` method that sets up and starts the server using StreamJsonRpc:
  ```csharp
  var stdin = Console.OpenStandardInput();
  var stdout = Console.OpenStandardOutput();
  var handler = new HeaderDelimitedMessageHandler(stdout, stdin, new JsonMessageFormatter());
  var jsonRpc = new JsonRpc(handler);
  var server = new TyhpLanguageServer(jsonRpc, /* other dependencies */);
  jsonRpc.AddLocalRpcTarget(server);
  jsonRpc.StartListening();
  await jsonRpc.Completion; // blocks until client disconnects
  ```
- Stores the `JsonRpc` instance as a field (`_jsonRpc`) for sending notifications to the client
- The `initialize` handler is a `[JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]` method that returns an `InitializeResult` with `ServerCapabilities` (start with minimal: `TextDocumentSyncKind.Incremental`)
- The `initialized` notification handler is a `[JsonRpcMethod("initialized", IsNotification = true)]` method for post-initialization work (e.g., workspace scan)
- The `shutdown` handler is a `[JsonRpcMethod("shutdown")]` method for cleanup
- The `exit` handler is a `[JsonRpcMethod("exit", IsNotification = true)]` method that exits the process
- All feature handlers are declared as `// PLACEHOLDER_PHASE_N` comments initially, to be implemented as additional `[JsonRpcMethod]` methods in later phases

**`ServerConfiguration` class:**
- `DebounceDelay` (int, milliseconds, default 300) — debounce for document change analysis
- `MaxConcurrentAnalysis` (int, default 4) — max concurrent file analysis operations
- `EnableDiagnostics` (bool, default true) — publish diagnostics on change
- `TyhpProjectPath` (string?) — path to `tyhp.json`, resolved from workspace root or client initialization options

**`CapabilityRegistration` class:**
- Static method to configure `ServerCapabilities` on the builder
- Initially declares: `TextDocumentSync` (incremental), `CompletionProvider` (placeholder), `HoverProvider` (placeholder), `DefinitionProvider` (placeholder)
- Additional capabilities added in later phases

**`TyhpHostedService` modification:**
- In the `case Tyhp.Config.Action.language_server:` block, instantiate `LanguageServerAction` and call `Start()`
- The `_isLongRunning` flag is already set to `true`

### Acceptance Criteria

- `Base2Ast` nodes populated via `SetContext(ParserRuleContext)` have correct `EndLine`/`EndColumn`/`EndIndex` values
- `BaseSymbol` instances store the end position from their declaring AST node
- `dotnet build` succeeds with no errors after adding `StreamJsonRpc` and `Microsoft.VisualStudio.LanguageServer.Protocol` packages and new files
- Running `tyhp language_server` starts the server process and blocks waiting for stdin input
- Sending a properly formatted LSP `initialize` request via stdin produces a valid `InitializeResult` response on stdout
- Sending `shutdown` followed by `exit` cleanly terminates the process
- The server reports `textDocumentSync` capability in its `InitializeResult`
- No existing functionality is broken (all existing actions still work)

### Dependencies

- **Requires:** Story 01 complete (diagnostic system, `CompilationService`)
- **Provides for Phase 2:** Server skeleton, directory structure, DI registration patterns

---

## Phase 2: Workspace Management and Document Synchronization

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the document lifecycle: tracking open/modified/closed documents, storing their content and parsed state, and providing the foundation for incremental analysis. This phase creates the `WorkspaceManager` and the `textDocument/didOpen`, `textDocument/didChange`, `textDocument/didClose`, and `textDocument/didSave` handlers.

### Deliverables

- Create `Tyhp/LanguageServer/Workspace/WorkspaceManager.cs`
- Create `Tyhp/LanguageServer/Workspace/DocumentState.cs`
- Create `Tyhp/LanguageServer/Workspace/DocumentSyncHandler.cs`
- Modify `Tyhp/LanguageServer/TyhpLanguageServer.cs` — Add document sync `[JsonRpcMethod]` handler methods and wire workspace services

### Implementation Details

**`DocumentState` class:**
- Represents the current state of a single document tracked by the server
- Properties:
  - `Uri` (DocumentUri) — the document's URI
  - `FilePath` (string) — resolved file system path
  - `Version` (int) — document version from client
  - `Content` (string) — current full text content
  - `LanguageMode` (string) — `"tyhp"`, `"php"`, or `"tyhpdef"` based on file extension
  - `ParsedAst` (SrcFileAst?) — last successfully parsed AST (null if not yet parsed or parse failed)
  - `Diagnostics` (IReadOnlyList<IDiagnostic>) — latest diagnostics from analysis
  - `IsDirty` (bool) — content has changed since last analysis
  - `LastAnalysisTime` (DateTime?) — when analysis last completed
  - `AnalysisCancellation` (CancellationTokenSource?) — cancel in-progress analysis on new changes

**`WorkspaceManager` class:**
- Central registry for all tracked documents
- Uses `ConcurrentDictionary<DocumentUri, DocumentState>` internally
- Methods:
  - `OpenDocument(DocumentUri uri, string content, int version)` — creates a new `DocumentState`
  - `UpdateDocument(DocumentUri uri, IEnumerable<TextDocumentContentChangeEvent> changes, int version)` — applies incremental changes to document content
  - `CloseDocument(DocumentUri uri)` — removes document from tracking
  - `GetDocument(DocumentUri uri)` — retrieves current state (returns null if not tracked)
  - `GetAllDocuments()` — returns all tracked documents
  - `GetDocumentsByLanguageMode(string mode)` — filter by language
  - `ApplyIncrementalChanges(string currentContent, IEnumerable<TextDocumentContentChangeEvent> changes)` — applies LSP incremental text changes to a string
- The `ApplyIncrementalChanges` method handles:
  - Range-based changes (start line/col to end line/col → replace text)
  - Full document replacement (no range specified)
  - Multiple changes in a single notification (applied sequentially)

**`DocumentSyncHandler` class:**
- Contains individual `[JsonRpcMethod]` methods for each document sync notification on `TyhpLanguageServer` (or a partial class):
  ```csharp
  [JsonRpcMethod("textDocument/didOpen", UseSingleObjectParameterDeserialization = true)]
  public void HandleDidOpen(DidOpenTextDocumentParams @params) { /* ... */ }

  [JsonRpcMethod("textDocument/didChange", UseSingleObjectParameterDeserialization = true)]
  public void HandleDidChange(DidChangeTextDocumentParams @params) { /* ... */ }

  [JsonRpcMethod("textDocument/didClose", UseSingleObjectParameterDeserialization = true)]
  public void HandleDidClose(DidCloseTextDocumentParams @params) { /* ... */ }

  [JsonRpcMethod("textDocument/didSave", UseSingleObjectParameterDeserialization = true)]
  public void HandleDidSave(DidSaveTextDocumentParams @params) { /* ... */ }
  ```
- `didOpen`: Call `WorkspaceManager.OpenDocument()`, mark document dirty, trigger analysis (Phase 3)
- `didChange`: Call `WorkspaceManager.UpdateDocument()` with incremental changes, mark dirty, trigger debounced analysis
- `didClose`: Call `WorkspaceManager.CloseDocument()`, clear published diagnostics for the URI
- `didSave`: Triggers incremental re-analysis of the changed file. Full project re-analysis is triggered only on workspace configuration changes.
- `TextDocumentSyncOptions` (with `TextDocumentSyncKind.Incremental`) are returned as part of `ServerCapabilities` in the `initialize` response

**Line/column utility considerations:**
- LSP uses 0-based line numbers and UTF-16 offset columns
- ANTLR and the Tyhp AST use 1-based line numbers and 0-based character columns
- Create a small utility (in `PositionUtilities`, Phase 3) for these conversions — for now, document the conversion requirement in `DocumentSyncHandler`

### Acceptance Criteria

- Opening a `.tyhp` file in an LSP client creates a `DocumentState` in the `WorkspaceManager`
- Typing in the file sends incremental changes that are correctly applied to the stored content
- Closing the file removes it from `WorkspaceManager`
- `WorkspaceManager.GetDocument()` returns the correct current content after multiple incremental edits
- Full document replacement changes (e.g., paste over entire content) work correctly
- No memory leaks on repeated open/close cycles (documents are fully cleaned up)
- `dotnet build` succeeds with no errors

### Dependencies

- **Requires:** Phase 1 (server skeleton, DI registration)
- **Provides for Phase 3:** Document content tracking, dirty state management

---

## Phase 3: Analysis Service, Position Utilities, and Diagnostic Publishing

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Build the core analysis pipeline that runs the parser, binder, and checker on open documents, and publishes the resulting diagnostics to the LSP client. This phase also creates position conversion utilities and the `SymbolFinder` for locating AST nodes and symbols at a cursor position.

### Deliverables

- Create `Tyhp/LanguageServer/Analysis/AnalysisService.cs`
- Create `Tyhp/LanguageServer/Analysis/IncrementalAnalyzer.cs`
- Create `Tyhp/LanguageServer/Analysis/PositionUtilities.cs`
- Create `Tyhp/LanguageServer/Analysis/SymbolFinder.cs`
- Create `Tyhp/LanguageServer/Handlers/DiagnosticsPublisher.cs`
- Modify `Tyhp/LanguageServer/Workspace/DocumentSyncHandler.cs` — Trigger analysis on document changes
- Modify `Tyhp/LanguageServer/TyhpLanguageServer.cs` — Wire analysis services as dependencies

### Implementation Details

**`AnalysisService` class:**
- Singleton service (registered in DI) that coordinates analysis for documents
- Dependencies: `WorkspaceManager`, `DiagnosticsPublisher`
- Methods:
  - `AnalyzeDocumentAsync(DocumentUri uri, CancellationToken ct)` — full analysis of a single document:
    1. Get document content from `WorkspaceManager`
    2. Parse the content using the existing parser infrastructure from `CompilationService` but providing in-memory document content instead of reading from disk. Create a `ParseFromContent(string content, string filePath)` method on `CompilationService` that accepts content directly — this reuses the existing visitor setup, error listeners, and language mode detection while bypassing disk I/O. Internally, it creates an `AntlrInputStream` from the content string, runs `TyhpLexer`, runs `TyhpParser`, and runs `TyhpParserAstVisitor` to produce the AST.
    3. Store the parsed AST in `DocumentState.ParsedAst`
    4. Run binder on the AST to get symbols/scope tree
    5. Run checker to produce diagnostics
    6. Map diagnostics to LSP format and publish
  - `AnalyzeAllAsync(CancellationToken ct)` — analyze all open documents (used on initialization)
  - `GetGlobalScope()` — returns the current project-wide `GlobalScope` (rebuilt on changes, cached)
- The binder needs cross-file information (the `GlobalScope`). Strategy:
  - **Initial workspace scan:** On first analysis (workspace open), parse ALL project files (using `Project.GetProjectSourceFiles()` plus any open documents) to build the initial `GlobalScope`. Store all parsed ASTs in `DocumentState` or a project-wide AST cache.
  - **On document change (debounced):**
    1. Re-parse ONLY the changed document from its in-memory content
    2. Update the changed document's AST in the cache
    3. Re-run the binder on ALL project ASTs (using cached ASTs for unchanged files, the new AST for the changed file) to rebuild the `GlobalScope` from scratch. This ensures cross-file semantics (e.g., a renamed class in file A is reflected in file B's diagnostics) are always correct.
    4. Re-run the checker on all open documents to produce fresh diagnostics
    5. Publish updated diagnostics for all open documents
  - **Performance characteristics:** Parsing is the expensive operation; re-using cached ASTs for unchanged files makes re-binding fast. Binding and checking walk in-memory data structures and are typically fast even for large projects. The 300ms debounce prevents redundant work during rapid typing.
  - **Project file change detection:** When a non-open project file changes on disk (e.g., via version control), the `WorkspaceManager` should detect this via file watchers, re-parse the changed file, and trigger a full re-bind cycle.

**`IncrementalAnalyzer` class:**
- Manages debounced analysis requests
- Uses a `System.Threading.Timer` or `Task.Delay` with cancellation for debounce
- When a document change is received:
  1. Cancel any pending debounced analysis for that URI
  2. Start a new debounce timer (default 300ms from `ServerConfiguration`)
  3. When the timer fires, call `AnalysisService.AnalyzeDocumentAsync()`
- Debounce is per-document (changes to file A don't delay analysis of file B)
- Configurable debounce delay via `ServerConfiguration.DebounceDelay`

**`PositionUtilities` static class:**
- `ToLspPosition(int antlrLine, int antlrColumn)` — converts ANTLR 1-based line + 0-based column to LSP 0-based line + 0-based column (character offset)
- `FromLspPosition(Position lspPosition)` — converts LSP position to (line, column) in ANTLR terms
- `ToLspRange(IBaseAst node)` — converts an AST node's start position (and optional end position) to an LSP `Range`
- `GetOffset(string text, Position position)` — converts an LSP position to a character offset in the document text
- `GetPosition(string text, int offset)` — converts a character offset to an LSP position
- Handle edge cases: EOF positions, empty lines, multi-byte characters (LSP uses UTF-16 offsets)

**`SymbolFinder` class:**
- Given a document's AST and a cursor position, finds the AST node at that position
- Methods:
  - `FindNodeAtPosition(SrcFileAst ast, int line, int column)` — walk the AST tree to find the most specific (deepest) node that contains the given position
  - `FindSymbolAtPosition(SrcFileAst ast, GlobalScope scope, int line, int column)` — find the node at the position, then resolve it to a `BaseSymbol` using the binder's `SymbolTree`
  - `FindDeclaringNode(BaseSymbol symbol)` — get the AST node that declares a symbol (for go-to-definition)
  - `FindReferences(BaseSymbol symbol, IEnumerable<SrcFileAst> allAsts, GlobalScope scope)` — find all AST nodes that reference a given symbol (for find-references)
- Node-at-position algorithm: recursively descend through AST children, filtering to nodes whose source range contains the target position, returning the deepest match
- Symbol resolution: once the AST node is found, determine what kind of reference it represents (variable usage, type reference, function call, member access, etc.) and use `SymbolTree` to resolve it

**`DiagnosticsPublisher` class:**
- Receives diagnostics from `AnalysisService` and publishes them to the LSP client
- Dependencies: `JsonRpc` instance (stored as a field on `TyhpLanguageServer`) for sending notifications
- Methods:
  - `PublishDiagnostics(DocumentUri uri, IReadOnlyList<IDiagnostic> diagnostics)` — maps Tyhp `IDiagnostic` instances to LSP `Diagnostic` objects and sends via `await _jsonRpc.NotifyWithParameterObjectAsync("textDocument/publishDiagnostics", diagnosticParams)`
  - `ClearDiagnostics(DocumentUri uri)` — publishes empty diagnostics list (on document close) via `_jsonRpc.NotifyWithParameterObjectAsync`
- Mapping from `IDiagnostic` to LSP `Diagnostic`:
  - `IDiagnostic.Severity` → LSP `DiagnosticSeverity` (Error/Warning/Information/Hint)
  - `IDiagnostic.Line/Column` → LSP `Range` (using `PositionUtilities`)
  - `IDiagnostic.EndLine/EndColumn` → end of range (if available, else same as start)
  - `IDiagnostic.Code` (MessageCode) → LSP diagnostic code (int)
  - `IDiagnostic.Message` → LSP diagnostic message
  - Source: `"tyhp"`
- Support diagnostic tags: `DiagnosticTag.Deprecated` for deprecated symbols, `DiagnosticTag.Unnecessary` for unused imports

### Acceptance Criteria

- Opening a `.tyhp` file triggers parsing and publishes diagnostics (parser errors appear in the client's Problems panel)
- Typing invalid syntax shows error diagnostics after the debounce delay
- Fixing the syntax clears the diagnostics
- Closing a file clears its diagnostics in the client
- `SymbolFinder.FindNodeAtPosition()` correctly identifies AST nodes for various cursor positions (on a variable, on a function name, on a type reference, on a keyword)
- Position conversion correctly handles the ANTLR-to-LSP offset differences
- Debouncing works: rapid typing only triggers one analysis after the final keystroke settles
- No analysis runs for non-Tyhp files
- `dotnet build` succeeds with no errors

### Dependencies

- **Requires:** Phase 2 (document tracking), Story 01 (`DiagnosticBag`, `CompilationService`), Story 02 (binder), Story 08 (checker)
- **Provides for Phase 4:** `SymbolFinder`, `AnalysisService`, parsed ASTs with symbol data

---

## Phase 4: Go-to-Definition and Hover Information

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the first two semantic LSP features: go-to-definition (navigate to where a symbol is declared) and hover (display type information and documentation when hovering over a symbol). These are the most impactful features for day-to-day development.

### Deliverables

- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/DefinitionHandler.cs`
- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/HoverHandler.cs`
- Modify `Tyhp/LanguageServer/TyhpLanguageServer.cs` — Add definition and hover `[JsonRpcMethod]` handler methods
- Modify `Tyhp/LanguageServer/Configuration/CapabilityRegistration.cs` — Declare definition and hover capabilities in `ServerCapabilities` returned by `initialize`

### Implementation Details

**`DefinitionHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("textDocument/definition", UseSingleObjectParameterDeserialization = true)]` method returning `Task<Location[]>`
- Dependencies: `WorkspaceManager`, `AnalysisService`, `SymbolFinder` (accessed via fields on `TyhpLanguageServer`)
- On `textDocument/definition` request:
  1. Get the document state for the request URI
  2. Convert LSP position to ANTLR line/column
  3. Call `SymbolFinder.FindSymbolAtPosition()` to get the symbol under the cursor
  4. If no symbol found, return empty result
  5. Get the symbol's `DeclaringAstNode` and `SourceFile` from `BaseSymbol` properties
  6. Convert the declaration's line/column to LSP position
  7. Return a `LocationOrLocationLinks` response pointing to the declaration
- Handle these symbol kinds:
  - Variables → jump to their declaration/assignment
  - Functions/methods → jump to `FunctionDeclarationAst` or `MethodDeclarationAst`
  - Classes/interfaces/traits/enums/structs → jump to their declaration AST
  - Properties → jump to `PropertyDeclarationAst`
  - Constants → jump to `ConstantDeclarationAst` or `ClassConstantDeclarationAst`
  - Type aliases → jump to the alias declaration
  - Generic type parameters → jump to the generic parameter declaration
  - Namespaces → jump to the namespace declaration
- Handle cross-file navigation: the symbol's `SourceFile` may differ from the request file
- Handle tyhpdef symbols: if the symbol comes from a `.tyhpdef` file, navigate to that file
- Handle `self`, `static`, `parent` → resolve to the containing class declaration

**`HoverHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("textDocument/hover", UseSingleObjectParameterDeserialization = true)]` method returning `Task<Hover?>`
- Dependencies: `WorkspaceManager`, `AnalysisService`, `SymbolFinder` (accessed via fields on `TyhpLanguageServer`)
- On `textDocument/hover` request:
  1. Find symbol at cursor position (same as definition handler)
  2. If no symbol found, return null (no hover)
  3. Build a Markdown-formatted hover string containing:
     - Symbol kind label (e.g., "class", "function", "variable", "property")
     - Full signature (e.g., `public function getUserById(int $id): ?User`)
     - Containing scope info (e.g., "in class UserService")
     - Doc comment if available (from `BaseSymbol.DocComment`)
     - Deprecation warning if `BaseSymbol.IsDeprecated`
  4. Return a `Hover` object with the Markdown content and the source range of the hovered token
- Signature formatting methods (in `HoverHandler` or a shared `SymbolFormatter` utility):
  - `FormatFunctionSignature(FunctionDeclarationSymbol symbol)` — `function name(params): returnType`
  - `FormatMethodSignature(ObjectMethodSymbol symbol)` — `visibility [static] [abstract] function name(params): returnType`
  - `FormatPropertySignature(ObjectPropertySymbol symbol)` — `visibility [readonly] Type $name`
  - `FormatVariableInfo(VariableSymbol symbol)` — `Type $name` with inferred type info
  - `FormatClassSignature(ObjectDeclarationSymbol symbol)` — `[abstract|final] class Name [extends Parent] [implements Interface1, Interface2]`
  - `FormatConstantSignature(ConstantSymbol symbol)` — `const Type NAME = value`
- Format using Tyhp syntax (not PHP) since the user is writing Tyhp code
- Use fenced code blocks with `tyhp` language identifier in the Markdown

### Acceptance Criteria

- Ctrl+Click (or F12) on a variable name navigates to its declaration
- Ctrl+Click on a function call navigates to the function declaration
- Ctrl+Click on a class name reference navigates to the class declaration
- Ctrl+Click on a method call navigates to the method declaration in the correct class
- Cross-file go-to-definition works (jumping from one `.tyhp` file to another)
- Go-to-definition on tyhpdef-sourced symbols opens the `.tyhpdef` file
- Hovering over a variable shows its type
- Hovering over a function shows its full signature with parameter types and return type
- Hovering over a class name shows the class declaration with extends/implements
- Hovering over a deprecated symbol shows a deprecation warning
- Doc comments from the source appear in the hover popup
- Empty hover result (no popup) when cursor is on whitespace, keywords, or punctuation without semantic meaning
- `dotnet build` succeeds with no errors

### Dependencies

- **Requires:** Phase 3 (`SymbolFinder`, `AnalysisService`, position utilities), Story 02 (binder symbols and name resolution)
- **Provides for Phase 5:** `SymbolFormatter` utility (if extracted), established pattern for feature handlers

---

## Phase 5: Completion (Autocomplete)

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement autocomplete suggestions based on the current cursor context. This includes scope-aware variable/function suggestions, member completion after `->` and `::`, namespace completion after `\`, type completion in type contexts, and auto-import suggestions.

### Deliverables

- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/CompletionHandler.cs`
- Modify `Tyhp/LanguageServer/TyhpLanguageServer.cs` — Add completion `[JsonRpcMethod]` handler methods
- Modify `Tyhp/LanguageServer/Configuration/CapabilityRegistration.cs` — Declare completion capabilities with trigger characters in `ServerCapabilities`

### Implementation Details

**`CompletionHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("textDocument/completion", UseSingleObjectParameterDeserialization = true)]` method returning `Task<CompletionList>`
- Optionally, a `[JsonRpcMethod("completionItem/resolve", UseSingleObjectParameterDeserialization = true)]` method for deferred detail loading
- Dependencies: `WorkspaceManager`, `AnalysisService`, `SymbolFinder` (accessed via fields on `TyhpLanguageServer`)
- Trigger characters: `$`, `>` (for `->`), `:` (for `::`), `\`, `<` (for generic args), `(`
- On `textDocument/completion` request:
  1. Determine the completion context:
     - What character triggered completion (or was it manual invocation?)
     - What token/node is at/before the cursor position?
     - Are we in a type context (parameter type, return type, property type, generic argument)?
     - Are we after `->` (instance member access)?
     - Are we after `::` (static member access)?
     - Are we after `$` (variable)?
     - Are we after `\` (namespace)?
     - Are we in an import/use statement?
     - Are we in a function call argument list?

**Completion context detection algorithm:**

1. Re-parse the current line up to the cursor position using error-recovery mode (ANTLR's default error recovery is sufficient).
2. Examine the last non-whitespace token before the cursor position and the enclosing grammar rule context.
3. Use a lookup table mapping `(lastToken, enclosingRule)` to `CompletionContext`:

| Last Token | Context | Completion Type |
|-----------|---------|-----------------|
| `->` or `?->` | After expression | Member completion (properties, methods) |
| `::` | After class name | Static member completion |
| `new ` | After `new` keyword | Class name completion |
| `:` | Parameter/return type position | Type completion |
| `extends` | Class declaration | Class name completion |
| `implements` | Class declaration | Interface name completion |
| `use ` | Inside class body | Trait name completion |
| `use ` | File level | Namespace/class completion |
| `\` | Namespace context | Namespace segment completion |
| `$` | Variable context | Variable name completion |
| Other/default | Statement position | Keyword + symbol completion |

For incomplete syntax (common during typing), fall back to the most recent successfully parsed context and use positional heuristics (is the cursor inside a function body? inside a class body? at file level?).

  2. Based on context, build the appropriate completion list:

- **Variable completion (after `$`):**
  - List all variables in scope (walk scope chain from current position up)
  - Include function parameters
  - Include `$this` when inside a class method
  - Set `CompletionItemKind.Variable`

- **Instance member completion (after `->`):**
  - Determine the type of the expression before `->`
  - If type is resolved to an `ObjectDeclarationSymbol`, list its public (and protected if in same class hierarchy) properties, methods, and constants
  - Include inherited members from parent classes and implemented interfaces
  - Include trait members
  - Include extension methods applicable to the type
  - Set appropriate `CompletionItemKind` (Method, Property, Field, Constant)

- **Static member completion (after `::`):**
  - Resolve the class name before `::`
  - List static methods, static properties, class constants, enum cases
  - Handle `self::`, `static::`, `parent::` context

- **Type completion (in type contexts):**
  - List all classes, interfaces, traits, enums, structs, type aliases in scope
  - Include built-in types (int, string, float, bool, array, mixed, void, never, null, true, false)
  - Include generic types with placeholder type parameters
  - Set `CompletionItemKind.Class`, `CompletionItemKind.Interface`, etc.

- **Namespace completion (after `\`):**
  - List child namespaces and types within the current namespace segment
  - Handle both absolute (`\App\Models\`) and relative namespace paths

- **Global completion (manual trigger, no specific context):**
  - Functions in scope
  - Constants in scope
  - Classes/types in scope
  - Keywords (if, else, for, foreach, while, switch, match, try, catch, function, class, etc.)
  - Snippet suggestions for common patterns

- **Auto-import suggestions:**
  - When a type/function name matches a symbol not currently imported, include it as a completion item
  - Set `additionalTextEdits` on the completion item to add the `use` statement at the top of the file
  - Label these items differently (e.g., include the fully qualified name)

- Completion item properties to populate:
  - `Label` — display name
  - `Kind` — icon type
  - `Detail` — short type/signature info
  - `Documentation` — doc comment (loaded on resolve if expensive)
  - `InsertText` or `TextEdit` — what gets inserted
  - `FilterText` — what the user types to filter
  - `SortText` — ordering priority
  - `Deprecated` flag — for deprecated symbols
  - `AdditionalTextEdits` — for auto-import

### Acceptance Criteria

- Typing `$` followed by characters shows variable suggestions from the current scope
- Typing `->` after an object variable shows instance members of that object's type
- Typing `::` after a class name shows static members and constants
- Typing in a type annotation context shows available types
- Typing `\` shows namespace segments
- Completion items show correct icons (variable, method, property, class, etc.)
- Selecting a completion item inserts the correct text
- Auto-import adds a `use` statement when selecting a type from another namespace
- Doc comments appear in the completion item detail/documentation
- Deprecated items are marked with strikethrough
- Completion is fast (responds within a few hundred milliseconds)
- `dotnet build` succeeds with no errors

### Dependencies

- **Requires:** Phase 4 (established handler patterns), Phase 3 (`SymbolFinder`, `AnalysisService`), Story 02 (binder scope/symbol resolution)
- **Provides for Phase 6:** Completion infrastructure, context detection logic

---

## Phase 6: Find References, Rename, and Document Highlights

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement three related features that all depend on finding all usages of a symbol across the project: find references, rename symbol, and document highlight. These share the core logic of `SymbolFinder.FindReferences()`.

### Deliverables

- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/ReferencesHandler.cs`
- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/RenameHandler.cs`
- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/DocumentHighlightHandler.cs`
- Modify `Tyhp/LanguageServer/Analysis/SymbolFinder.cs` — Implement `FindReferences()` fully
- Modify `Tyhp/LanguageServer/TyhpLanguageServer.cs` — Add references, rename, and highlight `[JsonRpcMethod]` handler methods
- Modify `Tyhp/LanguageServer/Configuration/CapabilityRegistration.cs` — Declare capabilities in `ServerCapabilities`

### Implementation Details

**`SymbolFinder.FindReferences()` enhancement:**
- Walk all parsed ASTs (both open documents and project files) to find nodes that reference the target symbol
- For each AST, walk the tree looking for:
  - Name references (`NameAst`, `NamespaceNameAst`) that resolve to the target symbol
  - Variable usages (`SimpleVariableAst`) that resolve to the target `VariableSymbol`
  - Member access expressions (`PropertyReferenceAst`, `MethodCallAst`) where the resolved member matches the target
  - Class constant references (`ClassConstantReferenceAst`) matching the target
  - Type references in annotations that resolve to the target type symbol
- Each match produces a `Location` (file URI + range)
- Optionally include the declaration itself in results (configurable via `ReferenceContext.IncludeDeclaration`)

**`ReferencesHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("textDocument/references", UseSingleObjectParameterDeserialization = true)]` method returning `Task<Location[]>`
- On `textDocument/references`:
  1. Find the symbol at cursor position
  2. Call `SymbolFinder.FindReferences()` with all project ASTs
  3. Return list of `Location` objects

**`RenameHandler` (methods on `TyhpLanguageServer`):**
- Implemented as two `[JsonRpcMethod]` methods:
  - `[JsonRpcMethod("textDocument/prepareRename", UseSingleObjectParameterDeserialization = true)]` returning `Task<Range?>`
  - `[JsonRpcMethod("textDocument/rename", UseSingleObjectParameterDeserialization = true)]` returning `Task<WorkspaceEdit?>`
- `textDocument/prepareRename`:
  1. Find symbol at cursor
  2. Verify the symbol can be renamed (not a built-in, not from a tyhpdef, not a keyword)
  3. Return the range of the token to be renamed and its current text
- `textDocument/rename`:
  1. Find symbol at cursor
  2. Validate: symbol is renameable, new name is valid identifier, no conflicts with existing symbols
  3. Find all references (same as find-references)
  4. Build a `WorkspaceEdit` containing `TextEdit`s for each reference location
  5. Include the declaration in the edits
  6. Handle cross-file renames (edits span multiple files)
- Rename validation:
  - Reject renaming built-in types/functions
  - Reject renaming tyhpdef-sourced symbols (they represent external PHP code)
  - Warn if new name conflicts with an existing symbol in scope
  - Handle renaming class members (property, method) — find all usages including through inheritance

**`DocumentHighlightHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("textDocument/documentHighlight", UseSingleObjectParameterDeserialization = true)]` method returning `Task<DocumentHighlight[]>`
- On `textDocument/documentHighlight`:
  1. Find symbol at cursor
  2. Find all references to that symbol within the same document only (not cross-file)
  3. Return `DocumentHighlight` objects with `DocumentHighlightKind.Read` or `DocumentHighlightKind.Write` depending on whether each reference is a read or assignment
- Distinguishing read vs. write:
  - Assignment left-hand side → `Write`
  - All other usages → `Read`
  - Declaration → `Write` (if it has an initializer) or `Text`

### Acceptance Criteria

- "Find All References" on a variable shows all usages across the project
- "Find All References" on a function shows all call sites
- "Find All References" on a class shows all instantiations, type references, and extends/implements usages
- References include cross-file usages
- Rename on a variable renames it everywhere it's used
- Rename on a method renames all call sites and the declaration
- Rename validates the new name is a valid identifier
- Rename rejects renaming built-in or tyhpdef symbols
- Prepare rename returns the correct range and current name
- Document highlight highlights all usages of a symbol in the current file
- Write usages (assignments) are highlighted differently from read usages
- `dotnet build` succeeds with no errors

### Dependencies

- **Requires:** Phase 4 (`SymbolFinder` base), Phase 3 (`AnalysisService`), Story 02 (binder name resolution)
- **Provides for Phase 7:** Complete `FindReferences` infrastructure

---

## Phase 7: Document Symbols, Signature Help, and Folding Ranges

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement secondary LSP features that improve code navigation and editing: document symbols (outline view), signature help (parameter hints while typing function arguments), and folding ranges (code collapse regions).

### Deliverables

- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/DocumentSymbolHandler.cs`
- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/SignatureHelpHandler.cs`
- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/FoldingRangeHandler.cs`
- Modify `Tyhp/LanguageServer/TyhpLanguageServer.cs` — Add document symbol, signature help, and folding range `[JsonRpcMethod]` handler methods
- Modify `Tyhp/LanguageServer/Configuration/CapabilityRegistration.cs` — Declare capabilities in `ServerCapabilities`

### Implementation Details

**`DocumentSymbolHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("textDocument/documentSymbol", UseSingleObjectParameterDeserialization = true)]` method returning `Task<DocumentSymbol[]>`
- On `textDocument/documentSymbol`:
  1. Get the parsed AST for the document
  2. Walk the AST and build a hierarchical `DocumentSymbol[]` tree
  3. Map AST nodes to LSP `SymbolKind`:
     - Namespace declarations → `SymbolKind.Namespace`
     - Class declarations → `SymbolKind.Class`
     - Interface declarations → `SymbolKind.Interface`
     - Trait declarations → `SymbolKind.Class` (no specific trait kind in LSP)
     - Enum declarations → `SymbolKind.Enum`
     - Enum cases → `SymbolKind.EnumMember`
     - Struct declarations → `SymbolKind.Struct`
     - Function declarations → `SymbolKind.Function`
     - Method declarations → `SymbolKind.Method`
     - Property declarations → `SymbolKind.Property`
     - Constant declarations → `SymbolKind.Constant`
     - Type aliases → `SymbolKind.TypeParameter`
     - Variables (class-level) → `SymbolKind.Variable`
  4. Each `DocumentSymbol` includes: name, kind, range (full node range), selection range (name token range), children (nested symbols), detail (e.g., return type), deprecated tag

**`SignatureHelpHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("textDocument/signatureHelp", UseSingleObjectParameterDeserialization = true)]` method returning `Task<SignatureHelp?>`
- Trigger characters: `(`, `,` (declared in `SignatureHelpOptions` within `ServerCapabilities`)
- On `textDocument/signatureHelp`:
  1. Determine if the cursor is inside a function call argument list
  2. Walk backward from cursor to find the function call AST node
  3. Identify which argument position the cursor is at (count commas before cursor)
  4. Resolve the function/method being called using `SymbolFinder`
  5. Build `SignatureInformation`:
     - Full signature label
     - Parameters with labels and documentation
     - Active parameter index based on cursor position
  6. Handle overloaded functions: return multiple `SignatureInformation` items
  7. Handle constructor calls (`new ClassName(`)
  8. Handle method calls (`$obj->method(`)
  9. Handle static method calls (`ClassName::method(`)

**`FoldingRangeHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("textDocument/foldingRange", UseSingleObjectParameterDeserialization = true)]` method returning `Task<FoldingRange[]>`
- On `textDocument/foldingRange`:
  1. Walk the AST and identify foldable regions:
     - Class/interface/trait/enum/struct bodies → `FoldingRangeKind.Region`
     - Function/method bodies → `FoldingRangeKind.Region`
     - Code blocks (if/else/for/foreach/while/switch/try/catch) → `FoldingRangeKind.Region`
     - Doc comments (multi-line) → `FoldingRangeKind.Comment`
     - Import/use statement groups → `FoldingRangeKind.Imports`
     - Namespace blocks → `FoldingRangeKind.Region`
     - Array literals (multi-line) → `FoldingRangeKind.Region`
  2. Return `FoldingRange` objects with start/end line numbers

### Acceptance Criteria

- The Outline panel (document symbols) shows the hierarchical structure of the file
- Symbols have correct kinds (class, function, property, etc.)
- Nested symbols appear as children (methods inside classes, etc.)
- Clicking a symbol in the outline navigates to its location
- Typing `(` after a function name shows signature help with parameter names and types
- Typing `,` moves the active parameter highlight to the next parameter
- Signature help works for regular functions, methods, static methods, and constructors
- Overloaded functions show multiple signatures
- Code regions are foldable (classes, functions, blocks, comments, imports)
- Folding multi-line doc comments works
- `dotnet build` succeeds with no errors

### Dependencies

- **Requires:** Phase 4 (symbol resolution), Phase 3 (AST analysis)
- **Provides for Phase 8:** Additional feature handler patterns

---

## Phase 8: Code Actions, Formatting, and Selection Range

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement code actions (quick fixes), basic code formatting, and smart selection range expansion. Code actions provide actionable suggestions linked to diagnostics (e.g., auto-import a missing type). Formatting provides basic code style enforcement. Selection range enables smart code selection expansion.

### Deliverables

- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/CodeActionHandler.cs`
- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/FormattingHandler.cs`
- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/SelectionRangeHandler.cs`
- Modify `Tyhp/LanguageServer/TyhpLanguageServer.cs` — Add code action, formatting, and selection range `[JsonRpcMethod]` handler methods
- Modify `Tyhp/LanguageServer/Configuration/CapabilityRegistration.cs` — Declare capabilities in `ServerCapabilities`

### Implementation Details

**`CodeActionHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("textDocument/codeAction", UseSingleObjectParameterDeserialization = true)]` method returning `Task<CodeAction[]>`
- On `textDocument/codeAction`:
  1. Get the diagnostics at the requested range
  2. For each diagnostic, determine if there are applicable code actions:
     - **Auto-import:** If the diagnostic is "Symbol not found" and a matching symbol exists in another namespace → offer to add a `use` statement
     - **Remove unused import:** If the diagnostic is "Unused import" → offer to remove the `use` statement
     - **Add missing type annotation:** If the diagnostic is about a missing type and the type can be inferred → offer to add the type
     - **Implement interface methods:** If a class declares `implements SomeInterface` but doesn't implement all methods → offer to generate method stubs
  3. Return `CodeAction[]` with:
     - Title (e.g., "Import App\\Models\\User")
     - Kind (`CodeActionKind.QuickFix`, `CodeActionKind.SourceOrganizeImports`, etc.)
     - Diagnostics this action resolves
     - Edit (a `WorkspaceEdit` with the required text changes)
- Start with auto-import and remove unused import as the most impactful code actions
- Additional code actions (implement interface, generate constructor, etc.) can be added incrementally

**`FormattingHandler` (methods on `TyhpLanguageServer`):**
- Implemented as two `[JsonRpcMethod]` methods:
  - `[JsonRpcMethod("textDocument/formatting", UseSingleObjectParameterDeserialization = true)]` returning `Task<TextEdit[]>`
  - `[JsonRpcMethod("textDocument/rangeFormatting", UseSingleObjectParameterDeserialization = true)]` returning `Task<TextEdit[]>`
- On `textDocument/formatting`:
  1. Parse the document to get the AST
  2. Walk the AST and produce formatted output using consistent style rules:
     - Indentation (configurable: spaces vs. tabs, width)
     - Brace placement (same line or next line — following the project's existing convention)
     - Import sorting (alphabetical, grouped by type: classes, functions, constants)
     - Consistent spacing around operators
     - Blank lines between class members
  3. Compute a minimal set of `TextEdit`s between the original content and formatted content
  4. Return the edits
- Note: Full formatting is complex. Start with import sorting and basic indentation normalization. More advanced formatting can be a `// PLACEHOLDER_STORY_30: advanced code formatting` for the documentation/polish story.

**`SelectionRangeHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("textDocument/selectionRange", UseSingleObjectParameterDeserialization = true)]` method returning `Task<SelectionRange[]>`
- On `textDocument/selectionRange`:
  1. Find the AST node at the cursor position
  2. Build a chain of progressively larger selection ranges by walking up the AST parent chain:
     - Innermost: the token at cursor
     - Next: the containing expression
     - Next: the containing statement
     - Next: the containing block
     - Next: the containing function/method
     - Next: the containing class
     - Outermost: the entire file
  3. Return a `SelectionRange` linked list (each range has a `parent` pointing to the next larger range)

### Acceptance Criteria

- Clicking the lightbulb on a "Symbol not found" diagnostic offers an auto-import code action
- Selecting the auto-import action adds the correct `use` statement at the top of the file
- "Remove unused import" action removes the unused `use` statement
- Document formatting normalizes indentation and sorts imports
- Range formatting works on a selected region only
- Selection range expansion (e.g., Shift+Alt+Right) expands from token → expression → statement → block → function → class → file
- Code actions have appropriate kinds (quickfix, source.organizeImports)
- `dotnet build` succeeds with no errors

### Dependencies

- **Requires:** Phase 6 (references infrastructure for rename edits), Phase 3 (diagnostic mapping)
- **Provides for Phase 9:** Code action patterns

---

## Phase 9: Semantic Tokens and Workspace Symbols

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement semantic token highlighting (richer syntax coloring based on semantic analysis) and workspace-wide symbol search. Semantic tokens allow the editor to color symbols based on their resolved meaning (e.g., distinguishing a local variable from a parameter, or a type reference from a namespace).

### Deliverables

- Create `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/SemanticTokensHandler.cs`
- Create `Tyhp/LanguageServer/Handlers/WorkspaceHandlers/WorkspaceSymbolHandler.cs`
- Modify `Tyhp/LanguageServer/TyhpLanguageServer.cs` — Add semantic tokens and workspace symbol `[JsonRpcMethod]` handler methods
- Modify `Tyhp/LanguageServer/Configuration/CapabilityRegistration.cs` — Declare semantic tokens legend and workspace symbol capability in `ServerCapabilities`

### Implementation Details

**`SemanticTokensHandler` (methods on `TyhpLanguageServer`):**
- Implemented as two `[JsonRpcMethod]` methods:
  - `[JsonRpcMethod("textDocument/semanticTokens/full", UseSingleObjectParameterDeserialization = true)]` returning `Task<SemanticTokens>`
  - `[JsonRpcMethod("textDocument/semanticTokens/full/delta", UseSingleObjectParameterDeserialization = true)]` returning `Task<SemanticTokensDelta>`
- Define the semantic token types legend:
  - `namespace`, `type`, `class`, `enum`, `interface`, `struct`, `typeParameter`
  - `parameter`, `variable`, `property`, `enumMember`, `function`, `method`
  - `keyword`, `modifier`, `comment`, `string`, `number`, `operator`
- Define the semantic token modifiers legend:
  - `declaration`, `definition`, `readonly`, `static`, `deprecated`, `abstract`
  - `async`, `modification`, `documentation`, `defaultLibrary`
- On `textDocument/semanticTokens/full`:
  1. Get the parsed AST and binder results for the document
  2. Walk the AST producing semantic tokens for each meaningful node:
     - Variable references → `variable` token type, with `readonly` modifier if const
     - Function/method names → `function`/`method` token type
     - Class/interface/trait names → `class`/`interface` token type
     - Type annotations → `type` token type
     - Parameters → `parameter` token type
     - Properties → `property` token type
     - Namespace segments → `namespace` token type
     - Generic type parameters → `typeParameter` token type
     - Enum cases → `enumMember` token type
     - Deprecated symbols → add `deprecated` modifier
     - Static members → add `static` modifier
     - Abstract members → add `abstract` modifier
  3. Encode as the LSP semantic tokens binary format (array of 5-tuples: deltaLine, deltaStartChar, length, tokenType, tokenModifiers)
- On `textDocument/semanticTokens/full/delta`:
  - Compare previous tokens with current tokens and return only the differences
  - Track previous result ID per document

**`WorkspaceSymbolHandler` (method on `TyhpLanguageServer`):**
- Implemented as a `[JsonRpcMethod("workspace/symbol", UseSingleObjectParameterDeserialization = true)]` method returning `Task<SymbolInformation[]>`
- On `workspace/symbol`:
  1. Take the query string from the request
  2. Search the `GlobalScope` for symbols matching the query:
     - Match by name (case-insensitive substring or fuzzy match)
     - Include: classes, interfaces, traits, enums, structs, functions, constants, type aliases
     - Exclude: variables, parameters, labels (too granular for workspace search)
  3. Return `SymbolInformation[]` with name, kind, location, and container name
  4. Limit results to a reasonable count (e.g., 100) for performance
- Support for qualified name search: `Namespace\ClassName` matches symbols by their fully qualified name

### Acceptance Criteria

- Variables, types, functions, and other symbols are colored differently based on semantic analysis (beyond simple syntax highlighting)
- Parameters and variables have different colors
- Static members are visually distinct from instance members
- Deprecated symbols show with strikethrough styling (via deprecated modifier)
- Type references in annotations are colored as types
- Generic type parameters have their own color
- Ctrl+T (Go to Symbol in Workspace) opens a search that finds classes, functions, and other declarations across the project
- Fuzzy matching works for workspace symbol search
- Results show the correct file and location
- Semantic token encoding produces valid LSP binary format
- `dotnet build` succeeds with no errors

### Dependencies

- **Requires:** Phase 3 (`AnalysisService` with binder results), Phase 4 (symbol resolution)
- **Provides for Phase 10:** Complete feature set for the language server

---

## Phase 10: VS Code Extension and Integration Testing

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Create a VS Code extension that launches the Tyhp language server, provides basic language configuration (file associations, bracket matching, comment toggling), and includes a TextMate grammar for base syntax highlighting. This phase also covers integration testing of the language server.

### Deliverables

- Create `vscode-tyhp/` directory at the project root
- Create `vscode-tyhp/package.json` — Extension manifest
- Create `vscode-tyhp/src/extension.ts` — Extension entry point
- Create `vscode-tyhp/language-configuration.json` — Bracket pairs, comments, auto-closing
- Create `vscode-tyhp/syntaxes/tyhp.tmLanguage.json` — TextMate grammar for `.tyhp` files
- Create `vscode-tyhp/syntaxes/tyhpdef.tmLanguage.json` — TextMate grammar for `.tyhpdef` files
- Create `vscode-tyhp/tsconfig.json` — TypeScript configuration
- Create `vscode-tyhp/.vscodeignore` — Files to exclude from VSIX package
- Modify `Tyhp/Config/DisplayHelp.cs` — Implement `LanguageServerHelp()` with full help text

### Implementation Details

**Extension manifest (`package.json`):**
- Extension ID: `tyhp.tyhp-lang`
- Display name: `Tyhp Language Support`
- Activation events: `onLanguage:tyhp`, `onLanguage:tyhpdef`
- Contributes:
  - Languages: `tyhp` (extensions: `.tyhp`), `tyhpdef` (extensions: `.tyhpdef`)
  - Grammars: TextMate grammars for both language IDs
  - Configuration:
    - `tyhp.languageServer.path` (string) — Path to the Tyhp compiler binary
    - `tyhp.languageServer.args` (string[]) — Additional arguments
    - `tyhp.projectFile` (string) — Path to `tyhp.json` (default: `./tyhp.json`)
    - `tyhp.diagnostics.enable` (boolean, default true)
    - `tyhp.completion.autoImport` (boolean, default true)

**Extension entry point (`extension.ts`):**
- On activation:
  1. Resolve the Tyhp compiler path from settings or `PATH`
  2. Create a `LanguageClient` from `vscode-languageclient` package
  3. Configure it to spawn the Tyhp compiler with `language_server` action: `tyhp language_server`
  4. Use stdio transport
  5. Set document selector for `tyhp` and `tyhpdef` languages
  6. Start the client
- On deactivation: stop the client

**Language configuration (`language-configuration.json`):**
- Comments: line `//`, block `/* */`
- Brackets: `{}`, `[]`, `()`, `<>`
- Auto-closing pairs: quotes, brackets, parentheses
- Surrounding pairs: quotes, brackets
- Folding markers: `#region` / `#endregion` (if supported), or brace-based
- Word pattern: PHP/Tyhp variable pattern (`\$?[a-zA-Z_]\w*`)
- Indentation rules: increase after `{`, decrease after `}`

**TextMate grammar (`tyhp.tmLanguage.json`):**
- Based on PHP grammar but extended for Tyhp syntax:
  - `<?tyhp` open tag (in addition to `<?php`)
  - `struct` keyword
  - `type` keyword (for type aliases)
  - `extension` keyword
  - `operator` keyword (for overloads)
  - `import` / `using` keywords
  - Generic syntax `<T>` in type positions
  - Disposable assignment `:=`
  - `async` / `await` keywords
  - `with` keyword
  - `nameof()`, `typeof()`, `default()` compile-time constructs
  - Type guard return syntax `$param is Type`
  - Property accessor syntax (`get`, `set` in property declarations)
- Scopes should follow standard TextMate naming conventions (e.g., `keyword.control.tyhp`, `entity.name.type.class.tyhp`, `variable.other.tyhp`)

**TextMate grammar (`tyhpdef.tmLanguage.json`):**
- Simpler grammar covering tyhpdef-specific syntax:
  - `import class`, `import function`, `import const`, `import enum` blocks
  - `deprecated`, `obsolete` keywords
  - Type annotations and function signatures
  - Namespace declarations

**`LanguageServerHelp()` in `DisplayHelp.cs`:**
- Usage: `tyhp language_server [options]`
- Description: Start the Tyhp Language Server Protocol (LSP) server for IDE integration
- Options:
  - `--stdio` — communicate via stdin/stdout (default, and the **only transport implemented in this story**; the skeleton in Phase 1 wires stdin/stdout exclusively)
  - `--tcp=<port>` — communicate via TCP on specified port *(documented for future use — not yet implemented; mark `// PLACEHOLDER_STORY_30: tcp transport`)*
  - `--pipe=<name>` — communicate via named pipe *(documented for future use — not yet implemented; mark `// PLACEHOLDER_STORY_30: named-pipe transport`)*
- Examples: `tyhp language_server` (start in stdio mode for VS Code/editor integration)
- Implement `LanguageServerHelp()` using `Message.Info()` and `Message.Display()` directly (without `HelpFormatting` utility). Story 13 will introduce `HelpFormatting` and refactor existing help methods to use it at that time.

**Integration testing approach:**
- Create test scripts that start the language server and send LSP messages via stdin
- Verify responses for:
  - `initialize` returns correct capabilities
  - `textDocument/didOpen` triggers diagnostics
  - `textDocument/completion` returns results
  - `textDocument/definition` returns correct location
  - `textDocument/hover` returns formatted content
  - `shutdown`/`exit` cleanly terminates
- Tests can use the `vscode-languageclient` test utilities or a simple stdin/stdout test harness

### Acceptance Criteria

- Installing the VS Code extension from the VSIX file succeeds
- Opening a `.tyhp` file activates the extension and starts the language server
- Basic syntax highlighting works (keywords, strings, numbers, comments, variables are colored)
- `.tyhpdef` files have syntax highlighting
- Bracket matching works for `{}`, `[]`, `()`, `<>`
- Comment toggling (Ctrl+/) works with `//` for line comments
- All LSP features from previous phases work through the VS Code extension:
  - Diagnostics appear in the Problems panel
  - Go-to-definition navigates correctly
  - Hover shows type information
  - Autocomplete works with trigger characters
  - Find references returns results
  - Rename works across files
  - Document outline shows symbols
  - Signature help shows during function calls
  - Code actions offer quick fixes
  - Semantic tokens provide richer coloring
- `tyhp help --subject=language_server` displays complete language server help text
- The extension gracefully handles the language server not being found (shows error message with setup instructions)
- The extension recovers if the language server crashes (auto-restart with backoff)
- `dotnet build` succeeds for the C# project
- `npm run compile` (or equivalent) succeeds for the VS Code extension
- `dotnet build` succeeds with no errors for the main project

### Dependencies

- **Requires:** Phases 1-9 (all language server features)
- **Provides:** Complete, usable VS Code extension for the Tyhp language

---

## Summary of Phases

| Phase | Title | Key Deliverables |
|-------|-------|------------------|
| 1 | Project Setup, NuGet, LSP Skeleton | `StreamJsonRpc` + `Microsoft.VisualStudio.LanguageServer.Protocol` packages, `LanguageServerAction`, `TyhpLanguageServer`, initialize/shutdown |
| 2 | Workspace Management, Document Sync | `WorkspaceManager`, `DocumentState`, didOpen/didChange/didClose |
| 3 | Analysis Service, Diagnostics | `AnalysisService`, `SymbolFinder`, `DiagnosticsPublisher`, position utilities |
| 4 | Go-to-Definition, Hover | `DefinitionHandler`, `HoverHandler`, symbol formatting |
| 5 | Completion (Autocomplete) | `CompletionHandler`, context detection, auto-import |
| 6 | Find References, Rename, Highlights | `ReferencesHandler`, `RenameHandler`, `DocumentHighlightHandler` |
| 7 | Document Symbols, Signature Help, Folding | `DocumentSymbolHandler`, `SignatureHelpHandler`, `FoldingRangeHandler` |
| 8 | Code Actions, Formatting, Selection | `CodeActionHandler`, `FormattingHandler`, `SelectionRangeHandler` |
| 9 | Semantic Tokens, Workspace Symbols | `SemanticTokensHandler`, `WorkspaceSymbolHandler` |
| 10 | VS Code Extension, Integration Testing | Extension manifest, TextMate grammar, integration tests |

---

## Cross-Cutting Concerns

### Error Handling

- All handlers must catch exceptions and return appropriate LSP error responses (never crash the server)
- If analysis fails for a document, log the error and return partial results where possible
- The server should continue operating even if individual requests fail

### Performance

- Document analysis should be debounced and cancellable
- Large projects should not block the server on startup — initial project analysis should be asynchronous
- Consider caching analysis results and only re-analyzing changed files
- The `AstCacheService` can be leveraged for cross-session AST caching

### Logging

- Use the LSP `window/logMessage` notification for server-side logging
- Log at appropriate levels: errors for failures, warnings for degraded functionality, info for lifecycle events, trace for debugging
- Use the existing `Message` class patterns where appropriate, but prefer LSP logging for server-mode output

### Configuration Changes

- Watch for `tyhp.json` changes and re-analyze the project when configuration changes
- Watch for `.tyhpdef` file changes (if they're being edited alongside the project)
- Handle workspace folder changes (multi-root workspaces)

---

*Last updated: 2026-02-16*

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the LSP implementation. Steps can be skipped, reordered, or modified as appropriate for your environment. You will need a working `dotnet build`, a built `tyhp` binary, and VS Code installed.

### Step 1: Verify the Build Compiles

```bash
cd /path/to/tyhp
dotnet build
```

Confirm the build succeeds with zero errors. The `StreamJsonRpc` and `Microsoft.VisualStudio.LanguageServer.Protocol` NuGet packages should restore without issues.

### Step 2: Verify LSP Server Starts and Responds to Initialize

Start the language server manually and send a raw LSP `initialize` request via stdin. Create a test script file `test_lsp_init.sh`:

```bash
#!/bin/bash
# Content-Length header is required by LSP
REQUEST='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":null,"rootUri":"file:///tmp/tyhp-test","capabilities":{}}}'
CONTENT_LENGTH=${#REQUEST}

printf "Content-Length: %d\r\n\r\n%s" "$CONTENT_LENGTH" "$REQUEST" | dotnet run -- language_server
```

**Expected:** A JSON-RPC response on stdout containing `"result"` with `"capabilities"` including at minimum `"textDocumentSync"`. The server should block waiting for more input after the response.

### Step 3: Verify Shutdown and Exit

Extend the test script to send `initialize`, then `shutdown`, then `exit`:

```bash
#!/bin/bash
INIT='{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":null,"rootUri":"file:///tmp/tyhp-test","capabilities":{}}}'
INITIALIZED='{"jsonrpc":"2.0","method":"initialized","params":{}}'
SHUTDOWN='{"jsonrpc":"2.0","id":2,"method":"shutdown"}'
EXIT='{"jsonrpc":"2.0","method":"exit"}'

send_msg() {
  local msg="$1"
  printf "Content-Length: %d\r\n\r\n%s" "${#msg}" "$msg"
}

{
  send_msg "$INIT"
  sleep 0.5
  send_msg "$INITIALIZED"
  sleep 0.5
  send_msg "$SHUTDOWN"
  sleep 0.5
  send_msg "$EXIT"
} | dotnet run -- language_server
```

**Expected:** The process exits cleanly with exit code 0 after receiving `exit`.

### Step 4: Verify Diagnostics with a .tyhp File

Create a small project for testing:

```bash
mkdir -p /tmp/tyhp-lsp-test/src
```

Create `/tmp/tyhp-lsp-test/tyhp.json`:

```json
{
  "include": ["src/**/*.tyhp"],
  "output": { "path": "build/" }
}
```

Create `/tmp/tyhp-lsp-test/src/test.tyhp` with intentional syntax errors:

```tyhp
<?tyhp

function greet(string $name): string {
    return "Hello, " . $name
}
```

Open this project in VS Code with the Tyhp extension installed (see Step 8). The missing semicolon on line 4 should produce a diagnostic in the Problems panel.

**Expected:** A diagnostic error pointing to line 4, indicating the missing semicolon or unexpected token.

### Step 5: Verify Go-to-Definition and Hover

Create `/tmp/tyhp-lsp-test/src/definitions.tyhp`:

```tyhp
<?tyhp

class User {
    public string $name;
    public int $age;

    public function greet(): string {
        return "Hi, I'm " . $this->name;
    }
}

function createUser(): User {
    $user = new User();
    $user->name = "Alice";
    $user->age = 30;
    return $user;
}
```

Open this file in VS Code:

- **Go-to-definition:** Place cursor on `User` in the `createUser` function's return type and press F12 (or Ctrl+Click). It should jump to the `class User` declaration.
- **Go-to-definition:** Place cursor on `$this->name` inside `greet()` and press F12. It should jump to `public string $name`.
- **Hover:** Hover over `$user` after the `new User()` assignment. It should show the type as `User`.
- **Hover:** Hover over the `greet` method name. It should show the full method signature: `public function greet(): string`.

### Step 6: Verify Autocomplete

In the same file, add a new function body and test autocomplete:

```tyhp
function testCompletion(): void {
    $user = new User();
    $user->
}
```

- After typing `$user->`, the autocomplete menu should appear showing `name`, `age`, and `greet()`.
- After typing `$u` at the start of a line, autocomplete should suggest `$user` from the local scope.
- In a type annotation position (e.g., after `function foo(`), typing should suggest available types like `User`, `string`, `int`, etc.

### Step 7: Verify Find References and Rename

Using the `definitions.tyhp` file from Step 5:

- **Find References:** Right-click on `$name` in the `User` class property and select "Find All References." It should list: the declaration, the usage in `greet()`, and the assignment in `createUser()`.
- **Rename:** Right-click on `$name` in the `User` class property and select "Rename Symbol." Rename it to `$fullName`. All three usages should update.
- **Rename rejection:** Try to rename a built-in type (like `string`) — it should refuse.

### Step 8: Install and Test the VS Code Extension

Build and install the VS Code extension:

```bash
cd vscode-tyhp
npm install
npm run compile
npx vsce package
code --install-extension tyhp-lang-*.vsix
```

After installing:

1. Open a `.tyhp` file — the extension should activate and the language server should start (check Output panel > "Tyhp Language Server").
2. Verify syntax highlighting: keywords (`class`, `function`, `if`, `return`) should be colored differently from strings, numbers, and identifiers.
3. Verify bracket matching: place cursor on `{` and its matching `}` should highlight.
4. Verify comment toggling: select a line and press Ctrl+/ — it should toggle `//` comments.
5. Open a `.tyhpdef` file — it should also have basic syntax highlighting.

### Step 9: Verify Document Symbols and Folding

Open a `.tyhp` file with classes, methods, and properties:

- **Outline panel:** The VS Code Outline view (sidebar) should show a tree: the class, its methods, and properties with correct icons.
- **Breadcrumbs:** The breadcrumb bar at the top of the editor should show `file > namespace > class > method` as you navigate.
- **Folding:** Click the fold arrow next to a class or function definition — the body should collapse. Multi-line doc comments should also be foldable.

### Step 10: Verify Signature Help

Create a function with multiple parameters and test signature help:

```tyhp
<?tyhp

function calculate(int $a, float $b, string $label): string {
    return $label . ": " . ($a + $b);
}

$result = calculate(
```

- After typing `(`, a signature help popup should appear showing `calculate(int $a, float $b, string $label): string` with `$a` highlighted as the active parameter.
- After typing `1, `, the active parameter highlight should move to `$b`.
- After typing `1, 2.5, `, the active parameter highlight should move to `$label`.

### Step 11: Verify Semantic Tokens

Open a file with various symbol types and check that semantic coloring is richer than basic syntax highlighting:

- Variables and parameters should have different colors.
- Type references in annotations (e.g., `User $user`) should be colored as types.
- Static members should be visually distinct.
- Deprecated symbols (if any) should show strikethrough.

### Step 12: Verify Code Actions

Create a file that references a type from another namespace without importing it:

```tyhp
<?tyhp

namespace App\Controllers;

function getUser(): User {
    return new User();
}
```

If `User` is defined in `App\Models`, the lightbulb icon should appear offering "Import App\Models\User" as a quick fix. Selecting it should add `use App\Models\User;` at the top of the file.

### Step 13: Verify Workspace Symbol Search

Press Ctrl+T (Go to Symbol in Workspace) and type a class or function name. Results should appear from across all project files with correct file locations and symbol kinds.

### Step 14: Verify Error Recovery

The language server should not crash on malformed input:

- Open a `.tyhp` file and type random characters — diagnostics should appear but the server should remain responsive.
- Delete half a class definition — diagnostics should update and other features (hover on valid symbols) should still work.
- Save a file with syntax errors — diagnostics should persist; fixing them should clear diagnostics.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
