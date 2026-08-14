---
title: 'Importing PHP Variables and Constants'
status:
  tier: 1
  story: '11'
  state: complete
---

Tyhp uses tyhpdef files to declare the types of PHP global variables, constants, functions, classes, and interfaces that your Tyhp code interacts with. Since Tyhp requires explicit types for all values, tyhpdef provides the bridge between PHP's dynamic runtime and Tyhp's static type system. Tyhpdef files use the <?tyhpdef opening tag and support importing constants, variables, functions, classes, traits, interfaces, and enums with full type annotations.

## What Are Tyhpdef Files?

Tyhpdef files (.tyhpdef) are declaration files that tell the Tyhp compiler about the types of PHP symbols that exist outside of your Tyhp codebase. They are similar to TypeScript's .d.ts files — they contain only type signatures, no implementation. The compiler uses these declarations to type-check your code against PHP's standard library, extensions, and third-party packages.

- Composer packages with `package.tyhp.json` (`tyhp/php`, `tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`) provide types for PHP builtins and Tyhp runtime APIs
- Built-in compiler registrations cover language types such as `decimal` aliases, compile-time constructs, and generic overlays the binder always knows
- User tyhpdef files declare types for your project's PHP dependencies and global variables
- Auto-generated tyhpdefs (`generate_tyhpdef`) are **not** in this alpha — write declarations by hand

## Importing Constants

PHP constants (defined with define() or const) are imported into tyhpdef files using the const keyword with an explicit type annotation.

```tyhp
<?tyhpdef

namespace App\Config;

// Import a constant with its type
const string APP_NAME;

// Import with alias — use DATABASE_HOST in Tyhp code
const string DB_HOST as DATABASE_HOST;

// Mark a constant as deprecated
deprecated const string OLD_API_KEY;

// PHP extension constants
const int PHP_INT_MAX;
const string PHP_EOL;
const bool PHP_ZTS;
```

```php
<?php

// Constants compile to direct PHP constant access
echo APP_NAME;

// Aliased constants compile to the original constant name
echo DB_HOST;
```

## Importing Variables

PHP global variables can be imported into tyhpdef files with explicit type annotations. The as keyword aliases the variable name within Tyhp code.

```tyhp
<?tyhpdef

// Import a global variable with its type
string $globalConfig;

// Import with alias (different variable name in Tyhp)
string $legacy_var as $modernVar;

// Mark as deprecated
deprecated string $oldSetting;

// Import superglobals with proper types
array<string, mixed> $GLOBALS;
array<string, string> $_SERVER;
array<string, string> $_ENV;
array<string, mixed> $_GET;
array<string, mixed> $_POST;
```

```php
<?php

// Imported variables compile to direct PHP variable access
echo $globalConfig;

// Aliased variables compile to the original variable name
echo $legacy_var;
```

## Importing Functions

PHP functions are imported in tyhpdef files with full type signatures, including parameter types, return types, and optional parameter markers. This allows the Tyhp type checker to validate calls to PHP functions. Function overloads (multiple signatures for the same function) are supported.

```tyhp
<?tyhpdef

// Import a PHP function with type annotations
function array_map(?callable $callback, array $array, array ...$arrays): array;

// Import with overloaded signatures
function str_replace(
    string|array $search,
    string|array $replace,
    string $subject
): string;
function str_replace(
    string|array $search,
    string|array $replace,
    array $subject
): array;

// Import with alias
function str_contains(string $haystack, string $needle) as stringContains: bool;

// Mark as deprecated
deprecated function mysql_connect(string $server): mixed;

// Import as async function
async function fetchRemoteData(string $url): string;
```

## Importing Classes and Interfaces

PHP classes and interfaces are imported with their full type signatures, including method signatures, properties, constants, and inheritance. Generic type parameters can be added to PHP classes that Tyhp provides generic overlays for.

```tyhp
<?tyhpdef

class DateTime {
    public function __construct(
        string $datetime = 'now',
        ?DateTimeZone $timezone = null
    );
    public function format(string $format): string;
    public function getTimestamp(): int;
    public function modify(string $modifier): DateTime|false;
    public static function createFromFormat(
        string $format,
        string $datetime
    ): DateTime|false;
}

// Add generics to PHP classes for type safety
class SplStack<T> {
    public function push(T $value): void;
    public function pop(): T;
    public function top(): T;
    public function isEmpty(): bool;
    public function count(): int;
}

// Interface import
interface JsonSerializable {
    public function jsonSerialize(): mixed;
}
```

## `??` on tyhpdef imports (not applied)

The grammar accepts `??` after a tyhpdef variable or constant (`int $dbPort ?? 5432`). This alpha does **not** bind that default or emit `defined()` / null-coalesce / `ErrorException` checks. Declare the type you actually use, and handle missing PHP globals in Tyhp (`isset`, `??` at the **use** site, or nullable types).

