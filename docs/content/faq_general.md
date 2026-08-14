---
title: 'FAQ: General'
---

## What is Tyhp?

Tyhp is a strongly-typed superset of PHP that transpiles to standard, readable PHP code. It adds features like generics, type guards, structs, operator overloads, extension methods, async/await, and more while maintaining full compatibility with the PHP ecosystem. Think of it as what TypeScript is to JavaScript, but for PHP.

## Is Tyhp a new language or a PHP extension?

Tyhp is neither a separate language nor a PHP extension. It is a superset of PHP — every valid PHP file is also valid in a Tyhp project (processed as-is). Tyhp adds new syntax on top of PHP for strong typing and additional language features. The Tyhp compiler is a standalone tool that reads your source files and outputs standard PHP. No PHP extensions or runtime modifications are required.

## Does Tyhp work with existing PHP code?

Yes. Tyhp is designed for gradual adoption. You can include `.php` files alongside `.tyhp` files in the same project. PHP files are passed through to the output unchanged. To get type-checking benefits when calling into existing PHP code, you write tyhpdef files that describe the PHP code's types to the compiler.

## What PHP versions does Tyhp support?

Tyhp currently supports targeting PHP 8.2, 8.3, 8.4, and 8.5. The target version is set via the `output.phpVersion` option in `tyhp.json` (default: `"8.4"`). The compiled output uses only features available in the targeted PHP version.

## Is Tyhp free and open source?

Yes. Tyhp is an open-source project. The compiler, runtime packages, and documentation are all freely available.

## Does Tyhp add runtime overhead?

Most Tyhp features are compile-time only and add zero runtime overhead. Types, generics, type guards, and type aliases are all erased during compilation. The compiled PHP is clean, idiomatic code that performs the same as hand-written PHP. Some features like decimal arithmetic and async/await use lightweight Tyhp runtime Composer packages (`tyhp/core`, `tyhp/decimal`, `tyhp/async`) which are added as Composer dependencies automatically when needed.

## How does Tyhp compare to PHP static analyzers like PHPStan or Psalm?

PHPStan and Psalm analyze existing PHP code using annotations (PHPDoc comments) to find type errors. Tyhp takes a different approach: it is a superset language with first-class type syntax built into the grammar. This means types are part of the language itself, not comments. Tyhp can also add new language features (generics, structs, operator overloads, async/await) that static analyzers cannot provide because they cannot change PHP's syntax. Additionally, Tyhp's compile-time checking is mandatory — you cannot ignore or suppress type errors the way you can with analyzer baselines.

## Can I use Composer packages with Tyhp?

Yes. Since Tyhp compiles to standard PHP, Composer packages work exactly as they do in PHP. To get type-checking for a Composer package, you provide tyhpdef files that describe the package's types. Automatic generation via `tyhp generate_tyhpdef` is **not** in this alpha — write tyhpdefs by hand. PHP builtins come from the `tyhp/php` package.

## What editor or IDE support is available?

This alpha does not include a language server or Xdebug proxy. Use `tyhp lint` and `tyhp build` from the CLI, and debug the emitted PHP. LSP, sourcemaps, and `tyhp xdebug_proxy` are planned for later releases.
