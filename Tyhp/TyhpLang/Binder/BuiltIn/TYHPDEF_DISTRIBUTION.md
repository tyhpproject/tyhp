# Tyhpdef Distribution Strategy (Story 06 Phase 6)

This document describes how type definition files reach the Tyhp compiler at bind time.

## Distribution matrix

| Component | Distribution | Discovery |
|-----------|--------------|-----------|
| Built-in scalar/utility types | Hardcoded C# (`Types.cs`, `StructUtilityTypes.cs`, `Functions.cs`, …) | Always available |
| Embedded legacy tyhp types | Compressed data in `TyhpBuiltIn/Tyhpdef.cs` | Loaded first (load order 0) |
| TyhpSpec | `TyhpSpec/` directory beside the compiler | Load order 1 |
| PHP extension types | `tyhpdef/php` Composer package | `vendor/tyhpdef/php/composer.json` (`extra.tyhp.package`) |
| PHP extension types (local/dev) | `runtime/packages/php/` | **Explicit** `tyhpdefInclude` / `include` of that tree's `composer.json` or `*.tyhpdef` globs — never auto-scanned |
| Runtime library types | `tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda` | `vendor/tyhp/*/composer.json` (`extra.tyhp.package`) **or** explicit `tyhpdefInclude` / `include` of each package's `composer.json` |
| User/project tyhpdefs | Paths in `tyhp.json` | `tyhpdefInclude` globs (load order 300) |

There is **no** silent discovery of `runtime/packages/`. Local checkouts must list package manifests (or tyhpdef globs) in `tyhp.json`.

Library `"type": "library"` builds additive-merge generated `extra.tyhp.package` onto publish-directory `composer.json`. Applications do not write `extra.tyhp.package`.

## Configuration (`tyhp.json` and CLI)

| Key | C# property | Purpose |
|-----|-------------|---------|
| `phpVersion` or `output.phpVersion` | `Project.PhpVersion` | Compiler target for emit **and** PHP-version gates. Missing key → `"8.2"` plus warning 4306 once per compilation. `tyhpdef/php` is a **single** stubs package: file-level `declare(php=…)` and `#[\Tyhp\Php]` filter visible APIs for 8.2–8.5. There are no mutually exclusive `tyhpdef/php-8.x` Composer packages. |
| `tyhpdefInclude` | `Project.TyhpdefIncludePaths` | Glob patterns for additional `.tyhpdef`/`.tyhp` files **and** `composer.json` files that have `extra.tyhp.package` |
| `overlay` / `tyhpdefOverlay` | `Project.TyhpdefOverlayPaths` | Overlay globs loaded after includes (last wins by Tyhp name) |
| `include` | (promoted when pattern ends with `.tyhpdef` or `composer.json`) | Same as `tyhpdefInclude` for those patterns; also used for compile inputs |
| `tyhpdefExclude` | `Project.TyhpdefExcludePaths` | Glob patterns to exclude after discovery |

CLI overrides use `--key=value` flags (e.g. `--phpVersion=8.2`, `--tyhpdefInclude:0=./tyhpdef/**/*.tyhpdef`).

### Compiler workspace

The compiler repo `tyhp.json` does not list package `composer.json` paths. Installed packages load from `vendor/` when `extra.tyhp.package` is an object. Local package source resolves from `TYHP_RUNTIME_SRC` or a sibling `tyhp-runtime-src/packages` checkout when a tool needs that tree. An explicit `tyhpdefInclude` entry can still point at a `composer.json` that has `extra.tyhp.package`.

Individual runtime packages pull PHP builtins by an explicit include, for example `core`'s `tyhp.json`:

```json
{
    "include": [
        "./tyhp_src/**/*.tyhp",
        "../php/composer.json"
    ]
}
```

Patterns ending in `.tyhpdef` or `composer.json` from `include` are promoted into the tyhpdef load set (`composer.json` only when `extra.tyhp.package` is a JSON object).

## Load order

1. Embedded tyhpdefs (0)
2. TyhpSpec (1)
3. Composer `vendor/` packages + explicit `composer.json` includes with `extra.tyhp.package` (100+)
4. User `tyhpdefInclude` / promoted `include` raw `.tyhpdef`/`.tyhp` paths (300)
5. Package and project `"overlay"` globs after their includes, in array order (`OverlaySequence`)

