---
title: 'Project Options List'
status:
  tier: 0
  story: '10'
  state: complete
---

This is the complete reference for all options available in `tyhp.json`. Options can also be passed as CLI arguments where noted. CLI arguments take precedence over file-based configuration.

## General Options

:::member[quiet]
Type: `bool` (default: `false`). When true, suppresses the banner and non-error output. Useful for scripting and CI environments.
:::

:::member[type]
Type: `string` (default: `"application"`). Project kind: `"application"` or `"library"`. Library projects default `build.generateTyhpdef` to true (that generator is still **not** in this alpha).
:::

:::member[locale]
Type: `string` (default: `"en-US"`). Sets the locale for compiler messages and diagnostic output.
:::

:::member[cache-dir]
Type: `string` (default: system local application data directory). Sets the directory used for caching parsed ASTs and build state. Caching speeds up subsequent builds by reusing parsed results for unchanged files.
:::

## Source File Options

:::member[include]
Type: `array<string>`. Glob patterns for source files to include in the project. Supports `*` (any file), `**` (any directory depth), and `?` (single character) wildcards. Example: `["./src/**/*.tyhp", "./src/**/*.php"]`.
:::

:::member[exclude]
Type: `array<string>`. Glob patterns for files to exclude from the project. Files matching these patterns are skipped even if they match an `include` pattern. Useful for excluding test files, vendor directories, or generated code.
:::

:::member[source.tagless]
Type: `bool` (default: `false`). When true, `.tyhp` and `.tyhpdef` files are parsed without requiring `<?tyhp` / `<?tyhpdef`. Closing `?>` is an error in this mode. Nested as `"source": { "tagless": true }`.
:::

## Output Options

These options are nested under the `"output"` key in `tyhp.json`.

:::member[output.path]
Type: `string` (default: `"build/"`). The directory where compiled PHP files are written, relative to the project root. The directory is created automatically if it does not exist.
:::

:::member[output.phpVersion]
Type: `string` (default: `"8.4"`). The target PHP version for the compiled output. Supported values: `"8.2"`, `"8.3"`, `"8.4"`, `"8.5"`. This affects which PHP features the emitter uses in the generated code.
:::

:::member[output.strictTypes]
Type: `bool` (default: `true`). When true, adds `declare(strict_types=1);` to all compiled PHP output files. Recommended for type safety.
:::

:::member[output.namespacePrefix]
Type: `string | null` (default: `null`). A namespace prefix added to or stripped from all namespaces in the output. Used when the compiled output needs a different namespace root than the source code.
:::

:::member[output.comments]
Type: `bool` (default: `true`). Controls whether comments from the Tyhp source are preserved in the compiled PHP output. Set to `false` to strip comments for smaller output.
:::

## Build Options

These options are nested under the `"build"` key in `tyhp.json`, except where noted.

:::warning Not in this alpha
`build.generateSourcemap` and `build.generateTyhpdef` are planned (Stories 17 and 20) and do not produce output in 805.0.0-alpha.1.
:::

:::member[build.generateSourcemap]
Type: `bool` (default: `false`). **Not in this alpha** (Story 17). The key is parsed but no `.php.map` files are written.
:::

:::member[build.sourcemapIncludeContent]
Type: `bool` (default: `false`). **Not in this alpha.** When sourcemaps land, true would embed original `.tyhp` source in the map.
:::

:::member[build.generateTyhpdef]
Type: `bool` (default: `false`; libraries default this to true in config). **Not in this alpha** (Story 20). Does not generate tyhpdef output yet.
:::

:::member[build.updateComposer]
Type: `bool` (default: `false`). When true, generates or updates a `composer.json` in the output directory with PSR-4 autoload mappings and Tyhp runtime package dependencies.
:::

:::member[build.entryPointAutoloader]
Type: `object | null` (default: `null`). A map of named autoloader paths. When set, entry point files (root code files) will have `require_once` statements added for the Composer autoloader. Example: `{"default": "vendor/autoload.php"}`.
:::

:::member[build.structBacking]
Type: `string` (default: `"array"`). Controls how Tyhp structs are represented in compiled PHP. The default `"array"` compiles structs to associative arrays.
:::

:::member[build.decimalBacking]
Type: `string` (default: `"bcmath"`). The PHP extension used for decimal arithmetic. Supported values: `"bcmath"`, `"gmp"`.
:::

:::member[build.decimalScale]
Type: `int` (default: `28`). The default number of decimal places for decimal arithmetic operations.
:::

:::member[build.decimalRounding]
Type: `string` (default: `"halfUp"`). The default rounding mode for decimal arithmetic.
:::

:::member[build.allowEval]
Type: `bool` (default: `false`). When true, re-enables `eval()` usage in Tyhp code. By default, `eval()` is disabled for security and type-safety reasons.
:::

:::member[build.experimentalReadonlyCloneWith]
Type: `bool` (default: `false`). When true, allows `clone ... with` on `readonly` properties for PHP 8.2–8.4 by emitting a compiler wrapper. PHP 8.5+ supports this natively and does not need the flag. `new ... with` on `readonly` never requires it. In-place `$obj with [...]` still cannot set `readonly` after construction.
:::

:::member[build.runtimeGenericChecks]
Type: `bool` (default: `false`). When true, the emitter inserts runtime type checks at generic parameter and return boundaries (in addition to compile-time checking). Off by default.
:::

## Optimization Options

```status
tier: 3
story: '23'
state: planned
```

:::warning Not in this alpha
Optimizer options are Stories 23/24. In this alpha the optimize pass is a no-op.
:::

These options are nested under the `"build"` key in `tyhp.json` and control the compiler optimizer. Each option also has a corresponding CLI flag that overrides the file value.

