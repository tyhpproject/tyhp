---
title: 'The init Property Modifier'
status:
  tier: 1
  story: '11'
  state: complete
---

The init modifier declares a property as settable only during construction and via the with keyword. This fills the gap between fully mutable properties and fully readonly properties. Unlike readonly, which prevents ALL mutation after construction — including via with — the init modifier allows functional updates that produce a new instance while keeping the original unchanged.

## Modifier Comparison

The following table shows how init compares to regular properties and readonly properties.

:::member[(no modifier)]
Set in constructor: Yes. Set after construction: Yes. Set via with: Yes.
:::

:::member[readonly]
Set in constructor: Yes. Set after construction: No. Set via with: No.
:::

:::member[init]
Set in constructor: Yes. Set after construction: No. Set via with: Yes.
:::

:::note
The init modifier is inspired by C#'s init accessor, adapted to Tyhp's with keyword semantics. Because with in Tyhp creates a new instance (via clone), allowing init properties to be set during with is semantically sound — the original object is never mutated.
:::

## Basic Syntax

The init modifier is placed in the property declaration alongside the visibility modifier. It can be used on standard property declarations and constructor-promoted properties.

```tyhp
<?tyhp

class User {
    public init string $name;
    public init int $age;
    public string $label = '';

    public function __construct(string $name, int $age) {
        $this->name = $name;  // OK — inside constructor
        $this->age = $age;    // OK — inside constructor
    }
}

$user = new User('Alice', 30);

// ERROR: init property cannot be set outside constructor or with
// $user->name = 'Bob';

// OK — with creates a new instance
$newUser = clone $user with [name => 'Bob'];
// $user->name is still 'Alice'
// $newUser->name is 'Bob'
```

## Constructor Property Promotion

The init modifier works with constructor property promotion (CPP), providing a concise way to declare init properties directly in the constructor signature.

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
$q = clone $p with [z => 5.0];
// $p is Point(1.0, 2.0, 0.0)
// $q is Point(1.0, 2.0, 5.0)
```

## Using with on init Properties

The with keyword is the only way to "update" init properties after construction. Since with always produces a new instance via clone, the original object's immutability is preserved.

```tyhp
<?tyhp

class Config {
    public function __construct(
        public init string $host,
        public init int $port,
        public init bool $useSsl = false
    ) {}
}

$dev = new Config('localhost', 3306);

// Create production config from dev config
$prod = clone $dev with [
    host => 'db.production.example.com',
    useSsl => true
];

// $dev is unchanged: Config('localhost', 3306, false)
// $prod is: Config('db.production.example.com', 3306, true)
```

## with on new with init Properties

When with is used with new (not clone) and the properties are init, the values are passed through an internal constructor parameter using a named argument.

```tyhp
<?tyhp

$point = new Point(1.0, 2.0) with [z => 3.0];
```

```php
<?php

$point = new Point(1.0, 2.0, __withInit: ['z' => 3.0]);
```

## init vs readonly

The key difference between init and readonly is how they interact with the with keyword. Both prevent direct mutation after construction, but init allows functional updates via with while readonly does not.

```tyhp
<?tyhp

class Example {
    public readonly string $fixed;    // Cannot be changed at all after construction
    public init string $updatable;     // Cannot be changed directly, but CAN be changed via with

    public function __construct(string $fixed, string $updatable) {
        $this->fixed = $fixed;
        $this->updatable = $updatable;
    }
}

$a = new Example('A', 'B');

// Both of these are errors:
// $a->fixed = 'X';       // ERROR — readonly
// $a->updatable = 'X';   // ERROR — init (no direct mutation)

// with keyword:
// $b = clone $a with [fixed => 'X'];       // ERROR — readonly cannot be set via with
$c = clone $a with [updatable => 'X'];       // OK — init properties CAN be set via with
```

## init + readonly Combination

Combining init and readonly on the same property is allowed but redundant. The readonly modifier is strictly more restrictive than init — it also prevents with. The combination is semantically equivalent to just readonly.

```tyhp
<?tyhp

class Config {
    // Allowed but redundant — behaves exactly like just readonly
    public init readonly string $key;

    public function __construct(string $key) {
        $this->key = $key;
    }
}

$cfg = new Config('api_key');
// $cfg->key = 'new';                        // ERROR — readonly
// $b = clone $cfg with [key => 'new'];      // ERROR — readonly prevents with
```

## Mixed init and Regular Properties

When a with expression sets both init properties and regular (mutable) properties, the compiler routes init properties through the internal __tyhpWithInit mechanism and sets regular properties directly.

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

$user = new User('Alice', 30);

// 'name' is init (routed through __tyhpWithInit),
// 'label' is regular (set directly)
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

## Inheritance

Init properties work correctly across class hierarchies. Subclass constructors forward the __withInit parameter to the parent, and the generated __clone() method calls parent::__clone() when the parent has init properties.

```tyhp
<?tyhp

class Animal {
    public function __construct(
        public init string $species,
        public init string $name
    ) {}
}

class Dog extends Animal {
    public init string $breed;

    public function __construct(string $name, string $breed) {
        parent::__construct('Canis familiaris', $name);
        $this->breed = $breed;
    }
}

$dog = new Dog('Rex', 'German Shepherd');

// Update inherited init property and own init property
$renamed = clone $dog with [name => 'Max'];
$rebranded = clone $dog with [breed => 'Labrador', name => 'Buddy'];
```

## Compiled PHP Output

Init properties compile to PHP readonly properties. The compiler generates additional infrastructure to support the with keyword: a $__tyhpWithInit property, constructor augmentation with a $__withInit parameter, and a __clone() method that applies the pending init values.

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
```

