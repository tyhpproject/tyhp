# Implementation Plan: Story 27 — `new<TArgs...>` Constructable Object Type

> **Roadmap position:** Story 27 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** all earlier stories (01–26)
> **Renumbered from:** legacy Story 20
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** `Syntax_TODO.md` / `TODO.md` — Constructable object type
> **Branch:** TBD
> **Generated:** 2026-02-17
> **Prerequisites:** All earlier stories (01–26) must be complete — the parser, binder, checker, emitter, Tyhp runtime packages, LSP, and testing infrastructure must be fully functional.

---

## Architecture Overview

### What `new<TArgs...>` Is

`new` is a **built-in type** in the Tyhp type system representing "an object that can be constructed." It is used exclusively in type positions — never in expression positions (nobody writes `new new()`). It enables type-safe factory patterns, dependency injection containers, and generic constructor constraints.

`new` works analogously to other built-in type facets like `callable<TArgs..., TReturn>` and `iterable<TKey, TValue>`:

| Built-in Type | Meaning | Generic Parameters |
|---------------|---------|-------------------|
| `callable` | Can be invoked | Parameter types + return type (return-last convention) |
| `iterable<K, V>` | Can be iterated | Key type, value type |
| `new` | Can be constructed with zero arguments | None |
| `new<TArgs...>` | Can be constructed with specific argument types | Constructor parameter types only (no return type — always the class itself) |

### Difference from `callable<>` Convention

Unlike `callable<TArgs..., TReturn>` which uses the **return-last** convention, `new<TArgs...>` has **NO return type parameter**. The return type of a constructor is always the class being constructed, so it would be redundant:

- `callable<string, int>` → takes `string`, returns `int` (last = return)
- `new<string, int>` → constructor takes `(string, int)`, returns the class itself (no return parameter)

### Computed Intersection Types

Tyhp's type system computes **intersection types** for concrete class instances that include all applicable built-in type facets.

**Example 1:** A class `MyService` with constructor `__construct(string $name, int $priority = 0)` has computed type:

```
MyService & object & mixed & new<string> & new<string, int>
```

Since `$name` is required, the class does NOT satisfy `new` (zero-arg constructable). It satisfies `new<string>` (minimum one arg) and `new<string, int>` (both args).

**Example 2:** A class with constructor `__construct(string $a = "", bool $b = false)` has computed type that includes:

```
... & new & new<string> & new<string, bool>
```

Since all parameters have defaults, the class satisfies `new` (zero args), `new<string>` (one arg), and `new<string, bool>` (two args).

### Type Hierarchy

```
mixed
  └── object
        ├── new            (constructable with zero args)
        ├── new<T1>        (constructable with one arg of type T1)
        ├── new<T1, T2>    (constructable with two args of types T1, T2)
        └── ...
```

All `new` variants extend `object` directly. They are **sibling types**, not a nested subtype chain — `new<T1, T2>` does NOT extend `new<T1>`. A concrete class may satisfy multiple `new<>` variants independently based on which constructor call prefixes are valid (considering default parameter values).

Not every `object` is constructable (abstract classes, private constructors, interfaces).

### Position in the Pipeline

```
ALL Prior Stories Complete (through Story 26)
    │
    ▼
┌──────────────────────────────────────────────────────────┐
│  STORY 27: new<TArgs...> Constructable Object Type       │
│  ◄── THIS PLAN                                           │
│                                                          │
│  Touches: Grammar, TyhpSpec, Binder, Checker, Emitter,  │
│           LSP, Testing                                   │
│                                                          │
│  Phase 1: Grammar (new in type positions)                │
│  Phase 2: TyhpSpec (built-in type definition)            │
│  Phase 3: Binder (computed new<> facets)                 │
│  Phase 4: Checker (constraint validation)                │
│  Phase 5: Emitter (strip new<> from PHP output)          │
│  Phase 6: LSP (autocomplete and hover)                   │
│  Phase 7: Testing                                        │
└──────────────────────────────────────────────────────────┘
```

### Design Principles

1. **`new` is a built-in type, not a class name.** It lives alongside `callable`, `iterable`, `object`, `mixed`, etc. in the type system. The parser must distinguish `new` in type position from `new` in expression position (creating instances).

2. **Constructor parameters only — no return type.** Unlike `callable<TArgs..., TReturn>`, the generic parameters of `new<>` are purely constructor parameter types. The return type is always the class being constructed.

3. **Default parameter expansion.** A constructor with N parameters where the last M have defaults generates M+1 `new<>` facets (one for each valid prefix of arguments, down to the required minimum).

4. **Only concrete classes with public constructors match.** Abstract classes, interfaces, traits, enums without constructors, and classes with private/protected constructors do NOT satisfy `new<>`.

5. **Compile-time only.** The `new` type has no PHP runtime representation. The emitter strips it from output, replacing it with `object` in type positions and removing it from generic constraints.

6. **`new` (bare) means zero-arg constructable.** This covers classes with no constructor, all-default constructors, and variadic-only constructors.

### Safety Notes

- **Before any file replacement or major rewrite**, create a timestamped backup: `<filename>.<YYYYMMDD_HHMMSS>.backup`
- **Never use** `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive commands
- **Prefer incremental edits** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Tyhp Code Examples

### Example 1: `new` and `new<TArgs...>` in Type Positions

```tyhp
<?tyhp

// As a parameter type — accept any zero-arg constructable class
function createDefault(new $constructable): object {
    return new $constructable();
}

// With generic args — accept any class constructable with (string, int)
function createWithArgs(new<string, int> $constructable, string $name, int $id): object {
    return new $constructable($name, $id);
}

// As a return type (unusual but valid)
function getFactory(): new<string> {
    return SomeClass::class;  // SomeClass has __construct(string $x)
}
```

### Example 2: Generic Constraints with `new<>`

```tyhp
<?tyhp

// T must be constructable with a string argument
function create<T extends new<string>>(string $value): T {
    return new T($value);
}

// T must be constructable with zero arguments AND implement Logger
function makeLogger<T extends new & Logger>(): T {
    return new T();
}

// Multiple constraints
function buildService<T extends new<Config, Database> & Service>(
    Config $config,
    Database $db
): T {
    return new T($config, $db);
}

// Usage:
class UserService extends Service {
    public function __construct(Config $config, Database $db) { ... }
}

UserService $svc = buildService<UserService>($config, $db); // OK
```

### Example 3: Computed Intersection Types

```tyhp
<?tyhp

class SimpleClass {
    // No constructor — constructable with zero args
}
// Computed type: SimpleClass & object & mixed & new

class ConfiguredClass {
    public function __construct(
        string $name,
        int $priority = 0,
        bool $active = true
    ) { ... }
}
// Computed type includes:
//   ConfiguredClass & object & mixed
//   & new<string>              (one required arg)
//   & new<string, int>         (two args)
//   & new<string, int, bool>   (three args)
// Does NOT include bare `new` (first param is required)

class AllDefaultsClass {
    public function __construct(string $label = "default") { ... }
}
// Computed type includes:
//   AllDefaultsClass & object & mixed
//   & new                      (zero args — all have defaults)
//   & new<string>              (one arg)
```

### Example 4: Error Cases

```tyhp
<?tyhp

abstract class AbstractBase {
    public function __construct(string $name) { ... }
}
// AbstractBase does NOT satisfy `new<string>` — cannot be instantiated

interface Buildable {
    // Interfaces do NOT satisfy `new<>` — cannot be instantiated
}

class PrivateCtorClass {
    private function __construct() { ... }
    public static function create(): static { return new static(); }
}
// PrivateCtorClass does NOT satisfy `new` — constructor is private

function tryCreate<T extends new<string>>(string $val): T {
    return new T($val);
}

// ERROR: CheckerTypeNotConstructable (4301)
// "Type 'AbstractBase' does not satisfy 'new' constraint — it must be
//  a concrete class with a public constructor"
tryCreate<AbstractBase>("hello");

// ERROR: CheckerConstructorSignatureMismatch (4302)
// "Type 'AllDefaultsClass' does not satisfy 'new<int>' —
//  constructor signature does not match"
function wrongSig<T extends new<int>>(): T { return new T(42); }
wrongSig<AllDefaultsClass>(); // AllDefaultsClass takes string, not int

