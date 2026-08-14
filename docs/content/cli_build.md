---
title: 'CLI: Build'
status:
  tier: 0
  story: '10'
  state: complete
---

The build action compiles your Tyhp source files into PHP code. It runs the full compilation pipeline — parse, bind, check, and emit — then writes the compiled PHP output files to the configured output directory.

## Usage

```
tyhp build [options]
```

## Options

- --tyhp-project=<path> — Path to the tyhp.json project file.
- --clean — Delete all .php and .php.map files from the output directory before building.
- --verbose — Display detailed output for each compilation phase, including per-file status and cache statistics.
- --dry-run — Run the full compilation pipeline (parse, bind, check, emit) but do not write any output files. Reports what would be written.
- --strict — Treat warnings as errors. The build fails if any warnings are produced.
- --quiet — Suppress the banner and non-diagnostic output.
- --watch — Watch source files for changes and rebuild automatically.

## Build Process

The build action performs the following steps in order:

1. Load and validate configuration from tyhp.json and CLI arguments.
2. Discover source files matching the configured include patterns (excluding files matching exclude patterns).
3. Parse all source files into Abstract Syntax Trees (ASTs). Parsing is multi-threaded for performance.
4. Load tyhpdef files and populate the global scope with built-in types, functions, constants, and external type definitions.
5. Run the binder to build scope hierarchies and resolve all name references.
6. Run the type checker to validate type compatibility, null safety, generic constraints, and other rules.
7. If errors are found, display diagnostics and exit with code 4 (CompileError). If --strict is set and warnings are found, exit with code 5 (CompileWarning).
8. Run the emitter to transform Tyhp-specific constructs into PHP equivalents.
9. Write compiled PHP files to the output directory.
10. Write compiled PHP files to the output directory and update composer.json with Tyhp runtime package requires when needed.

Sourcemap generation and `generate_tyhpdef` are **not** in this alpha even if the config keys exist.

## Configuration (tyhp.json)

The build action reads configuration from the tyhp.json project file. Key configuration sections include:

## Output Configuration

- output.path — Output directory for compiled PHP files (default: "build/").
- output.phpVersion — Target PHP version: "8.2", "8.3", "8.4", or "8.5" (default: "8.4").
- output.strictTypes — Add declare(strict_types=1) to all output files (default: true).
- output.comments — Include comments from source in the output (default: true).
- output.namespacePrefix — Optional prefix added to all namespaces in output.

## Build Configuration

- build.generateSourcemap — Planned (Story 17). Ignored in this alpha; do not rely on `.php.map` output.
- build.generateTyhpdef — Planned (Story 20). Ignored in this alpha.
- build.updateComposer — Generate or update composer.json with PSR-4 autoloading for the output (default: false).
- build.structBacking — How structs are backed in PHP: "array" (default: "array").
- build.decimalBacking — Decimal math backend: "bcmath" or "gmp" (default: "bcmath").
- build.decimalScale — Default scale for decimal operations (default: 28).
- build.allowEval — Re-enable the eval() function, which is disabled by default in Tyhp (default: false).
- psr4 — PSR-4 namespace-to-directory mappings for the output.
- psr4Includes — Additional PSR-4 autoload paths.

## Checker Configuration

Null safety, required type annotations, and narrowing mixed before use are unconditional. The checker section only exposes resource limits and tooling knobs:

- checker.templateStringMaxStates — Upper bound on template-string automaton complexity (default: 256).
- checker.maxFixIterations — Maximum auto-fix re-run iterations for tyhp lint --fix (default: 10).

## Example Configuration

```json
{
    "include": ["./src/**/*.tyhp", "./src/**/*.php"],
    "exclude": ["./src/legacy/**"],
    "output": {
        "path": "./build",
        "phpVersion": "8.4",
        "strictTypes": true
    },
    "build": {
        "updateComposer": true
    },
    "checker": {
        "templateStringMaxStates": 256
    },
    "psr4": {
        "App\\": "src/"
    }
}
```

## Incremental Builds

Tyhp supports incremental compilation using an AST cache. When you rebuild a project, files that have not changed since the last build are loaded from the cache rather than being re-parsed. This significantly speeds up rebuild times for large projects. The binder and checker still run on all files to ensure cross-file consistency.

Build state is persisted between runs. If no source files or configuration have changed, the build exits early with a "Nothing to build" message. Use --clean to force a full rebuild by clearing the output directory and build state.

## Error Output

Errors and warnings are displayed in a format similar to other compiled languages:

```
src/Models/User.tyhp(42,5): error TYHP4003: Member modifier 'static' is not allowed on interface methods
src/Services/Auth.tyhp(15,10): error TYHP3003: Symbol 'InvalidUser' not found

Build failed with 2 errors.

  Files:     42 source files
  Duration:  0.91s (parse: 0.45s, bind: 0.12s, check: 0.34s)
  Errors:    2
  Warnings:  0
```

## Tyhp Runtime Packages

Some Tyhp features require small runtime PHP packages to function. When build.updateComposer is enabled, the build action automatically adds the necessary Composer dependencies based on the features used in your code:

- tyhp/php — PHP builtin tyhpdefs (8.2 baseline in this alpha).
- tyhp/core — Generic type helpers, property accessors, disposable support, named types.
- tyhp/decimal — Decimal arithmetic operations (when the decimal type is used).
- tyhp/async — Promise-based async/await support (when async functions are used).
- tyhp/lambda — Expression-tree / PropertyPath runtime (when parsable lambdas are used).

Package MAJOR is the target PHP (`804.x` for PHP 8.4), not the compiler ceiling. Each package has its own `X.Y`. Applications pin `80N.X.Y`; libraries or PHP majors with **that package's** X (`803.0.* || 804.0.* || 805.0.*` while it is on `0.y`). See the Composer Runtime Packages page.
