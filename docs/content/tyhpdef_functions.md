---
title: 'Functions in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhpdef allows you to declare function signatures that describe existing PHP functions to the Tyhp type system. Function declarations in tyhpdef are signature-only — they have no function body, just parameter types and a return type followed by a semicolon. Functions can be global or namespaced, and support generic type parameters, constraints, reference parameters, variadic arguments, optional parameters with defaults, async markers, and aliases.

## Basic Function Declarations

A function declaration in tyhpdef consists of the function keyword, the function name, typed parameters in parentheses, a return type, and a terminating semicolon. All parameters must have type annotations and a return type must be specified.

```tyhp
<?tyhpdef

function \strlen(string $string): int;

function \str_contains(string $haystack, string $needle): bool;

function \array_key_exists(int|string $key, array $array): bool;

function \is_numeric(mixed $value): bool;
```

## Optional Parameters and Defaults

Parameters can have default values, making them optional. The default value appears after the equals sign, just like in PHP.

```tyhp
<?tyhpdef

function \substr(string $string, int $offset, ?int $length = null): string;

function \implode(string $separator = "", array<string> $array): string;

function \json_encode(
    mixed $value,
    int $flags = 0,
    int $depth = 512
): string|false;
```

## Reference and Variadic Parameters

Reference parameters use the ampersand prefix, and variadic parameters use the ellipsis syntax. Variadic parameters must be the last parameter.

```tyhp
<?tyhpdef

function \sort(array &$array, int $flags = SORT_REGULAR): true;

function \usort<T>(array<T> &$array, callable<T, T, int> $callback): true;

function \array_push<T>(array<T> &$array, T ...$values): int;

function \sprintf(string $format, mixed ...$values): string;
```

## Generic Function Declarations

Functions can declare generic type parameters with optional constraints. The generic parameters are placed in angle brackets after the function name.

```tyhp
<?tyhpdef

function \array_map<T, U>(
    callable<T, U> $callback,
    array<T> $array
): array<U>;

function \array_filter<T>(
    array<T> $array,
    ?callable<T, bool> $callback = null
): array<T>;

function \array_reduce<T, TCarry>(
    array<T> $array,
    callable<TCarry, T, TCarry> $callback,
    TCarry $initial
): TCarry;

function findByType<T extends Entity>(string $type): ?T;
```

## Async Function Declarations

Functions that return a Promise can be declared with the async keyword. When declared as async, the return type represents the resolved value type — the compiler understands that the actual PHP function returns a Promise wrapping that type.

```tyhp
<?tyhpdef

async function fetchUserData(int $userId): UserData;

async function downloadFile(string $url): string;

async function sendNotification(string $to, string $message): void;
```

## Function Aliases

A function can be imported under a different name using the as keyword. The original PHP function name comes first, then as, then the alias name that Tyhp code will use.

```tyhp
<?tyhpdef

function \testEmail as test_email(string $emailAddress): bool;

function \array_key_exists as keyExists(
    int|string $key,
    array $array
): bool;
```

:::tip
DO: Always specify full type annotations on every parameter and the return type. Tyhpdef functions with incomplete types will cause the compiler to treat missing types as mixed, reducing type safety.
:::

:::tip
DO: Use generic type parameters when a function's return type depends on its input types. This lets the compiler track types through function calls precisely.
:::

:::danger
DON'T: Include a function body in a tyhpdef declaration. Tyhpdef is declaration-only — every function signature must end with a semicolon, not a curly-brace block.
:::

:::danger
DON'T: Declare a variadic parameter anywhere other than the last position. The compiler will reject signatures where variadic parameters precede regular ones.
:::

## Callable Parameter Types

When a function accepts a callable parameter, you can specify the callable's parameter and return types using the return-last generic convention. The last generic argument is always the return type, and all preceding arguments are the parameter types.

```tyhp
<?tyhpdef

// callable<ReturnType> -- zero params, returns ReturnType
function \registerShutdown(callable<void> $callback): void;

// callable<ParamType, ReturnType> -- one param, returns ReturnType
function \array_walk<T>(
    array<T> &$array,
    callable<T, void> $callback
): true;

// callable<Param1, Param2, ReturnType> -- two params
function \usort<T>(
    array<T> &$array,
    callable<T, T, int> $callback
): true;
```

## Extension Function Declarations

Functions can be declared as extension functions using the extends keyword on the first parameter. Extension functions are called with instance method syntax on the extended type.

```tyhp
<?tyhpdef

function toCamelCase(extends string $str): string;

function toSnakeCase(extends string $str): string;

function toSlug(extends string $str, string $separator = "-"): string;
```

## Summary

- All parameters must be fully typed — no untyped parameters are allowed
- Return types must always be specified
- Function declarations end with a semicolon (no function body)
- Reference parameters use `&` just like in PHP
- Variadic parameters use `...` and must be the last parameter
- Generic type parameters with constraints are supported via angle bracket syntax
- Async functions use the `async` keyword and represent the resolved return type
- Functions can be aliased using `as` to expose them under a different name in Tyhp
- An imported function can be marked as `deprecated` or `obsolete`
- Callable parameters use return-last generic convention: `callable<Params..., ReturnType>`
