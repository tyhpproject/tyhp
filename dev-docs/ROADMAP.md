# Tyhp Implementation Roadmap

> **What this is:** the single ordered index the author follows top-to-bottom. Stories live in
> `IMPLEMENTATION_PLAN_TODO_STORY_NN.md`, renumbered into clean, zero-padded, dependency-ordered, tiered sequence
> (`01`–`30`, no gaps). Conventions (diagnostic codes, config keys, paths) are centralized in `CONVENTIONS.md`.
> PHP engine syntax-dispatch types (Traversable, ArrayAccess, Generator, magic methods, …) are
> inventoried in `PHP_LANGUAGE_HOOKS.md` (audit contract; Stories 21.6 / 21.7 implement a subset).
> Story 21.8 finishes `tyhpdef/php` Layer 3 overlays (generics, structs, DateTime operators) plus the
> checker holes that block that surface (Closure alias bounds, Tuple index, WeakMap append, const-int overloads).
> Story 21.9 emits type-alias `\Tyhp\Type` factories, `callable<..., TReturn>`, pack auto-splice / postfix `T...` / `__CallableParametersSlice`, and unifies checker utilities onto global `__` names. Story 21.10 adds `extra.tyhp.require`, Track C `extern` for author-only tyhpdefs, and a `tyhp/core` Composer plugin that syncs root `require-dev`. Story 21.11 is the additive bucket after 21.10 (Workstream A folds the tyhpdef package spec into `extra.tyhp.package` on `composer.json`; B–F are independent). Story 21.12 is the additive bucket after 21.11 (A: generic full-erase + `Generic::bind`; B: `NativeTypeTest` `is` emit; C Unresolved member-access; D emit-and-run; E docs/tagline/inference; F withdrawn — CI already exists; G remove `isa`/`isan`; H optional ctor `: void`). Stories **27** (object shapes + `__New<T>`), **27.1** (callable shapes + `type Name = struct { };`), **27.2** (block-target extension syntax), and **27.3** (split runtime / docs / AIDevGuide / IDE plugin into dedicated git repos) sit at the **end of Tier 2** after 21.12 so the type language and extension spelling are in the beta and the monorepo is split before Tier 3. Story 22 stays deferred.
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
- **Tier 2 — DX & Ecosystem:** LSP, sourcemaps + xdebug proxy, first-party IDE clients (VS Code + PhpStorm), and
  tyhpdef generation from PHP reflection + the PHP-version matrix (with the baseline+overlay regeneration pattern).
  The web playground (Story 22) stays in this tier numerically but is **deferred** until a compile host exists (docs
  currently ship on GitHub Pages, which cannot run the thin `tyhp build` backend).
