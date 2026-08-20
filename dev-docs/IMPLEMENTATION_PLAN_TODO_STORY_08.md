# Implementation Plan: Story 08 — Checker (Type Checking & Validation)

> **Roadmap position:** Story 08 — **Tier 0 — Spine** · **FLAGSHIP: the type checker**
> **Direct dependencies (new numbering):** 01, 02, 03, 05, 06
> **Renumbered from:** legacy Story 3
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** `TODO.md` Story 08
> **Branch:** TBD
> **Generated:** 2026-02-16
> **Prerequisites:** Story 01 (Diagnostic System, `DiagnosticBag`, `CompilationResult`, `CompilationService`), Story 02 (Binder — symbols, scopes, name resolution, tyhpdef loading), Story 03 (Extension operator overloads, tyhpdef inline extensions), Story 05 (BoundSymbol on AST nodes — provides `astNode.BoundSymbol` for direct symbol access), Story 06 (Built-in Types, Grammar Fixes, and Compiler Infrastructure)
> **Status:** SUBSTANTIALLY COMPLETE (2026-07-31 audit) — `TyhpChecker`, `TypeComparer`, `TypeInferrer`, and rules are implemented and wired. Phase checkboxes in this doc were never updated. Known incomplete acceptance items (esp. call-argument validation) are listed in `INCOMPLETE.md`.
> **Post-completion fixes verified 2026-07-31 (RESOLVED_BUGS):** item **35** (all-literal param/return types → `StaticValueTypeHelper`) and item **40** (`return <expr>;` in `__construct`/`__destruct` → **4153**) — DONE; remaining Story 08 gaps stay in `INCOMPLETE.md` / `FOUND_BUGS.md`.

---

## Architecture Overview

### What the Checker Does

The checker is the semantic validation phase of the Tyhp compiler. After the binder has built the scope tree and resolved all name references, the checker walks the bound AST and validates that the code is semantically correct according to Tyhp's type system. It produces errors, warnings, and informational diagnostics — it does NOT modify the AST or produce output code.

The checker answers questions like:
- Does this assignment's right-hand side match the declared type of the left-hand side?
- Does this function call pass arguments that match the parameter types?
- Does this class properly implement all interface methods?
- Is this variable definitely assigned before it is used?
- Is this operator overload usage valid for the types involved?
- Does this type guard function actually narrow the type correctly?

### Position in the Pipeline

```
Parser/AST (DONE)
    │
    ▼
Story 01: Foundation (DiagnosticBag, CompilationService, BuildAction)
    │
    ▼
Story 02: Binder (Symbols, Scopes, Name Resolution)
    │
    ▼
Story 06: Built-in Types & Compiler Infrastructure
    │
    ▼
┌─────────────────────────────────────────────────────────┐
│  STORY 08: Checker (Type Checking & Validation)          │
│  ◄── THIS PLAN                                          │
│                                                         │
│  Input:  Bound AST trees + GlobalScope + SymbolTree     │
│  Output: Diagnostics (errors, warnings, info) added     │
│          to CompilationResult.Diagnostics                │
│  Modifies: Nothing — the checker is read-only            │
└─────────────────────────────────────────────────────────┘
    │
    ▼
Story 09: Emitter (PHP code generation — uses checker-validated AST)
```

### Relationship to Existing Code

The existing checker files are minimal scaffolding:

| File | State | Action |
|------|-------|--------|
| `TyhpChecker.cs` | Entirely commented out; uses old `ErrorList`/`WarningList`/`InfoList` pattern | Delete and rebuild from scratch using `DiagnosticBag` |
| `CheckerState.cs` | Properties defined, `SnapShot()`/`Merge()` throw `NotImplementedException`, `Split()` has commented-out logic | Redesign and implement — the commented-out logic provides useful architectural hints |
| `VariableState.cs` | Empty class | Implement from scratch |
| `readme.md` | Short checklist of things to check | Keep as reference; expand with implementation notes |

Per the TODO.md architectural note, there is **no usable implementation to preserve** in the checker directory: as the table above shows, `TyhpChecker.cs` is entirely commented out, `CheckerState.cs` has only property stubs with `NotImplementedException` bodies, and `VariableState.cs` is an empty class. These files are commented-out/empty scaffolding that this plan **rebuilds from scratch** (not extends). The old design used separate `ErrorList`/`WarningList`/`InfoList` lists — Story 01's `DiagnosticBag` replaces that pattern entirely. ("Nothing to preserve" refers to the logic, not the files themselves, which remain present as stubs until rebuilt.)

### Design Principles

1. **DiagnosticBag is the only error reporting mechanism.** Every check that fails adds a diagnostic via `DiagnosticBag.AddError()`, `AddWarning()`, or `AddInfo()`. No exceptions are thrown for type errors.

2. **The checker is read-only with respect to the AST and scope tree.** It walks these structures but does not modify them. If the checker needs to store computed type information (e.g., inferred types for expressions), it uses a side dictionary (`Dictionary<IBase2Ast, ICheckedType>`), not AST mutation.

3. **ErrorAst nodes are skipped gracefully.** When the checker encounters an `ErrorAst` node from the visitor, it skips checking that subtree and does not produce cascading errors.

4. **Incremental implementation is essential.** The checker grows with every language feature. This plan divides checks into three tiers (Core PHP, Tyhp-specific, Advanced Tyhp) so that basic type checking works before advanced features are implemented.

5. **The checker uses the binder's symbol resolution.** It does not resolve names itself — it relies on the `SymbolTree` and resolved symbols from Story 02. If a name was unresolved by the binder, the checker skips checking that reference (the binder already reported the error).

    > **Note (Story 05):** The checker accesses symbols via the `BoundSymbol` property on AST nodes (added in Story 05). For any AST node, `astNode.BoundSymbol` returns the associated `IBaseSymbol` (or `null` for nodes without symbols like literals). The checker does NOT need a separate `NameResolver` reference — all symbol lookups use `BoundSymbol` directly.

6. **Type compatibility is structural for structs, nominal for classes.** Two structs with the same property shapes are compatible. Two classes with the same method signatures are NOT compatible unless related by inheritance/implementation.

7. **Non-nullable by default.** All types in Tyhp are non-nullable by default. A variable declared as `string $x` cannot hold `null` — it must be explicitly declared as `?string $x` (or `string|null $x`) to allow null. This is a fundamental difference from PHP, where any typed variable can silently receive `null` at runtime. The checker must enforce this strictly:
   - Assigning `null` to a non-nullable variable is a type error (`CheckerTypeMismatch`).
   - Passing `null` to a non-nullable parameter is a type error (`CheckerIncompatibleArgumentType`).
   - Returning `null` from a function with a non-nullable return type is a type error (`CheckerIncompatibleReturnType`).
   - A variable that may be `null` on some code paths (e.g., assigned in only one branch of an `if/else`) must be treated as possibly-null (`IsPossiblyNull = true`) and using it where a non-nullable type is expected produces `CheckerVariablePossiblyNull` (4015).
   - The `?` nullable prefix is the ONLY way to opt in to nullability. There is no configuration flag to weaken this — non-nullable by default is always enforced in Tyhp mode.

8. **Automatic smart casts after type narrowing.** When the checker narrows a variable's type through control flow analysis (via `instanceof`/`is` checks, null checks, type guard functions, or built-in type-checking functions like `is_string()`), the narrowed type is automatically used for all subsequent type checks within the narrowing scope. Developers do NOT need to cast or re-declare the variable — the checker automatically tracks the narrower type. This applies to:
   - `if ($x instanceof Foo)` — `$x` is automatically `Foo` inside the if-body, with no cast required.
   - `if ($x !== null)` — `$x` is automatically the non-null variant of its declared type inside the if-body.
   - `if (isString($x))` where `isString` is a type guard function — `$x` is automatically `string` inside the if-body.
   - `if (is_array($x))` — `$x` is automatically `array` inside the if-body (built-in type guard recognition).
   - Negative narrowing: in the else-branch of any of the above, the type is automatically narrowed to exclude the checked type.
   - Narrowing compounds: multiple checks in sequence further narrow the type (e.g., after checking `!== null` and then `instanceof Foo`, the type is `Foo`).
   - Narrowing resets: when a variable is reassigned, the narrowed type resets to the assigned type.

9. **Type inference on first assignment without explicit keywords.** When a variable is declared without a type annotation but has an initializer (first assignment), the type is automatically inferred from the right-hand side expression at its most narrow type. No `var` or `auto` keyword is needed — the absence of a type annotation combined with an assignment is sufficient to infer the type. The inferred type is then used for all subsequent type checks on that variable. Variables without both a type annotation and an initializer produce `CheckerVariableTypeRequired` (4016).

10. **Modular rule-based architecture.** Each category of validation lives in its own dedicated C# class (e.g., `NullSafetyRule`, `StructRule`, `ReferenceTrackingRule`), not in large partial classes. The `TyhpChecker` orchestrator walks the AST and dispatches to registered rules. This makes the checker easy to navigate ("where is the callable check?" → `CallableRule.cs`), easy to test (each rule can be unit-tested in isolation), and easy to extend (add a new rule class, register it). Rules must not depend on each other's internal state — they share state only through `CheckerState` and `TypeComparer`.

11. **Thoroughness over leniency.** The checker's job is to catch as many errors as possible *before* the code reaches PHP at runtime. Every PHP runtime `TypeError`, `ArgumentCountError`, or behavioral surprise that the checker could have caught statically is a checker bug. When in doubt, err on the side of reporting a diagnostic — the user can suppress false positives, but they cannot recover from a runtime crash in production.

### Checker Architecture Diagram

```
TyhpChecker
├── Check(astTrees, globalScope) — main entry point
│   ├── For each SrcFileAst:
│   │   ├── Create initial CheckerState for file scope
│   │   ├── Walk AST nodes recursively
│   │   │   └── For each node, dispatch to CheckerRuleRegistry
│   │   │       └── Registry invokes applicable ICheckerRule.Check() methods
│   │   └── Merge branching states (if/else, try/catch)
│   └── Aggregate results in CompilationResult.Diagnostics
│
├── CheckerRuleRegistry — rule orchestration
│   ├── RegisteredRules — all ICheckerRule instances
│   ├── Dispatch(node, state, diagnostics) — invoke rules for node type
│   └── Each rule declares which AST node types it handles
│       Each ICheckerRule implementation exposes a property
│       IEnumerable<Type> HandledNodeTypes { get; } that returns the AST
│       node types (e.g., typeof(PhpFunctionDeclAst)) it validates. The
│       CheckerRuleRegistry builds a Dictionary<Type, List<ICheckerRule>>
│       at startup by iterating all registered rules and indexing them by
│       their handled types. During AST traversal, for each visited node,
│       the registry looks up all rules registered for that node's type,
│       and — for each such rule — calls its bool Handles(IBase2Ast node)
│       predicate for optional fine-grained filtering before invoking
│       Check(). HandledNodeTypes is the coarse, type-based index used for
│       dispatch; Handles() is an optional, per-node refinement (default
│       implementation returns true). Rules are invoked in registration
│       order.
│
├── Rules/ — one class per check category
│   ├── TypeAnnotationRule        — required type annotations
│   ├── TypeCompatibilityRule     — assignment/argument/return checks
│   ├── TypeDeclarationValidationRule — PHP redundant/forbidden type combos
│   ├── NullSafetyRule            — non-nullable enforcement
│   ├── TypeNarrowingRule         — control flow narrowing
│   ├── ReferenceTrackingRule     — pass-by-reference variable tracking
│   ├── RelativeTypeRule          — self/parent/static validation
│   ├── GenericRule               — generic constraint checks
│   ├── CallableRule              — callable restrictions
│   ├── StructRule                — struct compatibility
│   ├── OperatorOverloadRule      — operator overload validation
│   ├── ExtensionRule             — extension method/operator checks
│   ├── AsyncRule                 — async/await context
│   ├── DisposableRule            — disposable := validation
│   ├── CompileTimeRule           — nameof/typeof/default/variable_exists
│   ├── ClosureRule               — closure/arrow use, static closure
│   ├── DeprecationRule           — deprecated symbol usage
│   ├── RestrictedFeatureRule     — eval/include/variable vars/dynamic props
│   ├── ControlFlowRule           — unreachable code, goto, yield, definite assignment
│   ├── DeclarationRule           — class/interface/trait/enum/magic methods
│   ├── OverloadRule              — function/method overload signatures
│   ├── AttributeRule             — attribute validation
│   ├── ImportRule                — unused/duplicate imports
│   └── CodeQualityRule           — unused vars/params, dead stores, warnings
│
├── TypeComparer (utility class)
│   ├── IsAssignableTo(source, target) — can source be assigned to target?
│   ├── IsSubtypeOf(child, parent) — inheritance/implementation check
│   ├── AreTypesEqual(a, b) — structural equality
│   ├── UnionTypes(a, b) — compute union
│   ├── IntersectTypes(a, b) — compute intersection
│   ├── NarrowType(current, narrowTo) — apply type narrowing
│   └── ResolveGenericType(generic, typeArgs) — substitute type params
│
├── CheckerState — tracks scope-local state during checking
│   ├── Parent — link to enclosing state
│   ├── Variables — variable definite-assignment and type narrowing state
│   ├── SnapShot() — immutable copy for branching
│   ├── Split(scopeType) — create child state
│   └── Merge(branchState) — merge after control flow
│
└── VariableState — per-variable tracking
    ├── DeclaredType — the original declared/inferred type
    ├── NarrowedType — current type (may be narrower than declared)
    ├── IsDefinitelyAssigned — assigned on all paths?
    ├── IsPossiblyNull — could be null here?
    ├── IsPossiblyUndefined — could be unset?
    ├── IsDisposable — assigned with :=?
    ├── IsReference — is this variable a reference (&$var)?
    └── ReferenceGroup — set of variables sharing the same reference (null if non-reference)
```

### File Organization — Modular Rule-Based Architecture

Checker rules are implemented as **individual classes**, each responsible for one category of validation. This modular approach makes the checker easier to navigate, test, and extend. Each rule class implements a common interface and is registered with the checker's rule registry.

**Core infrastructure:**

| Directory / File | Purpose |
|------------------|---------|
| `Tyhp/TyhpLang/Checker/TyhpChecker.cs` | Main entry point — walks ASTs and dispatches to registered rules |
| `Tyhp/TyhpLang/Checker/CheckerState.cs` | Scope-local state tracking (redesigned) |
| `Tyhp/TyhpLang/Checker/VariableState.cs` | Per-variable state tracking (type, nullability, assignment, reference) |
| `Tyhp/TyhpLang/Checker/TypeComparer.cs` | Type compatibility checking utility (static, pure) |
| `Tyhp/TyhpLang/Checker/TypeInferrer.cs` | Type inference for expressions and variables |
| `Tyhp/TyhpLang/Checker/UtilityTypeResolver.cs` | Resolves `\Tyhp\Readonly<T>`, `\Tyhp\NonNullable<T>`, etc. |
| `Tyhp/TyhpLang/Checker/ICheckedType.cs` | Interface for checker's internal type representation |
| `Tyhp/TyhpLang/Checker/CheckedType.cs` | Concrete type representations (simple, union, intersection, generic, nullable, literal) |

**Rule classes** (each is a self-contained class responsible for one category of checks):

| Directory / File | Purpose |
|------------------|---------|
| `Tyhp/TyhpLang/Checker/Rules/ICheckerRule.cs` | Interface: `IEnumerable<Type> HandledNodeTypes { get; }` (registry dispatch index), `bool Handles(IBase2Ast node)` (optional fine-grained filter; default returns `true`), and `void Check(IBase2Ast node, CheckerState state, DiagnosticBag diagnostics)` |
| `Tyhp/TyhpLang/Checker/Rules/CheckerRuleRegistry.cs` | Collects all rules, dispatches by AST node type |
| `Tyhp/TyhpLang/Checker/Rules/TypeAnnotationRule.cs` | Required type annotations on parameters, returns, properties, variables |
| `Tyhp/TyhpLang/Checker/Rules/TypeCompatibilityRule.cs` | Assignment, argument, return type compatibility checks |
| `Tyhp/TyhpLang/Checker/Rules/NullSafetyRule.cs` | Non-nullable-by-default enforcement, `IsPossiblyNull` tracking |
| `Tyhp/TyhpLang/Checker/Rules/TypeDeclarationValidationRule.cs` | PHP type declaration rules: redundant types, forbidden combinations, callable-as-property, resource restrictions |
| `Tyhp/TyhpLang/Checker/Rules/TypeNarrowingRule.cs` | Control flow narrowing, smart casts, type guards |
| `Tyhp/TyhpLang/Checker/Rules/ReferenceTrackingRule.cs` | Pass-by-reference parameter tracking, reference variable type propagation |
| `Tyhp/TyhpLang/Checker/Rules/RelativeTypeRule.cs` | `self`, `parent`, `static` type resolution and validation |
| `Tyhp/TyhpLang/Checker/Rules/GenericRule.cs` | Generic constraints, type argument count, restricted type validation |
| `Tyhp/TyhpLang/Checker/Rules/CallableRule.cs` | Callable restrictions, `__invoke` detection, generic callable convention |
| `Tyhp/TyhpLang/Checker/Rules/StructRule.cs` | Struct property validation, structural compatibility, struct-to-array widening |
| `Tyhp/TyhpLang/Checker/Rules/OperatorOverloadRule.cs` | Operator overload declaration and usage validation |
| `Tyhp/TyhpLang/Checker/Rules/ExtensionRule.cs` | Extension method/operator validation, `use extension` conflicts |
| `Tyhp/TyhpLang/Checker/Rules/AsyncRule.cs` | Async/await context checks, Promise unwrapping, async foreach |
| `Tyhp/TyhpLang/Checker/Rules/DisposableRule.cs` | Disposable `:=` validation, circular reference detection |
| `Tyhp/TyhpLang/Checker/Rules/CompileTimeRule.cs` | `nameof()`, `typeof()`, `default()`, `variable_exists()` validation |
| `Tyhp/TyhpLang/Checker/Rules/DeprecationRule.cs` | Deprecated/obsolete symbol usage warnings |
| `Tyhp/TyhpLang/Checker/Rules/RestrictedFeatureRule.cs` | `eval()`, `include`/`require` restrictions |
| `Tyhp/TyhpLang/Checker/Rules/ControlFlowRule.cs` | Unreachable code, definite assignment, break/continue, goto, yield validation |
| `Tyhp/TyhpLang/Checker/Rules/DeclarationRule.cs` | Class, interface, trait, enum declaration validation (abstract, final, visibility, magic methods, param validation, promoted properties) |
| `Tyhp/TyhpLang/Checker/Rules/ClosureRule.cs` | Closure/arrow function `use` validation, static closure restrictions, capture tracking |
| `Tyhp/TyhpLang/Checker/Rules/OverloadRule.cs` | Function/method overload signature validation |
| `Tyhp/TyhpLang/Checker/Rules/AttributeRule.cs` | Attribute class, target, argument, repeatability validation |
| `Tyhp/TyhpLang/Checker/Rules/ImportRule.cs` | Unused imports, duplicate imports, conflicting aliases |
| `Tyhp/TyhpLang/Checker/Rules/CodeQualityRule.cs` | Unused variables/params/members, dead stores, redundant casts, condition quality |

**Design rationale:** Each rule class is self-contained, handles one category of validation, and can be individually tested. The `TyhpChecker` orchestrator walks the AST and for each node, invokes the applicable rules from the registry. Rules declare which AST node types they handle. This avoids monolithic partial classes and makes it easy to find, modify, or add specific checks.

### Key Types from Prior Stories Used by the Checker

From Story 01:
- `DiagnosticBag` — all checker diagnostics go here
- `CompilationResult` — checker timing stored here
- `MessageCode` — checker codes in the 4000 range

From Story 02:
- `GlobalScope`, `IBaseScope` — scope tree root and navigation
- `IBaseSymbol`, `BaseSymbol` — symbol lookups
- `ObjectDeclarationSymbol` — class/interface/trait/enum/struct metadata
- `FunctionDeclarationSymbol` — function/method parameter and return types
- `VariableSymbol` — declared variable types
- `SymbolTree` — name resolution methods (`ResolveSymbol`, `ResolveMember`, `ResolveType`)
- `GenericTypeParameterSymbol` — generic constraints
- `TypeAliasSymbol` — type alias expansion

From Story 06:
- Hardcoded built-in types (decimal, struct, generic array/iterable/callable)
- Hardcoded utility types (`\Tyhp\Readonly<T>`, `\Tyhp\NonNullable<T>`, `\Tyhp\Partial<T>`, etc.)
- Compile-time construct signatures (nameof, typeof, default, variable_exists)
- PHP extension tyhpdef symbols (standard library function signatures) from Composer packages
- Runtime package tyhpdef symbols (IsDisposable, Promise, Decimal, Type, etc.) from Composer packages

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup using the canonical naming `<filename>.bak.<YYYYMMDD_HHMMSS>`
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: TyhpChecker Core Architecture and CheckerState Operations




### Phase Overview

Delete the commented-out `TyhpChecker.cs`, redesign `CheckerState.cs` with working `SnapShot()`, `Split()`, and `Merge()` operations, and create the core `TyhpChecker` class with its main entry point and AST dispatch infrastructure. This phase establishes the skeleton that all subsequent type-checking phases build upon.

### Deliverables

- `Tyhp/TyhpLang/Checker/TyhpChecker.cs` — Main checker class (rebuilt from scratch) with entry point and file-level dispatch
- `Tyhp/TyhpLang/Checker/CheckerState.cs` — Redesigned with working `SnapShot()`, `Split()`, `Merge()` operations
- `Tyhp/TyhpLang/Checker/ICheckedType.cs` — Interface for checker's internal type representation
- `Tyhp/TyhpLang/Checker/CheckedType.cs` — Concrete type representations used throughout the checker
- Updated `Tyhp/Domain/Exceptions/MessageCode.cs` — Additional checker-specific error codes

### Implementation Details

#### 1.1 Delete and Rebuild `TyhpChecker.cs`

**File:** `Tyhp/TyhpLang/Checker/TyhpChecker.cs`

Back up the existing file, then replace entirely. The old commented-out code used separate `ErrorList`/`WarningList`/`InfoList` which is superseded by `DiagnosticBag`.

Create a new `TyhpChecker` class:

- **Namespace:** `Tyhp.TyhpLang.Checker`
- **Constructor:** Accepts `DiagnosticBag diagnostics`, `Tyhp.TyhpLang.Binder.SymbolTree symbolTree`, `Tyhp.TyhpLang.Binder.Scopes.GlobalScope globalScope`
- **Main entry point:** `void Check(IEnumerable<SrcFileAst> astTrees)`
- **Internal state:**
  - `DiagnosticBag _diagnostics` — shared diagnostic collector
  - `SymbolTree _symbolTree` — for resolving types and symbols during checking
  - `GlobalScope _globalScope` — the bound scope tree
  - `Dictionary<IBase2Ast, ICheckedType> _expressionTypes` — computed types for expressions (side dictionary, preserving AST immutability)
  - `string _currentFileName` — current source file being checked (for diagnostic reporting)

`TyhpChecker` is the orchestrator — it walks ASTs and dispatches to rules via `CheckerRuleRegistry`. It holds a `CheckerRuleRegistry _rules` field initialized in the constructor with all `ICheckerRule` implementations.

The `Check()` method iterates over all `SrcFileAst` trees:

```
foreach SrcFileAst in astTrees:
    _currentFileName = srcFile.FileName
    Create initial CheckerState for file-level scope
    foreach child in srcFile.Children: CheckNode(child, state)
        → CheckNode recurses and, for each node, calls _rules.Dispatch(node, state, _diagnostics)
```

#### 1.2 Implement AST Dispatch Infrastructure

Dispatch is performed **exclusively** through the `CheckerRuleRegistry` — there is no hand-written `switch` over AST node types. The single recursive walker, `CheckNode(IBase2Ast node, CheckerState state)`, (1) skips `ErrorAst` nodes, (2) hands the node to the registry, which looks up the rules registered for the node's runtime type (via `HandledNodeTypes`) and invokes each rule's `Check()` (after its optional `Handles()` filter), and (3) recurses into the node's children. New checks are added in later phases by registering new `ICheckerRule` classes — never by extending a switch.

Pattern:

```csharp
private void CheckNode(IBase2Ast node, CheckerState state)
{
    if (node is ErrorAst) return; // Skip error nodes gracefully

    // Rule-registry dispatch is the ONE dispatch mechanism (no legacy type switch).
    _rules.Dispatch(node, state, _diagnostics);

    // Recurse into children; child scopes are created via CheckerState.Split(...)
    // by the rules/walker as appropriate (see §1.5).
    foreach (var child in node.Children)
    {
        CheckNode(child, state);
    }
}
```

The set of checks grows incrementally as each phase adds and registers more `ICheckerRule` implementations; the dispatcher itself never changes.

#### 1.3 Create `ICheckedType` Interface

**New file:** `Tyhp/TyhpLang/Checker/ICheckedType.cs`

The checker needs its own type representation that is richer than the AST's `ITypeExpression`. While `ITypeExpression` represents the syntax of a type annotation, `ICheckedType` represents the semantic meaning — resolved, expanded, and ready for compatibility checking.

```csharp
public interface ICheckedType
{
    CheckedTypeKind Kind { get; }
    string DisplayName { get; } // human-readable name for diagnostics (e.g., "int", "?string", "MyClass<int>")
    bool IsNullable { get; }
    bool IsNever { get; }
    bool IsVoid { get; }
    bool IsMixed { get; }
}
```

#### 1.4 Create `CheckedType` Hierarchy

**New file:** `Tyhp/TyhpLang/Checker/CheckedType.cs`

Create a `CheckedTypeKind` enum:
- `Simple` — scalar types, built-in types, named class/interface/trait/enum types
- `Union` — union type (`A|B`)
- `Intersection` — intersection type (`A&B`)
- `Nullable` — nullable wrapper (`?T`)
- `Generic` — generic instantiation (`Collection<User>`)
- `Literal` — literal value types (`true`, `false`, `null`, specific string/int literals)
- `Struct` — struct type (structural typing)
- `Callable` — callable/closure type
- `Never` — bottom type
- `Void` — void (return-type only)
- `Mixed` — top type
- `Unknown` — type could not be determined (error recovery)
- `Inferred` — type inferred from context (not yet resolved)

Create concrete implementations (all implementing `ICheckedType`):

