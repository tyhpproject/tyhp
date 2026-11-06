# Found Bugs / Issues

This file tracks **unresolved** issues only. Resolved issues are archived in
[`RESOLVED_BUGS.md`](RESOLVED_BUGS.md) (gitignored; not in the public tree).

Status tags:

- **Ready** — decided; not implemented yet.
- **NEEDS INPUT** — blocked on a user/design decision; do not implement until decided.
- **Deferred (Story 22+)** — intentionally out of scope until Story 22 or later (includes Story 31).
- **Deferred (no consumer)** — latent / no current consumer; revisit when a consumer appears.

Severity (Ready and NEEDS INPUT items):

- **Critical** — silent wrong emit, or a runtime package cannot compile.
- **High** — unsound accept of invalid programs, or rejection of a core language / stdlib path without a reasonable workaround.
- **Medium** — wrong types or diagnostics with a workaround, incomplete syntax, or a narrow soundness hole.
- **Low** — noise, docs, tooling, or a harvest gap already covered by Layer 3.

---

## Ready

### 18. `\Closure::bind` rejects a concrete closure as `\Closure<mixed, mixed, mixed>`

- **Where:** `packages/tyhpdef/php/_tyhpdef/overlays/Ext.Core.tyhpdef` `Closure::bind`. Call site: `tyhp-runtime-src/packages/tyhp/core/tyhp_src/Generic.tyhp` `callInClassScope`.
- **Issue:** `\Closure::bind($call, null, $object)` where `$call` is `\Closure<callable(object): void, ?object, null>` and `$object` is `object` reports TYHP4010: the argument is not assignable to `\Closure<mixed, mixed, mixed>`. `$call->bindTo(null, $object)` typechecks and rebinds the same scope. A runtime class-string is also not a `__ClosureScope`, so `\Closure::bind($call, null, $className)` is not a substitute.
- **Impact:** A static `\Closure::bind` that sets scope from an instance does not compile. `bindTo` is the same PHP operation and does compile.
- **Suggested fix:** Infer `TCallableShape`, `TOldThis`, and `TOldScope` from the closure argument before substituting `mixed`, and accept `bindTo`'s successful instantiation on `Closure::bind`.
- **Status:** Ready
- **Severity:** Medium — `bindTo` is a working substitute. Slice F uses that.

### 17. A `#[\Tyhp\Php]` method that does not match the build floor is omitted, not emitted behind `\PHP_VERSION_ID`

- **Where:** `Tyhp/TyhpLang/Binder/TyhpBinder.PhpVersionAttributes.cs` `IsPhpVersionGateOmitted` treats `#[\Tyhp\Php(">=8.4")]` as inactive when `output.phpVersion` is 8.2, so the method is not bound. `Tyhp/TyhpLang/Emitter/TyhpEmitter.Helpers.cs` `ShouldEmitPhpVersionGatedDeclaration` then drops it. The sibling that does match the floor is emitted with no `\PHP_VERSION_ID` check. `package.tyhpdef` for that build lists only the matching sibling.
- **Issue:** `tyhp/core` `Lazy::isLazy` is two disjoint methods. The 802 artifact's `Lazy.php` is `return false` with no version check, and `dist/tyhp-core/802.0.4/package.tyhpdef` declares only `#[\Tyhp\Php("<8.4")] isLazy`. `tyhp/async` typechecks every PHP build against that 802 tyhpdef (`packages/tyhp/async/composer.json` path repo). A bare `\Tyhp\Lazy::isLazy($object)` call therefore reports TYHP4101 when async is built at 8.4 or 8.5, because the `<8.4` method is hidden and the `>=8.4` method was never in the tyhpdef. The 802 `isLazy` also stays false when that package runs on PHP 8.4.
- **Impact:** A package built from the 802 tyhpdef cannot call the 8.4 half of a version-split core API while compiling its own 8.4 artifact. Disposal in `tyhp/async` calls `ReflectionClass::isUninitializedLazyObject` through `call_user_func` behind `\PHP_VERSION_ID >= 80400`, in the same source for every build, for that reason.
- **Suggested fix:** When a `#[\Tyhp\Php]` constraint is true for only some versions at or above the floor, emit that body inside `\PHP_VERSION_ID` and keep every sibling in `package.tyhpdef`. `RuntimeGateEmission.ClassifyAtOrAbove` already classifies that case.
- **Status:** Ready
- **Severity:** Critical — the 8.4 and 8.5 `tyhp/async` builds cannot compile a call to `\Tyhp\Lazy::isLazy`.

