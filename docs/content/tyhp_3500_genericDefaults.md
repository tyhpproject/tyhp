---
title: 'Generic Type Parameter Defaults'
status:
  tier: 3
  story: '28'
  state: complete
---

Generic type parameter defaults shipped with this alpha (ahead of the original Tier 3 slot). Trailing type parameters may specify `= Type`; omitted arguments use those defaults.

:::note
Generic type parameter defaults are a compile-time-only feature. All generics — including defaults — are erased from PHP output. There is zero runtime cost.
:::

## Basic Syntax

The default type is specified with = after the type parameter name and optional constraint. When using a generic type, omitted trailing arguments use their defaults.

```tyhp
<?tyhp

// T defaults to mixed when no type argument is provided
class Collection<T = mixed> {
    private array<T> $items = [];

    public function add(T $item): void {
        $this->items[] = $item;
    }

    public function getAll(): array<T> {
        return $this->items;
    }
}

// These are equivalent — T defaults to mixed
Collection $c1 = new Collection();
Collection<mixed> $c2 = new Collection<mixed>();

// Override the default
Collection<string> $names = new Collection<string>();
Collection<int> $ids = new Collection<int>();
```

## Compiled PHP Output

Generic defaults are erased along with all other generic information. The PHP output has no concept of type parameters or their defaults — they exist only during Tyhp compilation for type checking.

```tyhp
<?tyhp

class Collection<T = mixed> {
    private array<T> $items = [];

    public function add(T $item): void {
        $this->items[] = $item;
    }
}

Collection $c = new Collection();
Collection<string> $names = new Collection<string>();
```

```php
<?php
declare(strict_types=1);

class Collection {
    private array $items = [];

    public function add(mixed $item): void {
        $this->items[] = $item;
    }
}

$c = new Collection();
$names = new Collection();
```

## Defaults with Constraints

A type parameter can have both a constraint and a default. The syntax is T extends Constraint = Default. The default type must satisfy the constraint — if it does not, the compiler reports an error.

```tyhp
<?tyhp

// Promise defaults TReturn to mixed (runtime tyhp/async)
// The constraint allows void or mixed (both are valid return types)
class Promise<TReturn extends void|mixed = mixed> {
    private ?TReturn $result = null;

    public function resolve(TReturn $value): void {
        $this->result = $value;
    }
}

Promise $p1 = new Promise();           // TReturn = mixed (from default)
Promise<void> $p2 = new Promise<void>(); // Explicit override

// Override for async functions that return values
Promise<int> $p3 = new Promise<int>();   // TReturn = int
Promise<string> $p4 = new Promise<string>();

// Valid — array implements Countable
class Counter<T extends Countable = array> {
    public function count(T $items): int {
        return \count($items);
    }
}
```

## Multi-Parameter Defaults

When a generic type has multiple parameters, type arguments are applied left-to-right. Omitted trailing arguments use their defaults. This enables partial type argument application.

```tyhp
<?tyhp

class MyMap<TKey = string, TValue = mixed> {
    private array<TKey, TValue> $data = [];

    public function set(TKey $key, TValue $value): void {
        $this->data[$key] = $value;
    }

    public function get(TKey $key): ?TValue {
        return $this->data[$key] ?? null;
    }
}

// All defaults: TKey = string, TValue = mixed
MyMap $m1 = new MyMap();

// Override first, default second: TKey = int, TValue = mixed
MyMap<int> $m2 = new MyMap<int>();

// Override both: TKey = int, TValue = User
MyMap<int, User> $m3 = new MyMap<int, User>();
```

## Trailing-Only Rule

Defaulted type parameters must be trailing — a non-defaulted parameter cannot follow a defaulted one. This mirrors PHP function parameter defaults and prevents ambiguity in partial type argument application.

```tyhp
<?tyhp

// Valid — non-defaulted first, defaulted last
class Valid1<T, U = int> {}
class Valid2<T, U = int, V = string> {}
class Valid3<T = mixed> {}
class Valid4<T = int, U = string, V = bool> {}

// ERROR TYHP4311: Generic parameter 'U' without a default
// cannot follow parameter 'T' which has a default
// class Invalid<T = int, U> {}
```

:::danger
Error TYHP4311 (CheckerGenericNonDefaultAfterDefault): "Generic parameter 'U' without a default cannot follow parameter 'T' which has a default"
:::

## Default Must Satisfy Constraint

When a type parameter has both a constraint and a default, the default type must satisfy the constraint. The compiler verifies this at the declaration site — if the default does not implement or extend the constraint, it is a compile-time error.

