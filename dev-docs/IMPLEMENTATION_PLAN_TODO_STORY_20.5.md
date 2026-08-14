# Implementation Plan: Story 20.5 — PHP Version Gating (`declare(php=…)` + `#[\Tyhp\Php]`)

> **Roadmap position:** Story 20.5 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story 20, before Story 21)
> **Direct dependencies (new numbering):** 06, 08, 09, 10, 11, 04 (`tyhp/core` for the attribute class)
> **New story:** carved out during Story 21 package-design review — language + compiler support for PHP-version-conditional declarations so Story 21 can ship a **single** `tyhp/php` package (and `tyhp/php-ext-*`) instead of per-minor forks.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-07-23
> **Prerequisites:** Stories ≤11 complete or in progress for emitter feature surface; Story 04 runtime `tyhp/core` package; Story 06 package/tyhpdef loading; Story 08 checker; Story 09/11 emitter; Story 10 `output.phpVersion`.
> **Consumers:** Story 21 (PHP extension Composer packages), Story 20 Phase 8 (multi-target gated tyhpdef generation).

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [Scope (In / Out)](#scope-in--out)
- [Architecture Overview](#architecture-overview)
- [Decisions (locked)](#decisions-locked)
- [Phase 1: `\Tyhp\Php` attribute in `tyhp/core`](#phase-1-tyhpphp-attribute-in-tyhpcore)
- [Phase 2: Constraint evaluation library](#phase-2-constraint-evaluation-library)
- [Phase 3: Binder — `declare(php=…)` gating](#phase-3-binder--declarephp-gating)
- [Phase 4: Binder — `#[\Tyhp\Php]` member/type gating](#phase-4-binder--tyhpphp-membertype-gating)
- [Phase 5: Checker — validation & unreachable](#phase-5-checker--validation--unreachable)
- [Phase 6: Emitter — strip gate constructs](#phase-6-emitter--strip-gate-constructs)
- [Phase 7: Config default & package-load integration](#phase-7-config-default--package-load-integration)
- [Phase 8: Tests & conformance fixtures](#phase-8-tests--conformance-fixtures)
- [Diagnostic Codes](#diagnostic-codes)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

This story adds **compile-time PHP version gating** to Tyhp and tyhpdef:

1. **`declare(php="…")`** — file-level or block-level, using **full Composer version-constraint syntax**. Value is a string. The directive key is `php` and must appear **alone** in that `declare`.
2. **`#[\Tyhp\Php(string $version)]`** — attribute on class / interface / enum / trait and on their members (and other normal attribute targets such as functions). **Not** allowed on `struct` or `extension` declarations (those use `declare` blocks only).
3. Evaluation is against **`output.phpVersion`**. If unset, default to **`8.2`** and **warn**.
4. Bare version numbers like `"8.2"` mean the **whole minor** (`8.2.*`).
5. Inactive file-level `declare(php=…);` → **skip the file silently**.
6. Nested / combined gates → declaration must satisfy **all** active constraints; inactive regions are **unreachable**.
7. Version-**disjoint** same-name declarations are allowed; **overlapping** constraints that both declare the same symbol → **error**.
8. Gate constructs are **compile-time only** — never emitted to PHP (same treatment as `declare(output_file=…)`).

Story 21 depends on this story to maintain one stubs package across PHP 8.2–8.5.

---

## Motivation

Story 21 originally planned `tyhp/php-8.2` … `tyhp/php-8.5` via `cp -r` forks. Almost all Tyhp value (generics, scalar extensions, Pure/Inline) is identical across minors; only a small API surface differs. Per-minor packages force repeated hand-edits and Composer platform juggling.

With gating:

- One **`tyhp/php`** (+ **`tyhp/php-ext-*`**) package
- Diffs expressed as `declare(php=">=8.3")` / `#[\Tyhp\Php(">=8.4")]`
- Compiler filters by project `output.phpVersion`

---

## Scope (In / Out)

| In scope | Out of scope |
|----------|--------------|
| `declare(php="…")` semantics (file + block) | Changing Composer package layout (Story 21) |
| `#[\Tyhp\Php]` on class/interface/enum/trait + members + functions | `#[\Tyhp\Php]` on `struct` / `extension` |
| Composer-full constraint parsing + `8.2` ⇒ `8.2.*` | Multi-target tyhpdef generator merge (Story 20 Phase 8) |
| Binder filtering + version-disjoint same-name | Member-level `declare` (illegal in PHP/Tyhp grammar) |
| Checker diagnostics (43xx feature band) | LSP-specific UX polish beyond honoring existing bind/check |
| Emitter stripping of gate declare/attribute | Emitting runtime version checks into PHP |

---

## Architecture Overview

```
output.phpVersion (Story 10) ──► PhpVersionConstraint.IsSatisfied(target, constraint)
                                         │
         ┌───────────────────────────────┼───────────────────────────────┐
         ▼                               ▼                               ▼
  File-level declare(php=)        Block declare(php=) { }         #[\Tyhp\Php("…")]
  (skip file if inactive)         (skip body if inactive)         (omit symbol if inactive)
         │                               │                               │
         └───────────────────────────────┴───────────────────────────────┘
                                         │
                                         ▼
                              Binder registers only active symbols
                              Checker: alone / invalid / unreachable / overlap
                              Emitter: strip declare(php=) and #[\Tyhp\Php]
```

**AND semantics:** if a member with `#[\Tyhp\Php(">=8.4")]` sits inside `declare(php=">=8.3") { … }`, both must match. If the outer declare is inactive, the whole block (including attributes) is inactive / unreachable.

---

## Decisions (locked)

| Topic | Decision |
|-------|----------|
| Declare key | `php` |
| Constraint language | Full Composer syntax |
| `"8.2"` meaning | Whole minor `8.2.*` |
| File-level | Allowed; inactive → skip silently |
| Block-level | Wherever `declare` is allowed |
| Alone | `php` not mixed with other directives in the same `declare` |
| Attribute | `\Tyhp\Php` in `tyhp/core`, ctor `string $version` |
| Attribute targets | class, interface, enum, trait, members, functions — **not** struct/extension |
| Struct / extension gating | `declare(php=…) { … }` only |
| Same-name | Disjoint constraints OK; overlap → error |
| Nesting | Must satisfy all; unreachable when inactive |
| Missing `output.phpVersion` | Default `8.2` + warning |
| Where allowed | Packages and user projects |
| Story ownership | This story (20.5); packages in 21; multi-gen in 20 Phase 8 |

### Syntax examples

```tyhpdef
declare(php=">=8.5");

declare(php=">=8.3") {
    function json_validate(string $json, int $depth = 512, int $flags = 0): bool;
}

#[\Tyhp\Php(">=8.4")]
function array_find(array $array, callable $callback): mixed;

#[\Tyhp\Php(version: ">=8.2 <8.4")]
function example(string $v): mixed;
#[\Tyhp\Php(version: ">=8.4")]
function example(string $v, bool $strict = false): string;

declare(php=">=8.5") {
    extension UriStringExtensions extends string {
        // ...
    }
}
```

---

## Phase 1: `\Tyhp\Php` attribute in `tyhp/core`

### Phase Overview

Add the attribute class to the Tyhp core runtime package so projects that depend on `tyhp/core` can reference it as a normal attribute. The compiler treats it as a **compile-time gate** (stripped on emit).

### Deliverables

1. `runtime/packages/core/tyhp_src/Php.tyhp` (emitted under `src/Tyhp/Php.php`)
2. Rebuild / sync committed PHP under `runtime/packages/core/src/` per package conventions
3. Attribute metadata: appropriate `Attribute` targets (class, function, method, property, class constant, parameter, etc. — everything except documenting struct/extension as supported gate targets)

### Implementation sketch

```tyhp
<?tyhp

namespace Tyhp;

#[\Attribute(
    \Attribute::TARGET_CLASS
    | \Attribute::TARGET_FUNCTION
    | \Attribute::TARGET_METHOD
    | \Attribute::TARGET_PROPERTY
    | \Attribute::TARGET_CLASS_CONSTANT
    | \Attribute::TARGET_PARAMETER
)]
class Php
{
    public function __construct(
        public readonly string $version,
    ): void {}
}
```

### Acceptance Criteria

- [ ] `\Tyhp\Php` exists in `tyhp/core` with `string $version`
- [ ] Positional `#[\Tyhp\Php(">=8.4")]` and named `#[\Tyhp\Php(version: ">=8.4")]` both work
- [ ] Package still builds / self-host diff policy honored if this story touches runtime sources

---

## Phase 2: Constraint evaluation library

### Phase Overview

Implement a Composer-compatible version constraint evaluator used by binder and checker.

### Deliverables

1. C# helper (suggested path: `Tyhp/TyhpLang/Versioning/PhpVersionConstraint.cs` or under `Tyhp/Domain/`)
2. Normalize target `output.phpVersion` (`"8.2"` / `"8.2.0"`) for comparison
3. Treat bare `"8.2"` / `"=8.2"` in constraints as matching the **entire minor** (`>=8.2.0 <8.3.0` equivalent), consistent with stubs package policy
4. Support full Composer constraint grammar (ranges, `||`, `^`, `~`, stability flags as applicable to numeric PHP versions)

### Implementation notes

- Prefer a maintained Composer-semver-compatible library **or** a focused parser that matches Composer’s documented constraint language for PHP platform versions. Behavior must stay consistent with Composer’s `php` platform package expectations where applicable.
- Invalid constraint strings → diagnostic (do not throw uncaught).

### Acceptance Criteria

- [ ] Unit tests cover exact, `>=`, `<`, AND ranges, `||`, `^`, `~`, and bare minor banding
- [ ] Invalid constraints produce a stable error code (see Diagnostic Codes)

---

## Phase 3: Binder — `declare(php=…)` gating

### Phase Overview

Honor `declare(php=…)` when building scopes / registering symbols.

### Behavior

1. **File-level** `declare(php="…");` (non-block): if unsatisfied → **do not bind** the file’s declarations (skip silently; no error). Still parse for tooling if needed, but symbols are absent.
2. **Block** `declare(php="…") { … }` / alternate `declare:`…`enddeclare`: if unsatisfied → do not register symbols from the body; mark region inactive for checker.
3. **Alone rule:** if the declare list contains `php` plus any other directive → error (checker may also report; binder should not partially apply).
4. Combine with nested declares via **AND**.
5. Store active constraint context on declare block scopes (extend `DeclareBlockSymbol` / file metadata as needed).

### Interaction with existing declares

- `strict_types`, `ticks`, `encoding`, `output_file`, `include_tag` unchanged.
- `declare(php=…)` must not be mixed with them in the **same** declare statement.
- Separate sequential declares are fine:

```tyhp
declare(strict_types=1);
declare(php=">=8.4");
```

### Acceptance Criteria

- [ ] Inactive file-level gate → symbols from that file absent
- [ ] Inactive block → inner symbols absent
- [ ] Active block → symbols present and usable
- [ ] Nested declares AND correctly

---

## Phase 4: Binder — `#[\Tyhp\Php]` member/type gating

### Phase Overview

When registering a declaration that carries `#[\Tyhp\Php]`, evaluate `$version` against the effective constraint stack (including enclosing `declare(php=…)`).

### Behavior

1. Resolve attribute to `\Tyhp\Php` (FQCN).
2. Read `version` argument (positional or named).
3. If unsatisfied → **omit** the symbol from the scope (as if not declared).
4. **Version-disjoint same-name:** multiple declarations of the same symbol name are allowed when their **effective** constraint sets are pairwise disjoint; binder keeps the variant(s) that match the target version (usually exactly one).
5. **Overlap:** if two declarations of the same symbol have overlapping constraints (both could match some version, or both match the current target), report duplicate-declaration error (`CheckerPhpVersionDuplicateDeclaration` or existing duplicate path — prefer the reserved 43xx code for clarity).
6. **Struct / extension:** if `#[\Tyhp\Php]` appears on a struct or extension declaration, error (`CheckerPhpVersionAttributeInvalidTarget`). Authors must wrap with `declare(php=…) { }`.

### Acceptance Criteria

- [ ] Gated members appear only when version matches
- [ ] Disjoint same-name methods/functions bind the correct variant for the target
- [ ] Overlap → error
- [ ] Attribute on struct/extension → error

---

## Phase 5: Checker — validation & unreachable

### Phase Overview

All user-facing gate diagnostics live in the **checker feature band `4300–4399`** (reserved in `MessageCode.cs`).

### Rules

1. Invalid Composer constraint string.
2. `declare` mixes `php` with other directives.
3. Code / declarations inside an inactive `declare(php=…)` block → **unreachable** (reuse conceptual behavior of unreachable-code detection; use the Story 20.5 code for version-gate unreachable).
4. Nested inactive regions after an outer unsatisfied gate.
5. `#[\Tyhp\Php]` missing/non-string `version` argument.
6. `#[\Tyhp\Php]` on struct/extension.
7. Overlapping version-disjoint violations (if not already failed in binder — checker may double-check).
8. Warning when `output.phpVersion` was unset and defaulted to `8.2`.

### Acceptance Criteria

- [ ] Each reserved MessageCode has a conforming `.resx` entry (both culture files)
- [ ] Fixtures cover happy path + each error/warning

---

## Phase 6: Emitter — strip gate constructs

### Phase Overview

Gate machinery must not appear in emitted PHP.

### Behavior

1. Do not emit `declare(php=…)` (file or block). Emit only the **active** body’s statements when the gate was satisfied at compile time; inactive bodies emit nothing.
2. Do not emit `#[\Tyhp\Php(…)]` attributes on emitted declarations.
3. Follow the existing pattern for filtering Tyhp-only declares (`output_file`).

### Acceptance Criteria

- [ ] Golden `.tyhp → .php` fixtures contain no `declare(php=` and no `\Tyhp\Php` attributes
- [ ] Active gated code emits normally

---

## Phase 7: Config default & package-load integration

### Phase Overview

Wire `output.phpVersion` defaulting and ensure package/tyhpdef loading uses the same evaluator.

### Behavior

1. If `output.phpVersion` is missing/null → treat as `"8.2"` and emit **warning** once per compilation (not per file).
2. Story 06 package discovery / tyhpdef registration must apply file-level and declaration-level gates so Composer-loaded `tyhp/php` stubs filter correctly.
3. Document that Story 21’s single-package layout depends on this filtering (no mutually exclusive `tyhp/php-8.x` Composer packages).

### Acceptance Criteria

- [ ] Default + warning covered by test
- [ ] Loading a gated tyhpdef package with `output.phpVersion=8.2` vs `8.4` yields different visible APIs

---

## Phase 8: Tests & conformance fixtures

### Deliverables

1. Unit tests for constraint evaluator
2. Binder/checker/emitter tests for declare + attribute
3. Conformance fixtures under Story 07 layout (e.g. `tests/conformance/story20_5/`)
4. Matrix smoke: targets `8.2`, `8.3`, `8.4`, `8.5`

### Acceptance Criteria

- [ ] Conformance green with new fixtures
- [ ] `dotnet test` covers new components

---

## Diagnostic Codes

Reserve in `MessageCode.cs` checker **feature band** (update CONVENTIONS note to include Story 20.5). Exact registry:

| Code | Enum | Severity | When |
|------|------|----------|------|
| 4300 | `CheckerPhpVersionInvalidConstraint` | Error | Constraint string is not valid Composer syntax |
| 4301 | `CheckerPhpVersionDeclareNotAlone` | Error | `php` mixed with other declare directives |
| 4302 | `CheckerPhpVersionUnreachable` | Error/Warning* | Code under inactive `declare(php=…)` |
| 4303 | `CheckerPhpVersionDuplicateDeclaration` | Error | Same symbol, overlapping version constraints |
| 4304 | `CheckerPhpVersionAttributeInvalidTarget` | Error | `#[\Tyhp\Php]` on struct or extension |
| 4305 | `CheckerPhpVersionAttributeInvalidArgument` | Error | Missing/non-string `version` |
| 4306 | `CheckerPhpVersionDefaulted` | Warning | `output.phpVersion` unset; defaulted to `8.2` |

\* Prefer **error** for unreachable gated regions in tyhpdef/tyhp sources so authors notice dead gates; adjust only if it conflicts with intentional multi-version authoring inside one file (inactive arms of disjoint declares should **not** be diagnosed as unreachable — they are alternate variants). **Rule:** unreachable applies to nests that can never run for *any* supported target or to code after a gate that is inactive *and* not part of a version-disjoint alternate set. Practical implementation: do **not** flag inactive `declare(php=)` / `#[\Tyhp\Php]` bodies as unreachable when they are alternate variants; only flag nested code that is inactive because an *enclosing* constraint failed while the author wrote executable statements assuming it was active in a single-target compile — simplest v1: **only diagnose `CheckerPhpVersionUnreachable` for nested declares/attributes that are unsatisfiable given the outer constraint** (e.g. outer `>=8.4` with inner `<8.3`).

---

## Cross-Story References

| Story | Relationship |
|-------|----------------|
| **04** | Hosts `\Tyhp\Php` in `tyhp/core` |
| **06** | Package/tyhpdef load must honor gates |
| **08** | Checker rules / unreachable patterns |
| **09 / 11** | Emitter stripping |
| **10** | `output.phpVersion` |
| **20** | Phase 8 multi-target generator **emits** these gates (depends on this story) |
| **21** | Consumes gates for single `tyhp/php` + `tyhp/php-ext-*` packages |
| **12 / 19** | Lint/LSP automatically benefit once bind/check honor gates |

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). See `CONVENTIONS.md` and Story 07.

- [ ] **Golden fixtures:** `.tyhp` / `.tyhpdef` samples covering declare file/block, attribute additions, disjoint same-name, overlap error, struct/extension reject attribute, default version warning, emit stripping
- [ ] **Unit / integration tests:** constraint library + binder/checker/emitter
- [ ] **Conformance run green** before story done
- [ ] **Runtime self-host:** if `tyhp/core` sources change, recompile and diff committed PHP
