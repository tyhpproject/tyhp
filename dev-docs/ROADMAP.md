# Tyhp Implementation Roadmap

> **What this is:** the single ordered index the author follows top-to-bottom. Stories live in
> `IMPLEMENTATION_PLAN_TODO_STORY_NN.md`, renumbered into clean, zero-padded, dependency-ordered, tiered sequence
> (`01`–`30`, no gaps). Conventions (diagnostic codes, config keys, paths) are centralized in `CONVENTIONS.md`.
>
> **Restructure note:** this is a **reorganization + renumbering + a few targeted additions**, not a rewrite of
> technical substance. MessageCode allocations, API signatures, and paths were preserved; only ordering, numbering,
> tier framing, cross-references, and three new stories changed.

---

## The Tiered Scheme (rationale)

The stories are organized around a **thin vertical slice that compiles & runs end-to-end as early as possible**,
testing-first, with anti-drift guardrails, deferring breadth:

- **Tier 0 — Spine:** the minimum to make a real program compile and run: bootstrap → lexer/parser/binder
  foundations → runtime core/builtins → **the test/fixture/conformance harness (pulled forward to the front as the
  backbone)** → the checker → a basic emitter → build/CLI.
- **Tier 1 — Usable:** full emitter feature transformers, lint, CLI polish, plus two new first-class concerns —
  error-message quality and the written interop contract — the **expression-tree wedge showcase**, and
  **callable signature utilities** (Story 16.5) for typed `call_user_func*` / higher-order builtins.
- **Tier 2 — DX & Ecosystem:** LSP, sourcemaps + xdebug proxy, the new web playground, and tyhpdef generation from
  PHP reflection + the PHP-version matrix (with the baseline+overlay regeneration pattern).
- **Tier 3 — Advanced:** optimizer passes, remaining advanced language features, the reflection API, and final
  documentation/polish.

### Flagships

1. **The type checker (Story 08).** The flagship correctness engine. It sits on the spine because nothing validates
   without it. It is the largest, most prominent story.
2. **The query-builder / expression-tree "wedge" (Story 16 — Parsable Lambdas).** The marquee feature that makes
   Tyhp worth adopting (LINQ-to-SQL-style expression trees → ORMs/query builders). It was previously sequenced last
   (it declared "ALL prior stories" as prerequisites). It has been **lifted forward to the end of Tier 1**, the
   earliest point where its real dependencies — a mature checker (08), the emitter feature transformers (11), and
   the `tyhp/lambda` runtime (04) — are satisfied. It is labeled the explicit showcase.

### Cross-cutting guardrails baked in

- **Testing-first:** the conformance harness (Story 07, formerly Story 11) is pulled to Tier 0 as the backbone, and
  every story now carries a uniform **"Golden Fixtures / Tests (Acceptance)"** subsection.
- **Anti-drift:** `CONVENTIONS.md` names `Tyhp/Domain/Exceptions/MessageCode.cs` as the single source of truth for
  diagnostic codes and centralizes canonical config keys and paths; every story header cites it.
- **Bootstrap / self-host:** because the author hand-maintains both the Tyhp runtime source and the committed PHP
  runtime, a **runtime self-host conformance check** (recompile the Tyhp runtime, diff against committed PHP) is part
  of the conformance suite. "The compiler builds its own runtime" is a tracked milestone. As packages move to
  `dist/` and eventually a separate repo / Packagist, self-host golden baselines and emit-and-run test helpers must
  follow the phasing under Tier 2 (Story 21 note) — not hardcode forever-on-disk `runtime/packages/*/src`.

---

## The Sequence (follow top-to-bottom)

### Tier 0 — Spine (a real program compiles & runs)

