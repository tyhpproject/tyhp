# Built-in Types, Utility Types, and Compile-Time Functions

This directory registers symbols that are always available without loading `.tyhpdef` files.

| File | Purpose |
|------|---------|
| `Types.cs` | Scalar and core built-in types, `decimal`/`struct` aliases, generic metadata for `array`, `iterable`, `callable` |
| `UtilityTypes.cs` | Checker utility types in the `\Tyhp` namespace |
| `StructUtilityTypes.cs` | Global `__` struct/type utilities (`__StructKey`, `__FunctionReturnType`, `__CallableReturnType`, `__CallableParametersRest`, …) |
| `SymbolNameTypes.cs` | Global `__` symbol-name brands (`__ClassName`, `__MethodName`, …) |
| `TypeNameAlgebraTypes.cs` | Global `__` type-name string algebra (`__TypeName`, `__AsType`, …) |
| `Functions.cs` | Compile-time-only functions: `nameof`, `typeof`, `default`, `variable_exists` |
| `Constants.cs` | Magic constants |
| `Variables.cs` | Superglobals |

Registration order in `TyhpBinder.PopulateBuiltIns()`:

1. Built-in types, constants, variables, utility types, compile-time functions (`Types.cs`, `Constants.cs`, `Variables.cs`, `UtilityTypes.cs`, `SymbolNameTypes.cs`, `StructUtilityTypes.cs`, `TypeNameAlgebraTypes.cs`, `Functions.cs`)
2. Embedded tyhpdefs (`TyhpBuiltIn.Tyhpdef`)
3. TyhpSpec files (when present)
4. Composer package tyhpdefs via `package.tyhp.json` under `vendor/*/*/` (including `tyhp/php` for PHP builtins)
5. Explicit `package.tyhp.json` manifests listed in `tyhp.json` `tyhpdefInclude` / promoted `include` (local/dev path for ExtCore + runtime packages — **not** auto-scanned from `runtime/`)
6. User tyhpdef paths from `tyhp.json` (`tyhpdefInclude` / `tyhpdefExclude` raw `.tyhpdef`/`.tyhp` globs)

### Language-construct stubs (`exit` / `die` / `clone`)

`ExtCore.tyhpdef` declares `exit`, `die`, and `clone` as global functions for signatures / named args / FCC identity (Story 14.5). They load only when the PHP-extension package (or its tyhpdefs) is present via **vendor** or an **explicit include** — not gated with `declare(php=…)` / `#[\Tyhp\Php]` until Story 20.5 exists. Emit still rewrites clone-with (and related forms) by `output.phpVersion`. Userland redeclaration is rejected by the grammar (`functionName` is not semi-reserved), matching PHP reserved-keyword behavior.

See `TYHPDEF_DISTRIBUTION.md` for the full distribution strategy, configuration keys, and graceful fallback behavior.

Symbol registration is performed by `TyhpdefSymbolRegistrar`, which binds parsed tyhpdef ASTs through `TyhpBinder` before user code binding.

Parsed tyhpdef ASTs are stored in `AstCacheService` (always, independent of `EnableAstCache` for user files). Each bind deserializes a fresh tree so binder mutations (`BoundSymbol`, `OwningFile`) do not leak across compiles.

## Callable generic type convention (return-last)

The `callable` built-in type uses a **return-last** convention for generic parameters:

`callable<TArgs..., TReturn extends void|never|mixed>`

The **last** generic argument is always the return type. All preceding arguments are parameter types.

| Generic syntax | Meaning |
|---|---|
| `callable<string, int>` | Takes `string`, returns `int` |
| `callable<int, int, bool>` | Takes `(int, int)`, returns `bool` |
| `callable<void>` | No parameters, returns `void` |
| `callable<string, void>` | Takes `string`, returns `void` |

The last parameter uses the `ReturnTypeRestricted` constraint so `void` and `never` are valid return types (see restricted types below).

Type aliases that wrap `callable` must propagate this constraint:

```tyhp
type Callback<TReturn extends void|never|mixed> = callable<string, TReturn>;
type BiFunction<T1, T2, TReturn extends void|never|mixed> = callable<T1, T2, TReturn>;
```

`\Closure` follows the same return-last convention: `\Closure<TReturn>` is shorthand for zero arguments, and `\Closure<TArgs..., TReturn>` extends to the multi-parameter form.

Metadata: `BuiltInTypeSymbol.GenericParameterRequirements` for `callable` sets `UsesReturnLastConvention = true`.

## Restricted types convention

`void` and `never` cannot be used as generic type arguments unless the generic parameter's constraint explicitly opts in.

Examples:

- `array<void>` is rejected — `array` does not opt in to restricted types.
- `callable<void>` is valid — the return-type parameter uses `TReturn extends void|never|mixed`.

Each generic type definition chooses which restricted types to allow:

| Type | Allows `void` | Allows `never` |
|------|---------------|----------------|
| `callable` (return param) | yes | yes |
| `Promise` (future) | yes | no |
| `array`, `iterable`, SPL collections | no | no |

Utility types and compile-time function argument validation are implemented in the checker (Story 08).
