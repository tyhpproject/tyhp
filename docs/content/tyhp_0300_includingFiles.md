---
title: 'Including Files'
status:
  tier: 0
  story: '10'
  state: complete
---

Tyhp does not use PHP's `include`, `require`, `include_once`, or `require_once` as language statements in Tyhp source. Those constructs always produce **TYHP4801** (`CheckerIncludeNotAllowed`). There is no `import` keyword. Discover source with `tyhp.json` `include` globs, reference types with `use`, and load emitted PHP at runtime with Composer autoload (the emitter can inject the autoloader into entry points).

## How the Compiler Finds Source Files

The compiler's file set comes from `tyhp.json`, not from include statements in `.tyhp` files.

```json
{
    "include": ["./src/**/*.tyhp"],
    "exclude": ["./vendor/**", "./build/**"],
    "tyhpdefInclude": ["./tyhpdef/**/*.tyhpdef"],
    "output": {
        "path": "./build",
        "phpVersion": "8.4",
        "strictTypes": true
    }
}
```

- `include` / `exclude` — globs of `.tyhp` (and optionally `.php`) files to parse, bind, check, and emit
- `tyhpdefInclude` / `tyhpdefExclude` — type-definition files consumed for checking only (no PHP output)

Every matching file is in the compilation unit. Symbols are visible across files via namespaces and `use` statements; you do not need to "include" one Tyhp file from another.

## Composer Autoload and Entry Points

Emitted PHP is ordinary PHP. Runtime loading uses Composer PSR-4 (and `autoload.files` for namespace `_functions.php` files). Enable `build.updateComposer` if you want the compiler to write/merge `composer.json` in the output directory.

An **entry point** is a Tyhp source file with top-level executable code (code outside class, function, or namespace declarations). The emitter writes those as standalone PHP files. `build.entryPointAutoloader` (for example `{ "composer": "vendor/autoload.php" }`) makes the emitter prepend a `require_once` for the autoloader. Per-file override: `declare(autoload='composer'|'none'|path)`.

```tyhp
<?tyhp

declare(output_file='public/index.php');
declare(autoload='composer');

App\Application $app = new App\Application();
$app->run();
```

You cannot write `require_once 'vendor/autoload.php'` in Tyhp source — that is TYHP4801. The emitter injects the autoloader into the PHP output when autoload is configured.

## `declare(output_file=…)`

`declare(output_file='public/index.php')` routes the code that follows to a specific path under `output.path`, instead of the default PSR-4 layout. Use it for front controllers, CLI scripts, and other files that must live at a fixed location. The directive is stripped from the PHP output.

## PSR-4 Output Splitting

When the emitter processes source files, it splits them into PSR-4-compliant output units. Each class, interface, trait, or enum declaration gets its own output file. Namespace-level functions are grouped into a single `_functions.php` per namespace. Top-level executable code (entry points) is emitted as standalone files.

```tyhp
<?tyhp
// File: src/Models.tyhp

namespace App\Models;

class User {
    public function __construct(
        public string $name,
        public string $email
    ) {}
}

class Post {
    public function __construct(
        public string $title,
        public User $author
    ) {}
}
```

The above single source file produces two PHP output files:

```php
<?php
// Output: build/src/Models/User.php
declare(strict_types=1);

namespace App\Models;

class User {
    public function __construct(
        public string $name,
        public string $email
    ) {}
}
```

```php
<?php
// Output: build/src/Models/Post.php
declare(strict_types=1);

namespace App\Models;

use App\Models\User;

class Post {
    public function __construct(
        public string $title,
        public User $author
    ) {}
}
```

Output paths follow the type's fully-qualified name (and `output.path` / `output.namespacePrefix`). The `psr4` config key feeds generated Composer autoload mappings; it does not remap emit folders.

## How Compilation Works

1. The compiler discovers project source files via the include/exclude patterns in `tyhp.json`.
2. Each source file is parsed into an AST.
3. All files are bound together — symbols from one file are visible to others via namespace merging and `use`.
4. The checker validates types across all files in the project.
5. The emitter splits each file's AST into output file units (one per class, grouped functions, entry points).
6. PHP output files are written to the configured output directory. Entry points may receive an injected autoloader `require_once`.

## `eval()`

`eval()` is discouraged. Using it produces info diagnostic **TYHP4800**. Set `build.allowEval` to `true` in `tyhp.json` to silence that diagnostic. Code inside `eval()` is not type-checked. Prefer ordinary Tyhp/PHP modules and Composer autoload.

## Best Practices

:::tip
Put every `.tyhp` file you want compiled on an `include` glob in `tyhp.json`. That is how the compiler finds sources.
:::

:::tip
Use `use` statements for types and functions. The binder resolves symbols across all files in the project; you do not include one Tyhp file from another.
:::

:::tip
Configure `build.entryPointAutoloader` (and `declare(autoload=…)` when needed) so entry-point PHP receives `require_once` for Composer. Do not write `require` / `include` in Tyhp source.
:::

:::tip
Use `declare(output_file='path')` when a file must be emitted at a specific location (public index, CLI binary).
:::

:::tip
Use PSR-4 namespace conventions for classes. The emitter produces one output file per class, following the type's namespace path.
:::

## Common Mistakes

:::danger
Don't write `include`, `require`, `include_once`, or `require_once` in Tyhp source. They always error with TYHP4801. There is no `import` keyword either — use `use` plus Composer autoload plus `tyhp.json` include globs.
:::

:::danger
Don't wrap declarations in a casual or mismatched existence check (e.g. `if (!\class_exists('Foo'))` with a short name inside a namespace). Declaration gates are supported, but Tyhp requires the gate argument to be the exact fully-qualified name of what you declare — see <a href="tyhp_0310_declarationGating.html">Declaration Gating</a>.
:::

:::danger
Don't use `eval()` unless you have a hard requirement. It is info TYHP4800 unless `build.allowEval` is true, and the evaluated string is not type-checked.
:::

## Compiler Error Examples

```tyhp
<?tyhp

// ERROR TYHP4801 — include/require is not allowed in Tyhp
// require 'config/database.tyhp';
// require_once 'vendor/autoload.php';
// include_once 'helpers/math.tyhp';
```

Discover those files with `include` globs, `use` the types you need, and let the emitter inject the autoloader on entry points:

```tyhp
<?tyhp

declare(output_file='public/index.php');
declare(autoload='composer');

use App\Application;

Application $app = new Application();
$app->run();
```
