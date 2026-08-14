---
title: 'Hack / HHVM'
---

Hack is a statically typed programming language developed by Facebook (now Meta) that originally evolved from PHP. It runs on the HipHop Virtual Machine (HHVM), a custom runtime built by Facebook as a high-performance replacement for the standard PHP interpreter. Hack and Tyhp share a common goal — bringing strong typing to PHP-like code — but they take fundamentally different approaches to achieving it.

## What Hack Is

Hack was created at Facebook in 2014 to address the type safety and performance issues the company faced while maintaining one of the world's largest PHP codebases. Hack adds a comprehensive type system to PHP syntax, including generics, nullable types, type aliases, async/await, and a powerful type checker. It runs on HHVM rather than the standard PHP runtime (php-fpm/php-cli), and over the years has diverged significantly from PHP, removing certain PHP features and adding entirely new constructs.

## Similarities with Tyhp

- Both add static typing on top of PHP-like syntax
- Both support generics with type parameters and constraints
- Both support async/await for asynchronous programming
- Both support type aliases and type inference
- Both aim to catch errors at compile time rather than runtime
- Both support nullable types with explicit syntax

## Key Differences

## Runtime

The most significant difference is the runtime. Hack requires HHVM, a separate virtual machine that replaces the PHP runtime entirely. Tyhp compiles to standard, readable PHP code that runs on any PHP 8.2+ runtime. This means Tyhp code works with your existing PHP infrastructure — your web server, hosting, extensions, debugging tools, and deployment pipeline all remain unchanged.

## PHP Compatibility

Hack has diverged substantially from PHP over the years. HHVM dropped support for running plain PHP code in 2018 (version 3.30), and Hack has since introduced syntax and semantics that are incompatible with PHP. Tyhp, by contrast, is designed as a strict superset of PHP. Valid PHP is valid Tyhp (with the addition of required types), and compiled Tyhp output is standard PHP that any PHP developer can read and understand.

## Ecosystem Integration

Because Tyhp outputs standard PHP, it integrates seamlessly with the PHP ecosystem — Composer packages, PHP extensions, PHP frameworks (Laravel, Symfony, etc.), and existing PHP tools all work without modification. Hack's HHVM ecosystem is separate and significantly smaller, with many PHP packages requiring modification or re-implementation to work on HHVM.

## Scope and Philosophy

Hack has a large engineering team at Meta and introduces many new operators, control structures, and language constructs beyond what PHP offers. Tyhp takes a more conservative approach: it builds on top of PHP's existing syntax and adds new features only when they provide clear value. Some PHP features are restricted in `<?tyhp` (for example `eval()` is off by default, and dynamic properties are disallowed) so the compiler can type-check; see Lost and Changed Functionality. Tyhp's philosophy is to enhance PHP, not replace the runtime.

## When to Choose Hack

Hack has many strengths. Its type system is mature and battle-tested at Facebook's scale. If you are willing to adopt HHVM as your runtime and your project does not depend heavily on the standard PHP ecosystem, Hack offers a powerful and well-supported language. Tyhp does not claim to be better than Hack — it is a different solution with different trade-offs, designed for teams that want to stay within the standard PHP ecosystem while gaining strong typing and modern language features.
