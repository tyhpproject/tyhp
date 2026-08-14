# Tyhp — Design Decisions & Rejected Ideas

> **What this is:** a record of language/syntax ideas that were **considered and deliberately rejected** (or
> firmly decided), together with the reasoning, so they are not re-litigated later. If you find yourself about
> to propose one of these, read the rationale first.
>
> For ideas that are still open or wanted-but-unscheduled, see `DESIGN_OPEN_QUESTIONS.md`.
> For the committed sequence of work, see `ROADMAP.md`.

---

## Rejected syntax ideas

### Function call with a trailing block — REJECTED

The idea was Ruby/Kotlin-style trailing closures, where a block after the call is passed as a `\Closure`
argument that captures the surrounding scope:

```
// proposed usage
using($myObj = new MyObj()) { /* do something with $myObj */ }

// proposed declaration (the ^$block marks the trailing-block parameter)
function using(DisposableInterface $disposable, ?\Closure ^$disposableScope) { ... }
```

**Why rejected:** the control-flow semantics could not be made to work cleanly when compiled to PHP:

- `return` inside the block was supposed to return from the *outer* scope — no clean way to express this in
  emitted PHP (would need a sentinel exception thrown and caught, which breaks if caught by the declared
  function, and is fragile in general).
- `yield` / `yield from` inside the block had no workable lowering (a thrown-exception trick can't resume flow;
  detecting the generator case and rewriting was too complex/unreliable).
- `goto` had to be constrained to stay inside the block.
- Scope capture required detecting variable usage and injecting `use (&$var)` into a synthesized closure.

The disposable-scope use case this was meant to serve is instead handled by the `:=` disposable-assignment
operator (scope-based disposal), which needs none of this machinery.

### Function-call-as-assignment — REJECTED

Sugar that turned an assignment on a call into passing the RHS as a trailing argument:

```
// proposed usage → PHP
myFunc($blah) = 43.234 - 10.3;      // → myFunc($blah, 43.234 - 10.3);

// proposed declaration (the =$param marks the "assigned" trailing parameter)
function myFunc(MyClass $blah, float =$assignment) { ... }
```

**Why rejected:** obscure, surprising syntax with no real advantage over a normal trailing argument; not worth
the parser complexity or the readability cost.

### `usestrait` operator — REJECTED

An `instanceof`-like operator that tested whether a class uses a given trait, and narrowed to that trait's
members within the guarded scope:

```
if ($obj usestrait SomeTrait) { /* access SomeTrait's members with trait visibility */ }
```

**Why rejected:** trait method **aliasing and precedence** (from `use` adaptations) make it infeasible to
reliably resolve which members are in scope, or under what names, after conflict resolution. Not feasible to
implement correctly.

---

## Rejected feature ideas

### `derive`-style generated members — REJECTED

An attribute that made the compiler generate boilerplate members from a type's own declared fields —
fieldwise `==`, a `<=>` in declaration order, a `__toString()`, and similar:

```
#[Derive(Equatable, Comparable, Stringable)]
final class Money {
    public function __construct(
        public readonly int $amount,
        public readonly string $currency,
    ): void {}
}
```

The pitch was that derived members cannot drift from the type's shape, because they are regenerated from
it on every build — you can never forget to update `==` after adding a field.

**Why rejected:** too much room for failure trying to reduce every possible object shape to a single
generated implementation body. The generator would have to make one blanket decision about field
selection, inheritance, nullability, nested object comparison, recursion, and type strictness that is
correct for every class it is applied to — and when its guess is wrong the failure is silent, because a
generated `==` that compares the wrong things still compiles and still returns a `bool`. The cost of
hand-writing these members is visible and local; the cost of a subtly wrong generated one is neither.

Not re-litigate-proof: if a **narrow** version ever looks compelling (for example, deriving only
fieldwise equality, only for `final` classes whose properties are all `readonly` scalars), it can be
reconsidered on those much smaller terms. The rejection is of the general mechanism.

> **Related:** `sealed` classes/interfaces and value-semantics for object types are tracked separately in
> `DESIGN_OPEN_QUESTIONS.md`; neither depends on this.

---

## Firm decisions

### `eval()` is disabled entirely

`eval()` is **completely disabled** in Tyhp — not merely sandboxed or scope-isolated. Earlier drafts explored
running `eval` in an isolated file scope (moving the code to its own file and `include`-ing it with passed
arguments) and changing its signature to `eval(string $code, mixed ...$args): mixed`. That approach was
abandoned.

**Rationale:** `eval` is inherently insecure and defeats static analysis. If a project genuinely needs it, the
`eval`-using code must be written in **PHP** and imported via a `tyhpdef` file — it may not be written directly
in Tyhp.

> **Note:** the status of the `init` property modifier is currently **unresolved** (an old scratch note said it
> was cancelled in favor of `readonly`, but the website docs still document it as a live feature). It is tracked
> in `DESIGN_OPEN_QUESTIONS.md` until confirmed one way or the other.
