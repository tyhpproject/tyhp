# Implementation Plan: Story 27.1 — Callable Shapes and `type Name = struct { };`

> **Roadmap position:** Story 27.1 — **Tier 2 — DX & Ecosystem** (after **27**, before **27.2** / **27.3** then deferred **22**; **implement after 27**)
> **Direct dependencies (new numbering):** **27** (object-shape alias machinery, `\Tyhp\Type::is` descriptors), **21.6** / **21.9** (`callable<>` / `\Closure` / `\Fiber` / `Expression` / `PropertyPath` contract to replace), **11** (structs), **16.5** (callable utilities)
> **Do not implement until Story 27 is done.** Copy object-shape assignability / guards / harvest patterns; do not invent a second shape system.
> **Follow-on:** Story **27.2** (block-target extension syntax; may run in parallel), then Story **27.3** (split four source repos)
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Source:** Callable / struct shape unification (`object` / `callable` / `struct`)
> **Branch:** TBD
> **Prerequisites:** Stories through **21.12** and **27**. Optimizer (23–24), `internal` enforcement (25), and `?->` assignment (26) are **not** prerequisites.

This story **replaces** return-last `callable<>` (including `callable<..., TReturn>`, postfix `T...` on `callable`, and Rest splice-as-`callable` generics) with **callable shapes** spelled `callable(…): R`. It also moves named structs onto `type` aliases: `type Name = struct { … };` (delete `struct Name { }`). The three gradual PHP bases and their refinements:

| Base (untyped) | Shape |
|---|---|
| `object` | `object { public function now(): \DateTimeImmutable; }` — **Story 27**, alias-only |
| `callable` | `callable(bool $b, int $i =): string` — this story, **alias or inline** |
| `struct` / `array` | `struct { string $name; ?int $age = null; }` — this story, **named as `type`**, inline in type position allowed |

There is no PHPStan/Psalm `\Closure(int): string`. `\Closure` stays a **class**: `\Closure<TCallableShape, TThis, TScope>` where `TCallableShape` is bare `callable` or a callable shape.

---

## Architecture Overview

### Callable shapes

Bare `callable` remains “invokable, signature unknown” (the same role `object` has for instances). A **callable shape** is `callable` plus a PHP-style parameter list and a return type.

```tyhp
type Mapper<T, U> = callable(T $in): U;
type Shutdown = callable(): void;
type MutatesList<T> = callable(array<T> &$items): void;

function array_map<T, U>(callable(T $item): U $callback, array<T> $array): array<U>;
```

- **Named shapes** are `type F = callable(…): R;` (generics live on the alias, same as object shapes).
- **Inline** `callable(T $x): U` is legal in every type position (parameters, returns, properties, bounds, unions). Object shapes stay alias-only; callable signatures are short enough to inline.
- A shape is assignable **to** bare `callable`. Bare `callable` / `mixed` are **not** assignable to a shape without a guard or a tyhpdef assertion.
- Invokable objects whose `__invoke` matches the signature assign to the callable shape (join with Story 27 object shapes).
- `new Mapper` / `new` of a callable-shape alias is illegal. Values come from `fn`, `function`, first-class callables, or a matching `__invoke` object.
- PHP hint erase is `callable` (or omitted). Never emit a synthetic class.

### Parameter names and defaults

Names are optional. A list may mix named and unnamed parameters. Names enable named-argument checking when the value is called (`$cb(b: true)`). Unnamed parameters have no named-argument key (`__CallableParametersStruct` only includes named slots; the tuple form still has a position).

`=` marks **optional**, nothing else. The checker stores a flag, not an initializer. A written default expression is parsed and **discarded** — not type-checked, not compared, not part of assignability or type equality. `= false` and `= true` and bare `=` are the same shape. Space around `=` is optional (`bool=` / `bool =`).

These four spellings are equivalent (second parameter optional `bool`):

```tyhp
callable(int $i, bool $b = false): string
callable(int, bool = false): string
callable(int $i, bool $b =): string
callable(int, bool=): string
```

Prefer the valueless form in printers / generated tyhpdefs (`bool $b =` or `bool =`) so a discarded initializer cannot be mistaken for a contract. Arity facets still come from “has `=`” (a two-arg shape with an optional second still assigns to the one-arg shape).

