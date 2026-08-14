# Implementation Plan: Story 04 — Tyhp Runtime Library Modules

> **Roadmap position:** Story 04 — **Tier 0 — Spine**
> **Direct dependencies (new numbering):** 02, 03
> **Renumbered from:** legacy Story 1.5
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** Tyhp language design — PHP runtime module infrastructure, async/await, type system, decimal, disposable
> **Branch:** TBD
> **Generated:** 2026-03-13
> **Prerequisites:** Story 02 (Binder) and Story 03 (Extension Operator Overloads & Tyhpdef Inline Extensions). This story is foundational for the runtime that Stories 06+ depend on: it runs **after** Stories 02 and 03 and **before** Story 06 (TyhpSpec). It establishes the PHP runtime that those stories depend on. All subsequent stories that reference TyhpLib or Promise.php have been updated to reference the Composer packages established here. Note: Story 03 introduces the `extension operator` and `extension function` qualifiers for tyhpdef files. Each runtime package has `"type": "library"` in its `tyhp.json`, which causes the compiler (Story 20, Track C) to auto-generate a `package.tyhp.json` when compiled. That file is a **JSON manifest** with an `include` array (globs pointing at generated artifacts — it does not embed tyhpdef declarations). Generated `.tyhpdef` files use **dot-notation filenames** under `_tyhpdef/`; supporting auto-generated `.tyhp` files go in `_tyhpdef/support/`. Together, those included files are the authoritative type surface for the runtime libraries and are consumed by the binder in user projects (Story 06, Phase 6).
> **Status:** SUBSTANTIALLY COMPLETE (2026-07-31 audit) — `runtime/packages/{core,decimal,async,lambda}` sources + PHPUnit tests exist. Emitter `using` block acceptance was owned by Story 11 (now present). Coverage ≥80% gate not verified; see `INCOMPLETE.md`.

---

## Architecture Overview

### What This Plan Covers

This plan establishes the **complete runtime library** that compiled Tyhp code depends on at execution time. The runtime libraries are **written in Tyhp** (source in `runtime/packages/*/tyhp_src/`) and **compiled to PHP** (output in `runtime/packages/*/src/`). They are distributed as standard Composer packages containing the compiled PHP output and an auto-generated `package.tyhp.json` manifest whose `include` array points at `_tyhpdef/` (dot-notation `.tyhpdef` files) and `_tyhpdef/support/` (supporting `.tyhp` files when present). Every Tyhp language feature that cannot be fully erased at compile time needs corresponding runtime support — this plan identifies and implements all of it.

The runtime is organized as a set of **Composer packages** under a shared `Tyhp\` namespace, published to Packagist and installable via `composer require`. The Tyhp compiler's build action adds the appropriate packages as dependencies based on which language features the compiled code uses.

### Module Architecture

The runtime is split into four packages (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, and `tyhp/lambda` — the last introduced in Phase 11), organized by concern and dependency chain:

```
┌─────────────────────────────────────────────────────────────────┐
│  tyhp/async                                                     │
│  Promise, EventLoop, CancellationToken, Deferred,               │
│  AsyncIsDisposable, DisposableHelper, DisposableScope,           │
│  AsyncIterator                                                   │
│  ─────────────────────────────────────────────────               │
│  Requires: tyhp/core, PHP ≥ 8.1 (Fibers)                       │
└──────────────────────────┬──────────────────────────────────────┘
                           │ depends on
┌──────────────────────────┴──────────────────────────────────────┐
│  tyhp/lambda                                                    │
│  PropertyPath, Expression, ExpressionNode, ExpressionVisitor,   │
│  ExpressionSerializer, all concrete expression node types        │
│  ─────────────────────────────────────────────────               │
│  Requires: tyhp/core                                             │
└──────────────────────────┬──────────────────────────────────────┘
                           │ depends on
┌──────────────────────────┴──────────────────────────────────────┐
│  tyhp/decimal                                                   │
│  Decimal, DecimalConvertible, decimal()                          │
│  ─────────────────────────────────────────────                  │
│  Requires: tyhp/core — ext-bcmath or ext-gmp suggested          │
└──────────────────────────┬──────────────────────────────────────┘
                           │ depends on
┌──────────────────────────┴──────────────────────────────────────┐
│  tyhp/core                                                      │
│  Type, NamedType, GenericObject,                                │
│  PropertyAccessor, IsDisposable                                 │
│  ─────────────────────────────────────────────                  │
│  Requires: PHP ≥ 8.1                                            │
└─────────────────────────────────────────────────────────────────┘
```

Note: `tyhp/decimal`, `tyhp/lambda`, and `tyhp/async` are independent siblings that all depend on `tyhp/core`. None depend on each other. `tyhp/async` only needs `tyhp/core` for the `IsDisposable` interface (used by `DisposableHelper` and `DisposableScope`). `tyhp/lambda` only needs `tyhp/core` for the `Type` system integration.

### Directory Structure

All runtime modules live under a new top-level `runtime/` directory, separate from the compiler source in `Tyhp/`:

```
runtime/
├── README.md                        # Overview, getting started, contribution guide
├── composer.json                    # Root workspace config (monorepo development)
├── phpunit.xml                      # Root PHPUnit config (runs all package tests)
├── .php-cs-fixer.php                # Shared code style (PSR-12)
├── packages/
│   ├── core/
│   │   ├── composer.json            # tyhp/core — PHP ≥ 8.1
│   │   ├── src/
│   │   │   ├── Type.php
│   │   │   ├── NamedType.php
│   │   │   ├── PropertyAccessor.php
│   │   │   ├── Concerns/
│   │   │   │   ├── GenericObject.php
│   │   │   │   └── HasPropertyAccessors.php
│   │   │   ├── Contracts/
│   │   │   │   └── IsDisposable.php
│   │   │   └── Exceptions/
│   │   │       ├── AggregateException.php
│   │   │       ├── IncompatibleTypeException.php
│   │   │       └── InvalidTypeException.php
│   │   └── tests/
│   │       ├── TypeTest.php
│   │       ├── NamedTypeTest.php
│   │       ├── GenericObjectTest.php
│   │       └── PropertyAccessorTest.php
│   ├── decimal/
│   │   ├── composer.json            # tyhp/decimal — requires tyhp/core, suggests ext-bcmath/ext-gmp
│   │   ├── src/
│   │   │   ├── Decimal.php
│   │   │   ├── Contracts/
│   │   │   │   └── DecimalConvertible.php
│   │   │   ├── Backend/
│   │   │   │   ├── DecimalBackend.php       # Backend interface
│   │   │   │   ├── BcMathBackend.php        # ext-bcmath (preferred)
│   │   │   │   ├── GmpBackend.php           # ext-gmp (alternative)
│   │   │   │   └── IntegerScaledBackend.php # Pure-PHP fallback
│   │   │   └── Functions/
│   │   │       └── decimal.php      # decimal() factory function
│   │   └── tests/
│   │       └── DecimalTest.php
│   ├── lambda/
│   │   ├── composer.json            # tyhp/lambda — requires tyhp/core
│   │   ├── src/
│   │   │   ├── PropertyPath.php
│   │   │   ├── Expression.php
│   │   │   └── Expression/
│   │   │       ├── ExpressionNode.php
│   │   │       ├── ExpressionVisitor.php
│   │   │       ├── ExpressionSerializer.php
│   │   │       ├── ParameterExpression.php
│   │   │       ├── PropertyAccessExpression.php
│   │   │       ├── MethodCallExpression.php
│   │   │       ├── StaticMethodCallExpression.php
│   │   │       ├── BinaryExpression.php
│   │   │       ├── UnaryExpression.php
│   │   │       ├── ConstantExpression.php
│   │   │       ├── NullSafeAccessExpression.php
│   │   │       ├── TernaryExpression.php
│   │   │       ├── CoalesceExpression.php
│   │   │       ├── ArrayAccessExpression.php
│   │   │       ├── CastExpression.php
│   │   │       └── NewExpression.php
│   │   └── tests/
│   │       ├── PropertyPathTest.php
│   │       └── ExpressionTest.php
│   └── async/
│       ├── composer.json            # tyhp/async — requires tyhp/core, PHP ≥ 8.1
│       ├── src/
│       │   ├── Promise.php
│       │   ├── PromiseState.php
│       │   ├── EventLoop.php
│       │   ├── Deferred.php
│       │   ├── CancellationToken.php
│       │   ├── CancellationTokenSource.php
│       │   ├── DisposableHelper.php
│       │   ├── DisposableScope.php
│       │   ├── Contracts/
│       │   │   ├── AsyncIsDisposable.php
│       │   │   ├── AsyncIterator.php
│       │   │   ├── AsyncIterable.php
│       │   │   └── AsyncKeyValueIterator.php
│       │   └── Exceptions/
│       │       ├── OperationCancelledException.php
│       │       ├── TimeoutException.php
│       │       └── InvalidPromiseStateException.php
│       └── tests/
│           ├── PromiseTest.php
│           ├── EventLoopTest.php
│           ├── CancellationTokenTest.php
│           ├── DeferredTest.php
│           ├── DisposableHelperTest.php
│           ├── DisposableScopeTest.php
│           └── CombinatorTest.php
```

### Namespace Strategy

All packages share the `Tyhp\` namespace prefix via PSR-4 autoloading. Each package maps `Tyhp\` to its own `src/` directory. Composer merges these when generating the autoloader, so classes from different packages coexist seamlessly:

| Package | Composer Name | PSR-4 Mapping | Example Class |
|---------|--------------|---------------|---------------|
| Core | `tyhp/core` | `"Tyhp\\": "src/"` | `Tyhp\Type` |
| Decimal | `tyhp/decimal` | `"Tyhp\\": "src/"` | `Tyhp\Decimal` |
| Lambda | `tyhp/lambda` | `"Tyhp\\": "src/"` | `Tyhp\PropertyPath` |
| Async | `tyhp/async` | `"Tyhp\\": "src/"` | `Tyhp\Promise` |

This means the emitter generates `\Tyhp\Promise::_async(...)`, `\Tyhp\Type::is(...)`, `new \Tyhp\Decimal(...)` — all under the single `\Tyhp\` namespace, regardless of which package provides the class.

### Distribution Model

The Tyhp compiler determines which runtime packages a compiled project needs based on feature usage analysis:

| Feature Used | Package Required | Composer Command |
|-------------|-----------------|------------------|
| Generic classes with runtime tracking | `tyhp/core` | `composer require tyhp/core` |
| Typed variables, property hooks | `tyhp/core` | `composer require tyhp/core` |
| `decimal` type | `tyhp/decimal` | `composer require tyhp/decimal` |
| `async`/`await` keywords | `tyhp/async` | `composer require tyhp/async` |
| `:=` (using/dispose) with async | `tyhp/async` | `composer require tyhp/async` |
| `:=` (using/dispose) sync only | `tyhp/core` | `composer require tyhp/core` |
| `PropertyPath<T, R>` parameters | `tyhp/lambda` | `composer require tyhp/lambda` |
| `Expression<T, R>` parameters | `tyhp/lambda` | `composer require tyhp/lambda` |
| Async iteration (`foreach await`) | `tyhp/async` | `composer require tyhp/async` |

The compiler's build action (`TyhpLibDistributionService` from Story 10) adds the required packages to the output project's `composer.json` and runs `composer install`. This replaces the previous plan of copying TyhpLib files directly into the output directory.

When a runtime package is installed as a Composer dependency, the Tyhp compiler's binder discovers its `package.tyhp.json` from `vendor/tyhp/*/package.tyhp.json` and loads the tyhpdef and Tyhp sources listed by the manifest's `include` array (Story 06, Phase 6). This means user projects get full type information for the runtime library without access to the original Tyhp library source.

### Design Principles

1. **Familiar APIs.** Promise follows JavaScript's `Promise` API (then/catch/finally/all/allSettled/any/race/resolve/reject) augmented with C#'s `Task` patterns (WhenAll/WhenAny/ContinueWith/Wait/Run/Delay). Developers familiar with either language feel at home.

2. **Fiber-based cooperative scheduling.** PHP is single-threaded. Like JavaScript, async operations are cooperative — `await` suspends the current Fiber and yields to the event loop, which resumes other ready Fibers. No parallelism, but non-blocking I/O enables concurrency.

3. **Real I/O support.** The event loop uses `stream_select()` for non-blocking I/O on streams, sockets, and pipes. This enables genuinely asynchronous file reads, HTTP requests, database queries, etc. — not just timer-based delays.

4. **CancellationToken pattern.** Borrowed from C#, `CancellationToken` provides cooperative cancellation. Async operations accept an optional token and check it periodically. `CancellationTokenSource` controls token lifecycle.

5. **Package independence.** Each package has minimal dependencies. `tyhp/core` has zero external dependencies (only `php >= 8.1`). `tyhp/decimal` suggests `ext-bcmath` or `ext-gmp` but works without either (falls back to integer-scaled math). `tyhp/lambda` requires only `tyhp/core`. `tyhp/async` requires only `tyhp/core`.

6. **PSR compliance.** PSR-4 autoloading, PSR-12 code style, standard Composer packaging. Runtime packages are indistinguishable from any other Composer library.

7. **Start fresh, use existing as reference.** The existing `Promise.php`, `php_Async/`, and `Tyhp/TyhpLib/` code serves as reference and inspiration, but this plan builds from scratch with a clean architecture. Existing code has known bugs (race(), batch(), PromiseLoop timing) that are fixed by redesign rather than patching.

8. **Written in Tyhp, compiled to PHP.** The runtime library source code is written in Tyhp (`.tyhp` files under `tyhp_src/`) and compiled to PHP (output to `src/`). Each package has `"type": "library"` in its `tyhp.json`, which causes the compiler to auto-generate `package.tyhp.json` plus `_tyhpdef/` (dot-notation `.tyhpdef` files) and, when needed, `_tyhpdef/support/` `.tyhp` files on build (Story 20, Track C). The compiled PHP, manifest, and generated type artifacts are distributed via Composer — the hand-authored Tyhp sources under `tyhp_src/` are not included in the published package. This design means the runtime library itself uses Tyhp language features (operator overloads, extensions, generics, etc.) and benefits from the same type safety as user code.

9. **Emitter integration contract.** The emitter (Story 09/11) generates PHP code that calls into the runtime library classes. Each class documents its **emitter contract** — the exact method signatures and call patterns the emitter generates. The emitter design dictates the runtime API, not the other way around. Method names, parameter types, and call patterns must match exactly what the emitter outputs.

### Position in the Pipeline

```
┌───────────────────────────────────────────────────────────────────────┐
│  STORY 04: Tyhp Runtime Library Modules                              │
│  ◄── THIS PLAN                                                      │
│                                                                      │
│  Creates the Tyhp-to-PHP packages that compiled Tyhp code depends on.│
│  Other stories reference this runtime but don't build it.            │
│                                                                      │
│  Phase 1:  Infrastructure — monorepo, Composer, PHPUnit, CI          │
│  Phase 2:  Core — Type system (Type, NamedType)                      │
│  Phase 3:  Core — GenericObject, PropertyAccessor                     │
│  Phase 4:  Decimal — Decimal class, bcmath operations                │
│  Phase 5:  Async — Promise foundation (Fiber, states, _async/_await) │
│  Phase 6:  Async — Event loop (stream_select, timers, microtasks)    │
│  Phase 7:  Async — CancellationToken                                 │
│  Phase 8:  Async — Combinators (all, race, any, delay, etc.)         │
│  Phase 9:  Async — Disposable + Async iteration                      │
│  Phase 10: Using Block — grammar, AST, visitor, binder (emitter: S8) │
│  Phase 11: Lambda — PropertyPath, Expression trees, ExpressionVisitor │
│  Phase 12: Testing — comprehensive PHPUnit coverage                  │
└───────────────────────────────────────────────────────────────────────┘
         ▲                    ▲                    ▲
         │                    │                    │
    Story 06 (TyhpSpec)   Story 10 (Build)     Story 11 (Emitter)
    declares type sigs   distributes via      emits calls to
    for these packages   Composer deps        \Tyhp\* classes
```

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup: `<filename>.<YYYYMMDD_HHMMSS>.backup`
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: Module Infrastructure & Directory Structure

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create the `runtime/` directory tree, initialize Composer packages for the initial three modules (`core`, `decimal`, `async`; the fourth package, `lambda`, is added in Phase 11), set up PHPUnit testing infrastructure, and establish the monorepo development workflow. This phase produces a buildable, testable (empty) workspace.

### Deliverables

**New files:**
- `runtime/composer.json` — Root monorepo workspace
- `runtime/phpunit.xml` — Root test runner
- `runtime/.php-cs-fixer.php` — Code style config
- `runtime/README.md` — Developer documentation
- `runtime/packages/core/composer.json` — `tyhp/core` package
- `runtime/packages/decimal/composer.json` — `tyhp/decimal` package
- `runtime/packages/async/composer.json` — `tyhp/async` package
- Directory structure for `src/` and `tests/` in each package

### Implementation Details

#### 1.1 Create Root Monorepo Configuration

**File: `runtime/composer.json`**

```json
{
    "name": "tyhp/runtime",
    "description": "Tyhp runtime library monorepo — development workspace",
    "type": "project",
    "license": "MIT",
    "minimum-stability": "dev",
    "prefer-stable": true,
    "require": {
        "php": ">=8.1",
        "tyhp/core": "@dev",
        "tyhp/decimal": "@dev",
        "tyhp/async": "@dev"
    },
    "require-dev": {
        "phpunit/phpunit": "^10.0|^11.0",
        "friendsofphp/php-cs-fixer": "^3.0"
    },
    "repositories": [
        { "type": "path", "url": "packages/core" },
        { "type": "path", "url": "packages/decimal" },
        { "type": "path", "url": "packages/async" }
    ],
    "autoload-dev": {
        "psr-4": {
            "Tyhp\\Tests\\": "packages/core/tests/",
            "Tyhp\\Tests\\Decimal\\": "packages/decimal/tests/",
            "Tyhp\\Tests\\Async\\": "packages/async/tests/"
        }
    },
    "scripts": {
        "test": "phpunit",
        "cs-fix": "php-cs-fixer fix",
        "cs-check": "php-cs-fixer fix --dry-run --diff"
    }
}
```

The `repositories` block uses Composer path repositories so that during development, `tyhp/core`, `tyhp/decimal`, and `tyhp/async` resolve to the local `packages/` directories via symlinks. When published to Packagist, consumers install the real packages.

#### 1.2 Create Package Composer Configurations

**File: `runtime/packages/core/composer.json`**

```json
{
    "name": "tyhp/core",
    "description": "Tyhp runtime core — type system, generics, typed variables, property accessors",
    "type": "library",
    "license": "MIT",
    "require": {
        "php": ">=8.1"
    },
    "autoload": {
        "psr-4": {
            "Tyhp\\": "src/"
        }
    },
    "autoload-dev": {
        "psr-4": {
            "Tyhp\\Tests\\": "tests/"
        }
    }
}
```

**File: `runtime/packages/decimal/composer.json`**

```json
{
    "name": "tyhp/decimal",
    "description": "Tyhp runtime decimal — arbitrary-precision decimal arithmetic via bcmath",
    "type": "library",
    "license": "MIT",
    "require": {
        "php": ">=8.1",
        "tyhp/core": "^1.0"
    },
    "suggest": {
        "ext-bcmath": "Recommended for arbitrary-precision decimal arithmetic (preferred backend)",
        "ext-gmp": "Alternative backend for arbitrary-precision decimal arithmetic"
    },
    "autoload": {
        "psr-4": {
            "Tyhp\\": "src/"
        },
        "files": [
            "src/Functions/decimal.php"
        ]
    },
    "autoload-dev": {
        "psr-4": {
            "Tyhp\\Tests\\Decimal\\": "tests/"
        }
    }
}
```

The `files` autoload entry ensures the `\Tyhp\decimal()` factory function is available globally without explicit `require`.

**File: `runtime/packages/async/composer.json`**

```json
{
    "name": "tyhp/async",
    "description": "Tyhp runtime async — Promise, event loop, CancellationToken, async iteration",
    "type": "library",
    "license": "MIT",
    "require": {
        "php": ">=8.1",
        "tyhp/core": "^1.0"
    },
    "autoload": {
        "psr-4": {
            "Tyhp\\": "src/"
        }
    },
    "autoload-dev": {
        "psr-4": {
            "Tyhp\\Tests\\Async\\": "tests/"
        }
    }
}
```

#### 1.3 Create PHPUnit Configuration

**File: `runtime/phpunit.xml`**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<phpunit xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
         xsi:noNamespaceSchemaLocation="vendor/phpunit/phpunit/phpunit.xsd"
         bootstrap="vendor/autoload.php"
         colors="true"
         cacheDirectory=".phpunit.cache"
         failOnWarning="true"
         failOnRisky="true">
    <testsuites>
        <testsuite name="core">
            <directory>packages/core/tests</directory>
        </testsuite>
        <testsuite name="decimal">
            <directory>packages/decimal/tests</directory>
        </testsuite>
        <testsuite name="async">
            <directory>packages/async/tests</directory>
        </testsuite>
    </testsuites>
    <source>
        <include>
            <directory>packages/core/src</directory>
            <directory>packages/decimal/src</directory>
            <directory>packages/async/src</directory>
        </include>
    </source>
</phpunit>
```

#### 1.4 Create Code Style Configuration

**File: `runtime/.php-cs-fixer.php`**

Standard PSR-12 configuration with strict types declaration enforcement, ordered imports, and trailing commas in multiline. All source files must begin with `declare(strict_types=1);`.

#### 1.5 Create Directory Structure

Create all `src/` and `tests/` directories plus subdirectories (`Concerns/`, `Contracts/`, `Exceptions/`, `Functions/`). Each `src/` directory gets an empty `.gitkeep` until populated in subsequent phases.

### Acceptance Criteria

- [x] `runtime/` directory exists at the project root alongside `Tyhp/`
- [ ] `composer install` succeeds in the `runtime/` directory
- [ ] `composer test` runs PHPUnit (with zero tests, exits cleanly)
- [x] All three packages are symlinked via Composer path repositories
- [ ] `composer cs-check` runs PHP-CS-Fixer (with zero files, exits cleanly)
- [x] Directory structure matches the specification above
- [x] Each package's `composer.json` is valid (`composer validate` passes)

---

## Phase 2: Core Module — Type System

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the runtime type system that supports `typeof()`, `Type::is()`, generic type parameter tracking, union/intersection types, and nullable types. This is the foundation that `GenericObject` and the emitter's runtime type checks depend on.

### Deliverables

**New/rewritten files:**
- `runtime/packages/core/src/Type.php` — Complete implementation (replaces stub)
- `runtime/packages/core/src/NamedType.php` — Complete implementation (replaces stub)
- `runtime/packages/core/src/Contracts/IsDisposable.php` — Sync disposable interface
- `runtime/packages/core/src/Exceptions/IncompatibleTypeException.php` — Type mismatch exception
- `runtime/packages/core/src/Exceptions/InvalidTypeException.php` — Invalid type construction exception

### Implementation Details

#### 2.1 Implement `Type` Class

**File: `runtime/packages/core/src/Type.php`**

The `Type` class represents types at runtime. It supports scalar types, class types, union types, intersection types, nullable types, and generic types. Each `Type` instance is immutable.

```php
<?php

declare(strict_types=1);

namespace Tyhp;

final class Type implements \Stringable
{
    private function __construct(
        private readonly string $kind,
        private readonly ?string $name = null,
        private readonly array $typeArgs = [],
        private readonly bool $isReadOnly = false,
        private readonly bool $isNullable = false,
    ) {}

    // --- Scalar Type Singletons ---

    public static function string(): self { /* cached singleton */ }
    public static function int(): self { /* cached singleton */ }
    public static function float(): self { /* cached singleton */ }
    public static function bool(): self { /* cached singleton */ }
    public static function null(): self { /* cached singleton */ }
    public static function void(): self { /* cached singleton */ }
    public static function mixed(): self { /* cached singleton */ }
    public static function never(): self { /* cached singleton */ }
    public static function array(): self { /* cached singleton */ }
    public static function object(): self { /* cached singleton */ }
    public static function callable(): self { /* cached singleton */ }
    public static function iterable(): self { /* cached singleton */ }
    public static function resource(): self { /* cached singleton */ }

    // --- Composite Type Constructors ---

    public static function union(self ...$types): self;
    public static function intersection(self ...$types): self;
    public static function nullable(self $type): self;
    public static function generic(string $className, NamedType ...$params): self;
    public static function fromClassName(string $className): self;

    // --- Runtime Type Operations ---

    public static function of(mixed $value): self;
    public static function is(mixed $value, self $type): bool;
    public static function compatible(self $broad, self $narrow): bool;

    // --- Instance Methods ---

    public function asReadOnly(): self;
    public function asNullable(): self;
    public function asNonNullable(): self;
    public function genericParameter(string $name): ?NamedType;
    public function getKind(): string;
    public function getName(): ?string;
    public function isNullable(): bool;
    public function isReadOnly(): bool;
    public function __toString(): string;
}
```

Key implementation notes:

- **Singleton caching:** Scalar types are cached via static properties (e.g., `private static ?self $stringType = null`). Repeated calls to `Type::string()` return the same instance.
- **`Type::of(mixed $value)`:** Uses `get_debug_type()` for scalar/null detection, `$value::class` for objects, and inspects the `GenericObject` trait for generic type info when available.
- **`Type::is(mixed $value, Type $type)`:** Performs runtime type checking. For scalars, uses PHP's type juggling rules. For objects, uses `instanceof`. For unions, checks each member. For generics, delegates to `GenericObject::tyhpGenericObjectGetObjectType()` when available.
- **`Type::compatible(Type $broad, Type $narrow)`:** Structural compatibility check. `mixed` is compatible with everything. Union compatibility means the narrow type is a subset of the broad union. For generics, type arguments must be compatible positionally.

#### 2.2 Implement `NamedType` Class

**File: `runtime/packages/core/src/NamedType.php`**

```php
<?php