| New | Story | Direct deps |
|-----|-------|-------------|
| **01** | Foundation (diagnostics, compilation pipeline, build endpoint) | — |
| **02** | Binder (name resolution & scope building) | 01 |
| **03** | Extension operator overloads & tyhpdef inline extensions | 02 |
| **04** | Tyhp runtime library modules (core/decimal/async/lambda) | 02, 03 |
| **05** | Bind symbols to AST nodes | 02, 03, 04 |
| **06** | Built-in types, grammar fixes & compiler infrastructure *(incl. optional open tags / tagless source mode — Phase 7)* | 01–04 |
| **07** | **Testing Infrastructure & conformance harness** *(pulled forward — backbone; two waves — see below)* | 01 (+ exercises later stories incrementally) |
| **08** | **Checker (type checking & validation)** — *FLAGSHIP* | 01, 02, 03, 05, 06, **07 Wave A** |
| **08.5** | **Symbol-name types** *(checker feature — split from Story 31, additive)* | 06, 08 |
| **09** | Emitter (basic PHP output) | 01, 02, 03, 08 |
| **10** | Build Action (wire everything together) | 01, 02, 04, 05, 06, 08, 09 |
| **10.5** | **Deferred correctness & quality fixes** *(remediation sub-story — closes deferred `FOUND_BUGS.md` items)* | 08, 08.5, 09, 10 |

**Story 07 — implement in this order** (detail in `IMPLEMENTATION_PLAN_TODO_STORY_07.md`). Story 07 stays *before* Story 08 so the checker ships with a harness; later phases are authored early but activated as their story lands (`PLACEHOLDER_STORY_*` skips until then).

| Wave | When | Story 07 phases (in order) | Outcome |
|------|------|---------------------------|---------|
| **A** | **Complete before starting Story 08** | **1** test project & helpers → **2** parser fixtures → **3** parser edge cases & AST → **4** diagnostics → **5** binder → **5A** conformance harness (`tests/conformance/` runner + Story 06 tagless `manifest.json`) | `dotnet test` green for parser/diagnostics/binder/conformance; lint-level fixtures automated |
| **A2** | Parallel with Wave A (after Phase 1) | **9** PHPUnit runtime tests (+ optional .NET `Category=PHP` wrapper) | `runtime/` package tests runnable from CI-local workflow |
| **B** | Activate as each dep story lands | **6** checker tests → **08** · **7** emitter/E2E snapshots (basic pass-through → **09**; feature transforms → **11**) · **8** integration/build → **10** · **self-host** runtime diff → green after **10** | Full pipeline & golden `.tyhp → .php` fixtures; “compiler builds its own runtime” milestone |
| *(deferred)* | Outside Tier 0 spine | Story 07 **Phase 10** — CI/CD (GitHub Actions, coverage gates) | Automated CI — add when ready |

### Tier 1 — Usable

| New | Story | Direct deps |
|-----|-------|-------------|
| **11** | Emitter feature expansion | 05, 09 |
| **12** | Lint action | 01, 02, 05, 06, 08, 10 |
| **13** | CLI polish (help, init, version, composer) | 10 |
| **14** | **Error-message quality (first-class feature)** — *NEW* | 01, 08, 12 |
| **14.5** | **PHP 8.5 syntax surface + lowering** (`805.0.0`) *(additive — pipe, void cast, clone-with, 8.4 parse holes, exit/clone tyhpdefs)* | 06, 08, 09, 10, 11 |
| **15** | **The Tyhp ↔ PHP interop contract (written down)** — *NEW* | 04, 06, 09 |
| **16** | **Parsable lambdas (expression trees)** — *FLAGSHIP wedge showcase, lifted from Tier 3* | 08, 11, 04 |
| **16.5** | **Callable signature utilities** (`__CallableParametersStruct` / `__CallableParametersTuple` / `__CallableReturnType`) *(additive — TS-style `Parameters`/`ReturnType` for callables)* | 08, 08.5, 11 |

### Bug fixes break

Look at FOUND_BUGS.md and fix as many as possible.

### Tier 2 — DX & Ecosystem

