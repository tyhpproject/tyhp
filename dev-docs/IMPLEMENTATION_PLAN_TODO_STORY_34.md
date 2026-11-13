# Implementation Plan: Story 34 — `operator default()` (Author-Defined Default Value for a Class Type)

> **Roadmap position:** Story 34 — **Tier 3 — Advanced**, after Story 35 and before Story 32 (moved from Tier 4 on
> 2026-11-04).
> **Direct dependencies (new numbering):** 06, 08, 11
> **New story:** split from Story 31 Idea 5 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration). This story
> changes what an author writes, so `guide/28-availability-gotchas.md` must be updated too: its
> "not in the language yet" list is what an agent trusts to decide whether a construct is usable.

> **Prerequisites:** Story 06 (compile-time constructs — `default()`), Story 08 (checker), Story 11 (operator-overload
> declaration + call-site rewriting, which supplies the `operator <name>()` surface this reuses).
> **Related:** `FOUND_BUGS.md` → *Property initialization state, `default(T)` and runtime generic tracking*, items 4,
> 5 and 10. Item 10's stage-1 fix is a **prerequisite** for this story, and this story is its stage 2.
> **Note (2026-11-04):** `FOUND_BUGS.md` has since been renumbered. Item 10 is now #4 (`default(<class type>)` is
> typed as the class but emits `null`), and its stage 1 has shipped, so the prerequisite is met. The item numbers
> in the rest of this plan use the old layout.

## Summary

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

## Motivation

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
without this story the only possible answer is `null`, so `default(T)` with `T` bound to a class can never produce a
usable value even when the author has an obvious default in mind.

## Syntax

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

## Lowering model

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

## Decisions to make when scheduled

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

## Risks & Edge Cases

- **Recursion.** `operator default()` whose body evaluates `default(SameClass)` (directly or through a constructor
  default) recurses infinitely. Needs a checker cycle detection pass over operator bodies, or a runtime depth guard.
- **Construction cost in hot paths.** `default(T)` inside a loop now allocates. Worth a note in the guide; the
  optimizer stories (23/24) could hoist provably-immutable defaults.
- **`method_exists` cost.** The runtime hook does a `method_exists` per call. Cache per class name inside `Type`, or
  have the emitter resolve statically whenever the class is known at compile time (the common case) and fall back to
  the runtime hook only for generic bindings.
- **Interop.** Hand-written PHP that declares `__tyhpDefault` would be picked up by the runtime hook. Acceptable — it
  is the same opt-in surface — but should be documented rather than accidental.

## Golden Fixtures / Tests (Acceptance)

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
