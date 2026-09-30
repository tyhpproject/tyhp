# Implementation Plan: Story 21.12

> **Roadmap position:** Story 21.12 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story **21.11**, before Stories **27** / **27.1** / **27.2** / **27.3**, then deferred Story **22**). **Implement after 21.11.**
> **Direct dependencies (new numbering):** 04 (`tyhp/core` attributes), 06 (lexer / ctor grammar — G, H), 07 (conformance runner — D; CI workflow already exists), 08 (checker, Unresolved, attributes), 09 / 11 (`is` lowering, ctor emit), 20 (Track C stamp), **21.9** (`aliasFactory` — do not change that path)
> **Bucket story:** 21.12 collects independent post-21.11 workstreams. Workstreams do not share a single theme. Implement (or skip) each one on its own; adding a later workstream must not force a rewrite of earlier ones.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-09-15
> **Last design lock:** 2026-09-15 — Workstream A locked (generic full-erase + `Generic::bind`). Workstreams B–E, G, H locked the same day from `LANGUAGE_ASSESSMENT.md` follow-up (B redesigned to `#[\Tyhp\NativeTypeTest]`). Workstream F withdrawn: `.github/workflows/tests.yml` already runs `dotnet test`.
> **Status:** **Workstreams A–E, G, H design locked. F withdrawn.** Do **not** implement until this story is opened for implementation. Other workstreams may still be appended.
> **Prerequisites:** Mechanism C / D emit and Track C `#[\Tyhp\GenericRuntime]` stamping already exist (Stories 11 / 20). Workstream A rewrites that ABI; it does not depend on 21.11’s workstreams.
> **Consumers:** compiled Tyhp libraries and their PHP callers; Tyhp projects that `new` / call generic declarations from a vendor `package.tyhpdef`; `\Tyhp\Generic::bind`; runtime package rebuilds (`tyhp/core`, `tyhp/async`, …); Story 30 docs.

**Greenfield:** Tyhp is pre-release. Do not keep a dual emit path, a deprecation window, or “legacy factory-name consumer emit.” Same-compilation sites use factories/binders when tracked; **every** foreign compiled-library generic site uses `\Tyhp\Generic::bind`. Hand-written runtime tests (`GenericTest.php` and similar) are updated to the new attribute, not preserved as a second protocol.

---

## How to extend this story

This file is a **bucket**. Unrelated work belongs here as a new **Workstream**, not as extra phases stuffed into Workstream A.

To add work later, before implementation:

