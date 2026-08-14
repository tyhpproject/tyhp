---
title: Structs
status:
  tier: 1
  story: '11'
  state: complete
---

Structs are lightweight, schema-based value types in Tyhp. They compile to PHP associative arrays, giving you the performance of arrays with the type safety of named structures. Structs support property types, default values, nullable-as-optional, array key aliases, structural (schema-based) type compatibility, inheritance via extends, and anonymous inline declarations. Unlike classes, structs contain only typed properties — no methods, constants, or static members.

:::note
Struct declarations are completely erased from PHP output. They exist only at compile time for type checking. Instantiation produces an associative array.
:::

## Defining a Struct

A struct is declared with the struct keyword followed by a name and a body containing typed properties. Each property must have an explicit type annotation and ends with a semicolon.

```tyhp
<?tyhp

struct Point {
    float $x;
    float $y;
}

struct UserProfile {
    string $name;
    string $email;
    int $age;
    ?string $bio;  // Nullable = optional, defaults to null
}
```

```php
<?php
// PHP output — struct declarations are erased entirely.
// No PHP code is generated for the struct definition.
```

## Property Types and Defaults

Every struct property must have an explicit type annotation. Type inference is not allowed in struct property declarations. Properties without an initializer (`=`) are **required** at `new` — there are no implicit zeros for non-nullable fields. Omitting a required key is **TYHP CheckerStructRequiredPropertyNotSet**. Nullable properties without an explicit default default to `null` and are optional.

```tyhp
<?tyhp

struct Config {
    string $host;                  // Required — must be set at `new` (no implicit '')
    int $port = 0;                 // Optional — default 0
    ?string $username;             // Optional — nullable defaults to null
    ?string $password;             // Optional — nullable defaults to null
    bool $useSsl = false;          // Optional — default false
    float $timeout = 0.0;          // Optional — default 0.0
}

// ERROR — required $host is missing
// Config $bad = new Config();

Config $ok = new Config() with [host => 'localhost'];
```

## Array Key Aliases

Struct properties can be aliased to arbitrary PHP array keys using the as keyword. The alias may be a quoted string or a decimal integer. This lets you map a clean Tyhp property name to an existing array key that is not a valid PHP variable name — special characters, spaces, nested-looking paths, or numeric indexes. This is particularly useful when working with external data sources like APIs, databases, or positional argument lists.

```tyhp
<?tyhp

struct ApiResponse {
    int 'status_code' as $statusCode;
    string 'error-message' as $errorMessage;
    array 'data.items' as $items;
}

struct CallbackArgs {
    mixed 0 as $arg1;
    ?mixed 1 as $arg2;
}

ApiResponse $response = new ApiResponse() with [
    statusCode => 0,
    errorMessage => '',
    items => [],
];
$response->statusCode = 200;     // Accesses $response['status_code']
$response->errorMessage = '';     // Accesses $response['error-message']

CallbackArgs $args = new CallbackArgs() with [arg1 => "hello"];
$first = $args->arg1;            // Accesses $args[0]
```

```php
<?php
// PHP output — struct property access becomes array key access
// String aliases use the quoted key; integer aliases use a numeric key

$response = ['status_code' => 0, 'error-message' => '', 'data.items' => []];
$response['status_code'] = 200;
$response['error-message'] = '';

$args = [0 => "hello"];
$first = $args[0];
```

## Instantiation

Structs are instantiated with the new keyword. Properties without `=` must be set in the `with` clause; `new Point()` is an error if `$x` and `$y` are required. When every value is known at compile time, `new Point() with [...]` folds to a single array literal. Otherwise `with` emits `\array_replace`.

```tyhp
<?tyhp

struct Point {
    float $x;
    float $y;
}

// ERROR — required x and y are not set
// Point $origin = new Point();

// Instantiate with overrides using the with keyword
Point $p = new Point() with [
    x => 10.5,
    y => 20.3
];

struct Origin {
    float $x = 0.0;
    float $y = 0.0;
}

Origin $origin = new Origin();  // OK — defaults fill x and y
```

```php
<?php
// PHP output — struct instantiation becomes array creation
// Known defaults + with fold to one array (no array_replace)

$p = ['x' => 10.5, 'y' => 20.3];

$origin = ['x' => 0.0, 'y' => 0.0];
```

