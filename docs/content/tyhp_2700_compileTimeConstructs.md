---
title: 'Compile-Time Constructs'
status:
  tier: 1
  story: '11'
  state: complete
---

Tyhp provides four compile-time language constructs that are evaluated during compilation: nameof(), typeof(), default(), and variable_exists(). These constructs emit constant values or simple runtime calls in the PHP output, enabling refactoring-safe code, type-appropriate default values, and compile-time variable existence checks. Beyond these constructs, Tyhp has several other features that are fully resolved at compile time with zero runtime overhead.

## nameof() — Get Symbol Name as String

The nameof() construct returns the string name of a variable, class, method, property, or constant. The argument must be a valid, resolvable symbol reference. The compiler resolves the name at compile time and emits it as a string literal — no runtime reflection is involved.

```tyhp
<?tyhp

class User {
    public string $name;
    public int $age;
    public const int MAX_AGE = 150;

    public function greet(): string {
        return 'Hello!';
    }
}

User $user = new User();

// Variable name
echo nameof($user);              // 'user'

// Class name (short name only)
echo nameof(User);                // 'User'

// Namespaced class (still short name only)
echo nameof(\App\Models\User);    // 'User'

// Property name
echo nameof($user->name);         // 'name'
echo nameof(User::$age);          // 'age'

// Method name
echo nameof($user->greet);        // 'greet'

// Class constant
echo nameof(User::MAX_AGE);       // 'MAX_AGE'

// PropertyPath-style arrow fn — last segment only (Story 16)
echo nameof(fn (User $u) => $u->name);        // 'name'
echo nameof(fn (User $u) => $u->address->city); // 'city'
```

```php
<?php

// nameof() compiles to string literals — zero runtime cost
echo 'user';
echo 'User';
echo 'User';
echo 'name';
echo 'age';
echo 'greet';
echo 'MAX_AGE';
echo 'name';
echo 'city';
```

A common use case for nameof() is in error messages, logging, and validation — any place where you reference a symbol by name as a string. Using nameof() instead of a hardcoded string ensures the reference stays valid when the symbol is renamed.

```tyhp
<?tyhp

function validateAge(int $age): void {
    if ($age < 0 || $age > 150) {
        throw new \InvalidArgumentException(
            nameof($age) . ' must be between 0 and 150, got ' . $age
        );
    }
}

// If $age is renamed to $userAge, nameof() updates automatically:
// 'userAge must be between 0 and 150, got ...'

// ORM/serialization example
class UserQuery {
    public function orderBy(string $field): self {
        $this->orderField = $field;
        return $this;
    }
}

// Refactoring-safe field reference:
$query->orderBy(nameof(User::$name)); // 'name'
```

```php
<?php

function validateAge(int $age): void {
    if ($age < 0 || $age > 150) {
        throw new \InvalidArgumentException(
            'age' . ' must be between 0 and 150, got ' . $age
        );
    }
}

$query->orderBy('name');
```

:::tip
nameof() is refactoring-safe. If you rename a property, method, variable, or class, all nameof() references update automatically during compilation, preventing stale string references.
:::

## typeof() — Get Type at Compile Time

The typeof() construct returns a \Tyhp\Type instance representing the compile-time type of its argument. It accepts type names (like int, string, User). Scalars compile to `\Tyhp\Type::int()` / `::string()` / etc.; declared classes compile to `\Tyhp\Type::fromClassName(...)`. This is different from PHP's gettype() (a runtime string) and from `\Tyhp\Type::of($value)` (runtime inspection of a value).

```tyhp
<?tyhp

// typeof with scalar type names
\Tyhp\Type $intType = typeof(int);
\Tyhp\Type $stringType = typeof(string);

// typeof with class names
\Tyhp\Type $userType = typeof(User);
\Tyhp\Type $dateType = typeof(\DateTimeInterface);

// Use for runtime type checks with the Tyhp type system
if (\Tyhp\Type::is($value, typeof(string))) {
    echo 'Value is a string';
}

// Use in generic contexts for runtime type information
class Repository<T> {
    public function getEntityType(): \Tyhp\Type {
        return typeof(T);
    }
}
```

```php
<?php

$intType = \Tyhp\Type::int();
$stringType = \Tyhp\Type::string();

$userType = \Tyhp\Type::fromClassName('User'::class);
$dateType = \Tyhp\Type::fromClassName('DateTimeInterface'::class);

if (\Tyhp\Type::is($value, \Tyhp\Type::string())) {
    echo 'Value is a string';
}

// typeof(T) on a class generic — HasGenerics / variant lookup, not Type::of('T')
return ($this->__tyhpGeneric->resolvedType(\Repository::class, 'T'));
```

## default() — Get Default Value for a Type

The default() construct returns the default value for a given type. The argument must be a valid type name. The result is a constant value emitted directly into the PHP output. This is particularly useful in generic contexts where you need a type-appropriate zero value, or when initializing variables with sensible defaults.

```tyhp
<?tyhp

// Scalar type defaults
int $i = default(int);         // 0
float $f = default(float);     // 0.0
string $s = default(string);   // ''
bool $b = default(bool);       // false
array $a = default(array);     // []

// Nullable types always default to null
?int $ni = default(?int);      // null
?string $ns = default(?string); // null

// Object types default to null
?User $u = default(User);      // null

// Useful in generic code
function getOrDefault<T>(array $data, string $key): T {
    return $data[$key] ?? default(T);
}

// Initializing accumulators
function sum(array<int> $values): int {
    int $total = default(int); // 0
    foreach ($values as int $v) {
        $total += $v;
    }
    return $total;
}
```

