# Implementation Plan: Story 20 — Tyhpdef Generator (C# CLI Integration)

> **Roadmap position:** Story 20 — **Tier 2 — DX & Ecosystem**
> **Direct dependencies (new numbering):** 01, 02
> **Renumbered from:** legacy Story 10
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 20 of the Tyhp compiler TODO
> **Branch:** TBD
> **Prerequisites:** Story 01 (diagnostic system, CompilationService, BuildAction skeleton), Story 02 (binder — understanding of type system and symbol model)
> **Forward dependency:** Story 20 **Phase 8** (multi-target gated generation) requires **Story 20.5**
> **Key files:** `Tyhp/CLI/GenerateTyhpdefAction.cs`, `DebugProject/genTyhpdef.php`

---

## Architecture Overview

### Purpose of the Tyhpdef Generator

Tyhpdef files (`.tyhpdef`) are type definition files that describe the type signatures of external PHP code — extensions, Composer packages, and user-authored PHP libraries. They serve the same role as TypeScript's `.d.ts` files or C/C++ header files: allowing the Tyhp compiler to type-check code that references external PHP without having access to the actual PHP implementation.

Story 20 implements the `generate_tyhpdef` CLI action, which produces `.tyhpdef` files from three sources:

1. **PHP extensions** — introspecting installed PHP extensions via PHP's Reflection API (or a C# reimplementation)
2. **Composer packages** — introspecting autoloaded classes from Composer-managed PHP packages
3. **Compiled Tyhp code** — extracting the public API from bound/checked Tyhp ASTs to allow other projects to import compiled Tyhp libraries. For projects with `"type": "library"` in their `tyhp.json`, the build automatically generates a `package.tyhp.json` file that is distributed with the Composer package, enabling other Tyhp projects to consume the library's types without access to the original Tyhp source code.

### Current State

| Component | Location | Status |
|-----------|----------|--------|
| CLI action class | `Tyhp/CLI/GenerateTyhpdefAction.cs` | Validates `--ext-name` argument; all logic is commented out or stubbed |
| CLI routing | `Tyhp/CLI/TyhpHostedService.cs` | Routes `generate_tyhpdef` action to `GenerateTyhpdefAction`, already wired |
| Config getter | `Tyhp/Config/Project.cs` | `GetExtName()` method returns `--ext-name` CLI argument |
| Bundled extension tyhpdefs | `runtime/php-extensions/php8.2.9/` | 16+ extension tyhpdef files — **hand-enriched** over Reflection output (generics, overloads, type guards, language constructs). Live input for the compiler today. |
| Tyhp overlay backup snapshot | `runtime/php-extensions/overlays/` | Full-file copies of existing hand edits for recovery. Not loaded. Story 21 load-time overlays supersede in-place merge. |
| PHP generator scripts | `tools/genTyhpdef.php`, `DebugProject/genTyhpdef.php` | Functional Reflection-based generators (reference for Track A); diverge slightly (docs on/off, const syntax). To be replaced by C# orchestration. |
| Generated Composer tyhpdefs | `DebugProject/tyhpdef_gen/…` | Historical per-package dumps from the PHP script |
| Tyhpdef parser/visitor | `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.Tyhpdef.cs` | ~900 lines, functional — can parse `.tyhpdef` files |
| Binder tyhpdef loader | `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs` + `Tyhpdef.PackageLoading.cs` | Package discovery via `package.tyhp.json` (Story 06) |
| Stub corpora (enrichment input) | Psalm / PHPStan / Phan / PhpStorm (URLs in `runtime/README.md`) | Read-only harvest sources for PHPDoc/`@template`/stronger types — **not** checked in as Tyhp source of truth |

### Strategy: Two-Track Approach (PHP Delegation + C# Native)