## Property Access

In Tyhp, you access struct properties using the arrow operator (->), just like object properties. The compiler rewrites these to array key access in the PHP output.

```tyhp
<?tyhp

struct User {
    string $name;
    int $age;
}

User $user = new User() with [name => 'Alice', age => 30];

// Read access
string $n = $user->name;
int $a = $user->age;

// Write access
$user->age = 31;
```

```php
<?php
// PHP output — arrow access compiles to array key access
// Folded new ... with [...] when defaults are known

$user = ['name' => 'Alice', 'age' => 30];

$n = $user['name'];
$a = $user['age'];

$user['age'] = 31;
```

## Schema-Based Type Compatibility

Struct types use structural (schema-based) compatibility, not nominal compatibility. Two struct types are compatible if the source struct has all the properties required by the target struct with compatible types. Extra properties on the source are allowed. This is the opposite of classes, which use nominal (name-based) typing.

```tyhp
<?tyhp

struct Named {
    string $name;
}

struct Person {
    string $name;
    int $age;
}

Person $person = new Person() with [name => 'Alice', age => 30];

// OK — Person has all properties that Named requires
Named $named = $person;

// ERROR — Named does not have the 'age' property that Person requires
// Person $other = $named;  // Compile error
```

## Struct Extends

A struct can extend another struct to inherit its properties. The child struct includes all properties from the parent and can add additional properties.

```tyhp
<?tyhp

struct BaseEntity {
    int $id;
    string $createdAt;
}

struct UserEntity extends BaseEntity {
    string $name;
    string $email;
}

// UserEntity has: $id, $createdAt, $name, $email
UserEntity $user = new UserEntity() with [
    id => 1,
    createdAt => '2025-01-01',
    name => 'Alice',
    email => 'alice@example.com'
];
```

```php
<?php
// PHP output — inherited properties are included in the array

$user = ['id' => 1, 'createdAt' => '2025-01-01', 'name' => 'Alice', 'email' => 'alice@example.com'];
```

## Generic Structs

Structs can declare generic type parameters, the same way classes do. Parameters may carry constraints and defaults (generic defaults are the same Story 28 feature as on classes). Instantiation and type annotations supply type arguments; those arguments are substituted into property types (including inherited properties when `extends` passes type arguments to the parent). Like all struct machinery, generics are compile-time only — the PHP output is still a plain array with no runtime generic tracking.

```tyhp
<?tyhp

struct Box<T> {
    T $value;
}

struct CallableArgs1<T1> {
    T1 0 as $_1;
}

struct CallableArgs2<T1, T2> extends CallableArgs1<T1> {
    T2 1 as $_2;
}

Box<int> $box = new Box<int>() with [value => 42];
int $n = $box->value;

CallableArgs2<string, int> $args = new CallableArgs2<string, int>() with [
    _1 => "hello",
    _2 => 42,
];
string $first = $args->_1;  // inherited; type is string
```

```php
<?php
// PHP output — generics and the struct name are erased

$box = ['value' => 42];
$n = $box['value'];

$args = [0 => "hello", 1 => 42];
$first = $args[0];
```

## Anonymous Structs

Structs can be declared inline without a name. Anonymous structs are useful for one-off data shapes, such as function return types or local data groupings.

```tyhp
<?tyhp

// Anonymous struct as a variable type
struct { string $name; int $age; } $person = new struct {
    string $name;
    int $age;
} with [name => 'Alice', age => 30];

// Anonymous struct as a function return type
function getCoords(): struct { float $lat; float $lng; } {
    return new struct {
        float $lat;
        float $lng;
    } with [lat => 40.7128, lng => -74.0060];
}
```

```php
<?php
// PHP output — anonymous structs become inline arrays

$person = ['name' => 'Alice', 'age' => 30];

function getCoords(): array {
    return ['lat' => 40.7128, 'lng' => -74.0060];
}
```

## Immutable Updates with 'with'

The with keyword creates a modified copy of a struct. Since structs compile to arrays and arrays are value types in PHP, the original struct is not modified. This enables an immutable update pattern.

