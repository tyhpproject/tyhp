---
title: Generics
status:
  tier: 0
  story: '08'
  state: complete
---

Tyhp adds generic type parameters to classes, interfaces, traits, enums, structs, functions, and methods. Generics provide compile-time type safety for reusable components without any runtime overhead — they are completely erased from PHP output. Tyhp generics support constraints via extends, default type parameters, and generic import aliases.

:::note
Generics are a compile-time-only feature. All generic type parameters, arguments, and constraints are erased from the PHP output. There is zero runtime cost.
:::

## Generic Classes

A class can declare one or more generic type parameters inside angle brackets after the class name. These type parameters can be used as types for properties, method parameters, and return types within the class.

```tyhp
<?tyhp

class Box<T> {
    private T $value;

    public function __construct(T $value): void {
        $this->value = $value;
    }

    public function getValue(): T {
        return $this->value;
    }
}

Box<int> $intBox = new Box<int>(42);
int $val = $intBox->getValue();  // Type-safe: returns int
```

## Compiled PHP Output

Generic type parameters are erased from the PHP output. Unconstrained type parameters become mixed. Constrained type parameters use their constraint type as the PHP type hint.

```php
<?php
// PHP output — generic type parameters are erased
declare(strict_types=1);

class Box {
    private mixed $value;

    public function __construct(mixed $value) {
        $this->value = $value;
    }

    public function getValue(): mixed {
        return $this->value;
    }
}

$intBox = new Box(42);
$val = $intBox->getValue();
```

## Generic Interfaces

Interfaces can declare generic type parameters. Classes that implement a generic interface must specify concrete type arguments, and the compiler validates that the implementing methods match the parameterized signatures.

```tyhp
<?tyhp

interface Repository<TEntity, TId> {
    public function find(TId $id): ?TEntity;
    public function save(TEntity $entity): void;
    public function delete(TId $id): void;
}

class UserRepository implements Repository<User, int> {
    public function find(int $id): ?User {
        // ...
    }

    public function save(User $entity): void {
        // ...
    }

    public function delete(int $id): void {
        // ...
    }
}
```

```php
<?php
// PHP output — generic arguments on implements are erased
declare(strict_types=1);

interface Repository {
    public function find(mixed $id): mixed;
    public function save(mixed $entity): void;
    public function delete(mixed $id): void;
}

class UserRepository implements Repository {
    public function find(int $id): ?User {
        // ...
    }

    public function save(User $entity): void {
        // ...
    }

    public function delete(int $id): void {
        // ...
    }
}
```

## Generic Traits

```tyhp
<?tyhp

trait Cacheable<T> {
    private ?T $cached = null;

    public function getCached(): ?T {
        return $this->cached;
    }

    public function setCached(T $value): void {
        $this->cached = $value;
    }
}

class UserService {
    use Cacheable<User>;
}
```

## Generic Enums

```tyhp
<?tyhp

enum Result<T> {
    case Ok;
    case Error;

    public function map<U>(callable<T, U> $fn): Result<U> {
        // ...
    }
}
```

## Generic Functions and Methods

Standalone functions and class methods can declare their own generic type parameters. When calling a generic function, type arguments can be provided explicitly or inferred from the arguments.

```tyhp
<?tyhp

// Generic standalone function
function firstOrNull<T>(array<T> $items): ?T {
    return $items[0] ?? null;
}

// Type argument inferred from the array type
?string $first = firstOrNull(['a', 'b', 'c']);  // T inferred as string

// Explicit type argument
?int $val = firstOrNull<int>([1, 2, 3]);

// Generic method on a class
class Transformer {
    public function transform<TIn, TOut>(TIn $input, callable<TIn, TOut> $fn): TOut {
        return $fn($input);
    }
}
```

