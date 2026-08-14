---
title: 'About Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhp is a transpiler that compiles to PHP. When your Tyhp code needs to interact with existing PHP libraries, extensions, or Composer packages, the Tyhp compiler must understand their types and signatures. Tyhpdef is a declaration-only syntax that describes existing PHP code to Tyhp's type system, enabling full type checking, autocompletion, and compile-time validation for external PHP code.

Tyhpdef files use the `<?tyhpdef` open tag, have the `.tyhpdef` file extension, and contain only type declarations with no implementation code. The Tyhp compiler reads these files during compilation but never emits them as PHP output.

## What You Can Declare

Tyhpdef can describe the following PHP constructs to Tyhp:

- Functions (including async, generic, overloaded, and extension functions)
- Classes (including abstract, final, and generic classes)
- Interfaces (including generic interfaces with extends chains)
- Traits (including generic traits)
- Enums (both unit enums and backed enums with string or int values)
- Constants (typed global constants with optional defaults)
- Variables (typed global variables with optional defaults and aliases)

You can also define these supplemental constructs inside a Tyhpdef file to support your declarations:

- Structs (to describe PHP associative array structures)
- Type aliases (to create reusable type definitions)
- Namespaces (to organize declarations)

## How the Compiler Finds Tyhpdef Files

The Tyhp compiler loads Tyhpdef files from multiple sources in a specific order. Earlier sources take precedence when declarations conflict:

1. TyhpSpec files — The compiler's built-in type definitions (core types like decimal, struct, Promise, iterators, etc.) are embedded in the compiler binary and always loaded first.
2. PHP extension tyhpdefs — the `tyhp/php` Composer package (PHP 8.2 baseline in this alpha). Install it with your project so `\strlen`, `DateTime`, and other builtins type-check.
3. User project tyhpdefs — Your project's own .tyhpdef files, discovered via the tyhpdefInclude and tyhpdefExclude glob patterns in tyhp.json. By default, .tyhpdef files in the project root and tyhpdef/ subdirectory are included.
4. Generated Composer package tyhpdefs — Automatically generated declarations for installed Composer packages, stored in the tyhpdef_gen/ directory.

## Auto-Generating Tyhpdef Files

```status
tier: 2
story: '20'
state: planned
```

Tyhp **will** include a CLI tool that auto-generates tyhpdef files from PHP Reflection (Story 20). That command is **not** in this alpha. Write tyhpdef files by hand, or start from `tyhp/php`.

:::note
Auto-generated tyhpdef files may use broad types like mixed where more specific types could be used. Review and refine the generated output for the best type-checking experience.
:::

## When to Create Tyhpdef Files

:::tip
DO create tyhpdef files for: C extensions with no PHP source (e.g., custom PECL extensions), legacy PHP libraries without type declarations, Composer packages that lack PHPDoc or native type hints, and internal PHP libraries shared between PHP and Tyhp projects.
:::

:::danger
DON'T create tyhpdef files for: PHP extensions already bundled with the compiler (Core, Standard, SPL, Date, JSON, etc.), Composer packages that already have auto-generated tyhpdef coverage, or Tyhp code — .tyhp files are already fully typed and don't need separate declarations.
:::

## Accuracy Matters

The types and structures in your Tyhpdef files are the sole source of truth the Tyhp compiler uses for external PHP code. Anything incorrect or incomplete in those files can lead to compile-time errors (false positives or missed real errors) and runtime errors (type mismatches in the generated PHP output).

:::warning
Always ensure your tyhpdef declarations match the actual PHP implementation. Declaring a method as returning int when it actually returns string will cause the compiler to accept incorrect code and produce runtime type errors.
:::

## Tyhpdef vs Tyhp

While Tyhpdef syntax closely resembles Tyhp, there are key differences:

- Tyhpdef files use <?tyhpdef, not <?tyhp
- Tyhpdef files have the .tyhpdef extension, not .tyhp
- Tyhpdef contains declarations only — no function bodies, no property assignments, no executable code
- All method and function declarations end with a semicolon instead of a curly-brace body
- Tyhpdef code is never compiled into PHP output — it exists only for the compiler's type system
