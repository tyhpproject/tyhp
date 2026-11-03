# Implementation Plan: Story 36 — Conditional Return Types (v1: Overload Desugar + `extends ? :` + `match`)

> **Roadmap position:** Story 36 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 08, 11
> **New story:** split from Story 31 Idea 7 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration). This story
> changes what an author writes, so `guide/28-availability-gotchas.md` must be updated too: its
> "not in the language yet" list is what an agent trusts to decide whether a construct is usable.

> **Status:** Self-contained language/checker feature. **Placement note:** Stories 16–30 were reviewed for a home.
> None fit — Story 28 is only generic *parameter defaults*; Story 21 *consumes* specialized signatures in tyhpdefs but
> does not implement the type IR; Story 11 already shipped overload signatures as a distinct compile-time mechanism.
> **Prerequisites:** Story 08 (checker — literal types, assignability, generics), Story 11 (function/method overload
> signatures + emitter erasure to a single PHP implementation). Grammar/binder support for type-position `match` and
> conditional types as needed. Flag-presence also needs const values when known, `|`/`&`/`~` folding on known ints
> and named flag atoms, and literal/`contains` specificity in overload scoring (documented today, not yet scored).
> **Related:** `docs/content/tyhp_1200_functionOverloads.md`; `docs/content/tyhpdef_overloadedFunctions.md`;
> `CONVENTIONS.md` §6 (overlay placement); brainstorm 2026-08-04 (overloads → conditional IR; ternary + match;
> non-distributive v1; explicit `...T` deferred to Story 37); brainstorm 2026-08-13 (bitmask `contains F`, not
> exact-int overloads).
> **Out of scope here:** `infer`, distributive conditionals, recursion, tuple/variadic patterns, template-literal
> types, body-vs-every-arm verification, mapped/`keyof` algebra (see Story 37 / v3+); PHPStan-style `int-mask-of<F>`
> **subset** matching; flag-gated **parameter existence** (PHP almost never adds/removes params because of a flag —
> refine in-place types, including by-ref out-params); an opt-in to treat a runtime `int` variable as “has flag F.”

## Summary

V1 unifies four author surfaces into **one checker IR**: generics + **conditional return types**, including bitmask
**flag-presence**.

| Surface | Role |
|---------|------|
| Overload signatures (keep) | Readable discrete / one-flag contracts; **desugar** to generic + conditional/`match` return when the overload set is a clean discriminant partition |
| `T extends X ? A : B` | Dense binary / nested branching (parentheses allowed; `?:` right-associative) |
| `match (T) { … }` | Multi-arm branching; nestable; freely mixed with `?:`; arms include literals **and** `contains F` |
| `contains F` | Flag-presence predicate on a compile-time int / flag-set (`F ⊆` known bits). Param constraint **and** match arm. Subtype of `int` (or of a declared flag-family type) |

Classic overload resolution remains the **fallback** when a set cannot desugar cleanly (arity mismatches, overlapping
non-partitionable params, etc.) — never invent a mega-conditional that only “almost” matches.

**Non-distributive:** a union scrutinee is treated as one type, not mapped per constituent (`Box<string|int>` does
**not** become `array<string>|array<int>` in v1). Distribution is Story 37 via explicit `...T`.

Emit stays as today in spirit: one PHP function/method with the **implementation’s** (union) signature; overload /
conditional forms are compile-time only. Desugaring may **add** type parameters to an already-generic declaration for
the Tyhp IR; it does not invent runtime PHP generics.

## Motivation

Overloads work for cases generics cannot express alone (e.g. `true` → `array`, `false` → `string`), but they feel
bolted on: boilerplate, drift vs the implementation, poor composition when wrapping calls. Identity cases remain plain
generics (`function f<T extends array|string>(T $x): T`). Conditional/`match` returns give a single composable
mechanism; overloads become sugar for the common discrete-literal API.

Discrete literals are **not** enough for PHP bitmasks. `\json_encode($v, \JSON_PRETTY_PRINT | \JSON_THROW_ON_ERROR)`
is not an exact value of `JSON_THROW_ON_ERROR`, two `int $flags` overloads are the same signature, and today’s `|`
widens to `int`. V1 therefore adds **`contains F`**: the interesting question is whether a flag bit is **known to be
set**, not whether the argument equals one integer. Independent flags (`THROW` × `OBJECT_AS_ARRAY` ×
`PREG_OFFSET_CAPTURE`) explode overload matrices; `match` is the authoring form there. The same switch should also
gate `@throws` (e.g. `\JsonException` only when `JSON_THROW_ON_ERROR` is proven) so callers are not forced to keep a
`string|false` guard after opting into throw-on-error.

## Syntax sketch

```tyhp
// Ternary
function generateReport<T extends bool>(T $asArray = false): T extends true ? array : string { … }

// Match (same meaning)
function generateReport<T extends bool>(T $asArray = false): match (T) {
    true  => array,
    false => string,
} { … }

// Overload sugar → same IR when desugarable
function generateReport(true  $asArray): array;
function generateReport(false $asArray): string;
function generateReport(bool  $asArray = false): string|array { … }
```

