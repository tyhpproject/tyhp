---
title: 'The internal Visibility Modifier'
status:
  tier: 3
  story: '25'
  state: planned
---

:::warning Not in this alpha
This feature is **not included** in Tyhp 805.0.0-alpha.1 (roadmap Tier 2/3). The rest of this page describes the planned design. Do not expect these commands or syntax to work yet.
:::


The internal modifier is a compile-time visibility modifier that restricts access to a symbol within the defining project. A "project" is defined by the presence and scope of a tyhp.json configuration file. All .tyhp files compiled under the same tyhp.json share internal visibility. External projects consuming the library via tyhpdef files cannot see internal symbols — they are excluded from the generated type definitions.

:::note
Unlike public, protected, and private — which map directly to PHP visibility keywords — internal has no PHP equivalent. It is purely a Tyhp compile-time concept. Internal members compile to public PHP (or their otherwise-specified visibility) because PHP has no module/assembly boundary mechanism. Think of it like TypeScript's type annotations: enforced at compile time, erased in output.
:::

## Basic Syntax

The internal modifier can be applied to any declaration that has visibility: classes, interfaces, traits, enums, functions, constants, type aliases, methods, properties, and enum cases.

```tyhp
<?tyhp

// Internal class — visible within this project only
internal class InternalHelper {
    public function doWork(): void {
        // ...
    }
}

// Internal function
internal function computeHash(string $data): string {
    return \hash('sha256', $data);
}

// Internal constant
internal const int MAX_RETRIES = 3;

// Internal type alias
internal type UserId = int;

// Internal interface
internal interface Cacheable {
    public function getCacheKey(): string;
}

// Internal enum
internal enum LogLevel {
    case Debug;
    case Info;
    case Warning;
    case Error;
}

// Internal trait
internal trait HasTimestamps {
    public DateTime $createdAt;
    public DateTime $updatedAt;
}
```

## Internal Members on Public Classes

A public class can contain a mix of public and internal members. Internal members are accessible to all code within the same project but hidden from external consumers. This is useful for exposing a clean public API while keeping implementation details accessible within the project.

```tyhp
<?tyhp

public class UserService {
    // Public method — visible to everyone
    public function getUser(int $id): User {
        User $cached = $this->checkCache($id);
        if ($cached !== null) {
            return $cached;
        }
        return $this->fetchFromDb($id);
    }

    // Internal method — visible within project, hidden from tyhpdef
    internal function checkCache(int $id): ?User {
        return self::$cache[$id] ?? null;
    }

    // Internal method — implementation detail
    internal function fetchFromDb(int $id): User {
        // database logic
    }

    // Internal property
    internal static array $cache = [];

    // Internal constant
    internal const int CACHE_TTL = 3600;
}
```

## Project Scope and tyhp.json

The boundary for internal visibility is defined by the tyhp.json configuration file. Every file compiled under the same tyhp.json belongs to the same project and can access all internal symbols. When your library is consumed by an external project, that project uses the generated tyhpdef files — and internal symbols are excluded from those definitions.

```tyhp
// Project structure:
// my-library/
//   tyhp.json          ← defines the project boundary
//   src/
//     PublicApi.tyhp    ← public class, visible to consumers
//     InternalUtil.tyhp ← internal class, hidden from consumers
//
// consumer-app/
//   tyhp.json          ← different project
//   src/
//     App.tyhp         ← can use PublicApi but NOT InternalUtil
```

## How Enforcement Works

There are two complementary enforcement mechanisms for the internal modifier:

1. Tyhpdef exclusion (primary): When a library is compiled and its public API is exported as .tyhpdef files, items marked internal are excluded from the generated tyhpdef. External projects simply cannot see internal items — they do not exist in the type definitions.
2. Checker validation (secondary): When project A directly depends on project B's source (e.g., a multi-project workspace), the checker validates that project A does not reference any of project B's internal symbols. This catches violations even before tyhpdef generation.