```php
<?php
// PHP output — generic parameters and arguments are erased
declare(strict_types=1);

function firstOrNull(array $items): mixed {
    return $items[0] ?? null;
}

$first = firstOrNull(['a', 'b', 'c']);
$val = firstOrNull([1, 2, 3]);

class Transformer {
    public function transform(mixed $input, callable $fn): mixed {
        return $fn($input);
    }
}
```

## Constraints with extends

Type parameters can be constrained with the extends keyword. A constraint limits which types can be used as a type argument. The constraint can be a class, interface, or a union of types. When a type parameter has a constraint, the constraint type is used as the PHP type hint instead of mixed.

```tyhp
<?tyhp

// Single constraint
class SortedList<T extends Comparable> {
    public function add(T $item): void {
        // T is guaranteed to implement Comparable
    }
}

// Union constraint
function toNumber<T extends int|float|string>(T $value): float {
    return (float)$value;
}

// Multiple type parameters with constraints
class Mapper<TSource extends Entity, TTarget extends DTO> {
    public function map(TSource $source): TTarget {
        // ...
    }
}
```

```php
<?php
// PHP output — constrained generics use the constraint as the type hint
declare(strict_types=1);

class SortedList {
    public function add(Comparable $item): void {
        // ...
    }
}

function toNumber(int|float|string $value): float {
    return (float)$value;
}
```

## Generic Type Parameter Defaults

Generic type parameters can have default types, specified with = after the optional constraint. When a consumer does not provide a type argument for a defaulted parameter, the default is used. This enables cleaner APIs where the common case requires no explicit parameterization. Defaults are supported on classes, interfaces, traits, enums, type aliases, functions, and methods.

```tyhp
<?tyhp

// Promise defaults TReturn to mixed (runtime: tyhp/async)
class Promise<TReturn extends void|mixed = mixed> {
    // ...
}

Promise $p1 = new Promise();            // TReturn = mixed
Promise<void> $p2 = new Promise<void>();

// Override the default
Promise<int> $p3 = new Promise<int>();

// Multiple defaults — trailing parameters only
class MyMap<TKey = string, TValue = mixed> {
    // ...
}

MyMap $m1 = new MyMap();               // TKey = string, TValue = mixed
MyMap<int> $m2 = new MyMap<int>();      // TKey = int, TValue = mixed
MyMap<int, User> $m3 = new MyMap<int, User>();  // TKey = int, TValue = User

// Type alias with default
type Collection<T = mixed> = array<T>;

// Function with default
function wrap<T = string>(T $value): array<T> {
    return [$value];
}

// Interface with default
interface Repository<TEntity, TId = int> {
    public function find(TId $id): ?TEntity;
}
```

## Default Must Satisfy Constraint

When a type parameter has both a constraint and a default, the default type must satisfy the constraint. The compiler reports an error if the default type is not assignable to the constraint.

```tyhp
<?tyhp

// Valid — array implements Countable
class Counter<T extends Countable = array> {
    // ...
}

// ERROR: TYHP4310 — string does not implement Countable
// class BadCounter<T extends Countable = string> {
//     ...
// }
```

:::warning
Error TYHP4310 (CheckerGenericDefaultDoesNotSatisfyConstraint): "Default type 'string' does not satisfy constraint 'Countable' on generic parameter 'T'"
:::

## Trailing Rule for Defaults

Defaulted type parameters must be trailing — a non-defaulted parameter cannot follow a defaulted one. This mirrors PHP function parameter defaults and prevents ambiguity in partial type argument application.

```tyhp
<?tyhp

// Valid — non-defaulted parameters first, then defaulted
class Valid<T, U = int> {}
class AlsoValid<T, U = int, V = string> {}
class AllDefaults<T = mixed> {}

// ERROR: TYHP4311 — U has no default but follows T which has one
// class Invalid<T = int, U> {}
```

:::danger
Error TYHP4311 (CheckerGenericNonDefaultAfterDefault): "Generic parameter 'U' without a default cannot follow parameter 'T' which has a default"
:::