declare(strict_types=1);

namespace Tyhp;

final class NamedType extends Type
{
    public function __construct(
        private readonly string $parameterName,
        private readonly Type $underlyingType,
    ) {}

    public function getParameterName(): string;
    public function getUnderlyingType(): Type;
    public function asReadOnly(): self;
    public function __toString(): string;
}
```

`NamedType` extends `Type` to associate a name with a type — used for generic type parameters (e.g., `T` → `int` in `Collection<int>`).

#### 2.3 Implement `IsDisposable` Interface

**File: `runtime/packages/core/src/Contracts/IsDisposable.php`**

```php
<?php

declare(strict_types=1);

namespace Tyhp\Contracts;

interface IsDisposable
{
    public function dispose(): void;
}
```

Sync disposable interface. Classes implementing this can be used with Tyhp's `:=` (using assignment) operator. The compiler emits a `DisposableScope` that auto-disposes resources via `__destruct()` when the scope variable leaves scope. This avoids wrapping code in try/finally blocks — PHP's reference-counting GC ensures `__destruct()` fires deterministically when the scope exits, even on exception paths.

#### 2.4 Implement Exception Classes

**File: `runtime/packages/core/src/Exceptions/IncompatibleTypeException.php`**

Thrown when a runtime type check fails (e.g., assigning an `int` to a generic property typed as `string`). Carries the expected `Type`, the actual `Type`, and an optional variable/parameter name.

**File: `runtime/packages/core/src/Exceptions/InvalidTypeException.php`**

Thrown when an invalid type construction is attempted (e.g., creating a union with zero members).

### Acceptance Criteria

- [x] `Type::string()`, `Type::int()`, etc. return cached singleton instances
- [x] `Type::of(42)` returns a Type representing `int`
- [x] `Type::of("hello")` returns a Type representing `string`
- [x] `Type::of(new \stdClass())` returns a Type representing `stdClass`
- [x] `Type::is(42, Type::int())` returns `true`
- [x] `Type::is("hello", Type::int())` returns `false`
- [x] `Type::is(null, Type::nullable(Type::string()))` returns `true`
- [x] `Type::union(Type::int(), Type::string())` creates a union type
- [x] `Type::is(42, Type::union(Type::int(), Type::string()))` returns `true`
- [x] `Type::compatible(Type::mixed(), Type::int())` returns `true`
- [x] `Type::generic('Collection', new NamedType('T', Type::int()))` creates a generic type
- [x] `Type::fromClassName('stdClass')` creates a class type
- [x] `(string) Type::nullable(Type::string())` returns `"?string"`
- [x] `(string) Type::union(Type::int(), Type::string())` returns `"int|string"`
- [x] `IncompatibleTypeException` carries expected/actual types and formats a clear message
- [x] `IsDisposable` interface is loadable and implementable

---

## Phase 3: Core Module — Runtime Traits & Utilities

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the runtime traits and utility classes that compiled Tyhp code uses: `GenericObject` for runtime generic type tracking, and `PropertyAccessor`/`HasPropertyAccessors` for property hooks on PHP < 8.4. (Note: typed local variables are handled by type erasure at compile time — the compiler strips type annotations and emits plain PHP assignments. There is no runtime enforcement of local variable types.)

### Deliverables

**New/rewritten files:**
- `runtime/packages/core/src/Concerns/GenericObject.php` — Rewritten with complete Type integration
- `runtime/packages/core/src/Concerns/HasPropertyAccessors.php` — Property hook interception trait
- `runtime/packages/core/src/PropertyAccessor.php` — Property hook runtime (rewritten)

### Implementation Details

#### 3.1 Rewrite `GenericObject` Trait

**File: `runtime/packages/core/src/Concerns/GenericObject.php`**

The `GenericObject` trait is `use`d by any compiled class that has generic type parameters. It provides runtime tracking of what concrete types were supplied and optional runtime type checking on property access.

```php
trait GenericObject
{
    private ?Type $__tyhpObjectType = null;
    private array $__tyhpInterfaceGenerics = [];
    private array $__tyhpTypedProperties = [];

    protected function tyhpGenericObjectInit(NamedType ...$genericArguments): void;
    protected function tyhpGenericObjectInitInterface(string $interface, NamedType ...$args): void;
    protected function tyhpGenericObjectSetPropertyType(string $property, Type $type): void;

    public function tyhpGenericObjectGetObjectType(): ?Type;
    public function tyhpGenericObjectGetGenericType(string $parameterName): ?NamedType;

    protected function tyhpGenericObjectSetProperty(string $property, mixed $value): bool;
    protected function tyhpGenericObjectGetProperty(string $property): mixed;
    protected function tyhpGenericObjectIssetProperty(string $property): bool;
    protected function tyhpGenericObjectUnsetProperty(string $property): void;
}
```

Key changes from existing code:
- Uses the new `Type::is()` for runtime type checking (replacing the stub)
- `tyhpGenericObjectGetProperty()` properly returns the value (existing code is missing the `return` statement)
- All property operations correctly delegate to the `Type` system

#### 3.2 Typed Local Variables (Type Erasure — No Runtime Classes)

`StronglyTypedVar` and `StronglyTypedScope` have been **removed** from the runtime. Typed local variables in Tyhp source are now simply type-erased in the compiled PHP output — the type annotation is stripped and no runtime enforcement occurs. For example, `string $name = "Alice";` in Tyhp source compiles to just `$name = "Alice";` in PHP. The compiler handles type checking statically at compile time, similar to how TypeScript erases types. No runtime classes or traits are needed for this feature.

#### 3.3 Implement `PropertyAccessor` and `HasPropertyAccessors`

**File: `runtime/packages/core/src/PropertyAccessor.php`**

Provides runtime property hook dispatch for PHP < 8.4 targets. Each property accessor wraps typed get/set/isset/unset closures with optional lazy initialization and guard validation.

```php
final class PropertyAccessor
{
    public function __construct(
        private readonly Type $type,
        private readonly ?\Closure $get = null,
        private readonly ?\Closure $set = null,
        private readonly ?\Closure $isset = null,
        private readonly ?\Closure $unset = null,
        private readonly ?\Closure $lazy = null,
        private readonly ?\Closure $guard = null,
    ) {}

    public function get(object $target): mixed;
    public function set(object $target, mixed $value): void;
    public function isset(object $target): bool;
    public function unset(object $target): void;
    public function getType(): Type;
}
```

**File: `runtime/packages/core/src/Concerns/HasPropertyAccessors.php`**

```php
trait HasPropertyAccessors
{
    private array $__tyhpPropertyAccessors = [];
    private array $__tyhpPropertyValues = [];

    protected function tyhpRegisterAccessor(string $property, PropertyAccessor $accessor): void;

    public function __get(string $name): mixed;
    public function __set(string $name, mixed $value): void;
    public function __isset(string $name): bool;
    public function __unset(string $name): void;
}
```

The trait intercepts magic method calls and delegates to the registered `PropertyAccessor` for each property. When running on PHP 8.4+, the emitter uses native property hooks instead and this trait is not used.

#### 3.x Implement `AggregateException`

**File: `runtime/packages/core/src/Exceptions/AggregateException.php`**

```php
final class AggregateException extends \RuntimeException
{
    public function __construct(
        private readonly array $innerExceptions,
        string $message = 'One or more errors occurred',
        ?\Throwable $previous = null,
    ) {
        parent::__construct($message, 0, $previous);
    }

    /** @return \Throwable[] */
    public function getInnerExceptions(): array { return $this->innerExceptions; }
}
```

`AggregateException` collects multiple exceptions into a single throwable. Used by:
- `DisposableHelper` and `DisposableScope` (Phase 9) when multiple dispose calls fail
- `using` block multi-resource disposal (Phase 10) when dispose calls fail in the finally block
- `Promise::any()` (Phase 8) when all promises reject

This class lives in `tyhp/core` because it's needed by sync disposable patterns (`using` block) that don't require the async module.

### Acceptance Criteria

- [x] `GenericObject` trait initializes with `NamedType` arguments and tracks them
- [x] `tyhpGenericObjectSetProperty()` enforces types via `Type::is()`
- [x] `tyhpGenericObjectSetProperty()` throws `IncompatibleTypeException` on type mismatch
- [x] `tyhpGenericObjectGetProperty()` returns the stored value (bug fix from existing code)
- [x] `PropertyAccessor::get()` calls the get closure and returns the result
- [x] `PropertyAccessor::set()` validates type via `Type::is()` before calling the set closure
- [x] `HasPropertyAccessors` trait routes `__get`/`__set` to registered accessors
- [x] Unregistered property access on `HasPropertyAccessors` falls through to PHP default behavior
- [x] `AggregateException` stores and retrieves inner exceptions
- [x] `AggregateException` extends `\RuntimeException`

---

## Phase 4: Decimal Module

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Implement the complete `Decimal` class for arbitrary-precision decimal arithmetic with runtime backend detection. The class automatically selects the best available backend: `ext-bcmath` (preferred), `ext-gmp`, or a pure-PHP integer-scaled fallback. This replaces the partial implementation in `Tyhp/TyhpLib/Tyhp/decimal.php` with a feature-complete version supporting all arithmetic operations, comparisons, formatting, and operator-overload-friendly methods.

### Deliverables

**New files:**
- `runtime/packages/decimal/src/Decimal.php` — Public decimal arithmetic class (delegates to backend)
- `runtime/packages/decimal/src/Contracts/DecimalConvertible.php` — Conversion interface
- `runtime/packages/decimal/src/Functions/decimal.php` — Factory function
- `runtime/packages/decimal/src/Backend/DecimalBackend.php` — Backend interface
- `runtime/packages/decimal/src/Backend/BcMathBackend.php` — bcmath implementation
- `runtime/packages/decimal/src/Backend/GmpBackend.php` — GMP implementation
- `runtime/packages/decimal/src/Backend/IntegerScaledBackend.php` — Pure-PHP fallback

### Implementation Details

#### 4.1 Backend Architecture

The `Decimal` class delegates all arithmetic to a backend. The backend is selected once at runtime and cached:

```
┌──────────────────────────────────────────────────────┐
│  Decimal (public API)                                │
│  - Immutable value object                            │
│  - Delegates arithmetic to selected backend          │
└──────────────┬───────────────────────────────────────┘
               │ uses
