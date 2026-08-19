# Implementation Plan: Story 28 — Generic Type Parameter Defaults

> **Roadmap position:** Story 28 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** all earlier stories (01–27)
> **Renumbered from:** legacy Story 21
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** Tyhp language design — generic type parameter default values
> **Branch:** TBD
> **Generated:** 2026-02-17
> **Prerequisites:** All earlier stories (01–27) must be complete — the parser, binder, checker, emitter, tyhpdef generator, Tyhp runtime packages, LSP, and testing infrastructure must be fully functional.

---

## Architecture Overview

### What Generic Type Parameter Defaults Are

Generic type parameter defaults allow a generic parameter to specify a fallback type that is used when the consumer does not explicitly provide a type argument. This enables cleaner APIs where the common case requires no explicit parameterization.

```tyhp
<?tyhp

// Promise defaults TReturn to void — the common case for async procedures
class Promise<TReturn extends void|mixed = void> {
    // ...
}

// These are equivalent:
$p1 = new Promise();        // TReturn = void (from default)
$p2 = new Promise<void>();  // TReturn = void (explicit)
$p3 = new Promise<int>();   // TReturn = int  (explicit override)
```

### Syntax

The default type is specified with `=` after the optional constraint:

```
T                                    // No constraint, no default
T extends SomeType                   // Constraint, no default
T = DefaultType                      // No constraint, with default
T extends SomeType = DefaultType     // Constraint AND default
```

The default type must satisfy the constraint (if present). For example, `T extends Countable = string` is invalid because `string` does not implement `Countable`.

### Defaulted Parameters Must Be Trailing

Like function parameter defaults, generic type parameter defaults must be trailing. A non-defaulted parameter cannot follow a defaulted one:

```tyhp
// Valid:
class A<T, U = int> {}              // T required, U optional
class B<T, U = int, V = string> {}  // T required, U and V optional
class C<T = mixed> {}               // T optional

// Invalid:
class D<T = int, U> {}              // ERROR: non-defaulted U after defaulted T
```

### Partial Type Argument Application

When a generic type has defaults, type arguments are applied left-to-right. Omitted trailing arguments use their defaults:

```tyhp
class MyMap<TKey = string, TValue = mixed> {}

$a = new MyMap();                 // TKey = string, TValue = mixed
$b = new MyMap<int>();            // TKey = int, TValue = mixed
$c = new MyMap<int, User>();      // TKey = int, TValue = User
```

### Applies To All Generic Declarations

Generic defaults are supported on:
- Classes, interfaces, traits, enums
- Type aliases
- Functions and methods

```tyhp
// Type alias with default
type Collection<T = mixed> = array<T>;

// Function with default
function wrap<T = string>(T $value): array<T> {
    return [$value];
}

// Interface with default
interface Repository<TEntity, TId = int> {
    public function find(TId $id): ?TEntity;
}
```

### Type Inference Takes Priority Over Defaults

When a generic function/method is called, **type inference from arguments always takes priority** over defaults. Defaults are only used when a type parameter cannot be inferred and is not explicitly provided:

```tyhp
function wrap<T = string>(T $value): array<T> {
    return [$value];
}

wrap(42);        // T = int (inferred from argument, not string)
wrap("hello");   // T = string (inferred, coincidentally matches default)
wrap<bool>(true); // T = bool (explicit)
```

Defaults primarily benefit class-level generics where there are no constructor arguments to infer from, or when a type parameter is not used in any parameter position.

### Position in the Pipeline

```
ALL Stories 01–27 Complete
    │
    ▼
┌──────────────────────────────────────────────────────────────┐
│  STORY 28: Generic Type Parameter Defaults                    │
│  ◄── THIS PLAN                                               │
│                                                              │
│  Touches: Grammar (Parser), AST, Binder, Checker,           │
│           Tyhpdef Generator, LSP, Testing                    │
│                                                              │
│  Phase 1: Grammar — Add `= typeExpr` to generic param rule   │
│  Phase 2: AST — Add DefaultType to type argument AST node    │
│  Phase 3: Binder — Store default on GenericTypeParameterSymbol│
│  Phase 4: Checker — Validate and apply defaults               │
│  Phase 5: Tyhpdef — Preserve defaults in generated tyhpdef    │
│  Phase 6: LSP — Show defaults in hover/completion             │
│  Phase 7: Testing — Comprehensive test coverage               │
└──────────────────────────────────────────────────────────────┘
```

### Design Principles

1. **Defaults must satisfy constraints.** If a parameter has `T extends Foo = Bar`, the checker must verify that `Bar` satisfies the `extends Foo` constraint. This is a compile-time error, not a warning.

2. **Defaulted parameters must be trailing.** A non-defaulted parameter after a defaulted parameter is a compile-time error. This mirrors PHP function parameter rules and prevents ambiguity in partial application.

3. **Type inference takes priority.** When calling a generic function/method, inferred types from arguments always take priority over defaults. Defaults are a fallback for parameters that cannot be inferred.

4. **Defaults are preserved in tyhpdef.** When generating tyhpdef files for a library, generic parameter defaults are included so that consumers of the library get the same defaulting behavior.

5. **Defaults have no additional runtime impact.** Generic default resolution is purely a compile-time feature. Note that generics themselves are NOT fully erased — generic classes use the `GenericObject` trait with hidden `$__generic_*` constructor parameters for runtime type tracking (only when the checker flags the class as `RequiresRuntimeGenericTracking` — e.g., when `typeof(T)` is used; see Story 11, Phase 8). However, *defaults* are resolved at compile time before emission, so the emitter simply emits the resolved concrete type as the `__generic_*` named argument at the call site, the same as if the caller had specified the type explicitly.

