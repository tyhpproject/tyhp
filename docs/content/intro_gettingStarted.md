---
title: 'Getting Started'
status:
  tier: 0
  story: '10'
  state: complete
---

This guide walks you through creating your first Tyhp project, from initialization to running the compiled PHP output. Before proceeding, make sure you have Tyhp installed (see the Installation section).

## Initializing a Project

```
mkdir my-tyhp-project
cd my-tyhp-project
tyhp init
```

This creates a `tyhp.json` configuration file and a basic project structure.

## Project Structure

```
my-tyhp-project/
  tyhp.json          # Project configuration
  src/               # Tyhp source files (.tyhp)
  tyhpdef/           # Type definitions for external PHP code (.tyhpdef)
  build/             # Compiled PHP output (generated)
```

## The tyhp.json Configuration File

```json
{
    "include": [
        "./src/**/*.tyhp",
        "./src/**/*.php"
    ],
    "exclude": [],
    "output": {
        "path": "./build",
        "phpVersion": "8.4",
        "strictTypes": true
    }
}
```

Supported `output.phpVersion` values are `"8.2"`, `"8.3"`, `"8.4"`, and `"8.5"`.

## Writing Your First .tyhp File

Create `src/hello.tyhp`. Tyhp files use the `<?tyhp` opening tag instead of `<?php`:

```tyhp
<?tyhp

function greet(string $name): string
{
    return "Hello, {$name}!";
}

string $message = greet("World");
echo $message;
```

The variable `$message` has an explicit type annotation. The compiler enforces that `greet()` returns a string.

## Building Your Project

```
tyhp build
```

The compiler reads `tyhp.json`, runs parse / bind / check / emit, and writes PHP to the output directory. It also updates `composer.json` so the project can require `tyhp/php`, `tyhp/core`, and any other runtime packages the code needs. Those pins use `output.phpVersion` plus each package’s own `X.Y` (`"8.4"` + `0.0` → `804.0.0`). See the Composer Runtime Packages page.

```
composer install
php build/hello.php
```

Until Packagist lists the `tyhp/*` packages, use a compiler checkout so path repositories can resolve `runtime/packages/`.

## The Compiled Output

The compiled PHP looks like standard PHP. Variable type annotations are erased — they were checked at compile time.

```php
<?php
declare(strict_types=1);

function greet(string $name): string
{
    return "Hello, {$name}!";
}

$message = greet("World");
echo $message;
```

## Checking Without Building

```
tyhp lint
```

Faster than a full build. Useful in development and CI.

## Using PHP Libraries with Tyhpdef

Install `tyhp/php` for PHP builtin types (`\strlen`, `DateTime`, and so on). For other Composer packages, add `.tyhpdef` files under `tyhpdef/` that describe the public API you call. Automatic `tyhp generate_tyhpdef` is **not** in this alpha — write tyhpdefs by hand or copy from existing stubs.

## The Development Workflow

1. Write Tyhp code in `.tyhp` files
2. Run `tyhp lint` for type errors
3. Run `tyhp build` to compile to PHP
4. Run or deploy the compiled PHP

This alpha has no Language Server and no Xdebug/sourcemap debugging. Debug the emitted PHP, or iterate with `tyhp lint`.

## Next Steps

- Read about strong typing and non-nullable types
- Learn about generics, structs, and type aliases
- Explore extension methods, operator overloading, and parsable lambdas
