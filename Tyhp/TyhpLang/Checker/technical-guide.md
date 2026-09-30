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
   Check runs whenever bind produced a `GlobalScope`, even if bind reported
   tyhpdef or other errors — otherwise Mechanism D flags are dropped.
4. **Emit** — reads checker outputs via `CompilationResult` / `EmitContext`.

Orchestration lives in `Tyhp/Domain/Services/CompilationService.cs`:

- Bind runs only when parse produced no errors.
- Check runs when bind produced a non-null `GlobalScope` (tyhpdef bind errors
  do not skip check).
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
| `NativeTypeTests` | `T →` free function or concrete static method marked `#[\Tyhp\NativeTypeTest]` for `$x is T` emit |
| `RequiresDisposableTryFinally` | Disposable scopes needing try/finally fallback |
| `AsyncForeachKinds` | Await-foreach classification for desugaring |

`ExpressionTypes` is copied onto `CompilationResult` by `CompilationService.CheckParsedFiles`
and forwarded into `EmitContext` so expression-tree emission can spell per-node `$type`
strings. `NativeTypeTests` is copied the same way so `$x is T` emit can call a marked
guard (`\is_string($x)` or `\Class::method($x)`) instead of `\Tyhp\Type::is`.

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

1. If `FileSymbol.IsPhpVersionGateInactive` and the file-level constraint was **valid**
   (simply unsatisfied for `output.phpVersion`), skip the file. Binder already omitted
   its symbols; walking the AST would type-check unbound bodies (for example `$this` in
   an extension that was never bound). Invalid constraints still walk so **4300** can
   fire.
2. `CreateInitialState(srcFile)` — `ScopeType.File`, `CurrentFileName` set.
3. For each non-null child of the file AST, `CheckNode(child, state)`.
4. After all files: `CheckBoundTyhpdefThinMappings`. Included tyhpdefs are bound
   into `GlobalScope` but often absent from the walked trees. Class-body thin
   mappings get splice-member checks. A standalone tyhpdef extension gets
   `ExtensionRule.Check` — the same block-target declaration checks a
   project-source standalone extension gets, plus the splice-member checks that
   rule already runs for tyhpdef members.
5. `ImportRule.FlushRemainingImports` (unused-import reports).
6. `PropagateGenericVariantAcrossHierarchies()` — union-find over method
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
18. `ExternTypeUseRule` (TYHP4307 — `.tyhp` use of tyhpdef `extern` names)
19. `RestrictedFeatureRule`
20. `OverloadRule`
21. `AttributeRule`
22. `ImportRule`
23. `CodeQualityRule`
24. `WithKeywordRule`
25. `PhpVersionRule`
26. `InlineSpliceRule`

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
├── PropertyInitializationState.cs # $this->prop / self::$prop init + narrowing
├── ReferenceGroup.cs              # &$ alias groups
├── ICheckedType.cs / CheckedType.cs  # Checked-type ADT + CheckedTypes factory
├── CheckedTypeDisplay.cs          # Canonical DisplayName for unions / nullables
├── INarrowingResolution.cs        # Narrowing’s type-resolution surface
├── TypeComparer*.cs               # Assignability / subtyping / ops / generics / …
├── TypeInferrer*.cs               # Expression + type-expression inference
├── GeneratorBodyInference.cs      # Story 21.6 Phase 6b — infer Generator<K,V,S,R> from yield/return
├── GenericConstraintResolver.cs   # Caches ResolvedConstraint on type params
├── GenericTypeArgumentValidator.cs
├── UtilityTypeResolver.cs         # \Tyhp utility types (Readonly, Pick, …)
├── MagicUtilityTypeResolver.cs    # __SuperType / __CurrentScope / __IndexKeys / …
├── TypeNameAlgebraResolver.cs
├── SymbolNameTypeHelper.cs        # __FunctionName / __ClassName / …
├── SymbolNameTypeAssignability.cs
├── SymbolNameExistenceVerifier.cs
├── NameofTypeInferrer.cs
├── StructTypeHelper.cs
├── StructBagLiteralChecker.cs     # Named/positional bag literals vs struct shapes
├── CallableSignatureReflection.cs # Story 16.5 — params + return from a callable type
├── FunctionOverloadSelector.cs    # Story 16.5 — same-arity tyhpdef overload pick (functions + methods)
├── TyhpdefConstIntLiteral.cs      # Story 21.8 — tyhpdef `const int NAME ?? N` as literal N
├── ClosureProducerInference.cs    # Story 21.6 Phase 4 — Closure TCallableShape/TThis/TScope wrap
├── ClosureBindSupport.cs          # Story 21.6 Phase 5 — bind/bindTo/call leftover-scope + non-rebindable
├── GenericInheritanceBindings.cs  # Extends-chain bindings + Traversable foreach + ArrayAccess / ArrayAccessShape
├── ArrayAccessShapeSupport.cs     # Story 21.6 Phase 6d — per-key indexing, wide keys, no-append, exhaustiveness
├── ArrayAccessDestructureSupport.cs # Story 21.7 — list() / [] destructure on ArrayAccess
├── ArrayAppendInference.cs        # Open `array` locals: `$arr[] = $v` / `$arr[$k] = $v` infers `array<K, …>`
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
    ├── ExternTypeUse.cs / ExternTypeUseRule.cs  # TYHP4307 `.tyhp` use of `extern` names
    ├── TypeNarrowingRule.cs       # (namespace Tyhp.TyhpLang.Checker, not Rules)
    ├── ClosureParameterInference.cs
    ├── PropertyPathSupport.cs     # (namespace Tyhp.TyhpLang.Checker) Story 16 Phase 1
    ├── ExpressionTreeSupport.cs   # (namespace Tyhp.TyhpLang.Checker) Story 16 Phase 2
    ├── TypeGuardValidation.cs
    ├── PropertyInitializationAnalysis.cs
    ├── InlineSpliceRule.cs        # Call-site splice safety: 4174–4177, 4179, 4181
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
| `TypeComparer.Generics.cs` | Substitution by name or by symbol identity; expands deferred `__CallableReturnType` / `__CallableParametersStruct` / `__CallableParametersTuple` / `__CallableParametersRest` after `TCallable` is bound (Rest keeps its wrapper so call-site unpack can see it) |
| `TypeComparer.Operations.cs` | Equality, union/intersect, narrow positive/negative |
| `TypeComparer.Aliases.cs` | Alias expansion |
| `TypeComparer.TemplateStrings.cs` | Template-string inclusion |

### 3.2 `TypeInferrer` partials

| File | Responsibility |
|------|----------------|
| `TypeInferrer.cs` | Cache, `ResolveTypeExpression`, resolution scope |
| `TypeInferrer.Expressions.cs` | Scalars, vars, ops, ternary, new, closures, match, … |
| `TypeInferrer.Closure.cs` | Closure producer wrap (`TCallableShape` / `TThis` / `TScope`) for literals, FCC, `fromCallable` |
| `TypeInferrer.Dereferenceables.cs` | Calls, members, array access (`array`/`string`/`ArrayAccess<TKey,TValue>` via `GenericInheritanceBindings.TryGetArrayAccessTypes`; `ArrayAccessShape<T>` per-key via `__IndexValueType`; `InferIndexValue` shared with `list()` destructure), generics at call sites |
| `TypeInferrer.TypeExpressions.cs` | Annotation AST → `ICheckedType` |
| `TypeInferrer.Operators.cs` | Native numeric / binary operator result types |
| `TypeInferrer.OperatorOverloads.cs` | Operator-overload return-type lookup (Story 11 §8) |
| `TypeInferrer.TemplateStrings.cs` | Template-string type construction |

### 3.3 Rules (by concern)

| Rule | Primary concern |
|------|-----------------|
| `DeclarationRule` | Namespaces, classes, functions, methods, properties; owns scope setup; property-hook shape (duplicate / invalid name / `&set` / get parameters), modifier + visibility (TYHP4154 / TYHP4004), final-override (TYHP4166), `&get` version gate (TYHP4167, skipped on tyhpdef declarations), TYHP8015 if a tyhpdef hook body still reaches the checker, TYHP4326 when a user `.tyhp` type lists `\Traversable` in `extends`/`implements` (only `\Iterator` / `\IteratorAggregate` may extend it), TYHP4337 when a user `.tyhp` type lists `\UnitEnum` / `\BackedEnum` (only the engine `\BackedEnum` may extend `\UnitEnum`), and TYHP4338 when a user enum redeclares `cases` / `from` / `tryFrom`. Tyhpdef harvest shells are skipped structurally, not by `#[\Tyhp\Php]` |
| `TypeAnnotationRule` | Required annotations / typed locals. TYHP4016 wraps the subject in backticks: `$name` for a variable, parameter, or property (`FormatTypeRequiredName` keeps a single `$` when the AST name already includes it), and `return type` for a missing function or method return type |
| `ControlFlowRule` | if/loops/try/return/throw/ternary/match conditions; duplicate catch type (TYHP4124 warning) labels the first catch type |
| `TypeCompatibilityRule` | Assignments, calls, `new`, member access, arrays; `new` on a type alias is TYHP4069 (TYHP4347 when the alias is, or renames/generic-wraps, an object shape); `new T()` requires `T extends __New<Shape>` (TYHP4355) and checks args against the shape constructor (TYHP4357); `new $cls()` on `__ClassName<Shape>` without `__New` is TYHP4356 (`__ClassName<__New<Shape>>` uses the same 4357 ctor check); `__New<T>` where T is not a shape alias is TYHP4351; failing `__New` constructability is TYHP4352/4353/4354; **mixed use-site** (TYHP4160); **Unresolved receiver** member/call/index (TYHP4197, suppressed when the receiver subtree already has an error); call/ctor arity (TYHP4142/4143); unresolved method on a closed member receiver (scalars / `\Closure` / structs, TYHP4101) or on a shape-only receiver; `$x->__construct()` on a shape-typed value is TYHP4359; unresolved free-function call (TYHP4182); type-alias factory type arguments (TYHP4186); `fromCallable` private-method visibility (TYHP4025); `bind`/`bindTo`/`call` leftover-scope / non-rebindable (TYHP4334 / TYHP4336); array access on non-array/string/ArrayAccess (TYHP4093); ArrayAccess struct/array `TKey` (TYHP4330); ArrayAccessShape wide key (TYHP4331) / append (TYHP4332); homogeneous `$obj[] =` uses `offsetSet`'s first parameter (WeakMap `TKey extends object` rejects null; `ArrayAccess::offsetSet(null or TKey, …)` allows append); int-alias structs skip homogeneous ArrayAccess checks so `$args[0]` is the field; bare `array` / `array<V>` index keys are `int\|string` (`CheckedTypes.PhpArrayKey`); `array<int, V>` still rejects string keys; string offset keys stay `int`; `list()` / `[]` destructure on array, string, or ArrayAccess (`ArrayAccessDestructureSupport`; TYHP4094 otherwise; spread TYHP4095) |
| `TypeDeclarationValidationRule` | Illegal type-expression shapes; duplicate union/intersection members (TYHP4053) label the first member |
| `NullSafetyRule` | Definite assignment / non-null use sites |
| `TypeNarrowingRule` | Smart casts (static helper, not `ICheckerRule`) |
| `ClosureRule` + `ClosureParameterInference` | Closures / contextual params |
| `UnsetTrackingRule` | `unset` vs definite assignment / `AllowUnset` |
| `StructRule` / `WithKeywordRule` | Anonymous `TyhpStructDeclAst` (incl. ObjectGenerics / constraints) and `with` forms (member substitution on generic structs). Named `type Name = struct { };` aliases are `ObjectDeclarationSymbol` with `IsStruct`; `DeclarationRule` checks the alias body under that struct's generic scope. |
| `AsyncRule` / `DisposableRule` | await / disposables / emit flags |
| `CompileTimeRule` | `nameof` / `typeof` / `default` / `variable_exists`. `typeof` takes a `typeExpr` (`TyhpTypeofAst.TypeExpression`); the checker resolves that annotation (aliases included — not mixed) and flags Mechanism D / GenericObject when it names those generic parameters (same as `default`). `TypeInferrer` types `typeof(...)` as `\Tyhp\Type`. `nameof(T)` still accepts in-scope class or method generics via `IsInScopeGenericParameter`. |
| `ImportRule` | Unused / duplicate imports (TYHP4130 / TYHP4131; 4131 warning labels the first `use`); redundant local `use` of a `global use` symbol (TYHP4169). Heritage `extends`/`implements` names are marked from `DeclarationRule.CheckObjectType` (`MarkHeritageImportNames`) because that shell suppresses child traversal |
| `ExternTypeUseRule` | `.tyhp` use of a tyhpdef `extern` name (TYHP4307): type, function, or const. Parameter/return/catch/call-argument sites that skip `CheckNode` call `ExternTypeUse` from `DeclarationRule` / `ControlFlowRule` / `TypeCompatibilityRule` / `ClosureRule`. Inheritance of an extern type is binder TYHP3026/3027, not 4307. |
| `ExtensionRule` | Extension decls / imports. Empty `extension { }` is TYHP4172. A header `extends Type` and nested `extends` groups are exclusive (TYHP4362 / TYHP4363). Members with neither are TYHP4147. Target shape, unused binders, method-generic shadowing, intra-extension overlap, `static::` / `parent::`, and `&$this` are TYHP4364–TYHP4370. Child walk is suppressed; the rule checks members itself and calls `InlineSpliceRule.CheckMemberDeclaration`. Included standalone tyhpdef extensions (absent from `ParsedFiles`) are checked by this same rule from `CheckBoundTyhpdefThinMappings` |
| `InlineSpliceRule` | Splice-engine checker: written-param `&` contract (TYHP4174; block-target `&$this` is TYHP4369 / TYHP4370 instead), splice cycles (TYHP4175), `#[\Tyhp\Optimize\Inline]` on an extension member (TYHP4176), reserved `$__tyhpInlineTemp*` (TYHP4177), spliced-body accessibility (TYHP4179), erased-member unspliceable call sites (TYHP4181). Class methods are invoked from `DeclarationRule.CheckMethod` (they bypass `CheckNode`). TYHP4180 lives in `ReferenceTrackingRule`. |
| `PhpVersionRule` | `declare(php=…)` / `#[\Tyhp\Php]` gates (TYHP4300–4306); 4302 only for nested unsatisfiable constraints; TYHP8016 when `#[\Tyhp\Php]` is on a tyhpdef property hook |
| `RestrictedFeatureRule` | `eval` / include / variable-variables / `compact` / `extract`; undeclared instance-property **writes** (TYHP4134) unless the receiver type is exactly the global engine class `\stdClass` (not a subclass, not a leftover `#[\AllowDynamicProperties]` stamp, not a generic parameter constrained to `\stdClass`). Composite receivers — nullable wrap, union, intersection, and generic-parameter constraints — are walked inside this rule (not via `CheckerHelpers.TryGetObjectDeclaration`). A union reports if any object arm would; an intersection allows the write when any arm declares the property. Unknown / mixed / unconstrained type-parameter receivers stay unchecked. Undeclared **reads** on a typed object stay gradual; an Unresolved receiver read/write/call/index is TYHP4197 |
| Others | Overloads, attributes, deprecation, operator overloads, references, code quality |

---

## 4. `CheckerState` and rule context — how state flows

### 4.1 `CheckerState`

`CheckerState` is the **mutable, scope-local** environment for the walk. Important
fields:

- **Scope identity**: `ScopeType`, `Parent`, `CurrentFileName`,
  `CurrentNamespaceName`, optional `NameResolutionScope` override,
  `PhpVersionConstraintStack` (enclosing `declare(php=…)` AND-list for 4302).
- **Enclosing declarations**: `EnclosingObject`, `EnclosingFunction`,
  `EnclosingCallable`, `ObjectGenerics`, `FunctionGenerics`,
  `EnclosingObjectType`, `Modifiers`.
- **Expected types**: `ExpectedReturnType`, `ExpectedClosureType`,
  `ExpectedExpressionType` (this expression's contextual type for bare
  `new Generic()` inference — return operand, typed-var initializer,
  assignment RHS, call argument; not the function return applied to every
  nested `new`),
  `IsTypeGuardFunction`.
- **Flow flags**: async/generator/loop/switch/finally/closure,
  `HasReturnedOnAllPaths`, `IsExistenceProbeContext`,
  `IsParameterTypePosition` / `IsPropertyTypePosition`.
