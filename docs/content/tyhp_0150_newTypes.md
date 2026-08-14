---
title: 'New and Changed Types in Tyhp'
status:
  tier: 0
  story: '04'
  state: complete
---

Tyhp introduces several new types and enhances existing PHP types with generic support. This page covers generic PHP types, new Tyhp-specific types, and internal compiler types. Built-in types are registered in the compiler; runtime types such as `\Tyhp\Type` and `Promise<T>` come from the `tyhp/core` and `tyhp/async` packages.

## Generic PHP Types

Tyhp adds generic type parameters to many built-in PHP types. The non-generic versions remain available for backward compatibility, but the generic versions are preferred for maximum type safety.

## The `array<TKey, TValue>` and `iterable<TKey, TValue>` Types

The `array` and `iterable` types from PHP now have additional generic signatures that allow for precise element type control.

:::member[array<TKey extends string|int, TValue>]
A typed array with 2 generic parameters. The first is the key type (must be `string`, `int`, or `string|int`), and the second is the value type.
:::

:::member[array<TValue>]
A typed array with 1 generic parameter (the value type). This is list shorthand for `array<int|string, TValue>` — keys are `int|string`, not int-only.
:::

:::member[array]
The non-generic version, equivalent to `array<int|string, mixed>`.
:::

:::member[iterable<TKey, TValue>]
A typed iterable with 2 generic parameters. `iterable` is a built-in type equivalent to `array|Traversable` (with matching generic arguments when given). It is not a type alias.
:::

:::member[iterable<TValue>]
Single-parameter version. Like `array<TValue>`, this is shorthand for `iterable<int|string, TValue>`.
:::

:::member[iterable]
The non-generic version, equivalent to `iterable<mixed, mixed>`.
:::

```tyhp
<?tyhp

array<string> $names = ["Alice", "Bob"];
array<string, int> $ages = ["Alice" => 30, "Bob" => 25];

function processItems(iterable<string> $items): void {
    foreach ($items as $item) {
        echo $item;
    }
}
```

## The `\Traversable<TKey, TValue>` Type

The `\Traversable` interface from PHP gains optional generic parameters for the key and value types.

:::member[\Traversable<TKey, TValue>]
A typed traversable with 2 generic parameters. The `iterable<TKey, TValue>` built-in is equivalent to `array<TKey, TValue>|\Traversable<TKey, TValue>`.
:::

## The `\Iterator<TKey, TValue>` Type

The `\Iterator` interface extends `\Traversable` and gains generic parameters. `current()` returns `TValue` and `key()` returns `TKey` instead of `mixed`.

:::member[\Iterator<TKey, TValue>]
A generic iterator where `current()` returns `TValue` and `key()` returns `TKey`. Extends `\Traversable<TKey, TValue>`.
:::

## The `\IteratorAggregate<TKey, TValue>` Type

The `\IteratorAggregate` interface extends `\Traversable` and gains generic parameters. The `getIterator()` method returns `\Traversable<TKey, TValue>`.

:::member[\IteratorAggregate<TKey, TValue>]
A generic iterable aggregate where `getIterator()` returns `\Traversable<TKey, TValue>`. Extends `\Traversable<TKey, TValue>`.
:::

## The `\ArrayAccess<TKey, TValue>` Type

The `\ArrayAccess` interface gains generic parameters for type-safe array-like access.

:::member[\ArrayAccess<TKey, TValue>]
Provides type-safe array-like access. `offsetGet(TKey $offset): TValue`, `offsetSet(TKey $offset, TValue $value): void`, `offsetExists(TKey $offset): bool`, `offsetUnset(TKey $offset): void`.
:::

## The `\Closure<TArgs..., TReturn>` Type

The `\Closure` class gains generic parameters for its argument types and return type. It follows the same return-last convention as `callable` -- the last generic parameter is the return type, and all preceding parameters are the argument types. Optional trailing parameters expand to an intersection of arity facets the same way as `callable` (see below).

:::member[\Closure<TArgs..., TReturn>]
A typed closure where the compiler knows the parameter types and return type. Follows the return-last generic convention. Defaults yield arity-sibling intersections.
:::

