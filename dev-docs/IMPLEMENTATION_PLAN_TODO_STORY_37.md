# Implementation Plan: Story 37 — Advanced Conditional Types (v2)

> **Roadmap position:** Story 37 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 08, 36
> **New story:** split from Story 31 Idea 8 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration). This story
> changes what an author writes, so `guide/28-availability-gotchas.md` must be updated too: its
> "not in the language yet" list is what an agent trusts to decide whether a construct is usable.

> **Status:** Depends on Story 36 (v1 conditional/`match` IR) landing first. May ship as one story
> or staged sub-phases when scheduled.
> **Prerequisites:** Story 36 (v1); Story 08 (checker); solid error-depth / cycle handling before recursive forms.
> **Related:** brainstorm 2026-08-04 — v2 list; distributive spelling = explicit `...T` (not `T[]`, which means array).
> **Still not in v2:** full mapped/`keyof` / remapping type algebra (v3+ / undecided); forcing non-desugarable overload
> sets into conditionals (never — keep Story 36 fallback).

## Summary

V2 grows from “branch on a type” to limited **type-level programming** on top of Story 36’s IR:

1. **`infer`** — bind pieces of a matched shape (`T extends Promise<infer U> ? U : T`; match arms with
   `array<infer E>`; later `infer R extends string`).
2. **Distributive conditionals — explicit `...T`** (chosen over `T[]`):
   ```tyhp
   type Box<T> = ...T extends mixed ? array<T> : never;
   // Box<string|int> => array<string>|array<int>

   type Box2<T> = match (...T) {
       string => array<string>,
       int    => array<int>,
       default => never,
   };
   ```
   Without `...` → v1 non-distributive behavior preserved. With `...` → map the conditional/match over each union
   constituent and union the results. Prefer spread because PHP/`tyhp` already read `T[]` as “array of T.”
3. **Recursive conditional types** — e.g. `DeepAwaited<T>`; needs depth limits, cycle detection, usable diagnostics.
4. **Tuple / variadic inference** — `[infer H, ...infer Rest]`, etc., for typed rest/spread-style APIs.
5. **Template-literal type inference** — already planned elsewhere; land here as part of the conditional/`infer`
   pattern-matching story (route params, prefixes, etc.).
6. **Body vs every conditional arm** — eventually required correctness mode: implementation must be assignable under
   each arm’s refined params / promised return (“signatures don’t lie”). May be late v2 or a strict flag once the IR
   is stable — **not** a blocker for shipping branching itself.

## Motivation

V1 covers discrete flag/format APIs, bitmask `contains F`, and overload sugar. Library/stdlib/`tyhpdef` authors still need extraction (`ReturnOf`,
`Awaited`), honest union distribution (without TS’s invisible naked-`T` rule), and richer pattern forms. Explicit
`...T` is the PHP-facing improvement over TypeScript’s surprising default distribution.

## Decisions (defaults chosen)

1. **Distribution is opt-in** via `...T` / `match (...T)` — never implicit.
2. **Reject `T[]` as the distribute spelling** — conflicts with array types.
3. **`infer` is the gateway** for extraction; constrained infer and recursion follow with hard depth limits.
4. **Mapped/`keyof` algebra stays out** of v2.
5. **Body⇔arms checking** is planned and needed eventually, but ordered after the solver features above.

## Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | TS-style opaque errors / “excessively deep” | Depth caps, cycle detection, invest in diagnostics early |
| 2 | Distributive + `infer` interactions | Spec evaluation order; conformance corpus for naked vs `...` forms |
| 3 | Template-literal inference scope creep | Stick to planned use cases; don’t grow a full string-DSL type language |
| 4 | Body⇔arms false positives on intentional wide impls | Strict mode / incremental rollout; allow impl union as upper bound |

## Golden Fixtures / Tests (Acceptance)

- `Awaited<Promise<int>>` → `int`; non-promise → identity (via `infer`).
- `Box<string|int>` with `...T` → `array<string>|array<int>`; same without `...` → non-split (v1).
- Recursive unwrap bottoms out; cyclic types diagnose cleanly.
- Tuple `Head` / `Tail` patterns; at least one template-literal `infer` fixture from the planned design.
- (When enabled) a lying overload/conditional arm vs body is rejected under the body⇔arms mode.
