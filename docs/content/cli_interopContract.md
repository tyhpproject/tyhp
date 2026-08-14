---
title: 'CLI: Interop Contract'
status:
  tier: 1
  story: '15'
  state: complete
---

The Tyhp ↔ PHP interop contract is the written ABI between the Tyhp compiler (emitter) and the Tyhp runtime packages (`tyhp/core`, `tyhp/async`, `tyhp/decimal`, `tyhp/lambda`). Compiled Tyhp depends on concrete PHP shapes — class names, method names, and call patterns. This page is the user-facing index for that boundary.

:::note
**Emitter dictates runtime.** The emitter design owns the ABI. Runtime packages implement the shapes the emitter emits; they do not invent alternate public APIs that the compiler is expected to chase. See also `CONVENTIONS.md` §8.
:::

## Why this contract exists

Without an explicit contract, the coupling is implicit: emitter transformers call `\Tyhp\…` by exact FQN and signature, and the runtime must match. When either side changes silently, builds break late. Story 15 writes the contract down, stamps a version on both sides, and keeps a machine-checkable surface so drift is caught early.

## Contract version

Each runtime package stamps the contract it implements in Composer metadata:

```json
{
    "extra": {
        "tyhp": {
            "interopContractVersion": 1
        }
    }
}
```

The compiler constant is `Tyhp.TyhpLang.Interop.InteropContract.CurrentVersion` (currently **1**). When a required runtime package’s stamp does not match the compiler’s current version, the build fails with diagnostic **TYHP5018** (error).

:::warning
`interopContractVersion` is **not** the same as the compiler’s `MAJOR.MINOR.PATCH` (see `VERSIONING.md`) or a package’s Composer semver. A breaking change to an emitted name or signature bumps the interop contract on **both** the compiler and the runtime packages.
:::

## Synthetic dispatch naming

Canonical spellings live in compiler helpers — do not invent parallel schemes. Ownership:

| Kind | Owner |
|------|--------|
| Operator overload methods | `OperatorMethodNameGenerator` (`Tyhp/TyhpLang/Emitter/OperatorMethodNameGenerator.cs`) |
| Generics / property-hook polyfill names | `GeneratedNames` (`Tyhp/TyhpLang/GeneratedNames.cs`) |
| Project conventions pointer | `CONVENTIONS.md` §8 |

### Operators

Every overloadable operator maps to a **single** deterministic method name (no type suffixes, no `_N` collision numbers). Multiple forms of the same operator collapse into one method that dispatches on operand types.

**Binary / comparison**

| Operator | Method |
|----------|--------|
| `+` | `__add` |
| `-` (binary) | `__subtract` |
| `*` | `__multiply` |
| `/` | `__divide` |
| `%` | `__mod` |
| `**` | `__pow` |
| `&` | `__bwAnd` |
| `\|` | `__bwOr` |
| `^` | `__bwXor` |
| `<<` | `__bwSL` |
| `>>` | `__bwSR` |
| `.` (concat) | `__concat` |
| `<` | `__isLessThan` |
| `<=` | `__isLessThanOrEqual` |
| `>` | `__isGreaterThan` |
| `>=` | `__isGreaterThanOrEqual` |
| `==` | `__isEqual` |
| `!=` | `__isNotEqual` |
| `===` | `__isExact` |
| `!==` | `__isNotExact` |
| `<=>` | `__compare` |

**Unary / word**

| Operator | Method |
|----------|--------|
| unary `+` | `__asNumeric` |
| unary `-` | `__negate` |
| `++` | `__increment` |
| `--` | `__decrement` |
| `~` | `__bwNot` |
| `!` | `__not` |
| `empty` | `__isEmpty` |

**Convert**

| Form | Method |
|------|--------|
| convert-from (static factory) | `__from` |
| convert-to `string` | `__toString` |
| convert-to `bool` | `__toBool` |
| convert-to `int` | `__toInt` |
| convert-to `float` | `__toFloat` |
| convert-to `decimal` | `__toDecimal` |
| convert-to other type `T` | `__to{FormattedSegment}` |

Binary/unary/`empty` overloads emit as **static** methods. Convert-to emits as an **instance** method so it can satisfy `\Stringable` and `\Tyhp\Contracts\*Convertible`. Call sites rewrite to `ClassName::__add($l, $r)` (and peers). Feature detail: [Operator Overloads](tyhp_1600_operatorOverloads.md).

### GeneratedNames (generics & property hooks)

| Pattern | Example / spelling |
|---------|-------------------|
| Generic variant / bag suffix | `__tyhpGeneric` |
| Generic init hook | `__initGenerics__tyhpGeneric` |
| Generic factory | `new_<MangledFqn>__tyhpGeneric` |
| Polyfill get hook | `__get_<prop>__tyhpPropertyHook` |
| Polyfill set hook | `__set_<prop>__tyhpPropertyHook` |
| Polyfill init hook | `__initPropertyHooks__tyhpPropertyHook` |
| Extension `$this` receiver rename | `$this_` (`GeneratedNames.ExtensionReceiverThisAlias`) |