## Modifier Combinations

The internal modifier is orthogonal to PHP visibility and other modifiers. It can be combined with most modifiers but has specific restrictions.

:::member[internal static]
Allowed. The member is static and project-scoped.
:::

:::member[internal readonly]
Allowed. The property is readonly and project-scoped.
:::

:::member[internal abstract]
Allowed. The class or method is abstract and project-scoped.
:::

:::member[internal final]
Allowed. The class or method is final and project-scoped.
:::

:::member[internal protected]
Allowed. Means accessible within the project AND by subclasses in external projects. The protected PHP visibility is preserved in the output.
:::

:::member[internal private]
Not allowed. private is already more restrictive than internal (class-scoped vs project-scoped). Produces compiler error TYHP4055.
:::

```tyhp
<?tyhp

internal abstract class BaseHelper {
    // internal protected — accessible in project + subclasses externally
    internal protected function reset(): void {
        // ...
    }

    internal static function create(): static {
        return new static();
    }

    internal readonly string $id;

    // ERROR: 'internal' cannot be combined with 'private'
    // internal private function secret(): void {}
}
```

## Compiled PHP Output

The internal modifier is stripped from the PHP output. Since PHP has no equivalent concept, internal members are emitted with their effective PHP visibility. The following table shows how modifier combinations are transformed.

:::member[internal (on class member)]
Emits as: public
:::

:::member[internal (on top-level class/function)]
Emits as: (no modifier — PHP classes and functions are implicitly public)
:::

:::member[internal static]
Emits as: public static
:::

:::member[internal protected]
Emits as: protected
:::

:::member[internal readonly]
Emits as: public readonly
:::

:::member[internal abstract]
Emits as: abstract
:::

:::member[internal final]
Emits as: final
:::

```tyhp
<?tyhp

public class UserService {
    public function getUser(int $id): User {
        return $this->fetchFromDb($id);
    }

    internal function fetchFromDb(int $id): User {
        // implementation
    }

    internal int $retryCount = 0;
}

internal class InternalHelper {
    public function doWork(): void {
        // ...
    }
}

internal function helperFn(): void {
    // ...
}

internal const string CACHE_PREFIX = 'user_';
```

```php
<?php

class UserService {
    public function getUser(int $id): User {
        return $this->fetchFromDb($id);
    }

    public function fetchFromDb(int $id): User {
        // implementation
    }

    public int $retryCount = 0;
}

class InternalHelper {
    public function doWork(): void {
        // ...
    }
}

function helperFn(): void {
    // ...
}

const CACHE_PREFIX = 'user_';
```

## Generated Tyhpdef Output

When the compiler generates tyhpdef files for a library, internal symbols are excluded entirely. This is the primary mechanism that prevents external projects from accessing internal members.

```tyhp
// Given this source code:
// public class UserService {
//     public function getUser(int $id): User { ... }
//     internal function fetchFromDb(int $id): User { ... }
//     internal int $retryCount = 0;
// }
// internal class InternalHelper { ... }
// internal function helperFn(): void { ... }

// The generated tyhpdef contains ONLY:
// <?tyhpdef
// class UserService {
//     public function getUser(int $id): User;
// }

// InternalHelper, helperFn, fetchFromDb, and $retryCount
// are all excluded from the tyhpdef
```

## Internal Type Exposure Warning

If a public method's signature references an internal type (as a parameter type, return type, or property type), the compiler emits a warning. This alerts you that external consumers will not be able to use that member because they cannot see the internal type.

```tyhp
<?tyhp

internal class CacheResult {
    public string $data;
    public int $ttl;
}

public class UserService {
    // WARNING TYHP4057: Internal type 'CacheResult' is exposed
    // via public API member 'getCacheStats'.
    // External consumers will not be able to use this member.
    public function getCacheStats(): CacheResult {
        return new CacheResult();
    }
}
```

