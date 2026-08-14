---
title: 'The new<TArgs...> Constructable Object Type'
status:
  tier: 3
  story: '27'
  state: planned
---

:::warning Not in this alpha
This feature is **not included** in Tyhp 805.0.0-alpha.1 (roadmap Tier 2/3). The rest of this page describes the planned design. Do not expect these commands or syntax to work yet.
:::


Tyhp provides a built-in type called new that represents "an object that can be constructed." Used exclusively in type positions, new enables type-safe factory patterns, dependency injection containers, and generic constructor constraints. It works analogously to other built-in type facets like callable and iterable — new<TArgs...> specifies the constructor parameter types that a class must accept.

:::note
The new type is purely a compile-time construct. It is completely erased from PHP output — replaced with object in type positions and stripped from generic constraints. There is zero runtime cost.
:::

## Basic Syntax

The new type is used in type positions (parameter types, return types, property types, generic constraints). Bare new means "constructable with zero arguments." new<TArgs...> means "constructable with the specified argument types." Unlike callable<TArgs..., TReturn> which uses a return-last convention, new<TArgs...> has no return type parameter — the return type of a constructor is always the class itself.

```tyhp
<?tyhp

// Accept any class constructable with zero arguments
function createDefault(new $constructable): object {
    return new $constructable();
}

// Accept any class constructable with (string, int)
function createWithArgs(new<string, int> $constructable, string $name, int $id): object {
    return new $constructable($name, $id);
}

// As a return type
function getFactory(): new<string> {
    return SomeClass::class;
}
```

## Compiled PHP Output

The new type has no PHP runtime equivalent. In type positions, new is replaced with object. Generic arguments are stripped. Generic constraints containing new<> are erased entirely as part of generic erasure.

```php
<?php
declare(strict_types=1);

// function createDefault(new $constructable): object
// compiles to:
function createDefault(object $constructable): object {
    return new $constructable();
}

// function createWithArgs(new<string, int> $constructable, string $name, int $id): object
// compiles to:
function createWithArgs(object $constructable, string $name, int $id): object {
    return new $constructable($name, $id);
}

// function getFactory(): new<string>
// compiles to:
function getFactory(): object {
    return SomeClass::class;
}
```

## Generic Constraints with new<>

The most common use of new<> is as a generic constraint. This allows you to write generic factory functions that are type-safe: the compiler verifies that any concrete class passed as a type argument has a public constructor matching the specified signature.

```tyhp
<?tyhp

// T must be constructable with a string argument
function create<T extends new<string>>(string $value): T {
    return new T($value);
}

// T must be constructable with zero args AND implement Logger
function makeLogger<T extends new & Logger>(): T {
    return new T();
}

// Multiple constructor parameter types
function buildService<T extends new<Config, Database> & Service>(
    Config $config,
    Database $db
): T {
    return new T($config, $db);
}

// Usage:
class UserService extends Service {
    public function __construct(Config $config, Database $db) {
        // ...
    }
}

UserService $svc = buildService<UserService>($config, $db);
```

```php
<?php
declare(strict_types=1);

// Generic constraints are erased — T becomes mixed
function create(string $value): mixed {
    // At runtime, the class name is passed as a parameter or determined by context
    return new $value();
}

function makeLogger(): mixed {
    return new $class();
}

function buildService(Config $config, Database $db): mixed {
    return new $class($config, $db);
}
```

## Computed Intersection Types

Tyhp's type system automatically computes new<> type facets for every class based on its constructor signature. A class with N constructor parameters where the last M have defaults generates M+1 new<> facets — one for each valid prefix of arguments, down to the required minimum. These facets are included in the class's computed intersection type.

```tyhp
<?tyhp

// No constructor — constructable with zero args
class SimpleClass {
}
// Computed type includes: new

// One required param, two optional
class ConfiguredClass {
    public function __construct(
        string $name,
        int $priority = 0,
        bool $active = true
    ) {
        // ...
    }
}
// Computed type includes: new<string>, new<string, int>, new<string, int, bool>
// Does NOT include bare new (first param is required)

// All defaults — constructable with zero, one, or two args
class AllDefaultsClass {
    public function __construct(string $label = 'default', int $count = 0) {
        // ...
    }
}
// Computed type includes: new, new<string>, new<string, int>
```

## What Satisfies new<>

Only concrete classes with public constructors can satisfy new<> constraints. Abstract classes, interfaces, traits, enums, and classes with private or protected constructors do not satisfy new<>.

