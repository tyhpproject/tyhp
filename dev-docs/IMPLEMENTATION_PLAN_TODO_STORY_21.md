# Implementation Plan: Story 21 — PHP Extension Composer Packages (`tyhp/php` + `tyhp/php-ext-*`)

> **Roadmap position:** Story 21 — **Tier 2 — DX & Ecosystem**
> **Direct dependencies (new numbering):** 06, **20.5** (PHP version gating — hard), 20, and 28† (generic-parameter defaults — forward dependency; see ROADMAP note)
> **Renumbered from:** legacy Story 23
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 21 of the Tyhp compiler TODO
> **Branch:** TBD
> **Generated:** 2026-03-24
> **Last design lock:** 2026-07-29 — single `tyhp/php` + `tyhp/php-ext-*` packages; version diffs via Story 20.5 gating (no per-minor package forks); Phase 2 harvests Psalm/PHPStan/Phan/PhpStorm stubs for tyhpdef enrichment
> **Prerequisites:** Story 06 (Built-in Type System — package discovery via `package.tyhp.json`, `TyhpdefSymbolRegistrar`), **Story 20.5** (PHP Version Gating — `declare(php=…)` + `#[\Tyhp\Php]`), Story 20 (Tyhpdef Generator — for generating `.tyhpdef` files from PHP extensions; Phase 8 multi-target generator when available), Story 28 (Generic Type Parameter Defaults — `T = DefaultType` syntax)

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Phase 1: Package Infrastructure](#phase-1-package-infrastructure)
- [Phase 2: Tyhpdef Refinement](#phase-2-tyhpdef-refinement)
- [Phase 3: Core & SPL Generic Declarations](#phase-3-core--spl-generic-declarations)
- [Phase 4: Scalar & Callable Extension Classes](#phase-4-scalar--callable-extension-classes)
- [Phase 5: Validation and Testing](#phase-5-validation-and-testing)
- [Phase 6: Version Gating Across PHP Minors](#phase-6-version-gating-across-php-minors)
- [Phase 7: Database Extensions](#phase-7-database-extensions)
- [Phase 8: String & Text Extensions](#phase-8-string--text-extensions)
- [Phase 9: Web & Network Extensions](#phase-9-web--network-extensions)
- [Phase 10: Data Format Extensions](#phase-10-data-format-extensions)
- [Phase 11: File & System Extensions](#phase-11-file--system-extensions)
- [Phase 12: Image & Media Extensions](#phase-12-image--media-extensions)
- [Phase 13: Caching & Session Extensions](#phase-13-caching--session-extensions)
- [Phase 14: Math & Crypto Extensions](#phase-14-math--crypto-extensions)
- [Phase 15: Remaining Extensions](#phase-15-remaining-extensions)
- [Cross-Story References](#cross-story-references)

---

## Architecture Overview

### Purpose

PHP does not have generics. When the Tyhp compiler type-checks code that uses PHP built-in classes like `Iterator`, `Generator`, `SplStack`, or `WeakMap`, it needs generic type information that PHP itself does not provide. Additionally, PHP's scalar types (`string`, `int`, `float`, `bool`, `array`) and `Closure` have no methods — developers must use standalone functions like `\strlen()`, `\array_map()`, and `\abs()`.

The single `tyhp/php` Composer package (plus optional `tyhp/php-ext-*` packages) solves both problems by distributing:

1. **`.tyhpdef` files** — type signatures for all PHP built-in functions, classes, interfaces, constants, and enums from every bundled extension (Core, SPL, Standard, Date, JSON, PCRE, mbstring, DOM, etc.), with generic type parameters added directly to types that are logically generic (e.g., `Iterator<TKey, TValue>`, `SplStack<T>`)
2. **`.tyhp` files** — standalone extension classes that add methods to scalar types, arrays, and `Closure`, mapping to underlying PHP functions with clean, consistent naming
3. **`package.tyhp.json`** — the package manifest with an `include` array that tells the Tyhp compiler which files to load

### Package Naming

Two package categories exist:

**Base package** — one package covering all supported PHP minors (8.2–8.5), with version-specific APIs gated via Story 20.5:

| Package Name | Path | Composer `require.php` | Coverage |
|---|---|---|---|
| `tyhp/php` | `runtime/packages/php/` | `>=8.2` | PHP 8.2.x–8.5.x (and later minors as they land), commonly-bundled extensions |

PHP 8.1 is not supported. The minimum supported PHP version is 8.2. Version differences are **not** expressed as separate Composer packages; they use `declare(php=…)` / `#[\Tyhp\Php]` (Story 20.5) matched against the project's `output.phpVersion`.

**Individual extension packages** — one per optional extension for all PHP versions (gates inside for version-specific APIs):

| Package Name | Path | Extension |
|---|---|---|
| `tyhp/php-ext-curl` | `runtime/packages/php-ext-curl/` | curl |
| `tyhp/php-ext-pdo` | `runtime/packages/php-ext-pdo/` | PDO |
| `tyhp/php-ext-pdo_mysql` | `runtime/packages/php-ext-pdo_mysql/` | pdo_mysql |
| `tyhp/php-ext-gd` | `runtime/packages/php-ext-gd/` | gd |
| ... | ... | ... |

Do **not** create per-minor packages (`tyhp/php-8.2`, `tyhp/php-8.x-ext-*`, etc.). Patch-level PHP releases do not change the extension API surface; minor-level diffs are gated in the single tree.

### How Generic Declarations Work

Generic type parameters are added **directly to the `.tyhpdef` files**. The original auto-generated non-generic declaration is preserved as a comment block (prefixed with `// @generated-original:`) above the generic version. This approach avoids the need for separate overlay files or "last-wins" binder logic.

For example, in `Ext.Core.tyhpdef`:

```tyhpdef
// @generated-original: interface Iterator extends \Traversable {
// @generated-original:     public function current(): mixed;
// @generated-original:     public function key(): mixed;
// @generated-original:     public function next(): void;
// @generated-original:     public function rewind(): void;
// @generated-original:     public function valid(): bool;
// @generated-original: }

interface Iterator<TKey = mixed, TValue = mixed> extends \Traversable<TKey, TValue> {
    public function current(): TValue;
    public function key(): TKey;
    public function next(): void;
    public function rewind(): void;
    public function valid(): bool;
}
```

Generic type parameter defaults (from Story 28) allow these types to be used without explicit type arguments. For example, `Iterator` without arguments defaults to `Iterator<mixed, mixed>`, matching PHP's untyped behavior. Where possible, defaults are narrower than `mixed` (e.g., `SplPriorityQueue<TValue = mixed, TPriority = int>` since priorities are typically integers, `ArrayObject<TKey = string|int, TValue = mixed>` since PHP array keys can only be `string|int`).

### How Scalar Extension Classes Work

Standalone extension classes in `.tyhp` files under `_tyhpdef/support/` add methods to scalar types (`string`, `int`, `float`, `bool`), `array`, and `Closure`. These use the standard Tyhp extension syntax with `extends Type $this` on the first parameter.

All scalar extension classes use the tyhpdef extension auto-inclusion mechanism — extensions declared on a type in its `.tyhpdef` file (via `extension function`, `extension operator`, or `use extension` declarations) are automatically available in scope whenever the declared type is used in Tyhp code. No explicit `import extension` statement is required. This mechanism is defined in Story 06 Phase 4 — the binder stores extension declarations as part of the type's symbol metadata during tyhpdef loading, and includes them in resolution scope automatically when the type is referenced. Individual extension packages (Phase 7) may add additional auto-included extension methods to scalar types (e.g., an intl extension package could add `transliterate()` to strings).

Extension methods are annotated with:
- `#[\Tyhp\Optimize\Inline]` — for simple one-liner wrappers, so `$str->length()` compiles to `\mb_strlen($str)` with zero overhead
- `#[\Tyhp\Optimize\Pure]` — for side-effect-free methods, enabling optimizer optimizations (Story 24)

String methods use `mb_*` functions as defaults (e.g., `$str->length()` maps to `\mb_strlen()`, not `\strlen()`). Non-multibyte alternatives use a `byte` prefix (e.g., `$str->byteLength()` maps to `\strlen()`). No encoding parameter is exposed; PHP's `mbstring.internal_encoding` or `default_charset` applies.

Array extension methods follow a naming convention:
- **Immutable** methods (return a new array) use **past participle** names: `sorted()`, `reversed()`, `filtered()`
- **Mutable** methods (modify in-place via `extends array &$this`) use **present tense** names: `sort()`, `reverse()`, `push()`, `pop()`

### Current State

| Component | Path | Status |
|---|---|---|
| PHP 8.2.9 extension tyhpdefs | `runtime/php-extensions/php8.2.9/` | 16 `.tyhpdef` files, generated by `genTyhpdef.php` |
| Tyhpdef generator tool | Story 20 (`GenerateTyhpdefAction`) | Complete (Phase 8 multi-target gated gen when available) |
| PHP version gating | Story 20.5 (`declare(php=…)`, `#[\Tyhp\Php]`) | **Hard prerequisite** for this story |
| Package discovery (`package.tyhp.json`) | Story 06, Phase 4 (`LoadPackageTyhpdefs()`) | Complete |
| Generic type parameter defaults | Story 28 | Complete |
| Extension method syntax | Story 03 | Complete |
| Scalar extension classes | N/A | Do not exist yet |
| Single `tyhp/php` package | `runtime/packages/php/` | Does not exist yet (this story) |

### Version gating (Story 20.5)

Story 21 **depends on Story 20.5** for compile-time PHP version gating. Full semantics live in `IMPLEMENTATION_PLAN_TODO_STORY_20.5.md`; summary for package authors:

- **`declare(php="…")`** — file-level or block-level; full Composer constraint syntax; `"8.2"` means the whole minor `8.2.*`; `php` must appear **alone** in that `declare`; inactive file-level declare → skip the file silently.
- **`#[\Tyhp\Php(string $version)]`** — on class / interface / enum / trait and members (and functions). **Not** on `struct` / `extension` (wrap those in `declare(php=…) { … }` blocks).
- Evaluated against **`output.phpVersion`**. If unset → default **`8.2`** + warn.
- Version-**disjoint** same-name declarations are OK; **overlapping** constraints for the same symbol → error.

```tyhpdef
declare(php=">=8.3") {
    function json_validate(string $json, int $depth = 512, int $flags = 0): bool;
}

#[\Tyhp\Php(">=8.4")]
function array_find(array $array, callable $callback): mixed;

declare(php=">=8.5") {
    extension UriStringExtensions extends string {
        // ...
    }
}
```

### Design Principles

1. **One base package for all supported PHP minors.** `tyhp/php` at `runtime/packages/php/` is self-contained with commonly-bundled extension tyhpdefs, generic declarations, and scalar extension classes. Composer `require.php` is `>=8.2` (not mutually exclusive per-minor constraints).

2. **Individual packages for optional extensions.** Extensions not bundled by default (e.g., `curl`, `pdo`, `gd`) get their own Composer packages (`tyhp/php-ext-{name}` at `runtime/packages/php-ext-{name}/`). One package per extension covers all PHP versions; version-specific APIs use Story 20.5 gates. Packages may depend on each other (e.g., `tyhp/php-ext-pdo_mysql` depends on `tyhp/php-ext-pdo`).

3. **Version diffs via gating, not package forks.** Do not `cp -r` into `php-8.3` / `php-8.4` / `php-8.5` packages. Author and maintain one gated tree; use Story 20 Phase 8 multi-target generator when available; until then hand-apply `declare(php=…)` / `#[\Tyhp\Php]`.

4. **Generics and stub enrichments in tyhpdef, with overlay preservation.** Generic type parameters and other stub-derived refinements are expressed as **native tyhpdef** (not analyzer-only PHPDoc). Prefer keeping the Reflection-generated non-generic / under-typed form as `// @generated-original:` above the enriched declaration when editing in place. **In addition**, Story 20 Layer 3 maintains `runtime/php-extensions/overlays/` as a full-file preservation snapshot of hand-enriched extension tyhpdefs so mistaken regen cannot silently lose work. Programmatic overlay apply/merge is future work; until then the live load path remains `runtime/php-extensions/php8.2.9/` (and later `tyhp/php`), while `overlays/` is the recovery baseline. Reflection-generated `.tyhpdef` files only know PHP's runtime type hints (often `mixed` / untyped). Use community stub corpora (Psalm, PHPStan, Phan, PhpStorm — see Phase 2.3 and `runtime/README.md`) as the primary source of PHPDoc / analyzer annotations when refining signatures: parameter/return types, `@template` generics, array element/key types, stub array shapes (as Tyhp `struct` declarations), callable signatures, and type-guard / assertion metadata. Prefer translating those annotations into native Tyhp tyhpdef syntax over leaving `mixed`.

5. **Extension classes for scalar methods, not tyhpdef inline extensions.** Scalar type methods use standalone `extension` classes in `.tyhp` files under `_tyhpdef/support/`. This keeps them separate from the auto-generated tyhpdef declarations. Gate version-specific method bodies with `declare(php=…) { … }` (attributes are not allowed on `extension` declarations).

6. **Standalone packages, no monorepo root.** Each package has its own `composer.json`. There is no root `runtime/composer.json`. Each package manages its own dependencies and tests independently.

7. **Tyhpdef extension auto-inclusion.** All scalar and closure extension classes are automatically available via the tyhpdef extension auto-inclusion mechanism (Story 06 Phase 4). Extensions declared on a type in its `.tyhpdef` file are included in resolution scope whenever the type is referenced — no explicit `import extension` is required. Users get methods on `string`, `int`, `float`, `bool`, `array`, and `Closure` without any imports.

8. **PHP 8.2 is the floor; verify across 8.2–8.5.** Author against the single `tyhp/php` tree. Acceptance requires a lint/build matrix with `output.phpVersion` set to each of 8.2, 8.3, 8.4, and 8.5.

### Package Directory Structure

```
runtime/packages/php/
├── composer.json
├── package.tyhp.json
├── _tyhpdef/
│   ├── Ext.Calendar.tyhpdef
│   ├── Ext.Core.tyhpdef
│   ├── Ext.Ctype.tyhpdef
│   ├── Ext.Date.tyhpdef
│   ├── Ext.Dom.tyhpdef
│   ├── Ext.Exif.tyhpdef
│   ├── Ext.Fileinfo.tyhpdef
│   ├── Ext.Filter.tyhpdef
│   ├── Ext.Hash.tyhpdef
│   ├── Ext.Iconv.tyhpdef
│   ├── Ext.Json.tyhpdef
│   ├── Ext.Libxml.tyhpdef
│   ├── Ext.Mbstring.tyhpdef
│   ├── Ext.Openssl.tyhpdef
│   ├── Ext.Pcntl.tyhpdef
│   ├── Ext.Pcre.tyhpdef
│   ├── Ext.Phar.tyhpdef
│   ├── Ext.Posix.tyhpdef
│   ├── Ext.Random.tyhpdef
│   ├── Ext.Reflection.tyhpdef
│   ├── Ext.Session.tyhpdef
│   ├── Ext.Simplexml.tyhpdef
│   ├── Ext.Sodium.tyhpdef
│   ├── Ext.SPL.tyhpdef
│   ├── Ext.Standard.tyhpdef
│   ├── Ext.Tokenizer.tyhpdef
│   ├── Ext.Xml.tyhpdef
│   ├── Ext.Xmlreader.tyhpdef
│   ├── Ext.Xmlwriter.tyhpdef
│   ├── Ext.Zlib.tyhpdef
│   └── support/
│       ├── string.tyhp
│       ├── array.tyhp
│       ├── int.tyhp
│       ├── float.tyhp
│       ├── bool.tyhp
│       └── closure.tyhp
└── tests/
    ├── tyhp.json
    ├── test_generics.tyhp
    └── test_extensions.tyhp
```

Individual extension packages follow the same pattern (one package for all PHP versions):

```
runtime/packages/php-ext-curl/
├── composer.json
├── package.tyhp.json
├── _tyhpdef/
│   └── Ext.Curl.tyhpdef
└── tests/
    └── test_curl.tyhp
```

---

## Phase 1: Package Infrastructure

### Phase Overview

Create the directory structure, `composer.json`, and `package.tyhp.json` for the single `tyhp/php` package. Move the existing 16 `.tyhpdef` files from `runtime/php-extensions/php8.2.9/` into the new package structure and generate the remaining 14 tyhpdef files for newly-included extensions using Story 20's tyhpdef generator.

### Deliverables

1. `runtime/packages/php/` directory created with full structure
2. `runtime/packages/php/composer.json` created
3. `runtime/packages/php/package.tyhp.json` created
4. `runtime/packages/php/_tyhpdef/` directory with all 30 `.tyhpdef` files (dot-notation naming)
5. `runtime/packages/php/_tyhpdef/support/` directory created (empty until Phase 4)
6. `runtime/packages/php/tests/` directory created (empty until Phase 5)
7. `runtime/php-extensions/php8.2.9/` directory removed after files are moved

### Implementation Details

**1.1 — Create Package Directory**

```bash
mkdir -p runtime/packages/php/_tyhpdef/support
mkdir -p runtime/packages/php/tests
```

**1.2 — Create `composer.json`**

File: `runtime/packages/php/composer.json`

```json
{
    "name": "tyhp/php",
    "description": "Tyhp type definitions for PHP built-in extensions (8.2+) — includes function/class/constant signatures, generic type parameters, and scalar extension methods for string, array, int, float, bool, and Closure. Version-specific APIs are gated via declare(php=…) / #[\\Tyhp\\Php] (Story 20.5).",
    "type": "library",
    "license": "MIT",
    "require": {
        "php": ">=8.2"
    },
    "extra": {
        "tyhp": {
            "php-version": ">=8.2",
            "supported-minors": ["8.2", "8.3", "8.4", "8.5"],
            "extensions": [
                "Core", "calendar", "ctype", "date", "dom", "exif",
                "fileinfo", "filter", "hash", "iconv", "json", "libxml",
                "mbstring", "openssl", "pcntl", "pcre", "Phar", "posix",
                "random", "Reflection", "session", "SimpleXML", "sodium",
                "SPL", "standard", "tokenizer", "xml", "xmlreader",
                "xmlwriter", "zlib"
            ]
        }
    }
}
```

This is a standalone package. There is no root `runtime/composer.json`. The `php` constraint is `>=8.2` (not mutually exclusive per-minor ranges). `extra.tyhp.php-version` / `supported-minors` document the supported surface; actual API filtering is done by Story 20.5 against the project's `output.phpVersion`.

**1.3 — Create `package.tyhp.json`**

File: `runtime/packages/php/package.tyhp.json`

```json
{
    "include": [
        "./_tyhpdef/*.tyhpdef",
        "./_tyhpdef/support/*.tyhp"
    ]
}
```

The `include` array lists `.tyhpdef` files first, then `.tyhp` support files. The `.tyhp` files contain extension classes that are automatically available via the tyhpdef extension auto-inclusion mechanism.

**1.4 — Move and Rename Existing Tyhpdef Files**

Move all 16 `.tyhpdef` files from `runtime/php-extensions/php8.2.9/` to `runtime/packages/php/_tyhpdef/`, renaming from legacy `Ext{Name}.tyhpdef` to dot-notation `Ext.{Name}.tyhpdef`:

| Source | Destination |
|---|---|
| `ExtCore.tyhpdef` | `Ext.Core.tyhpdef` |
| `ExtDate.tyhpdef` | `Ext.Date.tyhpdef` |
| `ExtFilter.tyhpdef` | `Ext.Filter.tyhpdef` |
| `ExtHash.tyhpdef` | `Ext.Hash.tyhpdef` |
| `ExtJson.tyhpdef` | `Ext.Json.tyhpdef` |
| `ExtLibxml.tyhpdef` | `Ext.Libxml.tyhpdef` |
| `ExtOpenssl.tyhpdef` | `Ext.Openssl.tyhpdef` |
| `ExtPcntl.tyhpdef` | `Ext.Pcntl.tyhpdef` |
| `ExtPcre.tyhpdef` | `Ext.Pcre.tyhpdef` |
| `ExtRandom.tyhpdef` | `Ext.Random.tyhpdef` |
| `ExtReflection.tyhpdef` | `Ext.Reflection.tyhpdef` |
| `ExtSession.tyhpdef` | `Ext.Session.tyhpdef` |
| `ExtSodium.tyhpdef` | `Ext.Sodium.tyhpdef` |
| `ExtSPL.tyhpdef` | `Ext.SPL.tyhpdef` |
| `ExtStandard.tyhpdef` | `Ext.Standard.tyhpdef` |
| `ExtZlib.tyhpdef` | `Ext.Zlib.tyhpdef` |

After verifying all 16 files are intact, remove the old directory:

```bash
rm -rf runtime/php-extensions/php8.2.9/
```

If `runtime/php-extensions/` is now empty, remove it:

```bash
rmdir runtime/php-extensions/ 2>/dev/null || true
```

**1.5 — Generate New Tyhpdef Files**

Generate the remaining 14 tyhpdef files for extensions now included in the base package but not previously covered. Use Story 20's tyhpdef generator against a PHP 8.2 installation (baseline). Higher-minor APIs are added later in Phase 6 via Story 20.5 gates (or Story 20 Phase 8 multi-target generation when available):

```bash
tyhp generate_tyhpdef --ext-name=calendar --output=runtime/packages/php/_tyhpdef/Ext.Calendar.tyhpdef
tyhp generate_tyhpdef --ext-name=ctype --output=runtime/packages/php/_tyhpdef/Ext.Ctype.tyhpdef
tyhp generate_tyhpdef --ext-name=dom --output=runtime/packages/php/_tyhpdef/Ext.Dom.tyhpdef
tyhp generate_tyhpdef --ext-name=exif --output=runtime/packages/php/_tyhpdef/Ext.Exif.tyhpdef
tyhp generate_tyhpdef --ext-name=fileinfo --output=runtime/packages/php/_tyhpdef/Ext.Fileinfo.tyhpdef
tyhp generate_tyhpdef --ext-name=iconv --output=runtime/packages/php/_tyhpdef/Ext.Iconv.tyhpdef
tyhp generate_tyhpdef --ext-name=mbstring --output=runtime/packages/php/_tyhpdef/Ext.Mbstring.tyhpdef
tyhp generate_tyhpdef --ext-name=Phar --output=runtime/packages/php/_tyhpdef/Ext.Phar.tyhpdef
tyhp generate_tyhpdef --ext-name=posix --output=runtime/packages/php/_tyhpdef/Ext.Posix.tyhpdef
tyhp generate_tyhpdef --ext-name=SimpleXML --output=runtime/packages/php/_tyhpdef/Ext.Simplexml.tyhpdef
tyhp generate_tyhpdef --ext-name=tokenizer --output=runtime/packages/php/_tyhpdef/Ext.Tokenizer.tyhpdef
tyhp generate_tyhpdef --ext-name=xml --output=runtime/packages/php/_tyhpdef/Ext.Xml.tyhpdef
tyhp generate_tyhpdef --ext-name=xmlreader --output=runtime/packages/php/_tyhpdef/Ext.Xmlreader.tyhpdef
tyhp generate_tyhpdef --ext-name=xmlwriter --output=runtime/packages/php/_tyhpdef/Ext.Xmlwriter.tyhpdef
```

If the tyhpdef generator requires a specific PHP version to run against and the current environment is not PHP 8.2, use the generator's `--php-version` flag or run it against a PHP 8.2 Docker container.

### Acceptance Criteria

- [ ] `runtime/packages/php/composer.json` exists with `name: tyhp/php` and PHP `>=8.2` constraint
- [ ] `runtime/packages/php/package.tyhp.json` exists with `include` entries for `*.tyhpdef` and `support/*.tyhp`
- [ ] All 30 `.tyhpdef` files exist in `runtime/packages/php/_tyhpdef/` using dot-notation names
- [ ] `runtime/packages/php/_tyhpdef/support/` directory exists
- [ ] `runtime/packages/php/tests/` directory exists
- [ ] `runtime/php-extensions/php8.2.9/` is removed
- [ ] No data loss — all 16 original tyhpdef files are intact after the move
- [ ] No root `runtime/composer.json` exists (each package is standalone)
- [ ] No `runtime/packages/php-8.{2,3,4,5}/` per-minor forks exist

---

## Phase 2: Tyhpdef Refinement

### Phase Overview

Review and fix all 30 `.tyhpdef` files to ensure they parse correctly, contain accurate type signatures, and follow consistent formatting. Harvest PHPDoc / analyzer annotations from community PHP stub corpora to enrich generated signatures (narrower types, generics, callable signatures, stub array shapes → Tyhp `struct`s, type guards). Add `#[\Tyhp\Optimize\Pure]` annotations to pure PHP built-in functions.

Reflection-based generation (Story 20) only sees PHP's runtime type surface. Most built-ins are under-specified there (`mixed`, untyped params, no templates). Static analyzers already encode the missing information in stub files — Story 21 Phase 2 (and later extension phases) must use those stubs as the authoritative enrichment input when hand-refining or tooling-assisted-refining `.tyhpdef` output.

### Deliverables

1. All `.tyhpdef` files parse without errors using `tyhp lint`
2. Known type accuracy issues are fixed
3. `@generated` annotations are preserved
4. Stub-derived enrichments applied where stubs provide stronger types / generics / type-guard metadata than the generated surface (including `struct`s for stub array shapes)
5. `#[\Tyhp\Optimize\Pure]` attributes added to pure functions

### Implementation Details

**2.1 — Lint All Tyhpdef Files**

```bash
tyhp lint runtime/packages/php/_tyhpdef/
```

Fix any parse errors found. Common issues in auto-generated tyhpdefs:

- Missing semicolons after method declarations
- Incorrect return type syntax (e.g., `void` on constructors)
- Unsupported PHP 8.x type syntax that the tyhpdef parser does not handle
- Namespace resolution issues

**2.2 — Verify Extension Coverage**

Confirm that the 30 extension tyhpdef files cover all PHP 8.2 extensions that are commonly bundled:

| Category | Extensions |
|---|---|
| Always present | Core, date, filter, hash, json, libxml, pcre, random, Reflection, SPL, standard |
| Compiled by default | calendar, ctype, dom, exif, fileinfo, iconv, mbstring, openssl, Phar, posix, session, SimpleXML, sodium, tokenizer, xml, xmlreader, xmlwriter, zlib, pcntl |

**2.3 — Analyze Static-Analysis Stubs for Type Enrichment**

Before (and while) hand-editing generated `.tyhpdef` files, analyze the community stub corpora listed in `runtime/README.md` under **PHP Stubs with annotations**. These stubs are the primary source of PHPDoc / analyzer tags that describe how to modify the generated PHP tyhpdefs beyond what Reflection emits.

| Source | URL | Role |
|---|---|---|
| Psalm stubs | https://github.com/vimeo/psalm/tree/6.x/stubs | `@template`, `@psalm-*`, assertions, refined param/return types |
| PHPStan stubs | https://github.com/phpstan/phpstan-src/tree/2.2.x/stubs | `@phpstan-*`, templates, type aliases, assertion tags |
| Phan stubs | https://github.com/phan/phan/tree/v6/internal/stubs | Phan-specific / shared PHPDoc enrichments |
| PhpStorm stubs | https://github.com/jetbrains/phpstorm-stubs | Broad IDE stubs with PHPDoc for built-ins and extensions |

**What to extract from stubs (map into tyhpdef syntax):**

| Stub annotation / pattern | Use when refining tyhpdef |
|---|---|
| `@param`, `@return`, `@var` (and `@phpstan-` / `@psalm-` overrides) | Replace `mixed` / missing types with concrete unions, nullables, object types, and literals |
| `@template` / `@phpstan-template` / `@psalm-template` (+ bounds/defaults) | Drive Phase 3-style generic params on classes/interfaces/functions (e.g. `Iterator<TKey, TValue>`); preserve `// @generated-original:` for the non-generic Reflection form |
| `@param array<…>`, `list<…>`, `non-empty-array` (homogeneous / key-value arrays) | Express as `array<TKey, TValue>` / `list<T>` (or the closest Tyhp array form) on params, returns, and properties |
| Stub array shapes (`array{…}`, `array{foo: int, bar?: string}`, etc.) | **Do not** keep analyzer shape syntax. Declare a Tyhp `struct` in the tyhpdef (or a nearby support tyhpdef) with typed properties for each key; optional shape keys become nullable / optional struct properties; invalid PHP identifier keys use `'key' as $name` aliases. Use that struct type on the enriched signature. See `docs/content/tyhpdef_structs.md` |
| `@param callable(…):…` / Closure signatures | Tighten `callable` / `Closure` parameters and returns |
| `@throws` / `@phpstan-assert` / `@psalm-assert` / `@psalm-assert-if-true` / `@phpstan-assert-if-true` (and related) | Mark or author type-guard metadata for built-ins that narrow types (e.g. `is_*`, existence checks, custom assert helpers). Prefer Tyhp type-guard return syntax (`$param is Type`) on functions that should narrow in the checker, or ensure the checker continues to recognize well-known guards when stubs describe assertion behavior |
| Purity / immutability hints in stubs (where present) | Cross-check against the Pure attribute set in **2.4** — stubs inform, Tyhp `#[\Tyhp\Optimize\Pure]` is the emitted form |
| Extension / class member PHPDoc in PhpStorm stubs | Fill gaps for extension-specific APIs that Reflection typed poorly |

**Array-shape → struct example:**

A stub return type like `array{hostname: string, port: int, scheme?: string}` becomes:

```tyhpdef
struct StreamSocketPeerName {
    string $hostname;
    int $port;
    ?string $scheme;
}

function stream_socket_get_name(/* … */): StreamSocketPeerName|false;
```

Reuse one named struct when the same shape appears on multiple symbols. Nested shapes become nested struct property types.

**Workflow:**

1. Start from Story 20–generated `.tyhpdef` (or the copies under `runtime/php-extensions/` / `runtime/packages/php/_tyhpdef/`).
2. For each symbol (function, method, property, class/interface), look up the corresponding stub entry across the four corpora.
3. Prefer the **most precise type that Tyhp can express** and that agrees with real PHP runtime behavior. When corpora disagree, prefer consensus of Psalm + PHPStan; use PhpStorm for coverage gaps; use Phan as a tie-breaker / additional signal.
4. Translate stub tags into **native tyhpdef declarations** (typed params/returns, generics, `struct`s for array shapes, type-guard returns). Do not leave analyzer-only PHPDoc as the sole carrier of type information inside `.tyhpdef` files — the binder/checker consume tyhpdef syntax, not Psalm/PHPStan tags.
5. Keep the Reflection-generated non-enriched form as `// @generated-original:` (same pattern as Phase 3 generics) whenever a signature is materially changed.
6. Re-run enrichment for optional `tyhp/php-ext-*` packages in Phases 7–15 using the same stub sources (PhpStorm stubs are especially useful for less-common extensions).

**Out of scope for automated dump-and-paste:** Stub dialects that Tyhp cannot express yet stay as notes / follow-ups — do not invent unsupported tyhpdef syntax (including leaving `array{…}` shape literals in tyhpdef). Prefer a slightly wider but valid Tyhp type over an illegal construct. Stub array shapes must become `struct` declarations (or a wider `array` / `array<…>` if a struct is not practical).

**2.4 — Add `#[\Tyhp\Optimize\Pure]` Attributes**

Add `#[\Tyhp\Optimize\Pure]` to PHP built-in functions that are genuinely side-effect-free. A function is pure if it does NOT modify global/static state, does NOT perform I/O, always returns the same output for the same input, and does NOT modify its arguments.

Minimum viable set (must be completed in this story):

**String functions (Ext.Standard.tyhpdef, Ext.Mbstring.tyhpdef):**

```tyhpdef
#[\Tyhp\Optimize\Pure]
function strlen(string $string): int;

#[\Tyhp\Optimize\Pure]
function mb_strlen(string $string, ?string $encoding = null): int;
```

Pure string functions: `\strlen`, `\mb_strlen`, `\str_contains`, `\str_starts_with`, `\str_ends_with`, `\strpos`, `\strrpos`, `\mb_strpos`, `\mb_strrpos`, `\substr`, `\mb_substr`, `\strtolower`, `\strtoupper`, `\mb_strtolower`, `\mb_strtoupper`, `\ucfirst`, `\lcfirst`, `\ucwords`, `\trim`, `\ltrim`, `\rtrim`, `\str_replace`, `\str_ireplace`, `\implode`, `\explode`, `\sprintf`, `\str_pad`, `\str_repeat`, `\strrev`, `\str_word_count`, `\wordwrap`, `\nl2br`, `\chunk_split`, `\str_split`, `\mb_str_split`, `\mb_convert_case`, `\mb_convert_encoding`, `\mb_detect_encoding`, `\mb_ord`, `\preg_quote`

**Array functions (Ext.Standard.tyhpdef):**

Pure array functions: `\count`, `\array_key_exists`, `\in_array`, `\array_search`, `\array_merge`, `\array_slice`, `\array_chunk`, `\array_combine`, `\array_diff`, `\array_diff_key`, `\array_intersect`, `\array_intersect_key`, `\array_keys`, `\array_values`, `\array_unique`, `\array_reverse`, `\array_flip`, `\array_map`, `\array_filter`, `\array_reduce`, `\array_column`, `\array_fill`, `\array_pad`, `\array_key_first`, `\array_key_last`, `\array_sum`, `\array_product`, `\array_count_values`, `\compact`

**Math functions (Ext.Standard.tyhpdef):**

Pure math functions: `\abs`, `\ceil`, `\floor`, `\round`, `\max`, `\min`, `\pow`, `\sqrt`, `\log`, `\exp`, `\fmod`, `\intdiv`, `\pi`, `\sin`, `\cos`, `\tan`, `\base_convert`, `\dechex`, `\decoct`, `\decbin`, `\number_format`

**Type functions (Ext.Core.tyhpdef, Ext.Standard.tyhpdef):**

Pure type functions: `\intval`, `\floatval`, `\strval`, `\boolval`, `\gettype`, `\is_int`, `\is_float`, `\is_string`, `\is_bool`, `\is_array`, `\is_object`, `\is_null`, `\is_numeric`, `\is_callable`

**Hash functions (Ext.Hash.tyhpdef):**

Pure hash functions: `\md5`, `\sha1`, `\hash`, `\crc32`

**JSON functions (Ext.Json.tyhpdef):**

Pure JSON functions: `\json_encode`, `\json_decode`

**PCRE functions (Ext.Pcre.tyhpdef):**

Pure PCRE functions: `\preg_match`, `\preg_match_all`, `\preg_replace`, `\preg_split`, `\preg_quote`

**Encoding functions (Ext.Standard.tyhpdef):**

Pure encoding functions: `\base64_encode`, `\base64_decode`, `\urlencode`, `\urldecode`, `\rawurlencode`, `\rawurldecode`, `\htmlspecialchars`, `\htmlspecialchars_decode`, `\htmlentities`, `\html_entity_decode`, `\strip_tags`

**Impure functions (do NOT annotate):**
- Time-dependent: `\time`, `\microtime`, `\date`, `\mktime`
- Non-deterministic: `\rand`, `\mt_rand`, `\random_int`, `\random_bytes`
- I/O: `\file_get_contents`, `\file_put_contents`, `\fopen`, `\fread`, `\fwrite`
- Output: `\echo`, `\print`, `\var_dump`
- Mutating arguments: `\sort`, `\usort`, `\array_push`, `\array_pop`, `\array_shift`, `\shuffle`

### Acceptance Criteria

- [ ] All 30 `.tyhpdef` files parse without errors
- [ ] Parse errors discovered during linting are fixed
- [ ] `@generated` annotations are preserved in all files
- [ ] Stub corpora (Psalm, PHPStan, Phan, PhpStorm — URLs in `runtime/README.md`) were consulted when refining signatures
- [ ] Material type / generic / type-guard enrichments from stubs are expressed as native tyhpdef syntax (not left as analyzer-only PHPDoc)
- [ ] Stub array shapes (`array{…}`) are represented as Tyhp `struct` declarations (not as shape syntax in tyhpdef)
- [ ] Materially changed signatures retain `// @generated-original:` for the Reflection-generated form
- [ ] `#[\Tyhp\Optimize\Pure]` attributes added to the minimum viable set of pure functions listed above
- [ ] The 30 files cover all commonly-bundled PHP 8.2 extensions

---

## Phase 3: Core & SPL Generic Declarations

### Phase Overview

Edit the `Ext.Core.tyhpdef` and `Ext.SPL.tyhpdef` files to add generic type parameters directly to types that are logically generic. Use Phase 2.3 stub analysis (`@template` / `@phpstan-template` / `@psalm-template` and related tags from Psalm, PHPStan, Phan, and PhpStorm stubs) as the primary evidence for which types are generic and what their parameter names/bounds/defaults should be; reconcile with the explicit lists below when stubs and this plan disagree. The original auto-generated non-generic declarations are preserved as `// @generated-original:` comment blocks above the new generic versions. Generic type parameter defaults are used so types can be used without explicit type arguments.

### Deliverables

1. `Ext.Core.tyhpdef` updated with generic declarations for 8 types
2. `Ext.SPL.tyhpdef` updated with generic declarations for 15 types
3. All modified files parse without errors

### Implementation Details

**3.1 — Core Types (`Ext.Core.tyhpdef`)**

Edit `runtime/packages/php/_tyhpdef/Ext.Core.tyhpdef` to add generic parameters to the following types. For each type, comment out the original auto-generated declaration using the `// @generated-original:` prefix and add the generic version below it.

**Traversable:**

```tyhpdef
// @generated-original: interface Traversable {
// @generated-original: }

interface Traversable<TKey = mixed, TValue = mixed> {
}
```

**Iterator:**

```tyhpdef
// @generated-original: interface Iterator extends \Traversable {
// @generated-original:     public function current(): mixed;
// @generated-original:     public function key(): mixed;
// @generated-original:     public function next(): void;
// @generated-original:     public function rewind(): void;
// @generated-original:     public function valid(): bool;
// @generated-original: }

interface Iterator<TKey = mixed, TValue = mixed> extends \Traversable<TKey, TValue> {
    public function current(): TValue;
    public function key(): TKey;
    public function next(): void;
    public function rewind(): void;
    public function valid(): bool;
}
```

**IteratorAggregate:**

```tyhpdef
// @generated-original: interface IteratorAggregate extends \Traversable {
// @generated-original:     public function getIterator(): \Traversable;
// @generated-original: }

interface IteratorAggregate<TKey = mixed, TValue = mixed> extends \Traversable<TKey, TValue> {
    public function getIterator(): \Traversable<TKey, TValue>;
}
```

**ArrayAccess:**

```tyhpdef
// @generated-original: interface ArrayAccess {
// @generated-original:     public function offsetExists(mixed $offset): bool;
// @generated-original:     public function offsetGet(mixed $offset): mixed;
// @generated-original:     public function offsetSet(mixed $offset, mixed $value): void;
// @generated-original:     public function offsetUnset(mixed $offset): void;
// @generated-original: }

interface ArrayAccess<TKey = mixed, TValue = mixed> {
    public function offsetExists(TKey $offset): bool;
    public function offsetGet(TKey $offset): TValue;
    public function offsetSet(TKey $offset, TValue $value): void;
    public function offsetUnset(TKey $offset): void;
}
```

**Generator:**

```tyhpdef
// @generated-original: final class Generator implements \Iterator {
// @generated-original:     public function rewind(): void;
// @generated-original:     public function valid(): bool;
// @generated-original:     public function current(): mixed;
// @generated-original:     public function key(): mixed;
// @generated-original:     public function next(): void;
// @generated-original:     public function send(mixed $value): mixed;
// @generated-original:     public function throw(\Throwable $exception): mixed;
// @generated-original:     public function getReturn(): mixed;
// @generated-original: }

final class Generator<TKey = mixed, TValue = mixed, TSend = mixed, TReturn = mixed> implements \Iterator<TKey, TValue> {
    public function rewind(): void;
    public function valid(): bool;
    public function current(): TValue;
    public function key(): TKey;
    public function next(): void;
    public function send(TSend $value): TValue;
    public function throw(\Throwable $exception): TValue;
    public function getReturn(): TReturn;
}
```

**WeakReference:**

```tyhpdef
// @generated-original: final class WeakReference {
// @generated-original:     public function __construct(): void;
// @generated-original:     public static function create(object $object): \WeakReference;
// @generated-original:     public function get(): ?object;
// @generated-original: }

final class WeakReference<T extends object = object> {
    public function __construct(): void;
    public static function create(T $object): \WeakReference<T>;
    public function get(): ?T;
}
```

**WeakMap:**

```tyhpdef
// @generated-original: final class WeakMap implements \ArrayAccess, \Countable, \IteratorAggregate {
// @generated-original:     public function offsetGet(object $object): mixed;
// @generated-original:     public function offsetSet(object $object, mixed $value): void;
// @generated-original:     public function offsetExists(object $object): bool;
// @generated-original:     public function offsetUnset(object $object): void;
// @generated-original:     public function count(): int;
// @generated-original:     public function getIterator(): \Iterator;
// @generated-original: }

final class WeakMap<TKey extends object = object, TValue = mixed> implements \ArrayAccess<TKey, TValue>, \Countable, \IteratorAggregate<TKey, TValue> {
    public function offsetGet(TKey $object): TValue;
    public function offsetSet(TKey $object, TValue $value): void;
    public function offsetExists(TKey $object): bool;
    public function offsetUnset(TKey $object): void;
    public function count(): int;
    public function getIterator(): \Iterator<TKey, TValue>;
}
```

**Fiber:**

```tyhpdef
// @generated-original: final class Fiber {
// @generated-original:     public function __construct(callable $callback): void;
// @generated-original:     public function start(mixed ...$args): mixed;
// @generated-original:     public function resume(mixed $value = null): mixed;
// @generated-original:     public function throw(\Throwable $exception): mixed;
// @generated-original:     public function isStarted(): bool;
// @generated-original:     public function isSuspended(): bool;
// @generated-original:     public function isRunning(): bool;
// @generated-original:     public function isTerminated(): bool;
// @generated-original:     public function getReturn(): mixed;
// @generated-original:     public static function getCurrent(): ?\Fiber;
// @generated-original:     public static function suspend(mixed $value = null): mixed;
// @generated-original: }

final class Fiber<TStart = mixed, TReturn = mixed, TSuspend = mixed, TResume = mixed> {
    public function __construct(callable $callback): void;
    public function start(TStart ...$args): TSuspend;
    public function resume(TResume $value = null): TSuspend;
    public function throw(\Throwable $exception): TSuspend;
    public function isStarted(): bool;
    public function isSuspended(): bool;
    public function isRunning(): bool;
    public function isTerminated(): bool;
    public function getReturn(): TReturn;
    public static function getCurrent(): ?self;
    public static function suspend(TSuspend $value = null): TResume;
}
```

Non-generic types (`Countable`, `Stringable`, `JsonSerializable`) remain unchanged in the tyhpdef.

**3.2 — SPL Types (`Ext.SPL.tyhpdef`)**

Edit `runtime/packages/php/_tyhpdef/Ext.SPL.tyhpdef` to add generic parameters. Same `// @generated-original:` comment pattern.

**SPL Iterator Interfaces:**

```tyhpdef
interface SeekableIterator<TKey = mixed, TValue = mixed> extends \Iterator<TKey, TValue> {
    public function seek(int $offset): void;
    public function current(): TValue;
    public function key(): TKey;
    public function next(): void;
    public function rewind(): void;
    public function valid(): bool;
}

interface RecursiveIterator<TKey = mixed, TValue = mixed> extends \Iterator<TKey, TValue> {
    public function hasChildren(): bool;
    public function getChildren(): ?\RecursiveIterator<TKey, TValue>;
    public function current(): TValue;
    public function key(): TKey;
    public function next(): void;
    public function rewind(): void;
    public function valid(): bool;
}

interface OuterIterator<TKey = mixed, TValue = mixed> extends \Iterator<TKey, TValue> {
    public function getInnerIterator(): \Iterator<TKey, TValue>;
    public function current(): TValue;
    public function key(): TKey;
    public function next(): void;
    public function rewind(): void;
    public function valid(): bool;
}
```

**SPL Data Structures:**

```tyhpdef
class SplDoublyLinkedList<T = mixed> implements \Iterator<int, T>, \Countable, \ArrayAccess<int, T>, \Serializable {
    public function add(int $index, T $value): void;
    public function bottom(): T;
    public function top(): T;
    public function count(): int;
    public function current(): T;
    public function getIteratorMode(): int;
    public function isEmpty(): bool;
    public function key(): int;
    public function next(): void;
    public function offsetExists(int $index): bool;
    public function offsetGet(int $index): T;
    public function offsetSet(int $index, T $value): void;
    public function offsetUnset(int $index): void;
    public function pop(): T;
    public function prev(): void;
    public function push(T $value): void;
    public function rewind(): void;
    public function serialize(): string;
    public function setIteratorMode(int $mode): int;
    public function shift(): T;
    public function unserialize(string $data): void;
    public function unshift(T $value): void;
    public function valid(): bool;
}

class SplStack<T = mixed> extends \SplDoublyLinkedList<T> {
}

class SplQueue<T = mixed> extends \SplDoublyLinkedList<T> {
    public function dequeue(): T;
    public function enqueue(T $value): void;
}

abstract class SplHeap<T = mixed> implements \Iterator<int, T>, \Countable {
    public function compare(T $value1, T $value2): int;
    public function count(): int;
    public function current(): T;
    public function extract(): T;
    public function insert(T $value): void;
    public function isCorrupted(): bool;
    public function isEmpty(): bool;
    public function key(): int;
    public function next(): void;
    public function recoverFromCorruption(): bool;
    public function rewind(): void;
    public function top(): T;
    public function valid(): bool;
}

class SplMinHeap<T = mixed> extends \SplHeap<T> {
    public function compare(T $value1, T $value2): int;
}

class SplMaxHeap<T = mixed> extends \SplHeap<T> {
    public function compare(T $value1, T $value2): int;
}

class SplPriorityQueue<TValue = mixed, TPriority = int> implements \Iterator<int, TValue>, \Countable {
    public function compare(TPriority $priority1, TPriority $priority2): int;
    public function count(): int;
    public function current(): TValue;
    public function extract(): TValue;
    public function getExtractFlags(): int;
    public function insert(TValue $value, TPriority $priority): bool;
    public function isCorrupted(): bool;
    public function isEmpty(): bool;
    public function key(): int;
    public function next(): void;
    public function recoverFromCorruption(): bool;
    public function rewind(): void;
    public function setExtractFlags(int $flags): int;
    public function top(): TValue;
    public function valid(): bool;
}

class SplFixedArray<T = mixed> implements \IteratorAggregate<int, T>, \ArrayAccess<int, T>, \Countable, \JsonSerializable {
    public function __construct(int $size = 0): void;
    public function count(): int;
    public function getIterator(): \Iterator<int, T>;
    public function getSize(): int;
    public function jsonSerialize(): array;
    public function offsetExists(int $index): bool;
    public function offsetGet(int $index): T;
    public function offsetSet(int $index, T $value): void;
    public function offsetUnset(int $index): void;
    public function setSize(int $size): void;
    public function toArray(): array<int, T>;
    public static function fromArray(array $array, bool $preserveKeys = true): \SplFixedArray<mixed>;
}

class SplObjectStorage<TObject extends object = object, TData = mixed> implements \Countable, \Iterator<int, TObject>, \Serializable, \ArrayAccess<TObject, TData> {
    public function addAll(\SplObjectStorage<TObject, TData> $storage): int;
    public function attach(TObject $object, TData $info = null): void;
    public function contains(TObject $object): bool;
    public function count(int $mode = \COUNT_NORMAL): int;
    public function current(): TObject;
    public function detach(TObject $object): void;
    public function getHash(TObject $object): string;
    public function getInfo(): TData;
    public function key(): int;
    public function next(): void;
    public function offsetExists(TObject $object): bool;
    public function offsetGet(TObject $object): TData;
    public function offsetSet(TObject $object, TData $info = null): void;
    public function offsetUnset(TObject $object): void;
    public function removeAll(\SplObjectStorage<TObject, TData> $storage): int;
    public function removeAllExcept(\SplObjectStorage<TObject, TData> $storage): int;
    public function rewind(): void;
    public function serialize(): string;
    public function setInfo(TData $info): void;
    public function unserialize(string $data): void;
    public function valid(): bool;
}

class ArrayObject<TKey = string|int, TValue = mixed> implements \IteratorAggregate<TKey, TValue>, \ArrayAccess<TKey, TValue>, \Serializable, \Countable {
    public function __construct(array<TKey, TValue>|object $array = [], int $flags = 0, string $iteratorClass = "ArrayIterator"): void;
    public function append(TValue $value): void;
    public function asort(int $flags = \SORT_REGULAR): bool;
    public function count(): int;
    public function exchangeArray(array<TKey, TValue>|object $array): array<TKey, TValue>;
    public function getArrayCopy(): array<TKey, TValue>;
    public function getFlags(): int;
    public function getIterator(): \ArrayIterator<TKey, TValue>;
    public function getIteratorClass(): string;
    public function ksort(int $flags = \SORT_REGULAR): bool;
    public function natcasesort(): bool;
    public function natsort(): bool;
    public function offsetExists(TKey $key): bool;
    public function offsetGet(TKey $key): TValue;
    public function offsetSet(TKey $key, TValue $value): void;
    public function offsetUnset(TKey $key): void;
    public function serialize(): string;
    public function setFlags(int $flags): void;
    public function setIteratorClass(string $iteratorClass): void;
    public function uasort(callable $callback): bool;
    public function uksort(callable $callback): bool;
    public function unserialize(string $data): void;
}

class ArrayIterator<TKey = string|int, TValue = mixed> implements \SeekableIterator<TKey, TValue>, \ArrayAccess<TKey, TValue>, \Serializable, \Countable {
    public function __construct(array<TKey, TValue>|object $array = [], int $flags = 0): void;
    public function append(TValue $value): void;
    public function asort(int $flags = \SORT_REGULAR): bool;
    public function count(): int;
    public function current(): TValue;
    public function getArrayCopy(): array<TKey, TValue>;
    public function getFlags(): int;
    public function key(): TKey;
    public function ksort(int $flags = \SORT_REGULAR): bool;
    public function natcasesort(): bool;
    public function natsort(): bool;
    public function next(): void;
    public function offsetExists(TKey $key): bool;
    public function offsetGet(TKey $key): TValue;
    public function offsetSet(TKey $key, TValue $value): void;
    public function offsetUnset(TKey $key): void;
    public function rewind(): void;
    public function seek(int $offset): void;
    public function serialize(): string;
    public function setFlags(int $flags): void;
    public function uasort(callable $callback): bool;
    public function uksort(callable $callback): bool;
    public function unserialize(string $data): void;
    public function valid(): bool;
}

class RecursiveArrayIterator<TKey = string|int, TValue = mixed> extends \ArrayIterator<TKey, TValue> implements \RecursiveIterator<TKey, TValue> {
    public function getChildren(): ?\RecursiveArrayIterator<TKey, TValue>;
    public function hasChildren(): bool;
}
```

Each of the above types must have the `// @generated-original:` comment block before it, containing the complete original non-generic version. The code blocks above show only the generic versions for brevity.

**3.3 — Lint After Changes**

```bash
tyhp lint runtime/packages/php/_tyhpdef/Ext.Core.tyhpdef
tyhp lint runtime/packages/php/_tyhpdef/Ext.SPL.tyhpdef
```

### Acceptance Criteria

- [ ] `Ext.Core.tyhpdef` contains generic declarations for all 8 Core types (Traversable, Iterator, IteratorAggregate, ArrayAccess, Generator, WeakReference, WeakMap, Fiber)
- [ ] `Ext.SPL.tyhpdef` contains generic declarations for all 15 SPL types
- [ ] Every generic declaration has a `// @generated-original:` comment block above it with the complete original non-generic version
- [ ] Generic type parameter defaults are used (e.g., `TKey = mixed`, `T extends object = object`, `TKey = string|int`)
- [ ] Both files parse without errors via `tyhp lint`
- [ ] `SplPriorityQueue` defaults `TPriority` to `int`
- [ ] `ArrayObject` and `ArrayIterator` default `TKey` to `string|int`
- [ ] `WeakReference` and `WeakMap` constrain key types with `extends object`

---

## Phase 4: Scalar & Callable Extension Classes

### Phase Overview

Create standalone extension classes in `.tyhp` files under `_tyhpdef/support/` that add methods to PHP scalar types (`string`, `int`, `float`, `bool`), `array`, and `Closure`. All extension classes are automatically available via the tyhpdef extension auto-inclusion mechanism (declared in the `.tyhpdef` files, automatically in scope when the target type is referenced) and annotated with `#[\Tyhp\Optimize\Inline]` and `#[\Tyhp\Optimize\Pure]` where appropriate.

### Deliverables

1. `_tyhpdef/support/string.tyhp` — 65 methods on `string`
2. `_tyhpdef/support/array.tyhp` — 56 methods on `array`
3. `_tyhpdef/support/int.tyhp` — 20 methods on `int`
4. `_tyhpdef/support/float.tyhp` — 26 methods on `float`
5. `_tyhpdef/support/bool.tyhp` — 5 methods on `bool`
6. `_tyhpdef/support/closure.tyhp` — 4 methods on `\Closure`

Total: **176 extension methods**.

### Implementation Details

**4.1 — String Extension Class**

File: `runtime/packages/php/_tyhpdef/support/string.tyhp`

String methods use `mb_*` functions as defaults. No encoding parameter is exposed — PHP's `mbstring.internal_encoding` or `default_charset` applies. Non-multibyte alternatives use the `byte` prefix.

**Code examples (establishing the pattern):**

```tyhp
<?tyhp

extension StringMethods {
    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn length(extends string $this): int => \mb_strlen($this);

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn byteLength(extends string $this): int => \strlen($this);

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn isEmpty(extends string $this): bool => $this === '';

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn contains(extends string $this, string $needle): bool => \str_contains($this, $needle);

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn substring(extends string $this, int $start, ?int $length = null): string => \mb_substr($this, $start, $length);

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn replace(extends string $this, string|array $search, string|array $replace): string => \str_replace($search, $replace, $this);

    #[\Tyhp\Optimize\Pure]
    function match(extends string $this, string $pattern): ?array {
        $matches = [];
        if (\preg_match($pattern, $this, $matches) === 1) {
            return $matches;
        }
        return null;
    }

    #[\Tyhp\Optimize\Pure]
    function matchAll(extends string $this, string $pattern, int $flags = 0): array {
        $matches = [];
        \preg_match_all($pattern, $this, $matches, $flags);
        return $matches;
    }

    #[\Tyhp\Optimize\Pure]
    fn reverse(extends string $this): string => \implode('', \array_reverse(\mb_str_split($this)));
}
```

**Complete method catalog (all 65 methods):**

| Method Signature | Maps To | Inline | Pure |
|---|---|---|---|
| `length(): int` | `\mb_strlen($this)` | Yes | Yes |
| `byteLength(): int` | `\strlen($this)` | Yes | Yes |
| `isEmpty(): bool` | `$this === ''` | Yes | Yes |
| `isNotEmpty(): bool` | `$this !== ''` | Yes | Yes |
| `contains(string $needle): bool` | `\str_contains($this, $needle)` | Yes | Yes |
| `containsIgnoreCase(string $needle): bool` | `\mb_stripos($this, $needle) !== false` | Yes | Yes |
| `startsWith(string $prefix): bool` | `\str_starts_with($this, $prefix)` | Yes | Yes |
| `endsWith(string $suffix): bool` | `\str_ends_with($this, $suffix)` | Yes | Yes |
| `indexOf(string $needle, int $offset = 0): int\|false` | `\mb_strpos($this, $needle, $offset)` | Yes | Yes |
| `lastIndexOf(string $needle, int $offset = 0): int\|false` | `\mb_strrpos($this, $needle, $offset)` | Yes | Yes |
| `indexOfIgnoreCase(string $needle, int $offset = 0): int\|false` | `\mb_stripos($this, $needle, $offset)` | Yes | Yes |
| `substring(int $start, ?int $length = null): string` | `\mb_substr($this, $start, $length)` | Yes | Yes |
| `charAt(int $index): string` | `\mb_substr($this, $index, 1)` | Yes | Yes |
| `first(int $length = 1): string` | `\mb_substr($this, 0, $length)` | Yes | Yes |
| `last(int $length = 1): string` | `\mb_substr($this, -$length)` | Yes | Yes |
| `chunk(int $length = 1): array<int, string>` | `\mb_str_split($this, $length)` | Yes | Yes |
| `toLower(): string` | `\mb_strtolower($this)` | Yes | Yes |
| `toUpper(): string` | `\mb_strtoupper($this)` | Yes | Yes |
| `ucFirst(): string` | `\mb_strtoupper(\mb_substr($this, 0, 1)) . \mb_substr($this, 1)` | Yes | Yes |
| `lcFirst(): string` | `\mb_strtolower(\mb_substr($this, 0, 1)) . \mb_substr($this, 1)` | Yes | Yes |
| `ucWords(): string` | `\mb_convert_case($this, \MB_CASE_TITLE)` | Yes | Yes |
| `trim(string $characters = " \t\n\r\0\x0B"): string` | `\trim($this, $characters)` | Yes | Yes |
| `trimLeft(string $characters = " \t\n\r\0\x0B"): string` | `\ltrim($this, $characters)` | Yes | Yes |
| `trimRight(string $characters = " \t\n\r\0\x0B"): string` | `\rtrim($this, $characters)` | Yes | Yes |
| `replace(string\|array $search, string\|array $replace): string` | `\str_replace($search, $replace, $this)` | Yes | Yes |
| `replaceIgnoreCase(string\|array $search, string\|array $replace): string` | `\str_ireplace($search, $replace, $this)` | Yes | Yes |
| `regexReplace(string $pattern, string $replacement): string\|null` | `\preg_replace($pattern, $replacement, $this)` | Yes | Yes |
| `split(string $delimiter, int $limit = \PHP_INT_MAX): array<int, string>` | `\explode($delimiter, $this, $limit)` | Yes | Yes |
| `regexSplit(string $pattern, int $limit = -1, int $flags = 0): array<int, string>\|false` | `\preg_split($pattern, $this, $limit, $flags)` | Yes | Yes |
| `pad(int $length, string $padString = ' ', int $padType = \STR_PAD_RIGHT): string` | `\str_pad($this, $length, $padString, $padType)` | Yes | Yes |
| `padLeft(int $length, string $padString = ' '): string` | `\str_pad($this, $length, $padString, \STR_PAD_LEFT)` | Yes | Yes |
| `padRight(int $length, string $padString = ' '): string` | `\str_pad($this, $length, $padString, \STR_PAD_RIGHT)` | Yes | Yes |
| `repeat(int $times): string` | `\str_repeat($this, $times)` | Yes | Yes |
| `reverse(): string` | `\implode('', \array_reverse(\mb_str_split($this)))` | No | Yes |
| `byteReverse(): string` | `\strrev($this)` | Yes | Yes |
| `wordWrap(int $width = 75, string $break = "\n", bool $cutLongWords = false): string` | `\wordwrap($this, $width, $break, $cutLongWords)` | Yes | Yes |
| `wordCount(int $format = 0, ?string $characters = null): array\|int` | `\str_word_count($this, $format, $characters)` | Yes | Yes |
| `format(mixed ...$args): string` | `\sprintf($this, ...$args)` | Yes | Yes |
| `base64Encode(): string` | `\base64_encode($this)` | Yes | Yes |
| `base64Decode(bool $strict = false): string\|false` | `\base64_decode($this, $strict)` | Yes | Yes |
| `urlEncode(): string` | `\urlencode($this)` | Yes | Yes |
| `urlDecode(): string` | `\urldecode($this)` | Yes | Yes |
| `rawUrlEncode(): string` | `\rawurlencode($this)` | Yes | Yes |
| `rawUrlDecode(): string` | `\rawurldecode($this)` | Yes | Yes |
| `htmlEncode(int $flags = \ENT_QUOTES \| \ENT_SUBSTITUTE \| \ENT_HTML401, bool $doubleEncode = true): string` | `\htmlspecialchars($this, $flags, null, $doubleEncode)` | Yes | Yes |
| `htmlDecode(int $flags = \ENT_QUOTES \| \ENT_SUBSTITUTE \| \ENT_HTML401): string` | `\htmlspecialchars_decode($this, $flags)` | Yes | Yes |
| `htmlEntities(int $flags = \ENT_QUOTES \| \ENT_SUBSTITUTE \| \ENT_HTML401, bool $doubleEncode = true): string` | `\htmlentities($this, $flags, null, $doubleEncode)` | Yes | Yes |
| `nl2Br(bool $useXhtml = true): string` | `\nl2br($this, $useXhtml)` | Yes | Yes |
| `stripTags(array\|string\|null $allowedTags = null): string` | `\strip_tags($this, $allowedTags)` | Yes | Yes |
| `md5(bool $binary = false): string` | `\md5($this, $binary)` | Yes | Yes |
| `sha1(bool $binary = false): string` | `\sha1($this, $binary)` | Yes | Yes |
| `hash(string $algorithm, bool $binary = false): string` | `\hash($algorithm, $this, $binary)` | Yes | Yes |
| `crc32(): int` | `\crc32($this)` | Yes | Yes |
| `__toInt(): int` | `\intval($this)` | Yes | Yes |
| `__toFloat(): float` | `\floatval($this)` | Yes | Yes |
| `__toBool(): bool` | `(bool)$this` | Yes | Yes |
| `isNumeric(): bool` | `\is_numeric($this)` | Yes | Yes |
| `matches(string $pattern): bool` | `\preg_match($pattern, $this) === 1` | Yes | Yes |
| `match(string $pattern): ?array` | `\preg_match()` + matches | No | Yes |
| `matchAll(string $pattern, int $flags = 0): array` | `\preg_match_all()` + matches | No | Yes |
| `regexQuote(string $delimiter = '/'): string` | `\preg_quote($this, $delimiter)` | Yes | Yes |
| `convertEncoding(string $toEncoding, array\|string\|null $fromEncoding = null): string\|false` | `\mb_convert_encoding($this, $toEncoding, $fromEncoding)` | Yes | Yes |
| `detectEncoding(): string\|false` | `\mb_detect_encoding($this)` | Yes | Yes |
| `ord(): int\|false` | `\mb_ord($this)` | Yes | Yes |
| `jsonDecode(bool $associative = true, int $depth = 512, int $flags = 0): mixed` | `\json_decode($this, $associative, $depth, $flags)` | Yes | Yes |

**4.2 — Array Extension Class**

File: `runtime/packages/php/_tyhpdef/support/array.tyhp`

Array methods use generics (`<TKey, TValue>`) on each method. Immutable methods return new arrays (past participle names). Mutable methods use `extends array &$this` (present tense names).

**Code examples:**

```tyhp
<?tyhp

extension ArrayMethods {
    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn count<TKey, TValue>(extends array<TKey, TValue> $this): int => \count($this);

    #[\Tyhp\Optimize\Pure]
    function first<TKey, TValue>(extends array<TKey, TValue> $this): ?TValue {
        $k = \array_key_first($this);
        return $k !== null ? $this[$k] : null;
    }

    #[\Tyhp\Optimize\Pure]
    function sorted<TKey, TValue>(extends array<TKey, TValue> $this, int $flags = \SORT_REGULAR): array<int, TValue> {
        $copy = $this;
        \sort($copy, $flags);
        return $copy;
    }

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn mapped<TKey, TValue, TResult>(extends array<TKey, TValue> $this, callable(TValue): TResult $callback): array<TKey, TResult> => \array_map($callback, $this);

    #[\Tyhp\Optimize\Inline]
    fn sort<TKey, TValue>(extends array<TKey, TValue> &$this, int $flags = \SORT_REGULAR): bool => \sort($this, $flags);

    #[\Tyhp\Optimize\Inline]
    fn push<TKey, TValue>(extends array<TKey, TValue> &$this, TValue ...$values): int => \array_push($this, ...$values);

    #[\Tyhp\Optimize\Pure]
    function find<TKey, TValue>(extends array<TKey, TValue> $this, callable(TValue, TKey): bool $callback): ?TValue {
        foreach ($this as $key => $value) {
            if ($callback($value, $key)) {
                return $value;
            }
        }
        return null;
    }
}
```

**Complete method catalog (all 56 methods):**

*Access (Pure):*

| Method Signature | Maps To | Inline | Pure |
|---|---|---|---|
| `count<TKey, TValue>(): int` | `\count($this)` | Yes | Yes |
| `isEmpty<TKey, TValue>(): bool` | `\count($this) === 0` | Yes | Yes |
| `isNotEmpty<TKey, TValue>(): bool` | `\count($this) > 0` | Yes | Yes |
| `first<TKey, TValue>(): ?TValue` | `\array_key_first` + index | No | Yes |
| `last<TKey, TValue>(): ?TValue` | `\array_key_last` + index | No | Yes |
| `keys<TKey, TValue>(): array<int, TKey>` | `\array_keys($this)` | Yes | Yes |
| `values<TKey, TValue>(): array<int, TValue>` | `\array_values($this)` | Yes | Yes |
| `keyExists<TKey, TValue>(TKey $key): bool` | `\array_key_exists($key, $this)` | Yes | Yes |
| `contains<TKey, TValue>(TValue $value, bool $strict = false): bool` | `\in_array($value, $this, $strict)` | Yes | Yes |
| `search<TKey, TValue>(TValue $value, bool $strict = false): TKey\|false` | `\array_search($value, $this, $strict)` | Yes | Yes |
| `column(string\|int\|null $columnKey, string\|int\|null $indexKey = null): array` | `\array_column($this, $columnKey, $indexKey)` | Yes | Yes |

*Search (Pure, fallback implementations for PHP < 8.4):*

| Method Signature | Maps To | Inline | Pure |
|---|---|---|---|
| `find<TKey, TValue>(callable(TValue, TKey): bool $callback): ?TValue` | `foreach` loop | No | Yes |
| `findKey<TKey, TValue>(callable(TValue, TKey): bool $callback): ?TKey` | `foreach` loop | No | Yes |
| `any<TKey, TValue>(callable(TValue, TKey): bool $callback): bool` | `foreach` loop | No | Yes |
| `all<TKey, TValue>(callable(TValue, TKey): bool $callback): bool` | `foreach` loop | No | Yes |

*Immutable Transform (Pure, past participle names):*

| Method Signature | Maps To | Inline | Pure |
|---|---|---|---|
| `sorted<TKey, TValue>(int $flags = \SORT_REGULAR): array<int, TValue>` | copy + `\sort()` | No | Yes |
| `reverseSorted<TKey, TValue>(int $flags = \SORT_REGULAR): array<int, TValue>` | copy + `\rsort()` | No | Yes |
| `sortedByKey<TKey, TValue>(int $flags = \SORT_REGULAR): array<TKey, TValue>` | copy + `\ksort()` | No | Yes |
| `reverseSortedByKey<TKey, TValue>(int $flags = \SORT_REGULAR): array<TKey, TValue>` | copy + `\krsort()` | No | Yes |
| `sortedBy<TKey, TValue>(callable(TValue, TValue): int $callback): array<int, TValue>` | copy + `\usort()` | No | Yes |
| `sortedByKeyWith<TKey, TValue>(callable(TKey, TKey): int $callback): array<TKey, TValue>` | copy + `\uksort()` | No | Yes |
| `reversed<TKey, TValue>(bool $preserveKeys = false): array<TKey, TValue>` | `\array_reverse($this, $preserveKeys)` | Yes | Yes |
| `unique<TKey, TValue>(int $flags = \SORT_STRING): array<TKey, TValue>` | `\array_unique($this, $flags)` | Yes | Yes |
| `filtered<TKey, TValue>(?callable $callback = null, int $mode = 0): array<TKey, TValue>` | `\array_filter($this, $callback, $mode)` | Yes | Yes |
| `mapped<TKey, TValue, TResult>(callable(TValue): TResult $callback): array<TKey, TResult>` | `\array_map($callback, $this)` | Yes | Yes |
| `merged<TKey, TValue>(array<TKey, TValue> ...$arrays): array<TKey, TValue>` | `\array_merge($this, ...$arrays)` | Yes | Yes |
| `sliced<TKey, TValue>(int $offset, ?int $length = null, bool $preserveKeys = false): array<TKey, TValue>` | `\array_slice(...)` | Yes | Yes |
| `chunked<TKey, TValue>(int $length, bool $preserveKeys = false): array<int, array<TKey, TValue>>` | `\array_chunk(...)` | Yes | Yes |
| `flipped<TKey, TValue>(): array<TValue, TKey>` | `\array_flip($this)` | Yes | Yes |
| `padded<TKey, TValue>(int $length, TValue $value): array<TKey, TValue>` | `\array_pad($this, $length, $value)` | Yes | Yes |
| `combined(array $values): array` | `\array_combine($this, $values)` | Yes | Yes |
| `diffed<TKey, TValue>(array<TKey, TValue> ...$arrays): array<TKey, TValue>` | `\array_diff(...)` | Yes | Yes |
| `diffedByKey<TKey, TValue>(array<TKey, TValue> ...$arrays): array<TKey, TValue>` | `\array_diff_key(...)` | Yes | Yes |
| `intersected<TKey, TValue>(array<TKey, TValue> ...$arrays): array<TKey, TValue>` | `\array_intersect(...)` | Yes | Yes |
| `intersectedByKey<TKey, TValue>(array<TKey, TValue> ...$arrays): array<TKey, TValue>` | `\array_intersect_key(...)` | Yes | Yes |
| `zipped(array ...$arrays): array` | `\array_map(null, $this, ...$arrays)` | Yes | Yes |

*Mutable (NOT Pure, present tense names, `&$this`):*

| Method Signature | Maps To | Inline | Pure |
|---|---|---|---|
| `sort<TKey, TValue>(int $flags = \SORT_REGULAR): bool` | `\sort($this, $flags)` | Yes | No |
| `reverseSort<TKey, TValue>(int $flags = \SORT_REGULAR): bool` | `\rsort($this, $flags)` | Yes | No |
| `sortByKey<TKey, TValue>(int $flags = \SORT_REGULAR): bool` | `\ksort($this, $flags)` | Yes | No |
| `reverseSortByKey<TKey, TValue>(int $flags = \SORT_REGULAR): bool` | `\krsort($this, $flags)` | Yes | No |
| `sortBy<TKey, TValue>(callable(TValue, TValue): int $callback): bool` | `\usort($this, $callback)` | Yes | No |
| `sortByKeyWith<TKey, TValue>(callable(TKey, TKey): int $callback): bool` | `\uksort($this, $callback)` | Yes | No |
| `shuffle<TKey, TValue>(): bool` | `\shuffle($this)` | Yes | No |
| `push<TKey, TValue>(TValue ...$values): int` | `\array_push($this, ...$values)` | Yes | No |
| `pop<TKey, TValue>(): ?TValue` | `\array_pop($this)` | Yes | No |
| `shift<TKey, TValue>(): ?TValue` | `\array_shift($this)` | Yes | No |
| `unshift<TKey, TValue>(TValue ...$values): int` | `\array_unshift($this, ...$values)` | Yes | No |
| `splice<TKey, TValue>(int $offset, ?int $length = null, array $replacement = []): array<TKey, TValue>` | `\array_splice(...)` | Yes | No |

*Aggregate (Pure):*

| Method Signature | Maps To | Inline | Pure |
|---|---|---|---|
| `reduce<TKey, TValue, TResult>(callable(TResult, TValue): TResult $callback, TResult $initial): TResult` | `\array_reduce(...)` | Yes | Yes |
| `join<TKey, TValue>(string $separator = ''): string` | `\implode($separator, $this)` | Yes | Yes |
| `sum(): int\|float` | `\array_sum($this)` | Yes | Yes |
| `product(): int\|float` | `\array_product($this)` | Yes | Yes |
| `countValues<TKey, TValue>(): array<TValue, int>` | `\array_count_values($this)` | Yes | Yes |
| `toJson(int $flags = 0, int $depth = 512): string\|false` | `\json_encode($this, $flags, $depth)` | Yes | Yes |

*Misc:*

| Method Signature | Maps To | Inline | Pure |
|---|---|---|---|
| `walk<TKey, TValue>(callable(TValue, TKey): void $callback): bool` | `\array_walk($this, $callback)` (mutable, `&$this`) | Yes | No |
| `flatten<TKey, TValue>(): array` | `\array_walk_recursive` + collect | No | Yes |

**4.3 — Int Extension Class**

File: `runtime/packages/php/_tyhpdef/support/int.tyhp`

```tyhp
<?tyhp

extension IntMethods {
    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn abs(extends int $this): int => \abs($this);

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn clamp(extends int $this, int $min, int $max): int => \max($min, \min($max, $this));

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn isEven(extends int $this): bool => $this % 2 === 0;

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn between(extends int $this, int $min, int $max): bool => $this >= $min && $this <= $max;
}
```

| Method Signature | Maps To | Inline | Pure |
|---|---|---|---|
| `abs(): int` | `\abs($this)` | Yes | Yes |
| `clamp(int $min, int $max): int` | `\max($min, \min($max, $this))` | Yes | Yes |
| `isEven(): bool` | `$this % 2 === 0` | Yes | Yes |
| `isOdd(): bool` | `$this % 2 !== 0` | Yes | Yes |
| `isPositive(): bool` | `$this > 0` | Yes | Yes |
| `isNegative(): bool` | `$this < 0` | Yes | Yes |
| `isZero(): bool` | `$this === 0` | Yes | Yes |
| `between(int $min, int $max): bool` | `$this >= $min && $this <= $max` | Yes | Yes |
| `sign(): int` | `$this <=> 0` | Yes | Yes |
| `max(int ...$values): int` | `\max($this, ...$values)` | Yes | Yes |
| `min(int ...$values): int` | `\min($this, ...$values)` | Yes | Yes |
| `pow(int $exponent): int\|float` | `$this ** $exponent` | Yes | Yes |
| `__toString(): string` | `(string)$this` | Yes | Yes |
| `__toFloat(): float` | `(float)$this` | Yes | Yes |
| `__toBool(): bool` | `(bool)$this` | Yes | Yes |
| `toBase(int $base): string` | `\base_convert((string)$this, 10, $base)` | Yes | Yes |
| `toHex(): string` | `\dechex($this)` | Yes | Yes |
| `toOctal(): string` | `\decoct($this)` | Yes | Yes |
| `toBinary(): string` | `\decbin($this)` | Yes | Yes |
| `format(int $decimals = 0, string $decimalSeparator = '.', string $thousandsSeparator = ','): string` | `\number_format(...)` | Yes | Yes |

**4.4 — Float Extension Class**

File: `runtime/packages/php/_tyhpdef/support/float.tyhp`

```tyhp
<?tyhp

extension FloatMethods {
    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn abs(extends float $this): float => \abs($this);

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn ceil(extends float $this): int => (int)\ceil($this);

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn round(extends float $this, int $precision = 0, int $mode = \PHP_ROUND_HALF_UP): float => \round($this, $precision, $mode);

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn isNan(extends float $this): bool => \is_nan($this);
}
```

| Method Signature | Maps To | Inline | Pure |
|---|---|---|---|
| `abs(): float` | `\abs($this)` | Yes | Yes |
| `ceil(): int` | `(int)\ceil($this)` | Yes | Yes |
| `floor(): int` | `(int)\floor($this)` | Yes | Yes |
| `round(int $precision = 0, int $mode = \PHP_ROUND_HALF_UP): float` | `\round(...)` | Yes | Yes |
| `clamp(float $min, float $max): float` | `\max($min, \min($max, $this))` | Yes | Yes |
| `isNan(): bool` | `\is_nan($this)` | Yes | Yes |
| `isInfinite(): bool` | `\is_infinite($this)` | Yes | Yes |
| `isFinite(): bool` | `\is_finite($this)` | Yes | Yes |
| `isPositive(): bool` | `$this > 0.0` | Yes | Yes |
| `isNegative(): bool` | `$this < 0.0` | Yes | Yes |
| `isZero(): bool` | `$this == 0.0` | Yes | Yes |
| `between(float $min, float $max): bool` | `$this >= $min && $this <= $max` | Yes | Yes |
| `sign(): int` | `$this <=> 0.0` | Yes | Yes |
| `max(float ...$values): float` | `\max($this, ...$values)` | Yes | Yes |
| `min(float ...$values): float` | `\min($this, ...$values)` | Yes | Yes |
| `pow(float $exponent): float` | `$this ** $exponent` | Yes | Yes |
| `sqrt(): float` | `\sqrt($this)` | Yes | Yes |
| `log(float $base = \M_E): float` | `\log($this, $base)` | Yes | Yes |
| `sin(): float` | `\sin($this)` | Yes | Yes |
| `cos(): float` | `\cos($this)` | Yes | Yes |
| `tan(): float` | `\tan($this)` | Yes | Yes |
| `fmod(float $divisor): float` | `\fmod($this, $divisor)` | Yes | Yes |
| `__toString(): string` | `(string)$this` | Yes | Yes |
| `__toInt(): int` | `(int)$this` | Yes | Yes |
| `__toBool(): bool` | `(bool)$this` | Yes | Yes |
| `format(int $decimals = 0, string $decimalSeparator = '.', string $thousandsSeparator = ','): string` | `\number_format(...)` | Yes | Yes |

**4.5 — Bool Extension Class**

File: `runtime/packages/php/_tyhpdef/support/bool.tyhp`

```tyhp
<?tyhp

extension BoolMethods {
    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn __toString(extends bool $this): string => $this ? 'true' : 'false';

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn __toInt(extends bool $this): int => (int)$this;

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn not(extends bool $this): bool => !$this;

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn and(extends bool $this, bool $other): bool => $this && $other;

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn or(extends bool $this, bool $other): bool => $this || $other;
}
```

**4.6 — Closure Extension Class**

File: `runtime/packages/php/_tyhpdef/support/closure.tyhp`

Closure extension methods add utility functions for function composition, partial application, and memoization. These have real implementation bodies (not simple PHP function wrappers) and are NOT marked Inline.

```tyhp
<?tyhp

extension ClosureMethods {
    #[\Tyhp\Optimize\Pure]
    function compose(extends \Closure $this, \Closure $next): \Closure {
        $current = $this;
        return function(mixed ...$args) use ($current, $next): mixed {
            return $next($current(...$args));
        };
    }

    #[\Tyhp\Optimize\Pure]
    function then(extends \Closure $this, \Closure $next): \Closure {
        $current = $this;
        return function(mixed ...$args) use ($current, $next): mixed {
            return $next($current(...$args));
        };
    }

    function partial(extends \Closure $this, mixed ...$partialArgs): \Closure {
        $fn = $this;
        return function(mixed ...$args) use ($fn, $partialArgs): mixed {
            return $fn(...$partialArgs, ...$args);
        };
    }

    function memoize(extends \Closure $this): \Closure {
        $fn = $this;
        $cache = [];
        return function(mixed ...$args) use ($fn, &$cache): mixed {
            $key = \serialize($args);
            if (!\array_key_exists($key, $cache)) {
                $cache[$key] = $fn(...$args);
            }
            return $cache[$key];
        };
    }
}
```

### Acceptance Criteria

- [ ] All 6 extension class files exist in `_tyhpdef/support/`
- [ ] All files parse without errors via `tyhp lint`
- [ ] String methods use `mb_*` functions as defaults, `byte` prefix for non-multibyte alternatives
- [ ] Array immutable methods use past participle names, mutable use present tense with `&$this`
- [ ] All simple wrapper methods have `#[\Tyhp\Optimize\Inline]`
- [ ] All pure methods have `#[\Tyhp\Optimize\Pure]`
- [ ] Mutable array methods are NOT marked Pure
- [ ] Closure methods with complex bodies are NOT marked Inline
- [ ] Extension method generic type parameters are per-method (not on the extension class)
- [ ] All extension classes are automatically included via the tyhpdef extension auto-inclusion mechanism (no explicit `import extension` required)
- [ ] Total method count: 176 (65 string + 56 array + 20 int + 26 float + 5 bool + 4 closure)

---

## Phase 5: Validation and Testing

### Phase Overview

Validate the complete package — all `.tyhpdef` files, all `.tyhp` extension class files, and the `package.tyhp.json` manifest — by linting and creating test projects.

### Deliverables

1. All package files pass `tyhp lint`
2. A test project at `runtime/packages/php/tests/` exercises generic types and extension methods
3. The test project compiles successfully with `tyhp build`

### Implementation Details

**5.1 — Lint All Package Files**

```bash
tyhp lint runtime/packages/php/_tyhpdef/
tyhp lint runtime/packages/php/_tyhpdef/support/
```

**5.2 — Create Test Project Configuration**

File: `runtime/packages/php/tests/tyhp.json`

```json
{
    "quiet": false,
    "locale": "en-US",
    "include": [
        "./**/*.tyhp"
    ],
    "exclude": [],
    "output": {
        "path": "./build"
    }
}
```

**5.3 — Create Generics Test File**

File: `runtime/packages/php/tests/test_generics.tyhp`

```tyhp
<?tyhp

function testIterator(\Iterator<string, int> $iter): void {
    string $key = $iter->key();
    int $value = $iter->current();
}

function testIteratorUntyped(\Iterator $iter): void {
    mixed $key = $iter->key();
    mixed $value = $iter->current();
}

function testGenerator(): \Generator<int, string, null, bool> {
    yield 0 => "hello";
    yield 1 => "world";
    return true;
}

function testWeakMap(): void {
    \WeakMap<\stdClass, string> $map = new \WeakMap();
    \stdClass $obj = new \stdClass();
    $map[$obj] = "test";
    string $val = $map[$obj];
}

function testSplStack(): void {
    \SplStack<int> $stack = new \SplStack();
    $stack->push(42);
    int $top = $stack->top();
}

function testSplQueue(): void {
    \SplQueue<string> $queue = new \SplQueue();
    $queue->enqueue("first");
    string $item = $queue->dequeue();
}

function testSplPriorityQueue(): void {
    \SplPriorityQueue<string, int> $pq = new \SplPriorityQueue();
    $pq->insert("task", 5);
    string $top = $pq->current();
}

function testSplFixedArray(): void {
    \SplFixedArray<float> $arr = new \SplFixedArray(10);
    $arr[0] = 3.14;
    float $val = $arr[0];
}

function testArrayObject(): void {
    \ArrayObject<string, int> $ao = new \ArrayObject(["a" => 1, "b" => 2]);
    array<string, int> $copy = $ao->getArrayCopy();
    $ao["c"] = 3;
}

function testSplObjectStorage(): void {
    \SplObjectStorage<\stdClass, string> $storage = new \SplObjectStorage();
    \stdClass $obj = new \stdClass();
    $storage->attach($obj, "data");
    string $info = $storage->getInfo();
}

function testWeakReference(): void {
    \stdClass $obj = new \stdClass();
    \WeakReference<\stdClass> $ref = \WeakReference::create($obj);
    ?\stdClass $gotten = $ref->get();
}

function testFiber(): void {
    \Fiber<string, int, string, int> $fiber = new \Fiber(function (string $start): int {
        int $resume = \Fiber::suspend("suspended");
        return 42;
    });
    string $suspended = $fiber->start("hello");
    string $suspended2 = $fiber->resume(10);
    int $result = $fiber->getReturn();
}
```

**5.4 — Create Extension Methods Test File**

File: `runtime/packages/php/tests/test_extensions.tyhp`

```tyhp
<?tyhp

function testStringExtensions(): void {
    string $s = "Hello, World!";
    int $len = $s->length();
    int $byteLen = $s->byteLength();
    bool $empty = $s->isEmpty();
    bool $contains = $s->contains("World");
    string $lower = $s->toLower();
    string $upper = $s->toUpper();
    string $trimmed = $s->trim();
    string $replaced = $s->replace("World", "Tyhp");
    array<int, string> $parts = $s->split(", ");
    string $sub = $s->substring(0, 5);
    string $encoded = $s->base64Encode();
    string $hashed = $s->md5();
    bool $numeric = $s->isNumeric();
    bool $matches = $s->matches('/^Hello/');
    ?array $match = $s->match('/(\w+)/');
    string $reversed = $s->reverse();
}

function testArrayExtensions(): void {
    array<string, int> $arr = ["a" => 1, "b" => 2, "c" => 3];
    int $count = $arr->count();
    bool $empty = $arr->isEmpty();
    ?int $first = $arr->first();
    ?int $last = $arr->last();
    array<int, string> $keys = $arr->keys();
    array<int, int> $values = $arr->values();
    bool $has = $arr->keyExists("a");
    bool $contains = $arr->contains(2);
    string $json = $arr->toJson();

    array<string, int> $sorted = $arr->sortedByKey();
    array<string, int> $filtered = $arr->filtered(fn(int $v): bool => $v > 1);
    array<string, string> $mapped = $arr->mapped(fn(int $v): string => (string)$v);
    string $joined = $arr->values()->join(", ");
    int $sum = $arr->values()->sum();

    array<int, int> $nums = [3, 1, 4, 1, 5, 9];
    array<int, int> $immutableSorted = $nums->sorted();
    $nums->sort();
    $nums->push(2);
    ?int $popped = $nums->pop();
}

function testIntExtensions(): void {
    int $n = 42;
    int $abs = (-5)->abs();
    int $clamped = $n->clamp(0, 100);
    bool $even = $n->isEven();
    bool $between = $n->between(10, 50);
    string $hex = $n->toHex();
    string $formatted = $n->format(0, '.', ',');
}

function testFloatExtensions(): void {
    float $f = 3.14159;
    int $ceil = $f->ceil();
    int $floor = $f->floor();
    float $rounded = $f->round(2);
    float $clamped = $f->clamp(0.0, 10.0);
    bool $nan = $f->isNan();
    float $sq = $f->sqrt();
    string $formatted = $f->format(2);
}

function testBoolExtensions(): void {
    bool $b = true;
    string $str = $b->__toString();
    int $int = $b->__toInt();
    bool $negated = $b->not();
}

function testClosureExtensions(): void {
    \Closure $double = fn(int $x): int => $x * 2;
    \Closure $add1 = fn(int $x): int => $x + 1;
    \Closure $composed = $double->then($add1);
    \Closure $memoized = $double->memoize();
    \Closure $partial = (fn(int $a, int $b): int => $a + $b)->partial(10);
}
```

**5.5 — Build Test Project**

```bash
cd runtime/packages/php/tests
tyhp build
```

### Acceptance Criteria

- [ ] All `.tyhpdef` and `.tyhp` files pass `tyhp lint` without errors
- [ ] Test project files exist at `runtime/packages/php/tests/`
- [ ] Test project covers all generic overlay types from Phase 3
- [ ] Test project covers all scalar/closure extension methods from Phase 4
- [ ] Test project parses without errors (minimum) or builds successfully (full validation)
- [ ] Generic type defaults work (e.g., `\Iterator` without type args compiles as `\Iterator<mixed, mixed>`)

---

## Phase 6: Version Gating Across PHP Minors

### Phase Overview

Author and maintain **one** gated tree under `runtime/packages/php/` that covers PHP 8.2–8.5. Do **not** create `php-8.3` / `php-8.4` / `php-8.5` package forks via `cp -r`. Version-specific APIs are expressed with Story 20.5 `declare(php=…)` / `#[\Tyhp\Php]`. Prefer Story 20 Phase 8 multi-target gated tyhpdef generation when available; until then, hand-apply gates using the difference table below as guidance.

### Deliverables

1. Version-specific APIs gated inside the single `runtime/packages/php/` tree (no additional base packages)
2. Scalar extension methods that gain native PHP helpers on later minors use gated implementations (or gated declare blocks around extension methods)
3. Lint/build matrix against `output.phpVersion` ∈ {8.2, 8.3, 8.4, 8.5} on the **single** package
4. Optional extension packages that are PHP-version-specific (e.g. `uri`, `lexbor`) live as `tyhp/php-ext-*` with file- or block-level `declare(php=">=8.5")` (not separate `php-8.5-ext-*` package names)

### Implementation Details

**6.1 — Version Difference Analysis**

Use this table as guidance for what to gate inside the single package (and related `tyhp/php-ext-*` packages). Language features that are not stubs (pipe operator, property hooks, etc.) are listed for awareness; this phase focuses on tyhpdef / scalar-extension surface that must appear only when `output.phpVersion` satisfies the gate.

| Feature | PHP 8.2 | PHP 8.3 | PHP 8.4 | PHP 8.5 |
|---|---|---|---|---|
| `Fiber` | Present | Present | Present | Present |
| `Random\Randomizer` | Added | Present | Present | Present |
| `readonly` classes | Added | Present | Present | Present |
| `json_validate()` | No | Added | Present | Present |
| `mb_str_pad()` | No | Added | Present | Present |
| `Randomizer::getBytesFromString()` | No | Added | Present | Present |
| `Randomizer::getFloat()` / `nextFloat()` | No | Added | Present | Present |
| `array_find()` / `array_find_key()` | No | No | Added | Present |
| `array_any()` / `array_all()` | No | No | Added | Present |
| Property hooks | No | No | Added | Present |
| `new` without parens | No | No | Added | Present |
| `Deprecated` attribute | No | No | Added | Present |
| `request_parse_body()` | No | No | Added | Present |
| `BcMath\Number` class | No | No | Added | Present |
| `mb_ucfirst()` / `mb_lcfirst()` | No | No | Added | Present |
| Pipe operator (`\|>`) | No | No | No | Added |
| `uri` extension | No | No | No | Added |
| `#[\NoDiscard]` attribute | No | No | No | Added |
| `clone` with named args | No | No | No | Added |
| `array_first()` / `array_last()` | No | No | No | Added |
| Closures in constant exprs | No | No | No | Added |
| Asymmetric visibility (static) | No | No | No | Added |
| Persistent cURL share handles | No | No | No | Added |

**6.2 — Prefer Story 20 Phase 8 multi-target generation**

When Story 20 Phase 8 is available, regenerate (or refresh) tyhpdefs from multiple PHP installations into the **same** `runtime/packages/php/_tyhpdef/` tree with automatic Story 20.5 gates. Until then, hand-apply the gates in 6.3–6.5 against the baseline 8.2 tree from Phases 1–5.

**Do not:**

```bash
# ❌ FORBIDDEN — per-minor package forks
cp -r runtime/packages/php runtime/packages/php-8.3
cp -r runtime/packages/php runtime/packages/php-8.4
cp -r runtime/packages/php runtime/packages/php-8.5
```

**6.3 — Gate PHP 8.3+ APIs**

In the single package, add (or wrap) 8.3 introductions with Story 20.5 gates, e.g.:

```tyhpdef
declare(php=">=8.3") {
    #[\Tyhp\Optimize\Pure]
    function json_validate(string $json, int $depth = 512, int $flags = 0): bool;
}

// Or member/type attribute form where appropriate:
#[\Tyhp\Php(">=8.3")]
function mb_str_pad(/* … */): string;
```

Tyhpdef / attribute targets (guidance from the table):

- `json_validate()` in `Ext.Json.tyhpdef` (mark `#[\Tyhp\Optimize\Pure]`)
- `mb_str_pad()` in `Ext.Mbstring.tyhpdef`
- `Randomizer::getBytesFromString()`, `Randomizer::getFloat()`, `Randomizer::nextFloat()` in `Ext.Random.tyhpdef`

Scalar extension updates (gate with `declare(php=…) { … }` — attributes are not allowed on `extension` declarations):

- String `pad()`, `padLeft()`, `padRight()` may use `\mb_str_pad()` under `declare(php=">=8.3")`, with an 8.2 fallback arm using `\str_pad()` if desired (version-disjoint same-name OK)

**6.4 — Gate PHP 8.4+ APIs**

- `array_find()`, `array_find_key()`, `array_any()`, `array_all()` in `Ext.Standard.tyhpdef` (all `#[\Tyhp\Optimize\Pure]`), gated `>=8.4`
- `Deprecated` attribute class in `Ext.Core.tyhpdef`
- `request_parse_body()` in `Ext.Standard.tyhpdef`
- `mb_ucfirst()`, `mb_lcfirst()` in `Ext.Mbstring.tyhpdef`

Scalar extension updates:

- Array `find()`, `findKey()`, `any()`, `all()` — prefer native `\array_find()` etc. under `declare(php=">=8.4")`, keep foreach fallbacks for `<8.4` if the methods exist in the base package for all targets
- String `ucFirst()`, `lcFirst()` — `\mb_ucfirst()` / `\mb_lcfirst()` under `>=8.4`

**6.5 — Gate PHP 8.5+ APIs**

- `array_first()`, `array_last()` in `Ext.Standard.tyhpdef` (both `#[\Tyhp\Optimize\Pure]`), gated `>=8.5`
- `#[\NoDiscard]` attribute class in `Ext.Core.tyhpdef`
- `uri` / `lexbor` types are **not** in the base package — they go in `tyhp/php-ext-uri` / `tyhp/php-ext-lexbor` (Phase 15), with package contents gated `declare(php=">=8.5")` so inactive targets skip silently

Scalar extension updates:

- Array `first()` / `last()` — native `\array_first()` / `\array_last()` under `>=8.5`

**6.6 — Verify single package across `output.phpVersion` matrix**

Lint and build the **same** package with each target PHP version (Story 20.5 + Story 10 `output.phpVersion`):

```bash
# Example matrix — adjust to whatever CLI/config flag sets output.phpVersion
for v in 8.2 8.3 8.4 8.5; do
  tyhp lint runtime/packages/php/_tyhpdef/ --php-version="$v"
  tyhp lint runtime/packages/php/_tyhpdef/support/ --php-version="$v"
  # tests/tyhp.json (or CLI) sets "output": { "phpVersion": "$v" }
  (cd runtime/packages/php/tests && tyhp build)
done
```

Confirm:

- Under 8.2, gated 8.3+ symbols are absent (no resolve errors for unrelated code; gated APIs not visible)
- Under 8.3+, `json_validate` / `mb_str_pad` are visible
- Under 8.4+, `array_find` family / `mb_ucfirst` are visible
- Under 8.5+, `array_first` / `array_last` / `NoDiscard` are visible
- No `runtime/packages/php-8.{3,4,5}/` directories exist

### Acceptance Criteria

- [ ] Only one base package directory: `runtime/packages/php/` (no `php-8.3` / `php-8.4` / `php-8.5` forks)
- [ ] Composer `require.php` remains `>=8.2` (not mutually exclusive per-minor constraints)
- [ ] Version-specific tyhpdef APIs use Story 20.5 gates (`declare(php=…)` / `#[\Tyhp\Php]`)
- [ ] Lint/build matrix passes for `output.phpVersion` 8.2, 8.3, 8.4, and 8.5 on the single package
- [ ] 8.3+ gates cover `json_validate()` and `mb_str_pad()` (and Randomizer additions)
- [ ] 8.4+ gates cover `array_find()` etc., `mb_ucfirst()`, `mb_lcfirst()`, and `Deprecated`
- [ ] 8.5+ gates cover `array_first()`, `array_last()`, `#[\NoDiscard]`
- [ ] Version-specific scalar extension implementations use native functions when the active target satisfies the gate
- [ ] Package tests build successfully for each matrix target

---

## Phase 7: Database Extensions

### Phase Overview

Create individual Composer packages for database-related PHP extensions. Each extension gets **one** package (`tyhp/php-ext-{name}` at `runtime/packages/php-ext-{name}/`) for all PHP versions, with Story 20.5 gates for version-specific APIs. This phase establishes the package template used by all subsequent extension phases (Phases 8–15).

### Extensions Included

| Package | Extension | Composer Dependencies |
|---|---|---|
| `tyhp/php-ext-pdo` | PDO | base only |
| `tyhp/php-ext-pdo_mysql` | pdo_mysql | `tyhp/php-ext-pdo`, `tyhp/php-ext-mysqlnd` |
| `tyhp/php-ext-pdo_pgsql` | pdo_pgsql | `tyhp/php-ext-pdo` |
| `tyhp/php-ext-pdo_sqlite` | pdo_sqlite | `tyhp/php-ext-pdo` |
| `tyhp/php-ext-mysqli` | mysqli | base only |
| `tyhp/php-ext-pgsql` | pgsql | base only |
| `tyhp/php-ext-sqlite3` | sqlite3 | base only |

### Implementation Details

**Package Template**

Each individual extension package follows this structure:

```
runtime/packages/php-ext-{name}/
├── composer.json
├── package.tyhp.json
├── _tyhpdef/
│   ├── Ext.{Name}.tyhpdef
│   └── support/          (only if extension adds scalar methods)
│       └── {type}.tyhp
└── tests/
    └── test_{name}.tyhp
```

**Template `composer.json`:**

```json
{
    "name": "tyhp/php-ext-{name}",
    "description": "Tyhp type definitions for the PHP {name} extension (8.2+); version-specific APIs gated via Story 20.5",
    "type": "library",
    "license": "MIT",
    "require": {
        "php": ">=8.2",
        "tyhp/php": "@dev",
        "ext-{name}": "*"
    },
    "extra": {
        "tyhp": {
            "php-version": ">=8.2",
            "extensions": ["{name}"]
        }
    }
}
```

**Template `package.tyhp.json`:**

```json
{
    "include": [
        "./_tyhpdef/*.tyhpdef",
        "./_tyhpdef/support/*.tyhp"
    ]
}
```

**For each extension in this phase:**

1. Create the single package directory structure (`runtime/packages/php-ext-{name}/`)
2. Generate the tyhpdef using Story 20's tyhpdef generator (apply Story 20.5 gates for version-specific APIs; prefer Story 20 Phase 8 multi-target generation when available):
   ```bash
   tyhp generate_tyhpdef --ext-name={name} --output=runtime/packages/php-ext-{name}/_tyhpdef/Ext.{Name}.tyhpdef
   ```
3. Enrich signatures from stub corpora (Phase 2.3 — Psalm / PHPStan / Phan / PhpStorm): narrower param/return types, generics, callable signatures, stub array shapes as Tyhp `struct`s, and type-guard metadata. Apply generic declarations where applicable (using the `// @generated-original:` comment pattern from Phase 3)
4. Create a minimal test file for each package
5. Lint all generated files

**Generic Declarations:**

| Extension | Type | Generic Declaration |
|---|---|---|
| pdo | `PDOStatement` | Complex — fetch mode determines return type. Add generic overlay if the type system can express it, otherwise defer. |

**Tests and Verification:**

Each extension package should have a minimal test file (`tests/test_{name}.tyhp`) that verifies the tyhpdef declarations parse correctly and basic usage compiles. Lint all packages:

```bash
for dir in runtime/packages/php-ext-{pdo,pdo_mysql,pdo_pgsql,pdo_sqlite,mysqli,pgsql,sqlite3}/; do
    tyhp lint "$dir/_tyhpdef/"
done
```

### Acceptance Criteria

- [ ] All 7 database extension packages exist as one package each (gated; verified across `output.phpVersion` 8.2–8.5)
- [ ] Each package has correct `composer.json` with PHP constraint and extension dependency
- [ ] Package dependencies are correctly declared (e.g., `pdo_mysql` depends on `pdo`)
- [ ] All `.tyhpdef` files are generated and pass `tyhp lint`
- [ ] All packages have test files
- [ ] Generic declarations are added where applicable

---

## Phase 8: String & Text Extensions

### Phase Overview

Create individual Composer packages for string and text processing PHP extensions. Follow the package template established in Phase 7. Each extension is one package for all PHP versions; version-specific APIs use Story 20.5 gates and are verified across `output.phpVersion` 8.2–8.5.

### Extensions Included

| Package | Extension | Composer Dependencies | Has Scalar Extensions |
|---|---|---|---|
| `tyhp/php-ext-mbstring` | mbstring | base only | No |
| `tyhp/php-ext-intl` | intl | base only | Yes (string) |
| `tyhp/php-ext-iconv` | iconv | base only | No |
| `tyhp/php-ext-gettext` | gettext | base only | No |

### Implementation Details

**For each extension:** Follow the same steps as Phase 7 — generate tyhpdef into one `php-ext-*` package, apply Story 20.5 gates where needed, enrich from stub corpora (Phase 2.3), apply generic overlays, create tests, lint/verify across targets.

**Extension-Specific Scalar Extension Methods:**

**`tyhp/php-ext-intl` — String extensions:**

File: `runtime/packages/php-ext-intl/_tyhpdef/support/string.tyhp`

```tyhp
<?tyhp

extension IntlStringMethods {
    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn transliterate(extends string $this, string $id, ?string $rules = null, bool $reverse = false): string|false
        => \transliterator_transliterate($id, $this);

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn normalizeUnicode(extends string $this, int $form = \Normalizer::FORM_C): string|false
        => \Normalizer::normalize($this, $form);

    #[\Tyhp\Optimize\Inline]
    #[\Tyhp\Optimize\Pure]
    fn isNormalized(extends string $this, int $form = \Normalizer::FORM_C): bool
        => \Normalizer::isNormalized($this, $form);
}
```

These extension-specific scalar methods are automatically available via the tyhpdef extension auto-inclusion mechanism when the package is installed. They supplement the base package's `StringMethods` extension without conflicting.

**Generic Declarations:**

| Extension | Type | Generic Declaration |
|---|---|---|
| intl | `IntlIterator` | `IntlIterator<TValue = mixed>` implements `\Iterator<int, TValue>` |

### Acceptance Criteria

- [ ] All 4 string & text extension packages exist as one package each (gated; verified across `output.phpVersion` 8.2–8.5)
- [ ] Each package follows the Phase 7 template structure
- [ ] `intl` package includes scalar string extension methods
- [ ] `IntlIterator` has generic declaration
- [ ] All `.tyhpdef` files pass `tyhp lint`
- [ ] All packages have test files

---

## Phase 9: Web & Network Extensions

### Phase Overview

Create individual Composer packages for web and network PHP extensions. Follow the package template established in Phase 7. Each extension is one package for all PHP versions; version-specific APIs use Story 20.5 gates and are verified across `output.phpVersion` 8.2–8.5.

### Extensions Included

| Package | Extension | Composer Dependencies |
|---|---|---|
| `tyhp/php-ext-curl` | curl | base only |
| `tyhp/php-ext-openssl` | openssl | base only |
| `tyhp/php-ext-sockets` | sockets | base only |
| `tyhp/php-ext-ftp` | ftp | base only |

### Implementation Details

**For each extension:** Follow the same steps as Phase 7 — generate tyhpdef into one `php-ext-*` package, apply Story 20.5 gates where needed, enrich from stub corpora (Phase 2.3), apply generic overlays, create tests, lint/verify across targets.

### Acceptance Criteria

- [ ] All 4 web & network extension packages exist as one package each (gated; verified across `output.phpVersion` 8.2–8.5)
- [ ] Each package follows the Phase 7 template structure
- [ ] All `.tyhpdef` files pass `tyhp lint`
- [ ] All packages have test files

---

## Phase 10: Data Format Extensions

### Phase Overview

Create individual Composer packages for data format and markup PHP extensions. Follow the package template established in Phase 7. Each extension is one package for all PHP versions; version-specific APIs use Story 20.5 gates and are verified across `output.phpVersion` 8.2–8.5.

### Extensions Included

| Package | Extension | Composer Dependencies |
|---|---|---|
| `tyhp/php-ext-json` | json | base only |
| `tyhp/php-ext-xml` | xml | base only |
| `tyhp/php-ext-xmlreader` | xmlreader | base only |
| `tyhp/php-ext-xmlwriter` | xmlwriter | base only |
| `tyhp/php-ext-simplexml` | SimpleXML | base only |
| `tyhp/php-ext-dom` | dom | base only |
| `tyhp/php-ext-libxml` | libxml | base only |
| `tyhp/php-ext-csv` | csv | base only |

### Implementation Details

**For each extension:** Follow the same steps as Phase 7 — generate tyhpdef into one `php-ext-*` package, apply Story 20.5 gates where needed, enrich from stub corpora (Phase 2.3), apply generic overlays, create tests, lint/verify across targets.

**Note:** The `csv` extension is new in PHP 8.5. Create a single `tyhp/php-ext-csv` package and gate its contents with `declare(php=">=8.5")` (inactive targets skip silently).

### Acceptance Criteria

- [ ] All 8 data format extension packages exist as one package each (gated; verified across targets)
- [ ] Each package follows the Phase 7 template structure
- [ ] `tyhp/php-ext-csv` exists as one package, gated `>=8.5`, verified across targets
- [ ] All `.tyhpdef` files pass `tyhp lint`
- [ ] All packages have test files

---

## Phase 11: File & System Extensions

### Phase Overview

Create individual Composer packages for file and system PHP extensions. Follow the package template established in Phase 7. Each extension is one package for all PHP versions; version-specific APIs use Story 20.5 gates and are verified across `output.phpVersion` 8.2–8.5.

### Extensions Included

| Package | Extension | Composer Dependencies |
|---|---|---|
| `tyhp/php-ext-fileinfo` | fileinfo | base only |
| `tyhp/php-ext-zip` | zip | base only |
| `tyhp/php-ext-zlib` | zlib | base only |
| `tyhp/php-ext-posix` | posix | base only |
| `tyhp/php-ext-pcntl` | pcntl | base only |

### Implementation Details

**For each extension:** Follow the same steps as Phase 7 — generate tyhpdef into one `php-ext-*` package, apply Story 20.5 gates where needed, enrich from stub corpora (Phase 2.3), apply generic overlays, create tests, lint/verify across targets.

### Acceptance Criteria

- [ ] All 5 file & system extension packages exist as one package each (gated; verified across `output.phpVersion` 8.2–8.5)
- [ ] Each package follows the Phase 7 template structure
- [ ] All `.tyhpdef` files pass `tyhp lint`
- [ ] All packages have test files

---

## Phase 12: Image & Media Extensions

### Phase Overview

Create individual Composer packages for image and media PHP extensions. Follow the package template established in Phase 7. Each extension is one package for all PHP versions; version-specific APIs use Story 20.5 gates and are verified across `output.phpVersion` 8.2–8.5.

### Extensions Included

| Package | Extension | Composer Dependencies |
|---|---|---|
| `tyhp/php-ext-gd` | gd | base only |
| `tyhp/php-ext-imagick` | imagick | base only |
| `tyhp/php-ext-exif` | exif | base only |

### Implementation Details

**For each extension:** Follow the same steps as Phase 7 — generate tyhpdef into one `php-ext-*` package, apply Story 20.5 gates where needed, enrich from stub corpora (Phase 2.3), apply generic overlays, create tests, lint/verify across targets.

### Acceptance Criteria

- [ ] All 3 image & media extension packages exist as one package each (gated; verified across `output.phpVersion` 8.2–8.5)
- [ ] Each package follows the Phase 7 template structure
- [ ] All `.tyhpdef` files pass `tyhp lint`
- [ ] All packages have test files

---

## Phase 13: Caching & Session Extensions

### Phase Overview

Create individual Composer packages for caching and session PHP extensions. Follow the package template established in Phase 7. Each extension is one package for all PHP versions; version-specific APIs use Story 20.5 gates and are verified across `output.phpVersion` 8.2–8.5.

### Extensions Included

| Package | Extension | Composer Dependencies |
|---|---|---|
| `tyhp/php-ext-redis` | redis | base only |
| `tyhp/php-ext-memcached` | memcached | base only |
| `tyhp/php-ext-apcu` | apcu | base only |
| `tyhp/php-ext-session` | session | base only |

### Implementation Details

**For each extension:** Follow the same steps as Phase 7 — generate tyhpdef into one `php-ext-*` package, apply Story 20.5 gates where needed, enrich from stub corpora (Phase 2.3), apply generic overlays, create tests, lint/verify across targets.

### Acceptance Criteria

- [ ] All 4 caching & session extension packages exist as one package each (gated; verified across `output.phpVersion` 8.2–8.5)
- [ ] Each package follows the Phase 7 template structure
- [ ] All `.tyhpdef` files pass `tyhp lint`
- [ ] All packages have test files

---

## Phase 14: Math & Crypto Extensions

### Phase Overview

Create individual Composer packages for math and cryptography PHP extensions. Follow the package template established in Phase 7. Each extension is one package for all PHP versions; version-specific APIs use Story 20.5 gates and are verified across `output.phpVersion` 8.2–8.5.

### Extensions Included

| Package | Extension | Composer Dependencies |
|---|---|---|
| `tyhp/php-ext-bcmath` | bcmath | base only |
| `tyhp/php-ext-gmp` | gmp | base only |
| `tyhp/php-ext-sodium` | sodium | base only |
| `tyhp/php-ext-hash` | hash | base only |
| `tyhp/php-ext-ctype` | ctype | base only |

### Implementation Details

**For each extension:** Follow the same steps as Phase 7 — generate tyhpdef into one `php-ext-*` package, apply Story 20.5 gates where needed, enrich from stub corpora (Phase 2.3), apply generic overlays, create tests, lint/verify across targets.

### Acceptance Criteria

- [ ] All 5 math & crypto extension packages exist as one package each (gated; verified across `output.phpVersion` 8.2–8.5)
- [ ] Each package follows the Phase 7 template structure
- [ ] All `.tyhpdef` files pass `tyhp lint`
- [ ] All packages have test files

---

## Phase 15: Remaining Extensions

> **Note:** "Phase 15" here is an internal phase number within Story 21 (the 15th phase of this plan). It is **unrelated to any story number** — in particular it has nothing to do with the former "Story 15" (documentation & polish), which has been renumbered to **Story 30**.

### Phase Overview

Create individual Composer packages for all remaining PHP extensions not covered in Phases 7–14. Follow the package template established in Phase 7. Each extension is one package for all PHP versions; version-specific APIs use Story 20.5 gates and are verified across `output.phpVersion` 8.2–8.5.

### Extensions Included

| Package | Extension | Composer Dependencies |
|---|---|---|
| `tyhp/php-ext-bz2` | bz2 | base only |
| `tyhp/php-ext-calendar` | calendar | base only |
| `tyhp/php-ext-dba` | dba | base only |
| `tyhp/php-ext-ffi` | FFI | base only |
| `tyhp/php-ext-ldap` | ldap | base only |
| `tyhp/php-ext-mysqlnd` | mysqlnd | base only |
| `tyhp/php-ext-odbc` | odbc | base only |
| `tyhp/php-ext-opcache` | Zend OPcache | base only |
| `tyhp/php-ext-pdo_dblib` | pdo_dblib | `tyhp/php-ext-pdo` |
| `tyhp/php-ext-pdo_odbc` | PDO_ODBC | `tyhp/php-ext-pdo`, `tyhp/php-ext-odbc` |
| `tyhp/php-ext-readline` | readline | base only |
| `tyhp/php-ext-shmop` | shmop | base only |
| `tyhp/php-ext-snmp` | snmp | base only |
| `tyhp/php-ext-soap` | soap | base only |
| `tyhp/php-ext-sysvmsg` | sysvmsg | base only |
| `tyhp/php-ext-sysvsem` | sysvsem | base only |
| `tyhp/php-ext-sysvshm` | sysvshm | base only |
| `tyhp/php-ext-tidy` | tidy | base only |
| `tyhp/php-ext-xsl` | xsl | base only |

**PHP 8.5-introduced extensions** (still named `tyhp/php-ext-*`, not per-minor):

| Package | Extension | Gate |
|---|---|---|
| `tyhp/php-ext-uri` | uri | `declare(php=">=8.5")` (file- or block-level) |
| `tyhp/php-ext-lexbor` | lexbor | `declare(php=">=8.5")` (file- or block-level) |

### Implementation Details

**For each extension:** Follow the same steps as Phase 7 — generate tyhpdef into one `php-ext-*` package, apply Story 20.5 gates where needed, enrich from stub corpora (Phase 2.3), apply generic overlays, create tests, lint/verify across targets.

**PHP 8.5-introduced packages:** The `uri` and `lexbor` extensions are only available in PHP 8.5+. Create `tyhp/php-ext-uri` and `tyhp/php-ext-lexbor` (not `php-8.5-ext-*`) and gate package contents with `declare(php=">=8.5")` so inactive targets skip silently.

### Acceptance Criteria

- [ ] All remaining extension packages exist as one package each (gated; verified across targets)
- [ ] Each package follows the Phase 7 template structure
- [ ] Package dependencies are correctly declared (e.g., `pdo_dblib` depends on `pdo`, `pdo_odbc` depends on `pdo` and `odbc`)
- [ ] PHP 8.5-introduced packages (`uri`, `lexbor`) are named `tyhp/php-ext-*` and gated `>=8.5`
- [ ] All `.tyhpdef` files pass `tyhp lint`
- [ ] All packages have test files

---

## Cross-Story References

| Story | Relationship | Details |
|---|---|---|
| Story 03 | **Extension syntax** | Story 03 implements the extension method syntax (`extends Type $this`, `extension function`, `extension fn`, standalone extension classes) used by Phase 4's scalar extension classes. |
| Story 06 | **Upstream dependency** | Story 06 implements `package.tyhp.json` discovery, `LoadPackageTyhpdefs()`, and `TyhpdefSymbolRegistrar`. Story 21's packages are consumed through this pipeline. Extension auto-inclusion is handled by Story 06 Phase 4's tyhpdef extension auto-inclusion mechanism — the binder stores extension declarations as part of the type's symbol metadata during tyhpdef loading, and includes them in resolution scope automatically when the type is referenced. |
| Story 08 | **Consumer** | The checker uses types loaded from these packages to perform type checking. Generic type parameters flow through the binder into the checker's type system. |
| Story 10 | **Config** | `output.phpVersion` is the evaluation target for Story 20.5 gates in these packages. |
| **Story 20.5** | **Hard dependency (gating)** | Story 20.5 provides `declare(php=…)` and `#[\Tyhp\Php]` so Story 21 can ship a **single** `tyhp/php` (+ `tyhp/php-ext-*`) instead of per-minor forks. Story 21 must not start package authoring until 20.5 binder/checker/emitter gating works. |
| Story 20 | **Tool dependency** | Story 20 implements `tyhp generate_tyhpdef`, which generates the base `.tyhpdef` files from PHP extensions (Reflection / source). Phase 1 and Phase 7 rely on this tool. **Phase 8** (multi-target gated generation) is the preferred way to refresh version diffs into the single tree once available. Story 21 Phase 2.3 then enriches those generated files using community stub annotations (Psalm / PHPStan / Phan / PhpStorm — see `runtime/README.md`), because Reflection alone does not provide templates, refined types, or type-guard metadata. |
| Story 13 | **CLI integration** | Story 13 documents the `tyhp generate_tyhpdef` CLI command used to generate tyhpdefs. |
| Story 28 | **Syntax dependency** | Story 28 implements generic type parameter defaults (`T = DefaultType`). The generic-default grammar/syntax becomes available only after **Story 28 Phase 1**. Phase 3 uses defaults extensively (e.g., `Iterator<TKey = mixed, TValue = mixed>`), so Story 28 (Phase 1 at minimum) is a hard prerequisite and must be complete before this story executes. |
| Story 23 | **Optimizer** | Story 23 implements `#[\Tyhp\Optimize\Inline]` which is used on Phase 4's scalar extension methods to compile `$str->length()` to `\mb_strlen($str)` with zero overhead. |
| Story 24 | **Optimizer** | Story 24 implements `#[\Tyhp\Optimize\Pure]` validation. Phase 2.4 adds purity annotations to PHP built-in functions, and Phase 4 marks pure extension methods. Phase 2.3 stub analysis may inform which APIs are pure, but the emitted form is always `#[\Tyhp\Optimize\Pure]`. |

### Cross-Plan Updates Required

> **Cross-plan update required:** Stories 04 and 07 reference a monorepo `runtime/composer.json` root pattern that this story replaces with standalone Composer packages. When Story 21 is implemented:
> - Story 04's runtime package paths under `runtime/packages/` remain valid for the source layout, but the distribution model changes from monorepo to individual Composer packages.
> - Story 07's test paths referencing `runtime/php-extensions/` should be updated to match Story 21's directory layout (`runtime/packages/php/` and `runtime/packages/php-ext-*/`).
> - No monorepo root `composer.json` is needed — each package is independently publishable.
> - Any plans still describing `tyhp/php-8.x` / `runtime/packages/php-{version}/` should be updated to `tyhp/php` / `runtime/packages/php/` + Story 20.5 gating.

The following plans reference the obsolete monorepo pattern and need updating (as a separate task, outside Story 21):

| Plan | Sections to Update |
|---|---|
| `IMPLEMENTATION_PLAN_TODO_STORY_04.md` | Phase 1 (root monorepo configuration), `runtime/composer.json` creation, path repository entries. Source layout under `runtime/packages/` remains valid, but distribution model changes to standalone Composer packages. |
| `IMPLEMENTATION_PLAN_TODO_STORY_07.md` | PHP test execution (`cd runtime && composer test` → per-package `cd runtime/packages/{pkg} && composer test`), PHPUnit root config. Test paths referencing `runtime/php-extensions/` should be updated to `runtime/packages/php/` and `runtime/packages/php-ext-*/`. |
| `IMPLEMENTATION_PLAN_TODO_STORY_20.5.md` | Already owns gating semantics; Story 21 is listed as consumer — keep package layout references aligned with this plan (`tyhp/php`, `tyhp/php-ext-*`). |

*Last updated: 2026-07-23 — Locked design: single `tyhp/php` + `tyhp/php-ext-*`; version diffs via Story 20.5 (supersedes per-minor `cp -r` forks)*

---

## Human Testing and Verification

> These steps are for a human developer to manually verify the implementation works end-to-end. Steps can be skipped or modified as needed — they are guidelines, not a rigid checklist.

### Step 1: Verify Package Structure Exists

After Phase 1 is complete, verify the directory structure:

```bash
ls -la runtime/packages/php/
ls -la runtime/packages/php/_tyhpdef/
ls runtime/packages/php/_tyhpdef/ | wc -l   # Should be 30 tyhpdef files
ls -la runtime/packages/php/_tyhpdef/support/
ls -la runtime/packages/php/tests/
```

**Expected:**
- `composer.json` exists with `"name": "tyhp/php"`
- `package.tyhp.json` exists with `include` entries for `*.tyhpdef` and `support/*.tyhp`
- 30 `.tyhpdef` files exist using dot-notation naming (e.g., `Ext.Core.tyhpdef`, `Ext.SPL.tyhpdef`)
- `support/` directory exists (initially empty, populated in Phase 4)
- `tests/` directory exists
- The old `runtime/php-extensions/php8.2.9/` directory is removed

### Step 2: Verify Tyhpdef Files Parse Successfully

Run the Tyhp linter on all tyhpdef files:

```bash
tyhp lint runtime/packages/php/_tyhpdef/
```

**Expected:** All 30 `.tyhpdef` files parse without errors. If any parse errors are found, check the auto-generated content for syntax issues (missing semicolons, unsupported type syntax, etc.).

### Step 3: Verify Generic Type Declarations

After Phase 3, open `runtime/packages/php/_tyhpdef/Ext.Core.tyhpdef` and verify:

- `Iterator` has generic parameters: `Iterator<TKey = mixed, TValue = mixed>`
- `Generator` has generic parameters: `Generator<TKey = mixed, TValue = mixed, TSend = mixed, TReturn = mixed>`
- `WeakMap` has generic parameters: `WeakMap<TKey extends object = object, TValue = mixed>`
- Each generic declaration has a `// @generated-original:` comment block above it

Open `runtime/packages/php/_tyhpdef/Ext.SPL.tyhpdef` and verify:

- `SplStack<T = mixed>` extends `SplDoublyLinkedList<T>`
- `SplQueue<T = mixed>` extends `SplDoublyLinkedList<T>`
- `SplPriorityQueue<TValue = mixed, TPriority = int>` has `TPriority` defaulting to `int`
- `ArrayObject<TKey = string|int, TValue = mixed>` has `TKey` defaulting to `string|int`

### Step 4: Verify Generic Types Work in Tyhp Code

Create a test file `test_php_generics.tyhp`:

```tyhp
<?tyhp

// Iterator with explicit type args
function testIterator(\Iterator<string, int> $iter): void {
    string $key = $iter->key();
    int $value = $iter->current();
}

// Iterator with defaults (should be Iterator<mixed, mixed>)
function testIteratorDefault(\Iterator $iter): void {
    mixed $key = $iter->key();
    mixed $value = $iter->current();
}

// SplStack with type arg
function testSplStack(): void {
    \SplStack<int> $stack = new \SplStack();
    $stack->push(42);
    int $top = $stack->top();
}

// WeakMap with typed keys
function testWeakMap(): void {
    \WeakMap<\stdClass, string> $map = new \WeakMap();
    \stdClass $obj = new \stdClass();
    $map[$obj] = "test";
    string $val = $map[$obj];
}

// ArrayObject with defaults
function testArrayObject(): void {
    \ArrayObject<string, int> $ao = new \ArrayObject(["a" => 1, "b" => 2]);
    array<string, int> $copy = $ao->getArrayCopy();
}
```

Run `tyhp lint test_php_generics.tyhp` (ensuring the `tyhp/php` package is discoverable). **Expected:** No type errors. The generic type parameters flow correctly through method return types.

### Step 5: Verify Scalar Extension Methods

After Phase 4, verify the extension class files exist and parse:

```bash
ls runtime/packages/php/_tyhpdef/support/
tyhp lint runtime/packages/php/_tyhpdef/support/
```

**Expected:** Six files exist (`string.tyhp`, `array.tyhp`, `int.tyhp`, `float.tyhp`, `bool.tyhp`, `closure.tyhp`) and all parse without errors.

Create a test file `test_scalar_extensions.tyhp`:

```tyhp
<?tyhp

// String methods
string $s = "Hello, World!";
int $len = $s->length();
bool $has = $s->contains("World");
string $lower = $s->toLower();
string $trimmed = "  spaces  "->trim();
array<int, string> $parts = $s->split(", ");
string $sub = $s->substring(0, 5);
string $replaced = $s->replace("World", "Tyhp");
bool $numeric = "42"->isNumeric();
string $encoded = $s->base64Encode();
string $hashed = $s->md5();

// Array methods
array<string, int> $arr = ["a" => 1, "b" => 2, "c" => 3];
int $count = $arr->count();
bool $empty = $arr->isEmpty();
?int $first = $arr->first();
array<int, string> $keys = $arr->keys();
array<int, int> $vals = $arr->values();
string $json = $arr->toJson();
array<string, int> $filtered = $arr->filtered(fn(int $v): bool => $v > 1);
array<string, string> $mapped = $arr->mapped(fn(int $v): string => (string)$v);
string $joined = $arr->values()->join(", ");

// Int methods
int $n = 42;
int $abs = (-5)->abs();
bool $even = $n->isEven();
string $hex = $n->toHex();

// Float methods
float $f = 3.14159;
int $ceil = $f->ceil();
float $rounded = $f->round(2);
float $sq = $f->sqrt();

// Bool methods
bool $b = true;
string $boolStr = $b->__toString();
bool $negated = $b->not();

// Closure methods
\Closure $double = fn(int $x): int => $x * 2;
\Closure $add1 = fn(int $x): int => $x + 1;
\Closure $composed = $double->then($add1);
```

Run `tyhp lint test_scalar_extensions.tyhp`. **Expected:** No errors. All extension methods are resolved automatically without any import statements.

### Step 6: Verify Compiled PHP Output for Extension Methods

Compile `test_scalar_extensions.tyhp` and inspect the emitted PHP. For inlined methods, verify zero-overhead compilation:

- `$s->length()` should compile to `\mb_strlen($s)` (not a method call)
- `$s->contains("World")` should compile to `\str_contains($s, "World")`
- `$n->isEven()` should compile to `$n % 2 === 0`
- `$f->ceil()` should compile to `(int)\ceil($f)`

Run `php -l <output>.php` to verify the emitted PHP is syntactically valid.

### Step 7: Verify Runtime Behavior of Scalar Extensions

Create a runtime test file `test_scalar_runtime.tyhp`:

```tyhp
<?tyhp

// String
echo "hello"->length() . "\n";
echo "hello"->toUpper() . "\n";
echo "Hello, World!"->contains("World") ? "yes\n" : "no\n";
echo "  trimmed  "->trim() . "\n";
echo "a,b,c"->split(",")->join(" | ") . "\n";

// Array
array<int, int> $nums = [3, 1, 4, 1, 5, 9];
echo $nums->count() . "\n";
echo $nums->sorted()->join(", ") . "\n";
echo $nums->filtered(fn(int $v): bool => $v > 3)->join(", ") . "\n";

// Int
echo 42->isEven() ? "even\n" : "odd\n";
echo (-7)->abs() . "\n";
echo 255->toHex() . "\n";

// Float
echo 3.14159->round(2) . "\n";
echo 2.7->ceil() . "\n";

// Bool
echo true->__toString() . "\n";
echo false->not() ? "negated\n" : "not negated\n";
```

Compile with `tyhp build`, then run with `php <output>.php`. **Expected output:**

```
5
HELLO
yes
trimmed
a | b | c
6
1, 1, 3, 4, 5, 9
4, 5, 9
even
7
ff
3.14
3
true
negated
```

### Step 8: Verify Version Gating Across Minors (Phase 6)

After Phase 6 completes, verify there is still only the single base package and that gates work across targets:

```bash
ls runtime/packages/ | sort
# Expect: php/  (and later php-ext-*/), NOT php-8.2/, php-8.3/, php-8.4/, php-8.5/

grep -E '"name"|"php"' runtime/packages/php/composer.json
# Expect: "name": "tyhp/php" and "php": ">=8.2"
```

Run the lint/build matrix with `output.phpVersion` set to each of 8.2, 8.3, 8.4, 8.5 against `runtime/packages/php/`.

Verify gated additions are present in source and filtered by target:
- `Ext.Json.tyhpdef` contains `json_validate()` behind a `>=8.3` gate
- `Ext.Standard.tyhpdef` contains `array_find()` / friends behind `>=8.4` gates
- `Ext.Standard.tyhpdef` contains `array_first()` / `array_last()` behind `>=8.5` gates
- Under `output.phpVersion=8.2`, those gated symbols are not visible; under higher targets they are

### Step 9: Verify Individual Extension Packages (Phases 7-15)

Spot-check a few individual extension packages:

```bash
ls runtime/packages/php-ext-curl/
ls runtime/packages/php-ext-pdo/
ls runtime/packages/php-ext-gd/
```

**Expected:** Each has `composer.json`, `package.tyhp.json`, `_tyhpdef/`, and `tests/`. The `composer.json` for `pdo_mysql` should declare a dependency on the `pdo` package.

Run `tyhp lint` on a sample of extension packages to verify their tyhpdefs parse correctly.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
