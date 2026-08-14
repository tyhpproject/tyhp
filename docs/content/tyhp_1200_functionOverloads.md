---
title: 'Function and Method Overloads'
status:
  tier: 1
  story: '11'
  state: complete
---

Tyhp supports function and method overloading, allowing you to declare multiple signatures for the same function or method with different parameter types and specialized return types. Overload signatures are compile-time declarations that describe how the function behaves for specific input types. The final declaration must include a body (the implementation) that covers all overloaded variants. At compile time, the compiler selects the most specific overload based on argument types. In the PHP output, all overload signatures are stripped and only the single implementation remains — overloading does NOT create multiple PHP functions.

## Basic Function Overloads

Overload signatures are declarations that end with a semicolon (no body). The final declaration of the function must have a body that handles all possible parameter combinations. The compiler uses the overload signatures to determine the precise return type at each call site.

```tyhp
<?tyhp

// Overload: when $convertToInt is true, returns int
function convertNumber(string|int|float $value, true $convertToInt): int;

// Overload: when $convertToInt is false, returns float
function convertNumber(string|int|float $value, false $convertToInt): float;

// Implementation: covers all scenarios
function convertNumber(string|int|float $value, bool $convertToInt = false): int|float
{
    return $convertToInt ? \intval($value) : \floatval($value);
}

// The compiler knows the precise return type at each call site:
int $asInt = convertNumber("42", true);      // Compiler knows: returns int
float $asFloat = convertNumber("42", false);  // Compiler knows: returns float
int|float $maybe = convertNumber("42");        // Uses default: returns float
```

## Compiled PHP Output

Overload signatures are completely stripped from the PHP output. Only the final implementation with the body is emitted. This means overloading is purely a compile-time feature with zero runtime cost.

```php
<?php

function convertNumber(string|int|float $value, bool $convertToInt = false): int|float
{
    return $convertToInt ? \intval($value) : \floatval($value);
}
```

## Static Value Type Overloads

Overloads are especially powerful when combined with static value types. A static value type constrains a parameter to a specific literal value (true, false, 0, 1, a specific string, etc.). This allows the compiler to narrow the return type based on the exact value passed.

```tyhp
<?tyhp

function parseValue(string $input, 'json' $format): array;
function parseValue(string $input, 'csv' $format): array<string>;
function parseValue(string $input, 'raw' $format): string;
function parseValue(string $input, string $format): array|string
{
    return match($format) {
        'json' => \json_decode($input, true),
        'csv' => $input->getcsv(),
        default => $input,
    };
}

// Compiler knows exact return types:
array $json = parseValue($data, 'json');
string $raw = parseValue($data, 'raw');
```

```php
<?php

function parseValue(string $input, string $format): array|string
{
    return match($format) {
        'json' => \json_decode($input, true),
        'csv' => \str_getcsv($input),
        default => $input,
    };
}
```

## Method Overloads

Method overloads work identically to function overloads but are declared inside a class. All overload signatures for a method must have the same modifiers (visibility, static, final, etc.).

```tyhp
<?tyhp

class Repository
{
    // Overload: find by ID returns exact type
    public function find(int $id): User;

    // Overload: find by email might not exist
    public function find(string $email): ?User;

    // Implementation with body
    public function find(int|string $criteria): ?User
    {
        if (\is_int($criteria)) {
            return $this->findById($criteria);
        }
        return $this->findByEmail($criteria);
    }
}
```

```php
<?php

class Repository
{
    public function find(int|string $criteria): ?User
    {
        if (\is_int($criteria)) {
            return $this->findById($criteria);
        }
        return $this->findByEmail($criteria);
    }
}
```

## Async Method Overloads

Async methods can also have overloads. All overload signatures and the implementation must be marked with async. The compiler ensures type-safe resolution for both sync and async overloads.

```tyhp
<?tyhp

class ApiClient
{
    async public function fetch(int $id): Response;
    async public function fetch(string $url): Response;
    async public function fetch(int|string $target): Response
    {
        $url = \is_int($target) ? "/api/items/{$target}" : $target;
        return await $this->httpClient->get($url);
    }
}
```

