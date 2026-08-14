---
title: PXP
---

PXP is a PHP superset project that aims to add new syntax features to PHP and transpile the result to standard PHP. Like Tyhp, PXP extends PHP's capabilities, but the two projects differ in scope, approach, and focus areas.

## What PXP Is

PXP (pxp-lang) is an open-source project that provides a Rust-based parser and compiler for an extended PHP syntax. It began as a high-performance PHP parser and has grown into a language project that adds features like generics to PHP. PXP's tooling infrastructure — parser, AST, and formatter — is written in Rust, giving it strong performance characteristics for parsing and analysis.

## Similarities

- Both are supersets of PHP that transpile to standard PHP
- Both aim to add features that PHP currently lacks
- Both support generics as a key language addition
- Both produce standard PHP output that runs on any PHP runtime

## Differences

- Tyhp's primary focus is a comprehensive, enforced type system — every variable, parameter, property, and return type must be typed. PXP focuses on adding syntax features without requiring types everywhere.
- Tyhp includes a complete compile-time type checker that validates type correctness across the entire program. PXP's type handling is more limited in scope.
- Tyhp provides Tyhpdef files for describing external PHP libraries and Composer packages with full type information, similar to TypeScript's .d.ts files.
- Tyhp includes a runtime library (tyhp/core, tyhp/async, tyhp/decimal, tyhp/lambda) for features that require runtime support.
- Tyhp adds a broader set of language features beyond generics: structs, operator overloads, conversion operators, extension methods, async/await, disposables, expression trees, and more.
- Tyhp's compiler is written in C# (.NET), while PXP's tooling is written in Rust.
- Tyhp includes a linter (`tyhp lint`) in this alpha. Language Server and sourcemap debugging are planned, not shipped.

## Complementary Strengths

PXP's Rust-based parser offers excellent performance and could serve as a foundation for PHP-adjacent tooling. Tyhp takes a broader approach, providing a complete language with a type system, compiler pipeline, runtime library, and development tools. Both projects contribute to the PHP ecosystem by demonstrating that PHP can be extended with modern language features while maintaining backward compatibility.
