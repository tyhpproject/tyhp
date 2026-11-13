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
> The object-shape pages must say a trait clause may name a shape alias.

> **Status:** Emit the abstracts, and accept an object-shape alias on a trait `extends` or
> `implements` clause. Classes, enums, and interfaces still reject a shape there (TYHP4347).
> TYHP4044 / TYHP4045 stay the source of truth for a using class, including a shape it does not satisfy.
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

When scheduled, still **do not** emit `extends` / `implements` on the trait header (invalid PHP). Instead
collect members the trait **actually uses** from those required types and emit matching `abstract`
declarations so PHP enforces the needed signatures. Group those abstracts under a comment per requirement,
in the trait's clause order (`extends` first, then each `implements`). Name the same requirements in the
trait docblock with the analyzer tags below, so a PHP reader can see which class and interfaces the trait
is meant to be applied to:

```php
/**
 * @phpstan-require-extends Entity
 * @psalm-require-extends Entity
 * @require-extends Entity
 * @phpstan-require-implements Serializable
 * @psalm-require-implements Serializable
 * @require-implements Serializable
 */
trait Cacheable
{
    // extends Entity
    abstract public function getId() /* Entity signature, PHP-representable */;

    // implements Serializable
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

The header stays `trait Cacheable`. The abstracts require whoever `use`s the trait to provide those methods
(own body or inherited), which is as close as PHP itself can get. The comments and the `require-extends` /
`require-implements` tags name the Tyhp clauses for people and for static analysis. The sample above spells
types the way the Tyhp clause does; generated tags use the fully qualified name, same as other analysis tags.

## Used items only

Emit abstracts for symbols referenced on `$this` / `static` / `self` that resolve to the required class,
interface, or object shape (and members inherited through a class or interface). Do **not** dump the whole
target API.

| Used from the requirement | Emit |
| --- | --- |
| Instance/static method (`$this->getId()`, `static::foo()`) | `abstract` method with visibility + PHP-erased signature |
| Method the trait already declares | Skip (trait member wins) |
| Unused `Serializable::unserialize` | Skip |
| `private` on the target | Skip — not callable from the trait |
| Property read/write (`$this->id`) | Do **not** emit a trait property (collides with the parent’s property). PHP has no useful pre-8.4 abstract property. Document as methods-only unless a later PHP target makes abstract hooks viable |
| Class constants (`Entity::FOO`) | Skip — traits cannot require constants |

Signatures must be PHP-legal: erase generics / shapes the same way other emit does. Per-method docblocks on
the abstracts follow the existing analysis-docblock emitter.

## Object-shape requirements

A trait may name an object-shape alias in `extends` or in `implements`. A class, enum, or interface that
does the same stays TYHP4347 (`Object shape '{0}' is not a class`). `new`, `::`, and `::class` on a shape
stay TYHP4347 too.

```tyhp
type HasId = object {
    public string $id;
    public function getId(): string;
};

trait Cacheable extends HasId
{
    public function getCacheKey(): string
    {
        return $this->getId();
    }
}

trait Labelled implements HasId
{
    public function label(): string
    {
        return $this->id;
    }
}
```

Trait emit for that sample. `HasId` is a shape, so neither trait gets a require-extends or require-implements tag. `Cacheable` calls `getId()`, so that method is an abstract under the extends comment, and the property bag records `$id`. `Labelled` only reads `$id`, so it has the implements comment and no abstract:

```php
trait Cacheable
{
    // extends HasId (object{id: string})
    abstract public function getId(): string;

    public function getCacheKey(): string
    {
        return $this->getId();
    }
}

trait Labelled
{
    // implements HasId (object{id: string})

    public function label(): string
    {
        return $this->id;
    }
}
```

Allowed targets are a `type` alias whose expanded type is an object shape: the `object { … }` itself, a
rename of one, or a generic application (`extends Box<string>`). The clause is still a name, so an inline
`object { … }` stays TYHP4348. An intersection or union that merely contains a shape stays TYHP4347 on a
trait as well. Callable-shape aliases and struct aliases stay rejected.

`extends HasId` and `implements HasId` are the same requirement. The using class must satisfy the shape by
the existing width check: every public method except `__construct` (instance or static), every public
property, and every public constant, with compatible signatures. Extra members on the class are fine. The class does not
gain a PHP parent or interface from the shape. A miss is TYHP4044 when the alias is in `extends`, and
TYHP4045 when it is in `implements`. Inside the trait, `$this` / `self` / `static` resolve those shape
members, so the body typechecks and the emit pass can see which ones are used.

`__construct` on the shape stays a constructability signature. It is not required of the using class and it
is not emitted.

## Section comments

Place the abstracts before the trait's own members. One comment per requirement that still owns at least one
abstract, then that section's abstracts, then a blank line. Comment text is `// extends Entity` or
`// implements Serializable`: the keyword, one space, then the type's short name as written in the Tyhp
clause. A generic shape application keeps its arguments (`// extends Box<string>`).

