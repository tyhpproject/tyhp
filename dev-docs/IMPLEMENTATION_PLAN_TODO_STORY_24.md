# Implementation Plan: Story 24 — Advanced Optimizations

> **Roadmap position:** Story 24 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** 23
> **Renumbered from:** legacy Story 4.6
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 24 of the Tyhp compiler TODO
> **Branch:** TBD
> **Generated:** 2026-03-19
> **Prerequisites:** Story 23 (Compiler Optimizer MVP — framework, extension inlining, basic optimizations)

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Phase 1: Operator Chain Optimization Module](#phase-1-operator-chain-optimization-module)
- [Phase 2: Null-Safe Chain Collapsing Module](#phase-2-null-safe-chain-collapsing-module)
- [Phase 3: Type Guard Elimination Module](#phase-3-type-guard-elimination-module)
- [Phase 4: Devirtualization Module](#phase-4-devirtualization-module)
- [Phase 5: Struct Copy Elision Module](#phase-5-struct-copy-elision-module)
- [Phase 6: Pure Function Memoization, `#[\Tyhp\Optimize\Pure]` Attribute, and `#[\Tyhp\Optimize\Memoize]` Attribute](#phase-6-pure-function-memoization-tyhppure-attribute-and-tyhpmemoize-attribute)
- [Phase 7: Cross-Reference Constant Folding Module](#phase-7-cross-reference-constant-folding-module)
- [Phase 8: Escape Analysis Module](#phase-8-escape-analysis-module)
- [Phase 9: Tyhp Reflection API (Foundation)](#phase-9-tyhp-reflection-api-foundation)
- [Cross-Story References](#cross-story-references)

---

## Architecture Overview

### What This Story Adds

Story 23 established the optimizer framework and implemented the MVP modules (extension operator inlining, extension method inlining, synthetic class elimination, constant folding, dead code elimination). This story adds the advanced optimization modules that unlock deeper performance improvements. (Note: unused import pruning is **not** an optimizer module — it is performed unconditionally by the emitter's `PruneFileImports()`, Story 09 Phase 6, even when optimization is disabled.)

Each module follows the same `IOptimizationModule` interface from Story 23 and is independently toggleable via `build.optimizations` in `tyhp.json`. Modules are registered in `TyhpOptimizer.RegisterBuiltInModules()` alongside the existing MVP modules.

### Module Registry (Complete After This Story)

| Module | ConfigKey | Priority | MinLevel | Story |
|--------|-----------|----------|----------|-------|
| Extension Operator Inlining | `extensionOperatorInlining` | 100 | Basic | 23 |
| Extension Method Inlining | `extensionMethodInlining` | 200 | Basic | 23 |
| Synthetic Class Elimination | `syntheticClassElimination` | 300 | Basic | 23 |
| Constant Folding (literals) | `constantFolding` | 400 | Basic | 23 |
| **Cross-Ref Constant Folding** | `crossReferenceConstantFolding` | 450 | Aggressive | **24** |
| Dead Code Elimination | `deadCodeElimination` | 500 | Basic | 23 |
| **Operator Chain Optimization** | `operatorChainOptimization` | 700 | Aggressive | **24** |
| **Null-Safe Chain Collapsing** | `nullSafeChainCollapsing` | 800 | Basic | **24** |
| **Type Guard Elimination** | `typeGuardElimination` | 900 | Basic | **24** |
| **Devirtualization** | `devirtualization` | 1000 | Aggressive | **24** |
| **Struct Copy Elision** | `structCopyElision` | 1100 | Aggressive | **24** |
| **Pure Function Memoization** | `pureFunctionMemoization` | 1200 | Aggressive | **24** |
| **Memoize Call Deduplication** | `memoizeCallDeduplication` | 1250 | Basic | **24** |
| **Escape Analysis** | `escapeAnalysis` | 1300 | Aggressive | **24** |

### MessageCode Additions

Story 24 adds diagnostic codes in the **4712–4749** range, contiguous after Story 23's optimizer codes (4700–4711). (These are checker-range `4xxx` codes; the earlier "4520–4549" numbering was incorrect — it was both non-contiguous with 4700–4711 and overlapped Story 08's deprecation codes 4500–4501.)

| Code | Name | Severity | Description |
|------|------|----------|-------------|
| 4712 | `OptimizerOptimizedOperatorChain` | Info | An operator chain was optimized to reduce intermediate allocations |
| 4713 | `OptimizerCollapsedNullSafeChain` | Info | A null-safe method chain was collapsed |
| 4714 | `OptimizerEliminatedTypeGuard` | Info | A redundant type guard check was eliminated |
| 4715 | `OptimizerDevirtualizedCall` | Info | A virtual method call was devirtualized to a direct call |
| 4716 | `OptimizerElidedStructCopy` | Info | A struct copy was elided |
| 4717 | `OptimizerMemoizedPureFunction` | Info | A pure function call was memoized (via `#[\Tyhp\Optimize\Pure]`) |
| 4718 | `OptimizerFoldedConstantReference` | Info | A constant reference was folded to its literal value |
| 4719 | `OptimizerEscapeAnalysisOptimized` | Info | An allocation was optimized based on escape analysis |
| 4720 | `OptimizerPureAttributeInvalidTarget` | Warning | `#[\Tyhp\Optimize\Pure]` used on a function with detectable side effects (and `force` is not `true`) |
| 4721 | `OptimizerMemoizedExpensiveCall` | Info | A `#[\Tyhp\Optimize\Memoize]`-annotated function call was deduplicated |
| 4722 | `OptimizerMemoizeAttributeInvalidTarget` | Warning | `#[\Tyhp\Optimize\Memoize]` used on a function that cannot be memoized |
| 4723 | `OptimizerMemoizeCacheInvalidated` | Info | A `#[\Tyhp\Optimize\Memoize]` cache was invalidated due to intervening side effects |
| 4724 | `OptimizerPureForcedUnsafe` | Warning | `#[\Tyhp\Optimize\Pure(force: true)]` used — compiler cannot verify purity; developer accepts responsibility |

### File Organization

```
Tyhp/TyhpLang/Optimizer/
├── Modules/
│   ├── (existing MVP modules from Story 23)
│   ├── OperatorChainOptimizationModule.cs      (~300 lines) — Phase 1
│   ├── NullSafeChainCollapsingModule.cs        (~200 lines) — Phase 2
│   ├── TypeGuardEliminationModule.cs           (~250 lines) — Phase 3
│   ├── DevirtualizationModule.cs               (~300 lines) — Phase 4
│   ├── StructCopyElisionModule.cs              (~250 lines) — Phase 5
│   ├── PureFunctionMemoizationModule.cs        (~200 lines) — Phase 6
│   ├── MemoizeCallDeduplicationModule.cs       (~250 lines) — Phase 6
│   ├── CrossRefConstantFoldingModule.cs        (~200 lines) — Phase 7
│   └── EscapeAnalysisModule.cs                 (~350 lines) — Phase 8
└── Attributes/
    ├── InlineAttribute.cs                      (existing from Story 23)
    ├── PureAttribute.cs                        (~50 lines)  — Phase 6
    └── MemoizeAttribute.cs                     (~30 lines)  — Phase 6
```

---

## Phase 1: Operator Chain Optimization Module




### Phase Overview

Optimize chains of overloaded operator calls to reduce intermediate object allocations. When operators return new instances (common for value-type semantics like `Decimal`), a chain like `$a + $b + $c + $d` creates three intermediate objects. This module can detect specific patterns and generate more efficient code.

### Implementation Details

**Target pattern:**

```tyhp
$result = $a + $b + $c + $d;
```

Without optimization, this emits:

```php
$temp1 = $a->add($b);
$temp2 = $temp1->add($c);
$result = $temp2->add($d);
```

**Optimization strategies:**

1. **Variadic accumulation:** If the underlying type has a variadic `addAll(...$values)` or similar batch method (discoverable via symbol lookup), rewrite to a single call: `$result = $a->addAll($b, $c, $d);`

2. **In-place mutation:** If the type has a mutable `addInPlace()` method (for internal/private usage only), rewrite to: `$result = clone $a; $result->addInPlace($b); $result->addInPlace($c); $result->addInPlace($d);`

3. **Intermediate elimination:** When intermediate results are not referenced elsewhere, avoid assigning them to temporaries and chain directly.

**Emitter behavior note:** The emitter (Story 09) already produces chained method calls without intermediate temporaries (e.g., `($a->add($b))->add($c)` rather than `$temp1 = $a->add($b); $result = $temp1->add($c);`). This means strategy 3 (intermediate elimination) is already accomplished by default emitter behavior.

**Scope for this story:** Focus on strategy 1 (variadic accumulation). The module detects chains of 3+ operator calls on the same type and checks if the underlying type has a variadic batch method (e.g., `addAll(...$values)`) discoverable via symbol lookup. If found, the chain is rewritten to a single batch call. Strategy 2 (in-place mutation) is deferred as it requires deeper analysis of mutability.

### Acceptance Criteria

- [ ] Module has `ConfigKey = "operatorChainOptimization"`, `Priority = 700`, `MinimumLevel = Aggressive`
- [ ] Chains of 3+ operator calls on the same type are detected
- [ ] If the type has a variadic batch method (e.g., `addAll()`), the chain is rewritten to a single batch call
- [ ] Types without variadic batch methods are left unchanged (the emitter already chains calls without intermediates)
- [ ] Observable behavior is preserved (each operator call still executes in order)
- [ ] Metrics report chain lengths, batch method rewrites, and optimizations applied

---

## Phase 2: Null-Safe Chain Collapsing Module




### Phase Overview

Collapse redundant null-safe operator usage in method chains. When a chain uses `?->` on every call but the first call's null check already guarantees non-null for subsequent calls, the later `?->` operators can be replaced with regular `->`.

### Implementation Details

**Target pattern:**

```tyhp
$result = $user?->getProfile()?->getSettings()?->getTheme();
```

If `getProfile()` returns a non-nullable type (e.g., `Profile` not `?Profile`), the second `?->` is redundant. The checker's type information reveals this.

**Optimization:** Replace redundant null-safe operators with regular method calls where the receiver type is known to be non-nullable.

### Acceptance Criteria

- [ ] Module has `ConfigKey = "nullSafeChainCollapsing"`, `Priority = 800`, `MinimumLevel = Basic`
- [ ] Redundant `?->` on non-nullable receiver types are replaced with `->`
- [ ] Nullable receiver types retain `?->`
- [ ] The optimization respects the full type narrowing context from the checker

---

## Phase 3: Type Guard Elimination Module




### Phase Overview

Remove redundant type guard checks where the checker's type narrowing has already proven the type. When a variable's type is known to be narrowed (e.g., after a previous `instanceof` check or assignment), subsequent `instanceof` checks for the same type are unnecessary.

### Implementation Details

**Target pattern:**

```tyhp
if ($value instanceof string) {
    // ... code ...
    if ($value instanceof string) {  // redundant
        // ...
    }
}
```

**Optimization:** Replace the redundant check with `true` (which dead code elimination can then use to unwrap the if block).

**More complex case:**

```tyhp
function process(int|string $value): void {
    if ($value instanceof string) {
        handleString($value);
        return;
    }
    // At this point, $value is narrowed to int
    $value instanceof int;  // Always true — can be eliminated
}
```

The type guard elimination module leverages the checker's `VariableState` type narrowing information to determine which guards are redundant.

The checker persists narrowed type information by storing it on a `NarrowedTypes` side dictionary on `CompilationResult`, keyed by AST node reference. This dictionary maps each AST node where type narrowing occurs to the narrowed type at that point. This module (type-guard elimination) is a **consumer** of `CompilationResult.NarrowedTypes`; the **producer is Story 08** (the checker), which adds `NarrowedTypes` to `CompilationResult` in its Phase 7. `CompilationResult.NarrowedTypes` does not exist today — Story 08 is the canonical story that introduces it, and Story 24 (this story) depends on it being present. (See the Cross-Story References → Prerequisites table below, which lists Story 08 for this data.)

### Acceptance Criteria

- [ ] Module has `ConfigKey = "typeGuardElimination"`, `Priority = 900`, `MinimumLevel = Basic`
- [ ] Redundant `instanceof` checks within narrowed scopes are replaced with `true`
- [ ] Type narrowing after early returns is correctly handled
- [ ] Union type narrowing (elimination of alternatives) is correctly handled
- [ ] Only checks provably redundant by static analysis are eliminated

---

## Phase 4: Devirtualization Module




### Phase Overview

Replace virtual method calls with direct calls when the concrete type is known at compile time. PHP method calls are virtual by default (dynamic dispatch). When the optimizer can prove the exact runtime type, it can emit a direct call or even inline the method body.

### Implementation Details

**Target patterns:**

1. **Final class methods:** Methods on `final` classes cannot be overridden, so calls are always to the exact implementation.
2. **Sealed methods:** Methods marked `final` (in the method declaration) cannot be overridden.
3. **Constructor return types:** `new Foo()` is always exactly `Foo`, so methods called immediately on a constructor result can be devirtualized.
4. **Type-narrowed variables:** After an `instanceof Foo` check (where `Foo` is `final`), method calls can be devirtualized.

**Optimization:** Annotate the call site in the AST with the resolved concrete method, allowing the emitter to skip dynamic dispatch. For PHP, this primarily helps with static analysis and future JIT optimization; the runtime benefit is minimal but it enables further optimizations like inlining.

### Acceptance Criteria

- [ ] Module has `ConfigKey = "devirtualization"`, `Priority = 1000`, `MinimumLevel = Aggressive`
- [ ] Calls on `final` class instances are devirtualized
- [ ] Calls to `final` methods are devirtualized
- [ ] Calls on constructor return values are devirtualized
- [ ] Non-final, non-sealed calls are NOT devirtualized
- [ ] Metrics report devirtualized call count

---

## Phase 5: Struct Copy Elision Module




### Phase Overview

Eliminate unnecessary copies of struct values. Tyhp structs compile to associative arrays, which PHP copies on assignment. When the original value is not used after the copy, the copy is unnecessary.

### Implementation Details

**Target pattern:**

```tyhp
struct Point { int $x; int $y; }

$p = new Point() with { $x = 1, $y = 2 };
$q = $p with { $x = 3 };  // This copies $p, but if $p is not used after this, the copy is wasteful
```

If `$p` is not referenced after the `clone`/`with` expression, the optimizer can rewrite this to mutate `$p` in place (since structs are value types backed by arrays).

**Optimization:** When a struct is copied but the original is not used after the copy point, skip the copy and mutate in place.

### Acceptance Criteria

- [ ] Module has `ConfigKey = "structCopyElision"`, `Priority = 1100`, `MinimumLevel = Aggressive`
- [ ] Struct copies where the original is dead after the copy are elided
- [ ] Structs that are still referenced after the copy are NOT elided
- [ ] Correctly handles struct copies in loops and branches

---

## Phase 6: Pure Function Memoization, `#[\Tyhp\Optimize\Pure]` Attribute, and `#[\Tyhp\Optimize\Memoize]` Attribute




### Phase Overview

This phase introduces two related but distinct compiler attributes — `#[\Tyhp\Optimize\Pure]` and `#[\Tyhp\Optimize\Memoize]` — and two optimization modules that consume them. Together they address two common performance patterns:

1. **Pure functions** (`#[\Tyhp\Optimize\Pure]`) — functions with no side effects that always return the same output for the same input. The optimizer can eliminate duplicate calls, hoist calls out of loops, and reorder calls safely.
2. **Expensive but potentially impure functions** (`#[\Tyhp\Optimize\Memoize]`) — functions that may have side effects (like database queries) but whose results the developer wants cached to avoid redundant execution within a narrow scope.

Both attributes are namespaced under `\Tyhp\Optimize\` (see Story 23 "Compiler Attribute Namespace" section). They are compile-time attributes and are NOT emitted to the PHP output.

### 6a. `#[\Tyhp\Optimize\Pure]` Attribute

**Syntax:**

```tyhp
use \Tyhp\Optimize\Pure;

#[Pure()]
function add(int $a, int $b): int {
    return $a + $b;
}

#[\Tyhp\Optimize\Pure()]
function fibonacci(int $n): int {
    if ($n <= 1) return $n;
    return fibonacci($n - 1) + fibonacci($n - 2);
}
```

**Parameters:**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `force` | `bool` | `false` | When `true`, bypasses the compiler's purity validation. The developer asserts the function is pure even though the compiler cannot statically prove it. |

**`#[\Tyhp\Optimize\Pure()]` (default, `force: false`):**

The compiler validates that the function is genuinely pure:
- Does NOT modify `$this` or any parameter
- Does NOT call non-pure functions
- Does NOT access global/static mutable state
- Does NOT perform I/O (file, network, database, output buffering, etc.)

If validation fails, the compiler emits `OptimizerPureAttributeInvalidTarget` warning and the function is NOT treated as pure for optimization purposes.

**`#[\Tyhp\Optimize\Pure(force: true)]`:**

```tyhp
use \Tyhp\Optimize\Pure;

#[Pure(force: true)]
function expensiveComputation(string $key): ComputedResult {
    return ComputationCache::computeOrRetrieve($key);
}
```

The compiler skips purity validation entirely. This is for functions that are *genuinely* pure in intent but call into code the compiler cannot analyze (FFI, dynamically loaded code, complex library internals). The compiler emits `OptimizerPureForcedUnsafe` warning (informational, not blocking) to acknowledge the developer's assertion.

With `force: true`, **all pure optimizations apply** — duplicate call elimination, loop hoisting, and call reordering. The developer accepts full responsibility for correctness. If the function is not actually pure, these optimizations may produce incorrect results.

**When to use `force: true` vs `#[\Tyhp\Optimize\Memoize]`:**

| Scenario | Use |
|----------|-----|
| Function is truly pure but compiler can't prove it (e.g., calls unanalyzable library code) | `#[\Tyhp\Optimize\Pure(force: true)]` |
| Function has side effects but you want to avoid duplicate calls (e.g., same SQL query) | `#[\Tyhp\Optimize\Memoize]` |

The distinction matters because `#[\Tyhp\Optimize\Pure(force: true)]` enables aggressive optimizations (loop hoisting, reordering) that are **unsafe** for impure functions, while `#[\Tyhp\Optimize\Memoize]` only performs safe duplicate call elimination within a side-effect-aware scope.

**Optimizations enabled by `#[\Tyhp\Optimize\Pure]`:**

1. **Duplicate call elimination:** Identical calls with the same arguments reuse the first result.
2. **Loop hoisting:** Calls with loop-invariant arguments are moved before the loop.
3. **Call reordering:** The optimizer may reorder pure calls relative to other code for better performance.
4. **Constant folding integration:** Pure functions called with all-constant arguments may have their results computed at compile time (future enhancement).

**Optimization — duplicate call elimination:**

```tyhp
$x = compute($a, $b);
// ... code that doesn't modify $a or $b ...
$y = compute($a, $b);  // Same args → reuse $x
```

Rewrites to:

```tyhp
$x = compute($a, $b);
// ... code ...
$y = $x;
```

**Optimization — loop hoisting:**

```tyhp
for ($i = 0; $i < count($items); $i++) {
    $config = getConfig($appId);  // Pure + loop-invariant args
    process($items[$i], $config);
}
```

Rewrites to:

```tyhp
$config = getConfig($appId);
for ($i = 0; $i < count($items); $i++) {
    process($items[$i], $config);
}
```

### 6b. `#[\Tyhp\Optimize\Memoize]` Attribute

**Syntax:**

```tyhp
use \Tyhp\Optimize\Memoize;

#[Memoize()]
function getUserProfile(int $userId): UserProfile {
    return $this->db->query('SELECT * FROM users WHERE id = ?', [$userId]);
}
```

**Purpose:**

`#[\Tyhp\Optimize\Memoize]` is the safe, common-case attribute for "don't repeat this expensive call." It performs **scope-aware duplicate call elimination only** — if the same function is called with identical arguments within a scope where no intervening side effects could have changed the result, the second call is replaced with the cached first result.

Unlike `#[\Tyhp\Optimize\Pure]`, `#[\Tyhp\Optimize\Memoize]`:
- Does NOT enable loop hoisting (the function may have side effects that depend on execution context)
- Does NOT enable call reordering (side effects must execute in the original order)
- Does NOT require purity validation (the function is expected to be impure)
- DOES invalidate the cache when intervening side effects are detected

**AST transformation approach:** The optimizer performs compile-time duplicate call elimination within a single scope using temporary variables. When a memoized call is detected as a duplicate (same function + same arguments, no intervening side effects), the optimizer:
1. Introduces a temporary variable for the first call's result: `$__tyhp_memo_0 = getUserProfile(42);`
2. Replaces the original first call site with the temporary: `$profile1 = $__tyhp_memo_0;`
3. Replaces subsequent duplicate call sites with the temporary: `$profile2 = $__tyhp_memo_0;`

This is purely a compile-time transformation — no runtime caching infrastructure is generated. The scope of deduplication is limited to the current function/method body and resets at any point where intervening side effects are detected.

**Scope-aware caching rules:**

1. The cache is scoped to the current function/method body (never crosses function boundaries).
2. Identical calls = same function + same arguments (by value for scalars, by identity for objects).
3. The cache is **invalidated** at any point where a side effect could change the result:
   - Any non-pure function call (unless it's another `#[\Tyhp\Optimize\Memoize]` call with different arguments)
   - Any assignment to a property, global variable, or reference parameter
   - Any `echo`, `print`, file write, or I/O operation
   - Any `yield` or `await` expression
4. After invalidation, the next call re-executes and starts a new cache entry.

**Example — safe deduplication:**

```tyhp
use \Tyhp\Optimize\Memoize;

#[Memoize()]
function getExchangeRate(string $currency): decimal {
    return $this->rateService->fetchCurrentRate($currency);
}

function convertPrices(array<Product> $products, string $targetCurrency): void {
    foreach ($products as $product) {
        $rate = getExchangeRate($targetCurrency);  // Executes on first iteration, cached for subsequent iterations
        $product->convertedPrice = $product->price * $rate;
    }
}
```

Memoization is intra-scope deduplication, not loop hoisting. The memoized function call executes on the first iteration of the loop within the current scope. Subsequent iterations within the same scope reuse the cached result. The cache key is the function identity plus argument values. In this example, the first iteration executes `getExchangeRate($targetCurrency)` and caches the result; subsequent iterations return the cached value. This is "called once per scope, result cached for subsequent calls within that scope."

**Example — cache invalidation:**

```tyhp
$profile1 = getUserProfile(42);  // Executes
updateUser(42, ['name' => 'New Name']);  // Side effect — invalidates cache for getUserProfile
$profile2 = getUserProfile(42);  // Re-executes (cache was invalidated)
```

The `updateUser` call is a non-pure function that could change the result of `getUserProfile(42)`, so the cache is invalidated.

**Example — no deduplication across different arguments:**

```tyhp
$a = getUserProfile(1);   // Executes
$b = getUserProfile(2);   // Executes (different args)
$c = getUserProfile(1);   // Reuses $a (same args, no intervening side effects)
```

**Validation:**

The checker validates that `#[\Tyhp\Optimize\Memoize]` is applied to:
- Functions or methods (not properties, constants, or class declarations)
- Functions that return a value (void functions cannot be meaningfully memoized)

Applying it to invalid targets emits `OptimizerMemoizeAttributeInvalidTarget` warning.

### Deliverables

- `Tyhp/TyhpLang/Optimizer/Attributes/PureAttribute.cs` — `\Tyhp\Optimize\Pure` attribute recognition (with `force` parameter)
- `Tyhp/TyhpLang/Optimizer/Attributes/MemoizeAttribute.cs` — `\Tyhp\Optimize\Memoize` attribute recognition
- `Tyhp/TyhpLang/Optimizer/Modules/PureFunctionMemoizationModule.cs` — Pure function optimizations
- `Tyhp/TyhpLang/Optimizer/Modules/MemoizeCallDeduplicationModule.cs` — Memoize scope-aware deduplication

### Module Properties

**Pure Function Memoization Module:**

| Property | Value |
|----------|-------|
| `ConfigKey` | `pureFunctionMemoization` |
| `Priority` | `1200` |
| `MinimumLevel` | `Aggressive` |

**Memoize Call Deduplication Module:**

| Property | Value |
|----------|-------|
| `ConfigKey` | `memoizeCallDeduplication` |
| `Priority` | `1250` |
| `MinimumLevel` | `Basic` |

### Acceptance Criteria

- [ ] `#[\Tyhp\Optimize\Pure]` attribute is recognized and validated by the checker
- [ ] `#[\Tyhp\Optimize\Pure()]` (default) validates purity; fails with `OptimizerPureAttributeInvalidTarget` if impure
- [ ] `#[\Tyhp\Optimize\Pure(force: true)]` bypasses validation, emits `OptimizerPureForcedUnsafe` warning
- [ ] Pure function duplicate call elimination works for identical arguments
- [ ] Pure function loop hoisting works for loop-invariant arguments
- [ ] `#[\Tyhp\Optimize\Memoize]` attribute is recognized by the checker
- [ ] Memoize performs scope-aware duplicate call elimination
- [ ] Memoize cache is invalidated when intervening side effects are detected
- [ ] Memoize does NOT enable loop hoisting or call reordering
- [ ] Memoize on void functions emits `OptimizerMemoizeAttributeInvalidTarget` warning
- [ ] Both attributes are NOT emitted to PHP output
- [ ] Both attributes resolve via standard PHP name resolution (including `use` imports)
- [ ] `pureFunctionMemoization` module has `Priority = 1200`, `MinimumLevel = Aggressive`
- [ ] `memoizeCallDeduplication` module has `Priority = 1250`, `MinimumLevel = Basic`

### Pure Function Catalog for PHP Standard Library

For `#[\Tyhp\Optimize\Pure]` validation to be useful, the compiler must know which PHP standard library functions are pure. Without this knowledge, almost any user function that calls built-in PHP functions would fail purity validation (requiring `force: true`).

**Approach:** Add `#[\Tyhp\Optimize\Pure]` attributes to pure PHP built-in functions and methods in the PHP extension tyhpdef files (Story 21's `tyhp/php-{phpVersion}` packages). This includes functions like `\strlen()`, `\str_contains()`, `\array_map()`, `\count()`, `\max()`, `\min()`, `\abs()`, `\ceil()`, `\floor()`, `\round()`, `\intval()`, `\floatval()`, `\strval()`, `\boolval()`, `\array_key_exists()`, `\in_array()`, `\array_merge()`, `\array_slice()`, `\implode()`, `\explode()`, `\trim()`, `\strtolower()`, `\strtoupper()`, `\substr()`, `\str_replace()`, `\preg_match()`, `\sprintf()`, `\json_encode()`, `\json_decode()`, and many more.

The tyhpdef overlay files (`.tyhp`) and base tyhpdef files (`.tyhpdef`) in Story 21 must be updated to include `#[\Tyhp\Optimize\Pure]` annotations on all functions/methods that are genuinely side-effect-free. See the Story 21 cross-story reference for this requirement.

**Note:** This catalog does not need to be complete for the optimizer MVP. Unmarked functions are conservatively treated as impure. The catalog can be incrementally expanded over time.

---

## Phase 7: Cross-Reference Constant Folding Module




### Phase Overview

Extend the basic constant folding from Story 23 to fold constant references. When a `const` is defined with a literal value, references to that constant can be replaced with the literal — subject to visibility rules.

### Implementation Details

**Target pattern:**

```tyhp
class Config {
    private const int MAX_RETRIES = 3;

    function retry(): void {
        for ($i = 0; $i < self::MAX_RETRIES; $i++) { ... }
    }
}
```

The reference `self::MAX_RETRIES` can be folded to `3` since the constant is `private` and its value is a literal.

**Rules:**
- Only fold `private` and `internal` constants (public constants must remain as references for external compatibility).
- Only fold literal values (not expressions that require computation).
- In application projects, `public` constants on `final` classes can also be folded.

### Acceptance Criteria

- [ ] Module has `ConfigKey = "crossReferenceConstantFolding"`, `Priority = 450`, `MinimumLevel = Aggressive`
- [ ] Private constant references are folded to their literal values
- [ ] Public constant references are NOT folded (in library projects)
- [ ] Application projects can fold public constants on final classes
- [ ] Only literal values are folded (not computed expressions)

---

## Phase 8: Escape Analysis Module




### Phase Overview

Analyze whether objects "escape" their creation scope. Objects that do not escape can be optimized: allocated more efficiently, have unnecessary clones removed, or have their lifetime shortened.

### Implementation Details

An object "escapes" when:
- It is returned from the function
- It is assigned to a property or global variable
- It is passed to a function that stores it

An object that does NOT escape can be:
- Created with minimal overhead (no need for GC tracking)
- Mutated in place even if the language semantics suggest a copy
- Disposed eagerly at scope exit without waiting for GC

**Scope for this story:** Focus on identifying non-escaping objects and marking them in the AST for the emitter. Actual optimizations based on escape analysis (memory layout, stack allocation) are PHP-runtime-dependent and may have limited benefit in PHP's memory model. The primary benefit is enabling other optimizations (struct copy elision, pure function analysis) to be more precise.

### Acceptance Criteria

- [ ] Module has `ConfigKey = "escapeAnalysis"`, `Priority = 1300`, `MinimumLevel = Aggressive`
- [ ] Objects created within a function scope are analyzed for escape
- [ ] Non-escaping objects are annotated in the AST
- [ ] Escape analysis results are available to other optimization modules
- [ ] Complex escape paths (closures, callbacks, collections) are conservatively marked as escaping

---

## Phase 9: Tyhp Reflection API (Foundation)




### Phase Overview

This phase lays the foundation for the Tyhp reflection API — a set of runtime classes that provide accurate reflection information about Tyhp code regardless of optimization level. Unlike PHP's native reflection (which inspects the compiled PHP output and may produce incorrect results with optimized code), Tyhp reflection uses sourcemaps to provide a stable, correct view of the original Tyhp source structure.

**This is a foundational phase.** The full Tyhp reflection API is implemented in Story 29. This phase defines the architecture direction and documents the requirements that Story 29 must satisfy. The actual implementation of the runtime reflection classes is deferred to Story 29.

### Implementation Details

**Runtime classes (written in Tyhp, compiled to PHP, distributed as part of `tyhp/core`):**

- `\Tyhp\Reflection\ReflectionClass` — mirrors `\ReflectionClass` but uses sourcemap data
- `\Tyhp\Reflection\ReflectionMethod` — mirrors `\ReflectionMethod`
- `\Tyhp\Reflection\ReflectionProperty` — mirrors `\ReflectionProperty`
- `\Tyhp\Reflection\ReflectionParameter` — mirrors `\ReflectionParameter`

**Key differences from PHP reflection:**

| Feature | PHP Reflection | Tyhp Reflection |
|---------|---------------|-----------------|
| Extension methods | Not visible | Visible as methods on the target class |
| Operator overloads | Visible as `__OP_*` methods | Visible as operators with signatures |
| Generic parameters | Erased | Available with constraints |
| Inlined methods | Not visible (eliminated) | Visible (from sourcemap) |
| Source location | Points to `.php` file | Points to `.tyhp` file |

**Sourcemap-backed resolution:**

The Tyhp reflection classes load the `.php.map` sourcemap file at runtime and use it to:
1. Reconstruct the original class structure (including inlined/eliminated members).
2. Map PHP method names back to Tyhp names.
3. Provide accurate source file paths and line numbers.

**Data format:**

The sourcemap already contains the `names` array and position mappings. An additional metadata section (or a companion `.tyhp.meta.json` file emitted alongside the sourcemap) may be needed to store:
- Original Tyhp class structure (member list, generic parameters, extension members)
- Operator overload signatures
- Visibility and modifier information

This metadata format is defined in this phase but the emitter integration (actually writing the files) is coordinated with Story 17 (Sourcemaps) and Story 20 (Tyhpdef Generator).

### Acceptance Criteria

- [ ] Architecture document for the Tyhp reflection API exists, defining the class hierarchy (`\Tyhp\Reflection\ReflectionClass`, `\Tyhp\Reflection\ReflectionMethod`, `\Tyhp\Reflection\ReflectionProperty`, `\Tyhp\Reflection\ReflectionParameter`)
- [ ] The metadata format for Tyhp-specific reflection data (`.tyhp.meta.json` or embedded in sourcemap) is defined and documented
- [ ] The document specifies how sourcemaps are loaded and parsed at runtime for reflection purposes
- [ ] The document specifies how extension methods, operator overloads, and generic parameters are represented in the metadata
- [ ] The document specifies how inlined/eliminated members are reconstructed from metadata
- [ ] Requirements for Story 29 (full implementation) are clearly defined
- [ ] No runtime code is implemented in this phase — the actual `\Tyhp\Reflection\*` classes are built in Story 29

### Dependencies

- **Requires:** Story 17 (Sourcemaps), Story 04 (Tyhp runtime packages)
- **Provides:** Guaranteed reflection for optimized Tyhp code

---

## Cross-Story References

### Prerequisites

| Story | Relationship |
|-------|-------------|
| Story 23 | Direct prerequisite — provides the optimizer framework |
| Story 08 (Checker) | Provides type narrowing data used by type guard elimination and devirtualization |
| Story 17 (Sourcemaps) | Required for Tyhp reflection API |
| Story 04 (Runtime Packages) | `tyhp/core` hosts the Tyhp reflection runtime classes |

### Stories This Affects

| Story | Impact |
|-------|--------|
| Story 11 (Emitter Feature Expansion) | Struct transformer should coordinate with struct copy elision |
| Story 17 (Sourcemaps) | Metadata format for Tyhp reflection must be emitted alongside sourcemaps |
| Story 20 (Tyhpdef Generator) | Reflection metadata may overlap with `package.tyhp.json` manifest content |
| Story 18 (XDebug Proxy) | Tyhp reflection can provide enhanced debugging information |
| Story 21 (PHP Extension Packages) | `#[\Tyhp\Optimize\Pure]` attributes must be added to pure PHP built-in functions in the tyhpdef files |

---

## Human Testing and Verification

> **Note:** These steps are meant to help a human developer manually verify the advanced optimization modules from Story 24. Steps can be skipped, reordered, or adapted based on which modules have been implemented. All optimizer modules are individually toggleable, so each can be tested in isolation.

### Step 1: Verify the Advanced Optimizer Modules Compile

Run the project build to confirm all new module code compiles without errors:

```bash
dotnet build
```

Confirm there are no build errors in the `Tyhp/TyhpLang/Optimizer/Modules/` directory for the new files (`OperatorChainOptimizationModule.cs`, `NullSafeChainCollapsingModule.cs`, `TypeGuardEliminationModule.cs`, `DevirtualizationModule.cs`, `StructCopyElisionModule.cs`, `PureFunctionMemoizationModule.cs`, `MemoizeCallDeduplicationModule.cs`, `CrossRefConstantFoldingModule.cs`, `EscapeAnalysisModule.cs`).

### Step 2: Verify Module Registration

Confirm that all new modules appear in the optimizer's module registry:

```bash
tyhp build --optimize=aggressive --verbose
```

**Expected:** The verbose output lists all modules from both Story 23 and 24 in priority order. New modules from Story 24 should appear: `operatorChainOptimization`, `nullSafeChainCollapsing`, `typeGuardElimination`, `devirtualization`, `structCopyElision`, `pureFunctionMemoization`, `memoizeCallDeduplication`, `crossReferenceConstantFolding`, `escapeAnalysis`.

### Step 3: Verify Operator Chain Optimization

Create `test_opt_chain.tyhp`:

```tyhp
<?tyhp

use Tyhp\Decimal;

function testOperatorChain(): void {
    Decimal $a = \Tyhp\decimal("10");
    Decimal $b = \Tyhp\decimal("20");
    Decimal $c = \Tyhp\decimal("30");
    Decimal $d = \Tyhp\decimal("40");

    Decimal $result = $a + $b + $c + $d;  // Chain of 3+ operators
}
```

Run:

```bash
tyhp build --optimize=aggressive --verbose
```

**Expected:** If the Decimal type has a variadic batch method (e.g., `addAll()`), the chain is rewritten to a single batch call instead of three separate `add()` calls. Verbose output reports `OptimizerOptimizedOperatorChain`. If no batch method exists, the chain is left as chained calls (the emitter already avoids intermediates).

### Step 4: Verify Null-Safe Chain Collapsing

Create `test_opt_nullsafe.tyhp`:

```tyhp
<?tyhp

class Profile {
    public function getSettings(): Settings {
        return new Settings();
    }
}

class Settings {
    public function getTheme(): string {
        return "dark";
    }
}

function testNullSafe(?Profile $profile): void {
    // getSettings() returns non-nullable Settings, so second ?-> is redundant
    string $theme = $profile?->getSettings()?->getTheme();
}
```

Run:

```bash
tyhp build --optimize=basic --verbose
```

Inspect the output. **Expected:**

- First `?->` on `$profile` is preserved (receiver is nullable)
- Second `?->` on `getSettings()` result is collapsed to `->` (return type is non-nullable `Settings`)
- Verbose output reports `OptimizerCollapsedNullSafeChain`
- Output passes `php -l`

### Step 5: Verify Type Guard Elimination

Create `test_opt_typeguard.tyhp`:

```tyhp
<?tyhp

function testRedundantGuard(int|string $value): void {
    if ($value instanceof string) {
        // $value is narrowed to string
        if ($value instanceof string) {    // Redundant!
            echo \strtoupper($value);
        }
    }
}

function testNarrowedUnion(int|string $value): void {
    if ($value instanceof string) {
        echo "string";
        return;
    }
    // $value is narrowed to int at this point
    if ($value instanceof int) {           // Redundant!
        echo "int";
    }
}
```

Run:

```bash
tyhp build --optimize=basic --verbose
```

**Expected:**

- The redundant `instanceof string` inside the already-narrowed scope is replaced with `true`
- The redundant `instanceof int` after the early return is replaced with `true`
- If dead code elimination also runs, the `if (true)` wrappers may be unwrapped
- Verbose output reports `OptimizerEliminatedTypeGuard`

### Step 6: Verify Cross-Reference Constant Folding

Create `test_opt_constref.tyhp`:

```tyhp
<?tyhp

class Config {
    private const int MAX_RETRIES = 3;
    private const string PREFIX = "app_";

    public function getMaxRetries(): int {
        return self::MAX_RETRIES;  // Should fold to 3
    }

    public function buildKey(string $name): string {
        return self::PREFIX . $name;  // PREFIX should fold to "app_"
    }
}
```

Run:

```bash
tyhp build --optimize=aggressive --verbose
```

Inspect the output. **Expected:**

- `self::MAX_RETRIES` is replaced with `3` (private constant, safe to fold)
- `self::PREFIX` is replaced with `'app_'`
- Verbose output reports `OptimizerFoldedConstantReference`
- Public constants in library projects should NOT be folded

### Step 7: Verify `#[\Tyhp\Optimize\Pure]` Attribute

Create `test_opt_pure.tyhp`:

```tyhp
<?tyhp

use \Tyhp\Optimize\Pure;

#[Pure()]
function add(int $a, int $b): int {
    return $a + $b;
}

function testPureDedup(): void {
    int $x = add(1, 2);  // First call
    int $y = add(1, 2);  // Duplicate — should reuse $x

    for (int $i = 0; $i < 100; $i++) {
        int $val = add(5, 10);  // Loop-invariant — should hoist
        echo $val + $i;
    }
}
```

Run:

```bash
tyhp build --optimize=aggressive --verbose
```

Inspect the output. **Expected:**

- The second `add(1, 2)` call is replaced with `$x` (duplicate elimination)
- The `add(5, 10)` call inside the loop is hoisted before the loop
- The `#[Pure]` attribute does NOT appear in the PHP output
- Verbose output reports `OptimizerMemoizedPureFunction`

### Step 8: Verify `#[\Tyhp\Optimize\Pure(force: true)]`

Create `test_opt_pure_force.tyhp`:

```tyhp
<?tyhp

use \Tyhp\Optimize\Pure;

#[Pure(force: true)]
function expensiveLookup(string $key): string {
    return ExternalCache::get($key);  // Compiler can't prove purity
}

function testForced(): void {
    string $a = expensiveLookup("config");
    string $b = expensiveLookup("config");  // Should reuse $a
}
```

Run:

```bash
tyhp build --optimize=aggressive --verbose
```

**Expected:**

- The duplicate call is eliminated despite calling opaque code
- Verbose output includes `OptimizerPureForcedUnsafe` warning
- Verbose output reports `OptimizerMemoizedPureFunction`

### Step 9: Verify `#[\Tyhp\Optimize\Memoize]` Attribute

Create `test_opt_memoize.tyhp`:

```tyhp
<?tyhp

use \Tyhp\Optimize\Memoize;

class UserService {
    #[Memoize()]
    public function getUser(int $id): User {
        return $this->db->query("SELECT * FROM users WHERE id = ?", [$id]);
    }

    public function demo(): void {
        User $user1 = $this->getUser(42);    // Executes
        User $user2 = $this->getUser(42);    // Should be deduplicated

        $this->updateUser(42, "new name");   // Side effect — invalidates cache

        User $user3 = $this->getUser(42);    // Should re-execute (cache invalidated)
    }
}
```

Run:

```bash
tyhp build --optimize=basic --verbose
```

Inspect the output. **Expected:**

- First `getUser(42)` executes normally
- Second `getUser(42)` is replaced with the result of the first (deduplicated via temp variable)
- After `updateUser()` (side effect), the third `getUser(42)` re-executes
- Verbose output reports `OptimizerMemoizedExpensiveCall` and `OptimizerMemoizeCacheInvalidated`
- `#[Memoize]` does NOT appear in the PHP output

### Step 10: Verify Invalid Attribute Usage Produces Warnings

Create `test_opt_invalid_attrs.tyhp`:

```tyhp
<?tyhp

use \Tyhp\Optimize\{Pure, Memoize};

#[Pure()]
function impureFunction(): void {
    echo "I have side effects!";  // Should warn: detectable side effects
}

#[Memoize()]
function voidMemoize(): void {
    echo "Can't memoize void";  // Should warn: void can't be memoized
}
```

Run:

```bash
tyhp build --optimize=aggressive --verbose
```

**Expected:**

- `impureFunction`: `OptimizerPureAttributeInvalidTarget` warning (echo is a side effect)
- `voidMemoize`: `OptimizerMemoizeAttributeInvalidTarget` warning (void return)

### Step 11: Verify Devirtualization

Create `test_opt_devirt.tyhp`:

```tyhp
<?tyhp

final class Calculator {
    public function add(int $a, int $b): int {
        return $a + $b;
    }
}

function testDevirt(): void {
    Calculator $calc = new Calculator();
    int $result = $calc->add(1, 2);  // Final class — can be devirtualized

    int $direct = (new Calculator())->add(3, 4);  // Constructor result — can be devirtualized
}
```

Run:

```bash
tyhp build --optimize=aggressive --verbose
```

**Expected:**

- Verbose output reports `OptimizerDevirtualizedCall` for calls on the `final` class
- The output passes `php -l`

### Step 12: Verify Struct Copy Elision

Create `test_opt_struct_elision.tyhp`:

```tyhp
<?tyhp

struct Point {
    int $x;
    int $y;
}

function testElision(): void {
    Point $p = new Point() with { $x = 1, $y = 2 };
    Point $q = $p with { $x = 3 };
    // $p is NOT used after this point — copy can be elided
    echo $q->x;
}
```

Run:

```bash
tyhp build --optimize=aggressive --verbose
```

**Expected:** Verbose output reports `OptimizerElidedStructCopy` if the copy of `$p` was elided because `$p` is dead after the `with` expression.

### Step 13: Verify Module Isolation via Individual Overrides

Test that each advanced module can be individually enabled/disabled:

```bash
tyhp build --optimize=none --optimize-enable=nullSafeChainCollapsing --verbose
tyhp build --optimize=aggressive --optimize-disable=devirtualization --verbose
tyhp build --optimize=none --optimize-enable=crossReferenceConstantFolding --verbose
```

**Expected:** Only the specified modules run (or are excluded). Verbose output confirms which modules were active.

### Step 14: Verify No Behavioral Changes from Advanced Optimization

For a test file that exercises multiple advanced optimization targets:

1. Build with `--optimize=none` → run the output with `php` → capture output
2. Build with `--optimize=aggressive` → run the output with `php` → capture output
3. Compare the two outputs

**Expected:** The runtime output is identical for all test cases. Advanced optimizations are semantics-preserving (with the documented exception of PHP reflection output, which may differ).

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