### 16. An inline function with an explicit return type is not checked against the expected callable return

- **Where:** `Tyhp/TyhpLang/Checker/Rules/TypeCompatibilityRule.Calls.cs` `CheckArgumentAgainstParameterType`. An argument that is a `PhpInlineFunctionAst` is `CheckNode`'d with `ExpectedClosureType` set, then the method returns without comparing the closure to the parameter. `Tyhp/TyhpLang/Checker/Rules/ClosureRule.cs` applies that expected return only when the closure omits its return type.
- **Issue:** `fn (\stdClass $object): string => "no"` is accepted for `callable(\stdClass): void`. The same call with the return type omitted reports TYHP4009 (`'no'` is not compatible with `void`). A non-inline value of type `callable(\stdClass): string` reports TYHP4010. A mismatched parameter (`fn (int $n)`) is still reported.
- **Impact:** An inline function can declare a return type the callable shape does not allow. Callers of `ReflectionClass::newLazyGhost` (`callable(T): void`) and `newLazyProxy` (`callable(T): T`) are affected the same way as any other callable-shaped parameter.
- **Suggested fix:** After checking the inline function, compare its return type with the expected callable facet's return type, the same way a non-inline argument is compared.
- **Status:** Ready
- **Severity:** Medium — narrow soundness hole. Omitting the return type, or passing a non-inline callable, is checked.

### 15. Named argument labels that match a class are emitted as that class

- **Where:** `Tyhp/TyhpLang/Emitter/TyhpEmitter.Expressions.cs` `FormatArgument` prints `argument.Name.ValueString`. A label such as `column:` is bound as the class `Tyhp\Rhetoric\Attributes\Column`, so the emitted PHP is `\Tyhp\Rhetoric\Attributes\Column: $column`, which is a parse error. The same happens for `foreignKey:` and `index:` when those attribute classes are in scope of name resolution. Repro: `new Operation(OperationKind::AddColumn, 'posts', column: $column)` in `packages/tyhp/rhetoric/tyhp_src/Migrations/MapDiffer.tyhp` before the call was rewritten to positional arguments.
- **Issue:** Argument labels are names, not types. Qualifying them produces PHP that does not parse, and the build still reports success until PHP-CS-Fixer or PHPUnit lints the file.
- **Impact:** Any named argument whose label matches a class, case-insensitively, emits invalid PHP.
- **Suggested fix:** Emit the label's original spelling. Do not run type-name qualification on argument labels.
- **Status:** Ready
- **Severity:** Critical — silent wrong emit; the file does not parse.

### 12. Extension calls inside an expression-tree lambda are spliced and then rejected

- **Where:** `Tyhp/TyhpLang/Emitter/AliasConverter.cs` rewrites an extension call (`$string->startsWith(...)`) to the extension body before `ExpressionTreeEmissionHelper` builds the tree. `packages/tyhp/core` `StringExtensions.tyhp` (`startsWith` → `\str_starts_with`). Repro: `tyhp-runtime-src/dev-docs/rhetoric-spikes/expr/spike1/StartsWith.tyhp`.
- **Issue:** The checker accepts `fn ($p) => $p->title->startsWith('x')` passed to `Expression<callable(TitleProbe): bool>`. Emit then reports TYHP5024 `Cannot emit an expression tree for this fn: function call has no tree form`, with the span on the extension body in the tyhpdef rather than the call site. An ordinary instance method emits `methodCall`.
- **Impact:** A query lambda cannot call `tyhp/core` string extensions. Rhetoric cannot translate `startsWith` / `endsWith` / `contains` until the call stays a `MethodCallExpression` (method `startsWith`, receiver type `string`).
- **Suggested fix:** When the call sits inside an expression-tree lambda, skip the extension splice and emit `MethodCallExpression`. This also needs tests and the expression-tree docs. Not a one-line change.
- **Status:** Resolved. An extension call inside an expression-tree lambda stays a method call until the tree is built (`AliasConverter` notes those lambdas and skips the splice). `SpliceExpressionClosure` then splices the backing closure, so `compile()` still runs the extension body. Test: `tests/Tyhp.Tests/Emitter/ExpressionTreeExtensionMethodTests.cs`.
- **Severity:** Critical — the stdlib extension path has no tree form, so a package that queries through those methods cannot compile.

