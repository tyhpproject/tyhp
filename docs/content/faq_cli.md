---
title: 'FAQ: CLI'
---

## How do I build my project?

Run `tyhp build` in your project directory. This reads the `tyhp.json` configuration, discovers all included source files, and runs the full compilation pipeline: parse, bind, check, emit. Compiled PHP files are written to the configured output directory (default: `build/`).

```tyhp
tyhp build

# With options:
tyhp build --verbose          # detailed output
tyhp build --strict           # treat warnings as errors
tyhp build --dry-run          # check without writing files
tyhp build --clean            # wipe output directory first
```

After a successful build, the output summary shows file counts, per-phase timing, and any diagnostics:

```tyhp
Build completed successfully.

  Files:     42 source files -> 38 PHP files
  Duration:  1.23s (parse: 0.45s, bind: 0.12s, check: 0.34s, emit: 0.32s)
  Warnings:  2

Output written to: ./build/
```

## How do I use watch mode?

Run `tyhp build --watch` to start the compiler in watch mode. The compiler monitors your source files for changes and automatically rebuilds when a file is saved. Incremental compilation ensures that only changed files are re-parsed, making rebuilds fast.

## How do I debug Tyhp code?

This alpha has no sourcemaps or Xdebug proxy. Debug the compiled PHP under `output.path` (default `build/`), or iterate with `tyhp lint`. Sourcemaps and `tyhp xdebug_proxy` are planned (Stories 17–18).

## How do I integrate Tyhp with CI/CD?

Use `tyhp lint` in your CI pipeline to check for type errors without producing output files. The `--format json` flag provides machine-readable output for integration with CI tools. Use `--strict` to fail the build on warnings as well as errors.

```tyhp
# In your CI pipeline:
tyhp lint --format json --strict
```

For production builds, run `tyhp build --strict` to ensure a clean build with no warnings. The exit code reflects the result: 0 for success, 4 for errors, 5 for warnings (when `--strict` is not set).

## How do I generate tyhpdef files?

`tyhp generate_tyhpdef` is **not** in this alpha (Story 20). Write `.tyhpdef` files by hand. PHP builtins are provided by the `tyhp/php` Composer package (PHP 8.2 baseline).

## What is the language server for?

The language server is **not** in this alpha (Story 19). Use `tyhp lint` from the CLI or your editor's build task. LSP support is planned for a later release.
