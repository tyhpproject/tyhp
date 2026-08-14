---
title: 'Including Files'
status:
  tier: 0
  story: '10'
  state: complete
---

Tyhp supports PHP's file inclusion statements -- `include`, `include_once`, `require`, and `require_once` -- but enforces compile-time restrictions to maintain type safety. The Tyhp compiler must be able to resolve all included files statically so it can type-check across file boundaries.

## File Inclusion Statements

Tyhp supports all four PHP file inclusion statements. The behaviour is the same as PHP: `include` and `require` load a file, `include_once` and `require_once` load it only if it has not already been loaded, and `require` variants throw a fatal error if the file cannot be found.

```tyhp
<?tyhp

include 'helpers/formatting.tyhp';
include_once 'helpers/math.tyhp';

require 'config.tyhp';
require_once 'bootstrap.tyhp';
```

When you include a `.tyhp` file, the compiler adds it to the compilation unit. It is parsed, bound, type-checked, and emitted alongside the rest of the project. Including a `.php` file is also allowed -- the compiler treats it as standard PHP and applies limited type checking based on available tyhpdef information.

## Static String Requirement

Unlike PHP, Tyhp requires that all file inclusion paths be static strings or constant values. Dynamic includes using variables, expressions, or concatenation are not allowed. The compiler must resolve every included file at compile time to perform cross-file type checking, dependency tracking, and output file generation.

```tyhp
<?tyhp

// Allowed -- static string literal
require 'config/database.tyhp';
require_once 'vendor/autoload.php';

// Allowed -- constant value
const CONFIG_PATH = 'config/app.tyhp';
require CONFIG_PATH;
```

## Compiled PHP Output

Include and require statements pass through to the PHP output unchanged. The paths are preserved exactly as written:

```tyhp
<?tyhp

require_once 'vendor/autoload.php';

App\Application $app = new App\Application();
$app->run();
```

Compiles to:

```php
<?php
declare(strict_types=1);

require_once 'vendor/autoload.php';

$app = new App\Application();
$app->run();
```

## Entry Points

An entry point is any Tyhp source file that contains top-level executable code -- code outside of class, function, or namespace declarations. During the build, the emitter identifies entry point files and emits them as standalone PHP files. If configured, the emitter automatically prepends a Composer autoloader require statement to entry point files.

```tyhp
<?tyhp

// This file is an entry point because it has top-level executable code
require_once 'vendor/autoload.php';

App\Application $app = new App\Application();
$app->run();
```

Entry point files are emitted to the output directory at a path derived from their source location. The build configuration option `build.entryPointAutoloader` controls whether an autoloader require is injected automatically.

## How Compilation of Included Files Works

The Tyhp compiler processes included files as part of the compilation pipeline. When it encounters an include or require statement with a static path, it resolves the file, adds it to the project's file set, and processes it through all phases: parse, bind, check, and emit.

1. The compiler discovers all project source files via the include/exclude patterns in `tyhp.json`.
2. Each source file is parsed into an AST.
3. File-level include/require statements are resolved to find additional files to compile.
4. All files are bound together -- symbols from one file are visible to others via namespace merging.
5. The checker validates types across all files in the project.
6. The emitter splits each file's AST into output file units (one per class, grouped functions, entry points).
7. PHP output files are written to the configured output directory.

## PSR-4 Output Splitting

When the emitter processes source files, it splits them into PSR-4-compliant output units. Each class, interface, trait, or enum declaration gets its own output file. Namespace-level functions are grouped into a single file per namespace. Top-level executable code (entry points) is emitted as standalone files.

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

## Best Practices

:::tip
Use static string literals for all include and require paths. The compiler needs to resolve them at compile time for cross-file type checking.
:::

:::tip
Use `require_once` for files that must exist (configuration, bootstrapping). Use `include_once` for optional helper files.
:::

:::tip
Let the compiler handle cross-file type resolution. Declare classes in one file and reference them from another via `use` statements -- the binder resolves all symbols across all files regardless of include order.
:::

:::tip
Configure include/exclude patterns in `tyhp.json` to control which files the compiler processes. This is more reliable than relying on include statements for file discovery.
:::

:::tip
Use PSR-4 namespace conventions for classes. The emitter produces one output file per class, following PSR-4 directory structure conventions.
:::

## Common Mistakes

:::danger
Don't use variables, expressions, or concatenation in include/require paths. The compiler cannot resolve them statically and reports a compile error.
:::

:::danger
Don't rely on include-order for symbol availability. The binder resolves all symbols across all files regardless of include order -- unlike PHP, Tyhp does not require files to be included before their symbols are used.
:::

:::danger
Don't wrap declarations in a casual or mismatched existence check (e.g. `if (!\class_exists('Foo'))` with a short name inside a namespace). Declaration gates are supported, but Tyhp requires the gate argument to be the exact fully-qualified name of what you declare — see <a href="tyhp_0310_declarationGating.html">Declaration Gating</a>.
:::

:::danger
Don't use `eval()` in Tyhp code. It is banned by default for type safety. The `build.allowEval` configuration option can re-enable it, but this is strongly discouraged.
:::

## Compiler Error Examples

```tyhp
<?tyhp

// ERROR: Include path must be a static string or constant
string $path = 'config/database.tyhp';
// require $path;

// ERROR: Include path must be a static string or constant
string $module = 'auth';
// require 'modules/' . $module . '.tyhp';

// ERROR: Include path must be a static string or constant
// require getConfigPath();
```

All of the above produce compile errors because the paths cannot be resolved statically. Use static strings or constants instead:

```tyhp
<?tyhp

// OK -- static string
require 'config/database.tyhp';

// OK -- constant
const MODULE_PATH = 'modules/auth.tyhp';
require MODULE_PATH;
```