### 13. `illuminate-database` tyhpdef instantiates `Eloquent\Builder<mixed>`

- **Where:** `tyhp-runtime-src/packages/tyhpdef/illuminate-database/13.32.0/_tyhpdef/illuminate.database.tyhpdef` (generated Layer 1). `Eloquent\Builder` is `Builder<TModel extends Model>`. The file uses `Builder<mixed>` in 66 signatures. Laravel's docblocks spell these `Eloquent\Builder<*>` (for example `Query\Builder::orderBy`), and `PhpDocTypeParser` (`Tyhp/Domain/Services/PhpDoc/PhpDocTypeParser.cs`, the `*` case in `Emit`) turns the wildcard into `mixed` without looking at the template bound.
- **Issue:** Checking `$query->orderBy('id')` reports TYHP4035: `Type mixed does not satisfy constraint Illuminate\Database\Eloquent\Model`, with the span on the type argument in the tyhpdef (line 20300). Repro: `tyhp-runtime-src/dev-docs/rhetoric-spikes/illuminate/orderby/OrderBy.tyhp`; run `dotnet tyhp.dll lint --file=orderby/OrderBy.tyhp` from `tyhp-runtime-src/dev-docs/rhetoric-spikes/illuminate`. A signature is only checked when a call uses it, so files that never call one compile.
- **Impact:** Any call to a signature containing `Eloquent\Builder<mixed>` fails to compile. Each of these public `Query\Builder` methods fails when called: `orderBy`, `orderByDesc`, `from`, `selectSub`, `fromSub`, `joinSub`, `joinLateral`, `leftJoinLateral`, `leftJoinSub`, `rightJoinSub`, `crossJoinSub`, `straightJoinSub`, `whereBetween`, `orWhereBetween`, `whereNotBetween`, `orWhereNotBetween`, `whereExists`, `orWhereExists`, `whereNotExists`, `orWhereNotExists`, `union`, `unionAll`, `insertUsing`, `insertOrIgnoreUsing`. `where`, `whereNull`, and `whereIn` are fine. Fixing only `orderBy` leaves the next call broken.
- **Suggested fix:** Make the wildcard resolve to the template's bound (here `\Illuminate\Database\Eloquent\Model`) when the generator emits it, then regenerate the tyhpdef. That regeneration replaces Layer 1, so hand edits to the file (such as the `PendingHasThroughRelationship` arity fix) have to be re-applied. A checker alternative is to let `mixed` satisfy a bound when the type argument sits in a `.tyhpdef`; that also needs a check that `Eloquent\Builder<Post>` is assignable to `Eloquent\Builder<mixed>`. A one-method edit is not a correct fix for the other 65 sites.
- **Status:** Resolved. `GenericTypeArgumentValidator.ValidateUserConstraint` treats a `mixed` type argument as satisfying an `extends` bound when the report node belongs to a `.tyhpdef`. A `.tyhp` file is still checked. `G<Post>` remains assignable to `G<mixed>` through `TypeComparer.Subtyping.IsCovariantToMixedGenericArg`. The illuminate tyhpdef was not regenerated. Test: `tests/Tyhp.Tests/Checker/TyhpdefWildcardConstraintTests.cs`.
- **Severity:** Critical — a runtime package that calls `orderBy` cannot compile against this tyhpdef.

### 14. Object `with` lists accept unkeyed and spread entries without checking them