Unnamed variadic: `callable(int ...$args): R` and `callable(int ...): R` are the same (homogeneous variadic).

### Grouping `(typeExpr)`

Add `( typeExpr )` as a type atom (not only intersection-in-union, not only typed locals). Nested callables are **right-associative**; extra parens are style on a straight curry:

```tyhp
callable(bool $b): callable(string $s): callable(int $i): array
callable(bool $b): (callable(string $s): (callable(int $i): array))
```

Parentheses are required when the **function type** is an arm of `|` / `&`:

```tyhp
(callable(int $x): int) | null
(callable(User $u): bool) & \Countable
(callable(int $x): int) | (callable(string $s): int)
```

Nullable **callable** vs nullable **return**:

```tyhp
?callable(int $x): string      // null | (int → string)
callable(int $x): ?string      // int → string|null
```

Prefix `?` binds to the whole shape; `?` on the return binds inside.

### Unknown arity vs variadic

| Spelling | Meaning |
|---|---|
| `callable(...): bool` | Any parameters, returns `bool`. **Not** a value type and **not** directly invokable. Same role as today’s `TCallable extends callable<..., bool>`. Ellipsis is the **only** thing in the parameter list. |
| `callable(T ...$args): R` | Homogeneous PHP variadic. Invokable. Replaces postfix `callable<T..., R>`. |
| `callable(mixed ...$args): bool` | Invokable; every argument `mixed`. Not the unknown-arity wildcard. |

A parameter typed `callable(...): bool` is an error (same as today’s `callable<..., TReturn>` as a value). Use it as a bound: `TCallable extends callable(...): bool`.

### What `callable<>` must not survive

Delete the generic `callable<…>` spelling from the grammar, checker, tyhpdefs, runtime packages, and docs:

- Return-last (`callable<string, int>` — last is return)
- `callable<void>` as zero-arg returning void
- `callable<..., TReturn>`
- Postfix `T...` **on `callable`** (keep postfix `T...` if it still means PHP variadic elsewhere; do not leave a `callable<>` host)
- Rest splice *as* `callable` type arguments (`callable<Rest<T>, R>` → a real parameter list)

Mechanical rewrite of `tyhpdef/php`, overlays, `tyhp/core`, `tyhp/async`, `tyhp/lambda`, tests, and user docs is in scope. Greenfield: no dual spelling, no deprecation window.

### `\Closure` / `\Fiber` / `Expression` / utilities

Keep:

- `\Closure<TCallableShape, TThis, TScope>`
- `\Fiber<TResume, TCallableShape>`
- `\Tyhp\Expression<TCallableShape>` / `\Tyhp\PropertyPath<TCallableShape>`
- `__CallableReturnType<TCallable>` / `__CallableParametersStruct<TCallable>` / `__CallableParametersTuple<TCallable>` / `__CallableParametersSlice`

`TCallableShape extends callable` means bare `callable` **or** a callable shape. Example: `\Closure<callable(int $i): string>` or `\Closure<Mapper<int, string>>`.

First-class callables (`strlen(...)`, `$obj->m(...)`) infer a callable shape, not a `callable<>` facet.

### `is` / `instanceof`

`$fn is Mapper<int, string>` / `$fn instanceof Mapper<int, string>` is a **shape guard** (`\Tyhp\Type::is`, likely `is_callable` plus optional reflection later). Same operator as Story 27 object-shape guards. Prefer `is` in docs.

### Structs as `type Name = struct { };`

Named structs are type aliases whose RHS is a struct shape. **Delete** the `struct Name { }` declaration form (regex replace across the tree).

```tyhp
type Point = struct {
    float $x;
    float $y;
};

type UserProfile = struct {
    string $name;
    string $email;
    int $age;
    ?string $bio;
};

Point $p = new Point() with [x => 1.0, y => 2.0];
```

| Kind | Base | Value | `new Alias` |
|---|---|---|---|
| Object shape (27) | `object` | instance | **illegal** |
| Callable shape | `callable` | invokable | **illegal** |
| Struct shape | `array` / `struct` | associative array | **legal** — builds the array (`new` + `with`) |

`new Point() with […]` stays array construction (required keys, defaults). That is not a hole in the object-shape rule: struct aliases **have** a construction form.

Keep expression `new struct { float $x; float $y; } with […]` for one-off values (already shipped).

**Inline in type position** is allowed:

