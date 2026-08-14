---
title: 'The deprecated and obsolete Keywords in Tyhpdef'
status:
  tier: 1
  story: '14'
  state: complete
---

Tyhpdef provides two keywords for marking imported declarations as outdated: deprecated and obsolete. These keywords control how the Tyhp compiler responds when code references marked items. The deprecated keyword generates compiler warnings, encouraging migration away from the item while still allowing its use. The obsolete keyword generates compiler errors, hard-blocking usage entirely. Both keywords can be applied to nearly any **top-level** declaration — functions, classes, interfaces, traits, enums, constants, and variables. Member-level `deprecated` / `obsolete` (methods, properties, enum cases) parse in this alpha but are **not** copied onto symbols, so they do not warn or error.

## The deprecated Keyword

When an item is marked as deprecated, any reference to it in Tyhp code produces a compiler warning. The code still compiles and runs, but the warning signals that the item should be replaced with a newer alternative.

```tyhp
<?tyhpdef

deprecated function \mysql_connect(
    string $server,
    string $username,
    string $password
): \mysqli|false;

deprecated function \mysql_query(
    string $query,
    \mysqli $link
): \mysqli|false;

deprecated const int SORT_REGULAR;

deprecated class LegacyLogger {
    public function log(string $message): void;
}
```

## The obsolete Keyword

When an item is marked as obsolete, any reference to it in Tyhp code produces a compiler error. The code will not compile. Use obsolete for items that must never be used — such as functions with known security vulnerabilities or interfaces that have been completely replaced.

```tyhp
<?tyhpdef

obsolete function \md5(string $string, bool $binary = false): string;

obsolete interface OldDataStore {
    public function persist(Entity $entity): void;
    public function save(Entity $entity): void;
}

obsolete class UnsafeSerializer {
    public function serialize(mixed $data): string;
    public function unserialize(string $data): mixed;
}
```

## Keyword Placement

The deprecated or obsolete keyword must appear before all other modifiers on a declaration. For class members, the grammar allows it before visibility, but this alpha only **enforces** the keywords on top-level symbols.

```tyhp
<?tyhpdef

deprecated class LegacyLogger {
    public function log(string $message): void;
}

class UserService {
    // Parsed, not enforced in this alpha:
    // deprecated public function getUser(int $id): ?User;
    public function findById(int $id): ?User;
}
```

## Applying to Different Declaration Types

Both keywords can be applied to any of the following declaration types:

- Functions (global and namespaced)
- Classes, interfaces, traits, and enums (the type itself)
- Top-level constants and variables
- Top-level function overloads (each overload is its own top-level declaration)

Not enforced yet: methods, properties, class constants, and enum cases.

```tyhp
<?tyhpdef

deprecated enum LegacyStatus {
    case Active;
    case Inactive;
    case Deleted;
    case Archived;
}

// Deprecated trait
deprecated trait SingletonPattern {
    public static function getInstance(): static;
}

// Deprecated interface
deprecated interface Cacheable {
    public function getCacheKey(): string;
    public function getCacheTtl(): int;
}
```

## Deprecating Specific Overloads

When a function has multiple **top-level** overloads, you can deprecate individual overloads without affecting the others. Member-level overload markers are not enforced in this alpha.

```tyhp
<?tyhpdef

// Only the string-based connection is deprecated
deprecated function \connectDb(string $connectionString): DbConnection;
function \connectDb(DbConfig $config): DbConnection;
```

## Class-Level vs. Member-Level Deprecation

When a class itself is marked as deprecated, using the class in any way (instantiation, type hints, extends, implements) triggers a warning. Member-level `deprecated` on methods inside the class is parsed but not enforced in this alpha.

```tyhp
<?tyhpdef

deprecated class LegacyCache {
    public function get(string $key): mixed;
    public function getMultiple(array<string> $keys): array<mixed>;
}
```

## Effect in Tyhp Code

```tyhp
<?tyhp

// Warning: LegacyLogger is deprecated
LegacyLogger $logger = new LegacyLogger();

// OK: class is not deprecated (member-level deprecated is not enforced yet)
UserService $svc = new UserService();
?User $user = $svc->getUser(123);

// Error: OldDataStore is obsolete -- will not compile
// class Storage implements OldDataStore { }
```

:::tip
DO: Use deprecated for items that have a replacement but may still be in use in existing code. This allows gradual migration with compiler warnings guiding the process.
:::

:::tip
DO: Use obsolete for items that are dangerous, insecure, or fundamentally broken. This ensures the compiler hard-blocks their usage.
:::

:::danger
DON'T: Place deprecated or obsolete after visibility modifiers. The keyword must always come first: deprecated public function, not public deprecated function.
:::

:::danger
DON'T: Use obsolete for items that are merely old or slow. Reserve obsolete for items that should genuinely never be used. Use deprecated for soft discouragement.
:::

## Summary

- `deprecated` generates a compiler warning each time the item is referenced
- `obsolete` generates a compiler error, preventing compilation entirely
- Both keywords must appear before all other modifiers on a declaration
- Can be applied to top-level functions, classes, interfaces, traits, enums, constants, and variables
- Top-level function overloads can be deprecated or made obsolete independently
- A deprecated class triggers warnings on any usage (instantiation, type hints, inheritance)
- Member-level `deprecated` / `obsolete` parse but are not enforced in this alpha