┌──────────────┴───────────────────────────────────────┐
│  DecimalBackend (interface)                           │
│  - add(), subtract(), multiply(), divide(), etc.     │
│  - compare(), sqrt(), pow(), mod()                   │
├──────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌──────────┐  ┌─────────────────┐ │
│  │ BcMathBackend│  │GmpBackend│  │IntegerScaled    │ │
│  │ (preferred)  │  │(alt)     │  │Backend (fallback)│ │
│  │ ext-bcmath   │  │ext-gmp   │  │pure PHP         │ │
│  └─────────────┘  └──────────┘  └─────────────────┘ │
└──────────────────────────────────────────────────────┘
```

Backend selection at runtime (performed once, cached in a static property):

```php
private static function resolveBackend(): DecimalBackend
{
    if (self::$backend !== null) {
        return self::$backend;
    }

    if (\extension_loaded('bcmath')) {
        self::$backend = new BcMathBackend();
    } elseif (\extension_loaded('gmp')) {
        self::$backend = new GmpBackend();
    } else {
        self::$backend = new IntegerScaledBackend();
    }

    return self::$backend;
}
```

#### 4.2 Implement `DecimalBackend` Interface

**File: `runtime/packages/decimal/src/Backend/DecimalBackend.php`**

```php
interface DecimalBackend
{
    public function add(string $a, string $b, int $scale): string;
    public function subtract(string $a, string $b, int $scale): string;
    public function multiply(string $a, string $b, int $scale): string;
    public function divide(string $a, string $b, int $scale): string;
    public function modulo(string $a, string $b, int $scale): string;
    public function power(string $base, int $exponent, int $scale): string;
    public function sqrt(string $value, int $scale): string;
    public function compare(string $a, string $b, int $scale): int;
    public function negate(string $value): string;
    public function abs(string $value): string;
    public function getName(): string;
}
```

All backends operate on string representations of decimal numbers (e.g., `"10.50"`, `"-3.14159"`). The `Decimal` class handles parsing, scale management, and result construction; backends handle only raw arithmetic.

#### 4.3 Implement `BcMathBackend` (Preferred)

**File: `runtime/packages/decimal/src/Backend/BcMathBackend.php`**

Uses `bcadd()`, `bcsub()`, `bcmul()`, `bcdiv()`, `bcmod()`, `bcpow()`, `bcsqrt()`, `bccomp()`. This is the most mature arbitrary-precision math library in PHP and is bundled with most PHP installations.

#### 4.4 Implement `GmpBackend` (Alternative)

**File: `runtime/packages/decimal/src/Backend/GmpBackend.php`**

GMP natively handles arbitrary-precision integers but not decimals. The backend converts decimal values to scaled integers internally:

- `"10.50"` at scale 2 → GMP integer `1050`
- Arithmetic is performed on scaled integers
- Results are converted back to decimal strings with the correct scale

Key considerations:
- Multiplication: `(a * 10^s) * (b * 10^s) = a*b * 10^(2s)` — must divide result by `10^s`
- Division: `(a * 10^s) / (b * 10^s) = a/b` — must multiply numerator by `10^s` first
- `gmp_sqrt()` for square root, with scale adjustment

#### 4.5 Implement `IntegerScaledBackend` (Pure-PHP Fallback)

**File: `runtime/packages/decimal/src/Backend/IntegerScaledBackend.php`**

When neither `ext-bcmath` nor `ext-gmp` is available, this backend uses PHP's native integer arithmetic with manual decimal point management. This approach is preferred over float math because it avoids IEEE 754 floating-point representation errors entirely.

The strategy:
- Internally represent values as `int` (or `string` for values exceeding `PHP_INT_MAX`) with a scale factor
- `"10.50"` at scale 2 → integer `1050` with implicit scale 2
- All arithmetic is performed on integers, then the decimal point is reinserted
- For values exceeding `PHP_INT_MAX` (~9.2 quintillion), fall back to string-based arithmetic using PHP's arbitrary-length string math (manual carry/borrow digit-by-digit)

Precision limitations:
- Accurate for values that fit within PHP's 64-bit integer range when scaled (most business use cases)
- For extremely large values or extremely high scales, string-based digit-by-digit arithmetic is used (slower but correct)
- Square root uses Newton's method on scaled integers
- A runtime warning is emitted once if this fallback is used, recommending `ext-bcmath` installation

#### 4.6 Implement `Decimal` Class (Public API)

**File: `runtime/packages/decimal/src/Decimal.php`**

```php
final class Decimal implements DecimalConvertible, \Stringable, \JsonSerializable
{
    private static ?DecimalBackend $backend = null;

    public readonly string $value;
    public readonly int $scale;
    public readonly int $roundingMode;

    // --- Construction ---
    public function __construct(
        float|int|string|DecimalConvertible|null $value = null,
        ?int $scale = null,
        int $roundingMode = \PHP_ROUND_HALF_UP,
    );

    // --- Arithmetic (return new Decimal, immutable) ---
    public function add(float|int|string|DecimalConvertible $operand): self;
    public function subtract(float|int|string|DecimalConvertible $operand): self;
    public function multiply(float|int|string|DecimalConvertible $operand): self;
    public function divide(float|int|string|DecimalConvertible $operand, ?int $scale = null): self;
    public function modulo(float|int|string|DecimalConvertible $operand): self;
    public function power(int $exponent): self;
    public function negate(): self;
    public function abs(): self;
    public function sqrt(?int $scale = null): self;

    // --- Comparison ---
    public function compareTo(float|int|string|DecimalConvertible $other): int;
    public function equals(float|int|string|DecimalConvertible $other): bool;
    public function greaterThan(float|int|string|DecimalConvertible $other): bool;
    public function greaterThanOrEqual(float|int|string|DecimalConvertible $other): bool;
    public function lessThan(float|int|string|DecimalConvertible $other): bool;
    public function lessThanOrEqual(float|int|string|DecimalConvertible $other): bool;
    public function isZero(): bool;
    public function isPositive(): bool;
    public function isNegative(): bool;

    // --- Conversion ---
    public function __toInt(): int;
    public function __toFloat(): float;
    public function __toDecimal(): self;
    public function withScale(int $scale): self;
    public function round(int $precision = 0, int $mode = \PHP_ROUND_HALF_UP): self;
    public function floor(): self;
    public function ceil(): self;

    // --- Display ---
    public function __toString(): string;
    public function jsonSerialize(): string;
    public function format(int $decimals = 2, string $decimalSep = '.', string $thousandsSep = ','): string;

    // --- Statics ---
    public static function zero(int $scale = 2): self;
    public static function one(int $scale = 2): self;
    public static function min(self ...$values): self;
    public static function max(self ...$values): self;
    public static function sum(self ...$values): self;
    public static function avg(self ...$values): self;

    // --- Backend ---
    public static function getBackendName(): string;
    public static function setBackend(DecimalBackend $backend): void;
}
```

All arithmetic operations are immutable — they return new `Decimal` instances. The `Decimal` class normalizes operands, determines the result scale, delegates to the backend, and wraps the result. Scale is preserved from the operand with the highest scale, or overridden explicitly.

`getBackendName()` returns `"bcmath"`, `"gmp"`, or `"integer-scaled"` for diagnostics. `setBackend()` allows manual override for testing or when the automatic detection order is not desired.

#### 4.2 Implement `DecimalConvertible` Interface

**File: `runtime/packages/decimal/src/Contracts/DecimalConvertible.php`**

```php
interface DecimalConvertible
{
    public function __toDecimal(): \Tyhp\Decimal;
}
```

Any class implementing this interface can be used wherever a `Decimal` is expected.

#### 4.3 Implement `decimal()` Factory Function

**File: `runtime/packages/decimal/src/Functions/decimal.php`**

```php
namespace Tyhp;

if (!\function_exists('\\Tyhp\\decimal')) {
    function decimal(float|int|string|Contracts\DecimalConvertible|null $value = null): Decimal
    {
        return new Decimal($value);
    }
}
```

Guarded by `function_exists` to prevent redeclaration errors if multiple autoloaders load the file.

### Acceptance Criteria

**Core arithmetic (must pass on ALL backends):**
- [x] `new Decimal('10.50')` creates a Decimal with value `'10.50'` and scale 2
- [x] `new Decimal(42)` creates a Decimal with value `'42'` and scale 0
- [x] `decimal('10.50')->add('20.25')` returns Decimal `'30.75'`
- [x] `decimal('100')->divide('3', 10)` returns Decimal `'33.3333333333'`
- [x] `decimal('10')->multiply('3.5')` returns Decimal `'35.0'`
- [x] `decimal('10.5')->modulo('3')` returns the correct remainder
- [x] `decimal('2')->power(10)` returns Decimal `'1024'`
- [x] `decimal('-5.5')->abs()` returns Decimal `'5.5'`
- [x] `decimal('10.5')->compareTo('10.5')` returns `0`
- [x] `decimal('10.5')->greaterThan('10.4')` returns `true`
- [x] `decimal('10.555')->round(2)` rounds correctly
- [x] `decimal('10.555')->format(2, '.', ',')` returns `'10.56'`
- [x] `Decimal` implements `\Stringable` and `\JsonSerializable`
- [x] `decimal()` factory function is available after autoloading

**Backend detection:**
- [x] When `ext-bcmath` is loaded, `Decimal::getBackendName()` returns `"bcmath"`
- [x] When only `ext-gmp` is loaded (no bcmath), `Decimal::getBackendName()` returns `"gmp"`
- [x] When neither extension is loaded, `Decimal::getBackendName()` returns `"integer-scaled"`
- [x] `Decimal::setBackend()` allows manual override for testing
- [ ] All arithmetic tests produce identical results regardless of which backend is active
- [x] The `IntegerScaledBackend` emits a one-time runtime warning recommending `ext-bcmath`

**Backend-specific:**
- [x] `BcMathBackend` delegates to `bcadd()`, `bcsub()`, `bcmul()`, `bcdiv()`, `bccomp()`, etc.
- [x] `GmpBackend` correctly converts between scaled integers and decimal strings
- [x] `IntegerScaledBackend` handles values within `PHP_INT_MAX` range accurately
- [x] `IntegerScaledBackend` falls back to string-based arithmetic for values exceeding `PHP_INT_MAX`
- [x] `DecimalBackend` interface is implementable by third parties for custom backends

---

## Phase 5: Async Module — Promise Foundation

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `High`

### Phase Overview

Implement the core `Promise` class using PHP Fibers. This is the single most important runtime component — it provides the unified async/await primitive that the Tyhp emitter targets. The Promise combines JavaScript's `then`/`catch`/`finally` API with C#'s `Task`-style patterns, all built on cooperative Fiber scheduling.

### Deliverables

**New files:**
- `runtime/packages/async/src/PromiseState.php` — State enum
- `runtime/packages/async/src/Promise.php` — Core Promise class
- `runtime/packages/async/src/Deferred.php` — External resolve/reject control
- `runtime/packages/async/src/Exceptions/InvalidPromiseStateException.php` — State violation exception

### Implementation Details

#### 5.1 Implement `PromiseState` Enum

**File: `runtime/packages/async/src/PromiseState.php`**

```php
enum PromiseState: string
{
    case Pending = 'pending';
    case Fulfilled = 'fulfilled';
    case Rejected = 'rejected';
}
```

#### 5.2 Implement `Promise` Class — Core Structure

**File: `runtime/packages/async/src/Promise.php`**

The `Promise` class wraps a `Fiber` that executes an async operation. The Fiber can be suspended via `_await()` and resumed when the awaited Promise settles.

```php
final class Promise
{
    private PromiseState $state = PromiseState::Pending;
    private mixed $value = null;
    private ?\Throwable $error = null;
    private ?Fiber $fiber = null;
    private array $onSettled = [];

    private static \WeakMap $fiberToPromise;

    public function __construct(callable $executor)
    {
        if (!isset(self::$fiberToPromise)) {
            self::$fiberToPromise = new \WeakMap();
        }

        $this->fiber = new \Fiber(function () use ($executor): void {
            try {
                $result = $executor();
                $this->doResolve($result);
            } catch (\Throwable $e) {
                $this->doReject($e);
            }
        });

        self::$fiberToPromise[$this->fiber] = $this;
        EventLoop::getInstance()->scheduleFiber($this->fiber);
    }
}
```

Key design decisions:

- **One Fiber per Promise.** The executor runs inside a dedicated Fiber. When it returns, the Promise resolves. When it throws, the Promise rejects.
- **WeakMap for Fiber→Promise mapping.** When a Fiber throws an unhandled exception, the event loop can find the owning Promise and reject it. WeakMap prevents memory leaks — when the Fiber is garbage collected, the mapping is automatically removed.
- **Event loop scheduling.** The constructor does not start the Fiber immediately. Instead, it schedules it with the event loop (Phase 6). This ensures consistent ordering and prevents stack overflow from deeply nested Promise chains.

#### 5.3 Implement `_async()` and `_await()` — Emitter Bridge

These are the two functions the Tyhp emitter calls. They are the desugar targets for `async` and `await` keywords.

```php
// Wraps a callable in a Promise. Emitter target for: async function foo() { ... }
public static function _async(callable $fn): self
{
    return new self($fn);
}

// Suspends the current Fiber until $promise settles. Emitter target for: await $expr
public static function _await(self $promise): mixed
{
    if ($promise->state === PromiseState::Fulfilled) {
        return $promise->value;
    }
    if ($promise->state === PromiseState::Rejected) {
        throw $promise->error;
    }

    $currentFiber = \Fiber::getCurrent();
    if ($currentFiber === null) {
        throw new \RuntimeException('await can only be called from within an async context');
    }

    $promise->onSettled[] = static function () use ($currentFiber, $promise): void {
        if ($promise->state === PromiseState::Fulfilled) {
            EventLoop::getInstance()->scheduleFiberResume($currentFiber, $promise->value);
        } else {
            EventLoop::getInstance()->scheduleFiberThrow($currentFiber, $promise->error);
        }
    };

    return \Fiber::suspend();
}
```

Critical fix from existing code: The existing `_await()` stores `['fiber' => $fiber, 'type' => 'resolve']` in callbacks and resumes fibers directly in `processCallbacks()`. This bypasses the event loop's scheduling queue, which can cause re-entrancy bugs. The new design always schedules fiber resumption through the event loop.

#### 5.4 Implement `then()`, `catch()`, `finally()` — JS-Style Chaining

```php
public function then(?callable $onFulfilled = null, ?callable $onRejected = null): self
{
    return new self(function () use ($onFulfilled, $onRejected): mixed {
        try {
            $value = self::_await($this);
            return $onFulfilled !== null ? $onFulfilled($value) : $value;
        } catch (\Throwable $e) {
            if ($onRejected !== null) {
                return $onRejected($e);
            }
            throw $e;
        }
    });
}

public function catch(callable $onRejected): self
{
    return $this->then(null, $onRejected);
}

public function finally(callable $onFinally): self
{
    return $this->then(
        function (mixed $value) use ($onFinally): mixed {
            $onFinally();
            return $value;
        },
        function (\Throwable $error) use ($onFinally): never {
            $onFinally();
            throw $error;
        },
    );
}
```

Each chaining method creates a new Promise whose executor awaits the parent Promise and applies the transformation.

#### 5.5 Implement `resolve()`, `reject()` — Static Factories

```php
public static function resolve(mixed $value = null): self
{
    if ($value instanceof self) {
        return $value;
    }
    return new self(static fn(): mixed => $value);
}

public static function reject(\Throwable $reason): self
{
    return new self(static function () use ($reason): never {
        throw $reason;
    });
}
```

`Promise::resolve($promise)` returns the same Promise (identity), matching JavaScript semantics.

#### 5.6 Implement C#-Style Instance Methods

```php
public function continueWith(callable $continuation): self
{
    return new self(function () use ($continuation): mixed {
        try {
            $value = self::_await($this);
            return $continuation($value, null);
        } catch (\Throwable $e) {
            return $continuation(null, $e);
        }
    });
}

public function wait(int $timeoutMs = -1): mixed
{
    return EventLoop::getInstance()->runUntilSettled($this, $timeoutMs);
}

public function getResult(): mixed
{
    if ($this->state === PromiseState::Pending) {
        throw new InvalidPromiseStateException('Cannot get result of a pending Promise');
    }
    if ($this->state === PromiseState::Rejected) {
        throw $this->error;
    }
    return $this->value;
}

public function getState(): PromiseState { return $this->state; }
public function isCompleted(): bool { return $this->state !== PromiseState::Pending; }
public function isFulfilled(): bool { return $this->state === PromiseState::Fulfilled; }
public function isFaulted(): bool { return $this->state === PromiseState::Rejected; }
public function getError(): ?\Throwable { return $this->error; }
```

`continueWith()` follows the C# `Task.ContinueWith` pattern where the continuation receives both the result and the exception (one will be `null`).

`wait()` blocks the current thread by running the event loop until the Promise settles or the timeout expires. This is the equivalent of C#'s `Task.Wait()` — useful for bridging async code into synchronous entry points.

#### 5.7 Implement `Deferred` — External Resolution Control

**File: `runtime/packages/async/src/Deferred.php`**

```php
final class Deferred
{
    private readonly Promise $promise;
    private ?\Closure $resolveCallback = null;
    private ?\Closure $rejectCallback = null;
    private bool $settled = false;

    public function __construct()
    {
        $this->promise = new Promise(function (): mixed {
            return \Fiber::suspend();
        });
    }

    public function getPromise(): Promise { return $this->promise; }

