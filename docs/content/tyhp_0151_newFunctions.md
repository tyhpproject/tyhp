---
title: 'New and Changed Functions and Methods in Tyhp'
status:
  tier: 0
  story: '04'
  state: complete
---

Tyhp introduces new functions, changes the behavior of some existing PHP functions, and gives many built-in functions generic type signatures for better type safety. This page covers guard functions, generic built-in function signatures, compile-time functions, and user-defined extension methods.

## Guard / Type Narrowing Functions

Many existing PHP functions act as type guards in Tyhp. When used in conditional checks (`if`, `while`, ternary, `match`), they narrow the type of a variable within the guarded scope. See the Type Narrowing and Guards page for full details on how narrowing works.

## Scalar Type Guards

These PHP type-checking functions narrow a variable to the corresponding scalar type when used in a conditional:

- `\is_string($val)` -- narrows `$val` to `string`
- `\is_int($val)` / `\is_integer($val)` / `\is_long($val)` -- narrows `$val` to `int`
- `\is_float($val)` / `\is_double($val)` -- narrows `$val` to `float`
- `\is_bool($val)` -- narrows `$val` to `bool`
- `\is_array($val)` -- narrows `$val` to `array`
- `\is_object($val)` -- narrows `$val` to `object`
- `\is_null($val)` -- narrows `$val` to `null`
- `\is_numeric($val)` -- narrows `$val` to `int|float|string` (strings that are numeric)
- `\is_callable($val)` -- narrows `$val` to `callable`
- `\is_resource($val)` -- narrows `$val` to `resource`

```tyhp
<?tyhp

function format(string|int|float $value): string {
    if (\is_string($value)) {
        // $value is narrowed to `string` here
        return \strtoupper($value);
    } elseif (\is_int($value)) {
        // $value is narrowed to `int` here
        return \number_format($value, 0);
    } else {
        // $value is narrowed to `float` here (remaining union member)
        return \number_format($value, 2);
    }
}
```

## Dynamic Guard Functions

These PHP reflection/existence functions act as type guards that narrow string values to Tyhp's symbol name types:

- `\function_exists($name)` -- narrows `$name` to `__FunctionName`
- `\class_exists($name)` -- narrows `$name` to `__ClassName<object>` (parametric: `\class_exists<T>`)
- `\interface_exists($name)` -- narrows `$name` to `__InterfaceName`
- `\trait_exists($name)` -- narrows `$name` to `__TraitName`
- `\enum_exists($name)` -- narrows `$name` to `__EnumName`
- `\property_exists($obj, $name)` -- narrows `$name` to `__PropertyName<typeof($obj)>`
- `\method_exists($obj, $name)` -- narrows `$name` to `__MethodName<typeof($obj)>`
- `\is_subclass_of($obj, $className)` -- narrows `$className` to `__CompatibleTypeName<typeof($obj)>`
- `variable_exists($count)` / `variable_exists('count')` -- narrows the name to `__VarName`

`is` is a keyword (an `instanceof` alias), not a `\is_a(...)` function call. `$$var` is prohibited (TYHP4133); do not write `isset($$varName)`.

```tyhp
<?tyhp

function callIfExists(string $funcName, string $arg): mixed {
    if (\function_exists($funcName)) {
        // $funcName is narrowed to __FunctionName
        // Safe to call dynamically
        return $funcName($arg);
    }
    return null;
}
```

## Generic Built-in Function Signatures

Many PHP built-in functions are given generic type signatures in Tyhp. This means the compiler can infer precise return types based on the input types, providing much stronger type safety than PHP's native type declarations.

## Array Functions

Signatures come from the shipped PHP tyhpdefs. Some functions are generic; many are not. Do not assume a generic form unless the tyhpdef declares it.

- `\array_map<TValue = mixed, TResult extends void|never|mixed = mixed>(?callable<TValue, TResult> $callback, array<TValue> $array, array ...$arrays): array<TResult>`
- `\array_values<TKey extends int|string, TValue = mixed>(array<TKey, TValue> $array): array<int, TValue>`
- `\array_reverse<TKey extends int|string = int|string, TValue = mixed>(array<TKey, TValue> $array, bool $preserve_keys = false): array<TKey, TValue>`
- `\array_pop(array &$array): mixed` — not generic
- `\array_shift(array &$array): mixed` — not generic
- `\array_filter(array $array, ?callable $callback = null, int $mode = 0): array` — not generic
- `\array_keys(array $array, mixed $filter_value = null, bool $strict = false): array` — not generic
- `\array_merge(array ...$arrays): array` — not generic

