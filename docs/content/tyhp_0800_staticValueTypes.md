---
title: 'Static Value Types'
status:
  tier: 0
  story: '08'
  state: complete
---

Tyhp supports static value types (also called literal types) — types that represent a single specific value rather than a range of values. A static value type is the type of a particular literal: the integer 42, the string 'hello', or the boolean true. Static value types enable precise type narrowing, exhaustive pattern matching, and more expressive function signatures.

## Supported Literal Types

Tyhp supports literal types for the following value categories:

- Boolean literals: true, false
- Integer literals: 0, 1, 42, -1, 0xFF, 0b1010, 0o17
- Float literals: 0.0, 3.14, 1.2e3
- String literals: 'hello', 'error', 'active'
- The null literal: null

Each literal type is a subtype of its underlying scalar type. The literal type true is a subtype of bool, the literal type 42 is a subtype of int, and so on. This means a value of literal type 42 can be used wherever int is expected, but not vice versa.

## Using Literal Types in Type Annotations

Literal types can appear in type annotations, union types, and function parameter/return types. They restrict the accepted values to the specific literal.

```tyhp
<?tyhp

// A function that only accepts specific string values
function setStatus('active'|'inactive'|'pending' $status): void {
    // $status can only be one of these three strings
}

setStatus('active');    // OK
setStatus('inactive');  // OK
// setStatus('deleted');  // Compile error — 'deleted' is not in the union

// Boolean literal types
function setEnabled(true $flag): void {
    // Only accepts the literal value true
}

// Integer literal types
function getExitCode(): 0|1|2 {
    return 0;
}
```

## Literal Types in Union Types

Literal types are most powerful when used in union types. A union of literal types creates an enum-like constraint on the accepted values without needing a formal enum declaration.

```tyhp
<?tyhp

// String literal union — like a lightweight enum
type HttpMethod = 'GET'|'POST'|'PUT'|'PATCH'|'DELETE';

function sendRequest(HttpMethod $method, string $url): Response {
    // $method is guaranteed to be one of the five values
}

// Integer literal union
type LogLevel = 0|1|2|3|4;

function log(LogLevel $level, string $message): void {
    // ...
}

// Mixed literal types in a union
type Result = true|'error'|0;
```

## true and false as Types

The boolean literals true and false are each their own type. PHP 8.0+ supports true and false as standalone types. In Tyhp's type system, true and false are subtypes of bool, and the union true|false is equivalent to bool.

```tyhp
<?tyhp

// A function that always returns true
function isValid(): true {
    return true;
}

// A function that returns false on failure
function tryParse(string $input): int|false {
    int|false $result = \filter_var($input, FILTER_VALIDATE_INT);
    return $result;
}

// true|false is equivalent to bool
type StrictBool = true|false;  // Same as bool
```

## Literal Types in Function Overloads

Literal types enable function overloads where different literal argument values produce different return types. The compiler uses the literal type to select the correct overload signature.

```tyhp
<?tyhp

// Overloaded function signatures with literal types
function getConfig('database' $key): DatabaseConfig;
function getConfig('cache' $key): CacheConfig;
function getConfig('mail' $key): MailConfig;
function getConfig(string $key): mixed {
    // Implementation handles all cases
    return match($key) {
        'database' => new DatabaseConfig(),
        'cache' => new CacheConfig(),
        'mail' => new MailConfig(),
        default => throw new \InvalidArgumentException('Unknown config key'),
    };
}

// The compiler knows the return type based on the literal argument
DatabaseConfig $db = getConfig('database');  // Returns DatabaseConfig
CacheConfig $cache = getConfig('cache');      // Returns CacheConfig
```

## Compiled PHP Output

Literal types are a compile-time feature. In the PHP output, literal types are widened to their underlying scalar type because PHP does not support arbitrary literal types in type hints. The exceptions are true, false, and null, which PHP 8.0+ supports as standalone types.

```tyhp
<?tyhp

function getStatus(): 'active'|'inactive' {
    return 'active';
}

function getCode(): 0|1|2 {
    return 0;
}
```

```php
<?php
// PHP output — literal types are widened to scalar types
declare(strict_types=1);

function getStatus(): string {
    return 'active';
}

function getCode(): int {
    return 0;
}
```

The true, false, and null literal types are preserved in PHP output because PHP 8.0+ supports them natively.

```tyhp
<?tyhp

function alwaysTrue(): true {
    return true;
}

function mayFail(): string|false {
    return false;
}
```

```php
<?php
// PHP output — true, false, null types are preserved
declare(strict_types=1);

function alwaysTrue(): true {
    return true;
}

function mayFail(): string|false {
    return false;
}
```

## Type Narrowing with Literal Types

The checker automatically narrows union types containing literal values when control flow eliminates possibilities. After checking a specific literal value, the type is narrowed accordingly.

```tyhp
<?tyhp

function handle('success'|'error'|'pending' $status): void {
    if ($status === 'success') {
        // $status is narrowed to 'success' here
        return;
    }

    if ($status === 'error') {
        // $status is narrowed to 'error' here
        throw new \RuntimeException('Failed');
    }

    // $status is narrowed to 'pending' here
    echo 'Still pending...';
}
```

## Combining with Type Aliases

Static value types combine well with type aliases to create meaningful, named value sets.

```tyhp
<?tyhp

type Direction = 'north'|'south'|'east'|'west';
type HttpOk = 200;
type Digit = 0|1|2|3|4|5|6|7|8|9;

function move(Direction $dir, int $steps): void {
    // $dir is guaranteed to be one of the four directions
}

function isSuccessCode(HttpOk $code): true {
    return true;
}
```

## Error: Assigning Outside the Static Value Set

The compiler reports an error when a value that is not part of the static value type set is assigned.

```tyhp
<?tyhp

type Status = 'active'|'inactive';

Status $s = 'active';    // OK
// Status $s = 'deleted';   // ERROR — 'deleted' is not in Status
// Status $s = 42;          // ERROR — int is not assignable to 'active'|'inactive'
```

## Interaction with match Expressions

Static value types work with match expressions. The compiler can verify exhaustiveness — that all possible values are handled.

```tyhp
<?tyhp

type Color = 'red'|'green'|'blue';

function toHex(Color $color): string {
    return match($color) {
        'red' => '#FF0000',
        'green' => '#00FF00',
        'blue' => '#0000FF',
        // No default needed — all cases are covered
    };
}
```

## Best Practices

:::tip
Use string literal unions as lightweight alternatives to enums when the values are simple strings and you don't need enum methods or interfaces.
:::

:::tip
Use literal types in function overload signatures to provide precise return types based on literal arguments.
:::

:::tip
Use true and false types when a function genuinely only returns one boolean value — this communicates intent more clearly than bool.
:::

:::tip
Combine static value types with type aliases to create meaningful, named value sets like type Direction = 'north'|'south'|'east'|'west'.
:::

:::tip
Take advantage of automatic type narrowing — check literal values in conditionals and the compiler narrows the type for you within the branch scope.
:::

## Common Mistakes

:::danger
Don't use literal types when a backed enum would be more appropriate — enums provide methods, interfaces, and exhaustiveness checking, which literal unions do not.
:::

:::danger
Don't rely on literal type constraints at runtime — most literal types (except true, false, null) are widened to their scalar type in PHP output.
:::

:::danger
Don't create excessively large literal unions — if the set of values is large or dynamic, use an enum or a validated string type instead.
:::

:::danger
Don't confuse literal types with constants — a constant has a specific value, but its type in PHP is typically the scalar type (int, string). Literal types are a Tyhp compile-time concept.
:::