| New | Story | Direct deps |
|-----|-------|-------------|
| **17** | Sourcemap generation | 01, 09 |
| **18** | XDebug proxy | 17 |
| **19** | Language Server (LSP) | 01, 02, 08, 10 |
| **19.5** | **VS Code extension (`vscode-tyhp`)** *(additive — TextMate, LSP client, XDebug proxy UX, tasks, icons, status bar, workspace/init)* | 17, 18, 19 |
| **20** | Tyhpdef generator (C# CLI integration) | 01, 02 |
| **20.5** | **PHP version gating** (`declare(php=…)` + `#[\Tyhp\Php]`) *(additive — enables single-package stubs)* | 04, 06, 08, 09, 10, 11 |
| **21** | PHP extension Composer packages (`tyhp/php` + `tyhp/php-ext-*`) | 06, 20, **20.5**, 28† |
| **22** | **Web playground (live `.tyhp` → PHP)** — *NEW* | 10, 12, 17 |

> **Deferred: runtime-package distribution & versioning (→ Story 21).** The **full** Tyhp runtime-package
> distribution + versioning — publishing `tyhp/core` · `tyhp/decimal` · `tyhp/async` (and the `tyhp/php` /
> `tyhp/php-ext-*` extension packages) via a **published Packagist source** with proper version constraints — is
> deferred to **~Story 21**. Until then, an **interim local-source** inclusion is being implemented now: the
> generated `composer.json` gains Composer **`path` repositories** pointing at `runtime/packages/` so
> `composer install` resolves the runtime packages from the local checkout. This unblocks the Story 10 build
> pipeline's `composer install` step (see `FOUND_BUGS.md` — the runtime-package `require` constraint /
> unresolvable-`composer install` items, marked partially resolved via the interim fix) without committing to the
> published distribution model, which remains Story 21's responsibility. Story **20.5** supplies the
> `declare(php=…)` / `#[\Tyhp\Php]` gating language that lets Story 21 ship **one** stubs tree for all supported
> PHP minors instead of per-minor Composer packages.
>
> **Runtime packages: test consumption & self-host phasing (decided 2026-08-12).** Compiled package PHP now lives
> under `runtime/packages/dist/<pkg>/<version>/src` (not unversioned `runtime/packages/<pkg>/src`). Longer term,
> runtime packages are expected to move to their **own repo** and be consumed via **Composer** (path repos today,
> Packagist after Story 21). Compiler / test helpers must not permanently hardcode in-tree package paths.
>
> | Phase | When | What |
> |-------|------|------|
> | **1 — Now (in-tree `dist/`)** | Unblock suite reds | `EmittedPhpRunner`: resolve autoload root by scanning `runtime/packages/dist/tyhp-core/` for the newest `805.*` (tip MAJOR) and use `…/src/Tyhp`; fail clearly if missing/empty (e.g. after `--clean`). Optional better stopgap: path-repo `composer install` → load via `vendor/tyhp/core`. Self-host: either retarget golden diff at `dist/…/src` **or** keep skipped / lightly rewrite until packages have a clear home — do **not** restore unversioned `packages/*/src` for tyhpdefs (project `include` / `package.tyhpdef` already covers typechecking). |
> | **2 — Composer-shaped consumption** | Before / as packages leave this repo | Prefer resolving `tyhp/core` (and siblings) the same way real apps do: Composer path → `vendor/…`. Emit-and-run tests stop knowing about `dist/` layout details. |
> | **3 — Separate runtime repo / Packagist (~Story 21+)** | Packages published or moved out | **Self-host golden** (“recompile `tyhp_src`, diff committed PHP”) lives in the **runtime** repo (or its CI), pinning a compiler version — not as a forever in-tree compiler-suite check against local `dist/`. Compiler-repo tests consume published or path-installed packages only. |
>
> Open tracking: `FOUND_BUGS.md` — `EmittedPhpRunner` path + runtime self-host committed-output layout.

### Tier 3 — Advanced

| New | Story | Direct deps |
|-----|-------|-------------|
| **23** | Compiler optimizer (MVP) | 03, 08, 09 |
| **24** | Advanced optimizations | 23 |
| **25** | `internal` visibility modifier | all earlier (01–24) |
| **26** | Null-conditional chaining with assignment | all earlier (01–25) |
| **27** | `new<TArgs...>` constructable object type | all earlier (01–26) |
| **28** | Generic type parameter defaults | all earlier (01–27) |
| **29** | Tyhp reflection API (sourcemap-backed) | 17, 23, 04, 20, 03, 19, 25, 26, 27, 28 |
| **30** | Documentation & polish (final capstone) | all earlier (01–29) |