- `SimpleCheckedType` — wraps a resolved `IBaseSymbol` (from the binder). Has `IBaseSymbol ResolvedSymbol`. Represents named types like `int`, `string`, `MyClass`, `\App\Models\User`.
- `UnionCheckedType` — has `IReadOnlyList<ICheckedType> Members`. Represents `A|B|C`.
- `IntersectionCheckedType` — has `IReadOnlyList<ICheckedType> Members`. Represents `A&B`.
- `NullableCheckedType` — has `ICheckedType InnerType`. Represents `?T` (equivalent to `T|null`).
- `GenericCheckedType` — has `ICheckedType BaseType` and `IReadOnlyList<ICheckedType> TypeArguments`. Represents `Collection<User>`, `array<string, int>`.
- `LiteralCheckedType` — has `object? Value` and `SimpleCheckedType UnderlyingType`. Represents `true`, `false`, `null`, `42`, `'hello'`.
- `StructCheckedType` — has `Dictionary<string, ICheckedType> Properties`. Represents struct types with structural shape.
- `CallableCheckedType` — has `IReadOnlyList<ICheckedType> ParameterTypes` and `ICheckedType ReturnType`. Represents `callable(int, string): bool`.
- `SpecialCheckedType` — for `never`, `void`, `mixed`. Has `CheckedTypeKind Kind` distinguishing which.
- `UnresolvedCheckedType` — singleton for error recovery. Assignable to/from anything without producing cascading errors. (Renamed from `UnresolvedCheckedType` on 2026-07-24: the old name read as TypeScript's strict `unknown`, which is the opposite of its permissive behavior, and it leaked into diagnostics. `mixed` is the strict top type.)

Static factory methods on a `CheckedTypes` utility class:
- `CheckedTypes.Never` — singleton never type
- `CheckedTypes.Void` — singleton void type
- `CheckedTypes.Mixed` — singleton mixed type
- `CheckedTypes.Null` — singleton null literal type
- `CheckedTypes.Unknown` — singleton unknown type (error recovery)
- `CheckedTypes.Bool`, `CheckedTypes.Int`, `CheckedTypes.Float`, `CheckedTypes.String` — common scalar singletons
- `CheckedTypes.FromSymbol(IBaseSymbol symbol)` — create from a binder symbol
- `CheckedTypes.FromTypeExpression(ITypeExpression typeAst, IBaseScope scope, SymbolTree symbolTree)` — resolve an AST type expression to a checked type

#### 1.5 Redesign `CheckerState`

**File:** `Tyhp/TyhpLang/Checker/CheckerState.cs`

Back up the existing file. Redesign based on the existing property structure (which provides good architectural hints) plus the requirements from TODO.md:

Properties to keep (updated types where needed):
- `CheckerState? Parent` — link to enclosing scope's state
- `MemberModifier Modifiers` — active modifiers for current scope
- `IReadOnlyList<GenericTypeParameterSymbol> ObjectGenerics` — generic params from enclosing class (replaces `IEnumerable<GenericParameterAst>` — use binder symbols, not AST nodes)
- `IReadOnlyList<GenericTypeParameterSymbol> FunctionGenerics` — generic params from enclosing function/method
- `ObjectDeclarationSymbol? EnclosingObject` — the containing class/interface/trait/enum/struct (replaces `IBaseAst? ObjectDeclarationAst`)
- `FunctionDeclarationSymbol? EnclosingFunction` — the containing function/method (replaces `IBaseAst? FunctionDeclarationAst`)
- `ICheckedType? ExpectedReturnType` — expected return type of enclosing function (replaces `IType? ReturnOrMemberType`)
- `ScopeType ScopeType` — current scope type (uncommented)
- `Dictionary<string, VariableState> Variables` — variable tracking by name (replaces `IEnumerable<VariableState>`)

New properties:
- `bool IsInAsyncContext` — whether we're inside an `async` function (for await validation)
- `bool IsInLoopContext` — whether we're inside a loop (for break/continue validation)
- `int LoopDepth` — nesting level of loops (for `break N` / `continue N` validation)
- `bool IsInSwitchContext` — whether we're inside a switch (for break validation)
- `bool HasReturnedOnAllPaths` — whether all code paths have returned (for return type validation)
- `ICheckedType? EnclosingObjectType` — resolved checked type of the enclosing class (for `$this` type)
- `bool IsInsideFinally` — for validating control flow in finally blocks
- `string? CurrentFileName` — which file is being checked (for diagnostics)

**`SnapShot()` Implementation:**
Create a deep copy of the current state where all collections are copied (not shared). The snapshot is immutable — modifications to the original do not affect the snapshot and vice versa. This is used before branching control flow (if/else, try/catch) to capture the state for later comparison.

```csharp
public CheckerState SnapShot()
{
    var snapshot = new CheckerState(this);
    snapshot.Variables = new Dictionary<string, VariableState>(
        this.Variables.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Clone())
    );
    snapshot._locked = true;
    return snapshot;
}
```

**`Split(ScopeType)` Implementation:**
Create a child state for entering a new scope. The behavior varies by scope type:

| New Scope Type | Object Generics | Function Generics | Variables | EnclosingObject | EnclosingFunction | ExpectedReturnType |
|---------------|----------------|-------------------|-----------|----------------|-------------------|-------------------|
| Root/Global | Clear | Clear | Clear | Clear | Clear | Clear |
| Namespace/File | Carry | Clear | Clear | Clear | Clear | Clear |
| Object (class/interface/trait/enum/struct) | Set from object decl | Clear | Clear | Set | Clear | Clear |
| Function | Carry object | Set from func decl | New (params only) | Carry | Set | Set from func return type |
| Method (instance) | Carry object | Set from method decl | New (params + $this) | Carry | Set | Set from method return type |
| Method (static) | Carry object | Set from method decl | New (params only) | Carry | Set | Set from method return type |
| CodeBlock (if/for/while/etc.) | Carry | Carry | Copy from parent | Carry | Carry | Carry |
| SubBlock (else/elseif/catch) | Carry | Carry | Copy from parent (or from snapshot) | Carry | Carry | Carry |
| Label | Carry | Carry | Copy from parent | Carry | Carry | Carry |
| Anonymous function | Carry object | Set from anon func | New (params + use captures) | Carry | Set | Set from anon func return type |
| Anonymous class | Set from anon class | Clear | Clear | Set | Clear | Clear |

**`Merge(CheckerState branchState)` Implementation:**
Merge two branch states back together after control flow divergence (after if/else, try/catch). Rules:

- **Variables defined in BOTH branches:** Keep, with type = union of the two branches' types. `IsDefinitelyAssigned` = true only if true in both branches.
- **Variables defined in ONLY ONE branch:** Keep, but mark `IsPossiblyUndefined = true`.
- **Type narrowing:** Take the wider (less narrowed) type after merge. If one branch narrowed `$x` to `Foo` and the other didn't narrow, the merged type is the original (pre-narrowing) type.
- **Null tracking:** `IsPossiblyNull` is true if true in EITHER branch.
- **`HasReturnedOnAllPaths`:** True only if true in ALL branches (both the if-branch and else-branch returned).

#### 1.6 Add New MessageCode Values

**File:** `Tyhp/Domain/Exceptions/MessageCode.cs`

Add checker-specific error codes in the 4000 range. The following codes **already exist** in `MessageCode.cs` and MUST NOT be redefined: `4001`–`4007`, and **`4038 CheckerExtensionVisibilityNotAllowed`** (already committed — Story 08 *references* it, see §6.2.1). Add the new codes below. This is the complete set of codes Story 08 introduces and is mirrored exactly by Appendix B; every code referenced in this plan's prose appears here exactly once.

```
// Type compatibility errors
CheckerTypeMismatch = 4008,                   // "Cannot assign type '{0}' to type '{1}'"
CheckerIncompatibleReturnType = 4009,         // "Return type '{0}' is not compatible with declared return type '{1}'"
CheckerIncompatibleArgumentType = 4010,       // "Argument of type '{0}' is not assignable to parameter of type '{1}'"
CheckerMissingReturnStatement = 4011,         // "Function with return type '{0}' must return a value on all code paths"
CheckerUnreachableCode = 4012,                // "Unreachable code detected"

// Variable errors
CheckerVariableUsedBeforeAssignment = 4013,   // "Variable '${0}' is used before being assigned"
CheckerVariablePossiblyUndefined = 4014,      // "Variable '${0}' is possibly undefined"
CheckerVariablePossiblyNull = 4015,           // "Variable '${0}' is possibly null"
CheckerVariableTypeRequired = 4016,           // "Variable '${0}' must have a type annotation or inferable initializer"

// Class/interface validation errors
CheckerAbstractMethodNotImplemented = 4017,   // "Class '{0}' does not implement abstract method '{1}' from '{2}'"
CheckerInterfaceMethodNotImplemented = 4018,  // "Class '{0}' does not implement interface method '{1}' from '{2}'"
CheckerFinalClassExtended = 4019,             // "Cannot extend final class '{0}'"
CheckerFinalMethodOverridden = 4020,          // "Cannot override final method '{0}'"
CheckerReadonlyPropertyReassigned = 4021,     // "Cannot assign to readonly property '{0}'"
CheckerAbstractClassInstantiated = 4022,      // "Cannot instantiate abstract class '{0}'"

// Enum validation errors
CheckerEnumCaseTypeMismatch = 4023,           // "Enum case value type '{0}' does not match backed type '{1}'"
CheckerEnumMethodNotAllowed = 4024,           // "Enum cannot have a constructor"

// Visibility errors
CheckerMemberNotAccessible = 4025,            // "'{0}' is {1} and cannot be accessed from '{2}'"

// Control flow errors
CheckerBreakOutsideLoop = 4026,               // "'break' statement is not within a loop or switch"
CheckerContinueOutsideLoop = 4027,            // "'continue' statement is not within a loop"
CheckerAwaitOutsideAsync = 4028,              // "'await' can only be used inside an async function"

// Operator errors
CheckerInvalidOperatorForType = 4029,         // "Operator '{0}' cannot be applied to types '{1}' and '{2}'"

// Tyhp-specific errors
CheckerDisposableRequiresInterface = 4030,    // "Disposable assignment ':=' requires type implementing 'IsDisposable'"
CheckerWithKeywordInvalidProperty = 4031,     // "Property '{0}' does not exist on type '{1}'"
CheckerTypeGuardInvalidReturn = 4032,         // "Type guard function must return bool"
CheckerExtensionMethodNotStatic = 4033,       // "Extension method '{0}' must be static"
CheckerExtensionMethodNotPublic = 4034,       // "Extension method '{0}' must be public"
CheckerGenericConstraintNotSatisfied = 4035,  // "Type '{0}' does not satisfy constraint '{1}'"
CheckerGenericArgumentCountMismatch = 4036,   // "Generic type '{0}' expects {1} type argument(s), but {2} were provided"

// Struct-specific errors
CheckerStructPropertyRequired = 4037,         // "All struct properties must be typed"

// NOTE: 4038 is RESERVED — it is the already-committed CheckerExtensionVisibilityNotAllowed.
// Do NOT redefine it. The overload-signature diagnostic that previously claimed 4038 has been
// moved to 4118 (see CheckerOverloadSignatureIncompatible below).

// Throwable constraint
CheckerThrowNotThrowable = 4039,              // "'throw' expression must be an instance of \\Throwable"
CheckerCatchNotThrowable = 4040,              // "Caught type '{0}' must implement \\Throwable"
CheckerCatchNoIntersection = 4041,            // "Catch clause cannot use intersection types"
CheckerCatchNoScalar = 4042,                  // "Cannot catch scalar type '{0}'"

// Logical condition type
CheckerConditionNotBool = 4043,               // "Condition must be of type 'bool', got '{0}'"

// Trait errors
CheckerTraitRequirementNotMet = 4044,         // "Trait '{0}' requires the using class to extend '{1}'"
CheckerTraitRequirementImplNotMet = 4045,     // "Trait '{0}' requires the using class to implement '{1}'"

// Async iteration errors
CheckerAsyncIterableMissingAwait = 4046,      // "Cannot iterate 'AsyncIterable<{0}>' synchronously; use 'foreach (await $expr as ...)' inside an async function"
CheckerAwaitNonAsyncIterable = 4047,          // "'await' in foreach requires 'AsyncIterable<T>' or 'Promise<Iterable<T>>', got '{0}'"

// Restricted type in generic position errors
CheckerVoidInNonReturnPosition = 4048,        // "Type 'void' can only be used as a return type or in generic positions that explicitly allow it via constraint"
CheckerNeverInNonReturnPosition = 4049,       // "Type 'never' can only be used as a return type or in generic positions that explicitly allow it via constraint"

// Utility-type and reference errors
CheckerUtilityTypeInvalidKey = 4050,          // "Utility type key '{0}' does not match any property on type '{1}'"
CheckerUtilityTypeInvalidArgument = 4051,     // "Utility type argument does not satisfy constraint"
CheckerReferenceTypeChanged = 4052,           // "Reference parameter '${0}' reassigned to a different type"

// Composite (union/intersection) type errors
CheckerDuplicateTypeInComposite = 4053,       // "Type '{0}' appears more than once in a union/intersection"
CheckerMixedInComposite = 4054,               // "'mixed'/'never' cannot be used in a union/intersection"
CheckerRedundantTypeInUnion = 4055,           // "Redundant type in union (e.g., 'bool|false', 'object|MyClass', 'iterable|array')"
CheckerUseBoolInsteadOfTrueFalse = 4056,      // "'true|false' should be written as 'bool'"
CheckerNonClassInIntersection = 4057,         // "Only class/interface types may appear in an intersection"
CheckerCallableNotAllowedOnProperty = 4058,   // "'callable' cannot be used as a property type"
CheckerVoidNotAllowedHere = 4059,             // "'void' is only valid as a return type"
CheckerVoidRefReturn = 4060,                  // "Returning by reference from a void function is deprecated" (warning)
CheckerNeverNotAllowedHere = 4061,            // "'never' is only valid as a return type"
CheckerResourceNotAllowed = 4062,             // "'resource' cannot be used in user type declarations"
CheckerRefArgMustBeVariable = 4063,           // "By-reference argument must be a variable, not a literal"

// Relative-type (self/parent/static) errors
CheckerRelativeTypeOutsideClass = 4064,       // "'self'/'parent' used outside a class context"
CheckerParentWithoutParent = 4065,            // "'parent' used in a class that has no parent"
CheckerStaticNotReturnType = 4066,            // "'static' used outside return-type position"
CheckerDnfRedundantIntersection = 4067,       // "Redundant intersection in DNF type"

// Instantiation / clone errors
CheckerNeverMustNotReturn = 4068,             // "Function with 'never' return type contains a return statement"
CheckerCannotInstantiateNonClass = 4069,      // "Cannot 'new' a scalar/array/callable/built-in type"
CheckerCannotInstantiateTrait = 4070,         // "Cannot 'new' a trait"
CheckerCannotInstantiateInterface = 4071,     // "Cannot 'new' an interface"
CheckerCannotInstantiateEnum = 4072,          // "Cannot 'new' an enum"
CheckerCloneNonObject = 4073,                 // "'clone' on a non-object type"

// Magic-method and parameter errors
CheckerMagicMethodSignature = 4074,           // "Magic method '{0}' has an invalid signature"
CheckerDuplicateParameter = 4075,             // "Duplicate parameter name '${0}'"
CheckerRequiredAfterOptional = 4076,          // "Required parameter after optional parameter" (warning)
CheckerVariadicNotLast = 4077,                // "Variadic parameter must be the last parameter"
CheckerVariadicWithDefault = 4078,            // "Variadic parameter cannot have a default value"

// Argument errors
CheckerDuplicateNamedArgument = 4079,         // "Named argument '{0}' used more than once"
CheckerPositionalAfterNamed = 4080,           // "Positional argument after named argument"
CheckerUnknownNamedArgument = 4081,           // "No parameter named '{0}'"
CheckerNamedAfterUnpack = 4082,               // "Named argument after argument unpacking"

// Closure errors
CheckerClosureUseUndefined = 4083,            // "Closure 'use' variable '${0}' is not defined in the outer scope"
CheckerClosureUseThis = 4084,                 // "'use($this)' is redundant in non-static closures" (warning)
CheckerStaticClosureThis = 4085,              // "Static closure cannot reference '$this'"

// Generator / yield errors
CheckerYieldOutsideGenerator = 4086,          // "'yield' outside a generator function"
CheckerGeneratorInvalidReturnType = 4087,     // "Generator function with non-Generator return type"
CheckerYieldInFinally = 4088,                 // "'yield' inside a 'finally' block"
CheckerYieldFromNonIterable = 4089,           // "'yield from' on a non-iterable expression"

// Constant-expression / array errors
CheckerNonConstantExpression = 4090,          // "Non-constant expression in a constant-required context"
CheckerDivisionByZero = 4091,                 // "Division by zero in a constant expression"
CheckerDuplicateArrayKey = 4092,              // "Duplicate key in array literal" (warning)
CheckerInvalidArrayAccess = 4093,             // "Array access on a non-array/non-ArrayAccess type"
CheckerDestructuringNonArray = 4094,          // "List/destructuring on a non-array type"
CheckerDestructuringSpread = 4095,            // "Spread is not allowed in list/destructuring"
CheckerSpreadNonIterable = 4096,              // "Spread operator on a non-iterable"

// Static / instance context errors
CheckerThisInStaticContext = 4097,            // "'$this' used inside a static method or static closure"
CheckerNonStaticCalledStatically = 4098,      // "Non-static method called statically"
CheckerStaticCalledOnInstance = 4099,         // "Static method called on an instance" (warning)
CheckerStaticOutsideClass = 4100,             // "'static::' used outside a class context"

// 4101–4103 are intentionally unused (the original goto-label diagnostics were removed; goto is
// prohibited outright via 4104 below).
CheckerGotoProhibited = 4104,                 // "'goto' is prohibited in Tyhp"

// Promoted-property / readonly-class errors
CheckerPromotedPropertyNoType = 4105,         // "Promoted constructor property must have a type"
CheckerPromotedPropertyInAbstract = 4106,     // "Promoted property in an abstract/interface constructor"
CheckerPromotedVariadic = 4107,               // "Variadic parameter cannot be a promoted property"
CheckerReadonlyClassMutableProperty = 4108,   // "Mutable property in a readonly class"
CheckerReadonlyClassStaticProperty = 4109,    // "Non-constant static property in a readonly class"

// Enum / interface / trait errors
CheckerEnumCaseMissingValue = 4110,           // "Backed enum case is missing a value"
CheckerEnumCaseValueOnNonBacked = 4111,       // "Case value on a non-backed enum"
CheckerEnumCaseDuplicateValue = 4112,         // "Duplicate enum case value"
CheckerEnumPropertyNotAllowed = 4113,         // "Instance property not allowed on an enum"
CheckerInterfacePropertyInitializer = 4114,   // "Property initializer not allowed in an interface"
CheckerInterfacePropertyNotAllowed = 4115,    // "Instance property not allowed on an interface"
CheckerTraitConflict = 4116,                  // "Unresolved trait method conflict"
CheckerCircularTraitUse = 4117,               // "Circular trait use detected"

// 4118 (formerly the removed CheckerLooseComparisonWarning) now carries the overload diagnostic
// that was moved off the reserved 4038.
CheckerOverloadSignatureIncompatible = 4118,  // "Overload signature is not compatible with implementation signature"
CheckerIncomparableTypes = 4119,              // "Comparing types with no meaningful comparison" (warning)
CheckerConcatNonStringable = 4120,            // "String concatenation/echo with a non-stringable type"

// finally / catch quality errors
CheckerEmptyCatch = 4121,                     // "Empty catch block" (warning)
CheckerReturnInFinally = 4122,                // "'return' inside a 'finally' block" (warning)
CheckerBreakInFinally = 4123,                 // "'break'/'continue' inside a 'finally' block" (warning)
CheckerDuplicateCatch = 4124,                 // "Same exception type caught more than once" (warning)
CheckerCatchOrderBroadFirst = 4125,           // "Parent exception caught before child" (warning)

// Attribute errors
CheckerNotAnAttributeClass = 4126,            // "Class used as an attribute is not declared as an attribute"
CheckerAttributeTargetMismatch = 4127,        // "Attribute used on the wrong target"
CheckerAttributeNotRepeatable = 4128,         // "Non-repeatable attribute used multiple times"
CheckerOverrideNotOverriding = 4129,          // "'#[Override]' on a method that does not override"

// Import errors
CheckerUnusedImport = 4130,                   // "Unused 'use' import" (warning)
CheckerDuplicateImport = 4131,                // "Duplicate import" (warning)
CheckerConflictingImportAlias = 4132,         // "Two imports with the same alias"

// Restricted-feature errors
CheckerVariableVariableProhibited = 4133,     // "Variable variables ('$$var') are prohibited in Tyhp"
CheckerDynamicPropertyProhibited = 4134,      // "Dynamic property creation is prohibited"
CheckerCompactProhibited = 4135,              // "'compact()' is prohibited in Tyhp"
CheckerExtractProhibited = 4136,              // "'extract()' is prohibited in Tyhp"
CheckerGlobalVariableWarning = 4137,          // "'global $var' usage" (warning)

// Closure parameter inference errors
CheckerClosureParameterTypeRequired = 4138,   // "Cannot infer type for closure parameter '${0}'; provide an explicit type annotation"

// with keyword — readonly restrictions
CheckerCloneWithReadonlyRequiresConfig = 4139, // "Clone 'with' on readonly property '{0}' requires 'build.experimentalReadonlyCloneWith: true' in tyhp.json for PHP < 8.5"
CheckerWithReadonlyFinalClass = 4140,          // "Cannot use 'with' on readonly properties of final class '{0}' on PHP < 8.5"
CheckerWithReadonlyInPlace = 4141,             // "Cannot modify readonly property '{0}' with in-place 'with'; use 'clone ... with' or 'new ... with' instead"

// Function-call argument-count errors
CheckerMissingArgument = 4142,                 // "Missing required argument for parameter '${0}' of '{1}'"
CheckerTooManyArguments = 4143,               // "Too many arguments passed to '{0}'; expected at most {1}, got {2}"

// Code-quality warnings (4200+ range)
CheckerUnusedVariable = 4200,                 // "Variable '${0}' is assigned but never read" (warning)
CheckerUnusedParameter = 4201,                // "Parameter '${0}' is never used" (warning)
CheckerUnusedPrivateMember = 4202,            // "Private member '{0}' is never referenced" (warning)
CheckerAssignmentInCondition = 4203,          // "Assignment in a condition expression" (warning)
CheckerConditionAlwaysTrueFalse = 4204,       // "Condition is always true or false" (warning)
CheckerRedundantCast = 4205,                  // "Redundant cast to the same type" (warning)
CheckerDeadStore = 4206,                      // "Variable assigned then overwritten before read" (warning)
CheckerUnnecessaryNullCheck = 4207,           // "Null check on a non-nullable type" (warning)
CheckerUnreachableArm = 4208,                 // "Unreachable match/switch arm" (warning)
CheckerLossyCast = 4209,                      // "Lossy cast" (warning)
CheckerErrorThresholdReached = 4210,          // "Error threshold reached for file" (info)
CheckerStaticReturnSelfInNonFinal = 4211,     // "'new self()' returned from a 'static' return in a non-final class" (warning)

// Deprecation warnings (4500+ range)
CheckerDeprecatedUsage = 4500,                // "'{0}' is deprecated"
CheckerObsoleteUsage = 4501,                  // "'{0}' is obsolete and should not be used"

// Informational (4800+ range)
CheckerEvalUsage = 4800,                      // "'eval()' usage detected — this is disabled in Tyhp by default"
CheckerIncludeNotAllowed = 4801,              // "'include'/'require' is not allowed in Tyhp; use 'import' instead"
```

Add corresponding entries to the `.resx` resource file(s) created in Story 01.

> **Code allocation:** Story 08 owns the contiguous checker band **`4008–4211`** (excluding the reserved
> `4038 CheckerExtensionVisibilityNotAllowed`). Feature-story checker diagnostics (Stories 16, 25, 26,
> 27, 28) live in the separate **`4300–4399`** band, so they do not collide with Story 08's codes.

### Acceptance Criteria

- [ ] `TyhpChecker.cs` is the orchestrator class with a constructor accepting `DiagnosticBag`, `SymbolTree`, `GlobalScope` and initializing `CheckerRuleRegistry`
- [ ] `ICheckerRule` interface defined with `IEnumerable<Type> HandledNodeTypes { get; }` (used by the registry to index rules by AST node type), `bool Handles(IBase2Ast node)` (optional per-node refinement; default returns `true`), and `void Check(IBase2Ast node, CheckerState state, DiagnosticBag diagnostics)`
- [ ] `CheckerRuleRegistry` collects all rules and dispatches by AST node type (via `HandledNodeTypes`), applying `Handles()` before `Check()`
- [ ] `Check(IEnumerable<SrcFileAst>)` iterates files and dispatches to rules via registry without crashing
- [ ] `CheckNode()` gracefully skips `ErrorAst` nodes
- [ ] `ICheckedType` interface exists with `Kind`, `DisplayName`, `IsNullable`, `IsNever`, `IsVoid`, `IsMixed` properties
- [ ] All `CheckedType` concrete classes exist: `SimpleCheckedType`, `UnionCheckedType`, `IntersectionCheckedType`, `NullableCheckedType`, `GenericCheckedType`, `LiteralCheckedType`, `StructCheckedType`, `CallableCheckedType`, `SpecialCheckedType`, `UnresolvedCheckedType`
- [ ] `CheckedTypes` utility class provides factory methods and singletons for common types
- [ ] `CheckerState.SnapShot()` creates an immutable deep copy
- [ ] `CheckerState.Split(ScopeType)` creates child states with correct property inheritance per scope type
- [ ] `CheckerState.Merge(CheckerState)` correctly merges variable states from branching control flow
- [ ] New `MessageCode` values are added in the 4000 range with corresponding `.resx` entries
- [ ] The project compiles with no errors
- [ ] The checker can be instantiated and called on empty/trivial AST trees without crashing

### Dependencies

- **Requires:** Story 01 (`DiagnosticBag`, `MessageCode`), Story 02 (`GlobalScope`, `SymbolTree`, `IBaseScope`, symbol types), Story 06 (built-in types, utility types, and tyhpdef symbols in scope)
- **Provides:** Core checker infrastructure for all subsequent phases; `ICheckedType` hierarchy for type representation; `CheckerState` for scope-local tracking

---

## Phase 2: VariableState, Type Inference, and Expression Type Resolution




### Phase Overview

Implement `VariableState` for per-variable tracking (definite assignment, nullability, type narrowing, disposable status), create the `TypeInferrer` utility that resolves AST type expressions to `ICheckedType` instances, and implement the expression type resolver that computes the `ICheckedType` of any expression AST node. These are the fundamental building blocks used by every type check in later phases.

### Deliverables

- `Tyhp/TyhpLang/Checker/VariableState.cs` — Fully implemented per-variable state tracking
- `Tyhp/TyhpLang/Checker/TypeInferrer.cs` — Resolves AST `ITypeExpression` to `ICheckedType`, infers expression types
- Updated `CheckerState.cs` — Integration with `VariableState` operations (declare, assign, narrow, merge)
- Updated `TyhpChecker.cs` — Helper method `ResolveExpressionType(IBase2Ast expr, CheckerState state): ICheckedType`

### Implementation Details

#### 2.1 Implement `VariableState`

**File:** `Tyhp/TyhpLang/Checker/VariableState.cs`

```csharp
public class VariableState
{
    // Which variable this tracks (from the binder)
    public VariableSymbol? Symbol { get; init; }

    // The declared type of the variable (from type annotation or parameter type)
    public ICheckedType? DeclaredType { get; init; }

    // Current narrowed type — may be narrower than DeclaredType due to type guards, instanceof, etc.
    // null means "same as DeclaredType" (no narrowing active)
    public ICheckedType? NarrowedType { get; set; }

    // The effective type at this point in the code
    public ICheckedType EffectiveType => NarrowedType ?? DeclaredType ?? CheckedTypes.Unknown;

    // Has this variable been assigned on all code paths reaching this point?
    public bool IsDefinitelyAssigned { get; set; }

    // Could this variable be null at this point?
    public bool IsPossiblyNull { get; set; }

    // Could this variable be unset/undefined at this point?
    public bool IsPossiblyUndefined { get; set; }

    // Was this assigned with the disposable operator :=?
    public bool IsDisposable { get; set; }

    // Is this a parameter? (Parameters are always definitely assigned)
    public bool IsParameter { get; set; }

    // Is this variable a reference (&$var or pass-by-reference parameter)?
    public bool IsReference { get; set; }

    // If IsReference is true, this is the set of variable names that share
    // the same underlying storage. When one reference variable is reassigned,
    // all variables in the group must have their NarrowedType updated.
    // null if not a reference variable.
    public ReferenceGroup? ReferenceGroup { get; set; }

    // Clone for snapshot/merge operations
    public VariableState Clone() { ... }
}
```

**`ReferenceGroup`** — tracks aliased variables sharing the same storage:

```csharp
public class ReferenceGroup
{
    public HashSet<string> MemberVariables { get; } = new();

    // When any member is reassigned, all members' NarrowedType is updated
    // to the assigned type (or widened to a union if the assignment type
    // differs from the current type).
    public void PropagateTypeChange(string assignedVariable, ICheckedType newType,
        Dictionary<string, VariableState> variables) { ... }
}
```

**Reference tracking behavior:**
- When a pass-by-reference parameter is declared (`function foo(int &$x)`), the parameter's `VariableState` is created with `IsReference = true`.
- When a reference assignment occurs (`$a = &$b`), both `$a` and `$b` get `IsReference = true` and are added to the same `ReferenceGroup`.
- When a reference variable is reassigned to a different type (e.g., `$param = 1` inside a function that received `array &$param`), the checker updates the type for all members of the `ReferenceGroup`. This matches PHP's behavior where the type is only checked on function entry, not when the function returns.
- The checker emits a warning `CheckerReferenceTypeChanged` (4052) when a reference parameter is reassigned to a type that differs from its declared type, because the caller's variable will also change type — this can cause runtime `TypeError` on next use.
- When a function modifies a reference parameter to a wider or different type, the caller's variable must be treated as having the widened type after the call. If the checker cannot determine the function's effect on the reference, the caller's variable type becomes `mixed` after the call.
- `Merge()` on `CheckerState` must account for reference groups: if a variable is in a reference group and was modified in one branch but not another, the merged type must be the union of both branch types.

Key operations:
- `Clone()` — deep copy for `CheckerState.SnapShot()` and branching (must also clone `ReferenceGroup` membership)
- Static factory `VariableState.ForParameter(VariableSymbol param, ICheckedType type, bool isReference)` — creates a definitely-assigned variable state for function parameters, with reference tracking
- Static factory `VariableState.ForDeclaration(VariableSymbol symbol, ICheckedType? type, bool isAssigned)` — creates state for a variable declaration

#### 2.2 Add Variable Tracking Operations to `CheckerState`

**File:** `Tyhp/TyhpLang/Checker/CheckerState.cs`

Add methods for managing variables within a scope:

- `void DeclareVariable(string name, VariableSymbol symbol, ICheckedType? type, bool isAssigned)` — register a new variable in the current scope. If the variable already exists in this scope, report a diagnostic (Tyhp does not allow re-declaration in the same scope, unlike PHP).

- `void AssignVariable(string name, ICheckedType type)` — mark a variable as assigned with the given type. Walk up scopes to find the variable. Set `IsDefinitelyAssigned = true` and update the effective type.

- `VariableState? LookupVariable(string name)` — find a variable in the current scope or any parent scope. Walk up the `Parent` chain. Stop at function boundaries (PHP/Tyhp variables are function-scoped, not block-scoped, unless explicitly declared with `global`).

- `void NarrowVariable(string name, ICheckedType narrowedType)` — apply type narrowing to a variable (e.g., after `instanceof` check). Sets `NarrowedType` on the `VariableState`.

- `void ResetNarrowing(string name)` — remove type narrowing (e.g., after a re-assignment that could change the type).

- `Dictionary<string, VariableState> GetAllVariablesInScope()` — get all visible variables from this scope and parents (for merge operations).

#### 2.3 Implement `TypeInferrer`

**New file:** `Tyhp/TyhpLang/Checker/TypeInferrer.cs`

The `TypeInferrer` converts AST type expressions into the checker's `ICheckedType` representation, using the binder's `SymbolTree` for name resolution.

**`ICheckedType ResolveTypeExpression(ITypeExpression typeAst, IBaseScope scope)`**

Dispatch by concrete AST type expression class:

- **Simple type name** (e.g., `int`, `string`, `MyClass`) — resolve via `SymbolTree.ResolveType()` to find the symbol, then wrap in `SimpleCheckedType(symbol)`
- **Nullable type** (`?T`) — resolve inner type, wrap in `NullableCheckedType(inner)` or `UnionCheckedType([inner, null])`
- **Union type** (`A|B`) — resolve each member, create `UnionCheckedType(members)`. Flatten nested unions.
- **Intersection type** (`A&B`) — resolve each member, create `IntersectionCheckedType(members)`. Flatten nested intersections.
- **Generic type** (`Collection<User>`) — resolve base type and each type argument, create `GenericCheckedType(base, args)`
- **Built-in type keywords** (`int`, `string`, `float`, `bool`, `array`, `callable`, `mixed`, `void`, `never`, `null`, `true`, `false`, `iterable`, `object`) — map to the corresponding `CheckedTypes` singleton or `SimpleCheckedType`
- **`self`** — resolve from `CheckerState.EnclosingObject` to the class type in which the declaration appears. If not inside a class, report `CheckerRelativeTypeOutsideClass` (4064) and return `CheckedTypes.Unknown`. In traits, `self` resolves to the composing class at check time. If the trait is being checked in isolation (not yet composed into a class), `self` resolves to `CheckedTypes.Unknown` and no diagnostic is emitted — the check will be performed when the trait is used.
- **`parent`** — resolve from `CheckerState.EnclosingObject.ParentClass`. If no parent, report `CheckerParentWithoutParent` (4065). If not inside a class, report `CheckerRelativeTypeOutsideClass` (4064).
- **`static`** — only valid as a return type. Resolve from `CheckerState.EnclosingObject` but mark as a late-static-binding type. If used as a parameter or property type, report `CheckerStaticNotReturnType` (4066). The `RelativeTypeRule` performs the detailed validation.
- **`resource`** — if encountered in a user type declaration, report `CheckerResourceNotAllowed` (4062). If encountered from a tyhpdef (e.g., `fopen(): resource|false`), create a `SimpleCheckedType` for it.
- **Unresolvable type** — return `CheckedTypes.Unknown` and add a diagnostic

**`ICheckedType InferExpressionType(IBase2Ast expression, CheckerState state)`**

Compute the type of an expression by dispatching on AST node type:

- **Integer literal** → `CheckedTypes.Int` (or `LiteralCheckedType(value, int)` for literal types)
- **Float literal** → `CheckedTypes.Float`
- **String literal** → `CheckedTypes.String`
- **Boolean literal** (`true`/`false`) → `CheckedTypes.Bool` (or `LiteralCheckedType(true/false, bool)`)
- **Null literal** → `CheckedTypes.Null`
- **Variable reference** (`$x`) — look up in `CheckerState.Variables`, return `EffectiveType`
- **Binary expression** (`$a + $b`) — infer types of both operands, then determine result type based on operator and operand types (see operator type rules below)
- **Unary expression** (`!$a`, `-$x`) — infer operand type, determine result type
- **Function call** (`foo($x)`) — resolve function symbol, return its declared return type (resolving generics if applicable)
- **Method call** (`$obj->method()`) — resolve method symbol on the object's type, return its return type
- **Static method call** (`MyClass::method()`) — same, but for static methods
- **Property access** (`$obj->prop`) — resolve property symbol, return its declared type
- **Array access** (`$arr[$key]`) — infer array type, return element type
- **New expression** (`new MyClass()`) — return the class type
- **Clone expression** (`clone $obj`) — return the same type as the operand
- **Instanceof expression** (`$x instanceof Foo`) — return `CheckedTypes.Bool`
- **Ternary expression** (`$a ? $b : $c`) — union of true-branch and false-branch types
- **Null coalescing** (`$a ?? $b`) — type of `$a` with null removed, or type of `$b`
- **Match expression** — union of all arm return types
- **Inline function / closure** — `CallableCheckedType` with parameter and return types
- **Cast expression** (`(int)$x`) — the target type
- **Assignment expression** (`$x = expr`) — the type of the right-hand side (and update variable state)
- **`ErrorAst`** — return `CheckedTypes.Unknown`
- **Unrecognized expression** — return `CheckedTypes.Unknown` (checker skips what it doesn't understand)

**Operator type inference rules:**

**Arithmetic operators:**

| Operator | Left Type | Right Type | Result Type |
|----------|-----------|------------|-------------|
| `+` | `int` | `int` | `int` |
| `+` | `float` | `float` | `float` |
| `+` | `int` | `float` | `float` |
| `-` | `int` | `int` | `int` |
| `-` | `float` | `float` | `float` |
| `-` | `int` | `float` | `float` |
| `*` | `int` | `int` | `int` |
| `*` | `float` | `float` | `float` |
| `*` | `int` | `float` | `float` |
| `+`, `-`, `*` | `decimal` | `decimal` | `decimal` |
| `/` | `int` | `int` | `int\|float` (division may produce float) |
| `/` | `float` | `float` | `float` |
| `%` | `int` | `int` | `int` |
| `**` | `int` | `int` | `int\|float` (exponentiation may produce float) |
| `**` | `float` | `float` | `float` |

**String concatenation:**

| Operator | Left Type | Right Type | Result Type |
|----------|-----------|------------|-------------|
| `.` | `string` | `string` | `string` |
| `.` | `string` | `int` | `string` |
| `.` | `string` | `float` | `string` |

**Bitwise operators:**

| Operator | Left Type | Right Type | Result Type |
|----------|-----------|------------|-------------|
| `&` | `int` | `int` | `int` |
| `\|` | `int` | `int` | `int` |
| `^` | `int` | `int` | `int` |
| `~` (unary) | `int` | — | `int` |
| `<<` | `int` | `int` | `int` |
| `>>` | `int` | `int` | `int` |

**Comparison operators:**

| Operator | Left Type | Right Type | Result Type |
|----------|-----------|------------|-------------|
| `==`, `===`, `!=`, `!==`, `<`, `>`, `<=`, `>=` | any | any | `bool` |
| `<=>` | any | any | `int` |

**Logical operators:**

| Operator | Left Type | Right Type | Result Type |
|----------|-----------|------------|-------------|
| `&&`, `\|\|`, `and`, `or`, `xor` | any | any | `bool` |
| `!` (unary) | any | — | `bool` |

**Null coalescing and ternary:**

| Operator | Left Type | Right Type | Result Type |
|----------|-----------|------------|-------------|
| `??` | `?T` | `U` | `T\|U` (union of both operand types, with null removed from left side) |
| `?:` | `T` | `U` | `T\|U` |

**Assignment:**

| Operator | Left Type | Right Type | Result Type |
|----------|-----------|------------|-------------|
| `=` | — | `T` | `T` (result type matches the assigned value type) |
| `+=`, `-=`, `*=`, `/=`, `%=`, `**=`, `.=`, `&=`, `\|=`, `^=`, `<<=`, `>>=`, `??=` | — | — | Follows the corresponding binary operator rules |

**Overloaded operators:**

| Operator | Left Type | Right Type | Result Type |
|----------|-----------|------------|-------------|
| Overloaded operator | `T` | `U` | return type from operator overload declaration |

Store computed expression types in `TyhpChecker._expressionTypes` dictionary for reuse.

#### 2.4 Wire Expression Type Resolution into TyhpChecker

**File:** `Tyhp/TyhpLang/Checker/TyhpChecker.cs`

Add helper methods:

- `ICheckedType ResolveExpressionType(IBase2Ast expr, CheckerState state)` — calls `TypeInferrer.InferExpressionType()` and caches the result in `_expressionTypes`
- `ICheckedType ResolveTypeAnnotation(ITypeExpression typeAst, CheckerState state)` — calls `TypeInferrer.ResolveTypeExpression()` using the current scope

These are called by all subsequent check methods when they need to know the type of an expression or resolve a type annotation.

### Acceptance Criteria

- [ ] `VariableState` tracks declared type, narrowed type, definite assignment, null possibility, undefined possibility, and disposable status
- [ ] `VariableState.Clone()` produces independent copies
- [ ] `CheckerState.DeclareVariable()` registers variables with correct initial state
- [ ] `CheckerState.LookupVariable()` walks up scope chain and stops at function boundaries
- [ ] `CheckerState.NarrowVariable()` updates the narrowed type on the variable state
- [ ] `TypeInferrer.ResolveTypeExpression()` correctly resolves simple, nullable, union, intersection, and generic type expressions
- [ ] `TypeInferrer.InferExpressionType()` returns correct types for: integer/float/string/bool/null literals, variable references, binary arithmetic, comparison operators, function calls, method calls, property access, new expressions
- [ ] Operator type inference follows PHP/Tyhp numeric promotion rules
- [ ] `UnresolvedCheckedType` is returned for unrecognized expressions (no crashes)
- [ ] `ErrorAst` nodes return `UnresolvedCheckedType` without producing additional diagnostics
- [ ] Expression types are cached in `_expressionTypes` dictionary for reuse
- [ ] All new files compile without errors
- [ ] Individual files stay under 500 lines

### Dependencies

- **Requires:** Phase 1 (`TyhpChecker`, `CheckerState`, `ICheckedType`, `CheckedType` hierarchy)
- **Provides:** Variable tracking and type resolution for all type checks in Phases 3-6

---

## Phase 3: Type Compatibility Checking (TypeComparer)




### Phase Overview

Create the `TypeComparer` utility class that answers the fundamental question of the type system: "Is type A compatible with type B?" This includes assignability checking, subtype checking, type equality, union/intersection computation, type narrowing, and generic type resolution. This is the core engine that every type check in Phases 4-6 relies on.

### Deliverables

- `Tyhp/TyhpLang/Checker/TypeComparer.cs` — Type compatibility checking utility with all comparison operations
- Integration with `TyhpChecker` — helper methods that delegate to `TypeComparer`

### Implementation Details

#### 3.1 Create `TypeComparer` Static Utility Class

**New file:** `Tyhp/TyhpLang/Checker/TypeComparer.cs`

All methods on `TypeComparer` are static. `SymbolTree` and `GlobalScope` are NOT stored as class state — they are passed as explicit parameters to each method that requires them. This maintains the pure-function design while allowing access to the type hierarchy. Example signature: `static bool IsAssignableTo(ICheckedType source, ICheckedType target, SymbolTree symbolTree, GlobalScope globalScope)`.

**Note:** For brevity, subsequent method signatures in sections 3.5–3.7 (`UnionTypes`, `IntersectTypes`, `NarrowType`) omit the `SymbolTree symbolTree` and `GlobalScope globalScope` parameters. These parameters are required on all `TypeComparer` public methods and must be included in the implementation.

#### 3.2 Implement `IsAssignableTo(ICheckedType source, ICheckedType target)`

The core assignability check. Returns `true` if a value of `source` type can be assigned to a location of `target` type.

Rules (evaluated in order):

1. **Unknown type is always compatible** — if either type is `UnresolvedCheckedType`, return `true` (error recovery — don't cascade errors)
2. **Same type** — if `AreTypesEqual(source, target)`, return `true`
3. **Mixed accepts everything** — if `target` is `mixed`, return `true`
4. **Never is assignable to everything** — if `source` is `never`, return `true` (bottom type)
5. **Null to nullable** — if `source` is `null` literal and `target.IsNullable`, return `true`
6. **Nullable assignability** — if `target` is `NullableCheckedType(T)`, check `IsAssignableTo(source, T)` or `source` is null
7. **Union target** — if `target` is `UnionCheckedType(A, B, ...)`, return `true` if `IsAssignableTo(source, any member of union)`
8. **Union source** — if `source` is `UnionCheckedType(A, B, ...)`, return `true` if every member of union is assignable to `target`
9. **Intersection source** — if `source` is `IntersectionCheckedType(A, B, ...)`, return `true` if any member is assignable to `target`
10. **Intersection target** — if `target` is `IntersectionCheckedType(A, B, ...)`, return `true` if `source` is assignable to ALL members
11. **Literal to base type** — if `source` is `LiteralCheckedType`, check `IsAssignableTo(source.UnderlyingType, target)`
12. **Subtype check** — if `source` is a class/interface type and `target` is a class/interface type, check `IsSubtypeOf(source, target)`
13. **Struct-to-struct compatibility** — if both are `StructCheckedType`, check structural compatibility (all properties of target exist in source with compatible types)
14. **Struct-to-array widening** — if `source` is `StructCheckedType` and `target` is an array type: `struct` is assignable to `array`, `array<string, mixed>`, or `array<string, V>` where V is a supertype of the union of all struct property value types (see §5.10 for full rules). Struct is NOT assignable to `array<int, V>` (struct keys are strings).
15. **Object-to-struct structural compatibility (intersection types)** — when an intersection type includes `object&StructType` (or a concrete class/interface type intersected with a struct type), the checker validates that the object type has all properties declared in the struct type, with compatible types. This enables `ObjectHelper::with()` (from `tyhp/core`) which uses `<TProperties extends struct, T extends object&TProperties>` to type-check that an object's properties match a struct shape. The check works as follows:
    - For explicit `object&StructType`: any object satisfies the `object` part; the struct part is validated by checking that the runtime object will have properties matching the struct's declared property names and types
    - For concrete class/interface types (e.g., `MyClass&StructType`): validate that `MyClass` declares all properties in `StructType` with compatible types (property name match + `IsAssignableTo(structPropertyType, classPropertyType)`)
    - This is structural typing: the class does not need to explicitly declare compatibility with the struct — having the matching properties is sufficient
16. **Callable assignability** — if `target` is `callable` (untyped) and `source` is not already a `CallableCheckedType`: return `true` if `source` is `\Closure`, or if `source` is an object type whose class declares an `__invoke()` method (see §3.9 for full rules)
16. **Callable-to-callable compatibility** — if both are `CallableCheckedType`, check parameter contravariance and return type covariance
17. **Generic compatibility** — if both are `GenericCheckedType` with the same base type, check that all type arguments are compatible (respecting variance)
18. **PHP numeric widening** — `int` is assignable to `float` (implicit widening)
19. **No implicit string/numeric interop** — Tyhp does NOT allow implicit string/numeric interop. `int` and `float` are NOT assignable to `string`, and `string` is NOT assignable to `int` or `float`. Explicit casting is required (e.g., `(string)$intVal`, `(int)$strVal`). The `int` → `float` numeric widening (rule 18) is the only implicit numeric conversion allowed.

#### 3.3 Implement `IsSubtypeOf(ICheckedType child, ICheckedType parent)`

Check nominal subtype relationship through class inheritance and interface implementation. This requires walking the binder's symbol tree.

For `SimpleCheckedType` / `GenericCheckedType`:
1. Resolve the child's `ObjectDeclarationSymbol` from the binder
2. Walk the `extends` chain: if the child class extends a class that matches `parent`, return `true`
3. Walk the `implements` list: if the child class implements an interface that matches `parent`, return `true`
4. Recursively check parent classes' `implements` lists
5. Check trait implementations if applicable
6. Handle special cases: every class is a subtype of `object`, every type is a subtype of `mixed`

#### 3.4 Implement `AreTypesEqual(ICheckedType a, ICheckedType b)`

Structural type equality check:

- `SimpleCheckedType`: equal if they reference the same symbol (by `FullyQualifiedName`)
- `UnionCheckedType`: equal if they have the same set of member types (order-independent)
- `IntersectionCheckedType`: equal if they have the same set of member types (order-independent)
- `NullableCheckedType`: equal if inner types are equal
- `GenericCheckedType`: equal if base types are equal AND all type arguments are equal
- `LiteralCheckedType`: equal if values are equal and underlying types are equal
- `StructCheckedType`: equal if they have the same set of property names with equal types
- `CallableCheckedType`: equal if parameter types and return types are equal
- `SpecialCheckedType`: equal if same `Kind`

#### 3.5 Implement `UnionTypes(ICheckedType a, ICheckedType b)`

Compute the union of two types. Used after if/else branches and in ternary expressions.

- If either is `unknown`, return the other
- If either is `never`, return the other (never is the identity for union)
- If `AreTypesEqual(a, b)`, return either
- If one is a subtype of the other, return the wider type
- If both are nullable, union the inner types and make nullable
- Otherwise, create `UnionCheckedType([a, b])` with flattening of nested unions
- Deduplicate: remove members that are subtypes of other members

#### 3.6 Implement `IntersectTypes(ICheckedType a, ICheckedType b)`

Compute the intersection of two types. Used for type narrowing.

- If either is `unknown`, return the other
- If either is `mixed`, return the other (mixed is the identity for intersection)
- If `AreTypesEqual(a, b)`, return either
- If one is a subtype of the other, return the narrower type
- Otherwise, create `IntersectionCheckedType([a, b])` with flattening
- If intersection is empty (incompatible types), return `never`

#### 3.7 Implement `NarrowType(ICheckedType current, ICheckedType narrowTo)`

Apply type narrowing — used after `instanceof` checks, type guards, and truthiness checks.

- If `current` is a union type containing `narrowTo`, extract it from the union
- If `narrowTo` is a subtype of `current`, return `narrowTo`
- If `current` is nullable and narrowing to non-null, remove null from the type
- If narrowing to `null`, return the null literal type
- For `instanceof` narrowing: intersect `current` with `narrowTo`

**Negative narrowing** (e.g., in the else-branch of `instanceof`):
- `NarrowTypeNegative(ICheckedType current, ICheckedType excludeType)` — remove `excludeType` from union, or return `current` minus `excludeType` members

#### 3.8 Implement `iterable` Type Equivalence

In PHP, `iterable` is a pseudo-type semantically equivalent to `array|\Traversable`. The `TypeComparer` must implement this equivalence for correct type checking.

**Special rules for `IsAssignableTo` involving `iterable`:**

1. **`array` is assignable to `iterable`** — `IsAssignableTo(array, iterable)` returns `true`
2. **Any type implementing `\Traversable` is assignable to `iterable`** — if `source` implements `\Traversable` (checked via `IsSubtypeOf`), then `IsAssignableTo(source, iterable)` returns `true`
3. **`iterable` is assignable to `array|\Traversable`** — `IsAssignableTo(iterable, UnionType(array, Traversable))` returns `true`
4. **`iterable` is NOT assignable to `array` alone** — `IsAssignableTo(iterable, array)` returns `false` (iterable could be a Traversable)
5. **`iterable` is NOT assignable to `\Traversable` alone** — `IsAssignableTo(iterable, Traversable)` returns `false` (iterable could be an array)

**Generic `iterable` equivalence:**

When `iterable` has generic type arguments:
- `iterable<V>` is equivalent to `array<V>|\Traversable<mixed, V>` for type checking purposes
- `iterable<K, V>` is equivalent to `array<K, V>|\Traversable<K, V>`
- When checking `IsAssignableTo(array<K, V>, iterable<K, V>)`, return `true` (with generic argument compatibility checks)
- When checking `IsAssignableTo(SomeTraversable<K, V>, iterable<K, V>)` where `SomeTraversable` implements `\Traversable<K, V>`, return `true`

**Note:** `\Traversable` is defined in the PHP extension Composer package (`tyhp/php-{phpVersion}`). If the package is not installed, `iterable` still works for `array` but the `\Traversable` side of the equivalence cannot be checked. The checker should gracefully handle the case where `\Traversable` is not defined — it should still accept `array` as `iterable` and emit a warning if `iterable` is used but `\Traversable` is not available.

**Implementation approach:** Add these rules to `IsAssignableTo()` as special cases checked BEFORE the general subtype/compatibility rules (between rules 6 and 7 in the existing list). The `iterable` type name is hardcoded in the checker as a known built-in type, similar to how `mixed`, `void`, and `never` are handled.

#### 3.9 Implement `callable` Type Assignability

In PHP, `callable` is a pseudo-type that accepts multiple structurally different values. The `TypeComparer` must understand which types satisfy `callable` for correct type checking.

**Types assignable to `callable`:**

1. **`\Closure`** — always assignable to `callable`. `\Closure` is the primary callable type in both PHP and Tyhp.
2. **`CallableCheckedType` (typed callable)** — a `callable<A, B, R>` is assignable to untyped `callable`. A more specific callable is always assignable to a less specific one.
3. **Objects with `__invoke()` method** — any object type whose class declares a public `__invoke()` method is assignable to `callable`. The checker must look up the class declaration via the binder and check for `__invoke()`.
4. **`callable` to `callable`** — untyped callable is assignable to untyped callable (trivial).

**Types NOT assignable to `callable` in Tyhp:**

Unlike PHP, Tyhp does NOT accept the following as `callable` at the type level (these are PHP legacy callable forms that bypass type safety):
- `string` — PHP allows function name strings (e.g., `'strlen'`), but Tyhp requires first-class callable syntax (`strlen(...)` which produces a `\Closure`)
- `array` — PHP allows `[$object, 'method']` and `['ClassName', 'method']` arrays, but Tyhp does not accept arrays as callable types

This restriction is intentional: Tyhp's type system requires that callable values have statically-known signatures. String and array callables cannot be type-checked at compile time.

**`callable` to other types:**

- `callable` is assignable to `mixed` (rule 3)
- `callable` is NOT assignable to `\Closure` (callable is wider — it includes `__invoke()` objects)
- `callable` is NOT assignable to `object` (callable is a separate type category)
- A typed `callable<A, B, R>` is assignable to a less specific `callable<A, mixed>` (covariance on return type, contravariance on parameter types — handled by rule 16)

**Generic `callable` assignability:**

When checking `IsAssignableTo(source, callable<...>)` where the target is a typed callable:
- If `source` is `\Closure<...>`, check parameter/return type compatibility using the same callable compatibility rules (rule 16)
- If `source` is an object with `__invoke()`, extract the `__invoke()` method's parameter and return types and check compatibility against the target callable's generic type arguments

**Implementation approach:** Add callable assignability as rule 15 in `IsAssignableTo()`, checked AFTER struct rules and BEFORE callable-to-callable compatibility. The checker should have a helper method `IsCallableType(ICheckedType type)` that checks whether a type satisfies `callable`.

#### 3.10 Implement `mixed`, `object`, and `bool` Type Equivalences

Several PHP built-in types are semantically equivalent to unions or act as supertypes of specific type families. The `TypeComparer` must handle these equivalences.

**`mixed` type — equivalent to `object|resource|array|string|float|int|bool|null`:**

Per [PHP documentation](https://www.php.net/manual/en/language.types.mixed.php), `mixed` is the top type — every other type is a subtype of it. In the `TypeComparer`:

- `IsAssignableTo(anything, mixed)` returns `true` (already rule 3)
- `IsAssignableTo(mixed, T)` returns `false` for any specific type T (mixed could be anything; the developer must narrow first)
- `mixed` in a union is absorbed: `A|mixed` = `mixed`
- `mixed` in an intersection is eliminated: `A&mixed` = `A`
- When a variable has type `mixed`, accessing properties, calling methods, or performing operations requires narrowing via type guards, `instanceof`, or type assertion. Without narrowing, only operations valid for ALL types are allowed (essentially none beyond assignment and comparison).

**`object` type — supertype of all class/interface instances:**

- `IsAssignableTo(AnyClass, object)` returns `true` — every class, interface implementation, trait user, and enum is a subtype of `object`
- `IsAssignableTo(\Closure, object)` returns `true` — Closure is a class
- `IsAssignableTo(object, AnyClass)` returns `false` — `object` could be any class; must narrow first
- `IsAssignableTo(object, callable)` returns `false` — not all objects are callable
- `object` does NOT include scalar types (`int`, `string`, `float`, `bool`), `array`, `resource`, or `null`

**`bool` type — equivalent to `true|false`:**

- `true` and `false` are literal types that are subtypes of `bool`
- `IsAssignableTo(true, bool)` returns `true`
- `IsAssignableTo(false, bool)` returns `true`
- `IsAssignableTo(bool, true)` returns `false` (bool could be false)
- `true|false` in a union simplifies to `bool`
- In conditional narrowing: after `if ($x === true)`, `$x` narrows to `true`; in the else branch, `$x` narrows to `false` (if the original type was `bool`)

**Summary of PHP pseudo-type equivalences:**

| PHP Type | Equivalent Union / Semantics | Handled In |
|----------|------------------------------|------------|
| `iterable` | `array\|\Traversable` | §3.8 |
| `callable` | `\Closure \| object-with-__invoke()` | §3.9 |
| `mixed` | `object\|resource\|array\|string\|float\|int\|bool\|null` (top type) | §3.10, rule 3 |
| `object` | Supertype of all class instances | §3.10, §3.3 |
| `bool` | `true\|false` | §3.10 |
| `void` | Return-type-only; no value | §3.11 |
| `never` | Bottom type; subtype of everything | §3.11 |

#### 3.11 Implement `ResolveGenericType(ICheckedType generic, Dictionary<string, ICheckedType> typeArguments)`

Substitute generic type parameters with their concrete type arguments:

- Walk the `ICheckedType` tree
- When encountering a `SimpleCheckedType` that references a `GenericTypeParameterSymbol`, look up its name in `typeArguments` and substitute
- Recursively resolve type arguments in nested generics, unions, intersections, etc.
- If a type parameter has no substitution, leave it as-is (may be an open generic)

#### 3.12 Handle Special Type Relationships

**`void` type:**
- Only valid as a function return type
- Cannot be assigned to any variable
- Cannot appear in union or intersection types
- `void` function calls cannot be used as expressions (except in statement position)

**`never` type:**
- Subtype of everything
- No value satisfies it
- Functions returning `never` always throw or loop infinitely
- `never` in a union is eliminated: `A|never` = `A`
- `never` in an intersection dominates: `A&never` = `never`

**`mixed`, `object`, `bool` types:** See §3.10 for detailed equivalence rules.

**`null` literal type:**
- `null` is assignable to any nullable type

**`decimal` type:**
- Treated as a semi-scalar: supports arithmetic operations
- NOT a subtype of `int` or `float` (separate type)
- Operator overloads from `tyhp/decimal`'s package (discovered via `package.tyhp.json`) define valid operations

**`struct` types:**
- Structural typing: two structs with the same property shapes are compatible
- A struct is assignable to another struct if the source has all properties of the target with compatible types (width subtyping)

#### 3.13 Integrate into TyhpChecker

Add convenience methods to `TyhpChecker`:

- `bool IsAssignable(ICheckedType source, ICheckedType target)` → delegates to `TypeComparer.IsAssignableTo()`
- `void CheckAssignment(IBase2Ast node, ICheckedType source, ICheckedType target, string context)` — checks assignability and reports `MessageCode.CheckerTypeMismatch` diagnostic if not compatible
- `void CheckReturnType(IBase2Ast node, ICheckedType actual, ICheckedType expected)` — checks return type compatibility

### Acceptance Criteria

- [ ] `TypeComparer.IsAssignableTo(CheckedTypes.Int, CheckedTypes.Int)` returns `true`
- [ ] `TypeComparer.IsAssignableTo(CheckedTypes.Int, CheckedTypes.Float)` returns `true` (numeric widening)
- [ ] `TypeComparer.IsAssignableTo(CheckedTypes.String, CheckedTypes.Int)` returns `false`
- [ ] `TypeComparer.IsAssignableTo(CheckedTypes.Null, NullableCheckedType(CheckedTypes.String))` returns `true`
- [ ] `TypeComparer.IsAssignableTo(anyType, CheckedTypes.Mixed)` returns `true` for all types
- [ ] `TypeComparer.IsAssignableTo(CheckedTypes.Never, anyType)` returns `true` for all types
- [ ] Union types: `IsAssignableTo(Int, Union(Int, String))` returns `true`
- [ ] Union types: `IsAssignableTo(Union(Int, String), Int)` returns `false`
- [ ] Intersection types: `IsAssignableTo(Intersection(A, B), A)` returns `true`
- [ ] Subtype checking walks inheritance chains correctly
- [ ] Struct structural compatibility works (source has all target properties)
- [ ] Struct-to-array widening: `IsAssignableTo(struct{name:string,age:int}, array<string,string|int>)` returns `true`
- [ ] Struct-to-array widening: `IsAssignableTo(struct{name:string,age:int}, array<string,string>)` returns `false` (int doesn't fit)
- [ ] Struct-to-array widening: `IsAssignableTo(struct{...}, array<int,mixed>)` returns `false` (struct keys are strings)
- [ ] Array-to-struct: `IsAssignableTo(array<string,mixed>, SomeStruct)` returns `false` (cannot implicitly narrow)
- [ ] Generic type parameter substitution replaces parameters with concrete types
- [ ] `NarrowType` correctly narrows union types and nullable types
- [ ] `UnionTypes(A, B)` produces a flattened, deduplicated union
- [ ] `void` and `never` special rules are enforced
- [ ] `UnresolvedCheckedType` does not cause false compatibility failures
- [ ] All methods are static and pure (no side effects)
- [ ] `IsAssignableTo(array, iterable)` returns `true`
- [ ] `IsAssignableTo(Traversable_impl, iterable)` returns `true` for any type implementing `\Traversable`
- [ ] `IsAssignableTo(iterable, array)` returns `false`
- [ ] Generic iterable: `IsAssignableTo(array<int, string>, iterable<int, string>)` returns `true`
- [ ] `iterable` type equivalence gracefully handles missing `\Traversable` definition
- [ ] `IsAssignableTo(\Closure, callable)` returns `true`
- [ ] `IsAssignableTo(ObjectWithInvoke, callable)` returns `true` for objects with `__invoke()` method
- [ ] `IsAssignableTo(string, callable)` returns `false` (Tyhp does not accept string callables)
- [ ] `IsAssignableTo(array, callable)` returns `false` (Tyhp does not accept array callables)
- [ ] `IsAssignableTo(callable<string, int>, callable)` returns `true` (typed callable assignable to untyped)
- [ ] `IsAssignableTo(mixed, string)` returns `false` (mixed must be narrowed before use)
- [ ] `IsAssignableTo(AnyClass, object)` returns `true`
- [ ] `IsAssignableTo(object, AnyClass)` returns `false` (object must be narrowed)
- [ ] `IsAssignableTo(true, bool)` returns `true`
- [ ] `IsAssignableTo(bool, true)` returns `false`
- [ ] `true|false` union simplifies to `bool`
- [ ] File stays under 500 lines. When the file exceeds 500 lines, split it into `TypeComparer.cs` and `TypeComparer.Generics.cs`.

### Dependencies

- **Requires:** Phase 1 (`ICheckedType` hierarchy, `CheckedType` concrete classes), Story 02 (`SymbolTree.ResolveType()`, `ObjectDeclarationSymbol` inheritance chain)
- **Provides:** The core type compatibility engine used by every check method in Phases 4-6

---

## Phase 4: Tier 1 — Core PHP Type Checking (Declarations, Statements, Expressions)




### Phase Overview

Implement the first tier of type checks covering standard PHP type system validation: variable assignment type compatibility, function/method return types, argument type matching, class member visibility, abstract/final/readonly validation, interface implementation, enum validation, and control flow statement checks. This is the largest single phase and produces a checker that validates most standard PHP-equivalent code.

### Deliverables

- `Tyhp/TyhpLang/Checker/Rules/DeclarationRule.cs` — Class, interface, trait, enum, function, property, constant declaration checks
- `Tyhp/TyhpLang/Checker/Rules/ControlFlowRule.cs` — Statement-level checks (if, for, foreach, while, switch, try/catch, return, throw, break, continue)
- `Tyhp/TyhpLang/Checker/Rules/TypeCompatibilityRule.cs` — Expression-level checks (assignments, binary/unary ops, function calls, member access, new, instanceof)
- `Tyhp/TyhpLang/Checker/Rules/TypeDeclarationValidationRule.cs` — PHP type declaration rules (redundant types, forbidden combinations, callable-as-property, resource, void/never restrictions)
- `Tyhp/TyhpLang/Checker/Rules/ReferenceTrackingRule.cs` — Pass-by-reference parameter tracking, reference variable type propagation
- `Tyhp/TyhpLang/Checker/Rules/RelativeTypeRule.cs` — `self`, `parent`, `static` resolution and validation
- `Tyhp/TyhpLang/Checker/Rules/ClosureRule.cs` — Closure/arrow function `use` validation, static closure restrictions
- Updated `TyhpChecker.cs` — Rule registry wiring and dispatch

### Implementation Details

#### 4.1 Declaration Checks (`Rules/DeclarationRule.cs`)

**Namespace declarations:**
- Validate that namespace content is allowed (classes, functions, constants, interfaces, traits, enums, sub-namespaces)
- Create child `CheckerState` via `Split(ScopeType.Namespace)` or `Split(ScopeType.NamespaceBlock)`
- Recurse into namespace body

**Class declarations — `CheckClassDeclaration(classAst, state)`:**
- Validate modifiers:
  - Only one visibility modifier allowed (use existing `MessageCode.CheckerMultipleVisibilities` = 4002)
  - `abstract` and `final` cannot coexist on the same class (`MessageCode.CheckerMemberModifierConflict` = 4005)
  - `static` is not allowed on class declarations (`MessageCode.CheckerNotAllowedMemberModifier` = 4003)
- Create child `CheckerState` via `Split(ScopeType.ObjectTypeDeclaration)`:
  - Set `EnclosingObject` to the class's `ObjectDeclarationSymbol`
  - Set `ObjectGenerics` from the class's generic parameters
- Check `extends`:
  - Parent class must exist (binder already checked this — skip if unresolved)
  - Parent class must not be `final` → `MessageCode.CheckerFinalClassExtended` (4019)
  - No circular inheritance (the binder checks this; the checker skips the check if the binder already reported it)
- Check `implements`:
  - All interface methods must be implemented → `MessageCode.CheckerInterfaceMethodNotImplemented` (4018)
  - Implementation signatures must be compatible (parameter types contravariant, return types covariant)
- Check abstract methods:
  - If class is not abstract, all inherited abstract methods must be implemented → `MessageCode.CheckerAbstractMethodNotImplemented` (4017)
- Recurse into class body (methods, properties, constants)

**Interface declarations:**
- All methods are implicitly abstract and public
- Interfaces cannot have non-public methods → report error
- Interfaces can have constants
- Check `extends` (interfaces can extend multiple interfaces) — no circular inheritance

**Trait declarations:**
- Validate trait members (methods, properties)
- Trait `extends` requirements (Tyhp extension) → store for checking at use site (// PLACEHOLDER_PHASE_6: trait requirement checks)
- Trait `implements` requirements (Tyhp extension) → store for checking at use site

**Enum declarations:**
- Backed enums: validate that all case values match the backed type (`int` or `string`) → `MessageCode.CheckerEnumCaseTypeMismatch` (4023)
- Enums cannot have constructors → `MessageCode.CheckerEnumMethodNotAllowed` (4024)
- Enums cannot have mutable properties
- Enums can implement interfaces — check implementation
- Enum cases are constants — validate uniqueness (binder handles this)

**Function declarations — `CheckFunctionDeclaration(funcAst, state)`:**
- Create child state via `Split(ScopeType.FunctionDeclaration)`:
  - Set `EnclosingFunction` to the function's `FunctionDeclarationSymbol`
  - Set `FunctionGenerics` from function generic parameters
  - Set `ExpectedReturnType` from declared return type
  - Register parameters as variables via `state.DeclareVariable()` (with `IsParameter = true`, `IsDefinitelyAssigned = true`)
- Validate parameter types: each parameter must have a type annotation (in Tyhp mode) or be inferable
- Validate default parameter values: default value type must be assignable to parameter type
- Validate return type:
  - After checking the body, verify `HasReturnedOnAllPaths` if return type is not `void` → `MessageCode.CheckerMissingReturnStatement` (4011)
- Recurse into function body

**Method declarations:**
- Same as function declarations plus:
  - Validate modifier combinations (`abstract` methods cannot have a body, `final` methods cannot be overridden)
  - `abstract` and `final` cannot coexist on same method
  - `abstract` and `private` cannot coexist
  - If overriding a parent method: check signature compatibility (parameter types contravariant, return type covariant)
  - If overriding a `final` method → `MessageCode.CheckerFinalMethodOverridden` (4020)
  - Instance methods: add `$this` as an implicit variable with the enclosing class type
  - Static methods: `$this` is not available

**Property declarations:**
- Validate type annotation exists (required in Tyhp)
- Validate default value type matches declared type
- `readonly` properties: track for reassignment checking (set `IsReadonly` on variable state)
- Property accessors: validate accessor type compatibility → existing codes 4004, 4006, 4007

**Constant declarations:**
- Validate value is a compile-time constant expression
- Validate value type matches declared type (if type is declared)

#### 4.2 Statement Checks (`Rules/ControlFlowRule.cs`)

**If/elseif/else statements — `CheckIfStatement(ifAst, state)`:**
- Check condition type is `bool` → `MessageCode.CheckerConditionNotBool` (4043) (Tyhp requires explicit boolean conditions, unlike PHP's truthy/falsy)
- `CheckerState.SnapShot()` before the if-branch
- Check the if-body with `Split(ScopeType.CodeBlock)`
- For type narrowing: if condition is `$x instanceof Foo`, narrow `$x` to `Foo` in the if-branch, narrow to "not Foo" in the else-branch
- For nullable narrowing: if condition is `$x !== null`, narrow to non-null in if-branch, to `null` in else-branch
- After all branches: `Merge()` the branch states
- `HasReturnedOnAllPaths` = true only if ALL branches return (including an else branch)

**For/while/do-while statements:**
- Check condition type is `bool` (4043)
- Set `IsInLoopContext = true`, increment `LoopDepth` in child state
- Check loop body with `Split(ScopeType.CodeBlock)`
- Variables assigned inside the loop body may be "possibly undefined" outside (unless assigned before the loop)

**Foreach statement:**
- Infer the iterable expression type
- **Synchronous iteration:** The iterable must be `array`, `iterable`, or implement `\Traversable`
  - If the iterable has generic type arguments, infer the key and value types
  - Declare the key variable (if present) with the inferred key type
  - Declare the value variable with the inferred value type
- **Async iteration (`foreach (await $expr as $item)`):** When the foreach expression is an `await` expression:
  - Must be inside an async function context (`IsInAsyncContext = true`), otherwise report `CheckerAwaitOutsideAsync` (4028)
  - If the awaited expression is `AsyncIterable<T>` (from `tyhp/async`'s package (discovered via `package.tyhp.json`)): this is async iteration. The loop variable type is `T`. For key-value async iteration with `AsyncKeyValueIterator<TKey, TValue>`, the key type is `TKey` and value type is `TValue`.
  - If the awaited expression is `Promise<Iterable<T>>`: resolve the Promise to `Iterable<T>`, then iterate synchronously. The loop variable type is `T`. This is resolve-then-iterate, not true async iteration.
  - If the awaited expression is neither `AsyncIterable<T>` nor `Promise<Iterable<T>>`, report a type error.
- **Missing `await` on `AsyncIterable<T>`:** If the foreach expression (without `await`) has type `AsyncIterable<T>`, report an error: "Cannot iterate `AsyncIterable<T>` without `await`; use `foreach (await $expr as ...)`"
- Set loop context flags
- Check body

**Switch/match statements:**
- Check the switch expression type
- For each case: check case value type is compatible with switch expression type
- For match expressions: check exhaustiveness (all possible values are covered, or there is a default arm)
- Track `HasReturnedOnAllPaths` correctly through fallthrough cases
- Set `IsInSwitchContext = true`

**Try/catch/finally statements:**
- Check try body with `Split(ScopeType.CodeBlock)`
- For each catch clause:
  - Validate caught type implements `\Throwable` → `MessageCode.CheckerCatchNotThrowable` (4040)
  - Cannot catch with intersection types → `MessageCode.CheckerCatchNoIntersection` (4041)
  - Cannot catch scalar/struct/enum types → `MessageCode.CheckerCatchNoScalar` (4042)
  - Declare the catch variable with the caught type (or union of caught types)
  - Check catch body
- Check finally body (if present)
- Set `IsInsideFinally = true` in finally state (to restrict control flow)
- Merge all branch states (try-completed + catch branches)

**Return statement:**
- Infer the return expression type (or `void` if no expression)
- Check compatibility with `CheckerState.ExpectedReturnType` → `MessageCode.CheckerIncompatibleReturnType` (4009)
- Set `HasReturnedOnAllPaths = true` on current state
- Code after `return` is unreachable → `MessageCode.CheckerUnreachableCode` (4012) (as a warning)

**Throw statement:**
- Infer the thrown expression type
- Must implement `\Throwable` → `MessageCode.CheckerThrowNotThrowable` (4039)
- Set `HasReturnedOnAllPaths = true` (throw always exits)
- Code after `throw` is unreachable

**Break/continue statements:**
- `break` requires `IsInLoopContext || IsInSwitchContext` → `MessageCode.CheckerBreakOutsideLoop` (4026)
- `continue` requires `IsInLoopContext` → `MessageCode.CheckerContinueOutsideLoop` (4027)
- If `break N` / `continue N`, validate `N <= LoopDepth`
- Set `HasReturnedOnAllPaths = true` for the current code block (control exits the block)

**Echo/print statements:**
- Arguments to `echo`/`print` must be scalar types or objects implementing `\Stringable`/`__toString()`. Non-stringable arguments produce `MessageCode.CheckerConcatNonStringable` (4120) as an error.

**Global statement (`global $var`):**
- Import the variable from `GlobalScope` into the current function scope
- The variable's type is the type from the global scope. If the global variable has no declared type, it is typed as `mixed`.

**Static variable statement (`static $var = value`):**
- Declare a persistent variable in the function scope
- The variable persists across function calls (runtime behavior; the checker just validates the type)

#### 4.3 Expression Checks (`Rules/TypeCompatibilityRule.cs`)

**Assignment expressions — `CheckAssignment(assignAst, state)`:**
- Infer the right-hand side type
- If the left-hand side has a declared type, check assignability → `MessageCode.CheckerTypeMismatch` (4008)
- If the left-hand side is a new variable (first assignment), declare it with the inferred type
- If the left-hand side is a readonly property being reassigned outside the constructor → `MessageCode.CheckerReadonlyPropertyReassigned` (4021)
- Update variable state: `IsDefinitelyAssigned = true`, update narrowed type
- Handle compound assignments (`+=`, `-=`, etc.): check that the compound operation is valid for the types

**Function call expressions — `CheckFunctionCall(callAst, state)`:**
- Resolve the function symbol
- Check argument count matches parameter count (accounting for optional/variadic parameters)
- For each argument: check type compatibility with the corresponding parameter type → `MessageCode.CheckerIncompatibleArgumentType` (4010)
- If the function has generic type parameters, infer the type arguments from the call-site argument types
- Return the function's declared return type

**Method call expressions — `CheckMethodCall(callAst, state)`:**
- Infer the receiver object type
- Resolve the method symbol on the receiver type (using binder's `ResolveMember`)
- Check visibility: if the method is `private`/`protected`, ensure the call is from an appropriate context → `MessageCode.CheckerMemberNotAccessible` (4025)
- Check arguments same as function calls
- Return the method's return type

**Static method call expressions:**
- Resolve the class and static method
- Check visibility
- Check arguments
- Return type

**Property access expressions:**
- Infer the object type
- Resolve the property symbol
- Check visibility → `MessageCode.CheckerMemberNotAccessible` (4025)
- Return the property's declared type

**New expressions — `CheckNewExpression(newAst, state)`:**
- Resolve the class type
- Class must not be abstract → `MessageCode.CheckerAbstractClassInstantiated` (4022)
- Class must not be an interface or trait
- Resolve the constructor
- Check constructor arguments (same as function call)
- Return the class type

**Instanceof expressions:**
- Left side: any expression
- Right side: must be a class/interface name
- Result type: `bool`
- Side effect: enables type narrowing in enclosing if-condition

**Cast expressions:**
- Validate the cast is allowed (PHP allows `(int)`, `(float)`, `(string)`, `(bool)`, `(array)`, `(object)`)
- Result type: the cast target type
- Lossy casts produce `MessageCode.CheckerLossyCast` (4209) as a warning. Lossy casts are: `(int)` on `float`, `(int)` on `string` with non-numeric content, `(float)` on `string` with non-numeric content.

**Binary operator expressions:**
- Infer types of both operands
- Validate the operator is applicable to the operand types → `MessageCode.CheckerInvalidOperatorForType` (4029)
- For overloaded operators: check if the left operand's type has an operator overload for this operator
- Return the result type from the operator type table (Phase 2, section 2.3)

**Unary operator expressions:**
- Infer operand type
- Validate operator applicability
- Return result type (`!` → `bool`, `-` → numeric type, `++`/`--` → same numeric type)

**Ternary expressions (`$a ? $b : $c`):**
- Check condition is `bool` (4043)
- Infer types of both branches
- Result type: `UnionTypes(trueType, falseType)`

**Null coalescing (`$a ?? $b`):**
- Infer type of `$a`: must be nullable
- Result type: non-null part of `$a`'s type, unioned with type of `$b`

**Match expression:**
- Check subject type
- For each arm: check condition values are compatible with subject type
- Result type: union of all arm return types
- Check exhaustiveness (all possible values covered, or default arm present)

**Array creation (`[1, 2, 3]` or `['key' => 'value']`):**
- Infer element types from all elements
- If all elements have the same type: `array<int, T>` or `array<string, T>`
- If mixed types: `array<string|int, T1|T2|...>`

**Spread operator (`...$array`):**
- Validate operand is iterable
- Type becomes the element type of the iterable

#### 4.4 Type Declaration Validation (`Rules/TypeDeclarationValidationRule.cs`)

PHP enforces several compile-time rules on type declarations that must be replicated by the checker. These are checks on the type *annotation itself*, not on the value assigned to it.

**Redundant and duplicate types in unions/intersections:**
- Each name-resolved type may only appear once in a union or intersection. `int|string|INT` and `Countable&Traversable&COUNTABLE` are errors → `MessageCode.CheckerDuplicateTypeInComposite` (4053)
- Using `mixed` or `never` in a union or intersection is an error (these are already the widest/narrowest types) → `MessageCode.CheckerMixedInComposite` (4054)

**Forbidden union combinations:**
- If `bool` is used in a union, `false` and `true` cannot also appear → `MessageCode.CheckerRedundantTypeInUnion` (4055). E.g., `bool|false` is redundant.
- If `object` is used in a union, class types cannot also appear → `CheckerRedundantTypeInUnion` (4055). E.g., `object|MyClass` is redundant.
- If `iterable` is used in a union, `array` and `\Traversable` cannot also appear → `CheckerRedundantTypeInUnion` (4055). E.g., `iterable|array` is redundant.
- `true|false` as a union is not allowed — use `bool` instead → `MessageCode.CheckerUseBoolInsteadOfTrueFalse` (4056)

**Forbidden intersection combinations:**
- Only class/interface types may appear in intersections. Scalar types (`int`, `string`, `bool`, `float`), `array`, `callable`, `null`, `void`, `never`, `mixed` cannot → `MessageCode.CheckerNonClassInIntersection` (4057)
- `self`, `parent`, `static` cannot appear in intersection types → `CheckerNonClassInIntersection` (4057)

**DNF type validation (Disjunctive Normal Form — PHP 8.2+):**
- A union where some members are intersection types. E.g., `A|(B&C)`.
- If a more generic type is used, the more restrictive intersection is redundant.
- Two identical intersection types in a DNF are an error.

**Callable as property type:**
- `callable` cannot be used as a class/interface/enum property type declaration → `MessageCode.CheckerCallableNotAllowedOnProperty` (4058). This is a PHP restriction.

**`void` restrictions:**
- `void` is only valid as a return type, not as a parameter type, property type, or variable type → `MessageCode.CheckerVoidNotAllowedHere` (4059)
- Returning by reference from a `void` function is a deprecated pattern → `MessageCode.CheckerVoidRefReturn` (4060) (warning)

**`never` restrictions:**
- `never` is only valid as a return type → `MessageCode.CheckerNeverNotAllowedHere` (4061)
- A function with return type `never` must not contain any `return` statement (not even `return;`) — it must always throw or loop infinitely.

**`resource` type:**
- `resource` cannot be used in user-land type declarations (PHP does not allow it). If a user writes `resource` as a type hint, report → `MessageCode.CheckerResourceNotAllowed` (4062)
- Internally, the checker may encounter `resource` types from PHP extension tyhpdefs (e.g., `fopen()` returns `resource|false`). These are valid as inferred types for type compatibility checks, but users cannot declare parameters, returns, or properties as `resource`.

**Nullable `void` and `never`:**
- `?void` and `?never` are errors → `MessageCode.CheckerVoidNotAllowedHere` / `CheckerNeverNotAllowedHere`
- `void|null` and `never|null` are also errors.

#### 4.5 Reference Tracking (`Rules/ReferenceTrackingRule.cs`)

PHP allows pass-by-reference parameters and reference assignments. The checker must track these because they affect type safety in ways that are invisible at the call site.

**Pass-by-reference parameters (`function foo(int &$x)`):**
- When a function declares a by-reference parameter, the parameter's `VariableState.IsReference` is set to `true`.
- The type of the parameter is checked at the call site: the caller must pass a variable (not a literal or expression), and the variable's type must match the parameter type → `MessageCode.CheckerIncompatibleArgumentType` (4010).
- Inside the function body, if the reference parameter is reassigned to a different type (e.g., `$x = "hello"` when `$x` was declared as `int &$x`), the checker emits `MessageCode.CheckerReferenceTypeChanged` (4052) as a warning. This is because the caller's variable will also change to that type, which may cause a `TypeError` on next use at the caller.
- When the function returns, the caller must treat the by-reference variable's type as potentially changed. If the checker can determine the exact type change (e.g., the function always assigns an `int` to the reference), it uses that. Otherwise, the caller's variable type widens to `mixed`.

**Reference assignments (`$a = &$b`):**
- Both `$a` and `$b` are placed in the same `ReferenceGroup`.
- Any subsequent assignment to `$a` also changes `$b`'s type, and vice versa.
- The checker tracks this through `ReferenceGroup.PropagateTypeChange()`.

**Calling functions with by-reference parameters:**
- The argument must be a variable (or array access, or property access), NOT a literal or temporary expression → `MessageCode.CheckerRefArgMustBeVariable` (4063)
- The variable's type must be assignable to the parameter's declared type.

**Reference and type narrowing interaction:**
- Type narrowing on a reference variable is fragile — any modification through another alias in the reference group invalidates the narrowing. The checker resets narrowing on all members of a `ReferenceGroup` when any member is assigned.

#### 4.6 Relative Type Resolution (`Rules/RelativeTypeRule.cs`)

PHP has three relative class types: `self`, `parent`, and `static`. These require special resolution in the checker.

**`self` resolution:**
- `self` refers to the class in which the type declaration textually appears (not the runtime class).
- Only valid inside class/interface/trait/enum declarations → `MessageCode.CheckerRelativeTypeOutsideClass` (4064)
- Resolved from `CheckerState.EnclosingObject`.
- In traits, `self` resolves to the composing class at check time. If the trait is being checked in isolation (not yet composed into a class), `self` resolves to `CheckedTypes.Unknown` and no diagnostic is emitted — the check will be performed when the trait is used.

**`parent` resolution:**
- `parent` refers to the parent class of the class in which the type declaration appears.
- Only valid inside classes that have a parent → `MessageCode.CheckerParentWithoutParent` (4065)
- Resolved from `CheckerState.EnclosingObject.ParentClass`.
- Using `parent` in a class that does not extend anything is an error.

**`static` resolution:**
- `static` is a return-only type. It requires that the returned value is an instance of the class the method is called on (late static binding, PHP 8.0+).
- Only valid as a return type → `MessageCode.CheckerStaticNotReturnType` (4066)
- Cannot be used as a parameter type or property type.
- In the checker, `static` in a return type means: the returned value must be `self` or a subclass of `self`. When checking `return new self()` vs return type `static`, this is valid. If a method with return type `static` returns `new self()`, this is valid only if the class is `final`. For non-final classes, returning `new self()` from a method declaring return type `static` produces `MessageCode.CheckerStaticReturnSelfInNonFinal` (4211) as a warning, because a child class calling this method would receive an instance of the parent, not the child.
- For type compatibility: `static` is a subtype of `self` (any `static` satisfies a `self` return type), but `self` is NOT a subtype of `static` (a `self` return does not satisfy a `static` return type, because the caller might be a subclass).

**`self`/`parent`/`static` in intersection types:**
- Not allowed → `CheckerNonClassInIntersection` (4057)

#### 4.7 Instantiation Validation (within `Rules/TypeCompatibilityRule.cs`)

`new` expressions require additional checks beyond abstract class validation:

**Cannot instantiate non-class types:**
- `new string()`, `new int()`, `new float()`, `new bool()`, `new array()` → `MessageCode.CheckerCannotInstantiateNonClass` (4069). These are scalar/built-in types, not classes.
- `new callable()`, `new iterable()`, `new mixed()`, `new void()`, `new never()`, `new null()`, `new true()`, `new false()`, `new object()` → same error (4069).
- `new resource()` → same error (4069), plus `CheckerResourceNotAllowed` (4062) for the type itself.

**Cannot instantiate traits:**
- `new MyTrait()` → `MessageCode.CheckerCannotInstantiateTrait` (4070)

**Cannot instantiate interfaces (already implied but make explicit):**
- `new MyInterface()` → `MessageCode.CheckerCannotInstantiateInterface` (4071)

**Cannot instantiate enums directly:**
- `new MyEnum()` → `MessageCode.CheckerCannotInstantiateEnum` (4072). Enum values are created through case references (`MyEnum::CaseA`).

**`clone` validation:**
- `clone` on a non-object type (scalar, array, struct, enum, null) → `MessageCode.CheckerCloneNonObject` (4073)
- `clone` on `mixed` is an error → `MessageCode.CheckerCloneNonObject` (4073). The developer must narrow the type first using `is_object()` or `instanceof` before using `clone`.

#### 4.8 Magic Method Signature Validation (within `Rules/DeclarationRule.cs`)

PHP magic methods have strict signature requirements. The checker validates these because PHP silently misbehaves or throws runtime errors when signatures are wrong.

| Magic Method | Required Signature | Error Code |
|---|---|---|
| `__construct()` | No return type declaration allowed | `CheckerMagicMethodSignature` (4074) |
| `__destruct()` | No parameters, no return type | 4074 |
| `__clone()` | No parameters, return `void` | 4074 |
| `__toString()` | No parameters, must return `string` | 4074 |
| `__debugInfo()` | No parameters, must return `array` | 4074 |
| `__get(string $name)` | Exactly 1 `string` param | 4074 |
| `__set(string $name, mixed $value)` | Exactly 2 params, first is `string` | 4074 |
| `__isset(string $name)` | Exactly 1 `string` param, must return `bool` | 4074 |
| `__unset(string $name)` | Exactly 1 `string` param, return `void` | 4074 |
| `__call(string $name, array $args)` | Exactly 2 params | 4074 |
| `__callStatic(string $name, array $args)` | Exactly 2 params, must be `static` | 4074 |
| `__invoke(...)` | Variable params allowed, must have return type | 4074 |
| `__sleep()` | No params, must return `array` | 4074 |
| `__wakeup()` | No params, return `void` | 4074 |
| `__serialize()` | No params, must return `array` | 4074 |
| `__unserialize(array $data)` | Exactly 1 `array` param, return `void` | 4074 |
| `__set_state(array $props)` | Must be `static`, 1 `array` param, returns `static` | 4074 |

**Additional magic method checks:**
- Magic methods cannot be `static` (except `__callStatic`, `__set_state`) → 4074
- Magic methods should not be `private` in interfaces
- `__construct` cannot have a return type (not even `void`)
- `__toString` must return `string`, not `string|null` or any union containing non-string

#### 4.9 Parameter Declaration Validation (within `Rules/DeclarationRule.cs`)

**Duplicate parameter names:**
- Two parameters with the same name in a function/method → `MessageCode.CheckerDuplicateParameter` (4075)
- Example: `function foo(int $x, string $x)` — error

**Required parameter after optional:**
- A required parameter (no default value) after an optional parameter (has default) → `MessageCode.CheckerRequiredAfterOptional` (4076) (warning, deprecated in PHP 8.0+)
- Exception: if the required parameter is variadic, it IS allowed as the last parameter.

**Variadic parameter position:**
- Variadic parameter (`...$args`) must be the last parameter → `MessageCode.CheckerVariadicNotLast` (4077)
- Variadic parameter cannot have a default value → `MessageCode.CheckerVariadicWithDefault` (4078)

**Parameter count limits:**
- PHP has no hard limit, but an unreasonable number (e.g., > 255) may indicate a design issue — informational only.

#### 4.10 Named Argument Validation (within `Rules/TypeCompatibilityRule.cs`)

**Duplicate named argument:**
- Calling `foo(x: 1, x: 2)` → `MessageCode.CheckerDuplicateNamedArgument` (4079)

**Positional argument after named:**
- `foo(x: 1, 2)` — positional argument after named argument → `MessageCode.CheckerPositionalAfterNamed` (4080)

**Named argument for non-existent parameter:**
- `foo(nonExistent: 1)` where `foo` has no parameter named `nonExistent` → `MessageCode.CheckerUnknownNamedArgument` (4081)

**Named argument after unpacking:**
- `foo(...$args, x: 1)` — named argument after argument unpacking → `MessageCode.CheckerNamedAfterUnpack` (4082)

**Named argument with variadic:**
- Named arguments can be collected into variadic parameters — validate the types match.

#### 4.11 Closure and Arrow Function Validation (`Rules/ClosureRule.cs`)

**New rule class.**

**Closure `use` variable validation:**
- Variables in the `use(...)` clause must exist in the enclosing scope → `MessageCode.CheckerClosureUseUndefined` (4083)
- `use($this)` is redundant in non-static closures (it's captured automatically) → `MessageCode.CheckerClosureUseThis` (4084) (warning)

**Static closure restrictions:**
- Static closures (`static function() { ... }` or `static fn() => ...`) cannot reference `$this` → `MessageCode.CheckerStaticClosureThis` (4085)
- Static closures cannot capture `$this` through `use(...)` → same error (4085)

**Arrow function capture:**
- Arrow functions (`fn($x) => $expr`) implicitly capture variables from the outer scope. The checker must verify that all captured variables are defined in the enclosing scope.
- Arrow functions cannot modify outer scope variables (they capture by value, not reference).

**Closure by-reference capture:**
- `use(&$var)` — the variable gets `IsReference = true` in the closure's scope, linked to the outer variable via `ReferenceGroup`. Same reference tracking rules as §4.5 apply.

**Closure binding context:**
- `Closure::bind()` and `Closure::bindTo()` — if the checker can statically determine the binding, validate that `$this` usage within the closure is compatible with the bound class.

#### 4.12 Generator and Yield Validation (within `Rules/ControlFlowRule.cs`)

**Yield outside generator function:**
- `yield` can only appear inside a function body, not at the top level or inside a closure that is not itself a generator → `MessageCode.CheckerYieldOutsideGenerator` (4086)

**Generator return type:**
- A function containing `yield` is a generator. Its return type must be `Generator`, `\Generator<TKey, TValue, TSend, TReturn>`, `iterable`, or `\Iterator`/`\Traversable` → `MessageCode.CheckerGeneratorInvalidReturnType` (4087)

**Yield in finally:**
- `yield` inside a `finally` block is a fatal error in PHP → `MessageCode.CheckerYieldInFinally` (4088)

**`yield from` validation:**
- The expression after `yield from` must be iterable or a `Generator` → `MessageCode.CheckerYieldFromNonIterable` (4089)
- `yield from` cannot be used in a function that also has `yield` with send values (if the inner generator uses send, the outer must too).

**Generator with explicit return value:**
- PHP 7+ allows `return $value` in generators. The return type is reflected in `Generator<K, V, S, TReturn>` where `TReturn` matches the returned value type.

#### 4.13 Constant Expression Validation (within `Rules/DeclarationRule.cs`)

PHP requires certain values to be "constant expressions" (evaluable at compile time). The checker must validate this.

**Locations requiring constant expressions:**
- Class constant initializers (`const FOO = ...`)
- Property default values (`public int $x = ...`)
- Enum case values (`case A = ...`)
- Parameter default values (`function foo(int $x = ...)`)
- Attribute arguments (`#[MyAttr(...)]`)

**What is allowed in constant expressions:**
- Scalar literals (int, float, string, bool, null)
- Constant references (`PHP_INT_MAX`, `self::CONST`, `ClassName::CONST`)
- Enum case references (`MyEnum::CaseA`)
- Array literals with constant elements (`[1, 2, 3]`)
- Arithmetic on constants (`self::X + 1`)
- String concatenation of constants
- Ternary with constant operands
- `new ClassName(...)` with constant arguments (PHP 8.1+)

**What is NOT allowed in constant expressions:**
- Variable references (`$x`)
- Function/method calls (except `new` with constant args in PHP 8.1+)
- `yield`, `await`, `include`/`require`
- Closures/anonymous functions
- Property access on non-constant objects

Report → `MessageCode.CheckerNonConstantExpression` (4090) when a non-constant expression appears in a constant-required context.

**Division by zero in constant expressions:**
- `const X = 1 / 0` → `MessageCode.CheckerDivisionByZero` (4091) (error)

#### 4.14 Array and List Validation (within `Rules/TypeCompatibilityRule.cs`)

**Duplicate keys in array literal:**
- `[1 => 'a', 1 => 'b']` — duplicate key → `MessageCode.CheckerDuplicateArrayKey` (4092) (warning). The second value silently overwrites the first in PHP.

**Array access on invalid type:**
- `$x[0]` where `$x` is not `array`, `string`, or a type implementing `\ArrayAccess` → `MessageCode.CheckerInvalidArrayAccess` (4093)
- `$x[0]` on `null` → same error (or null safety check from NullSafetyRule)

**Array unpacking with string keys:**
- `[...$arr]` where `$arr` has string keys — PHP 8.1+ allows this, but earlier versions did not. The checker should validate based on the target PHP version.

**List/destructuring validation:**
- `[$a, $b] = $expr` — the right side must be an array or iterable → `MessageCode.CheckerDestructuringNonArray` (4094)
- Nested list: `[[$a, $b], $c] = $nested` — validate nesting structure matches
- `[...$rest] = $arr` — spread in list/destructuring is not allowed in PHP → `MessageCode.CheckerDestructuringSpread` (4095)
- Keyed destructuring: `['key' => $var] = $arr` — validate key exists in source type if statically known

**Spread operator validation:**
- `...$arg` in function call: the argument must be iterable → `MessageCode.CheckerSpreadNonIterable` (4096)
- `...$arg` in array literal: same validation
- Only one `...$arg` can appear in an argument list after named parameters

#### 4.15 Method Resolution and `$this` Validation (within `Rules/TypeCompatibilityRule.cs`)

**`$this` in static context:**
- Referencing `$this` inside a static method or static closure → `MessageCode.CheckerThisInStaticContext` (4097)

**Calling non-static method statically:**
- `ClassName::instanceMethod()` where `instanceMethod` is not static → `MessageCode.CheckerNonStaticCalledStatically` (4098)
- Exception: `parent::method()` is allowed even for non-static methods when called from an instance context.

**Calling static method on instance (warning):**
- `$obj->staticMethod()` — this works in PHP but is misleading → `MessageCode.CheckerStaticCalledOnInstance` (4099) (warning)

**`parent::` without parent:**
- Already covered by `CheckerParentWithoutParent` (4065), but also applies to `parent::method()` calls, not just type references.

**`static::` in non-class context:**
- `static::method()` outside a class body → `MessageCode.CheckerStaticOutsideClass` (4100)

#### 4.16 Goto Validation (within `Rules/ControlFlowRule.cs`)

**Tyhp prohibits `goto` entirely.** The `goto` statement and labels are not allowed in Tyhp code. If the parser encounters a `goto` statement or a label declaration, the checker reports `MessageCode.CheckerGotoProhibited` (4104) as an error. The other goto validation rules (4101-4103) are not needed since goto is banned outright.

#### 4.17 Constructor and Promoted Property Validation (within `Rules/DeclarationRule.cs`)

**Constructor return type:**
- `__construct(): int` — constructors cannot have a return type (not even `void`) → `CheckerMagicMethodSignature` (4074)

**Promoted properties (`public readonly string $name` in constructor):**
- Promoted property must have a type annotation → `MessageCode.CheckerPromotedPropertyNoType` (4105)
- Abstract constructors cannot have promoted properties → `MessageCode.CheckerPromotedPropertyInAbstract` (4106)
- Interface constructors (if they exist) cannot have promoted properties → same (4106)
- Promoted property visibility is inherited from the parameter modifier (must match property visibility rules)
- Promoted variadic parameter is not allowed → `MessageCode.CheckerPromotedVariadic` (4107)

**Readonly class validation:**
- If the class is declared `readonly`, all declared properties must be readonly. Non-readonly properties → `MessageCode.CheckerReadonlyClassMutableProperty` (4108)
- `readonly` classes cannot declare static properties that are not constants → `MessageCode.CheckerReadonlyClassStaticProperty` (4109)
- `readonly` properties cannot have a `set` accessor → `CheckerReadonlyPropertyReassigned` (4021)

#### 4.18 Enum Additional Validation (within `Rules/DeclarationRule.cs`)

Beyond what's in §4.1:

**Backed enum completeness:**
- Every case in a backed enum must have a value → `MessageCode.CheckerEnumCaseMissingValue` (4110)
- Non-backed enums must NOT have case values → `MessageCode.CheckerEnumCaseValueOnNonBacked` (4111)

**Enum case value uniqueness:**
- Two enum cases with the same value → `MessageCode.CheckerEnumCaseDuplicateValue` (4112)

**Enum property restrictions:**
- Enums cannot have instance properties (only constants) → `MessageCode.CheckerEnumPropertyNotAllowed` (4113)
- Enums cannot use traits that declare properties → same (4113)

**Enum and interfaces:**
- If an enum implements an interface requiring mutable state (writable property, setter), report incompatibility.

#### 4.19 Interface and Trait Additional Validation (within `Rules/DeclarationRule.cs`)

**Interface restrictions:**
- Interface properties cannot have initializers → `MessageCode.CheckerInterfacePropertyInitializer` (4114)
- Interface cannot have instance property declarations (PHP doesn't support interface properties at all) → `MessageCode.CheckerInterfacePropertyNotAllowed` (4115)
- Interface methods are implicitly `public abstract` — specifying `private` or `protected` is an error.
- Interface constants CAN exist and CAN be final (PHP 8.1+).

**Trait conflict resolution:**
- When a class uses multiple traits with the same method name, and no `insteadof`/`as` resolution is provided → `MessageCode.CheckerTraitConflict` (4116)
- `as` visibility change: the new visibility must not be more restrictive if the method is part of an interface contract.

**Circular trait use:**
- Trait A uses Trait B which uses Trait A → `MessageCode.CheckerCircularTraitUse` (4117)

#### 4.20 Comparison and Equality Validation (within `Rules/TypeCompatibilityRule.cs`)

**Loose comparison (`==`, `!=`) is allowed in Tyhp.** However, the checker does NOT perform type narrowing based on loose comparisons. Only strict comparisons (`===`, `!==`) produce type narrowing. This is because PHP's type coercion means `$x == "1"` is true when `$x` is `1` (int), so the type cannot be reliably narrowed. The type narrowing rules in §5.3 apply ONLY to `===` and `!==` comparisons, `instanceof`, and type guard functions.

**Incomparable types:**
- Comparing types that have no meaningful comparison (e.g., `$object < $array`) → `MessageCode.CheckerIncomparableTypes` (4119) (warning)

**Spaceship operator (`<=>`):**
- Both operands must be comparable (same type or numeric types) → `CheckerInvalidOperatorForType` (4029)

#### 4.21 String Operation Validation (within `Rules/TypeCompatibilityRule.cs`)

**String concatenation with non-stringable:**
- `$str . $object` where `$object`'s class does not implement `__toString()` or `\Stringable` → `MessageCode.CheckerConcatNonStringable` (4120) (error)
- `$str . $array` → same error (arrays cannot be concatenated as strings)
- `$str . null` → error. Tyhp is non-nullable by default; if `null` reaches a concat operation, it is a type error.

**String interpolation variable existence:**
- `"Hello $name"` — `$name` must be defined in scope → `CheckerVariableUsedBeforeAssignment` (4013)
- Complex interpolation `"Hello {$obj->prop}"` — validate `$obj` is defined and `prop` exists on its type.

**`echo`/`print` with non-stringable:**
- Arguments to `echo`/`print` must be scalar types or objects implementing `\Stringable`/`__toString()`. Non-stringable arguments produce `MessageCode.CheckerConcatNonStringable` (4120) as an error.

#### 4.22 Exception Handling Additional Checks (within `Rules/ControlFlowRule.cs`)

**Empty catch block:**
- A catch block with no statements → `MessageCode.CheckerEmptyCatch` (4121) (warning). This silently swallows exceptions.

**Finally block control flow:**
- `return` inside `finally` is allowed but overwrites the try/catch return value — emit `MessageCode.CheckerReturnInFinally` (4122) (warning)
- `break`/`continue` inside `finally` that exits the finally scope → `MessageCode.CheckerBreakInFinally` (4123) (warning)

**Multiple catch for same exception type:**
- Catching the same exception type in multiple catch clauses → `MessageCode.CheckerDuplicateCatch` (4124) (warning). The second catch is unreachable.

**Catch ordering:**
- Catching a parent exception type before a child type → `MessageCode.CheckerCatchOrderBroadFirst` (4125) (warning). E.g., catching `\Exception` before `\InvalidArgumentException` makes the second catch unreachable.

### Acceptance Criteria

- [ ] Class declarations validate: modifier combinations, final class extension, abstract method implementation, interface method implementation
- [ ] Function/method declarations validate: parameter types, default values, return types on all paths, modifier combinations
- [ ] Property declarations validate: type annotation presence, default value compatibility, readonly enforcement
- [ ] Enum declarations validate: backed type case values, no constructor, interface implementation
- [ ] If/else statements: condition must be bool, type narrowing through instanceof/null checks, correct state merging across branches
- [ ] Loop statements: condition must be bool, break/continue context validation, loop nesting depth
- [ ] Try/catch: caught types must implement `\Throwable`, no intersection in catch, correct state merging
- [ ] Return statements: type checked against expected return type, unreachable code detected
- [ ] Throw statements: thrown value must be `\Throwable`
- [ ] Assignment: type compatibility checked, readonly enforcement, variable state updated
- [ ] Function calls: argument count and types validated against parameter signatures
- [ ] Method calls: visibility checked, argument types validated
- [ ] New expressions: abstract class instantiation prevented, constructor args validated
- [ ] Binary/unary operators: type applicability validated, result type correctly inferred
- [ ] **Type declaration validation:** `int|string|INT` reports duplicate type error; `bool|false` reports redundant type; `object|MyClass` reports redundant type; `iterable|array` reports redundant type; `true|false` reports "use bool instead"; `mixed|int` reports error; `int&string` reports non-class in intersection
- [ ] **Callable as property:** `callable` type on a class property reports `CheckerCallableNotAllowedOnProperty`
- [ ] **Void/never restrictions:** `void` used as parameter/property type reports error; `never` used as parameter type reports error; `?void` and `?never` report error
- [ ] **Resource type:** `resource` in user type declaration reports `CheckerResourceNotAllowed`; `resource` from extension tyhpdefs is accepted as an inferred type
- [ ] **Reference tracking:** Pass-by-reference parameter creates `VariableState` with `IsReference = true`; reassigning reference parameter to different type emits warning; calling by-reference function with a literal argument reports error; reference group propagates type changes to all members
- [ ] **Relative types:** `self` outside a class reports `CheckerRelativeTypeOutsideClass`; `parent` in a class without a parent reports `CheckerParentWithoutParent`; `static` as a parameter type reports `CheckerStaticNotReturnType`; `self`/`parent`/`static` in intersection types reports error
- [ ] **Instantiation:** `new string()` reports `CheckerCannotInstantiateNonClass`; `new MyTrait()` reports error; `new MyInterface()` reports error; `new MyEnum()` reports error; `clone` on non-object reports `CheckerCloneNonObject`
- [ ] **Magic methods:** `__construct` with return type reports error; `__toString` not returning `string` reports error; `__callStatic` not static reports error; `__destruct` with parameters reports error
- [ ] **Parameter validation:** duplicate parameter names report error; required param after optional reports warning; variadic not last reports error; variadic with default reports error
- [ ] **Named arguments:** duplicate named arg reports error; positional after named reports error; named arg for non-existent param reports error
- [ ] **Closures:** `use` variable not defined reports error; static closure referencing `$this` reports error; arrow function captured variable not defined reports error
- [ ] **Generators:** `yield` outside generator function reports error; `yield` in `finally` reports error; `yield from` non-iterable reports error
- [ ] **Constant expressions:** variable reference in class constant initializer reports error; function call in enum case value reports error; division by zero in constant expression reports error
- [ ] **Array/list:** duplicate array keys produce warning; array access on non-array/non-ArrayAccess reports error; list destructuring on non-array reports error
- [ ] **Method resolution:** `$this` in static method reports error; non-static method called statically reports error; `static::` outside class reports error
- [ ] **Goto:** `goto` statement or label declaration reports `CheckerGotoProhibited` (4104) error (goto is prohibited in Tyhp)
- [ ] **Promoted properties:** promoted property without type reports error; promoted variadic reports error; readonly class with mutable property reports error
- [ ] **Enums:** non-backed enum with case value reports error; backed enum without case value reports error; duplicate case values reports error; enum with instance property reports error
- [ ] **Interfaces/traits:** interface with property initializer reports error; duplicate trait methods without resolution reports error; circular trait use reports error
- [ ] **Comparisons:** loose `==`/`!=` is allowed without warning; loose comparisons do NOT trigger type narrowing; string concat with non-stringable object reports error
- [ ] **Exception handling:** empty catch block produces warning; catching parent exception before child produces warning; return in finally produces warning; duplicate catch type produces warning
- [ ] All diagnostics use correct `MessageCode` values with file/line/column from AST nodes
- [ ] Each rule class file stays under 500 lines
- [ ] The checker does not crash on any of the `Examples/*.tyhp` or `Examples/*.php` files (may produce diagnostics, but does not crash)

### Dependencies

- **Requires:** Phase 1 (TyhpChecker core, CheckerState), Phase 2 (VariableState, TypeInferrer, expression type resolution), Phase 3 (TypeComparer for all type compatibility checks)
- **Provides:** A functional type checker for standard PHP-equivalent code; foundation for Tyhp-specific checks in Phases 5-6

---

## Phase 5: Tier 2 — Tyhp-Specific Type Checking




### Phase Overview

Add checks for Tyhp language features that go beyond standard PHP: mandatory type annotations, variable type inference, control-flow type narrowing, type guard function validation, generic type parameter constraint checking, struct property validation, and struct structural compatibility. These are the features that distinguish Tyhp from PHP and make it a statically-typed language.

### Deliverables

- `Tyhp/TyhpLang/Checker/Rules/TypeAnnotationRule.cs` — Type annotation validation, required types everywhere
- `Tyhp/TyhpLang/Checker/Rules/NullSafetyRule.cs` — Non-nullable enforcement
- `Tyhp/TyhpLang/Checker/Rules/TypeNarrowingRule.cs` — Control flow type narrowing, smart casts
- `Tyhp/TyhpLang/Checker/Rules/GenericRule.cs` — Generic constraint checking
- `Tyhp/TyhpLang/Checker/Rules/StructRule.cs` — Struct property validation, structural compatibility
- Updated `TypeInferrer.cs` — Type inference from initializer expressions and contextual closure parameter inference
- Updated `Tyhp/TyhpLang/Grammar/TyhpParser.g4` — Relax `parameterTypeExpressionGrammarAddon` to `optionalTypeWithoutStatic` (type enforcement moves from grammar to checker). **Requires regenerating the ANTLR parser via `./compile_grammar.sh`, then `dotnet clean && dotnet restore && dotnet build`.**

### Implementation Details

#### 5.1 Required Types Everywhere and Non-Nullable by Default

Tyhp requires type annotations on all locations where PHP supports type hints. This is a key difference from PHP. Additionally, **all types are non-nullable by default** - a type like `string` can never hold `null`. Developers must use `?string` (or `string|null`) to explicitly opt in to nullability. There is no configuration flag to weaken this; non-nullable by default is always enforced in Tyhp mode. (See Design Principle 7.)

**Check locations requiring type annotations:**
- Function/method parameters (each parameter must have a type annotation)
- Closure/fn expression parameters (type annotation required unless inferable from calling context — see §5.11)
- Function/method return types (must be declared)
- Class properties (must have a type annotation)
- Class constants (type annotation recommended but may be inferred from value)
- Variables (must have a type annotation OR an initializer from which the type can be inferred; see Design Principle 9 for inference-without-keywords)

For each location without a type annotation:
- If the type can be inferred from the initializer/default value, infer it and use the inferred type (narrowest possible type from the first assignment)
- If the type cannot be inferred (e.g., uninitialized variable with no annotation), report `MessageCode.CheckerVariableTypeRequired` (4016)

**Non-nullable enforcement checks:**
- When checking any assignment, argument pass, or return: if the target type is non-nullable (i.e., not `?T`, not `T|null`, and not `mixed`), then `null` is NOT a valid value. The checker must reject:
  - Direct null assignment: `string $x = null;` produces error (`CheckerTypeMismatch` 4008)
  - Null argument: `foo(null)` where parameter is `string` produces error (`CheckerIncompatibleArgumentType` 4010)
  - Null return: `return null;` in a function returning `string` produces error (`CheckerIncompatibleReturnType` 4009)
  - Possibly-null variable used as non-null: if `$x` has type `?string` and is used where `string` is expected without a null check, produces error (`CheckerVariablePossiblyNull` 4015)
- The `IsPossiblyNull` flag on `VariableState` must be set correctly:
  - After assigning a nullable expression to a variable, `IsPossiblyNull = true`
  - After a null check (`$x !== null`), `IsPossiblyNull = false` in the true branch
  - After merging branches where one path could be null, `IsPossiblyNull = true`
- The `TypeComparer.IsAssignableTo()` rules from Phase 3 already handle this: `null` is only assignable to nullable targets (rule 5). This section ensures the checker actively produces diagnostics at every site where a possibly-null or explicitly-null value flows into a non-nullable location.

Implementation location: Extend the declaration check methods from Phase 4 to enforce type annotation presence and non-nullable semantics.

#### 5.2 Variable Type Inference

When a variable has no explicit type annotation but has an initializer, infer the type from the initializer expression:

**`ICheckedType InferVariableType(IBase2Ast initializer, CheckerState state)`**

- If the initializer is a literal: use the literal's type (`42` → `int`, `'hello'` → `string`, `true` → `bool`, `3.14` → `float`, `null` → `null`)
- If the initializer is a `new ClassName()`: use `ClassName`
- If the initializer is a function call: use the function's return type
- If the initializer is a method call: use the method's return type
- If the initializer is a variable reference: use that variable's current type
- If the initializer is a ternary/null-coalescing: use the union of branch types
- If the initializer is a cast: use the cast target type
- If the initializer is an array literal: infer `array<keyType, valueType>` from elements
- If the initializer is a binary expression: use the expression's result type
- If the type cannot be inferred: return `CheckedTypes.Unknown` and report diagnostic

Once inferred, the variable is treated as if it had that type annotation. The inferred type is stored in `_expressionTypes` and on the `VariableState`.

#### 5.3 Automatic Smart Casts via Type Narrowing Through Control Flow

Tyhp performs **automatic smart casts** (type narrowing) when control flow provides type information. The checker must track narrowed types through conditional branches. Developers do NOT need to perform explicit casts or re-declare variables after a type check; the checker automatically updates the variable's effective type within the narrowing scope. This is a core Tyhp feature (see Design Principle 8).

For example, after `if ($x instanceof Foo)`, the variable `$x` is automatically treated as type `Foo` inside the if-body. Calling `$x->fooMethod()` is valid without any cast. In the else-branch, `$x` is automatically narrowed to exclude `Foo`. This smart cast behavior applies to all forms of type narrowing below.

**`instanceof` narrowing:**
```
if ($x instanceof Foo) {
    // $x is automatically smart-cast to Foo here; no explicit cast needed
    // calling $x->fooMethod() is valid because the checker knows $x is Foo
} else {
    // $x is automatically narrowed to "original type minus Foo" here
}
```

Note: The `is`/`isa`/`isan` keywords are aliases for `instanceof` and trigger the same smart cast behavior.

Implementation:
- In `CheckIfStatement`, analyze the condition AST
- If condition is `$var instanceof ClassName` (or `$var is ClassName`):
  - In the true-branch state: `NarrowVariable($var, ClassName)`
  - In the false-branch state: `NarrowVariable($var, NarrowTypeNegative(originalType, ClassName))`

**Null check narrowing (automatic smart cast, critical for non-nullable-by-default enforcement):**
```
if ($x !== null) {
    // $x is narrowed to non-null here
} else {
    // $x is narrowed to null here
}
```

Implementation:
- If condition is `$var !== null`:
  - True branch: narrow to non-null (remove `null` from union, remove nullable wrapper)
  - False branch: narrow to `null`
- If condition is `$var === null`: reverse of above
- Note: loose comparisons (`$var != null`, `$var == null`) do NOT produce type narrowing (see §4.20).

**Truthiness narrowing:**
- Tyhp does NOT support truthiness narrowing. `if ($x)` where `$x` is not `bool` always produces `CheckerConditionNotBool` (4043). Developers must use explicit checks: `if ($x !== null)` for null checks, `if ($x !== 0)` for zero checks, etc.

**Type guard function narrowing (automatic smart cast):**
```
function isString(mixed $x): $x is string { return \is_string($x); }
if (isString($y)) {
    // $y is automatically smart-cast to string here (no explicit cast needed)
}
```

Implementation:
- When the condition is a function call to a type guard function:
  - Look up the function's return type — if it's a `$param is Type` type guard return
  - Map the call argument to the parameter
  - In the true branch: narrow the argument variable to the guard's target type

**Built-in type guard recognition:**
- `is_string($x)` → narrow to `string`
- `is_int($x)` → narrow to `int`
- `is_float($x)` → narrow to `float`
- `is_bool($x)` → narrow to `bool`
- `is_array($x)` → narrow to `array`
- `is_null($x)` → narrow to `null`
- `is_object($x)` → narrow to `object`
- `is_callable($x)` → narrow to `callable`
- `is_numeric($x)` → narrow to `int|float|string` (numeric strings)

The checker should have a registry of known built-in type guard functions and their narrowing effects. All of the above trigger automatic smart casts: when `is_string($x)` is the condition of an `if`, the variable `$x` is automatically treated as `string` inside the true branch without any explicit cast.

#### 5.4 Type Guard Function Validation

When a function declares a type guard return type (`$param is Type`):
- The function must return `bool` → `MessageCode.CheckerTypeGuardInvalidReturn` (4032)
- The function must actually perform a type check on the specified parameter (warning if the body does not reference the parameter in a type-checking context)
- The parameter name in `$param is Type` must match an actual parameter of the function
- The guard type must be a valid type expression

#### 5.5 Generic Type Parameter Constraint Checking

When a generic type is instantiated (e.g., `new Collection<User>()`):
- Resolve the generic type definition (e.g., `Collection<T extends Countable>`)
- For each type argument, check that it satisfies the constraint → `MessageCode.CheckerGenericConstraintNotSatisfied` (4035)
  - If constraint is `T extends SomeClass`: the type argument must be a subtype of `SomeClass`
  - If constraint is `T extends SomeInterface`: the type argument must implement `SomeInterface`
- Check that the correct number of type arguments is provided → `MessageCode.CheckerGenericArgumentCountMismatch` (4036)
  - If fewer than required: error
  - If more than allowed: error
  - If generic parameters have defaults, they are optional (SEE: IMPLEMENTATION_PLAN_TODO_STORY_28.md for full generic default validation and application logic)

#### 5.6 Generic Type Argument Validation in Method/Function Calls

When calling a generic function or method:
- If explicit type arguments are provided (e.g., `foo<int, string>($x)`), validate each against constraints
- If type arguments are omitted, infer them from the call-site arguments:
  - For each parameter with a generic type, match the argument type against the parameter type pattern
  - If inference is ambiguous or fails, request explicit type arguments
- After inference/validation, substitute type arguments and re-check the call with concrete types

#### 5.6a Runtime Generic Tracking Flag

A generic class requires runtime generic tracking when ANY of the following conditions are met:
- `instanceof T` or `is T` type checks are used with a generic type parameter
- `new T()` construction using a generic type parameter
- `typeof(T)` expressions referencing a generic type parameter
- Passing generic type arguments to constructors of other generic classes (e.g., `new Collection<T>()`)
- Any runtime type operation that cannot be statically erased
- The class contains generic-typed properties that require runtime type enforcement via the `GenericObject` trait

Store `RequiresRuntimeGenericTracking` in a side dictionary (`Dictionary<ObjectDeclarationSymbol, bool>`) on `CheckerState` or `CompilationResult`, consistent with the existing `ExpressionTypes` side dictionary pattern. Do NOT mutate the symbol directly — the checker must remain read-only with respect to the symbol tree.

When this flag is set, the emitter reads the `RequiresRuntimeGenericTracking` side dictionary and adds the `\Tyhp\Concerns\GenericObject` trait to the class and emits `tyhpGenericObjectInit()` calls in the constructor with the concrete type arguments. Runtime access to generic type parameters uses `$this->tyhpGenericObjectGetGenericType('T')`.

#### 5.7 Callable Generic Type Validation

When the checker encounters a `callable` type with generic type arguments (e.g., `callable<A, B, C>`), it must validate and interpret them using the **return-last convention**: the last type argument is the return type, and everything before it represents the parameter types.

**Resolution rules:**

- `callable<A, B, C>` → `CallableCheckedType` with `ParameterTypes = [A, B]` and `ReturnType = C`
- `callable<R>` (single type argument) → `CallableCheckedType` with `ParameterTypes = []` and `ReturnType = R`
- `callable` (zero type arguments) → untyped callable; no generic validation is performed

**Implementation in `TypeInferrer.ResolveTypeExpression()`:**

When resolving a generic type expression where the base type is `callable`:
1. Do NOT create a `GenericCheckedType(callable, args)` — instead, create a `CallableCheckedType` directly
2. Extract the last type argument as the return type
3. Extract all preceding type arguments as the parameter types
4. Validate each type argument individually (see §5.7a for restricted type validation)

**Examples:**

| Tyhp Source | Resolved CheckedType |
|-------------|---------------------|
| `callable<string, int>` | `CallableCheckedType(params=[string], return=int)` — takes string, returns int |
| `callable<int, int, bool>` | `CallableCheckedType(params=[int, int], return=bool)` — takes (int, int), returns bool |
| `callable<void>` | `CallableCheckedType(params=[], return=void)` — takes nothing, returns void |
| `callable<string, void>` | `CallableCheckedType(params=[string], return=void)` — takes string, returns void |
| `callable` | Untyped callable (no generic args, no validation) |

The `callable` built-in type's generic parameters have the following implicit constraints:
- Parameter-position type arguments: no special constraints (standard types)
- Last type argument (return position): `extends void|never|mixed` — this allows `void` and `never` as return types

#### 5.8 Built-in Utility Type Resolution

Tyhp provides built-in utility types in the `\Tyhp` namespace, inspired by TypeScript's utility types. These are NOT ordinary generic types — they are **checker operations** that transform their type arguments at compile time. The binder registers them as special built-in types (Story 06, Phase 3); the checker resolves them during type expression resolution.

When `TypeInferrer.ResolveTypeExpression()` encounters a type in the `\Tyhp` namespace that matches one of the registered utility types, it delegates to the appropriate resolver method instead of creating a standard `GenericCheckedType`.

**Property modifier utility types** (T must be a class, interface, or struct type):

| Utility Type | Resolver | Behavior |
|---|---|---|
| `\Tyhp\Readonly<T>` | `ResolveReadonlyUtility(T)` | Returns a copy of T where all public/protected properties have `IsReadonly = true`. If T is already readonly or has no properties, returns T unchanged. |
| `\Tyhp\Partial<T>` | `ResolvePartialUtility(T)` | Returns a copy of T where all properties are nullable (`?PropertyType`). Useful for creating "patch" types for partial updates. |
| `\Tyhp\Required<T>` | `ResolveRequiredUtility(T)` | Returns a copy of T where all nullable properties become non-nullable. Inverse of `Partial<T>`. |
| `\Tyhp\Pick<T, K>` | `ResolvePickUtility(T, K)` | K must be a string literal or union of string literals matching property names of T. Returns a type containing only the named properties. Error if K names a property that doesn't exist on T. |
| `\Tyhp\Omit<T, K>` | `ResolveOmitUtility(T, K)` | K must be a string literal or union of string literals. Returns a type containing all properties of T except those named in K. Error if K names a property that doesn't exist on T. |

**Type manipulator utility types** (operate on any type):

| Utility Type | Resolver | Behavior |
|---|---|---|
| `\Tyhp\Record<K, V>` | `ResolveRecordUtility(K, V)` | Returns `array<K, V>`. K must satisfy `int\|string` constraint. Syntactic sugar for typed associative arrays. |
| `\Tyhp\Exclude<T, U>` | `ResolveExcludeUtility(T, U)` | T should be a union type. Returns T with all members assignable to U removed. If T is not a union, returns `never` if T is assignable to U, otherwise returns T. |
| `\Tyhp\Extract<T, U>` | `ResolveExtractUtility(T, U)` | T should be a union type. Returns T with only members assignable to U kept. If T is not a union, returns T if T is assignable to U, otherwise returns `never`. |
| `\Tyhp\NonNullable<T>` | `ResolveNonNullableUtility(T)` | Removes `null` from T. If T is `?SomeType`, returns `SomeType`. If T is `SomeType\|null\|OtherType`, returns `SomeType\|OtherType`. If T is not nullable, returns T unchanged. |
| `\Tyhp\Nullable<T>` | `ResolveNullableUtility(T)` | Returns `?T` (equivalent to `T\|null`). If T is already nullable, returns T unchanged. |

**Function type introspection utility types** (T must be a callable type):

| Utility Type | Resolver | Behavior |
|---|---|---|
| `\Tyhp\ReturnType<T>` | `ResolveReturnTypeUtility(T)` | T must be `callable<..., R>` or `\Closure<..., R>`. Returns R (the return type). If T is an untyped `callable`, returns `mixed`. Error if T is not callable. |
| `\Tyhp\Parameters<T>` | `ResolveParametersUtility(T)` | T must be `callable<A, B, ..., R>` or `\Closure<A, B, ..., R>`. Returns `array<int, A\|B\|...>` representing the parameter types (ordered). If T is an untyped `callable`, returns `array<int, mixed>`. Error if T is not callable. |

**Async utility types:**

| Utility Type | Resolver | Behavior |
|---|---|---|
| `\Tyhp\Awaited<T>` | `ResolveAwaitedUtility(T)` | If T is `Promise<V>`, returns `Awaited<V>` (recursively unwraps nested Promises). If T is not a Promise, returns T unchanged. E.g., `Awaited<Promise<Promise<string>>>` → `string`. |

**Validation rules:**
- If a utility type receives an argument that violates its constraint (e.g., `\Tyhp\Readonly<int>` where `int` is not a class/struct), report `MessageCode.CheckerGenericConstraintNotSatisfied` (4035)
- If `Pick<T, K>` or `Omit<T, K>` receives a K that names a non-existent property, report a new error `MessageCode.CheckerUtilityTypeInvalidKey` (suggest code 4050)
- Utility types are resolved eagerly during type expression resolution — they do not produce `GenericCheckedType` nodes in the type tree

**Implementation location:** Utility type resolution is handled by the dedicated `UtilityTypeResolver` class (see File Organization). `TypeInferrer.ResolveTypeExpression()` delegates to `UtilityTypeResolver` when it encounters a type in the `\Tyhp` namespace. The check for `\Tyhp` namespace membership is a fast namespace prefix check before any resolver lookup.

#### 5.7a Restricted Types in Generic Positions (`void` and `never`)

`void` and `never` are **return-type-only** types. They must NOT be allowed as generic type arguments UNLESS the generic parameter's constraint explicitly includes them. This prevents nonsensical types like `array<void>` while allowing valid uses like `callable<string, void>` and `Promise<void>`.

**Checker rule — when a type argument `T_arg` is provided for a generic parameter `T_param`:**

1. If `T_arg` is `void` or `never`:
   - Check if `T_param`'s constraint union includes `void` or `never` respectively (e.g., `TReturn extends void|never|mixed`)
   - If the constraint allows it → OK, the type argument is valid
   - If the constraint does NOT include the restricted type → Error
2. If `T_arg` is any other type → proceed with normal constraint checking (§5.5)

**Error codes:**
- `void` in a non-allowed position → `MessageCode.CheckerVoidInNonReturnPosition` (4048): "Type 'void' can only be used as a return type or in generic positions that explicitly allow it via constraint"
- `never` in a non-allowed position → `MessageCode.CheckerNeverInNonReturnPosition` (4049): "Type 'never' can only be used as a return type or in generic positions that explicitly allow it via constraint"

**Built-in types that opt in:**
- `callable`'s last (return) parameter has constraint `extends void|never|mixed`, so `callable<string, void>` is valid
- `Promise<T extends void|mixed>` allows `void` as its type argument, so `Promise<void>` is valid

**User-defined types can opt in** by declaring a constraint that includes `void` or `never`:
```
class MyResult<TReturn extends void|mixed> { ... }
```
This allows `MyResult<void>` because `T`'s constraint explicitly includes `void`.

**Types that do NOT opt in:**
- `array<T>` — `T` has no `void`/`never` constraint, so `array<void>` is an error
- `Collection<T extends Countable>` — constraint is `Countable`, not `void`, so `Collection<void>` is an error

**Examples:**

| Expression | Valid? | Reason |
|------------|--------|--------|
| `callable<string, int>` | Yes | No restricted types used |
| `callable<void>` | Yes | `void` is in return position; constraint allows it |
| `callable<string, void>` | Yes | `void` is last arg (return); constraint allows it |
| `callable<void, string>` | **Error** (4048) | `void` is in parameter position; no constraint allows it |
| `array<void>` | **Error** (4048) | `array`'s `T` has no `void` constraint |
| `array<never>` | **Error** (4049) | `array`'s `T` has no `never` constraint |
| `Promise<void>` | Yes | `Promise<T extends void\|mixed>` allows `void` |
| `MyType<T extends void\|mixed>` used as `MyType<void>` | Yes | User-defined constraint allows it |

**Integration with §5.5 (Generic Type Parameter Constraint Checking):**

The restricted-type check should run BEFORE the normal constraint satisfaction check in §5.5. When `CheckerGenericConstraintNotSatisfied` (4035) is evaluated, first check whether the type argument is `void` or `never` and whether the constraint explicitly permits it. If it doesn't, report the more specific 4048/4049 error instead of the generic 4035 error.

#### 5.9 Struct Property Validation

When checking a `struct` declaration:
- All properties must have type annotations → `MessageCode.CheckerStructPropertyRequired` (4037)
- All properties must have a default value OR be nullable (so the struct can be default-constructed)
- Struct properties are implicitly `readonly` after construction
- Structs cannot have methods (beyond built-in operations)
- Structs cannot `extend` classes or `implement` interfaces (they are value types)
- Struct compatibility: two structs with the same property shapes are structurally compatible

#### 5.10 Struct Compatibility Checking

Structs in Tyhp are backed by PHP associative arrays at runtime. The checker must understand this relationship for correct type compatibility.

**Struct-to-struct assignability:**

A struct is assignable to another struct if the source has ALL properties of the target with assignable types (width subtyping — the source may have more properties than the target).

**Struct-to-array assignability:**

Since structs are associative arrays at runtime, a struct is assignable to array types with appropriate widening:

1. **`struct` → `array`** — always valid (untyped array)
2. **`struct` → `array<string, mixed>`** — always valid (all struct keys are strings, all values are `mixed`-compatible)
3. **`struct` → `array<string, V>`** — valid if the union of ALL the struct's property value types is assignable to `V`. For example:
   - `struct { name: string, label: string }` is assignable to `array<string, string>` (all values are `string`)
   - `struct { name: string, age: int }` is assignable to `array<string, string|int>` (union of values fits)
   - `struct { name: string, age: int }` is NOT assignable to `array<string, string>` (the `int` property doesn't fit)
4. **`struct` → `array<int, V>`** — NOT valid (struct keys are strings, not integers)
5. **`struct` → `iterable`** — valid (since `array` is assignable to `iterable`, and struct is assignable to `array`)

**Type information loss on struct-to-array widening:**

When a struct is widened to an array type, the per-property type information and key names are lost. The resulting array type only knows the key type (`string`) and the union of all value types. This is a one-way operation:

```
struct User { name: string, age: int }

$user = User { name: 'Alice', age: 30 };
$arr: array<string, string|int> = $user;  // OK — widening

// After widening:
// $arr['name'] has type string|int (NOT string)
// $arr['age'] has type string|int (NOT int)
// $arr['nonexistent'] has type string|int (no key validation)
// The checker no longer knows which keys exist or their individual types
```

This means struct-to-array widening is **lossy** — the developer loses compile-time guarantees about specific keys and their types. The checker should NOT implicitly narrow back from `array<string, V>` to a struct type without explicit type assertion or validation.

**Array-to-struct assignability:**

- An array literal with the right shape (all required keys present with compatible value types) is assignable to a struct type
- A typed `array<string, V>` variable is NOT implicitly assignable to a struct type (the checker cannot verify at compile time that the array has the right keys with the right per-key types)
- An explicit struct construction from an array (e.g., via a type assertion or factory) is required to go from array back to struct

#### 5.11 Contextual Type Inference for Closure Parameters

**Grammar prerequisite:** The `parameterTypeExpressionGrammarAddon` rule in `Tyhp/TyhpLang/Grammar/TyhpParser.g4` currently forces Tyhp mode to use `typeExprWithoutStatic` (type required at parse time). This must be relaxed to `optionalTypeWithoutStatic` so that untyped parameters are syntactically valid. Type enforcement moves entirely to the checker:

```antlr
// Tyhp/TyhpLang/Grammar/TyhpParser.g4 — CHANGE REQUIRED
// Before:
parameterTypeExpressionGrammarAddon
    : {this.isLanguageMode("tyhp")}? typeExprWithoutStatic
    | {!this.isLanguageMode("tyhp")}? optionalTypeWithoutStatic
    ;

// After:
parameterTypeExpressionGrammarAddon
    : optionalTypeWithoutStatic
    ;
```

With this change, the grammar allows untyped parameters in both modes. The checker (§5.1) enforces that function/method parameters still require explicit type annotations, while closure/fn parameters (this section) allow contextual inference.

> **Build note:** Editing `Tyhp/TyhpLang/Grammar/TyhpParser.g4` requires **regenerating the ANTLR parser** before this checker behavior takes effect — run `./compile_grammar.sh` from the repository root, then `dotnet clean && dotnet restore && dotnet build`, and commit the updated generated sources in `Tyhp/TyhpLang/Parser/`. Grammar regeneration is **not** triggered by `dotnet build` alone. Until the parser is regenerated, untyped closure/`fn` parameters will still fail at parse time and the §5.11 checker logic will not be exercised.

---

Tyhp requires all parameters to have types. For closure/fn expression parameters, the type can be omitted when it is inferable from the calling context. If the type cannot be inferred, the checker reports an error.

**Inference sources (in priority order):**

1. **Function/method parameter type:** When a closure is passed as an argument to a function whose parameter has a typed signature:
   - `callable<T, R>` → first closure parameter is `T`, return is `R`
   - `callable<T1, T2, R>` → first closure param is `T1`, second is `T2`, return is `R`
   - `Expression<T, R>` → first closure parameter is `T`, return is `R`
   - `PropertyPath<T, R>` → first closure parameter is `T`, return is `R`
   - `\Closure(T): R` → first closure parameter is `T`, return is `R`

2. **Generic type resolution:** When the target type contains generic parameters (e.g., `Expression<T, bool>` where `T` is bound to `User` from `QueryBuilder<User>`), resolve the generic first, then use the resolved type for parameter inference.

3. **Variable type annotation:** When a closure is assigned to a typed variable:
   ```tyhp
   callable<User, bool> $predicate = fn ($u) => $u->age > 18;
   // $u inferred as User from the variable's type
   ```

4. **Return type context:** When a closure is the return value of a function with a declared return type:
   ```tyhp
   function getPredicate(): callable<User, bool> {
       return fn ($u) => $u->age > 18;
       // $u inferred as User from the return type
   }
   ```

**Inference algorithm — `InferClosureParameterTypes(closureAst, expectedType, state)`:**

1. If `expectedType` is a callable/closure type with known parameter types:
   a. Match each untyped closure parameter (by position) to the corresponding expected parameter type
   b. If the closure has MORE parameters than the expected type, the extra parameters cannot be inferred — report `CheckerClosureParameterTypeRequired` (4138)
   c. If the closure has FEWER parameters, that's fine (partial application / ignored parameters)
2. Store the inferred types on the closure parameter symbols via `VariableState` with `IsInferred = true`
3. Continue type-checking the closure body with the inferred parameter types
4. If any parameter has no explicit type AND no inferable type, report:
   - `CheckerClosureParameterTypeRequired = 4138` — "Cannot infer type for closure parameter '${0}'; provide an explicit type annotation"

**Interaction with existing checks:**

- The "required types everywhere" check (§5.1) currently requires type annotations on function/method parameters. For closure/fn parameters specifically, this check is relaxed: the type is required but can be satisfied by either an explicit annotation OR contextual inference.
- The `TypeInferrer.InferExpressionType()` for closures (currently returns `CallableCheckedType`) must now include the inferred parameter types, not just explicitly annotated ones.
- The emitter (Story 11) must emit the inferred types in the generated PHP output. When a closure parameter type is inferred, the emitter generates the fully typed PHP closure: `fn (\App\Models\User $u) => ...` even though the Tyhp source wrote `fn ($u) => ...`.

**Edge cases:**

- **Already typed parameters:** If the closure parameter already has an explicit type, the explicit type takes precedence. The checker validates that the explicit type is compatible with the expected type.
- **Mixed typed/untyped:** `fn (User $u, $v) => ...` — `$u` uses explicit type, `$v` is inferred from context. Each parameter is handled independently.
- **No context available:** `$fn = fn ($u) => $u->age;` without a typed variable — cannot infer `$u`. Error: `CheckerClosureParameterTypeRequired` (4138).
- **Nested closures:** `fn ($u) => fn ($v) => $u->related($v)` — outer parameter inferred from calling context, inner parameter inferred from the return type context of the outer closure.
- **Chained method calls:** `->where(fn ($u) => ...).select(fn ($u) => ...)` — each closure independently infers from its own method's parameter type.

### Acceptance Criteria

- [ ] Variables without type annotations but with initializers have their types correctly inferred
- [ ] Variables without type annotations AND without initializers produce `CheckerVariableTypeRequired` diagnostic
- [ ] Function parameters without type annotations produce a diagnostic (in Tyhp mode)
- [ ] Function return types missing produce a diagnostic (in Tyhp mode)
- [ ] `instanceof` narrowing works: variable type changes inside the true/false branches of an `if ($x instanceof Foo)` check
- [ ] Null check narrowing works: `if ($x !== null)` narrows to non-null in true branch
- [ ] Built-in type guard functions (`is_string`, `is_int`, etc.) trigger type narrowing
- [ ] User-defined type guard functions (`$param is Type` return) trigger type narrowing
- [ ] Type guard functions validate: must return `bool`, parameter must exist
- [ ] Generic type constraints are checked at instantiation sites
- [ ] Generic type argument count mismatches are detected
- [ ] Generic type inference from call-site arguments works for simple cases
- [ ] Struct properties validate: all typed, all have defaults or nullable
- [ ] Struct structural compatibility: matching shapes are assignable
- [ ] Narrowing resets when a variable is reassigned after a narrowing point
- [ ] **Non-nullable by default:** assigning `null` to a non-nullable variable produces `CheckerTypeMismatch` (4008)
- [ ] **Non-nullable by default:** passing `null` as a non-nullable parameter produces `CheckerIncompatibleArgumentType` (4010)
- [ ] **Non-nullable by default:** returning `null` from a function with non-nullable return type produces `CheckerIncompatibleReturnType` (4009)
- [ ] **Non-nullable by default:** using a `?string` variable where `string` is expected (without null check) produces `CheckerVariablePossiblyNull` (4015)
- [ ] **Non-nullable by default:** `null` IS assignable to `?string` (nullable) with no error
- [ ] **Smart casts:** after `if ($x instanceof Foo)`, calling `$x->fooMethod()` in the true branch is valid without explicit cast
- [ ] **Smart casts:** after `if ($x !== null)`, using `$x` where a non-nullable type is expected is valid in the true branch
- [ ] **Smart casts:** after a type guard function call `if (isUser($x))`, `$x` is treated as the guard target type in the true branch
- [ ] **Smart casts:** in the else-branch of any narrowing check, the type is automatically narrowed to exclude the checked type
- [ ] **Type inference:** first assignment without type annotation infers at narrowest possible type (no `var`/`auto` keyword needed)
- [ ] **Callable generics:** `callable<string, int>` is validated as "takes string, returns int" (return-last convention)
- [ ] **Callable generics:** `callable<int, int, bool>` is validated as "takes (int, int), returns bool"
- [ ] **Callable generics:** `callable<void>` is valid (return-type position allows void via constraint)
- [ ] **Callable generics:** `callable<string, void>` is valid (last arg is return, void allowed via constraint)
- [ ] **Callable generics:** `callable<void, string>` is an error — `CheckerVoidInNonReturnPosition` (4048) — void in parameter position with no constraint
- [ ] **Restricted types:** `array<void>` is an error — `CheckerVoidInNonReturnPosition` (4048) — array's T has no void constraint
- [ ] **Restricted types:** `array<never>` is an error — `CheckerNeverInNonReturnPosition` (4049) — array's T has no never constraint
- [ ] **Restricted types:** `Promise<void>` is valid — Promise's T has `extends void|mixed` constraint
- [ ] **Restricted types:** User-defined `MyType<T extends void|mixed>` correctly allows void as type argument
- [ ] All new checks produce diagnostics with correct `MessageCode`, file, line, column
- [ ] **Closure parameter inference:** `fn ($u) => $u->age` passed to `callable<User, int>` parameter infers `$u` as `User`
- [ ] **Closure parameter inference:** `fn ($u) => $u->age > 18` passed to `Expression<User, bool>` infers `$u` as `User`
- [ ] **Closure parameter inference:** generic resolution works — `QueryBuilder<User>::where(fn ($u) => ...)` infers `$u` as `User`
- [ ] **Closure parameter inference:** variable assignment — `callable<User, bool> $fn = fn ($u) => ...` infers `$u` as `User`
- [ ] **Closure parameter inference:** explicit types still work and take precedence over inference
- [ ] **Closure parameter inference:** mixed typed/untyped params — `fn (User $u, $v) => ...` infers only `$v`
- [ ] **Closure parameter inference:** no context produces `CheckerClosureParameterTypeRequired` (4138)
- [ ] **Closure parameter inference:** emitter generates fully typed PHP closures with inferred types
- [ ] `\Tyhp\Readonly<MyStruct>` resolves to a type where all properties are readonly
- [ ] `\Tyhp\Partial<MyStruct>` resolves to a type where all properties are nullable
- [ ] `\Tyhp\Required<MyStruct>` resolves to a type where all properties are non-nullable
- [ ] `\Tyhp\Pick<MyStruct, 'name'|'age'>` resolves to a type with only 'name' and 'age' properties
- [ ] `\Tyhp\Omit<MyStruct, 'age'>` resolves to a type with all properties except 'age'
- [ ] `\Tyhp\Record<string, int>` resolves to `array<string, int>`
- [ ] `\Tyhp\Exclude<string|int|null, null>` resolves to `string|int`
- [ ] `\Tyhp\Extract<string|int|null, string>` resolves to `string`
- [ ] `\Tyhp\NonNullable<?string>` resolves to `string`
- [ ] `\Tyhp\Nullable<string>` resolves to `?string`
- [ ] `\Tyhp\ReturnType<callable<string, int>>` resolves to `int`
- [ ] `\Tyhp\Parameters<callable<string, int, bool>>` resolves to `array<int, string|int>`
- [ ] `\Tyhp\Awaited<Promise<string>>` resolves to `string`
- [ ] `\Tyhp\Awaited<Promise<Promise<int>>>` resolves to `int` (recursive unwrap)
- [ ] Invalid utility type usage (e.g., `\Tyhp\Readonly<int>`) produces appropriate error diagnostic

### Dependencies

- **Requires:** Phase 3 (TypeComparer for structural/generic/nullable comparison), Phase 4 (declaration and expression check infrastructure), Phase 2 (VariableState narrowing operations, TypeInferrer)
- **Provides:** Tyhp's core differentiating type checking features; foundation for advanced features in Phase 6

---

## Phase 6: Tier 3 — Advanced Tyhp Feature Checks




### Phase Overview

Add checks for advanced Tyhp features: operator overload validation, extension method validation, type alias expansion, static value type validation, `with` keyword validation, disposable `:=` validation, async/await validation, compile-time construct validation, dynamic language feature validation, function overload validation, trait requirement validation, property accessor validation, and restricted PHP feature detection (`eval`, `include`/`require`).

### Deliverables

- `Tyhp/TyhpLang/Checker/Rules/OperatorOverloadRule.cs` — Operator overload validation
- `Tyhp/TyhpLang/Checker/Rules/ExtensionRule.cs` — Extension method/operator validation
- `Tyhp/TyhpLang/Checker/Rules/AsyncRule.cs` — Async/await context checks
- `Tyhp/TyhpLang/Checker/Rules/DisposableRule.cs` — Disposable `:=` validation
- `Tyhp/TyhpLang/Checker/Rules/CompileTimeRule.cs` — `nameof()`/`typeof()`/`default()`/`variable_exists()` validation
- `Tyhp/TyhpLang/Checker/Rules/DeprecationRule.cs` — Deprecated/obsolete symbol usage
- `Tyhp/TyhpLang/Checker/Rules/RestrictedFeatureRule.cs` — `eval`/`include`/`require`/variable variables/dynamic properties restrictions
- `Tyhp/TyhpLang/Checker/Rules/OverloadRule.cs` — Function/method overload signature validation
- `Tyhp/TyhpLang/Checker/Rules/AttributeRule.cs` — Attribute class, target, argument, and repeatability validation
- `Tyhp/TyhpLang/Checker/Rules/ImportRule.cs` — Unused imports, duplicate imports, conflicting aliases
- `Tyhp/TyhpLang/Checker/Rules/CodeQualityRule.cs` — Unused variables/params/members, dead stores, redundant casts, condition warnings
- Updated `TypeComparer.cs` — Type alias expansion support

### Implementation Details

#### 6.1 Operator Overload Validation

When checking a class that declares operator overloads:
- Validate the overload signature matches expectations for the operator:
  - Binary operators (`+`, `-`, `*`, `/`, etc.) must have two parameters (left and right operands), at least one of which is `self`
  - Unary operators (`+`, `-`, `!`, `~`) must have exactly one parameter (typed as `self`)
  - Comparison operators must return `bool` (or `int` for `<=>`)
  - The `convert` operator must specify a target type (return type) or source type (parameter type)
- Validate that the operator is in the `OverloadableOperator` enum (not all operators are overloadable)
- Error if `<Type>` target syntax is used on an operator inside a class body (not an extension) → `MessageCode.ExtensionOperatorTargetNotAllowed` (3015, from Story 03)

When checking an expression that uses an operator on types with overloads:
- Resolve the operator overload on the left-hand operand's type (check both class-level and extension operators)
- If not found on the left operand's type, check for fallback operators on the right operand's type
- Also check extension operators that are in scope (via `use extension` or auto-activated from tyhpdef)
- Use the overload's return type as the expression's result type
- Check that the right-hand operand is assignable to the overload's parameter type

#### 6.1.1 Extension Operator Overload Validation (Story 03)

When checking an extension that declares operator overloads:
- The operator must use the `<Type>` syntax to specify the target type → `MessageCode.ExtensionOperatorMissingTarget` (3014) if missing
- The `<Type>` must resolve to a valid type (class, interface, enum, or scalar)
- At least one of the operator parameters must be typed as `self` (which resolves to the `<Type>` target)
- Extension operators cannot conflict with operator overloads already declared on the target type (error if they do)
- Extension operators cannot conflict with other in-scope extension operators for the same type and operator

#### 6.1.2 Tyhpdef Inline Extension Operator Validation (Story 03)

When checking tyhpdef inline extension members (`extension operator`, `extension function`):
- The `extension` qualifier is only valid on `function` and `operator` — error on properties, constants, etc. → `MessageCode.TyhpdefInlineExtensionInvalidMember` (8012)
- `extension operator` must NOT have `<Type>` (target is the enclosing class) — error if present
- `self` in extension operator parameters resolves to the enclosing tyhpdef class
- `$this` in extension function bodies resolves to an instance of the enclosing tyhpdef class
- Extension function bodies can only access public members of the enclosing tyhpdef class (same restriction as regular extensions)

#### 6.2 Extension Method Validation

When checking an extension declaration:
- The extension must contain only `public static` methods → `MessageCode.CheckerExtensionMethodNotStatic` (4033), `MessageCode.CheckerExtensionMethodNotPublic` (4034)
- The first parameter of each extension method must use the `extends` keyword to specify the extended type
- The extended type must be a valid type (can be scalar, class, interface)
- Extension methods cannot conflict with existing methods on the extended type (warning if they shadow)

When checking a method call that resolves to an extension method (via binder's `ResolveExtensionMethod`):
- Validate the receiver expression type is compatible with the extension's first parameter type
- Check remaining arguments against the remaining parameters
- Return the extension method's return type

#### 6.2.1 Tyhpdef `use extension` Conflict Validation (Story 03)

When a tyhpdef class body contains `use extension ExtensionName`:
- Validate the extension reference resolves to a valid extension declaration
- Check for name conflicts between extension members and declared class methods → `MessageCode.TyhpdefExtensionConflict` (8010)
- If adaptations (`as`, `insteadof`) are present, validate they reference valid member names
- If conflicts remain unresolved by adaptations, report error with guidance to use `as` or `insteadof`
- **Visibility adaptations are invalid on extensions.** The grammar accepts `ExtName::method as protected;` because it reuses `traitAdaptations`, but extensions are always public. If a visibility-only adaptation is used (no rename, just a modifier), report error → the **already-committed** `MessageCode.CheckerExtensionVisibilityNotAllowed` (`4038`) — "Visibility cannot be changed on extension members; extension members are always public". (This diagnostic already exists in `MessageCode.cs`; Story 08 references it and does NOT redefine it. The previous draft incorrectly re-declared it as a new code `4139`, which both duplicated the existing `4038` diagnostic and collided with `CheckerCloneWithReadonlyRequiresConfig = 4139`.)

Note: This same check applies to `use extension` at the top-level of Tyhp source files (not just inside tyhpdef class bodies).

#### 6.3 Type Alias Expansion and Validation

When the checker encounters a type name that resolves to a `TypeAliasSymbol`:
- Expand the alias to its underlying type
- If the alias is generic, substitute the type arguments
- Recursively expand nested aliases
- Detect circular alias definitions (the binder checks this; the checker skips the check if the binder already reported it)
- The expanded type is used for all compatibility checks

Implementation: Add `ExpandTypeAliases(ICheckedType type)` method to `TypeComparer` that recursively expands aliases until a non-alias type is reached.

#### 6.4 String Literal Type Validation for Dynamic Features

Tyhp validates that string values used in dynamic contexts (dynamic class instantiation, dynamic method calls, etc.) are valid identifiers when possible. This validation uses the checker's symbol resolution, not special `__` prefixed type aliases.

When a parameter or variable is used in a dynamic context and the value is a string literal or compile-time constant:
- For dynamic class instantiation (`new $className()`): validate the string literal resolves to a valid class name
- For dynamic method calls (`$obj->$methodName()`): validate the string literal matches a method on the resolved type
- For dynamic function calls (`$functionName()`): validate the string literal resolves to a valid function name
- For dynamic property access (`$obj->$propName`): validate the string literal matches a property on the resolved type

If the string value is not a compile-time-known literal, emit a warning suggesting the developer use compile-time-known strings for safer dynamic dispatch.

#### 6.5 `with` Keyword Validation

The `with` keyword has three forms, each with different readonly rules:

**Common checks (all forms):**
- For each property in the `with` block:
  - The property name must exist on the target type → `MessageCode.CheckerWithKeywordInvalidProperty` (4031)
  - The value type must be assignable to the property's declared type
- **Struct-object structural compatibility:** When the `with` target type involves an intersection type like `object&StructType` or `MyClass&StructType` (as used by `\Tyhp\ObjectHelper::with()`), validate that the object type has all properties declared in the struct type with compatible types (see TypeComparer rule 15 — object-to-struct structural compatibility).

**Form-specific readonly checks:**

1. **`clone $obj with [...]`** — readonly property rules:
   - PHP 8.5+: always allowed (native `clone()` handles readonly)
   - PHP 8.2-8.4, non-final class, `build.experimentalReadonlyCloneWith` is `true`: allowed (emitter generates anonymous class wrapper)
   - PHP 8.2-8.4, non-final class, config is `false` (default): emit `MessageCode.CheckerCloneWithReadonlyRequiresConfig` (4139)
   - PHP 8.2-8.4, `final` class: emit `MessageCode.CheckerWithReadonlyFinalClass` (4140) — cannot extend final class for the anonymous wrapper

2. **`new MyClass(...) with [...]`** — readonly property rules:
   - PHP 8.5+: always allowed (emitter uses native `clone(new C(...), [...])`)
   - PHP 8.2-8.4, non-final class: always allowed (emitter wraps in anonymous class + clone; constructor arguments pass through via inheritance, no reflection needed, no opt-in required)
   - PHP 8.2-8.4, `final` class: emit `MessageCode.CheckerWithReadonlyFinalClass` (4140) — cannot extend final class for the anonymous wrapper

3. **`$obj with [...]`** (in-place mutation) — readonly property rules:
   - **Always blocked** on all PHP versions: emit `MessageCode.CheckerWithReadonlyInPlace` (4141) — "Cannot modify readonly property '{0}' with in-place 'with'; use 'clone ... with' or 'new ... with' instead." Since the object already exists, there is no safe way to reinitialize readonly properties from outside the class scope.
- If the target is a class: the class must allow property assignment (the property is not readonly, OR the assignment is inside the constructor of the owning class)
- If the target is a struct: all named properties must exist in the struct definition
- The result type is the same as the target type

#### 6.6 Disposable Validation

When checking a disposable assignment (`$resource := $expr`):
- The right-hand side expression type must implement the `IsDisposable` interface → `MessageCode.CheckerDisposableRequiresInterface` (4030)
- Mark the variable as `IsDisposable = true` in the `VariableState`
- The checker should verify that disposable variables go out of scope properly (the emitter handles the actual `DisposableScope` generation for auto-dispose via `__destruct()`)
- For `AsyncIsDisposable`: the `disposeAsync()` method should exist

**Circular reference detection for disposable scopes:**

The checker performs two circular reference analyses on disposable scopes to inform the emitter (Story 11) which disposal strategy to use:

1. **Closure-captures-`$this` detection:** When a closure captures `$this` (explicitly or implicitly) and is stored as a property on the same object, the checker flags the closure for `WeakReference` emission. This is not an error — it is a flag stored on the symbol that the emitter reads to generate `$__weakSelf = \WeakReference::create($this)` instead of letting the closure capture `$this` directly. This breaks the most common circular reference pattern (closure → `$this` → property → closure) that would otherwise delay `DisposableScope.__destruct()`.

2. **Bidirectional object reference detection:** When the checker detects bidirectional references between disposable objects that cannot be resolved by `WeakReference` (e.g., parent holds a strong reference to child, child holds a strong reference to parent, and both implement `IsDisposable`), the checker flags the enclosing disposable scope for try/finally fallback and emits a warning to the developer. The emitter reads this flag and generates try/finally instead of `DisposableScope` for that specific scope.

These flags are stored on the scope/symbol metadata and read by the emitter (Story 11) to determine which disposal strategy to use: `DisposableScope` (default), `WeakReference`-augmented `DisposableScope` (for closure captures), or try/finally fallback (for unresolvable circular references).

When checking `using()` function calls:
- The first argument must implement `IsDisposable`
- The second argument must be a callable

#### 6.7 Async/Await Validation

When checking an `async` function declaration:
- The return type must be `Promise<T>` (or `void` for fire-and-forget async functions)
- The function body may contain `await` expressions
- Set `IsInAsyncContext = true` in the child `CheckerState`

When checking an `await` expression:
- Must be inside an async function → `MessageCode.CheckerAwaitOutsideAsync` (4028)
- The operand must be of type `Promise<T>` or `AsyncIterable<T>`
- If the operand is `Promise<T>`, the result type is `T` (the resolved value type)
- If the operand is `AsyncIterable<T>`, the `await` is only valid as the expression of a `foreach` statement (see Phase 4, Foreach statement validation). Using `await` on an `AsyncIterable<T>` outside of a `foreach` is a type error.

When checking a function call to an `async` function:
- The return type is `Promise<T>` (the caller gets a Promise, not the resolved value)
- If the call is preceded by `await`, the type is `T`

**Async foreach validation (see also Phase 4 Foreach statement):**

When the checker encounters `foreach (await $expr as $item)`:
1. Verify `IsInAsyncContext = true`, otherwise report `CheckerAwaitOutsideAsync` (4028)
2. Infer the type of `$expr` (the expression after `await`)
3. **If `$expr` is `AsyncIterable<T>`:** This is true async iteration.
   - The loop variable `$item` has type `T`
   - The emitter will desugar to a while-loop with `_await()` calls on `next()` and `current()`
4. **If `$expr` is `Promise<Iterable<T>>`:** This is resolve-then-iterate.
   - The Promise is resolved first (via `_await()`), producing an `Iterable<T>`
   - The loop variable `$item` has type `T`
   - The emitter resolves the Promise then uses a standard `foreach`
5. **If `$expr` is `Promise<AsyncIterable<T>>`:** Composition of both.
   - The Promise is resolved first, producing `AsyncIterable<T>`
   - Then async iteration proceeds as in case 3
6. **If `$expr` is none of the above:** Report a type error.

When the checker encounters `foreach ($expr as $item)` WITHOUT `await`:
- If `$expr` has type `AsyncIterable<T>`, report an error: "Cannot iterate `AsyncIterable<T>` synchronously; use `foreach (await $expr as ...)` inside an async function"

#### 6.8 Compile-Time Construct Validation

When checking compile-time constructs hardcoded in the binder (Story 06, Phase 3):

**`nameof(reference)`:**
- The argument must be a valid symbol reference (variable, class, method, property, constant)
- The argument must be resolvable in the current scope
- The result type is `string` (a string literal at compile time)

**`typeof(typeReference)`:**
- The argument must be a valid type name or typed expression
- The result type is `\Tyhp\Type`

**`default(typeName)`:**
- The argument must be a valid type name
- The result type depends on the argument:
  - `default(int)` → `int` (value `0`)
  - `default(string)` → `string` (value `''`)
  - `default(bool)` → `bool` (value `false`)
  - `default(?T)` → `T|null` (value `null`)
  - `default(array)` → `array` (value `[]`)
  - `default(float)` → `float` (value `0.0`)
  - For nullable types: always `null`

**`variable_exists(varName)`:**
- The argument must be a string literal matching a variable name
- The result type is `bool`
- This is a compile-time check, not runtime

#### 6.9 Dynamic Language Feature Validation

Tyhp restricts certain PHP dynamic features and validates string values used in dynamic contexts:

**Dynamic class instantiation:** `new $className()` — if `$className` is a string literal or compile-time constant, validate it resolves to an existing class. If it's a runtime string, emit a warning about potential type safety issues.

**Dynamic method calls:** `$obj->$methodName()` — if `$methodName` is a string literal, validate it matches a method on the resolved type of `$obj`. If it's a runtime string, emit a warning.

**Dynamic function calls:** `$functionName()` — if `$functionName` is a string literal, validate it resolves to an existing function. If it's a runtime string, emit a warning.

**Dynamic property access:** `$obj->$propName` — if `$propName` is a string literal, validate it matches a property on the resolved type. If it's a runtime string, emit a warning.

For each: if the string variable is a compile-time-known value (literal, constant, or narrowed via type guard), perform full validation. Otherwise, emit a diagnostic suggesting the developer ensure type safety.

#### 6.10 Function/Method Overload Validation

When a function or method has overload signatures (multiple declared signatures before the implementation):
- Each overload signature must be a valid subset of the implementation signature
- The implementation signature must be able to handle all overload variants
- Overload signatures must be distinguishable (different parameter types or counts)
- Report `MessageCode.CheckerOverloadSignatureIncompatible` (4118) for incompatible overloads

#### 6.11 Trait Requirement Validation

Tyhp extends trait syntax with `extends` and `implements` requirements:

**`trait MyTrait extends BaseClass`:**
- When a class `use`s this trait, the class must extend `BaseClass` (directly or indirectly) → `MessageCode.CheckerTraitRequirementNotMet` (4044)

**`trait MyTrait implements SomeInterface`:**
- When a class `use`s this trait, the class must implement `SomeInterface` → `MessageCode.CheckerTraitRequirementImplNotMet` (4045)

Implementation: When checking a class that uses traits, iterate over used traits and verify each trait's `extends`/`implements` requirements are satisfied by the using class.

#### 6.12 Property Accessor Validation

When checking property accessor declarations (get/set hooks):
- Accessor type consistency: if a property has both `get` and `set`, the types must be consistent
- Accessor visibility cannot be more visible than the property → existing `MessageCode.CheckerAccessorVisibilityCannotBeMoreVisibleThanProperty` (4004)
- `get` accessor must return a value compatible with the property type
- `set` accessor parameter type must match the property type
- Accessor body validation (same as method body checking)

#### 6.13 Restricted PHP Feature Detection

Tyhp restricts certain PHP features. Report diagnostics when they are used:

**`eval()` usage:**
- Disabled by default in Tyhp → `MessageCode.CheckerEvalUsage` (4800) as informational diagnostic
- Can be re-enabled via `build.allowEval` configuration (// PLACEHOLDER_STORY_10: read from config)

**`include`/`require`/`include_once`/`require_once` usage:**
- Not allowed in Tyhp → `MessageCode.CheckerIncludeNotAllowed` (4801) as an error
- Tyhp uses `import` statements instead
- Exception: `require_once` for the Composer autoloader (`vendor/autoload.php`) in entry point files (configurable)

**`extract()` / `compact()` usage:**
- These dynamically create/collect variables, which breaks static analysis
- Emit a warning

**Deprecation/obsolescence checking:**
- When any symbol marked `IsDeprecated` is referenced, emit `MessageCode.CheckerDeprecatedUsage` (4500) as a warning
- When any symbol marked `IsObsolete` is referenced, emit `MessageCode.CheckerObsoleteUsage` (4501) as an error

#### 6.14 Attribute Validation (`Rules/AttributeRule.cs`)

**New rule class.**

**Attribute class validation:**
- The class used as an attribute must be declared with `#[Attribute]` → `MessageCode.CheckerNotAnAttributeClass` (4126)
- Validate the `Attribute::TARGET_*` flags match the usage context (e.g., a class-only attribute used on a function) → `MessageCode.CheckerAttributeTargetMismatch` (4127)

**Attribute argument validation:**
- Attribute arguments must satisfy the attribute class constructor parameter types → `CheckerIncompatibleArgumentType` (4010)
- Attribute arguments must be constant expressions (same rules as §4.13) → `CheckerNonConstantExpression` (4090)

**Repeatable attributes:**
- If the same attribute is applied multiple times and the attribute is not marked as repeatable (`Attribute::IS_REPEATABLE`) → `MessageCode.CheckerAttributeNotRepeatable` (4128)

**Built-in attribute recognition:**
- `#[Deprecated]` / `#[Obsolete]` — set `IsDeprecated` / `IsObsolete` on the target symbol for `DeprecationRule` to consume.
- `#[Override]` — validate the method actually overrides a parent method → `MessageCode.CheckerOverrideNotOverriding` (4129)
- `#[SensitiveParameter]` — informational, no check needed.
- `#[AllowDynamicProperties]` — relax dynamic property restriction for this class.

#### 6.15 Import and Namespace Validation (`Rules/ImportRule.cs`)

**New rule class.**

**Unused imports (warning):**
- A `use` statement that is never referenced in the file → `MessageCode.CheckerUnusedImport` (4130) (warning)
- Applies to: `use ClassName`, `use function functionName`, `use const CONST_NAME`

**Duplicate imports:**
- Two `use` statements importing the same fully-qualified name → `MessageCode.CheckerDuplicateImport` (4131) (warning)

**Conflicting aliases:**
- Two `use` statements with the same alias → `MessageCode.CheckerConflictingImportAlias` (4132) (error). E.g., `use Foo\Bar as Baz; use Qux\Bar as Baz;`

**Group use validation:**
- `use Foo\{Bar, Baz, Bar}` — duplicate in group use → `CheckerDuplicateImport` (4131)

#### 6.16 Variable and Dynamic Feature Restrictions (`Rules/RestrictedFeatureRule.cs`)

Expand the existing restricted feature rule with Tyhp-specific prohibitions:

**Variable variables prohibited:**
- `$$var` or `${$expr}` — variable variables break static analysis → `MessageCode.CheckerVariableVariableProhibited` (4133) (error)

**Dynamic property creation prohibited:**
- Setting a property that doesn't exist on the class (without `#[AllowDynamicProperties]`) → `MessageCode.CheckerDynamicPropertyProhibited` (4134) (error)
- PHP 8.2 deprecated dynamic properties; Tyhp makes this an error.

**`compact()`/`extract()` prohibition:**
- These are already listed as warnings — upgrade to errors in Tyhp.
- `compact()` creates an array from variable names (breaks type tracking) → `MessageCode.CheckerCompactProhibited` (4135)
- `extract()` creates variables from array keys (breaks type tracking) → `MessageCode.CheckerExtractProhibited` (4136)

**Global variables restriction:**
- `global $var` — while technically supported for PHP interop, emit a warning in Tyhp code → `MessageCode.CheckerGlobalVariableWarning` (4137) (warning). Prefer dependency injection.

**Unset on typed variables:**
- `unset($x)` where `$x` has a declared type — after `unset`, the variable is undefined. If it's subsequently used, `CheckerVariablePossiblyUndefined` (4014) applies.
- `unset($obj->readonlyProp)` — cannot unset readonly property → `CheckerReadonlyPropertyReassigned` (4021)

#### 6.17 Code Quality Diagnostics (`Rules/CodeQualityRule.cs`)

**New rule class.** These are all warnings/informational — they don't prevent compilation but help developers write better code.

**Unused variables:**
- A variable that is assigned but never read → `MessageCode.CheckerUnusedVariable` (4200) (warning)
- Exception: variables named `$_` or starting with `$_` are intentionally unused.
- Exception: loop variables in `foreach` key position.

**Unused parameters:**
- A function/method parameter that is never used in the body → `MessageCode.CheckerUnusedParameter` (4201) (warning)
- Exception: method overrides/implementations (the parameter may be unused in this implementation but required by the interface/parent signature).
- Exception: parameters in abstract methods (no body to check).

**Unused private members:**
- A `private` method, property, or constant that is never referenced → `MessageCode.CheckerUnusedPrivateMember` (4202) (warning)
- Does not apply to `protected` or `public` (may be used externally).

**Assignment in condition:**
- `if ($x = getValue())` — assignment where comparison was likely intended → `MessageCode.CheckerAssignmentInCondition` (4203) (warning)
- Suppress with extra parentheses: `if (($x = getValue()))` (intentional pattern).

**Condition always true/false (when statically determinable):**
- `if (true)`, `while (false)`, `if (1 === 1)` → `MessageCode.CheckerConditionAlwaysTrueFalse` (4204) (warning)
- This only catches trivially obvious cases, not arbitrary expressions.

**Redundant type cast:**
- `(int) $x` where `$x` is already `int` → `MessageCode.CheckerRedundantCast` (4205) (warning)
- `(string) $s` where `$s` is already `string` → same

**Dead store:**
- A variable is assigned but then reassigned before being read → `MessageCode.CheckerDeadStore` (4206) (warning)
- This indicates the first assignment's value is wasted.

**Unnecessary null check:**
- `$x !== null` where `$x` is a non-nullable type → `MessageCode.CheckerUnnecessaryNullCheck` (4207) (warning)
- `$x === null` where `$x` is non-nullable → same, and the condition is always false.

**Unreachable match/switch arm:**
- A match/switch arm that can never be reached because a previous arm already covers it → `MessageCode.CheckerUnreachableArm` (4208) (warning)

### Acceptance Criteria

- [ ] Class-level operator overload declarations validate: correct parameter count, at least one `self` parameter, correct return type, valid operator
- [ ] Extension operator overloads validate: `<Type>` present and resolves, at least one `self` parameter
- [ ] Tyhpdef inline extension operators validate: no `<Type>`, `self` resolves to enclosing class
- [ ] Tyhpdef inline extension functions validate: `$this` resolves to enclosing class, public-only member access
- [ ] Operator overload usage resolves to the correct overload (class-level or extension) and uses its return type
- [ ] Extension method declarations validate: public, static, first parameter has `extends` keyword
- [ ] Extension method calls resolve correctly and check argument types
- [ ] Tyhpdef `use extension` validates: extension exists, conflicts detected and reported, adaptations processed
- [ ] Type aliases are expanded recursively during type checking
- [ ] `with` keyword validates property existence and value type compatibility for all three forms (`clone with`, `new with`, in-place `with`)
- [ ] `clone ... with` on readonly: allowed on PHP 8.5+; requires `build.experimentalReadonlyCloneWith` on PHP 8.2-8.4; blocked on `final` classes
- [ ] `new ... with` on readonly: allowed on PHP 8.5+; always allowed on PHP 8.2-8.4 for non-final classes; blocked on `final` classes
- [ ] In-place `$obj with [...]` on readonly: always blocked with `CheckerWithReadonlyInPlace` (4141)
- [ ] Disposable `:=` validates the right-hand side implements `IsDisposable`
- [ ] `await` is only allowed inside async functions
- [ ] `await` on a `Promise<T>` produces type `T`
- [ ] `foreach (await $asyncIterable as $item)` validates that `$asyncIterable` implements `AsyncIterable<T>` and infers `$item` as type `T`
- [ ] `foreach (await $promise as $item)` where `$promise` is `Promise<Iterable<T>>` resolves to synchronous iteration with `$item` as type `T`
- [ ] `foreach ($asyncIterable as $item)` without `await` on an `AsyncIterable<T>` produces error `CheckerAsyncIterableMissingAwait` (4046)
- [ ] `foreach (await $expr as $item)` where `$expr` is not `AsyncIterable<T>` or `Promise<Iterable<T>>` produces error `CheckerAwaitNonAsyncIterable` (4047)
- [ ] `foreach (await $expr as $item)` outside an async function produces `CheckerAwaitOutsideAsync` (4028)
- [ ] `nameof()` validates the argument is a resolvable symbol
- [ ] `typeof()` validates the argument is a valid type reference
- [ ] `default()` returns the correct default type for each input type
- [ ] Dynamic feature usage (dynamic class/method/property names) warns when not using proper named types
- [ ] Function overload signatures are validated against the implementation signature
- [ ] Trait `extends`/`implements` requirements are checked at the use site
- [ ] Property accessor types are validated for consistency
- [ ] `eval()` usage produces an informational diagnostic
- [ ] `include`/`require` usage produces an error diagnostic
- [ ] Deprecated symbol references produce warnings
- [ ] **Attributes:** non-attribute class used as attribute reports error; attribute target mismatch reports error; non-repeatable attribute used twice reports error; `#[Override]` on non-overriding method reports error
- [ ] **Imports:** unused import produces warning; duplicate import produces warning; conflicting alias produces error
- [ ] **Variable restrictions:** variable variables (`$$var`) produce error; dynamic property creation without `#[AllowDynamicProperties]` produces error; `compact()`/`extract()` produce error
- [ ] **Code quality:** unused variable produces warning; unused parameter produces warning (except in overrides); unused private member produces warning; assignment in condition produces warning; always-true/false condition produces warning; redundant cast produces warning; dead store produces warning; unnecessary null check on non-nullable produces warning
- [ ] Each rule class file stays under 500 lines. When a file exceeds 500 lines, split it into additional rule classes.

### Dependencies

- **Requires:** Phase 4 (Tier 1 checks provide the declaration/expression checking infrastructure), Phase 3 (TypeComparer for advanced type comparisons), Phase 5 (type narrowing for dynamic feature checks), Story 06 (built-in types, utility types, compile-time constructs, `IsDisposable` from `tyhp/core`, `Promise<T>` from `tyhp/async`)
- **Provides:** Complete Tyhp language feature validation; ready for pipeline integration in Phase 7

---

## Phase 7: Pipeline Integration, Configuration, and Validation




### Phase Overview

Wire the completed checker into the compilation pipeline (`CompilationService`, `BuildAction`, `LintAction`), replace the `// PLACEHOLDER_STORY_08` markers from Stories 01 and 02, connect checker behavior to configuration options, and validate the entire checker against the existing example files.

### Deliverables

- Updated `Tyhp/Domain/Services/CompilationService.cs` — Call `TyhpChecker.Check()` after binding
- Updated `Tyhp/CLI/BuildAction.cs` — Checker placeholder replaced with actual invocation
- Updated `Tyhp/CLI/LintAction.cs` — Checker invocation (lint = parse + bind + check, no emit)
- Updated `Tyhp/Config/Project.cs` — Checker configuration option parsing (where possible; some depend on Story 10)
- Validation results from running the checker on all `Examples/*.tyhp` and `Examples/*.php` files
- Updated `.resx` resource files with all new `MessageCode` message strings

### Implementation Details

#### 7.1 Update `CompilationService` to Include Checking

**File:** `Tyhp/Domain/Services/CompilationService.cs`

After the bind step completes and `CompilationResult.GlobalScope` is populated:

1. Check `result.Diagnostics.HasErrors` — the pipeline enforces strict checkpoints. If the parser produces errors, binding is skipped. If the binder produces errors, the checker is skipped. Each stage only runs if the previous stage completed without errors. This prevents cascading false positives and ensures each stage operates on valid input.
2. Instantiate `TyhpChecker` with `result.Diagnostics`, `symbolTree` (from binder), `result.GlobalScope`
3. Call `checker.Check(result.ParsedFiles)`
4. Record `CompilationResult.CheckDuration`

The checker adds diagnostics directly to the shared `DiagnosticBag` on `CompilationResult`, so no result collection is needed — the diagnostics are already aggregated.

#### 7.2 Update `BuildAction`

**File:** `Tyhp/CLI/BuildAction.cs`

Replace the checker placeholder:

```csharp
// BEFORE:
// PLACEHOLDER_STORY_08: Run checker — log "Checker not yet implemented, skipping"

// AFTER:
var checkStart = Stopwatch.GetTimestamp();
var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope);
checker.Check(result.ParsedFiles);
result.CheckDuration = Stopwatch.GetElapsedTime(checkStart);
Message.Info($"Check complete: {result.Diagnostics.ErrorCount} errors, {result.Diagnostics.WarningCount} warnings ({result.CheckDuration.TotalMilliseconds:F0}ms)");
```

After checking:
- If `result.Diagnostics.HasErrors`: display errors, skip emitter, exit with `ExitCode.CompileError`
- If warnings only: display warnings, continue to emitter (or exit with `ExitCode.CompileWarning` in strict mode)
- Display check timing in the phase summary

#### 7.3 Update `LintAction`

**File:** `Tyhp/CLI/LintAction.cs`

The lint action runs parse → bind → check, skipping the emitter. After Story 08, the lint action becomes fully functional (minus emit):

1. Parse files via `CompilationService`
2. Bind via `TyhpBinder`
3. Check via `TyhpChecker`
4. Display all diagnostics
5. Exit with appropriate exit code

The lint action is the primary way to exercise the checker without needing the emitter.

#### 7.4 Connect Checker Configuration Options

Some checker behaviors are configurable. Read settings from `Project` configuration where available:

| Config Option | Checker Behavior | Default |
|--------------|-----------------|---------|
| ~~`checker.allowUncheckedMixed`~~ | **Superseded 2026-07-24: removed.** Narrowing `mixed` is unconditional — see Story 10 notes. Do not re-add. | — |
| ~~`checker.strictNullChecks`~~ | **Superseded 2026-07-24: removed.** Null safety is unconditional. Do not re-add. | — |
| ~~`checker.noImplicitAny`~~ | **Superseded 2026-07-24: removed.** Required annotations / inferable initializers are unconditional. Do not re-add. | — |
| `build.allowEval` | If true, `eval()` usage does not produce a diagnostic | `false` |

Use the following hardcoded defaults until Story 10 provides configuration parsing:

| Config Option | Default Value |
|--------------|---------------|
| `build.allowEval` | `false` |
| `checker.maxErrorsPerFile` | `100` |

Mark each with:
```csharp
// PLACEHOLDER_STORY_10: Read build.allowEval from Project config
bool allowEval = false;
```

#### 7.5 Add All Message Strings to Resource Files

**File:** `Resources/CLI.TyhpHostedService.resx`

Add `.resx` entries for all new `MessageCode` values added in Phase 1 (section 1.6):

```xml
<!-- Type compatibility -->
<data name="ERROR_TYHP4008"><value>Cannot assign type '{0}' to type '{1}'</value></data>
<data name="ERROR_TYHP4009"><value>Return type '{0}' is not compatible with declared return type '{1}'</value></data>
<data name="ERROR_TYHP4010"><value>Argument of type '{0}' is not assignable to parameter of type '{1}'</value></data>
<data name="ERROR_TYHP4011"><value>Function with return type '{0}' must return a value on all code paths</value></data>
<data name="WARNING_TYHP4012"><value>Unreachable code detected</value></data>

<!-- Variable errors -->
<data name="ERROR_TYHP4013"><value>Variable '${0}' is used before being assigned</value></data>
<data name="ERROR_TYHP4014"><value>Variable '${0}' is possibly undefined</value></data>
<data name="WARNING_TYHP4015"><value>Variable '${0}' is possibly null</value></data>
<data name="ERROR_TYHP4016"><value>Variable '${0}' must have a type annotation or inferable initializer</value></data>

<!-- Class/interface errors -->
<data name="ERROR_TYHP4017"><value>Class '{0}' does not implement abstract method '{1}' from '{2}'</value></data>
<data name="ERROR_TYHP4018"><value>Class '{0}' does not implement interface method '{1}' from '{2}'</value></data>
<data name="ERROR_TYHP4019"><value>Cannot extend final class '{0}'</value></data>
<data name="ERROR_TYHP4020"><value>Cannot override final method '{0}'</value></data>
<data name="ERROR_TYHP4021"><value>Cannot assign to readonly property '{0}'</value></data>
<data name="ERROR_TYHP4022"><value>Cannot instantiate abstract class '{0}'</value></data>

<!-- Enum errors -->
<data name="ERROR_TYHP4023"><value>Enum case value type '{0}' does not match backed type '{1}'</value></data>
<data name="ERROR_TYHP4024"><value>Enum cannot have a constructor</value></data>

<!-- Visibility errors -->
<data name="ERROR_TYHP4025"><value>'{0}' is {1} and cannot be accessed from '{2}'</value></data>

<!-- Control flow errors -->
<data name="ERROR_TYHP4026"><value>'break' statement is not within a loop or switch</value></data>
<data name="ERROR_TYHP4027"><value>'continue' statement is not within a loop</value></data>
<data name="ERROR_TYHP4028"><value>'await' can only be used inside an async function</value></data>

<!-- Operator errors -->
<data name="ERROR_TYHP4029"><value>Operator '{0}' cannot be applied to types '{1}' and '{2}'</value></data>

<!-- Tyhp-specific errors -->
<data name="ERROR_TYHP4030"><value>Disposable assignment ':=' requires type implementing 'IsDisposable'</value></data>
<data name="ERROR_TYHP4031"><value>Property '{0}' does not exist on type '{1}'</value></data>
<data name="ERROR_TYHP4032"><value>Type guard function must return bool</value></data>
<data name="ERROR_TYHP4033"><value>Extension method '{0}' must be static</value></data>
<data name="ERROR_TYHP4034"><value>Extension method '{0}' must be public</value></data>
<data name="ERROR_TYHP4035"><value>Type '{0}' does not satisfy constraint '{1}'</value></data>
<data name="ERROR_TYHP4036"><value>Generic type '{0}' expects {1} type argument(s), but {2} were provided</value></data>
<data name="ERROR_TYHP4037"><value>All struct properties must be typed</value></data>
<!-- NOTE: TYHP4038 (CheckerExtensionVisibilityNotAllowed) already ships in Resources; Story 08 does NOT re-add it. The overload message now lives at TYHP4118 (below). -->
<data name="ERROR_TYHP4118"><value>Overload signature is not compatible with implementation signature</value></data>
<data name="ERROR_TYHP4039"><value>'throw' expression must be an instance of \Throwable</value></data>
<data name="ERROR_TYHP4139"><value>Clone 'with' on readonly property '{0}' requires 'build.experimentalReadonlyCloneWith: true' in tyhp.json for PHP &lt; 8.5</value></data>
<data name="ERROR_TYHP4140"><value>Cannot use 'with' on readonly properties of final class '{0}' on PHP &lt; 8.5</value></data>
<data name="ERROR_TYHP4141"><value>Cannot modify readonly property '{0}' with in-place 'with'; use 'clone ... with' or 'new ... with' instead</value></data>
<data name="ERROR_TYHP4142"><value>Missing required argument for parameter '${0}' of '{1}'</value></data>
<data name="ERROR_TYHP4143"><value>Too many arguments passed to '{0}'; expected at most {1}, got {2}</value></data>
<data name="ERROR_TYHP4040"><value>Caught type '{0}' must implement \Throwable</value></data>
<data name="ERROR_TYHP4041"><value>Catch clause cannot use intersection types</value></data>
<data name="ERROR_TYHP4042"><value>Cannot catch scalar type '{0}'</value></data>
<data name="ERROR_TYHP4043"><value>Condition must be of type 'bool', got '{0}'</value></data>
<data name="ERROR_TYHP4044"><value>Trait '{0}' requires the using class to extend '{1}'</value></data>
<data name="ERROR_TYHP4045"><value>Trait '{0}' requires the using class to implement '{1}'</value></data>
<data name="ERROR_TYHP4046"><value>Cannot iterate 'AsyncIterable&lt;{0}&gt;' synchronously; use 'foreach (await $expr as ...)' inside an async function</value></data>
<data name="ERROR_TYHP4047"><value>'await' in foreach requires 'AsyncIterable&lt;T&gt;' or 'Promise&lt;Iterable&lt;T&gt;&gt;', got '{0}'</value></data>
<data name="ERROR_TYHP4048"><value>Type 'void' can only be used as a return type or in generic positions that explicitly allow it via constraint</value></data>
<data name="ERROR_TYHP4049"><value>Type 'never' can only be used as a return type or in generic positions that explicitly allow it via constraint</value></data>
<data name="ERROR_TYHP4050"><value>Key '{0}' does not match a property on type '{1}'</value></data>
<data name="ERROR_TYHP4051"><value>Utility type argument does not satisfy constraint</value></data>
<data name="WARNING_TYHP4052"><value>Reference parameter '{0}' reassigned to type '{1}', which differs from declared type '{2}'</value></data>
<data name="ERROR_TYHP4053"><value>Type '{0}' appears more than once in a union or intersection type</value></data>
<data name="ERROR_TYHP4138"><value>Cannot infer type for closure parameter '${0}'; provide an explicit type annotation</value></data>
<data name="ERROR_TYHP4054"><value>'mixed' or 'never' cannot be used in union or intersection types</value></data>
<data name="ERROR_TYHP4055"><value>Redundant type '{0}' in union type</value></data>
<data name="ERROR_TYHP4056"><value>Use 'bool' instead of 'true|false'</value></data>
<data name="ERROR_TYHP4057"><value>Non-class type '{0}' cannot appear in intersection types</value></data>
<data name="ERROR_TYHP4058"><value>'callable' cannot be used as a property type declaration</value></data>
<data name="ERROR_TYHP4059"><value>'void' can only be used as a return type</value></data>
<data name="WARNING_TYHP4060"><value>Returning by reference from a void function is deprecated</value></data>
<data name="ERROR_TYHP4061"><value>'never' can only be used as a return type</value></data>
<data name="ERROR_TYHP4062"><value>'resource' cannot be used in user type declarations</value></data>
<data name="ERROR_TYHP4063"><value>By-reference argument must be a variable, not a literal or expression</value></data>
<data name="ERROR_TYHP4064"><value>'self' or 'parent' used outside class context</value></data>
<data name="ERROR_TYHP4065"><value>'parent' used in class '{0}' that has no parent class</value></data>
<data name="ERROR_TYHP4066"><value>'static' can only be used as a return type</value></data>
<data name="ERROR_TYHP4067"><value>Redundant intersection in DNF type</value></data>
<data name="ERROR_TYHP4068"><value>Function with 'never' return type must not contain a return statement</value></data>
<data name="ERROR_TYHP4069"><value>Cannot instantiate non-class type '{0}'</value></data>
<data name="ERROR_TYHP4070"><value>Cannot instantiate trait '{0}'</value></data>
<data name="ERROR_TYHP4071"><value>Cannot instantiate interface '{0}'</value></data>
<data name="ERROR_TYHP4072"><value>Cannot instantiate enum '{0}'</value></data>
<data name="ERROR_TYHP4073"><value>'clone' cannot be applied to non-object type '{0}'</value></data>
<data name="ERROR_TYHP4074"><value>Magic method '{0}' has invalid signature: {1}</value></data>
<data name="ERROR_TYHP4075"><value>Duplicate parameter name '${0}'</value></data>
<data name="WARNING_TYHP4076"><value>Required parameter '${0}' follows optional parameter</value></data>
<data name="ERROR_TYHP4077"><value>Variadic parameter must be the last parameter</value></data>
<data name="ERROR_TYHP4078"><value>Variadic parameter cannot have a default value</value></data>
<data name="ERROR_TYHP4079"><value>Duplicate named argument '{0}'</value></data>
<data name="ERROR_TYHP4080"><value>Positional argument after named argument</value></data>
<data name="ERROR_TYHP4081"><value>Named argument '{0}' does not match any parameter</value></data>
<data name="ERROR_TYHP4082"><value>Named argument after argument unpacking</value></data>
<data name="ERROR_TYHP4083"><value>Variable '${0}' in closure 'use' clause is not defined in the enclosing scope</value></data>
<data name="WARNING_TYHP4084"><value>'use($this)' is redundant in non-static closures</value></data>
<data name="ERROR_TYHP4085"><value>Static closure cannot reference '$this'</value></data>
<data name="ERROR_TYHP4086"><value>'yield' can only be used inside a generator function</value></data>
<data name="ERROR_TYHP4087"><value>Generator function return type must be 'Generator', 'iterable', or 'Iterator'</value></data>
<data name="ERROR_TYHP4088"><value>'yield' inside a 'finally' block is not allowed</value></data>
<data name="ERROR_TYHP4089"><value>'yield from' expression must be iterable or a Generator</value></data>
<data name="ERROR_TYHP4090"><value>Non-constant expression in constant-required context</value></data>
<data name="ERROR_TYHP4091"><value>Division by zero in constant expression</value></data>
<data name="WARNING_TYHP4092"><value>Duplicate array key '{0}'</value></data>
<data name="ERROR_TYHP4093"><value>Cannot use array access on type '{0}'</value></data>
<data name="ERROR_TYHP4094"><value>Cannot destructure non-array type '{0}'</value></data>
<data name="ERROR_TYHP4095"><value>Spread operator in list/destructuring is not allowed</value></data>
<data name="ERROR_TYHP4096"><value>Spread operator requires iterable type, got '{0}'</value></data>
<data name="ERROR_TYHP4097"><value>'$this' cannot be used in static context</value></data>
<data name="ERROR_TYHP4098"><value>Non-static method '{0}' cannot be called statically</value></data>
<data name="WARNING_TYHP4099"><value>Static method '{0}' called on instance</value></data>
<data name="ERROR_TYHP4100"><value>'static::' used outside class context</value></data>
<data name="ERROR_TYHP4104"><value>'goto' is prohibited in Tyhp</value></data>
<data name="ERROR_TYHP4105"><value>Promoted constructor property must have a type annotation</value></data>
<data name="ERROR_TYHP4106"><value>Promoted properties are not allowed in abstract or interface constructors</value></data>
<data name="ERROR_TYHP4107"><value>Variadic parameter cannot be a promoted property</value></data>
<data name="ERROR_TYHP4108"><value>Mutable property '{0}' in readonly class</value></data>
<data name="ERROR_TYHP4109"><value>Static property in readonly class</value></data>
<data name="ERROR_TYHP4110"><value>Backed enum case '{0}' must have a value</value></data>
<data name="ERROR_TYHP4111"><value>Non-backed enum case '{0}' must not have a value</value></data>
<data name="ERROR_TYHP4112"><value>Duplicate enum case value '{0}'</value></data>
<data name="ERROR_TYHP4113"><value>Enums cannot have instance properties</value></data>
<data name="ERROR_TYHP4114"><value>Interface property cannot have an initializer</value></data>
<data name="ERROR_TYHP4115"><value>Interfaces cannot have instance property declarations</value></data>
<data name="ERROR_TYHP4116"><value>Unresolved trait method conflict for '{0}'</value></data>
<data name="ERROR_TYHP4117"><value>Circular trait use detected: {0}</value></data>
<data name="WARNING_TYHP4119"><value>Comparing types '{0}' and '{1}' has no meaningful comparison</value></data>
<data name="ERROR_TYHP4120"><value>String concatenation with non-stringable type '{0}'</value></data>
<data name="WARNING_TYHP4121"><value>Empty catch block silently swallows exceptions</value></data>
<data name="WARNING_TYHP4122"><value>Return statement in finally block overwrites try/catch return value</value></data>
<data name="WARNING_TYHP4123"><value>Break/continue in finally block</value></data>
<data name="WARNING_TYHP4124"><value>Exception type '{0}' is already caught by a previous catch clause</value></data>
<data name="WARNING_TYHP4125"><value>Catching parent exception '{0}' before child makes subsequent catch unreachable</value></data>
<data name="ERROR_TYHP4126"><value>Class '{0}' is not declared as an attribute class</value></data>
<data name="ERROR_TYHP4127"><value>Attribute '{0}' cannot be applied to {1}</value></data>
<data name="ERROR_TYHP4128"><value>Attribute '{0}' is not repeatable</value></data>
<data name="ERROR_TYHP4129"><value>Method '{0}' has #[Override] attribute but does not override a parent method</value></data>
<data name="WARNING_TYHP4130"><value>Unused import '{0}'</value></data>
<data name="WARNING_TYHP4131"><value>Duplicate import '{0}'</value></data>
<data name="ERROR_TYHP4132"><value>Conflicting import alias '{0}'</value></data>
<data name="ERROR_TYHP4133"><value>Variable variables ('$$var') are prohibited in Tyhp</value></data>
<data name="ERROR_TYHP4134"><value>Dynamic property creation is prohibited</value></data>
<data name="ERROR_TYHP4135"><value>'compact()' is prohibited in Tyhp</value></data>
<data name="ERROR_TYHP4136"><value>'extract()' is prohibited in Tyhp</value></data>
<data name="WARNING_TYHP4137"><value>'global $var' usage; prefer dependency injection</value></data>
<data name="WARNING_TYHP4200"><value>Variable '${0}' is assigned but never read</value></data>
<data name="WARNING_TYHP4201"><value>Parameter '${0}' is never used</value></data>
<data name="WARNING_TYHP4202"><value>Private member '{0}' is never referenced</value></data>
<data name="WARNING_TYHP4203"><value>Assignment in condition; use '===' for comparison or add extra parentheses if intentional</value></data>
<data name="WARNING_TYHP4204"><value>Condition is always {0}</value></data>
<data name="WARNING_TYHP4205"><value>Redundant cast to '{0}'</value></data>
<data name="WARNING_TYHP4206"><value>Value assigned to '${0}' is overwritten before being read</value></data>
<data name="WARNING_TYHP4207"><value>Unnecessary null check on non-nullable type '{0}'</value></data>
<data name="WARNING_TYHP4208"><value>Unreachable match/switch arm</value></data>
<data name="WARNING_TYHP4209"><value>Lossy cast from '{0}' to '{1}'</value></data>
<data name="INFO_TYHP4210"><value>Error threshold reached for this file; further errors are suppressed</value></data>
<data name="WARNING_TYHP4211"><value>Returning 'new self()' from a method with return type 'static' in non-final class '{0}'; child classes will receive parent instance</value></data>
<data name="WARNING_TYHP4500"><value>'{0}' is deprecated</value></data>
<data name="ERROR_TYHP4501"><value>'{0}' is obsolete and must not be used</value></data>
<data name="INFO_TYHP4800"><value>'eval()' usage detected — this is disabled in Tyhp by default</value></data>
<data name="ERROR_TYHP4801"><value>'include'/'require' is not allowed in Tyhp; use 'import' instead</value></data>
```

Ensure every `MessageCode` has a corresponding `.resx` entry. The message format strings should use `{0}`, `{1}`, etc. for parameters matching the `formatParams` passed to `DiagnosticBag.AddError()`.

#### 7.6 Validate with Example Files

Run the complete pipeline (parse → bind → check) on all files in `Examples/`:

| Example File | Expected Behavior |
|--------------|-------------------|
| `Examples/OperatorOverloads.tyhp` | Should validate operator overload declarations and usage |
| `Examples/PropertyAccessors.tyhp` | Should validate property accessor type consistency |
| `Examples/TypeGuards.tyhp` | Should validate type guard return types and narrowing |
| `Examples/WithKeyword.tyhp` | Should validate `with` property existence and types |
| `Examples/Structs.tyhp` | Should validate struct property typing and compatibility |
| `Examples/AsyncAwait.tyhp` | Should validate async/await context requirements |
| `Examples/Extensions.tyhp` | Should validate extension method declarations |
| `Examples/Generics.tyhp` | Should validate generic constraints and argument counts |
| `Examples/Disposables.tyhp` | Should validate disposable `:=` assignment |
| `Examples/StaticValueTypes.tyhp` | Should validate static value type constraints |
| All `Examples/*.php` | Should validate basic PHP type checking |

Expected outcome:
- The checker does not crash on any example file
- Valid code produces zero errors (may produce warnings for deprecation, etc.)
- Intentionally incorrect patterns produce the correct error diagnostics
- Performance is acceptable (checking all example files completes in under 10 seconds on the development machine)

#### 7.7 Ensure Placeholder Markers for Future Work

Verify the following cross-story placeholders exist:

**In checker code:**
- `// PLACEHOLDER_STORY_09: Emitter reads checker diagnostics for conditional emit paths` — add this in `CompilationService.cs` at the point where the emitter would be invoked
- `// PLACEHOLDER_STORY_10: Read checker configuration from Project config`
- `// PLACEHOLDER_STORY_12: Lint action output format (--format json/sarif)`
- `// PLACEHOLDER_STORY_07: Unit tests for checker`
- `// PLACEHOLDER_STORY_19: LSP integration — publishDiagnostics from checker`

**In pipeline code (from Stories 01/02):**
- Replace all `// PLACEHOLDER_STORY_08` markers with actual checker invocations

#### 7.8 Persist Narrowed Type Information

**Persist narrowed type information:** `CompilationResult.NarrowedTypes` **does not exist in the codebase today** — **Story 08 is the canonical producer that newly adds it here.** Add a `NarrowedTypes` property (`public IReadOnlyDictionary<IBase2Ast, ICheckedType> NarrowedTypes { get; set; }`) to `CompilationResult`. During type-narrowing passes, the checker populates this dictionary with the narrowed type for each AST node where narrowing occurs. This data is **consumed later** by Story 24's advanced optimizer (type-guard elimination) and by Story 11; those stories must list Story 08 as the source of `NarrowedTypes` in their prerequisites and must not redefine the property.

### Acceptance Criteria

- [ ] `CompilationService` calls `TyhpChecker.Check()` after binding and records `CheckDuration`
- [ ] `BuildAction` invokes the checker — the `// PLACEHOLDER_STORY_08` marker is replaced
- [ ] `LintAction` runs parse → bind → check and reports all diagnostics
- [ ] Running `tyhp build` on a project exercises the full pipeline through checking
- [ ] Running `tyhp lint` on a project reports checker diagnostics without emitting
- [ ] All `MessageCode` values have corresponding `.resx` message strings
- [ ] Checker configuration defaults are set (strict null checks, no implicit any, etc.)
- [ ] The checker does not crash on any of the 30 `Examples/` files
- [ ] Check duration is reported in the build/lint summary output
- [ ] Diagnostics include correct file name, line number, column number, and human-readable message
- [ ] The exit code is `CompileError` when checker errors are present, `CompileWarning` when only warnings, `Success` when clean
- [ ] Cross-story placeholder markers exist for Stories 09, 10, 12, 07, 19
- [ ] No regressions in existing functionality (parsing, AST caching, binding, debug action)
- [ ] `CompilationResult.NarrowedTypes` is populated during checking and contains narrowed type entries for all type guard branches

### Dependencies

- **Requires:** All previous phases (1-6) must be complete for full checker functionality. However, pipeline integration can begin as soon as Phase 1 is complete (the checker can be wired in with minimal checks and expanded incrementally)
- **Provides:** A fully functional type checker integrated into the compilation pipeline; diagnostic output for all Tyhp language features; the `tyhp lint` command becomes a useful developer tool

---

## Cross-Cutting Concerns

### Incremental Implementation Strategy

The checker is the largest single story in the Tyhp compiler TODO and will grow continuously as language features are added. The phased approach enables early pipeline integration:

1. **Phase 1** (core architecture) enables pipeline integration immediately — `TyhpChecker.Check()` can be called even when it only contains the dispatch skeleton
2. **Phase 2** (variable/type resolution) enables basic expression type computation
3. **Phase 3** (TypeComparer) enables any type compatibility check
4. **Phase 4** (Tier 1) makes the checker useful for standard PHP code
5. **Phase 5** (Tier 2) adds Tyhp's differentiating features
6. **Phase 6** (Tier 3) adds advanced features
7. **Phase 7** (integration) can start as early as Phase 1 and be expanded incrementally

**Recommendation:** Wire the checker into the pipeline after Phase 1. Each subsequent phase adds more checks, and the pipeline exercises them immediately. This provides continuous feedback and catches architectural issues early.

### Error Recovery Strategy

The checker must be resilient to errors from earlier phases:

1. **Unresolved symbols:** If the binder could not resolve a name (added `BinderSymbolNotFound` diagnostic), the checker should NOT add cascading "type mismatch" errors for expressions using that symbol. Check for `UnresolvedCheckedType` and skip further checks on those expressions.

2. **ErrorAst nodes:** When `CheckNode()` encounters an `ErrorAst`, return immediately without adding diagnostics. The visitor already reported the error.

3. **Partially bound AST:** If the binder partially succeeded (some files bound, others had errors), the checker should still check the successfully-bound files.

4. **Internal checker errors:** If the checker encounters an unexpected condition (e.g., an AST node type it doesn't recognize), it should log a `DiagnosticSeverity.Info` diagnostic with `MessageCode.CheckerUnknownError` (4001) and continue, NOT throw an exception.

### Performance Considerations

The checker walks the entire AST tree for every file. For large projects:

1. **Expression type caching:** The `_expressionTypes` dictionary avoids recomputing expression types when the same expression is checked multiple times (e.g., in nested conditions).

2. **Short-circuit on error:** The checker stops reporting errors for a file after `checker.maxErrorsPerFile` errors (default: 100). This prevents overwhelming output. After the threshold is reached, a single `MessageCode.CheckerErrorThresholdReached` (4210) info diagnostic is emitted for that file. Warnings are not counted toward the threshold.

3. **Parallel checking:** Parallel checking is out of scope for Story 08. Individual files are checked sequentially. Parallel checking is a future optimization tracked separately.

4. **Lazy type alias expansion:** Don't expand type aliases until they're needed. Cache expanded aliases to avoid repeated expansion.

### Placeholder Convention Reminder

**Within this implementation plan** — for future phases of the same plan:
```csharp
// PLACEHOLDER_PHASE_N: description of what goes here
```

**Cross-story references** — for work that belongs to a different TODO.md story:
```csharp
// PLACEHOLDER_STORY_N: description of what goes here
```

### File Organization Summary

New files created in this implementation:

```
Tyhp/TyhpLang/Checker/
├── TyhpChecker.cs                    (~200 lines — orchestrator, walks ASTs, dispatches to rules)
├── CheckerState.cs                   (~300 lines — redesigned scope state tracking)
├── VariableState.cs                  (~120 lines — per-variable tracking incl. reference groups)
├── ReferenceGroup.cs                 (~60 lines — reference alias group tracking)
├── ICheckedType.cs                   (~30 lines — checker type interface)
├── CheckedType.cs                    (~350 lines — concrete type representations)
├── TypeComparer.cs                   (~400 lines — type compatibility checking)
├── TypeInferrer.cs                   (~350 lines — type expression resolution and inference)
├── UtilityTypeResolver.cs            (~200 lines — \Tyhp\Readonly<T> etc. resolution)
├── Rules/
│   ├── ICheckerRule.cs               (~20 lines — rule interface)
│   ├── CheckerRuleRegistry.cs        (~80 lines — rule collection and dispatch)
│   ├── DeclarationRule.cs            (~400 lines — class, interface, trait, enum, function, property)
│   ├── ControlFlowRule.cs            (~350 lines — if, for, while, switch, try/catch, return)
│   ├── TypeCompatibilityRule.cs      (~400 lines — assignments, calls, operators, member access)
│   ├── TypeAnnotationRule.cs         (~200 lines — required type annotations)
│   ├── TypeDeclarationValidationRule.cs (~250 lines — PHP redundant/forbidden type combos)
│   ├── NullSafetyRule.cs             (~200 lines — non-nullable enforcement)
│   ├── TypeNarrowingRule.cs          (~300 lines — control flow narrowing, smart casts)
│   ├── ReferenceTrackingRule.cs      (~200 lines — pass-by-reference tracking)
│   ├── RelativeTypeRule.cs           (~150 lines — self/parent/static validation)
│   ├── GenericRule.cs                (~250 lines — generic constraint checks)
│   ├── CallableRule.cs               (~150 lines — callable restrictions, __invoke)
│   ├── StructRule.cs                 (~250 lines — struct validation, compatibility)
│   ├── OperatorOverloadRule.cs       (~200 lines — operator overload validation)
│   ├── ExtensionRule.cs              (~200 lines — extension method/operator validation)
│   ├── AsyncRule.cs                  (~200 lines — async/await context checks)
│   ├── DisposableRule.cs             (~200 lines — disposable := validation)
│   ├── CompileTimeRule.cs            (~150 lines — nameof/typeof/default/variable_exists)
│   ├── ClosureRule.cs                (~200 lines — closure/arrow use, static closure)
│   ├── DeprecationRule.cs            (~100 lines — deprecated symbol usage)
│   ├── RestrictedFeatureRule.cs      (~150 lines — eval/include/var vars/dynamic props)
│   ├── OverloadRule.cs               (~200 lines — function/method overload signatures)
│   ├── AttributeRule.cs              (~200 lines — attribute validation)
│   ├── ImportRule.cs                 (~150 lines — unused/duplicate imports)
│   └── CodeQualityRule.cs            (~250 lines — unused vars/params, dead stores, warnings)
└── readme.md                         (existing — expand with implementation notes)
```

Modified files:

```
Tyhp/Domain/Exceptions/MessageCode.cs — New checker codes (4008–4211 excluding the already-existing 4038, plus 4500–4501, 4800–4801)
Tyhp/Domain/Services/CompilationService.cs — Checker invocation after binding
Tyhp/CLI/BuildAction.cs — Replace PLACEHOLDER_STORY_08 with checker call
Tyhp/CLI/LintAction.cs — Replace PLACEHOLDER_STORY_08 with checker call
Resources/CLI.TyhpHostedService.resx — Message strings for all new codes
```

---

## Appendix A: Checker readme.md Notes (Existing)

The existing `Tyhp/TyhpLang/Checker/readme.md` contains a short checklist of things to check. These map to the implementation as follows:

| readme.md Item | Phase | Implementation Location |
|---------------|-------|------------------------|
| throw can only throw an instance of `\Throwable` | Phase 4 | `Rules/ControlFlowRule.cs` — throw statement check |
| catch can only catch real object types, not scalar/struct/enum | Phase 4 | `Rules/ControlFlowRule.cs` — try/catch check |
| catch can only catch types that extend `\Throwable` | Phase 4 | `Rules/ControlFlowRule.cs` — try/catch check |
| catch uses union types only, no intersection | Phase 4 | `Rules/ControlFlowRule.cs` — try/catch check |
| variable types, assignment and usage | Phase 2 + 4 | `VariableState`, `Rules/TypeCompatibilityRule.cs` |
| returns can only return the return type | Phase 4 | `Rules/ControlFlowRule.cs` — return check |
| function/methods with non-void return must return on all paths | Phase 4 | `Rules/DeclarationRule.cs` + `CheckerState.HasReturnedOnAllPaths` |
| logical statements can only be bool (different from PHP) | Phase 4 | `Rules/ControlFlowRule.cs` — condition checks |
| variable assignment before use | Phase 2 | `VariableState.IsDefinitelyAssigned` |
| override/implemented method compatibility | Phase 4 | `Rules/DeclarationRule.cs` — class/interface checks |
| Closure binding in class context | Phase 6 | `Rules/RestrictedFeatureRule.cs` — closure binding tracking |
| type declaration redundancy/forbidden combos | Phase 4 | `Rules/TypeDeclarationValidationRule.cs` |
| pass-by-reference parameter type tracking | Phase 4 | `Rules/ReferenceTrackingRule.cs` |
| self/parent/static resolution and validation | Phase 4 | `Rules/RelativeTypeRule.cs` |
| resource type not allowed in user declarations | Phase 4 | `Rules/TypeDeclarationValidationRule.cs` |
| callable not allowed on properties | Phase 4 | `Rules/TypeDeclarationValidationRule.cs` |
| void/never position restrictions | Phase 4 | `Rules/TypeDeclarationValidationRule.cs` |
| instantiate non-class (`new string()`) | Phase 4 | `Rules/TypeCompatibilityRule.cs` |
| magic method signatures | Phase 4 | `Rules/DeclarationRule.cs` |
| duplicate/ordering/variadic parameter validation | Phase 4 | `Rules/DeclarationRule.cs` |
| named argument validation | Phase 4 | `Rules/TypeCompatibilityRule.cs` |
| closure `use` variable validation | Phase 4 | `Rules/ClosureRule.cs` |
| static closure `$this` restriction | Phase 4 | `Rules/ClosureRule.cs` |
| yield/generator validation | Phase 4 | `Rules/ControlFlowRule.cs` |
| constant expression validation | Phase 4 | `Rules/DeclarationRule.cs` |
| array/list validation (duplicate keys, access) | Phase 4 | `Rules/TypeCompatibilityRule.cs` |
| `$this` in static context | Phase 4 | `Rules/TypeCompatibilityRule.cs` |
| goto label/jump validation | Phase 4 | `Rules/ControlFlowRule.cs` |
| constructor promoted property validation | Phase 4 | `Rules/DeclarationRule.cs` |
| readonly class all-readonly check | Phase 4 | `Rules/DeclarationRule.cs` |
| enum case values (backed/non-backed/uniqueness) | Phase 4 | `Rules/DeclarationRule.cs` |
| interface/trait additional validation | Phase 4 | `Rules/DeclarationRule.cs` |
| loose comparison (allowed, no warning; no type narrowing) | Phase 4 | `Rules/TypeCompatibilityRule.cs` |
| string concat with non-stringable | Phase 4 | `Rules/TypeCompatibilityRule.cs` |
| empty catch / duplicate catch / catch ordering | Phase 4 | `Rules/ControlFlowRule.cs` |
| clone on non-object | Phase 4 | `Rules/TypeCompatibilityRule.cs` |
| return/break in finally (warnings) | Phase 4 | `Rules/ControlFlowRule.cs` |
| attribute validation | Phase 6 | `Rules/AttributeRule.cs` |
| unused/duplicate imports | Phase 6 | `Rules/ImportRule.cs` |
| variable variables / dynamic properties | Phase 6 | `Rules/RestrictedFeatureRule.cs` |
| unused variables / parameters / private members | Phase 6 | `Rules/CodeQualityRule.cs` |
| assignment in condition | Phase 6 | `Rules/CodeQualityRule.cs` |
| dead stores / redundant casts / always-true conditions | Phase 6 | `Rules/CodeQualityRule.cs` |

---

## Appendix B: MessageCode Values Used by the Checker

### Existing Codes (from `MessageCode.cs`)

| Code | Name | Description |
|------|------|-------------|
| 4001 | `CheckerUnknownError` | Fallback for unexpected checker errors |
| 4002 | `CheckerMultipleVisibilities` | More than one visibility modifier on a member |
| 4003 | `CheckerNotAllowedMemberModifier` | Modifier not valid in this context |
| 4004 | `CheckerAccessorVisibilityCannotBeMoreVisibleThanProperty` | Accessor is more visible than its property |
| 4005 | `CheckerMemberModifierConflict` | Conflicting modifiers (e.g., abstract + final) |
| 4006 | `CheckerInvalidPropertyAccessorType` | Invalid accessor type |
| 4007 | `CheckerParameterNotAllowedOnPropertyAccessorType` | Parameter on wrong accessor type |
| 4038 | `CheckerExtensionVisibilityNotAllowed` | Visibility adaptation not allowed on extension members (already committed in `MessageCode.cs`; referenced by §6.2.1, not redefined by Story 08) |

### New Codes to Add (Phase 1)

| Code | Name | Description |
|------|------|-------------|
| 4008 | `CheckerTypeMismatch` | Type assignment incompatibility |
| 4009 | `CheckerIncompatibleReturnType` | Return type does not match declared type |
| 4010 | `CheckerIncompatibleArgumentType` | Argument type does not match parameter type |
| 4011 | `CheckerMissingReturnStatement` | Non-void function missing return on some paths |
| 4012 | `CheckerUnreachableCode` | Code after return/throw/break/continue (warning) |
| 4013 | `CheckerVariableUsedBeforeAssignment` | Variable used before any assignment |
| 4014 | `CheckerVariablePossiblyUndefined` | Variable might not be defined on all paths |
| 4015 | `CheckerVariablePossiblyNull` | Variable might be null (warning) |
| 4016 | `CheckerVariableTypeRequired` | No type annotation and no inferable initializer |
| 4017 | `CheckerAbstractMethodNotImplemented` | Abstract method not implemented by concrete class |
| 4018 | `CheckerInterfaceMethodNotImplemented` | Interface method not implemented |
| 4019 | `CheckerFinalClassExtended` | Final class cannot be extended |
| 4020 | `CheckerFinalMethodOverridden` | Final method cannot be overridden |
| 4021 | `CheckerReadonlyPropertyReassigned` | Readonly property assigned outside constructor |
| 4022 | `CheckerAbstractClassInstantiated` | Cannot `new` an abstract class |
| 4023 | `CheckerEnumCaseTypeMismatch` | Enum case value wrong type for backed enum |
| 4024 | `CheckerEnumMethodNotAllowed` | Enum has forbidden method (e.g., constructor) |
| 4025 | `CheckerMemberNotAccessible` | Private/protected member accessed incorrectly |
| 4026 | `CheckerBreakOutsideLoop` | Break outside loop/switch |
| 4027 | `CheckerContinueOutsideLoop` | Continue outside loop |
| 4028 | `CheckerAwaitOutsideAsync` | Await outside async function |
| 4029 | `CheckerInvalidOperatorForType` | Operator not applicable to operand types |
| 4030 | `CheckerDisposableRequiresInterface` | `:=` requires IsDisposable |
| 4031 | `CheckerWithKeywordInvalidProperty` | Property does not exist on target type |
| 4032 | `CheckerTypeGuardInvalidReturn` | Type guard must return bool |
| 4033 | `CheckerExtensionMethodNotStatic` | Extension method must be static |
| 4034 | `CheckerExtensionMethodNotPublic` | Extension method must be public |
| 4035 | `CheckerGenericConstraintNotSatisfied` | Type arg does not satisfy constraint |
| 4036 | `CheckerGenericArgumentCountMismatch` | Wrong number of type arguments |
| 4037 | `CheckerStructPropertyRequired` | Struct property must be typed |
| ~~4038~~ | *(reserved)* | `CheckerExtensionVisibilityNotAllowed` already exists in `MessageCode.cs` — not a new code; see Existing Codes above. The overload diagnostic that previously claimed 4038 moved to **4118**. |
| 4039 | `CheckerThrowNotThrowable` | Thrown value not Throwable |
| 4040 | `CheckerCatchNotThrowable` | Caught type not Throwable |
| 4041 | `CheckerCatchNoIntersection` | Intersection type in catch clause |
| 4042 | `CheckerCatchNoScalar` | Scalar type in catch clause |
| 4043 | `CheckerConditionNotBool` | Condition is not boolean |
| 4044 | `CheckerTraitRequirementNotMet` | Trait extends requirement not met |
| 4045 | `CheckerTraitRequirementImplNotMet` | Trait implements requirement not met |
| 4046 | `CheckerAsyncIterableMissingAwait` | Synchronous iteration of AsyncIterable |
| 4047 | `CheckerAwaitNonAsyncIterable` | await-foreach on non-async-iterable type |
| 4048 | `CheckerVoidInNonReturnPosition` | void used in generic position without constraint |
| 4049 | `CheckerNeverInNonReturnPosition` | never used in generic position without constraint |
| 4050 | `CheckerUtilityTypeInvalidKey` | Pick/Omit key does not match a property on type |
| 4051 | `CheckerUtilityTypeInvalidArgument` | Utility type argument does not satisfy constraint |
| 4052 | `CheckerReferenceTypeChanged` | Reference parameter reassigned to different type (warning) |
| 4053 | `CheckerDuplicateTypeInComposite` | Same type appears more than once in union/intersection |
| 4054 | `CheckerMixedInComposite` | `mixed` or `never` used in union/intersection |
| 4055 | `CheckerRedundantTypeInUnion` | Redundant type in union (e.g., `bool\|false`, `object\|MyClass`, `iterable\|array`) |
| 4056 | `CheckerUseBoolInsteadOfTrueFalse` | `true\|false` should be `bool` |
| 4057 | `CheckerNonClassInIntersection` | Non-class type in intersection (scalars, self, parent, static) |
| 4058 | `CheckerCallableNotAllowedOnProperty` | `callable` cannot be used as property type |
| 4059 | `CheckerVoidNotAllowedHere` | `void` used outside return type position |
| 4060 | `CheckerVoidRefReturn` | Returning by reference from void function (warning) |
| 4061 | `CheckerNeverNotAllowedHere` | `never` used outside return type position |
| 4062 | `CheckerResourceNotAllowed` | `resource` cannot be used in user type declarations |
| 4063 | `CheckerRefArgMustBeVariable` | By-reference argument must be a variable, not a literal |
| 4064 | `CheckerRelativeTypeOutsideClass` | `self` or `parent` used outside class context |
| 4065 | `CheckerParentWithoutParent` | `parent` used in class that has no parent |
| 4066 | `CheckerStaticNotReturnType` | `static` used outside return type position |
| 4067 | `CheckerDnfRedundantIntersection` | Redundant intersection in DNF type |
| 4068 | `CheckerNeverMustNotReturn` | Function with `never` return type contains a return statement |
| 4069 | `CheckerCannotInstantiateNonClass` | `new` on scalar/array/callable/built-in type |
| 4070 | `CheckerCannotInstantiateTrait` | `new` on a trait |
| 4071 | `CheckerCannotInstantiateInterface` | `new` on an interface |
| 4072 | `CheckerCannotInstantiateEnum` | `new` on an enum |
| 4073 | `CheckerCloneNonObject` | `clone` on non-object type |
| 4074 | `CheckerMagicMethodSignature` | Magic method has wrong parameter count, types, return type, or modifiers |
| 4075 | `CheckerDuplicateParameter` | Two parameters with the same name |
| 4076 | `CheckerRequiredAfterOptional` | Required parameter after optional parameter (warning) |
| 4077 | `CheckerVariadicNotLast` | Variadic parameter is not the last parameter |
| 4078 | `CheckerVariadicWithDefault` | Variadic parameter has a default value |
| 4079 | `CheckerDuplicateNamedArgument` | Same named argument used twice in a call |
| 4080 | `CheckerPositionalAfterNamed` | Positional argument after named argument |
| 4081 | `CheckerUnknownNamedArgument` | Named argument does not match any parameter |
| 4082 | `CheckerNamedAfterUnpack` | Named argument after argument unpacking |
| 4083 | `CheckerClosureUseUndefined` | Closure `use` variable not defined in outer scope |
| 4084 | `CheckerClosureUseThis` | `use($this)` is redundant in non-static closures (warning) |
| 4085 | `CheckerStaticClosureThis` | Static closure references `$this` |
| 4086 | `CheckerYieldOutsideGenerator` | `yield` outside generator function |
| 4087 | `CheckerGeneratorInvalidReturnType` | Generator function with non-Generator return type |
| 4088 | `CheckerYieldInFinally` | `yield` inside `finally` block |
| 4089 | `CheckerYieldFromNonIterable` | `yield from` non-iterable expression |
| 4090 | `CheckerNonConstantExpression` | Non-constant expression in constant-required context |
| 4091 | `CheckerDivisionByZero` | Division by zero in constant expression |
| 4092 | `CheckerDuplicateArrayKey` | Duplicate key in array literal (warning) |
| 4093 | `CheckerInvalidArrayAccess` | Array access on non-array/non-ArrayAccess type |
| 4094 | `CheckerDestructuringNonArray` | List/destructuring on non-array type |
| 4095 | `CheckerDestructuringSpread` | Spread in list/destructuring |
| 4096 | `CheckerSpreadNonIterable` | Spread operator on non-iterable |
| 4097 | `CheckerThisInStaticContext` | `$this` used inside static method or static closure |
| 4098 | `CheckerNonStaticCalledStatically` | Non-static method called statically |
| 4099 | `CheckerStaticCalledOnInstance` | Static method called on instance (warning) |
| 4100 | `CheckerStaticOutsideClass` | `static::` used outside class context |
| ~~4101~~ | ~~`CheckerGotoLabelNotFound`~~ | ~~Not needed — goto is prohibited outright (use 4104)~~ |
| ~~4102~~ | ~~`CheckerGotoIntoLoop`~~ | ~~Not needed — goto is prohibited outright (use 4104)~~ |
| ~~4103~~ | ~~`CheckerGotoOutOfFinally`~~ | ~~Not needed — goto is prohibited outright (use 4104)~~ |
| 4104 | `CheckerGotoProhibited` | `goto` is prohibited in Tyhp |
| 4105 | `CheckerPromotedPropertyNoType` | Promoted constructor property without type |
| 4106 | `CheckerPromotedPropertyInAbstract` | Promoted property in abstract/interface constructor |
| 4107 | `CheckerPromotedVariadic` | Variadic parameter cannot be a promoted property |
| 4108 | `CheckerReadonlyClassMutableProperty` | Mutable property in readonly class |
| 4109 | `CheckerReadonlyClassStaticProperty` | Static property in readonly class |
| 4110 | `CheckerEnumCaseMissingValue` | Backed enum case missing value |
| 4111 | `CheckerEnumCaseValueOnNonBacked` | Case value on non-backed enum |
| 4112 | `CheckerEnumCaseDuplicateValue` | Duplicate enum case value |
| 4113 | `CheckerEnumPropertyNotAllowed` | Instance property on enum |
| 4114 | `CheckerInterfacePropertyInitializer` | Property initializer in interface |
| 4115 | `CheckerInterfacePropertyNotAllowed` | Instance property on interface |
| 4116 | `CheckerTraitConflict` | Unresolved trait method conflict |
| 4117 | `CheckerCircularTraitUse` | Circular trait use detected |
| 4118 | `CheckerOverloadSignatureIncompatible` | Overload incompatible with implementation (reassigned from the reserved 4038; reuses the slot freed by the removed `CheckerLooseComparisonWarning`) |
| 4119 | `CheckerIncomparableTypes` | Comparing types with no meaningful comparison (warning) |
| 4120 | `CheckerConcatNonStringable` | String concat with non-stringable type |
| 4121 | `CheckerEmptyCatch` | Empty catch block (warning) |
| 4122 | `CheckerReturnInFinally` | Return statement in finally block (warning) |
| 4123 | `CheckerBreakInFinally` | Break/continue in finally block (warning) |
| 4124 | `CheckerDuplicateCatch` | Same exception type in multiple catch clauses (warning) |
| 4125 | `CheckerCatchOrderBroadFirst` | Parent exception caught before child (warning) |
| 4126 | `CheckerNotAnAttributeClass` | Class used as attribute not declared as attribute |
| 4127 | `CheckerAttributeTargetMismatch` | Attribute used on wrong target type |
| 4128 | `CheckerAttributeNotRepeatable` | Non-repeatable attribute used multiple times |
| 4129 | `CheckerOverrideNotOverriding` | `#[Override]` on method that doesn't override |
| 4130 | `CheckerUnusedImport` | Unused `use` import (warning) |
| 4131 | `CheckerDuplicateImport` | Duplicate import (warning) |
| 4132 | `CheckerConflictingImportAlias` | Two imports with same alias |
| 4133 | `CheckerVariableVariableProhibited` | Variable variables (`$$var`) prohibited in Tyhp |
| 4134 | `CheckerDynamicPropertyProhibited` | Dynamic property creation prohibited |
| 4135 | `CheckerCompactProhibited` | `compact()` prohibited in Tyhp |
| 4136 | `CheckerExtractProhibited` | `extract()` prohibited in Tyhp |
| 4137 | `CheckerGlobalVariableWarning` | `global $var` usage (warning) |
| 4138 | `CheckerClosureParameterTypeRequired` | Cannot infer closure parameter type; explicit annotation required |
| 4139 | `CheckerCloneWithReadonlyRequiresConfig` | Clone `with` on readonly requires opt-in config for PHP < 8.5 |
| 4140 | `CheckerWithReadonlyFinalClass` | `with` on readonly of `final` class not possible on PHP < 8.5 |
| 4141 | `CheckerWithReadonlyInPlace` | In-place `with` cannot modify readonly properties |
| 4142 | `CheckerMissingArgument` | Required argument missing in a function/method call |
| 4143 | `CheckerTooManyArguments` | Too many arguments passed to a function/method call |
| 4200 | `CheckerUnusedVariable` | Variable assigned but never read (warning) |
| 4201 | `CheckerUnusedParameter` | Parameter never used in body (warning) |
| 4202 | `CheckerUnusedPrivateMember` | Private member never referenced (warning) |
| 4203 | `CheckerAssignmentInCondition` | Assignment in condition expression (warning) |
| 4204 | `CheckerConditionAlwaysTrueFalse` | Condition is always true or false (warning) |
| 4205 | `CheckerRedundantCast` | Cast to the same type (warning) |
| 4206 | `CheckerDeadStore` | Variable assigned then overwritten before read (warning) |
| 4207 | `CheckerUnnecessaryNullCheck` | Null check on non-nullable type (warning) |
| 4208 | `CheckerUnreachableArm` | Unreachable match/switch arm (warning) |
| 4209 | `CheckerLossyCast` | Lossy cast (warning) |
| 4210 | `CheckerErrorThresholdReached` | Error threshold reached for file (info) |
| 4211 | `CheckerStaticReturnSelfInNonFinal` | `new self()` returned from `static` return in non-final class (warning) |
| 4500 | `CheckerDeprecatedUsage` | Using a deprecated symbol (warning) |
| 4501 | `CheckerObsoleteUsage` | Using an obsolete symbol (error) |

> **Note:** Codes 4500-4501 are reserved for checker deprecation/obsolescence warnings. The optimizer (Story 23) must NOT use codes in the 4500-4511 range — optimizer diagnostics are reassigned to the 4700-4799 range.

| 4800 | `CheckerEvalUsage` | eval() usage detected (info) |
| 4801 | `CheckerIncludeNotAllowed` | include/require not allowed (error) |

---

## Appendix C: Type Compatibility Quick Reference

### Assignability Rules Summary

| Source Type | Target Type | Assignable? |
|-------------|-------------|-------------|
| `T` | `T` | Yes (same type) |
| any | `mixed` | Yes |
| `never` | any | Yes (bottom type) |
| `null` | `?T` | Yes |
| `int` | `float` | Yes (numeric widening) |
| `T` | `T\|U` | Yes (member of union) |
| `T&U` | `T` | Yes (member of intersection) |
| child class | parent class | Yes (nominal subtype) |
| implementor | interface | Yes (implements) |
| struct A (superset) | struct B (subset) | Yes (structural width subtype) |
| `string` | `int` | No |
| `T\|U` | `T` | No (union not assignable to member) |
| `T` | `T&U` | No (single not assignable to intersection) |
| `void` | any | No (void is not a value) |
| abstract class | `new AbstractClass()` | No (cannot instantiate) |

### Type Narrowing Summary

| Condition | True Branch Narrowing | False Branch Narrowing |
|-----------|----------------------|----------------------|
| `$x instanceof Foo` | `$x` → `Foo` | `$x` → original minus `Foo` |
| `$x !== null` | `$x` → non-null | `$x` → `null` |
| `$x === null` | `$x` → `null` | `$x` → non-null |
| `is_string($x)` | `$x` → `string` | `$x` → original minus `string` |
| `is_int($x)` | `$x` → `int` | `$x` → original minus `int` |
| `isMyGuard($x)` | `$x` → guard target type | `$x` → original minus target |

---

*Generated: 2026-02-16 | Updated: 2026-03-20 | Source: TODO.md Story 08 | Branch: Not Specified*

---

## Human Testing and Verification

> **Note:** These steps are meant to help a human developer manually verify the checker implementation. Steps can be skipped, reordered, or adapted based on what has already been tested or what is most relevant. The goal is practical validation, not exhaustive coverage.

### Step 1: Verify the Checker Compiles and Integrates

Run the project build to confirm the checker code compiles without errors:

```bash
dotnet build
```

Confirm there are no build errors in the `Tyhp/TyhpLang/Checker/` directory.

### Step 2: Verify Type Mismatch Detection

Create a test file `test_checker_types.tyhp`:

```tyhp
<?tyhp

function testTypeMismatch(): void {
    int $x = "hello";  // Should produce CheckerTypeMismatch
    string $name = 42;  // Should produce CheckerTypeMismatch
    float $f = true;    // Should produce CheckerTypeMismatch
}
```

Run the linter (checker):

```bash
tyhp lint test_checker_types.tyhp
```

**Expected:** Each assignment should produce a `TYHP4008` (CheckerTypeMismatch) error. Verify the error messages reference the correct line numbers and types involved.

### Step 3: Verify Non-Nullable Enforcement

Create a test file `test_checker_null.tyhp`:

```tyhp
<?tyhp

function testNullSafety(): void {
    string $name = null;    // Should error: null assigned to non-nullable
    ?string $maybe = null;  // Should be OK: nullable type
    int $count = $maybe;    // Should error: ?string not assignable to int
}

function testNullParam(string $required): void {
    echo $required;
}

function callWithNull(): void {
    testNullParam(null);  // Should error: null passed to non-nullable param
}
```

Run:

```bash
tyhp lint test_checker_null.tyhp
```

**Expected:**
- Line with `string $name = null` → error (null to non-nullable)
- Line with `?string $maybe = null` → no error
- Line with `int $count = $maybe` → type mismatch error
- Call `testNullParam(null)` → incompatible argument type error

### Step 4: Verify Function Call Argument Checking

Create a test file `test_checker_calls.tyhp`:

```tyhp
<?tyhp

function add(int $a, int $b): int {
    return $a + $b;
}

function testCalls(): void {
    int $result = add(1, 2);           // OK
    int $bad1 = add("x", 2);          // Should error: string not assignable to int
    int $bad2 = add(1);               // Should error: missing argument
    int $bad3 = add(1, 2, 3);         // Should error: too many arguments
}
```

Run:

```bash
tyhp lint test_checker_calls.tyhp
```

**Expected:**
- First call → no error
- Second call → `CheckerIncompatibleArgumentType` for first argument
- Third call → `CheckerMissingArgument`
- Fourth call → `CheckerTooManyArguments`

### Step 5: Verify Type Inference on First Assignment

Create a test file `test_checker_inference.tyhp`:

```tyhp
<?tyhp

function testInference(): void {
    $x = 42;             // Inferred as int
    $x = "hello";        // Should error: string not assignable to int

    $name = "Alice";     // Inferred as string
    int $len = $name;    // Should error: string not assignable to int

    $flag = true;        // Inferred as bool
    $flag = 0;           // Should error: int not assignable to bool
}
```

Run:

```bash
tyhp lint test_checker_inference.tyhp
```

**Expected:** The re-assignments to incompatible types should produce `CheckerTypeMismatch` errors, confirming the inferred types are locked after first assignment.

### Step 6: Verify Type Narrowing (Smart Casts)

Create a test file `test_checker_narrowing.tyhp`:

```tyhp
<?tyhp

class Dog {
    public function bark(): string {
        return "Woof!";
    }
}

class Cat {
    public function meow(): string {
        return "Meow!";
    }
}

function testNarrowing(Dog|Cat $animal): void {
    if ($animal instanceof Dog) {
        echo $animal->bark();   // Should be OK: narrowed to Dog
    } else {
        echo $animal->meow();   // Should be OK: narrowed to Cat
    }

    echo $animal->bark();  // Should error: Dog|Cat does not have bark()
}

function testNullNarrowing(?string $value): void {
    if ($value !== null) {
        int $len = \strlen($value);  // Should be OK: narrowed to string
    }
    int $len2 = \strlen($value);  // Should error/warn: value could be null
}
```

Run:

```bash
tyhp lint test_checker_narrowing.tyhp
```

**Expected:**
- Inside `if ($animal instanceof Dog)`, `bark()` is valid
- Inside the else branch, `meow()` is valid (narrowed to Cat)
- After the if/else, calling `bark()` on `Dog|Cat` should error
- Inside `if ($value !== null)`, `$value` is non-null string
- Outside the null check, `$value` is still nullable

### Step 7: Verify Interface and Class Declaration Checks

Create a test file `test_checker_declarations.tyhp`:

```tyhp
<?tyhp

interface Printable {
    public function __toString(): string;
}

class User implements Printable {
    public function __construct(
        private string $name
    ) {}

    // Missing __toString() implementation — should error
}

abstract class Shape {
    abstract public function area(): float;
}

class Circle extends Shape {
    public function __construct(private float $radius) {}
    // Missing area() implementation — should error
}
```

Run:

```bash
tyhp lint test_checker_declarations.tyhp
```

**Expected:**
- `User` should produce an error for not implementing `Printable::__toString()`
- `Circle` should produce an error for not implementing `Shape::area()`

### Step 8: Verify Operator Overload Validation

Create a test file `test_checker_operators.tyhp`:

```tyhp
<?tyhp

class Money {
    public function __construct(
        private int $cents
    ) {}

    public operator +(self $left, self $right): self {
        return new self($left->cents + $right->cents);
    }
}

function testOperators(): void {
    Money $a = new Money(100);
    Money $b = new Money(200);
    Money $c = $a + $b;         // Should be OK
    Money $d = $a + 5;          // Should error: int not compatible with Money
    string $s = $a + $b;       // Should error: Money not assignable to string
}
```

Run:

```bash
tyhp lint test_checker_operators.tyhp
```

**Expected:**
- `$a + $b` → OK, produces Money
- `$a + 5` → error (no operator overload for Money + int)
- `string $s = $a + $b` → type mismatch (Money to string)

### Step 9: Verify Struct Type Checking

Create a test file `test_checker_structs.tyhp`:

```tyhp
<?tyhp

struct Point {
    int $x;
    int $y;
}

function testStructs(): void {
    Point $p = new Point() with { $x = 1, $y = 2 };
    int $x = $p->x;          // Should be OK
    string $s = $p->x;       // Should error: int not assignable to string
    $p->x = "hello";         // Should error: string not assignable to int
    echo $p->z;              // Should error: property z does not exist on Point
}
```

Run:

```bash
tyhp lint test_checker_structs.tyhp
```

**Expected:** Type mismatches and unknown property access are flagged.

### Step 10: Verify Generic Constraint Checking

Create a test file `test_checker_generics.tyhp`:

```tyhp
<?tyhp

class Collection<T> {
    /** @var array<T> */
    private array $items = [];

    public function add(T $item): void {
        $this->items[] = $item;
    }

    public function first(): ?T {
        return $this->items[0] ?? null;
    }
}

function testGenerics(): void {
    Collection<int> $nums = new Collection<int>();
    $nums->add(42);       // OK
    $nums->add("hello");  // Should error: string not assignable to int (generic T = int)
}
```

Run:

```bash
tyhp lint test_checker_generics.tyhp
```

**Expected:** Adding a string to a `Collection<int>` should produce a type error.

### Step 11: Verify Restricted Feature Detection

Create a test file `test_checker_restricted.tyhp`:

```tyhp
<?tyhp

function testRestricted(): void {
    eval('echo "hello";');       // Should produce CheckerEvalUsage info diagnostic
    include 'other_file.php';    // Should produce CheckerIncludeNotAllowed error
    $$dynamicVar = "value";      // Should produce a warning about variable variables
}
```

Run:

```bash
tyhp lint test_checker_restricted.tyhp
```

**Expected:**
- `eval()` → info-level diagnostic `TYHP4800`
- `include` → error `TYHP4801`
- Variable variables → warning

### Step 12: Verify Diagnostic Formatting and MessageCodes

After running any of the above test files, verify:

1. Each diagnostic includes a `TYHP` code number (e.g., `TYHP4001`)
2. Each diagnostic includes the file path, line number, and column number
3. Error vs warning vs info severity is correctly indicated
4. The message text is descriptive and mentions the actual types involved

### Step 13: Verify ErrorAst Graceful Handling

Create a test file with intentional syntax errors to confirm the checker does not crash on `ErrorAst` nodes:

```tyhp
<?tyhp

function broken(): void {
    int $x = ;  // parse error — should produce ErrorAst
    $x + 1;     // checker should not crash on this line
}

class Valid {
    public function ok(): int {
        return 42;  // checker should still validate this
    }
}
```

Run:

```bash
tyhp lint test_checker_errorast.tyhp
```

**Expected:** Parser errors are reported for the broken lines, but the checker does not crash and continues checking the `Valid` class. No unhandled exceptions or stack traces.

### Step 14: Verify Checker Does Not Block Valid PHP Constructs

Create a test file that uses standard PHP constructs to ensure the checker does not produce false positives:

```tyhp
<?tyhp

function validPhp(): void {
    array $items = [1, 2, 3];
    foreach ($items as int $item) {
        echo $item;
    }

    ?string $result = null;
    if ($result !== null) {
        echo \strtoupper($result);
    }

    int $x = 10;
    float $y = $x;  // int-to-float widening should be OK
}
```

Run:

```bash
tyhp lint test_checker_valid.tyhp
```

**Expected:** Zero errors and zero warnings. Every construct here should pass type checking.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
