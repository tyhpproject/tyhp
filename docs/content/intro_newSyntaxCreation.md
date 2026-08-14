---
title: 'How New Syntax is Created in Tyhp'
---

Tyhp is built on top of PHP and uses its syntax first and foremost. However, to achieve features like generics, structs, and strong typing, Tyhp introduces new syntax on top of PHP. This page explains how new syntax is designed, how the compiler pipeline processes it, and how conflicts with future PHP versions are handled.

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

## Handling PHP Syntax Conflicts

Because Tyhp extends PHP, there is always a possibility that a future PHP version will introduce syntax that conflicts with Tyhp's additions. When this happens, Tyhp follows a defined migration process:

1. If PHP implements the same feature using a different syntax, Tyhp deprecates its own syntax in favor of PHP's. Compile-time flags allow both syntaxes during the transition. (MINOR to deprecate; a later MINOR to change the default and drop the old syntax.)
2. If PHP introduces a different feature that uses conflicting syntax, Tyhp attempts to detect and differentiate the usages at compile time. If differentiation is not feasible, Tyhp transitions to a new non-conflicting syntax. (MINOR throughout — not held for the next MAJOR / PHP ceiling bump.)
3. If PHP adds new functionality that does not conflict, Tyhp adopts the new PHP syntax on the next feasible release. (MINOR, or MAJOR only if that PHP version is also a new ceiling.)
4. In all cases, Tyhp tries to detect usages of both PHP and Tyhp syntax at compile time to support both during the transition period.

## Conflict Resolution Timeline

When a syntax conflict is discovered with a new PHP version, the following steps are taken:

1. **[Immediate]** — An alert is issued to developers indicating the incompatibility with the specific PHP version.
2. **[ASAP]** — A PATCH release adds parsing support for the new PHP syntax, with compile-time options to toggle between PHP's and Tyhp's conflicting syntax. The default preserves existing Tyhp behavior.
3. **[ASAP, if applicable]** — A MINOR release deprecates the old Tyhp syntax and provides an alternative syntax with the same functionality. The new syntax is opt-in via a compiler option.
4. **[Later MINOR]** — The old conflicting syntax is removed. The new syntax becomes the default and the transitional compiler options are removed. This does not wait for the next PHP / MAJOR bump.

## Community Feature Proposals

The Tyhp language is open to community-driven feature proposals. If you have an idea for a new language feature or syntax improvement:

1. Open an issue on the Tyhp GitHub repository describing the proposed feature, its syntax, and its use cases
2. Include examples showing the proposed Tyhp syntax and the expected PHP output
3. Discuss how the feature interacts with existing Tyhp and PHP syntax
4. Consider potential conflicts with future PHP development roadmaps
5. Proposals that follow the design principles outlined above and have broad utility are most likely to be considered