These identifiers (except the extension-receiver rename, which is emit-only) are reserved by the checker so user code cannot collide with generated symbols.

### Extensions

Extension methods keep their declared method name. Call sites rewrite instance-style calls into static dispatch with the receiver first:

```php
ExtensionClass::method($receiver, /* …args */);
```

When the author names the receiver `$this` (the documented spelling), emit renames that parameter to `$this_` in the static method signature and rewrites body references (including nested closures) accordingly — PHP rejects a parameter literally named `$this`. Other receiver names emit unchanged.

Null-safe extension calls short-circuit with a temporary receiver binding. See [Extensions](tyhp_2100_extensions.md).

## Type erasure

Tyhp features that exist for type-checking often leave no (or a reduced) PHP footprint:

| Feature | At runtime | See |
|---------|------------|-----|
| **Generics** | Type parameters erased from signatures; tracked objects keep a `GenericObject` bag (`$__tyhpGeneric`) when the emitter needs runtime checks | [Generics](tyhp_0500_generics.md) |
| **Type aliases** | Fully erased; usages spell the underlying type | [Type Aliases](tyhp_0700_typeAliases.md) |
| **Type guards / narrowing** | Compile-time only; `is` / guard checks lower to ordinary PHP boolean tests (`instanceof`, `is_*`, etc.) | [Type Narrowing and Guards](tyhp_0200_typeNarrowingAndGuards.md) |
| **Structs** | Declarations erased; values are associative arrays | [Structs](tyhp_0400_structs.md) |
| **Callable signature utilities** | Compile-time only; `__CallableReturnType<T>` erases to the callable's return type (or `mixed` while `T` is still unbound); `__CallableParametersStruct<T>` / `__CallableParametersTuple<T>` erase to `array` (struct-as-array). Tuple bags use int keys `0..n-1` (`$_1`, `$_2`, … aliases, same shape family as `CallableArgs*`). `__CallableParametersRest<T>` is the rest-unpack marker for `Rest<T> ...$args` (TypeScript `...args: Parameters<T>`); it erases to `mixed` so PHP does not demand each unpacked argument be an array. Defaulted parameters are optional struct fields at check time; optionality does not survive erasure. ExtStandard `\call_user_func` / `\call_user_func_array` tyhpdefs use these utilities; emit is still the PHP builtins (no new `\Tyhp\*` types, no contract bump) | [New Types](tyhp_0150_newTypes.md) |
| **`internal`** | **Not in this alpha** (Story 25). Planned: compile-time visibility only; would emit as ordinary PHP visibility | [internal modifier](tyhp_3100_internalModifier.md) |

What **survives** is whatever the emitter explicitly lowers into calls on the runtime surface below (operators, disposables, async, property-hook polyfills, generic bags, `with`, `PropertyPath` / expression trees, and so on).

## Runtime entry points

FQNs the contract covers. Packages are Composer names under `runtime/packages/`.

### Emitter `RequirePackage` today

The emitter currently records required Composer packages via `EmitContext.RequirePackage` for:

- **`tyhp/core`** — generics, `Type`/`NamedType`, property-accessor polyfill, `ObjectHelper`, convertible contracts, operator overload exceptions, …
- **`tyhp/async`** — `Promise`, await, async wrappers, async disposal paths, …
- **`tyhp/lambda`** — `PropertyPath` and full `Expression` trees construction at call sites

Those packages are what the build action can auto-wire into a project’s `composer.json` from emit.

### Required contract surface (all packages)

Even when the emitter does not yet `RequirePackage` a package for every call site, the following FQNs are part of the **written** interop surface (direct calls, future emit, and self-host):

#### `tyhp/core`

| FQN | Role |
|-----|------|
| `\Tyhp\Type` | Runtime type values / checks (`Type::check`, `Type::is`, …) |
| `\Tyhp\NamedType` | Named type arguments in the generic bag |
| `\Tyhp\GenericObject` | Per-instance generic argument bag |
| `\Tyhp\PropertyAccessor` | Property-hook polyfill registrations |
| `\Tyhp\PropertyAccessorObject` | Host for registered accessors (`$__tyhpPropertyHook`) |
| `\Tyhp\ObjectHelper` | Object-form `with` / clone-with lowering |
| `\Tyhp\Contracts\IsDisposable` | Sync disposable protocol |
| `\Tyhp\Concerns\HasGenerics` | Trait wiring the generic bag |
| `\Tyhp\Concerns\UsesPropertyAccessors` / `HasPropertyAccessors` / `HandlesGet` / `HandlesSet` / `HandlesIsset` / `HandlesUnset` / `BootsTraits` | Property-accessor polyfill concerns |
| `\Tyhp\Contracts\{Convertible,StringConvertible,BoolConvertible,IntConvertible,FloatConvertible}` | Convert-to interfaces |
| `\Tyhp\Exceptions\InvalidParametersForOperatorOverloadException` | Operator dispatch miss |
| `\Tyhp\Exceptions\AggregateException` | Multi-error disposal |
| `\Tyhp\Exceptions\{InvalidTypeException,IncompatibleTypeException,PropertyNotFoundException}` | Key type/property failures |