† See "Judgment calls" — Story 21 has a forward dependency on Story 28.

### Tier 4 — Future plans

> Large, cross-cutting work sequenced **after** the features it builds on. Not part of the contiguous `01`–`30`
> spine; queued for after the capstone.

| New | Story | Direct deps |
|-----|-------|-------------|
| **31** | **Future Ideas & Optimizations** (collection; Idea 1 = Tyhp Link; Ideas 9–10 = compiler plugins v1/v2) — *NEW* | 06, 08, 08.5, 09, 11, 13, 15, 17, 18, 19.5†, 23, 24, 25 |

**Story 31 — Future Ideas & Optimizations.** A collection of future ideas/optimizations, each liftable into its own
story when scheduled. **Idea 1 — Tyhp Link:** a build-time *linker* + tiered runtime *loader* that beats Composer autoloading by using the
checker's full type graph: a three-tier model (`opcache.preload` → Tyhp fast loader → Composer PSR-4 fallback), dual
emission (readable PSR-4 for debug + optimized topo-sorted bundles for release), function/constant lowering
(`internal`-driven, `[GlobalFunction]` opt-out), a `[Preload]` root-set attribute, and the **emit-time
canonicalization + lowering/relocation invariant** that make string-based symbol references safe to
canonicalize/relocate. The **symbol-name types themselves** (`__ClassName`, `__FunctionName`, …) were split out into
**Story 08.5** (they are a linker-independent checker feature); Story 31 consumes them. Later ideas in the same
doc include scalar-conversion contracts, default interface implementations, static-analysis docblocks,
`operator default()`, no-uninitialized-storage, conditional types (v1/v2), **Idea 9 — compiler plugins v1**
(Tyhp/PHP host; Composer packages + `plugin.tyhp.json`; project/global discovery; pre-binder `AstTransform` /
`Check` / `Emit`; `p'…'` / `p"…"` / `p<<<` islands; backtick ops; process order; options schema; test harness),
and **Idea 10 — compiler plugins v2**
(post-binder transform + rebind; TextMate/VS Code island highlighting via Story 19.5; fix-its; statement islands;
sandbox TBD; shared cache; optimizer hooks). Detail in `IMPLEMENTATION_PLAN_TODO_STORY_31.md`.

† Idea 10’s IDE/TextMate piece depends on Story 19.5; Idea 9 does not.

---

## Old → New mapping

| Old | New | Story | Tier |
|-----|-----|-------|------|
| 0 | **01** | Foundation | 0 |
| 1 | **02** | Binder | 0 |
| 1.2 | **03** | Extension operator overloads & tyhpdef inline extensions | 0 |
| 1.5 | **04** | Tyhp runtime library modules | 0 |
| 1.6 | **05** | Bind symbols to AST nodes | 0 |
| 2 | **06** | Built-in types, grammar fixes & compiler infrastructure | 0 |
| 11 | **07** | Testing infrastructure & conformance harness *(moved forward)* | 0 |
| 3 | **08** | Checker *(flagship)* | 0 |
| 4 | **09** | Emitter (basic) | 0 |
| 6 | **10** | Build action | 0 |
| 8 | **11** | Emitter feature expansion | 1 |
| 7 | **12** | Lint action | 1 |
| 13 | **13** | CLI polish | 1 |
| — | **14** | Error-message quality *(NEW)* | 1 |
| — | **14.5** | PHP 8.5 syntax surface + lowering (`805.0.0`) *(NEW, additive)* | 1 |
| — | **15** | Interop contract *(NEW)* | 1 |
| 16 | **16** | Parsable lambdas / expression trees *(wedge — lifted to Tier 1)* | 1 |
| — | **16.5** | Callable signature utilities *(NEW, additive)* | 1 |
| 9 | **17** | Sourcemap generation | 2 |
| 14 | **18** | XDebug proxy | 2 |
| 12 | **19** | Language Server (LSP) | 2 |
| — | **19.5** | VS Code extension (`vscode-tyhp`) *(NEW, additive)* | 2 |
| 10 | **20** | Tyhpdef generator | 2 |
| — | **20.5** | PHP version gating (`declare(php=…)` / `#[\Tyhp\Php]`) *(NEW, additive)* | 2 |
| 23 | **21** | PHP extension Composer packages (`tyhp/php` + `tyhp/php-ext-*`) | 2 |
| — | **22** | Web playground *(NEW)* | 2 |
| 4.5 | **23** | Compiler optimizer (MVP) *(moved to Tier 3)* | 3 |
| 4.6 | **24** | Advanced optimizations *(moved to Tier 3)* | 3 |
| 17 | **25** | `internal` visibility | 3 |
| 19 | **26** | Null-conditional chaining with assignment | 3 |
| 20 | **27** | `new<TArgs...>` constructable type | 3 |
| 21 | **28** | Generic type parameter defaults | 3 |
| 22 | **29** | Tyhp reflection API | 3 |
| 99 | **30** | Documentation & polish | 3 |

