# Implementation Plan: Story 46 — Async HTTP, Filesystem, and Database I/O

> **Roadmap position:** Story 46 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 04, 08, 11
> **New story:** split from Story 31 Idea 17 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration).
> Update `tyhp_2600_asyncAndAwait.md`, the `tyhp/async` package, and `cli_interopContract.md` (new helpers).

> **Prerequisites:** Story 04 (`tyhp/async` event loop, `Promise`, Fibers), Story 08/11 (`async`/`await` emit).
> **Source:** `LANGUAGE_ASSESSMENT.md` §4.5 / §6.7. Locked onto this story 2026-09-15 (not 21.12).

## Summary

`tyhp/async` already runs a single-threaded loop (microtasks → timers → `stream_select` → Fiber resume).
PDO, `curl_exec`, and `file_get_contents` still **block** that loop. This story adds non-blocking **HTTP**,
**filesystem**, and **database** I/O so `await` has real I/O, not just timers.

v1 stays on **`tyhp/async`’s existing loop**. Do not rewrite the runtime onto Revolt/Amp as a prerequisite.
A Revolt adapter (or becoming a Revolt driver) is a later addendum if a consumer needs to share a loop with
Amp 3 / ReactPHP.

Ship as modules of `tyhp/async` (or thin `tyhp/async-http` / `tyhp/async-fs` / `tyhp/async-db` packages that
`require` `tyhp/async`), written in Tyhp, not hand-edited PHP.

## Decisions (defaults chosen)

1. **Loop** — v1 uses the current `EventLoop` / `stream_select`. One loop per process, as today.
2. **HTTP** — first-party client: DNS + TLS + `stream_select` sockets, `await`-able request/response, no
   blocking `curl_exec`. HTTP/1.1 is enough for v1; HTTP/2 is later.
3. **Filesystem** — non-blocking reads/writes/directory listing via the loop (chunked streams or worker
   Fibers that do not steal the loop with a long `fread` of a huge file). Document that some OS calls
   still block; do not pretend `unlink` is async if it isn’t.
4. **Database** — v1 is a **non-blocking driver for one stack** (pick at schedule time: `pgsql` non-blocking
   or `mysqlnd` async), plus a documented **do-not-call blocking PDO on the loop** diagnostic or FAQ.
   Wrapping all of PDO as async is out.
5. **Interop** — new public types bump `interopContractVersion` if they are in `tyhp/async`.
6. **Not in v1** — a full framework, an ORM, React/Amp compatibility shims.

## Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Blocking PDO accidentally used inside `async` | Docs (21.12 E) + a later lint if we can prove a call is blocking |
| 2 | TLS / DNS complexity | Start with HTTP client against a test server; do not boil the ocean on HTTP/2 |
| 3 | Two event loops if someone also loads Revolt | v1: unsupported; document “one loop.” Adapter is an addendum |
| 4 | Scope creep into “write Amp” | Three I/O surfaces, one loop, no framework |

## Golden Fixtures / Tests (Acceptance)

- `await httpGet(url)` returns a body without blocking the loop (a concurrent timer still fires).
- `await readFile(path)` on a fixture file; a planted blocking `file_get_contents` in an async function
  is **not** treated as the happy path (docs/test comments say it blocks).
- One DB round-trip on the chosen driver completes under `await` with the loop pumping.
- Existing `Promise` / timer tests stay green.
