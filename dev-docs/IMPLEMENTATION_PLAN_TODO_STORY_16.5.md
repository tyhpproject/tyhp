# Implementation Plan: Story 16.5 — Callable Signature Utilities (`__CallableParameters*` / `__CallableReturnType`)

> **Roadmap position:** Story 16.5 — **Tier 1 — Usable** (additive sub-story, inserted after Story 16, before the bug-fixes break / Tier 2)
> **Direct dependencies (new numbering):** 08, 08.5, 11
> **New story:** callable-keyed signature reflection utilities so builtins like `\call_user_func` / `\call_user_func_array` can correlate callback ↔ args ↔ return without arity-overload explosion or homogeneous `array<string, T1|T2|…>` bags.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-08-05
> **Prerequisites:** Story 08 (checker), Story 08.5 (utility-type registration / `__Struct*` family / `__FunctionReturnType` / `__MethodReturnType` surface), Story 11 (structs mature enough for synthetic parameter bags and structural assignability).
> **Consumers:** Runtime tyhpdefs (commented “once we have …” targets already marked in-tree):
> `ExtStandard` — `call_user_func`, `call_user_func_array`, `forward_static_call`, `forward_static_call_array`,
> `register_shutdown_function`, `register_tick_function`; `ExtSPL` — `iterator_apply`; `ExtCore` —
> `\Closure::call`; `ExtReflection` — `ReflectionFunction`/`ReflectionMethod` `invoke`/`invokeArgs`,
> `ReflectionClass`/`ReflectionObject`/`ReflectionEnum` `newInstance`/`newInstanceArgs` (may share Story 27).
> Story 21 stub enrichment; docs (`tyhp_0150_newTypes.md`).

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [Scope (In / Out)](#scope-in--out)
- [Decisions (locked)](#decisions-locked)
- [Target tyhpdef surface](#target-tyhpdef-surface)
- [Architecture Overview](#architecture-overview)
- [Phase 1: Utility type registration + signature reflection API](#phase-1-utility-type-registration--signature-reflection-api)
- [Phase 2: `__CallableReturnType<TCallable>`](#phase-2-__callablereturntypetcallable)
- [Phase 3: `__CallableParametersStruct<TCallable>` (named bags)](#phase-3-__callableparametersstructtcallable-named-bags)
- [Phase 4: `__CallableParametersTuple<TCallable>` (positional bags)](#phase-4-__callableparameterstuletcallable-positional-bags)
- [Phase 5: Optional parameters via partial / required-key assignability](#phase-5-optional-parameters-via-partial--required-key-assignability)
- [Phase 6: Rest / variadic unpack for `call_user_func` (if needed)](#phase-6-rest--variadic-unpack-for-call_user_func-if-needed)
- [Phase 7: Retype builtins + docs + conformance](#phase-7-retype-builtins--docs--conformance)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

Deliver TypeScript-style **callable-keyed** signature utilities:

| Utility | Meaning |
|---------|---------|
| `__CallableReturnType<TCallable>` | Return type of callable type `TCallable` |
| `__CallableParametersStruct<TCallable>` | Named-arg bag (string keys → param types) as a synthetic **struct** |
| `__CallableParametersTuple<TCallable>` | Positional-arg bag (int keys `0..n-1`) as a synthetic **struct** (same shape family as hand-written `CallableArgs*`) |

APIs bind a single type parameter from the callback argument (inference), then derive `$args` / return from it — **no** `typeof($callback)` in type position for this story.

Optional parameters are modeled with **partial / required-key** assignability (not a full power-set intersection of bags), reusing `__StructPartial` / struct optionality where possible.

---

## Motivation

Today `\call_user_func_array` is typed with either:

- `array<string, T1|T2|…>` — loses key↔type correlation, or
- hand-written `CallableArgs1`…`CallableArgs16` — positional only, arity-capped, manually maintained.

`\call_user_func` is a 17-way arity overload ladder for the same reason.

Name-string utilities from Story 08.5 (`__FunctionReturnType<'strlen'>`, `__MethodReturnType<T, M>`) help when the callee is a **literal name**. Higher-order builtins pass a **callable value**; they need utilities keyed by the **callable type**, analogous to TypeScript’s `Parameters<T>` / `ReturnType<T>`.

---

## Scope (In / Out)

| In scope | Out of scope |
|----------|----------------|
| `__CallableReturnType` / `__CallableParametersStruct` / `__CallableParametersTuple` | `typeof(expr)` as a generic type argument (deferred; TS-style `TCallable` inference is enough) |
| Checker resolution from concrete callable / closure / function / method / arity-facet types | Labeled `callable<string $name, …>` syntax (names come from symbol signatures or best-effort from facets) |
| Synthetic struct construction for named + positional bags | Replacing all Story 08.5 name-string utilities (`__FunctionReturnType<'f'>` stays) |
| Partial/required handling for defaults | Infinite variadic expansion |
| Retype `\call_user_func` / `\call_user_func_array` (and document the pattern for peers) | Rewriting every higher-order stub in ExtStandard in one pass |
| Docs in `tyhp_0150_newTypes.md` | Story 27 `new<>` bags (related pattern; separate story) |

---

## Decisions (locked)

1. **TS-style generics, not `typeof` in type args.**  
   `function f<TCallable extends callable>(TCallable $cb, __CallableParametersStruct<TCallable> $args): __CallableReturnType<TCallable>`  
   Hold `typeof($x)` in type position for a later story if still needed for ad-hoc annotations.

2. **Optional params → partial struct / required-key rules**, not an intersection of every optional-key subset (avoids `2^k` blow-up). Positional arity facets remain the model for *call* arity; named bags use struct optionality.

3. **Positional bags are first-class** via `__CallableParametersTuple` (auto-built), so hand-maintained `CallableArgs*` can be retired from builtins once this lands. Manual `CallableArgs*` may remain as interim / examples until Phase 7.

4. **`call_user_func_array` keeps two overloads** (named struct vs positional tuple) matching PHP’s dual key rules.

5. **Names:** when the callable type is a bare `callable<TArgs…, TReturn>` facet with no parameter names, Tuple still works (positional types); Struct may degrade (no string keys) or be unavailable — prefer symbol-backed callables (functions, methods, closures with real params) for named bags.

---

## Target tyhpdef surface

### `\call_user_func_array` (yes — this is the shape)

```tyhp
function call_user_func_array<TCallable extends callable>(
    TCallable $callback,
    __CallableParametersStruct<TCallable> $args
): __CallableReturnType<TCallable>;

function call_user_func_array<TCallable extends callable>(
    TCallable $callback,
    __CallableParametersTuple<TCallable> $args
): __CallableReturnType<TCallable>;
```

These replace the arity-capped `CallableArgs*` / `array<string, …>` ladders once the utilities resolve.

### `\call_user_func` (better than the 0..16 overload ladder)

**Ideal (preferred once Phase 6 lands):** one signature whose rest args are the unpack of the callable’s parameter list:

```tyhp
function call_user_func<TCallable extends callable>(
    TCallable $callback,
    __CallableParametersRest<TCallable> ...$args
): __CallableReturnType<TCallable>;
```

(Exact spelling of “rest typed from tuple” is an implementation detail of Phase 6 — e.g. variadic parameter whose type is derived from `__CallableParametersTuple<TCallable>`, or a dedicated rest utility.)

**Fallback if rest-from-tuple is deferred:** keep a *thin* arity overload ladder, but collapse return typing to `__CallableReturnType<…>` and/or infer `TCallable` from `$callback` so each overload is shorter. Do **not** invent a homogeneous `mixed ...$args` that drops argument checking.

Until Phase 6, the existing 17 overloads remain acceptable interim stubs.

---

## Architecture Overview

```
TCallable (inferred from $callback)
    │
    ▼
Signature reflection (checker)
  { name?, type, optional, variadic, byRef }[]
    │
    ├─► __CallableReturnType          → return CheckedType
    ├─► __CallableParametersStruct    → synthetic struct (string keys)
    └─► __CallableParametersTuple     → synthetic struct (int keys 0..n-1)
              │
              └─► optional keys marked optional / stripped via __StructPartial
                    for assignability of partial bags
```

**Reuse:** `UtilityTypeResolver` / `UtilityBehavior` (Story 08.5), `CallableArityFacetBuilder` (facet → arg types / return), `__StructDef` / `__StructPartial` / structural struct assignability (08.5 / structs), existing `CallableArgs*` as the *shape* reference for Tuple emit-as-array.

---

## Phase 1: Utility type registration + signature reflection API

### Deliverables

1. Register `__CallableReturnType`, `__CallableParametersStruct`, `__CallableParametersTuple` (and optionally `__CallableParametersRest` if Phase 6 needs a named utility) in the built-in utility surface (`UtilityBehavior` + binder registration), same pattern as `__FunctionReturnType`.
2. Shared checker helper: given a `CheckedType` known to be callable-ish, produce an ordered parameter list + return type (walk facets / closure / function / method symbols).
3. Erasure: utilities erase like other `__` utilities (to the resolved concrete type / struct-as-array); no new runtime PHP types.

### Acceptance Criteria

- [ ] Utilities are resolvable in type position with one type argument
- [ ] Non-callable type args produce a clear diagnostic
- [ ] Reflection agrees with arity-facet arg counts for simple `callable<…>` cases

---

## Phase 2: `__CallableReturnType<TCallable>`

### Behavior

- Resolves to the return type of `TCallable` (facet-aware: prefer the selected facet when known; otherwise the common return / first facet policy consistent with call checking).
- Align with / complement name-string `__FunctionReturnType` / `__MethodReturnType` (those stay; this is the callable-type variant).

### Acceptance Criteria

- [ ] `callable<string, int>` → `int`
- [ ] Closure / function symbols → declared return type
- [ ] Works as the return type of a generic `call_user_func*` signature under inference

---

## Phase 3: `__CallableParametersStruct<TCallable>` (named bags)

### Behavior

- Builds a synthetic struct with one property per non-variadic parameter; property name = parameter name; type = parameter type.
- Used for named-key assoc arrays passed to `call_user_func_array`.

### Acceptance Criteria

- [ ] Wrong key → unknown property / struct error
- [ ] Wrong type for key → assignability error
- [ ] Correct named bag assigns and returns `__CallableReturnType`

---

## Phase 4: `__CallableParametersTuple<TCallable>` (positional bags)

### Behavior

- Synthetic struct with int key aliases `0 as $_1`, `1 as $_2`, … matching current `CallableArgs*` convention.
- Used for positional `call_user_func_array` arrays.

### Acceptance Criteria

- [ ] Positional literal / array assigns when types match
- [ ] Can replace hand-written `CallableArgsN<…>` in tyhpdefs for the same arities tested

---

## Phase 5: Optional parameters via partial / required-key assignability

### Behavior

- Required parameters → required struct fields.
- Parameters with defaults → optional fields (nullable-or-optional struct semantics / `__StructPartial` as appropriate).
- Variadic: either a final optional `array<T>`-like field or leave excess unchecked (match arity-facet policy — document the choice).

### Acceptance Criteria

- [ ] Omitting a defaulted named param is allowed
- [ ] Omitting a required named param is an error
- [ ] No exponential intersection of subset structs required for correctness

---

## Phase 6: Rest / variadic unpack for `call_user_func` (if needed)

### Behavior

Enable a single `\call_user_func` signature whose trailing variadic arguments are checked against the callable’s parameter list (TypeScript `...args: Parameters<T>` analogue).

If this cannot land cleanly in this story, keep Phase 7’s `call_user_func` as the interim overload ladder and file the rest-unpack gap explicitly in `INCOMPLETE.md` / follow-up — **do not** ship unchecked `mixed ...$args`.

### Acceptance Criteria

- [ ] Either one rest-typed signature works end-to-end, **or** documented deferral with overloads retained and return type improved via `__CallableReturnType` where practical

---

## Phase 7: Retype builtins + docs + conformance

### Deliverables

1. Replace `ExtStandard.tyhpdef` `call_user_func_array` ladders with the two-overload Struct/Tuple form (uncomment / activate the planned signatures).
2. Improve `call_user_func` per Phase 6 outcome.
3. Document utilities in `docs/content/tyhp_0150_newTypes.md` under Type Utility Types (alongside `__FunctionReturnType` / `__MethodReturnType`).
4. Conformance / checker tests under `tests/Tyhp.Tests/Checker/` and optionally `tests/conformance/story16_5/`.

### Acceptance Criteria

- [x] Builtins compile in the binder/checker
- [x] Golden cases for named + positional `call_user_func_array`
- [x] Docs list the three primary utilities and the locked decisions

---

## Cross-Story References

| Story | Relationship |
|-------|----------------|
| **08** | Checker / assignability / call checking |
| **08.5** | Utility type machinery; `__Struct*`; name-string `__FunctionReturnType` / `__MethodReturnType` |
| **11** | Struct emission / structural typing maturity |
| **16** | Expression trees also traffic in callables — no hard dependency; sequenced adjacent in Tier 1 |
| **21** | Stub enrichment can adopt the same pattern for other higher-order PHP functions |
| **27** | `new<>` optional-arity facets — sibling *pattern*, not shared deliverable |

---

## Golden Fixtures / Tests (Acceptance)

- [x] Resolve `__CallableReturnType` for `callable<A,B,R>`, named function, and closure
- [x] Named bag: valid keys/types OK; bad key; bad type
- [x] Positional tuple: matches `CallableArgs*`-style arrays
- [x] Optional default omitted OK; required omitted fails
- [x] Inference: `call_user_func_array($cb, $args)` correlates without explicit type args at the call site
- [x] Regression: existing callable arity-facet assignability tests still green
