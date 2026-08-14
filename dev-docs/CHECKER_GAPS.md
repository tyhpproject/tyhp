# Checker Gaps — Capability Matrix & Backlog

Living tracker of **Tyhp checker** capability vs intended behavior. Prefer this file for
“what still needs to work as intended”; keep raw discovery notes in [`FOUND_BUGS.md`](FOUND_BUGS.md)
and story plans in `IMPLEMENTATION_PLAN_TODO_STORY_*.md`. Story-level incomplete themes also
appear in [`INCOMPLETE.md`](INCOMPLETE.md) (sibling reconcile — cross-link only).

**Created:** 2026-08-06 (Milestone: checker capability matrix).  
**Baseline contract:** **Mechanism D only** for function/method generic emit/check (see § Mechanism D).

---

## How to use

1. **Before claiming a checker feature is “done”** — check the matrix row and backlog priority.
2. **When fixing or discovering a gap** — update the matrix status, cite `FOUND_BUGS.md` (do not
   clear FOUND entries from here), and adjust P0/P1/P2 if ranking changes.
3. **Emitter / binder spillover** — if the checker cannot behave as intended without emit or
   call-site changes, log it under [Emitter / call-site spillover](#emitter--call-site-spillover)
   *and* keep a checker-facing row if the user-visible contract is checker-shaped.
4. **Do not invent TypeScript features** — only gaps against Tyhp’s own intended design.

### Status legend

| Status | Meaning |
|--------|---------|
| **Done** | Behaves as intended; covered by tests / RESOLVED. Keep out of backlog. |
| **Partial** | Works for a subset; remaining holes are intentional backlog (not “won’t do”). |
| **Broken** | Known incorrect / suite-red / falsely permissive or rejecting. |
| **Open-backlog** | Intended behavior not implemented yet (includes former “documented unsupported”). |
| **Deferred** | Explicitly postponed to a named story or later phase; still a backlog item. |

Former **documented-unsupported** guide ⚠️ items are **Open-backlog** (or Partial/Deferred) here —
they are work to make compile/check as intended, not permanent exclusions.

---

## Scope

| In scope | Out of scope (for this file) |
|----------|------------------------------|
| Checker rules, `TypeComparer*`, `TypeInferrer*`, narrowing, assignability, generics checking, declaration validation | Pure CLI / LSP / docs polish with no checker contract |
| Binder/symbol gaps that make checker checks wrong (e.g. trait members) | Full story implementation plans (link them) |
| Emitter / call-site changes **required** for the checker contract to hold | Dumping every Done Story 08 row |

**Assumed Done unless listed:** Story 08 flagship surface that already passes tests and is not
named below (call-arg validation, match-arm narrowing, loop/early-exit narrowing, property init /
`unset` tracking, `mixed` use-site enforcement, etc.). Prefer actionable non-Done rows over an
exhaustive Done dump.

### Mechanism D (only intended generic emit/check contract)

- **Mechanism D** (Closure-binder `__tyhpGeneric` variants + unified `\Tyhp\Generic::bind`) is the
  **only** intended contract for function/method generics (decided 2026-08-03, implemented
  2026-08-04; design record in `FOUND_BUGS.md` § Mechanism D / `RESOLVED_BUGS.md`).
- **Mechanism A** (flat `__tyhpGeneric(...$typeArgs, ...$valueArgs)` body — variant *is* the body)
  is **superseded**. Class-level Mechanism C (`$__tyhpGeneric` bag / `__initGenerics__tyhpGeneric` /
  `new_*__tyhpGeneric` factories) is **unchanged** and is **not** A debt.
- **Naming trap:** D reused A-era names (`RequiresGenericVariant`, `GenericCallTargets`,
  suffix `__tyhpGeneric`). Those symbols are **live D plumbing**, not leftover flat-A emit.
  Residual debt is mostly **stale “Mechanism A” wording** and a few mislabeled docs — not a second
  emit path. See [Mechanism A residual debt](#mechanism-a-residual-debt-audit-2026-08-06).

### Done* (do not reopen as gaps)

| Capability | Notes |
|------------|--------|
| `switch (true)` case-body narrowing from type guards | **Done** — Top-type #5 fixed 2026-08-03; `TypeGuardRuleTests`. Do not list as open. (`INCOMPLETE.md` reconciled 2026-08-06 — also Done.) |
| `$obj::class` → `__ClassName<T>` (static type of receiver) | **Done** 2026-08-06 (FOUND 2026-08-06 audit §5). |
| Call-site **arity** overload selection for argument validation | **Done** 2026-08-13 — arity pick plus same-arity type pick (`FunctionOverloadSelector`). Documented exact→static-value→ambiguity remains P2 #17. |
| Free-function call-site generic inference (`array_map`, …) | **Mostly Done** 2026-08-06 (FOUND Story 11 §4 resolved); residual method-call / nested edges → Partial below. |
| Ctor/dtor `return <expr>` → 4153 | **Done** — RESOLVED_BUGS item 40 (2026-07-31); `ControlFlowRule.CheckReturn`. 2026-08-03 suite-red Skip was stale — all 7 `ConstructorDestructorReturnRuleTests` pass with Skip removed (re-verified 2026-08-06). Un-skip those two Facts; do not reopen as a checker gap. |

---

## Capability matrix (non-Done emphasized)

### Assignability

| Capability | Status | Cite |
|------------|--------|------|
| Same-declaration generic instantiations compare type args (`Box<string>` ↛ `Box<int>`) | **Done** | FOUND generic structs 2026-08-05 §1 → RESOLVED 2026-08-06 |
| Bare `true`/`false` literal assignability suite | **Done** | FOUND suite-reds 2026-08-03 §4 → RESOLVED 2026-08-07 |
| `bool` ≡ `true\|false` (symmetric union folding) | **Done** | FOUND #42 → RESOLVED 2026-08-07 |
| Parametric `__ClassName<A>` ↔ `__ClassName<B>` + literal-vs-`T` | **Done** | FOUND 2026-08-06 §3 → RESOLVED 2026-08-06 |
| Bare `__ClassName` ≡ `__ClassName<object>` (siblings too) | **Done** | FOUND 2026-08-06 §2 → RESOLVED 2026-08-06 |
| Static-value / literal-union typing (widen/collapse, return resolution) | **Done** | FOUND suite-reds 2026-08-03 §5 → RESOLVED 2026-08-07 |

### Generics

| Capability | Status | Cite |
|------------|--------|------|
| Generic default type args applied by checker (`T = Type` when omitted) | **Done** | Story 28 Phase 4 core; CHECKER_GAPS P1 #12 → RESOLVED 2026-08-07 |
| Call-site generic inference (methods / nested multi-arg residuals) | **Done** | FOUND Story 11 §4 residual → RESOLVED 2026-08-07 (method path); nested conflict polish optional |
| Full tyhpdef **type-based** overload selection + return inference | **Partial** | Same-arity assignability pick (`FunctionOverloadSelector`) landed Story 16.5 Phase 7 for Struct vs Tuple / similar; documented exact→static-value→ambiguity diagnostics still later |
| Mechanism D contract; no new Mechanism A paths | **Done** (emit already D); Phase 0 comment/guide rebrand **Done** | [A residual audit](#mechanism-a-residual-debt-audit-2026-08-06) |

### Inference

| Capability | Status | Cite |
|------------|--------|------|
| Array literal inference (`$xs = [1,2,3]` → `array<…>`; empty `[]` → `array<never, never>`) | **Done** | CHECKER_GAPS P1 #13 → RESOLVED 2026-08-07 |
| Closure params from expected `callable` / call-site context | Assumed **Done** / keep regressions green | Story 08 / closure inference |
| Emit inferred PHP typehints when Tyhp omitted them | **Done** | FOUND 2026-08-06 emit inferred typehints → RESOLVED 2026-08-11 |

### Narrowing

| Capability | Status | Cite |
|------------|--------|------|
| Tyhpdef-driven symbol-name existence guards (not hardcoded `TypeNarrowingRule` maps) | **Done** | FOUND 2026-08-06 §1 → RESOLVED 2026-08-06 |
| `is` / `isa` / `isan` aliases check/compile like `instanceof` | **Done** | Guide §16 / §28; emit+narrow already landed (RESOLVED 2026-07-30); confirmed 2026-08-07 |
| `switch (true)` guard cases | **Done** | RESOLVED 2026-08-03; see Done* |

### Callables

| Capability | Status | Cite |
|------------|--------|------|
| Arity-based overload pick for arg validation | **Done** (arity + same-arity type pick) | FOUND 2026-08-05 §2; Story 16.5 Phase 7 |
| Signature-reflection utilities / retire `CallableArgs*` ladders | **Done** (ExtStandard `call_user_func*`) | Story 16.5 Phase 7; `CallableArgs*` structs remain as examples |

### Structs

| Capability | Status | Cite |
|------------|--------|------|
| Anonymous `new struct {…}` | **Done** | Guide §10; binder binds anon decls (2026-08-07) |
| `clone` on struct | **Done** | Guide §10; emit no-op / `with` via `\array_replace` |
| Generic struct member substitution on read | Assumed **Done** | FOUND 2026-08-05 (contrast whole-type Broken row) |

### Symbol-name / utilities

| Capability | Status | Cite |
|------------|--------|------|
| `TypeName::class` → `__ClassName<ThatType>` | **Partial** (Done for `::class`) | FOUND 2026-08-06 §4 |
| `nameof(TypeName)` → parametric `__ClassName<ThatType>` (still bare) | **Done** | FOUND 2026-08-06 §4 → RESOLVED 2026-08-06 |
| Template string extras (char classes, `infer` capture, inline groups) | **Open-backlog** / later | Story 08.5 Phase 6 deferred subset |
| Subclass-as-`class-string` on `__ClassName` | **Done** (via `__CompatibleTypeName<T>`, not `__ClassName`) | RESOLVED 2026-08-11 parametric CompatibleTypeName |

### Declarations

| Capability | Status | Cite |
|------------|--------|------|
| Property hook **bodies** type-checked | **Done** | FOUND property-hook follow-up §1 → RESOLVED 2026-08-06; corrected 2026-08-06 (`set =>` vs void) |
| Trait members visible to dynamic-property check (no blanket suppress) | **Done** | FOUND Story 08 Phase 6 reopen §1 → RESOLVED 2026-08-06 |
| Unused-import (4130) false positives for type-only uses | **Done** | FOUND Story 12 Phase 6 §3 → RESOLVED 2026-08-06 |
| Ctor/dtor `return <expr>` → 4153 | **Done** | RESOLVED_BUGS item 40; `ConstructorDestructorReturnRuleTests` (re-verified 2026-08-06 — stale Skip only) |

### Other

| Capability | Status | Cite |
|------------|--------|------|
| `(decimal)` cast | **Open-backlog** if checker-facing; else parse/emit | Guide §4 / §28 ⚠️ |
| Reserved keyword as class name → clean diagnostic (no NRE abort) | Spillover (parse/visitor) | FOUND generic structs 2026-08-05 §3 |

---

## Ranked backlog

### P0 — correctness / High (fix or unblock runtime packages)

1. ~~**Same-type generic instantiations ignore type args**~~ — **Done** (2026-08-06)  
   Same-base `GenericCheckedType` assignability is definitive (invariant user generics).  
   Follow-up (2026-08-06): `G<T>` → `G<mixed>` carve-out (`T` ≠ `void`/`never`) for heterogeneous
   bags — see RESOLVED Fixed 2026-08-06 P0 #1 follow-up.  
   → RESOLVED Fixed 2026-08-06 / FOUND generic structs 2026-08-05 §1.

2. ~~**Tyhpdef-driven symbol-name guards**~~ — **Done** (2026-08-06)  
   Prefer tyhpdef `$param is Type` guards over `SymbolNameGuards`; ExtCore `*_exists` updated.  
   `SymbolNameGuards` remains fallback for bool-returning receiver-capturing stubs.  
   → RESOLVED Fixed 2026-08-06 / FOUND 2026-08-06 §1.

3. ~~**Bare `__ClassName` ≡ `__ClassName<object>`** (+ siblings)~~ — **Done** (2026-08-06)  
   → RESOLVED Fixed 2026-08-06 / FOUND 2026-08-06 §2.

4. ~~**Parametric `__ClassName<T>` assignability + literal-vs-`T`**~~ — **Done** (2026-08-06)  
   → RESOLVED Fixed 2026-08-06 / FOUND 2026-08-06 §3.

5. ~~**`nameof(TypeName)` parametric branding** (parity with `::class`)~~ — **Done** (2026-08-06)  
   → RESOLVED Fixed 2026-08-06 / FOUND 2026-08-06 §4.

6. ~~**Property hook bodies not type-checked**~~ — **Done** (2026-08-06; semantics corrected same day)  
   `get` / `set => expr` checked against the **property type**; block `set` is void + `$value`.  
   Seeds `$this` / prop-init in hooks; types `parent::$prop::get()`/`::set()`.  
   → RESOLVED Fixed 2026-08-06 / FOUND property-hook follow-up §1.

7. ~~**Unused-import false positives (type-only positions)**~~ — **Done** (2026-08-06)  
   → RESOLVED Fixed 2026-08-06 / FOUND Story 12 Phase 6 §3.

8. ~~**Trait members vs dynamic-property blanket suppress**~~ — **Done** (2026-08-06)  
   Resolve used traits for TYHP4134; blanket suppress removed.  
   → RESOLVED Fixed 2026-08-06 / FOUND Story 08 Phase 6 reopen §1.

### P1 — suite-red / precision / former ⚠️ promotions

9. ~~**`true`/`false` literal assignability suite**~~ — **Done** (2026-08-07)  
   Confirmed already fixed (Prop-init #41 / 2026-08-03); suite-red Skip was stale. Follow-up
   (found during P1 re-verification): `array<true>` rejected the widened-literal element type from
   `[true, true]` (nominal `Simple("true")` source vs. `LiteralCheckedType(true)` target had no
   symmetric branch) — fixed same day in `TypeComparer.Assignability.cs`.  
   → RESOLVED Fixed 2026-08-07 / FOUND suite-reds 2026-08-03 §4.

10. ~~**`bool` ≡ `true\|false` asymmetry**~~ — **Done** (2026-08-07)  
    `CheckedTypes.UnionTypes` folds `true|false` → `bool`; assignability covers leftover
    unfolded unions; TYHP4056 still flags the spelling.  
    → RESOLVED Fixed 2026-08-07 / FOUND #42.

11. ~~**Static-value / literal-union typing suite**~~ — **Done** (2026-08-07)  
    Confirmed already fixed; suite-red Skip was stale (`StaticValueTypeRuleTests`,
    `StaticValueTypeEmitterTests`, `Phase08_5RuleTests.Check_FunctionReturnType_ResolvesFromLiteral`).  
    → RESOLVED Fixed 2026-08-07 / FOUND suite-reds 2026-08-03 §5.

12. ~~**Generic default type args applied by checker**~~ — **Done** (2026-08-07)  
    `GenericTypeArgumentValidator` fills omitted trailing defaults; bare/`new` apply when
    defaulted; decl diagnostics TYHP4310–4312. Function call-site inference→default fallback
    remains Story 28 follow-up (explicit/inferred paths already work).  
    → RESOLVED Fixed 2026-08-07 / Story 28.

13. ~~**Array literal inference**~~ — **Done** (2026-08-07)  
    `InferArrayLiteral` for `PhpArrayAst` / short-syntax `PhpArrayPairListAst`; list →
    `array<int|string, V>`, map → `array<K,V>`, empty → `array<never, never>` (so
    string-keyed targets like `array<string, T> $x = []` assign). Follow-up: empty
    was briefly `array<int|string, never>` and failed key covariance — fixed same day.  
    → RESOLVED Fixed 2026-08-07.

14. ~~**Call-site generic inference residuals** (method calls, nested edges)~~ — **Done** (2026-08-07)  
    `ResolveMethodReturnType` runs the same argument-driven inference as free functions;
    unbound method generics are gradual for arg checking.  
    → RESOLVED Fixed 2026-08-07 / FOUND Story 11 §4 residual.

15. ~~**`is` / `isa` / `isan` like `instanceof`**~~ — **Done** (2026-08-07)  
    Product already correct (lexer/`InstanceOf`/narrowing/emit); suite + guides refreshed.  
    → Confirmed already fixed.

16. ~~**Anonymous `new struct` / clone struct**~~ — **Done** (2026-08-07)  
    Clone was already emitted; anonymous decls were skipped in the binder — now bound and
    emit as defaults arrays. Type-position `struct {…}` remains out of grammar (not P1).  
    → RESOLVED Fixed 2026-08-07.

### P2 — deferred / later / low urgency

17. **Full tyhpdef type-based overload selection** — Partial (Story 16.5 Phase 7)
    Same-arity assignability pick landed (`FunctionOverloadSelector`) so
    `call_user_func_array` Struct vs Tuple bags select correctly. Documented
    exact → static-value → compatible priority and ambiguity diagnostics remain
    later (not P0).

18. **Template string extras** (char classes, infer, inline groups) — Open-backlog / later  
    → Story 08.5 Phase 6 deferred subset.

19. **`(decimal)` cast** — Open-backlog / parse-emit as needed  
    → Guide §4 / §28.

20. **Mechanism A residual migration** — docs/naming debt (not flat-A emit)  
    → [Mechanism A residual debt](#mechanism-a-residual-debt-audit-2026-08-06). Do **not** delete
    `RequiresGenericVariant` / `GenericCallTargets` / `__tyhpGeneric` — those are D (and C).

21. ~~**Subclass-as-`class-string`** — via `__CompatibleTypeName<T>` (not `__ClassName`)~~ — **Done** (2026-08-11)
    → RESOLVED Fixed 2026-08-11 parametric CompatibleTypeName.

---

## Mechanism A residual debt (audit 2026-08-06)

**Verdict:** Flat Mechanism A emit is **gone**. Hot paths already implement Mechanism D under A-era
names. Remaining work is **rebrand / clarify**, not a second emit rewrite. Do **not** block P0
generic assignability on deleting these symbols.

### Taxonomy (do not conflate)

| Label | Shape | Status |
|-------|--------|--------|
| **A (flat callable)** | `foo__tyhpGeneric(?Type $t, …$valueArgs)` body = author body | **Superseded; none found** in `runtime/packages/*/src` or emitter |
| **D (callable binder)** | `foo__tyhpGeneric(?Type $t): \Closure` → value Closure; call `binder(types…)(values…)` | **Live** — `TyhpEmitter.GenericVariants.cs` |
| **C (class bag)** | `$this->__tyhpGeneric`, `__initGenerics__tyhpGeneric`, `new_<MangledFqn>__tyhpGeneric` | **Live** — separate; keep |

### Inventory

| Area | Path / symbol | Role today | Dead / active / dual? | Naive removal risk |
|------|---------------|------------|------------------------|--------------------|
| Emitter | `TyhpEmitter.GenericVariants.cs` (`EmitGenericVariantPair`, `TryBuildGenericVariantCall`) | D pair + curried call sites | **Active hot path (D)** | Breaks all callable generics |
| Emitter | `TyhpEmitter.Declarations.cs` `BuildDeclarationParameterList` | Binder = type-args only when `_currentVariantGenericParams` set | **Active (D)** | Flat or wrong signatures |
| Emitter | `EmitContext.RequiresGenericVariant` / `GenericCallTargets` | Checker→emit side channels | **Active (D)**; XML still says “Mechanism A” | Silent miss of binders / wrong calls |
| Emitter | `TyhpEmitter.Expressions.cs` typeof/default/`is` comments | Resolve `$__generic_*` / GenericObject | **Active**; comments say A | Low if comment-only; high if logic deleted |
| Emitter | `TyhpEmitter.GenericClasses.cs` / PropertyAccessors | Mechanism C bag + factories | **Active (C)** — not A | Breaks class generics / hooks |
| Checker | `TyhpChecker.RequiresGenericVariant`, `MarkRequiresGenericVariant`, `PropagateGenericVariantAcrossHierarchies` | Flag callables needing D binders; hierarchy pair-or-none | **Active (D)**; docs still describe A “leading params” | Override/interface ABI holes |
| Checker | `GenericCallTargets` / `RecordGenericCallTargetsIn` (`TypeInferrer.Dereferenceables.cs`) | Explicit type-arg call routing | **Active (D)** | Call sites lose curry rewrite |
| Checker | `DeclarationRule.Callable.FlagGenericVariantIfNeeded`, `CheckerHelpers.UsesGenericAtRuntime` | Decide when binder needed | **Active (D)** | Missing binders or over-emit |
| Checker | `CompileTimeRule` / `CheckerHelpers` “Mechanism A variant” comments | typeof/default/`is` on callable generics | **Active**; naming lag | Same as D |
| Pipeline | `CompilationResult`, `CompilationService`, `BuildAction`, test harnesses | Pass flags into `EmitContext` | **Active** | Emit without flags → erase-only PHP |
| Names | `GeneratedNames.GenericVariantSuffix` (`__tyhpGeneric`) | Shared D+C suffix; XML says “Mechanism A” | **Active** | PHP ABI break |
| Runtime D | `core` `Type::isType__tyhpGeneric`, `Generic::bind`; `async` `Promise::*__tyhpGeneric` binders; `lambda` Expression* binders | Closure binders + curry calls | **Active (D)** — no flat A found | Interop / async / lambda break |
| Runtime C | `$__tyhpGeneric` bag, `__initGenerics__tyhpGeneric`, `new_*__tyhpGeneric` | Class generics | **Active (C)** | Class generic runtime break |
| Runtime mislabel | `HasGenerics.tyhp` / `.php` (“Mechanism A tracking”) | Hosts C bag | **Active (C)**; wrong label | Comment-only if fixed |
| Tests | `GenericVariantEmitterTests` | Asserts **D** shapes (`: \Closure`, `binder()()` ) | **Active (D)** — not A | Do not “fix to A” |
| Tests | `MechanismCEmitterTests`, `GenericObjectEmitterTests`, `PropertyHookEmitterTests` | C / factories | **Active (C)** | — |
| Tests | `DefaultAndGenericTrackingRuleTests`, `InstanceofGenericParameterEmitterTests` | Comments say A; assert D/C | Naming lag | — |
| Guides | `Checker/technical-guide.md` §9.4 + Open Q1 | Still frames flags as A; asks if D landed | **Stale** | Agents may “migrate emit” again |
| Guides | `Emitter/technical-guide.md` | Correctly says emit is D; notes comment lag | Mostly current | — |
| Historical | `FOUND_BUGS.md` / `RESOLVED_BUGS.md` Mechanism A/D prose | Design record | Keep as history | Don’t delete; don’t treat rebuild-A note as current |

**Package scan (2026-08-06):** Every callable `*__tyhpGeneric` in `runtime/packages/{core,async,lambda}/src` returns `\Closure` (D) or is a C factory/init hook. No flat A `(…$typeArgs, …$valueArgs)` body found.

### Phased migration order (docs first; no behavior change until optional rename)

| Phase | Work | Risk |
|-------|------|------|
| **0** | Rebrand comments/XML/guides: A → D (callable) or C (bag). Close Checker guide Open Q1: flags already drive D. Fix `HasGenerics` “Mechanism A tracking” → Mechanism C. Update `GeneratedNames` / `TyhpChecker` docs that still say “leading parameters”. | **Done** (2026-08-06) — docs/comments only; no symbol renames |
| **1** | Optional cosmetic API rename (`RequiresGenericVariant` → e.g. `RequiresGenericBinder`) across checker → `CompilationResult` → emit + tests. **Not required** for correctness. | Medium churn; easy to miss a harness (future; not this PR) |
| **2** | **Do not** remove `__tyhpGeneric` / `$__tyhpGeneric` / factories — ABI. Do not “strip dual emission”; D *is* the pair. | Catastrophic if done |
| **3** | When packages rebuild clean: confirm `tyhp build` emit matches hand-synced D PHP (fidelity), not A→D migration | Checker WIP blockers elsewhere |

**Phase 0 Done (2026-08-06).** Proceed to P0 #1 (generic same-type assignability). **Do not** start Phase 1/2 or delete flags in that fix — assignability is independent of A naming debt.

### Naive-removal risks (summary)

- Deleting `RequiresGenericVariant` / `GenericCallTargets` → no binders / no curry → runtime type erasure & wrong PHP.
- Deleting `$__tyhpGeneric` / init / factories → Mechanism C collapse.
- “Migrating packages off `__tyhpGeneric`” → packages are already D/C; would break `\Tyhp\Generic::bind` and tests.

---

## Emitter / call-site spillover

Items found while auditing checker intent that are **not pure checker** (or need emit to honor checker results). Keep short; detail stays in FOUND.

| Item | Why it matters for checker intent | Cite | Status |
|------|-----------------------------------|------|--------|
| Emitter drops **inferred** PHP typehints (closure returns, etc.) | Checker knows the type; emitted PHP loses it | FOUND 2026-08-06 emit inferred typehints → RESOLVED 2026-08-11 | **Done** |
| Mechanism A residual (naming/docs; emit already D) | Stale A wording can mislead agents into redoing D | [A residual audit](#mechanism-a-residual-debt-audit-2026-08-06) | **Done** (Phase 0); optional rename = Phase 1 |
| Method-call generic inference path | Checker return typing incomplete for methods | FOUND Story 11 §4 residual | Partial |
| Anonymous struct / clone | Needs emit (+ checker) to “work as intended” | Guide §10 | Open-backlog |
| `(decimal)` cast | Likely parse/emit; checker only if cast typing is specified | Guide §4 | Open-backlog |
| Reserved keyword class name → NRE abort | Parse/visitor hardening | FOUND 2026-08-05 §3 | Open |
| Docs still say bare-only `class_exists` narrowing | Doc debt with High checker work | FOUND 2026-08-06 §6 | Open (with feature) |

---

## Open questions / maintenance notes

1. **INCOMPLETE.md reconcile (2026-08-06):** Story 08 Top-type #5 / `switch (true)` is **Done** in both files (RESOLVED 2026-08-03). This file remains the living capability matrix; `INCOMPLETE.md` holds story-level themes.
2. **Ctor/dtor 4153 suite-red §8 — reconciled 2026-08-06:** **Done** (Skip noise). RESOLVED_BUGS item 40 fix holds; `ControlFlowRule.CheckReturn` reports 4153. All 7 `ConstructorDestructorReturnRuleTests` pass with the two suite-red Facts un-skipped. Remaining hygiene: un-skip those Facts and move FOUND suite-reds §8 to RESOLVED (not a checker backlog item).
3. **Variance:** Same-declaration generic arg comparison is **invariant** for user generics (Done 2026-08-06 / P0 #1), plus a one-way `G<T>` → `G<mixed>` carve-out when `T` is not `void`/`never` (P0 #1 follow-up 2026-08-06). Explicit declared `in`/`out` variance remains future design; `array`/`iterable` stay covariant.
4. **Maintenance:** When closing a P0/P1 item, flip the matrix row to Done, leave the FOUND entry for humans to move to RESOLVED, and trim the ranked list. Prefer linking stories over copying phase checklists.
5. **Story 16.5** callable-signature utilities and ExtStandard `call_user_func*` retype landed (Phase 7). Residual: documented exact→static-value→ambiguity overload diagnostics (P2 #17); peer stubs (`forward_static_call*`, etc.) stay commented.
6. **Do not** treat former guide ⚠️ rows as “won’t implement”; promote and schedule.
7. **Mechanism A vs D naming (resolved by audit 2026-08-06; Phase 0 Done):** Checker flags already feed Mechanism D emit. Comments/guides rebranded A→D/C; Checker guide Open Q1 closed. Optional API rename remains Phase 1 — see [A residual audit](#mechanism-a-residual-debt-audit-2026-08-06).

---

## Quick cite index

| Theme | Primary cite |
|-------|----------------|
| Generic same-type assignability | FOUND 2026-08-05 generic structs §1 |
| Symbol-name guards / `__ClassName` | FOUND 2026-08-06 audit §§1–6 |
| `true`/`false` / `true\|false` | FOUND suite-reds §4; FOUND #42 |
| Property hooks unchecked bodies | FOUND property-hook follow-up §1 |
| Trait + dynamic property | FOUND Story 08 Phase 6 reopen §1 |
| Unused import type-only | FOUND Story 12 Phase 6 §3 |
| Overloads / Story 16.5 | FOUND 2026-08-05 §2 → RESOLVED 2026-08-13 Phase 7; residual exact/static-value/ambiguity in P2 #17 |
| Generic defaults | Story 28; guide §7 |
| Array literal inference | Story 08; guide §5 |
| `[$obj,'method']` array-callable → `callable` | RESOLVED Fixed 2026-08-07 (user-reported, `Generic.tyhp:495`) |
| Mechanism D / A residual | FOUND Mechanism D; RESOLVED 2026-08-04; [A residual audit](#mechanism-a-residual-debt-audit-2026-08-06) |
| `switch (true)` Done | RESOLVED Top-type #5 2026-08-03 |
| Ctor/dtor 4153 Done | RESOLVED_BUGS item 40; FOUND suite-reds §8 was stale Skip |