```tyhp
function origin(): struct { float $lat; float $lng; } {
    return new struct { float $lat; float $lng; } with [lat => 0.0, lng => 0.0];
}
```

Grouping applies: `(struct { float $lat; float $lng; }) | null`.

**`extends` on the struct RHS** keeps today’s field-copy semantics:

```tyhp
type Child = struct extends Parent {
    int $extra;
};
```

`Parent & struct { int $extra; }` is the intersection spelling. Struct–struct intersection **merges** keys; a conflicting key type is an error.

Generic structs: `type Box<T> = struct { T $value; };`. `T extends struct`, `ArrayAccessShape<TStruct>`, `__Properties`, `__CallableParametersStruct`, and `ObjectHelper::with` keep working: `struct` remains the base type of every struct shape.

`$x is Point` is already a shape guard (structs erase to `array`). Align wording with object / callable shape guards.

### Erase / emit

| Site | PHP |
|---|---|
| Callable shape (named or inline) | `callable` (or omitted) |
| Struct shape (named or inline) | `array` (existing struct erase) |
| `new` of a struct-shape alias | existing struct array construction |
| `new` of an object- or callable-shape alias | error (27 already for objects) |
| `$x is CallableShape` / `$x is StructAlias` | `\Tyhp\Type::is` (existing struct path + new callable descriptor) |

---

## Pipeline

```
Story 27 (object shapes + __New<T>)
    │
    ▼
┌──────────────────────────────────────────────────────────┐
│  STORY 27.1: Callable shapes + type Name = struct { };    │
│                                                          │
│  Phase 1: Grammar — callable(…): R, grouping, struct RHS │
│  Phase 2: Delete callable<> + rewrite checker utilities  │
│  Phase 3: Assignability, invoke, named args, __invoke    │
│  Phase 4: Closure / Fiber / Expression / FCC inference   │
│  Phase 5: Struct declaration → type alias + inline types │
│  Phase 6: is/instanceof descriptors                      │
│  Phase 7: Mechanical rewrite (tyhpdef/php, runtime, tests)│
│  Phase 8: LSP                                            │
│  Phase 9: Tests, docs, conformance                       │
└──────────────────────────────────────────────────────────┘
    │
    ▼
Story 27.2 — block-target extension syntax — then Story 27.3 — split four source repos — then skip deferred Story 22 — then Tier 3
```

### Design principles

1. **The type is the keyword.** `object` / `callable` / `struct` — not `class`, not `fn`, not `function` in type position.
2. **Inline when the RHS is small.** Callable signatures and struct field lists may appear in `typeExpr`. Object bodies stay alias-only (Story 27).
3. **One callable spelling.** No leftover `callable<>`. `\Closure` is not a signature syntax.
4. **`new` follows constructability.** Struct aliases construct arrays; object and callable aliases do not construct.
5. **Greenfield rewrite.** Update every `callable<` and `struct Name {` in-tree. No dual emit, no deprecation.
6. **Defaults are optionality.** `=` on a callable-shape parameter is a flag. Names are optional. Initializers are not part of the type.

### Diagnostic codes

Add `MessageCode` values in the **4300–4399** feature-checker band at implementation time (`MessageCode.cs` + both `.resx` files). Do **not** reuse numbers already assigned (including Story 27’s new codes). See `CONVENTIONS.md`. Do not document specific numbers in this plan.

Expected diagnostics (names indicative):

- `callable<>` / return-last spelling no longer parses (or parses as an error)
- `callable(...): R` used as a value / direct call
- `new` of a callable-shape alias
- Bare `callable` assigned to a callable shape without a guard
- Named-argument mismatch against a named callable shape (unnamed slots cannot be targeted by name)
- `struct Name { }` declaration form no longer parses
- Conflicting keys on struct–struct intersection
- Ungrouped `(callable(…): R)|null` / DNF that needs `(typeExpr)`

---

## Tyhp / tyhpdef examples

### Inline callable (stdlib style)

```tyhp
<?tyhpdef

function \array_map<T, U>(
    callable(T $item): U $callback,
    array<T> $array
): array<U>;

function \usort<T>(array<T> &$array, callable(T $a, T $b): int $callback): true;

function \array_filter<T>(
    array<T> $array,
    ?callable(T $value): bool $callback = null
): array<T>;
```

### Names optional, `=` is optionality only

