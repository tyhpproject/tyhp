# Implementation Plan: Story 41 — Trait-Requirement Abstract Members

> **Roadmap position:** Story 41 — **Tier 3 — Advanced**, after Story 32 and before Story 23 (moved from Tier 4 on
> 2026-11-04: shipping it after the first stable release would change the emitted PHP that plain-PHP consumers of
> published packages compile against).
> **Direct dependencies (new numbering):** 08, 09, 11
> **New story:** split from Story 31 Idea 12 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration). This story
> changes what an author writes, so `guide/28-availability-gotchas.md` must be updated too: its
> "not in the language yet" list is what an agent trusts to decide whether a construct is usable.

> **Status:** Emit-only; Tyhp source stays `trait T extends C implements I`. Checker
> diagnostics TYHP4044 / TYHP4045 stay the source of truth for using classes.
> **Prerequisites:** Story 08 (trait `extends`/`implements` check), Story 09/11 (emitter).
> **Related:** [Trait Requirements](../docs/content/tyhp_2000_traitRequirements.md) currently erase the clauses
> entirely. PHP traits cannot `extends` / `implements`; they **can** declare `abstract` methods, which the
> composing class (or a parent) must satisfy.
> **Source:** trait-requirements page (`Cacheable extends Entity implements Serializable`), 2026-08-14.

## Summary

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

## Used items only

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

Signatures must be PHP-legal: erase generics / shapes the same way other emit does. Story 22.1 docblocks can
annotate the abstracts later.

## Why this helps

- Tyhp already rejects a using class that misses `extends` / `implements`.
- Emitted PHP is also consumed as PHP. Abstract used-members catch `use Cacheable` from PHP without those
  methods at **class load**, not at first call.
- Parent implementations count in PHP (`User extends Entity` already has `getId()`), so a correct Tyhp
  using class does not need to redeclare the abstracts.

## Decisions (defaults chosen)

1. **Used members only** — not the full required type.
2. **Methods first** — properties/constants are out until PHP can express them without colliding.
3. **Keep erasing `extends`/`implements`** on the trait declaration.
4. **Checker unchanged** — 4044/4045 remain; this is a PHP-side net, not a substitute.

## Risks & Edge Cases

- Signature mismatch vs parent: PHP fatal if the abstract is incompatible with `Entity::getId()`. Emit the
  target’s signature, not a guessed one.
- Overloaded / generic methods: emit the collapsed PHP signature.
- Trait already has a same-named concrete method: skip.
- Two required types contributing the same name: one abstract; signatures must be compatible or skip and
  rely on the checker.
- `final` methods on the parent: parent still satisfies the trait abstract (verify in fixtures).
- Visibility: copy the target member’s visibility (`protected` stays `protected`).

## Golden Fixtures / Tests (Acceptance)

- `Cacheable` example: abstracts for `getId` and `serialize` only; no `unserialize`; no `extends`/`implements`
  on the trait.
- Using class that extends `Entity` + implements `Serializable` still emits/runs (parent satisfies abstracts).
- Trait that never calls into the requirement emits no extra abstracts.
- Property-only use of the required type does not add a colliding trait property.
- Existing trait-requirement checker tests (4044/4045) still pass.
