# Implementation Plan: Story 31 — Tyhp Link (Compiled Autoloading & Linking)

> **About this story:** Story 31 is **Tyhp Link**, the build-time linker and tiered runtime loader. It also keeps
> [Appendix A](#appendix-a-eager-resolution-optimization-for-the-binder-two-pass), a binder name-resolution
> performance optimization that shares the "resolve symbol references" theme.
>
> Until 2026-11-03 this document was a collection of future ideas, with Tyhp Link as Idea 1. The other ideas now
> have their own stories:
>
> | Old | New | Story |
> |-----|-----|-------|
> | Idea 2 | **32** | Scalar-conversion contracts (`*Convertible`, Stringable-style casts) |
> | Idea 3 | **33** | Default interface implementations (interface-attached traits) |
> | Idea 4 | **22.1** Part A | Emit static-analysis docblocks (PHPStan/Psalm/PHPDoc) |
> | Idea 5 | **34** | `operator default()` (author-defined default value for a class type) |
> | Idea 6 | **35** | No uninitialized storage (ban `unset`, require property init) |
> | Idea 7 | **36** | Conditional return types (v1) |
> | Idea 8 | **37** | Advanced conditional types (v2) |
> | Idea 9 | **38** | Compiler plugins v1 |
> | Idea 10 | **39** | Compiler plugins v2 |
> | Idea 11 | **40** | Pipe-emit chained extension calls (`\|>` on PHP 8.5+) |
> | Idea 12 | **41** | Trait-requirement abstract members |
> | Idea 13 | **42** | Anti-glob (`!` prefix) in JSON config glob arrays |
> | Idea 14 | **43** | Runtime `is Closure<…>` / `is Fiber<…>` via reflection |
> | Idea 15 | **44** | Opt-in runtime checks for erased Tyhp contracts |
> | Idea 16 | **45** | Precompiled tyhpdef symbol cache (cold start) |
> | Idea 17 | **46** | Async HTTP, filesystem, and database I/O |
> | Idea 18 | **22.1** Part B | Tyhp templates (`<?=` as a Tyhp echo in `.tyhp` files) |
> | Idea 19 | **47** | Declaration-site generic variance (`in` / `out`) |
>
> Each lives in `IMPLEMENTATION_PLAN_TODO_STORY_NN.md`.

> **Roadmap position:** Story 31 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 06, 08, 08.5, 09, 11, 13, 15, 17, 18, 23, 24, 25
> **New story:** created during autoloading/linking brainstorm; no legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single
> source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating
> ranges. New `tyhp.json` keys proposed here are **provisional** and must be registered in `CONVENTIONS.md` §4
> when the story is scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Source:** Autoloading/linking design discussion
> **Branch:** TBD
> **Generated:** 2026-06-18
> **Updated:** 2026-11-03 (Ideas 2–19 split into Stories 22.1 and 32–47)
> **Prerequisites:** Story 06 (built-in types & compile-time constructs), Story 08 (checker), **Story 08.5
> (symbol-name types — definitions, narrowing, existence verification, typed `nameof()`)**, Story 09/11 (emitter),
> Story 13 (CLI/composer + profile toggle), Story 15 (interop contract), Story 17/18 (sourcemaps + xdebug proxy),
> Story 23/24 (optimizer), Story 25 (`internal` visibility)

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [Architecture Overview](#architecture-overview)
- [The Three-Tier Linker Model](#the-three-tier-linker-model)
- [Phase 1: The Link Graph (build-time)](#phase-1-the-link-graph-build-time)
- [Phase 2: Symbol-Name Types (delivered by Story 08.5)](#phase-2-symbol-name-types-delivered-by-story-085)
- [Phase 3: The Lowering & Relocation Invariant](#phase-3-the-lowering--relocation-invariant)
- [Phase 4: Function/Constant Lowering](#phase-4-functionconstant-lowering)
- [Phase 5: The `[Preload]` Attribute](#phase-5-the-preload-attribute)
- [Phase 6: Dual Emission & Bundling](#phase-6-dual-emission--bundling)
- [Phase 7: The Runtime Loader](#phase-7-the-runtime-loader)
- [Phase 8: Debuggability](#phase-8-debuggability)
- [Phase 9: User documentation & AIDevGuide](#phase-9-user-documentation--aidevguide)
- [Configuration](#configuration)
- [Decisions (defaults chosen)](#decisions-defaults-chosen)
- [Risks & Edge Cases](#risks--edge-cases)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)
- [Appendix A: Eager-Resolution Optimization for the Binder Two-Pass](#appendix-a-eager-resolution-optimization-for-the-binder-two-pass)

---

## Summary

**Tyhp Link** is a build-time *linker* plus a tiered runtime *loader* that replaces Composer's filesystem-search
autoloading with something a compiler can do better, because the checker (Story 08) already computes the full,
validated static type/dependency graph that Composer can only guess at.

It bundles four ideas into one coherent feature:

1. **A three-tier resolution model** — `opcache.preload` (static linking) → a Tyhp fast loader (shared-objects) →
   Composer PSR-4 (`dlopen` fallback).
2. **Dual emission** — readable PSR-4 PHP for debugging + optimized, minified, topo-sorted bundles for production,
   selectable by a `tyhp.json` profile toggle.
3. **Function/constant lowering** — free functions/constants lowered to static members so they ride the fast, lazy
   class path instead of Composer's eager `files` includes.
4. **Emit-time canonicalization of typed symbol-strings** — built on the symbol-name types delivered in **Story 08.5**
   (`__ClassName`, `__FunctionName`, …); this story adds the *safety backbone* that lets the emitter
   rewrite/canonicalize those string-based symbol references when symbols are lowered or relocated.

> **Status:** Tier 4, future plans. This story is intentionally large and cross-cutting; it is sequenced after the
> features it depends on (checker, emitter expansion, optimizer, sourcemaps, interop contract, `internal` visibility).

---

## Motivation

Composer's autoloader is a *runtime filesystem search*:

- **PSR-4 (dev):** string-munge the FQCN, then `is_file()` against each registered prefix root — string ops plus
  **N `stat` syscalls per class miss**.
- **Optimized classmap (`-o`):** an O(1) `array<FQCN,path>` hash, but still falls back to PSR-4 on a miss.
- **Authoritative classmap (`-a`):** classmap only; a miss means "does not exist." Fastest standard option.
- **`files` autoload:** files `require`d **eagerly on every request** — the only way PHP can surface free
  functions/constants, since it cannot autoload those.

Even with `-a` + opcache, the residual per-request costs are: building the classmap array, the autoload **callback**
per class miss, one tiny `include` per class (PSR-4 is one-class-per-file), `realpath` churn, and the eager `files`
includes whether or not they are used.

A compiler is not bound to any of this. Tyhp knows the complete inheritance/implements/uses DAG and can:

- topologically sort a **guaranteed-linkable** `opcache.preload` script (the part hand-written preload always gets
  wrong),
- **bundle** co-used classes into one file (fewer, larger includes),
- **tree-shake** to only the reachable set (whole-program builds for CLI/serverless),
- emit a **flat, authoritative** class map that eliminates PSR-4 string work and filesystem probing,
- **lower** free functions/constants so they stop being eager.

The unifying frame: Composer does **dynamic loading** (search the disk at runtime); Tyhp can do **static linking**
(decide everything at build time) and pick the runtime strategy per deployment target.

---

## Architecture Overview

```
                         ┌──────────────────────────────────────────────┐
   Checker (Story 08)    │  PHASE 1: LINK GRAPH (build-time)            │
   validated type DAG ──▶│  • mandatory closure (ancestors/iface/trait) │
                         │  • [Preload] root set → closure              │
                         │  • optional PGO cohort sets (Story 23/24)    │
                         │  • stable IDs/hashes + manifest              │
                         └───────────────┬──────────────────────────────┘
                                         │
              ┌──────────────────────────┼───────────────────────────┐
              ▼                          ▼                           ▼
   PHASE 6: DUAL EMISSION       PHASE 4: LOWERING          PHASE 3: CANONICALIZATION
   (Emitter 09/11)              (functions/consts →        + INVARIANT (emitter)
   • readable PSR-4 (debug)     static members)            • consumes Story 08.5
   • optimized bundles (prod)   driven by `internal`         symbol-name types
                                + [GlobalFunction] opt-out  • alias→FQN canonicalization
                                                            • opaque-string conservatism
              │
              ▼
   PHASE 7: RUNTIME LOADER (three tiers)
   preload → Tyhp fast loader (prepend) → Composer PSR-4 fallback
              │
              ▼
   PHASE 8: DEBUGGABILITY (sourcemaps Story 17 + xdebug proxy Story 18)
```

### Key file locations (anticipated)

| Component | Path (anticipated) | Notes |
|-----------|--------------------|-------|
| Link graph / closure computation | `Tyhp/TyhpLang/Linker/` (new) | Consumes the checker's `SymbolTree` |
| Manifest writer | `Tyhp/TyhpLang/Linker/LinkManifest.cs` (new) | `class → bundle/file`, cohort sets, IDs |
| Bundle/preload emission | `Tyhp/TyhpLang/Emitter/` | Extends Story 09/11 emitter |
| Symbol-name type definitions | `Tyhp/TyhpLang/Binder/BuiltIn/UtilityTypes.cs` | Delivered by **Story 08.5** — `__`-prefixed types |
| Symbol-string canonicalization | `Tyhp/TyhpLang/Emitter/` | Emit-time rewriting using `UseIncludeSymbol` data |
| Runtime loader (emitted PHP) | build output (`output.path`) | `tyhp_autoload.php`, `tyhp_preload.php` |
| Profile toggle | `Tyhp/Config/Project.cs` | New `tyhp.json` keys (see Configuration) |

---

## The Three-Tier Linker Model

| Tier | Native analogy | Handles | Cost per class |
|------|----------------|---------|----------------|
| **1. Preload** (`opcache.preload`, topo-sorted) | Static linking | Hot core, `[Preload]` closure | **Zero** — no loader call ever fires |
| **2. Tyhp fast loader** (registered `prepend=true`, flat authoritative map, mandatory-cohort-eager) | Shared objects | Warm/lazy Tyhp + project classes | One hash lookup + one bundle `include` that satisfies the whole mandatory cohort |
| **3. Composer PSR-4** (fallback) | `dlopen` | Third-party vendor classes, dynamic/unknown names | Standard Composer cost (only on genuine misses) |

Per-deployment strategy (selected by the profile toggle):

| Target | Strategy | Why |
|--------|----------|-----|
| FPM / long-lived web | Preload (topo-sorted) + thin lazy tail | Amortize once across all workers (shared SHM) |
| Serverless / CLI | Whole-program single file + tree-shake | Cold start; no autoload at all |
| Big app, memory-bound | Flat authoritative map + feature chunks | Lazy, but O(1) and syscall-light |

---

## Phase 1: The Link Graph (build-time)

After type checking, compute per type/symbol:

- **Mandatory closure** — ancestors + interfaces + traits. PHP *must* load these to define the class, so under PSR-4
  each is a separate synchronous autoload round-trip. Bundling them is always correct and always a win.
- **`[Preload]` root set** — author-marked roots (Phase 5); the compiler computes and topo-sorts the transitive
  closure automatically.
- **Probable/cohort sets (optional)** — signature/coupling deps, optionally PGO-weighted. Only relevant to the
  *non-preloaded* lazy tail; implemented as an opt-in optimizer pass (Story 23/24).
- **Stable identity** — a content hash or sequential integer ID per type, for flat-file naming and the map.

Emit a **manifest** (the `composer dump-autoload` equivalent, but derived from real semantics): `class → bundle/file`,
cohort membership, IDs. Regenerated **atomically** with a content hash so partial rebuilds cannot serve stale maps.

**Correctness rule:** within any bundled file, declarations are emitted in **topological order** (parent before
child). PHP only early-binds a class when its parent is already defined; otherwise it binds in source order.

---

## Phase 2: Symbol-Name Types (delivered by Story 08.5)

> **Split out.** The symbol-name types — the `__ClassName`/`__FunctionName`/… built-in type **definitions** (all
> erasing to plain `string`), **type-guard narrowing** (`\class_exists` → `__ClassName`, …), **compile-time existence
> verification** on literal assignment, and **typed `nameof()`** — are now delivered by **Story 08.5**
> (`IMPLEMENTATION_PLAN_TODO_STORY_31.md` no longer defines them). They are a linker-independent checker feature whose
> only prerequisites are Story 06 (built-in type surface) and Story 08 (checker), so they were pulled forward.

What this story relies on Story 08.5 having delivered:

- The full symbol-name type set (`__VarName`, `__TypedVarName<T>`, `__FunctionName`, `__StructName`,
  `__ClassName<TObject extends object = object>` (bare ≡ `<object>`), `__EnumName<…>`, `__TraitName<…>`,
  `__InterfaceName<…>`, `__UsedTraitName<T>`, `__CompatibleTypeName<T>`, `__PropertyName<T>`,
  `__MethodName<T>` (owner only; exact-method utilities via `__MethodReturnType`), `__ConstName`,
  `__ObjectConstName<T>`, `__EnumCaseName<T>`), each erasing to `string`.
- Narrowing of `string` to those types via the standard PHP existence guards.
- Existence verification for string literals assigned to a symbol-name type.
- `nameof()` returning the precise symbol-name type.
- (Optionally, if it landed in Story 08.5 Phase 7) the type-name string algebra (`__TypeName`, `__UnionTypeName`, …).

**What this story (Story 31) adds on top:** the **emit-time canonicalization** of those typed symbol-strings —
expanding `use … as` aliases to real FQNs, remapping lowered functions/constants to their static-member homes, and
remapping relocated classes to their emitted file/name. That is the genuinely linker-specific part and is specified
in Phase 3.

See `IMPLEMENTATION_PLAN_TODO_STORY_08.5.md` for the full type table, narrowing guards, and existence-verification
behavior.

---

## Phase 3: The Lowering & Relocation Invariant

This is the safety backbone for the whole story. **A symbol may be lowered (Phase 4) or relocated (Phase 6) only if
every reference to it is statically known to the compiler.**

The hazard: any optimization that *moves* or *renames* a symbol breaks references the compiler cannot see. PHP hides
symbol references inside **string literals**, which are otherwise opaque:

```php
call_user_func("foo");            // 'foo' lowered to Fns::foo  → BROKEN
new $className;                   // $className = "User" (an alias) → wrong FQN
"App\\Models\\User"::someStatic;  // never alias-expanded
is_callable("helper");            // 'helper' was lowered          → wrong answer
```

### The two-part rule

1. **Typed symbol-strings are canonicalized at emit time.** When a string is *typed* as a symbol-name type (Phase 2)
   or produced by `nameof()`, the checker validates it and the **emitter rewrites it** to the canonical emitted
   location:
   - expand `use … as X` aliases to the real FQN (the data already lives in `UseIncludeSymbol.ImportedName` /
     `ImportedNameSegments`; `NameResolver.ResolveUseAlias` already does this for *name references*),
   - remap lowered functions/constants to their static-member home,
   - remap relocated classes to their emitted file/name.
2. **Opaque strings force conservatism.** If the compiler *cannot prove* a string is a symbol reference, then any
   symbol it *might* name **cannot be lowered/relocated** — it must stay at its canonical PHP-visible location (or keep
   a shim). The `[GlobalFunction]` opt-out (Phase 4) is the manual escape hatch for "I reference this dynamically."

Net effect: typed symbol-strings *expand* the set of safely optimizable symbols; opaque strings *shrink* it. **This
invariant is owned by the interop contract (Story 15)** as a hard, machine-checkable rule.

**Generated docblocks count as typed symbol-strings.** Story 22.1 Part A emits PHPStan/Psalm docblocks whose tags
name symbols (`class-string<App\User>`, `Collection<App\Models\Order>`, FQNs in `@param` / `@return` / `@extends`).
Those positions must be rewritten by the same lowering/relocation remap, so a relocated or lowered symbol is never
left stale inside a generated tag.

---

## Phase 4: Function/Constant Lowering

Lower free functions/constants to **static members on a synthetic class** so they ride the fast, lazy class-autoload
path instead of Composer's eager `files` includes.

- **Default policy — `internal`-driven (Story 25):**
  - `internal` free functions/constants → **lowered** (no external references possible by definition, always safe).
  - public/exported free functions/constants → **kept real** (or shimmed) by default, so name-based interop works.
- **Opt-out / override attribute — `[GlobalFunction]` / `[GlobalConstant]`:** forces a symbol to remain a genuine
  global-scope function/constant regardless of visibility (for `define()` semantics, reflection, framework hooks,
  `call_user_func('name')`, etc.).
- **Interop shims (Story 15):** the *exported* surface — anything callable by name from outside Tyhp — either stays a
  real function or gets a thin global-function shim (small file, or in preload).
- **Guarded by Phase 3:** a symbol referenced by an opaque string is never lowered.

---

## Phase 5: The `[Preload]` Attribute

A **linker root-set** mechanism. The author marks hot roots; the compiler computes and topo-sorts the closure.

- **Marks roots only; closure is automatic.** You never annotate a parent class just because you annotated its child —
  the compiler auto-includes ancestors/interfaces/traits in correct topo order.
- **Replaces the "probable closure" heuristic for the preload tier.** Deterministic, reviewable author intent instead
  of a fragile guess. Cohort bundling (Phase 1) remains an *optional* optimization for the lazy tail you deliberately
  did not preload.
- **Applies to functions/constants too** — a `[Preload]`-marked lowered "functions class" is how lowered
  functions/constants become truly zero-cost.
- **Profile-gated:** preloaded files cannot be redefined without a server restart, so `[Preload]` is a **no-op in
  the dev/debug profile** and only takes effect in the prod profile.
- **Dependency-declared `[Preload]`:** the **app owns its preload budget**. App-level `[Preload]` is always honored;
  dependency-level `[Preload]` is **advisory and config-gated** (see `link.honorDependencyPreload`).
- **Build report:** emit preload-set size (class count, approximate bytes); optionally a lint warning past a
  configurable threshold to discourage over-marking.
- **Vendor-in-closure:** if a `[Preload]` closure reaches a non-preloadable vendor class, preload what is possible; the
  vendor parent resolves via the Composer fallback on first use.

---

## Phase 6: Dual Emission & Bundling

One compile produces two artifact sets, selected by the profile toggle:

| Artifact set | Purpose | Loaded by |
|--------------|---------|-----------|
| **Readable** (per-class, PSR-4, commented) | Debugging, diffing, source of truth | Composer fallback, or Tyhp loader in dev mode |
| **Optimized** (concatenated, topo-sorted, minified, flat-mapped, preload-ready) | Production speed | Tyhp fast loader + preload |

Bundling strategies (granularity is configurable):

- **Whole-program single file** — CLI/serverless; tree-shaken to the reachable set only; zero autoload.
- **Feature/module chunks** — group by reachability cluster so loading one class pulls its natural cohort in a single
  `include`.
- **Flat authoritative map** — sequential IDs or content hashes name files; the loader callback becomes a single map
  lookup + `include`, with no namespace→path string work and no `is_file` fallback.

Bundles contain **declaration-only** units (no top-level side effects) so reordering/concatenation is safe. Minify
mainly for cold-parse + disk size; once opcache is warm the steady-state win comes from *fewer files + preload + no
PSR-4 probing*, not byte-shaving.

---

## Phase 7: The Runtime Loader

Emit `tyhp_preload.php` (Tier 1) and `tyhp_autoload.php` (Tier 2).

- **Registration:** register the Tyhp loader **after** `vendor/autoload.php` with `spl_autoload_register($cb, true, true)`
  so it sits at the **front** of the SPL queue (Composer auto-prepends its own loader by default; registering after it
  with `prepend=true` guarantees Tyhp is first). When Tyhp owns the entry point entirely, control the order directly.
- **Clean fall-through:** for a class it does not own, the callback **returns and does nothing** (does not throw, does
  not error) so the engine proceeds to Composer. Autoloader return values are ignored; only post-callback class
  existence matters.
- **Cohort-eager includes:** a single bundle `include` defines the requested class **and its mandatory cohort** in
  topo order; future cohort lookups never fire.
- **Authoritative for Tyhp-owned namespaces:** a miss within Tyhp-owned namespaces is a hard "does not exist" (no
  filesystem probe); unknown/vendor names fall through to Composer.

---

## Phase 8: Debuggability

Minified prod bundles destroy line numbers and readability, so the prod toggle depends on:

- **Sourcemaps (Story 17)** — map optimized bundle positions back to readable `.tyhp`/PSR-4 source.
- **XDebug proxy (Story 18)** — remap breakpoints and stack frames so prod bundles are debuggable.

This is the same model as JS minifiers shipping `.map` files; it is what makes leaving the prod toggle on tolerable.

---

## Phase 9: User documentation & AIDevGuide

Linking changes how a built application is laid out and loaded, which is the most visible thing this story does to an author's project. It needs published documentation, not just config-key registration.

**Published documentation (`docs/content/`):**

- **New** `docs/content/project_linking.md` — the three-tier model, what `debug` and `release` profiles produce, when Composer autoloading is still involved, and what a bundle looks like on disk. Register it in `docs/content/toc.json`.
- `docs/content/project_optionsList.md` — every `link.*` key from [Configuration](#configuration) with its default.
- `docs/content/project_composerPackages.md` / `docs/content/cli_build.md` — how linking interacts with Composer autoloading and with the normal build output layout.
- `docs/content/tyhp_2700_compileTimeConstructs.md` — the `[Preload]` attribute, and that a dependency's `[Preload]` is not honored by default.
- `docs/content/cli_sourcemapGeneration.md` / `docs/content/cli_xdebugProxy.md` — debugging a release bundle.
- `docs/content/diagnostics_reference.md` — the codes allocated at implementation time.

**AIDevGuide:** update `AIDevGuide/guide/26-build-cli.md` (the `link.*` keys and the output layout a release build produces), `guide/17-compile-time-helpers.md` (`[Preload]`), `handbook/02-autoloading.md` (linked applications versus Composer autoloading), and `handbook/03-build-cli-workflow.md`. Because `AIDevGuide/` is regenerated from the prompt in `AIDevGuide/REGEN.md`, add linking to prompt item 26 and to the handbook autoloading paragraph — otherwise a regeneration drops it.

**Acceptance:**

- [ ] Every `link.*` key is documented with its default
- [ ] Docs show what a `release` build produces and how to debug it
- [ ] `[Preload]` is documented, including the dependency-preload default
- [ ] `docs/content/toc.json` lists the new page
- [ ] `AIDevGuide/handbook/02-autoloading.md` covers linked output, and `REGEN.md` would regenerate it

---

## Configuration

Provisional `tyhp.json` keys (register in `CONVENTIONS.md` §4 when scheduled; `Tyhp/Config/Project.cs` is
authoritative at implementation time):

| Key | Meaning |
|-----|---------|
| `link.profile` | `"debug"` (readable + Composer) or `"release"` (optimized bundles + preload). Default `"debug"`. |
| `link.bundling` | `"none"` \| `"chunks"` \| `"whole-program"`. Default `"none"`. |
| `link.preload` | Enable preload-script emission in release profile. Default `true` in release. |
| `link.honorDependencyPreload` | Whether dependency-declared `[Preload]` is honored. Default `false` (app owns budget). |
| `link.lowerFunctions` | Master switch for function/constant lowering. Default `true` (policy still `internal`-driven). |
| `link.flatMap` | Use flat authoritative ID/hash file layout for the lazy tail. Default `false`. |
| `link.preloadWarnThreshold` | Class-count threshold above which a preload-bloat warning is emitted. |

> Diagnostics introduced by this story (existence-verification errors for symbol-name types, invariant violations,
> preload-bloat warnings, etc.) are added to `Tyhp/Domain/Exceptions/MessageCode.cs` with matching `.resx` entries at
> implementation time — per `CONVENTIONS.md` §1, codes are **not** allocated in this story doc.

---

## Decisions (defaults chosen)

These were the five open questions from design; defaults are chosen so the story is actionable, and flagged for
confirmation when scheduled.

1. **Lowering default → `internal`-driven** (lower internal, keep public real/shimmed; `[GlobalFunction]` overrides).
   Ties to Story 25 instead of a standalone rule.
2. **Symbol-name types → split into Story 08.5** (definitions, narrowing, existence verification, typed `nameof()`).
   They are a linker-independent checker feature and a hard prerequisite for safe lowering, so they now land early in
   Tier 0; this story consumes them and adds only the emit-time canonicalization (Phase 3) before Phase 4/6.
3. **Dependency `[Preload]` → config-gated** (`link.honorDependencyPreload`, default off; app owns the budget).
4. **Cohort bundling → optional optimizer pass** (Story 23/24); core path is mandatory-closure bundling only.
5. **Attribute names → `[Preload]`, `[GlobalFunction]` / `[GlobalConstant]`** (opt-out for lowering).

---

## Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Preload is deploy-time only (cannot invalidate without restart) | `[Preload]` and preload emission are no-ops in the debug profile |
| 2 | Dynamic class names (`new $x`, DI containers, reflection) the graph cannot see | Flat map covers known classes; unknown FQCNs fall through to Composer; never assume the graph is total |
| 3 | `class_exists($x, false)` semantics shift (cohort defines members earlier) | Low risk; document it; keep cohort bundling opt-in/tunable |
| 4 | Function-lowering breaks name-based interop | `internal`-driven default + `[GlobalFunction]` + Story 15 shims; guarded by the Phase 3 invariant |
| 5 | Side-effect ordering changes under reordering/bundling | Bundle declaration-only units; keep side-effectful inits explicit and ordered |
| 6 | Over-eager probable-closure wastes memory | Cohort bundling is opt-in and PGO-driven, not guessed |
| 7 | Artifact staleness on partial rebuilds | Atomic regeneration + content-hash manifest |
| 8 | Registration order vs. Composer auto-prepend | Register after `vendor/autoload.php` with `prepend=true`, or own the bootstrap |
| 9 | Cross-boundary inheritance (Tyhp class extends vendor class) | Nested autoload is allowed; do not topo-sort vendor classes into Tyhp bundles — they resolve via fallback |
| 10 | Minification's steady-state payoff is modest once opcache is warm | Set expectations: wins come from fewer files + preload + no PSR-4 probing |
| 11 | Namespace partition assumption (Tyhp vs vendor) | Keep partitions disjoint so `classmap-authoritative` Composer setups orphan nothing |
| 12 | Opaque-string references to symbols slated for lowering/relocation | Phase 3 invariant: such symbols stay at canonical location / keep shims |
| 13 | Minified release bundles drop the PHPStan/Psalm docblocks that Story 22.1 emits in every build | Bundling keeps generated docblocks unless `emit.docblocks` is `"off"`; their symbol names follow the Phase 3 remap |

---

## Golden Fixtures / Tests (Acceptance)

Per `CONVENTIONS.md` §5, add `.tyhp → .php` (+ expected-diagnostics) golden fixtures under
`tests/conformance/story31/<feature>/` with a `manifest.json` the runner asserts.

- **Symbol-name types:** narrowing, literal-assignment existence verification, and erasure to plain `string` are
  covered by **Story 08.5**'s fixtures; this story depends on them passing but does not re-own them.
- **Canonicalization:** typed symbol-string with a `use … as` alias emits the real FQN; `nameof()` of a lowered
  function emits the static-member reference; opaque-string reference *prevents* lowering of the named symbol.
- **Lowering:** `internal` function lowered to static member; public function kept real; `[GlobalFunction]` override;
  emitted PHP shape verified.
- **`[Preload]`:** root marking produces a topo-sorted, linkable preload script including the mandatory closure;
  no-op under the debug profile; build-report size assertion.
- **Loader:** fast loader registered at front of SPL queue; clean fall-through to Composer for vendor classes;
  cohort `include` defines the whole mandatory cohort in one shot.
- **Dual emission:** debug profile produces readable PSR-4 loadable by Composer; release profile produces optimized
  bundles + preload; whole-program build is tree-shaken to the reachable set.
- **Invariant gate (Story 15):** a build that would lower/relocate a symbol with an unprovable reference is rejected or
  conservatively keeps the symbol in place.
- **Self-host:** keep the runtime self-host conformance diff green if the runtime is rebuilt under release profile.

---

## Appendix A: Eager-Resolution Optimization for the Binder Two-Pass

> **Scope note:** This is a *binder name-resolution performance* optimization, not strictly a linker feature. It is
> parked here because it shares the "resolve symbol references" theme and is a useful precursor to the link-graph work
> in Phase 1 (a faster, incrementally-populated resolution pass makes closure computation cheaper). It can be lifted
> into its own story or a binder-performance story if that reads cleaner when scheduled.

### Current behavior

`TyhpBinder.Bind()` (`Tyhp/TyhpLang/Binder/TyhpBinder.cs`) runs two passes:

1. **Pass 1 — declaration walk** (`BindFile` per source file): registers every declaration into the scope tree, but
   leaves all type references (`extends`/`implements`, parameter/return/property/constant types, generic
   constraints/defaults, extension targets, etc.) **unresolved**.
2. **Pass 2 — resolution walk** (`RunResolutionPass` → `ResolveInScope`): recursively re-walks the **entire** scope
   tree a second time and calls `NameResolver.ResolveType(...)` on every reference.

The cost: pass 2 re-traverses the whole tree (every file scope, namespace block, object/function/method scope, and
nested code block) even though, by the time the declaration walk finishes, the *vast majority* of references already
have an unambiguous, declared target that could have been resolved the moment it was encountered.

### Proposed optimization

Fold most resolution into Pass 1 and replace the full Pass-2 tree walk with a drain of a **deferred-reference work
list**:

1. **Eagerly resolve during Pass 1 when (and only when) the answer is order-independent.** As `BindFile` registers a
   symbol and encounters a type reference, attempt `ResolveType` immediately. If it resolves to an already-declared
   target *and that result cannot change as more declarations are bound*, record the resolution now.
2. **Defer the rest into a list.** Any reference that does not yet resolve — or whose resolution is **not provably
   stable** under later declarations — is appended to a `_deferredResolutions` work list as a
   `(symbol-or-reference, owning-scope)` pair, in encounter order.
3. **Pass 2 drains the list instead of walking the tree.** `RunResolutionPass` iterates `_deferredResolutions` once
   and resolves each entry, emitting the existing unresolved-reference diagnostics
   (`BinderUnresolvedExtendsType`, `BinderUnresolvedParameterType`, …) for anything still unresolved. No recursive
   scope re-traversal.

### Correctness: the resolution-order hazard (the careful part)

Eager resolution is **only safe when the result is final regardless of declarations not yet seen.** Tyhp/PHP name
resolution for an *unqualified or relative* name depends on context that Pass 1 has not finished populating at the
moment the reference is first seen:

- a **sibling** declaration later in the same file/namespace can be the correct target,
- the **same namespace declared across multiple files** is not fully populated until every file's Pass 1 completes,
- `use` imports / aliases (`UseIncludeSymbol`) in the file may appear after the reference,
- a relative name must bind to a closer (sibling/namespace-local) symbol **before** falling through to a root-level
  symbol of the same name — so eagerly binding an unqualified `Foo` to a root `\Foo` is **wrong** if a namespace-local
  `Foo` is declared later.

Therefore the eager path must be conservative:

- **Safe to resolve eagerly:** fully-qualified (`\`-prefixed / absolute) names whose target is already declared, and
  references to built-ins / tyhpdef symbols (already fully loaded before the declaration walk via
  `PopulateBuiltIns` + `LoadTyhpdefSymbols`). These are immune to later sibling/import shadowing.
- **Must defer:** every unqualified/relative name (it may be shadowed by a not-yet-bound sibling, namespace-local
  declaration, or `use` import), and anything that does not resolve on first attempt.

A safe, simpler first cut is to **only eager-resolve fully-qualified names + built-in/tyhpdef references** and defer
everything else; the deferred list is then drained after *all* files' declaration walks complete, preserving today's
exact resolution semantics while still eliminating the full second tree traversal for the common cases.

### Sketch

```csharp
// Pass 1: during BindFile, when a reference is encountered:
if (IsOrderIndependent(typeRef, scope)              // fully-qualified, or built-in/tyhpdef
    && NameResolver.TryResolveStable(typeRef, scope) is { } sym)
{
    Bind(typeRef, sym);                              // resolve now
}
else
{
    _deferredResolutions.Add((typeRef, scope));      // resolve in Pass 2
}

// Pass 2: drain only the deferred work list (no tree walk)
foreach (var (typeRef, scope) in _deferredResolutions)
{
    var resolved = NameResolver.ResolveType(typeRef, scope);
    if (resolved == null)
        EmitUnresolvedDiagnostic(typeRef, scope);    // same diagnostics as today
}
```

### Risks & notes

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Eagerly binding an unqualified name to a root symbol that a later sibling should shadow | Restrict eager path to fully-qualified + built-in/tyhpdef references; defer all unqualified/relative names |
| 2 | Cross-file same-namespace population incomplete mid-Pass-1 | Drain the deferred list only **after** all files' declaration walks finish (it already is, since Pass 2 runs after the file loop) |
| 3 | Method/function references intentionally deferred today (generic params in scope) | Keep deferring them via the same list; the list ordering preserves the "method-level generics in scope" guarantee |
| 4 | Diagnostic parity (line/column/message) must not regress | Drain path reuses the existing `ResolveObjectDeclarationTypes`/`ResolveFunctionTypes`/`ResolveGenericParameterConstraints` diagnostic calls |

### Acceptance

- Resolution output (resolved targets + emitted diagnostics) is **byte-identical** to the current full-tree Pass 2
  across the runtime self-host build and the conformance corpus.
- Measurable reduction in Pass-2 work (e.g. count of scopes visited / `ResolveType` calls) on a large build.
- Fully-qualified and built-in/tyhpdef references are resolved in Pass 1; unqualified/relative references are still
  resolved correctly (including sibling-shadows-root cases) via the deferred drain.