In the generated tyhpdef, exposed internal types are replaced with mixed since the internal type is not available to external consumers.

## Cross-Project Access Errors

When code in one project attempts to use an internal symbol from another project, the compiler reports an error. This applies to all forms of symbol reference: type hints, function calls, property access, constant access, extends, implements, and trait use.

```tyhp
<?tyhp
// In consumer-app/src/App.tyhp (different project from the library)

use MyLib\UserService;
use MyLib\InternalHelper;  // ERROR TYHP4054: Cannot access internal type

$svc = new UserService();
$svc->getUser(1);          // OK — public method
$svc->fetchFromDb(1);      // ERROR TYHP4053: Cannot access internal member
$svc->retryCount;          // ERROR TYHP4053: Cannot access internal member
```

## Same-Project Access

All code within the same project (under the same tyhp.json) can freely access internal members. There are no restrictions within a project.

```tyhp
<?tyhp
// In my-library/src/Tests.tyhp (same project as UserService)

use MyLib\UserService;
use MyLib\InternalHelper;  // OK — same project

$svc = new UserService();
$svc->fetchFromDb(1);      // OK — same project
$svc->retryCount;          // OK — same project

$helper = new InternalHelper();  // OK — same project
$helper->doWork();
```

## Best Practices

:::tip
Use internal for implementation details that need to be shared across multiple classes within your package but should not be part of the public API. This keeps your public API surface small and intentional.
:::

:::tip
Use internal on helper classes, utility functions, and shared constants that support your public API but are not meant for direct consumer use.
:::

:::tip
Use internal protected on methods that should be accessible within the project and also overridable by subclasses in external projects.
:::

:::tip
Mark test helpers and test fixtures as internal so they are available to tests within your project but not exported as part of your public API.
:::

:::tip
Review compiler warning TYHP4057 when it appears. If a public method returns or accepts an internal type, consider making the method internal or the type public.
:::

## Common Mistakes

:::danger
Don't confuse internal with private. private restricts access to the declaring class only. internal restricts access to the defining project — all classes and files within the same tyhp.json can access internal members.
:::

:::danger
Don't expect PHP to enforce internal at runtime. The internal modifier is compile-time only and is stripped from the PHP output. A consumer could theoretically call the method via raw PHP — but the Tyhp type system prevents this.
:::

:::danger
Don't combine internal with private — private is already more restrictive than internal. This combination produces compiler error TYHP4055.
:::

:::danger
Don't use internal on local variables or loop constructs — internal is a visibility modifier for declarations that appear in the project's API surface. Using it in invalid contexts produces compiler error TYHP4056.
:::

:::danger
Don't return or accept internal types from public methods without understanding the consequences. External consumers will see the type as mixed in the tyhpdef and cannot use the full type information.
:::

```tyhp
<?tyhp

// WRONG: internal + private is not allowed
// class Service {
//     internal private function helper(): void {}
//     // ERROR TYHP4055: 'internal' cannot be combined with 'private'
// }

// CORRECT: use internal alone (project-scoped)
class Service {
    internal function helper(): void {}
}

// CORRECT: use private alone (class-scoped)
class Service2 {
    private function helper(): void {}
}
```

## Compiler Errors

- TYHP4053: Cannot access internal member '{name}' from outside the defining project '{project}' — triggered when code in one project references an internal method, property, or constant from another project.
- TYHP4054: Cannot access internal type '{name}' from outside the defining project '{project}' — triggered when code in one project references an internal class, interface, trait, enum, or type alias from another project.
- TYHP4055: The 'internal' modifier cannot be combined with 'private' — triggered when both internal and private appear on the same declaration.
- TYHP4056: The 'internal' modifier is not valid in this context — triggered when internal is used in an invalid position such as a local variable.
- TYHP4057 (warning): Internal type '{type}' is exposed via public API member '{member}' — triggered when a public method returns or accepts an internal type. External consumers will see mixed instead.