A shape alias appends the property bag when the shape has any properties, using the same `object{id: string}`
spelling the analysis-docblock helper already uses for property shapes. Methods are the abstracts under the
comment, so the bag does not repeat them:

```php
// extends HasId (object{id: string})
abstract public function getId(): string;
```

A shape with no properties keeps the plain `// extends HasId` / `// implements HasId` comment.

Section order is the clause order on the trait: the `extends` type first when the trait has one, then each
`implements` type in source order. Within a section, abstracts follow the order the trait first references
those members.

A used member is emitted once. When more than one requirement declares it, it belongs to exactly one section:

| Declared on | Section |
| --- | --- |
| The `extends` type only | `// extends …` |
| Exactly one `implements` type | that `// implements …` |
| The `extends` type and one or more `implements` types | the earliest of those `implements` types |
| Two or more `implements` types | the earliest of those `implements` types |

A class or interface requirement that loses every used member to an earlier `implements` section gets no
comment. Its docblock tag still names it. A shape alias has no such tag, so its section comment is always
emitted, in clause order, even when it owns no abstracts (property-only shape, or every method claimed by
an earlier `implements`). That comment is the only place the shape's name appears in the PHP.

## Requirement tags

`@extends` and `@implements` are the wrong tags here. Those describe the type's own parents. The tags that
mean "a class that `use`s this trait must extend / implement this type" are:

| Analyzer | Extends (one class) | Implements (repeat, one tag per interface, clause order) |
| --- | --- | --- |
| PHPStan 1.10.56+ | `@phpstan-require-extends \App\Entity` | `@phpstan-require-implements \Serializable` |
| Psalm | `@psalm-require-extends \App\Entity` | `@psalm-require-implements \Serializable` |
| Phan 6 | `@require-extends \App\Entity` | `@require-implements \Serializable` |

PHPStan also reads the `@psalm-require-extends` / `@psalm-require-implements` spelling. Psalm does not read
the `@phpstan-` spelling. Phan 6 reads the unprefixed spelling.

Emit them on the trait docblock through the existing analysis-docblock path (`emit.docblocks`,
`emit.docblockDialect`), with the same merge rules: author tags win per key, generated names are fully
qualified. These tags say more than the PHP header, so the default `lossy-only` mode includes them.
`emit.docblocks: "off"` omits them.