```tyhp
<?tyhp

array<string> $names = ["Alice", "Bob", "Charlie"];

// Return type is inferred as array<int> because the callback returns int
array<int> $lengths = \array_map(fn(string $name): int => \strlen($name), $names);

// Return type preserves the element type
array<string> $filtered = \array_filter($names, fn(string $n): bool => \strlen($n) > 3);
```

## Other Generic Functions

Many other PHP standard library functions also receive generic signatures. The tyhpdef files that ship with the compiler contain the full set of generic signatures for all PHP extensions.

## Compile-Time Functions

Tyhp provides four compile-time functions that are evaluated during compilation and replaced with their computed values in the PHP output. These are language constructs with dedicated lexer tokens and grammar rules, available only in `<?tyhp` blocks.

## `nameof(expr)`

Returns the string name of a symbol at compile time. The argument must be a valid symbol reference (a variable, function, class, method, property, or constant). The result is a string literal in the compiled PHP output. This is invaluable for refactoring-safe string references -- when you rename a symbol, `nameof()` references update automatically.

```tyhp
<?tyhp

class User {
    public string $name;
    public string $email;
}

// nameof returns the symbol name as a string
string $propName = nameof(User::$name);   // "name"
string $className = nameof(User);          // "User"
string $nested = nameof(fn (User $u) => $u->name); // "name"

int $count = 42;
string $varName = nameof($count);          // "count"

// Useful for error messages and logging
function validate(string $field, mixed $value): void {
    if ($value === null) {
        throw new \InvalidArgumentException(
            "Field '" . $field . "' cannot be null"
        );
    }
}
validate(nameof(User::$email), $emailInput);
```

Compiles to:

```php
<?php
declare(strict_types=1);

$propName = 'name';
$className = 'User';

$count = 42;
$varName = 'count';

function validate(string $field, mixed $value): void {
    if ($value === null) {
        throw new \InvalidArgumentException(
            "Field '" . $field . "' cannot be null"
        );
    }
}
validate('email', $emailInput);
```

## `typeof(expr)`

Returns a `\Tyhp\Type` instance representing the compile-time type of its argument. Scalars emit `\Tyhp\Type::int()` / `::string()` / etc. Declared classes emit `\Tyhp\Type::fromClassName(...)`. `\Tyhp\Type::of($value)` is a **runtime** helper that inspects a value — it is not what `typeof()` compiles to.

```tyhp
<?tyhp

use App\Models\User;

string $name = "Alice";
\Tyhp\Type $type = typeof(string);
\Tyhp\Type $userType = typeof(User);
```

Compiles to:

```php
<?php
declare(strict_types=1);

use App\Models\User;

$name = "Alice";
$type = \Tyhp\Type::string();
$userType = \Tyhp\Type::fromClassName('User'::class);
```

## `default(Type)`

Returns the default value for a given type at compile time. The argument must be a type expression, not a value. Each type has a well-defined default value:

- `default(int)` returns `0`
- `default(float)` returns `0.0`
- `default(string)` returns `''`
- `default(bool)` returns `false`
- `default(array)` returns `[]`
- `default(?T)` returns `null` for any nullable type
- `default(ClassName)` for a non-scalar / object type returns `null`. Assigning that to a non-nullable target is a type error; use `?User $u = default(User)` (or another nullable type).

```tyhp
<?tyhp

int $count = default(int);       // 0
string $text = default(string);   // ""
bool $flag = default(bool);       // false
array $items = default(array);    // []
?string $opt = default(?string);  // null
?User $user = default(User);      // null — non-scalars default to null
// User $bad = default(User);     // ERROR: null is not assignable to non-nullable User
```

Compiles to:

```php
<?php
declare(strict_types=1);

$count = 0;
$text = '';
$flag = false;
$items = [];
$opt = null;
$user = null;
```

## `variable_exists(expr)`

Checks whether a variable exists in the current scope. The argument is the variable itself or a string literal: `variable_exists($count)` or `variable_exists('count')`. Unlike `isset()`, `variable_exists()` is about existence, not the value — a declared variable that holds `null` still counts as existing. When the compiler cannot fold the check to a boolean literal, it emits `\array_key_exists('name', \get_defined_vars())`, not `isset()`. It can narrow a name to `__VarName`. `$$var` is prohibited (TYHP4133).