6. **Defaults can reference earlier type parameters.** A default can reference type parameters declared earlier in the same parameter list: `class Pair<T, U = T>` is valid and means `Pair<int>` is equivalent to `Pair<int, int>`.

### MessageCode Numbering

This story uses MessageCode values in the **4300–4399 feature-checker band** (`4310–4312`), relocated out of the 4000–4211 range now owned contiguously by Story 08. Story 27's `new<>` codes occupy `4300–4303` in the same band.

| Code | Enum Name | Message |
|------|-----------|---------|
| 4310 | `CheckerGenericDefaultDoesNotSatisfyConstraint` | "Default type '{0}' does not satisfy constraint '{1}' on generic parameter '{2}'" |
| 4311 | `CheckerGenericNonDefaultAfterDefault` | "Generic parameter '{0}' without a default cannot follow parameter '{1}' which has a default" |
| 4312 | `CheckerGenericDefaultCircularReference` | "Generic parameter '{0}' has a circular default type reference" |

> **Note:** All three codes sit in the `4300–4399` feature-checker band (`4310`, `4311`, `4312`), keeping them clear of Story 08's contiguous `4008–4211` allocation and of Story 25's internal-visibility codes (`4330–4334`).

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup: `<filename>.<YYYYMMDD_HHMMSS>.backup`
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: Grammar — Add Default Type to Generic Parameter Rule




### Phase Overview

Extend the `tyhpGenericsTypeArgument` parser rule to accept an optional `= typeExpr` after the optional constraint. This allows the grammar to parse declarations like `<T extends Foo = Bar>` and `<T = string>`.

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Grammar/TyhpParser.g4` — Update `tyhpGenericsTypeArgument` rule

**Regenerated files:**
- All ANTLR-generated parser files (via `compile_grammar.sh`)

### Implementation Details

#### 1.1 Update `tyhpGenericsTypeArgument` Rule

**File: `Tyhp/TyhpLang/Grammar/TyhpParser.g4`**

Change the `tyhpGenericsTypeArgument` rule from:

```
tyhpGenericsTypeArgument
    : Identifier=name (T_EXTENDS TypeExpr=typeExpr)?
    ;
```

To:

```
tyhpGenericsTypeArgument
    : Identifier=name (T_EXTENDS ConstraintExpr=typeExpr)? (T_SYM_EQUAL DefaultExpr=typeExpr)?
    ;
```

Key points:
- `T_SYM_EQUAL` is the existing `=` token in the lexer (already used for assignments, default parameter values, etc.)
- The constraint (`T_EXTENDS`) and default (`T_SYM_EQUAL`) are both optional and independent
- The default comes after the constraint, matching the reading order: `T extends Foo = Bar` reads as "T extends Foo, defaults to Bar"
- ANTLR handles the `typeExpr` ambiguity via labeled alternatives (`ConstraintExpr` vs `DefaultExpr`)

#### 1.2 Verify No Ambiguity with Existing `=` Usage

The `=` token inside `<...>` angle brackets is unambiguous because:
- Inside a generic parameter list, `=` cannot mean assignment (there are no variables)
- The `typeExpr` rule is well-defined and terminates naturally before `,` or `>`
- This is the same pattern used by TypeScript (`T = DefaultType`), C# (no equivalent), and Kotlin (`T = DefaultType`)

#### 1.3 Regenerate Parser

After modifying the grammar files:

```bash
./compile_grammar.sh
dotnet clean && dotnet restore && dotnet build
```

Requires `antlr-ng` (`npm install -g antlr-ng`). Grammar regeneration is **not** triggered by `dotnet build` alone.

### Acceptance Criteria

- [ ] `<T>` parses successfully (no constraint, no default)
- [ ] `<T extends Foo>` parses successfully (constraint, no default)
- [ ] `<T = Bar>` parses successfully (no constraint, with default)
- [ ] `<T extends Foo = Bar>` parses successfully (constraint and default)
- [ ] `<T, U = int>` parses successfully (mixed required and defaulted params)
- [ ] `<T extends Foo = Bar, U extends Baz = Qux>` parses successfully (multiple constrained+defaulted params)
- [ ] The `ConstraintExpr` and `DefaultExpr` labels are accessible on the parse tree context

---

## Phase 2: AST — Add Default Type to Generic Type Argument AST Node




### Phase Overview

Extend `TyhpGenericsTypeArgumentAst` to carry an optional default type expression, and update the AST visitor to populate it from the parse tree.

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Ast/TyhpGenericsTypeArgumentAst.cs` — Add `DefaultType` property
- `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpGenerics.cs` — Pass default type to AST factory

### Implementation Details

#### 2.1 Update `TyhpGenericsTypeArgumentAst`

**File: `Tyhp/TyhpLang/Ast/TyhpGenericsTypeArgumentAst.cs`**

Add a third child slot for the default type:

```csharp
public class TyhpGenericsTypeArgumentAst : Base2Ast
{
    public PhpNameAst? Name => Children.ElementAtOrDefault(0) as PhpNameAst;
    public ITypeExpression? TypeConstraint => Children.ElementAtOrDefault(1) as ITypeExpression;
    public ITypeExpression? DefaultType => Children.ElementAtOrDefault(2) as ITypeExpression;

    public static TyhpGenericsTypeArgumentAst Create(
        PhpNameAst name,
        ITypeExpression? typeConstraint,
        ITypeExpression? defaultType,
        ParserRuleContext context,
        string? languageMode = null)
    {
        var result = new TyhpGenericsTypeArgumentAst
        {
            Identifier = name?.ValueString ?? "",
            Children = [name, typeConstraint, defaultType],
        };
        result.SetContext(context, languageMode);
        return result;
    }
}
```