// ERROR: CheckerAbstractNotConstructable (4303)
// "Abstract type 'AbstractBase' cannot satisfy 'new' constraint"
function noAbstract<T extends new>(): T { return new T(); }
noAbstract<AbstractBase>();
```

---

## Phase 1: Grammar — `new` as a Type Keyword

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Add `new` as a recognized type keyword in type positions, with optional generic arguments `new<T1, T2, ...>`. The parser must distinguish `new` in type position (this feature) from `new` in expression position (creating instances, which already exists).

### Deliverables

**Modified grammar files:**
- `Tyhp/TyhpLang/Grammar/PhpParser.g4` — Add `NewType=T_NEW` to the `typeWithoutStatic` rule as an alternative alongside `ArrayType=T_ARRAY` and `CallableType=T_CALLABLE`. **Justification:** unlike Story 26 (which kept its grammar changes Tyhp-only), `new` is a *type-position keyword* shared by the base type grammar (it sits beside `T_ARRAY`/`T_CALLABLE` in `typeWithoutStatic`), so the alternative belongs in the base `PhpParser.g4`. The optional generic arguments are still gated to Tyhp mode via the `typeNameGrammarAddon` override in `TyhpParser.g4` (§1.2), so PHP-mode parsing is unaffected.
- `Tyhp/TyhpLang/Grammar/TyhpParser.g4` — No changes needed if `typeNameGrammarAddon` already handles generic arguments for all type alternatives (verify)
- `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` / `Tyhp/TyhpLang/Grammar/PhpLexer.g4` — No changes needed; `T_NEW` already exists as a token

**Modified visitor files:**
- `Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpTypes.cs` — Handle the `NewType` alternative when building type AST nodes

**Regenerated parser files:**
- All ANTLR-generated files in `Tyhp/TyhpLang/Parser/`

### Implementation Details

#### 1.1 Modify the `typeWithoutStatic` Grammar Rule

**File:** `Tyhp/TyhpLang/Grammar/PhpParser.g4`

The current rule is:

```antlr
typeWithoutStatic
    : (ArrayType=T_ARRAY | CallableType=T_CALLABLE | Identifier=name)
        typeNameGrammarAddon
    | typeWithoutStaticGrammarAddon
    ;
```

Add `NewType=T_NEW` as a new alternative in the first production:

```antlr
typeWithoutStatic
    : (ArrayType=T_ARRAY | CallableType=T_CALLABLE | NewType=T_NEW | Identifier=name)
        typeNameGrammarAddon
    | typeWithoutStaticGrammarAddon
    ;
```

This works because:
- `T_NEW` already exists as a lexer token
- The `typeNameGrammarAddon` rule (overridden in `TyhpParser.g4`) already handles optional generic arguments via `tyhpGenericsTypeArguments?`
- The parser can distinguish type-position `new` from expression-position `new` by grammatical context: `typeWithoutStatic` is only reached from type expression rules (parameter types, return types, property types, generic constraints), never from expression rules

#### 1.2 Verify Generic Arguments Work for `new` Types

**File:** `Tyhp/TyhpLang/Grammar/TyhpParser.g4`

The `typeNameGrammarAddon` override already attaches optional generic arguments:

```antlr
typeNameGrammarAddon
    : GenericArguments=tyhpGenericsTypeArguments?
        {this.isLanguageMode("tyhp")}?
    ;
