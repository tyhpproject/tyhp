# Implementation Plan: Story 22 — Web Playground (Live `.tyhp` → PHP)

> **Roadmap position:** Story 22 — **Tier 2 — DX & Ecosystem** · **NEW (created during the roadmap restructure)**
> **Direct dependencies (new numbering):** 10 (`tyhp build` CLI), 12 (lint — JSON diagnostics format); 17 (sourcemaps, optional enhancement)
> **Renumbered from:** **NEW** — this story did not exist before the restructure; it adds a public, two-pane web playground.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** Roadmap cross-cutting requirement — "a web-playground story"
> **Branch:** TBD

---

## Architecture Overview

### Goal

A two-pane web page that makes Tyhp tangible in a browser:

- **Left pane:** an editable `.tyhp` source editor.
- **Right pane:** the live-generated PHP plus a diagnostics list (errors/warnings/info), updated as the user edits.

This is a learning/marketing/triage tool, not part of the compiler pipeline. The explicit design constraint is **the simplest implementation that works**: a thin backend that shells out to the existing `tyhp build` (and `tyhp lint --format json`) against a temporary file, rather than a WASM port or an in-browser compiler.

### Architecture (thin backend over the CLI)

```
Browser (two-pane SPA)
   │  POST /api/compile { source }
   ▼
Thin backend (PHP or minimal service)
   │  1. write source to a sandboxed temp dir as a one-file tyhp project
   │  2. run `tyhp lint --format json`  -> diagnostics
   │  3. run `tyhp build`               -> generated PHP (read from output dir)
   │  4. (optional) include sourcemap (Story 17) for line mapping
   ▼
JSON { php, diagnostics[], sourcemap? }  ->  rendered in the right pane
```

The backend reuses the machine-readable diagnostics contract from Story 12 (`--format json`, and SARIF where useful) so the playground never re-parses human text. It reuses `tyhp build`'s normal output writing (Story 10) and simply reads the emitted `.php` from the temp project's output path.

---

## Phase 1 — Backend Compile Endpoint

- Implement a single `POST /api/compile` endpoint that accepts `{ source, options? }`.
- Materialize a minimal, sandboxed temp project: a temp dir, a generated `tyhp.json` (canonical keys per `CONVENTIONS.md`: `include`, `output.path`, `locale`), and the posted source as one `.tyhp` file.
- Invoke the CLI:
  - `tyhp lint --format json` → parse diagnostics directly into the response.
  - `tyhp build` → read the generated PHP from `output.path`.
- Return `{ php, diagnostics[], timings }`. Always clean up the temp dir (`finally`), and never leak host paths into the response.

## Phase 2 — Sandboxing & Limits

- Enforce hard limits: max source size, wall-clock timeout per compile, no network/composer access, no filesystem access outside the temp dir, rate limiting per client.
- Run the CLI as a child process with the temp dir as CWD; treat any non-zero/garbled output as a structured "internal error" diagnostic rather than crashing the endpoint.
- Catch `\Throwable` at the boundary (project convention) and return a clean error payload.

## Phase 3 — Two-Pane Front End

- A single static page: source editor on the left (with `.tyhp` syntax highlighting), generated PHP + diagnostics on the right.
- Debounced auto-compile on edit (e.g. 400ms idle) calling `/api/compile`; show diagnostics with severity colors and click-to-jump to the source span (using diagnostic spans; line mapping refined by the optional sourcemap).
- Shareable permalinks (encode source in the URL) and a set of starter examples drawn from the conformance fixtures (Story 07).

## Phase 4 — Packaging & Deploy

- Document how to run the playground locally and how it is deployed alongside the doc site (`https://tyhplang.com/`, Story 30). Keep it a separate, optional component with no dependency from the compiler back onto the playground.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** The playground's starter examples are drawn from (and validated against) the conformance suite (Story 07), so a passing example in the suite renders identically in the playground.
- [ ] **Backend tests:** `/api/compile` returns correct `{ php, diagnostics }` for a known-good source and structured error diagnostics for a known-bad source; temp dirs are always cleaned up; limits/timeouts are enforced.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes (the playground depends on, and must not regress, the CLI it shells out to).
- [ ] **No host leakage:** Responses never contain absolute host paths; sandbox escape attempts are rejected.
- [ ] **Diagnostics registered centrally:** The playground consumes diagnostics; it never declares codes. Any new codes go only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`).
