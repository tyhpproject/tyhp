---
title: 'Project Configuration'
status:
  tier: 0
  story: '10'
  state: complete
---

Every Tyhp project is driven by a configuration file called `tyhp.json`. This file tells the compiler which source files to include, where to write compiled PHP output, and what PHP version to target. Language strictness is always on (null safety, required types). It is the equivalent of `tsconfig.json` in the TypeScript world.

## Creating a Project

The fastest way to create a new Tyhp project is with the `init` command:

```tyhp
tyhp init
```

This creates a `tyhp.json` file, `src/index.tyhp`, a root `composer.json` pinning `tyhp/php` and `tyhp/core`, and `src/`, `build/`, and `tyhpdef/` directories. Init writes `source.tagless` (default false) and, when you set a namespace, a `psr4` mapping.

## File Location

By default, the Tyhp CLI looks for `tyhp.json` in the current working directory. You can specify a different path using the `--tyhp-project` option:

```tyhp
tyhp build --tyhp-project=/path/to/my/tyhp.json
```

## Basic Structure

A `tyhp.json` file is a standard JSON file with top-level keys for general settings and nested objects for output, build, checker, and tyhpdef configuration sections.

```json
{
    "include": ["./src/**/*.tyhp"],
    "exclude": [],

    "output": {
        "path": "./build",
        "phpVersion": "8.4",
        "strictTypes": true
    },

    "tyhpdefInclude": ["./tyhpdef/**/*.tyhpdef"],
    "tyhpdefExclude": []
}
```

## Project Structure Conventions

While Tyhp does not enforce a specific directory layout, the following structure is conventional and works well with the default configuration:

```json
my-project/
├── tyhp.json
├── composer.json
├── src/
│   ├── Models/
│   │   └── User.tyhp
│   ├── Services/
│   │   └── AuthService.tyhp
│   └── index.tyhp
├── tyhpdef/
│   ├── vendor.tyhpdef
│   └── extensions.tyhpdef
└── build/
    └── (compiled PHP output)
```

- `src/` — Your Tyhp source files (`.tyhp`). This is where you write your application code.
- `tyhpdef/` — Type definition files (`.tyhpdef`) that describe existing PHP libraries, Composer packages, and PHP extensions to the Tyhp compiler.
- `build/` — The default output directory where compiled PHP files are written. This directory is created automatically by `tyhp build`.

## Relationship to Composer

Tyhp projects work alongside Composer. Your project can have both a `tyhp.json` (for the Tyhp compiler) and a `composer.json` (for PHP dependency management). When the `build.updateComposer` option is enabled, the Tyhp build process can automatically generate or update a `composer.json` in the output directory with PSR-4 autoload mappings for the compiled PHP code.

Tyhp runtime features (such as generics, decimal types, and async/await) are distributed as Composer packages (`tyhp/core`, `tyhp/decimal`, `tyhp/async`). The build process automatically adds these as dependencies based on which features your code uses. Package MAJOR is the target PHP (`804` for PHP 8.4); `X.Y` is **that package's** version, independent of the compiler. See the Composer Runtime Packages page.

## No Configuration Required

If no `tyhp.json` file is found, the compiler uses sensible defaults: it looks for `.tyhp` files in the current directory, outputs to `build/`, and targets PHP 8.4. An informational message is displayed to let you know defaults are being used.

:::tip
See the Project Options List page for a complete reference of all available configuration options.
:::