- Concrete class with no constructor: satisfies bare new (zero-arg constructable)
- Concrete class with public constructor: satisfies new<TArgs...> based on its parameter types
- Concrete class with all-default constructor: satisfies bare new and each prefix variant
- Variadic-only constructor (__construct(mixed ...$args)): satisfies bare new
- Abstract class: does NOT satisfy new<> — cannot be instantiated
- Interface: does NOT satisfy new<> — cannot be instantiated
- Trait: does NOT satisfy new<> — cannot be instantiated
- Enum: does NOT satisfy new<> — enums use cases, not constructors
- Private/protected constructor: does NOT satisfy new<> — not externally constructable
- Struct: does NOT satisfy new<> — structs use struct literal syntax, not new

## Type Hierarchy

All new variants extend object directly. They are sibling types, not a nested subtype chain — new<T1, T2> does NOT extend new<T1>. A concrete class may satisfy multiple new<> variants independently based on its constructor's default parameter values. Every new type is assignable to object and mixed.

```tyhp
<?tyhp

// new extends object
object $obj = getConstructable();  // Always valid

// Each new<> variant is independent
// A class with __construct(string $a, int $b = 0) satisfies:
//   new<string>        — required arg only
//   new<string, int>   — both args
// But it does NOT satisfy:
//   new                — $a has no default
//   new<int>           — wrong type for first arg
//   new<string, int, bool> — too many params
```

## Intersection Types with new<>

The new type can be combined with other types in intersection types. This is useful when you need a type that is both constructable and implements a specific interface.

```tyhp
<?tyhp

// Must be constructable with zero args AND implement Serializable
function createAndSerialize<T extends new & Serializable>(): string {
    T $instance = new T();
    return \serialize($instance);
}

// Must be constructable with Config AND implement Service
function bootService<T extends new<Config> & Service>(Config $config): T {
    T $service = new T($config);
    $service->boot();
    return $service;
}
```

In the compiled PHP output, the new<> component is removed from intersection types. If only one component remains, it is emitted directly. If new<> was the only component, it is replaced with object.

```php
<?php
declare(strict_types=1);

// new & Serializable in a non-generic context compiles to:
// Serializable (the new component is stripped)

// new<string> | null compiles to:
// object | null (new<> replaced with object in unions)
```

## Default Parameter Expansion

A constructor with default parameters generates multiple new<> facets. Each valid prefix of arguments (from the minimum required count up to the total parameter count) produces a separate facet. This means a class can satisfy more specific and less specific new<> constraints simultaneously.

```tyhp
<?tyhp

class Flexible {
    public function __construct(
        string $name,
        int $priority = 0,
        bool $active = true
    ) {
        // ...
    }
}

// All of these are valid because Flexible's defaults expand to
// new<string>, new<string, int>, and new<string, int, bool>
function f1<T extends new<string>>(): T { return new T('test'); }
function f2<T extends new<string, int>>(): T { return new T('test', 1); }
function f3<T extends new<string, int, bool>>(): T { return new T('test', 1, true); }

Flexible $a = f1<Flexible>();
Flexible $b = f2<Flexible>();
Flexible $c = f3<Flexible>();
```

## Inherited Constructors

If a class does not declare its own constructor but inherits one from a parent class, the parent's constructor signature is used for new<> facet computation. If the parent constructor is protected and the child does not override it with a public one, the child is not externally constructable.

```tyhp
<?tyhp

class Base {
    public function __construct(string $name) {
        // ...
    }
}

// Child inherits Base's constructor
class Child extends Base {
    // No own constructor — uses Base's __construct(string $name)
}

// Child satisfies new<string> because it inherits Base's constructor
function create<T extends new<string>>(string $name): T {
    return new T($name);
}

Child $child = create<Child>('test');
```

## Dependency Injection Example

The new<> type is particularly useful for dependency injection containers and service locators that need to construct objects generically while maintaining type safety.

```tyhp
<?tyhp

class Container {
    private array<string, object> $instances = [];

    public function singleton<T extends new>(string $className): T {
        if (!isset($this->instances[$className])) {
            $this->instances[$className] = new T();
        }
        return $this->instances[$className];
    }

    public function make<T extends new<Config>>(string $className, Config $config): T {
        return new T($config);
    }
}

class CacheService {
    public function __construct() { }
}

class DatabaseService {
    public function __construct(Config $config) { }
}

Container $container = new Container();
CacheService $cache = $container->singleton<CacheService>(CacheService::class);
DatabaseService $db = $container->make<DatabaseService>(DatabaseService::class, $config);
```

## Compiler Error Examples

