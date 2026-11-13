# Implementation Plan: Story 48 — PHP 8.4 Lazy Objects

> **Roadmap position:** Story **48**, sequenced in **Tier 3** immediately after Story 22.1 and immediately before Story 22.2. The number is 48 because that is the next unused plan number after 47. 22.1 and 22.2 keep their numbers. Roadmap order, not the story number, decides when the work happens. This story does not depend on 22.1 or 22.2 and does not gate them.
> **Direct dependencies:** 08 (checker), 11 (emitter, including disposables and Mechanism C), 21 / 21.8 (`tyhpdef/php` Reflection overlay), 27.1 (`callable(…): R`). Runtime packages: `tyhp/core` (helper) and `tyhp/async` (disposal paths).
> **New story.** No legacy number.
> **Conventions:** Diagnostic codes live in `Tyhp/Domain/Exceptions/MessageCode.cs`. New user-facing strings go in both `Resources/CLI.TyhpHostedService.resx` and `Resources/CLI.TyhpHostedService.en-US.resx`.
> **Generated:** 2026-11-05
> **Branch:** TBD

Implementers of this story update the Status table as slices land. The agent that owns `tyhp/rhetoric` trusts that table (see [For the tyhp/rhetoric implementer](#for-the-tyhprhetoric-implementer)).

---

## Status

| Slice | State | Landing evidence |
|-------|--------|------------------|
| Overall | Not started | This table. Every slice is Not started. |
| A — tyhpdef callable types | Landed | `tyhp-runtime-src/packages/tyhpdef/php/_tyhpdef/overlays/Ext.Reflection.tyhpdef`: `newLazyGhost` / `resetAsLazyGhost` take `callable(T): void`; `newLazyProxy` / `resetAsLazyProxy` take `callable(T): T`. Tests: `packages/tyhpdef/php/tests/reflection.tyhp`, `tests/fail/lazy_ghost_initializer_not_void.tyhp` (TYHP4009), `tests/fail/lazy_proxy_factory_wrong_return.tyhp` (TYHP4009). |
| B — `\Tyhp\Lazy` | Landed | `tyhp-runtime-src/packages/tyhp/core/tyhp_src/Lazy.tyhp`, `tests/LazyTest.php` (`testGhost_OnPhp84_StaysUninitializedUntilPropertyRead`, `testGhost_DoesNotBindGenericArguments`, `testGhost_InitializerThatThrows_LeavesObjectLazyOnPhp84`, `testGhost_ForwardsOptions_OnPhp84`, `testReset_ForwardsOptions_OnPhp84`, `testGhost_BelowPhp84_RunsInitializerImmediately`, `testProxy_OnPhp84_IsNotTheRealInstance`, `testIsLazy_FalseForOrdinaryObject_AndForEagerFallback`, `testInitialize_ForcesInit_OnPhp84_AndReturnsSameInstance`, `testReset_OnPhp84_BecomesLazyAgain`, `testPreset_DoesNotInitialize`, `testEmittedLazyPhp_MatchesPhpVersionFloor`). `__PropertyName<T>` is the `preset` parameter. 804 emits native calls and no `\PHP_VERSION_ID`. 802 emits only the eager path. |
| C — `lazy new` | Landed | `lazy new Class(args)` / `lazy new Class<T>(args)`. TYHP4402 (`CheckerLazyNewNotCapable`), TYHP4403 (`CheckerLazyNewHasNoLazyProperty`). PHP ≥ 8.4 with a stored instance property emits `ReflectionClass::newLazyGhost` (`TyhpEmitter.Expressions.cs`); a Mechanism C class emits `new_<Mangled>__tyhpLazy`, which calls `__initGenerics__tyhpGeneric` then `__construct` (`TyhpEmitter.GenericClasses.cs`). No stored property, or a floor below 8.4, emits eager `new` or the existing generic factory, because PHP skips a ghost initializer when nothing is lazy-capable. Tests: `LazyNewParseTests`, `LazyNewCheckerTests`, `LazyNewEmitterTests`. |
| D — disposal | Landed | `Tyhp/TyhpLang/Emitter/TyhpEmitter.Disposables.cs` (`EmitSyncDisposeGuard`, `DisposableGuardCondition`) and `Tyhp/TyhpLang/Emitter/TyhpEmitter.Statements.cs` (`using` / `using await`) emit `$var instanceof CONTRACT && !(\PHP_VERSION_ID >= 80400 && (new \ReflectionClass($var))->isUninitializedLazyObject($var))` (no `\PHP_VERSION_ID` test when the floor is 8.4 or newer, and no `\Tyhp\Lazy` call). `tyhp-runtime-src/packages/tyhp/async/tyhp_src/DisposableScope.tyhp` and `DisposableHelper.tyhp` skip before dispose with one source for every build, so the 802 build skips a ghost on PHP 8.4. Tests: `tests/Tyhp.Tests/Emitter/LazyDisposeEmitterTests.cs`; `packages/tyhp/async/tests/DisposableScopeTest.php` and `DisposableHelperTest.php` (`testSkipsUninitializedGhost`, `testDisposesInitializedGhostAndOrdinaryObject`), also run with `TYHP_TEST_DIST_BUILD=802`. |
| E — docs, guide, grammar | Landed | `tyhp-docs-src/content/tyhp_0152_lazyObjects.md` (TOC after `tyhp_0151_newFunctions.md`; pointers in `quickref.md`, `package_core.md`, `tyhp_0500_generics.md`, `tyhp_2300_disposables.md`; `diagnostics_reference.md` regenerated for TYHP4402 and TYHP4403). `tyhp-ai-dev-guide/guide/31-lazy-objects.md`, `QUICK_GUIDE.md`, `SKILL.md`, `guide/00-index.md`, `guide/14-disposal.md`, `handbook/07-runtime-api.md`. Grammar: `tyhp-ide-plugin/vscode/syntaxes/tyhp.tmLanguage.json` scope `keyword.control.lazy.tyhp` with a `new` lookahead (PhpStorm uses that grammar). `Generic::bind()->ghost()` is documented; the method is on `Generic.tyhp`. |
| F — `Generic::bind()->ghost()` | Landed | `tyhp-runtime-src/packages/tyhp/core/tyhp_src/Generic.tyhp`. `#[\Tyhp\Php(">=8.4")]` `ghost` returns `ReflectionClass::newLazyGhost`; the initializer `bindTo`s the ghost's class and calls `__initGenerics__tyhpGeneric` with the padded type arguments, then `__construct`, on that instance. `#[\Tyhp\Php("<8.4")]` `ghost` returns `new`. Tests: `packages/tyhp/core/tests/GenericTest.php` (`testNewStaysEager`, `testGhostOnPhp84DefersConstructionUntilPropertyRead`, `testGhostBelowPhp84MatchesNew`, `testGhostBindsTypeArgumentsBeforeTheConstructor`, `testCallableBindHasNoGhost`, `testEmittedGenericPhpMatchesThePhpVersionFloor`). 804 and 805 emit `newLazyGhost` and no `\PHP_VERSION_ID`. 802 and 803 emit only `newWithArgs`. |

When a slice lands, change its state to `Landed` and replace the evidence cell with the files and test names a reader can open to see the behavior. `In progress` is for a slice that has a partial diff and is not yet the contract above.

---

## Table of Contents

- [Summary](#summary)
- [Decided](#decided)
- [Verify while implementing](#verify-while-implementing)
- [PHP version floors](#php-version-floors)
- [Slice A — tyhpdef callable types](#slice-a--tyhpdef-callable-types)
- [Slice B — `\Tyhp\Lazy` in tyhp/core](#slice-b--tyhplazy-in-tyhpcore)
- [Slice C — `lazy new` in the compiler](#slice-c--lazy-new-in-the-compiler)
- [Slice D — disposal](#slice-d--disposal)
- [Slice E — docs, AI dev guide, IDE grammar](#slice-e--docs-ai-dev-guide-ide-grammar)
- [Slice F — `Generic::bind()->ghost()`](#slice-f--genericbind-ghost)
- [For the tyhp/rhetoric implementer](#for-the-tyhprhetoric-implementer)
- [Rejected alternatives](#rejected-alternatives)
- [Non-goals](#non-goals)

Ship A, then B. C and D can proceed once B's callable types exist where they use them (C does not need the tighter callable types; D does not call `isLazy`). F (`Generic::bind()->ghost()`) does not wait on B or C and does not change the compiler. E last, after the signatures and diagnostics are frozen.

---

## Summary

PHP 8.4 lazy ghosts (`ReflectionClass::newLazyGhost`) and lazy proxies (`newLazyProxy`) become a Tyhp feature in three layers:

1. The Reflection overlay names the initializer and the factory as callable shapes.
2. `\Tyhp\Lazy` in `tyhp/core` is the runtime API, with an eager fallback on PHP older than 8.4.
3. `lazy new ClassName(args)` and `lazy new ClassName<T>(args)` defer the constructor on builds whose `output.phpVersion` is at least 8.4, and bind Mechanism C type arguments before that constructor runs.

A class-string `\Tyhp\Generic` binder gains `ghost(...)` (Slice F) when the class or the type arguments are chosen at runtime. `new` and `__invoke` on that binder stay eager. `Generic.tyhp` is part of this story.

`using` / `:=` must not initialize a ghost just to dispose it. Docs, the AI dev guide, and the editor grammar describe the same contract.

`tyhp/rhetoric` is out of scope for every slice. The rhetoric agent implements lazy loading later, against this file. See the chapter below.

---

## Decided

1. **Ghosts keep identity. Proxies do not.** A ghost initializes in place (`$ghost === $ghost` before and after init). A proxy forwards to a separate real instance (`$proxy !== $real`).
2. **`lazy` is only the word before `new`.** It is not a modifier on classes, functions, properties, or parameters. `Tyhp\Rhetoric\Query\Query::lazy()` (`packages/tyhp/rhetoric/tyhp_src/Query/Query.tyhp`, `lazy(int $count = 1000): \Generator<int, T>`) stays a method. Do not edit that method. Do not edit anything under `packages/tyhp/rhetoric/` or `packages/dist/tyhp-rhetoric/`.
3. **Arguments to `lazy new` are eager.** They run left to right at the `lazy new` expression, the same as `new`. Only the constructor body is deferred, and only when the build targets PHP ≥ 8.4.
4. **If the initializer throws, PHP reverts the object to lazy.** Tyhp does not catch that.
5. **These operations initialize a lazy object:** property access, `ReflectionProperty::getValue` / `setValue`, `get_object_vars`, `foreach`, `serialize`, `json_encode`, and `clone`. A method call that does not touch state does not. `ReflectionClass::SKIP_INITIALIZATION_ON_SERIALIZE` opts out of serialize-init only. It does not affect `json_encode`.
6. **Internal classes other than `stdClass` cannot be lazy.** A class that extends an internal class other than `stdClass` cannot either. A class with no lazy-capable instance property is returned by PHP as a normal instance and the initializer is never called (verified on PHP 8.5.12: the constructor does not run at all). `lazy new` therefore emits an ordinary `new` for such a class and Tyhp warns. A tracked generic class (Mechanism C) always has the injected `$__tyhpGeneric` bag, a real instance property, so it is lazy-capable even when the author declares no property.
7. **Generic classes are not constructed with bare `new`.** Mechanism C (`Tyhp/TyhpLang/Emitter/technical-guide.md`, "Mechanism C") emits `new_<MangledFqn>__tyhpGeneric`: `ReflectionClass::newInstanceWithoutConstructor()`, then protected `__initGenerics__tyhpGeneric`, then `__construct`. A hand-written `newLazyGhost(fn ($o) => $o->__construct(...))` skips that hook, and the constructor prologue then binds nulls (`needsInit()` is still true). `lazy new` emits a second factory whose closure runs the hook, then the constructor, in class scope. `\Tyhp\Lazy::ghost` does not bind: its `T` is the class, not the class's type arguments. Binding belongs to `lazy new` (Slice C) and to `Generic::bind(...)->ghost(...)` (Slice F).
8. **Do not change `Type.tyhp`'s `lazyInner`.** That field is a forward-reference wrapper inside `\Tyhp\Type`, not PHP lazy objects.
9. **No `lazy proxy new` syntax.** Proxies stay on `\Tyhp\Lazy::proxy`.
10. **Doctrine.** `enableNativeLazyObjects(bool)` and `isNativeLazyObjectsEnabled(): bool` already exist on the Doctrine ORM 3.7 tyhpdef (`packages/tyhpdef/doctrine-orm/3.7.1/_tyhpdef/doctrine.orm.tyhpdef`). Mention them in the docs slice. Do not edit that tyhpdef.

---

## Verify while implementing

These are checks, not open design choices. If a check fails, fix the implementation to match the decided behavior. Do not pick a different product behavior.

- Callable shapes on the Reflection overlay accept the class type parameter `T` (Slice A). The fallback signature is specified there if they do not.
- `__PropertyName<T>` accepts a method type parameter (Slice B). The fallback type is specified there if it does not.
- `ReflectionMethod::invoke` can call protected `__initGenerics__tyhpGeneric` on PHP 8.2+ without `setAccessible`.
- The hook's own-level argument list matches `EmitGenericFactoryIfNeeded` (the hook chains to the parent). A `Box<int>` ghost must observe `int` inside the constructor, not null.
- Which symbol flag means "this class is an internal PHP class" (declared by `tyhpdef/php`, not by user code).
- Which instance properties PHP treats as not lazy-capable (static properties, and virtual hook-only properties). Confirm against PHP 8.4, then match the warning.
- `ReflectionProperty::isInitialized` on an uninitialized ghost. Slice D and the rhetoric chapter do not depend on it. Reading a preset key uses `getRawValue`, which the overlay already types.

---

## PHP version floors

`output.phpVersion` is the oldest PHP the build runs on, with no upper bound (`tyhp-ai-dev-guide/guide/30-php-version-gating.md`). `RuntimeGateEmission.ClassifyAtOrAbove` (`Tyhp/TyhpLang/Emitter/RuntimeGateEmission.cs`):

| Constraint vs the floor | Emitted |
|-------------------------|---------|
| True for every version at or above the floor | The body, no `\PHP_VERSION_ID` check |
| True for none of them | Nothing |
| True for only some | The body inside `if (\PHP_VERSION_ID …)` |

`tyhp/core` is built four times (`packages/build-common.sh` `DIST_BUILDS`, `tyhp.json` `output.path` `../../dist/tyhp-core/{phpVersion.id}.{version.major}.{version.minor}/src`, override `--output:phpVersion=`). Published composer constraints are `>=8.2`, `>=8.3`, `>=8.4`, `>=8.5` (`php_constraint_for_major`). There is no ceiling.

| Build | Floor | Native lazy body (`>=8.4`) | Eager body (`<8.4`) |
|-------|-------|----------------------------|---------------------|
| 802, 803 | 8.2, 8.3 | Emitted inside `\PHP_VERSION_ID` | Emitted inside `\PHP_VERSION_ID` |
| 804, 805 | 8.4, 8.5 | Emitted with no check | Omitted |

Expression-level features (`|>`, property hooks, readonly `with`) do not use that declaration gate. They call `EmitContext.IsPhpVersionAtLeast` and emit one shape. `lazy new` follows that expression pattern, not the declaration pattern. See Slice C.

---

## Slice A — tyhpdef callable types

**Floor.** The methods are already `#[\Tyhp\Php(">=8.4")]`. This slice only tightens parameter types. No new PHP floor.

**Files.**

- `tyhp-runtime-src/packages/tyhpdef/php/_tyhpdef/overlays/Ext.Reflection.tyhpdef`
- `tyhp-runtime-src/packages/tyhpdef/php/tests/reflection.tyhp`
- `tyhp-runtime-src/packages/tyhpdef/php/tests/fail/lazy_ghost_initializer_not_void.tyhp` and `lazy_ghost_initializer_not_void.expect.json`
- `tyhp-runtime-src/packages/tyhpdef/php/tests/fail/lazy_proxy_factory_wrong_return.tyhp` and `lazy_proxy_factory_wrong_return.expect.json`
- `tyhp-runtime-src/CHANGELOG.md` (`tyhpdef/php: …`)

Leave `packages/tyhpdef/php/_tyhpdef/Ext.Reflection.tyhpdef` as untyped `callable`. The `@overlay-against` comments stay the Layer 1 signatures (`callable $initializer`, `callable $factory`). `TYHP8021` compares those comments to Layer 1.

**Signatures.** `ReflectionClass<T extends object>` already uses `T` on `initializeLazyObject(T $object): T` and `newInstance(): T`. Callable shapes already name a class type parameter (`callable(T): R` on `Box<T>::map` in `tests/Tyhp.Tests/Checker/SourceOverloadTests.cs`). Guide: `tyhp-ai-dev-guide/guide/07-generics.md` (`callable(T): U`, `callable(): void`). Replace the four overlay methods:

```tyhp
#[\Tyhp\Php(">=8.4")]
public function newLazyGhost(callable(T): void $initializer, int $options = 0): T;

#[\Tyhp\Php(">=8.4")]
public function newLazyProxy(callable(T): T $factory, int $options = 0): T;

#[\Tyhp\Php(">=8.4")]
public function resetAsLazyGhost(T $object, callable(T): void $initializer, int $options = 0): void;

#[\Tyhp\Php(">=8.4")]
public function resetAsLazyProxy(T $object, callable(T): T $factory, int $options = 0): void;
```

Keep the return type `T` on the creating methods. Keep the `>=8.4` attribute.

The comment above `hasConstant` in that overlay is about symbol-name guards (`__MethodName<T>`), which do not see the class generic unless it is redeclared as a method generic. `callable(T)` is not a symbol-name guard. Do not copy that workaround unless the test below fails.

**Fallback, only if the overlay test rejects `callable(T)`.** Redeclare a method generic that defaults to the class generic, and keep the return type `T`:

```tyhp
public function newLazyGhost<TObj extends object = T>(callable(TObj): void $initializer, int $options = 0): T;
public function newLazyProxy<TObj extends object = T>(callable(TObj): TObj $factory, int $options = 0): T;
public function resetAsLazyGhost<TObj extends object = T>(T $object, callable(TObj): void $initializer, int $options = 0): void;
public function resetAsLazyProxy<TObj extends object = T>(T $object, callable(TObj): TObj $factory, int $options = 0): void;
```

If that also fails, stop. Do not widen the parameters back to untyped `callable` and do not push the shape down into the Layer 1 stub.

**Tests.** The harness is `packages/test-all-tyhpdef.sh` (positives under `packages/tyhpdef/php/tests/`, failures in `tests/fail/*.tyhp` plus `tests/fail/<stem>.expect.json`). `tyhpdef/php` is linted at `tests/tyhp.json` (PHP 8.2) and at `tyhp-php8.4.json`. A call that exists only on 8.4 must sit in `declare(php=">=8.4")`, the same way `tests/standard.tyhp` wraps `\array_find`.

In `reflection.tyhp`, inside `declare(php=">=8.4")`:

- `newLazyGhost` accepts `fn (\stdClass $o): void => …` on `\ReflectionClass<\stdClass>` and the result is `\stdClass`.
- `newLazyProxy` accepts `fn (\stdClass $o): \stdClass => $o` and the result is `\stdClass`.
- The matching `resetAsLazyGhost` / `resetAsLazyProxy` calls typecheck.

Fail fixtures (wrong return / wrong parameter) set `"php": "8.4"` in the expect sidecar so `project_for_php_version` selects `tyhp-php8.4.json`. Record the diagnostic code the lint actually reports in `"codes"`. That code is an existing assignability code, not TYHP4402.

Run: `packages/test-all-tyhpdef.sh --package php` from `tyhp-runtime-src`.

---

## Slice B — `\Tyhp\Lazy` in tyhp/core

**Floor.** See the table in [PHP version floors](#php-version-floors). 804 and 805 emit only the native calls, with no `\PHP_VERSION_ID`. 802 and 803 emit both bodies, each inside `\PHP_VERSION_ID`, because those artifacts require `php: >=8.2` / `>=8.3` and can run on 8.4.

**Files.**

- `tyhp-runtime-src/packages/tyhp/core/tyhp_src/Lazy.tyhp` (new)
- `tyhp-runtime-src/packages/tyhp/core/tests/LazyTest.php` (new)
- `tyhp-runtime-src/packages/tyhp/core/phpunit.xml` (add the file to the `core-runtime` suite; that suite lists files, it does not scan the directory)
- `tyhp-runtime-src/CHANGELOG.md` (`tyhp/core: …`)

Do not edit `Type.tyhp`. Do not add a second helper under rhetoric.

**Pattern.** `Json::decode<T>` reifies a method type parameter with `typeof(T)` (`packages/tyhp/core/tyhp_src/Json.tyhp`). That is Mechanism D: a generic method that mentions `typeof(T)` receives a real `\Tyhp\Type`. `\Tyhp\Type::getName()` and `\Tyhp\Type::typeArguments()` are the runtime view of that type (`Type.tyhp`). Use the same mechanism. Do not add a `class-string<T>` parameter. `T` is a method generic, so it is reified. `final class`, private constructor, same shape as `Json` and `DisposableHelper`.

**API.**

```tyhp
namespace Tyhp;

final class Lazy {
    private function __construct() {}

    public static function ghost<T extends object>(
        callable(T): void $initializer,
        int $options = 0,
    ): T;

    public static function proxy<T extends object>(
        callable(T): T $factory,
        int $options = 0,
    ): T;

    public static function isLazy(object $object): bool;

    public static function initialize<T>(T $object): T;

    public static function reset<T extends object>(
        T $object,
        callable(T): void $initializer,
        int $options = 0,
    ): void;

    public static function preset<T extends object>(
        T $object,
        __PropertyName<T> $property,
        mixed $value,
    ): void;
}
```

`guide/18-string-as-type.md` types `function id<T>(): __ClassName<T>` and `__PropertyName<User>`. `__PropertyName<T>` on this method generic is the property name. If the checker rejects `__PropertyName<T>` here, the parameter type is `string`. Do not switch the method to a class-string API.

Write each method twice, with disjoint `#[\Tyhp\Php]` attributes (legal on class methods; `TYHP4371` is properties, class constants, enum cases, and interface methods). `StringHelper.tyhp` already uses `#[\Tyhp\Php(">=8.4")]` on methods. `ArrayExtensions.tyhp` uses `declare(php=…)` because an `extension` cannot carry the attribute (`TYHP4304`). `Lazy` is a class, so the attribute is the right tool. Sibling methods must be disjoint or `TYHP4303` fires.

```tyhp
#[\Tyhp\Php(">=8.4")]
public static function ghost<T extends object>(callable(T): void $initializer, int $options = 0): T {
    // native body
}

#[\Tyhp\Php("<8.4")]
public static function ghost<T extends object>(callable(T): void $initializer, int $options = 0): T {
    // eager body
}
```

Same split for `proxy`, `isLazy`, `initialize`, `reset`, and `preset`.

**`ghost` on `>=8.4`.**

1. Reflect the class (`new \ReflectionClass<T>(T::class)`).
2. `$reflection->newLazyGhost($initializer, $options)`. No generic binding: `T` is the class of the instance, not its type arguments, so `ghost` must not guess them. A caller-supplied initializer, `lazy new` (Slice C), or `Generic::bind(...)->ghost(...)` (Slice F) binds them. An exception from the initializer propagates and the object stays lazy.
3. Return the object as `T`. PHP returns a normal instance when the class has no lazy-capable property; that is correct, and the initializer has already run.

**`ghost` on `<8.4`.** `newInstanceWithoutConstructor()`, then the initializer, then return that instance. Same end state. The object is not lazy. `isLazy` on it is false.

**`proxy`.** On `>=8.4`, `newLazyProxy` with the caller's factory. Do not bind generics on the proxy shell. The real instance is whatever the factory returns; a `new` inside that factory is ordinary Mechanism C. On `<8.4`, call the factory with `newInstanceWithoutConstructor()` of `T` and return what the factory returns. Document that the `<8.4` proxy path is not a proxy: the caller receives the factory's return value immediately.

**`isLazy`.** On `>=8.4`, `(new \ReflectionClass($object))->isUninitializedLazyObject($object)`. On `<8.4`, `false`. This is the class-level uninitialized check. It is not `ReflectionProperty::isLazy` (that method is already on the overlay and means something else).

**`initialize`.** On `>=8.4`, `(new \ReflectionClass($object))->initializeLazyObject($object)` and return it as `T`. On `<8.4`, return `$object`.

**`reset`.** On `>=8.4`, `resetAsLazyGhost($object, $initializer, $options)`. The helper does not force `SKIP_DESTRUCTOR`; the caller passes it. On `<8.4`, run the initializer on the existing instance immediately. Do not claim the instance became lazy.

**`preset`.** On `>=8.4`, `ReflectionProperty::setRawValueWithoutLazyInitialization` (already on the overlay, gated `>=8.4`, object typed `T`). On `<8.4`, `ReflectionProperty::setValue`. Neither path goes through property hooks on purpose: the 8.4 method is the raw write; the older path is ordinary reflection set.

**Options.** `$options` is forwarded. `ReflectionClass::SKIP_INITIALIZATION_ON_SERIALIZE` and `ReflectionClass::SKIP_DESTRUCTOR` already exist on the base stub. Do not redeclare them.

**Tests** (`tests/LazyTest.php`, PHPUnit against a built `dist/tyhp-core`). Follow `tests/GenericTest.php`, which already builds a stand-in Mechanism C class with `__initGenerics__tyhpGeneric`.

- `Ghost_OnPhp84_StaysUninitializedUntilPropertyRead` (skip the body when `PHP_VERSION_ID < 80400`).
- `Ghost_DoesNotBindGenericArguments` — a `Box<int>` stand-in ghost constructed by its initializer records `mixed`, not `int`.
- `Ghost_InitializerThatThrows_LeavesObjectLazyOnPhp84`, `Ghost_ForwardsOptions_OnPhp84`, `Reset_ForwardsOptions_OnPhp84`.
- `Ghost_BelowPhp84_RunsInitializerImmediately`.
- `Proxy_OnPhp84_IsNotTheRealInstance`.
- `IsLazy_FalseForOrdinaryObject_AndForEagerFallback`.
- `Initialize_ForcesInit_OnPhp84_AndReturnsSameInstance`.
- `Reset_OnPhp84_BecomesLazyAgain`.
- `Preset_DoesNotInitialize`.

Build with `packages/base-build-all.sh` or a core-only `dotnet …/tyhp.dll build --output:phpVersion=8.4` (and once with `8.2`) before PHPUnit. Assert the 8.4 emitted `Lazy.php` contains `newLazyGhost` and does not contain `\PHP_VERSION_ID`. Assert the 8.2 emitted file contains `\PHP_VERSION_ID` and both `newLazyGhost` and `newInstanceWithoutConstructor`.

Rhetoric calls this class. It does not declare its own wrapper. See the rhetoric chapter.

---

## Slice C — `lazy new` in the compiler

**Floor.** One shape per build, chosen with `IsPhpVersionAtLeast(8, 4)` inside `BuildNewExpression`. No `\PHP_VERSION_ID` in the expression.

| `output.phpVersion` | Emit |
|---------------------|------|
| ≥ 8.4 | `newLazyGhost`, or the lazy generic factory below |
| < 8.4 | The existing eager `new`, or the existing `new_<Mangled>__tyhpGeneric` factory |

A project that needs a ghost when an older-floor binary happens to run on 8.4 calls `\Tyhp\Lazy::ghost` (Slice B). `lazy new` will not.

**Syntax.** `lazy new ClassName(args)` and `lazy new ClassName<T>(args)` only, including an empty argument list. `lazy new` uses the same `classNameReference` `new` uses, so generic arguments parse the way `new Box<int>(...)` already parses (`Tyhp/TyhpLang/Grammar/shared/TyhpOverrides.reached.g4`, `newDereferenceable` / `newNonDereferenceable`).

`lazy new class`, `lazy new $expr`, and `lazy` in any other position are not this feature.

**Lexer.** Do not make `lazy` a hard keyword. `with` is reclassified to `T_TYHP_WITH` for every occurrence (`TyhpLexer.GrammarMethods.cs`, `EqualsKeyword(text, "with")`). `lazy` must not follow that. In that same reclassifier, when the language mode is tyhp and the text is `lazy` and the next default-channel token is `T_NEW`, set the token type to a new `T_TYHP_LAZY`. Otherwise leave `T_STRING`. `->lazy(`, `function lazy(`, and `Query::lazy()` stay identifiers because the next token is `(`.

Add `T_TYHP_LAZY` to the token list in `Tyhp/TyhpLang/Grammar/TyhpLexer.g4`. Edit `TyhpLexer.GrammarMethods.cs` (the tyhpdef copy is generated from it). Regenerate with `./compile_grammar.sh`. Do not hand-edit `TyhpParser.cs`.

**Grammar.** In `shared/TyhpOverrides.reached.g4`, add a tyhp-gated alternative to both `newDereferenceable` and `newNonDereferenceable`:

```
T_TYHP_LAZY T_NEW Identifier=classNameReference Arguments=argumentList
```

The non-dereferenceable form is the same with the existing `newIsFollowedByArgumentList` guard, so `lazy new Foo` and `lazy new Foo()` follow `new`. Labels: `#tyhpLazyNew` and `#tyhpLazyNewNonDereferenceable`.

**Visitor and AST.** Visit methods are authored in `Tyhp/TyhpLang/Visitor/shared/PhpParserAstVisitor/newDereferenceable.inc` and `newNonDereferenceable.inc`, then copied by `compile_grammar.sh`. Build a `PhpNewAst` (`Tyhp/TyhpLang/Ast/PhpNewAst.cs`) with a bool `IsLazy`. Anonymous class and dynamic `new` do not set it. `lazy new` of those forms fails to match `classNameReference` and remains a parse error. That is the intended diagnostic; do not add a MessageCode for it.

**Checker.** `BuildNewExpression`'s check path already typechecks `PhpNewAst` arguments (`Tyhp/TyhpLang/Checker/Rules/TypeCompatibilityRule.Dereferenceables.cs` and `TypeInferrer.Dereferenceables.cs`). `IsLazy` runs those same argument checks, then:

- **TYHP4402** (error) `CheckerLazyNewNotCapable`. The target is an internal class other than `\stdClass`, or it extends one, or it is not a class (struct, interface, enum, trait). Message: `` `lazy new` cannot construct `{0}` ``. `\stdClass` and a user class that extends only `\stdClass` are allowed.
- **TYHP4403** (warning) `CheckerLazyNewHasNoLazyProperty`. The class has no lazy-capable instance property (the injected generic bag of a tracked generic class counts as one), so PHP will return a normal instance without calling the initializer and the emitter uses `new`. Message: `` `{0}` has no lazy-capable instance property, so `lazy new` runs the constructor immediately ``.

`4400` and `4401` are taken (`CheckerReadonlyWithRequiresPhp83`, `CheckerReadonlyClassCloneWithRequiresPhp85`). `4381`–`4399` are reserved in `MessageCode.cs`. `4402` and `4403` are the next free checker codes. Add `ERROR_TYHP4402`, `WARNING_TYHP4403`, `EXPLAIN_TYHP4402`, and `EXPLAIN_TYHP4403` to **both** resx files. `tyhp explain` reads `EXPLAIN_TYHP{code}` through `MessageCodeCatalog.ResolveLongForm`. Explain text: ghosts initialize in place; internal classes and classes with no instance state cannot stay lazy; the warning means the expression is still valid and behaves like `new`.

Constructor mismatches reuse whatever code ordinary `new` already reports for the same bad call. Do not add a third code. Do not diagnose a method named `lazy`.

**Emitter, non-generic, floor ≥ 8.4.** In `BuildNewExpression` (`TyhpEmitter.Expressions.cs`), when `newExpr.IsLazy`:

Evaluate the arguments in order, in the caller's scope, before the ghost is created. Do not put the argument expressions inside the ghost closure, and do not assign temps inside a function the arguments cannot see the caller's locals from. The arguments are the arguments of an immediately-invoked wrapper that captures them in one variadic parameter, so one expression remains and positional, named, and spread arguments reach the constructor as written:

```php
(static function (mixed ...$__tyhpLazyArgs): object {
    return (new \ReflectionClass(\App\User::class))->newLazyGhost(
        static function (\App\User $__tyhp_obj) use ($__tyhpLazyArgs): void {
            $__tyhp_obj->__construct(...$__tyhpLazyArgs);
        }
    );
})(/* arg0 */, /* arg1 */)
```

With no arguments there is no wrapper. A class with no constructor gets an empty initializer. `lazy new static()` uses `static::class`.

**Emitter, Mechanism C, floor ≥ 8.4.** Do not change the existing factory in `EmitGenericFactoryIfNeeded`. It stays:

```php
$__tyhp_obj = self::$__reflectedClass__tyhpGeneric->newInstanceWithoutConstructor();
$__tyhp_obj->__initGenerics__tyhpGeneric($__generic_T);
$__tyhp_obj->__construct($id);
return $__tyhp_obj;
```

Add a second method, `GeneratedNames.GenericLazyFactory` → `new_<MangledFqn>__tyhpLazy`, only when the floor is ≥ 8.4 and the class already gets the eager factory. Reuse the same reflection cache (`self::$__reflectedClass__tyhpGeneric`, or the function static on a readonly class). Do not declare that field twice. The closure is created inside the factory, so the protected call is in class scope:

```php
final public static function new_App_Box__tyhpLazy(?\Tyhp\Type $__generic_T, mixed $item): self {
    self::$__reflectedClass__tyhpGeneric ??= new \ReflectionClass(self::class);
    return self::$__reflectedClass__tyhpGeneric->newLazyGhost(
        function (self $__tyhp_obj) use ($__generic_T, $item): void {
            $__tyhp_obj->__initGenerics__tyhpGeneric($__generic_T);
            $__tyhp_obj->__construct($item);
        }
    );
}
```

The readonly-class hook still starts with `__bootGenericBag__tyhpGeneric()` before any bag read. `lazy new` of that class calls `new_<Mangled>__tyhpLazy`, not `\Tyhp\Lazy::ghost`, and not the eager factory. Do not stamp this name into `#[\Tyhp\GenericRuntime]`'s `factory` (that stamp stays the eager factory).

**Emitter, floor < 8.4.** `IsLazy` is ignored for emit. Generic classes go through `TryBuildGenericFactoryCall`. Other classes go through `new Class(args)`. No lazy factory is emitted.

**Tests.** Next to the existing `new` tests.

| Test | File |
|------|------|
| `LazyNew_EmitsNewLazyGhost_WhenPhpVersionAtLeast84` | `tests/Tyhp.Tests/Emitter/LazyNewEmitterTests.cs` |
| `LazyNew_EmitsEagerNew_WhenPhpVersionBelow84` | same |
| `LazyNew_GenericClass_InitHookRunsBeforeConstructor` | same, assert `__initGenerics__tyhpGeneric` appears before `__construct` inside the closure, and the eager factory method is unchanged |
| `LazyNew_ReadonlyGeneric_BootsBagInsideHook` | same |
| `LazyNew_ArgumentsEvaluatedBeforeGhost` | same, a call with a side effect is outside the closure |
| `LazyNew_ConstructorArgumentMismatch_UsesTheSameCodeAsNew` | `tests/Tyhp.Tests/Checker/LazyNewCheckerTests.cs` |
| `LazyNew_InternalClass_ReportsTyhp4402` | same (`DateTime`, and a user class that `extends \DateTime`) |
| `LazyNew_StdClass_IsAllowed` | same |
| `LazyNew_NoInstanceProperty_WarnsTyhp4403` | same |
| `LazyNew_LazyNotAcceptedAsModifier` | `tests/Tyhp.Tests/Parser/LazyNewParseTests.cs` (`lazy class`, `lazy function`) |
| `MethodNamedLazy_EmitsUnchangedCall` | `tests/Tyhp.Tests/Emitter/LazyNewEmitterTests.cs` (`$q->lazy()` stays `->lazy(`) |
| `LazyNew_DynamicAndAnonymous_DoNotParse` | parser test |

**Guides.** Update, in the same slice:

- `Tyhp/TyhpLang/Emitter/technical-guide.md` (Mechanism C: the lazy factory beside the eager one; `BuildNewExpression`)
- `Tyhp/TyhpLang/Parser/technical-guide.md` (contextual `lazy`, only before `new`)
- `Tyhp/TyhpLang/Grammar/technical-guide.md` if it lists tokens
- `Tyhp/TyhpLang/Checker/technical-guide.md` (TYHP4402, TYHP4403)
- `Tyhp/TyhpLang/Ast/technical-guide.md` (`PhpNewAst.IsLazy`)
- `Tyhp/TyhpLang/Visitor/technical-guide.md` (the new visit labels)

The binder does not gain a new symbol. Skip `Binder/technical-guide.md` unless a sentence there describes `new` and would be false for `lazy new`.

**Changelog.** `tyhp/CHANGELOG.md` (syntax, TYHP4402, TYHP4403, emitted PHP).

**Out of scope here.** `lazy proxy new`. Proxies stay on `\Tyhp\Lazy::proxy`.

---

## Slice D — disposal

**Floor.** The check is `ReflectionClass::isUninitializedLazyObject`, which does not initialize the object and exists only on PHP ≥ 8.4. A build whose `output.phpVersion` is below 8.4 can still run on 8.4 or newer, where a ghost can exist (for example a Doctrine native lazy object), so the guard tests `\PHP_VERSION_ID >= 80400` first instead of relying on the build floor. The guard does not call `\Tyhp\Lazy::isLazy`: the `tyhp/core` 802 and 803 builds ship `isLazy` as a constant `false` and would not skip a ghost on 8.4, and calling it would add a `tyhp/core` requirement to disposal code.

**What the emitter does today.** `TyhpEmitter.Disposables.cs` does not call `DisposableHelper`.

- Nested `using` / `:=` compiles to try/finally. `EmitSyncDisposeGuard` emits `if ($x instanceof \Tyhp\Contracts\IsDisposable) { $x->dispose(); }`.
- Function-level `:=` compiles to `\Tyhp\DisposableScope::create()` and `->using(...)`. Disposal runs in `DisposableScope::disposeAll` (`tyhp-runtime-src/packages/tyhp/async/tyhp_src/DisposableScope.tyhp`), which calls `$disposable->dispose()` or `disposeAsync()`.
- `\Tyhp\DisposableHelper::dispose` is a separate async API in `tyhp-runtime-src/packages/tyhp/async/tyhp_src/DisposableHelper.tyhp`. It is not under `packages/tyhp/core/`. Callers who invoke it directly must get the same skip.

**Behavior.** On an uninitialized lazy object, skip `dispose()` and `disposeAsync()`. PHP does not run the destructor of a ghost that was never initialized; skipping `dispose` matches that. An initialized object, lazy or not, still disposes exactly as it does today. An ordinary object is not lazy, so `dispose()` still runs.

**Emit.** Change the guard to the form below. When `output.phpVersion` is 8.4 or newer the `\PHP_VERSION_ID` test is omitted. No package is required beyond the disposable contracts.

```php
if ($h instanceof \Tyhp\Contracts\IsDisposable && !(\PHP_VERSION_ID >= 80400 && (new \ReflectionClass($h))->isUninitializedLazyObject($h))) {
    $h->dispose();
}
```

**Runtime.** At the start of each iteration in `DisposableScope::disposeAll` and `DisposableHelper::dispose`, `continue` when a private `isUninitializedLazyObject` helper is true, before `disposeAsync` or `dispose`. The helper is the same source in every build: false below PHP 8.4, otherwise `\call_user_func([new \ReflectionClass($object), 'isUninitializedLazyObject'], $object) === true`. `call_user_func` is needed because every async build type-checks against the PHP 8.2 reflection signatures, which omit that method, and a `declare(php=">=8.4")` block is not compiled in the 802 and 803 builds.

**Files.**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.Disposables.cs`
- `Tyhp/TyhpLang/Emitter/technical-guide.md` (disposal paragraph)
- `tests/Tyhp.Tests/Emitter/LazyDisposeEmitterTests.cs` (or cases on `DisposableFinishEmitterTests.cs` / `NestedBlockDisposableEmitterTests.cs`)
- `tyhp-runtime-src/packages/tyhp/async/tyhp_src/DisposableScope.tyhp`
- `tyhp-runtime-src/packages/tyhp/async/tyhp_src/DisposableHelper.tyhp`
- `tyhp-runtime-src/packages/tyhp/async/tests/DisposableScopeTest.php`
- `tyhp-runtime-src/packages/tyhp/async/tests/DisposableHelperTest.php`
- `tyhp/CHANGELOG.md` (emitted `using` / `:=`)
- `tyhp-runtime-src/CHANGELOG.md` (`tyhp/async: …`)

**Tests.**

- `Using_BelowPhp84_GuardsReflectionCallBehindVersionId` and `Using_AtOrAbovePhp84_CallsReflectionWithoutVersionId` — the emitted guard text for each floor.
- `Using_OrdinaryObject_StillDisposes`, `Using_UninitializedGhost_IsNotInitializedOrDisposed`, `Using_InitializedGhost_StillDisposes` — executed PHP, including an 8.2-floor build on a newer PHP.
- `UsingAwait_UninitializedGhost_SyncFallbackSkips`, `FunctionScopeUsingEquals_UninitializedGhost_IsNotInitializedOrDisposed` — executed against the built `tyhp/core` and `tyhp/async`.
- `DisposableScope_SkipsUninitializedGhost` — PHPUnit, skip when `PHP_VERSION_ID < 80400`.
- `DisposableHelper_SkipsUninitializedGhost` — same.
- `DisposableScope_DisposesInitializedObject` — a ghost that was initialized, and a normal object, both get `dispose()`.

---

## Slice E — docs, AI dev guide, IDE grammar

Current contract only. No story numbers, no rhetoric internals, no `Query::lazy`.

**Docs** (`tyhp-docs-src`). There is no page for the `new` expression. `content/tyhp_1300_newObjectDeclSyntax.md` is constructor declarations (`: void`, `: parent(...)`), not instantiation. Add `content/tyhp_0152_lazyObjects.md` and list it in `content/toc.json` after `tyhp_0151_newFunctions.md`. Cover:

- `lazy new Name(args)` / `lazy new Name<T>(args)`: arguments run now, constructor runs on first use when `output.phpVersion` is ≥ 8.4, and the expression is an ordinary `new` below that.
- Ghosts keep identity. Generic classes stay bound because the compiler emits the init hook.
- `\Tyhp\Lazy` signatures from Slice B, including the eager fallback and what `reset` does on older PHP (runs the initializer, does not become lazy).
- `preset`, `isLazy`, `initialize`.
- `SKIP_INITIALIZATION_ON_SERIALIZE` versus `json_encode` (json still initializes).
- Disposal: an uninitialized lazy disposable is not disposed.
- One sentence for Doctrine users: call `enableNativeLazyObjects(true)` on the ORM configuration. The method is `enableNativeLazyObjects(bool $nativeLazyObjects): void` on the 3.7 tyhpdef. Do not describe Doctrine's proxy generator.

Also add a row to the glance table in `content/package_core.md`, a short pointer in `content/quickref.md`, and a sentence on `content/tyhp_2300_disposables.md`. Regenerate `content/diagnostics_reference.md` with `tyhp/scripts/sync-diagnostics-reference.sh` (`TYHP_DOCS_SRC` or the sibling checkout). Do not hand-edit that page. Changelog: `tyhp-docs-src/CHANGELOG.md`.

**AI dev guide** (`tyhp-ai-dev-guide`).

- New `guide/31-lazy-objects.md`.
- One line in `guide/00-index.md` and one line in `QUICK_GUIDE.md` under Behavior / expressions, with a `→` pointer.
- A short pointer in `guide/14-disposal.md`.
- The public signatures in `handbook/07-runtime-api.md`.
- If `REGEN.md` lists section files (`01`…`30`), extend that inventory. Do not link `CHANGELOG.md` from `SKILL.md`.
- Tell agents to write `lazy new` or `\Tyhp\Lazy`, not `ReflectionClass::newLazyGhost`. The reason to give them: a generic class binds `__initGenerics__tyhpGeneric` on `lazy new`; `\Tyhp\Lazy::ghost` does not bind type arguments, and a hand-written ghost closure skips the hook, so the constructor records null type arguments.
- Changelog: `tyhp-ai-dev-guide/CHANGELOG.md`.

**IDE** (`tyhp-ide-plugin`). The grammar is `vscode/syntaxes/tyhp.tmLanguage.json`, repository `keyword-tyhp`, which is included before `source.tyhp.php#new-expression`. PhpStorm copies that file in `phpstorm/build.gradle.kts` (`syncTextMateBundle`). There is no second grammar to edit.

Add a pattern next to `keyword.control.using.tyhp`:

```json
{
  "match": "\\b(?i:lazy)\\b(?=\\s+(?i:new)\\b)",
  "name": "keyword.control.lazy.tyhp"
}
```

The lookahead is what keeps `->lazy()` from being highlighted as a keyword. Scope: `keyword.control.lazy.tyhp`.

Changelogs: `vscode/CHANGELOG.md` and `phpstorm/CHANGELOG.md` under `## Unreleased`, and one line in the repo-root `CHANGELOG.md` because the grammar ships in both clients.

No examples-repo demo.

---

## Slice F — `Generic::bind()->ghost()`

**Floor.** Slice B has landed. Match `Lazy.tyhp` and `testEmittedLazyPhp_MatchesPhpVersionFloor`. `Lazy.tyhp` declares each method twice, with disjoint `#[\Tyhp\Php(">=8.4")]` and `#[\Tyhp\Php("<8.4")]`. `Generic` is a class, so the attribute is the tool, the same one `Lazy.tyhp` uses. `declare(php=…)` is for extensions. The 804 build contains the native call and no `\PHP_VERSION_ID`. The 802 build contains only the eager path, no native call, and no `\PHP_VERSION_ID`. 803 follows 802. 805 follows 804.

**Files.**

- `tyhp-runtime-src/packages/tyhp/core/tyhp_src/Generic.tyhp`
- `tyhp-runtime-src/packages/tyhp/core/tests/GenericTest.php` (already listed in `packages/tyhp/core/phpunit.xml`; do not add a test file and do not edit `phpunit.xml`)
- `tyhp-runtime-src/CHANGELOG.md`, one `tyhp/core:` line under `## [Unreleased]`, when this slice is implemented. Do not write that line in the plan.

No compiler change. `lazy new` stays Slice C. No `proxy` method on this binder.

**What `bind` already does.** `bind(mixed $target, ?\Tyhp\Type ...$typeArguments): mixed`. A class-string (no `::`, and either `class_exists` or not yet loaded) goes to `bindClass`, which returns `new self($className, $typeArguments, $info->erased, $info->factory)`. Any other target goes to `bindCallable` and returns the binder `\Closure` from `invokeMethodTarget` or `invokeFunctionTarget`. That closure does not gain `ghost`. Ghosts are instances.

`new(mixed ...$constructorArguments): object` and `__invoke(mixed ...$constructorArguments): object` both call `newWithArgs` and stay eager. Do not make ordinary dynamic construction lazy. `TTarget` is PHPDoc only. The Tyhp return type stays `object`.

**How `new` binds the hook.** `newWithArgs` does not call `__initGenerics__tyhpGeneric` itself. When the class is erased, or `$factory` is null or `''`, it uses `reflectClass($className)->newInstanceArgs($constructorArguments)` and does not touch the hook. Otherwise it loads the factory method stamped on `#[\Tyhp\GenericRuntime]` (`$info->factory`, `new_GenericBox__tyhpGeneric` on the `GenericTest` stand-in), pads `$this->typeArguments` with `padTypeArguments` against that method, and `invokeArgs(null, …)` with those type arguments followed by the constructor arguments. String keys on constructor arguments stay named. The factory is a method of the target class (`EmitGenericFactoryIfNeeded`). Its body allocates with `newInstanceWithoutConstructor()`, calls the protected hook with this class's own type arguments, calls `__construct`, and returns that object. Do not call the factory from `ghost`. That allocation is a second instance. The caller's variable is the ghost.

**API.** Same constructor arguments as `new`. No `$options`.

```tyhp
/**
 * @param mixed ...$constructorArguments Arguments forwarded to `TTarget::__construct`.
 *
 * @return TTarget
 *
 * @throws InvalidTypeException
 * @throws \LogicException
 */
#[\Tyhp\Php(">=8.4")]
public function ghost(mixed ...$constructorArguments): object;

#[\Tyhp\Php("<8.4")]
public function ghost(mixed ...$constructorArguments): object;
```

Add a `ghost(42)` line beside the existing `new(42)` example in the class docblock.

**`ghost` on `<8.4`.** `return $this->newWithArgs($constructorArguments);`. Same result as `new`, including the one-shot `$spent` guard, the erased branch, named constructor arguments, and the `\LogicException`. The object is not lazy.

**`ghost` on `>=8.4`.** Take the same `$spent` guard `newWithArgs` takes, and set `$spent` before returning, so `new`, `__invoke`, and `ghost` still share one construction. Then:

- Erased, or no factory: `reflectClass($this->className)->newLazyGhost(...)`. The initializer only calls `__construct` with `$constructorArguments`. No hook.
- Tracked: pad type arguments the way `newWithArgs` pads them, against the factory method. Do not `invoke` the factory. `newLazyGhost` an initializer that, on that same instance, calls `__initGenerics__tyhpGeneric` with the padded type arguments and then calls `__construct` with the constructor arguments. Bind that closure to `$this->className` (`\Closure::bind`) so the protected call is in the factory method's scope, which is how `new` reaches the hook. Return the object `newLazyGhost` returned.

Do not read a property on the ghost before returning. The first read is what runs the initializer.

`\Tyhp\Lazy::ghost` is not a substitute. Its initializer is a caller-written closure outside the class, so it cannot bind type arguments. This binder already holds the `\Tyhp\Type` values from `bind()`. `Lazy::ghost` reifies a compile-time `T` with `typeof(T)` instead.

**Tests.** Add cases to `GenericTest.php`. Use the existing `GenericBox` stand-in. Its constructor already records the runtime generic bag in `$typeAtConstruction`. That bag is what `typeof(T)` reads on a compiled generic. Do not add a second harness, and do not call `typeof` from this PHPUnit file.

- `testGhostOnPhp84DefersConstructionUntilPropertyRead` — `markTestSkipped('native lazy objects require PHP 8.4')` when `PHP_VERSION_ID < 80400`. `Generic::bind(GenericBox::class, Type::int())->ghost(42)` returns a `GenericBox` whose `constructorRan` is false, and `ReflectionClass::isUninitializedLazyObject` is true for it. After reading `$item`, `constructorRan` is true, `$item` is `42`, `typeAtConstruction` is `'int'`, and the instance is the same object.
- `testGhostBelowPhp84MatchesNew` — `markTestSkipped('the 8.4 build takes the native path')` when `PHP_VERSION_ID >= 80400`. `ghost(42)` has already run the constructor: `constructorRan`, `$item === 42`, `typeAtConstruction === 'int'`, the same observations as `testNewIsEquivalentToInvoking` and `testTypeArgumentsAreBoundBeforeTheConstructorRuns`.
- `testNewStaysEager` — on every PHP version, `new(42)` has `constructorRan === true` before any later read. `__invoke` stays on `newWithArgs`.
- `testGhostBindsTypeArgumentsBeforeTheConstructor` — on every PHP version, once the instance has initialized (a property read on 8.4, immediate below that), `typeAtConstruction` is `'int'`. A constructor that sees the bag only through the prologue's `needsInit()` path would record `'mixed'`.
- `testCallableBindHasNoGhost` — `Generic::bind((new GenericBoxWriteProbe())->write(...), Type::int())`, the callable shape `testBindCallableReturnsBinderClosureUnchangedForByRef` already uses, is a `\Closure`, not a `Generic`, and `method_exists($fn, 'ghost')` is false.
- `testEmittedGenericPhpMatchesThePhpVersionFloor` — same path shape as `LazyTest::readEmittedLazy` (`dist/tyhp-core/804.latest/src/Tyhp/Generic.php` and `802.latest`). 804 contains `newLazyGhost` and does not contain `\PHP_VERSION_ID`. 802 does not contain `newLazyGhost` or `\PHP_VERSION_ID`, and still contains the eager factory `invokeArgs` that `new` emits on every build.

---

## For the tyhp/rhetoric implementer

This chapter is the handoff. There will not be a second prompt. Re-read this file, especially [Status](#status) and [Slice B](#slice-b--tyhplazy-in-tyhpcore), before writing rhetoric lazy-loading code. If this file and your memory disagree, this file wins. If Status and the code disagree, trust the files named as landing evidence, then re-read the slice.

Rhetoric implementation is not work Story 48 will do. Do not expect slices A–E to edit `packages/tyhp/rhetoric/` or `IMPLEMENTATION_PLAN_TYHP_RHETORIC.md`.

### What Story 48 is building

The contract this story owns:

- `lazy new` in the compiler (Slice C).
- `\Tyhp\Lazy` in `tyhp/core`: `ghost`, `proxy`, `isLazy`, `initialize`, `reset`, `preset` (Slice B).
- Typed Reflection callbacks in `tyhpdef/php`: ghost initializer `callable(T): void`, proxy factory `callable(T): T` (Slice A).
- Disposal that skips an uninitialized lazy object (Slice D).
- Docs, the AI dev guide, and the `lazy` grammar (Slice E).

Rhetoric code is not part of that list.

### How it works

Ghosts initialize in place and keep identity. Proxies do not: `$proxy !== $real`. Entity references must be ghosts so `Tracker` identity (`spl_object_id`, `$byKey`) stays stable when the object initializes.

`\Tyhp\Lazy` is the supported API. `ReflectionClass::newLazyGhost(fn ($o) => $o->__construct(...))` does not call `__initGenerics__tyhpGeneric`. The constructor prologue then binds null type arguments. A rhetoric model that is a Tyhp generic must be created with `\Tyhp\Lazy::ghost` or with `lazy new`. Both bind the type arguments before the constructor. Raw reflection does not.

A generic model whose class or type arguments are chosen at runtime is constructed with `\Tyhp\Generic::bind(...)->ghost(...)`, not `\Tyhp\Lazy::ghost` and not `ReflectionClass::newLazyGhost`. Static `lazy new Class<T>(...)` remains the compile-time path (Slice C). Rhetoric still does not implement this; it only plans against it.

On PHP older than 8.4 the helper is eager: same end state, and the object is not lazy. `tyhp/rhetoric` publishes 803, 804, and 805 (`packages/build-common.sh`, `set_package_dist_builds`). Gate rhetoric's lazy feature with `#[\Tyhp\Php]` / `declare(php=…)`, the same way the helper is gated.

What that gate actually emits, because `output.phpVersion` is a floor and the 803 artifact requires `php: >=8.3`:

- The 804 and 805 rhetoric builds (floors 8.4 and 8.5) emit the lazy body with no `\PHP_VERSION_ID` check.
- The 803 build emits a `\PHP_VERSION_ID` branch. On PHP 8.3 the eager path runs (`with(fn)` / `$db->load` / `$db->loadMissing`, the matrix names for eager and explicit load). On PHP 8.4+ that same 803 binary takes the lazy path. Do not try to delete that branch. The compiler has no ceiling.

`lazy new` is different from the helper. It is a compile-time choice of the package floor (`IsPhpVersionAtLeast(8, 4)`), with no runtime branch. A `lazy new` compiled into the 803 artifact stays eager even if that artifact later runs on PHP 8.4. Prefer `\Tyhp\Lazy::ghost` so the 803 artifact still becomes lazy on PHP 8.4. `lazy new` is optional for rhetoric. The helper is the dependency.

`preset` writes a property without initializing (`ReflectionProperty::setRawValueWithoutLazyInitialization` on 8.4+). `reset` is `resetAsLazyGhost`. Pass `ReflectionClass::SKIP_DESTRUCTOR` when expiring a live instance so PHP does not run the destructor before the object becomes lazy again. `json_encode` and normal `serialize` initialize. `SKIP_INITIALIZATION_ON_SERIALIZE` opts out of serialize only. If the initializer throws, PHP leaves the object lazy.

`ReflectionProperty::getValue` initializes. `Tyhp\Rhetoric\Tracking\PropertyBag::read` calls `getValue` after `isInitialized` (`packages/tyhp/rhetoric/tyhp_src/Tracking/PropertyBag.tyhp`). Do not use `read` to look at a key that must stay lazy. Read it with `ReflectionProperty::getRawValue` (already on the overlay, `getRawValue(T $object): mixed`, `>=8.4`).

### What your plan already says

Phase 10, `IMPLEMENTATION_PLAN_TYHP_RHETORIC.md`:

> Opt-in lazy loading using PHP 8.4 lazy ghosts (gated with `declare(php=…)` / `#[\Tyhp\Php]`), plus a lazy collection class.

Comparison matrix, Lazy loading row:

> Rhetoric: opt-in `lazyLoading()`. Notes: PHP 8.4+ lazy ghost objects (`ReflectionClass::newLazyGhost`) for references; a lazy collection class for collections. Off by default.

That is the whole lazy-loading promise. It does not name `\Tyhp\Lazy`, generic binding, batching, `reference`, deferred columns, reset-in-place, a throwing strict mode, tracker skips, or a dead context.

### What to add to that plan

Add the following as explicit Phase 10 work, or as a named follow-on inside Phase 10. Mark it blocked on Story 48 Slice B (the helper) and Slice A (callable types). Slice C (`lazy new`) is optional. Keep implementing every other rhetoric phase. Only this lazy-loading implementation waits.

Do not copy the helper's signatures into the rhetoric plan. Link `tyhp/dev-docs/IMPLEMENTATION_PLAN_TODO_STORY_48.md`. If Status or the Slice B signatures change, follow this file.

- Call `\Tyhp\Lazy` once Status says Slice B has landed. Until then, do not invent a second helper and do not call `newLazyGhost` directly.
- Ghosts, not proxies, for references, so tracker identity stays stable. Register the ghost in the tracker before it initializes, so a later query for the same key fills that same instance.
- Batch the first initialization. The first ghost of a class in a context loads every pending ghost of that class with one `WHERE id IN (...)` query.
- `reference($id)`. Nothing in the rhetoric sources is named `reference` today (`alignForeignKey` is a private method on `Tracker`). Use `reference`. It returns a ghost with only the key preset, and it does not query. `alignForeignKey` must read that key without initializing the ghost (`getRawValue`, not `PropertyBag::read`).
- Deferred columns. An attribute on large columns. Hydration presets the columns it loaded. The ghost fetches the rest on first access.
- Refresh and expire in place with `Lazy::reset` / `resetAsLazyGhost` and `SKIP_DESTRUCTOR`, instead of hydrating a second instance and copying columns.
- Opt-in strict mode, the `preventLazyLoading()` equivalent. Its initializer throws. PHP then leaves the object lazy.
- A collection class for has-many / belongs-to-many. PHP arrays cannot be lazy.
- Tracker changes are mandatory. `detectChanges`, `snapshot`, `compare`, `adoptReferences`, and `readMembers` must not initialize an untouched ghost. An untouched ghost is not dirty. Take the snapshot when the ghost finishes initializing.
- Initializers close over `Context`. Touching a ghost after that scoped context is gone (Octane workers) throws a clear error. It does not query a dead connection.
- `Query::lazy()` is unrelated. It is the generator at `Query::lazy(int $count = 1000)`. Do not introduce a `lazy` keyword anywhere except `lazy new`, and only after Slice C has landed. The method name `lazy()` stays.

### How to update the rhetoric plan now

While Story 48 is Not started:

1. Revise the Phase 10 lazy-loading bullet and the matrix row so they point at this plan for the mechanism.
2. Add the list above as Phase 10 work (or a clearly named follow-on in that phase), blocked on Slice B and Slice A.
3. Leave every other phase executable.
4. Do not paste `\Tyhp\Lazy`'s API into that plan.

### How to check progress later

1. Re-read the Status section of this file.
2. When Slice B is `Landed`, implement rhetoric lazy loading against the signatures in the Slice B section. Re-read them. They win over memory.
3. If Status and the code disagree, trust the landing-evidence paths, then re-read the slice.

---

## Rejected alternatives

- A reserved `lazy` keyword, reclassified everywhere the way `with` is. That would break `Query::lazy()`.
- A `\PHP_VERSION_ID` branch inside `lazy new`. Expression emit (`IsPhpVersionAtLeast`) picks one shape. The helper's declaration split is what branches, and only because the 802/803 packages have no PHP ceiling.
- Emitting only the eager path into the 802/803 `tyhp/core` binaries, with no version check. Those binaries are published as `php: >=8.2` / `>=8.3` and do run on 8.4. An eager-only body would construct a normal instance on PHP 8.4.
- Changing the existing Mechanism C factory so every `new Box<int>` becomes a ghost.
- Replacing `Type::$lazyInner`.
- `lazy proxy new`.
- A rhetoric-local wrapper around `newLazyGhost`.

---

## Non-goals

Story 48 does not implement, design in code, or edit:

- Rhetoric lazy references
- Batch lazy loads (`WHERE id IN (...)`)
- `reference($id)`
- Deferred columns
- Tracker ghost-skipping (`detectChanges`, `snapshot`, `compare`, `adoptReferences`, `readMembers`)
- Lazy collections
- Refresh-in-place of rhetoric models

Those belong to the rhetoric agent, after Slice B has landed, using the chapter above. This story also does not add `lazy proxy new`, does not change `Query::lazy()`, and does not edit the Doctrine ORM tyhpdef.