```php
<?php

// default() compiles to literal constant values
$i = 0;
$f = 0.0;
$s = '';
$b = false;
$a = [];
$ni = null;
$ns = null;
$u = null;

function sum(array $values): int {
    $total = 0;
    foreach ($values as $v) {
        $total += $v;
    }
    return $total;
}
```

:::member[default(int)]
Returns 0
:::

:::member[default(float)]
Returns 0.0
:::

:::member[default(string)]
Returns '' (empty string)
:::

:::member[default(bool)]
Returns false
:::

:::member[default(array)]
Returns [] (empty array)
:::

:::member[default(?T)]
Returns null for any nullable type
:::

:::member[default(ClassName)]
Returns null for any object/class type
:::

## variable_exists() — Compile-Time Variable Existence Check

The variable_exists() construct checks if a variable is declared in the current scope. When the compiler can determine the answer statically, it emits a boolean literal (true or false). When the answer depends on runtime conditions, it emits `\array_key_exists('name', \get_defined_vars())` — not `isset()`, so a variable that exists and holds `null` still counts as present.

```tyhp
<?tyhp

string $name = 'Alice';

// Compile-time known: $name is declared, emits true
if (variable_exists($name)) {
    echo $name;
}

// Compile-time known: $undeclared is not declared, emits false
if (variable_exists($undeclared)) {
    // This block is dead code — compiler may warn
}

// Useful for conditional logic based on variable availability
function processConfig(): void {
    if (variable_exists($customHandler)) {
        $customHandler();
    } else {
        defaultHandler();
    }
}
```

```php
<?php

// variable_exists() compiles to true/false literals
// or array_key_exists + get_defined_vars when the answer is not folded
$name = 'Alice';

if (true) {
    echo $name;
}

if (false) {
    // dead code
}

function processConfig(): void {
    if (\array_key_exists('customHandler', \get_defined_vars())) {
        $customHandler();
    } else {
        defaultHandler();
    }
}
```

## Other Compile-Time Features

Beyond the four compile-time constructs above, Tyhp has several other features that are resolved entirely at compile time and produce no runtime overhead. These features exist only during compilation and are erased from the PHP output.

- Type aliases (type MyType = int|string) — resolved during compilation, erased from PHP output. All references to the alias are replaced with the underlying type.
- Structs — compiled to PHP associative arrays. Struct declarations are erased; struct property access compiles to array key access.
- Generics — type parameters are erased via type erasure. Generic annotations produce no PHP code. Optional runtime type tracking is available via the `HasGenerics` trait (`\Tyhp\Concerns\HasGenerics`).
- Static value types (__ClassName, __FunctionName, etc.) — compile-time string subtypes that are erased to plain string in PHP output.
- Type guard return types ($param is Type) — compiled to bool return type in PHP output. The type narrowing information is used only by the checker.
- Function overload signatures — only the implementation body is emitted to PHP. Overload signatures are compile-time declarations for the checker.
- Trait extends/implements requirements — validated at compile time, stripped from PHP output. PHP traits cannot extend or implement.
- The is keyword (alias for instanceof) — compiled to standard PHP instanceof operator.

## Best Practices

:::tip
Use nameof() instead of hardcoded strings when referencing symbol names. It is refactoring-safe — renaming a property, method, or variable automatically updates all nameof() references at compile time.
:::

:::tip
Use default(T) in generic code to get type-appropriate zero values. This eliminates the need for manual default value handling when working with generic type parameters.
:::

:::tip
Use typeof() for runtime type introspection that integrates with Tyhp's \Tyhp\Type system. It provides structured type information instead of raw strings.
:::

:::tip
Use variable_exists() instead of isset() when you need to check whether a variable is declared in scope. variable_exists() is a compile-time construct that the compiler can fold to a boolean literal, or emit as `\array_key_exists(..., \get_defined_vars())`.
:::

## Common Mistakes

:::danger
Passing expressions to nameof(). Only variables, properties, methods, constants, and class names are accepted — not arbitrary expressions like nameof($a + $b) or nameof(getValue()). The compiler reports an error for invalid arguments.
:::

:::danger
Confusing typeof() with PHP's \gettype(). typeof() is a compile-time construct that returns a \Tyhp\Type object. PHP's \gettype() is a runtime function that returns a string like 'integer' or 'string'. They serve different purposes and are not interchangeable.
:::

:::danger
Using default() with non-nullable union types other than nullable. Only nullable types (?T) have a well-defined default (null). Union types like int|string do not have a single default value — the compiler reports an error.
:::

:::danger
Passing an unresolvable symbol reference to nameof(). The argument must refer to a declared symbol in scope. Using nameof($nonExistentVar) or nameof(NonExistentClass) produces a compiler error.
:::

## Compiler Errors

```tyhp
<?tyhp

// ERROR: Cannot resolve symbol 'nonExistentVar'
// echo nameof($nonExistentVar);

// ERROR: Cannot resolve type 'NonExistentClass'
// $type = typeof(NonExistentClass);

// ERROR: Cannot use default() with non-nullable union types
// $val = default(int|string);

// ERROR: nameof() only accepts symbol references
// echo nameof(1 + 2);
// echo nameof(getValue());

// OK: All of these work correctly
string $existingVar = 'hello';
echo nameof($existingVar);     // 'existingVar'
echo default(int);             // 0
$t = typeof(string);           // \Tyhp\Type instance
bool $exists = variable_exists($existingVar); // true
```

:::note
All four compile-time constructs (nameof, typeof, default, variable_exists) are resolved during compilation. They add zero runtime overhead — the PHP output contains only the resolved constant values or simple function calls.
:::