    public function resolve(mixed $value = null): void;
    public function reject(\Throwable $reason): void;
}
```

`Deferred` creates a Promise that suspends immediately in its executor. External code calls `resolve()` or `reject()` to settle it. This is essential for wrapping callback-based and event-driven APIs into Promises.

**Also: `Promise::withResolvers()` (JS `Promise.withResolvers` equivalent)**

```php
public static function withResolvers(): object
{
    $deferred = new Deferred();
    return (object) [
        'promise' => $deferred->getPromise(),
        'resolve' => fn(mixed $value = null) => $deferred->resolve($value),
        'reject' => fn(\Throwable $reason) => $deferred->reject($reason),
    ];
}
```

#### 5.8 Implement `run()` — Top-Level Entry Point

```php
public static function run(callable $fn): mixed
{
    $promise = self::_async($fn);
    return EventLoop::getInstance()->run($promise);
}
```

`Promise::run()` is the entry point for async code from synchronous PHP. It creates a Promise, starts the event loop, and blocks until the Promise settles. The emitter generates this at the outermost async boundary.

### Acceptance Criteria

- [x] `new Promise(fn() => 42)` creates a pending Promise that resolves to 42 when the event loop ticks
- [x] `Promise::resolve(42)->getResult()` returns 42
- [x] `Promise::reject(new \Exception('fail'))->isFaulted()` returns true
- [x] `Promise::_async(fn() => 42)` creates a Promise wrapping the callable
- [x] `Promise::run(fn() => Promise::_await(Promise::resolve(42)))` returns 42
- [x] `Promise::_await()` outside a Fiber throws `\RuntimeException`
- [x] `then()` chains correctly: `Promise::resolve(2)->then(fn($v) => $v * 3)` resolves to 6
- [x] `catch()` handles rejections: `Promise::reject(new \Exception('fail'))->catch(fn($e) => 'caught')` resolves to 'caught'
- [x] `finally()` runs on both resolution and rejection
- [x] `continueWith()` receives `($value, null)` on success and `(null, $error)` on failure
- [x] `wait()` blocks until the Promise settles
- [x] `wait($timeoutMs)` throws `TimeoutException` if the timeout expires
- [x] `Deferred` creates a Promise that can be resolved externally
- [x] `Promise::withResolvers()` returns an object with `promise`, `resolve`, `reject`
- [x] Multiple `then()` chains on the same Promise all resolve correctly
- [x] Awaiting an already-resolved Promise returns immediately
- [x] Awaiting an already-rejected Promise throws immediately

---

## Phase 6: Async Module — Event Loop with I/O

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `High`

### Phase Overview

Implement the event loop that drives all async operations. The event loop provides cooperative Fiber scheduling, timer management, and real I/O via `stream_select()`. This is the engine behind every `Promise` and every `await`.

### Deliverables

**New files:**
- `runtime/packages/async/src/EventLoop.php` — Singleton event loop

### Implementation Details

#### 6.1 Event Loop Architecture

The event loop follows a tick-based model inspired by Node.js's libuv and ReactPHP's event loop:

```
┌───────────────────────────────────┐
│         Event Loop Tick           │
│                                   │
│  1. Process microtask queue       │  ← Fiber resumes, deferred callbacks
│  2. Process timer queue           │  ← Expired setTimeout/delay callbacks
│  3. Poll I/O (stream_select)      │  ← Non-blocking read/write on streams
│  4. Process I/O callbacks         │  ← Handlers for ready streams
│  5. Check for completion          │  ← All queues empty + root settled?
│                                   │
│  If not done → next tick          │
└───────────────────────────────────┘
```

#### 6.2 Implement `EventLoop` Class

**File: `runtime/packages/async/src/EventLoop.php`**

```php
final class EventLoop
{
    private static ?self $instance = null;

    private \SplQueue $microtaskQueue;
    private \SplPriorityQueue $timerQueue;
    private array $readStreams = [];
    private array $writeStreams = [];
    private array $readCallbacks = [];
    private array $writeCallbacks = [];
    private array $fiberResumeQueue = [];
    private bool $running = false;
    private int $nextTimerId = 0;
    private array $activeTimers = [];

    private function __construct() { /* init queues */ }

    public static function getInstance(): self;

    // --- Fiber Scheduling ---

    public function scheduleFiber(\Fiber $fiber): void;
    public function scheduleFiberResume(\Fiber $fiber, mixed $value = null): void;
    public function scheduleFiberThrow(\Fiber $fiber, \Throwable $error): void;

    // --- Timers ---

    public function delay(int $ms, callable $callback): string;
    public function cancelTimer(string $id): void;
    public function interval(int $ms, callable $callback): string;

    // --- I/O Watchers ---

    public function addReadStream(mixed $stream, callable $callback): void;
    public function addWriteStream(mixed $stream, callable $callback): void;
    public function removeReadStream(mixed $stream): void;
    public function removeWriteStream(mixed $stream): void;

    // --- Deferred / Microtasks ---

    public function defer(callable $callback): void;
    public function queueMicrotask(callable $callback): void;

    // --- Lifecycle ---

    public function run(Promise $rootPromise): mixed;
    public function runUntilSettled(Promise $promise, int $timeoutMs = -1): mixed;
    public function tick(): bool;
    public function stop(): void;
    public function isRunning(): bool;
}
```

#### 6.3 Implement the Tick Loop

```php
public function run(Promise $rootPromise): mixed
{
    $this->running = true;

    while ($this->running) {
        $this->processMicrotasks();
        $this->processTimers();
        $this->pollIO();

        if ($rootPromise->isCompleted() && $this->isEmpty()) {
            break;
        }

        if (!$this->hasPendingWork()) {
            if ($rootPromise->isCompleted()) {
                break;
            }
            $this->waitForIO();
        }
    }

    $this->running = false;

    if ($rootPromise->isFaulted()) {
        throw $rootPromise->getError();
    }

    return $rootPromise->getResult();
}
```

Key design decisions:

- **`processMicrotasks()`** drains the entire microtask queue before moving to timers or I/O. This ensures that `Promise.then()` chains complete synchronously within a single tick, matching JavaScript semantics.
- **`processTimers()`** fires callbacks for all expired timers (those whose target time ≤ `hrtime(true)`). Uses `hrtime(true)` (nanosecond monotonic clock) for timing precision.
- **`pollIO()`** calls `stream_select()` with a calculated timeout. If there are pending timers, the timeout is the time until the next timer fires. If there are no timers and no pending microtasks, it blocks until I/O is ready (preventing busy-waiting).
- **`waitForIO()`** is the blocking `stream_select()` call when there is no immediate work. The timeout is chosen to be the minimum of: the next timer expiry, or a maximum of 1 second (to check for new work periodically).

#### 6.4 Implement I/O Watchers

```php
public function addReadStream(mixed $stream, callable $callback): void
{
    $id = (int) $stream;
    $this->readStreams[$id] = $stream;
    $this->readCallbacks[$id] = $callback;
}

public function removeReadStream(mixed $stream): void
{
    $id = (int) $stream;
    unset($this->readStreams[$id], $this->readCallbacks[$id]);
}

private function pollIO(): void
{
    if (empty($this->readStreams) && empty($this->writeStreams)) {
        return;
    }

    $read = \array_values($this->readStreams);
    $write = \array_values($this->writeStreams);
    $except = null;

    $timeout = $this->calculateIOTimeout();

    $changed = @\stream_select($read, $write, $except, 0, $timeout);

    if ($changed === false || $changed === 0) {
        return;
    }

    foreach ($read as $stream) {
        $id = (int) $stream;
        if (isset($this->readCallbacks[$id])) {
            ($this->readCallbacks[$id])($stream);
        }
    }

    foreach ($write as $stream) {
        $id = (int) $stream;
        if (isset($this->writeCallbacks[$id])) {
            ($this->writeCallbacks[$id])($stream);
        }
    }
}
```

#### 6.5 Implement Fiber Scheduling Methods

```php
public function scheduleFiber(\Fiber $fiber): void
{
    $this->fiberResumeQueue[] = ['fiber' => $fiber, 'action' => 'start'];
    $this->drainFiberQueue();
}

public function scheduleFiberResume(\Fiber $fiber, mixed $value = null): void
{
    $this->fiberResumeQueue[] = ['fiber' => $fiber, 'action' => 'resume', 'value' => $value];
}

public function scheduleFiberThrow(\Fiber $fiber, \Throwable $error): void
{
    $this->fiberResumeQueue[] = ['fiber' => $fiber, 'action' => 'throw', 'error' => $error];
}

private function drainFiberQueue(): void
{
    while (!empty($this->fiberResumeQueue)) {
        $item = \array_shift($this->fiberResumeQueue);
        $fiber = $item['fiber'];

        try {
            match ($item['action']) {
                'start' => $fiber->start(),
                'resume' => $fiber->resume($item['value'] ?? null),
                'throw' => $fiber->throw($item['error']),
            };
        } catch (\Throwable $e) {
            if (isset(Promise::$fiberToPromise[$fiber])) {
                Promise::$fiberToPromise[$fiber]->doReject($e);
            } else {
                throw $e;
            }
        }
    }
}
```

Fiber operations are queued rather than executed immediately. The `drainFiberQueue()` method processes them as microtasks. This prevents stack overflow from deeply nested `await` chains and ensures consistent ordering.

### Acceptance Criteria

- [x] `EventLoop::getInstance()` returns a singleton
- [x] `EventLoop::getInstance()->delay(100, fn() => ...)` fires the callback after ~100ms
- [x] `EventLoop::getInstance()->addReadStream($stream, fn($s) => ...)` fires when data is available
- [x] `stream_select()` is used with proper timeouts (not busy-waiting)
- [x] `run()` blocks until the root Promise settles
- [x] `run()` processes microtasks before timers and I/O (correct priority order)
- [x] `run()` exits cleanly when all work is done
- [ ] Multiple Promises running concurrently resolve in the correct order
- [x] `stop()` breaks out of the event loop
- [x] `cancelTimer()` prevents a scheduled timer from firing
- [x] I/O watchers are properly cleaned up when removed
- [x] The event loop does not busy-wait when there is no immediate work (uses blocking `stream_select()`)
- [x] `runUntilSettled()` respects timeout parameter
- [x] Fiber scheduling prevents stack overflow on deep `await` chains (queued, not recursive)

---

## Phase 7: Async Module — CancellationToken

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Implement the C#-style cooperative cancellation pattern. `CancellationTokenSource` creates tokens that async operations monitor. When `cancel()` is called, all registered callbacks fire and pending operations throw `OperationCancelledException`.

### Deliverables

**New files:**
- `runtime/packages/async/src/CancellationToken.php`
- `runtime/packages/async/src/CancellationTokenSource.php`
- `runtime/packages/async/src/Exceptions/OperationCancelledException.php`

### Implementation Details

#### 7.1 Implement `CancellationToken`

**File: `runtime/packages/async/src/CancellationToken.php`**

```php
final class CancellationToken
{
    private bool $cancelled = false;
    private array $callbacks = [];
    private static ?self $none = null;

    /** @internal — created by CancellationTokenSource */
    public function __construct() {}

    public function isCancellationRequested(): bool
    {
        return $this->cancelled;
    }

    public function throwIfCancellationRequested(): void
    {
        if ($this->cancelled) {
            throw new Exceptions\OperationCancelledException($this);
        }
    }

    public function register(callable $callback): callable
    {
        if ($this->cancelled) {
            $callback();
            return static fn() => null;
        }

        $this->callbacks[] = $callback;
        $index = \array_key_last($this->callbacks);

        return function () use ($index): void {
            unset($this->callbacks[$index]);
        };
    }

    public static function none(): self
    {
        return self::$none ??= new self();
    }

    /** @internal */
    public function cancel(): void
    {
        if ($this->cancelled) {
            return;
        }
        $this->cancelled = true;
        foreach ($this->callbacks as $callback) {
            $callback();
        }
        $this->callbacks = [];
    }
}
```

Key design:
- `register()` returns a deregistration callable, allowing cleanup. If the token is already cancelled when `register()` is called, the callback fires immediately.
- `CancellationToken::none()` returns a singleton token that is never cancelled — used as a default parameter to avoid null checks.
- The `cancel()` method is marked `@internal` because only `CancellationTokenSource` should call it.

#### 7.2 Implement `CancellationTokenSource`

**File: `runtime/packages/async/src/CancellationTokenSource.php`**

```php
final class CancellationTokenSource implements \Tyhp\Contracts\IsDisposable
{
    private readonly CancellationToken $token;
    private bool $disposed = false;
    private ?string $timerId = null;

    public function __construct(?int $cancelAfterMs = null)
    {
        $this->token = new CancellationToken();

        if ($cancelAfterMs !== null) {
            $this->cancelAfter($cancelAfterMs);
        }
    }

    public function getToken(): CancellationToken
    {
        return $this->token;
    }

    public function cancel(): void
    {
        $this->token->cancel();
        $this->cleanupTimer();
    }

    public function cancelAfter(int $ms): void
    {
        $this->cleanupTimer();
        $this->timerId = EventLoop::getInstance()->delay($ms, function (): void {
            $this->cancel();
        });
    }

    public function isCancellationRequested(): bool
    {
        return $this->token->isCancellationRequested();
    }

    public function dispose(): void
    {
        if ($this->disposed) {
            return;
        }
        $this->disposed = true;
        $this->cleanupTimer();
    }

    private function cleanupTimer(): void
    {
        if ($this->timerId !== null) {
            EventLoop::getInstance()->cancelTimer($this->timerId);
            $this->timerId = null;
        }
    }
}
```

`CancellationTokenSource` implements `IsDisposable` so it can be used with Tyhp's `:=` (using) pattern. Disposing the source cancels any pending auto-cancel timer.

#### 7.3 Implement `OperationCancelledException`

**File: `runtime/packages/async/src/Exceptions/OperationCancelledException.php`**

```php
final class OperationCancelledException extends \RuntimeException
{
    public function __construct(
        private readonly CancellationToken $token,
        string $message = 'The operation was cancelled',
        ?\Throwable $previous = null,
    ) {
        parent::__construct($message, 0, $previous);
    }

    public function getToken(): CancellationToken
    {
        return $this->token;
    }
}
```

#### 7.4 Integrate CancellationToken with Promise

Add optional `CancellationToken` parameter to relevant Promise methods. Update `Promise` to accept cancellation:

```php
public static function delay(int $ms, ?CancellationToken $token = null): self
{
    return new self(function () use ($ms, $token): void {
        $token?->throwIfCancellationRequested();

        $deferred = new Deferred();
        $timerId = EventLoop::getInstance()->delay($ms, function () use ($deferred): void {
            $deferred->resolve();
        });

        $unregister = $token?->register(function () use ($timerId, $deferred): void {
            EventLoop::getInstance()->cancelTimer($timerId);
            $deferred->reject(new Exceptions\OperationCancelledException($token));
        });

        try {
            self::_await($deferred->getPromise());
        } finally {
            $unregister?.call($token);
        }
    });
}

public static function run(callable $fn, ?CancellationToken $token = null): mixed;
```

### Acceptance Criteria

- [x] `new CancellationTokenSource()` creates a source with a pending token
- [x] `$source->getToken()->isCancellationRequested()` is `false` initially
- [x] `$source->cancel()` sets `isCancellationRequested()` to `true`
- [x] `$source->cancel()` fires all registered callbacks
- [x] `register()` on an already-cancelled token fires immediately
- [x] `register()` returns a deregistration callable
- [x] Calling the deregistration callable prevents the callback from firing
- [x] `CancellationToken::none()` returns a singleton that is never cancelled
- [x] `throwIfCancellationRequested()` throws `OperationCancelledException` when cancelled
- [x] `new CancellationTokenSource(1000)` auto-cancels after ~1 second
- [x] `cancelAfter()` replaces any previous auto-cancel timer
- [x] `dispose()` cancels the auto-cancel timer
- [x] `OperationCancelledException` carries the token reference
- [x] `Promise::delay(1000, $cancelledToken)` throws `OperationCancelledException`
- [x] `CancellationTokenSource` implements `IsDisposable`

---

## Phase 8: Async Module — Combinators & Utilities

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement all Promise combinator methods (all, allSettled, any, race) and utility methods (delay, timeout, batch). These follow JavaScript's `Promise` API with additional C# aliases and a `CancellationToken` parameter where appropriate.

### Deliverables

**Modified files:**
- `runtime/packages/async/src/Promise.php` — Add static combinator and utility methods

**New files:**
- `runtime/packages/async/src/Exceptions/TimeoutException.php`

**Note:** `AggregateException` is provided by `tyhp/core` (see Phase 3, §3.x) — not created in this phase.

### Implementation Details

#### 8.1 Implement `all()` / `whenAll()`

```php
public static function all(array $promises, ?CancellationToken $token = null): self
{
    return new self(function () use ($promises, $token): array {
        $results = [];
        foreach ($promises as $key => $promise) {
            $token?->throwIfCancellationRequested();
            $results[$key] = self::_await($promise);
        }
        return $results;
    });
}

public static function whenAll(Promise ...$promises): self
{
    return self::all($promises);
}
```

`all()` awaits each Promise sequentially within its executor Fiber. Because each `_await()` suspends the Fiber, the event loop can run other Fibers while waiting. The results array preserves the original keys.

Note: This sequential-await approach is simpler than attempting parallel resolution. In a single-threaded Fiber model, the Promises are already running concurrently (their executors were scheduled when the Promises were created). `all()` just waits for each to complete.

#### 8.2 Implement `allSettled()`

```php
public static function allSettled(array $promises): self
{
    return new self(function () use ($promises): array {
        $results = [];
        foreach ($promises as $key => $promise) {
            try {
                $value = self::_await($promise);
                $results[$key] = (object) ['status' => 'fulfilled', 'value' => $value];
            } catch (\Throwable $e) {
                $results[$key] = (object) ['status' => 'rejected', 'reason' => $e];
            }
        }
        return $results;
    });
}
```

Returns an array of result objects (with `status`, `value` or `reason`), matching JavaScript's `Promise.allSettled()` exactly. Never rejects — even if all Promises reject, the combinator itself resolves.

#### 8.3 Implement `race()` / `whenAny()`

```php
public static function race(array $promises, ?CancellationToken $token = null): self
{
    return new self(function () use ($promises, $token): mixed {
        $token?->throwIfCancellationRequested();

        $deferred = new Deferred();
        $settled = false;

        foreach ($promises as $promise) {
            $promise->then(
                function (mixed $value) use ($deferred, &$settled): void {
                    if (!$settled) {
                        $settled = true;
                        $deferred->resolve($value);
                    }
                },
                function (\Throwable $error) use ($deferred, &$settled): void {
                    if (!$settled) {
                        $settled = true;
                        $deferred->reject($error);
                    }
                },
            );
        }

        $unregister = $token?->register(function () use ($deferred, &$settled, $token): void {
            if (!$settled) {
                $settled = true;
                $deferred->reject(new Exceptions\OperationCancelledException($token));
            }
        });

        try {
            return self::_await($deferred->getPromise());
        } finally {
            $unregister?.call($token);
        }
    });
}