- **Where:** `Tyhp/TyhpLang/Checker/Rules/WithKeywordRule.cs` → `ValidateWithProperties` skips every pair whose key `GetPropertyName` cannot resolve (`if (propertyName is null) continue;`), so an unkeyed entry (`[5]`) or a spread (`[...$src]`) is never checked, and its value type is never compared with a property. `Tyhp/TyhpLang/Emitter/WithKeywordHelper.cs` → `CreateOverrideArray` then gives an unkeyed entry the key `''` and passes spreads through. Runtime: `tyhp-runtime-src/packages/tyhp/core/tyhp_src/ObjectHelper.tyhp` (`ObjectHelper::with`).
- **Issue:** With `class Point { public int $x = 0; public int $y = 0; }`, all of these compile with no diagnostic:

  ```tyhp
  new Point() with ['not an int'];
  clone $p with [7];
  $p with [9];
  new Point() with [...$src];          // array<string, string> $src
  clone $p with [...$src, x => 1];
  ```

  Emitted PHP (8.4 target): `\Tyhp\ObjectHelper::with(new Point(), ['' => 'not an int'])`, `\Tyhp\ObjectHelper::with(clone $p, ['' => 7])`, `\Tyhp\ObjectHelper::with(new Point(), [...$src])`. On an 8.5 target the clone forms emit native `clone($p, ['' => 7])` and `clone($p, [...$src, 'x' => 1])`.
- **Impact:**
  - `ObjectHelper::with` throws `PropertyNotFoundException` for the `''` key, and for any spread key that is not a property.
  - Native `clone()` on PHP 8.5 does not throw for `''`: it creates a dynamic property named `''` and only raises a deprecation (checked on PHP 8.5.12), so the value is silently lost.
  - Spread values are never type-checked, so a wrong type surfaces as a runtime `TypeError` (`Cannot assign string to property P::$x of type int`) instead of a compile error.
- **Decision (2026-11-04):** A spread in an object `with` list is allowed when the checker can verify it, and an error otherwise. Unkeyed entries are always an error.
- **Fix:**
  - **Unkeyed entries:** error, with the same message as Story 22.2's provisional `CheckerStructEntryUnkeyed`, generalized to properties (`name => value`).
  - **Spread source:** must have a struct type (named, generic instance, or anonymous), as in Story 22.2 rule S1. `array`, `array<K, V>`, `mixed`, nullable structs, and unions are errors, because their keys cannot be checked against properties.
  - **Spread fields:** each field's PHP array key (its alias, if it has one, such as `'status_code'`) must name a property of the target, through `extends`. Each field type must be assignable to that property. These are Story 22.2 rules S2 and S3, matched by the array key because that is the property name the runtime assigns.
  - **Same rules as keyed entries:** each spread field also goes through the checks a keyed entry gets today, including `ValidateReadonlyWith` for readonly properties.
  - **Order:** entries and spreads apply left to right, and a later one overrides an earlier one (rule S6).
  - **Emit:** both paths (`ObjectHelper::with` and native PHP 8.5 `clone()`) keep emitting the spread as written; only the checker changes. Add fixtures for 8.4 and 8.5 targets.
  - Implement alongside Story 22.2 so struct and object `with` lists share the unkeyed-entry diagnostic and the spread checks.
- **Status:** Ready. Not started. Story 22.2 covers struct `with` lists; this entry covers object `with` lists.
- **Severity:** Critical — on a PHP 8.5 target, `clone $p with [7]` silently writes a dynamic property instead of failing; on other targets the checker accepts code that always throws.

---

## NEEDS INPUT

### 3. `tyhpGenericObjectInitInterface` is never emitted

- **Where:** `runtime/packages/core/tyhp_src/Concerns/GenericObject.tyhp` (declares
  `tyhpGenericObjectInitInterface` and the `$__tyhpInterfaceGenerics` backing field); no producer
  anywhere under `Tyhp/`.
- **Issue:** The `GenericObject` trait supports recording per-interface generic arguments, but the
  emitter never calls it — a repo-wide search finds the method used only in hand-written examples
  (`Examples/Generics.php`). Only the class's own generic arguments are recorded, via
  `tyhpGenericObjectInit`.
- **Impact:** Runtime type information for `class Foo implements Bar<int>` does not record the `int`,
  so any future runtime check or reflection over interface generic arguments has nothing to read.
  Latent rather than actively breaking, since nothing consumes it yet.
