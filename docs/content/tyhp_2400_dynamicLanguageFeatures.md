---
title: 'Dynamic Language Features in Tyhp'
status:
  tier: 0
  story: '08'
  state: complete
---

Tyhp restricts PHP's dynamic language features to improve type safety and enable static analysis. Variable variables (`$$var`) are prohibited (TYHP4133). Variable functions, dynamic property access, dynamic method calls, and dynamic class instantiation are not disabled — but they require type narrowing through special string value types or type guard functions before use. This ensures the compiler can verify that the referenced symbols actually exist, preventing runtime errors from invalid references.

## Why Dynamic Features Are Restricted

In PHP, dynamic features like variable functions and dynamic method calls accept any string at runtime with no compile-time verification. This makes it impossible for a static type checker to reason about the code. Tyhp solves this by requiring developers to narrow the type of the string before using it in a dynamic context. Once narrowed, the compiler can verify the symbol exists and the dynamic access compiles to the same PHP code as standard PHP — no runtime overhead is added. Variable variables (`$$var`) are a separate case: they are banned outright (TYHP4133).

## The Special __ Types

Tyhp introduces a set of special string value types (prefixed with __) that represent compile-time-verified symbol names. These types are subtypes of string that carry additional compile-time guarantees about the symbol they reference. Assigning a string literal to one of these types causes the compiler to verify the symbol exists at compile time.

:::member[__VarName]
A string that is a valid variable name in the current scope. Used with `variable_exists($input)` on a simple variable. Variable variables (`$$var`) are prohibited (TYHP4133) — `__VarName` does not re-enable them.
:::

:::member[__FunctionName]
A string that is a valid, existing function name. Required for variable functions ($fn()). The compiler verifies the function is declared and accessible.
:::

:::member[__MethodName]
A string that is a valid method name on the target object. Required for dynamic method calls ($obj->$method()).
:::

:::member[__PropertyName<T>]
A string that is a valid property name on type T. Required for dynamic property access ($obj->$prop). The generic parameter T specifies which class's properties are valid.
:::

:::member[__ClassName]
A string that is a valid, existing class name (resolvable to a class declaration). Required for dynamic class instantiation (new $className()).
:::

:::member[__ConstName]
A string that is a valid constant name. Required for dynamic constant access.
:::

:::note
The __ types are compile-time-only annotations. They are erased from the PHP output — the generated PHP uses plain string values. No runtime overhead is added.
:::

## Variable Variables ($$var) — Prohibited

PHP allows accessing variables by name using `$$var`. Tyhp **prohibits** variable variables (TYHP4133). There is no `__VarName` exception and no `variable_exists($$input)` guard that re-enables them. Check whether a simple variable is in scope with `variable_exists($input)` (see Compile-Time Constructs).

```tyhp
<?tyhp

string $greeting = 'Hello';

// ERROR TYHP4133: variable variables are prohibited
// __VarName $varName = 'greeting';
// echo $$varName;

// OK: existence check on a simple variable — not $$input
if (variable_exists($greeting)) {
    echo $greeting;
}
```

```php
<?php

$greeting = 'Hello';

if (\array_key_exists('greeting', \get_defined_vars())) {
    echo $greeting;
}
```

## Variable Functions ($fn())

Calling a function through a variable requires the variable to be of type __FunctionName. The compiler verifies the function exists and is callable.

```tyhp
<?tyhp

function greet(string $name): string {
    return "Hello, {$name}!";
}

// OK: string literal narrowed to __FunctionName automatically
__FunctionName $fn = 'greet';
echo $fn('World'); // Outputs: Hello, World!

// OK: narrowing via runtime check
string $funcName = getHandlerName();
if (\function_exists($funcName)) {
    // $funcName is narrowed to __FunctionName here
    echo $funcName('test');
}
```

```php
<?php

function greet(string $name): string {
    return "Hello, {$name}!";
}

$fn = 'greet';
echo $fn('World');

$funcName = getHandlerName();
if (\function_exists($funcName)) {
    echo $funcName('test');
}
```

## Dynamic Property Access ($obj->$prop)

Accessing a property via a variable requires __PropertyName<T> where T is the type of the object. The generic parameter ensures only valid property names for that specific class are accepted.

```tyhp
<?tyhp

class User {
    public string $name = 'Alice';
    public int $age = 30;
    public string $email = 'alice@example.com';
}

User $user = new User();

// OK: narrowed to __PropertyName<User>
__PropertyName<User> $prop = 'name';
echo $user->$prop; // Outputs: Alice

// OK: dynamic narrowing via property_exists
string $fieldName = getFieldName();
if (\property_exists($user, $fieldName)) {
    // $fieldName is narrowed to __PropertyName<User>
    echo $user->$fieldName;
}
```

```php
<?php

class User {
    public string $name = 'Alice';
    public int $age = 30;
    public string $email = 'alice@example.com';
}

$user = new User();

$prop = 'name';
echo $user->$prop;

$fieldName = getFieldName();
if (\property_exists($user, $fieldName)) {
    echo $user->$fieldName;
}
```

## Dynamic Method Calls ($obj->$method())

Calling a method via a variable requires the variable to be narrowed to __MethodName. The compiler verifies the method exists on the target object.

```tyhp
<?tyhp

class Calculator {
    public function add(int $a, int $b): int { return $a + $b; }
    public function sub(int $a, int $b): int { return $a - $b; }
}

Calculator $calc = new Calculator();

// OK: literal narrowed to __MethodName
__MethodName $method = 'add';
echo $calc->$method(5, 3); // Outputs: 8

// OK: narrowing via method_exists
string $op = getOperation();
if (\method_exists($calc, $op)) {
    echo $calc->$op(10, 4);
}
```

