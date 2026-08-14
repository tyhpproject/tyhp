---
title: 'Tyhp Quick Reference'
---

A condensed overview of Tyhp language features. Each section includes a brief explanation and representative code. Features marked **not in this alpha** are planned (Tier 3) and will not compile yet.

## File Tags and Open Tags

Tyhp files use `<?tyhp` (source) and `<?tyhpdef` (type definitions). File extensions are `.tyhp` and `.tyhpdef`. Strict types are always enforced. Use `declare(output_file=...)` to control compiled output paths.

<a href="tyhp_0000_openTag.html">See full documentation →</a>

```tyhp
<?tyhp
declare(output_file='public/index.php');

string $greeting = 'Hello, Tyhp!';
```

## Strongly Typed Variables

Every variable, parameter, property, and return type must have an explicit type or be inferred from its first assignment. All types are non-nullable by default. Prefix with `?` for nullable.

<a href="tyhp_0100_stronglyTyped.html">See full documentation →</a>

```tyhp
string $name = 'Alice';
$count = 42;                      // inferred as int
?string $nickname = null;          // nullable
array<string> $names = ['Alice', 'Bob'];
```

## New and Changed Types

Built-in types gain generic parameters: `array<T>`, `Iterator<K, V>`, `Closure<TArgs..., TReturn>`, `callable<TArgs..., TReturn>`. New types include `decimal` for precise arithmetic and `Promise<T>` for async.

<a href="tyhp_0150_newTypes.html">See full documentation →</a>

```tyhp
decimal $price = 19.99d;
decimal $total = $price + 2.00d;
callable<string, int> $parser;     // accepts string, returns int
Promise<User> $future = fetchUser(42);
```

## Type Narrowing and Guards

Type guard checks (`is_*()`, `instanceof`, `is`, null checks) automatically narrow variable types within the guarded scope. Custom type guard functions use the `$param is Type` return syntax.

<a href="tyhp_0200_typeNarrowingAndGuards.html">See full documentation →</a>

```tyhp
function processValue(mixed $value): string {
    if ($value is string) {
        return $value->toUpper();    // narrowed to string
    }
    if (\is_int($value)) {
        return (string)$value;       // narrowed to int
    }
    return '';
}

// custom type guard
function isNonEmpty(mixed $v): $v is array {
    return \is_array($v) && \count($v) > 0;
}
```

## Generics

Type parameters on classes, interfaces, traits, enums, functions, and methods. Fully erased at compile time. Supports `extends` constraints and default type parameters.

<a href="tyhp_0500_generics.html">See full documentation →</a>

```tyhp
class Box<T> {
    public function __construct(private T $value) {}
    public function getValue(): T { return $this->value; }
}

Box<int> $intBox = new Box<int>(42);

function firstOrNull<T>(array<T> $items): ?T {
    return $items[0] ?? null;
}
```

## Generic Defaults

Generic type parameters can have default types with `=`. Defaults must be trailing and can reference earlier parameters.

<a href="tyhp_3500_genericDefaults.html">See full documentation →</a>

```tyhp
class Collection<T = mixed> { ... }
class Pair<T, U = T> { ... }
class Promise<TReturn extends void|mixed = void> { ... }
```

## Type Aliases

The `type` keyword creates named shortcuts for complex type expressions. Supports generics and class-level aliases with visibility. Fully erased at compile time.

<a href="tyhp_0700_typeAliases.html">See full documentation →</a>

```tyhp
type UserId = int;
type Callback = callable(string, int): bool;
type Optional<T = mixed> = T|null;

class UserService {
    public type UserIdType = int;
}
```

## Static Value Types

Literal types that represent a single specific value. Enable precise unions as lightweight enums and function overloads with literal dispatch.

<a href="tyhp_0800_staticValueTypes.html">See full documentation →</a>

```tyhp
type HttpMethod = 'GET'|'POST'|'PUT'|'PATCH'|'DELETE';
type Color = 'red'|'green'|'blue';
function getExitCode(): 0|1|2 { return 0; }
```

## Structs

Lightweight value types that compile to PHP associative arrays. Support typed properties, defaults, array key aliases (`as`), inheritance, anonymous declarations, and immutable updates with `with`.

<a href="tyhp_0400_structs.html">See full documentation →</a>

```tyhp
struct Point { float $x; float $y; }

Point $p = new Point() with { x => 10.5, y => 20.3 };
Point $p2 = clone $p with { x => 5.0 };

struct ApiResponse {
    int 'status_code' as $statusCode;
    string $body;
}
```

## Short Function Syntax

