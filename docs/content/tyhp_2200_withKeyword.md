---
title: 'The with Keyword'
status:
  tier: 1
  story: '11'
  state: complete
---

The with keyword in Tyhp provides a concise way to set properties on objects and structs. It is a binary operator that produces a fully initialized value in a single expression. Combined with `readonly` properties, `clone ... with` enables immutable update patterns.

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

Emit depends on whether `with` is a **statement** or an **expression**, whether the properties are `readonly`, and the target PHP version.

**Statement** form (an assignment, or a bare `$obj with [...]`): non-readonly properties become direct assignments on the same variable — no temp, no helper.

```php
<?php

$user = new User('Alice', 30);
$user->role = 'admin';

$updated = clone $user;
$updated->name = 'Bob';
$updated->age = 25;
```

**Expression** form (for example `return clone $x with [...]`, or a `with` used as a value):

- PHP 8.5: native `clone($x, [...])` (including `clone(new Color(), [...])` for `new ... with`)
- PHP 8.2–8.4: `\Tyhp\ObjectHelper::with(...)`

```php
<?php

// PHP 8.5 expression clone with
return clone($cfg, ['enabled' => false]);

// PHP 8.2–8.4 expression clone/new with (non-readonly)
return \Tyhp\ObjectHelper::with(clone $cfg, ['enabled' => false]);
return \Tyhp\ObjectHelper::with(new Widget(), ['color' => "blue"]);
```

**Readonly `clone ... with` on PHP 8.2–8.4** (requires `build.experimentalReadonlyCloneWith: true`) emits a reflection IIFE: an anonymous subclass with `$__tyhp_overrides`, `ReflectionClass::newInstanceWithoutConstructor()`, a property copy via `ReflectionObject`, then `clone $__wrapper`. It is not a simple `__clone()` wrapper around the source object.

**Readonly `new ... with`:**

- PHP 8.4: `clone (new class(...) extends \Color { public function __clone(): void { ... } })`
- PHP 8.5: `clone(new Color(), ['alpha' => 128])`

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
$coords = \array_replace($coords, ['alt' => 100.0]);
```

:::note
Choose the form by intent: use new ... with or clone ... with when you want a brand-new value (the clone form preserves immutability of the source), and use $obj with [...] when you want to update the object you already hold and keep the same identity. Because the in-place form mutates the existing instance, it cannot assign `readonly` properties after construction — use `clone $obj with [...]` (or `new ... with`) for those.
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

$app = new App();
$app->name = 'MyApp';
$app->config = \Tyhp\ObjectHelper::with(clone $defaultConfig, ['debug' => true, 'env' => 'production']);
```

## with and readonly Properties

`readonly` properties cannot be assigned after construction. `new ... with` and `clone ... with` are allowed to set them on the new or cloned instance. Direct assignment and in-place `$obj with [...]` after construction are not.

On PHP 8.5+, `clone ... with` uses native `clone($obj, [...])`. On PHP 8.2–8.4, `clone ... with` on `readonly` properties requires `build.experimentalReadonlyCloneWith: true` in `tyhp.json` (the compiler emits a reflection IIFE: `newInstanceWithoutConstructor`, `$__tyhp_overrides`, then `clone $__wrapper` — not a simple `__clone()` wrapper around the source). `new ... with` on `readonly` does not need that flag (PHP 8.4 emits an anonymous class with `__clone()`; PHP 8.5 uses `clone(new Color(), [...])`). There is no separate `init` modifier — `readonly` plus `with` is the immutable-update pattern.

:::member[(no modifier)]
Set in constructor: Yes. Set after construction: Yes. Set via `new`/`clone` with: Yes. Set via in-place with: Yes.
:::

:::member[readonly]
Set in constructor: Yes. Set after construction: No. Set via `new`/`clone` with: Yes. Set via in-place with: No.
:::

```tyhp
<?tyhp

class Point {
    public function __construct(
        public readonly float $x,
        public readonly float $y,
        public readonly float $z = 0.0
    ) {}
}

$p = new Point(1.0, 2.0);

$q = clone $p with [z => 5.0];
// $p is still Point(1.0, 2.0, 0.0)
// $q is Point(1.0, 2.0, 5.0)

// $p->z = 3.0;  // ERROR: readonly
// $p with [z => 3.0];  // ERROR after construction: in-place with cannot set readonly
```

## with on Structs

Since structs compile to PHP associative arrays, the with keyword on structs compiles to `\array_replace` (not `\array_merge`).

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
$elevated = \array_replace($coords, ['alt' => 100.0]);
```

## Best Practices

:::tip
Use with after new or clone to set properties concisely in a single expression.
:::

:::tip
Use `readonly` properties with `clone ... with` for immutable update patterns — the original object is never mutated.
:::

:::tip
Nest with expressions for deep property initialization instead of using multiple intermediate variables.
:::

:::tip
Use with on structs for functional-style updates — since structs are value types, this is both clean and efficient.
:::

:::tip
Prefer `readonly` over leaving properties mutable when you want immutability but still need functional updates via `new`/`clone` with.
:::

## Common Mistakes

:::danger
Do not use in-place `$obj with [...]` on readonly properties — they cannot be mutated after construction. Use `new ... with` or `clone ... with` instead.
:::

:::danger
Do not assume $obj with [...] returns a copy — the in-place form mutates and returns the SAME instance. Use clone $obj with [...] when you need an independent copy that leaves the original unchanged.
:::

:::danger
Do not set private or protected properties via with from outside the class.
:::

```tyhp
<?tyhp

class Example {
    public readonly string $id;
    public readonly string $name;

    public function __construct(string $id, string $name) {
        $this->id = $id;
        $this->name = $name;
    }
}

$a = new Example('1', 'Alice');

// OK: clone ... with may set readonly properties on the copy
$c = clone $a with [name => 'Bob'];

// ERROR: in-place with cannot set readonly after construction
// $a with [name => 'Bob'];

// ERROR: 'nonExistent' is not a property of Example
// $d = clone $a with [nonExistent => 'value'];
```

:::warning
On PHP 8.2–8.4, `clone ... with` on `readonly` properties requires `build.experimentalReadonlyCloneWith: true` in `tyhp.json`. PHP 8.5+ supports it natively. `new ... with` on readonly does not need the flag.
:::

## Compiler Errors

- Setting a readonly property via in-place `$obj with [...]` after construction.
- `clone ... with` on a readonly property on PHP &lt; 8.5 without `build.experimentalReadonlyCloneWith`.
- Setting a non-existent property via with.
- Setting a private or protected property via with from outside the class.
- Applying with to an expression that is not a new expression, a clone expression, or an addressable object/struct instance.
