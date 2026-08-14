# Interop contract (Story 15)

## Role

Codifies the Tyhp compiler ↔ runtime PHP ABI: contract version, required `\Tyhp\*` symbols, and
where build-time enforcement runs. User-facing docs: `docs/content/cli_interopContract.md`.
Project pointer: `CONVENTIONS.md` §8.

## Types

| Type | Purpose |
|------|---------|
| `InteropContract` | `CurrentVersion`, package name list, Composer stamp read/resolve helpers |
| `InteropContractSurface` | Enumerable required FQNs (class/interface) per runtime package |

## Version stamp

Each of `runtime/packages/{core,decimal,async,lambda}/composer.json` (and dist Composer manifests
from `build-common.sh`) carries:

```json
"extra": { "tyhp": { "interopContractVersion": 1 } }
```

Must equal `InteropContract.CurrentVersion`. Mismatch → `MessageCode.EmitterInteropContractMismatch`
(TYHP5018, error).

## Enforcement

`TyhpLibDistributionService.ValidateInteropContractVersions` runs at the start of
`AddRuntimePackageDependencies` whenever the emit context (or content scan) requires any
`tyhp/*` package. For each required package it resolves `composer.json` via the runtime path map
(or `vendor/tyhp/…` under the output directory) and compares the stamp.

## Surface check

Tests under `tests/Tyhp.Tests/Interop/` assert every `InteropContractSurface.RequiredSymbols`
entry appears in the matching package’s committed `package.tyhpdef`.

## Emit call patterns (lambda / Story 16)

`InteropContractSurface` already lists `\Tyhp\PropertyPath`, `\Tyhp\Expression`, and
`\Tyhp\Expression\ExpressionNode` under `tyhp/lambda`. Story 16 wires the emitter:

| Pattern | Where |
|---------|--------|
| `RequirePackage("tyhp/lambda")` | `PropertyPathEmissionHelper` / `ExpressionTreeEmissionHelper` / `AliasConverter.RewriteArgumentListConverts` |
| `new \Tyhp\PropertyPath($sourceType, $resultType, $path, $callable)` | Phase 1 PropertyPath |
| `nullSafeFlags: [bool, …]` appended when the chain uses `?->` | Phase 1 PropertyPath |
| `new \Tyhp\Expression(body:, parameters:, callable:, returnType:)` with nested `\Tyhp\Expression\*` nodes | Phase 2 Expression |
| `$expr->callable` when target is `\Closure` | PropertyPath and Expression |
| `new \Tyhp\Expression\InstanceofExpression($operand, $targetType, $type)` | Phase 3 `is` / `instanceof` in an Expression `fn` body |

`Expression<TArgs…, TReturn>` callable-style arity is a checker convention (last type argument is the return type). Emit still constructs `new \Tyhp\Expression(...)` with N `ParameterExpression` nodes — additive, no contract bump.

`nameof(fn ($x) => $x->a->b)` folds to a string literal at emit time; it is not a runtime `\Tyhp\*` call.

`ExpressionSerializer` / `Expression::equals` are runtime APIs, not emitter call patterns, so they are not on `InteropContractSurface`.

Additive emit of an already-listed FQN does **not** bump `interopContractVersion` — that includes
the optional `$nullSafeFlags` ctor parameter, Phase 2 direct emission of `Expression` /
`Expression\*` node constructors, and Phase 3 `InstanceofExpression` (a new nested node under
the already-listed `ExpressionNode` family). Renaming the ctor, `$callable`, or the FQN would
be breaking and must bump the stamp on compiler + packages.
User-facing ABI detail: `docs/content/cli_interopContract.md` § `tyhp/lambda`.

Checker-only `__CallableReturnType` / `__CallableParametersStruct` /
`__CallableParametersTuple` / `__CallableParametersRest` erase to a concrete
return type, to `array` (struct-as-array), or to `mixed` (Rest, used as a
variadic element type). Named-parameter bags come from
`__CallableParametersStruct` and positional bags from
`__CallableParametersTuple` (int keys `0..n-1`, CallableArgs-style `$_N`
aliases). Optional callable parameters become optional struct fields
(required-key assignability at check time); that optionality does not survive
erasure — the runtime value is still a PHP array. `__CallableParametersRest`
does not introduce a runtime list type: `Rest<T> ...$args` emits as
`mixed ...$args` and the checker unpacks trailing arguments against `T`'s
parameter list. ExtStandard `\call_user_func` / `\call_user_func_array`
tyhpdefs use Rest / Struct / Tuple at check time; emit is still those PHP
functions. Unbound
`__CallableReturnType<TCallable>` (still a type parameter) erases to `mixed`.
They are not `\Tyhp\*` runtime symbols and are not on `InteropContractSurface`.
