---
title: 'Use Statements and Imports'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhp supports all standard PHP use statement forms and adds two Tyhp-specific extensions: generic arguments on import aliases and the `use extension` statement for importing extension method providers. Use statements work the same in Tyhp as they do in PHP -- they create aliases for fully-qualified class, function, and constant names.

## Standard Use Statements

All PHP use statement forms are supported in Tyhp. These behave identically to PHP -- they create namespace aliases for the current file.

```tyhp
<?tyhp

// Import a class
use App\Models\User;

// Import with alias
use App\Models\User as UserModel;

// Import a function
use function App\Helpers\formatDate;

// Import a constant
use const App\Config\MAX_RETRIES;
```

## Group Use Declarations

Group use declarations import multiple symbols from the same namespace prefix in a single statement.

```tyhp
<?tyhp

// Group import -- same type
use App\Models\{User, Post, Comment};

// Group import -- mixed types
use App\Services\{
    AuthService,
    function validateToken,
    const MAX_LOGIN_ATTEMPTS
};
```

## Compiled PHP Output for Standard Use Statements

Standard use statements pass through to PHP largely unchanged. The emitter automatically prunes unused imports and sorts them alphabetically:

```php
<?php
declare(strict_types=1);

use App\Models\{User, Post, Comment};

use App\Services\{
    AuthService,
    function validateToken,
    const MAX_LOGIN_ATTEMPTS
};
```

## Generic Import Aliases

Tyhp allows generic type arguments on import aliases. This lets you create a local alias for a specific instantiation of a generic class. The generic arguments are compile-time only -- they are erased from the PHP output. The alias becomes a regular PHP use statement without the generic arguments.

```tyhp
<?tyhp

// Import a generic class with specific type arguments
use App\Collections\TypedList as IntList<int>;
use App\Collections\TypedMap as UserMap<string, User>;

// Now IntList is an alias for TypedList<int>
IntList $numbers = new IntList();
$numbers->add(42);        // OK -- int
// $numbers->add('hello');   // Compile error -- string is not int

// UserMap is an alias for TypedMap<string, User>
UserMap $users = new UserMap();
$users->set('admin', new User('Admin'));
```

Compiles to:

```php
<?php
declare(strict_types=1);

use App\Collections\TypedList as IntList;
use App\Collections\TypedMap as UserMap;

$numbers = new IntList();
$numbers->add(42);

$users = new UserMap();
$users->set('admin', new User('Admin'));
```

:::note
The generic arguments on the alias are a compile-time concept. They tell the Tyhp compiler to treat the alias as if it were the generic class with those specific type arguments. In the PHP output, only the plain alias remains.
:::

## Use Extension Statements

The `use extension` statement imports extension method providers into the current scope. Extension methods allow adding methods to existing types without modifying them. When you import an extension, its methods become available on the extended type within the importing file.

```tyhp
<?tyhp

// Import an extension -- its methods are now available on the extended type
use extension App\Extensions\StringHelpers;

// StringHelpers extends string with methods like toCamelCase()
string $name = 'hello_world';
string $camel = $name->toCamelCase();  // 'helloWorld'
```

Use extension statements also support trait-like adaptation syntax. This allows you to rename or hide specific extension methods when importing.

```tyhp
<?tyhp

// Import extension with adaptations
use extension App\Extensions\StringHelpers {
    StringHelpers::toCamelCase as toCC;
}

string $name = 'hello_world';
string $camel = $name->toCC();  // Uses the renamed method
```

## Compiled PHP Output for Use Extension

Extension method calls compile to static method calls on the extension class. The `use extension` statement itself is removed from the output (resolved at compile time) and replaced with a standard `use` import for the extension class:

```tyhp
<?tyhp

use extension App\Extensions\StringHelpers;

string $name = 'hello_world';
string $camel = $name->toCamelCase();
```

Compiles to:

```php
<?php
declare(strict_types=1);

use App\Extensions\StringHelpers;

$name = 'hello_world';
$camel = StringHelpers::toCamelCase($name);
```

## Tyhpdef Use Statements

In tyhpdef files, use statements serve the same purpose -- importing type symbols into the current scope. Tyhpdef files describe external PHP library signatures for the Tyhp compiler. The same use statement forms (including group use, typed use, and use extension) are supported in tyhpdef files.

```tyhp
<?tyhpdef

// Tyhpdef use statements follow the same syntax
use Some\External\Library\{ClassA, ClassB};
use function Some\External\Library\helperFunction;
use const Some\External\Library\SOME_CONSTANT;

// Tyhpdef can also declare type aliases
type StringOrNull = string|null;
```

## Import Pruning

During compilation, the emitter automatically prunes unused use statements from the PHP output. If a use statement is declared but never referenced in the emitted code, it is removed. The emitter also sorts remaining imports alphabetically and groups them by type (classes, functions, constants) following PHP community conventions.

Imports for Tyhp-only constructs that are erased during emission (type aliases, generic type parameters, struct types) are automatically removed since they have no PHP equivalent.

## Best Practices

:::tip
Use group imports to keep use statements organized when importing multiple symbols from the same namespace.
:::

:::tip
Use generic import aliases to create type-safe aliases for specific generic instantiations. This is cleaner than repeating generic arguments everywhere.
:::

:::tip
Use the `use extension` statement to bring extension methods into scope. Extension methods are only available when explicitly imported -- this gives you full control over which extensions are active.
:::

:::tip
Place use statements at the top of the file, before any namespace or class declarations. The emitter handles automatic import pruning, so unused imports are cleaned up in the output.
:::

:::tip
Use trait-like adaptation syntax on `use extension` when you need to rename extension methods to avoid naming conflicts.
:::

## Common Mistakes

:::danger
Don't use generic arguments on imports without an alias. The syntax requires `as AliasName<GenericArgs>` -- you cannot write `use App\MyClass<int>;` directly.
:::

:::danger
Don't import extension classes with a plain `use` statement and expect extension methods to work. You must use the `use extension` syntax to activate extension methods on the target type.
:::

:::danger
Don't import Tyhp-only types (type aliases, struct names) in PHP interop code. These are erased during compilation and do not exist at runtime.
:::

:::danger
Don't forget to import extensions before using their methods. Without a `use extension` import, calling an extension method produces a compile error because the method is not recognized on the base type.
:::

## Compiler Error Examples

```tyhp
<?tyhp

// ERROR: Generic arguments require an alias
// use App\Collections\TypedList<int>;

// Correct: use 'as' to create a named alias with generic args
use App\Collections\TypedList as IntList<int>;

// ERROR: Extension methods not available without 'use extension'
// use App\Extensions\StringHelpers;  // plain import, not extension import
// $name->toCamelCase();  // method not found on string

// Correct: use 'use extension' to activate extension methods
use extension App\Extensions\StringHelpers;
string $name = 'hello_world';
string $camel = $name->toCamelCase();  // OK
```
