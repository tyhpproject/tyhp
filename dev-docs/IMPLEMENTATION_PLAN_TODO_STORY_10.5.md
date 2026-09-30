# Implementation Plan: Story 10.5 — Deferred Correctness & Quality Fixes

> **Roadmap position:** Story 10.5 — **Tier 0 — Spine** (additive remediation sub-story, inserted after Story 10)
> **Direct dependencies (new numbering):** 08, 08.5, 09, 10
> **New story:** a focused remediation pass that pulls forward a set of correctness and quality gaps that were
> discovered during the Story 07–10 audits and **deliberately deferred** at the time (logged in `FOUND_BUGS.md`)
> because they were non-trivial, out of the discovering phase's scope, or risked conflicting with concurrent
> in-progress work. The spine (Stories 01–10) is now landed, so the dependencies that made these unsafe to fix
> in place have cleared, and they can be addressed as a single coherent unit of work.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single
> source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating
> ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** `FOUND_BUGS.md` open audit items (Stories 07, 08, 08.5, 09, 10)
> **Branch:** TBD
> **Generated:** 2026-06-23
> **Prerequisites:** Stories 08 (checker), 08.5 (symbol-name types + template strings), 09 (emitter), and 10
> (build action) are complete. This story is **additive** — it does not introduce new language surface; it closes
> correctness/robustness gaps in already-shipped subsystems. Each item maps to a specific open entry in
> `FOUND_BUGS.md`; those entries are being collapsed to a "tracked in Story 10.5" note as this plan is authored.
> **Status:** COMPLETED (2026-07-31 audit) — all 13 remediation items landed; residual fixture coverage gaps listed in `INCOMPLETE.md`.

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [Scope (In / Out)](#scope-in--out)
- [Architecture Overview](#architecture-overview)
- [Phase 1: Checker — Type-Guard Validation & Branch Narrowing](#phase-1-checker--type-guard-validation--branch-narrowing)
- [Phase 2: Checker — Utility-Type Generic Constraints & `Readonly<T>`](#phase-2-checker--utility-type-generic-constraints--readonlyt)
- [Phase 3: Checker — Extension-Method Modifier Policy & Template-String Guard](#phase-3-checker--extension-method-modifier-policy--template-string-guard)
- [Phase 4: Emitter — Operator-Overload Resolution, Alias Spelling & Wrapped-Conditional Detection](#phase-4-emitter--operator-overload-resolution-alias-spelling--wrapped-conditional-detection)
- [Phase 5: Build Pipeline — Incremental Output Verification & Per-Phase Error Counts](#phase-5-build-pipeline--incremental-output-verification--per-phase-error-counts)
- [Phase 6: Test Infrastructure — Conformance Fixtures & Self-Host Allowlist](#phase-6-test-infrastructure--conformance-fixtures--self-host-allowlist)
- [Cross-Cutting Concerns](#cross-cutting-concerns)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

Story 10.5 is a **deferred-debt remediation story**. It collects thirteen open items from the `FOUND_BUGS.md`
audit log — each previously deferred for a documented reason — and turns them into actionable work now that the
spine has landed and the blocking dependencies have cleared. The items fall into four areas, which become the
phase grouping:

1. **Checker correctness** (Phases 1–3) — type-guard return-type validation, negative/`else`-branch narrowing,
   utility-type generic-constraint enforcement, the `Readonly<T>` resolver, the extension-method modifier policy,
   and the template-string membership size guard + config plumbing.
2. **Emitter correctness** (Phase 4) — right-operand-aware operator-overload selection, a proper type→PHP alias
   spelling (replacing `ITypeExpression.ToString()`), and generalized wrapped-conditional class detection.
3. **Build-pipeline robustness** (Phase 5) — detecting missing/deleted output on an incremental "Nothing to build"
   early-exit, and reporting accurate **per-phase** error counts instead of the cumulative total.
4. **Test infrastructure** (Phase 6) — the `tests/conformance/story08_5/` golden fixtures (now producible because
   the emitter has landed) and a self-host allowlist so `SelfHostRuntimeConformanceTests` stops passing vacuously.

No new language features are introduced. Every change either fixes incorrect behavior, removes a latent robustness
hazard, or upgrades test coverage to catch regressions the prior scaffolding could mask.

> **What is intentionally NOT here:** the *full* runtime-package distribution + versioning via a published
> Packagist source. That is deferred to **~Story 21** (PHP extension Composer packages + version matrix). The
> interim local-source inclusion (Composer `path` repositories pointing at `runtime/packages/`) is being
> implemented separately and tracked in `FOUND_BUGS.md`; it is **not** part of this story.

---

## Motivation

During the Story 07 → Story 10 audits, reviewers fixed every clear, in-scope bug **in place** and logged the
remaining issues to `FOUND_BUGS.md` rather than guessing at a design decision, reworking a method that a
concurrent agent was editing, or implementing a fix whose real prerequisite had not yet landed. Each entry recorded
a precise "Why not fixed now." Those reasons have now expired:

- The **emitter** has landed (Story 09) and the **build action** is wired (Story 10), so the items that said "wait
  for the emitter" (e.g. the `story08_5` byte-identical conformance fixtures) and "wait for config plumbing in
  Story 10" (the template-string `maxStates` wiring) are now actionable.
- The **`PLACEHOLDER_STORY_11`** operator-overload-resolution deferral and the **`PLACEHOLDER_STORY_10`**
  template-string config deferral are pulled forward here because the latent code paths are now reachable and the
  correct fix is well understood.
- The **language-design questions** (extension-method modifiers; whether "no modifiers" means implicit
  `public static`) have been **settled by the user** (see Phase 3) and can be implemented rather than logged.

Closing these as one batch keeps the per-audit sections of `FOUND_BUGS.md` honest (open = genuinely open) and
prevents the slow accretion of "known but unscheduled" correctness debt on the spine subsystems.

---

## Scope (In / Out)

**In scope (13 items, by source audit):**

| # | Item | Source `FOUND_BUGS.md` entry | Primary file(s) | Phase |
|---|------|------------------------------|-----------------|-------|
| 1 | Type-guard body not verified to return `bool` | S08 Phase 5 #2 | `Rules/TypeAnnotationRule.cs` | 1 |
| 2 | Type-guard negative/`else`-branch narrowing missing | S08 Phase 5 #3 | `Rules/TypeNarrowingRule.cs` | 1 |
| 3 | Utility-type generic constraints registered but never enforced | S08.5 Phases 1–3 #1 | `UtilityTypeResolver.cs`, `GenericTypeArgumentValidator.cs` | 2 |
| 4 | `\Tyhp\Readonly<T>` resolver is a no-op, skips invalid-arg validation | S08 Phase 5 #1 | `UtilityTypeResolver.cs` (`ResolveReadonly`) | 2 |
| 5 | Extension-method modifiers only validated when ≥1 modifier present | S08 Phase 6 #1 | `Rules/ExtensionRule.cs` | 3 |
| 6 | Template-string *membership* checks have no size guard | S08.5 Phases 6–7 #1 | `TemplateStringMatcher.cs`, `TypeComparer` | 3 |
| 7 | Operator-overload selection ignores right-operand type & skips arity | S09 Phases 5–6 #2 | `Emitter/AliasConverter.cs` | 4 |
| 8 | `TypeAliasMap` uses `ITypeExpression.ToString()` for alias targets | S09 Phases 1–2 #2 | `Emitter/EmitContext.cs` (`CollectAliasMaps`) | 4 |
| 9 | Wrapped-conditional class detection only handles `if` | S09 Phases 1–2 #1 | `Emitter/PHPOutputFileSplitter.cs` | 4 |
| 10 | Incremental "Nothing to build" doesn't detect missing/deleted output | S10 Phases 5–7 #2 | `CLI/BuildAction.cs`, `IncrementalBuildService.cs` | 5 |
| 11 | "Parse phase completed with N error(s)" count includes bind/check errors | S08 Phase 7 #1 | `CLI/BuildAction.cs`, `CLI/LintAction.cs`, `CompilationService.cs` | 5 |
| 12 | No `tests/conformance/story08_5/` golden fixtures | S08.5 Phases 1–3 #2 | `tests/conformance/story08_5/` | 6 |
| 13 | `SelfHostRuntimeConformanceTests` passes vacuously | S07 Waves A2 & B #1 | `ConformanceSuiteTests.cs`, `SelfHostRunner.cs` | 6 |

**Out of scope (explicitly):**

- Full runtime-package distribution/versioning via a published Packagist source — **~Story 21**.
- The interim Composer `path`-repositories local-source inclusion — tracked separately (concurrent work).
- The runtime self-compilation milestone itself ("the compiler builds its own runtime") — post-Story-10; Phase 6
  only hardens the *test* so it can't be masked, it does not make the runtime `tyhp_src` compile.
- Any new diagnostic *language* surface; this story reuses existing `MessageCode` values (4032, 4035,
  `CheckerGenericConstraintNotSatisfied`) and, where a new code is warranted (the extension `extends`-keyword case),
  adds it in `MessageCode.cs` per `CONVENTIONS.md`.

---

## Architecture Overview

### Affected Components

```
Tyhp/TyhpLang/Checker/
├── Rules/TypeAnnotationRule.cs        (Phase 1 — type-guard body returns bool)
├── Rules/TypeNarrowingRule.cs         (Phase 1 — negative/else-branch narrowing)
├── Rules/ExtensionRule.cs             (Phase 3 — extension-method modifier policy)
├── UtilityTypeResolver.cs             (Phase 2 — Readonly<T> + constraint routing)
├── GenericTypeArgumentValidator.cs    (Phase 2 — per-parameter constraint validation for utility types)
├── CheckedType.cs / StructCheckedType (Phase 2 — per-property readonly flag)
├── TemplateStringMatcher.cs           (Phase 3 — step-budget guard)
└── TypeComparer.* / CheckerOptions    (Phase 3 — thread TemplateStringMaxStates from config)

Tyhp/TyhpLang/Emitter/
├── AliasConverter.cs                  (Phase 4 — FindMatchingOperatorOverload right-operand + arity)
├── EmitContext.cs                     (Phase 4 — CollectAliasMaps type→PHP spelling)
└── PHPOutputFileSplitter.cs           (Phase 4 — IsWrappedObjectDeclaration generalization)

Tyhp/CLI/
├── BuildAction.cs                     (Phase 5 — output-existence re-check; per-phase counts)
└── LintAction.cs                      (Phase 5 — per-phase counts)

Tyhp/Domain/Services/
├── IncrementalBuildService.cs         (Phase 5 — record produced output paths in build state)
└── CompilationService.cs              (Phase 5 — snapshot ErrorCount per phase)

Tyhp/Domain/Diagnostics/
└── CompilationResult.cs               (Phase 5 — expose per-phase error counts)

Tyhp/Domain/Exceptions/
└── MessageCode.cs                     (Phase 3 — dedicated extension missing-`extends` code, if added)

tests/
├── conformance/story08_5/             (Phase 6 — NEW golden .tyhp → .php fixtures)
└── Tyhp.Tests/Conformance/ConformanceSuiteTests.cs + TestHelpers/SelfHostRunner.cs (Phase 6 — allowlist)
```

### Design Principles

1. **Reuse existing diagnostics.** The deferred items already name their intended codes (`CheckerTypeGuardInvalidReturn`
   = 4032, `CheckerGenericConstraintNotSatisfied` = 4035). Wire the existing codes through; only mint a new code where
   the audit flagged a *wrong* code reuse (the extension missing-`extends` case currently borrows
   `CheckerMagicMethodSignature`).
2. **No drive-by behavior changes.** Each fix is scoped to the method named in its `FOUND_BUGS.md` entry plus the
   minimal supporting model change (e.g. the per-property readonly flag for `Readonly<T>`).
3. **Acceptance is fixture-backed.** Where the prior deferral cited "no fixtures" (Phase 6) or "latent, not observed"
   (operator overloads, template-string guard), the fix lands *with* a regression test or conformance fixture that
   would have caught it.
4. **Settled decisions over open questions.** The one item that was deferred as a "language-design decision"
   (extension modifiers) carries a now-settled rule (Phase 3); it is implemented, not re-logged.

---

## Phase 1: Checker — Type-Guard Validation & Branch Narrowing




### Phase Overview

Two related type-guard correctness gaps from the Story 08 Phase 5 audit. Both live in the checker's narrowing/
annotation rules and share the type-guard concept (`$param is Type` return types and guard-function narrowing).

### Item 1.1 — Type-guard function body not verified to return `bool`

- **Problem.** A function declaring a type-guard return type (`$param is Type`) must return `bool`
  (`CheckerTypeGuardInvalidReturn`, **4032**, per §5.4). `CheckTypeGuard` today only validates that the named guard
  parameter exists; it never confirms the function's `return` statements yield `bool`. A type-guard function that
  returns a non-`bool` is silently accepted.
- **Where.** `Tyhp/TyhpLang/Checker/Rules/TypeAnnotationRule.cs` — `CheckTypeGuard(...)`; diagnostic
  `CheckerTypeGuardInvalidReturn` (4032).
- **Source.** `FOUND_BUGS.md` — Audit Phase 5 of Story 08, item #2.
- **Recommended approach.**
  - Coordinate with the existing return-statement / control-flow checking so a `$param is Type` return type sets
    the function's effective `ExpectedReturnType` to `bool` for the purpose of return validation.
  - Walk the function body's reachable `return` statements (reuse the control-flow rule's return collection rather
    than re-walking) and verify each returned expression's inferred type is assignable to `bool`. Any non-`bool`
    return emits `CheckerTypeGuardInvalidReturn` (4032) at the offending `return` (or at the signature if there are
    no returns / a `void` fall-through path).
  - A guard function with **no** `return` (or a reachable implicit `void` exit) is invalid — a guard must always
    yield a boolean — and emits 4032.
- **Acceptance criteria.**
  - [x] A type-guard function whose body returns a non-`bool` (e.g. `return $x;` where `$x: int`) emits 4032.
  - [x] A type-guard function with a reachable path that does not return `bool` (e.g. falls through to implicit
        `void`) emits 4032.
  - [x] A correct type-guard function (`return $x instanceof Foo;`, `return is_string($x);`) emits no diagnostic.
  - [x] The existing guard-parameter-existence check is preserved.

### Item 1.2 — Type-guard negative/`else`-branch narrowing missing

- **Problem.** Type-guard *function* narrowing (`if (isString($x))`, user-defined and built-in) applies only in the
  **positive** branch. The negative/`else` branch is not narrowed — e.g. the `else` of `is_null($x)` does not narrow
  `$x` to non-null. (The reversed-operand-order `null === $var` half was already resolved 2026-06-22; this is the
  remaining negative-branch half.)
- **Where.** `Tyhp/TyhpLang/Checker/Rules/TypeNarrowingRule.cs`.
- **Source.** `FOUND_BUGS.md` — Audit Phase 5 of Story 08, item #3.
- **Recommended approach.**
  - When a guard narrows `$x` to `T` in the positive branch, compute the **complement** type for the negative branch
    and apply it to the `else` / fall-through state. For a guard to `T`, the negative branch narrows `$x` to the
    type difference `declared(x) \ T` (use the existing `Exclude`/difference machinery in `UtilityTypeResolver`/
    `TypeComparer`; e.g. `is_null` → non-null complement, `is_string` → remove `string` from a union).
  - Where a precise complement is not representable (open guard over an arbitrary user type), leave the negative
    branch unnarrowed (conservative, current behavior) rather than narrowing incorrectly.
  - Thread this through the same branch-state plumbing the positive branch already uses (`VariableState` fork/merge),
    so the `else` and post-`if` join states stay correct.
- **Acceptance criteria.**
  - [x] `if (is_null($x)) { … } else { /* $x is non-null here */ }` narrows `$x` to its non-null type in the `else`.
  - [x] `if (is_string($x)) { … } else { /* string removed */ }` narrows a union `$x` by removing `string`.
  - [x] A guard whose complement is not representable leaves the negative branch unchanged (no false narrowing).
  - [x] Existing positive-branch narrowing and the `null === $var` operand-order handling are unaffected.

### Dependencies

- **Requires:** Story 08 (checker, narrowing, control-flow rule, `VariableState`).
- **Provides:** Correct type-guard validation + symmetric branch narrowing for downstream checker rules.

---

## Phase 2: Checker — Utility-Type Generic Constraints & `Readonly<T>`




### Phase Overview

Two utility-type resolution gaps that share `UtilityTypeResolver.cs` and the generic-argument validation path. Both
turn declared-but-dead validation metadata into enforced behavior.

### Item 2.1 — Utility-type generic constraints registered but never enforced

- **Problem.** Symbol-name / utility types (`__MethodName<T>`, `__EnumCaseName<T>`, `__UsedTraitName<T>`, the
  `\Tyhp` utilities) register `GenericParameterRequirements` with per-parameter constraints (`ClassOrStruct`,
  `EnumOnly`, …), but **built-in utility** types are validated only for **arity** (`ValidateUtilityArity`), never
  for per-parameter constraints — that path is reserved for `BuiltInTypeSymbol`, and `BuiltInUtilityTypeSymbol`
  bypasses `ValidateBuiltInConstraint`. So `__EnumCaseName<SomeClass>` / `__MethodName<int>` are accepted with no
  diagnostic and the declared constraints are dead metadata.
- **Where.** `Tyhp/TyhpLang/Checker/UtilityTypeResolver.cs` (`ValidateUtilityArity`) and
  `Tyhp/TyhpLang/Checker/GenericTypeArgumentValidator.cs` (`ValidateInstantiation`); the
  `BuiltInUtilityTypeSymbol` → `ValidateBuiltInConstraint` gap.
- **Source.** `FOUND_BUGS.md` — Audit Phases 1–3 of Story 08.5, item #1.
- **Recommended approach.**
  - Route utility-type generic arguments through **per-parameter constraint validation** (not just arity): after the
    arity check passes, for each declared `GenericParameterRequirements` constraint, validate the corresponding
    resolved type argument and emit `CheckerGenericConstraintNotSatisfied` when it does not satisfy the constraint.
  - Share the constraint-checking logic with the existing `BuiltInTypeSymbol` path (`ValidateBuiltInConstraint`)
    rather than duplicating it, so `BuiltInTypeSymbol` and `BuiltInUtilityTypeSymbol` enforce constraints
    identically. This also fixes the noted shared gap for the other `\Tyhp` utility types that currently validate
    argument shape ad-hoc inside their own `Resolve*` methods.
  - Keep arity validation first (clearer diagnostics: arity error before constraint error).
- **Acceptance criteria.**
  - [x] `__EnumCaseName<SomeClass>` (constraint `EnumOnly`) emits `CheckerGenericConstraintNotSatisfied`.
  - [x] `__MethodName<int>` (constraint requiring a class/struct) emits `CheckerGenericConstraintNotSatisfied`.
  - [x] Well-constrained usages (`__EnumCaseName<SomeEnum>`, `__MethodName<SomeClass>`) emit no diagnostic.
  - [x] Arity errors still fire (and take precedence) for wrong argument counts.
  - [x] `BuiltInTypeSymbol` and `BuiltInUtilityTypeSymbol` share one constraint-validation routine.

### Item 2.2 — `\Tyhp\Readonly<T>` resolver is a no-op and skips invalid-arg validation

- **Problem.** `ResolveReadonly` returns `args[0]` unchanged. Per §5.8 it should return a copy of `T` where all
  properties are `IsReadonly = true`, and it should emit `CheckerGenericConstraintNotSatisfied` (**4035**) when `T`
  is not a class/interface/struct (e.g. `\Tyhp\Readonly<int>`). Neither behavior exists, so two acceptance criteria
  ("`Readonly<MyStruct>` resolves to all-readonly properties"; "`Readonly<int>` errors") are unmet.
- **Where.** `Tyhp/TyhpLang/Checker/UtilityTypeResolver.cs` — `ResolveReadonly(...)`; diagnostic
  `CheckerGenericConstraintNotSatisfied` (4035).
- **Source.** `FOUND_BUGS.md` — Audit Phase 5 of Story 08, item #1.
- **Recommended approach.**
  - **Checked-type model change (prerequisite):** add a **per-property readonly flag** to the struct/object checked
    type. `StructCheckedType.Properties` is currently `Dictionary<string, ICheckedType>` with no readonly bit;
    change the property value to a small record (e.g. `PropertyInfo { ICheckedType Type; bool IsReadonly; }`) — or
    a parallel readonly set — threaded through `CheckedType.cs`/`ICheckedType.cs` and every construction/consumption
    site. Default `IsReadonly = false` to preserve current behavior.
  - `ResolveReadonly<T>` then returns a **copy** of `T` with every property's `IsReadonly` set to `true`.
  - When `T` is not a class/interface/struct, emit `CheckerGenericConstraintNotSatisfied` (4035) and resolve to a
    safe fallback (the unmodified `T` or `unknown`) so checking continues.
  - Audit the property-readonly consumers (assignment checking) so the new flag is honored where it should produce a
    "cannot assign to readonly property" diagnostic; if that wiring is broader than this story, gate it behind the
    minimum needed to satisfy the two acceptance criteria and note the remainder.
- **Acceptance criteria.**
  - [x] `\Tyhp\Readonly<MyStruct>` resolves to a struct type where all properties report `IsReadonly = true`.
  - [x] `\Tyhp\Readonly<int>` (and other non-class/interface/struct args) emits `CheckerGenericConstraintNotSatisfied`
        (4035).
  - [x] The new per-property readonly flag defaults to `false` and does not change existing struct/object behavior.
  - [x] `dotnet build` is clean and the existing `Category=Checker` suite stays green.

### Dependencies

- **Requires:** Story 08 (checker), Story 08.5 (utility/symbol-name types, `GenericTypeArgumentValidator`).
- **Provides:** Enforced utility-type constraints and a working `Readonly<T>` transform for downstream rules.

---

## Phase 3: Checker — Extension-Method Modifier Policy & Template-String Guard




### Phase Overview

One settled language-design fix (extension-method modifiers) and one robustness/config fix (template-string
membership guard). Grouped because both are localized checker changes with a settled decision behind them.

### Item 3.1 — Dead extension-method modifier validation + miscoded missing-`extends` check

- **Original problem (as filed).** The `public`/`static` checks in `CheckExtensionFunction` ran only when
  `GetFunctionModifiers(...) != MemberModifier.None`, so a no-modifier extension function was never flagged.
- **Corrected finding (the filed framing was based on a false premise).** Extension members reuse the
  `functionDeclarationStatement` grammar rule, whose leading `functionModifiersGrammarAddon` is **overridden in
  Tyhp mode** to expose only an optional `async` (`Tyhp/TyhpLang/Grammar/TyhpParser.g4` lines 643–646). The PHP visibility/static
  modifiers (`public`/`protected`/`private`/`static`) have **no grammar slot** in any Tyhp function declaration,
  including extension members — they cannot be written, so the parser would reject them outright. Consequently the
  entire `if (modifiers != MemberModifier.None) { ... }` block (and `GetFunctionModifiers` / `CollectModifiers` /
  `TokenToModifier`) was **dead code**: the only token that addon can hold is `async`, which maps to
  `MemberModifier.None`, so `CheckerExtensionMethodNotStatic` (4033) / `CheckerExtensionMethodNotPublic` (4034)
  could never fire. The "implicit `public static`" rule is an **emit-time** detail, not a checker concern.
  The genuinely-needed check — missing `extends` clause — reused `CheckerMagicMethodSignature`, a poor code choice.
- **Where.** `Tyhp/TyhpLang/Checker/Rules/ExtensionRule.cs` — `CheckExtensionFunction`.
- **Source.** `FOUND_BUGS.md` — Audit Phase 6 of Story 08, item #1.
- **Resolution (implemented).**
  - Removed the dead modifier-validation block and the unused `GetFunctionModifiers` / `CollectModifiers` /
    `TokenToModifier` helpers from `ExtensionRule.cs`.
  - Retired message codes `4033` (`CheckerExtensionMethodNotStatic`) and `4034` (`CheckerExtensionMethodNotPublic`)
    from `MessageCode.cs` and both `.resx` files. They had no other references. The numbers are left as a gap (not
    reused), matching the existing convention.
  - Added a dedicated checker-error code `CheckerExtensionMissingExtends = 4147` (next free code; nothing in any
    story plan reserves the 4147+ range) with matching `ERROR_TYHP4147` strings in both `.resx` files, and switched
    the missing-`extends` check to use it instead of `CheckerMagicMethodSignature`.
- **Acceptance criteria.**
  - [x] Dead modifier validation and its helpers are removed (modifiers are not expressible in the grammar).
  - [x] The missing-`extends` case emits a **dedicated** new diagnostic code (`4147`), not
        `CheckerMagicMethodSignature`.
  - [x] New code + both `.resx` strings are present and in sync; retired `4033`/`4034` removed from code and `.resx`.

### Item 3.2 — Template-string *membership* checks have no size guard

- **Problem.** `IsSubtypeOf` (pattern ⊆ pattern) is size-guarded (`Complexity > maxStates`), but the **membership**
  path (`literal ∈ pattern`) used for the common "assign a string literal to a template type" case has **no** guard.
  The recursive matcher explores every split at every quantified hole, so a pathological pattern (several adjacent
  unbounded `${string}` holes vs. a long literal) can backtrack super-linearly. Latent today (literal length bounded
  by source; algebra patterns small) but a real robustness hazard. Additionally, `CheckerOptions.TemplateStringMaxStates`
  is **not** threaded into the static `TypeComparer`, so the matcher is hardcoded at 256 (the still-open
  `PLACEHOLDER_STORY_10` config wiring).
- **Where.** `Tyhp/TyhpLang/Checker/TemplateStringMatcher.cs` — `Matches(...)` / `MatchHole(...)`; compare the
  guarded `IsSubtypeOf(...)`. Config: `CheckerOptions.TemplateStringMaxStates` → static `TypeComparer`.
- **Source.** `FOUND_BUGS.md` — Audit Phases 6–7 of Story 08.5, item #1.
- **Recommended approach.**
  - **Share one budget across both membership and subtyping.** Introduce a single complexity/step budget used by
    both `Matches` (membership) and `IsSubtypeOf` (inclusion). Add a **step counter** to the recursive matcher that
    increments per recursive split/consumed repetition and **aborts when the budget is exceeded** (treat an
    over-budget match as an explicit "too complex" outcome — surface the existing complexity diagnostic rather than
    silently failing/looping).
  - **Thread the config value.** Plumb `CheckerOptions.TemplateStringMaxStates` from configuration into the static
    `TypeComparer` (constructor/static init or an explicit set during checker setup), removing the hardcoded `256`
    and resolving `PLACEHOLDER_STORY_10`. The default stays 256 when unset.
- **Acceptance criteria.**
  - [x] Membership (`literal ∈ pattern`) is bounded by the same budget as subtyping; a pathological pattern aborts
        with the complexity outcome instead of backtracking unboundedly.
  - [x] `CheckerOptions.TemplateStringMaxStates` from `tyhp.json` is honored by the matcher (verifiable by setting a
        small value and observing the budget take effect); default remains 256.
  - [x] The hardcoded `256` and the `PLACEHOLDER_STORY_10` marker are removed.
  - [x] Existing template-string membership/subtyping behavior for in-budget patterns is unchanged (Phase 6/7 tests
        stay green).

### Dependencies

- **Requires:** Story 08 (checker), Story 08.5 (template strings, `TypeComparer`), Story 10 (`CheckerOptions` config
  plumbing).
- **Provides:** A settled, enforced extension-method contract and a bounded, config-driven template-string matcher.

---

## Phase 4: Emitter — Operator-Overload Resolution, Alias Spelling & Wrapped-Conditional Detection




### Phase Overview

Three emitter-side correctness items from the Story 09 audits. Items 4.1 and 4.2 are correctness; item 4.3 is a
currently-no-op heuristic generalization (low priority but cheap with fixtures).

### Item 4.1 — Operator-overload selection ignores the right-operand type & skips arity

- **Problem.** `FindMatchingOperatorOverload` accepts a `rightOperand` parameter but never uses it: for extension
  operators it returns the **first** `ExtensionContributedOperators` entry whose `Operator` and arity match; for
  class-level operators it returns the first `Members` entry whose `Operator` matches, with **no arity check at
  all**. When a type declares the same operator for several right-hand types (e.g. `decimal`'s `+` for `self` vs
  `float|int|string|DecimalConvertible`), the wrong overload can be selected, producing a call to a mangled method
  name that does not match the intended operand type. Latent because `ResolveExpressionType` returns `null` for
  plain `$var` operands, so the rewrite path rarely fires for ordinary code.
- **Where.** `Tyhp/TyhpLang/Emitter/AliasConverter.cs` — `FindMatchingOperatorOverload`.
- **Source.** `FOUND_BUGS.md` — Audit Phases 5–6 of Story 09, item #2. This is the "Full operator overload
  rewriting" work the phase explicitly deferred via `// PLACEHOLDER_STORY_11` — **pulled forward into 10.5.**
- **Recommended approach.**
  - **Match the right operand's resolved type** against each candidate overload's declared parameter type(s):
    iterate candidates whose `Operator` matches and select the one whose (other-operand) parameter type is
    compatible with the resolved right-operand type, preferring the most specific match (exact type over a union/
    convertable supertype).
  - **Proper arity handling** for both paths: extension operators are static and take both operands (arity check on
    the full parameter list); class-level operators are instance methods on the left/self operand and take only the
    *other* operand (arity check on the single other-operand parameter — note Story 09 already fixed the
    receiver-duplication in `RewriteInstanceOperatorCall`). Add the missing arity check to the class-level branch.
  - When the right-operand type cannot be resolved (`ResolveExpressionType` returns `null`), fall back to the current
    first-match behavior so no regression occurs for unresolved operands, but document it as the conservative path.
  - Remove the `// PLACEHOLDER_STORY_11` marker for this resolution once implemented.
- **Acceptance criteria.**
  - [x] When a type declares the same operator for multiple right-hand types, the overload whose parameter type
        matches the resolved right-operand type is selected (verified with a `decimal`-style fixture: `+` over
        `self` vs `float|int|...`).
  - [x] Class-level operator candidates are arity-checked (no longer "first `Operator` match wins").
  - [x] Unresolved right operands fall back to the prior behavior (no regression).
  - [x] The `PLACEHOLDER_STORY_11` operator-resolution marker is removed; emitter tests stay green.

### Item 4.2 — `TypeAliasMap` uses `ITypeExpression.ToString()` for alias targets

- **Problem.** Alias targets are stored via `ToString()` on the type-expression AST (`TypeAliasSymbol.AliasedType.ToString()`),
  which may not match the PHP type spelling needed by `ConvertAliases()`. No user-visible failure today only because
  `ConvertAliases` was a no-op placeholder when this was logged — but it is fragile now that alias conversion is live.
- **Where.** `Tyhp/TyhpLang/Emitter/EmitContext.cs` — `CollectAliasMaps`.
- **Source.** `FOUND_BUGS.md` — Audit Phases 1–2 of Story 09, item #2.
- **Recommended approach.**
  - Replace `ITypeExpression.ToString()` with a proper **type → PHP pretty-printer / resolved type spelling** that
    produces the same string `ConvertAliases()` consumes. Share this spelling helper between `CollectAliasMaps` and
    `ConvertAliases()` so the map keys/values and the conversion site agree by construction.
  - Prefer resolving against the bound/checked type (FQN-aware) rather than the raw AST text, so aliases of
    namespaced/imported types spell correctly.
- **Acceptance criteria.**
  - [x] Alias targets are stored using the shared type→PHP spelling helper, not `ToString()`.
  - [x] `CollectAliasMaps` and `ConvertAliases()` use the same spelling routine (no divergence).
  - [x] A type alias to a namespaced/imported type converts to the correct PHP spelling (fixture-verified).

### Item 4.3 — Wrapped-conditional class detection only handles `if`

- **Problem.** `IsWrappedObjectDeclaration` checks only `PhpIfAst`. A class guarded by `while`, `switch`, or another
  conditional wrapper is split out as a PSR-4 object file instead of staying in the entry-point/root output file.
  Currently effectively a no-op (low priority) — the plan's heuristic explicitly only calls out
  `if (!class_exists(...))`.
- **Where.** `Tyhp/TyhpLang/Emitter/PHPOutputFileSplitter.cs` — `IsWrappedObjectDeclaration`.
- **Source.** `FOUND_BUGS.md` — Audit Phases 1–2 of Story 09, item #1.
- **Recommended approach (preferred).** Generalize the heuristic to also recognize other conditional wrappers
  (`while`, `switch`, and the general "single object declaration inside a conditional guard" shape), and add
  regression fixtures for each. (Alternative considered and rejected: explicitly documenting `if (!class_exists(...))`
  as the *only* supported guard — rejected because the generalization is cheap and the fixtures pin behavior.)
- **Acceptance criteria.**
  - [x] A class wrapped in `while`/`switch`/other conditional guards is recognized as a wrapped object declaration
        and stays in the root/entry-point output (not split to PSR-4).
  - [x] Regression fixtures cover `if`, `while`, and `switch` wrappers.
  - [x] The existing `if (!class_exists(...))` behavior is preserved.

### Dependencies

- **Requires:** Story 09 (emitter, `AliasConverter`, `EmitContext`, `PHPOutputFileSplitter`), Story 08 (resolved
  types for operand/alias spelling).
- **Provides:** Correct operator-overload rewriting, stable alias spelling, and robust file splitting.

---

## Phase 5: Build Pipeline — Incremental Output Verification & Per-Phase Error Counts




### Phase Overview

Two CLI/build-service robustness items: a correctness gap in the incremental early-exit, and a long-standing
mislabel of per-phase error counts.

### Item 5.1 — Incremental "Nothing to build" doesn't detect missing/deleted output

- **Problem.** The incremental skip is keyed only on source-file hashes + the config/compiler-version hash. If a
  prior build succeeded (state saved) and the user deletes the generated `build/` output (or individual `.php`
  files) without touching any source, the next `tyhp build` reports "Nothing to build" and produces no output even
  though the output is gone. Matches the documented conservative source-only strategy, but is a real robustness gap
  (workaround today is `--clean`).
- **Where.** `Tyhp/CLI/BuildAction.cs` — the incremental early-exit block (`!fileChanges.HasChanges &&
  IsStateValid(...) && !CleanBeforeBuild && !DryRun`); `Tyhp/Domain/Services/IncrementalBuildService.cs`.
- **Source.** `FOUND_BUGS.md` — Audit Phases 5–7 of Story 10, item #2.
- **Recommended approach.**
  - **Record the set of produced output file paths** in the build state (`tyhp-build-state.json`) after a successful
    build (in addition to the existing source-hash + config/compiler-version data).
  - Before the incremental early-exit, **verify all recorded outputs still exist on disk.** If any recorded output
    is missing, **force a rebuild** (do not take the early exit) so the deleted/missing files are regenerated.
  - Keep this independent of source-change detection (a missing output forces a rebuild even when no source changed).
    Preserve `--clean` semantics (still deletes state).
- **Acceptance criteria.**
  - [x] `tyhp-build-state.json` records the produced output file paths from the last successful build.
  - [x] Deleting an output `.php` file (no source change) causes the next build to rebuild rather than report
        "Nothing to build."
  - [x] When all recorded outputs exist and no source/config changed, the early-exit still fires (no perf regression).
  - [x] `--clean` behavior is unchanged.

### Item 5.2 — "Parse phase completed with N error(s)" count includes bind/check errors

- **Problem.** `ParseFiles` now runs parse → bind → check internally, so when Step 5 reads
  `result.Diagnostics.ErrorCount` the count already includes binder and checker errors. A file whose only error is a
  binder `TYHP3002` still prints "Parse phase completed with 1 error(s)," attributing a bind/check error to the parse
  phase (Phase 7 widened the mislabel to also absorb checker errors).
- **Where.** `Tyhp/CLI/BuildAction.cs` and `Tyhp/CLI/LintAction.cs` — Step 5 ("Log parse results");
  `Tyhp/Domain/Services/CompilationService.cs`; `Tyhp/Domain/Diagnostics/CompilationResult.cs`.
- **Source.** `FOUND_BUGS.md` — Audit Phase 7 of Story 08, item #1.
- **Recommended approach.**
  - **Snapshot `Diagnostics.ErrorCount` after each phase** inside `CompilationService` (after parse, after bind,
    after check) and **expose per-phase counts on `CompilationResult`** (e.g. `ParseErrorCount`, `BindErrorCount`,
    `CheckErrorCount`, computed as deltas so each phase's count reflects only errors introduced in that phase).
  - Have the CLI (`BuildAction`/`LintAction` Step 5 and the summary) **report per-phase deltas** instead of the
    cumulative total, so "Parse phase completed with N error(s)" reflects only parse-introduced errors.
- **Acceptance criteria.**
  - [x] `CompilationResult` exposes per-phase error counts (parse/bind/check) as deltas.
  - [x] A file whose only error is a binder `TYHP3002` reports **0** parse-phase errors (the error is attributed to
        the bind phase).
  - [x] A checker-only error is attributed to the check phase, not parse.
  - [x] The CLI summary's per-phase lines use the per-phase counts; the total still equals the sum.

### Dependencies

- **Requires:** Story 10 (BuildAction, IncrementalBuildService, build-state model), Story 01
  (CompilationService/CompilationResult), Story 12 (LintAction — shares Step 5; coordinate if not yet landed).
- **Provides:** Correct incremental rebuilds on deleted output and accurate per-phase diagnostics.

---

## Phase 6: Test Infrastructure — Conformance Fixtures & Self-Host Allowlist




### Phase Overview

Two test-infrastructure items that were deferred because their prerequisite (the emitter / a compiling runtime) had
not landed. The emitter has since landed, so the `story08_5` fixtures are now producible; the self-host item is
hardened so it cannot mask regressions while the runtime still fails to self-compile.

### Item 6.1 — No `tests/conformance/story08_5/` golden fixtures

- **Problem.** The Story 08.5 plan's "Golden Fixtures / Tests" section calls for `.tyhp → .php` conformance fixtures
  under `tests/conformance/story08_5/`, including the "symbol-name types erase to plain `string`, byte-identical"
  assertions. None exist (only `story06/`, `story09/` exist); Story 08.5 behavior is covered only by unit tests.
  This was deferred because the erasure/byte-identical `.php` expectations require the emitter — which has **now
  landed**, making this **actionable**.
- **Where.** `tests/conformance/story08_5/` (new); driven by the existing `ConformanceRunner` harness (Story 07).
- **Source.** `FOUND_BUGS.md` — Audit Phases 1–3 of Story 08.5, item #2 (process note).
- **Recommended approach.**
  - Add `.tyhp → .php` conformance fixtures under `tests/conformance/story08_5/` exercising symbol-name types,
    `nameof()` result typing, type/struct utilities, and template strings.
  - Include the **erasure assertions**: symbol-name types must emit as plain `string` in the generated PHP, with
    **byte-identical** expected `.php` output (the fixtures are the source of truth, matching the existing
    `story09/emit-basic` golden pattern).
  - Wire them into the conformance suite so they run with the rest of the golden fixtures.
- **Acceptance criteria.**
  - [x] `tests/conformance/story08_5/` contains `.tyhp → .php` golden fixtures for the Phase 1–7 features.
  - [x] Fixtures assert symbol-name types erase to plain `string`, byte-identical.
  - [x] The conformance run is green with the new fixtures.

### Item 6.2 — `SelfHostRuntimeConformanceTests` passes vacuously while runtime `tyhp_src` doesn't compile

- **Problem.** `SelfHost_RecompiledRuntime_MatchesCommittedPhp` early-returns (passes) when **every** runtime
  package fails to build. Today all four packages (`core`/`decimal`/`async`/`lambda`) fail to compile from
  `tyhp_src`, so the test exercises the `SelfHostRunner` infrastructure but asserts nothing about self-host PHP
  correctness. A regression that makes one package build but emit wrong PHP for the others could be masked while the
  majority still fail to build.
- **Where.** `tests/Tyhp.Tests/Conformance/ConformanceSuiteTests.cs` —
  `SelfHost_RecompiledRuntime_MatchesCommittedPhp`; `tests/Tyhp.Tests/TestHelpers/SelfHostRunner.cs` —
  `VerifyAllPackages`.
- **Source.** `FOUND_BUGS.md` — Audit Waves A2 & B of Story 07, item #1.
- **Recommended approach.**
  - Keep the scaffolding, but add an **allowlist / tracking set of packages expected to compile.** The moment any
    package builds, the test **asserts on that package's PHP-output correctness** (diff against committed `src/`),
    so a building-but-wrong package can no longer be masked by the still-failing majority.
  - Start the allowlist empty (or with whatever currently builds, which is none) and grow it as packages begin to
    self-compile. A package on the allowlist that *fails* to build is a test failure (it regressed).
  - Revisit the full self-host milestone when the runtime self-compiles (post-Story-10); this item only removes the
    "vacuous pass" masking hazard.
- **Acceptance criteria.**
  - [x] The test carries an explicit allowlist of packages expected to compile.
  - [x] A package on the allowlist is asserted for PHP-output correctness (diff vs committed `src/`).
  - [x] A building-but-incorrect package fails the test even if other packages still fail to build.
  - [x] With the allowlist empty (current state), the suite stays green but no longer asserts vacuously on a
        building package.

### Dependencies

- **Requires:** Story 07 (conformance harness, `SelfHostRunner`, `SnapshotManager`), Story 09 (emitter — produces the
  `.php` for `story08_5` fixtures), Story 08.5 (the features under test).
- **Provides:** Regression coverage for symbol-name erasure and a non-masking self-host gate.

---

## Cross-Cutting Concerns

### Diagnostics Registered Centrally

The only new diagnostic introduced by this story is the dedicated extension "missing `extends`" code (Phase 3,
Item 3.1), which **must** be added in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth per
`CONVENTIONS.md`) with matching `Resources/CLI.TyhpHostedService.resx` / `.en-US.resx` strings. All other items
reuse existing codes (`CheckerTypeGuardInvalidReturn` 4032, `CheckerGenericConstraintNotSatisfied` 4035).

### Checked-Type Model Change (Phase 2)

The per-property readonly flag (Item 2.2) is the one model change with cross-cutting reach: every construction and
consumption site of `StructCheckedType.Properties` must be updated when the value shape changes. Default the flag to
`false` so the change is behavior-preserving outside `Readonly<T>`. Land this with a clean `dotnet build` and a green
`Category=Checker` run before building on top of it in the same phase.

### No Drive-By Scope Creep

Each item is scoped to the method(s) named in its `FOUND_BUGS.md` entry plus the minimal supporting change. Where a
fix reveals a broader concern (e.g. honoring the new readonly flag in *all* assignment checks), implement only what
the listed acceptance criteria require and log any genuinely larger follow-up back to `FOUND_BUGS.md` rather than
expanding this story.

### Placeholder Resolution

This story resolves two standing placeholders:
- `// PLACEHOLDER_STORY_11` (operator-overload rewriting) — Phase 4, Item 4.1.
- `// PLACEHOLDER_STORY_10` (template-string `maxStates` config wiring) — Phase 3, Item 3.2.

Remove each marker as its item lands.

### Coordination

The full runtime-package distribution/versioning (published Packagist source) is **out of scope** and deferred to
~Story 21. The interim Composer `path`-repositories local-source inclusion is being implemented separately; this
story neither edits that path nor the runtime packaging.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite
> established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to
> it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [x] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures for the remediated
      behaviors — the new `tests/conformance/story08_5/` set (Phase 6), an operator-overload-resolution fixture
      (Phase 4), a type-alias-spelling fixture (Phase 4), and wrapped-conditional `if`/`while`/`switch` fixtures
      (Phase 4). The committed fixtures are the source of truth for expected compiler output.
- [x] **Unit / integration tests:** Cover each checker item (type-guard return-`bool` 4032, negative-branch
      narrowing, utility-type constraint enforcement, `Readonly<T>` transform + 4035, extension-modifier policy,
      template-string budget) under `Category=Checker`; the build items (incremental output re-check, per-phase
      counts) under `Category=Build`; and the self-host allowlist under `Category=Conformance`.
- [x] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story
      is considered done.
- [x] **Runtime self-host conformance:** The hardened `SelfHostRuntimeConformanceTests` (Phase 6) stays green with
      an empty allowlist and asserts correctness for any allowlisted package.
- [x] **Diagnostics registered centrally:** Any new diagnostic code (the extension missing-`extends` code) is added
      only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never
      re-declared in this doc.

---

*Generated: 2026-06-23 | Source: FOUND_BUGS.md open audit items (Stories 07–10) | Branch: Not Specified*