:::member[build.profile]
Type: `string` (default: `"debug"`). The build profile that sets coordinated defaults for optimization, source maps, and comment output. Supported values: `"debug"`, `"balanced"`, `"release"`. CLI: `--profile=<value>`.
:::

:::member[build.optimize]
Type: `string` (default: derived from `profile`). The optimization level applied to the compiled output, overriding the profile default. Supported values: `"none"`, `"basic"`, `"aggressive"`. `basic` enables extension inlining, constant folding, dead code elimination, and unused import pruning. CLI: `--optimize=<value>`.
:::

:::member[build.optimizations]
Type: `object` (default: `{}`). Per-module enable/disable overrides applied on top of the `optimize` level. Keys are optimization module names; values are booleans. Use this to turn individual optimizations on or off without changing the overall level. CLI: `--optimize-enable=<module>` and `--optimize-disable=<module>`.
:::

## PSR-4 Autoload Options

:::member[psr4]
Type: `object | null` (default: `null`). PSR-4 namespace-to-directory mappings for the compiled output. Keys are namespace prefixes (with trailing `\\`), values are directory paths. Example: `{"App\\": "src/"}`.
:::

:::member[psr4Includes]
Type: `array<string> | null` (default: `null`). Additional PSR-4 autoload paths to include in the generated `composer.json`.
:::

## Checker Options

These options are nested under the `"checker"` key in `tyhp.json`. They control resource limits and tooling behavior — not language strictness.

:::note
Null safety, required type annotations, and narrowing `mixed` before use are unconditional language rules. There are no `checker.*` toggles that relax them.
:::

:::member[checker.templateStringMaxStates]
Type: `int` (default: `256`). Upper bound on template-string automaton complexity for subtyping/inclusion checks. When exceeded, the checker is conservative and emits a diagnostic.
:::

:::member[checker.maxFixIterations]
Type: `int` (default: `10`). Maximum auto-fix re-run iterations for `tyhp lint --fix`.
:::

## Tyhpdef Options

:::member[tyhpdefInclude]
Type: `array<string>`. Glob patterns for tyhpdef files to load. These files provide type information for existing PHP code, Composer packages, and PHP extensions. The `TyhpdefConfig` class default is `["**/*.tyhpdef"]`, but loading **clears** that list then reads `tyhp.json`. If you omit `tyhpdefInclude`, **no** project `.tyhpdef` files are loaded (`tyhp init` creates `tyhpdef/` but does not set the glob). Add e.g. `"tyhpdefInclude": ["./tyhpdef/**/*.tyhpdef"]` when you author stubs. Entries in `include` that end in `.tyhpdef` or `package.tyhp.json` are also loaded as tyhpdefs.
:::

:::member[tyhpdefExclude]
Type: `array<string>` (default: `[]`). Glob patterns for tyhpdef files to exclude from loading.
:::

## CLI-Only Options

These options are only available as command-line arguments and override corresponding config file values.

:::member[--clean]
Wipe the output directory before building. Deletes all `.php` and `.php.map` files in the output directory. Safety checks prevent cleaning the project root or system directories.
:::

:::member[--verbose]
Enable detailed output during compilation. Shows per-phase timing, cache statistics, file counts, and memory usage.
:::

:::member[--dry-run]
Run the full compilation pipeline (parse, bind, check, emit) but do not write any output files. Reports what would be written.
:::

:::member[--strict]
Treat warnings as errors. The build fails if any warnings are produced, in addition to errors.
:::

:::member[--tyhp-project]
Specify the path to the `tyhp.json` project file. Overrides the default behavior of looking in the current working directory.
:::

:::member[--watch]
**Not in this alpha.** `tyhp build --watch` prints that watch mode is unimplemented, then runs a normal one-shot build.
:::

:::member[--fix]
Apply auto-fixable diagnostic replacements (`tyhp lint --fix`). Experimental.
:::

## Lint-Specific Options

:::member[--format]
Output format for lint diagnostics. Supported values: `text` (default, human-readable), `json` (machine-readable), `sarif` (SARIF v2.1.0 for GitHub Code Scanning and similar).
:::

:::member[--file]
Lint a single file instead of the whole project. Still loads tyhpdefs and built-in types for full type checking.
:::

## Action-Specific Options

:::member[ext-name]
Type: `string`. Used with the planned `generate_tyhpdef` action (Story 20; **not in this alpha**). Specifies the PHP extension name to generate type definitions for.
:::

:::member[subject]
Type: `string`. Used with the `help` action. Specifies which action to display help for.
:::

## Complete Example

Below is a comprehensive `tyhp.json` showing all available options with their default values. In practice, you only need to specify the options you want to change from their defaults.

```json
{
    "type": "application",
    "quiet": false,
    "locale": "en-US",

    "include": ["./src/**/*.tyhp", "./src/**/*.php"],
    "exclude": ["./src/legacy/**"],
    "source": {
        "tagless": false
    },

    "output": {
        "path": "./build",
        "phpVersion": "8.4",
        "strictTypes": true,
        "namespacePrefix": null,
        "comments": true
    },

    "build": {
        "generateSourcemap": false,
        "sourcemapIncludeContent": false,
        "generateTyhpdef": false,
        "updateComposer": false,
        "structBacking": "array",
        "decimalBacking": "bcmath",
        "decimalScale": 28,
        "decimalRounding": "halfUp",
        "allowEval": false,
        "experimentalReadonlyCloneWith": false,
        "runtimeGenericChecks": false
    },

    "psr4": {
        "App\\": "src/"
    },

    "checker": {
        "templateStringMaxStates": 256,
        "maxFixIterations": 10
    },

    "tyhpdefInclude": ["./tyhpdef/**/*.tyhpdef"],
    "tyhpdefExclude": []
}
```
