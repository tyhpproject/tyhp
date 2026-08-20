# Tyhp Checker — Developer Technical Guide

This guide explains how the Tyhp type checker works, grounded in the source under
`Tyhp/TyhpLang/Checker/`. It is intended for contributors who need to add rules,
change assignability, extend inference, or debug check-phase diagnostics and
emitter side-channels.

Related reading in-tree:

- `readme.md` — short informal checklist of language rules still being enforced
- `tests/Tyhp.Tests/Checker/` — behavioral coverage by area
- `FOUND_BUGS.md` / `RESOLVED_BUGS.md` — design notes that motivated several
  checker/emitter contracts (especially generics)

---

## 1. Overview / purpose in the compilation pipeline

The checker is the **third** major phase after parse and bind:

1. **Parse** — `CompilationService.ParseFiles` builds `SrcFileAst` trees.
2. **Bind** — `TyhpBinder.Bind` builds symbols and scopes (`GlobalScope`).
3. **Check** — `TyhpChecker.Check` walks bound ASTs, emits diagnostics, and
   records side-channel data the emitter (and LSP/optimizer consumers) need.
4. **Emit** — reads checker outputs via `CompilationResult` / `EmitContext`.

Orchestration lives in `Tyhp/Domain/Services/CompilationService.cs`:

- Bind runs only when parse produced no errors.
- Check runs only when bind produced a non-null `GlobalScope`.
- `CheckParsedFiles` constructs `TyhpChecker` with `options.Checker`
  (`CheckerOptions`), calls `Check`, then copies checker outputs onto
  `CompilationResult`.

The checker’s job is **semantic validation and type resolution**, not name
binding. It assumes symbols are already attached where the binder attaches them.
It also does work the binder deliberately skips (for example resolving free
function names at call sites — see `CheckerHelpers.ResolveFreeFunction`).

Beyond diagnostics, the checker produces **emitter contracts**:

| Output | Purpose |
|--------|---------|
| `NarrowedTypes` | Control-flow narrowed types keyed by AST (optimizer / LSP) |
| `RequiresRuntimeGenericTracking` | Classes/enums needing `GenericObject` emit |
| `RequiresGenericVariant` | Callables needing Mechanism D `__tyhpGeneric` Closure binders |
| `GenericCallTargets` | Call sites → resolved generic callees for variant routing |
| `RequiresWeakReferenceCapture` | Closures needing WeakReference `$this` capture |
| `InferredClosureSignatures` | Contextual param/return types for closures that omitted authored annotations (emitter typehint recovery) |
| `ExpressionTypes` | Per-expression types memoized during inference (Story 16 Phase 2 expression-tree emit) |
| `RequiresDisposableTryFinally` | Disposable scopes needing try/finally fallback |
| `AsyncForeachKinds` | Await-foreach classification for desugaring |

`ExpressionTypes` is copied onto `CompilationResult` by `CompilationService.CheckParsedFiles`
and forwarded into `EmitContext` so expression-tree emission can spell per-node `$type`
strings.

---

## 2. Entry points and check orchestration

### 2.1 `TyhpChecker`

Primary type: `Tyhp/TyhpLang/Checker/TyhpChecker.cs`.

Construction:

```csharp
new TyhpChecker(diagnostics, symbolTree, globalScope, options?, rules?)
```

- Builds a `CheckerRuleRegistry` from `rules` or `CreateDefaultRules()`.
- Creates a shared `TypeInferrer` and a single `CheckerRuleContext` for the
  session.
- Configures template-string matcher budget from
  `CheckerOptions.TemplateStringMaxStates`.

Public entry:

```csharp
checker.Check(IEnumerable<SrcFileAst> astTrees)
```

Per file:

1. `CreateInitialState(srcFile)` — `ScopeType.File`, `CurrentFileName` set.
2. For each non-null child of the file AST, `CheckNode(child, state)`.
3. After all files: `ImportRule.FlushRemainingImports` (unused-import reports).
4. `PropagateGenericVariantAcrossHierarchies()` — union-find over method
   override/implement families so Mechanism D binder flags are consistent across a
   hierarchy.

### 2.2 `CheckNode` traversal

`CheckNode` is the recursive walker:

1. Skip `ErrorAst`.
2. `_rules.Dispatch(node, state, _ruleContext, _diagnostics)`.
3. If any applicable rule returns `SuppressChildTraversal(node) == true`,
   **do not** walk `AstChildren`; still run `CheckAttributes`.
4. Otherwise walk children, then `CheckAttributes`.

Attributes are visited separately because class-member entry points often bypass
full `CheckNode` on the declaration itself; `CheckAttributes` keeps
name-based rules (notably `ImportRule`) seeing attribute class names.

### 2.3 Default rule registration order

`CreateDefaultRules()` registers rules in this order (registration order matters
only when multiple rules handle the same node type — they all run; suppression
is OR’d):

1. `DeclarationRule`
2. `TypeAnnotationRule`
3. `ControlFlowRule`
4. `TypeCompatibilityRule`
5. `TypeDeclarationValidationRule`
6. `ReferenceTrackingRule`
7. `ClosureRule`
8. `AsyncBlockRule`
9. `NullSafetyRule`
10. `UnsetTrackingRule`
11. `StructRule`
12. `OperatorOverloadRule`
13. `ExtensionRule`
14. `AsyncRule`
15. `DisposableRule`
16. `CompileTimeRule`
17. `DeprecationRule`
18. `RestrictedFeatureRule`
19. `OverloadRule`
20. `AttributeRule`
21. `ImportRule`
22. `CodeQualityRule`
23. `WithKeywordRule`

Tests can inject a custom `IEnumerable<ICheckerRule>` to isolate behavior.

### 2.4 Options

`Tyhp/Domain/Services/CheckerOptions.cs`:

| Option | Role |
|--------|------|
| `AllowEval` | When true, `eval()` is not diagnosed |
| `MaxErrorsPerFile` | Cap per-file errors (0 = unlimited); default 100 |
| `TemplateStringMaxStates` | Template-string automaton budget; default 256 |
| `PhpVersion` | Version-gated checks (e.g. `with` on readonly); default `"8.4"` |
| `ExperimentalReadonlyCloneWith` | Opt-in for anonymous-class `clone … with` on PHP &lt; 8.5 |

Comments in `CheckerOptions` state that null safety and required annotations are
**unconditional** — there are no toggles that relax them.

---

## 3. Folder / file map

```
Tyhp/TyhpLang/Checker/
├── TyhpChecker.cs                 # Orchestrator
├── CheckerState.cs                # Scope-local mutable check state
├── VariableState.cs               # Per-variable assignment / narrowing
├── PropertyInitializationState.cs # $this->prop init + narrowing
├── ReferenceGroup.cs              # &$ alias groups
├── ICheckedType.cs / CheckedType.cs  # Checked-type ADT + CheckedTypes factory
├── CheckedTypeDisplay.cs          # Canonical DisplayName for unions / nullables
├── INarrowingResolution.cs        # Narrowing’s type-resolution surface
├── TypeComparer*.cs               # Assignability / subtyping / ops / generics / …
├── TypeInferrer*.cs               # Expression + type-expression inference
├── GenericConstraintResolver.cs   # Caches ResolvedConstraint on type params
├── GenericTypeArgumentValidator.cs
├── UtilityTypeResolver.cs         # \Tyhp utility types (Readonly, Pick, …)
├── TypeNameAlgebraResolver.cs
├── SymbolNameTypeHelper.cs        # __FunctionName / __ClassName / …
├── SymbolNameTypeAssignability.cs
├── SymbolNameExistenceVerifier.cs
├── NameofTypeInferrer.cs
├── StructTypeHelper.cs
├── StructBagLiteralChecker.cs     # Named/positional bag literals vs struct shapes
├── CallableSignatureReflection.cs # Story 16.5 — params + return from a callable type
├── FunctionOverloadSelector.cs    # Story 16.5 — same-arity tyhpdef overload pick
├── GenericInheritanceBindings.cs  # Extends-chain generic bindings for receivers/shapes
├── TemplateString*.cs             # Pattern matching for template-string types
├── PhpStringLiteralHelper.cs
├── readme.md
├── technical-guide.md             # This document
└── Rules/
    ├── ICheckerRule.cs
    ├── CheckerRuleRegistry.cs
    ├── CheckerRuleContext.cs
    ├── CheckerHelpers.cs
    ├── DeclarationRule*.cs
    ├── ControlFlowRule*.cs
    ├── TypeCompatibilityRule*.cs
    ├── TypeNarrowingRule.cs       # (namespace Tyhp.TyhpLang.Checker, not Rules)
    ├── ClosureParameterInference.cs
    ├── PropertyPathSupport.cs     # (namespace Tyhp.TyhpLang.Checker) Story 16 Phase 1
    ├── ExpressionTreeSupport.cs   # (namespace Tyhp.TyhpLang.Checker) Story 16 Phase 2
    ├── TypeGuardValidation.cs
    ├── PropertyInitializationAnalysis.cs
    └── … other *Rule.cs files
```

### 3.1 `TypeComparer` partials

| File | Responsibility |
|------|----------------|
| `TypeComparer.cs` | Public API façade, template-string budget thread-static state |
| `TypeComparer.Assignability.cs` | `IsAssignableToCore` |
| `TypeComparer.ConvertAssignability.cs` | `IsAssignableViaOperatorConvert` (call/return/`new` only) |
| `TypeComparer.Subtyping.cs` | `IsSubtypeOfCore`, inheritance walk |
| `TypeComparer.BuiltInTypes.cs` | `iterable`, `callable`, array-like, gradual array rules |
| `TypeComparer.Generics.cs` | Substitution by name or by symbol identity; expands deferred `__CallableReturnType` / `__CallableParametersStruct` / `__CallableParametersTuple` / `__CallableParametersRest` / `\Tyhp\ReturnType` after `TCallable` is bound (Rest keeps its wrapper so call-site unpack can see it) |
| `TypeComparer.Operations.cs` | Equality, union/intersect, narrow positive/negative |
| `TypeComparer.Aliases.cs` | Alias expansion |
| `TypeComparer.TemplateStrings.cs` | Template-string inclusion |

