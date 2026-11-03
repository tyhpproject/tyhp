# Implementation Plan: Story 45 — Precompiled Tyhpdef Symbol Cache (Cold Start)

> **Roadmap position:** Story 45 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 05, 13, 21
> **New story:** split from Story 31 Idea 16 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration).
> Compiler-cache behavior: update `cli_lint.md` / `cli_build.md` (cache flags) and `faq_cli.md`.

> **Status:** Performance, not a language feature. Related to Story 31 Appendix A (binder Pass-2
> drain) but that optimization still *walks* stubs; this story avoids re-binding them.
> **Prerequisites:** Story 05 (binder symbol tables), Story 13 / 21 (how `tyhpdef/php` is loaded).
> **Source:** `LANGUAGE_ASSESSMENT.md` §4.5 (~12.6 s cold / ~0.4 s warm for a 2-file project against
> `tyhpdef/php` + runtime packages).

## Summary

Cold CLI is dominated by parsing and binding the stub corpus, not the user's two files. There is already an
on-disk **AST** cache (`--no-cache` on lint). Warm runs are fine; CI and first contact pay the full bind.

Ship (or generate on first install of `tyhpdef/php`) a **bound-symbol snapshot** keyed by package name +
version + compiler major + `output.phpVersion`, so `tyhp lint` of a hello-world project does not re-bind
~38K lines of tyhpdef. Invalidation is content-hash / version; a mismatch falls back to today's bind.

This is independent of Appendix A's eager-resolution pass and of Workstream 21.12 D/F. Measure first
(parse vs bind vs check) before picking the snapshot format.

## Decisions (defaults chosen)

1. **Measure first** — add a verbose timing breakdown (`parse` / `bind tyhpdef` / `bind user` / `check` /
   `emit`) before choosing a format. If parse dominates, AST cache completeness may be enough.
2. **Snapshot is compiler-private** — not a user-facing `.tyhpdef` format; may live next to the package
   or in the CLI cache dir.
3. **Invalidation** — package version + file hashes + compiler version + PHP target. Never reuse across
   those.
4. **Do not** require users to run a separate `tyhp cache warmup` for correctness; a missing snapshot
   just means a slow first run (today's behavior).
5. **Out of v1** — pre-binding user `overlay/` files (those change constantly).

## Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Stale snapshot after overlay edit | Hash overlays into the key; user overlays are not in the shipped snapshot |
| 2 | Snapshot format churn every compiler commit | Key includes compiler version; treat as cache, not an ABI |
| 3 | Memory vs time | Snapshot should be cheaper than re-bind; if it is not, do not ship it |

## Golden Fixtures / Tests (Acceptance)

- Timing probe: cold bind of `tyhpdef/php` with a snapshot vs without, recorded in a test that only
  asserts the snapshot path is used (not a wall-clock SLA in CI).
- Changing one overlay file misses the shipped snapshot and still type-checks correctly.
- `--no-cache` ignores the symbol snapshot as well as the AST cache.
