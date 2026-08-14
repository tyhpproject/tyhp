---
title: 'The Mixed Type'
status:
  tier: 0
  story: '08'
  state: complete
---

The mixed type is the top type in Tyhp's type system. It represents any value — int, string, object, array, null, or any other type. While PHP allows mixed to be used freely, Tyhp adds compile-time safeguards: you must narrow a mixed value through a type guard before performing type-specific operations on it. This prevents runtime type errors that would otherwise go undetected.

## What Mixed Means

In Tyhp's type system, mixed is the supertype of everything. Any value is assignable to mixed, and mixed is the default type used when a generic type parameter has no constraint and its type argument is erased.

- mixed accepts any value — int, string, float, bool, array, object, callable, null, and all user-defined types.
- mixed|T is simplified to mixed (mixed absorbs all union members).
- mixed&T is simplified to T (mixed is the identity for intersection).
- A variable typed as mixed can hold any value but cannot be used in type-specific operations without narrowing.

```tyhp
<?tyhp

// mixed accepts any value
mixed $value = 42;
$value = 'hello';
$value = [1, 2, 3];
$value = null;
$value = new SomeObject();
```

## Requiring Type Guards Before Use

Tyhp enforces that you narrow a mixed value to a specific type before performing type-specific operations. This is done through type guards: instanceof/is checks (scalars emit `\Tyhp\Type::is(...)`), type-checking functions (is_string, is_int, etc.), null checks (`$item === null` / `$item !== null`, not `$item is null`), and user-defined type guard functions.

```tyhp
<?tyhp

function processValue(mixed $value): string {
    // ERROR — cannot call string methods on mixed
    // return \strtoupper($value);

    // ERROR — cannot perform arithmetic on mixed
    // return $value + 1;

    // Correct — narrow first, then use
    if ($value is string) {
        return $value;  // $value is automatically narrowed to string
    }

    if (\is_int($value)) {
        return (string)$value;  // $value is narrowed to int
    }

    return '';
}
```

## Automatic Smart Casts

After a type guard narrows the type, the compiler automatically tracks the narrower type within the guard's scope. No explicit casts or re-declarations are needed. This works with instanceof/is checks, null checks, built-in type-checking functions, and user-defined type guard functions.

```tyhp
<?tyhp

function describe(mixed $item): string {
    if ($item === null) {
        return 'null';
    }
    // After the null check, $item is non-null mixed

    if ($item instanceof User) {
        // $item is automatically User here
        return 'User: ' . $item->name;
    }

    if (\is_array($item)) {
        // $item is automatically array here
        return 'Array with ' . \count($item) . ' elements';
    }

    if (\is_string($item)) {
        // $item is automatically string here
        return 'String: ' . \strtoupper($item);
    }

    return 'Unknown type';
}
```

:::note
Negative narrowing also works: in the else-branch of a type guard, the type is narrowed to exclude the checked type. Multiple checks in sequence further narrow the type.
:::

## When Mixed Is Acceptable

There are legitimate use cases for mixed:

- Deserialization functions that return data of unknown shape (e.g., JSON decoding).
- Generic container implementations where the type parameter has no constraint.
- Wrapper functions that accept any value and pass it through (e.g., logging, caching).
- Interop with untyped PHP libraries where the return type is genuinely unknown.
- Event systems or message buses where the payload type varies.

```tyhp
<?tyhp

// Acceptable — JSON decoding returns unknown structure
function decodePayload(string $json): mixed {
    return \json_decode($json, true);
}

// Acceptable — logging wrapper that accepts any value
function logValue(string $label, mixed $value): void {
    echo $label . ': ' . \var_export($value, true);
}
```

## Compiler Errors with Mixed

The compiler produces errors when mixed values are used in type-specific operations without narrowing.

