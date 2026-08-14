---
title: 'Short Function Syntax'
status:
  tier: 1
  story: '11'
  state: complete
---

Tyhp introduces a short syntax for named functions and methods using the fn keyword with a => expression body. This is syntactic sugar that compiles to a standard function declaration with an explicit return statement. It is ideal for simple, single-expression functions and methods.

:::warning
This is NOT the same as PHP's arrow functions. PHP's fn($x) => $x + 1 creates an anonymous closure. Tyhp's fn myFunc(int $x): int => $x + 1; creates a named function or method.
:::

## Basic Syntax

A short function replaces the function keyword with fn and replaces the curly-brace body with => expression;. The expression is implicitly returned.

```tyhp
<?tyhp

// Short function syntax
fn double(int $n): int => $n * 2;

// Equivalent full syntax
function double(int $n): int {
    return $n * 2;
}

// Usage
int $result = double(5);  // $result is 10
```

## Compiled PHP Output

The fn keyword compiles to a regular function declaration. The expression body is wrapped in a return statement.

```tyhp
<?tyhp

fn double(int $n): int => $n * 2;
fn greet(string $name): string => "Hello, {$name}!";
```

```php
<?php
// PHP output — fn compiles to standard function with return
declare(strict_types=1);

function double(int $n): int
{
    return $n * 2;
}

function greet(string $name): string
{
    return "Hello, {$name}!";
}
```

## Short Methods in Classes

The fn keyword can also be used for class methods. All standard method modifiers (public, protected, private, static, final) are supported.

```tyhp
<?tyhp

class Calculator {
    private int $value;

    public fn getValue(): int => $this->value;

    public fn add(int $a, int $b): int => $a + $b;

    public static fn pi(): float => 3.14159265358979;

    protected fn doubleValue(): int => $this->value * 2;

    final public fn isPositive(): bool => $this->value > 0;
}
```

```php
<?php
// PHP output — short methods compile to standard methods
declare(strict_types=1);

class Calculator {
    private int $value;

    public function getValue(): int
    {
        return $this->value;
    }

    public function add(int $a, int $b): int
    {
        return $a + $b;
    }

    public static function pi(): float
    {
        return 3.14159265358979;
    }

    protected function doubleValue(): int
    {
        return $this->value * 2;
    }

    final public function isPositive(): bool
    {
        return $this->value > 0;
    }
}
```

## Generic Short Functions

Short functions support generic type parameters, just like regular function declarations. Generic parameters are erased in PHP output.

```tyhp
<?tyhp

// Generic short function
fn identity<T>(T $value): T => $value;

// Generic short method in a class
class Container<T> {
    private T $item;

    public fn getItem(): T => $this->item;

    public fn map<U>(callable<T, U> $fn): U => $fn($this->item);
}
```

```php
<?php
// PHP output — generics erased, fn expanded
declare(strict_types=1);

function identity(mixed $value): mixed
{
    return $value;
}

class Container {
    private mixed $item;

    public function getItem(): mixed
    {
        return $this->item;
    }

    public function map(callable $fn): mixed
    {
        return $fn($this->item);
    }
}
```

## Async Short Functions

Short functions can be marked as async, just like regular function declarations. The async modifier works with all other modifiers.

```tyhp
<?tyhp

async fn fetchData(string $url): string => await $httpClient->get($url);

class ApiClient {
    async public fn getUser(int $id): User => await $this->fetch("/users/{$id}");
}
```

```php
<?php
// PHP output — async fn compiles to function returning Promise
declare(strict_types=1);

function fetchData(string $url): \Tyhp\Promise
{
    return \Tyhp\Promise::_async(function() use ($url) {
        return \Tyhp\Promise::_await($httpClient->get($url));
    });
}

class ApiClient {
    public function getUser(int $id): \Tyhp\Promise
    {
        return \Tyhp\Promise::_async(function() use ($id) {
            return \Tyhp\Promise::_await($this->fetch("/users/{$id}"));
        });
    }
}
```

## Short Function Overload Signatures

A short function can serve as the implementation body for function overload signatures. The overload signatures declare different parameter/return types, and the short function provides the single implementation.

```tyhp
<?tyhp

// Overload signatures followed by short function implementation
function double(int $value): int;
function double(float $value): float;
fn double(int|float $value): int|float => $value * 2;
```

```php
<?php
// PHP output — overload signatures erased, implementation expanded
declare(strict_types=1);

function double(int|float $value): int|float
{
    return $value * 2;
}
```

## Compiler Errors

Short functions must have an expression body with =>. Using a block body or omitting the expression produces a compile error.

```tyhp
<?tyhp

// ERROR — cannot use block body with fn syntax
// fn myFunc(int $val): int {
//     return $val + 5;
// }

// ERROR — short function without => expression
// fn myFunc(int $val): int;  // This is an overload signature, not a short function

// CORRECT — use => with a single expression
fn myFunc(int $val): int => $val + 5;
```

## Difference from PHP Arrow Functions

PHP's fn keyword creates anonymous arrow functions (closures). Tyhp's fn keyword creates named functions and methods. These are completely different constructs. PHP's anonymous arrow functions remain valid in Tyhp and are not affected by this feature.

```tyhp
<?tyhp

// Tyhp named short function — creates a named function/method
fn square(int $n): int => $n * $n;

// PHP anonymous arrow function — creates an anonymous closure
$squareClosure = fn(int $n): int => $n * $n;

// Both are valid and coexist in Tyhp
int $a = square(5);              // Calls the named function
int $b = $squareClosure(5);      // Calls the anonymous closure
```

:::note
Short functions are pure syntactic sugar. They produce identical PHP output to the equivalent function declaration with a return statement. There is no semantic or runtime difference.
:::

## Best Practices

:::tip
Use fn for simple, single-expression functions — getters, simple calculations, and one-liner transformations are ideal candidates.
:::

:::tip
Use fn for getter methods in classes — public fn getName(): string => $this->name; is concise and readable.
:::

:::tip
Use fn with all standard modifiers — public, protected, private, static, final, and async all work with the short syntax.
:::

:::tip
Use fn with generics for concise, type-safe utility functions — fn identity<T>(T $value): T => $value; is clean and readable.
:::

## Common Mistakes

:::danger
Don't confuse Tyhp's fn (named function shorthand) with PHP's fn (anonymous arrow functions). They look similar but are fundamentally different constructs.
:::

:::danger
Don't use fn for complex multi-statement logic — if the body needs more than a single expression, use a regular function declaration with curly braces.
:::

:::danger
Don't use a block body { } with the fn keyword — fn myFunc(): int { return 5; } is a syntax error. Use => expression; instead.
:::

:::danger
Don't forget the semicolon at the end of a short function declaration — fn myFunc(): int => 5; requires the trailing semicolon after the expression.
:::