## New → Old mapping (inverse)

| New | Old |
|-----|-----|
| 01 | 0 |
| 02 | 1 |
| 03 | 1.2 |
| 04 | 1.5 |
| 05 | 1.6 |
| 06 | 2 |
| 07 | 11 |
| 08 | 3 |
| 09 | 4 |
| 10 | 6 |
| 11 | 8 |
| 12 | 7 |
| 13 | 13 |
| 14 | *(NEW)* |
| 14.5 | *(NEW)* |
| 15 | *(NEW)* |
| 16 | 16 |
| 16.5 | *(NEW)* |
| 17 | 9 |
| 18 | 14 |
| 19 | 12 |
| 19.5 | *(NEW)* |
| 20 | 10 |
| 20.5 | *(NEW)* |
| 21 | 23 |
| 22 | *(NEW)* |
| 23 | 4.5 |
| 24 | 4.6 |
| 25 | 17 |
| 26 | 19 |
| 27 | 20 |
| 28 | 21 |
| 29 | 22 |
| 30 | 99 |

> The new sequence is **contiguous `01`–`30`** with additive sub-stories (`08.5`, `10.5`, `14.5`, `16.5`, `19.5`, `20.5`) inserted where needed.
> Two numbers happen to be unchanged (old 13 → 13, old 16 → 16).

---

## NEW stories created during the restructure

- **Story 14 — Error-message quality (Tier 1):** diagnostic quality as a product feature — style guide, rich
  source spans/underlines, "did you mean" suggestions, `--explain`, and a message-consistency gate. Codes still live
  only in `MessageCode.cs`.
- **Story 14.5 — PHP 8.5 syntax surface + lowering (Tier 1, additive — inserted after Story 14):** close remaining
  PHP 8.4 parse holes (abstract/interface property-hook `;`, attributes on hooks, full `exit`/`die` argument lists),
  add PHP 8.5 syntax (pipe `|>`, `(void)` cast, `clone(…)` / clone-with, attributes on top-level `const`), declare
  `exit`/`die`/`clone` in tyhpdef for signatures while keeping keyword forms in the grammar, rewrite for lower
  `output.phpVersion`, and bump the compiler to **`805.0.0`**. Detail in `IMPLEMENTATION_PLAN_TODO_STORY_14.5.md`.
- **Story 15 — Interop contract (Tier 1):** the Tyhp ↔ PHP boundary written down — emitter synthetic-dispatch
  naming, type-erasure/lowering rules, the runtime entry-point surface, versioning, and a machine-checkable
  contract surface that feeds the self-host conformance check.
- **Story 22 — Web playground (Tier 2):** a two-pane page (editable `.tyhp` left; live PHP + diagnostics right).
  Simplest implementation: a thin backend that shells `tyhp build` / `tyhp lint --format json` on a sandboxed temp file.