## The `\Generator<TKey, TValue, TSend, TReturn>` Type

The `\Generator` class gains four generic parameters for full type safety in generator functions.

:::member[\Generator<TKey, TValue, TSend, TReturn>]
`TKey` is the yield key type, `TValue` is the yield value type, `TSend` is the type accepted by `send()`, and `TReturn` is the type returned by `getReturn()`.
:::

## The `\Fiber<TStart, TResume, TSuspend, TReturn>` Type

The `\Fiber` class gains four generic parameters for the start value, resume value, suspend value, and return value.

:::member[\Fiber<TStart, TResume, TSuspend, TReturn>]
`TStart` is the type of the initial value passed to `start()`, `TResume` is the type accepted by `resume()`, `TSuspend` is the type returned by `suspend()`, and `TReturn` is the final return type from `getReturn()`.
:::

## The `\WeakReference<T>` Type

The `\WeakReference` class gains a generic parameter for the referenced object type. The `get()` method returns `?T` instead of `?object`.

:::member[\WeakReference<T>]
A weak reference to an object of type `T`. `get()` returns `?T`.
:::

## The `\WeakMap<TKey extends object, TValue>` Type

The `\WeakMap` class gains generic parameters for the key type (constrained to `object`) and value type.

:::member[\WeakMap<TKey extends object, TValue>]
A type-safe weak object-keyed map. Keys are weakly referenced and do not prevent garbage collection.
:::

## The `\UnitEnum` and `\BackedEnum<TValue>` Types

The `\UnitEnum` interface remains as-is. `\BackedEnum` gains a generic parameter `TValue extends string|int` for the backing value type. The `from()` and `tryFrom()` methods use `TValue` for their parameter and return types.

:::member[\BackedEnum<TValue extends string|int>]
A backed enum where `from(TValue): static` and `tryFrom(TValue): ?static` use the backing type parameter.
:::

## The `\SensitiveParameterValue<T>` Type

The `\SensitiveParameterValue` class gains a generic parameter `T` so `getValue()` returns `T` instead of `mixed`.

:::member[\SensitiveParameterValue<T>]
Wraps a sensitive parameter value. `getValue()` returns `T`.
:::

## Generic SPL Types

Many SPL (Standard PHP Library) classes and interfaces gain generic type parameters in Tyhp. The following is a comprehensive list of SPL types with generic support:

- `\OuterIterator<TKey, TValue>` -- extends `\Iterator<TKey, TValue>`
- `\RecursiveIterator<TKey, TValue>` -- extends `\Iterator<TKey, TValue>`
- `\SeekableIterator<TKey, TValue>` -- extends `\Iterator<TKey, TValue>`
- `\SplObserver<TSubject>` -- typed observer pattern
- `\SplSubject<TObserver>` -- typed subject pattern
- `\SplDoublyLinkedList<TValue>` -- typed doubly linked list
- `\SplStack<TValue>` -- extends `\SplDoublyLinkedList<TValue>`
- `\SplQueue<TValue>` -- extends `\SplDoublyLinkedList<TValue>`
- `\SplHeap<TValue>` -- typed heap
- `\SplMaxHeap<TValue>` -- extends `\SplHeap<TValue>`
- `\SplMinHeap<TValue>` -- extends `\SplHeap<TValue>`
- `\SplPriorityQueue<TValue, TPriority>` -- typed priority queue
- `\SplFixedArray<TValue>` -- typed fixed-size array
- `\ArrayObject<TKey, TValue>` -- typed array object wrapper
- `\SplObjectStorage<TObject extends object, TInfo>` -- typed object storage
- `\IteratorIterator<TKey, TValue>` -- extends `\OuterIterator<TKey, TValue>`
- `\AppendIterator<TKey, TValue>` -- extends `\IteratorIterator<TKey, TValue>`
- `\ArrayIterator<TKey, TValue>` -- extends `\SeekableIterator<TKey, TValue>`
- `\CachingIterator<TKey, TValue>` -- extends `\IteratorIterator<TKey, TValue>`
- `\FilterIterator<TKey, TValue>` -- extends `\IteratorIterator<TKey, TValue>`
- `\CallbackFilterIterator<TKey, TValue>` -- extends `\FilterIterator<TKey, TValue>`
- `\InfiniteIterator<TKey, TValue>` -- extends `\IteratorIterator<TKey, TValue>`
- `\LimitIterator<TKey, TValue>` -- extends `\IteratorIterator<TKey, TValue>`
- `\NoRewindIterator<TKey, TValue>` -- extends `\IteratorIterator<TKey, TValue>`
- `\RegexIterator<TKey, TValue>` -- extends `\FilterIterator<TKey, TValue>`
- All `Recursive*` variants of the above iterators also gain the same generic parameters