The compiler reports specific errors when new<> constraints are violated. Different error codes distinguish between abstract types, missing constructors, and signature mismatches.

```tyhp
<?tyhp

abstract class AbstractBase {
    public function __construct(string $name) { }
}

interface Buildable {
}

class PrivateCtorClass {
    private function __construct() { }
}

class StringCtorClass {
    public function __construct(string $name) { }
}

function createNew<T extends new>(): T { return new T(); }
function createString<T extends new<string>>(string $v): T { return new T($v); }
function createInt<T extends new<int>>(int $v): T { return new T($v); }

// ERROR TYHP4060: Abstract type 'AbstractBase' cannot satisfy 'new' constraint
// createNew<AbstractBase>();

// ERROR TYHP4058: Type 'Buildable' does not satisfy 'new' constraint
// — it must be a concrete class with a public constructor
// createNew<Buildable>();

// ERROR TYHP4058: Type 'PrivateCtorClass' does not satisfy 'new' constraint
// — it must be a concrete class with a public constructor
// createNew<PrivateCtorClass>();

// ERROR TYHP4059: Type 'StringCtorClass' does not satisfy 'new<int>'
// — constructor signature does not match
// createInt<StringCtorClass>(42);
```

:::warning
Error TYHP4058 (CheckerTypeNotConstructable): "Type '{0}' does not satisfy 'new' constraint — it must be a concrete class with a public constructor"
:::

:::warning
Error TYHP4059 (CheckerConstructorSignatureMismatch): "Type '{0}' does not satisfy 'new<{1}>' — constructor signature does not match"
:::

:::warning
Error TYHP4060 (CheckerAbstractNotConstructable): "Abstract type '{0}' cannot satisfy 'new' constraint"
:::

## Interaction with callable<>

The new<> type and callable<> type are independent facets in a class's computed intersection type. A class that has both a public constructor and an __invoke method satisfies both. A constraint like T extends new<string> & callable<int, bool> requires both.

```tyhp
<?tyhp

class Validator {
    public function __construct(string $pattern) {
        // ...
    }

    public function __invoke(string $input): bool {
        // ...
    }
}

// Validator satisfies both new<string> AND callable<string, bool>
function createAndRun<T extends new<string> & callable<string, bool>>(
    string $pattern,
    string $input
): bool {
    T $validator = new T($pattern);
    return $validator($input);
}
```

## Tyhpdef Integration

The binder computes new<> facets for tyhpdef-declared classes (external PHP classes) just as it does for user-declared classes. This enables using new<> constraints with standard library and third-party classes.

```tyhp
<?tyhp

// DateTime has __construct(string $datetime = 'now', ?DateTimeZone $timezone = null)
// So DateTime satisfies: new, new<string>, new<string, ?DateTimeZone>

function createDated<T extends new<string>>(string $date): T {
    return new T($date);
}

DateTime $dt = createDated<DateTime>('2026-01-01');
```

## Best Practices

:::tip
Use new<> constraints for generic factory functions, builders, and dependency injection. They provide compile-time verification that a class can actually be constructed with the expected arguments.
:::

:::tip
Combine new<> with interface constraints (T extends new<Config> & Service) to require both constructability and interface compliance in a single generic parameter.
:::

:::tip
Use bare new (without generic arguments) when you only need zero-argument constructability. This is common for singleton and prototype patterns.
:::

:::tip
Prefer new<> constraints over runtime reflection for constructor validation. The compiler catches mismatches at compile time instead of at runtime.
:::

:::tip
Remember that default parameter expansion allows a class with __construct(string $a, int $b = 0) to satisfy both new<string> and new<string, int>. Design constraints to match the minimum required arguments.
:::

## Common Mistakes

:::danger
Don't use new<> on types you will not construct — it adds an unnecessary constraint. Only use new<> when the generic function or class actually creates instances.
:::

:::danger
Don't forget that new<> only constrains the constructor signature. It does not guarantee the class has specific methods or properties — use interface constraints for that.
:::

:::danger
Don't expect new<> to work with abstract classes, interfaces, or enums. These types cannot be directly instantiated and will produce a compiler error.
:::

:::danger
Don't confuse new<string, int> with callable<string, int>. The new type has no return type parameter (the constructor always returns the class itself), while callable uses a return-last convention.
:::

:::danger
Don't rely on new<> at runtime — it is completely erased from PHP output. The constraint exists only at compile time for type safety.
:::

:::danger
Don't assume new<T1, T2> is a subtype of new<T1>. Each new<> variant is an independent facet — a class satisfies them based on its actual constructor defaults, not by subtyping between new<> types.
:::