- **Why blocked:** No current feature depends on it; emitting it correctly requires resolving
  interface generic arguments through the inheritance chain, including transitively implemented
  interfaces. The method was left on its own `$__tyhpInterfaceGenerics` field rather than folded
  into `$__tyhpGenerics`; an interface name is just another key, so emit-or-delete is a small
  change either way.
- **Suggested fix:** When emitting the generic prologue, also emit one
  `tyhpGenericObjectInitInterface('<interface>', …)` call per implemented interface that carries
  generic arguments. Alternatively remove the trait method until a consumer exists, so the runtime
  surface does not advertise unimplemented behavior.
- **Status:** NEEDS INPUT / Deferred (no consumer). Emit-or-delete when a consumer appears (or
  decide to delete the trait method now). Not Story 22+; leave until a consumer needs interface
  generic args at runtime, or until product chooses to remove dead API surface.
- **Severity:** Low — latent; nothing reads interface generic args at runtime yet.

### 5. Flare 1.1.2 typechecks against newer Illuminate and Livewire tyhpdefs than its lock

- **Where:** `tyhp-runtime-src/packages/tyhpdef/spatie-laravel-flare/1.1.2`,
  `tyhp-runtime-src/packages/tyhpdef/spatie-error-solutions/1.1.3`, and
  `tyhp-runtime-src/packages/tyhpdef/spatie-ignition/1.16.0`. Composer now installs real `illuminate/*`
  v12.69.2 because flare 1.1.2 requires `illuminate/support` `^11.0|^12.0`. No Illuminate
  11 or 12 tyhpdef tree exists, so `tests/tyhp.json` and `extra.tyhp.require` still include
  Illuminate 13.32.0 component tyhpdefs and `laravel-framework/13.32.0/composer.json`. Flare
  1.1.2 also depends on `livewire/livewire` `^3.6` and calls `LivewireManager::getClass()`
  and `Livewire\Mechanisms\ComponentRegistry`. The only harvest is `livewire-livewire/4.4.5`,
  which does not declare those members.
- **Issue:** Lint can succeed against types the installed packages do not use.
  `Flare::handles()` calls `Exceptions::reportable(callable)`, and the 13.32 tyhpdef still
  types that as `reportable(callable): ReportableHandler`.
- **Impact:** A green lint does not mean the 1.1.2 signatures match Illuminate 12 or
  Livewire 3.
- **Suggested fix:** Harvest Illuminate 12 (the lock’s v12.69.2 components these packages
  reference) and Livewire 3.6, then point these three test projects at those tyhpdefs. Do
  not start that harvest until this entry is decided.
- **Status:** NEEDS INPUT
- **Decision needed:** Whether to harvest Illuminate 12 (the lock’s v12.69.2) and Livewire 3.6, then point the flare, error-solutions, and ignition test projects at those tyhpdefs.
- **Severity:** Medium — lint passes; the version skew stays invisible.

### 11. No list type for `array_is_list` / `Arrays::isList` narrowing

- **Where:** Tyhp type grammar (`list` is reserved as the PHP `list()` function name in
  `PhpReservedFunctionNames`, not a type). `array<T>` is documented as shorthand for
  `array<int|string, T>`, which is not a 0-indexed list. `Nette\Utils\Arrays::isList` in
  `tyhp-runtime-src/packages/tyhpdef/nette-utils/4.1.5` returns `($value is list ? true : false)` in PHPDoc
  and is implemented with `is_array && array_is_list`.
- **Issue:** A type-guard return cannot name a list. `$value is array` would be wrong
  (`isList` is false for associative arrays). `$value is array<int, mixed>` is also wrong:
  int keys are not required to be `0..n-1` without gaps.
- **Impact:** Callers of `Arrays::isList` (and any similar `array_is_list` wrapper) stay
  on `bool`. The true branch cannot narrow the subject to a list.
- **Suggested fix:** A list type, or a type-guard form that means `array_is_list`, distinct
  from `array` and from `array<int, T>`.
- **Status:** Resolved — stay boolean; do not add a list type or array subtype. Narrowing is intentionally not supported for now because `list` is reserved and the need is small.
- **Severity:** Medium — lost narrowing only; the boolean result is still correct.

