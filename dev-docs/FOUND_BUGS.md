# Found Bugs / Issues

This file tracks **unresolved** issues discovered during code review/audits. Each entry notes the
audit context and a timestamp.

Resolved issues are archived locally in `RESOLVED_BUGS.md` (gitignored; not in the public tree).

Status tags used here:

- **Open** — eligible to implement (or decided, ready for implementation).
- **NEEDS INPUT** — blocked on a user/design decision; do not implement until decided.
- **Deferred (Story 16+)** — intentionally out of scope until Story 16 or later (includes Story 21 / 31).
- **Deferred (no consumer)** — latent / no current consumer; revisit when a consumer appears (not Story 16+).

---

## Audit: Story 19.5 Phase 11 review (PhpStorm LSP client) — 2026-08-19

### 1. `verifyPlugin` fails on internal API usage in TextMate/plugin-install path (Phase 9/10, not Phase 11)
- **When found:** 2026-08-19 (Phase 11 review; ran `./gradlew verifyPlugin` against the real
  `PS-262.9437.196` platform as an extra check beyond the requested `unitTest` / `buildPlugin`).
- **Where:** `tyhp-lang/phpstorm/src/main/kotlin/com/tyhp/lang/textmate/TyhpTextMateBundleSupport.kt`
  — `pluginInstallBundlePath()` calls `com.intellij.ide.plugins.PluginManagerCore.getPlugin(PluginId)`,
  which is annotated `@ApiStatus.Internal`.
- **Issue:** `./gradlew verifyPlugin` (Plugin Verifier) reports `Compatible` overall but **fails the
  task** with `[INTERNAL_API_USAGES]` for this one call (plus unrelated deprecated/experimental
  `ToolWindowFactory` overrides in `TyhpLspToolWindowFactory` that are warnings only, not failures).
  `unitTest` and `buildPlugin` (the two tasks this review was asked to run) both succeed — `verifyPlugin`
  is not wired into either of those and was not requested — but a future CI job that does run
  `verifyPlugin` (or `check`, if it's ever added there) will fail on this.
- **Why not fixed now:** `TyhpTextMateBundleSupport` is Phase 9/10 (TextMate bundle registration /
  plugin-local install path resolution), not Phase 11 (LSP client) — out of scope for this review.
  The fix is presumably to resolve the plugin's install directory via a public API (e.g. the
  `PluginDescriptor`/`IdeaPluginDescriptor` already available to the bundle provider instance, or
  `PluginManager.getInstance().findEnabledPlugin(...)` if that's public in 2026.2) instead of the
  internal `PluginManagerCore.getPlugin`.
- **Status:** Open.

---

## Audit: Story 19.5 Phase 4 review (VS Code LSP client) — 2026-08-19

### 1. VSIX packages unbundled per-file `out/**/*.js` alongside the esbuild bundle
- **When found:** 2026-08-19 (Phase 4 review; inspected `vsce ls`/`npm run package` output).
- **Where:** `tyhp-lang/vscode/.vscodeignore` — excludes `src/**`, `**/*.ts`, `out/**/*.map`,
  `out/**/*.test.js`, but not the plain `tsc`-compiled (non-bundled) `.js` files under
  `out/binary/**`, `out/config/**`, `out/lsp/**`, `out/status/**`.
- **Issue:** `npm run package` (`vsce package`) ships both the esbuild bundle
  (`out/extension.js`, ~805 KB, self-contained incl. `vscode-languageclient`) **and** the
  redundant unbundled per-module compile output (`out/binary/*.js`, `out/config/*.js`,
  `out/lsp/*.js`, `out/status/*.js`, ~78 KB total) in the VSIX. The extension's `main` is
  `./out/extension.js` (the bundle), so the extra per-module files are never loaded at
  runtime — just dead weight in the package.
- **Why not fixed now:** Predates Phase 4 — the same gap already applies to `out/binary/**`
  and `out/config/**` since the 0.4.0 (Phase 3) binary-manager work. Fixing `.vscodeignore`
  (e.g. add `out/binary/**`, `out/config/**`, `out/lsp/**`, `out/status/**` while still
  shipping `out/extension.js` and `out/extension.js.map`) touches packaging for every
  phase's compiled output, not just Phase 4, so it's out of this review's scope.
- **Status:** Open.

---

## Audit: Story 19 Phase 8 review (code actions, formatting, selection range) — 2026-08-18

### 1. `DocumentAnalysisTests.RapidDidChange_DebouncesToSingleSettledAnalysis` is timing-flaky under full-suite load
- **When found:** 2026-08-18 (Phase 8 review; full-suite regression run after Phase 8 fixes).
- **Where:** `tests/Tyhp.Tests/LanguageServer/DocumentAnalysisTests.cs:240` — asserts
  `(afterTyping - afterOpen) >= 1` analysis passes after a debounce window elapses.
- **Issue:** Failed once during a full-suite run (`afterTyping - afterOpen` was `0`) but passed immediately when
  re-run in isolation. This is `IncrementalAnalyzer`/`AnalysisService` debounce-timing territory from Phase 3, not
  Phase 8 — none of the Phase 8 changes (`CodeActionEngine`, `DocumentFormatter`, `SelectionRangeCollector`,
  `CapabilityRegistration`) touch debounce scheduling. Looks like a fixed-delay timing assumption that can miss under
  CPU contention from the rest of the suite running in parallel.
- **Why not fixed now:** Out of scope for the Phase 8 review this entry comes from; needs its own look at whether the
  test should poll/wait instead of asserting a hard count after a fixed sleep.
- **Status:** Open.

---

## Audit: fresh review of Medium #6 (`final` property hooks) — 2026-08-10

> Fresh-agent review of the Medium #6 fix confirmed it correct and not regressed (findings archived
> in `RESOLVED_BUGS.md`). The `EmittedPhpRunner` dist-path gap from that review is also in
> `RESOLVED_BUGS.md` (fixed 2026-08-12).