```tyhp
<?tyhp

int $count = 0;

if (variable_exists($count)) {
    // $count is declared in this scope
}

if (variable_exists('count')) {
    // same check using a string literal name
}

// Difference from isset():
// isset() returns false for variables that are null
// variable_exists() returns true as long as the variable is declared
```

Compiles to (when not folded):

```php
<?php
declare(strict_types=1);

$count = 0;

if (\array_key_exists('count', \get_defined_vars())) {
}
```

:::note
`variable_exists` is a language construct with its own lexer token (`T_TYHP_VARIABLE_EXISTS`) and grammar rule. It is parsed as an internal function by the compiler and is only available in Tyhp mode (`<?tyhp` blocks).
:::

:::note
All four compile-time functions (`nameof`, `typeof`, `default`, `variable_exists`) have dedicated lexer tokens and grammar rules in the Tyhp parser. They are parsed as internal functions and are only available in Tyhp mode.
:::

## Extension Methods

Tyhp lets you declare `extension` types that add methods to existing types, including scalars. The receiver is a parameter marked `extends string $this` (or another type). User-defined extensions work in this alpha. See Scalar Pseudo-Objects and Extensions for the full syntax.

```tyhp
<?tyhp

extension StringHelpers {
    function toCamelCase(extends string $this): string {
        return $this;
    }
}

string $name = "hello_world";
string $camel = $name->toCamelCase();
```

Compiles to:

```php
<?php
declare(strict_types=1);

$name = 'hello_world';
$camel = \StringHelpers::toCamelCase($name);
```

## Built-in Scalar Methods

```status
tier: 2
story: '21'
state: planned
```

:::warning Not in this alpha
A built-in catalog of scalar methods (`$name->toUpper()`, `$name->contains(...)`, array `map`, …) is **planned** for Story 21 (`tyhp/php` support extensions). It is **not included** in Tyhp 805.0.0-alpha.1. Until it ships, call PHP functions (`\strtoupper($name)`) or write and import your own extensions as in the example above.
:::

## Best Practices

:::tip
Use type guard functions (`\is_string()`, `\is_int()`, etc.) to narrow union types before performing type-specific operations. The compiler automatically narrows in the guarded scope.
:::

:::tip
Use `nameof()` instead of hardcoded name strings for variables, properties, and class names. It is refactoring-safe -- renaming the symbol updates all `nameof()` references.
:::

:::tip
Use `typeof()` when you need a `\Tyhp\Type` value for Tyhp's runtime type helpers. For a class-name string, use `nameof(User)` or `User::class`.
:::

:::tip
Use `default(Type)` to initialize variables with the canonical default value for their type. This is clearer than writing `0`, `''`, or `false` directly and communicates intent.
:::

:::tip
Use generic function signatures to get precise return types from built-in functions. For example, `\array_map` with typed callbacks gives you a precisely typed return array.
:::

## Common Mistakes

:::danger
Don't call methods on union types without narrowing first. The compiler rejects operations that are not valid for all members of the union.
:::

:::danger
Don't hardcode class or property names as strings when `nameof()` or `typeof()` is available. Hardcoded strings break silently when you rename symbols.
:::

:::danger
Don't use `variable_exists()` or other compile-time functions in `<?php` blocks. They are Tyhp-only language constructs and cause parse errors in PHP mode.
:::

:::danger
Don't confuse `variable_exists()` with `isset()`. `isset()` returns `false` for variables set to `null`, while `variable_exists()` returns `true` for any declared variable regardless of its value.
:::

## Compiler Error Examples

```tyhp
<?tyhp

string|int $value = getValue();

// ERROR: cannot pass string|int to a string-only function without narrowing
// $length = \strlen($value);

// Correct approach: narrow with a type guard
if (\is_string($value)) {
    string $upper = \strtoupper($value);  // OK: $value is narrowed to string
}
```

```tyhp
<?tyhp

// ERROR: nameof() requires a valid symbol reference
// string $invalid = nameof(42);  // 42 is not a symbol

// Correct usage:
int $myNumber = 42;
string $name = nameof($myNumber);  // "myNumber"
```