The existing two-argument `Create` overload should be updated to the three-argument version. All existing call sites pass `null` for `defaultType` until they are updated.

#### 2.2 Update AST Visitor

**File: `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpGenerics.cs`**

Update `VisitTyhpGenericsTypeArgument` to extract the default type from the parse tree:

```csharp
public override TyhpGenericsTypeArgumentAst VisitTyhpGenericsTypeArgument(
    [NotNull] TyhpParser.TyhpGenericsTypeArgumentContext context)
{
    var name = PhpNameAst.Create(context.Identifier, context);
    var typeConstraint = context.ConstraintExpr != null
        ? this.VisitTypeExpr(context.ConstraintExpr)
        : null;
    var defaultType = context.DefaultExpr != null
        ? this.VisitTypeExpr(context.DefaultExpr)
        : null;
    return TyhpGenericsTypeArgumentAst.Create(name, typeConstraint, defaultType, context);
}
```

Note the label changes: `context.TypeExpr` becomes `context.ConstraintExpr` (matching the renamed grammar label from Phase 1).

**Important (BREAKING — high blast radius):** The grammar label rename from `TypeExpr` to `ConstraintExpr` is a breaking change to the generated parser API. It affects ALL code that references the `TypeExpr` property on `TyhpGenericsTypeArgumentContext`, and the generated context property only changes name after the parser is regenerated (Phase 1.3). This requires a **repo-wide update in the same change**: before/with the visitor change above, search the entire codebase for references to `TypeExpr` on `tyhpGenericsTypeArgument`-related contexts (including visitor files, binder files, and any test helpers) and update all occurrences to `ConstraintExpr`. The code will not compile until every reference is migrated, so treat this as an atomic rename + regeneration step.

### Acceptance Criteria

- [ ] `TyhpGenericsTypeArgumentAst.DefaultType` returns `null` when no default is specified
- [ ] `TyhpGenericsTypeArgumentAst.DefaultType` returns the correct `ITypeExpression` when a default is specified
- [ ] `TyhpGenericsTypeArgumentAst.TypeConstraint` continues to work correctly (renamed label in grammar)
- [ ] Existing code that creates `TyhpGenericsTypeArgumentAst` continues to compile (updated call sites)
- [ ] The AST round-trips correctly: parse → AST → verify DefaultType is populated

---

## Phase 3: Binder — Store Default on `GenericTypeParameterSymbol`




### Phase Overview

Extend `GenericTypeParameterSymbol` to carry an optional default type, and populate it during the binding walk.

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Binder/Symbols/GenericTypeParameterSymbol.cs` — Add `DefaultType` property
- Binder walk files that create `GenericTypeParameterSymbol` instances — pass default type

### Implementation Details

#### 3.1 Verify `GenericTypeParameterSymbol` Properties

**File: `Tyhp/TyhpLang/Binder/Symbols/GenericTypeParameterSymbol.cs`**

The `DefaultType` **property declaration** already exists on `GenericTypeParameterSymbol` (the field is present in code). **However, it is currently never populated** — the grammar, AST, and binder walk do not yet read or set a default type. This story wires up the *population* of that property end-to-end (grammar in Phase 1, AST in Phase 2, binder in §3.2). Treat the property as a pre-existing slot that is dead/unset until this story; do NOT read this as "defaults are already implemented."

Verify that the following properties are present:

```csharp
public class GenericTypeParameterSymbol : BaseSymbol, ...
{
    // Already present from Story 02:
    public ITypeExpression? Constraint { get; internal set; }
    public TypeVariance Variance { get; internal set; }

    // Property slot present in code, but NOT populated until this story:
    public ITypeExpression? DefaultType { get; internal set; }
}
```

If not already present, add the following convenience property:

```csharp
    /// <summary>
    /// Whether this generic parameter has a default type.
    /// Convenience property — equivalent to `DefaultType != null`.
    /// </summary>
    public bool HasDefault => DefaultType != null;
```

The focus of this phase is ensuring the binding walk correctly **populates** `DefaultType` from the AST (Phase 2's output), not adding the property itself.

#### 3.2 Populate Default During Binding

In the binder walk, wherever `GenericTypeParameterSymbol` instances are created from `TyhpGenericsTypeArgumentAst` nodes, also read `DefaultType`:

```csharp
// Pseudocode — in the binder walk for generic parameter lists
foreach (var genericParamAst in genericParamListAst.Items)
{
    var symbol = new GenericTypeParameterSymbol
    {
        Name = genericParamAst.Identifier,
        Constraint = genericParamAst.TypeConstraint,
        DefaultType = genericParamAst.DefaultType,  // NEW
        // ... other properties
    };
    // Add to scope
}
```

The binder does NOT validate that the default satisfies the constraint — that is the checker's responsibility (Phase 4).

#### 3.3 Ordering Validation Is Deferred

The binder does NOT validate that defaulted parameters are trailing. This validation is performed by the checker (Phase 4) so that a proper diagnostic can be emitted with file/line/column information.

### Acceptance Criteria

- [ ] `GenericTypeParameterSymbol.DefaultType` is `null` for parameters without defaults
- [ ] `GenericTypeParameterSymbol.DefaultType` is populated for parameters with defaults
- [ ] `GenericTypeParameterSymbol.HasDefault` returns `true` when a default is present
- [ ] The binder correctly binds `<T extends Foo = Bar>` with both constraint and default
- [ ] The binder correctly binds `<T = string>` with default but no constraint

---

## Phase 4: Checker — Validate and Apply Defaults




### Phase Overview

The checker is responsible for three tasks related to generic defaults:

1. **Validate declarations** — Ensure defaulted parameters are trailing and defaults satisfy constraints
2. **Apply defaults at usage sites** — When fewer type arguments are provided than declared parameters, fill in defaults from right to left
3. **Integrate with type inference** — When calling generic functions/methods, inferred types take priority over defaults

### Deliverables

**Modified files:**
- `Tyhp/TyhpLang/Checker/TyhpChecker.TyhpFeatures.cs` (or relevant checker file) — Add validation and default application logic (ordering, constraint satisfaction, circular-reference detection)
- `Tyhp/Domain/Exceptions/MessageCode.cs` — Add error codes 4310–4312
- `Tyhp/Resources/CLI.TyhpHostedService.resx` — Add localized error strings for 4310, 4311, 4312

### Implementation Details

#### 4.1 Validate Generic Parameter Ordering

When the checker processes a generic declaration (class, function, type alias, etc.), it must verify that no non-defaulted parameter follows a defaulted parameter:

```
function ValidateGenericParameterDefaults(
    IReadOnlyList<GenericTypeParameterSymbol> params,
    IBase2Ast declarationNode,
    string fileName
):
    seenDefault = false
    lastDefaultedName = ""

    for each param in params:
        if param.HasDefault:
            seenDefault = true
            lastDefaultedName = param.Name
        else if seenDefault:
            diagnostics.AddError(
                MessageCode.CheckerGenericNonDefaultAfterDefault, fileName,
                param.Line, param.Column,
                param.Name, lastDefaultedName
            )