### 3.2 `TypeInferrer` partials

| File | Responsibility |
|------|----------------|
| `TypeInferrer.cs` | Cache, `ResolveTypeExpression`, resolution scope |
| `TypeInferrer.Expressions.cs` | Scalars, vars, ops, ternary, new, closures, match, … |
| `TypeInferrer.Dereferenceables.cs` | Calls, members, array access, generics at call sites |
| `TypeInferrer.TypeExpressions.cs` | Annotation AST → `ICheckedType` |
| `TypeInferrer.Operators.cs` | Native numeric / binary operator result types |
| `TypeInferrer.OperatorOverloads.cs` | Operator-overload return-type lookup (Story 11 §8) |
| `TypeInferrer.TemplateStrings.cs` | Template-string type construction |

### 3.3 Rules (by concern)

| Rule | Primary concern |
|------|-----------------|
| `DeclarationRule` | Namespaces, classes, functions, methods, properties; owns scope setup; property-hook modifier + final-override + `&get` version gate (TYHP4154 / TYHP4166 / TYHP4167) |
| `TypeAnnotationRule` | Required annotations / typed locals |
| `ControlFlowRule` | if/loops/try/return/throw/ternary/match conditions |
| `TypeCompatibilityRule` | Assignments, calls, `new`, member access, arrays; **mixed use-site** (TYHP4160); call/ctor arity (TYHP4142/4143) |
| `TypeDeclarationValidationRule` | Illegal type-expression shapes |
| `NullSafetyRule` | Definite assignment / non-null use sites |
| `TypeNarrowingRule` | Smart casts (static helper, not `ICheckerRule`) |
| `ClosureRule` + `ClosureParameterInference` | Closures / contextual params |
| `UnsetTrackingRule` | `unset` vs definite assignment / `AllowUnset` |
| `StructRule` / `WithKeywordRule` | Struct decls (incl. ObjectGenerics / constraints) and `with` forms (member substitution on generic structs) |
| `AsyncRule` / `DisposableRule` | await / disposables / emit flags |
| `CompileTimeRule` | `nameof` / `typeof` / `default` / `variable_exists` (bare `nameof(T)` / `typeof(T)` accept in-scope class or method generics via `IsInScopeGenericParameter`) |
| `ImportRule` | Unused / duplicate imports |
| Others | Overloads, attributes, deprecation, restricted PHP features, extensions, operator overloads, references, code quality |

---

## 4. `CheckerState` and rule context — how state flows

### 4.1 `CheckerState`

`CheckerState` is the **mutable, scope-local** environment for the walk. Important
fields:

- **Scope identity**: `ScopeType`, `Parent`, `CurrentFileName`,
  `CurrentNamespaceName`, optional `NameResolutionScope` override.
- **Enclosing declarations**: `EnclosingObject`, `EnclosingFunction`,
  `EnclosingCallable`, `ObjectGenerics`, `FunctionGenerics`,
  `EnclosingObjectType`, `Modifiers`.
- **Expected types**: `ExpectedReturnType`, `ExpectedClosureType`,
  `IsTypeGuardFunction`.
- **Flow flags**: async/generator/loop/switch/finally/closure,
  `HasReturnedOnAllPaths`, `IsExistenceProbeContext`,
  `IsParameterTypePosition` / `IsPropertyTypePosition`.
- **Tracked maps**:
  - `Variables` → `VariableState`
  - `PropertyInit` → `PropertyInitializationState` (`$this->prop`)
  - `IndexAccessNarrowing` → `$arr[0]` / `$arr['k']` keys
  - `MemberAccessNarrowing` → `$obj->prop` keys (not `$this`)

#### Snapshots vs splits

- `SnapShot()` — deep copy for branching; result is **locked** (immutable). Used
  as the pre-branch baseline for if/else merges.
- `Fork()` — deep copy that stays **mutable**. Use when rebinding
  `FunctionGenerics` / `ObjectGenerics` / `NameResolutionScope` before
  `ResolveTypeExpression` (which may itself `SnapShot` for cross-file
  annotations). Using `SnapShot` for that path throws
  `InvalidOperationException` ("snapshot is immutable") → TYHP4001.
- `Split(scopeType)` — child scope with parent link. Reset vs clone of maps
  depends on `ScopeType` (function boundaries get fresh variable maps; code
  blocks clone visible locals).

#### Merge vs absorb

- `Merge(branch)` — join two paths (treats *this* and *branch* as alternate
  paths). Assignment becomes definite only if both paths assigned; narrowing is
  dropped when paths disagree; index/member narrowing kept only when both sides
  agree (`TypeComparer.AreTypesEqual`).
- `AbsorbJoinedVariables(joined)` — copy an already-joined result over *this*
  without counting the pre-branch map as a third path. **Required** for
  if/else (join then⋈else first, then absorb).

Typed locals are **function-scoped** (PHP semantics). Declarations hoist to the
function-boundary dictionary; branch mutations clone-on-write into the current
scope so they do not mutate the pre-branch binding.

#### `IsInsideClosure` vs `EnclosingCallable`

`EnclosingCallable` deliberately **leaks through closures** so nested named
functions inside a closure can still be rejected
(`CheckerNestedNamedFunctionNotAllowed`). Checks that attribute a `return` to a
specific callable (e.g. `__construct`/`__destruct` void-return) must consult
`IsInsideClosure` rather than `EnclosingCallable` alone.

### 4.2 `CheckerRuleContext`

`Rules/CheckerRuleContext.cs` is the façade rules use:

- Re-entrancy into the walker: `CheckNode` / `CheckNodes` / `CheckStatementBlock`
  / `CheckAttributes`
- Type APIs: `ResolveExpressionType`, `ResolveTypeAnnotation`,
  `ResolveMemberDeclaredType`, `ResolveFunctionDeclaredType`
- Assignability: `IsAssignable` → `SymbolNameTypeAssignability.IsAssignableTo`
  (wraps `TypeComparer` + symbol-name literal existence)
- Diagnostics: `ReportError` → `TyhpChecker.TryAddError` (respects max-errors; forwards AST
  `EndLine`/`EndColumn` when set so rich underlines span the full node)
- Emitter flags: `MarkRequiresRuntimeGenericTracking`,
  `MarkRequiresGenericVariant`, `RecordGenericCallTargetsIn`, weak-ref /
  disposable / async-foreach markers
- Struct `new … with` bookkeeping: `MarkStructNewCheckedViaWith`

It implements `INarrowingResolution` so statement narrowing and expression
inference can share the same narrowing entry points.

### 4.3 Session-level caches on `TyhpChecker`

- `_expressionTypes` — memo for `TypeInferrer.InferExpressionType`
- `_narrowedTypes` — AST nodes where narrowing was recorded for consumers
- Generic / disposable / async sets described in §1

---

## 5. Rule system architecture

### 5.1 `ICheckerRule`

```csharp
IEnumerable<Type> HandledNodeTypes { get; }
bool Handles(IBase2Ast node) => true;           // optional filter
bool SuppressChildTraversal(IBase2Ast node) => false;
void Check(...);
```

Dispatch is by **exact runtime type** of the AST node (`node.GetType()`), not by
base interfaces. Rules must list concrete AST class types.

### 5.2 `CheckerRuleRegistry`

Indexes rules by handled type. On `Dispatch`:

1. Look up rules for the node’s concrete type (none → return `false`).
2. For each rule where `Handles(node)` is true, call `Check`.
3. If any such rule wants suppression, return `true`.

Multiple rules can fire on one node (e.g. `PhpBinaryOpAst` is handled by
compatibility, null-safety, with-keyword, disposable, reference tracking, etc.).

### 5.3 SuppressChildTraversal — why it exists

Rules that manage their own child walk (control flow, declarations, closures,
dereferenceables) suppress the default child walk so:

- Branch states stay isolated.
- Write targets are not treated as reads.
- Nested structure is visited in the correct scope order.

**Critical consequence:** suppressed subtrees are invisible to other rules’
default dispatch. Several places compensate explicitly:

- `TypeCompatibilityRule` walks call arguments itself (types + arity:
  TYHP4142 missing required / TYHP4143 too many; unpack `...$args` skips
  static arity).
- `TypeInferrer.RecordGenericCallTargetsIn` scans suppressed trees for generic
  call sites that would otherwise never be recorded.
- `CheckerHelpers.CheckCompileTimeConstructsInTree` / `UsesGenericAtRuntime`
  scan bodies for `typeof`/`default`/`instanceof` that `CompileTimeRule` may not
  see in every position.
- Class members: `DeclarationRule.CheckObjectBody` calls `CheckMethod` /
  `CheckProperty` **directly** (not `CheckNode`). Rules that only register
  `PhpMethodDeclAst` would never run — several rules document this and expose
  static helpers invoked from `DeclarationRule` (`AsyncRule.ValidateAsyncMethod`,
  `AttributeRule.ValidateDeclarationAttributes`, etc.).
- **Override signature checking** (`ValidateOverrideSignature`): parent
  parameter/return annotations are resolved with
  `ResolveMemberDeclaredType` against the child receiver (declaring-class
  `ObjectGenerics` + extends-chain substitution), not with the child's state
  alone. Otherwise a base signature like `Expression<TSource, mixed>` looked up
  while checking `ExpressionBuilder<T> extends Expression<T, bool>` reports
  false TYHP3003 for `TSource`, and any diagnostic on the parent AST would show
  parent line/col under the child's `CurrentFileName`.
- Diagnostic helpers (`CheckerHelpers.ReportError*` / `ResolveDiagnosticFileName`)
  prefer `node.OwningFile?.FileName` over `state.CurrentFileName` so spans stay
  tied to the file that owns the AST node.

### 5.4 Composition patterns

1. **Scope-owning rule** (`DeclarationRule`, `ClosureRule`, `ControlFlowRule`) —
   `Split` / `SnapShot` / `Merge` / `Absorb`, then `context.CheckNode`.
2. **Expression rule** (`TypeCompatibilityRule`) — resolve types via context,
   compare with `IsAssignable`, report mismatches.