- **Tier 3 — Advanced:** optimizer passes, promise interop with other PHP async libraries (Story 24.1),
  remaining advanced language features, the reflection API, and final documentation/polish.

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
| *(core shipped)* | `.github/workflows/tests.yml` already runs `dotnet test` | Story 07 **Phase 10** leftover — PHPUnit job, coverage, extra OS | `ALPHA_RELEASE.md` public-contributor wave |

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
| **19.5** | **IDE extensions (`tyhp-lang`)** *(additive — grow existing `tyhp-lang/vscode/` syntax extension; same client surface on VS Code + PhpStorm: TextMate, LSP, XDebug proxy UX, tasks, icons, status bar, workspace/init)* | 17, 18, 19 |
| **20** | Tyhpdef generator (C# CLI integration) | 01, 02 |
| **20.5** | **PHP version gating** (`declare(php=…)` + `#[\Tyhp\Php]`) *(additive — enables single-package stubs)* | 04, 06, 08, 09, 10, 11 |
| **20.7** | **Tyhpdef hooked properties** (`{ get; set; }` / `{ &get; }` bodyless signatures + generator emit) *(additive — implement **before** 20.6)* | 02, 08, 14.5, 20, 20.5 |
| **20.6** | **Thin extension mappings** (tyhpdef class-body `=>` only; Tyhp `extension { }` `=>` erased, `{ }` emitted) + call-site splice engine *(additive)* | 03, 08, 09, 11, 20, **20.7** |
| **21** | PHP extension Composer packages (`tyhpdef/php` + `tyhpdef/php-ext-*`) | 06, 20, **20.5**, **20.6**, **20.7**, 28† |
| **21.1** | **Tyhpdef `extern` types** *(additive — name-only placeholders for optional Composer/extension peers; implement **before** 21.5)* | 02, 08, 14, 20, **21** |
| **21.5** | **Composer toolchain integration** *(additive — `tyhp install composer`, init merge/scripts, `tyhp/compiler`, `generate_tyhpdef --vendor`)* | 13, 10, 19.5, **20**, 21, **21.1** |
| **21.6** | **Closure / Fiber / Generator type-system contract** *(additive — tyhpdef `TCallableShape`/`TThis`/`TScope`; strip builtin Closure return-last; Expression `TCallableShape`; infer `\Generator<K,V,S,R>`; Traversable implements; homogeneous ArrayAccess; `ArrayAccessShape`; `#[\Tyhp\PhpType]`)* | 08, 08.5, 16, **16.5**, **21** |
| **21.7** | **ArrayAccess destructuring & Stringable auto-implement** *(additive — `list()` / named `[]` on ArrayAccess; `__toString` ⇒ `\Stringable`)* | 08, **21.6** |
| **21.8** | **`tyhpdef/php` Layer 3 overlay completeness** *(additive — Core/Date/SPL/Standard/Callables hand overlays: generics, structs, type-guard returns, DateTime operators; checker: Closure alias bounds, Tuple index, WeakMap append, const-int overloads)* | 03, 08, 16.5, **21**, **21.6** |
| **21.9** | **Type-alias `\Tyhp\Type` factories** *(additive — emit namespace function / class static method returning `\Tyhp\Type`; hints still erase; `typeof` is `typeExpr`; also `callable<..., TReturn>`, pack splice / `T...` / Slice, `__` utility unification)* | 04, 08, 09, **11**, 15, **16.5** |
| **21.10** | **`extra.tyhp.require`, Track C `extern`, Composer plugin sync** *(additive — ambient vs author-only tyhpdefs; plugin on `tyhp/core` walks extras + `require` before vendor populate; `extern function` / `const`; `internal` grammar + public tyhpdef omit)* | 04, 13, 20, **21**, **21.1**, **21.5** |
| **21.11** | **Bucket after 21.10** *(additive — A: `extra.tyhp.package` on `composer.json`; B: UnitEnum/BackedEnum auto-implement; C: engine attributes; D: `\stdClass` write gate; E: XDebug SensitiveParameter redaction; F: duplicate diagnostics label the original)* | 08, 10, 13, **21.5**, 20, **21**, **21.6**, **21.7**, **21.10**, 18, 14 |
| **21.12** | **Bucket after 21.11** *(additive — A: generic full-erase + `Generic::bind`; B: `NativeTypeTest` `is` emit; C Unresolved member-access; D emit-and-run; E docs/tagline/inference; F withdrawn; G remove `isa`/`isan`; H optional ctor `: void`)* | 04, 06, 07, 08, 09, 11, 20, **21.9** |
| **27** | **Object shapes and `__New<T>`** *(lifted from Tier 3 — `object { }` alias-only shapes; replaced `new<TArgs...>`; **implement next** after 21.12)* | 08, 08.5, 11, 20, **21.12** |
| **27.1** | **Callable shapes and `type Name = struct { };`** *(additive — `callable(…): R`; delete `callable<>`; named structs are type aliases; **implement after 27**)* | **27**, 11, 16.5, **21.6**, **21.9** |
| **27.2** | **Block-target extension syntax** *(additive — `extension Name extends Type` / nested `extends Type { }`; drop per-member `extends T $this` and `operator +<T>`; **may run in parallel with 27 / 27.1**; must finish before 27.3)* | 03, 11, 20, **20.6** |
| **27.3** | **Split runtime, docs, AIDevGuide, and IDE plugin into dedicated git repos** *(additive — **implement after 27.2**; last Tier 2 story before deferred 22)* | **27.2**, **27.1**, 19.5, 21 |
| **22** | **Web playground (live `.tyhp` → PHP)** — *NEW · **DEFERRED*** (no compile host on GitHub Pages; skip in the sequence) | 10, 12, 17 |

> **Deferred: runtime-package distribution & versioning (→ Story 21).** The **full** Tyhp runtime-package
> distribution + versioning — publishing `tyhp/core` · `tyhp/decimal` · `tyhp/async` (and the `tyhpdef/php` /
> `tyhpdef/php-ext-*` extension packages) via a **published Packagist source** with proper version constraints — is
> deferred to **~Story 21**. Until then, an **interim local-source** inclusion is being implemented now: the
> generated `composer.json` gains Composer **`path` repositories** pointing at `runtime/packages/` so
> `composer install` resolves the runtime packages from the local checkout. This unblocks the Story 10 build
> pipeline's `composer install` step (see `FOUND_BUGS.md` — the runtime-package `require` constraint /
> unresolvable-`composer install` items, marked partially resolved via the interim fix) without committing to the
> published distribution model, which remains Story 21's responsibility. Story **20.5** supplies the
> `declare(php=…)` / `#[\Tyhp\Php]` gating language that lets Story 21 ship **one** stubs tree for all supported
> PHP minors instead of per-minor Composer packages. Story **20.7** adds bodyless hooked-property syntax on
> tyhpdefs (`{ get; set; }`, `{ &get; }`) so generated APIs can say a property is hooked. Story **20.6**
> requires class-body tyhpdef
> `extension fn` / `extension operator` to be **thin mappings**, and Tyhp `extension { }` **short `=>`
> members to be erased** (no PHP backer method; Track C copies the expression). Brace bodies stay real PHP.
> Story **21.1** adds tyhpdef `extern` placeholders so library wrappers can mention optional Composer /
> extension peers without shipping those packages. Story **21.10** lets **compiled Tyhp libraries**
> declare ambient tyhpdefs in `extra.tyhp.require` (any Composer name) versus author-only
> `require-dev`; Track C emits `extern` for the latter; a plugin on `tyhp/core` installs the
> merged extras plus `tyhp/compiler` on the app root `require-dev` in one solve.
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
> | **3 — Separate runtime repo / Packagist (Story 27.3)** | Packages published or moved out | **Self-host golden** (“recompile `tyhp_src`, diff committed PHP”) lives in the **runtime** repo (or its CI), pinning a compiler version — not as a forever in-tree compiler-suite check against local `dist/`. Compiler-repo tests consume published or path-installed packages only. Story **27.3** is the extract. |
>
> Open tracking: `FOUND_BUGS.md` — `EmittedPhpRunner` path + runtime self-host committed-output layout.
>
> **Deferred: web playground (Story 22).** Docs/site hosting is **GitHub Pages** (static). Story 22’s simplest
> design is a thin backend that shells `tyhp build` / `tyhp lint --format json` — that needs a process host Pages
> cannot provide. **Skip Story 22** when following this sequence (implement **27** then **27.1** then **27.2** then **27.3**, then skip 22). Later Tier 3 stories do not depend on it.
> Revisit when a compile host exists (small VPS, container, Cloudflare Worker+isolate, etc.). Compiling the C# CLI
> to a browser WASM module is *possible in principle* and would fit Pages, but it is a compiler-port (in-memory
> API, virtual FS, NativeAOT/WASI, payload size) — not a playground tweak. Decision: **do not take WASM as the v1
> path**; keep the thin-backend design when the story un-defers. Detail in `IMPLEMENTATION_PLAN_TODO_STORY_22.md`.