public static function whenAny(Promise ...$promises): self
{
    return self::race($promises);
}
```

Critical fix from existing code: The old `race()` directly called `$fiber->resume($value)` and `$fiber->throw($error)`, bypassing the event loop queue. This can cause undefined behavior if the Fiber is not in a suspended state. The new implementation uses `Deferred` — the first Promise to settle resolves/rejects the shared Deferred, and only the first settlement takes effect.

#### 8.4 Implement `any()`

```php
public static function any(array $promises, ?CancellationToken $token = null): self
{
    return new self(function () use ($promises, $token): mixed {
        $token?->throwIfCancellationRequested();

        if (empty($promises)) {
            throw new \Tyhp\Exceptions\AggregateException([], 'All promises were rejected');
        }

        $deferred = new Deferred();
        $errors = [];
        $remaining = \count($promises);
        $resolved = false;

        foreach ($promises as $key => $promise) {
            $promise->then(
                function (mixed $value) use ($deferred, &$resolved): void {
                    if (!$resolved) {
                        $resolved = true;
                        $deferred->resolve($value);
                    }
                },
                function (\Throwable $error) use ($key, $deferred, &$errors, &$remaining, &$resolved): void {
                    if ($resolved) {
                        return;
                    }
                    $errors[$key] = $error;
                    $remaining--;
                    if ($remaining === 0) {
                        $deferred->reject(
                            new \Tyhp\Exceptions\AggregateException($errors, 'All promises were rejected'),
                        );
                    }
                },
            );
        }

        return self::_await($deferred->getPromise());
    });
}
```

`any()` resolves with the first fulfillment. If all Promises reject, it rejects with an `AggregateException` containing all errors. Matches JavaScript's `Promise.any()`.

#### 8.5 Implement `delay()` and `timeout()`

```php
public static function delay(int $ms, ?CancellationToken $token = null): self
{
    // Implemented in Phase 7 (§7.4)
}

public static function timeout(self $promise, int $ms, ?CancellationToken $token = null): self
{
    return self::race([
        $promise,
        self::delay($ms, $token)->then(function (): never {
            throw new Exceptions\TimeoutException("Operation timed out");
        }),
    ], $token);
}
```

#### 8.6 Implement `batch()`

```php
public static function batch(
    array $items,
    callable $processor,
    int $concurrency = 5,
    ?CancellationToken $token = null,
): self {
    return new self(function () use ($items, $processor, $concurrency, $token): array {
        $results = [];
        $active = [];
        $itemQueue = $items;
        $nextIndex = 0;

        while ($nextIndex < \count($itemQueue) || !empty($active)) {
            $token?->throwIfCancellationRequested();

            while (\count($active) < $concurrency && $nextIndex < \count($itemQueue)) {
                $idx = $nextIndex++;
                $active[$idx] = $processor($itemQueue[$idx]);
            }

            if (!empty($active)) {
                $settledResult = self::_await(self::allSettled($active));
                foreach ($settledResult as $idx => $outcome) {
                    if ($outcome->status === 'fulfilled') {
                        $results[$idx] = $outcome->value;
                        unset($active[$idx]);
                    } else {
                        throw $outcome->reason;
                    }
                }
            }
        }

        \ksort($results);
        return $results;
    });
}
```

Fix from existing code: The old `batch()` compared `$promise === $completed` which doesn't work because `race()` returns a value, not the Promise object. The new implementation uses `allSettled()` on the active batch and processes completed ones.

#### 8.7 Implement `fromGenerator()`

```php
public static function fromGenerator(\Generator $generator): self
{
    return new self(function () use ($generator): mixed {
        $value = null;
        while ($generator->valid()) {
            $yielded = $generator->current();
            if ($yielded instanceof self) {
                try {
                    $value = $generator->send(self::_await($yielded));
                } catch (\Throwable $e) {
                    $value = $generator->throw($e);
                }
            } else {
                $value = $generator->send($yielded);
            }
        }
        return $generator->getReturn();
    });
}
```

Adapts a Generator (coroutine-style) into a Promise. Each `yield $promise` is awaited, and the result is sent back to the generator.

#### 8.8 Implement Exception Classes

**File: `runtime/packages/async/src/Exceptions/TimeoutException.php`**

```php
final class TimeoutException extends \RuntimeException
{
    public function __construct(
        string $message = 'The operation timed out',
        private readonly int $timeoutMs = 0,
        ?\Throwable $previous = null,
    ) {
        parent::__construct($message, 0, $previous);
    }

    public function getTimeoutMs(): int { return $this->timeoutMs; }
}
```

**`AggregateException`** is defined in `tyhp/core` (see Phase 3, §3.x) and used here via the `Tyhp\Exceptions` namespace.

### Acceptance Criteria

- [x] `Promise::all([resolve(1), resolve(2)])` resolves to `[1, 2]`
- [x] `Promise::all([resolve(1), reject(err)])` rejects with `err`
- [x] `Promise::all([])` resolves to `[]`
- [x] `Promise::allSettled([resolve(1), reject(err)])` resolves to `[{status:'fulfilled',value:1}, {status:'rejected',reason:err}]`
- [x] `Promise::race([delay(100)->then(fn()=>1), delay(50)->then(fn()=>2)])` resolves to `2`
- [x] `Promise::race([reject(err), resolve(1)])` behavior depends on which settles first
- [x] `Promise::any([reject(e1), resolve(1)])` resolves to `1`
- [x] `Promise::any([reject(e1), reject(e2)])` rejects with `AggregateException` containing both errors
- [x] `Promise::any([])` rejects with `AggregateException`
- [x] `Promise::delay(100)` resolves after ~100ms
- [x] `Promise::timeout(neverSettles, 100)` rejects with `TimeoutException` after ~100ms
- [x] `Promise::timeout(resolve(42), 100)` resolves to `42` (completes before timeout)
- [x] `Promise::batch([1,2,3,4,5], fn($i) => delay(10)->then(fn()=>$i*2), 2)` processes with max 2 concurrent
- [x] `Promise::batch()` results are in the original order regardless of completion order
- [x] `Promise::fromGenerator($gen)` correctly awaits yielded Promises and sends results back
- [x] `Promise::whenAll()` is an alias for `all()`
- [x] `Promise::whenAny()` is an alias for `race()`
- [x] All combinators pass through `CancellationToken` cancellation correctly

---

## Phase 9: Async Module — Disposable & Async Iteration

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Implement `AsyncIsDisposable`, `DisposableHelper` (explicit programmatic disposal), `DisposableScope` (scope-based auto-dispose via `__destruct()`), and the async iteration interfaces (`AsyncIterator`, `AsyncIterable`, `AsyncKeyValueIterator`).

### Deliverables

**New files:**
- `runtime/packages/async/src/Contracts/AsyncIsDisposable.php`
- `runtime/packages/async/src/DisposableHelper.php`
- `runtime/packages/async/src/DisposableScope.php`
- `runtime/packages/async/src/Contracts/AsyncIterator.php`
- `runtime/packages/async/src/Contracts/AsyncIterable.php`
- `runtime/packages/async/src/Contracts/AsyncKeyValueIterator.php`

### Implementation Details

#### 9.1 Implement `AsyncIsDisposable`

**File: `runtime/packages/async/src/Contracts/AsyncIsDisposable.php`**

```php
interface AsyncIsDisposable
{
    public function disposeAsync(): Promise;
}
```

Classes implementing `AsyncIsDisposable` perform async cleanup (e.g., closing a database connection that requires a network round-trip).

#### 9.2 Implement `DisposableHelper`

**File: `runtime/packages/async/src/DisposableHelper.php`**

```php
final class DisposableHelper
{
    public static function dispose(\Tyhp\Contracts\IsDisposable|AsyncIsDisposable ...$disposables): Promise
    {
        return Promise::_async(function () use ($disposables): void {
            $errors = [];

            foreach (\array_reverse($disposables) as $disposable) {
                try {
                    if ($disposable instanceof AsyncIsDisposable) {
                        Promise::_await($disposable->disposeAsync());
                    } elseif ($disposable instanceof \Tyhp\Contracts\IsDisposable) {
                        $disposable->dispose();
                    }
                } catch (\Throwable $e) {
                    $errors[] = $e;
                }
            }

            if (!empty($errors)) {
                if (\count($errors) === 1) {
                    throw $errors[0];
                }
                throw new \Tyhp\Exceptions\AggregateException($errors, 'One or more errors during disposal');
            }
        });
    }
}
```

Disposes resources in **reverse order** (LIFO), matching C#'s `using` block behavior. Collects all disposal errors and throws an `AggregateException` if multiple fail.

`DisposableHelper` is available for explicit programmatic disposal when manual control is needed. The emitter uses `DisposableScope` (see §9.3) for automatic scope-based disposal.

#### 9.3 Implement `DisposableScope`

**File: `runtime/packages/async/src/DisposableScope.php`**

```php
final class DisposableScope
{
    private array $disposables = [];
    private bool $disposed = false;

    private function __construct() {}

    public static function create(): self
    {
        return new self();
    }

    public function using(\Tyhp\Contracts\IsDisposable|AsyncIsDisposable $resource): mixed
    {
        $this->disposables[] = $resource;
        return $resource;
    }

    public function __destruct()
    {
        if ($this->disposed) {
            return;
        }
        $this->disposed = true;
        $this->disposeAll();
    }

    private function disposeAll(): void
    {
        $errors = [];
        foreach (\array_reverse($this->disposables) as $disposable) {
            try {
                if ($disposable instanceof AsyncIsDisposable) {
                    $promise = $disposable->disposeAsync();
                    if (\Fiber::getCurrent() !== null) {
                        Promise::_await($promise);
                    } else {
                        EventLoop::getInstance()->run($promise);
                    }
                } elseif ($disposable instanceof \Tyhp\Contracts\IsDisposable) {
                    $disposable->dispose();
                }
            } catch (\Throwable $e) {
                $errors[] = $e;
            }
        }
        $this->disposables = [];
        if (!empty($errors)) {
            if (\count($errors) === 1) {
                throw $errors[0];
            }
            throw new \Tyhp\Exceptions\AggregateException($errors, 'One or more errors during disposal');
        }
    }

    public function dispose(): void
    {
        $this->__destruct();
    }
}
```

Scope-based auto-dispose via `__destruct()`. This is what the emitter generates for `:=` (using assignment) blocks. When the `$__scope` variable leaves scope, PHP's reference-counting GC fires `__destruct()` deterministically, even on exception paths.

**Design notes:**
- For async disposables, the destructor checks if it's inside a Fiber context. If yes, it can await via `Promise::_await()`. If no, it uses `EventLoop::run()` to block-wait.
- `dispose()` is available for explicit disposal if needed (e.g., eager cleanup before the scope naturally exits).
- The `$disposed` flag prevents double-disposal (e.g., if `dispose()` is called manually and then `__destruct()` fires).

**Circular reference mitigation and explicit disposal:**

`DisposableScope` relies on `__destruct()` for deterministic disposal, which requires PHP's reference-counting GC to detect that the scope variable has no remaining references. Circular references defeat reference counting and defer destruction to the cycle collector (non-deterministic). The most common circular reference pattern — closures that capture `$this` and are stored as properties on the same object — is handled by the emitter (Story 11) generating `WeakReference`-based captures instead of direct `$this` captures. This breaks the reference cycle at the PHP level without any runtime cost to the Tyhp developer.

Individual resources support idempotent disposal — calling `$resource->dispose()` directly is always safe. When `DisposableScope` iterates its registered resources during `disposeAll()`, it checks each resource's disposed state and skips resources that have already been individually disposed. This means developers can call `$resource->dispose()` for early cleanup without worrying about double-disposal when the scope exits.

For developers who want explicit scope boundaries rather than relying on variable lifetime, Tyhp provides the `using` block syntax (see Phase 10). Unlike the `:=` operator which uses `DisposableScope` and `__destruct()`, the `using` block ALWAYS compiles to a try/finally block for guaranteed deterministic disposal. The `using` block does NOT use the `:=` operator in its declarations — it uses standard assignment (`=`) or no assignment at all. See Phase 10 for the complete grammar, AST, visitor, binder, and emitter design.

#### 9.4 Implement Async Iteration Interfaces

**File: `runtime/packages/async/src/Contracts/AsyncIterator.php`**

```php
interface AsyncIterator
{
    public function next(): Promise;       // Resolves to bool (true = has next)
    public function current(): Promise;    // Resolves to the current value
}
```

**File: `runtime/packages/async/src/Contracts/AsyncIterable.php`**

```php
interface AsyncIterable
{
    public function getAsyncIterator(): AsyncIterator;
}
```

**File: `runtime/packages/async/src/Contracts/AsyncKeyValueIterator.php`**

```php
interface AsyncKeyValueIterator extends AsyncIterator
{
    public function currentKey(): Promise;    // Resolves to the current key
    public function currentValue(): Promise;  // Resolves to the current value
}
```

The emitter transforms `foreach (await $asyncIterable as $item)` into:
```php
$__asyncIter = $asyncIterable->getAsyncIterator();
while (\Tyhp\Promise::_await($__asyncIter->next())) {
    $item = \Tyhp\Promise::_await($__asyncIter->current());
    // ... loop body ...
}
```

### Acceptance Criteria

- [x] `AsyncIsDisposable` interface is loadable and implementable
- [x] `DisposableHelper::dispose($syncDisposable)` calls `dispose()` and returns a resolved Promise
- [x] `DisposableHelper::dispose($asyncDisposable)` awaits `disposeAsync()` and returns a resolved Promise
- [x] `DisposableHelper::dispose($a, $b, $c)` disposes in reverse order ($c, $b, $a)
- [x] Multiple disposal errors produce an `AggregateException`
- [x] Single disposal error throws that error directly
- [x] `DisposableScope::create()` returns a new scope
- [x] `$scope->using($resource)` registers the resource and returns it
- [x] When `$scope` leaves scope, `__destruct()` fires and calls `dispose()` in reverse order
- [x] `DisposableScope` works across exception paths (resources disposed even when exception is thrown)
- [x] `DisposableScope` works in loop scopes (reassigning `$scope` disposes previous)
- [x] `DisposableScope` works with nested scopes (inner scope disposed before outer)
- [x] Async disposables are properly awaited in the destructor
- [x] `$scope->dispose()` allows explicit early disposal
- [x] Double-disposal is prevented by the `$disposed` flag
- [x] `DisposableScope` skips resources that have already been individually disposed (idempotent)
- [x] Calling `$resource->dispose()` directly before scope exit does not cause double-disposal
- [x] `AsyncIterator` interface is loadable and implementable
- [x] `AsyncIterable` interface is loadable and implementable
- [x] `AsyncKeyValueIterator` extends `AsyncIterator`
- [x] A class can implement `AsyncIterable` and return an `AsyncIterator` from `getAsyncIterator()`

---

## Phase 10: `using` Block — Grammar, AST, Visitor, Binder (Emitter Spec Only)

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the `using` block as a Tyhp language feature. The `using` block provides explicit, deterministic resource disposal at a specific code point. Unlike the `:=` operator (which uses `DisposableScope` and relies on `__destruct()`), the `using` block ALWAYS compiles to a try/finally block. This guarantees disposal timing regardless of circular references, GC behavior, or variable lifetime.

**Scope of this phase:** this story delivers the front-end of the feature only — grammar, AST nodes, visitor, and binder. The emitter's try/finally code generation is **specified here** (§10.6) for reference but is **implemented in Story 11** (`IMPLEMENTATION_PLAN_TODO_STORY_11.md`, Phase 7). The emitter-related acceptance items below remain `[ ]` because they are completed by Story 11.

The `using` block does NOT use the `:=` operator — it uses standard assignment (`=`) or no assignment. Resources declared in the `using` header are disposed in reverse order when the block exits, whether normally or via exception.

### Syntax Design

```tyhp
// Single resource with inferred type
using (db = new DatabaseConnection()) {
    db.query("SELECT * FROM users");
}

// Single resource with explicit type
using (DatabaseConnection db = new DatabaseConnection()) {
    db.query("SELECT * FROM users");
}

// Unassigned resource (created and disposed, not accessible by name)
using (new TempFile("/tmp/work")) {
    // temp file exists during this block
}

// Multiple resources (disposed in reverse order)
using (db = new DatabaseConnection(), cache = new CacheConnection()) {
    // both db and cache available
}

// Async resources (awaits disposeAsync() in finally)
using await (conn = new AsyncConnection()) {
    await conn.sendAsync(data);
}
```

**Important:** The `:=` operator is NOT allowed within `using` block declarations. Using `:=` inside a `using()` declaration is a compilation error. The `using` block handles disposal via try/finally — combining it with `:=` (which uses `DisposableScope`) would create conflicting disposal strategies.

### Compiled Output

**Single resource:**
```php
$db = new \DatabaseConnection();
try {
    $db->query("SELECT * FROM users");
} finally {
    if ($db instanceof \Tyhp\Contracts\IsDisposable) {
        $db->dispose();
    }
}
```

**Multiple resources (flat try/finally with null-init):**

Resources are null-initialized, then constructed inside the try block. This ensures that if any constructor throws, already-constructed resources are still disposed. Each dispose call is wrapped in its own try/catch to ensure all resources are attempted, with errors collected into an `AggregateException`:
```php
$db = null;
$cache = null;
try {
    $db = new \DatabaseConnection();
    $cache = new \CacheConnection();
    // body — both $db and $cache available
} finally {
    $__disposeErrors = [];
    if ($cache instanceof \Tyhp\Contracts\IsDisposable) {
        try { $cache->dispose(); } catch (\Throwable $__e) { $__disposeErrors[] = $__e; }
    }
    if ($db instanceof \Tyhp\Contracts\IsDisposable) {
        try { $db->dispose(); } catch (\Throwable $__e) { $__disposeErrors[] = $__e; }
    }
    if (!empty($__disposeErrors)) {
        throw new \Tyhp\Exceptions\AggregateException($__disposeErrors, 'One or more errors during disposal');
    }
}
```

**Unassigned resource:**
```php
$__using_0 = new \TempFile("/tmp/work");
try {
    // body
} finally {
    if ($__using_0 instanceof \Tyhp\Contracts\IsDisposable) {
        $__using_0->dispose();
    }
}
```

**Async resources (`using await`):**
```php
$conn = new \AsyncConnection();
try {
    \Tyhp\Promise::_await($conn->sendAsync($data));
} finally {
    if ($conn instanceof \Tyhp\Contracts\AsyncIsDisposable) {
        \Tyhp\Promise::_await($conn->disposeAsync());
    } elseif ($conn instanceof \Tyhp\Contracts\IsDisposable) {
        $conn->dispose();
    }
}
```

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` — add lexer rule for `T_TYHP_USING` keyword
- `Tyhp/TyhpLang/Grammar/TyhpParser.g4` — add `tyhpUsingBlock`, `tyhpUsingResourceList`, `tyhpUsingResource` rules; plug into `statementWithoutTerminalGrammarAddon`
- `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpStatements.cs` — add `VisitTyhpUsingBlock`, `VisitTyhpUsingResource` visitor methods
- `Tyhp/TyhpLang/Binder/TyhpBinder.CodeBlocks.cs` — add `BindUsingBlock` for scope creation and resource validation

