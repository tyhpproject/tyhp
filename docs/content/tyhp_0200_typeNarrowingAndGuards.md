---
title: 'Type Narrowing and Guards in Tyhp'
status:
  tier: 0
  story: '08'
  state: complete
---

When dealing with values that can be of multiple types (such as union types or nullable types), you often need to verify or narrow the type before performing type-specific operations. Tyhp uses type guards -- boolean checks that narrow a variable's type within a guarded scope. This happens automatically: whenever a variable passes a type guard check, the compiler tracks its narrowed type within the guarded block. No explicit casts or re-declarations are needed.

## How Type Narrowing Works

Type narrowing is scope-sensitive. When a variable passes a type guard check in a conditional, its type is narrowed within the guarded block. Outside the block, the variable reverts to its full declared type. Narrowing is automatic -- the compiler tracks the narrowed type and uses it for all subsequent type checks within the scope.

```tyhp
<?tyhp

string|int|float|bool $myVar = \getResultFromDatabaseQuery();

if (\is_string($myVar)) {
    // $myVar is automatically narrowed to `string` in this block
    $myVar = \substr($myVar, 0, 5);  // OK: substr expects string

    // Reassignment within a narrowed scope ends the current narrowing
    $myVar = 44;
    // $myVar is now narrowed to `int` for the rest of this block
}
// Outside the if block, $myVar is its full union type again:
// string|int|float|bool
```

## Built-in Type Guards

PHP's built-in type checking functions act as type guards in Tyhp. When used in a conditional check, they narrow the type of the checked variable.

- `\is_string($val)` -- narrows to `string`
- `\is_int($val)` -- narrows to `int`
- `\is_float($val)` -- narrows to `float`
- `\is_bool($val)` -- narrows to `bool`
- `\is_array($val)` -- narrows to `array`
- `\is_object($val)` -- narrows to `object`
- `\is_null($val)` -- narrows to `null`
- `\is_numeric($val)` -- narrows to `int|float|string`
- `\is_callable($val)` -- narrows to `callable`
- `\is_resource($val)` -- narrows to `resource`

## The `instanceof` and `is` Keywords

Tyhp supports `instanceof` from PHP and adds several aliases. The following keywords are all equivalent and can be used interchangeably as type guard expressions:

- `instanceof` -- the standard PHP keyword
- `is` -- Tyhp alias
- `isa` -- Tyhp alias (reads naturally: "$x isa Foo")
- `isan` -- Tyhp alias (reads naturally: "$x isan Iterator")
- `is_a` -- Tyhp alias (with underscore)
- `is_an` -- Tyhp alias (with underscore)

All of these check if a value is an instance of a specific class or interface (or a descendant of that type) and narrow the variable's type accordingly. They are defined in the lexer as the `T_TYHP_IS` token and are only available in Tyhp mode. For scalar types, the `is` keyword compiles to the appropriate type check function. For object types, it compiles to `instanceof`.

```tyhp
<?tyhp

BuilderInterface|ConnectionInterface|null $builder = \getRemoteBuilderInstance();

// Null check as type guard
if (!\is_null($builder)) {
    // $builder is narrowed to BuilderInterface|ConnectionInterface
    $isConnected = $builder->connected();
}

// instanceof narrows to specific type
if ($builder instanceof BuilderInterface) {
    // $builder is narrowed to BuilderInterface
    $builder->build();
}

// 'is' keyword works the same as instanceof
if ($builder is ConnectionInterface) {
    // $builder is narrowed to ConnectionInterface
    $builder->disconnect();
}

// Natural English-like syntax
if ($builder isa BuilderInterface) {
    $builder->build();
}
```

## Null Checks as Type Guards

Null comparisons also act as type guards. Checking `!== null`, `=== null`, or `!\is_null()` narrows nullable types:

```tyhp
<?tyhp

?string $name = \getOptionalName();

if ($name !== null) {
    // $name is narrowed to `string` (null removed from the union)
    echo \strtoupper($name);
} else {
    // $name is narrowed to `null` in the else branch
    echo "No name provided";
}
```

## Negated Guards (Else Branches)

When a type guard check fails (the else branch), the variable is narrowed to the remaining types -- the original type minus the guarded type. This is called negative narrowing.