### Tier 3 — Advanced

| New | Story | Direct deps |
|-----|-------|-------------|
| **23** | Compiler optimizer (MVP) | 03, 08, 09, **20.6** |
| **24** | Advanced optimizations | 23 |
| **24.1** | **Promise interop** *(additive — `Promise::from`, suggested outbound wrappers, optional React/Revolt loop driver; does not gate 25)* | `tyhp/async` (04 / 06) |
| **25** | `internal` visibility modifier | all earlier (01–24); **24.1 does not gate** |
| **26** | Null-conditional chaining with assignment | all earlier (01–25) |
| **28** | Generic type parameter defaults | all earlier (01–27) |
| **29** | Tyhp reflection API (sourcemap-backed) | 17, 23, 04, 20, 03, 19, 25, 26, 27, 28 |
| **30** | Documentation & polish (final capstone) | all earlier (01–29) |

**Story 24.1 — Promise interop (additive, between 24 and 25).** `async` / `await` stay `\Tyhp\Promise` and `\Tyhp\EventLoop`. `Promise::from` adopts a foreign thenable. Recommended packages (`tyhp/async-http-promise`, `tyhp/async-guzzle`, `tyhp/async-react`, `tyhp/async-amp`) wrap a Tyhp promise as the foreign interface and, optionally, replace the loop owner with ReactPHP or Revolt. They are `suggest`ed, not `require`d. One loop owns the thread. Does not depend on the optimizer and does not gate Story 25. Detail in `IMPLEMENTATION_PLAN_TODO_STORY_24.1.md`.

† See "Judgment calls" — Story 21 has a forward dependency on Story 28.

### Tier 4 — Future plans

> Large, cross-cutting work sequenced **after** the features it builds on. Not part of the contiguous `01`–`30`
> spine; queued for after the capstone.

| New | Story | Direct deps |
|-----|-------|-------------|
| **31** | **Future Ideas & Optimizations** (collection; Idea 1 = Tyhp Link; Ideas 9–10 = compiler plugins v1/v2; Idea 11 = `|>` for chained extensions on PHP 8.5+; Idea 12 = trait-requirement abstract members; Idea 13 = anti-glob `!` prefix in JSON config glob arrays; Idea 14 = runtime `is Closure<…>` via reflection) — *NEW* | 06, 08, 08.5, 09, 11, 13, 15, 17, 18, 19.5†, 23, 24, 25, **21.6** |

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
(post-binder transform + rebind; TextMate/VS Code + PhpStorm island highlighting via Story 19.5; fix-its; statement islands;
sandbox TBD; shared cache; optimizer hooks), and **Idea 11 — pipe-emit chained extension calls** (`$s->a()->b()` →
PHP 8.5 `|>` when the rewrite is receiver-only), and **Idea 12 — trait-requirement abstract members**
(emit `abstract` methods for used signatures from a trait’s `extends`/`implements`; PHP still cannot name
the class/interface on the trait), and **Idea 13 — anti-glob (`!` prefix) in JSON config glob arrays**
(a `!` string in `include` / `overlay` / `tyhpdefInclude` / … drops matching paths from that array’s sibling
globs; no new keys), and **Idea 14 — runtime `is Closure<…>` / `is Fiber<…>` via reflection**
(Story 21.6 ships `as` only; a later helper would bump the interop contract). Detail in `IMPLEMENTATION_PLAN_TODO_STORY_31.md`.