```tyhp
<?tyhp

struct Point {
    float $x;
    float $y;
}

Point $p1 = new Point() with [x => 1.0, y => 2.0];

// Create a modified copy — $p1 is unchanged
Point $p2 = clone $p1 with [x => 5.0];
// $p2 = {x: 5.0, y: 2.0}
// $p1 = {x: 1.0, y: 2.0} — unchanged

// clone $p on a struct with no `with` is a no-op (arrays copy on assignment)
Point $copy = clone $p1;  // emits: return $p1;  (or `$copy = $p1`)
```

```php
<?php
// PHP output — 'clone with' on structs becomes array_replace
// Bare clone is a no-op

$p1 = ['x' => 1.0, 'y' => 2.0];

$p2 = \array_replace($p1, ['x' => 5.0]);
```

## In-Place Updates with 'with'

The with keyword can also be applied directly to an existing struct value — without new or clone — to update properties in place. For structs this is a convenient shorthand for reassigning `\array_replace` of the existing array back to the same variable. Because structs are value types backed by arrays, only the variable being updated is affected; any prior copies remain unchanged.

```tyhp
<?tyhp

struct Point {
    float $x;
    float $y;
}

Point $p = new Point() with [x => 1.0, y => 2.0];

// In-place update — $p is reassigned with the new value of x
$p with [x => 9.0];
// $p = {x: 9.0, y: 2.0}
```

```php
<?php
// PHP output — in-place 'with' on a struct is an array replace

$p = ['x' => 1.0, 'y' => 2.0];

$p = \array_replace($p, ['x' => 9.0]);
```

:::note
All three forms of with on a struct — after new, after clone, and in place on an existing value — emit as `\array_replace` when the result cannot be folded. `new Point() with [...]` folds to a single array literal when every value is known at compile time. Bare `clone $p` on a struct is a no-op (`$p` is returned as-is).
:::

## Structs as Function Parameters

Structs can be used as function parameter types and return types. The compiler validates that the passed arguments match the struct schema at compile time.

```tyhp
<?tyhp

struct Dimensions {
    float $width;
    float $height;
}

function calculateArea(Dimensions $dims): float {
    return $dims->width * $dims->height;
}

Dimensions $box = new Dimensions() with [width => 10.0, height => 5.0];
float $area = calculateArea($box);  // 50.0
```

```php
<?php
// PHP output — struct parameter type is erased to array

function calculateArea(array $dims): float {
    return $dims['width'] * $dims['height'];
}

$box = ['width' => 10.0, 'height' => 5.0];
$area = calculateArea($box);
```

## Best Practices

:::tip
Use structs for lightweight data containers that don't need methods or behaviour — data transfer objects (DTOs), configuration shapes, and API response/request types are ideal use cases.
:::

:::tip
Use array key aliases when working with external data sources (APIs, databases) that use non-standard key names like 'status-code' or 'data.items'.
:::

:::tip
Take advantage of structural compatibility — accept a struct type with just the properties you need. A function that only needs a name can accept any struct that has a string $name property.
:::

:::tip
Use extends to compose structs with shared properties. Create a base struct like BaseEntity with $id and $createdAt, then extend it for specific entities.
:::

:::tip
Use the with keyword to create modified copies instead of mutating structs in place. This leads to predictable, immutable data flow.
:::

## Common Mistakes

:::danger
Don't omit required struct properties at `new`. A non-nullable property without `=` has no implicit zero; `new Point()` is an error unless every required key is set via `with` or has a default.
:::

:::danger
Don't add methods, constants, or static members to structs — they are data-only. Use classes for types that need behaviour.
:::

```tyhp
<?tyhp

// ERROR — structs cannot contain methods
// struct BadStruct {
//     int $value;
//     function getValue(): int {  // Compile error!
//         return $this->value;
//     }
// }
```

:::danger
Don't omit type annotations on struct properties — every property must have an explicit type. Type inference is not allowed in struct declarations.
:::

```tyhp
<?tyhp

// ERROR — missing type annotation
// struct BadStruct {
//     $name;  // Compile error! Type required
// }

// CORRECT
struct GoodStruct {
    string $name;
}
```

:::danger
Don't use visibility modifiers (public, protected, private) on struct properties — all struct properties are public. Don't use implements on structs — structs cannot implement interfaces.
:::

:::danger
Don't rely on reference semantics — structs are value types (backed by arrays), so assigning a struct to another variable creates a copy, not a reference.
:::