#### `tyhp/decimal`

| FQN | Role |
|-----|------|
| `\Tyhp\Decimal` | Decimal value type |
| `\Tyhp\Contracts\DecimalConvertible` | Convert-to decimal |

Contract surface for decimal operators / convert targets and direct use. The emitter does not currently list `tyhp/decimal` via `RequirePackage` for every decimal path; projects that use decimal still depend on the package.

#### `tyhp/async`

| FQN | Role |
|-----|------|
| `\Tyhp\Promise` | Async wrappers, `_await`, `_async`, `run` |
| `\Tyhp\EventLoop` | Fiber / timer loop |
| `\Tyhp\CancellationToken` / `\Tyhp\CancellationTokenSource` | Cancellation |
| `\Tyhp\DisposableScope` | `:=` disposable scopes |
| `\Tyhp\Contracts\AsyncIsDisposable` | Async disposal |

#### `tyhp/lambda`

| FQN | Role |
|-----|------|
| `\Tyhp\Expression` | Expression-tree root — **Phase 2 emit target**; runtime base for `PropertyPath` |
| `\Tyhp\Expression\ExpressionNode` (and `Expression\*` node types) | Tree nodes — **Phase 2 emit** builds these directly |
| `\Tyhp\PropertyPath` | Property-path expressions — **Phase 1 emit target** |

**Phase 1 emit ABI (Story 16):** when a call argument targets `PropertyPath<T, R>` and the argument is an inline property-chain `fn`, the emitter lowers to:

```php
new \Tyhp\PropertyPath(
    \SourceType::class,   // or a builtin type string such as 'string'
    'resultTypeString',
    ['segment', /* … */],
    fn (/* … */) => /* original chain */
);
```

Constructor shape (required): `(string $sourceType, string $resultType, array $path, \Closure $callable, bool $allowEmpty = false, array $nullSafeFlags = [])`. Call sites omit `$allowEmpty`.

A chain containing `?->` additionally passes the per-segment flags by name, so the runtime builds `NullSafeAccessExpression` nodes instead of `PropertyAccessExpression` for those segments:

```php
new \Tyhp\PropertyPath(
    \User::class,
    '?string',
    ['address', 'city'],
    fn (\User $u): ?string => $u?->address?->city,
    nullSafeFlags: [true, true],
);
```

**Phase 2 emit ABI (Story 16):** when a call argument targets `Expression<T, R>` and the argument is an inline arrow `fn`, the emitter lowers to a root `Expression` plus nested node constructors (additive — no contract version bump):

```php
new \Tyhp\Expression(
    body: new \Tyhp\Expression\BinaryExpression(
        new \Tyhp\Expression\PropertyAccessExpression(
            new \Tyhp\Expression\ParameterExpression('u', \User::class, 0),
            'age',
            'int'
        ),
        '>',
        new \Tyhp\Expression\ConstantExpression(18, 'int'),
        'bool'
    ),
    parameters: [
        new \Tyhp\Expression\ParameterExpression('u', \User::class, 0),
    ],
    callable: fn (\User $u) => $u->age > 18,
    returnType: 'bool'
);
```

Supported nested FQNs include `ParameterExpression`, `PropertyAccessExpression`, `NullSafeAccessExpression`, `MethodCallExpression`, `StaticMethodCallExpression`, `BinaryExpression`, `UnaryExpression`, `ConstantExpression`, `TernaryExpression`, `CoalesceExpression`, `ArrayAccessExpression`, `CastExpression`, `NewExpression`, and `InstanceofExpression` (Phase 3 — `$x is T` / `$x instanceof T`). Captured outer variables emit as `ConstantExpression($var, $type)`.

`Expression<TArgs…, TReturn>` follows the callable return-last convention: `Expression<R>` is zero-parameter, `Expression<T, R>` is one-parameter, `Expression<T, T, int>` is a two-parameter comparator. That arity is a checker/type-system convention; the runtime class remains `Expression<TSource, TReturn>` and emit still constructs `new \Tyhp\Expression(...)`. No contract version bump.

`nameof(fn ($x) => $x->a->b)` is a compile-time fold to the last property segment (a string literal). It does not emit `PropertyPath` / `Expression` construction.

