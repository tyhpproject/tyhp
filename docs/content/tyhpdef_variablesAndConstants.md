---
title: 'Variables and Constants in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhpdef lets you describe existing PHP global variables and constants to the Tyhp compiler. This enables type-safe access to PHP superglobals, user-defined globals, and defined constants from your Tyhp code. You can specify types, provide default values with the ?? operator, and alias names for cleaner Tyhp usage.

## Declaring Variables

Variable declarations specify a type followed by the variable name. When Tyhp imports a variable at runtime, it checks the value and attempts to coerce it to the declared type. If coercion fails, an ErrorException is thrown.

```tyhp
<?tyhpdef

// Import a global variable with its type
string $globalAppName;

// Import an object variable
\App\Config $appConfig;

// Import a typed array
array<string, mixed> $globalSettings;
```

## Default Values with ??

The `??` operator provides a fallback value when the PHP variable is unset or null. This prevents runtime errors when accessing variables that may not exist.

```tyhp
<?tyhpdef

// If $debugMode is not set in PHP, default to false
bool $debugMode ?? false;

// If $maxRetries is not set, default to 3
int $maxRetries ?? 3;

// If $appName is not set, use a default string
string $appName ?? "MyApp";
```

## Variable Aliasing

Variables can be aliased using `as $newName` so your Tyhp code uses a different name than the PHP global.

```tyhp
<?tyhpdef

// Import $blah1 from PHP as $globalGuest in Tyhp
Guest $blah1 as $globalGuest;

// Import and alias with a default
string $legacy_app_name as $appName ?? "DefaultApp";
```

## Import by Reference

By default, imported scalar variables are copies — changes in Tyhp do not affect the PHP global and vice versa. To keep changes synchronized, import by reference using `&`. Object variables are always references to the same instance, even without `&`.

```tyhp
<?tyhpdef

// Copy: changes in Tyhp won't affect the PHP global
int $requestCount;

// Reference: changes are synchronized with PHP
int &$sharedCounter;

// Reference with alias and default
string &$rawInput as $userInput ?? "";
```

:::note
Object variables are always references to the same instance regardless of whether & is used. The & modifier only matters for scalar types (int, float, string, bool, array).
:::

## Declaring Constants

Constants are declared using the `const` keyword followed by the type and name. Constants are always immutable and globally accessible.

```tyhp
<?tyhpdef

// Simple constant declarations
const string APP_ENV;
const bool DEBUG_MODE;
const int MAX_CONNECTIONS;
const float PI_PRECISE;
const array<string> SUPPORTED_LOCALES;
```

## Constant Defaults with ??

Like variables, constants can have fallback values using `??`. The default is used if the constant is not defined in PHP at runtime.

```tyhp
<?tyhpdef

// Constants with defaults
const float TAX_RATE ?? 0.08;
const string DEFAULT_LOCALE ?? "en_US";
const int CACHE_TTL ?? 3600;
const bool FEATURE_FLAG_ENABLED ?? false;
```

## Constant Aliasing

Constants can be aliased using the fully-qualified PHP name with <code>as</code> to use a different name in Tyhp.

```tyhp
<?tyhpdef

// Import PHP constant with a shorter alias in Tyhp
const int \MAX_LOOPS_ALLOWED as MAX_LOOPS;
const string \App\Config\DATABASE_URL as DB_URL;
```

## Superglobal Type Declarations

The compiler ships with built-in type information for PHP superglobals (`$_GET`, `$_POST`, `$_SERVER`, etc.), so you do not need to declare them yourself. However, if you need to narrow their types for a specific project, you can redeclare them in a project-level tyhpdef file.

## Deprecated and Obsolete

Both variables and constants can be marked as `deprecated` or `obsolete`.

```tyhp
<?tyhpdef

deprecated const string OLD_API_KEY;
deprecated string $legacyConfig;
```

## Runtime Behavior

When Tyhp imports a variable or constant at runtime, it performs two steps:

1. Checks if the variable/constant exists. If it does not and no ?? default is provided, an ErrorException is thrown.
2. Attempts to coerce the value to the declared type. If coercion fails, an ErrorException is thrown.

Global variables and constants can also be imported inside regular Tyhp code (not just Tyhpdef files). This allows you to wrap the import in a try-catch block for graceful error handling.

## Best Practices

:::tip
DO provide ?? defaults for variables and constants that may not be defined in all environments. This prevents runtime errors in development vs production scenarios.
:::

:::tip
DO use aliases to give cryptic or legacy PHP global names cleaner Tyhp identifiers.
:::

:::danger
DON'T import PHP superglobals ($_GET, $_POST, $_SERVER, etc.) in your tyhpdef files. The compiler already has built-in type information for them.
:::

:::danger
DON'T declare variables with incorrect types. If a PHP global is a string but you declare it as int, the runtime coercion may throw or silently produce wrong values.
:::