Mixing (preferred style: match for multi-way, ternary for binary refine):

```tyhp
function parse<F extends string, A extends bool>(
    string $input,
    F $format,
    A $assoc = true,
): match (F) {
    'json' => A extends true ? array : object,
    'csv'  => array<string>,
    'raw'  => string,
    default => mixed,
} { … }
```

Flag-presence (bitmask). `contains F` means **F is on** (`F ⊆` known bits), **not** “bits ⊆ F”:

```tyhp
function json_encode(
    mixed $value,
    int $flags = 0,
    int $depth = 512,
): match ($flags) {
    contains JSON_THROW_ON_ERROR => string,
    default => string|false,
};

// One-flag overload sugar → same IR when desugarable
function json_encode(mixed $value, contains JSON_THROW_ON_ERROR $flags): string;
function json_encode(mixed $value, int $flags = 0, int $depth = 512): string|false;
```

Later discriminants and extra param **types** (not existence) use whole-call matching — PHP often puts `$flags`
after the value they refine:

```tyhp
function preg_match(
    string $pattern,
    string $subject,
    match ($flags) {
        contains PREG_OFFSET_CAPTURE => array<array{string, int}> &$matches,
        default => array<string> &$matches,
    } = null,
    int $flags = 0,
): 1|0|false;
```

Nested match / nested ternary / parentheses all allowed. Arms in v1: literals, unions of literals, `contains F`,
and `extends Type` / plain type patterns — **no `infer`**.

Optional (if cheap): named aliases for conditional types so signatures stay short
(`type ParseResult<F, A> = match (F) { … };`).

## Flag representation (`contains F`)

`contains F` is evaluated against the **known flag-set** of the argument, built as follows:

1. **Numeric bits when the const value is known.** `const JSON_THROW_ON_ERROR = 4194304` (or equivalent) lets the
   checker fold `|` / `&` / `~` on literal ints and known const ints. `contains F` is then `(known & F) === F`
   (treat a multi-bit `F` as “all of F’s bits are on”).
2. **Named flag atom when the numeric value is unknown.** `const int JSON_THROW_ON_ERROR;` (no initializer) is still
   a distinct atom. `|` of atoms builds a known set; `contains F` is set membership. An opaque numeric literal (or
   mixed atom + uninterpretable int) does **not** prove `contains F` — those bits are unknown.

A runtime `int` / unfolded expression has **no** known bits. Omitted flag args use the default’s type (`0` ⇒ empty
set).

`contains F` is a **subtype of `int`** (or of a closed flag-family type, if declared), so it can appear as an
overload parameter type without colliding with another `int $flags` signature.

## Desugar rules (overloads → IR)

- Promote literal-constrained discriminant params to type parameters (`true|false` → `T extends bool`, `'json'|…` →
  `F extends string`, etc.).
- Promote `contains F $flags` to a flag-set type parameter; the corresponding `match` arm is `contains F`.
- Build a `match` / nested conditional covering each overload arm; implementation return type is the runtime/erasure
  upper bound. Proven `contains JSON_THROW_ON_ERROR` should also select the `@throws \JsonException` contract for
  that arm (wide arm keeps `string|false` and no throw-on-error promise).
- Already-generic functions: **add** discriminant type params; keep existing ones.
- If the set is not a clean partition → keep classic overload matching; do not force a conditional rewrite.
- Independent flags / later discriminants: **prefer `match`**, not an overload matrix. Classic overload fallback
  still applies when desugar would be dishonest.

## Overload scoring when several `contains` arms match

Prefer conditionals for multi-flag APIs. If several `contains` overloads **all** match a call:

- Score by **number of proven bits** (or proven atoms, when numeric values are unknown) required by the constraint —
  more specific wins (`contains A|B` beats `contains A` when both A and B are proven).
- **Tie → ambiguity error.** Do not pick declaration order.

Literal specificity (docs already describe exact / static-value / compatible / generic) must actually participate in
scoring; `contains F` is the bitmask analogue of “narrower matcher wins.”

## Semantics / style guidance

- When the discriminant is a wide type (plain `bool` / plain `int`, not a literal or known flag-set), the
  conditional resolves to the **wide** arm / union of arms (same as today’s overload fallback to the impl
  signature). **Unknown `int` is always wide** — never pick `contains F` unless the bit/atom is proven. No opt-in
  to “trust this variable has F” in v1.
- “Flag not present” is only safe when the bit is proven **off** (literal `0`, or a folded mask with no that bit).
  Otherwise it is unknown → wide.
- `default` required when the scrutinee is not a closed exhaustive set.
- Docs guidance: identity → plain generics; 2 arms → ternary or overloads; 3+ arms → match; one bitmask flag →
  `contains F` overload or a two-arm match; several independent flags → match, not overload explosion; different
  return *shape* under author control → prefer separate named functions when practical.

