# Implementation Plan: Story 20 — Tyhpdef Generator (C# CLI Integration)

> **Roadmap position:** Story 20 — **Tier 2 — DX & Ecosystem**
> **Direct dependencies (new numbering):** 01, 02
> **Renumbered from:** legacy Story 10
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 20 of the Tyhp compiler TODO
> **Branch:** TBD
> **Prerequisites:** Story 01 (diagnostic system, CompilationService, BuildAction skeleton), Story 02 (binder — understanding of type system and symbol model)
> **Forward dependency:** Story 20 **Phase 8** (multi-target gated generation) requires **Story 20.5**
> **Key files:** `Tyhp/CLI/GenerateTyhpdefAction.cs`, `tools/genTyhpdef.php` (Track A docs reference — port, do not exec), `DebugProject/genTyhpdef.php`

### Decisions locked (2026-08-20)

These supersede earlier wording in this document wherever they conflict.

1. **CLI package flag is `--package-path`.** Remove `--composer-package` entirely (Story 13 help and `ActionConfigProvider.ValueTakingFlags`). No alias.
2. **Default CLI output directory is `tyhpdef/` (Tracks A and B only).** If a Tyhp project is open, `{projectRoot}/tyhpdef/`. If not, `{cwd}/tyhpdef/`. `--output=<path>` overrides the directory. `--output-file=<path>` writes a single explicit file (relative paths resolve against the output directory unless absolute). Track C never writes here.
3. **This story delivers Layer 1 and Layer 2.** Layer 1 = Reflection baseline for PHP extensions (Track A). Layer 2 = stub harvest into generated overlay files. Layer 3 hand overlays and compiler overlay *load* remain Story 21, except `--verify` must apply overlays to compare **final** forms (see 9).
4. **Track C is compile-time only** (`tyhp build`, after check, before optimize). It is not a CLI parse of `.tyhp` files into `tyhpdef/`.
   - `"type": "library"`: **always** write `package.tyhpdef` **and** `package.tyhp.json` in the build output directory. **`build.generateTyhpdef` is ignored.**
   - `"type": "application"`: if `build.generateTyhpdef = true`, write **only** `package.tyhpdef` (no `package.tyhp.json`). If false/unset, write nothing.
