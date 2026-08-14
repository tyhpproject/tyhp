---
title: 'Variables and Constants in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhpdef lets you describe existing PHP global variables and constants to the Tyhp compiler. This enables type-safe access to PHP superglobals, user-defined globals, and defined constants from your Tyhp code. You can specify types, alias names with `as`, and mark top-level imports `deprecated` or `obsolete`.

## Declaring Variables

Variable declarations specify a type followed by the variable name. The compiler uses that type when your Tyhp code reads the global. There is **no** runtime existence check or `ErrorException` coercion generated from the tyhpdef.

```tyhp
<?tyhpdef

// Import a global variable with its type
string $globalAppName;

// Import an object variable
\App\Config $appConfig;

// Import a typed array
array<string, mixed> $globalSettings;
```

## `??` fallbacks (parsed, not applied)

The grammar accepts `??` after a tyhpdef variable or constant (`int $maxRetries ?? 3`). This alpha does **not** bind that default or emit `isset` / `defined()` / `ErrorException` checks. Treat `??` as reserved syntax: declare the type you actually use, and handle missing PHP globals in Tyhp/PHP yourself.

## Variable Aliasing

Variables can be aliased using `as $newName` so your Tyhp code uses a different name than the PHP global.

```tyhp
<?tyhpdef

// Import $blah1 from PHP as $globalGuest in Tyhp
Guest $blah1 as $globalGuest;
```

## Import by reference

Tyhpdef variable declarations do **not** accept `&`. `int &$sharedCounter` is not a tyhpdef form. Object values are PHP references as usual; scalars are whatever PHP already stored.

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

## Constant Aliasing

Constants can be aliased using the fully-qualified PHP name with <code>as</code> to use a different name in Tyhp.

```tyhp
<?tyhpdef

// Import PHP constant with a shorter alias in Tyhp
const int \MAX_LOOPS_ALLOWED as MAX_LOOPS;
const string \App\Config\DATABASE_URL as DB_URL;
```

## Superglobal Type Declarations

PHP superglobals (`$_GET`, `$_POST`, `$_SERVER`, and similar) are typed by the `tyhp/php` package. You do not need to redeclare them unless you want a narrower project-specific type.

## Deprecated and Obsolete

Both **top-level** variables and constants can be marked as `deprecated` or `obsolete`. Member-level markers on class constants are parsed but not enforced in this alpha.

```tyhp
<?tyhpdef

deprecated const string OLD_API_KEY;
deprecated string $legacyConfig;
```

## Runtime Behavior

Tyhpdef imports are **type information**. The compiler does not emit existence checks, coercion, or `ErrorException` for missing globals. If a PHP variable or constant is unset, PHP's usual notices/errors apply at runtime.

## Best Practices

:::tip
DO declare accurate types for the globals you actually read. Handle missing values in Tyhp (`isset`, `??` at the **use** site, or nullable types), not via tyhpdef `??` (not applied in this alpha).
:::

:::tip
DO use aliases to give cryptic or legacy PHP global names cleaner Tyhp identifiers.
:::

:::danger
DON'T import PHP superglobals ($_GET, $_POST, $_SERVER, etc.) unless you are intentionally narrowing the `tyhp/php` stubs. Duplicate declarations can conflict.
:::

:::danger
DON'T declare variables with incorrect types. If a PHP global is a string but you declare it as int, the runtime coercion may throw or silently produce wrong values.
:::