```

#### 4.2 Validate Default Types Satisfy Constraints

When a generic parameter has both a constraint and a default, the checker must verify that the default type satisfies the constraint:

```
function ValidateGenericDefaultConstraints(
    IReadOnlyList<GenericTypeParameterSymbol> params,
    IBase2Ast declarationNode,
    string fileName
):
    for each param in params:
        if param.HasDefault and param.Constraint != null:
            defaultCheckedType = resolveType(param.DefaultType)
            constraintCheckedType = resolveType(param.Constraint)

            if not isAssignableTo(defaultCheckedType, constraintCheckedType):
                diagnostics.AddError(
                    MessageCode.CheckerGenericDefaultDoesNotSatisfyConstraint,
                    fileName, param.Line, param.Column,
                    defaultCheckedType.DisplayName,
                    constraintCheckedType.DisplayName,
                    param.Name
                )
```

**Special consideration for forward references:** A default type can reference earlier type parameters in the same list. When validating `class Pair<T, U = T>`, the checker must resolve `T` in the context of the parameter list. Since `T` has no concrete type at declaration time, the constraint check should verify structural compatibility: `U = T` is valid when `U` has no constraint (since `T` satisfies `mixed`), and `U extends Foo = T` is valid only if `T extends Foo` is also declared.

#### 4.2a Detect Circular Default References

Because a default may reference earlier type parameters, defaults can form a reference cycle (directly, e.g. `<T = T>`, or transitively through other parameters' defaults). The checker must detect such cycles and emit `CheckerGenericDefaultCircularReference` (4312) rather than recursing indefinitely:

```
function ValidateGenericDefaultCircularReferences(
    IReadOnlyList<GenericTypeParameterSymbol> params,
    IBase2Ast declarationNode,
    string fileName
):
    // Build a directed graph: param -> the type parameters referenced by its default
    for each param in params where param.HasDefault:
        visited = empty set
        if defaultReferencesCycle(param, params, visited):
            diagnostics.AddError(
                MessageCode.CheckerGenericDefaultCircularReference, fileName,
                param.Line, param.Column,
                param.Name
            )

// defaultReferencesCycle walks the references in a parameter's default type
// (and the defaults of any parameters it references) detecting a return to `param`.
function defaultReferencesCycle(param, params, visited) -> bool:
    if param.Name in visited:
        return true          // cycle detected
    if not param.HasDefault:
        return false
    visited.Add(param.Name)
    for each referencedName in typeParametersReferencedBy(param.DefaultType):
        referenced = params.firstWhere(p => p.Name == referencedName)
        if referenced != null and defaultReferencesCycle(referenced, params, visited):
            return true
    visited.Remove(param.Name)
    return false
```

This runs alongside (and before) the constraint-satisfaction check in §4.2 — a default that participates in a cycle cannot be meaningfully resolved, so cycle detection short-circuits further validation/resolution of that parameter. The direct self-reference case (`<T = T>`) is the simplest instance and must be reported as 4312.

#### 4.3 Apply Defaults at Type Instantiation Sites

When a generic type is instantiated with fewer type arguments than declared parameters, the checker fills in defaults for the missing trailing parameters.

This integrates with the existing logic in **Story 08, Phase 5.5** ("Generic Type Parameter Constraint Checking"):

```
function ResolveGenericTypeArguments(
    IReadOnlyList<GenericTypeParameterSymbol> declaredParams,
    IReadOnlyList<ICheckedType> providedArgs,
    IBase2Ast usageNode,
    string fileName
) -> IReadOnlyList<ICheckedType>:

    // Count required (non-defaulted) parameters
    requiredCount = count of params where not HasDefault
    totalCount = declaredParams.Count

    if providedArgs.Count < requiredCount:
        diagnostics.AddError(
            MessageCode.CheckerGenericArgumentCountMismatch,
            fileName, usageNode.Line, usageNode.Column,
            totalCount, providedArgs.Count
        )
        return empty

    if providedArgs.Count > totalCount:
        diagnostics.AddError(
            MessageCode.CheckerGenericArgumentCountMismatch,
            fileName, usageNode.Line, usageNode.Column,
            totalCount, providedArgs.Count
        )
        return empty

    // Build the full argument list, filling defaults for missing args
    resolvedArgs = new List<ICheckedType>()

    for i = 0 to totalCount - 1:
        if i < providedArgs.Count:
            resolvedArgs.Add(providedArgs[i])
        else:
            // Apply default — resolve the default type expression
            // Note: defaults may reference earlier params, so substitute
            // already-resolved args when resolving the default
            defaultType = resolveTypeWithSubstitutions(
                declaredParams[i].DefaultType,
                declaredParams[0..i],
                resolvedArgs[0..i]
            )
            resolvedArgs.Add(defaultType)

    // Validate all resolved args against constraints (existing logic)
    for i = 0 to totalCount - 1:
        validateConstraint(declaredParams[i], resolvedArgs[i], usageNode, fileName)

    return resolvedArgs
