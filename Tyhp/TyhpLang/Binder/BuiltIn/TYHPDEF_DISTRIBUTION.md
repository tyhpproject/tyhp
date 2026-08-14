# Tyhpdef Distribution Strategy (Story 06 Phase 6)

This document describes how type definition files reach the Tyhp compiler at bind time.

## Distribution matrix

| Component | Distribution | Discovery |
|-----------|--------------|-----------|
| Built-in scalar/utility types | Hardcoded C# (`Types.cs`, `UtilityTypes.cs`, `Functions.cs`, …) | Always available |
| Embedded legacy tyhp types | Compressed data in `TyhpBuiltIn/Tyhpdef.cs` | Loaded first (load order 0) |
| TyhpSpec | `TyhpSpec/` directory beside the compiler | Load order 1 |
| PHP extension types | `tyhp/php` Composer package | `vendor/tyhp/php/package.tyhp.json` |
| PHP extension types (local/dev) | `runtime/packages/php/` or `runtime/php-extensions/php{version}/` | **Explicit** `tyhpdefInclude` / `include` of that tree's `package.tyhp.json` or `*.tyhpdef` globs — never auto-scanned |
| Runtime library types | `tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda` | `vendor/tyhp/*/package.tyhp.json` **or** explicit `tyhpdefInclude` / `include` of each package's `package.tyhp.json` |
| User/project tyhpdefs | Paths in `tyhp.json` | `tyhpdefInclude` globs (load order 300) |

There is **no** silent discovery of `runtime/packages/` or `runtime/php-extensions/`. Local checkouts must list package manifests (or tyhpdef globs) in `tyhp.json`.

`package.tyhp.json` generation is deferred to Story 20.

## Configuration (`tyhp.json` and CLI)

| Key | C# property | Purpose |
|-----|-------------|---------|
| `phpVersion` or `output.phpVersion` | `Project.PhpVersion` | Target PHP version for emit; `tyhp/php` is a single stubs package (8.2 baseline in this alpha) |
| `tyhpdefInclude` | `Project.TyhpdefIncludePaths` | Glob patterns for additional `.tyhpdef`/`.tyhp` files **and** `package.tyhp.json` manifests |
| `include` | (promoted when pattern ends with `.tyhpdef` or `package.tyhp.json`) | Same as `tyhpdefInclude` for those patterns; also used for compile inputs |
| `tyhpdefExclude` | `Project.TyhpdefExcludePaths` | Glob patterns to exclude after discovery |

CLI overrides use `--key=value` flags (e.g. `--phpVersion=8.2`, `--tyhpdefInclude:0=./tyhpdef/**/*.tyhpdef`).

### Local / monorepo example

The repo root `tyhp.json` pins ExtCore + runtime packages via explicit includes (no auto-scan):

```json
{
    "phpVersion": "8.2",
    "tyhpdefInclude": [
        "./runtime/packages/php/package.tyhp.json",
        "./runtime/packages/core/package.tyhp.json",
        "./runtime/packages/decimal/package.tyhp.json",
        "./runtime/packages/async/package.tyhp.json",
        "./runtime/packages/lambda/package.tyhp.json"
    ]
}
```

Individual runtime packages already pull PHP builtins the same way, e.g. `runtime/packages/core/tyhp.json`:

```json
{
    "include": [
        "./tyhp_src/**/*.tyhp",
        "../../php-extensions/php8.2.9/**/*.tyhpdef"
    ]
}
```

Patterns ending in `.tyhpdef` or `package.tyhp.json` from `include` are promoted into the tyhpdef load set.

## Load order

1. Embedded tyhpdefs (0)
2. TyhpSpec (1)
3. Composer `vendor/` packages + explicit `package.tyhp.json` includes (100+)
4. User `tyhpdefInclude` / promoted `include` raw `.tyhpdef`/`.tyhp` paths (300)

Excludes from `tyhpdefExclude` are applied after all sources are collected.

## Graceful fallback when packages are missing

The compiler **never crashes** when optional tyhpdef packages are absent:

| Missing package | Severity | Code | Behavior |
|-----------------|----------|------|----------|
| `tyhp/php` (and no explicit include of PHP-extension tyhpdefs) | Warning | `8026` | Continue with hardcoded built-ins only |
| `tyhp/core`, `tyhp/decimal`, `tyhp/async`, or `tyhp/lambda` | Warning | `8027` | Continue; affected runtime types unavailable |

Warnings are emitted once per missing package during tyhpdef loading.

## What is **not** bundled with the compiler binary

- PHP extension `.tyhpdef` files (Composer package `tyhp/php`; this alpha is a PHP 8.2 baseline)
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
| `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.PackageLoading.cs` | Vendor + explicit `package.tyhp.json` discovery |
| `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.Distribution.cs` | User includes, excludes, missing-package warnings, PHP version matching |

## Verification

```bash
# Repo root tyhp.json supplies explicit package includes
dotnet run --project tyhp.csproj -- lint runtime/packages/async/package.tyhpdef

# Without includes / vendor php-extension for 8.4, expect WARNING_TYHP8026
dotnet run --project tyhp.csproj -- lint --phpVersion=8.4 --tyhpdefInclude:0=./runtime/packages/core/package.tyhp.json runtime/packages/core/package.tyhpdef
```

Expected: 0 errors when includes/vendor cover the needed packages; missing-package warnings when they do not.
