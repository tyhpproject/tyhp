# Implementation Plan: Story 33 — Default Interface Implementations (interface-attached traits)

> **Roadmap position:** Story 33 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 06, 08, 09, 11
> **New story:** split from Story 31 Idea 3 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration). This story
> changes what an author writes, so `guide/28-availability-gotchas.md` must be updated too: its
> "not in the language yet" list is what an agent trusts to decide whether a construct is usable.

> **Prerequisites:** Story 06 (built-in types / trait surface), Story 08 (checker), Story 09/11 (emitter — this is
> where the flattening happens). Interacts with Story 32 (a natural delivery mechanism for `*Convertible` default
> cross-conversions) but does not depend on it.

## Summary

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

## Motivation

Traits and interfaces are two halves of the same intent: the interface is the *contract*, the trait is a *reusable
implementation of that contract*. Today the author must repeat both:

```tyhp
class A implements Comparable { use ComparableDefault; }
class B implements Comparable { use ComparableDefault; }   // forgot the `use`? silent gap / fatal
```

Forgetting the `use` is a common bug (a required method silently missing, or a fatal at class load). Attaching the
trait to the interface makes the implementation travel with the contract, so `implements` alone is sufficient.

For **Story 32** specifically, this is where useful *default cross-conversions* live: implement `__toInt()` and get a
default `__toString()`/`__toFloat()`/`__toBool()` derived from it, each overridable.

## Syntax

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

## Lowering model (compile-time flattening)

PHP interfaces **cannot** hold traits or method bodies, so this is pure emit-time sugar — the interface still emits
as a plain PHP interface (signatures/constants only). The work happens when emitting each **class**:

1. Compute the transitive set of interfaces the class implements (direct + inherited + via other interfaces).
2. Collect every trait attached to any interface in that set.
3. **Deduplicate by distinct trait** (a diamond where two interfaces attach the same trait flattens to one `use`).
4. Subtract traits the class already `use`s explicitly, and members the class defines itself (see precedence).
5. Emit a single `use TraitX, TraitY, …;` block (plus any needed adaptation block, see below) in the class body.

The interface's own emitted PHP is unchanged (no trait reference — PHP would reject it).

## Precedence & collision resolution (the careful part)

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

## Decisions (defaults chosen)

1. **Injection is compile-time flattening** into the class; interfaces emit as plain PHP interfaces.
2. **Class-defined members and explicit `use` always suppress injection** for the overlapping trait/member.
3. **Dedup by distinct trait**; same-trait diamonds are no-ops.
4. **Unresolved trait-vs-trait collisions are a hard error**, resolved via an explicit `use { insteadof / as }` block
   in the class (interface-qualified sugar deferred).
5. **Attachment is transitive** through interface `extends`.

## Risks & Edge Cases

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

## Golden Fixtures / Tests (Acceptance)

Per `CONVENTIONS.md` §5, add `.tyhp → .php` (+ expected-diagnostics) golden fixtures under
`tests/conformance/story31/interface-traits/` with a `manifest.json` the runner asserts.

- **Basic attach:** `interface I { use T; }`; `class C implements I {}` emits `class C implements I { use T; }`.
- **Transitive:** `I extends J`, both attach traits; an implementer of `I` gets both, deduped, in one `use`.
- **Diamond dedup:** `I` and `K` both attach `T`; a class implementing both emits a single `use T;`.
- **Class override wins:** class defines the member → no trait member injected; no conflict.
- **Conflict error:** two different attached traits, same member, class silent → diagnostic emitted.
- **Explicit resolution:** author's `use A, B { A::m insteadof B; B::m as m2; }` suppresses auto-injection and compiles.
- **Interface emission:** the interface itself emits as a valid PHP interface with **no** trait reference.
- **Story 32 synergy:** `StringConvertible` attaches a default-conversion trait; implementing it (implementing only
  `__toInt()`, say) yields a working `__toString()` derived default, overridable by the class.