`ExpressionSerializer::toJson` / `equals` and `Expression::equals` are **runtime library APIs** (not emitter call patterns). User code that calls them depends on `tyhp/lambda` the same way any other package type does.

When a `PropertyPath` / `Expression` value is passed where `\Closure` is expected, emit extracts the stored closure as `$expr->callable` (public readonly property on `\Tyhp\Expression`). Where `callable` is expected, the object is passed through (`__invoke`).

Feature detail: [Parsable Lambdas](tyhp_3000_parsableLambdas.md).

## Lowering protocols (index)

Brief map from Tyhp construct → PHP runtime pattern. Goldens live under the conformance suite; prefer those over re-deriving internals from this page.

| Construct | Lowering sketch | Goldens / docs |
|-----------|-----------------|----------------|
| Disposables `:=` | `\Tyhp\DisposableScope::create()` + `using` / try-finally fallback | `tests/conformance/story11/disposables/`; [Disposables](tyhp_2300_disposables.md) |
| Async / await | `\Tyhp\Promise::_async` / `_await` / `run`; return type `\Tyhp\Promise` | `tests/conformance/story11/async/`; [Async and Await](tyhp_2600_asyncAndAwait.md) |
| `with` / clone-with | `\Tyhp\ObjectHelper::with(…)` (or native PHP 8.5 `clone(…)` when targeted) | `tests/conformance/story11/with/`; Story 14.5 clone suites; [with keyword](tyhp_2200_withKeyword.md) |
| Operators | Static/`__to*` calls via `__add`, … | `tests/conformance/story11/operator-overloads/`; [Operator Overloads](tyhp_1600_operatorOverloads.md) |
| Generics | Erasure + optional `GenericObject` bag / `__initGenerics__tyhpGeneric` / factories | `tests/conformance/story11/generics/`; [Generics](tyhp_0500_generics.md) |
| Property hooks (PHP &lt; 8.4) | Strip native hooks; `UsesPropertyAccessors` + `PropertyAccessor` registration; `__get_/__set_*__tyhpPropertyHook` | Story 14.5 hook suites (native ≥ 8.4/8.5); polyfill paths in emitter |
| Expression trees / PropertyPath | Phase 1: `new \Tyhp\PropertyPath(…)` (plus `nullSafeFlags:` for `?->` chains); Phase 2: `new \Tyhp\Expression(…)` + nested `\Tyhp\Expression\*` nodes; Phase 3: `InstanceofExpression` for `is`/`instanceof`, multi-parameter `Expression<TArgs…, TReturn>` arity (still `new \Tyhp\Expression`); `\Closure` sites → `$path->callable` | `tests/Tyhp.Tests/Emitter/PropertyPathEmitterTests.cs`, `ExpressionEmitterTests.cs`; [Parsable Lambdas](tyhp_3000_parsableLambdas.md) |
| PHP 8.5 surface / lower targets | Pipe, `(void)`, `clone(…)`, exit/die, attributes | `tests/conformance/story14_5/` |

Story 15 owns a thin index suite under `tests/conformance/story15/` (operators, extensions, generics erasure, structs, disposables, async, expression trees). Fuller emit coverage remains in story11 / story14_5 — see the conformance README.

## Compatibility policy

- **Additive** changes (new optional runtime helpers the emitter does not yet call, new convertible targets that do not rename existing methods) may ship without bumping `interopContractVersion`. A minor bump is optional documentation hygiene.
- **Breaking** changes — any rename or signature change of an emitted call, synthetic method, or required FQN — require bumping `interopContractVersion` on the compiler (`InteropContract.CurrentVersion`) **and** on every affected runtime package’s `extra.tyhp.interopContractVersion`.
- **PHP matrix** — which PHP minor a release supports is coordinated with Story 21 and the PHP-encoded compiler **MAJOR** in `VERSIONING.md`. Contract bumps and MAJOR bumps are independent axes; both may move in the same release when an ABI and a PHP target change together.

## Machine-checkable surface

`InteropContractSurface` enumerates emitter-required runtime symbols (the contract surface). Conformance tests assert that each symbol is declared — with the documented namespace and keyword — in the owning package’s committed `package.tyhpdef`. The tyhpdef is the checked source of truth because emitted `src/` PHP is regenerated (and `dist/` is not committed).

Runtime **self-host** (Story 07: recompile `runtime/packages/*/tyhp_src` and diff against committed PHP) consumes this list once the Story 07 allowlist is green. Until then, Story 15 treats self-host as gated — the surface list is ready for that milestone; it does not block documenting or version-stamping the contract.

:::tip
When changing emit call patterns, update `OperatorMethodNameGenerator` / `GeneratedNames`, the runtime packages, `InteropContractSurface`, this page, and bump `interopContractVersion` if the change is breaking — in that order of truth: **code helpers → runtime → surface enum → docs → version stamp**.
:::
