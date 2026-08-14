---
title: 'Null-Conditional Assignment'
status:
  tier: 3
  story: '26'
  state: planned
---

:::warning Not in this alpha
This feature is **not included** in Tyhp 805.0.0-alpha.1 (roadmap Tier 2/3). The rest of this page describes the planned design. Do not expect these commands or syntax to work yet.
:::


Tyhp extends PHP by allowing assignment through the null-safe operator (?->). In PHP, the null-safe operator can only be used for reading properties and calling methods — using it on the left-hand side of an assignment is a fatal error. Tyhp lifts this restriction: when any ?-> in the chain encounters null, the entire assignment becomes a no-op and the expression evaluates to null. When no nulls are encountered, the assignment proceeds normally.

:::note
Null-conditional assignment eliminates verbose null-checking boilerplate when setting properties on nullable objects. Instead of nested if-null checks, you write a single concise statement.
:::

## Basic Syntax

Use the ?-> operator on the left-hand side of an assignment to conditionally set a property. If the object is null, the assignment is silently skipped. If the object is non-null, the assignment executes normally.

```tyhp
<?tyhp

class Config {
    public string $theme = 'light';
    public int $fontSize = 14;
}

?Config $config = loadConfig();

// If $config is non-null, sets $config->theme to 'dark'
// If $config is null, does nothing
$config?->theme = 'dark';

// Without null-conditional assignment, you'd write:
// if ($config !== null) {
//     $config->theme = 'dark';
// }
```

## Compiled PHP Output

Null-conditional assignments compile to inline ternary expressions with null checks. The ?-> operator is replaced with a regular -> inside the true branch, since the null check has already verified the object is non-null.

```php
<?php
declare(strict_types=1);

class Config {
    public string $theme = 'light';
    public int $fontSize = 14;
}

$config = loadConfig();

// $config?->theme = 'dark';  compiles to:
(($config !== null) ? ($config->theme = 'dark') : null);
```

## Chained Null-Conditional Assignment

Multiple ?-> operators can be chained. If any segment in the chain encounters null, the entire assignment becomes a no-op. Each ?-> generates a nested ternary in the compiled output.

```tyhp
<?tyhp

class Address {
    public string $city = '';
    public ?string $zip = null;
}

class User {
    public string $name = '';
    public ?Address $address = null;
}

?User $user = getUser();

// Chained: sets city only if both $user and $user->address are non-null
$user?->address?->city = 'Berlin';

// Three levels deep
?Company $company = getCompany();
$company?->ceo?->address?->city = 'Munich';
```

```php
<?php
declare(strict_types=1);

// $user?->address?->city = 'Berlin';  compiles to:
(($user !== null) ? (($user->address !== null) ? ($user->address->city = 'Berlin') : null) : null);

// $company?->ceo?->address?->city = 'Munich';  compiles to:
(($company !== null) ? (($company->ceo !== null) ? (($company->ceo->address !== null) ? ($company->ceo->address->city = 'Munich') : null) : null) : null);
```

## Mixed Chains

You can mix regular -> and null-safe ?-> in the same chain. Only the segments with ?-> generate null checks. Regular -> segments are emitted as-is.

```tyhp
<?tyhp

class Department {
    public ?User $manager = null;
}

class Company {
    public Department $department;
}

Company $company = getCompany();

// Only ?-> on manager generates a null check
// $company->department is accessed with regular ->
$company->department?->manager->name = 'Alice';
```

```php
<?php
declare(strict_types=1);

// $company->department?->manager->name = 'Alice';  compiles to:
(($company->department !== null) ? ($company->department->manager->name = 'Alice') : null);
```

## All Assignment Operators

Every assignment operator that works with regular -> also works with ?->. The operator is preserved inside the ternary wrapper.

```tyhp
<?tyhp

?Counter $obj = getCounter();

$obj?->count += 1;
$obj?->count -= 1;
$obj?->count *= 2;
$obj?->count /= 2;
$obj?->count %= 3;
$obj?->count **= 2;
$obj?->label .= ' suffix';
$obj?->label ??= 'default';
$obj?->flags &= 0xFF;
$obj?->flags |= 0x01;
$obj?->flags ^= 0xFF;
$obj?->bits <<= 2;
$obj?->bits >>= 2;
```

