---
title: 'New and Changed Functions and Methods in Tyhp'
status:
  tier: 0
  story: '04'
  state: complete
---

Tyhp introduces new functions, changes the behavior of some existing PHP functions, and gives many built-in functions generic type signatures for better type safety. This page covers guard functions, generic built-in function signatures, compile-time functions, and scalar pseudo-object methods.

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
- `\is_a($obj, $className)` -- narrows `$className` to `__CompatibleTypeName<typeof($obj)>`
- `\is_subclass_of($obj, $className)` -- narrows `$className` to `__CompatibleTypeName<typeof($obj)>`
- `isset($$varName)` -- narrows `$varName` to `__VarName`

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

- `\array_map<T, U>(callable<T, U> $callback, array<T> $array): array<U>`
- `\array_filter<T>(array<T> $array, ?callable<T, bool> $callback = null): array<T>`
- `\array_keys<TKey, TValue>(array<TKey, TValue> $array): array<TKey>`
- `\array_values<T>(array<T> $array): array<T>`
- `\array_merge<T>(array<T> ...$arrays): array<T>`
- `\array_unique<T>(array<T> $array): array<T>`
- `\array_reverse<T>(array<T> $array): array<T>`
- `\array_slice<T>(array<T> $array, int $offset, ?int $length = null): array<T>`
- `\array_pop<T>(array<T> &$array): ?T`
- `\array_shift<T>(array<T> &$array): ?T`
- `\array_push<T>(array<T> &$array, T ...$values): int`
- `\array_column<TKey, TValue>(array<array<TKey, TValue>> $array, TKey $columnKey): array<TValue>`
- `\array_combine<TKey, TValue>(array<TKey> $keys, array<TValue> $values): array<TKey, TValue>`
- `\array_chunk<T>(array<T> $array, int $length): array<array<T>>`
- `\array_reduce<T, U>(array<T> $array, callable<U, T, U> $callback, U $initial): U`
- `\array_walk<TKey, TValue>(array<TKey, TValue> &$array, callable<TValue, TKey, void> $callback): bool`

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

Returns a `\Tyhp\Type` instance representing the type of the given expression at compile time. For class/interface names, it resolves to the fully-qualified class name string. This is useful for runtime type reflection with compile-time safety.

```tyhp
<?tyhp

use App\Models\User;

string $name = "Alice";
\Tyhp\Type $type = typeof($name);    // \Tyhp\Type::_string()

// typeof on a class name resolves to the FQCN string
string $fqcn = typeof(User);           // 'App\\Models\\User'
```

Compiles to:

```php
<?php
declare(strict_types=1);

use App\Models\User;

$name = "Alice";
$type = \Tyhp\Type::_string();

$fqcn = 'App\\Models\\User';
```

## `default(Type)`

Returns the default value for a given type at compile time. The argument must be a type expression, not a value. Each type has a well-defined default value:

- `default(int)` returns `0`
- `default(float)` returns `0.0`
- `default(string)` returns `''`
- `default(bool)` returns `false`
- `default(array)` returns `[]`
- `default(?T)` returns `null` for any nullable type

```tyhp
<?tyhp

int $count = default(int);       // 0
string $text = default(string);   // ""
bool $flag = default(bool);       // false
array $items = default(array);    // []
?string $opt = default(?string);  // null
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
```

## `variable_exists(expr)`

Checks whether a variable with the given name exists in the current scope. Unlike `isset()`, `variable_exists()` checks for the variable's existence without checking its value -- a variable that exists but holds `null` still returns `true`. It acts as a type guard, narrowing the variable name to `__VarName`.

```tyhp
<?tyhp

string $varName = "myVar";

if (variable_exists($varName)) {
    // $varName is narrowed to __VarName
    // The variable named by $varName is known to exist in scope
}

// Difference from isset():
// isset() returns false for variables that are null
// variable_exists() returns true as long as the variable is declared
```

:::note
`variable_exists` is a language construct with its own lexer token (`T_TYHP_VARIABLE_EXISTS`) and grammar rule. It is parsed as an internal function by the compiler and is only available in Tyhp mode (`<?tyhp` blocks).
:::

:::note
All four compile-time functions (`nameof`, `typeof`, `default`, `variable_exists`) have dedicated lexer tokens and grammar rules in the Tyhp parser. They are parsed as internal functions and are only available in Tyhp mode.
:::

## Extension Methods (Scalar Pseudo-Object Methods)

Tyhp provides built-in extension methods on scalar types (`string`, `int`, `float`, `bool`, `array`) that allow calling methods directly on scalar values using object syntax. These compile to standard PHP function calls in the output. See the dedicated Scalar Pseudo-Objects documentation page for the full list of available methods.

```tyhp
<?tyhp

string $name = "alice";
string $upper = $name->toUpper();  // Compiles to: \strtoupper($name)

array<int> $numbers = [3, 1, 4, 1, 5];
int $count = $numbers->count();    // Compiles to: \count($numbers)
```

Compiles to:

```php
<?php
declare(strict_types=1);

$name = 'alice';
$upper = \strtoupper($name);

$numbers = [3, 1, 4, 1, 5];
$count = \count($numbers);
```

## Best Practices

:::tip
Use type guard functions (`\is_string()`, `\is_int()`, etc.) to narrow union types before performing type-specific operations. The compiler automatically narrows in the guarded scope.
:::

:::tip
Use `nameof()` instead of hardcoded name strings for variables, properties, and class names. It is refactoring-safe -- renaming the symbol updates all `nameof()` references.
:::

:::tip
Use `typeof()` instead of hardcoded class name strings when you need the FQCN of a type. This keeps your code safe from namespace or rename changes.
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

// ERROR: Method 'toUpper' does not exist on type 'int'
// Must narrow first:
// $value->toUpper();

// Correct approach: narrow with a type guard
if (\is_string($value)) {
    string $upper = $value->toUpper();  // OK: $value is narrowed to string
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