```tyhp
<?tyhp

// Valid — array implements Countable
class GoodCounter<T extends Countable = array> {
}

// Valid — JsonSerializable extends Serializable
class GoodSerializer<T extends Serializable = JsonSerializable> {
}

// ERROR TYHP4310: Default type 'string' does not satisfy
// constraint 'Countable' on generic parameter 'T'
// class BadCounter<T extends Countable = string> {}

// ERROR TYHP4310: Default type 'int' does not satisfy
// constraint 'Stringable' on generic parameter 'T'
// class BadStringable<T extends Stringable = int> {}
```

:::warning
Error TYHP4310 (CheckerGenericDefaultDoesNotSatisfyConstraint): "Default type '{0}' does not satisfy constraint '{1}' on generic parameter '{2}'"
:::

## Defaults Referencing Earlier Parameters

A default type can reference type parameters declared earlier in the same parameter list. This enables patterns where one parameter's default depends on another parameter's resolved type.

```tyhp
<?tyhp

// U defaults to T — Pair<int> is equivalent to Pair<int, int>
class Pair<T, U = T> {
    public T $first;
    public U $second;
}

Pair<int> $p1 = new Pair<int>();        // U = int (from T)
Pair<int, string> $p2 = new Pair<int, string>(); // U = string (explicit)

// TValue defaults to array<TKey> — index reversal map
class Index<TKey, TValue = array<TKey>> {
    public function add(TKey $key, TValue $value): void {
        // ...
    }
}

Index<string> $idx = new Index<string>();  // TValue = array<string>
```

:::note
When a default references an earlier parameter that also has a constraint, the checker validates that the resolved default satisfies the later parameter's constraint (if any). For example, <T extends Comparable, U extends Comparable = T> is valid because T is guaranteed to satisfy Comparable.
:::

## Defaults on Functions and Methods

Generic defaults are supported on functions and methods, not just classes. However, for functions and methods, type inference from arguments takes priority over defaults. Defaults are used only when a type parameter cannot be inferred from the call-site arguments and is not explicitly provided.

```tyhp
<?tyhp

// Function with default
function wrap<T = string>(T $value): array<T> {
    return [$value];
}

wrap(42);         // T = int (inferred from argument, NOT string)
wrap('hello');    // T = string (inferred, coincidentally matches default)
wrap<bool>(true); // T = bool (explicit overrides everything)

// Function with non-inferable parameter
function create<T = stdClass>(): T {
    return new T();
}

create();               // T = stdClass (default, nothing to infer from)
create<MyClass>();      // T = MyClass (explicit)
```

## Type Inference Priority

When calling a generic function or method, the resolution order for each type parameter is strictly defined. Type inference from arguments always takes priority over defaults. Defaults are a last resort for parameters that cannot be determined any other way.

1. Explicit type arguments — provided by the caller (e.g., foo<int>())
2. Type inference — inferred from argument types at the call site
3. Defaults — used for parameters that are neither explicitly provided nor inferable

```tyhp
<?tyhp

function transform<TIn = string, TOut = TIn>(
    TIn $input,
    callable<TIn, TOut> $fn
): TOut {
    return $fn($input);
}

// TIn = int (inferred from 42), TOut = string (inferred from callable return)
string $result = transform(42, fn(int $x): string => (string)$x);

// TIn = string (default), TOut = string (default = TIn)
// Only when nothing can be inferred
// string $r = transform<>(...);  // hypothetical no-arg call
```

## Defaults on Interfaces and Type Aliases

Generic defaults are supported on interfaces and type aliases. Classes implementing a generic interface with defaults can omit the defaulted type arguments.

```tyhp
<?tyhp

// Interface with default — TId defaults to int
interface Repository<TEntity, TId = int> {
    public function find(TId $id): ?TEntity;
    public function save(TEntity $entity): void;
    public function delete(TId $id): void;
}

// TId defaults to int — most entities use integer IDs
class UserRepository implements Repository<User> {
    public function find(int $id): ?User { /* ... */ }
    public function save(User $entity): void { /* ... */ }
    public function delete(int $id): void { /* ... */ }
}

// Override the default — use string UUID
class OrderRepository implements Repository<Order, string> {
    public function find(string $id): ?Order { /* ... */ }
    public function save(Order $entity): void { /* ... */ }
    public function delete(string $id): void { /* ... */ }
}

// Type alias with default
type Collection<T = mixed> = array<T>;

Collection $anything = [1, 'two', 3.0];  // T = mixed
Collection<int> $numbers = [1, 2, 3];     // T = int
```

