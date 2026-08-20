# Implementation Plan: Story 10 — Build Action (Wire Everything Together)

> **Roadmap position:** Story 10 — **Tier 0 — Spine**
> **Direct dependencies (new numbering):** 01, 02, 04, 05, 06, 08, 09 (the optimizer — Stories 23–24 — is wired as an optional pass when present)
> **Renumbered from:** legacy Story 6
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** `TODO.md` Story 10
> **Branch:** TBD
> **Generated:** 2026-02-16
> **Prerequisites:** The Tier 0 spine stories must be complete — Stories 01, 02, 03, 04, 05, 06, 08, and 09 (diagnostic system, binder with BoundSymbol (Story 05), built-in types/TyhpSpec, checker, basic emitter, and the Tyhp runtime packages (Story 04)). The optimizer (Stories 23–24, Tier 3) is **not** a prerequisite: Story 10 wires an optional optimize pass that stays a no-op until those stories land. **[Judgment call — optimizer was moved to Tier 3; see ROADMAP.]**
> **Status:** SUBSTANTIALLY COMPLETE (2026-07-31 audit) — full `BuildAction` pipeline, config, incremental build, output writer, and composer integration landed. Residual gaps (e.g. `MaxErrorsPerFile` config wiring) in `INCOMPLETE.md`. `--watch` is intentionally stubbed pending Story 19.

---

## Architecture Overview

### What Story 10 Accomplishes

Story 10 is the integration story. Stories 01–09 each built a piece of the compilation pipeline in isolation — diagnostics, binder, built-in types/TyhpSpec, checker, basic emitter, and runtime packages. Story 10 wires them all together into a functional `tyhp build` command that a developer can run to compile their Tyhp project into PHP output. It also expands the configuration system to support all the build-related options needed to control the pipeline, including build profiles and optimization settings.

### Current State (After All Prior Stories)

By the time Story 10 begins, the following infrastructure exists from earlier spine stories (Stories 01 through 09):

