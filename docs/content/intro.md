---
title: 'What is Tyhp?'
---

:::warning Alpha
Tyhp **805.0.0-alpha.1** is a public alpha. The compiler, type checker, emitter, CLI (`build` / `lint` / `init`), and parsable lambdas work. Language Server, sourcemaps, Xdebug proxy, and the tyhpdef generator CLI are **not** in this release. Syntax and package versions may still change.
:::

Tyhp is a strongly-typed superset of PHP that transpiles to standard, readable PHP code. It brings the safety and expressiveness of compiled, strongly-typed languages to the PHP ecosystem while preserving full backward compatibility with existing PHP code and libraries. Think of Tyhp as doing for PHP what TypeScript does for JavaScript — it is still PHP at its core, enhanced with a powerful type system and modern language features.

All type checking in Tyhp happens at compile time. The Tyhp compiler verifies your code, catches type errors, and then erases all type information during transpilation. The resulting PHP output is clean, readable, and runs on any standard PHP runtime. Compiled programs that use Tyhp language features depend on small Composer packages (`tyhp/core`, `tyhp/async`, `tyhp/decimal`, `tyhp/lambda`). PHP builtins are typed via `tyhp/php`.

## Key Features

- Strong typing with compile-time type checking and type inference — types are non-nullable by default
- Generics with type erasure — write type-safe generic classes and functions that compile to standard PHP
- Structs — lightweight value types backed by PHP arrays for high-performance data structures
- Type guards and smart casts — automatic type narrowing after instanceof, is, and null checks without explicit casts
- Async/await syntax — write asynchronous code with Promise-based concurrency that compiles to PHP
- Extension methods — add methods to existing types without modifying their source
- Operator overloading — define custom behavior for arithmetic and comparison operators on your classes
- Type aliases — create named types for complex type expressions
- Property accessors — computed and validated getters/setters with clean syntax
- Disposable pattern — automatic resource cleanup with deterministic disposal via the using pattern
- Compile-time constructs — nameof(), typeof(), and default() evaluated at compile time
- Tyhpdef files — type definition files for describing external PHP libraries, similar to TypeScript's .d.ts files
- Parsable lambdas / expression trees — capture `fn` expressions as data for query builders and similar APIs

## Why Use Tyhp?

PHP is a mature, widely-deployed language with a vast ecosystem. Tyhp enhances it without replacing it. By catching type errors at compile time rather than at runtime, Tyhp eliminates entire categories of bugs before your code ever runs. The compiler acts as a safety net — verifying type compatibility, null safety, generic constraints, and more — then produces clean PHP that you can deploy anywhere PHP runs.

- Zero runtime overhead for types — all type checking is erased during compilation
- Gradual adoption — mix .tyhp and .php files in the same project
- Full PHP interop — use Composer packages and PHP extensions via tyhpdef files
- Readable output — the compiled PHP is clean and human-readable, not obfuscated
- Familiar syntax — if you know PHP, you already know most of Tyhp

## How It Works

The Tyhp compiler processes your source files through a multi-phase pipeline: parsing, binding (symbol and scope resolution), type checking, and emission. Each phase catches different categories of errors. The emitter transforms Tyhp-specific constructs — generics, structs, extension methods, operator overloads, async/await — into equivalent PHP patterns, producing standard PHP files ready for deployment.

:::note
This documentation is written for experienced PHP developers. Familiarity with PHP syntax, object-oriented programming, and Composer is assumed.
:::
