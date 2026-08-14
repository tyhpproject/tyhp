---
title: 'Lost and Changed Functionality in Tyhp'
status:
  tier: 0
  story: '10'
  state: complete
---

While Tyhp is a superset of PHP and maintains broad compatibility, some PHP features are disabled or restricted to improve type safety, security, and static analysis. This page documents every disabled feature, every restricted feature, and every preserved PHP feature — along with the Tyhp alternatives for each restriction.

## Disabled Features

The following PHP features are completely disabled in Tyhp. Using them produces a compiler error. For each disabled feature, a Tyhp alternative is provided.

## eval() — Disabled

The eval() function is disabled in Tyhp. It is a security risk (arbitrary code execution) and makes static analysis impossible — the compiler cannot type-check dynamically generated code. Using eval() produces compiler diagnostic 4800.

```tyhp
<?tyhp

// ERROR 4800: eval() usage is disabled in Tyhp
// eval('echo "Hello";');

// ALTERNATIVE: Write the dynamic code in a PHP file
// and import it via a tyhpdef file
// Or use closures/callables for dynamic behavior
```

If you absolutely need eval-like functionality, write that code in a plain PHP file and import it into your Tyhp project via a tyhpdef file. This keeps the security boundary clear and the eval usage isolated from type-checked code.

## Short Open Tags — Disabled

Short open tags (<?) and echo short tags (<?=) are not supported in Tyhp. Tyhp files must use the <?tyhp opening tag. PHP files in the same project can still use <?php.

```tyhp
// ERROR: Short open tags are not valid in Tyhp
// <? echo 'hello'; ?>
// <?= $variable ?>

// CORRECT: Use <?tyhp for Tyhp files
<?tyhp
echo 'hello';
echo $variable;
```

## Dynamic Properties — Disabled

Creating properties dynamically on objects ($obj->newProp = value where newProp is not declared in the class) is disabled. All properties must be declared in the class definition. This aligns with PHP 8.2+'s deprecation of dynamic properties and ensures the compiler can verify all property accesses.

```tyhp
<?tyhp

class User {
    public string $name;
}

User $user = new User();
$user->name = 'Alice';  // OK: declared property

// ERROR: Dynamic property creation is disabled
// $user->email = 'alice@example.com';  // 'email' not declared

// ALTERNATIVE: Declare the property in the class
class UserWithEmail {
    public string $name;
    public string $email;  // declared
}

// Or use __get/__set for controlled dynamic access
class FlexibleUser {
    private array $extra = [];

    public function __get(string $name): mixed {
        return $this->extra[$name] ?? null;
    }

    public function __set(string $name, mixed $value): void {
        $this->extra[$name] = $value;
    }
}
```

## Nested Named Functions — Disabled

PHP allows a named `function` (or, in Tyhp, a named method) to be declared inside the body of
another function or method. PHP hoists the nested declaration to the *global* scope the first time
the enclosing callable runs — it does not close over the enclosing scope like a closure — which
does not fit Tyhp's static, per-file symbol model. Declaring a named function inside another named
function or method produces compiler diagnostic 4802.

```tyhp
<?tyhp

function outer(): void {
    // ERROR 4802: named functions cannot be declared inside another function or method
    // function nested(): void {}
}

class C {
    public function go(): void {
        // ERROR 4802: same restriction applies inside methods
        // function nested(): void {}
    }
}

// ALTERNATIVE: a private method
class Better {
    public function go(): void {
        $this->nested();
    }

    private function nested(): void {}
}

// ALTERNATIVE: a closure assigned to a typed local
function alsoBetter(): void {
    $nested = function (): void {};
    $nested();
}
```

A named function declared at file/namespace scope inside an `if (!\function_exists(...))` guard is
unaffected by this restriction — that pattern does not nest the declaration inside another named
function or method.

## Untyped Catch Blocks — Disabled

PHP allows catch blocks without specifying an exception type (catch ($e) or bare catch). In Tyhp, all catch blocks must specify the exception type. Use \Throwable to catch all exceptions.

```tyhp
<?tyhp

// ERROR: Untyped catch block
// try { ... } catch ($e) { ... }

// CORRECT: Always specify the exception type
try {
    riskyOperation();
} catch (\Throwable $e) {
    // Catches all exceptions and errors
    echo $e->getMessage();
}

// BEST: Catch specific exception types
try {
    riskyOperation();
} catch (\InvalidArgumentException $e) {
    // Handle specific error
} catch (\Throwable $e) {
    // Catch everything else
}
```

