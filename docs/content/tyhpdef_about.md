---
title: 'About Tyhpdef'
status:
  tier: 0
  story: '02'
  state: complete
---

Tyhp is a transpiler that compiles to PHP. When your Tyhp code needs to interact with existing PHP libraries, extensions, or Composer packages, the Tyhp compiler must understand their types and signatures. Tyhpdef is a declaration-only syntax that describes existing PHP code to Tyhp's type system, enabling compile-time type checking for external PHP code.

Tyhpdef files use the `<?tyhpdef` open tag, have the `.tyhpdef` file extension, and contain only type declarations with no implementation code. The Tyhp compiler reads these files during compilation but never emits them as PHP output.

## What You Can Declare

Tyhpdef can describe the following PHP constructs to Tyhp:

- Functions (including async, generic, overloaded, and extension functions)
- Classes (including abstract, final, and generic classes)
- Interfaces (including generic interfaces with extends chains)
- Traits (including generic traits)
- Enums (both unit enums and backed enums with string or int values)
- Constants (typed global constants; `as` aliases; top-level `deprecated` / `obsolete`)
- Variables (typed global variables; `as` aliases; top-level `deprecated` / `obsolete`)

You can also define these supplemental constructs inside a Tyhpdef file to support your declarations:

- Structs (to describe PHP associative array structures)
- Type aliases (to create reusable type definitions)
- Namespaces (to organize declarations)

## How the Compiler Finds Tyhpdef Files

The Tyhp compiler loads type information from multiple sources. Earlier sources take precedence when declarations conflict:

1. Built-in registrations — Core language types (`decimal`, iterators, `callable<…>`, compile-time constructs, and similar) are registered in the compiler itself and are always available.
2. Composer packages with `package.tyhp.json` — Runtime libraries (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`) and the `tyhp/php` builtins package (stubs cover PHP 8.2+ APIs; this alpha’s compiler/init default target is PHP 8.4, ceiling 8.5). Install them with your project so `\strlen`, `DateTime`, `\Tyhp\Type`, and other APIs type-check.
3. User project tyhpdefs — files matching `tyhpdefInclude` in `tyhp.json`, plus any `include` entries that end in `.tyhpdef` or `package.tyhp.json`. If you omit `tyhpdefInclude`, **project `.tyhpdef` files are not loaded** (`tyhp init` creates a `tyhpdef/` folder but does not set the glob). Add e.g. `"tyhpdefInclude": ["./tyhpdef/**/*.tyhpdef"]` (or `"**/*.tyhpdef"`) when you author stubs.

This alpha does **not** auto-generate tyhpdefs and does not scan a `tyhpdef_gen/` directory. Write declarations by hand, or start from `tyhp/php`.

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
DON'T create tyhpdef files for: PHP builtins already covered by `tyhp/php` (Core, Standard, SPL, Date, JSON, and similar — install that package instead of redeclaring them), or Tyhp code — `.tyhp` files are already fully typed and don't need separate declarations. Automatic generation for Composer packages is not in this alpha, so third-party PHP packages you call still need hand-written tyhpdefs for the members you use.
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
