# Implementation Plan: Story 49 — Calling Tyhp Packages from PHP

> **Roadmap position:** Story **49**, sequenced in **Tier 3** immediately after Story 48 and immediately before Story 22.2. The number is 49 because that is the next unused plan number after 48. Roadmap order, not the story number, decides when the work happens. This story does not depend on 48 or 22.2 and does not gate them.
> **Direct dependencies:** 08 (checker), 11 (emitter, Mechanism C and Mechanism D), 21.12 (generic full-erase, `\Tyhp\Generic::bind`), 22.1 (emitted PHPDoc), 27.3 (split repos). Runtime packages: `tyhp/core`, `tyhp/async`, `tyhp/lambda`, and a re-emit of every `tyhp/*` package. Slice H (PHP package docs) also waits on Phase 14 of `tyhp-runtime-src/dev-docs/IMPLEMENTATION_PLAN_TYHP_RHETORIC.md`.
> **New story.** No legacy number. Absorbs the former `tyhp-runtime-src/dev-docs/IMPLEMENTATION_PLAN_TYHP_PHP_DOCS.md` (now Slice H).
> **Conventions:** Diagnostic codes live in `Tyhp/Domain/Exceptions/MessageCode.cs`. New user-facing strings go in both `Resources/CLI.TyhpHostedService.resx` and `Resources/CLI.TyhpHostedService.en-US.resx`.
> **Generated:** 2026-11-05
> **Branch:** TBD

