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

This creates a `tyhp.json` configuration file, a root `composer.json` (with `tyhp/php` and `tyhp/core` pins), `src/index.tyhp`, and empty `tyhpdef/` and `build/` directories.

## Project Structure

```
my-tyhp-project/
  tyhp.json          # Project configuration
  composer.json      # Pins tyhp/php and tyhp/core
  src/index.tyhp     # Sample entry file
  tyhpdef/           # Type definitions for external PHP code (.tyhpdef)
  build/             # Compiled PHP output (generated)
```

## The tyhp.json Configuration File

```json
{
    "include": ["src/**/*.tyhp"],
    "exclude": ["vendor/**", "node_modules/**"],
    "source": {
        "tagless": false
    },
    "output": {
        "path": "build/",
        "phpVersion": "8.4",
        "strictTypes": true,
        "comments": true
    }
}
```

Supported `output.phpVersion` values are `"8.2"`, `"8.3"`, `"8.4"`, and `"8.5"`.

## Writing Your First .tyhp File

`tyhp init` writes `src/index.tyhp`. Tyhp files use the `<?tyhp` opening tag instead of `<?php`:

```tyhp
<?tyhp
declare(strict_types=1);
namespace App;

echo 'Hello, World!';
```

## Building Your Project

```
tyhp build
```

The compiler reads `tyhp.json`, runs parse / bind / check / emit, and writes PHP to the output directory. `tyhp init` already pins `tyhp/php` and `tyhp/core` in the project `composer.json`. `tyhp build` updates Composer metadata only when `build.updateComposer` is `true` (default **false**); otherwise it reports which packages the emit needs. Pins use `output.phpVersion` plus each package’s own `X.Y` (`"8.4"` + `0.0` → `804.0.0`). See the Composer Runtime Packages page.

```
composer install
php build/src/index.php
```

Until Packagist lists the `tyhp/*` packages, use a compiler checkout so path repositories can resolve `runtime/packages/`.

## The Compiled Output

The compiled PHP looks like standard PHP. Variable type annotations are erased — they were checked at compile time.

```php
<?php
declare(strict_types=1);

namespace App;

echo 'Hello, World!';
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
