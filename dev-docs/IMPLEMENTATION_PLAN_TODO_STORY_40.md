# Implementation Plan: Story 40 — Pipe-Emit Chained Extension Calls (`|>` on PHP 8.5+)

> **Roadmap position:** Story 40 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 09, 11, 14.5
> **New story:** split from Story 31 Idea 11 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration). This story
> changes what an author writes, so `guide/28-availability-gotchas.md` must be updated too: its
> "not in the language yet" list is what an agent trusts to decide whether a construct is usable.

> **Status:** Emit-only optimization; Tyhp source stays `$recv->ext()->ext()`.
> **Prerequisites:** Story 11 (extension rewrite to static calls), Story 14.5 (`|>` parse/emit +
> `IsPhpVersionAtLeast(8, 5)`), Story 09/11 emitter. Story 21’s built-in scalar catalog would benefit once it
> ships (`$name->trim()->toLower()`).
> **Related:** `BuildPipeExpression` / `PipeOperatorEmitterTests` already implement *source* `|>`; this story is
> the inverse — *generate* `|>` from extension chains that today nest as `C::c(C::b(C::a($x)))`.
> **Source:** chaining example on Scalar Pseudo-Objects (`$input->trimmed()->lower()->dashed()`), 2026-08-14.

## Summary

Extension method chains rewrite today to nested static calls (innermost = leftmost receiver):

```tyhp
$result = $input->trimmed()->lower()->dashed();
```

```php
// Current emit (all PHP targets)
$result = \StringHelpers::dashed(\StringHelpers::lower(\StringHelpers::trimmed($input)));
```

When `output.phpVersion` is **8.5 or newer**, emit the same chain with PHP’s pipe operator **when able**:

```php
$result = $input
    |> \StringHelpers::trimmed(...)
    |> \StringHelpers::lower(...)
    |> \StringHelpers::dashed(...);
```

That is equivalent (`$a |> f(...) |> g(...)` ≡ `g(f($a))`) and stays left-to-right. Below 8.5, keep nested
calls (same as today’s extension emit; do not invent a second lowering path).

## When it is able

PHP 8.5 `|>` passes the left value as the **callable’s argument**. First-class callable `Ext::method(...)` is
legal on the RHS. Use pipe only when the rewritten static call would take **exactly the piped receiver** (no
extra arguments supplied):

| Tyhp | Able? | Emit ≥ 8.5 |
| --- | --- | --- |
| `$s->trimmed()->lower()` | Yes | `$s \|> \E::trimmed(...) \|> \E::lower(...)` |
| `$s->truncate(10)` | No | `\E::truncate($s, 10)` (extra arg) |
| `$s->trimmed()->truncate(10)` | Partial | `\E::truncate($s \|> \E::trimmed(...), 10)` or keep all nested — pick one policy when scheduled |
| `$s->trimmed()` (single call) | No benefit | `\E::trimmed($s)` — do not introduce `|>` for length-1 |

Default policy when scheduled: **pipe a maximal suffix/prefix of receiver-only calls**; mix-in extra-arg calls
with nested application rather than wrapping every extra-arg step in `(fn($x) => Ext::m($x, …))` unless that
reads better in a long chain. Do not change evaluation order.

Also apply to user `extension` methods and (later) Story 21 stdlib scalar extensions. Instance methods on real
objects are out of scope unless they already lower to static calls the same way.

## Implementation sketch

- Hook after (or in) extension-call rewrite (`ExtensionMethod` emit): detect a left-associated chain of
  rewritten static calls sharing the pipe-friendly shape.
- Gate: `IsPhpVersionAtLeast(8, 5)` (same helper as Story 14.5 pipe).
- RHS spelling: FQCN `\\Ext::method(...)` (FCC), matching current static rewrite names.
- Reuse parenthesization rules from `BuildPipeExpression` (nested left, FCC vs closure).
- Config: none required; optional later opt-out if someone prefers nested calls in diffs.

## Decisions (defaults chosen)

1. **Source unchanged** — authors keep `$x->a()->b()`; pipe is an emit strategy, not new Tyhp syntax.
2. **≥ 8.5 only** — never emit `|>` when targeting 8.2–8.4.
3. **Receiver-only** — extra arguments disable that step’s pipe form.
4. **Length ≥ 2** — single extension calls stay ordinary static calls.

## Risks & Edge Cases

- Extra-arg steps in the middle of a chain (policy above).
- Named arguments / variadics on the extension.
- `by-ref` receiver (if ever allowed) — pipe is by-value; do not pipe.
- Debugging / sourcemaps: one Tyhp chain vs several `|>` ops (Story 17).
- PSR-12 / line wrapping of long pipes vs nested calls.

## Golden Fixtures / Tests (Acceptance)

- `$input->trimmed()->lower()->dashed()` → nested statics on 8.4; `|>` FCC chain on 8.5.
- Single `$s->trimmed()` never emits `|>`.
- `$s->truncate(10)` never emits `|>`.
- Mixed chain has a locked expected spelling once the partial-pipe policy is chosen.
- Existing `PipeOperatorEmitterTests` (source `|>`) still pass; new tests live next to
  `ExtensionMethodEmitterTests`.
