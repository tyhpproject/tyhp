# Incomplete Items — Stories 01–14

> **Audit date:** 2026-07-31 (checker gap pointer refreshed 2026-08-06)  
> **Scope:** `IMPLEMENTATION_PLAN_TODO_STORY_{01–14,08.5,10.5}.md` verified against the codebase.  
> **Method:** Story docs + `FOUND_BUGS.md` / `RESOLVED_BUGS.md` cross-check + symbol/fixture inventory under `Tyhp/`, `tests/`, `runtime/`, `Tyhp/TyhpLang/Grammar/`.  
> **Bug-file cross-check:** Re-run 2026-07-31 — each item below notes its FOUND / RESOLVED match (or “not tracked”).  
> **Checker capability gaps:** Ongoing checker inventory (checker-first; emitter/call-site noted when relevant to checker intent) lives in [`CHECKER_GAPS.md`](CHECKER_GAPS.md) — updated 2026-08-06. This file keeps story-level incompleteness; see CHECKER_GAPS for the living matrix.  
> **Not in scope:** Story 15+ incompleteness (except when a 01–14 item explicitly defers to a later story).

Many story plans still show unchecked phase acceptance boxes even though the work landed (especially Stories 06–10). Those stale checkboxes are **not** repeated here. This file lists items that are still **actually unfinished**, deferred, or known broken relative to story acceptance.

---

## Summary

| Story | Overall | Incomplete themes |
|-------|---------|-------------------|
| 01 | Complete | None material |
| 02 | Substantially complete | Tyhpdef integrity duplicate-check placeholder |
| 03 | Complete | Bare `>` operator overload grammar gap resolved 2026-07-31 |
| 04 | Substantially complete | Coverage ≥80% gate unverified; self-host of runtime still not green |
| 05 | Complete | None material |
| 06 | Complete | Doc checkboxes stale only |
| 07 | Substantially complete | Phase 10 CI/CD deferred; runtime self-host milestone |
| 08 | Substantially complete | Trait dynamic-property binding; residual generics edge cases — see [`CHECKER_GAPS.md`](CHECKER_GAPS.md) |
| 08.5 | Substantially complete | Fixture breadth (struct/`__MethodName` utilities) — see Story 10.5 residual / [`CHECKER_GAPS.md`](CHECKER_GAPS.md) |
| 09 | Substantially complete | Stale emitter `OutputFileWriter` placeholders (clean/dry-run live in `OutputWriterService`) |
| 10 | Substantially complete | `MaxErrorsPerFile` config wiring; stale placeholders |
| 10.5 | Complete (13/13) | Residual fixture coverage for struct/`__MethodName` utilities |
| 11 | Substantially complete | PHP&lt;8.4 property-accessor rewrite; several call-site edge cases |
| 12 | Substantially complete | Real auto-fixes; cross-file `--file` |
| 13 | Complete (phases) | Open CLI polish bugs |
| 14 | Complete (phases) | Open diagnostic-quality bugs |

---

## Story 01 — Foundation (diagnostics, compilation pipeline)

**Status in plan:** COMPLETED.

No incomplete story items. Diagnostic bag, `CompilationService`, build/lint skeletons, localization, and formatting are present under `Tyhp/Domain/Diagnostics/` and `Tyhp/CLI/`.

---

## Story 02 — Binder

**Status:** Substantially complete (`TyhpBinder`, scopes, symbols, tyhpdef loading).

### Incomplete

1. **Integrity check — cross-file tyhpdef duplicate declarations**  
   - **Where:** `Tyhp/CLI/IntegrityChecks/TyhpdefCheck.cs` still has `// PLACEHOLDER_STORY_02: Check for duplicate declarations across tyhpdef files`.  
   - **Bug files:** Not tracked in FOUND/RESOLVED (distinct from Story 13 Phase 7 malformed-tyhpdef NRE, which *is* in FOUND).  
   - **To finish:** After binder/tyhpdef registration, scan registered symbols for duplicate FQNs across tyhpdef sources and report diagnostics (align with binder duplicate handling / **8025** where applicable).

---

## Story 03 — Extension operator overloads & tyhpdef inline extensions

