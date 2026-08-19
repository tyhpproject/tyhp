---
title: 'FAQ: Tyhpdef Syntax'
---

## When do I need to write tyhpdef files?

You need tyhpdef files whenever your Tyhp code interacts with PHP code that exists outside the Tyhp project — Composer packages, PHP extensions (like PDO, cURL, or Redis), legacy PHP codebases, or any PHP code that the Tyhp compiler does not compile itself. Tyhpdef files tell the compiler what types those external functions, classes, and constants have so it can type-check your code correctly.

## Can tyhpdef files be auto-generated?

Not in this alpha. `tyhp generate_tyhpdef` is Story 20. Write tyhpdef files by hand, or use the `tyhp/php` package for PHP builtins. You can still copy and edit `.tyhpdef` files from other projects.

## How do I type PHP extensions?

PHP extensions (like PDO, cURL, mbstring, etc.) are typed with tyhpdef files. This alpha ships a PHP 8.2-baseline `tyhp/php` package for commonly bundled extensions. Additional or newer APIs need hand-written tyhpdefs. The `generate_tyhpdef` CLI is not in this alpha.

## How do I add Tyhp extension methods in a tyhpdef file?

You cannot declare a standalone `extension Name { … }` block in a `.tyhpdef` file — that construct is Tyhp-only. On a tyhpdef class, write inline `extension function` / `extension fn` / `extension operator` members with mapping bodies, or attach a Tyhp extension with `use extension` so callers get the methods without their own import. See [Extensions in Tyhpdef](tyhpdef_extensions.md).

## Can I override tyhpdef files?

Yes. If an auto-generated tyhpdef file has incorrect or imprecise types, you can create your own tyhpdef file with corrected declarations. When multiple tyhpdef files declare the same symbol, the one loaded last takes precedence. Place your overrides in a separate directory and make sure it appears after the generated files in your `tyhpdefInclude` glob patterns.

## What happens if a tyhpdef is wrong?

Mismatches between tyhpdef declarations and actual PHP code can lead to runtime errors. For example, if you declare a function returns `string` but the actual PHP function returns `int`, the compiled PHP code may pass a value of the wrong type to a subsequent call, resulting in a `TypeError` at runtime. Always keep your tyhpdef files synchronized with the PHP code they describe. When in doubt, use more permissive types (like `mixed`) rather than risk an incorrect narrow type.

## Do I need tyhpdef for Composer packages?

If you want the Tyhp compiler to type-check your interactions with a Composer package, yes. Without a tyhpdef, the compiler does not know the types of the package's classes, methods, and functions. Write a tyhpdef for the members you actually use. Automatic generation is not in this alpha.

:::tip
You do not need to define every method of a class in a tyhpdef file. Only import the members (methods, properties, constants) that your Tyhp code actually calls. Private members should never be imported since they are not accessible.
:::