## The `callable<TArgs..., TReturn>` Type

In Tyhp, the `callable` type can accept generic parameters that specify the argument types and return type. The convention is return-last: argument types come first, followed by the return type as the final generic parameter. This mirrors the natural reading order of a function signature.

```tyhp
<?tyhp

// A callable that takes a string and returns int
callable<string, int> $parser;

// A callable that takes two ints and returns bool
callable<int, int, bool> $comparator;

// A callable with no parameters that returns void
callable<void> $callback;

// Using generic callables in function signatures
function apply<T, U>(callable<T, U> $fn, T $value): U {
    return $fn($value);
}
```

:::note
The `void` and `never` types are restricted types -- they cannot be used as generic type arguments unless the generic parameter's constraint explicitly allows them. The `callable` type's return parameter uses `TReturn extends void|never|mixed`, which opts in to both restricted types, making `callable<void>` and `callable<never>` valid.
:::

### Optional parameters and arity facets

Trailing parameters with default values expand into an **intersection of arity siblings** (not a subtype chain). The same model applies to `\Closure<…>` and will be reused by `new<…>` constructable facets (planned; Story 27).

```tyhp
<?tyhp

// Inferred type: callable<string, int, void> & callable<string, void>
function greet(string $name, int $times = 1): void { ... }

callable<string, void> $oneArg = greet(...);           // OK
callable<string, int, void> $twoArg = greet(...);      // OK

// Explicit intersection is also allowed (prefer a type alias so each callable's
// generics close before `&`):
type Greeter = callable<string, int, void> & callable<string, void>;
Greeter $either;
```

Each facet is an independent arity: `callable<string, int, void>` does **not** imply `callable<string, void>` by itself — only the intersection (or a value inferred with defaults) is assignable to both.

A trailing variadic never produces infinite facets. `function joinAll(string ...$parts): void` is typed `callable<void> & callable<string, void>` — the prefix facets plus one that accepts a single variadic argument. Calls with more arguments than any facet are left unchecked rather than rejected.

## New Tyhp-Specific Types

## The `decimal` Type

Tyhp introduces a `decimal` type for precise arithmetic calculations. In the compiled PHP, a `decimal` value is an instance of the `\Tyhp\Decimal` wrapper class that handles all arithmetic operations using bcmath, gmp, or a pure-PHP fallback (configurable in `tyhp.json`). There is no `19.99d` suffix. Construct decimals with `\Tyhp\decimal('19.99')` or `new \Tyhp\Decimal(...)`.

```tyhp
<?tyhp

decimal $price = \Tyhp\decimal('19.99');
decimal $tax = \Tyhp\decimal('2.00');
decimal $total = $price + $tax;  // Precise arithmetic

class Invoice {
    public decimal $amount;
    public decimal $taxRate;

    public function calculateTotal(): decimal {
        return $this->amount * (\Tyhp\decimal('1') + $this->taxRate);
    }
}
```

The `decimal` type supports all standard arithmetic operators (`+`, `-`, `*`, `/`, `%`, `**`), comparison operators (`==`, `<`, `>`, `<=`, `>=`, `<=>`), unary negation (`-$val`), and casts to `int`, `float`, and `string`. These compile to static method calls on `\Tyhp\Decimal` (for example `\Tyhp\Decimal::__add($a, $b)`).