- **Tracked maps**:
  - `Variables` → `VariableState`
  - `PropertyInit` → `PropertyInitializationState` (`$this->prop`, enclosing-class `self::$prop`)
  - `IndexAccessNarrowing` → `$arr[0]` / `$arr['k']` / `$arr[$k]` keys
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
  paths). Assignment becomes definite only if both paths assigned. Locals
  union `EffectiveType` into `DeclaredType` and clear `NarrowedType`. Property
  `NarrowedType` is the union of both sides when both recorded a refinement
  (so `if ($p === null) { $p = new T(); }` is non-null on the join); otherwise
  it is cleared. Index/member narrowing is kept only when both sides agree
  (`TypeComparer.AreTypesEqual`).
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
  / `CheckStatementSequence` / `CheckAttributes`
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

- `TypeCompatibilityRule` walks call arguments itself: `CheckNode` on each
  non-closure argument expression (so nested `yield` / `await` / operators
  still run under `SuppressChildTraversal` on `PhpDereferenceableAst`), plus
  types + arity (TYHP4142 missing required / TYHP4143 too many; unpack
  `...$args` skips static arity). Nested calls / `new` used as *receivers*
  are re-entered via `CheckNestedCalleeIfNeeded`.
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
- Class heritage: `CheckObjectType` calls `MarkImportNames` on the written
  `extends` name and `implements` list (`MarkHeritageImportNames`) so a
  file-level `use` whose only references are heritage names is not TYHP4130.
  Tyhpdef shells already `CheckNode` non-body children in
  `CheckTyhpdefImportObject`, so they do not need the same helper.
- **Override signature checking** (`ValidateOverrideSignature`): parent
  parameter/return annotations are resolved with
  `ResolveMemberDeclaredType` against the child receiver (declaring-class
  `ObjectGenerics` + extends-chain substitution), not with the child's state
  alone. Otherwise a base signature like `Expression<TCallableShape>` looked up
  while checking `ExpressionBuilder<T> extends Expression<callable(T): bool>` reports
  false TYHP3003 for `TCallableShape`, and any diagnostic on the parent AST would show
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

### 5.5 `ExternTypeUseRule` (TYHP4307)

A tyhpdef `extern class` / `interface` / `enum` / kind-unspecified `extern \Name;` / `extern function` / `extern const` is a bound name (`IsExtern` on
`BaseSymbol`) but is not usable in `.tyhp`. After bind, the
checker reports **TYHP4307** (`CheckerExternTypeUsed`; short message
``Name `{0}` is extern``) at `.tyhp` use sites. Secondary label `declared here`
points at the `extern` declaration; `Help` is `@provided-by` as-is when present.

**Use (error in `.tyhp`):**

- Type annotations (parameter, return, property, catch, catch union arm,
  generic argument), including a single `|` / `&` arm that is extern
- `new ExternType`, `instanceof ExternType` (and `is`)
- `use` / `use` group importing that FQCN, including `use function` / `use const`
- A call of an extern function (`\bcadd(...)`) — `CheckCallReturn` resolves the
  callee because call nodes suppress the child walk of the name
- A const fetch (`\GMP_ROUND_PLUSINF`) via `PhpNameAst` + `ResolveFreeConstant`
- A call whose inferred return type is extern
- An argument passed to a callee parameter whose type is (or contains) extern —
  reported at the argument, instead of a misleading TYHP4010 against the
  placeholder

**Not a use:**

- Naming a **real** class whose signature mentions extern (until a call /
  argument site)
- `.tyhpdef` parameter / return / property types, default values, and mapping
  bodies that name an extern type, function, or const
- `extends` / `implements` of extern — binder TYHP3026 / TYHP3027 only

`ExternTypeUseRule` handles nodes that `CheckNode` actually visits
(`PhpTypeExpressionAst`, `PhpNewAst`, `instanceof` binaries, `PhpImportDeclAst`,
call dereferenceables, and `PhpNameAst` const fetches). Declaration, catch, closure, and call-argument sites
that suppress child walks call `ExternTypeUse` helpers from those rules.

Unions: each arm is independent; one extern arm is still a use. After a later
real declaration of the same FQCN (real-wins merge), that arm is a real type.

---

## 6. Type comparison / assignability / subtyping

### 6.1 Checked types (`ICheckedType`)

Kinds (`CheckedTypeKind`): Simple, Union, Intersection, Nullable, Generic,
Literal, Struct, ObjectShape, Callable, Never, Void, Mixed, Unresolved, Inferred,
TemplateString.

Important encodings:

- **Nominal types** — `SimpleCheckedType` wrapping a binder symbol
  (`BuiltInTypeSymbol`, `ObjectDeclarationSymbol`, `GenericTypeParameterSymbol`,
  …).
- **`UnresolvedCheckedType`** — compiler-internal recovery singleton;
  assignable to/from everything so one failure does not cascade. Display name
  `"unresolved"`. Comments emphasize it is **not** the user-facing top type;
  **`mixed`** is the strict top (assigns from anything, assigns only to
  `mixed` / `?mixed` without narrowing). Member access (`->` / `?->` / `::` on a
  value), method call (including extension lookup and `$fn()` invoke), and
  indexing (including `list()` / `[]` destructure) on an Unresolved receiver are
  TYHP4197 unless that receiver subtree already has an error. Emit does not
  rewrite `->` as struct `[]` or splice an extension for an Unresolved receiver. In this
  codebase, “unknown” gradual typing is represented by unresolved/mixed-like
  behavior at unresolved member *inference* sites (see call inference comments);
  the checker still reports the use site.
- **Literals** — `LiteralCheckedType` (including `null`, `true`, `false`).
- **Structs** — structural property maps (`StructCheckedType`).
- **Object shapes** — `ObjectShapeCheckedType` from a `type Alias = object { … }` (or an intersection that includes a shape). The alias is a type, not a class: `new` / `::` / heritage / `::class` are TYHP4347. `new` on a non-shape type alias (including an alias of a class) is TYHP4069; a rename or generic wrapper of a shape alias used as `new` is TYHP4347. A member map (`ObjectShapeMemberMap`) holds methods, properties, and constants resolved from the bound shape AST: PHP class-const spelling (`const NAME = expr` / `const T NAME = expr`) is `PhpConstDeclListAst`; tyhpdef `const T NAME` / `??` is `TyhpdefImportConstDeclListAst`. Width subtyping: a class, interface, enum object, or other shape assigns to `S` when every public instance member of `S` except `__construct` exists with a compatible signature (callable assignability for methods; readonly/hook variance for properties; class constants via `ConstantSatisfiesShape`). Extra source members are allowed. `protected` / `private` do not satisfy the shape. Magic (`__call`, `__get`, …) matches only if the shape lists that same magic method. `object` / `mixed` do not assign to a shape (TYHP4358). `S` is a subtype of `object`. Two shapes with the same members are structurally equal; generic instantiations substitute into member signatures. Recursive aliases (`parent(): ?Node`) are coinductive: a logical (source declaration, shape alias) pair currently being width-checked is assumed to hold when it reappears in a member type. **Instance member access** (`$c->now()`, including after `$x is ClockShape` narrowing and on `__New<Shape>` / `new $cls(...)` values) reads that map (`TypeComparer.TryFindShapeInstanceMethod` / `TryFindShapeInstanceProperty`); `$c->__construct()` is TYHP4359. `__construct` on a shape is constructability for `__New` / `new T()` only (`TypeComparer.NewConstraint.cs`); it is not an instance method. Missing `__construct` on `__New<S>` means PHP’s default public 0-arg constructor, not “any constructor.” A written 0-arg `__construct(): void` matches any public constructor invokable with zero arguments. `T extends __New<S>` implies `T extends S` plus constructability. **`__New<S>`** (`UtilityBehavior.New`, `TypeComparer.NewConstraint.cs`) is a constructability wrapper: `T` must be an object-shape alias (TYHP4351). Values are instances of `S` whose class is concrete and public-`new`-able as `S`’s constructor. Missing `__construct` on `S` means PHP’s default 0-arg public constructor (not “any constructor”). Written `__construct(): void` matches any public ctor invokable with zero args. Written args require every shape-allowed call to be accepted by the class (extra optional class parameters allowed). Abstract / interface / trait / enum is TYHP4352; non-public ctor is TYHP4353; ctor mismatch is TYHP4354. `new T(...)` is allowed only when `T` is bounded by `__New<…>` (otherwise TYHP4355); arguments (positional or named — named arguments are matched to the selected facet's parameter names, same as a nominal `new Foo(...)`) are checked against the **shape** constructor (TYHP4357). `new $cls(...)` on `__ClassName<Shape>` (no `__New`) is TYHP4356; `__ClassName<__New<Shape>>` allows `new $cls(...)` with the same 4357 check. Cycle stubs without member maps rebuild constructability from the expanded shape AST.
- **Callables** — `CallableCheckedType(parameterTypes, returnType)` from a `callable(…): R` shape (or a function/closure/`__invoke` symbol). `IsAnyArity` for `callable(...): TReturn` (empty `ParameterTypes` plus that flag;
  a zero-parameter facet is `IsAnyArity` false). Optional trailing defaults
  (`=` on a shape parameter; initializer discarded) synthesize an `IntersectionCheckedType` of arity siblings via `CallableArityFacetBuilder`
  (shared prefix math in `ArityFacetExpansion`). A trailing homogeneous `T ...$args` adds
  exactly one variadic-inclusive facet — enough to match a one-extra-argument target without
  unbounded siblings. A trailing **pack** variadic (`__CallableParametersRest<T> ...$args`)
  does **not** get a 0-arg sibling or a `Rest...` wrapper facet: it splices T's parameters
  (deferred pack while T is open) so the closure implements `callable(Rest<T> ...): R`. Typed
  `callable` / `\Closure` may appear in user-written intersections. Invoke /
  closure contextual typing select a facet by argument count; named arguments on `$cb(name: …)`
  select the facet that stores that name (`TrySelectCallableFacetForCall`) and type-check
  against that slot. Unnamed parameters have no named-argument key — the checker does not
  invent names. A hand-written intersection may
  give each arity its own return type. Any-arity facets are generic bounds only (not value
  types, not invokable). Display names use `callable(...): TReturn` so they are not mistaken
  for a 0-arg `callable(): TReturn`. A shape assigns to bare `callable`; bare `callable` /
  `mixed` do not assign to a shape (TYHP4360). Parameter types are contravariant and returns
  covariant (`AreCallableTypesCompatible`). Extra optional parameters on an implementation
  are the shorter arity siblings, so a longer optional-arity source still assigns to a
  shorter target. An object whose public `__invoke` matches the shape assigns to it.

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
- `NarrowType` / `NarrowTypeNegative` — a true type guard asserts its target; `NarrowType` keeps that target when the meet would be `never` (e.g. `callable` after `is_array`). `IntersectTypes` is unchanged, so a declared `callable&array` annotation is still `never`.
- `ResolveGenericType` / `ResolveGenericTypeBySymbol`
- `ExpandTypeAliases` — file-level `TypeAliasSymbol` and class-level `ObjectTypeAliasSymbol`, with generic substitution. Class-level bodies resolve `self`/`static`/`parent` against the owning class (the resolve callback receives the alias). Cycles return the alias symbol (no infinite recursion); the binder reports `BinderCircularTypeAlias` (TYHP3029) on cyclic **source** `.tyhp` declarations unless every participant's RHS resolves to an object shape (directly, via union/intersection, or via a rename/generic instantiation of another shape alias). A bare object-shape alias expands to `ObjectShapeCheckedType` with a member map; generic arguments are substituted into those member signatures. A cycle stub of a shape alias is an `ObjectShapeCheckedType` with the same alias / type arguments and no member map (identity only). Intersection members are expanded individually. When every expanded member is a legal intersection inhabitant (class-like, type parameter, `object`/`struct`, object shape, callable facet), the annotation is rebuilt as `IntersectionCheckedType` — unrelated interfaces stay `A&B` instead of collapsing to `never`. Redundant supertypes still drop (`Child&Parent` → `Child`). Members that cannot co-inhabit (`int&string`, `callable&array`) still fold through `IntersectTypes` and become `never`.
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
    `SourceSatisfiesStruct`, object-shape members via
    `SourceSatisfiesObjectShape`).
13. Literals (bool → bool/true/false; template strings; …).
14. Same-declaration `GenericCheckedType` pairs: type arguments decide
    assignability (user generics **invariant** via mutual assignability /
    equality, plus a one-way carve-out `G<T>` → `G<mixed>` when `T` is not
    `void`/`never`; `array`/`iterable` **covariant**). Matching bases with
    incompatible args return false — do not fall through to declaration-only
    `ImplementsOrExtends` (that would accept `Box<string>` as `Box<int>`).
    **`__New`:** same-base `__New<A>` vs `__New<B>` uses constructability
    (`SourceSatisfiesNewConstraint`) instead of generic invariance. A class
    assigns to `__New<S>` when it matches `S` as an instance and its public
    constructor accepts every call the shape constructor allows (TYHP4352
    abstract/interface/trait/enum, TYHP4353 non-public ctor, TYHP4354 arity
    or parameter mismatch). `__New<S>` peels to `S` when the target is the
    instance shape. `__New` is a subtype of `object`.
    **`\Closure<C, This, Scope>`:** `C` uses callable assignability (not
    invariance); `TThis` / `TScope` stay invariant unless the target slot is
    the overlay default (`object|null` / `?object` / `__ClosureThis`, or a
    scope union that includes `null`, including a collapsed bare `null`
    default), so `\Closure<callable(int): string>` accepts inferred instance
    closures. Packs auto-splice inside a callable-shape parameter list; `Rest<T> ...$args` is
    assignable to that spliced facet.
    Different bases may still use object nominal subtyping; callables,
    symbol-name types, etc. follow afterward. Explicit `in`/`out` variance
    is not implemented; the mixed carve-out is not general covariance.
    **Callable facets:** `callable(...): R` accepts a known-arity source when
    every applicable return <: R (parameter lists ignored). The any-arity
    facet is not assignable to a known-arity target. A required-parameter
    shape is not assignable to a shorter arity; all-defaulted functions
    still assign via their shorter arity siblings. Parameter types are
    contravariant and returns covariant (`AreCallableTypesCompatible`).
    Equality ignores parameter names and discarded `=` initializers. A shape
    assigns to bare `callable`; bare `callable` / `mixed` do not assign to a
    shape (TYHP4360). An object with public `__invoke` assigns to untyped
    `callable` by presence of that method; a typed `callable(…): R` target
    (including any-arity) builds `__invoke`'s signature into a callable facet
    and runs the same parameter/return check. Matching arity and return still
    assign; a mismatched `__invoke(): int` does not satisfy
    `callable(string): bool` or `callable(...): bool`.

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
the check. Unresolved stays permissive for **assignability** so error recovery
does not cascade. Member access (including `?->` and `::` on a value receiver),
calls (including `$fn()` invoke), and indexing (including `list()` / `[]`
destructure) on an Unresolved receiver are TYHP4197
(`CheckerHelpers.ReportUnresolvedReceiver`) unless the receiver subtree already
has an error. Unresolved is not `mixed` (TYHP4160).

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
Same-declaration `GenericCheckedType` pairs compare type arguments
(`AreGenericArgumentsCompatible`, invariant unless declared otherwise)
*before* that nominal walk, so `Closure<callable(): int>` is not a subtype of
`Closure<callable(): string>` and `UnionTypes` does not subsume both away.

PHP auto-implements `\Stringable` on any class or interface that declares
instance `__toString` (including a `convert(self): string` operator, which emit
lowers to `__toString`). `ImplementsOrExtends` treats that as a subtype of
engine `\Stringable` so assignability, `instanceof`, and echo/concat (TYHP4120)
match the runtime. Written `implements \Stringable` remains legal. Namespaced
lookalikes are not the engine interface.

PHP auto-implements `\UnitEnum` on every enumeration and `\BackedEnum` (which
extends `\UnitEnum`) on backed enumerations. `ImplementsOrExtends` treats an
enum kind as a subtype of those engine interfaces without a written
`implements`. Member lookup injects the same interfaces into the existing
implements walk so `cases()` / `from()` / `tryFrom()` resolve; emit stays
native PHP enums (no written `implements \UnitEnum` / `\BackedEnum`).
User `.tyhp` classes, interfaces, and traits must not list those interfaces
(TYHP4337); user enums must not list them or redeclare `cases` / `from` /
`tryFrom` (TYHP4338). Tyhpdef harvest shells are skipped. Namespaced
lookalikes are not the engine interfaces.

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
  `__ClassName<E>` → `__ClassName<object>` → `string`). A written
  `__EnumName<E>` assigns to another `__EnumName<…>` when the type arguments
  match. When argument checking substitutes an unbound callee type parameter
  on `__EnumName<T>`, that placeholder accepts any `__EnumName<E>`.