```tyhp
<?tyhp

string|int|null $value = \getData();

if (\is_string($value)) {
    // $value is `string`
} else {
    // $value is `int|null` (string was removed)

    if ($value !== null) {
        // $value is `int` (null was also removed)
    }
}
```

## Narrowing Through Logical Operators

Type narrowing compounds through `&&` (AND) and distributes through `||` (OR) operators:

```tyhp
<?tyhp

string|int|null $val = \getData();

// AND narrows cumulatively
if ($val !== null && \is_string($val)) {
    // $val is `string` (both conditions apply)
}

// OR produces the union of the narrowed types
if (\is_string($val) || \is_int($val)) {
    // $val is `string|int`
}
```

## Custom Type Guard Functions

You can define your own type guard functions using a special return type syntax. Instead of returning `bool`, the return type declares which parameter is narrowed to which type using the syntax: `$paramName is TypeExpr` or `$paramName instanceof TypeExpr`.

```tyhp
<?tyhp

// Custom type guard function
function isNonEmptyArray(mixed $value): $value is array {
    return \is_array($value) && \count($value) > 0;
}

bool|array $val = \myFunc(34);

if (isNonEmptyArray($val)) {
    // $val is narrowed to `array` in this block
    $first = $val[0];
} else {
    // $val is narrowed to `bool` in this block
    // (array was removed from the union)
}
```

The guard return type syntax is defined in the grammar's `returnTypeGrammarAddon` rule. You can use any of the `is`/`instanceof` variants in the return type declaration.

```tyhp
<?tyhp

// Guard with a class type
function isActiveUser(object $obj): $obj instanceof ActiveUser {
    return $obj instanceof ActiveUser && $obj->isActive();
}

// Guard with a union type
function isStringOrInt(mixed $val): $val is string|int {
    return \is_string($val) || \is_int($val);
}

// Guard narrowing to a scalar
function isPositiveInt(mixed $val): $val is int {
    return \is_int($val) && $val > 0;
}
```

## Dynamic Guard Functions

PHP's symbol existence functions also act as type guards, narrowing string values to Tyhp's symbol name types. This enables type-safe dynamic programming:

- `\function_exists($name)` -- narrows `$name` to `__FunctionName`
- `\class_exists($name)` -- narrows `$name` to `__ClassName<object>` (omit `<T>`; use `\class_exists<Foo>($name)` for `__ClassName<Foo>`)
- `\function_exists($name)` -- narrows `$name` to `__FunctionName`
- `\interface_exists($name)` / `\trait_exists($name)` / `\enum_exists($name)` -- same pattern with `__InterfaceName` / `__TraitName` / `__EnumName`
- `\interface_exists($name)` -- narrows `$name` to `__InterfaceName`
- `\trait_exists($name)` -- narrows `$name` to `__TraitName`
- `\enum_exists($name)` -- narrows `$name` to `__EnumName`
- `\property_exists($obj, $name)` -- narrows `$name` to `__PropertyName<typeof($obj)>`
- `\method_exists($obj, $name)` -- narrows `$name` to `__MethodName<typeof($obj)>`
- `isset($$varName)` -- narrows `$varName` to `__VarName`
- `variable_exists($name)` -- narrows `$name` to `__VarName`

## Early Return Narrowing

When a type guard is used with an early return (or `throw`, `continue`, `break`), the type is narrowed for all subsequent code after the guard. This is a common pattern for eliminating null or invalid types at the top of a function.

```tyhp
<?tyhp

function processUser(?User $user): string {
    if ($user === null) {
        return "No user";
    }

    // $user is narrowed to `User` for all remaining code
    // because the null case was handled by the early return
    return $user->getName();
}

function handleValue(string|int|array $val): void {
    if (\is_array($val)) {
        throw new \InvalidArgumentException("Arrays not supported");
    }

    // $val is narrowed to `string|int` for all remaining code
    echo $val;
}
```

## Ternary and Match Narrowing

Type narrowing also works within ternary expressions and `match` expressions:

```tyhp
<?tyhp

?string $name = \getOptionalName();

// Ternary narrowing: in the true branch, $name is `string`
string $display = $name !== null ? \strtoupper($name) : "Anonymous";

// Match narrowing
string|int|float $value = \getMixedValue();
string $result = match(true) {
    \is_string($value) => $value,           // narrowed to string
    \is_int($value)    => (string) $value,   // narrowed to int
    default            => \number_format($value, 2), // narrowed to float
};
```

