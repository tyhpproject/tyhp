# Implementation Plan: Story 43 — Runtime `is Closure<…>` / `is Fiber<…>` via Reflection

> **Roadmap position:** Story 43 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 08, 15, 21.6
> **New story:** split from Story 31 Idea 14 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration).
> A runtime `is` rewrite, not new type syntax: update `tyhp_0150_newTypes.md` / `tyhp_0100_typesAndLocals.md`
> (`is` vs `as` on Closure/Fiber), and `cli_interopContract.md` if a `tyhp/core` helper is emitted.

> **Status:** Story **21.6** ships compile-time `as` only. This story is the runtime `is` follow-up.
> **Prerequisites:** Story 21.6 (Closure/Fiber type-system contract), Story 15 (interop bump if a `tyhp/core` helper is emitted), Story 08 (`is` / `instanceof`).
> **Related:** `ReflectionFunction::getClosureThis()`, `getClosureScopeClass()`, parameter/return types; PHP `Fiber` has no generic metadata.
> **Source:** Story 21.6 design lock, 2026-09-01.

## Summary

Tyhp generics on PHP’s `final` `\Closure` and `\Fiber` are erased. `is \Closure<callable<int, string>>` cannot be a true runtime generic test unless the compiler rewrites it to **reflection** (and/or a `tyhp/core` helper) that compares **PHP** parameter/return types, bound `$this`, and scope class.

That is **best-effort**: many closures are `mixed` at runtime; Tyhp-only precision is gone. Document the gap. Do not pretend `is` equals the checker.

If emit calls a runtime helper, bump `interopContractVersion` (Story 15).

## Decisions (defaults)

1. **Not in 21.6** — 21.6 uses `as` (compile-time assertion) for narrowing Closure/Fiber type arguments.
2. **PHP types only** — reflection cannot recover erased Tyhp generics.
3. **Helper optional** — a `\Tyhp\…` helper is allowed; it is an interop-contract change.

## Acceptance (when scheduled)

- `is \Closure` still uses `instanceof`.
- `is \Closure<callable<int, string>>` is either rewritten to a documented reflection helper or is a compile error directing users to `as`.
- Untyped PHP closures do not spuriously match a precise `TCallableShape`.
- Interop version bumped if and only if emit calls a new runtime helper.