## The `(decimal)` Cast

Tyhp provides a `(decimal)` cast operator for converting values to the `decimal` type, similar to PHP's built-in casts like `(int)` and `(float)`.

```tyhp
<?tyhp

float $price = 19.99;
decimal $precisePrice = (decimal) $price;

string $amount = "99.95";
decimal $parsed = (decimal) $amount;
```

## Compiled PHP Output for `decimal`

The `decimal` type compiles to `\Tyhp\Decimal` instances. Arithmetic operations become static method calls:

```tyhp
<?tyhp

decimal $a = \Tyhp\decimal('10.5');
decimal $b = \Tyhp\decimal('3.2');
decimal $result = $a + $b;
```

Compiles to:

```php
<?php
declare(strict_types=1);

$a = \Tyhp\decimal('10.5');
$b = \Tyhp\decimal('3.2');
$result = \Tyhp\Decimal::__add($a, $b);
```

## The `struct` Base Type

The `struct` base type is the parent of all struct declarations. At the PHP level it compiles to an associative `array`, but Tyhp gives it typed properties, schema-based typing, and value-type semantics. See the Structs documentation page for full details on struct declarations, anonymous structs, and the `with` keyword.

## The `void` Type

The `void` type in Tyhp is treated as a first-class keyword with its own lexer token (`T_TYHP_VOID`). It can be used in more type expression contexts than in PHP, including in type alias definitions, generic type arguments (where the generic parameter opts in via its constraint), and as part of callable/closure return types.

## The `never` Type

The `never` type from PHP is retained in Tyhp with the same semantics: it indicates that a function never returns (it always throws, calls `exit()`, or enters an infinite loop). Like `void`, it is a restricted type that can only appear as a generic type argument when the constraint explicitly allows it.

## The `mixed` Type

The `mixed` type from PHP is available in Tyhp but is discouraged. Tyhp's strong type system should eliminate most needs for `mixed`. There is no compiler setting that disallows `mixed`; prefer specific types or union types instead. See the dedicated Mixed Type documentation page for more details.

## Template String Types

Tyhp supports template (encaps) string types in type position: a double-quoted pattern with `${T}` holes and optional quantifiers right after `}` (`?` = 0–1, `+` = 1+, `*` = 0+). Examples: `"${string}*"`, `"api/${string}/items"`. They erase to `string`. See Type Aliases for a short usage note.

## The `self` / `static` / `parent` Relative Class Types

Tyhp supports relative class types with and without generic type arguments. The rules differ for
bare vs parameterized forms, and for instance vs static methods.

### Bare `self` / bare `static` (no type-argument list)

| Context | Bare `self` | Bare `static` |
|--------|-------------|---------------|
| Instance method | Declaring class with **this receiver’s** type args | Late-bound class of `$this`, with **that** instance’s type args (polymorphic “same as `$this`”) |
| Static method | Declaring class — under-specified args follow the bare-class-name rule | Class of the **call-site receiver** (`Child` in `Child::foo()` / `Child<string>::foo()`), with that reference’s type args |

Bare forms **inherit** receiver / call-site type arguments. They do **not** mean “fill class
defaults.” Defaults apply only when the class reference is under-specified the same way a bare
`Foo` / `Foo::` would elsewhere: if every omitted parameter has a default, apply them; otherwise
error. Inside the open generic’s own body, `$this` / bare `self` stay in terms of the class’s own
parameters (do not silently default to `mixed`).

### Parameterized `self<…>` / `parent<…>`

Allowed — explicit instantiation of a declaration whose arity is known at the spelling site:

```tyhp
<?tyhp

class Collection<T> {
    public function merge(self<T> $other): self<T> {
        // self<T> refers to Collection<T> with the written generic args
        // ...
    }
}

class TypedList<T> extends Collection<T> {
    public function concat(parent<T> $other): self<T> {
        // parent<T> refers to Collection<T>
        // ...
    }
}
```

### Parameterized `static<…>` — forbidden