- **Story 19.5 — VS Code extension (Tier 2, additive — inserted after Story 19):** first-party
  `vscode-tyhp/` client for Stories 17–19 — TextMate highlighting, LSP client for `tyhp language_server`,
  XDebug-proxy debug wiring, tasks, file icons, status bar, and workspace/`tyhp.json` awareness (including
  `tyhp init`). Binary discovery (PATH → setting), download install (global or extension-local), and
  extension-only auto-update / pin. Packageable VSIX only — **no** Marketplace submit in this story; Cursor
  compatible but not a separate deliverable. Detail in `IMPLEMENTATION_PLAN_TODO_STORY_19.5.md`.
- **Story 20.5 — PHP version gating (Tier 2, additive — inserted after Story 20):** compile-time
  `declare(php="…")` (Composer constraint strings) and `#[\Tyhp\Php(string $version)]` so Story 21 can ship a
  single `tyhp/php` (+ `tyhp/php-ext-*`) stubs package instead of per-minor forks. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_20.5.md`.
- **Story 10.5 — Deferred correctness & quality fixes (Tier 0, additive — inserted after Story 10):** a focused
  remediation pass that pulls forward thirteen correctness/robustness items discovered during the Story 07–10 audits
  and deliberately deferred at the time (logged in `FOUND_BUGS.md`). It groups them by subsystem — checker
  (type-guard return-`bool` validation, negative/`else`-branch narrowing, utility-type generic-constraint
  enforcement, the `Readonly<T>` resolver, the extension-method `public static` modifier policy, the template-string
  membership size guard + `maxStates` config wiring), emitter (right-operand-aware operator-overload resolution,
  proper type→PHP alias spelling, generalized wrapped-conditional class detection), build pipeline (incremental
  missing-output detection, accurate per-phase error counts), and test infra (the `tests/conformance/story08_5/`
  golden fixtures, now producible since the emitter landed, and a non-masking self-host allowlist). It resolves the
  standing `PLACEHOLDER_STORY_10` (template-string config) and `PLACEHOLDER_STORY_11` (operator overload rewriting)
  markers. No new language surface. Detail in `IMPLEMENTATION_PLAN_TODO_STORY_10.5.md`.
- **Story 08.5 — Symbol-name types (Tier 0, additive — split from Story 31):** the linker-independent "Part A" of the
  symbol-name types — the `__ClassName`/`__FunctionName`/… built-in type definitions (all erasing to `string`),
  type-guard narrowing (`\class_exists` → `__ClassName`, …), compile-time existence verification on literal
  assignment, and typed `nameof()`. Depends only on Story 06 (built-in type surface) and Story 08 (checker), both
  done. Story 31 keeps the linker-specific "Part B" (emit-time canonicalization + the lowering/relocation invariant).
  The struct/type utilities + type-level `__As*` land with it (Phase 5). It also introduces **template string types**
  (Phase 6) — a general first-class feature: types denoting sets of strings via literal text, `${T}` interpolation
  holes, and regex-style quantifiers (`+ * ? {n} {n,} {,m} {n,m}`), modeled as regular languages with size-guarded
  inclusion. The type-name *string algebra* (`__TypeName`, `__UnionTypeName`, …) is **Phase 7**, sequenced after
  Phase 6 (its consumer). Template strings ride on the existing PHP interpolated-string parse; the remaining work is
  accepting interpolated strings in type position + a checker pattern type kind. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_08.5.md`.
- **Story 16.5 — Callable signature utilities (Tier 1, additive — inserted after Story 16):** TypeScript-style
  callable-keyed utilities `__CallableReturnType<TCallable>`, `__CallableParametersStruct<TCallable>` (named-arg
  bags), and `__CallableParametersTuple<TCallable>` (positional bags), so `\call_user_func` /
  `\call_user_func_array` (and peers) can correlate callback ↔ args ↔ return without arity-overload ladders or
  homogeneous `array<string, T1|T2|…>` maps. Uses inferred `TCallable extends callable` (no `typeof` in type
  arguments). Optional parameters use partial/required struct assignability rather than power-set intersections.
  Depends on Stories 08, 08.5, and 11. Detail in `IMPLEMENTATION_PLAN_TODO_STORY_16.5.md`.

---

## Feature additions folded into existing stories (post-restructure)

These are targeted feature additions that fit cleanly inside an existing story rather than warranting a new
top-level story (the sequence stays contiguous `01`–`30`):

