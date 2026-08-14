# Tyhp Runtime Library

The PHP runtime library that compiled Tyhp code depends on at execution time. Every Tyhp language feature that cannot be fully erased at compile time has corresponding PHP runtime support provided by these packages.

## Packages

| Package | Description |
|---------|-------------|
| `tyhp/php` | Type definitions for PHP builtins and bundled extensions (PHP 8.2 baseline) |
| `tyhp/core` | Type system, generics, typed variables, property accessors |
| `tyhp/decimal` | Arbitrary-precision decimal arithmetic via bcmath |
| `tyhp/async` | Promise, event loop, CancellationToken, async iteration |
| `tyhp/lambda` | PropertyPath and Expression tree runtime classes |

## Development Setup

```bash
# Install dependencies (symlinks local packages automatically)
composer install

# Run tests
composer test

# Check code style
composer cs-check

# Fix code style
composer cs-fix
```

## Architecture

The runtime is organized as a Composer monorepo. During development, packages are symlinked via Composer path repositories. When published to Packagist, consumers install the real packages.

Runtime PHP packages share the `Tyhp\` root namespace and require PHP >= 8.2. `tyhp/php` is tyhpdefs only (no emitted PHP).

### PSR-4 Namespace Ownership

All three packages register the `Tyhp\` root prefix for PSR-4 autoloading, but each package owns distinct sub-namespaces:

| Package | Owned Sub-namespaces |
|---------|---------------------|
| `tyhp/core` | `Tyhp\TypeSystem\`, `Tyhp\Generics\`, `Tyhp\Variables\`, `Tyhp\Accessors\` |
| `tyhp/decimal` | `Tyhp\Decimal\` |
| `tyhp/async` | `Tyhp\Async\` |

**Important:** Top-level namespace subdirectory names must not overlap across packages. If two packages define classes under the same `Tyhp\Foo\` sub-namespace, Composer's autoloader will silently resolve to whichever package path it finds first, causing unpredictable class loading. Always ensure each package owns exclusive subdirectory names under `src/`.

### PHPUnit Schema Validation

The `phpunit.xml` configuration references the schema at `vendor/phpunit/phpunit/phpunit.xsd`, which is a relative path that only resolves after running `composer install`. If you see schema validation errors in your IDE before installation, this is expected — run `composer install` first to populate the `vendor/` directory.

### PHP Stubs with annotations (analyze annotations to build php tyhpdefs)

Layer 2 enrichment inputs for Story 20 / 21 (harvest into native tyhpdef; credit in headers / `SOURCES.md`). Not loaded by the compiler directly:

- https://github.com/vimeo/psalm/tree/6.x/stubs
- https://github.com/phpstan/phpstan-src/tree/2.2.x/stubs
- https://github.com/phan/phan/tree/v6/internal/stubs
- https://github.com/jetbrains/phpstorm-stubs

### Hand-enriched overlay baseline (Layer 3)

`runtime/php-extensions/overlays/` holds a full-file snapshot of the current hand-enriched extension tyhpdefs (`php8.2.9/`, `Decimal/`) so regenerators cannot silently destroy that work. See `php-extensions/overlays/README.md`. The harvest tree remains `runtime/php-extensions/php8.2.9/`. The Composer package the compiler and Packagist use is `runtime/packages/php/`.
