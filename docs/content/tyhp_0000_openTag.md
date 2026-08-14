---
title: 'The `<?tyhp` and `<?tyhpdef` Open Tags'
status:
  tier: 0
  story: '06'
  state: complete
---

Two of the first differences you will notice in Tyhp are a new open tag and a new file extension. These allow the Tyhp compiler to distinguish Tyhp code from PHP code and apply its strongly-typed compilation rules only to the appropriate sections. The new open tags work the same way as the existing PHP open tag `<?php`. The echo open tag `<?=` is PHP echo mode (not Tyhp). A bare `<?` is not a PHP short open tag — Tyhp uses `<?` only as the start of `<?tyhp`, `<?tyhpdef`, or `<?php`.

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

## Tagless source (`source.tagless`)

Set `"source": { "tagless": true }` in `tyhp.json` (or `source.tagless` on a `package.tyhp.json` manifest) to parse `.tyhp` / `.tyhpdef` files as Tyhp/tyhpdef from the first token, without an open tag. Closing `?>` is not allowed in that mode. Use this for files that are entirely Tyhp (no interleaved HTML). The default remains tagged `<?tyhp` / `<?tyhpdef` files.

## Strict Types Are Always Enabled

In Tyhp, compiled PHP output always includes `declare(strict_types=1);`. Every `.tyhp` file is compiled with strict types enforced. This means PHP's type coercion rules do not apply in Tyhp code -- passing an `int` where a `string` is expected always produces a type error, never a silent conversion.

```tyhp
<?tyhp

// No need to write declare(strict_types=1) -- compiled output always includes it

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
declare(autoload='composer');

// Everything after output_file is emitted to public/index.php
App\Application $app = new App\Application();
$app->run();
```

:::note
`output_file` and `autoload` are Tyhp-specific `declare()` keys. They are stripped from the compiled PHP output — only their effects (output path and whether the emitter injects an autoloader `require_once`) apply during the build. Tyhp source cannot write `require` / `include` itself (that is always TYHP4801). See the Including Files page.
:::

## The `declare(autoload=…)` Directive

`declare(autoload=…)` is a per-file override for `build.entryPointAutoloader`. Values:

- `'composer'` — inject the configured Composer autoloader (default `vendor/autoload.php` under the output directory)
- `'none'` (or empty) — do not inject an autoloader for this file
- a config map key or a path — look up `build.entryPointAutoloader` first, otherwise treat the value as a path relative to the output directory

The emitter writes the `require_once` into the PHP output. Do not put `require_once 'vendor/autoload.php'` in Tyhp source.

## How the Compiler Distinguishes Tyhp from PHP

The lexer recognizes the tag that follows `<?` and sets an internal language mode accordingly. When it sees `<?tyhp` followed by whitespace, it emits a `T_TYHP_OPEN_TAG` token and enters Tyhp mode. When it sees `<?tyhpdef` followed by whitespace, it emits a `T_TYHPDEF_OPEN_TAG` token and enters Tyhpdef mode. When it sees `<?php`, it emits the standard `T_OPEN_TAG` token and enters PHP mode. When it sees `<?=`, it enters PHP echo mode (`phpEcho`). A bare `<?` that is not followed by `tyhp`, `tyhpdef`, or `php` is not a PHP short open tag. This language mode determines which keywords are active -- for example, `struct`, `async`, `await`, `is`, `typeof`, and `nameof` are only recognized as keywords in Tyhp mode.

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
Don't use `<?=` expecting Tyhp mode — that is PHP echo mode. Don't write a bare `<?` as a PHP short open tag; it is not treated as PHP. Use `<?tyhp` (or tagless source) for Tyhp, and `<?php` only when you intend PHP mode.
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