† Idea 10’s IDE/TextMate piece depends on Story 19.5 (both editor clients); Idea 9 does not.

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
| — | **19.5** | IDE extensions (`tyhp-lang`) *(NEW, additive — grow existing syntax extension)* | 2 |
| 10 | **20** | Tyhpdef generator | 2 |
| — | **20.5** | PHP version gating (`declare(php=…)` / `#[\Tyhp\Php]`) *(NEW, additive)* | 2 |
| — | **20.7** | Tyhpdef hooked properties *(NEW, additive — implement before 20.6)* | 2 |
| — | **20.6** | Thin extension mappings (tyhpdef + Tyhp `=>` erasure) *(NEW, additive)* | 2 |
| 23 | **21** | PHP extension Composer packages (`tyhpdef/php` + `tyhpdef/php-ext-*`) | 2 |
| — | **21.1** | Tyhpdef `extern` types *(NEW, additive — implement before 21.5)* | 2 |
| — | **21.5** | Composer toolchain integration *(NEW, additive)* | 2 |
| — | **21.6** | Closure / Fiber / Generator type-system contract *(NEW, additive)* | 2 |
| — | **21.7** | ArrayAccess destructuring & Stringable auto-implement *(NEW, additive)* | 2 |
| — | **21.8** | `tyhpdef/php` Layer 3 overlay completeness *(NEW, additive)* | 2 |
| — | **21.9** | Type-alias `\Tyhp\Type` factories *(NEW, additive)* | 2 |
| — | **21.10** | `extra.tyhp.require`, Track C `extern`, Composer plugin sync *(NEW, additive)* | 2 |
| — | **21.11** | `extra.tyhp.package` + enum/engine-attribute/`\stdClass`/XDebug-redaction/duplicate-label bucket *(NEW, additive — after 21.10, before 21.12)* | 2 |
| — | **21.12** | Generic full-erase + `Generic::bind` + language-assessment B–E/G–H bucket *(F withdrawn)* *(NEW, additive — after 21.11, before 27)* | 2 |
| 20 | **27** | Object shapes and `__New<T>` (replaced `new<TArgs...>`; lifted to Tier 2 after 21.12) | 2 |
| — | **27.1** | Callable shapes (`callable(…): R`) + `type Name = struct { };` *(NEW, additive — implement after 27, before 27.2)* | 2 |
| — | **27.2** | Block-target extension syntax (`extension Name extends Type`) *(NEW, additive — implement in the beta; may run in parallel with 27 / 27.1; before 27.3)* | 2 |
| — | **27.3** | Split runtime / docs / AIDevGuide / IDE plugin into dedicated git repos *(NEW, additive — implement after 27.2, before deferred 22)* | 2 |
| — | **22** | Web playground *(NEW · deferred — no GitHub Pages compile host)* | 2 |
| 4.5 | **23** | Compiler optimizer (MVP) *(moved to Tier 3)* | 3 |
| 4.6 | **24** | Advanced optimizations *(moved to Tier 3)* | 3 |
| — | **24.1** | Promise interop *(NEW, additive — between 24 and 25; does not gate 25)* | 3 |
| 17 | **25** | `internal` visibility | 3 |
| 19 | **26** | Null-conditional chaining with assignment | 3 |
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
| 20.7 | *(NEW)* |
| 20.6 | *(NEW)* |
| 21 | 23 |
| 21.1 | *(NEW)* |
| 21.5 | *(NEW)* |
| 21.6 | *(NEW)* |
| 21.7 | *(NEW)* |
| 21.8 | *(NEW)* |
| 21.9 | *(NEW)* |
| 21.10 | *(NEW)* |
| 21.11 | *(NEW)* |
| 21.12 | *(NEW)* |
| 27 | 20 |
| 27.1 | *(NEW)* |
| 27.2 | *(NEW)* |
| 27.3 | *(NEW — was 27.2 repo split)* |
| 22 | *(NEW)* |
| 23 | 4.5 |
| 24 | 4.6 |
| 24.1 | *(NEW)* |
| 25 | 17 |
| 26 | 19 |
| 28 | 21 |
| 29 | 22 |
| 30 | 99 |