`static<…>` is **not allowed** in any scope (including `final` classes). Late-static binding must
not invent or rebind type arguments. Prefer bare `static`, or explicit `DeclaringClass<…>` /
`self<…>` / `parent<…>` when an instantiation must be written. The checker reports
**TYHP4168** (`CheckerParameterizedStaticForbidden`).

### Factories that stamp a method generic onto the class

Use `: self<T>` or the declaring class name (`: Promise<T>`), **not** `: static<T>`:

```tyhp
<?tyhp

final class Promise<TReturn extends void|mixed = mixed> {
    public static function _async<T extends void|mixed>(callable<T> $fn): self<T> {
        return new self<T>($fn);
    }
}
```

On a `final` class, bare `self` and bare `static` remain interchangeable for *non-parameterized*
returns; parameterized returns use `self<…>` or the class name.

### Fluent inheritance

A non-generic parent may return bare `static` without knowing whether a child is generic. Call
sites with `GenericBuilder<int> $b` get `GenericBuilder<int>` back from inherited fluent methods —
parent declaration sites never name the child’s type parameters:

```tyhp
<?tyhp

class Builder {
    public function tap(): static {
        return $this;
    }
}

class GenericBuilder<T> extends Builder {
    public function __construct(public T $value): void {}
}

function demo(GenericBuilder<int> $b): GenericBuilder<int> {
    return $b->tap();
}
```

### `static` as a checked type (return / assignability)

Bare `static` is a first-class late-bound type. A value is valid where `static` is expected only
when it is **verifiably** that late-bound type, for example:

- `return $this;` (instance methods — `$this` is typed as `static`)
- the result of another call whose return type is (or resolves to) `static`
- a generic method/function / member whose substituted return type is `static`
- a value narrowed by `if ($var instanceof static) { … }` (or equivalent guards targeting `static`)

Ordinary `self` / declaring-class instances (including `new self()`) are **not** assignable to
`static` without such a proof. At call sites, a `: static` return expands to the receiver /
call-site class reference (including its type arguments).

## The `Promise<T>` Type

Tyhp provides a `Promise<T>` type for async/await support. The `Promise` class is defined as `Promise<TReturn extends void|mixed = mixed>` where `TReturn` is the fulfillment value type (default `mixed`). The constraint allows `Promise<void>` for async functions that do not return a value. A bare `Promise` is `Promise<mixed>`, not `Promise<void>`.

```tyhp
<?tyhp

// An async function returns a Promise
async function fetchUser(int $id): User {
    // The actual return type is Promise<User>
    $response = await \httpGet("/users/{$id}");
    return User::fromJson($response);
}

// Using the promise
Promise<User> $userPromise = fetchUser(42);
User $user = await $userPromise;
```

Key `Promise` methods include static combinators (`all<T>`, `race<T>`, `resolved<T>`, `rejected<T>`, `delay`, `timeout<T>`, `batch<TItem, TResult>`, `run<T>`), instance methods (`then<TResult>`, `catch<TResult>`, `finally`), and the internal `_async`/`_await` methods that the `async`/`await` keywords desugar to. See the Async/Await documentation for full details.

## Internal Compiler Types

The following types are part of Tyhp's internal type system. They are used by the compiler for type checking and type manipulation. Most are prefixed with `__` to indicate they are internal.

## The `__TyhpInternal<TType>` Type

The foundational internal wrapper type. A variable with a `__TyhpInternal<T>` type resolves to `T` but cannot be directly assigned by the developer. It can only be set via the return value of a function/method or via a type guard. Nearly all `__`-prefixed types are defined in terms of `__TyhpInternal<>`.

## Symbol Name Types

These types represent string values that the compiler knows refer to specific symbols in scope. They enable Tyhp's type-safe dynamic language features. Each is obtained by using the corresponding type guard function (e.g., `\class_exists()` narrows a string to `__ClassName`).

:::member[__VarName]
A `string` representing a variable name valid in the current scope. Obtained via `variable_exists($count)` or `variable_exists('count')` — the argument is the variable itself or a string literal. Alias for `__TypedVarName<mixed>`. (`$$var` is prohibited: TYHP4133.)
:::

