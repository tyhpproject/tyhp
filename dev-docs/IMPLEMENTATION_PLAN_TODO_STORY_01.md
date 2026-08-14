# Implementation Plan: Story 01 — Foundation (Diagnostic System, Compilation Pipeline, Build Endpoint)

> **Roadmap position:** Story 01 — **Tier 0 — Spine**
> **Direct dependencies (new numbering):** none — this is the bootstrap story
> **Renumbered from:** legacy Story 0
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 01 of the Tyhp compiler TODO
> **Branch:** TBD
> **Prerequisite:** Parser, AST system, Visitor system, CLI infrastructure — all complete and functional
> **Status:** COMPLETED ON 2026-02-19 (EXECUTE, OPTIMIZE)

---

## Architecture Overview

### Current System Architecture

The Tyhp compiler is a .NET 9 (C# 13) application built as a hosted service using `Microsoft.Extensions.Hosting`. The entry point is `Program.cs`, which configures DI, localization, and launches `TyhpHostedService`. CLI actions are routed via the `Tyhp.Config.Action` enum and dispatched in `TyhpHostedService.StartAsync()`. Each action is an `ActionRunnerBase` subclass.

The existing pipeline works as follows:
- **Parser:** ANTLR4 grammars (`TyhpLexer.g4`, `TyhpParser.g4`) produce `TyhpLexer`/`TyhpParser` C# classes in `Tyhp/TyhpLang/Parser/` (regenerate with `./compile_grammar.sh` after editing `Tyhp/TyhpLang/Grammar/`; then `dotnet clean && dotnet restore && dotnet build`)
- **Visitor:** `PhpParserAstVisitor` (base) + `TyhpParserAstVisitor` (extends) walk parse trees, producing `Base2Ast`-derived AST nodes stored in `Tyhp/TyhpLang/Ast/`
- **AST Caching:** `AstCacheService` provides in-memory + file-based caching with SHA256/MD5 invalidation
- **CLI:** `TyhpHostedService` dispatches to action classes. Only `DebugAction`, `GenerateTyhpdefAction`, and `IntegrityCheckAction` are implemented

### Story 01 Goal

Establish three foundational systems that every subsequent phase depends on:

1. **Unified Diagnostic System** — Replace the fragmented error handling (three exception types with inconsistent naming, raw string error bags, 110+ crash-on-error throws in the visitor) with a structured, thread-safe `DiagnosticBag` and `IDiagnostic` interface
2. **Compilation Service** — Extract reusable multi-threaded parsing logic from `DebugAction` into a shared `CompilationService` that `BuildAction`, `LintAction`, and the future language server can all use
3. **Build/Lint CLI Endpoints** — Create skeleton `BuildAction` and `LintAction` classes wired into `TyhpHostedService`, exercising the full pipeline with placeholder steps for binder/checker/emitter

### Key Patterns and Conventions

- **Namespace convention:** `Tyhp.Domain.*` for domain types, `Tyhp.CLI.*` for CLI, `Tyhp.TyhpLang.*` for compiler internals
- **Thread safety:** Parsing is multi-threaded using `Parallel.ForEach` with `ThreadLocal<>` lexer/parser instances. Any shared state must use concurrent collections
- **AST base class:** All AST nodes extend `Base2Ast`, which provides `Line`, `Column`, `StartIndex`, `LanguageMode`, serialization/deserialization, and child management
- **Localization:** `IStringLocalizer<TyhpHostedService>` is wired via DI. Message codes follow `ERROR_TYHP{code}` / `WARNING_TYHP{code}` / `INFO_TYHP{code}` key format in `.resx` resource files
- **Action pattern:** Actions extend `ActionRunnerBase`, implement `Start(CancellationToken)`, and are instantiated/dispatched in `TyhpHostedService.StartAsync()`
- **Console output:** All output goes through `Tyhp.CLI.Message` static class using `ConcurrentWriter` from the Konsole library

### MessageCode Numbering Scheme (Established)

| Range | Component |
|-------|-----------|
| 1000s | Parser/Lexer/Grammar |
| 2000s | Visitor/AST Generation |
| 3000s | Binder |
| 4000s | Checker |
| 5000s | Emitter |
| 6000s | Configuration (reserved) |
| 7000s | CLI (reserved) |
| 8000s | Tyhpdef (reserved) |
| 9000s | Internal Compiler Errors (reserved) |

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup: `<filename>.<YYYYMMDD_HHMMSS>.backup`
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: Diagnostic Infrastructure — Core Types

### Overview

Create the foundational diagnostic types: `DiagnosticSeverity` enum, `IDiagnostic` interface, `Diagnostic` record class, and `DiagnosticBag` thread-safe collector. These types form the universal error/warning/info reporting mechanism used by every compiler phase.

### Deliverables

- `Tyhp/Domain/Diagnostics/DiagnosticSeverity.cs` — Severity enum
- `Tyhp/Domain/Diagnostics/IDiagnostic.cs` — Diagnostic interface
- `Tyhp/Domain/Diagnostics/Diagnostic.cs` — Concrete diagnostic record
- `Tyhp/Domain/Diagnostics/DiagnosticBag.cs` — Thread-safe collection
- `Tyhp/Domain/Diagnostics/IDiagnosticFormatter.cs` — Formatter strategy interface (for future JSON/SARIF output)
- `Tyhp/Domain/Diagnostics/ConsoleDiagnosticFormatter.cs` — Default console formatter delegating to `Message` class

### Implementation Details

**`DiagnosticSeverity.cs`**

Create an enum in namespace `Tyhp.Domain.Diagnostics` with four values:
- `Error` — compilation fails
- `Warning` — compilation succeeds but suspicious
- `Info` — informational (style, deprecation)
- `Hint` — low-priority suggestions (for future LSP use)

**`IDiagnostic.cs`**

Interface with the following contract:
- `DiagnosticSeverity Severity { get; }`
- `Tyhp.Domain.Exceptions.MessageCode Code { get; }`
- `string FileName { get; }`
- `int Line { get; }`
- `int Column { get; }`
- `int? EndLine { get; }` — optional, for IDE diagnostic spans
- `int? EndColumn { get; }` — optional, for IDE diagnostic spans
- `string Message { get; }` — localized human-readable message
- `object[] FormatParams { get; }` — parameters for message formatting
- `void Display(IDiagnosticFormatter? formatter = null)` — output via formatter (default: console)

**`Diagnostic.cs`**

Implement as a `record class` for value equality (useful in tests). Include:
- Constructor accepting all fields
- Static factory methods: `Diagnostic.Error(...)`, `Diagnostic.Warning(...)`, `Diagnostic.Info(...)`, `Diagnostic.Hint(...)`
- Each factory method takes `(MessageCode code, string fileName, int line, int column, params object[] formatParams)` and optional `int? endLine = null, int? endColumn = null`
- `Display()` implementation delegates to the formatter or falls back to `ConsoleDiagnosticFormatter`
- The `Message` property getter should call `CLI.Message.LocalizeErrorCode()`, `LocalizeWarningCode()`, or `LocalizeInfoCode()` based on severity, to produce the localized text

**`DiagnosticBag.cs`**

Thread-safe diagnostic collector using `ConcurrentBag<IDiagnostic>` internally:
- `void Add(IDiagnostic diagnostic)` — add single diagnostic
- `void AddRange(IEnumerable<IDiagnostic> diagnostics)` — merge collections
- `void AddError(MessageCode code, string fileName, int line, int column, params object[] formatParams)` — convenience
- `void AddWarning(...)` — convenience
- `void AddInfo(...)` — convenience
- `bool HasErrors { get; }` — any Error-severity?
- `bool HasWarnings { get; }` — any Warning-severity?
- `int ErrorCount { get; }` — count of errors
- `int WarningCount { get; }` — count of warnings
- `int InfoCount { get; }` — count of infos
- `IReadOnlyList<IDiagnostic> Errors { get; }` — filtered view
- `IReadOnlyList<IDiagnostic> Warnings { get; }` — filtered view
- `IReadOnlyList<IDiagnostic> All { get; }` — all diagnostics ordered by file then line
- `void DisplayAll(IDiagnosticFormatter? formatter = null)` — display all
- Implement `IEnumerable<IDiagnostic>` for LINQ support

**`IDiagnosticFormatter.cs`**

Strategy interface for output formatting:
- `void Format(IDiagnostic diagnostic)` — format a single diagnostic
- `void FormatSummary(DiagnosticBag bag)` — format the summary (counts)

**`ConsoleDiagnosticFormatter.cs`**

Default formatter that delegates to the existing `Tyhp.CLI.Message.TyhpError(...)`, `Message.TyhpWarn(...)`, `Message.TyhpInfo(...)` methods with the file/line/column/code overloads. This preserves the existing console output format: `filename(line,column): error TYHP1001: message text`.

### Acceptance Criteria

- All new files compile without errors
- `DiagnosticBag` is thread-safe: concurrent `Add()` calls from multiple threads do not corrupt state
- `Diagnostic.Error(MessageCode.ParserUnknownError, "test.tyhp", 1, 0)` creates a diagnostic with `Severity == Error`, correct code, file, line, column
- `DiagnosticBag.HasErrors` returns `true` after adding an error diagnostic, `false` when only warnings/info
- `DiagnosticBag.All` returns diagnostics sorted by file name then line number
- `Display()` produces output in the same format as the existing `Message.TyhpError(fileName, line, col, code, params)` format
- No changes to existing files in this phase

### Dependencies

- **Requires:** Nothing (new files only, referencing existing `MessageCode` enum and `Message` class)
- **Provides:** Core diagnostic types for all subsequent phases

---

## Phase 2: CompilationResult and AST-Diagnostic Bridge

### Overview

Create the `CompilationResult` aggregation type that carries all phase outputs and diagnostics through the pipeline. Also create the helper bridge between AST nodes and diagnostics, and ensure `SrcFileAst` carries its filename.

### Deliverables

- `Tyhp/Domain/Diagnostics/CompilationResult.cs` — Pipeline result aggregator
- `Tyhp/Domain/Diagnostics/DiagnosticExtensions.cs` — Extension methods for AST-to-diagnostic bridge
- Modification to `Tyhp/Domain/Exceptions/MessageCode.cs` — Add new message codes and document numbering scheme
- Verification of `Tyhp/TyhpLang/Ast/SrcFileAst.cs` filename storage (read-only check, modify only if needed)

### Implementation Details

**`CompilationResult.cs`**

Create in namespace `Tyhp.Domain.Diagnostics`:
- `DiagnosticBag Diagnostics { get; }` — instantiated in constructor, collects all diagnostics across all phases
- `bool Success => !Diagnostics.HasErrors` — convenience
- `IReadOnlyList<SrcFileAst>? ParsedFiles { get; set; }` — set after parse phase
- `Tyhp.TyhpLang.Binder.Scopes.GlobalScope? GlobalScope { get; set; }` — set after bind phase
- `IReadOnlyList<Tyhp.TyhpLang.Emitter.PHPOutputFile>? OutputFiles { get; set; }` — set after emit phase
- `TimeSpan ParseDuration { get; set; }`
- `TimeSpan BindDuration { get; set; }`
- `TimeSpan CheckDuration { get; set; }`
- `TimeSpan EmitDuration { get; set; }`
- `Tyhp.Domain.Enums.ExitCode GetExitCode()` — returns `Success` if no errors/warnings, `CompileError` if errors, `CompileWarning` if warnings only

**`DiagnosticExtensions.cs`**

Static extension methods for creating diagnostics from AST nodes:
- `static void AddFromAst(this DiagnosticBag bag, DiagnosticSeverity severity, MessageCode code, IBase2Ast node, string fileName, params object[] formatParams)`
  - Extracts `node.Line` and `node.Column` automatically
- `static void AddErrorFromAst(this DiagnosticBag bag, MessageCode code, IBase2Ast node, string fileName, params object[] formatParams)` — convenience
- `static void AddWarningFromAst(this DiagnosticBag bag, MessageCode code, IBase2Ast node, string fileName, params object[] formatParams)` — convenience

This avoids every call site manually extracting line/column from AST nodes.

**`MessageCode.cs` Updates**

Add new codes and document the numbering scheme at the top of the file:
- Add a comment block at the top documenting the range scheme (1000s=parser, 2000s=visitor, etc.)
- Reserve 6000s for configuration, 7000s for CLI, 8000s for tyhpdef, 9000s for internal compiler errors
- Add visitor-specific codes:
  - `VisitorUnexpectedAlternative = 2002`
  - `VisitorMissingRequiredNode = 2003`
  - `VisitorUnsupportedConstruct = 2004`

**`SrcFileAst` Verification**

`SrcFileAst` already stores the filename via its `Identifier` property (set to `Path.GetFullPath(fileName)` in `AbstractCreate`) and exposes it via `FileName => AstCacheService.GetRelativePath(this.Identifier)`. This is sufficient. No modification needed unless the relative path resolution causes issues, in which case add an `AbsoluteFilePath` property.

### Acceptance Criteria

- `CompilationResult` compiles and can hold parsed files, diagnostics, and timing data
- `GetExitCode()` returns `ExitCode.Success` when no diagnostics, `ExitCode.CompileError` when errors exist, `ExitCode.CompileWarning` when only warnings exist
- `DiagnosticExtensions.AddFromAst()` correctly extracts line/column from a `Base2Ast` node
- `MessageCode.cs` has the new visitor codes (2002, 2003, 2004) and range documentation comments
- All existing code continues to compile without errors

### Dependencies

- **Requires:** Phase 1 (DiagnosticBag, IDiagnostic, Diagnostic)
- **Provides:** CompilationResult for CompilationService (Phase 4) and BuildAction (Phase 6); DiagnosticExtensions for visitor refactor (Phase 3) and future binder/checker phases

---

## Phase 3: Refactor Exception Classes and Error AST Node

### Overview

Refactor the existing `TyhpError`, `TyhpWarning`, `TyhpInfo` exception classes to bridge with the new diagnostic system. Fix the naming inconsistency (`TyhpCode` vs `MessageCode`). Create or verify the `ErrorAst` node type for representing parse-time errors in the AST tree.

### Deliverables

- Modified `Tyhp/Domain/Exceptions/TyhpError.cs` — Add `ToDiagnostic()` method
- Modified `Tyhp/Domain/Exceptions/TyhpWarning.cs` — Rename `TyhpCode` to `MessageCode`, add `ToDiagnostic()`
- Modified `Tyhp/Domain/Exceptions/TyhpInfo.cs` — Rename `TyhpCode` to `MessageCode`, add `ToDiagnostic()`
- `Tyhp/TyhpLang/Ast/ErrorAst.cs` — Error AST node for representing parse/visitor errors in the tree (create if it doesn't exist)

### Implementation Details

**Exception Class Refactoring**

For each of `TyhpError`, `TyhpWarning`, `TyhpInfo`:

1. **Fix naming inconsistency:** In `TyhpWarning.cs` and `TyhpInfo.cs`, rename the property `TyhpCode` to `MessageCode` to match `TyhpError.MessageCode`. This is a breaking change within the codebase, so search for all usages of `.TyhpCode` on warning/info instances and update them.

2. **Add `ToDiagnostic()` method** on each class that converts the exception to an `IDiagnostic`:
   - `TyhpError.ToDiagnostic()` returns `Diagnostic.Error(this.MessageCode, this.FileName, this.LineNumber, this.Column, this.FormatParams)`
   - `TyhpWarning.ToDiagnostic()` returns `Diagnostic.Warning(...)` (using the renamed `MessageCode` property, cast from int to `MessageCode` enum)
   - `TyhpInfo.ToDiagnostic()` returns `Diagnostic.Info(...)`

3. **Add a constructor overload** on `TyhpWarning` and `TyhpInfo` that accepts `Tyhp.Domain.Exceptions.MessageCode` (the enum) in addition to the existing `int` constructor, matching what `TyhpError` already has.

4. **Refactor `Display()`** on each to delegate through the diagnostic system rather than calling `Message` directly. This eliminates duplicated formatting logic. The `Display()` method becomes: `this.ToDiagnostic().Display();`

**Search for `TyhpCode` usages before renaming:**
- `TyhpBinder.cs` has `List<TyhpWarning>` and `List<TyhpInfo>` — check if anything accesses `.TyhpCode` on those
- Any other files referencing `TyhpWarning.TyhpCode` or `TyhpInfo.TyhpCode`

**`ErrorAst.cs`**

The `ErrorAst` class does not currently exist in the codebase (confirmed by glob search). Create it:
- Extends `Base2Ast`
- Represents a parse-time or visitor-time error in the AST tree
- Has a `string ErrorMessage` property for the error description
- Has a `MessageCode Code` property for the error code
- Static factory method: `ErrorAst.Create(ParserRuleContext context, string? languageMode = null)` — creates an error node with the position from the context
- Must be registered in `AstNodeTypeRegistry` so it can be serialized/deserialized (follow the pattern of other AST node registrations)
- Later phases (binder, checker) should detect `ErrorAst` nodes and skip them gracefully

### Acceptance Criteria

- `TyhpWarning.MessageCode` and `TyhpInfo.MessageCode` properties exist (old `TyhpCode` is gone)
- All existing usages of `TyhpCode` have been updated — project compiles
- `TyhpError.ToDiagnostic()` returns a `Diagnostic` with `Severity == Error` and matching code/file/line/column
- `TyhpWarning.ToDiagnostic()` returns `Severity == Warning`
- `TyhpInfo.ToDiagnostic()` returns `Severity == Info`
- `ErrorAst` can be created from a `ParserRuleContext` and carries line/column/error information
- `ErrorAst` can be serialized and deserialized via `Base2Ast.Serialize()`/`Deserialize()`
- `ErrorAst` can participate as a child in any AST node list without breaking tree structure

### Dependencies

- **Requires:** Phase 1 (Diagnostic class and factory methods)
- **Provides:** `ToDiagnostic()` bridge for converting caught exceptions to bag entries; `ErrorAst` for visitor error recovery (Phase 5)

---

## Phase 4: ANTLR Error Listener Refactor

### Overview

Extract the `CustomErrorListener<TType>` nested class from `DebugAction` into a shared, reusable class that writes into a `DiagnosticBag` instead of a `ConcurrentBag<(string, string)>`. This is needed before extracting the compilation service, since the error listener is part of the parsing pipeline.

### Deliverables

- `Tyhp/TyhpLang/Parser/TyhpAntlrErrorListener.cs` — Shared ANTLR error listener writing to `DiagnosticBag`
- Modified `Tyhp/CLI/DebugAction.cs` — Replace `CustomErrorListener` with `TyhpAntlrErrorListener`, replace `ConcurrentBag<(string, string)> errors` with `DiagnosticBag`

### Implementation Details

**`TyhpAntlrErrorListener.cs`**

Create in namespace `Tyhp.TyhpLang.Parser`:
- Generic class `TyhpAntlrErrorListener<TType>` implementing `IAntlrErrorListener<TType>`
- Constructor takes a `DiagnosticBag` reference
- `SetFileName(string fileName)` method for thread-local filename tracking (same pattern as current)
- `SyntaxError(...)` implementation:
  - Filter out `reportAttemptingFullContext`, `reportContextSensitivity`, and `failed predicate` messages (current behavior)
  - For remaining errors: create a `Diagnostic` and add to the bag
  - Map to `MessageCode`:
    - If `TType` is `int` (lexer errors): use `MessageCode.ParserUnknownError` or a new lexer-specific code
    - If `TType` is `IToken` (parser errors): use `MessageCode.ParserUnexpectedError`
  - Include the offending token text (via `recognizer.Vocabulary.GetDisplayName(tokenType)`) as a format parameter
  - Use `line` and `charPositionInLine` from the `SyntaxError` callback for diagnostic position

**`DebugAction.cs` Updates**

This is a careful refactor of a 580-line file. Create a backup before modifying.

1. Remove the nested `CustomErrorListener<TType>` class entirely
2. Replace `private ConcurrentBag<(string fileName, string message)> errors` with `private DiagnosticBag diagnostics = new DiagnosticBag();`
3. Update the constructor where `CustomErrorListener` instances are created:
   - Replace `new CustomErrorListener<int>("LEXER", errors)` with `new TyhpAntlrErrorListener<int>(diagnostics)`
   - Replace `new CustomErrorListener<IToken>("PARSER", errors)` with `new TyhpAntlrErrorListener<IToken>(diagnostics)`
4. Update `SetFileName` calls to use the new listener type
5. Update all references to `this.errors`:
   - `this.errors.Count` → `this.diagnostics.ErrorCount`
   - `this.errors.IsEmpty` → `!this.diagnostics.HasErrors`
   - `this.errors.Add(...)` → `this.diagnostics.AddError(...)`
   - Error summary display: replace `string.Join` of tuples with `this.diagnostics.DisplayAll()`
6. Update progress bar display strings that reference `err:{this.errors.Count}` → `err:{this.diagnostics.ErrorCount}`
7. In `GetLexerAndParser()` method: update to use `TyhpAntlrErrorListener` instead of `CustomErrorListener`
8. In `ParseFile()` exception catch blocks: replace `this.errors.Add((fileData.Key, ...))` with `this.diagnostics.AddError(MessageCode.ParserUnknownError, fileData.Key, 0, 0, e.Message)`

### Acceptance Criteria

- `TyhpAntlrErrorListener<TType>` compiles and correctly implements `IAntlrErrorListener<TType>`
- `DebugAction` no longer contains `CustomErrorListener` nested class
- `DebugAction` uses `DiagnosticBag` instead of `ConcurrentBag<(string, string)>`
- Running the debug action still works: files parse, errors are collected, summary is displayed
- Error output format from `DebugAction` matches or improves on previous format (uses proper `filename(line,col): error TYHP1001: ...` format)
- The `TyhpAntlrErrorListener` can be reused by any code that creates an ANTLR lexer/parser

### Dependencies

- **Requires:** Phase 1 (DiagnosticBag), Phase 2 (MessageCode updates)
- **Provides:** Shared error listener for CompilationService (Phase 5)

---

## Phase 5: Visitor Error Handling Refactor

### Overview

Add a `DiagnosticBag` to the visitor classes and replace all ~110+ `throw new InvalidOperationException` / `throw new Exception` calls across the 19+ visitor partial class files with diagnostic collection + `ErrorAst` return. This is the largest single phase by file count but follows a highly mechanical pattern.

### Deliverables

- Modified `Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.cs` — Add `DiagnosticBag Diagnostics` property and constructor parameter
- Modified `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.cs` — Pass `DiagnosticBag` through to base constructor
- Modified visitor partial classes (19 files) — Replace `throw` statements with diagnostic + `ErrorAst`
- Modified `Tyhp/CLI/DebugAction.cs` — Pass `DiagnosticBag` when constructing the visitor

### Implementation Details

**Add `DiagnosticBag` to Visitor Base Class**

In `PhpParserAstVisitor.cs`:
- Add a `public DiagnosticBag Diagnostics { get; }` property
- Update the constructor to accept and store a `DiagnosticBag` parameter:
  `public PhpParserAstVisitor(CommonTokenStream? tokens, string filename, string fileHash, DiagnosticBag diagnostics)`
- The `DiagnosticBag` is shared across all partial classes since they are the same class

In `TyhpParserAstVisitor.cs`:
- Update the constructor to accept and pass through the `DiagnosticBag`:
  `public TyhpParserAstVisitor(CommonTokenStream? tokens, string filename, string fileHash, DiagnosticBag diagnostics) : base(tokens, filename, fileHash, diagnostics)`

**Replace Throw Statements**

For each visitor partial class, replace the pattern:

```csharp
// BEFORE:
_ => throw new InvalidOperationException("Unexpected X alternative: " + context.GetType().Name),
```

With:

```csharp
// AFTER:
_ => {
    this.Diagnostics.AddError(
        MessageCode.VisitorUnexpectedAlternative,
        this._filename, context.Start.Line, context.Start.Column,
        "X", context.GetType().Name
    );
    return ErrorAst.Create(context, PhpParserAstVisitor.GetCurrentLanguageMode(context));
}
```

**File-by-file approach (process in this order, highest throw count first):**

1. `PhpParserAstVisitor.PhpDereferenceables.cs` — ~29 throws
2. `PhpParserAstVisitor.PhpObjects.cs` — ~22 throws
3. `PhpParserAstVisitor.PhpIdentifiers.cs` — ~19 throws
4. `PhpParserAstVisitor.PhpTopStatements.cs` — ~11 throws
5. `PhpParserAstVisitor.PhpStatements.cs` — ~10 throws
6. `PhpParserAstVisitor.PhpTypes.cs` — ~8 throws
7. `TyhpParserAstVisitor.Tyhpdef.cs` — ~6 throws
8. `TyhpParserAstVisitor.TyhpObjects.cs` — ~4 throws
9. `PhpParserAstVisitor.PhpBlocks.cs` — ~2 throws
10. `TyhpParserAstVisitor.TyhpGenerics.cs` — ~2 throws
11. `PhpParserAstVisitor.PhpExpressions.cs` — ~1 throw
12. `PhpParserAstVisitor.PhpFunctions.cs` — ~1 throw
13. `PhpParserAstVisitor.PhpRoot.cs` — ~1 throw
14. `PhpParserAstVisitor.PhpTryCatchBlocks.cs` — ~1 throw
15. `PhpParserAstVisitor.PhpParametersAndArguments.cs` — ~1 throw
16. `TyhpParserAstVisitor.TyhpRoot.cs` — ~1 throw
17. `TyhpParserAstVisitor.TyhpFunctions.cs` — ~1 throw
18. `TyhpParserAstVisitor.TyhpReturnTypes.cs` — ~1 throw
19. `TyhpParserAstVisitor.TyhpIdentifiers.cs` — ~1 throw

**Important:** Some throw locations may require returning different AST interface types. The `ErrorAst` must implement or be castable to the expected return type. Options:
- Make `ErrorAst` implement the commonly expected interfaces (`IExpression`, `IStatement`, `ITopStatement`, etc.) with no-op/default implementations
- Or use separate error node types per interface (more complex, less recommended)
- The simplest approach: Make `ErrorAst` implement the most commonly used interfaces. For any location where a very specific interface is required, return `null` if the return type is nullable, or create a minimal typed wrapper

**Update `DebugAction.cs` Visitor Instantiation**

In `ParseFile()` method, update the `TyhpParserAstVisitor` construction:

```csharp
// BEFORE:
ast = new TyhpParserAstVisitor(
    parser.TokenStream as CommonTokenStream,
    fileData.Key,
    fileDataHash
).Visit(ctx) as SrcFileAst;

// AFTER:
var visitor = new TyhpParserAstVisitor(
    parser.TokenStream as CommonTokenStream,
    fileData.Key,
    fileDataHash,
    this.diagnostics
);
ast = visitor.Visit(ctx) as SrcFileAst;
```

### Acceptance Criteria

- Zero `throw new InvalidOperationException` or `throw new Exception` calls remain in visitor partial classes (search for `throw new InvalidOperationException` and `throw new Exception` in `Tyhp/TyhpLang/Visitor/`)
- The compiler no longer crashes when encountering unexpected grammar alternatives — it collects diagnostics and continues
- All diagnostics from the visitor include correct file name, line, and column information
- The AST tree remains structurally valid even when it contains `ErrorAst` nodes
- The debug action still parses files correctly (existing functionality preserved)
- All visitor partial classes compile without errors
- `ErrorAst` nodes appear in the AST tree where errors occurred (verifiable by examining the parsed AST for a file with intentional syntax errors)

### Dependencies

- **Requires:** Phase 1 (DiagnosticBag), Phase 2 (MessageCode visitor codes), Phase 3 (ErrorAst), Phase 4 (DebugAction already uses DiagnosticBag)
- **Provides:** Non-crashing visitor for CompilationService; diagnostic-producing visitor for all future phases

---

## Phase 6: CompilationService and CompilationOptions

### Overview

Extract the reusable compilation pipeline from `DebugAction` into a shared `CompilationService` class. This service handles file discovery, multi-threaded parsing, visitor execution, AST caching, error collection, and progress reporting. Both `DebugAction` and the new `BuildAction` will use it.

### Deliverables

- `Tyhp/Domain/Services/CompilationService.cs` — Shared compilation pipeline service
- `Tyhp/Domain/Services/CompilationOptions.cs` — Options for the compilation service
- `Tyhp/Domain/Services/CompilationProgress.cs` — Progress reporting data class
- Modified `Tyhp/CLI/DebugAction.cs` — Refactored to delegate parsing to `CompilationService`

### Implementation Details

**`CompilationOptions.cs`**

Create in namespace `Tyhp.Domain.Services`:
- `int MaxThreads { get; set; }` — max concurrent parsing threads (-1 for unlimited, default: -1)
- `bool EnableAstCache { get; set; }` — use AST cache (default: true)
- `bool ReportAmbiguities { get; set; }` — ANTLR ambiguity detection (default: false)
- `bool EnableProfiling { get; set; }` — parser profiling (default: false)
- `IProgress<CompilationProgress>? Progress { get; set; }` — progress callback (default: null)
- `int GarbageCollectInterval { get; set; }` — GC after N files, 0 = disabled (default: 1000)
- `long PreReadThreshold { get; set; }` — bytes threshold for pre-reading files into memory (default: 1GB)
- `int PreReadMinFiles { get; set; }` — minimum file count to trigger pre-read (default: 1000)

**`CompilationProgress.cs`**

Create in namespace `Tyhp.Domain.Services`:
- `int FilesProcessed { get; init; }`
- `int TotalFiles { get; init; }`
- `int ErrorCount { get; init; }`
- `int WarningCount { get; init; }`
- `string CurrentFile { get; init; }`
- `long MemoryUsage { get; init; }`

**`CompilationService.cs`**

Create in namespace `Tyhp.Domain.Services`:

Main entry point:
- `CompilationResult ParseFiles(IEnumerable<string> filePaths, CompilationOptions options, CancellationToken cancellationToken = default)`

Internal implementation — extract from `DebugAction`:
- Thread-local `TyhpLexer` and `TyhpParser` management (same `ThreadLocal<>` pattern)
- `TyhpAntlrErrorListener` attached to lexer/parser, writing to the `CompilationResult.Diagnostics` bag
- File pre-reading logic (reading all files into memory if below threshold)
- `Parallel.ForEach` loop with `MaxDegreeOfParallelism` from options
- Per-file parsing: determine file type (`.php`/`.tyhp`/`.tyhpdef`), call appropriate parser entry point
- Visitor execution: create `TyhpParserAstVisitor`, pass `DiagnosticBag`, visit parse tree
- AST cache integration: check `AstCacheService.Get()`, store via `AstCacheService.AddOrUpdate()`
- GC interval: trigger `GC.Collect()` every N files per options
- Progress reporting: call `options.Progress?.Report(...)` with `CompilationProgress` data
- Cancellation: check `cancellationToken.IsCancellationRequested` in the loop
- Timing: record `ParseDuration` on the `CompilationResult`
- Return `CompilationResult` with `ParsedFiles` populated and all diagnostics collected

**`DebugAction.cs` Refactoring**

This is a significant refactor. Create a backup of `DebugAction.cs` before modifying.

The goal is to make `DebugAction` a thin wrapper:
1. Discover files (keep `WalkSync` or use `Project.GetProjectSourceFiles()`)
2. Create `CompilationOptions` from debug settings (`MaxThreads`, `reportAmbiguities`, `doThreadProfiling`, etc.)
3. Create a progress callback that updates the `ProgressBar`
4. Call `CompilationService.ParseFiles(files, options, cancelToken)`
5. Display results from `CompilationResult`
6. Keep debug-specific features as post-processing:
   - Hash checking (`CompareOrUpdateHash`)
   - Parse tree dumping (`buildTree`, `dumpCtxTree`)
   - Parser profiling display (`DisplayParserProfile`)

Specifically remove from `DebugAction`:
- `ThreadLocal<TyhpLexer>` and `ThreadLocal<TyhpParser>` management → moved to `CompilationService`
- The `ParseFile()` method body → logic moved to `CompilationService`
- The `CustomErrorListener` (already removed in Phase 4)
- The `DiagnosticBag diagnostics` field (now lives inside `CompilationResult`)
- The parallel parsing loop → delegated to `CompilationService`
- The file pre-reading logic → delegated to `CompilationService`

Keep in `DebugAction`:
- `WalkSync()` for file discovery (debug action uses its own directory path)
- `cancelHandler()` for Ctrl+C
- Hash checking logic
- Tree dumping logic
- Parser profiling display
- `BytesToString()` utility
- Progress bar setup and display

### Acceptance Criteria

- `CompilationService.ParseFiles()` correctly parses a collection of `.php`, `.tyhp`, and `.tyhpdef` files
- The returned `CompilationResult` contains all parsed `SrcFileAst` instances and all diagnostics
- Multi-threading works: multiple files are parsed concurrently when `MaxThreads > 1` or `MaxThreads == -1`
- AST caching works: cached files are retrieved without re-parsing
- Cancellation works: passing a cancelled token stops processing
- Progress reporting works: the callback receives progress updates during parsing
- `DebugAction` still functions identically from the user's perspective (same output, same behavior)
- `DebugAction` is noticeably simpler/shorter after refactoring

### Dependencies

- **Requires:** Phase 1 (DiagnosticBag), Phase 2 (CompilationResult), Phase 4 (TyhpAntlrErrorListener), Phase 5 (visitor with DiagnosticBag)
- **Provides:** Shared parsing service for BuildAction (Phase 7) and LintAction (Phase 7)

---

## Phase 7: Build and Lint Action Skeletons with ActionRunnerBase Enhancement

### Overview

Create the `BuildAction` and `LintAction` CLI endpoint classes, wire them into `TyhpHostedService`, and enhance `ActionRunnerBase` to return compilation results. The build and lint actions will use `CompilationService` for parsing, with placeholder steps for binder, checker, and emitter.

### Deliverables

- Modified `Tyhp/CLI/ActionRunnerBase.cs` — Enhanced return type and result property
- `Tyhp/CLI/BuildAction.cs` — Build action skeleton with full pipeline placeholders
- `Tyhp/CLI/LintAction.cs` — Lint action skeleton (parse + future check, skip emit)
- Modified `Tyhp/CLI/TyhpHostedService.cs` — Wire up BuildAction and LintAction

### Implementation Details

**`ActionRunnerBase.cs` Enhancement**

Change the `Start` method signature to return a `CompilationResult?`:
- `public abstract CompilationResult? Start(CancellationToken cancellationToken);`
- This lets `TyhpHostedService` get the result and set the exit code

Alternatively, to minimize breakage on existing actions that don't produce a `CompilationResult`, add a `Result` property:
- `public CompilationResult? Result { get; protected set; }`
- Keep `Start()` as `void` but set `this.Result` inside the method
- **Recommended approach:** Change the return type. Update `DebugAction`, `GenerateTyhpdefAction`, and `IntegrityCheckAction` to return `null` (they don't produce compilation results).

**`BuildAction.cs`**

Create in namespace `Tyhp.CLI`:
- Extend `ActionRunnerBase`
- Accept `Tyhp.Config.Project` via constructor (for file discovery and configuration)
- Implement `Start(CancellationToken)` with the full pipeline skeleton:

```
1. Log "Starting build..."
2. Discover source files from Project.GetProjectSourceFiles()
3. Create CompilationOptions from project config
4. Parse all files via CompilationService.ParseFiles()
5. Log parse results (file count, error count, parse duration)
6. [PLACEHOLDER_STORY_02] Load tyhpdefs — log "Tyhpdef loading not yet implemented, skipping"
7. [PLACEHOLDER_STORY_02] Run binder — log "Binder not yet implemented, skipping"
8. [PLACEHOLDER_STORY_08] Run checker — log "Checker not yet implemented, skipping"
9. [PLACEHOLDER_STORY_09] Run emitter — log "Emitter not yet implemented, skipping"
10. [PLACEHOLDER_STORY_09] Write output files — log "Output writing not yet implemented, skipping"
11. Display diagnostic summary (errors, warnings, file count)
12. Display timing for each phase
13. Return CompilationResult
```

Each placeholder step should:
- Log that it's skipped (using `Message.Info()`)
- Not produce errors or throw
- Be clearly marked with `// PLACEHOLDER_PHASE_N:` comments (for future phases of this plan) or `// PLACEHOLDER_STORY_N:` comments (for work belonging to other TODO.md stories) for future replacement

The parse step should fully work using `CompilationService`.

**`LintAction.cs`**

Create in namespace `Tyhp.CLI`:
- Extend `ActionRunnerBase`
- Accept `Tyhp.Config.Project` via constructor
- Nearly identical to `BuildAction` but:
  - Skip the emitter and output writing steps (log "Lint mode: emit step skipped")
  - The intent is parse → bind → check → report (no emit)
  - For now, just parse and report

**`TyhpHostedService.cs` Updates**

Wire the new actions into the switch statement:

In `case Tyhp.Config.Action.build:`:
- Instantiate `BuildAction` with `this.project`
- Assign to `this._actionRunner`
- Capture the `CompilationResult?` returned by `Start(this._actionCancelTokenSource.Token)` in a local (e.g. `buildResult`)
- Set `Environment.ExitCode` from that returned result: `if (buildResult != null) Environment.ExitCode = (int)buildResult.GetExitCode();`

In `case Tyhp.Config.Action.lint:`:
- Same pattern but with `LintAction`

Update the existing action cases to work with the new `ActionRunnerBase` return type if it was changed.

### Acceptance Criteria

- `tyhp build` command runs without crashing (using appropriate CLI args or a `tyhp.json` in the test directory)
- `tyhp lint` command runs without crashing
- Build action discovers source files from project configuration
- Build action parses all discovered files using `CompilationService`
- Build action displays diagnostic summary (file count, error count, warning count, parse duration)
- Build action reports placeholder messages for unimplemented pipeline steps
- Build action sets `Environment.ExitCode` correctly based on parse results
- Lint action behaves like build but skips emit placeholders
- `DebugAction`, `GenerateTyhpdefAction`, `IntegrityCheckAction` still work (no regressions)
- All new and modified files compile without errors

### Dependencies

- **Requires:** Phase 6 (CompilationService)
- **Provides:** Functional `tyhp build` and `tyhp lint` CLI commands; pipeline skeleton with placeholders for Story 02+ of the main TODO

---

## Phase 8: Localization Resource Files

### Overview

Create the `.resx` resource files that back the localization system. The `IStringLocalizer` infrastructure is already wired in `Program.cs` (with `ResourcesPath = "Resources"`), but no actual resource files exist. This phase creates the English resource file with entries for all existing `MessageCode` values and verifies the localization pipeline works end-to-end.

### Deliverables

- `Resources/CLI.TyhpHostedService.en-US.resx` — English resource file with all message entries (or the appropriate naming convention for the localizer)
- Potential modification to `tyhp.csproj` — Ensure `.resx` files are embedded resources
- Verification that `Message.LocalizeErrorCode(1001)` returns the expected string instead of the raw key

### Implementation Details

**Resource File Naming**

The .NET `ResourceManagerStringLocalizer` expects resource files named according to the type they localize. Since the localizer is `IStringLocalizer<TyhpHostedService>`, the resource file should follow the pattern:
- Base: `Resources/CLI.TyhpHostedService.resx` (default/fallback)
- English: `Resources/CLI.TyhpHostedService.en-US.resx`

The `ResourcesPath` is set to `"Resources"` in `Program.cs`, and the root namespace is set to `"Tyhp"` via the assembly attribute. So the resource file path relative to the project root should be `Resources/CLI.TyhpHostedService.resx`.

However, the actual resource file naming may need experimentation. The localizer resolves based on the fully qualified type name minus the root namespace, with dots replaced by the path separator. For `Tyhp.CLI.TyhpHostedService` with root namespace `Tyhp`, the expected resource name is `CLI.TyhpHostedService`.

**Resource File Content**

Create entries for all existing and new `MessageCode` values:

Error codes (ERROR_TYHP prefix):
- `ERROR_TYHP1001` = `"Unknown parser error: {0}"`
- `ERROR_TYHP1002` = `"Unexpected token '{0}' at position {1}"`
- `ERROR_TYHP1003` = `"Compilation aborted"`
- `ERROR_TYHP2001` = `"Unknown visitor error"`
- `ERROR_TYHP2002` = `"Unexpected grammar alternative '{1}' in rule '{0}'"`
- `ERROR_TYHP2003` = `"Required AST node missing in rule '{0}'"`
- `ERROR_TYHP2004` = `"Language construct '{0}' is not yet supported"`
- `ERROR_TYHP3001` = `"Unknown binder error"`
- `ERROR_TYHP3002` = `"Duplicate declaration of symbol '{0}'"`
- `ERROR_TYHP3003` = `"Symbol '{0}' not found"`
- `ERROR_TYHP3004` = `"Symbol type '{0}' is not valid for parent scope '{1}'"`
- `ERROR_TYHP4001` = `"Unknown checker error"`
- `ERROR_TYHP4002` = `"Multiple visibility modifiers specified"`
- `ERROR_TYHP4003` = `"Member modifier '{0}' is not allowed here"`
- `ERROR_TYHP4004` = `"Accessor visibility cannot be more visible than its property"`
- `ERROR_TYHP4005` = `"Conflicting member modifiers: '{0}' and '{1}'"`
- `ERROR_TYHP4006` = `"Invalid property accessor type '{0}'"`
- `ERROR_TYHP4007` = `"Parameter not allowed on property accessor of type '{0}'"`
- `ERROR_TYHP5001` = `"Unknown emitter error"`

General localized strings:
- `error` = `"error"`
- `warning` = `"warning"`
- `info` = `"info"`

**`.csproj` Verification**

Check if `.resx` files in `Resources/` are automatically picked up as embedded resources. The commented-out `LocalizationSourceFiles` section in `tyhp.csproj` suggests this was previously attempted. The default .NET SDK behavior should include `.resx` files automatically, but verify:
- If not auto-included, add an `<EmbeddedResource>` item group entry
- Ensure the `.resx` file compiles to a `.resources` file that the `ResourceManagerStringLocalizer` can find

**End-to-End Verification**

After creating the resource file:
1. Build the project
2. Verify that `Message.LocalizeErrorCode(1001)` returns `"Unknown parser error: {0}"` (not the raw key `"ERROR_TYHP1001"`)
3. Verify that `Message.TyhpError("test.tyhp", 1, 0, 1001)` outputs the localized message

**Fallback Strategy**

If `.resx` does not work with the current `IStringLocalizer` setup (the commented-out `My.Extensions.Localization.Json` package reference in `.csproj` suggests JSON resources were considered), consider:
1. Using the built-in `ResourceManagerStringLocalizer` with `.resx` (preferred)
2. Using a JSON-based localizer if `.resx` integration proves problematic
3. As a last resort, implementing a simple dictionary-based fallback in `Message.LocalizeStringFormat()`

### Acceptance Criteria

- `.resx` resource file exists at the correct path with entries for all `MessageCode` values
- Project compiles with the `.resx` file included
- `Message.LocalizeErrorCode(1001)` returns the human-readable message (not the raw key)
- `Message.LocalizeErrorCode(2002, "PhpIdentifiers", "SomeContext")` returns `"Unexpected grammar alternative 'SomeContext' in rule 'PhpIdentifiers'"`
- All `Message.TyhpError(...)`, `Message.TyhpWarn(...)`, `Message.TyhpInfo(...)` calls produce properly localized output
- The diagnostic system's `Display()` method outputs correctly localized messages

### Dependencies

- **Requires:** Phase 2 (new MessageCode values)
- **Provides:** Functional localization for all diagnostic messages; foundation for future locale translations

---

## Phase 9: Diagnostic Output Formatting Strategy

### Overview

Finalize the diagnostic output formatting system by verifying the console formatter works end-to-end with the localization system, and ensuring the architecture supports future JSON and SARIF formatters. Also validate the existing `MessageCode` numbering scheme and add documentation.

### Deliverables

- Modified `Tyhp/Domain/Exceptions/MessageCode.cs` — Final documentation of numbering scheme with reserved ranges
- `Tyhp/Domain/Diagnostics/JsonDiagnosticFormatter.cs` — Skeleton JSON formatter (for future CI/LSP use)
- Verification that the full pipeline works: parse error → `DiagnosticBag` → `CompilationResult` → `DisplayAll()` → localized console output

### Implementation Details

**`MessageCode.cs` Final Documentation**

Add a comprehensive comment block at the top of the `MessageCode` enum documenting:
- The numbering scheme with all ranges
- Instructions for adding new codes ("When adding items, also add the string to the Resources")
- Reserved ranges for future use (6000s, 7000s, 8000s, 9000s)

**`JsonDiagnosticFormatter.cs`**

Create a skeleton implementation of `IDiagnosticFormatter`:
- `Format(IDiagnostic diagnostic)` — serialize a single diagnostic to JSON format and write to a `TextWriter` or `StringBuilder`
- `FormatSummary(DiagnosticBag bag)` — serialize summary counts
- JSON shape should follow a structure compatible with future LSP `PublishDiagnosticsParams`:

```json
{
  "severity": "error",
  "code": "TYHP1001",
  "file": "test.tyhp",
  "range": { "start": { "line": 1, "column": 0 }, "end": { "line": 1, "column": 5 } },
  "message": "Unknown parser error"
}
```

This is a skeleton — full JSON output will be fleshed out when the lint `--format json` flag is implemented (Story 12 of main TODO). The skeleton just proves the formatter strategy pattern works.

**End-to-End Validation**

Create a manual verification path:
1. Run `tyhp build` on a project with intentional syntax errors
2. Verify diagnostics are collected in `CompilationResult`
3. Verify `DisplayAll()` outputs properly formatted, localized messages to the console
4. Verify error count, warning count, and exit code are correct

### Acceptance Criteria

- `MessageCode.cs` has comprehensive numbering scheme documentation
- `JsonDiagnosticFormatter` compiles and can serialize a diagnostic to JSON
- The `IDiagnosticFormatter` strategy pattern works: swapping `ConsoleDiagnosticFormatter` for `JsonDiagnosticFormatter` changes the output format
- Full pipeline verification: a `.tyhp` file with syntax errors produces localized diagnostic output through the entire chain (parser → listener → `DiagnosticBag` → `CompilationResult` → `DisplayAll()`)
- No regressions in existing functionality

### Dependencies

- **Requires:** All previous phases (1-8)
- **Provides:** Complete, production-ready diagnostic system; verified end-to-end pipeline; foundation for all Story 02+ work in the main TODO

---

## Cross-Cutting Concerns

### Error Recovery Strategy

The following conventions should be established in Story 01 and followed by all future phases:

1. **Parser errors (ANTLR):** Handled by `TyhpAntlrErrorListener`, added to `DiagnosticBag`, parsing continues via ANTLR's built-in recovery
2. **Visitor errors:** Handled by adding to `DiagnosticBag` and returning `ErrorAst` nodes; visitor continues processing remaining nodes
3. **Binder errors (future):** Should detect `ErrorAst` nodes and skip them; report binding errors to `DiagnosticBag`
4. **Checker errors (future):** Should detect `ErrorAst` nodes and skip them; report type errors to `DiagnosticBag`
5. **Fatal/unrecoverable errors:** Continue using `TyhpError`/`TyhpWarning`/`TyhpInfo` exception classes, but catch them and convert to diagnostics via `ToDiagnostic()`

### Placeholder Convention

There are two placeholder formats used in this codebase:

**Within an implementation plan** — for future phases of the same plan:
```csharp
// PLACEHOLDER_PHASE_N: description of what goes here
```
When starting Phase N of this plan, search for `PLACEHOLDER_PHASE_N` and implement each placeholder.

**Cross-story references** — for work that belongs to a different TODO.md story:
```csharp
// PLACEHOLDER_STORY_N: description of what goes here
```
When starting Story N from `TODO.md`, search for `PLACEHOLDER_STORY_N` across all implementation plans and implement each placeholder.

### File Organization Summary

New files created in this implementation:

```
Tyhp/Domain/Diagnostics/
├── DiagnosticSeverity.cs       (~20 lines)
├── IDiagnostic.cs              (~30 lines)
├── Diagnostic.cs               (~120 lines)
├── DiagnosticBag.cs            (~150 lines)
├── DiagnosticExtensions.cs     (~50 lines)
├── CompilationResult.cs        (~60 lines)
├── IDiagnosticFormatter.cs     (~15 lines)
├── ConsoleDiagnosticFormatter.cs (~60 lines)
└── JsonDiagnosticFormatter.cs  (~80 lines)

Tyhp/TyhpLang/Parser/
└── TyhpAntlrErrorListener.cs   (~80 lines)

Tyhp/TyhpLang/Ast/
└── ErrorAst.cs                 (~50 lines)

Tyhp/Domain/Services/
├── CompilationService.cs       (~300 lines)
├── CompilationOptions.cs       (~30 lines)
└── CompilationProgress.cs      (~20 lines)

Tyhp/CLI/
├── BuildAction.cs              (~150 lines)
└── LintAction.cs               (~100 lines)

Resources/
└── CLI.TyhpHostedService.resx  (~100 lines XML)
```

Modified files:
```
Tyhp/Domain/Exceptions/MessageCode.cs
Tyhp/Domain/Exceptions/TyhpError.cs
Tyhp/Domain/Exceptions/TyhpWarning.cs
Tyhp/Domain/Exceptions/TyhpInfo.cs
Tyhp/CLI/ActionRunnerBase.cs
Tyhp/CLI/DebugAction.cs
Tyhp/CLI/TyhpHostedService.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.cs
Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpDereferenceables.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpObjects.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpIdentifiers.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpTopStatements.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpStatements.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpTypes.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpBlocks.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpExpressions.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpFunctions.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpRoot.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpTryCatchBlocks.cs
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpParametersAndArguments.cs
Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.Tyhpdef.cs
Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpObjects.cs
Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpGenerics.cs
Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpRoot.cs
Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpFunctions.cs
Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpReturnTypes.cs
Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpIdentifiers.cs
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [x] **Golden fixtures:** *(Diagnostics covered by Story 07/14 conformance & `Category=Diagnostics` tests; no Story-01-specific emit fixtures required.)* Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [x] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [x] **Conformance run green:** *(Category=Diagnostics green as of Story 14 wrap-up.)* The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [x] **Runtime self-host conformance (runtime-affecting stories only):** *N/A — Story 01 is not runtime-affecting.* Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [x] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
