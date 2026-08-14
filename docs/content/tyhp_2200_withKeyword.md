---
title: 'The with Keyword'
status:
  tier: 1
  story: '11'
  state: complete
---

The with keyword in Tyhp provides a concise way to set properties on objects and structs. It is a binary operator that produces a fully initialized value in a single expression. Combined with the init property modifier, with enables clean immutable update patterns.

## The Three Forms of with

Tyhp supports three forms of the with keyword, distinguished by what appears on its left-hand side:

:::member[new X(...) with [...]]
Follows a new expression. Constructs a fresh instance and sets the listed properties on it. The result is the newly constructed object.
:::

:::member[clone $x with [...]]
Follows a clone expression. Copies the source object (shallow clone), sets the listed properties on the copy, and leaves the original unchanged. The result is the cloned object. This is the immutable-update form.
:::

:::member[$obj with [...]]
Operates in-place on an existing object instance. It MUTATES the same instance and RETURNS THE SAME instance — there is no clone and no new. Use this form when you want to set several properties on an object you already hold and keep working with that exact object.
:::

## Basic Syntax

The with keyword follows a new expression, a clone expression, or an existing object/struct value, and is followed by [] or {} syntax where keys are property names and values are the values to assign.

```tyhp
<?tyhp

class User {
    public string $name;
    public int $age;
    public string $role = 'user';

    public function __construct(string $name, int $age) {
        $this->name = $name;
        $this->age = $age;
    }
}

// Using with after new
$user = new User('Alice', 30) with [
    role => 'admin'
];

// Using with after clone
$updated = clone $user with [
    name => 'Bob',
    age => 25
];
```

## Compiled PHP Output

The with keyword compiles to a temporary variable followed by property assignment statements. The compiler generates unique temporary variable names to avoid collisions.

```php
<?php

// new ... with compiles to temp variable + property assignments
$__with_1 = new User('Alice', 30);
$__with_1->role = 'admin';
$user = $__with_1;

// clone ... with compiles similarly
$__with_2 = clone $user;
$__with_2->name = 'Bob';
$__with_2->age = 25;
$updated = $__with_2;
```

## In-Place with on an Existing Instance

When with is applied directly to an existing object instance (not preceded by new or clone), it mutates that instance in place and evaluates to the SAME instance. No clone is made and no new object is constructed — any other reference to the object sees the updated values. This is the form to use when you want to batch-assign several properties on an object you already hold.

```tyhp
<?tyhp

class User {
    public string $name;
    public int $age;
    public string $role = 'user';

    public function __construct(string $name, int $age) {
        $this->name = $name;
        $this->age = $age;
    }
}

$user = new User('Alice', 30);

// In-place with: mutates $user and returns the same instance
$same = $user with [
    age => 31,
    role => 'admin'
];

// $same and $user are the SAME object
// $user->age is now 31 and $user->role is now 'admin'
// ($same === $user) is true
```

## Compiled PHP Output for In-Place with

For an object instance, the in-place form compiles to direct property-assignment statements on the same variable. The expression's value is the object itself, so it can be assigned or chained without a clone or a constructor call.

```php
<?php

$user = new User('Alice', 30);

// $user with [...] emits direct property assignments on the same variable
$user->age = 31;
$user->role = 'admin';
$same = $user;

// No clone, no new — $same and $user reference the same object
```

For a struct value (which is backed by a PHP associative array), the in-place form emits as an array replace/merge that writes the updated keys back into the same variable.

```tyhp
<?tyhp

struct Coordinates {
    float $lat;
    float $lng;
    float $alt = 0.0;
}

$coords = new Coordinates() with [lat => 40.7, lng => -74.0];

// In-place with on an existing struct value
$coords with [alt => 100.0];
```

```php
<?php

$coords = ['lat' => 40.7, 'lng' => -74.0, 'alt' => 0.0];

// In-place struct with emits an array replace into the same variable
$coords = \array_merge($coords, ['alt' => 100.0]);
```

:::note
Choose the form by intent: use new ... with or clone ... with when you want a brand-new value (the clone form preserves immutability of the source), and use $obj with [...] when you want to update the object you already hold and keep the same identity. Because the in-place form mutates the existing instance, it does not route init properties through the clone mechanism — init properties cannot be changed by an in-place with after construction.
:::

## Nested with Expressions

The with keyword supports nesting. Inner with expressions are processed before outer ones.

```tyhp
<?tyhp

class Config {
    public bool $debug = false;
    public string $env = 'dev';
}

class App {
    public Config $config;
    public string $name;
}

$app = new App() with [
    name => 'MyApp',
    config => clone $defaultConfig with [
        debug => true,
        env => 'production'
    ]
];
```

```php
<?php

// Inner with is processed first
$__with_1 = clone $defaultConfig;
$__with_1->debug = true;
$__with_1->env = 'production';

$__with_2 = new App();
$__with_2->name = 'MyApp';
$__with_2->config = $__with_1;
$app = $__with_2;
```

## with and init Properties

The init modifier declares a property as settable only during construction and via the with keyword. This is distinct from readonly, which prevents ALL mutation after construction — including via with. The init modifier enables immutable update patterns: because with always creates a new instance (via clone), the original object is never mutated.

:::member[(no modifier)]
Set in constructor: Yes. Set after construction: Yes. Set via with: Yes.
:::

