# TyhpLang Lint — Technical Guide

Auto-fix machinery under `Tyhp.TyhpLang.Lint` for `tyhp lint --fix`. This folder is **not** the lint CLI itself (`Tyhp/CLI/LintAction.cs`) and **not** the checker rules that produce diagnostics — it only applies registered text transforms keyed by `MessageCode`.

Grounded in the eight files under `Lint/` + `Lint/Fixes/`, CLI wiring in `LintAction`, config in `Project` / `CheckerConfig`, and `tests/Tyhp.Tests/Lint/LintFixEngineTests.cs`.

## Pipeline fit

```text
tyhp lint
  └─ LintAction
       ├─ CompilationService.ParseFiles (parse → bind → check)
       ├─ if Project.LintFix:   // --fix or lint.fix
       │    └─ ApplyAutoFixes
       │         ├─ LintFixEngine.CreateDefault()
       │         ├─ loop iteration = 1 .. MaxFixIterations
       │         │    ├─ engine.ApplyFixes(result, previouslyApplied)
       │         │    ├─ on LoopDetected → error + break
       │         │    ├─ on no applications → break
       │         │    ├─ record successes into previouslyApplied
       │         │    ├─ if no success this pass → break  (stubs always hit this)
       │         │    └─ else re-run RunPipeline (parse/bind/check)
       │         └─ merge LintFixApplied / LintFixFailed / loop diagnostics
       ├─ skip emit
       └─ display diagnostics / exit code
```

**Config:**
- Enable: `Project.LintFix` — CLI `--fix` wins when present (including `--fix=false`); else `lint.fix` in `tyhp.json` (default false).
- Iterations: `CheckerConfig.MaxFixIterations` (default `10`) — CLI `--max-fix-iterations` wins over `checker.maxFixIterations`. `ApplyAutoFixes` uses `Math.Max(1, MaxFixIterations)`.

Lint never emits PHP; fixes mutate **source** `.tyhp` / project files on disk, then recompile.

---

## Types and contracts

### `ILintFix`

| Member | Role |
| --- | --- |
| `TargetCode` | Exactly one `MessageCode` this fix handles |
| `Description` | Human-readable label (also surfaced in `LintFixApplied` / `LintFixFailed` diagnostics) |
| `Apply(sourceText, diagnostic)` | Pure-ish transform: returns `LintFixResult` — **must not** write files itself |

Engine owns I/O (read/write/backup). Fixes only rewrite strings.

### `LintFixResult` + `TextEdit`

- `Succeeded(modifiedSourceText, edits?)` / `Failed(reason)` factories.
- `TextEdit`: `StartLine`/`EndLine` **1-based**, `StartColumn`/`EndColumn` **0-based**, matching the diagnostic coordinate contract (documented on `TextEdit`).
- Today’s stub fixes never populate `Edits`; the engine does not apply edits itself — it uses `ModifiedSourceText` as the full file replacement when writing.

### `LintFixApplication` / `LintFixPassResult` / `LintFixLocationKey`

- **`LintFixApplication`:** one diagnostic + fix + result + optional `BackupPath` (set only when a write happened).
- **`LintFixLocationKey`:** `(FileName, Code, Line, Column)` — identity for loop detection across iterations.
- **`LintFixPassResult`:** list of applications for this pass; `LoopDetected` + `LoopLocation` when a previously fixed location reappears.

---

## `LintFixEngine` — end-to-end behavior

### Construction

- `new LintFixEngine(IEnumerable<ILintFix>)` builds a `Dictionary<MessageCode, ILintFix>`. **Duplicate `TargetCode` throws `ArgumentException`** (message includes both descriptions).
- `CreateDefault()` registers the four stubs in this order: type annotation, missing import, unused import, sort imports.

### One instance per lint `--fix` run

XML remarks: one engine spans **all fix iterations** so each modified file is backed up **exactly once**, preserving the pre-fix original. **Not thread-safe.**

### `ApplyFixes(CompilationResult, previouslyApplied?)`

Walks `result.Diagnostics.All` in bag order:

1. Skip diagnostics with no registered fix.
2. Build `LintFixLocationKey`. If it is in `previouslyApplied`, **return immediately** with `LoopDetected = true` and applications collected so far (may be empty if the first matching diagnostic loops).
3. Missing/blank `FileName` → record `Failed("Diagnostic has no file path")`, continue.
4. Read source via per-pass `sourceCache` (`StringComparer.Ordinal` — case-sensitive paths are distinct files).
5. Call `fix.Apply(sourceText, diagnostic)`.
6. Write only when **all** of:
   - `Success`
   - non-empty `ModifiedSourceText`
   - text **differs** from current cached source (`Ordinal`)
7. On write: `EnsureBackup` then `File.WriteAllText`; update cache so later diagnostics in the **same pass** see prior edits.
8. I/O failures (`IOException`, `UnauthorizedAccessException`, `NotSupportedException`) become `Failed(ex.Message)` and clear backup path for that application.

Diagnostics without a registered code produce **no** application entry (silent skip).

### Backups

- Public `CreateBackup(path)` → `{file}.bak.{yyyyMMdd_HHmmss}`; if that path exists, append `_2`, `_3`, … (one-second timestamp resolution).
- Private `EnsureBackup`: first successful write per file path (engine lifetime) creates the backup; later writes reuse the same path string so the backup remains the **original** content.