3. **Static helper modules** — `TypeNarrowingRule`, `ClosureParameterInference`,
   `TypeGuardValidation`, `PropertyInitializationAnalysis`, `CheckerHelpers`.
4. **Cross-rule explicit calls** — when dispatch cannot reach a node.

---

## 6. Type comparison / assignability / subtyping

### 6.1 Checked types (`ICheckedType`)

Kinds (`CheckedTypeKind`): Simple, Union, Intersection, Nullable, Generic,
Literal, Struct, Callable, Never, Void, Mixed, Unresolved, Inferred,
TemplateString.

Important encodings:

- **Nominal types** — `SimpleCheckedType` wrapping a binder symbol
  (`BuiltInTypeSymbol`, `ObjectDeclarationSymbol`, `GenericTypeParameterSymbol`,
  …).
- **`UnresolvedCheckedType`** — compiler-internal recovery singleton;
  assignable to/from everything so one failure does not cascade. Display name
  `"unresolved"`. Comments emphasize it is **not** the user-facing top type;
  **`mixed`** is the strict top (assigns from anything, assigns only to
  `mixed` / `?mixed` without narrowing). In this codebase, “unknown” gradual
  typing is represented by unresolved/mixed-like behavior at unresolved member
  sites (see call inference comments).
- **Literals** — `LiteralCheckedType` (including `null`, `true`, `false`).
- **Structs** — structural property maps (`StructCheckedType`).
- **Callables** — `CallableCheckedType(parameterTypes, returnType)`. Optional trailing defaults
  synthesize an `IntersectionCheckedType` of arity siblings via `CallableArityFacetBuilder`
  (shared prefix math in `ArityFacetExpansion`). A trailing `...$args` adds exactly one
  variadic-inclusive facet — enough to match a one-extra-argument target without unbounded
  siblings. Typed `callable` / `\Closure` may appear in user-written intersections. Invoke /
  closure contextual typing select a facet by argument count; a hand-written intersection may
  give each arity its own return type.

**Display names (`ICheckedType.DisplayName`)** — diagnostic-facing only (not PHP emit;
emitter uses `TypeSpellingHelper`). `UnionCheckedType` / `NullableCheckedType` go through
`CheckedTypeDisplay`, which flattens nested unions, collapses `?T` ↔ `T|null`, drops
duplicate members (`CheckedTypes.AreTypesEqual`), and picks a canonical spelling:

- `?T` when the only non-null member is a single non-union type; if that member is an
  `IntersectionCheckedType` it is parenthesized (`?(A&B)`, never the ambiguous `?A&B`)
- `A|B|…|null` when null is present alongside multiple non-null members (never `?(A|B)`,
  and never mix `?T` into a larger `|` union)

Construction / assignability stay separate — display normalization does not rewrite the
underlying union graph.

`CheckedTypes` factory provides singletons (`Never`, `Void`, `Mixed`, `Null`,
`Unresolved`, primitives) and lightweight union helpers. Note:
`CheckedTypes.FromTypeExpression` is a stub returning `Unresolved`; real
resolution goes through `TypeInferrer.ResolveTypeExpression`.

### 6.2 Public `TypeComparer` API

All methods take `SymbolTree` + `GlobalScope` explicitly (pure static helpers):

- `IsAssignableTo(source, target, …)`
- `IsSubtypeOf(child, parent, …)`
- `AreTypesEqual`
- `UnionTypes` / `IntersectTypes`
- `NarrowType` / `NarrowTypeNegative`
- `ResolveGenericType` / `ResolveGenericTypeBySymbol`
- `ExpandTypeAliases`
- Inheritance helpers (`ImplementsOrExtends`, `EnumerateDirectAncestors`, …)

Cycles are guarded with `visited` pair sets; recursive pairs are treated as
compatible/equal to avoid infinite recursion.

### 6.3 Assignability highlights (`IsAssignableToCore`)

Order and special cases matter (see comments in
`TypeComparer.Assignability.cs`):

1. Unresolved ↔ anything → true.
2. Equality → true.
3. Constrained type parameter source: if its `ResolvedConstraint` is assignable
   to the target → true.
4. Target `mixed` → true; source `never` → true.
5. `void` encodings unified (`SpecialCheckedType` vs builtin `\void`).
6. **Union sources** checked per-member **before** mixed/nullable guards
   (so `Foo|null` and unions containing mixed-like members behave correctly).
7. Source `mixed` only to `mixed` / `?mixed`.
8. Null literals / builtin `null` vs nullable targets.
9. Nullable source vs non-nullable target rejected unless target union accepts
   null.
10. Unwrap nullable targets; special-case nullable source vs union targets.
11. Iterable / array gradual rules (`TryCheckIterableAssignability`).
12. Union targets (any member; `bool` → union that covers both `true` and
    `false`, or an explicit `bool` member); intersection sources (any);
    intersection targets (all — struct members structural via
    `SourceSatisfiesStruct`).
13. Literals (bool → bool/true/false; template strings; …).
14. Same-declaration `GenericCheckedType` pairs: type arguments decide
    assignability (user generics **invariant** via mutual assignability /
    equality, plus a one-way carve-out `G<T>` → `G<mixed>` when `T` is not
    `void`/`never`; `array`/`iterable` **covariant**). Matching bases with
    incompatible args return false — do not fall through to declaration-only
    `ImplementsOrExtends` (that would accept `Box<string>` as `Box<int>`).
    Different bases may still use object nominal subtyping; callables,
    symbol-name types, etc. follow afterward. Explicit `in`/`out` variance
    is not implemented; the mixed carve-out is not general covariance.

**Operator convert at call/return/`new` (not in `IsAssignableToCore`):**
`TypeComparer.IsAssignableViaOperatorConvert` mirrors AliasConverter's implicit convert rewrite —
convert-to when the source class declares `operator convert(self): Target`, convert-from when the
target class declares `operator convert(Source)`. Used by `CheckerRuleContext.IsAssignableAllowingOperatorConvert`
and `TyhpChecker.CheckReturnType` only. Plain assignments stay ordinary assignability. This is
**not** Story 31 Idea 2 (`*Convertible` / accept `T|TConvertible` everywhere). When the source
resolves to a **trait** (`$this` inside a trait method types as the trait itself), convert-to also
accepts a composing class's convert-to (`TraitComposingClassHasConvertToOverload`, mirroring
`AliasConverter`'s emit-side fallback). Binary/unary overload return inference for trait-`$this`
uses the same composing-class enumeration with an agree-on-resolved-return policy (see §7.6).

**Use-site enforcement (beyond assignability):** unnarrowed `mixed` / `?mixed`
is rejected by `CheckerHelpers.ReportMixedRequiresNarrowing` (TYHP4160) when used
in type-specific operations — member access, calls/invoke, indexing, arithmetic /
bitwise / concat (including compound assigns other than `=` / `??=`), logical
operands, unary numeric/`!`, and foreach. Comparison, `instanceof`/`is`, coalesce,
and casts are allowed (they enable narrowing or are assertions). Unary `clone` on
`mixed` uses TYHP4073. Keyword call forms `clone(...)` (ArgumentList operand) are
validated against the ExtCore `clone` stub for arity / named args / argument types,
but the **result type** is the type of the cloned object argument (same as unary
`clone $x`), not the stub's declared `object` return. Existence-probe contexts skip
the check. Unresolved stays permissive so error recovery does not cascade.

`IsUnnarrowedMixed` also treats a **union that contains** unnarrowed `mixed`
(e.g. `mixed|string`) as requiring narrowing, matching bare `mixed`. Separately,
`TypeDeclarationValidationRule` rejects `mixed`/`never` inside unions/intersections
(`CheckerMixedInComposite` / TYHP4054) using `TypeComparer.IsMixedType` /
`IsNeverType` (not the raw `.IsMixed` flag), because named builtins often resolve to
`SimpleCheckedType` rather than the `SpecialCheckedType` singleton. Generic
type-parameter **constraints** (`T extends void|mixed`) are exempt via
`CheckerState.IsGenericConstraintPosition` — Promise-style bounds intentionally
admit `mixed`. Named `mixed`/`void`/`never` resolution through `PhpNamedTypeAst`
maps to the same singletons as the builtin path (`FromResolvedTypeSymbol`).

### 6.4 Subtyping (`IsSubtypeOfCore`)

Used for inheritance-style questions and some narrowing/algebra. Object decls
use `ImplementsOrExtends` with a depth cap (`MaxInheritanceDepth = 100`).

**Quirk documented in code:** `implements` / `extends` clauses are parsed as
`IClassName`, so `ObjectDeclarationSymbol.ImplementsTypes` is often empty.
Resolvers therefore also walk raw AST class-name nodes.

### 6.5 Symbol-name assignability wrapper

`CheckerRuleContext.IsAssignable` does **not** call `TypeComparer` alone. It
uses `SymbolNameTypeAssignability`, which:

- Rejects unresolved → symbol-name target (stricter than raw comparer).
- Accepts string literals verified to exist (`SymbolNameExistenceVerifier`) for
  `__FunctionName` / `__ClassName` / etc.
- Allows erasure assignability for branded symbol-name utility types
  (`__ClassName<User>` → `__ClassName<object>` → `string`; `__EnumName<E>` →
  `__ClassName<E>`; …).
- Routes subclass-as-`class-string` through `__CompatibleTypeName<T>` only
  (`SymbolNameTypeHelper.IsCompatibleBrandAssignable`): `__ClassName<S>` /
  `__EnumName<S>` / `__InterfaceName<S>` / `__CompatibleTypeName<S>` assign to
  `__CompatibleTypeName<T>` when `S` is the same as or a subtype of `T`.
  Parametric `__ClassName<T>` stays invariant between distinct type args.

---

## 7. Type inference

### 7.1 Caching

`InferExpressionType`:

1. Return cached type from `TyhpChecker` if present.
2. Else `InferExpressionTypeCore`, then `SetExpressionType`.

This means the first resolution “wins”; control-flow rules that need side
effects during inference (ternary arm merges) coordinate carefully with this
cache (see ternary comments in `ControlFlowRule`).