```tyhp
type WithName = callable(int $i, bool $b = false): string;
type NoNames = callable(int, bool = false): string;
type ValuelessDefault = callable(int $i, bool $b =): string;
type Compact = callable(int, bool=): string;
```

`WithName` and `ValuelessDefault` are the same type. `NoNames` and `Compact` are the same type. Positionally all four accept the same implementations; only shapes with `$b` check `$cb(b: …)`.

### Named callable + unknown arity bound

```tyhp
<?tyhp

type Predicate<T> = callable(T $value): bool;

function allMatch<T>(array<T> $items, Predicate<T> $p): bool {
    foreach ($items as $item) {
        if (!$p($item)) {
            return false;
        }
    }
    return true;
}
```

```tyhp
<?tyhpdef

function iterator_apply<TCallable extends callable(...): bool>(
    \Traversable $iterator,
    TCallable $callback
): true;
```

### Closure / expression trees

```tyhp
\Closure<callable(int $i): string> $parser;
\Tyhp\Expression<callable(User $u): bool> $expr = fn ($u) => $u->age > 18;
```

### Struct alias

```tyhp
<?tyhp

type Point = struct {
    float $x;
    float $y;
};

function midpoint(Point $a, Point $b): Point {
    return new Point() with [
        x => ($a->x + $b->x) / 2.0,
        y => ($a->y + $b->y) / 2.0,
    ];
}
```

---

## Phase 1: Grammar — callable shapes, grouping, struct-as-alias RHS

### Phase Overview

Parse `callable(…): typeExpr` in `typeExpr` (Tyhp and tyhpdef). Parse `( typeExpr )` as a type atom. Allow `tyhpStructShape` on a `type` alias RHS (and in `typeExpr` for inline structs). Stop parsing `struct Name { }` as a declaration and stop parsing `callable<…>` as a generic type.

### Deliverables

- `Tyhp/TyhpLang/Grammar/TyhpParser.g4` — `callableType`, grouping, `tyhpStructShape` on alias RHS / `typeExpr`; remove `struct` declaration production and `callable` generic arguments
- Regenerated parser files via `./compile_grammar.sh`

### Implementation Details

Sketch (names indicative):

```antlr
callableType
    : T_CALLABLE T_OPEN_ROUND_BRACE
        ( T_ELLIPSIS | callableShapeParameterList )?
      T_CLOSE_ROUND_BRACE returnType
    ;

callableShapeParameter
    : typeExpr name? ( T_SYM_EQUAL expression? )?
    ;

groupedType
    : T_OPEN_ROUND_BRACE typeExpr T_CLOSE_ROUND_BRACE
    ;
```

`expression` after `=` is optional. Do not reuse the PHP parameter production if it requires a name or a real default.

- After `callable ( … )`, `:` continues a type. Expression `fn ( … ) =>` / `function ( … ) {` stay expressions.
- `T_CALLABLE` `(` is unique vs bare `callable` in `typeExpr`.
- Distinguishing type vs expression uses the same typeExpr vs expr split as `new`.
- `type Name = struct { … };` reuses the existing struct body (fields, `as` aliases, `extends`).
- Inline `struct { … }` in `typeExpr` is the same body without a name.
- `function f(object { } $x)` remains illegal (Story 27). `function f(callable(int $i): void $c)` and `function f(struct { int $n; } $s)` parse.

### Acceptance Criteria

- [x] `callable(int $i): string` parses in parameter / return / alias RHS / generic bound
- [x] `callable(int $i, bool $b = false): string`, `callable(int, bool = false): string`, `callable(int $i, bool $b =): string`, and `callable(int, bool=): string` all parse
- [x] `callable(...): bool` parses; `callable(T ...$args): R` and `callable(T ...): R` parse as a variadic
- [x] `(callable(int $x): int) | null` parses; `callable(int $x): int | null` is return `int|null`
- [x] `type Point = struct { float $x; float $y; };` parses
- [x] `struct Point { }` does **not** parse as a declaration
- [x] `callable<string, int>` does **not** parse as a callable type
- [x] `./compile_grammar.sh` then `dotnet build` succeed; no new ANTLR ambiguities

---

## Phase 2: Delete `callable<>` and retarget utilities

### Phase Overview

