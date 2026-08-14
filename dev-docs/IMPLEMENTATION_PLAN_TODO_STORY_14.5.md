# Implementation Plan: Story 14.5 — PHP 8.5 Syntax Surface + Lowering (`805.0.0`)

> **Roadmap position:** Story 14.5 — **Tier 1 — Usable** (additive sub-story, inserted after Story 14, before Story 15)
> **Direct dependencies (new numbering):** 06 (tyhpdef / builtins), 08 (checker), 09 (basic emitter), 10 (`output.phpVersion`), 11 (feature emit / existing aviz·hooks·`with`/`clone` lowering)
> **New story:** bring Tyhp’s PHP grammar + emit pipeline up through **PHP 8.5**, close remaining **8.4** parse holes vs php-src 8.4.4, and bump the compiler product version to **`805.0.0`** (highest supported PHP = 8.5).
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence. Version encoding is defined in `docs/content/release_planning.md`.

> **Branch:** TBD
> **Generated:** 2026-08-06
> **Story status:** COMPLETE (Phase 7 acceptance 2026-08-07)
> **Prerequisites:** Story 11 property-hook / asymmetric-visibility / `with` emit paths exist; Story 10 supplies `output.phpVersion` + `IsPhpVersionAtLeast`; Story 06 can load ExtCore / language-construct stubs.
> **Consumers:** Story 15 (interop contract — document new lowerings), Story 20.5 / 21 (stubs gated to 8.5 APIs), runtime packages that may use pipe / clone-with / void-cast once available.

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [Scope (In / Out)](#scope-in--out)
- [Decisions (locked)](#decisions-locked)
- [Version bump](#version-bump)
- [Architecture Overview](#architecture-overview)
- [Phase 1: Grammar — close PHP 8.4 holes](#phase-1-grammar--close-php-84-holes)
- [Phase 2: Grammar — PHP 8.5 tokens & productions](#phase-2-grammar--php-85-tokens--productions)
- [Phase 3: tyhpdef — `exit` / `die` / `clone` signatures](#phase-3-tyhpdef--exit--die--clone-signatures)
- [Phase 4: Binder / checker — wire keyword-calls + new exprs](#phase-4-binder--checker--wire-keyword-calls--new-exprs)
- [Phase 5: Emitter — native emit vs lower-target rewrite](#phase-5-emitter--native-emit-vs-lower-target-rewrite)
- [Phase 6: Product version `805.0.0` + docs](#phase-6-product-version-80500--docs)
- [Phase 7: Conformance fixtures & acceptance](#phase-7-conformance-fixtures--acceptance)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

Tyhp’s base PHP grammars already target **php-src 8.4.4** and cover most 8.3–8.4 syntax. This story:

1. Fixes **incomplete 8.4** parse coverage (abstract/interface property-hook `;`, attributes on hooks, full `exit`/`die` call forms).
2. Adds **PHP 8.5** syntax: pipe `|>`, `(void)` cast, `clone(…)` / clone-with, attributes on top-level `const`.
3. Declares **`exit` / `die` / `clone`** as tyhpdef functions for signatures / named args / FCC checking, while **keeping keyword parse forms in the grammar** (locked decision below).
4. **Rewrites** new syntax for `output.phpVersion` below the introducing minor (same pattern as property-hook polyfill and `WithKeywordHelper` PHP 8.5 `clone()`).
5. Bumps Tyhp to **`805.0.0`**.

---

## Motivation

Users writing modern PHP (and Tyhp-as-superset) expect 8.5 constructs to parse and either emit natively or lower cleanly. The grammar audit found:

| Gap | Introduced |
|-----|------------|
| Abstract/interface hook body `;` | 8.4 (incomplete vs claimed 8.4.4 base) |
| Attributes on property hooks | 8.4 |
| `exit`/`die` only accept optional bare `expr`, not full arg list / `...` / named args | 8.4 |
| Pipe `\|\>` | 8.5 |
| `(void)` cast | 8.5 |
| `clone($obj, $withProperties)` / FCC `clone(...)` | 8.5 |
| Attributes on compile-time non-class `const` | 8.5 |

Shipping these with version-aware emit makes Tyhp’s MAJOR encoding (`805` = PHP 8.5) honest.

---

## Scope (In / Out)

| In scope | Out of scope |
|----------|--------------|
| Lexer/parser for all items in the priority list from the 8.3–8.5 audit | New PHP *library* APIs (`array_first`, URI ext, lazy-object Reflection, …) — Story 21 / stubs |
| tyhpdef stubs for `exit` / `die` / `clone` | Moving keyword *lexing* to `T_STRING` |
| Checker typing for pipe, void cast, clone-with, exit call forms | Semantic-only 8.5 items already allowed by grammar (casts/closures in const exprs, static aviz, `final` promotion) beyond smoke tests |
| Emitter: native ≥ introducing version; rewrite/polyfill for lower targets | Changing Tyhp `with` surface (already emits 8.5 `clone()` when targeting 8.5 — integrate, don’t redesign) |
| Version bump `804.4.1` → `805.0.0` + release/docs touch-ups | Story 20.5 gating syntax; Marketplace / packaging |

---

## Decisions (locked)

### 1. `exit` / `die` — hybrid (grammar + tyhpdef), **not** tyhpdef-only

**Question:** Keep bare `exit;` in grammar and put “function call syntax” only in tyhpdef?

**Answer: No — tyhpdef alone cannot parse call forms.**

- The lexer emits **`T_EXIT`** (not `T_STRING`). `name` / normal call paths only accept string/qualified names, so `exit(0)` never becomes a tyhpdef function call without a **keyword production**.
- PHP itself keeps `T_EXIT` + `ctor_arguments`; Tyhp should mirror that.

**Locked model:**

| Form | Where |
|------|--------|
| Bare `exit;` / `die;` | **Grammar only** (`T_EXIT` with no args) |
| Call-like `exit(…)`, `die(…)`, named args, `exit(...)` FCC | **Grammar** as `T_EXIT` + full `argumentList?` (same shape as php-src `ctor_arguments`) |
| Signature, types, named-parameter checking, callable identity | **tyhpdef** (`function exit(string\|int $status = 0): never`, same for `die`) |
| Emit | Pass through when legal; for targets that need it, keep PHP-compatible keyword/call spelling (both are valid PHP ≥ 8.4 for call forms; bare form always OK) |

Binder/checker: resolve `#phpExprExit` (and FCC uses) against the tyhpdef symbols rather than inventing an ad-hoc checker special-case for every arity.

### 2. `clone` — hybrid; bare unary stays in grammar

**Question:** Keep `clone $x` in grammar; put function-call / clone-with only in tyhpdef?

**Answer: Same constraint — `T_CLONE` is reserved.** Call forms need grammar; tyhpdef owns the signature.

**Locked model:**

| Form | Where |
|------|--------|
| Unary `clone $x` | **Grammar** (`#phpExprClone` / unary) — keep |
| `clone($x)`, `clone($x, […])`, `clone(...)` FCC | **Grammar** via `T_CLONE` + argument list (php-src `clone_argument_list` semantics: multi-arg / trailing-comma forms that disambiguate from parenthesized unary) |
| Signature | **tyhpdef** `function clone(object $object, array $withProperties = []): object` |
| Emit ≥ 8.5 | Native `clone(…)` / unary as written |
| Emit &lt; 8.5 | Rewrite clone-with to existing Story 11 paths (`ObjectHelper::with`, readonly IIFE, assignment-after-clone); unary `clone $x` unchanged; reject or rewrite FCC `clone(...)` if unrepresentable on older PHP |

Align with `WithKeywordHelper.BuildNativeCloneCall` (already emits call-shaped `clone` for PHP ≥ 8.5).

### 3. Pipe, `(void)`, const attributes, hook fixes — grammar-first

These are true new tokens / productions (or missing 8.4 alternatives). No tyhpdef substitute.

### 4. Lowering policy

| Feature | Native when | Lower target behavior |
|---------|-------------|------------------------|
| Property hooks / aviz | ≥ 8.4 | Existing polyfill (Story 11) — only fix parse holes here |
| `exit`/`die` call + named / FCC | ≥ 8.4 semantics | Emit call form; on &lt; 8.4 prefer positional `exit($status)` / bare; FCC `exit(...)` → `\Closure::fromCallable('exit')` or equivalent documented lowering |
| Pipe `\|\>` | ≥ 8.5 | Nested call chain / temps (left-to-right), matching PHP pipe semantics (single-arg callables) |
| `(void) expr` | ≥ 8.5 | Omit cast / emit `expr;` as discarded statement (no runtime effect); suppress NoDiscard-style warnings only when attribute exists |
| `clone($o, $props)` | ≥ 8.5 | Rewrite via Story 11 clone/`with` helpers |
| Attributes on top-level `const` | ≥ 8.5 | Strip or keep attributes per existing attribute emit rules; on &lt; 8.5 strip with diagnostic if attribute would be required at runtime |

### 5. Version

Compiler `<Version>` becomes **`805.0.0`**. MAJOR `805` = highest supported PHP minor 8.5 (`docs/content/release_planning.md`).

---

## Version bump

| Item | Action |
|------|--------|
| `tyhp.csproj` `<Version>` | `804.4.1` → `805.0.0` |
| Docs / examples citing `804.4.1` | Update representative samples (`docs/content/intro_installation.md`, lint JSON examples, etc.) |
| Grammar headers | Cite php-src lineage through **8.5.x** after ports |
| `output.phpVersion` | Continue supporting 8.2–8.5; default policy unchanged (Story 20.5 owns missing-default warn) |

---

## Architecture Overview

```
Source (.tyhp / .php)
    │
    ▼
PhpLexer / PhpParser  ──► 8.4 holes fixed + 8.5 tokens (T_PIPE, T_VOID_CAST)
    │                       exit/clone keyword + argumentList forms
    ▼
Binder  ──► attach exit/die/clone keyword-calls to tyhpdef FunctionDeclarationSymbol
    │
    ▼
Checker ──► type pipe RHS callable(1); clone-with array; void cast; const attrs
    │
    ▼
Emitter (IsPhpVersionAtLeast)
    ├─ target ≥ feature  → native PHP spelling
    └─ target < feature  → rewrite / polyfill / strip (+ diagnostics when impossible)
```

---

## Phase 1: Grammar — close PHP 8.4 holes

> **Status:** COMPLETE

- [x] **`propertyHookBody`:** add bare `T_SYM_SEMICOLON` alternative (abstract / interface hooks: `{ get; set; }`), matching php-src `property_hook_body`.
- [x] **`propertyHookList`:** allow `attributes?` before each `propertyHook` (php-src `property_hook_list` + attributes).
- [x] **`#phpExprExit`:** change from optional `expr?` to optional full **`argumentList`** (empty / args / `...` FCC / named args), mirroring `ctor_arguments`.
- [x] Regenerate lexer/parser (`compile_grammar.sh`); update `Grammar/technical-guide.md` for these productions.
- [x] **Removed `(unset)` / `T_UNSET_CAST` entirely** from lexer/parser/docs/tests (PHP 8.5 deprecates it; Tyhp’s highest supported PHP is 8.5 — do **not** add it to unary pre-ops; users cannot use it).

**Tests:** parse fixtures for interface hooks, attributed hooks, `exit(...)`, `exit(status: 0)`; rejection of `(unset)$x`.
---

## Phase 2: Grammar — PHP 8.5 tokens & productions

> **Status:** COMPLETE

- [x] **Lexer:** `T_PIPE` (`|>`), `T_VOID_CAST` (`(void)` with whitespace rules like other casts).
- [x] **Parser — pipe:** binary expr at correct precedence (php-src: `expr T_PIPE expr`); left-associative chain.
- [x] **Parser — void cast:** unary / statement/`expr_list` forms per php-src (including for-loop expr lists if applicable).
- [x] **Parser — clone:** keep unary `T_CLONE expr`; add call form with argument list / ambiguity handling equivalent to `clone_argument_list` (so `clone($x)` vs `clone($x, …)` work).
- [x] **Parser — const attributes:** allow attributes on top-level `const` (php-src `attributed_top_statement` including `T_CONST`), single-const-statement rule if PHP requires one declarator when attributed.
- [x] Update grammar file headers to PHP-8.5 lineage; refresh `technical-guide.md`.

**Tests:** parse-only fixtures for each construct (including rejection of illegal attributed multi-`const` if PHP rejects it).

---

## Phase 3: tyhpdef — `exit` / `die` / `clone` signatures

> **Status:** COMPLETE

- [x] Add to the appropriate core / ExtCore (or language-construct) tyhpdef surface:

```tyhp
function exit(string|int $status = 0): never;
function die(string|int $status = 0): never;

function clone(object $object, array $withProperties = []): object;
```

  Hand-curated block at top of `runtime/php-extensions/php8.2.9/ExtCore.tyhpdef` (not from reflection).

- [x] Gate with `declare(php=…)` / `#[\Tyhp\Php]` **only if** Story 20.5 is available; otherwise document interim “always present, emit rewrites by `output.phpVersion`” (preferred for this story so 14.5 does not block on 20.5).

  **Choice:** always present (Story 20.5 not available). Documented in ExtCore comment + `Binder/BuiltIn/README.md`. Emit rewrites remain Phase 5 / `output.phpVersion`.

- [x] Ensure symbols are bindable as global functions for FCC and `callable` use without allowing userland redeclaration (match PHP reserved behavior — checker/binder errors on user `function exit`).

  Redeclaration already impossible: lexer emits `T_EXIT` / `T_CLONE`; `functionName` accepts only `T_STRING` | `T_READONLY`. Verified by parse tests; no new binder hook required.

**Tests:** `tests/Tyhp.Tests/Binder/ExitDieCloneTyhpdefStubTests.cs` — stub load/signatures + userland redeclaration parse rejection.

---

## Phase 4: Binder / checker — wire keyword-calls + new exprs

> **Status:** COMPLETE

- [x] Bind `T_EXIT` / `T_CLONE` call forms to the tyhpdef symbols (same pipeline as normal calls for arg count/types/names).
- [x] Unary `clone $x` remains the existing clone expression (object type check); not forced through the two-arg tyhpdef overload unless convenient.
- [x] **Pipe:** RHS must be callable with one required parameter; result type = return type of RHS; diagnose invalid RHS / by-ref params per PHP rules as practical.
- [x] **`(void)`:** type-checks operand; result is void / non-value in expression position per PHP rules; intentional discard for `#[\NoDiscard]` when that attribute exists in stubs.
- [x] **Const attributes:** validate attribute targets (`TARGET_CONSTANT` when Attribute stubs know it).
- [x] Register any new `MessageCode`s in `MessageCode.cs` + both `.resx` files (Story 14 style).

  **Audit (item 6):** Phase 4 new codes are **TYHP4162–4165** only (`CheckerPipeRhsNotCallable`, `CheckerPipeRhsInvalidArity`, `CheckerPipeRhsByRefParameter`, `CheckerNoDiscardReturnUnused`). Const-attribute work reuses existing **TYHP4126/4127**. All four new codes are present in `MessageCode.cs` with matching `ERROR_`/`WARNING_` + `EXPLAIN_` entries in both `CLI.TyhpHostedService.resx` and `CLI.TyhpHostedService.en-US.resx` (parity OK). No gaps to fix.

---

## Phase 5: Emitter — native emit vs lower-target rewrite

> **Status:** COMPLETE

- [x] **Pipe:** ≥ 8.5 emit `|>`; &lt; 8.5 rewrite to nested calls or temp assignments (preserve left-to-right; parenthesize arrow functions as PHP requires).
- [x] **`(void)`:** ≥ 8.5 emit cast; &lt; 8.5 emit discarded expression statement / omit cast.
- [x] **`clone` call / clone-with:** ≥ 8.5 native; &lt; 8.5 reuse `WithKeywordHelper` / `ObjectHelper::with` / readonly IIFE patterns; unary `clone $x` pass-through.
- [x] **`exit`/`die`:** emit keyword forms; ensure FCC lowering for &lt; 8.4 if we accept those sources.
- [x] **Const attributes / hook attributes:** follow existing attribute emission; strip when target PHP cannot represent them, with clear diagnostics when stripping changes semantics.
- [x] Extend `Grammar/technical-guide.md` and emitter technical guide with rewrite tables.

---

## Phase 6: Product version `805.0.0` + docs

> **Status:** COMPLETE

- [x] Set `<Version>805.0.0</Version>` in `tyhp.csproj`.
- [x] Update user-facing version mentions in docs samples as needed.
- [x] Short upgrade note: Tyhp 805 understands PHP 8.5 syntax and rewrites for lower `output.phpVersion`.
- [x] Optional: mention in `docs/content/release_planning.md` “Current Status” that development tip is 805.

---

## Phase 7: Conformance fixtures & acceptance

> **Status:** COMPLETE (2026-08-07) — all `story14_5` golden subgroups green (19/19); parse/emit focused regressions green; `story11/` conformance green (18/18); `tyhp version` → `805.0.0`

- [x] Golden `.tyhp` → `.php` fixtures under `tests/conformance/story14_5/` (or story-named path per `CONVENTIONS.md`) for:
  - [x] interface/abstract hooks with `;` (`tests/conformance/story14_5/interface-abstract-hooks/`, native `output.phpVersion` 8.5)
  - [x] attributed property hooks (`tests/conformance/story14_5/attributed-property-hooks/`, native `output.phpVersion` 8.5; attributes preserved on interface `;` hooks + class hook bodies)
  - [x] `exit` / `die` call + FCC (`tests/conformance/story14_5/exit-die-call/`, native `output.phpVersion` 8.5 bare/positional/empty/named/FCC; lower case `output:phpVersion` 8.2 empty→bare, named→positional, FCC→static arrow)
  - [x] pipe chains (`tests/conformance/story14_5/pipe-chains/`, native `output.phpVersion` 8.5 FCC/chain/arrow/variable/precedence; lower case `output:phpVersion` 8.2 nested-call rewrite)
  - [x] `(void)` cast (`tests/conformance/story14_5/void-cast/`, native `output.phpVersion` 8.5 statement + for-list forms; lower case `output:phpVersion` 8.2 omit cast / discarded operand)
  - [x] `clone($o, […])` native + rewrite (`tests/conformance/story14_5/clone-call/`, native `output.phpVersion` 8.5 unary/parenthesized/trailing-comma/named/clone-with/FCC; lower case `output:phpVersion` 8.2 ObjectHelper::with / unary rewrite / FCC→static arrow)
  - [x] attributed top-level `const` (`tests/conformance/story14_5/attributed-top-level-const/`, native `output.phpVersion` 8.5 preserve `#[…]` on file/namespace `const`; lower case `output:phpVersion` 8.2 strip attributes + TYHP5017, keep `const` lines)
- [x] Unit tests for precedence (pipe vs other ops) and clone ambiguity.
- [x] Full conformance / relevant emitter categories green.

---

## Cross-Story References

| Story | Relationship |
|-------|----------------|
| **06** | tyhpdef loading for exit/die/clone |
| **08** | Call checking, pipe typing |
| **09 / 11** | Emit + existing hook/aviz/`with`/`clone` lowering |
| **10** | `output.phpVersion` |
| **14** | Diagnostic quality for new codes |
| **15** | Document new interop lowerings after this lands |
| **20.5 / 21** | Optional gating of stubs; PHP 8.5 stub APIs |
| **Grammar audit (chat)** | Source priority list for this story |

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07** is the project backbone. See `CONVENTIONS.md`.

- [x] **Parse:** Every 8.4 hole and 8.5 construct in scope parses; known illegal forms fail cleanly.
- [x] **Golden emit:** Native snapshots for `output.phpVersion` 8.5; rewrite snapshots for at least one lower version (8.2 or 8.4 as appropriate per feature).
- [x] **tyhpdef:** `exit` / `die` / `clone` check named args and arity via stubs.
- [x] **Version:** `tyhp version` reports `805.0.0`.
- [x] **Conformance green** for new fixtures; no regressions in property-hook / `with` suites.
- [x] **Diagnostics:** new codes only in `MessageCode.cs` + both `.resx` files.