```tyhp
<?tyhp

function badExample(mixed $value): void {
    // ERROR — cannot call methods on mixed without narrowing
    // $value->doSomething();

    // ERROR — cannot perform arithmetic on mixed
    // int $result = $value + 1;

    // ERROR — cannot access properties on mixed
    // string $name = $value->name;

    // ERROR — cannot index mixed as array
    // $item = $value[0];
}
```

## There Is No Escape Hatch, By Design

Narrowing is not optional and there is no compiler setting that relaxes it. Tyhp has exactly one top type, and it is always strict: a mixed value must be narrowed to a specific type before you can do anything type-specific with it. There is no second, permissive top type to fall back on.

If you know TypeScript, mixed behaves like TypeScript's unknown, and Tyhp deliberately has no equivalent of TypeScript's any. TypeScript ships both only for historical reasons — any predates unknown and could not be made strict without breaking existing code, so unknown was added alongside it as the safe alternative. Tyhp starts from the strict semantics instead, so the unsafe variant never needs to exist.

:::note
A project-wide toggle would also break down across package boundaries: a library compiled with relaxed rules and consumed by a strict project would leave the meaning of its published signatures ambiguous. Keeping one unconditional rule keeps a type's meaning identical in every project that consumes it.
:::

When narrowing feels impossible, that is a signal the type is wrong rather than a reason for an escape hatch. Prefer a union type when you know the accepted types, generics when the type varies with a caller, or a user-defined type guard when the check is genuinely a runtime one.

## Mixed in Generic Erasure

When generics are erased for PHP output, unconstrained type parameters become mixed. Constrained type parameters use their constraint type instead. This means generic code in PHP output uses mixed where the original Tyhp code was type-safe through generics.

```tyhp
<?tyhp

// Unconstrained — T becomes mixed in PHP output
function identity<T>(T $value): T {
    return $value;
}

// Constrained — T becomes Countable in PHP output
function countItems<T extends Countable>(T $items): int {
    return \count($items);
}
```

```php
<?php
// PHP output — unconstrained generics become mixed,
// constrained generics use the constraint type
declare(strict_types=1);

function identity(mixed $value): mixed {
    return $value;
}

function countItems(Countable $items): int {
    return \count($items);
}
```

## Prefer Generics or Union Types Over Mixed

When you know the range of types a value can hold, use generics or union types instead of mixed. Generics preserve type information across calls, and union types make the accepted types explicit.

```tyhp
<?tyhp

// Bad — using mixed when the types are known
function formatValue(mixed $value): string {
    // Must narrow before using
    if (\is_int($value)) { return (string)$value; }
    if (\is_float($value)) { return \number_format($value, 2); }
    return '';
}

// Better — use a union type
function formatValue(int|float $value): string {
    if (\is_int($value)) { return (string)$value; }
    return \number_format($value, 2);
}

// Better — use generics for type preservation
function wrap<T>(T $value): array<T> {
    return [$value];
}
```

## Best Practices

:::tip
Use mixed only when the type is genuinely unknown at compile time — JSON data, untyped library interop, or truly polymorphic APIs.
:::

:::tip
Always narrow mixed values with type guards (is, instanceof, is_string(), etc.) before performing type-specific operations. The compiler automatically tracks the narrowed type.
:::

:::tip
Prefer specific types, union types, or generics over mixed whenever the accepted types are known. This provides better type safety and IDE support.
:::

:::tip
Use generics instead of mixed for function parameters and return types when the type should be preserved through the call — generics maintain type information that mixed discards.
:::

## Common Mistakes

:::danger
Don't use mixed as a lazy alternative to union types — if a value can be int or string, use int|string, not mixed.
:::

:::danger
Don't perform operations on mixed values without narrowing — calling methods, accessing properties, or performing arithmetic on mixed produces a compile error.
:::

:::danger
Don't use mixed as a function parameter type when generics would be more appropriate — generics preserve type information through calls, while mixed discards it.
:::

:::danger
Don't use mixed for return types when the return type is deterministic — the caller loses all type information and must narrow the result.
:::