```

#### 4.4 Integration with Type Inference for Functions/Methods

When calling a generic function or method, the resolution order is:

1. **Explicit type arguments** — provided by the caller (e.g., `foo<int>()`)
2. **Type inference** — inferred from argument types at the call site
3. **Defaults** — used for parameters that are neither explicitly provided nor inferable

This means defaults serve as a last resort. The existing inference logic from Story 08, Phase 5.6 should be extended:

```
function ResolveGenericFunctionCall(
    FunctionDeclarationSymbol func,
    IReadOnlyList<ICheckedType>? explicitTypeArgs,
    IReadOnlyList<ICheckedType> argumentTypes,
    IBase2Ast callNode,
    string fileName
) -> IReadOnlyList<ICheckedType>:

    declaredParams = func.GenericParameters
    resolvedArgs = new ICheckedType?[declaredParams.Count]

    // Step 1: Apply explicit type arguments (left to right)
    if explicitTypeArgs != null:
        for i = 0 to explicitTypeArgs.Count - 1:
            resolvedArgs[i] = explicitTypeArgs[i]

    // Step 2: Infer remaining from call-site arguments
    for each unresolved param at index i:
        inferred = inferTypeFromArguments(declaredParams[i], func.Parameters, argumentTypes)
        if inferred != null:
            resolvedArgs[i] = inferred

    // Step 3: Apply defaults for still-unresolved parameters
    for each unresolved param at index i:
        if declaredParams[i].HasDefault:
            resolvedArgs[i] = resolveTypeWithSubstitutions(
                declaredParams[i].DefaultType,
                declaredParams[0..i],
                resolvedArgs[0..i]
            )
        else:
            diagnostics.AddError(
                MessageCode.CheckerGenericArgumentCountMismatch, ...
            )

    return resolvedArgs
```

#### 4.5 Add MessageCodes

**File: `Tyhp/Domain/Exceptions/MessageCode.cs`**

```csharp
// Generic type parameter defaults (4310-4312) — Story 28
CheckerGenericDefaultDoesNotSatisfyConstraint = 4310,  // "Default type '{0}' does not satisfy constraint '{1}' on generic parameter '{2}'"
CheckerGenericNonDefaultAfterDefault = 4311,           // "Generic parameter '{0}' without a default cannot follow parameter '{1}' which has a default"
CheckerGenericDefaultCircularReference = 4312,         // "Generic parameter '{0}' has a circular default type reference"
```

**File: `Tyhp/Resources/CLI.TyhpHostedService.resx`**

Add corresponding localized strings for all three error codes (4310, 4311, 4312).

### Acceptance Criteria

- [ ] `<T = int, U>` produces `CheckerGenericNonDefaultAfterDefault` (4311) error
- [ ] `<T extends Countable = string>` produces `CheckerGenericDefaultDoesNotSatisfyConstraint` (4310) error
- [ ] `<T extends Serializable = JsonSerializable>` validates successfully (when JsonSerializable implements Serializable)
- [ ] `new Promise()` resolves to `Promise<void>` (default applied)
- [ ] `new Promise<int>()` resolves to `Promise<int>` (explicit overrides default)
- [ ] `new MyMap<int>()` for `MyMap<TKey, TValue = mixed>` resolves to `MyMap<int, mixed>`
- [ ] `<T, U = T>` is valid — `Pair<int>` resolves to `Pair<int, int>`
- [ ] `<T, U extends Comparable = T>` where `T extends Comparable` is valid
- [ ] `<T, U extends Comparable = T>` where `T` has no constraint produces error 4310
- [ ] `<T = T>` (direct self-reference) produces `CheckerGenericDefaultCircularReference` (4312)
- [ ] A transitive default cycle (e.g. `<T = U, U = T>`) produces `CheckerGenericDefaultCircularReference` (4312) without infinite recursion
- [ ] Type inference takes priority over defaults for function calls
- [ ] When inference fails and a default exists, the default is used
- [ ] When inference fails and no default exists, `CheckerGenericArgumentCountMismatch` is reported
- [ ] `MessageCode.CheckerGenericDefaultDoesNotSatisfyConstraint` (4310) exists with correct resource string
- [ ] `MessageCode.CheckerGenericNonDefaultAfterDefault` (4311) exists with correct resource string
- [ ] `MessageCode.CheckerGenericDefaultCircularReference` (4312) exists with correct resource string

---

## Phase 5: Tyhpdef — Preserve Defaults in Generated Package Tyhpdef




### Phase Overview

When generating `package.tyhp.json` for a compiled Tyhp library (Story 20, Track C; triggered by `"type": "library"` in `tyhp.json`), generic parameter defaults must be included in the output so that consumers of the library get the same defaulting behavior.

### Deliverables

**Modified files:**
- Tyhpdef generator (Story 20) — Include default types in generic parameter output
- Tyhpdef model classes — Extend `GenericParameters` representation to include defaults

### Implementation Details

#### 5.1 Update Tyhpdef Model

Generic parameters must be stored as structured data (not raw strings) to support robust serialization and deserialization of constraints and defaults. Use the `TyhpdefGenericParameter` class:

```csharp
public class TyhpdefGenericParameter
{
    public string Name { get; set; }
    public string? Constraint { get; set; }
    public string? DefaultType { get; set; }
}
```

If the current `TyhpdefClassDeclaration` and `TyhpdefMethod` store generic parameters as `List<string>`, migrate them to `List<TyhpdefGenericParameter>`. This structured representation is required to preserve constraints and defaults through the tyhpdef round-trip pipeline.

#### 5.2 Generate Default Syntax in Package Tyhpdef Output

When writing a tyhpdef file, include the default type in the generic parameter list:

```tyhpdef
<?tyhpdef

