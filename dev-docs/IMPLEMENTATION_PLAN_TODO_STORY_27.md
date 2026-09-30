# Implementation Plan: Story 27 — Object Shapes and `__New<T>`

> **Roadmap position:** Story 27 — **Tier 2 — DX & Ecosystem** (after **21.12**, before **27.1** / **27.2** / **27.3** then deferred **22**; **implement next**)
> **Direct dependencies (new numbering):** 08, 08.5, 11, 20, **21.12** (Tier 2 complete through 21.12)
> **Follow-on:** Story **27.1** (callable shapes + `type Name = struct { };`), then Story **27.2** (block-target extension syntax), then Story **27.3** (repo split)
> **Renumbered from:** legacy Story 20 (`new<TArgs...>` constructable object type — **replaced**); lifted from Tier 3 so the type language is in the beta
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** Object-shape design (`object { … }` structural types + constructability via `__New<T>`)
> **Branch:** TBD
> **Prerequisites:** Stories through **21.12** are complete. Required pieces: checker (08), `__ClassName` / `__` utilities (08.5), type aliases (11), tyhpdef generate / `package.tyhpdef` (20). Optimizer (23–24), `internal` enforcement (25), and `?->` assignment (26) are **not** prerequisites.

This story **replaces** the planned `new` / `new<TArgs...>` built-in type. Constructability is a constraint on an **object shape**, not a parallel arity facet (`new<string, int>`). There is no `new` keyword in type position. The shape introducer is **`object`**, not `class` (`new class { }` remains the PHP/Tyhp anonymous-class **expression**).

---

## Architecture Overview

### What an object shape is

An object shape is a **structural object type**: a predicate on an instance. It is the object-side counterpart of a struct (array shape). Two values may match the same shape and still be different nominal PHP classes.

A shape exists only as the **right-hand side of a `type` alias**. The shape spelling is `object { … }` (the PHP type being refined). The alias name is how the shape is used in every other type position (parameters, properties, returns, generic bounds, unions, `__ClassName<…>`, `__New<…>`).

```tyhp
<?tyhpdef

type ClockShape = object {
    public function now(): \DateTimeImmutable;
};

type FallbackConstraint = object {
    public function getPrettyString(): string;
    public function __toString(): string;
};

function createClock(): ClockShape;
function parseConstraint(string $pretty): \Composer\Semver\Constraint\ConstraintInterface | FallbackConstraint;
```

```tyhp
<?tyhp

type HasQuery = object {
    public function query(string $sql): mixed;
};

type StringCtor = object {
    public function __construct(string $value): void;
};

function takeQuery(HasQuery $q): mixed {
    return $q->query('select 1');
}

function create<T extends __New<StringCtor>>(string $value): T {
    return new T($value);
}
```

`ClockShape` in type position means `object` refined by that shape. Callers do not write `object & ClockShape` (the alias already includes `object`).

### What an object shape is not

| Forbidden | Why |
|---|---|
| `object { … }` except as a `type` alias RHS | Keeps type positions readable; the alias is the name |
| `new ClockShape` | Not a PHP class |
| `ClockShape::foo()` / `ClockShape::class` | Not a class name |
| `extends ClockShape` / `implements ClockShape` | Not a declaration |
| `$x->__construct(…)` through a shape | `__construct` on a shape is a **constructability** signature for `__New` / `new T()`, never an instance method |
| Magic (`__get` / `__call` / …) satisfying other members | A magic method matches only if the shape lists **that** magic method |

There is **no** `extends` / `implements` on the `object { }` header (`object extends Foo` is not PHP). Nominal parents are intersections:

```tyhp
type TimestampedLogger = \Psr\Log\LoggerInterface & object {
    public function getLastLogAt(): \DateTimeImmutable;
};
```

That is `\Psr\Log\LoggerInterface` plus a structural `getLastLogAt()`. Extra members on a concrete type are still allowed (width subtyping).

### Assignability (instance types)

A value of nominal type `C` (class, interface, enum object, or another object shape) is assignable to shape `S` when:

1. **Width:** every **public** member of `S` exists on `C` (extra members on `C` are allowed).
2. **Methods:** each method on `S` is callable as that method (existing callable assignability: parameter contravariance, return covariance, defaulted extra parameters on `C` allowed).
3. **Properties:** name match plus readonly / hook variance (a writable shape property must not match a `readonly` or get-only hooked property).
4. **Visibility:** shape members are **public only**. `protected` / `private` on `C` do not satisfy the shape.
5. **`object` / `mixed` are not assignable to `S`** without a shape guard or a tyhpdef assertion. `S` is narrower than `object`.

Empty `type X = object {};` is illegal (`object` stays the spelling).

Two shape aliases with the same members are structurally equal. Recursive shape aliases that mention their own name (`type Node = object { public function parent(): ?Node; }`) are allowed (exception to circular-alias `TYHP3029` when the cycle is an object-shape alias).

### `__New<T>`

`__New<T>` is a built-in utility type (same family as `__FunctionReturnType`). `T` **must** be an object-shape alias (checker rule).

Values of `Shape` and of `__New<Shape>` are **object instances** matching that shape. `__New` adds: the **class of that instance is concrete and `new`-able** with the shape’s constructor rule.

| | `T extends ClockShape` | `T extends __New<ClockShape>` |
|---|---|---|
| Values of `T` | instances matching the shape | instances matching the shape whose class is `new`-able as the shape |
| Abstract / interface | allowed if members match | rejected |
| Private / protected constructor | irrelevant | rejected |
| `new T(...)` | error | allowed; arguments from the shape’s `__construct` |
| No `__construct` on the shape | fine (instance API only) | means PHP **default 0-arg** public constructor |

**Implied 0-arg:** `__New<HasQuery>` where `HasQuery` has no `__construct` requires `new T()` with zero arguments **and** `query()`. `PDO` matches `HasQuery` as an instance type and does **not** match `__New<HasQuery>`.

**Written 0-arg:** `public function __construct(): void;` (or `();`) on the shape matches any public constructor that can be invoked with zero arguments, including “no constructor in source” and “all parameters have defaults.”

**Written args:** `public function __construct(string $dsn, ?string $user = null): void;` means every call the shape allows must be accepted by the class constructor (callable compatibility). Extra optional parameters on the class are allowed. A class with only `__construct(string $dsn)` does not satisfy a shape that also allows a second argument.

`__construct` on a shape is never callable as `$instance->__construct(...)`.

### Class names: `__ClassName<Shape>` and `__ClassName<__New<Shape>>`

`Shape` / `__New<Shape>` are instance types. A **class-name string** uses the existing `__ClassName<T>` utility:

| Type | Meaning |
|---|---|
| `__ClassName<Shape>` | String naming **some** class whose instances match `Shape` (may be abstract; may have a non-public constructor). **Not** an exact-name brand — shapes have no PHP class name. |
| `__ClassName<__New<Shape>>` | String naming a **concrete, public-`new`-able** class matching `Shape`. `new $cls(...)` is allowed with the shape’s constructor arguments. |

Nominal `__ClassName<Foo>` (exact class `Foo`) is unchanged and remains invariant.

`new $cls()`:

- `__ClassName<object>` / bare `__ClassName` — existing behavior.
- `__ClassName<NominalClass>` — existing behavior.
- `__ClassName<Shape>` — **error** (`new` requires `__New`).
- `__ClassName<__New<Shape>>` — allowed; check arguments against the shape constructor.

Guards (extend existing `\class_exists<T>` rather than a parallel API):

- `\class_exists<ClockShape>($name)` narrows to `__ClassName<ClockShape>` and must establish that the named class matches the **instance** shape (compile-time for a string literal; runtime helper + compile-time brand for a dynamic string).
- `\class_exists<__New<ClockShape>>($name)` narrows to `__ClassName<__New<ClockShape>>` (shape + constructability).

