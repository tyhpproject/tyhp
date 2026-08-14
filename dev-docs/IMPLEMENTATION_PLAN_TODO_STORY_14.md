# Implementation Plan: Story 14 — Error Message Quality (First-Class Feature)

> **Roadmap position:** Story 14 — **Tier 1 — Usable** · **NEW (created during the roadmap restructure)**
> **Direct dependencies (new numbering):** 01 (diagnostic system + MessageCode), 08 (checker — primary diagnostic producer), 12 (lint output formats)
> **Renumbered from:** **NEW** — this story did not exist before the restructure; it elevates diagnostic quality to a first-class deliverable.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** Roadmap cross-cutting requirement — "error-message quality as a first-class feature"
> **Branch:** TBD

---

## Architecture Overview

### Why This Is Its Own Story

By the end of Tier 0 the compiler produces diagnostics from every phase (parser, visitor, binder, checker, emitter) through the unified `DiagnosticBag`/`IDiagnostic` system (Story 01). Those diagnostics are *correct* but not yet *excellent*: messages vary in tone, many lack source spans/underlines, few offer actionable fixes, and there is no consistency gate. A compiler's developer experience is dominated by the quality of its error messages — this story treats that quality as a product feature with its own acceptance bar, rather than an afterthought scattered across phases.

This story does **not** invent new diagnostic codes for language features (those belong to the stories that own them). It standardizes **how** every diagnostic is authored, rendered, and tested, and upgrades the existing catalog to that bar.

### Single Source of Truth

All diagnostic codes live in `Tyhp/Domain/Exceptions/MessageCode.cs` and their localized text in `Resources/CLI.TyhpHostedService.en-US.resx` (keys `ERROR_TYHP{code}` / `WARNING_TYHP{code}` / `INFO_TYHP{code}`), resolved via `Tyhp/CLI/Message.cs`. This story adds **authoring rules and rendering quality** on top of that machinery — it must not fork the code registry. See `CONVENTIONS.md`.

---

## Phase 1 — Diagnostic Message Style Guide & Catalog Audit

> **Status:** COMPLETED

- [x] Author a short, enforced message style guide (lives in `CONVENTIONS.md` → "Diagnostic message style"): present tense, no trailing period on the short message, name the offending symbol/type in backticks, never blame the user, prefer "expected X, found Y" framing.
- [x] Audit every existing `.resx` entry against the guide and rewrite non-conforming text. Do **not** renumber any codes.
- [x] Define the message anatomy: `code` · `severity` · `primary span` · `short message` · optional `labels` (secondary spans) · optional `help`/`note` · optional `suggestion`.

## Phase 2 — Rich Source Spans & Underlines (Rustc-style rendering)

> **Status:** COMPLETED

- [x] Extend `IDiagnostic` rendering so a diagnostic can carry a primary span plus zero or more labeled secondary spans (reusing `Base2Ast.Line/Column` and the end-position properties).
- [x] Add a console renderer in `Tyhp/CLI/` that prints the offending source line with a caret/underline under the span and inline labels, with color via the existing `Message`/`ConcurrentWriter` path. Degrade gracefully to single-line output when `quiet` is set.
- [x] Wire the renderer into `BuildAction`/`LintAction` text output. The JSON/SARIF formats (Story 12) already carry span data — extend their schema to include secondary labels and suggestions.

## Phase 3 — Suggestions & "Did You Mean"

> **Status:** COMPLETED

- [x] Add an optional `suggestion` (machine-applicable edit: span + replacement text) to `IDiagnostic`.
- [x] Provide a small Levenshtein-based "did you mean" helper for the binder/checker to attach to "unknown symbol/type/member" diagnostics, sourced from the in-scope symbol table.
- [x] Surface suggestions in text output (as `help:` hints) and in JSON/SARIF (as `fixes`), so they can later drive `tyhp lint --fix` (Story 12) and LSP code actions (Story 19) — those consumers are out of scope here but the data contract is defined here.

## Phase 4 — Help Text, Error Index & Explain Command

> **Status:** COMPLETED

- [x] Reserve a documentation slot per code in the `docs/` content set (the doc site of Story 30) and add a stable `--explain TYHP{code}` CLI affordance that prints the long-form explanation for a code.
- [x] Generate an error-code index from `MessageCode.cs` (read dynamically — never hardcode the list) so docs and the explain command stay in sync.

## Phase 5 — Consistency Gate (anti-drift for messages)

> **Status:** COMPLETED

- [x] Add a build-time/test-time check that fails if: a `MessageCode` lacks a `.resx` entry, a `.resx` entry references a non-existent code, or a message violates the style guide's mechanical rules (trailing period, missing backticks around interpolated identifiers, empty help).
- [x] This gate is the message-quality analogue of the diagnostic-code single-source-of-truth rule in `CONVENTIONS.md`.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [x] **Golden fixtures:** Add `.tyhp` inputs that intentionally trigger representative diagnostics, with golden **expected-diagnostic** snapshots (code, severity, primary/secondary spans, suggestion) to the conformance suite (Story 07). The committed snapshots are the source of truth for rendered output. *(Phase 5: `tests/conformance/story14/diagnostics/` covers codes 1002 / 3020 / 4008 via manifest expectations; rich span/suggestion snapshots remain in unit tests under `tests/Tyhp.Tests/Diagnostics/`.)*
- [x] **Unit / integration tests:** Cover the span renderer, the "did you mean" helper, the suggestion data contract, and the `--explain` command. *(Phase 4: `--explain` / catalog / docs-index sync tests added; earlier phases covered renderer + DidYouMean; Phase 5: `MessageConsistencyGateTests`.)*
- [x] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done. *(`Category=Conformance` and `Category=Diagnostics` are green — 147/147 on 2026-07-27, including the three new fixtures. The full `dotnet test tyhp.sln` run is 867/879 with 10 failures that pre-date this story and are unrelated to it: fixtures in `WithKeywordEmitterTests` / `StructEmitterTests` / `DisposableFinishEmitterTests` / `TraitRequirementEmitterTests` / `Phase08_5RuleTests` declare symbols that collide with PHP builtins and now trip `BinderDuplicateSymbolDeclaration` (3002) — tracked in `FOUND_BUGS.md` under the Story 12 Phase 6 audit.)*
- [x] **Consistency gate green:** Every `MessageCode` has a conforming `.resx` entry and vice versa (Phase 5 check passes).
- [x] **Diagnostics registered centrally:** No new codes are introduced here; any future codes go only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`).
