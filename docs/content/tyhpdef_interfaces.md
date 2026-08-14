---
title: 'Interfaces in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhpdef lets you describe existing PHP interfaces to the Tyhp compiler. When importing an interface, you provide fully typed method signatures, constants, and property declarations. You only need to declare the members you intend to use in your Tyhp code — the compiler treats undeclared members as nonexistent.

## Basic Interface Declaration

Consider a PHP interface that you want to use from Tyhp:

```php
<?php

interface Logger {
    public function info($message, $context = []);
    public function error($message, $context = []);
    public function debug($message, $context = []);
}
```

The Tyhpdef declaration adds full type information to every method signature. All methods end with a semicolon — no curly-brace bodies:

```tyhp
<?tyhpdef

interface Logger {
    public function info(string $message, array<string, mixed> $context = []): void;
    public function error(string $message, array<string, mixed> $context = []): void;
    public function debug(string $message, array<string, mixed> $context = []): void;
}
```

## Interface Constants

Interfaces can declare typed constants. The constant type and name are specified, but the value itself is not included since Tyhpdef is declaration-only.

```tyhp
<?tyhpdef

interface Cacheable {
    public const int DEFAULT_TTL;
    public const string CACHE_PREFIX;

    public function getCacheKey(): string;
    public function getTtl(): int;
}
```

## Interface Inheritance

Interfaces can extend one or more other interfaces using the extends keyword, matching PHP's interface inheritance model.

```tyhp
<?tyhpdef

interface Readable {
    public function read(int $length): string;
}

interface Writable {
    public function write(string $data): int;
}

interface Stream extends Readable, Writable {
    public function close(): void;
    public function isOpen(): bool;
}
```

## Generic Interfaces

Interfaces support generic type parameters with optional constraints. This lets you describe PHP interfaces that work with different types while maintaining type safety in Tyhp.

```tyhp
<?tyhpdef

interface Repository<T> {
    public function find(int $id): ?T;
    public function findAll(): array<T>;
    public function save(T $entity): void;
    public function delete(T $entity): void;
}

// Generic interface with constraint
interface SortableCollection<T extends \Comparable> {
    public function add(T $item): void;
    public function sort(): array<T>;
    public function first(): ?T;
}
```

## Generic Interface Inheritance

Generic interfaces can extend other generic interfaces, passing through or constraining type parameters.

```tyhp
<?tyhpdef

interface CacheableRepository<T> extends Repository<T>, Cacheable {
    public function findCached(int $id): ?T;
    public function invalidate(int $id): void;
}

// Narrowing the generic type in a child interface
interface UserRepository extends Repository<\App\Models\User> {
    public function findByEmail(string $email): ?\App\Models\User;
}
```

## Method Overloads

Tyhpdef supports method overloads on interfaces. You declare multiple signatures for the same method name with different parameter types and return types. Tyhp uses these overloads to narrow return types based on how the method is called.

```tyhp
<?tyhpdef

struct PersonName {
    string $first;
    string $last;
}

interface Person {
    public function getFirstName(): ?string;
    public function getLastName(): ?string;

    // Overloaded method — return type depends on the argument
    public function getFullName(true $asStruct): PersonName;
    public function getFullName(false $asStruct): string;
    public function getFullName(bool $asStruct = false): string|PersonName;
}
```

## Async Methods

Interface methods can be marked as async. An async method returns a Promise of the declared return type.

```tyhp
<?tyhpdef

interface AsyncRepository<T> {
    async public function find(int $id): T;
    async public function findAll(): array<T>;
    async public function save(T $entity): void;
}
```

## Deprecated and Obsolete Interfaces

Interfaces and their individual members can be marked as `deprecated` or `obsolete`. The compiler emits a warning when deprecated items are used and an error for obsolete items.

```tyhp
<?tyhpdef

deprecated interface OldLogger {
    public function log(string $message): void;
}

interface ModernLogger {
    deprecated public function logLegacy(string $msg): void;
    public function log(string $level, string $message): void;
}
```

## Best Practices

:::tip
DO provide fully typed method signatures with specific types instead of mixed. The more precise your declarations, the better type checking Tyhp can provide.
:::

:::tip
DO use method overloads when a PHP interface method returns different types based on its arguments. This gives Tyhp the information it needs for accurate type narrowing.
:::

:::danger
DON'T include method bodies in interface declarations. All methods must end with a semicolon, not a curly-brace block.
:::

:::danger
DON'T declare private members. Only public and protected members are visible to Tyhp. Private members in a Tyhpdef interface declaration will cause a parse error.
:::
