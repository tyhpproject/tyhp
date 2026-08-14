---
title: 'PHP Magic Methods in Tyhp'
status:
  tier: 0
  story: '08'
  state: complete
---

Tyhp supports all of PHP's magic methods with additional type safety requirements. Magic methods that return mixed in PHP must have their return values type-narrowed before use. The compiler enforces correct signatures, auto-generates certain magic methods for generic classes, and prioritizes extension methods over __call for compile-time-safe dispatch.

## Supported Magic Methods

All standard PHP magic methods are supported in Tyhp. They must follow PHP's expected signatures but can use Tyhp's enhanced type system. The following table lists every supported magic method and its Tyhp-specific behavior.

:::member[__construct()]
Constructor. Supports Tyhp constructor property promotion with modifiers like init. The return type can optionally be : void or : parent(args) for explicit parent constructor invocation.
:::

:::member[__destruct()]
Destructor. Called when an object is garbage collected. Used internally by DisposableScope for automatic resource disposal via the := operator.
:::

:::member[__get(string $name): mixed]
Called when reading an inaccessible or non-existent property. Returns mixed — must be narrowed before use in a type-safe context.
:::

:::member[__set(string $name, mixed $value): void]
Called when writing to an inaccessible or non-existent property. Type-safe wrapper methods are encouraged instead of direct __set usage.
:::

:::member[__isset(string $name): bool]
Called by isset() or empty() on inaccessible or non-existent properties.
:::

:::member[__unset(string $name): void]
Called by unset() on inaccessible or non-existent properties.
:::

:::member[__call(string $name, array $arguments): mixed]
Called when invoking inaccessible or non-existent instance methods. Returns mixed. Extension methods are checked BEFORE __call — if an extension method matches the call, it takes priority.
:::

:::member[__callStatic(string $name, array $arguments): mixed]
Called when invoking inaccessible or non-existent static methods. Returns mixed. Same extension method priority applies as __call.
:::

:::member[__toString(): string]
Called when an object is used in a string context. Must return string — the compiler enforces this return type strictly.
:::

:::member[__invoke(...$args): mixed]
Called when an object is used as a function ($obj()). Return type is mixed — must be narrowed before use.
:::

:::member[__clone(): void]
Called after an object is cloned with clone. The compiler auto-generates __clone() for classes with init properties to support the with keyword.
:::

:::member[__debugInfo(): array]
Called by var_dump() to get the properties to display. Must return an array.
:::

:::member[__serialize(): array]
Called during serialization. Must return an array containing the object's serialized state.
:::

:::member[__unserialize(array $data): void]
Called during unserialization. Receives the array returned by __serialize().
:::

## Type Safety with mixed Returns

Magic methods like __get(), __call(), __callStatic(), and __invoke() return mixed in PHP. In Tyhp, the mixed type must be narrowed before the value can be used in a typed context. This means you must use type narrowing (is checks, instanceof) or type assertions before using the return value.

```tyhp
<?tyhp

class DynamicConfig {
    private array $data = [];

    public function __get(string $name): mixed {
        return $this->data[$name] ?? null;
    }

    public function __set(string $name, mixed $value): void {
        $this->data[$name] = $value;
    }
}

DynamicConfig $config = new DynamicConfig();
$config->dbHost = 'localhost';

// ERROR: Cannot use mixed directly without narrowing
// string $value = $config->dbHost;

// OK: Narrow with is check first
mixed $raw = $config->dbHost;
if ($raw is string) {
    string $value = $raw; // OK: narrowed to string
    echo $value;
}
```

The recommended pattern is to implement type-safe wrapper methods that internally use __get/__set but expose typed interfaces to callers.

```tyhp
<?tyhp

class TypedConfig {
    private array $data = [];

    public function __get(string $name): mixed {
        return $this->data[$name] ?? null;
    }

    // Type-safe wrapper methods — preferred approach
    public function getString(string $key, string $default = ''): string {
        mixed $val = $this->data[$key] ?? $default;
        return $val is string ? $val : $default;
    }

    public function getInt(string $key, int $default = 0): int {
        mixed $val = $this->data[$key] ?? $default;
        return $val is int ? $val : $default;
    }
}
```

## Extension Methods vs __call

Tyhp's extension methods are resolved at compile time and provide full type checking. When both an extension method and a __call magic method could handle a method call, the extension method takes priority. This means extension methods are always checked first — __call is only invoked when no matching extension method exists.

```tyhp
<?tyhp

class ApiClient {
    // __call handles unknown methods at runtime
    public function __call(string $name, array $args): mixed {
        return $this->sendRequest($name, $args);
    }
}

// Extension method — compile-time type safety
extension ApiClientExtensions extends ApiClient {
    public static function getUsers(extends ApiClient $client): array<User> {
        mixed $result = $client->sendRequest('getUsers', []);
        // narrowing and type-safe return
        return $result is array ? $result : [];
    }
}

ApiClient $api = new ApiClient();

// This calls the extension method (type-safe, compile-time checked)
array<User> $users = $api->getUsers();

// This falls through to __call (returns mixed, requires narrowing)
mixed $result = $api->unknownMethod();
```

