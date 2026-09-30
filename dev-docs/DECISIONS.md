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

### C#-style `init` property modifier — REJECTED

A write-once-during-construction property modifier (C# `init`: settable in the constructor and via `with`,
but not by later assignment). Website drafts treated it as a live feature, distinct from `readonly`
(which those drafts claimed would also block `with`).

**Why rejected:** `readonly` plus `clone ... with` / `new ... with` already covers the immutable-update
pattern. `readonly` blocks in-place mutation after construction; `with` on a new or cloned instance can
still set those properties on the copy. A second modifier would duplicate that, and the compiler never
grew grammar, binder, checker, or diagnostic codes for `init` as a property modifier (`TYHP4055` /
`TYHP4056` in those drafts collide with unrelated checker codes).

This is not coming back. Document `readonly` + `with` instead
(`docs/content/tyhp_2200_withKeyword.md`). On PHP 8.2–8.4, `clone ... with` on `readonly` needs
`build.experimentalReadonlyCloneWith: true`; PHP 8.5+ does it natively.

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

### Optional-peer tyhpdefs: split packages and version suffixes — REJECTED

Libraries like `monolog/monolog` type-hint classes from Composer `suggest` / `require-dev` (Elastica, Gelf, AWS, …)
so apps only install the backers they use. The tyhpdef wrapper must not `require` those PHP packages (that would
install them for everyone).

**Rejected packaging:**

- **One Composer package per backer** (`tyhpdef/monolog-monolog__gelf`, …). Users would need a Tyhp-specific extra
  require besides the PHP package they already installed. The right pairing is `monolog/monolog` →
  `tyhpdef/monolog-monolog` and `ruflin/elastica` → `tyhpdef/ruflin-elastica`.
- **Version suffixes on the same package** (`3.10.0-base`, `3.10.0-gelf`, `3.10.0-elastica`). Composer installs
  **one** version of a package; `3.10.0-gelf` is a prerelease of `3.10.0`, not a variant; Gelf+Elastica cannot both
  be selected.

**Also rejected:** treating every unresolved tyhpdef name as a placeholder (typos go silent); naming the keyword
`stub` (Layer 2 overlay harvest already uses that word); hollow overlay `class \Foreign\Type {}` (usable empty
type + `TYHP8002` when the real wrapper arrives).

**Chosen:** Story 21.1 tyhpdef-only `extern` placeholders; a real declaration of the same Tyhp name silently wins.
Detail in `IMPLEMENTATION_PLAN_TODO_STORY_21.1.md`.

### Ambient vs author-only tyhpdefs (`extra.tyhp.require`) — DECIDED (Story 21.10)

Composer `require` / `require-dev` keep their usual meaning. Tyhpdef packages stay out of `require`. There is no custom `require-tyhp` root key.

A library lists **author** tyhpdefs in `require-dev` and the **ambient** subset (what consumers must install to type-check the public API) in `extra.tyhp.require`. Any Composer name is allowed, not only `tyhpdef/*`. `tyhp/compiler` is not listed in extras on `tyhp/*` packages; the plugin still pins it on the **root** `require-dev`.

Library `package.tyhpdef` generation spells ambient names as real FQNs and emits name-only `extern` (types, `extern function`, `extern const`) plus `@provided-by` for require-dev-only owners. Using those names from `.tyhp` is `TYHP4307`.

A Composer plugin on **`tyhp/core`** (`type: composer-plugin` + `extra.class`, hand-written PHP) walks extras **and** runtime `require` edges from Packagist / `repositories` at `PRE_DEPENDENCIES_SOLVING`, before vendor populate, and writes the merged set in one solve. Composer 2 only activates plugins whose package type is `composer-plugin` or `composer-installer`; `library` + `extra.class` is never registered. `PluginInstaller` still installs the package like a library. Cycles skip that branch. `--no-dev` is a no-op. The first `composer require tyhp/core` often misses that transaction; CLI check / `tyhp composer sync` recovers.

`internal` is parsed in 21.10 and omitted from the public `package.tyhpdef`. Story 25 still owns checker enforcement and the internals overlay.

Detail in `IMPLEMENTATION_PLAN_TODO_STORY_21.10.md`.

### Fiber `suspend` return is `mixed` — DECIDED (Story 21.6, confirmed 21.8)

`Fiber::suspend()` is a static method on whatever fiber is running. Typing its return as that
fiber’s `TResume` would require call-stack tracing (which helper called `suspend`, which `new Fiber`
it belongs to, whether one helper serves many fibers).

**Decision:** `TResume` is only the **argument** of `$fiber->resume($value)`. `start` / `throw` /
`suspend` **returns** stay `mixed|null`. No CFA, no `__CurrentFiber`, no special case for `suspend`
inside the `new Fiber` callback. Narrow from `mixed` before use. Confirmed again in the Story 21.8
Layer 3 overlay audit — do not reopen for tyhpdef or checker work.

Detail in `IMPLEMENTATION_PLAN_TODO_STORY_21.6.md` Decision 10.

### No tyhpdef `operator []` / `operator count()` for engine hooks — DECIDED (Story 21.8)

`\ArrayAccess` indexing and `\Countable` `count()` are Zend engine hooks. The checker already
implements `$obj[$k]` via `ArrayAccess<TKey, TValue>` / `ArrayAccessShape` and `count($x)` via the
`count` stub (`Countable|array` → `int`).

**Do not** add bodyless `operator []` or `operator count()` on those types in Layer 3. They would
fight `InferArrayAccess` and duplicate `count()`. DateTime is a different case: comparisons are
native PHP operators on the values, and `+`/`-` are mapped because PHP TypeErrors on
`DateTime + DateInterval` — that mapping is Story 21.8, not this rejection.

---

## Language-assessment locks (2026-09-15)

Firmly decided while planning Story 21.12 / 31. Do not reopen as taste questions.

### `isa` / `isan` / `is_a` / `is_an` operator aliases — REMOVED (Story 21.12 G)

Greenfield. Keep `is` and PHP `instanceof`. Delete the other spellings from the lexer (they were one
`T_TYHP_IS` alternative list). No deprecation warning. `is_a` as a keyword also shadowed PHP’s
`\is_a()` function; the function stays.

### Omitted constructor return type = `: void` — DECIDED (Story 21.12 H)

`function __construct(int $x) {}` is legal and means no `parent::__construct` insertion. Written
`: void` remains allowed. `: parent(...)` remains the only new form. Tyhp does **not** gain PHP’s
implicit parent-constructor call.

### `with` on structs vs objects — KEEP (Story 21.12 E)

Same keyword, different identity, matching PHP: passing an array copies, passing an object shares
the instance. Document that analogy. Do not rename.

### Public tagline “typed superset” — DECIDED (Story 21.12 E)

User-facing prose says Tyhp is a **typed superset** of PHP, not “strongly typed.” The checker is
strict *inside Tyhp*; erased contracts are advisory at the PHP boundary (Idea 4 / Idea 15).

### Emitter as a transformer pipeline — REJECTED for now (DOQ §18)

Do not rewrite the `EmitNode` walk for maintainability. Story 21.12 D (emit-and-run corpus) is the
substitute. Revisit only if that corpus keeps finding interaction bugs after 21.12 A–C.

Detail: `IMPLEMENTATION_PLAN_TODO_STORY_21.12.md`; leftover open questions in
`DESIGN_OPEN_QUESTIONS.md` §16 (PHP-boundary guarantees, throwing `default` on exhaustive `match`).


