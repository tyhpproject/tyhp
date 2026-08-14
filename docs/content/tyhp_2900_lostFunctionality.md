---
title: 'Lost and Changed Functionality in Tyhp'
status:
  tier: 0
  story: '10'
  state: complete
---

While Tyhp is a superset of PHP and maintains broad compatibility, some PHP features are disabled or restricted to improve type safety, security, and static analysis. This page documents disabled features, restricted features, and preserved PHP features — along with the Tyhp alternatives for each restriction.

## Disabled Features

The following PHP features are disabled or strongly discouraged in Tyhp. Using them produces a compiler diagnostic as noted. For each, a Tyhp alternative is provided.

## eval() — Informational (TYHP4800)

The eval() function is discouraged in Tyhp. It is a security risk (arbitrary code execution) and makes static analysis impossible — the compiler cannot type-check dynamically generated code. Using eval() produces **informational** diagnostic TYHP4800 (`CheckerEvalUsage`), not an error. Set `build.allowEval: true` in `tyhp.json` to suppress it.

```tyhp
<?tyhp

// INFO 4800: eval() usage (suppressed when build.allowEval is true)
// eval('echo "Hello";');

// ALTERNATIVE: Write the dynamic code in a PHP file
// and declare it via a tyhpdef file
// Or use closures/callables for dynamic behavior
```

If you absolutely need eval-like functionality, write that code in a plain PHP file and declare it into your Tyhp project via a tyhpdef file. This keeps the security boundary clear and the eval usage isolated from type-checked code. `build.allowEval` only suppresses the informational diagnostic; it does not change how eval behaves at runtime.

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

## Catch Types

PHP 8 requires every `catch` clause to name an exception type — untyped `catch ($e)` is not a PHP feature. Tyhp's checker validates the types that are present: they must be `\Throwable` (or a subtype), not scalars, and not intersection types.

```tyhp
<?tyhp

try {
    riskyOperation();
} catch (\Throwable $e) {
    echo $e->getMessage();
}

try {
    riskyOperation();
} catch (\InvalidArgumentException $e) {
    // Handle specific error
} catch (\Throwable $e) {
    // Catch everything else
}
```

## Variable Variables ($$var) — Disabled

Variable variables are prohibited (TYHP4133). There is no `__VarName` exception. Use `variable_exists($name)` on a simple variable when you need an existence check.

```tyhp
<?tyhp

string $greeting = 'Hello';

// ERROR TYHP4133: variable variables are prohibited
string $name = 'greeting';
// echo $$name;

// OK: existence check on a simple variable
if (variable_exists($greeting)) {
    echo $greeting;
}
```

## Restricted Features (Require Type Narrowing)

The following PHP dynamic features are still available in Tyhp but require type narrowing through type guard functions or the special __ types before use. This ensures the compiler can verify the referenced symbols exist. See the Dynamic Language Features page for full details.

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

## include/require — Disabled

`include` / `include_once` / `require` / `require_once` **always** produce compiler error TYHP4801 (`CheckerIncludeNotAllowed`). There is no static-path exception. The emitter injects `vendor/autoload.php` itself; a user-written `require_once __DIR__ . '/vendor/autoload.php'` still fails 4801. There is no `import` keyword — use `use` plus Composer autoloading.

```tyhp
<?tyhp

// ERROR 4801: include/require are not allowed
string $file = getIncludeFile();
// include $file;

// ERROR 4801: even a static Composer autoload path is rejected
// require_once __DIR__ . '/vendor/autoload.php';

// ALTERNATIVE: `use` the type; Composer (and the emitter's autoload injection) loads it
use App\Models\User;
use App\Services\UserService;
use App\Services\OrderService;
```

## extract() and compact() — Disabled

The extract() and compact() functions dynamically create or collect variables, which completely bypasses static analysis. The compiler reports **errors** TYHP4136 (`CheckerExtractProhibited`) and TYHP4135 (`CheckerCompactProhibited`).

```tyhp
<?tyhp

// ERROR TYHP4136: extract() is prohibited
// \extract($data);

// ALTERNATIVE: Destructure explicitly
string $name = $data['name'];
int $age = $data['age'];

// ERROR TYHP4135: compact() is prohibited
// $result = \compact('name', 'age');

// ALTERNATIVE: Build the array explicitly
array $result = ['name' => $name, 'age' => $age];
```

## global $var — Warning (TYHP4137)

The `global` keyword is not unrestricted. Using `global $var` produces warning TYHP4137 (`CheckerGlobalVariableWarning`). Prefer importing PHP globals through tyhpdef (see Importing PHP Variables and Constants) or passing values as parameters.

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

- eval() — INFO 4800 — Security risk, impossible to type-check — suppressed by `build.allowEval`; alternative: write PHP and declare via tyhpdef
- Short open tags (<?, <?=) — DISABLED — Use <?tyhp instead
- Dynamic properties — DISABLED — Use declared properties or __get/__set
- Nested named functions/methods — DISABLED — Use a private method or a closure assigned to a typed local
- Variable variables ($$var) — DISABLED — TYHP4133; use `variable_exists($name)` on a simple variable
- Variable functions ($fn()) — RESTRICTED — Requires __FunctionName or \function_exists() guard
- Dynamic property access ($obj->$prop) — RESTRICTED — Requires __PropertyName<T> or \property_exists() guard
- Dynamic method calls ($obj->$method()) — RESTRICTED — Requires __MethodName or \method_exists() guard
- Dynamic class instantiation (new $cls()) — RESTRICTED — Requires __ClassName or \class_exists() guard
- include/require — DISABLED — Always TYHP4801, including `vendor/autoload.php`; use `use` + Composer
- extract()/compact() — DISABLED — Errors TYHP4136 / TYHP4135; use explicit variable assignment/array building
- `global $var` — WARNING TYHP4137 (`CheckerGlobalVariableWarning`) — not unrestricted
- Catch types — checker validates present catch types (must be `\Throwable`); untyped `catch ($e)` is not a PHP 8 feature

## Best Practices

:::tip
Use the Tyhp alternatives for disabled features: declared properties instead of dynamic properties, `use` plus Composer instead of include/require, and `\Throwable` catch types.
:::

:::tip
For restricted dynamic features, use the type guard functions (\class_exists(), \function_exists(), \method_exists(), \property_exists()) to narrow types before dynamic access. The compiler recognizes these as type guards.
:::

:::tip
Prefer static access patterns over dynamic ones. Direct property access ($obj->name), method calls ($obj->method()), and class instantiation (new ClassName()) provide full compile-time type checking.
:::

## Common Mistakes

:::danger
Using eval() without intending to. eval() is an informational diagnostic (TYHP4800), suppressed by `build.allowEval`. If you need dynamic code execution, isolate it in a plain PHP file and declare the interface via tyhpdef.
:::

:::danger
Using <? or <?= in Tyhp files. Always use <?tyhp as the opening tag for Tyhp source files.
:::

:::danger
Using dynamic access patterns without type guards. Variable functions, dynamic properties, dynamic methods, and dynamic class instantiation all require type narrowing first. Variable variables (`$$var`) are prohibited (TYHP4133).
:::

:::danger
Omitting type annotations. Unlike PHP, Tyhp requires types on all variables (or inference from first assignment), parameters, return types, and class properties.
:::