### 7.2 Expression inference (`TypeInferrer.Expressions.cs`)

Handles scalars, encaps lists, named constants, magic constants, variables
(including narrowed / property / index / member maps), binary/unary/ternary,
parenthesized `PhpDereferenceableExpressionAst`, dereferenceables, `new`,
closures, `nameof` / `typeof` / `default`, `isset`/`empty`/`variable_exists`,
`match`, and bound function symbols as callables.

Notable behaviors encoded in comments:

- Parenthesized expressions must unwrap or conditions type as unresolved.
- `default(Class)` infers **null** (emitter produces `null`), not the class
  type — otherwise null safety is defeated.
- `typeof(...)` types as `\Tyhp\Type`.
- **Array literals** (`PhpArrayAst` / short-syntax `PhpArrayPairListAst`):
  `InferArrayLiteral` unions widened element/key types. List shorthand
  normalizes to `array<int|string, V>`; maps keep `array<K,V>`. Empty `[]` is
  `array<never, never>` (not one-arg `array<never>` → `array<int|string, never>`)
  so covariant key/value assignability accepts any `array<…>` target, including
  narrowed keys like `array<string, T>`.

### 7.3 Dereferenceables and calls

`TypeInferrer.Dereferenceables.cs` is the largest inference surface:

- Chains: base + suffix (call, instance/static member, class const, array
  access).
- **`::class`**: brands as `__ClassName<R>` (or interface/enum/trait sibling)
  from the receiver type `R`, keeping generics (`self<T>::class` →
  `__ClassName<Promise<T>>`). Erases to bare `__ClassName` then `string`.
  Name bases with type arguments (`self<T>::`) resolve via the same path as
  `new self<T>`. Parameterized `static<…>` is rejected (TYHP4168).
- **`instanceof Foo<…>` / `self<T>`**: emitter reifies to
  `\Tyhp\Type::is($x, Type::generic(…))` so type arguments are checked at
  runtime (native `instanceof` would drop them). Bare `instanceof static` /
  `instanceof Foo` stay as PHP `instanceof`. Narrowing applies the same type
  arguments via `ResolveInstanceofTargetType`. Parameterized `instanceof static<…>`
  is forbidden.
- **Free functions**: binder does not bind call-site names →
  `CheckerHelpers.ResolveFreeFunction`.
- **First-class callable** syntax `foo(...)` → callable signature, not invoke.
  Parameter and return annotations resolve under the callee’s
  `FunctionGenerics` (`InferCallableSymbol` / `InferCallableFromFunction`), so
  shapes like `array<TKey, TValue>` keep type parameters instead of collapsing
  to `unresolved` (and falsely failing `KeyIntOrString`).
- **Invoking a callable value** (`$fn($x)`, property-held `\Closure<…>`,
  `|>` RHS): `TryGetCallableReturnType` / `InferPipeResult` select an arity
  facet, then `CallableGenericInference` binds any remaining
  `GenericTypeParameterSymbol`s from the actual argument types (same structural
  matching as direct-call `TryInferGenericBindings`) and substitutes into the
  return type. `TypeCompatibilityRule` applies the same bindings before
  argument assignability so open generics do not false-positive
  `CheckerIncompatibleArgumentType`.
- **Call / method return types**: `ResolveFunctionReturnType` /
  `ResolveMethodReturnType` likewise fork `FunctionGenerics` (and declaring
  object generics for methods) before resolving the declared return annotation,
  then apply call-site type-argument substitution and argument-driven inference.
- **Method calls**: resolve on receiver before treating `CallableCheckedType`
  as already-invoked return (avoids skipping generic substitution —
  FOUND_BUGS item 39).
- Unresolvable instance methods → `Unresolved` (gradual; avoid cascade).
- Generic call recording for Mechanism D binder routing.
- `ResolveMemberDeclaredType` / `ResolveFunctionDeclaredType` substitute
  receiver / call-site type arguments into declared annotations.

### 7.4 Type annotations

`ResolveTypeExpression`:

1. Prefer the **declaring file’s** namespace/`use` scope for annotations written
   elsewhere (`NameResolutionScope` / `TryGetDeclaringFileResolutionScope`).
2. `ResolveTypeExpressionCore` by AST shape.
3. `TypeComparer.ExpandTypeAliases`.
4. Generic instantiations go through `GenericTypeArgumentValidator`.

While resolving type arguments of a generic instantiation (`callable<…>`,
`array<…>`, `Box<…>`, utility types, …), `CheckerState.IsGenericTypeArgumentPosition`
is set. An undeclared named type in that position reports `BinderSymbolNotFound`
(TYHP3003) at the spelling (e.g. `TResult` inside `callable<?TResult, int>`), with
a DidYouMean suggestion from in-scope type names. Top-level parameter/return
unresolved names stay binder-owned (TYHP3019/3020) so those sites are not
double-diagnosed.

Relative types:

- **Bare `self` / bare `static`:** inherit receiver / call-site type arguments (see
  `docs/content/tyhp_0150_newTypes.md`). Inside an open generic body they stay in terms of the
  class’s own parameters (no silent defaults to `mixed`).
- **`self` / `parent`:** resolve via the enclosing / declaring object. For dereferenceable bases
  (`parent::$prop`, `parent::$prop::get()`), `parent` uses
  `TypeComparer.TryGetParentDeclaration` when `ExtendsType` is null — raw
  `extends` is usually an `IClassName`, not an `ITypeExpression`.
  `Owner::$prop::get()` types as the property type; `::set(...)` as void.
- **Parameterized `self<…>` / `parent<…>`:** allowed. Resolution uses `ResolveRelativeType` as the
  base (not binder `ResolveType` alone), so call-site factories like `: self<T>` preserve method
  generics the same way an explicit class name would.
- **Parameterized `static<…>`:** forbidden everywhere (TYHP4168
  `CheckerParameterizedStaticForbidden`), including `final` classes and `new` / `instanceof` /
  `::class` spellings.
- **Bare `static`:** a distinct `StaticCheckedType` through inference. Illegal as a **parameter or
  property** type (TYHP4066). Nested bare `static` in generic args (`ReflectionClass<static>`) is
  allowed. `$this` in instance methods is typed as `static` so it satisfies `: static` returns;
  ordinary `self` / `new self()` instances do not. At call sites, `: static` expands to the
  receiver / call-site class reference (including type arguments) — fluents on a non-generic
  parent therefore return `GenericBuilder<int>` when invoked on that child.
- Property *declarations* also parse via `typeWithoutStatic`, so top-level `static` properties are
  rejected earlier.

### 7.5 Closures and contextual parameters

- `ClosureRule` owns scope: clears inherited `ExpectedReturnType` (so a closure inside
  `__construct` does not inherit `void`), sets `IsInsideClosure`, registers captures, then
  `ClosureParameterInference.InferAndRegisterParameters`. When the author omitted a return
  type but `ExpectedClosureType` (call-site argument or typed-var annotation) supplies a
  callable facet, that facet's return becomes `ExpectedReturnType` for body checking.
- `ClosureParameterInference` also records an `InferredClosureSignature` (omitted param /
  return slots filled from the facet) on the checker so the emitter can spell recoverable
  PHP typehints that were never written in Tyhp source.
- Non-static closures automatically bind `$this` from the enclosing instance
  method (`BindEnclosingThis`) and re-seed `PropertyInit` across the
  anonymous-function boundary. PHP does not require `use ($this)`; without this
  bind, `$this` is unresolved inside the closure and suffixes such as
  `$this->map[$k]->method()` fall through to `mixed` (false TYHP4160).
- Untyped parameters require `ExpectedClosureType` from the call-site argument
  position (`SetExpectedClosureTypeFromArgument`); otherwise
  `CheckerClosureParameterTypeRequired`.
- **Story 16 Phase 1 — `PropertyPath<T, R>`:** `SetExpectedClosureTypeFromArgument` /
  `SetExpectedClosureTypeFromAnnotation` map `\Tyhp\PropertyPath<TSource, TReturn>` to
  `callable<TSource, TReturn>` so an inline `fn` at a PropertyPath parameter is contextually
  typed. When the argument *is* an inline function, `TypeCompatibilityRule.ValidateArgumentTypes`
  requires arrow syntax (else TYHP4320) and a simple `$param->a->b` / `?->` chain
  (`PropertyPathSupport`, else TYHP4321). Any other argument is checked by ordinary
  assignability, so forwarding an existing `PropertyPath` value or passing `null` to a
  nullable parameter is accepted; only a non-assignable value reports TYHP4320. Passing a
  PropertyPath/Expression value where `\Closure` is expected is allowed at the call site — the
  emitter extracts `->callable`. Type detection keys off the bound declaration, so a user class
  also named `PropertyPath` is never treated as the `tyhp/lambda` type.
  `nameof(fn ($x) => $x->a->b)` is accepted when the fn is a single-parameter arrow whose body
  is a PropertyPath-style chain (last segment; TYHP4321 otherwise) — see `CompileTimeRule` /
  `NameofTypeInferrer`.
- **Story 16 Phase 2–3 — `Expression<TArgs…, TReturn>`:** the same contextual mapping is applied via
  `ExpressionTreeSupport.TryMapToCallable` (callable arity convention: last type arg = return;
  earlier args = parameters). `GenericTypeArgumentValidator` special-cases `\Tyhp\Expression` so
  `Expression<R>`, `Expression<T, R>`, and `Expression<T, T, int>` are all legal even though the
  runtime class is declared `<TSource, TReturn>`; `GenericInheritanceBindings` maps `TReturn` to
  the last argument and `TSource` to the first parameter type (or `mixed` for `Expression<R>`).
  Inline `fn` arguments require arrow syntax (TYHP4323), a body
  composed only of supported expression kinds (TYHP4322 — no assignment / await / yield /
  match / nested fn / free function calls / throw / include, …), and
  definitely-assigned outer captures (TYHP4324). Phase 3 allows `instanceof` / `is` (RHS is a
  type name, builtin, or captured class-name variable). Forwarded `Expression` values and `null` for
  nullable parameters still pass via assignability. Helpers live in `ExpressionTreeSupport.cs`
  (not a Transformers layer). At call sites, `ResolveCalleeParameterType` keeps PropertyPath /
  Expression wrappers that still mention unbound method generics (`select<R>(Expression<T, R>)`)
  instead of collapsing the whole type to mixed; it substitutes those method parameters with
  mixed so the inline `fn` is still contextually typed from the class generic (`T` → `User`).
  `ResolveNamedType` looks up in-scope `ObjectGenerics` / `FunctionGenerics` /
  `EnclosingObject.GenericParameters` before treating a bare name as a generic instantiation.
  `TryInferGenericBindings` for methods also resolves parameter annotations via
  `ResolveDeclaredTypeOnReceiver` (declaring-class `ObjectGenerics` + receiver substitution),
  so chaining off `select<R>(Expression<T, R>)` (`->select(...)->sortBy(...)`) does not
  report TYHP3003 on the class parameter `T` when the return type is inferred at the call site.