```php
<?php

class ApiClient
{
    public function fetch(int|string $target): \Tyhp\Promise
    {
        return \Tyhp\Promise::_async(function () use ($target) {
            $url = \is_int($target) ? "/api/items/{$target}" : $target;
            return \Tyhp\Promise::_await($this->httpClient->get($url));
        });
    }
}
```

## Overloads with Short Function Syntax

The final implementation can use the short fn syntax if the body is a single expression. The overload signatures remain semicolon-terminated as usual.

```tyhp
<?tyhp

function negate(int $value): int;
function negate(float $value): float;
fn negate(int|float $value): int|float => -$value;
```

```php
<?php

function negate(int|float $value): int|float
{
    return -$value;
}
```

## Overload Resolution Rules

The compiler selects the most specific matching overload at compile time using these rules:

1. The compiler examines all overload signatures and the implementation signature for the given function or method.
2. For each call site, it compares the argument types against each overload signature's parameter types.
3. The most specific matching signature is selected — a signature with narrower parameter types beats one with broader types.
4. Static value types (true, false, string literals, integer literals) are the most specific matchers.
5. If no specific overload matches, the implementation's broader types are used as the fallback.
6. If the argument types are ambiguous (multiple overloads match equally), the compiler reports an error.

## Rules

1. The final declaration in the overload set must have a body (block or => expr). All preceding declarations must be signature-only (ending with ;).
2. The implementation's parameter types must be a superset of all overload signatures' parameter types. It must accept every valid call described by the overloads.
3. The implementation's return type must be a superset of all overload signatures' return types.
4. For method overloads, all signatures must share the same modifiers (public, static, final, async, etc.).
5. Static value types (true, false, integer literals, string literals) can be used in overload signatures to narrow types based on specific argument values.
6. Overloads do not create multiple PHP functions — only the implementation body is emitted.

## Best Practices

:::tip
Use overloads for type-safe polymorphic APIs where the return type depends on the input type.
:::

:::tip
Combine overloads with static value types for maximum type narrowing — the compiler can determine exact return types at each call site.
:::

:::tip
Keep overload signatures compatible with the implementation — the implementation must be able to handle every call described by the overloads.
:::

:::tip
Keep method modifiers consistent across all overloads and the implementation.
:::

```tyhp
<?tyhp

// DO: ensure the implementation covers all overloads
function stringify(int $value): string;
function stringify(float $value): string;
function stringify(bool $value): string;
function stringify(int|float|bool $value): string
{
    return (string)$value;
}

// DO: use static value types for precise return types
function getConfig(true $asArray): array;
function getConfig(false $asArray): object;
function getConfig(bool $asArray = false): array|object
{
    $data = \file_get_contents('config.json');
    return \json_decode($data, $asArray);
}

// DO: keep method modifiers consistent across overloads
class Service {
    public static function create(int $id): self;
    public static function create(string $name): self;
    public static function create(int|string $arg): self { /* ... */ }
}
```

## Common Mistakes

:::danger
Do not declare overloads without a final implementation body. Every overload set must end with a declaration that has a body.
:::

:::danger
Do not use different modifiers across overloads. All method overloads must share the same visibility and modifiers.
:::

:::danger
Do not make the implementation narrower than the overloads. The implementation must accept all argument combinations described by the overload signatures.
:::

:::danger
Do not create overloads with incompatible parameter counts unless the extra parameters have default values in the implementation.
:::

```tyhp
<?tyhp

// ERROR: missing implementation with body
// function doSomething(int $a): void;
// function doSomething(string $a): void;

// ERROR: modifier mismatch across overloads
// class BadService {
//     public function process(int $id): void;
//     private function process(string $name): void;
//     public function process(int|string $arg): void { /* ... */ }
// }

// ERROR: implementation doesn't cover string overload
// function narrow(int $a): int;
// function narrow(string $a): string;
// function narrow(int $a): int { return $a; }
```

## Compiler Errors

- Missing implementation body: All overload sets must end with a declaration that has a body.
- Modifier mismatch: All method overloads must share the same visibility and modifiers.
- Incomplete coverage: The implementation's parameter types must accept all argument combinations from the overloads.
- Return type mismatch: The implementation's return type must be a superset of all overload return types.
- Incompatible parameter count: If overloads have different parameter counts, the implementation must have defaults for the extra parameters.
