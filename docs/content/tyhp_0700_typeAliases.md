---
title: 'Type Aliases'
status:
  tier: 1
  story: '11'
  state: complete
---

Type aliases in Tyhp let you create named shortcuts for complex or frequently used type expressions. They are completely erased during compilation — no PHP code is generated for a type alias declaration, and all usages are replaced with the underlying type in the PHP output. Type aliases can be declared at the root level (within a namespace) or inside a class body with visibility modifiers.

:::note
Type aliases have zero runtime cost. They exist only at compile time for type checking and are fully erased from PHP output.
:::

## Root-Level Type Aliases

A root-level type alias is declared with the type keyword followed by a name, an equals sign, and a type expression. It can appear at the namespace level alongside class and function declarations.

```tyhp
<?tyhp

namespace App\Types;

// Simple type alias
type UserId = int;

// Union type alias
type StringOrNull = string|null;

// Complex type alias
type Callback = callable(string, int): bool;

// Using type aliases
function findUser(UserId $id): ?User {
    // ...
}

function filter(array $items, Callback $predicate): array {
    return \array_filter($items, $predicate);
}
```

```php
<?php
// PHP output — type alias declarations are erased,
// usages are replaced with the underlying type
declare(strict_types=1);

namespace App\Types;

function findUser(int $id): ?User {
    // ...
}

function filter(array $items, callable $predicate): array {
    return \array_filter($items, $predicate);
}
```

## Generic Type Aliases

Type aliases can have generic type parameters, making them reusable templates for type expressions. Generic type alias parameters support the same syntax as class generics — including constraints and defaults.

```tyhp
<?tyhp

// Generic type alias
type Collection<T> = array<T>;

// Generic alias with constraint
type EntityList<T extends Entity> = array<T>;

// Generic alias with default
type Optional<T = mixed> = T|null;

// Multi-parameter generic alias
type Map<TKey, TValue> = array<TKey, TValue>;

// Using generic type aliases
Collection<string> $names = ['Alice', 'Bob'];
Optional<int> $age = null;
Map<string, User> $users = [];
```

```php
<?php
// PHP output — generic type aliases are fully erased
declare(strict_types=1);

$names = ['Alice', 'Bob'];
$age = null;
$users = [];
```

## Class-Level Type Aliases

Type aliases can be declared inside a class body. Class-level type aliases support visibility modifiers (public, protected, private), controlling whether the alias is accessible outside the class. They are useful for defining types that are conceptually owned by a class.

```tyhp
<?tyhp

class UserService {
    // Public — accessible from outside the class
    public type UserIdType = int;

    // Protected — accessible from subclasses
    protected type UserData = array{name: string, email: string};

    // Private — only accessible within this class
    private type InternalState = array{cache: array, dirty: bool};

    private InternalState $state;

    public function findUser(UserIdType $id): ?User {
        // ...
    }
}

// Accessing a public class-level type alias from outside
UserService\UserIdType $id = 42;
```

```php
<?php
// PHP output — class-level type aliases are erased,
// all usages replaced with underlying types
declare(strict_types=1);

class UserService {
    private array $state;

    public function findUser(int $id): ?User {
        // ...
    }
}

$id = 42;
```

## Self, Static, and Parent Scoping

Class-level type aliases can be referenced using self, static, and parent, just like class constants and methods.

```tyhp
<?tyhp

class Base {
    public type IdType = int;

    public function getId(): self\IdType {
        return 1;
    }
}

class Child extends Base {
    public type IdType = string;

    public function getParentIdType(): parent\IdType {
        // parent\IdType is int (from Base)
        return 1;
    }

    public function getOwnIdType(): self\IdType {
        // self\IdType is string (from Child)
        return 'abc';
    }
}
```

```php
<?php
// PHP output — type aliases resolved and erased
declare(strict_types=1);

class Base {
    public function getId(): int {
        return 1;
    }
}

class Child extends Base {
    public function getParentIdType(): int {
        return 1;
    }

    public function getOwnIdType(): string {
        return 'abc';
    }
}
```

## Type Alias Expansion

The compiler resolves type aliases by recursively expanding them to their underlying types. Nested aliases (an alias that references another alias) are fully expanded. Circular aliases — where alias A references alias B which references alias A — are detected and produce a compile error.

```tyhp
<?tyhp

type StringOrNull = string|null;
type Name = StringOrNull;  // Expands to string|null

// Circular alias — compile error
// type A = B;
// type B = A;  // Error: circular type alias
```

## Traits and Interfaces Cannot Have Type Aliases

Type aliases are only allowed in class and enum declarations. Traits and interfaces cannot define type aliases because they represent contracts and mixins, not concrete type definitions.

```tyhp
<?tyhp

// ERROR — traits cannot have type aliases
// trait MyTrait {
//     type MyType = int;  // Compile error
// }

// ERROR — interfaces cannot have type aliases
// interface MyInterface {
//     type MyType = int;  // Compile error
// }

// OK — classes can have type aliases
class MyClass {
    public type MyType = int;
}

// OK — enums can have type aliases
enum Status {
    case Active;
    case Inactive;
    public type StatusOrNull = self|null;
}
```

## Error: Alias Referencing Undefined Type

The compiler reports an error if a type alias references a type that does not exist.

```tyhp
<?tyhp

// ERROR — UndefinedClass does not exist
// type MyAlias = UndefinedClass;  // Compile error: symbol not found
```

## Best Practices

:::tip
Use type aliases to give meaningful names to complex type expressions — this improves code readability. For example, type Callback = callable(Request): Response is clearer than repeating the callable signature everywhere.
:::

:::tip
Use generic type aliases to create reusable type templates. For example, type Optional<T> = T|null provides a concise nullable wrapper.
:::

:::tip
Use class-level type aliases with visibility modifiers to scope types to their owning class. This enables type abstraction — the internal type can change without affecting external consumers.
:::

:::tip
Use type aliases for domain-specific types (e.g., type UserId = int) to make code self-documenting. This communicates intent even though the underlying type is simple.
:::

## Common Mistakes

:::danger
Don't define type aliases in traits or interfaces — they are not allowed and produce a compile error.
:::

:::danger
Don't create circular type aliases — the compiler detects and reports these as errors.
:::

:::danger
Don't rely on type aliases existing at runtime — they are completely erased from PHP output. You cannot reference a type alias name in PHP interop code.
:::

:::danger
Don't use type aliases when a simple union type inline would be clearer — aliases add indirection that may hurt readability for trivial cases.
:::
