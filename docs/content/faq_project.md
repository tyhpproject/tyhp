---
title: 'FAQ: Project Configuration'
---

## What goes in tyhp.json?

The `tyhp.json` file configures the Tyhp compiler for your project. It specifies which source files to include, where to write compiled PHP output, which PHP version to target, and various build options. Language strictness is not a config knob — null safety and required types are always on. The file uses standard JSON format and is placed in your project root.

A minimal configuration only needs source file patterns and an output path:

```json
{
    "include": ["./src/**/*.tyhp"],
    "output": {
        "path": "./build"
    }
}
```

## How do I set the target PHP version?

Set the `output.phpVersion` option in `tyhp.json`. Supported values are `"8.2"`, `"8.3"`, `"8.4"`, and `"8.5"` (default `"8.4"`). This controls which PHP features the compiler uses in the generated output **and** which `tyhp/*` Composer MAJOR to install (`"8.3"` → `803.x`). See the Composer Runtime Packages page.

```json
{
    "output": {
        "phpVersion": "8.3"
    }
}
```

## How do I set tyhp/* versions in composer.json?

Package MAJOR is the **PHP version the artifact is for**, not the compiler version. Each `tyhp/*` package has its own `X.Y`. An app with `output.phpVersion` `"8.4"` and core `0.0` should require `tyhp/core: 804.0.0`. A library that supports PHP 8.3+ should or the majors using **that package's** X: `803.0.* || 804.0.* || 805.0.*`. See the Composer Runtime Packages page.

## How do I configure source maps?

Source maps are **not** in this alpha (Story 17). The `build.generateSourcemap` key may appear in examples for later releases; it does not produce `.php.map` files yet.

## How do I exclude files from compilation?

Use the `exclude` array with glob patterns. Files matching any exclude pattern are skipped even if they match an `include` pattern:

```json
{
    "include": ["./src/**/*.tyhp"],
    "exclude": [
        "./src/tests/**",
        "./src/legacy/**",
        "./src/**/*.draft.tyhp"
    ]
}
```

Glob patterns support `*` (any file name), `**` (any directory depth), and `?` (single character).

## Can I have multiple output directories?

Not directly. The <code>output.path</code> option specifies a single output directory. However, PSR-4 namespace mappings (via the <code>psr4</code> option) control the directory structure within the output directory. Each namespace prefix maps to a subdirectory, so classes in different namespaces are written to different subdirectories automatically.

```json
{
    "output": {
        "path": "./build"
    },
    "psr4": {
        "App\\Models\\": "src/Models/",
        "App\\Services\\": "src/Services/"
    }
}
```

If you need completely separate output directories for different parts of your project, consider using multiple `tyhp.json` files with the `--tyhp-project` flag.

## What happens if there is no tyhp.json?

If no `tyhp.json` is found, the compiler uses sensible defaults: it looks for `.tyhp` files in the current directory, outputs to `build/`, targets PHP 8.4, and enables strict type checking. An informational message is displayed to let you know defaults are being used. Run `tyhp init` to create a configuration file with these defaults.

## How do I configure the type checker?

Language strictness is not configurable. Null safety, required type annotations (or inferable initializers), and narrowing `mixed` before use are always enforced. The `checker` section only exposes resource limits and tooling knobs:

```json
{
    "checker": {
        "templateStringMaxStates": 256,
        "maxFixIterations": 10
    }
}
```

- `templateStringMaxStates` (default: `256`) — Upper bound on template-string automaton complexity for subtyping checks.
- `maxFixIterations` (default: `10`) — Maximum auto-fix re-run iterations for `tyhp lint --fix`.
