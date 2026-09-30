# Implementation Plan: Story 25 — `internal` Visibility Modifier

> **Roadmap position:** Story 25 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** all earlier stories (01–24)
> **Renumbered from:** legacy Story 17
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** Tyhp language design — `internal` access boundary
> **Branch:** TBD
> **Generated:** 2026-02-17
> **Prerequisites:** All earlier stories (01–24) must be complete — the parser, binder, checker, emitter, tyhpdef generator, Tyhp runtime packages, LSP, and testing infrastructure must be fully functional.
> **Story 21.10 slice:** grammar (`T_TYHP_INTERNAL`), binder `IsInternal`, 4002 with other visibilities, PHP emit as public / unprefixed, and **omit from the public `package.tyhpdef`** land in 21.10 so libraries can mark author-only types before this story. **This story still owns** checker project-boundary errors, the internals overlay, `includeInternals`, overlay occupancy, and LSP filtering. Do not re-invent the token.

---

## Architecture Overview

### What `internal` Is

`internal` is a compile-time visibility modifier that restricts access to a symbol to within the **defining project**. A "project" is defined by the presence and scope of a `tyhp.json` configuration file. Everything compiled within a single `tyhp.json` project can see `internal` members.

External projects that consume the library through its published tyhpdef **do not** see internal items by default — those items are omitted from the public tyhpdef. A consumer may **opt in** to a dependency's internals by listing that package's Composer name in `includeInternals` (see [Package Internals Overlay](#package-internals-overlay-opt-in)). Opt-in is an **access** gate: the extra declarations become ordinary tyhpdef symbols you can use. Independently, every loaded package's internals overlay is always parsed for **PHP name occupancy** so this project cannot emit a top-level PHP symbol that would collide at runtime.

Unlike `public`, `protected`, and `private` — which map directly to PHP visibility keywords — `internal` has **no PHP equivalent**. It is purely a Tyhp compile-time concept. Internal members compile to `public` PHP (for class members) or no modifier (for top-level declarations) because PHP has no module/assembly boundary mechanism.

### The TypeScript Analogy

Tyhp does for PHP what TypeScript does for JavaScript. In the same way that TypeScript's type annotations are erased in the JavaScript output, Tyhp's `internal` modifier is erased in the PHP output. The enforcement happens entirely at compile time, and the tyhpdef generation system serves as the primary boundary mechanism for external consumers.

### How `internal` Is Enforced

There are two complementary enforcement mechanisms, plus an explicit opt-in to load a package's internals:

1. **Tyhpdef exclusion (primary).** When a Tyhp library is compiled (`"type": "library"` in `tyhp.json`) and its public API is exported as `package.tyhpdef` + `extra.tyhp.package` (Story 20, Track C), items marked `internal` are **excluded** from the public tyhpdef. External projects that depend on the library via `extra.tyhp.package` cannot see those items unless they opt in.

2. **Internals overlay (opt-in access + always-on occupancy).** The same Track C run also writes a second tyhpdef (when any internal symbols exist) and records it under `extra.tyhp.package` `"internalOverlay"`. That file is **not** listed in `"overlay"`. Every loaded package that ships `"internalOverlay"` is parsed for PHP name occupancy (duplicate detection) even when the consumer did not opt in. The overlay is **merged into the accessible type environment** only when the consuming project's `tyhp.json` names the package in `includeInternals`. When merged, it is applied as the first overlay(s) for that package — after `"include"`, before `"overlay"`.

3. **Checker validation (secondary).** When project A directly depends on project B's **source** (e.g., a multi-project workspace), the checker validates that project A does not reference any of project B's `internal` symbols. This catches violations even before tyhpdef generation. Opt-in does not apply to source-to-source project references; it applies only to packages loaded through `extra.tyhp.package`.

### Position in the Pipeline

```
ALL earlier stories complete
(Stories 01–24)
    │
    ▼
┌──────────────────────────────────────────────────────────────┐
│  STORY 25: `internal` Visibility Modifier                    │
│  ◄── THIS PLAN                                               │
│                                                              │
│  Touches: Grammar (Lexer + Parser), Binder, Checker,        │
│           Emitter, Tyhpdef Generator, LSP, Testing           │
│                                                              │
│  Phase 1: Grammar — Add `internal` token and parser rules    │
│  Phase 2: Binder — Track `IsInternal` on symbols             │
│  Phase 3: Checker — Enforce project boundary access rules    │
│  Phase 4: Emitter — Strip `internal`, emit as `public`       │
│  Phase 5: Tyhpdef Generation — Public exclusion + internals  │
│           overlay; occupancy always; access via              │
│           `includeInternals`                                 │
│  Phase 6: LSP Support — Filter autocomplete by boundary      │
│  Phase 7: Testing — Comprehensive test coverage              │
└──────────────────────────────────────────────────────────────┘
```

### Design Principles

1. **`internal` is compile-time only.** It never appears in emitted PHP. It does not affect runtime behavior.

2. **The project boundary is `tyhp.json`.** A single `tyhp.json` file defines the scope of a project. All `.tyhp` files compiled under that project share internal visibility.

3. **Tyhpdef exclusion is the primary enforcement.** External consumers of a library rely on tyhpdef files. Internal items are omitted from the **public** tyhpdef (`package.tyhpdef` / `"include"`). They are written only to the internals overlay, as ordinary tyhpdef declarations (`internal` is not a tyhpdef keyword). Consumers who do not opt in never load that overlay, so the symbols do not exist in their compilation.

4. **`internal` can combine with non-visibility modifiers.** A member can be `internal static`, `internal readonly`, etc. The `internal` modifier is orthogonal to `static`, `abstract`, `final`, and `readonly`, but it cannot be combined with other visibility modifiers (`public`, `protected`, `private`).

5. **`internal` is valid on any item that could appear in tyhpdef.** This includes: classes, interfaces, traits, enums, functions, constants, type aliases, methods, properties, and enum cases.

6. **`internal` is a standalone visibility modifier.** It cannot be combined with any other visibility modifier (`public`, `protected`, `private`). Using `internal` with another visibility modifier is a `CheckerMultipleVisibilities` (4002) error, the same as using `public` and `private` together. For class members, `internal` emits as `public` in PHP. For top-level declarations, `internal` emits as no modifier (PHP default).

### Tyhp Syntax Examples

**Top-level declarations:**

```tyhp
<?tyhp

// Internal class — visible within this project only
internal class InternalHelper {
    public function doWork(): void {
        // ...
    }
}

// Internal function
internal function computeHash(string $data): string {
    return hash('sha256', $data);
}

// Internal constant
internal const int MAX_RETRIES = 3;

// Internal type alias
internal type UserId = int;

// Internal interface
internal interface Cacheable {
    public function getCacheKey(): string;
}

// Internal enum
internal enum LogLevel {
    case Debug;
    case Info;
    case Warning;
    case Error;
}

// Internal trait
internal trait HasTimestamps {
    public DateTime $createdAt;
    public DateTime $updatedAt;
}

// Public class is visible to external consumers
public class PublicService {
    // Internal method — visible within project, hidden from tyhpdef
    internal function resetState(): void {
        // ...
    }

    // Internal property
    internal int $retryCount = 0;

    // Public method — visible to everyone
    public function process(): void {
        $this->resetState();
    }
}
```

**Emitted PHP output (for the public class above):**

```php
<?php

class PublicService {
    // internal is stripped; emitted as public
    public function resetState(): void {
        // ...
    }

    public int $retryCount = 0;

    public function process(): void {
        $this->resetState();
    }
}
```

**Generated tyhpdef (for external consumers):**

```tyhpdef
<?tyhpdef

// InternalHelper is EXCLUDED entirely
// computeHash is EXCLUDED entirely
// MAX_RETRIES is EXCLUDED entirely
// UserId is EXCLUDED entirely
// Cacheable is EXCLUDED entirely
// LogLevel is EXCLUDED entirely
// HasTimestamps is EXCLUDED entirely

class PublicService {
    // resetState() is EXCLUDED — it was internal
    // $retryCount is EXCLUDED — it was internal

    public function process(): void;
}
```

**Generated internals overlay** (`package.internal.tyhpdef`, loaded only when the consumer opts in):

```tyhpdef
<?tyhpdef

class InternalHelper {
    public function doWork(): void;
}

function computeHash(string $data): string;

const int MAX_RETRIES = 3;

type UserId = int;

interface Cacheable {
    public function getCacheKey(): string;
}

enum LogLevel {
    case Debug;
    case Info;
    case Warning;
    case Error;
}

trait HasTimestamps {
    public DateTime $createdAt;
    public DateTime $updatedAt;
}

partial class PublicService {
    public function resetState(): void;
    public int $retryCount;
}
```

### Package Internals Overlay (opt-in)

This is the complete contract for publishing and consuming a library's internal API through tyhpdef. It does not change PHP emit: opted-in internals remain `public` (or unmodified top-level) in PHP, the same as they are for the defining project.

#### Publisher (Track C library build)

Libraries always emit `package.tyhpdef` and additive-merge `extra.tyhp.package` onto publish-directory `composer.json`. Applications never publish an internals overlay: `build.generateTyhpdef` writes `package.tyhpdef` only (no `extra.tyhp.package`), so there is no `"internalOverlay"` section to advertise.

When the compilation has **at least one** exportable internal symbol (a top-level internal type, function, constant, type alias, or extension; or an internal member of a public type — `private` is never exported):

1. Write the public tyhpdef as today, with internals excluded (`package.tyhpdef`).
2. Write `package.internal.tyhpdef` next to it, containing those internal items as ordinary tyhpdef (see shape below).
3. Write `"internalOverlay": ["./package.internal.tyhpdef"]` on `extra.tyhp.package`. Do **not** put that path in `"include"` or `"overlay"`. Package Composer `name` for `includeInternals` matching is `composer.json` `"name"` (the tyhpdef spec lives on that same file; do not stamp a second `"name"` onto `extra.tyhp.package`).

When the compilation has **no** exportable internal symbols:

- Do not write `package.internal.tyhpdef`. If a previous build left that file in the output directory, delete it.
- Omit the `"internalOverlay"` key from `extra.tyhp.package` (an empty array is treated the same as omitted at load time).

Track C overwrites `package.internal.tyhpdef` on every successful library emit that has internals, the same way it overwrites `package.tyhpdef`. `WriteManifest` continues to additive-merge generated `extra.tyhp.package` onto publish-directory `composer.json` (`"include": ["./package.tyhpdef"]`, empty `"overlay"`), and adds `"internalOverlay": ["./package.internal.tyhpdef"]` only when internals exist. Hand-maintained manifests (Story 21 `_tyhpdef/` packages) are not produced by Track C; their authors list `"internalOverlay"` themselves.

`tyhp generate_tyhpdef` Track A/B (PHP reflection / Composer PHP) does **not** write this overlay. PHP docblock `@internal` and the `--include-internal` flag are a different feature. A package author **may** still list hand-written files under `"internalOverlay"`; the loader does not care how the files were produced.

#### Internals overlay file shape

`internal` remains invalid in tyhpdef mode (Phase 1). The overlay is ordinary tyhpdef:

| Source | Overlay declaration |
|--------|---------------------|
| Wholly internal class / interface / trait / enum / function / constant / type alias / extension | Full declaration, same spelling Track C would have used if the item were public |
| Internal members of a **public** type | Overlay `partial` of that type adding only those members |
| Internal enum cases on a public enum | Overlay `partial enum` adding those cases |
| `private` members | Not written (never part of tyhpdef) |

Public signatures in `package.tyhpdef` still resolve internal **type aliases** to their underlying public types (section 5.3). The alias itself is declared in the internals overlay so opted-in consumers can use the alias name.

Namespaces: same grouping as the public tyhpdef. A namespace that is entirely internal appears only in the internals overlay.

Header: mark the file as generated internals overlay. Every consumer that loads this package parses it for PHP name occupancy. It is merged into the accessible type environment only when the consumer lists this package in `includeInternals`. Honor the package's `source.tagless` the same as other package tyhpdefs.

#### `extra.tyhp.package` — `"internalOverlay"`

New optional array, same JSON shape as `"overlay"`: strings that are paths or globs relative to the manifest directory.

```json
{
    "include": ["./package.tyhpdef"],
    "exclude": [],
    "internalOverlay": ["./package.internal.tyhpdef"],
    "overlay": [
        "./_tyhpdef/overlays/stubs/*.tyhpdef",
        "./_tyhpdef/overlays/*.tyhpdef"
    ]
}
```

Rules:

- `"internalOverlay"` is **never** copied into `"overlay"`. Default consumers do not merge it into the accessible type environment; they still parse it for occupancy.
- Glob expansion matches `"overlay"`: array order, then lexicographic paths within a glob; `./_tyhpdef/*.tyhpdef` does not recurse into overlay folders.
- A path/glob **string** that also appears in `"include"` or `"overlay"` is invalid: error `TyhpdefInternalsOverlayPathConflict` (4337) when the manifest is loaded, whether or not any consumer opted in.
- `--verify` and overlay stamp/create apply `"include"` + `"overlay"` only. They never apply `"internalOverlay"`.

#### Consumer (`tyhp.json`) — `"includeInternals"`

New top-level array of Composer package **names** (the `"name"` field in that package's `composer.json`, e.g. `"tyhp/core"`, `"monolog/monolog"`). Parsed like `tyhpdefInclude`: array of strings; empty/whitespace entries ignored; duplicate names collapsed case-insensitively (first spelling kept). Omitted or `[]` means internals overlays are **not merged for access** (completions, references, signatures). Occupancy parsing still happens for every loaded package that ships `"internalOverlay"`. There is no CLI flag; `tyhp.json` is the only control.

```json
{
    "includeInternals": [
        "vendor/package",
        "tyhp/core"
    ]
}
```

Matching: for each loaded `extra.tyhp.package`, the Composer name is `"name"` from that package’s `composer.json` (the same file that holds `extra.tyhp.package`).

Compare that name to `includeInternals` with ordinal case-insensitive equality. Folder names, tyhp project names, and Packagist versions are not used. A package whose `composer.json` has no `"name"` cannot match any opt-in entry.

`includeInternals` is a set of names. Package load order is unchanged. Occupancy always uses `"internalOverlay"` when present. Accessible merge prepends those files to **that package's** overlay sequence only for names in the set.

Opt-in is **not transitive**. If A lists `b/pkg` and C depends on A, C does not load `b/pkg`'s internals unless C also lists `b/pkg`.

Source-to-source multi-project references (checker 4330/4331) are unchanged. `includeInternals` only affects packages loaded through `extra.tyhp.package`.

#### Load order

Parse each package's `"internalOverlay"` files **once** whenever the globs match at least one file. Do not parse them twice (occupancy + access).

For each package, after expanding globs:

1. `"include"` — baseline, as today.
2. `"internalOverlay"` — if the globs match any files:
   - **Always** register **PHP name occupancy** (see below).
   - **If** this project's `includeInternals` contains this package's Composer name, also bind those files as accessible overlays (`isOverlay: true`), in array order, as the first overlay(s) for the package.
3. `"overlay"` — stubs then hand-written, as today.

If `"internalOverlay"` is missing or matches nothing: skip step 2. Warn 4335 only when the consumer **opted in**.

Consequence of overlay last-wins (accessible merge only): a later `"overlay"` **full replace** of a type drops members that existed only in the internals overlay. A later `"overlay"` **`partial`** keeps unlisted members, including internals-overlay members. Package authors who ship internals should use `partial` overlays (not full replace) for types that have internal members.

#### PHP name occupancy (always on)

`internal` is erased in PHP. A package's `internal class Foo` still emits `class Foo`. A consumer that never opts in can still declare `class Foo` in the same namespace and produce a PHP duplicate at runtime. Occupancy exists so the binder/checker/LSP catch that at compile time.

**When:** For every loaded `extra.tyhp.package` that has matching `"internalOverlay"` files — **whether or not** the package is in `includeInternals`.

**What occupies a name:** top-level declarations in those files that emit a PHP top-level symbol, using the same PHP symbol tables Tyhp already uses for duplicates (`BinderDuplicateSymbolDeclaration` / `TyhpdefDuplicateFqnAcrossPackages`):

| Occupies | Does not occupy |
|----------|-----------------|
| Wholly internal class, interface, trait, enum (full declarations) | Overlay `partial` that only adds members to a type already in `"include"` (the public type already occupies that class-like name) |
| Internal free functions | Type aliases (compile-time only; no PHP symbol) |
| Internal namespace/global constants | Internal members of public types (methods, properties, class constants, enum cases) |
| Internal extension **backer class** PHP name (same name Track C would emit) | |

**Checks:**

| Collision | Diagnostic |
|-----------|------------|
| This project's `.tyhp` source declares a top-level symbol whose emitted PHP name is already occupied by a package internal, and that package is **not** in `includeInternals` | **4338** `CheckerDeclarationConflictsWithPackageInternal` on the project's declaration |
| Same, but the package **is** in `includeInternals` (symbol is in the accessible environment) | Existing duplicate: `BinderDuplicateSymbolDeclaration` (3002) or `TyhpdefDuplicateFqnAcrossPackages` (8025). Do not also emit 4338 |
| Two loaded packages both occupy the same FQN (public vs internal, or internal vs internal), even if neither is opted in | **8025** `TyhpdefDuplicateFqnAcrossPackages` — feed occupancy FQNs into `TyhpdefSymbolRegistrar` so this fires without accessible merge |

4338 is an **error** (PHP would fail). Primary span is the project's conflicting declaration. `{0}` is the FQN; `{1}` is the occupying package's Composer name. Quick-fix is rename (or stop declaring it). Adding `includeInternals` does **not** fix 4338 — the PHP names still collide.

LSP uses the same compilation: declaration-site 4338/3002/8025 appear in the editor. Completions, hover, go-to-definition, and workspace symbols for **using** internals remain gated on `includeInternals`. Occupancy-only symbols are not offered as completion items.

#### Diagnostics (warnings, not errors)

Both fire at package-load / config time, once per unique Composer name, with the primary span on the `includeInternals` entry in `tyhp.json`. `build.strictMode` / `--strict` promotes them to errors, like other warnings.

| Situation | Code | When |
|-----------|------|------|
| Name is in `includeInternals`, a matching package **is** loaded, and `"internalOverlay"` is missing, empty, or expands to **zero** files | 4335 `CheckerPackageInternalsNotProvided` | Package exists as a Tyhp package but publishes no internals overlay |
| Name is in `includeInternals` and **no** loaded `extra.tyhp.package` has that Composer `name` | 4336 `CheckerPackageInternalsUnknownPackage` | Not a loaded Tyhp dependency (not in `vendor/`, not pulled in via `tyhpdefInclude` / `include`) |

Do **not** warn when the consumer did not list the package. Do **not** warn when internals files load successfully (even if the overlay happens to add nothing useful). Listing the current project's own Composer name is 4336 unless that project's **published** manifest is also loaded as a dependency.

#### Checker and LSP after opt-in (access gate)

Symbols merged from `"internalOverlay"` because of `includeInternals` are ordinary tyhpdef symbols: `IsInternal = false`. They are in the compilation's **accessible** type environment, so:

- Phase 3 does **not** report 4330/4331 for them (the access check returns on `!IsInternal`).
- Phase 6 completions, hover, go-to-definition, and workspace symbols show them because they are accessible.
- 4333 still applies only to **this** compilation's own `internal` source types leaking through **this** project's public API. It does not treat another package's overlay-loaded types as internal.

Without opt-in, those files still contribute occupancy (4338 / 8025) but are **not** accessible: references behave as today (unresolved names), and LSP does not suggest them for use.

**Re-export.** Because opted-in overlay symbols look public, this project may use them in its own public signatures. Track C will emit those type names into **this** project's public tyhpdef. A downstream consumer that does not also opt into the original package cannot use those types (unresolved name) but **will** still hit occupancy (4338) if it declares the same PHP top-level name, as long as the original package is loaded.

#### What is unchanged

- Same-project source still sees `internal` directly; no overlay involved.
- Consumers that do not opt in still cannot **use** internal items (they are not in the accessible environment).
- Emitted PHP still has no `internal` keyword.
- `internal` is still not a tyhpdef keyword.

### MessageCode Numbering

This story uses MessageCode values starting at **4330** (4320–4324 are allocated to Story 16). Register the enum values in `MessageCode.cs` and the `.resx` keys; `CONVENTIONS.md` remains the diagnostic-code source of truth.

| Code | Enum Name | Severity | Message |
|------|-----------|----------|---------|
| 4330 | `CheckerAccessToInternalMember` | error | Cannot access internal member `{0}` from outside the defining project `{1}` |
| 4331 | `CheckerAccessToInternalType` | error | Cannot access internal type `{0}` from outside the defining project `{1}` |
| 4332 | `CheckerInternalNotAllowedHere` | error | The `internal` modifier is not valid in this context |
| 4333 | `CheckerInternalMemberExposedViaPublicApi` | error | Internal type `{0}` is exposed via public API member `{1}`; the return/parameter type must be the same visibility or more visible than the member itself |
| 4334 | `CheckerInternalInTraitAlias` | error | The `internal` modifier cannot be used in a trait alias visibility declaration; use `public`, `protected`, or `private` instead |
| 4335 | `CheckerPackageInternalsNotProvided` | warning | Package `{0}` listed in `includeInternals` does not provide an internals overlay |
| 4336 | `CheckerPackageInternalsUnknownPackage` | warning | Package `{0}` listed in `includeInternals` is not a loaded Tyhp package |
| 4337 | `TyhpdefInternalsOverlayPathConflict` | error | Path `{0}` is listed in both `internalOverlay` and `{1}` of `extra.tyhp.package` |
| 4338 | `CheckerDeclarationConflictsWithPackageInternal` | error | Cannot declare `{0}`; package `{1}` already emits that PHP name as an internal symbol |

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup using the canonical naming: `<filename>.bak.<YYYYMMDD_HHMMSS>`
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: Grammar — Add `internal` as a Modifier Token




### Phase Overview

Add `internal` as a recognized keyword in the Tyhp lexer and parser. It must be usable in the same positions as other modifiers: as a class/function/declaration modifier at the top level, and as a member modifier within class/interface/trait/enum bodies. The keyword is only recognized in `tyhp` language mode (not raw PHP mode or tyhpdef mode).

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` — Add `T_TYHP_INTERNAL` token
- `Tyhp/TyhpLang/Grammar/TyhpParser.g4` — Add `internal` to modifier rules via grammar addon overrides
- `Tyhp/TyhpLang/Grammar/PhpParser.g4` — No changes (internal is Tyhp-only; the base PHP grammar is untouched)

**Regenerated files:**
- `Tyhp/TyhpLang/Parser/` — Regenerated ANTLR4 C# parser/lexer classes (via `compile_grammar.sh`)
- `Tyhp/TyhpLang/Tyhp/TyhpLang/Grammar/*.interp`, `Tyhp/TyhpLang/Tyhp/TyhpLang/Grammar/*.tokens` — Regenerated ANTLR4 metadata files

### Implementation Details

#### 1.1 Lexer: Add `T_TYHP_INTERNAL` Token

**File: `Tyhp/TyhpLang/Grammar/TyhpLexer.g4`**

Add `T_TYHP_INTERNAL` to the `tokens` block alongside the existing Tyhp-specific tokens:

```antlr
tokens {
    // ... existing tokens ...
    T_TYHP_INTERNAL,
    // ...
}
```

Add the lexer rule in the `ST_IN_SCRIPTING` mode, following the same pattern as other Tyhp keywords (`T_TYHP_ASYNC`, `T_TYHP_OPERATOR`, etc.):

```antlr
T_TYHP_INTERNAL options{caseInsensitive=true;}:
                            'internal' {(this._languageMode == "tyhp")}? -> type(T_TYHP_INTERNAL);
```

This rule:
- Is case-insensitive (matching Tyhp's convention for keywords)
- Only activates in `tyhp` language mode (not raw PHP or tyhpdef)
- Uses the same semantic predicate pattern as existing Tyhp keywords

#### 1.2 Parser: Add `internal` to Member Modifier Rule

**File: `Tyhp/TyhpLang/Grammar/TyhpParser.g4`**

Override the `memberModifierGrammarAddon` rule to include `T_TYHP_INTERNAL` alongside the existing `T_TYHP_ASYNC`:

```antlr
// ! OVERRIDE
memberModifierGrammarAddon
    : TokenValue=T_TYHP_ASYNC {this.isLanguageMode("tyhp")}?
    | TokenValue=T_TYHP_INTERNAL {this.isLanguageMode("tyhp")}?
    ;
```

This makes `internal` valid in the `memberModifier` rule (defined in `PhpParser.g4`), which is used by:
- `nonEmptyMemberModifiers` → property declarations, method declarations, constant declarations within classes/interfaces/traits/enums
- `traitAliasVisibility` → trait alias visibility changes

#### 1.3 Parser: Add `internal` to Class/Function/Declaration Modifiers

**File: `Tyhp/TyhpLang/Grammar/TyhpParser.g4`**

Add `internal` as a valid modifier for top-level declarations. Since `classModifier` in `PhpParser.g4` only supports `abstract` and `final`, we need a grammar addon override for class declarations that allows `internal`:

Override `classDeclarationStatementGrammarAddon` to allow `internal` as a class modifier:

```antlr
// ! OVERRIDE
classDeclarationStatementGrammarAddon
    : T_TYHP_INTERNAL Modifiers=classModifiers? ObjectType=T_CLASS Identifier=T_STRING
        classNameGrammarAddon Extends=extendsFrom
        Implements=implementsList FindDocComment=T_OPEN_CURLY_BRACE
        StatementList=classStatementList T_CLOSE_CURLY_BRACE
        {this.isLanguageMode("tyhp")}?                                          #tyhpInternalClassDeclaration
    ;
```

Similarly, add grammar addon overrides for functions at the top level. For function declarations, update `functionModifiersGrammarAddon`:

```antlr
// ! OVERRIDE
functionModifiersGrammarAddon
    : IsInternal=T_TYHP_INTERNAL? IsAsync=T_TYHP_ASYNC? {this.isLanguageMode("tyhp")}?
    ;
```

Similarly, override the modifier grammar addons for traits, interfaces, and enums to accept `T_TYHP_INTERNAL`:

**Trait declarations:**

```antlr
// ! OVERRIDE
traitModifiersGrammarAddon
    : IsInternal=T_TYHP_INTERNAL? {this.isLanguageMode("tyhp")}?
    ;
```

**Interface declarations:**

```antlr
// ! OVERRIDE
interfaceModifiersGrammarAddon
    : IsInternal=T_TYHP_INTERNAL? {this.isLanguageMode("tyhp")}?
    ;
```

**Enum declarations:**

```antlr
// ! OVERRIDE
enumModifiersGrammarAddon
    : IsInternal=T_TYHP_INTERNAL? {this.isLanguageMode("tyhp")}?
    ;
```

For top-level constants and type aliases, update `topStatementGrammarAddon` to handle `internal` prefixed declarations. Since top-level constants and type aliases are already handled via `topStatementGrammarAddon`, add `T_TYHP_INTERNAL` as an optional prefix to the existing alternatives.

**Extension declarations:** Extension class declarations can be `internal` (making the entire extension internal). Extension classes do not allow modifiers such as `abstract`, `final`, or `readonly`. Update the extension declaration grammar addon to accept an optional `T_TYHP_INTERNAL` prefix.

**The full list of contexts where `internal` is valid:**
- Class declarations (in addition to abstract/final/readonly)
- Interface declarations
- Trait declarations
- Enum declarations
- Function declarations
- Constant declarations (class level and namespace level)
- Type alias declarations (class level and namespace level)
- Method declarations (as a visibility modifier, cannot be used with other visibility modifiers)
- Property declarations (as a visibility modifier, cannot be used with other visibility modifiers)
- Enum case declarations
- Extension method and operator overload declarations
- Class operator overload declarations
- Extension class declarations (the entire extension class is internal)

#### 1.4 Regenerate Parser

After modifying the grammar files:

```bash
./compile_grammar.sh
dotnet clean && dotnet restore && dotnet build
```

Requires `antlr-ng` (`npm install -g antlr-ng`). Grammar regeneration is **not** triggered by `dotnet build` alone.

### Acceptance Criteria

- [ ] `internal` is recognized as a keyword in Tyhp mode: `<?tyhp internal class Foo {}`
- [ ] `internal` is NOT recognized as a keyword in Tyhpdef mode: `<?tyhpdef internal class Foo {}` → parsed as identifier
- [ ] `internal` is NOT recognized in raw PHP mode: `<?php internal class Foo {}` → parsed as identifier
- [ ] `internal` works as a member modifier: `internal function foo(): void;`
- [ ] `internal` works as a class modifier: `internal class Foo {}`
- [ ] `internal` works as a function modifier: `internal function bar(): void {}`
- [ ] `internal` can combine with other modifiers: `internal static function baz(): void {}`
- [ ] `internal abstract class Foo {}` is syntactically valid
- [ ] ANTLR4 generates clean parser/lexer C# code without errors
- [ ] All existing tests continue to pass (no grammar regressions)

### Dependencies

- **Requires:** All earlier stories complete (01–24 — grammar/parser infrastructure functional)
- **Provides for:** Phase 2 (binder needs to read the `internal` modifier from AST nodes)

---

## Phase 2: Binder — Track `IsInternal` on Symbols




### Phase Overview

Update the binder to recognize the `internal` modifier on AST nodes and propagate it to the symbol system. Every symbol that can carry visibility (`ObjectDeclarationSymbol`, `FunctionDeclarationSymbol`, `ConstantSymbol`, `ObjectMethodSymbol`, `ObjectPropertySymbol`, `ObjectConstantSymbol`, `TypeAliasSymbol`, etc.) must track whether it was declared `internal`.

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Binder/Symbols/BaseSymbol.cs` — Add `IsInternal` property
- `Tyhp/TyhpLang/Binder/Symbols/ObjectDeclarationSymbol.cs` — Propagate `IsInternal` from AST
- `Tyhp/TyhpLang/Binder/Symbols/FunctionDeclarationSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ConstantSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ObjectMethodSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ObjectPropertySymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ObjectConstantSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/TypeAliasSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ObjectAccessorMethodSymbol.cs` — Propagate `IsInternal`
- `Tyhp/TyhpLang/Binder/Symbols/ObjectOperatorOverloadMethodSymbol.cs` — Propagate `IsInternal`
- Extension-related symbol classes — Propagate `IsInternal` for extension declarations marked internal
- Enum case symbols — Propagate `IsInternal` for internal enum case declarations
- `Tyhp/TyhpLang/Binder/SymbolTree.cs` — Store project identity (from `tyhp.json` path) on the symbol tree
- `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.cs` — Extract `internal` modifier from parsed nodes

### Implementation Details

#### 2.1 Add `IsInternal` to `BaseSymbol`

**File: `Tyhp/TyhpLang/Binder/Symbols/BaseSymbol.cs`**

Add a boolean property to the base symbol class:

```csharp
public class BaseSymbol : Interfaces.IBaseSymbol
{
    /// <summary>
    /// Whether this symbol was declared with the `internal` modifier.
    /// Internal symbols are only visible within the defining project.
    /// </summary>
    public bool IsInternal { get; set; } = false;

    /// <summary>
    /// The project identity (tyhp.json path) that defines this symbol's visibility boundary.
    /// Null for symbols from external tyhpdef files (which by definition are public).
    /// </summary>
    public string? DefiningProjectPath { get; set; } = null;
}
```

By placing `IsInternal` on `BaseSymbol`, all symbol subclasses inherit it automatically. The `DefiningProjectPath` allows the checker to compare whether two symbols belong to the same project.

#### 2.2 Add `IInternalizable` Interface

`IInternalizable` is a required interface that provides a clear type-safe contract for which AST nodes support the `internal` modifier:

```csharp
namespace Tyhp.TyhpLang.Binder.Symbols.Interfaces
{
    public interface IInternalizable
    {
        bool IsInternal { get; set; }
        string? DefiningProjectPath { get; set; }
    }
}
```

Since `IsInternal` is on `BaseSymbol`, every symbol already has the property. The interface provides explicit documentation of intent and enables type-safe checks in the checker and emitter.

#### 2.3 Store Project Identity on SymbolTree

**File: `Tyhp/TyhpLang/Binder/SymbolTree.cs`**

Add a `ProjectPath` property to the `SymbolTree` class that records the `tyhp.json` file path for the current compilation:

```csharp
/// <summary>
/// The absolute path to the tyhp.json file that defines this project.
/// Used to determine project boundaries for `internal` visibility checks.
/// </summary>
public string? ProjectPath { get; set; }
```

When the binder initializes, it sets `ProjectPath` from `Project.GetProjectPath()`. Every symbol created during binding inherits this project path via `DefiningProjectPath`.

#### 2.4 Visitor: Extract `internal` from AST Nodes

**Files: `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.cs`, `.Tyhpdef.cs`**

When visiting member modifier nodes, check for `T_TYHP_INTERNAL` in the modifier list and set the `IsInternal` flag on the corresponding AST node. The modifier is represented by the `TokenValueGrammarAddon` alternative in the `memberModifier` rule (via `memberModifierGrammarAddon`).

The visitor must also handle the top-level declaration variants where `internal` appears as a prefix (from Phase 1's grammar changes).

#### 2.5 Binder: Propagate `internal` to Symbols

When the binder creates symbols from AST nodes, check the AST's modifier flags and set `IsInternal = true` on the symbol if the `internal` modifier is present. Also set `DefiningProjectPath` to the current compilation's project path.

For symbols loaded from external `.tyhpdef` files (public `"include"`, `"overlay"`, and **accessible** `"internalOverlay"`), `IsInternal` defaults to `false`. Accessible internals-overlay symbols are ordinary public tyhpdef symbols. Occupancy-only internals (package not in `includeInternals`) are **not** registered as accessible symbols; they only occupy PHP names for 4338 / 8025.

### Acceptance Criteria

- [ ] `BaseSymbol.IsInternal` property exists and defaults to `false`
- [ ] `BaseSymbol.DefiningProjectPath` property exists and defaults to `null`
- [ ] `SymbolTree.ProjectPath` is set from `tyhp.json` during binder initialization
- [ ] When parsing `internal class Foo {}`, the `ObjectDeclarationSymbol` for `Foo` has `IsInternal = true`
- [ ] When parsing `internal function bar(): void {}`, the `FunctionDeclarationSymbol` for `bar` has `IsInternal = true`
- [ ] When parsing `public class Baz { internal function qux(): void {} }`, the `ObjectMethodSymbol` for `qux` has `IsInternal = true`
- [ ] Symbols loaded from `.tyhpdef` files have `IsInternal = false`
- [ ] All symbols created during binding have `DefiningProjectPath` set to the current project path
- [ ] No binder regressions — existing tests pass

### Dependencies

- **Requires:** Phase 1 (grammar must recognize `internal` so the binder can read it from AST)
- **Provides for:** Phase 3 (checker needs `IsInternal` and `DefiningProjectPath` to validate access)

---

## Phase 3: Checker — Enforce Project Boundary Access Rules




### Phase Overview

Implement checker validations that prevent `internal` members from being accessed outside their defining project. The checker compares the `DefiningProjectPath` of the referenced symbol against the current compilation's project path. If they differ, and the symbol is `internal`, the checker reports an error.

This check applies to **source** symbols (`IsInternal = true`). Package internals merged because of `includeInternals` are tyhpdef symbols with `IsInternal = false`; the checker allows them because they are accessible, not because it special-cases a friend list. Occupancy-only internals are not in the accessible environment, so references to them are unresolved — except a **declaration** of the same emitted PHP name reports 4338 (Phase 5 occupancy).

Additionally, validate modifier combinations: `internal` cannot combine with any other visibility modifier (`public`, `protected`, `private`), and `internal` is only valid in contexts where visibility makes sense.

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Checker/TyhpChecker.cs` — Add `internal` access checks and modifier validation
- `Tyhp/Domain/Exceptions/MessageCode.cs` — Add new error/warning codes (4330–4338)
- `Resources/CLI.TyhpHostedService.en-US.resx` and `Resources/CLI.TyhpHostedService.resx` — Add localized messages

### Implementation Details

#### 3.1 Add MessageCode Values

**File: `Tyhp/Domain/Exceptions/MessageCode.cs`**

Add the following values to the `Checker` region:

```csharp
#region Checker

// ... existing codes 4001–4007 ...

// Internal visibility (4330–4338) — Story 25
CheckerAccessToInternalMember = 4330,
CheckerAccessToInternalType = 4331,
CheckerInternalNotAllowedHere = 4332,
CheckerInternalMemberExposedViaPublicApi = 4333,
CheckerInternalInTraitAlias = 4334,
CheckerPackageInternalsNotProvided = 4335,
CheckerPackageInternalsUnknownPackage = 4336,
TyhpdefInternalsOverlayPathConflict = 4337,
CheckerDeclarationConflictsWithPackageInternal = 4338,

#endregion Checker
```

4335/4336 are **warnings** emitted when loading packages (Phase 5.7), not by the checker walk. 4337 is an **error** on a malformed `extra.tyhp.package`. 4338 is an **error** when this project's source declares a PHP name occupied by a package internal (occupancy, not opt-in). Keep all numeric values in this story's 4330 band, next to 4330–4334 in `MessageCode.cs`.

#### 3.2 Add Localized Messages

**Files:** `Resources/CLI.TyhpHostedService.en-US.resx` and `Resources/CLI.TyhpHostedService.resx`

Short messages follow `CONVENTIONS.md` §2 (present tense, no trailing period, backticks on symbols and config keys):

| Key | Value |
|-----|-------|
| `ERROR_TYHP4330` | Cannot access internal member `{0}` from outside the defining project `{1}` |
| `ERROR_TYHP4331` | Cannot access internal type `{0}` from outside the defining project `{1}` |
| `ERROR_TYHP4332` | The `internal` modifier is not valid in this context |
| `ERROR_TYHP4333` | Internal type `{0}` is exposed via public API member `{1}`; the return/parameter type must be the same visibility or more visible than the member itself |
| `ERROR_TYHP4334` | The `internal` modifier cannot be used in a trait alias visibility declaration; use `public`, `protected`, or `private` instead |
| `WARNING_TYHP4335` | Package `{0}` listed in `includeInternals` does not provide an internals overlay |
| `WARNING_TYHP4336` | Package `{0}` listed in `includeInternals` is not a loaded Tyhp package |
| `ERROR_TYHP4337` | Path `{0}` is listed in both `internalOverlay` and `{1}` of `extra.tyhp.package` |
| `ERROR_TYHP4338` | Cannot declare `{0}`; package `{1}` already emits that PHP name as an internal symbol |

#### 3.3 Checker: Validate Modifier Combinations

**File: `Tyhp/TyhpLang/Checker/TyhpChecker.cs`**

Add a check in the modifier validation logic (near existing `CheckerMultipleVisibilities` / `CheckerMemberModifierConflict` checks):

```
When processing member modifiers:
1. If `internal` is present AND any other visibility modifier (`public`, `protected`, `private`) is present
   → report CheckerMultipleVisibilities (4002, existing code). `internal` is a standalone visibility
   modifier and cannot be combined with other visibility modifiers.
2. If `internal` is present in a context where visibility is not applicable
   (e.g., local variables, loop constructs) → report CheckerInternalNotAllowedHere (4332)
```

`internal` is a standalone visibility modifier. It cannot be combined with `public`, `protected`, or `private`. When `internal` is the only visibility modifier, the PHP output uses `public` for class members and no modifier for top-level declarations (PHP classes/functions are public by default).

#### 3.4 Checker: Validate Access to Internal Symbols

Add a method `CheckInternalAccess(BaseSymbol referencedSymbol, string currentProjectPath)` called whenever the checker resolves a symbol reference (type reference, function call, property access, constant reference, etc.):

```
CheckInternalAccess(referencedSymbol, currentProjectPath):
    if (!referencedSymbol.IsInternal) return;  // not internal, always accessible
                                               // (includes all tyhpdef-loaded symbols: public include,
                                               //  overlay, and opted-in internalOverlay)

    if (referencedSymbol.DefiningProjectPath == null) return;  // defensive; tyhpdef path already filtered

    if (referencedSymbol.DefiningProjectPath == currentProjectPath) return;  // same project, OK

    // Different project, internal symbol → ERROR
    if (referencedSymbol is type declaration):
        report CheckerAccessToInternalType (4331) with symbol name and project name
    else:
        report CheckerAccessToInternalMember (4330) with symbol name and project name
```

This check must be integrated at every point where the checker resolves a reference:

- **Type references:** class names in `extends`, `implements`, type hints, `new` expressions, `instanceof`/`is` checks, generic arguments, return types, parameter types
- **Function calls:** free function calls, static method calls
- **Property access:** `$obj->property`, `Class::$staticProp`
- **Constant access:** `Class::CONSTANT`, global constants
- **Method calls:** `$obj->method()`, `Class::staticMethod()`
- **Trait use:** `use TraitName;`
- **Type alias references:** references to type aliases declared as `internal` in **source**

Opt-in to a package's internals overlay does not change this algorithm. Those symbols never have `IsInternal = true`. Source-to-source access across `tyhp.json` projects still errors (4330/4331); listing a Composer name in `includeInternals` does not grant access to another project's `.tyhp` source.

#### 3.5 Checker: Error on Internal Type Exposure in Public API

When a `public` method, property, or function has a parameter type, return type, or property type that references an internal type that cannot be resolved to a public type (i.e., an internal class, interface, trait, or enum — NOT a type alias), report an error:

```
CheckInternalTypeExposure(memberSymbol):
    if (memberSymbol.IsInternal) return;  // internal member can freely use internal types
    if (memberSymbol is not public) return;  // non-public members don't expose API

    for each type reference in memberSymbol's signature (params, return, property type):
        resolvedType = resolveTypeAliases(referencedType)  // recursively resolve internal aliases
        if (resolvedType.IsInternal && resolvedType is not TypeAlias && resolvedType.DefiningProjectPath == currentProjectPath):
            report ERROR CheckerInternalMemberExposedViaPublicApi (4333)
            message: "Internal type '{resolvedType.Name}' is exposed via public API member '{memberSymbol.Name}'. The return/parameter type must be the same visibility or more visible than the member itself."
```

Note: Internal type ALIASES are resolved by the tyhpdef generator (Phase 5, section 5.3). Only unresolvable internal types (classes, interfaces, traits, enums) produce this error.

4333 applies only to types with `IsInternal = true` declared in **this** compilation. Types loaded from another package's internals overlay are not internal in this sense; using them in this project's public API is allowed (see Architecture — re-export).

#### 3.6 Interaction with Trait Alias Visibility

`internal` cannot appear in trait alias visibility declarations (e.g., `use TraitName { method as internal; }` is a compile error). This is because `internal` is a compile-time-only concept, while trait alias visibility changes (`as public`, `as protected`, `as private`) are runtime PHP constructs. The checker must produce a diagnostic error when `internal` is used in a trait `as` clause.

**Diagnostic code:** `CheckerInternalInTraitAlias = 4334` — "The 'internal' modifier cannot be used in a trait alias visibility declaration. Use 'public', 'protected', or 'private' instead."

### Acceptance Criteria

- [ ] `CheckerAccessToInternalMember` (4330) is reported when accessing an internal method/property/constant from outside the project
- [ ] `CheckerAccessToInternalType` (4331) is reported when referencing an internal class/interface/trait/enum/type-alias from outside the project
- [ ] `CheckerMultipleVisibilities` (4002) is reported for `internal public`, `internal private`, `internal protected` declarations
- [ ] `CheckerInternalNotAllowedHere` (4332) is reported for `internal` in invalid contexts
- [ ] `CheckerInternalMemberExposedViaPublicApi` (4333) error is reported when a public method returns/accepts an unresolvable internal type (class, interface, trait, enum)
- [ ] `internal` members are freely accessible within the same project (no errors)
- [ ] After `includeInternals` loads a package overlay, using those tyhpdef symbols does **not** report 4330/4331
- [ ] Declaring a top-level symbol whose PHP name matches a non-opted-in package internal reports 4338
- [ ] `internal static`, `internal readonly`, `internal abstract` are accepted without errors
- [ ] All existing checker tests pass (no regressions)
- [ ] Error messages include the symbol name and project name for debugging clarity

### Dependencies

- **Requires:** Phase 2 (binder must set `IsInternal` and `DefiningProjectPath` on symbols)
- **Provides for:** Phase 4 (emitter needs to know about `internal` to strip it), Phase 5 (tyhpdef generator needs checker validation complete)

---

## Phase 4: Emitter — Strip `internal`, Emit as `public`




### Phase Overview

The emitter must handle the `internal` modifier by **stripping it** from the output. Since PHP has no `internal` keyword, internal members are emitted with their effective PHP visibility:

- `internal` alone → emitted as `public` (for class members) or nothing (for top-level declarations, since PHP classes/functions are public by default)
- `internal static` → emitted as `public static`
- `internal abstract` → emitted as `abstract` (with default public visibility)
- `internal final` → emitted as `final`
- `internal readonly` → emitted as `public readonly`

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Update modifier emission logic to strip `internal`

### Implementation Details

#### 4.1 Modifier Emission Logic

**File: `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs`**

In the emitter's modifier processing logic, add handling for the `internal` modifier:

```
When emitting modifiers for a declaration:
1. Collect all modifiers from the AST/symbol
2. Remove `internal` from the modifier list
3. If no explicit PHP visibility remains (i.e., `internal` was the only visibility):
   a. For class members: emit `public` (default visibility for internal members)
   b. For top-level declarations: emit nothing (PHP classes/functions are public by default)
4. Emit remaining modifiers in standard PHP order: visibility, static, abstract/final, readonly
```

**Examples of modifier transformation:**

| Tyhp Modifiers | Emitted PHP Modifiers |
|---|---|
| `internal` | `public` (for members) / nothing (for top-level) |
| `internal static` | `public static` |
| `internal readonly` | `public readonly` |
| `internal abstract` | `abstract` |
| `internal final` | `final` |
| `internal static readonly` | `public static readonly` |

#### 4.2 Class Declaration Emission

For top-level declarations with `internal`:

```php
// Tyhp input:
internal class Helper { }

// PHP output:
class Helper { }
// (no modifier — PHP classes are implicitly public)
```

```php
// Tyhp input:
internal abstract class BaseHelper { }

// PHP output:
abstract class BaseHelper { }
```

#### 4.3 No Changes to Method/Property Bodies

The `internal` modifier only affects the declaration line. Method bodies, property initializers, and all other code within internal declarations are emitted identically to their public counterparts.

### Acceptance Criteria

- [ ] `internal class Foo {}` emits as `class Foo {}`
- [ ] `internal function bar(): void {}` emits as `function bar(): void {}`
- [ ] `internal abstract class Baz {}` emits as `abstract class Baz {}`
- [ ] `internal static function qux(): void {}` emits as `function qux(): void {}` (top-level) or `public static function qux(): void {}` (class member)
- [ ] `internal readonly int $x = 0;` emits as `public readonly int $x = 0;`
- [ ] `internal const int C = 1;` emits as `const C = 1;` (top-level) or `public const C = 1;` (class member)
- [ ] No `internal` keyword ever appears in emitted PHP
- [ ] All emitter tests pass (no regressions)

### Dependencies

- **Requires:** Phase 1 (grammar), Phase 2 (binder knows about `internal`)
- **Provides for:** Phase 5 (emitter must be working before tyhpdef generation can rely on the symbol system)

---

## Phase 5: Tyhpdef Generation — Public Exclusion and Internals Overlay




### Phase Overview

Update Track C (`TyhpCodeTyhpdefGenerator`) so a library build writes **two** tyhpdef surfaces:

1. **Public** `package.tyhpdef` — internals excluded (default consumers).
2. **Internals overlay** `package.internal.tyhpdef` — the excluded items, as ordinary tyhpdef, advertised on `extra.tyhp.package` `"internalOverlay"`. Always parsed for PHP name occupancy; merged into the accessible type environment only when the consumer lists the package in `includeInternals`.

Then update package loading: occupancy always; accessible overlay prepend when opted in. Full contract: Architecture → Package Internals Overlay.

### Deliverables

**Modified files:**
- `Tyhp/Domain/Services/TyhpCodeTyhpdefGenerator.cs` — Public `IsInternal` filter; second walk (or dual-write) for the internals overlay; `WriteManifest` emits `"internalOverlay"` when that file exists; delete stale `package.internal.tyhpdef` when there are no internals
- `Tyhp/Config/Project.cs` — Parse top-level `includeInternals` (array of strings, same reader as `tyhpdefInclude`)
- `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.PackageLoading.cs` — Read `"internalOverlay"`; resolve Composer `name`; occupancy parse always; accessible overlay prepend when opted in; warnings 4335/4336; error 4337
- `Tyhp/TyhpLang/Binder/TyhpdefSymbolRegistrar.cs` — Occupancy FQNs participate in 8025 even without accessible merge; project source vs occupancy reports 4338
- `Tyhp/Domain/Services/TyhpdefOverlayApplier.cs` / `--verify` path — Apply `"include"` + `"overlay"` only; never `"internalOverlay"`
- `Tyhp/CLI/GenerateTyhpdefAction.cs` — Track A/B do not write Tyhp internals overlays; do not confuse with `--include-internal` (PHP `@internal`)

> **Dependency note:** Hook the public exclusion and internals overlay into Story 20's Track C emission loop in `TyhpCodeTyhpdefGenerator`. Do not duplicate the walk with a second ad-hoc serializer. If class names differ at implementation time, attach both writers to whatever class owns Track C `package.tyhpdef` emission.

### Implementation Details

#### 5.1 Filter Internal Symbols During Public Tyhpdef Output

When the generator walks the symbol tree for **`package.tyhpdef`**, filter at each level:

```
GeneratePublicTyhpdef(SymbolTree tree):
    for each top-level symbol in tree:
        if (symbol.IsInternal) → SKIP (do not write to public tyhpdef)

        if (symbol is class/interface/trait/enum):
            write class/interface/trait/enum header
            for each member in symbol:
                if (member.IsInternal) → SKIP
                write member declaration
            write closing brace

        if (symbol is function / constant / type alias):
            write declaration
```

Applications with `build.generateTyhpdef` still write only this public `package.tyhpdef` (no `extra.tyhp.package`, no internals overlay).

#### 5.2 Handle Partial Internal Classes (public file)

A class may have a mix of public and internal members. The class is included in the **public** tyhpdef with only non-internal members:

```tyhp
// Source:
public class UserService {
    public function getUser(int $id): User { }
    internal function invalidateCache(): void { }
    public int $timeout = 30;
    internal int $retryCount = 0;
}

// package.tyhpdef:
class UserService {
    public function getUser(int $id): User;
    public int $timeout;
}
```

#### 5.3 Handle Internal Types in Public Signatures

If a public method's signature references an internal type, the **public** tyhpdef must resolve the type to a publicly visible equivalent:

- **Internal type aliases:** Resolve the alias to its underlying type. If the underlying type is also an internal alias, continue resolving recursively until all types in the signature are publicly visible within the public tyhpdef. For example:
  - `internal type UserId = int;` → resolve to `int`
  - `internal type UserMap = array<int, User>;` (where `User` is public) → resolve to `array<int, User>`
  - `internal type UserIds = UserId[];` (where `UserId` is `internal type UserId = int;`) → resolve to `int[]`

- **Internal classes/interfaces/traits/enums in public signatures:** If after full type alias resolution, any type in a public member's signature is still an internal class, interface, trait, or enum, the checker reports `CheckerInternalMemberExposedViaPublicApi` (4333). This is a **compile error**. The developer must make the referenced type public, make the member internal, or change the signature. The generator does not replace those types with `mixed`.

#### 5.4 Exclude Entire Internal Namespaces (public file)

If all declarations within a namespace are internal, omit that namespace block from `package.tyhpdef`. If only some are internal, include the namespace with only the non-internal declarations. The internals overlay (5.5) still contains the internal declarations under that namespace.

#### 5.5 Write `package.internal.tyhpdef`

On a **library** Track C emit, also walk internals:

```
GenerateInternalsOverlay(SymbolTree tree):
    for each top-level symbol in tree:
        if (symbol.IsInternal):
            write a full tyhpdef declaration (same spelling as if it were public)
            continue

        if (symbol is class/interface/trait/enum) AND any member IsInternal:
            write `partial` of that type containing only the internal members
            (internal members spelled as ordinary public tyhpdef members)

        // public-only types with no internal members: skip
```

`private` members are never written. Internal type aliases are declared in this file (`type UserId = int;`); they are not inlined here.

If this walk produces **no** declarations, do not write the file; delete a leftover `package.internal.tyhpdef` in the output directory; omit `"internalOverlay"` from the manifest.

If it produces any declarations, write `package.internal.tyhpdef` beside `package.tyhpdef`. File header must state that every consumer parses the file for PHP name occupancy, and that it is merged into the accessible type environment only when the consumer lists this package in `includeInternals`. Use the same tagless setting as the public file.

Example corresponding to 5.2:

```tyhpdef
<?tyhpdef

partial class UserService {
    public function invalidateCache(): void;
    public int $retryCount;
}
```

A wholly internal class is a full `class` (not `partial`) in this file only.

#### 5.6 Write `"internalOverlay"` on `extra.tyhp.package`

Extend `WriteManifest`:

```json
{
    "include": ["./package.tyhpdef"],
    "exclude": [],
    "internalOverlay": ["./package.internal.tyhpdef"],
    "overlay": [],
    "source": { "tagless": <same as today> }
}
```

Include `"internalOverlay"` if and only if `package.internal.tyhpdef` was written. Never list that path in `"include"` or `"overlay"`. Match `includeInternals` against this package’s `composer.json` `"name"`; do not stamp `"name"` onto `extra.tyhp.package`.

#### 5.7 Load-time: occupancy always, access when opted in

**Config.** `Project` reads `includeInternals` into `List<string>` (dedupe case-insensitive, skip empty). Pass the list on `CompilationOptions` into `TyhpdefLoadContext`.

**Composer name.** When loading a package, resolve the Composer name as that `composer.json` `"name"`. Match against `includeInternals` with ordinal ignore-case comparison.

**Per package:**

1. Load `"include"` as today.
2. Validate `"internalOverlay"` vs `"include"` / `"overlay"`: if any path/glob **string** is duplicated across those arrays, error 4337 (`{1}` is `include` or `overlay`). Do this even when the consumer did not opt in.
3. If `"internalOverlay"` expands to one or more files, parse those files **once**:
   - **Always** register PHP name occupancy (Architecture — occupancy table) into `TyhpdefSymbolRegistrar` / the duplicate-name set. Use the same PHP symbol tables as existing duplicate detection (class-like vs function vs constant).
   - **If** this package's Composer name is in `includeInternals`, also bind the same files as accessible overlays (`isOverlay: true`) **before** `"overlay"`, in array order, lexicographic within a glob, same tagless flag as the rest of the package.
4. If this package's Composer name is in `includeInternals` and `"internalOverlay"` is missing, empty, or expands to zero files → warning 4335, then continue without internals files.
5. Load `"overlay"` as today.
6. After all packages are discovered, each name in `includeInternals` that matched **no** loaded package → warning 4336 once.

**Project source vs occupancy.** When the binder registers a top-level symbol from this project's `.tyhp` files, if that emitted PHP name is occupied by a package internal and that package is **not** in `includeInternals`, report 4338 on the project's declaration. If the package **is** opted in, the symbol is already in the accessible environment — use 3002 / 8025, not 4338.

`--verify` / `TyhpdefOverlayApplier` uses `"overlay"` only (or the stubs-then-hand directory convention when no manifest array is present). It must not merge `"internalOverlay"` into the verified public API. Occupancy is a compile-time duplicate check, not part of `--verify`.

`tyhp overlay create` / `stamp` do not write into internals overlay files and do not append to `"internalOverlay"`.

### Acceptance Criteria

- [ ] Internal classes/functions/constants/type aliases/interfaces/traits/enums are absent from `package.tyhpdef`
- [ ] Internal members of public types are absent from `package.tyhpdef`
- [ ] Internal type aliases in public signatures are resolved to underlying public types in `package.tyhpdef`
- [ ] Public methods referencing unresolvable internal types produce checker error 4333
- [ ] Non-internal members of public types are preserved in `package.tyhpdef`
- [ ] All-internal namespaces are omitted from `package.tyhpdef`
- [ ] When internals exist, `package.internal.tyhpdef` contains wholly internal types as full declarations and internal members of public types as overlay `partial`
- [ ] When internals exist, `extra.tyhp.package` has `"internalOverlay": ["./package.internal.tyhpdef"]` and that path is not in `"include"` or `"overlay"`
- [ ] Track C matches `includeInternals` against publish-directory `composer.json` `"name"` (no extra `"name"` stamp on `extra.tyhp.package`)
- [ ] When internals do not exist, `package.internal.tyhpdef` is absent (stale file deleted) and `"internalOverlay"` is omitted
- [ ] Applications do not emit `package.internal.tyhpdef` or `"internalOverlay"`
- [ ] Consumer without `includeInternals` does not bind internals-overlay symbols into the **accessible** environment (references are unresolved)
- [ ] Consumer without `includeInternals` still registers occupancy: declaring a colliding top-level PHP name is error 4338
- [ ] Two loaded packages occupying the same FQN via internals (or public vs internal) report 8025 without requiring `includeInternals`
- [ ] Consumer with `includeInternals: ["vendor/name"]` merges that package's internals overlay after `"include"` and before `"overlay"`
- [ ] Opted-in overlay symbols type-check as ordinary public tyhpdef (no 4330/4331); a colliding project declaration uses 3002/8025, not 4338
- [ ] Overlay `partial` members on public types do not create occupancy entries (the public type already occupies the class-like name)
- [ ] Type aliases in internals overlays do not occupy PHP names
- [ ] Missing internals overlay for a listed loaded package → warning 4335, not an error
- [ ] Listed name that is not a loaded Tyhp package → warning 4336, not an error
- [ ] Path listed in both `internalOverlay` and `include`/`overlay` → error 4337
- [ ] `--verify` does not apply `"internalOverlay"`
- [ ] Generated public and internals tyhpdef files parse with the tyhpdef parser
- [ ] Round-trip: compile library → generate both files → parse public tyhpdef with no internals; parse internals overlay and see the internal items

### Dependencies

- **Requires:** Phase 2 (binder `IsInternal` flags), Phase 3 (4333 during the library compile; 4335–4338 registered), Story 20 (tyhpdef generator), Story 21 (package overlay load order), existing 3002 / 8025 duplicate machinery
- **Provides for:** Phase 6 (LSP uses the same compilation: access gated, occupancy diagnostics always), Phase 7 (tests)

---

## Phase 6: LSP Support — Filter by Project Boundary




### Phase Overview

Update the Language Server (Story 19) to respect `internal` visibility in autocomplete suggestions, hover information, go-to-definition, and diagnostics. Internal **source** members should be visible when editing files within the defining project and hidden when editing files in a dependent project that did not load them.

When the consumer's `tyhp.json` lists a package in `includeInternals`, that overlay is merged into the accessible type environment as ordinary tyhpdef symbols. Completions, hover, go-to-definition, and workspace symbols show them — the same compilation the checker uses. Hover does **not** display the `internal` keyword for overlay-loaded items (`internal` is not in tyhpdef). Hover still shows `internal` for symbols in the defining project's `.tyhp` source.

Without opt-in, internals overlays still contribute occupancy: declaring a colliding top-level name surfaces 4338 in the editor. Completions for **using** those names stay hidden.

### Deliverables

> **Conditional enhancement — builds on Story 19.** These items layer onto Story 19's existing handlers (under `Tyhp/LanguageServer/Handlers/`) and may be deferred without blocking the core `internal` feature, since tyhpdef exclusion (Phase 5) is the primary external-boundary enforcement. When implemented, modify these Story 19 files:

**Modified files:**
- `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/CompletionHandler.cs` — Filter internal symbols based on project boundary (`IsInternal` + `DefiningProjectPath`)
- `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/HoverHandler.cs` — Show `internal` modifier in hover tooltips
- `Tyhp/LanguageServer/Handlers/DiagnosticsPublisher.cs` — Surface 4330–4338 and warnings 4335–4336 in real-time
- `Tyhp/LanguageServer/Handlers/TextDocumentHandlers/CodeActionHandler.cs` — Quick-fix to make an internal **source** symbol public; 4338 is rename-only (do not offer `includeInternals` — opt-in does not remove the PHP name collision)
- Workspace symbol search handler (Story 19) — Exclude internal symbols from workspace symbol results for external projects

### Implementation Details

#### 6.1 Autocomplete Filtering

When providing completions for a file in project A that depends on project B:

```
GetCompletions(position, currentProjectPath):
    candidates = getAllCandidateSymbols(position)

    for each candidate in candidates:
        if (candidate.IsInternal &&
            candidate.DefiningProjectPath != currentProjectPath):
            remove candidate from results

    return filtered candidates
```

This means:
- When editing a file in the same project, internal source members appear in autocomplete
- When editing a file in a dependent project that did **not** opt in, internals are not completion items for **use** (occupancy-only). Declaring a colliding top-level name still reports 4338
- When editing a file in a dependent project that **did** list the package in `includeInternals`, internals-overlay symbols appear as ordinary tyhpdef completions
- Internal members from the same project's source should be visually distinguished (e.g., with a modifier badge or different icon)

#### 6.2 Hover Information

When hovering over an `internal` symbol, include the modifier in the display:

```
internal class InternalHelper
Defined in: MyLibrary (project)
```

#### 6.3 Go-to-Definition

Go-to-definition should work for internal symbols within the same project. For symbols that exist only in an internals overlay, go-to-definition works when that overlay is **accessible** (opted in; it lands on the overlay tyhpdef). Without opt-in, use-site go-to-definition cannot find them. Occupancy does not make them navigable.

#### 6.4 Real-time Diagnostics

The LSP should surface the checker errors (4330–4334, 4337, 4338) and warnings (4335, 4336) in real-time as the user types, following the same diagnostic reporting mechanism used for other checker/package-load diagnostics.

#### 6.5 Code Actions

Provide a quick-fix code action when the user references an internal symbol from outside the project:

- **"Make '{symbolName}' public"** — If the user controls the source of the symbol, offer to remove the `internal` modifier
- **"Change visibility to public"** — Alternative wording

### Acceptance Criteria

- [ ] Autocomplete hides internal source symbols from external projects that did not opt in
- [ ] Autocomplete shows internal source symbols within the same project
- [ ] Autocomplete shows internals-overlay symbols when `includeInternals` loaded that package
- [ ] Autocomplete does **not** offer occupancy-only internals for use; declaring a colliding name still shows 4338
- [ ] Hover tooltip displays `internal` modifier for source symbols; overlay-loaded items hover as ordinary tyhpdef
- [ ] Go-to-definition works for internal symbols within the same project and for **accessible** internals-overlay symbols
- [ ] Diagnostics 4330–4338 appear in the editor
- [ ] Quick-fix code action is available for internal access violations (when applicable); 4338 is not "add includeInternals"
- [ ] No LSP regressions — existing completion, hover, and diagnostic features work correctly

### Dependencies

- **Requires:** Phase 3 (checker errors defined), Story 19 (LSP infrastructure)
- **Provides for:** Phase 7 (testing includes LSP behavior verification)

---

## Phase 7: Testing — Comprehensive Coverage




### Phase Overview

Create comprehensive tests covering all aspects of the `internal` modifier: grammar parsing, binder symbol tracking, checker validation (positive and negative cases), emitter output, tyhpdef generation exclusion, and LSP behavior.

### Deliverables

**New test files:**
- Grammar tests for `internal` keyword parsing
- Binder tests for `IsInternal` propagation
- Checker tests for access validation and modifier combination validation
- Emitter tests for `internal` stripping
- Tyhpdef generation tests for internal exclusion
- LSP tests for completion filtering
- End-to-end integration tests

**New example files:**
- `Examples/InternalVisibility.tyhp` — Comprehensive example of `internal` usage
- `Examples/InternalVisibility.php` — Expected PHP output

### Implementation Details

#### 7.1 Grammar Tests

Test that the parser correctly handles `internal` in all valid positions:

```
Test cases:
- internal class Foo {}                          → parses with internal modifier
- internal interface Bar {}                      → parses with internal modifier
- internal trait Baz {}                          → parses with internal modifier
- internal enum Qux { case A; }                  → parses with internal modifier
- internal function helper(): void {}            → parses with internal modifier
- internal const int MAX = 10;                   → parses with internal modifier
- internal type UserId = int;                    → parses with internal modifier
- class Foo { internal function bar(): void {} } → parses member with internal
- class Foo { internal int $x = 0; }             → parses property with internal
- class Foo { internal const int C = 1; }        → parses constant with internal
- internal abstract class Foo {}                 → parses combined modifiers
- internal final class Foo {}                    → parses combined modifiers
- internal static function foo(): void {}        → parses combined modifiers
```

Negative test cases (syntax errors):
```
- internal internal class Foo {}                 → duplicate modifier error
- <?php internal class Foo {}                    → not a keyword in PHP mode
```

#### 7.2 Binder Tests

Test that `IsInternal` and `DefiningProjectPath` are correctly set:

```
Test cases:
- internal class → symbol.IsInternal == true
- public class → symbol.IsInternal == false
- class (no modifier) → symbol.IsInternal == false
- internal method → symbol.IsInternal == true
- symbols from tyhpdef → symbol.IsInternal == false
- symbols from an internals overlay → symbol.IsInternal == false (load gate, not a second visibility)
- all symbols from this project's `.tyhp` → DefiningProjectPath matches current project
```

#### 7.3 Checker Tests — Positive Cases (Access Allowed)

```
Test cases:
- Same project, access internal class → no error
- Same project, access internal method → no error
- Same project, access internal function → no error
- Same project, access internal constant → no error
- Same project, access internal type alias → no error
- Same project, internal static method → no error
- Public method with internal type alias parameter → type alias resolved in tyhpdef, no error
- Consumer with `includeInternals` matching a package that ships an overlay → access overlay types/members, no 4330/4331
```

#### 7.4 Checker Tests — Negative Cases (Access Denied)

"Different project" here means compiling against the other project's **source** (shared workspace / source reference), where `IsInternal` is still true. A tyhpdef consumer without `includeInternals` fails with unresolved names instead, because the symbols are not loaded.

```
Test cases:
- Different project, access internal class → CheckerAccessToInternalType (4331)
- Different project, access internal method → CheckerAccessToInternalMember (4330)
- Different project, access internal function → CheckerAccessToInternalMember (4330)
- Different project, access internal constant → CheckerAccessToInternalMember (4330)
- Different project, access internal type alias → CheckerAccessToInternalType (4331)
- Different project, extend internal class → CheckerAccessToInternalType (4331)
- Different project, implement internal interface → CheckerAccessToInternalType (4331)
- Different project, use internal trait → CheckerAccessToInternalType (4331)
- internal + any other visibility (public, private, protected) → CheckerMultipleVisibilities (4002)
- internal on local variable → CheckerInternalNotAllowedHere (4332)
- Public method with internal class/interface return type → CheckerInternalMemberExposedViaPublicApi (4333) error
```

#### 7.5 Emitter Tests

```
Test cases:
- internal class → emits class (no modifier)
- internal function → emits function (no modifier)
- internal method → emits public method
- internal property → emits public property
- internal static method → emits public static method
- internal abstract class → emits abstract class
- internal readonly property → emits public readonly property
- Verify: the word "internal" never appears in ANY emitted PHP output
```

#### 7.6 Tyhpdef Generation Tests

```
Test cases:
- Compile library with internal class → public tyhpdef does not contain the class; internals overlay does
- Compile library with internal function → public tyhpdef does not contain the function; internals overlay does
- Compile library with mixed public/internal class members → public tyhpdef contains only public members; internals overlay is `partial` with the internal members
- Compile library with internal constant / type alias → public tyhpdef omits them; internals overlay declares them
- Compile library with all-internal namespace → namespace omitted from public tyhpdef; present in internals overlay
- Public method with internal type alias parameter → public tyhpdef resolves alias to underlying type; internals overlay still declares the alias
- Library with no internal symbols → no `package.internal.tyhpdef`, no `"internalOverlay"` key, leftover file deleted
- Library with internals → `extra.tyhp.package` has `"internalOverlay": ["./package.internal.tyhpdef"]` not listed in `"include"` or `"overlay"`
- Application `generateTyhpdef` → no internals overlay, no `extra.tyhp.package`
- Consumer without `includeInternals` → internals-overlay symbols are unresolved for **use**
- Consumer without `includeInternals` that declares `class InternalHelper` (same namespace as the package internal) → error 4338
- Consumer without `includeInternals` that only adds members via `partial` is N/A in `.tyhp` source; declaring a **new** top-level class that matches an internal class is 4338; declaring a class that matches a **public** package type is existing duplicate (3002/8025)
- Two packages both occupying `\\Foo` via internals (or one public + one internal) → 8025 without opt-in
- Consumer `includeInternals: ["the/package"]` with overlay present → symbols resolve; no 4330/4331; colliding project declaration is 3002/8025, not 4338
- Internals overlay `partial` on a public class does not 4338 a consumer class of a **different** name
- Type alias in internals overlay does not 4338 a consumer class of the same short name
- Consumer `includeInternals` for a loaded package with no overlay → warning 4335, build continues
- Consumer `includeInternals` for an unknown name → warning 4336, build continues
- Manifest lists the same glob in `internalOverlay` and `overlay` → error 4337
- Opt-in is not transitive (downstream project without `includeInternals` still cannot see the original package's internals)
- Generated tyhpdef files parse without errors
```

#### 7.7 End-to-End Integration Tests

Create a two-project test scenario:

**Project A (Library):**
```tyhp
<?tyhp

namespace MyLib;

public class Calculator {
    public function add(int $a, int $b): int {
        return $this->doAdd($a, $b);
    }

    internal function doAdd(int $a, int $b): int {
        return $a + $b;
    }

    internal int $precision = 2;
}

internal class CalculatorImpl {
    // implementation details
}

internal function helperFn(): void {
    // ...
}
```

**Project A's generated public tyhpdef:**
```tyhpdef
<?tyhpdef

namespace MyLib;

class Calculator {
    public function add(int $a, int $b): int;
    // doAdd and $precision are excluded
}
// CalculatorImpl is excluded
// helperFn is excluded
```

**Project A's generated internals overlay** (`package.internal.tyhpdef`):
```tyhpdef
<?tyhpdef

namespace MyLib;

partial class Calculator {
    public function doAdd(int $a, int $b): int;
    public int $precision;
}

class CalculatorImpl {
    // implementation details
}

function helperFn(): void;
```

**Project B (Consumer, no `includeInternals`) — should compile with unresolved symbols, not 4330/4331:**
```tyhp
<?tyhp

use MyLib\Calculator;
use MyLib\CalculatorImpl;  // ERROR: type not found (excluded from public tyhpdef)

$calc = new Calculator();
$calc->add(1, 2);         // OK
$calc->doAdd(1, 2);       // ERROR: member not found
$calc->precision;          // ERROR: member not found
helperFn();                // ERROR: symbol not found
```

**Project B with `"includeInternals": ["my-lib/package"]` (Composer name of A) — should compile:**
```tyhp
<?tyhp

use MyLib\Calculator;
use MyLib\CalculatorImpl;  // OK — loaded from internals overlay

$calc = new Calculator();
$calc->add(1, 2);         // OK
$calc->doAdd(1, 2);       // OK
$calc->precision;          // OK
helperFn();                // OK
```

4330/4331 still apply when Project B compiles against Project A's **source** (multi-project workspace) rather than A's published tyhpdef.

#### 7.8 Example File

**New file: `Examples/InternalVisibility.tyhp`**

```tyhp
<?tyhp

namespace App\Services;

// Public API — visible to external consumers
public class UserService {
    public function getUser(int $id): User {
        $cache = $this->checkCache($id);
        if ($cache !== null) {
            return $cache;
        }
        return $this->fetchFromDb($id);
    }

    public function listUsers(): array {
        return $this->queryAll();
    }

    // Internal methods — implementation details hidden from consumers
    internal function checkCache(int $id): ?User {
        return self::$cache[$id] ?? null;
    }

    internal function fetchFromDb(int $id): User {
        // database logic
    }

    internal function queryAll(): array {
        // query logic
    }

    // Internal state
    internal static array $cache = [];
}

// Internal helper — not visible to consumers
internal class UserQueryBuilder {
    internal function buildQuery(string $table): string {
        return "SELECT * FROM {$table}";
    }
}

// Internal constant
internal const string CACHE_PREFIX = 'user_';

// Internal type alias
internal type UserMap = array<int, User>;
```

**New file: `Examples/InternalVisibility.php`** (expected output)

```php
<?php

namespace App\Services;

class UserService {
    public function getUser(int $id): User {
        $cache = $this->checkCache($id);
        if ($cache !== null) {
            return $cache;
        }
        return $this->fetchFromDb($id);
    }

    public function listUsers(): array {
        return $this->queryAll();
    }

    public function checkCache(int $id): ?User {
        return self::$cache[$id] ?? null;
    }

    public function fetchFromDb(int $id): User {
        // database logic
    }

    public function queryAll(): array {
        // query logic
    }

    public static array $cache = [];
}

class UserQueryBuilder {
    public function buildQuery(string $table): string {
        return "SELECT * FROM {$table}";
    }
}

const CACHE_PREFIX = 'user_';
```

### Acceptance Criteria

- [ ] All grammar parse tests pass
- [ ] All binder symbol tracking tests pass
- [ ] All checker positive cases (access allowed) pass
- [ ] All checker negative cases (access denied, modifier conflicts) pass and produce correct error codes
- [ ] All emitter tests pass — `internal` never appears in output
- [ ] All tyhpdef generation exclusion tests pass
- [ ] Internals overlay generation and `includeInternals` load tests pass (including 4335/4336 warnings and 4337)
- [ ] End-to-end two-project test demonstrates public exclusion, opt-in load, and source-to-source 4330/4331
- [ ] `Examples/InternalVisibility.tyhp` compiles without errors
- [ ] `Examples/InternalVisibility.php` matches expected emitted output
- [ ] No regressions in any existing test suites

### Dependencies

- **Requires:** Phases 1–6 (all implementation phases must be complete)
- **Provides:** Complete verification that the `internal` modifier works correctly across the entire compilation pipeline

---

## Phase 8: User documentation and AIDevGuide

### Phase Overview

`internal` is a new modifier authors write, so it needs a full language page rather than a note. The part readers will get wrong is that it is a **compile-time** boundary: the emitted PHP says `public`, so nothing stops PHP code from calling an internal member at runtime. Say that plainly instead of letting readers assume PHP-level enforcement.

`docs/content/tyhp_3100_internalModifier.md` already exists as the page for this feature; this phase makes it describe shipped behavior.

### Pages to update (create a sibling page only if an existing page cannot hold the topic)

| Page | What this story adds |
|------|----------------------|
| `docs/content/tyhp_3100_internalModifier.md` | Where `internal` is allowed, what "project boundary" means, which modifier combinations conflict, that emitted PHP is `public`, that internal items are omitted from the public tyhpdef, that a consumer opts in with `includeInternals`, and that all loaded packages' internals occupy PHP names for duplicate detection (4338) even without opt-in. Flip `status.state` to `complete` and delete the `:::warning Not in this alpha` block |
| `docs/content/tyhpdef_classes.md` / `docs/content/cli_tyhpdefGeneration.md` | Public `package.tyhpdef` omits internals; libraries also write `package.internal.tyhpdef` and `"internalOverlay"` on `extra.tyhp.package`; `--include-internal` (PHP `@internal`) is a different flag |
| `docs/content/tyhpdef_overlays.md` | `"internalOverlay"` is not `"overlay"`; occupancy parse is always on; accessible merge is after `"include"` and before `"overlay"` when opted in; `--verify` ignores it |
| `docs/content/project_optionsList.md` | Document `includeInternals` (`array<string>`, Composer `name` values). Add it to the complete `tyhp.json` example |
| `docs/content/project_composerPackages.md` | Default consumers cannot **use** internals; opt in with `includeInternals`; missing overlay is a warning; PHP names from internals still occupy for 4338 / 8025 |
| `CONVENTIONS.md` | Add `includeInternals` to §4; mention `"internalOverlay"` in §6 overlay load order |
| `docs/content/tyhp_2900_lostFunctionality.md` | The runtime gap: PHP callers are not blocked |
| `docs/content/diagnostics_reference.md` | 4330–4338 |
| `docs/content/cli_languageServer.md` | Completion/hover for source `internal`; opted-in overlay symbols appear as ordinary tyhpdef; occupancy-only internals are not use-completions; 4338 on colliding declarations |
| `docs/content/quickref.md` / `docs/content/faq_tyhpSyntax.md` | One `internal` line; why it is not `private` and not PHP-enforced; pointer to `includeInternals` |

### AIDevGuide

`AIDevGuide/` is the bundle an agent loads to write Tyhp applications, and it is **regenerated** from the prompt in `AIDevGuide/REGEN.md`. A claim corrected only in a section file comes back the next time the bundle is regenerated, so update the prompt as well as the section.

| File | What this story changes |
|---|---|
| `AIDevGuide/guide/22-declarations.md` | Currently states "**`internal` visibility is not in the language** — don't use it." Replace with the shipped rules |
| `AIDevGuide/guide/28-availability-gotchas.md` | Remove `internal` visibility from the "Not in the language yet (don't use)" list and add it to "use freely" |
| `AIDevGuide/guide/23-tyhpdef.md` | Public tyhpdef omits internals; `includeInternals` + `"internalOverlay"` is the opt-in |
| `AIDevGuide/guide/29-php-mapping.md` | `internal` → `public` in emitted PHP |
| `AIDevGuide/QUICK_GUIDE.md` | The `internal` line currently reads "⚠️ not in the language yet"; point it at the declarations section instead |
| `AIDevGuide/REGEN.md` | Prompt item 22 explicitly asks for "`internal` not in language"; correct it, or the next regeneration reinstates the wrong claim in all three files above |

### Acceptance Criteria

- [ ] `tyhp_3100_internalModifier.md` describes shipped behavior, including that emitted PHP is `public`
- [ ] That page's front matter reads `state: complete` and no longer warns that the feature is absent from this alpha
- [ ] Docs state that internal items are absent from the **public** tyhpdef, that `includeInternals` merges `"internalOverlay"` for access, and that occupancy (4338) applies even without opt-in
- [ ] `includeInternals` is in `project_optionsList.md` and `CONVENTIONS.md` §4; `"internalOverlay"` is in overlay docs and `CONVENTIONS.md` §6
- [ ] New diagnostics (4330–4338) are in `diagnostics_reference.md`
- [ ] `guide/22-declarations.md`, `guide/28-availability-gotchas.md`, and `QUICK_GUIDE.md` no longer say `internal` is unavailable
- [ ] `AIDevGuide/REGEN.md` item 22 no longer asks for the "not in language" claim

### Dependencies

- **Requires:** Phases 1–7 (the shipped feature and its verified behavior)
- **Provides:** Published documentation and agent-facing guide entries for `internal`

---

## New MessageCode Values Summary

```csharp
// Internal visibility (4330–4338) — Story 25
CheckerAccessToInternalMember = 4330,
CheckerAccessToInternalType = 4331,
CheckerInternalNotAllowedHere = 4332,
CheckerInternalMemberExposedViaPublicApi = 4333,
CheckerInternalInTraitAlias = 4334,
CheckerPackageInternalsNotProvided = 4335,       // warning
CheckerPackageInternalsUnknownPackage = 4336,    // warning
TyhpdefInternalsOverlayPathConflict = 4337,      // error
CheckerDeclarationConflictsWithPackageInternal = 4338, // error
```

Localized short messages are in Phase 3.2. Do not restate code ranges elsewhere; `MessageCode.cs` is authoritative.

---

## Cross-Phase Consistency Checklist

After completing all phases, verify these cross-cutting concerns:

1. **Grammar consistency** — The `internal` keyword is recognized in exactly the same positions in Tyhp mode (not Tyhpdef mode)
2. **Binder-to-checker data flow** — Every symbol that can be `internal` has its `IsInternal` and `DefiningProjectPath` correctly set before the checker runs
3. **Checker-to-emitter data flow** — The emitter correctly reads modifier information and strips `internal` regardless of whether the checker has run (defensive coding)
4. **Tyhpdef round-trip** — Public generate → parse → internal items absent (`IsInternal = false` on remaining symbols). Internals overlay generate → parse → those declarations exist as ordinary tyhpdef. Consumer without `includeInternals` does not get **accessible** overlay symbols but **does** occupy PHP names (4338 on colliding declarations); consumer with `includeInternals` gets accessible merge.
5. **Error code consistency** — `MessageCode.cs` enum values, `.resx` entries, and documented codes (4330–4338) are all in sync
6. **LSP consistency** — LSP uses the same compilation as the checker: source `internal` is filtered by project boundary; opted-in overlay symbols appear because they are accessible; occupancy-only symbols are not completion items but 4338 still appears on colliding declarations
7. **Config keys** — `includeInternals` (tyhp.json) and `internalOverlay` (extra.tyhp.package) match `CONVENTIONS.md` and `Project.cs` / the manifest reader
8. **Example file correctness** — `Examples/InternalVisibility.tyhp` compiles to `Examples/InternalVisibility.php` exactly
9. **No PHP output leakage** — The string `internal` never appears as a keyword in any emitted PHP file

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the `internal` visibility modifier implementation. Steps can be skipped, reordered, or modified as needed. You need: a built `tyhp` binary and PHP 8.4+ installed.

### Step 1: Verify the Build Compiles

If grammar files were modified, regenerate the parser first:

```bash
cd /path/to/tyhp
./compile_grammar.sh
dotnet clean && dotnet restore && dotnet build
```

Confirm zero errors. The regenerated ANTLR parser files and new checker/emitter logic should compile cleanly.

### Step 2: Set Up Two Test Projects (Library + Consumer)

The `internal` modifier is about project boundaries, so testing requires two separate projects.

**Create the library project:**

```bash
mkdir -p /tmp/tyhp-internal-test/my-lib/src
cd /tmp/tyhp-internal-test/my-lib
```

Create `tyhp.json`:

```json
{
  "type": "library",
  "include": ["src/**/*.tyhp"],
  "output": {
    "path": "build/",
    "phpVersion": "8.4",
    "strictTypes": true
  }
}
```

Create `composer.json` (needed so `includeInternals` can match `"name"`):

```json
{
  "name": "example/my-lib"
}
```

**Create the consumer project:**

```bash
mkdir -p /tmp/tyhp-internal-test/my-app/src
cd /tmp/tyhp-internal-test/my-app
```

Create `tyhp.json`:

```json
{
  "include": ["src/**/*.tyhp"],
  "tyhpdefInclude": ["../my-lib/build/composer.json"],
  "output": {
    "path": "build/",
    "phpVersion": "8.4",
    "strictTypes": true
  }
}
```

### Step 3: Test Internal Class — Emitter Output

Create `/tmp/tyhp-internal-test/my-lib/src/Helpers.tyhp`:

```tyhp
<?tyhp

namespace MyLib;

internal class InternalHelper {
    public function compute(int $x): int {
        return $x * 2;
    }
}

public class PublicService {
    internal function resetState(): void {
        // internal method
    }

    public function process(): string {
        $this->resetState();
        return "processed";
    }

    internal int $retryCount = 0;
    public int $timeout = 30;
}

internal function helperFn(): void {
    echo "internal function\n";
}

internal const int MAX_RETRIES = 3;

internal type UserId = int;
```

Compile the library:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Build succeeds. Inspect `build/Helpers.php`:

- `InternalHelper` should be emitted as `class InternalHelper` (no `internal` keyword, no visibility modifier on the class itself).
- `resetState()` should be emitted as `public function resetState()` (internal stripped, emitted as public).
- `$retryCount` should be emitted as `public int $retryCount = 0`.
- `helperFn()` should be emitted as `function helperFn()` (no modifier).
- `MAX_RETRIES` should be emitted as `const MAX_RETRIES = 3`.
- The word `internal` should **never** appear anywhere in the PHP output.

Verify no leakage:

```bash
grep -r "internal" /tmp/tyhp-internal-test/my-lib/build/
```

**Expected:** No matches (or only matches within string literals/comments, NOT as a PHP keyword).

### Step 4: Test Internal Access Within Same Project (Should Succeed)

Create `/tmp/tyhp-internal-test/my-lib/src/SameProjectUsage.tyhp`:

```tyhp
<?tyhp

namespace MyLib;

class InternalConsumer {
    public function useInternal(): void {
        $helper = new InternalHelper();
        $result = $helper->compute(5);
        echo $result . "\n";

        helperFn();
        $max = MAX_RETRIES;

        $svc = new PublicService();
        $svc->resetState();
        $count = $svc->retryCount;
    }
}
```

Compile:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Build succeeds with zero errors. All internal symbols are accessible within the same project.

### Step 5: Test Tyhpdef Generation — Public Exclusion and Internals Overlay

Inspect the library build output:

```bash
cat /tmp/tyhp-internal-test/my-lib/build/composer.json
cat /tmp/tyhp-internal-test/my-lib/build/package.tyhpdef
cat /tmp/tyhp-internal-test/my-lib/build/package.internal.tyhpdef
```

**Expected `extra.tyhp.package` on that `composer.json`:**
- `"include"` contains `./package.tyhpdef`
- `"internalOverlay"` is `["./package.internal.tyhpdef"]`
- That path is **not** in `"overlay"` or `"include"`
- Composer package name for `includeInternals` is `composer.json` `"name"` (`example/my-lib`)

**Expected `package.tyhpdef` (public):**
- `InternalHelper` class is **not present**.
- `helperFn` function is **not present**.
- `MAX_RETRIES` constant is **not present**.
- `UserId` type alias is **not present**.
- `PublicService` class **is present**, but:
  - `resetState()` method is **not present**.
  - `$retryCount` property is **not present**.
  - `process()` method **is present**.
  - `$timeout` property **is present**.

**Expected `package.internal.tyhpdef`:**
- `InternalHelper`, `helperFn`, `MAX_RETRIES`, and `UserId` **are present** as ordinary tyhpdef (no `internal` keyword).
- `partial class PublicService` contains `resetState()` and `$retryCount`.

### Step 6: Test Cross-Project Access Without Opt-In (Should Fail)

Create `/tmp/tyhp-internal-test/my-app/src/Consumer.tyhp`:

```tyhp
<?tyhp

namespace App;

use MyLib\PublicService;
use MyLib\InternalHelper;

$svc = new PublicService();
$svc->process();

// These should all fail — internals overlay is not in the accessible environment:
$svc->resetState();
$count = $svc->retryCount;

$helper = new InternalHelper();
```

Compile the consumer project:

```bash
cd /tmp/tyhp-internal-test/my-app
dotnet run --project /path/to/tyhp -- build
```

**Expected:** unresolved type/member diagnostics (the symbols are not accessible). **Not** 4330/4331 — those apply to source-to-source project references. Occupancy is registered: do not declare `class InternalHelper` in this step.

### Step 6b: Test Opt-In via `includeInternals` (Should Succeed)

Add to the consumer `tyhp.json`:

```json
"includeInternals": ["example/my-lib"]
```

Recompile the consumer.

**Expected:** build succeeds. `InternalHelper`, `resetState()`, and `$retryCount` resolve. No 4330/4331.

Then temporarily set `"includeInternals": ["example/does-not-exist"]` and compile.

**Expected:** warning 4336; internals still not accessible. Restore `example/my-lib` when done.

### Step 6c: Test Occupancy Collision Without Opt-In (Should Fail)

Remove `includeInternals` (or leave it empty). Replace the consumer source with:

```tyhp
<?tyhp

namespace MyLib;

class InternalHelper {
    public function oops(): void {}
}
```

Compile.

**Expected:** error 4338 (`CheckerDeclarationConflictsWithPackageInternal`) — the package already emits `MyLib\InternalHelper` as public PHP. Adding `includeInternals` must **not** make this compile; rename the consumer class instead.

### Step 7: Test Modifier Combination Errors

Create `/tmp/tyhp-internal-test/my-lib/src/ModifierErrors.tyhp`:

```tyhp
<?tyhp

namespace MyLib\Errors;

// ERROR: Cannot combine internal with other visibility modifiers
internal public class BadClass1 {}

class BadClass2 {
    // ERROR: Cannot combine internal with private
    internal private function badMethod(): void {}

    // ERROR: Cannot combine internal with protected
    internal protected int $badProp = 0;
}
```

Compile:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Error 4002 (`CheckerMultipleVisibilities`) for each `internal + public/private/protected` combination.

### Step 8: Test Valid Modifier Combinations

Create `/tmp/tyhp-internal-test/my-lib/src/ValidModifiers.tyhp`:

```tyhp
<?tyhp

namespace MyLib\Valid;

internal abstract class InternalAbstractBase {
    internal abstract function doWork(): void;
}

internal final class InternalFinalHelper {
    internal static function create(): static {
        return new static();
    }

    internal readonly string $id = "abc";
}

internal interface InternalContract {
    public function execute(): void;
}

internal enum InternalStatus {
    case Active;
    case Inactive;
}

internal trait InternalLogging {
    public function log(string $msg): void {
        echo $msg . "\n";
    }
}
```

Compile:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Build succeeds with zero errors. `internal` combines freely with `abstract`, `final`, `static`, `readonly`.

Inspect the output PHP:
- `internal abstract class` → `abstract class`
- `internal final class` → `final class`
- `internal static function` → `public static function`
- `internal readonly string` → `public readonly string`
- `internal interface` → `interface`
- `internal enum` → `enum`
- `internal trait` → `trait`

### Step 9: Test Internal Type Exposed via Public API Error

Create `/tmp/tyhp-internal-test/my-lib/src/ExposureError.tyhp`:

```tyhp
<?tyhp

namespace MyLib\Exposure;

internal class InternalData {
    public string $value;
}

// ERROR: public method returns internal type
public class PublicApi {
    public function getData(): InternalData {
        return new InternalData();
    }
}
```

Compile:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Error 4333 (`CheckerInternalMemberExposedViaPublicApi`) — "Internal type 'InternalData' is exposed via public API member 'getData'."

### Step 10: Test Internal Type Alias Resolution in Tyhpdef

Create `/tmp/tyhp-internal-test/my-lib/src/AliasResolution.tyhp`:

```tyhp
<?tyhp

namespace MyLib\Aliases;

internal type UserId = int;

public class UserService {
    // Uses internal type alias in public signature — should resolve to `int` in tyhpdef
    public function getUser(UserId $id): string {
        return "User {$id}";
    }
}
```

Compile and inspect the generated tyhpdef:

**Expected:** In `package.tyhpdef`, `getUser` should have its parameter typed as `int` (the alias `UserId` resolved to its underlying type), NOT as `UserId`. In `package.internal.tyhpdef`, `type UserId = int;` is declared so opted-in consumers can use the alias name.

### Step 11: Test Trait Alias Visibility Error

Create `/tmp/tyhp-internal-test/my-lib/src/TraitAliasError.tyhp`:

```tyhp
<?tyhp

namespace MyLib\TraitTest;

trait MyTrait {
    public function hello(): void {}
}

class MyClass {
    use MyTrait {
        hello as internal;
    }
}
```

Compile:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
```

**Expected:** Error 4334 (`CheckerInternalInTraitAlias`) — "The 'internal' modifier cannot be used in a trait alias visibility declaration."

### Step 12: Test PHP Mode and Tyhpdef Mode Rejection

Verify that `internal` is NOT recognized as a keyword outside Tyhp mode.

Create `/tmp/tyhp-internal-test/my-lib/src/PhpMode.tyhp`:

```tyhp
<?php

// In raw PHP mode, "internal" should be treated as an identifier, not a keyword
$internal = "some value";
echo $internal;
```

Compile:

**Expected:** Compiles without errors. `$internal` is treated as a variable name, not a keyword.

### Step 13: Verify Runtime Behavior

Create `/tmp/tyhp-internal-test/my-lib/src/RuntimeTest.tyhp`:

```tyhp
<?tyhp

namespace MyLib\Runtime;

internal class Calculator {
    public function add(int $a, int $b): int {
        return $a + $b;
    }
}

public class MathService {
    internal Calculator $calc;

    public function __construct() {
        $this->calc = new Calculator();
    }

    public function sum(int $a, int $b): int {
        return $this->calc->add($a, $b);
    }
}

$svc = new MathService();
echo "3 + 4 = " . $svc->sum(3, 4) . "\n";
```

Compile and run:

```bash
cd /tmp/tyhp-internal-test/my-lib
dotnet run --project /path/to/tyhp -- build
php build/RuntimeTest.php
```

**Expected output:**

```
3 + 4 = 7
```

The `internal` modifier is compile-time only — at runtime, everything is public and works normally.

### Step 14: Verify LSP Behavior (if Story 19 is Complete)

Open the library project in VS Code with the Tyhp extension:

1. In `SameProjectUsage.tyhp`, type `new Internal` — autocomplete should suggest `InternalHelper`.
2. Hover over `InternalHelper` — tooltip should show the `internal` modifier.
3. In the consumer project **without** `includeInternals`, `InternalHelper` should NOT appear in autocomplete.
4. Typing `$svc->` in that consumer should NOT suggest `resetState()` or `$retryCount`.
5. After adding `"includeInternals": ["example/my-lib"]`, `InternalHelper` and `resetState()` should appear as ordinary tyhpdef completions (hover does not show the `internal` keyword).
6. Without `includeInternals`, declaring `namespace MyLib; class InternalHelper {}` in the consumer should show diagnostic 4338. Autocomplete for `new Internal` should still not suggest the package internal.

### Step 15: Clean Up

```bash
rm -rf /tmp/tyhp-internal-test
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
