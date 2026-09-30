# Implementation Plan: Story 21.7 — ArrayAccess Destructuring & Stringable Auto-Implement

> **Roadmap position:** Story 21.7 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story **21.6**, before Story **21.8**). **Implement after 21.6.**
> **Direct dependencies (new numbering):** 08 (checker, `4094`, `4120`, `__toString` magic-method rules), **21.6** (`$obj[$k]` / `ArrayAccess<K,V>` / `ArrayAccessShape` typing)
> **New story:** PHP `list()` / `[]` destructuring on `\ArrayAccess` (positional **and** named keys). PHP auto-`implements` `\Stringable` for any class or interface that declares `__toString()`. Tyhp matches both.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-09-02
> **Last design lock:** 2026-09-02 — PHP 8.5 verified: named and positional destructuring call `offsetGet` only; `__toString` makes `instanceof \Stringable` true (including interfaces).
> **Status:** **Design locked.** Implement after 21.6. Language-hooks inventory: `PHP_LANGUAGE_HOOKS.md`.
> **Prerequisites:** Story 08 (`CheckerDestructuringNonArray` 4094, `CheckerConcatNonStringable` 4120, magic `__toString` signature); Story 21.6 (ArrayAccess indexing types).
> **Consumers:** Story 30 (docs); any `ArrayAccess` / `__toString` user code.

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [What already exists](#what-already-exists)
- [Scope (In / Out)](#scope-in--out)
- [Decisions (locked)](#decisions-locked)
- [ArrayAccess destructuring](#arrayaccess-destructuring)
- [Stringable auto-implement](#stringable-auto-implement)
- [Phases](#phases)
- [Out of scope](#out-of-scope)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

PHP 7.1+ destructures ArrayAccess objects the same way as arrays: each bound key is `offsetGet($key)`. That includes **named** keys (`['asdf' => $x] = $obj`), not only `list($a, $b)` / `[$a, $b]`. Tyhp currently rejects anything that is not array/string (`4094`).

PHP also auto-implements `\Stringable` for any class **or interface** that declares `__toString()`. Tyhp requires a written `implements \Stringable` for assignability and for `4120` (echo/concat). This story matches the engine.

---

## Motivation

21.6 types `$obj[$k]`. Destructuring is the same hook (`offsetGet`) and should use those types. Stringable is the other PHP language hook that still diverges from the engine (hooks inventory).

---

## What already exists

| Surface | Today |
|---|---|
| `$obj[$k]` | Story 21.6: `TValue` / `ArrayAccessShape` per-key. **4093** when not array/string/ArrayAccess |
| `[$a, $b] = $x` / `list()` | `TypeCompatibilityRule.Dereferenceables` — array or string only; else **4094** |
| Spread in destructure | **4095** (keep) |
| `__toString(): string` | Magic-method signature check; return must be `string` |
| Echo / concat / interpolation | `IsStringable` = scalar / `string` / `ImplementsInterface("Stringable")` — **no** `__toString` fallback |
| Emit | Native `list` / `[]` and native `__toString`. PHP adds `Stringable` at runtime even if source omitted it |

**PHP 8.5.8 probe (2026-09-02), object implementing `ArrayAccess`:**

| Construct | Engine |
|---|---|
| `[$a, $b] = $obj` / `list($a, $b) = $obj` | `offsetGet(0)`, `offsetGet(1)`. **No** `offsetExists` |
| `['asdf' => $x, 'myFlag' => $y] = $obj` | `offsetGet('asdf')`, `offsetGet('myFlag')`. **No** `offsetExists` |
| `[, $b] = $obj` | `offsetGet(1)` only (skipped slots are not read) |
| `[0 => $i, 'k' => $s] = $obj` | mixed keys; each `offsetGet` with that key |
| Nested `[[$a, $b], $c] = $obj` | outer `offsetGet(0)` then inner destructure (inner ArrayAccess also `offsetGet`) |
| Missing key | still `offsetGet`; value is whatever `offsetGet` returns (often `null` / notice inside the method) |
| `[&$r] = $obj` | works when `offsetGet` returns by-ref |

String `[$c0, $c1] = "ab"` is **not** ArrayAccess (PHP warns; not this story).

`class Foo { function __toString(): string }` → `instanceof \Stringable` is true; `class_implements` lists `Stringable`. An **interface** that declares `__toString()` also lists `Stringable` in `class_implements` / `getInterfaces()` (engine auto-extends).

---

## Scope (In / Out)

**In**

- Checker: ArrayAccess (homogeneous and `ArrayAccessShape`) is a legal destructure source. Keys type-check like `$obj[$k]`. Bound variables get `offsetGet`’s type.
- Named, positional, skipped slots, nested, mixed int/string keys — whatever PHP does.
- By-ref destructure: same rule as `$r =& $obj[$k]` (needs `&offsetGet`).
- Checker: a class or interface that declares `__toString()` **is** `\Stringable` (assignability, `instanceof`, `4120`).
- Written `implements \Stringable` remains legal (redundant with `__toString`).
- Tests + user docs + hooks inventory.

**Out**

- Changing 21.6 indexing / `ArrayAccessShape` / `PhpType`.
- Story 31 Idea 2 (`*Convertible`, `__toInt`, implicit int/float/bool casts). Native Stringable only.
- `list()` on strings (PHP does not treat a string as an array here).
- Spread in destructure (still **4095**).
- Synthesizing `implements \Stringable` in **emitted** PHP (engine already adds it). Omit unless a test proves Reflection-on-source needs it.

---

## Decisions (locked)

1. **Match PHP.** Destructuring an ArrayAccess object is `offsetGet` per bound key. Not `offsetExists`. Not foreach.
2. **Named keys are in.** `['asdf' => $asdfVal, 'myFlag' => $myBool] = $obj;` is the same hook with string (or int) keys. Verified on PHP 8.5.
3. **Types come from 21.6.** Homogeneous: each element is `TValue` if the key is assignable to `TKey`. `ArrayAccessShape`: each named/positional key is `__IndexValueType<T, K>` (or the wide-key diagnostic). `$obj[]` append does not apply (destructure is reads).
4. **4094** stays for types that are not array, string, or ArrayAccess. ArrayAccess is no longer “non-array” for this diagnostic.
5. **Skipped slots** are not reads (no `offsetGet`, no type binding).
6. **`__toString` ⇒ Stringable** for classes **and** interfaces, matching `instanceof` / `class_implements`. Authors need not write `implements \Stringable`.
7. **Emit unchanged** for both features (native destructure, native `__toString`). No interop bump.

---

## ArrayAccess destructuring

Lowering is the same as `$obj[$k]` (21.6). Checker walks the destructure pattern:

- Positional `[$a, $b]` → keys `0`, `1`.
- `[, $b]` → key `1` only.
- `['asdf' => $x]` → key `'asdf'`.
- Nested: type of the inner source is the type of that `offsetGet`; if that is ArrayAccess, continue; if array/string, existing destructure rules; else **4094**.

`list()` is the same AST as `[]` destructure.

---

## Stringable auto-implement

Anywhere the checker asks “does this type implement Stringable?” (assignability to `\Stringable`, `instanceof \Stringable`, echo/concat/`4120`), treat a class or interface as Stringable if it:

- lists `\Stringable` in `implements` / `extends`, **or**
- declares `__toString` (instance, non-static, return `string` — existing magic-method rule).

A class that implements an interface which itself has `__toString` is Stringable (the interface is Stringable; implements follows).

Do not invent `__toInt` / Idea 2 here.

---

## Phases

### Phase 1 — ArrayAccess destructure

- Allow ArrayAccess in `CheckArrayPairList` (and any parallel destructure path).
- Type each bound target via 21.6 `offsetGet` / InferArrayAccess.
- Keep **4095**. Keep **4094** for other types.
- Tests: positional, named, skip, nested, shape per-key, wide key, non-ArrayAccess still 4094.

### Phase 2 — Stringable

- Shared helper used by assignability, `instanceof`, and `4120`.
- Tests: class with only `__toString` is `\Stringable`; interface with `__toString` is `\Stringable`; class without `__toString` is not; explicit `implements \Stringable` still works; echo of `__toString` class is not 4120.

### Phase 3 — Docs

- User docs: destructure ArrayAccess; `__toString` implies Stringable.
- `PHP_LANGUAGE_HOOKS.md` status → **21.7** then **Done** when tests land.

---

## Out of scope

Story 31 Idea 2 (`*Convertible`); refined-type `emit` (`DESIGN_OPEN_QUESTIONS.md`); Fiber CFA (rejected in 21.6); runtime `is Closure<…>` (31 Idea 14); dual Closure spelling (removed in 21.6).

---

## Cross-Story References

| Story | Relationship |
|---|---|
| 08 | 4094 / 4095 / 4120 / `__toString` signature |
| 21.6 | ArrayAccess indexing types; destructure reuses them |
| 21.8 | `tyhpdef/php` Layer 3 overlay completeness (independent; sequenced after this story) |
| 31 Idea 2 | `*Convertible` is **not** this story |
| PHP_LANGUAGE_HOOKS | Audit rows for ArrayAccess destructure and Stringable |
| 30 | User-facing polish if this lands first |

---

## Golden Fixtures / Tests (Acceptance)

- [x] `ArrayAccess<int, User>`: `[$a, $b] = $o` → `$a`/`$b` are `User`; keys `0`/`1` assignable to `int`.
- [x] `ArrayAccess<string, bool>`: `['asdf' => $x, 'myFlag' => $y] = $o` → `$x`/`$y` are `bool`.
- [x] `ArrayAccessShape<Config>`: `['host' => $h] = $o` → `$h` is the `host` field type; unknown key / wide `string` key → same diagnostics as `$o[$k]`.
- [x] `[, $b] = $o` does not require key `0`.
- [x] Nested destructure when `offsetGet(0)` is itself ArrayAccess or array.
- [x] `[$a] = $notArrayAccess` still **4094**.
- [x] `class Foo { public function __toString(): string }` is assignable to `\Stringable`; `echo $foo` is not 4120; `$foo instanceof \Stringable` is true at compile time.
- [x] `interface I { public function __toString(): string; }` is a `\Stringable` type; implementors are too.
- [x] Class with neither `__toString` nor `implements Stringable` is not `\Stringable`.