```

This applies to ALL alternatives in `typeWithoutStatic` — `T_ARRAY`, `T_CALLABLE`, `Identifier=name`, and now `T_NEW`. So `new<string, int>` will automatically parse as a `typeWithoutStatic` node with `NewType=T_NEW` and `GenericArguments` populated. No changes to `TyhpParser.g4` needed.

#### 1.3 No Ambiguity with Expression-Position `new`

The grammar is unambiguous because:
- **Expression context:** `T_NEW` appears in `newDereferenceable` / `newNonDereferenceable` rules, which are reached from `expression` → `expressionPrimary` → `fullyDereferenceable` / `newNonDereferenceable`. These rules expect a class name reference or anonymous class after `new`.
- **Type context:** `T_NEW` in `typeWithoutStatic` is reached from `typeExprWithoutStatic` → `type` → `returnType` / `optionalTypeWithoutStatic`, etc. These are structurally distinct parse paths.

ANTLR4's adaptive LL(*) parser handles this without ambiguity because the parent rule context determines which path is taken.

#### 1.4 Update the Visitor for `NewType` Type Nodes

**File:** `Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpTypes.cs`

In the visitor method that processes `typeWithoutStatic` contexts, add handling for the new `NewType` alternative. The visitor should create a type AST node that represents the `new` built-in type, similar to how it handles `CallableType`:

- If `ctx.NewType != null`:
  - Create a built-in type AST node with name `"new"`
  - If generic arguments are present (from `typeNameGrammarAddon`), attach them as the constructor parameter types
  - The AST node should be distinguishable from a class name reference to `new` (it's a built-in type, not an identifier)

#### 1.5 Regenerate ANTLR Parser

After modifying `PhpParser.g4`, regenerate all ANTLR parser output files:

```bash
./compile_grammar.sh
dotnet clean && dotnet restore && dotnet build
```

This regenerates `TyhpLexer.cs`, `TyhpParser.cs`, `TyhpParserVisitor.cs`, `TyhpParserBaseVisitor.cs`, and updates `Tyhp/TyhpLang/Tyhp/TyhpLang/Grammar/*.tokens` / `Tyhp/TyhpLang/Tyhp/TyhpLang/Grammar/*.interp`. Requires `antlr-ng` (`npm install -g antlr-ng`). Grammar regeneration is **not** triggered by `dotnet build` alone. Verify no grammar conflicts or warnings.

### Acceptance Criteria

- [ ] `new` is accepted in type positions: parameter types, return types, property types, generic constraints
- [ ] `new<string>`, `new<string, int>`, `new<T>` parse correctly with generic arguments
- [ ] `new` without generics parses correctly as a bare type
- [ ] Expression-position `new` (creating instances) still works: `new MyClass()`, `new $var()`, `new class {}`
- [ ] No grammar ambiguities or ANTLR warnings
- [ ] The visitor produces correct AST nodes for `new` type references
- [ ] `new` in intersection types works: `new & MyInterface` parses correctly
- [ ] `new<string>` in union types works: `new<string> | null` parses correctly
- [ ] All existing grammar tests continue to pass
- [ ] All example files continue to parse without errors

### Dependencies

- **Requires:** All prior stories complete (through Story 26) — grammar infrastructure, parser, visitor
- **Provides:** Grammar foundation for all subsequent phases

---

## Phase 2: TyhpSpec — Define `new` as a Built-in Type

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Define `new` as a built-in type in the TyhpSpec type definition files and register it in the type system. Establish the relationship `new extends object` and define how `new<TArgs...>` generic parameters map to constructor parameter types.

### Deliverables

**Modified binder built-in documentation:**
- `Tyhp/TyhpLang/Binder/BuiltIn/Types.cs` — Add `new` type documentation as a code comment alongside the built-in type registration (the file `Tyhp/TyhpSpec/tyhpTypes.tyhpdef` no longer exists)

**Modified binder built-in files:**
- `Tyhp/TyhpLang/Binder/BuiltIn/Types.cs` — Register `new` as a built-in type in `GlobalScope`

**Modified checker type files:**
- `Tyhp/TyhpLang/Checker/CheckedTypes.cs` (or equivalent) — Add `NewCheckedType` or handle `new` via `SimpleCheckedType` with special semantics

### Implementation Details

#### 2.1 Document `new` as a Built-in Type

**File:** `Tyhp/TyhpLang/Binder/BuiltIn/Types.cs`

Add a documentation comment above the `new` type registration in `PopulateGlobal()`:

```csharp
// Built-in type: new / new<TArgs...>
//
// Represents an object that can be constructed. `new` (bare) means constructable
// with zero arguments. `new<TArgs...>` means constructable with specific argument types.
//
// `new` extends `object` — every constructable type is an object.
//
// Unlike `callable<TArgs..., TReturn>` which uses return-last convention,
// `new<TArgs...>` has NO return type parameter — the return type is always
// the class itself.
//
// Examples:
//   new                    — constructable with zero arguments
//   new<string>            — constructable with (string)
//   new<string, int>       — constructable with (string, int)
//   new<string, int, bool> — constructable with (string, int, bool)
```

Note: Unlike `callable` which is a PHP-native type, `new` is Tyhp-specific. The actual type semantics are handled by the checker, not by tyhpdef declarations. The code comment serves as documentation; the checker implements the logic.

#### 2.2 Register `new` as a Built-in Type

**File:** `Tyhp/TyhpLang/Binder/BuiltIn/Types.cs`

In `Types.PopulateGlobal(GlobalScope globalScope)`, add `new` to the built-in types list alongside `callable`, `iterable`, `object`, etc.:

- Register `BuiltInTypeSymbol("new")` in the global scope
- This allows the binder to resolve `new` in type references

#### 2.3 Create `NewCheckedType` in the Checker

**File:** `Tyhp/TyhpLang/Checker/CheckedTypes/NewCheckedType.cs` (new file, or extend existing type hierarchy)

Create a checked type representation for `new<TArgs...>`, analogous to `CallableCheckedType`:

- `NewCheckedType` implements `ICheckedType`
- Properties:
  - `IReadOnlyList<ICheckedType> ConstructorParameterTypes` — the types of the constructor parameters (empty list for bare `new`)
  - `bool HasGenericArgs` — whether this is `new` (false) or `new<...>` (true with zero args is still bare `new`)
  - `CheckedTypeKind Kind => CheckedTypeKind.Simple` (or a new `CheckedTypeKind.New` if warranted)
  - `string DisplayName` — `"new"` or `"new<string, int>"` etc.
  - `bool IsNullable => false`
  - `bool IsNever => false`
  - `bool IsVoid => false`
  - `bool IsMixed => false`

**Resolution rules in `TypeInferrer.ResolveTypeExpression()`:**

When resolving a type expression where the base type is `new`:
1. If no generic arguments → create `NewCheckedType(constructorParameterTypes: [])` representing bare `new`
2. If generic arguments present → create `NewCheckedType(constructorParameterTypes: [resolvedArg1, resolvedArg2, ...])` — ALL generic args are constructor parameter types (no return-last convention)

#### 2.4 Establish `new extends object` Relationship

In `TypeComparer.IsAssignableTo()`:

- `NewCheckedType` is always assignable to `object` (since `new extends object`)
- `NewCheckedType` is always assignable to `mixed`
- `NewCheckedType` is NOT assignable to any concrete class (it represents a constraint, not a specific class)
- A concrete class type IS assignable to `NewCheckedType` if the class satisfies the constructability requirements (checked in Phase 4)

### Acceptance Criteria

- [ ] `new` is registered as a built-in type in `GlobalScope` via `Types.PopulateGlobal()`
- [ ] The binder resolves `new` in type references to the built-in type symbol
- [ ] `NewCheckedType` class exists with `ConstructorParameterTypes` property
- [ ] `TypeInferrer.ResolveTypeExpression()` creates `NewCheckedType` for `new` and `new<string, int>` type expressions
- [ ] `TypeComparer.IsAssignableTo(NewCheckedType, object)` returns `true`
- [ ] `TypeComparer.IsAssignableTo(NewCheckedType, mixed)` returns `true`
- [ ] `NewCheckedType.DisplayName` returns `"new"` or `"new<string, int>"` correctly
- [ ] The documentation comment for `new` is present in `Types.cs` alongside the built-in type registration

### Dependencies

- **Requires:** Phase 1 (Grammar — `new` parses in type positions)
- **Provides:** Type system foundation for binder computed types (Phase 3) and checker validation (Phase 4)

---

## Phase 3: Binder — Compute `new<>` Facets for Class Types

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

When the binder processes class declarations, compute the `new<>` type facets that should be included in the class's computed intersection type. For each valid constructor signature (considering default parameters), add the corresponding `new<...>` type to the computed intersection.

### Deliverables

**Modified binder files:**
- `Tyhp/TyhpLang/Binder/TyhpBinder.cs` (or relevant partial class) — Add `new<>` facet computation during class declaration binding
- `Tyhp/TyhpLang/Binder/Symbols/ObjectDeclarationSymbol.cs` — Add `ConstructableTypes` property storing the computed `new<>` facets

### Implementation Details

#### 3.1 Add `ConstructableTypes` to `ObjectDeclarationSymbol`

**File:** `Tyhp/TyhpLang/Binder/Symbols/ObjectDeclarationSymbol.cs`

> **Layering note:** `NewCheckedType` lives in the **checker** layer (Phase 2.3) and must NOT be referenced from the binder. The binder stores **raw constructable-type info** (the resolved/declared constructor parameter-type expressions for each valid call prefix); the **checker** materializes `NewCheckedType` instances from this raw info when validating constraints (Phase 4). The pseudocode below that mentions `NewCheckedType(...)` is illustrative shorthand for "a constructable facet with these parameter types" — at the binder level this is the raw parameter-type list, not a checker `NewCheckedType` object.

Add a property to store the computed constructable type facets as raw info:

- `List<ConstructableFacet> ConstructableTypes { get; }` — populated during binding, where `ConstructableFacet` is a binder-level record holding the ordered constructor parameter type expressions for one valid call prefix
- This list contains one entry for each valid constructor call signature
- For a constructor `__construct(string $a, bool $b = false, int $c = 0)`, this would contain facets with parameter lists:
  - `[string]` — minimum args (one required)
  - `[string, bool]` — two args
  - `[string, bool, int]` — three args (all explicit)

#### 3.2 Implement `new<>` Facet Computation

**File:** `Tyhp/TyhpLang/Binder/TyhpBinder.cs` (or new partial class `TyhpBinder.ConstructableTypes.cs`)

**Reuse:** Call `Tyhp.TyhpLang.ArityFacetExpansion.GetValidArityPrefixes` for the
`requiredCount…totalCount` loop. Callable/Closure optional-arity facets already use this helper
via `CallableArityFacetBuilder` — do **not** duplicate the prefix math. The binder stores raw
parameter-type lists per prefix; the checker materializes `NewCheckedType` (binder must not
reference checker types).

After binding a class declaration, compute its `new<>` facets:

```
function computeConstructableTypes(ObjectDeclarationSymbol classSymbol):
    // Rule 1: Only concrete classes with public constructors
    if classSymbol.IsAbstract:
        return []  // abstract classes are not constructable
    if classSymbol.ObjectKind is Interface or Trait:
        return []  // interfaces and traits are not constructable
    
    constructor = findConstructor(classSymbol)
    
    if constructor is null:
        // No constructor — constructable with zero args
        return [NewCheckedType([])]
    
    if constructor.Visibility is not Public:
        return []  // private/protected constructors are not constructable
    
    // Rule 2: Compute all valid call signatures considering defaults
    // Use ArityFacetExpansion.GetValidArityPrefixes on constructor.Parameters flags
    // (HasDefault = DefaultValue != null, IsVariadic).
    parameters = constructor.Parameters
    prefixes = ArityFacetExpansion.GetValidArityPrefixes(...)
    
    result = []
    for argCount in prefixes:
        paramTypes = parameters[0..argCount].Select(p => p.ResolvedType)  // non-variadic only
        result.Add(rawFacet(paramTypes))
    
    return result
```

**Variadic note:** Variadic parameters are excluded from prefix counts (same as callable facets). A
variadic-only constructor yields only bare `new` (prefix `0`). Do not generate infinite arities.

**Inherited constructors / promoted params:** unchanged from the original Story 27 rules below.

#### 3.3 Handle Special Constructor Cases

**Variadic constructors:**
- A constructor with only a variadic parameter `__construct(mixed ...$args)` satisfies `new` (zero args) because variadic parameters are always optional
- The `new<>` facets should include `new` but NOT generate infinite facets for each possible arg count — just `new` for the zero-arg case

**Inherited constructors:**
- If a class does not declare its own constructor but inherits one from a parent class, use the parent's constructor signature for facet computation
- If the parent constructor is `protected` and the child does not override it, the child is NOT externally constructable

**Constructor with promoted properties:**
- Promoted constructor parameters (`public string $name`) count as regular parameters for `new<>` facet computation

#### 3.4 Store Computed Facets on the Symbol

After computing the `ConstructableTypes` list, store it on the `ObjectDeclarationSymbol`. The checker and LSP will read this list later when validating `new<>` constraints.

#### 3.5 Integration with Computed Intersection Types

If the type system already computes intersection types for class instances (e.g., adding `&callable<...>` when `__invoke()` is present, or `&iterable<K, V>` when `Traversable` is extended), integrate `new<>` facets into the same mechanism:

- When computing the full type of a class instance, include all `ConstructableTypes` entries in the intersection
- Example: `MyClass & object & mixed & new<string> & new<string, int>`
- If the class has zero `ConstructableTypes`, the `new` facet is absent from the intersection (the class is not constructable)

### Acceptance Criteria

- [ ] `ObjectDeclarationSymbol.ConstructableTypes` property exists and is populated during binding
- [ ] A class with no constructor has `ConstructableTypes = [NewCheckedType([])]` (one entry: bare `new`)
- [ ] A class with `__construct(string $a, int $b = 0)` has `ConstructableTypes = [NewCheckedType([string]), NewCheckedType([string, int])]`
- [ ] A class with `__construct(string $a = "", bool $b = false, int $c = 0)` has 4 entries: bare `new`, `new<string>`, `new<string, bool>`, `new<string, bool, int>`
- [ ] Abstract classes have `ConstructableTypes = []` (empty)
- [ ] Interfaces have `ConstructableTypes = []`
- [ ] Classes with private/protected constructors have `ConstructableTypes = []`
- [ ] A class with a variadic-only constructor `__construct(mixed ...$args)` has `ConstructableTypes = [NewCheckedType([])]`
- [ ] Inherited constructors are considered: a child class without its own constructor uses the parent's
- [ ] Promoted constructor parameters are treated as regular parameters for `new<>` facets
- [ ] The computed `new<>` facets are included in the class's computed intersection type
- [ ] Enum types have `ConstructableTypes = []` (enums cannot be instantiated via `new`)

### Dependencies

- **Requires:** Phase 2 (TyhpSpec — `NewCheckedType` class exists), Story 02 (Binder infrastructure — `ObjectDeclarationSymbol`, constructor binding)
- **Provides:** Computed `new<>` facets for checker validation (Phase 4)

---

## Phase 4: Checker — Validate `new<>` Constraints

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Add type checking logic that validates `new<>` constraint satisfaction. When a type argument is provided for a generic parameter constrained by `new<TArgs...>`, the checker verifies that the class has a public constructor with a matching signature. This covers:

- Direct `new<>` constraint checking in generic type parameters
- Type assignability: `MyClass` → `new<string>` if constructor matches
- Error reporting for abstract classes, interfaces, and private constructors

### Deliverables

**Modified checker files:**
- `Tyhp/TyhpLang/Checker/TyhpChecker.TyhpFeatures.cs` — Add `CheckNewConstraint()` method
- `Tyhp/TyhpLang/Checker/TypeComparer.cs` — Extend `IsAssignableTo()` for `NewCheckedType` targets
- `Tyhp/TyhpLang/Checker/TypeInferrer.cs` — Handle `new<>` in type resolution

**Modified MessageCode:**
- `Tyhp/Domain/Exceptions/MessageCode.cs` — Add new error codes 4300–4303 (4300 is emitted from Phase 5.4)

**Modified resource files:**
- `Tyhp/Resources/CLI.TyhpHostedService.resx` — Add localized error strings for 4300–4303

### Implementation Details

#### 4.1 Add MessageCode Values

**File:** `Tyhp/Domain/Exceptions/MessageCode.cs`

Add the following codes in the `#region Checker` section. Story 27's canonical allocation is **4300–4303** (4300 is the static-generic resolution error referenced in Phase 5.4):

```csharp
// new<> constructable type constraints (4300-4303)
CheckerCannotResolveStaticGenericType = 4300, // "Cannot resolve generic type parameter '{0}' to a concrete type at this static call site"
CheckerTypeNotConstructable = 4301,          // "Type '{0}' does not satisfy 'new' constraint — it must be a concrete class with a public constructor"
CheckerConstructorSignatureMismatch = 4302,  // "Type '{0}' does not satisfy 'new<{1}>' — constructor signature does not match"
CheckerAbstractNotConstructable = 4303,      // "Abstract type '{0}' cannot satisfy 'new' constraint"
```

> **Note:** `CheckerAbstractNotConstructable = 4303` is **introduced by this story** — it does NOT already exist in committed code (despite earlier plan wording that implied otherwise). 4303 is free, so it is allocated here.

**File:** `Tyhp/Resources/CLI.TyhpHostedService.resx` (and locale variants)

Add corresponding resource strings:

```
ERROR_TYHP4300 = "Cannot resolve generic type parameter '{0}' to a concrete type at this static call site"
ERROR_TYHP4301 = "Type '{0}' does not satisfy 'new' constraint — it must be a concrete class with a public constructor"
ERROR_TYHP4302 = "Type '{0}' does not satisfy 'new<{1}>' — constructor signature does not match"
ERROR_TYHP4303 = "Abstract type '{0}' cannot satisfy 'new' constraint"
```

#### 4.2 Extend `TypeComparer.IsAssignableTo()` for `NewCheckedType`

**File:** `Tyhp/TyhpLang/Checker/TypeComparer.cs`

Add a case for when the `target` type is `NewCheckedType`:

```
IsAssignableTo(source, target):
    ...existing cases...
    
    if target is NewCheckedType newTarget:
        // Source must be a concrete class with a matching public constructor
        
        if source is not SimpleCheckedType or source.ResolvedSymbol is not ObjectDeclarationSymbol:
            return false  // only class types can satisfy new<>
        
        classSymbol = source.ResolvedSymbol as ObjectDeclarationSymbol
        
        // Check if the class's computed ConstructableTypes include a match
        if newTarget.ConstructorParameterTypes is empty (bare `new`):
            // Bare `new` — class must be constructable with zero args
            return classSymbol.ConstructableTypes.Any(ct => ct.ConstructorParameterTypes.Count == 0)
        else:
            // new<T1, T2, ...> — find a matching facet
            return classSymbol.ConstructableTypes.Any(ct =>
                ct.ConstructorParameterTypes.Count == newTarget.ConstructorParameterTypes.Count
                && ct.ConstructorParameterTypes.Zip(newTarget.ConstructorParameterTypes)
                    .All((actual, expected) => IsAssignableTo(expected, actual))
                    // Note: parameter types are contravariant — if the constraint says
                    // new<string>, the constructor can accept string or any supertype
            )
```

Also add the reverse direction: `NewCheckedType` as source:

```
    if source is NewCheckedType sourceNew:
        // new<T1, T2> is assignable to object (since new extends object)
        if target is SimpleCheckedType("object") or target is SimpleCheckedType("mixed"):
            return true
        // new<T1, T2> is assignable to another new<...> only if parameter lists match exactly
        if target is NewCheckedType targetNew:
            return isNewSubtype(sourceNew, targetNew)
        return false

// isNewSubtype checks if one NewCheckedType is a subtype of another.
// Since new<T1, T2> does NOT extend new<T1> (they are sibling types, not a nested
// subtype chain), this only returns true when parameter lists match exactly:
// same count and each parameter type is compatible (with contravariance for parameters).
function isNewSubtype(source: NewCheckedType, target: NewCheckedType) -> bool:
    if source.ConstructorParameterTypes.Count != target.ConstructorParameterTypes.Count:
        return false
    return source.ConstructorParameterTypes.Zip(target.ConstructorParameterTypes)
        .All((srcParam, tgtParam) => IsAssignableTo(tgtParam, srcParam))
        // Contravariant: target param type must be assignable TO source param type
```

#### 4.3 Implement `new<>` Constraint Matching

`new<>` constraints are matched against a class's computed `ConstructableTypes` facet list, not by subtyping between `new<>` types themselves:

- A class's `ConstructableTypes` list contains one entry for each valid constructor call prefix (considering default parameter values). For example, `__construct(string $a, int $b = 0)` produces facets `new<string>` and `new<string, int>`, but NOT `new` (since `$a` has no default).
- A `new<>` constraint is satisfied if ANY entry in the class's `ConstructableTypes` list matches the constraint's parameter types.
- `new<string, int>` does NOT automatically imply `new<string>` — a class with `__construct(string $a, int $b)` (both required) has only the facet `new<string, int>` and does not satisfy `new<string>`.

Each `new<>` constraint is independently matched against the facet list.

#### 4.4 Implement `CheckNewConstraint()` in the Checker

**File:** `Tyhp/TyhpLang/Checker/TyhpChecker.TyhpFeatures.cs`

Add a method that validates `new<>` constraints at generic instantiation sites:

```
function CheckNewConstraint(
    ICheckedType typeArgument,      // The concrete type being checked (e.g., MyClass)
    NewCheckedType constraint,      // The new<> constraint (e.g., new<string>)
    IBase2Ast node,                 // AST node for error location
    string fileName
):
    // Step 1: Verify the type argument is a class type
    if typeArgument is not a class type:
        diagnostics.AddError(
            MessageCode.CheckerTypeNotConstructable, fileName,
            node.Line, node.Column,
            typeArgument.DisplayName
        )
        return
    
    classSymbol = resolveToClassSymbol(typeArgument)
    
    // Step 2: Check for abstract classes
    if classSymbol.IsAbstract:
        diagnostics.AddError(
            MessageCode.CheckerAbstractNotConstructable, fileName,
            node.Line, node.Column,
            classSymbol.FullyQualifiedName
        )
        return
    
    // Step 3: Check for interfaces/traits
    if classSymbol.ObjectKind is Interface or Trait:
        diagnostics.AddError(
            MessageCode.CheckerTypeNotConstructable, fileName,
            node.Line, node.Column,
            classSymbol.FullyQualifiedName
        )
        return
    
    // Step 4: Check constructor existence and visibility
    constructor = findPublicConstructor(classSymbol)
    if constructor is null and constraint.ConstructorParameterTypes.Count > 0:
        diagnostics.AddError(
            MessageCode.CheckerConstructorSignatureMismatch, fileName,
            node.Line, node.Column,
            classSymbol.FullyQualifiedName,
            formatTypeList(constraint.ConstructorParameterTypes)
        )
        return
    
    // Step 5: Check signature match
    if not IsAssignableTo(typeArgument, constraint):
        if constraint.ConstructorParameterTypes.Count == 0:
            diagnostics.AddError(
                MessageCode.CheckerTypeNotConstructable, fileName,
                node.Line, node.Column,
                classSymbol.FullyQualifiedName
            )
        else:
            diagnostics.AddError(
                MessageCode.CheckerConstructorSignatureMismatch, fileName,
                node.Line, node.Column,
                classSymbol.FullyQualifiedName,
                formatTypeList(constraint.ConstructorParameterTypes)
            )
```

#### 4.5 Integrate with Generic Constraint Checking

**File:** `Tyhp/TyhpLang/Checker/TyhpChecker.TyhpFeatures.cs` (or the generic constraint checking section)

When checking generic type parameter constraints (already implemented in Story 08, Phase 5, §5.5):

- Existing logic handles `T extends SomeClass` and `T extends SomeInterface`
- Add handling for `T extends new<TArgs...>`:
  - When the constraint includes a `NewCheckedType`, call `CheckNewConstraint()` for the type argument
  - If the constraint is an intersection (e.g., `T extends new<string> & Logger`), check each component separately

#### 4.6 Validate `new<>` in Non-Constraint Positions

When `new<string>` is used directly as a parameter type (not just a generic constraint):

```tyhp
function factory(new<string> $constructable): object {
    return new $constructable("hello");
}
```

The checker should validate that:
- Arguments passed to this function satisfy the `new<string>` type
- The `new $constructable("hello")` expression is valid (the parameter is known to accept string)

This requires extending the checker's function call validation to handle `new<>` parameter types as a form of constraint on the argument.

#### 4.7 Validate `new` Expressions Using `new<>` Type Information

When the checker encounters `new T($args...)` where `T` is a generic type parameter constrained by `new<TArgs...>`:

- Verify the argument types match the `new<TArgs...>` parameter types
- The return type of the `new` expression is `T` (the generic parameter)
- If `T` is not constrained by `new<>`, report an error: the type parameter might not be constructable

### Acceptance Criteria

- [ ] `MessageCode.CheckerTypeNotConstructable` (4301) exists with correct resource string
- [ ] `MessageCode.CheckerConstructorSignatureMismatch` (4302) exists with correct resource string
- [ ] `MessageCode.CheckerAbstractNotConstructable` (4303) exists with correct resource string
- [ ] `TypeComparer.IsAssignableTo(MyConcreteClass, new<string>)` returns `true` when MyConcreteClass has `__construct(string $x)`
- [ ] `TypeComparer.IsAssignableTo(MyConcreteClass, new<int>)` returns `false` when constructor takes string, not int
- [ ] `TypeComparer.IsAssignableTo(AbstractClass, new)` returns `false`
- [ ] `TypeComparer.IsAssignableTo(InterfaceType, new)` returns `false`
- [ ] `TypeComparer.IsAssignableTo(PrivateCtorClass, new)` returns `false`
- [ ] `TypeComparer.IsAssignableTo(NoCtorClass, new)` returns `true` (no constructor = zero-arg constructable)
- [ ] `TypeComparer.IsAssignableTo(AllDefaultsClass, new)` returns `true`
- [ ] `TypeComparer.IsAssignableTo(NewCheckedType, object)` returns `true` (new extends object)
- [ ] Generic constraint `T extends new<string>` validated at instantiation site with concrete class
- [ ] Abstract class used for `T extends new` produces `CheckerAbstractNotConstructable` (4303)
- [ ] Wrong constructor signature produces `CheckerConstructorSignatureMismatch` (4302)
- [ ] `new T(...)` inside a generic function body validates argument types against the `new<TArgs...>` constraint
- [ ] Multiple `new<>` constraints in an intersection (`T extends new<string> & Logger`) are all checked independently
- [ ] Default parameter expansion: `MyClass` with `__construct(string $a, int $b = 0)` satisfies both `new<string>` and `new<string, int>`
- [ ] Error diagnostics include correct file, line, and column information

### Dependencies

- **Requires:** Phase 3 (Binder — `ConstructableTypes` populated on class symbols), Phase 2 (TyhpSpec — `NewCheckedType` exists), Story 08 (Checker infrastructure — `TypeComparer`, `TypeInferrer`, generic constraint checking)
- **Provides:** Type-safe constructor constraint validation

---

## Phase 5: Emitter — Strip `new<>` from PHP Output

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

The `new<>` type is purely a compile-time construct. The emitter must remove it from all PHP output since PHP has no equivalent concept. In type positions, `new` is replaced with `object`. In generic constraints, the `new<>` constraint is stripped entirely.

### Deliverables

**Modified emitter files:**
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` (or relevant partial class) — Add `new<>` stripping logic in type emission
- `Tyhp/TyhpLang/Emitter/PHPOutputFile.cs` — Handle `new<>` in `ConvertAliases()` if applicable

### Implementation Details

#### 5.1 Strip `new` from Type Positions

When emitting a type expression that contains `new` or `new<TArgs...>`:

**Standalone `new` type:**
```
// Tyhp:
function create(new $constructable): object { ... }

// Emitted PHP:
function create(object $constructable): object { ... }
```

Replace `new` with `object` because `new extends object` and PHP understands `object`.

**`new<TArgs...>` with generics:**
```
// Tyhp:
function factory(new<string, int> $constructable): object { ... }

// Emitted PHP:
function factory(object $constructable): object { ... }
```

Strip the generic arguments and replace with `object`.

#### 5.2 Strip `new<>` from Generic Constraints

Generic constraints are already stripped by the emitter (PHP doesn't have generics), but ensure `new<>` constraints are handled:

```
// Tyhp:
function create<T extends new<string>>(string $value): T { ... }

// Emitted PHP:
function create(string $value): mixed { ... }
```

The entire generic parameter `<T extends new<string>>` is erased. The return type `T` becomes `mixed` or the concrete type if determinable.

#### 5.3 Strip `new<>` from Intersection Types

When `new<>` appears in an intersection type:

```
// Tyhp:
function create<T extends new<string> & Logger>(string $val): T { ... }

// Emitted PHP:
// The generic is erased. If new<> was in a non-generic intersection:
function create(Logger $constructable): Logger { ... }
// Remove the new<string> component, keep the remaining components
```

When emitting an intersection type that includes `new` or `new<TArgs...>`:
- Remove the `new<>` component from the intersection
- If only one component remains, emit it directly
- If `new<>` was the only component, replace with `object`

#### 5.4 Emit `new T(...)` Where `T` Is a Generic Type Parameter

When `new T(...)` is used where `T` is a generic type parameter, the containing class has the `\Tyhp\Concerns\GenericObject` trait (since `RequiresRuntimeGenericTracking` is set — see Story 08/Story 11). The emitter resolves `T` at runtime via the `GenericObject` trait:

```php
$__T = $this->tyhpGenericObjectGetGenericType('T')->getType();
new $__T(...$args)
```

The `tyhpGenericObjectGetGenericType()` method (from `\Tyhp\Concerns\GenericObject`) returns a `NamedType` whose `getType()` provides the concrete class name string, which PHP's `new` operator accepts as a variable class name.

**Static context support for `new T()`:** In static methods where `$this` is not available, the compiler injects hidden `\Tyhp\NamedType` parameters for each generic type parameter that needs runtime resolution. This is consistent with the constructor parameter injection pattern used by `GenericObject`.

**Compiled output pattern for static methods:**

Tyhp source:
```
class Factory<T extends Serializable> {
    public static function create(): T {
        return new T();
    }
}

$user = Factory<User>::create();
```

Compiled PHP:
```php
class Factory {
    public static function create(\Tyhp\NamedType $__tyhp_T = new \Tyhp\NamedType('T', \Tyhp\Type::mixed())): mixed {
        // Runtime constraint check: T extends Serializable
        if (!\Tyhp\Type::compatible(
            \Tyhp\Type::fromClassName(\Serializable::class),
            $__tyhp_T->getUnderlyingType()
        )) {
            throw new \Tyhp\Exceptions\IncompatibleTypeException(
                \Tyhp\Type::fromClassName(\Serializable::class),
                $__tyhp_T->getUnderlyingType(),
                'T',
            );
        }
        return new ($__tyhp_T->getUnderlyingType()->getName())();
    }
}

// Call site — compiler resolves T to User and passes it:
$user = Factory::create(new \Tyhp\NamedType('T', \Tyhp\Type::fromClassName(\User::class)));
```

**Key design points:**
1. Hidden parameters use `\Tyhp\NamedType` (consistent with `GenericObject.tyhpGenericObjectInit()`)
2. Default value uses the generic parameter's default type if one exists, otherwise `\Tyhp\Type::mixed()`
3. Runtime constraint checks use `\Tyhp\Type::compatible()` against the declared bound
4. The tyhpdef for the method includes metadata marking which parameters are compiler-injected so consuming code knows to pass them
5. The naming convention `$__tyhp_T` (prefixed, matching the type parameter name) avoids collisions
6. If T is itself a generic parameter from an enclosing scope, the compiler chains through (passes the enclosing scope's type parameter)

**Checker behavior:** When a static method body uses `new T()` (or other runtime-generic operations), the checker marks the method as requiring reified type parameters. At the call site, if the type parameter cannot be resolved to a concrete type or a chained generic parameter, emit error `CheckerCannotResolveStaticGenericType` (MessageCode **4300** — see Phase 4.1; do NOT reuse 4303, which is `CheckerAbstractNotConstructable`).

#### 5.5 Handle `new<>` in Union Types

If `new<>` appears in a union type (unusual but syntactically valid):

```
// Tyhp:
new<string> | null

// Emitted PHP:
object | null   (or ?object)
```

Replace the `new<>` component with `object` in the union.

### Acceptance Criteria

- [ ] `new` type in parameter position emits as `object`
- [ ] `new<string, int>` type in parameter position emits as `object`
- [ ] Generic constraints containing `new<>` are fully stripped (as part of generic erasure)
- [ ] Intersection types containing `new<>` have the `new<>` component removed
- [ ] Union types containing `new<>` have `new<>` replaced with `object`
- [ ] `new<>` in return type position emits as `object`
- [ ] All example files with `new<>` types compile to valid PHP
- [ ] The emitted PHP is syntactically valid and runnable (no `new` keyword in type positions)
- [ ] No `new<` or `new ` appears in type positions in any emitted PHP file

### Dependencies

- **Requires:** Phase 4 (Checker — validation complete before emission), Story 09 (Emitter infrastructure)
- **Provides:** Valid PHP output for code using `new<>` types

---

## Phase 6: LSP — Autocomplete and Hover Support

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Add Language Server Protocol support for `new<>` types: autocomplete suggestions when typing type annotations, hover information showing the computed `new<>` facets for a class, and diagnostic integration for real-time error reporting.

### Deliverables

**Modified LSP files** (exact paths are **conditional on Story 19's language-server infrastructure** — locate the concrete handlers there and modify in place; do not assume new files):
- LSP hover handler (Story 19's hover provider) — Show `new<>` facets when hovering over class names
- LSP completion handler (Story 19's completion provider) — Suggest `new` and `new<>` in type annotation positions
- LSP diagnostic handler (Story 19's diagnostic pipeline) — Real-time `new<>` constraint validation

### Implementation Details

#### 6.1 Hover Information

When hovering over a class name, include its `new<>` facets in the hover tooltip:

```
class UserService
  Constructable: new<Config, Database>, new<Config, Database, Logger>
  Extends: Service
  Implements: Configurable
```

When hovering over a `new<string>` type annotation, show:

```
new<string>
  Built-in type: constructable with (string)
  Satisfied by any concrete class with a public constructor accepting string
```

#### 6.2 Autocomplete

In type annotation positions (parameter types, return types, property types, generic constraints):

- Suggest `new` as a type keyword (alongside `callable`, `iterable`, `object`, etc.)
- After typing `new<`, suggest type names for the generic arguments
- After typing `extends ` in a generic constraint, suggest `new` and `new<>`

#### 6.3 Diagnostic Integration

Real-time diagnostics should show:
- `CheckerTypeNotConstructable` (4301) when an abstract/interface/private-ctor class is used where `new<>` is expected
- `CheckerConstructorSignatureMismatch` (4302) when constructor signature doesn't match
- `CheckerAbstractNotConstructable` (4303) for abstract classes

#### 6.4 Go-to-Definition for `new<>` Types

When a user clicks "Go to Definition" on a `new<>` type:
- For `new` (bare): navigate to the built-in type documentation or show inline info
- For `new<string>`: no specific definition target (it's a constraint, not a declaration)

### Acceptance Criteria

- [ ] Hovering over a class name shows its `new<>` facets in the tooltip
- [ ] Hovering over a `new<string>` type annotation shows explanatory text
- [ ] `new` appears in autocomplete suggestions in type positions
- [ ] `new<>` generic argument autocomplete suggests type names
- [ ] Real-time diagnostics appear for `new<>` constraint violations
- [ ] The LSP does not crash when processing files with `new<>` types

### Dependencies

- **Requires:** Phase 4 (Checker — constraint validation working), Story 19 (LSP infrastructure)
- **Provides:** Full IDE experience for `new<>` types

---

## Phase 7: Testing

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Comprehensive testing of all `new<>` functionality across the compilation pipeline: grammar parsing, type resolution, binder facet computation, checker constraint validation, emitter stripping, and end-to-end compilation.

### Deliverables

**New test files:**
- `tests/Tyhp.Tests/NewType/NewTypeParserTests.cs` — Tyhp/TyhpLang/Grammar/parser tests
- `tests/Tyhp.Tests/NewType/NewTypeBinderTests.cs` — Binder facet computation tests
- `tests/Tyhp.Tests/NewType/NewTypeCheckerTests.cs` — Checker constraint validation tests
- `tests/Tyhp.Tests/NewType/NewTypeEmitterTests.cs` — Emitter stripping tests
- `tests/Tyhp.Tests/NewType/NewTypeEndToEndTests.cs` — Full pipeline tests

**New example files:**
- `Examples/NewType.tyhp` — Comprehensive `new<>` usage examples
- `Examples/NewType.php` — Expected PHP output for the above

### Implementation Details

#### 7.1 Parser Tests

Test that `new` and `new<TArgs...>` parse correctly in all type positions:

- Parameter type: `function f(new $x)` and `function f(new<string> $x)`
- Return type: `function f(): new<int>`
- Property type: `public new<string> $factory;`
- Generic constraint: `function f<T extends new<string>>()`
- Intersection type: `new<string> & Logger`
- Union type: `new<string> | null`
- Nested generics: `new<array<string>>` (constructor takes `array<string>`)
- No conflict with expression `new`: `$obj = new MyClass()` still parses

#### 7.2 Binder Tests

Test `ConstructableTypes` computation:

| Class Definition | Expected `ConstructableTypes` |
|-----------------|------------------------------|
| `class A {}` (no constructor) | `[new]` |
| `class B { public function __construct() {} }` | `[new]` |
| `class C { public function __construct(string $x) {} }` | `[new<string>]` |
| `class D { public function __construct(string $x, int $y) {} }` | `[new<string, int>]` |
| `class E { public function __construct(string $x, int $y = 0) {} }` | `[new<string>, new<string, int>]` |
| `class F { public function __construct(string $x = "", bool $b = false) {} }` | `[new, new<string>, new<string, bool>]` |
| `class G { public function __construct(mixed ...$args) {} }` | `[new]` |
| `abstract class H { public function __construct(string $x) {} }` | `[]` (abstract) |
| `interface I {}` | `[]` (interface) |
| `class J { private function __construct() {} }` | `[]` (private ctor) |
| `class K { protected function __construct() {} }` | `[]` (protected ctor) |
| `class L extends C {}` (no own ctor, inherits from C) | `[new<string>]` (inherited) |

#### 7.3 Checker Tests

Test constraint validation:

| Test Case | Expected Result |
|-----------|----------------|
| `create<A>()` where `T extends new` and A has no ctor | OK |
| `create<C>()` where `T extends new<string>` and C takes string | OK |
| `create<C>()` where `T extends new<int>` and C takes string | Error 4302 |
| `create<H>()` where `T extends new` and H is abstract | Error 4303 |
| `create<I>()` where `T extends new` and I is interface | Error 4301 |
| `create<J>()` where `T extends new` and J has private ctor | Error 4301 |
| `create<E>()` where `T extends new<string>` and E has `(string, int=0)` | OK (default expansion) |
| `create<E>()` where `T extends new` and E has `(string, int=0)` | Error 4301 (string is required) |
| `create<F>()` where `T extends new` and F has `(string="", bool=false)` | OK (all defaults) |
| `new<string>` assignable to `object` | True |
| `new<string>` assignable to `mixed` | True |
| Multiple constraints: `T extends new<string> & Logger` | Both checked independently |

#### 7.4 Emitter Tests

Test PHP output:

| Tyhp Input | Expected PHP Output |
|------------|-------------------|
| `function f(new $x): object` | `function f(object $x): object` |
| `function f(new<string, int> $x): object` | `function f(object $x): object` |
| `function f<T extends new<string>>(string $v): T` | Generic erasure → `function f(string $v): mixed` |
| `new & Logger` intersection | `Logger` (new stripped) |
| `new<string> \| null` union | `object \| null` (or `?object`) |

#### 7.5 End-to-End Tests

Create a comprehensive `Examples/NewType.tyhp` file and its expected `Examples/NewType.php` output:

- Verify the full pipeline: parse → bind → check → emit → write
- Verify no checker errors on valid code
- Verify expected checker errors on invalid code (using diagnostic assertions)
- Verify emitted PHP is valid and runnable

#### 7.6 Snapshot Tests

For each type expression involving `new<>`, create snapshot tests comparing:
- AST structure (parse output)
- Resolved checked types (checker output)
- Emitted PHP (emitter output)

### Acceptance Criteria

- [ ] All parser tests pass: `new` and `new<TArgs...>` parse in all type positions
- [ ] All binder tests pass: `ConstructableTypes` computed correctly for every class shape
- [ ] All checker tests pass: constraint validation produces correct errors/success
- [ ] All emitter tests pass: `new<>` correctly stripped from PHP output
- [ ] End-to-end tests pass: full pipeline from `.tyhp` to `.php`
- [ ] `Examples/NewType.tyhp` compiles without errors and produces expected PHP output
- [ ] No regressions in existing test suites
- [ ] Snapshot tests capture correct AST, type, and emission output

### Dependencies

- **Requires:** All previous phases (1–6) complete
- **Provides:** Confidence that the `new<>` feature works correctly across the entire pipeline

---

## New MessageCode Values

```csharp
#region Checker — new<> constructable type constraints (4300-4303)

CheckerCannotResolveStaticGenericType = 4300,    // "Cannot resolve generic type parameter '{0}' to a concrete type at this static call site"
CheckerTypeNotConstructable = 4301,              // "Type '{0}' does not satisfy 'new' constraint — it must be a concrete class with a public constructor"
CheckerConstructorSignatureMismatch = 4302,      // "Type '{0}' does not satisfy 'new<{1}>' — constructor signature does not match"
CheckerAbstractNotConstructable = 4303,          // "Abstract type '{0}' cannot satisfy 'new' constraint"

#endregion
```

Note: Story 27's canonical allocation is the **4300–4399 feature-checker band** (`4300–4303`), relocated out of the 4000–4211 range now owned contiguously by Story 08. Story 28's generic-default codes live at `4310–4312`; `4320–4324` belong to Story 16. Within this band, `CheckerCannotResolveStaticGenericType` is `4300` (it had previously collided with `CheckerAbstractNotConstructable`; that duplicate was resolved, then both were moved into the 4300 band).

---

## File Organization Summary

### New Files

```
Tyhp/TyhpLang/Checker/CheckedTypes/
└── NewCheckedType.cs              (~60 lines)

tests/Tyhp.Tests/NewType/
├── NewTypeParserTests.cs          (~150 lines)
├── NewTypeBinderTests.cs          (~200 lines)
├── NewTypeCheckerTests.cs         (~250 lines)
├── NewTypeEmitterTests.cs         (~100 lines)
└── NewTypeEndToEndTests.cs        (~150 lines)

Examples/
├── NewType.tyhp                   (~100 lines)
└── NewType.php                    (~80 lines)
```

### Modified Files

```
Tyhp/TyhpLang/Grammar/PhpParser.g4                             — Add T_NEW to typeWithoutStatic
Tyhp/TyhpLang/Grammar/.antlr/*                                 — Regenerated
Tyhp/TyhpLang/Parser/*                           — Regenerated ANTLR output
Tyhp/TyhpLang/Visitor/PhpParserAstVisitor.PhpTypes.cs — Handle NewType alternative
Tyhp/TyhpLang/Binder/BuiltIn/Types.cs            — Document and register new as built-in
Tyhp/TyhpLang/Binder/BuiltIn/Types.cs            — Register new as built-in
Tyhp/TyhpLang/Binder/Symbols/ObjectDeclarationSymbol.cs — Add ConstructableTypes
Tyhp/TyhpLang/Binder/TyhpBinder.cs               — Compute new<> facets
Tyhp/TyhpLang/Checker/TypeComparer.cs             — IsAssignableTo for NewCheckedType
Tyhp/TyhpLang/Checker/TypeInferrer.cs             — Resolve new<> type expressions
Tyhp/TyhpLang/Checker/TyhpChecker.TyhpFeatures.cs — CheckNewConstraint()
Tyhp/TyhpLang/Emitter/TyhpEmitter.cs              — Strip new<> from output
Tyhp/Domain/Exceptions/MessageCode.cs             — Add 4300-4303
Tyhp/Resources/CLI.TyhpHostedService.resx         — Add error strings (4300-4303)
```

---

## Cross-Cutting Concerns

### Interaction with `callable<>`

The `new<>` type and `callable<>` type are independent facets in the computed intersection type. A class that:
- Has a public constructor `__construct(string $name)` → gets `new<string>` facet
- Implements `__invoke(int $x): bool` → gets `callable<int, bool>` facet

These are separate and don't interact. A constraint `T extends new<string> & callable<int, bool>` requires BOTH.

### Interaction with Generic Type Parameters

When a generic parameter `T` is constrained by `new<TArgs...>`, and `TArgs` itself contains generic parameters from an outer scope, the checker must resolve these through the normal generic substitution mechanism:

```tyhp
class Factory<TArg> {
    public function create<T extends new<TArg>>(): T {
        // When Factory is instantiated as Factory<string>,
        // T's constraint becomes new<string>
    }
}
```

### Interaction with Tyhpdef

Tyhpdef files describe external PHP classes. The binder should compute `ConstructableTypes` for tyhpdef-declared classes just as it does for user-declared classes. This enables:

```tyhp
// DateTime has a public constructor: __construct(string $datetime = "now", ?DateTimeZone $timezone = null)
// So DateTime satisfies: new, new<string>, new<string, ?DateTimeZone>

function createDated<T extends new<string>>(string $date): T {
    return new T($date);
}

DateTime $dt = createDated<DateTime>("2026-01-01"); // OK
```

### Interaction with Enum Types

PHP enums cannot be instantiated with `new`. Backed enums have implicit `from()` and `tryFrom()` static methods, but these are not constructors. Enums should have `ConstructableTypes = []`.

### Interaction with Struct Types

Tyhp structs are value types backed by arrays at runtime. They have special construction semantics (`struct { ... }` literals) and do NOT use `new`. Structs should have `ConstructableTypes = []` and should NOT satisfy `new<>` constraints.

---

*Last updated: 2026-02-17*

---

## Human Testing and Verification

> These steps are for a human developer to manually verify the implementation works end-to-end. Steps can be skipped or modified as needed — they are guidelines, not a rigid checklist.

### Step 1: Verify `new` Parses in Type Positions

Create a file `test_new_type_parse.tyhp`:

```tyhp
<?tyhp

// As parameter type — bare new
function createDefault(new $constructable): object {
    return new $constructable();
}

// As parameter type — new with generic args
function createWithArgs(new<string, int> $constructable, string $name, int $id): object {
    return new $constructable($name, $id);
}

// As return type
function getFactory(): new<string> {
    return SomeClass::class;
}

// In generic constraint
function make<T extends new<string>>(string $value): T {
    return new T($value);
}

// In intersection type
function makeLogger<T extends new & Serializable>(): T {
    return new T();
}

// In union type (unusual but valid)
function maybeFactory(): new<string> | null {
    return null;
}

// As property type
class Container {
    public new<string> $factory;
}

// Expression-position new still works
class RegularClass {
    public function __construct() {}
}

RegularClass $obj = new RegularClass();
```

Run `tyhp lint test_new_type_parse.tyhp`. **Expected:** No parse errors. The `new` keyword is accepted in type positions and expression-position `new` still works without conflict.

### Step 2: Verify Computed `new<>` Facets on Classes

Create a file `test_new_type_facets.tyhp`:

```tyhp
<?tyhp

// No constructor — should have facet: new
class NoCtorClass {}

// All-default constructor — should have facets: new, new<string>
class AllDefaults {
    public function __construct(string $label = "default") {}
}

// One required, one optional — should have facets: new<string>, new<string, int>
class MixedParams {
    public function __construct(string $name, int $priority = 0) {}
}

// All required — should have facet: new<string, int, bool>
class AllRequired {
    public function __construct(string $a, int $b, bool $c) {}
}

// Zero-arg constructable
function testZeroArg<T extends new>(): T {
    return new T();
}

// One-arg constructable
function testOneArg<T extends new<string>>(string $val): T {
    return new T($val);
}

// Verify concrete types satisfy constraints
NoCtorClass $a = testZeroArg<NoCtorClass>();
AllDefaults $b = testZeroArg<AllDefaults>();
AllDefaults $c = testOneArg<AllDefaults>("hello");
MixedParams $d = testOneArg<MixedParams>("world");
```

Run `tyhp lint test_new_type_facets.tyhp`. **Expected:** No errors. Each call satisfies the `new<>` constraint based on the class's constructor signature and defaults.

### Step 3: Verify Checker Error Cases

Create a file `test_new_type_errors.tyhp`:

```tyhp
<?tyhp

abstract class AbstractBase {
    public function __construct(string $name) {}
}

interface Buildable {}

class PrivateCtorClass {
    private function __construct() {}
}

class RequiresString {
    public function __construct(string $x) {}
}

function createZeroArg<T extends new>(): T {
    return new T();
}

function createWithString<T extends new<string>>(string $val): T {
    return new T($val);
}

function createWithInt<T extends new<int>>(int $val): T {
    return new T($val);
}

// ERROR 4303: Abstract class cannot satisfy new constraint
createZeroArg<AbstractBase>();

// ERROR 4301: Interface cannot satisfy new constraint
createZeroArg<Buildable>();

// ERROR 4301: Private constructor cannot satisfy new constraint
createZeroArg<PrivateCtorClass>();

// ERROR 4301: RequiresString has no zero-arg constructor
createZeroArg<RequiresString>();

// ERROR 4302: Constructor takes string, not int
createWithInt<RequiresString>(42);
```

Run `tyhp lint test_new_type_errors.tyhp`. **Expected:** Errors on each marked line:
- `AbstractBase` — error 4303
- `Buildable` — error 4301
- `PrivateCtorClass` — error 4301
- `RequiresString` with `new` — error 4301 (no zero-arg constructor)
- `RequiresString` with `new<int>` — error 4302 (signature mismatch)

### Step 4: Verify Emitted PHP Output

Compile `test_new_type_facets.tyhp` (the valid file from Step 2) and inspect the emitted PHP. **Expected:**

- All `new` and `new<...>` types in parameter positions are replaced with `object`:
  ```php
  function createDefault(object $constructable): object { ... }
  function createWithArgs(object $constructable, string $name, int $id): object { ... }
  ```
- Generic parameters with `new<>` constraints are erased entirely (standard generic erasure):
  ```php
  function testZeroArg(): mixed { ... }
  function testOneArg(string $val): mixed { ... }
  ```
- Verify no `new<` appears in any type position in the emitted PHP
- Verify the emitted PHP is syntactically valid by running `php -l <output>.php`

### Step 5: Verify Runtime Behavior

Create a file `test_new_type_runtime.tyhp`:

```tyhp
<?tyhp

class Greeter {
    private string $greeting;

    public function __construct(string $greeting) {
        $this->greeting = $greeting;
    }

    public function greet(string $name): string {
        return $this->greeting . ', ' . $name . '!';
    }
}

class DefaultGreeter {
    public function greet(): string {
        return 'Hello, World!';
    }
}

function createAndGreet<T extends new<string>>(string $greeting, string $name): string {
    T $instance = new T($greeting);
    return $instance->greet($name);
}

function createDefault<T extends new>(): T {
    return new T();
}

echo createAndGreet<Greeter>('Hi', 'Alice') . "\n";

DefaultGreeter $g = createDefault<DefaultGreeter>();
echo $g->greet() . "\n";
```

Compile with `tyhp build`, then run with `php <output>.php`. **Expected output:**

```
Hi, Alice!
Hello, World!
```

### Step 6: Verify `new` in Intersection Types

Create a file `test_new_intersection.tyhp`:

```tyhp
<?tyhp

interface Logger {
    public function log(string $message): void;
}

class ConsoleLogger implements Logger {
    public function __construct() {}

    public function log(string $message): void {
        echo $message . "\n";
    }
}

function makeLogger<T extends new & Logger>(): T {
    T $logger = new T();
    $logger->log('Logger created');
    return $logger;
}

ConsoleLogger $logger = makeLogger<ConsoleLogger>();
$logger->log('Working!');
```

Compile and run. **Expected output:**

```
Logger created
Working!
```

Also verify that the `new` component is stripped from the intersection in the emitted PHP — the parameter should just be typed as `Logger` (or `object` if that's how intersection erasure works).

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
