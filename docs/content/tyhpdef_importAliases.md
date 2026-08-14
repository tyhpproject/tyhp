---
title: 'Import Aliases in Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Almost anything declared in a tyhpdef file can be aliased using the as keyword. Aliasing lets you expose PHP items under different names in Tyhp — renaming functions, classes, methods, properties, constants, and even enum cases. When an item is aliased, only the aliased name is available in Tyhp code; the original name is not accessible unless it is also imported separately without an alias. You can import the same PHP item multiple times with different aliases.

## Use Statement Aliases

Tyhpdef supports standard PHP use statement syntax for importing and aliasing classes, functions, and constants from other namespaces. This includes single imports, aliased imports, and grouped imports.

```tyhp
<?tyhpdef

use App\Models\User;
use App\Models\Post as BlogPost;

use function App\Helpers\formatDate;
use function App\Helpers\sanitize as clean;

use const App\Config\MAX_RETRIES;
use const App\Config\DEFAULT_TIMEOUT as TIMEOUT;
```

## Group Use Declarations

Multiple imports from the same namespace can be grouped using brace syntax, just like in PHP.

```tyhp
<?tyhpdef

use App\Models\{
    User,
    Post as BlogPost,
    Comment
};

use function App\Helpers\{
    formatDate,
    sanitize as clean,
    truncate
};
```

## Function Aliases

Functions can be aliased at the declaration level. The original PHP function name comes first, then as, then the name Tyhp code will use.

```tyhp
<?tyhpdef

function \testEmail as test_email(string $emailAddress): bool;

function \array_key_exists as keyExists(
    int|string $key,
    array $array
): bool;

// Generic function with alias
function \array_map<T, U> as map(
    callable<T, U> $callback,
    array<T> $array
): array<U>;
```

## Variable and Constant Aliases

```tyhp
<?tyhpdef

int $myGlobalPHPVar as $myGlobalTyhpVar;

const int \MAX_LOOPS_ALLOWED as MAX_LOOPS;

const string \APP_VERSION as VERSION;
```

## Class, Interface, Trait, and Enum Aliases

Object type declarations can be aliased at the declaration level. The original PHP name is specified with as followed by the alias name that Tyhp will use.

```tyhp
<?tyhpdef

class Guest as Ghost implements Person {
    protected string $placeOfEntry as $placeOfHaunt;

    public function isFirstVisit as justDied(): bool;
}

interface BasketInterface as BucketInterface {}

trait HasTools as isHandyman {}

enum USAStates as CountryProvinces {}
```

## Member Aliases Within Classes

Properties, methods, and constants within a class or interface declaration can be individually aliased. This lets you rename members without changing the underlying PHP class.

```tyhp
<?tyhpdef

class LegacyApi as Api {
    public string $usr_nm as $username;
    public string $eml_addr as $email;
    protected int $crt_dt as $createdAt;

    public function get_usr as getUser(int $id): ?User;
    public function upd_usr as updateUser(int $id, User $data): bool;
    public function del_usr as deleteUser(int $id): bool;
}
```

## Enum Aliases with Backing Types

An enum can be imported multiple times with different aliases, and can even be given a backing type that differs from the original PHP enum definition.

```tyhp
<?tyhpdef

enum BadgeType as BackedBadgeType: string implements PersonalInfoAttachmentInterface {
    case GUEST;
    case EMPLOYEE;
    case MANAGER;
    case OWNER = 'o';

    public function canAccessBreakRoom as canAccessServerRoom(): bool;
}
```

## Extension Import Aliases

Extension classes (Tyhp's extension method feature) can be imported with use extension and aliased using trait adaptation syntax.

```tyhp
<?tyhpdef

use extension StringExtensions {
    StringExtensions::toCamelCase as toCC;
    StringExtensions::toSnakeCase as toSC;
}
```

## Alias Restrictions

Do not alias these — PHP dispatches them by their real names. This alpha does **not** reject the aliases at compile time:

- A class's `__construct` or `__destruct` method
- Any magic methods (`__get`, `__set`, `__isset`, `__unset`, `__call`, `__callStatic`, `__sleep`, `__wakeup`, `__toString`, `__invoke`, `__clone`, etc.)

:::tip
DO: Use aliases to give cleaner, more readable names to legacy PHP APIs that use abbreviations or inconsistent naming.
:::

:::tip
DO: Import the same PHP item with multiple aliases when you need different type views of the same underlying object (e.g., an enum with and without a backing type).
:::

:::danger
DON'T: Alias magic methods or constructors. PHP still dispatches on the original names; this alpha does not reject those aliases for you.
:::

:::danger
DON'T: Include namespace separators in alias names. Aliases must be simple identifiers — you cannot alias an item into a sub-namespace with the as keyword.
:::

## Summary

- When an item is aliased, only the aliased name is available in Tyhp
- The same PHP item can be imported multiple times with different aliases
- Items imported multiple times with different aliases are treated as distinct types in Tyhp
- Tyhp compiles back to the original PHP names — aliases are a compile-time concept
- Alias names must be simple identifiers (no namespace separators)
- Use statement aliases follow standard PHP `use ... as ...` syntax
- Group use declarations with aliases are supported
- Class members (properties, methods, constants) can be individually aliased
