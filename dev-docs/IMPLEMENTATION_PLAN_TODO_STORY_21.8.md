# Implementation Plan: Story 21.8 — `tyhpdef/php` Layer 3 Overlay Completeness

> **Roadmap position:** Story 21.8 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story **21.7**, before Story **21.9**). **Implement after 21.7.**
> **Direct dependencies (new numbering):** 03 / 20.6 (tyhpdef `operator` vs `extension operator =>`), 08 (checker consumes live overlay types), 16.5 (`__CallableParametersRest` / Struct / Tuple), **21** (`tyhpdef/php` overlay load), **21.6** (Closure / Fiber / Generator / ArrayAccess / Traversable Layer 3 contract already in `Ext.Core.tyhpdef`)
> **New story:** finish the hand-written Layer 3 overlays for Core, Date, SPL, Standard, and Standard.Callables so they are the Tyhp developer’s PHP surface — generics, overloads, structs, type aliases, type-guard returns, and DateTime operators (native comparisons + mapped `+`/`-`). Close FOUND_BUGS #5 (`SplPriorityQueue`). Also the four checker holes that block that surface: Closure alias-bound expansion (#6), `__CallableParametersTuple` index (#8), WeakMap `$map[] =`, and const-int overload selection for `parse_url` / `pathinfo`. Do not edit Layer 1 or Layer 2.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-09-02
> **Last design lock:** 2026-09-02 — Layer 3 audit plus post-audit decisions (checker items in this story; remaining ceilings in `FOUND_BUGS.md` / `DESIGN_OPEN_QUESTIONS.md` / `DECISIONS.md` / Story 31 Idea 14).
> **Status:** **Design locked.** Implement after 21.7.
> **Prerequisites:** Story 21 (overlay load order: stubs then hand), Story 21.6 (Core Closure/Fiber/Generator/ArrayAccess already overlaid).
> **Consumers:** every Tyhp program that calls PHP builtins through `tyhpdef/php`; Story 30 (docs).

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [What already exists](#what-already-exists)
- [Scope (In / Out)](#scope-in--out)
- [Decisions (locked)](#decisions-locked)
- [Overlay rules (do not reinvent)](#overlay-rules-do-not-reinvent)
- [Ext.Core](#extcore)
- [Ext.SPL](#extspl)
- [Ext.Standard](#extstandard)
- [Ext.Standard.Callables](#extstandardcallables)
- [Ext.Date](#extdate)
- [DateTime operators](#datetime-operators)
- [Checker work in this story](#checker-work-in-this-story)
- [Hygiene](#hygiene)
- [Phases](#phases)
- [Out of scope](#out-of-scope)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

`tyhpdef/php` Layer 3 (`_tyhpdef/overlays/*.tyhpdef`, not under `stubs/`) is the last-wins overlay that sets the shape of PHP for Tyhp developers. Today that layer is a thin slice: Closure/Fiber/Iterator/ArrayAccess (Core), getdate/localtime structs (Date), a handful of SPL generic defaults, Standard array-shape structs, and `call_user_func*` Rest/Struct/Tuple typing.

This story overlays **everything** still missing or wrong after Layer 1 + Layer 2:

- Daily Standard generics (`array_map`, `str_replace<TSubject>`, `is_*` type-guard returns, inverted Layer 2 harvest).
- SPL subclass `implements` remaps that currently shadow parent `Iterator<int, TValue>`.
- DatePeriod constructors, DateInterval properties, date/timezone structs.
- Native DateTime comparison operators and mapped `+`/`-` onto `add` / `sub` / `diff`.
- Core holes (`Generator::current(): TValue`, `SensitiveParameterValue` ctor, `BackedEnum::from` → `static`, `gc_status` struct, existence-check type-guard returns).
- Checker companions: expand type-alias generic bounds (FOUND_BUGS #6), stop treating `__CallableParametersTuple` as ArrayAccess (FOUND_BUGS #8), reject WeakMap `$map[] =`, treat tyhpdef `const int NAME ?? N` as literal `N` at overload selection.

Layer 1 and Layer 2 stay generated. Hand overlays last-win. Nothing in this story is deferred to a later tyhpdef pass.

---

## Motivation

Layer 2 harvest is a subset, often without generic defaults, and sometimes actively wrong (`array_reduce` element type inverted, `str_replace<TKey>` unused, `gc_status(): array<int, int>`, subclass `implements \Iterator` with no type args). Pure.Standard `partial function` only merges `#[\Tyhp\Optimize\Pure]` — it cannot install those generics. Array extension methods already assume generic free functions (`\array_map`, `\str_replace`) that the free-function overlay never provided.

PHP `DateTime + DateInterval` is a TypeError. Comparison (`==`, `<=>`, …) is native engine behavior. Tyhpdef already has both forms; this package does not use them yet.

---

## What already exists

| Surface | Today |
|---|---|
| Overlay load | `extra.tyhp.package`: include `_tyhpdef/*.tyhpdef` + `extensions/`; overlay stubs then `overlays/*.tyhpdef`. Lex order among hand files: Core, Date, SPL, Standard.Callables, Standard, then Pure.* |
| Ext.Core L3 | Traversable/Iterator/IteratorAggregate/ArrayAccess defaults + member remaps; Generator four-arg defaults + `send`/`throw`; WeakReference `create<TIn>` / `get(): ?T`; WeakMap `offsetGet`/`getIterator`; Fiber `TResume`/`TCallableShape`; Closure `TCallableShape`/`TThis`/`TScope` + bind overloads |
| Ext.Date L3 | `GetDateParts` / `LocalTimeParts`; `getdate`; `localtime` true/false/bool overloads |
| Ext.SPL L3 | Seekable/Recursive/OuterIterator defaults + child/inner remaps; DLL/Stack/Queue/Heap/Min/Max header defaults only; SplObjectStorage dual-gated `addAll` copies; ArrayObject/ArrayIterator/RecursiveArrayIterator TKey=`string\|int` + a few members |
| Ext.Standard L3 | ParseUrl / PathInfo / LastError / StreamMetadata / ImageSizeInfo / TimeOfDay structs + overloads |
| Ext.Standard.Callables L3 | `CallableArgs1`–`16`; `call_user_func` / `_array` generic + `_unsafe` aliases |
| Pure.Standard / Pure.Core | `#[Pure]` only. Several stamps claim generic / type-guard signatures that are **not Layer 1** |
| Layer 2 | Harvested Psalm/PHPStan/PhpStorm overlays. **Is** a compile input. Many SPL wrappers already declare `TKey`/`TValue` but restate untyped `implements \Iterator` |

---

## Scope (In / Out)

**In**

- Hand overlays only: `runtime/packages/php/_tyhpdef/overlays/Ext.{Core,Date,SPL,Standard,Standard.Callables}.tyhpdef`.
- Restamp those files (`tyhp overlay stamp`) so `@overlay-against` matches Layer 1 compact form.
- Restamp `Pure.Standard.tyhpdef` / `Pure.Core.tyhpdef` against **real Layer 1** (not the desired live signature). `partial function` still only merges purity.
- Tests for the new / corrected types (foreach, `array_map`, `str_replace`, DatePeriod, DateTime operators, type-guard returns).
- User docs that describe the **current** DateTime operator contract (native comparisons; mapped `+`/`-`). Correct the existing “DateTime arithmetic is native passthrough” line in `tyhpdef_classes.md` / `tyhpdef_extensions.md`.
- Close FOUND_BUGS #5 (`SplPriorityQueue` generics + Iterator remap) once overlays + the existing foreach fixture agree on `<TPriority, TValue>` order.
- Checker: FOUND_BUGS #6 (expand type aliases in `ValidateUserConstraint`), FOUND_BUGS #8 (`__CallableParametersTuple` index must not go through homogeneous ArrayAccess), WeakMap append reject, const-int `??` values as literals for overload selection (`parse_url` / `pathinfo`).

**Out**

- Editing Layer 1 (`_tyhpdef/Ext.*.tyhpdef`) or Layer 2 (`overlays/stubs/`). Generator harvest fixes for DateInterval properties / DatePeriod ctor / `getTransitions` default are FOUND_BUGS (overlay hides them here).
- Other `tyhpdef/php` extensions (Filter, Hash, Json, Libxml, Pcre, Random, Reflection) and `tyhpdef/php-ext-*`.
- Changing Closure / Fiber / ArrayAccess / Traversable contracts locked in 21.6 (except the four checker items listed under In).
- Native `operator []` on ArrayAccess, `operator count()` on Countable — `DECISIONS.md`.
- FOUND_BUGS #4 (intersection `A&B` → `never`) and #7 (TYHP4330 double-report) — stay in FOUND_BUGS, not this story.
- Runtime-flag and format-string ceilings — `DESIGN_OPEN_QUESTIONS.md`.
- Runtime `is Closure<…>` / `is Fiber<…>` — Story 31 Idea 14.
- Fiber `suspend` as `TResume` — rejected in 21.6 (`DECISIONS.md`).

---

## Decisions (locked)

1. **Layer 3 last-wins is the Tyhp PHP surface.** Prefer header-only `partial class Foo<T = mixed> implements …;` for generic / implements remaps, and a second brace `partial` for members. Full function overlay replaces the entire overload set — restate every needed overload in that file.
2. **Do not restate a Layer 2 signature that is already correct.** Overlay only when L3 adds defaults, remaps `implements`, changes a member type, adds overloads/structs, or replaces a wrong harvest.
3. **Drop harvested extra null** on `Generator::current()` the same way Core already does for `Iterator::current` and `ArrayAccess.offsetGet`. Direct `$g->current()` is `TValue`.
4. **Subclass `implements` that omit type args shadow the parent.** `TryGetTraversableIterationTypes` stops at the current class. L3 must rewrite `SplQueue` / `SplStack` / heaps / wrapper iterators so `implements \Iterator<int, TValue>` (or `<TKey, TValue>`) is written on the subclass.
5. **`SplPriorityQueue` is `<TPriority, TValue>`** with `Iterator<int, TValue>` (Psalm / L2). Default extract flag `EXTR_DATA` → `current`/`extract`/`top` are `TValue`. Flag-dependent `EXTR_PRIORITY` / `EXTR_BOTH` cannot be expressed in tyhpdef (`DESIGN_OPEN_QUESTIONS.md`). Align `Foreach_SplPriorityQueue_UsesIteratorIntAndTValue` to TPriority-first (`\SplPriorityQueue<int, string>` if the value should be `string`).
6. **`str_replace` / `str_ireplace` / `substr_replace` use `TSubject extends array\|string` → `TSubject`.** Layer 2’s unused `TKey` is replaced.
7. **`is_*` overlays use type-guard returns** (`$value instanceof int`, etc.) even though `TypeNarrowingRule` hardcodes the same names. Layer 3 is the developer-facing contract. Overlay aliases (`is_integer`, `is_long`, `is_double`) the same way. Existence checks (`class_exists`, …) overlay `$param is __ClassName` / `__FunctionName` / … matching `tyhp_2400_dynamicLanguageFeatures.md`.
8. **`min` / `max` are two overloads**, not one `min<T>(T $value, T ...)`: `min<T>(array<T> $values): T` and `min<T>(T $a, T $b, T ...$rest): T`. A single `T` types `min($arr)` as returning the array.
9. **DateTime comparisons are native** (`operator ==` / `!=` / `<` / `<=` / `>` / `>=` / `<=>` bodyless). Right operand is `\DateTimeInterface`. Put them on `DateTimeInterface` if overlay `partial interface` accepts `operator`; otherwise on both `DateTime` and `DateTimeImmutable`.
10. **DateTime arithmetic is mapped**, not native. PHP TypeErrors on `DateTime + DateInterval`. Use class-body `extension operator` with `=>`:
    - `+ (self, DateInterval)` → `$left->add($right)`
    - `- (self, DateInterval)` → `$left->sub($right)`
    - `- (self, DateTimeInterface)` → `$left->diff($right)`
    Direct method map: mutable `DateTime::add`/`sub` mutate `$left` and return it. Do **not** clone. `DateTimeImmutable` already returns a new instance. **Never** write bodyless `operator +;` on these types.
11. **No DateInterval comparison operators** (incomparable since PHP 7.4).
12. **Structs for PHP array-shapes** use `?T` for omitted keys (same model as `ParseUrlComponents`). Numeric PHP keys use `int N as $name`.
13. **Strip `TODO: review this for inaccuracies` and Story-number comments** from overlay files. Overlay comments describe the type contract, not planning history.
14. **Stamps record Layer 1 compact signatures**, not the live overlay signature. Run `tyhp overlay stamp` after edits. Pure.* stamps that currently claim `str_replace<TSubject>` / `is_int: $value instanceof int` / `array_map<TValue, TResult>` must be rewritten to Layer 1.
15. **Expand type-alias bounds in `ValidateUserConstraint`** (FOUND_BUGS #6). Overlay `__ClosureThis` / `__ClosureScope` stay as written.
16. **Struct int-alias indexing is not ArrayAccess** (FOUND_BUGS #8). `$args[0]` on `__CallableParametersTuple` yields the field type.
17. **WeakMap append is illegal.** Tightens 21.6 Decision 21: append is still `offsetSet(null, $v)`; when `null` is not assignable to the key parameter, `$map[] =` is a type error. Checker; L2 `offsetSet(TKey)` already forbids null.
18. **Tyhpdef `const int NAME ?? N` is literal `N` at overload selection** when the start value is a known integer. `\PHP_URL_HOST` then picks `parse_url(..., 1 $component)`. If the const has no known start value, keep the `int` catch-all.

---

## Overlay rules (do not reinvent)

See `docs/content/tyhpdef_overlays.md`. Short form used throughout this story:

- Header-only `partial class Foo<T>;` replaces **written** generic / extends / implements / `as` clauses; omitted clauses stay.
- Brace `partial class Foo { members }` replaces listed members (including overload sets). Written headers in the brace form are ignored.
- Full `function foo(...)` replaces the entire overload set for that Tyhp name.
- `function php as tyhp(...)` **adds** an alias and leaves the original; a later full `function php<…>` replaces the original.
- Overlay `partial function foo;` merges attributes only — use Pure.* for `#[Pure]`, Ext.Standard for signatures.
- `@overlay-against:` is Layer 1 compact (no `public`/`static`/`final` on methods; type stamps are header-only).

---

## Ext.Core

File: `runtime/packages/php/_tyhpdef/overlays/Ext.Core.tyhpdef`.

### Keep

Iterator / Traversable / IteratorAggregate (defaults + `current`/`key`/`getIterator`); ArrayAccess header + `offsetGet(): TValue` only; Generator header defaults + `send`/`throw`; WeakReference `create`/`get`; WeakMap `offsetGet`/`getIterator`; Fiber; Closure + `__ClosureThis` / `__ClosureScope`. Do not restate WeakMap/WeakReference generic **headers** (already Layer 2). Do not copy ArrayAccess `offsetExists`/`offsetSet`/`offsetUnset`.

Optional: drop the redundant `implements \Iterator<TKey, TValue>, \Traversable<TKey, TValue>` on the Generator header (L2 already writes it). Defaults remain.

### Add / change

| Symbol | Overlay |
|---|---|
| `Generator::current` | `public function current(): TValue;` in the existing brace partial. Stamp: `function current(): mixed`. |
| `SensitiveParameterValue` | `partial class SensitiveParameterValue<TValue = mixed>;` plus `__construct(TValue $value): void`. L2 already has `getValue(): TValue`. |
| `BackedEnum` | Brace partial: `from(string\|int $value): static;` `tryFrom(...): ?static`. |
| `gc_status` | Structs + gated functions. PHP &lt;8.3: `runs`, `collected`, `threshold`, `roots` (`int`). PHP ≥8.3 also: `running`/`protected`/`full` (`bool`), `buffer_size` (`int`), `application_time`/`collector_time`/`destructor_time`/`free_time` (`float`). Replace L2 `array<int, int>`. |
| `get_declared_traits` | `array<int, string>` (L2 already did classes/interfaces). |
| `set_error_handler` | `?callable<int, string, string, int, bool>` (PHP 8 dropped `$errcontext`). Return of the previous handler stays `?callable` (unknown prior shape). |
| `set_exception_handler` | `?callable<\Throwable, void>`; previous stays `?callable`. |
| `Error` / `Exception` `getCode` | `int` (covariant vs `Throwable::getCode(): mixed`). Leave `Throwable` mixed. |
| `InternalIterator` | `partial class InternalIterator<TKey = mixed, TValue = mixed>;` plus `current(): TValue;` `key(): TKey;`. |
| `class_exists` | `function class_exists(string $class, bool $autoload = true): $class is \__ClassName;` Generic form `class_exists<T>(string $class, …): $class is \__ClassName<T>` if the grammar already allows it on this function (08.5). Same pattern: `function_exists` → `__FunctionName`; `interface_exists` → `__InterfaceName`; `trait_exists` → `__TraitName`; `enum_exists` → `__EnumName`; `property_exists` → `__PropertyName` on arg 1; `method_exists` → `__MethodName` on arg 1. Checker already special-cases these names; the overlay is the declared contract. |
| Stamps / comments | Compact Layer 1 stamps (Iterator `extends Traversable`; `get(): ?object`; Fiber/Closure methods without `public`/`static`). Remove Story 21.6 comments. |

---

## Ext.SPL

File: `runtime/packages/php/_tyhpdef/overlays/Ext.SPL.tyhpdef`.

### Keep

SeekableIterator / RecursiveIterator / OuterIterator defaults; RecursiveIterator `getChildren(): ?RecursiveIterator<TKey, TValue>`; OuterIterator `getInnerIterator(): ?Iterator<TKey, TValue>`; SplDoublyLinkedList / SplHeap header defaults only (L2 already remaps Iterator/ArrayAccess); ArrayObject / ArrayIterator `TKey = string\|int` default; ArrayObject `__construct` / `exchangeArray`; RecursiveArrayIterator `getChildren` generic; ArrayIterator `key(): TKey` (aligned with Core Iterator, drops L1 `null`). EmptyIterator L2 `Iterator<never, never>` — no L3 entry.

### Add / change

**Collections (foreach/index)**

| Symbol | Overlay |
|---|---|
| `SplStack` / `SplQueue` | `partial class …<TValue = mixed> implements \Iterator<int, TValue>, \ArrayAccess<int, TValue>, \Serializable, \Countable, \Traversable;` |
| `SplMinHeap` / `SplMaxHeap` | `partial class …<TValue = mixed> implements \Iterator<int, TValue>, \Countable, \Traversable;` |
| `SplPriorityQueue` | `partial class SplPriorityQueue<TPriority = mixed, TValue = mixed> implements \Iterator<int, TValue>, \Traversable, \Countable;` plus `compare(TPriority, TPriority): int`, `current(): TValue`, `extract(): TValue`, `top(): TValue`. L2 already has `insert(TValue, TPriority): true`. |
| `SplFixedArray` | Replace L2 `<TValue, TInValue>` + untyped ArrayAccess: `partial class SplFixedArray<TValue = mixed> implements \IteratorAggregate<int, TValue>, \ArrayAccess<int, TValue>, \Countable, \JsonSerializable;` plus `getIterator(): \Iterator<int, TValue>` and `fromArray<TInput>(array<int, TInput> $array, bool $preserveKeys = true): \SplFixedArray<TInput>`. |

**SplObjectStorage**

- Delete the duplicate `#[\Tyhp\Php("<8.4")]` / `>=8.4` member copies (L1 `addAll`/`removeAll`/`removeAllExcept` return `int` on both gates). One ungated brace partial.
- Do **not** restate the generic header (L2 already has `TObject extends object = object, TData = mixed` and Iterator/ArrayAccess remaps, including 8.4 `SeekableIterator`).
- Overlay members L2 left as `object`/`mixed`: `current(): TObject`; `attach(TObject $object, TData $info = null)`; `contains` / `detach` / `offsetGet`/`offsetSet`/`offsetExists`/`offsetUnset` with `TObject`/`TData` if still ungeneric after L2.

**ArrayObject**

- `getIterator(): \Iterator<TKey, TValue>` (not `\ArrayIterator<…>` — `$iteratorClass` is a runtime string).

**RecursiveArrayIterator**

- Remap `implements \RecursiveIterator<TKey, TValue>, \SeekableIterator<TKey, TValue>, \ArrayAccess<TKey, TValue>, …` so L2’s untyped Iterator/ArrayAccess do not shadow.

**Wrapper iterators** — add `TKey = mixed, TValue = mixed` defaults where missing, and **write** `implements \Iterator<TKey, TValue>` (plus `OuterIterator<TKey, TValue>` / `RecursiveIterator<TKey, TValue>` / `ArrayAccess<TKey, TValue>` when those clauses exist on L1/L2). Targets:

- `IteratorIterator`, `FilterIterator`, `LimitIterator`, `AppendIterator`, `InfiniteIterator`, `NoRewindIterator`
- `CachingIterator` — also overlay `offsetGet(TKey): TValue` / `offsetSet(TKey, TValue)` (L2 hardcoded `string`)
- `CallbackFilterIterator` — constructor `callable(TValue, TKey, TIterator): bool`
- `RegexIterator`, `RecursiveFilterIterator`, `RecursiveCallbackFilterIterator`, `RecursiveRegexIterator`, `ParentIterator`, `RecursiveCachingIterator`

`FilterIterator` constructor: `TIterator extends \Iterator<TKey, TValue>` (L1 is `\Iterator`, L2 bound `Traversable` is looser).

**RecursiveIteratorIterator**

```tyhp
partial class RecursiveIteratorIterator<
    TKey = mixed,
    TValue = mixed,
    TIterator extends \RecursiveIterator<TKey, TValue> = \RecursiveIterator<TKey, TValue>
> implements \Iterator<TKey, TValue>, \OuterIterator<TKey, TValue>, \Traversable;
```

Constructor takes `TIterator` (L1 is `\Traversable`). `getInnerIterator` / `getChildren` follow TKey/TValue. `RecursiveTreeIterator` keeps L2 `current(): string` / `key(): string` (prefixed tree text) — do **not** force `TValue` onto `current()`.

**Filesystem / file**

| Symbol | Overlay |
|---|---|
| `FilesystemIterator` | `SeekableIterator<string, \SplFileInfo\|string\|self>` (union of `CURRENT_AS_*`). Flag-precise current() is `DESIGN_OPEN_QUESTIONS.md`. |
| `RecursiveDirectoryIterator` | Keep L2 `SeekableIterator<string, RecursiveDirectoryIterator\|string\|SplFileInfo>`; overlay `getChildren(): ?\RecursiveDirectoryIterator` if L1 is non-null and PHP can return null. |
| `GlobIterator` | `Countable` already on L1; inherit FilesystemIterator current/key union. Header remap `SeekableIterator<string, …>` to match parent. |
| `SplFileObject` | `Iterator<int, array\|string\|false>` (CSV flag → `array`; default → `string`; false on EOF/error). `SplTempFileObject` inherits. |

**`iterator_to_array`**

L2 already has generics but return is bare `array`. Overlay:

```tyhp
function iterator_to_array<TKey extends int|string, TValue>(
    \Traversable<TKey, TValue>|array<TKey, TValue> $iterator,
    true $preserve_keys
): array<TKey, TValue>;
function iterator_to_array<TKey extends int|string, TValue>(
    \Traversable<TKey, TValue>|array<TKey, TValue> $iterator,
    false $preserve_keys
): array<int, TValue>;
function iterator_to_array<TKey extends int|string, TValue>(
    \Traversable<TKey, TValue>|array<TKey, TValue> $iterator,
    bool $preserve_keys = true
): array<TKey, TValue>|array<int, TValue>;
```

**MultipleIterator**

Overlay `current(): array<int|string, TValue>` and `key(): array<int|string, TKey>` (L2 `key()` is typed as values). `MIT_KEYS_*` stays coarse.

---

## Ext.Standard

File: `runtime/packages/php/_tyhpdef/overlays/Ext.Standard.tyhpdef`.

### Keep

Structs `ParseUrlComponents`, `PathInfo`, `LastError`, `StreamMetadata`, `ImageSizeInfo`, `TimeOfDay` and the existing function overloads. Add:

- `parse_url` literals: `-1` → `ParseUrlComponents\|false`; `0,1,3,4,5,6,7` → `string\|false\|null`; keep `2` → `int\|false\|null` and the `int` catch-all. Constants: `PHP_URL_SCHEME=0` … `FRAGMENT=7`. Decision 18 makes `\PHP_URL_HOST` select the `1` overload.
- `socket_get_status(mixed $stream): StreamMetadata` (PHP alias of `stream_get_meta_data`).

Do **not** re-overlay L2 signatures that already preserve keys/values correctly: `array_fill_keys`, `array_flip`, `array_key_exists`, `array_diff*`, `array_intersect*` (first-array TKey/TValue). `count` / `sizeof` stay L1 `Countable\|array` → `int`.

### Replace / add — strings

```tyhp
function str_replace<TSubject extends array|string>(
    array|string $search, array|string $replace, TSubject $subject, mixed &$count = null
): TSubject;
```

Same for `str_ireplace`. `substr_replace<TSubject extends array|string>(TSubject $string, array|string $replace, array|int $offset, array|int|null $length = null): TSubject`.

`explode`: `array<int, string>` (drop L2 `\|false`; empty separator throws on PHP 8).

### Replace / add — type guards

Return `$value instanceof T` (or the documented union). Overlay every L1 alias:

| Function | Guard |
|---|---|
| `is_int` / `is_integer` / `is_long` | `$value instanceof int` |
| `is_float` / `is_double` | `$value instanceof float` |
| `is_string` | `$value instanceof string` |
| `is_bool` | `$value instanceof bool` |
| `is_array` | `$value instanceof array` |
| `is_object` | `$value instanceof object` |
| `is_null` | `$value instanceof null` |
| `is_numeric` | `$value instanceof int\|float\|string` |
| `is_scalar` | `$value instanceof int\|float\|string\|bool` |
| `is_iterable` | `$value instanceof iterable` |
| `is_countable` | `$value instanceof array\|\Countable` |
| `is_resource` | `$value instanceof resource` |
| `is_callable` | `$value instanceof callable` when `$syntax_only` is `false`; keep a `bool` overload for `true $syntax_only` |

### Replace / add — array (full overlay where L2 is missing or wrong)

L2 **absent** (install from L1 + generics): `array_map`, `array_filter`, `array_walk`, `array_walk_recursive`, `array_fill`, `array_slice`, `array_splice`, `array_reverse`, `array_replace`, `array_replace_recursive`, `array_pad`, `array_chunk`, `array_column`, `in_array`, `min`, `max`, `next`, `prev`, `array_pop`, `array_shift`, `array_push`, `array_unshift`, `array_is_list` (bool is fine; optional `$array is list` if guard syntax allows), PHP 8.4 `array_find` / `array_find_key` / `array_any` / `array_all` (`#[\Tyhp\Php(">=8.4")]`), PHP 8.5 `array_first` / `array_last` (`>=8.5`).

L2 **wrong or incomplete** (replace the whole overload set):

| Function | Live overlay |
|---|---|
| `array_map` | `array_map<TValue, TResult extends void\|never\|mixed>(callable<TValue, TResult> $callback, array<TValue> $array): array<TResult>` plus a `?callable` / extra-arrays form (`array ...$arrays`) returning `array`. `null $callback` zip stays `array` (N-array zip typing is checker, not this overlay). |
| `array_values` | `array_values<TKey, TValue>(array<TKey, TValue> $array): array<int, TValue>` |
| `array_reduce` | `array_reduce<TValue, TCarry>(array<TValue> $array, callable<TCarry, TValue, TCarry> $callback, TCarry $initial): TCarry` |
| `array_search` | `array_search<TKey, TValue>(TValue $needle, array<TKey, TValue> $haystack, bool $strict = false): TKey\|false` |
| `array_keys` | Two overloads: all-keys `array<int, TKey>`; filter `array_keys(array<TKey, TValue>, TValue $filter_value, bool $strict = false): array<int, TKey>`. L1/L2 require `$filter_value` — that is a harvest bug, fix in L3. |
| `array_unique` | return `array<TKey, TValue>` (L2 return is bare `array`) |
| `array_combine` | return `array<TKey, TValue>` |
| `array_merge` / `array_merge_recursive` | `array_merge<TKey, TValue>(array<TKey, TValue> ...$arrays): array<TKey, TValue>` |
| `current` / `end` / `reset` / `next` / `prev` | `TValue\|false` |
| `key` | `TKey\|null` |
| `usort` / `uasort` | `callable<TValue, TValue, int>` |
| `uksort` | `callable<TKey, TKey, int>` |
| `array_key_first` / `array_key_last` | `?TKey` |
| `array_filter` | `array<TKey, TValue>` in/out; optional mode overloads `0` / `ARRAY_FILTER_USE_KEY=2` / `BOTH=1` for callback arity |
| `array_walk` / `array_walk_recursive` | restore `$arg = null`; `callable<TValue, TKey, mixed, void>` |
| `in_array` | `in_array<TKey, TValue>(TValue $needle, array<TKey, TValue> $haystack, bool $strict = false): bool` |
| `array_fill` | `array_fill<TValue>(int $start_index, int $count, TValue $value): array<int, TValue>` |
| `array_slice` | `array<TKey, TValue>` in/out (`preserve_keys=false` reindexes ints — acceptable) |
| `array_reverse` | `array<TKey, TValue>` in/out |
| `array_replace` / `array_replace_recursive` | same pattern as merge |
| `array_pad` | `array<TKey, TValue>` |
| `array_chunk` | `array<int, array<TKey, TValue>>` |
| `array_column` | `array` of rows → `array<int\|string, mixed>` (no row struct without a caller-supplied shape) |
| `array_pop` / `array_shift` | `?TValue` |
| `array_push` / `array_unshift` | `TValue ...$values): int` |
| `array_splice` | `array<TKey, TValue>` by-ref + return removed `array<TKey, TValue>` |
| `array_find` | `?TValue` with `callable<TValue, TKey, bool>` |
| `array_find_key` | `?TKey` |
| `array_any` / `array_all` | `bool` with the same callback |
| `array_first` / `array_last` | `?TValue` |
| `min` / `max` | array overload + variadic overload (Decision 8) |
| `range` | Restate both L1 version gates (`<8.3` mixed, `≥8.3` `string\|int\|float`). Overloads: `int,int → array<int,int>`; `float involved → array<int,float>`; `string,string → array<int,string>`. |
| `fgetcsv` / `str_getcsv` | `array<int, string\|null>\|false` |
| `parse_ini_file` / `parse_ini_string` | `false $process_sections` → `array<string, string>`; `true` → `array<string, array<string, string>>`; `bool` catch-all union |
| `get_headers` | `false $associative` → `array<int, string>\|false`; `true` → `array<string, string\|array<int, string>>\|false` |
| `proc_get_status` | struct `ProcStatus`: `command` (string), `pid` (int), `running`/`signaled`/`stopped` (bool), `exitcode`/`termsig`/`stopsig` (int), plus documented extra keys as optional |

Signatures must match `runtime/packages/php/_tyhpdef/extensions/array.tyhpdef` where that file already spells a generic free-function call (`mapped` → `array_map`, `replace` is string-side, `search` → `array_search`, …).

---

## Ext.Standard.Callables

File: `runtime/packages/php/_tyhpdef/overlays/Ext.Standard.Callables.tyhpdef`.

### Keep

`CallableArgs1`–`16`; `call_user_func` / `call_user_func_array` generic + `_unsafe` aliases; Struct + Tuple overloads on `_array`. Do **not** add an untyped `array $args` overload on the generic name.

### Add

Same Rest / Struct+Tuple + `_unsafe` aliases for `forward_static_call` / `forward_static_call_array`.

`register_shutdown_function<TCallable extends callable>(TCallable $callback, __CallableParametersRest<TCallable> ...$args): void`.

Fix generic docblocks that still say `@return mixed`. Stamps vs Layer 1 (`mixed ...$args`, `array $args`).

---

## Ext.Date

File: `runtime/packages/php/_tyhpdef/overlays/Ext.Date.tyhpdef`.

### Keep

`GetDateParts` / `LocalTimeParts` and `getdate` / `localtime` overloads. Fix stamps to L1 `?int $timestamp = null` and `bool $associative = false`. Drop TODOs.

### Add — DatePeriod

L2 header generics stay useful; L3 must last-win `implements \IteratorAggregate<int, TDate>, \Traversable<int, TDate>` (L2 leaves `IteratorAggregate` unparameterized) and replace the single 4-arg constructor:

```tyhp
public function __construct(TDate $start, \DateInterval $interval, TEnd $end, int $options = 0): void;
public function __construct(TDate $start, \DateInterval $interval, int $recurrences, int $options = 0): void;
#[\Deprecated('8.4')]
public function __construct(string $isostr, int $options = 0): void;
```

Also: `getIterator(): \Iterator<int, TDate>`; `getStartDate(): TDate`; `getEndDate(): TEnd`; `createFromISO8601String` (`#[\Tyhp\Php(">=8.3")]`) → `\DatePeriod<\DateTimeImmutable>`; properties `$start`/`$current` as `?TDate`, `$end` as `TEnd`.

### Add — DateInterval properties

L1/L2 are methods only. Brace partial:

```tyhp
public int $y;
public int $m;
public int $d;
public int $h;
public int $i;
public int $s;
public float $f;
public int $invert;
public int|false $days;
public bool $from_string;
public string $date_string;
```

### Add — array-shape structs + functions

| Struct | Keys | Overlay on |
|---|---|---|
| `DateParseResult` | `year`/`month`/`day`/`hour`/`minute`/`second` as `int\|false`; `fraction` as `float\|false`; `warning_count`/`error_count` (`int`); `warnings`/`errors` (`array<int, string>`); `is_localtime` (`bool`); optional `zone_type`, `zone`, `is_dst`, `tz_abbr`, `tz_id`; optional nested `DateParseRelative` | `date_parse`, `date_parse_from_format`. **No** `\|false` on `date_parse` (PHP 8 returns the array; errors live in `errors`). |
| `TimezoneTransition` | `ts` (int), `time` (string), `offset` (int), `isdst` (bool), `abbr` (string) | `timezone_transitions_get`, `DateTimeZone::getTransitions` → `array<int, TimezoneTransition>\|false`. Restate **both** PHP version gates of `getTransitions`; default `$timestampEnd` is `2147483647` on both (L1 `<8.5` uses `PHP_INT_MAX`, wrong for 8.2–8.4). |
| `TimezoneLocation` | `country_code` (string), `latitude`/`longitude` (float), `comments` (`string\|false`) | `timezone_location_get`, `DateTimeZone::getLocation` |
| `DateLastErrors` | `warning_count`, `warnings`, `error_count`, `errors` | `date_get_last_errors`, `DateTimeImmutable::getLastErrors` / `DateTime::getLastErrors` |
| `DateSunInfo` | `sunrise`/`sunset`/`transit`/`civil_twilight_begin`/`civil_twilight_end`/`nautical_twilight_begin`/`nautical_twilight_end`/`astronomical_twilight_begin`/`astronomical_twilight_end` as `int\|false` | `date_sun_info` → `DateSunInfo\|false` |
| `TimezoneAbbreviation` | `dst` (bool), `offset` (int), `timezone_id` (`?string`) | inner element of `timezone_abbreviations_list` / `listAbbreviations` → `array<string, array<int, TimezoneAbbreviation>>` |

`timezone_identifiers_list` / `DateTimeZone::listIdentifiers`: `array<int, string>` (drop L2 `\|false` unless a PHP 8 probe shows false).

---

## DateTime operators

All in `Ext.Date.tyhpdef` via overlay `partial class` / `partial interface`.

### Native (bodyless `operator` — emitter leaves PHP)

PHP compares `DateTimeInterface` by timeline, not identity:

```tyhp
operator ==(self $left, \DateTimeInterface $right): bool;
operator !=(self $left, \DateTimeInterface $right): bool;
operator <(self $left, \DateTimeInterface $right): bool;
operator <=(self $left, \DateTimeInterface $right): bool;
operator >(self $left, \DateTimeInterface $right): bool;
operator >=(self $left, \DateTimeInterface $right): bool;
operator <=>(self $left, \DateTimeInterface $right): int;
```

Host on `DateTimeInterface` if legal; otherwise both concrete classes with the same right-operand type.

### Mapped (`extension operator` + `=>` — splice to methods)

On `DateTime`:

```tyhp
extension operator +(self $left, \DateInterval $right): \DateTime => $left->add($right);
extension operator -(self $left, \DateInterval $right): \DateTime => $left->sub($right);
extension operator -(self $left, \DateTimeInterface $right): \DateInterval => $left->diff($right);
```

On `DateTimeImmutable`, same mappings returning `\DateTimeImmutable` for `+`/`-` interval and `\DateInterval` for `diff`.

`$left->add($right)` on mutable `DateTime` mutates `$left`. That is the PHP method. Do not clone.

**Illegal:** bodyless `operator +(self, DateInterval)` — that would emit `$a + $b` and TypeError at runtime.

User docs: comparisons are native passthrough; `+`/`-` are thin mappings onto `add`/`sub`/`diff`. Fix `tyhpdef_classes.md` / `tyhpdef_extensions.md` lines that call “DateTime arithmetic” native.

---

## Checker work in this story

Pulled in after the Layer 3 audit (2026-09-02). Overlay files stay as written for Closure aliases, Tuple structs, WeakMap `offsetSet`, and `parse_url` literal overloads; the checker is what makes those contracts usable.

| Item | Change |
|---|---|
| FOUND_BUGS #6 | `ValidateUserConstraint` expands type aliases used as `extends` bounds (after sibling substitution). `\Closure<C, Host>` / `\Closure<C, Host, Host>` type-checks when `Host` is `object`. |
| FOUND_BUGS #8 | Indexing `__CallableParametersTuple` (int-alias struct keys) yields the field type. Do not run homogeneous `ArrayAccess` key-assignability on that path. Also restore the union-of-closures `__CallableReturnType` mismatch in `Check_GenericApply_UnionOfClosures_DoesNotAssignToSingleReturn`. |
| WeakMap append | If `offsetSet`’s key is not `null\|TKey`, `$map[] =` is a type error (same idea as ArrayAccessShape TYHP4332, for homogeneous ArrayAccess). L2 `WeakMap::offsetSet(TKey)` already forbids null. |
| Const-int overload selection | A tyhpdef `const int NAME ?? N` with a known integer start value is literal `N` when selecting overloads. `\PHP_URL_HOST` then matches `parse_url(..., 1 $component)` instead of the `int` catch-all. No known start value → keep `int`. Cheap; do it if overload selection already sees const symbols. |

---

## Hygiene

- `tyhp overlay stamp` on all touched hand overlays and on Pure.*.
- Pure.Standard / Pure.Core stamps must match Layer 1 compact signatures (`function str_replace(array|string $search, …): array|string`, `function is_int(mixed $value): bool`, `function array_map(?callable $callback, array $array, array ...$arrays): array`, …). After Ext.Standard last-wins the generic, Pure still only adds `#[Pure]` to the live overload set.
- Remove every `TODO: review this for inaccuracies`.
- Remove Story-number comments from overlay files.

---

## Phases

Implement in this order so later overlays can assume earlier remaps (Core Iterator before SPL foreach).

### Phase 1 — Ext.Core

Generator `current`, SensitiveParameterValue, BackedEnum, gc_status, get_declared_traits, error/exception handlers, getCode, InternalIterator, existence-check type-guard returns, stamps/comments.

### Phase 2 — Ext.SPL

Stack/Queue/Heap implements remaps; SplPriorityQueue; SplFixedArray; SplObjectStorage collapse + TObject members; ArrayObject getIterator; wrapper iterators; RecursiveIteratorIterator; filesystem/file; iterator_to_array; MultipleIterator. Fix the SplPriorityQueue foreach fixture argument order.

### Phase 3 — Ext.Standard + Callables

Structs keep + parse_url literals + socket_get_status; TSubject string replaces; type guards; array_* list in this plan; min/max; range gates; csv/ini/headers/proc_get_status; forward_static_call*; register_shutdown_function.

### Phase 4 — Ext.Date + operators

DatePeriod, DateInterval properties, parse/timezone/sun/last-errors structs, native comparisons, mapped `+`/`-`/`diff`.

### Phase 5 — Stamp, Pure restamp, docs, FOUND_BUGS #5

`tyhp overlay stamp`; Pure.* Layer 1 stamps; user docs for DateTime operators; mark FOUND_BUGS #5 resolved when the foreach test is green on `<TPriority, TValue>`.

### Phase 6 — Checker companions

FOUND_BUGS #6 (alias expansion in `ValidateUserConstraint`); FOUND_BUGS #8 (Tuple index + union-of-closures return); WeakMap `$map[] =` reject; const-int `??` as literal at overload selection. Golden fixtures below. Mark #6 / #8 / #10 resolved when those fixtures are green.

---

## Out of scope

Decided 2026-09-02. Overlay work in this story still **hides** the L1 harvest bugs (DateInterval properties, DatePeriod ctors, `getTransitions` default) — the generator fix is FOUND_BUGS, not this story.

| Issue | Where it lives |
|---|---|
| ArrayAccess TYHP4330 double-report (FOUND_BUGS #7) | FOUND_BUGS. Not this story. |
| Intersection `A&B` → `never` (FOUND_BUGS #4) | FOUND_BUGS. Not this story. |
| L1 harvest: DateInterval properties, DatePeriod 4-arg mixed ctor, `getTransitions` `PHP_INT_MAX` default | Overlay hides them here. Generator regen will reintroduce until FOUND_BUGS #11. |
| Fiber `suspend()` as `TResume` | Rejected in 21.6. `DECISIONS.md`. |
| `operator []` on ArrayAccess / `operator count()` on Countable | Do not add tyhpdef operators. Checker already owns indexing / `count()`. `DECISIONS.md`. |
| SplPriorityQueue `setExtractFlags` (EXTR_DATA / PRIORITY / BOTH) | Overlay default is EXTR_DATA → `TValue`. Per-flag types: `DESIGN_OPEN_QUESTIONS.md`. |
| FilesystemIterator `CURRENT_AS_*` / `KEY_AS_*` | Overlay union of modes. Per-flag types: `DESIGN_OPEN_QUESTIONS.md`. |
| `sprintf` / `sscanf` / `pack` / `unpack` format strings | Revisit later. `DESIGN_OPEN_QUESTIONS.md`. |
| `compact` / `extract` / `settype` / `get_defined_vars` | Leave as harvested. Manual review list in `DESIGN_OPEN_QUESTIONS.md`. |
| User `implements Traversable` | Done in 21.6. |
| Runtime `is Closure<…>` / `is Fiber<…>` | Story 31 Idea 14. |

---

## Cross-Story References

| Story | Relationship |
|---|---|
| 03 / 20.6 | Native `operator` vs mapped `extension operator =>` |
| 08 / 08.5 | Type-guard returns; `__ClassName` et al. |
| 16.5 | `__CallableParametersRest` / Struct / Tuple used by Callables + `register_shutdown_function` |
| 21 | Overlay glob order |
| 21.6 | Core Closure/Fiber/Generator/ArrayAccess — do not reopen |
| 21.7 | Independent; implement 21.8 after 21.7 |
| FOUND_BUGS #5 | Closed by SplPriorityQueue L3 + fixture argument-order fix |
| FOUND_BUGS #6 / #8 / #10 | Closed by Phase 6 checker companions |
| FOUND_BUGS #4 / #7 / #11 | Stay in FOUND_BUGS; not this story |
| 30 | User-facing DateTime operator docs |
| 31 Idea 14 | Runtime `is Closure<…>` / `is Fiber<…>` — leave there |

---

## Golden Fixtures / Tests (Acceptance)

### Core

- [ ] `$g = (function (): \Generator<int, string> { yield 1 => "a"; })();` — `$g->current()` is `string`, not `string\|null`.
- [ ] `new \SensitiveParameterValue("x")` infers `SensitiveParameterValue<string>`; `getValue()` is `string`.
- [ ] `BackedEnum::from` on a concrete backed enum is `static` (assignable to that enum), not `\BackedEnum`.
- [ ] `gc_status()["runs"]` is `int` (not indexing an `array<int, int>`).
- [ ] `if (\class_exists($name)) { new $name(); }` still narrows `$name` (overlay guard return + existing checker list).
- [ ] `Error::getCode()` is `int`; `Throwable::getCode()` stays `mixed`.
- [ ] `\Closure<callable<int, string>, Host>` (and the 3-arg `TThis`/`TScope` form) type-checks when `Host` is a class; no TYHP4035 (FOUND_BUGS #6).
- [ ] `$map[] = $obj;` on `\WeakMap<object, string>` is a type error; `$map[$obj] = "x"` is not.

### SPL

- [ ] `foreach` over `\SplQueue<User>` / `\SplStack<User>` / `\SplMinHeap<User>` / `\SplMaxHeap<User>`: value is `User`, key is `int`.
- [ ] `\SplPriorityQueue<int, User>` foreach: key `int`, value `User`. Existing fixture updated to TPriority-first.
- [ ] `\SplFixedArray<string>`: `$a[0]` is `string`; `fromArray(["a"])` is `\SplFixedArray<string>`.
- [ ] `\SplObjectStorage<User, string>`: `current()` is `User`; `attach($user, "meta")` type-checks; `attach("nope")` does not.
- [ ] `\ArrayObject<string, User>::getIterator()` is `\Iterator<string, User>` (not necessarily ArrayIterator).
- [ ] `\FilterIterator` / `\LimitIterator` / `\IteratorIterator` foreach uses `TKey`/`TValue`, not `mixed`.
- [ ] `\RecursiveIteratorIterator` foreach uses declared `TKey`/`TValue`.
- [ ] `iterator_to_array($it, true)` keeps `TKey`; `iterator_to_array($it, false)` is `array<int, TValue>`.

### Standard

- [ ] `\str_replace("a", "b", $s)` with `string $s` is `string`; with `array<int, string> $s` is `array<int, string>`. `\ltrim(\str_replace(...), ...)` type-checks when subject is `string`.
- [ ] `\array_map(fn(int $n): string => (string) $n, [1, 2])` is `array<string>`.
- [ ] `if (\is_int($x)) { int $y = $x; }` — overlay guard return, not only the hardcoded checker list.
- [ ] `\array_values(array<string, User>)` is `array<int, User>`.
- [ ] `\array_reduce($nums, fn(int $c, int $n): int => $c + $n, 0)` is `int` (not inverted TIn/TReturn).
- [ ] `\array_search($user, array<string, User>)` is `string\|false`.
- [ ] `\array_keys($arr)` does not require a filter value; with filter, keys are `TKey`.
- [ ] `\min([1, 2, 3])` is `int`; `\min(1, 2, 3)` is `int`; `\min($arr)` is **not** typed as returning the array.
- [ ] `\array_find` / `\array_first` gated and returning `?TValue`.
- [ ] `parse_url($u)` is `ParseUrlComponents\|false`; `parse_url($u, 2)` is `int\|false\|null`; `parse_url($u, -1)` is the struct.
- [ ] `parse_url($u, \PHP_URL_HOST)` is `string\|false\|null` (const-int literal at overload selection), not the `int` catch-all.

### Callables

- [ ] `forward_static_call($fn, ...)` return is `__CallableReturnType`; `_unsafe` alias remains.
- [ ] Existing `call_user_func_array` Tuple/Struct tests stay green. No untyped `array` overload on the generic name.
- [ ] `function demo(__CallableParametersTuple<callable<string, int, bool>> $args): string { return $args[0]; }` is `string` (FOUND_BUGS #8). Union-of-closures `__CallableReturnType` still mismatches a single `int` return.

### Date + operators

- [ ] `getdate()` field access: `$d->seconds` / `$d->timestamp` (key `0`).
- [ ] `new \DatePeriod($start, $interval, $end)` type-checks (3-arg). `getIterator()` yields `TDate`.
- [ ] `$interval->d` is `int`.
- [ ] `$dt < $other` / `$dt <=> $other` type-check (`DateTimeInterface` right operand) and **emit as native operators**.
- [ ] `$dt + $interval` type-checks as `DateTime` / `DateTimeImmutable` and **emits** `$dt->add($interval)` (or the spliced equivalent), not `$dt + $interval`.
- [ ] `$dt - $otherDt` emits `$dt->diff($otherDt)` and is `\DateInterval`.
- [ ] `date_parse()` result fields are the struct, not `array`.