**New files:**
- `Tyhp/TyhpLang/Ast/TyhpUsingBlockAst.cs` — AST node for the using block statement
- `Tyhp/TyhpLang/Ast/TyhpUsingResourceAst.cs` — AST node for individual resource declarations

### Implementation Details

#### 10.1 Lexer: `T_TYHP_USING` Keyword

**File: `Tyhp/TyhpLang/Grammar/TyhpLexer.g4`**

Add a lexer rule that emits `T_TYHP_USING` when the keyword `using` appears in tyhp mode. The token is already declared in the `tokens { }` block but has no matching rule:

```antlr
T_TYHP_USING options{caseInsensitive=true;}:
    'using' {this._languageMode == "tyhp"}? -> type(T_TYHP_USING);
```

This must be ordered to not conflict with `T_TYHP_USING_EQUAL` (`:=`). Since `T_TYHP_USING_EQUAL` matches `:=` (colon-equals), there is no lexer ambiguity — the `using` keyword is a standalone identifier token.

#### 10.2 Parser: `tyhpUsingBlock` Grammar Rules

**File: `Tyhp/TyhpLang/Grammar/TyhpParser.g4`**

Add the `using` block as a `statementWithoutTerminalGrammarAddon` (it's a block statement, no semicolon):

```antlr
// Add to statementWithoutTerminalGrammarAddon alternatives:
statementWithoutTerminalGrammarAddon
    : Statement=tyhpUsingBlock {this.isLanguageMode("tyhp")}?    #tyhpStatementUsingBlock
    ;

tyhpUsingBlock
    : T_TYHP_USING IsAsync=T_TYHP_ASYNC?
      T_OPEN_ROUND_BRACE Resources=tyhpUsingResourceList T_CLOSE_ROUND_BRACE
      Body=blockStatement
    ;

tyhpUsingResourceList
    : Items+=tyhpUsingResource (T_SYM_COMMA Items+=tyhpUsingResource)*
    ;

tyhpUsingResource
    // Typed variable: DatabaseConnection db = new DatabaseConnection()
    : TypeExpr=typeExprWithoutStatic Variable=simpleVariable T_SYM_EQUAL Expr=expr
    // Inferred variable: db = new DatabaseConnection()
    | Variable=simpleVariable T_SYM_EQUAL Expr=expr
    // Unassigned: new TempFile("/tmp/work")
    | Expr=expr
    ;
```

Key design decisions:
- `IsAsync=T_TYHP_ASYNC?` supports `using await (...)` for async disposables
- Standard `=` assignment only — `:=` is NOT allowed here (validated by the binder)
- `blockStatement` is the body, which creates its own scope
- Multiple resources separated by commas

#### 10.2.1 Regenerate ANTLR Parser

After modifying the grammar files:

```bash
./compile_grammar.sh
dotnet clean && dotnet restore && dotnet build
```

Requires `antlr-ng` (`npm install -g antlr-ng`). Grammar regeneration is **not** triggered by `dotnet build` alone.

#### 10.3 AST Nodes

**File: `Tyhp/TyhpLang/Ast/TyhpUsingBlockAst.cs`**

```csharp
using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class TyhpUsingBlockAst : Base2Ast, IStatement
    {
        private const short IS_ASYNC_OFFSET = 7100;

        public bool IsAsync => GetFlag(IS_ASYNC_OFFSET);
        public IReadOnlyList<TyhpUsingResourceAst> Resources =>
            Children.OfType<TyhpUsingResourceAst>().ToList();
        public IStatement? Body => Children.LastOrDefault() as IStatement;

        public static TyhpUsingBlockAst Create(
            bool isAsync,
            IEnumerable<TyhpUsingResourceAst> resources,
            IStatement body,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var children = new List<IBase2Ast>();
            children.AddRange(resources);
            children.Add(body);

            var result = new TyhpUsingBlockAst
            {
                Children = children,
            };
            result.SetContext(context, languageMode);
            if (isAsync) result.SetFlag(IS_ASYNC_OFFSET, true);
            return result;
        }
    }
}
```

**File: `Tyhp/TyhpLang/Ast/TyhpUsingResourceAst.cs`**

```csharp
using Antlr4.Runtime;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.TyhpLang.Ast
{
    public class TyhpUsingResourceAst : Base2Ast
    {
        private const short HAS_TYPE_OFFSET = 7200;
        private const short HAS_VARIABLE_OFFSET = 7201;

        public bool HasTypeAnnotation => GetFlag(HAS_TYPE_OFFSET);
        public bool HasVariable => GetFlag(HAS_VARIABLE_OFFSET);

        /// <summary>Type annotation (if present). Null for inferred/unassigned.</summary>
        public IBase2Ast? TypeExpr => HasTypeAnnotation ? Children.ElementAtOrDefault(0) : null;

        /// <summary>Variable being assigned to (if present). Null for unassigned resources.</summary>
        public IBase2Ast? Variable => HasVariable
            ? Children.ElementAtOrDefault(HasTypeAnnotation ? 1 : 0)
            : null;

        /// <summary>The resource expression (always present).</summary>
        public IExpression? Expression => Children.LastOrDefault() as IExpression;

        public static TyhpUsingResourceAst Create(
            IBase2Ast? typeExpr,
            IBase2Ast? variable,
            IExpression expression,
            ParserRuleContext context,
            string? languageMode = null)
        {
            var children = new List<IBase2Ast>();
            if (typeExpr != null) children.Add(typeExpr);
            if (variable != null) children.Add(variable);
            children.Add(expression);

            var result = new TyhpUsingResourceAst
            {
                Children = children,
            };
            result.SetContext(context, languageMode);
            if (typeExpr != null) result.SetFlag(HAS_TYPE_OFFSET, true);
            if (variable != null) result.SetFlag(HAS_VARIABLE_OFFSET, true);
            return result;
        }
    }
}
```

#### 10.4 Visitor: Build AST from Parse Tree

**File: `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpStatements.cs`**

Add visitor methods to the existing partial class:

```csharp
public TyhpUsingBlockAst VisitTyhpUsingBlock(TyhpParser.TyhpUsingBlockContext context)
{
    var isAsync = context.IsAsync != null;
    var resources = context.Resources.Items
        .Select(r => VisitTyhpUsingResource(r))
        .ToList();
    var body = (IStatement)this.VisitBlockStatement(context.Body);

    return TyhpUsingBlockAst.Create(isAsync, resources, body, context, _languageMode);
}

public TyhpUsingResourceAst VisitTyhpUsingResource(TyhpParser.TyhpUsingResourceContext context)
{
    IBase2Ast? typeExpr = context.TypeExpr != null
        ? this.VisitTypeExprWithoutStatic(context.TypeExpr)
        : null;

    IBase2Ast? variable = context.Variable != null
        ? this.VisitSimpleVariable(context.Variable)
        : null;

    var expression = (IExpression)this.VisitExpr(context.Expr);

    return TyhpUsingResourceAst.Create(typeExpr, variable, expression, context, _languageMode);
}
```

The `statementWithoutTerminalGrammarAddon` override must dispatch to `VisitTyhpUsingBlock`:

```csharp
public override IBase2Ast? VisitStatementWithoutTerminalGrammarAddon(
    TyhpParser.StatementWithoutTerminalGrammarAddonContext context)
{
    if (context is TyhpParser.TyhpStatementUsingBlockContext usingCtx)
    {
        return VisitTyhpUsingBlock(usingCtx.tyhpUsingBlock());
    }
    return base.VisitStatementWithoutTerminalGrammarAddon(context);
}
```

#### 10.5 Binder: Scope and Validation

**File: `Tyhp/TyhpLang/Binder/TyhpBinder.CodeBlocks.cs`**

Add `BindUsingBlock` to handle the `using` block:

```csharp
private void BindUsingBlock(TyhpUsingBlockAst usingBlock, IBaseScope parentScope)
{
    // Create a new code block scope for the using block body
    if (parentScope is not ICodeBlockScopeParent cbParent)
    {
        _diagnostics.AddError(MessageCode.BinderInvalidSymbolTypeForParent,
            _currentFileName, usingBlock.Line, usingBlock.Column, "using block");
        return;
    }

    var blockSymbol = new CodeBlockSymbol(
        $"using@{usingBlock.Line}:{usingBlock.Column}",
        ScopeType.CodeBlock,
        _currentFileName);

    var blockScope = new CodeBlockScope(cbParent, blockSymbol);
    cbParent.AddChildScope(blockScope);

    // Bind each resource declaration
    foreach (var resource in usingBlock.Resources)
    {
        BindUsingResource(resource, blockScope, usingBlock.IsAsync);
    }

    // Bind the body within the new scope
    if (usingBlock.Body != null)
    {
        BindStatementBlock(usingBlock.Body, blockScope);
    }
}
```

Validation rules for `BindUsingResource`:
1. If the resource has a variable, create a `VariableSymbol` in the block scope with `IsDisposable = true`
2. If the resource has no variable, register it as a synthetic variable (`$__using_N`)
3. Validate that the resource expression's type implements `IsDisposable` (sync) or `AsyncIsDisposable` (if `using await`)
4. If the expression contains `:=` operator, emit a compilation error: "`:=` operator cannot be used inside `using` block declarations. Use `=` instead."

#### 10.6 Emitter: try/finally Generation

The emitter (Story 11, Phase 7) handles `TyhpUsingBlockAst` by generating nested try/finally blocks. This is part of Story 11's emitter work, but the design is specified here:

**Single resource:**
```php
$variable = $expression;
try {
    // body
} finally {
    if ($variable instanceof \Tyhp\Contracts\IsDisposable) {
        $variable->dispose();
    }
}
```

**Multiple resources (flat with null-init and error collection):**
```php
$resource1 = null;
$resource2 = null;
try {
    $resource1 = $expr1;
    $resource2 = $expr2;
    // body
} finally {
    $__disposeErrors = [];
    if ($resource2 instanceof \Tyhp\Contracts\IsDisposable) {
        try { $resource2->dispose(); } catch (\Throwable $__e) { $__disposeErrors[] = $__e; }
    }
    if ($resource1 instanceof \Tyhp\Contracts\IsDisposable) {
        try { $resource1->dispose(); } catch (\Throwable $__e) { $__disposeErrors[] = $__e; }
    }
    if (!empty($__disposeErrors)) {
        throw new \Tyhp\Exceptions\AggregateException($__disposeErrors, 'One or more errors during disposal');
    }
}
```

**Async resources (`using await`):**
```php
$variable = $expression;
try {
    // body
} finally {
    if ($variable instanceof \Tyhp\Contracts\AsyncIsDisposable) {
        \Tyhp\Promise::_await($variable->disposeAsync());
    } elseif ($variable instanceof \Tyhp\Contracts\IsDisposable) {
        $variable->dispose();
    }
}
```

**Unassigned resources:**
- Generate synthetic variable names: `$__using_0`, `$__using_1`, etc.
- These variables are not accessible in the body but are used in the finally block

#### 10.7 TyhpLib PHP Dependencies

The `using` block compiles to PHP try/finally with direct `dispose()`/`disposeAsync()` calls. The runtime dependencies are:

- **`tyhp/core`**: `IsDisposable` interface, `AggregateException` (for multi-resource dispose error collection)
- **`tyhp/async`** (only for `using await`): `AsyncIsDisposable` interface, `Promise::_await()`

No new PHP classes are created for this phase — it uses existing classes from `tyhp/core` and `tyhp/async`.

### Acceptance Criteria

- [x] `T_TYHP_USING` keyword is lexed correctly in tyhp mode
- [x] `T_TYHP_USING` does not conflict with `T_TYHP_USING_EQUAL` (`:=`)
- [x] `using (resource = expr) { ... }` parses without errors
- [x] `using (Type resource = expr) { ... }` parses with explicit type
- [x] `using (expr) { ... }` parses without variable assignment
- [x] `using (r1 = e1, r2 = e2) { ... }` parses multiple resources
- [x] `using await (r = expr) { ... }` parses async using
- [ ] `:=` inside `using()` declaration produces a compilation error
- [x] Using block body creates its own scope (variables inside are not accessible outside)
- [ ] Single resource compiles to try/finally with dispose() in finally
- [ ] Multiple resources compile to flat try/finally with null-init and error collection
- [ ] Constructor throw on Nth resource still disposes resources 1..N-1
- [ ] Dispose errors are collected into `AggregateException` (not lost on first failure)
- [ ] Unassigned resources get synthetic `$__using_N` variable names
- [ ] Resources are disposed in reverse declaration order
- [ ] Null-safe disposal (`instanceof` check before dispose)
- [ ] `using await` compiles to async disposal (`_await(disposeAsync())`) in finally
- [ ] `using await` falls back to sync `dispose()` if resource only implements `IsDisposable`
- [x] AST nodes (`TyhpUsingBlockAst`, `TyhpUsingResourceAst`) are well-formed
- [x] Binder creates a proper scope for the using block body
- [ ] Binder validates resource types implement `IsDisposable` or `AsyncIsDisposable`

### Dependencies

- **Requires:** Phase 9 (`IsDisposable`, `AsyncIsDisposable` interfaces), Story 02 (binder infrastructure)
- **Provides for:** Story 11 (emitter references `TyhpUsingBlockAst` for try/finally generation)

---

## Phase 11: Lambda Module — PropertyPath & Expression Trees

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the `tyhp/lambda` Composer package containing the PHP runtime classes for parsable lambdas and expression trees. When a Tyhp function parameter is typed as `PropertyPath<T, R>` or `Expression<T, R>`, the compiler (Story 16) converts inline `fn` expressions into structured objects that the receiving function can inspect, walk, and translate into other domains (SQL, validation rules, serialization, etc.).

This phase creates all the PHP runtime classes. The compiler-side work (checker validation, emitter transformation, type system integration) is handled by Story 16 — this phase only covers the runtime classes that the emitted code instantiates.

### Deliverables

**New files:**
- `runtime/packages/lambda/composer.json` — `tyhp/lambda` package definition, requires `tyhp/core`
- `runtime/packages/lambda/src/PropertyPath.php` — Property access chain extraction
- `runtime/packages/lambda/src/Expression.php` — Base expression tree class
- `runtime/packages/lambda/src/Expression/ExpressionNode.php` — Abstract base for all nodes
- `runtime/packages/lambda/src/Expression/ExpressionVisitor.php` — Visitor pattern for walking trees
- `runtime/packages/lambda/src/Expression/ExpressionSerializer.php` — JSON serialization of trees
- `runtime/packages/lambda/src/Expression/ParameterExpression.php` — Lambda parameter reference
- `runtime/packages/lambda/src/Expression/PropertyAccessExpression.php` — Property access (`$x->prop`)
- `runtime/packages/lambda/src/Expression/MethodCallExpression.php` — Method call (`$x->method()`)
- `runtime/packages/lambda/src/Expression/StaticMethodCallExpression.php` — Static call (`Class::method()`)
- `runtime/packages/lambda/src/Expression/BinaryExpression.php` — Binary operators (`$x + $y`)
- `runtime/packages/lambda/src/Expression/UnaryExpression.php` — Unary operators (`!$x`)
- `runtime/packages/lambda/src/Expression/ConstantExpression.php` — Constants/literals and captured vars
- `runtime/packages/lambda/src/Expression/NullSafeAccessExpression.php` — Null-safe access (`$x?->prop`)
- `runtime/packages/lambda/src/Expression/TernaryExpression.php` — Ternary (`$x ? $y : $z`)
- `runtime/packages/lambda/src/Expression/CoalesceExpression.php` — Null coalescing (`$x ?? $y`)
- `runtime/packages/lambda/src/Expression/ArrayAccessExpression.php` — Array access (`$x[$i]`)
- `runtime/packages/lambda/src/Expression/CastExpression.php` — Type casts (`(int) $x`)
- `runtime/packages/lambda/src/Expression/NewExpression.php` — Object creation (`new Foo()`)

### Implementation Details

#### 11.1 Package Scaffolding

**File: `runtime/packages/lambda/composer.json`**

```json
{
    "name": "tyhp/lambda",
    "description": "Tyhp parsable lambdas: PropertyPath and Expression tree runtime classes",
    "type": "library",
    "license": "MIT",
    "require": {
        "php": ">=8.1",
        "tyhp/core": "^1.0"
    },
    "autoload": {
        "psr-4": {
            "Tyhp\\": "src/"
        }
    },
    "autoload-dev": {
        "psr-4": {
            "Tyhp\\Tests\\": "tests/"
        }
    }
}
```

#### 11.2 Implement `PropertyPath`

**File: `runtime/packages/lambda/src/PropertyPath.php`**

```php
namespace Tyhp;

class PropertyPath extends Expression
{
    public readonly array $path;

    public function __construct(
        string $sourceType,
        string $resultType,
        array $path,
        \Closure $callable
    ) {
        $this->path = $path;

        $paramExpr = new Expression\ParameterExpression('source', $sourceType, 0);
        $body = $paramExpr;
        foreach ($path as $segment) {
            $body = new Expression\PropertyAccessExpression($body, $segment, 'mixed');
        }

        parent::__construct($body, [$paramExpr], $callable, $resultType);
    }

    public function getPropertyName(): string
    {
        return \end($this->path) ?: '';
    }

    public function getPath(): string
    {
        return \implode('.', $this->path);
    }

    public function getSegments(): array
    {
        return $this->path;
    }

    public function getValue(mixed $source): mixed
    {
        return ($this->callable)($source);
    }
}
```

`PropertyPath` extends `Expression`, ensuring that any code accepting `Expression<T, R>` also accepts `PropertyPath<T, R>`. The constructor builds a chain of `PropertyAccessExpression` nodes from the path segments.

#### 11.3 Implement `Expression`

**File: `runtime/packages/lambda/src/Expression.php`**

```php
namespace Tyhp;

class Expression
{
    public readonly Expression\ExpressionNode $body;

    /** @var Expression\ParameterExpression[] */
    public readonly array $parameters;

    public readonly \Closure $callable;

    public readonly string $returnType;

    public function __construct(
        Expression\ExpressionNode $body,
        array $parameters,
        \Closure $callable,
        string $returnType
    ) {
        $this->body = $body;
        $this->parameters = $parameters;
        $this->callable = $callable;
        $this->returnType = $returnType;
    }

    public function __invoke(mixed ...$args): mixed
    {
        return ($this->callable)(...$args);
    }

    public function compile(): \Closure
    {
        return $this->callable;
    }
}
```

#### 11.4 Implement Expression Node Hierarchy

**File: `runtime/packages/lambda/src/Expression/ExpressionNode.php`**

```php
namespace Tyhp\Expression;

abstract class ExpressionNode
{
    public readonly string $type;
    public readonly string $nodeType;

    abstract public function accept(ExpressionVisitor $visitor): mixed;
}
```

**Concrete node classes** — each extends `ExpressionNode`, stores its properties as `public readonly`, and dispatches to the corresponding `ExpressionVisitor` method:

| Node Class | File | Properties | Visitor Method |
|-----------|------|-----------|----------------|
| `ParameterExpression` | `ParameterExpression.php` | `string $name`, `string $paramType`, `int $index` | `visitParameter()` |
| `PropertyAccessExpression` | `PropertyAccessExpression.php` | `ExpressionNode $object`, `string $property` | `visitPropertyAccess()` |
| `NullSafeAccessExpression` | `NullSafeAccessExpression.php` | `ExpressionNode $object`, `string $property` | `visitNullSafeAccess()` |
| `MethodCallExpression` | `MethodCallExpression.php` | `ExpressionNode $object`, `string $method`, `array $arguments` | `visitMethodCall()` |
| `StaticMethodCallExpression` | `StaticMethodCallExpression.php` | `string $class`, `string $method`, `array $arguments` | `visitStaticMethodCall()` |
| `BinaryExpression` | `BinaryExpression.php` | `ExpressionNode $left`, `string $operator`, `ExpressionNode $right` | `visitBinary()` |
| `UnaryExpression` | `UnaryExpression.php` | `string $operator`, `ExpressionNode $operand`, `bool $isPrefix` | `visitUnary()` |
| `ConstantExpression` | `ConstantExpression.php` | `mixed $value` | `visitConstant()` |
| `TernaryExpression` | `TernaryExpression.php` | `ExpressionNode $condition`, `?ExpressionNode $ifTrue`, `ExpressionNode $ifFalse` | `visitTernary()` |
| `CoalesceExpression` | `CoalesceExpression.php` | `ExpressionNode $left`, `ExpressionNode $right` | `visitCoalesce()` |
| `ArrayAccessExpression` | `ArrayAccessExpression.php` | `ExpressionNode $array`, `ExpressionNode $index` | `visitArrayAccess()` |
| `CastExpression` | `CastExpression.php` | `string $targetType`, `ExpressionNode $operand` | `visitCast()` |
| `NewExpression` | `NewExpression.php` | `string $class`, `array $arguments` | `visitNew()` |

Each concrete node follows this pattern:
```php
namespace Tyhp\Expression;

class ParameterExpression extends ExpressionNode
{
    public function __construct(
        public readonly string $name,
        public readonly string $paramType,
        public readonly int $index,
    ) {
        $this->type = $paramType;
        $this->nodeType = 'parameter';
    }

    public function accept(ExpressionVisitor $visitor): mixed
    {
        return $visitor->visitParameter($this);
    }
}
```

#### 11.5 Implement `ExpressionVisitor`

**File: `runtime/packages/lambda/src/Expression/ExpressionVisitor.php`**

```php
namespace Tyhp\Expression;

abstract class ExpressionVisitor
{
    public function visit(ExpressionNode $node): mixed
    {
        return $node->accept($this);
    }

    abstract public function visitParameter(ParameterExpression $node): mixed;
    abstract public function visitPropertyAccess(PropertyAccessExpression $node): mixed;
    abstract public function visitNullSafeAccess(NullSafeAccessExpression $node): mixed;
    abstract public function visitMethodCall(MethodCallExpression $node): mixed;
    abstract public function visitStaticMethodCall(StaticMethodCallExpression $node): mixed;
    abstract public function visitBinary(BinaryExpression $node): mixed;
    abstract public function visitUnary(UnaryExpression $node): mixed;
    abstract public function visitConstant(ConstantExpression $node): mixed;
    abstract public function visitTernary(TernaryExpression $node): mixed;
    abstract public function visitCoalesce(CoalesceExpression $node): mixed;
    abstract public function visitArrayAccess(ArrayAccessExpression $node): mixed;
    abstract public function visitCast(CastExpression $node): mixed;
    abstract public function visitNew(NewExpression $node): mixed;
}
```

Library authors extend this to build SQL translators, serialization mappers, etc.

#### 11.6 Implement `ExpressionSerializer`

**File: `runtime/packages/lambda/src/Expression/ExpressionSerializer.php`**

```php
namespace Tyhp\Expression;

final class ExpressionSerializer
{
    public static function toJson(\Tyhp\Expression $expression): string
    {
        return \json_encode(self::nodeToArray($expression->body), \JSON_PRETTY_PRINT);
    }

    private static function nodeToArray(ExpressionNode $node): array
    {
        $data = ['nodeType' => $node->nodeType, 'type' => $node->type];

        if ($node instanceof PropertyAccessExpression) {
            $data['object'] = self::nodeToArray($node->object);
            $data['property'] = $node->property;
        } elseif ($node instanceof BinaryExpression) {
            $data['left'] = self::nodeToArray($node->left);
            $data['operator'] = $node->operator;
            $data['right'] = self::nodeToArray($node->right);
        } elseif ($node instanceof ConstantExpression) {
            $data['value'] = $node->value;
        } elseif ($node instanceof ParameterExpression) {
            $data['name'] = $node->name;
            $data['index'] = $node->index;
        }
        // ... additional node types follow the same pattern

        return $data;
    }
}
```

Converts expression trees to JSON for passing across API boundaries (e.g., client-side query building sent to a server).

### Acceptance Criteria

- [x] `composer.json` for `tyhp/lambda` is valid and requires `tyhp/core`
- [x] PSR-4 autoloading resolves all `\Tyhp\` classes from the lambda package
- [x] `PropertyPath` stores source/result types, path segments, and callable
- [x] `PropertyPath->getPropertyName()` returns the last path segment
- [x] `PropertyPath->getPath()` returns dot-notation string
- [x] `PropertyPath->getValue($obj)` executes the property access chain
- [x] `PropertyPath` extends `Expression` (is a subtype)
- [x] `Expression` stores body, parameters, callable, and returnType
- [x] `Expression->__invoke()` executes the stored callable
- [x] `Expression->compile()` returns the stored `\Closure`
- [x] All 13 concrete `ExpressionNode` types are implemented with correct properties
- [x] Each node's `accept()` dispatches to the correct `ExpressionVisitor` method
- [x] `ExpressionVisitor` can walk any expression tree
- [x] `ExpressionSerializer::toJson()` produces valid JSON for all node types
- [x] Structural equality can be determined by comparing serialized JSON

### Dependencies

- **Requires:** Phase 1 (monorepo infrastructure), Phase 2 (`tyhp/core` type system for type string references)
- **Provides for:** Story 16 (compiler emits `new \Tyhp\PropertyPath(...)` and `new \Tyhp\Expression(...)` calls that instantiate these classes)

---

## Phase 12: Comprehensive Testing

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Write comprehensive PHPUnit tests for all runtime modules. Tests cover correctness, edge cases, error handling, and integration between modules.

### Deliverables

**New test files:**
- `runtime/packages/core/tests/TypeTest.php`
- `runtime/packages/core/tests/NamedTypeTest.php`
- `runtime/packages/core/tests/GenericObjectTest.php`
- `runtime/packages/core/tests/PropertyAccessorTest.php`
- `runtime/packages/decimal/tests/DecimalTest.php`
- `runtime/packages/async/tests/PromiseTest.php`
- `runtime/packages/async/tests/EventLoopTest.php`
- `runtime/packages/async/tests/CancellationTokenTest.php`
- `runtime/packages/async/tests/DeferredTest.php`
- `runtime/packages/async/tests/CombinatorTest.php`
- `runtime/packages/async/tests/DisposableHelperTest.php`
- `runtime/packages/async/tests/DisposableScopeTest.php`
- `runtime/packages/async/tests/IntegrationTest.php`

### Implementation Details

#### 11.1 Core Module Tests

**TypeTest.php:**
```
- testScalarTypeSingletons: each factory returns cached instance
- testTypeOf: correct type detection for all PHP types
- testTypeIs: runtime type checking for scalars, objects, null
- testUnionType: creation, is(), compatible(), __toString()
- testIntersectionType: creation, is(), compatible()
- testNullableType: wrapping, is() with null values
- testGenericType: creation with NamedType params, genericParameter()
- testCompatibility: mixed ⊃ int, int|string ⊃ int, etc.
- testReadOnly: asReadOnly() returns immutable copy
- testFromClassName: creates class types, instanceof checks
```

**GenericObjectTest.php:**
```
- testInit: tyhpGenericObjectInit sets generic parameters
- testSetProperty: type-checked property setting
- testSetPropertyTypeMismatch: throws IncompatibleTypeException
- testGetProperty: returns stored value (regression test for missing return)
- testIssetUnset: property existence and removal
- testInterfaceGenerics: tyhpGenericObjectInitInterface
- testGetObjectType: returns correct generic type
```

#### 11.2 Decimal Module Tests

**DecimalTest.php:**
```
- testConstruction: from int, float, string, null, DecimalConvertible
- testArithmetic: add, subtract, multiply, divide, modulo, power
- testComparison: compareTo, equals, greaterThan, lessThan, etc.
- testRounding: round, floor, ceil with various modes
- testScale: withScale, scale preservation across operations
- testEdgeCases: division by zero, very large numbers, negative zero
- testFormatting: format(), __toString(), jsonSerialize()
- testStaticHelpers: zero, one, min, max, sum, avg
- testImmutability: operations return new instances, original unchanged
- testDecimalFactory: \Tyhp\decimal() function
```

#### 11.3 Async Module Tests

**PromiseTest.php:**
```
- testResolve: Promise::resolve(42) → fulfilled with 42
- testReject: Promise::reject(new Exception) → rejected
- testAsyncAwait: _async wraps callable, _await retrieves value
- testAwaitOutsideFiber: _await throws RuntimeException
- testAwaitAlreadyResolved: returns immediately
- testThenChaining: value transformation through then()
- testCatchHandling: catch() handles rejection
- testFinallyOnResolve: finally() runs on fulfillment
- testFinallyOnReject: finally() runs on rejection
- testContinueWith: receives (value, null) or (null, error)
- testWait: blocking wait returns value
- testWaitTimeout: throws TimeoutException
- testWithResolvers: external resolution control
- testPromiseOfPromise: resolve(Promise) flattens
```

**EventLoopTest.php:**
```
- testSingleton: getInstance() returns same instance
- testFiberScheduling: scheduled Fibers run in order
- testTimerDelay: delay fires after specified time
- testTimerCancel: cancelled timer does not fire
- testReadStream: callback fires when data available
- testWriteStream: callback fires when writable
- testRemoveStream: removed watcher stops firing
- testMicrotaskPriority: microtasks run before timers
- testNoSpinWait: event loop sleeps when idle (not busy-waiting)
- testConcurrentPromises: multiple Promises interleave correctly
```

**CancellationTokenTest.php:**
```
- testInitialState: not cancelled
- testCancel: sets cancelled, fires callbacks
- testRegisterBeforeCancel: callback fires on cancel
- testRegisterAfterCancel: callback fires immediately
- testDeregistration: deregistered callback doesn't fire
- testNoneToken: singleton, never cancelled
- testSourceAutoCancel: cancelAfter fires after timeout
- testSourceDispose: cleans up timer
- testDelayWithCancellation: cancelled delay throws OperationCancelledException
```

**CombinatorTest.php:**
```
- testAll: waits for all, preserves keys
- testAllWithRejection: rejects on first failure
- testAllSettled: never rejects, captures all outcomes
- testRace: resolves with first settler
- testAny: resolves with first fulfillment
- testAnyAllRejected: throws AggregateException
- testDelay: resolves after time
- testTimeout: rejects if too slow
- testTimeoutSuccess: resolves if fast enough
- testBatch: respects concurrency limit, preserves order
- testFromGenerator: awaits yielded Promises
```

**IntegrationTest.php:**
```
- testAsyncWithIO: read from non-blocking stream
- testAsyncWithCancellation: cancel mid-operation
- testNestedAsync: async calling async calling async
- testConcurrentWithSharedState: multiple Promises accessing shared data
- testDisposableInAsync: :=-style pattern in async context
- testAsyncIterator: foreach-await pattern
- testComplexWorkflow: real-world-like workflow combining all features
```

### Acceptance Criteria

- [ ] `composer test` passes with zero failures
- [ ] All core module tests pass
- [ ] All decimal module tests pass
- [ ] All async module tests pass
- [ ] Integration tests demonstrate cross-module functionality
- [ ] Code coverage is ≥ 80% for all packages (measured by PHPUnit's coverage driver)
- [ ] No regressions from existing behavior (where existing code was functional)
- [x] Edge cases are covered (empty arrays, null values, zero timeouts, etc.)
- [x] Error paths are tested (timeouts, cancellations, type mismatches, etc.)

---

## Appendix A: Complete Promise API Reference

### Instance Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `then(?callable $onFulfilled, ?callable $onRejected)` | `Promise` | JS: Transform resolved value or handle rejection |
| `catch(callable $onRejected)` | `Promise` | JS: Handle rejection (sugar for `then(null, ...)`) |
| `finally(callable $onFinally)` | `Promise` | JS: Run callback regardless of outcome |
| `continueWith(callable $continuation)` | `Promise` | C#: Continuation receives `($value, $error)` |
| `wait(int $timeoutMs = -1)` | `mixed` | C#: Blocking wait, returns value or throws |
| `getResult()` | `mixed` | C#: Get result (throws if pending or faulted) |
| `getState()` | `PromiseState` | State inspection |
| `isCompleted()` | `bool` | C#: Not pending |
| `isFulfilled()` | `bool` | Resolved successfully |
| `isFaulted()` | `bool` | C#: Rejected with error |
| `isCancelled()` | `bool` | C#: Rejected with OperationCancelledException |
| `getError()` | `?\Throwable` | Get rejection reason |

### Static Methods

| Method | Returns | Description |
|--------|---------|-------------|
| `_async(callable $fn)` | `Promise` | **Emitter target** — wraps callable in Promise |
| `_await(Promise $promise)` | `mixed` | **Emitter target** — suspends Fiber until settled |
| `resolve(mixed $value)` | `Promise` | JS: Pre-resolved Promise |
| `reject(\Throwable $reason)` | `Promise` | JS: Pre-rejected Promise |
| `all(array $promises, ?CT $token)` | `Promise` | JS: Wait for all |
| `allSettled(array $promises)` | `Promise` | JS: Wait for all, never rejects |
| `any(array $promises, ?CT $token)` | `Promise` | JS: First fulfillment |
| `race(array $promises, ?CT $token)` | `Promise` | JS: First settlement |
| `whenAll(Promise ...$promises)` | `Promise` | C# alias for `all()` |
| `whenAny(Promise ...$promises)` | `Promise` | C# alias for `race()` |
| `run(callable $fn, ?CT $token)` | `mixed` | C#/Entry: Blocking event loop |
| `delay(int $ms, ?CT $token)` | `Promise` | Timer-based delay |
| `timeout(Promise $p, int $ms, ?CT $token)` | `Promise` | Race against timeout |
| `batch(array $items, callable $fn, int $c, ?CT $token)` | `Promise` | Concurrency-limited batch |
| `fromGenerator(\Generator $gen)` | `Promise` | Coroutine adapter |
| `withResolvers()` | `object` | JS: External resolve/reject control |

*CT = CancellationToken*

---

## Appendix B: Files Created/Modified

### New Files (runtime/)

| File | Package | Purpose |
|------|---------|---------|
| `runtime/composer.json` | — | Monorepo workspace config |
| `runtime/phpunit.xml` | — | Root test runner |
| `runtime/.php-cs-fixer.php` | — | Code style config |
| `runtime/README.md` | — | Developer docs |
| `runtime/packages/core/composer.json` | core | Package definition |
| `runtime/packages/core/src/Type.php` | core | Runtime type system |
| `runtime/packages/core/src/NamedType.php` | core | Named type parameters |
| `runtime/packages/core/src/PropertyAccessor.php` | core | Property hooks runtime |
| `runtime/packages/core/src/Concerns/GenericObject.php` | core | Generic type tracking trait |
| `runtime/packages/core/src/Concerns/HasPropertyAccessors.php` | core | Property hook interception trait |
| `runtime/packages/core/src/Contracts/IsDisposable.php` | core | Sync disposable interface |
| `runtime/packages/core/src/Exceptions/IncompatibleTypeException.php` | core | Type mismatch exception |
| `runtime/packages/core/src/Exceptions/InvalidTypeException.php` | core | Invalid type exception |
| `runtime/packages/decimal/composer.json` | decimal | Package definition |
| `runtime/packages/decimal/src/Decimal.php` | decimal | Decimal arithmetic class (delegates to backend) |
| `runtime/packages/decimal/src/Contracts/DecimalConvertible.php` | decimal | Conversion interface |
| `runtime/packages/decimal/src/Functions/decimal.php` | decimal | Factory function |
| `runtime/packages/decimal/src/Backend/DecimalBackend.php` | decimal | Backend interface |
| `runtime/packages/decimal/src/Backend/BcMathBackend.php` | decimal | bcmath backend (preferred) |
| `runtime/packages/decimal/src/Backend/GmpBackend.php` | decimal | GMP backend (alternative) |
| `runtime/packages/decimal/src/Backend/IntegerScaledBackend.php` | decimal | Pure-PHP fallback backend |
| `runtime/packages/async/composer.json` | async | Package definition |
| `runtime/packages/async/src/Promise.php` | async | Core Promise class |
| `runtime/packages/async/src/PromiseState.php` | async | State enum |
| `runtime/packages/async/src/EventLoop.php` | async | Fiber scheduler + I/O |
| `runtime/packages/async/src/Deferred.php` | async | External resolve/reject |
| `runtime/packages/async/src/CancellationToken.php` | async | Cooperative cancellation |
| `runtime/packages/async/src/CancellationTokenSource.php` | async | Token lifecycle control |
| `runtime/packages/async/src/DisposableHelper.php` | async | Explicit programmatic disposal orchestration |
| `runtime/packages/async/src/DisposableScope.php` | async | Scope-based auto-dispose via __destruct() |
| `runtime/packages/async/src/Contracts/AsyncIsDisposable.php` | async | Async disposable interface |
| `runtime/packages/async/src/Contracts/AsyncIterator.php` | async | Async iterator interface |
| `runtime/packages/async/src/Contracts/AsyncIterable.php` | async | Async iterable interface |
| `runtime/packages/async/src/Contracts/AsyncKeyValueIterator.php` | async | Async key-value iterator |
| `runtime/packages/async/src/Exceptions/OperationCancelledException.php` | async | Cancellation exception |
| `runtime/packages/async/src/Exceptions/TimeoutException.php` | async | Timeout exception |
| `runtime/packages/core/src/Exceptions/AggregateException.php` | core | Multiple error container (used by disposable patterns and async combinators) |
| `runtime/packages/async/src/Exceptions/InvalidPromiseStateException.php` | async | State violation exception |
| `runtime/packages/lambda/composer.json` | lambda | Package definition |
| `runtime/packages/lambda/src/PropertyPath.php` | lambda | Property access chain extraction |
| `runtime/packages/lambda/src/Expression.php` | lambda | Base expression tree class |
| `runtime/packages/lambda/src/Expression/ExpressionNode.php` | lambda | Abstract base for all expression nodes |
| `runtime/packages/lambda/src/Expression/ExpressionVisitor.php` | lambda | Visitor pattern for walking trees |
| `runtime/packages/lambda/src/Expression/ExpressionSerializer.php` | lambda | JSON serialization of expression trees |
| `runtime/packages/lambda/src/Expression/ParameterExpression.php` | lambda | Lambda parameter reference node |
| `runtime/packages/lambda/src/Expression/PropertyAccessExpression.php` | lambda | Property access node |
| `runtime/packages/lambda/src/Expression/MethodCallExpression.php` | lambda | Method call node |
| `runtime/packages/lambda/src/Expression/StaticMethodCallExpression.php` | lambda | Static method call node |
| `runtime/packages/lambda/src/Expression/BinaryExpression.php` | lambda | Binary operator node |
| `runtime/packages/lambda/src/Expression/UnaryExpression.php` | lambda | Unary operator node |
| `runtime/packages/lambda/src/Expression/ConstantExpression.php` | lambda | Constants/literals and captured vars |
| `runtime/packages/lambda/src/Expression/NullSafeAccessExpression.php` | lambda | Null-safe property access node |
| `runtime/packages/lambda/src/Expression/TernaryExpression.php` | lambda | Ternary expression node |
| `runtime/packages/lambda/src/Expression/CoalesceExpression.php` | lambda | Null coalescing node |
| `runtime/packages/lambda/src/Expression/ArrayAccessExpression.php` | lambda | Array access node |
| `runtime/packages/lambda/src/Expression/CastExpression.php` | lambda | Type cast node |
| `runtime/packages/lambda/src/Expression/NewExpression.php` | lambda | Object creation node |
| All `tests/` files | all | PHPUnit test suites |

### New Compiler Files (Phase 10 — Using Block)

| File | Purpose |
|------|---------|
| `Tyhp/TyhpLang/Ast/TyhpUsingBlockAst.cs` | AST node for the `using` block statement |
| `Tyhp/TyhpLang/Ast/TyhpUsingResourceAst.cs` | AST node for individual `using` resource declarations |

**Modified compiler files:**
- `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` — lexer rule for `T_TYHP_USING` keyword
- `Tyhp/TyhpLang/Grammar/TyhpParser.g4` — `tyhpUsingBlock`, `tyhpUsingResourceList`, `tyhpUsingResource` rules
- `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpStatements.cs` — visitor methods for using block
- `Tyhp/TyhpLang/Binder/TyhpBinder.CodeBlocks.cs` — `BindUsingBlock` and `BindUsingResource`

### Removed Files

| File | Reason |
|------|--------|
| `Tyhp/TyhpLib/` (entire directory) | Replaced by `runtime/packages/core/` and `runtime/packages/decimal/` |
| `Promise.php` (root) | Replaced by `runtime/packages/async/src/Promise.php` |
| `src/Tyhp/Promise.php` | Stub, replaced |
| `src/Tyhp/PromiseLoop.php` | Stub, replaced |

---

## Appendix C: Cross-References to Other Stories

| Story | Reference | Impact |
|-------|-----------|--------|
| **Story 06 (TyhpSpec)** | Defines type signatures for `Promise<T>`, `decimal`, `IsDisposable`, etc. | Story 04 provides the PHP implementations that match these signatures. Story 06 has been updated to reference `runtime/packages/` paths. |
| **Story 5 (TyhpLib)** | Originally planned TyhpLib as a single unit | **Fully superseded by Story 04.** Story 5 has been marked as absorbed — all phases defer to Story 04. The cross-cutting concerns (emitter contract, error handling, extension dispatch) have been incorporated into Story 04's design principles and implementation details. |
| **Story 10 (Build Action)** | Originally planned `TyhpLibDistributionService` to copy TyhpLib | Story 04 changes distribution to `composer require`. Story 10 has been updated. |
| **Story 11 (Emitter)** | `AsyncAwaitTransformer` emits `\Tyhp\Promise::_async()` etc. | API is unchanged. Story 11 has been updated to rely on Composer autoloading instead of `require_once`. |
| **Story 07 (Testing)** | PHP test infrastructure for runtime packages | Story 07 has been updated to reference `runtime/packages/` paths. |
| **Story 13 (CLI)** | `tyhp composer` action proxies Composer | Story 04's packages are standard Composer libraries — no changes needed. |
| **Story 30 (Documentation)** | References "Async/await (Fiber-based)" | Story 04 provides the implementation that documentation describes. Updated to reference `tyhp/async` package. |
| **Story 16 (Expression Trees)** | `PropertyPath` and `Expression` PHP runtime classes | Story 04 Phase 11 provides the runtime classes in `tyhp/lambda`. Story 16 handles the compiler-side work (checker, emitter, type system) and adds `tyhp/lambda` as a Composer dependency. |
| **Story 28 (Generic Defaults)** | Uses `Promise<TReturn extends void|mixed = void>` as an example | Story 04 provides the `Promise` class with this generic default. |

---

## Appendix D: Runtime Features Inventory

Complete mapping of Tyhp language features to runtime requirements:

| Feature | Runtime Module | Compile-Time Only? |
|---------|---------------|-------------------|
| Async/await keywords | `tyhp/async` | No — Fiber-based Promise |
| Async foreach | `tyhp/async` | No — AsyncIterator interfaces |
| `:=` using/dispose (sync) | `tyhp/core` | No — IsDisposable interface |
| `using` block (sync) | `tyhp/core` | No — IsDisposable interface, try/finally emit |
| `:=` using/dispose (async) | `tyhp/async` | No — AsyncIsDisposable, DisposableScope (emitter), DisposableHelper (manual) |
| `using await` block (async) | `tyhp/async` | No — AsyncIsDisposable, try/finally emit with _await() |
| Decimal type | `tyhp/decimal` | No — multi-backend Decimal class (bcmath/gmp/integer-scaled) |
| Generic classes | `tyhp/core` | No — GenericObject trait, Type system |
| Typed variables | — | Yes — type-erased at compile time (annotations stripped, no runtime enforcement) |
| Property hooks (PHP < 8.4) | `tyhp/core` | No — PropertyAccessor, HasPropertyAccessors |
| `typeof()` expression | `tyhp/core` | No — `Type::of()` |
| Type aliases | — | Yes — erased at compile time |
| Structs | — | Yes — compiled to PHP arrays |
| Extension methods | — | Yes — rewritten to static calls |
| Operator overloading | — | Yes — rewritten to method calls |
| `with` expression | — | Yes — emitted as clone + property assignment |
| `nameof()` expression | — | Yes — constant-folded to string |
| `default()` expression | — | Yes — constant-folded to default value |
| `variable_exists()` | — | Yes — emitted as `isset()` |
| Type guards / narrowing | — | Yes — compile-time type narrowing |
| Scalar literal types | — | Yes — compile-time type checking only |
| String interpolation | — | Yes — native PHP feature |
| `?.`, `??`, `??=` operators | — | Yes — native PHP 8.0+ operators |
| `is` / `isa` / `isan` | — | Yes — emitted as `instanceof` |
| `init` property modifier | — | Yes — emitted as `readonly` + `__clone()` |
| `internal` visibility | — | Yes — compile-time enforcement only |
| Expression trees (Story 16) | `tyhp/lambda` | No — PropertyPath, Expression, ExpressionNode hierarchy (Phase 11) |
| `PropertyPath<T, R>` parameters | `tyhp/lambda` | No — PropertyPath class for property chain extraction |

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the runtime library modules work correctly. Steps can be skipped, reordered, or modified as needed. The runtime packages are PHP code — testing is done via Composer, PHPUnit, and the PHP CLI. Some steps also involve the Tyhp compiler to verify grammar/binder integration for the `using` block feature.

### Step 1: Verify Monorepo Infrastructure

Confirm the `runtime/` directory structure, Composer workspace, and PHPUnit configuration are set up correctly:

```bash
cd runtime
composer install
```

Expected: Composer installs all dependencies for all four packages (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`) without errors. The `vendor/` directory is populated and autoloading works.

