---
title: 'The `<?tyhp` and `<?tyhpdef` Open Tags'
status:
  tier: 0
  story: '06'
  state: complete
---

Two of the first differences you will notice in Tyhp are a new open tag and a new file extension. These allow the Tyhp compiler to distinguish Tyhp code from PHP code and apply its strongly-typed compilation rules only to the appropriate sections. The new open tags work the same way as the existing PHP open tag `<?php`, except that Tyhp does not support short open tags (`<?`) or echo open tags (`<?=`). If either of those are encountered, the compiler treats them as PHP code.

## The `<?tyhp` Open Tag

The `<?tyhp` open tag marks the start of a Tyhp code block. It is the primary open tag for all Tyhp source files. Like `<?php`, it must be followed by at least one whitespace character. Tyhp code blocks can be closed with `?>` for inline output (same as PHP), and the file can contain multiple Tyhp code blocks interleaved with inline HTML.

```tyhp
<?tyhp

string $greeting = "Hello, Tyhp!";
echo $greeting;
```

When the compiler encounters `<?tyhp`, it enters Tyhp language mode. All code within this block is subject to Tyhp's strong typing rules, new keywords (`struct`, `extension`, `async`, `await`, `with`, `operator`, `is`, `typeof`, `nameof`, `variable_exists`, etc.), and compile-time checks. The block ends at `?>` (for inline output) or at the end of the file.

## The `<?tyhpdef` Open Tag

The `<?tyhpdef` open tag marks the start of a Tyhp type definition file. Tyhpdef files describe the type signatures of external PHP code -- extensions, Composer packages, or any PHP code that your Tyhp project depends on. They provide the compiler with type information without containing any runtime logic.

```tyhp
<?tyhpdef

namespace App\Services;

class PaymentGateway {
    public function charge(decimal $amount, string $currency): bool;
    public function refund(string $transactionId): bool;
}
```

Tyhpdef files are not compiled into PHP output. They exist purely for the compiler's type-checking phase. The compiler parses them and registers their symbols (classes, functions, constants, interfaces, enums, traits, type aliases) into the global scope so that your Tyhp code can reference and type-check against them. Tyhpdef also supports the `deprecated` and `obsolete` keywords to mark declarations that should trigger compiler warnings or errors when used.

## File Extensions

Tyhp uses two file extensions that correspond to the two open tags:

- `.tyhp` -- Tyhp source files containing `<?tyhp` code blocks. These are compiled into `.php` output files.
- `.tyhpdef` -- Tyhp type definition files containing `<?tyhpdef` declarations. These are consumed by the compiler for type checking but produce no output.

The compiler uses the file extension to determine how to parse the file. A `.tyhp` file is parsed starting with the `tyhpSrcFile` grammar rule, which expects `<?tyhp` blocks (with optional inline HTML output between blocks). A `.tyhpdef` file is parsed starting with the `tyhpdefSrcFile` grammar rule, which expects a single `<?tyhpdef` block.

## Strict Types Are Always Enabled

In Tyhp, `declare(strict_types=1)` is always active and cannot be disabled. Every `.tyhp` file is compiled with strict types enforced, regardless of whether you explicitly write a `declare(strict_types=1)` statement. This means PHP's type coercion rules do not apply in Tyhp code -- passing an `int` where a `string` is expected always produces a type error, never a silent conversion.

```tyhp
<?tyhp

// No need to write declare(strict_types=1) -- it is always on
// Attempting to disable it has no effect

function greet(string $name): string {
    return "Hello, " . $name;
}

// greet(42);  // ERROR: int is not assignable to string
```

The compiled PHP output always includes `declare(strict_types=1);` automatically:

```php
<?php
declare(strict_types=1);

function greet(string $name): string {
    return "Hello, " . $name;
}
```

## The `declare(output_file='path')` Directive

Tyhp extends PHP's `declare()` statement with a custom `output_file` directive. This tells the compiler to emit the code that follows the declaration into a specific output file path, overriding the default PSR-4 path computation. This is useful when you need precise control over where a file ends up in the build output.

```tyhp
<?tyhp

declare(output_file='public/index.php');

// Everything after this directive is emitted to public/index.php
require_once 'vendor/autoload.php';

App\Application $app = new App\Application();
$app->run();
```

:::note
The `output_file` directive is a Tyhp-specific extension to `declare()`. It is stripped from the compiled PHP output -- only its effect (controlling the output file path) is applied during the build.
:::

