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

### 6. `Process::runAsync` throws: emitted `ProcessRun` reads struct fields with `->` on an array

- **Where:** `tyhp-runtime-src/packages/tyhp/io/tyhp_src/Process/ProcessRun.tyhp` (reads
  `$this->options->cwd`, `->env`, `->input`, `->timeoutMs` on the promoted
  `private readonly ProcessOptions $options`). Emitted
  `packages/dist/tyhp-io/805.0.4/src/Tyhp/Io/Process/ProcessRun.php` lines 49, 63, 87, 88, and 110
  keep `->` even though `$options` is emitted as `array` (a struct). Other struct reads in the
  same package emit `['key']` (`$result['exitCode']` in `Process::mustRunAsync`).
- **Issue:** `Process::runAsync(['php', '-v'])` on PHP 8.5.12 against dist `805.0.4` emits
  `Warning: Attempt to read property "input" on array`, then throws
  `TypeError: substr(): Argument #1 ($string) must be of type string, null given`
  (`ProcessRun.php:111`). Passing an explicit options array
  (`['input' => '', 'timeoutMs' => 0]`, or with `cwd` and `env` as `null`) throws the same way.
  No call to `runAsync` or `mustRunAsync` succeeds, so a property read of a struct-typed
  promoted constructor property is not lowered to array access in the emitter.
  The Composer `autoload.files` of the `tyhp/io` `805.0.4` dist lists no
  `src/Tyhp/Io/Process/_functions.php`, so a plain `composer require` caller first gets
  `InvalidTypeException: Unknown type declaration "Tyhp\Io\Process\ProcessResult"`; the
  `TypeError` above is what remains once that file is loaded. The io dist was emitted at 15:59 on
  2026-11-06, before the async and core re-emit; re-emit `tyhp/io` before judging the `->` read.
- **Impact:** The `Process` API in `tyhp/io` is unusable from PHP. The PHP docs page
  `php_package_io.md` must not present `Process::runAsync` as a working call until this is fixed.
- **Suggested fix:** Find why member access on a struct-typed `$this->property` is emitted as
  `->` when a local or parameter of the same struct type emits `['key']`, fix the emitter, and
  re-emit `tyhp/io`. Add an emitter test that reads a struct field through a promoted
  constructor property.
- **Status:** Ready
- **Severity:** High — a package API a user would call first fails on every call, and there is no
  working call form.

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
