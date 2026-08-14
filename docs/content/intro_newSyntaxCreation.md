---
title: 'How New Syntax is Created in Tyhp'
---

Tyhp is built on top of PHP and uses its syntax first and foremost. However, to achieve features like generics, structs, and strong typing, Tyhp introduces new syntax on top of PHP. This page explains how new syntax is designed, how the compiler pipeline processes it, and how conflicts with future PHP versions are handled.

Compiler versioning (MAJOR encodes the PHP ceiling; MINOR carries Tyhp language change) is described on the [Release Planning](release_planning.md) page. This page is the public mirror of the syntax-conflict and rollout sections of `VERSIONING.md` in the compiler repository.

## Design Principles for New Syntax

When creating new syntax in Tyhp, the following principles are followed in order of priority:

1. Match existing PHP syntax wherever possible — if PHP has a similar construct, Tyhp mirrors its patterns
2. Follow widely-accepted conventions from other languages — for features without PHP precedent (like generics), Tyhp adopts the most common syntax across languages like TypeScript, C#, Java, and Kotlin
3. Ensure compatibility with existing PHP syntax — new Tyhp syntax must not break valid PHP code
4. Maintain a PHP-like feel — even novel syntax should feel natural to PHP developers

## The Compiler Pipeline

Understanding how the Tyhp compiler processes code helps explain how new features are added. The compiler uses a multi-phase pipeline:

1. Lexer — The ANTLR4-based lexer tokenizes .tyhp source files into a token stream. Tyhp extends the PHP lexer grammar with new tokens for Tyhp-specific keywords and operators.
2. Parser — The ANTLR4-based parser converts the token stream into a parse tree according to the grammar rules. Tyhp extends the PHP parser grammar with new productions for Tyhp constructs like generics, structs, type aliases, and more.
3. Visitor/AST — The visitor walks the parse tree and produces an Abstract Syntax Tree (AST) composed of typed node classes. Each Tyhp-specific construct has its own AST node type.
4. Binder — The binder walks the AST to build scope hierarchies and register symbols (classes, functions, variables, etc.). It performs name resolution, linking references to their declarations.
5. Checker — The type checker validates the bound AST against the type system rules: type compatibility, null safety, generic constraints, visibility, and more. It performs type inference and control-flow-aware type narrowing.
6. Emitter — The emitter transforms the validated AST into PHP output. Tyhp-specific constructs are converted to their PHP equivalents: generics are erased, structs become arrays, extension methods become static calls, and so on.

## Adding a New Feature

Adding a new language feature to Tyhp involves changes at each pipeline stage:

- Grammar modification — New tokens and parser rules are added to TyhpLexer.g4 and TyhpParser.g4 (which extend PhpLexer.g4 and PhpParser.g4)
- AST nodes — New AST node classes are created to represent the construct in the tree
- Visitor updates — The visitor is extended to convert the new parse tree nodes into the new AST nodes
- Binder changes — The binder learns to register and resolve symbols for the new construct
- Checker rules — Type checking rules are added to validate correct usage
- Emitter output — The emitter learns to transform the new construct into equivalent PHP code

## PHP syntax conflict resolution

Because Tyhp builds on PHP's syntax first and only adds new syntax when necessary, a future PHP version can introduce syntax that conflicts with, or overlaps, a Tyhp syntax. When that happens Tyhp works through the following, in order (usually spread across one or more releases):

1. **Integrate and detect.** Try to adopt PHP's change while detecting each syntax (PHP's and Tyhp's) at compile time so both can coexist. Not always practical or possible, but always the preferred first step. Usually a MINOR change. If the conflict arrives with a new PHP minor, a MAJOR bump may ship in the same train because the PHP ceiling moved — the Tyhp syntax work is still MINOR.
2. **PHP implements the same feature differently.** Deprecate Tyhp's own syntax in favor of PHP's. Compile flags may enable or disable either syntax until Tyhp drops the old one. MINOR to deprecate; a later MINOR to change the default and remove the old syntax.
3. **PHP implements a different feature whose syntax conflicts with Tyhp's.** If the two usages can be reliably differentiated at compile time, keep both. If not, migrate Tyhp's syntax to be compatible: first deprecate the old syntax (with compile-time enable/disable options), then remove it. MINOR throughout — do not wait for the next PHP / MAJOR bump.
4. **PHP adds new functionality that does not affect any Tyhp syntax.** Adopt the new PHP syntax at the next feasible release. Tyhp tracks PHP dev releases and roadmaps to anticipate this. MINOR, or MAJOR only if that PHP version is also a new ceiling.

Tyhp-only language evolution does not wait for a PHP version bump and is not scheduled as "the next MAJOR." MAJOR only changes when the highest supported PHP minor changes.

## Rollout plan for incompatible changes

This plan applies whether the incompatibility comes from a new PHP version or from Tyhp changing its own syntax. Removal and default-flips are MINOR work. They are not held for the next MAJOR, because MAJOR only moves when the PHP ceiling does.

- **a. [immediate]** Alert developers (via this website) about the incompatibility.
- **b. [asap]** Emergency PATCH that adds compile-time option(s) to disable the new, incompatible syntax (PHP and/or Tyhp) and to enable/disable the corresponding alternative. Default: keep existing Tyhp behavior. May be delayed if parsing new PHP syntax is substantial work.
- **c. [asap, if applicable]** A MINOR that deprecates the old conflicting Tyhp syntax and, if PHP does not fully replace it, provides a new Tyhp alternative. The alternative is off by default; the option from (b) still applies. Gives developers time to migrate.
- **d. [later MINOR]** The old syntaxes are removed in favor of the new ones, and the compile options from (b) and (c) are removed. This can be `805.N.0` on the same PHP ceiling; it does not require `806.0.0`.

If the conflict is introduced by a new PHP minor, (b)–(d) may ship on the new MAJOR (`806.x`) because that release is the one that claims support for that PHP. The version *part* that carries the Tyhp syntax change is still MINOR.

## Community Feature Proposals

The Tyhp language is open to community-driven feature proposals. If you have an idea for a new language feature or syntax improvement:

1. Open an issue on [tyhpproject/tyhp](https://github.com/tyhpproject/tyhp/issues) describing the proposed feature, its syntax, and its use cases
2. Include examples showing the proposed Tyhp syntax and the expected PHP output
3. Discuss how the feature interacts with existing Tyhp and PHP syntax
4. Consider potential conflicts with future PHP development roadmaps
5. Proposals that follow the design principles outlined above and have broad utility are most likely to be considered
