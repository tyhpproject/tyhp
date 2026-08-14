---
title: 'Strongly Typed'
status:
  tier: 0
  story: '08'
  state: complete
---

The primary feature of Tyhp is making PHP strongly typed. While PHP allows optional type hints in some places, Tyhp enforces explicit typing throughout your code. Every variable, parameter, property, and return type must have an explicit type declaration or be inferable from context. In Tyhp, we do not refer to these as "type hints" since they are not hints -- they are enforced type declarations. All types are also non-nullable by default: `string $x` means `$x` can only hold a `string`, never `null`. To allow null, you must explicitly declare the type as nullable: `?string $x` or `string|null $x`.

## Where Types Are Required

The places in PHP that already support optional type hints are required to be typed in Tyhp:

- Class, interface, and trait properties
- Method and function parameters
- Method and function return types
- Class and object constants (typed constants)

Additionally, Tyhp requires types for:

- Variables -- either an explicit type declaration or type inference on first assignment
- Closure / arrow function parameters and return types
- Catch block exception types

## Variable Type Declarations

There are two ways to type a variable in Tyhp: explicit type declaration and type inference.

## 1. Explicit Type Declaration

You can declare a variable's type by placing the type before the variable name. This is similar to typed properties in PHP classes, but extended to all variables in any scope.

```tyhp
<?tyhp

// Declare a typed variable without assigning a value
// The variable exists but is unset -- using it before assignment is an error
bool $myBool;

// Assign a value to the typed variable
$myBool = \isDatabaseServerUp();

// Declare and assign in one statement
string $myStr = "hello world";

// Union types are supported
string|int $myKey = $myBool ? 1 : $myStr;

// Since $myKey is a union type, either branch is valid
$myKey = "foo";
$myKey = 35;

// Nullable types must be explicit
?string $maybeName = null;
string|null $alsoMaybeName = null;
```

## Parenthesized Type Declarations

Tyhp also supports parenthesized type declarations, where the type is wrapped in parentheses before the variable name. This is equivalent to the standard form and is purely a stylistic choice. It can help visually distinguish complex union or intersection types from the variable name.

```tyhp
<?tyhp

// Parenthesized type declaration
(string) $myStr = "hello";
(int|float) $number = 3.14;
(decimal) $price = 19.99d;

// Equivalent to:
string $myStr2 = "hello";
int|float $number2 = 3.14;
decimal $price2 = 19.99d;
```

## 2. Type Inference from First Assignment

When you assign a value to a variable without an explicit type declaration, Tyhp automatically infers the type from the right-hand side expression. The inferred type is locked in at the first assignment and cannot change. The inference uses the narrowest possible type based on the expression. No `var` or `auto` keyword is needed -- the absence of a type annotation combined with an assignment is sufficient.

```tyhp
<?tyhp

// Type is inferred from the assigned value
$myStr = "asdf";           // Inferred as `string`
$myBool = \canIMoveThis(); // Inferred from the function's return type
$price = 19.99;            // Inferred as `float`
$discount = 1.89d;         // Inferred as `decimal`
$count = 42;              // Inferred as `int`
$items = [1, 2, 3];       // Inferred as `array<int>`
```

:::note
Type inference only works on the first assignment. If a variable has no type annotation and no initializer, the compiler reports an error requiring either an explicit type or an initial value.
:::

## Non-Nullable by Default

All types in Tyhp are non-nullable by default. A variable declared as `string $x` cannot hold `null` -- it must be explicitly declared as `?string $x` (or `string|null $x`) to allow null. This is a fundamental difference from PHP, where any typed variable can silently receive `null` at runtime.

```tyhp
<?tyhp

string $name = "Alice";
// $name = null;  // ERROR: Cannot assign null to non-nullable string

// To allow null, declare as nullable
?string $maybeName = null;  // OK
string|null $alsoNullable = null;  // Also OK
```

## Type Immutability

Once a variable is typed -- whether explicitly declared or inferred -- its type is immutable. You cannot assign a value of an incompatible type, and you cannot re-declare a variable with a different type in the same scope.

