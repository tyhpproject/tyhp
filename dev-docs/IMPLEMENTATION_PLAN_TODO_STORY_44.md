# Implementation Plan: Story 44 — Opt-in Runtime Checks for Erased Tyhp Contracts

> **Roadmap position:** Story 44 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 04, 08, 09, 11, 15
> **New story:** split from Story 31 Idea 15 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration).
> A runtime assert helper plus emit at public boundaries: update `tyhp_0400_structs.md`,
> `cli_interopContract.md`, `cli_build.md` (new config key), and the guide's `handbook/07-runtime-api.md`.

> **Prerequisites:** Story 08 (struct shapes / generic bounds already checked in Tyhp), Story 09/11 (emit at
> parameter/return boundaries), Story 04 (`tyhp/core` helper), Story 15 (interop bump if a new helper is
> emitted). Complements Story 22.1 Part A (docblocks are static; this is runtime).
> **Source:** `LANGUAGE_ASSESSMENT.md` §4.3. Two-world discussion in `DESIGN_OPEN_QUESTIONS.md` §16.

## Summary

Every Tyhp-only guarantee that erases — struct shapes, generic bounds, the future `sealed` and refined types — is
advisory at the PHP boundary. A PHP caller can pass `['cents' => 'abc']` into a `Money` parameter and PHP will
accept it. Story 22.1 docblocks tell PHPStan; this story tells the running program, **when the library author opts in**.

v1 is **struct-shaped parameters and returns on public/protected methods and functions** that PHP can call
(emitted symbols, not `internal`). A `tyhp/core` helper receives the value plus a `\Tyhp\Type::struct(...)`
descriptor (already emitted for `typeof` / Mechanism C) and throws `\Tyhp\TypeError` (or a dedicated exception)
on missing keys, extra required-schema failures, or wrong scalar kinds. Generic class bounds and `sealed` are
out of v1.

This is the same product shape as `build.runtimeGenericChecks`: off by default, on for libraries that want a
loud PHP-side failure instead of a silent shape mismatch.

## Decisions (defaults chosen)

1. **Opt-in** — `build.runtimeStructChecks` (provisional; register in `CONVENTIONS.md` §4 when scheduled), default
   `false`. No per-parameter attribute in v1 (an inverse "skip this parameter" attribute is a later addition).
2. **Public PHP-callable surface only** — public/protected functions and methods. Private / `internal` skipped.
3. **Structs only in v1** — not generic `T extends Foo`, not `sealed`, not refined types.
4. **Throw, do not coerce** — wrong shape is an error, not a cast.
5. **Descriptor reuse** — `\Tyhp\Type::struct` already exists; do not invent a second schema format.
6. **Interop** — new helper is an `interopContractVersion` bump.

## Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Cost on every public call | Default off; only libraries that care turn it on |
| 2 | Extra keys vs structural typing | Match the checker: extra keys allowed; missing required keys fail |
| 3 | Nested structs / arrays of structs | Recurse using the same `Type` descriptor |
| 4 | Overlap with Mechanism C property set checks | Those guard *generic* properties on tracked instances; this guards *struct arrays* at the PHP boundary. Different slot. |

## Golden Fixtures / Tests (Acceptance)

- Public `function pay(Money $m)` with checks on: PHP `pay(['cents' => 1, 'currency' => 'USD'])` succeeds;
  `pay(['cents' => 'x'])` throws; `pay(1)` throws.
- Checks off: the same bad array is accepted by PHP (Tyhp callers still type-check).
- Nested `array<Money>` validates each element.
- `internal` / private methods are not wrapped.