> The new sequence is **contiguous `01`–`30`** with additive sub-stories (`08.5`, `10.5`, `14.5`, `16.5`, `19.5`, `20.5`, `20.7`, `20.6`, `21.1`, `21.5`, `21.6`, `21.7`, `21.8`, `21.9`, `21.10`, `21.11`, `21.12`, `24.1`, `27.1`, `27.2`, `27.3`) inserted where needed.
> **20.7 before 20.6:** 20.6 was allocated first; hooked-property tyhpdef syntax must land before the splice engine can see `&get` vs by-value `get` on generated tyhpdefs.
> **21.1 before 21.5:** `extern` must land before `--vendor` generation so optional-peer tyhpdefs are loadable.
> **21.6 after 16.5 and 21:** Closure/Fiber/Generator typing is overlay + checker; independent of 21.5 Composer glue.
> **21.7 after 21.6:** ArrayAccess destructure reuses 21.6 `$obj[$k]` types; Stringable auto-implement is the other remaining language-hook gap.
> **21.8 after 21.7:** Layer 3 `tyhpdef/php` overlay completeness (generics/structs/DateTime operators) is independent of 21.7 language hooks; sequence it after so Core ArrayAccess/Iterator overlays from 21.6 are already locked.
> **21.9 after 21.8:** type-alias `\Tyhp\Type` factories (hints still erase; `typeof` is `typeExpr`) plus `callable<..., TReturn>`, pack auto-splice / postfix `T...` / `__CallableParametersSlice`, and `__` utility unification.
> **21.10 after 21.9:** `extra.tyhp.require` + Track C `extern` for author-only tyhpdefs + `tyhp/core` Composer plugin (one-solve tree walk). `internal` grammar + public tyhpdef omit land here; Story 25 still owns checker / overlay.
> **21.11 after 21.10:** additive bucket (`extra.tyhp.package` on `composer.json`; UnitEnum/BackedEnum; engine attributes; `\stdClass` write gate; XDebug SensitiveParameter redaction; duplicate diagnostics label the original).
> **21.12 after 21.11:** additive bucket (A: generic full-erase + `Generic::bind`; B: `#[\Tyhp\NativeTypeTest]` tyhpdef-driven `is` emit; C: Unresolved member-access; D: emit-and-run harness; E: honest emit / inference-first locals / “typed superset”; F withdrawn — `.github/workflows/tests.yml` already exists; G: remove `isa`/`isan`/`is_a`/`is_an`; H: optional ctor `: void`).
> **27 after 21.12:** object shapes (`object { }` alias-only) + `__New<T>` (replaced `new<TArgs...>`); lifted from Tier 3 so the type language is in the beta. **Implement next.**
> **27.1 after 27:** callable shapes (`callable(…): R`; delete `callable<>`) and `type Name = struct { };` (inline `struct { }` in type position; `new` of a struct alias still constructs an array).
> **27.2 after 27.1 (numbered; may run in parallel):** block-target extension syntax (`extension Name extends Type` / nested `extends Type { }`; drop per-member `extends T $this` and `operator +<T>`). Must finish before the repo split.
> **27.3 after 27.2:** split `runtime/packages`, `docs/`, `AIDevGuide/`, and `tyhp-lang/` into dedicated git repos on `tyhpproject` (`tyhp-runtime-src`, `tyhp-docs-src`, `tyhp-ai-dev-guide`, `tyhp-ide-plugin`). Published `tyhp/*` and `tyhpdef/*` repos live on `tyhpproject-packages` (existing `tyhpproject` package repos are re-created there; Packagist URLs are updated). Then skip deferred **22**.
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
- **Story 22 — Web playground (Tier 2, deferred):** a two-pane page (editable `.tyhp` left; live PHP + diagnostics
  right). Simplest implementation: a thin backend that shells `tyhp build` / `tyhp lint --format json` on a
  sandboxed temp file. **Deferred** because the public site is GitHub Pages (static) and there is no host for that
  backend. Browser WASM of the compiler is not the v1 path (see the Tier 2 deferral note). Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_22.md`.
- **Story 19.5 — IDE extensions (Tier 2, additive — inserted after Story 19):** first-party **Tyhp Language** (`tyhp-lang`) clients — **VS Code**
  (`tyhp-lang/vscode/` — already in-repo as a syntax-only extension; this story grows it) and **PhpStorm** (`tyhp-lang/phpstorm/`) for Stories 17–19 — same surface on both: TextMate
  highlighting, LSP client for `tyhp language_server`, XDebug-proxy debug wiring, tasks, file icons, status bar,
  and workspace/`tyhp.json` awareness (including `tyhp init`). Canonical TextMate grammars live in `tyhp-lang/vscode/syntaxes/`; binary discovery
  (PATH → setting), download install (global or plugin-local), and plugin-only auto-update / pin. Packageable
  artifacts only (VSIX + PhpStorm plugin ZIP) — **no** Marketplace submit in this story; Cursor is compatible via
  the same VSIX, not a separate deliverable. Detail in `IMPLEMENTATION_PLAN_TODO_STORY_19.5.md`.
- **Story 20.5 — PHP version gating (Tier 2, additive — inserted after Story 20):** compile-time
  `declare(php="…")` (Composer constraint strings) and `#[\Tyhp\Php(string $version)]` so Story 21 can ship a
  single `tyhpdef/php` (+ `tyhpdef/php-ext-*`) stubs package instead of per-minor forks. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_20.5.md`.
- **Story 20.7 — Tyhpdef hooked properties (Tier 2, additive — inserted after Story 20.5, implement
  before 20.6):** bodyless `{ get; set; }` / `{ &get; }` on tyhpdef class properties, generator emit
  from Tracks A/B/C, binder flags for `HasAccessor` / by-ref get. Unblocks Story 20.6 splice
  referenceability. Detail in `IMPLEMENTATION_PLAN_TODO_STORY_20.7.md`.
- **Story 20.6 — Thin extension mappings (Tier 2, additive — inserted after Story 20.7):**
  class-body tyhpdef `extension fn` / `extension operator` are **thin mappings** only (PHP arrow-function
  `=>` expressions, always erased at emit, no brace bodies, no `__TyhpInlineExt_*` backer). Tyhp
  `extension { }` uses **form as the contract**: `=>` emits no PHP backer method (Track C copies the
  expression); a single-`return` brace body is spliced **and** emits a method PHP callers can use;
  multi-statement bodies emit a method Tyhp calls. Also owns the shared call-site splice engine and its
  safety rules (no parameter mutation, no cycles, repeated arguments hoisted to a temp, parameters
  behave as by reference). `#[Inline]` is not the erasure marker, and it is an error on extension
  members. This removes extension inlining from Story 23. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_20.6.md`.
- **Story 21.1 — Tyhpdef `extern` types (Tier 2, additive — inserted after Story 21, implement
  before 21.5):** name-only `extern class` / `interface` / `enum` placeholders so a tyhpdef may
  mention types from Composer `suggest` / `require-dev` / optional `ext-*` without shipping those
  packages. Real declarations of the same Tyhp name silently win. Using an `extern` type from
  `.tyhp` is an error that points at the placeholder (and `@provided-by: tyhpdef/…` when present).
  Track B emits `_tyhpdef/externs.tyhpdef` only when an existing wrapper owns the type and the
  target lists that PHP package in `suggest` / `require-dev`; `require` deps use a wrapper
  `require` (generate in dependency order). Unknown origin stays unresolved.
  Detail in `IMPLEMENTATION_PLAN_TODO_STORY_21.1.md`.
- **Story 21.5 — Composer toolchain integration (Tier 2, additive — inserted after Story 21.1):**
  `tyhp install composer`, shape-A init (Tyhp-authored `composer.json`, no `composer init`) with
  merge into an existing file, root `scripts.tyhp` + `post-autoload-dump`, `generate_tyhpdef --vendor`
  (prefer `tyhpdef/*` companions and `tyhpdef/php-ext-*`, else `vendor-tyhpdef/` stubs + overlays; Track B
  `extern` emit is Story 21.1), and a `tyhp/compiler` Packagist wrapper
  (`vendor/bin/tyhp`, `--install-binary`). No Composer plugin in v1. Grill locked 2026-08-27.
  Detail in `IMPLEMENTATION_PLAN_TODO_STORY_21.5.md`.
- **Story 21.6 — Closure / Fiber / Generator type-system contract (Tier 2, additive — inserted after Story 21.5,
  implement after 16.5 and 21):** strip builtin return-last `\Closure<TArgs…, TReturn>`; tyhpdef
  `\Closure<TCallableShape, TThis, TScope>` with callable-ness from `__invoke`; Fiber
  `TResume` on `resume()` only; infer `\Generator<TKey, TValue, TSend, TReturn>` from generator
  bodies (declared return is `\Generator` / two-arg / four-arg, never the body's `return` type);
  magic types `__SuperType` / `__SuperTypeName` / `__CurrentScope` /
  `__CallableThis` / `__CallableScope`; rewrite `\Tyhp\Expression` / `PropertyPath` to
  `TCallableShape`; Traversable `implements` rule; homogeneous `ArrayAccess<K,V>` indexing;
  `ArrayAccessShape<TStruct>` per-key maps; `#[\Tyhp\PhpType]` emit hints; index utilities
  `__IndexKeys` / `__IndexValueType` / `__IndexValueTypes`. Design locked 2026-09-02. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_21.6.md`.
- **Story 21.7 — ArrayAccess destructuring & Stringable auto-implement (Tier 2, additive — inserted after
  Story 21.6):** `list()` / `[]` destructuring on `\ArrayAccess` (positional and named keys, same
  `offsetGet` as `$obj[$k]`); `__toString()` implies `\Stringable` for classes and interfaces.
  Design locked 2026-09-02. Detail in `IMPLEMENTATION_PLAN_TODO_STORY_21.7.md`.
- **Story 21.8 — `tyhpdef/php` Layer 3 overlay completeness (Tier 2, additive — inserted after Story 21.7):**
  finish hand overlays for Core, Date, SPL, Standard, and Standard.Callables (generics, overloads,
  array-shape structs, `is_*` / existence type-guard returns, DateTime native comparisons plus
  mapped `+`/`-` onto `add`/`sub`/`diff`). Closes FOUND_BUGS #5 (`SplPriorityQueue`), #6 (Closure
  alias-bound expansion), #8 (`__CallableParametersTuple` index), and #10 (WeakMap append). Also
  const-int `??` values as literals at overload selection (`parse_url` / `pathinfo`). Does not
  edit Layer 1 or Layer 2. Design locked 2026-09-02. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_21.8.md`.
- **Story 21.9 — Type-alias `\Tyhp\Type` factories (Tier 2, additive — inserted after Story 21.8):**
  emit a namespace function or class static method named after each source type alias that returns
  the `\Tyhp\Type` for its body (generic aliases take `?\Tyhp\Type` value arguments, not Mechanism D
  Closures). PHP hints still expand to the underlying type. `typeof` / `is` / `instanceof` / `default`
  of an alias call the factory. `typeof` takes `typeExpr` only (same as `default`). Tyhp `use` of an
  alias becomes `use function` in PHP when the factory is used. **Appended:** `callable<..., TReturn>`
  any-arity facet (wildcard parameter list, pinned return) plus inferred generic `extends` checking;
  first overlay consumer is `iterator_apply`. **Appended 2026-09-09:** packs auto-splice inside `callable<>`;
  postfix `T...` is PHP variadic / homogeneous (exact vs `extends`); `__CallableParametersSlice`;
  pack-preserving `__Nullable`; all built-in checker utilities as global `__` names. `array_map` zip
  and `ClosureExtensions.compose` / `then` consume splice. Design locked 2026-09-09. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_21.9.md`.
- **Story 21.10 — `extra.tyhp.require`, Track C `extern`, Composer plugin sync (Tier 2, additive — inserted after Story 21.9):**
  compiled Tyhp libraries declare ambient tyhpdef packages in `extra.tyhp.require` (any Composer
  name, not only `tyhpdef/*`) versus author-only `require-dev`. Track C emits real FQNs for ambient
  names and name-only `extern` (including `extern function` / `extern const`) for the rest. A
  Composer plugin on `tyhp/core` walks extras **and** runtime `require` edges from Packagist /
  `repositories` before vendor populate, merges constraints onto the app root `require-dev` (plus
  `tyhp/compiler`) in one solve, and no-ops on `--no-dev`. `internal` is parsed and omitted from the
  public `package.tyhpdef`; Story 25 still owns checker enforcement and the internals overlay
  (`internalOverlay` on `extra.tyhp.package`). Design locked 2026-09-10. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_21.10.md`.
- **Story 21.11 — Bucket after 21.10 (Tier 2, additive):** Workstream A puts the tyhpdef package
  spec on `extra.tyhp.package` of `composer.json` (`include` / `exclude` / `overlay` / `source.tagless`;
  vendor and explicit load use that sentinel). Independent workstreams: B UnitEnum/BackedEnum
  auto-implement, C PHP engine attributes, D `\stdClass` undeclared-write gate, E XDebug
  `#[\SensitiveParameter]` redaction, F duplicate diagnostics label the original declaration.
  Story 25 still owns internals overlay load (`internalOverlay` on this same `extra.tyhp.package`
  object). Detail in `IMPLEMENTATION_PLAN_TODO_STORY_21.11.md`.
- **Story 21.12 — Bucket after 21.11 (Tier 2, additive):** Workstream A fully erases generic
  classes/methods/functions that do not use the bound type at runtime (properties stay Mechanism C
  unless `#[\Tyhp\EraseGeneric]`); stamps `#[\Tyhp\GenericRuntime]` (no longer `NoEmit`) on every
  emitted generic class/method/function in PHP and `package.tyhpdef`; same-compilation sites keep
  factories/binders; foreign compiled-library sites and PHP callers use `\Tyhp\Generic::bind(...)(...)`.
  Independent workstreams from `LANGUAGE_ASSESSMENT.md` §8: **B** `#[\Tyhp\NativeTypeTest]` tyhpdef-driven
  `$x is T` emit, **C** Unresolved receivers (FOUND #56), **D** emit-and-run harness (extends existing
  `.github/workflows/tests.yml`), **E** honest emit + inference-first locals + “typed superset” tagline,
  **F** withdrawn (CI already exists; leftover PHPUnit/coverage/OS is `ALPHA_RELEASE.md`),
  **G** remove `isa`/`isan`/`is_a`/`is_an`, **H** omitted constructor return type = `: void`. Async HTTP/DB/FS
  is Story 31 Idea 17. Emitter-pipeline refactor is rejected for now. Next in sequence: Stories **27** / **27.1** / **27.2** / **27.3**. Story 22 stays deferred. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_21.12.md`.
- **Story 27 — Object shapes and `__New<T>` (Tier 2, lifted from Tier 3 — implement after 21.12):** alias-only
  `object { … }` structural types, constructability via `__New<T>` (replaces `new<TArgs...>`). Nominal parents
  are intersections (`LoggerInterface & object { … }`). Harvest of `return new class` yields a shape alias.
  Detail in `IMPLEMENTATION_PLAN_TODO_STORY_27.md`.
- **Story 27.1 — Callable shapes and `type Name = struct { };` (Tier 2, additive — implement after 27):**
  `callable(…): R` (inline or aliased); delete return-last `callable<>`; `\Closure` still takes a callable
  shape as `TCallableShape`, never PHPStan `\Closure(int): string`. Named structs are `type Name = struct { };`
  (`new Alias() with` still builds an array); inline `struct { }` in type position; `(typeExpr)` grouping.
  Detail in `IMPLEMENTATION_PLAN_TODO_STORY_27.1.md`.
- **Story 27.2 — Block-target extension syntax (Tier 2, additive — numbered after 27.1, may run in parallel; before 27.3):**
  standalone `extension { }` takes a class-like `extends Type` header and/or nested `extends Type { }`
  groups; `$this` is implied; operators list every operand with `self` = target (rewritten on PHP emit);
  overlapping same member on `MyClass<string>` vs `MyClass<T>` is an error. Drop per-member
  `extends T $this` and `operator +<T>`. Rewrite `tyhp/core` catalogs. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_27.2.md`.
- **Story 27.3 — Split runtime, docs, AIDevGuide, and IDE plugin into dedicated git repos (Tier 2, additive — implement after 27.2):**
  extract `runtime/packages/` → `tyhp-runtime-src`, `docs/` → `tyhp-docs-src`, `AIDevGuide/` →
  `tyhp-ai-dev-guide`, `tyhp-lang/` → `tyhp-ide-plugin`. Those source repos stay on GitHub org
  `tyhpproject`, with the compiler. Published `tyhp/*` and `tyhpdef/*` package repos live on
  `tyhpproject-packages`; existing `tyhpproject` package repos are re-created there and Packagist
  repository URLs are updated. Compiler keeps CLI/LSP; path discovery via
  `TYHP_RUNTIME_SRC` / sibling checkout. Runtime-src also lands owner-maintained tyhpdefs
  (`TYHPDEF_OWNERSHIP.md` at repo root: public metapackage + `*-impl`, handoff/scan scripts,
  GitHub templates; compiler `--vendor` honors `extra.tyhp.tyhpdef` / bundled skip). Last step
  writes a sibling checklist for CLA / GitHub apps / Packagist. Then skip deferred **22**. Detail in
  `IMPLEMENTATION_PLAN_TODO_STORY_27.3.md`.
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
  (PHP version gating) was added so 21 can ship a single `tyhpdef/php` package instead of per-minor forks.
- **Object shapes lifted into the beta (Story 27 + 27.1 → end of Tier 2, after 21.12).** Was sequenced after
  26 in Tier 3 with “all earlier” prerequisites. Real deps are checker/aliases/`__ClassName`/tyhpdefs (27) and
  the 21.6/21.9 callable contract plus structs (27.1). Keep numbers **27** / **27.1** (not 21.13). Story **27.2**
  (block-target extension syntax) sits in the same beta window and may run in parallel. Story **27.3**
  (repo split) follows 27.2. Skip deferred 22 after 27.3. Optimizer / `internal` enforcement / `?->` assignment
  stay post-beta.

- **Web playground deferred in-place (Story 22 stays numbered in Tier 2).** Hosting is GitHub Pages; the thin
  compile backend cannot run there. Numbering stays contiguous `01`–`30`; skip 22 until a host exists. WASM-in-
  browser is recorded as a possible later approach, not the current plan.

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
3. **Story 21 → Story 20.5 + Story 20.7 + Story 20.6 + Story 28 dependencies.** The PHP-extension packages (21, Tier 2) require PHP-version
   gating (`declare(php=…)` / `#[\Tyhp\Php]`), owned by **Story 20.5** (hard prerequisite for the single-package
   layout); tyhpdef hooked-property syntax owned by **Story 20.7**; thin tyhpdef class-body mappings owned by **Story 20.6**; and `T = DefaultType` generic defaults, owned by Story 28 (Tier 3). Decision: keep 21 in Tier 2;
   only the generic-default declarations need 28 (flag the forward dependency; stubs can land non-defaulted generics
   first if needed). **Confirm**, or move the generic-default-dependent phase of 21 to run after 28.
4. **Self-references in legacy notes.** A few historical notes were preserved verbatim where they describe genuine
   history (e.g. "legacy Story 5 (TyhpLib) was absorbed into Story 04", and the "no Stories 5/15/18" explanation in
   Story 30). These intentionally reference legacy numbers as history, not as live cross-references.
5. **Other repo docs not in scope.** Cross-reference rewriting covered the `IMPLEMENTATION_PLAN_TODO_STORY_*.md`
   set (plus the new docs, this ROADMAP, and CONVENTIONS). Other root docs (`TODO.md`, `Syntax_TODO.md`,
   `MASTER_FEATURES_LIST.md`, etc.) may still use legacy story numbers and were intentionally left untouched.
   **Confirm whether you want those updated too.**
6. **Stories 27 / 27.1 in the Tier 2 beta.** Object shapes, callable shapes, and `type Name = struct { };` are the
   type language strangers will copy. Decision: implement **27 then 27.1 after 21.12**, skip deferred 22, leave
   23+ post-beta. Do not teach `callable<>` / `struct Point { }` in the beta and break them in 1.0.
   **Confirmed 2026-09-17.**
7. **Story 27.3 after 27.2 (still Tier 2).** Split runtime packages, docs, AIDevGuide, and IDE plugins into
   dedicated git repos before Tier 3. Decision: implement **27.2** (extension syntax) then **27.3** (repo split)
   after 27.1, then skip deferred 22. **Confirmed 2026-09-17; 27.2/27.3 split 2026-09-18.**
8. **Story 27.2 — block-target extension syntax (still Tier 2, in the beta).** Replace per-member
   `extends T $this` / `operator +<T>` with `extension Name extends Type` (and nested groups) before the
   repo extract so `tyhp/core` and docs move already rewritten. **Confirmed 2026-09-18.**
9. **Story 24.1 — one loop owner.** Foreign thenables convert at the edge (`Promise::from` in, suggested
   wrapper packages out). Tyhp's loop stays the default. React or Revolt may replace it for the process;
   both loops never run together. Amp `Future::await` inside a Tyhp fiber is a spike, not a promise.
   **Confirmed 2026-09-28.**