## Compiled PHP Output

Type narrowing is entirely a compile-time concept. The guard functions compile to normal PHP -- no runtime type tracking is added. The narrowing information only exists during the Tyhp compilation phase for type checking purposes.

```tyhp
<?tyhp

function describe(string|int $val): string {
    if (\is_string($val)) {
        return "String: " . $val;
    }
    return "Int: " . $val;
}
```

Compiles to:

```php
<?php
declare(strict_types=1);

function describe(string|int $val): string {
    if (\is_string($val)) {
        return 'String: ' . $val;
    }
    return 'Int: ' . $val;
}
```

Custom type guard functions compile identically to regular boolean functions -- the guard return type syntax is erased:

```tyhp
<?tyhp

function isPositiveInt(mixed $val): $val is int {
    return \is_int($val) && $val > 0;
}
```

Compiles to:

```php
<?php
declare(strict_types=1);

function isPositiveInt(mixed $val): bool {
    return \is_int($val) && $val > 0;
}
```

The `is` keyword compiles to `instanceof` for object types:

```tyhp
<?tyhp

function checkBuilder(object $obj): void {
    if ($obj is BuilderInterface) {
        $obj->build();
    }
}
```

Compiles to:

```php
<?php
declare(strict_types=1);

function checkBuilder(object $obj): void {
    if ($obj instanceof BuilderInterface) {
        $obj->build();
    }
}
```

## Best Practices

:::tip
Use early return patterns with null checks to avoid deep nesting. This narrows the type for all remaining code and makes functions easier to read.
:::

:::tip
Create custom type guard functions for complex type checks that you repeat across your codebase. This centralizes the logic and gives each call site automatic narrowing.
:::

:::tip
Use `is` or `instanceof` to narrow to specific class or interface types. Both work identically -- choose whichever reads more naturally in your code.
:::

:::tip
Use specific types in function parameters and return types to minimize the need for narrowing. The more precise your types, the less narrowing you need.
:::

:::tip
Take advantage of negated guards (else branches). After checking `\is_string()`, the else branch automatically has `string` removed from the union.
:::

## Common Mistakes

:::danger
Don't assume a variable's type without using a type guard. The compiler rejects operations that are not valid for all types in the union.
:::

:::danger
Don't use `mixed` without narrowing it first. `mixed` requires a type guard before any type-specific operation can be performed.
:::

:::danger
Don't rely on PHP's implicit type coercion. Tyhp enforces strict types -- passing an `int` where a `string` is expected is always an error, even if PHP would coerce it.
:::

:::danger
Don't assume narrowing persists after scope exit. Once you leave the guarded block (if/else/while), the variable reverts to its full declared type.
:::

:::danger
Don't forget that reassigning a variable within a narrowed scope resets the narrowing to the new assigned type.
:::

## Compiler Error Examples

```tyhp
<?tyhp

// ERROR: Cannot call string method on a union type without narrowing
string|int $value = \getValue();
// $length = \strlen($value);  // strlen expects string, not string|int

// Fix: narrow first
if (\is_string($value)) {
    $length = \strlen($value);  // OK: $value is narrowed to string
}

// ERROR: Property access on potentially null type
?User $user = \findUser(42);
// $name = $user->getName();  // $user might be null

// Fix: null guard
if ($user !== null) {
    $name = $user->getName();  // OK: $user is narrowed to User
}

// Or use nullsafe operator (does not narrow, returns nullable)
?string $name = $user?->getName();
```

## Important Notes

- Type narrowing is scope-sensitive -- it only applies within the guarded block.
- Assigning a value of a different type within a narrowed scope ends the current narrowing and starts a new one based on the assigned type.
- Early returns, throws, continues, and breaks narrow the type for the remaining code after them.
- The narrowed type can only be one of the types in the original union type -- narrowing never introduces new types.
- Outside of the guarded scope, the variable reverts to its full declared type.
- Multiple guards compound: after checking `!== null` and then `is Foo`, the type is `Foo`.
- Custom type guard functions must actually return a boolean value at runtime -- the guard return type only tells the compiler about the narrowing effect.
