# Implementation Plan: Story 21.1 — Tyhpdef `extern` Types

> **Roadmap position:** Story 21.1 — **Tier 2 — DX & Ecosystem** (additive sub-story). Inserted after Story 21, **implement before Story 21.5.**
> **Direct dependencies (new numbering):** 02 (binder / symbol table), 08 (checker), 14 (diagnostic quality / `--explain`), 20 (Track B `generate_tyhpdef --package-path`), 21 (include vs overlay load, `TYHP8002` / `TYHP8025`)
> **New story:** tyhpdef may declare name-only `extern` placeholders for types this package mentions but does not own. Track B looks up the type in existing `tyhpdef/*` packages: `require` → require that wrapper; `suggest` / `require-dev` → `extern` with `@provided-by: tyhpdef/…`; unknown origin stays unresolved. A real declaration of the same Tyhp name silently wins. Using an `extern` type from `.tyhp` is a compile error.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-08-27
> **Last design lock:** 2026-08-27 — keyword `extern` (not `stub`); name-only `extern class` / `interface` / `enum`; real wins; `.tyhp` cannot use the type; Track B emits `extern` **only** when an existing `tyhp/*` package declares the type and the target’s `composer.json` lists that PHP package in `suggest` / `require-dev` (`@provided-by` is that `tyhpdef/*` name); `require` → require the `tyhpdef/*` wrapper, no `extern`; unknown origin stays unresolved; no package split and no Composer version suffixes
> **Status:** **Design locked.** Ready to implement. This is the next story after 21; do not start 21.5 until 21.1 has landed so `--vendor` generation can emit `extern` instead of broken tyhpdefs.
> **Prerequisites:** Story 02 (`TyhpBinder`, overlay merge); Story 08; Story 14 (short-message style, `--explain`); Story 20 Track B; Story 21 (`extra.tyhp.package` include/overlay, `TyhpdefSymbolRegistrar` / `TYHP8025`).
> **Consumers:** Story 21.5 (`generate_tyhpdef --vendor` must emit `extern` for optional peers); first-party wrappers such as `tyhpdef/monolog-monolog`; any later Composer-library tyhpdef whose public API type-hints `suggest` / `require-dev` / optional extension types.

---

## Table of Contents