## Restricted Features (Require Type Narrowing)

The following PHP dynamic features are still available in Tyhp but require type narrowing through type guard functions or the special __ types before use. This ensures the compiler can verify the referenced symbols exist. See the Dynamic Language Features page for full details.

## Variable Variables ($$var) — Restricted

Variable variables require the variable name to be of type __VarName or to pass a variable_exists() check. The compiler must be able to verify the referenced variable exists.

```tyhp
<?tyhp

string $greeting = 'Hello';

// ERROR: Plain string used for variable variable
string $name = 'greeting';
// echo $$name;  // Compiler error!

// OK: Narrowed to __VarName
__VarName $varName = 'greeting';
echo $$varName;  // OK

// OK: Using variable_exists() guard
if (variable_exists($$name)) {
    echo $$name;  // OK: guard narrows the type
}
```

## Variable Functions ($fn()) — Restricted

Calling a function through a variable requires __FunctionName type or a \function_exists() guard.

```tyhp
<?tyhp

// ERROR: Plain string for variable function
string $fn = 'strtoupper';
// echo $fn('hello');  // Compiler error!

// OK: Narrowed to __FunctionName
__FunctionName $fn = 'strtoupper';
echo $fn('hello');  // OK

// OK: Using function_exists() guard
string $callback = getCallback();
if (\function_exists($callback)) {
    echo $callback('hello');  // OK
}
```

## Dynamic Property/Method Access — Restricted

Dynamic property access ($obj->$prop) and dynamic method calls ($obj->$method()) require the variable to be narrowed to __PropertyName<T> or __MethodName, or to pass \property_exists() or \method_exists() guards.

```tyhp
<?tyhp

class User {
    public string $name = 'Alice';
    public function greet(): string { return 'Hello!'; }
}

User $user = new User();

// ERROR: Plain string for dynamic access
string $prop = 'name';
// echo $user->$prop;  // Compiler error!

// OK: Using property_exists() guard
if (\property_exists($user, $prop)) {
    echo $user->$prop;  // OK
}

// OK: Using __PropertyName<User>
__PropertyName<User> $field = 'name';
echo $user->$field;  // OK
```

## Dynamic Class Instantiation (new $class()) — Restricted

Creating an object from a variable class name requires __ClassName type or a \class_exists() guard.

```tyhp
<?tyhp

// ERROR: Plain string for dynamic instantiation
string $cls = 'App\\Models\\User';
// $obj = new $cls();  // Compiler error!

// OK: Using class_exists() guard
if (\class_exists($cls)) {
    $obj = new $cls();  // OK
}

// OK: Using __ClassName
__ClassName $cls = 'App\\Models\\User';
$obj = new $cls();  // OK
```

## include/require — Restricted

Dynamic include/require with non-constant paths is disabled (compiler error 4801). Only static string paths or constant values are allowed. Tyhp uses import statements and Composer autoloading instead. An exception is made for require_once of Composer's vendor/autoload.php in entry point files.

```tyhp
<?tyhp

// ERROR 4801: Dynamic include path
string $file = getIncludeFile();
// include $file;  // Compiler error!

// OK: Static string path
require_once __DIR__ . '/vendor/autoload.php';

// ALTERNATIVE: Use Tyhp import statements
import App\Models\User;
import App\Services\{UserService, OrderService};
```

## extract() and compact() — Restricted

The extract() and compact() functions dynamically create or collect variables, which completely bypasses static analysis. The compiler emits a warning when these functions are used.

```tyhp
<?tyhp

// WARNING: extract() bypasses static analysis
// \extract($data);  // Compiler warning!

// ALTERNATIVE: Destructure explicitly
string $name = $data['name'];
int $age = $data['age'];

// WARNING: compact() bypasses static analysis
// $result = \compact('name', 'age');  // Compiler warning!

// ALTERNATIVE: Build the array explicitly
array $result = ['name' => $name, 'age' => $age];
```

## Required Typing

Unlike PHP where type declarations are optional, Tyhp requires types everywhere. All variables, parameters, return types, and properties must have explicit type annotations or be typed by inference from the first assignment.