---

## Audit: generic structs implementation review — 2026-08-05

> Pre-existing checker limitations the generic-struct work depends on. In-place fixes (`GenericInheritanceBindings.ForLevel`, `build-all-dryrun.sh`) are in `RESOLVED_BUGS.md`. Call-site tyhpdef overload selection for `call_user_func*` (item #2) is also in `RESOLVED_BUGS.md` (Story 16.5 Phase 7, 2026-08-13).


## Suite reds skipped 2026-08-03 (mid-flight WIP / regressions)

> Mid-flight suite-red tracking. Resolved clusters (#2–#15) are in `RESOLVED_BUGS.md`.
> Un-skip each remaining cluster when its root cause lands.


### 1. Runtime self-host: committed output layout stale after packages→`dist`
- **When found:** 2026-08-03 (full suite).
- **Where:** `SelfHostRuntimeConformanceTests.SelfHost_RecompiledRuntime_MatchesCommittedPhp`;
  `SelfHostRunner` still expects golden PHP under `runtime/packages/<pkg>/src` (same mid-reorg as
  Story 11 Phase 2 item #6(b)).
- **Issue:** Infrastructure failure `missing committed src at …/<pkg>/src` — package emit now goes
  to `runtime/packages/dist/<pkg>/<version>/src`; unversioned `packages/*/src` is gone. Self-host
  cannot diff recompiled output against the old path.
- **Skipped:** that Fact (message references this item / #6(b)).
- **Not about tyhpdefs / project `include`:** self-host is a **golden emit check** (rebuild
  `tyhp_src` → diff previously committed PHP). Normal Tyhp projects get runtime types via `include`
  / Composer `package.tyhpdef` — they do **not** need committed package PHP trees for typechecking.
  Do not restore unversioned `src/` solely to satisfy this harness.
- **Update (2026-08-04, supervised emit):** historical note — under the old layout, decimal emit was
  verified byte-identical and core PHP was re-emitted but untracked. Superseded by the `dist/` move.
- **Decision (2026-08-12):** Follow `ROADMAP.md` Tier 2 phasing (“Runtime packages: test consumption &
  self-host phasing”).
  1. **Now:** either retarget `SelfHostRunner` golden compare to `dist/…/src` **or** keep the Fact
     skipped / lightly rewrite until the packages home is settled — low investment preferred if a
     separate runtime repo is imminent.
  2. **After packages leave / Packagist (~Story 21+):** move the golden self-host check to the
     **runtime** repo (or its CI), pinning a compiler version; do not keep forever-on-disk
     compiler-suite diffs against local `dist/`.
- **Status (2026-08-12):** Open — **kept skipped (low investment)**. Full `SelfHostRunner` retarget
  to `dist/…/src` is larger than warranted while packages may leave the compiler repo; revisit when
  the runtime home is settled or when un-skipping is needed for CI signal.
- **Skip message updated** on `SelfHost_RecompiledRuntime_MatchesCommittedPhp` to cite this decision.

## Audit: Property initialization state, `default(T)` and runtime generic tracking

> Deferred runtime/checker gaps from generic-tracking / property-init design work. Mechanisms A–D and items 1–8 are in `RESOLVED_BUGS.md`.


### 9. `tyhpGenericObjectInitInterface` is never emitted

- **When found:** 2026-07-28 (property definite-assignment design audit).
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
- **Why not fixed now:** No current feature depends on it; emitting it correctly requires resolving
  interface generic arguments through the inheritance chain, including transitively implemented
  interfaces.
- **Suggested fix:** When emitting the generic prologue, also emit one
  `tyhpGenericObjectInitInterface('<interface>', …)` call per implemented interface that carries
  generic arguments. Alternatively remove the trait method until a consumer exists, so the runtime
  surface does not advertise unimplemented behavior.
- **Decision (2026-07-28):** **Deferred**, lowest priority — nothing consumes interface generic
  arguments today and it is latent rather than breaking. Revisit only when a consumer appears; if
  nothing needs it by then, prefer **removing** the trait method so the runtime surface stops
  advertising unimplemented behavior.
- **Note (2026-07-29):** item 11 landed, so the per-declaring-class map now exists.
  `tyhpGenericObjectInitInterface` was deliberately left on its own `$__tyhpInterfaceGenerics` field
  rather than folded into `$__tyhpGenerics`: with no producer and no consumer, merging them would only
  move dead code. Whichever way this item is eventually settled — emit it or delete it — the merge is now
  a few lines, since an interface name is just another key.
- **Status:** Deferred (no consumer). Revisit emit-or-delete when a consumer appears. Not Story 16+; leave until a consumer needs interface generic args at runtime.

### 10. `default(<class type>)` is typed as the class but emits `null`

- **When found:** 2026-07-28 (design review follow-up to this audit).
- **Where:** `Tyhp/TyhpLang/Checker/TypeInferrer.Expressions.cs` (`TyhpDefaultAst` resolves to the
  spelled type expression), `Tyhp/TyhpLang/Emitter/TyhpEmitter.Expressions.cs` →
  `BuildDefaultExpression` (class types fall into the `_ => "null"` catch-all).
- **Issue:** The same catch-all that breaks generics (item 4) also covers **object types**.
  `default(MyClass)` is inferred as the non-nullable type `MyClass` but emits the literal `null`, so
  the checker believes it has an instance where the runtime has null. Unlike item 4 this is not merely
  a wrong value — it defeats null-safety entirely, because the resulting expression is typed
  non-nullable and therefore never checked again.

  Reproduced with **0 checker errors**:

  ```tyhp
  class Consumer {
      public static function nonNullableReturn(?MyClass $instance = null): MyClass {
          return $instance ?? default(MyClass);
      }
      public static function assignToNonNullable(): int {
          $x = default(MyClass);
          return $x->n;
      }
  }
  ```

  emits

  ```php
  public static function nonNullableReturn(?\Probe\MyClass $instance = null): \Probe\MyClass {
      return $instance ?? null;
  }
  public static function assignToNonNullable(): int {
      $x = null;
      return $x->n;
  }
  ```

- **Impact:** `return $instance ?? null;` against a non-nullable declared return type throws a
  `TypeError` under `declare(strict_types=1)` whenever the argument is null — which is the entire point
  of writing `?? default(MyClass)`. The second case throws
  `Error: Attempt to read property "n" on null`, having passed a property read on a value the checker
  typed as an object and the emitter made null. `NullSafetyRule` never sees a nullable type, so nothing
  intervenes.
- **Why not fixed now:** The complete answer requires author-supplied defaults for object types (Story
  31 Idea 5, `operator default()`), which is future work. The *soundness* half is small and should not
  wait for it.
- **Decision (2026-07-28):** Two-stage.
  1. **Now:** make the checker treat the default of a class type as **`null`** — that is, infer
     `default(<class type>)` as the null type rather than the class type — so the existing type
     incompatibility machinery reports it. A non-nullable return or assignment target then produces a
     clear error stating the return type does not allow null, and `$x->n` is caught by null safety.
     This matches what the emitter actually produces.
  2. **Later (Story 31 Idea 5):** when a class declares `operator default(): self`, infer
     `default(<that class>)` as the class type again and emit the call. Absent the operator the default
     stays `null` and stage 1's error stands.
- **Suggested fix:** In `TypeInferrer`, special-case `TyhpDefaultAst` over an object type to yield the
  null type (keep the spelled type for scalars and arrays, which fold to real values). Verify the
  resulting diagnostic wording names the class and points at the nullability, since the naive
  "cannot convert null to MyClass" phrasing would not explain *why* the expression is null.
- **Stage 1 (checker treats class-type `default` as null):** done — see `RESOLVED_BUGS.md`.
- **Status:** Deferred (Story 16+ / Story 31 Idea 5). Stage 1 (checker treats class-type default as null) is done; stage 2 waits on `operator default()`.

---

## Audit: Story 11 Phase 2 import-consolidation work (pre-existing emitter test failures observed)

> Pre-existing emitter test failures observed during import-consolidation work (not caused by that work).

### 6. Pre-existing test failures surfaced during item #2/#3 verification (NOT caused by that work)
- **When found:** 2026-07-22 (running the full suite to verify items #2/#3 caused no regressions).
- These fail identically with and without the item #2/#3 fixes; none touch the changed code paths.
- **(b) `Integration.TyhpLibPhpTests.PhpUnit_RuntimePackages_AllPass`** — test still **skipped**; PHPUnit reports errors from stale/inconsistent compiled PHP in mid-reorg runtime packages (autoload path issue fixed 2026-07-22 — see `RESOLVED_BUGS.md`).
- **Status (2026-08-12):** Open — left skipped. Un-skipping still fights the packages→`dist`
  migration (Composer/PHPUnit layout + package compile health, including remaining async
  `Promise.tyhp` checker errors once `TResult` is renamed). Not a clean one-shot fix in this
  session; revisit after runtime package home / dist consumption is settled.


---

## Audit: Phases 3–4 of Story 10 (Output Writer Service + Composer JSON Service)

> Reviewed Phase 3 (`Tyhp/Domain/Services/OutputWriterService.cs` — disk writer, path-conflict
> detection, dry-run, sourcemap-URL append) and Phase 4 (`Tyhp/Domain/Services/ComposerJsonService.cs`
> — `composer.json` generate/merge, PSR-4 mapping derivation, `autoload.files`, runtime-package
> detection), plus their `BuildAction` Step 9 wiring and the new `OutputWriterServiceTests` /
> `ComposerJsonServiceTests`. The implementation is sound: it compiles, the 8 new `Category=Build`
> tests pass (full suite green at 295 passed / 1 skipped in the concurrent agent's run), the
> output-directory resolution is consistent between file writes (`OutputWriterService.ComputeOutputPath`)
> and `composer.json` placement (`BuildOutputCleaner.ResolveOutputDirectory`), and both now resolve
> against `Project.GetProjectPath()` — which fixes the CWD-relative output bug logged under the Story 09
> Phases 7–8 audit (item #2) and the Story 10 Phase 2 audit (item #3) for the *temporary* writer.
>
> Two clear, in-scope bugs were **fixed in place** during this audit (not listed below):
> 1. **Line-ending inconsistency in the sourcemap-URL append.** `OutputWriterService.AppendSourceMappingUrl`
>    injected `Environment.NewLine` (`\r\n` on Windows) into PHP content that `PHPOutputFile.Generate()`
>    deliberately normalizes to `\n`, producing mixed line endings on non-Unix platforms (and
>    non-deterministic output for golden fixtures). Changed to a literal `"\n"`.
> 2. **Malformed existing `composer.json` could crash the build.** `ComposerJsonService.MergeAutoloadSection`
>    read existing `autoload.files` entries via `node?.GetValue<string>()`, which throws
>    `InvalidOperationException` for any non-string array element (e.g. a number/object a user hand-edited
>    in). That throw is outside the method's only try/catch (which wraps just the final write), so a
>    malformed input file would crash the whole build, violating the "build should never crash" guidance.
>    Replaced with a defensive `JsonValue.TryGetValue<string>` helper that skips non-string entries.

### 1. Runtime-package `require` constraints use the compiler assembly version (published registry deferred)
- **When found:** 2026-06-22 (Story 10 Phases 3–4 review).
- **Where:** `Tyhp/Domain/Services/ComposerJsonService.cs` — `MergeRequireSection` /
  `GetCompilerVersionConstraint`.
- **Issue:** `require` entries for `tyhp/core`, `tyhp/decimal`, `tyhp/async` use a version constraint
  derived from the compiler assembly version, not independent runtime-package semver. Published
  `composer install` against a registry remains unresolved.
- **Disposition:** Interim local `path` repositories (2026-06-23) are done — see
  `RESOLVED_BUGS.md`. Full Packagist/registry versioning is deferred to Story 21.
- **Status:** Deferred (Story 16+ / Story 21).