```php
<?php

class Calculator {
    public function add(int $a, int $b): int { return $a + $b; }
    public function sub(int $a, int $b): int { return $a - $b; }
}

$calc = new Calculator();

$method = 'add';
echo $calc->$method(5, 3);

$op = getOperation();
if (\method_exists($calc, $op)) {
    echo $calc->$op(10, 4);
}
```

## Dynamic Class Instantiation (new $className())

Creating an object from a variable class name requires __ClassName. The compiler verifies that the string refers to an existing class.

```tyhp
<?tyhp

// OK: string literal narrowed to __ClassName
__ClassName $cls = 'App\\Models\\User';
$instance = new $cls();

// OK: narrowing from runtime input
mixed $className = getHandlerClass();
if ($className is string && \class_exists($className)) {
    // $className is narrowed to __ClassName
    $handler = new $className();
}
```

```php
<?php

$cls = 'App\\Models\\User';
$instance = new $cls();

$className = getHandlerClass();
if (\is_string($className) && \class_exists($className)) {
    $handler = new $className();
}
```

## Type Narrowing for Dynamic Features

The special __ types are subtypes of string. You can narrow a plain string to a __ type in several ways: assigning a string literal (the compiler verifies it at compile time), using the is keyword for type assertion, or using PHP's built-in existence-checking functions which the Tyhp compiler recognizes as type guards.

The following ExtCore / PHP existence checks are type guards (return `$param is …` in tyhpdefs) that narrow strings to the appropriate __ types:

- `\class_exists($name)` — narrows `$name` to `__ClassName<object>` (use `\class_exists<T>($name)` for `__ClassName<T>`)
- `\function_exists($name)` — narrows `$name` to `__FunctionName`
- `\interface_exists($name)` / `\trait_exists($name)` / `\enum_exists($name)` — `__InterfaceName` / `__TraitName` / `__EnumName` (same default-`<object>` pattern)
- `\method_exists($obj, $name)` — narrows `$name` to `__MethodName` of the receiver type (hardcoded fallback until tyhpdef can capture the receiver)
- `\property_exists($obj, $name)` — narrows `$name` to `__PropertyName<T>`
- `is` type assertion — narrows to any __ type explicitly

```tyhp
<?tyhp

// Pattern 1: String literal (compile-time verification)
__ClassName $cls = 'App\\Models\\User';

// Pattern 2: is type assertion
string $input = getConfig('handler_class');
if ($input is __ClassName) {
    $handler = new $input();
}

// Pattern 3: Built-in type guard function
string $funcName = getSetting('callback');
if (\function_exists($funcName)) {
    $result = $funcName();
}

// Pattern 4: Combined narrowing
mixed $methodName = getInput();
if ($methodName is string && \method_exists($obj, $methodName)) {
    $obj->$methodName();
}
```

## Best Practices

:::tip
Use the specific __ type annotations (__ClassName, __FunctionName, __PropertyName<T>, __MethodName) for variables that hold symbol names. This enables compile-time verification of the symbol's existence. Variable variables (`$$var`) are prohibited — use `variable_exists($name)` on a simple variable when you need an existence check.
:::

:::tip
Prefer static access patterns over dynamic ones whenever possible. Static property access ($obj->name), static method calls ($obj->method()), and direct class instantiation (new ClassName()) are always preferable because the compiler can fully verify them.
:::

:::tip
Use PHP's built-in existence-checking functions (\class_exists(), \function_exists(), \method_exists(), \property_exists()) as type guards before dynamic access. The compiler recognizes these and automatically narrows the type.
:::

:::tip
When working with configuration-driven class names or callbacks, narrow the type at the boundary (as close to the input source as possible) and pass the narrowed __ type through your code.
:::

## Common Mistakes

:::danger
Using a plain string for dynamic access without narrowing first. The compiler requires type narrowing — using an unverified string for `$fn()`, `$obj->$prop`, `$obj->$method()`, or `new $cls()` produces a compiler error. Variable variables (`$$var`) are prohibited entirely (TYHP4133).
:::

:::danger
Using extract() or compact() — these dynamically create or collect variables, which completely bypasses static analysis. Tyhp reports errors TYHP4136 (extract) and TYHP4135 (compact).
:::

:::danger
Attempting dynamic property creation ($obj->$newProp = value) where $newProp does not exist on the class. Tyhp disables dynamic property creation — all properties must be declared in the class definition or accessed via __get/__set magic methods.
:::

## Compiler Errors

The Tyhp compiler reports specific errors when dynamic features are used without proper type narrowing.

```tyhp
<?tyhp

// ERROR: Dynamic class instantiation with plain string
// requires narrowing to __ClassName
string $cls = getClassName();
// $obj = new $cls();  // Compiler error!

// FIX: Narrow first
if (\class_exists($cls)) {
    $obj = new $cls();  // OK: $cls narrowed to __ClassName
}

// ERROR: String literal does not match any known class
// __ClassName $bad = 'NonExistentClass';  // Compiler error!

// ERROR: 'notAProperty' is not a property of User
// __PropertyName<User> $bad2 = 'notAProperty';  // Compiler error!

// ERROR TYHP4133: Variable variables are prohibited
string $name = getUserInput();
// echo $$name;

// ERROR: Variable function without narrowing
string $fn = getCallback();
// $fn('arg');  // Compiler error: use __FunctionName type
```

:::note
Dynamic language features compile to the same PHP code as standard PHP — variable functions and dynamic member access produce identical PHP output. Variable variables (`$$var`) are not emitted; they are a compile error (TYHP4133). The __ types are compile-time-only annotations that are erased during compilation. No runtime overhead is added.
:::