Use `fn` for single-expression named functions and methods. Compiles to a standard function with an explicit `return`.

<a href="tyhp_1100_shortFunctionSyntax.html">See full documentation →</a>

```tyhp
fn double(int $n): int => $n * 2;
public fn getValue(): int => $this->value;
fn identity<T>(T $value): T => $value;
```

## Function and Method Overloads

Multiple signatures for different parameter types with specialized return types. Only the implementation body is emitted to PHP. Zero-cost compile-time feature.

<a href="tyhp_1200_functionOverloads.html">See full documentation →</a>

```tyhp
function convert(string|int|float $v, true $toInt): int;
function convert(string|int|float $v, false $toInt): float;
function convert(string|int|float $v, bool $toInt = false): int|float {
    // implementation
}
```

## Scalar Pseudo-Objects

Call methods on scalar types (`string`, `int`, `float`, `bool`, `array`) using object syntax. Compiles to PHP function calls with zero overhead.

<a href="tyhp_1000_scalarPseudoObjects.html">See full documentation →</a>

```tyhp
$name->contains('World');         // \str_contains($name, 'World')
$name->trim()->tolower();         // \strtolower(\trim($name))
$numbers->map(fn($n) => $n * 2);  // \array_map(fn($n) => $n * 2, $numbers)
$price->round(2);                 // \round($price, 2)
```

## Extensions

Add methods and operator overloads to existing types without modifying their source. Extension method calls compile to static method calls. Imported via `use extension`.

<a href="tyhp_2100_extensions.html">See full documentation →</a>

```tyhp
extension StringExtensions {
    function toCamelCase(extends string $this): string { ... }
}

use extension App\Extensions\StringExtensions;
$text->toCamelCase();  // StringExtensions::toCamelCase($text)
```

## Object Declaration Changes

Constructors support return types for explicit parent chaining (`: parent(args...)`). Constructor property promotion works as in PHP, including `readonly`.

<a href="tyhp_1300_newObjectDeclSyntax.html">See full documentation →</a>

```tyhp
class Point {
    public function __construct(
        public readonly float $x,
        public readonly float $y
    ) {}
}

class NamedPoint extends Point {
    public function __construct(
        public string $name,
        float $x, float $y
    ): parent($x, $y) {}
}
```

## The `with` Keyword

Sets properties on objects/structs immediately after `new` or `clone`. Combined with `readonly` properties, enables immutable update patterns (`clone $obj with [...]` leaves the original unchanged).

<a href="tyhp_2200_withKeyword.html">See full documentation →</a>

```tyhp
$user = new User('Alice', 30) with [role => 'admin'];
$updated = clone $user with [name => 'Bob', age => 25];
```

## Operator Overloads

Define custom operator behavior on classes and enums. Supports binary, unary, comparison, conversion, and special operators (`true`/`false`/`empty`/`null`). Calls are rewritten to method calls in PHP output.

<a href="tyhp_1600_operatorOverloads.html">See full documentation →</a>

```tyhp
class Vector {
    operator +(self $l, self $r) => new static($l->x + $r->x, $l->y + $r->y);
    operator ==(self $l, self $r) => $l->x === $r->x && $l->y === $r->y;
    operator convert(int $v) { return new static((float)$v); }
}
```

## Trait Requirements

Traits can declare `extends` and `implements` requirements. Enforced at compile time and erased from PHP output.

<a href="tyhp_2000_traitRequirements.html">See full documentation →</a>

```tyhp
trait Cacheable extends Entity implements Serializable {
    // can safely use Entity and Serializable members
}
```

## The `internal` Modifier (not in this alpha)

Restricts access to the defining project (scoped by `tyhp.json`). Emitted as `public` in PHP. Internal symbols are excluded from generated tyhpdef files.

<a href="tyhp_3100_internalModifier.html">See full documentation →</a>

```tyhp
internal class InternalHelper { ... }
internal function computeHash(string $data): string { ... }
internal const int MAX_RETRIES = 3;
```

## Null-Conditional Assignment (not in this alpha)

Assignment through `?->` on the left-hand side. If any `?->` in the chain encounters null, the assignment becomes a no-op. Works with all assignment operators.

<a href="tyhp_3300_nullConditionalAssignment.html">See full documentation →</a>

```tyhp
$config?->theme = 'dark';
$user?->address?->city = 'Berlin';
$obj?->count += 1;
```

## Disposables and the `:=` Operator

Deterministic resource management via `IsDisposable`/`AsyncIsDisposable` interfaces. `:=` (using assignment) auto-disposes on scope exit. `using` blocks provide explicit try/finally disposal.