Remove return-last `callable` generic construction from the binder, checker, arity-facet builder, and printers. Retarget `__CallableReturnType` / parameters struct/tuple/slice, pack splice, and `callable<..., R>` call sites to callable shapes.

### Deliverables

- Checker / binder: callable types are shapes (or bare `callable`)
- `CallableArityFacetBuilder` / `ArityFacetExpansion`: facets come from a parameter list + defaults, not `<>` arity prefixes
- Printers / tyhpdef writers emit `callable(…): R` using valueless `=` for optional parameters

### Implementation Details

- Presence of `=` still produces arity facets (a two-arg callback with an optional second still assigns to the one-arg shape). Drop any parsed initializer; do not keep it on the type.
- `callable(...): R` is constraint-only (not invokable, not a value).
- Pack splice that today happens *inside* `callable<>` becomes ordinary parameters on the shape (or a documented utility applied to a shape). Do not keep a hidden `<>` channel.
- Story 21.9 docs and diagnostics that mention `callable<..., TReturn>` / `T...` on `callable` are rewritten in Phase 9; checker codes that exist only for `<>` are removed or retargeted.

### Acceptance Criteria

- [x] No remaining parser/binder path constructs `callable<T, U>`
- [x] `__CallableReturnType<callable(int $i): string>` is `string`
- [x] `T extends callable(...): bool` still type-checks as a bound
- [x] Direct `callable(...): bool $cb` is an error

---

## Phase 3: Assignability, invoke, named arguments, `__invoke`

### Phase Overview

Callable-shape assignability (parameter contravariance, return covariance, extra optional parameters on the implementation). Optional-vs-required uses the `=` flag only. Named arguments when that slot has a name. `__invoke` objects assign when the method matches.

### Acceptance Criteria

- [x] `callable(int $i): string` accepts a matching function / `fn` / FCC / `__invoke` object
- [x] Bare `callable` does not assign to a shape
- [x] A shape assigns to bare `callable`
- [x] `callable(int $i, bool $b = false): string` and `callable(int $i, bool $b =): string` are the same type
- [x] `callable(int, bool = false)` and `callable(int, bool = true)` assign to each other (initializer ignored)
- [x] `callable(int $i, bool $b =): string` and `callable(int, bool=): string` are interchangeable positionally; only the named form checks `$cb(b: …)`
- [x] A function `function f(int $i, bool $b = true): string` satisfies `callable(int, bool = false): string`
- [x] `$cb(name: 'x')` checks against the shape’s `$name` when present
- [x] Missing names: named arguments are not checked against invented names

---

## Phase 4: `\Closure` / `\Fiber` / `Expression` / FCC inference

### Phase Overview

`TCallableShape` accepts callable shapes. Infer callable shapes for first-class callables and `fn` / `function` values. `as` for a specific Closure still uses a shape argument.

### Acceptance Criteria

- [x] `\Closure<callable(int $i): string>` type-checks (defaults for `TThis` / `TScope`)
- [x] `Expression<callable(User $u): bool> $e = fn ($u) => $u->age > 18` still captures
- [x] `strlen(...)` infers `callable(string $string): int` (or equivalent tyhpdef names)
- [x] No `\Closure(int): string` spelling

---

## Phase 5: Struct declaration → type alias

### Phase Overview

Binder/checker/emitter treat `type Name = struct { };` as today’s named struct. `new Name() with` constructs an array. Inline `struct { }` in type position is a nameless struct type (equality by fields). Delete `struct Name { }`.

### Acceptance Criteria

- [x] Existing `new Point() with […]` programs work after the rewrite to `type Point = struct { };`
- [x] `new Mapper` stays illegal for a callable alias; `new ClockShape` stays illegal
- [x] Inline `struct { float $x; float $y; }` is a legal return type
- [x] `type Child = struct extends Parent { int $extra; };` copies fields
- [x] `T extends struct` still matches struct-shape aliases

---

## Phase 6: `is` / `instanceof` descriptors

### Phase Overview

Callable-shape guards go through `\Tyhp\Type::is` (Story 27 path). Struct aliases already do; keep that and align messages. Source aliases may keep `\Tyhp\Type` factories (Story 21.9).

### Acceptance Criteria

- [x] `if ($fn is Predicate<int>)` narrows and does not emit `instanceof Predicate`
- [x] `$x is Point` for a struct alias still works
- [x] `$x is User` for a real class still emits native `instanceof`