1. Add a row to [Workstream inventory](#workstream-inventory) (`B`, `C`, …). Do **not** renumber existing letters.
2. Add a ToC link.
3. Paste a new top-level `## Workstream X — <title>` section using the [Workstream template](#workstream-template). Fill summary, decisions, phases (`X.1`, `X.2`, …), and acceptance tests inside that section.
4. Keep inner phase numbers local to that workstream (`A.1` stays `A.1` even if Workstream B is inserted in the inventory above it).
5. Shared closing sections ([Cross-Story References](#cross-story-references), repo-wide notes) may gain a bullet; do not merge two workstreams’ phase lists.

Do not fold unrelated tasks into Workstream A’s phases.

---

## Table of Contents

- [How to extend this story](#how-to-extend-this-story)
- [Workstream inventory](#workstream-inventory)
- [Workstream template](#workstream-template)
- [Workstream A — Generic full-erase and `Generic::bind` interop](#workstream-a--generic-full-erase-and-genericbind-interop)
  - [A. Summary](#a-summary)
  - [A. Motivation](#a-motivation)
  - [A. What this is not](#a-what-this-is-not)
  - [A. What already exists](#a-what-already-exists)
  - [A. Scope (In / Out)](#a-scope-in--out)
  - [A. Decisions (locked)](#a-decisions-locked)
  - [A. Runtime-use vs property tracking](#a-runtime-use-vs-property-tracking)
  - [A. `#[\Tyhp\EraseGeneric]`](#a-tyhperasegeneric)
  - [A. `#[\Tyhp\GenericRuntime]`](#a-tyhpgenericruntime)
  - [A. Emit dispatch](#a-emit-dispatch)
  - [A. `\Tyhp\Generic::bind`](#a-tyhpgenericbind)
  - [A. Phases](#a-phases)
  - [A. Golden fixtures / tests](#a-golden-fixtures--tests)
- [Workstream B — `#[\Tyhp\NativeTypeTest]` (tyhpdef-driven `is` emit)](#workstream-b--tyhpnativetypetest-tyhpdef-driven-is-emit)
- [Workstream C — Unresolved receivers must not silently satisfy member access](#workstream-c--unresolved-receivers-must-not-silently-satisfy-member-access)
- [Workstream D — Emit-and-run beaten-path harness](#workstream-d--emit-and-run-beaten-path-harness)
- [Workstream E — Honest emit docs, inference-first locals, “typed superset”](#workstream-e--honest-emit-docs-inference-first-locals-typed-superset)
- [Workstream F — withdrawn (CI already exists)](#workstream-f--withdrawn-ci-already-exists)
- [Workstream G — Remove `isa` / `isan` / `is_a` / `is_an`](#workstream-g--remove-isa--isan--is_a--is_an)
- [Workstream H — Optional constructor `: void`](#workstream-h--optional-constructor--void)
- [Cross-Story References](#cross-story-references)

---

## Workstream inventory

| ID | Title | Status | Notes |
|----|-------|--------|--------|
| **A** | Generic full-erase when `T` is unused at runtime; PHP + tyhpdef `GenericRuntime`; foreign sites use `Generic::bind` | **Design locked** | Greenfield. Rebuild `runtime/packages/*`. Author opt-in `#[EraseGeneric]` for properties. |
| **B** | `#[\Tyhp\NativeTypeTest]` on tyhpdef type-guards; `$x is T` emits that function | **Design locked** | Not a hardcoded `is_string` table. Independent of A. |
| **C** | Unresolved receivers must diagnose member access, not emit as if typed | **Design locked** | FOUND #56. Independent of A. |
| **D** | Emit-and-run beaten-path harness (compile + `php`, fail on warnings) | **Design locked** | First slice is curated, not a random fuzzer. |
| **E** | Honest emit docs; locals lead with inference; public tagline “typed superset”; `with` kept | **Design locked** | Docs. Generics erase pages stay on A.8. |
| **F** | Public GitHub Actions CI | **Withdrawn** | `.github/workflows/tests.yml` already runs `dotnet test`. Remaining Phase 10 polish (PHPUnit job, coverage, multi-OS) is `ALPHA_RELEASE.md`, not this story. Do not renumber G/H. |
| **G** | Remove `isa` / `isan` / `is_a` / `is_an` (keep `is` + `instanceof`) | **Design locked** | Greenfield; no deprecation window. |
| **H** | Omitted constructor return type means `: void`; `: parent(...)` stays | **Design locked** | Greenfield grammar. Independent of G. |
| **I+** | *(add rows here)* | Not started | Independent of A–H. Use the template below. |

---

## Workstream template

Copy this block when adding Workstream I (then J, …). Replace `X` with the letter.

```markdown
## Workstream X — <short title>

> **Status:** Draft / Design locked
> **Depends on:** <21.11, Workstream A, nothing, …>

### X. Summary

<what and why, current contract only>

### X. Decisions (locked)

| Topic | Decision |
|-------|----------|
| | |

### X. Phases

#### X.1 — <name>

- [ ] …

#### X.2 — Documentation and AIDevGuide

Per `CONVENTIONS.md` §10. Name the pages. Write "no change required" if that is true.

### X. Golden fixtures / tests

- [ ] …
```

---

## Workstream A — Generic full-erase and `Generic::bind` interop

> **Status:** **Design locked.**
> **Depends on:** 04, 08, 09 / 11, 20, 21.9 (`aliasFactory` unchanged).

### A. Summary

When a generic class, method, or function never needs the bound type at runtime, emitted PHP is a normal class / function: no `HasGenerics`, no `__initGenerics__tyhpGeneric`, no `__tyhpGeneric` binder, no factory. Type hints of `T` still erase to the constraint or `mixed` (`TypeSpellingHelper`).

Generic-typed **properties** still opt the enclosing class into Mechanism C **by default** (set checks / `setPropertyType`). Authors opt out with `#[\Tyhp\EraseGeneric]` on a property or on the class (all generic-typed instance properties, including promoted and trait-flattened). `typeof(T)` and the other runtime-use constructs still force Mechanism C; mixed mode is allowed (class tracked, properties erased).

Compiled-library tyhpdefs and the emitted PHP both carry `#[\Tyhp\GenericRuntime]` on every emitted generic class, method, and function (erased or not). The attribute is a real PHP attribute (`NoEmit` dropped). `factory` / `binder` are present only when helpers were emitted. `erased` and `layouts` tell `\Tyhp\Generic::bind` what to do. PHP builtins / overlay tyhpdefs that omit the stamp stay erase / inline.

Tyhp **same-compilation** sites keep today’s direct factory / binder / plain `new`. Tyhp **foreign** compiled-library sites always emit `\Tyhp\Generic::bind(...)(...)`. PHP callers use that same spelling. `bind()` does not construct early; `__invoke` (and `new()` as an alias) runs the factory or a plain `new`.

### A. Motivation

Today’s stamp is omitted when helpers are absent, so consumers emit plain `new` / plain calls. If a later compile of that library starts using `T` at runtime, unrecompiled (and, without a stamp, even recompiled-against-an-old-tyhpdef) sites never bind types. Stamping a factory name that was not emitted makes consumers call a missing method.

Stamping `GenericRuntime` always, without baking helper names into consumer PHP, and routing foreign sites through `Generic::bind` keeps one call shape whether the library is erased or tracked.

### A. What this is not

- Not declaration-site generic variance (`DESIGN_OPEN_QUESTIONS.md`).
- Not changing `aliasFactory` / type-alias factories (Story 21.9). `typeof(Alias)` still calls the alias factory; it does not go through `Generic::bind`.
- Not routing compiler-owned machinery through `Generic::bind` (property-hook `register__tyhpGeneric`, `HasGenerics` injection, `PropertyAccessor`).
- Not wrapping `await` / `_async` desugar in `Generic::bind`. Those stay on the `Promise::_await` / `_async` **wrappers**.
- Not stamping interfaces or abstract methods.
- Not a one-shot `Generic::construct`. The PHP interop spelling is `bind(...)(...)`.
- Not serializing constraints or defaults onto the attribute.
- Not a compatibility shim for consumer PHP that called `new_<Mangled>__tyhpGeneric` by name.

### A. What already exists

- Checker: `RequiresRuntimeGenericTracking`, `RequiresGenericVariant`, `UsesGenericAtRuntime`, `PropagateGenericVariantAcrossHierarchies`, `TypeInvolvesGenericsForTracking` (properties).
- Emitter: skip `HasGenerics` / factory when a class is untracked; skip the Mechanism D binder when a callable does not use its own generics at runtime; call-site `new Pair(1, 'x')` for untracked classes; `callee__tyhpGeneric(types…)(values…)` when flagged.
- `#[\Tyhp\GenericRuntime]` is `#[\Tyhp\NoEmit]` on the **class**, so it does not exist in vendor PHP. Stamping is skipped unless `binder` / `factory` / `aliasFactory` is set. Consumer emit keys off `HasFactory` / `HasBinder`.
- `\Tyhp\Generic::bind` looks for `__initGenerics__tyhpGeneric` / `name__tyhpGeneric` by convention, materializes class instances before `new()`, and does not read layouts. `ReflectionClass` is cached; methods and attributes are not.

### A. Scope (In / Out)

**In**

- `EraseGeneric` author attribute; property-tracking default; mixed mode with `typeof`.
- Full erase of classes/methods/functions with no remaining runtime use.
- `GenericRuntime`: drop `NoEmit`; add `erased` + `layouts`; stamp on PHP and tyhpdef for every emitted generic class/method/function.
- Consumer / PHP interop via `Generic::bind`; same-compilation factories/binders unchanged.
- `Generic::bind` protocol, caches, layout errors, no early construct.
- Rebuild runtime packages so `dist/` PHP and `package.tyhpdef` match.
- Docs / AIDevGuide / `REGEN.md` / diagnostics reference.

**Out**

- Interfaces / abstracts stamps.
- Inverse “keep tracking on this one property” attribute.
- Constraint/default payload on `GenericRuntime`.
- Changing PHP builtin / overlay generics (still no stamp).

### A. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Greenfield | No dual protocol. Update fixtures; do not preserve hook-only `Generic::bind`. |
| Same-compilation tracked | Direct factory / `__tyhpGeneric` curry. |
| Same-compilation erased | Plain `new` / plain call. |
| Foreign compiled library (`GenericRuntime` on the tyhpdef symbol) | Always `Generic::bind`, erased or not. Never bake `factory` / `binder` names into consumer PHP. |
| PHP builtin / overlay (no stamp) | Erase / inline, as today. |
| Compiler-owned sites | Direct helpers. Not `Generic::bind`. |
| `await` / `_async` desugar | Wrapper names, not `Generic::bind`. |
| Properties | Mechanism C by default. `#[EraseGeneric]` opts a property (or all properties on a class) out of property tracking. |
| `typeof` / `default` / `is` / `new T` / forwarded type args | Always tracking. Cannot opt out. Mixed mode with erased properties is allowed. |
| Class fully erased | Consequence of: no runtime-use constructs **and** no remaining generic-typed properties that were not opted out. |
| `runtimeGenericChecks` vs `EraseGeneric` | Erase attribute wins for those properties. |
| Inheritance of `EraseGeneric` | Not inherited. Each class opts in. Tracked child of erased generic parent does not `parent::__initGenerics__`. |
| Trait flatten | Class-level `EraseGeneric` applies to generic-typed instance properties that end up on that class, including from traits. |
| Author `#[GenericRuntime]` | Checker **warning**. Compiler restamps; authors do not write it. |
| `EraseGeneric` on method/function | Checker **error**. |
| `EraseGeneric` itself | `#[NoEmit]`. Compile hint only. |
| `GenericRuntime` itself | Drop `NoEmit`. Real PHP attribute. Same class on tyhpdef and emitted PHP. |
| Stamp targets | Every **emitted** generic class, method, and function (erased or not). Not interfaces / abstracts. |
| `factory` / `binder` on the stamp | Only when helpers exist (`erased: false`). |
| `aliasFactory` | Unchanged (21.9). Not `Generic::bind`. |
| `layouts` | `list<int>`. First ship `[1]`. A written `layout: 1` is sugar for `[1]` when **reading**. Stamps write `layouts` only. |
| Layout pick | Highest layout `Generic::bind` implements that the declaration lists. Empty intersection → runtime exception (include `compiler`). Compiler TYHP5023 if the tyhpdef’s layouts have no intersection with this compiler. |
| `bind()` construct | Do not materialize an instance in `bind()`. `__invoke` / `new()` call the named factory (types then ctor args) or `new $class(...)`. |
| PHP interop spelling | `$obj = Generic::bind(Foo::class, Type::string())("asdf");` and `$r = Generic::bind(combine(...), Type::string())("asdf", "blah");`. Keep `new()` as `__invoke` alias; document `bind(...)(...)` as the spelling. |
| One-shot helper | No. |
| Constraints/defaults on the attribute | No. Names/arity are not required either; extras are ignored when erased. |
| `@template` PHPDoc | Still emitted on erased generic classes. |
| Caches in `Generic` | `ReflectionClass`, `ReflectionMethod` (factory/binder/hook as used), parsed `GenericRuntime` per class/method/function. |

### A. Runtime-use vs property tracking

**Runtime use** (always Mechanism C on the enclosing class, or Mechanism D on the callable) — same scan as today, plus hierarchy union for D:

- `typeof(T)` / `default(T)`
- `instanceof T` / `is T` (and aliases)
- `new T()`
- Type arguments forwarded into another generic `new` / call (`new Foo<T>()`, `decode<T>()`)

Type hints of `T` on parameters and returns do **not** count.

**Property tracking** (Mechanism C on the enclosing class, independently of the scan above):

- A property (including promoted) whose type involves a class generic parameter or a generic application (`T`, `?T`, `Cell<T>`, `\Closure<…>`), unless that property is opted out with `EraseGeneric`.

Free `T` / `?T` properties keep today’s always-on set checks when not opted out. Parameterized properties keep `setPropertyType` registration when not opted out.

### A. `#[\Tyhp\EraseGeneric]`

New runtime-package class, compile-time only:

```tyhp
namespace Tyhp;

#[\Attribute(\Attribute::TARGET_CLASS | \Attribute::TARGET_PROPERTY | \Attribute::TARGET_PARAMETER)]
#[\Tyhp\NoEmit]
final class EraseGeneric
{
}
```

| Target | Meaning |
|--------|---------|
| Property or promoted parameter | This slot does not trigger or receive Mechanism C property tracking. PHP hint is the constraint or `mixed`. |
| Class | Same for every generic-typed instance property on that class after trait flatten. |
| Method / function / anything else | Error. |

No inverse attribute in this workstream. If one property must stay checked, do not put the class-level attribute; mark the others.

### A. `#[\Tyhp\GenericRuntime]`

Drop `#[\Tyhp\NoEmit]` from the class. Usages **are** emitted onto PHP. Constructor (Tyhp source):

- `bool $erased = false`
- `?string $binder = null`
- `?string $factory = null`
- `?string $aliasFactory = null`
- `array<int, int> $layouts = [1]` (declaration order; treat as `list<int>`)
- `?string $compiler = null`

**Read path:** if `layout:` is present and `layouts:` is omitted, treat as `layouts: [<that int>]`. If both are present, `layouts` wins.

**Stamp path (Track C + class emit):** always include `layouts: [1]` and `compiler:` (compiler version string, as today).

| Host | `erased` | Helper names |
|------|----------|--------------|
| Generic class, fully erased | `true` | omit `factory` |
| Generic class, tracked | `false` | `factory: "new_<MangledFqn>__tyhpGeneric"` |
| Generic method/function, erased | `true` | omit `binder` |
| Generic method/function, tracked | `false` | `binder:` clean companion name (`zero__tyhpGeneric` or `Backer::jsonDecodeAs__tyhpGeneric` as today) |
| Type alias with factory | omit / `false` | `aliasFactory:` as today |
| Type alias without factory | do not stamp | — |
| Interface / abstract method | do not stamp | — |
| PHP builtin / overlay generic | do not stamp | — |

`TryRead` / `StampGenericRuntime` must succeed for `#[GenericRuntime(erased: true, layouts: [1])]` with no helper names. Current “no binder/factory/aliasFactory → do not stamp / do not parse” is wrong.

Author-written `GenericRuntime` on source: warning; generator/emitter overwrite with the computed stamp.

### A. Emit dispatch

Do **not** overload `RequiresRuntimeGenericTrackingFor` / `RequiresGenericVariantFor` so that “has `GenericRuntime`” also means “emit `HasGenerics` on this class body.” Split the questions:

| Question | True when |
|----------|-----------|
| Emit Mechanism C plumbing on **this** class body | Checker tracking set (runtime use and/or non-erased generic-typed properties), plus today’s parent-chain rule |
| Emit Mechanism D binder on **this** callable | Checker `RequiresGenericVariant` (including hierarchy union) |
| Rewrite `new Foo<T>(…)` / `foo<T>(…)` to **local** factory / binder | Target is a source symbol in this compilation and the local plumbing exists |
| Rewrite to **`Generic::bind`** | Target symbol carries `GenericRuntime` from a tyhpdef (class with generic params, or callable with generic params), erased or not |
| Plain `new` / plain call | Local erased, **or** generic tyhpdef **without** `GenericRuntime` (PHP builtins / overlays) |

Same-compilation `new self<T>` / `new static` inside a tracked class still uses that class’s factory.

Foreign example (tracked or erased — same consumer PHP):

```php
$box = \Tyhp\Generic::bind(\Vendor\Box::class, \Tyhp\Type::int())(42);
$result = \Tyhp\Generic::bind(\Vendor\Holder::identity(...), \Tyhp\Type::string())($value);
```

First-class callables for instance methods, static methods, functions, and extension backers (`Backer::name(...)`). Do not wrap with an extra `...$args` Closure.

### A. `\Tyhp\Generic::bind`

**Classes**

1. Resolve `ReflectionClass` (cached).
2. Read `GenericRuntime` (cached). Missing → `InvalidTypeException` (not a Tyhp generic).
3. Pick layout (cached supported list, currently `{1}`). None in common → exception mentioning `layouts` and `compiler`.
4. Store class name, type arguments, erased flag, factory name. **No instance.**
5. `__invoke` / `new()`: spent-guard; then either `new $className(...$ctorArgs)` or `$className::$factory(...$typeArguments, ...$ctorArgs)`.

**Callables**

1. Resolve named function/method as today (FCC, `[obj, 'method']`, `'Foo::bar'`, function name). Anonymous Closure still rejected.
2. Read `GenericRuntime` on that method/function (cached). Missing binder **and** `erased !== true` → `InvalidTypeException`.
3. Layout check, same as classes.
4. `erased: true` → `Closure::fromCallable(...)` (by-ref preserved). Type arguments ignored.
5. Else invoke the named binder with type arguments only; return that `\Closure` unchanged.

`PlainBox`-shaped classes (no attribute) still throw. Hand-written `GenericBox` fixtures gain `GenericRuntime`.

Update the class docblock: Tyhp call sites **do** use `Generic::bind` for **foreign** compiled libraries; same-compilation still uses factories.

### A. Phases

#### A.1 — `GenericRuntime` + `EraseGeneric` in `tyhp/core`

- [x] Drop `#[\Tyhp\NoEmit]` from `GenericRuntime`. Add `erased`, `layouts`; keep `binder` / `factory` / `aliasFactory` / `compiler`.
- [x] Binder `GenericRuntimeInfo` / `GenericRuntimeAttributeSupport`: parse `erased`, `layouts`, `layout` sugar; `TryRead` true without helper names when `layouts` / `erased` / `layout` is present.
- [x] Add `EraseGeneric` (`NoEmit`, targets class / property / parameter).
- [x] Checker: `EraseGeneric` illegal target → error; author `GenericRuntime` → warning. Allocate codes in `MessageCode.cs` only.

#### A.2 — Checker tracking

- [x] Property tracking remains the default (`TypeInvolvesGenericsForTracking`).
- [x] Skip that mark when the property or its enclosing class has `EraseGeneric`.
- [x] Runtime-use scan unchanged; mixed mode (typeof + erased properties) still marks the class.
- [x] Class-level `EraseGeneric` sees trait-flattened instance properties.
- [x] `EraseGeneric` is not inherited.

#### A.3 — Library emit (same compilation)

- [x] Untracked class (no runtime use, all generic-typed properties opted out or absent): no trait, no init hook, no factory, no ctor gate. PHP hints already constraint/`mixed`.
- [x] Tracked class: today’s Mechanism C, including property set checks only for properties **not** opted out.
- [x] Callable without runtime use: one PHP symbol; no binder.
- [x] Stamp `GenericRuntime` on the emitted PHP class/method/function per [A. `#[\Tyhp\GenericRuntime]`](#a-tyhpgenericruntime).
- [x] Keep `@template` PHPDoc on erased generic classes.
- [x] Do not emit `EraseGeneric` (NoEmit).

#### A.4 — Track C tyhpdef stamp

- [x] Always stamp generic classes/methods/functions that were emitted, including `erased: true` with no `factory`/`binder`.
- [x] Do not stamp interfaces / abstracts.
- [x] `aliasFactory` path unchanged.
- [x] `StampGenericRuntime` no longer requires a helper name.

#### A.5 — Consumer emit

- [x] Split “emit plumbing on this body” from “foreign `Generic::bind`.”
- [x] `new Vendor\Box<int>(42)` → `Generic::bind(\Vendor\Box::class, Type::int())(42)` whenever the tyhpdef has `GenericRuntime`.
- [x] Explicit type-arg calls to foreign generic callables → `Generic::bind(<fcc>, ...types)(...args)`.
- [x] Calls **without** type args stay on the wrapper / plain name (today’s rule).
- [x] TYHP5023: no intersection between declaration `layouts` and this compiler’s supported layouts.
- [x] Overlay / PHP builtin generics without the stamp: still erase.

#### A.6 — `Generic::bind` runtime

- [x] Attribute + layout protocol; caches as locked.
- [x] No early construct; `__invoke` ≡ `new()`.
- [x] Erased class / callable paths; tracked factory / binder paths.
- [x] Unknown layout / missing attribute exceptions.
- [x] Update `runtime/packages/core/tests/GenericTest.php` (and any hook-only fixtures).

#### A.7 — Rebuild runtime packages

- [x] Re-emit `runtime/packages/*` so `dist/` PHP and `package.tyhpdef` carry the new stamps (e.g. `Promise` keeps `erased: false` + factory/binder; identity-style APIs may erase). Use `./runtime/packages/base-build-all.sh`.  All `tyhpdef/*` packages will not need to be updated because non of them contain Tyhp code (they have Tyhpdef only).
- [x] Do not git-restore generated `src/`.

#### A.8 — Documentation and AIDevGuide

Per `CONVENTIONS.md` §10.

| Page | Change |
|------|--------|
| `docs/content/tyhp_0500_generics.md` | Erase vs tracking; `EraseGeneric`; foreign `Generic::bind`; stamp always present. |
| `docs/content/tyhp_2500_phpMagicMethods.md` | `HasGenerics` only when tracked; mixed mode. |
| `docs/content/tyhp_2700_compileTimeConstructs.md` | Generics: erase by default at PHP, tracking when needed; `Generic::bind` for PHP / foreign. |
| `docs/content/cli_tyhpdefGeneration.md` | Stamp every emitted generic class/method/function; `erased` / `layouts`; `NoEmit` no longer on `GenericRuntime`. |
| `docs/content/cli_build.md` | Consumers use `Generic::bind`, not helper names. |
| `docs/content/cli_interopContract.md` | `GenericRuntime` is runtime-visible; `EraseGeneric` is compile-only. |
| `docs/content/faq_general.md` / `faq_tyhpSyntax.md` | Same contract. |
| `docs/content/tyhp_2100_extensions.md` / `tyhpdef_extensions.md` | Foreign binder sites via `Generic::bind`; splice still when **no** stamp. |
| `docs/content/tyhp_0700_typeAliases.md` / `tyhpdef_typeAliases.md` / `tyhp_0350_useStatements.md` | `aliasFactory` unchanged; do not imply class/method stamps require helpers. |
| `docs/content/diagnostics_reference.md` | New warning/error; TYHP5023 layouts intersection. |
| `AIDevGuide/guide/07-generics.md` | Erase, `EraseGeneric`, `Generic::bind` spelling. |
| `AIDevGuide/guide/23-tyhpdef.md` | `GenericRuntime` is emitted to PHP; not `NoEmit`. |
| `AIDevGuide/guide/12-extensions.md` / `09-type-aliases.md` / `26-build-cli.md` / `25-runtime-packages.md` / `29-php-mapping.md` | Stamp / bind vs splice / alias factory. |
| `AIDevGuide/handbook/05-php-interop.md` | PHP spelling `bind(...)(...)`. |
| `AIDevGuide/handbook/07-runtime-api.md` | `GenericRuntime` / `Generic::bind` / `EraseGeneric` signatures. |
| `AIDevGuide/handbook/03-build-cli-workflow.md` | Generated tyhpdef stamps. |
| `AIDevGuide/QUICK_GUIDE.md` | One-line generics / tyhpdef claims. |
| `AIDevGuide/REGEN.md` | Replace “stamp when helpers were emitted / consumers call those helpers / GenericRuntime is NoEmit” with the locked contract. |

Clear stale claims: “consumers call compiled helpers by name,” “GenericRuntime is NoEmit,” “stamp only when helpers exist,” “Tyhp call sites never use `Generic::bind`.”

- [x] Updated the pages in the table; cleared the stale claims.

### A. Golden fixtures / tests

- [x] Emitter: `identity<T>` method — no binder; `GenericRuntime(erased: true, layouts: [1])` on the PHP method.
- [x] Emitter: `Box<T> { public T $value; }` — Mechanism C (factory, set checks).
- [x] Emitter: same `Box` with class-level `EraseGeneric` — fully erased; attribute `erased: true`; `@template` still present.
- [x] Emitter: `typeof(T)` + class-level `EraseGeneric` — tracked class, no property set checks (mixed mode).
- [x] Emitter: same-compilation `new Box<int>(…)` tracked → factory; erased → `new Box(…)`.
- [x] Emitter: consumer fixture whose callee comes from a tyhpdef with `GenericRuntime` → `Generic::bind`, never `new_…__tyhpGeneric` / `foo__tyhpGeneric`.
- [x] Emitter: `Iterator<T>`-style overlay without stamp → still plain PHP, not `Generic::bind`.
- [x] Checker: `EraseGeneric` on a method → error; author `GenericRuntime` → warning.
- [x] Track C: `package.tyhpdef` always has `GenericRuntime` on emitted generic classes/methods/functions; helpers omitted iff `erased: true`.
- [x] PHPUnit: `Generic::bind` erased class `__invoke`; tracked class calls factory; spent binder; missing attribute throws; unknown `layouts: [99]` throws; erased callable by-ref; `new()` ≡ `__invoke`.
- [x] Conformance goldens under `tests/conformance/story15/generics-erasure/` (and library stamps) updated for the new attribute / bind consumer shape.

---

## Workstream B — `#[\Tyhp\NativeTypeTest]` (tyhpdef-driven `is` emit)

> **Status:** **Design locked.**
> **Depends on:** Story 04 (`tyhp/core` attributes), Story 08 (type-guard returns), Story 09/11 (`is` emit). Independent of A.
> **Source:** `LANGUAGE_ASSESSMENT.md` §4.2 / §6.3. Author lock 2026-09-15: do **not** hard-code `\is_string`; declare the mapping on the tyhpdef.

### B. Summary

`$x is string` should emit `\is_string($x)` because **the `is_string` tyhpdef says so**, not because the emitter has a builtin table. The existing type-guard return (`: $value is string`) already means “this function tests for `string`.” An extra compile-only attribute picks **which** of those guards is the native lowering for `$x is T` (so `is_integer` / `is_long` stay aliases for calls, and `is_numeric` is not used for `$x is int`).

User and library tyhpdefs can mark their own guards the same way (`isPositiveInt` → `$x is PositiveInt`).

Resolved class / interface / enum / trait RHS with **no** `NativeTypeTest` still emit native `$x instanceof Fqn`. That is PHP’s operator, not a stdlib function. Unions, nullables, structs, generic params, and unmarked types stay `\Tyhp\Type::is`.

### B. `#[\Tyhp\NativeTypeTest]`

New `tyhp/core` class, compile-time only:

```tyhp
namespace Tyhp;

#[\Attribute(\Attribute::TARGET_FUNCTION)]
#[\Tyhp\NoEmit]
final class NativeTypeTest
{
}
```

T is **not** repeated on the attribute. It is the type-guard return of the same function:

```tyhp
#[\Tyhp\NativeTypeTest]
function is_string(mixed $value): $value is string;
```

| Rule | Detail |
|------|--------|
| Target | Free functions only in this workstream. Methods / static methods → error. |
| Must have | Type-guard return `$param is T` whose `$param` is the first parameter. |
| `T` | A single type: not a union, intersection, or nullable. Transparent aliases expand before lookup (`type UserId = string` → `is_string`). |
| Extra parameters | Allowed only if they all have defaults (so emit is `fqn($x)`). |
| Duplicate `T` | Checker error. Canonical `is_int` is marked; `is_integer` / `is_long` are not. |
| Author attribute on Tyhp source | Allowed (same rules). The attribute is `NoEmit` (never appears in PHP). |
| Unmarked guards | Still narrow when **called**. They do not win `$x is T` emit. |

v1 overlay marks (Layer 3 `Ext.Standard.tyhpdef`): `is_string`, `is_int`, `is_float`, `is_bool`, `is_array`, `is_object`, `is_null`, `is_resource`, and the `is_callable` overload whose extra parameters are defaulted. Do **not** mark `is_numeric`, `is_scalar`, `is_countable`, `is_iterable` (union guards), or the `is_callable(..., true)` overload.

### B. Emit dispatch

For `$x is T` / `$x instanceof T` (after alias expansion of `T`):

1. If a `NativeTypeTest` function is registered for exact `T` → emit `\Fqn($x)` (current namespace / import rules as for any global call).
2. Else if `T` is a resolved class / interface / enum / trait (not a generic param) → `$x instanceof Fqn`.
3. Else → `\Tyhp\Type::is($x, <typeof(T)>)` (today’s path: unions, `?T`, structs, generic params, unmarked aliases).

Checker / narrowing unchanged. This is emit + attribute validation.

### B. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Hard-coded `string` → `\is_string` table | **No.** Attribute on the tyhpdef. |
| `instanceof` for unmarked object types | Yes. Language operator, not a function. |
| Nullable / union composition (`$x === null \|\| \is_string($x)`) | Out of this workstream. Stay on `Type::is`. |
| `$x is null` | Allowed; overlay marks `is_null`. Update docs that currently forbid `$x is null`. |
| `isa` / `isan` | Workstream G removes them. Until G lands, they lower like `is`. |

### B. Phases

#### B.1 — Attribute + checker

- [x] Add `NativeTypeTest` in `tyhp/core` (`NoEmit`, `TARGET_FUNCTION`).
- [x] Binder/checker: validate the table above; allocate diagnostic codes in `MessageCode.cs` only.
- [x] Index `T` → function symbol for the emitter (one map per compilation).

#### B.2 — Emitter + overlay

- [x] `$x is T` uses the dispatch above.
- [x] Stamp `#[\Tyhp\NativeTypeTest]` on the v1 overlay list. Leave `is_integer` / `is_long` / `is_double` unmarked.

#### B.3 — Documentation and AIDevGuide

| Page | Change |
|------|--------|
| `docs/content/tyhp_0200_typeNarrowingAndGuards.md` | `is` emit follows `NativeTypeTest`; `$x is null` ok; drop “scalars always `Type::is`”. |
| `docs/content/tyhpdef_functions.md` (or overlays page) | How to mark a guard. |
| `docs/content/cli_interopContract.md` | Attribute is compile-only (`NoEmit`). |
| `README.md` | Hero `$value is string` example emits `\is_string($value)`. |
| `AIDevGuide/guide/16-type-guards.md` / `25-runtime-packages.md` | Same contract. |
| `AIDevGuide/REGEN.md` | `is` lowering is tyhpdef-driven. |

### B. Golden fixtures / tests

- [x] Overlay-marked `is_string` / `is_int` / `is_null`: `$x is string` emits `\is_string($x)`, not `Type::is`.
- [x] `$x is int` emits `\is_int`, never `\is_integer` / `\is_long`.
- [x] Unmarked user `function isMoney(mixed $v): $v is Money` does **not** change `$x is Money` (struct still `Type::is`; class still `instanceof`).
- [x] Marked user guard: `$x is PositiveInt` emits `\isPositiveInt($x)`.
- [x] Duplicate `NativeTypeTest` for `string` → error.
- [x] Attribute on a method / on `is_numeric` (union guard) → error.
- [x] `$x is User` (class, unmarked) emits `instanceof`.
- [x] `$x is ?string`, `$x is int\|string`, `$x is T` still `Type::is`.
- [x] Existing `NullableInstanceofEmitterTests` / `TypeGuardRuleTests` stay green.

---

## Workstream C — Unresolved receivers must not silently satisfy member access

> **Status:** **Design locked.**
> **Depends on:** Story 08 (member access / `mixed` use-site). Independent of A.
> **Source:** `LANGUAGE_ASSESSMENT.md` §6.1. FOUND_BUGS #56.

### C. Summary

`UnresolvedCheckedType` is assignable to and from everything **on purpose**, so one missing name does not cascade. That recovery must not also mean "this receiver has every member, and emit may rewrite `->` as if it were a struct/object." If the checker cannot type a receiver, member access is a diagnostic. If a diagnostic already exists on that expression, do not pile on.

Do **not** change global `Unresolved` assignability.

### C. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Property read/write, method call, extension lookup on an `Unresolved` receiver | Diagnostic (new checker code; allocate in `MessageCode.cs` per `CONVENTIONS.md`). |
| Already-diagnosed subtree (undefined function, undefined name, …) | Suppress this diagnostic (one report per failure). |
| `mixed` receiver | Unchanged: TYHP4160 / existing mixed use-site rules. `Unresolved` is not `mixed`. |
| Emit | Do not rewrite `->` to `[]` or splice an extension for an `Unresolved` receiver. |
| Assignability `Unresolved` ↔ `T` | Unchanged. |
| Indexing `$xs[$k]` when `$xs` is `Unresolved` | Same rule as member access (diagnose unless already diagnosed). |

### C. Phases

#### C.1 — Checker

- [x] Member-access / call / index paths: if receiver kind is `Unresolved` and the subtree has no prior error, report.
- [x] Tests: inferred-untyped foreach-shaped hole (even if #44 stays fixed, a synthetic `Unresolved` receiver still errors); cascade from `unknownFn()->foo` is a single diagnostic.

#### C.2 — Documentation and AIDevGuide

- [x] `docs/content/diagnostics_reference.md` for the new code.
- [x] Checker technical guide: `Unresolved` recovery vs member-access.

### C. Golden fixtures / tests

- [x] `unknownFn()->x` reports the undefined function; no second "unresolved receiver" if suppressed.
- [x] A receiver that is `Unresolved` with **no** prior diagnostic (test helper / fixture) reports the new code and does not emit a property read.
- [x] `mixed $m; $m->x` still uses the mixed rule, not this one.

FOUND_BUGS #56 is addressed by this workstream (diagnostic TYHP4197 / `CheckerUnresolvedReceiver`).

---

## Workstream D — Emit-and-run beaten-path harness

> **Status:** **Design locked.**
> **Depends on:** Story 07 conformance runner. Independent of A.
> **Source:** `LANGUAGE_ASSESSMENT.md` §6.1. v1 is **not** a random program generator.

### D. Summary

A program that type-checks with zero errors and then warns or fatals under PHP is a Critical silent wrong-emit. v1 is a **curated corpus** on the beaten path. Random / generative fuzzing is out (`DESIGN_OPEN_QUESTIONS.md` §18 stays “don’t”).

### D. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Input | `.tyhp` fixtures that the checker accepts with 0 errors (warnings allowed only if listed). |
| Run | `php -d error_reporting=-1 -d display_errors=1` on emitted PHP. |
| Fail | Non-zero exit, or stderr matching Warning / Notice / Deprecated / Fatal / Parse error. |
| Pass | Exit 0 and empty stderr (or stdout-only expected output when the fixture declares it). |
| Location | New conformance tree, e.g. `tests/conformance/emit-and-run/`, not mixed into golden-PHP-only story15 dirs. |
| Seed | Regression programs for RESOLVED #44–#48 plus a handful of generic / `is` / extension cases. |
| Random generation | Out. |
| CI | Extend `.github/workflows/tests.yml` (already runs `dotnet test`). PHPUnit-on-CI, coverage, and extra OS runners are `ALPHA_RELEASE.md`, not this workstream. |

### D. Phases

#### D.1 — Runner

- [x] Harness compiles each fixture, runs PHP, classifies stderr.
- [x] Add a step to the **existing** `.github/workflows/tests.yml` so that job runs the emit-and-run filter (needs PHP on the runner). Do not invent a second workflow.

#### D.2 — Seed corpus

- [x] Struct foreach property + missing member (must error at check, not reach PHP).
- [x] Struct extension call emits a real call / splice, not `$recv['name']()`.
- [x] `Box<int> $b = new Box<string>(…)` does not reach PHP.
- [x] Namespaced top-level function call runs.
- [x] `$x is string` runs under the B lowering once B lands; until then the fixture still must not warn.

#### D.3 — Documentation and AIDevGuide

- [x] `AIDevGuide/handbook/` testing / conformance note: emit-and-run vs golden PHP.
- [x] `CONVENTIONS.md` only if a new test-tree layout needs a path rule.

### D. Golden fixtures / tests

- [x] Seed corpus green on the current compiler after C (and B if already merged).
- [x] A planted fixture that emits `$arr->prop` on an array fails the harness.

---

## Workstream E — Honest emit docs, inference-first locals, “typed superset”

> **Status:** **Design locked.**
> **Depends on:** nothing. Independent of A (A.8 owns generics erase / `Generic::bind` pages).
> **Source:** `LANGUAGE_ASSESSMENT.md` §4.2 / §6.3 / §4.4; DOQ §15c / §15d / §16 locked 2026-09-15.

### E. Summary

Docs-only. Three product locks in one sweep:

1. **Tell the truth about emit** — when output is Tyhp-runtime PHP (hooks, async, structs-as-arrays, remaining `Type::is`).
2. **Locals lead with inference** — `$var = …` is the documented primary form; `Type $var = …` stays legal for when inference is not enough.
3. **Tagline is “typed superset”** — not “strongly typed.” Same two-world honesty TypeScript uses. Replace the phrase in user-facing prose (site, README, FAQs, AIDevGuide). Do not add a denial essay about strong typing.

`with` stays as implemented. Document object `with` as mutating identity and struct `with` as rebinding an array — the same distinction as passing an object vs passing an array into a PHP function. Do not rename the keyword.

### E. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Public wording | “typed superset of PHP” / “typed superset of the PHP language.” Drop “strongly typed” from taglines and first-paragraph definitions. |
| `Type $var` | Keep in the language. Quickref / intro examples use inferred locals first. |
| `with` | Keep. Document the PHP array-vs-object argument analogy. |
| Generics Mechanism C/D / `EraseGeneric` | A.8, not E. |
| Emit honesty in scope | Property-hook polyfill on PHP < 8.4; async → `Promise::_async`; structs → arrays; remaining `Type::is` after B; blocking I/O blocks `tyhp/async`. |

### E. Phases

#### E.1 — Documentation and AIDevGuide

| Page | Change |
|------|--------|
| `docs/content/intro.md`, `faq_general.md`, `faq_other.md`, `faq_tyhpSyntax.md`, `tyhp_0000_openTag.md`, `tyhp_2900_lostFunctionality.md`, `intro_newSyntaxCreation.md`, `other_bref.md`, `other_hack.md`, `other_typescript.md` | Tagline “typed superset”. `other_typescript.md` should not say both languages are “strongly-typed supersets.” |
| `README.md` | Same tagline; do not show only trivial `Box<T>` erase as if it were always true. |
| `docs/content/quickref.md` and intro examples | Locals: `$sum = 0;` / inferred `foreach`; show `int $x = …` as the explicit form, not the first form. |
| `docs/content/tyhp_2500_phpMagicMethods.md` | Hook polyfill only when `output.phpVersion` < 8.4. |
| `docs/content/tyhp_2600_asyncAndAwait.md` | Lowering to `Promise::_async` / `_await`; blocking PHP I/O blocks the loop (Idea 17 is the later I/O). |
| `docs/content/tyhp_0400_structs.md` | PHP sees `array`; no runtime shape check (Ideas 4 / 15). |
| `docs/content/tyhp_2200_withKeyword.md` | Array-vs-object argument analogy; no rename. |
| `AIDevGuide/QUICK_GUIDE.md`, `guide/01-mental-model.md`, `REGEN.md` | Tagline + inference-first locals. |
| `AIDevGuide/guide/28-availability-gotchas.md` | Only if it claims `isa`/`isan` or required ctor `: void` — those pages update in G / H. |

- [x] Updated the pages in the table; grepped remaining user-facing “strongly typed superset.”

### E. Golden fixtures / tests

- [x] No compiler change. Docs review: one hooked property, one async function, one struct, one inferred local in quickref, README first paragraph says “typed superset.”

---

## Workstream F — withdrawn (CI already exists)

> **Status:** **Withdrawn** (2026-09-15). Letter **F** is retired; do not reuse it. **G** and **H** keep their letters.

`.github/workflows/tests.yml` already runs `dotnet test tests/Tyhp.Tests/Tyhp.Tests.csproj` on push and pull_request (`ubuntu-latest`, .NET 9, NuGet cache). `CONTRIBUTING.md` already points at it. `LANGUAGE_ASSESSMENT.md` §4.5, `INCOMPLETE.md`, and Story 07 Phase 10 were stale when they said there was no workflow.

This was never a language change. Putting “add GitHub Actions” in 21.12 next to `NativeTypeTest` and ctor `: void` was the wrong bucket. Story 31 is the wrong bucket too (language/runtime ideas).

**Still true, not this workstream:**

- Do not un-skip FOUND #1 / #2 in CI.
- Workstream **D** adds one step to the **existing** `tests.yml` when emit-and-run lands (needs PHP on the runner).
- PHPUnit job, coverage reports, extra OS runners, and a fuller `tests/readme.md` wait for the public-contributor wave in `ALPHA_RELEASE.md`.

---

## Workstream G — Remove `isa` / `isan` / `is_a` / `is_an`

> **Status:** **Design locked.**
> **Depends on:** Story 06 lexer (`T_TYHP_IS`). Independent of B (until G lands, leftover aliases still lower like `is`).
> **Source:** `LANGUAGE_ASSESSMENT.md` §4.4 / §6.5; DOQ §15a. Greenfield: **no** deprecation window.

### G. Summary

Keep `is` and PHP `instanceof`. Delete the other Tyhp spellings. They are the same token today:

```
('is'|'isa'|'isan'|'is_a'|'is_an') → T_TYHP_IS
```

`is_a` / `is_an` go too: they are on that same alternative list, and `is_a` as a **keyword** shadows PHP’s `\is_a()` function. `\is_a(...)` the function remains; it is just no longer a `instanceof` alias.

Greenfield: tests, docs, and AIDevGuide that use `isa`/`isan`/`is_a`/`is_an` as operators are rewritten to `is` or `instanceof`. No warning, no flag, no dual lexer.

### G. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Keep | `is`, `instanceof` |
| Remove | `isa`, `isan`, `is_a`, `is_an` as operator keywords |
| `\is_a()` / `\is_an` as names | `\is_a()` is the PHP function (tyhpdef unchanged). There is no `\is_an()`. |
| Deprecation period | None. |

### G. Phases

#### G.1 — Lexer / parser / checker / emitter

- [x] `TyhpLexer.g4`: `T_TYHP_IS` matches `is` only (still Tyhp/tyhpdef mode).
- [x] Drop `isa`/`isan`/`is_a`/`is_an` from operator tables (`CheckerHelpers`, `TypeNarrowingRule`, `TyhpEmitter.Helpers`, `ExpressionTreeSupport`, `PhpBinaryOperator`, …).
- [x] Regenerate ANTLR artifacts.
- [x] Using a removed spelling is a normal unexpected-token / unknown-identifier parse, not a dedicated “deprecated alias” diagnostic.

#### G.2 — Documentation and AIDevGuide

| Page | Change |
|------|--------|
| `docs/content/tyhp_0200_typeNarrowingAndGuards.md` | Only `is` and `instanceof`. |
| `AIDevGuide/guide/16-type-guards.md`, `QUICK_GUIDE.md`, `REGEN.md` | Same. |
| `AIDevGuide/guide/28-availability-gotchas.md` | Do not list `isa`/`isan` as live syntax. |
| Tests / examples / conformance | Rewrite to `is` or `instanceof`. |

### G. Golden fixtures / tests

- [x] `$x is Foo` and `$x instanceof Foo` still parse and narrow.
- [x] `$x isa Foo` / `$x isan Foo` / `$x is_a Foo` / `$x is_an Foo` do not parse as `instanceof`.
- [x] `\is_a($obj, User::class)` is a function call, not the operator.

---

## Workstream H — Optional constructor `: void`

> **Status:** **Design locked.**
> **Depends on:** Story 06 grammar (`tyhpCtorReturnType`). Independent of G.
> **Source:** `LANGUAGE_ASSESSMENT.md` §4.4 / §6.5; DOQ §15b. Greenfield.

### H. Summary

`function __construct(int $x) {}` is legal Tyhp and means the same as `: void` (no `parent::__construct` insertion). `: void` remains allowed. `: parent(...)` remains the **only** new form and is still required when the author wants the parent constructor called first.

Do **not** insert `parent::__construct()` when the author wrote neither `: void` nor `: parent(...)`. Omitted = `: void`, not PHP’s implicit parent call.

### H. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Omitted ctor return type | Same as `: void`. |
| Written `: void` | Still legal. |
| `: parent(...)` | Unchanged; still the chaining form. |
| Implicit PHP parent-ctor call | Not added. |
| Emitted PHP | Still no return type on `__construct`. |

### H. Phases

#### H.1 — Grammar / AST / binder / checker / emitter

- [x] `tyhpCtorReturnType` optional on `tyhpClassCtorWithReturnType` (`ReturnType=tyhpCtorReturnType?`).
- [x] Missing node → `TyhpCtorReturnTypeAst` void (or null treated as void in binder/checker).
- [x] Regenerated ANTLR artifacts.
- [x] Existing `: void` / `: parent(...)` goldens unchanged.

#### H.2 — Documentation and AIDevGuide

| Page | Change |
|------|--------|
| `docs/content/tyhp_1300_newObjectDeclSyntax.md` | `: void` optional; omitted = no parent call; `: parent(...)` still required to chain. |
| `docs/content/tyhp_2500_phpMagicMethods.md` | Same. |
| `AIDevGuide/guide/22-declarations.md`, `QUICK_GUIDE.md`, `REGEN.md` | Same. |
| `AIDevGuide/guide/28-availability-gotchas.md` | Remove “ctors must declare `: void`.” |

### H. Golden fixtures / tests

- [x] `function __construct(int $x) {}` parses; emits PHP ctor with no return type; does not call `parent::__construct`.
- [x] `function __construct(int $x): void {}` still works (byte-equivalent emit to omitted).
- [x] `: parent(...)` still inserts `parent::__construct(...)` first.
- [x] Non-constructor methods still require a return type.

---

## Cross-Story References

- Story **04** — `tyhp/core` hosts `Generic`, `GenericRuntime`, `EraseGeneric`, `NativeTypeTest`.
- Story **06** — lexer / ctor grammar (G, H).
- Story **07** — conformance runner (D). `.github/workflows/tests.yml` already exists; D extends it. Remaining Phase 10 polish is `ALPHA_RELEASE.md`.
- Story **08** — type-guard returns, member access, `Unresolved` (B, C).
- Story **09** / **11** — `is` emit; FOUND #1d same-compilation factories stay; foreign sites are the exception.
- Story **20** — Track C `package.tyhpdef` stamp.
- Story **21.9** — `aliasFactory`; do not route aliases through `Generic::bind`.
- Story **21.11** — previous bucket; this story does not depend on 21.11 A–F.
- Story **30** — user docs must match when A / B / E / G / H are implemented.
- Story **31** Idea 4 (docblocks), Idea 5 (`operator default()`), Idea 15 (runtime struct checks), Idea 16 (tyhpdef symbol cache), Idea 17 (async HTTP/DB/FS).
- `FOUND_BUGS.md` #1 #2 (still skipped; do not fail CI on them), #6 (Idea 5), #56 (Workstream C).
- `LANGUAGE_ASSESSMENT.md` §8 — why B–E, G, H exist; F withdrawn.
- `DESIGN_OPEN_QUESTIONS.md` §§15–18 — 15a/b/d and 16 tagline locked here; 15c `with` kept; 17 → Idea 17; 18 don’t.
- `DECISIONS.md` — `isa`/`isan`/`is_a`/`is_an` removed; omitted ctor `: void`; `with` kept; tagline “typed superset”; no emitter pipeline.
- `ALPHA_RELEASE.md` — Packagist / `tyhp init` without this checkout; leftover CI polish (PHPUnit job, coverage, extra OS).
- `CONVENTIONS.md` §10 — named documentation pages above.
- `ROADMAP.md` — 21.12 row after 21.11.

When adding Workstream I+, add a bullet here rather than inventing a second references section.