```php
<?php
declare(strict_types=1);

// All generics — including defaults — are erased
interface Repository {
    public function find(mixed $id): mixed;
    public function save(mixed $entity): void;
    public function delete(mixed $id): void;
}

class UserRepository implements Repository {
    public function find(int $id): ?User { /* ... */ }
    public function save(User $entity): void { /* ... */ }
    public function delete(int $id): void { /* ... */ }
}

class OrderRepository implements Repository {
    public function find(string $id): ?Order { /* ... */ }
    public function save(Order $entity): void { /* ... */ }
    public function delete(string $id): void { /* ... */ }
}
```

## Edge Cases

Generic defaults support various type expressions including union types, nullable types, and generic types as default values.

```tyhp
<?tyhp

// Default is a union type
class Flexible<T = int|string> {
    public T $value;
}

// Default is a nullable type
class Optional<T = ?string> {
    public T $value;
}

// Default is a generic type
class Wrapper<T = array<int>> {
    public T $data;
}

// Default is void (useful for return types in async)
class AsyncTask<TReturn extends void|mixed = mixed> {
    public function getResult(): TReturn {
        // ...
    }
}
```

## Compiler Error Examples

The compiler catches two categories of errors with generic defaults: ordering violations (non-defaulted after defaulted) and constraint violations (default not satisfying constraint).

```tyhp
<?tyhp

// ERROR TYHP4311: non-defaulted U follows defaulted T
// class Bad1<T = int, U> {}

// ERROR TYHP4311: non-defaulted V follows defaulted U
// class Bad2<T, U = int, V> {}

// ERROR TYHP4310: string does not implement Countable
// class Bad3<T extends Countable = string> {}

// ERROR TYHP4310: int does not implement Stringable
// class Bad4<T extends Stringable = int> {}

// ERROR TYHP4312: circular reference — T cannot default to itself
// class Bad5<T = T> {}
```

## Tyhpdef Syntax

Hand-written tyhpdefs can declare the same generic defaults. Auto-generation (`generate_tyhpdef`) is not in this alpha.

```tyhp
<?tyhpdef

class Promise<TReturn extends void|mixed = mixed> {
    public static function _async<T extends void|mixed = mixed>(callable<T> $fn): Promise<T>;
    public static function _await<T>(Promise<T> $promise): T;
}

type Collection<T = mixed> = array<T>;
```

## Summary of Syntax Forms

- <T> — No constraint, no default. T is required.
- <T extends SomeType> — Constraint, no default. T is required and must extend SomeType.
- <T = DefaultType> — No constraint, with default. T is optional and defaults to DefaultType.
- <T extends SomeType = DefaultType> — Constraint and default. T is optional, defaults to DefaultType, and DefaultType must satisfy SomeType.

## Best Practices

:::tip
Use defaults for commonly-used type parameters where there is a clear "common case." For example, `Promise<TReturn extends void|mixed = mixed>` matches the runtime package so omitted `TReturn` is `mixed`.
:::

:::tip
Use inference-friendly function signatures where the type can be inferred from arguments. Defaults are a fallback — type inference produces more precise types when arguments are available.
:::

:::tip
Use defaults on interfaces to reduce boilerplate for implementors. Repository<TEntity, TId = int> lets most repositories skip specifying the ID type.
:::

:::tip
Use self-referencing defaults (T, U = T) for symmetric types like Pair<T, U = T> where both parameters are commonly the same type.
:::

:::tip
Keep defaulted parameters at the end of the parameter list. The trailing-only rule is enforced by the compiler, and it also makes partial application intuitive.
:::

## Common Mistakes

:::danger
Don't provide defaults that violate constraints — <T extends Countable = string> is a compile error because string does not implement Countable.
:::

:::danger
Don't put defaulted parameters before non-defaulted ones — <T = int, U> is a compile error. Defaulted parameters must be trailing.
:::

:::danger
Don't rely on defaults for function calls when arguments can provide inference. Type inference is always more precise than defaults: wrap(42) infers T = int even if the default is T = string.
:::

:::danger
Don't create self-referencing defaults (e.g., <T = T>) — this is a circular reference and produces TYHP4312.
:::

:::danger
Don't expect generic defaults to have any runtime impact — all generics, including defaults, are erased from the PHP output. They exist only for compile-time type safety.
:::

:::danger
Don't forget to verify that defaults referencing earlier parameters satisfy the later parameter's constraints. For example, <T, U extends Comparable = T> is only valid if T itself is known to extend Comparable.
:::