---

## Deferred (Story 22+ / Story 31)

### 1. Runtime self-host: committed output layout stale after packages→`dist`

- **Where:** `SelfHostRuntimeConformanceTests.SelfHost_RecompiledRuntime_MatchesCommittedPhp`;
  `SelfHostRunner` still expects golden PHP under `runtime/packages/<pkg>/src`.
- **Issue:** Infrastructure failure `missing committed src at …/<pkg>/src` — package emit now goes
  to `runtime/packages/dist/<pkg>/<version>/src`; unversioned `packages/*/src` is gone. Self-host
  cannot diff recompiled output against the old path.
- **Skipped:** that Fact (message references this item).
- **Not about tyhpdefs / project `include`:** self-host is a **golden emit check** (rebuild
  `tyhp_src` → diff previously committed PHP). Normal Tyhp projects get runtime types via `include`
  / Composer `package.tyhpdef` — they do **not** need committed package PHP trees for typechecking.
  Do not restore unversioned `src/` solely to satisfy this harness.
- **Decision (2026-08-12):** Follow `ROADMAP.md` Tier 2 phasing (“Runtime packages: test consumption &
  self-host phasing”).
  1. **Now:** either retarget `SelfHostRunner` golden compare to `dist/…/src` **or** keep the Fact
     skipped / lightly rewrite until the packages home is settled — low investment preferred if a
     separate runtime repo is imminent.
  2. **After packages leave / Packagist (~Story 21+):** move the golden self-host check to the
     **runtime** repo (or its CI), pinning a compiler version; do not keep forever-on-disk
     compiler-suite diffs against local `dist/`.
- **Status:** Deferred (Story 27.3) — **kept skipped (low investment)**. Full `SelfHostRunner` retarget to
  `dist/…/src` is larger than warranted while packages may leave the compiler repo; revisit when the
  runtime home is settled or when un-skipping is needed for CI signal.
- **Severity:** Low — skipped golden-harness path only; user compiles and type-checks are unaffected.

### 2. `PhpUnit_RuntimePackages_AllPass` still skipped

- **Where:** `Integration.TyhpLibPhpTests.PhpUnit_RuntimePackages_AllPass`.
- **Issue:** Test still **skipped**; PHPUnit reports errors from stale/inconsistent compiled PHP in
  mid-reorg runtime packages.
- **Status:** Deferred (Story 27.3) — left skipped. Un-skipping still fights the packages→`dist` migration
  (Composer/PHPUnit layout + package compile health, including remaining async `Promise.tyhp`
  checker errors once `TResult` is renamed). Not a clean one-shot fix; revisit after runtime
  package home / dist consumption is settled.
- **Severity:** Low — skipped integration test only; not a compile or emit failure on its own.

### 4. `default(<class type>)` is typed as the class but emits `null`

- **Where:** `Tyhp/TyhpLang/Checker/TypeInferrer.Expressions.cs` (`TyhpDefaultAst` resolves to the
  spelled type expression), `Tyhp/TyhpLang/Emitter/TyhpEmitter.Expressions.cs` →
  `BuildDefaultExpression` (class types fall into the `_ => "null"` catch-all).
- **Issue:** `default(MyClass)` still emits the literal `null`. Stage 1 already makes the checker
  treat a class-type `default` as the null type so the existing incompatibility / null-safety
  machinery reports it (matching what the emitter produces). Stage 2 is still open: without
  `operator default()`, `return $instance ?? default(MyClass)` against a non-nullable return is a
  type error rather than a constructed instance, and that is the entire point of writing
  `?? default(MyClass)`.
- **Why deferred:** Author-supplied defaults for object types are Story 34
  (`operator default()`).
- **Stage 2 (Story 34):** When a class declares `operator default(): self`, infer
  `default(<that class>)` as the class type again and emit the call. Absent the operator the
  default stays `null` and Stage 1's error stands.
- **Status:** Deferred (Story 34, now in Tier 3). Stage 2 waits on `operator default()`.
- **Severity:** Medium — class-typed `default` still emits `null`; Stage 1 already type-errors the
  useful `?? default(MyClass)` shape. Stage 2 is the feature.

