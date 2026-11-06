# Implementation Plan: Story 32 — Scalar-Conversion Contracts (`*Convertible`, Stringable-style casts)

> **Roadmap position:** Story 32 — **Tier 3 — Advanced**, after Story 34 and before Story 41 (moved from Tier 4 on
> 2026-11-04: shipping it after the first stable release would change what existing casts do at runtime).
> **Direct dependencies (new numbering):** 06, 08, 09, 11, 15
> **New story:** split from Story 31 Idea 2 on 2026-11-03. No legacy number.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`; new
> `tyhp.json` keys proposed here are provisional and must be registered in `CONVENTIONS.md` §4 when the story is
> scheduled. See `ROADMAP.md` for the full tiered sequence.

> **Documentation requirement.** When scheduled, this story must carry a user documentation & AIDevGuide
> phase: the `tyhp-docs-src/content/` pages that describe the shipped surface, plus the affected
> `tyhp-ai-dev-guide` `guide/` and `handbook/` sections *and* the matching item in its `REGEN.md` (the guide is
> regenerated from that prompt, so a change missing from it is reverted by the next regeneration). This story
> changes what an author writes, so `guide/28-availability-gotchas.md` must be updated too: its
> "not in the language yet" list is what an agent trusts to decide whether a construct is usable.

> **Prerequisites:** Story 06 (built-in types & cast tokens), Story 08 (checker / type inference), Story 09/11
> (emitter expression lowering), Story 15 (interop contract — the emitter may only call the listed `\Tyhp\*`
> runtime surface). Ties into the existing `operator convert` mechanism (Decimal) — see Decision 5.
> Native `\Stringable` from `__toString()` (no written `implements`) is **Story 21.7**, not this story.

## Summary

A set of runtime contracts — `\Tyhp\Contracts\StringConvertible` (extends native `\Stringable`), `IntConvertible`,
`FloatConvertible`, `BoolConvertible`, and `\Tyhp\Contracts\DecimalConvertible` — that let an object define how it
converts to a scalar, plus a **compiler rule that rewrites cast expressions to call the contract method** when the
operand implements the matching interface. This mirrors how PHP natively routes string context through
`\Stringable::__toString()`, and generalizes it to `int`/`float`/`bool`/`decimal`, which PHP has **no** native hook
for.

| Cast token | Contract | Method | Fallback when the operand does **not** implement it |
|------------|----------|--------|------------------------------------------------------|
| `(string)` (`T_STRING_CAST`) | `StringConvertible` *(extends `\Stringable`)* | `__toString(): string` | native `(string)` |
| `(int)` (`T_INT_CAST`) | `IntConvertible` | `__toInt(): int` | native `(int)` |
| `(float)` (`T_DOUBLE_CAST`) | `FloatConvertible` | `__toFloat(): float` | native `(float)` |
| `(bool)` (`T_BOOL_CAST`) | `BoolConvertible` | `__toBool(): bool` | native `(bool)` |
| `(decimal)` (`T_DECIMAL_CAST`) | `DecimalConvertible` | `__toDecimal(): \Tyhp\Decimal` | `\Tyhp\decimal($x)` — already dispatches on the contract internally |

> **Where this lives today.** Casts are lexed to the `T_*_CAST` tokens, typed in
> `Tyhp/TyhpLang/Checker/TypeInferrer.Expressions.cs` (+ `Checker/Rules/CodeQualityRule.cs`), and emitted verbatim
> as native PHP casts by `TyhpEmitter.BuildUnaryExpression` (`Tyhp/TyhpLang/Emitter/TyhpEmitter.Expressions.cs`).
> `(decimal) $x` already lowers to `\Tyhp\decimal($x)`, and `\Tyhp\decimal()` / `Decimal::__construct` already do
> `$value instanceof DecimalConvertible ? $value->__toDecimal() : …`. This story adds the *general* interface-driven
> rewrite for the other four casts and formalizes the decimal case.

## Motivation

PHP gives `\Stringable` special engine treatment: any object used in a string context (cast, concat, interpolation,
`echo`, `%s`, string-typed parameters in weak mode) has `__toString()` called automatically. There is no equivalent
for `int`/`float`/`bool`: `(int) $obj` on an arbitrary object emits a **warning and yields `1`**, `(float) $obj`
yields `1.0`, `(bool) $obj` is **always `true`**, and `(string) $obj` without `__toString` is a fatal `Error`. So
today a Tyhp value object (money, a typed ID, a measurement) cannot participate in casts the way a native scalar can.

The `*Convertible` contracts close that gap: a class opts in by implementing the interface, and every matching cast
site calls the method instead of the lossy/loud native cast.

## Emission model (the core rewrite)

For a cast `(T) $expr`, the emitter chooses one of three lowerings from the operand's **static type** (`T` = the
target scalar, `IFace` = the matching contract, `m()` = the method):

1. **Statically implements the contract** → direct call, no guard:

```php
$expr->__toInt()
```

2. **Statically a scalar / provably cannot implement it** (`int`, `string`, `float`, `bool`, `array`, `null`, an
   enum, a `final` class that does not implement the contract, …) → **native cast unchanged**:

```php
(int) $expr
```

3. **Unknown / `mixed` / `object` / a union that *might* implement it** → **runtime guard** (assign to a temp first
   so a side-effecting operand is evaluated once):

```php
(($__c = $expr) instanceof \Tyhp\Contracts\IntConvertible ? $__c->__toInt() : (int) $__c)
```

Special cases:

- **`(string)`** — because `StringConvertible extends \Stringable` and PHP already routes `(string)` (and every
  implicit string context) through `__toString()`, the explicit `(string)` cast needs **no rewrite**; the contract
  exists for *type* parity (using it as a type / `instanceof`) and to make the string case symmetric with the others.
- **`(decimal)`** — not a real PHP cast; keeps lowering to `\Tyhp\decimal($expr)`, which already dispatches on
  `DecimalConvertible` at runtime. No separate `instanceof` guard is emitted (avoids double-dispatch), and this keeps
  the emitter within the Story 15 interop surface (`\Tyhp\decimal` is already listed).
- **Nullable operands (`?Foo`)** — the guard's `else` branch (native cast) already reproduces PHP's null-cast
  semantics (`(int) null === 0`, `(string) null === ''`, `(bool) null === false`, `(float) null === 0.0`), so no
  extra null handling is needed; `instanceof` is false for `null`.
- **Temp-variable hygiene** — the guard must use a fresh, collision-free temp (emit-scoped counter) and only when the
  operand is not already a simple variable; a bare `$var` operand can be referenced twice safely.

## What else `\Stringable` does that we would need to emulate

Direct answer to the design question. Ordered from "already covered by requirement 1" to "larger follow-on scope":

1. **Implicit-context coercion, not just explicit casts (the big one).** PHP calls `__toString()` in *every* string
   context, not only `(string)`: concatenation (`.`), interpolation (`"$x"` / `"{$x}"`), `echo`/`print`,
   `sprintf('%s')`, and `string`-typed parameters/returns in weak mode. Full parity for the numeric/bool contracts
   would mean firing them in the analogous implicit contexts:
   - arithmetic operators and comparisons (numeric context),
   - boolean contexts: `if`/`while` conditions, `!`, `&&`/`||`, ternary condition, `empty()`,
   - `int`/`float`/`bool`-typed parameters and returns in weak mode.
   This is the expensive part (the checker must *inject* conversions at coercion points, not just rewrite an explicit
   token) and is a **scope decision**. Recommendation: **Phase 1 = explicit casts only** (matches requirement 1 and is
   fully deterministic); implicit contexts are a Phase 2 follow-on. Note the asymmetry: `(string)` /
   `StringConvertible` gets implicit string contexts **for free** from the engine because `\Stringable` is native;
   `int`/`float`/`bool` do **not**, so implicit-context parity for those is purely a compiler feature.

2. **Implicit interface satisfaction from the magic method.** PHP auto-implements `\Stringable` for any class that
   declares `__toString()` — you never write `implements \Stringable`. Decision: do we treat declaring
   `__toInt()`/`__toFloat()`/`__toBool()`/`__toDecimal()` as *implicitly* implementing the corresponding contract
   (checker synthesizes the `implements`)? Recommendation: **yes**, for parity and ergonomics — the checker adds the
   `implements` so `instanceof`, type acceptance, and the cast rewrite all "just work", matching how the engine treats
   `__toString`.

3. **First-class use as a type + `instanceof`.** `\Stringable` works as a parameter/return/property type and in
   `instanceof`. The contracts must be equally first-class: `function f(int|IntConvertible $x)`,
   `$x instanceof FloatConvertible`, `?BoolConvertible`, etc. This mostly falls out of them being ordinary interfaces,
   but the checker's cast/coercion rules must **accept `T|TConvertible` wherever a bare `T` is accepted**.

4. **Conversion methods may throw.** Since PHP 7.4 `__toString()` may throw (older PHP made it a fatal error). The
   contract is: conversion methods **may throw**, and the emitted guard must let the exception propagate (never
   swallow it). Only relevant if we ever targeted PHP < 7.4, which the version floor rules out.

5. **Return-type enforcement.** PHP enforces `__toString(): string`. The contracts already type their returns
   (`: int`, `: float`, `: bool`, `: \Tyhp\Decimal`); the checker should enforce those on implementers and the
   emitter should trust them (no re-coercion) — except decimal, whose method returns a `\Tyhp\Decimal` *object*, not
   a scalar.

6. **Conversion helper functions (`strval`/`intval`/`floatval`/`boolval`, `settype`).** `strval()` already honors
   `\Stringable` natively. For parity the compiler could rewrite `intval($obj)`/`floatval($obj)`/`boolval($obj)` /
   `settype($obj, …)` to route through the contracts with the same guard. Recommendation: **out of scope for Phase 1**;
   listed as a parity follow-on (identical guard, different call sites).

7. **Precedence vs. `operator convert`.** Tyhp already has `operator convert(self $v): int` (Decimal defines
   convert-to-scalar operators, emitted via `OperatorMethodNameGenerator` / `AliasConverter`). A type could have
   **both** a `convert`-to-scalar operator and a `*Convertible` contract. Recommendation: at a **cast site the
   contract method wins** (it is the explicit, PHP-parity affordance); alternatively make the two mutually exclusive
   with a checker diagnostic. **Flag for confirmation when scheduled.**

8. **Object-target native fallback is intentionally lossy.** For the guard's `else` branch, remember native
   `(int) $obj` / `(float) $obj` on a contract-less object **warns and yields `1`/`1.0`**, `(bool) $obj` is always
   `true`, and `(string) $obj` without `__toString` is a fatal `Error`. That is faithful PHP behavior — the contract
   is the *fix*. The checker **may** additionally warn when a cast targets an object type that implements neither the
   contract nor the native affordance (a likely bug).

## Decisions (defaults chosen)

1. **Scope → explicit casts only in Phase 1** (Behavior 1). Implicit-context coercion is a Phase 2 follow-on.
2. **`(string)` → no rewrite** (native `\Stringable` already covers it); the contract exists for typing/parity.
3. **`(decimal)` → keep the `\Tyhp\decimal($x)` lowering** (already dispatches on `DecimalConvertible`); no extra guard.
4. **Magic method → implicit `implements`** (Behavior 2): declaring the method synthesizes the contract in the checker.
5. **Precedence → contract method wins at a cast site** over `operator convert` (Behavior 7); flag for confirmation.
6. **Helper functions (`intval`/…/`settype`) → out of scope for Phase 1** (Behavior 6).

## Risks & Edge Cases

| # | Issue | Mitigation |
|---|-------|------------|
| 1 | Double evaluation of a side-effecting operand under the runtime guard | Bind the operand to a fresh emit-scoped temp; skip the temp when the operand is a bare variable |
| 2 | Overlap / ambiguity with `operator convert` to a scalar | Decision 5: contract wins at cast sites; consider a checker diagnostic for the both-defined case |
| 3 | `(string)` double-handling (rewrite *and* native `__toString`) | Decision 2: never rewrite `(string)`; rely on native `\Stringable` |
| 4 | Decimal double-dispatch (guard + `\Tyhp\decimal` both testing the contract) | Decision 3: emit only `\Tyhp\decimal($x)`; let the runtime function dispatch |
| 5 | Interop surface violation (emitter calling a non-approved `\Tyhp\*` symbol) | Only `\Tyhp\decimal` and the `\Tyhp\Contracts\*` interfaces are referenced; register with Story 15 |
| 6 | Contract method throwing inside the ternary guard | Guard lets exceptions propagate; documented as allowed (Behavior 4) |
| 7 | Nullable/`mixed` operands producing wrong null-cast semantics | `instanceof` is false for `null`; `else` branch reproduces native null-cast behavior |
| 8 | Scope creep into implicit contexts before the checker can inject coercions safely | Gate implicit-context parity behind Phase 2; Phase 1 is token-local and deterministic |

## Golden Fixtures / Tests (Acceptance)

Per `CONVENTIONS.md` §5, add `.tyhp → .php` (+ expected-diagnostics) golden fixtures under
`tests/conformance/story31/convertable/` with a `manifest.json` the runner asserts.

- **Static direct call:** operand statically implements `IntConvertible` → `(int) $x` emits `$x->__toInt()`.
- **Static native passthrough:** operand statically `int`/`string`/enum/`final` non-implementer → native cast unchanged.
- **Runtime guard:** operand `mixed`/`object`/`Foo|int` → ternary `instanceof` guard with a single-evaluation temp.
- **`(string)`:** never rewritten; native `(string)`/implicit string contexts still call `__toString` via `\Stringable`.
- **`(decimal)`:** lowers to `\Tyhp\decimal($x)`; a `DecimalConvertible` implementer round-trips through `__toDecimal`.
- **Implicit `implements`:** a class declaring `__toFloat()` (no explicit `implements`) satisfies `instanceof FloatConvertible`.
- **Precedence:** a type with both `operator convert(self): int` and `IntConvertible` uses the contract at the cast site.
- **Throwing method:** a conversion method that throws propagates out of the cast expression.
- **Self-host:** keep the runtime self-host conformance diff green (Decimal implements all five contracts already).
