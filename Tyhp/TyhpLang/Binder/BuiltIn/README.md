# Built-in Types, Utility Types, and Compile-Time Functions

This directory registers symbols that are always available without loading `.tyhpdef` files.

| File | Purpose |
|------|---------|
| `Types.cs` | Scalar and core built-in types, `decimal`/`struct` aliases, generic metadata for `array` / `iterable`; `callable` is zero-arity (signatures are `callable(…): R` shapes) |
| `UtilityTypes.cs` | Documents that checker utilities are **not** under `\Tyhp` (see `StructUtilityTypes`) |
| `StructUtilityTypes.cs` | Global `__` utilities (`__Nullable`, `__Partial`, `__CallableReturnType`, `__StructKey`, …) |
| `MagicUtilityTypes.cs` | Global `__` Closure / indexing utilities (`__SuperType`, `__SuperTypeName`, `__CurrentScope`, `__CallableThis`, `__CallableScope`, `__IndexKeys`, `__IndexValueType`, `__IndexValueTypes`) |
| `SymbolNameTypes.cs` | Global `__` symbol-name brands (`__ClassName`, `__MethodName`, …) |
| `TypeNameAlgebraTypes.cs` | Global `__` type-name string algebra (`__TypeName`, `__AsType`, …) |
| `Functions.cs` | Compile-time-only functions: `nameof`, `typeof`, `default`, `variable_exists` |
| `Constants.cs` | Magic constants |
| `Variables.cs` | Superglobals |

Registration order in `TyhpBinder.PopulateBuiltIns()`:

1. Built-in types, constants, variables, compile-time functions (`Types.cs`, `Constants.cs`, `Variables.cs`, `SymbolNameTypes.cs`, `StructUtilityTypes.cs`, `MagicUtilityTypes.cs`, `TypeNameAlgebraTypes.cs`, `Functions.cs`)
2. Embedded tyhpdefs (`TyhpBuiltIn.Tyhpdef`)
3. TyhpSpec files (when present)
4. Composer package tyhpdefs via `composer.json` with `extra.tyhp.package` under `vendor/*/*/` from both `require` and `require-dev` (including `tyhpdef/php` and `tyhpdef/php-ext-*`). Runtime-package presence (TYHP8027) uses `composer.json` `"name"` when present. require-dev-only `.tyhp` sources are not compiled/linted as the consumer's files.
5. Explicit `composer.json` files listed in `tyhp.json` `tyhpdefInclude` / promoted `include` when `extra.tyhp.package` is a JSON object (local/dev path for ExtCore + runtime packages — **not** auto-scanned from `runtime/`)
6. User tyhpdef paths from `tyhp.json` (`tyhpdefInclude` / `tyhpdefExclude` raw `.tyhpdef`/`.tyhp` globs)
7. Overlay globs from `extra.tyhp.package` `"overlay"` and `tyhp.json` `overlay` / `tyhpdefOverlay`, after includes, in array order

### Language-construct stubs (`exit` / `die` / `clone`)

`ExtCore.tyhpdef` declares `exit`, `die`, and `clone` as global functions for signatures / named args / FCC identity (Story 14.5). They load only when the PHP-extension package (or its tyhpdefs) is present via **vendor** or an **explicit include**. Package files go through the same binder as user code, so `declare(php=…)` and `#[\Tyhp\Php]` omit inactive symbols for the project's `output.phpVersion`. Emit still rewrites clone-with (and related forms) by `output.phpVersion`. Userland redeclaration is rejected by the grammar (`functionName` is not semi-reserved), matching PHP reserved-keyword behavior.

See `TYHPDEF_DISTRIBUTION.md` for the full distribution strategy, configuration keys, and graceful fallback behavior.

Symbol registration is performed by `TyhpdefSymbolRegistrar`, which binds parsed tyhpdef ASTs through `TyhpBinder` before user code binding.

Parsed tyhpdef ASTs are stored in `AstCacheService` (always, independent of `EnableAstCache` for user files). Each bind deserializes a fresh tree so binder mutations (`BoundSymbol`, `OwningFile`) do not leak across compiles.

## Callable shapes

Bare `callable` is “invokable, signature unknown”. Signatures are **callable shapes** spelled `callable(…): R` (inline or on a `type` alias). `=` on a parameter marks optional (the initializer is discarded). `callable(...): R` is the unknown-arity bound (not a value type). Homogeneous variadic: `callable(T ...$args): R`. Pack splice is an ordinary shape parameter (`callable(__CallableParametersRest<T> ...): R`).

```tyhp
type Callback<TReturn extends void|never|mixed> = callable(string $s): TReturn;
type BiFunction<T1, T2, TReturn extends void|never|mixed> = callable(T1 $a, T2 $b): TReturn;
```

`\Closure` is a class. Spell `\Closure<callable(int $i): string>` (`TCallableShape`, then defaulted `TThis` / `TScope`). `\Closure<int, string>` is not “takes int, returns string”.

The `callable` builtin is zero-arity (`GenericParameterRequirements.ZeroArity()`). `void` / `never` are legal as a shape’s return type.

## Restricted types convention

`void` and `never` cannot be used as generic type arguments unless the generic parameter's constraint explicitly opts in.

Examples:

- `array<void>` is rejected — `array` does not opt in to restricted types.
- `callable(): void` is valid — the shape’s return type may be `void` / `never`.

Each generic type definition chooses which restricted types to allow:

| Type | Allows `void` | Allows `never` |
|------|---------------|----------------|
| `callable` (return of a shape) | yes | yes |
| `Promise` (future) | yes | no |
| `array`, `iterable`, SPL collections | no | no |

Utility types and compile-time function argument validation are implemented in the checker (Story 08).