## Type Inference Priority Over Defaults

When calling a generic function or method, type inference from arguments always takes priority over defaults. Defaults are only used when a type parameter cannot be inferred and is not explicitly provided. The resolution order is: explicit type arguments first, then type inference from arguments, then defaults as a last resort.

```tyhp
<?tyhp

function wrap<T = string>(T $value): array<T> {
    return [$value];
}

wrap(42);         // T = int (inferred from argument, NOT string)
wrap('hello');    // T = string (inferred, coincidentally matches default)
wrap<bool>(true); // T = bool (explicit overrides everything)
```

:::note
Defaults primarily benefit class-level generics where there are no constructor arguments to infer from, or when a type parameter is not used in any parameter position.
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

Pair<int> $p = new Pair<int>();  // U = int (from T's value)
```

## Generic Extends and Implements

Generic arguments on extends and implements clauses provide compile-time type checking but are erased in PHP output.

```tyhp
<?tyhp

class StringCollection extends Collection<string> {
    // Inherits Collection's methods typed to string
}

class UserRepo implements Repository<User, int> {
    // Must implement Repository methods with User and int
}
```

```php
<?php
// PHP output — generic arguments on extends/implements are erased
declare(strict_types=1);

class StringCollection extends Collection {
}

class UserRepo implements Repository {
}
```

## Generic Import Aliases

You can create a use import alias with bound generic arguments. This creates a typed shortcut for a specific generic instantiation.

```tyhp
<?tyhp

use App\Collections\TypedList as IntList<int>;

IntList $numbers = new IntList();
$numbers->add(42);
```

## Constraint Violation Errors

The compiler reports an error when a type argument does not satisfy the constraint specified on the generic parameter.

```tyhp
<?tyhp

class SortedList<T extends Comparable> {
    public function add(T $item): void { }
}

// ERROR — int does not implement Comparable
// SortedList<int> $list = new SortedList<int>();

// OK — User implements Comparable
SortedList<User> $list = new SortedList<User>();
```

## Missing Type Argument Errors

When a generic type has required (non-defaulted) type parameters, omitting them produces an error.

```tyhp
<?tyhp

class Pair<T, U> {
    public T $first;
    public U $second;
}

// ERROR — Pair requires 2 type arguments, 0 provided
// Pair $p = new Pair();

// ERROR — Pair requires 2 type arguments, 1 provided
// Pair<int> $p = new Pair<int>();

// OK — all required type arguments provided
Pair<int, string> $p = new Pair<int, string>();
```

## Best Practices

:::tip
Use generics to write reusable, type-safe classes, interfaces, and functions. Generics preserve type information through calls, unlike mixed.
:::

:::tip
Use constraints (extends) to limit type parameters to types that support the operations you need. This also produces better PHP type hints in the compiled output.
:::

:::tip
Use defaults on type parameters when there is a sensible common case (e.g., `Promise<TReturn extends void|mixed = mixed>`). This reduces boilerplate for callers.
:::

:::tip
Let the compiler infer type arguments when calling generic functions — explicit type arguments are often unnecessary. The compiler infers types from the arguments you pass.
:::

:::tip
Use meaningful generic parameter names: T for a single general type, TKey/TValue for key-value pairs, TEntity/TId for domain-specific parameters.
:::

## Common Mistakes

:::danger
Don't rely on generics at runtime — they are erased from PHP output and exist only at compile time. There is no runtime type checking of generic parameters.
:::

:::danger
Don't place a non-defaulted type parameter after a defaulted one — this produces error TYHP4311.
:::

:::danger
Don't specify a default type that does not satisfy the constraint — this produces error TYHP4310.
:::

:::danger
Don't over-use generics for types that are naturally a single concrete type — generics add complexity. Use them when genuine type parameterization is needed.
:::

:::danger
Don't create self-referencing defaults (e.g., <T = T>) — this is circular-reference error TYHP4312.
:::