- Routes subclass-as-`class-string` through `__CompatibleTypeName<T>` only
  (`SymbolNameTypeHelper.IsCompatibleBrandAssignable`): `__ClassName<S>` /
  `__EnumName<S>` / `__InterfaceName<S>` / `__CompatibleTypeName<S>` assign to
  `__CompatibleTypeName<T>` when `S` is the same as or a subtype of `T`.
  Parametric `__ClassName<T>` stays invariant between distinct **nominal** type
  args. When the target brand is an object shape or `__New<Shape>`,
  `__ClassName<A>` assigns to `__ClassName<B>` if `A` is assignable to `B`
  (structural: a class matching a narrower shape also matches a wider one;
  `__ClassName<__New<S>>` assigns to `__ClassName<S>`). String literals and
  `Foo::class` assign to `__ClassName<Shape>` / `__ClassName<__New<Shape>>`
  when the named class satisfies that instance/`__New` predicate
  (`SymbolNameExistenceVerifier`). `\class_exists<Shape>($s)` /
  `\class_exists<__New<Shape>>($s)` still narrow via the tyhpdef `$class is
  __ClassName<T>` guard; `T extends object` accepts shapes and `__New<Shape>`
  (`IsObjectConstraint`). Dynamic `string` still needs the guard.

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
`match`, `yield` / `yield from` / `PhpYieldAst`, and bound function symbols as
callables.

`InferDereferenceableBase` types a member-access receiver. Besides variables,
`new`, names, and wrapped `PhpDereferenceableExpressionAst`, a leftover
`IExpression` base (`($n + tick())->ext()`) goes through `InferExpressionType`
so the receiver is the operator result (`int`), not unresolved.

Notable behaviors encoded in comments:

- Parenthesized expressions must unwrap or conditions type as unresolved.
- `default(Class)` infers **null** (emitter produces `null`), not the class
  type — otherwise null safety is defeated.
- `typeof(...)` types as `\Tyhp\Type`. The argument is a type expression (`typeof(int|string)`, `typeof(Optional<int>)`, `typeof(?int)`, `typeof(T)`); `typeof($x)` does not parse.
- **Array literals** (`PhpArrayAst` / short-syntax `PhpArrayPairListAst`):
  `InferArrayLiteral` unions widened element/key types. List shorthand
  normalizes to `array<int|string, V>`; maps keep `array<K,V>`. Empty `[]` is
  `array<never, never>` (not one-arg `array<never>` → `array<int|string, never>`)
  so covariant key/value assignability accepts any `array<…>` target, including
  narrowed keys like `array<string, T>`. Duplicate constant keys on a parsed
  `[…]` / `array(…)` pair list are TYHP4092; the primary span is the later pair
  and `declared here` labels the first. Trailing-comma skip slots are not keys.
