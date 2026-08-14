# Implementation Plan: Story 31 — Future Ideas & Optimizations

> **About this story:** Story 31 is a **collection of future ideas and optimizations** rather than a single feature.
> Each idea is self-contained and may later be lifted into its own story when scheduled. The current contents are:
>
> - **Idea 1 — Tyhp Link (compiled autoloading & linking):** the build-time linker + tiered runtime loader (the
>   original body of this document; everything from [Summary](#summary) through the acceptance fixtures).
> - **Idea 2 — Scalar-conversion contracts (`*Convertible`, Stringable-style casts):** interface-driven cast
>   lowering so objects can define how they convert to `string`/`int`/`float`/`bool`/`decimal`, mirroring how PHP
>   routes string context through `\Stringable::__toString()`. See [Idea 2](#idea-2--scalar-conversion-contracts-convertible-stringable-style-casts).
> - **Idea 3 — Default interface implementations (interface-attached traits):** let an interface declare `use SomeTrait;`
>   in its body so implementers automatically inherit those traits, removing the "write `implements` *and* `use`"
>   duplication. See [Idea 3](#idea-3--default-interface-implementations-interface-attached-traits).
> - **Idea 4 — Emit static-analysis docblocks (PHPStan/Psalm/PHPDoc):** generate the docblock tags that carry the
>   type fidelity native PHP declarations cannot (generics, array shapes, `class-string`, callable arg types), so the
>   emitted PHP type-checks and autocompletes as precisely as the Tyhp source. See
>   [Idea 4](#idea-4--emit-static-analysis-docblocks-phpstanpsalmphpdoc).
> - **Idea 5 — `operator default()` (author-defined default value for a class type):** let a class declare the value
>   `default(MyClass)` produces, so object types can have a real default instead of always yielding `null`. See
>   [Idea 5](#idea-5--operator-default-author-defined-default-value-for-a-class-type).
> - **Idea 6 — No uninitialized storage (ban `unset` on properties/locals; require property init):** erase PHP's
>   uninitialized property/local slot from Tyhp — no `AllowUnset`, statics need initializers, instance props must be
>   set by declaration/promotion/ctor, locals stay definite-assignment. See
>   [Idea 6](#idea-6--no-uninitialized-storage-ban-unset-require-property-init).
> - **Idea 7 — Conditional return types (v1):** unify function/method overloads with generic conditional/`match`
>   return types (`T extends X ? A : B`, nested, mixable), including bitmask **flag-presence** (`contains F`).
>   Overloads desugar to the same checker IR when the set is a clean discriminant partition. Non-distributive. See
>   [Idea 7](#idea-7--conditional-return-types-v1-overload-desugar--extends----match).
> - **Idea 8 — Advanced conditional types (v2):** `infer`, explicit distributive `...T` / `match (...T)`, recursive
>   conditionals, tuple/variadic inference, template-literal type inference, and body-vs-arms checking. See
>   [Idea 8](#idea-8--advanced-conditional-types-v2).
> - **Idea 9 — Compiler plugins v1 (Tyhp/PHP host, `p'ns:…'` / `p"ns:…"` / `p<<<`, backtick ops):** out-of-process
>   plugins with staged hooks (`AstTransform` pre-binder, `Check`, `Emit`); **Composer packages** with root
>   **`plugin.tyhp.json`** (manifest); project + global discovery; compile-time path autoload-excluded; namespaced
>   DSL islands (quotes + heredoc/nowdoc); backtick ops; process order, options schema, test harness.
>   See [Idea 9](#idea-9--compiler-plugins-tyhpphp-host-pns--backtick-operators).
> - **Idea 10 — Compiler plugins v2 (typed transform, IDE, statements, sandbox, …):** post-binder AST transform +
>   rebind flag; TextMate/VS Code (Story 19.5) island highlighting; diagnostic spans/fix-its; statement-form islands;
>   sandbox (TBD); shared plugin cache; optimizer hooks. See
>   [Idea 10](#idea-10--compiler-plugins-v2).
> - **Idea 11 — Pipe-emit chained extension calls (`|>` on PHP 8.5+):** when a chain of extension methods
>   (`$s->trimmed()->lower()->dashed()`) rewrites to nested static calls, emit PHP 8.5 `|>` instead **when able**
>   (receiver-only calls; `output.phpVersion` ≥ 8.5). See
>   [Idea 11](#idea-11--pipe-emit-chained-extension-calls--on-php-85).
> - **Idea 12 — Trait-requirement abstract members:** when a trait `extends` / `implements`, emit PHP
>   `abstract` members for the **used** signatures from those targets (PHP still cannot name the class/interface
>   on the trait). See
>   [Idea 12](#idea-12--trait-requirement-abstract-members).
> - **Appendix A — Eager-resolution optimization for the binder two-pass:** a binder name-resolution performance
>   optimization (fold resolution into Pass 1 + drain a deferred list in Pass 2).

> **Roadmap position:** Story 31 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 06, 08, 08.5, 09, 11, 13, 15, 17, 18, 23, 24, 25
> **New story:** created during autoloading/linking brainstorm; no legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single
> source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating
> ranges. New `tyhp.json` keys proposed here are **provisional** and must be registered in `CONVENTIONS.md` §4
> when the story is scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Source:** Autoloading/linking design discussion; Ideas 9–10 from compiler-plugins brainstorm 2026-08-05;
> Idea 11 from extension-chain emit brainstorm 2026-08-14; Idea 12 from trait-requirements emit brainstorm
> 2026-08-14
> **Branch:** TBD
> **Generated:** 2026-06-18
> **Updated:** 2026-08-14 (Idea 12 — trait-requirement abstract members)
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
- [Configuration](#configuration)
- [Decisions (defaults chosen)](#decisions-defaults-chosen)
- [Risks & Edge Cases](#risks--edge-cases)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)
- [Idea 2 — Scalar-Conversion Contracts (`*Convertible`, Stringable-style casts)](#idea-2--scalar-conversion-contracts-convertible-stringable-style-casts)
- [Idea 3 — Default Interface Implementations (interface-attached traits)](#idea-3--default-interface-implementations-interface-attached-traits)
- [Idea 4 — Emit Static-Analysis Docblocks (PHPStan/Psalm/PHPDoc)](#idea-4--emit-static-analysis-docblocks-phpstanpsalmphpdoc)
- [Idea 5 — `operator default()` (Author-Defined Default Value for a Class Type)](#idea-5--operator-default-author-defined-default-value-for-a-class-type)
- [Idea 6 — No Uninitialized Storage (Ban `unset`, Require Property Init)](#idea-6--no-uninitialized-storage-ban-unset-require-property-init)
- [Idea 7 — Conditional Return Types (v1: Overload Desugar + `extends ? :` + `match`)](#idea-7--conditional-return-types-v1-overload-desugar--extends----match)
- [Idea 8 — Advanced Conditional Types (v2)](#idea-8--advanced-conditional-types-v2)
- [Idea 9 — Compiler Plugins v1 (Tyhp/PHP Host, Hook Surfaces, `p"ns:…"`, Backtick Operators)](#idea-9--compiler-plugins-tyhpphp-host-pns--backtick-operators)
- [Idea 10 — Compiler Plugins v2](#idea-10--compiler-plugins-v2)
- [Idea 11 — Pipe-Emit Chained Extension Calls (`|>` on PHP 8.5+)](#idea-11--pipe-emit-chained-extension-calls--on-php-85)
- [Idea 12 — Trait-Requirement Abstract Members](#idea-12--trait-requirement-abstract-members)
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

## Idea 2 — Scalar-Conversion Contracts (`*Convertible`, Stringable-style casts)

> **Status:** Tier 4, future plans. Self-contained; may be lifted into its own story when scheduled.
> **Prerequisites:** Story 06 (built-in types & cast tokens), Story 08 (checker / type inference), Story 09/11
> (emitter expression lowering), Story 15 (interop contract — the emitter may only call the listed `\Tyhp\*`
> runtime surface). Ties into the existing `operator convert` mechanism (Decimal) — see Decision 5.

### Summary

A set of runtime contracts — `\Tyhp\Contracts\StringConvertible` (extends native `\Stringable`), `IntConvertible`,
`FloatConvertible`, `BoolConvertible`, and `\Tyhp\Contracts\DecimalConvertible` — that let an object define how it
converts to a scalar, plus a **compiler rule that rewrites cast expressions to call the contract method** when the
operand implements the matching interface. This mirrors how PHP natively routes string context through
`\Stringable::__toString()`, and generalizes it to `int`/`float`/`bool`/`decimal`, which PHP has **no** native hook
for.

| Cast token | Contract | Method | Fallback when the operand does **not** implement it |
|------------|----------|--------|------------------------------------------------------|
| `(string)` (`T_STRING_CAST`) | `StringConvertible` *(extends `\Stringable`)* | `__toString(): string` | native `(string)` |
| `(int)` (`T_INT_CAST`) | `IntConvertible` | `__toInt(): int` | native `(int)` |
| `(float)` (`T_DOUBLE_CAST`) | `FloatConvertible` | `__toFloat(): float` | native `(float)` |
| `(bool)` (`T_BOOL_CAST`) | `BoolConvertible` | `__toBool(): bool` | native `(bool)` |
| `(decimal)` (`T_DECIMAL_CAST`) | `DecimalConvertible` | `__toDecimal(): \Tyhp\Decimal` | `\Tyhp\decimal($x)` — already dispatches on the contract internally |

> **Where this lives today.** Casts are lexed to the `T_*_CAST` tokens, typed in
> `Tyhp/TyhpLang/Checker/TypeInferrer.Expressions.cs` (+ `Checker/Rules/CodeQualityRule.cs`), and emitted verbatim
> as native PHP casts by `TyhpEmitter.BuildUnaryExpression` (`Tyhp/TyhpLang/Emitter/TyhpEmitter.Expressions.cs`).
> `(decimal) $x` already lowers to `\Tyhp\decimal($x)`, and `\Tyhp\decimal()` / `Decimal::__construct` already do
> `$value instanceof DecimalConvertible ? $value->__toDecimal() : …`. This idea adds the *general* interface-driven
> rewrite for the other four casts and formalizes the decimal case.

### Motivation

PHP gives `\Stringable` special engine treatment: any object used in a string context (cast, concat, interpolation,
`echo`, `%s`, string-typed parameters in weak mode) has `__toString()` called automatically. There is no equivalent
for `int`/`float`/`bool`: `(int) $obj` on an arbitrary object emits a **warning and yields `1`**, `(float) $obj`
yields `1.0`, `(bool) $obj` is **always `true`**, and `(string) $obj` without `__toString` is a fatal `Error`. So
today a Tyhp value object (money, a typed ID, a measurement) cannot participate in casts the way a native scalar can.

The `*Convertible` contracts close that gap: a class opts in by implementing the interface, and every matching cast
site calls the method instead of the lossy/loud native cast.

### Emission model (the core rewrite)

For a cast `(T) $expr`, the emitter chooses one of three lowerings from the operand's **static type** (`T` = the
target scalar, `IFace` = the matching contract, `m()` = the method):

1. **Statically implements the contract** → direct call, no guard:

```php
$expr->__toInt()
```

2. **Statically a scalar / provably cannot implement it** (`int`, `string`, `float`, `bool`, `array`, `null`, an
   enum, a `final` class that does not implement the contract, …) → **native cast unchanged**:

```php
(int) $expr
```

3. **Unknown / `mixed` / `object` / a union that *might* implement it** → **runtime guard** (assign to a temp first
   so a side-effecting operand is evaluated once):

```php
(($__c = $expr) instanceof \Tyhp\Contracts\IntConvertible ? $__c->__toInt() : (int) $__c)
```

Special cases:

- **`(string)`** — because `StringConvertible extends \Stringable` and PHP already routes `(string)` (and every
  implicit string context) through `__toString()`, the explicit `(string)` cast needs **no rewrite**; the contract
  exists for *type* parity (using it as a type / `instanceof`) and to make the string case symmetric with the others.
- **`(decimal)`** — not a real PHP cast; keeps lowering to `\Tyhp\decimal($expr)`, which already dispatches on
  `DecimalConvertible` at runtime. No separate `instanceof` guard is emitted (avoids double-dispatch), and this keeps
  the emitter within the Story 15 interop surface (`\Tyhp\decimal` is already listed).
- **Nullable operands (`?Foo`)** — the guard's `else` branch (native cast) already reproduces PHP's null-cast
  semantics (`(int) null === 0`, `(string) null === ''`, `(bool) null === false`, `(float) null === 0.0`), so no
  extra null handling is needed; `instanceof` is false for `null`.
- **Temp-variable hygiene** — the guard must use a fresh, collision-free temp (emit-scoped counter) and only when the
  operand is not already a simple variable; a bare `$var` operand can be referenced twice safely.

### What else `\Stringable` does that we would need to emulate

Direct answer to the design question. Ordered from "already covered by requirement 1" to "larger follow-on scope":

1. **Implicit-context coercion, not just explicit casts (the big one).** PHP calls `__toString()` in *every* string
   context, not only `(string)`: concatenation (`.`), interpolation (`"$x"` / `"{$x}"`), `echo`/`print`,
   `sprintf('%s')`, and `string`-typed parameters/returns in weak mode. Full parity for the numeric/bool contracts
   would mean firing them in the analogous implicit contexts:
   - arithmetic operators and comparisons (numeric context),
   - boolean contexts: `if`/`while` conditions, `!`, `&&`/`||`, ternary condition, `empty()`,
   - `int`/`float`/`bool`-typed parameters and returns in weak mode.
   This is the expensive part (the checker must *inject* conversions at coercion points, not just rewrite an explicit
   token) and is a **scope decision**. Recommendation: **Phase 1 = explicit casts only** (matches requirement 1 and is
   fully deterministic); implicit contexts are a Phase 2 follow-on. Note the asymmetry: `(string)` /
   `StringConvertible` gets implicit string contexts **for free** from the engine because `\Stringable` is native;
   `int`/`float`/`bool` do **not**, so implicit-context parity for those is purely a compiler feature.

2. **Implicit interface satisfaction from the magic method.** PHP auto-implements `\Stringable` for any class that
   declares `__toString()` — you never write `implements \Stringable`. Decision: do we treat declaring
   `__toInt()`/`__toFloat()`/`__toBool()`/`__toDecimal()` as *implicitly* implementing the corresponding contract
   (checker synthesizes the `implements`)? Recommendation: **yes**, for parity and ergonomics — the checker adds the
   `implements` so `instanceof`, type acceptance, and the cast rewrite all "just work", matching how the engine treats
   `__toString`.

3. **First-class use as a type + `instanceof`.** `\Stringable` works as a parameter/return/property type and in
   `instanceof`. The contracts must be equally first-class: `function f(int|IntConvertible $x)`,
   `$x instanceof FloatConvertible`, `?BoolConvertible`, etc. This mostly falls out of them being ordinary interfaces,
   but the checker's cast/coercion rules must **accept `T|TConvertible` wherever a bare `T` is accepted**.

4. **Conversion methods may throw.** Since PHP 7.4 `__toString()` may throw (older PHP made it a fatal error). The
   contract is: conversion methods **may throw**, and the emitted guard must let the exception propagate (never
   swallow it). Only relevant if we ever targeted PHP < 7.4, which the version floor rules out.

5. **Return-type enforcement.** PHP enforces `__toString(): string`. The contracts already type their returns
   (`: int`, `: float`, `: bool`, `: \Tyhp\Decimal`); the checker should enforce those on implementers and the
   emitter should trust them (no re-coercion) — except decimal, whose method returns a `\Tyhp\Decimal` *object*, not
   a scalar.

6. **Conversion helper functions (`strval`/`intval`/`floatval`/`boolval`, `settype`).** `strval()` already honors
   `\Stringable` natively. For parity the compiler could rewrite `intval($obj)`/`floatval($obj)`/`boolval($obj)` /
   `settype($obj, …)` to route through the contracts with the same guard. Recommendation: **out of scope for Phase 1**;
   listed as a parity follow-on (identical guard, different call sites).

7. **Precedence vs. `operator convert`.** Tyhp already has `operator convert(self $v): int` (Decimal defines
   convert-to-scalar operators, emitted via `OperatorMethodNameGenerator` / `AliasConverter`). A type could have
   **both** a `convert`-to-scalar operator and a `*Convertible` contract. Recommendation: at a **cast site the
   contract method wins** (it is the explicit, PHP-parity affordance); alternatively make the two mutually exclusive
   with a checker diagnostic. **Flag for confirmation when scheduled.**

8. **Object-target native fallback is intentionally lossy.** For the guard's `else` branch, remember native
   `(int) $obj` / `(float) $obj` on a contract-less object **warns and yields `1`/`1.0`**, `(bool) $obj` is always
   `true`, and `(string) $obj` without `__toString` is a fatal `Error`. That is faithful PHP behavior — the contract
   is the *fix*. The checker **may** additionally warn when a cast targets an object type that implements neither the
   contract nor the native affordance (a likely bug).

### Decisions (defaults chosen)

1. **Scope → explicit casts only in Phase 1** (Behavior 1). Implicit-context coercion is a Phase 2 follow-on.
2. **`(string)` → no rewrite** (native `\Stringable` already covers it); the contract exists for typing/parity.
3. **`(decimal)` → keep the `\Tyhp\decimal($x)` lowering** (already dispatches on `DecimalConvertible`); no extra guard.
4. **Magic method → implicit `implements`** (Behavior 2): declaring the method synthesizes the contract in the checker.
5. **Precedence → contract method wins at a cast site** over `operator convert` (Behavior 7); flag for confirmation.
6. **Helper functions (`intval`/…/`settype`) → out of scope for Phase 1** (Behavior 6).

### Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Double evaluation of a side-effecting operand under the runtime guard | Bind the operand to a fresh emit-scoped temp; skip the temp when the operand is a bare variable |
| 2 | Overlap / ambiguity with `operator convert` to a scalar | Decision 5: contract wins at cast sites; consider a checker diagnostic for the both-defined case |
| 3 | `(string)` double-handling (rewrite *and* native `__toString`) | Decision 2: never rewrite `(string)`; rely on native `\Stringable` |
| 4 | Decimal double-dispatch (guard + `\Tyhp\decimal` both testing the contract) | Decision 3: emit only `\Tyhp\decimal($x)`; let the runtime function dispatch |
| 5 | Interop surface violation (emitter calling a non-approved `\Tyhp\*` symbol) | Only `\Tyhp\decimal` and the `\Tyhp\Contracts\*` interfaces are referenced; register with Story 15 |
| 6 | Contract method throwing inside the ternary guard | Guard lets exceptions propagate; documented as allowed (Behavior 4) |
| 7 | Nullable/`mixed` operands producing wrong null-cast semantics | `instanceof` is false for `null`; `else` branch reproduces native null-cast behavior |
| 8 | Scope creep into implicit contexts before the checker can inject coercions safely | Gate implicit-context parity behind Phase 2; Phase 1 is token-local and deterministic |

### Golden Fixtures / Tests (Acceptance)

Per `CONVENTIONS.md` §5, add `.tyhp → .php` (+ expected-diagnostics) golden fixtures under
`tests/conformance/story31/convertable/` with a `manifest.json` the runner asserts.

- **Static direct call:** operand statically implements `IntConvertible` → `(int) $x` emits `$x->__toInt()`.
- **Static native passthrough:** operand statically `int`/`string`/enum/`final` non-implementer → native cast unchanged.
- **Runtime guard:** operand `mixed`/`object`/`Foo|int` → ternary `instanceof` guard with a single-evaluation temp.
- **`(string)`:** never rewritten; native `(string)`/implicit string contexts still call `__toString` via `\Stringable`.
- **`(decimal)`:** lowers to `\Tyhp\decimal($x)`; a `DecimalConvertible` implementer round-trips through `__toDecimal`.
- **Implicit `implements`:** a class declaring `__toFloat()` (no explicit `implements`) satisfies `instanceof FloatConvertible`.
- **Precedence:** a type with both `operator convert(self): int` and `IntConvertible` uses the contract at the cast site.
- **Throwing method:** a conversion method that throws propagates out of the cast expression.
- **Self-host:** keep the runtime self-host conformance diff green (Decimal implements all five contracts already).

---

## Idea 3 — Default Interface Implementations (interface-attached traits)

> **Status:** Tier 4, future plans. Self-contained; may be lifted into its own story when scheduled.
> **Prerequisites:** Story 06 (built-in types / trait surface), Story 08 (checker), Story 09/11 (emitter — this is
> where the flattening happens). Interacts with Idea 2 (a natural delivery mechanism for `*Convertible` default
> cross-conversions) but does not depend on it.

### Summary

Let an **interface declare `use SomeTrait;` in its body**. When a class `implements` that interface, the compiler
**automatically injects the attached trait(s) into the class**, so the author no longer writes both `implements I`
*and* `use ITrait`. This is the well-precedented **default-interface-implementation** pattern (Java 8 default
methods, C# 8 default interface methods, Scala/Rust default trait methods; PHP itself has floated it in several
RFCs).

```tyhp
interface StringConvertible extends \Stringable {
    use DefaultStringConvertible;   // Tyhp-only: flattened into every implementer
}

// before: class Money implements StringConvertible { use DefaultStringConvertible; ... }
// after:  class Money implements StringConvertible { ... }   // trait injected automatically
```

> **Why this beats the erased generic approach.** An earlier design put a generic `__to<T>()` on a base
> `Convertible<T>` plus a `HasConvertible` trait that dispatched at runtime via `$this instanceof …`. That is a dead
> end under Tyhp's **erased generics** (`guide/07-generics.md`): `T` does not exist at runtime, so the method cannot
> pick a branch, and a class implementing several `*Convertible` contracts would always hit the first `instanceof`.
> Interface-attached traits sidestep erasure entirely — everything is resolved and flattened **at compile time** —
> and they generalize to any interface, not just conversion.

### Motivation

Traits and interfaces are two halves of the same intent: the interface is the *contract*, the trait is a *reusable
implementation of that contract*. Today the author must repeat both:

```tyhp
class A implements Comparable { use ComparableDefault; }
class B implements Comparable { use ComparableDefault; }   // forgot the `use`? silent gap / fatal
```

Forgetting the `use` is a common bug (a required method silently missing, or a fatal at class load). Attaching the
trait to the interface makes the implementation travel with the contract, so `implements` alone is sufficient.

For **Idea 2** specifically, this is where useful *default cross-conversions* live: implement `__toInt()` and get a
default `__toString()`/`__toFloat()`/`__toBool()` derived from it, each overridable.

### Syntax

Allow a `use`-trait statement inside an interface body (new grammar + binder support):

```tyhp
interface I extends J {
    use TraitA;
    use TraitB;
    public function required(): void;   // still a normal abstract signature
}
```

- Only **`use <trait>`** is meaningful in an interface body — no properties, no method bodies written inline (those
  live in the trait). Constants and method signatures remain as today.
- Attachment is **transitive through `extends`**: an implementer collects attached traits from the entire interface
  ancestry.

### Lowering model (compile-time flattening)

PHP interfaces **cannot** hold traits or method bodies, so this is pure emit-time sugar — the interface still emits
as a plain PHP interface (signatures/constants only). The work happens when emitting each **class**:

1. Compute the transitive set of interfaces the class implements (direct + inherited + via other interfaces).
2. Collect every trait attached to any interface in that set.
3. **Deduplicate by distinct trait** (a diamond where two interfaces attach the same trait flattens to one `use`).
4. Subtract traits the class already `use`s explicitly, and members the class defines itself (see precedence).
5. Emit a single `use TraitX, TraitY, …;` block (plus any needed adaptation block, see below) in the class body.

The interface's own emitted PHP is unchanged (no trait reference — PHP would reject it).

### Precedence & collision resolution (the careful part)

PHP's existing rules do most of the work and shrink the hard case:

1. **Class member wins.** PHP precedence is *class > trait > inherited*. If the class defines the method/property
   itself, no attached-trait member is injected for it. **Overriding is just "write the method."** This dissolves
   most apparent collisions into ordinary author intent.
2. **Same trait via multiple interface paths is safe.** Dedup in step 3 means a diamond of the *same* trait is a
   no-op, not a conflict.
3. **The only real conflict:** two **different** attached traits define the same non-abstract member, and the class
   does **not** provide it. (Abstract-in-one is fine; identical property definitions are fine; differing property
   definitions are a PHP fatal, same as hand-written `use`.)

For that residual conflict, resolution options (default + escape hatch chosen; fancier form deferred):

- **(Default) Hard compile error + explicit-`use` escape hatch.** Auto-inject only when unambiguous. On an
  unresolved trait-vs-trait collision, emit a diagnostic and let the author take manual control by writing a normal
  PHP-style block in the class:

```tyhp
class Foo implements StringConvertible, LegacyStringable {
    use DefaultStringConvertible, LegacyStringableTrait {
        DefaultStringConvertible::__toString insteadof LegacyStringableTrait;
        LegacyStringableTrait::__toString as legacyToString;
    }
}
```

  When the compiler sees an explicit `use` naming traits that back implemented interfaces, it **suppresses
  auto-injection for those traits** and uses the author's block verbatim. Zero new grammar; reuses PHP's
  `insteadof`/`as`; auto handles the common case, explicit handles the conflict. The tax: the author must name the
  backing *trait*, which the interface was trying to hide (a deliberate, documented leak).

- **(Deferred) Interface-qualified resolution.** Sugar so the author needn't know trait names, e.g.
  `use StringConvertible::__toString insteadof LegacyStringable;`. More ergonomic but needs new grammar plus an
  interface→trait→member map; add later only if the feature earns it.

- **(Rejected) Silent precedence by `implements` order.** Deterministic but surprising; too easy to miscompile
  intent. At most a loud warning, never a silent default.

### Decisions (defaults chosen)

1. **Injection is compile-time flattening** into the class; interfaces emit as plain PHP interfaces.
2. **Class-defined members and explicit `use` always suppress injection** for the overlapping trait/member.
3. **Dedup by distinct trait**; same-trait diamonds are no-ops.
4. **Unresolved trait-vs-trait collisions are a hard error**, resolved via an explicit `use { insteadof / as }` block
   in the class (interface-qualified sugar deferred).
5. **Attachment is transitive** through interface `extends`.

### Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Two different attached traits collide on a member | Hard error + explicit `use { insteadof/as }` escape hatch (Decision 4) |
| 2 | Author wants to override an attached default | Just define the member on the class; class wins (Decision 2) |
| 3 | Diamond: same trait attached via two interfaces | Dedup by distinct trait; single `use` emitted (Decision 3) |
| 4 | Attached trait declares `static` properties (shared per-class state) | Document the shared-state gotcha; same semantics as a hand-written `use` |
| 5 | Attached trait itself has abstract requirements | Normal trait behavior; implementer must satisfy them (reuse existing trait-requirement infra) |
| 6 | Leaky abstraction: conflict resolution forces naming the hidden trait | Accepted tax for the default; interface-qualified sugar deferred as the fix |
| 7 | Interaction with Story-31 lowering/relocation | Flattening runs before lowering, so attached traits are invisible to the linker — state it in the invariant |
| 8 | An interface author changing attached traits silently alters every implementer | Build report / lint: list interfaces whose attached-trait set changed; treat as an API-surface change |

### Golden Fixtures / Tests (Acceptance)

Per `CONVENTIONS.md` §5, add `.tyhp → .php` (+ expected-diagnostics) golden fixtures under
`tests/conformance/story31/interface-traits/` with a `manifest.json` the runner asserts.

- **Basic attach:** `interface I { use T; }`; `class C implements I {}` emits `class C implements I { use T; }`.
- **Transitive:** `I extends J`, both attach traits; an implementer of `I` gets both, deduped, in one `use`.
- **Diamond dedup:** `I` and `K` both attach `T`; a class implementing both emits a single `use T;`.
- **Class override wins:** class defines the member → no trait member injected; no conflict.
- **Conflict error:** two different attached traits, same member, class silent → diagnostic emitted.
- **Explicit resolution:** author's `use A, B { A::m insteadof B; B::m as m2; }` suppresses auto-injection and compiles.
- **Interface emission:** the interface itself emits as a valid PHP interface with **no** trait reference.
- **Idea 2 synergy:** `StringConvertible` attaches a default-conversion trait; implementing it (implementing only
  `__toInt()`, say) yields a working `__toString()` derived default, overridable by the class.

---

## Idea 4 — Emit Static-Analysis Docblocks (PHPStan/Psalm/PHPDoc)

> **Status:** Tier 4, future plans. Self-contained; may be lifted into its own story when scheduled.
> **Prerequisites:** Story 06 (built-in type surface), Story 08 (checker — the authoritative source of the precise
> types), Story 09/11 (emitter — where docblocks are attached today via `ApplyDocComment`). Interacts with
> Story 08.5 (symbol-name types → `class-string`), Story 25 (`internal` → `@internal`), and Idea 1 Phase 3
> (canonicalization of symbol strings — now extended to docblock type positions) and the debug/release profile.

### Summary

Teach the emitter to **generate the static-analysis docblock tags** (`@param`, `@return`, `@var`, `@template`,
`@extends`, `@property`, …) that carry the type information native PHP declarations **cannot** express — generics,
array shapes, `class-string`, callable argument types, refined scalars — so the emitted PHP type-checks under
PHPStan/Psalm and autocompletes in IDEs with the same precision as the Tyhp source.

Today the emitter only *passes through* author-written docblocks (`TyhpEmitter.Helpers.ApplyDocComment` →
`EmitItem.AttachDocComment`, gated on `Config.IncludeComments`) and already **reads** docblocks for its own emitter
directives (`@tyhpEmitterStart(onCall)`, `inheritFrom`, … — see `Tyhp/TyhpLang/Emitter/readme.md`). It generates
**no** analysis tags. This idea adds a generator that derives tags from the checker's typed signatures and merges
them with any author docblock (without disturbing the emitter directives).

> **Note (2026-07-29):** that pass-through only became true on 2026-07-29. Until then no docblock reached any
> emitted file, and a class-level one silently deleted the class body — see FOUND_BUGS.md items 19–21. Any
> estimate for this idea that assumed a working pass-through baseline predates that fix.

### Motivation

Tyhp's value proposition is static types richer than PHP's runtime type system. When those lower to native PHP, the
precision is lost at the language level:

| Tyhp declares | Native PHP can only say | Precision lost |
|---------------|--------------------------|----------------|
| `array<int, User>` | `array` | key + value types |
| `array<User>` / a list | `array` | value type; list-ness |
| `Collection<User>` | `Collection` | element type |
| `callable<int, string>` | `callable` | signature |
| `__ClassName` / `__ClassName<T>` (symbol type) | `string` | it's a `class-string` / `class-string<T>` |
| generic class `Box<T>` | `Box` | the type parameter entirely |

PHPStan/Psalm/PhpStorm recover all of it from docblocks. Emitting them means downstream consumers of the generated
PHP (vendored Tyhp packages, mixed Tyhp+PHP projects, third parties who never see `.tyhp`) get full type safety and
IDE support "for free," and the generated code passes a strict static-analysis baseline instead of collapsing to
`mixed`/`array`.

### What we can emit ("everything we possibly can")

Emit each tag **wherever the checker has the information and the native declaration is lossier than the docblock**:

- **Signatures:** `@param` (precise type per parameter), `@return`, `@param-out` (by-ref outputs),
  `@return $this`/`static`/`self`, and `@throws` *(only if/when the checker tracks thrown types — otherwise
  deferred)*.
- **Generics:** `@template` (with bounds `@template T of X`; variance `@template-covariant`/`-contravariant` where
  Tyhp models it; defaults `@template T = X`), and `@extends` / `@implements` / `@use` for generic
  parents / interfaces / traits.
- **Properties & magic:** `@var` on properties whose native type is lossy; `@property` / `@property-read` /
  `@property-write` for accessor/magic properties (ties into `HasPropertyAccessors`); `@method` for magic methods.
- **Class/member modifiers:** `@readonly` / `@immutable`, `@final`, `@internal` (from `internal` visibility,
  Story 25), `@deprecated` (from Tyhp's deprecation markers — `T_TYHPDEF_DEPRECATED` / `_OBSOLETE`), `@api`.
- **Constants:** `@var` (or `@type`) for constant value types.
- **Refined types the dialects understand:** array shapes `array{id: int, name: string}`, `list<T>`,
  `class-string<T>` (from Story 08.5 symbol-name types), `callable(T): U`, `key-of<…>` / `value-of<…>`, literal and
  `int<min,max>` / `non-empty-string` / `positive-int` refinements *where the checker can prove them*.

### Where it hooks

1. **`DocblockTypeSpellingHelper` (new)** — a companion to the existing `TypeSpellingHelper` that renders a checked
   type into a **PHPStan/Psalm type string** (the lossy-recovery counterpart to the native spelling). Single source
   of truth for the mapping table above.
2. **A docblock builder** — per emitted declaration (function/method/property/class/const), assemble the tag set from
   the checked signature via the helper, then **merge** with the author's `node.DocComment` and hand the result to a
   merge-aware `ApplyDocComment`.
3. **Emit gating** — reuse `Config.IncludeComments` plus new keys (below); no analysis tags in the minified release
   bundle (sourcemaps/Story 17 cover debugging there).

### Merge policy (with author docblocks and emitter directives)

The author docblock is parsed into: free-form summary/description, structured analysis tags, and **Tyhp emitter
directives**. Then:

- **Emitter directives are untouchable.** `@tyhpEmitterStart(onCall)`, `inheritFrom`, signed blocks, etc. pass
  through verbatim — they drive emission and must never be reordered, deduped, or dropped.
- **Author-written analysis tags win per key.** If the author already wrote `@param $x …` or `@return …`, keep it
  (the author may encode a refinement the checker can't infer); the generator only **fills gaps**.
- **Summary/description prose is preserved** and kept above the generated tags.
- **Lint on contradiction (optional):** if an author tag provably contradicts the checked type, emit a warning rather
  than silently overriding either way.
- **Deterministic ordering** of generated tags (params in signature order, then `@return`, etc.) for stable diffs.

### Configuration

Provisional `tyhp.json` keys (register in `CONVENTIONS.md` §4 when scheduled; `Tyhp/Config/Project.cs` authoritative):

| Key | Meaning |
|-----|---------|
| `emit.docblocks` | `"off"` \| `"lossy-only"` \| `"all"`. Default `"lossy-only"` (emit only where the docblock beats the native type). |
| `emit.docblockDialect` | `"phpstan"` \| `"psalm"` \| `"both"` \| `"phpdoc"`. Default `"both"` (shared syntax; prefixed `@phpstan-`/`@psalm-` variants only where they diverge). |
| `emit.docblockProfile` | Whether docblocks are emitted in the release/minified profile. Default `false` (debug-only; sourcemaps cover release). |

Gated overall by the existing `Config.IncludeComments`.

### Canonicalization (extends the Phase 3 invariant)

Generated docblock types frequently **name symbols** — `class-string<App\User>`, `Collection<App\Models\Order>`,
FQNs in `@param`/`@return`/`@extends`. Those symbol names must go through the **same** alias→FQN expansion and
lowering/relocation remap as [Phase 3](#phase-3-the-lowering--relocation-invariant): a `use … as X` alias must expand
to the real FQN, and a relocated/lowered symbol must be rewritten to its emitted home. **Extend the Phase 3
invariant so "typed symbol-strings" explicitly includes the symbol-name positions inside emitter-generated
docblocks.** (Author-written docblock text is treated as opaque unless it is a recognized analysis tag the generator
also manages.)

### Decisions (defaults chosen)

1. **Dialect → PHPStan/Psalm shared syntax** (`emit.docblockDialect="both"`); prefixed variants only where they diverge.
2. **Volume → `lossy-only`** by default (skip redundant `@param int $x` when native `int $x` already says it); `all` available.
3. **Merge → author tags and emitter directives win / are preserved; generator fills gaps only.**
4. **Profile → debug-only by default** (`emit.docblockProfile=false`); stripped from minified release bundles.
5. **Canonicalization → generated docblock symbol names obey the Phase 3 invariant** (alias-expanded, relocation-safe).
6. **`@throws`/purity/variance → emit only where the checker actually has the data**; otherwise deferred, not guessed.

### Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Clobbering Tyhp emitter directives in a docblock | Directives are parsed out and passed through verbatim; never deduped/reordered (Merge policy) |
| 2 | Generated type contradicts the native declaration → tool errors | `DocblockTypeSpellingHelper` derives from the same checked type as the native spelling; they cannot disagree by construction |
| 3 | Author tag conflicts with generated tag | Author wins per key; optional contradiction lint (Decision 3) |
| 4 | Docblock byte bloat in production | `lossy-only` default + debug-only profile; stripped from minified bundles (Decision 4) |
| 5 | Stale symbol names after lowering/relocation | Phase 3 invariant extended to generated docblock symbol positions (Decision 5) |
| 6 | Dialect drift (PHPStan vs Psalm syntax differences) | Default to shared syntax; emit prefixed variants only where required |
| 7 | Over-claiming refinements the checker can't prove (`positive-int`, etc.) | Emit refined types only when statically proven; else fall back to the broad type |
| 8 | Round-trip / re-emit stability | Deterministic tag ordering; generator is idempotent over its own output |

### Golden Fixtures / Tests (Acceptance)

Per `CONVENTIONS.md` §5, add `.tyhp → .php` (+ expected-output) golden fixtures under
`tests/conformance/story31/docblocks/` with a `manifest.json` the runner asserts.

- **Generics:** `array<int, User>` param emits `@param array<int, User> $x` alongside native `array $x`; a generic
  class emits `@template` + `@extends`/`@implements`.
- **class-string:** a Story-08.5 `__ClassName` param emits `@param class-string $c` (and `class-string<T>` when bound).
- **Callable:** `callable<int, string>` emits `@param callable(int): string`.
- **Lossy-only default:** a plain `int $x` emits **no** redundant `@param int $x`; `all` mode does.
- **Merge:** an author docblock with a summary + `@throws` + `@tyhpEmitterStart(onCall)` keeps all three, gains the
  generated `@param`/`@return`, and does not duplicate an author-written `@return`.
- **Directive safety:** `@tyhpEmitterStart(onCall)` / signed blocks emit byte-identical to today.
- **Profile:** debug profile includes docblocks; release/minified profile omits them.
- **Canonicalization:** a `use App\Models\User as U;` alias inside a generated `@param Collection<U>` emits the real
  FQN; a relocated/lowered symbol is rewritten (shares the Phase 3 fixture harness).
- **Static-analysis gate:** run PHPStan (max level) / Psalm over an emitted fixture package and assert zero type
  errors that the docblocks are responsible for resolving.

---

## Idea 5 — `operator default()` (Author-Defined Default Value for a Class Type)

> **Status:** Tier 4, future plans. Self-contained; may be lifted into its own story when scheduled.
> **Prerequisites:** Story 06 (compile-time constructs — `default()`), Story 08 (checker), Story 11 (operator-overload
> declaration + call-site rewriting, which supplies the `operator <name>()` surface this reuses).
> **Related:** `FOUND_BUGS.md` → *Property initialization state, `default(T)` and runtime generic tracking*, items 4,
> 5 and 10. Item 10's stage-1 fix is a **prerequisite** for this idea, and this idea is its stage 2.

### Summary

Let a class declare the value that `default(<that class>)` produces:

```tyhp
class Money {
    public function __construct(public readonly int $amount = 0) {}

    operator default(): self {
        return new self(0);
    }
}

$m = default(Money);   // → Money(0) instead of null
```

When a class does **not** declare `operator default()`, the default for that class type remains `null` — matching
today's behavior and keeping the change purely additive.

This reuses the existing operator-declaration surface (`operator convert(int $value): self` in
`runtime/packages/decimal/tyhp_src/Decimal.tyhp`), so no new declaration syntax is introduced — only a new operator
name.

### Motivation

`default()` currently folds to a per-type literal (`int` → `0`, `string` → `''`, …) and falls back to `null` for
everything else, including every object type. That fallback is unsound today: `default(MyClass)` is **inferred as
`MyClass`** but **emits `null`**, so the checker believes it holds an instance while the runtime holds null
(`FOUND_BUGS.md` item 10, reproduced with zero checker errors).

Item 10's stage 1 makes that honest by inferring the default of a class type as `null`, which turns the common
intent into a compile error:

```tyhp
function myFunc(?MyClass $instance = null): MyClass {
    return $instance ?? default(MyClass);   // error: the return type does not allow null
}
```

That diagnostic is correct, but it leaves the author with no way to *express* a default for their own type — they
must widen the return to `?MyClass` or hand-write a sentinel instance at every call site. `operator default()` closes
that gap: with it declared, the snippet above type-checks and works, because `default(MyClass)` is genuinely a
`MyClass`.

It also completes the generic story. `Type::defaultValue()` (item 4) needs an answer for object types at runtime;
without this idea the only possible answer is `null`, so `default(T)` with `T` bound to a class can never produce a
usable value even when the author has an obvious default in mind.

### Syntax

```tyhp
class Money {
    operator default(): self { … }
}
```

- **No parameters.** The operator receives nothing; it is conceptually static.
- **Return type must be `self`.** Returning anything else is a checker error — a "default value for `Money`" that is
  not a `Money` has no meaning.
- **At most one per class.** Unlike `operator convert`, which overloads on parameter/return type, `default` has a
  single form so there is nothing to overload on.
- **No `$this`.** The body cannot reference the instance being defaulted (there isn't one). The checker must reject
  `$this` inside it, the same way it does for other static contexts.

### Lowering model

Emit a static method and rewrite `default()` call sites to invoke it:

```php
final class Money {
    public static function __tyhpDefault(): static { return new self(0); }
}

// default(Money)  →
\App\Money::__tyhpDefault()
```

`Type::defaultValue()` (item 4) resolves object types through the same method:

```php
// inside \Tyhp\Type::defaultValue()
if ($this->isObjectType() && \method_exists($name = $this->getName(), '__tyhpDefault')) {
    return $name::__tyhpDefault();
}
return null;   // object type with no author-defined default
```

That single hook makes `default(T)` for a generic `T` bound to `Money` produce `Money(0)` without any additional
emitter work, because the generic path already routes through `Type::defaultValue()`.

The `__tyhpDefault` name follows the existing emitter conventions (`__tyhpGeneric` suffix, `tyhpGenericObject*` trait
methods, `__generic_*` parameters) and must be **reserved** by the checker so user code cannot declare a colliding
method — including case-insensitively, since PHP method names are case-insensitive.

### Decisions to make when scheduled

- **Inheritance.** A static `__tyhpDefault` is inherited, but a parent returning `self` would make `default(Child)`
  yield a *`Parent`* instance, which is wrong. Preferred resolution: treat the operator as **not inherited** for the
  purpose of resolving `default(<class>)` — a class either declares its own or its default is `null`. This is the most
  predictable rule and avoids a silent wrong-type result. The alternative (inherit, but require the emitted method to
  use `new static(...)`) only works when the parent's constructor signature suits every subclass.
- **Abstract classes and interfaces.** Both are uninstantiable, so `operator default()` on them cannot return `self`.
  Preferred: **disallow** on interfaces and abstract classes.
- **Enums.** An enum default could name a case (`operator default(): self { return self::None; }`). Plausible and
  cheap, but decide explicitly rather than letting it fall out of the class rule.
- **Constant-expression contexts.** With the operator present, `default(MyClass)` is a *method call* and therefore not
  a constant expression, so it cannot be a property initializer or parameter default — while `default(MyClass)`
  without the operator folds to `null` and is constant. The constant-expression predicate must key off whether the
  class declares the operator (this constraint is recorded in `FOUND_BUGS.md` item 5).
- **Memoization.** Each `default(MyClass)` currently constructs a fresh instance. Sharing one instance would be
  cheaper and is desirable for immutable value types, but silently sharing a *mutable* default across unrelated call
  sites is a serious bug. Preferred: **do not memoize**; if a shared instance is wanted the author can return a
  singleton from their own operator body.

### Risks & Edge Cases

- **Recursion.** `operator default()` whose body evaluates `default(SameClass)` (directly or through a constructor
  default) recurses infinitely. Needs a checker cycle detection pass over operator bodies, or a runtime depth guard.
- **Construction cost in hot paths.** `default(T)` inside a loop now allocates. Worth a note in the guide; the
  optimizer stories (23/24) could hoist provably-immutable defaults.
- **`method_exists` cost.** The runtime hook does a `method_exists` per call. Cache per class name inside `Type`, or
  have the emitter resolve statically whenever the class is known at compile time (the common case) and fall back to
  the runtime hook only for generic bindings.
- **Interop.** Hand-written PHP that declares `__tyhpDefault` would be picked up by the runtime hook. Acceptable — it
  is the same opt-in surface — but should be documented rather than accidental.

### Golden Fixtures / Tests (Acceptance)

- **No operator:** `default(MyClass)` infers as `null`; `function f(): MyClass { return default(MyClass); }` reports
  the "return type does not allow null" error (item 10 stage 1).
- **With operator:** the same function compiles and returns a real instance; emitted PHP calls
  `MyClass::__tyhpDefault()`.
- **The motivating shape:** `function myFunc(?MyClass $i = null): MyClass { return $i ?? default(MyClass); }` compiles
  and, executed under PHP, returns the operator's instance when `$i` is null.
- **Generic binding:** `default(T)` with `T` bound to a class declaring the operator produces the instance at runtime
  via `Type::defaultValue()`; bound to a class without it produces `null`.
- **Rejections:** non-`self` return type; parameters on the operator; `$this` in the body; two declarations in one
  class; declaration on an interface or abstract class; user code declaring `__tyhpDefault` (reserved-name error).
- **Constant contexts:** `public MyClass $m = default(MyClass);` is rejected when the operator is declared, and
  behaves per item 5 when it is not.
- **Inheritance:** `default(Child)` where only `Parent` declares the operator yields `null` (per the not-inherited
  decision) rather than a `Parent` instance.

---

## Idea 6 — No Uninitialized Storage (Ban `unset`, Require Property Init)

> **Status:** Tier 4, future plans. Self-contained; may be lifted into its own story when scheduled.
> **Prerequisites:** Story 08 (checker) — especially Prop-init **#6** (local definite assignment / TYHP4014),
> **#7** (property initialization / TYHP4157), and **#8** (`unset` tracking / `AllowUnset` / TYHP4158). This idea
> revises the **#8** design decision (declaration-gated unset via `#[\Tyhp\AllowUnset]`) rather than adding a
> parallel feature.
> **Related:** `RESOLVED_BUGS.md` → Prop-init #6 / #7 / #8 (2026-08-03); brainstorm 2026-08-03 (reject PHP-side
> hook-wrapping to seal `unset`; tighten Tyhp rules instead). Runtime: `runtime/packages/core/tyhp_src/AllowUnset.tyhp`
> becomes obsolete under this idea and should be removed when scheduled.

### Summary

Tyhp erases PHP's **uninitialized storage** concept for object fields and forbids returning a local to the
undefined state via `unset`. Absence is spelled with types (`?T = null`), never with `IS_UNDEF`.

Concrete rules:

1. **Ban `unset` on properties** (instance and static). No `#[\Tyhp\AllowUnset]` escape hatch. Prefer
   `?T $prop = null` (or a domain empty value) when "no value" is needed.
2. **Require an initializer on every typed static property.**
3. **Require every typed instance storage property to be initialized** by (a) a declaration initializer, (b) a
   promoted constructor parameter, or (c) a direct `$this->prop = …` assignment in the constructor body (same three
   sources as Prop-init #7). Helper-method assignment from the constructor still does **not** count.
4. **Locals may be declared without an initializer** (`string $s;`), but definite-assignment analysis must prove a
   write on every path before a read (existing TYHP4014).
5. **Ban `unset` on local variables** as well. To drop a large value, assign `null` on a nullable local
   (`?string $s = …; $s = null;`) rather than `unset($s)`.

Emitted PHP may still be unset by a plain-PHP caller; that remains an interop trust boundary (no hook-wrapping tax).
Tyhp source must not be able to create the uninitialized state.

### Motivation

Prop-init #7/#8 closed the Tyhp-source holes that let code crash with PHP's
`must not be accessed before initialization`. `#8` deliberately kept an opt-in (`AllowUnset`) for the rare case
where uninitialized ≠ null. That escape hatch:

- reintroduces cross-method "possibly uninitialized" uncertainty for every `AllowUnset` property,
- forces authors and the checker to reason about a second absence state that Tyhp's non-nullable-by-default model
  already rejects elsewhere, and
- is almost always better spelled `?T = null`.

Banning `unset` entirely (properties + locals) and requiring static initializers makes the language rule match the
mental model: **storage is always initialized after construction / before use; nullability is how you express
absence.**

### Current baseline (what already exists)

| Surface | Today (Story 08 Prop-init) | This idea |
|---|---|---|
| Instance prop read before init | TYHP4157 | Keep; treat as hard guarantee after construction |
| Instance `unset($this->prop)` without attribute | TYHP4158 | Always error (delete `AllowUnset`) |
| Instance `unset` with `#[AllowUnset]` | Allowed; clears init → 4157 on later reads | Remove attribute + path |
| Static typed property without initializer | Not specially required | **New:** require initializer |
| Static `unset(Foo::$x)` | Not specially rejected (beyond general gaps) | **New:** always error |
| Local declare-without-init | Allowed | Keep |
| Local read before assign | TYHP4014 | Keep |
| Local `unset($x)` | Clears definite assignment → later reads 4014 | **New:** always error (no clear-and-reuse) |

### Inheritance / replaced constructors (verify when implementing)

Prop-init #7 today tracks **only properties declared on the class under analysis**
(`PropertyInitializationAnalysis.EnumerateTrackedProperties` walks `objectSymbol.Members`, not the `extends`
chain). `MayBeUninitializedAfterConstruction` is recorded on each property symbol when **that declaring class's**
constructor is analyzed.

That leaves a known soundness gap to **close or explicitly decide** under this idea:

```tyhp
class Parent {
    public string $name;           // no initializer
    public function __construct(string $name): void {
        $this->name = $name;       // Parent analysis marks $name initialized after construction
    }
}

class Child extends Parent {
    public function __construct(): void {
        // no parent::__construct(), no $this->name = …
    }

    public function label(): string {
        return $this->name;        // PHP: uninitialized crash; checker may still trust Parent's flag
    }
}
```

**Required behavior when this idea is scheduled:**

- After a class's constructor completes (or when it has no constructor), every typed instance storage property
  visible on `$this` — **including inherited ones without declaration-level guarantees** — must be definitely
  initialized, or the constructor (or the missing-constructor case) reports an error at the declaration / ctor.
- A subclass that **replaces** `__construct` does **not** inherit the parent's post-construction guarantees unless
  it either:
  - calls `parent::__construct(…)` in a way the checker can credit (at minimum: an unconditional call that
    dominates the end of the child constructor — exact dominance / argument rules to specify when implementing), or
  - itself assigns every inherited storage property that is not declaration-guaranteed (initializer / promotion on
    the ancestor), or
  - those ancestor properties already have declaration-level guarantees (initializer or promotion).
- Re-check the no-constructor case on a subclass: inherited props that only became initialized via a parent ctor
  body are **not** safe if the subclass adds no ctor and PHP therefore never runs the parent ctor either
  (same gap as replacing the ctor without `parent::__construct()`).

Do **not** silently trust "parent analyzed clean" when the parent's constructor is not actually invoked on the
construction path under analysis.

### Locals vs properties

- **Properties / statics:** no uninitialized state, ever, after the relevant init point (object construction / class
  load for statics with required initializers).
- **Locals:** temporary unassigned is fine (`string $s;` then assign in both `if`/`else` arms). That is
  definite-assignment, not uninitialized storage. What changes is only **`unset($local)`** — forbidden; use
  `?T` + `= null` if the goal is to release a value.

### Implementation sketch (when scheduled)

- Remove / obsolete `\Tyhp\AllowUnset` (runtime class, binder `AllowsUnset`, AttributeRule special case, TYHP4158
  messaging that suggests the attribute). Replace with a single "cannot `unset` property" diagnostic (reuse 4158 or
  retarget its message).
- `UnsetTrackingRule`: reject `unset` on typed instance props, static props, and locals; stop clearing property-init /
  local assignment state for those targets (rejection is enough).
- Static properties: declaration check requiring a default value; extend init / unset rules to
  `SymbolType` static instance properties (today's Prop-init walk skips statics).
- Property-init analysis: extend tracked set to inherited instance storage properties; credit
  `parent::__construct` per the inheritance rules above; add regression tests for replace-ctor / no-ctor subclass
  cases.
- Docs: diagnostics reference, AIDevGuide mental model / strict rules, delete AllowUnset from the runtime package
  surface.
- Keep local TYHP4014 definite-assignment as-is aside from banning `unset`.

### Decisions locked by this idea

- No PHP emission of identity property hooks solely to seal `unset` (interop trust boundary; not worth the
  get/ref/array-append tax).
- No require-initializer-at-declaration for locals.
- `?T = null` is the supported spelling for optional / clearable values (properties and locals).
- Helper calls from `__construct` still do not count as initializing `$this->prop` (unchanged from #7).

### Risks & Edge Cases

- **Breaking change** relative to the 2026-08-03 `#8` ship: any code using `#[AllowUnset]` or `unset` on
  properties/locals must migrate to nullable assignment.
- **`unset($arr[$i])` / `unset($obj->arr[$key])`** — array-offset unset is not "uninitialize a typed property"; keep
  allowing offset unset unless a separate decision says otherwise. This idea targets **variable and property
  storage slots**.
- **Readonly / hooked properties** — already restricted; ensure diagnostics stay coherent (hooked props cannot be
  unset in PHP anyway).
- **Promoted parameters** — remain a declaration guarantee; no `AllowUnset` on promotion.
- **Traits** — property introduced by a trait must satisfy the same init rules in each using class's constructor
  analysis (including inheritance rules when the using class extends another type).

### Golden Fixtures / Tests (Acceptance)

- `unset($this->prop)` on a typed instance property → error (no attribute makes it legal).
- `unset(Foo::$staticProp)` → error; `public static string $x;` without default → error; with `= ''` → ok.
- Instance prop without initializer / promotion / ctor assignment → error (existing 4157-style cases plus
  ctor-exit "not definitely initialized" if not already reported at read sites).
- Subclass replaces `__construct` without `parent::__construct` and without assigning inherited
  non-declaration-guaranteed props → error; with unconditional `parent::__construct(…)` (once credit rules land) →
  ok; inherited prop with initializer → ok without child assignment.
- `string $s; echo $s;` → TYHP4014; `string $s; $s = 'a'; echo $s;` → ok.
- `unset($s)` on a local → error; `?string $s = 'a'; $s = null;` → ok.
- Runtime packages / conformance build with **zero** remaining `AllowUnset` / property-`unset` usages.

---

## Idea 7 — Conditional Return Types (v1: Overload Desugar + `extends ? :` + `match`)

> **Status:** Tier 4, future plans. Self-contained language/checker feature; may be lifted into its own story when
> scheduled. **Placement note:** Stories 16–30 were reviewed for a home. None fit — Story 28 is only generic *parameter
> defaults*; Story 21 *consumes* specialized signatures in tyhpdefs but does not implement the type IR; Story 11 already
> shipped overload signatures as a distinct compile-time mechanism. Parked here as the next type-system enhancement.
> **Prerequisites:** Story 08 (checker — literal types, assignability, generics), Story 11 (function/method overload
> signatures + emitter erasure to a single PHP implementation). Grammar/binder support for type-position `match` and
> conditional types as needed. Flag-presence also needs const values when known, `|`/`&`/`~` folding on known ints
> and named flag atoms, and literal/`contains` specificity in overload scoring (documented today, not yet scored).
> **Related:** `docs/content/tyhp_1200_functionOverloads.md`; `docs/content/tyhpdef_overloadedFunctions.md`;
> `CONVENTIONS.md` §6 (overlay placement); brainstorm 2026-08-04 (overloads → conditional IR; ternary + match;
> non-distributive v1; explicit `...T` deferred to Idea 8); brainstorm 2026-08-13 (bitmask `contains F`, not
> exact-int overloads).
> **Out of scope here:** `infer`, distributive conditionals, recursion, tuple/variadic patterns, template-literal
> types, body-vs-every-arm verification, mapped/`keyof` algebra (see Idea 8 / v3+); PHPStan-style `int-mask-of<F>`
> **subset** matching; flag-gated **parameter existence** (PHP almost never adds/removes params because of a flag —
> refine in-place types, including by-ref out-params); an opt-in to treat a runtime `int` variable as “has flag F.”

### Summary

V1 unifies four author surfaces into **one checker IR**: generics + **conditional return types**, including bitmask
**flag-presence**.

| Surface | Role |
|---------|------|
| Overload signatures (keep) | Readable discrete / one-flag contracts; **desugar** to generic + conditional/`match` return when the overload set is a clean discriminant partition |
| `T extends X ? A : B` | Dense binary / nested branching (parentheses allowed; `?:` right-associative) |
| `match (T) { … }` | Multi-arm branching; nestable; freely mixed with `?:`; arms include literals **and** `contains F` |
| `contains F` | Flag-presence predicate on a compile-time int / flag-set (`F ⊆` known bits). Param constraint **and** match arm. Subtype of `int` (or of a declared flag-family type) |

Classic overload resolution remains the **fallback** when a set cannot desugar cleanly (arity mismatches, overlapping
non-partitionable params, etc.) — never invent a mega-conditional that only “almost” matches.

**Non-distributive:** a union scrutinee is treated as one type, not mapped per constituent (`Box<string|int>` does
**not** become `array<string>|array<int>` in v1). Distribution is Idea 8 via explicit `...T`.

Emit stays as today in spirit: one PHP function/method with the **implementation’s** (union) signature; overload /
conditional forms are compile-time only. Desugaring may **add** type parameters to an already-generic declaration for
the Tyhp IR; it does not invent runtime PHP generics.

### Motivation

Overloads work for cases generics cannot express alone (e.g. `true` → `array`, `false` → `string`), but they feel
bolted on: boilerplate, drift vs the implementation, poor composition when wrapping calls. Identity cases remain plain
generics (`function f<T extends array|string>(T $x): T`). Conditional/`match` returns give a single composable
mechanism; overloads become sugar for the common discrete-literal API.

Discrete literals are **not** enough for PHP bitmasks. `\json_encode($v, \JSON_PRETTY_PRINT | \JSON_THROW_ON_ERROR)`
is not an exact value of `JSON_THROW_ON_ERROR`, two `int $flags` overloads are the same signature, and today’s `|`
widens to `int`. V1 therefore adds **`contains F`**: the interesting question is whether a flag bit is **known to be
set**, not whether the argument equals one integer. Independent flags (`THROW` × `OBJECT_AS_ARRAY` ×
`PREG_OFFSET_CAPTURE`) explode overload matrices; `match` is the authoring form there. The same switch should also
gate `@throws` (e.g. `\JsonException` only when `JSON_THROW_ON_ERROR` is proven) so callers are not forced to keep a
`string|false` guard after opting into throw-on-error.

### Syntax sketch

```tyhp
// Ternary
function generateReport<T extends bool>(T $asArray = false): T extends true ? array : string { … }

// Match (same meaning)
function generateReport<T extends bool>(T $asArray = false): match (T) {
    true  => array,
    false => string,
} { … }

// Overload sugar → same IR when desugarable
function generateReport(true  $asArray): array;
function generateReport(false $asArray): string;
function generateReport(bool  $asArray = false): string|array { … }
```

Mixing (preferred style: match for multi-way, ternary for binary refine):

```tyhp
function parse<F extends string, A extends bool>(
    string $input,
    F $format,
    A $assoc = true,
): match (F) {
    'json' => A extends true ? array : object,
    'csv'  => array<string>,
    'raw'  => string,
    default => mixed,
} { … }
```

Flag-presence (bitmask). `contains F` means **F is on** (`F ⊆` known bits), **not** “bits ⊆ F”:

```tyhp
function json_encode(
    mixed $value,
    int $flags = 0,
    int $depth = 512,
): match ($flags) {
    contains JSON_THROW_ON_ERROR => string,
    default => string|false,
};

// One-flag overload sugar → same IR when desugarable
function json_encode(mixed $value, contains JSON_THROW_ON_ERROR $flags): string;
function json_encode(mixed $value, int $flags = 0, int $depth = 512): string|false;
```

Later discriminants and extra param **types** (not existence) use whole-call matching — PHP often puts `$flags`
after the value they refine:

```tyhp
function preg_match(
    string $pattern,
    string $subject,
    match ($flags) {
        contains PREG_OFFSET_CAPTURE => array<array{string, int}> &$matches,
        default => array<string> &$matches,
    } = null,
    int $flags = 0,
): 1|0|false;
```

Nested match / nested ternary / parentheses all allowed. Arms in v1: literals, unions of literals, `contains F`,
and `extends Type` / plain type patterns — **no `infer`**.

Optional (if cheap): named aliases for conditional types so signatures stay short
(`type ParseResult<F, A> = match (F) { … };`).

### Flag representation (`contains F`)

`contains F` is evaluated against the **known flag-set** of the argument, built as follows:

1. **Numeric bits when the const value is known.** `const JSON_THROW_ON_ERROR = 4194304` (or equivalent) lets the
   checker fold `|` / `&` / `~` on literal ints and known const ints. `contains F` is then `(known & F) === F`
   (treat a multi-bit `F` as “all of F’s bits are on”).
2. **Named flag atom when the numeric value is unknown.** `const int JSON_THROW_ON_ERROR;` (no initializer) is still
   a distinct atom. `|` of atoms builds a known set; `contains F` is set membership. An opaque numeric literal (or
   mixed atom + uninterpretable int) does **not** prove `contains F` — those bits are unknown.

A runtime `int` / unfolded expression has **no** known bits. Omitted flag args use the default’s type (`0` ⇒ empty
set).

`contains F` is a **subtype of `int`** (or of a closed flag-family type, if declared), so it can appear as an
overload parameter type without colliding with another `int $flags` signature.

### Desugar rules (overloads → IR)

- Promote literal-constrained discriminant params to type parameters (`true|false` → `T extends bool`, `'json'|…` →
  `F extends string`, etc.).
- Promote `contains F $flags` to a flag-set type parameter; the corresponding `match` arm is `contains F`.
- Build a `match` / nested conditional covering each overload arm; implementation return type is the runtime/erasure
  upper bound. Proven `contains JSON_THROW_ON_ERROR` should also select the `@throws \JsonException` contract for
  that arm (wide arm keeps `string|false` and no throw-on-error promise).
- Already-generic functions: **add** discriminant type params; keep existing ones.
- If the set is not a clean partition → keep classic overload matching; do not force a conditional rewrite.
- Independent flags / later discriminants: **prefer `match`**, not an overload matrix. Classic overload fallback
  still applies when desugar would be dishonest.

### Overload scoring when several `contains` arms match

Prefer conditionals for multi-flag APIs. If several `contains` overloads **all** match a call:

- Score by **number of proven bits** (or proven atoms, when numeric values are unknown) required by the constraint —
  more specific wins (`contains A|B` beats `contains A` when both A and B are proven).
- **Tie → ambiguity error.** Do not pick declaration order.

Literal specificity (docs already describe exact / static-value / compatible / generic) must actually participate in
scoring; `contains F` is the bitmask analogue of “narrower matcher wins.”

### Semantics / style guidance

- When the discriminant is a wide type (plain `bool` / plain `int`, not a literal or known flag-set), the
  conditional resolves to the **wide** arm / union of arms (same as today’s overload fallback to the impl
  signature). **Unknown `int` is always wide** — never pick `contains F` unless the bit/atom is proven. No opt-in
  to “trust this variable has F” in v1.
- “Flag not present” is only safe when the bit is proven **off** (literal `0`, or a folded mask with no that bit).
  Otherwise it is unknown → wide.
- `default` required when the scrutinee is not a closed exhaustive set.
- Docs guidance: identity → plain generics; 2 arms → ternary or overloads; 3+ arms → match; one bitmask flag →
  `contains F` overload or a two-arm match; several independent flags → match, not overload explosion; different
  return *shape* under author control → prefer separate named functions when practical.

### Stub placement (tyhpdefs)

Flag-dependent / `contains F` signatures belong in **overlays**, not regenerated baselines — see `CONVENTIONS.md` §6.
Story 21 consumes the specialized stubs; this idea implements the checker IR. Generation tooling may emit a wide
baseline (`json_encode(…): string|false`); the overlay supplies the `contains` / `match` / overload refinement.

### Touches (anticipated)

Grammar (type-position conditional + `match` + `contains F` in type and match-arm position), AST/checked-type IR
(flag-set / named-atom types), binder (overload desugar / type-param injection; const initializers), checker
(evaluate conditionals at call sites; exhaustiveness; `|`/`&`/`~` fold; `contains` scoring; whole-call matching
for later discriminants), emitter (unchanged PHP shape aside from any metadata), overlays under
`runtime/php-extensions/overlays/` (cite `CONVENTIONS.md` §3 / §6 rather than duplicating layout), docs
(`tyhp_1200_functionOverloads.md` + `tyhpdef_overloadedFunctions.md` + new conditional-types page), conformance
fixtures.

### Decisions (defaults chosen)

1. **One IR** — overloads desugar to conditional/`match` returns when clean; else classic overload resolution.
2. **Ship both `?:` and `match`**; they mix and nest.
3. **Non-distributive** evaluation in v1.
4. **No `infer` / recursion / tuple patterns / template-literal types / body⇔arms checking** in v1 (Idea 8).
5. **PHP emit** remains a single implementation with the impl union signature.
6. **Numeric bits when the const value is known; named flag atom when it is not.** `|` folds on both; opaque
   numerics mixed with unknown-value atoms do not prove `contains F`.
7. **Matcher spelling is `contains F`** — presence (`F ⊆` known bits). Not `int-mask-of<F>` subset-of-F.
8. **Unknown `int` is always wide.** No v1 opt-in to treat a variable as having a flag.
9. **Several matching `contains` overloads:** score by number of proven bits/atoms; **tie is an ambiguity error.**
   Prefer `match` over an overload matrix for independent flags.
10. **Flag-dependent stubs live in overlays** (`CONVENTIONS.md` §6), not regenerated baselines.

### Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Desugar ≠ historical overload specificity | Require provable equivalence; fallback to classic overloads when unsure |
| 2 | Default args + literal discriminants | Spec which arm applies when the arg is omitted (use default’s literal type) |
| 3 | Grammar clash: value `match` vs type `match` | Type position only; same keyword, different production |
| 4 | Author complexity | Docs push match for ≥3 arms; discourage deep nested ternaries |
| 5 | Tyhpdef / Story 21 consumers | v1 unblocks richer stubs; generation tooling can adopt later; overlays hold the refinements |
| 6 | `PRETTY \| THROW` widens to `int` today | Fold `|`/`&`/`~` on known ints and named atoms or `contains` never fires on real call sites |
| 7 | `const int F` with no value | Named atom; do not treat an unrelated numeric literal as proving `contains F` |
| 8 | Later discriminant (`preg_match` `$flags` after `$matches`) | Whole-call matching (selection already sees the full arg list) |
| 9 | PHPStan `int-mask-of<F>` subset vs our presence | Docs: `contains F` is `F ⊆` bits; `PRETTY \| THROW` **does** match `contains THROW` |
| 10 | Two `contains` overloads, equal proven-bit count | Ambiguity error; do not use declaration order |
| 11 | Narrow return without matching `@throws` | Same arm gates both; otherwise callers keep `is_string` guards |

### Golden Fixtures / Tests (Acceptance)

- `generateReport(true)` / `(false)` / `()` (default) resolve to `array` / `string` / narrowed-by-default type.
- Overload-only spelling and conditional-only spelling for the same API produce identical call-site types.
- Nested `match` + inner `?:` as in `parse` sketch.
- Non-desugarable overload set still resolves via classic overloads (no bogus conditional).
- `Box<string|int>` with a non-distributive conditional does **not** split into per-member results.
- Emitted PHP has one function and no residual overload signatures.
- `json_encode($v)` and `json_encode($v, 0)` → `string|false`; `json_encode($v, JSON_THROW_ON_ERROR)` → `string`;
  `json_encode($v, JSON_PRETTY_PRINT | JSON_THROW_ON_ERROR)` → `string`; `json_encode($v, JSON_PRETTY_PRINT)` →
  `string|false`; `json_encode($v, $runtimeInt)` → `string|false` (always wide).
- Named-atom const (no numeric initializer) combined with `|` still proves `contains` of that atom; an opaque
  numeric mixed with that atom does not.
- Two `contains` overloads that both match: more proven bits wins; equal proven-bit count is an ambiguity error.
- Overlay-refined `json_encode` / `json_decode` / `preg_match` stubs type-check against the wide baseline + overlay
  pattern (`CONVENTIONS.md` §6); regeneration of the baseline must not clobber them.

---

## Idea 8 — Advanced Conditional Types (v2)

> **Status:** Tier 4, future plans. Depends on Idea 7 (v1 conditional/`match` IR) landing first. May ship as one story
> or staged sub-phases when scheduled.
> **Prerequisites:** Idea 7 (v1); Story 08 (checker); solid error-depth / cycle handling before recursive forms.
> **Related:** brainstorm 2026-08-04 — v2 list; distributive spelling = explicit `...T` (not `T[]`, which means array).
> **Still not in v2:** full mapped/`keyof` / remapping type algebra (v3+ / undecided); forcing non-desugarable overload
> sets into conditionals (never — keep Idea 7 fallback).

### Summary

V2 grows from “branch on a type” to limited **type-level programming** on top of Idea 7’s IR:

1. **`infer`** — bind pieces of a matched shape (`T extends Promise<infer U> ? U : T`; match arms with
   `array<infer E>`; later `infer R extends string`).
2. **Distributive conditionals — explicit `...T`** (chosen over `T[]`):
   ```tyhp
   type Box<T> = ...T extends mixed ? array<T> : never;
   // Box<string|int> => array<string>|array<int>

   type Box2<T> = match (...T) {
       string => array<string>,
       int    => array<int>,
       default => never,
   };
   ```
   Without `...` → v1 non-distributive behavior preserved. With `...` → map the conditional/match over each union
   constituent and union the results. Prefer spread because PHP/`tyhp` already read `T[]` as “array of T.”
3. **Recursive conditional types** — e.g. `DeepAwaited<T>`; needs depth limits, cycle detection, usable diagnostics.
4. **Tuple / variadic inference** — `[infer H, ...infer Rest]`, etc., for typed rest/spread-style APIs.
5. **Template-literal type inference** — already planned elsewhere; land here as part of the conditional/`infer`
   pattern-matching story (route params, prefixes, etc.).
6. **Body vs every conditional arm** — eventually required correctness mode: implementation must be assignable under
   each arm’s refined params / promised return (“signatures don’t lie”). May be late v2 or a strict flag once the IR
   is stable — **not** a blocker for shipping branching itself.

### Motivation

V1 covers discrete flag/format APIs, bitmask `contains F`, and overload sugar. Library/stdlib/`tyhpdef` authors still need extraction (`ReturnOf`,
`Awaited`), honest union distribution (without TS’s invisible naked-`T` rule), and richer pattern forms. Explicit
`...T` is the PHP-facing improvement over TypeScript’s surprising default distribution.

### Decisions (defaults chosen)

1. **Distribution is opt-in** via `...T` / `match (...T)` — never implicit.
2. **Reject `T[]` as the distribute spelling** — conflicts with array types.
3. **`infer` is the gateway** for extraction; constrained infer and recursion follow with hard depth limits.
4. **Mapped/`keyof` algebra stays out** of v2.
5. **Body⇔arms checking** is planned and needed eventually, but ordered after the solver features above.

### Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | TS-style opaque errors / “excessively deep” | Depth caps, cycle detection, invest in diagnostics early |
| 2 | Distributive + `infer` interactions | Spec evaluation order; conformance corpus for naked vs `...` forms |
| 3 | Template-literal inference scope creep | Stick to planned use cases; don’t grow a full string-DSL type language |
| 4 | Body⇔arms false positives on intentional wide impls | Strict mode / incremental rollout; allow impl union as upper bound |

### Golden Fixtures / Tests (Acceptance)

- `Awaited<Promise<int>>` → `int`; non-promise → identity (via `infer`).
- `Box<string|int>` with `...T` → `array<string>|array<int>`; same without `...` → non-split (v1).
- Recursive unwrap bottoms out; cyclic types diagnose cleanly.
- Tuple `Head` / `Tail` patterns; at least one template-literal `infer` fixture from the planned design.
- (When enabled) a lying overload/conditional arm vs body is rejected under the body⇔arms mode.

---

## Idea 9 — Compiler Plugins v1 (Tyhp/PHP Host, `p"ns:…"`, Backtick Operators)

> **Status:** Tier 4, future plans. **v1** of the plugin surface; **v2** is [Idea 10](#idea-10--compiler-plugins-v2).
> May be lifted into its own story when scheduled.
> **Prerequisites:** Story 06 (grammar/lexer + GrammarAddon), Story 08 (checker), Story 09/11 (emitter), Story 10/13
> (`tyhp.json` / CLI), Story 20 (packaging / Composer + Tyhp library layout). Story 03 operator-overload emit is a
> useful pattern for lowering backtick ops — not a hard dependency for DSL islands.
> **Related:** brainstorm 2026-08-05 — plugin surface without arbitrary ANTLR extension; PHP/Tyhp out-of-process host.
> **Explicitly out of scope for v1** (see Idea 10 or deferred notes): post-binder/typed transform; statement-form
> islands; TextMate/IDE injection; diagnostic fix-its; sandbox policy; shared plugin cache; optimizer hooks;
> multi-round `.tyhp` re-entry into the compile pipeline (considered, **not rejected**, not planned until demand);
> third-party ANTLR mutation; PHP VM embed in C#; `#"` prefixes; unnamespaced first-wins dispatch; per-file
> `use plugin operator …`; custom unary operators (use ordinary calls).

### Summary

Add a **small, intentional compiler-plugin surface** so ecosystem authors can extend Tyhp without forking the
compiler or regenerating ANTLR grammars:

1. **Plugins authored in Tyhp and/or PHP**, executed by the **host PHP process** against a stable IR/API (C# owns
   parse + orchestration; plugins never require a Dotnet SDK).
2. **`p'ns:…'` / `p"ns:…"` / `p<<<ns` / `p<<<'ns'`** — namespaced DSL **islands** as base expressions (v1: expr-only).
   Quotes match PHP; heredoc interpolates, nowdoc does not; heredoc/nowdoc **label = namespace**. One plugin may
   register **multiple** namespaces; project aliases notify the plugin of source↔canonical bindings.
3. **`` `op` ``** — binary, left-associative **custom operators** (opaque compile-time ids). `` `$a `+` $b` `` ≠
   `$a + $b`.
4. **Hooks (not the visitor):** `AstTransform` (full AST, **pre-binder**, with activation preconditions), `Check`,
   `Emit`. Syntax islands optional.
5. **v1 packaging essentials:** plugins are **Composer packages** with root **`plugin.tyhp.json`** (the manifest);
   discovery from **project + global** Composer installs; compile-time code on an **autoload-excluded** path;
   runtime helpers in-package or via Composer `require`; explicit **process order**; **`plugins.options` JSON
   Schema**; **plugin test harness**; Composer **`"tyhp": ">=…"`** for host compatibility.
6. **Config hybrid:** auto-register namespaces/operators on enable; app config for aliases/disables/conflicts;
   **attributes are not exclusively owned** — conflicts resolved by order + plugin options.

This is *not* “PHP C-extensions for arbitrary syntax.” It is closer to Roslyn analyzers/source generators + a fixed
island/operator spelling — including plugins that use **no** new syntax at all.

### Motivation

- Full ANTLR extensibility from external packages is impractical (compile-time monolithic grammar, generated
  `TyhpParser.*Context` coupling, fixed GrammarAddon slots, manual `compile_grammar.sh`).
- Checker/emitter-only plugins already cover most ecosystem value (arch rules, framework awareness, security,
  attribute-driven codegen) — but authors need a **blessed syntax hook** for embedded languages and custom ops
  without inventing new keywords per feature in core.
- Tyhp’s audience writes PHP/Tyhp, not C#. An out-of-process PHP host matches packaging and skills.

### Non-goals (keep the surface small)

| Non-goal | Why |
|----------|-----|
| Arbitrary new tokens/productions from plugins | ANTLR is closed; islands + backticks are the escape hatches |
| Plugin strings in type-name / member-name positions (v1) | Binder/tooling cost; defer until a real need |
| `p"…"` as everyday infix operators | Too verbose; backticks own that job |
| In-process PHP embed in C# | Portability/sandbox nightmare vs `php` worker |
| Parallel type-reporting API (“plugin returns `int`”) as primary | Lowered AST + normal checker is the source of truth |
| Hooks into the ANTLR visitor / parse-tree walk | Visitor stays core-owned; plugins see **AST**, not `*Context` |
| Mutating the tree during checker as the primary rewrite API | Structural rewrites belong in AstTransform (v1) / post-binder transform (Idea 10) |
| Post-binder typed transform, statement islands, IDE TextMate, fix-its, sandbox, shared cache, optimizer hooks | **Idea 10 (v2)** |
| Multi-round codegen (emit new `.tyhp` that re-enters compile) | Considered; **not rejected**; **not planned** until clear demand |
| Exclusive attribute ownership by a plugin | Conflicts use **load order + plugin options** (rename/disable), not exclusive claim |
| Custom unary operators | Use ordinary function/method calls around the operand |

### Architecture & plugin hook surfaces

Plugins **do not** plug into the visitor. The visitor always produces a core AST (including transient
`PluginStringAst` / `PluginInfixAst` when those forms appear). The host then invokes plugins at well-defined
pipeline stages:

```text
parse (core ANTLR — only new forms: p'…'/p"…" + `op`)
  → visitor (core only — no plugin visitor hooks;
             may create transient PluginStringAst / PluginInfixAst;
             PluginStringAst body = constant string or encaps list)
  → ★ AstTransform (plugins) — full per-file AST pass, pre-binder
  → binder (core)
  → ★ Check (plugins) — alongside / into the checker
  → ★ Emit (plugins) — alongside / into the emitter for custom nodes & side outputs
```

A plugin may implement **any subset** of the hooks. Registering `p"ns:…"` or backtick ops is optional.

#### Hook 1 — `AstTransform` (post-visitor, pre-binder) — primary mutation surface

- Host passes the **complete file AST** (or a stable IR serialization of it) to each enabled plugin that
  implements this hook, in a deterministic order (document when scheduled; e.g. `plugins.enabled` order).
- Plugin may **inspect and mutate anywhere** in that tree — not only plugin-string / backtick nodes. Examples:
  - lower `p"sql:…"` / `` `cross` `` to core or custom AST;
  - rewrite/expand `#[Dto]` / `#[Route]` / other attributes into methods, properties, or companion decls;
  - inject traits, strip or rewrite calls, desugar framework patterns;
  - insert synthetic helpers the binder will see as ordinary declarations.
- **Must** eliminate every `PluginStringAst` / `PluginInfixAst` the plugin owns (or the host errors after the
  pass if any remain unhandled).
- After all transform plugins finish, binder runs on the resulting tree as if it had been authored that way.

This is the hook that makes **syntax-free plugins** first-class: arch/framework/codegen packages never need a
namespace or operator; they only implement `AstTransform` and/or `Check` / `Emit`.

##### AstTransform activation preconditions (skip when idle)

Spinning up a PHP plugin worker and shipping a full AST is expensive when the plugin would no-op. Each
`AstTransform` plugin may declare **extra** activation preconditions in its manifest. The host evaluates the
**effective** precondition set against a cheap **file-level AST index** *before* invoking the plugin. If nothing
matches, the plugin is **skipped** for that file.

Illustrative author-declared kinds (optional extras):

| Kind | Meaning | Example |
|------|---------|---------|
| `astNodeTypeExists` | At least one node of a given AST type is present | `"PhpAttributeAst"` |
| `astNodeTypeWithValue` | A node of a given type carries a specific value | attribute name `Dto` (literal or from config) |
| `configEquals` | Plugin effective config key equals a value (gating) | `enableDtoAttributeHandler` == `true` |
| `all` | AND-group of predicates (needed for gate + match) | config gate ∧ attribute match |
| `always` | Unconditional (rare; prefer specific predicates) | full-project rewriters |

**Config resolution for predicates:** each plugin has an **effective options object** =
plugin-package defaults (from options schema / manifest defaults) overlaid by
`tyhp.json` → `plugins.options.<package>`. Preconditions may **gate** on those values and may **read string
(and other scalar) values** from them (e.g. which attribute name to match). Missing keys use schema defaults;
unknown keys already fail schema validation at project load.

**Config-aware `when` (v1) — gating + value refs:**

JSON shape (illustrative):

```json
{
  "name": "acme/tyhp-dto",
  "hooks": ["astTransform", "emit"],
  "optionsSchema": "plugin/options.schema.json",
  "optionsDefaults": {
    "enableDtoAttributeHandler": true,
    "matchDtoAttributeName": "Dto"
  },
  "astTransform": {
    "when": [
      {
        "all": [
          { "configEquals": { "path": "enableDtoAttributeHandler", "value": true } },
          {
            "astNodeTypeWithValue": {
              "type": "PhpAttributeAst",
              "name": { "$config": "matchDtoAttributeName" }
            }
          }
        ]
      }
    ]
  }
}
```

Project override example (`tyhp.json`):

```json
{
  "plugins": {
    "enabled": ["acme/tyhp-dto"],
    "options": {
      "acme/tyhp-dto": {
        "enableDtoAttributeHandler": true,
        "matchDtoAttributeName": "DataTransfer"
      }
    }
  }
}
```

Then the predicate matches `#[DataTransfer]` (not `#[Dto]`), and if the user sets
`"enableDtoAttributeHandler": false` the whole `all` group fails → that wake-up path does not fire (host-injected
ns/op predicates, if any, still can).

Conventions:

- **`configEquals`:** `{ "path": "<dot.path>", "value": <json-scalar> }` — compare effective options at `path` with
  `value` (strict JSON equality for bool/number/string/null). Primary **gating** form.
- **`{ "$config": "<dot.path>" }`:** placeholder anywhere a string (or scalar) literal is allowed in a predicate
  (e.g. `name`, and later other fields). Resolved from effective options before matching.
- **`all`:** AND. Top-level `when` entries remain OR’d with each other and with host-injected ns/op predicates.
- Host evaluates config refs **before** AST matching (cheap); no plugin PHP process needed to decide skip vs run
  for these gates.
- Same effective options object is what the plugin receives at invoke time (so runtime handler logic and
  preconditions stay consistent).

**Host-injected predicates (v1 — mandatory, not configurable):**

For every plugin that registers `namespaces` and/or `operators` in `plugin.tyhp.json`, the host **always** unions
the following into that plugin’s effective `when` set — authors do **not** list these, and cannot turn them off:

- `pluginStringNamespace` → every **source spelling** of each registered namespace (identity + project aliases from
  `plugins.namespaces`)
- `pluginInfixOperator` → every **source spelling** of each registered operator id (identity + project aliases from
  `plugins.operators`)

So a plugin that declares `namespaces.sql` / `operators.cross` is guaranteed to wake up for `p"sql:…"`,
`p<<<sql`, `` `cross` ``, and any aliased spellings (`acmesql`, `x`, …). There is no footgun where a package
registers a dialect/op but forgets the matching precondition, and **manifest `when` never needs to restate
aliases** — the host patches from the binding table. These injections are **not** gated by author `configEquals`
unless we later add an explicit opt-out (v1: none).

Manifest sketch (ns/op plugins — no manual `pluginStringNamespace` / `pluginInfixOperator` in `when`):

```json
{
  "name": "acme/tyhp-sql",
  "hooks": ["astTransform", "check"],
  "namespaces": {
    "sql": { "forms": ["expr_rvalue"] },
    "orm": { "forms": ["expr_rvalue"] }
  },
  "astTransform": {
    "when": []
  }
}
```

Effective `when` after host injection (if project aliases `sql` → source `acmesql`, `orm` unaliased):

```json
[
  { "pluginStringNamespace": ["acmesql", "orm"] }
]
```

```json
{
  "name": "acme/tyhp-vec",
  "hooks": ["astTransform", "check", "emit"],
  "operators": { "cross": {}, "dot": {}, "hadamard": {} },
  "astTransform": {
    "when": []
  }
}
```

Effective `when` if `` `+` `` aliases to `hadamard` and `` `x` `` aliases to `cross`:

```json
[
  { "pluginInfixOperator": ["cross", "x", "dot", "hadamard", "+"] }
]
```

Semantics:

- Top-level effective preconditions are OR’d (any match → run). Use `all` for AND (gate ∧ match).
- Host builds the AST index once per file (including source namespace labels / backtick ids), then filters plugins.
- **Injected ns/op predicates are mandatory** whenever the plugin registers namespaces/operators. Author `when`
  entries are **additional** ORs (config-gated attribute scans, etc.).
- Plugins with **no** namespaces, **no** operators, and **empty/omitted** `when` are treated as `always` (run every
  file) — discouraged except for deliberate whole-project rewriters. Prefer explicit config-gated or
  `astNodeTypeExists` predicates instead.
- If a plugin registers namespaces/ops **and** also writes redundant `pluginStringNamespace` /
  `pluginInfixOperator` in `when`, host may ignore/dedupe them; canonical behavior is injection from the registry +
  alias table only.
- `Check` / `Emit` may grow similar skip-logic later; v1 focuses on `AstTransform` (expensive full-tree round-trip).

#### Hook 2 — `Check` (checker stage)

- Runs with binder output available (scopes, symbols, types) — what `AstTransform` deliberately does not have yet.
- Emit diagnostics; enforce layering, forbidden APIs, framework container rules, taint-style patterns, etc.
- Supply **checker rules for any custom AST nodes** the plugin introduced in `AstTransform`.
- Prefer **read-only** analysis here. Large structural rewrites belong in `AstTransform`; typed/post-binder
  rewrites are **Idea 10** (`TypedAstTransform` + rebind flag).

#### Hook 3 — `Emit` (emitter stage)

- Lower custom nodes to PHP; emit companion/side files (OpenAPI, serializers, stubs).
- Ordinary core AST needs no plugin emit — the core emitter handles it after a successful transform.

#### What plugins intentionally do *not* hook

| Stage | Plugin access? |
|-------|----------------|
| Lexer / grammar / parser | No (except core-owned `p` / `` ` `` forms) |
| Visitor / parse-tree `*Context` walk | **No** |
| Binder internals | No direct hooks in v1 — transform earlier so binder sees normal/custom AST |
| Optimizer (Stories 23–24) | **Idea 10 (v2)** — not v1 |
| Post-binder / typed AST rewrite | **Idea 10 (v2)** |

**Locals / `$this` / class scope (v1):** double-quoted `p"…"` already surfaces captures as normal encaps AST
(variables / `${…}` / `{…}` exprs) inside the transient plugin node — plugins do **not** re-lex interpolation.
`AstTransform` replaces those with ordinary references / calls before binder; binder resolves; checker types.
Single-quoted `p'…'` has no interpolation (PHP semantics). Type-aware DSLs either encode types in helper
signatures or keep a custom node and implement `Check`.

### Syntax

#### Plugin string islands — `p'ns:…'` / `p"ns:…"`

```tyhp
// Single-quoted: literal body (no $ / encaps interpolation) — like PHP '…' / b'…'
$q1 = p'sql:SELECT id, name FROM users WHERE id = 1';

// Double-quoted: full PHP encapsed-string semantics — like PHP "…" / b"…"
// $userId, {$expr}, ${…} are already parsed to encaps AST nodes for the plugin
$row = p"sql:SELECT id, name FROM users WHERE id = $userId LIMIT 1";
$msg = p"sql:SELECT * FROM users WHERE name = {$user->name}";

return p"sql:SELECT COUNT(*) FROM users" + 1;
```

- Lexical form analogous to **`b'…'` / `b"…"`** (binary strings): **`p` prefix** + normal PHP quote rules.
  Never `#"` (PHP `#` line-comment collision).
- **Quote semantics match PHP (and existing Tyhp string lexing):**
  - **`p'…'`** — constant encapsed string; no variable interpolation; escapes follow single-quoted rules.
  - **`p"…"`** — enters the same double-quote / encaps path as ordinary `"…"` (`ST_DOUBLE_QUOTES`,
    `encapsList` / `encapsVar`): `$var`, `$obj->prop`, `{$expr}`, `${…}`, etc. are **already AST** when the
    plugin runs (literal segments + `IEncapsVarOrString` children), not a raw unparsed blob the plugin must
    re-scan for `$`.
- Transient **plugin expression AST** wraps: registered **namespace**, plus the **body** as either a constant
  string or an encaps list (same shapes the visitor already builds for normal scalars). Plugins consume that
  structured body when lowering.
- Payload **requires** a statically discoverable `namespace:` prefix (e.g. leading constant segment
  `sql:…`). Host dispatches by registered namespace — no scan-until-handled. Interpolation must not obscure the
  namespace (e.g. `p"$x:SELECT"` is invalid for dispatch).
- **Namespace identifier rules (v1):** the `ns` in `p"ns:…"`, `p'ns:…'`, and the heredoc/nowdoc label must be a
  PHP/Tyhp **`LABEL`** — the same fragment as variable/identifier names in `PhpLexer.g4`:

  ```text
  LABEL: [a-zA-Z_\u0080-\u00ff][a-zA-Z0-9_\u0080-\u00ff]*
  ```

  So namespaces are simple identifiers (`sql`, `gql`, `jsonschema`), not dotted paths, URLs, or arbitrary
  punctuation. That keeps quoted forms and **unquoted** heredoc/nowdoc labels (`p<<<sql` / `p<<<'sql'`) on the
  same spelling (PHP heredoc labels without quotes are already `LABEL`s). Manifest `namespaces` keys must obey
  the same rule; invalid keys fail at plugin load.
- Unknown namespace → error. Two plugins claiming the same namespace → install/config error unless the app
  explicitly resolves it (alias / disable — see Configuration).
- **Multi-namespace plugins (v1):** a single plugin **may register many namespaces** in one manifest (e.g. `sql`,
  `orm`, `ddl`). Complex packages expose multiple island dialects without shipping one Composer package per
  dialect. Host dispatches each source `ns` to the owning plugin; the plugin’s hooks see which namespace (and any
  project alias) applied.
- **v1 grammar placement:** base expression only (`phpExprPrecBaseGrammarAddon` → anywhere `expr` is legal).
- **Not** statement / lvalue / type-name in v1 (statement-form islands → Idea 10).
- Multi-statement expansion in an expr slot must lower to a **single expression** (helper call, IIFE, etc.) or be
  rejected.

##### Heredoc / nowdoc islands (v1) — `p<<<ns` / `p<<<'ns'`

Long DSLs must not force a one-line `p"…"`. Mirror PHP / `b<<<` style:

```tyhp
// Heredoc — interpolates (same encaps rules as p"…"); LABEL is the plugin namespace
$row = p<<<sql
SELECT id, name
FROM users
WHERE id = $userId
LIMIT 1
sql;

// Nowdoc — no interpolation (same as p'…')
$q = p<<<'sql'
SELECT id, name FROM users WHERE id = 1
sql;
```

- **Label = namespace** (no repeated `sql:` prefix in the body). Host dispatches on the label exactly as for
  `p"sql:…"`. The label is an unquoted (or nowdoc-quoted) **`LABEL`**, so it must already satisfy the namespace
  identifier rules above — no crazy names that would require a double-quoted heredoc label.
- Heredoc → encaps AST children; nowdoc → constant body. Closing label rules follow PHP heredoc/nowdoc.
- Still **expr-only** in v1.

#### Backtick operators — `` `$a `op` $b` ``

```tyhp
$n = ($a `cross` $b) `dot` $c;
$hadamard = $a `+` $b;   // plugin op id "+", NOT bare +
$sum = $a + $b;          // normal PHP/Tyhp + / operator overloads
```

- Binary, **left-associative**, **one fixed precedence** for all custom ops (document when scheduled; do not
  invent per-op precedence in v1).
- Interior text is an **opaque compile-time operator id** (not dynamic/interpolated). May be `+`, `cross`, `×`,
  etc., as long as it contains no `` ` ``.
- Enabled project-wide via plugin registration — **not** per-file `use plugin operator …`.

### Plugin packaging — Composer packages + `plugin.tyhp.json` (v1)

**Plugins are Composer packages.** There is no parallel plugin-install channel. A plugin package is a normal
Composer package that also carries a root **`plugin.tyhp.json`** — that file **is the plugin manifest**.

#### Discovery

When Tyhp runs a project build/check, it discovers plugins from:

1. **Project** — Composer packages installed for the project (typically under `vendor/`) that contain a root
   `plugin.tyhp.json`.
2. **Global** — Composer packages installed in the user’s global Composer home that likewise contain
   `plugin.tyhp.json` (exact discovery path follows Composer’s global vendor layout when scheduled).

`tyhp.json` `plugins.enabled` (and order/options) selects and configures which of the discovered packages actually
run. A package present but not enabled is inert.

#### Layout inside the plugin package

Split **compile-time plugin host code** from **runtime library code** so the app never autoloads the compiler
hooks:

```text
acme/tyhp-sql/                          # Composer package root
  composer.json                         # name, require (incl. "tyhp": ">=…"), autoload for RUNTIME only
  plugin.tyhp.json                      # MANIFEST — hooks, namespaces, entrypoint path, etc.
  plugin/                               # compile-time only (AstTransform/Check/Emit PHP)
    Plugin.php                          # entrypoint class loaded by Tyhp host, NOT by app
    …
  src/                                  # runtime library used by emitted / rewritten app code
    Sql.php                             # e.g. \Acme\TyhpSql\Sql::query
  tyhp_src/                             # optional Tyhp sources for the runtime library
  …
```

- **`composer.json` autoload** maps only the **runtime** tree (`src/`, emitted PHP, etc.) — the same surface the
  consuming app needs at request time.
- **Compile-time `plugin/` (or path named in the manifest)** is **excluded** from Composer autoload (omit from
  `autoload` / `autoload-dev` for production consumers; do not PSR-4 it into the app). Tyhp’s host loads that
  path explicitly when invoking the plugin worker.
- Runtime helpers the rewrite targets may live **in this same package** (`src/`) **or** in a **separate Composer
  dependency** listed under `require` (e.g. `acme/tyhp-sql` requires `acme/sql-runtime`). No special “companion”
  install step beyond Composer.

#### `plugin.tyhp.json` — the manifest

Root of the package. Declares everything Tyhp needs that is not already in `composer.json`:

```json
{
  "name": "acme/tyhp-sql",
  "entrypoint": "plugin/Plugin.php",
  "entrypointClass": "Acme\\TyhpSql\\Plugin\\Plugin",
  "hooks": ["astTransform", "check"],
  "dependsOn": [],
  "optionsSchema": "plugin/options.schema.json",
  "namespaces": {
    "sql": {
      "forms": ["expr_rvalue"],
      "captureLocals": true
    },
    "orm": {
      "forms": ["expr_rvalue"],
      "captureLocals": true
    },
    "ddl": {
      "forms": ["expr_rvalue"]
    }
  },
  "astTransform": {
    "when": []
  }
}
```

A single plugin may list **one or many** `namespaces` (and many `operators`). Namespace/op wake-ups are
**host-injected** from this registry (+ aliases); do not re-list them under `when`.

```json
{
  "name": "acme/tyhp-arch",
  "entrypoint": "plugin/Plugin.php",
  "entrypointClass": "Acme\\TyhpArch\\Plugin\\Plugin",
  "hooks": ["check"]
}
```

```json
{
  "name": "acme/tyhp-dto",
  "entrypoint": "plugin/Plugin.php",
  "entrypointClass": "Acme\\TyhpDto\\Plugin\\Plugin",
  "hooks": ["astTransform", "emit"],
  "optionsSchema": "plugin/options.schema.json",
  "optionsDefaults": {
    "enableDtoAttributeHandler": true,
    "matchDtoAttributeName": "Dto"
  },
  "astTransform": {
    "when": [
      {
        "all": [
          { "configEquals": { "path": "enableDtoAttributeHandler", "value": true } },
          {
            "astNodeTypeWithValue": {
              "type": "PhpAttributeAst",
              "name": { "$config": "matchDtoAttributeName" }
            }
          }
        ]
      }
    ]
  }
}
```

```json
{
  "name": "acme/tyhp-vec",
  "entrypoint": "plugin/Plugin.php",
  "entrypointClass": "Acme\\TyhpVec\\Plugin\\Plugin",
  "hooks": ["astTransform", "check", "emit"],
  "operators": {
    "cross": {},
    "dot": {},
    "hadamard": {}
  },
  "astTransform": {
    "when": []
  }
}
```

- **`name`** should match the Composer package name.
- **`entrypoint` / `entrypointClass`** — compile-time class under the excluded plugin path; implements the hooks
  subset it lists.
- Syntax-free plugins omit `namespaces` / `operators`.
- Manifest `forms` gate where `p'ns:…'` / `p"ns:…"` / `p<<<` may appear (`expr_rvalue` in v1).
- Preferred transform target: **core AST**; custom nodes require `Check` / `Emit` as needed.
- Runtime code location is **Composer’s problem** (`autoload` + `require`) — the manifest does not need to
  duplicate PSR-4 roots unless we later add optional hints for tyhpdef discovery.

#### App `composer.json` sketch (consumer)

```json
{
  "require": {
    "acme/tyhp-sql": "^1.0",
    "acme/tyhp-vec": "^1.0"
  }
}
```

After `composer install`, runtime classes from those packages are available to the app; Tyhp discovers
`plugin.tyhp.json` in each package and runs compile-time hooks only for packages listed in `tyhp.json`
`plugins.enabled`.

### Configuration (`tyhp.json`) — hybrid auto-register

> **Provisional keys** — register in `CONVENTIONS.md` §4 when this idea is scheduled. Names below are illustrative.

Tyhp discovers Composer packages (project + global) that contain `plugin.tyhp.json`. Listing a package under
`plugins.enabled` **activates** it and **auto-registers** its manifest namespaces and operator ids.

**App config is only required for:**

- which discovered plugins to enable (and their **process order**),
- package-specific **options** (validated against the plugin’s options schema),
- **aliases** for namespaces and operators when resolving conflicts (or for nicer spellings),
- **disables** / other conflict resolution.

```json
{
  "include": ["./src/**/*.tyhp"],
  "output": { "path": "./build", "phpVersion": "8.2" },
  "plugins": {
    "enabled": [
      "acme/tyhp-sql",
      "other/tyhp-sqlish",
      "acme/tyhp-vec"
    ],
    "options": {
      "acme/tyhp-sql": {
        "connection": "default"
      }
    },
    "namespaces": {
      "acmesql": "acme/tyhp-sql:sql",
      "acmeorm": "acme/tyhp-sql:orm",
      "othersql": "other/tyhp-sqlish:sql"
    },
    "operators": {
      "+": "acme/tyhp-vec:hadamard",
      "cross": "acme/tyhp-vec:cross",
      "x": "acme/tyhp-vec:cross"
    }
  }
}
```

Reading of alias maps:

- **`plugins.namespaces`:** **source spelling → `package:canonicalNamespace`**. Here the app writes
  `p"acmesql:…"`, `p<<<acmesql`, etc.; host routes those to `acme/tyhp-sql`’s canonical `sql`. The other
  package’s `sql` is exposed as `othersql`. Canonical names that were remapped must **not** remain claimable in
  source by the displaced plugin.
- **`plugins.operators`:** **source backtick id → `package:canonicalOpId`** (or a short form when unambiguous).
  `` `$a `x` $b` `` and `` `$a `cross` $b` `` can both map to the same canonical `cross`.

Collision policy:

1. Auto-register on enable (a plugin may contribute **multiple** namespaces and operators).
2. Two plugins claiming the same **source** `p` namespace or backtick id → **config/install error** naming both;
   resolve via alias map and/or disable.
3. **Attributes are not exclusively owned.** Conflicts use **process order** + each plugin’s **options**
   (retarget / disable). No exclusive attribute registry.

##### Alias notification to plugins (v1 — required)

When the host invokes a plugin, it passes an **effective binding table** for that plugin, e.g.:

```json
{
  "namespaces": {
    "sql": { "sourceSpellings": ["acmesql"] },
    "orm": { "sourceSpellings": ["acmeorm"] },
    "ddl": { "sourceSpellings": ["ddl"] }
  },
  "operators": {
    "cross": { "sourceSpellings": ["cross", "x"] },
    "hadamard": { "sourceSpellings": ["+"] },
    "dot": { "sourceSpellings": ["dot"] }
  }
}
```

Rules:

- Plugins **must** use this table (or equivalent API) — not hard-coded assumption that source spelling ===
  manifest key — when deciding which islands/ops they own in *this* project.
- After aliasing `sql` → source `acmesql`, that plugin must **not** still handle bare `p"sql:…"` if `sql` was
  given to another package (or left unowned). Host dispatch is authoritative; the table keeps plugin-local logic
  (logging, diagnostics, TextMate later, multi-dialect switches) honest.
- Same for operators: if `` `+` `` is aliased to `hadamard`, the vec plugin treats source `+` as canonical
  `hadamard` and does not assume bare `` `hadamard` `` exists unless listed.
- Unaliased entries appear with `sourceSpellings` equal to the canonical id (identity mapping).
- **AstTransform preconditions** always include host-injected `pluginStringNamespace` /
  `pluginInfixOperator` matches for those **source spellings** (see above) — not something authors configure per
  alias.

### Process / load order (v1)

From day one, authors must control which plugin runs before which:

- Manifest may declare `dependsOn: ["acme/tyhp-dto"]` and/or numeric `order` hints.
- App `tyhp.json` **`plugins.enabled` array order** is the default process order; optional
  `plugins.order` / dependency overrides win when specified.
- Host topologically sorts by dependencies, then applies explicit order; cycles → config error.
- Same order applies across `AstTransform` → `Check` → `Emit` for a given plugin relative to others (document
  exact per-hook sequencing when scheduled).

### Runtime library code (v1) — same Composer package or a dependency

Rewritten/emitted code may call helpers such as `\Acme\TyhpSql\Sql::query`. Those helpers are ordinary Composer
autoload surface:

- **In-package runtime** — `src/` (or equivalent) of the plugin package, listed in that package’s `composer.json`
  `autoload` (and optional `tyhp_src/` compiled into it).
- **External runtime** — another package in the plugin’s `require` (or the app’s `require`).

Compile-time `plugin/` code stays **out** of app autoload. There is no separate “install companion” step beyond
`composer require` of the plugin (and whatever it depends on). Emitted code must only reference symbols that are
reachable via the app’s Composer dependency graph.

### Host compatibility (v1)

No separate `tyhp-plugin-host` version channel. Plugin `composer.json` constrains the Tyhp toolchain like any other
package, e.g. `"tyhp": ">=804.4.3"`. Host rejects plugins whose constraint does not satisfy the running compiler.

### `plugins.options` config schema (v1)

Each plugin may ship a **JSON Schema** (and optional **`optionsDefaults`**) for its options object. Host builds
**effective options** = defaults ← `tyhp.json` `plugins.options.<package>`, validates against the schema at project
load, and fails with path-accurate config diagnostics on mismatch.

Effective options are used for:

- plugin invoke payload (handler logic),
- **AstTransform precondition gating** (`configEquals`) and **value refs** (`{ "$config": "…" }`) — e.g. enable a
  DTO attribute handler and choose the attribute name without hard-coding `Dto` in the manifest,
- author-facing knobs (connections, strictness, attribute rename/disable for conflict resolution, etc.).

### Plugin test harness (v1)

First-class author testing (parallel to conformance fixtures):

- Fixture: input `.tyhp` (+ optional `tyhp.json` plugin enablement) → expected diagnostics and/or expected
  post-transform AST / emitted PHP snippets.
- Runner invokes the real host↔plugin protocol with the plugin under test — no Dotnet SDK required for authors.
- Golden fixtures live with the plugin package; Tyhp may also ship a few reference plugins under
  `tests/conformance/` when the story is scheduled.

### End-to-end usage sketch

**Syntax plugin (SQL + vec ops + heredoc):**

```tyhp
<?tyhp
namespace App;

function loadUser(\PDO $pdo, int $userId): User {
    $row = p<<<sql
SELECT id, name FROM users WHERE id = $userId LIMIT 1
sql;
    return User::fromRow($row);
}

function physics(Vec3 $a, Vec3 $b, Vec3 $c): Vec3 {
    $n = ($a `cross` $b) `dot` $c;
    $hadamard = $a `+` $b;
    $sum = $a + $b;
    return $hadamard;
}
```

Conceptual `AstTransform` result (binder/checker see ordinary AST):

```tyhp
$row = \Acme\TyhpSql\Sql::query(
    $pdo,
    'SELECT id, name FROM users WHERE id = ? LIMIT 1',
    [$userId],
);

$n = \Acme\TyhpVec\Ops::dot(\Acme\TyhpVec\Ops::cross($a, $b), $c);
$hadamard = \Acme\TyhpVec\Ops::hadamard($a, $b);
```

**Syntax-free plugins (still first-class):**

```tyhp
#[Dto]
class UserRow {
    public function __construct(
        public readonly int $id,
        public readonly string $name,
    ) {}
}

// Arch/check plugin (Check hook only): Domain must not reference Infrastructure
namespace App\Domain {
    // use App\Infrastructure\Db;  → diagnostic from acme/tyhp-arch
}
```

`acme/tyhp-dto` uses `AstTransform` to expand `#[Dto]`. If another plugin also watches `Dto`, the user sets
process order and/or options (`attributeName`, `enabled: false`) — not exclusive ownership.

### Phased delivery (v1, when scheduled)

| Phase | Ship |
|-------|------|
| 1 | Host↔PHP protocol + Composer discovery (project + global `plugin.tyhp.json`) + **order/deps** + **`tyhp` constraint** + **options JSON Schema** + **test harness** |
| 2 | **`AstTransform`** (preconditions + full-file mutate) + runtime via Composer autoload (in-package or dependency) |
| 3 | `p'…'` / `p"…"` + **`p<<<` / `p<<<'…'`** expr islands + namespace dispatch |
| 4 | **`Emit`** + custom-node check/emit |
| 5 | Backtick binary ops + hybrid auto-register operator map |

v2 items → [Idea 10](#idea-10--compiler-plugins-v2).

### Decisions (defaults chosen) — v1

1. **Tyhp/PHP out-of-process plugins** — not C# NuGet-only, not embedded PHP in the compiler.
2. **No third-party ANTLR mutation** — only core-owned `p` / `` ` `` forms.
3. **No visitor hooks** — plugins see **AST** after visit.
4. **Primary mutation surface = pre-binder `AstTransform`** (full file; activation preconditions).
5. **Host always injects alias-aware `pluginStringNamespace` / `pluginInfixOperator` preconditions** for every
   registered namespace/op (source spellings). Not optional; authors do not configure these in `when`.
6. **Author `when` may reference effective plugin config:** `configEquals` for gating;
   `{ "$config": "path" }` for string/scalar fields (e.g. attribute names); `all` for AND. Defaults from the
   plugin package, overridden by `plugins.options` in `tyhp.json`. Extra `when` entries are for non-ns/op
   triggers (attributes, etc.).
7. **`Check` / `Emit` optional**; any subset OK.
8. **Process order is v1** — `dependsOn` / `enabled` order / overrides; cycles fail.
9. **`p'…'` / `p"…"` / heredoc / nowdoc** = DSL islands (expr only); label = namespace for `p<<<`.
10. **Plugin namespaces are `LABEL` identifiers** (`PhpLexer` `LABEL` / variable-name rules) — same spelling for
   `p"ns:…"`, manifest keys, and unquoted heredoc/nowdoc labels; no dotted/URL/punctuation namespaces.
11. **One plugin may register multiple namespaces and multiple operators.**
12. **Project aliases are v1:** `plugins.namespaces` / `plugins.operators` map source spellings →
    `package:canonical`; host **always notifies** the plugin of its effective binding table so it does not handle
    displaced spellings or miss aliased ones.
13. **PHP quote/encapsed semantics**; backticks = opaque binary op ids (op ids are *not* required to be `LABEL`s —
    they may be `+`, `×`, etc., since they are not heredoc labels).
14. **Namespaces/operators:** single owner per **source spelling** (error on clash unless config resolves).
15. **Attributes:** no exclusive ownership — order + options resolve conflicts.
16. **Plugins are Composer packages**; manifest = root **`plugin.tyhp.json`**; discover project + global installs.
17. **Compile-time plugin path excluded from Composer autoload**; runtime helpers via package `autoload` and/or
    Composer dependencies — not a separate companion install channel.
18. **Host compat via Composer `"tyhp": ">=…"`** — not a parallel host semver channel.
19. **`plugins.options` validated** against plugin-shipped JSON Schema (with defaults).
20. **Plugin test harness** ships with v1.
21. **No custom unary ops**; no multi-round `.tyhp` re-entry (deferred, not rejected).

### Risks & Edge Cases — v1

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Process-per-phase overhead | Preconditions skip idle plugins; batch transforms |
| 2 | IR / API breakages | Composer `tyhp` constraint; semver discipline |
| 3 | Backtick vs future `` ` `` uses | Reserve infix-op position; fixtures |
| 4 | SQL/local capture / injection | Encaps → bound params; plugin owns parameterization |
| 5 | Namespace/op collisions | Hard error + alias/disable; **alias table passed into plugin** |
| 6 | Plugin still handles old spelling after alias | Host dispatch + binding table; conformance that displaced `sql` is not handled |
| 7 | Attribute double-handling | Document order + options; conformance |
| 8 | Custom nodes without Check/Emit | Host error |
| 9 | Heredoc label ≠ registered/aliased namespace | Unknown-namespace error |
| 10 | Dependency cycles in plugin order | Config error at load |
| 11 | False-negative / forgotten ns-op `when` | **Mandatory host injection** from registry + aliases |
| 12 | IDE highlighting inside islands | Opaque in v1; **Idea 10** TextMate injection |

### Golden Fixtures / Tests (Acceptance) — v1

- Check-only and AstTransform syntax-free plugins (arch / `#[Dto]`).
- AstTransform skip when author extras miss **and** no registered ns/op source spelling is present; run when an
  injected ns/op spelling or an author `when` hits.
- Injected preconditions: plugin with `namespaces.sql` wakes on `p"sql:…"` without listing it in `when`; after
  alias to `acmesql`, wakes on `p"acmesql:…"` and not on displaced bare `sql` if another owner has it.
- Config-gated `when`: `enableDtoAttributeHandler: false` skips the DTO attribute wake path; renaming
  `matchDtoAttributeName` to `DataTransfer` matches `#[DataTransfer]` via `{ "$config": "…" }`.
- `p'sql:…'`, `p"sql:…"`, `p<<<sql` (heredoc encaps), `p<<<'sql'` (nowdoc) lower correctly; unknown ns fails.
- Invalid namespace spellings (e.g. `sql.v2`, `my-sql`) rejected at manifest load and/or parse — must match `LABEL`.
- **Multi-namespace:** one plugin registers `sql` + `orm`; both `p"sql:…"` and `p"orm:…"` dispatch to it.
- **Namespace alias:** `acmesql` → `acme/tyhp-sql:sql`; `p"acmesql:…"` handled; bare `p"sql:…"` not handled by that
  plugin when displaced; plugin invocation receives binding table reflecting the alias.
- **Operator alias:** source `` `x` `` → canonical `cross`; plugin sees mapping; does not require `` `cross` `` in
  source if only `x` is configured.
- Double-quoted encaps / heredoc interpolations yield encaps AST children before transform.
- Process order: plugin A before B changes transform outcome; `dependsOn` cycle fails at config load.
- Options schema: invalid `plugins.options` fails with config diagnostic.
- Runtime via Composer: in-package `src/` autoload and/or a `require`d dependency; compile-time `plugin/` not in app
  autoload.
- Composer `tyhp` constraint: incompatible plugin rejected.
- Discovery: project `vendor/` plugin and a global-install plugin both found when enabled.
- Test harness: sample plugin package passes its own golden fixtures via the harness.
- Backtick ops / `` `+` `` vs bare `+`; leftover PluginString/Infix after transform → error.
- Attribute conflict: two plugins + order/options resolve without exclusive-ownership API.
- No Dotnet SDK required for plugin authors.

---

## Idea 10 — Compiler Plugins v2

> **Status:** Tier 4, future plans. **v2** of [Idea 9](#idea-9--compiler-plugins-tyhpphp-host-pns--backtick-operators).
> Schedule only after v1 has shipped enough real plugins to validate demand.
> **Prerequisites:** Idea 9 (v1 plugin host); Story 19.5 (VS Code extension) for IDE/TextMate pieces; Stories 23–24
> for optimizer hooks; Story 08 binder/checker maturity for post-binder transform + rebind.
> **Related:** brainstorm 2026-08-05 follow-ups (typed transform, IDE islands, fix-its, statements, sandbox, cache).

### Summary

Grow the plugin platform beyond v1’s pre-binder / compile-only / expr-only surface:

1. **`TypedAstTransform` (post-binder)** — full AST pass with symbols/types available; each mutation flags
   **needs rebind** or not; host rebinds when any participant requests it (combine former “typed transform” +
   “project/cross-file context” needs — binder scopes and project symbols are visible here).
2. **IDE / TextMate for `p` islands** — plugin ships a TextMate grammar (or injection) for its namespace; Story 19.5
   VS Code extension matches `p"ns:…"`, `p<<<ns`, etc., and applies highlighting; room for further LSP pickup
   (completion, hover) as the extension evolves.
3. **Diagnostic spans + fix-its** — accurate offsets inside islands/encaps; suggested fixes consumable by
   `tyhp lint --fix` / IDE.
4. **Statement-form islands** — `p'ns:…';` / heredoc as statements, not only exprs.
5. **Sandbox policy** — **vital but under-designed**; constrain filesystem/network/process access for PHP plugin
   workers. Leave detailed policy open; design when scheduling v2.
6. **Shared plugin cache** — build-wide memo (DB schema, OpenAPI doc, etc.) loaded once per plugin per build.
7. **Optimizer hooks** — once Stories 23–24 exist, let plugins mark purity / forbid certain opts / contribute
   opt-aware metadata.

### Post-binder transform + rebind

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

### IDE integration (Story 19.5)

- Plugin manifest may point at a **TextMate grammar** / injection scoped to its namespace.
- `vscode-tyhp` detects `p` / `p<<<` forms, reads the namespace/label, and activates the matching grammar for the
  island body (encaps `$vars` can remain Tyhp/PHP scopes).
- Additional IDE surfaces (completion, hover, go-to for tables/columns) are desirable but unspecified in detail —
  capture as follow-ons once TextMate injection works.

### Diagnostic spans & fix-its

- Plugin diagnostics carry **source spans** mapped through encaps/heredoc bodies (not only the outer `p` token).
- Optional **fix-it** payloads (range + replacement text) for lint/IDE apply.

### Statement-form islands

- GrammarAddon on statement positions; same namespaces/quote/heredoc rules; typically side-effecting DSLs
  (`p'route:…';`, `p<<<fsm …`).

### Sandbox policy (open design)

Needs a real threat model: read project files vs arbitrary FS, network for schema pull, process spawn, time/memory
limits. v2 must ship *a* policy; exact defaults TBD when scheduled — do not pretend it is specified here.

### Shared plugin cache

Host-provided keyed cache for the build (`schema:default`, etc.) so SQL/OpenAPI plugins do not re-parse huge
artifacts per file. Invalidation rules TBD (file mtimes / config hash).

### Optimizer hooks

After optimizer modules exist: plugins may declare purity, freeze regions, or supply opt metadata so lowering and
opts do not fight. Exact API follows Stories 23–24 shapes.

### Explicitly still deferred (not in v2 either)

- **Multi-round generated `.tyhp` re-entering the compile pipeline** — considered during v1 design; **not
  rejected**; leave unplanned until a concrete product need appears. v2 side-file emit (PHP/JSON/OpenAPI) remains
  enough for foreseeable codegen.

### Decisions (defaults chosen) — v2

1. Post-binder transform + **rebind flag** rather than a separate “project analysis only” API.
2. TextMate-first IDE story via Story 19.5; richer LSP is incremental.
3. Statement islands after expr+heredoc have proven out in v1.
4. Sandbox is mandatory for v2 readiness but **spec is intentionally open**.
5. No `.tyhp` re-entry pipeline in v2 planning.

### Golden Fixtures / Tests (Acceptance) — v2

- TypedAstTransform sees resolved types; `needsRebind` true triggers a second bind; false does not.
- TextMate: fixture workspace with plugin grammar highlights `p<<<sql` body in VS Code extension tests.
- Fix-it applies via lint harness.
- Statement-form `p'…';` lowers / checks.
- Shared cache hit across two files in one build.
- Sandbox: at least one denied operation fixture once policy exists.
- Optimizer hook smoke once Stories 23–24 APIs exist.

---

## Idea 11 — Pipe-Emit Chained Extension Calls (`|>` on PHP 8.5+)

> **Status:** Tier 4, future plans. Emit-only optimization; Tyhp source stays `$recv->ext()->ext()`.
> **Prerequisites:** Story 11 (extension rewrite to static calls), Story 14.5 (`|>` parse/emit +
> `IsPhpVersionAtLeast(8, 5)`), Story 09/11 emitter. Story 21’s built-in scalar catalog would benefit once it
> ships (`$name->trim()->toLower()`).
> **Related:** `BuildPipeExpression` / `PipeOperatorEmitterTests` already implement *source* `|>`; this idea is
> the inverse — *generate* `|>` from extension chains that today nest as `C::c(C::b(C::a($x)))`.
> **Source:** chaining example on Scalar Pseudo-Objects (`$input->trimmed()->lower()->dashed()`), 2026-08-14.

### Summary

Extension method chains rewrite today to nested static calls (innermost = leftmost receiver):

```tyhp
$result = $input->trimmed()->lower()->dashed();
```

```php
// Current emit (all PHP targets)
$result = \StringHelpers::dashed(\StringHelpers::lower(\StringHelpers::trimmed($input)));
```

When `output.phpVersion` is **8.5 or newer**, emit the same chain with PHP’s pipe operator **when able**:

```php
$result = $input
    |> \StringHelpers::trimmed(...)
    |> \StringHelpers::lower(...)
    |> \StringHelpers::dashed(...);
```

That is equivalent (`$a |> f(...) |> g(...)` ≡ `g(f($a))`) and stays left-to-right. Below 8.5, keep nested
calls (same as today’s extension emit; do not invent a second lowering path).

### When it is able

PHP 8.5 `|>` passes the left value as the **callable’s argument**. First-class callable `Ext::method(...)` is
legal on the RHS. Use pipe only when the rewritten static call would take **exactly the piped receiver** (no
extra arguments supplied):

| Tyhp | Able? | Emit ≥ 8.5 |
| --- | --- | --- |
| `$s->trimmed()->lower()` | Yes | `$s \|> \E::trimmed(...) \|> \E::lower(...)` |
| `$s->truncate(10)` | No | `\E::truncate($s, 10)` (extra arg) |
| `$s->trimmed()->truncate(10)` | Partial | `\E::truncate($s \|> \E::trimmed(...), 10)` or keep all nested — pick one policy when scheduled |
| `$s->trimmed()` (single call) | No benefit | `\E::trimmed($s)` — do not introduce `|>` for length-1 |

Default policy when scheduled: **pipe a maximal suffix/prefix of receiver-only calls**; mix-in extra-arg calls
with nested application rather than wrapping every extra-arg step in `(fn($x) => Ext::m($x, …))` unless that
reads better in a long chain. Do not change evaluation order.

Also apply to user `extension` methods and (later) Story 21 stdlib scalar extensions. Instance methods on real
objects are out of scope unless they already lower to static calls the same way.

### Implementation sketch

- Hook after (or in) extension-call rewrite (`ExtensionMethod` emit): detect a left-associated chain of
  rewritten static calls sharing the pipe-friendly shape.
- Gate: `IsPhpVersionAtLeast(8, 5)` (same helper as Story 14.5 pipe).
- RHS spelling: FQCN `\\Ext::method(...)` (FCC), matching current static rewrite names.
- Reuse parenthesization rules from `BuildPipeExpression` (nested left, FCC vs closure).
- Config: none required; optional later opt-out if someone prefers nested calls in diffs.

### Decisions (defaults chosen)

1. **Source unchanged** — authors keep `$x->a()->b()`; pipe is an emit strategy, not new Tyhp syntax.
2. **≥ 8.5 only** — never emit `|>` when targeting 8.2–8.4.
3. **Receiver-only** — extra arguments disable that step’s pipe form.
4. **Length ≥ 2** — single extension calls stay ordinary static calls.

### Risks & Edge Cases

- Extra-arg steps in the middle of a chain (policy above).
- Named arguments / variadics on the extension.
- `by-ref` receiver (if ever allowed) — pipe is by-value; do not pipe.
- Debugging / sourcemaps: one Tyhp chain vs several `|>` ops (Story 17).
- PSR-12 / line wrapping of long pipes vs nested calls.

### Golden Fixtures / Tests (Acceptance)

- `$input->trimmed()->lower()->dashed()` → nested statics on 8.4; `|>` FCC chain on 8.5.
- Single `$s->trimmed()` never emits `|>`.
- `$s->truncate(10)` never emits `|>`.
- Mixed chain has a locked expected spelling once the partial-pipe policy is chosen.
- Existing `PipeOperatorEmitterTests` (source `|>`) still pass; new tests live next to
  `ExtensionMethodEmitterTests`.

---

## Idea 12 — Trait-Requirement Abstract Members

> **Status:** Tier 4, future plans. Emit-only; Tyhp source stays `trait T extends C implements I`. Checker
> diagnostics TYHP4044 / TYHP4045 stay the source of truth for using classes.
> **Prerequisites:** Story 08 (trait `extends`/`implements` check), Story 09/11 (emitter).
> **Related:** [Trait Requirements](../docs/content/tyhp_2000_traitRequirements.md) currently erase the clauses
> entirely. PHP traits cannot `extends` / `implements`; they **can** declare `abstract` methods, which the
> composing class (or a parent) must satisfy.
> **Source:** trait-requirements page (`Cacheable extends Entity implements Serializable`), 2026-08-14.

### Summary

Today a required base/interface is compile-time only. Emitted PHP is a plain trait, so a PHP consumer (or
hand-written PHP class) can `use Cacheable` without `getId()` / `serialize()` and only fail when those
calls run:

```tyhp
trait Cacheable extends Entity implements Serializable
{
    public function getCacheKey(): string
    {
        return static::class . ':' . $this->getId();
    }

    public function toCacheValue(): string
    {
        return $this->serialize();
    }
}
```

```php
// Current emit
trait Cacheable
{
    public function getCacheKey(): string { /* … */ }
    public function toCacheValue(): string { /* … */ }
}
```

When scheduled, still **do not** emit `extends` / `implements` on the trait (invalid PHP). Instead collect
members the trait **actually uses** from those required types and emit matching `abstract` declarations so
PHP enforces the needed signatures:

```php
trait Cacheable
{
    abstract public function getId() /* Entity signature, PHP-representable */;
    abstract public function serialize(): string;

    public function getCacheKey(): string
    {
        return static::class . ':' . $this->getId();
    }

    public function toCacheValue(): string
    {
        return $this->serialize();
    }
}
```

That does **not** name `Entity` or `Serializable` on the trait. It does require whoever `use`s the trait to
provide those methods (own body or inherited), which is as close as PHP can get.

### Used items only

Emit abstracts for symbols referenced on `$this` / `static` / `self` that resolve to the required class or
interface (and members inherited through them). Do **not** dump the whole target API.

| Used from the requirement | Emit |
| --- | --- |
| Instance/static method (`$this->getId()`, `static::foo()`) | `abstract` method with visibility + PHP-erased signature |
| Method the trait already declares | Skip (trait member wins) |
| Unused `Serializable::unserialize` | Skip |
| `private` on the target | Skip — not callable from the trait |
| Property read/write (`$this->id`) | Do **not** emit a trait property (collides with the parent’s property). PHP has no useful pre-8.4 abstract property. Document as methods-only unless a later PHP target makes abstract hooks viable |
| Class constants (`Entity::FOO`) | Skip — traits cannot require constants |

Signatures must be PHP-legal: erase generics / shapes the same way other emit does. Idea 4 docblocks can
annotate the abstracts later.

### Why this helps

- Tyhp already rejects a using class that misses `extends` / `implements`.
- Emitted PHP is also consumed as PHP. Abstract used-members catch `use Cacheable` from PHP without those
  methods at **class load**, not at first call.
- Parent implementations count in PHP (`User extends Entity` already has `getId()`), so a correct Tyhp
  using class does not need to redeclare the abstracts.

### Decisions (defaults chosen)

1. **Used members only** — not the full required type.
2. **Methods first** — properties/constants are out until PHP can express them without colliding.
3. **Keep erasing `extends`/`implements`** on the trait declaration.
4. **Checker unchanged** — 4044/4045 remain; this is a PHP-side net, not a substitute.

### Risks & Edge Cases

- Signature mismatch vs parent: PHP fatal if the abstract is incompatible with `Entity::getId()`. Emit the
  target’s signature, not a guessed one.
- Overloaded / generic methods: emit the collapsed PHP signature.
- Trait already has a same-named concrete method: skip.
- Two required types contributing the same name: one abstract; signatures must be compatible or skip and
  rely on the checker.
- `final` methods on the parent: parent still satisfies the trait abstract (verify in fixtures).
- Visibility: copy the target member’s visibility (`protected` stays `protected`).

### Golden Fixtures / Tests (Acceptance)

- `Cacheable` example: abstracts for `getId` and `serialize` only; no `unserialize`; no `extends`/`implements`
  on the trait.
- Using class that extends `Entity` + implements `Serializable` still emits/runs (parent satisfies abstracts).
- Trait that never calls into the requirement emits no extra abstracts.
- Property-only use of the required type does not add a colliding trait property.
- Existing trait-requirement checker tests (4044/4045) still pass.

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
