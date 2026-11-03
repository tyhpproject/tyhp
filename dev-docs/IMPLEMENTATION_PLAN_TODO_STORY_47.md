# Implementation Plan: Story 47 — Declaration-Site Generic Variance (`in` / `out`)

> **Roadmap position:** Story 47 — **Tier 4 — Future plans**
> **Direct dependencies (new numbering):** 08, 28
> **New story:** split from Story 31 Idea 19 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration). This story
> changes what an author writes, so `guide/28-availability-gotchas.md` must be updated too: its
> "not in the language yet" list is what an agent trusts to decide whether a construct is usable.

> **Prerequisites:** Story 08 (checker subtyping), Story 28 (generic defaults — declaration order).
> Interacts with Story 22.1 Part A (`@template-covariant` / `@template-contravariant`).
> **Source:** `DESIGN_OPEN_QUESTIONS.md` §6 (moved here 2026-11-03); asked for in public beta feedback on the
> r/PHP launch thread (2026-11).

## Summary

Let a generic type parameter declare how its type argument may vary, so that `Producer<Dog>` can be used
where `Producer<Animal>` is expected:

```tyhp
interface Producer<out T> { function produce(): T; }            // covariant   — T only comes out
interface Consumer<in T>  { function consume(T $value): void; }  // contravariant — T only goes in
interface Transform<in TIn, out TOut> { function apply(TIn $x): TOut; }
```

```tyhp
Producer<Animal> $p = getDogProducer();   // covariance:     Producer<Dog>    → Producer<Animal>
Consumer<Dog>    $c = getAnimalConsumer(); // contravariance: Consumer<Animal> → Consumer<Dog>
```

Today every generic is invariant. Generics are erased, so variance changes nothing in the emitted PHP; it is a
checker rule. Most of the plumbing already exists and is unused:

- `Tyhp/TyhpLang/Enum/TypeVariance.cs` defines `Invariant` / `Covariant` / `Contravariant`.
- `GenericTypeParameterSymbol.Variance` holds it.
- `TypeComparer.Subtyping.cs` already reads it when comparing type arguments and branches correctly
  (covariant → compare in order, contravariant → compare reversed, invariant → require equality).
- Nothing in the compiler ever assigns `Variance`, so it is always `Invariant`. There is no syntax to set it.

The missing pieces are the surface syntax, the binder wiring, and the variance-safety check.

## Open questions (answer before scheduling)

- **Variance safety checking — the real cost.** Declared variance is unsound without a position check:
  `out T` may only appear in output positions (return types, readonly property types) and `in T` only in
  input positions (parameter types). Enforcing that needs a pass over every member signature of a variant
  generic, plus diagnostics. Implement full safety checking, or trust the author (cheap, but unsound)?
- **Constructors and `readonly`.** A constructor takes `T` as a parameter (input position) even on a covariant
  `out T`, and `readonly T $x` is input-at-construction but output thereafter. C# exempts constructors from the
  check. Confirm the same rule, and decide how `readonly` properties are classified.
- **Existing array covariance.** `TypeComparer.Subtyping.cs` has an `arrayLikeCovariant` special case that forces
  covariance for array-like types regardless of declared variance. Does declared variance subsume it, or do they
  coexist (and which wins)?
- **Declaration-site only, or use-site too?** C# is declaration-site only; Kotlin has both; Java is use-site only
  (wildcards). Declaration-site alone is the smaller feature and probably the right first cut.
- **Interaction with `extends` constraints and generic defaults (Story 28).** Declaration order
  (`out T extends Foo = Bar`).
- **Structs.** Struct compatibility is already structural, so variance is probably meaningless there. Confirm it is
  a class/interface/trait-only annotation.
- **Method-level generics.** Most likely reject variance on a generic method's own type parameters.

## Golden Fixtures / Tests (Acceptance)

To be written when the open questions are answered. At minimum: covariant and contravariant assignment succeed,
invariant assignment still fails, and `out T` in a parameter position reports a diagnostic (if safety checking is
chosen).