```php
<?php
declare(strict_types=1);

(($obj !== null) ? ($obj->count += 1) : null);
(($obj !== null) ? ($obj->count -= 1) : null);
(($obj !== null) ? ($obj->count *= 2) : null);
(($obj !== null) ? ($obj->count /= 2) : null);
(($obj !== null) ? ($obj->count %= 3) : null);
(($obj !== null) ? ($obj->count **= 2) : null);
(($obj !== null) ? ($obj->label .= ' suffix') : null);
(($obj !== null) ? ($obj->label ??= 'default') : null);
(($obj !== null) ? ($obj->flags &= 0xFF) : null);
(($obj !== null) ? ($obj->flags |= 0x01) : null);
(($obj !== null) ? ($obj->flags ^= 0xFF) : null);
(($obj !== null) ? ($obj->bits <<= 2) : null);
(($obj !== null) ? ($obj->bits >>= 2) : null);
```

## Expression Value

A null-conditional assignment is also an expression. It evaluates to the assigned value when the object is non-null, or null when the object is null. The result type is always T|null, where T is the type the assignment would produce if the object were non-null.

```tyhp
<?tyhp

?User $user = getUser();

// Capture the result — string if $user is non-null, null otherwise
string|null $result = ($user?->name = 'Alice');

// Chained — null if any segment is null
string|null $cityResult = ($user?->address?->city = 'Berlin');

// Use in a conditional
if (($user?->name = 'Alice') !== null) {
    echo 'Name was set';
}
```

```php
<?php
declare(strict_types=1);

$result = (($user !== null) ? ($user->name = 'Alice') : null);

$cityResult = (($user !== null) ? (($user->address !== null) ? ($user->address->city = 'Berlin') : null) : null);

if ((($user !== null) ? ($user->name = 'Alice') : null) !== null) {
    echo 'Name was set';
}
```

## Side-Effect Safety

When the object expression has side effects (such as a function call), the compiler ensures it is evaluated only once by introducing a temporary variable. Simple variable accesses do not need a temporary variable.

```tyhp
<?tyhp

// Function call — evaluated only once
getUser()?->name = 'Alice';

// Method call in chain — each side-effecting segment gets a temp variable
$repo?->findUser($id)?->profile?->bio = 'Updated';

// Simple variable — no temp needed
$user?->name = 'Alice';
```

```php
<?php
declare(strict_types=1);

// getUser() is evaluated once via temp variable
(($__tyhp_nsa_0 = getUser()) !== null ? ($__tyhp_nsa_0->name = 'Alice') : null);

// Each side-effecting sub-expression gets its own temp variable
(($repo !== null) ? (($__tyhp_nsa_0 = $repo->findUser($id)) !== null ? (($__tyhp_nsa_0->profile !== null) ? ($__tyhp_nsa_0->profile->bio = 'Updated') : null) : null) : null);

// Simple variable — no temp needed
(($user !== null) ? ($user->name = 'Alice') : null);
```

## Null-Conditional with Null Coalesce Assignment

The ??= operator works naturally with null-conditional assignment. If the object is non-null, ??= sets the property only when its current value is null.

```tyhp
<?tyhp

?Config $config = loadConfig();

// Set theme to 'light' only if $config is non-null AND $config->theme is null
$config?->theme ??= 'light';

// Chained with ??=
$config?->override?->fontSize ??= 14;
```

```php
<?php
declare(strict_types=1);

(($config !== null) ? ($config->theme ??= 'light') : null);

(($config !== null) ? (($config->override !== null) ? ($config->override->fontSize ??= 14) : null) : null);
```

## No Type Narrowing After Null-Conditional Assignment

A null-conditional assignment does not narrow the object variable's type. After the assignment, the object is still nullable because the assignment may or may not have executed. This is different from an explicit null check, which does narrow the type.

```tyhp
<?tyhp

?User $user = getUser();

$user?->name = 'Alice';
// $user is still ?User here — NOT narrowed to User
// The assignment may or may not have executed

// Compare with explicit null check:
if ($user !== null) {
    $user->name = 'Alice';
    // $user IS narrowed to User inside this block
}
```

:::warning
Null-conditional assignment does not prove the object is non-null. The variable remains nullable after the statement. If you need narrowing, use an explicit null check instead.
:::

## Struct Interaction

Null-conditional assignment works with Tyhp structs. Since structs are backed by arrays at runtime, struct property access compiles to array key access. The null-safe wrapper applies around the array assignment.

```tyhp
<?tyhp

struct Point {
    int $x;
    int $y;
}

?Point $p = getPoint();
$p?->x = 10;
```

```php
<?php
declare(strict_types=1);

// Struct property access emits as array key access
(($p !== null) ? ($p['x'] = 10) : null);
```