### 7.6 Operators

`TypeInferrer.Operators.cs` encodes native PHP numeric promotion / division / exponentiation
result types used when no matching operator overload applies.

When operand types match a declared `operator` form, `TypeInferrer.OperatorOverloads.cs` prefers
that form's declared return type instead (same left-first then right selection as
`AliasConverter` / `OperatorOverloadResolver`, including extension-contributed and native-passthrough
tyhpdef forms for declared return truth). Binary ops, unary ops (`+`/`-`/`~`/`!`/`++`/`--`), and
compound assigns (`+=`, `>>=`, …) all go through this path before falling back to native promotion.
`self` / `static` in the overload return type resolve against the owning class or, for a builtin
extension operator (`extension Foo { operator *<string>(self, int): self }`), against that builtin
— `ResolveOperatorOverloadReturnType` forks `CheckerState` and seeds both `EnclosingObject` (the
declaring extension symbol, so the "used outside class" guard in `ResolveRelativeType` does not
fire) and `EnclosingObjectType` (the actual `self` value) regardless of what the call site's own
enclosing class/object happens to be.

**Trait-`$this`:** `$this` inside a trait method is still typed as the trait itself (there is no
per-composing-class walk of trait bodies). When the trait has no matching form, inference searches
classes/enums that `use` the trait (`TypeComparer.EnumerateObjectsUsingTrait`), remapping
trait-typed operands to each composing class so that class's `self` parameters match (checker
analogue of AliasConverter temporarily pushing the user onto `_classStack`). A hit is accepted only
when **every** composing user that declares a matching form resolves to the **same** return type
(after `self`/`static` expansion against that user). Agreement unblocks the common single-user case
and multi-user cases that share a concrete return (e.g. both `: int`). Disagreement (including two
users both declaring `: self`, which resolve to distinct class types) falls back to native inference
— usually `Unresolved` for two object operands — rather than inventing a first-match type that would
be wrong for other users. Convert-to assignability still has its own composing-class fallback
(`TraitComposingClassHasConvertToOverload`).

PHP 8.5 pipe (`|>`) is special-cased in
`InferBinary`: the result type is the return type of the arity-1 callable facet on the
RHS after argument-driven generic binding from the LHS (opaque `callable` /
`\Closure` / `__invoke` → `mixed`). `TypeCompatibilityRule`
(`TypeCompatibilityRule.Pipe.cs`) validates that the RHS is callable, accepts exactly
one argument, and does not take its first parameter by reference when that is
diagnosable from an FCC or inline closure; open-generic facet parameters are bound
from the LHS before the assignability check.

PHP 8.5 `(void) expr` is a **discard**, not a value-producing cast (`InferUnary` →
`CheckedTypes.Void`). Grammar keeps it out of value positions; assignability still
rejects void if it appears. `TypeCompatibilityRule` (`TypeCompatibilityRule.VoidCast.cs`)
type-checks the operand and allows `mixed` (discard is not a type-specific use).
Wrapping a call is the intentional-discard form for `#[\NoDiscard]`:

- Discarded call / for-list item to a NoDiscard-marked callable → warning TYHP4165
  (`CheckerHelpers.ReportNoDiscardIfDiscarded` from `CheckerRuleContext.CheckStatementBlock`
  for function/method bodies, and from `ControlFlowRule` for nested blocks / for-lists)
- `(void) call` → suppress TYHP4165
- ExtCore `NoDiscard` class is Story 21; until then unbound `#[\NoDiscard]` is
  allow-listed like `Override` / `AllowUnset`, and the attribute is detected by name
  on the callee declaration

`for` condition lists (`for_cond_exprs`): only the **last** item is a boolean
condition; preceding items (including `(void)` and discarded calls) are checked as
side-effect expressions only.

### 7.5 Attribute targets (`AttributeRule`)

`AttributeRule` validates attribute classes (TYHP4126), `Attribute::TARGET_*`
flags (TYHP4127), repeatability (TYHP4128), and `#[Override]` (TYHP4129).

Dispatch:

- Functions / object types / **top-level** `PhpConstDeclListAst` → registry
  `HandledNodeTypes` via `CheckNode`.
- Class members (methods, properties, parameters, enum cases, class consts) →
  `ValidateDeclarationAttributes` from `DeclarationRule` member paths (not
  registered, to avoid double-fire).

Target bits follow PHP 8.5 `zend_attributes.h`:

| Flag | Bit |
|------|-----|
| `TARGET_CLASS` … `TARGET_PARAMETER` | `1<<0` … `1<<5` |
| `TARGET_CONSTANT` (top-level `const`) | `1<<6` (= 64) |
| `TARGET_ALL` | `(1<<7)-1` (= 127) |
| `IS_REPEATABLE` | `1<<7` (= 128) |

`TARGET_CONSTANT` applies only when `EnclosingObject` is null (file / namespace
`const`). Class / enum constants and enum cases require `TARGET_CLASS_CONSTANT`.
Bare `#[Attribute]` / empty args default to `TARGET_ALL`. Flags are read from
the attribute class’s `#[Attribute(...)]` meta (named constants, `|`, or numeric
literals). Unresolvable flag expressions skip the TARGET_* check rather than
guessing. `Override` / `AllowUnset` keep name-based special cases.

---

## 8. Control flow, narrowing, type guards

### 8.1 `ControlFlowRule`

Owns if, loops, try/catch, statement blocks, returns, jumps, conditionals,
yield, goto (prohibited), echo, ternary, and unary `throw` / synthetic unary
`return` (expression-bodied callables).

**If without else:** builds an implicit negative-narrowed path; if the then-arm
`HasReturnedOnAllPaths`, only the negative path is absorbed (dead then-state
must not leak). Comments in `CheckIf` are the authoritative rationale.

**Switch (non-match):** each non-falling-through case group starts from a fresh
`Split` of the pre-switch state. Single-condition arms get positive
`ApplyConditionNarrowing` (multi-condition OR arms do not). A fall-through
target joins two entry paths before the body runs: the continued prior-arm
state after `RevertStaleGuardNarrowing` (drop unused prior-guard assumptions;
keep real assignments), and a fresh direct-entry `Split` with this arm's own
single-condition guard applied. The body is checked once against that merge so
uses safe on only one path are rejected. Case-label expressions are checked on
a pre-switch probe. `HasReturnedOnAllPaths` resets at the start of every arm.

Conditions are type-checked on a disposable probe so progressive `&&`
narrowing during validation does not leak into the post-if continuation
(`CheckConditionExpression`).

Logical conditions must be bool-ish (Tyhp differs from PHP truthiness) —
enforced via helpers used from control-flow checking.

**Foreach / catch variables:** `DeclareForeachVariable` (and catch bindings in
`CheckTryCatch`) record the loop/catch variable's type on those AST nodes via
`ResolveExpressionType`, including the inner `$var` under the extra
`PhpVariableAst` wrapper `VisitForeachVariable` adds. `ControlFlowRule`
suppresses child traversal on `PhpLoopAst` / `PhpTryCatchAst`, so those binding
sites would otherwise never enter `InferExpressionType` — language-server hover
on `as $x` or `catch (E $e)` would have no checker type.

### 8.2 `TypeNarrowingRule`

Static helper (not registered as `ICheckerRule`). Entry:
`ApplyConditionNarrowing(condition, branchState, context, symbolTree, globalScope, positive)`.

Supports:

- Unwrap parentheses and logical `!` (flips polarity).
- `&&` in positive / `||` in negative (De Morgan); other combinations do not
  narrow.
- `instanceof` / `is` (variables, `$this->prop`, `$var->prop`, constant index).
- Null comparisons.
- `isset` / existence probes.
- Built-in guards: `is_string`, `is_int`, … (`BuiltInTypeGuards` map).
- User type-guard callables (`$param is Type` return types), including ExtCore
  stubs and call-site generics (`isType<int>($x)`, `\class_exists<Foo>($n)`).
  Omitted trailing type arguments use each parameter's default
  (`T extends object = object` → narrow to `__ClassName<object>`). Prefer this
  path whenever the resolved callee has a `TyhpReturnTypeGuardAst` (both
  polarities). Call-site substitution for ordinary (non-guard) calls does **not**
  eagerly apply those defaults — omitted type arguments stay open until
  argument-driven inference fills them. Applying a `TReturn = void` default
  before inference would require `callable(): void` and reject real callbacks.
- Symbol-name guard **fallback** (`SymbolNameGuards`): only when the callee still
  returns plain `bool` (e.g. `property_exists` / `method_exists` / `is_a` /
  `is_subclass_of`, which capture the receiver type as a brand type argument).
  `class_exists` / `function_exists` / `interface_exists` / `trait_exists` /
  `enum_exists` are driven by ExtCore tyhpdef return-type guards instead.

Assignment resets narrowing via `ResetNarrowingOnAssignment` (variable + index
+ member maps).

Positive/negative type algebra uses `TypeComparer.NarrowType` /
`NarrowTypeNegative`.

### 8.3 Type guards on declarations

`TypeGuardValidation`:

- Guard return AST → expected return type is `bool`.
- Validates the guarded parameter name exists.
- `DeclarationRule` sets `IsTypeGuardFunction` when checking such callables.