- [Summary](#summary)
- [Why 21.1, not 21.6](#why-211-not-216)
- [Motivation](#motivation)
- [What this is not](#what-this-is-not)
- [Scope (In / Out)](#scope-in--out)
- [Decisions (locked)](#decisions-locked)
- [Phase 1: Grammar and lexer](#phase-1-grammar-and-lexer)
- [Phase 2: Visitor and AST](#phase-2-visitor-and-ast)
- [Phase 3: Binder — placeholders and real-wins merge](#phase-3-binder--placeholders-and-real-wins-merge)
- [Phase 4: Checker — use of `extern` from `.tyhp`](#phase-4-checker--use-of-extern-from-tyhp)
- [Phase 5: Diagnostics](#phase-5-diagnostics)
- [Phase 6: Track B generator](#phase-6-track-b-generator)
- [Phase 7: Overlay and include interactions](#phase-7-overlay-and-include-interactions)
- [Phase 8: TextMate, LSP, technical guides](#phase-8-textmate-lsp-technical-guides)
- [Phase 9: User documentation](#phase-9-user-documentation)
- [Phase 10: First-party proving ground (`tyhpdef/monolog-monolog`)](#phase-10-first-party-proving-ground-tyhpmonolog-monolog)
- [Diagnostic Codes](#diagnostic-codes)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

Composer libraries often type-hint classes they do not `require`. Monolog is the example: `ElasticaHandler` lives in `monolog/monolog`, but `\Elastica\Client` lives in `ruflin/elastica`, which is only `suggest` / `require-dev`. The PHP package is right to keep those out of `require`. The tyhpdef wrapper must not pull Elastica (or AWS, Gelf, Predis, …) into every Monolog user’s `vendor/`.

Today the binder treats those names as unresolved (`TYHP3019` / `TYHP3020` / `TYHP3017`), so the **wrapper tyhpdef cannot load** even when the consuming project never touches those handlers.

This story adds a tyhpdef-only `extern` declaration:

```tyhp
<?tyhpdef

// @provided-by: tyhpdef/ruflin-elastica
extern class \Elastica\Document;
extern class \Elastica\Client;

// @provided-by: tyhpdef/php-ext-curl
extern class \CurlHandle;
```

- The name is legal **in tyhpdef signatures** (parameters, returns, properties, union arms).
- The name is **not** a usable type in `.tyhp`. Using it is an error that points at the `extern` declaration and, when present, `@provided-by`.
- A later **real** `class` / `interface` / `enum` of the same Tyhp name (same kind) **silently replaces** the placeholder — including across Composer packages (`TYHP8025` does not fire).
- One `tyhpdef/monolog-monolog` at version `3.10.0`. Users who actually use Elastica install `ruflin/elastica` and `tyhpdef/ruflin-elastica` (when that wrapper exists). No `__gelf` sidecar packages. No `3.10.0-gelf` version suffixes.
- Track B does **not** `extern` every qualified unknown name. It looks up the type in **already-generated** `tyhp/*` packages. First-party wrappers are generated in **require dependency order** so required types exist as real declarations before a consumer is generated.

---

## Why 21.1, not 21.6

Story 21.5’s `generate_tyhpdef --vendor` will generate tyhpdefs for installed Composer libraries. Those libraries have the same optional-peer pattern as Monolog. If `extern` lands **after** 21.5, `--vendor` ships unloadable tyhpdefs and has to be retrofitted.

`extern` depends on binder / checker / Track B / overlay load (21). It does **not** depend on Composer install glue (21.5).

Implement **21.1 next**, then 21.5 consumes it.

---

## Motivation

`runtime/packages/monolog-monolog/3.10.0/_tyhpdef/monolog.monolog.tyhpdef` fails to load from the package test `tyhp.json` with a long list of unresolved names. They are not one problem:

| Bucket | Examples | This story |
|--------|----------|------------|
| Optional Composer peers | `\Elastica\Document`, `\Gelf\Message`, … when `tyhpdef/ruflin-elastica` (etc.) already exists and the PHP package is only `suggest` / `require-dev` | **`extern`** with `@provided-by: tyhpdef/…` |
| Optional PHP extensions | `\CurlHandle` when `tyhpdef/php-ext-curl` exists and `ext-curl` is not in `require` | same; `@provided-by: tyhpdef/php-ext-curl` |
| Required PHP / ext packages | `\Psr\Log\LoggerInterface` via `psr/log` in `require`; types in `tyhpdef/php` | **No `extern`.** Wrapper `require`s that `tyhpdef/*` package. Generate those wrappers **first**. |
| Unknown origin | qualified name not declared by any existing `tyhpdef/*` package | **Leave unresolved** (`TYHP3019` / `3020`). Human finds the defining package, adds/generates that `tyhpdef/*` wrapper, then regenerates — or hand-writes an `extern` that regen will not overwrite. |
| Intra-package generator bugs | unqualified `LogRecord`, `InputOptions`, `Options` | **Do not extern.** Qualify or fix Track B so `TYHP3019` still catches typos |
| Test-only surface | `Monolog\Test\MonologTestCase extends \PHPUnit\Framework\TestCase` | **Omit** from published tyhpdef (`autoload-dev`). Do not extern PHPUnit so a library type can `extends` it |

A hollow overlay `class \Elastica\Document {}` would silence `TYHP3019` and then lie: it is a complete empty type, user code type-checks without Elastica, emit can mention a class that autoload-fails, and a later `tyhpdef/ruflin-elastica` hits `TYHP8002` / `TYHP8025` instead of upgrading. Overlay `partial` cannot introduce a missing type (`TYHP8019`). `extern` exists so the placeholder is **not** a usable type and **does** upgrade.

---

## What this is not

Rejected in `DECISIONS.md` (optional-peer tyhpdefs). Do not re-open in this story:

- Split `tyhpdef/monolog-monolog__gelf` (and one package per backer).
- Composer version suffixes (`3.10.0-base`, `3.10.0-gelf`). Composer installs one version; `3.10.0-gelf` is a prerelease of `3.10.0`.
- Implicit “every unresolved tyhpdef name becomes a placeholder” (typos become silent `extern`).
- Keyword `stub` (Layer 2 overlay harvest already owns that word).
- Rewriting foreign types to `object` / `mixed`.
- `omit` of every optional-backed handler class.
- Requiring `tyhpdef/ruflin-elastica` from `tyhpdef/monolog-monolog` (that Composer-requires the PHP package and installs Elastica for everyone).
- TypeScript-style `skipLibCheck` for tyhpdefs.

---

## Scope (In / Out)

| In scope | Out of scope |
|----------|----------------|
| Tyhpdef-only contextual keyword `extern` immediately before `class` / `interface` / `enum` | `extern` in `.tyhp` source (lexes as `T_STRING`; parse error is enough) |
| Name-only form: `extern class \Foo\Bar;` (semicolon, no body, no members, no generics, no `extends` / `implements`, no `abstract` / `final`, no `as` alias) | `extern trait`, `extern function`, `extern const`, `extern struct`, empty `{ }` bodies |
| Optional `// @provided-by: tyhpdef/<vendor>-<name>` (or `tyhpdef/php-ext-*`) immediately above the declaration | A second grammar clause (`provided by "…"`); `#[\Tyhp\Extern]`; pointing `@provided-by` at the **PHP** package (`ruflin/elastica`) instead of the wrapper |
| Binder: `IsExtern` on the type symbol; tyhpdef signatures may name it | Making `ElasticaHandler` itself extern; mentioning that class in `.tyhp` is fine |
| Real declaration of the same FQCN + kind silently replaces `extern` (include and overlay, either order) | Changing overlay `partial` / `omit` rules except “partial onto `extern` is illegal” |
| `.tyhp` use of an `extern` type is an error (type annotations, `new`, `instanceof`, `catch`, inferred expression types, generic arguments, `use` imports of that FQCN) | Allowing values of `extern` type to flow if the user never writes the name |
| Track B `--package-path`: catalog lookup against existing `tyhpdef/*` packages; `require` → wrapper `require` (no extern); `suggest` / `require-dev` → `extern` + `@provided-by`; unknown origin left unresolved; skip `autoload-dev`; omit generated types that `extends` / `implements` an extern | Auto-`extern` every qualified name; namespace heuristics without a catalog hit; Track A unless a test proves need; Track C `extern` is **Story 21.10** (`extra.tyhp.require` vs author-only `require-dev`) |
| First-party acceptance: generator catalog behavior + compiler fixtures; Monolog 3.10.0 uses generated `extern` where the catalog hits and hand `extern` / unresolved for the rest | Publishing every suggest-peer wrapper (`tyhpdef/ruflin-elastica`, …) in this story |
| Docs, `--explain`, TextMate `extern`, Binder/Checker/Grammar `technical-guide.md` | Composer plugin that auto-installs `tyhpdef/*` companions (21.5 / later) |

---

## Decisions (locked)

| Topic | Decision |
|-------|----------|
| Keyword | **`extern`**. Not `stub`, `opaque`, `declare`, or `foreign`. Contextual in tyhpdef only, same dual-rule pattern as `partial` (`Grammar/technical-guide.md` §4). Keyword when immediately followed by `class` / `interface` / `enum`. |
| Form | Name-only, semicolon terminated. `extern class \Elastica\Document;` |
| Kinds | `class`, `interface`, `enum` only. Traits are not types. |
| Where it may appear | Baseline / `include` **and** overlay. Unlike `omit`, not overlay-only. Generator-owned Layer 1 is the normal home. |
| Members | None. A body, property, method, or `use` inside `extern` is an error. |
| Hierarchy on the placeholder | No `extends`, `implements`, backing type on `enum`, generics, `abstract` / `final`, `readonly`, `as` alias. |
| Combine with other tyhpdef modifiers | Cannot combine with `partial`, `omit`, `deprecated`, `obsolete` on the **same** declaration (extend `TYHP8018`). |
| Tyhpdef signatures | Parameters, returns, properties, and union/intersection arms **may** name an `extern` type. That does **not** make the owning Monolog class extern. |
| Tyhpdef `extends` / `implements` | A **real** tyhpdef type must **not** extend or implement an `extern` type. Omit that generated type instead (test bases, foreign interfaces). |
| `.tyhp` “used” | Any type position or expression whose type is `extern` (including union arms), plus `new`, `instanceof`, `catch`, and importing the FQCN. Referencing a **real** class whose *signature* mentions extern (e.g. `ElasticaHandler`) is **not** by itself a use. Calling a member whose return/parameter type is extern **is** a use at that call/argument site. |
| Real wins | Real `class` / `interface` / `enum` of the same FQCN and kind replaces `extern`, either order, same package or another. No `TYHP8002` / `TYHP8025`. Silent. |
| Extern after real | No-op. Do not weaken. No diagnostic. |
| Two externs, same kind | Same symbol. Keep the first `@provided-by` if they differ. |
| Kind mismatch | `extern class` vs real `interface` (or two externs of different kinds) is an error. |
| `partial` onto extern | Illegal (would add members to a placeholder). Full overlay replace with a real type is the upgrade path. |
| `omit` of extern | Allowed in overlay; the name is gone; remaining mentions become unresolved (`TYHP3019` / `3020`) as today. |
| `@provided-by` | Required on **generator-owned** `extern` (catalog always knows the wrapper). Value is the **Tyhp wrapper** name: `tyhpdef/ruflin-elastica`, `tyhpdef/php-ext-curl`. That wrapper already `require`s the PHP package / corresponds to `ext-*`. Hand-written `extern` may omit the comment. Diagnostic help cites this name as-is (install / include that package). |
| Unqualified names | **Never** auto-`extern`. `LogRecord` / `InputOptions` stay unresolved so generator bugs stay visible. |
| Unknown origin | Qualified name **not** declared (as a **real** type, not `extern`) by any existing `tyhp/*` package → **do not auto-`extern`**. Leave the signature as generated; bind reports `TYHP3019` / `3020`. Human locates the defining package, generates that wrapper (dependency order), regenerates the consumer — or adds a hand `extern` in a file regen does not overwrite. |
| Catalog | FQCN → `tyhpdef/*` package that **really** declares it. Do not treat another package’s `extern` as providing the type. |
| Target `composer.json` | Use the **PHP** package being wrapped (`monolog/monolog`), not the wrapper’s own `composer.json`, to decide `require` vs `suggest` / `require-dev` (and `ext-*`). |
| `require` + catalog hit | No `extern`. Ensure the wrapper `require`s that `tyhpdef/*` package (add the Composer `require` if missing). Generate required wrappers **before** the consumer. |
| `suggest` or `require-dev` + catalog hit | `extern` + `@provided-by: tyhpdef/…`. Do **not** `require` that wrapper (would pull the PHP package for everyone). |
| Catalog hit but PHP package / `ext-*` is in none of `require` / `suggest` / `require-dev` | Treat as unknown origin: no auto-`extern` (the target did not declare the relationship; keep `TYHP3019` so a human notices). |
| Track C | **Superseded by Story 21.10.** 21.1 shipped “never emit `extern` from compiled Tyhp.” 21.10 classifies names via `extra.tyhp.require` (ambient FQN) vs author-only `require-dev` (`extern` + `@provided-by`). |
| Hollow `class Foo {}` | Unchanged: still a real empty type. Docs warn this is the wrong tool for optional peers. |

### Merge table (include + overlay + cross-package)

| Already in the table | Incoming | Result |
|----------------------|----------|--------|
| nothing | `extern class Foo` | placeholder |
| `extern class Foo` | `extern class Foo` | same symbol |
| `extern class Foo` | real `class Foo` | **real wins**, silent |
| real `class Foo` | `extern class Foo` | **no-op** |
| `extern class Foo` | real `interface Foo` | **error** (kind mismatch) |
| `extern class Foo` | `extern interface Foo` | **error** (kind mismatch) |
| real `class Foo` | real `class Foo` | existing `TYHP8002` / `TYHP8025` |
| `extern class Foo` | overlay `partial class Foo` | **error** (partial onto extern) |
| `extern class Foo` | overlay `omit class Foo` | name removed |
| `extern class Foo` | overlay full `class Foo { … }` | **real wins** (full replace) |

Unions: each arm resolves independently. `\Predis\Client|\Redis` can be `extern|real` until Predis’s tyhpdef is loaded; only the Predis arm upgrades.

---

## Phase 1: Grammar and lexer

### Lexer (`TyhpLexer.g4`)

Add the same `_AS_T_STRING` / `T_TYHPDEF_EXTERN` pair as `partial`:

- Only when `_languageMode == "tyhpdef"`.
- `T_TYHPDEF_EXTERN` when the next token is `class` / `interface` / `enum` (case insensitive, word boundary), with the same `prepareLess` / `streamLA` / `doPreparedLess` pattern.
- Otherwise `extern` is `T_STRING` (so a method named `extern` still parses).

Do **not** treat `trait` as a follower. Do **not** enable the keyword in `.tyhp` mode.

### Parser (`TyhpParser.g4`)

Do **not** reuse `tyhpdefImportClassDeclarationStatement`’s `{ body }` form.

Add bodyless alternatives, for example:

```
tyhpdefExternClassDeclaration
    : tyhpdefDeprecatedOrObsolete? T_TYHPDEF_EXTERN T_CLASS
        Identifier=tyhpdefClassNameWithOptionalAlias T_SYM_SEMICOLON
    ;
```

and the same for `interface` and `enum` (enum has **no** backing type and **no** `implements`).

Wire them into `tyhpdefAttributedStatement` / top-statement.

Parser must **reject** (not recover into a class-with-body):

- `extern class Foo { }` and any members
- `extern class Foo extends Bar;`
- `extern abstract class Foo;`
- `extern partial class Foo;`
- `extern class Foo as Bar;`
- `extern class Foo<T>;`

`tyhpdefDeprecatedOrObsolete` currently is `deprecated` | `obsolete` | `omit`. Combining those with `extern` is a **semantic** `TYHP8018` (Phase 3), not a parse success. Prefer: if the grammar allows `deprecated extern class Foo;`, binder reports `TYHP8018`. Cleaner: grammar forbids `tyhpdefDeprecatedOrObsolete` on extern rules so only binder-less parse failure happens — **lock: grammar forbids** `deprecated` / `obsolete` / `omit` / `partial` on the same declaration as `extern` (no `tyhpdefDeprecatedOrObsolete?` on the extern rules; `partial` is not on those rules at all).

Regenerate the parser. Update `Grammar/technical-guide.md` §4 with `extern` beside `partial`.

---

## Phase 2: Visitor and AST

Build a type declaration AST that the binder already understands (`ObjectDeclarationSymbol` / class-like), with:

- `IsExtern = true`
- Empty member list
- No `Extends` / `Implements`
- Optional `@provided-by` parsed from the immediately preceding `// @provided-by:` comment (trim; one package name; ignore unknown `@` tags)

Do not invent a second AST family if flagging the existing class/interface/enum node is enough.

`as` on `tyhpdefClassNameWithOptionalAlias`: if the grammar still allows an alias on the identifier rule, visitor/binder must error (`TYHP8028` family) rather than register two names.

---

## Phase 3: Binder — placeholders and real-wins merge

### Symbol flag

`IsExtern` on the object/type symbol (class, interface, enum). Treat as a **bound** name: `TYHP3019` / `TYHP3020` / `TYHP3021` / `TYHP3022` do **not** fire for that FQCN.

### Registration (`TyhpdefSymbolRegistrar` + include `TYHP8002`)

When the incoming symbol and the existing symbol share an FQCN:

1. Same kind, existing extern, incoming real → **replace** with real; drop `IsExtern`; do not report `8002` / `8025`.
2. Same kind, existing real, incoming extern → **keep real**; do not register a second symbol.
3. Same kind, both extern → keep existing.
4. Same kind, both real → existing duplicate diagnostics.
5. Different kind, and either side is extern → new kind-mismatch diagnostic (Phase 5).

Replacement must be **order-independent** across packages. Loading Elastica’s tyhpdef before or after Monolog’s `extern` yields the real type.

### Tyhpdef `extends` / `implements`

If the target type `IsExtern`, report the new binder diagnostic (do not wait for checker). This is the `MonologTestCase extends TestCase` case: the generator must omit that class so this diagnostic does not fire on published wrappers.

### Overlay

- Full overlay real type onto extern → real wins (already last-wins replace; ensure `IsExtern` is cleared).
- Overlay extern onto real → no-op (Phase 7).
- Overlay `partial` whose target is extern → error; skip the partial.
- `omit` of extern → existing omit path.

`TryHandleTyhpdefOverlayKeywords`: `extern` is not a fourth flag in that combo counter if the grammar already forbids stacking; still reject if a visitor path can set both.

---

## Phase 4: Checker — use of `extern` from `.tyhp`

After bind, a type is “extern” when the named type symbol has `IsExtern`.

**In `.tyhp` files** (not `.tyhpdef`):

Error when:

- A type annotation (parameter, return, property, catch, catch union arm, generic argument) names an extern type, including as an arm of `|` / `&`
- `new ExternType`, `instanceof ExternType`
- `use` / `use` group that imports that FQCN
- An expression’s inferred type is extern (calling `getDocument(): \Elastica\Document`, passing an argument to a parameter whose type is extern)

**Not** an error:

- Naming `Monolog\Handler\ElasticaHandler` (real)
- Loading a tyhpdef that mentions extern in signatures
- `.tyhpdef` parameter/return/property types that are extern

**Call / construct sites:** `new ElasticaHandler($client)` checks the argument against `\Elastica\Client`. If that parameter type is extern, the call is a use — error at the argument (and/or the callee type argument), not a mysterious `TYHP4010` against an empty class.

Do not emit PHP for a compilation that still has this error (normal error pipeline). Defense in depth: emitter seeing `IsExtern` in a `.tyhp` type position is an internal error, not silent erase.

`obsolete` already errors on use; `extern` is similar but the remediation is “include the providing tyhpdef,” not “do not use this API.” Do not reuse `TYHP4501`.

---

## Phase 5: Diagnostics

Add enum values in `MessageCode.cs` and matching `ERROR_TYHP*` (and `--explain` long form) in both `.resx` files. Follow Story 14 short-message rules. **Proposed** allocations (adjust if taken; do not reuse 4326–4399):

| Enum (proposed) | Band | When |
|-----------------|------|------|
| `CheckerExternTypeUsed` (~4307) | Checker feature band (gap after 20.5’s 4306) | `.tyhp` used an `extern` type |
| `BinderExternExtendsOrImplements` (~3026) | Binder | tyhpdef (or `.tyhp`) `extends` / `implements` an `extern` type |
| `TyhpdefExternIllegalDeclaration` (~8028) | Tyhpdef | body, members, generics, `extends` on the placeholder, `as` alias, `extern trait` if it somehow parses |
| `TyhpdefExternKindMismatch` (~8029) | Tyhpdef | extern vs real (or extern vs extern) kind conflict |
| `TyhpdefPartialOnExtern` (~8030) | Tyhpdef | overlay/include `partial` targeting an extern type |

`TYHP8018` message today lists `partial` / `omit` / `deprecated` / `obsolete`. If any stack with `extern` can still reach the binder, **update that message** to include `extern`.

Short messages (draft; lock in `.resx` at implement time):

- `Type `{0}` is extern`
- `Extends type `{0}` is extern`
- `Implements type `{0}` is extern`

Story 14 labels: secondary span on the `extern` declaration (“declared here”). Help / note: cite `@provided-by` as-is (`tyhpdef/ruflin-elastica`, `tyhpdef/php-ext-curl`). Do not stuff remediation into the short message.

`TYHP3019` / `3020` remain for names that are **neither** real **nor** `extern` (typos, unqualified PHPDoc aliases, unknown-origin types the generator correctly refused to guess).

---

## Phase 6: Track B generator

`--package-path` (and `--source` when it is a package-shaped tree) after emitting this package’s own types.

First-party `tyhpdef/*` wrappers **must be generated in dependency order** along the target’s Composer **`require`** graph (and `ext-*` in `require`). `psr/log` → `tyhpdef/psr-log` before `monolog/monolog` → `tyhpdef/monolog-monolog`. `dev-docs/PROPOSED_TYHPDEF_PACKAGES.md` already ranks public-API prerequisites the same way. If a required type has no wrapper yet, leave it unresolved and **warn** on the generate CLI (“no `tyhpdef/*` package declares `\Psr\Log\LoggerInterface`; generate that wrapper first”) — do **not** `extern` a required dependency.

### Catalog

Before classifying foreign names, build **FQCN → providing `tyhpdef/*` package** from wrappers that already exist:

- In-repo: `runtime/packages/*` (each `extra.tyhp.package` include/overlay tree).
- Later `--vendor` (21.5): installed `vendor/tyhpdef/*/composer.json` (`extra.tyhp.package`).

Index only **real** `class` / `interface` / `enum` declarations. Skip `extern` placeholders so Monolog’s own `extern class \Elastica\Document` does not make Monolog look like the provider.

Map each `tyhpdef/*` package to the PHP package (or `ext-*`) it wraps: that package’s `composer.json` `require` of `vendor/name` / `ext-*` (e.g. `tyhpdef/ruflin-elastica` → `ruflin/elastica`, `tyhpdef/php-ext-curl` → `ext-curl`, `tyhpdef/php` → always-present builtins).

### Classify each foreign type

Walk every generated signature type (params, returns, properties, `extends`, `implements`). Resolve names **inside this package’s generated set** (qualify relative names to the current namespace — **fix intra-package `LogRecord` here**).

Then for each remaining name:

| Condition | Action |
|-----------|--------|
| Unqualified | Leave as written. Never auto-`extern`. |
| Catalog miss (unknown origin) | Leave as written. Bind will report `TYHP3019` / `3020`. No guess from namespace vs `suggest` names. |
| Catalog hit, providing PHP package / `ext-*` is in the **target** `require` | No `extern`. Add/keep `require` of that `tyhpdef/*` on the **wrapper** `composer.json`. |
| Catalog hit, providing PHP package / `ext-*` is in the target `suggest` or `require-dev` (and not in `require`) | Emit `extern` with `// @provided-by: tyhpdef/…`. Do not add a wrapper `require`. |
| Catalog hit, providing package listed in none of those three | Leave unresolved (undeclared relationship). |
| Generated type `extends` / `implements` a name that would be `extern` | **Do not emit** that generated type (warn). Do not extern PHPUnit just to keep `Monolog\Test\MonologTestCase`. |

Default file set stays `autoload` only. Do **not** turn on `--include-dev` for published wrappers.

Do not call Packagist from Track B in this story. Do not invent `tyhpdef/<vendor>-<name>` `@provided-by` lines for packages that are not in the catalog yet.

### Where to write generated `extern`

**Lock: `_tyhpdef/externs.tyhpdef`.** `AUTO-GENERATED` header; regen **overwrites this file only**. `extra.tyhp.package` `"include"` glob `./_tyhpdef/*.tyhpdef` already loads it — no extra include entry.

Hand `extern` for unknown-origin types lives in a **different** include file (e.g. `_tyhpdef/backers.extern.tyhpdef`) or in overlay. Regen must not touch those. Two `extern` of the same kind merge.

Do not put generator `extern` in `"overlay"`.

### Track C / Track A

Track C: **21.1 shipped never-emit `extern`.** Story **21.10** reverses that for author-only `require-dev` owners (`extra.tyhp.require` stays ambient). Track A: no change unless a test proves an extension tyhpdef needs it; do not block this story on that.

---

## Phase 7: Overlay and include interactions

Update the overlay merge docs and `TyhpBinder.Overlays.cs` as in the merge table.

`// @overlay-against:` does not apply to `extern` (there is no Layer 1 member list). Stamping an extern is a no-op.

Hand-written **include** files (e.g. `_tyhpdef/backers.extern.tyhpdef`) or overlay may **add** extra `extern` names the catalog could not prove. Regen must not overwrite those files. Do **not** use a hollow `class \Foreign\Type {}` as a fake API.

`tyhp overlay create` on an extern type: do not copy it into a hollow `class { }` overlay. Either refuse with a clear error or copy `extern class Foo;` as-is. **Lock: refuse** (`create` is for replacing a real declaration).

---

## Phase 8: TextMate, LSP, technical guides

- `tyhp-lang/vscode/syntaxes/tyhp.tmLanguage.json`: highlight `extern` as a tyhpdef modifier (same bucket as `deprecated` / `obsolete`).
- LSP: no new protocol. Hover/go-to on an extern name should land on the `extern` declaration; the new diagnostic is enough for v1.
- Update `Tyhp/TyhpLang/Grammar/technical-guide.md`, `Binder/technical-guide.md`, `Checker/technical-guide.md`, and the generator area guide (Track B emit of `externs.tyhpdef`). Current behavior only.

---

## Phase 9: User documentation

New page `docs/content/tyhpdef_extern.md` (link from `docs/content/toc.json` near overlays / deprecated). Cover:

- Why optional Composer peers exist
- Syntax, `@provided-by: tyhpdef/…` (the wrapper, not the PHP package)
- Real-wins when that wrapper is included
- What counts as a `.tyhp` use
- Do not write hollow `class \Foreign\Type {}` overlays
- Generator writes `_tyhpdef/externs.tyhpdef` only for catalog-proven `suggest` / `require-dev` types; unknown names stay unresolved until a human adds a wrapper or a hand `extern`

Update: `tyhpdef_overlays.md` (partial cannot target extern; omit may), `tyhpdef_about.md` / `quickref_tyhpdef.md` / `faq_tyhpdefSyntax.md` as needed, `cli_tyhpdefGeneration.md` (Track B emits `extern`), `diagnostics_reference.md` via the usual code registry.

Reader-relevant rules: describe the **current** contract. Do not document rejected splits except if a FAQ must correct a misconception; prefer not mentioning them.

---

## Phase 10: First-party proving ground (`tyhpdef/monolog-monolog`)

Regen **`runtime/packages/monolog-monolog/3.10.0`** with the new Track B rules.

The baseline tyhpdef still **names** `\Elastica\Document` in signatures even when tests never call `ElasticaHandler`. Those names must be `extern` (generated or hand) or bind keeps reporting `TYHP3019` / `3020`. Core tests do not need Elastica **installed**; they do need the names resolved as `extern` or as a real wrapper.

Expectations:

- `tyhpdef/psr-log` is already required (catalog + `psr/log` in Monolog `require`). Do not `extern` `\Psr\Log\LoggerInterface`.
- Optional **extensions** that already have `tyhpdef/php-ext-*` and appear in Monolog `suggest` / `require-dev` (e.g. `\CurlHandle` → `tyhpdef/php-ext-curl`) get generated `extern` in `_tyhpdef/externs.tyhpdef` with `@provided-by: tyhpdef/php-ext-curl`. Do **not** add those ext wrappers to `tyhpdef/monolog-monolog` `require`.
- Optional **Composer** peers with **no** `tyhpdef/*` wrapper yet (`\Elastica\Document`, `\Gelf\Message`, …) stay **unresolved** in generated output. That is correct. Either generate those wrappers later (then regen Monolog) or keep a **hand** `extern` file regen does not overwrite (e.g. `_tyhpdef/backers.extern.tyhpdef`).
- `Monolog\Test\` is **not** in the published tyhpdef (`autoload-dev` / extends PHPUnit).
- Intra-package names like `LogRecord` are qualified; they are not in `externs.tyhpdef`.
- Delete the experimental `overlays/backerStubs.tyhpdef` hollow/`partial` workaround.

Other monolog versions may wait for a bulk regen; 3.10.0 is the pin. Do not invent `__gelf` packages or version suffixes. Do not create every suggest-peer wrapper in this story.

Compiler tests (not only the runtime package) must prove: catalog + suggest → `extern` + `@provided-by`; catalog + require → wrapper `require`, no `extern`; catalog miss → no `extern`; `.tyhp` use of extern errors; a second tyhpdef with a real `class` upgrades.

---

## Diagnostic Codes

Allocate in `MessageCode.cs` only. Proposed names/numbers are in Phase 5. Do not reuse `TYHP3019` for “resolved but extern.” Do not reuse `TYHP4501` (`obsolete`).

---

## Cross-Story References

| Story | Relationship |
|-------|----------------|
| **02** | Symbol table; this story adds `IsExtern` and changes duplicate registration. |
| **08** | Checker use sites; new 4307-band diagnostic. |
| **14** | Short-message style, labels, `--explain`. |
| **20** | Track B walks signatures and writes Layer 1; this story adds `externs.tyhpdef` emit and intra-package qualification. |
| **21** | Include/overlay load; `TYHP8002` / `TYHP8025` gain a real-wins exception; overlay `partial` cannot target extern. |
| **21.5** | **Depends on this story.** `--vendor` Track B must emit `extern` for optional peers. Do not rewrite foreign types to `object`. |
| **21.10** | Track C `extern` for compiled Tyhp libraries (`extra.tyhp.require` vs author-only `require-dev`); `extern function` / `extern const`. 21.1 Track B rules stay. |
| **30** | Remaining docs polish. |

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). See `CONVENTIONS.md` and Story 07.

- [ ] **Parse:** `extern class` / `interface` / `enum` with FQCN and with namespaced identifier; `extern` as a method name still parses as `T_STRING`
- [ ] **Parse reject:** body, `extends`, `abstract`, `partial extern`, `extern trait`, `extern class Foo<T>`
- [ ] **Bind:** tyhpdef method `function f(\ExternNs\T $x): \ExternNs\T;` does **not** report `TYHP3019` / `3020` when `extern class \ExternNs\T;` is in include
- [ ] **Bind:** tyhpdef `class C extends \ExternNs\T` reports the extends-extern diagnostic
- [ ] **Merge:** extern then real (two include packages) → real; real then extern → real; two reals still `TYHP8002` / `8025`; class vs interface mismatch errors
- [ ] **Checker:** `.tyhp` type-hint / `new` / call that returns extern → `CheckerExternTypeUsed`; mentioning a real class whose ctor takes extern is OK until the call
- [ ] **Union:** only one arm extern → still a use in `.tyhp`; after real package for that arm, that arm is real
- [ ] **Overlay:** `omit` removes extern; `partial` onto extern errors; full class overlay upgrades
- [ ] **Generator:** fixture PHP package + existing `tyhpdef/*` catalog: type from `require` → wrapper `require`, no `extern`; type from `suggest` / `require-dev` → `_tyhpdef/externs.tyhpdef` with `@provided-by: tyhpdef/…`; type in no catalog → **no** `extern`; unqualified alias is **not** externed; a class extending PHPUnit is omitted; regen overwrites `externs.tyhpdef` only
- [ ] **Track C** fixture does not emit `extern` *(21.1 lock; Story 21.10 adds classified Track C `extern`)*
- [ ] **Monolog 3.10.0:** `psr/log` types not externed; catalog-hit optional ext types externed; unknown Composer peers left unresolved unless a hand `extern` file exists; `backerStubs.tyhpdef` gone
- [ ] **Docs + resx + `--explain`** for new codes; TextMate highlights `extern`
- [ ] **Conformance run green** before story done
- [ ] **No** `__gelf` packages, **no** `3.10.0-gelf` versions, **no** `stub` keyword in grammar