```tyhp
<?tyhp

// All variables must be typed or inferred
string $name = 'Alice';     // explicit type
$age = 30;                  // inferred as int from literal

// ERROR: No type and no initializer
// $unknown;  // Compiler error: type required

// All function parameters must have types
function greet(string $name): string {
    return "Hello, {$name}!";
}

// ERROR: Missing parameter type
// function bad($name): string { ... }  // Compiler error!

// ERROR: Missing return type
// function bad2(string $name) { ... }  // Compiler error!

// All class properties must have types
class User {
    public string $name;      // OK: typed
    // public $email;          // ERROR: missing type
}
```

## Non-Nullable by Default

All types in Tyhp are non-nullable by default. A variable declared as string $x cannot hold null — it must be explicitly declared as ?string $x (or string|null $x) to allow null. This is a fundamental difference from PHP.

```tyhp
<?tyhp

string $name = 'Alice';

// ERROR: Cannot assign null to non-nullable string
// $name = null;  // Compiler error!

// OK: Explicitly nullable
?string $nickname = null;
$nickname = 'Ali';  // OK
$nickname = null;   // OK: nullable

// OK: Union with null
string|null $title = null;
```

## Preserved Features

The following PHP features work exactly the same in Tyhp, with no restrictions or modifications. Tyhp is a superset of PHP — all standard PHP syntax and constructs are preserved.

- All PHP operators and control structures (if/else, for, foreach, while, do-while, switch, match)
- String interpolation and heredoc/nowdoc syntax
- list() and [] destructuring assignment
- Spread operator (...) for arrays and function arguments
- Named arguments in function calls
- Match expressions
- Enums (PHP 8.1+)
- Fibers (PHP 8.1+, used internally by async/await)
- Attributes/Annotations (#[Attribute])
- Readonly properties and readonly classes
- Intersection types (A&B)
- Union types (A|B)
- First-class callable syntax (strlen(...))
- Nullsafe operator (?->)
- References (&$variable)
- Global keyword (global $var)
- Static variables (static $count = 0)
- Goto statements
- Try/catch/finally
- Closures and arrow functions (fn() =>)
- Generators and yield
- Traits
- Interfaces and abstract classes
- Constants (const and define())
- Namespaces

## Summary Table

The following table summarizes the status of each changed feature, why it is restricted, and the Tyhp alternative.

- eval() — DISABLED — Security risk, impossible to type-check — Alternative: Write PHP and import via tyhpdef
- Short open tags (<?, <?=) — DISABLED — Use <?tyhp instead
- Dynamic properties — DISABLED — Use declared properties or __get/__set
- Nested named functions/methods — DISABLED — Use a private method or a closure assigned to a typed local
- Untyped catch blocks — DISABLED — Always specify exception type (use \Throwable)
- Variable variables ($$var) — RESTRICTED — Requires __VarName or variable_exists() guard
- Variable functions ($fn()) — RESTRICTED — Requires __FunctionName or \function_exists() guard
- Dynamic property access ($obj->$prop) — RESTRICTED — Requires __PropertyName<T> or \property_exists() guard
- Dynamic method calls ($obj->$method()) — RESTRICTED — Requires __MethodName or \method_exists() guard
- Dynamic class instantiation (new $cls()) — RESTRICTED — Requires __ClassName or \class_exists() guard
- Dynamic include/require — RESTRICTED — Only static paths allowed; use import statements
- extract()/compact() — RESTRICTED — Warning emitted; use explicit variable assignment/array building

## Best Practices

:::tip
Use the Tyhp alternatives for disabled features: declared properties instead of dynamic properties, import statements instead of include/require, and \Throwable catch types instead of untyped catches.
:::

:::tip
For restricted dynamic features, use the type guard functions (\class_exists(), \function_exists(), \method_exists(), \property_exists()) to narrow types before dynamic access. The compiler recognizes these as type guards.
:::

:::tip
Prefer static access patterns over dynamic ones. Direct property access ($obj->name), method calls ($obj->method()), and class instantiation (new ClassName()) provide full compile-time type checking.
:::

## Common Mistakes

:::danger
Attempting to use eval() in Tyhp. If you need dynamic code execution, isolate it in a plain PHP file and import the interface via tyhpdef.
:::

:::danger
Using <? or <?= in Tyhp files. Always use <?tyhp as the opening tag for Tyhp source files.
:::

:::danger
Using dynamic access patterns without type guards. Variable variables, variable functions, dynamic properties, dynamic methods, and dynamic class instantiation all require type narrowing first.
:::

:::danger
Omitting type annotations. Unlike PHP, Tyhp requires types on all variables (or inference from first assignment), parameters, return types, and class properties.
:::