```php
<?php

// Extension method compiles to static call:
$users = ApiClientExtensions::getUsers($api);

// __call compiles to standard PHP magic method dispatch:
$result = $api->unknownMethod();
```

## Auto-Generated Magic Methods for Generics

When a class uses generics with runtime type tracking, the Tyhp compiler adds the GenericObject trait from the tyhp/core package. This trait provides runtime generic type parameter storage and retrieval. The compiler also auto-generates constructor logic to initialize the generic type information.

```tyhp
<?tyhp

class TypedCollection<T> {
    private array $items = [];

    public function add(T $item): void {
        $this->items[] = $item;
    }

    public function get(int $index): T {
        return $this->items[$index];
    }
}
```

```php
<?php

// The compiler adds the GenericObject trait and type tracking
class TypedCollection {
    use \Tyhp\Concerns\GenericObject;

    private array $items = [];

    public function add(mixed $item): void {
        $this->items[] = $item;
    }

    public function get(int $index): mixed {
        return $this->items[$index];
    }
}

// When instantiated with concrete types:
$collection = new TypedCollection();
$collection->__setGenericTypes([\Tyhp\Type::of('int')]);
```

## __toString Must Return string

The compiler strictly enforces that __toString() returns string. Unlike PHP which coerces the return value, Tyhp treats a non-string return type as a compiler error.

```tyhp
<?tyhp

class Money {
    public function __construct(
        private int $amount,
        private string $currency
    ) {}

    // OK: returns string
    public function __toString(): string {
        return "{$this->amount} {$this->currency}";
    }
}

// ERROR: __toString must return string
// class Bad {
//     public function __toString(): int { return 42; }
// }
```

## Constructor Return Type Syntax

Tyhp extends constructor syntax with optional return type annotations. The return type can be : void (documentation purposes) or : parent(args) to indicate that the parent constructor is called with specific arguments.

```tyhp
<?tyhp

class Base {
    public function __construct(public string $name) {}
}

class Child extends Base {
    public function __construct(
        string $name,
        public int $age
    ): parent($name) {
        // parent::__construct($name) is called automatically
    }
}
```

```php
<?php

class Base {
    public function __construct(public string $name) {}
}

class Child extends Base {
    public function __construct(
        string $name,
        public int $age
    ) {
        parent::__construct($name);
    }
}
```

## __clone and the with Keyword

The compiler auto-generates __clone() for classes with init properties to support immutable cloning via the with keyword. You do not need to write __clone() manually for init-property classes — the compiler handles it.

```tyhp
<?tyhp

class Point {
    public function __construct(
        public init int $x,
        public init int $y
    ) {}
}

Point $p1 = new Point(1, 2);
Point $p2 = clone $p1 with [x => 10]; // $p2->x is 10, $p2->y is 2
```

## Best Practices

:::tip
Implement type-safe wrapper methods (getString(), getInt(), etc.) instead of relying on __get() and __set() directly. Wrapper methods provide compile-time type checking and eliminate the need for callers to narrow mixed return values.
:::

:::tip
Use extension methods instead of __call() whenever possible. Extension methods are resolved at compile time, provide full type safety, and compile to static method calls — while __call() loses all type information and requires runtime narrowing.
:::

:::tip
Use operator overloads instead of relying on __toString() for arithmetic or comparison operations. Operator overloads provide explicit, type-safe semantics.
:::

:::tip
Let the compiler auto-generate __clone() for classes with init properties. The auto-generated __clone() correctly handles the with keyword for immutable value objects.
:::

## Common Mistakes

:::danger
Relying on __get() and __set() for dynamic property creation. Tyhp disables dynamic property creation on classes — all properties must be declared in the class definition. Use __get/__set only for controlled access to a backing store like an array.
:::

:::danger
Using __call() for type-safe method dispatch. __call() returns mixed and provides no compile-time type information. Use function overloads or extension methods instead for type-safe dispatch patterns.
:::

:::danger
Assuming mixed return values from magic methods are a specific type without narrowing. Always use is checks or type assertions before using values returned from __get(), __call(), __callStatic(), or __invoke().
:::

:::danger
Returning a non-string value from __toString(). The compiler enforces that __toString() must have a string return type.
:::

:::danger
Manually writing __clone() logic for init property handling. Let the compiler generate it automatically — manual __clone() implementations may conflict with the with keyword behavior.
:::

## Compiled PHP Output

Magic methods compile to standard PHP magic methods. The compiler does not transform the magic method signatures or bodies — they pass through to PHP directly. The only additions are auto-generated magic methods for generic classes (GenericObject trait) and auto-generated __clone() for init-property classes.

```tyhp
<?tyhp

class Entity {
    public function __serialize(): array {
        return ['id' => $this->id, 'name' => $this->name];
    }

    public function __unserialize(array $data): void {
        $this->id = $data['id'];
        $this->name = $data['name'];
    }

    public function __debugInfo(): array {
        return ['id' => $this->id];
    }
}
```

```php
<?php

// Magic methods pass through to PHP unchanged
class Entity {
    public function __serialize(): array {
        return ['id' => $this->id, 'name' => $this->name];
    }

    public function __unserialize(array $data): void {
        $this->id = $data['id'];
        $this->name = $data['name'];
    }

    public function __debugInfo(): array {
        return ['id' => $this->id];
    }
}
```