:::member[__TypedVarName<T>]
Like `__VarName` but the compiler also knows the type of the referenced variable. If the string value can be resolved at compile time, the generic parameter `T` is the variable's declared type.
:::

:::member[__FunctionName]
A `string` representing a function name in scope. Obtained via `\function_exists()` as a type guard.
:::

:::member[__ClassName]
A `string` representing a class name in scope. Obtained via `\class_exists()` as a type guard.
Bare `__ClassName` is equivalent to `__ClassName<object>`; `\class_exists<T>($n)` narrows to `__ClassName<T>`.
Parametric `__ClassName<T>` is invariant in `T` (exact class name). For "name of `T` or a descendant", use `__CompatibleTypeName<T>`.
:::

:::member[__InterfaceName]
A `string` representing an interface name in scope. Obtained via `\interface_exists()` as a type guard.
Bare form ≡ `__InterfaceName<object>`; parametric form mirrors `__ClassName<T>`.
:::

:::member[__EnumName]
A `string` representing an enum name in scope. Obtained via `\enum_exists()` as a type guard. Alias of `__ClassName`.
Bare form ≡ `__EnumName<object>`.
:::

:::member[__TraitName]
A `string` representing a trait name in scope. Obtained via `\trait_exists()` as a type guard.
Bare form ≡ `__TraitName<object>`.
:::

:::member[__StructName]
A `string` representing the name of a struct type in scope.
:::

:::member[__UsedTraitName<T>]
A `__TraitName` that is specifically a trait used by the class or enum specified by `T`.
:::

:::member[__CompatibleTypeName<T>]
A class, enum, or interface name that is compatible with (same as or descendant of) `T`. Used with the `is` / `instanceof` keyword and with `\is_subclass_of()`.
Accepts string literals naming a subtype of `T`, and branded `__ClassName<S>` / `__EnumName<S>` / `__InterfaceName<S>` / `__CompatibleTypeName<S>` when `S` is the same as or a subtype of `T`.
:::

:::member[__PropertyName<T>]
A `string` representing a property name on the type `T`. Obtained via `\property_exists()` as a type guard.
:::

:::member[__MethodName<T>]
A `string` representing a method name on the type `T`. Obtained via `\method_exists()` as a type guard.
:::

:::member[__ConstName]
A `string` representing a constant name in scope.
:::

:::member[__ObjectConstName<T>]
A constant name scoped to a specific class or enum `T`.
:::

:::member[__EnumCaseName<T>]
The name of a specific enum case on the given enum `T`. Extends `__ObjectConstName`.
:::

## Type Name String Types

These types represent string representations of types themselves, used for dynamic type reflection and compile-time type manipulation.

:::member[__BaseTypeName]
A string literal union of all single type names: `'int'`, `'float'`, `'bool'`, `'array'`, `'string'`, `'null'`, `'mixed'`, `'self'`, `'parent'`, `'static'`, `'callable'`, `'iterable'`, `'object'`, plus `__StructName`, `__ClassName`, `__EnumName`, and `__InterfaceName`.
:::

:::member[__NullableBaseTypeName]
A nullable type name string, like `'?int'` or `'?MyClass'`. Defined as a `?`-prefixed `__BaseTypeName`.
:::

:::member[__UnionTypeName]
A full union type string like `'int|string|null'`. Built from `__BaseTypeName` segments joined by `|`.
:::

:::member[__IntersectTypeName]
A full intersection type string like `'MyClass&MyInterface'`. Built from base type segments joined by `&`.
:::

:::member[__NotNullableTypeName]
Any type name string (base, union, or intersection) that is guaranteed not to be nullable.
:::

:::member[__TypeName]
The universal type name string type. Can represent any type expressed as a string: base, nullable, union, intersection, or non-nullable variants.
:::

:::member[__NonMatchingStringType]
A special string type with a constant value that is un-matchable except to itself. Acts as a bottom/never-matching string type, used as a fallback in type computations.
:::

## Struct Utility Types

These types provide compile-time operations on struct types.