```php
<?php

class User {
    public readonly string $name;
    public readonly int $age;
    public string $label = '';

    /** @internal */
    public array $__tyhpWithInit = [];

    public function __construct(string $name, int $age, array $__withInit = []) {
        $this->name = $name;
        $this->age = $age;
        if (isset($__withInit['name'])) { $this->name = $__withInit['name']; }
        if (isset($__withInit['age'])) { $this->age = $__withInit['age']; }
    }

    public function __clone(): void {
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

## Constructor-Promoted init PHP Output

```tyhp
<?tyhp

class Point {
    public function __construct(
        public init float $x,
        public init float $y,
        public init float $z = 0.0
    ) {}
}
```

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
```

## clone with Compiled PHP Output

When clone ... with is used on a class with init properties, the compiler uses a save/restore pattern. The $__tyhpWithInit array is temporarily set on the source object before cloning, then immediately restored. The __clone() method on the new instance applies the values to the readonly properties (PHP 8.2+ allows readonly reinitialization within __clone).

```tyhp
<?tyhp

$updated = clone $user with [name => 'Bob', age => 25];
```

```php
<?php

$__origInit_1 = $user->__tyhpWithInit;
$user->__tyhpWithInit = ['name' => 'Bob', 'age' => 25];
$updated = clone $user;
$user->__tyhpWithInit = $__origInit_1;
```

## Inheritance PHP Output

```tyhp
<?tyhp

class Employee extends User {
    public init string $department;

    public function __construct(string $name, int $age, string $department) {
        parent::__construct($name, $age);
        $this->department = $department;
    }
}
```

```php
<?php

class Employee extends User {
    public readonly string $department;

    public function __construct(
        string $name,
        int $age,
        string $department,
        array $__withInit = []
    ) {
        parent::__construct($name, $age, $__withInit);
        $this->department = $department;
        if (isset($__withInit['department'])) {
            $this->department = $__withInit['department'];
        }
    }

    public function __clone(): void {
        parent::__clone();
        foreach ($this->__tyhpWithInit as $__prop => $__val) {
            match ($__prop) {
                'department' => $this->department = $__val,
                default => null,
            };
        }
        $this->__tyhpWithInit = [];
    }
}
```

## Best Practices

:::tip
Use init for properties that should be immutable after construction but need to support functional updates via with. This is ideal for value objects, configuration classes, and data transfer objects.
:::

:::tip
Prefer init over readonly when you want clone-with semantics. Use readonly only when a property must never change under any circumstance.
:::

:::tip
Use constructor property promotion with init for concise immutable value types: public function __construct(public init float $x, public init float $y) {}
:::

:::tip
Use init properties to implement the builder pattern or functional update pattern. Create a base instance and derive variations with clone $base with [...].
:::

:::tip
Use init properties in configuration classes where you want safe defaults that can be overridden during construction or via with, but not accidentally mutated later.
:::

## Common Mistakes

:::danger
Don't try to set init properties outside the constructor or with keyword. Init properties are immutable after construction — only with (which creates a new instance) can set them.
:::

:::danger
Don't confuse init with readonly. readonly prevents ALL mutation including via with. init allows mutation via with because with creates a new instance.
:::

:::danger
Don't use init with property hooks (get/set). The init modifier is for backing properties only — property hooks are incompatible because they need to intercept writes, but the underlying readonly enforcement prevents engine-level writes.
:::

:::danger
Don't use init on static properties. Static properties are not instance-scoped and are not involved in construction or clone/with patterns.
:::

:::danger
Don't use init on methods — it is a property-only modifier. Applying init to a method declaration produces a compiler error.
:::

```tyhp
<?tyhp

class User {
    public init string $name;

    public function __construct(string $name) {
        $this->name = $name;  // OK — inside constructor
    }

    public function rename(string $newName): void {
        // ERROR TYHP4055: Property 'name' is declared as 'init'
        // and can only be set during construction or via 'with'.
        // $this->name = $newName;
    }
}

$user = new User('Alice');

// ERROR — direct assignment outside constructor
// $user->name = 'Bob';

// OK — use with to create a new instance
$updated = clone $user with [name => 'Bob'];
```

```tyhp
<?tyhp

class Bad {
    // ERROR TYHP4056: 'init' modifier cannot be used with property hooks
    // public init string $name {
    //     get => $this->name;
    //     set => \strtolower($value);
    // }

    // ERROR: 'init' is not a valid modifier for methods
    // public init function doSomething(): void {}

    // ERROR: init + static is a modifier conflict
    // public static init string $shared;
}
```

:::warning
The init modifier requires PHP 8.2+ as the target version. The compiled PHP uses readonly property reinitialization within __clone(), which is a PHP 8.2 feature. The compiler emits a diagnostic if the target PHP version is below 8.2.
:::

## Compiler Errors

- TYHP4055: Property '{name}' is declared as 'init' and can only be set during construction or via 'with' — triggered when assigning to an init property outside the constructor or a with expression.
- TYHP4056: 'init' modifier cannot be used with property hooks — triggered when init is combined with get/set property hooks on the same property.
- TYHP4003: 'init' is not a valid modifier — triggered when init is applied to methods, classes, or other non-property declarations.
- TYHP4005: Modifier conflict — triggered when init is combined with static.