Excludes from `tyhpdefExclude` are applied after all sources are collected.

## PHP-version gates on loaded packages

`Tyhpdef.GetSourceFiles` only **discovers and parses**. Registration is `TyhpdefSymbolRegistrar` → `BindTyhpdefSourceFile` → `BindFile`, so PHP-version gates apply to Composer `vendor/` packages and to explicit `composer.json` includes:

- Inactive file-level `declare(php=…);` skips that file’s symbols. Package `.tyhp` overlays parse the Tyhp declare forms (`declare(...);` and `declare(...) { }`). Tyhpdef files currently parse only the colon/`enddeclare` declare form, whose body is PHP `innerStatementList` rather than tyhpdef declarations — so `.tyhpdef` stubs gate with `#[\Tyhp\Php]` on functions/types/members.
- Unsatisfied `#[\Tyhp\Php("…")]` omits the declaration. Evaluation is `PhpVersionConstraint.Evaluate` against `CompilationOptions.PhpVersion` — the same constraint language as user projects; package load does not fork a second evaluator.

Story 21’s single `tyhpdef/php` (and `tyhpdef/php-ext-*`) layout depends on this filtering. Versioned vendor directories named `tyhpdef/php-{major}.{minor}` are still skipped unless they match the target (legacy per-minor layout). `tyhpdef/php-ext-*` is not a versioned PHP-minor package and is always eligible. The unversioned `tyhpdef/php` package is always eligible and relies on gates instead.

Composer `require` and `require-dev` both contribute `extra.tyhp.package` tyhpdefs from `vendor/` (devs put compile-only stubs such as `tyhpdef/php-ext-curl` in require-dev). A package that is **only** `require-dev` does not contribute its `.tyhp` sources to the consumer lint/build file set.

Runtime package identity for TYHP8027 uses `composer.json` `"name"` (`tyhp/core` → `core`) when present, so a path-repo install whose physical directory is `dist/tyhp-core/805.0.1` still counts as `core`. Directory-name heuristics (`vendor/tyhp/{name}` or `packages/{name}`) remain the fallback.

## Graceful fallback when packages are missing

The compiler **never crashes** when optional tyhpdef packages are absent:

| Missing package | Severity | Code | Behavior |
|-----------------|----------|------|----------|
| `tyhpdef/php` (and no explicit include of PHP-extension tyhpdefs) | Warning | `8026` | Continue with hardcoded built-ins only |
| `tyhp/core`, `tyhp/decimal`, `tyhp/async`, or `tyhp/lambda` | Warning | `8027` | Continue; affected runtime types unavailable |

Warnings are emitted once per missing package during tyhpdef loading.

## What is **not** bundled with the compiler binary

- PHP extension `.tyhpdef` files (Composer package `tyhpdef/php`, gated per `output.phpVersion`)
- Runtime package tyhpdefs (`tyhp/core`, etc.)
- `DebugProject/tyhpdef_gen/` development artifacts
- Legacy `OLD_Tyhpdef.cs` Base64 bundle (removed in Phase 6)

Built-in types are compiled into the C# binary; no separate tyhpdef files ship for scalars and utility types.

## Implementation files

| File | Role |
|------|------|
| `Tyhp/Config/Project.cs` | Parses `phpVersion`, `tyhpdefInclude`, `tyhpdefExclude`; promotes `include` patterns for tyhpdefs/manifests |
| `Tyhp/Domain/Services/CompilationOptions.cs` | Carries tyhpdef settings to binder |
| `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs` | Orchestrates load pipeline |
| `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.PackageLoading.cs` | Vendor + explicit `composer.json` (`extra.tyhp.package`) discovery |
| `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.Distribution.cs` | User includes, excludes, missing-package warnings, PHP version matching |

## Verification

```bash
# Repo root tyhp.json supplies explicit package includes
dotnet run --project tyhp.csproj -- lint runtime/packages/async/package.tyhpdef

# Without includes / vendor php-extension for 8.4, expect WARNING_TYHP8026
dotnet run --project tyhp.csproj -- lint --phpVersion=8.4 --tyhpdefInclude:0=./runtime/packages/core/composer.json runtime/packages/core/package.tyhpdef
```

Expected: 0 errors when includes/vendor cover the needed packages; missing-package warnings when they do not.