## Null-Safe on Non-Nullable Types

Using ?-> on a non-nullable object is syntactically valid and semantically safe — it is equivalent to using ->. The compiler accepts it without error but may emit an informational hint suggesting you use -> instead, since the null check is unnecessary.

```tyhp
<?tyhp

User $user = new User();

// Valid but unnecessary — $user is non-nullable
// The compiler may suggest using -> instead
$user?->name = 'Alice';

// Preferred — use -> for non-nullable objects
$user->name = 'Alice';
```

:::note
Info TYHP4060: "Null-safe operator '?->' used on non-nullable type 'User'. Consider using '->' instead." This is an informational hint, not an error.
:::

## Practical Example: Optional Configuration

Null-conditional assignment is particularly useful for optional configuration objects and builder patterns where the target may or may not exist.

```tyhp
<?tyhp

class DatabaseConfig {
    public string $host = 'localhost';
    public int $port = 3306;
    public ?DatabaseConfig $replica = null;
}

function applyOverrides(?DatabaseConfig $config, array<string, mixed> $overrides): void {
    $config?->host = $overrides['host'] ?? 'localhost';
    $config?->port = $overrides['port'] ?? 3306;

    // Apply to replica if it exists
    $config?->replica?->host = $overrides['replica_host'] ?? 'localhost';
    $config?->replica?->port = $overrides['replica_port'] ?? 3306;
}
```

```php
<?php
declare(strict_types=1);

class DatabaseConfig {
    public string $host = 'localhost';
    public int $port = 3306;
    public ?DatabaseConfig $replica = null;
}

function applyOverrides(?DatabaseConfig $config, array $overrides): void {
    (($config !== null) ? ($config->host = $overrides['host'] ?? 'localhost') : null);
    (($config !== null) ? ($config->port = $overrides['port'] ?? 3306) : null);

    (($config !== null) ? (($config->replica !== null) ? ($config->replica->host = $overrides['replica_host'] ?? 'localhost') : null) : null);
    (($config !== null) ? (($config->replica !== null) ? ($config->replica->port = $overrides['replica_port'] ?? 3306) : null) : null);
}
```

## Compiler Error Examples

Null-conditional assignment reuses the same type checking as regular assignment. Type mismatches produce the same errors — the null-safe wrapper is transparent to error reporting.

```tyhp
<?tyhp

class User {
    public string $name;
    public int $age;
}

?User $user = getUser();

// ERROR: int is not assignable to string
// $user?->name = 42;

// ERROR: string is not assignable to int
// $user?->age = 'not a number';

// ERROR: += requires numeric, but $name is string
// $user?->name += 1;

// ERROR: string|null is not assignable to string
// (expression type is string|null due to null-conditional wrapper)
// string $result = ($user?->name = 'Alice');
```

:::danger
The expression type of a null-conditional assignment is always T|null. Assigning it to a non-nullable variable is a type error. Use string|null (or ?string) for the receiving variable.
:::

## Best Practices

:::tip
Use null-conditional assignment to safely set properties on nullable objects. It replaces verbose if-null boilerplate with a single concise statement.
:::

:::tip
Use null-conditional assignment for optional configuration: $config?->setting = value is clean and self-documenting.
:::

:::tip
Use chained null-conditional assignment to safely navigate deep object graphs: $order?->customer?->address?->city = 'Berlin' handles any null in the chain gracefully.
:::

:::tip
Combine with ??= for safe defaulting: $obj?->prop ??= 'fallback' sets the property to 'fallback' only if the object exists and the property is currently null.
:::

:::tip
When capturing the result of a null-conditional assignment, always declare the variable as T|null to account for the null case.
:::

## Common Mistakes

:::danger
Don't expect null-conditional assignment to throw or report an error when the object is null — it silently skips the assignment. If you need to know whether the assignment happened, check the expression result for null.
:::

:::danger
Don't use ?-> on non-nullable types — it is unnecessary and adds a runtime null check for no reason. Use regular -> instead.
:::

:::danger
Don't assume the object is narrowed to non-null after a null-conditional assignment. The variable remains nullable because the assignment is conditional.
:::

:::danger
Don't assign the result of a null-conditional assignment to a non-nullable variable. The result is always T|null, so the receiving variable must be nullable.
:::

:::danger
Don't forget that compound operators (+=, .=, etc.) still perform their type checks. $obj?->stringProp += 1 is a type error because += requires a numeric type.
:::