**Status:** Complete (grammar/AST/visitor/binder + Story 11 emit; bare `>` overloadable as of 2026-07-31).

### Incomplete

None material. Former gap (bare `>` in `tyhpClassOperatorOverloadOp`) is in [`RESOLVED_BUGS.md`](RESOLVED_BUGS.md)
(ExtDecimal audit §1, Fixed 2026-07-31).

---

## Story 04 — Runtime library modules

**Status:** Substantially complete — packages `core` / `decimal` / `async` / `lambda` with `tyhp_src/`, committed `src/`, and PHPUnit tests under `runtime/packages/*/tests/`.

### Incomplete / unverified

1. **Code coverage ≥ 80% acceptance gate**  
   - Coverage is configured in `runtime/phpunit.xml`, but this audit did **not** confirm ≥80% measured coverage for all packages.  
   - **Bug files:** Not tracked in FOUND/RESOLVED.  
   - **To finish:** Run PHPUnit with coverage and close gaps, or revise the acceptance bar if the gate is no longer desired.

2. **Runtime self-host (“compiler builds its own runtime”)**  
   - Owned jointly with Story 07; `SelfHostRunner.ExpectedToCompileAllowlist` is still empty and packages are not yet assertable as self-compiling.  
   - **Bug files:** Mentioned in FOUND (Story 10.5 Phase 6 review — allowlist empty by design for that phase) and RESOLVED archive of Story 07 Waves A2&B (vacuous self-host pass was deferred to 10.5 scaffolding). **Neither file claims the full self-host milestone is done.**  
   - **To finish:** Get each `runtime/packages/*/tyhp_src` compiling via `tyhp build`, allowlist packages, and keep golden `src/` diffs green.

3. ~~**`using` block emitter acceptance (Phase 10 checklist in Story 04)**~~ — **DONE**  
   - Spec’d in Story 04, implemented in Story 11 (`UsingBlockEmitterTests`, `TyhpEmitter` disposables/using). Verified: `UsingBlockEmitterTests` green. Leftover unchecked boxes in the Story 04 plan (if any) are stale.

---

## Story 05 — Bind symbols to AST nodes

**Status:** Complete — `IBase2Ast.BoundSymbol` / `Base2Ast.BoundSymbol` exist and are used by binder, checker, and emitter.

No incomplete story items.

---

## Story 06 — Built-ins, grammar fixes, tagless mode

**Status:** Complete — verified: `tyhpGenericParameterDeclaration` / `tyhpGenericTypeArgument` split; tagless lexer/parser modes; `LoadPackageTyhpdefs` / `TyhpdefSymbolRegistrar`; built-in utilities/`nameof`/`typeof`; `tests/conformance/story06/tagless/`; `OLD_Tyhpdef.cs` removed.

No incomplete story items (phase checkboxes in the plan doc remain stale).

---

## Story 07 — Testing infrastructure & conformance

**Status:** Waves A/B substantially present (`tests/Tyhp.Tests` categories Parser/Diagnostics/Binder/Checker/Emitter/EndToEnd/Integration/Conformance/CLI/Lint/PHP; `tests/conformance/` harness).

### Incomplete

1. **Phase 10 — CI/CD pipeline (explicitly DEFERRED)**  
   - No `.github/workflows` in the repo.  
   - **Bug files:** Not tracked in FOUND/RESOLVED.  
   - **To finish (when ready):** GitHub Actions on push/PR for `dotnet test`, optional PHPUnit job, coverage report, category filters documented in `tests/readme.md`.

2. **Runtime self-host conformance milestone**  
   - `SelfHostRuntimeConformanceTests` + allowlist hardening exist (Story 10.5), but allowlist is empty and no package is asserted as compiling.  
   - **Bug files:** Same as Story 04 item 2 — scaffolding tracked; full milestone not claimed resolved.  
   - **To finish:** Same as Story 04 item 2 — make runtime packages compile and allowlist them.

3. **Stale `PLACEHOLDER_STORY_07` comments**  
   - Comments in `TyhpChecker.cs` / `CompilationService.cs` still say unit tests are placeholders even though `Category=Checker` tests exist. Cosmetic cleanup only.  
   - **Bug files:** Not tracked in FOUND/RESOLVED.