Then verify PHPUnit is configured:

```bash
cd runtime
./vendor/bin/phpunit --list-suites
```

Expected: Lists test suites for `core`, `decimal`, `async`, and `lambda`.

### Step 2: Verify `tyhp/core` — Type System

Run the core package tests:

```bash
cd runtime
./vendor/bin/phpunit --testsuite core
```

Expected: All core tests pass. Then manually verify key classes work:

```php
<?php
// test_core_manual.php — run from runtime/ directory
require_once 'vendor/autoload.php';

use Tyhp\Type;
use Tyhp\NamedType;

// Test Type factory methods
$intType = Type::int();
$stringType = Type::string();
$classType = Type::of('DateTime');

echo "int type: " . $intType->getName() . "\n";       // "int"
echo "string type: " . $stringType->getName() . "\n";  // "string"
echo "class type: " . $classType->getName() . "\n";    // "DateTime"

// Test type checking
echo "isInt: " . ($intType->isInt() ? 'true' : 'false') . "\n";  // true
echo "isString: " . ($intType->isString() ? 'true' : 'false') . "\n";  // false

// Test NamedType
$named = new NamedType('MyClass', ['T' => Type::int()]);
echo "NamedType: " . $named->getName() . "\n";
```

Run:

```bash
cd runtime && php test_core_manual.php && rm test_core_manual.php
```