## Stub placement (tyhpdefs)

Flag-dependent / `contains F` signatures belong in **overlays**, not regenerated baselines — see `CONVENTIONS.md` §6.
Story 21 consumes the specialized stubs; this story implements the checker IR. Generation tooling may emit a wide
baseline (`json_encode(…): string|false`); the overlay supplies the `contains` / `match` / overload refinement.

## Touches (anticipated)

Grammar (type-position conditional + `match` + `contains F` in type and match-arm position), AST/checked-type IR
(flag-set / named-atom types), binder (overload desugar / type-param injection; const initializers), checker
(evaluate conditionals at call sites; exhaustiveness; `|`/`&`/`~` fold; `contains` scoring; whole-call matching
for later discriminants), emitter (unchanged PHP shape aside from any metadata), overlays under
`runtime/php-extensions/overlays/` (cite `CONVENTIONS.md` §3 / §6 rather than duplicating layout), docs
(`tyhp_1200_functionOverloads.md` + `tyhpdef_overloadedFunctions.md` + new conditional-types page), conformance
fixtures.

## Decisions (defaults chosen)

1. **One IR** — overloads desugar to conditional/`match` returns when clean; else classic overload resolution.
2. **Ship both `?:` and `match`**; they mix and nest.
3. **Non-distributive** evaluation in v1.
4. **No `infer` / recursion / tuple patterns / template-literal types / body⇔arms checking** in v1 (Story 37).
5. **PHP emit** remains a single implementation with the impl union signature.
6. **Numeric bits when the const value is known; named flag atom when it is not.** `|` folds on both; opaque
   numerics mixed with unknown-value atoms do not prove `contains F`.
7. **Matcher spelling is `contains F`** — presence (`F ⊆` known bits). Not `int-mask-of<F>` subset-of-F.
8. **Unknown `int` is always wide.** No v1 opt-in to treat a variable as having a flag.
9. **Several matching `contains` overloads:** score by number of proven bits/atoms; **tie is an ambiguity error.**
   Prefer `match` over an overload matrix for independent flags.
10. **Flag-dependent stubs live in overlays** (`CONVENTIONS.md` §6), not regenerated baselines.

## Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Desugar ≠ historical overload specificity | Require provable equivalence; fallback to classic overloads when unsure |
| 2 | Default args + literal discriminants | Spec which arm applies when the arg is omitted (use default’s literal type) |
| 3 | Grammar clash: value `match` vs type `match` | Type position only; same keyword, different production |
| 4 | Author complexity | Docs push match for ≥3 arms; discourage deep nested ternaries |
| 5 | Tyhpdef / Story 21 consumers | v1 unblocks richer stubs; generation tooling can adopt later; overlays hold the refinements |
| 6 | `PRETTY \| THROW` widens to `int` today | Fold `|`/`&`/`~` on known ints and named atoms or `contains` never fires on real call sites |
| 7 | `const int F` with no value | Named atom; do not treat an unrelated numeric literal as proving `contains F` |
| 8 | Later discriminant (`preg_match` `$flags` after `$matches`) | Whole-call matching (selection already sees the full arg list) |
| 9 | PHPStan `int-mask-of<F>` subset vs our presence | Docs: `contains F` is `F ⊆` bits; `PRETTY \| THROW` **does** match `contains THROW` |
| 10 | Two `contains` overloads, equal proven-bit count | Ambiguity error; do not use declaration order |
| 11 | Narrow return without matching `@throws` | Same arm gates both; otherwise callers keep `is_string` guards |

## Golden Fixtures / Tests (Acceptance)

- `generateReport(true)` / `(false)` / `()` (default) resolve to `array` / `string` / narrowed-by-default type.
- Overload-only spelling and conditional-only spelling for the same API produce identical call-site types.
- Nested `match` + inner `?:` as in `parse` sketch.
- Non-desugarable overload set still resolves via classic overloads (no bogus conditional).
- `Box<string|int>` with a non-distributive conditional does **not** split into per-member results.
- Emitted PHP has one function and no residual overload signatures.
- `json_encode($v)` and `json_encode($v, 0)` → `string|false`; `json_encode($v, JSON_THROW_ON_ERROR)` → `string`;
  `json_encode($v, JSON_PRETTY_PRINT | JSON_THROW_ON_ERROR)` → `string`; `json_encode($v, JSON_PRETTY_PRINT)` →
  `string|false`; `json_encode($v, $runtimeInt)` → `string|false` (always wide).
- Named-atom const (no numeric initializer) combined with `|` still proves `contains` of that atom; an opaque
  numeric mixed with that atom does not.
- Two `contains` overloads that both match: more proven bits wins; equal proven-bit count is an ambiguity error.
- Overlay-refined `json_encode` / `json_decode` / `preg_match` stubs type-check against the wide baseline + overlay
  pattern (`CONVENTIONS.md` §6); regeneration of the baseline must not clobber them.
