# Implementation Plan: Story 21.6 — Closure / Fiber / Generator Type-System Contract

> **Roadmap position:** Story 21.6 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story **21.5**, before Story **21.7**). **Implement after 16.5 and 21.** Independent of 21.5 (Composer glue).
> **Direct dependencies (new numbering):** 08 (checker, callable facets, `__invoke`), 08.5 (utility-type registration), 16 (Expression / PropertyPath), 16.5 (`__CallableReturnType` / `__CallableParametersRest` / tyhpdef overloads), 21 (`tyhpdef/php` Layer 3 overlays)
> **New story:** replace the compiler’s built-in return-last `\Closure<TArgs…, TReturn>` handling with tyhpdef-authored generics (`TCallableShape`, `TThis`, `TScope`). Callable-ness comes from `__invoke`, not `extends T`. Fiber keeps `TResume` only on `resume()`’s argument; static `suspend` / start-return stay `mixed`. Add `__SuperType` / `__SuperTypeName` / `__CurrentScope` / `__CallableThis` / `__CallableScope`. Rewrite `\Tyhp\Expression` / `PropertyPath` to `TCallableShape`. Infer `\Generator<TKey, TValue, TSend, TReturn>` from generator function bodies; declared return is `\Generator` / `\Generator<K, V>` / full four-arg form (never the `return` statement’s type). Forbid user `implements`/`extends` of `\Traversable` except on `\Iterator` / `\IteratorAggregate`. Homogeneous `ArrayAccess<K,V>` indexing. `ArrayAccessShape<TStruct>` per-key maps; `#[\Tyhp\PhpType]` emit hints. Language-hooks inventory: `PHP_LANGUAGE_HOOKS.md`.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-09-01
> **Last design lock:** 2026-09-02 — ArrayAccessShape + `#[\Tyhp\PhpType]` on all PHP type-declaration sites + per-key exhaustiveness; overlay `runtime/packages/php/_tyhpdef/overlays/Ext.Core.tyhpdef`.
> **Status:** **Design locked** for Closure / Fiber / Generator / Traversable-implements / homogeneous ArrayAccess / ArrayAccessShape / `PhpType`. Do not implement until a later pass turns phases into work.
> **Prerequisites:** Story 08 (`CallableCheckedType`, `__invoke` facets, `GenericTypeArgumentValidator`); Story 16.5 (callable utilities + `FunctionOverloadSelector`); Story 16 (`Expression` / `PropertyPath`); Story 21 (Layer 3 overlay load).
> **Consumers:** Story 30 (docs); `tyhpdef/php` / `tyhp/lambda`; any code using `\Closure<int, string>` (breaking use-site spelling).

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [What already exists (do not reinvent)](#what-already-exists-do-not-reinvent)
- [Scope (In / Out)](#scope-in--out)
- [Decisions (locked)](#decisions-locked)
- [Type-system contract](#type-system-contract)
- [Canonical tyhpdef surface](#canonical-tyhpdef-surface)
- [Magic types](#magic-types)
- [Creation and inference](#creation-and-inference)
- [bind / bindTo / call](#bind--bindto--call)
- [Fiber](#fiber)
- [Generator](#generator)
- [Traversable implements](#traversable-implements)
- [ArrayAccess](#arrayaccess)
- [PhpType](#phptype)
- [ArrayAccessShape](#arrayaccessshape)
- [Expression / PropertyPath](#expression--propertypath)
- [Breaking use-site migration](#breaking-use-site-migration)
- [Interop contract](#interop-contract)
- [Phases](#phases)
- [Out of scope](#out-of-scope)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

PHP’s `\Closure` is a real class. Tyhp currently pretends it is a second `callable` with return-last type arguments (`\Closure<int, string>` = takes `int`, returns `string`). That blocks tyhpdef generics for `$this` / scope and fights the Layer 3 overlay.

This story:

1. **Removes** compiler special-case return-last handling for the **name** `Closure` (`IsBuiltInCallable`, `CallableArityFacetBuilder` treating generic args as params+return).
2. **Loads** Closure / Fiber generics from `tyhpdef/php` Layer 3 (`Ext.Core.tyhpdef` overlay). `callable` still uses return-last; `\Closure` does not.
3. **Types** `\Closure<TCallableShape, TThis, TScope>`. No `new Closure<…>()`. Instances come from anonymous functions, first-class callables (FCC), or `Closure::fromCallable`. Generics are **inferred**.
4. **Makes** Closure callable via `__invoke` (same rule as any `__invoke` object), not `class Closure extends T`.
5. **Adds** utility types: `__SuperType<T>`, `__SuperTypeName<T>`, `__CurrentScope`, `__CallableThis<TCallable>`, `__CallableScope<TCallable>`, plus overlay aliases `__ClosureThis` / `__ClosureScope<TThis>`.
6. **Retypes** Fiber as `Fiber<TResume = mixed, TCallableShape extends callable = callable>` with mixed start/throw/suspend **returns**.
7. **Rewrites** `\Tyhp\Expression` / `PropertyPath` (and builders) to `TCallableShape`, dropping the Expression return-last arity special-case.
8. **Infers** `\Generator<TKey, TValue, TSend, TReturn>` from `yield` / `return` in generator bodies. The **declared** return type is `\Generator`, `\Generator<TKey, TValue>`, or the full four-arg form — not `TReturn`.
9. **Rejects** `implements \Traversable` / user `interface extends \Traversable` except on `\Iterator` and `\IteratorAggregate` (tyhpdef harvest may still list redundant Traversable).
10. **Types** homogeneous `$obj[$k]` via `ArrayAccess<TKey, TValue>` (and emits **4093**).
11. **Adds** `#[\Tyhp\PhpType('mixed')]` on any PHP type-declaration site (parameter, function/method **return**, property, typed constant). Stripped on emit.
12. **Adds** `\Tyhp\Contracts\ArrayAccessShape<TStruct extends struct>` for per-key maps; checker instantiates `offsetGet`/`offsetSet` per key; unhandled keys are errors.

---

## Motivation

- Story 06/08 documented `\Closure<TArgs…, TReturn>` as documentation for variable arity, **not** a declaration-site `class Foo<...TArgs>`. Variadic type parameters are out of scope.
- Overlay authors need `$this` and visibility scope on Closure (`bind` / `bindTo` / `call` / `fromCallable`).
- `callable` is a facet; Closure is a class. Every Closure is callable (`__invoke`). Not every callable is a Closure (`fromCallable` wraps the rest).
- Fiber::suspend is static. Typing start/resume **returns** as `TSuspend` would be a lie without call-stack tracing. `resume($value)`’s **argument** as `TResume` is still a sound protocol of that Fiber instance.

---

## What already exists (do not reinvent)

| Surface | Today |
|---|---|
| `callable<TArgs…, TReturn>` | Built-in `GenericParameterRequirements.Callable()`; `CallableCheckedType`; **keep** |
| `\Closure<TArgs…, TReturn>` | Same return-last path via `IsBuiltInCallable` / `IsClosureTypeName`; **remove** |
| `__invoke` → callable facet | Story 08; reuse for Closure after special-case removal |
| `__CallableReturnType` / `__CallableParametersRest` | Story 16.5; reuse on `__invoke`, Fiber `getReturn` / `start`, `call` |
| Tyhpdef same-arity overloads | `FunctionOverloadSelector` (16.5 Phase 7); use for bind `'static'` vs explicit scope |
| Closure `$this` in literals | `ClosureRule` / `ClosureThisBindingTests`; extend to infer `TThis` |
| Layer 3 overlay | `runtime/packages/php/_tyhpdef/overlays/Ext.Core.tyhpdef` — **source of truth** for signatures in this story |
| Yield validation | `ControlFlowRule` 4086 / 4088 / 4089; `IsGenerator` on symbols; `InferYield` already reads **declared** TSend |
| Generator return family | Story 08 `4087` (`CheckerGeneratorInvalidReturnType`): declared type must be `Generator` / `iterable` / `Iterator` / `Traversable` |
| Language-hooks inventory | `dev-docs/PHP_LANGUAGE_HOOKS.md` — audit contract for PHP syntax-dispatch types |
| ArrayAccess `$obj[$k]` | `InferArrayAccess` returns `mixed` for objects; **4093 allocated but never emitted** |
| Foreach Traversable | `GenericInheritanceBindings.TryGetTraversableIterationTypes` |
| Expression arity special-case | `ResolveExpressionCallableArityArguments`; **replace** with `TCallableShape` |
| Story 15 `interopContractVersion` | Emitted PHP ABI. This story is type-system unless a runtime helper is added (deferred → Story 31 Idea 14) |

---

## Scope (In / Out)

**In**

- Checker: stop treating `Closure` as a built-in return-last generic; instantiate from `ObjectDeclarationSymbol` generics.
- Utility types listed below; erase like sibling `__` types (`object` / `string` / `mixed` as appropriate).
- Overlay signatures (Closure, Fiber) + `call(TNewThis extends object)`.
- Inference for literals, FCC, `fromCallable`.
- bind / bindTo overloads + checker rules (leftover scope vs `TNewThis`; object vs class-string `$newScope`).
- fromCallable visibility vs `__CurrentScope` when knowable.
- Fiber `TResume` on `resume` argument only.
- Generator body inference (`TKey` / `TValue` / `TSend` / `TReturn`) and declared-return shapes `\Generator` / `\Generator<K, V>` / `\Generator<K, V, S, R>`.
- Traversable `extends` / `implements` rule (Phase 0 — can land before Closure work).
- Homogeneous ArrayAccess: Layer 3 defaults + `offsetGet(): TValue` (not harvested `null|TValue`); `$obj[$k]` / assign / append; **4093**.
- `#[\Tyhp\PhpType]` + `ArrayAccessShape<TStruct>` (index utilities, inherit emit hints on implementors, per-key body exhaustiveness).
- Expression / PropertyPath / builders → `TCallableShape`.
- Docs + tests + type-system contract (this document + user-facing pages).
- Story 31 Idea 14: runtime `is Closure<…>` via reflection (document only).

**Out**

- Declaration-site variadic type parameters (`class Foo<...TArgs>` / `TArgs...`).
- `class Closure extends TCallableShape`.
- Compile-time fiber/closure call-stack tracing / CFA / `__CurrentFiber` / `__CurrentClosure` / `__FiberSuspend` / `__FiberResume` — **rejected**, not deferred. `Fiber::suspend()` return is `mixed` for every call site.
- Runtime generic metadata on PHP `Closure` / `Fiber` (`GenericObject`).
- Dual Closure spelling (old return-last **and** new `TCallableShape`) — **rejected**. This story **removes** compiler return-last for the name `Closure`.
- Story 15 interop version bump (unless Idea 14 ships a helper later).
- WeakReference Layer 3 header (Layer 2 already has `WeakReference<T>`).
- Inferring `TSend` from later uses of an untyped `$x` after `$x = yield` (no Hindley–Milner). Mixed + narrowing.
- Omitting the return type on generator functions (still `\Generator` at minimum).
- Refined-type `emit` member (`DESIGN_OPEN_QUESTIONS.md`; with refined types). `PhpType` is the v1 emit hint.
- `.tyhp` interface method overloads (still tyhpdef-only / class body+implementation).
- Stringable auto-`implements` from `__toString`; `list()` / named destructuring on ArrayAccess — **Story 21.7**.
- Remaining `tyhpdef/php` Layer 3 overlay completeness (array_map, str_replace TSubject, SPL implements remaps, DatePeriod, DateTime operators, …) — **Story 21.8**. Closure alias-bound expansion (FOUND_BUGS #6), `__CallableParametersTuple` index (FOUND_BUGS #8), and WeakMap `$map[] =` also move to 21.8.

---

## Decisions (locked)

1. **No variadic Closure generics.** Use `TCallableShape extends callable`.
2. **No `extends TCallableShape`.** `__invoke` supplies the callable facet (same as any invokable class). **No dual Closure spelling:** remove compiler return-last for the **name** `Closure` (`IsBuiltInCallable` / `IsClosureTypeName`). `\Closure<int, string>` is not “takes int, returns string”. Only the tyhpdef shape `\Closure<TCallableShape, TThis, TScope>` remains. `callable<…>` stays return-last.
3. **`TCallableShape` has no default.** Bare `\Closure` stays open/gradual. `\Closure<callable<int, string>>` is valid (`TThis` / `TScope` default).
4. **`TThis` default** is `__ClosureThis` (`object|null`) so static closures still match omitted `TThis`.
5. **`TScope` default** is `__ClosureScope<TThis>` = `__SuperType<TThis> | __SuperTypeName<TThis> | null`. **`'static'` is not part of stored `TScope`.**
6. **`'static'`** is only a `bind` / `bindTo` argument meaning “keep old scope.” Overloads return `TOldScope` / current `TScope`, not the literal `'static'`.
7. **`fromCallable` result** is `\Closure<TCallableShape, __CallableThis<T>, __CallableScope<T>>`. `__CurrentScope` is **not** result `TThis`. PHP binds `$this` / stored scope from **`$callback`**; “current scope” in the PHP manual is an **access check**.
8. **Keep `__CurrentScope`** (lexical class/enum; `null` at top-level; still the class in a **static** method). Used for annotations and fromCallable visibility.
9. **`__SuperType<T>`** = `T` or parent **object** types. **`__SuperTypeName<T>`** = class-name strings of `T` or parents (inverse of `__CompatibleTypeName`, which is descendants). If `T` is `null` or `object`: `__SuperType` → `object`, `__SuperTypeName` → `__ClassName`.
10. **Fiber:** `TResume` only on `resume($value)`. start / throw / suspend **returns** `mixed|null`. No `TSuspend`. `getCurrent(): ?\Fiber<mixed, callable>`. **`Fiber::suspend()` is `mixed` at every call site**, including inside the `new Fiber` callback. Call-stack tracing / CFA / `__CurrentFiber` will not be added — `suspend` can run from any helper and may serve one or many fibers. Narrow from `mixed` before use. That is the complete contract.
11. **`getCurrent` (Closure):** `\Closure<callable, __ClosureThis, __ClosureScope<__ClosureThis>>`. No extra generic for the user to fill. PHP 8.5 throws outside a closure (non-null).
12. **Invokable `fromCallable`:** keep **intersection** of arity facets; drop the class type. Do not convert to a union.
13. **Opaque bind:** if `TThis` is `object|null` / gradual `\Closure`, allow `bindTo` (cannot prove error). If `TThis` is a known class, `TNewThis` must be compatible; `null` this only from already-unbound; arrow / some FCC / internals → error when PHP forbids.
14. **`as` vs `is`:** v1 is `as` (compile-time assertion). Runtime `is Closure<callable<…>>` is Story 31 Idea 14 (reflection; PHP types only; would bump interop if a `tyhp/core` helper is used).
15. **Expression/PropertyPath** move to `TCallableShape` in this story (not a follow-up).
16. **No Story 15 bump** for type-system-only work.
17. **Generator declared return** is what the **caller** receives: `\Generator`, `\Generator<TKey, TValue>`, or `\Generator<K, V, S, R>` (or `iterable` / `\Iterator` / `\Traversable`). Never the `return` statement’s type (`TReturn` is `getReturn()` after exhaustion).
18. **Bare `\Generator`:** checker infers all four args from the body; callers see `\Generator<inferred…>`. Two-arg form pins `TKey`/`TValue` and infers `TSend`/`TReturn`. Four-arg form is fully explicit; body must match.
19. **`TSend`:** unused `yield $v;` → `mixed`. Typed target (`int $x = yield`, or `foo(yield)` with `foo(int $x)`) constrains `TSend` to that type. Several typed targets → **intersection** (conflicting `int` vs `string` is an error). Untyped `$x = yield` → `TSend = mixed`, `$x` is `mixed`, narrow before use.
20. **Traversable:** only `\Iterator` and `\IteratorAggregate` may `extends \Traversable`. User classes/enums must not list Traversable in `implements` (use Iterator or IteratorAggregate). `.tyhpdef` harvest may list both; do not flag stubs.
21. **Homogeneous ArrayAccess:** `$obj[$k]` is `TValue`; `$k` assignable to `TKey`; `$obj[] =` is `offsetSet(null, …)`. Struct/array `TKey` is an error. Foreach is not ArrayAccess. Native `\ArrayAccess` `offset*` always emit as PHP `mixed` (PHP contravariance).
22. **`#[\Tyhp\PhpType('…')]`:** string is a PHP type-hint spelling. Replaces the **emitted PHP type of the declaration it sits on** — parameter, function/method **return**, property, or typed constant (class or file-level). Usages stripped (like `#[\Tyhp\Php]`). Checker types unchanged. Implementors **inherit** the hint from the interface/abstract member they satisfy (do not require repeating the attribute on `Config::offsetGet`). Not on classes, enum cases, catch, locals, or property hooks (use the property or the `set` parameter). Constructor promotion: one attribute covers the parameter and the property (both flags).
23. **`ArrayAccessShape<TStruct extends struct>`** extends `ArrayAccess<__IndexKeys<T>, __IndexValueTypes<T>>`. Do not spell shape as `ArrayAccess<SomeStruct>`. Canonical source: `runtime/packages/core/tyhp_src/Contracts/ArrayAccessShape.tyhp`.
24. **Per-key exhaustiveness:** for `offsetGet` / `offsetSet`, if the method generic’s constraint is a **finite** literal union (struct keys), check the body **once per inhabitant** `K`. `$offset` is assumed `K`; reachable **value** returns must be `__IndexValueType<T, K>`. `K` is covered only if some reachable path returns a value of that type. `throw` / `never` does **not** cover a key. `match` is sufficient, not required. Infinite constraints (`string`): one envelope check, no per-key exhaustiveness. Call-site wide keys: dedicated diagnostic (not `never`). `$obj[] =` is an error on a shape.

---

## Type-system contract

This is the checker ↔ tyhpdef contract. Overlay signatures are authoritative; C# must not re-interpret Closure type arguments as return-last.

### Assignability

| Source | Target | Result |
|---|---|---|
| `\Closure<C, This, Scope>` | `callable` / `C` when `C` is a callable facet | Yes, via `__invoke` facet (after substitution) |
| `\Closure<C, …>` | `\Closure<D, …>` | Callable variance on `C`; **`TThis` / `TScope` invariant** |
| Callable facet (literal) | `\Closure` / `\Closure<C, …>` | Yes when the facet matches `C` (literals are Closure instances) |
| Untyped `callable` | `\Closure<…>` | No (Story 08: callable is wider) |
| Invokable object `MyInv` | `\Closure<…>` | No; use `fromCallable` |
| `\Closure<…>` | `MyInv` | No |

Optional parameters on `__invoke` still produce an **intersection** of arity facets on the object. `fromCallable` stores that intersection as `TCallableShape`, not a union.

### Creation (no explicit type arguments on values)

There is no `new \Closure<…>()`. Producers:

| Producer | `TCallableShape` | `TThis` | `TScope` |
|---|---|---|---|
| `function` / `fn` literal | signature of the literal | enclosing `$this` or `null` (`static fn`) | enclosing class or `null` |
| FCC `$obj->m(...)` | method signature | `typeof($obj)` | method’s class |
| FCC `Foo::m(...)` | method signature | `null` | `Foo` |
| FCC `foo(...)` | function signature | `null` | `null` |
| `fromCallable($cb)` | inferred from `$cb` | `__CallableThis<typeof($cb)>` | `__CallableScope<typeof($cb)>` |

Type **annotations** may write `\Closure<callable<int, string>>` (defaults fill `TThis` / `TScope`).

### `__CallableThis` / `__CallableScope`

| `TCallable` | `__CallableThis` | `__CallableScope` |
|---|---|---|
| `\Closure<C, This, Scope>` | `This` | `Scope` |
| Instance method / FCC `$obj->m(...)` | object type | that class |
| Static method / FCC `Foo::m(...)` | `null` | `Foo` |
| Function / FCC `foo(...)` | `null` | `null` |
| Invokable object | that class | that class |
| Bare `callable<…>` / untyped `callable` | `object\|null` | `__ClosureScope<object\|null>` |
| Union / intersection | distribute | distribute |

### fromCallable visibility (K)

At the call site, if the checker can see that `$callback` is not callable from `__CurrentScope` (e.g. another class’s `private` method), emit a compile error. Otherwise PHP `TypeError` at runtime. `__CurrentScope` is **not** copied onto the result’s `TThis`.

### Erasure

| Type | PHP |
|---|---|
| `\Closure<…>` | `\Closure` |
| `Fiber<…>` | `\Fiber` |
| `__CurrentScope` | `object\|null` (or omit in signatures that already erase) |
| `__CallableThis` / `__CallableScope` / `__SuperType` | resolved object / union |
| `__SuperTypeName` / `__ClassName` | `string` |
| `__IndexKeys<T>` | `string`, `int`, or `string\|int` (erased literals) |
| `__IndexValueType` / `__IndexValueTypes` | erased field types |
| `#[\Tyhp\PhpType('…')]` | usages stripped; PHP type is the constructor string |

---

## Canonical tyhpdef surface

Hand overlay: `runtime/packages/php/_tyhpdef/overlays/Ext.Core.tyhpdef`. Implementers must keep C# in sync with this file; do not fork a second Closure shape in `Types.cs`.

**`call`:** PHP requires a non-null object. Use `TNewThis extends object` (not `__ClosureThis`).

Locked Fiber / Closure shape (aliases + headers + methods):

```tyhpdef
type __ClosureThis = object|null;
type __ClosureScope<TThis extends __ClosureThis> =
    __SuperType<TThis>|__SuperTypeName<TThis>|null;

partial class Fiber<TResume = mixed, TCallableShape extends callable = callable>;

final partial class Closure<
    TCallableShape extends callable,
    TThis extends __ClosureThis = __ClosureThis,
    TScope extends __ClosureScope<TThis> = __ClosureScope<TThis>
>;
```

`bind` / `bindTo`: two overloads each — explicit `__ClosureScope<TNewThis>` → result `TNewScope`; `'static'` or omitted → result old `TScope` / `TOldScope`.

`fromCallable`:

```tyhpdef
static function fromCallable<TCallableShape extends callable>(
    TCallableShape $callback
): \Closure<TCallableShape, __CallableThis<TCallableShape>, __CallableScope<TCallableShape>>;
```

`getCurrent` (Closure, PHP ≥ 8.5): `\Closure<callable, __ClosureThis, __ClosureScope<__ClosureThis>>`.

`getCurrent` (Fiber): `?\Fiber<mixed, callable>`.

---

## Magic types

Register as built-in utilities (same pattern as `StructUtilityTypes` / `SymbolNameTypes`), except the two **aliases** which may stay in the overlay:

| Type | Kind | Behavior |
|---|---|---|
| `__SuperType<T>` | Utility | Object types: `T` and parent classes. `T` is `null` or `object` → `object` |
| `__SuperTypeName<T>` | Utility | Class-name strings of `T` and parents (brand like `__ClassName`). Inverse of `__CompatibleTypeName`. `T` is `null` or `object` → `__ClassName` |
| `__CurrentScope` | Utility (0-arity) | Lexical enclosing class/enum type; `null` at top-level; class type in instance **and** static methods |
| `__CallableThis<TCallable extends callable>` | Utility | See table above |
| `__CallableScope<TCallable extends callable>` | Utility | See table above |
| `__ClosureThis` | Overlay alias | `object\|null` |
| `__ClosureScope<TThis>` | Overlay alias | `__SuperType<TThis>\|__SuperTypeName<TThis>\|null` |
| `__IndexKeys<T extends struct>` | Utility | Union of `T`’s array keys as **literal** types (property names + `as` aliases) |
| `__IndexValueType<T extends struct, K>` | Utility | Field type of key `K`; **distributes** over a union `K` |
| `__IndexValueTypes<T extends struct>` | Utility | Union of all field types of `T` (ArrayAccess envelope `TValue`) |

TScope **checker rule** (in addition to the alias): `'static'` is illegal as a **stored** Closure type argument; it is only a bind argument. Object `$newScope`: require `TNewThis <: typeof($newScope)`. String `$newScope`: require `__SuperTypeName<TNewThis>` (not `__CompatibleTypeName`).

---

## Creation and inference

- Closure literals: infer `TCallableShape` from parameters/return (existing contextual typing); set `TThis` / `TScope` from enclosing object / `static fn`.
- FCC: same as PHP / `fromCallable` for the callable’s own `$this` and scope class (verified 2026-09-01 with `ReflectionFunction::getClosureThis()` / `getClosureScopeClass()`).
- Do not stamp the **caller’s** class onto `fromCallable` result `TThis`.

---

## bind / bindTo / call

**Overloads** (already in overlay): pick with `FunctionOverloadSelector`. `'static'` must not be assignable to `__ClosureScope<…>`.

**Extra checker rules** (overloads cannot express):

1. After `'static'` / omitted scope: `TNewThis` still compatible with leftover `TOldScope` (`$newThis instanceof` that scope).
2. Known non-rebindable (arrow functions, some FCC, internal-class `$newThis`): error.
3. Gradual `TThis`: allow bind.
4. `call`: `TNewThis extends object`; check `$newThis` against current `TThis` / `TScope` like a temporary bind; invoke with `__CallableParametersRest` / `__CallableReturnType`.

---

## Fiber

```tyhpdef
partial class Fiber<TResume = mixed, TCallableShape extends callable = callable>;
```

| Method | Types |
|---|---|
| `__construct` | `TCallableShape $callback` |
| `start` | `__CallableParametersRest<TCallableShape> ...$args`: `mixed\|null` |
| `resume` | `null\|TResume $value`: `mixed\|null` |
| `throw` | `\Throwable`: `mixed\|null` |
| `suspend` | `mixed\|null`: `mixed\|null` |
| `getReturn` | `__CallableReturnType<TCallableShape>` (throws at runtime if not terminated — do not add `null`) |
| `getCurrent` | `?\Fiber<mixed, callable>` |

Honest hole that is **accepted, not deferred:** `Fiber::suspend()` return vs `TResume`. `suspend` is static and can run from any helper serving one or many fibers. The return is `mixed`. Narrow before use. No CFA, no `__CurrentFiber`, no special case for `suspend` inside the `new Fiber` callback.

Generator `TSend` stays; `yield` is in the generator body. Fiber `suspend` is a static that can run in helpers.

---

## Generator

Layer 3 already has `Generator<TKey = mixed, TValue = mixed, TSend = mixed, TReturn = mixed>` plus `send(TSend): TValue` / `throw(): TValue`. This story makes the **checker** infer those args from generator functions and methods (`IsGenerator` is already set when the body contains `yield`).

A function containing `yield` is a generator (PHP). Its **declared** return type is the value of a **call** (`$g = foo()`), which is a Generator object — not the value of `return` inside the body (`$g->getReturn()`).

### Declared return (locked)

| Written return type | Checker |
|---|---|
| `\Generator` | Infer `\Generator<TKey, TValue, TSend, TReturn>` from the body; that inferred type is what callers see |
| `\Generator<TKey, TValue>` | Pin key/value; infer `TSend` / `TReturn` (defaults `mixed` until body fills them) |
| `\Generator<K, V, S, R>` | Fully explicit; body must be assignable to those args |
| `iterable` / `iterable<K, V>` / `\Iterator` / `\Traversable` (and generic forms) | Legal weaker **export** (Story 08 `4087`). Callers do not get `send` / `getReturn`. Inside the body, still infer a Generator shape for `yield` |

`function foo(): string { yield 1; return "done"; }` is an error (`4087`). Async functions still must not return Generator (`AsyncRule`).

Falling off the end without `return` is PHP `getReturn() === null` → inferred `TReturn` includes `null` (generators do not need `return` on every path; already skipped by `CheckerMissingReturnStatement`).

### Body inference (locked)

| Slot | From |
|---|---|
| `TValue` | Union of `yield $v` / `yield $k => $v` values |
| `TKey` | Union of explicit keys; `yield $v` (no key) contributes `int` (PHP sequential keys) |
| `TReturn` | Union of `return $x` values in the generator; plus `null` if any path falls off without `return` |
| `TSend` | See below |

**`TSend`** is the value of a `yield` **expression** (`$x = yield`, `foo(yield)`, …) — i.e. `Generator::send()`. Not `TValue`.

| Site | `TSend` |
|---|---|
| `yield $v;` only (value unused) | `mixed` (`send()` discarded) |
| `int $x = yield` / `int $x = yield $v` | `TSend` assignable to `int` |
| Several typed targets | **Intersection** of those types; empty intersection → error |
| `$x = yield` with no declared type (first assignment) | `TSend = mixed`, `$x` is `mixed`; narrow before use |
| `foo(yield)` with `foo(int $x)` | Same as a typed target |

Do not infer `TSend` from later uses of an untyped `$x` (no Hindley–Milner).

**`yield from $inner`:** operand must be iterable/`Generator` (`4089`). Merge inner `TKey`/`TValue`. Outer `TSend` must be a legal `send()` into the inner (contravariant). The `yield from` **expression** is the inner `TReturn` (already in `TypeInferrer.InferYieldFromResult`); arrays / non-Generator Traversables evaluate to `null`.

**Call sites:** `$g = foo()` has the inferred `\Generator<…>`; `$g->send($x)` / `foreach` / `getReturn()` use overlay members + existing Traversable foreach (`GenericInheritanceBindings`).

Story 08 already validates yield-outside-generator, yield-in-finally, and yield-from-non-iterable. This story adds generic inference and declared-vs-inferred matching.

---

## Traversable implements

Full PHP-hooks wording: `dev-docs/PHP_LANGUAGE_HOOKS.md` (Traversable implements).

Only `\Iterator` and `\IteratorAggregate` may `extends \Traversable`. User classes/enums must not list `\Traversable` in `implements` — they implement Iterator or IteratorAggregate (Traversable is inherited). PHP 8 also rejects `implements \Iterator, \Traversable` as a duplicate.

`.tyhpdef` / `#[\Tyhp\Php]` harvest often lists both; **do not** diagnose stubs. Traits must not require Traversable; require Iterator or IteratorAggregate.

This is `DeclarationRule` only. Implement as **Phase 0** (can ship before Closure).

---

## ArrayAccess

Full inventory: `dev-docs/PHP_LANGUAGE_HOOKS.md`.

Layer 2 harvest already has `offsetExists` / `offsetGet` / `offsetSet(null|TKey, …)` / `offsetUnset` with `TKey`/`TValue`. Layer 3 only adds generic **defaults** and drops harvest’s extra `null` on `offsetGet` (`null|TValue` → `TValue`), same as `WeakMap::offsetGet` / `Iterator::current`.

Do not copy the other three methods into Layer 3.

- `$obj[$k]` → `TValue`; `$k` assignable to `TKey`.
- `$obj[] = $v` → Layer 2 `offsetSet(null, $v)` (`null|TKey`).
- Not array / string / ArrayAccess → **4093** (emit it; code already allocated).
- `TKey` that is a struct or `array` → error (not a legal PHP offset).
- `foreach` does **not** use ArrayAccess (existing Traversable path).

**PHP `ArrayAccess` is `mixed`.** Implementing `offsetGet(string $offset): User` fatals against `offsetGet(mixed $offset)`. Emitter: any method that implements `\ArrayAccess::{offsetExists,offsetGet,offsetSet,offsetUnset}` spells those params (and `offsetGet`’s return) as PHP `mixed`, as if `#[\Tyhp\PhpType('mixed')]` were present. Authors do not have to stamp the attribute on every homogeneous implementor.

Do not special-case `ArrayAccess<SomeStruct>` as a shape (collides with `TKey` + default `TValue`). Use `ArrayAccessShape`.

---

## PhpType

`runtime/packages/core/tyhp_src/PhpType.tyhp` (+ `package.tyhpdef`). Compile-time hint; **usages stripped** on emit (the `PhpType` class itself still compiles, like `Php`).

PHP type declarations exist on parameters, returns, properties, and typed constants. `PhpType` targets all of those (`FUNCTION` / `METHOD` / `PARAMETER` / `PROPERTY` / `CLASS_CONSTANT` / `CONSTANT`). Closures and arrow functions are `FUNCTION` (return) plus their parameters.

```tyhp
#[\Attribute(
    \Attribute::TARGET_FUNCTION
    | \Attribute::TARGET_METHOD
    | \Attribute::TARGET_PARAMETER
    | \Attribute::TARGET_PROPERTY
    | \Attribute::TARGET_CLASS_CONSTANT
    | \Attribute::TARGET_CONSTANT
)]
final class PhpType {
    public function __construct(public readonly string $type): void {}
}
```

| Placement | Effect on emit | Checker |
|---|---|---|
| Parameter (function, method, closure, property-hook `set`) | That parameter’s PHP type is `$type` | Unchanged |
| Function or method (including closures) | **Return** PHP type is `$type` (params unchanged unless they have their own attribute) | Unchanged |
| Property (including hooked and constructor-promoted) | That property’s PHP type is `$type` | Unchanged |
| Typed constant (class / interface / trait / enum, or file-level when emit writes a type) | That constant’s PHP type is `$type` | Unchanged |

`$type` is a PHP type-hint spelling (`mixed`, `int`, `string|int`, `?\Foo`). Invalid spelling → compile error at the attribute.

**Not a type-declaration site** (compile error if `PhpType` is used there): class / interface / trait / enum (including enum backing type), enum cases, catch types, local variables (Tyhp-only; no PHP hint), property **hooks** (`get`/`set` — put it on the property or on the `set` parameter).

Constructor promotion: the attribute sits on one declaration that is both a parameter and a property. Because `PhpType` includes both `TARGET_PARAMETER` and `TARGET_PROPERTY`, one stamp replaces both emitted types.

Implementing a method or property **inherits** `PhpType` from the interface/abstract member it satisfies, so `ArrayAccessShape` stamps it once.

Not a refined-type `emit` member (that stays `DESIGN_OPEN_QUESTIONS.md` with refined types).

---

## ArrayAccessShape

Canonical: `runtime/packages/core/tyhp_src/Contracts/ArrayAccessShape.tyhp` (sketch already on disk). Do **not** list it in `package.tyhpdef` until Phase 6d — `__IndexKeys` / `__IndexValueType` / `__IndexValueTypes` must exist first or the package will not load. `PhpType` is already in `package.tyhpdef`.

```tyhp
interface ArrayAccessShape<TStruct extends struct>
    extends \ArrayAccess<__IndexKeys<TStruct>, __IndexValueTypes<TStruct>>
{
    public function offsetExists(
        #[\Tyhp\PhpType('mixed')] __IndexKeys<TStruct> $offset,
    ): bool;

    #[\Tyhp\PhpType('mixed')]
    public function offsetGet<TKey extends __IndexKeys<TStruct>>(
        #[\Tyhp\PhpType('mixed')] TKey $offset,
    ): __IndexValueType<TStruct, TKey>;

    public function offsetSet<TKey extends __IndexKeys<TStruct>>(
        #[\Tyhp\PhpType('mixed')] TKey $offset,
        #[\Tyhp\PhpType('mixed')] __IndexValueType<TStruct, TKey> $value,
    ): void;

    public function offsetUnset(
        #[\Tyhp\PhpType('mixed')] __IndexKeys<TStruct> $offset,
    ): void;
}
```

`.tyhp` interfaces cannot overload; this is the implementor surface (one method per name). `$obj[$k]` is `offsetGet`. Wide `string`/`int`/`mixed` keys at the call site → dedicated diagnostic (narrow or `as`). `$obj[] =` → error. `offsetExists` on a schema key is `bool` (sparse). Envelope `ArrayAccess<Keys, ValueUnion>` is what you get if the static type is only `ArrayAccess`.

**Body check (offsetGet / offsetSet):** `TKey extends __IndexKeys<T>` is a finite literal union. For each inhabitant `K`:

1. Assume `$offset : K`.
2. Walk the body with existing narrowing (`if`, `match`, helpers, …). `match` is not required.
3. Every reachable **value** `return` must be assignable to `__IndexValueType<T, K>` (offsetGet) / `$value` writes to that field type (offsetSet).
4. `K` is **covered** only if a reachable path **returns a value** of that type (offsetGet) or performs a typed write (offsetSet).
5. `throw` / `never` does **not** cover `K`.
6. Missing coverage → compile error (unhandled key).

If the constraint is not a finite literal/enum union, check the body once against the envelope (`__IndexValueTypes<T>`). Same instantiation rule for later methods whose input generic determines the return (`__IndexValueType<T, K>` and siblings).

`offsetExists` / `offsetUnset`: `$offset` is `__IndexKeys<T>` only; no per-key value map. Integer vs string `'0'` vs `0` follows the struct’s `as` aliases. Unset of a required struct field is allowed (sparse PHP). `ArrayAccessShape<T>` is a subtype of its envelope via `extends`; `TStruct` is **invariant** (reads and writes). No extra widening to `ArrayAccess<string, mixed>`.

---

## Expression / PropertyPath

Drop `ResolveExpressionCallableArityArguments` and docs that `Expression<R>` / `Expression<T1,T2,R>` are extra class type arguments.

Target shape (names may match existing `T` on builders):

```tyhp
class Expression<TCallableShape extends callable> {
    public readonly \Closure<TCallableShape> $callable;
    public function __invoke(...): __CallableReturnType<TCallableShape>;
    public function compile(): \Closure<TCallableShape>;
}
class PropertyPath<TCallableShape extends callable> extends Expression<TCallableShape> { … }
class ExpressionBuilder<T extends object> extends Expression<callable<T, bool>> { }
class PropertyPathBuilder<T extends object> extends PropertyPath<callable<T, mixed>> { }
```

Use-site: `Expression<User, string>` → `Expression<callable<User, string>>`. Contextual `fn` at an Expression parameter still maps through the callable facet of `TCallableShape` (existing Story 16 wiring, retargeted).

Files: `runtime/packages/lambda/tyhp_src/Expression.tyhp`, `PropertyPath.tyhp`, `ExpressionBuilder.tyhp`, `PropertyPathBuilder.tyhp`, `package.tyhpdef`, checker `PropertyPathSupport` / `ExpressionTreeSupport` / `GenericInheritanceBindings`, tests, `docs/content/tyhp_3000_parsableLambdas.md`.

---

## Breaking use-site migration

| Old | New |
|---|---|
| `\Closure<int, string>` | `\Closure<callable<int, string>>` |
| `\Closure<void>` | `\Closure<callable<void>>` |
| `Expression<User, string>` | `Expression<callable<User, string>>` |
| `Expression<string>` (zero args) | `Expression<callable<string>>` |
| `__CallableReturnType<\Closure<string, int>>` | `__CallableReturnType<\Closure<callable<string, int>>>` |

Update tests (`CallReturnTypeInferenceTests`, `CallableSignatureUtilityTests`, GenericObject emitter, …), Story 06/08 wording in **user** docs (`tyhp_0150_newTypes.md`, `quickref.md`), AIDevGuide. Do not leave a compatibility parse for the old Closure spelling.

---

## Interop contract

- **This story:** no `interopContractVersion` bump. Emit still uses PHP `\Closure` / `\Fiber`. PropertyPath still extracts `$expr->callable` where a Closure is required. `#[\Tyhp\PhpType]` usages are stripped (same as `#[\Tyhp\Php]`).
- **Story 31 Idea 14:** if a `tyhp/core` helper implements runtime `is Closure<…>`, that **does** bump the interop contract (Story 15 rules).

---

## Phases

### Phase 0 — Traversable implements (can land first)

- `DeclarationRule`: only Iterator / IteratorAggregate may extend Traversable; user classes/enums/traits must not list Traversable; skip tyhpdef harvest.
- Tests: user class `implements Traversable` errors; `implements Iterator` OK; `interface Foo extends Traversable` errors; Generator stub does not error.

### Phase 1 — Strip Closure return-last

- `GenericTypeArgumentValidator.IsBuiltInCallable`: `callable` only, not `Closure`.
- `CallableArityFacetBuilder`: stop mapping `GenericCheckedType` Closure args as params+return; use `__invoke` / `TCallableShape`.
- `SatisfiesCallableConstraint`: generic `\Closure<C, …>` satisfies callable when `C` does (or via `__invoke`).
- Tests that used `\Closure<int, string>` as return-last must migrate or they become arity/constraint errors.

### Phase 2 — Register magic types

- `UtilityBehavior` + `StructUtilityTypes` / `SymbolNameTypes` (or a small new registrar) for `__SuperType`, `__SuperTypeName`, `__CurrentScope`, `__CallableThis`, `__CallableScope`, `__IndexKeys`, `__IndexValueType`, `__IndexValueTypes`.
- Resolver implementations + erasure in `TypeSpellingHelper`.
- Docs in `docs/content/tyhp_0150_newTypes.md`.

### Phase 3 — Overlay + `call()`

- Keep Layer 3 as source of truth.
- `call<TNewThis extends object>(...)`.
- Confirm overlay merge of Closure generic header (Story 21 `ApplyOverlayPartialTypeHeader`).

### Phase 4 — Inference

- Literals, FCC, `fromCallable` using `__CallableThis` / `__CallableScope`.
- `__CurrentScope` filling at signatures / fromCallable visibility.

### Phase 5 — bind / bindTo / call rules

- Overload pick + leftover-scope `instanceof` + object vs name `$newScope` + non-rebindable + internal classes.

### Phase 6 — Fiber

- Overlay already drafted; checker must not invent `TSuspend`. `getCurrent` arity is two params (`TResume`, `TCallableShape`).

### Phase 6b — Generator inference

- After visiting a generator body, compute `TKey` / `TValue` / `TSend` / `TReturn` as above.
- If the declared return is bare `\Generator`, substitute the inferred generic type as the function’s effective return (callers and `ExpectedReturnType` for nested yield).
- If two- or four-arg `\Generator<…>` is written, check body inference against those pins (`4087` or a dedicated mismatch code).
- `return` in a generator checks against `TReturn`, not the export `Generator` type.
- Typed `yield` expression sites constrain `TSend`; untyped `$x = yield` stays mixed.
- `yield from` merges inner Generator args; keep `4089`.
- Closures/methods with `yield` use the same rules (`IsGenerator` already set in the binder).

### Phase 6c — Homogeneous ArrayAccess

- Keep Layer 3 to defaults + `offsetGet(): TValue` only (Layer 2 already has the other `offset*` methods).
- `InferArrayAccess` / assign / `isset`/`unset` use `TKey`/`TValue` (same inheritance-binding idea as foreach).
- Emit **4093**. Reject struct/array `TKey`. `$obj[] =` uses `null` offset.
- Spell `\ArrayAccess` `offset*` as PHP `mixed` (native interface).

### Phase 6d — PhpType + ArrayAccessShape

- Compile `PhpType` (`tyhp/core`; already in `package.tyhpdef`). Honor on every PHP type-declaration site (parameter, function/method return, property, typed constant). Strip usages. Inherit onto implementations. Reject on class / enum case / catch / local / property hook.
- Register index utilities, then add `ArrayAccessShape` to `package.tyhpdef`. `$obj[$k]` uses `offsetGet`. Call-site key diagnostic. No append.
- Per-key body instantiation for `offsetGet` / `offsetSet` (finite keys). `never` does not cover.

### Phase 7 — Expression / PropertyPath

- Source + checker special-case removal + interop emit still `\Closure` with inferred `TCallableShape`.

### Phase 8 — Docs, AIDevGuide, diagnostics

- [x] User docs: Closure / Fiber / Generator declared-return shapes / magic types / when to write `callable` vs `\Closure`; Traversable cannot be implemented directly; `ArrayAccess<K,V>` indexing; `ArrayAccessShape`; `#[\Tyhp\PhpType]`.
- [x] Keep `PHP_LANGUAGE_HOOKS.md` in sync when a row’s status changes.
- [x] `as` not `is` for Closure/Fiber type arguments.
- [x] Allocate `MessageCode` values in `MessageCode.cs` + both `.resx` files (bind incompatibility, fromCallable visibility, `'static'` stored as TScope, etc.).
- [x] Type-system contract: this story + a short pointer from `docs/content/tyhpdef_classes.md` / `tyhp_0150_newTypes.md` (reader-relevant; no grill history).

### Phase 9 — Conformance / golden tests

- [x] See acceptance list below.

---

## Out of scope

- Variadic type parameters; omitting generator return types; WeakMap/WeakReference Layer 3 header generics (Layer 2); `.tyhp` interface overloads.
- Runtime `is Closure<…>` — **Story 31 Idea 14**.
- Refined-type `emit` member — `DESIGN_OPEN_QUESTIONS.md` (refined types). `PhpType` is v1.
- Stringable auto-implement; `list()` / named ArrayAccess destructure — **Story 21.7**.
- `tyhpdef/php` Layer 3 overlay completeness — **Story 21.8**.
- Fiber CFA / `__CurrentFiber` / dual Closure spelling — **rejected** in `DECISIONS.md` (not future work).

---

## Cross-Story References

| Story | Relationship |
|---|---|
| 06 / 08 | Closure return-last **docs** superseded for the **class**; `callable` convention unchanged. Generator `4087` / yield 4086–4089 reused; this story adds generic inference |
| 08.5 | `__CompatibleTypeName` stays (descendants); this story adds ancestor `__SuperTypeName` and index utilities `__IndexKeys` / `__IndexValueType` / `__IndexValueTypes` |
| 16 | Expression arity special-case removed here |
| 16.5 | Callable utilities + overload pick reused |
| 21 | Overlay load; `Ext.Core` Layer 3 is the Closure/Fiber/Generator/ArrayAccess contract |
| PHP_LANGUAGE_HOOKS | Audit inventory |
| 15 | No version bump; Idea 14 would bump |
| 31 Idea 14 | Runtime `is` via reflection |
| 21.7 | ArrayAccess `list()` / named destructure; Stringable auto-implement |
| 21.8 | Remaining `tyhpdef/php` Layer 3 overlays (array generics, SPL remaps, DateTime operators); FOUND_BUGS #6 / #8 / #10 |
| 30 | User-facing polish if this lands first |

---

## Golden Fixtures / Tests (Acceptance)

- [x] `\Closure<callable<int, string>>` is a Closure with `__invoke` facet takes `int` returns `string`.
- [x] `\Closure<int, string>` is **not** return-last (constraint / arity error or `TCallableShape` fails `extends callable`).
- [x] Bare `\Closure` remains gradual (not `Closure<callable<void>, …>`).
- [x] `fn (int $x): string => …` infers `\Closure<callable<int, string>, …>` with enclosing `TThis`.
- [x] `static fn` has `TThis = null`.
- [x] FCC `$obj->m(...)` / `fromCallable([$obj, 'm'])`: `TThis` is `typeof($obj)`, not the caller class.
- [x] `fromCallable('strlen')` / function FCC: `TThis` and `TScope` null.
- [x] `fromCallable([A::class, 'staticMethod'])`: `TThis` null, `TScope` is `A`.
- [x] Invokable `fromCallable`: intersection of arities, not union, class dropped.
- [x] Private method `fromCallable` from another class: compile error when visible to the checker.
- [x] `bindTo($x)` / `bindTo($x, 'static')`: result `TScope` unchanged; `'static'` not stored.
- [x] `bindTo($x, Parent::class)` / parent instance: allowed when `TThis <: Parent`.
- [x] `bindTo` unrelated class: error when `TThis` is a known class; allowed when gradual.
- [x] Arrow `bindTo`: error.
- [x] `call(null)`: error (`TNewThis extends object`).
- [x] `$fiber->resume(1)` type-checks against `TResume`; `Fiber::suspend()` return is `mixed`.
- [x] `Fiber::getCurrent()` is `?\Fiber<mixed, callable>`, not three type args and not default `callable<void>` only.
- [x] `Expression<callable<User, string>>` contextual `fn`; old `Expression<User, string>` as two-arg return-last **fails** or is migrated in tests.
- [x] `__SuperType<Dog>` accepts `Animal` objects; `__SuperTypeName<Dog>` accepts `'Animal'`; `__CompatibleTypeName<Dog>` still accepts `'Puppy'`, not `'Animal'`.
- [x] `__CurrentScope` inside instance method is the class; inside static method is the class (not `null`); top-level is `null`.
- [x] Emit: Closure/Fiber/magic types erase; no `interopContractVersion` change.
- [x] Existing `__invoke` classes other than Closure still get callable facets (no regression).
- [x] `function foo(): \Generator { yield 1; }` — callers see `\Generator<int, int, mixed, null>` (or equivalent; unused TSend mixed; fall-off TReturn null).
- [x] `function foo(): string { yield 1; }` — `4087`.
- [x] `function foo(): \Generator<int, string> { yield "a"; }` — OK; `yield 1` — mismatch on TValue.
- [x] `int $x = yield;` → `send(int)` at call sites; `$x = yield;` untyped → mixed, narrowing required to use `$x`.
- [x] `int $a = yield; string $b = yield;` — TSend intersection error.
- [x] `return "done"` in a generator contributes `TReturn = string` (and `null` if another path falls off).
- [x] `yield from` of `\Generator<int, string, mixed, bool>` merges keys/values; expression type is `bool`.
- [x] User `class C implements \Traversable` — error; `class C implements \Iterator` — OK; `interface I extends \Traversable` — error; `interface I extends \Iterator` — OK.
- [x] Harvested `final class Generator implements \Iterator, \Traversable` — no diagnostic.
- [x] `ArrayAccess<string, User>`: `$o[$k]` is `User` when `$k` is `string`; `$o[] = $u` OK; `$not[$k]` on a plain class — 4093.
- [x] `ArrayAccess<ConfigStruct, mixed>` (struct as TKey) — error.
- [x] `implements ArrayAccess<string, User>` emits `offsetGet(mixed $offset): mixed` (not `string` / `User`).
- [x] `ArrayAccessShape<ConfigMap>`: `$o['host']` is `string`; `$o[$s]` with `string $s` — key diagnostic; `$o[] =` — error.
- [x] `offsetGet` body covering only `'host'` when the struct also has `'port'` — unhandled-key error; `throw` in the `'port'` path does not cover.
- [x] `#[\Tyhp\PhpType('mixed')]` on a parameter/return/property/class constant is stripped; PHP hint is `mixed`; checker still uses the Tyhp type. Promoted ctor param covers both param and property. `PhpType` on a class or property hook is an error.
- [x] Implementor of `ArrayAccessShape` emits `offsetGet(mixed $offset): mixed` without repeating `PhpType` on the class method.