- **Array append/keyed write (`$arr[] = $v`, `$arr[$k] = $v`)** on an *open* array
  local (bare `array $x`, inferred `$x = []` / `array<never, never>`, or unresolved
  `array<TKey, TValue>` — including a bare `array $x` **parameter**, since refining
  the local narrowing for later reads in the same body does not change the
  parameter's declared signature) narrows the local to `array<K, V>` from the
  write (`ArrayAppendInference`). Append (`$arr[]`) always contributes PHP's
  implicit `int` key. A keyed write (`$arr[$k]`) contributes `$k`'s own type
  **only** when it resolves to a plain `int` or `string` — that is the same
  "grow the open array" operation as append, just with the write's real key
  instead of an implicit one; an unresolved/`mixed` key is not guessed at, and
  the write falls through to the ordinary compatibility check unrefined.
  Further writes union into `K` / `V`. Annotated `array<K, V>` is not open —
  writes are checked against `K` / `V` as-is. This is what makes
  `\implode('', $digits)` and `\implode('', \array_reverse($digits))`
  type-check after ordinary PHP list/map-building. `TypeCompatibilityRule`
  skips the element-vs-placeholder assignability check when it just refined
  (`int` ↛ `never`).
  An open array that is *never* written to before being passed to a generic
  function (e.g. bare `array $x = []` straight into `\array_reverse($x)`) is a
  known gap: its type is still the unresolved `array<TKey, TValue>` placeholder,
  which the generic call cannot bind concrete arguments from. Ordinary
  PHP list-building (`declare → append/keyed-write → use`) is unaffected.
- **`list()` / `[]` destructure:** assignment whose left is a `PhpArrayPairListAst`
  is not an array literal. `ArrayAccessDestructureSupport` types each bound
  target as the corresponding index read (`InferIndexValue` — array element,
  string offset, ArrayAccess `TValue`, ArrayAccessShape per-key). Skipped
  slots (`[, $b]`) are not reads. Spread is TYHP4095. Other sources are
  TYHP4094.
- **Yield expressions:** `yield` / `yield $v` / `yield $k => $v` (unary
  `T_YIELD` or `PhpYieldAst`) type as TSend of the enclosing callable's
  `Generator<TKey, TValue, TSend, TReturn>` — PHP's `Generator::send()` value,
  read from `ExpectedReturnType` only while `IsInGeneratorContext` is set
  (same closure-boundary rule as generator `return`). After the body is
  visited, `GeneratorBodyInference` fills TKey / TValue / TSend / TReturn from
  yields and returns. Bare `\Generator` (and two-arg `\Generator<K,V>`)
  substitute that inferred generic as the callable's effective return so
  callers and later `InferYield` cache lookups see it. Layer 3 fills omitted
  `Generator` type arguments with `mixed`, so written arity (bare /
  two-arg / four-arg) is taken from the return-type AST, not the resolved
  default-filled generic. Missing TSend slot during the body visit is
  `mixed`, then typed `yield` targets (`int $x =
  yield`, `foo(yield)` with `foo(int $x)`) constrain TSend to their
  intersection (empty intersection → TYHP4328). Untyped `$x = yield` stays
  mixed. Literal `true`/`false` widen to `bool` (Generator type arguments are
  invariant). Four-arg form is fully explicit; the body must be assignable to those
  pins (TYHP4087). `yield from $expr` (unary `T_YIELD_FROM`) types as the
  operand's TReturn when it is a `Generator`, else `null` for array /
  `iterable` / other `Traversable` (PHP: non-Generator `yield from` evaluates
  to `null`). Yield-from also merges inner TKey / TValue (and inner TSend as a
  constraint). Key and value operands are still inferred so nested expressions
  keep real types.

### 7.3 Dereferenceables and calls

`TypeInferrer.Dereferenceables.cs` is the largest inference surface:

- Chains: base + suffix (call, instance/static member, class const, array
  access).
- **`::class`**: brands as `__ClassName<R>` (or interface/enum/trait sibling)
  from the receiver type `R`, keeping generics (`self<T>::class` →
  `__ClassName<Promise<T>>`). Erases to bare `__ClassName` then `string`.
  Name bases with type arguments (`self<T>::`) resolve via the same path as
  `new self<T>`. Parameterized `static<…>` is rejected (TYHP4168).
- **`is Foo<…>` / `self<T>`**: emitter reifies parameterized **nominals** to
  `\Tyhp\Type::is($x, Type::generic(…))` so type arguments are checked at
  runtime (native `instanceof` would drop them). Parameterized **aliases**
  (`$fn is Predicate<int>`) use the alias factory / callable-shape descriptor
  instead of `Type::generic`. Bare `$x is static` / `$x is Foo` stay as PHP
  `instanceof`. Narrowing applies the same type arguments via
  `ResolveInstanceofTargetType`, then expands type aliases so a callable-shape
  alias becomes the shape (invoke-checked). Parameterized `$x is static<…>`
  is forbidden. A type-position name on the `is` / `instanceof` RHS that
  resolves to no symbol is TYHP3003 (`BinderSymbolNotFound`), not a silent
  mixed/unknown expression; `self` / `parent` / `static` outside a class are
  TYHP4064, and `parent` with no superclass is TYHP4065. Class-level aliases
  on that RHS (`self\Alias`, `static\Alias`, `parent\Alias`, `Class\Alias`)
  resolve through `NameResolver.TryResolveObjectTypeAlias`, the same helper
  type annotations use; a hit is stamped onto the name's `BoundSymbol` so emit
  can reify `$x is self\Alias` to `\Tyhp\Type::is($x, self::Alias())` for a
  type alias, or `\Tyhp\Type::is($x, Type::struct(…))` for a nested named
  struct (`class C { type Point = struct { … }; }` / `$x is C\Point`). A miss
  continues ordinary type-name resolution, so a nested class under the same
  prefix as a class FQN still binds. Dynamic
  `$x instanceof $classNameVar` still uses expression inference.
- **Free functions**: binder does not bind call-site names →
  `CheckerHelpers.ResolveFreeFunction`. Qualified names (`\Ns\fn`, `Ns\fn`)
  split on `\` the same way type names do. A class-kind `use App\Types\UserId`
  whose target is a source `TypeAliasSymbol` is alias-name-as-call
  (`ResolveTypeAliasFactory`): the `type` declaration is the only alias; Tyhp
  does not invent a factory from a same-named function or method. Tyhpdef aliases stay
  type-only. `UserId()` / `Optional(typeof(int))` type as `\Tyhp\Type`. Type
  arguments on the call (`Optional<int>()`) are TYHP4186 — write
  `typeof(Optional<int>)` or `Optional(typeof(int))`. Class-level aliases are
  static methods (`UserService::NameType()`), not `use function`. TYHP4186
  applies only to alias factories, not to generic methods. Ordinary static
  methods that happen to share a builtin type name (`Type::bool()`) are not
  factories: type-position `bool` stays the builtin.   Unqualified free-function lookup from a file or namespace-block statement list uses
  `NameResolver.FindCallSiteScope` with the checker's `CurrentNamespaceName` so the
  enclosing `NamespaceBlockScope` is searched (PHP current-namespace lookup). Method
  bodies already had that scope via `EnclosingObject` / `EnclosingCallable`; top-level
  `twice()` in `namespace App` must resolve `\App\twice` the same way. Unqualified
  lookup skips class members so those methods do not hide a namespace function
  or file-level alias factory. When the call
  does not resolve to a declared function, tyhpdef stub, source alias factory,
  import, or compile-time builtin, `TypeCompatibilityRule.CheckCall` reports
  TYHP4182 (`CheckerUndefinedFunction`) instead of silently typing as mixed.
  Generic name lookup prefers class-likes; the call path then looks for a sibling
  function so `pathinfo()` still resolves next to `struct PathInfo`.
- **First-class callable** syntax `foo(...)` → callable signature, not invoke.
  Parameter and return annotations resolve under the callee’s
  `FunctionGenerics` (`InferCallableSymbol` / `InferCallableFromFunction`), so
  shapes like `array<TKey, TValue>` keep type parameters instead of collapsing
  to `unresolved` (and falsely failing `KeyIntOrString`).
- **Invoking a callable value** (`$fn($x)`, property-held `\Closure<…>`,
  `$obj(...)` on a user `__invoke` class, `|>` RHS): `TryGetCallableReturnType`
  / `InferPipeResult` select an arity facet, then `CallableGenericInference`
  binds any remaining `GenericTypeParameterSymbol`s from the actual argument
  types (same structural matching as direct-call `TryInferGenericBindings`)
  and substitutes into the return type. Named arguments (`$cb(name: …)`) are
  matched against the selected facet's `ParameterNames` when present
  (`TrySelectCallableFacetForCall` / `ValidateCallableFacetArguments`); unnamed
  shape parameters are not targeted by invented names. A union **pattern** whose actual is not
  an equal-arity union tries each arm against the actual (structural arms
  before a naked type parameter), so `T|__ClassName<T>` and
  `Traversable<K,V>|array<K,V>` infer from a class, `Foo::class`, or a concrete
  iterator. Typed `\Closure<C, …>` facets come from
  `C`. A non-Closure object with `__invoke` has no stored `TCallableShape`;
  `TryGetCallableReturnType` and `TypeCompatibilityRule.CheckCall` pull the
  facet from that method so `$h(1)` types and checks like `$h->__invoke(1)`.
  Bare `\Closure` stays gradual (overlay `__invoke` is not used as a fallback).
  `TypeCompatibilityRule` applies the same bindings before argument
  assignability so open generics do not false-positive
  `CheckerIncompatibleArgumentType`.
- **Call / method return types**: `ResolveFunctionReturnType` /
  `ResolveMethodReturnType` likewise fork `FunctionGenerics` (and declaring
  object generics for methods) before resolving the declared return annotation,
  then apply call-site type-argument substitution and argument-driven inference.
- **Method calls**: resolve on receiver before treating `CallableCheckedType`
  as already-invoked return (avoids skipping generic substitution —
  FOUND_BUGS item 39). After `SymbolTree.ResolveMember`, instance lookup also
  consults `NameResolver.ResolveExtensionMethod` with the call-site scope so
  in-scope scalar / class extensions (including file-local `hide` / `as`) type
  correctly on builtin receivers (`string`, `int`, `float`, `bool`, `array`)
  whose declaring scope is global. `TryResolveInstanceOrExtensionMethod`
  canonicalizes the receiver through `TypeComparer.TryGetBuiltInName` /
  `ResolveBuiltIn` (and template strings to `string`) so literal types
  (`'hello world'`), `CheckedTypes.Int` operator results, and a fresh
  `BuiltInTypeSymbol("string")` match the global `\string` / `\int` symbols
  that `extends string` / `extends int` bind against. A block-target method
  (`extension E extends Box` / nested `extends Box`) whose synthesized `$this`
  type is a namespaced relative name does not re-resolve from that global
  index scope; `ResolveExtensionMethod` then matches the block's
  `ExtensionBlockTargetSymbol`, so `$x->m()` infers as the declared return
  type when the result is chained or stored in a local. A `self` return on
  that method is the block target, not the extension class — including a
  scalar/builtin target (`extends string`). `EnclosingObject` is
  `ObjectDeclarationSymbol`-typed and cannot hold a builtin, so
  `ResolveMethodReturnType` seeds the extension symbol there only to satisfy
  the "used outside class" guard and seeds the real (possibly builtin) target
  on `EnclosingObjectType`, which `self` resolution prefers.
  A generic target (`extends MyClass<T>`, or a nested `extends<T> MyClass<T>`)
  declares those parameters on the extension or group. They are separate
  symbols from `MyClass`'s own parameters, even when the spelling matches.
  Call-site inference matches the receiver against that target type and
  substitutes the extension or group parameters into the method's return
  type, whether the call is chained or stored. On a `MyClass<string>`
  receiver, a return of `T` infers as `string`, and a bare `self` return
  infers as the substituted target `MyClass<string>`. A receiver that does
  not supply a matching type argument — a raw `MyClass`, or a generic
  application whose other arguments do not match the target — leaves the
  parameter unsubstituted. Non-generic targets, including scalar `self`
  (`extends string`), keep the return type already resolved above.
  `CheckCall` treats
  `self::` / `parent::` / `static::` as instance-method forwarding
  (`allowInstanceForwarding`): argument arity, types, generics, and visibility
  run against the non-static callee. Named-class `Foo::instanceMethod()` stays
  static-only. Call-site arity uses
  `GetCallSiteParameters` / `ExcludeExtensionReceiver`: walk to the declaring
  `extension` object (including through `StaticMethodDeclarationScope`) or
  skip a first parameter named `$this`, so `$s->greet()` does not demand a
  positional `$this`. `InferCallableFromMethod` builds the callable facet from
  those same call-site parameters.
- Unresolvable instance methods → `Unresolved` (gradual; avoid cascade).
  `TypeCompatibilityRule.CheckCall` / `CheckInstanceMemberAccess` report
  TYHP4197 when the *receiver* itself is Unresolved and that subtree has no
  prior error. They report TYHP4101 (`CheckerSymbolNameNotFound`) when the
  receiver is a closed member
  receiver (`IsClosedMemberReceiver`: scalar pseudo-object `string` / `int` /
  `float` / `bool` / `array` / `\Closure`, plus structs / `struct` shapes)
  and the member is missing or `hide`d. Ordinary class receivers stay gradual
  (`__call`, missing trait, uncompiled dependency). Structs cannot grow PHP
  instance methods (`__call`) or dynamic properties, so `$m->nope()` /
  `$m->doesNotExist` on a named struct is a compile error the same way
  `$s->missing()` on `string` is.
- Generic call recording for Mechanism D binder routing.
- `ResolveMemberDeclaredType` / `ResolveFunctionDeclaredType` substitute
  receiver / call-site type arguments into declared annotations.
- **Array access:** `InferIndexValue` types `$x[$k]` (array element, string
  offset, ArrayAccess `TValue`, ArrayAccessShape per-key, int-alias struct
  field). Struct/array `ArrayAccess` `TKey` is TYHP4330 from
  `GenericTypeArgumentValidator` at the type-expression site.
  `CheckArrayAccess` / destructure skip a second TYHP4330 when the receiver
  is that same `\ArrayAccess<…>` instantiation (`IsDirectArrayAccessInstantiation`);
  a class that *implements* `ArrayAccess<T, …>` with `T` substituted to a
  struct still reports at the index. `TypeCompatibilityRule.CheckArrayAccess` skips homogeneous
  ArrayAccess key checks for int-alias structs (`__CallableParametersTuple`,
  `CallableArgs*`) so `$args[0]` is not compared as “literal `0` assignable to
  `string`”. `$obj[] =` on a genuine `ArrayAccess`-implementing object is
  `offsetSet(null, $v)`: when the implementor's `offsetSet` first parameter
  does not accept null (`WeakMap` `TKey extends object`), that is TYHP4008;
  Layer 2 `ArrayAccess::offsetSet(null|TKey, …)` still allows append.
  `IsArrayAccessObjectReceiver` gates this check to a non-struct object
  declaration — plain `array` append and struct append (either struct
  representation) reach the same no-index branch but have no `offsetSet`
  contract, so they stay unchecked.
- **Named constants:** `InferNamedConstant` / name-base inference resolve
  free constants (`CheckerHelpers.ResolveFreeConstant`) and prefer a tyhpdef
  `const int NAME ?? N` start value as literal `N` (`TyhpdefConstIntLiteral`)
  so overload selection can distinguish `1 $component` from `int $component`.
  Unary `-` on an integer or float literal infers the negated literal (`-1`
  is not `1`, `-1.5` is not `1.5`) so `parse_url($u, -1)` does not pick the
  `1 $component` overload, and a signed float literal type
  (`scalarTypeNegativeDNumber`) binds distinctly from its positive
  counterpart.

### 7.4 Type annotations

`ResolveTypeExpression`:

1. Prefer the **declaring file’s** namespace/`use` scope for annotations written
   elsewhere (`NameResolutionScope` / `TryGetDeclaringFileResolutionScope`).
2. `ResolveTypeExpressionCore` by AST shape.
3. `TypeComparer.ExpandTypeAliases`.
4. Generic instantiations go through `GenericTypeArgumentValidator`.

While resolving type arguments of a generic instantiation (`callable(...)`,
`array<…>`, `Box<…>`, utility types, …), `CheckerState.IsGenericTypeArgumentPosition`
is set. An undeclared named type in that position reports `BinderSymbolNotFound`
(TYHP3003) at the spelling (e.g. `TResult` inside `callable(?TResult): int`), with
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
  PHP typehints that were never written in Tyhp source. Declared closure parameter types
  are checked contravariantly against the expected facet (`?int` expected rejects
  `fn(int)`; `int` expected accepts `fn(?int)`), so `array_map` zip padding is enforced.
- Non-static closures automatically bind `$this` from the enclosing instance
  method (`BindEnclosingThis`) and re-seed `PropertyInit` across the
  anonymous-function boundary. PHP does not require `use ($this)`; without this
  bind, `$this` is unresolved inside the closure and suffixes such as
  `$this->map[$k]->method()` fall through to `mixed` (false TYHP4160).
- **Producer inference (Story 21.6 Phase 4):** `InferClosure` / FCC /
  `fromCallable` wrap the callable facet as
  `\Closure<TCallableShape, TThis, TScope>` (`ClosureProducerInference` +
  `TypeInferrer.Closure.cs`). There is no `new Closure<…>()`. Table:

  | Producer | `TCallableShape` | `TThis` | `TScope` |
  |---|---|---|---|
  | `function` / `fn` literal | signature of the literal | enclosing `$this`, or `null` for `static fn` | enclosing class, or `null` at top-level |
  | FCC `$obj->m(...)` | method signature | `typeof($obj)` | method's declaring class |
  | FCC `Foo::m(...)` | method signature | `null` | method's declaring class |
  | FCC `foo(...)` | function signature | `null` | `null` |
  | `fromCallable($cb)` | inferred from `$cb` | `__CallableThis<typeof($cb)>` | `__CallableScope<typeof($cb)>` |

  `TScope` is always the class that physically **declares** the method
  (`ReflectionFunction::getClosureScopeClass()`), not the class name written at
  the call site — `Sub::inherited(...)` where `inherited` is only declared on
  `Base` reports scope `Base`, for both instance and static FCC and both
  array/string `fromCallable` forms (`WrapMethodCallableAsClosure` /
  `FindDeclaringClass`). Verified against PHP 8.5 via `ReflectionFunction`.

  Invokable `fromCallable` keeps the `__invoke` **intersection** of arity facets
  and drops the class from `TCallableShape` (Decision 12). `__CurrentScope` is
  an access check only — not copied onto result `TThis`. When the checker can
  see that `$callback` is another class's private method (array / `Class::method`
  string form), `TypeCompatibilityRule` reports TYHP4025. Bare `\Closure` stays
  gradual; annotations may write `\Closure<callable(int $i): string>`
  (`TThis` / `TScope` default). There is no `\Closure(int): string`.
  `\Closure<C, …>` assigns to `callable` / a callable facet when `C` does
  (`TypeComparer.TryCheckCallableAssignability`). Typed-local
  `Expression<callable(User $u): bool> $e = fn ($u) => …` captures the same
  way as a call argument (`TypeAnnotationRule` + `ExpressionTreeSupport`).
- **bind / bindTo / call (Story 21.6 Phase 5):** overlay two-arity overloads on
  `ObjectMethodSymbol.Overloads` (explicit `__ClosureScope<TNewThis>` vs
  omitted/`'static'` keeping `TOldScope` / current `TScope`).
  `FunctionOverloadSelector.SelectMethod` picks among those signatures: arity
  first (`CheckerHelpers.SelectMethodOverloadForCall`), then same-arity scoring
  against each candidate's **constraint** so `bindTo($x, 'static')` is not
  checked only against `__ClosureScope<…>`. Zero-argument and omitted-optional
  calls still score: the implementation's default value is compared to each
  candidate's parameter type so a literal-typed default (`0 $flags = 0`) wins
  over the `int` catch-all. `'static'` is never stored as
  `TScope` (TYHP4335 if written as a Closure type argument). Extra rules in
  `ClosureBindSupport` (overloads cannot express them):

  | Rule | When |
  |---|---|
  | Leftover-scope `instanceof` | omitted / `'static'` / `call`: `TNewThis` must still inhabit leftover `TScope` (or `TThis` when the stored scope is gradual) |
  | Object `$newScope` | `TNewThis <: typeof($newScope)` |
  | String `$newScope` | `__SuperTypeName<TNewThis>` (ancestors), not `__CompatibleTypeName` (descendants) |
  | Gradual `TThis` | `object\|null` / bare `\Closure` / omitted slots: allow bind (cannot prove error) |
  | Non-rebindable | arrow literals, FCC, `fromCallable` of a non-Closure, internal-class `$newThis`/`$newScope` → TYHP4336 |
  | `call(null)` | `TNewThis extends object` → TYHP4334 |

  Static closures (`TThis` known `null` and `TScope` a known class) cannot take a
  non-null `$newThis`. `null` `$newThis` is only legal when the closure is
  already unbound. The non-rebindable bit lives on `GenericCheckedType` and is
  not part of assignability or `DisplayName`. Bind inference unwraps late-static
  `$this` (`StaticCheckedType`) to the declaring class, same as Phase 4 producer
  `TThis`, so `bindTo($this)` does not store `static` as the result `TThis`.
- Untyped parameters require `ExpectedClosureType` from the call-site argument
  position (`SetExpectedClosureTypeFromArgument`); otherwise
  `CheckerClosureParameterTypeRequired`.
- **Story 16 — `PropertyPath<TCallableShape>`:** `SetExpectedClosureTypeFromArgument` /
  `SetExpectedClosureTypeFromAnnotation` map `\Tyhp\PropertyPath<TCallableShape>` through
  the callable facet of `TCallableShape` (typically `callable(TSource $source): TReturn`) so an
  inline `fn` at a PropertyPath parameter is contextually typed.
  When the argument *is* an inline function, `TypeCompatibilityRule.ValidateArgumentTypes`
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
- **Story 16 — `Expression<TCallableShape>`:** the same contextual mapping is applied via
  `ExpressionTreeSupport.TryMapToCallable`, which reads the single class type argument
  (`TCallableShape extends callable`) rather than treating extra type arguments as
  params+return. `Expression<callable(User): string>` is the use-site spelling;
  `Expression<User, string>` is a generic arity error. `GenericTypeArgumentValidator` and
  `GenericInheritanceBindings` treat Expression like any other one-parameter generic.
  Inline `fn` arguments require arrow syntax (TYHP4323), a body
  composed only of supported expression kinds (TYHP4322 — no assignment / await / yield /
  match / nested fn / free function calls / throw / include, …), and
  definitely-assigned outer captures (TYHP4324). `instanceof` / `is` are allowed (RHS is a
  type name, builtin, or captured class-name variable). Forwarded `Expression` values and `null` for
  nullable parameters still pass via assignability. Helpers live in `ExpressionTreeSupport.cs`
  (not a Transformers layer). At call sites, `ResolveCalleeParameterType` keeps PropertyPath /
  Expression wrappers that still mention unbound method generics (`select<R>(Expression<callable(T): R>)`)
  instead of collapsing the whole type to mixed; it substitutes those method parameters with
  mixed so the inline `fn` is still contextually typed from the class generic (`T` → `User`).
  Symbol-name brands with unbound method generics (`__ClassName<T>|__InterfaceName<T>` on
  `assertInstanceOf`) likewise keep the brand shape, defaulting unbound args to `object`
  (bare-brand equivalence) so a non-name like `1` is still TYHP4010 rather than accepted via mixed.
  Callable-facet argument slots (`$fn(...)`) and `|>` use that same substitution
  (`GradualizeUnboundCallableParameter`). Unbound `__EnumName<T>` is filled with the unbound-enum
  placeholder, so a written `__EnumName<object>` stays invariant and does not accept every enum.
  `ResolveNamedType` looks up in-scope `ObjectGenerics` / `FunctionGenerics` /
  `EnclosingObject.GenericParameters` before treating a bare name as a generic instantiation.
  `TryInferGenericBindings` for methods also resolves parameter annotations via
  `ResolveDeclaredTypeOnReceiver` (declaring-class `ObjectGenerics` + receiver substitution),
  so chaining off `select<R>(Expression<callable(T): R>)` (`->select(...)->sortBy(...)`) does not
  report TYHP3003 on the class parameter `T` when the return type is inferred at the call site.

### 7.6 Operators

`TypeInferrer.Operators.cs` encodes native PHP numeric promotion / division / exponentiation
result types used when no matching operator overload applies. Native `<=>` infers `-1|0|1`
(PHP always returns one of those three ints), a subtype of `int`. Overloaded `<=>` keeps
its declared return.

When operand types match a declared `operator` form, `TypeInferrer.OperatorOverloads.cs` prefers
that form's declared return type instead (same left-first then right selection as
`AliasConverter` / `OperatorOverloadResolver`, including extension-contributed and native-passthrough
tyhpdef forms for declared return truth). Operator parameter types that are `type` aliases (for
example PECL decimal `DecimalValue = Decimal|NumericString|int`) expand before matching, so
`$left + $right` both `\Decimal\Decimal` selects `operator +(self, DecimalValue)`. Binary ops, unary ops (`+`/`-`/`~`/`!`/`++`/`--`), and
compound assigns (`+=`, `>>=`, …) all go through this path before falling back to native promotion.

**Object operands without a matching form:** PHP throws `TypeError` for arithmetic / bitwise /
unary `+` `-` `~` `++` `--` on objects, and for concat of objects that are not `\Stringable`.
`TypeCompatibilityRule` requires a matching Story 11 overload (unary vs binary are distinct
enums — unary `+` is `Plus`, binary is `Add`) and reports TYHP4029
(`CheckerInvalidOperatorForType`) when none matches. Concat of scalars or `\Stringable`
objects (including `__toString` auto-implement) uses the native PHP path instead of an
overload. Comparisons on objects stay on the native PHP path (overloads optional). `OperatorOverloadResolver.SelectMatchingBinaryForm` returns
null when both operand types are known and no form's parameter types match — it does not fall back
to a wrong-shape first-arity candidate. Either operand resolving to the checker's `Unresolved`
error-recovery marker (a different, already-reported resolution failure) skips this check entirely
rather than cascading a second, misleading TYHP4029 on top of it.

`self` / `static` in the overload return type resolve against the owning class or, for an
extension operator (`extension Foo extends string { operator * (self, int): self }`), against that
block target — `ResolveOperatorOverloadReturnType` forks `CheckerState` and seeds both
`EnclosingObject` (the declaring extension symbol, so the "used outside class" guard in
`ResolveRelativeType` does not fire) and `EnclosingObjectType` (the actual `self` value) regardless
of what the call site's own enclosing class/object happens to be. `static::` and `parent::` inside
an extension member are TYHP4368.

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

Unary `yield` / `yield from` are handled in `InferUnary` before overload lookup
(they are not overloadable); `PhpYieldAst` is a dedicated `InferExpressionTypeCore`
arm. See §7.2 for TSend / TReturn / null rules.

Value-producing casts (`InferCastType`) type `(int)` / `(bool)` / `(string)` /
`(float)` / `(decimal)` / `(array)` as the matching builtin. `(object)` matches PHP:
an operand that is already an object is untouched (`InferObjectCastType` returns the
operand's own type unchanged — no new instance, same class), so casting an already
`object`-typed, `\stdClass`-typed, user-class-typed, or all-object-union value is an
identity no-op (and `CodeQualityRule`'s redundant-cast check flags it, except when the
operand is unresolved/mixed — those are recovery/gradual types, not a no-op conversion).
Only a genuine
array / scalar / null / `mixed` operand (not already an object) is converted, and that
conversion resolves the engine class `\stdClass` (`ObjectDeclarationSymbol`), not
`BuiltInTypeSymbol("object")`. Undeclared-property writes on the conversion result
therefore follow exact `\stdClass` (TYHP4134 named gate in `RestrictedFeatureRule`);
the identity case is not a license for undeclared writes on any other type the
operand happens to be.

PHP 8.5 `(void) expr` is a **discard**, not a value-producing cast (`InferUnary` →
`CheckedTypes.Void`). Grammar keeps it out of value positions; assignability still
rejects void if it appears. `TypeCompatibilityRule` (`TypeCompatibilityRule.VoidCast.cs`)
type-checks the operand and allows `mixed` (discard is not a type-specific use).
Wrapping a call is the intentional-discard form for `#[\NoDiscard]`:

- Discarded call / for-list item to a NoDiscard-marked callable → warning TYHP4165
  (`CheckerHelpers.ReportNoDiscardIfDiscarded` from
  `CheckerRuleContext.CheckStatementSequence` for function/method bodies and nested
  blocks, and from `ControlFlowRule` for for-lists)
- Warns from the **invoked** declaration only. Interface and abstract method callees
  do not warn (PHP would not). An override does not inherit the warning unless it is
  itself marked. Trait-imported methods keep the attribute (PHP copies it onto the
  using class).
- A string-literal `#[\NoDiscard]` `$message` is `{1}` on TYHP4165 (`": " + message`,
  or empty `{1}` when omitted / not a literal), same pattern as TYHP4500.
- `(void) call` → suppress TYHP4165. Used return (including `$_ = …`) does not warn.
  Not gated on `output.phpVersion`.
- ExtCore `NoDiscard` class is Story 21; until then unbound `#[\NoDiscard]` is
  allow-listed like `Override` / `AllowUnset`, and the attribute is detected by name
  on the callee declaration

`for` condition lists (`for_cond_exprs`): only the **last** item is a boolean
condition; preceding items (including `(void)` and discarded calls) are checked as
side-effect expressions only.

### 7.5 Attribute targets (`AttributeRule`)

`AttributeRule` validates attribute classes (TYHP4126), `Attribute::TARGET_*`
flags (TYHP4127), repeatability (TYHP4128), `#[Override]` (TYHP4129 methods
and PHP 8.5+ properties; TYHP4339 on `__construct`),
`#[\Tyhp\PhpType]` (TYHP4327 / TYHP4329), and
`#[\Tyhp\NativeTypeTest]` (TYHP4340–4344; index of `T →` function or concrete static method
is built after the walk
so overlay tyhpdefs that are bound but not visited still register). An object-shape alias
body (`object { … }`) is not keyed as `object`, so `$x is ClockShape` does not lower to
`\is_object($x)`. A callable-shape alias body (`callable(…): R`) is not keyed as
`callable`, so `$fn is Predicate<int>` does not lower to `\is_callable($fn)`.

Dispatch:

- Functions / object types / **top-level** `PhpConstDeclListAst` → registry
  `HandledNodeTypes` via `CheckNode`.
- Class members (methods, properties, parameters, enum cases, class consts) →
  `ValidateDeclarationAttributes` from `DeclarationRule` member paths (not
  registered, to avoid double-fire).
- Operator-overload operands (`LeftParameter` / `RightParameter`, both use
  `attributedParameter`) → `OperatorOverloadRule.Check` calls
  `ValidateDeclarationAttributes` on each explicitly, same reason: it sets
  `SuppressChildTraversal`, so `CheckNode` never reaches either operand.
  Operator bodies are checked under `Split(ScopeType.StaticMethodDeclaration)`
  with those operands seeded into `Variables` the same way `CheckMethod` seeds
  parameters, so `$value->value` inside `operator convert` resolves instead of
  becoming unresolved (which would false-positive TYHP4205 on `(float)$value->value`).

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
guessing. A **promoted constructor parameter** is both a parameter and a
property, so the generic flag check matches when the attribute has
`TARGET_PARAMETER` **or** `TARGET_PROPERTY` (for example Core `\Override` is
`TARGET_METHOD|TARGET_PROPERTY` and is legal on a promoted param once property
targets are allowed). `Override` / `AllowUnset` keep name-based special cases.
Unbound `#[\Tyhp\Php]` on omitted version-gated variants is treated as a known
compile-time attribute (same allow-list as `Override` / `NoDiscard` /
`Deprecated`) so those arms do not produce TYHP4126.

`#[Override]` on a **method** that does not override a non-private ancestor or
interface method is TYHP4129. Trait bodies are skipped (PHP checks the composing
class). `#[Override]` on `__construct` is always TYHP4339 (any `output.phpVersion`)
— constructors are exempt from override semantics, so a parent constructor does
not make the attribute legal. On a **property** (including a promoted constructor
parameter), `output.phpVersion` **&lt; 8.5** is still a TARGET mismatch (TYHP4127);
**≥ 8.5** walks the same parent + interface graph as methods and reports TYHP4129
when no same-name non-private property exists. Covered by `AttributeRuleTests.cs`.

`#[\DelayedTargetValidation]` on a declaration, when `output.phpVersion` is
**≥ 8.5**, skips **TARGET_*** (TYHP4127) for **PHP Core engine** attributes on
that same declaration (`\Override`, `\Deprecated`, `\NoDiscard`,
`\SensitiveParameter`, `\ReturnTypeWillChange`, `\DelayedTargetValidation`,
`\Attribute`, `\AllowDynamicProperties`). Functional checks are not skipped:
`#[\Override]` still TYHP4129 when it does not override, and `#[\NoDiscard]`
unused-return is still TYHP4165. Userland attributes still get TYHP4127.
`output.phpVersion` **&lt; 8.5** ignores the skip (property `#[\Override]` remains
4127 even with this attribute). Unbound Core names (isolated tests without the
ExtCore stub) are treated as engine classes; declarations under `runtime/packages/php/`, `/packages/php/`, or the resolved runtime-src `php` package are engine sources too. A user `.tyhp` class that reuses a
Core name is not. `\Tyhp\AllowUnset` / `\Tyhp\Php` are not Core and are not
skipped. Covered by `AttributeRuleTests.cs`.

`#[\Deprecated]` (`Deprecated` / `\Deprecated`, optional `$message` / `$since`)
does not go through `AttributeRule` for the use-site warning. The **binder**
sets `BaseSymbol.IsDeprecated` when that attribute is on the declaration (in
addition to the tyhpdef `deprecated` keyword). `DeprecationRule` then emits
TYHP4500 on references. A string-literal `$message` is stored as
`DeprecatedMessage` and appended after the name (`{0}` name, `{1}` `": " +
message`, or empty `{1}` when omitted). `$since` is not in the short message.
The warning is **not** gated on `output.phpVersion` (same policy as
`#[\NoDiscard]`); the engine class stays `#[\Tyhp\Php(">=8.4")]` in tyhpdef.
Class-constant attributes live on the const **list** AST, so bind also applies
the attribute from that list onto each constant symbol.

Covered by `DeprecationRuleTests.cs`.

`#[\Tyhp\PhpType('…')]` short-circuits `TARGET_*` / `Override`. Allowed hosts are
PHP type-declaration sites only: parameter, function, method, closure, property
declaration, and const list/decl (TYHP4327 otherwise — including class /
interface / trait / enum, enum case, catch, local, and property hook). The
constructor string must be a PHP type-hint spelling (`mixed`, `int`,
`string|int`, `?\Foo`); missing or invalid spelling is TYHP4329. Checker types
are unchanged; emit replaces the host’s PHP type (`TyhpEmitter.PhpType.cs`).
Hook `get`/`set` are rejected; put the attribute on the property or the `set`
parameter. Catch and typed locals cannot parse attributes today; the checker
still rejects them if attributes are present.

Covered by `PhpTypeAttributeTests.cs`.

### 7.6 PHP version gates (`PhpVersionRule`)

Feature-band diagnostics **4300–4306** plus **4371**. `PhpVersionRule` reports 4371
(`CheckerPhpVersionAttributeInvalidMember`) for `#[\Tyhp\Php]` on a property, class constant, enum case, or
interface method in `.tyhp` source (`RejectTyhpPhpOnUnconditionalMember`); tyhpdef members are exempt because
the binder gates them at compile time and PHP never sees them. Binder already reports 4301 (mixed
`declare`), 4303 (overlapping same-name gates that would also collide as
ordinary overloads — same parameter-list shape, or a static method and an
instance method of the same name), 4304 (attribute on struct /
extension), and 4305 (missing/non-string `version`). 4303 keeps the primary span
on the later declaration and labels the first overlapping gated declaration
`declared here`. The checker fills **4300**
(invalid Composer constraint) and **4302** (nested unsatisfiable gate), and
double-checks the binder codes. `DiagnosticBag` de-dupes identical reports, so
binder+checker on the same span do not double-fire.

**4302 is not “this arm is inactive for `output.phpVersion`.”** Alternate
variants (`declare(php=">=8.4")` beside `declare(php="<8.4")`, or disjoint
`#[\Tyhp\Php]` overloads) are not unreachable. 4302 fires only when an inner
constraint cannot overlap the enclosing constraint stack
(`PhpVersionConstraint.AnyOverlap` is false) — e.g. outer `>=8.4` with inner
`<8.3`. File-level `declare(php=…);` constraints are loaded onto
`CheckerState.PhpVersionConstraintStack` at the start of each file from
`FileSymbol.PhpVersionConstraints`. Block `declare` pushes a copy via
`Split(DeclareBlock)` and walks the body itself (`SuppressChildTraversal`).
After a satisfied block's body is checked, its variable state flows back to the enclosing state.
A block the emitter writes behind a runtime check (`declare(ext=…)`, or a php gate that is only
sometimes true; `NeedsRuntimeCheck`) joins like an `if` without `else`. A
`declare(ext="x")` directly followed by `declare(ext="!x")` is joined like `if`/`else` by
`CheckerRuleContext.CheckStatementSequence` (`CheckComplementaryExtBlocks`; the arms are marked in
`IsPairedGateBlock` so `CheckDeclare` absorbs each straight into its arm state).
A **satisfied** block uses full `CheckNode`. An **unsatisfied or invalid**
block uses `ScanUnboundRegion` only (nested 4300/4302/4305) — full type-check
of a binder-omitted extension would treat `$this` as illegal in a static
method (4097) and resolve calls against the current `output.phpVersion`
(4182 for APIs that exist only on the gated minor). Compound Composer
constraints (`">=8.4 <8.5"`) parse as AND; inactivity is the same as a
simple `">=8.4"` that the target does not satisfy.
A file whose file-level gate is unsatisfied (valid constraint, inactive) is
not walked at all — see `Check` above.

**4306** (`CheckerPhpVersionDefaulted`) is a **warning once per compilation**
when `CompilationOptions.PhpVersion` was empty and defaulted to `8.2`
(`PhpVersionWasDefaulted`). Emitted at the start of `TyhpChecker.Check`, not
per file.

Class members bypass `CheckNode`; `DeclarationRule` / `ExtensionRule` call
`CheckerRuleContext.ValidatePhpVersionMember` (same pattern as
`AttributeRule.ValidateDeclarationAttributes`). Extension members are keyed by
the extension type FQN (`obj:` + FullyQualifiedName), matching the binder's
`ObjectDeclarationScope` — not the enclosing namespace. Ungated
`fn first(extends string …)` and gated `fn first(extends array …)` in different
`extension` declarations therefore do not 4303. `ScanUnboundRegion` pushes the
same container (synthesizing `obj:\Ns\Name` when the binder omitted an inactive
extension) so inactive `declare(php=…)` variants still overlap only against
that extension type.

**8016** (`TyhpdefPhpVersionGateOnPropertyHook`): `#[\Tyhp\Php]` on an individual
tyhpdef `get` / `set` hook. Version gates belong on the property or an enclosing
`declare(php=…)`. 4304 stays struct/extension-only (its message cannot name hooks).
Hook-level `#[\Tyhp\Php]` in Tyhp source is not 8016 (library tyhpdef generation skips copying it).

**8015** (`TyhpdefPropertyHookBodyNotAllowed`) is allocated on `DeclarationRule`
as a visitor-regression guard: tyhpdef hook grammar is bodyless, so a non-null
hook body on a `.tyhpdef` property is reported instead of type-checked.

### 7.7 Call-site splice (`InlineSpliceRule` + `ReferenceTrackingRule`)

The emit-side splice engine (`Tyhp/TyhpLang/Emitter/Splice/`) substitutes a
single-`return` **extension** body at the call site (tyhpdef thin mappings
included). Nested reduction (`ReduceNested`) uses the same ownership gate
(`CallSiteSpliceEngine.IsSpliceOwnedCallee`): it does not inline an ordinary
class method just because that method happens to be a single `return`.
Splicing is always on (`optimize: none` does not disable it). Cycle detection
(`TYHP4175`) additionally follows `#[\Tyhp\Optimize\Inline]` members. The checker
owns the safety diagnostics:

| Code | When |
|------|------|
| TYHP4174 | Body writes a parameter not declared `&`, declares `&` on an unwritten parameter, or mutates `$this` without `&` on a class-body thin mapping. Passing a parameter to a callee's `&` parameter is a write — free functions (`\ksort` / `\sort`) and static/instance methods (`Json::tryDecode($this, $out)`). Declaration checks run before `CheckNode`, so method callees are resolved via SymbolTree rather than `BoundSymbol`. On a block-target extension the receiver annotation is `&$this` (TYHP4369 when the body writes `$this` without it, TYHP4370 when `&$this` is unused); TYHP4174 does not also report that receiver. |
| TYHP4175 | Direct or transitive cycle among spliceable single-return members. |
| TYHP4176 | `#[\Tyhp\Optimize\Inline]` on an extension member (form decides splicing, not the attribute). |
| TYHP4177 | User variable / parameter whose name starts with `GeneratedNames.InlineTempVariablePrefix` (`__tyhpInlineTemp`). Also enforced from `DeclarationRule` parameter validation. |
| TYHP4179 | Spliced body references a member less accessible than the spliced member. Extension members are treated as public. |
| TYHP4180 | Argument to a `&` parameter is not referenceable (general call-site rule in `ReferenceTrackingRule`, every optimize level), including the receiver of `extends T &$this`. Reads `HasAccessor` / `GetHookReturnsRef`: `{ get; }` is not referenceable; `{ &get; }` is; `&__get` / `&offsetGet` stay as they are. |
| TYHP4181 | Erased member (no PHP backer) at a call site that cannot be spliced faithfully. Members that emit a PHP method decline the splice and keep the real call instead. |

`HasPhpBacker` is `!IsShortSyntax` for Tyhp `extension { }` members: short `=>`
is erased; a brace body keeps a PHP method even when it is a single `return`.
Tyhpdef class-body thin mappings and compiler-generated synthetic extension
classes are always erased. Class-owned methods and operators always have a
PHP backer. An unspliceable call to an erased member is `TYHP4181`; the same
shape on a brace member keeps the real call instead.

Because a tyhpdef `extension operator ... => expr` mapping is always erased,
`DeclarationRule.ValidateOperatorOverloadSet`'s reserved-generated-name check
(a real method may not share an operator's synthesized name — TYHP4074) skips
`TyhpOperatorOverloadAst.IsInlineExtension` operators: the mapping's whole
point is to call an already-declared real method of that name (e.g.
`extension operator convert(self $v): int => $v->__toInt();` alongside a
real `__toInt()`), not to synthesize a conflicting one. A native (non-
`extension`) operator still synthesizes its own backing method and keeps
reserving its generated name.

`ControlFlowRule` suppresses child traversal on `return`, so this rule also
handles `PhpJumpStatementAst` / `PhpReturnStatementAst` and walks call sites in
the returned expression. Tyhpdef `=>` bodies desugar to a unary `return`; splice
extraction accepts that shape. Class-body `extension fn` call sites resolve
through `ObjectDeclarationSymbol.FindSyntheticInlineMember` (implicit `$this` is
prepended on the method symbol; authored parameters follow it).
`#[\Tyhp\Optimize\Inline]` lives on `TyhpdefInlineExtensionFunctionAst`, not the
inner method node, and is `TYHP4176` at the **declaration** (and again at a call
site if one is walked).

Included tyhpdefs are bound into `GlobalScope` but are often absent from
`ParsedFiles`. After the main walk, `TyhpChecker.CheckBoundTyhpdefThinMappings`
re-checks them. Class-body thin mappings (`extension fn` / `extension operator`)
run `InlineSpliceRule.CheckMemberDeclaration` so `4176` / `4174` / `4175` /
`4179` still fire without a user call site. A standalone
`TyhpdefStandaloneExtensionDeclAst` runs `ExtensionRule.Check` with a file-level
`CheckerState`, the same entry a project-source standalone extension uses. That
applies the block-target declaration checks: missing header or nested group
(TYHP4147), header mixed with nested groups (TYHP4362), a loose member beside a
group (TYHP4363), target shape (TYHP4364), unused type parameters (TYHP4365),
method-generic shadowing (TYHP4366), intra-extension overlap (TYHP4367),
`static::` / `parent::` (TYHP4368), and `&$this` (TYHP4369 / TYHP4370). The same
call runs the splice-member checks `ExtensionRule` already performs for tyhpdef
members. A standalone extension operator is checked through
`CheckExtensionOperatorOverload`, which seeds `EnclosingObject` (the extension)
and `EnclosingObjectType` (the block target) before the signature's `self` is
resolved, so `self` means that target. Class-body `extension fn` / `extension
operator` stay on the thin-mapping path; they have no header `extends`.
`CheckMember` unwraps `TyhpdefInlineExtensionFunctionAst` to the inner method
so `BoundSymbol` and authored `&$this` are visible (they live on the method, not
the wrapper). Before resolving the receiver/parameter types used for 4179, it
`Fork`s and seeds `FunctionGenerics` from the callee so method type parameters
(`TKey`/`TValue` in `array<TKey, TValue>` / `callable(TValue, TKey): bool`)
resolve instead of TYHP3003 — declaration checks often run on the outer
extension/object state, which has no method generics. On a block-target
extension, writing `$this` (assignment, `++`/`--`, or passing it to a `&`
parameter) requires the `&$this` annotation (TYHP4369) and an unused
`&$this` is TYHP4370. Class-body thin mappings still use TYHP4174 for a
`$this` write. Object targets that only call methods on `$this`, or assign
through one of its properties, do not need `&$this` (the handle is shared).
Scalar / array / struct targets are by-value, so the same kind of write
through a `$this`-rooted member or array-access chain (`$this->field = …`,
`$this[$k] = …`) does need `&$this`.
`ReferenceTrackingRule` also applies `4180` to the call-site receiver when that
first parameter is `&`.

Class methods bypass `CheckNode`; `DeclarationRule.CheckMethod` calls
`InlineSpliceRule.CheckMemberDeclaration`. For a class-body thin mapping it
seeds `$this` as the enclosing tyhpdef type while keeping `EnclosingObject` as
the synthetic extension (so `IsExtensionReceiverThis` stays true) and zips
authored AST parameters against the symbol list with the implicit `$this`
skipped. Extension functions go through `ExtensionRule.CheckExtensionFunction`
(same hook).

---

## 8. Control flow, narrowing, type guards

### 8.1 `ControlFlowRule`

Owns if, loops, try/catch, statement blocks, returns, jumps, conditionals,
yield (unary `yield` / `yield from`, plus unused `PhpYieldAst`), goto
(prohibited), echo, ternary, and unary `throw` / synthetic unary `return`
(expression-bodied callables).

**Unreachable code (TYHP4012):** statement lists warn once on the first
executable statement after a definite terminator — `return`, `throw`,
`break`, `continue`, `exit`/`die`, or a never-typed expression statement.
Later statements in the same list are still type-checked (other diagnostics
can still fire) but do not each get another 4012. Nested `{ … }` that is
itself that first dead statement is warned as a unit; inner statements are
not re-warned. Empty `;` (`PhpNopStatementAst`) is skipped so the warning
lands on the next real statement. Function/method bodies walk through
`CheckerRuleContext.CheckStatementBlock` → `CheckStatementSequence`; nested
`{ … }` blocks and braced if-arm bodies use `CheckStatementSequence` on the
branch state. A braceless if-arm (`if ($c) exit;`, no `{ }`) is a single
statement rather than a list, so it never enters `CheckStatementSequence` —
`ControlFlowRule.CheckStatement` instead calls
`CheckerRuleContext.MarkTerminatedIfNeverReturning` directly after checking
that one statement, so `exit`/`die`/never-typed-expression arms still mark
the branch as terminated (`return`/`throw`/`break`/`continue` already do this
via their own dispatch either way). Match/switch arms stay on TYHP4208
(`CheckerUnreachableArm`), not 4012.

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

Conditions are type-checked on a disposable probe so progressive `&&` /
`||` operand narrowing during validation does not leak into the post-if
continuation (`CheckConditionExpression`).

Logical conditions must be bool-ish (Tyhp differs from PHP truthiness) —
enforced via helpers used from control-flow checking.

**Foreach / catch variables:** `DeclareForeachVariable` (and catch bindings in
`CheckTryCatch`) record the loop/catch variable's type on those AST nodes via
`ResolveExpressionType`, including the inner `$var` under the extra
`PhpVariableAst` wrapper `VisitForeachVariable` adds. Typed foreach bindings
(`foreach ($xs as string $v)` / `as int $k => string $v`) store the annotation
on that wrapper's `Type`. Compatibility: the iterable key/value type must be
assignable **to** the declared type (TYHP4196); the loop variable then has the
**declared** type and is not narrowed to a more specific iterable type. Omitting
the annotation still infers from the iterable. `ControlFlowRule`
suppresses child traversal on `PhpLoopAst` / `PhpTryCatchAst`, so those binding
sites would otherwise never enter `InferExpressionType` — language-server hover
on `as $x` or `catch (E $e)` would have no checker type.

**Duplicate catch types:** the same exception type in a later `catch` is
TYHP4124 (warning). The primary span is the duplicate type; a `declared here`
label points at the first catch type. Empty catch remains TYHP4121.

Foreach key/value typing (`ExtractIterableKeyType` / `ExtractIterableValueType`):
`array`/`iterable` builtins use positional type arguments (value last; key
first when present) **before** named/anonymous struct shapes, so
`foreach` over `array<Struct>` / `iterable<Struct>` binds the value as `Struct`
(same as `$arr[0]`). Struct shapes themselves (iterating a struct value, not an
array of structs) use string property-name keys; otherwise the
iterated type's `Iterator` / `IteratorAggregate` / `Traversable` contract is
resolved via `GenericInheritanceBindings.TryGetTraversableIterationTypes`
(substituting the receiver's own type arguments into the implements clause) so
types like `Generator<TKey,TValue,TSend,TReturn>` and
`SplPriorityQueue<TValue,TPriority>` do not mis-bind from their own parameter
positions. Other generics still fall back to positional first/last.

**Class constants (Tyhp / tyhpdef):** `CheckClassConstants` requires a type on
the original class/interface/trait/enum declaration (TYHP4194) or infers it
from the nearest typed ancestor (parent, then interfaces, then traits). A child
redeclaration that changes the type is TYHP4195. File-level `const int X` in
`.tyhp` is TYHP4193 (`TypeAnnotationRule`); `.tyhpdef` file-level typed consts
use a separate import grammar and are legal. Object-shape member consts
(`type S = object { public const int FOO = 1; }`) are not file-level: the visitor
stamps `objectShapeMember` so TYHP4193 does not fire.

Generator `return`: inside a generator, `CheckReturn` checks the returned
expression against `TReturn` from `Generator<…, TReturn>` (or `mixed` for bare
`Generator` / `Iterator` / `Traversable` / `iterable`), not against the
`\Generator` type itself — matching PHP's `Generator::getReturn()` payload.
Falling off the end without `return` contributes `null` to inferred TReturn.
A generator whose declared return is not in the Generator / iterable /
Iterator / Traversable family reports TYHP4087 (`function foo(): string {
yield 1; }`). After the body visit, `GeneratorBodyInference` (see §7.2)
checks declared two-arg / four-arg pins against the inferred tuple (also
TYHP4087) and records the inferred generic on the callable so call-site
`ResolveFunctionReturnType` / `ResolveMethodReturnType` / `InferClosure` see
it for bare and two-arg `\Generator`. Written `\Generator` is not treated as
the Layer 3 default `Generator<mixed, mixed, mixed, mixed>` — that would pin
TSend and hide body inference from callers. `InferClosure` `CheckNode`s a
generator closure before reading that map, because `$fn = function(): \Generator
{…}` types the RHS during `CheckBinaryOp` before the child walk would run
`ClosureRule`.

**Yield:** `yield $v` / `yield $k => $v` parse as `PhpYieldAst`; `yield from`
and bare `yield;` stay prefix `PhpUnaryOpAst`. `Handles` admits those unary
operators (in addition to `throw` / synthetic `return`) so `CheckYield` runs
on real sites. Expression types are inferred in `TypeInferrer` (§7.2), not
here. `CheckYield` also feeds `GeneratorBodyCollector` on the checker state
(keys, values, yield-from merge). `CheckerYieldOutsideGenerator` (TYHP4086) uses
`IsInGeneratorContext` alone — the same closure-boundary rule as generator
`return` unwrapping (`EnclosingFunction.IsGenerator` would leak the outer
callable's generator-ness into a nested non-generator). `CheckerYieldInFinally`
(TYHP4088) fires when `IsInsideFinally` is set by `CheckTryCatch`.
`CheckerReturnInFinally` (TYHP4122) is an error for a `return` in that same
`finally`, including one nested in `try` / `if` / a loop. Entering a function,
method, closure, arrow function, or `async` block clears `IsInsideFinally`, so
a `return` inside a callable written in the `finally` belongs to that callable.
`CheckerYieldFromNonIterable` (TYHP4089) requires the `yield from` operand to
be `array` / `iterable` / `Traversable` (including `Generator` / `Iterator`);
generic wrappers are unwrapped so `Generator<K,V,…>` is not rejected, and every
union member must be iterable. Unary yield does not suppress child traversal,
so operand operators still reach `TypeCompatibilityRule`. `PhpYieldAst`
suppresses child traversal (key/value walked here). A yield used as a
**call argument** (`useIt(yield from 123);`) is `CheckNode`'d by
`TypeCompatibilityRule.CheckCall` — the call's own child walk is
suppressed, so `CheckYield` would otherwise never run on a bare
call-argument statement. Typed yield-send assignment / argument sites skip
mixed→T assignability when TSend is unpinned and record the target as a
TSend constraint instead.

### 8.2 `TypeNarrowingRule`

Static helper (not registered as `ICheckerRule`). Entry:
`ApplyConditionNarrowing(condition, branchState, context, symbolTree, globalScope, positive)`.

Supports:

- Unwrap parentheses and logical `!` (flips polarity).
- Branch bodies: `&&` in positive / `||` in negative (De Morgan); other
  combinations do not narrow a branch. Separately, short-circuit *operand*
  checking applies the left's positive narrowing to the right of `&&` and the
  left's negative narrowing to the right of `||` (`TypeCompatibilityRule.CheckBinaryOp`).
- `is` / `instanceof` (variables, `$this->prop`, enclosing-class `self::$prop` /
  `static::$prop` / `ClassName::$prop`, `$var->prop`, constant or simple variable index).
  Type aliases expand to the alias body for narrowing (`$x is UserId` where
  `UserId = int` narrows to `int`; `$fn is Predicate<int>` expands the generic
  alias to the callable shape so `$fn(1)` type-checks). Emit still uses
  `\Tyhp\Type::is($x, UserId())` / `\Tyhp\Type::is($fn, Predicate(\Tyhp\Type::int()))`.
- Strict identity comparisons against `null` / `true` / `false`
  (`!==` / `===`; `$x !== false` drops `false` from `T|false`).
- `isset` / existence probes.
- Type-guard callables (`$param is Type` or `$array[$key] is Type` return types) from tyhp and tyhpdef only — including PHP `is_*` / `*_exists` overlays and call-site generics (`isType<int>($x)`, `\class_exists<Foo>($n)`).
  Omitted trailing type arguments use each parameter's default
  (`T extends object = object` → narrow to `__ClassName<object>`). When the
  callee is generic and the call omits type arguments, guard resolution runs
  `TryInferGenericBindings` first so `T` can bind from a sibling argument
  (`T|__ClassName<T>` vs a receiver object or `Foo::class` / `__ClassName<Foo>`).
  That is how `\property_exists($widget, $name)` / `\method_exists(Foo::class, $n)`
  produce `__PropertyName<Widget>` / `__MethodName<Foo>` from the tyhpdef
  `$param is __PropertyName<T>` return. Call-site substitution for ordinary (non-guard) calls does
  **not** eagerly apply those defaults — omitted type arguments stay open until
  argument-driven inference fills them. Applying a `TReturn = void` default
  before inference would require `callable(): void` and reject real callbacks.
- Free-function guard callees with multiple tyhpdef overloads of the same name
  (`is_a`'s object-instance vs. `$allow_string = true` class-name-string forms,
  `is_callable`'s `$syntax_only = false` narrowing vs. syntax-only `: bool`)
  pick their guard via `FunctionOverloadSelector.Select` — the same
  argument-compatibility scoring ordinary call checking uses — not arity alone.
  Same-arity overloads (`is_a($x, Y::class, true)` fits both signatures) need the
  literal-argument scoring to land on the right guard; picking the wrong
  same-arity overload would narrow to the wrong (and unsound) target type.

Assignment resets narrowing via `ResetNarrowingOnAssignment` (variable + index
+ member maps).

Positive/negative type algebra uses `TypeComparer.NarrowType` /
`NarrowTypeNegative`. A true type-guard assertion is kept even when it does
not meet the current type (`callable` after `is_array` becomes `array`, not
`never`). Declared intersections still use `IntersectTypes`.

### 8.3 Type guards on declarations

`TypeGuardValidation`:

- Guard return AST → expected return type is `bool`.
- Validates every variable named by the guard subject is a parameter: the
  bare `$param`, or both the array and (when it is a variable) the index of
  `$array[$key]`. Constant indices are not parameters.
- At a call site, `TypeNarrowingRule.TryResolveCallSiteGuardSubject` substitutes
  those parameters into the subject (`$array` → the array argument, `$key` →
  the key argument) and then narrows the resulting `$arr[0]` / `$arr[$k]`
  through `IndexAccessNarrowing`.
- `DeclarationRule` sets `IsTypeGuardFunction` when checking such callables.

### 8.4 Null safety and definite assignment

`NullSafetyRule` reports use-before-assign / possibly-null at variable and
`$this->prop` reads, but suppresses diagnostics under existence-probe contexts
(`??`, `??=`, `isset`, `empty`, `variable_exists`) and skips simple assignment
LHS as reads.

`PropertyInitializationAnalysis` seeds constructor vs instance-method
property-init maps (Prop-init #7), including enclosing-class static properties
so `self::$x` shares `$this->prop` narrowing, and records post-construction
guarantees on instance symbols for later methods. Static methods seed only
static properties. `UnsetTrackingRule` implements Prop-init #8.

### 8.5 Code quality (`CodeQualityRule`)

Always-true/false conditions (TYHP4204) apply to `if` / `while` / `do` / the last
`for` test / ternary. `match` subjects are skipped: `match (true) { $cond => … }`
is a value comparison, not an `if (true)`. Arm reachability runs from
`TypeInferrer.InferMatch` (so `return match (…)` still reports — ControlFlowRule
does not `CheckNode` a returned match). When a match arm statically always
matches the subject (`true` / `1 === 1` against `match (true)`, or a literal
identical to a value-match subject) and a later **non-default** arm exists, the
always-matching condition is TYHP4204 and each later non-default arm is
TYHP4208. `default` after an always-matching arm does not warn.

Redundant casts (TYHP4205) require a known, non-mixed operand. Unresolved and
`mixed` skip the warning because they are assignable to every target as recovery
/ gradual typing, not because the cast is a no-op.

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
  itself. Bare `new Box()` (no explicit type-argument list) additionally
  instantiates from `CheckerState.ExpectedExpressionType` when that expected
  type is a unique `Box<…>` of the same declaration (return / typed local /
  assignment / argument). Context wins over defaults. A union expected type
  infers only when every non-null member agrees; parent/interface expected
  types do not infer a subclass `new`. `ContextualNewInference` owns the
  match. Call arguments skip `CheckNode` of those constructions until the
  parameter type is known so the first inference already sees the expected
  type. Declaration sites also run `ValidateGenericParameterDefaults`
  (TYHP4310–4312). When a type argument is an in-scope generic parameter whose
  `ResolvedConstraint` was never filled (tyhpdef symbols are bound but not
  walked by `DeclarationRule`), `ValidateUserConstraint` resolves the
  parameter's constraint AST first so `TIn extends object` can satisfy
  `WeakReference<T extends object>` instead of reporting a spurious TYHP4035.
  A constraint referencing an earlier sibling parameter (Story 21.6's
  `TScope extends __ClosureScope<TThis>`) resolves that sibling as its own
  open `GenericTypeParameterSymbol` — `ResolveAndValidateUserTypeArguments`
  forks a `CheckerState` with the declaring symbol's own generics in scope so
  the name binds, then substitutes already-resolved sibling arguments
  (explicit or defaulted) into the resolved constraint via
  `TypeComparer.ResolveGenericType` before comparing, mirroring how
  `ResolveDefaultTypeArgument` substitutes siblings into a default type.
  `ResolveTypeExpressionCore` does not expand aliases; after that sibling
  substitution, `ValidateUserConstraint` runs `TypeComparer.ExpandTypeAliases`
  on both the bound and the type argument so bounds written as `__ClosureThis`
  (`object|null`) and `__ClosureScope<TThis>` compare against the expanded
  union (a defaulted TScope is still `__ClosureScope<Host>` until expansion).
  `\Closure<C, Host>` / `\Closure<C, Host, Host>` therefore type-check when
  `Host` is a class. Overlay aliases stay spelled as aliases in the tyhpdef.
  `__CallableThis<C>` / `__CallableScope<C>` (overlay `fromCallable` result
  slots) skip the TThis / TScope constraint: they inhabit those unions by
  definition and stay deferred while `C` is still an open method generic
  (otherwise every `fromCallable` call reports a spurious TYHP4035).
- Built-ins / utilities / `callable` — existing arity + constraint rules.
- Utility types → `UtilityTypeResolver`.
- Builtin `callable` is zero-arity. Signatures are `callable(…): R` shapes → `CallableCheckedType` via `CallableArityFacetBuilder.BuildFromShape`. `callable(...): TReturn`
  is the any-arity facet (`IsAnyArity`): a wildcard parameter list, not a rest pack,
  and not a 0-arg facet (empty `ParameterTypes` with `IsAnyArity` false). Bare `...` as a
  type argument is TYHP4183. Direct parameter / property / return of the facet is TYHP4184;
  invoking it is TYHP4185. Known-arity callables assign to it when every applicable return <:
  `TReturn`; the facet does not assign to a known-arity target. A 0-parameter
  `callable(): TReturn` is a distinct 0-arg facet — `callable(int): TReturn` is not
  assignable to it. All-defaulted functions still assign via their 0-arg arity
  sibling. After `TryInferGenericBindings` succeeds on the **selected** overload,
  `ValidateInferredBindings` runs `ValidateUserConstraint` (TYHP4035) on each
  inferred (or explicit call-site) type argument. `\Closure<…>`
  is a class generic (`TCallableShape`, `TThis`, `TScope`), not this path.
- `\Fiber<…>` is a class generic (`TResume`, `TCallableShape`) from the Layer 3
  overlay. Do not invent `TSuspend` / `TStart` / a third type argument. Extra
  written args are arity errors. `resume($value)` is typed against `TResume`;
  `start` / `throw` / `Fiber::suspend()` **returns** stay `mixed|null` at every
  call site, including inside `new Fiber` callbacks (no CFA / `__CurrentFiber`).
  `getCurrent()` is `?\Fiber<mixed, callable>` — two args, not
  `callable(): void`-only. `getReturn()` is `__CallableReturnType<TCallableShape>`
  (not `null`).
- Builtins with `GenericParameterRequirements`.
- User classes / aliases — arity + per-parameter constraints. `never` is a
  valid type argument (the bottom type), including `array<never>` and
  `PromiseInterface<never>`. `void` is restricted unless the parameter's
  `extends` bound mentions it (`T extends void`, `T extends void|mixed`).
  The opt-in is read from the **declared** bound before sibling substitution:
  `TypeComparer.ResolveGenericType` rebuilds unions via `UnionTypesCore`, which
  collapses `void|mixed` to `mixed` and would otherwise make `Promise<void>`
  look like a non-return generic use of `void` (TYHP4048). `T extends mixed`
  still rejects `Foo<void>`. `array<void>` and other builtins that do not opt
  in still error. Callable-shape parameters still reject both `void` and
  `never`. Return types (`function f(): void` / `function f(): never`) are
  unaffected. Parameter and property positions still reject both.

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
   the body for **function/method** generics (including extension methods via
   `ExtensionRule`), then `MarkRequiresGenericVariant`. The scan covers
   `typeof`/`default`/`instanceof`/`is`, type arguments on `new Foo<T>` /
   `new self<T>`, and type arguments on calls (`decode<T>()`,
   `Type::isType<T>()`) so a wrapper can forward the bound type into another
   binder (grammar addons are not AstChildren; parameterized `new static<…>` is
   rejected as TYHP4168). After the full walk,
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
and global `__` utilities (`__FunctionReturnType`, `__CallableReturnType`,
`__Properties`, …)
at annotation resolution time. Per-parameter constraints for built-in
utilities run through `GenericTypeArgumentValidator.ValidateUtilityConstraints`
(shared `ValidateBuiltInConstraint`) before the behavior-specific `Resolve*`
methods.

`__Properties<T>` materializes a synthetic struct from `T`’s instance
properties (classes/interfaces, inheritance included, statics omitted) or
struct record keys. Each field keeps its declared type and is marked
`IsOptional`, so `[]` and any subset of keys assign and extra keys are
TYHP4031. This is not `__Partial` (which also wraps field types as
nullable) and not `__PropertyName<T> | __StructKey<T>` (those stay name
unions). An unbound type parameter stays a deferred `GenericCheckedType`
until call-site substitution; `ExpandAfterSubstitution` then rebuilds the
shape. `ApplyInferredBindings` treats it like the callable-signature
utilities so `clone($object, $withProperties)` can infer `T` from `$object`
and check the bag. Unbound `__Properties<T>` erases to `array`.

`__New<T>` (`UtilityBehavior.New`, registered in `StructUtilityTypes`) stays a
`GenericCheckedType` wrapper. `T` must be an object-shape alias (TYHP4351 for
`__New<int>` / `__New<User>` / `__New<object>`). Values are instances matching
the shape whose class is constructable as the shape constructor
(`TypeComparer.NewConstraint.cs`). Cycle stubs without a member map rebuild
constructability from the shape AST via `ExpandTypeAliases` so implied 0-arg
vs written `__construct` is not lost.

`MagicUtilityTypeResolver` handles `__SuperType` (T plus parent classes;
`object`/`null` → `object`; deferred `__SuperType<T>` while T is unbound
still satisfies `object` / `__ClassName`’s Object bound so signatures such
as `__ClassName<__SuperType<T>>` and `ReflectionClass<__SuperType<T>>` do
not report TYHP4035) and `__SuperTypeName` (a symbol-name brand,
inverse of `__CompatibleTypeName`, so class-name literals assign via
existence verification; `object`/`null` → `__ClassName`), 0-arity
`__CurrentScope` (enclosing class/enum — including inside static methods —,
or `null` at top-level), `__CallableThis` / `__CallableScope` (Closure is
`TCallableShape, TThis, TScope`; invokable objects; bare `callable` →
`object|null` / `object|__ClassName|null`; unions and intersections
distribute), and `__IndexKeys` / `__IndexValueType` / `__IndexValueTypes`
(struct array-key literals including `as` aliases; value lookup distributes
over a union `K`). Unbound type arguments stay as deferred wrappers until
`ExpandAfterSubstitution`. Overlay aliases `__ClosureThis` / `__ClosureScope`
are not registered here. `__CallableReturnType` /
`__CallableParametersStruct` / `__CallableParametersTuple` /
`__CallableParametersRest` rely on the
registered `Callable` constraint (`SatisfiesCallableConstraint`) for invalid
arguments — those resolvers extract shapes only and do not emit a second
`CheckerUtilityTypeInvalidArgument`. Empty `\Closure<>` does
not satisfy `Callable`. `callable(…): R` facets (`CallableCheckedType`) do.
Generic `\Closure<C, …>` satisfies `Callable` when `C`
(TCallableShape) does, or a bare `\Closure` does via `__invoke`; Closure type
arguments are not params+return. Unbound generic type parameters and unresolved
recovery types do satisfy `Callable` so `TCallable extends callable` can
appear as a type argument without a spurious TYHP4035 at the declaration.
Unions of callables and intersections that include a callable also satisfy
`Callable`, so `__CallableReturnType<(callable(): int)|(callable(): string)>` and
optional-arity intersections are not rejected before return-type extraction.
A non-callable already reported as TYHP4035 resolves to the unresolved
recovery type rather than `mixed`, so narrowing diagnostics do not pile on
top of the original failure. An unbound type parameter (`TCallable extends callable`) is different: `__CallableReturnType<TCallable>`
stays as a `GenericCheckedType` of that utility
so call-site substitution can fill `TCallable`. `TypeComparer.SubstituteType`
then calls `UtilityTypeResolver.ExpandAfterSubstitution`, which re-runs
return-type extraction (including unions of callables → union of returns)
and collapses the wrapper to the concrete return type. Two `\Closure<…>`
instantiations with different type arguments are not subtypes of each other
(generic arguments are compared before the nominal class walk), so a ternary
of closures stays a union and `__CallableReturnType` is `int|string`. A substituted
non-callable does not leak as `__CallableReturnType<int>`; it recovers to
unresolved. Bare opaque `callable` / `\Closure` reflects successfully with
zero parameters and a `mixed` return. The bare any-arity facet
`callable(...): TReturn` reflects return `TReturn` and an empty parameter
list (Tuple/Struct stay empty — no names, no arity), matching untyped
`callable` for the parameter bags.

Invoking a value typed as a callable type parameter (`TCallable $cb; $cb()`)
infers `__CallableReturnType<TCallable>` via
`UtilityTypeResolver.MakeDeferredCallableReturnType`. That wrapper compares
equal to `__CallableReturnType<TCallable>` (same type argument), so
`return $cb()` type-checks against that annotation.

`CallableSignatureReflection` is the shared helper: given a callable-ish
`ICheckedType`, it produces an ordered parameter list `{ name?, type,
optional, variadic, byRef }` plus the return type. Facet / `callable(…): R` /
`\Closure<C, …>` forms come from `CallableArityFacetBuilder` (Closure facets
from `C`, not return-last class args; `CollectCallableFacets` walks unions
and intersections so a union of closures contributes each member’s facet;
longest facet for the parameter list when optional-arity intersections are
present; return type follows the first facet, matching
`TryGetCallableReturnType` when no call arity is selected). Function, method,
and closure symbols use
`FromParameterInfos` / `FromClosureParameters` so names and by-ref / variadic
flags survive. Those names are also stored on `CallableCheckedType` (equality
ignores them). Unions of callables are merged by `TryReflect` when arities
match; `TryGetReturnType` still unions returns even when arities differ.
`__CallableReturnType` resolves through this helper (including
after generic inference). `__CallableParametersStruct<TCallable>` expands to a
synthetic struct keyed `$name` for each non-variadic named parameter; nameless
`callable(string): int` facets degrade to an empty struct (no string keys).
`__CallableParametersTuple<TCallable>` expands to a synthetic struct keyed
`$_1`, `$_2`, … with integer array-key aliases `0`, `1`, … (same shape family
as hand-written `CallableArgs*`); nameless facets still produce those int
keys. Defaulted parameters (and parameters beyond the shortest arity facet)
are `StructPropertyInfo.IsOptional` so a partial bag can omit them. Required
parameters stay required fields. This is required-key assignability on one
struct — not an intersection of every key-subset bag, and not `__Partial`
(which would make the field types nullable). Variadic parameters are omitted
from both bags; extra keys/indices stay TYHP4031, matching arity-facet policy
(unbounded extra args are not modeled on the bag). An unbound `TCallable`
stays a deferred `GenericCheckedType` until substitution, then
`ExpandAfterSubstitution` re-resolves the bag.

`__CallableParametersRest<TCallable>` is the TypeScript `...args: Parameters<T>`
analogue. It is a **pack**: on a callable shape it is an ordinary variadic
parameter (`callable(__CallableParametersRest<T> ...): R` has the same arity as
splicing T's parameters). While `TCallable` is open the splice stays deferred
(hover may still show the Rest wrapper). Value-position `Rest<T> ...$args`
unpacks at the call; that inner function **is** the spliced callable
(`function (Rest<T> ...$args): R` assigns to
`callable(__CallableParametersRest<T> ...): R` /
`\Closure<callable(__CallableParametersRest<T> ...): R>`), not a 0-arg sibling
plus a variadic Rest wrapper.
`callable(__CallableParametersRest<T> ...): __CallableReturnType<T>` reconstructs T
and assigns to the callable type parameter T (`memoize`). Extension-method
inference binds the extension receiver and skips that parameter
when matching call arguments (`compose` / `then` / `memoize()` with no extra args).
`array_map` zip Slice array arguments are checked as list arrays
(`array<int|string, P_i>`), not a raw one-arg `array<P_i>`, so a typed
`array<string>` value is not rejected as `array<int|string, string>`.

It stays a `GenericCheckedType` wrapper even after `TCallable` is
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
`run<TValue>(callable(TValue): TValue $cb, TValue $seed)` called with `1` binds
`TValue` to the literal type `1`, and feeding that back into a parameter would
demand a callback returning exactly `1`. Laziness also matters for ordering —
inferring up front would type closure arguments before the closure branch
supplies their contextual parameter types.

ExtStandard `\call_user_func` is a single Rest-unpack signature
(`TCallable $callback, Rest<TCallable> ...$args): __CallableReturnType<TCallable>`).
`\call_user_func_array` keeps two same-arity overloads (named Struct bag vs
positional Tuple bag). `FunctionOverloadSelector` refines
`SelectFunctionOverloadForCall`'s arity filter by scoring argument/parameter
compatibility — array-literal key shape (`StructBagLiteralChecker.Classify`)
picks Tuple for list / int keys and Struct for string keys. Named struct
variables (including hand-written `CallableArgs*`) are scored by materialized
shape (`HasIntegerKeyAliases`) so a `CallableArgs2` value selects Tuple rather
than the named bag. An exact integer-literal match (including a tyhpdef
`const int NAME ?? N` whose start value is `N`) scores above a plain `int`
parameter so `parse_url($u, \PHP_URL_HOST)` selects `1 $component` rather than
the `int` catch-all. Untyped `array` arguments still use
`\call_user_func_array_unsafe`. Hand-written `CallableArgs*` structs remain as
examples; the arity ladder is gone from the builtins. Named structs are
structurally assignable to matching synthetic bags (and to other named structs
with a compatible schema). Named structs and anonymous shapes inhabit the
built-in `struct` bound (`T extends struct`); that check is
`IsStructInhabitant` / `IsBuiltInName(target, "struct")`, not schema matching
against an empty `struct{}`.

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
Named generic structs are materialized through `StructTypeHelper` before that
compare, including fields inherited from `struct extends Parent<T>`. The silent
resolver used for that shape opens a single-member type expression so a generic
argument written as `T` binds to the declaring struct's parameter and is
substituted from the instantiation (`CallableArgs2<string, int>` matches the
`$_1` / `$_2` positional bag). Integer-key fields (`T 0 as $_1`) stay on those
`$_N` names with their int aliases.
A source property marked optional cannot satisfy a required target key
(runtime instances of the source may omit it). `__Partial` / `__Required`
/ `__Pick` / `__Omit` / `__Properties` materialize a property shape via
`StructTypeHelper.TryGetPropertyShape` (same path as `__AsReadOnly`), so they
apply to named struct/class declarations rather than only already-built
`StructCheckedType`s. `__Partial` sets `IsOptional` on every field (and
wraps types as nullable); `__Properties` sets `IsOptional` but keeps the
original field types; `__Required` clears it. Pick/Omit accept `'name'`
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
   user-visible `unknown` type. Assignability Unresolved ↔ T stays open.
   Member access / call / index (including `?->`, `$fn()`, `::` on a value,
   and `list()` / `[]` destructure) on an Unresolved receiver is TYHP4197 unless
   that subtree already has an error. `mixed` stays TYHP4160.
9. **SilentDiagnostics** — hierarchy probes in `TypeComparer` use a private
   silent bag so failed lookups do not spam errors.
10. **Partial classes** — large rules/comparers/inferrers split by concern;
    keep public surface on the primary file.

---

## 11. Important helpers

| Helper | When to use |
|--------|-------------|
| `CheckerHelpers.ResolveFreeFunction` | Call-site free function symbols |
| `CheckerHelpers.ResolveFreeConstant` | Call-site free constant names (`\PHP_URL_HOST`); binder often leaves `BoundSymbol` null |
| `CheckerHelpers.SelectFunctionOverloadForCall` | Arity-based tyhpdef overload pick (too-few / too-many bounds). A call that includes `...$xs` prefers a trailing-variadic signature over the primary when the known prefix fits, so `max($a, $b, ...$rest)` is not left on `max(array<T>)`. |
| `CheckerHelpers.SelectMethodOverloadForCall` | Same arity pick for `ObjectMethodSymbol.Overloads` |
| `CheckerHelpers.TryResolveInstanceOrExtensionMethod` | Own members, then in-scope extensions; canonicalizes builtin receivers before `ResolveExtensionMethod`. `staticOnly` skips instance members unless `allowInstanceForwarding` (`self`/`parent`/`static` `::` calls). Named-class `Foo::instanceMethod()` stays static-only |
| `CheckerHelpers.GetCallSiteParameters` / `ExcludeExtensionReceiver` | Skip the extension receiver (`$this` on a block-target member, or a thin-mapping `$this`) so instance-call arity does not count it |
| `FunctionOverloadSelector` | Same-arity type pick among tyhpdef overloads (Story 16.5 — Struct vs Tuple bags; Story 21.6 Phase 5 — method overloads / bind `'static'`; Story 21.8 — const-int literals vs `int`). Empty argument lists still type-score. Omitted optionals are scored as the **implementation's** default value against each candidate's parameter type, so `wordCount()` / `take($p)` select `0 $format = 0` rather than the `int` catch-all. |
| `TyhpdefConstIntLiteral` | Tyhpdef `const int NAME ?? N` start value as integer literal type |
| `ClosureBindSupport` | Story 21.6 Phase 5 — bind leftover-scope, SuperTypeName `$newScope`, non-rebindable, `call(null)` |
| `CheckerHelpers.ReportError*` / `ReportWarning` / `ReportInfo` | Diagnostics with file/line/end span from the AST node when `EndLine`/`EndColumn` are set |
| `CheckerHelpers.ReportErrorWithDidYouMean` | Unknown-name suggestions (Story 14) |
| `CheckerHelpers.IsThrowableType` / `IsBoolType` / `IsIterableType` | Control-flow / catch / condition checks |
| `CheckerHelpers.ResolveInstanceofTargetType` | instanceof / `is` RHS typing; optional diagnostics for undeclared type names (TYHP3003) and relative keywords |
| `CheckerHelpers.UsesGenericAtRuntime` | Flag Mechanism D binder / GenericObject needs |
| `CheckerHelpers.CheckCompileTimeConstructsInTree` | typeof/default, `await`, member/call/class-const, and bare `PhpNameAst` (`return FOO`) under ControlFlowRule-suppressed trees |
| `CheckerHelpers.NamesGenericParameterIn` / `SoleTypeName` | Generic runtime-use detection |
| `CheckerHelpers.IsInStaticContext` | `$this` / generic-static diagnostics |
| `CheckerHelpers.IsExtensionReceiverThis` | Allow the extension receiver `$this` in static extension methods (not TYHP4097) |
| `PropertyInitializationAnalysis.*` | Seed/record `$this` init maps |
| `CheckedTypeDisplay.FormatUnion` / `FormatNullable` | Canonical diagnostic `DisplayName` for unions / nullables (`?T` vs `…|null`) |
| `ClosureParameterInference.*` | Contextual closure params + `InferredClosureSignature` for emit |
| `PropertyPathSupport.*` | Story 16 Phase 1 — PropertyPath type detection + property-chain walk; Phase 3 `nameof(fn)` last-segment helper |
| `ExpressionTreeSupport.*` | Story 16 — Expression type detection, `TCallableShape` → callable facet, body validation (including `instanceof`/`is`), captures |
| `TypeGuardValidation.*` | `$x is T` / `$array[$key] is T` return types |
| `GenericConstraintResolver` | Before comparing constrained `T` |
| `GenericTypeArgumentValidator` | At generic instantiation sites |
| `UtilityTypeResolver` | `\Tyhp\…` and global `__` utility annotations |
| `MagicUtilityTypeResolver` | `__SuperType` / `__SuperTypeName` / `__CurrentScope` / `__CallableThis` / `__CallableScope` / `__IndexKeys` / `__IndexValueType` / `__IndexValueTypes` |
| `CallableArityFacetBuilder` | Build/select callable arity facets from parameter lists and `callable(…): R` shapes; `CollectCallableFacets` walks unions / intersections / `Closure<C, …>` |
| `CallableSignatureReflection` | Story 16.5 — ordered params + return from a callable-ish `ICheckedType` or binder `ParameterInfo` list |
| `SymbolNameTypeHelper` / `SymbolNameTypeAssignability` / `SymbolNameExistenceVerifier` | Branded name types |
| `NameofTypeInferrer` | `nameof` expression typing; in-scope generics type as plain `string` (emitter folds to the parameter spelling); `nameof(fn ($x) => $x->a->b)` types as `__PropertyName<T>` when the lambda parameter is annotated |
| `StructTypeHelper` | Struct shape helpers; substitutes `GenericCheckedType` args via `GenericInheritanceBindings` |
| `GenericInheritanceBindings` | Symbol-keyed receiver/extends generic bindings; `Iterator`/`IteratorAggregate`/`Traversable` foreach key/value and `ArrayAccess` `TKey`/`TValue` from implements (overlay `ImplementsTypes` first so remapped `Generator implements Iterator<TKey, TValue>` is visible); `ArrayAccessShape<TStruct>` via `TryGetArrayAccessShapeStruct` (not `ArrayAccess<SomeStruct>`) |
| `ArrayAccessShapeSupport` | Call-site wide keys (TYHP4331) / no-append (TYHP4332); per-key `offsetGet`/`offsetSet` exhaustiveness (TYHP4333). Finite literal keys instantiate the body once per inhabitant; `throw`/`never` do not cover. Infinite constraints (`string` / unbound `TStruct`) keep one envelope body check |
| `ArrayAccessDestructureSupport` | Story 21.7 — `list()` / `[]` destructure on array, string, or ArrayAccess. Positional keys `0, 1, …` (skipped slots are not reads); named keys as written. Bound variables get `InferIndexValue` (`TValue` / shape per-key). TYHP4094 when the source is none of those; TYHP4095 for spread |
| `ArrayAppendInference` | Open `array` locals after `$arr[] = $v` (implicit `int` key) or `$arr[$k] = $v` (real key, only when it resolves to plain `int`/`string`): infer `array<K, …>` so `implode` / `array_reverse` see resolved keys. Annotated `array<K, V>` is unchanged |
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
   not from `ObjectPropertySymbol`. Authored `&get` (by-ref get) in **Tyhp source** is rejected with
   TYHP4167 when `CheckerOptions.PhpVersion` is below 8.4 — the polyfill path
   cannot preserve by-ref semantics through magic `__get`, and silent by-value
   lowering would change aliasing. Native `&get` is emitted only for PHP ≥ 8.4.
   Tyhpdef hooked properties skip TYHP4167: a tyhpdef describes an existing API
   rather than emitting toward `output.phpVersion`. Gate the symbol with
   `#[\Tyhp\Php]` / `declare(php=…)` when the PHP API is 8.4-only. Hook
   visibility (`public` / `protected` / `private`) and `final` are legal;
   a hook must not be more visible than its property (TYHP4004). Unknown hook
   names and `&set` are TYHP4006; duplicate `get`/`get` uses the existing
   duplicate-symbol diagnostic. Tyhpdef types are `TyhpdefImportObjectDeclAst`
   (not `PhpObjectTypeDeclAst`); `DeclarationRule` handles that shell via
   `CheckTyhpdefImportObject`, which seeds `EnclosingObject` / `EnclosingObjectType`
   before walking members the same way `CheckObjectType` does for `.tyhp` classes.
   Bodyless tyhpdef method signatures skip the missing-return rule. Final-hook
   override (TYHP4166) can still recover the owner from the property's bound
   symbol when `EnclosingObject` is unset.

3. **Extension `$this` is the block receiver, not instance `$this`** —
   `ExtensionRule` suppresses child traversal and checks bodies under
   `StaticMethodDeclaration` (extensions emit as static methods). The target
   is the header `extends Type` or the nested `extends` group that contains
   the member. The rule seeds `EnclosingObject` (the extension, so
   `IsExtension` is visible), `EnclosingObjectType` (the block target, so
   `self` means that type), `ObjectGenerics` (extension and group binders,
   group names first), and the synthesized `$this` parameter. `self` in an
   anonymous class nested in the member stays that class — the binder stops
   at the nearest object scope; the checker does not flag `static::` /
   `parent::` inside that class either.
   `CheckerHelpers.IsExtensionReceiverThis` exempts the receiver from
   TYHP4097 (`CheckerThisInStaticContext`). Real static methods and static
   closures still reject `$this`. At call sites, `ExcludeExtensionReceiver`
   walks parents until it finds an `IsExtension` object (so a method whose
   `ContainingScope` is the static method, not the extension object, still
   skips `$this`) and also skips a first parameter named `$this` / `this`.
   Operators take the same seeding path via `CheckExtensionOperatorOverload`,
   including a standalone `.tyhpdef` extension operator. `CheckNode` runs
   `OperatorOverloadRule` and the splice-member check against that seeded
   state, so `self` in the thin signature means the block target.
   A non-instantiable operator target (`void`, `never`, `null`, `mixed`,
   `resource`, `true`, `false`) is TYHP3025 from the binder and is not seeded
   as `EnclosingObjectType`. Target rules run on the alias-expanded type. A
   single class, interface, enum, builtin, or generic application is valid,
   including the shapes `object`, `callable`, and `struct` (`$this` has that
   declared type). `?T` is valid: it applies to a receiver of type `?T` and of
   type `T`, and `$this` is `?T`, so a use as `T` needs a null check (`!== null`
   narrows it to `T`). An alias of `?T` or of one of those single types is
   valid. A union target is valid, including an alias whose body is that union,
   **provided every member of the union is itself a valid single target** —
   `IsRejectedExtensionTarget` checks each `UnionCheckedType` member
   recursively, so an intersection, `void`, an `object { … }` shape, or a
   type parameter reached through one union arm (directly, or through a type
   alias used as that arm) is still TYHP4364, not just one written as the
   whole target. A legal union applies only when every type the receiver
   might be is a member of the union: `string|int` applies to `string`,
   `int`, and `string|int`, and does not apply to `?string` or
   `string|int|bool`. A union that includes `null` applies to every
   non-empty combination of its members (nullable pairs such as `?string`,
   and narrower unions such as `null|string|int` and `string|int`) except a
   receiver that is only `null`. `$this` in the member is that union, not a
   narrowed member. Call-site method lookup is
   `CheckerHelpers.TryResolveInstanceOrExtensionMethod`. When the method name
   has any union-target candidate, it still tries the ordinary
   `NameResolver.ResolveExtensionMethod` path first (so aliasing, `hide` /
   `insteadof`, tyhpdef auto-activation, and precedence ranking keep working
   for every other candidate of that name) and only re-checks or replaces
   that pick with a full union-aware scan
   (`ExtensionBlockTargetChecks.UnionTargetCoversReceiver`) when the ordinary
   result is missing or its target does not really cover the original
   receiver once nullability and the union's other members are considered —
   the ordinary path matches only the union's first member, so a receiver
   that is a later member, or nullable when the union has no `null` member,
   needs that full scan. `SymbolTree.ExtensionMethodIndex` (and so
   `HasUnionTargetMethod`) is keyed by the method's *declared* name, so a
   call site that only knows a `use extension { Ext::real as alias; }`
   rename resolves the alias back to that declared name first
   (`CheckerHelpers.ResolveDeclaredExtensionMethodName`, mirroring
   `NameResolver`'s own receiver/file/global alias precedence over the same
   public dictionaries) before deciding whether the call needs the
   union-aware path at all — otherwise a non-first-member receiver calling
   through the alias would silently fail to resolve.
   An intersection, `void`, `never`, `mixed`, `_`, a
   type parameter, or an `object { … }` shape is TYHP4364. Two groups in the
   same extension that can both match one receiver and declare the same method
   or operator are TYHP4367; separate extensions are not compared.
   Convert-from forms in that same extension (an `operator convert` whose operand
   is a source type, not `self` or `static`) share one `__from`. Overlapping source
   types — the same operand-atom rule as class-owned operator forms, including a
   shared union member or `mixed` — are TYHP4074, including when the forms sit on
   different nested targets. Distinct source types stay one legal `__from` whose
   parameter is their union. Convert-to forms on different targets stay legal:
   each group's `self` is that target, and the generated `__to…` method dispatches
   on it.
   `extends string` and `extends string|int` both apply to `string`, so the
   same member on both is TYHP4367. `extends string` and
   `extends ?string` both apply to `string`. `extends object` overlaps every
   class, interface, and enum target in that extension, and overlaps
   `extends callable` because both apply to `\Closure`. `extends callable`
   overlaps other callable-shaped targets in that extension (`string`, `array`,
   `\Closure`, and callable shapes) for the same member. An unused binder `T`
   is TYHP4365. A method generic that reuses an in-scope binder name is
   TYHP4366. Writing `$this` without `&$this` is TYHP4369; `&$this` with no
   write is TYHP4370. Calling a method on an object `$this`, or assigning
   through one of its properties (`$this->prop = …`), is not a write — the
   handle is shared, so the mutation is visible to the caller without `&`.
   For a scalar / array / struct target (`CheckerHelpers.IsClosedMemberReceiver`
   on the resolved `EnclosingObjectType`), assigning through a `$this`-rooted
   member or array-access chain (`$this->field = …`, `$this[$k] = …`,
   `$this[] = …`) **is** a write — those targets are by-value PHP parameters,
   so the mutation only reaches the caller through `&$this`, the same as
   reassigning `$this` itself.

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

11. **Template-string step limit is per thread** — a checker stores
    `TemplateStringMaxStates` on the thread that constructs it. Nested
    assignability checks on that thread share one exceeded flag; exhaustion
    reports `CheckerTemplateStringMaxStatesExceeded` instead of a generic
    mismatch.

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
  them. `TypeCompatibilityRule.CheckCall` (and `TypeInferrer.InferCall`) write
  `BoundSymbol` on the callee name to the selected overload so `DeprecationRule`
  (later on the same dereferenceable) reports deprecation on that signature, not
  the primary.
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
  `IClassName` clauses. `CheckTraversableListing` (TYHP4326) then rejects a
  user `.tyhp` type that lists `\Traversable` in those clauses. Only the
  engine interfaces `\Iterator` and `\IteratorAggregate` may extend
  Traversable. `CheckEngineEnumListing` (TYHP4337) is the same written-clause
  walk for `\UnitEnum` / `\BackedEnum`: user classes, interfaces, and traits
  must not list them; user enums must not list them either (already implied).
  Only the engine `\BackedEnum` may extend `\UnitEnum`.
  `CheckEnumEngineMethodRedeclare` (TYHP4338) rejects a user enum method
  named `cases`, `from`, or `tryFrom`. Tyhpdef import shells
  (`TyhpdefImportObjectDeclAst`) never run the listing checks, and harvest
  method signatures are skipped by AST / `LanguageMode`, so harvested
  `implements \UnitEnum` / `\BackedEnum` and `cases` / `from` / `tryFrom`
  stay quiet; `#[\Tyhp\Php]` is an ordinary version gate on real `.tyhp`
  declarations and does not exempt them (harvest output is always
  `.tyhpdef`, never a `PhpObjectTypeDeclAst`).

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
| Inference | `TypeInferrerTests.cs`, `CallReturnTypeInferenceTests.cs`, `TrueFalseLiteralTypeTests.cs`, `ArrayLiteralInferenceTests.cs`, `ArrayAppendInferenceTests.cs`, `YieldTypeInferenceTests.cs` |
| Narrowing / guards | `TypeGuardRuleTests.cs`, `PropertyNarrowingRuleTests.cs`, `StaticPropertyNarrowingTests.cs`, `CompoundAssignmentNarrowingTests.cs` |
| Definite assignment / props | `DefiniteAssignmentRuleTests.cs`, `PropertyInitializationRuleTests.cs`, `UnsetTrackingRuleTests.cs` |
| Calls / members | `CallArgumentValidationTests.cs`, `MemberDispatchRuleTests.cs`, `UnresolvedFunctionCallTests.cs` (TYHP4182), `UnresolvedReceiverTests.cs` (TYHP4197), `MixedUseSiteRuleTests.cs` (TYHP4160), `NewTypeAliasCheckerTests.cs` (`new` on a type alias: TYHP4069 / TYHP4347) |
| Generics | `GenericInheritanceSubstitutionTests.cs`, `DefaultAndGenericTrackingRuleTests.cs` |
| Declarations / OOP | `MethodOverrideRuleTests.cs`, `OverrideGenericSignatureTests.cs`, `InterfaceImplementationRuleTests.cs`, `InheritanceTargetRuleTests.cs`, … |
| Attributes | `AttributeRuleTests.cs`, `ConstAttributeTargetCheckTests.cs`, `PhpTypeAttributeTests.cs` (TYHP4327 / 4329), `NativeTypeTestRuleTests.cs` (TYHP4340–4344), `DeprecationRuleTests.cs` (`#[\Deprecated]` / TYHP4500) |
| PHP version gates | `PhpVersionRuleTests.cs` (Story 20.5 4300–4306); `TyhpdefPropertyHookRuleTests.cs` (8016 hook-level gate) |
| Features | `Async`/`Overload`/`OperatorOverload`/`Phase*` suites; `ClosureInferenceTests.cs` (Story 21.6 Phase 4 producers); `ClosureThisBindingTests.cs`; `MagicUtilityTypeTests.cs` |
| Call-site splice | `InlineSpliceCheckerTests.cs` (4174–4177, 4179–4181); emit coverage in `CallSiteSpliceEmitterTests.cs` |

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
