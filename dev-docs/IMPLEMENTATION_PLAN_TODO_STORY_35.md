# Implementation Plan: Story 35 — No Uninitialized Storage (Ban `unset`, Require Property Init)

> **Roadmap position:** Story 35 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 08
> **New story:** split from Story 31 Idea 6 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration). This story
> changes what an author writes, so `guide/28-availability-gotchas.md` must be updated too: its
> "not in the language yet" list is what an agent trusts to decide whether a construct is usable.

> **Prerequisites:** Story 08 (checker) — especially Prop-init **#6** (local definite assignment / TYHP4014),
> **#7** (property initialization / TYHP4157), and **#8** (`unset` tracking / `AllowUnset` / TYHP4158). This story
> revises the **#8** design decision (declaration-gated unset via `#[\Tyhp\AllowUnset]`) rather than adding a
> parallel feature.
> **Related:** `RESOLVED_BUGS.md` → Prop-init #6 / #7 / #8 (2026-08-03); brainstorm 2026-08-03 (reject PHP-side
> hook-wrapping to seal `unset`; tighten Tyhp rules instead). Runtime: `runtime/packages/core/tyhp_src/AllowUnset.tyhp`
> becomes obsolete under this story and should be removed when scheduled.

## Summary

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

## Motivation

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

## Current baseline (what already exists)

| Surface | Today (Story 08 Prop-init) | This story |
|---|---|---|
| Instance prop read before init | TYHP4157 | Keep; treat as hard guarantee after construction |
| Instance `unset($this->prop)` without attribute | TYHP4158 | Always error (delete `AllowUnset`) |
| Instance `unset` with `#[AllowUnset]` | Allowed; clears init → 4157 on later reads | Remove attribute + path |
| Static typed property without initializer | Not specially required | **New:** require initializer |
| Static `unset(Foo::$x)` | Not specially rejected (beyond general gaps) | **New:** always error |
| Local declare-without-init | Allowed | Keep |
| Local read before assign | TYHP4014 | Keep |
| Local `unset($x)` | Clears definite assignment → later reads 4014 | **New:** always error (no clear-and-reuse) |

## Inheritance / replaced constructors (verify when implementing)

Prop-init #7 today tracks **only properties declared on the class under analysis**
(`PropertyInitializationAnalysis.EnumerateTrackedProperties` walks `objectSymbol.Members`, not the `extends`
chain). `MayBeUninitializedAfterConstruction` is recorded on each property symbol when **that declaring class's**
constructor is analyzed.

That leaves a known soundness gap to **close or explicitly decide** under this story:

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

**Required behavior when this story is scheduled:**

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

## Locals vs properties

- **Properties / statics:** no uninitialized state, ever, after the relevant init point (object construction / class
  load for statics with required initializers).
- **Locals:** temporary unassigned is fine (`string $s;` then assign in both `if`/`else` arms). That is
  definite-assignment, not uninitialized storage. What changes is only **`unset($local)`** — forbidden; use
  `?T` + `= null` if the goal is to release a value.

## Implementation sketch (when scheduled)

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

## Decisions locked by this story

- No PHP emission of identity property hooks solely to seal `unset` (interop trust boundary; not worth the
  get/ref/array-append tax).
- No require-initializer-at-declaration for locals.
- `?T = null` is the supported spelling for optional / clearable values (properties and locals).
- Helper calls from `__construct` still do not count as initializing `$this->prop` (unchanged from #7).

## Risks & Edge Cases

- **Breaking change** relative to the 2026-08-03 `#8` ship: any code using `#[AllowUnset]` or `unset` on
  properties/locals must migrate to nullable assignment.
- **`unset($arr[$i])` / `unset($obj->arr[$key])`** — array-offset unset is not "uninitialize a typed property"; keep
  allowing offset unset unless a separate decision says otherwise. This story targets **variable and property
  storage slots**.
- **Readonly / hooked properties** — already restricted; ensure diagnostics stay coherent (hooked props cannot be
  unset in PHP anyway).
- **Promoted parameters** — remain a declaration guarantee; no `AllowUnset` on promotion.
- **Traits** — property introduced by a trait must satisfy the same init rules in each using class's constructor
  analysis (including inheritance rules when the using class extends another type).

## Golden Fixtures / Tests (Acceptance)

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