5. **Track A is PHP extensions only.** **Track B** is `--source` (PHP files) and `--package-path` (`composer.json` autoload → PHP files). `--include-dev` also reads `autoload-dev`. Do **not** use PHP Reflection for vendor packages.
6. **Track A JSON is a real schema**, one PHP process per extension (stdin or temp `.php`, not `php -r` one-liners). See Phase 2.
7. **`TyhpdefDeclaration` IR** is complete enough for A/B/C and Phase 8 (Phase 5).
8. **Do not implement other stories' compiler features** (vendor discovery, `8025`, Story 25 `internal`, Story 28 generic defaults). `--verify` **does** apply overlays (same semantics as Story 21: array order, last wins, `omit` hides) so it can check final forms; that is not a substitute for shipping overlay load in the binder (Story 21).
9. **`--verify` checks final forms after overlays.** Generate/load Layer 1, apply every overlay listed (stubs and hand, including Layer 2 if present in `"overlay"`), then compare that merged API to the golden PHP API. Fully **missing** golden symbols → fail (`7506`). Symbols **`omit`ted by an overlay** → not a fail. Extra overlay-only members are allowed. Compatible generics/overloads/structs that still cover the PHP signature pass.
10. **CLI `tyhpdef/` layout** uses `--split` (see Key Design Decisions). Default is one `.tyhpdef` per generation target (one extension, one Composer package, or one `--source` run).
11. **Track C extensions: PHP backer class + tyhpdef `extension { }` with short mappings.** See Phase 6.6. Generated `package.tyhpdef` never emits `use extension` for the library’s own extensions (consumers write `use extension Name` in `.tyhp`). Do not invent sidecar `include` files or generated overlay files for extension attachments. Do **not** emit file-level mapped `extension function`. Do **not** invent a `scalar` keyword.
12. **`--composer-package` is gone.** Phase 9 updates user docs for everything this story ships, including `docs/content/cli_tyhpdefGeneration.md` and the tyhpdef / `use` / extension pages listed there.
13. **Layer 2 stub corpora:** fetch into gitignored `tools/stub-cache/` (URLs in `runtime/README.md`); do not commit upstream stub trees. `--require-stubs` default **false**.
14. **Dogfood:** Story 20 AC is one small extension (`json` or `filter`) Layer 1+2 into `tyhpdef/`. Full Core/standard regen is not a gate. Never write over `php8.2.9/`.
15. **`partial` is legal in base / `include` tyhpdefs (this story).** Additive members only; duplicate members are errors; `omit` is illegal. Story 21 adds overlay last-wins replace and `omit`. No `partial extension` in this story.
16. **Tyhpdef `extension Name { }` is new (this story).** Every member must use short `=>` syntax, must have a return type, and is implicitly inline (consumer emit splices the mapping; no `#[Inline]` on these members). Empty `extension Name {}` is an error. Brace bodies are illegal. `deprecated` / `obsolete` on members are allowed.
17. **`use extension` is opt-in by default**, including for tyhpdef-declared extensions. Loading a package does **not** auto-activate its `extension { }` blocks unless that tyhpdef contains `global use extension …` (see 6.6). Same-file Tyhp `extension { }` stays in scope in that file. This story adds postfix `hide`, operator `insteadof`, and `global use`. Class-body tyhpdef `use extension` on a **type** still auto-activates that type’s attached surface for consumers (Story 03), minus hidden members.
18. **`global use` is C# `global using`:** it prefixes any `use` / `use function` / `use const` / `use extension` and applies to the **entire compilation** that loaded that file. Story 21 scalar packages author `global use extension \Tyhp\StringExtensions;` (etc.) in their tyhpdefs so consumers get `$s->length()` without a per-file import. Track C must **not** emit `global use extension` for a library’s own compiled extensions (those stay opt-in). A **non-`global` `use`** in a file / namespace may re-import a globally included symbol **in order to mutate it for that scope** (alias, `hide`, `insteadof`, method `as`). A local `use` that does **not** mutate (same name, no alias, no adaptations) is a **warning**: the import is not needed because the item is globally included.
19. **Track B `--source` is PHP only.** A `.tyhp` / `.tyhpdef` / any non-`.php` path is an error (`TyhpdefSourceNotPhp` = 7507). Generate Tyhp APIs with Track C, or compile then run Track B on the emitted PHP.
20. **Base `partial` with no matching type is an error.** Include-load order among additive partials does not matter (duplicates error either way). Overlay `partial` with no matching type stays a **warning** and skip (Story 21). **No** `partial if exists` / extra keyword — that behavior **is** overlay `partial`.
21. **Track A PHP is Tyhp-managed by default.** Tyhp downloads and keeps private CLI runtimes (one per minor 8.2–8.5) plus a Tyhp-owned `php.ini`. It does **not** search `PATH`, a local `brew` install, or `PHP_BINARY`. Unix/macOS artifacts: StaticPHP prebuilt `php` first, Homebrew **bottles** (direct GHCR download, no `brew` command) as fallback. `--php=<path>` is a **single-target escape hatch** (custom / private extensions, unknown config, **no** gated merge). `--php-targets` uses **only** managed runtimes. See [Track A PHP runtimes](#track-a-php-runtimes).
22. **Doc comments are first-class generator output** (unless `--no-docs`). Track A builds them from the **php.net HTML manual** (same idea as `tools/genTyhpdef.php`). Track B/C **copy** comments from source and apply tyhpdef adjustments. Stubs **fill holes** (and remain the extra **type** source). See [Documentation comments](#documentation-comments).

---

## Architecture Overview

### Purpose of the Tyhpdef Generator

Tyhpdef files (`.tyhpdef`) are type definition files that describe the type signatures of external PHP code — extensions, Composer packages, and user-authored PHP libraries. They serve the same role as TypeScript's `.d.ts` files or C/C++ header files: allowing the Tyhp compiler to type-check code that references external PHP without having access to the actual PHP implementation.

Story 20 implements the `generate_tyhpdef` CLI action (and Track C during `tyhp build`), which produces `.tyhpdef` files from three sources:

1. **PHP extensions (Track A)** — introspect installed, compiled-from-C PHP extensions via PHP's Reflection API. PHP is required. No fallback to source parsing.
2. **PHP source / Composer packages (Track B)** — parse PHP files with the C# ANTLR PHP parser. For `--package-path`, read that package's `composer.json` autoload config, collect the listed PHP files, and generate tyhpdef from them. PHP runtime is not required.
3. **Compiled Tyhp code (Track C)** — during `tyhp build` only. Libraries always emit `package.tyhpdef` + `package.tyhp.json` in the build output. Applications emit `package.tyhpdef` only when `build.generateTyhpdef` is true. Consuming Tyhp projects discover `package.tyhp.json` under `vendor/` (loader owned by Story 06).

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

### Strategy: Three Tracks (by *source*, not by output layout)

Tracks name **where types come from**. They do not name how files are laid out on disk.

- **Track A (PHP extension Reflection):** Used only for `--ext-name`. C# starts one PHP process, feeds it a Reflection dumper (stdin or temp `.php`), and reads one JSON document matching the Phase 2 schema. C# maps JSON → IR → `.tyhpdef`. PHP is a reflection data source, not a tyhpdef formatter. Giant `php -r` one-liners are forbidden (quoting, Windows, non-JSON defaults).

- **Track B (PHP source):** Used for `--source` and `--package-path`. Parse PHP with the existing ANTLR PHP parser, merge type hints with PHPDoc, emit into `{projectRoot|cwd}/tyhpdef/` (or `--output`). For `--package-path`, the file set comes from that package's `composer.json` `autoload` (add `autoload-dev` when `--include-dev`). Not Track C; CLI `--source` does not mean “parse the current project's `.tyhp` and dump `tyhpdef/`.” **Only `.php` files** — `.tyhp` / `.tyhpdef` / anything else is `TyhpdefSourceNotPhp` (7507). Compile Tyhp then run Track B on the emitted PHP, or use Track C.

- **Track C (compile-time, Tyhp AST):** Used only during `tyhp build`. Walk bound/checked symbols. Libraries always emit `package.tyhpdef` + `package.tyhp.json` in the **build output directory**. Applications with `build.generateTyhpdef = true` emit only `package.tyhpdef` there. `build.generateTyhpdef` is **ignored** for libraries. Ordering: `checker → tyhpdef generation → optimizer → emitter`.

Track A + Layer 2 (Phases 2 / 2.5) first for extensions. Track B (Phases 3–4) for PHP files and vendor packages. Track C (Phase 6) needs Story 02 binder substantially complete.

**Critical ordering requirement (Story 23):** Track C must run **before** the optimizer transforms the AST so `package.tyhpdef` reflects the original public API (including extension methods and operators), regardless of what the optimizer inlines internally.

### Pipeline Position

```
User runs: tyhp generate_tyhpdef --ext-name=curl
    │
    ▼
TyhpHostedService dispatches to GenerateTyhpdefAction
    │
    ├──► --ext-name  → Track A (PHP required; error TYHP7501 if missing)
    │       PHP Reflection JSON (Phase 2 schema) → IR
    │       → php.net manual → doc comments on IR
    │       → Layer 1 .tyhpdef
    │       → Layer 2 stub harvest (types + missing doc holes) → overlays/stubs/*.tyhpdef
    │
    ├──► --package-path  → Track B
    │       composer.json autoload → PHP files → ANTLR → IR
    │       → copy source `/** */` (tyhpdef adjustments) → .tyhpdef
    │
    ├──► --source  → Track B
    │       glob PHP files → ANTLR → IR
    │       → copy source `/** */` (tyhpdef adjustments) → .tyhpdef
    │
    └──► tyhp build → Track C (not CLI tyhpdef/)
            copy Tyhp source comments onto IR
            library → always package.tyhpdef + package.tyhp.json in build output
            application + generateTyhpdef → package.tyhpdef only
```

### Key Design Decisions

1. **Track A is `--ext-name` only, and requires a PHP runtime Tyhp can invoke.** Reflection is the only accurate source for compiled extensions. Default: Tyhp-managed PHP (download + isolated ini). `--php=<path>` opts into a user binary. If no runtime can be obtained, report `TyhpdefPhpNotFound` (7501) or a download/checksum code (7508–7512). Never fall back to Track B for extensions. Never silently use `PATH` `php`.

2. **Track B is `--source` and `--package-path`.** No PHP runtime. `--package-path` is not a fourth track: it is Track B with a Composer-derived file list.

3. **Tyhpdef output syntax is the same regardless of track.** All tracks produce valid `.tyhpdef` that `TyhpParserAstVisitor.Tyhpdef.cs` can parse. File *names* differ by mode (see output paths).

4. **PHPDoc is the primary extra *type* source in Track B.** Merge rule in Phase 4: prefer PHPDoc when the PHP hint is weak (`array` / `iterable` / `object` / `mixed` / `callable` / absent). **Prose** (summaries, `@param` descriptions) is copied from the source comment; stubs only fill missing prose. Track A prose comes from the php.net manual, not from stubs.

5. **Track C runs after bind+check, before optimize.** It needs `GlobalScope` (Story 02).

6. **Who writes what:**
   - Tracks A/B → `{projectRoot|cwd}/tyhpdef/` (or `--output` / `--output-file`).
   - Track C library → `{buildOutputDir}/package.tyhpdef` + `{buildOutputDir}/package.tyhp.json`. Always. Ignore `build.generateTyhpdef`.
   - Track C application + `build.generateTyhpdef = true` → `{buildOutputDir}/package.tyhpdef` only.

7. **CLI `tyhpdef/` file layout (`--split`).** Default **`file`**: one `.tyhpdef` per generation target, flat under `tyhpdef/`.
   - Extension: `tyhpdef/ExtJson.tyhpdef`
   - Composer package: `tyhpdef/guzzlehttp.guzzle.tyhpdef`
   - `--source`: `tyhpdef/source.tyhpdef` unless `--output-file`
   - Layer 2: `tyhpdef/overlays/stubs/*.tyhpdef` (never mixed into the Layer 1 file)

   Optional `--split=namespace`: one file per namespace (`tyhpdef/GuzzleHttp.Client.tyhpdef` style; globals in `tyhpdef/_global.tyhpdef` or kept on the primary file). Optional `--split=type`: one file per class/interface/trait/enum (PSR-4-ish, dots not directories). **Default stays `file`.** Per-type explodes Core/standard into thousands of files and is slower to load; offer it for huge Composer packages, do not use it for `--ext-name=standard`.

8. **Generated output does not add sidecar extension files.** Hand-crafted overlays after generation are listed by the author in `package.tyhp.json` / `tyhp.json`. Track C still **describes** extension methods/operators that the library's Tyhp source already declared, as inline members inside `package.tyhpdef` (public API). The emitter still writes the real PHP to `src/`; that is not a tyhpdef `include`.

9. **Runtime libraries** are consumed via `package.tyhp.json` (Story 06). Regenerating them is not a Story 20 gate; when those packages are built as libraries, Track C is what they use.

### Documentation comments

`--no-docs` skips emitting `/** … */` on all tracks (and Track A skips downloading the manual). Default is **on**. `--locale` selects the php.net manual language (default `en`).

Reference implementation to port (do **not** shell out to it): `tools/genTyhpdef.php` — `loadDocs()`, `phpDocHtmlNodeToMarkdown()`, and the function-level docblock builder (overview, description, notes, warnings, `@deprecated`, `@param` text, `@return` text, `@link`, `@generated`). That script’s `loadDocs` switch today only handles **`function`**; C# must also look up **class / method / property / constant / enum** pages using php.net ids (`function.strlen`, `class.datetime`, `datetime.format`, …). `@throws` was a TODO in the PHP tool — scrape the manual Errors/Exceptions section when present.

**Priority (prose — summaries and tag *descriptions*):**

| Track | 1 (primary) | 2 | 3 (holes only) |
|-------|-------------|---|----------------|
| **A** (`--ext-name`) | php.net HTML manual — if a page is found, **assemble** the tyhpdef `/** */` from it (do not keep a weaker Reflection summary). The PHP tool only did this when `getDocComment()` was empty; C# still assembles from the manual whenever the page exists | Reflection `docComment` from the JSON dump — **only** when no manual page exists (C extensions are usually empty) | Stub PHPDoc: fill missing summary / `@param` / `@return` / `@throws` text. Never replace non-empty manual prose |
| **B** (`--source` / `--package-path`) | Copy the doc comment from the PHP source AST | Normalize + tyhpdef adjustments (keep prose; type tags still follow the Phase 4 merge rule) | Stub cache hole-fill for matching FQNs (types + missing prose). Does **not** write Track A `overlays/stubs/` unless we are generating an extension |
| **C** (`tyhp build`) | Copy the doc comment from the Tyhp source | Normalize + tyhpdef adjustments (`@generated`, mapping notes if useful) | Stubs almost never apply (user/library API). If an FQN happens to match a stub, hole-fill only |

**Track A manual:**

- URL: `https://www.php.net/distributions/manual/php_manual_{locale}.html.gz` (same as `genTyhpdef.php`)
- Cache: `{LocalApplicationData}/Tyhp/php-manuals/{locale}/` (gzip kept; parse once per process). Offline with cache: use it. Offline without cache: warn, emit without manual prose, still use Reflection comments + stubs
- Locales the old tool used: `en`, `de`, `es`, `fr`, `it`, `ja`, `pt_BR`, `ru`, `tr`, `uk`, `zh`. Unknown `--locale`: error or fall back to `en` with a warning
- Parse HTML in **C#** after Reflection JSON → IR (`PhpManualDocExtractor`). Do not parse the manual inside the PHP dumper (keep JSON types-only aside from Reflection’s own `docComment` field)
- HTML → markdown for comment bodies: port `phpDocHtmlNodeToMarkdown`
- Examples from the manual: **off by default** (`INCLUDE_PHP_EXAMPLES = false` in the PHP tool). Do not port the example `/* */` rewriting unless a later flag `--include-manual-examples` is added
- Always emit `@link https://www.php.net/manual/{locale}/…` when a page was found
- Always emit `@generated` with PHP version + extension version (as the PHP tool does)
- php.net element ids (underscore → hyphen): `function.{name}`, `class.{name}`, `{class}.{method}`, `{class}.props.{prop}`, `{class}.constants.{const}`, `constant.{name}` for true globals. Port the function xpath first, then add class/member cases the PHP `loadDocs` switch never implemented (`// TODO get doc comment content from php documentation` on classes/methods)
- HTML parser: add **HtmlAgilityPack** (or equivalent) as a NuGet; do not spawn PHP `DOMDocument` for this

**Track B/C copy:** preserve the original comment text as the docblock body. Allowed modifications: wrap/indent for tyhpdef, add/replace **type** fragments in `@param`/`@return`/`@var`/`@template` from the merged signature, add `@generated`, drop tags that are meaningless in tyhpdef. Do not invent a new summary when the source already has one.

**Stubs (supplement, all tracks that have a matching FQN):**

- **Types:** unchanged Layer 2 rules (`@template`, weaker `array`/`mixed`/`callable`)
- **Prose:** only where the assembled comment is missing that piece (empty summary, `@param $x` with no text, no `@return` description, no `@throws`)
- Consensus among stub corpora when they disagree on prose: prefer Psalm+PHPStan; otherwise keep what Track A/B/C already has

Credit the PHP Documentation Group in `THIRD_PARTY.md` and in generated `SOURCES.md` / cache notes when the manual was used.

### Three-Layer Generation Model (design lock)

Reflection alone under-types Core/Standard. Community stubs close much of that gap. Tyhp-specific truth (operators, language constructs, advanced generics) must not live only in git history. Generation is therefore **three layers**:

| Layer | Source | Owns |
|-------|--------|------|
| **1. Baseline (mechanical)** | PHP Reflection per target (`8.2`–`8.5`) via Track A JSON dump (one process / extension) + php.net manual for **doc comments** | Names, signatures, defaults, by-ref/variadic, unions/intersections, enums, attributes, deprecation / tentative returns, assembled `/** */` from the manual |
| **2. Enrichment (imported)** | Psalm / PHPStan / Phan / PhpStorm stubs (`runtime/README.md`) | Docblock **types**, `@template`, better `array`/`callable`, some overloads; **prose hole-fill** only — harvested into **generated overlay** files. CLI: `{output}/overlays/stubs/`. Story 21 packages: `_tyhpdef/overlays/stubs/`. Listed **first** in `"overlay"`. |
| **3. Tyhp overlays (hand-owned)** | Package `_tyhpdef/overlays/*.tyhpdef` (not `stubs/`). `runtime/php-extensions/overlays/` is a **backup snapshot only** | Real generics, operator overloads, `exit`/`die`/`clone`, Tyhp-only constructs, anything stubs miss. Listed **last** in `"overlay"`. Never written into baseline. |

```text
For each PHP target (8.2, 8.3, 8.4, 8.5):
  PhpRuntimeManager.Ensure(minor) → php -n -c tyhp.ini + Reflection dumper → JSON snapshot
       ↓
  Merge snapshots → gated IR   (Phase 8; requires Story 20.5 for emit)
       ↓
  TyhpdefOutputWriter → Layer 1 baseline (`tyhpdef/Ext*.tyhpdef` CLI; `_tyhpdef/*.tyhpdef` in Story 21 packages)
       ↓
  Layer 2 stub harvest → `{output}/overlays/stubs/*.tyhpdef`
       ↓
  (compile time) apply package.tyhp.json "overlay" in array order (stubs first, hand last; last wins)
```

**Short term (this story):** CLI writes Layer 1 + Layer 2 under the default `tyhpdef/` directory (or `--output`). Never default to `runtime/php-extensions/php8.2.9/` — that tree is the live hand-enriched compiler input until Story 21 overlays exist.  
**Long term (Story 21 + Phase 8):** one package; version diffs via `declare(php=…)` / `#[\Tyhp\Php]` instead of per-minor forks.

#### What Reflection can and cannot provide

**Can:** constants (value→type), functions/methods (params, defaults, by-ref, variadic, return types including union/intersection/nullable), classes/interfaces/traits/enums, attributes, deprecation flags, tentative return types, basic type-guard remaps for well-known `is_*`.

**Cannot (or only poorly):** generics / templates; arity overloads; operators / extension methods; precise array/callable shapes; language constructs (`exit`/`die`/`clone`); cross-version API surface without multi-target reflect + merge.

#### Stub harvest + credit (Layer 2)

**What “stub corpora” means:** Psalm, PHPStan, Phan, and PhpStorm publish large trees of PHP stub files (see URLs in `runtime/README.md`) with better PHPDoc/`@template` than Reflection. Layer 2 *reads* those stubs and writes Tyhp overlay tyhpdefs. We do **not** commit those upstream trees into git as Tyhp source of truth.

**How this story obtains them:** generate-time **fetch into a gitignored cache** (e.g. `tools/stub-cache/`, listed in `.gitignore`), using the URLs already in `runtime/README.md`. CI can populate the cache in a setup step. If the cache is missing, Layer 2 warns and skips (Layer 1 still succeeds) unless `--require-stubs` is set (then error). Do not vendor the full PhpStorm stubs repo in `runtime/`.

1. Match symbols by FQN.
2. Prefer stub param/return/`@template` when Reflection is weak (`array`, `mixed`, `callable`, untyped).
3. Fill **prose** only where Layer 1 (manual / copied source) left that piece empty. Never overwrite a non-empty php.net or source summary.
4. When corpora disagree, prefer consensus of Psalm + PHPStan; use PhpStorm for coverage gaps; Phan as additional signal. If Psalm and PHPStan disagree with no consensus, keep the Layer 1 Reflection type / existing prose and record a warning (do not guess).
5. Emit a per-file header crediting sources, plus `{output}/SOURCES.md` / `NOTICE` listing URLs and licenses (include the PHP Documentation Group when Track A used the manual).
6. Harvest into `{output}/overlays/stubs/*.tyhpdef` (Layer 2 overlay files). Stubs are overlays — they are **not** written into the Layer 1 baseline file.

Layer 2 **overlay files** apply to **PHP extensions** (Track A). `--package-path` / `--source` do not write `overlays/stubs/`. Track B may still consult the same stub cache to fill type/prose **holes** on matching FQNs inside the generated tyhpdef.

**Story 20 dogfood (what “json vs Core/standard” means):** get **one small extension** (`json` or `filter`) working end-to-end (Layer 1 + Layer 2 into `tyhpdef/`) before treating the story as proven. Regenerating all of Core + standard into `tyhpdef/` is **not** a Story 20 acceptance gate (those extensions are huge; that regen is a follow-on / CI job). Never write that output over `runtime/php-extensions/php8.2.9/`.

#### Overlay preservation + regen safety (Layer 3)

Hand enrichment already exists in `php8.2.9/Ext*.tyhpdef` (generics, overloads, type guards, language constructs, Decimal operators). A naïve regen of **baseline** files will wipe in-place edits.

**Story 21 lock:** overlays are **separate tyhpdef files** loaded after baseline (`package.tyhp.json` `"overlay"` **in array order; last wins**). Stub harvest files (`_tyhpdef/overlays/stubs/`) are listed first and may be regenerated. Hand-written files (`_tyhpdef/overlays/*.tyhpdef`, non-recursive) are listed last and must **never** be overwritten by regen. Match by **Tyhp name**. Full declaration replaces. **`partial` in overlay** merges members with last-wins replace. **`omit` (overlay-only)** hides a symbol. Optional `// @overlay-against:` stamps the compact Layer 1 signature. Regenerators overwrite Layer 1 baseline and may overwrite `overlays/stubs/`; they **must not** touch hand-written `_tyhpdef/overlays/*.tyhpdef` or `runtime/php-extensions/overlays/`.

**`partial` split (Story 20 vs 21):** Story 20 parses `partial` and applies it on **base / `include`** tyhpdefs as **additive-only** (duplicate member → error; `omit` illegal). Story 21 overlay load **extends** that keyword: listed members replace, `omit` is legal. Header changes (generics / `extends` / `implements` / flags) remain a full replace, overlay-only.

`runtime/php-extensions/overlays/` is a **full-file backup** of hand edits that already existed before load-time overlays shipped. It is not loaded. After extracting those edits into package overlay files, keep the snapshot for recovery.

Until Story 21 overlay load is wired, the compiler continues to load `runtime/php-extensions/php8.2.9/` (live tree). Do not implement in-place `@tyhp-overlay` / `@generated-original` merge as the long-term mechanism — that was superseded.

Language constructs (`exit`/`die`/`clone`) and PECL Decimal operators are overlay-owned content (`tyhp/php` overlays and `tyhp/php-ext-decimal` overlays respectively — PECL Decimal is not `tyhp/decimal`).

#### Snapshots as first-class artifacts

Per-target Reflection JSON under `tyhpdef_gen/snapshots/{phpVersion}/` (checked in or CI-cached) so regen does not require four PHP installs on every laptop. Multi-target merge diffs snapshots, not live processes only.

#### Skeleton implementation order (do not skip)

1. Phase 5 stub — `TyhpdefDeclaration` IR + naive writer (round-trip one small Ext).
2. Track A skeleton — C# PHP JSON dump → IR → `.tyhpdef` for one extension (`json` or `filter`).
3. Multi-target harness — `--php-targets=8.2,8.3,8.4,8.5` via **managed** PHP runtimes; write snapshots. No user binary map.
4. Diff/merge prototype (comment-annotated or gated once Story 20.5 lands).
5. Stub enricher (PhpStorm first for coverage; then Psalm/PHPStan).
6. Story 21 overlay load (array order, last wins; stubs first, hand last).
7. Replace `tools/genTyhpdef.php` as the primary path (keep as reference until parity).

#### Practical locks

1. Dogfood **one** extension end-to-end before Core/Standard.
2. Attribution is mandatory in generated headers + `SOURCES.md`.
3. Do not block Layer 1–2 scaffolding on Story 21 package rename — migrate paths later.
4. Phase 8 gate **emit** waits on Story 20.5; snapshot + merge IR can land earlier.

### Track A PHP runtimes

User-installed PHP is the wrong default for Track A. `PATH` / Homebrew / XAMPP binaries have unknown `php.ini`, unknown loaded extensions, and usually only one minor. Gated Layer 1 needs a **known** CLI per minor (8.2–8.5), with Tyhp controlling which extensions are enabled.

**Two modes (mutually exclusive):**

| Mode | How | Gated merge (`--php-targets`) | Config |
|------|-----|-------------------------------|--------|
| **Managed (default)** | Tyhp downloads a private CLI per minor into a cache it owns | **Yes** — this is the only gated path | Tyhp `php.ini` via `php -n -c <tyhp.ini>` |
| **User binary (`--php=<path>`)** | Caller points at an executable that already has the extension loaded | **No** — error `TyhpdefPhpUserBinaryWithTargets` (7510) if combined with `--php-targets` | **Their** ini / loaded modules. Tyhp does **not** pass `-n`. Warn once: unknown configuration; single-target only |

Do **not** add `--php-map=8.2=/opt/php82,...` for user binaries. If gated output is required, use managed PHP. If the extension is private / not provisionable, use `--php` once (or hand-write the tyhpdef) and skip gates.

**`--php-version` vs `--php-targets`:**

- `--php-version=8.3` (or `output.phpVersion` if unset, else `8.2`) + managed PHP → **one** snapshot, **ungated** tyhpdef. Phase 2 dogfood.
- `--php-targets=8.2,8.3,8.4,8.5` → managed PHP for each minor → snapshots → Phase 8 gated tree. Story 21 regen uses this.
- `--php` + `--php-version`: the binary’s `PHP_VERSION` must match that minor or error.
- `--php` + `--php-targets`: always error (7510).
- `--no-php` + `--ext-name`: still error (Track A needs a runtime).

The generator’s PHP is **not** `output.phpVersion`. `output.phpVersion` is the **compiler** emit/check target (Story 10 / 20.5). Managed 8.2–8.5 exist so Layer 1 can describe all of those APIs in one gated file.

**Cache (not git, not on `PATH`):**

`{LocalApplicationData}/Tyhp/php-runtimes/{rid}/{major.minor}/{patch}/`

- `rid`: `win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64` (error on unsupported RID).
- **Do not** put the compiler version in this path (unlike the AST cache) — PHP runtimes persist across Tyhp upgrades.
- Override: `--php-runtime-dir=<path>` (CI volume). Env `TYHP_PHP_RUNTIME_DIR` is the same override.
- Not a user-facing PHP install. Do not prepend it to `PATH`. Do not document it as “install PHP”.

**What Tyhp downloads:**

A **pinned in-repo manifest** (`Tyhp/Domain/Services/PhpRuntimeManifest.json` or equivalent) maps `(rid, major.minor)` → artifact URL + sha256 (or a resolver that picks the latest **patch** of that minor, then checksums the artifact). Logical provider ids in the manifest (`windows-php-net`, `staticphp`, `homebrew-bottle`) — **not** a single hardcoded GitHub repo path — so a project rename does not break the manager.

Requirements of every provider:

1. HTTPS download, checksum required before exec (`TyhpdefPhpRuntimeChecksumMismatch` = 7509).
2. NTS CLI `php` / `php.exe` for that RID.
3. Never invoke `brew`, `pecl`, `spc craft`, or compile `php-src` on the user’s machine as the default path.
4. Never require Docker.
5. If no provider can supply a RID, fail clearly (7508) and mention `--php` as the escape hatch.

**Provider order**

| Priority | Host | Source | Notes |
|----------|------|--------|--------|
| 1 | Windows | Official [windows.php.net](https://windows.php.net/download/) NTS zip (`https://windows.php.net/downloads/releases/` and the archive under `https://downloads.php.net/~windows/releases/`) | Zip already contains `ext/*.dll` for curl, gd, intl, … |
| 1 | macOS / Linux | **StaticPHP** prebuilt **PHP CLI** (not the `spc` build tool). Prefer a fat/“gigantic” (or equivalent) artifact so optional extensions are already in the binary | php.net does **not** ship portable CLI zips |
| 2 | macOS / Linux | **Homebrew bottle fallback** — direct download + extract, **without** the `brew` command | Only if StaticPHP artifacts are missing, renamed, 404, or checksum-fail. See below |
| — | Windows | Homebrew bottles | **Not used** (no Windows bottles) |

**StaticPHP (primary Unix/macOS PHP; formerly static-php-cli):**

The project is renaming **static-php-cli → StaticPHP** (v3). Today the GitHub repo is still [crazywhalecc/static-php-cli](https://github.com/crazywhalecc/static-php-cli); docs/CDN already say StaticPHP ([static-php.dev](https://static-php.dev), prebuilt host [dl.static-php.dev](https://dl.static-php.dev), bulk builds under [github.com/static-php/hosted](https://github.com/static-php/hosted)). Tyhp downloads a **ready-made `php` binary**, not `spc` + a local compile.

Rename-proof resolver (try in order, first checksum-ok wins):

1. URLs recorded in `PhpRuntimeManifest.json` (may still say `static-php-cli` during the transition)
2. `https://dl.static-php.dev/` current layout (`v3/…` today; accept a `v2/` or unversioned layout if the manifest says so)
3. GitHub Releases on `crazywhalecc/static-php-cli` **and** any successor `static-php/static-php` / `static-php/staticphp` (or whatever the v3 release actually uses)
4. `github.com/static-php/hosted` release/action artifacts if that is where fat CLIs are published

Treat “StaticPHP”, “static-php-cli”, and `spc` as **aliases of one provider** in logs and docs. Prefer **gigantic** (or the documented fat set) over **base** so Story 21 optional extensions can load. If only `base` exists, Core/json/standard still work; `--ext-name=curl` may then 7511.

**Homebrew bottle fallback (no `brew` CLI):**

This is possible. Bottles are OCI blobs on GitHub Container Registry, not a simple php.net zip.

1. Read formula metadata from `https://formulae.brew.sh/api/formula/php@{major.minor}.json` (e.g. `php@8.4.json`). That JSON lists `bottle.stable.root_url` (`https://ghcr.io/v2/homebrew/core`), per-platform `files.{tag}.url` + `sha256`, and `dependencies`.
2. Authenticate to GHCR with the **anonymous public pull** flow Homebrew itself uses (token from `https://ghcr.io/token?service=ghcr.io&scope=repository:homebrew/core/php/8.4:pull`, or the well-known unauthenticated `Bearer QQ==` for public bottles). Do **not** require a GitHub login or a local `brew` install.
3. Download the matching blob (`Accept: application/vnd.oci.image.layer.v1.tar+gzip`), verify sha256, extract the tarball. Do not run `brew install` / `brew unpack`.
4. **Pour the dependency closure too.** A `php@8.4` bottle is **not** standalone — the formula lists openssl, icu4c, oniguruma, libzip, etc. Fetch each dependency’s bottle the same way into a Tyhp-owned prefix. One PHP keg without those dylibs will not run.
5. Match the bottle **platform tag** to the host (`arm64_sonoma`, `arm64_sequoia`, `arm64_tahoe`, `sonoma`, `arm64_linux`, `x86_64_linux`, …). No matching tag → skip this fallback (do not guess).
6. Bottles bake a **Cellar prefix** (`/opt/homebrew/Cellar`, `/usr/local/Cellar`, `/home/linuxbrew/.linuxbrew/Cellar`). Extract into a Tyhp cache prefix and set library search paths (`DYLD_FALLBACK_LIBRARY_PATH` / `LD_LIBRARY_PATH` / a minimal rpath rewrite) so the binary does not require a real Homebrew install. If relocation cannot be made to work on this OS, skip this fallback and 7508.
7. After extract, generate Tyhp’s `php.ini` (`-n -c`) against that prefix’s `extension_dir`. Do not use Homebrew’s `/opt/homebrew/etc/php/…` ini.
8. shivammathur/php tap bottles (`https://ghcr.io/v2/shivammathur/php`) are **not** required; homebrew/core `php@8.2`…`php@8.5` is enough. Do not scrape random taps.

Homebrew fallback is **backup only**. StaticPHP prebuilts stay preferred because they are one file and have no Cellar/dylib graph.

**Tyhp-owned config (managed only):**

- Always invoke `{php} -n -c {cache}/tyhp.ini {dumper}`.
- `tyhp.ini` is generated by Tyhp: enable the extensions needed for this `--ext-name` (and, for Story 21 regen, the always-present set plus the optional ext being generated). `extension_dir` points at the managed tree. No `auto_prepend`, no user `PHP_INI_SCAN_DIR`.
- These binaries are **not** for running the user’s app.

**Extensions:**

- **Always-present** (Core / standard / json / …): must be available in every managed minor.
- **Public optional** (`curl`, `mbstring`, `gd`, `intl`, `pdo`, … — Story 21 `tyhp/php-ext-*`): provision from the same distro when possible (Windows `ext/php_*.dll`; Unix loadable `.so` or compiled-in). If the managed runtime cannot load `--ext-name=foo`, error `TyhpdefPhpExtensionNotProvisioned` (7511) — caller uses `--php` with a PHP that already has it, or hand-writes the tyhpdef.
- **Private / in-house / unpublished PECL:** `--php` (single-target) or a hand tyhpdef. Tyhp will not fetch arbitrary unpublished `.so` files.

**Patch auto-update (managed only):**

On a Track A run, for each minor needed, if the cache is missing **or** a newer **patch** of that minor exists (php.net releases index / equivalent), download, checksum, smoke-test (`php -v`, dumper runs, expected ext loads). Swap to the new patch only after smoke-test passes; keep the previous patch until then (`TyhpdefPhpRuntimeUpdateFailed` = 7512 is a **warning**, then use last-known-good). Never bump the **minor** automatically. `--no-php-runtime-update` skips the check (CI pinning). Offline with a warm cache: use cache, no error. Offline with empty cache: 7508/7501.

**Snapshots still matter:** checked-in / CI-cached JSON under `tyhpdef_gen/snapshots/{phpVersion}/` lets merge run without re-downloading PHP. `--refresh-snapshots` forces a live managed reflect.

**Homebrew (humans only, not Tyhp):** Tyhp does not use Homebrew. If a human wants a user-binary `--php` for a private extension, versioned formulae work on Apple Silicon:

```bash
brew install php@8.2 php@8.3 php@8.4 php@8.5
# example: /opt/homebrew/opt/php@8.2/bin/php
```

That path is **`--php` only** (single-target, unknown ini). It is not how gated Story 21 regen runs.

**Attribution:**

Tyhp does not own PHP, StaticPHP, Homebrew, or the stub corpora. Credits live in three places (do not fork the list):

| Place | Holds |
|-------|--------|
| Repo root [`THIRD_PARTY.md`](../THIRD_PARTY.md) | Canonical list of download hosts and stub projects |
| Root [`README.md`](../README.md) | Short acknowledgments + link |
| Generated `{output}/SOURCES.md` and each managed PHP cache `ATTRIBUTION.txt` | The **specific** artifact URL/version used for that run |

Do **not** put the full catalog only in `README.md`. Apache `NOTICE` is for notices that ship **inside** a Tyhp distribution of third-party **source**; managed PHP is a cache download, so `THIRD_PARTY.md` + per-cache `ATTRIBUTION.txt` is the right pair. Stub harvest that **is** committed under `_tyhpdef/overlays/stubs/` must keep header credit + `SOURCES.md` as already required.

#### Risks

- Regenerating Core/Standard without overlays → checker regressions.
- Stub formats differ (normalizer required).
- CI needs network (or a warm `php-runtimes` cache / snapshots) for managed PHP; missing target must fail hard. Do not assume a matrix of Homebrew/apt PHPs on the laptop. Homebrew **bottles** are a download fallback only; Cellar/dylib relocation can fail on a given macOS tag — then 7508, not a half-working php.
- Two legacy PHP generators (`tools/` vs `DebugProject/`) diverge — C# path must not invent a third format.

### Dependency Map

```
Phase 1: GenerateTyhpdefAction Expansion (CLI infrastructure)
    │
    ├──► Phase 5: Tyhpdef Output Writer + IR (shared; implement before tracks)
    │
    ├──► Phase 2: PHP Extension Reflection (Track A, Layer 1)
    │       │
    │       └──► Phase 2.5: Stub Harvest (Layer 2)
    │
    ├──► Phase 3: PHPDoc Parser (needed by Track B)
    │       │
    │       └──► Phase 4: C# Native PHP Source → Tyhpdef (Track B, including --package-path)
    │
    └──► Phase 6: Tyhpdef from Tyhp Code (Track C)
             │
             └──► Phase 7: Validation, Integration, and End-to-End Testing
                      │
                      ├──► Phase 9: User documentation (after Phases 1–7; Phase 8 docs after 20.5)
                      │
                      └──► Phase 8: Multi-Target PHP Version Generation (gated output) — **requires Story 20.5**
```

> **Phase-ordering note:** Tracks A (Phase 2), B (Phase 4), C (Phase 6), and Layer 2 (Phase 2.5) all construct `TyhpdefDeclaration` objects and serialize them via `TyhpdefOutputWriter`. **Phase 5 must be implemented before Phases 2, 2.5, 4, and 6.** Effective build order: Phase 1 → Phase 5 (shared IR/writer) → Phase 3 (PHPDoc parser) → Phase 2 (Track A) → Phase 2.5 (Layer 2) → Phase 4 (Track B) → Phase 6 (Track C) → Phase 7 → **Phase 9 (docs for 1–7)** → **Phase 8 (after Story 20.5)** → **Phase 9 remainder (multi-target docs)**.

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
| 7505 | `TyhpdefLibraryEntrypointDetected` | Library project contains entrypoint file(s) with root-level side-effect statements (emitted by Track C / build when generation runs; not a parse/bind code) |
| 7506 | `TyhpdefVerifyIncompatible` | `--verify` found an existing tyhpdef that is not compatible with the generated golden PHP signature |
| 7507 | `TyhpdefSourceNotPhp` | `--source` / `--package-path` collected a non-`.php` file (including `.tyhp` / `.tyhpdef`) |
| 7508 | `TyhpdefPhpRuntimeDownloadFailed` | Managed PHP download failed (network, unsupported RID, empty cache offline) |
| 7509 | `TyhpdefPhpRuntimeChecksumMismatch` | Managed PHP artifact failed checksum |
| 7510 | `TyhpdefPhpUserBinaryWithTargets` | `--php` combined with `--php-targets` |
| 7511 | `TyhpdefPhpExtensionNotProvisioned` | Managed PHP cannot load `--ext-name` (use `--php` or a hand tyhpdef) |
| 7512 | `TyhpdefPhpRuntimeUpdateFailed` | Patch auto-update failed; last-known-good still used (warning) |

`TyhpdefDuplicateFqnAcrossPackages = 8025` is a parse/bind diagnostic owned by Story 06 — do NOT define or test it in this story.

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
- Modified `Tyhp/Config/ActionConfigProvider.cs` — Register `--package-path`, `--source`, `--output-file`, `--php`, `--php-targets`, `--php-runtime-dir`, `--validate`, `--split`, `--locale`; **remove `--composer-package`**; add boolean flags `--no-docs`, `--include-internal`, `--include-deprecated`, `--overwrite`, `--no-php`, `--verify`, `--include-dev`, `--require-stubs`, `--no-php-runtime-update`, `--refresh-snapshots`
- Draft `docs/content/cli_tyhpdefGeneration.md` as flags land (Phase 9 is the completeness pass for all user docs this story ships)
- Modified `Tyhp/Domain/Exceptions/MessageCode.cs` — New tyhpdef generation message codes (7500–7512, in the `generate_tyhpdef` CLI range)

### Implementation Details

**1.1 — Define `TyhpdefGenerationOptions`**

New file: `Tyhp/Domain/Services/TyhpdefGenerationOptions.cs`

Namespace `Tyhp.Domain.Services`:

- `TyhpdefGenerationMode Mode { get; init; }` — enum: `PhpExtension`, `ComposerPackage`, `PhpSourceFiles`, `TyhpCode`
- `string? ExtensionName { get; init; }` — PHP extension name (for `PhpExtension` mode)
- `string? PackagePath { get; init; }` — path to Composer package directory (for `ComposerPackage` mode). CLI flag: `--package-path`
- `List<string> SourcePaths { get; init; }` — glob patterns for PHP source files (for `PhpSourceFiles` mode). Reuse the existing glob matching infrastructure from `Project.GetProjectSourceFiles()` for pattern resolution.
- `string OutputDirectory { get; init; }` — where to write generated `.tyhpdef` files. Default: `{projectRoot}/tyhpdef/` when a project is loaded, otherwise `{cwd}/tyhpdef/`
- `string? OutputFileName { get; init; }` — explicit output file path (`--output-file`). Relative paths resolve against `OutputDirectory`
- `bool PreferPhpRuntime { get; init; }` — unused for Composer/source (always Track B). For extensions this is always true (Track A required)
- `string? PhpExecutablePath { get; init; }` — `--php`: user binary (Track A escape hatch). Null = managed PHP
- `string? PhpRuntimeDir { get; init; }` — `--php-runtime-dir` / `TYHP_PHP_RUNTIME_DIR`
- `bool NoPhpRuntimeUpdate { get; init; }` — skip patch auto-update
- `bool RefreshSnapshots { get; init; }` — re-reflect even if snapshots exist
- `List<string> PhpTargets { get; init; }` — `--php-targets` minors (managed only)
- `string? PhpVersion { get; init; }` — target PHP version string for output metadata
- `int PhpProcessTimeoutMs { get; init; }` — timeout for the Track A PHP process (default: 60000, one process per extension)
- `string Locale { get; init; }` — php.net manual language for Track A (`php_manual_{locale}.html.gz`). Default: `"en"` (or project `Locale`)
- `bool IncludeDocComments { get; init; }` — emit `/** */` (default: true). Track A: php.net manual (+ Reflection if no page). Track B/C: copy source comments. `--no-docs` skips emit **and** the Track A manual download
- `bool IncludeDeprecated { get; init; }` — include and mark deprecated items (default: true)
- `bool IncludeInternal { get; init; }` — include `@internal` items (default: false)
- `bool IncludeDev { get; init; }` — `--include-dev`: also collect Composer `autoload-dev` (default: false)
- `string Split { get; init; }` — `--split=file|namespace|type` (default: `file`)
- `bool RequireStubs { get; init; }` — fail if Layer 2 stub cache is missing (default: false)
- `bool Overwrite { get; init; }` — overwrite existing tyhpdef files (default: false)
- `bool Verify { get; init; }` — apply overlays and compatibility-verify final forms
- `string? ValidatePath { get; init; }` — `--validate` directory/file to parse-check

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
   - `--ext-name=curl` → `PhpExtension` mode → **Track A**
   - `--package-path=./vendor/guzzlehttp/guzzle` → `ComposerPackage` mode → **Track B** (composer.json autoload → PHP files)
   - `--source=./src/**/*.php` → `PhpSourceFiles` mode → **Track B**
   - Track C is triggered from `BuildAction`, not from this CLI verb
2. Parse common options:
   - `--output=./tyhpdef/` → output directory (overrides the default `{projectRoot|cwd}/tyhpdef/`)
   - `--output-file=ExtCurl.tyhpdef` → explicit file name/path
   - `--php=/usr/bin/php` → **user** PHP executable (Track A only; single-target; unknown config)
   - `--php-targets=8.2,8.3,8.4,8.5` → managed PHP per minor (Phase 8 gated). Illegal with `--php`
   - `--php-runtime-dir=…` → managed cache override
   - `--no-php-runtime-update` / `--refresh-snapshots`
   - `--php-version=8.3` → single-target managed minor (or must match `--php`’s version)
   - `--locale=en` → Track A php.net manual language (`php_manual_en.html.gz`); ignored by Track B/C except as a no-op
   - `--no-docs` → skip emitting `/** */` on all tracks; Track A does not download the manual
   - `--include-internal` → include `@internal` items
   - `--include-deprecated` → include deprecated items (default on; `--no-deprecated` to skip)
   - `--overwrite` → overwrite existing files
   - `--split=file|namespace|type` → CLI `tyhpdef/` layout (default `file`)
   - `--include-dev` → Composer `autoload-dev` as well as `autoload`
   - `--require-stubs` → Layer 2 cache missing is an error
   - `--no-php` → **error** in `--ext-name` mode (Track A requires PHP). Ignored for `--source` / `--package-path` (already Track B)
   - `--validate=<path>` / `--verify` → Phase 7 modes
3. Create `TyhpdefGenerationOptions` from parsed arguments
4. Determine which track to use:
   - `PhpExtension` → Track A only (PHP required)
   - `PhpSourceFiles` or `ComposerPackage` → Track B always
   - `--no-php` with `--ext-name` → `TyhpdefPhpNotFound` error
5. Dispatch to the appropriate generator — add these placeholder markers in the dispatch logic for subsequent phases to replace:
   - `// PLACEHOLDER_PHASE_2: Track A — PHP Extension Reflection`
   - `// PLACEHOLDER_PHASE_4: Track B — C# Native (including --package-path)`
6. Report results:
   - Display generated file paths
   - Display declaration counts
   - Display timing
   - Display any diagnostics
   - Set exit code based on result

**1.5 — Expand `Project.cs` Configuration Getters**

File: `Tyhp/Config/Project.cs`

Add methods to read the new CLI arguments:

- `GetTyhpdefOutputDir()` — reads `--output` argument; if unset, `{projectRoot}/tyhpdef/` when a project is loaded, else `{cwd}/tyhpdef/`
- `GetTyhpdefOutputFile()` — reads `--output-file` argument
- `GetPhpExecutablePath()` — reads `--php` argument (user binary; null means managed)
- `GetTyhpdefPhpVersion()` — reads `--php-version` argument
- `GetTyhpdefPhpTargets()` — reads `--php-targets`
- `GetTyhpdefPhpRuntimeDir()` — reads `--php-runtime-dir`
- `GetTyhpdefNoPhpRuntimeUpdate()` — reads `--no-php-runtime-update`
- `GetTyhpdefRefreshSnapshots()` — reads `--refresh-snapshots`
- `GetTyhpdefLocale()` — reads `--locale` argument (defaults to `this.Locale`)
- `GetTyhpdefSourcePaths()` — reads `--source` argument(s)
- `GetTyhpdefPackagePath()` — reads `--package-path` argument
- `GetTyhpdefNoDocs()` — reads `--no-docs` flag
- `GetTyhpdefIncludeInternal()` — reads `--include-internal` flag
- `GetTyhpdefIncludeDeprecated()` — reads `--include-deprecated` / `--no-deprecated`
- `GetTyhpdefOverwrite()` — reads `--overwrite` flag
- `GetTyhpdefNoPhp()` — reads `--no-php` flag
- `GetTyhpdefValidatePath()` — reads `--validate`
- `GetTyhpdefVerify()` — reads `--verify`
- `GetTyhpdefSplit()` — reads `--split` (default `file`)
- `GetTyhpdefIncludeDev()` — reads `--include-dev`
- `GetTyhpdefRequireStubs()` — reads `--require-stubs`

These all follow the same pattern as the existing `GetExtName()` method: reading from `this._configuration["key"]`.

Also update `ActionConfigProvider`:

- `ValueTakingFlags`: add `--package-path`, `--source`, `--output-file`, `--php`, `--php-targets`, `--php-runtime-dir`, `--validate`, `--split`, `--locale`; **remove `--composer-package`**
- `BareBooleanFlags`: add `--no-docs`, `--include-internal`, `--include-deprecated`, `--no-deprecated`, `--overwrite`, `--no-php`, `--verify`, `--include-dev`, `--require-stubs`, `--no-php-runtime-update`, `--refresh-snapshots`

**1.6 — Add Tyhpdef Generation MessageCodes**

File: `Tyhp/Domain/Exceptions/MessageCode.cs`

Add the generation codes inside the existing `#region CLI — generate_tyhpdef action (7500–7599)` region (the placeholder comment there is awaiting Story 20):

- `TyhpdefGenerationError = 7500`
- `TyhpdefPhpNotFound = 7501`
- `TyhpdefSourceParseError = 7502`
- `TyhpdefOutputWriteError = 7503`
- `TyhpdefPhpDocParseError = 7504`
- `TyhpdefLibraryEntrypointDetected = 7505`
- `TyhpdefVerifyIncompatible = 7506`
- `TyhpdefSourceNotPhp = 7507`
- `TyhpdefPhpRuntimeDownloadFailed = 7508`
- `TyhpdefPhpRuntimeChecksumMismatch = 7509`
- `TyhpdefPhpUserBinaryWithTargets = 7510`
- `TyhpdefPhpExtensionNotProvisioned = 7511`
- `TyhpdefPhpRuntimeUpdateFailed = 7512`

These live in the `generate_tyhpdef` CLI range (7500–7599). The 8000s range is reserved for tyhpdef *parse/bind* diagnostics and must not be used here. Do not define `8025`.

### Acceptance Criteria

- [ ] `GenerateTyhpdefAction` parses all new CLI arguments without crashing
- [ ] Running `tyhp generate_tyhpdef --ext-name=curl` reports "PHP delegation not yet implemented" (placeholder)
- [ ] Running `tyhp generate_tyhpdef --source=./src/*.php` reports "C# native generation not yet implemented" (placeholder)
- [ ] Running `tyhp generate_tyhpdef` with no arguments displays a helpful error message listing required arguments
- [ ] `TyhpdefGenerationOptions` correctly captures all parsed arguments
- [ ] `TyhpdefGenerationResult` can hold generation statistics and diagnostics
- [ ] New `MessageCode` values are added in the `generate_tyhpdef` CLI range (7500–7512)
- [ ] All new and modified files compile without errors
- [ ] No regressions in existing `GenerateTyhpdefAction` functionality (the `--ext-name` validation still works)

### Dependencies

- **Requires:** Story 01 (diagnostic system with `DiagnosticBag`, `MessageCode`) — for error reporting
- **Provides:** CLI infrastructure for Phases 2-7

---

## Phase 2: PHP Extension Reflection (Track A, Layer 1)




### Phase Overview

Implement Track A: **PHP compiled extensions only**. C# starts **one PHP process per extension**, feeds it a Reflection dumper (stdin to `php` or a temp `.php` file — **not** `php -r` one-liners), and reads **one JSON document** matching the schema below. C# deserializes that JSON into `TyhpdefDeclaration` IR and writes Layer 1 `.tyhpdef` via `TyhpdefOutputWriter` (Phase 5).

`--package-path` is **not** implemented here (Phase 4 / Track B).

### Deliverables

- `Tyhp/Domain/Services/PhpDelegationTyhpdefGenerator.cs` — C# orchestrator: spawn PHP, parse JSON, map to IR
- `Tyhp/Domain/Services/PhpRuntimeManager.cs` — download / cache / isolated ini / patch update for managed PHP
- `Tyhp/Domain/Services/PhpRuntimeManifest.json` — checksummed artifact map (or equivalent)
- `Tyhp/Domain/Services/PhpRuntimeDetector.cs` — inspect a **user** `--php` binary only (`php -v`, `php -m`)
- `Tyhp/Domain/Services/PhpReflectionDump.php.template` (or an embedded C# string written to a temp file) — the PHP dumper; emits JSON only, never tyhpdef
- `Tyhp/Domain/Services/PhpManualDocExtractor.cs` — download/cache php.net `php_manual_{locale}.html.gz`, port of `tools/genTyhpdef.php` `loadDocs` + HTML→markdown, attach `DocComment` on IR
- Modified `Tyhp/CLI/GenerateTyhpdefAction.cs` — Replace `PLACEHOLDER_PHASE_2` with Track A

### Implementation Details

**2.1 — Resolve a PHP runtime (`PhpRuntimeManager` + user `--php`)**

New files: `Tyhp/Domain/Services/PhpRuntimeManager.cs`, `PhpRuntimeManifest.json` (or code-generated from it), `PhpRuntimeDetector.cs`

**Managed path (default when `--php` is unset):**

- `PhpRuntimeManager.Ensure(minor: "8.3", options)` → `{ Path, Version, IniPath, LoadedExtensions }`
- Cache layout and download rules: [Track A PHP runtimes](#track-a-php-runtimes)
- Invoke the dumper as `{Path} -n -c {IniPath} …` — never the user’s ini
- Single-target minor: `--php-version`, else project `output.phpVersion`, else `8.2`
- Do **not** search `PATH`, Homebrew, XAMPP, or `PHP_BINARY`

**User path (`--php=<path>`):**

- `PhpRuntimeDetector.Inspect(explicitPath)` — that path only. No PATH search.
- Missing / not executable → `TyhpdefPhpNotFound` (7501)
- Combined with `--php-targets` → `TyhpdefPhpUserBinaryWithTargets` (7510)
- If `--php-version` is set, `PHP_VERSION` major.minor must match
- Invoke **without** `-n` so their loaded private extension is visible
- Emit a **warning** that configuration is uncontrolled and output is single-target / ungated
- `PhpRuntimeInfo`: `string Path`, `string Version`, `List<string> LoadedExtensions`, `bool IsManaged`

**2.2 — Reflection JSON schema (contract)**

The PHP dumper writes a single UTF-8 JSON object to stdout. C# deserializes with `System.Text.Json`. Unknown properties are ignored (forward compatible). Missing required properties are a `TyhpdefGenerationError`.

Root object:

```json
{
  "schemaVersion": 1,
  "phpVersion": "8.3.11",
  "extension": "json",
  "constants": [ /* Constant */ ],
  "functions": [ /* Function */ ],
  "classes": [ /* ClassLike */ ]
}
```

`PhpType` (never a PHP `ReflectionType` object; never `(string)$type` alone):

```json
{
  "kind": "none | named | nullable | union | intersection",
  "text": "string|int",
  "name": "string",
  "builtin": true,
  "nullable": false,
  "types": [ /* nested PhpType */ ]
}
```

- `"none"`: untyped (`text` empty).
- `"named"`: single named type (`name` is the PHP name, `text` is tyhpdef-ready, FQN with leading `\` for classes).
- `"nullable"`: `?T` — `types` has one element, or `name`/`text` already include `?`.
- `"union"` / `"intersection"`: `types` lists members; `text` is the joined form (`A|B`, `A&B`).

`PhpValue` (defaults and constant values — **not** raw `json_encode($phpValue)`):

```json
{
  "kind": "null | bool | int | float | string | array | const | nan | inf | neginf | unavailable",
  "value": null,
  "constName": null
}
```

| kind | `value` | notes |
|------|---------|--------|
| `null` / `bool` / `int` / `float` / `string` | JSON null/bool/number/string | ordinary scalars |
| `array` | JSON array of `PhpValue` (list) or object map of string→`PhpValue` | no objects/resources inside |
| `const` | omit `value` | `constName` is the PHP constant used as default (e.g. `JSON_THROW_ON_ERROR`) when `isDefaultValueConstant()` |
| `nan` / `inf` / `neginf` | omit | float specials |
| `unavailable` | omit | default exists but cannot be reflected (or is an object/resource) — tyhpdef omits `= …` |

`Parameter`:

```json
{
  "name": "flags",
  "type": { /* PhpType */ },
  "optional": true,
  "default": { /* PhpValue or null if none */ },
  "variadic": false,
  "byRef": false,
  "promoted": false,
  "attributes": [ /* Attribute */ ]
}
```

`Attribute`:

```json
{
  "name": "\\SensitiveParameter",
  "args": [ { /* PhpValue */ } ]
}
```

`Function` (file-level or method — methods add modifiers):

```json
{
  "name": "json_encode",
  "params": [ /* Parameter */ ],
  "returnType": { /* PhpType */ },
  "returnByRef": false,
  "tentativeReturn": false,
  "deprecated": false,
  "docComment": "/** … */",
  "attributes": [ /* Attribute */ ],
  "modifiers": []
}
```

`modifiers` for methods: `"public"` / `"protected"` / `"private"` / `"static"` / `"final"` / `"abstract"`. File-level functions: empty.

`Constant`:

```json
{
  "name": "JSON_ERROR_NONE",
  "value": { /* PhpValue */ },
  "type": { /* PhpType inferred from value, plus PHP 8.3 typed class-const if present */ },
  "modifiers": [],
  "deprecated": false,
  "docComment": null
}
```

`Property`:

```json
{
  "name": "code",
  "type": { /* PhpType */ },
  "modifiers": ["public"],
  "hasDefault": false,
  "default": null,
  "deprecated": false,
  "docComment": null,
  "attributes": []
}
```

`EnumCase`:

```json
{
  "name": "Hello",
  "backing": { /* PhpValue or null */ },
  "docComment": null
}
```

`ClassLike`:

```json
{
  "kind": "class | interface | trait | enum",
  "name": "JsonException",
  "fqn": "\\JsonException",
  "modifiers": [],
  "extends": "\\Exception",
  "implements": ["\\Throwable"],
  "uses": ["\\SomeTrait"],
  "isAnonymous": false,
  "backingType": null,
  "docComment": null,
  "deprecated": false,
  "attributes": [],
  "constants": [],
  "properties": [],
  "methods": [],
  "enumCases": []
}
```

- `backingType` is `"int"` / `"string"` for backed enums, else `null`.
- `uses` is trait names (Reflection `getTraits()`), FQN.
- Skip anonymous classes (`isAnonymous: true` should not appear for extension APIs; if it does, C# drops them).
- Include interfaces, traits, and enums from `ReflectionExtension`, not only classes.

**Dumper implementation notes:**

- One process, one extension, timeout `PhpProcessTimeoutMs` (default 60s).
- Pass the extension name as `argv[1]` or an env var (`TYHP_REFLECT_EXT`).
- `json_encode($dump, JSON_UNESCAPED_UNICODE | JSON_INVALID_UTF8_SUBSTITUTE)` of the schema object only — no extra stdout.
- stderr is diagnostics; non-zero exit → `TyhpdefGenerationError`.
- Do not `json_encode` live PHP values that can be `INF`/`NAN`/objects; always wrap as `PhpValue`.

**2.3 — Map JSON → IR → file**

- Verify the extension is in `PhpRuntimeInfo.LoadedExtensions`; else user binary → `TyhpdefGenerationError`, managed → `TyhpdefPhpExtensionNotProvisioned`.
- Construct `TyhpdefDeclaration` objects (Phase 5). Seed `DocComment` from JSON `docComment` when Reflection provided one.
- Unless `--no-docs`: `PhpManualDocExtractor.Attach(ir, locale)` — see [Documentation comments](#documentation-comments). If a php.net page exists, **assemble** the comment from the manual (replace empty/placeholder Reflection text). Keep Reflection comments only when no page exists.
- Write via `TyhpdefOutputWriter`.

**2.3b — `PhpManualDocExtractor`**

Port `tools/genTyhpdef.php` (do not exec that PHP file). Download `php_manual_{locale}.html.gz` into `{LocalApplicationData}/Tyhp/php-manuals/{locale}/`. Use an HTML parser (e.g. HtmlAgilityPack — add the NuGet if needed). Look up functions **and** types/members. Assemble tyhpdef `/** */` with `@link` + `@generated`. Missing manual or parse failure: **warning**, continue with Reflection comments. `--no-docs`: do not download.

**2.4 — Wire Track A into `GenerateTyhpdefAction`**

1. If `--php-targets` is set: Phase 8 path (or placeholder until Phase 8); must not be combined with `--php`
2. Else resolve one runtime:
   - `--php` set → `PhpRuntimeDetector.Inspect` (user binary)
   - else → `PhpRuntimeManager.Ensure` (managed)
3. If no runtime: **error** `TyhpdefPhpNotFound` / `TyhpdefPhpRuntimeDownloadFailed` — no Track B fallback, no PATH fallback
4. If the extension is not in `LoadedExtensions`: user binary → `TyhpdefGenerationError`; managed → `TyhpdefPhpExtensionNotProvisioned`
5. `PhpDelegationTyhpdefGenerator.GenerateFromExtension(...)`
6. `ComposerPackage` / `PhpSourceFiles` must not enter this generator
7. Parse the generated tyhpdef with the existing tyhpdef parser
8. Report results (including whether the runtime was managed vs `--php`)

**2.5 — Output file paths (CLI Track A / B)**

Resolution order:

1. `--output-file` if set (absolute, or relative to the output directory). Implies a single file (ignores `--split` except `file`).
2. Else `--split=file` (default) into the output directory:
   - Extension: `{outputDir}/Ext{ExtName}.tyhpdef` (e.g. `tyhpdef/ExtJson.tyhpdef`)
   - Composer package: `{outputDir}/{vendor}.{package}.tyhpdef`
   - `--source`: `{outputDir}/source.tyhpdef`
3. `--split=namespace`: `{outputDir}/{Dot.Namespace}.tyhpdef`; file-level/global declarations go in `{outputDir}/_global.tyhpdef` (or the primary Ext/package file if there is only one namespace)
4. `--split=type`: `{outputDir}/{Dot.Namespace.TypeName}.tyhpdef`; globals still `_global.tyhpdef`
5. Default `outputDir`: `{projectRoot}/tyhpdef/` if a project is loaded, else `{cwd}/tyhpdef/`
6. `--output` replaces `outputDir` (create it if missing)
7. Layer 2 always: `{outputDir}/overlays/stubs/` (independent of `--split`)
8. **Never** default to `runtime/php-extensions/`
9. If a target file exists and `--overwrite` is not set, warning and skip

### Acceptance Criteria

- [ ] `PhpRuntimeManager.Ensure()` downloads (or reuses cache) a managed CLI for a minor and reports version + loaded extensions
- [ ] Managed invoke uses `php -n -c <tyhp.ini>` (user ini is ignored)
- [ ] `tyhp generate_tyhpdef --ext-name=json` **without** `--php` does **not** require `PATH` php
- [ ] `PhpRuntimeDetector.Inspect()` only accepts `--php`; it does not search PATH / Homebrew
- [ ] `--php` + `--php-targets` is `TYHP7510`
- [ ] `--php=/nonexistent/php` is `TYHP7501`
- [ ] `tyhp generate_tyhpdef --ext-name=json` (managed PHP available) writes `tyhpdef/ExtJson.tyhpdef` (or `--output` equivalent)
- [ ] Generated file starts with `<?tyhpdef` and contains constants, functions, and class declarations
- [ ] Generated file parses through `TyhpParserAstVisitor.Tyhpdef.cs`
- [ ] Dumper JSON conforms to schema version 1 (union types, `PhpValue` defaults, traits/enums, attributes)
- [ ] `--ext-name=nonexistent` is a clear error
- [ ] `--ext-name=curl` when managed PHP cannot load curl is `TYHP7511` (not a silent PATH fallback)
- [ ] `--ext-name=json` without any PHP runtime (download failed / empty cache) is `TYHP7501` or `TYHP7508` (no Track B fallback)
- [ ] Unix/macOS provider order is StaticPHP prebuilt `php` first, Homebrew bottle extract (no `brew` binary) second; Windows never uses bottles
- [ ] StaticPHP resolver accepts the v3 rename (manifest + `dl.static-php.dev` + old `static-php-cli` GitHub URLs) without a code fork per name
- [ ] Each managed PHP cache directory contains `ATTRIBUTION.txt` naming the provider and artifact; repo `THIRD_PARTY.md` stays the catalog
- [ ] `--ext-name=json` without `--no-docs` emits real manual prose on `json_encode` (overview/`@link` to php.net), not an empty comment
- [ ] `--no-docs` omits `/**` blocks and does not require the manual cache
- [ ] `--overwrite` skip vs overwrite works
- [ ] Timing and declaration counts are reported
- [ ] All new files compile without errors

### Dependencies

- **Requires:** Phase 1; **Phase 5** (IR + writer)
- **Provides:** Working `tyhp generate_tyhpdef --ext-name=X` (Layer 1)

---

## Phase 2.5: Stub Harvest (Layer 2)




### Phase Overview

Harvest Psalm / PHPStan / Phan / PhpStorm stubs into **generated overlay** tyhpdefs next to Layer 1 output. This story delivers Layer 2 files; Story 21 loads `"overlay"` at bind time.

### Deliverables

- `Tyhp/Domain/Services/StubHarvestTyhpdefEnricher.cs` — match FQNs, merge into overlay IR, write files
- `{outputDir}/overlays/stubs/*.tyhpdef` plus `{outputDir}/SOURCES.md` (or `NOTICE`) with URLs and licenses
- When Track C / a future `package.tyhp.json` is written for extension packages (Story 21), list stub globs **first** in `"overlay"`

### Implementation Details

- Input: Layer 1 IR (or the just-written baseline file) + stub trees (URLs in `runtime/README.md`)
- Prefer a **vendored snapshot** so CI need not hit the network
- Match by FQN; enrich **types** only when Layer 1 is weak (`array`, `mixed`, `callable`, untyped)
- Enrich **prose** only when Layer 1 (manual / source) left that piece empty (summary, `@param` text, `@return` text, `@throws`)
- Psalm+PHPStan consensus; PhpStorm for gaps; Phan as extra signal; no consensus → keep Layer 1 and warn
- **Do not** overwrite `{outputDir}/overlays/*.tyhpdef` outside `stubs/`
- **Do not** overwrite `runtime/php-extensions/overlays/`
- Attribution headers on every generated stub overlay file

### Acceptance Criteria

- [ ] Generating `--ext-name=json` also writes stub-overlay file(s) under `tyhpdef/overlays/stubs/` (when stub snapshot is present)
- [ ] `SOURCES.md` / `NOTICE` lists corpora URLs and licenses
- [ ] Hand-written overlay paths are not touched
- [ ] Stub overlays do **not** replace a non-empty `json_encode` summary that came from the php.net manual
- [ ] Dogfood one extension (json or filter) end-to-end Layer 1+2 before Core/Standard

### Dependencies

- **Requires:** Phase 2 (Layer 1 JSON/IR), Phase 5 (writer)
- **Provides:** Layer 2 overlay files for Story 21 to load

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

PHPDoc type expressions can be complex. Parse them into a normalized string suitable for tyhpdef output. **Deterministic simplifications (lock):**

| PHPDoc form | Tyhpdef output |
|-------------|----------------|
| Simple named / `?T` / `A\|B` / `A&B` / `(A&B)\|null` | Preserve |
| `array<K, V>`, `list<T>`, `Collection<T>`, `\Generator<…>` | Preserve |
| `string[]` | `array<int, string>` |
| `array{name: string, age: int}` | `array` (shapes are not tyhpdef-MVP; Layer 2/3 may enrich later) |
| `callable(int, string): bool` | `callable` (callable signatures are not tyhpdef-MVP) |
| `class-string<T>` | `string` |
| `'foo'\|'bar'`, `0\|1`, `true`, `false` | Preserve when the tyhpdef grammar accepts literals; else `string` / `int` / `bool` |
| Anything else unrepresentable | `mixed` |

The parser does not fully resolve types — it produces a string that is valid tyhpdef type syntax.

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

Implement Track B: a pure C# tyhpdef generator that parses PHP source files using the existing ANTLR PHP parser/visitor, walks the resulting ASTs to extract type information from PHP type hints and PHPDoc annotations, and produces `.tyhpdef` output. **`--package-path` is this track:** read `composer.json`, collect autoloaded PHP files, then parse them. PHP is not invoked.

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
- Extract: doc comment → **copy onto IR** (Track B primary prose). Parse with `PhpDocParser` for **types** and tags
- Extract: `@template` tags for generic parameters
- Recurse into class body for members

**For method declarations:**
- Extract: name, visibility, static/abstract/final modifiers
- Extract: parameter list (names, type hints, default values, variadic, by-reference, promoted)
- Extract: return type hint
- Extract: doc comment → **copy** as tyhpdef `DocComment`; parse `@param`, `@return`, `@throws`, `@template` for types. Adjust type fragments in the copied comment to match the merged signature; do not invent a new summary
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

1. If mode is `PhpSourceFiles`:
   - Discover PHP source files from `options.SourcePaths` (glob patterns)
2. If mode is `ComposerPackage`:
   - Resolve `--package-path` to a directory containing `composer.json`
   - Collect PHP files from **`autoload`**; if `--include-dev`, also `autoload-dev`:
     - `psr-4` / `psr-0`: all `.php` files under each mapped directory (recursive)
     - `classmap`: each listed file, or all `.php` files under each listed directory
     - `files`: each listed file
   - Skip nested `vendor/` directories inside the package
   - If `composer.json` is missing or has no autoload paths (and no autoload-dev when `--include-dev`), error `TyhpdefGenerationError`
3. Parse collected files using `CompilationService.ParseFiles()` (existing infrastructure; already accepts `.php`). **Every collected path must end in `.php`** (case-insensitive). `.tyhp`, `.tyhpdef`, and any other extension → error `TyhpdefSourceNotPhp` (7507) naming the file. Do not silently skip.
4. Create `NativeTyhpdefGenerator` with options and diagnostics
5. Call `generator.Generate()` and write output (paths from Phase 2.5)
6. Report results

`--no-php` is a no-op here. Track A is never used for packages.

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
- [ ] Running `tyhp generate_tyhpdef --source=./foo.tyhp` (or any non-`.php`) reports `TYHP7507`
- [ ] Running `tyhp generate_tyhpdef --package-path=./vendor/…` reads `composer.json` autoload and produces a parseable tyhpdef (no PHP process)
- [ ] `--package-path` without `composer.json` reports a clear error
- [ ] Class declarations include extends, implements, constants, properties, methods
- [ ] Method signatures include parameter types (from type hints or PHPDoc), return types, and modifiers
- [ ] PHPDoc `@param` and `@return` types are used when PHP type hints are absent
- [ ] PHPDoc `@template` tags produce generic parameter declarations in the tyhpdef output
- [ ] Source `/** */` comments are copied onto the tyhpdef (unless `--no-docs`); type tags may be adjusted to the merged signature
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

Create a shared tyhpdef output writer that formats extracted type information into valid `.tyhpdef` syntax. Used by **Track A, Track B, Track C, and Layer 2**. Implement this before those phases.

**5.1 — Define `TyhpdefDeclaration` Intermediate Representation**

New file: `Tyhp/Domain/Services/TyhpdefDeclaration.cs`

Records (all lists default empty; strings default `""` / `null` as appropriate):

- `TyhpdefFile` — `string Header`, `List<string> FileAttributes`, `List<TyhpdefDeclareBlock> DeclareBlocks`, `List<TyhpdefNamespace> Namespaces`, `List<TyhpdefConstant> GlobalConstants`, `List<TyhpdefFunction> GlobalFunctions`, `List<TyhpdefTypeAlias> TypeAliases`, `List<TyhpdefClassDeclaration> GlobalTypes`
- `TyhpdefDeclareBlock` — `string Constraint` (e.g. `php=">=8.3"`), `List<TyhpdefNamespace> Namespaces`, plus the same declaration lists as a file (Phase 8; unused until 20.5)
- `TyhpdefNamespace` — `string Name`, `List<TyhpdefClassDeclaration> Classes`, `List<TyhpdefFunction> Functions`, `List<TyhpdefConstant> Constants`, `List<TyhpdefTypeAlias> TypeAliases`
- `TyhpdefGenericParameter` — `string Name`, `string? Constraint`, `string? Default` (Default unused until Story 28; emit only the name + constraint for now)
- `TyhpdefAttribute` — `string Name`, `List<string> Arguments`
- `TyhpdefTypeAlias` — `string Name`, `string AliasedType`, `List<TyhpdefGenericParameter> GenericParameters`, `string? DocComment`
- `TyhpdefClassDeclaration` — `string Kind` (`class` / `interface` / `trait` / `enum` / `struct` / `extension`), `string Name` (PHP name for `class` / Tyhp name for `extension`), `string? AsAlias` (`class PhpName as TyhpName` — Tyhp name when present; used for `__tyhpExtensionBacker`), `bool IsPartial`, `List<string> Modifiers`, `List<TyhpdefAttribute> Attributes`, `string? Extends`, `List<string> Implements`, `List<string> Uses`, `List<TyhpdefGenericParameter> GenericParameters`, `string? BackingType`, `List<TyhpdefConstant> Constants`, `List<TyhpdefProperty> Properties`, `List<TyhpdefMethod> Methods`, `List<TyhpdefMethod> Operators`, `List<TyhpdefExtensionMember> ExtensionMembers`, `List<TyhpdefEnumCase> EnumCases`, `string? DocComment`, `bool IsDeprecated`, `bool IsObsolete`, `string? PhpGate`
- `TyhpdefMethod` — `string Name`, `List<string> Modifiers`, `List<TyhpdefAttribute> Attributes`, `List<TyhpdefParameter> Parameters`, `string ReturnType`, `List<TyhpdefGenericParameter> GenericParameters`, `List<TyhpdefMethod> Overloads`, `string? DocComment`, `bool IsDeprecated`, `bool IsObsolete`, `bool ReturnsReference`, `bool IsAsync`, `string? PhpGate`
- `TyhpdefExtensionMember` — `string Kind` (`function` / `fn` / `operator`), `string Name`, `string? OperatorTarget` (`<string>` on standalone tyhpdef `extension` operators; null for class-body inline operators), `List<TyhpdefParameter> Parameters`, `string ReturnType`, `List<TyhpdefGenericParameter> GenericParameters`, `string? Body` (class-body inline: brace or `=>`; standalone tyhpdef `extension`: **`=> expr` only**), `string? DocComment`, `bool IsDeprecated`, `bool IsObsolete`
- `TyhpdefProperty` — `string Name`, `string Type`, `List<string> Modifiers`, `List<TyhpdefAttribute> Attributes`, `string? GetterHint`, `string? SetterHint`, `string? DocComment`, `bool IsDeprecated`
- `TyhpdefConstant` — `string Name`, `string Type`, `string? Value`, `bool UsesCoalesce` (`??` import-const form), `List<string> Modifiers`, `string? DocComment`, `bool IsDeprecated`
- `TyhpdefParameter` — `string Name`, `string Type`, `string? DefaultValue`, `bool IsVariadic`, `bool IsByReference`, `bool IsPromoted`, `List<TyhpdefAttribute> Attributes`
- `TyhpdefFunction` — same as `TyhpdefMethod` plus optional `bool IsExtension` (standalone `function …(extends T $this, …)`)
- `TyhpdefEnumCase` — `string Name`, `string? BackingValue`, `string? DocComment`

Phase 8 may set `PhpGate` / `DeclareBlocks`; Tracks A–C leave them empty until 20.5.

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
- Structs: `struct <name> { … }`
- Type aliases: `type <name> = <type>;`
- Inline extensions on a class: `extension function` / `extension fn` / `extension operator` with bodies (Story 03)
- Standalone tyhpdef `extension Name { function … => …; operator *<T>(…) => …; }` (this story; short form only)
- `partial class` / `enum` / `interface` / `trait` (this story; additive in `include`)
- Operator overloads: operator methods in the class body
- `class PhpName as TyhpName` (existing alias; Track C uses `as Name__tyhpExtensionBacker` for compiled extensions)
- File/member PHP gates: `declare(php=…)` / `#[\Tyhp\Php(…)]` (emit only when Phase 8 / Story 20.5 sets them)

**5.4 — Output File Splitting Strategy**

CLI Track A/B honors `--split` (Phase 2.5). The writer accepts either one `TyhpdefFile` or a sequence of files (caller splits IR by namespace/type).

Track C: always **one** `{buildOutputDir}/package.tyhpdef`. No `--split`. No extra `include` entries.

### Acceptance Criteria

- [ ] `TyhpdefOutputWriter.Write()` produces valid tyhpdef syntax from `TyhpdefFile` input
- [ ] Output is valid tyhpdef grammar (see `TyhpParser.g4`); do not require byte-identity with hand-enriched `php8.2.9/` files
- [ ] Constants, functions, classes, interfaces, traits, and enums are correctly formatted
- [ ] Doc comments are included in the output (when `IncludeDocComments` option is true)
- [ ] Deprecated items are marked with the `deprecated` keyword
- [ ] Namespace blocks wrap their contents correctly
- [ ] Generated output parses without errors through the tyhpdef parser
- [ ] The writer handles edge cases: empty classes, interfaces with no methods, enums with no cases
- [ ] All new files compile without errors

### Dependencies

- **Requires:** Nothing (standalone utility)
- **Provides:** Shared IR + formatting for Phases 2, 2.5, 4, and 6

---

## Phase 6: Tyhpdef Generation from Tyhp Code (Track C)




### Phase Overview

Implement Track C: extract the public API from bound, checked Tyhp ASTs and write type-definition artifacts. Track C is **the source** (Tyhp code), not a layout strategy. This runs after the checker and **before** the optimizer (Story 23).

### Output Structure

A `"type": "library"` build **always** writes these into the **build output directory**. CLI `generate_tyhpdef` uses `{projectRoot|cwd}/tyhpdef/` and is Tracks A/B only.

```
build/                          # library
├── package.tyhpdef
├── package.tyhp.json
├── composer.json
└── src/
```

```
build/                          # application with build.generateTyhpdef = true
├── package.tyhpdef             # only this — no package.tyhp.json
├── composer.json               # if the app build emits one
└── src/
```

**`package.tyhp.json` schema (this story emits at least these keys):**

```json
{
    "include": [
        "./package.tyhpdef"
    ],
    "exclude": [],
    "overlay": [],
    "source": {
        "tagless": false
    }
}
```

| Key | Meaning |
|-----|---------|
| `include` | Globs relative to the package root. Libraries emit `["./package.tyhpdef"]` only. Applications do not emit this file. |
| `exclude` | Globs subtracted from `include` matches. Emit `[]` until there is something to exclude. Loader support is Story 06 if not already present. |
| `overlay` | Ordered overlay globs; last wins (Story 21). Library Track C emits `[]` unless the library ships overlays. |
| `source.tagless` | Package's own tagless setting (Story 06 already reads this and top-level `tagless`). Emit `false` unless the library was compiled tagless. |

Further keys may be added later. Consuming Tyhp looks for `vendor/{vendor}/{package}/package.tyhp.json` — **discovery and load are Story 06**, not this story.

Project layout:

```
my-library/
├── tyhp.json
├── tyhp_src/
└── build/
    ├── composer.json
    ├── package.tyhp.json
    ├── package.tyhpdef
    └── src/*.php
```

### Entrypoint Detection and Project Type Rules

| Project Type | `build.generateTyhpdef` | Story 20 behavior |
|-------------|------------------------|-------------------|
| `library` | ignored (any value) | **Always** write `package.tyhpdef` **and** `package.tyhp.json`. If generation sees entrypoint files, `TyhpdefLibraryEntrypointDetected` (7505) and fail that generate step. |
| `application` | `false` / unset | No Track C. |
| `application` | `true` | Write **only** `package.tyhpdef` in the build output. No `package.tyhp.json`. |

**Entrypoint:** root-level statements that are not pure declarations. `function_exists` / `class_exists` declaration guards are **not** entrypoints.

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
    - Copy source doc comments onto IR (`DocComment`); `--no-docs` omits them
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
- Use `TyhpdefOutputWriter` (Phase 5) to format **one** `TyhpdefFile`
- Write `{buildOutputDir}/package.tyhpdef`
- If the project is a **library**, also write `{buildOutputDir}/package.tyhp.json`. Applications never write the manifest.
- Skip private members. Do not implement Story 25 `internal` filtering.
- Generic parameters: name + constraint only (no `= default` until Story 28)
- Regular methods: signatures only
- If the Tyhp **source** declared a standalone `extension { }`, emit the backer `class` + tyhpdef `extension { }` **in** `package.tyhpdef` (that is the library API). Do not emit extra sidecar `.tyhp` files or extra `include` globs. Hand overlays after generation are the author's manifest problem.

**Tyhpdef file content rules — `package.tyhpdef` must:**
- Include `<?tyhpdef` header with auto-generated comment, compiler version, and timestamp
- Include only the public/protected API (no private members)
- Include only type signatures for ordinary methods (no method bodies)
- Include generic type parameters with constraints (defaults: Story 28)
- Include `deprecated` / `obsolete` markers where applicable

**Extension method and operator handling:** see **6.6**. Track A/B never emit Tyhp extension syntax. Track C emits a compiled `class PhpName as Name__tyhpExtensionBacker` plus a tyhpdef `extension Name { }` with short mappings. `include` stays `["./package.tyhpdef"]`. Do not generate overlay files for extension attachments. `partial` in this package’s `include` is only for additive members on types declared in **other** tyhpdefs (not for standalone extensions).

**6.2 — Symbol-to-TyhpdefDeclaration Mapping**

Map binder symbols to tyhpdef intermediate representations:

| Symbol Type | Tyhpdef Declaration |
|-------------|-------------------|
| `ObjectDeclarationSymbol` (class) | `TyhpdefClassDeclaration` with `Kind = "class"` |
| `ObjectDeclarationSymbol` (interface) | `TyhpdefClassDeclaration` with `Kind = "interface"` |
| `ObjectDeclarationSymbol` (trait) | `TyhpdefClassDeclaration` with `Kind = "trait"` |
| `ObjectDeclarationSymbol` (enum) | `TyhpdefClassDeclaration` with `Kind = "enum"` |
| `ObjectDeclarationSymbol` (struct) | `TyhpdefClassDeclaration` with `Kind = "struct"` |
| `ObjectDeclarationSymbol` (extension) | `TyhpdefClassDeclaration` with `Kind = "class"`, `AsAlias = Name + GeneratedNames.ExtensionBackerSuffix`, plus a second `TyhpdefClassDeclaration` with `Kind = "extension"` (short mapped members). Never emit `use extension`. |
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
- **Type aliases:** Output as `type X = Y;` declarations
- **Class-body operator overloads (Tyhp `operator +` on the type):** mapped `#[\Tyhp\Optimize\Inline] extension operator` bodies targeting the compiled `__add` (etc.) — see 6.6. **Not** bodyless native `operator` unless the PHP type already has the operator in the engine.
- **Standalone `extension { }` blocks:** emitted as (1) `class PhpName as Name__tyhpExtensionBacker` for the compiled PHP class, plus (2) tyhpdef `extension Name { … => Backer::method(…) }` — see 6.6. Never emit `use extension` for the library’s own extensions.
- **Generic constraints:** Output as `<T extends Constraint>` on the declaration
- **Property accessors:** Output accessor hints on properties
- **Async functions:** Note the `async` keyword and `Promise<T>` return type
- **Disposable types:** Note the `IsDisposable` interface implementation

**6.4 — Wire into BuildAction**

File: `Tyhp/CLI/BuildAction.cs`

After the checker completes but **before** the optimizer (Story 23), at the `PLACEHOLDER_STORY_20` marker:

1. Decide whether Track C runs:
   - **library:** always (ignore `build.generateTyhpdef`)
   - **application:** only if `project.Build.GenerateTyhpdef == true`
2. If Track C is running and the project is a `library`, scan parsed files for entrypoints; if found, `TyhpdefLibraryEntrypointDetected` and skip writing artifacts.
3. Create `TyhpCodeTyhpdefGenerator` with the bound `GlobalScope`
4. Call `generator.Generate()`
5. Write `{buildOutputDir}/package.tyhpdef`
6. If library: write `{buildOutputDir}/package.tyhp.json`
7. Report generation results in the build summary

**6.5 — `build.generateTyhpdef` Configuration**

Use the existing `BuildConfig.GenerateTyhpdef` property (Story 10). Do not re-define it.

- **Library:** the property is **ignored**. Track C always runs.
- **Application:** Track C runs only when the property is `true`. Story 10's `??= (Type == Library)` default must not be used as the Track C gate for libraries (libraries generate even if someone set `false`).

**6.6 — Extension Method and Extension Operator Handling (Track C)**

Existing alias semantics stay `class PHP_NAME as TYHP_NAME`. Track C uses that so the compiled PHP class keeps its PHP name while the Tyhp extension occupies `StringOperators`.

Add `GeneratedNames.ExtensionBackerSuffix = "__tyhpExtensionBacker"` (same family as `__tyhpGeneric`). Collision with a user type of that Tyhp name is an error. The suffix is Tyhp-only; it never appears in emitted PHP.

**Owned class operators** (Tyhp `class Money { operator +(self $a, int $b): self { … } }` → PHP `Money::__add`). Keep class-body inline members. Put `#[\Tyhp\Optimize\Inline]` on these so consumer emit splices the mapping and does not wrap another forwarding method. Grammar today does not allow attributes on `tyhpdefExtensionFunction` / `tyhpdefExtensionOperator` — add them on **class-body** inline members in this story.

```tyhp
<?tyhpdef

class \MyLib\Money {
    public static function __add(self|int $l, self|int $r): static|int;

    #[\Tyhp\Optimize\Inline]
    extension operator +(self $a, int $b): self => self::__add($a, $b);
}
```

Do **not** invent a standalone `extension MoneyOps` for operators that were declared on the type itself.

**Standalone Tyhp `extension StringOperators { … }`:** emit both the PHP backer and a tyhpdef `extension` (this story **legalizes** `extension Name { }` in `.tyhpdef`, reversing the Story 03 ban).

```tyhp
<?tyhpdef

class StringOperators as StringOperators__tyhpExtensionBacker {
    public static function toUpper(string $this_): string;
    public static function toSpecialString(\App\MyClass $this_): string;
    public static function __multiply<T extends string|array>(T $l, int $r): T;
}

extension StringOperators {
    function toUpper(extends string $this): string
        => StringOperators__tyhpExtensionBacker::toUpper($this);
    function toSpecialString(extends \App\MyClass $this): string
        => StringOperators__tyhpExtensionBacker::toSpecialString($this);
    operator *<string>(self $left, int $right): string
        => StringOperators__tyhpExtensionBacker::__multiply($left, $right);
    operator *<array>(self $left, int $right): array
        => StringOperators__tyhpExtensionBacker::__multiply($left, $right);
}
```

Rules for tyhpdef `extension { }`:

- Members are `function` / `operator` (Tyhp extension grammar), **not** class-body `extension function`.
- Every member **must** use short `=>` syntax and **must** have a return type. Brace bodies are illegal. Empty `extension Name {}` is an error.
- Every member is **implicitly inline**: consumer emit splices the mapping expression. Do not emit `#[\Tyhp\Optimize\Inline]` on these members (redundant). `deprecated` / `obsolete` are allowed.
- `$this` / `self` mean the **target** (`extends` / `<Type>`), same as Tyhp `extension { }`. Mapping callees use the **Tyhp** backer alias (`StringOperators__tyhpExtensionBacker::…`); emit rewrites that to PHP `StringOperators::…`.
- Any `=>` expression is legal (`Backer::toUpper($this)` or `\strtoupper($this)`). Track C prefers the compiled backer method when the emitter produced one. Hand tyhpdefs may map straight to a builtin and omit a backer class.
- Allow the full Tyhp extension operator set in short form: binary, unary, `convert`, with `<Type>`.
- Do **not** emit bodyless `function toCamelCase(extends string $str): string;` for compiled extensions — that form still means “this PHP function *is* the extension.” Track C never uses it for this.
- No `partial extension` in this story. Duplicate `extension StringOperators` in base / `include` is an error. Overlay may replace the whole extension later (Story 21).

**`#[Inline]` on Tyhp source vs emit:** `#[\Tyhp\Optimize\Inline]` on the library’s Tyhp methods is a call-site splice when **compiling that library**. It must **not** omit the PHP method from the backer class (a later consumer or PHP caller may need it). Consumer emit splices the **tyhpdef mapping** (`StringOperators::toUpper($x)`), not a second wrapper. Inlining through the backer into `\strtoupper` is a later optimizer, not this story.

**Backer PHP shape:** pick the most precise tyhpdef view of the compiled method, in this order:

1. **Generic** when related overloads share a type parameter (`__multiply<T extends string|array>(T $l, int $r): T`). Prefer this. If `T extends string|array` is not a legal constraint, do not invent a new constraint form — fall through.
2. **Overloads** on the backer static method (tyhpdef overload signatures). This should always be possible when (1) is not.
3. **Union** (`string|array`) only if neither (1) nor (2) can be expressed. Unions may fail to narrow returns in the checker; treat that as a generator bug to fix via (1) or (2), not as the normal path.

Tracks A/B looking at PHP only ever see the union; they do not invent generics.

**`partial` in base tyhpdefs (this story):** legal on `class` / `enum` / `interface` / `trait` in `include` files. Additive members only; does not change generics / `extends` / `implements` / flags. Duplicate member → error. **Missing target type → error** (the type must already exist in this compilation’s include set). Include-load order among additive partials does not matter. `omit` is illegal here. **Not** used for standalone extension attachments. Overlay last-wins + `omit` + “missing target → warning, skip” are Story 21. Do **not** add `partial if exists` or a third keyword — optional additive members belong in an overlay.

**`global use` (this story):** `global` prefixes any import and applies to every file in the compilation (C# `global using`). Distinct from PHP `global $var`.

```tyhp
global use App\Models\User;
global use function App\Helpers\formatDate;
global use const App\Config\MAX_RETRIES;
global use extension \Tyhp\StringExtensions;
global use extension \Tyhp\StringExtensions {
    toSnakeCase hide;   // compilation-wide hide; Story 21 will not need this
};
```

When a **loaded tyhpdef** contains `global use extension X`, every `.tyhp` file in that compilation sees `X` as imported. Story 21 uses this so `tyhp/php` scalar methods are always on. Track C **must not** emit `global use extension` for the library’s own extensions — consumers still write `use extension StringOperators` (or hide/`insteadof`) so two `*<string>` packages can coexist.

**Local `use` overlaying `global use`:** a non-`global` `use` / `use function` / `use const` / `use extension` in a file (or braced namespace) may re-import something already in scope via `global use` **so that scope can mutate it**. Mutations are:

- An `as` alias that changes the short name (`use App\Models\User as UserModel`, `use extension StringExtensions as SE`)
- A non-empty adaptation block (`as` method rename, postfix `hide`, `insteadof`)

Those mutations apply **only** to that file / namespace. Other files still see the unadapted global import. This is how a caller hides or prefers a member of a compilation-wide extension without editing the tyhpdef.

A local `use` that does **not** mutate is redundant and must **warn** (not error). No mutation means: same imported FQN, no `as` (or `as` to the same short name the global import already uses), and either no `{ }` or an empty `{ }`. Group `use` warns **per name** that is globally included and unmutated; names in the same group that do alias or adapt are silent. The statement remains valid.

```tyhp
// Assume a loaded tyhpdef already has:
//   global use extension \Tyhp\StringExtensions;
//   global use App\Models\User;

use extension \Tyhp\StringExtensions;           // warning — already globally included
use App\Models\User;                            // warning — already globally included
use extension \Tyhp\StringExtensions { };       // warning — empty adaptations

use App\Models\User as UserModel;               // OK — local alias
use extension \Tyhp\StringExtensions {
    StringExtensions::toSnakeCase hide;
    StringExtensions::toUpper as toCC;
};                                              // OK — scope-local hide / rename
```

Register `CheckerRedundantGlobalImport` in `MessageCode.cs` (next free checker code beside the existing import diagnostics). Message should state that the import is not needed because the item is globally included.

**Consumption: `use extension` is opt-in unless `global use extension` is in a loaded tyhpdef.**

Two in-scope extensions that both declare `toUpper` on `string` or `operator *<string>` is a conflict (same idea as today’s conflicting extension methods). Adaptations:

| Form | Methods | Operators |
|------|---------|-----------|
| `Foo::bar as baz;` | rename | **Error** — operators cannot be renamed; use `hide` or `insteadof` |
| `Foo::bar hide;` / `operator *<string> hide;` | exclude | exclude |
| `A::bar insteadof B;` / `A::operator *<string> insteadof B;` | pick winner | pick winner |

`hide` is a **contextual** keyword only inside `{ }` adaptations (so `function hide()` elsewhere stays valid). Do **not** reuse overlay `omit`. Do **not** use PHP trait `as private`.

```tyhp
use extension StringOperators {
    StringOperators::toUpper as toCC;
    StringOperators::toSnakeCase hide;
    StringOperators::operator *<string> hide;
    operator *<array> hide;   // qualifier optional when this `use` lists one extension
};

use extension StringOperators, RepeatOps {
    RepeatOps::operator *<string> insteadof StringOperators;
};
```

- `operator *<string> hide` hides that target only. `operator * hide` hides every `*` member on that extension.
- `operator convert hide` hides every convert on that extension. Finer-grained convert hide is out of scope.
- `operator +<Money> hide` hides all `+` members for that target from this extension (unary and binary together).
- Hide of a name that extension does not declare is an error.
- `insteadof` RHS may be any extension **in scope**, including a same-file `extension { }` that was not listed on this `use` — so a file that *declares* `*` can prefer another extension’s `*`.
- Class-body tyhpdef `use extension Foo { operator + hide; }` still auto-activates **that class’s** attached surface for consumers (Story 03), minus hidden members.

**Grammar this story must add:**

1. Tyhpdef `extension Name { }` with short `function` / `operator` members (`=>` required, return type required).
2. Attributes on **class-body** tyhpdef `extension function` / `extension operator` (for owned-type `#[Inline]`).
3. `partial` on tyhpdef `class` / `enum` / `interface` / `trait` in any tyhpdef (binder: include = additive, duplicate error, missing target error; overlay mode is Story 21).
4. `use extension` adaptations: postfix `hide`; `operator` + token + optional `<Type>` in method references; `insteadof` on those refs; `as` on operators → error. Same syntax in `.tyhp` and tyhpdef.
5. `global use` / `global use function` / `global use const` / `global use extension` (compilation-wide). Distinct from PHP `global $var`. Local (non-`global`) `use` of a globally included symbol with alias or non-empty adaptations applies those mutations in that file / namespace only; the same `use` with no mutation warns (`CheckerRedundantGlobalImport`).
6. `GeneratedNames.ExtensionBackerSuffix`.

**Do not emit:** `use extension StringOperators` or `global use extension …` from Track C; file-level mapped `extension function`; `scalar T { }`; `_tyhpdef/support/` sidecars; fictional `__TyhpInlineExt_*` names; generated overlay files solely to attach extensions. Map to the real compiled class / `__add` / static method the emitter already produced.

**Do not omit** emitting the PHP backer method because it was `#[Inline]` in Tyhp source.

**6.7 — Consumption still out of scope (except 6.6 grammar)**

Story 20 **writes** `package.tyhp.json` and **implements** the grammar/binder rules in 6.6 (`extension` in tyhpdef, base `partial`, `hide` / operator adaptations). Vendor scan, glob load, overlay last-wins/`omit`, and `8025` duplicate FQN remain Stories 06 / 21. Do not re-implement overlay load here.

### Acceptance Criteria

- [ ] `TyhpCodeTyhpdefGenerator.Generate()` produces tyhpdef declarations from bound Tyhp symbols
- [ ] Public classes, interfaces, traits, enums, functions, and constants are included
- [ ] Private members are excluded from tyhpdef output
- [ ] Tyhp source `/** */` comments are copied onto `package.tyhpdef` (unless `IncludeDocComments` is false)
- [ ] Generic parameters and constraints are correctly represented (no generic defaults until Story 28)
- [ ] Tyhp-specific constructs (structs, extensions, type aliases, operator overloads) appear in the output
- [ ] Library projects **always** write `package.tyhpdef` and `package.tyhp.json` in the build output (`build.generateTyhpdef` ignored)
- [ ] Application + `build.generateTyhpdef=true` writes `package.tyhpdef` only (no `package.tyhp.json`)
- [ ] When Track C runs on a library, entrypoint files report `TYHP7505`
- [ ] Library `package.tyhp.json` includes `include`, `exclude`, `overlay`, and `source.tagless`
- [ ] Library `include` is `["./package.tyhpdef"]`; do not emit generated overlay files for extension attachments
- [ ] Class-body operators map with `#[\Tyhp\Optimize\Inline] extension operator … => self::__add(…)` (or the real compiled name)
- [ ] Standalone `extension { }` becomes `class PhpName as Name__tyhpExtensionBacker` plus tyhpdef `extension Name { … => Backer::… }` — never `use extension` from Track C, never file-level mapped `extension function`, never `scalar`
- [ ] Tyhpdef `extension` members are short `=>` only, return types required, implicitly inline; empty extension is an error
- [ ] `use extension` adaptations: `hide` and operator `insteadof` parse; `as` on an operator is an error
- [ ] Base `partial` parses in `include` tyhpdefs (additive; duplicate member error; missing target error; `omit` illegal)
- [ ] `global use extension` parses in `.tyhp` and `.tyhpdef`; Track C does not emit it for the library’s own extensions
- [ ] Local (non-`global`) `use` of a globally included symbol with an alias or non-empty adaptations applies those mutations in that file / namespace only
- [ ] Local `use` of a globally included symbol with no alias and no adaptations warns (`CheckerRedundantGlobalImport`); empty `{ }` counts as no mutation
- [ ] `--source` / `--package-path` of a non-`.php` file reports `TYHP7507`
- [ ] Generated `package.tyhpdef` parses without errors through the tyhpdef parser
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
- Modified `Tyhp/CLI/GenerateTyhpdefAction.cs` — Add `--verify` mode: regenerate golden and check that existing tyhpdefs are **compatible** with it (not a raw text diff)
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

**7.2 — Add Tyhpdef Verification Mode (`--verify`)**

`--verify` regenerates Layer 1 (the golden PHP API), then builds the **final** existing API by applying overlays, then compatibility-checks that final form against the golden.

Stubs are **overlays**, not edits to Layer 1. If they appear in the overlay list, they are applied like any other overlay (order, last wins).

**Overlay list for verify:**

1. If a `package.tyhp.json` is in play (library build output, or `{output}/package.tyhp.json`), use its `"overlay"` array (and `include` / `exclude` as Story 06 would).
2. Else CLI convention: apply `{output}/overlays/stubs/*.tyhpdef` first, then `{output}/overlays/*.tyhpdef` (non-recursive, not `stubs/`).

**Then:**

1. Every golden symbol must appear in the **final** API, **unless** an overlay `omit`s it — `omit` is not an error.
2. A golden symbol that is simply absent (no omit) → fail `TyhpdefVerifyIncompatible` (7506).
3. Matching callables: final param/return types must accept every value the golden PHP signature accepts (generic refinement / extra overloads OK if they still cover the PHP shape; illegal narrowing fails).
4. Extra overlay-only members (operators, `exit`/`die`, Tyhp-only) are allowed.
5. Kind mismatch fails.
6. Compare merged IR/symbols, not file text. Hand-enriched `php8.2.9/` will not be byte-identical and that is not the criterion.

**7.3 — Validate Track A (PHP Delegation)**

Test the full Track A pipeline:

1. Generate tyhpdef for a common PHP extension (e.g., `json`, `date`, `pcre`)
2. Parse the generated file through the tyhpdef parser
3. Verify the file contains expected declarations (classes, functions, constants)
4. Verify `json_encode` (and a `DateTime` method) have assembled manual `/** */` with `@link` unless `--no-docs`
5. Compare against bundled files with `--verify` only as a **compatibility** check (hand enrichments must still be compatible; text mismatch is expected)
6. Test error handling: non-existent extension, PHP not available

**7.4 — Validate Track B (C# Native)**

Test the full Track B pipeline:

1. Generate tyhpdef from the PHP example files in `Examples/*.php`
2. Parse the generated files through the tyhpdef parser
3. Verify extracted types match expected signatures
4. Test PHPDoc-only types (no PHP type hints)
5. Test mixed type sources (PHP hints + PHPDoc)
6. Test that source `/** */` summaries are copied (and still present after type-tag merge)
7. Test complex constructs: generics via `@template`, union types, intersection types

**7.5 — Validate Track C (From Tyhp Code)**

Test the full Track C pipeline (requires Story 02 binder):

1. Build a simple Tyhp project with `build.generateTyhpdef = true`
2. Verify tyhpdef files are generated alongside PHP output
3. Parse the generated tyhpdef files
4. Verify public API declarations are present and correct
5. Verify Tyhp source doc comments were copied onto those declarations
6. Load the generated tyhpdef in a second Tyhp project and verify name resolution works

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

Implement / complete `GenerateTyhpdefHelp()` (Story 13 left a stub that documents `--composer-package` and a "not available" paragraph):

- Replace `--composer-package` with `--package-path`
- Document `--ext-name`, `--package-path`, `--source`, `--output`, `--output-file`, `--split`, `--php`, `--php-version`, `--php-targets`, `--php-runtime-dir`, `--include-dev`, `--locale`, `--no-docs`, `--include-internal`, `--overwrite`, `--validate`, `--verify`, `--no-php-runtime-update`, `--refresh-snapshots`
- Examples:
  - `tyhp generate_tyhpdef --ext-name=curl` (managed PHP; no PATH php required)
  - `tyhp generate_tyhpdef --ext-name=curl --php-version=8.3`
  - `tyhp generate_tyhpdef --ext-name=curl --php-targets=8.2,8.3,8.4,8.5` (gated; managed only)
  - `tyhp generate_tyhpdef --ext-name=myext --php=/opt/php/bin/php` (private ext; ungated)
  - `tyhp generate_tyhpdef --package-path=./vendor/guzzlehttp/guzzle --output=./tyhpdef/`
  - `tyhp generate_tyhpdef --source=./lib/**/*.php --split=namespace`
  - `tyhp generate_tyhpdef --validate ./tyhpdef/`
- Document: Track A default = Tyhp-managed PHP (isolated ini); `--php` = single-target user binary, illegal with `--php-targets`; Track B = `--source` / `--package-path`; default output `{projectRoot|cwd}/tyhpdef/`; `--split=file|namespace|type`; `--verify` applies overlays and treats `omit` as intentional; Track A doc comments come from the php.net manual (`--locale`); `--no-docs` omits `/** */` and skips the manual download

### Acceptance Criteria

- [ ] `tyhp generate_tyhpdef --validate ./runtime/php-extensions/php8.2.9/` correctly reports pass/fail for all 16 bundled extension tyhpdefs
- [ ] `tyhp generate_tyhpdef --validate ./DebugProject/tyhpdef_gen/` reports results for all 72 Composer package tyhpdefs
- [ ] Track A end-to-end: `--ext-name=json` produces a file that parses, contains all expected `json_*` functions, and (without `--no-docs`) has php.net prose on `json_encode`
- [ ] Track B end-to-end: `--source=Examples/*.php` produces parseable tyhpdef files with correct type signatures
- [ ] Track C end-to-end: a Tyhp build with `build.generateTyhpdef=true` produces loadable tyhpdef files (when Story 02 binder is available)
- [ ] The `--verify` mode reports incompatible enrichments / missing golden API and exits non-zero; compatible generics/overloads pass
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
| `PhpDelegationTyhpdefGenerator.cs` | 400 lines | C# orchestrator: one PHP process + JSON → IR |
| `NativeTyhpdefGenerator.cs` | 500 lines | May need to split AST walking into partial classes |
| `PhpAstTypeExtractor.cs` | 500 lines | Complex type extraction logic |
| `PhpRuntimeManager.cs` | 400 lines | Download / cache / isolated ini |
| `PhpManualDocExtractor.cs` | 500 lines | php.net HTML → markdown; may split xpath helpers |
| `PhpDocParser.cs` | 400 lines | Tag parsing is mechanical but verbose |
| `PhpDocTypeParser.cs` | 300 lines | Type expression parsing |
| `TyhpdefOutputWriter.cs` | 400 lines | Formatting logic |
| `TyhpCodeTyhpdefGenerator.cs` | 400 lines | Symbol walking + mapping |

### Error Handling Conventions

All tyhpdef generation code should follow Story 01's diagnostic system:

- Use `DiagnosticBag.AddError()` / `AddWarning()` / `AddInfo()` for all compiler messages
- Never throw exceptions for recoverable errors (missing files, parse errors in source)
- Use `MessageCode` values in the **7500–7599** `generate_tyhpdef` CLI range. Never use the 8000s (parse/bind) for generation errors.
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
- `// PLACEHOLDER_STORY_10: Read build.generateTyhpdef from tyhp.json` — **stale for libraries.** Libraries ignore the flag. Applications still read `Build.GenerateTyhpdef`.
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
├── PhpRuntimeManager.cs            (~400 lines)
├── PhpRuntimeManifest.json
├── PhpRuntimeDetector.cs           (~80 lines; `--php` inspect only)
├── PhpManualDocExtractor.cs        (~500 lines; php.net HTML → markdown)
├── NativeTyhpdefGenerator.cs       (~500 lines)
├── PhpAstTypeExtractor.cs          (~500 lines)
├── TyhpdefOutputWriter.cs          (~400 lines)
├── TyhpdefDeclaration.cs           (~200 lines)
├── StubHarvestTyhpdefEnricher.cs   (~400 lines)
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
Tyhp/CLI/BuildAction.cs (Track C wiring)
docs/content/cli_tyhpdefGeneration.md (and the Phase 9 user-docs set)
Tyhp/Config/ActionConfigProvider.cs
tyhp.csproj (HtmlAgilityPack or equivalent HTML parser for php.net manuals)
```

---

*Last updated: 2026-08-20 — Track A php.net manual doc comments (port `genTyhpdef.php`); Track B/C copy source comments; stubs hole-fill only*

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the tyhpdef generator works end-to-end across all three tracks (PHP delegation, C# native, and Tyhp code generation). Steps can be skipped, reordered, or adapted as needed. Track A **does not** require a user-installed PHP; first managed download needs network.

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

### Step 2: Verify Track A — PHP Extension Tyhpdef Generation (Managed PHP)

Run **without** `--php` and without relying on Homebrew/`PATH` php:

```bash
dotnet run -- generate_tyhpdef --ext-name=json
```

Expected:
- Tyhp downloads or reuses a managed CLI (first run needs network); user `php` on PATH is not required
- A file `tyhpdef/ExtJson.tyhpdef` is created (or under `--output` if given). **Not** `runtime/php-extensions/`.
- The file starts with `<?tyhpdef`
- The file contains declarations for `json_encode`, `json_decode`, `JSON_ERROR_NONE`, `JsonException`, etc.
- `json_encode` has a real `/** */` from the php.net manual (overview + `@link` to `function.json-encode`), not an empty comment (unless `--no-docs`)
- Generation timing and declaration counts are reported to the console
- Layer 2 stub overlays appear under `tyhpdef/overlays/stubs/` when the stub snapshot is present

Try a second extension:

```bash
dotnet run -- generate_tyhpdef --ext-name=date
```

Expected: A tyhpdef file with `DateTime`, `DateTimeImmutable`, `DateInterval`, `date()`, `time()`, etc. Class and method comments come from the manual (`class.datetime`, `datetime.format`, …), not only free functions.

### Step 3: Verify Track A Error Handling

Try a non-existent extension:

```bash
dotnet run -- generate_tyhpdef --ext-name=nonexistent_ext_xyz
```

Expected: A clear error that the extension is not loaded in the **managed** runtime (`TYHP7511` or `TYHP7500` as implemented).

User-binary escape hatch with a bad path:

```bash
dotnet run -- generate_tyhpdef --ext-name=json --php=/nonexistent/path/php
```

Expected: Error `TYHP7501` (`TyhpdefPhpNotFound`).

`--php` cannot be combined with gated multi-target:

```bash
dotnet run -- generate_tyhpdef --ext-name=json --php=/usr/bin/php --php-targets=8.2,8.3
```

Expected: Error `TYHP7510`.

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

```bash
dotnet run -- generate_tyhpdef --source=test_php_source.tyhp --output=./test_output/ --no-php
```

Expected: Error `TYHP7507` (`TyhpdefSourceNotPhp`). Track B does not parse `.tyhp`.

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
    /**
     * Adds two integers.
     */
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
- A `build/package.tyhp.json` manifest file is created (library: always, even if `build.generateTyhpdef` were false)
- A `build/package.tyhpdef` file is created with the public API of `Calculator`
- `add` keeps the source summary (“Adds two integers”) unless docs were disabled
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

Expected: The verify mode regenerates Layer 1, applies overlays (stubs + hand; `omit` is not a missing-API error), and checks that the **final** API is compatible with the golden PHP signatures. Exit non-zero only for real holes or contradictory types (`TYHP7506`). A text diff against hand-enriched bundled files is **not** the pass criterion.

### Step 12: Verify `--no-docs` Flag

```bash
dotnet run -- generate_tyhpdef --ext-name=json --output=./test_output/ --overwrite --no-docs
```

Expected: The generated tyhpdef file has no doc comments (no `/**` blocks). The php-manuals cache is not required for this run.

### Step 12b: Verify Track A manual docs (default on)

```bash
dotnet run -- generate_tyhpdef --ext-name=json --output=./test_output/ --overwrite
```

Expected: `json_encode` has a non-empty overview from the php.net manual and `@link https://www.php.net/manual/en/function.json-encode.php` (or the `--locale` equivalent).

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

    operator +(self $left, self $right): self {
        return new Money($left->amount + $right->amount, $left->currency);
    }
}

extension MoneyExtensions {
    function label(extends Money $this): string {
        return (string)$this->amount . ' ' . $this->currency;
    }
}
```

Build as a library project and inspect the generated tyhpdef:

Expected:
- `package.tyhpdef` has `class Money` with `public static function __add(…)` and `#[\Tyhp\Optimize\Inline] extension operator +(self $a, self $b): self => self::__add($a, $b);`
- `package.tyhpdef` has `class MoneyExtensions as MoneyExtensions__tyhpExtensionBacker` (compiled static methods) plus `extension MoneyExtensions { function label(extends Money $this): string => MoneyExtensions__tyhpExtensionBacker::label($this); }`
- No `use extension MoneyExtensions` in the generated tyhpdef (consumers write that in `.tyhp`)
- `package.tyhp.json` `include` is `["./package.tyhpdef"]`

### Step 14: Duplicate FQN (not this story)

Skip. `TYHP8025` is Story 06. Do not treat it as a Story 20 human-test gate.

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
> **PHP runtimes:** this phase uses **Tyhp-managed** CLIs only ([Track A PHP runtimes](#track-a-php-runtimes)). No Homebrew matrix, no `--php-map`, no Docker requirement.

### Phase Overview

Extend `generate_tyhpdef` so one invocation can reflect **multiple** PHP minors via managed runtimes, diff their API
surfaces, and emit a **single** gated `.tyhpdef` tree that Story 21 can ship inside `tyhp/php` /
`tyhp/php-ext-*`.

Pipeline for this phase (builds on the three-layer model above):

```text
--php-targets=8.2,8.3,8.4,8.5
    → PhpRuntimeManager.Ensure each minor (download / patch-update / isolated ini)
    → per-target Track A JSON snapshots (tyhpdef_gen/snapshots/{ver}/)
    → diff/merge → gated IR
    → TyhpdefOutputWriter → Layer 1 baseline tree
    → Layer 2 stub harvest → _tyhpdef/overlays/stubs/*.tyhpdef
    → (compile time) apply "overlay" in array order (stubs first, hand last; last wins)
```

### Deliverables

1. `--php-targets=8.2,8.3,8.4,8.5` (comma-separated minors). **Managed PHP only.** Combining with `--php` is `TYHP7510`.
2. `PhpRuntimeManager.Ensure` for every listed minor before reflect; fail the whole run if any minor cannot be provisioned (no silent partial merge)
3. Per-target reflection snapshots (Track A JSON) under `tyhpdef_gen/snapshots/`
4. Diff/merge pass:
   - Identical across all targets → ungated declaration
   - Added at version V → `declare(php=">=V") { … }` and/or `#[\Tyhp\Php(">=V")]` on members
   - Signature changed at V → version-disjoint declarations (overlapping constraints must not be emitted)
   - Removed after V → upper-bound constraint (`<V+`)
5. Documented regen workflow: overwrite Layer 1 baseline and Layer 2 `overlays/stubs/`; never wipe hand-written `_tyhpdef/overlays/*.tyhpdef` or `runtime/php-extensions/overlays/` (backup snapshot)
6. Attribution headers + `SOURCES.md` when Layer 2 stub enrichments are applied

### Implementation notes

- Prefer emitting **member attributes** for additions inside shared classes; use **`declare(php=…)` blocks** for
  whole functions/files and for **struct/extension** (Story 20.5 forbids `#[\Tyhp\Php]` on those).
- Output must be valid under Story 20.5 semantics and lint clean for each `output.phpVersion` in the matrix.
- Hand-authored Tyhp overlays are separate tyhpdefs under each package's `_tyhpdef/overlays/` (Story 21). `runtime/php-extensions/overlays/` is a recovery snapshot of pre-split hand edits, not the load path.
- Live compiler load path remains `php8.2.9/` until Story 21 packages + overlay load are wired; then baseline + `package.tyhp.json` `overlay`.
- Do not extend `// @generated-original:` as the overlay mechanism; use load-time overlay files and optional `// @overlay-against:`.
- `--refresh-snapshots` re-runs managed reflect even when snapshot files exist.
- Patch auto-update runs per minor unless `--no-php-runtime-update`.

### Acceptance Criteria

- [ ] Multi-target run requires Story 20.5 to be implemented (constraint + attribute semantics available)
- [ ] `--php-targets` never invokes `PATH` / Homebrew PHP; only `PhpRuntimeManager`
- [ ] `--php` + `--php-targets` is `TYHP7510`
- [ ] Generating Core/standard stubs for 8.2–8.5 produces one gated tree
- [ ] `tyhp lint` with `output.phpVersion` set to each minor succeeds and exposes the expected APIs
- [ ] Missing/unprovisionable managed target → actionable error (no silent partial merge)
- [ ] Regen into staging does not modify hand-written `_tyhpdef/overlays/*.tyhpdef` or `runtime/php-extensions/overlays/`
- [ ] Backup snapshot exists and is documented (`runtime/php-extensions/overlays/README.md`)
- [ ] Stub-derived enrichments credit sources (file header and/or `SOURCES.md`)

---

## Phase 9: User documentation

> Completeness pass for every user-facing page this story ships. Draft `cli_tyhpdefGeneration.md` in Phase 1 if useful; this phase is the gate. Multi-target / gated-output docs wait until Phase 8 (Story 20.5) has landed.

### Phase Overview

Update the published docs so they match the implemented CLI, Track C emit, tyhpdef `extension { }`, base `partial`, `global use`, local `use` overlay, `hide` / operator `insteadof`, and (after Phase 8) multi-target generation. Do not leave Story 03 wording that says tyhpdef cannot declare standalone `extension { }`.

### Pages to update (create a sibling page only if an existing page cannot hold the topic)

| Page | What this story adds |
|------|----------------------|
| `docs/content/cli_tyhpdefGeneration.md` | `--package-path` (no `--composer-package`); default `tyhpdef/`; `--split`; `--include-dev`; `--verify`; `--source` PHP-only (`TYHP7507`); Track A vs B vs C; Layer 1+2; **managed PHP default** (windows.php.net / StaticPHP / Homebrew bottle fallback); `--php` single-target escape hatch; `--php-targets` gated (managed only); `--php-runtime-dir`; patch auto-update; **`--locale` / php.net HTML manuals for Track A comments**; `--no-docs`; Track B/C copy source comments; stubs fill remaining holes |
| Repo [`THIRD_PARTY.md`](../THIRD_PARTY.md) / [`README.md`](../README.md) | Keep acknowledgments in sync when providers or stub URLs change; cache `ATTRIBUTION.txt` + generated `SOURCES.md` |
| `docs/content/cli_build.md` | Track C: library always writes `package.tyhpdef` + `package.tyhp.json`; application + `build.generateTyhpdef` |
| `docs/content/tyhpdef_extensions.md` | Reverse the Story 03 ban: tyhpdef `extension Name { }` with short `=>` only, return types required, implicitly inline; empty block error; no brace bodies; `$this`/`self` = target; no `partial extension`; no `#[\Tyhp\Php]` (Story 20.5) |
| `docs/content/tyhp_2100_extensions.md` | Backer `class PhpName as Name__tyhpExtensionBacker`; Track C does not emit `use extension` / `global use extension`; opt-in `use extension`; postfix `hide`; operator `insteadof`; `as` on operators is an error |
| `docs/content/tyhp_0350_useStatements.md` | `global use` / `use function` / `use const` / `use extension` (C# `global using`, not PHP `global $var`); local `use` may re-import a globally included symbol to apply alias / adaptations for that file or namespace; redundant non-mutating local `use` warns |
| `docs/content/tyhp_1600_operatorOverloads.md` | Operator `hide` / `insteadof`; operators cannot be renamed with `as` |
| `docs/content/tyhpdef_functions.md` / `docs/content/tyhpdef_classes.md` | Base / `include` `partial` (additive, duplicate = error, missing target = error, `omit` illegal). Point overlay last-wins + `omit` at Story 21 |
| `docs/content/tyhpdef_about.md` / `docs/content/quickref_tyhpdef.md` / `docs/content/quickref.md` | Short pointers to the new forms |
| `docs/content/faq_cli.md` / `docs/content/faq_tyhpSyntax.md` / `docs/content/faq_tyhpdefSyntax.md` | Generator flags; `global use` vs local `use`; tyhpdef `extension` |
| `docs/content/diagnostics_reference.md` | New generation codes 7500–7512 and `CheckerRedundantGlobalImport` once registered |
| `docs/content/toc.json` | Add any new sibling pages (see `docs/readme.md`) |

Any new page must have YAML front matter and a `toc.json` entry or the docs build fails.

### Acceptance Criteria

- [ ] Every language and CLI behavior this story implements is described on the pages above (or a new sibling page linked from them)
- [ ] Docs no longer say tyhpdef cannot declare standalone `extension { }`, and no longer document `--composer-package`
- [ ] `global use` and the redundant local-`use` warning are documented with examples
- [ ] After Phase 8: `cli_tyhpdefGeneration.md` covers `--php-targets` / managed PHP / gated output; until then those sections stay marked planned

### Dependencies

- **Requires:** Phases 1–7 for the language/CLI surface; Phase 8 before documenting multi-target generation
- **Provides:** User-facing docs that match shipped behavior

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add generator goldens to the conformance suite (Story 07 layout): checked-in PHP (or Reflection JSON snapshot) → expected `.tyhpdef` / overlay stubs, plus expected-diagnostics for error paths (`--ext-name` missing PHP, bad `--package-path`). Include at least one Track A golden whose `json_encode` (or equivalent) comment is assembled from a **vendored/cached** manual excerpt so CI need not download php.net. Also `.tyhp` / `.tyhpdef` fixtures for `global use`, local mutating `use` (alias / `hide` / `insteadof`), and `CheckerRedundantGlobalImport` on a non-mutating local `use`. These generator goldens are **not** `.tyhp → .php` emit fixtures.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance:** N/A for this story (generator does not change runtime PHP emit). Skip unless Track C is used to rebuild a runtime package in a later story.
- [ ] **Diagnostics registered centrally:** New codes 7500–7512 and `CheckerRedundantGlobalImport` only in `Tyhp/Domain/Exceptions/MessageCode.cs` (see `CONVENTIONS.md`).

---

## Remaining open questions

1. **`exclude` loader:** Story 20 emits `exclude: []`. Honoring exclude at bind time is Story 06 if not already there.