| Component | Source Story | Current State After Completion |
|-----------|-------------|-------------------------------|
| `DiagnosticBag`, `IDiagnostic`, `Diagnostic` | Story 01 | Fully functional diagnostic collection |
| `CompilationResult` | Story 01 | Aggregates all phase outputs and timing |
| `CompilationService` | Story 01 | Handles multi-threaded parsing, returns `CompilationResult` |
| `BuildAction` (skeleton) | Story 01 | Skeleton with `PLACEHOLDER_STORY_*` markers for bind/check/emit |
| `LintAction` (skeleton) | Story 01 | Skeleton, parse-only |
| `TyhpAntlrErrorListener` | Story 01 | Shared ANTLR error listener writing to `DiagnosticBag` |
| `ActionRunnerBase` | Story 01 | Enhanced with `CompilationResult?` return or `Result` property |
| `TyhpBinder` | Story 02 | Full binding walk (declaration + resolution passes), `GlobalScope` output |
| `SymbolTree` / `NameResolver` | Story 02 | Name resolution methods (simple, qualified, member, type) |
| Tyhpdef Loading | Story 02 | `Tyhpdef.Get()` discovers and parses tyhpdef files |
| TyhpSpec files | Story 06 | Complete `tyhpTypes.tyhpdef`, `tyhpDisposable.tyhp`, async types, etc. |
| `TyhpChecker` | Story 08 | Full type checking and validation walk |
| `CheckerState` / `VariableState` | Story 08 | Control-flow-aware type narrowing and variable tracking |
| `TyhpEmitter` | Story 09 | AST-to-PHP emission with `EmitItem` trees |
| `PHPOutputFile` | Story 09 | File splitting, alias conversion, import pruning, code generation |
| Tyhp runtime Composer packages | Story 04 | Runtime packages (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`) under `runtime/packages/`; written in Tyhp, compiled to PHP |

### What Story 10 Adds

1. **Full pipeline wiring in `BuildAction`** — Replace all `PLACEHOLDER_STORY_*` markers with actual calls to the binder, checker, emitter, and output writer. Handle the complete lifecycle from file discovery to PHP file output.

2. **Expanded configuration parsing** — Parse 25+ new configuration options from `tyhp.json` and CLI arguments that control output paths, PHP version targeting, checker strictness, emitter behavior, and more.

3. **Shared parsing logic refinement** — If `CompilationService` from Story 01 needs enhancement to support the full build pipeline (e.g., binding, checking as part of the service), refine it. Otherwise, validate that the existing extraction works correctly with all pipeline phases.

4. **Output file writing** — Write compiled PHP files to disk, create directories, handle path conflicts, optionally generate/update `composer.json`.

5. **Build flags** — `--clean`, `--verbose`, `--dry-run`, `--strict`, `--watch` (placeholder for future).

6. **Incremental compilation** — Leverage the existing AST cache to only recompile changed files.

### Pipeline Flow Diagram

```
tyhp build
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│  BuildAction.Start(CancellationToken)                       │
│                                                             │
│  1. Load & validate configuration (Project)                 │
│  2. Discover source files (Project.GetProjectSourceFiles()) │
│  3. Discover tyhpdef files (config globs + defaults)        │
│  4. Optionally clean output directory (--clean)             │
│  5. Parse all source files (CompilationService.ParseFiles)  │
│  6. Parse all tyhpdef files (CompilationService.ParseFiles) │
│  7. Create GlobalScope + populate built-ins                 │
│  8. Load tyhpdef symbols into GlobalScope                   │
│  9. Run binder on parsed ASTs (TyhpBinder.Bind)             │
│  10. Run checker on bound ASTs (TyhpChecker.Check)          │
│  10.5. If library: error on any entrypoint (ignore generateTyhpdef)   │
│  11. If errors → report + exit CompileError                 │
│  12. If warnings + --strict → exit CompileWarning           │
│  12.5. Tyhpdef Track C: _tyhpdef/, support/, package.tyhp.json (if generateTyhpdef) │
│  12.6. Run optimizer (TyhpOptimizer, Story 23)             │
│  13. Run emitter (TyhpEmitter.Emit)                         │
│  14. Split output files (PHPOutputFile.FromAstTree)         │
│  15. Convert aliases (PHPOutputFile.ConvertAliases)          │
│  16. Prune imports (PHPOutputFile.PruneFileImports)          │
│  17. Generate PHP code (PHPOutputFile.Generate)              │
│  18. Write PHP files to disk (--dry-run skips): if sourcemaps, append │
│      //# sourceMappingURL=... to each file's PHP content string first, │
│      then write the .php file; write companion .map files after         │
│  19. Optionally update composer.json                        │
│  20. Add Composer dependencies for Tyhp runtime packages    │
│  21. Display summary (files, errors, warnings, timings)     │
│  22. Return CompilationResult                               │
└─────────────────────────────────────────────────────────────┘
```

**Note on optimizer integration (Story 23) and tyhpdef (Story 20):** The optimizer runs between the error gate and the emitter (step 12.6). Step 12.5 (Tyhpdef Track C: `{buildOutputDir}/package.tyhpdef` and, for libraries, `package.tyhp.json` with `include: ["./package.tyhpdef"]`) occurs **before** optimization to preserve the stable public API contract. The optimizer receives the fully bound, checked AST and performs transformations such as extension inlining, constant folding, and dead code elimination based on the `build.optimize` level, `build.profile`, and `build.optimizations` configuration. See Story 23 for the full optimizer pipeline and configuration details.

> **Note:** The pipeline flow diagram uses conceptual step numbers (1-22) for illustration. The implementation in Phase 2 uses sequential code steps (1-10 with sub-steps). These are not meant to correspond 1:1 — the implementation steps represent the actual method code flow, while the diagram shows the logical phase relationships.

### Key Design Decisions

1. **`CompilationService` scope:** Story 01 created `CompilationService` for parsing only. Story 10 needs to decide whether to extend `CompilationService` to orchestrate the full pipeline (parse → bind → check → emit) or keep it as a parsing service and have `BuildAction` orchestrate the pipeline. **Decision:** `CompilationService` handles parsing and binding (creating `GlobalScope`, running `TyhpBinder.Bind()`). `BuildAction` orchestrates the remaining pipeline stages: checker, optimizer, emitter, and output writer. This separation keeps the shared parse+bind infrastructure reusable for both `BuildAction` and `LintAction`.

2. **Configuration loading timing:** Configuration must be fully parsed before the pipeline starts, since options like `output.phpVersion` affect tyhpdef loading and `checker.*` options affect checker behavior. Parse configuration in `BuildAction.Start()` before calling any pipeline phase.

3. **Output path computation:** PHP output file paths are determined by namespace + class name + `output.path` + `output.namespacePrefix` + PSR-4 aliases. This computation happens in `PHPOutputFile.FromAstTree()` (Story 09) but requires configuration access. Pass `Project` config to the emitter.

4. **Incremental compilation strategy:** Use the existing `AstCacheService` SHA256 invalidation. When a file's hash matches the cache, skip parsing. For binding/checking, a full re-bind is simpler initially (incremental binding is complex). Mark incremental bind/check as a future enhancement.

5. **Tyhp runtime package distribution:** The build action adds Composer dependencies for `tyhp/core`, `tyhp/decimal`, and `tyhp/async` as needed based on feature usage. The build step runs `composer require tyhp/core` (and `tyhp/decimal`, `tyhp/async` as needed) to ensure runtime packages are available. Distribution is Composer-only.

6. **Duplicate fully-qualified type names across packages:** If two different packages each define the same fully-qualified type name (same FQN), that is a **compile-time error**. The binder / multi-package resolution must detect the conflict and surface a diagnostic; the build must not silently merge or shadow.

### Namespace and File Organization

New and modified files for this story:

```
Tyhp/CLI/
├── BuildAction.cs              (replace skeleton from Story 01, ~400 lines)
├── TyhpHostedService.cs        (wire BuildAction, modify)
└── ActionRunnerBase.cs         (minor: verify Result property, modify)

Tyhp/Config/
├── Project.cs                  (expand with 25+ new config properties, modify)
├── BuildConfig.cs              (new: build-specific config section, ~120 lines)
├── OutputConfig.cs             (new: output-specific config section, ~80 lines)
├── CheckerConfig.cs            (new: checker-specific config section, ~50 lines)
└── TyhpdefConfig.cs            (new: tyhpdef-specific config section, ~50 lines)

Tyhp/Domain/Services/
├── CompilationService.cs       (minor refinements, modify)
├── OutputWriterService.cs      (new: handles writing PHP files to disk, ~250 lines)
└── ComposerJsonService.cs      (new: handles composer.json generation/update, ~150 lines)
```

### Safety Guidance

- **Before replacing the Story 01 `BuildAction` skeleton**, create a timestamped backup: `BuildAction.cs.bak.<timestamp>` (e.g. `BuildAction.cs.bak.20260216_143000`)
- **Before modifying `Project.cs`**, create a backup — this is a central file with existing working logic
- **Never use destructive git commands** (`git reset`, `git checkout .`, `git clean`)
- **Incremental edits preferred** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

### MessageCode Numbering

Story 10 introduces configuration and CLI error codes:

| Range | Component | Source |
|-------|-----------|--------|
| 6000s | Configuration errors | New in this story |
| 7100–7199 | Build action errors | New in this story |

**7000s CLI Action Subdivision:**

The 7000–7999 range is subdivided into 100-code sub-ranges per CLI action to avoid code collisions across stories:

| Range | CLI Action |
|-------|-----------|
| 7000–7099 | Shared CLI / generic errors |
| 7100–7199 | `build` action |
| 7200–7299 | `lint` action |
| 7300–7399 | `language_server` action |
| 7400–7499 | `xdebug_proxy` action |
| 7500–7599 | `generate_tyhpdef` action |
| 7600–7699 | `init` action |
| 7700–7799 | `composer` action |
| 7800–7899 | `debug` / `integrity_check` actions |
| 7900–7999 | Reserved for future CLI actions |

---

## Phase 1: Expand Configuration Parsing — Output and Build Options




### Phase Overview

Expand `Project.cs` and create dedicated configuration section classes to parse the 25+ new configuration options from `tyhp.json` that the build pipeline requires. Without these options, the build action cannot determine output paths, PHP version targets, or emitter behavior. This phase creates the configuration infrastructure; subsequent phases consume it.

### Deliverables

- `Tyhp/Config/OutputConfig.cs` — Output-specific configuration section
- `Tyhp/Config/BuildConfig.cs` — Build-specific configuration section
- `Tyhp/Config/CheckerConfig.cs` — Checker-specific configuration section
- `Tyhp/Config/TyhpdefConfig.cs` — Tyhpdef-specific configuration section
- Modified `Tyhp/Config/Project.cs` — Parse new sections, expose via properties
- New `MessageCode` values for configuration errors (6000s range)
- Modified `Tyhp/Domain/Exceptions/MessageCode.cs` — Add configuration error codes

### Implementation Details

**`OutputConfig.cs`**

Create in namespace `Tyhp.Config`:

- `string Path { get; set; }` — output directory for compiled PHP files (default: `"build/"`)
- `string? NamespacePrefix { get; set; }` — prefix added to all namespaces in output (default: `null`, no prefix)
- `bool IncludeComments { get; set; }` — include/exclude comments in output (default: `true`)
- `string PhpVersion { get; set; }` — target PHP version (default: `"8.4"`)
- `bool StrictTypes { get; set; }` — add `declare(strict_types=1)` to all output files (default: `true`)

Parse from `tyhp.json` keys: `output:path`, `output:namespacePrefix`, `output:comments`, `output:phpVersion`, `output:strictTypes`

**`Project.cs` — Project Type**

Add a `ProjectType` enum and property to `Project.cs`:

- `ProjectType Type { get; set; }` — the project type (default: `application`)
- Enum values: `application` (default — compiled for direct execution), `library` (compiled as a reusable package distributed via Composer)

Parse from `tyhp.json` key: `type`

When `Type` is `library`:
- `build.generateTyhpdef`: when JSON omits the key, property stays `null` until `Project.ConfigChanged()` applies `this.Build.GenerateTyhpdef ??= (this.Type == ProjectType.Library)` (see `BuildConfig.GenerateTyhpdef` below) — **library ⇒ `true`**, application ⇒ `false`
- **Entrypoint validation:** Library projects must **error** on entrypoint source files (files containing root-level side-effect statements — executable top-level code). This applies **regardless of** `build.generateTyhpdef` (including when tyhpdef generation is disabled).
- When tyhpdef generation runs (Story 20, Track C), output includes: `.tyhpdef` files under `_tyhpdef/` using **dot-notation** filenames (e.g. `App.Models.User.tyhpdef`), supporting `.tyhp` files under `_tyhpdef/support/`, and a `package.tyhp.json` manifest in the **build output directory** whose `include` field is an array of **globs** pointing at those artifacts (see Step 6.5).
- The `package.tyhp.json` is auto-included when the library is installed as a Composer dependency in another Tyhp project
- **Tagless flag in the generated manifest:** the consuming compiler honors a package's own tagless setting when loading its files (Story 06, Phase 7 — `package.tyhp.json` supports an optional `source.tagless` boolean, default `false`). If the tyhpdef generator (Story 20) emits the `_tyhpdef/**` artifacts **without** open tags, the generated `package.tyhp.json` **must** declare `source.tagless: true` so consumers parse them correctly; if it emits them with classic `<?tyhpdef` / `<?tyhp` tags, the flag is omitted (defaults to `false`). The flag describes how *this* package's published files were authored and is independent of the consuming project's `source.tagless`.

When `Type` is `application`:
- `build.generateTyhpdef`: same `??=` resolution as above — **application ⇒ `false`** when omitted
- No `package.tyhp.json` or `_tyhpdef/` Track C output unless the project is a library with tyhpdef generation enabled (Story 20)

**`BuildConfig.cs`**

Create in namespace `Tyhp.Config`:

- `bool? GenerateTyhpdef { get; set; }` — auto-generate tyhpdef for compiled code (default: `null` — resolved in `Project.ConfigChanged()` immediately after parsing `type`: defaults to `true` for `library` projects, `false` for `application` projects via `this.Build.GenerateTyhpdef ??= (this.Type == ProjectType.Library)`)
- `bool GenerateSourcemap { get; set; }` — generate sourcemaps (default: `false`). Sourcemap configuration is consolidated in `BuildConfig.GenerateSourcemap` (key `build:generateSourcemap`). Story 17 reads this value from `BuildConfig` rather than adding a duplicate property on `Project.cs`.
- `bool SourceMapIncludeContent { get; set; }` — include original source content in sourcemaps (default: `false`). Parsed from `build:sourcemapIncludeContent` key. When `true`, the generated `.map` files embed the original Tyhp source in the `sourcesContent` array, making sourcemaps self-contained (no need for the original `.tyhp` files at debug time).
- `bool UpdateComposer { get; set; }` — generate/update `composer.json` for PSR-4 (default: `false`)
- `Dictionary<string, string>? EntryPointAutoloader { get; set; }` — autoloader file paths keyed by name (default: `null`). Keys are logical autoloader names (e.g., `"composer"`, `"custom"`). Values are file paths relative to project root (e.g., `"vendor/autoload.php"`). The emitter prepends `require_once` statements for these files at the top of entry point output files. Example config: `{ "composer": "vendor/autoload.php" }`.
- `string StructBacking { get; set; }` — how to back structs: `"array"` or custom class (default: `"array"`)
- `string DecimalBacking { get; set; }` — `"bcmath"` or `"gmp"` (default: `"bcmath"`)
- `int DecimalScale { get; set; }` — default decimal scale (default: `28`)
- `string DecimalRounding { get; set; }` — default rounding mode (default: `"halfUp"`)
- `bool AllowEval { get; set; }` — re-enable `eval()` (default: `false`)
- `bool CleanBeforeBuild { get; set; }` — wipe output directory before building (default: `false`)
- `bool Verbose { get; set; }` — detailed output (default: `false`)
- `bool DryRun { get; set; }` — check without writing files (default: `false`)
- `bool StrictMode { get; set; }` — treat warnings as errors (default: `false`)
- `Dictionary<string, string>? Psr4 { get; set; }` — PSR-4 namespace aliases (default: `null`)
- `List<string>? Psr4Includes { get; set; }` — additional PSR-4 autoload paths (default: `null`)
- `string? Profile { get; set; }` — build profile name: `"debug"`, `"balanced"`, `"release"` (default: `null` — no profile). Sets defaults for `optimize`, `generateSourcemap`, and other settings. See Story 23 for profile definitions.
- `string? Optimize { get; set; }` — optimization level: `"none"`, `"basic"`, `"aggressive"` (default: `null` — resolved from profile or defaults to `"none"`). See Story 23 for level definitions.
- `Dictionary<string, bool>? Optimizations { get; set; }` — individual optimization module overrides (default: `null`). Keys are module config keys (e.g., `"extensionOperatorInlining"`, `"constantFolding"`). Values enable (`true`) or disable (`false`) specific modules on top of the resolved level. See Story 23 for module config keys.
- `bool ExperimentalReadonlyCloneWith { get; set; }` — enable anonymous class wrapper for `clone ... with` on readonly properties for PHP < 8.5 (default: `false`). See Story 11, Phase 6.
- `bool RuntimeGenericChecks { get; set; }` — emit runtime type checks at generic boundaries (default: `false`). See Story 11, Phase 8.

Parse from `tyhp.json` keys: `build:generateTyhpdef`, `build:generateSourcemap`, `build:sourcemapIncludeContent`, `build:updateComposer`, `build:entryPointAutoloader`, `build:structBacking`, `build:decimalBacking`, `build:decimalScale`, `build:decimalRounding`, `build:allowEval`, `psr4`, `psr4Includes`, `build:profile`, `build:optimize`, `build:optimizations`, `build:experimentalReadonlyCloneWith`, `build:runtimeGenericChecks`

CLI argument overrides for optimizer: `--profile=debug|balanced|release` → `Profile`, `--optimize=none|basic|aggressive` → `Optimize`, `--optimize-enable=key1,key2` → individual `true` overrides, `--optimize-disable=key1,key2` → individual `false` overrides

CLI argument overrides: `--clean` → `CleanBeforeBuild`, `--verbose` → `Verbose`, `--dry-run` → `DryRun`, `--strict` → `StrictMode`

**`CheckerConfig.cs`**

Create in namespace `Tyhp.Config`:

- ~~`bool AllowUncheckedMixed { get; set; }` — allow `mixed` without type guards (default: `false`)~~
  **Superseded 2026-07-24: removed.** `mixed` is Tyhp's single, always-strict top type (TypeScript's
  `unknown`, with no `any` counterpart); narrowing is unconditional and intentionally not configurable.
  Do not re-add this key. See `FOUND_BUGS.md` and `docs/content/tyhp_0600_theMixedType.json`.
- ~~`bool StrictNullChecks { get; set; }` — strict null checking (default: `true`)~~
  **Superseded 2026-07-24: removed.** Null safety is unconditional — using a possibly-null value
  always requires an explicit null check. Do not re-add this key.
- ~~`bool NoImplicitAny { get; set; }` — disallow implicit `mixed` on untyped variables (default: `true`)~~
  **Superseded 2026-07-24: removed.** Parameters, returns, and locals without a type annotation or
  inferable initializer always produce an error. Do not re-add this key.
- `int MaxFixIterations { get; set; }` — maximum auto-fix re-run iterations for `tyhp lint --fix` (default: `10`). See Story 12, Phase 6.

Parse from `tyhp.json` keys: `checker:maxFixIterations` (plus `checker:templateStringMaxStates`)

**`TyhpdefConfig.cs`**

Create in namespace `Tyhp.Config`:

- `List<string> Include { get; set; }` — glob patterns for tyhpdef files to load (default: `["**/*.tyhpdef"]`)
- `List<string> Exclude { get; set; }` — glob patterns for tyhpdef files to exclude (default: `[]`)

Parse from `tyhp.json` keys: `tyhpdefInclude`, `tyhpdefExclude`

**`Project.cs` Modifications**

Add new properties:

- `ProjectType Type { get; private set; }` — parsed from `tyhp.json` key `type` (default: `application`)
- `OutputConfig Output { get; private set; }`
- `BuildConfig Build { get; private set; }`
- `CheckerConfig Checker { get; private set; }`
- `TyhpdefConfig TyhpdefOptions { get; private set; }`

> **Pre-existing config preserved — `source.tagless`:** Story 06 (Phase 7) already added the `source.*` config group, currently just `bool Tagless` parsed from `source:tagless` (default `false`), surfaced on `CompilationOptions.Tagless`. Story 10's configuration expansion must **preserve** this property and its parsing; do not drop it when restructuring `ConfigChanged()`. It governs only the consuming project's **own** source files. Files loaded from packages honor *each package's* `package.tyhp.json` tagless setting instead (Story 06 package loader), so the project-level `source.tagless` must not be applied to package content.

In `ConfigChanged()`, parse the new sections. Use the existing `IConfiguration` pattern with section binding:

```csharp
// Example pattern for parsing nested config
this.Output = new OutputConfig();
this.Output.Path = this._configuration["output:path"] ?? "build/";
this.Output.PhpVersion = this._configuration["output:phpVersion"] ?? "8.4";
// ... etc
```

For CLI argument overrides, check `this._configuration["clean"]`, `this._configuration["verbose"]`, `this._configuration["dry-run"]`, `this._configuration["strict"]` and overlay onto the config section properties.

**`MessageCode.cs` Updates**

Add configuration error codes in the 6000s range:

- `ConfigUnknownError = 6001` — generic configuration error
- `ConfigMissingRequiredField = 6002` — a required configuration field is missing
- `ConfigInvalidValue = 6003` — a configuration value is out of range or invalid type
- `ConfigInvalidGlobPattern = 6004` — a glob pattern is malformed
- `ConfigOutputPathNotWritable = 6005` — the output path is not writable
- `ConfigInvalidPhpVersion = 6006` — the target PHP version is not recognized
- `ConfigPsr4InvalidMapping = 6007` — a PSR-4 mapping is invalid
- `ConfigInvalidProjectType = 6008` — the `type` field has an unrecognized value (must be `"application"` or `"library"`)

Add build action error codes in the 7100s range (7000s range is subdivided per CLI action — see MessageCode numbering guide):

- `BuildUnknownError = 7100` — generic build error
- `BuildNoSourceFiles = 7101` — no source files found matching include patterns
- `BuildOutputPathConflict = 7102` — multiple declarations write to the same output file
- `BuildFileWriteError = 7103` — failed to write an output file to disk
- `BuildCleanFailed = 7104` — failed to clean the output directory
- `BuildRuntimePackageNotAvailable = 7105` — Tyhp runtime Composer package not available for installation

### Acceptance Criteria

- [ ] `ProjectType` enum (`application`, `library`) is defined and parsed from `tyhp.json`
- [ ] `OutputConfig`, `BuildConfig`, `CheckerConfig`, `TyhpdefConfig` classes exist with all properties listed above
- [ ] `build.generateTyhpdef` defaults to `true` when `type` is `"library"`, `false` when `"application"` (via `??=` resolution after parsing `type`)
- [ ] Library project type is documented to forbid entrypoint files regardless of `build.generateTyhpdef`
- [ ] Each config class has sensible defaults matching the defaults specified above; `build.generateTyhpdef` omitted in JSON resolves via `this.Build.GenerateTyhpdef ??= (this.Type == ProjectType.Library)`
- [ ] `Project.cs` exposes `Output`, `Build`, `Checker`, `TyhpdefOptions` properties
- [ ] Values from `tyhp.json` are parsed into the new config sections (testable by creating a `tyhp.json` with these fields and inspecting `Project` after construction)
- [ ] CLI argument overrides (`--clean`, `--verbose`, `--dry-run`, `--strict`) correctly overlay onto config properties
- [ ] `MessageCode.cs` has new codes in the 6000s and 7000s ranges
- [x] Existing `Project.cs` functionality (IncludePaths, ExcludePaths, CacheDir, Locale, BeQuiet, GetProjectSourceFiles, GetProjectPath) is not broken
- [ ] Unknown or malformed configuration values log a warning but do not crash the compiler
- [ ] The project compiles with no errors after all changes

### Dependencies

- **Requires:** Story 01 complete (the config is consumed by `BuildAction` which was skeletoned in Story 01)
- **Provides:** Configuration infrastructure for all subsequent phases of this plan. Phase 2 (BuildAction) reads `Project.Output`, `Project.Build`, etc.

---

## Phase 2: Implement Full BuildAction Pipeline




### Phase Overview

Replace the Story 01 skeleton `BuildAction` with the complete compilation pipeline. This is the centerpiece of Story 10 — wiring parse → bind → check → emit → write into a single cohesive flow. Each pipeline phase uses the configuration from Phase 1 and the infrastructure from Stories 01–09 (plus the optional optimizer pass, Stories 23–24, when present).

### Deliverables

- Replaced `Tyhp/CLI/BuildAction.cs` — Full pipeline implementation (~400 lines)
- Modified `Tyhp/CLI/TyhpHostedService.cs` — Wire `BuildAction` with `Project` config and proper exit code handling
- All `PLACEHOLDER_STORY_*` markers in `BuildAction` replaced with actual implementations

### Implementation Details

**`BuildAction.cs` — Full Implementation**

The `BuildAction` class should accept `Project` via constructor (to access all configuration). The `Start(CancellationToken)` method implements the full pipeline:

**Step 1: Configuration validation and setup**

- Read `Project.Output`, `Project.Build`, `Project.Checker`, `Project.TyhpdefOptions`
- Validate output path exists or can be created
- If `--clean` is set, wipe the output directory (with safety check — refuse to clean if it's the project root or a parent of the source directory)
- If `--verbose`, set a verbose flag for detailed logging throughout

**Step 2: File discovery**

- Call `Project.GetProjectSourceFiles()` to discover `.tyhp` and `.php` source files
- If no files found, add `MessageCode.BuildNoSourceFiles` warning and return early
- Log file count and total size if verbose
- Separate tyhpdef files from source files: use `TyhpdefConfig.Include`/`Exclude` globs to discover `.tyhpdef` files in the project and standard tyhpdef directories

**Step 3: Parse source files**

- Create `CompilationOptions` from `Project` config:
  - `MaxThreads` from project config or default (-1 unlimited)
  - `EnableAstCache` from project config (default true)
  - `ReportAmbiguities` = false (not needed for build)
  - `GarbageCollectInterval` from project config (default 1000)
- Start a `Stopwatch` for parse timing
- Call `CompilationService.ParseFiles(sourceFiles, options, cancellationToken)`
- Record `CompilationResult.ParseDuration`
- If `CompilationResult.Diagnostics.HasErrors` after parsing, skip subsequent phases but still report diagnostics
- Log parse results if verbose (file count, error count, cache hit rate)

**Step 4: Binding (handled by CompilationService)**

CompilationService performs binding after successful parsing (added by Story 05). Verify that binding integration is present and functioning correctly. This includes:
- Creating a `GlobalScope` and populating built-in types, constants, and superglobals
- Loading and parsing tyhpdef files via `Tyhpdef.Get()`
- Running the full `TyhpBinder.Bind()` (declaration pass + resolution pass)
- Setting `BoundSymbol` on all declaration and reference AST nodes (Story 05)
- Recording `CompilationResult.GlobalScope` and `CompilationResult.BindDuration`

The `BuildAction` does NOT run the binder separately — it is integrated into `CompilationService.ParseFiles()`. After this call, `CompilationResult` contains `ParsedFiles` with `BoundSymbol` populated on AST nodes and `GlobalScope` with the complete symbol tree.

If verbose, log bind results from `CompilationResult` (scope count, symbol count, unresolved references, bind duration).

**Step 5: Run checker**

- If `CompilationResult.Diagnostics.HasErrors`, skip checker (binding errors prevent meaningful checking)
- Create `TyhpChecker` with `CompilationResult.Diagnostics` and `Project.Checker` config
- Call `checker.Check(result.ParsedFiles, result.GlobalScope)`
- Record `CompilationResult.CheckDuration`
- Log checker results if verbose

**Step 5.5: Library entrypoint validation (after check)**

- If `Project.Type == Library`: verify **no** compilation unit is an **entrypoint** (root-level side-effect / executable top-level statements — same notion as entrypoint files elsewhere in this plan). If any entrypoint exists, add a **compile error** (or rely on checker/binder diagnostics if that phase already reports it) and ensure the build does not proceed to tyhpdef generation, optimization, or emit — **regardless of** `build.generateTyhpdef`

**Step 6: Error gate — decide whether to continue to emission**

- If `CompilationResult.Diagnostics.HasErrors`:
  - Display all diagnostics
  - Display timing summary
  - Set exit code to `ExitCode.CompileError`
  - Return `CompilationResult`
- If `CompilationResult.Diagnostics.HasWarnings` and `Project.Build.StrictMode`:
  - Display all diagnostics
  - Display timing summary
  - Set exit code to `ExitCode.CompileWarning`
  - Return `CompilationResult` (skip emit in strict mode with warnings)
- Otherwise, continue to emission

**Step 6.5: Tyhpdef Track C — `_tyhpdef/`, support `.tyhp`, and `package.tyhp.json` (library + `generateTyhpdef`)**

- If `Project.Build.GenerateTyhpdef` resolves to `true` (library default via `??=`):
  - `// PLACEHOLDER_STORY_20: Generate tyhpdef for compiled code (Track C)`
  - Emit **`.tyhpdef`** files under **`_tyhpdef/`** using **dot-notation** filenames mirroring the FQN (e.g. `App.Models.User.tyhpdef`)
  - Emit any **supporting `.tyhp`** files under **`_tyhpdef/support/`** as required by Story 20
  - Write **`package.tyhp.json`** in the **build output directory** with an **`include`** array of **globs** that cover the generated `_tyhpdef/**` artifacts (and support files as needed) so consumers resolve the manifest consistently. If the generated artifacts are tagless (no open tags), also write **`source.tagless: true`** into the manifest so the consuming compiler's package loader (Story 06, Phase 7) parses them via the tagless entry rules; otherwise omit the flag (defaults to `false`).
  - This MUST run **before** the optimizer (Step 7) to capture the unoptimized public API

**Step 7: Run optimizer (Story 23)**

- Resolve `OptimizationConfig` from `Project.ResolveOptimizationConfig()` (profile → level → individual overrides)
  - `Project.ResolveOptimizationConfig()` (parameterless — it resolves the active `build:profile` internally; defined by Story 23, which owns this method) returns a resolved `OptimizationConfig` that merges profile defaults with user overrides from `tyhp.json`. Defined in Phase 1 as part of configuration setup. Merge precedence: profile defaults (lowest) → `tyhp.json` `build:optimize` level and `build:optimizations` module overrides → CLI argument overrides `--optimize-enable`/`--optimize-disable` (highest).
- If optimization level is not `None` or individual overrides enable specific modules:
  - Start timing for optimize phase
  - Create `TyhpOptimizer` and `OptimizationContext` with AST trees, global scope, diagnostics, config, and project type
  - Call `optimizer.Optimize(context)` → returns per-module metrics
  - Store metrics in `CompilationResult.OptimizationMetrics`
  - Record `CompilationResult.OptimizeDuration`
  - Log per-module metrics if verbose
- If optimization level is `None` and no individual overrides: skip entirely (zero overhead)

**Step 8: Run emitter**

- Start timing for emit phase
- Create `TyhpEmitter` with `Project` config (emitter needs output config, build config for struct backing, decimal backing, etc.)
- Call `emitter.Emit(result.GlobalScope, result.ParsedFiles, project)` → returns `IEnumerable<PHPOutputFile>`
- Store output files in `CompilationResult.OutputFiles`
- Record `CompilationResult.EmitDuration`
- If `--dry-run`, skip file writing but still report what would be written

**Step 9: Write output files**

- If not `--dry-run`:
  - Create an `OutputWriterService` (see Phase 3)
  - Call `outputWriter.WriteAll(result.OutputFiles)` → writes PHP files to disk (see Phase 3: when sourcemaps are enabled, **`//# sourceMappingURL=...` is appended to each file's in-memory PHP string before** `File.WriteAllText`; **companion `.map` JSON is written afterward**, typically via `WriteSourcemap` per file)
  - Handle file path conflicts (report `MessageCode.BuildOutputPathConflict`)
  - Create output directories as needed
  - Log each file written if verbose
- If `Project.Build.UpdateComposer`:
  - Use `ComposerJsonService` (see Phase 4) to generate/update `composer.json`

**Step 10: Display summary**

- Display all diagnostics via `CompilationResult.Diagnostics.DisplayAll()`
- Display timing for each phase:
  - Parse: `CompilationResult.ParseDuration`
  - Bind: `CompilationResult.BindDuration`
  - Check: `CompilationResult.CheckDuration`
  - Optimize: `CompilationResult.OptimizeDuration` (if optimizer ran)
  - Emit: `CompilationResult.EmitDuration`
  - Total: sum of all
- Display optimizer summary (if optimizer ran):
  - Optimizations: N transformations (M modules active)
- Display summary counts:
  - Files processed (input count)
  - Files written (output count, or "0 (dry run)" if dry-run)
  - Errors, Warnings
  - Peak memory usage
- Return `CompilationResult`

**`TyhpHostedService.cs` Updates**

In `case Tyhp.Config.Action.build:`:

```csharp
case Tyhp.Config.Action.build:
    var buildAction = new BuildAction(this.project);
    var buildResult = buildAction.Start(this._actionCancelTokenSource.Token);
    Environment.ExitCode = (int)(buildResult?.GetExitCode() 
        ?? Tyhp.Domain.Enums.ExitCode.Success);
    break;
```

Pass `this.project` to `BuildAction` so it has access to all configuration. `BuildAction.Start()` returns a `CompilationResult?` which is used to determine the exit code via `GetExitCode()`.

### Acceptance Criteria

- [ ] `BuildAction` implements the full 10-step pipeline as described above (including library entrypoint validation regardless of `generateTyhpdef`)
- [ ] All `PLACEHOLDER_STORY_*` markers from Story 01 are replaced with actual implementations (except `PLACEHOLDER_STORY_20` for tyhpdef generation)
- [ ] `tyhp build` command executes the full pipeline: parse → bind → check → (tyhpdef) → optimize → emit → write
- [ ] Build action reads configuration from `Project.Output`, `Project.Build`, `Project.Checker`
- [ ] Build action stops after checker if there are errors (does not attempt emission with broken code)
- [ ] Build action respects `--strict` mode (warnings = failure)
- [ ] Build action respects `--dry-run` (reports what would be written without writing)
- [ ] Build action respects `--verbose` (detailed logging at each phase)
- [x] Build action reports per-phase timing in the summary
- [x] Build action reports file counts, error counts, warning counts in the summary
- [x] Build action sets `Environment.ExitCode` correctly: `Success` (0), `CompileError` (4), `CompileWarning` (5)
- [x] `TyhpHostedService` passes `project` to `BuildAction` and reads exit code via `GetExitCode()`
- [ ] Running `tyhp build` on a valid Tyhp project produces PHP output files in the configured output directory
- [ ] Running `tyhp build` on a project with type errors reports errors and exits with `CompileError`
- [ ] Two packages defining the same fully-qualified type name is reported as a compile-time error (no silent merge)
- [ ] The build action handles cancellation via `CancellationToken` at each phase boundary
- [x] No regressions in existing `DebugAction`, `GenerateTyhpdefAction`, `IntegrityCheckAction`

### Dependencies

- **Requires:** Phase 1 (configuration parsing), Stories 01–09 (spine pipeline components; optimizer 23–24 optional)
- **Provides:** Functional `tyhp build` command for Phase 3–5 to refine and extend

---

## Phase 3: Output Writer Service




### Phase Overview

Create the `OutputWriterService` that handles the mechanics of writing compiled PHP files to disk: creating directories, handling path conflicts, writing file content, and managing output directory hygiene. This is extracted from `BuildAction` to keep the action class focused on orchestration.

> **Authoritative file writer:** `Tyhp/Domain/Services/OutputWriterService.cs` (this story) is the **authoritative, canonical** mechanism for writing `.php` and `.map` files to disk. It **supersedes** Story 09's temporary `OutputFileWriter`/`BuildAction` wiring, which was explicitly minimal and provisional. Story 09 only produces output **strings** (`PHPOutputFile.Generate()`); persisting them to disk is owned here. Any remaining references to Story 09's temporary writer should be considered replaced by `OutputWriterService`.

### Deliverables

- `Tyhp/Domain/Services/OutputWriterService.cs` — File writing service (~250 lines)
- Integration with `BuildAction` (Phase 2) for the file-writing step

### Implementation Details

**`OutputWriterService.cs`**

Create in namespace `Tyhp.Domain.Services`:

**Constructor:**
- Accept `DiagnosticBag diagnostics` for error reporting
- Accept `Project project` for output path configuration

**`WriteAll(IReadOnlyList<PHPOutputFile> outputFiles, bool dryRun = false)` method:**

Core file-writing logic:

1. Compute the full output path for each `PHPOutputFile`:
   - Base output directory: `Project.Output.Path` (resolved relative to project root)
   - For PSR-4 object declarations: compute path from namespace + class name using PSR-4 mapping rules
   - For root code files: place in the output root
   - For custom `declare(output_file="...")` files: use the specified path
   - Apply `Project.Output.NamespacePrefix` if set — strip or add namespace prefix segments from the directory path

2. Detect path conflicts:
   - Build a dictionary of `output path → list of PHPOutputFile`
   - If multiple files map to the same path, report `MessageCode.BuildOutputPathConflict` for each conflict
   - Skip conflicting files (do not overwrite)

3. For each non-conflicting file:
   - Ensure the output directory exists: `Directory.CreateDirectory(Path.GetDirectoryName(fullPath))`
   - Call `outputFile.Generate()` to produce the PHP source string
   - If `Project.Build.GenerateSourcemap` is true: **append** the `//# sourceMappingURL=...` line to that **in-memory** `content` string **before** any PHP write (Story 17 — the comment must be part of the string written to the `.php` file)
   - If not `dryRun`: write `content` to the `.php` path using `File.WriteAllText(fullPath, content, Encoding.UTF8)`
   - If `Project.Build.GenerateSourcemap` is true and not `dryRun`: after the `.php` file is written, generate JSON (e.g. `outputFile.SourceMap()`) and write the **separate** `.map` file (e.g. `WriteSourcemap`) — **map file after PHP**, not before
   - If `dryRun`: log the file path and size that would be written (and log map path when sourcemaps are enabled)
   - Track the file in a list of written files for summary reporting

4. Return a `WriteResult` record:
   - `int FilesWritten` — count of files successfully written
   - `int FilesSkipped` — count of files skipped due to conflicts or errors
   - `List<string> WrittenPaths` — list of all file paths written
   - `List<string> SkippedPaths` — list of skipped file paths with reasons

**`CleanOutputDirectory(string outputPath)` method:**

Safety-checked output directory cleaning:

- Verify `outputPath` is not the project root
- Verify `outputPath` is not a parent of any source directory (from `Project.IncludePaths`)
- Verify `outputPath` is not a system directory (e.g., `/`, `C:\`, home directory)
- If all checks pass: delete all `.php` and `.php.map` files in the directory recursively
- Do NOT delete the directory itself or non-PHP files (preserve `.gitkeep`, `README`, etc.)
- If checks fail: add `MessageCode.BuildCleanFailed` diagnostic and skip cleaning

**`ComputeOutputPath(PHPOutputFile outputFile, Project project)` method:**

Path computation logic:

- If `outputFile.IsPSR4ObjectDeclaration`:
  - Extract namespace from `outputFile.FileNameSpace`
  - Check `Project.Build.Psr4` for namespace prefix mappings
  - Map namespace segments to directory segments
  - Class name becomes the filename with `.php` extension
  - Combine: `{outputDir}/{mappedNamespaceDir}/{ClassName}.php`
- If `outputFile.FilePath` is set (from `declare(output_file="")`):
  - Use the explicitly declared path, resolved relative to `outputDir`
- Otherwise (root code, namespace functions):
  - For namespace functions: use namespace path + `_functions.php` (e.g., `src/Helpers/_functions.php`)
  - For root code (entry points): mirror the source file's path relative to the source root, replacing `.tyhp` with `.php` (e.g., `tyhp-src/web/index.tyhp` → `src/web/index.php`)
- Apply `NamespacePrefix` stripping if configured

**Sourcemap writing integration (Story 17):**

- **`//# sourceMappingURL=...` belongs in the PHP file content:** append it to the string returned from `Generate()` **before** `File.WriteAllText` for the `.php` file. Do not write the PHP file first and then patch the file on disk for the comment unless the implementation explicitly uses a single string buffer for the final bytes.
- `WriteSourcemap(PHPOutputFile outputFile, string phpFilePath)` (or equivalent): call `outputFile.SourceMap()` to produce the map JSON and write `phpFilePath + ".map"` **after** the `.php` file has been written.

**Note:** This sourcemap handling in OutputWriterService is initial/placeholder logic. Story 17 will introduce a dedicated `SourceMapWriter` class, and the sourcemap logic in OutputWriterService will be refactored to delegate to `SourceMapWriter` at that time.

### Acceptance Criteria

- [ ] `OutputWriterService` can write `PHPOutputFile` instances to disk at computed paths
- [ ] PSR-4 path computation correctly maps namespaces to directories (e.g., `App\Models\User` → `src/Models/User.php` if `psr4: {"App\\": "src/"}`)
- [ ] Path conflicts are detected and reported as diagnostics (not thrown as exceptions)
- [ ] Output directories are created as needed
- [ ] `CleanOutputDirectory` only deletes `.php` and `.php.map` files, not other files
- [ ] `CleanOutputDirectory` refuses to clean dangerous paths (project root, system directories)
- [ ] `WriteAll` with `dryRun=true` produces a summary without writing any files
- [ ] When sourcemaps are enabled, `//# sourceMappingURL=...` is present in the written `.php` content (appended before the PHP write), and `.map` files are written separately afterward
- [ ] `WriteResult` contains accurate counts of written/skipped files
- [ ] File encoding is UTF-8 for all written files

### Dependencies

- **Requires:** Phase 1 (configuration for output paths, PSR-4 mappings), Story 09 (PHPOutputFile.Generate(), PHPOutputFile.SourceMap())
- **Provides:** File writing capability for BuildAction (Phase 2 integration)

---

## Phase 4: Composer JSON Service




### Phase Overview

Create a service that generates or updates `composer.json` in the output directory to support PSR-4 autoloading of the compiled PHP output. This enables compiled Tyhp projects to be used with Composer's autoloader, which is standard practice in the PHP ecosystem.

### Deliverables

- `Tyhp/Domain/Services/ComposerJsonService.cs` — Composer.json generation/update service (~150 lines)
- Integration with `BuildAction` for the `build.updateComposer` configuration option

### Implementation Details

**`ComposerJsonService.cs`**

Create in namespace `Tyhp.Domain.Services`:

**`GenerateOrUpdate(string outputDirectory, Project project, IReadOnlyList<PHPOutputFile> outputFiles)` method:**

1. Check if `composer.json` already exists in `outputDirectory`
2. If it exists:
   - Read and parse the existing JSON
   - Merge PSR-4 autoload entries without overwriting user additions
   - Update the `autoload.psr-4` section with namespace-to-directory mappings derived from the compiled output
3. If it does not exist:
   - Generate a new `composer.json` with:
     - `name`: derived from project config or a default
     - `autoload.psr-4`: namespace-to-directory mappings
     - `require`: Tyhp runtime packages based on feature usage (`tyhp/core`, `tyhp/decimal`, `tyhp/async`)

**PSR-4 autoload mapping computation:**

- Walk the `outputFiles` list
- For each `IsPSR4ObjectDeclaration` file, extract the namespace
- Group by namespace prefix
- Generate PSR-4 entries: `{ "App\\": "src/", "App\\Models\\": "src/Models/" }`
- Respect `Project.Build.Psr4` configuration for custom mappings
- Respect `Project.Output.NamespacePrefix` for prefix adjustments

**Function file autoload (`autoload.files`) computation:**

- Walk the `outputFiles` list for files that are namespace-level function groupings (output as `_functions.php`)
- For each `_functions.php` file, add its path (relative to the output directory) to the `autoload.files` array in `composer.json`
- Example: if the emitter produces `src/Helpers/_functions.php` and `src/Utils/_functions.php`, the generated `composer.json` includes:
  ```json
  {
      "autoload": {
          "psr-4": { "App\\": "src/" },
          "files": [
              "src/Helpers/_functions.php",
              "src/Utils/_functions.php"
          ]
      }
  }
  ```
- This is required because Composer's PSR-4 autoloader only loads classes — standalone PHP functions in `_functions.php` files must be explicitly listed in `autoload.files` to be auto-included
- When merging with an existing `composer.json`, append new `_functions.php` paths to the existing `files` array without duplicating entries
- The `tyhp/decimal` runtime package already uses this pattern for its `\Tyhp\decimal()` factory function

**Tyhp runtime package require entries:**

- Based on feature usage in the compiled output, add `require` entries for the appropriate Tyhp runtime Composer packages:
  - `tyhp/core` — generic types, property accessors, disposables, named types
  - `tyhp/decimal` — decimal operations
  - `tyhp/async` — async/await, Promise, async disposables
- Package versions should match the compiler version

**Autoloader include in entry points:**

- If `Project.Build.EntryPointAutoloader` is configured:
  - For each entry point file (root code files), add a `require_once` statement at the top for the autoloader path
  - E.g., `require_once __DIR__ . '/vendor/autoload.php';`
  - The `EntryPointAutoloader` config maps named autoloaders to their paths

### Acceptance Criteria

- [ ] `ComposerJsonService` can generate a new `composer.json` with correct PSR-4 mappings and `autoload.files` entries
- [ ] `ComposerJsonService` can update an existing `composer.json` without overwriting user additions (both PSR-4 and files arrays)
- [ ] PSR-4 mappings correctly reflect the namespace structure of the compiled output
- [ ] All `_functions.php` files are included in `autoload.files`
- [ ] The generated `composer.json` is valid JSON
- [ ] Tyhp runtime packages (`tyhp/core`, `tyhp/decimal`, `tyhp/async`) are included as dependencies based on feature usage
- [ ] The service does nothing if `Project.Build.UpdateComposer` is false
- [ ] No files are written in dry-run mode

### Dependencies

- **Requires:** Phase 1 (configuration for PSR-4, autoloader, updateComposer), Phase 3 (output file paths are known)
- **Provides:** Composer autoloading support for compiled output

---

## Phase 5: Incremental Compilation and Build Performance




### Phase Overview

Implement incremental compilation support using the existing `AstCacheService` infrastructure. When a file has not changed since the last build, skip re-parsing it and use the cached AST. Also implement smart rebuild logic that detects which files need re-checking based on dependency analysis.

### Deliverables

- Modified `Tyhp/Domain/Services/CompilationService.cs` — Enhanced cache-hit tracking and reporting
- `Tyhp/Domain/Services/IncrementalBuildService.cs` — Tracks file changes and determines rebuild scope (~200 lines)
- Modified `Tyhp/CLI/BuildAction.cs` — Integrate incremental compilation
- Build state persistence (save/load build state between runs)

### Implementation Details

**`IncrementalBuildService.cs`**

Create in namespace `Tyhp.Domain.Services`:

**File change detection:**

- `DetermineChangedFiles(IEnumerable<string> allFiles, string buildStatePath)`:
  - Load the previous build state (a JSON file mapping `filePath → hash`)
  - For each file, compute SHA256 hash and compare against the stored hash
  - Return three lists:
    - `ChangedFiles` — files whose hash differs from the stored state
    - `NewFiles` — files not present in the stored state
    - `RemovedFiles` — files in the stored state but no longer present on disk
    - `UnchangedFiles` — files with matching hashes

**Dependency analysis (simplified initial version):**

A complete dependency analysis would track which files import which other files and rebuild all dependents when a file changes. For the initial implementation, use a simpler approach:

- If ANY file changed → re-bind and re-check ALL files (binding is a global operation due to namespace merging). The binder must also enforce **no duplicate FQN** across distinct packages (compile-time error — see Key Design Decisions).
- Only skip re-PARSING of unchanged files (use AST cache)
- This is conservative but correct

Future enhancement (not in this phase):
- `// PLACEHOLDER_STORY_19: Incremental binding based on dependency graph`
- Track `use`/`import` statements to build a dependency graph
- Only re-bind/re-check files affected by changes

**Build state persistence:**

- After a successful build, save the build state:
  - File path → SHA256 hash mapping
  - Compiler version (invalidate state if compiler version changes)
  - Configuration hash (invalidate state if `tyhp.json` changes)
  - Timestamp
- Store at `{outputDir}/tyhp-build-state.json`
- If the state file does not exist or is invalid, treat all files as new (full rebuild)

**`CompilationService.cs` Enhancements**

The existing `CompilationService` already integrates with `AstCacheService` for parse-level caching. Enhance it to report cache statistics:

- Track and report: `cacheHits` (files loaded from AST cache), `cacheMisses` (files re-parsed)
- Add these to `CompilationProgress` for verbose reporting
- Add to `CompilationResult` as optional metadata

**`BuildAction.cs` Integration**

Before calling `CompilationService.ParseFiles()`:

1. Load previous build state via `IncrementalBuildService`
2. Determine changed/new/removed files
3. If no files changed and no configuration changed: log "Nothing to build" and exit early
4. Pass all files to `CompilationService.ParseFiles()` — the AST cache handles skipping unchanged files at the parse level
5. After successful build: save new build state

The `--clean` flag should also delete the build state file (forcing a full rebuild).

### Acceptance Criteria

- [ ] `IncrementalBuildService` correctly detects changed, new, removed, and unchanged files
- [ ] Unchanged files use cached ASTs (not re-parsed)
- [ ] If no files changed and config unchanged, the build exits early with "Nothing to build"
- [ ] Build state is persisted to disk after successful builds
- [ ] Build state is invalidated if compiler version changes
- [ ] Build state is invalidated if `tyhp.json` configuration changes
- [ ] `--clean` flag deletes the build state file
- [ ] `CompilationService` reports cache hit/miss statistics
- [ ] The full rebuild path (no cache) still works correctly
- [ ] Incremental builds produce the same output as full builds (correctness guarantee)
- [ ] Verbose mode shows cache statistics (e.g., "Parsed 5/100 files (95 from cache)")

### Dependencies

- **Requires:** Phase 2 (BuildAction must be functional first), Story 01 (AstCacheService)
- **Provides:** Build performance optimization for iterative development workflows

---

## Phase 6: Tyhp Runtime Package Distribution Integration




### Phase Overview

Implement the mechanism for managing Tyhp runtime Composer package dependencies in the compiled output. The build action needs to ensure that the appropriate runtime packages (`tyhp/core`, `tyhp/decimal`, `tyhp/async`) are added as Composer dependencies based on the features used in the compiled code. Distribution is Composer-only — the three packages are published from `runtime/packages/` as set up in Story 04.

### Deliverables

- `Tyhp/Domain/Services/TyhpLibDistributionService.cs` — Tyhp runtime Composer package dependency management (~150 lines)
- Modified `Tyhp/CLI/BuildAction.cs` — Integrate runtime package dependency step
- Documentation of runtime package distribution strategy in code comments

### Implementation Details

**`TyhpLibDistributionService.cs`**

Create in namespace `Tyhp.Domain.Services`:

**Feature-to-package mapping:**

Analyze the emitter output to determine which Tyhp runtime Composer packages are needed, then add the appropriate `composer require` entries:

- If any `decimal` operations exist → `composer require tyhp/decimal`
- If any generic runtime tracking (i.e., `GenericObject` trait usage, `tyhpGenericObjectInit()`, `tyhpGenericObjectSetPropertyType()`, `tyhpGenericObjectGetGenericType()`), type helpers (`Type`, `NamedType`), property accessors, or disposable (sync) patterns are emitted → `composer require tyhp/core`
- If any async/await or async disposable patterns are emitted → `composer require tyhp/async`

Each package is independent and managed via Composer from `runtime/packages/` (set up in Story 04).

**`AddRuntimePackageDependencies(string outputDirectory, Project project, DiagnosticBag diagnostics, IReadOnlyList<PHPOutputFile> outputFiles)` method:**

1. Analyze `outputFiles` to determine which runtime features are used
2. Build a list of required packages (`tyhp/core`, `tyhp/decimal`, `tyhp/async`)
3. Use `ComposerJsonService` (Phase 4) to add the required packages as `require` entries in `composer.json` (direct JSON modification)
4. After modifying `composer.json`, attempt to run `composer install` to install the packages:
   - Check for `composer` in PATH, then `composer.phar` in project root
   - If Composer is available: run `composer install` to install the new dependencies
   - If Composer is not available: log a warning with instructions ("Composer not found. Run `composer install` manually to install Tyhp runtime packages, or install Composer: https://getcomposer.org") but continue (the `composer.json` changes are written either way)
5. If a required package cannot be resolved: add `MessageCode.BuildRuntimePackageNotAvailable` warning diagnostic
6. If `Project.Build.UpdateComposer` is false: log which packages would be needed but skip adding them

**`DetermineRequiredPackages(IReadOnlyList<PHPOutputFile> outputFiles)` method:**

Returns a `List<string>` of required package names by reading the `EmitContext.RequiredPackages` set. During emission, each transformer registers the runtime packages it needs:

- `GenericTransformer` → adds `tyhp/core` (for `GenericObject`, `Type`, `NamedType`)
- `DisposableTransformer` → adds `tyhp/core` (for `DisposableScope`, `IsDisposable`)
- `WithKeywordTransformer` → adds `tyhp/core` (for `ObjectHelper`)
- `AsyncAwaitTransformer` → adds `tyhp/async` (for `Promise`, async runtime)
- `OperatorOverloadTransformer` → adds `tyhp/core` (for `InvalidParametersForOperatorOverloadException`)
- Decimal type emission → adds `tyhp/decimal`

`EmitContext.RequiredPackages` and `EmitContext.RequirePackage()` are defined in Story 11 Phase 1. Until Story 11 is implemented, stub this integration with a `// PLACEHOLDER_STORY_11: EmitContext.RequiredPackages provides required runtime package list` comment. Story 10 should define `EmitContext` as a class with a `RequiredPackages` property (`HashSet<string>`) and `RequirePackage(string packageName)` method as an empty scaffold that Story 11 will populate with actual transformer-driven logic.

The `EmitContext` (Story 11, Phase 1) maintains a `HashSet<string> RequiredPackages` that transformers call `EmitContext.RequirePackage("tyhp/core")` on during their `Transform()` or `PreProcess()` methods. This is more reliable than scanning output text because each transformer knows exactly what runtime features it uses, with no false positives.

### Acceptance Criteria

- [ ] `TyhpLibDistributionService` correctly determines which runtime packages are needed based on emitted output
- [ ] Required packages are added as Composer dependencies via `ComposerJsonService`
- [ ] Feature-to-package mapping is correct: decimal → `tyhp/decimal`, generic/type/property/disposable → `tyhp/core`, async/await/async disposable → `tyhp/async`
- [ ] Unavailable runtime packages produce a warning diagnostic (`BuildRuntimePackageNotAvailable`), not a crash
- [ ] The `BuildAction` calls runtime package dependency management after successful emission
- [ ] Dry-run mode logs which packages would be required without modifying `composer.json`
- [ ] When `UpdateComposer` is false, the service logs needed packages without adding them

### Dependencies

- **Requires:** Phase 2 (BuildAction), Phase 4 (ComposerJsonService), Story 04 (Tyhp runtime packages must be compiled and published)
- **Provides:** Runtime package availability for compiled Tyhp code

---

## Phase 7: End-to-End Validation and Polish




### Phase Overview

Validate the complete build pipeline by running it against the example files in `Examples/`, fixing any integration issues, polishing error messages, and ensuring the build summary output is clear and useful. This phase is the final integration testing and polish pass.

### Deliverables

- End-to-end validation results for all `Examples/*.tyhp` files
- Bug fixes for any integration issues found during validation
- Polished build summary output formatting
- Updated resource strings for new `MessageCode` values
- Comprehensive error handling for edge cases

### Implementation Details

**7.1 — Run Build on Example Files**

Execute `tyhp build` (or equivalent programmatic call) against the `Examples/` directory:

- `Examples/OperatorOverloads.tyhp` — tests operator overload emission
- `Examples/PropertyAccessors.tyhp` — tests property accessor emission
- `Examples/TypeGuards.tyhp` — tests type guard compilation
- `Examples/WithKeyword.tyhp` — tests `with` keyword emission
- `Examples/Structs.tyhp` — tests struct-to-array emission
- `Examples/Generics.tyhp` — tests generic type erasure
- `Examples/ExtensionMethods.tyhp` — tests extension method rewriting
- `Examples/AsyncAwait.tyhp` — tests async/await Promise emission
- All `.php` example files — tests PHP pass-through

For each file:
- Record whether it parses, binds, checks, and emits without crashes
- Record any diagnostic output
- If emission succeeds, inspect the PHP output for correctness
- Document any known limitations or expected failures

**7.2 — Fix Integration Issues**

Common integration issues to anticipate:

- **Phase ordering bugs:** The emitter may expect binder data that the binder doesn't set, or the checker may expect emitter types that don't exist yet. Trace through specific examples to find these.
- **Configuration not reaching downstream:** A config option is parsed in Phase 1 but not threaded through to the emitter or checker. Add the missing parameter passing.
- **Path resolution bugs:** Output paths computed incorrectly on different OS platforms (Windows vs. Linux). Test with forward and backward slashes.
- **Empty project handling:** `tyhp build` on a project with no source files should produce a helpful message, not crash.
- **Missing tyhpdef handling:** If tyhpdefs for referenced PHP extensions are missing, the build should warn but continue (with reduced type checking).

**7.3 — Polish Build Summary Output**

Design a clear, concise build summary format:

```
Build completed successfully.

  Files:     42 source files → 38 PHP files
  Duration:  1.23s (parse: 0.45s, bind: 0.12s, check: 0.34s, emit: 0.32s)
  Warnings:  2
  Memory:    45.2 MB peak

Output written to: ./build/
```

Or for failures:

```
Build failed with 3 errors.

  src/Models/User.tyhp(42,5): error TYHP4003: Member modifier 'static' is not allowed on interface methods
  src/Services/Auth.tyhp(15,10): error TYHP3003: Symbol 'InvalidUser' not found
  src/Services/Auth.tyhp(23,1): error TYHP4002: Multiple visibility modifiers specified

  Files:     42 source files
  Duration:  0.91s (parse: 0.45s, bind: 0.12s, check: 0.34s)
  Errors:    3
  Warnings:  0
```

Implement this formatting in `BuildAction` using the `Message` class methods.

**7.4 — Add Resource Strings for New MessageCodes**

Update the resource file (`.resx` from Story 01, Phase 8) with entries for all new `MessageCode` values:

- `ERROR_TYHP6001` = `"Configuration error: {0}"`
- `ERROR_TYHP6002` = `"Required configuration field '{0}' is missing"`
- `ERROR_TYHP6003` = `"Configuration value '{0}' is invalid for option '{1}'"`
- `ERROR_TYHP6004` = `"Invalid glob pattern: '{0}'"`
- `ERROR_TYHP6005` = `"Output path '{0}' is not writable"`
- `ERROR_TYHP6006` = `"Unrecognized PHP version: '{0}'. Supported: 8.2, 8.3, 8.4"`
- `ERROR_TYHP6007` = `"Invalid PSR-4 mapping: namespace '{0}' cannot map to path '{1}'"`
- `ERROR_TYHP7100` = `"Build error: {0}"`
- `ERROR_TYHP7101` = `"No source files found matching include patterns"`
- `ERROR_TYHP7102` = `"Output path conflict: multiple files write to '{0}'"`
- `ERROR_TYHP7103` = `"Failed to write output file '{0}': {1}"`
- `ERROR_TYHP7104` = `"Cannot clean output directory '{0}': {1}"`
- `WARNING_TYHP7105` = `"Tyhp runtime Composer package '{0}' is not available; compiled code may not run correctly without it"`

**7.5 — Edge Case Handling**

Implement graceful handling for these edge cases:

- **No `tyhp.json` file:** Use defaults for all configuration. Display informational message: "No tyhp.json found, using defaults."
- **Empty source directory:** Report `MessageCode.BuildNoSourceFiles` and exit with `Success` (not an error — the project is simply empty)
- **Disk full during write:** Catch `IOException`, add `MessageCode.BuildFileWriteError` diagnostic, continue writing remaining files
- **Permission denied on output path:** Catch `UnauthorizedAccessException`, add `MessageCode.ConfigOutputPathNotWritable` diagnostic, abort build
- **Circular namespace references:** The binder should catch this (Story 02), but verify the build action handles the resulting diagnostics gracefully
- **Very large projects (10,000+ files):** Ensure progress reporting works, memory stays reasonable, and the GC interval is effective

**7.6 — Watch Mode Placeholder**

Add a `--watch` placeholder in `BuildAction`:

- If `--watch` is specified, set `_isLongRunning = true` on the hosted service
- Log "Watch mode is not yet implemented" as an informational message
- `// PLACEHOLDER_STORY_19: File watcher for --watch mode` — the language server story may introduce file watching infrastructure that can be reused
- For now, just run a single build and exit

### Acceptance Criteria

- [ ] `tyhp build` runs successfully on at least 5 example files from `Examples/`
- [ ] Build summary output is clear, concise, and consistent (matches the format shown above)
- [ ] All new `MessageCode` values have corresponding resource strings
- [ ] Empty project produces a helpful message and exits with `Success`
- [ ] Missing `tyhp.json` uses defaults and produces an informational message
- [ ] Disk-full and permission-denied errors are handled gracefully (diagnostic, not crash)
- [ ] Very large file sets (tested with debug project's 1000+ files) parse without memory issues
- [ ] `--watch` flag is accepted and produces a "not yet implemented" message
- [ ] No regression in any existing CLI action
- [ ] The build pipeline is robust: any single-file error (parse, bind, check) does not crash the entire build

### Dependencies

- **Requires:** All previous phases (1–6), all prerequisite spine stories (01–09)
- **Provides:** A polished, production-quality `tyhp build` command ready for real-world use

---

## Cross-Cutting Concerns

### Error Recovery Strategy

The build action should be resilient to individual file errors:

1. **Parse errors in one file** — Other files continue parsing. The file with errors gets `ErrorAst` nodes and diagnostics.
2. **Bind errors in one file** — The binder continues with other files. Unresolved symbols are reported as diagnostics.
3. **Check errors in one file** — The checker continues with other files. Type errors are reported as diagnostics.
4. **Emit errors** — If emission fails for one file, skip it and continue with others. Report `MessageCode.EmitterUnknownError`.
5. **Write errors** — If writing fails for one file, skip it and continue with others. Report `MessageCode.BuildFileWriteError`.

The build action should NEVER crash due to a single file's issues. The `CompilationResult` collects all diagnostics, and the summary reports the total.

### Placeholder Convention

This story introduces placeholders for future work:

**Cross-story placeholders** (`// PLACEHOLDER_STORY_N:` — for work belonging to other TODO.md stories):
- `// PLACEHOLDER_STORY_12: SARIF output format for lint action` — Story 12 lint enhancements
- `// PLACEHOLDER_STORY_12: Auto-fix mode for lint action` — Story 12 lint `--fix`
- `// PLACEHOLDER_STORY_11: Advanced emitter features` — Story 11 emitter expansion
- `// PLACEHOLDER_STORY_17: Sourcemap generation integration` — Story 17 sourcemaps
- `// PLACEHOLDER_STORY_20: Generate tyhpdef for compiled code (Track C: package.tyhpdef + library package.tyhp.json in the build output directory)` — Story 20 tyhpdef generation
- `// PLACEHOLDER_STORY_19: File watcher for --watch mode` — Story 19 language server / file watching
- `// PLACEHOLDER_STORY_19: Incremental binding based on dependency graph` — Story 19 incremental binding

### Configuration Precedence

Configuration values are resolved in this order (later overrides earlier):

1. Built-in defaults (in config class constructors)
2. `tyhp.json` file values
3. Environment variables (if supported — future enhancement)
4. CLI arguments (`--clean`, `--verbose`, `--dry-run`, `--strict`, `--format`, `--file`)

### Thread Safety Considerations

- `CompilationService.ParseFiles()` is multi-threaded — all shared state must use concurrent collections (already established in Story 01)
- `TyhpBinder.Bind()` is single-threaded per the Story 02 design (walks files sequentially)
- `TyhpChecker.Check()` is single-threaded per the Story 08 design
- `TyhpEmitter.Emit()` could potentially be parallelized per file, but the initial implementation should be single-threaded for simplicity
- `OutputWriterService.WriteAll()` should be single-threaded to avoid filesystem race conditions
- The `DiagnosticBag` is thread-safe (from Story 01) and can be shared across all phases

### File Size Guidelines

| File | Target Maximum | Notes |
|------|---------------|-------|
| `BuildAction.cs` | 400 lines | Split into helper methods or partial classes if larger |
| `OutputWriterService.cs` | 250 lines | Focused on file writing mechanics |
| `ComposerJsonService.cs` | 150 lines | JSON manipulation only |
| `IncrementalBuildService.cs` | 200 lines | File change detection + state persistence |
| `TyhpLibDistributionService.cs` | 150 lines | Tyhp runtime Composer package dependency management |
| `Project.cs` | 300 lines | Should not grow excessively; config sections are in separate files |
| Each config section class | 50-120 lines | Focused on one config area |

### Complete File Inventory

**New files:**
```
Tyhp/Config/OutputConfig.cs         (~80 lines)
Tyhp/Config/BuildConfig.cs          (~120 lines)
Tyhp/Config/CheckerConfig.cs        (~50 lines)
Tyhp/Config/TyhpdefConfig.cs        (~50 lines)
Tyhp/Domain/Services/OutputWriterService.cs     (~250 lines)
Tyhp/Domain/Services/ComposerJsonService.cs     (~150 lines)
Tyhp/Domain/Services/IncrementalBuildService.cs  (~200 lines)
Tyhp/Domain/Services/TyhpLibDistributionService.cs (~150 lines)
```

**Modified files:**
```
Tyhp/CLI/BuildAction.cs             (replace Story 01 skeleton)
Tyhp/CLI/TyhpHostedService.cs       (wire BuildAction with Project config)
Tyhp/CLI/ActionRunnerBase.cs        (verify Result property — may be no-op if Story 01 already handled)
Tyhp/Config/Project.cs              (add new config section properties)
Tyhp/Domain/Exceptions/MessageCode.cs (add 6000s and 7000s codes)
Tyhp/Domain/Services/CompilationService.cs (minor: cache stats, enhanced reporting)
Tyhp/Resources/CLI.TyhpHostedService.resx (add resource strings for new codes)
```

---

*Generated: 2026-02-16 | Source: TODO.md Story 10 | Branch: Not Specified*

---

## Human Testing and Verification

> **Note:** These steps are meant to help a human developer manually verify the full build pipeline wiring from Story 10. Steps can be skipped, reordered, or adapted based on what has already been tested. This story is an integration story — the focus is on verifying that all pipeline phases connect correctly and the `tyhp build` command works end-to-end.

### Step 1: Verify the Build Compiles

Run the project build to confirm all Story 10 code compiles without errors:

```bash
dotnet build
```

Confirm there are no build errors in `Tyhp/CLI/BuildAction.cs`, `Tyhp/Config/`, or `Tyhp/Domain/Services/`.

### Step 2: Verify Configuration Parsing

Create a `tyhp.json` with all new configuration options to verify parsing:

```json
{
    "type": "application",
    "include": ["**/*.tyhp"],
    "output": {
        "path": "build/",
        "namespacePrefix": null,
        "comments": true,
        "phpVersion": "8.4",
        "strictTypes": true
    },
    "build": {
        "profile": "balanced",
        "optimize": "basic",
        "generateSourcemap": false,
        "updateComposer": true,
        "structBacking": "array",
        "decimalBacking": "bcmath",
        "decimalScale": 28,
        "verbose": false
    }
}
```

Run:

```bash
tyhp build --verbose
```

**Expected:** The verbose output should show the parsed configuration values. No warnings about unknown or invalid configuration keys.

### Step 3: Verify CLI Argument Overrides

Test that CLI arguments correctly override `tyhp.json` values:

```bash
tyhp build --clean --verbose --dry-run --strict
```

**Expected:**

- `--clean`: Output directory is cleaned before build (verbose output confirms cleaning)
- `--verbose`: Detailed logging at each phase
- `--dry-run`: No files are written; summary says "0 (dry run)" for files written
- `--strict`: Warnings are treated as errors (build fails if there are warnings)

### Step 4: Verify End-to-End Build Pipeline

Create a simple project structure:

```
test-project/
├── tyhp.json
└── src/
    └── App/
        ├── Models/
        │   └── User.tyhp
        └── index.tyhp
```

`tyhp.json`:

```json
{
    "type": "application",
    "include": ["src/**/*.tyhp"],
    "output": {
        "path": "build/"
    }
}
```

`src/App/Models/User.tyhp`:

```tyhp
<?tyhp

namespace App\Models;

class User {
    public function __construct(
        private string $name,
        private string $email
    ) {}

    public function getName(): string {
        return $this->name;
    }

    public function getEmail(): string {
        return $this->email;
    }
}
```

`src/App/index.tyhp`:

```tyhp
<?tyhp

use App\Models\User;

User $user = new User("Alice", "alice@example.com");
echo "User: " . $user->getName() . " <" . $user->getEmail() . ">\n";
```

Run:

```bash
cd test-project
tyhp build
```

**Expected:**

1. Build completes successfully
2. Output files are created under `build/`:
   - `build/src/Models/User.php` (PSR-4 class file)
   - `build/src/index.php` (entry point file)
3. Each output file starts with `<?php` and has `declare(strict_types=1);`
4. Each output file passes `php -l` syntax check
5. Running `php build/src/index.php` (after setting up autoloading) prints the expected output

### Step 5: Verify Build Summary Output

After running a build, verify the summary matches the expected format:

```bash
tyhp build
```

**Expected output format:**

```
Build completed successfully.

  Files:     2 source files → 2 PHP files
  Duration:  0.15s (parse: 0.05s, bind: 0.02s, check: 0.04s, emit: 0.04s)
  Warnings:  0
  
Output written to: ./build/
```

Verify:
- File counts are accurate
- Per-phase timing is shown
- Output directory path is displayed
- If optimizer ran, optimizer timing and transformation count are shown

### Step 6: Verify Error Reporting and Error Gate

Create a test file with intentional type errors:

```tyhp
<?tyhp

namespace App;

function broken(): void {
    int $x = "not an int";  // type error
    $y->nonExistentMethod();  // unresolved symbol
}
```

Run:

```bash
tyhp build
```

**Expected:**

- Build fails with error diagnostics
- Error messages include file path, line number, column, and `TYHP` code
- Summary shows error count
- Exit code is non-zero (`CompileError`)
- No output files are written (error gate prevents emission)

### Step 7: Verify Strict Mode

Create a file that produces warnings but no errors, then test with `--strict`:

```bash
tyhp build --strict
```

**Expected:** If there are any warnings, the build fails with exit code `CompileWarning` and no output files are written.

### Step 8: Verify Dry Run Mode

```bash
tyhp build --dry-run
```

**Expected:**

- The full pipeline runs (parse, bind, check, emit)
- The summary reports what files would be written and their sizes
- No files are actually created or modified in the output directory
- Verify with `ls build/` (should be empty or unchanged)

### Step 9: Verify Clean Build

```bash
# First, do a normal build
tyhp build

# Verify output files exist
ls build/

# Now do a clean build
tyhp build --clean

# Verify old output was cleaned and new output exists
ls build/
```

**Expected:**

- `--clean` deletes existing `.php` and `.php.map` files before building
- Non-PHP files in the output directory (e.g., `.gitkeep`) are preserved
- The clean does NOT delete the output directory itself
- Safety checks prevent cleaning dangerous paths (project root, system directories)

### Step 10: Verify PSR-4 Path Mapping

Create a `tyhp.json` with PSR-4 mapping:

```json
{
    "include": ["src/**/*.tyhp"],
    "output": {
        "path": "build/"
    },
    "psr4": {
        "App\\": "src/"
    }
}
```

Create classes in `App\Models` and `App\Services` namespaces. Run `tyhp build`.

**Expected:**

- `App\Models\User` → `build/src/Models/User.php`
- `App\Services\AuthService` → `build/src/Services/AuthService.php`
- Namespace functions → `build/src/Helpers/_functions.php` (if in `App\Helpers`)

### Step 11: Verify Composer JSON Generation

With `build.updateComposer` set to `true`:

```json
{
    "build": {
        "updateComposer": true
    }
}
```

Run:

```bash
tyhp build
```

**Expected:**

- A `composer.json` is created (or updated) in the output directory
- It contains `autoload.psr-4` entries matching the compiled namespace structure
- If namespace-level functions exist, their `_functions.php` paths are in `autoload.files`
- Required Tyhp runtime packages are in `require` based on feature usage
- The generated JSON is valid: `php -r "json_decode(file_get_contents('build/composer.json'), true);"`

### Step 12: Verify Library Project Type

Create a project with `"type": "library"`:

```json
{
    "type": "library",
    "include": ["src/**/*.tyhp"]
}
```

Create a source file with top-level executable code (entry point):

```tyhp
<?tyhp
echo "I am an entrypoint!";
```

Run:

```bash
tyhp build
```

**Expected:**

- Build fails with an error: library projects must not have entry points
- This error occurs regardless of `build.generateTyhpdef` setting

Now remove the entry point and verify a library with only class declarations builds successfully.

### Step 13: Verify Incremental Build

Run two consecutive builds:

```bash
tyhp build --verbose
# Note the output, then immediately:
tyhp build --verbose
```

**Expected:**

- Second build should be faster (cached ASTs reused)
- Verbose output shows cache statistics (e.g., "Parsed 0/5 files (5 from cache)")
- If no files changed, build may exit early with "Nothing to build"

Now modify one source file and rebuild:

```bash
# Edit a .tyhp file
tyhp build --verbose
```

**Expected:** Only the changed file is re-parsed; other files use the cache. All files are re-bound and re-checked (conservative strategy).

### Step 14: Verify Empty Project Handling

Create an empty project (no source files matching include patterns):

```json
{
    "include": ["src/**/*.tyhp"]
}
```

But don't create any `.tyhp` files in `src/`.

```bash
tyhp build
```

**Expected:** Build completes with an informational message like "No source files found matching include patterns." Exit code should be `Success` (0), not an error.

### Step 15: Verify Missing tyhp.json Handling

Run `tyhp build` in a directory without a `tyhp.json`:

```bash
mkdir /tmp/no-config-test
cd /tmp/no-config-test
echo '<?tyhp\necho "hello";' > test.tyhp
tyhp build
```

**Expected:** Build uses default configuration values and displays an informational message: "No tyhp.json found, using defaults."

### Step 16: Verify Configuration Error Reporting

Create a `tyhp.json` with invalid values:

```json
{
    "output": {
        "phpVersion": "5.6"
    },
    "type": "invalid_type"
}
```

Run:

```bash
tyhp build
```

**Expected:**

- `ConfigInvalidPhpVersion` warning/error for unsupported PHP version
- `ConfigInvalidProjectType` warning/error for unrecognized type
- Error messages include the `TYHP6xxx` code and describe the problem clearly

### Step 17: Verify Exit Codes

Test each exit code scenario:

```bash
# Successful build
tyhp build
echo $?  # Expected: 0 (Success)

# Build with type errors
tyhp build  # on project with errors
echo $?  # Expected: 4 (CompileError)

# Build with warnings in strict mode
tyhp build --strict  # on project with warnings
echo $?  # Expected: 5 (CompileWarning)
```

### Step 18: Verify the Full Pipeline Output Is Runnable

Create a complete small project and verify the output actually works:

`src/hello.tyhp`:

```tyhp
<?tyhp

function greet(string $name): string {
    return "Hello, " . $name . "!";
}

echo greet("World") . "\n";
echo greet("Tyhp") . "\n";
```

Run:

```bash
tyhp build
php build/src/hello.php
```

**Expected output:**

```
Hello, World!
Hello, Tyhp!
```

This confirms the entire pipeline — parsing, binding, checking, emitting, and file writing — produces correct, runnable PHP.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