Implementers of this story update the Status table as slices land. The agent that owns `tyhp/rhetoric` trusts that table (see [For the tyhp/rhetoric implementer](#for-the-tyhprhetoric-implementer)).

---

## Status

| Slice | State | Landing evidence |
|-------|--------|------------------|
| Overall | In progress | Slices A–G landed. Slice H is not started. `tyhp/rhetoric` was not re-emitted: its vendor directory is missing, and `composer update` is still refused by Symfony security advisories. |
| A — `Type` ergonomics in `tyhp/core` | Landed | `BuiltIn.tyhp`, `TypeLike.tyhp`, `Type.tyhp`, `Generic.tyhp`; `tests/BuiltInTest.php` (`testConstantsAreBuiltInCases`, `testConstantWorksAsDefaultAndAttributeArgument`, `testBuiltInNullableAndOrReturnType`); `tests/TypeTest.php` (`testTypeArgNormalizesTypeLikeAndClassStrings`, `testNullableStaysMagicAndOptionalHasBothForms`, `testStructFieldMapRequiresKeysUnlessOptional`, `testListAndMapFactories`, `testGenericArgumentsByPositionNameAndNamedType`, `testGenericRejectsBadArguments`, `testGenericSpellingsAreTheSameType`). `nullable()` is not a TypeLike method; `Type::nullable($type)` stays the static factory. |
| B — binder-first generic methods and functions | Landed | `TyhpEmitter.GenericVariants.cs`, `TyhpEmitter.Helpers.cs` (erased callables stay `layouts: [1]`; non-erased callables stamp `layouts: [2]`), `DeclarationRule.Callable.cs`, `Generic.tyhp`, `ArrayAccessShape.tyhp`. `ArrayAccessShape::offsetGet` and `offsetSet` no longer declare `TKey`; the key is `__IndexKeys<TStruct>` and the value is `__IndexValueTypes<TStruct>`. Tests: `GenericVariantEmitterTests`, `BinderFirstGenericCheckerTests`, `GenericEraseEmitterTests`, `RuntimeGateEmitterTests`, `JsonDecodeEmitterTests`, `InstanceofGenericParameterEmitterTests`, `Library_GenericMethodAboveFloor_BothSiblingsStampTheSameGenericRuntime` (130 passed), `GenericTest::testBindLayout1CallableAcceptsTypeBuiltInAndClassString`, `GenericTest::testBindLayout2CallableAcceptsTypeBuiltInAndClassString`, `PropertyAccessorTest` (31 passed). Re-emitted: core, decimal, async, lambda, parallel, io. |
| C — `new()` and `type()` on generic classes | Landed | `TyhpEmitter.GenericClasses.cs`, `TyhpEmitter.Declarations.cs`, `TyhpEmitter.Docblocks.cs`, `DeclarationRule.ObjectType.cs`, `MessageCode.cs` (TYHP4406). `new()` matches the constructor's visibility and is not `final`; `type()` is public and not `final`. A generic child with at least as many type parameters redeclares both (`Child::new($A = null, $B = null)` over `Box::new($T = null)`). A generic child with fewer type parameters redeclares both with its own names plus a trailing `mixed ...$__tyhpInherited` (PHP rejects an override that drops a parameter); `Type::generic` ignores that variadic when it reads names (`Type.tyhp`, core re-emitted). A public `new()` on a non-final class refuses to build a subclass whose constructor is not public. A non-generic `IntBox extends Box<int>` inherits `Box::new` / `Box::type`; the closure reflects `static::class`, so `IntBox::new()(args)` builds an `IntBox`. A child constructor narrower than the inherited `new()` does not redeclare it (`Emit_NarrowerConstructor_InheritsNew`); the constructor stays as written, and calling the inherited public `new()` still fails at that private constructor. Mechanism C helpers stay on `GenericFactoryModifiers`. Tests: `GenericClassBinderEmitterTests` and `GenericClassReservedMemberCheckerTests` (22 with `Emit_GenericFactory_MatchesConstructorVisibility`, all passed), including `Emit_ChildWithFewerParameters_RedeclaresWithInheritedTail`, `Emit_PublicNew_GuardsSubclassConstructorUnlessFinal`, `Run_InheritedNew_NeverBuildsAClassWithNonPublicConstructor`, `Emit_ConcreteGeneric_EmitsPublicNewAndType`, `Emit_AbstractGeneric_EmitsTypeOnly`, `Emit_TwoParameterGeneric_NamesBothParameters`, `Emit_NewVisibility_MatchesConstructorAndIsNotFinal`, `Emit_GenericChild_RedeclaresNewAndTypeWithItsOwnParameters`, `Emit_InterfaceAndTrait_GetNeither`, `Run_NewTypeAndIntBox_ConstructAndCompareTypes`, `GenericClassDeclaringType_ReportsTyhp4406`, `GenericClassWhoseParentDeclaresNew_ReportsTyhp4406`. |
| D — `#[\Tyhp\InferGeneric]` | Landed | `InferGeneric.tyhp`, `InferGenericAttributeSupport.cs`, `TyhpEmitter.GenericVariants.cs`, `DeclarationRule.Callable.cs`, TYHP4407, TYHP4408. Tests: `InferGeneric_TypeParameterOnlyInReturn_ReportsTyhp4407`, `InferGeneric_OnNonGenericMethod_WarnsTyhp4408`, `Emit_InferGeneric_EmitsValueSignatureAndLayout1Companion`, `Emit_ErasedInferGeneric_StaysPlain`, `Emit_InferGenericCall_BareUsesPlain_ExplicitUsesCompanion`, `Run_InferGeneric_BindsCompanionAndInfersPromiseArrays` (includes a `Promise<T> ...` variadic), `InferGeneric_TypeParameterReachableFromAnArgument_DoesNotReportTyhp4407` (callable return, class-string, `T`, `?T`, `Promise<T>`, `?Promise<T>`, `array<Promise<T>>`, `Promise<T> ...`), `InferGeneric_TypeParameterOnlyInAByRefParameter_ReportsTyhp4407`, `InferGeneric_OnNonGenericFunction_WarnsAndNeverErrors` (`GenericVariantEmitterTests`, `BinderFirstGenericCheckerTests`, and `Library_GenericMethodAboveFloor_BothSiblingsStampTheSameGenericRuntime`: 93 passed). A by-reference parameter is not an inference source, so it neither satisfies TYHP4407 nor feeds the companion. Re-emitted core 802–805 and lambda 802–805. Rhetoric source marked; vendor missing, not re-emitted. InferGeneric: `Lazy::proxy` and `Lazy::reset` (both PHP siblings), `PropertyAccessorObject::register`, `Expression::of`, `PropertyPath::on`, `PropertyPathBuilder::of`, `Context::entry`, `Tracker::entry`. Binder-first: `Lazy::ghost` (`callable(T): void`), `Expression::lambda` and `PropertyPath::from` (the type parameter is the callable shape), `PropertyPathBuilder::backedBy`, `Expr::backedBy`, `ExpressionBuilder::fromComparison`, `ExpressionBuilder::continueFrom`, `Json::decode`, `Json::tryDecode` (its type parameter appears only in `T &$out`), `Type::isType`, `StringExtensions::jsonDecodeAs` and `jsonTryDecodeAs`. Slice E/F async, parallel, and io generics, and the other public rhetoric generics, stay binders. |
| E — `tyhp/async` for PHP callers | Landed | `packages/tyhp/async/tyhp_src/Functions/async.tyhp`, `Functions/await.tyhp`, `Promise.tyhp` (`wait`, `driveAwait`), `EventLoop.tyhp` (`driverFiber`, `isLoopFiber`, `ensureNotReentrant`), `RootFunctions.tyhp` (async and core `_gnrc`). `#[\Tyhp\InferGeneric]`: `Tyhp\async`, `Tyhp\await`, `_async`, `_await`, `_asyncMain`, `Promise::_async`, `_await`, `then`, `resolve`, `resolved`, `run`, `all`, `whenAll`, `race`, `whenAny`, `any`, `timeout`. `Promise::resolve` is marked through the `null|T|Promise<T>` source (`TryGetValueOrPromiseArms`, `VariantInferenceKind.ValueOrPromise`: a promise contributes its result type, another non-null value its own type, null nothing; a union with any other arm stays TYHP4407). `Promise::run` is marked because `callable(): T` is a source. Left binders because TYHP4407 would fire: `allSettled` (`TKey` is not reachable from `array<TKey, Promise<T>>`) and `batch` (`TItem` sits in `array<TItem>`, which is not a source, and `TBatchReturn` sits in the callable's `Promise<TBatchReturn>` result, which is not a sole callable-return source). `reject`, `rejected`, and `withResolvers` have `T` only in the result and stay binder-first (`Promise::reject()($e)`, `Promise::withResolvers(Type::int)()`). `Type::or` returns the type itself when both sides are the same (`Type.tyhp`), so plain calls whose arguments share a result type infer it. `function async` inside `namespace Tyhp` already parsed; no lexer change. An async InferGeneric method stamps its settled return type with `_async__tyhpGeneric` (`TyhpEmitter.Declarations.cs`, `AsyncInferGenericReturnNeedsVariant`). Generic init freezes each type argument with `->immutable()` before a nullable property type is registered (`TyhpEmitter.GenericClasses.cs`). Tests: `AsyncAwaitFunctionsTest` (`testAwaitAsyncFromSynchronousPhpReturnsTheValueAndRunsQueuedWork`, `testAwaitInsideAsyncSuspendsAndResumes`, `testConcurrentAwaitsInterleave`, `testAwaitTimeoutFromSynchronousPhp`, `testAwaitTimeoutInsideAFiber`, `testCancelledTokenFromSynchronousPhp`, `testCancelledTokenInsideAFiber`, `testTokenCancelledWhileTheLoopRuns`, `testAwaitFromALoopCallbackOutsideAFiberThrows`, `testAwaitFromAUserFiberRunsTheLoop`, `testPlainCallsInferPromiseResultTypes`, `testPlainCallsWithSameResultTypesInferThatType`, `testPlainResolveInfersFromAValueOrAPromise`), `PromiseTest` (`testWait`, `testWaitTimeout`, `testWaitNegativeTimeoutHasNoDeadline`), `TypeTest::testOrOfTheSameTypeIsThatType`, and in the compiler tests `Emit_InferGeneric_ValueOrPromiseUnion_ReadsEachArmOnce`, `Run_InferGeneric_InfersFromValueOrPromiseUnion`, `InferGeneric_UnionWithAnUnrelatedArm_ReportsTyhp4407`, `InferGeneric_ArrayKeyAndArrayItemAreNotSources_ReportTyhp4407`. The full async suite (162 tests) passes on the 802, 803, 804, and 805 builds under PHP 8.5.12; the core suite passes (164 tests, 2 skipped). The existing async tests call `reject`, `withResolvers`, `allSettled`, and `batch` in their binder-first form. Re-emitted async and core 802–805. |
| F — `tyhp/lambda` root functions | Landed | `packages/tyhp/lambda/tyhp_src/RootFunctions.tyhp` (`fallback function _expr`, `fallback function _path`). Both are `#[\Tyhp\InferGeneric]`, with `@template T of object` and `@return \Tyhp\PropertyPathBuilder<T>&T`. Docblocks name `_expr` and `_path` and say each is skipped when a global function of that name already exists. Bodies forward the type parameter (`\Tyhp\Expression::of<T>(...)`, `\Tyhp\PropertyPath::on<T>(...)`). `Expression::of` and `PropertyPath::on` are not erased (`erased: false`, `of__tyhpGeneric` and `on__tyhpGeneric` companions, `layouts: [1]`), so a body that dropped `<T>` would stamp `_expr` and `_path` erased and a Tyhp call with explicit type arguments could not reach the companion. Each root function now stamps `erased: false`, `binder: "_expr__tyhpGeneric"` / `"_path__tyhpGeneric"`, `layouts: [1]`, as the Slice E root functions do; the plain `_expr(...)` calls `_expr__tyhpGeneric(null)(...)`. `packages/tyhp/lambda/composer.json` `autoload.files` is `src/_functions.php`, and `packages/build-common.sh` lists that same path on the published lambda package. `tests/bootstrap.php` loads `src/_functions.php`. Tests: `RootFunctionsTest` (`testExprMatchesExpressionOf`, `testExprWithSegmentsMatchesExpressionOf`, `testPathMatchesPropertyPathOn`, `testExprCompanionBindsTheSameWayAsExpressionOfCompanion`, `testPathCompanionBindsTheSameWayAsPropertyPathOnCompanion`). The lambda suite is 112 tests, 387 assertions, all passed on PHP 8.5.12 against the 802 build. Re-emitted lambda 802–805. `Expression::lambda`, `PropertyPath::from`, and `backedBy` stay binder-first. |
| G — Tyhp docs, AI dev guide, emitter guide | Landed | `Tyhp/TyhpLang/Emitter/technical-guide.md` (§5 Mechanism C `new()` / `type()`, §5 Mechanism D layout 2 / `#[\Tyhp\InferGeneric]` / layout 1). `tyhp-docs-src/content/`: `tyhp_0500_generics.md`, `package_core.md`, `package_async.md`, `package_lambda.md`, `cli_interopContract.md`, `quickref.md`, `tyhp_2100_extensions.md`, `tyhp_2500_phpMagicMethods.md`, `tyhp_2700_compileTimeConstructs.md`, `tyhp_0152_lazyObjects.md`, `faq_general.md`, `faq_tyhpSyntax.md`, `tyhpdef_extensions.md`, `cli_build.md`, `cli_tyhpdefGeneration.md`, `diagnostics_reference.md` (regenerated; TYHP4404–TYHP4408, and TYHP4409–TYHP4411 left in place). `tyhp-ai-dev-guide/`: `guide/07-generics.md`, `guide/29-php-mapping.md`, `guide/25-runtime-packages.md`, `handbook/05-php-interop.md`, `handbook/07-runtime-api.md`, `QUICK_GUIDE.md`, `SKILL.md`, `REGEN.md`, and, after review, `guide/12-extensions.md`, `guide/23-tyhpdef.md`, `guide/26-build-cli.md`, `handbook/03-build-cli-workflow.md` (foreign layout-2 call is `name(types)(args)`). Review also corrected `package_async.md` (`await` has no companion; `Promise::wait` suspends in a loop fiber) and `handbook/07-runtime-api.md` (`wait` signature). |
| H — PHP package docs | Landed | `tyhp-docs-src/content/php_packages.md`, `php_package_core.md`, `php_package_async.md`, `php_package_decimal.md`, `php_package_lambda.md`, `php_package_io.md`, `php_package_parallel.md`, `php_package_compiler.md`. `content/toc.json` section **Packages for PHP** lists those eight files. The index links `rhetoric_php_getting_started.md`. `php generate_docs.php` succeeded. |

When a slice lands, change its state to `Landed` and replace the evidence cell with the files and test names a reader can open to see the behavior. `In progress` is for a slice that has a partial diff and is not yet the contract above.

---

## Table of Contents

- [Summary](#summary)
- [Decided](#decided)
- [Verify while implementing](#verify-while-implementing)
- [Diagnostics](#diagnostics)
- [Slice A — `Type` ergonomics in tyhp/core](#slice-a--type-ergonomics-in-tyhpcore)
- [Slice B — binder-first generic methods and functions](#slice-b--binder-first-generic-methods-and-functions)
- [Slice C — `new()` and `type()` on generic classes](#slice-c--new-and-type-on-generic-classes)
- [Slice D — `#[\Tyhp\InferGeneric]`](#slice-d--tyhpinfergeneric)
- [Slice E — tyhp/async for PHP callers](#slice-e--tyhpasync-for-php-callers)
- [Slice F — tyhp/lambda root functions](#slice-f--tyhplambda-root-functions)
- [Slice G — Tyhp docs, AI dev guide, emitter guide](#slice-g--tyhp-docs-ai-dev-guide-emitter-guide)
- [Slice H — PHP package docs](#slice-h--php-package-docs)
- [For the tyhp/rhetoric implementer](#for-the-tyhprhetoric-implementer)
- [Rejected alternatives](#rejected-alternatives)
- [Non-goals](#non-goals)

Ship A first: B, C, and D emit calls to `Type::arg` and type parameters as `\Tyhp\TypeLike|string`. B before C and D, because both reuse B's binder emit and stamp. D before E, because E marks the async API with the attribute. F only needs D (for `_expr` / `_path`). G after A–F, once signatures and diagnostics are frozen. H last, after G and after rhetoric Phase 14.

---

## Summary

A PHP developer who calls a `tyhp/*` package today has to know Tyhp internals: `__tyhpGeneric` binders, `\Tyhp\Generic::bind`, `new \Tyhp\NamedType(...)`, `\Tyhp\Type::int()`, `_asyncMain`, and `Expression::of`. This story makes the emitted PHP read like an ordinary PHP library, then documents it for PHP developers.

```php
use Tyhp\Type as T;
use function Tyhp\async;
use function Tyhp\await;

$user = \Tyhp\Json::decode(T::struct(['id' => T::int, 'name' => T::string]))($json);
$box = \Vendor\Box::new(T::int)(42);
$boxType = \Vendor\Box::type(T::int);
$value = await(async(fn (): int => 42));
$adults = \_expr(\App\User::class)->age->gte(18);
```

Five changes:

1. **Binder-first generics** (Slice B). A generic method or function that needs its type arguments at runtime is emitted under its own name as the binder: `Json::decode($T)($json)`. The `__tyhpGeneric` second method goes away for these declarations. Erased generics stay plain methods.
2. **Generic classes** (Slice C) get a generated `Box::new(T)(...)` binder and a `Box::type(T)` type builder.
3. **`#[\Tyhp\InferGeneric]`** (Slice D) keeps a generic a plain PHP call when every type parameter comes from its arguments. The async API (`then`, `all`, `race`, …) uses it.
4. **`Type` ergonomics** (Slice A). `use Tyhp\Type as T`, constants such as `T::int` backed by a `\Tyhp\BuiltIn` enum, a `\Tyhp\TypeLike` interface, `T::list` / `T::map` / `T::struct` / `T::optional`, and `T::generic` with positional or named type arguments.
5. **Async and expressions for PHP** (Slices E and F). Namespaced `Tyhp\async()` and `Tyhp\await()`, and root functions `_expr()` and `_path()`.

Slices G and H document the result: G for Tyhp authors (new diagnostics, the emitted shape), H for PHP developers (a **Packages for PHP** section on tyhplang.com).

The generic runtime contract is versioned. Callables compiled by this story are stamped `layouts: [2]`. Layout 1 stays supported by the compiler and by `Generic::bind`, so packages compiled before this story keep working.

---

## Decided

### Binder-first generics

1. **Shape.** A non-erased generic method or function whose type parameters are not all inferable (see `#[\Tyhp\InferGeneric]`) is emitted as one PHP method under its declared name. That method is the binder: it takes only type arguments and returns a `\Closure` with the author's value signature. `Json::decode($T)($json)`. No `decode__tyhpGeneric` is emitted.
2. **Binder parameters.** One parameter per declared type parameter, named after it (`$T`, `$TKey`), not `$__generic_T`. Each is typed `\Tyhp\TypeLike|string|null` and defaults to `null`. The binder normalizes each argument with `\Tyhp\Type::arg(...)`.
3. **Stamp.** `#[\Tyhp\GenericRuntime(erased: false, binder: "decode", layouts: [2], compiler: "…")]`. The `binder` names the declared method itself.
4. **Inference.** Method-level type parameters only. Class-level type parameters are never inferred. When a binder argument is `null`, the closure infers that parameter on **every call**, from:
   - a `callable(...): T` argument's declared return type (`Type::fromCallableReturn`);
   - a `class-string<T>` / `__ClassName<T>` argument (`Type::fromClassName`);
   - a `T $value` argument (`Type::of($value)`), skipping `null`, and the union of the results when several arguments use `T`.
   If none applies, the declared default is used. Otherwise the parameter stays unbound, as an unbound type argument is today.
5. **No misuse guard.** A PHP caller who passes value arguments to the binder (`Json::decode($json)`) gets PHP's own `TypeError`, or a `\Tyhp\Exceptions\InvalidTypeException` from `Type::arg`. The binder does not count extra arguments.
6. **PHPDoc.** The binder's docblock starts with the two-step call, then the author's docblock, then `@template`, `@param \Tyhp\Type<T>|\Tyhp\BuiltIn|class-string<T>|null $T`, and `@return \Closure(<value signature>): <return>`. It builds on Story 22.1's docblock emit.
7. **Erased generics stay plain.** `#[\Tyhp\GenericRuntime(erased: true, …)]` methods are unchanged. Docs say which methods take `()` first.
8. **Forbidden generics.** The checker rejects type parameters on magic methods (including `__construct`), and on a method that overrides or implements a non-generic parent method or a non-generic PHP interface method. The binder would change the PHP signature PHP dispatches on. See [Diagnostics](#diagnostics).
9. **`Generic::bind` and `_gnrc` stay.** They are the way to bind a layout-1 library, a class or callable chosen at runtime, and an `#[\Tyhp\InferGeneric]` method with explicit types. `Generic::bind` accepts layouts 1 and 2 and takes `\Tyhp\TypeLike|string` type arguments.
10. **Unbound-closure cache.** Allowed, not required: a static binder may cache the closure it returns per distinct argument tuple. Do not cache instance binders (they capture `$this`).

### `#[\Tyhp\InferGeneric]`

11. **Meaning.** On a generic method or function, every type parameter comes from the arguments. The compiler emits the plain method (today's wrapper) and keeps the `name__tyhpGeneric` binder companion, stamped `layouts: [1]`. PHP callers call it plainly. PHP callers who want explicit types use `Generic::bind`. Tyhp call sites with explicit type arguments call the companion.
12. **Compile-time check.** A type parameter that none of the inference sources can reach is an error. The attribute on a non-generic declaration is a warning.
13. **Inference sources.** The three in item 4, plus:
    - a `Promise<T>` argument: its own generic type (`Type::of` already returns a promise's generic object type);
    - an `array<Promise<T>>` or `Promise<T> ...` argument: the union of the promises' result types (new).
14. **Applied to:** `_async`, `_await`, `_asyncMain`, `Promise::then`, `resolve`, `resolved`, `all`, `allSettled`, `race`, `any`, `whenAll`, `whenAny`, `timeout`, `batch`, and the new `Tyhp\async` / `Tyhp\await` (Slice E), and `_expr` / `_path` (Slice F). Slice D also audits every other non-erased public generic in `tyhp/*` (see that slice).
15. **`Promise::withResolvers()` stays binder-first.** Nothing to infer from. Docs prefer `new \Tyhp\Deferred()` or `\Tyhp\Deferred::new(T::int)()`.

### Generic classes

16. **`Box::new(T...)(ctorArgs)`.** A generated static binder on every concrete generic class, with the same visibility as the constructor. A private constructor produces a private `::new()`. A protected constructor produces a protected `::new()`. A public constructor produces a public `::new()`. Parameters are the class's type parameters, named and typed as in item 2. It returns a `\Closure` with the constructor's value signature that constructs through the existing Mechanism C factory. `new Box(42)` still works and uses the declared defaults. `FOUND_BUGS.md` #19 (Ready) is this visibility hole on the existing Mechanism C helper: `new_<Mangled>__tyhpGeneric` is public for `EloquentQuery`'s private constructor. This story does not fix that helper by editing rhetoric. When emitting `::new()`, use the constructor's visibility; do not emit a public `::new()` for a private or protected constructor.
17. **`Box::type(T...)`.** A generated public static method returning `\Tyhp\Type` for `Box<T…>`. Same parameters. An omitted argument is the declared default, or unbound when there is none. PHPDoc `@return \Tyhp\Type<Box<T>>`. Nests: `Map::type(T::string, Box::type(T::int))`.
18. **Where they are generated.** `new()` and `type()` on concrete classes; `type()` only on abstract classes; neither on interfaces or traits.
19. **Reserved names.** A generic class, or any of its ancestors, that declares a method named `new` or `type` (case-insensitive) is a checker error.
20. **Class stamp.** The Mechanism C factory contract (`factory: "new_<Mangled>__tyhpGeneric"`) does not change. `type()` is a public member. `new()` uses the constructor's visibility from item 16 (`FOUND_BUGS.md` #19).

### `Type` ergonomics

21. **Import alias.** Docs use `use Tyhp\Type as T;`.
22. **Constants.** `Type` declares one class constant per builtin, each pointing at a case of a new enum `\Tyhp\BuiltIn`: `T::string`, `T::int`, `T::float`, `T::bool`, `T::null`, `T::void`, `T::mixed`, `T::never`, `T::array`, `T::object`, `T::callable`, `T::iterable`, `T::resource` (the names `Type::__callStatic` builds today). They work in attribute arguments and parameter defaults. `T::int()` keeps working and still returns `Type`. Docs teach the constant.
23. **`\Tyhp\TypeLike`.** An interface implemented by `Type` and `BuiltIn`. Every runtime API that takes a type declares `\Tyhp\TypeLike|string`. A string is a class-string. A type spelling such as `'array<int>'` goes through `Type::parse(...)` explicitly.
24. **`Type::arg(TypeLike|string|null $type): ?Type`.** `null` stays `null`. A `Type` passes through. A `BuiltIn` case becomes its `Type`. A string naming a loaded class, interface, or enum becomes `Type::fromClassName`. Any other string throws `\Tyhp\Exceptions\InvalidTypeException` that tells the caller to use `Type::parse`.
25. **Combined types.** Fluent `->nullable()` and `->or(TypeLike|string ...$others)` on both `Type` and `BuiltIn`. Static `T::list($value)`, `T::map($key, $value)`, `T::struct([...])`, `T::optional($type)`, and `T::generic($class, ...$args)`.
26. **`T::struct`.** An array first argument is the field map. Every field is required unless wrapped in `T::optional(...)`. The name is optional (`T::struct(name: 'App\UserData', fields: [...])`). The existing three-argument form keeps its meaning.
27. **`T::generic(string $generic, TypeLike|string|NamedType ...$args)`.** The existing `Type::generic` is widened, not replaced. Arguments are positional, named, or mixed (named after positional). `NamedType` arguments keep working.
    - Names come from the layout-2 `Box::new` binder's parameter names, or from a layout-1 factory's `$__generic_*` parameters. A plain PHP class has no names: positional only.
    - The runtime rejects an unknown name, a wrong count, and a named argument that fills a slot already filled by position.
    - `array` and `iterable` follow `Type::parse`: one argument is `TValue`, two are `TKey`, `TValue`. Docs use `T::list` / `T::map` for them.
    - PHPDoc: `@template TClass of object`, `@param class-string<TClass> $generic`, `@return Type<TClass>`.
28. **By name and by position are equal.** `Type::is`, `Type::compatible`, and the generic type cache treat `Box<int>` built by position, by name, with `NamedType`, or by `Type::parse` as the same type.
29. **PHPStan / Psalm.** A phantom `@template T` on `Type`; `@method static Type<int> int()` and the other factories; a conditional `@return` on `Type::arg` mapping each `BuiltIn` case to `Type<int>`, `Type<string>`, and so on.
30. **Structs from PHP** are built with `T::struct([...])`. No marker classes. `Type::register` for public structs is optional and not required by any slice.
31. **`Type::of(mixed $value)` is unchanged.** It is the value-to-type function the compiler emits for inference.

### Async

32. **`Tyhp\async(callable $fn): Promise`** and **`Tyhp\await(Promise $promise, ?int $timeoutMs = null, ?CancellationToken $token = null): mixed`**, namespaced functions in `tyhp/async`, both `#[\Tyhp\InferGeneric]`.
    - Inside a loop fiber, `await` suspends. A timeout goes through `Promise::timeout`.
    - From synchronous PHP, `await` runs the loop the way `EventLoop::run` does (until no immediate work is left), and honours the timeout and the token.
    - Inside the running loop but outside a fiber, `await` throws the re-entrancy `\LogicException` `EventLoop::run` throws.
33. **`$promise->wait(?int $timeoutMs = null, ?CancellationToken $token = null)`** behaves like `await($promise, …)`. `-1` still means no timeout.
34. **No `Tyhp\delay()`.**
35. **Unchanged, still available, not taught to PHP developers:** `_async`, `_await`, `_asyncMain`, `Promise::run`, `EventLoop::run`.
36. **Docblock fix.** The root-function docblocks in `tyhp/core` and `tyhp/async` `RootFunctions.tyhp` describe the emitted names (`_gnrc`, `_async`, `_await`, `_asyncMain`), not `gnrc` / `async`, including the "Skipped when a global … already exists" line.

### Expressions

37. **`_expr(User::class, ...$segments)`** is `Expression::of`, and **`_path(User::class)`** is `PropertyPath::on`. Both are `fallback function`s in `tyhp/lambda`, `#[\Tyhp\InferGeneric]`, with `@template T of object` and `@return PropertyPathBuilder<T>&T` docblocks.

### Out of scope

38. **Extension methods are not touched.** `\Tyhp\StringHelper` and the `\Tyhp\ArrayExtensions` static calls are not documented for PHP callers.

---

## Verify while implementing

These are checks, not open design choices. If a check fails, fix the implementation to match the decided behavior. If the decided behavior is impossible, stop and raise it; do not pick a different product behavior.

- **Generic argument equality.** Current state, read 2026-11-05 in `packages/tyhp/core/tyhp_src/Type.tyhp`: `is` reaches `isGeneric`, which calls `compatible`, and `areGenericArgsCompatible` unwraps `NamedType` and compares by position, so `is` / `compatible` already treat both forms as equal. `sameStructure` (used by `sameGenericList` for the generic type cache) requires both sides to be `NamedType` with the same parameter name, and `parse` names class arguments `T0`, `T1`, …, so `Type::parse('Box<int>')` and `Type::generic(Box::class, new NamedType('T', Type::int()))` miss each other in the cache. Make `sameStructure` compare by position, and have `T::generic` store declared names when they are known. Add tests for all four spellings in item 28.
- **Variadic generics.** Count the generic methods in `tyhp/*` with a variadic value parameter (`Promise::whenAll`, `whenAny`, `Expression::of`, …). The binder-first closure must forward variadics unchanged.
- **Named arguments into a variadic.** Confirm PhpStorm and PHPStan accept `T::generic(Map::class, TKey: T::string, TValue: T::int)` without a warning. If one warns, keep the runtime behavior and document positional calls first.
- **Arrays of promises.** `Type::of` on an array returns plain `array`. Inference from `array<Promise<T>>` must read each element. Confirm it on an empty array (falls back to the default).
- **Constants holding enum cases.** Confirm the Tyhp parser and emitter accept a class constant named `int` (and the other builtin names) on `Type` whose value is `BuiltIn::int`, and that the 802 build (no typed class constants) emits it. PHP itself accepts reserved words as constant and case names, except `class`.
- **`function async` in Tyhp source.** `Tyhp/TyhpLang/Parser/TyhpLexer.GrammarMethods.cs` already lets `function await` declare a free function. Confirm `function async` inside `namespace Tyhp` parses the same way.
- **Loop fiber detection.** `await` must tell a fiber the loop started from a user's own `\Fiber`. Match how `Promise::_await` and `EventLoop` track loop fibers today.
- **Generated static methods across inheritance.** PHP checks static method signatures for compatibility. Confirm that `Child<A, B> extends Box<A>` can declare `new($A = null, $B = null)` over `Box::new($T = null)`, and decide what a non-generic `IntBox extends Box<int>` inherits. If PHP rejects a needed shape, stop and raise it; do not rename `new` / `type`.
- **Two-argument `Type::struct` callers.** Item 26 changes a two-argument `Type::struct('Name', [...])` from "no keys required" to "all keys required". Search compiler emit and `tyhp/*` sources for two-argument calls before changing the default.
- **PHPStan with `BuiltIn` arguments.** Confirm whether PHPStan resolves `T` for a binder called with `T::int`. If it does not, the `Type::arg` conditional return is still required; note the limitation in Slice H's index.

---

## Diagnostics

Use the next free codes after TYHP4403 (Story 48). If another story has taken these numbers by the time this one starts, use the next free ones and update this table.

| Code | Name | Severity | When |
|------|------|----------|------|
| TYHP4404 | `CheckerGenericOnMagicMethod` | Error | A magic method (`__construct`, `__get`, `__call`, `__invoke`, `__toString`, …) declares type parameters. The message names the method. |
| TYHP4405 | `CheckerGenericOverridesNonGenericMethod` | Error | A generic method overrides or implements a non-generic parent method, or a non-generic method of a PHP interface. The message names the overridden method. |
| TYHP4406 | `CheckerGenericClassReservedMemberName` | Error | A generic class, or one of its ancestors, declares a method named `new` or `type`. |
| TYHP4407 | `CheckerInferGenericNotInferable` | Error | `#[\Tyhp\InferGeneric]` is on a generic with a type parameter no argument can infer. The message names the type parameter. |
| TYHP4408 | `CheckerInferGenericOnNonGeneric` | Warning | `#[\Tyhp\InferGeneric]` is on a declaration with no type parameters. |

TYHP4151 (`CheckerGenericOverrideParameterMismatch`) is unrelated and unchanged. Regenerate `tyhp-docs-src/content/diagnostics_reference.md` in Slice G.

---

## Slice A — `Type` ergonomics in tyhp/core

**Files.**

- `tyhp-runtime-src/packages/tyhp/core/tyhp_src/BuiltIn.tyhp` (new enum, implements `TypeLike`)
- `tyhp-runtime-src/packages/tyhp/core/tyhp_src/TypeLike.tyhp` (new interface)
- `tyhp-runtime-src/packages/tyhp/core/tyhp_src/Type.tyhp`: constants, `arg`, `list`, `map`, `optional`, widened `struct` and `generic`, fluent `or` / `nullable` accepting `TypeLike|string`, `sameStructure` by position, class PHPDoc (item 29)
- `tyhp-runtime-src/packages/tyhp/core/tyhp_src/Generic.tyhp`: `bind` takes `TypeLike|string ...$typeArguments` and normalizes with `Type::arg`
- `tyhp-runtime-src/packages/tyhp/core/tests/TypeTest.php` and new `tests/BuiltInTest.php`
- `tyhp-runtime-src/CHANGELOG.md` (`tyhp/core: …`, `### Added`)

**Behavior.** Items 21–31. `T::int` is `BuiltIn::int`. `BuiltIn::int->nullable()` and `T::int->or(T::string)` return `Type`. Every existing `Type` factory keeps its signature or widens it (`self` parameters become `TypeLike|string`).

**`TypeLike` members.** `toType(): Type`, `nullable(): Type`, `or(TypeLike|string ...$others): Type`. `Type::toType()` returns `$this`.

**Tests.**

- Each constant is the matching `BuiltIn` case, and `Type::arg(T::int)` equals `Type::int()`.
- A constant works as a parameter default and in an attribute argument (`#[SomeAttr(T::int)]` read back through reflection).
- `Type::arg` with `null`, a `Type`, a `BuiltIn`, a loaded class-string, an interface, an enum, and `'array<int>'` (throws, message names `Type::parse`).
- `T::struct(['id' => T::int, 'name' => T::optional(T::string)])` requires `id` only. The three-argument form is unchanged.
- `T::generic` positional, named, mixed, too many, too few, unknown name, named slot already filled by position, `NamedType`, and a plain PHP class (positional only; a name throws).
- Equality: item 28 across all four spellings, through `is`, `compatible`, and the cache.

---

## Slice B — binder-first generic methods and functions

**Files.**

- `Tyhp/TyhpLang/Binder/Symbols/GenericRuntimeAttributeSupport.cs` (`CurrentLayout` becomes 2 for callables; supported layouts become `[1, 2]`) and `GenericRuntimeInfo.cs` (`HasSupportedLayout` checks the supported set)
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.GenericVariants.cs`:
  - `EmitGenericVariantPair` / `EmitGenericVariantContract`: one method under the declared name for layout 2.
  - `EmitGenericVariantBinderMethod` / `EmitGenericVariantBinderFunction`, `BuildVariantBinderMethodSignature` / `BuildVariantBinderFunctionSignature`: parameter names and types (item 2).
  - `EmitVariantTypeArgPrologue`, `OpenVariantValueClosure`: `Type::arg` normalization; per-call inference inside the closure (item 4).
  - `BuildVariantBinderDocComment` / `BuildVariantClosureReturnDoc`: item 6.
  - `TryBuildGenericVariantCall`, `FormatStoredGenericBinder`, `BuildVariantTypeArguments`: call sites emit `name(types)(args)` for layout 2 and `name__tyhpGeneric(types)(args)` for a layout-1 callee.
  - `BuildVariantTypeofLookup` / `BuildVariantDefaultLookup` and `GenericVariantParamName`: read `$T`, not `$__generic_T`, in layout 2.
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.Helpers.cs` (`GenericRuntimeLayoutIsSupported`, the stamp at ~418)
- `Tyhp/TyhpLang/GeneratedNames.cs` (doc comments; `GenericVariantSuffix` stays for classes, `#[InferGeneric]` companions, and layout-1 consumers)
- `Tyhp/TyhpLang/Checker/Rules/DeclarationRule.Callable.cs` (TYHP4404, TYHP4405)
- `Tyhp/Domain/Exceptions/MessageCode.cs`, both resx files
- `tyhp-runtime-src/packages/tyhp/core/tyhp_src/Generic.tyhp`: `bindCallable` / `invokeMethodTarget` accept layout 2 (the binder is the declared method; every parameter is a type parameter), `padTypeArguments` / `genericParameterArity` count layout-2 parameters, and the supported-layout error lists `[1, 2]`
- `tyhp-runtime-src/packages/base-build-all.sh` re-emit of every package, then each package's PHP tests updated where they call a now binder-first method by its plain name
- `tyhp/CHANGELOG.md`; `tyhp-runtime-src/CHANGELOG.md` (`tyhp/core: …`, and one line per package whose public PHP calls change)

**Behavior.** Items 1–10. Interfaces and abstract methods declare only the binder signature, so implementors declare one method.

**Tests** (`tests/Tyhp.Tests/Emitter/`, `tests/Tyhp.Tests/Checker/`):

- Emit snapshots: static method, instance method, free function, interface method, abstract method, a method with a variadic value parameter, a method with a type-parameter default, and an erased generic (unchanged).
- Inference inside the closure: callable return, class-string, `T $value` with a `null` skipped, two parameters using `T` (union), none applicable (default), no default (unbound).
- Call sites: a Tyhp call to a layout-2 callee; a Tyhp call to a layout-1 tyhpdef stamp (`layouts: [1]`); an unsupported stamp (`layouts: [3]`) still reports `EmitterGenericRuntimeLayoutUnsupported`.
- TYHP4404 on `__construct<T>`, `__get<T>`, `__invoke<T>`. TYHP4405 on a generic `jsonSerialize<T>()` and on a generic override of a non-generic parent method. No diagnostic on a generic override of a generic parent method.
- `tyhp-runtime-src/packages/tyhp/core/tests/GenericTest.php`: `Generic::bind` on a layout-1 and a layout-2 callable, with `Type`, `BuiltIn`, and class-string arguments.

---

## Slice C — `new()` and `type()` on generic classes

**Files.**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.GenericClasses.cs` (next to `EmitGenericFactoryIfNeeded`)
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.Docblocks.cs` (item 16 and 17 PHPDoc)
- `Tyhp/TyhpLang/Checker/` declaration rules (TYHP4406), `MessageCode.cs`, both resx files
- `tyhp-runtime-src/packages/tyhp/core/tyhp_src/Type.tyhp` (`T::generic` reads names from `new()`)
- `tyhp/CHANGELOG.md`

**Behavior.** Items 16–20. `Box::new(T::int)(42)` and `new Box(42)` construct the same class; the first binds `int`, the second the declared default. `Box::type(T::int)` equals `T::generic(Box::class, T::int)` and `Type::parse('Vendor\Box<int>')`.

**Tests.** Emit snapshots for a concrete, an abstract, and a two-parameter generic class; a generic child of a generic parent (see [Verify while implementing](#verify-while-implementing)). Runtime: `Box::new(T::int)(42)` reports `Box<int>` through `Type::of`; nested `Map::type(T::string, Box::type(T::int))`. TYHP4406 on a generic class declaring `type()`, and on a generic class whose non-generic parent declares `new()`.

---

## Slice D — `#[\Tyhp\InferGeneric]`

**Files.**

- `tyhp-runtime-src/packages/tyhp/core/tyhp_src/InferGeneric.tyhp` (new attribute, `Attribute::TARGET_METHOD | Attribute::TARGET_FUNCTION`)
- `Tyhp/TyhpLang/Binder/Symbols/` (bind the attribute), `Tyhp/TyhpLang/Checker/` (TYHP4407, TYHP4408), `MessageCode.cs`, both resx files
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.GenericVariants.cs`: layout-1 pair for attributed callables; `BuildWrapperDelegatingTypeArguments` and `TryFindCallableReturnInferenceParameter` gain the `Promise<T>` and array-of-promises sources (item 13)
- `tyhp/CHANGELOG.md`; `tyhp-runtime-src/CHANGELOG.md` (`tyhp/core: …`)

**Behavior.** Items 11–13. Tyhp call sites with no explicit type arguments call the plain method. Tyhp call sites with explicit type arguments call the companion.

**Audit.** List every non-erased public generic method and function in `tyhp-runtime-src/packages/tyhp/*/tyhp_src` (the emitted `#[\Tyhp\GenericRuntime(erased: false, binder: …)]` stamps under `packages/dist/*/805.latest/src` are the quick inventory). For each, record in this slice's landing evidence whether it is `#[\Tyhp\InferGeneric]` or binder-first. Known plain-call candidates, to mark when every type parameter is inferable: `Expression::of`, `PropertyPathBuilder::of`, `PropertyPath::on`, `Lazy::ghost`, `Lazy::proxy`, `Lazy::reset`. `Expression::lambda` and `PropertyPath::from` are marked only if their type parameter is inferable from the callable argument; otherwise Slice H documents them with `()`. Item 14's list is marked in Slice E and F.

**Tests.** TYHP4407 for a `T` reachable only from the return type. TYHP4408 on a non-generic method. Emit: plain method plus companion, stamped `layouts: [1]`. Runtime: `Generic::bind(Promise::resolved(...), T::int)(1)` binds `int`; `Promise::all([$a, $b])` infers the union of the two result types.

---

## Slice E — tyhp/async for PHP callers

**Files.**

- `tyhp-runtime-src/packages/tyhp/async/tyhp_src/Functions/async.tyhp` and `Functions/await.tyhp` (new, `namespace Tyhp`)
- `tyhp-runtime-src/packages/tyhp/async/tyhp_src/Promise.tyhp`: `#[\Tyhp\InferGeneric]` on item 14's methods; `wait` (item 33)
- `tyhp-runtime-src/packages/tyhp/async/tyhp_src/RootFunctions.tyhp` and `tyhp-runtime-src/packages/tyhp/core/tyhp_src/RootFunctions.tyhp`: `#[\Tyhp\InferGeneric]` on `_async`, `_await`, `_asyncMain`; docblock fix (item 36)
- `tyhp-runtime-src/packages/tyhp/async/composer.json` `autoload.files`, if the emitted function file is not already listed
- `tyhp-runtime-src/packages/tyhp/async/tests/` (new `AsyncAwaitFunctionsTest.php`; `PromiseTest.php` for `wait`)
- `tyhp-runtime-src/CHANGELOG.md` (`tyhp/async: …` under `### Added` and `### Changed`; `tyhp/core: …` under `### Fixed` for the docblocks)

**Behavior.** Items 32–36.

**Tests.**

- `await(async(fn (): int => 42))` from synchronous PHP returns 42 and runs queued work before returning.
- `await` inside `async` suspends and resumes; two concurrent `await`s interleave.
- `await($slow, 10)` throws `TimeoutException` both from synchronous PHP and inside a fiber.
- A cancelled token throws `OperationCancelledException`.
- `await` from a loop callback outside a fiber throws `\LogicException`.
- `$promise->wait()`, `wait(10)`, `wait(-1)`.
- `Promise::then`, `all`, `race` called plainly from PHP infer their result types (`Type::of` on the returned promise).

---

## Slice F — tyhp/lambda root functions

**Files.**

- `tyhp-runtime-src/packages/tyhp/lambda/tyhp_src/RootFunctions.tyhp` (new; `fallback function _expr` and `fallback function _path`)
- `tyhp-runtime-src/packages/tyhp/lambda/composer.json` `autoload.files`
- `tyhp-runtime-src/packages/tyhp/lambda/tests/` (new `RootFunctionsTest.php`)
- `tyhp-runtime-src/CHANGELOG.md` (`tyhp/lambda: …`, `### Added`)

**Behavior.** Item 37. Docblocks name the emitted functions (`_expr`, `_path`) and state that they are skipped when a global function of that name already exists.

**Tests.** `_expr(User::class)->age->gte(18)` builds the same tree as `Expression::of(User::class)->age->gte(18)`. `_expr(User::class, 'address', 'city')` matches the segment form. `_path(User::class)->name` matches `PropertyPath::on(User::class)->name`.

---

## Slice G — Tyhp docs, AI dev guide, emitter guide

**Files.**

- `Tyhp/TyhpLang/Emitter/technical-guide.md` §5 "Mechanism D" (layout 2 binder under the declared name; `#[InferGeneric]` pair; layout-1 consumption) and §5 "Mechanism C" (`new()` / `type()`)
- `tyhp-docs-src/content/tyhp_0500_generics.md`, `package_core.md`, `package_async.md`, `package_lambda.md`, `cli_interopContract.md`, `quickref.md`, and the other pages that show `__tyhpGeneric` or `Generic::bind` PHP calls (`tyhp_2100_extensions.md`, `tyhp_2500_phpMagicMethods.md`, `tyhp_2700_compileTimeConstructs.md`, `tyhp_0152_lazyObjects.md`, `faq_general.md`, `faq_tyhpSyntax.md`, `tyhpdef_extensions.md`, `cli_build.md`, `cli_tyhpdefGeneration.md`): describe the emitted shape and the PHP call
- `tyhp-docs-src/content/diagnostics_reference.md` (regenerated for TYHP4404–TYHP4408)
- `tyhp-ai-dev-guide/guide/07-generics.md` (forbidden generics, reserved `new` / `type`, `#[\Tyhp\InferGeneric]`), `guide/29-php-mapping.md`, `guide/25-runtime-packages.md`, `handbook/05-php-interop.md`, `handbook/07-runtime-api.md`, `QUICK_GUIDE.md`, `SKILL.md`; the matching `REGEN.md` item
- `tyhp-docs-src/CHANGELOG.md`, `tyhp-ai-dev-guide/CHANGELOG.md`

**Done when** a search of those files finds `__tyhpGeneric` only where a page describes layout-1 compatibility or `#[\Tyhp\InferGeneric]` companions, and every PHP sample uses the call syntax in [Slice H](#call-syntax).

---

## Slice H — PHP package docs

Start this slice only after Slices A–G have landed and after `tyhp-runtime-src/dev-docs/IMPLEMENTATION_PLAN_TYHP_RHETORIC.md`, including Phase 14, is finished.

**Audience.** PHP developers who call a `tyhp/*` package and do not write Tyhp.

**Site.** [tyhplang.com](https://tyhplang.com), generated from `tyhp-docs-src` (`content/`, `content/toc.json`, `php generate_docs.php`). Same site as the existing Runtime Packages section and as rhetoric Phase 14's docs. Do not add a second site. The Tyhp-language pages at `content/package_*.md` stay in place.

### Goals

- A PHP index page plus one page per package in [Packages](#packages), linked from a new TOC section.
- Every code sample is PHP and uses [Call syntax](#call-syntax).
- A reader can complete a package's quick start without opening a Tyhp language page.

### Not in this slice

- A Tyhp language tutorial: `<?tyhp`, the `is` operator, angle-bracket type arguments, the `async` / `await` keywords, `fn` as a parsable lambda, or `decimal` operators.
- Compiler internals, emit layouts, `__tyhpGeneric` helper names, diagnostic-code tours.
- `tyhpdef/*` packages. PHP callers do not install them. Mention one only if a sample is wrong unless the caller installs it; none of the packages below require that.
- Rewriting `content/package_*.md` (Slice G updates them for Tyhp authors).
- A second rhetoric doc set, or a handbook chapter in `tyhp-ai-dev-guide`.

### Call syntax

The index's call-syntax section teaches this, once. Package pages link to it and still write each sample with the real call. Samples never call `*__tyhpGeneric` helpers or construct `\Tyhp\NamedType`.

```php
use Tyhp\Type as T;
use function Tyhp\async;
use function Tyhp\await;
```

**Types.** `T::int`, `T::string`, and the other builtin constants. Combined types: `T::int->nullable()`, `T::int->or(T::string)`, `T::list(T::int)`, `T::map(T::string, T::int)`, `T::struct(['id' => T::int, 'name' => T::optional(T::string)])`. A class is its class-string: `\App\User::class`. A generic class is `Box::type(T::int)` (nests: `Map::type(T::string, Box::type(T::int))`), or `T::generic(\Vendor\Box::class, T::int)` for a plain PHP class or a class named at runtime. A type written as text goes through `T::parse('array<string, App\Post>')`.

**Generic methods take their types first.** Call the method with its type arguments, then call the result with the value arguments. Omitted type arguments are inferred from the values where possible.

```php
$user = \Tyhp\Json::decode(T::struct(['id' => T::int, 'name' => T::string]))($json);
$rows = $result->rowsAs(T::struct(['id' => T::int]))();
$p = \Tyhp\Promise::rejected()(new \RuntimeException('no'));
```

**Methods whose types come from their arguments are plain calls.** The async API and the expression builders: `Promise::resolved(42)`, `$p->then(fn (int $v): string => (string) $v)`, `Promise::all([$a, $b])`, `\_expr(\App\User::class)`. Each page lists which of its methods take types first. The PHPDoc of every generic method shows its call form.

**Generic classes.** `\Vendor\Box::new(T::int)(42)` binds `int`. `new \Vendor\Box(42)` uses the declared default.

**Runtime-chosen classes and explicit types on plain calls.** `\Tyhp\Generic::bind($target, ...$types)` (root function `\_gnrc`) binds a class-string or a first-class callable: `\Tyhp\Generic::bind($className, T::int)->new(42)`, `\Tyhp\Generic::bind(\Tyhp\Promise::resolved(...), T::int)(1)`.

**Async.** `async(fn () => …)` returns a `\Tyhp\Promise`. `await($promise)` returns its value: inside `async` it suspends, and from ordinary synchronous PHP it runs the event loop until the work is done. `await($promise, 500)` adds a timeout in milliseconds. `$promise->wait()` is the same as `await($promise)`.

**Expressions.** `\_expr(\App\User::class)->age->gte(18)->and()->isActive->same(true)` and `\_path(\App\User::class)->address->city`.

### Page outline

Use this outline for every page unless Phase 14 specified a different one for rhetoric's PHP pages. Match that outline instead when it did.

1. Front matter with `title` only, same as `content/package_core.md`.
2. Opening paragraph: Composer package name, PHP namespace, and what a PHP caller does with the package.
3. Install: `composer require`, the PHP version from that package's `composer.json`, and required or suggested extensions from that file. Runtime packages use the `80N.X.Y` scheme in `content/package_intro.md` (`802` for PHP 8.2 through `805` for PHP 8.5). Read the package's current `version` from its `composer.json` for the pin. `tyhp/compiler` uses the compiler version (`content/package_compiler.md`).
4. Quick start: one `php` sample that installs nothing beyond the require, calls the package, and produces a value.
5. One heading per job the caller does. Name the real class or function, give the PHP signature (no angle brackets), say whether it takes types first, and add a `php` sample where the call is not the quick start.
6. Errors: the exception classes the caller catches.
7. Related: the other PHP package pages this one calls. A link to a Tyhp-language page is optional further reading.

Shared rules:

- Fences are `php`, `bash`, `json`, or `text`. No `tyhp` fence.
- Struct values are PHP arrays; their types are `T::struct([...])`. `with` is `\Tyhp\ObjectHelper::with($object, $properties)`.
- Promises are values. Samples drive them with `await(...)`.

### Where the pages go

| File | Page |
| --- | --- |
| `tyhp-docs-src/content/php_packages.md` | Index and the call-syntax section |
| `tyhp-docs-src/content/php_package_core.md` | `tyhp/core` |
| `tyhp-docs-src/content/php_package_async.md` | `tyhp/async` |
| `tyhp-docs-src/content/php_package_decimal.md` | `tyhp/decimal` |
| `tyhp-docs-src/content/php_package_lambda.md` | `tyhp/lambda` |
| `tyhp-docs-src/content/php_package_io.md` | `tyhp/io` |
| `tyhp-docs-src/content/php_package_parallel.md` | `tyhp/parallel` |
| `tyhp-docs-src/content/php_package_compiler.md` | `tyhp/compiler` |

Add a TOC section in `tyhp-docs-src/content/toc.json`, after the Runtime Packages section, titled **Packages for PHP**. List `php_packages.md` first, then the package pages in table order. TOC items are filenames under `content/`.

`tyhp/rhetoric` has no row. The index has one line linking the PHP pages Phase 14 added.

Add one `### Added` line under `## [Unreleased]` in `tyhp-docs-src/CHANGELOG.md`.

### Packages

The inventory is the directories under `tyhp-runtime-src/packages/tyhp/` that have a `composer.json`: eight packages.

#### `tyhp/core`

**What a PHP caller does.** Loads the runtime the other packages sit on, builds types, checks values, decodes JSON into a struct array, and constructs generic classes.

**Page:** `content/php_package_core.md`.

**Covers:**

- `\Tyhp\Type` (imported as `T`): the constants, `nullable`, `or`, `list`, `map`, `struct`, `optional`, `generic`, `parse`, `of`, `is`, `check`. `\Tyhp\TypeLike` and `\Tyhp\BuiltIn` by name, as the types a parameter accepts.
- Generic classes: `Box::new(...)(...)`, `Box::type(...)`.
- `\Tyhp\Generic::bind` and `\_gnrc`, for classes and callables chosen at runtime. One-shot `new()` / `__invoke`, and the `\LogicException` on a second construct.
- `\Tyhp\Json::decode` and `tryDecode`, types first, with a struct type. Invalid JSON throws `\JsonException`. A value that does not match throws `\Tyhp\Exceptions\IncompatibleTypeException`.
- `\Tyhp\ObjectHelper::with`.
- Caller-facing exceptions: `IncompatibleTypeException`, `InvalidTypeException`, `PropertyNotFoundException`.
- Contracts a caller implements or type-checks: `IsDisposable` and the `*Convertible` interfaces, by name and method only.

**Leave out:** authoring `#[\Tyhp\GenericRuntime]`, `#[\Tyhp\InferGeneric]`, `EraseGeneric`, or `NoEmit`; extension methods and their static backers. The Composer plugin is one sentence on the compiler page (it can add `tyhp/compiler` to `require-dev`), not an application API.

**Done:** the quick start decodes JSON with `Json::decode(T::struct([...]))($json)` and constructs a generic with `Box::new(T::int)(...)`, with no angle bracket.

#### `tyhp/async`

**What a PHP caller does.** Starts work that returns a `\Tyhp\Promise`, waits for it from synchronous PHP or from other async work, cancels it, and combines several promises.

**Page:** `content/php_package_async.md`. Depends on `tyhp/core`.

**Covers:**

- `Tyhp\async` and `Tyhp\await` (timeout, token, and the synchronous-PHP behavior), and `$promise->wait`.
- `then`, `catch`, `finally`, `getResult`, `getState`.
- Factories and combinators as plain calls: `resolve`, `resolved`, `all`, `allSettled`, `race`, `any`, `timeout`, `batch`, `whenAll`, `whenAny`. Types first: `rejected`, `withResolvers`.
- `\Tyhp\Deferred` (`new \Tyhp\Deferred()`, `\Tyhp\Deferred::new(T::int)()`), `\Tyhp\CancellationToken`, `\Tyhp\CancellationTokenSource`.
- `\Tyhp\PromiseSettledResult`, the object `allSettled` returns: public `$status`, `$value`, `$reason` (`packages/dist/tyhp-async/805.latest/src/Tyhp/PromiseSettledResult.php`).
- Exceptions: `OperationCancelledException`, `TimeoutException`, `InvalidPromiseStateException`, `AggregateException`, and the `\LogicException` `await` throws when called from a loop callback outside `async`.
- The async iterator contracts only as the methods a PHP class implements (`next`, `current`, `currentKey`, `currentValue`, `getAsyncIterator`), driven with `async` / `await`.

**Done:** the quick start is `await(async(fn (): int => 42))` and prints the value. A second sample combines two promises with `Promise::all` and `await`.

#### `tyhp/decimal`

**What a PHP caller does.** Builds an immutable `\Tyhp\Decimal` and calls arithmetic methods. Results stay decimal. PHP has no decimal operators, so the page shows methods.

**Page:** `content/php_package_decimal.md`. Depends on `tyhp/core`.

**Covers:**

- `new \Tyhp\Decimal($value, $scale, $roundingMode)` and `\Tyhp\decimal($value)`.
- `add`, `subtract`, `multiply`, `divide`, `modulo`, `power`, `negate`, `abs`, `sqrt`, `compareTo`, the comparison methods, `round`, `floor`, `ceil`, `withScale`, `format`, `__toString`, `jsonSerialize`, and the static `zero`, `one`, `min`, `max`, `sum`, `avg`.
- Scale and rounding: string operands keep their written scale; `divide`'s `$scale` overrides; a negative scale throws `\ValueError`; division by zero throws `\DivisionByZeroError`.
- Backends, in the order the package picks them: `ext-decimal`, `ext-bcmath`, `ext-gmp`, then pure PHP. `getBackendName()` and `setBackend`. Names from `content/package_decimal.md` and `composer.json` `suggest`.
- `\Tyhp\Contracts\DecimalConvertible`, the interface a caller implements (`__toDecimal(): Decimal`).

**Done:** the quick start is `\Tyhp\decimal('19.99')->multiply(3)` (or `new \Tyhp\Decimal`) and prints the result. No `*` sample.

#### `tyhp/lambda`

**What a PHP caller does.** Builds an expression tree or a property path for a library method that expects one. PHP closures are not captured as trees.

**Page:** `content/php_package_lambda.md`. Depends on `tyhp/core`.

**Covers:**

- `\_expr($class, ...$segments)` as the entry, with `\Tyhp\Expression::of` as the same call. Property access (`->name`, through `__get`), `->prop('name')`, comparisons (`gte`, `eq`, `same`, `isNull`), and `and` / `or`.
- `\_path($class)`, with `\Tyhp\PropertyPath::on` as the same call.
- `\Tyhp\Expression::lambda` and `\Tyhp\PropertyPath::from`, with the call form Slice D's audit recorded.
- Passing a finished builder to another library (the object is the argument). Do not invent a repository class.
- The node classes under `\Tyhp\Expression` a caller reads when walking a tree: the set in `tyhp-ai-dev-guide/handbook/07-runtime-api.md` (`ParameterExpression` through `LambdaExpression`, including `ArrayExpression` and `NullSafeMethodCallExpression`), with the fields a caller reads (`body`, `parameters`, `type`, `items`). Not the interpreter or serializer.
- Failures: the `\LogicException` messages `content/package_lambda.md` quotes for a comparison with no property and for property access on a completed builder.

**Done:** the quick start is `\_expr(\App\User::class)->age->gte(18)` in a `php` fence.

#### `tyhp/io`

**What a PHP caller does.** Performs HTTP, process, file, socket, Redis, PostgreSQL, MySQL, PDO, and DNS work on the `tyhp/async` loop, and moves blocking work onto `tyhp/parallel` when that package is installed.

**Page:** `content/php_package_io.md`. Depends on `tyhp/core` and `tyhp/async`. `tyhp/parallel` is optional.

**Covers**, using the classes under `Tyhp\Io`:

| Job | Entry | Notes for the sample |
| --- | --- | --- |
| HTTP | `HttpClient::shared()`, `getAsync`, `postAsync`, `postJsonAsync`, `sendAsync` | Returns `Promise`. `await` it. |
| HTTP JSON | `getJsonAsync` | Types first: `$client->getJsonAsync(T::struct([...]))($url)`. |
| Processes | `Process::runAsync`, `mustRunAsync` | `ProcessResult` fields as emitted. |
| Files | `File::readAsync`, `writeAsync`, `appendAsync`, `copyAsync`, `hashAsync`, `linesAsync` | `linesAsync` returns an async iterable. Show one iteration inside `async` with `await`. |
| File JSON | `File::readJsonAsync` | Types first, like `Json::decode`. |
| Sockets | `Socket::connectAsync`, `SocketConnection` | `tls://` needs OpenSSL. |
| Redis | `RedisClient::connectAsync` | |
| PostgreSQL | `PostgresConnection::connectAsync` | Needs `ext-pgsql`. |
| MySQL | `MySqlConnection::connectAsync` | Needs `ext-mysqli`. |
| PDO | `PdoConnection` | Needs `ext-pdo`. With `tyhp/parallel` available, the query runs on a worker. |
| Rows | `\Tyhp\Io\Db\DbResult::rowsAs`, `firstAs` | Types first, with a struct type. The value is an array. |
| DNS | `Dns::resolveAsync` | Uses a worker when `tyhp/parallel` is available. |
| Blocking work | `Offload::runAsync`, `Offload::usesWorkers` | The caller writes a class that implements `OffloadJob`. |
| Cancel | `CancellationToken` from `tyhp/async` | Pass `$token` through. |

Extensions and the optional parallel behavior come from `packages/tyhp/io/composer.json` `suggest` and `content/package_io.md`. A missing extension throws `\Tyhp\Io\Exceptions\IoUnavailableException` on first use.

**Errors:** `IoException`, `IoUnavailableException`, `HttpException`, `HttpStatusException`, `DbException`, `RedisException`, `ProcessFailedException`, plus `TimeoutException` and `OperationCancelledException` from `tyhp/async`. With workers, a non-PDO failure rejects with `ParallelException`.

**Leave out:** `MySqlPlaceholders`, `RespParser`, `IoWait`, `IoPending`, `PdoJob`, and `OffloadTask`.

**Done:** the quick start is `await(HttpClient::shared()->getAsync(...))`. A second sample calls `getJsonAsync` or `readJsonAsync` types first with `T::struct`.

#### `tyhp/parallel`

**What a PHP caller does.** Runs a serializable task in another PHP process and receives a `Promise` of the result. CLI programs fork a pool. php-fpm talks to a socket set in `TYHP_PARALLEL_SOCKET`.

**Page:** `content/php_package_parallel.md`. Depends on `tyhp/core` and `tyhp/async`. Requires `ext-pcntl` and `ext-posix` (`composer.json`).

**Covers:**

- `Parallel::start`, `isAvailable`, `shutdown`, `serve`.
- `Parallel::runAsync($task, $token)` returning a `Promise`, driven with `await`. Rewrite the existing PHP sample in `content/package_parallel.md` (`Parallel::start`, then `EventLoop::getInstance()->runUntilSettled(Parallel::runAsync(...))`) to `await(Parallel::runAsync(...))`.
- `forAsync`, `forEachAsync`, and `invokeAsync`, with the call form Slice D's audit recorded. `forEachAsync` has type parameters `TItem`, `TResult`, and `TKey`.
- The caller writes a named class that implements `\Tyhp\ParallelTask` with `run()`. Closures and anonymous classes are not tasks.
- `ParallelException::getRemoteError()` returns the array the sample reads (`class`, `message`). `ParallelUnavailableException` when the pool cannot run.

**Leave out:** `ParallelPool`, `ParallelClient`, `ParallelServer`, `ParallelLink`, `ParallelFrames`, and the frame opcodes.

**Done:** the quick start starts the pool, awaits `Parallel::runAsync` on a tiny named task class in the same sample, and catches `ParallelException`.

#### `tyhp/compiler`

**What a PHP caller does.** Runs `vendor/bin/tyhp`, which downloads the native compiler and forwards arguments to it. Application code does not construct the shim.

**Page:** `content/php_package_compiler.md`.

**Covers:** the caller-facing contract already in `content/package_compiler.md`, rewritten to stand alone: `composer require --dev tyhp/compiler`, the `post-autoload-dump` hook `vendor/bin/tyhp --install-binary`, `vendor/bin/tyhp <command>`, `--install-binary` swallowed by the shim, `GITHUB_TOKEN` / `GH_TOKEN`, and production `composer install --no-dev --no-scripts`. PHP `>=8.2` and `ext-curl`, from `packages/tyhp/compiler/composer.json`. The version is the compiler version, not `80N.X.Y`. `Tyhp\Compiler\Shim`, `Installer`, `Platform`, and `HttpClient` implement the bin script; application code does not call them.

**Done:** the page's only samples are Composer and `vendor/bin/tyhp`.

#### `tyhp/rhetoric`

Owned by Phase 14 of the rhetoric plan. No page or samples here. The index links to Phase 14's pages.

### Done

- The eight files in [Where the pages go](#where-the-pages-go) exist, and `content/toc.json` lists them under **Packages for PHP**.
- `php generate_docs.php` in `tyhp-docs-src` succeeds.
- `tyhp/rhetoric` has no new page from this slice. The index links to Phase 14's pages.
- Every `php` sample uses [Call syntax](#call-syntax). A search of the new files finds no `<?tyhp`, no `php` fence containing `<int>` or `::resolved<`, no `__tyhpGeneric`, no `NamedType`, no `StringHelper`, no `ArrayExtensions`, no `_asyncMain`, and no `Type::int()` (the constant is taught).
- The core, async, decimal, lambda, io, and parallel quick starts each run against the installed package without a Tyhp file.
- `tyhp-docs-src/CHANGELOG.md` has the `### Added` line.
- `content/package_*.md` gains at most a single-line related link at the bottom pointing to the matching PHP page.

### Order

1. Confirm Phase 14's PHP page outline, if it specified one, and use it.
2. Add the TOC section and `php_packages.md` with the call-syntax section.
3. Write the package pages in table order, core first.
4. Link the rhetoric pages from the index.
5. Run `php generate_docs.php`.
6. Add the docs changelog line.

---

## For the tyhp/rhetoric implementer

Rhetoric is not edited by this story beyond the re-emit in Slice B. Its generic methods become binder-first unless Slice D's audit marks them.

Phase 14 of `IMPLEMENTATION_PLAN_TYHP_RHETORIC.md` currently tells its PHP pages to call generics through `\Tyhp\Generic::bind` with `\Tyhp\Type::int()`, `Type::fromClassName`, and `new \Tyhp\NamedType(...)`, and to build trees with `\Tyhp\Expression::of`. Once Slices A–F have landed, Phase 14's PHP samples use [Call syntax](#call-syntax) instead: `use Tyhp\Type as T`, types-first method calls, `Box::new` / `Box::type`, `T::struct`, `await`, and `\_expr`.

### How to update the rhetoric plan now

1. Point Phase 14's PHP call-syntax bullet and its **Done when** line at this file's [Call syntax](#call-syntax), instead of restating `Generic::bind` and `NamedType`.
2. Do not paste this story's signatures into that plan.
3. Leave every other phase executable.

### How to check progress later

1. Re-read the Status section of this file.
2. Write Phase 14's PHP pages against the call syntax only after Slices A–F are `Landed`.
3. If Status and the code disagree, trust the landing-evidence paths, then re-read the slice.

---

## Rejected alternatives

- **Named type arguments on the plain method** (`Json::decode($json, T: T::int)`). A variadic value parameter (`mixed ...$args`) swallows named arguments.
- **Inferring class-level type parameters** from constructor arguments. A mutable container would be typed too narrowly (`new Collection([1])` would reject a later `'x'`).
- **A misuse guard** that detects value arguments passed to a binder.
- **Automatic plain emit** whenever every type parameter is inferable. Adding a type parameter later would silently change how PHP calls the method. `#[\Tyhp\InferGeneric]` makes it explicit.
- **Binder-first for the async API** (`$p->then()($fn)`, `Promise::all()($ps)`). Async code is mostly these calls.
- **Static properties** (`T::$int`). They cannot appear in attribute arguments or parameter defaults.
- **Namespaced type functions or bare constants** (`T\int`, `int` cast-like tokens). PHP parses `(int)` as a cast, and functions cannot appear in constant expressions.
- **Strings for builtin types** (`'int'`) in `Type::arg`. Fragile. Type spellings go through `Type::parse` explicitly.
- **`T::of($class, ...)`** for generic types. `Type::of(mixed $value)` already returns a value's type and the compiler emits it. `Type::generic` is widened instead.
- **`::T()` instead of `::type()`** on generic classes.
- **`Map::of(...)`** as the generic-class type builder.
- **Struct marker classes** emitted per public struct so a struct has a class-string.
- **`Tyhp\delay()`.**
- **Exposing extension methods to PHP**: aliases, a single helper class, fluent wrappers.

---

## Non-goals

- Extension methods, `\Tyhp\StringHelper`, `\Tyhp\ArrayExtensions`, and `\Tyhp\ArrayExtensionsVersioned`.
- Removing `_async`, `_await`, `_asyncMain`, `Promise::run`, `EventLoop::run`, `Generic::bind`, or `_gnrc`.
- Removing layout 1. The compiler and `Generic::bind` keep consuming it.
- Changing `Type::of`.
- Required struct registration (`Type::register` for every public struct).
- `tyhpdef/*` docs pages, a Tyhp language tutorial for PHP developers, and rhetoric's PHP pages (rhetoric Phase 14).
