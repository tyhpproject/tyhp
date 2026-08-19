# Implementation Plan: Story 12 — Lint Action

> **Roadmap position:** Story 12 — **Tier 1 — Usable**
> **Direct dependencies (new numbering):** 01, 02, 05, 06, 08, 10
> **Renumbered from:** legacy Story 7
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** `TODO.md` Story 12
> **Branch:** TBD
> **Generated:** 2026-02-16
> **Prerequisites:** Story 01 (Foundation — diagnostic system, CompilationService, BuildAction skeleton), Story 02 (Binder — symbols, scopes, name resolution, tyhpdef loading), Story 05 (BoundSymbol on AST nodes), Story 06 (TyhpSpec — built-in type definitions), Story 08 (Checker — type checking and validation), Story 10 (Build Action — full pipeline wired together, configuration expansion)
> **Status:** SUBSTANTIALLY COMPLETE (2026-07-31 audit) — lint action, JSON/SARIF formatters, and fix *infrastructure stubs* landed. Real auto-fix Apply() bodies and cross-file `--file` mode remain incomplete; see `INCOMPLETE.md`.

---

## Architecture Overview

### What the Lint Action Does

The lint action is a developer-facing CLI command (`tyhp lint`) that runs the compilation pipeline through parse, bind, and check — but **skips the emitter and output writing**. It is conceptually "build without emit." Its sole purpose is to report all errors, warnings, and informational diagnostics found in the project's Tyhp source code, then exit with an appropriate status code.

Beyond simply omitting the emitter, the lint action adds several lint-specific features:

1. **Multiple output formats** — text (human-readable, default), JSON (machine-readable for IDE/CI tooling), and SARIF (Static Analysis Results Interchange Format for GitHub/CI integration)
2. **Single-file linting** — the `--file` flag allows linting a single file instead of the entire project, for faster feedback during development
3. **Auto-fix capability** (future enhancement) — the `--fix` flag triggers auto-fixable issue resolution such as adding missing type annotations, adding/removing/sorting `use` imports

### Relationship to the Build Action

The lint action shares the vast majority of its pipeline with `BuildAction` (from Story 10). The key differences are:

| Aspect | Build Action | Lint Action |
|---

> **Status:** SUBSTANTIALLY COMPLETE (2026-07-31 audit) — lint action, JSON/SARIF formatters, and fix *infrastructure stubs* landed. Real auto-fix Apply() bodies and cross-file `--file` mode remain incomplete; see `INCOMPLETE.md`.

-----|-------------|-------------|
| Parse | Yes | Yes |
| Bind | Yes | Yes |
| Check | Yes | Yes |
| Emit | Yes | **No** |
| Write output files | Yes | **No** |
| Generate sourcemaps | Yes (optional) | **No** |
| Update composer.json | Yes (optional) | **No** |
| Output format flag | No | Yes (`--format`) |
| Single file flag | No | Yes (`--file`) |
| Auto-fix flag | No | Yes (`--fix`, future) |

Both actions use `CompilationService` (from Story 01) for parsing and share the same binder and checker infrastructure. The implementation should maximize code reuse with `BuildAction` while keeping `LintAction` focused on its diagnostic-reporting purpose.

### Pipeline Position

```
Parser/AST (DONE)
    │
    ▼
Story 01: Foundation (DiagnosticBag, CompilationService, BuildAction skeleton)
    │
    ▼
Story 02: Binder (Symbols, Scopes, Name Resolution, Tyhpdef Loading)
    │
    ▼
Story 06: TyhpSpec (Built-in Tyhp Types)
    │
    ▼
Story 08: Checker (Type Checking, Validation)
    │
    ▼
Story 10: Build Action (Full Pipeline, Config Expansion)
    │
    ▼
┌─────────────────────────────────────────────────────────┐
│  STORY 12: Lint Action  ◄── THIS PLAN                    │
│                                                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │ LintAction.cs                                   │    │
│  │  • Parse → Bind → Check (skip emit)             │    │
│  │  • Report diagnostics                           │    │
│  │  • Exit code based on severity                  │    │
│  └─────────────────────────┬───────────────────────┘    │
│                            │                            │
│  ┌─────────────────────────▼───────────────────────┐    │
│  │ Lint-Specific Features                          │    │
│  │  • --format text|json|sarif                     │    │
│  │  • --file <path> (single-file lint)             │    │
│  │  • --fix (auto-fix, future)                     │    │
│  │  • Diagnostic formatter strategy                │    │
│  └─────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
```

### Key Design Decisions

1. **Shared pipeline, separate action class:** `LintAction` is its own `ActionRunnerBase` subclass — not a mode flag on `BuildAction`. This keeps responsibilities clean and allows lint-specific features (format flags, fix mode) without cluttering the build action.

2. **Formatter strategy pattern:** The `--format` flag selects a diagnostic output formatter. Story 01's implementation plan already defines `IDiagnosticFormatter`, `ConsoleDiagnosticFormatter`, and a skeleton `JsonDiagnosticFormatter`. The lint action completes the JSON formatter and adds a SARIF formatter.

3. **Single-file mode:** When `--file` is specified, only that file is parsed/bound/checked. However, the binder still needs the `GlobalScope` with tyhpdef symbols loaded, so the pipeline still loads tyhpdefs and built-ins. Only the user-code parsing and checking is scoped to the single file.

4. **Auto-fix as a future phase:** The `--fix` flag is stubbed in this plan but marked as future enhancement (per TODO.md: "future enhancement"). The fix infrastructure (code actions, AST rewriting, file writing) is designed but left as placeholders.

5. **Configuration:** Lint-specific configuration options (`--format`, `--file`, `--fix`) are parsed from CLI arguments via the existing `IConfiguration` infrastructure. New configuration keys are added to `Project.cs`.

### Existing Infrastructure to Leverage

| Component | Location | Used For |
|-----------|----------|----------|
| `ActionRunnerBase` | `Tyhp/CLI/ActionRunnerBase.cs` | Base class for LintAction |
| `TyhpHostedService` | `Tyhp/CLI/TyhpHostedService.cs` | Action dispatch (has `case Action.lint:` placeholder) |
| `CompilationService` | Story 01: `Tyhp/Domain/Services/CompilationService.cs` | Shared parsing pipeline |
| `CompilationResult` | Story 01: `Tyhp/Domain/Diagnostics/CompilationResult.cs` | Aggregated pipeline results |
| `DiagnosticBag` | Story 01: `Tyhp/Domain/Diagnostics/DiagnosticBag.cs` | Diagnostic collection |
| `IDiagnosticFormatter` | Story 01: `Tyhp/Domain/Diagnostics/IDiagnosticFormatter.cs` | Formatter strategy interface |
| `ConsoleDiagnosticFormatter` | Story 01: `Tyhp/Domain/Diagnostics/ConsoleDiagnosticFormatter.cs` | Default text output |
| `JsonDiagnosticFormatter` | Story 01: `Tyhp/Domain/Diagnostics/JsonDiagnosticFormatter.cs` | Skeleton JSON output |
| `TyhpBinder` | Story 02: `Tyhp/TyhpLang/Binder/TyhpBinder.cs` | Binding walk |
| `TyhpChecker` | Story 08: `Tyhp/TyhpLang/Checker/TyhpChecker.cs` | Type checking |
| `Project` | `Tyhp/Config/Project.cs` | Configuration and file discovery |
| `Message` | `Tyhp/CLI/Message.cs` | Console output utilities |
| `ExitCode` | `Tyhp/Domain/Enums/ExitCode.cs` | Exit status codes |
| `DisplayHelp` | `Tyhp/Config/DisplayHelp.cs` | Help text (has `LintHelp()` stub) |
| `Action` enum | `Tyhp/Config/Action.cs` | Already has `lint` entry |