:::member[__StructRecord<TStructType, TKey>]
Represents a single record (key-value pair) within a struct type.
:::

:::member[__StructRecords<TStructType, TValueType>]
Represents the collection of all records in a struct as an array of `__StructRecord`.
:::

:::member[__StructDef<TRecordSet>]
Defines a struct type from its record set. Used for programmatic struct type construction.
:::

:::member[__StructPartial<TStructType, TIncludeKeys, TExcludeKeys>]
Represents a subset of a struct by including or excluding specific keys. If `TIncludeKeys` is null, `TExcludeKeys` is used for exclusion (and vice versa). Both null produces an empty struct.
:::

:::member[__StructKey<TStructType>]
The key type (property name type) of a struct's records.
:::

## Type Utility Types

These types provide compile-time type manipulation capabilities, similar to utility types in TypeScript.

:::member[__Properties<T>]
Returns the property names (for objects) or record keys (for structs) of a given type. Resolves to `__PropertyName<T> | __StructKey<T>`.
:::

:::member[__FunctionReturnType<T>]
Extracts the return type of a function given its name string.
:::

:::member[__MethodReturnType<T, M>]
Extracts the return type of a method given the owning type `T` and method name string `M`.
:::

:::member[__CallableReturnType<TCallable>]
Extracts the return type of a callable type `TCallable` — a `callable<…>` / `\Closure<…>` facet, a first-class function or method, or a type parameter inferred from a callback argument. Complements `__FunctionReturnType` / `__MethodReturnType`, which key off name strings rather than the callable type. Erases to that return type, or to `mixed` while `TCallable` is still unbound.
:::

:::member[__CallableParametersStruct<TCallable>]
Named-argument bag for a callable type `TCallable`. Resolves to a synthetic struct with one property per non-variadic named parameter (key `$name`, type = parameter type). Parameters with defaults are optional fields and may be omitted from a literal; required parameters must be present (TYHP4325). Facets without names (`callable<string, int>`) degrade to an empty struct. Variadic parameters are omitted (extra keys stay unknown-property errors). Erases to `array`. An unbound `TCallable` stays deferred until the callable is inferred at a call site.
:::

:::member[__CallableParametersTuple<TCallable>]
Positional-argument bag for a callable type `TCallable`. Resolves to a synthetic struct with int key aliases `0 as $_1`, `1 as $_2`, … matching the hand-written `CallableArgs*` convention. Unlike the named bag, parameter names are not required — a bare `callable<string, int>` facet still produces `$_1: string`. Defaulted trailing parameters are optional indexes and may be omitted from a list literal; required indexes must be present. Variadic parameters are omitted. Erases to `array`. An unbound `TCallable` stays deferred until the callable is inferred at a call site. List literals (`['Ada', 36]`) and explicit int keys (`[0 => 'Ada', 1 => 36]`) assign when types match.
:::

:::member[__CallableParametersRest<TCallable>]
Rest-unpack of a callable type `TCallable`'s parameter list (TypeScript `...args: Parameters<T>`). Used as a trailing variadic: `function invoke<TCallable extends callable>(TCallable $cb, __CallableParametersRest<TCallable> ...$args): __CallableReturnType<TCallable>`. After `TCallable` is inferred from `$cb`, each remaining positional argument is checked 1:1 against the callable's parameters (TYHP4010 / TYHP4142 / TYHP4143). Defaulted parameters may be omitted; a trailing variadic on the callable accepts extra arguments at that element type. Bare opaque `callable` stays gradual (unknown arity). Unions of same-arity callables merge parameter types; mismatched arities stay gradual. A trailing spread (`...$packed`) or a named pack into the rest parameter is not treated as an empty argument list. Positionals after a rest-region spread are not typed as the first inner parameter. The wrapper is kept at check time (it does not collapse to a Tuple struct). Erases to `mixed` so PHP does not demand each unpacked argument be an array.
:::

`\call_user_func` and `\call_user_func_array` in ExtStandard use these utilities:

```tyhp
function call_user_func<TCallable extends callable>(
    TCallable $callback,
    __CallableParametersRest<TCallable> ...$args
): __CallableReturnType<TCallable>;

function call_user_func_array<TCallable extends callable>(
    TCallable $callback,
    __CallableParametersStruct<TCallable> $args
): __CallableReturnType<TCallable>;

function call_user_func_array<TCallable extends callable>(
    TCallable $callback,
    __CallableParametersTuple<TCallable> $args
): __CallableReturnType<TCallable>;
```

`TCallable` is inferred from `$callback` (no `typeof` in type position). Named assoc arrays select the Struct overload; list / int-keyed arrays select the Tuple overload. Untyped `array` bags still use `\call_user_func_array_unsafe`. Hand-written `CallableArgs*` structs remain as examples; builtins no longer use the arity ladder. Peers (`forward_static_call*`, `register_shutdown_function`, `iterator_apply`, `\Closure::call`) follow the same pattern when retyped.

Locked decisions: utilities are keyed by the callable type, not a name string; optional parameters are modeled as optional struct fields (required-key assignability), not a power-set of subset bags; positional bags are first-class via Tuple (int keys `0..n-1`, `$_N` aliases).

:::member[__TypeDiff<T, U>]
Type subtraction: produces `T` with `U` removed. If nothing remains, resolves to `void`.
:::

:::member[__AsNotNullable<T>]
Strips `null` from a type. Returns `void` if the input was just `null`.
:::

:::member[__AsNullable<T>]
Makes any type nullable by adding `| null`.
:::

:::member[__AsReadOnly<T>]
Marks a type as readonly. The compiler errors if you try to modify a variable or property of this type.
:::

:::member[__AsTypeName<T>]
Converts a type to its string name representation. The inverse of `__AsType`.
:::

:::member[__AsType<T>]
Converts a type name string back to the actual type. The inverse of `__AsTypeName`.
:::

:::member[__AsNotNullableTypeName<T>]
Converts a type name string to its non-nullable version. Returns `__NonMatchingStringType` if the input was `'null'`.
:::

:::member[__AsNullableTypeName<T>]
Converts a type name string to its nullable version.
:::

## Compiled PHP Output for Generic Types

All generic type parameters are erased in the compiled PHP output. The base type is preserved, but the generic arguments are stripped:

```tyhp
<?tyhp

function getNames(array<string> $items): array<string> {
    return \array_filter($items, fn(string $s): bool => \strlen($s) > 0);
}

\WeakMap<object, string> $cache = new \WeakMap();
```

Compiles to:

```php
<?php
declare(strict_types=1);

function getNames(array $items): array {
    return \array_filter($items, fn(string $s): bool => \strlen($s) > 0);
}

$cache = new \WeakMap();
```

## Best Practices

:::tip
Use generic array types (`array<string>`, `array<string, int>`) instead of plain `array` for element-level type safety. `array<string>` is shorthand for `array<int|string, string>`. The compiler can then catch type mismatches when adding or retrieving elements.
:::

:::tip
Use `callable<TArgs..., TReturn>` to specify the signature of callable parameters. This gives you compile-time checking on both the arguments passed to the callable and its return type.
:::

:::tip
Prefer specific types over `mixed`. Use union types when a value can legitimately be one of several types, and reserve `mixed` for truly unknown types like deserialized data.
:::

:::tip
Use `decimal` for financial calculations and any domain where floating-point precision errors are unacceptable.
:::

## Common Mistakes

:::danger
Don't use plain `array` when you can specify `array<string>` or `array<string, int>`. The unparameterized form provides no element type safety.
:::

:::danger
Don't ignore generic type parameters on built-in types. Using `\Iterator` instead of `\Iterator<string, User>` loses type information for `current()` and `key()`.
:::

:::danger
Don't use `float` for financial calculations. Use `decimal` instead -- `float` is subject to IEEE 754 precision errors that can accumulate in arithmetic.
:::

:::danger
Don't try to use `void` or `never` as generic type arguments unless the generic parameter's constraint explicitly allows them (e.g., `T extends void|mixed`).
:::
