---
title: 'Classes in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhpdef lets you describe existing PHP classes to the Tyhp compiler. When importing a class, you declare its properties, methods, constants, and relationships (extends, implements). You only need to declare the members you actually use in your Tyhp code — members already defined via an imported interface do not need to be re-declared.

## Basic Class Declaration

Consider a PHP class you want to use from Tyhp:

```php
<?php

class Customer implements Person {
    protected $data;
    public $heightInCentimeters = 23;

    public function __construct($data = []) {
        $this->data = $data;
    }

    // ... implements Person methods ...
}
```

The Tyhpdef declaration provides full type information. Methods are signatures only, ending with semicolons:

```tyhp
<?tyhpdef

struct CustomerData {
    ?string $first_name;
    ?string $last_name;
}

class Customer implements Person {
    public float $heightInCentimeters;
    protected CustomerData $data;

    public function __construct(CustomerData $data = []): void;

    // Members from the Person interface are inherited
    // and do not need to be re-declared here.
}
```

## Class Modifiers

Classes can be declared with `abstract` or `final` modifiers. These modifiers control how Tyhp sees the class at compile time. Make sure they match the actual PHP class — declaring a non-final PHP class as final in Tyhpdef will prevent valid subclassing in Tyhp.

```tyhp
<?tyhpdef

abstract class Entity {
    protected int $id;
    public function getId(): int;
    abstract public function getTableName(): string;
}

final class ImmutableConfig {
    public function get(string $key): mixed;
    public function has(string $key): bool;
}
```

## Properties

Properties are declared with visibility modifiers and types. Only public and protected properties should be declared — private properties are not accessible from Tyhp code. Properties do not include default values in Tyhpdef.

```tyhp
<?tyhpdef

class Product {
    public int $id;
    public string $name;
    public float $price;
    public ?string $description;
    protected array<string> $tags;
    public readonly string $sku;
}
```

## Methods

Method declarations include the full signature: visibility, optional modifiers (static, abstract, final), the function keyword, parameters with types, and return type. All methods end with a semicolon.

```tyhp
<?tyhpdef

class UserService {
    public function find(int $id): ?\App\Models\User;
    public function findByEmail(string $email): ?\App\Models\User;
    public static function create(string $name, string $email): \App\Models\User;
    abstract protected function validate(\App\Models\User $user): bool;
    final public function delete(int $id): void;
}
```

## Constructors

Constructors are declared using `__construct` and can specify a return type of `: void`. Constructor parameter promotion (public/protected/private on parameters) is supported.

```tyhp
<?tyhpdef

class DatabaseConnection {
    public function __construct(
        string $host,
        int $port = 3306,
        ?string $database = null
    ): void;

    public function query(string $sql): array<mixed>;
    public function close(): void;
}
```

## Constants

Class constants are declared with visibility, the const keyword, a type, and a name. Values are not included.

```tyhp
<?tyhpdef

class HttpStatus {
    public const int OK;
    public const int NOT_FOUND;
    public const int INTERNAL_ERROR;
    public const string DEFAULT_CONTENT_TYPE;
}
```

## Extends and Implements

Classes can extend a parent class and implement one or more interfaces, mirroring PHP's inheritance model.

```tyhp
<?tyhpdef

class AdminUser extends \App\Models\User implements \Stringable, \JsonSerializable {
    public function getRole(): string;
    public function __toString(): string;
    public function jsonSerialize(): mixed;
}
```

## Generic Classes

Classes support generic type parameters with optional constraints. Generics enable type-safe descriptions of PHP classes that work with different types.

```tyhp
<?tyhpdef

class Collection<T> {
    public function add(T $item): void;
    public function get(int $index): T;
    public function count(): int;
    public function toArray(): array<T>;
    public function filter(callable<T, bool> $predicate): Collection<T>;
    public function map<U>(callable<T, U> $transform): Collection<U>;
}

// Generic class with constraint and extends
class TypedRepository<T extends Entity> extends Repository<T> {
    public const int DEFAULT_PAGE_SIZE;
    public function paginate(int $page, int $size): array<T>;
    async public function saveAsync(T $entity): T;
}
```

## Trait Usage

Classes in Tyhpdef can declare trait usage. Trait conflict resolution and aliasing syntax works the same as in PHP.

```tyhp
<?tyhpdef

class AuditableUser {
    use TimestampedEntity, SoftDeletes {
        SoftDeletes::delete insteadof TimestampedEntity;
        TimestampedEntity::delete as hardDelete;
    }

    public function getAuditLog(): array<string>;
}
```

## Operator Overloads

Tyhpdef supports two operator forms on a class:

1. **Native passthrough** — bodyless `operator …;` (no `extension` keyword). The underlying PHP
   type already supports the operator (engine magic / PECL). Tyhp type-checks `$a + $b` but the
   emitter **does not** rewrite it to a method call.
2. **Mapped overload** — `extension operator … { … }` or `=> …` (**body required**). Maps Tyhp
   operator usage onto methods (e.g. `plus()` or a compiled `__add`). Call sites are rewritten.

```tyhp
<?tyhpdef

// Native: DateTime / PECL Decimal-style — leave PHP operators alone
class Instant {
    operator +(self $left, DateInterval $right): Instant;
    operator <=>(self $left, Instant $right): int;
}

// Mapped: rewrite $a + $b via a body (brace or =>)
class Money {
    public function plus(Money $other): Money;
    public function isEqualTo(Money $other): bool;

    extension operator +(self $left, self $right): self {
        return $left->plus($right);
    }
    extension operator ==(self $left, self $right): bool => $left->isEqualTo($right);
}
```

A bodyless `extension operator +(…): T;` is an error — use plain `operator` for native ops, or
give `extension operator` a body.

## Class Aliasing

When the PHP class name differs from what you want to use in Tyhp, you can alias it using the `as` keyword. The original fully-qualified PHP class name comes first, followed by `as` and the Tyhp alias.

```tyhp
<?tyhpdef

class \Vendor\LongNamespace\SomeVeryLongClassName as ShortName {
    public function doWork(): void;
}
```

## Deprecated and Obsolete

The class itself can be marked `deprecated` or `obsolete` (top-level). Member-level markers parse but are not enforced in this alpha.

```tyhp
<?tyhpdef

deprecated class LegacyAuth {
    public function login(string $user, string $pass): bool;
}

class Auth {
    public function loginWithPassword(string $pass): bool;
    public function loginWithToken(string $token): bool;
}
```

## Best Practices

:::tip
DO declare only public and protected members. Private members are not accessible from Tyhp code and should be omitted from your Tyhpdef declarations.
:::

:::tip
DO match the abstract/final modifiers to the actual PHP class. Mismatched modifiers cause compile-time or runtime errors.
:::

:::danger
DON'T include ordinary method bodies or property default values in class declarations — those end
with a semicolon. Exception: tyhpdef `extension function` / `extension operator` **require** bodies
(brace or `=>`) because they map onto PHP methods. Bodyless `operator …;` (no `extension`) is the
native-passthrough form.
:::

:::danger
DON'T re-declare methods that are already defined on an imported interface the class implements. The compiler inherits those signatures automatically.
:::