:::member[readonly]
Set in constructor: Yes. Set after construction: No. Set via with: No.
:::

:::member[init]
Set in constructor: Yes. Set after construction: No. Set via with: Yes.
:::

```tyhp
<?tyhp

class Point {
    public function __construct(
        public init float $x,
        public init float $y,
        public init float $z = 0.0
    ) {}
}

$p = new Point(1.0, 2.0);

// init properties CAN be set via with (creates a new instance)
$q = clone $p with [z => 5.0];
// $p is still Point(1.0, 2.0, 0.0)
// $q is Point(1.0, 2.0, 5.0)

// init properties CANNOT be assigned directly after construction
// $p->z = 3.0;  // ERROR: init property cannot be set outside constructor or with
```

## Compiled PHP Output for init Properties with with

When with is used on a class with init properties, the compiler generates a save/restore pattern. The init properties are emitted as PHP readonly, so they cannot be set directly after construction. Instead, the compiler temporarily sets a $__tyhpWithInit array on the source object before cloning, then restores it. The generated __clone() method reads and applies the values.

```php
<?php

class Point {
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

    public function __clone(): void {
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

// clone $p with [z => 5.0] compiles to:
$__origInit_1 = $p->__tyhpWithInit;
$p->__tyhpWithInit = ['z' => 5.0];
$q = clone $p;
$p->__tyhpWithInit = $__origInit_1;
```

## with on new with init Properties

When with is used with new (not clone) and the properties are init, the values are passed through the $__withInit constructor parameter using a named argument.

```tyhp
<?tyhp

$point = new Point(1.0, 2.0) with [z => 3.0];
```

```php
<?php

$point = new Point(1.0, 2.0, __withInit: ['z' => 3.0]);
```

## Mixed init and Regular Properties

When a with expression sets both init properties and regular (mutable) properties, the compiler routes init properties through the __tyhpWithInit mechanism and sets regular properties directly.

```tyhp
<?tyhp

class User {
    public init string $name;
    public init int $age;
    public string $label = '';

    public function __construct(string $name, int $age) {
        $this->name = $name;
        $this->age = $age;
    }
}

// 'name' is init, 'label' is regular
$labeled = clone $user with [name => 'Charlie', label => 'VIP'];
```

```php
<?php

// init properties go through __tyhpWithInit, regular ones are set directly
$__origInit_1 = $user->__tyhpWithInit;
$user->__tyhpWithInit = ['name' => 'Charlie'];
$labeled = clone $user;
$user->__tyhpWithInit = $__origInit_1;
$labeled->label = 'VIP';
```

## with on Structs

Since structs compile to PHP associative arrays, the with keyword on structs compiles to array merge or direct key override.

```tyhp
<?tyhp

struct Coordinates {
    float $lat;
    float $lng;
    float $alt = 0.0;
}

$coords = new Coordinates() with [lat => 40.7, lng => -74.0];
$elevated = clone $coords with [alt => 100.0];
```

```php
<?php

$coords = ['lat' => 40.7, 'lng' => -74.0, 'alt' => 0.0];
$elevated = \array_merge($coords, ['alt' => 100.0]);
```

## Best Practices

:::tip
Use with after new or clone to set properties concisely in a single expression.
:::

:::tip
Use init properties with with for immutable update patterns — the original object is never mutated.
:::

:::tip
Nest with expressions for deep property initialization instead of using multiple intermediate variables.
:::

:::tip
Use with on structs for functional-style updates — since structs are value types, this is both clean and efficient.
:::

:::tip
Prefer init over leaving properties mutable when you want immutability but need to support functional updates via with.
:::

## Common Mistakes

:::danger
Do not use with on readonly properties — they cannot be set via with. Use init instead if you need immutable properties that support with.
:::

:::danger
Do not confuse init with readonly — readonly prevents with, while init allows it.
:::

:::danger
Do not assume $obj with [...] returns a copy — the in-place form mutates and returns the SAME instance. Use clone $obj with [...] when you need an independent copy that leaves the original unchanged.
:::

:::danger
Do not set private or protected properties via with from outside the class.
:::

:::danger
Do not mix manual parent::__construct() calls with the : parent() constructor return type when using init properties — the compiler handles parent chaining automatically.
:::

```tyhp
<?tyhp

class Example {
    public readonly string $id;
    public init string $name;

    public function __construct(string $id, string $name) {
        $this->id = $id;
        $this->name = $name;
    }
}

$a = new Example('1', 'Alice');

// ERROR: readonly properties cannot be set via with
// $b = clone $a with [id => '2'];

// OK: init properties can be set via with
$c = clone $a with [name => 'Bob'];

// ERROR: 'nonExistent' is not a property of Example
// $d = clone $a with [nonExistent => 'value'];
```

:::warning
The with keyword requires PHP 8.2+ when used with init properties, because readonly property reinitialization in __clone() is a PHP 8.2 feature.
:::

## Compiler Errors

- Setting a readonly property via with (use init instead).
- Setting a non-existent property via with.
- Setting a private or protected property via with from outside the class.
- Applying with to an expression that is not a new expression, a clone expression, or an addressable object/struct instance.
- Setting an init property via the in-place ($obj with [...]) form after construction — init properties can only be set during construction, via new ... with, or via clone ... with.