Literal class names that already match may be assigned to `__ClassName<ClockShape>` / `__ClassName<__New<ClockShape>>` without a guard, the same way `'App\\User'` assigns to `__ClassName<User>` when it is that class.

### `is` / `instanceof`

In Tyhp these are the same operator. Both must work. Documentation and examples use `$x is ClockShape`.

`$x is ClockShape` / `$x instanceof ClockShape` is a **shape guard**, not PHP `instanceof` of a class. Emit is `\Tyhp\Type::is(...)` (same path as structs and unmarked aliases). After a successful guard, the checker narrows to `object & ClockShape` (or `__New<ClockShape>` if that was the tested type).

`$x is ClockShape` does **not** make `$x->__construct()` legal.

### Runtime `Type` descriptor

`$x is ClockShape` needs a `\Tyhp\Type` value describing the shape. That descriptor is for guards / optional reflection matching. It must **not** construct instances.

- `.tyhp` source aliases may emit a `ClockShape(): \Tyhp\Type` factory like other source aliases.
- Tyhpdef aliases have no PHP function; the compiler **inlines** the descriptor at each `is` / `instanceof` site (or passes it to a `tyhp/core` helper).
- Existence-only matching is the v1 runtime (methods / properties present). Signature-accurate reflection may be added later behind the same `\Tyhp\Type::is` API, with caching inside `tyhp/core`.

### Erase / emit

| Site | PHP |
|---|---|
| Parameter / return / property typed as a shape or `__New<Shape>` | `object`, or nominal conjuncts from an intersection (`LoggerInterface & ClockShape` → `LoggerInterface` when that is a legal PHP hint) |
| Generic bound `T extends __New<Shape>` | erased with other generics |
| `new T(...)` | `new $erasedClass(...)` (existing generic-class emit) |
| `new $cls(...)` with `__ClassName<__New<Shape>>` | native `new $cls(...)` |
| Shape name in `instanceof` | never native `instanceof Shape`; always `\Tyhp\Type::is` |

Never emit a synthetic PHP `class ClockShape`.

### Harvest / `package.tyhpdef`

PHP `return new class { … }` (and Tyhp `return new class`) must not collapse to `object`.