Expected: Output shows correct type names and boolean results without exceptions.

### Step 3: Verify `tyhp/core` — GenericObject Trait

```php
<?php
// test_generic_object.php
require_once 'vendor/autoload.php';

use Tyhp\Type;
use Tyhp\Concerns\GenericObject;

class Box {
    use GenericObject;

    private mixed $value;

    public function __construct(mixed $value, Type ...$genericTypes) {
        $this->tyhpGenericObjectInit(...$genericTypes);
        $this->value = $value;
    }

    public function getValue(): mixed {
        return $this->value;
    }
}

$intBox = new Box(42, Type::int());
echo "Box value: " . $intBox->getValue() . "\n";
$genericType = $intBox->tyhpGenericObjectGetGenericType('T');
echo "Generic type: " . ($genericType ? $genericType->getName() : 'null') . "\n";
```

Run:

```bash
cd runtime && php test_generic_object.php && rm test_generic_object.php
```

Expected: Box stores the value and the generic type parameter is retrievable at runtime.

### Step 4: Verify `tyhp/core` — IsDisposable Interface

```php
<?php
// test_disposable.php
require_once 'vendor/autoload.php';

use Tyhp\Contracts\IsDisposable;

class TempFile implements IsDisposable {
    private string $path;
    private bool $disposed = false;

    public function __construct(string $path) {
        $this->path = $path;
        echo "Created: $path\n";
    }

    public function dispose(): void {
        if (!$this->disposed) {
            $this->disposed = true;
            echo "Disposed: {$this->path}\n";
        }
    }
}

$file = new TempFile("/tmp/test.txt");
$file->dispose();
$file->dispose(); // should be idempotent
```

Run:

```bash
cd runtime && php test_disposable.php && rm test_disposable.php
```

Expected: "Created: /tmp/test.txt" then "Disposed: /tmp/test.txt" printed once (second dispose is a no-op).

### Step 5: Verify `tyhp/decimal` — Decimal Arithmetic

Run the decimal package tests:

```bash
cd runtime
./vendor/bin/phpunit --testsuite decimal
```

Then manually test:

```php
<?php
// test_decimal.php
require_once 'vendor/autoload.php';

use Tyhp\Decimal;

$a = new Decimal('10.50');
$b = new Decimal('3.25');

$sum = $a->add($b);
$diff = $a->subtract($b);
$prod = $a->multiply($b);
$quot = $a->divide($b);

echo "10.50 + 3.25 = " . $sum->__toString() . "\n";
echo "10.50 - 3.25 = " . $diff->__toString() . "\n";
echo "10.50 * 3.25 = " . $prod->__toString() . "\n";
echo "10.50 / 3.25 = " . $quot->__toString() . "\n";

// Test comparisons
echo "10.50 > 3.25: " . ($a->greaterThan($b) ? 'true' : 'false') . "\n";
echo "10.50 == 10.50: " . ($a->isEqualTo(new Decimal('10.50')) ? 'true' : 'false') . "\n";
```

Run:

```bash
cd runtime && php test_decimal.php && rm test_decimal.php
```

Expected: Correct decimal arithmetic results. The backend (bcmath, gmp, or integer-scaled fallback) should be chosen automatically based on available PHP extensions.

### Step 6: Verify `tyhp/async` — Promise Foundation

Run the async package tests:

```bash
cd runtime
./vendor/bin/phpunit --testsuite async
```

Then manually test basic Promise functionality:

```php
<?php
// test_promise.php
require_once 'vendor/autoload.php';

use Tyhp\Promise;

// Test basic resolve
$result = Promise::run(function () {
    $p = Promise::resolved(42);
    $value = Promise::_await($p);
    echo "Resolved: $value\n";
    return $value;
});
echo "Run result: $result\n";

// Test async/await pattern
$result2 = Promise::run(function () {
    $p1 = Promise::resolved(10);
    $p2 = Promise::resolved(20);

    $a = Promise::_await($p1);
    $b = Promise::_await($p2);

    return $a + $b;
});
echo "Sum: $result2\n";
```

Run:

```bash
cd runtime && php test_promise.php && rm test_promise.php
```

Expected: "Resolved: 42", "Run result: 42", "Sum: 30". Fibers are used for cooperative scheduling.

### Step 7: Verify `tyhp/async` — Event Loop with Timers

```php
<?php
// test_eventloop.php
require_once 'vendor/autoload.php';

use Tyhp\Promise;

$result = Promise::run(function () {
    echo "Before delay\n";
    Promise::_await(Promise::delay(100)); // 100ms delay
    echo "After delay\n";
    return "done";
});
echo "Result: $result\n";
```

Run:

```bash
cd runtime && php test_eventloop.php && rm test_eventloop.php
```

Expected: "Before delay", (brief pause), "After delay", "Result: done". The event loop should handle the timer-based delay.

### Step 8: Verify `tyhp/async` — Combinators

```php
<?php
// test_combinators.php
require_once 'vendor/autoload.php';

use Tyhp\Promise;

$results = Promise::run(function () {
    $p1 = Promise::resolved(1);
    $p2 = Promise::resolved(2);
    $p3 = Promise::resolved(3);

    return Promise::_await(Promise::all([$p1, $p2, $p3]));
});

echo "All results: " . implode(', ', $results) . "\n";  // 1, 2, 3
```

Run:

```bash
cd runtime && php test_combinators.php && rm test_combinators.php
```

Expected: "All results: 1, 2, 3". The `Promise::all()` combinator waits for all promises.

### Step 9: Verify `tyhp/lambda` — PropertyPath

Run the lambda package tests:

```bash
cd runtime
./vendor/bin/phpunit --testsuite lambda
```

Then manually test:

```php
<?php
// test_propertypath.php
require_once 'vendor/autoload.php';

use Tyhp\PropertyPath;

$path = new PropertyPath('user.address.city');
echo "Path: " . $path->__toString() . "\n";
echo "Segments: " . implode(' -> ', $path->getSegments()) . "\n";

$obj = (object) [
    'user' => (object) [
        'address' => (object) [
            'city' => 'Portland'
        ]
    ]
];
$value = $path->resolve($obj);
echo "Resolved: $value\n";
```

Run:

```bash
cd runtime && php test_propertypath.php && rm test_propertypath.php
```

Expected: "Path: user.address.city", "Segments: user -> address -> city", "Resolved: Portland".

### Step 10: Verify `using` Block Grammar and Compilation

Create a test file using the `using` block syntax (`:=` assignment for disposable resources):

```tyhp
<?tyhp
namespace Test\UsingBlock;

use Tyhp\Contracts\IsDisposable;

class DatabaseConnection implements IsDisposable {
    private bool $disposed = false;

    public function query(string $sql): string {
        return "result";
    }

    public function dispose(): void {
        if (!$this->disposed) {
            $this->disposed = true;
        }
    }
}

function processData(): void {
    DatabaseConnection $conn := new DatabaseConnection();
    string $result = $conn->query("SELECT 1");
}
```

Save as `test_using_block.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_using_block.tyhp
```

Expected:
- The `:=` syntax parses correctly
- The variable `$conn` is marked as disposable (`IsDisposable = true` on the `VariableSymbol`)
- The binder recognizes `IsDisposable` interface on `DatabaseConnection`
- The emitter wraps the scope in a try/finally that calls `$conn->dispose()`

### Step 11: Run All Package Tests Together

```bash
cd runtime
./vendor/bin/phpunit
```

Expected: All tests across all four packages pass. The PHPUnit output should show zero failures and zero errors.

### Step 12: Clean Up

```bash
rm -f test_using_block.tyhp
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