---

## Phase 7: Mechanical rewrite

### Phase Overview

Replace every in-tree `callable<…>` and `struct Name { }` (Tyhp, tyhpdef, tests, examples, runtime `tyhp_src`). Re-emit **compiled** first-party packages (`core`, `async`, `lambda`, `compiler`, `decimal`) with `runtime/packages/base-build-all.sh` **after** the compiler accepts the new spelling — do not hand-edit `src/` or `dist/`. Companion `tyhpdef/*` packages have no Tyhp emit; only their `.tyhpdef` (especially Layer 3 overlays that already wrote `callable<>`) need the spelling rewrite.

### Scope (indicative)

- `tyhpdef/php` and `tyhpdef/php-ext-*` Layer 3 overlays (Standard.Callables, Core Closure, Date/OpenSSL structs, …)
- `runtime/packages/*/tyhp_src` (`core`, `async`, `lambda`, `compiler`, …)
- Companion `tyhpdef/*` **overlays** that already use `callable<>` (doctrine-collections, guzzlehttp-promises, psr-event-dispatcher, illuminate-support, …). Generated Layer 1 stubs with no `callable<>` / `struct` do not need a pass
- Compiler tests / conformance fixtures
- `docs/content/` pages listed in Phase 9
- `AIDevGuide/`

### Acceptance Criteria

- [x] Repo-wide search finds no `callable<` type arguments (except historical notes in this plan / ROADMAP)
- [x] Repo-wide search finds no `struct Name {` declarations in `.tyhp` / `.tyhpdef` (expression `new struct {` remains)
- [x] `dotnet test` for affected fixtures is green after the rewrite
- [x] Compiled runtime packages re-emit with `./runtime/packages/base-build-all.sh`

---

## Phase 8: LSP

Hover and completion treat callable shapes and struct-shape aliases as types. Do not offer `new` on callable aliases. Keep `new` + `with` on struct aliases.

### Acceptance Criteria

- [x] Hover on `callable(int $i): string` / `Mapper` describes a callable shape
- [x] Hover on `Point` describes a struct shape
- [x] `new Mapper` is not a completion snippet; `new Point` is

---

## Phase 9: Tests, docs, conformance

### Docs to update in this story’s implementation (not necessarily this planning edit)

- `docs/content/tyhp_0150_newTypes.md` — replace `callable<>`; Closure examples use shapes
- `docs/content/tyhp_0400_structs.md` / `tyhpdef_structs.md` — `type Name = struct { };`; inline type-position structs
- `docs/content/tyhp_0700_typeAliases.md` / `tyhpdef_typeAliases.md`
- `docs/content/tyhpdef_functions.md` / `quickref.md` / `quickref_tyhpdef.md`
- `docs/content/tyhp_3000_parsableLambdas.md` — `Expression<callable(…): R>`
- `docs/content/tyhp_3400_newTypeConstraint.md` — cross-link the three bases
- Diagnostics that mention `callable<>` / `callable<..., TReturn>`
- Area `technical-guide.md` files (grammar, checker, binder)

### Acceptance Criteria

- [x] Unit tests cover callable assignability, optional `=` (with and without an initializer), unnamed parameters, unknown arity, grouping, struct alias `new`, inline structs
- [x] Docs match the shipped contract
- [x] Message consistency gate passes for new/removed codes

---

## Out of scope (v1)

- Object-shape `object { }` (Story 27)
- PHPStan `\Closure(int): string`
- `fn` / `function` as callable-shape introducers
- Dual `callable<>` support
- `struct Name { }` declaration sugar
- Signature-accurate runtime reflection of callables (existence / `is_callable` is enough for v1 guards)
- Story 29 reflection catalog details

---

## Dependencies

- **Requires:** Story **27**; Stories **21.6** / **21.9** (callable/`Closure` contract); Story **11** (structs); Story **16.5** (callable utilities)
- **Provides:** Callable shapes; `type Name = struct { };`; type-position `struct { }`; `(typeExpr)` grouping; one callable spelling
- **Unblocks:** Story **27.3** (split runtime / docs / AIDevGuide / IDE plugin into dedicated repos); Story **27.2** (block-target extension syntax) may run in parallel and does not wait on this story; Story 30 docs polish; Story 29 may describe callable/struct shapes in reflection later