- **Optional open tags / extension-driven "tagless" source mode → Story 06, Phase 7.** An opt-in `source.tagless`
  setting (`tyhp.json`, default `false`) lets authors omit the `<?tyhp` / `<?tyhpdef` open tag and rely on the file
  extension to choose the language mode. When enabled, the open tag is allowed but not required, and the closing tag
  `?>` is always an error. It is a front-end (lexer + config) concern with no checker/emitter dependency, so it lives
  with Story 06's grammar/lexer/compiler-infrastructure work. The config key is registered in `CONVENTIONS.md` §4. A
  future release may flip the default to `true` (tagless by default). `tyhp init` scaffolding (Story 13) emits the key.

---

## What moved tiers (and why)

- **Testing harness pulled FORWARD (old 11 → new 07, into Tier 0).** Was a late story; now the spine backbone so
  every subsequent story validates against golden fixtures from day one. Its "Stories 0–10 gate" language was
  reframed as incremental authoring (build the harness first; activate per-story fixtures as each story lands).
- **Expression-tree wedge LIFTED (old 16 → new 16, into Tier 1 showcase).** Was last with "ALL prior stories"
  prerequisites; relaxed to its real deps (checker 08, emitter expansion 11, lambda runtime 04) and labeled the
  flagship showcase.
- **Optimizer moved BACK to Tier 3 (old 4.5/4.6 → new 23/24).** Per the tier definition ("optimizer passes" are
  advanced). The build action (10) previously listed the optimizer as a prerequisite; it now wires an **optional**
  optimize pass that no-ops until 23/24 land. (Judgment call — see below.)
- **PHP-extension packages (old 23 → new 21) placed in Tier 2** per the DX/ecosystem definition; **Story 20.5**
  (PHP version gating) was added so 21 can ship a single `tyhp/php` package instead of per-minor forks.

---

## Judgment calls / ambiguities to confirm

1. **Single checker, two roles.** The tier sketch wanted a *minimal* checker on the spine and *full* checker breadth
   in Tier 1. There is exactly one checker plan (the full, ~4k-line Story 08), and splitting it would rewrite
   substance. Decision: keep the single comprehensive checker on the spine (08), labeled the flagship; Tier 1's
   "full checker breadth" is satisfied by it. **Confirm you're happy not splitting the checker.**
2. **Build action ↔ optimizer ordering.** Story 10 (build) originally required the optimizer (old 4.5/4.6). With the
   optimizer moved to Tier 3 (23/24), Story 10 now wires the optimize phase as **optional/no-op until present**. This
   is the one place a previously-stated hard dependency was softened. **Confirm this is acceptable** (alternative:
   keep a tiny "optional optimize hook" in 10 and the full modules in 23/24 — which is what the edit assumes).
3. **Story 21 → Story 20.5 + Story 28 dependencies.** The PHP-extension packages (21, Tier 2) require PHP-version
   gating (`declare(php=…)` / `#[\Tyhp\Php]`), owned by **Story 20.5** (hard prerequisite for the single-package
   layout), and use `T = DefaultType` generic defaults, owned by Story 28 (Tier 3). Decision: keep 21 in Tier 2;
   only the generic-default declarations need 28 (flag the forward dependency; stubs can land non-defaulted generics
   first if needed). **Confirm**, or move the generic-default-dependent phase of 21 to run after 28.
4. **Self-references in legacy notes.** A few historical notes were preserved verbatim where they describe genuine
   history (e.g. "legacy Story 5 (TyhpLib) was absorbed into Story 04", and the "no Stories 5/15/18" explanation in
   Story 30). These intentionally reference legacy numbers as history, not as live cross-references.
5. **Other repo docs not in scope.** Cross-reference rewriting covered the `IMPLEMENTATION_PLAN_TODO_STORY_*.md`
   set (plus the new docs, this ROADMAP, and CONVENTIONS). Other root docs (`TODO.md`, `Syntax_TODO.md`,
   `MASTER_FEATURES_LIST.md`, etc.) may still use legacy story numbers and were intentionally left untouched.
   **Confirm whether you want those updated too.**