## Aliasing with as

The as keyword renames an imported symbol within Tyhp code. This is useful when PHP constants or variables have names that conflict with Tyhp reserved words, or when you want a more descriptive name. The alias is compile-time only — the generated PHP uses the original name.

```tyhp
<?tyhpdef

// Import and rename a constant
const string PHP_EOL as LINE_ENDING;

// Import and rename a variable
array $GLOBALS as $phpGlobals;

// Import and rename a function
function str_contains(string $haystack, string $needle) as containsString: bool;
```

```tyhp
<?tyhp

// Use the alias in Tyhp code
echo LINE_ENDING;  // refers to PHP_EOL

bool $found = containsString('hello world', 'world'); // calls \str_contains()
```

```php
<?php

// Aliases compile to the original PHP symbol names
echo PHP_EOL;

$found = \str_contains('hello world', 'world');
```

## Deprecated and Obsolete Markers

Tyhpdef supports deprecated and obsolete markers on imported declarations. Using a deprecated symbol emits a compiler warning, while using an obsolete symbol emits an error. This helps projects gradually migrate away from legacy PHP APIs.

```tyhp
<?tyhpdef

// deprecated: still works, but compiler warns on use
deprecated function mysql_connect(string $server): mixed;

// obsolete: should not be used, compiler errors on use
obsolete function ereg(string $pattern, string $string): int|false;

// deprecated constant
deprecated const string LEGACY_API_URL;
```

```tyhp
<?tyhp

// WARNING: 'mysql_connect' is deprecated
// $conn = \mysql_connect('localhost');

// ERROR: 'ereg' is obsolete and should not be used
// $result = \ereg('[0-9]+', $input);
```

## Tyhpdef File Organization

Tyhpdef files are loaded from multiple sources in a specific priority order. Higher-priority sources take precedence when the same symbol is declared in multiple places.

1. Built-in compiler registrations — language types always available (highest priority).
2. Composer packages with `package.tyhp.json` — `tyhp/php` (PHP builtins; stubs cover 8.2+ APIs) plus `tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`.
3. User project tyhpdefs — files matching `tyhpdefInclude` in `tyhp.json`, plus `include` entries that end in `.tyhpdef` or `package.tyhp.json`. Omitting `tyhpdefInclude` loads **no** project `.tyhpdef` files (`tyhp init` does not set the glob).

Auto-generated Composer package tyhpdefs are not produced in this alpha.

## Best Practices

:::tip
Always provide types for all imported PHP variables and constants. Tyhp requires explicit type annotations in tyhpdef — untyped imports are not allowed.
:::

:::tip
Handle missing PHP globals at the use site (`isset`, `??` in Tyhp code, or nullable types). Tyhpdef `??` is parsed but not applied in this alpha.
:::

:::tip
Use as aliases to give PHP symbols more descriptive names in Tyhp code. The alias is compile-time only and does not affect the generated PHP.
:::

:::tip
Mark legacy or deprecated PHP APIs with the deprecated keyword in your tyhpdef files. This gives developers compiler warnings when they use APIs that should be replaced.
:::

:::tip
Add generic type parameters to PHP classes like SplStack<T>, SplQueue<T>, and SplFixedArray<T> in tyhpdef for better type safety with SPL collections.
:::

## Common Mistakes

:::danger
Accessing PHP globals without importing via tyhpdef first. Tyhp does not allow untyped global access — all PHP globals, constants, and functions must have tyhpdef declarations for the compiler to validate usage.
:::

:::danger
Assuming imported variables are non-null unless declared so. If a PHP variable might not be set at runtime, declare it as nullable (`?type`) and check it in Tyhp.
:::

:::danger
Duplicating declarations that already exist in `tyhp/php`. Prefer that package for PHP standard library functions and classes instead of hand-writing overlapping tyhpdefs.
:::

:::danger
Using <?tyhp in tyhpdef files. Tyhpdef files must use the <?tyhpdef opening tag. The tyhpdef mode is a separate language mode with its own set of valid declarations.
:::

## Compiler Errors

```tyhp
<?tyhpdef

// ERROR: Missing type annotation
// const APP_NAME;  // Must specify type: const string APP_NAME;

// ERROR: Missing type on variable import
// $globalConfig;  // Must specify type: string $globalConfig;

// OK: All of these are valid tyhpdef declarations
const string APP_NAME;
const int MAX_RETRIES;
string $globalConfig;
deprecated function old_func(): void;
```

:::warning
Tyhpdef files use the <?tyhpdef opening tag, NOT <?tyhp. The tyhpdef mode is a separate language mode that supports only declaration syntax: const, variable imports, function, class, trait, interface, and enum declarations. No implementation code is allowed.
:::
