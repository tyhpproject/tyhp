---
title: 'New Object Declaration Syntax'
status:
  tier: 0
  story: '06'
  state: complete
---

Tyhp adds new syntax to class declarations that does not exist in PHP. This includes constructor return types for explicit constructor chaining, promoted constructor parameters, the init property modifier, and property accessors. These features make class declarations more expressive and self-documenting.

## Constructor Return Types

In PHP, constructors never have a return type. Tyhp introduces two constructor return type annotations: : void and : parent(args...). These provide a structured way to declare constructor behavior and enforce correct parent constructor chaining.

## Explicit Void Constructor

The : void return type explicitly states that a constructor does not chain to a parent. This is the default behavior when no return type is specified, but writing it makes the intent clear and self-documenting.

```tyhp
<?tyhp

class SimpleClass
{
    public function __construct(
        public string $name,
        public int $age
    ): void {}
}
```

## Compiled PHP Output

The : void annotation is stripped from the PHP output. The constructor is emitted as a standard PHP constructor with no return type.

```php
<?php

class SimpleClass
{
    public function __construct(
        public string $name,
        public int $age
    ) {}
}
```

## Constructor Chaining with : parent(args...)

The : parent(args...) syntax calls the parent constructor at the START of the child constructor body. The arguments in the parent(...) call are passed to parent::__construct(). This replaces the manual parent::__construct(...) call that PHP developers write by hand and guarantees it always runs first.

```tyhp
<?tyhp

class BaseModel
{
    public function __construct(
        protected bool $isActive,
        protected int $priority,
        protected string $label
    ): void {}
}

class UserModel extends BaseModel
{
    public string $displayName;

    public function __construct(string $name): parent(false, 19, $name)
    {
        $this->displayName = $name->toupper();
    }
}
```

## Compiled PHP Output

The : parent(args...) annotation compiles to a parent::__construct(args...) call inserted at the very beginning of the constructor body. The return type annotation is removed.

```php
<?php

class BaseModel
{
    public function __construct(
        protected bool $isActive,
        protected int $priority,
        protected string $label
    ) {}
}

class UserModel extends BaseModel
{
    public string $displayName;

    public function __construct(string $name)
    {
        parent::__construct(false, 19, $name);
        $this->displayName = \strtoupper($name);
    }
}
```

## Original Incoming Values

An important detail: the arguments passed to parent(...) use the original incoming parameter values, not values modified within the constructor body. Even if a parameter is reassigned inside the constructor, the parent receives the value as it was when the constructor was called.

```tyhp
<?tyhp

class Child extends BaseModel
{
    public string $processed;

    public function __construct(string $input): parent(true, 5, $input)
    {
        $input = $input->tolower();
        $this->processed = $input;
    }
}
```

```php
<?php

class Child extends BaseModel
{
    public string $processed;

    public function __construct(string $input)
    {
        parent::__construct(true, 5, $input);
        $input = \strtolower($input);
        $this->processed = $input;
    }
}
```

:::tip
Because parent(...) is called before the constructor body executes, the parent always receives the original parameter values. This prevents subtle bugs where a developer modifies a parameter before calling parent::__construct().
:::

## Passing Static Values and Parameters

The arguments in parent(...) can be static values (literals, constants) or parameters from the current constructor. They cannot be expressions that depend on $this or computed values, since the parent call happens before the constructor body.

```tyhp
<?tyhp

class Vehicle extends BaseModel
{
    public function __construct(
        string $type,
        int $year
    ): parent(true, $year, $type) {}
}

class DefaultVehicle extends BaseModel
{
    public function __construct(): parent(false, 0, 'unknown') {}
}
```

```php
<?php

class Vehicle extends BaseModel
{
    public function __construct(
        string $type,
        int $year
    ) {
        parent::__construct(true, $year, $type);
    }
}

class DefaultVehicle extends BaseModel
{
    public function __construct()
    {
        parent::__construct(false, 0, 'unknown');
    }
}
```

## The init Property Modifier

The init modifier declares a property as settable only during construction and via the with keyword. This is distinct from PHP's readonly modifier, which prevents ALL mutation after construction — including via with. The init modifier fills the gap between fully mutable properties and fully readonly properties.

- `(no modifier)` — Set in constructor: Yes. Set after construction: Yes. Set via `with`: Yes.
- `readonly` — Set in constructor: Yes. Set after construction: No. Set via `with`: No.
- `init` — Set in constructor: Yes. Set after construction: No. Set via `with`: Yes.

```tyhp
<?tyhp

class User
{
    public init string $name;
    public init int $age;
    public string $label = '';

    public function __construct(string $name, int $age)
    {
        $this->name = $name;
        $this->age = $age;
    }
}

$user = new User('Alice', 30);

// init properties CANNOT be assigned directly after construction
// $user->name = 'Bob';  // ERROR: init property

// init properties CAN be set via with (creates a new instance)
$newUser = clone $user with [name => 'Bob'];
// $user->name is still 'Alice'
// $newUser->name is 'Bob'
```

## Compiled PHP Output for init Properties

The init modifier emits as PHP readonly. For classes with init properties, the compiler generates a __clone() method and a $__tyhpWithInit helper property that enables with to set readonly properties on the cloned instance.