```tyhp
<?tyhp

string $myStr = "hello";

// ERROR: Cannot assign int to variable of type string
// $myStr = 42;

// ERROR: Cannot re-declare type for an already typed variable
// int $myStr = 42;
```

## The `unset` + Re-declare Pattern

If you need to change a variable's type, you must first `unset` it and then re-declare it. The `unset` call removes the variable from the current scope, allowing a fresh declaration.

```tyhp
<?tyhp

string $myStr = "hello";

// Remove the variable from scope
unset($myStr);

// Now re-declare with a different type
int $myStr = 42;  // OK: previous declaration was unset
```

:::warning
The `unset` call must be in the same scope where the variable was declared. You cannot `unset` a variable from a parent scope.
:::

## Array and Generic Types

Tyhp extends PHP's `array` type with generic parameters for type-safe collections. See the New Types page for the full list of generic types.

```tyhp
<?tyhp

// Typed arrays using generics
array<string> $names = ["Alice", "Bob"];
array<string, int> $ages = ["Alice" => 30, "Bob" => 25];

// The generic type constrains what can be added
$names[] = "Charlie";  // OK: string
// $names[] = 42;      // ERROR: int is not assignable to array<string>
```

## Compiled PHP Output

Tyhp's variable type declarations are a compile-time concept. In the compiled PHP output, variable type annotations are stripped because PHP does not support typed local variables. However, function/method parameter types, return types, and property types are preserved in the PHP output since PHP supports those natively. Generic type parameters are also erased.

```tyhp
<?tyhp

string $name = "Alice";
int $age = 30;
array<string> $hobbies = ["reading", "coding"];

function greet(string $name): string {
    string $greeting = "Hello, " . $name;
    return $greeting;
}
```

Compiles to:

```php
<?php
declare(strict_types=1);

$name = "Alice";
$age = 30;
$hobbies = ["reading", "coding"];

function greet(string $name): string {
    $greeting = "Hello, " . $name;
    return $greeting;
}
```

Note how `string $name` and `int $age` at the local variable level are stripped to just `$name` and `$age`, while the function parameter `string $name` and return type `: string` are preserved because PHP supports those natively. The generic `array<string>` becomes just `array` in the output.

## Best Practices

:::tip
Always declare types for function/method parameters and return types. The compiler enforces this, but doing so proactively makes your code self-documenting.
:::

:::tip
Use explicit type declarations for variables when the type might not be obvious from context. Let inference handle cases where the type is immediately clear from the assigned value.
:::

:::tip
Use union types (`string|int`) when a variable legitimately needs to hold multiple types. Prefer specific types or union types over `mixed`.
:::

:::tip
Use generic types (`array<string>`) for collections to get element-level type safety. This prevents accidentally inserting wrong types into arrays.
:::

:::tip
Use nullable types (`?string` or `string|null`) explicitly when a variable may be null. Non-nullable by default catches many null-related bugs at compile time.
:::

## Common Mistakes

:::danger
Don't assign a value of an incompatible type to a typed variable. Once a variable's type is set (explicitly or by inference), it cannot hold values of different types.
:::

:::danger
Don't try to re-declare a variable's type without unsetting it first. Duplicate type declarations in the same scope produce a `BinderDuplicateSymbolDeclaration` error.
:::

:::danger
Don't use `mixed` as a type unless absolutely necessary. Tyhp's strong type system should eliminate most needs for `mixed` -- prefer specific types or union types.
:::

:::danger
Don't omit types on function parameters or return types. The compiler requires all function signatures to be fully typed.
:::

:::danger
Don't assume a variable is nullable. All types are non-nullable by default in Tyhp. Assigning `null` to a non-nullable variable produces a `CheckerTypeMismatch` error.
:::

## Compiler Error Examples

```tyhp
<?tyhp

// ERROR: CheckerTypeMismatch -- Cannot assign int to string
string $x = "hello";
// $x = 42;

// ERROR: CheckerTypeMismatch -- Cannot assign null to non-nullable string
string $name = "Alice";
// $name = null;

// ERROR: BinderDuplicateSymbolDeclaration -- Variable already declared in this scope
int $count = 1;
// int $count = 2;

// ERROR: CheckerVariableTypeRequired -- Variable has no type and no initializer
// $mystery;
```