- Invent a shape alias (collision-safe name, e.g. same namespace `OwningType_method_Return` or a reserved `__anon\` prefix).
- Use that alias as the return (or union arm).
- Skip writing a PHP class for the anonymous type (existing `anonClass@` skip stays).

`__FunctionReturnType<'createClock'>` / `__MethodReturnType<Plugin, 'parseConstraint'>` recover an inline-harvested shape when the author did not pick a nicer alias. Overlays may replace the generated alias with a hand-written one.

### Pipeline

```
Stories through 21.12
    │
    ▼
┌──────────────────────────────────────────────────────────┐
│  STORY 27: Object shapes + __New<T>                      │
│                                                          │
│  Phase 1: Grammar (object { } only on type-alias RHS)    │
│  Phase 2: AST + binder (shape alias target)              │
│  Phase 3: Checker assignability                          │
│  Phase 4: __New<T> + new T() / constructor matching      │
│  Phase 5: __ClassName<Shape> + class_exists<T>           │
│  Phase 6: is/instanceof + Type descriptor emit           │
│  Phase 7: Tyhpdef harvest + package.tyhpdef              │
│  Phase 8: LSP                                            │
│  Phase 9: Tests, docs, conformance                       │
└──────────────────────────────────────────────────────────┘
    │
    ▼
Story 27.1 — callable shapes + type Name = struct { };
```

### Design principles

1. **Shapes are aliases, not declarations.** `object { }` only on `type` RHS.
2. **Structural members, nominal parents via `&`.** Width subtyping + callable/property variance. No `extends`/`implements` on the `object { }` header.
3. **`__New` means constructable.** Missing `__construct` on a `__New<Shape>` is PHP default 0-arg, not “any constructor.”
4. **Class names are `__ClassName<…>`.** Instance types stay `Shape` / `__New<Shape>`.
5. **No `new` type keyword.** Avoids expression vs type `new` grammar.
6. **Public instance API only** on shapes, plus `__construct` as a constructability signature.
7. **Compile-time first.** Runtime shape guards exist so `object` can be narrowed; they are weaker than the checker unless reflection is opted into later.

### Diagnostic codes

Add `MessageCode` values in the **4300–4399** feature-checker band at implementation time (`MessageCode.cs` + both `.resx` files). Do **not** reuse numbers already assigned (PHP version gating, extern, generic defaults, expression trees, ArrayAccess shapes, Closure bind, …). See `CONVENTIONS.md`. Do not document specific numbers in this plan.

Expected diagnostics (names indicative):

- Shape used as `new` / `::` / `extends` / `implements` / `::class`
- `object { }` not on a `type` alias RHS
- Empty shape body
- Non-public member on a shape
- `__New<T>` where `T` is not an object-shape alias
- Type does not satisfy `__New<Shape>` (abstract, interface, enum, trait, non-public ctor, ctor arity)
- `new T()` where `T` is not `__New<…>`
- `new $cls()` where `$cls` is `__ClassName<Shape>` without `__New`
- Constructor argument mismatch against the shape
- `object` / `mixed` assigned to a shape without a guard
- `$x->__construct()` on a shape-typed value

---

## Tyhp / tyhpdef examples

### Instance duck typing

```tyhp
<?tyhp

type ClockShape = object {
    public function now(): \DateTimeImmutable;
};

function formatNow(ClockShape $c): string {
    return $c->now()->format('c');
}

formatNow(new \DateTimeImmutable()); // OK if DateTimeImmutable satisfies the shape
```

### Anonymous-class return (tyhpdef)

```tyhp
<?tyhpdef

type FallbackConstraint = object {
    public function getPrettyString(): string;
    public function __toString(): string;
};

function parseConstraint(string $pretty): \Composer\Semver\Constraint\ConstraintInterface | FallbackConstraint;
```

### Factories (`__New`)

```tyhp
<?tyhp

type ZeroArg = object {
    public function ping(): void;
};

type Named = object {
    public function __construct(string $name): void;
    public function ping(): void;
};

function makeZero<T extends __New<ZeroArg>>(): T {
    return new T();
}

function makeNamed<T extends __New<Named>>(string $name): T {
    return new T($name);
}
```

`makeZero<PDO>()` fails (PDO is not 0-arg). `takeQuery(new \PDO(...))` can succeed for `HasQuery` if `query` matches.

### Class-name values

```tyhp
<?tyhp

function spinUp(__ClassName<__New<Named>> $cls, string $name): Named {
    return new $cls($name);
}

string $raw = \getClassName();
if (\class_exists<Named>($raw)) {
    // $raw is __ClassName<Named> — instance shape only; new $raw is an error
}

if (\class_exists<__New<Named>>($raw)) {
    Named $obj = spinUp($raw, 'x');
}
```

### Shape guard

```tyhp
function fromObject(object $obj): void {
    if ($obj is ClockShape) {
        formatNow($obj);
    }
}
```

### Nominal + extra members

```tyhp
type TimestampedLogger = \Psr\Log\LoggerInterface & object {
    public function getLastLogAt(): \DateTimeImmutable;
};
```

This is `\Psr\Log\LoggerInterface` plus a structural `getLastLogAt()`.

---

## Phase 1: Grammar — `object { }` on `type` alias RHS only

### Phase Overview

Parse `type Name = object { … };` in `.tyhp` and `.tyhpdef`. Do **not** add `object { }` to `typeExpr` (that would allow parameter/return inline shapes). Do **not** parse `extends` / `implements` on the `object` token.

### Deliverables

- `Tyhp/TyhpLang/Grammar/TyhpParser.g4` — extend `tyhpTypeAlias`; add `tyhpObjectShape`
- Regenerated parser files via `./compile_grammar.sh`

### Implementation Details

#### 1.1 Alias RHS

```antlr
tyhpTypeAlias
    : T_TYHP_TYPE_ALIAS Identifier=name
        GenericArguments=tyhpGenericParameterDeclarations? T_SYM_EQUAL
        (TypeExpr=typeExpr | ObjectShape=tyhpObjectShape) T_SYM_SEMICOLON
    ;
```

`tyhpTypeAlias` is already shared by Tyhp and tyhpdef declaration rules.

#### 1.2 Shape header and body

```antlr
tyhpObjectShape
    : T_OBJECT T_OPEN_CURLY_BRACE StatementList=tyhpdefClassStatementList
        T_CLOSE_CURLY_BRACE
    ;
```

`T_OBJECT` is the existing PHP `object` type token. `object {` is unique vs bare `object` in `typeExpr`.

Reuse **tyhpdef** class member lists (signatures only: methods, properties, consts, hooked properties; semicolon-terminated). No method bodies, no `new class` expression, no trait `use` in v1 (list members explicitly). No `abstract` / `final` / `readonly` on the `object` token.

`__construct` / `__destruct` signatures are legal members. `__destruct` is instance API; `__construct` is constructability-only (enforced in the checker, not the grammar).

#### 1.3 Not in `typeExpr`

A parameter `function f(object { … } $x)` must fail to parse (or parse as a declaration and error). Only the alias production accepts `tyhpObjectShape`. Bare `object` as a type remains legal.

#### 1.4 Alias-RHS intersection with a nominal parent

Nominal parents for a shape (see architecture overview) are ordinary intersections on the alias RHS, not heritage on the `object` token — `\Psr\Log\LoggerInterface & object { … }` must parse. That needs its own alias-RHS-only production so plain nominal intersections (`A & B`, no shape item) are unaffected and keep parsing through `typeExpr` / `intersectionType`:

```antlr
tyhpObjectShapeIntersection
    : Items+=tyhpObjectShapeIntersectionItem
        (T_AMPERSAND_NOT_FOLLOWED_BY_VAR_OR_VARARG
            Items+=tyhpObjectShapeIntersectionItem)+
    ;

tyhpObjectShapeIntersectionItem
    : {this.looksLikeObjectShape()}? ObjectShape=tyhpObjectShape
    | NominalType=type
    ;
```

`looksLikeObjectShapeIntersection()` gates the alternative on `tyhpTypeAlias` by scanning the `&`-chain for at least one `object {` item (skipping shape bodies via balanced-brace counting) without consuming input, the same lookahead idiom as `looksLikeObjectShape()`. A nominal type or shape may appear on either side of `&`, and a chain may mix more than two items.

### Acceptance Criteria

- [x] `type Foo = object { public function bar(): void; };` parses in `.tyhp` and `.tyhpdef`
- [x] `type Foo = object extends Bar { };` does **not** parse
- [x] Generic `type Box<T> = object { public function get(): T; };` parses
- [x] `function f(object { } $x)` does not parse as an object shape
- [x] Existing `type Foo = int;`, `object $x`, and `class Foo { }` declarations still parse
- [x] `type Foo = \Psr\Log\LoggerInterface & object { … };` (and `object { … } & LoggerInterface`) parses; plain nominal intersections (`A & B`) are unaffected; `function f(A & object { } $x)` still does not parse
- [x] `./compile_grammar.sh` then `dotnet build` succeed; no new ANTLR ambiguities

### Dependencies

- **Requires:** Stories through 21.12 (type-alias grammar, tyhpdef class members)
- **Provides:** Parse trees for later phases

---

## Phase 2: AST and binder — shape alias target

### Phase Overview

Bind an object-shape alias to a dedicated type, not an `ObjectDeclarationSymbol`. The alias name resolves in type position; it must not resolve as a class for `new`, `::`, `extends`, or `implements`.

### Deliverables

- AST node for the shape (public members only; no `extends` / `implements` on the `object { }` header) attached to the type-alias declaration
- Binder: alias target = object shape; recursive self-references allowed
- Symbol queries used by `new` / `::` / heritage treat the alias as **not** a class

### Implementation Details

- Do not invent `anonClass@` identifiers or PHP FQNs for shapes.
- Member binding follows tyhpdef class members (public only; error on `private`/`protected`/`internal`).
- Nominal parents are ordinary intersections on the alias RHS (`LoggerInterface & object { … }`), not heritage on the shape header.
- `typeof(ClockShape)` / source-alias factory: produce `\Tyhp\Type` (Phase 6). No `::class`.
- Circular alias `TYHP3029`: if every participant in the cycle is an object-shape alias (or a union/intersection/generic wrapping one), allow the cycle. Keep `TYHP3029` for `type A = B; type B = A` of non-shape aliases.

### Acceptance Criteria

- [x] `ClockShape` in a parameter type resolves to the shape
- [x] `new ClockShape()` is a bind/check error (not “unknown class”)
- [x] `class X extends ClockShape {}` is an error
- [x] `type Node = object { public function parent(): ?Node; };` binds
- [x] Non-shape `type A = B; type B = A;` still errors

---

## Phase 3: Checker — structural assignability

### Phase Overview

Implement width subtyping from nominal object types (and other shapes) **to** an object shape. Reject `object` / `mixed` without a guard.

### Deliverables

- `TypeComparer` rules for object shapes (alongside struct structural rules and `object & StructType`)
- Property readonly/hook variance
- Public-only matching
- Magic methods match only themselves

### Implementation Details

- Reuse callable assignability for methods (same as checking that `C::foo` can be used as `S::foo`).
- Properties: required presence; getter-only shape property may match a read/write class property; a settable shape property must be settable on the class.
- `__construct` on a **target instance** shape does not require the source to expose a callable `__construct` method. Constructor matching is Phase 4 (`__New` only). A shape used only as an instance type may list `__construct`; that listing does not add `$x->__construct()` and does not affect instance assignability except as documentation for `__New`.
- Intersection: `Logger & ClockShape` requires both. `ClockShape` already includes `object`.
- Union: a value assignable to any arm is assignable to the union; a value assignable to a union is assignable to a shape only if every arm is.

### Acceptance Criteria

- [x] Class with extra methods assigns to a smaller shape
- [x] Missing method / incompatible signature does not assign
- [x] `protected function foo()` does not satisfy `public function foo()`
- [x] `__call` does not satisfy `foo()`
- [x] Explicit `__call` on both sides matches
- [x] `object` / `mixed` do not assign to a shape
- [x] `DateTimeImmutable` assigns to a shape of its public API (spot-check)

---

## Phase 4: `__New<T>` and `new T()`

### Phase Overview

Register `__New<T>` as a utility type. `T` must be an object-shape alias. Enable `new T(...)` iff `T` is bounded by `__New<Shape>` (or is a concrete class, existing rules). Match constructors with callable compatibility; missing shape `__construct` ⇒ 0-arg default.

### Deliverables

- `StructUtilityTypes` (or equivalent) registration for `__New`
- Checker: constraint satisfaction, `new T` argument checking
- Emitter: `__New<Shape>` in value position spells like a shape (`object` / nominal parents); bounds erase

### Implementation Details

- `__New<int>` / `__New<User>` / `__New<object>` are errors (not an object-shape alias).
- `T extends __New<Shape>` implies `T extends Shape` plus constructability.
- Abstract class, interface, trait, enum: fail `__New`.
- Non-public constructor: fail `__New`.
- `new T()` argument lists checked against the **shape** constructor (not an inferred “all prefixes of the class”). The class must accept those calls.
- `ArityFacetExpansion` may still compute which prefixes a **class** constructor supports when testing “can this class be invoked as the shape.” Do not reintroduce `new<TArgs>` facets on every class.

### Acceptance Criteria

- [x] `function f<T extends __New<ZeroArg>>(): T { return new T(); }` accepts a no-ctor class
- [x] Rejects abstract / interface / private ctor / required-arg ctor
- [x] `function f<T extends Shape>(): T { return new T(); }` errors
- [x] `__construct(string $a, int $b = 0)` on the shape accepts `new T($s)` and `new T($s, $i)` when the class can
- [x] `HasQuery` without ctor: instance-assign `PDO`; `__New<HasQuery>` does not accept `PDO`

---

## Phase 5: `__ClassName<Shape>` and guards

### Phase Overview

Special-case parametric `__ClassName<T>` when `T` is an object shape or `__New<Shape>`. Extend `\class_exists<T>()`. Restrict `new $cls` as specified in the architecture overview.

### Deliverables

- Checker assignability for class-name brands vs shapes
- `\class_exists<T>` tyhpdef/checker when `T` is a shape / `__New<Shape>`
- `new $cls(...)` argument checking for `__ClassName<__New<Shape>>`

### Implementation Details

- Do not change invariant exact-name `__ClassName<Foo>` for nominal `Foo`.
- String literal `'Vendor\\Concrete'` assigns to `__ClassName<Shape>` when `Concrete` structurally matches; assigns to `__ClassName<__New<Shape>>` when it also satisfies `__New`.
- Dynamic `string` still needs `\class_exists<…>`.
- Runtime for dynamic names: helper that loads/reflects the class (not an instance) and applies the v1 existence (and constructability) checks. Cache on the class name + descriptor identity.

### Acceptance Criteria

- [x] `__ClassName<ClockShape> $n` does not allow `new $n()`
- [x] `__ClassName<__New<Named>> $n` allows `new $n('x')` and rejects wrong args
- [x] `\class_exists<ClockShape>($s)` narrows correctly
- [x] Literal matching class name assigns without a guard

---

## Phase 6: `is` / `instanceof` and `\Tyhp\Type`

### Phase Overview

Lower `$x is Shape` / `$x instanceof Shape` to `\Tyhp\Type::is`. Provide descriptors. Source alias factory optional; tyhpdef inlines.

### Deliverables

- Emitter path (already special-cases non-class `is` targets)
- `tyhp/core` `\Tyhp\Type` shape descriptor + matcher (existence-only v1) in `runtime/packages/core/tyhp_src`; re-emit with `./runtime/packages/base-build-all.sh core` (do not hand-edit `src/` / `dist/`)
- Interop contract bump if a new public `Type` API is required (`CONVENTIONS.md` / Story 15)

### Implementation Details

- Same emit for `is` and `instanceof`.
- Narrowing in the checker: subject becomes the shape (intersection with prior type).
- `$x is ClockShape` does not enable `$x->__construct()`.
- Descriptor must not expose `newInstance` / `new $name` helpers that bypass `__New`.

### Acceptance Criteria

- [x] `if ($obj is ClockShape)` narrows and emits `\Tyhp\Type::is`
- [x] `$obj instanceof ClockShape` emits the same helper, not `instanceof ClockShape`
- [x] `$x is User` for a real class still emits native `instanceof`
- [x] Runtime: object missing `now()` fails the guard; object with `now()` passes v1
- [x] `$c->now()` works on a `ClockShape`-typed (or narrowed) receiver; `$c->__construct()` is 4359

---

## Phase 7: Tyhpdef harvest and `package.tyhpdef`

### Phase Overview

Stop collapsing anonymous-class returns to `object`. Generate shape aliases. Print inferred Tyhp `new class` returns as shapes.

### Deliverables

- `NativeTyhpdefGenerator` (and related): do not skip the **return type** of functions that return `new class`; still skip emitting a named class for `anonClass@`
- `package.tyhpdef` writer for compiled Tyhp libraries
- Overlay-friendly generated alias names

### Implementation Details

- Prefer one alias per declaration site (PHP anonymous classes are per-site).
- If the anonymous class `extends` / `implements` real types, generate an intersection of those nominal types with the object shape (same as a hand-written `LoggerInterface & object { … }`).
- Members: public instance members + `__construct` signature when present (for `__New`, not for instance calls).
- Hand overlays may `omit` the generated alias and supply a better name; last-wins overlay of the function return type is allowed.

### Acceptance Criteria

- [x] Harvest of `return new class { public function foo(): void {} }` yields a shape alias + that return type
- [x] `parseConstraint`-style union with a named class and an anonymous class is expressible
- [x] `tyhp build` library `package.tyhpdef` preserves `new class` return shapes
- [x] `__FunctionReturnType<'f'>` equals `f`’s shape return

---

## Phase 8: LSP

### Phase Overview

Hover, completion, and go-to-definition treat shape aliases as types. Do not offer `new Shape` or `Shape::`. Completion on a shape-typed receiver offers the shape’s public instance members (not `__construct`).

### Acceptance Criteria

- [x] Hover on `ClockShape` describes an object shape
- [x] Completion after `$c->` on `ClockShape $c` lists shape methods
- [x] `new ClockShape` is not a completion snippet

---

## Phase 9: Tests, docs, conformance

### Phase Overview

Checker/emitter/binder tests for every rule above. Conformance samples. User-facing docs. Runtime package tests for `\Tyhp\Type::is` shape matching.

### Docs to update in this story’s implementation (not necessarily this planning edit)

- `docs/content/tyhp_3400_newTypeConstraint.md` — planned/shipped contract (object shapes + `__New`)
- `docs/content/tyhp_0700_typeAliases.md` / `tyhpdef_typeAliases.md` — shape RHS
- `docs/content/tyhp_0150_newTypes.md` — `__New<T>`
- `docs/content/tyhp_0200_typeNarrowingAndGuards.md` — shape guards
- `docs/content/tyhp_2400_dynamicLanguageFeatures.md` — `__ClassName<Shape>`
- `docs/content/cli_tyhpdefGeneration.md` — anonymous-class harvest
- `AIDevGuide/guide/28-availability-gotchas.md` — remove “not in language” once shipped
- `docs/content/cli_interopContract.md` — if `Type` API grows
- Area `technical-guide.md` files (binder, checker, emitter, visitor, grammar)

### Manual verification sketch

1. Parse/bind/check examples in this document.
2. `new T()` factory runtime: compile a small `.tyhp` program, run PHP, greet/construct as in the old Story 27 runtime sample — expressed with `__New<Shape>`.
3. Confirm emitted PHP contains no `new<` type spellings and no synthetic shape class.
4. Confirm `$obj is ClockShape` runs as a helper and narrows in the checker.

### Acceptance Criteria

- [x] Unit tests cover assignability, `__New`, `__ClassName`, guards, harvest
- [x] `php -l` on emitted output
- [x] Docs match the shipped contract
- [x] Message consistency gate passes for new codes

---

## Out of scope (v1)

- Inline `object { }` in parameter/return/property types
- Callable shapes and `type Name = struct { };` (Story **27.1**)
- `default` / `empty` modifiers on `__construct`
- Trait `use` inside a shape body
- `readonly` / `abstract` / `final` on the `object` token
- Structural matching via `__get` / `__call`
- Replacing interfaces for PHP types that already have a named contract
- Story 29 reflection catalog details beyond `\Tyhp\Type::is` existence matching

---

## Dependencies

- **Requires:** Stories through **21.12**, especially 08 (assignability), 08.5 (`__ClassName`, utilities), 11 (aliases, structs), 20 (tyhpdef generate / `package.tyhpdef`)
- **Provides:** Object shapes; `__New<T>`; constructable generics; anonymous-class tyhpdefs; shape guards
- **Unblocks:** Story **27.1** (callable shapes + `type Name = struct { };`); Story **27.2** (block-target extension syntax) may run in parallel; Story **27.3** (repo split) after 27.2; Story 29 may describe shape types in reflection later; Story 30 docs polish
- **Packages:** no sweep of companion `tyhpdef/*`. Harvest (Phase 7) improves the **next** generate of those stubs. Compiled `tyhp/core` must rebuild for `\Tyhp\Type::is`.
)