Failed stubs do **not** create backups (verified by tests).

---

## Registered fixes (`Lint/Fixes/`)

All four are **stubs**: `Apply` returns `Failed("Not yet implemented")`. Comments reference Story 02 / Story 08 placeholders.

| Class | `TargetCode` | Intended behavior (from docs/comments) | Notes |
| --- | --- | --- | --- |
| `AddMissingTypeAnnotationFix` | `CheckerVariableTypeRequired` | Insert inferred type annotation | Emitted today by `TypeAnnotationRule` / some declaration member checks when annotations are required |
| `AddMissingImportFix` | `BinderSymbolNotFound` | Insert a suggested `use` | **Very broad** code — not import-specific yet; comment says wait for binder suggestions |
| `RemoveUnusedImportFix` | `CheckerUnusedImport` | Delete unused `use` | Produced by `ImportRule` (checker), despite stub comment mentioning binder |
| `SortImportsFix` | `CheckerDuplicateImport` | Canonicalize import order | **Provisional** target: remarks admit there is no dedicated ordering diagnostic; `CheckerDuplicateImport` is import-related and unused by the other stubs |

Until stubs become real, `ApplyAutoFixes` typically: one pass → applications with failures → `anySucceeded == false` → break. Users still see `LintFixFailed` warnings when `--fix` matches those codes.

---

## CLI orchestration details (`LintAction.ApplyAutoFixes`)

Worth knowing when changing the engine (lives outside this folder but defines the contract):

- Successes add `LintFixLocationKey` to `previouslyApplied` and emit **info** `MessageCode.LintFixApplied` with the fix description.
- Failures emit **warning** `MessageCode.LintFixFailed` with description + failure reason.
- Loop → progress error + `MessageCode.LintUnexpectedError` with arg `"AutoFixLoop"`.
- After a successful write, pipeline re-runs so the next iteration sees updated sources and fresh diagnostics.
- Max iterations: warn `CLI_LintFixMaxIterationsReached` and stop without requiring a loop.
- Fix-engine diagnostics are `AddRange`’d onto the **latest** `CompilationResult` for display/exit code.

---

## Conventions for new fixes

1. Implement `ILintFix` in `Lint/Fixes/`, one class per `MessageCode`.
2. Register in `CreateDefault()` (or inject via constructor in tests).
3. Keep `Apply` side-effect free: return full `ModifiedSourceText` (and optional `Edits` for future LSP/UI).
4. Prefer stable `Description` strings (they appear in diagnostics; localization of those strings is owned by CLI/`Message` resources for the wrapper diagnostics, not by the fix class today).
5. Do not register two fixes for the same code.
6. Assume same-pass sequential application with a shared source cache — make fixes composable or order-independent when possible.
7. Coordinate system for any `TextEdit`s: line 1-based, column 0-based.

---

## Weirdness / WHY

| Behavior | Why (from code/comments/tests) |
| --- | --- |
| Engine not thread-safe; one instance per run | Backup map must span iterations without re-backing up mid-edit files |
| Ordinal path comparer in source cache | Case-sensitive FS: `Foo.tyhp` and `foo.tyhp` must not share text |
| Success with identical text skips write | Avoids useless backups/touches when a fix is a no-op |
| Loop key includes `MessageCode` + line/col | Same file can have multiple fixable issues; reappearance of the **same** diagnostic location means a fix fight |
| Loop aborts mid-pass | Fail fast; do not keep applying after detecting oscillation |
| SortImports → `CheckerDuplicateImport` | Placeholder until a dedicated ordering code exists |
| Stubs still registered | Story 12 Phase 6: wire `--fix` end-to-end before real transforms |
| `TextEdit` unused by engine write path | Data contract for future tooling; engine currently replaces whole file text |
| Fixes don’t use diagnostic suggestions yet | Story 14 defined suggestion spans for later `--fix` / LSP; engine today ignores them |

---

## Tests

`tests/Tyhp.Tests/Lint/LintFixEngineTests.cs` covers:

- Default registration of four codes
- Stub failure message
- Empty pass when no matching diagnostics
- Stub does not write or backup
- Loop detection via `previouslyApplied`
- Duplicate `TargetCode` constructor throw
- Successful multi-fix same file: chained cache + single original backup
- `CreateBackup` collision suffix

---

## Open Questions / Needs Clarification

1. Will real fixes consume `IDiagnostic` suggestion/replacement payloads (Story 14 contract), or only re-derive edits from source + message args?
2. Should `AddMissingImportFix` stay on `BinderSymbolNotFound`, or move to a dedicated “missing import with suggestion” code once the binder emits one?
3. What is the eventual `MessageCode` for import sorting, and should `SortImportsFix` unregister from `CheckerDuplicateImport` before that ships?
4. Should `Edits` drive the write path (apply patches) instead of requiring full `ModifiedSourceText`, especially for LSP reuse?
5. Is whole-file rewrite + re-parse the long-term model, or will incremental AST patching appear?
6. Backup files (`.bak.*`) — are they cleaned up by any command, or left for the user indefinitely?