## The `declare(tag='identifier')` Directive

The `tag` directive marks a file with an identifier tag that can be referenced by the build configuration. Tags enable selective compilation, file grouping, or conditional inclusion based on build profiles.

```tyhp
<?tyhp

declare(tag='migrations');

// This file is tagged as a migration file
// Build configuration can include/exclude files by tag
```

## How the Compiler Distinguishes Tyhp from PHP

The lexer recognizes the tag that follows `<?` and sets an internal language mode accordingly. When it sees `<?tyhp` followed by whitespace, it emits a `T_TYHP_OPEN_TAG` token and enters Tyhp mode. When it sees `<?tyhpdef` followed by whitespace, it emits a `T_TYHPDEF_OPEN_TAG` token and enters Tyhpdef mode. When it sees `<?php`, it emits the standard `T_OPEN_TAG` token and enters PHP mode. This language mode determines which keywords are active -- for example, `struct`, `async`, `await`, `is`, `typeof`, and `nameof` are only recognized as keywords in Tyhp mode.

## Mixing `<?tyhp` and `<?php` in `.tyhp` Files

A `.tyhp` file can contain both `<?tyhp` and `<?php` code blocks. This is useful for gradual migration or when you need to include PHP code that uses features outside Tyhp's type system. Each block is independently parsed in its respective language mode:

```tyhp
<?tyhp
// This block is Tyhp code -- strongly typed
string $name = "World";
?>

<!-- This is inline HTML output -->
<h1>Hello!</h1>

<?php
// This block is PHP code -- standard PHP rules apply
$legacyVar = someOldFunction();
?>
```

:::note
While mixing is allowed, the Tyhp compiler does not type-check PHP blocks within `.tyhp` files. PHP blocks are passed through as-is. Only `<?tyhp` blocks receive the full benefit of Tyhp's type system.
:::

## Compiled PHP Output

Every `<?tyhp` code block compiles to a `<?php` block with `declare(strict_types=1)` automatically inserted. Tyhp-specific syntax (variable type declarations, generics, structs, etc.) is transformed into valid PHP. The `<?tyhp` tag itself does not appear in the output.

```tyhp
<?tyhp

namespace App\Controllers;

class HomeController {
    public function index(): string {
        return "Welcome!";
    }
}
```

Compiles to:

```php
<?php
declare(strict_types=1);

namespace App\Controllers;

class HomeController {
    public function index(): string {
        return "Welcome!";
    }
}
```

## Best Practices

:::tip
Always start Tyhp source files with `<?tyhp` as the open tag. This activates the full Tyhp type system and all Tyhp-specific keywords.
:::

:::tip
Use the `.tyhp` extension for all Tyhp source files and `.tyhpdef` for type definition files. The compiler relies on the extension to determine the parsing mode.
:::

:::tip
Use `<?tyhpdef` for describing the types of external PHP libraries. This gives the compiler the type information it needs without requiring you to modify the library code.
:::

:::tip
Use `declare(output_file='path')` when you need a specific output file path, such as for entry point scripts or CLI tools that must live at exact locations.
:::

## Common Mistakes

:::danger
Don't use `<?php` as the open tag for new Tyhp code. Code inside `<?php` blocks is parsed in PHP mode without Tyhp's type checking, keywords, or compile-time features.
:::

:::danger
Don't use short open tags (`<?`) or echo open tags (`<?=`) in `.tyhp` files. The compiler treats these as PHP code or inline output, not Tyhp code.
:::

:::danger
Don't try to disable strict types with `declare(strict_types=0)`. Tyhp always enforces strict types -- the directive is ignored and the output always includes `declare(strict_types=1)`.
:::

:::danger
Don't use `<?tyhp` inside `.tyhpdef` files. Tyhpdef files must start with `<?tyhpdef` and contain only type declarations, not executable code.
:::

## Compiler Error Examples

If you accidentally use `<?php` in a `.tyhp` file and try to use Tyhp-specific syntax, the compiler reports parse errors because those keywords are not active in PHP mode:

```tyhp
<?php
// ERROR: 'struct' is not recognized as a keyword in PHP mode
// struct Point { int $x; int $y; }

// ERROR: Variable type declarations are not valid PHP syntax
// string $name = "hello";
```

Correct usage with `<?tyhp`:

```tyhp
<?tyhp

// All Tyhp keywords are active in <?tyhp blocks
struct Point {
    int $x;
    int $y;
}

// Variable type declarations work
string $name = "hello";
```