---

## Story 08 — Checker (flagship)

**Status:** Substantially complete — large rule set, `TypeComparer*`, `TypeInferrer*`, pipeline wiring, many tests.

### Incomplete (acceptance-critical)

1. ~~**Call-argument / named-argument validation largely unreachable**~~ — **DONE / RESOLVED**
   (Fixed 2026-08-03; `ResolveFreeFunction` / `instanceChain.Base` / constructor args;
   TYHP4010 + 4079–4081/4096; `CallArgumentValidationTests`).  
   - **RESOLVED_BUGS:** Fixed 2026-08-03 — Call-argument validation never runs.

2. ~~**`mixed` use-site enforcement incomplete**~~ — **DONE / RESOLVED** 2026-08-04 (Top-type #1;
   TYHP4160). Residual core dry-run TYHP4160 on property-accessor handler traits → FOUND **#1c**
   (deferred to HasPropertyAccessors / polyfill WIP).
   - **RESOLVED_BUGS:** Fixed 2026-08-04 — Top-type #1: enforce `mixed` at use sites.

3. ~~**Control-flow narrowing gaps beyond Story 10.5**~~ — **DONE / RESOLVED** (Top-type #2–#5,
   #7, #9; `is_iterable`/`is_countable` 2026-07-31).  
   - ~~`match`-arm narrowing/result checking~~ — **DONE / RESOLVED** 2026-08-03 (Top-type #4).  
   - ~~Top-type **#5** `switch (true)` case-body narrowing~~ — **DONE / RESOLVED** 2026-08-03
     (`ControlFlowRule.CheckConditional`; `TypeGuardRuleTests`).  
   - Loop-body narrowing, positive-guard intersect-vs-replace, and early-exit narrowing
     (`return`/`throw`/`continue`/`break`, including `!`-negated guards) are done.  
   - Further control-flow / narrowing backlog (documented-unsupported forms, new guards, etc.) —
     tracked in [`CHECKER_GAPS.md`](CHECKER_GAPS.md), not here.

4. **Other open checker gaps affecting “full” Story 08 acceptance** (see `FOUND_BUGS.md` /
   `RESOLVED_BUGS.md`; living inventory in [`CHECKER_GAPS.md`](CHECKER_GAPS.md))  
   - Still open in **FOUND_BUGS** [Audit: Property initialization state, `default(T)` and runtime generic tracking]:  
     - ~~**§6** Local definite-assignment computed but not reported.~~ — **DONE / RESOLVED** (Fixed 2026-08-03; emits TYHP4014; ternary/if join + hoisted-local Split/Merge).  
     - ~~**§7** No property initialization-state analysis.~~ — **DONE / RESOLVED** (Fixed 2026-08-03; emits TYHP4157 on `$this->prop` reads; initializer / promotion / ctor-body assignment seeds; `??`/`isset` probes).  
     - ~~**§8** `unset()` untracked.~~ — **DONE / RESOLVED** (Fixed 2026-08-03; `UnsetTrackingRule`; locals clear TYHP4014 state; typed `$this->prop` requires `#[\Tyhp\AllowUnset]` else TYHP4158; with attribute, unset clears property-init → TYHP4157).
     - Plus remaining generics / literal-type edge cases still Open in that audit (Prop-init **§41** bare `true`/`false` mismatch → **RESOLVED** 2026-08-03).  
     - ~~§37 `instanceof T` / `is T` against a generic type parameter never reifies~~ — **DONE / RESOLVED**
       - **RESOLVED_BUGS:** Fixed 2026-07-31 — emit-time reify to `\Tyhp\Type::is($value, typeof(T)-lookup)`
         (Mechanism D callable binder / GenericObject class lookup; supersedes Mechanism A). Prior
         TYHP4156 reject removed. Verified: `InstanceofGenericParameterEmitterTests` green.
   - **Dynamic-property check / trait member binding (S08 Phase 6 #1) — REOPENED**  
     - Prior “resolved” suppress of TYHP4134 when any trait is `use`d papers over missing trait member binding.  
     - **FOUND_BUGS:** [Audit: Phase 6 of Story 08 — reopened] → **§1. Dynamic-property check ignores trait-provided properties** (Open).  
     - **Checker gaps:** Also tracked in [`CHECKER_GAPS.md`](CHECKER_GAPS.md).  
     - **To finish:** Bind/flatten trait members into the symbol model (or resolve `use` targets); remove the blanket suppress; still flag true dynamic assignments.  
   - ~~Literal all-literal param/return unresolved (3019/3020)~~ — **DONE / RESOLVED**  
     - **RESOLVED_BUGS:** Fixed 2026-07-31 — FOUND_BUGS **item 35**. Verified: `StaticValueTypeHelper` + binder/`NameResolver` + tests (`StaticValueTypeRuleTests`, `Bind_AllLiteralParameterAndReturnTypes_*`) green.  
   - ~~`return <expr>;` inside `__construct`/`__destruct` never rejected~~ — **DONE / RESOLVED**  
     - **RESOLVED_BUGS:** Fixed 2026-07-31 — FOUND_BUGS **item 40** (`CheckerConstructorDestructorCannotReturnValue` **4153**). Verified: `ControlFlowRule.CheckReturn` + `DeclarationRule.Callable` force void; `ConstructorDestructorReturnRuleTests` green.

---

## Story 08.5 — Symbol-name types & template strings

**Status:** Substantially complete — binder registration, checker helpers, template-string matcher, nameof typing, `tests/conformance/story08_5/` suites.

### Incomplete

1. ~~**Symbol-name types not always erased in emitted signatures**~~ — **DONE / RESOLVED**
   - **RESOLVED_BUGS:** Fixed 2026-08-03 — Parametric audit **§2** (`TypeSpellingHelper` erases
     `IsSymbolNameBehavior` brands to `string`; `SymbolNameTypeEmitterTests` green).
   - ~~**Related still open:** type-name algebra brands (`__TypeName`, …) — FOUND_BUGS Parametric **§3**.~~
     **Also resolved 2026-08-03** — Parametric **§3** (see below).

2. ~~**Parametric `__ClassName<T>` (and siblings) incompletely checked**~~ — **DONE / RESOLVED**
   - **RESOLVED_BUGS:** Fixed 2026-08-06 (P0 #2–#5: tyhpdef guards, bare≡`<object>`, nameof branding,
     parametric assignability / literal-vs-`T`) and Fixed 2026-08-11 (`__CompatibleTypeName<T>`
     subclass-as-`class-string` assignability; `__ClassName` stays invariant).
   - **Checker gaps:** Symbol-name / CompatibleTypeName rows in [`CHECKER_GAPS.md`](CHECKER_GAPS.md).

3. ~~**Type-name algebra utilities not erased in emitted signatures**~~ — **DONE / RESOLVED**
   - **RESOLVED_BUGS:** Fixed 2026-08-03 — Parametric audit **§3** (`TypeSpellingHelper` erases
     `IsTypeNameAlgebraBehavior` string brands to `string`; `__AsType` spells the resolved type;
     `TypeNameAlgebraEmitterTests` green).

4. **Conformance fixture breadth**  
   - Suites cover `__ClassName`/`__FunctionName`, `nameof`, template strings / `__TypeName`/`__UnionTypeName`.  
   - Still missing dedicated fixtures for struct/type utilities and `__MethodName` / `__EnumCaseName` erasure.  
   - **FOUND_BUGS:** [Audit: Story 10.5 Phase 6 implementation review] → **§1. `story08_5` golden fixtures do not exercise … `__MethodName`/`__EnumCaseName`** (Open — coverage expansion).  
   - **To finish:** Add `story08_5` build fixtures + expected PHP for those forms.

---

## Story 09 — Basic emitter

**Status:** Substantially complete — `TyhpEmitter` pass-through, splitter, alias conversion, `tests/conformance/story09/emit-basic/`.

### Incomplete / cleanup

1. **Stale `PLACEHOLDER_STORY_10` on `Emitter/OutputFileWriter.cs`**  
   - Comments claim clean/dry-run are unimplemented there; real clean/dry-run live in `OutputWriterService` / `BuildAction`.  
   - **Bug files:** Not tracked as an open FOUND bug (RESOLVED Story 09 audits mention `OutputFileWriter` as pipeline-sound). Cosmetic hygiene only.  
   - **To finish:** Remove or rewrite placeholders so they do not imply missing Story 09/10 behavior.

Feature transforms (structs, async, disposables, etc.) are Story 11, not gaps in Story 09.

---

## Story 10 — Build action

**Status:** Substantially complete — `BuildAction` pipeline, config sections, incremental build, composer update, runtime package deps, CLI tests.

### Incomplete

1. **`checker.maxErrorsPerFile` not wired from project config**  
   - `CheckerOptions.MaxErrorsPerFile` exists and is honored by `TyhpChecker`, but `FromProject` does not map it (only `AllowEval`, `TemplateStringMaxStates`, PHP version, experimental clone-with).  
   - **FOUND_BUGS:** [Audit: Story 10.5 Phase 3 …] → **§1. Stale `PLACEHOLDER_STORY_10` comment on already-wired `AllowEval`** (Open) — same entry notes **`MaxErrorsPerFile` still has an accurate unwired placeholder**.  
   - **To finish:** Parse `checker.maxErrorsPerFile` in `CheckerConfig` / `Project` and set it in `CheckerOptions.FromProject`; remove the accurate PLACEHOLDER.

2. **Stale `PLACEHOLDER_STORY_10` on `AllowEval`**  
   - Already wired via `project.Build.AllowEval` in `FromProject`; comment is wrong.  
   - **FOUND_BUGS:** Same audit entry as above — **§1. Stale `PLACEHOLDER_STORY_10` comment on already-wired `AllowEval`** (Open; impact none functionally).  
   - **To finish:** Delete the stale comment.

3. **`--watch`**  
   - Story 10 acceptance only required accepting the flag and printing “not yet implemented” — **met** (`PLACEHOLDER_STORY_19` in `BuildAction`). Full watcher is Story 19, not an incomplete Story 10 item.

4. **`PLACEHOLDER_STORY_20` tyhpdef generation after build**  
   - Intentionally deferred to Story 20 — not a Story 10 incompleteness.

---

## Story 10.5 — Deferred correctness & quality fixes

**Status:** COMPLETED — all 13 remediation items verified present (type-guard 4032, else-branch narrowing, utility constraints, `Readonly<T>`, extension `extends` **4147**, template-string budget + config, operator overload resolver, alias spelling, wrapped conditionals, incremental output paths, per-phase error counts, `story08_5` fixtures, self-host allowlist). Plan checkboxes updated 2026-07-31.

### Residual (post-completion quality)

1. **Broader `story08_5` fixtures** — see Story 08.5 item 3 (**FOUND:** Story 10.5 Phase 6 §1).  
2. **`MaxErrorsPerFile` wiring** — tracked under Story 10 (**FOUND:** Story 10.5 Phase 3 §1 note).

---

## Story 11 — Emitter feature expansion

**Status:** Substantially complete — inline emitter ADR accepted; conformance suites under `tests/conformance/story11/` for async, disposables, generics, imports, operators, short-functions, structs, type-aliases, type-guards, with.

### Incomplete

1. **Property accessor rewriting for PHP &lt; 8.4** — **Done**  
   - Lowering in `TyhpEmitter.PropertyAccessors.cs`: piece traits / `UsesPropertyAccessors`, `final` Handles* magics, `tyhpTry*` merge, `$this->prop` / compound / isset / unset / `parent::$prop::get|set` rewrites, `private(set)` emit + polyfill `setVisibility`, examples golden.  
   - Residual polish only if new edge cases appear in the wild.

2. **Open call-site / emit edge cases (`FOUND_BUGS.md` Story 11 audits)**  
   - Implicit convert not rewritten at call/return sites (plus constructor args, extension calls,
     named args) — **RESOLVED 2026-08-11** (Story 11 operator-overload call-site rewriting finish §8
     old §2; see `RESOLVED_BUGS.md`).  
   - Compound-assign temp extraction for array-element LHS — **RESOLVED 2026-08-11** (same audit old
     §3; see `RESOLVED_BUGS.md`).  
   - Null-safe (`?->`) extension-method calls drop null-safety — **RESOLVED 2026-07-31** (Story 11 extension-method call-site rewriting §1; see `RESOLVED_BUGS.md`).  
   - Struct→`array` / `array<K,V>`↔bare `array` assignability — **RESOLVED 2026-07-31** (Story 11 struct emission finish §1 + §6; see `RESOLVED_BUGS.md`). Same-audit §4 (call-site generic inference) — **RESOLVED 2026-08-06**; §5 (return-scope generics) — **RESOLVED 2026-08-11** (see `RESOLVED_BUGS.md`).  
   - Trait-`$this` direct-operand operator rewrite — **RESOLVED 2026-08-11** (Story 11 §8; composing-class
     search + `static::__op` late static binding; see `RESOLVED_BUGS.md`).  
   - Checker convert assignability at call/return/`new` — **RESOLVED 2026-08-11** (same audit; see
     `RESOLVED_BUGS.md`). Scoped `operator convert` mirror of emit — not Story 31 Idea 2
     `*Convertible`.  
   - Checker binary/unary operator inference uses overload return types — **RESOLVED 2026-08-11**
     (same audit; see `RESOLVED_BUGS.md`).  
   - Checker trait-`$this` overload return via agreed composing-class type — **RESOLVED 2026-08-11**
     (fresh review of trait-`$this` rewrite; agree-on-resolved-return / else Unresolved; see
     `RESOLVED_BUGS.md`).

3. **Stale fallback comment in `ComposerJsonService.DetermineRequiredPackages`**  
   - `EmitContext.RequiredPackages` / `RequirePackage` are used; content-scan fallback remains. Comment still says PLACEHOLDER. Cleanup unless the fallback is still required.  
   - **Bug files:** Not tracked in FOUND/RESOLVED (hygiene).

---

## Story 12 — Lint action

**Status:** Substantially complete — lint pipeline, JSON/SARIF, help text, fix *engine stubs*.

### Incomplete

1. **Real auto-fix implementations**  
   - Phase 6 intentionally stubbed `Apply()` → `"Not yet implemented"` (`AddMissingImportFix`, `RemoveUnusedImportFix`, `SortImportsFix`, `AddMissingTypeAnnotationFix`).  
   - **Bug files:** Not tracked as a defect in FOUND (intentional stub scope of Story 12 Phase 6). Related open FOUND item in that audit is unused-import walk gaps (fixture 3002 collisions resolved 2026-07-31).  
   - **To finish:** Implement edits using binder/checker suggestions (placeholders reference Stories 02/08); wire machine-applicable `IDiagnostic.Suggestion` from Story 14 where available.

2. **`--file` without cross-file resolution**  
   - `LintAction` still only parses the single file (`PLACEHOLDER_STORY_12`).  
   - **Bug files:** Not tracked in FOUND/RESOLVED. Distinct from Story 13’s symlink `--file` path bug
     (fixed 2026-07-31; see `RESOLVED_BUGS.md` Story 13 Phase 8 §1).  
   - **To finish:** Parse full project for symbols; check/report only the target file.

3. ~~**`--strict` on lint**~~ — **DONE** 2026-07-31  
   - `GetExitCode(strictMode)` + hosted-service wiring; lint failure message; see `RESOLVED_BUGS.md` (Story 13 Phase 2 §2).

4. ~~**NEEDS INPUT (blocks product decisions, not pure coding)**~~ — **DONE** 2026-07-31  
   - Builtin shadowing → **A** (no shadowing; rename fixtures) — see `RESOLVED_BUGS.md` (Story 12 Phase 6 §2).  
   - Canonical `exclude` vs `excludes` → **A** (`exclude` only) — see `RESOLVED_BUGS.md` (Story 13 Phase 2 §1).

---

## Story 13 — CLI polish

**Status:** COMPLETED (phases 1–8). Remaining items are post-completion bugs / decisions.

### Incomplete / open bugs

1. ~~**`lint --file` fails across symlink project paths**~~ — **DONE** 2026-07-31 — `PathCanonicalizer` + `IsLintFileInProject`; see `RESOLVED_BUGS.md` (Story 13 Phase 8 §1).  
2. ~~**Malformed tyhpdef can NullReference during integrity parse.**~~ — **DONE** 2026-07-31 — visitor null guards + `ParseContent` safety net; see `RESOLVED_BUGS.md` (Story 13 Phase 7 §1).  
3. ~~**Failed parses cached without diagnostics** (also Story 14)~~ — **DONE** 2026-07-31 — see `RESOLVED_BUGS.md` (Story 13 Phase 7 §2 / Story 14 Phase 4 §2).  
4. ~~**Config warnings on stdout corrupt machine-readable output**~~ — **DONE** 2026-07-31 — hybrid bag+stderr; see `RESOLVED_BUGS.md` (Story 13 Phase 4 §1).  
5. ~~**`exclude` vs `excludes`**~~ — **DONE** 2026-07-31 — canonical `exclude`; see `RESOLVED_BUGS.md` (Story 13 Phase 2 §1).  
6. ~~**`--strict` never promotes warnings on lint**~~ — **DONE** 2026-07-31 — see Story 12 item 3 / `RESOLVED_BUGS.md` (Story 13 Phase 2 §2).

---

## Story 14 — Error-message quality

**Status:** COMPLETED (phases 1–5 + fixtures/tests). Remaining items are post-completion quality bugs.

### Incomplete / open bugs

1. ~~**Space-separated option values also collected as positional paths** (`tyhp lint --format json src/`) — **FOUND:** [Story 14 Phase 5] **§1**.~~ **DONE** 2026-07-31 (`ValueTakingFlags` + `ExtractPositionalPaths`; see `RESOLVED_BUGS.md`).
2. ~~**Syntax errors can leak internal visitor diagnostics (TYHP2002)** naming ANTLR context classes — **FOUND:** [Story 14 Phase 5].~~ **DONE** 2026-08-10 (see `RESOLVED_BUGS.md`).
3. ~~**Rich underlines often single-caret** because many checker diagnostics omit `EndColumn` — **FOUND:** [Story 14 Phase 5].~~ **DONE** 2026-08-11 (`Base2Ast` end positions + checker `Report*` forwarding; see `RESOLVED_BUGS.md`).
4. **Per-code docs lost examples/remediation** when the error index became generated — **FOUND:** [Story 14 Phase 4] **§1** (Open).
5. **AST cache stores broken parses** (shared with Story 13) — previously tracked under Story 14 Phase 4; verify against current `AstCacheService` gating before re-logging.
6. ~~**Call-argument validation unreachable**~~ — **DONE** 2026-08-03 (Story 08 / Story 14 Phase 3 §1; see `RESOLVED_BUGS.md`).

---

## Cross-cutting (Stories 01–14)

| Item | Notes |
|------|--------|
| Runtime package Packagist publishing | Deferred to Story 21 (proper Packagist/semver). Interim Composer `path` + `@dev` is intentional/appropriate for now (see `RESOLVED_BUGS.md` entry 25). |
| Sourcemaps | Story 17 placeholders — out of scope. |
| LSP / watch file watcher | Story 19 — out of scope (`--watch` stub is OK for Story 10). |
| Tyhpdef generator Track C | Story 20 — out of scope. |
| Optimizer | Stories 23–24 — wired as no-op in build; out of scope. |

---

## Doc hygiene notes (not feature incompleteness)

- Stories **06, 07, 08, 09, 10** still contain large unchecked phase checklists from the original plan; implementation has largely moved on. Prefer the **Status** line at the top of each plan + this file over those checkboxes.  
- Story **10.5** checkboxes were marked complete in this audit.  
- Stories **01, 05, 12, 13, 14** golden-fixture boilerplate items were marked complete/N/A where appropriate.  
- **2026-07-31 bug-file pass:** Verified RESOLVED items **35** (literal types) and **40** (ctor/dtor value return → 4153) in code + tests; marked DONE under Story 08 item 4. No other INCOMPLETE theme was fully claimed resolved in `RESOLVED_BUGS.md`.