The TODO.md presents two options: **Option A** (invoke the PHP script from C#) and **Option B** (reimplement in C#). This plan implements both as complementary tracks:

- **Track A (PHP Delegation via `php -r`):** The fastest path to a working `tyhp generate_tyhpdef` command. The C# action makes targeted `php -r` calls to invoke PHP's Reflection API, captures structured JSON output, and processes it in C# to generate tyhpdef syntax. No PHP scripts are embedded or bundled — all logic lives in C#, with PHP used only as a reflection data source.

- **Track B (C# Native):** A pure C# implementation that parses PHP source files using the existing ANTLR PHP parser/visitor, walks the resulting ASTs, extracts type information from PHP type hints and PHPDoc annotations, and generates `.tyhpdef` syntax. This eliminates the PHP runtime dependency for tyhpdef generation.

- **Track C (From Tyhp Code):** When `build.generateTyhpdef` config is true, extract public API declarations from the bound, checked Tyhp AST and emit type-definition output: for `library` projects, a single `package.tyhp.json` manifest in the build output directory (the Composer entry point for discovery); for `application` projects with generation enabled, `.tyhpdef` files may mirror the namespace structure. This runs after the checker but **before** the optimizer (Story 23) to capture the unoptimized public API.

Track A is implemented first (Phases 1-2) because it provides immediate value with minimal effort. Track B (Phases 3-5) is the more robust long-term solution. Track C (Phase 6) depends on Story 02 (binder) being substantially complete.

**Critical ordering requirement (Story 23):** When the optimizer (Story 23) is active, `package.tyhp.json` generation (Track C) must run **before** the optimizer transforms the AST. The `package.tyhp.json` must reflect the original, unoptimized public API — including all extension methods, operators, and class members — regardless of what the optimizer inlines or eliminates internally. The build pipeline (Story 10) enforces this ordering: `checker → tyhpdef generation → optimizer → emitter`.

### Pipeline Position

```
User runs: tyhp generate_tyhpdef --ext-name=curl
    │
    ▼
TyhpHostedService dispatches to GenerateTyhpdefAction
    │
    ├──► Track A: C# orchestrator with `php -r` calls (if PHP is available)
    │       │
    │       ▼
    │    php -r returns JSON → C# processes → .tyhpdef file
    │
    ├──► Track B: C# native (if PHP is not available, or --source mode)
    │       │
    │       ▼
    │    Parse PHP files with ANTLR → Walk AST → Extract types → .tyhpdef file
    │
    └──► Track C: From Tyhp code (during build, if configured)
            │
            ▼
         Parse + Bind Tyhp ASTs → Extract public API → package.tyhp.json (library) or .tyhpdef files (application)
```

### Key Design Decisions

1. **Track A is the default when PHP is available.** The C# orchestrator makes small `php -r` calls to invoke PHP's Reflection API, capturing JSON output. PHP's Reflection API provides the most accurate type information for extensions and Composer packages because it introspects the actual loaded code at runtime. All logic for processing reflection data and generating tyhpdef syntax is in C#.

2. **Track B is the fallback and the default for `--source` mode.** When users want to generate tyhpdefs from PHP source files without running PHP (e.g., in a CI environment without the target PHP extensions installed), the C# native implementation parses the source files directly.

3. **Tyhpdef output format is the same regardless of track.** All tracks produce valid `.tyhpdef` syntax that can be parsed by `TyhpParserAstVisitor.Tyhpdef.cs`.

4. **PHPDoc is the primary source of type information in Track B.** PHP source files often have richer type information in PHPDoc comments (`@param`, `@return`, `@var`, `@throws`, `@template`) than in PHP type hints alone. The C# native implementation must parse and use PHPDoc annotations.

5. **Track C (Tyhp code generation) produces output after the binder pass.** It needs bound symbols with fully resolved types, which means it depends on Story 02's binder being functional.

6. **Library projects auto-generate `package.tyhp.json` and `_tyhpdef/`.** When `tyhp.json` has `"type": "library"`, the build automatically generates `.tyhpdef` files (one per type, using dot-notation filenames) in a `_tyhpdef/` folder and a `package.tyhp.json` manifest in the build output directory. The manifest contains an `include` array of globs pointing to the generated files. Supporting `.tyhp` files (e.g., auto-generated extension classes) are placed in `_tyhpdef/support/`. Application projects can also opt-in by setting `build.generateTyhpdef = true`. Library projects must NOT contain entrypoint files (root-level statements with side effects).

7. **Tyhpdef output must faithfully represent extension methods and extension operators.** When a Tyhp library defines extension methods or extension operators (Story 03), the generated `.tyhpdef` files must include these as tyhpdef inline `extension function` and `extension operator` declarations within the relevant class body. The delegation bodies point to auto-generated extension class `.tyhp` files in `_tyhpdef/support/`. This allows consuming projects to use the same operator overloads and extension methods that the library's Tyhp source code defined.

8. **Runtime libraries (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`) use `package.tyhp.json` as their type definition source.** These packages are written in Tyhp and compiled to PHP. Their auto-generated `package.tyhp.json` manifests (pointing to `_tyhpdef/` contents) serve as the authoritative type definitions that the compiler loads when user code depends on these packages. (Note: the older manually-authored `TyhpSpec` files were removed in Story 04; runtime libraries now rely entirely on these generated `package.tyhp.json` manifests. Fundamental built-in type aliases such as `type decimal = __TyhpInternal<float>` are handled by Story 06's built-in registration, not TyhpSpec.)

### Three-Layer Generation Model (design lock)

Reflection alone under-types Core/Standard. Community stubs close much of that gap. Tyhp-specific truth (operators, language constructs, advanced generics) must not live only in git history. Generation is therefore **three layers**:

| Layer | Source | Owns |
|-------|--------|------|
| **1. Baseline (mechanical)** | PHP Reflection per target (`8.2`–`8.5`) via Track A `php -r` → JSON | Names, signatures, defaults, by-ref/variadic, unions/intersections, enums, attributes, deprecation / tentative returns |
| **2. Enrichment (imported)** | Psalm / PHPStan / Phan / PhpStorm stubs (`runtime/README.md`) | Docblock types, `@template`, better `array`/`callable`, some overloads — harvested into **generated overlay** files `_tyhpdef/overlays/stubs/` (native tyhpdef + attribution). Listed **first** in `"overlay"`. |
| **3. Tyhp overlays (hand-owned)** | Package `_tyhpdef/overlays/*.tyhpdef` (not `stubs/`). `runtime/php-extensions/overlays/` is a **backup snapshot only** | Real generics, operator overloads, `exit`/`die`/`clone`, Tyhp-only constructs, anything stubs miss. Listed **last** in `"overlay"`. Never written into baseline. |

```text
For each PHP target (8.2, 8.3, 8.4, 8.5):
  phpX.Y -r ReflectExtension → JSON snapshot
       ↓
  Merge snapshots → gated IR   (Phase 8; requires Story 20.5 for emit)
       ↓
  TyhpdefOutputWriter → Layer 1 baseline (_tyhpdef/*.tyhpdef)
       ↓
  Layer 2 stub harvest → _tyhpdef/overlays/stubs/*.tyhpdef
       ↓
  (compile time) apply package.tyhp.json "overlay" in array order (stubs first, hand last; last wins)
```

**Short term:** emit / compare under `runtime/php-extensions/php{ver}/` or staging `tyhpdef_gen/`.  
**Long term (Story 21 + Phase 8):** one package; version diffs via `declare(php=…)` / `#[\Tyhp\Php]` instead of per-minor forks.

#### What Reflection can and cannot provide

**Can:** constants (value→type), functions/methods (params, defaults, by-ref, variadic, return types including union/intersection/nullable), classes/interfaces/traits/enums, attributes, deprecation flags, tentative return types, basic type-guard remaps for well-known `is_*`.

**Cannot (or only poorly):** generics / templates; arity overloads; operators / extension methods; precise array/callable shapes; language constructs (`exit`/`die`/`clone`); cross-version API surface without multi-target reflect + merge.

#### Stub harvest + credit (Layer 2)

Treat the four stub trees as **read-only inputs** (vendored or fetched at generate time):

1. Match symbols by FQN.
2. Prefer stub param/return/`@template` when Reflection is weak (`array`, `mixed`, `callable`, untyped).
3. When corpora disagree, prefer consensus of Psalm + PHPStan; use PhpStorm for coverage gaps; Phan as additional signal.
4. Emit a per-file header crediting sources, plus a package `SOURCES.md` / `NOTICE` listing URLs and licenses.
5. **Do not** copy stub files wholesale into the repo as Tyhp source of truth — harvest into tyhpdef IR and emit `_tyhpdef/overlays/stubs/*.tyhpdef` (Layer 2 overlay files, listed first in `"overlay"`).

#### Overlay preservation + regen safety (Layer 3)

Hand enrichment already exists in `php8.2.9/Ext*.tyhpdef` (generics, overloads, type guards, language constructs, Decimal operators). A naïve regen of **baseline** files will wipe in-place edits.

**Story 21 lock:** overlays are **separate tyhpdef files** loaded after baseline (`package.tyhp.json` `"overlay"` **in array order; last wins**). Stub harvest files (`_tyhpdef/overlays/stubs/`) are listed first and may be regenerated. Hand-written files (`_tyhpdef/overlays/*.tyhpdef`, non-recursive) are listed last and must **never** be overwritten by regen. Match by **Tyhp name**. Full declaration replaces; `partial` merges members on class/enum/interface/trait; `omit` hides a symbol. Optional `// @overlay-against:` stamps the compact Layer 1 signature. Regenerators overwrite Layer 1 baseline and may overwrite `overlays/stubs/`; they **must not** touch hand-written `_tyhpdef/overlays/*.tyhpdef` or `runtime/php-extensions/overlays/`.

`runtime/php-extensions/overlays/` is a **full-file backup** of hand edits that already existed before load-time overlays shipped. It is not loaded. After extracting those edits into package overlay files, keep the snapshot for recovery.

Until Story 21 overlay load is wired, the compiler continues to load `runtime/php-extensions/php8.2.9/` (live tree). Do not implement in-place `@tyhp-overlay` / `@generated-original` merge as the long-term mechanism — that was superseded.

Language constructs (`exit`/`die`/`clone`) and PECL Decimal operators are overlay-owned content (`tyhp/php` overlays and `tyhp/php-ext-decimal` overlays respectively — PECL Decimal is not `tyhp/decimal`).

#### Snapshots as first-class artifacts

Per-target Reflection JSON under `tyhpdef_gen/snapshots/{phpVersion}/` (checked in or CI-cached) so regen does not require four PHP installs on every laptop. Multi-target merge diffs snapshots, not live processes only.

#### Skeleton implementation order (do not skip)

1. Phase 5 stub — `TyhpdefDeclaration` IR + naive writer (round-trip one small Ext).
2. Track A skeleton — C# `php -r` → JSON → IR → `.tyhpdef` for one extension (`json` or `filter`).
3. Multi-target harness — `--php-targets=8.2,8.3,8.4,8.5` + binary map; write snapshots.
4. Diff/merge prototype (comment-annotated or gated once Story 20.5 lands).
5. Stub enricher (PhpStorm first for coverage; then Psalm/PHPStan).
6. Story 21 overlay load (array order, last wins; stubs first, hand last).
7. Replace `tools/genTyhpdef.php` as the primary path (keep as reference until parity).

#### Practical locks

1. Dogfood **one** extension end-to-end before Core/Standard.
2. Attribution is mandatory in generated headers + `SOURCES.md`.
3. Do not block Layer 1–2 scaffolding on Story 21 package rename — migrate paths later.
4. Phase 8 gate **emit** waits on Story 20.5; snapshot + merge IR can land earlier.

#### Risks

- Regenerating Core/Standard without overlays → checker regressions.
- Stub formats differ (normalizer required).
- CI needs multiple PHP binaries/containers; missing target must fail hard.
- Two legacy PHP generators (`tools/` vs `DebugProject/`) diverge — C# path must not invent a third format.

### Dependency Map

```
Phase 1: GenerateTyhpdefAction Expansion (CLI infrastructure)
    │
    ├──► Phase 2: PHP Delegation (Track A)
    │
    ├──► Phase 3: PHPDoc Parser (needed by Track B)
    │       │
    │       └──► Phase 4: C# Native PHP Source → Tyhpdef (Track B)
    │
    ├──► Phase 5: Tyhpdef Output Writer (shared by Tracks B and C)
    │
    └──► Phase 6: Tyhpdef from Tyhp Code (Track C)
             │
             └──► Phase 7: Validation, Integration, and End-to-End Testing
                      │
                      └──► Phase 8: Multi-Target PHP Version Generation (gated output) — **requires Story 20.5**
```

> **Phase-ordering note (forward dependency):** The diagram lists Phase 5 (the shared `TyhpdefOutputWriter` + `TyhpdefDeclaration` IR) as a sibling, but Tracks A (Phase 2), B (Phase 4), and C (Phase 6) all *consume* it — they construct `TyhpdefDeclaration` objects and serialize them via `TyhpdefOutputWriter`. **Phase 5 must be implemented before Phases 2, 4, and 6** (or the consuming phases must stub the writer/IR until Phase 5 lands). Treat the effective build order as: Phase 1 → Phase 5 (shared IR/writer) → Phase 3 (PHPDoc parser) → Phases 2/4/6 (tracks) → Phase 7 → **Phase 8 (after Story 20.5)**.

### MessageCode Numbering

Tyhpdef *generation* (the `generate_tyhpdef` CLI action) uses the CLI `generate_tyhpdef` range **7500–7599** (per `MessageCode.cs`'s documented CLI subdivision). The 8000s range is reserved for tyhpdef *parse/bind* diagnostics only — generation errors must NOT use it.

| Code | Name | Description |
|------|------|-------------|
| 8001 | `TyhpdefParseError` | (parse/bind) A tyhpdef file failed to parse |
| 8002 | `TyhpdefDuplicateDeclaration` | (parse/bind) A tyhpdef declares a symbol that already exists |
| 8003 | `TyhpdefFileNotFound` | (parse/bind) A configured tyhpdef path does not exist |
| 8004 | `TyhpdefInvalidFormat` | (parse/bind) A tyhpdef file has an unexpected structure |
| 7500 | `TyhpdefGenerationError` | Error during tyhpdef generation |
| 7501 | `TyhpdefPhpNotFound` | PHP runtime not found (Track A) |
| 7502 | `TyhpdefSourceParseError` | PHP source file failed to parse during Track B |
| 7503 | `TyhpdefOutputWriteError` | Failed to write tyhpdef output file |
| 7504 | `TyhpdefPhpDocParseError` | Failed to parse PHPDoc comment block |
| 7505 | `TyhpdefLibraryEntrypointDetected` | Library project contains entrypoint file(s) with root-level side-effect statements |
| 8025 | `TyhpdefDuplicateFqnAcrossPackages` | (parse/bind) Two different packages define the same fully-qualified type name. **Defined by Story 06** (parse/bind range); Story 20 only *references* it. |

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup: `<filename>.bak.<timestamp>` (e.g. `<filename>.bak.20260612153000`)
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: GenerateTyhpdefAction CLI Infrastructure




### Phase Overview

Expand the `GenerateTyhpdefAction` class from its current minimal state (validates `--ext-name` only) into a proper CLI action with multiple operation modes, configuration parsing, output path management, and diagnostic reporting. This phase establishes the CLI infrastructure that both Track A and Track B plug into.

### Deliverables

- `Tyhp/CLI/GenerateTyhpdefAction.cs` — Expanded with mode detection, argument parsing, and pipeline orchestration
- `Tyhp/Domain/Services/TyhpdefGenerationOptions.cs` — Options record for tyhpdef generation
- `Tyhp/Domain/Services/TyhpdefGenerationResult.cs` — Result type for tyhpdef generation
- Modified `Tyhp/Config/Project.cs` — New configuration getters for tyhpdef generation CLI arguments
- Modified `Tyhp/Domain/Exceptions/MessageCode.cs` — New tyhpdef generation message codes (7500-7505, in the `generate_tyhpdef` CLI range)

### Implementation Details

**1.1 — Define `TyhpdefGenerationOptions`**

New file: `Tyhp/Domain/Services/TyhpdefGenerationOptions.cs`

Namespace `Tyhp.Domain.Services`:

- `TyhpdefGenerationMode Mode { get; init; }` — enum: `PhpExtension`, `ComposerPackage`, `PhpSourceFiles`, `TyhpCode`
- `string? ExtensionName { get; init; }` — PHP extension name (for `PhpExtension` mode)
- `string? PackagePath { get; init; }` — path to Composer package directory (for `ComposerPackage` mode)
- `List<string> SourcePaths { get; init; }` — glob patterns for PHP source files (for `PhpSourceFiles` mode). Reuse the existing glob matching infrastructure from `Project.GetProjectSourceFiles()` for pattern resolution.
- `string OutputDirectory { get; init; }` — where to write generated `.tyhpdef` files
- `string? OutputFileName { get; init; }` — explicit output file name (if single-file output)
- `bool PreferPhpRuntime { get; init; }` — prefer PHP delegation (Track A) when available (default: true)
- `string? PhpExecutablePath { get; init; }` — explicit path to PHP binary (default: search PATH)
- `string? PhpVersion { get; init; }` — target PHP version string for output metadata
- `string Locale { get; init; }` — locale for documentation language (default: "en")
- `bool IncludeDocComments { get; init; }` — include PHPDoc in output (default: true)
- `bool IncludeDeprecated { get; init; }` — mark deprecated items (default: true)
- `bool IncludeInternal { get; init; }` — include `@internal` items (default: false)
- `bool Overwrite { get; init; }` — overwrite existing tyhpdef files (default: false)

**1.2 — Define `TyhpdefGenerationMode` Enum**

Add to `TyhpdefGenerationOptions.cs` or a separate file:

- `PhpExtension` — generate from a PHP extension using Reflection API
- `ComposerPackage` — generate from a Composer package's autoloaded classes
- `PhpSourceFiles` — generate from PHP source files (C# native parsing)
- `TyhpCode` — generate from compiled Tyhp code's public API

**1.3 — Define `TyhpdefGenerationResult`**

New file: `Tyhp/Domain/Services/TyhpdefGenerationResult.cs`

Namespace `Tyhp.Domain.Services`:

- `DiagnosticBag Diagnostics { get; }` — generation diagnostics
- `List<string> GeneratedFiles { get; }` — paths to generated tyhpdef files
- `int TotalDeclarations { get; set; }` — count of declarations generated
- `int ClassCount { get; set; }` — classes/interfaces/traits/enums generated
- `int FunctionCount { get; set; }` — functions generated
- `int ConstantCount { get; set; }` — constants generated
- `TimeSpan Duration { get; set; }` — how long generation took
- `bool Success => !Diagnostics.HasErrors` — convenience

**1.4 — Expand `GenerateTyhpdefAction`**

File: `Tyhp/CLI/GenerateTyhpdefAction.cs`

Replace the current minimal implementation. The action should:

1. Parse CLI arguments to determine the generation mode:
   - `--ext-name=curl` → `PhpExtension` mode
   - `--package-path=./vendor/guzzlehttp/guzzle` → `ComposerPackage` mode
   - `--source=./src/**/*.php` → `PhpSourceFiles` mode (Track B)
   - (Track C is triggered from `BuildAction`, not directly)
2. Parse common options:
   - `--output=./tyhpdef/` → output directory
   - `--output-file=ExtCurl.tyhpdef` → explicit file name
   - `--php=/usr/bin/php` → PHP executable path
   - `--php-version=8.3` → target PHP version
   - `--locale=en` → documentation language
   - `--no-docs` → skip doc comments
   - `--include-internal` → include `@internal` items
   - `--overwrite` → overwrite existing files
   - `--no-php` → force C# native mode (skip PHP delegation)
3. Create `TyhpdefGenerationOptions` from parsed arguments
4. Determine which track to use:
   - If mode is `PhpExtension` or `ComposerPackage` and `PreferPhpRuntime` is true → check if PHP is available → if yes, use Track A; otherwise fall back to Track B
   - If mode is `PhpSourceFiles` → always use Track B
   - If `--no-php` is specified → always use Track B
5. Dispatch to the appropriate generator — add these placeholder markers in the dispatch logic for subsequent phases to replace:
   - `// PLACEHOLDER_PHASE_2: Track A — PHP Delegation`
   - `// PLACEHOLDER_PHASE_4: Track B — C# Native`
6. Report results:
   - Display generated file paths
   - Display declaration counts
   - Display timing
   - Display any diagnostics (warnings about missing types, deprecated items, etc.)
   - Set exit code based on result

**1.5 — Expand `Project.cs` Configuration Getters**

File: `Tyhp/Config/Project.cs`

Add methods to read the new CLI arguments:

- `GetTyhpdefOutputDir()` — reads `--output` argument
- `GetTyhpdefOutputFile()` — reads `--output-file` argument
- `GetPhpExecutablePath()` — reads `--php` argument
- `GetTyhpdefPhpVersion()` — reads `--php-version` argument
- `GetTyhpdefLocale()` — reads `--locale` argument (defaults to `this.Locale`)
- `GetTyhpdefSourcePaths()` — reads `--source` argument(s)
- `GetTyhpdefPackagePath()` — reads `--package-path` argument
- `GetTyhpdefNoDocs()` — reads `--no-docs` flag
- `GetTyhpdefIncludeInternal()` — reads `--include-internal` flag
- `GetTyhpdefOverwrite()` — reads `--overwrite` flag
- `GetTyhpdefNoPhp()` — reads `--no-php` flag

These all follow the same pattern as the existing `GetExtName()` method: reading from `this._configuration["key"]`.

**1.6 — Add Tyhpdef Generation MessageCodes**

File: `Tyhp/Domain/Exceptions/MessageCode.cs`

Add the generation codes inside the existing `#region CLI — generate_tyhpdef action (7500–7599)` region (the placeholder comment there is awaiting Story 20):

- `TyhpdefGenerationError = 7500`
- `TyhpdefPhpNotFound = 7501`
- `TyhpdefSourceParseError = 7502`
- `TyhpdefOutputWriteError = 7503`
- `TyhpdefPhpDocParseError = 7504`
- `TyhpdefLibraryEntrypointDetected = 7505`

These live in the `generate_tyhpdef` CLI range (7500–7599) because tyhpdef *generation* is a CLI action. The 8000s range is reserved for tyhpdef *parse/bind* diagnostics and must not be used here. `TyhpdefDuplicateFqnAcrossPackages = 8025` is a parse/bind diagnostic owned by Story 06 — do NOT redefine it; reference it.

### Acceptance Criteria

- [ ] `GenerateTyhpdefAction` parses all new CLI arguments without crashing
- [ ] Running `tyhp generate_tyhpdef --ext-name=curl` reports "PHP delegation not yet implemented" (placeholder)
- [ ] Running `tyhp generate_tyhpdef --source=./src/*.php` reports "C# native generation not yet implemented" (placeholder)
- [ ] Running `tyhp generate_tyhpdef` with no arguments displays a helpful error message listing required arguments
- [ ] `TyhpdefGenerationOptions` correctly captures all parsed arguments
- [ ] `TyhpdefGenerationResult` can hold generation statistics and diagnostics
- [ ] New `MessageCode` values are added in the `generate_tyhpdef` CLI range (7500-7505)
- [ ] All new and modified files compile without errors
- [ ] No regressions in existing `GenerateTyhpdefAction` functionality (the `--ext-name` validation still works)

### Dependencies

- **Requires:** Story 01 (diagnostic system with `DiagnosticBag`, `MessageCode`) — for error reporting
- **Provides:** CLI infrastructure for Phases 2-7

---

## Phase 2: PHP Delegation (Track A)




### Phase Overview

Implement Track A: the C# action orchestrates tyhpdef generation by making small `php -r` calls to invoke PHP's Reflection API, capturing structured JSON output, and processing it in C# to generate tyhpdef syntax. This approach keeps the logic in C# while using PHP only for reflection data that cannot be obtained any other way (extensions are compiled C code). No PHP scripts are embedded or bundled.

### Deliverables

- `Tyhp/Domain/Services/PhpDelegationTyhpdefGenerator.cs` — C# orchestrator that makes `php -r` calls and processes JSON results
- `Tyhp/Domain/Services/PhpRuntimeDetector.cs` — Utility to find and verify a PHP installation
- Modified `Tyhp/CLI/GenerateTyhpdefAction.cs` — Replace `PLACEHOLDER_PHASE_2` with Track A delegation

### Implementation Details

**2.1 — Implement `PhpRuntimeDetector`**

New file: `Tyhp/Domain/Services/PhpRuntimeDetector.cs`

A utility class that locates and validates a PHP installation:

- `static PhpRuntimeInfo? Detect(string? explicitPath = null)` — returns info or null if not found
- Search strategy:
  1. If `explicitPath` is provided, check that path directly
  2. Check the `PHP_BINARY` environment variable
  3. Search `PATH` for `php` (or `php.exe` on Windows)
  4. Check common installation paths:
     - Linux: `/usr/bin/php`, `/usr/local/bin/php`
     - macOS: `/opt/homebrew/bin/php`, `/usr/local/bin/php`
     - Windows: `C:\php\php.exe`, `C:\xampp\php\php.exe`
- `PhpRuntimeInfo` record: `string Path`, `string Version`, `List<string> LoadedExtensions`
- To get version: run `php --version` and parse output
- To get loaded extensions: run `php -m` and parse output (one extension per line)

**2.2 — Implement `PhpDelegationTyhpdefGenerator`**

New file: `Tyhp/Domain/Services/PhpDelegationTyhpdefGenerator.cs`

A C# orchestrator that uses small `php -r` calls to retrieve reflection data as JSON, then processes the JSON in C# to generate tyhpdef syntax using the `TyhpdefOutputWriter` (Phase 5).

**For PHP Extension mode (`--ext-name`):**

- Verify the extension is installed by checking `PhpRuntimeInfo.LoadedExtensions`
- If not installed, report `MessageCode.TyhpdefGenerationError` with a clear message
- Make a series of targeted `php -r` calls to retrieve structured JSON:

  1. **Constants:** `php -r "echo json_encode((new \ReflectionExtension('curl'))->getConstants());"` → parse JSON dict of name→value, infer types from values
  2. **Functions:** `php -r "echo json_encode(array_map(fn($f) => ['name' => $f->getName(), 'params' => array_map(fn($p) => ['name' => $p->getName(), 'type' => (string)$p->getType(), 'optional' => $p->isOptional(), 'default' => $p->isDefaultValueAvailable() ? $p->getDefaultValue() : null, 'variadic' => $p->isVariadic(), 'byRef' => $p->isPassedByReference()], $f->getParameters()), 'returnType' => (string)$f->getReturnType(), 'deprecated' => $f->isDeprecated()], (new \ReflectionExtension('curl'))->getFunctions()));"` → parse JSON array of function descriptors
  3. **Classes:** For each class name from `php -r "echo json_encode((new \ReflectionExtension('curl'))->getClassNames());"`, retrieve class details via separate `php -r` calls: methods, properties, constants, interfaces, parent class, modifiers
  4. **Enum cases:** For PHP 8.1+ enums, reflect enum cases and backing values

- Each `php -r` call returns JSON that C# deserializes using `System.Text.Json`
- The C# code constructs `TyhpdefDeclaration` objects (from Phase 5) from the deserialized data
- The `TyhpdefOutputWriter` (Phase 5) formats the declarations as tyhpdef syntax

**For Composer Package mode (`--package-path`):**

- Verify the package directory exists and has a `composer.json`
- Use `php -r` to load Composer's autoloader and enumerate classes: `php -r "require '{packagePath}/vendor/autoload.php'; echo json_encode(get_declared_classes());"` (filtered to the package's namespace)
- For each discovered class, reflect it using the same `php -r` approach as extension mode
- Process JSON results in C# and generate tyhpdef via `TyhpdefOutputWriter`

**Process management:**

- Use `System.Diagnostics.Process` to spawn PHP
- Set `RedirectStandardOutput = true`, `RedirectStandardError = true`
- Set working directory to the project root (or the package directory for Composer mode)
- Set a timeout (configurable, default 30 seconds per `php -r` call)
- Capture stdout as JSON, stderr as error messages
- If PHP returns non-zero exit code, report `MessageCode.TyhpdefGenerationError`
- Batch multiple reflection queries into single `php -r` calls where practical to minimize process spawn overhead

**2.3 — Wire Track A into `GenerateTyhpdefAction`**

File: `Tyhp/CLI/GenerateTyhpdefAction.cs`

Replace the `// PLACEHOLDER_PHASE_2: Track A` marker:

1. Call `PhpRuntimeDetector.Detect(options.PhpExecutablePath)`
2. If PHP is found:
   - For `PhpExtension` mode: call `PhpDelegationTyhpdefGenerator.GenerateFromExtension(phpInfo, options)`
   - For `ComposerPackage` mode: call `PhpDelegationTyhpdefGenerator.GenerateFromComposerPackage(phpInfo, options)`
3. If PHP is not found:
   - For `PhpExtension` mode: report `MessageCode.TyhpdefPhpNotFound` as an **error** — Track B cannot handle extensions because they are compiled C code with no PHP source files to parse. Message: *"PHP runtime required for extension tyhpdef generation. Use --php to specify the PHP binary path."*
   - For `ComposerPackage` mode: report `MessageCode.TyhpdefPhpNotFound` as a **warning** and fall through to Track B (when implemented in Phase 4), which can parse the package's PHP source files
   - If Track B is not yet implemented for `ComposerPackage`, report an error: "PHP not found and C# native generation not yet available"
4. Validate the generated tyhpdef by parsing it through the existing tyhpdef parser
5. Report results

**2.5 — Output File Path Determination**

Implement the output file path logic:

- If `--output-file` is specified, use it directly
- If `--output` directory is specified:
  - For extension mode: `{output}/Ext{ExtName}.tyhpdef`
  - For Composer mode: `{output}/{vendor}.{package}.{version}.tyhpdef`
- Default output directory: `./runtime/php-extensions/php{version}/` for extensions, `./tyhpdef_gen/` for Composer packages
- Create output directories if they do not exist
- If file exists and `--overwrite` is not set, report a warning and skip

### Acceptance Criteria

- [ ] `PhpRuntimeDetector.Detect()` correctly locates PHP on the current system (or returns null)
- [ ] `PhpRuntimeDetector.Detect()` correctly reports the PHP version and loaded extensions
- [ ] Running `tyhp generate_tyhpdef --ext-name=json` (with PHP available) produces a valid `ExtJson.tyhpdef` file
- [ ] The generated tyhpdef file begins with `<?tyhpdef` and contains constants, functions, and class declarations
- [ ] The generated tyhpdef file parses without errors through the existing tyhpdef parser (`TyhpParserAstVisitor.Tyhpdef.cs`)
- [ ] Running `tyhp generate_tyhpdef --ext-name=nonexistent` reports a clear error
- [ ] Running `tyhp generate_tyhpdef --ext-name=curl` on a system without PHP reports `TyhpdefPhpNotFound` as an error (no fallback for extensions)
- [ ] Output file paths follow the established naming convention
- [ ] The `--overwrite` flag works correctly (skip vs. overwrite)
- [ ] Generation timing and declaration counts are reported
- [ ] All new files compile without errors

### Dependencies

- **Requires:** Phase 1 (CLI infrastructure, options, result types); **Phase 5** (`TyhpdefOutputWriter` + `TyhpdefDeclaration` IR) — Track A constructs `TyhpdefDeclaration` objects and serializes them via `TyhpdefOutputWriter`, so Phase 5 must precede this phase (or be stubbed)
- **Provides:** Working `tyhp generate_tyhpdef --ext-name=X` command via PHP delegation

---

## Phase 3: PHPDoc Parser for C# Native Generation




### Phase Overview

Build a C# PHPDoc comment parser that extracts type information from PHPDoc annotations (`@param`, `@return`, `@var`, `@throws`, `@template`, `@deprecated`, `@internal`, `@method`, `@property`). This is needed by Track B (C# native generation) because PHP source files often have richer type information in doc comments than in PHP type hints.

### Deliverables

- `Tyhp/Domain/Services/PhpDoc/PhpDocParser.cs` — PHPDoc block parser
- `Tyhp/Domain/Services/PhpDoc/PhpDocBlock.cs` — Parsed doc block data structure
- `Tyhp/Domain/Services/PhpDoc/PhpDocTag.cs` — Individual tag data structure
- `Tyhp/Domain/Services/PhpDoc/PhpDocTypeParser.cs` — PHPDoc type expression parser (handles union types, generics, array shapes, etc.)

### Implementation Details

**3.1 — Define `PhpDocBlock` Data Structure**

New file: `Tyhp/Domain/Services/PhpDoc/PhpDocBlock.cs`

- `string Summary { get; }` — first line of the doc comment (before the first tag)
- `string Description { get; }` — full description text (between summary and first tag)
- `List<PhpDocTag> Tags { get; }` — all parsed tags
- `List<PhpDocTag> ParamTags { get; }` — filtered `@param` tags
- `PhpDocTag? ReturnTag { get; }` — the `@return` tag (if present)
- `List<PhpDocTag> ThrowsTags { get; }` — `@throws` tags
- `List<PhpDocTag> TemplateTags { get; }` — `@template` tags (for PHPStan-style generics)
- `PhpDocTag? VarTag { get; }` — `@var` tag
- `PhpDocTag? DeprecatedTag { get; }` — `@deprecated` tag
- `bool IsInternal { get; }` — has `@internal` tag
- `bool IsDeprecated { get; }` — has `@deprecated` tag
- `List<PhpDocTag> MethodTags { get; }` — `@method` tags (magic methods)
- `List<PhpDocTag> PropertyTags { get; }` — `@property`, `@property-read`, `@property-write` tags

**3.2 — Define `PhpDocTag` Data Structure**

New file: `Tyhp/Domain/Services/PhpDoc/PhpDocTag.cs`

- `string TagName { get; }` — e.g., "param", "return", "throws", "template"
- `string? TypeExpression { get; }` — the type string (e.g., "string|int", "array<string, mixed>")
- `string? ParameterName { get; }` — for `@param` tags, the `$paramName`
- `string? Description { get; }` — the description text after type and name
- `string RawContent { get; }` — the entire raw content after the tag name

**3.3 — Implement `PhpDocParser`**

New file: `Tyhp/Domain/Services/PhpDoc/PhpDocParser.cs`

A parser that takes a raw doc comment string and produces a `PhpDocBlock`:

- `static PhpDocBlock Parse(string docComment)` — main entry point
- Strip the `/**` prefix and `*/` suffix
- Strip leading `*` from each line
- Parse the summary (first non-empty line before any tag)
- Parse the description (remaining text before the first tag)
- Parse tags: each line starting with `@` begins a new tag
- Multi-line tags: continuation lines (not starting with `@`) are appended to the current tag
- Handle PHPStan/Psalm extensions:
  - `@template T` — generic type parameter
  - `@template T of SomeClass` — bounded generic parameter
  - `@param-out` — output parameter type
  - `@assert` — type assertion
  - `@phpstan-param`, `@phpstan-return` — PHPStan-specific overrides (take precedence over `@param`/`@return`)
  - `@psalm-param`, `@psalm-return` — Psalm-specific overrides

Tag-specific parsing:
- `@param <type> $name [description]` — parse type, dollar-prefixed name, description
- `@return <type> [description]` — parse type, description
- `@throws <type> [description]` — parse type, description
- `@var <type> [$name] [description]` — parse type, optional name, description
- `@template <name> [of <constraint>]` — parse name, optional constraint
- `@deprecated [description]` — parse optional deprecation message
- `@method [static] <returnType> <name>(<params>) [description]` — parse magic method signature
- `@property [-read|-write] <type> $name [description]` — parse magic property

**3.4 — Implement `PhpDocTypeParser`**

New file: `Tyhp/Domain/Services/PhpDoc/PhpDocTypeParser.cs`

PHPDoc type expressions can be complex. Parse them into a normalized string suitable for tyhpdef output:

- Simple types: `string`, `int`, `float`, `bool`, `array`, `object`, `null`, `void`, `mixed`, `never`, `self`, `static`, `$this`
- Nullable: `?string` → `?string`
- Union: `string|int|null` → `string|int|null`
- Intersection: `Foo&Bar` → `Foo&Bar`
- Generic: `array<string, int>`, `Collection<User>`, `\Generator<int, string, void, bool>` → preserve as-is
- Array shapes: `array{name: string, age: int}` → preserve or simplify to `array`
- Callable: `callable(int, string): bool` → `callable`
- Array syntax: `string[]` → `array` (or `array<int, string>` for richer output)
- Class-string: `class-string<T>` → `string` (for tyhpdef simplification)
- Literal types: `'foo'|'bar'`, `0|1`, `true`, `false` → preserve where supported, simplify otherwise
- Parenthesized: `(Foo&Bar)|null` → `(Foo&Bar)|null`

The parser does not need to fully resolve types — it produces a string that is valid tyhpdef type syntax. Types that cannot be represented in tyhpdef syntax should be simplified to `mixed`.

### Acceptance Criteria

- [ ] `PhpDocParser.Parse()` correctly parses a standard PHP doc comment with `@param`, `@return`, `@throws` tags
- [ ] `PhpDocParser.Parse()` handles multi-line descriptions
- [ ] `PhpDocParser.Parse()` handles `@template` tags for PHPStan-style generics
- [ ] `PhpDocParser.Parse()` handles `@deprecated` and `@internal` tags
- [ ] `PhpDocParser.Parse()` handles `@method` and `@property` magic tags
- [ ] `PhpDocTypeParser` correctly normalizes union types, nullable types, and generic types
- [ ] `PhpDocTypeParser` simplifies unsupported type expressions to `mixed`
- [ ] All new files compile without errors
- [ ] Edge cases: empty doc comments, malformed tags, nested generics, array shapes

### Dependencies

- **Requires:** Nothing (standalone utility)
- **Provides:** PHPDoc type extraction for Phase 4 (C# native generator)

---

## Phase 4: C# Native PHP Source to Tyhpdef (Track B)




### Phase Overview

Implement Track B: a pure C# tyhpdef generator that parses PHP source files using the existing ANTLR PHP parser/visitor, walks the resulting ASTs to extract type information from PHP type hints and PHPDoc annotations, and produces `.tyhpdef` output. This eliminates the PHP runtime dependency for tyhpdef generation from source files.

### Deliverables

- `Tyhp/Domain/Services/NativeTyhpdefGenerator.cs` — Main C# native generator class
- `Tyhp/Domain/Services/PhpAstTypeExtractor.cs` — Extracts type information from PHP AST nodes combined with PHPDoc
- Modified `Tyhp/CLI/GenerateTyhpdefAction.cs` — Replace `PLACEHOLDER_PHASE_4` with Track B

### Implementation Details

**4.1 — Implement `NativeTyhpdefGenerator`**

New file: `Tyhp/Domain/Services/NativeTyhpdefGenerator.cs`

Main class for C# native tyhpdef generation:

- Constructor accepts `TyhpdefGenerationOptions` and `DiagnosticBag`
- `TyhpdefGenerationResult Generate()` — main entry point
- Uses `CompilationService` (from Story 01) to parse PHP source files
- For each parsed `SrcFileAst`:
  1. Walk the AST top-down
  2. Identify declarations: classes, interfaces, traits, enums, functions, constants
  3. For each declaration, extract type information using `PhpAstTypeExtractor`
  4. Generate tyhpdef syntax using `TyhpdefOutputWriter` (Phase 5)
- Collect all generated tyhpdef content grouped by namespace
- Write output files

**4.2 — Implement `PhpAstTypeExtractor`**

New file: `Tyhp/Domain/Services/PhpAstTypeExtractor.cs`

Walks PHP AST nodes and extracts type information by combining PHP type hints with PHPDoc annotations:

**For class/interface/trait/enum declarations:**
- Extract: name, namespace, modifiers (abstract, final, readonly)
- Extract: extends clause, implements list
- Extract: doc comment → parse with `PhpDocParser`
- Extract: `@template` tags for generic parameters
- Recurse into class body for members

**For method declarations:**
- Extract: name, visibility, static/abstract/final modifiers
- Extract: parameter list (names, type hints, default values, variadic, by-reference, promoted)
- Extract: return type hint
- Extract: doc comment → parse `@param`, `@return`, `@throws`, `@template` tags
- **Type merging strategy (deterministic rule):** If both a PHP type hint and a PHPDoc type exist for a parameter:
  - If the PHP type hint is one of: `array`, `iterable`, `object`, `mixed`, or `callable` — use the PHPDoc type (which is typically more specific, e.g., `array<string, int>` vs `array`)
  - In all other cases, prefer the PHP type hint (it's runtime-enforced and authoritative)
  - If only PHPDoc exists (no PHP type hint), use the PHPDoc type
  - If neither exists, use `mixed`

**For property declarations:**
- Extract: name, visibility, static/readonly modifiers
- Extract: type hint
- Extract: doc comment → parse `@var` tag
- Same type merging strategy as methods

**For constant declarations:**
- Extract: name, visibility
- Extract: value expression (for type inference)
- Infer type from the constant value: `"str"` → `string`, `42` → `int`, `3.14` → `float`, `true` → `bool`, `null` → `null`, `[]` → `array`

**For function declarations (file-level):**
- Same as method declarations but without class context

**For enum declarations:**
- Extract: name, backing type (`:string`, `:int`)
- Extract: cases with optional values
- Extract: implemented interfaces
- Extract: methods and constants

**4.3 — Handle Namespaces and Use Imports**

When walking PHP source files:

- Track the current namespace context from `namespace` declarations
- Track `use` imports to resolve short class names in type hints and doc comments
- When emitting tyhpdef declarations, use fully-qualified names (prefixed with `\`)
- Group declarations by namespace in the output

**4.4 — Handle Visibility Filtering**

For tyhpdef output, only include publicly-accessible API by default:

- Include `public` members
- Include `protected` members (they are part of the class contract for inheritance)
- Exclude `private` members (unless explicitly requested)
- Include `@method` and `@property` magic declarations from doc comments
- Respect `--include-internal` flag for `@internal` items

**4.5 — Wire Track B into `GenerateTyhpdefAction`**

File: `Tyhp/CLI/GenerateTyhpdefAction.cs`

Replace the `// PLACEHOLDER_PHASE_4: Track B` marker:

1. If mode is `PhpSourceFiles`, or if PHP delegation failed/unavailable:
   - Discover PHP source files from `options.SourcePaths` (glob patterns)
   - Parse them using `CompilationService.ParseFiles()` (existing infrastructure)
   - Create `NativeTyhpdefGenerator` with options and diagnostics
   - Call `generator.Generate()`
   - Write output files
   - Report results

**4.6 — Handling PHP-Specific Constructs**

The C# native generator needs special handling for PHP idioms:

- **Dynamic properties (`__get`/`__set`):** If `@property` doc tags exist, include them as declared properties
- **Magic methods (`__call`/`__callStatic`):** If `@method` doc tags exist, include them as declared methods
- **Constructor promotion:** PHP 8.0+ constructor parameter promotion (parameters with visibility modifiers) — create both the parameter and the promoted property
- **Named arguments:** Record parameter names (they are part of the public API in PHP 8.0+)
- **Readonly classes:** PHP 8.2+ `readonly class` keyword
- **Enum methods:** PHP 8.1+ enum cases and methods
- **Intersection types:** PHP 8.1+ intersection types in type hints
- **DNF types:** PHP 8.2+ disjunctive normal form types

### Acceptance Criteria

- [ ] `NativeTyhpdefGenerator.Generate()` correctly processes PHP source files and produces tyhpdef content
- [ ] Running `tyhp generate_tyhpdef --source=./Examples/*.php` produces valid tyhpdef files
- [ ] Class declarations include extends, implements, constants, properties, methods
- [ ] Method signatures include parameter types (from type hints or PHPDoc), return types, and modifiers
- [ ] PHPDoc `@param` and `@return` types are used when PHP type hints are absent
- [ ] PHPDoc `@template` tags produce generic parameter declarations in the tyhpdef output
- [ ] Namespaces are correctly handled: declarations are grouped by namespace with `namespace X { ... }` blocks
- [ ] Only public/protected members are included by default
- [ ] `@deprecated` items are marked with the `deprecated` keyword in tyhpdef output
- [ ] Constructor promotion creates both parameter entries and property declarations
- [ ] Generated tyhpdef files parse without errors through the existing tyhpdef parser
- [ ] All new files compile without errors

### Dependencies

- **Requires:** Phase 1 (CLI infrastructure), Phase 3 (PHPDoc parser), **Phase 5** (`TyhpdefOutputWriter` + `TyhpdefDeclaration` IR — consumed by this track, so it must precede Phase 4)
- **Requires:** Story 01 (`CompilationService`) for parsing PHP files
- **Provides:** Working `tyhp generate_tyhpdef --source=./src/*.php` command

---

## Phase 5: Tyhpdef Output Writer




### Phase Overview

Create a shared tyhpdef output writer that formats extracted type information into valid `.tyhpdef` syntax. This module is used by both Track B (C# native PHP-to-tyhpdef) and Track C (Tyhp code-to-tyhpdef), ensuring consistent output formatting.

### Deliverables

- `Tyhp/Domain/Services/TyhpdefOutputWriter.cs` — Formats declarations into tyhpdef syntax
- `Tyhp/Domain/Services/TyhpdefDeclaration.cs` — Intermediate representation of a tyhpdef declaration

### Implementation Details

**5.1 — Define `TyhpdefDeclaration` Intermediate Representation**

New file: `Tyhp/Domain/Services/TyhpdefDeclaration.cs`

A set of record types representing tyhpdef declarations before they are serialized to text:

- `TyhpdefFile` — top-level file: `string Header`, `List<TyhpdefNamespace> Namespaces`, `List<TyhpdefConstant> GlobalConstants`, `List<TyhpdefFunction> GlobalFunctions`
- `TyhpdefNamespace` — namespace block: `string Name`, `List<TyhpdefClassDeclaration> Classes`, `List<TyhpdefFunction> Functions`, `List<TyhpdefConstant> Constants`
- `TyhpdefClassDeclaration` — class/interface/trait/enum: `string Kind` (class/interface/trait/enum), `string Name`, `List<string> Modifiers`, `string? Extends`, `List<string> Implements`, `List<string> GenericParameters`, `List<TyhpdefConstant> Constants`, `List<TyhpdefProperty> Properties`, `List<TyhpdefMethod> Methods`, `List<TyhpdefEnumCase> EnumCases`, `string? DocComment`, `bool IsDeprecated`
- `TyhpdefMethod` — method signature: `string Name`, `List<string> Modifiers`, `List<TyhpdefParameter> Parameters`, `string ReturnType`, `List<string> GenericParameters`, `string? DocComment`, `bool IsDeprecated`, `bool ReturnsReference`
- `TyhpdefProperty` — property: `string Name`, `string Type`, `List<string> Modifiers`, `string? DocComment`
- `TyhpdefConstant` — constant: `string Name`, `string Type`, `string? Value`, `List<string> Modifiers`, `string? DocComment`
- `TyhpdefParameter` — parameter: `string Name`, `string Type`, `string? DefaultValue`, `bool IsVariadic`, `bool IsByReference`, `string? Attributes`
- `TyhpdefFunction` — top-level function: same as method but without class context
- `TyhpdefEnumCase` — enum case: `string Name`, `string? BackingValue`, `string? DocComment`

**5.2 — Implement `TyhpdefOutputWriter`**

New file: `Tyhp/Domain/Services/TyhpdefOutputWriter.cs`

Serializes `TyhpdefFile` to a valid `.tyhpdef` string:

- `static string Write(TyhpdefFile file)` — main entry point
- Output format follows the patterns established in `runtime/php-extensions/php8.2.9/ExtCore.tyhpdef` and `genTyhpdef.php`:
  1. `<?tyhpdef` header
  2. Generation metadata comment: `/** AUTO-GENERATED, DO NOT EDIT ... */`
  3. Global constants (outside namespace blocks)
  4. For each namespace: `namespace X { ... }` block containing:
     - Constants
     - Functions
     - Classes/interfaces/traits/enums with full member declarations
  5. Consistent indentation (4 spaces inside namespace blocks, 4 more inside class bodies)
- Doc comment formatting: preserve summary and relevant tags (`@param`, `@return`, `@throws`)
- Type string formatting: ensure fully-qualified class names start with `\`
- Whitespace cleanup: remove excessive blank lines (match the cleanup logic in `genTyhpdef.php`)

**5.3 — Tyhpdef Syntax Rules**

The writer must produce syntax that matches the tyhpdef grammar rules in `Tyhp/TyhpLang/Grammar/TyhpParser.g4`:

- Constants: `[modifiers] const <type> <name> [= <value> | ?? <value>];`
- Functions: `[deprecated] function [&]<name>(<params>): <returnType>;`
- Classes: `[modifiers] class <name> [extends <parent>] [implements <interfaces>] { ... }`
- Interfaces: `interface <name> [extends <parents>] { ... }`
- Traits: `trait <name> { ... }`
- Enums: `enum <name> [: <backingType>] [extends <parent>] [implements <interfaces>] { ... }`
- Methods: `[modifiers] function [&]<name>(<params>)[: <returnType>];`
- Properties: `[modifiers] <type> $<name>;`
- Enum cases: `case <name> [= <value>];`
- Parameters: `[attributes] <type> [&][...$]<name> [= <default>]`
- Attributes: `#[<name>(<args>)]`

**5.4 — Output File Splitting Strategy**

For large codebases, the writer may produce output exceeding the 500-line guideline. Implement a splitting strategy:

- One tyhpdef file per PHP extension or Composer package (default — matches current behavior)
- Option to split into multiple files by namespace
- Option to split into one file per class (PSR-4 mirroring)

The default behavior (one file per extension/package) matches the existing `genTyhpdef.php` output and is sufficient for this story.

### Acceptance Criteria

- [ ] `TyhpdefOutputWriter.Write()` produces valid tyhpdef syntax from `TyhpdefFile` input
- [ ] Output matches the formatting conventions of existing tyhpdef files in `runtime/php-extensions/php8.2.9/`
- [ ] Constants, functions, classes, interfaces, traits, and enums are correctly formatted
- [ ] Doc comments are included in the output (when `IncludeDocComments` option is true)
- [ ] Deprecated items are marked with the `deprecated` keyword
- [ ] Namespace blocks wrap their contents correctly
- [ ] Generated output parses without errors through the tyhpdef parser
- [ ] The writer handles edge cases: empty classes, interfaces with no methods, enums with no cases
- [ ] All new files compile without errors

### Dependencies

- **Requires:** Nothing (standalone utility)
- **Provides:** Shared output formatting for Phases 4 (Track B) and 6 (Track C)

---

## Phase 6: Tyhpdef Generation from Tyhp Code (Track C)




### Phase Overview

Implement Track C: when the `build.generateTyhpdef` configuration option is true (auto-enabled for `library` projects), extract the public API declarations from the bound, checked Tyhp AST and write type-definition artifacts. This runs after the checker but **before** the optimizer (Story 23) in the build pipeline, ensuring output captures the original unoptimized public API. This allows other Tyhp (or PHP) projects to import the compiled library's type information.

### Output Structure

Track C generates two kinds of artifacts in a `_tyhpdef/` folder within the build output directory:

```
build/
├── _tyhpdef/
│   ├── App.Models.User.tyhpdef           (one .tyhpdef per type, dot-notation namespace)
│   ├── App.Models.Order.tyhpdef
│   ├── App.Services.PaymentService.tyhpdef
│   ├── App.Constants.tyhpdef             (global constants grouped by namespace)
│   ├── App.Functions.tyhpdef             (global functions grouped by namespace)
│   └── support/
│       ├── App.Extensions.MoneyOps.tyhp  (auto-generated extension classes)
│       └── App.Extensions.StringUtils.tyhp
├── package.tyhp.json                      (manifest pointing to _tyhpdef/ contents)
├── src/
│   └── ... (compiled PHP output)
```

**File naming convention:** Namespace segments use dot notation in the filename instead of a folder hierarchy. For example, a class `\App\Models\User` produces `App.Models.User.tyhpdef`. This mirrors PSR-4 structure but uses dots instead of directories.

**Supporting files:** Auto-generated files that cannot be represented purely in tyhpdef syntax (such as extension classes with delegation bodies for operator overloads) go in a `support/` subfolder as `.tyhp` files.

**`package.tyhp.json`** is a JSON manifest (not containing tyhpdef syntax itself) that lists which files from the package to include:

```json
{
    "include": [
        "./_tyhpdef/*.tyhpdef",
        "./_tyhpdef/support/*.tyhp"
    ]
}
```

This is placed in the build output directory (alongside the generated `composer.json`, `_tyhpdef/` folder, and compiled PHP output). The build output directory IS the Composer package — it is what gets published to Packagist and installed into `vendor/`. Consuming Tyhp projects discover this file in `vendor/{vendor}/{package}/package.tyhp.json`, read the `include` array, and load the referenced `.tyhpdef` and `.tyhp` files. The schema is defined in Story 06.

The correct project layout is:

```
my-library/                    # project root (has tyhp.json)
├── tyhp.json
├── tyhp_src/
├── build/                     # compiler output = the composer package
│   ├── composer.json          # generated
│   ├── package.tyhp.json      # generated
│   ├── _tyhpdef/
│   │   └── *.tyhpdef
│   └── src/
│       └── *.php
```

### Entrypoint Detection and Project Type Rules

| Project Type | `build.generateTyhpdef` | Behavior |
|-------------|------------------------|----------|
| `library` | `true` (auto-default) | Generate `_tyhpdef/` + `package.tyhp.json`. **Error if entrypoint files exist.** |
| `library` | `false` (explicit) | Skip tyhpdef generation. **Still error if entrypoint files exist.** |
| `application` | `false` (auto-default) | No tyhpdef generation. |
| `application` | `true` (explicit) | Generate `_tyhpdef/` + `package.tyhp.json` (same output as library). |

**Entrypoint file definition:** A file is considered an entrypoint if it contains root-level statements with side effects — any code that is not purely declarations (classes, functions, constants, interfaces, traits, enums). Special handling exists for declaration guards (wrapping declarations in `function_exists` / `class_exists` checks) — these are NOT considered entrypoints. Any other root-level statement (function calls, echo, assignments, control flow) makes the file an entrypoint.

Library projects must **always** error on entrypoint files regardless of the `build.generateTyhpdef` setting, because libraries should not contain executable entry points.

### Deliverables

- `Tyhp/Domain/Services/TyhpCodeTyhpdefGenerator.cs` — Generates tyhpdef from bound Tyhp symbols
- Modified `Tyhp/CLI/BuildAction.cs` — Wire tyhpdef generation as a post-check, pre-optimizer step (replaces the existing `// PLACEHOLDER_STORY_20` marker at Step 7.5 in the build pipeline, before the optimizer at Step 8)

### Implementation Details

**6.1 — Implement `TyhpCodeTyhpdefGenerator`**

New file: `Tyhp/Domain/Services/TyhpCodeTyhpdefGenerator.cs`

This generator walks the bound `GlobalScope` (from Story 02's binder) and extracts public API declarations:

- Constructor accepts `GlobalScope`, `TyhpdefGenerationOptions`, and `DiagnosticBag`
- `TyhpdefGenerationResult Generate()` — main entry point
- Walk each `NamespaceScope` in `GlobalScope`:
  - For each `ObjectDeclarationSymbol` (class/interface/trait/enum/struct/extension):
    - Skip if visibility is private or package-internal (if Tyhp implements module visibility)
    - Create a `TyhpdefClassDeclaration` with all public/protected members
    - Include generic parameters and constraints
    - Include type alias declarations
    - Include operator overload signatures
    - Include property accessor declarations
  - For each `FunctionDeclarationSymbol`:
    - Create a `TyhpdefFunction` with full signature
    - Include generic parameters
    - Include overload signatures
  - For each `ConstantSymbol`:
    - Create a `TyhpdefConstant` with type and value
- Use `TyhpdefOutputWriter` (Phase 5) to format and write the output
- Generate one `.tyhpdef` file per type declaration using dot-notation filenames in `_tyhpdef/`
- Generate supporting `.tyhp` files in `_tyhpdef/support/` for extension class delegation bodies
- Generate `package.tyhp.json` manifest in the build output directory with `include` array pointing to `_tyhpdef/` contents

**Tyhpdef file content rules — each `.tyhpdef` file must:**
- Include `<?tyhpdef` header with auto-generated comment, compiler version, and timestamp
- Include only the public/protected API (no private members, no internal members)
- Include only type signatures, not method bodies
- Include generic type parameters with constraints and defaults (Story 28)
- Include `deprecated` / `obsolete` markers where applicable
- NOT include implementation details — method bodies are omitted (signatures only)

**Extension method and operator handling:**
- Extension methods defined by the library are emitted as tyhpdef inline `extension function` declarations within the target class's `.tyhpdef` file (using Story 03 syntax)
- Extension operators are emitted as tyhpdef inline `extension operator` declarations within the target class's `.tyhpdef` file
- The delegation bodies (e.g., `return \MyLib\__TyhpInlineExt_Money::__OP_Money_ADD_Money($left, $right);`) are placed in separate `.tyhp` files in `_tyhpdef/support/` — these are the auto-generated extension classes that the tyhpdef inline declarations delegate to
- If an extension targets a type from a different package (not the library being compiled), it is emitted as a standalone extension declaration at the top level of a dedicated `.tyhpdef` file

**6.2 — Symbol-to-TyhpdefDeclaration Mapping**

Map binder symbols to tyhpdef intermediate representations:

| Symbol Type | Tyhpdef Declaration |
|-------------|-------------------|
| `ObjectDeclarationSymbol` (class) | `TyhpdefClassDeclaration` with `Kind = "class"` |
| `ObjectDeclarationSymbol` (interface) | `TyhpdefClassDeclaration` with `Kind = "interface"` |
| `ObjectDeclarationSymbol` (trait) | `TyhpdefClassDeclaration` with `Kind = "trait"` |
| `ObjectDeclarationSymbol` (enum) | `TyhpdefClassDeclaration` with `Kind = "enum"` |
| `ObjectDeclarationSymbol` (struct) | `TyhpdefClassDeclaration` with `Kind = "struct"` |
| `ObjectDeclarationSymbol` (extension) | `TyhpdefClassDeclaration` with `Kind = "extension"` |
| `FunctionDeclarationSymbol` | `TyhpdefFunction` |
| `ObjectMethodSymbol` | `TyhpdefMethod` |
| `ObjectPropertySymbol` | `TyhpdefProperty` |
| `ObjectConstantSymbol` | `TyhpdefConstant` with visibility modifiers |
| `ConstantSymbol` | `TyhpdefConstant` |
| `TypeAliasSymbol` | Type alias declaration (tyhpdef `type X = Y;` syntax) |
| `GenericTypeParameterSymbol` | Generic parameter on the parent declaration |
| `ObjectOperatorOverloadMethodSymbol` | Operator overload method in the class body |

**6.3 — Handling Tyhp-Specific Constructs in Tyhpdef Output**

Tyhp-specific features need special treatment in tyhpdef output:

- **Structs:** Output as a struct declaration with typed properties
- **Extension methods:** Output as extension declarations with the `extends` keyword on the first parameter
- **Type aliases:** Output as `type X = Y;` declarations
- **Operator overloads:** Output as operator overload method declarations
- **Generic constraints:** Output as `<T extends Constraint>` on the declaration
- **Property accessors:** Output accessor hints on properties
- **Async functions:** Note the `async` keyword and `Promise<T>` return type
- **Disposable types:** Note the `IsDisposable` interface implementation

**6.4 — Wire into BuildAction**

File: `Tyhp/CLI/BuildAction.cs`

After the checker completes but **before** the optimizer (Story 23), at the `PLACEHOLDER_STORY_20` marker:

1. **Entrypoint check (library projects):** If project type is `library`, scan all parsed files for entrypoint detection. If any file contains root-level statements with side effects (not purely declarations or declaration guards), report an error diagnostic and halt the build.
2. Check `project.GetBuildGenerateTyhpdef()` configuration (resolved against project type default)
3. If true, create `TyhpCodeTyhpdefGenerator` with the bound `GlobalScope`
4. Call `generator.Generate()`
5. Write `.tyhpdef` files to `{buildOutputDir}/_tyhpdef/`
6. Write supporting `.tyhp` files to `{buildOutputDir}/_tyhpdef/support/`
7. Generate `package.tyhp.json` manifest in the build output directory (`{buildOutputDir}/package.tyhp.json`)
8. Report generation results in the build summary

**6.5 — `build.generateTyhpdef` Configuration**

Use the existing `BuildConfig.GenerateTyhpdef` property (defined in Story 10 Phase 1). Do not re-define this property. When `null`, the effective default is determined by the project type: `true` for `library` projects, `false` for `application` projects. Resolved as: `this.Build.GenerateTyhpdef ??= (this.Type == ProjectType.Library)`

**6.6 — Extension Method and Extension Operator Handling**

Extension methods and extension operators in the generated tyhpdef output use the tyhpdef inline syntax defined in Story 03. The delegation bodies are separated from the type signatures:

**In the `.tyhpdef` files (type signatures only):**

Extension operators (from Tyhp source `operator +<Money>(Money $left, Money $right): Money { ... }`):
```
class \MyLib\Money {
    // ... regular methods ...
    extension operator +(self $left, self $right): self {
        return \MyLib\__TyhpInlineExt_Money::__OP_Money_ADD_Money($left, $right);
    }
}
```

Extension methods (from Tyhp source `extension function getBadgeNumber(): string { ... }`):
```
class \MyLib\Employee {
    // ... regular methods ...
    extension function getBadgeNumber(): string {
        return \MyLib\__TyhpInlineExt_Employee::getBadgeNumber($this);
    }
}
```

The inline extension bodies contain delegation calls to the compiled synthetic extension classes. These delegation bodies are necessary because operator overloads and extension methods cannot be properly represented as pure tyhpdef signatures — they need to map to PHP methods that may have different signatures. If a developer prefers separation, they can use the `use extension` syntax in tyhpdef instead.

> **Note:** Having executable bodies (brace-body or `=> expr;` arrow-body) in `.tyhpdef` extension declarations is intentional and by design. This is established in Story 03's grammar changes for tyhpdef inline extensions (`extension function`, `extension fn`, `extension operator`). The bodies specify the delegation target for the extension method/operator. The tyhpdef parser grammar (`TyhpParser.g4`) supports this syntax via the `tyhpdefExtensionFunction`, `tyhpdefExtensionOperator` rules added in Story 03.

**In the `_tyhpdef/support/` folder (auto-generated `.tyhp` files):**
- The synthetic extension classes (e.g., `__TyhpInlineExt_Money`) that the inline declarations delegate to are placed as `.tyhp` source files in the support folder
- These are included in the `package.tyhp.json` manifest via the `include` array

**Standalone extension declarations:**
- Extensions targeting types from a different package (not the library being compiled) are emitted as standalone extension declarations at the top level of a dedicated `.tyhpdef` file, preserving the `<TargetType>` syntax

**6.7 — Package manifest (`package.tyhp.json`) discovery by consuming projects**

> **Scope-overlap note:** The vendor-directory scan and `package.tyhp.json` / tyhpdef *discovery and loading* described below is **binder/loader work that overlaps Story 02 (binder) and Story 06 (built-in/tyhpdef registration, including the `package.tyhp.json` schema and the `8025 TyhpdefDuplicateFqnAcrossPackages` diagnostic)**. Story 20 *produces* the `package.tyhp.json` manifests; the *consumption* mechanism here is owned by Stories 02/06 and should be cross-referenced rather than re-implemented by Story 20. This subsection documents the end-to-end contract for completeness.

When a Tyhp project depends on a Composer package that contains a `package.tyhp.json` file, the binder must discover and load it automatically:

1. During binder initialization (Story 02, Story 06 Phase 4), scan the `vendor/` directory for `package.tyhp.json` files
2. Discovery path: `{projectRoot}/vendor/{vendor}/{package}/package.tyhp.json`
3. Read the `include` array from the manifest and resolve globs relative to the package directory
4. Load all matching `.tyhpdef` and `.tyhp` files referenced by the manifest
5. Loading priority: TyhpSpec > PHP ext > **`package.tyhp.json` manifests** > user tyhpdefs
6. If a Composer package provides both PHP source and a `package.tyhp.json`, the manifest takes precedence (the PHP source is not parsed for type information)
7. Package manifest loading is recursive: if package A depends on package B, both `A/package.tyhp.json` and `B/package.tyhp.json` are loaded
8. **Duplicate FQN conflict:** If two different packages define the same fully-qualified type name, this is a **compile-time error** with a diagnostic identifying both packages. This is an improvement over PHP/Composer where duplicate classes silently cause autoloader races.

This discovery mechanism is what enables the Tyhp runtime libraries (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`) to be consumed by user projects — the compiler loads their `package.tyhp.json` files from `vendor/tyhp/*/package.tyhp.json`.

### Acceptance Criteria

- [ ] `TyhpCodeTyhpdefGenerator.Generate()` produces tyhpdef declarations from bound Tyhp symbols
- [ ] Public classes, interfaces, traits, enums, functions, and constants are included
- [ ] Private members are excluded from tyhpdef output
- [ ] Generic parameters and constraints are correctly represented
- [ ] Tyhp-specific constructs (structs, extensions, type aliases, operator overloads) appear in the output
- [ ] The `build.generateTyhpdef` config option triggers generation during `tyhp build`
- [ ] Library projects (`"type": "library"`) auto-generate `package.tyhp.json` in the build output directory
- [ ] Library projects error on entrypoint files (files with root-level side-effect statements) regardless of `build.generateTyhpdef`
- [ ] `package.tyhp.json` is a JSON manifest with `include` array pointing to `_tyhpdef/` contents
- [ ] `.tyhpdef` files use dot-notation filenames (e.g., `App.Models.User.tyhpdef`) in `_tyhpdef/`
- [ ] Extension methods appear as tyhpdef inline `extension function` declarations in the target class's `.tyhpdef` file
- [ ] Extension operators appear as tyhpdef inline `extension operator` declarations in the target class's `.tyhpdef` file
- [ ] Auto-generated extension class `.tyhp` files are placed in `_tyhpdef/support/`
- [ ] `package.tyhp.json` `include` array references both `_tyhpdef/*.tyhpdef` and `_tyhpdef/support/*.tyhp`
- [ ] `package.tyhp.json` discovery works: consuming projects find manifest, read `include` globs, load referenced files
- [ ] Duplicate FQN from two different packages produces a compile-time error
- [ ] Runtime library packages (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`) produce valid `package.tyhp.json` files
- [ ] Generated tyhpdef files parse without errors through the tyhpdef parser
- [ ] Generated tyhpdef files can be loaded by the binder in another project (round-trip test)
- [ ] All new and modified files compile without errors

### Dependencies

- **Requires:** Story 02 (binder) must be substantially complete — needs `GlobalScope` with populated symbols
- **Requires:** Phase 5 (TyhpdefOutputWriter) for output formatting
- **Provides:** Auto-generated tyhpdef files for compiled Tyhp libraries

---

## Phase 7: Validation, Integration, and End-to-End Testing




### Phase Overview

Validate the complete tyhpdef generation pipeline end-to-end, ensure generated files are parseable and usable by the binder, fix any issues discovered during integration testing, and add the tyhpdef validation mode to the CLI for ongoing quality assurance.

### Deliverables

- Modified `Tyhp/CLI/GenerateTyhpdefAction.cs` — Add `--validate` mode that parses and validates existing tyhpdef files
- Modified `Tyhp/CLI/GenerateTyhpdefAction.cs` — Add `--verify` mode that re-generates and compares against existing tyhpdef files
- End-to-end validation of all three tracks
- Fixes for any issues found during validation

### Implementation Details

**7.1 — Add Tyhpdef Validation Mode**

Add a `--validate` flag to `GenerateTyhpdefAction` that:

1. Accepts a directory or file path argument: `tyhp generate_tyhpdef --validate ./tyhpdef/`
2. Discovers all `.tyhpdef` files in the path
3. Parses each file through the existing tyhpdef parser pipeline
4. Reports per-file pass/fail status with line-level error details
5. Reports aggregate statistics: total files, passed, failed, total errors
6. Exits with non-zero code if any files fail

This is useful for CI integration and for validating tyhpdef files after generation.

**7.2 — Add Tyhpdef Verification Mode**

Add a `--verify` flag that:

1. Re-generates tyhpdef files from the source (PHP extension, Composer package, or Tyhp code)
2. Compares the generated output against the existing tyhpdef files
3. Reports differences (new declarations, removed declarations, changed signatures)
4. Exits with non-zero code if differences are found (useful for CI to detect outdated tyhpdefs)

**7.3 — Validate Track A (PHP Delegation)**

Test the full Track A pipeline:

1. Generate tyhpdef for a common PHP extension (e.g., `json`, `date`, `pcre`)
2. Parse the generated file through the tyhpdef parser
3. Verify the file contains expected declarations (classes, functions, constants)
4. Compare against the existing bundled tyhpdef for the same extension
5. Test error handling: non-existent extension, PHP not available

**7.4 — Validate Track B (C# Native)**

Test the full Track B pipeline:

1. Generate tyhpdef from the PHP example files in `Examples/*.php`
2. Parse the generated files through the tyhpdef parser
3. Verify extracted types match expected signatures
4. Test PHPDoc-only types (no PHP type hints)
5. Test mixed type sources (PHP hints + PHPDoc)
6. Test complex constructs: generics via `@template`, union types, intersection types

**7.5 — Validate Track C (From Tyhp Code)**

Test the full Track C pipeline (requires Story 02 binder):

1. Build a simple Tyhp project with `build.generateTyhpdef = true`
2. Verify tyhpdef files are generated alongside PHP output
3. Parse the generated tyhpdef files
4. Verify public API declarations are present and correct
5. Load the generated tyhpdef in a second Tyhp project and verify name resolution works

**7.6 — Validate Existing Bundled Tyhpdefs**

Run the validation mode on all existing tyhpdef files:

1. `tyhp generate_tyhpdef --validate ./runtime/php-extensions/php8.2.9/` — 16 extension files
2. `tyhp generate_tyhpdef --validate ./DebugProject/tyhpdef_gen/8.3.11/en/` — 72 Composer package files
3. Document any parse errors found and fix them (or flag as known issues)

**7.7 — Performance Validation**

Verify generation performance is acceptable:

- Track A: extension generation should complete in under 5 seconds per extension
- Track B: PHP source file processing should handle at least 100 files per second
- Track C: tyhpdef extraction from bound symbols should be near-instantaneous

**7.8 — Help Text for `generate_tyhpdef` Action**

File: `Tyhp/Config/DisplayHelp.cs`

Implement the `GenerateTyhpdefHelp()` method that currently shows "NOT IMPLEMENTED":

- Document all supported modes and flags
- Include examples:
  - `tyhp generate_tyhpdef --ext-name=curl`
  - `tyhp generate_tyhpdef --ext-name=curl --output=./tyhpdef/ --php-version=8.3`
  - `tyhp generate_tyhpdef --source=./vendor/guzzlehttp/guzzle/src/**/*.php --output=./tyhpdef/`
  - `tyhp generate_tyhpdef --validate ./tyhpdef/`
- Document the Track A vs. Track B fallback behavior

### Acceptance Criteria

- [ ] `tyhp generate_tyhpdef --validate ./runtime/php-extensions/php8.2.9/` correctly reports pass/fail for all 16 bundled extension tyhpdefs
- [ ] `tyhp generate_tyhpdef --validate ./DebugProject/tyhpdef_gen/` reports results for all 72 Composer package tyhpdefs
- [ ] Track A end-to-end: `--ext-name=json` produces a file that parses and contains all expected `json_*` functions
- [ ] Track B end-to-end: `--source=Examples/*.php` produces parseable tyhpdef files with correct type signatures
- [ ] Track C end-to-end: a Tyhp build with `build.generateTyhpdef=true` produces loadable tyhpdef files (when Story 02 binder is available)
- [ ] The `--verify` mode detects differences between generated and existing tyhpdef files
- [ ] Help text for `generate_tyhpdef` is complete and includes examples
- [ ] All validation passes without regressions
- [ ] Error messages are clear and actionable for all failure modes

### Dependencies

- **Requires:** All previous phases (1-6)
- **Requires:** Story 01 (`CompilationService`, `DiagnosticBag`) for parsing validation
- **Requires:** Story 02 (binder) for Track C validation — this specific acceptance criterion can be deferred
- **Provides:** Complete, validated tyhpdef generation pipeline

---

## Cross-Cutting Concerns

### File Size Guidelines

| File | Target Maximum | Notes |
|------|---------------|-------|
| `GenerateTyhpdefAction.cs` | 300 lines | Orchestration only; delegate to service classes |
| `PhpDelegationTyhpdefGenerator.cs` | 400 lines | C# orchestrator with `php -r` calls + JSON processing |
| `NativeTyhpdefGenerator.cs` | 500 lines | May need to split AST walking into partial classes |
| `PhpAstTypeExtractor.cs` | 500 lines | Complex type extraction logic |
| `PhpDocParser.cs` | 400 lines | Tag parsing is mechanical but verbose |
| `PhpDocTypeParser.cs` | 300 lines | Type expression parsing |
| `TyhpdefOutputWriter.cs` | 400 lines | Formatting logic |
| `TyhpCodeTyhpdefGenerator.cs` | 400 lines | Symbol walking + mapping |

### Error Handling Conventions

All tyhpdef generation code should follow Story 01's diagnostic system:

- Use `DiagnosticBag.AddError()` / `AddWarning()` / `AddInfo()` for all compiler messages
- Never throw exceptions for recoverable errors (missing files, parse errors in source)
- Use `MessageCode` values in the 8000s range
- Log progress for long-running operations (Track A with large Composer packages, Track B with many source files)

### Placeholder Convention

**Within this implementation plan** — for future phases:
```csharp
// PLACEHOLDER_PHASE_N: description of what goes here
```

**Cross-story references** — for work belonging to other TODO.md stories:
```csharp
// PLACEHOLDER_STORY_N: description of what goes here
```

Key cross-story placeholders in this plan:
- `// PLACEHOLDER_STORY_02: Use binder GlobalScope for Track C` — in `TyhpCodeTyhpdefGenerator`
- `// PLACEHOLDER_STORY_10: Read build.generateTyhpdef from tyhp.json` — in `Project.cs` (Story 10 is a prerequisite and will have implemented project type and build config before Story 20 executes)
- `// PLACEHOLDER_STORY_07: Add unit tests for tyhpdef generation` — in test files (not created in this plan)

### Rollback Safety

Before making changes to any existing file:
- Create a timestamped backup: `cp file.ext file.ext.bak.$(date +%Y%m%d%H%M%S)`
- This applies especially to `GenerateTyhpdefAction.cs`, `Project.cs`, `TyhpHostedService.cs`, and `MessageCode.cs`
- Never use `git reset`, `git revert`, `git checkout .`, or `git clean` to undo changes

### File Organization Summary

New files created in this implementation:

```
Tyhp/Domain/Services/
├── TyhpdefGenerationOptions.cs     (~60 lines)
├── TyhpdefGenerationResult.cs      (~40 lines)
├── PhpDelegationTyhpdefGenerator.cs (~400 lines)
├── PhpRuntimeDetector.cs           (~150 lines)
├── NativeTyhpdefGenerator.cs       (~500 lines)
├── PhpAstTypeExtractor.cs          (~500 lines)
├── TyhpdefOutputWriter.cs          (~400 lines)
├── TyhpdefDeclaration.cs           (~200 lines)
├── TyhpCodeTyhpdefGenerator.cs     (~400 lines)
└── PhpDoc/
    ├── PhpDocParser.cs             (~400 lines)
    ├── PhpDocBlock.cs              (~80 lines)
    ├── PhpDocTag.cs                (~40 lines)
    └── PhpDocTypeParser.cs         (~300 lines)
```

Modified files:
```
Tyhp/CLI/GenerateTyhpdefAction.cs
Tyhp/Config/Project.cs
Tyhp/Config/DisplayHelp.cs
Tyhp/Domain/Exceptions/MessageCode.cs
Tyhp/CLI/BuildAction.cs (if Story 01 has created it, for Track C wiring)
```

---

*Last updated: 2026-03-23 | Source: TODO.md Story 20 | Branch: Not Specified*

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the tyhpdef generator works end-to-end across all three tracks (PHP delegation, C# native, and Tyhp code generation). Steps can be skipped, reordered, or adapted as needed. Some steps require PHP to be installed on your system; those are noted.

### Step 1: Verify CLI Argument Parsing

Run the generate_tyhpdef action with no arguments to check for a helpful error:

```bash
dotnet run -- generate_tyhpdef
```

Expected: A clear error message listing required arguments (e.g., `--ext-name`, `--source`, or `--package-path`).

Verify the help text:

```bash
dotnet run -- help --subject=generate_tyhpdef
```

Expected: Documentation showing all supported modes, flags, and examples.

### Step 2: Verify Track A — PHP Extension Tyhpdef Generation (Requires PHP)

Generate a tyhpdef for a common PHP extension:

```bash
dotnet run -- generate_tyhpdef --ext-name=json
```

Expected:
- A file like `runtime/php-extensions/php{version}/ExtJson.tyhpdef` is created (exact path depends on config)
- The file starts with `<?tyhpdef`
- The file contains declarations for `json_encode`, `json_decode`, `JSON_ERROR_NONE`, `JsonException`, etc.
- Generation timing and declaration counts are reported to the console

Try a second extension:

```bash
dotnet run -- generate_tyhpdef --ext-name=date
```

Expected: A tyhpdef file with `DateTime`, `DateTimeImmutable`, `DateInterval`, `date()`, `time()`, etc.

### Step 3: Verify Track A Error Handling

Try a non-existent extension:

```bash
dotnet run -- generate_tyhpdef --ext-name=nonexistent_ext_xyz
```

Expected: A clear error message saying the extension is not installed/loaded. Error code should be `TYHP7500`.

If PHP is not available on the system:

```bash
dotnet run -- generate_tyhpdef --ext-name=json --php=/nonexistent/path/php
```

Expected: Error `TYHP7501` (`TyhpdefPhpNotFound`) since extensions require PHP for reflection.

### Step 4: Verify Generated Tyhpdef Parses Successfully

The generated tyhpdef files should be parseable by the existing tyhpdef parser. Test this with the `--validate` flag:

```bash
dotnet run -- generate_tyhpdef --validate ./tyhpdef/
```

Expected:
- All `.tyhpdef` files in the directory are parsed
- Each file reports pass/fail status
- Aggregate statistics are shown (total files, passed, failed)
- Exit code `0` if all pass

### Step 5: Verify Bundled Extension Tyhpdefs Still Parse

Validate the 16 bundled extension tyhpdef files shipped with the project:

```bash
dotnet run -- generate_tyhpdef --validate ./runtime/php-extensions/
```

Expected: All files pass validation with no parse errors.

### Step 6: Verify Track B — C# Native PHP Source Parsing (No PHP Required)

Create a small PHP file to generate a tyhpdef from:

Create `test_php_source.php`:

```php
<?php

namespace App\Models;

/**
 * Represents a user in the system.
 *
 * @template T of int|string
 */
class User
{
    /**
     * @param string $name The user's name
     * @param int $age The user's age
     */
    public function __construct(
        public readonly string $name,
        public readonly int $age = 0,
    ) {}

    /**
     * @return array<string, mixed>
     */
    public function toArray(): array
    {
        return ['name' => $this->name, 'age' => $this->age];
    }

    /** @deprecated Use toArray() instead */
    public function serialize(): string
    {
        return \json_encode($this->toArray());
    }
}
```

Generate a tyhpdef from this source:

```bash
dotnet run -- generate_tyhpdef --source=test_php_source.php --output=./test_output/ --no-php
```

Expected:
- A `.tyhpdef` file is generated in `./test_output/`
- The file contains:
  - `class User` declaration in `namespace App\Models`
  - `__construct` method with promoted `$name` (string) and `$age` (int) parameters
  - Both properties listed with correct types and `readonly` modifier
  - `toArray()` method with return type `array` (or `array<string, mixed>` from PHPDoc)
  - `serialize()` method marked with `deprecated`
  - Generic parameter `T` (from `@template T of int|string`)
  - Doc comments preserved (if `--no-docs` is NOT specified)

### Step 7: Verify PHPDoc Type Extraction

Create `test_phpdoc.php` with types only in PHPDoc (no PHP type hints):

```php
<?php

namespace App\Services;

class LegacyService
{
    /**
     * @param string $query
     * @param array<string, mixed> $params
     * @return array<int, object>|false
     */
    public function execute($query, $params = [])
    {
        // ...
    }
}
```

```bash
dotnet run -- generate_tyhpdef --source=test_phpdoc.php --output=./test_output/ --no-php
```

Expected:
- `execute` method parameters have types from PHPDoc: `string $query`, `array $params`
- Return type comes from PHPDoc: `array|false` (or more specific if supported)
- The PHPDoc `@param` types override the missing PHP type hints

### Step 8: Verify Track C — Tyhpdef from Tyhp Code (During Build)

Create a small library project. Create `test_lib/tyhp.json`:

```json
{
    "type": "library",
    "include": ["src/**/*.tyhp"],
    "output": {
        "path": "./build"
    }
}
```

Create `test_lib/src/Calculator.tyhp`:

```tyhp
<?tyhp

namespace MyLib;

class Calculator {
    public function add(int $a, int $b): int {
        return $a + $b;
    }

    public function subtract(int $a, int $b): int {
        return $a - $b;
    }
}
```

Build the library project:

```bash
cd test_lib && dotnet run --project ../tyhp.csproj -- build
```

Expected:
- A `build/package.tyhp.json` manifest file is created
- A `build/_tyhpdef/` directory is created containing `.tyhpdef` files
- The manifest JSON has an `include` array pointing to the tyhpdef files
- The `.tyhpdef` files contain the public API of `Calculator` (both methods, no private members)
- The tyhpdef files parse successfully

### Step 9: Verify Library Entrypoint Detection

Add an entrypoint (root-level statement) to the library project. Create `test_lib/src/main.tyhp`:

```tyhp
<?tyhp

echo "Hello from library!";
```

Rebuild:

```bash
cd test_lib && dotnet run --project ../tyhp.csproj -- build
```

Expected:
- An error diagnostic `TYHP7505` (`TyhpdefLibraryEntrypointDetected`) is reported
- The build fails (library projects must not have entrypoint files)

### Step 10: Verify `--overwrite` Flag

Generate a tyhpdef, then try generating again without `--overwrite`:

```bash
dotnet run -- generate_tyhpdef --ext-name=json --output=./test_output/
dotnet run -- generate_tyhpdef --ext-name=json --output=./test_output/
```

Expected (second run): A warning that the file already exists and was skipped.

Now with `--overwrite`:

```bash
dotnet run -- generate_tyhpdef --ext-name=json --output=./test_output/ --overwrite
```

Expected: The file is regenerated successfully with no skip warning.

### Step 11: Verify `--verify` Mode

If the `--verify` mode is implemented, test it:

```bash
dotnet run -- generate_tyhpdef --ext-name=json --output=./test_output/
# Manually edit the generated file (add or remove a declaration)
dotnet run -- generate_tyhpdef --verify --ext-name=json --output=./test_output/
```

Expected: The verify mode detects the difference and reports which declarations were added/removed/changed. Exit code should be non-zero.

### Step 12: Verify `--no-docs` Flag

```bash
dotnet run -- generate_tyhpdef --ext-name=json --output=./test_output/ --overwrite --no-docs
```

Expected: The generated tyhpdef file has no doc comments (no `/**` blocks).

### Step 13: Verify Extension Method and Operator Handling in Track C

Create a library with extension methods. Create `test_ext_lib/src/MoneyOps.tyhp`:

```tyhp
<?tyhp

namespace MyLib;

class Money {
    public function __construct(
        public readonly int $amount,
        public readonly string $currency
    ) {}
}

extension MoneyExtensions {
    operator +(extends Money $left, Money $right): Money {
        return new Money($left->amount + $right->amount, $left->currency);
    }
}
```

Build as a library project and inspect the generated tyhpdef:

Expected:
- The `Money` class `.tyhpdef` file includes an `extension operator` inline declaration
- A supporting `.tyhp` file exists in `_tyhpdef/support/` with the extension class delegation
- `package.tyhp.json` includes both `_tyhpdef/*.tyhpdef` and `_tyhpdef/support/*.tyhp` in the `include` array

### Step 14: Verify Duplicate FQN Detection

If two packages define the same fully-qualified class name, the compiler should detect and report it:

Expected: A diagnostic `TYHP8025` (`TyhpdefDuplicateFqnAcrossPackages`) identifying both packages.

### Step 15: Cleanup

Remove test files and directories:

```bash
rm -f test_php_source.php test_phpdoc.php
rm -rf test_output/ test_lib/ test_ext_lib/
```

---

## Phase 8: Multi-Target PHP Version Generation (gated output)

> **Depends on Story 20.5** (`declare(php=…)` / `#[\Tyhp\Php]`). Do **not** block Phases 1–7 on this phase.
> Single-target generation remains the default MVP; this phase is the long-term merge toolchain for Story 21.
> Snapshot collection and ungated merge prototypes may land before 20.5; **emitting** real gates requires 20.5.

### Phase Overview

Extend `generate_tyhpdef` so one invocation can reflect **multiple** installed PHP targets, diff their API
surfaces, and emit a **single** gated `.tyhpdef` tree that Story 21 can ship inside `tyhp/php` /
`tyhp/php-ext-*`.

Pipeline for this phase (builds on the three-layer model above):

```text
--php-targets=8.2,8.3,8.4,8.5
    → per-target Track A JSON snapshots (tyhpdef_gen/snapshots/{ver}/)
    → diff/merge → gated IR
    → TyhpdefOutputWriter → Layer 1 baseline tree
    → Layer 2 stub harvest → _tyhpdef/overlays/stubs/*.tyhpdef
    → (compile time) apply "overlay" in array order (stubs first, hand last; last wins)
```

### Deliverables

1. CLI / config for listing target PHP versions and how to invoke each (binaries or containers), e.g.
   `--php-targets=8.2,8.3,8.4,8.5` with per-target executable map
2. Per-target reflection snapshots (Track A JSON or equivalent) under `tyhpdef_gen/snapshots/`
3. Diff/merge pass:
   - Identical across all targets → ungated declaration
   - Added at version V → `declare(php=">=V") { … }` and/or `#[\Tyhp\Php(">=V")]` on members
   - Signature changed at V → version-disjoint declarations (overlapping constraints must not be emitted)
   - Removed after V → upper-bound constraint (`<V+`)
4. Fail clearly if any configured target PHP is missing / cannot run
5. Documented regen workflow: overwrite Layer 1 baseline and Layer 2 `overlays/stubs/`; never wipe hand-written `_tyhpdef/overlays/*.tyhpdef` or `runtime/php-extensions/overlays/` (backup snapshot)
6. Attribution headers + `SOURCES.md` when Layer 2 stub enrichments are applied

### Implementation notes

- Prefer emitting **member attributes** for additions inside shared classes; use **`declare(php=…)` blocks** for
  whole functions/files and for **struct/extension** (Story 20.5 forbids `#[\Tyhp\Php]` on those).
- Output must be valid under Story 20.5 semantics and lint clean for each `output.phpVersion` in the matrix.
- Hand-authored Tyhp overlays are separate tyhpdefs under each package's `_tyhpdef/overlays/` (Story 21). `runtime/php-extensions/overlays/` is a recovery snapshot of pre-split hand edits, not the load path.
- Live compiler load path remains `php8.2.9/` until Story 21 packages + overlay load are wired; then baseline + `package.tyhp.json` `overlay`.
- Do not extend `// @generated-original:` as the overlay mechanism; use load-time overlay files and optional `// @overlay-against:`.

### Acceptance Criteria

- [ ] Multi-target run requires Story 20.5 to be implemented (constraint + attribute semantics available)
- [ ] Generating Core/standard stubs for 8.2–8.5 produces one gated tree
- [ ] `tyhp lint` with `output.phpVersion` set to each minor succeeds and exposes the expected APIs
- [ ] Missing target PHP → actionable error (no silent partial merge)
- [ ] Regen into staging does not modify hand-written `_tyhpdef/overlays/*.tyhpdef` or `runtime/php-extensions/overlays/`
- [ ] Backup snapshot exists and is documented (`runtime/php-extensions/overlays/README.md`)
- [ ] Stub-derived enrichments credit sources (file header and/or `SOURCES.md`)

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