### MessageCode Numbering

Lint-specific diagnostic codes use the **7000s range** (reserved for CLI errors per Story 01's numbering scheme). However, the lint action primarily reports diagnostics from the parser (1000s), visitor (2000s), binder (3000s), and checker (4000s). Lint-specific codes are only needed for lint infrastructure errors and auto-fix messages:

| Code | Name | Description |
|------|------|-------------|
| 7200 | `LintFileNotFound` | The `--file` target does not exist |
| 7201 | `LintPathNotFound` | An explicit positional lint path does not exist |
| 7202 | `LintInvalidPath` | An explicit positional lint path is invalid |
| 7203 | `LintAccessDenied` | Access was denied during lint |
| 7204 | `LintIoError` | An I/O error occurred during lint |
| 7205 | `LintUnexpectedError` | An unexpected error occurred during lint |
| 7206 | `LintNoSourceFiles` | No source files were found to lint |
| 7207 | `LintCancelled` | Lint was cancelled |
| 7208 | `LintFileNotInProject` | The `--file` target is not within project include paths |
| 7209 | `LintFixApplied` | An auto-fix was applied (info severity) |
| 7210 | `LintFixFailed` | An auto-fix could not be applied (warning severity) |
| 7211 | `LintUnsupportedFormat` | The `--format` value is not recognized |

> **Note:** Codes 7201–7207 predate Story 12 (path discovery / I/O). Story 12 Phase 1 adds 7200 and
> 7208–7211 for `--file` / `--format` / `--fix`. `MessageCode.cs` is authoritative — see `CONVENTIONS.md`.

The 7000–7999 range is subdivided per CLI action (7200–7299 for lint). See Story 10 MessageCode numbering guide for the full subdivision.

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup: `<filename>.bak.<timestamp>` (e.g. `<filename>.bak.20260216_143000`)
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: Lint Configuration and CLI Argument Parsing




### Phase Overview

Add lint-specific configuration options to the `Project` class and wire them through the CLI argument parsing infrastructure. This enables the `--format`, `--file`, and `--fix` flags to be read from CLI arguments and/or the `tyhp.json` configuration file.

### Deliverables

- Modified `Tyhp/Config/Project.cs` — new lint-specific configuration properties
- Modified `Tyhp/Domain/Exceptions/MessageCode.cs` — new lint-specific message codes
- Modified `Tyhp/Domain/Enums/ExitCode.cs` — reuse existing `CompileError` exit code for lint errors (no new exit code needed)

### Implementation Details

**1.1 — Add Lint Configuration Properties to `Project.cs`**

File: `Tyhp/Config/Project.cs`

Add the following properties inside the `#region configuration items` section:

- `LintFormat` (`string`) — output format for lint: `"text"` (default), `"json"`, `"sarif"`. Parsed from `--format` CLI argument or `lint.format` in `tyhp.json`.
- `LintFile` (`string?`) — optional single-file path for lint: `null` (default, lint entire project). Parsed from `--file` CLI argument.
- `LintFix` (`bool`) — whether to apply auto-fixes: `false` (default). Parsed from `--fix` CLI argument or `lint.fix` in `tyhp.json`.

Add parsing logic inside `ConfigChanged()`:

```csharp
// Lint config options
var format = this._configuration["format"] ?? this._configuration["lint:format"];
this.LintFormat = String.IsNullOrWhiteSpace(format) ? "text" : format.Trim();
var file = this._configuration["file"];
this.LintFile = String.IsNullOrWhiteSpace(file) ? null : file.Trim();
// CLI --fix wins when present; otherwise lint.fix
if (this._configuration.GetSection("fix").Exists())
    this.LintFix = this._configuration["fix"].ParseBool();
else
    this.LintFix = this._configuration["lint:fix"].ParseBool();
```

Also expand bare `--fix` in `Program.cs` `ExpandBareBooleanFlags` (same treatment as `--clean` / `--verbose`) so value-less `--fix` overlays as `true`.

The CLI argument parsing uses the existing `IConfiguration` infrastructure — command-line arguments like `--format=json` are automatically available via `this._configuration["format"]` through the Microsoft.Extensions.Configuration command-line provider (already wired in `Program.cs`).

**1.2 — Add Lint-Specific MessageCodes**

File: `Tyhp/Domain/Exceptions/MessageCode.cs`

Add to the existing `#region CLI — lint action (7200–7299)` (alongside pre-existing 7201–7207 path/I/O codes):

- `LintFileNotFound = 7200`
- `LintFileNotInProject = 7208`
- `LintFixApplied = 7209`
- `LintFixFailed = 7210`
- `LintUnsupportedFormat = 7211`

**1.3 — Validate Lint Configuration**

Create a helper method `ValidateLintConfig()` on `Project` (or as a static method) that checks:

- `LintFormat` is one of `"text"`, `"json"`, `"sarif"` — if not, report a diagnostic with `MessageCode.LintUnsupportedFormat`
- `LintFile`, if specified, exists on disk — if not, report `MessageCode.LintFileNotFound`
- `LintFile`, if specified, is within the project's include paths (or at minimum, is a `.tyhp`/`.php`/`.tyhpdef` file) — if not, report `MessageCode.LintFileNotInProject`

This validation runs at the start of `LintAction.Start()` before beginning the compilation pipeline.

**1.4 — Add Resource Strings for New MessageCodes**

File: `Tyhp/Resources/CLI.TyhpHostedService.resx` (or the localization resource file established in Story 01)

Add entries:
- `ERROR_TYHP7200` = `"File not found: '{0}'"`
- `ERROR_TYHP7208` = `"File '{0}' is not within the project source paths"`
- `INFO_TYHP7209` = `"Auto-fix applied: {0}"`
- `WARNING_TYHP7210` = `"Auto-fix '{0}' could not be applied: {1}"` (fix description, failure reason — refined in Phase 6 so two failing fixes on one diagnostic are distinguishable)
- `ERROR_TYHP7211` = `"Unsupported output format '{0}'. Valid formats: text, json, sarif"`

### Acceptance Criteria

- [x] `Project.LintFormat` defaults to `"text"` and can be set via `--format` CLI argument
- [x] `Project.LintFile` defaults to `null` and can be set via `--file` CLI argument
- [x] `Project.LintFix` defaults to `false` and can be set via `--fix` CLI argument
- [x] New `MessageCode` values 7200 / 7208–7211 exist in the enum with the `#region CLI — lint action` section
- [x] Validation logic exists for lint configuration (format value, file existence)
- [x] Resource strings are added for all new message codes
- [x] All existing functionality continues to work (no regressions)
- [x] The project compiles without errors

### Dependencies

- Story 01 must be complete (`DiagnosticBag`, `MessageCode` enum, resource files infrastructure)
- The `IConfiguration` CLI argument parsing must be functional (it is — `Program.cs` wires `AddCommandLine()`)

---

## Phase 2: Core LintAction Implementation




### Phase Overview

Create the `LintAction` class that runs the compilation pipeline through parse, bind, and check — skipping emit — and reports diagnostics. This is the core of the lint feature. It uses `CompilationService` for parsing, `TyhpBinder` for binding, and `TyhpChecker` for type checking, then formats and outputs all collected diagnostics.

### Deliverables

- `Tyhp/CLI/LintAction.cs` — the core lint action class
- Modified `Tyhp/CLI/TyhpHostedService.cs` — wire `LintAction` into the `case Action.lint:` dispatch
- Modified `Tyhp/CLI/ActionRunnerBase.cs` — add `Result` property if not already added by Story 01

### Implementation Details

**2.1 — Create `LintAction.cs`**

File: `Tyhp/CLI/LintAction.cs`

Create the class in namespace `Tyhp.CLI`:

- Extend `ActionRunnerBase`
- Constructor accepts `Tyhp.Config.Project` (for configuration and file discovery) and `IDiagnosticFormatter` (for output formatting, selected based on `--format` flag)
- Store `CancellationToken` and `IsCancelled` flag (same pattern as `DebugAction` and `IntegrityCheckAction`)

The `Start(CancellationToken)` method implements this pipeline:

```
Step 1:  Validate lint configuration (format, file, fix flags)
Step 2:  Discover source files
           - If --file is specified: lint only that file
           - Otherwise: use Project.GetProjectSourceFiles()
Step 3:  Parse all files via CompilationService.ParseFiles()
Step 4:  Record parse duration on CompilationResult
Step 5:  If parse has fatal errors, skip bind/check
Step 6:  Binding (handled by CompilationService)
           - CompilationService.ParseFiles() handles both parsing and binding (creating `GlobalScope`, running `TyhpBinder.Bind()`)
           - LintAction then invokes the checker on the bound result
           - LintAction does NOT run the optimizer or emitter — it stops after checking
           - BoundSymbol is set on all AST nodes (Story 05)
           - CompilationResult.GlobalScope and BindDuration are populated automatically
Step 7:  (Binding duration already recorded by CompilationService)
Step 8:  Run checker on bound ASTs
           - Call TyhpChecker.Check(result.ParsedFiles, result.GlobalScope)
           - The checker reads BoundSymbol from AST nodes (Story 05) for symbol lookups
Step 9:  Record check duration on CompilationResult
Step 10: Display all diagnostics using the selected formatter
Step 11: Display summary (file count, error count, warning count, durations)
Step 12: Set exit code based on CompilationResult
Step 13: Log "Lint mode: emit step skipped" (info-level, for clarity)
```

Key differences from `BuildAction`:
- Steps for emitting, writing output files, generating sourcemaps, and updating composer.json are **omitted entirely** (not placeholder-skipped, just absent)
- The formatter is configurable (text/json/sarif) based on `Project.LintFormat`
- Single-file mode (Step 2) changes file discovery

**2.2 — Implement Single-File Mode**

When `Project.LintFile` is not null:

- Resolve the file path to an absolute path
- Validate it exists (report `MessageCode.LintFileNotFound` if not)
- Validate it has a supported extension (`.tyhp`, `.php`, `.tyhpdef`)
- Pass only this single file to `CompilationService.ParseFiles()`
- The binder still loads tyhpdefs and built-ins into `GlobalScope` (so type references in the single file can be resolved)
- The binder binds the single file into the `GlobalScope` (creating its `FileScope`, `NamespaceScope`, etc.)
- The checker checks only the single file's AST
- This provides fast feedback for a single file without the overhead of parsing/binding/checking the entire project

Note: Single-file mode has a limitation — cross-file references (e.g., a class in this file references a class defined in another file) will produce unresolved-reference diagnostics unless the binder also loads the rest of the project's symbols. There are two approaches:

- **Simple approach (implement first):** Lint only the single file. Cross-file references show as unresolved. This is useful for syntax checking and type-annotation checking within a single file.
- **Full approach (future enhancement):** Parse all project files but only check the target file. This resolves cross-file references but is slower. Mark with `// PLACEHOLDER_STORY_12: Full single-file mode with cross-file resolution`.

**2.3 — Implement Diagnostic Output with Selected Formatter**

After all pipeline phases complete, output diagnostics using the formatter:

```csharp
var formatter = LintAction.CreateFormatter(project.LintFormat);
result.Diagnostics.DisplayAll(formatter);
formatter.FormatSummary(result.Diagnostics);
```

**`CreateFormatter(string format)` — private static method:**
Factory method that creates the appropriate `IDiagnosticFormatter` based on the format string:
- `"text"` → `new ConsoleDiagnosticFormatter()`
- `"json"` → `new JsonDiagnosticFormatter()`
- `"sarif"` → `new SarifDiagnosticFormatter()`
- Unknown format → add `MessageCode.LintUnsupportedFormat` diagnostic and fall back to `ConsoleDiagnosticFormatter`

**2.4 — Implement Exit Code Logic**

The exit code is determined by the `CompilationResult`:

- No diagnostics → `ExitCode.Success` (0)
- Errors present → `ExitCode.CompileError` (4)
- Only warnings present → `ExitCode.CompileWarning` (5)

This logic already exists in `CompilationResult.GetExitCode()` from Story 01. Call it and set `Environment.ExitCode`.

**2.5 — Implement Summary Output**

After diagnostics, display a summary line:

For text format:
```
Lint complete: 42 files checked, 3 errors, 7 warnings, 12 info (parse: 1.2s, bind: 0.8s, check: 0.5s)
```

For JSON format, the summary is included in the JSON output structure (see Phase 3).

For SARIF format, the summary is embedded in the SARIF tool run object (see Phase 4).

**2.6 — Wire `LintAction` into `TyhpHostedService`**

File: `Tyhp/CLI/TyhpHostedService.cs`

Replace the current `case Tyhp.Config.Action.lint:` placeholder:

```csharp
case Tyhp.Config.Action.lint:
    var lintFormatter = LintAction.CreateFormatter(this.project.LintFormat);
    var lintAction = new LintAction(this.project, lintFormatter);
    var lintResult = lintAction.Start(this._actionCancelTokenSource.Token);
    Environment.ExitCode = (int)(lintResult?.GetExitCode() 
        ?? Tyhp.Domain.Enums.ExitCode.Success);
    break;
```

Note: The formatter selection may need access to Story 01's `IDiagnosticFormatter` interface. If Story 01 has not yet been implemented, use a temporary inline implementation or import the planned type.

**2.7 — Enhance `ActionRunnerBase` (if not already done by Story 01)**

File: `Tyhp/CLI/ActionRunnerBase.cs`

Story 01's plan specifies adding a `Result` property or changing `Start()` return type. If this has not been done yet, add:

```csharp
public CompilationResult? Result { get; protected set; }
```

This allows `TyhpHostedService` to read the result after `Start()` completes:

```csharp
Environment.ExitCode = (int)(this._actionRunner?.Result?.GetExitCode() ?? Tyhp.Domain.Enums.ExitCode.Success);
```

If `CompilationResult` does not yet exist (Story 01 not complete), add a simpler property:

```csharp
public int? ResultExitCode { get; protected set; }
```

### Acceptance Criteria

- [x] `LintAction.cs` exists and extends `ActionRunnerBase`
- [x] `LintAction.Start()` runs the pipeline: parse → bind → check (no emit)
- [x] `tyhp lint` command executes without crashing (verified via `TyhpHostedService` dispatch)
- [x] Diagnostics from all phases (parse, bind, check) are collected and displayed
- [x] Exit code is set correctly: 0 for success, 4 for errors, 5 for warnings-only
- [x] Single-file mode (`--file`) works: only the specified file is parsed and checked
- [x] Single-file mode validates the file exists and has a supported extension
- [x] The selected formatter (`--format`) is used for output
- [x] A summary line is displayed after diagnostics (file count, error/warning counts, durations)
- [x] `LintAction` is wired into `TyhpHostedService` at the `case Action.lint:` branch
- [x] "Lint mode: emit step skipped" info message is logged
- [x] All existing actions (`debug`, `integrity_check`, `generate_tyhpdef`) still work (no regressions)
- [x] The project compiles without errors

### Dependencies

- Phase 1 (Lint Configuration) must be complete
- Story 01 (Foundation) must be complete: `CompilationService`, `CompilationResult`, `DiagnosticBag`, `IDiagnosticFormatter`, `ConsoleDiagnosticFormatter`
- Story 02 (Binder) must be complete — binding is integrated into CompilationService.ParseFiles()
- Story 05 (BoundSymbol) must be complete — checker reads BoundSymbol from AST nodes
- Story 08 (Checker) must be complete — TyhpChecker.Check() is called directly
- Story 10 (Build Action) should exist as a reference implementation for the pipeline pattern

---

## Phase 3: JSON Diagnostic Formatter




### Phase Overview

Complete the `JsonDiagnosticFormatter` (skeleton created in Story 01) to produce machine-readable JSON output when `--format=json` is specified. This enables CI/CD pipelines, IDEs, and other tools to consume lint results programmatically.

**Note:** The existing `JsonDiagnosticFormatter` from Story 01 is a **skeleton** that emits an NDJSON (newline-delimited JSON lines) format (one JSON object per diagnostic, plus a trailing summary line). This phase **replaces that skeleton** with a single-document JSON format (`version`/`tool`/`diagnostics`/`summary`) that produces one complete JSON object — more widely supported by CI tools, IDEs, and standard JSON parsers. The NDJSON implementation must be **fully replaced**, not kept alongside, and the class-level XML doc-comment on `JsonDiagnosticFormatter` (which currently documents the NDJSON/LSP skeleton) must be rewritten to describe the new single-document schema and the coordinate handling below. (Story 01 deliberately deferred the final JSON design to Story 12, so this is the canonical definition.)

### Deliverables

- Modified `Tyhp/Domain/Diagnostics/JsonDiagnosticFormatter.cs` — full JSON output implementation
- A documented JSON output schema

### Implementation Details

**3.1 — Define the JSON Output Schema**

The JSON output should be an object with two top-level keys: `diagnostics` (array) and `summary` (object):

```json
{
  "version": "1.0",
  "tool": {
    "name": "tyhp",
    "version": "804.4.1"
  },
  "diagnostics": [
    {
      "severity": "error",
      "code": "TYHP4002",
      "message": "Multiple visibility modifiers specified",
      "file": "src/Models/User.tyhp",
      "range": {
        "start": { "line": 41, "column": 8 },
        "end": { "line": 41, "column": 25 }
      }
    }
  ],
  "summary": {
    "filesChecked": 42,
    "errorCount": 3,
    "warningCount": 7,
    "infoCount": 12,
    "durations": {
      "parseMs": 1200,
      "bindMs": 800,
      "checkMs": 500,
      "totalMs": 2500
    }
  }
}
```

> **Coordinate convention (authoritative):** the `range` coordinates above are **0-based line** and **0-based column**. The internal diagnostic contract (`IDiagnosticFormatter`) is **1-based line, 0-based column**; the JSON formatter converts the line to 0-based (`line − 1`) and passes the column through unchanged. In the example, the diagnostic at internal line 42 / column 8 is emitted as `line: 41, column: 8`.

**3.2 — Implement `JsonDiagnosticFormatter`**

File: `Tyhp/Domain/Diagnostics/JsonDiagnosticFormatter.cs`

The formatter should:

- Implement `IDiagnosticFormatter`
- Collect all diagnostics into a list during `Format()` calls (buffer them, do not write one-by-one)
- In `FormatSummary()`, serialize the entire JSON document (diagnostics + summary) and write to `Console.Out` or a provided `TextWriter`
- Use `System.Text.Json.JsonSerializer` for serialization (available in .NET 9 without additional dependencies)
- Support optional pretty-printing (controlled by a constructor parameter or a static flag)

Key implementation points:

- `Format(IDiagnostic diagnostic)` — add the diagnostic to an internal `List<DiagnosticJsonModel>` (a simple POCO for serialization)
- `FormatSummary(DiagnosticBag bag)` — create the complete JSON output and write it as a single document to the output stream
- The severity field should use lowercase strings: `"error"`, `"warning"`, `"info"`, `"hint"`
- The code field should use the `TYHP{code}` format (e.g., `"TYHP4002"`)
- Line numbers are emitted **0-based**: the formatter converts the internal 1-based line to 0-based by subtracting 1 (`line − 1`), matching the authoritative `IDiagnosticFormatter` contract and the existing `JsonDiagnosticFormatter` behavior (this also matches what the Story 19 LSP protocol handler consumes). Clamp to 0 so a line < 1 never produces a negative coordinate.
- Column numbers are emitted **0-based** and passed through unchanged. Internal diagnostics are already 0-based in column (the contract is 1-based line, 0-based column), so the JSON formatter must NOT add 1 to columns. (SARIF — Phase 4 — is the only format that uses 1-based for both line and column.)
- If `EndLine`/`EndColumn` on the diagnostic are null, the `end` property in the range should equal the `start` property

**3.3 — Create JSON Model Classes**

Create internal model classes for JSON serialization (can be nested within `JsonDiagnosticFormatter` or in a separate file):

- `LintJsonOutput` — top-level: `version`, `tool`, `diagnostics[]`, `summary`
- `LintJsonTool` — `name`, `version`
- `LintJsonDiagnostic` — `severity`, `code`, `message`, `file`, `range`
- `LintJsonRange` — `start`, `end`
- `LintJsonPosition` — `line`, `column`
- `LintJsonSummary` — `filesChecked`, `errorCount`, `warningCount`, `infoCount`, `durations`
- `LintJsonDurations` — `parseMs`, `bindMs`, `checkMs`, `totalMs`

Use `System.Text.Json` attributes (`[JsonPropertyName]`) to ensure camelCase property names in the output.

**3.4 — Accept CompilationResult for Summary Data**

The `FormatSummary()` method on `IDiagnosticFormatter` only receives a `DiagnosticBag`. However, the JSON summary needs timing data and file counts from `CompilationResult`. Options:

- **Option A:** Extend `IDiagnosticFormatter.FormatSummary()` to accept an optional `CompilationResult?` parameter
- **Option B:** Add a `SetContext(CompilationResult result)` method to the formatter interface
- **Option C:** Pass the timing/count data to the formatter via constructor or a `SetMetadata()` call

**Decision:** Option B — add a `SetContext(CompilationResult)` method to `IDiagnosticFormatter` (with a default no-op implementation so existing formatters don't break). The `LintAction` calls `formatter.SetContext(result)` before calling `DisplayAll()`.

### Acceptance Criteria

- [x] `JsonDiagnosticFormatter` produces valid JSON output conforming to the documented schema
- [x] Running `tyhp lint --format=json` outputs a single JSON document to stdout
- [x] Each diagnostic in the JSON output includes severity, code, message, file, and range
- [x] The summary section includes file count, error/warning/info counts, and timing durations
- [x] The JSON is valid and parseable by standard JSON parsers
- [x] Empty diagnostic lists produce valid JSON (`"diagnostics": []`)
- [x] The formatter buffers diagnostics and writes the complete document in `FormatSummary()`
- [x] No output is written to stdout before `FormatSummary()` is called (clean JSON output without interleaved text)
- [x] The `ConsoleDiagnosticFormatter` still works unchanged (no regressions from interface changes)

### Dependencies

- Phase 2 (Core LintAction) must be complete — the formatter is used by `LintAction`
- Story 01 must have `IDiagnosticFormatter` interface and `ConsoleDiagnosticFormatter` defined
- `System.Text.Json` must be available (it is — included in .NET 9 SDK)

---

## Phase 4: SARIF Diagnostic Formatter




### Phase Overview

Create a `SarifDiagnosticFormatter` that produces output in the Static Analysis Results Interchange Format (SARIF v2.1.0), enabling integration with GitHub Code Scanning, Azure DevOps, and other CI platforms that consume SARIF.

### Deliverables

- `Tyhp/Domain/Diagnostics/SarifDiagnosticFormatter.cs` — SARIF v2.1.0 output formatter
- A documented SARIF output structure

### Implementation Details

**4.1 — SARIF v2.1.0 Output Structure**

SARIF (Static Analysis Results Interchange Format) is a JSON-based standard defined by OASIS. The output structure for the Tyhp lint tool:

```json
{
  "$schema": "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/main/sarif-2.1/schema/sarif-schema-2.1.0.json",
  "version": "2.1.0",
  "runs": [
    {
      "tool": {
        "driver": {
          "name": "tyhp",
          "version": "804.4.1",
          "informationUri": "https://tyhp.dev",
          "rules": [
            {
              "id": "TYHP4002",
              "shortDescription": { "text": "Multiple visibility modifiers" },
              "defaultConfiguration": { "level": "error" }
            }
          ]
        }
      },
      "results": [
        {
          "ruleId": "TYHP4002",
          "level": "error",
          "message": { "text": "Multiple visibility modifiers specified" },
          "locations": [
            {
              "physicalLocation": {
                "artifactLocation": { "uri": "src/Models/User.tyhp" },
                "region": {
                  "startLine": 42,
                  "startColumn": 9,
                  "endLine": 42,
                  "endColumn": 26
                }
              }
            }
          ]
        }
      ]
    }
  ]
}
```

**4.2 — Implement `SarifDiagnosticFormatter`**

File: `Tyhp/Domain/Diagnostics/SarifDiagnosticFormatter.cs`

The formatter should:

- Implement `IDiagnosticFormatter`
- Collect diagnostics during `Format()` calls (buffer them)
- In `FormatSummary()`, serialize the complete SARIF document and write to stdout
- Map diagnostic severities to SARIF levels:
  - `Error` → `"error"`
  - `Warning` → `"warning"`
  - `Info` → `"note"`
  - `Hint` → `"note"` (SARIF does not have a "hint" level)
- Collect unique rule IDs from all diagnostics and generate the `rules` array in the `tool.driver` section
- Use relative file paths in `artifactLocation.uri` (relative to the project root)
- SARIF uses 1-based line numbers and 1-based column numbers

**4.3 — Create SARIF Model Classes**

Create internal model classes for SARIF serialization:

- `SarifLog` — `$schema`, `version`, `runs[]`
- `SarifRun` — `tool`, `results[]`
- `SarifTool` — `driver`
- `SarifToolDriver` — `name`, `version`, `informationUri`, `rules[]`
- `SarifRule` — `id`, `shortDescription`, `defaultConfiguration`
- `SarifResult` — `ruleId`, `level`, `message`, `locations[]`
- `SarifLocation` — `physicalLocation`
- `SarifPhysicalLocation` — `artifactLocation`, `region`
- `SarifArtifactLocation` — `uri`
- `SarifRegion` — `startLine`, `startColumn`, `endLine`, `endColumn`
- `SarifMessage` — `text`

Use `System.Text.Json` with `[JsonPropertyName]` attributes. The `$schema` property requires `[JsonPropertyName("$schema")]`.

**4.4 — Rule Description Generation**

For the `rules` array, generate a short description for each unique `MessageCode` encountered:

- Use the localized message template (from the resource file) as the `shortDescription`
- Strip format parameters (`{0}`, `{1}`) from the template for the generic description
- Map the diagnostic severity to `defaultConfiguration.level`

### Acceptance Criteria

- [x] `SarifDiagnosticFormatter` produces valid SARIF v2.1.0 JSON output
- [x] Running `tyhp lint --format=sarif` outputs a single SARIF document to stdout
- [x] The SARIF schema reference is included (`$schema` property)
- [x] Each diagnostic maps to a SARIF `result` with correct `ruleId`, `level`, `message`, and `locations`
- [x] The `tool.driver.rules` array contains entries for all unique rule IDs in the results
- [x] Severity mapping is correct: Error→error, Warning→warning, Info→note, Hint→note
- [x] File paths in `artifactLocation.uri` are relative to the project root
- [x] The SARIF output can be consumed by GitHub Code Scanning (validated by uploading to a test repository, or by validating against the SARIF schema)
- [x] Empty diagnostic lists produce valid SARIF (`"results": []`)

### Dependencies

- Phase 2 (Core LintAction) must be complete
- Phase 3 (JSON Formatter) is recommended to be complete first (shared JSON serialization patterns)
- The SARIF v2.1.0 specification must be followed: https://docs.oasis-open.org/sarif/sarif/v2.1.0/sarif-v2.1.0.html

---

## Phase 5: Help Text and Single-File Enhancements




### Phase Overview

Complete the lint help text in `DisplayHelp.cs`, enhance single-file mode with better diagnostic UX, and add progress/summary reporting that adapts to the selected output format.

### Deliverables

- Modified `Tyhp/Config/DisplayHelp.cs` — complete `LintHelp()` implementation
- Modified `Tyhp/CLI/LintAction.cs` — enhanced progress reporting and single-file UX
- Consideration for how lint interacts with `--quiet` mode

### Implementation Details

**5.1 — Complete `LintHelp()` in `DisplayHelp.cs`**

File: `Tyhp/Config/DisplayHelp.cs`

Replace the current `LintHelp()` stub with comprehensive help text:

```
tyhp lint — Check for errors and warnings without producing output files

Usage:
    tyhp lint [options]

Options:
    --format=<format>    Output format: text (default), json, sarif
    --file=<path>        Lint a single file instead of the entire project
    --fix                Apply auto-fixable changes (experimental)
    --quiet              Suppress non-diagnostic output (banner, summary)
    --include=<glob>     Include additional source paths
    --exclude=<glob>     Exclude source paths

Description:
    Runs the Tyhp compilation pipeline (parse, bind, check) on your project
    without producing output files. Reports all errors, warnings, and
    informational diagnostics found in the source code.

    Exit codes:
        0    No errors or warnings found
        1    Cancelled (e.g., Ctrl+C) — GenericError
        4    One or more errors found
        5    Warnings found (no errors)

Output Formats:
    text     Human-readable output to the console (default)
    json     Machine-readable JSON output to stdout
    sarif    SARIF v2.1.0 output for CI integration (GitHub, Azure DevOps)

Examples:
    tyhp lint                         Lint the entire project
    tyhp lint --format=json           Lint with JSON output
    tyhp lint --file=src/User.tyhp    Lint a single file
    tyhp lint --format=sarif > out.sarif   Generate SARIF report
```

Use `Message.Info()` and `Message.Display()` for formatting, following the pattern in `GeneralHelp()`.

**5.2 — Suppress Non-Diagnostic Output in Machine-Readable Formats**

When `--format=json` or `--format=sarif` is specified, the lint action should:

- **Suppress** the CLI banner (`Message.Banner()`) — this would corrupt the JSON/SARIF output
- **Suppress** progress messages and info-level log messages (e.g., "Lint mode: emit step skipped")
- **Suppress** the text summary line (it's embedded in the JSON/SARIF output)
- Only write the formatted diagnostic output to stdout

Implementation: check `Project.LintFormat` at the start of `LintAction.Start()`. If the format is not `"text"`, set a flag `suppressConsoleOutput = true` and guard all `Message.*` calls with this flag. The formatter's `FormatSummary()` call handles the final output.

For `--quiet` mode (existing `Project.BeQuiet` flag):
- In text format: suppress the banner and info messages, but still show errors and warnings
- In JSON/SARIF format: same as non-quiet (machine-readable formats already suppress non-diagnostic output)

**5.3 — Enhance Single-File Mode Diagnostics**

When `--file` is used, adjust the summary output:

- Text format: `"Lint complete: 1 file checked (src/User.tyhp), 2 errors, 0 warnings"`
- JSON format: the file path appears in the `summary` section
- If the file has no diagnostics, display a success message: `"No issues found in src/User.tyhp"`

**5.4 — Handle Edge Cases**

- **No source files found:** If `Project.GetProjectSourceFiles()` returns empty and no `--file` is specified, report an info-level message: "No source files found. Check your include/exclude paths in tyhp.json." Exit with `ExitCode.Success` (no errors = no lint issues).
- **All files cached (no re-parsing needed):** If all files are served from AST cache, the parse duration may be near zero. Display timing accurately.
- **Cancellation:** If the user presses Ctrl+C during linting, display partial results (whatever diagnostics have been collected so far) and exit with `ExitCode.GenericError` (value `1` — already defined in `ExitCode.cs`).
- **Very large projects:** Progress reporting for JSON/SARIF formats should be via stderr (not stdout) to avoid corrupting the machine-readable output. Use `Console.Error.WriteLine()` for progress in non-text formats.

### Acceptance Criteria

- [x] `tyhp help --subject=lint` displays comprehensive help text with all flags, examples, and exit code documentation
- [x] `tyhp lint --format=json` produces clean JSON output on stdout with no interleaved text
- [x] `tyhp lint --format=sarif` produces clean SARIF output on stdout with no interleaved text
- [x] Progress messages in non-text formats go to stderr (not stdout)
- [x] `--quiet` mode suppresses the banner and info messages in text format
- [x] Single-file mode displays the file path in the summary
- [x] Empty project (no source files) produces an informational message and exits with success
- [x] Ctrl+C during linting displays partial results and exits cleanly
- [x] Help text matches actual available flags and behavior

### Dependencies

- Phase 2 (Core LintAction) must be complete
- Phase 3 (JSON Formatter) must be complete
- Phase 4 (SARIF Formatter) must be complete

---

## Phase 6: Auto-Fix Infrastructure (Stub)




### Phase Overview

Create the infrastructure for the `--fix` flag, which will apply auto-fixable changes to source files. This phase creates the framework and stubs for fix actions but does NOT implement actual fix logic — that depends on the checker (Story 08) identifying fixable issues and producing fix suggestions. The actual fix implementations are deferred to when the checker supports them.

### Deliverables

- `Tyhp/TyhpLang/Lint/ILintFix.cs` — interface for a lint fix action
- `Tyhp/TyhpLang/Lint/LintFixResult.cs` — result of applying a fix
- `Tyhp/TyhpLang/Lint/LintFixEngine.cs` — engine that applies fixes to source files
- Modified `Tyhp/CLI/LintAction.cs` — integrate fix engine when `--fix` is specified

### Implementation Details

**6.1 — Define the `ILintFix` Interface**

File: `Tyhp/TyhpLang/Lint/ILintFix.cs`

An auto-fix is a code transformation that resolves a specific diagnostic:

- `MessageCode TargetCode { get; }` — the diagnostic code this fix addresses
- `string Description { get; }` — human-readable description of the fix (e.g., "Add missing type annotation")
- `LintFixResult Apply(string sourceText, IDiagnostic diagnostic)` — apply the fix to the source text and return the result

**6.2 — Define `LintFixResult`**

File: `Tyhp/TyhpLang/Lint/LintFixResult.cs`

- `bool Success { get; }` — whether the fix was applied
- `string? ModifiedSourceText { get; }` — the modified source text (null if fix failed)
- `string? FailureReason { get; }` — why the fix failed (null if successful)
- `TextEdit[] Edits { get; }` — the individual text edits applied (for reporting)

Where `TextEdit` is a simple record: `(int StartLine, int StartColumn, int EndLine, int EndColumn, string NewText)`.

**6.3 — Create `LintFixEngine`**

File: `Tyhp/TyhpLang/Lint/LintFixEngine.cs`

The engine orchestrates fix application:

- `LintFixEngine(IEnumerable<ILintFix> registeredFixes)` — constructor taking registered fix implementations
- `IReadOnlyList<LintFixApplication> ApplyFixes(CompilationResult result)` — apply all applicable fixes
- For each diagnostic in the result, check if a registered fix targets that `MessageCode`
- If so, read the source file, apply the fix, and write the modified source back
- Collect results for reporting

**6.4 — Register Placeholder Fix Implementations**

Create placeholder/stub fix classes for the fix types mentioned in TODO.md Story 12a:

- `Tyhp/TyhpLang/Lint/Fixes/AddMissingTypeAnnotationFix.cs` — `// PLACEHOLDER_STORY_08: Implement when checker identifies inferable types`
- `Tyhp/TyhpLang/Lint/Fixes/AddMissingImportFix.cs` — `// PLACEHOLDER_STORY_02: Implement when binder reports missing imports with suggestions`
- `Tyhp/TyhpLang/Lint/Fixes/RemoveUnusedImportFix.cs` — `// PLACEHOLDER_STORY_02: Implement when binder identifies unused imports`
- `Tyhp/TyhpLang/Lint/Fixes/SortImportsFix.cs` — `// PLACEHOLDER_STORY_02: Implement when binder provides import ordering`

Each stub class implements `ILintFix` but its `Apply()` method returns `LintFixResult { Success = false, FailureReason = "Not yet implemented" }`.

**6.5 — Integrate Fix Engine into `LintAction`**

File: `Tyhp/CLI/LintAction.cs`

After the check phase completes (Step 9), if `Project.LintFix` is true:

```
Step 9.5: Run fix engine
             - Instantiate LintFixEngine with registered fixes
             - Call ApplyFixes(result)
             - For each successful fix: report MessageCode.LintFixApplied (info)
             - For each failed fix: report MessageCode.LintFixFailed (warning)
             - If any fixes were applied: re-run parse/bind/check on the modified files
             - **Loop detection:** Track a set of `(file, messageCode, line, column)` tuples for all applied fixes. Before applying a fix, check if a previously-fixed issue at that location was reintroduced by a different fix. If detected, stop with an error: "Auto-fix loop detected: fix for {code} at {location} was undone by a subsequent fix"
             - **Max depth:** Configurable maximum re-run iterations, defaulting to 10. After reaching max depth, stop with a warning listing remaining unfixed issues. Configuration key: `checker:maxFixIterations` in `CheckerConfig` (CLI: `--max-fix-iterations=N`). See Story 10 Phase 1 `CheckerConfig` for the property definition.
             - Display fix summary: "Applied N fixes, M failed, K iterations"
```

The re-run after fixing is important — applying one fix may resolve multiple diagnostics or introduce new ones.

**6.6 — Safety Measures for Auto-Fix**

- **Never modify files without `--fix` flag explicitly set**
- Before modifying any source file, create a backup: `<file>.bak.<timestamp>`
- Log every file modification with the exact changes made
- If fix application fails midway, report what was and was not applied
- Consider a `--fix-dry-run` flag (future) that shows what would be changed without actually modifying files

### Acceptance Criteria

- [x] `ILintFix` interface exists with `TargetCode`, `Description`, and `Apply()` method
- [x] `LintFixResult` exists with success/failure tracking and edit details
- [x] `LintFixEngine` exists and can iterate over diagnostics looking for applicable fixes
- [x] At least 4 placeholder fix classes exist (add type annotation, add import, remove unused import, sort imports)
- [x] All placeholder fixes return `Success = false` with "Not yet implemented" message
- [x] `LintAction` integrates the fix engine when `--fix` is specified
- [x] When `--fix` is specified but no fixes are available, display "No auto-fixes available for the current diagnostics"
- [x] Fix application creates backup files before modifying source files
- [x] Fix results are reported using `MessageCode.LintFixApplied` / `MessageCode.LintFixFailed`
- [x] The fix infrastructure does not affect normal lint operation when `--fix` is not specified
- [x] All placeholder comments use `// PLACEHOLDER_STORY_N:` format referencing the appropriate story

### Dependencies

- Phase 2 (Core LintAction) must be complete
- Story 01 diagnostic infrastructure must be available for reporting fix results
- Story 02 (Binder) is needed for actual import-related fixes (placeholder for now)
- Story 08 (Checker) is needed for actual type-annotation fixes (placeholder for now)

---

## Cross-Cutting Concerns

### File Organization Summary

New files created in this implementation:

```
Tyhp/CLI/
└── LintAction.cs                          (~250 lines)

Tyhp/Domain/Diagnostics/
├── JsonDiagnosticFormatter.cs             (~200 lines, modified from Story 01 skeleton)
└── SarifDiagnosticFormatter.cs            (~250 lines)

Tyhp/TyhpLang/Lint/
├── ILintFix.cs                            (~20 lines)
├── LintFixResult.cs                       (~30 lines)
├── LintFixEngine.cs                       (~100 lines)
└── Fixes/
    ├── AddMissingTypeAnnotationFix.cs     (~30 lines, stub)
    ├── AddMissingImportFix.cs             (~30 lines, stub)
    ├── RemoveUnusedImportFix.cs           (~30 lines, stub)
    └── SortImportsFix.cs                  (~30 lines, stub)
```

Modified files:

```
Tyhp/Config/Project.cs                    (add LintFormat, LintFile, LintFix properties)
Tyhp/Config/DisplayHelp.cs                (complete LintHelp() method)
Tyhp/CLI/TyhpHostedService.cs             (wire LintAction into lint case)
Tyhp/CLI/ActionRunnerBase.cs              (add Result property if not already done)
Tyhp/Domain/Exceptions/MessageCode.cs     (add 7200 / 7208–7211 lint codes)
Tyhp/Domain/Enums/ExitCode.cs             (potentially add LintError, or reuse CompileError)
Tyhp/Domain/Diagnostics/IDiagnosticFormatter.cs  (add SetContext method)
Tyhp/Resources/CLI.TyhpHostedService.resx (add lint message strings)
```

### Placeholder Convention

**Cross-story placeholders** (`// PLACEHOLDER_STORY_N:` — for work belonging to other TODO.md stories):

- `// PLACEHOLDER_STORY_02: Implement when binder reports missing imports with suggestions` — in import-related fix stubs (binder exists but may need import suggestion enhancement)
- `// PLACEHOLDER_STORY_08: Implement when checker identifies inferable types` — in type annotation fix stub
- `// PLACEHOLDER_STORY_12: Full single-file mode with cross-file resolution` — in single-file implementation

**Within-plan placeholders** (`// PLACEHOLDER_PHASE_N:` — for future phases of this plan):

- `// PLACEHOLDER_PHASE_3: Use JsonDiagnosticFormatter` — ~~in Phase 2~~ **done in Phase 3** (single-document JSON schema)
- `// PLACEHOLDER_PHASE_4: Use SarifDiagnosticFormatter` — ~~in Phase 2~~ **done in Phase 4** (SARIF v2.1.0 single-document output)
- `// PLACEHOLDER_PHASE_6: Run fix engine when --fix is specified` — ~~in Phase 2~~ **done in Phase 6** (LintFixEngine + placeholder fixes)

### Testing Strategy

Until the test infrastructure (Story 07 of TODO.md) is in place, validation should use:

1. **Manual CLI testing:** Run `tyhp lint` on the example files in `Examples/` and verify output
2. **Format validation:** Run `tyhp lint --format=json` and pipe output through `jq` or `python -m json.tool` to validate JSON
3. **SARIF validation:** Validate SARIF output against the SARIF schema using a SARIF validator tool
4. **Single-file testing:** Run `tyhp lint --file=Examples/TypeGuards.tyhp` and verify only that file is checked
5. **Exit code testing:** Verify exit codes with `echo $?` after running lint on files with known errors
6. **Regression testing:** Ensure `tyhp debug`, `tyhp integrity_check`, and `tyhp build` still work after changes

### Error Handling Conventions

All new code follows the Story 01 diagnostic system:

- Use `DiagnosticBag.AddError()` / `AddWarning()` / `AddInfo()` for all compiler messages
- Never throw exceptions for recoverable errors
- Use appropriate `MessageCode` values in the 7000 range for lint-specific errors
- Log unexpected exceptions at error level and add them as internal-error diagnostics

### Integration with Future Stories

The lint action designed in this plan integrates forward with:

- **Story 11 (Emitter Feature Expansion):** No direct impact — lint does not emit
- **Story 07 (Testing Infrastructure):** Lint tests should cover all format outputs and edge cases
- **Story 19 (Language Server):** The LSP `textDocument/publishDiagnostics` can reuse the same diagnostic pipeline. The `JsonDiagnosticFormatter` output is structurally similar to LSP diagnostic notifications, enabling code sharing
- **Story 13 (CLI Polish):** The `LintHelp()` text created here is the foundation for the help system completion

---

*Generated: 2026-02-16 | Source: TODO.md Story 12 | Branch: Not Specified*

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the lint action implementation works end-to-end. Steps can be skipped, reordered, or adapted as needed. All commands assume you are in the project root directory and the project has been built successfully (`dotnet build`).

### Step 1: Verify `tyhp lint` Runs Without Crashing

Run the lint command against the project (or any directory with `.tyhp` files):

```bash
dotnet run -- lint
```

Expected behavior:
- The pipeline executes parse → bind → check (no emit step)
- Diagnostics (if any) are displayed to the console
- A summary line is printed: `Lint complete: N files checked, X errors, Y warnings, Z info (parse: ...s, bind: ...s, check: ...s)`
- An info-level message says `"Lint mode: emit step skipped"`
- The process exits with code `0` (no errors), `4` (errors found), or `5` (warnings only)

Verify the exit code:

```bash
dotnet run -- lint; echo "Exit code: $?"
```

### Step 2: Verify Text Format Output (Default)

Create a test file with a known error. For example, create `test_lint_error.tyhp`:

```tyhp
<?tyhp

namespace TestLint;

class Foo {
    public function bar(): string {
        return 42;  // type mismatch: returning int where string expected
    }
}
```

Run lint:

```bash
dotnet run -- lint --file=test_lint_error.tyhp
```

Expected output (text format):
- A diagnostic referencing `test_lint_error.tyhp` with the line number of `return 42`
- The error code should be in the checker range (4000s), e.g., `TYHP4009` (incompatible return type)
- The summary line shows `1 file checked, 1 error`
- Exit code is `4`

### Step 3: Verify JSON Format Output

Run the same lint with JSON output:

```bash
dotnet run -- lint --format=json --file=test_lint_error.tyhp
```

Expected output:
- A single JSON document printed to stdout (no interleaved text, no banner)
- The JSON should parse cleanly:

```bash
dotnet run -- lint --format=json --file=test_lint_error.tyhp | python3 -m json.tool
```

Verify the JSON structure has:
- `"version": "1.0"`
- `"tool"` object with `"name": "tyhp"`
- `"diagnostics"` array with at least one entry containing `severity`, `code`, `message`, `file`, `range`
- `"summary"` object with `filesChecked`, `errorCount`, `warningCount`, `infoCount`, `durations`
- Line numbers are 0-based (internal 1-based line minus 1)
- Column numbers are 0-based (passed through unchanged)

### Step 4: Verify SARIF Format Output

```bash
dotnet run -- lint --format=sarif --file=test_lint_error.tyhp
```

Expected output:
- A single SARIF v2.1.0 JSON document on stdout
- Verify structure:

```bash
dotnet run -- lint --format=sarif --file=test_lint_error.tyhp | python3 -m json.tool
```

Check for:
- `"$schema"` pointing to the SARIF schema URL
- `"version": "2.1.0"`
- `"runs"` array with one run entry
- `"tool.driver.name": "tyhp"`
- `"tool.driver.rules"` array with rule entries for encountered diagnostics
- `"results"` array with at least one result containing `ruleId`, `level`, `message`, `locations`
- `level` values should be `"error"`, `"warning"`, or `"note"` (not `"info"` or `"hint"`)

### Step 5: Verify Single-File Mode

Create a clean file with no errors:

```tyhp
<?tyhp

namespace TestClean;

class CleanClass {
    public int $value = 0;

    public function getValue(): int {
        return $this->value;
    }
}
```

Save as `test_lint_clean.tyhp` and run:

```bash
dotnet run -- lint --file=test_lint_clean.tyhp
```

Expected:
- Summary says `1 file checked` with `0 errors, 0 warnings`
- Exit code is `0`

Now test with a non-existent file:

```bash
dotnet run -- lint --file=nonexistent.tyhp
```

Expected:
- A diagnostic with code `TYHP7200` (`LintFileNotFound`)
- Exit code is non-zero

### Step 6: Verify Unsupported Format Handling

```bash
dotnet run -- lint --format=xml
```

Expected:
- A diagnostic with code `TYHP7211` (`LintUnsupportedFormat`)
- Falls back to text format output
- The error message should mention valid formats: `text, json, sarif`

### Step 7: Verify `--fix` Stub Behavior

```bash
dotnet run -- lint --fix
```

Expected:
- Lint runs normally
- A message indicates that no auto-fixes are available (all fix stubs return "Not yet implemented")
- If there are fixable diagnostics, each stub should report `LintFixFailed` (code 7210) with "Not yet implemented"
- If no fixable diagnostics exist, a message says "No auto-fixes available for the current diagnostics"

### Step 8: Verify Help Text

```bash
dotnet run -- help --subject=lint
```

Expected:
- Comprehensive help text showing:
  - Usage: `tyhp lint [options]`
  - All flags: `--format`, `--file`, `--fix`, `--quiet`
  - Exit code documentation (0, 4, 5)
  - Output format descriptions (text, json, sarif)
  - Examples

### Step 9: Verify Quiet Mode

```bash
dotnet run -- lint --quiet
```

Expected:
- No banner is displayed
- Info-level messages are suppressed
- Errors and warnings are still shown
- Summary may be suppressed (depending on implementation)

### Step 10: Verify No Regressions in Other Actions

Run existing actions to ensure they still work:

```bash
dotnet run -- debug
dotnet run -- integrity_check
dotnet run -- build
```

Each should behave as before with no changes in behavior.

### Step 11: Verify Clean JSON/SARIF Output Has No Interleaved Text

For machine-readable formats, verify that stdout contains ONLY the JSON/SARIF document:

```bash
dotnet run -- lint --format=json 2>/dev/null | head -c 1
```

Expected: The first character should be `{` (the start of the JSON object). If it's anything else (like a banner message), the output is corrupted.

```bash
dotnet run -- lint --format=sarif 2>/dev/null | head -c 1
```

Same expectation: first character is `{`.

### Step 12: Cleanup

Remove any test files created during verification:

```bash
rm -f test_lint_error.tyhp test_lint_clean.tyhp
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [x] **Golden fixtures:** *Lint diagnostics covered via Story 14 fixtures + formatter unit tests; no dedicated story12 emit suite.* Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [x] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [x] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [x] **Runtime self-host conformance (runtime-affecting stories only):** *N/A.* Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [x] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
