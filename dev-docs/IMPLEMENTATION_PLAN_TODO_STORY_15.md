# Implementation Plan: Story 15 — The Tyhp ↔ PHP Interop Contract (Written Down)

> **Status:** **COMPLETE** (acceptance choice B)
> **Acceptance choice:** Phases 1–4 + Story 15 goldens + contract-surface test; runtime self-host conformance remains **gated** until the Story 07 allowlist is green — this story feeds that milestone rather than blocking on it.
> **Roadmap position:** Story 15 — **Tier 1 — Usable** · **NEW (created during the roadmap restructure)**
> **Direct dependencies (new numbering):** 04 (runtime library modules), 06 (built-in types / tyhpdef surface), 09 (basic emitter); informed by 11 (emitter feature transformers) and 20 (tyhpdef generator)
> **Renumbered from:** **NEW** — this story did not exist before the restructure; it captures the compiler↔runtime boundary as an explicit, versioned contract.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** Roadmap cross-cutting requirement — "the interop contract written down"
> **Branch:** TBD
> **Canonical doc:** `docs/content/cli_interopContract.md` · pointer in `CONVENTIONS.md` §8

---

## Architecture Overview

### The Problem This Story Solves

The Tyhp compiler (C#) and the Tyhp runtime (committed PHP under `runtime/packages/{core,decimal,async,lambda}/`, written in Tyhp and compiled to PHP) are coupled by an **implicit ABI**: the emitter generates PHP that calls runtime classes by exact name/signature, and the runtime must provide exactly those shapes. Today that contract is spread across the emitter transformers (Story 11), the runtime sources (Story 04), and the tyhpdef surface (Story 06/20). When any side changes, the other can silently break.

This story writes the contract **down** as a single authoritative document and a machine-checkable surface, so both sides can evolve against a shared spec. It produces specification + conformance hooks, not new language features.

### Scope

The contract covers every place compiled Tyhp depends on a concrete runtime shape:

- **Synthetic dispatch naming** — the exact emitted names for operator-overload methods and extension-method/operator static dispatch. Current ABI uses single static names from `OperatorMethodNameGenerator` (`__add`, `__subtract`, `__multiply`, …; unary `__asNumeric` / `__negate` / …; convert `__from` / `__toString` / `__to{Type}`) plus `GeneratedNames` (`__tyhpGeneric`, `__initGenerics__tyhpGeneric`, `new_<MangledFqn>__tyhpGeneric`, `__get_/__set_<prop>__tyhpPropertyHook`). Extensions keep the method name and call as `ExtensionClass::method($receiver, …)`. Canonical ownership is listed in `CONVENTIONS.md` §8 and detailed in `docs/content/cli_interopContract.md` — do not resurrect obsolete `__OP_<Type>_<OP>_<Type>` spellings.
- **Type erasure rules** — how generics, type aliases, type guards, structs, and `internal` lower to PHP (what survives at runtime vs. what is erased).
- **Runtime entry points** — the public surface of `\Tyhp\*` the emitter is allowed to call: `Type`/`NamedType`/`GenericObject`, `PropertyAccessor`/`IsDisposable` (core); `Decimal` (decimal); `Promise`/`EventLoop`/`CancellationToken`/`DisposableScope` (async); `Expression`/`ExpressionNode`/`PropertyPath` (lambda). Emitter `RequirePackage` today: `tyhp/core`, `tyhp/async`; decimal/lambda remain required surface for direct/future calls.
- **Lowering protocols** — disposables (`:=` → `DisposableScope`), async/await (Promise/Fiber), `with`, null-conditional assignment, expression trees (Story 16 wiring).
- **tyhpdef surface** — what the runtime packages publish via their auto-generated `package.tyhp.json` (`include` globs → `_tyhpdef/` / `package.tyhpdef`) and the stability guarantees on those declarations.

---

## Phase 1 — Author the Interop Contract Document

- [x] Create `docs/content/cli_interopContract.md` (rendered into the Story 30 doc site) plus a concise canonical summary in `CONVENTIONS.md` §8.
- [x] For each runtime class the emitter calls, record: fully-qualified name and the emitter call pattern that produces them. State explicitly: **the emitter design dictates the runtime API, not vice versa** (consistent with Story 04 Phase 9, "Emitter integration contract").
- [x] Insert TOC entry after `cli_build.md`; note interop contract version in `VERSIONING.md`.

## Phase 2 — Lowering Reference (per feature)

- [x] Document the canonical PHP lowering for each Tyhp construct that needs runtime support (index in `cli_interopContract.md`), with cross-links to story11 / story14_5 goldens and `tests/conformance/story15/` index suite. The doc stays an index, not a duplicate of emitter internals.
- [x] Add thin Story 15 golden fixtures under `tests/conformance/story15/` (and update conformance README).

## Phase 3 — Versioning & Compatibility Policy

- [x] Specify compatibility rules in docs/`VERSIONING.md`/`CONVENTIONS.md`: additive = optional minor; emitted name/signature change = bump `interopContractVersion` on both sides; PHP matrix coordinated with Story 21.
- [x] Stamp `extra.tyhp.interopContractVersion` on runtime package `composer.json` files (+ dist via `build-common.sh`); add `InteropContract.CurrentVersion` (= 1); enforce mismatch as **TYHP5018** (error) in `MessageCode.cs` + resx.

## Phase 4 — Machine-Checkable Surface (anti-drift)

- [x] Document the enumerable "emitter-required runtime symbols" surface (`InteropContractSurface`) and that self-host (Story 07) consumes it when green.
- [x] Implement `InteropContractSurface` + contract-surface test against committed `package.tyhpdef` (preferred over gitignored `dist/` / mid-reorg `src/`).

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.
>
> **Acceptance choice (locked):** Phases 1–4 + goldens + contract-surface test. Self-host remains gated on Story 07.

- [x] **Golden fixtures:** Thin Story 15 index fixtures under `tests/conformance/story15/` (operators, extensions, generics erasure, structs, disposables, async, expression trees). The committed `.php` is the source of truth for the emitted contract.
- [x] **Contract-surface test:** `InteropContractSurfaceTests` asserts every required symbol exists in the matching package `package.tyhpdef`; version-stamp tests cover all four packages.
- [x] **Conformance run green:** Story 15 fixtures + Interop tests pass (within the Phases 1–4 + goldens + surface-test acceptance choice).
- [ ] **Runtime self-host conformance:** Recompiling the Tyhp runtime sources and diffing against the committed `runtime/` PHP satisfies the contract surface (the "compiler builds its own runtime" milestone — see Story 07). **Gated** until Story 07 allowlist is green; Story 15 feeds the surface list but does not wait on full self-host.
- [x] **Diagnostics registered centrally:** Contract-version mismatch **TYHP5018** (`EmitterInteropContractMismatch`) added only in `Tyhp/Domain/Exceptions/MessageCode.cs` with matching resx + regenerated `diagnostics_reference.md`.