### 8.4 Null safety and definite assignment

`NullSafetyRule` reports use-before-assign / possibly-null at variable and
`$this->prop` reads, but suppresses diagnostics under existence-probe contexts
(`??`, `??=`, `isset`, `empty`, `variable_exists`) and skips simple assignment
LHS as reads.

`PropertyInitializationAnalysis` seeds constructor vs instance-method
property-init maps (Prop-init #7) and records post-construction guarantees on
symbols for later methods. `UnsetTrackingRule` implements Prop-init #8.

---

## 9. Generics: constraints, validation, resolvers

### 9.1 Constraint resolution

`GenericConstraintResolver.ResolveAll` / `EnsureResolved`:

- Resolves `GenericTypeParameterSymbol.Constraint` AST into
  `ResolvedConstraint` via `context.ResolveTypeAnnotation`.
- Cycles erase to `mixed`.
- Sibling parameters in constraints resolve as themselves; bound substitution
  happens later in assignability when asking whether `T` is assignable to a
  target.

Called when entering function/method/object scopes in `DeclarationRule`.

### 9.2 Argument validation

`GenericTypeArgumentValidator.ValidateInstantiation`:

- User classes / aliases — arity with **omitted trailing defaults filled**
  (`ResolveAndValidateUserTypeArguments`); per-parameter constraints. Bare
  `Box` / `new Box()` apply defaults unless resolving inside the open generic
  itself. Declaration sites also run `ValidateGenericParameterDefaults`
  (TYHP4310–4312).
- Built-ins / utilities / `callable` — existing arity + constraint rules.
- Utility types → `UtilityTypeResolver`.
- Builtin `callable<…>` → `CallableCheckedType`.
- Builtins with `GenericParameterRequirements`.
- User classes / aliases — arity + per-parameter constraints;
  `void`/`never` restricted unless constraint opts in.

### 9.3 Substitution

- By name: `ResolveGenericType`.
- By **symbol identity**: `ResolveGenericTypeBySymbol` — required when nested
  declarations reuse the same parameter spelling (`Derived<T> extends Base<T>`)
  (FOUND_BUGS item 11 comments in code).

### 9.4 Runtime generic emit flags

Two mechanisms in the **current checker API** (names match code /
`CompilationResult`):

1. **Class/enum GenericObject tracking** —
   `MarkRequiresRuntimeGenericTracking` when a class/enum body needs bound
   class type parameters at runtime (`typeof(T)` / `default(T)` /
   `instanceof T` on **class** generics). Interfaces/traits skipped; only
   decls with generic parameters.

2. **Callable Mechanism D binders** —
   `FlagGenericVariantIfNeeded` uses `CheckerHelpers.UsesGenericAtRuntime` on
   the body for **function/method** generics, then
   `MarkRequiresGenericVariant`. The scan covers `typeof`/`default`/`instanceof`/`is`
   and type arguments on `new Foo<T>` / `new self<T>` (grammar addons are not
   AstChildren; parameterized `new static<…>` is rejected as TYHP4168). After the full walk,
   `PropagateGenericVariantAcrossHierarchies` unions override/implement
   families so a call through a base/interface cannot silently miss the
   binder.

`RecordGenericCallTargetsIn` records call sites with explicit type arguments
so the emitter can route to `__tyhpGeneric` even under suppressed subtrees.

> Design note: Emit is Mechanism D (Closure binder + curried call sites). API
> names like `RequiresGenericVariant` / `__tyhpGeneric` are shared D+C ABI
> legacy — see `CHECKER_GAPS.md` Mechanism A residual audit. Flat Mechanism A
> emit is gone.

### 9.5 Utility types

`UtilityTypeResolver` expands `\Tyhp` utilities (`Readonly`, `Partial`,
`Pick`, `Awaited`, struct helpers, symbol-name brands, type-name algebra, …)
and global `__` utilities (`__FunctionReturnType`, `__CallableReturnType`, …)
at annotation resolution time. Per-parameter constraints for built-in
utilities run through `GenericTypeArgumentValidator.ValidateUtilityConstraints`
(shared `ValidateBuiltInConstraint`) before the behavior-specific `Resolve*`
methods. `\Tyhp\ReturnType` / `\Tyhp\Parameters` / `__CallableReturnType` /
`__CallableParametersStruct` / `__CallableParametersTuple` /
`__CallableParametersRest` rely on the
registered `Callable` constraint (`SatisfiesCallableConstraint`) for invalid
arguments — those resolvers extract shapes only and do not emit a second
`CheckerUtilityTypeInvalidArgument`. Empty `callable<>` / `\Closure<>` does
not satisfy `Callable` (generic forms need at least one type argument under
the return-last convention). Unbound generic type parameters and unresolved
recovery types do satisfy `Callable` so `TCallable extends callable` can
appear as a type argument without a spurious TYHP4035 at the declaration.
Unions of callables and intersections that include a callable also satisfy
`Callable`, so `__CallableReturnType<callable<int>|callable<string>>` and
optional-arity intersections are not rejected before return-type extraction.
A non-callable already reported as TYHP4035 resolves to the unresolved
recovery type rather than `mixed`, so narrowing diagnostics do not pile on
top of the original failure. An unbound type parameter (`TCallable extends callable`) is different: `__CallableReturnType<TCallable>`
and `\Tyhp\ReturnType<TCallable>` stay as a `GenericCheckedType` of that utility
so call-site substitution can fill `TCallable`. `TypeComparer.SubstituteType`
then calls `UtilityTypeResolver.ExpandAfterSubstitution`, which re-runs
return-type extraction (including unions of callables → union of returns)
and collapses the wrapper to the concrete return type. A substituted
non-callable does not leak as `__CallableReturnType<int>`; it recovers to
unresolved. Bare opaque `callable` / `\Closure` reflects successfully with
zero parameters and a `mixed` return.

Invoking a value typed as a callable type parameter (`TCallable $cb; $cb()`)
infers `__CallableReturnType<TCallable>` via
`UtilityTypeResolver.MakeDeferredCallableReturnType`. That wrapper compares
equal to `\Tyhp\ReturnType<TCallable>` (same type argument), so
`return $cb()` type-checks against either spelling.

`CallableSignatureReflection` is the shared helper: given a callable-ish
`ICheckedType`, it produces an ordered parameter list `{ name?, type,
optional, variadic, byRef }` plus the return type. Facet / `callable<…>` /
`\Closure<…>` forms come from `CallableArityFacetBuilder` (longest facet for
the parameter list when optional-arity intersections are present; return type
follows the first facet, matching `TryGetCallableReturnType` when no call
arity is selected). Function, method, and closure symbols use
`FromParameterInfos` / `FromClosureParameters` so names and by-ref / variadic
flags survive. Those names are also stored on `CallableCheckedType` (equality
ignores them). Unions of callables are merged by `TryReflect` when arities
match; `TryGetReturnType` still unions returns even when arities differ.
`__CallableReturnType` resolves through this helper (including
after generic inference). `__CallableParametersStruct<TCallable>` expands to a
synthetic struct keyed `$name` for each non-variadic named parameter; nameless
`callable<…>` facets degrade to an empty struct (no string keys).
`__CallableParametersTuple<TCallable>` expands to a synthetic struct keyed
`$_1`, `$_2`, … with integer array-key aliases `0`, `1`, … (same shape family
as hand-written `CallableArgs*`); nameless facets still produce those int
keys. Defaulted parameters (and parameters beyond the shortest arity facet)
are `StructPropertyInfo.IsOptional` so a partial bag can omit them. Required
parameters stay required fields. This is required-key assignability on one
struct — not an intersection of every key-subset bag, and not `\Tyhp\Partial`
(which would make the field types nullable). Variadic parameters are omitted
from both bags; extra keys/indices stay TYHP4031, matching arity-facet policy
(unbounded extra args are not modeled on the bag). An unbound `TCallable`
stays a deferred `GenericCheckedType` until substitution, then
`ExpandAfterSubstitution` re-resolves the bag.

`__CallableParametersRest<TCallable>` is the TypeScript `...args: Parameters<T>`
analogue. It stays a `GenericCheckedType` wrapper even after `TCallable` is
bound (it does not collapse to a Tuple struct) so call-site checking can
unpack trailing arguments. Used as `__CallableParametersRest<TCallable> ...$args`
on a generic wrapper, `ValidateArgumentTypes` infers `TCallable` from the
sibling callback, reflects the callable's parameter list, and checks each
remaining positional argument 1:1 (TYHP4010 on a type mismatch, TYHP4142 when
a required parameter is omitted, TYHP4143 when there are extra arguments and
the callable is not itself variadic). Defaulted callable parameters may be
omitted. A trailing variadic on the *callable* accepts extra rest args at that
element type. Bare opaque `callable` / `\Closure` (unknown arity) and unbound
`TCallable` stay gradual. Unions of callables merge when every non-null member
has the same non-variadic arity (parameter types are unioned; a slot is
optional only when every member marks it optional); mismatched arities and
opaque members stay gradual rather than inventing a 0-parameter list. A
trailing spread (`invoke($cb, ...$packed)`) and a named pack into the Rest
parameter (`args: $x`) are not treated as an empty rest list — they skip
TYHP4142/4143 because the supplied values are not statically counted. Named
`args: $x` does not start positional unpack (PHP packs that one value into the
variadic). Positionals after a rest-region spread (`invoke($cb, ...$packed, $x)`)
are not typed as inner parameter 0; only positionals before the first spread
are checked 1:1. Emit erases Rest to `mixed` so PHP does not demand each unpacked
argument be an `array`. Inside the wrapper body, `$args` is the positional bag
(or untyped `array` while `T` is open), not `array<int, Rest<T>>`.

At a call site, `ValidateArgumentTypes` fills `TCallable` from the sibling
callback argument. Those bindings are applied *only* to parameter types that
carry a deferred callable-signature utility
(`TypeCompatibilityRule.ApplyInferredBindings`), and the inference itself runs
lazily on first such parameter. Ordinary generic parameters keep the gradual
mixed policy: inference binds from argument values, so
`run<TValue>(callable<TValue, TValue> $cb, TValue $seed)` called with `1` binds
`TValue` to the literal type `1`, and feeding that back into a parameter would
demand a callback returning exactly `1`. Laziness also matters for ordering —
inferring up front would type closure arguments before the closure branch
supplies their contextual parameter types.

ExtStandard `\call_user_func` is a single Rest-unpack signature
(`TCallable $callback, Rest<TCallable> ...$args): ReturnType<TCallable>`).
`\call_user_func_array` keeps two same-arity overloads (named Struct bag vs
positional Tuple bag). `FunctionOverloadSelector` refines
`SelectFunctionOverloadForCall`'s arity filter by scoring argument/parameter
compatibility — array-literal key shape (`StructBagLiteralChecker.Classify`)
picks Tuple for list / int keys and Struct for string keys. Named struct
variables (including hand-written `CallableArgs*`) are scored by materialized
shape (`HasIntegerKeyAliases`) so a `CallableArgs2` value selects Tuple rather
than the named bag. Untyped `array` arguments still use
`\call_user_func_array_unsafe`. Hand-written `CallableArgs*` structs remain as
examples; the arity ladder is gone from the builtins. Named structs are
structurally assignable to matching synthetic bags (and to other named structs
with a compatible schema).

Array literals used as named bags are checked at the AST
(`StructBagLiteralChecker`): unknown keys are TYHP4031, wrong value types are
`CheckerTypeMismatch`, missing required keys are TYHP4325. Eligibility is
decided for the whole literal before anything is reported — a spread,
positional, or dynamically keyed entry sends the literal back to ordinary
assignability rather than leaving half of it reported twice. Quoted keys get
no "did you mean" fix (the decoded name is shorter than the source it was
written as, so the edit span would cut into the quotes), matching
`WithKeywordRule`. Empty `[]` is valid when every bag field is optional
(all-defaulted parameters); omitting a required key is TYHP4325. Structural
assignability (`IsStructAssignableToStruct`) likewise allows a source that
lacks optional target keys and rejects a source that lacks a required key.
A source property marked optional cannot satisfy a required target key
(runtime instances of the source may omit it). `\Tyhp\Partial` / `\Tyhp\Required`
/ `\Tyhp\Pick` / `\Tyhp\Omit` materialize a property shape via
`StructTypeHelper.TryGetPropertyShape` (same path as `\Tyhp\Readonly`), so they
apply to named struct/class declarations rather than only already-built
`StructCheckedType`s. `\Tyhp\Partial` sets `IsOptional` on every field (and
wraps types as nullable); `\Tyhp\Required` clears it. Pick/Omit accept `'name'`
or `'$name'` for keys stored as `$name`. Bags are checked where a real
`CheckerState` exists — arguments, typed variables, assignments (`=` / `??=`),
parameter defaults, and returns; the file-name-only `TyhpChecker.CheckAssignment`
overload deliberately stays on plain assignability.

Positional bags take the same path once the target struct carries integer
aliases: list literals (`['Ada', 36]`) and explicit int keys
(`[0 => 'Ada', 1 => 36]`) match by index, while string keys `'_1'` / `'_2'`
still match the property names. Numeric string keys follow PHP's own folding
rule — only the canonical decimal spelling of the int becomes an int key, so
`'0'` is index `0` but `'00'`, `' 0'`, `'+1'`, and `'-0'` stay string keys and
are reported as unknown properties. Constant `$args[0]` index access infers the
matching parameter type (the bag erases to an int-keyed array, so no extra emit
rewrite is needed). Trailing optional indexes may be omitted; extra indexes
remain TYHP4031. Variadic parameters are omitted from the bag (excess args are
not modeled; arity-facet call checking still applies when invoking the callable
directly).

Integer aliases also drive struct → array widening
(`TypeComparer.AreStructKeysAssignableTo`). A struct erases to an array keyed by
its property names, so a string-keyed struct still does not fit
`array<int, V>`; a fully positional bag is the mirror image and does not fit
`array<string, V>` but does fit `array<int, V>`. The `array<V>` shorthand
normalizes to an `int|string` key and admits both.

---

## 10. Coding conventions and patterns

1. **Rules stay thin** — heavy logic in static helpers or `TypeComparer` /
   `TypeInferrer` partials.
2. **Exact AST types** in `HandledNodeTypes` — no interface dispatch.
3. **Document CheckNode bypasses** — if `CheckObjectBody` calls you directly,
   omit the type from registry *or* provide a static entry and call it
   explicitly (prefer comments like those on `AsyncRule` / `AttributeRule`).
4. **Suppress + re-walk** — when suppressing children, walk what you need with
   `context.CheckNode` under the right state.
5. **Clone-on-write** for variable/property mutations in branches.
6. **Join discipline** — if/else: `then.Merge(else)` then
   `state.AbsorbJoinedVariables(then)`; never merge each arm into the
   pre-branch state separately.
7. **Diagnostics** — prefer `CheckerHelpers.ReportError(context, …)` /
   `context.ReportError` so max-errors-per-file applies; direct
   `diagnostics.AddError` bypasses the cap (used in some helpers).
8. **Unresolved vs mixed** — use unresolved for recovery; do not invent a
   user-visible `unknown` type.
9. **SilentDiagnostics** — hierarchy probes in `TypeComparer` use a private
   silent bag so failed lookups do not spam errors.
10. **Partial classes** — large rules/comparers/inferrers split by concern;
    keep public surface on the primary file.

---

## 11. Important helpers

| Helper | When to use |
|--------|-------------|
| `CheckerHelpers.ResolveFreeFunction` | Call-site free function symbols |
| `CheckerHelpers.SelectFunctionOverloadForCall` | Arity-based tyhpdef overload pick (too-few / too-many bounds) |
| `FunctionOverloadSelector` | Same-arity type pick among tyhpdef overloads (Story 16.5 — Struct vs Tuple bags) |
| `CheckerHelpers.ReportError*` / `ReportWarning` / `ReportInfo` | Diagnostics with file/line/end span from the AST node when `EndLine`/`EndColumn` are set |
| `CheckerHelpers.ReportErrorWithDidYouMean` | Unknown-name suggestions (Story 14) |
| `CheckerHelpers.IsThrowableType` / `IsBoolType` / `IsIterableType` | Control-flow / catch / condition checks |
| `CheckerHelpers.ResolveInstanceofTargetType` | instanceof RHS typing |
| `CheckerHelpers.UsesGenericAtRuntime` | Flag Mechanism D binder / GenericObject needs |
| `CheckerHelpers.CheckCompileTimeConstructsInTree` | typeof/default under suppressed trees |
| `CheckerHelpers.NamesGenericParameterIn` / `SoleTypeName` | Generic runtime-use detection |
| `CheckerHelpers.IsInStaticContext` | `$this` / generic-static diagnostics |
| `CheckerHelpers.IsExtensionReceiverThis` | Allow `extends T $this` in static extension methods (not TYHP4097) |
| `PropertyInitializationAnalysis.*` | Seed/record `$this` init maps |
| `CheckedTypeDisplay.FormatUnion` / `FormatNullable` | Canonical diagnostic `DisplayName` for unions / nullables (`?T` vs `…|null`) |
| `ClosureParameterInference.*` | Contextual closure params + `InferredClosureSignature` for emit |
| `PropertyPathSupport.*` | Story 16 Phase 1 — PropertyPath type detection + property-chain walk; Phase 3 `nameof(fn)` last-segment helper |
| `ExpressionTreeSupport.*` | Story 16 Phase 2–3 — Expression type detection, callable-arity args, body validation (including `instanceof`/`is`), captures |
| `TypeGuardValidation.*` | `$x is T` return types |
| `GenericConstraintResolver` | Before comparing constrained `T` |
| `GenericTypeArgumentValidator` | At generic instantiation sites |
| `UtilityTypeResolver` | `\Tyhp\…` and global `__` utility annotations |
| `CallableArityFacetBuilder` | Build/select callable arity facets from parameter lists |
| `CallableSignatureReflection` | Story 16.5 — ordered params + return from a callable-ish `ICheckedType` or binder `ParameterInfo` list |
| `SymbolNameTypeHelper` / `SymbolNameTypeAssignability` / `SymbolNameExistenceVerifier` | Branded name types |
| `NameofTypeInferrer` | `nameof` expression typing; in-scope generics type as plain `string` (emitter folds to the parameter spelling); `nameof(fn ($x) => $x->a->b)` types as `__PropertyName<T>` when the lambda parameter is annotated |
| `StructTypeHelper` | Struct shape helpers; substitutes `GenericCheckedType` args via `GenericInheritanceBindings` |
| `GenericInheritanceBindings` | Symbol-keyed receiver/extends generic bindings (shared by member substitution and struct shapes) |
| Template-string types (`TemplateStringPattern`, matcher, budget) | Pattern-typed strings |

---

## 12. Weirdness / non-obvious design (with WHY)

1. **SuppressChildTraversal + manual scans** — default walk would break
   control-flow state; compensation scans exist so emit flags and nested calls
   are not silently dropped (`RecordGenericCallTargetsIn` comments are the
   clearest statement of this).

2. **Class members bypass `CheckNode`** — `DeclarationRule` owns object body
   order (attributes, async validation, property-init seeding). Registry rules
   for `PhpMethodDeclAst` would double-fire or never fire; code chooses
   explicit static hooks. Property-hook final overrides (TYHP4166) are checked
   here too: walk ancestors like `TryFindOverriddenMethod`, but continue past a
   level that redeclares the property without the same `get`/`set` (partial
   override). The nearest ancestor that *declares* that hook decides (`final` →
   error). A plain unhooked property breaks the chain. Hook `final` is read from
   the AST via `DeclaringAstNode` (`PhpPropertyAst` or promoted `PhpParameterAst`),
   not from `ObjectPropertySymbol`. Authored `&get` (by-ref get) is rejected with
   TYHP4167 when `CheckerOptions.PhpVersion` is below 8.4 — the polyfill path
   cannot preserve by-ref semantics through magic `__get`, and silent by-value
   lowering would change aliasing. Native `&get` is emitted only for PHP ≥ 8.4.

3. **Extension `extends T $this` is a parameter, not instance `$this`** —
   `ExtensionRule` suppresses child traversal and checks bodies under
   `StaticMethodDeclaration` (extensions emit as static methods). It seeds
   `EnclosingObject` (`IsExtension`) and registers parameters so bare
   `$this` / `$this->…` resolve to the receiver type.
   `CheckerHelpers.IsExtensionReceiverThis` exempts that receiver from
   TYHP4097 (`CheckerThisInStaticContext`). Real static methods and static
   closures still reject `$this`.
   Standalone `extension { operator +<T>(self …): self }` members take the
   same seeding path via `CheckExtensionOperatorOverload`: `EnclosingObject`
   is the extension symbol (so `IsExtension` is visible) and
   `EnclosingObjectType` is the resolved `<T>` target so `self`/`static` mean
   the extended type, not the extension class — including builtins such as
   `string` / `int` from `operator *<string>(self …)`. Without that seed,
   `OperatorOverloadRule`'s return-type resolve reports TYHP4064 for every
   documented standalone extension operator.

4. **`EnclosingCallable` through closures** — nested named functions must still
   be detected; return attribution must use `IsInsideClosure`.

5. **If-without-else negative path** — PHP/Tyhp idioms rely on negative
   narrowing after early-return guards; earlier merges leaked then-arm
   null-narrowing into continuation (see `CheckIf` comments).

6. **Typed locals + sibling blocks** — duplicate `int $id` in two consecutive
   `foreach` bodies is allowed; `DeclaringBlockScope` distinguishes shadowing
   from exited sibling scopes.

7. **Annotation resolution uses declaring file scope** — short names in
   property types must resolve where they were written, not where they are
   read.

8. **Generic substitution by symbol identity** — name-keyed maps collide across
   nested generic decls.

9. **`default(T)` types as null for objects** — matches emitter output; keeps
   null safety honest.

10. **Import unused flush after all files** — mid-walk flush would attribute
   diagnostics to the wrong file / miss later uses (`ImportRule` comments).

11. **Template-string budget is thread-static** — nested assignability checks
    share one budget; exhaustion reports
    `CheckerTemplateStringMaxStatesExceeded` instead of a generic mismatch.

12. **Progressive `&&` narrowing during condition check must not leak** —
    conditions are checked on a probe snapshot.

13. **Mechanism D hierarchy propagation runs post-walk** — a method body may be
    checked after a call site in another file; flags inferred from bodies must
    be applied to entire override families afterward.

---

## 13. Interactions with Binder, Emitter, diagnostics

### Binder

- Provides `GlobalScope`, bound declaration symbols, generic parameter lists,
  overload lists, import symbols.
- Does **not** bind free-function call names inside bodies — checker resolves
  them.
- **Exception (Story 14.5):** `exit(...)` / `die(...)` / `clone(...)` keyword
  call forms (`PhpUnaryOpAst` + `PhpArgumentListAst` operand) get
  `BoundSymbol` set to the ExtCore tyhpdef function. Checker
  `TypeCompatibilityRule.TryCheckKeywordConstructCall` then runs the same
  arity / named-arg / type pipeline as `CheckCall`. Inference for
  `clone(...)` uses the first object argument's type (like unary clone), not
  the stub's `object` return. Unary `clone $x` / parenthesized `clone($x)` and
  bare `exit;` stay unbound and keep the unary clone object check (TYHP4073).
- Extends/implements binding gaps are diagnosed again in
  `DeclarationRule.CheckInheritanceTargets` because binder paths may miss
  `IClassName` clauses.

### Diagnostics

- Errors go through `DiagnosticBag` with `MessageCode` values
  (`Checker*` codes).
- `TyhpChecker.TryAddError` enforces `MaxErrorsPerFile` and emits
  `CheckerErrorThresholdReached` once per file when the cap is hit.
- Fatal unexpected exceptions in `CheckParsedFiles` become
  `CheckerUnknownError`.

### Emitter

`CompilationService` copies checker outputs into `CompilationResult`; emit
builds `EmitContext` with the same sets/dictionaries. Emitter uses them for:

- `GenericObject` trait injection (`RequiresRuntimeGenericTracking`)
- `__tyhpGeneric` dual emission and call rewriting (`RequiresGenericVariant`,
  `GenericCallTargets`)
- WeakReference closure capture / disposable try-finally
- Inferred closure param/return typehints for emit (`InferredClosureSignatures`)
- Async foreach desugaring

`NarrowedTypes` is documented for optimizer/LSP consumers; it is populated by
`RecordNarrowedType` during narrowing.

The checker does **not** emit PHP. It only validates and flags.

### Pipeline gating

Parse errors → no bind. Bind failure / null scope → no check. Check errors do
not by themselves skip recording side-channels; the checker still fills the
result fields when `Check` completes.

---

## 14. Common pitfalls for contributors

1. **Registering `PhpMethodDeclAst` without a DeclarationRule hook** — your
   rule may never run (or may double-run if both paths exist). Follow existing
   patterns.

2. **Suppressing children and forgetting to walk arguments / nested calls** —
   type errors and generic call targets silently disappear.

3. **Mutating snapshot state** — locked snapshots throw
   `InvalidOperationException`.

4. **Merging if/else into the pre-branch state** — definite assignment never
   clears; use absorb-after-join.

5. **Using `TypeComparer.IsAssignableTo` from rules that need symbol-name
   literal existence** — use `context.IsAssignable` instead.

6. **Assuming `BoundSymbol` on call-site function names** — use
   `ResolveFreeFunction`.

7. **Treating `unresolved` as a language type** — it is recovery; prefer fixing
   the resolution failure.

8. **Forgetting `GenericConstraintResolver.ResolveAll`** when introducing new
   generic scopes — constrained `T` will not subtype its bound.

9. **Name-keyed generic substitution across nested decls** — use
   `ResolveGenericTypeBySymbol` when parameter symbols differ.

10. **Relying on `CompileTimeRule` alone to flag `typeof(T)`** — use
    `UsesGenericAtRuntime` / `FlagGenericVariantIfNeeded` for emit correctness.

11. **Piping test output through `head`/`tail`/`grep`** — project shell rule:
    redirect to a temp file and read it (unrelated to checker logic, but common
    when iterating on checker tests).

12. **Editing emitted `runtime/packages/*/src` to paper over checker bugs** —
    fix Tyhp/tyhpdefs instead (workspace runtime-package rule).

---

## 15. Test map (where to look)

Under `tests/Tyhp.Tests/Checker/`:

| Area | Example tests |
|------|----------------|
| Orchestration / smoke | `CheckerTests.cs`, `ValidCodeNoErrorsTests.cs` |
| Comparer | `TypeComparerTests.cs`, `GradualArrayAssignabilityTests.cs`, `ConstrainedGenericAssignabilityTests.cs`, `CheckedTypeDisplayNameTests.cs` |
| Inference | `TypeInferrerTests.cs`, `CallReturnTypeInferenceTests.cs`, `TrueFalseLiteralTypeTests.cs`, `ArrayLiteralInferenceTests.cs` |
| Narrowing / guards | `TypeGuardRuleTests.cs`, `PropertyNarrowingRuleTests.cs`, `CompoundAssignmentNarrowingTests.cs` |
| Definite assignment / props | `DefiniteAssignmentRuleTests.cs`, `PropertyInitializationRuleTests.cs`, `UnsetTrackingRuleTests.cs` |
| Calls / members | `CallArgumentValidationTests.cs`, `MemberDispatchRuleTests.cs` |
| Generics | `GenericInheritanceSubstitutionTests.cs`, `DefaultAndGenericTrackingRuleTests.cs` |
| Declarations / OOP | `MethodOverrideRuleTests.cs`, `OverrideGenericSignatureTests.cs`, `InterfaceImplementationRuleTests.cs`, `InheritanceTargetRuleTests.cs`, … |
| Attributes | `AttributeRuleTests.cs`, `ConstAttributeTargetCheckTests.cs` |
| Features | `Async`/`Overload`/`OperatorOverload`/`Phase*` suites |

Most integration-style tests parse+bind then `new TyhpChecker(...).Check(...)`
and assert diagnostics.

---

## Open Questions / Needs Clarification

1. ~~**Mechanism A vs Mechanism D**~~ — **Resolved (Phase 0 / audit 2026-08-06):**
   Checker flags (`RequiresGenericVariant`, `GenericCallTargets`) already drive
   Mechanism D emit (`TyhpEmitter.GenericVariants.cs`). Flat Mechanism A is gone.
   Residual naming of `__tyhpGeneric` / `RequiresGenericVariant` is intentional
   ABI legacy (optional cosmetic rename = Phase 1, not required).

2. ~~**`ExpressionTypes` consumer**~~ — **Resolved (Story 16 Phase 2):**
   `CompilationService.CheckParsedFiles` copies `checker.ExpressionTypes` onto
   `CompilationResult.ExpressionTypes`, and `EmitContext.Create` / `BuildAction`
   forward it so `ExpressionTreeEmissionHelper` can spell per-node runtime type
   strings (falling back to `'mixed'`).

3. **`CheckedTypes.FromTypeExpression` stub** — still returns `Unresolved`
   with a “Phase 2” comment though `TypeInferrer` is the real path. Is the
   stub obsolete API that should remain for compatibility, or pending removal?

4. **`readme.md` vs implementation completeness** — the informal readme lists
   rules such as catch/throw Throwable constraints; much is implemented in
   `ControlFlowRule.Exception`, but the readme still says “so much more!!!!!”
   without a checklist of remaining gaps. A maintained gap list was not found
   solely under `Checker/`.

5. **Thread safety of `TypeComparer` template-string budget** — budget flags
   are `[ThreadStatic]`. Is the checker guaranteed single-threaded per
   compilation, or can parallel file checks share a comparer incorrectly?

6. **Whether `NarrowedTypes` keys are exhaustive** — recording happens via
   `RecordNarrowedType` on specific paths; not every `NarrowVariable` call
   necessarily publishes to the dictionary. Exact completeness guarantees for
   LSP were not fully enumerated from a single authoritative list in code.