<a href="tyhp_2300_disposables.html">See full documentation →</a>

```tyhp
$db := new DatabaseConnection($dsn);
// $db->dispose() called automatically at scope exit

using ($db = new DatabaseConnection($dsn)) {
    $db->query('SELECT ...');
}

using await ($conn = new AsyncConnection($url)) { ... }
```

## Async and Await

First-class `async`/`await` using PHP Fibers. Async functions return `Promise<T>`. Includes combinators (`Promise::all`, `race`, `batch`, `timeout`), async iteration, and cancellation tokens.

<a href="tyhp_2600_asyncAndAwait.html">See full documentation →</a>

```tyhp
async function fetchUser(int $id): User {
    return await $repo->find($id);
}

array $results = await Promise::all([$fetchA, $fetchB, $fetchC]);

foreach (await $queue->messagesAsync() as Message $msg) {
    // process messages asynchronously
}
```

## Parsable Lambdas and Expression Trees

When a parameter is typed `PropertyPath<T, R>` or `Expression<T, R>`, an inline `fn` is captured as a data structure instead of compiling to a closure. Enables building SQL queries, validation rules, etc. at runtime.

<a href="tyhp_3000_parsableLambdas.html">See full documentation →</a>

```tyhp
$query->select(fn ($u) => $u->address->city);  // PropertyPath
$query->where(fn ($u) => $u->age > 18 && $u->isActive); // Expression tree

Expression<User, bool> $expr = fn ($u) => $u->age > 18;
```

## The `new<TArgs...>` Type Constraint (not in this alpha)

The `new` built-in type constrains a generic parameter to types with a public constructor accepting specific argument types. Enables type-safe factory patterns and DI containers. Erased to `object` in PHP.

<a href="tyhp_3400_newTypeConstraint.html">See full documentation →</a>

```tyhp
function create<T extends new<string>>(string $value): T {
    return new T($value);
}

function makeLogger<T extends new & Logger>(): T {
    return new T();
}
```

## Compile-Time Constructs

Four compile-time constructs that resolve at compilation with zero runtime cost.

<a href="tyhp_2700_compileTimeConstructs.html">See full documentation →</a>

```tyhp
nameof($user->name);        // 'name' — refactoring-safe string name
typeof(int);                // \Tyhp\Type::int()
default(int);               // 0 — zero value for a type
variable_exists($x);        // true/false literal
```

## Use Statements and Imports

All PHP `use` forms plus generic import aliases (`use Foo as Bar<int>`) and `use extension` for importing extension method providers. Unused imports are auto-pruned.

<a href="tyhp_0350_useStatements.html">See full documentation →</a>

```tyhp
use App\Collections\TypedList as IntList<int>;
IntList $numbers = new IntList();

use extension App\Extensions\StringHelpers;
$camel = $name->toCamelCase();
```

## The Mixed Type

`mixed` is the top type and always requires type guard narrowing before any type-specific operations — there is no setting that relaxes this, and no permissive counterpart (Tyhp has no equivalent of TypeScript's `any`). Prefer generics or union types when the accepted types are known.

<a href="tyhp_0600_theMixedType.html">See full documentation →</a>

## Dynamic Language Features

Variable variables, variable functions, dynamic property/method access, and dynamic instantiation require type narrowing via special `__` types (`__ClassName`, `__FunctionName`, `__PropertyName<T>`, etc.) before use.

<a href="tyhp_2400_dynamicLanguageFeatures.html">See full documentation →</a>

```tyhp
__ClassName $cls = 'App\\Models\\User';
if (\class_exists($cls)) {
    $obj = new $cls();
}
```

## PHP Magic Methods

All PHP magic methods are supported with additional type safety. `mixed`-returning magic methods (`__get`, `__call`, etc.) require type narrowing before use. Extension methods take priority over `__call`. The compiler may generate `__clone()` helpers when `with` updates `readonly` properties.

<a href="tyhp_2500_phpMagicMethods.html">See full documentation →</a>

## Including Files

`include`/`require`/`include_once`/`require_once` all require static string paths or constants — no dynamic includes. The compiler performs PSR-4 output splitting with one PHP file per class.

<a href="tyhp_0300_includingFiles.html">See full documentation →</a>

## Lost and Changed Functionality

Tyhp disables `eval()`, short open tags (`<?`), dynamic properties, and untyped catch blocks. All types must be declared or inferred. All catch blocks require a typed variable. Dynamic features require type narrowing.

<a href="tyhp_2900_lostFunctionality.html">See full documentation →</a>