Dialect spelling, in clause order (the extends tags, then each interface's tags). Within a clause the order is `@phpstan-`, `@psalm-`, then the unprefixed Phan tag when that dialect includes it:

| `emit.docblockDialect` | Tags |
| --- | --- |
| `both` (default) | `@phpstan-require-…`, `@psalm-require-…`, and unprefixed `@require-…` |
| `phpstan` | `@phpstan-require-…` only |
| `psalm` | `@psalm-require-…` only |
| `phpdoc` | unprefixed `@require-extends` / `@require-implements` only |

List every class and interface clause, including a requirement that contributed no abstract. Shape clauses
are omitted here; see below. The tags are what tells
PHPStan, Psalm, or Phan that `use Cacheable` on a class that merely happens to have `getId()` is still
wrong unless that class actually extends `Entity` and implements `Serializable`. PHP ignores the tags; the
abstracts are what PHP enforces.

Section comments follow `output.comments`. They are omitted when comments are off. The tags stay, because
generated analysis tags are independent of `output.comments`.

### Shape clauses have no require tag

`@phpstan-require-extends`, `@psalm-require-extends`, and `@require-extends` take a class name.
`@phpstan-require-implements`, `@psalm-require-implements`, and `@require-implements` take an interface
name. A shape alias is erased. It is not a class or an interface, and the name `HasId` does not exist in
the PHP file. Writing it in either tag makes PHPStan, Psalm, and Phan report an unknown type.

`object{id: string}` is the property-bag spelling those analyzers already understand on `@param` and
`@return`. It is not a legal operand of require-extends or require-implements, and it cannot carry the
shape's methods. `@property` and `@method` on the trait would claim the trait already has those members,
which hides the requirement.

So a shape clause emits no require-extends and no require-implements tag. The section comment names the
alias, with the property bag when the shape has properties. Used methods are still abstracts, and that is
the part PHP enforces. Properties and constants stay checker-only, same as on a class requirement. With
`output.comments: false` the shape's name is absent from the PHP; there is no analysis tag that can keep it.

## Why this helps

- Tyhp already rejects a using class that misses `extends` / `implements`.
- Emitted PHP is also consumed as PHP. Abstract used-members catch `use Cacheable` from PHP without those
  methods at **class load**, not at first call.
- Parent implementations count in PHP (`User extends Entity` already has `getId()`), so a correct Tyhp
  using class does not need to redeclare the abstracts.
- PHPStan, Psalm, and Phan can require the actual class and interfaces. A same-shaped method on an unrelated
  class satisfies the abstract and still fails those analyzers. A shape alias has no tag they can check;
  Tyhp's width check and the emitted abstracts cover it.

## Decisions (defaults chosen)

1. **Used members only** — not the full required type.
2. **Methods first** — properties/constants are out until PHP can express them without colliding.
3. **Keep erasing `extends`/`implements`** on the trait header. Name each clause with a section comment and
   with `require-extends` / `require-implements` tags.
4. **4044/4045 stay the using-class diagnostics.** A shape alias is legal on a trait clause and is checked
   by width subtyping. Classes, enums, and interfaces still report TYHP4347 for a shape.
5. **One section per owning requirement**, in clause order. A shared member is listed under its earliest
   `implements` type, or under `extends` when no `implements` type declares it. A shape comment is emitted
   even when that section owns no abstracts.
6. **Requirement tags follow `emit.docblocks` and `emit.docblockDialect`.** They are emitted for classes
   and interfaces only. Section comments follow `output.comments`.

## Risks & Edge Cases

- Signature mismatch vs parent: PHP fatal if the abstract is incompatible with `Entity::getId()`. Emit the
  target’s signature, not a guessed one.
- Overloaded / generic methods: emit the collapsed PHP signature.
- Trait already has a same-named concrete method: skip.
- Two required types contributing the same name: one abstract, in the section from the table above. The
  signatures must be compatible, or skip that member and rely on the checker. Every clause is still named
  in the docblock.
- A class or interface requirement with no remaining abstracts gets no section comment. Its tag remains.
  A shape requirement always gets its section comment, and never a require-extends / require-implements tag.
- A property-only shape emits no abstract and no tag. The section comment, including `object{…}` when the
  shape has properties, is the whole PHP record of that requirement.
- `final` methods on the parent: parent still satisfies the trait abstract (verify in fixtures).
- Visibility: copy the target member’s visibility (`protected` stays `protected`).
- Do not emit `@extends` / `@implements` on the trait. Those tags mean the trait itself extends or
  implements the type.

## Golden Fixtures / Tests (Acceptance)

- `Cacheable` example: abstracts for `getId` and `serialize` only; no `unserialize`. Header is
  `trait Cacheable` with no `extends` / `implements` clause. `getId` sits under `// extends Entity`;
  `serialize` sits under `// implements Serializable`.
- A member declared on both `Entity` and `Serializable` is emitted once, under `// implements Serializable`.
- A member declared on two interfaces is emitted once, under the earlier `implements` comment.
- Default dialect `both`: for the base, `@phpstan-require-extends`, `@psalm-require-extends`, and
  `@require-extends`, then the same three `require-implements` tags per interface, in clause order,
  including an interface that contributed no abstract. `phpdoc` dialect emits only the unprefixed
  `@require-extends` / `@require-implements` forms.
- Using class that extends `Entity` + implements `Serializable` still emits/runs (parent satisfies abstracts).
- Trait that never calls into the requirement emits no extra abstracts. Its docblock still names each class
  and interface clause when `emit.docblocks` is not `off`. A shape clause keeps its section comment and
  gets no require tag.
- Property-only use of a required class or interface does not add a colliding trait property.
- `trait Cacheable extends HasId` and `trait Labelled implements HasId` both typecheck when `HasId` is an
  object-shape alias. The using class is accepted when it satisfies the shape, and TYHP4044 or TYHP4045
  reports when it does not. A class, enum, or interface clause naming `HasId` is still TYHP4347.
- Emit for `extends HasId` with a used `getId()` and a property `$id`: header `trait Cacheable`, comment
  `// extends HasId (object{id: string})`, abstract `getId`, no trait property, and no
  `@phpstan-require-extends` / `@psalm-require-extends` / `@require-extends` mentioning `HasId` or
  `object{…}`.
- A property-only shape still emits its section comment and emits no abstract.
- An intersection or union alias that contains a shape is still TYHP4347 on a trait clause.
- Existing trait-requirement checker tests (4044/4045) still pass.