class Promise<TReturn extends void|mixed = void> {
    public static function _async<T extends void|mixed = void>(callable<T> $fn): static<T>;
    public static function _await<T>(Promise<T> $promise): T;
    // ...
}

type Collection<T = mixed> = array<T>;
```

#### 5.3 Parse Defaults in Tyhpdef Input

The tyhpdef parser (which uses the same grammar) already handles parsing `= typeExpr` after Phase 1. Verify that tyhpdef files with generic defaults round-trip correctly: parse → bind → check → generate `package.tyhp.json` → parse again.

### Acceptance Criteria

- [ ] Generated `package.tyhp.json` files include `= DefaultType` in generic parameter lists
- [ ] `Promise<TReturn extends void|mixed = void>` appears correctly in `tyhp/async`'s generated `package.tyhp.json`
- [ ] `package.tyhp.json` files with generic defaults can be consumed by another Tyhp project via Composer dependency
- [ ] External consumers can use `new Promise()` without specifying `<void>` when consuming via `package.tyhp.json`
- [ ] Round-trip: Tyhp source → tyhpdef → consume → same behavior

---

## Phase 6: LSP — Show Defaults in Hover and Completion




### Phase Overview

The language server should display generic parameter defaults in hover tooltips and code completion to help developers understand optional type parameters.

### Deliverables

**Modified files:**
- LSP hover provider — Include defaults in generic type display
- LSP completion provider — Show defaulted parameters as optional

### Implementation Details

#### 6.1 Hover Display

When hovering over a generic type usage like `Promise`, show the full signature including defaults:

```
class Promise<TReturn extends void|mixed = void>
```

When hovering over a usage like `new Promise()`, show the resolved type:

```
Promise<void>  (TReturn defaults to void)
```

#### 6.2 Completion Display

When auto-completing generic type arguments (e.g., after typing `Promise<`), show parameters with their defaults:

```
TReturn extends void|mixed = void  (optional)
```

#### 6.3 Signature Help

When the user is typing generic type arguments, signature help should indicate which parameters are required vs. optional (defaulted), similar to how function parameter signature help works.

### Acceptance Criteria

- [ ] Hovering over `Promise` shows `Promise<TReturn extends void|mixed = void>`
- [ ] Hovering over `new Promise()` shows resolved type `Promise<void>`
- [ ] Code completion for generic parameters indicates which are optional
- [ ] Signature help displays defaults for generic parameters

---

## Phase 7: Testing — Comprehensive Test Coverage




### Phase Overview

Add tests covering all aspects of generic type parameter defaults: parsing, binding, checking, tyhpdef round-tripping, and edge cases.

### Deliverables

**New test files/sections:**
- Generic default parsing tests
- Generic default binding tests
- Generic default checker tests (validation, application, inference interaction)
- Generic default tyhpdef tests

### Implementation Details

#### 7.1 Parsing Tests

```
GenericDefaultParsingTests:
- Parse `<T = int>` — verify DefaultExpr is populated
- Parse `<T extends Foo = Bar>` — verify both ConstraintExpr and DefaultExpr
- Parse `<T, U = int>` — verify first has no default, second has default
- Parse `<T = int, U = string>` — verify both have defaults
- Parse `<T>` — verify DefaultExpr is null (backward compatibility)
- Parse `<T extends Foo>` — verify DefaultExpr is null (backward compatibility)
```

#### 7.2 Binding Tests

```
GenericDefaultBindingTests:
- Bind `class A<T = int>` — verify GenericTypeParameterSymbol.DefaultType is populated
- Bind `class A<T extends Foo = Bar>` — verify both Constraint and DefaultType
- Bind `function foo<T = string>()` — verify function-level generic default
- Bind `type Alias<T = mixed> = array<T>` — verify type alias generic default
```

#### 7.3 Checker Validation Tests

```
GenericDefaultValidationTests:
- `<T = int>` — valid, no constraint to violate
- `<T extends Countable = array>` — valid, array implements Countable
- `<T extends Countable = string>` — ERROR 4310
- `<T = int, U>` — ERROR 4311
- `<T, U = int, V>` — ERROR 4311 (V has no default after U has one)
- `<T, U = int, V = string>` — valid
- `<T = int, U = string, V = bool>` — valid (all defaulted)
- `<T, U = T>` — valid (forward reference to earlier param)
- `<T extends Comparable, U extends Comparable = T>` — valid
- `<T, U extends Comparable = T>` — ERROR 4310 (T doesn't satisfy Comparable)
```

#### 7.4 Default Application Tests

```
GenericDefaultApplicationTests:
- `Promise<>` / `new Promise()` → resolves to Promise<void>
- `Promise<int>` → resolves to Promise<int>
- `MyMap<int>` for `MyMap<K, V = mixed>` → resolves to MyMap<int, mixed>
- `MyMap<int, string>` → resolves to MyMap<int, string>
- `MyMap<>` for `MyMap<K = string, V = mixed>` → resolves to MyMap<string, mixed>
- Too few args (less than required count) → CheckerGenericArgumentCountMismatch
- Too many args → CheckerGenericArgumentCountMismatch
- `Pair<int>` for `Pair<T, U = T>` → resolves to Pair<int, int>
```

#### 7.5 Inference Priority Tests

```
GenericDefaultInferencePriorityTests:
- `wrap(42)` for `wrap<T = string>(T $value)` → T = int (inferred, not string)
- `wrap<bool>(true)` → T = bool (explicit)
- `wrap("hello")` → T = string (inferred, coincidentally matches default)
- Function with non-inferable generic: `create<T = int>(): T` called as `create()` → T = int (default)
```

#### 7.6 Tyhpdef Round-Trip Tests

```
GenericDefaultTyhpdefTests:
- Compile class with generic defaults → tyhpdef includes defaults
- Consume tyhpdef with defaults → defaults work for consumer
- Round-trip: source → tyhpdef → parse → verify defaults preserved
```

#### 7.7 Edge Case Tests

```
GenericDefaultEdgeCaseTests:
- Default is a union type: `<T = int|string>`
- Default is a nullable type: `<T = ?string>`
- Default is a generic type: `<T = array<int>>`
- Default is `void` with opt-in constraint: `<T extends void|mixed = void>`
- Default is `never` with opt-in constraint: `<T extends never|mixed = never>`
- Default references itself: `<T = T>` — ERROR 4312 (circular reference)
- Multiple classes in inheritance chain with different defaults
- Interface with defaults implemented by class
- Type alias with defaults used in another type alias
```

### Acceptance Criteria

- [ ] All parsing tests pass
- [ ] All binding tests pass
- [ ] All checker validation tests pass (both valid and invalid cases)
- [ ] All default application tests pass
- [ ] All inference priority tests pass
- [ ] All tyhpdef round-trip tests pass
- [ ] All edge case tests pass
- [ ] No regressions in existing generic tests (backward compatibility)

---

## Appendix A: Complete MessageCode Summary

```csharp
// Generic type parameter defaults (4310-4312) — Story 28
CheckerGenericDefaultDoesNotSatisfyConstraint = 4310,  // "Default type '{0}' does not satisfy constraint '{1}' on generic parameter '{2}'"
CheckerGenericNonDefaultAfterDefault = 4311,            // "Generic parameter '{0}' without a default cannot follow parameter '{1}' which has a default"
CheckerGenericDefaultCircularReference = 4312,          // "Generic parameter '{0}' has a circular default type reference"
```

Note: These codes live in the `4300–4399` feature-checker band. Story 27's `new<>` codes are `4300–4303` (same band). Story 08 owns the contiguous `4008–4211` range; Story 16's expression-tree errors are `4320–4324`; Story 25's internal-visibility errors are `4330–4334`. Placing Story 28's codes at `4310–4312` keeps clear boundaries from all of these.

---

## Appendix B: Files Modified/Created

| File | Change |
|------|--------|
| `Tyhp/TyhpLang/Grammar/TyhpParser.g4` | Add `= typeExpr` to `tyhpGenericsTypeArgument` rule |
| `Tyhp/TyhpLang/Ast/TyhpGenericsTypeArgumentAst.cs` | Add `DefaultType` property, update `Create` factory |
| `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpGenerics.cs` | Extract `DefaultExpr` from parse tree |
| `Tyhp/TyhpLang/Binder/Symbols/GenericTypeParameterSymbol.cs` | Add `DefaultType` and `HasDefault` properties |
| Binder walk files | Pass `DefaultType` when creating `GenericTypeParameterSymbol` |
| `Tyhp/TyhpLang/Checker/TyhpChecker.TyhpFeatures.cs` | Validate ordering, constraint satisfaction, apply defaults |
| `Tyhp/Domain/Exceptions/MessageCode.cs` | Add 4310–4312 |
| `Tyhp/Resources/CLI.TyhpHostedService.resx` | Add localized error strings for 4310, 4311, 4312 |
| Tyhpdef generator | Include default types in generic parameter output |
| LSP hover/completion providers | Display defaults |
| Test files | Comprehensive generic default tests |

---

## Appendix C: Cross-References to Other Stories

This feature is referenced or assumed by:

- **Story 02 (Binder):** `GenericTypeParameterSymbol` is defined here. Story 28 adds the `DefaultType` property.
- **Story 06 (TyhpSpec):** The `Promise<TReturn extends void|mixed = void>` type definition uses a generic default. Story 06 assumes this syntax works.
- **Story 08 (Checker), Phase 5.5:** States "If generic parameters have defaults, they are optional." Story 28 implements this rule.
- **Story 20 (Tyhpdef Generator):** `package.tyhp.json` output must preserve generic defaults. Story 28 extends the generator to include them.

---

## Human Testing and Verification

> These steps are for a human developer to manually verify the implementation works end-to-end. Steps can be skipped or modified as needed — they are guidelines, not a rigid checklist.

### Step 1: Verify Grammar Accepts Default Syntax

Create a file `test_generic_defaults_parse.tyhp`:

```tyhp
<?tyhp

// No constraint, with default
class Container<T = mixed> {
    private T $value;
    public function __construct(T $value) {
        $this->value = $value;
    }
    public function get(): T {
        return $this->value;
    }
}

// Constraint and default
class TypedContainer<T extends Serializable = JsonSerializable> {
    private T $value;
    public function __construct(T $value) {
        $this->value = $value;
    }
}

// Multiple params, trailing defaults
class MyMap<TKey, TValue = mixed> {
    private array<TKey, TValue> $data = [];
}

// All defaulted
class Config<TKey = string, TValue = mixed> {}

// Forward reference — U defaults to T
class Pair<T, U = T> {
    public T $first;
    public U $second;
    public function __construct(T $first, U $second) {
        $this->first = $first;
        $this->second = $second;
    }
}

// Function with default
function wrap<T = string>(T $value): array<T> {
    return [$value];
}

// Interface with default
interface Repository<TEntity, TId = int> {
    public function find(TId $id): ?TEntity;
}

// Type alias with default
type Collection<T = mixed> = array<T>;
```

Run `tyhp lint test_generic_defaults_parse.tyhp`. **Expected:** No parse errors. All declaration patterns with `= DefaultType` are accepted.

### Step 2: Verify Default Application at Usage Sites

Create a file `test_generic_defaults_usage.tyhp`:

```tyhp
<?tyhp

class Promise<TReturn extends void|mixed = void> {
    private ?TReturn $result = null;

    public function resolve(TReturn $value): void {
        $this->result = $value;
    }

    public function getResult(): ?TReturn {
        return $this->result;
    }
}

class Box<T = mixed> {
    public T $value;
    public function __construct(T $value) {
        $this->value = $value;
    }
}

// Default applied — TReturn = void
Promise $p1 = new Promise();

// Explicit — TReturn = void (same as default)
Promise<void> $p2 = new Promise<void>();

// Explicit override — TReturn = int
Promise<int> $p3 = new Promise<int>();
$p3->resolve(42);

// Default applied — T = mixed
Box $b1 = new Box('hello');

// Explicit — T = string
Box<string> $b2 = new Box<string>('hello');
```

Run `tyhp lint test_generic_defaults_usage.tyhp`. **Expected:** No errors. `Promise` without type args should resolve to `Promise<void>` and `Box` without type args should resolve to `Box<mixed>`.

### Step 3: Verify Partial Type Argument Application

Create a file `test_generic_defaults_partial.tyhp`:

```tyhp
<?tyhp

class MyMap<TKey = string, TValue = mixed> {
    private array<TKey, TValue> $data = [];

    public function set(TKey $key, TValue $value): void {
        $this->data[$key] = $value;
    }

    public function get(TKey $key): ?TValue {
        return $this->data[$key] ?? null;
    }
}

// Both default: TKey = string, TValue = mixed
MyMap $m1 = new MyMap();

// First explicit, second default: TKey = int, TValue = mixed
MyMap<int> $m2 = new MyMap<int>();

// Both explicit: TKey = int, TValue = string
MyMap<int, string> $m3 = new MyMap<int, string>();
$m3->set(1, 'hello');
string|null $val = $m3->get(1);

// Forward reference default: Pair<T, U = T>
class Pair<T, U = T> {
    public T $first;
    public U $second;
    public function __construct(T $first, U $second) {
        $this->first = $first;
        $this->second = $second;
    }
}

// U defaults to T (int), so Pair<int> = Pair<int, int>
Pair<int> $p = new Pair<int>(1, 2);
```

Run `tyhp lint test_generic_defaults_partial.tyhp`. **Expected:** No errors. Partial application fills in defaults from right to left.

### Step 4: Verify Checker Error Cases

Create a file `test_generic_defaults_errors.tyhp`:

```tyhp
<?tyhp

// ERROR 4311: Non-defaulted U follows defaulted T
class BadOrder<T = int, U> {}

// ERROR 4311: Non-defaulted V follows defaulted U
class BadOrder2<T, U = int, V> {}

// ERROR 4310: Default type does not satisfy constraint
// (string does not implement Countable)
class BadConstraint<T extends Countable = string> {}
```

Run `tyhp lint test_generic_defaults_errors.tyhp`. **Expected:**
- `BadOrder` — error 4311 (`CheckerGenericNonDefaultAfterDefault`)
- `BadOrder2` — error 4311 on `V`
- `BadConstraint` — error 4310 (`CheckerGenericDefaultDoesNotSatisfyConstraint`)

### Step 5: Verify Type Inference Takes Priority Over Defaults

Create a file `test_generic_defaults_inference.tyhp`:

```tyhp
<?tyhp

function wrap<T = string>(T $value): array<T> {
    return [$value];
}

// T should be inferred as int from the argument, NOT the default (string)
array<int> $intArr = wrap(42);

// T should be inferred as string (coincidentally matches default)
array<string> $strArr = wrap("hello");

// T is explicitly provided as bool
array<bool> $boolArr = wrap<bool>(true);

// T cannot be inferred (no argument of type T), so default is used
function create<T = int>(): T {
    // This would need runtime generic support; just testing type resolution
    return default(T);
}
```

Run `tyhp lint test_generic_defaults_inference.tyhp`. **Expected:** No errors. `wrap(42)` should infer `T = int`, not use the default `string`.

### Step 6: Verify Runtime Behavior

Create a file `test_generic_defaults_runtime.tyhp`:

```tyhp
<?tyhp

class Box<T = string> {
    public T $value;
    public function __construct(T $value) {
        $this->value = $value;
    }
    public function describe(): string {
        return 'Box(' . (string)$this->value . ')';
    }
}

// Default: T = string
Box $s = new Box('hello');
echo $s->describe() . "\n";

// Explicit: T = int
Box<int> $i = new Box<int>(42);
echo $i->describe() . "\n";

class Pair<T, U = T> {
    public function __construct(public T $first, public U $second) {}
}

// U defaults to int (same as T)
Pair<int> $p = new Pair<int>(1, 2);
echo $p->first . ', ' . $p->second . "\n";
```

Compile with `tyhp build`, then run with `php <output>.php`. **Expected output:**

```
Box(hello)
Box(42)
1, 2
```

### Step 7: Verify Tyhpdef Round-Trip

If working with a library project, compile a Tyhp library that uses generic defaults and verify the generated `package.tyhp.json` preserves the defaults. Then create a consumer project that depends on the library and verify that the consumer can use the generic types without specifying defaulted arguments.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