```php
<?php

class User
{
    public readonly string $name;
    public readonly int $age;
    public string $label = '';

    /** @internal */
    public array $__tyhpWithInit = [];

    public function __construct(string $name, int $age, array $__withInit = [])
    {
        $this->name = $name;
        $this->age = $age;
        if (isset($__withInit['name'])) { $this->name = $__withInit['name']; }
        if (isset($__withInit['age'])) { $this->age = $__withInit['age']; }
    }

    public function __clone(): void
    {
        foreach ($this->__tyhpWithInit as $__prop => $__val) {
            match ($__prop) {
                'name' => $this->name = $__val,
                'age' => $this->age = $__val,
                default => null,
            };
        }
        $this->__tyhpWithInit = [];
    }
}
```

## Constructor Property Promotion with init

The init modifier works with constructor property promotion, just like readonly does in PHP.

```tyhp
<?tyhp

class Point
{
    public function __construct(
        public init float $x,
        public init float $y,
        public init float $z = 0.0
    ) {}
}

$p = new Point(1.0, 2.0);
$q = clone $p with [z => 5.0];
// $p = Point(1.0, 2.0, 0.0)
// $q = Point(1.0, 2.0, 5.0)
```

```php
<?php

class Point
{
    /** @internal */
    public array $__tyhpWithInit = [];

    public function __construct(
        public readonly float $x,
        public readonly float $y,
        public readonly float $z = 0.0,
        array $__withInit = []
    ) {
        if (isset($__withInit['x'])) { $this->x = $__withInit['x']; }
        if (isset($__withInit['y'])) { $this->y = $__withInit['y']; }
        if (isset($__withInit['z'])) { $this->z = $__withInit['z']; }
    }

    public function __clone(): void
    {
        foreach ($this->__tyhpWithInit as $__prop => $__val) {
            match ($__prop) {
                'x' => $this->x = $__val,
                'y' => $this->y = $__val,
                'z' => $this->z = $__val,
                default => null,
            };
        }
        $this->__tyhpWithInit = [];
    }
}
```

## init vs readonly

```tyhp
<?tyhp

class Example
{
    public readonly string $fixed;
    public init string $updatable;

    public function __construct(string $fixed, string $updatable)
    {
        $this->fixed = $fixed;
        $this->updatable = $updatable;
    }
}

$a = new Example('A', 'B');

// Both are errors — neither can be set directly after construction:
// $a->fixed = 'X';      // ERROR: readonly
// $a->updatable = 'X';  // ERROR: init

// readonly CANNOT be set via with:
// $b = clone $a with [fixed => 'X'];      // ERROR

// init CAN be set via with:
$c = clone $a with [updatable => 'X'];      // OK
```

:::note
Combining init and readonly on the same property is allowed but redundant — the readonly modifier is strictly more restrictive. The combination is semantically equivalent to just readonly.
:::

:::warning
The init modifier requires PHP 8.2+ because readonly property reinitialization in __clone() is a PHP 8.2 feature.
:::

## Best Practices

:::tip
Use : parent(args...) to clearly express constructor chaining — it ensures the parent constructor is always called first and prevents accidental omission.
:::

:::tip
Use : void on leaf classes to explicitly document that no parent constructor chaining occurs.
:::

:::tip
Use init properties when you want immutability after construction but need to support functional updates via the with keyword.
:::

:::tip
Use constructor property promotion with init for concise, immutable data classes.
:::

## Common Mistakes

:::danger
Do not use : parent(...) on a class that has no parent class — the compiler reports an error.
:::

:::danger
Do not use computed expressions or $this in parent(...) arguments — only static values and incoming parameters are allowed since the parent call happens before the constructor body.
:::

:::danger
Do not manually call parent::__construct() when using : parent(...) — the compiler inserts the call automatically and a manual call would result in a double invocation error.
:::

:::danger
Do not use init with property hooks — init requires PHP readonly semantics, which are incompatible with property hooks.
:::

:::danger
Do not try to set init properties directly after construction — they can only be set in the constructor or via the with keyword.
:::

```tyhp
<?tyhp

// ERROR: no parent class to chain to
// class NoParent {
//     public function __construct(): parent() {}
// }

// ERROR: $this not available in parent args
// class Bad extends BaseModel {
//     public function __construct(string $name): parent(true, 0, $this->process($name)) {}
// }

// ERROR: double parent call
// class Double extends BaseModel {
//     public function __construct(string $name): parent(true, 0, $name) {
//         parent::__construct(true, 0, $name);
//     }
// }

// ERROR: init with property hooks
// class BadHooks {
//     public init string $name {
//         get => $this->name;
//         set => \strtolower($value);
//     }
// }

// ERROR: init on a method
// class BadMethod {
//     public init function doSomething(): void {}
// }
```

## Compiler Errors

- Using : parent(...) on a class that does not extend another class.
- Passing arguments to parent(...) that don't match the parent constructor's parameter types.
- Using $this or method calls in parent(...) arguments.
- Manually calling parent::__construct() in the body when : parent(...) is already specified.
- Using init with property hooks (error code 4056).
- Assigning to an init property outside the constructor or with keyword (error code 4055).
- Using init on a method (error code 4003).
- Combining init with static (modifier conflict).
