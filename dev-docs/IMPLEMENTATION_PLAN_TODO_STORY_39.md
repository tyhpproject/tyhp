# Implementation Plan: Story 39 — Compiler Plugins v2

> **Roadmap position:** Story 39 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 08, 19.5, 23, 24, 38
> **New story:** split from Story 31 Idea 10 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration).

> **Status:** **v2** of [Story 38](IMPLEMENTATION_PLAN_TODO_STORY_38.md).
> Schedule only after v1 has shipped enough real plugins to validate demand.
> **Prerequisites:** Story 38 (v1 plugin host); Story 19.5 (VS Code extension) for IDE/TextMate pieces; Stories 23–24
> for optimizer hooks; Story 08 binder/checker maturity for post-binder transform + rebind.
> **Related:** brainstorm 2026-08-05 follow-ups (typed transform, IDE islands, fix-its, statements, sandbox, cache).

## Summary

Grow the plugin platform beyond v1’s pre-binder / compile-only / expr-only surface:

1. **`TypedAstTransform` (post-binder)** — full AST pass with symbols/types available; each mutation flags
   **needs rebind** or not; host rebinds when any participant requests it (combine former “typed transform” +
   “project/cross-file context” needs — binder scopes and project symbols are visible here).
2. **IDE / TextMate for `p` islands** — plugin ships a TextMate grammar (or injection) for its namespace; Story 19.5
   VS Code and PhpStorm clients match `p"ns:…"`, `p<<<ns`, etc., and apply highlighting; room for further LSP pickup
   (completion, hover) as the extensions evolve.
3. **Diagnostic spans + fix-its** — accurate offsets inside islands/encaps; suggested fixes consumable by
   `tyhp lint --fix` / IDE.
4. **Statement-form islands** — `p'ns:…';` / heredoc as statements, not only exprs.
5. **Sandbox policy** — **vital but under-designed**; constrain filesystem/network/process access for PHP plugin
   workers. Leave detailed policy open; design when scheduling v2.
6. **Shared plugin cache** — build-wide memo (DB schema, OpenAPI doc, etc.) loaded once per plugin per build.
7. **Optimizer hooks** — once Stories 23–24 exist, let plugins mark purity / forbid certain opts / contribute
   opt-aware metadata.

## Post-binder transform + rebind

```text
… → AstTransform (v1, pre-binder)
  → binder
  → ★ TypedAstTransform (v2) — full AST; may read types/symbols/project model
  → [optional rebind if any mutation flagged needsRebind]
  → Check → Emit …
```

- Plugin returns mutated tree plus **`needsRebind: bool`** (per invocation or per edit — exact API when scheduled).
- If any plugin in the round sets `needsRebind`, host runs binder again (bounded rounds; cycle/limit diagnostics).
- Prefer rebind-false edits when possible (annotations, metadata) to keep builds fast.
- Cross-file / project queries (implementors of an interface, route table, etc.) are available in this phase via
  the bound project model — not in v1 pre-binder `AstTransform`.

## IDE integration (Story 19.5)

- The plugin manifest (`extra.tyhp.plugin` in the plugin package's `composer.json`) may point at a **TextMate
  grammar** / injection scoped to its namespace; the path is relative to that `composer.json`, like other manifest
  paths.
- `tyhp-lang` (VS Code + PhpStorm) detect `p` / `p<<<` forms, read the namespace/label, and activate the matching
  grammar for the island body (encaps `$vars` can remain Tyhp/PHP scopes). Shared grammars from Story 19.5.
- Additional IDE surfaces (completion, hover, go-to for tables/columns) are desirable but unspecified in detail —
  capture as follow-ons once TextMate injection works.

## Diagnostic spans & fix-its

- Plugin diagnostics carry **source spans** mapped through encaps/heredoc bodies (not only the outer `p` token).
- Optional **fix-it** payloads (range + replacement text) for lint/IDE apply.

## Statement-form islands

- GrammarAddon on statement positions; same namespaces/quote/heredoc rules; typically side-effecting DSLs
  (`p'route:…';`, `p<<<fsm …`).

## Sandbox policy (open design)

Needs a real threat model: read project files vs arbitrary FS, network for schema pull, process spawn, time/memory
limits. v2 must ship *a* policy; exact defaults TBD when scheduled — do not pretend it is specified here.

## Shared plugin cache

Host-provided keyed cache for the build (`schema:default`, etc.) so SQL/OpenAPI plugins do not re-parse huge
artifacts per file. Invalidation rules TBD (file mtimes / config hash).

## Optimizer hooks

After optimizer modules exist: plugins may declare purity, freeze regions, or supply opt metadata so lowering and
opts do not fight. Exact API follows Stories 23–24 shapes.

## Explicitly still deferred (not in v2 either)

- **Multi-round generated `.tyhp` re-entering the compile pipeline** — considered during v1 design; **not
  rejected**; leave unplanned until a concrete product need appears. v2 side-file emit (PHP/JSON/OpenAPI) remains
  enough for foreseeable codegen.

## Decisions (defaults chosen) — v2

1. Post-binder transform + **rebind flag** rather than a separate “project analysis only” API.
2. TextMate-first IDE story via Story 19.5; richer LSP is incremental.
3. Statement islands after expr+heredoc have proven out in v1.
4. Sandbox is mandatory for v2 readiness but **spec is intentionally open**.
5. No `.tyhp` re-entry pipeline in v2 planning.

## Golden Fixtures / Tests (Acceptance) — v2

- TypedAstTransform sees resolved types; `needsRebind` true triggers a second bind; false does not.
- TextMate: fixture workspace with plugin grammar highlights `p<<<sql` body in VS Code extension tests.
- Fix-it applies via lint harness.
- Statement-form `p'…';` lowers / checks.
- Shared cache hit across two files in one build.
- Sandbox: at least one denied operation fixture once policy exists.
- Optimizer hook smoke once Stories 23–24 APIs exist.
