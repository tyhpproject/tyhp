# Implementation Plan: Story 02 — Binder (Name Resolution & Scope Building)

> **Roadmap position:** Story 02 — **Tier 0 — Spine**
> **Direct dependencies (new numbering):** 01
> **Renumbered from:** legacy Story 1
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 02 of the Tyhp compiler TODO
> **Branch:** TBD
> **Prerequisite:** Story 01 complete (diagnostic system with `DiagnosticBag`, `IDiagnostic`, `CompilationResult`, `CompilationService`, `BuildAction` skeleton, `TyhpAntlrErrorListener`, visitor error handling refactor)
> **Key Dependency:** The binder depends on `DiagnosticBag` from Story 01 for all error reporting. The `CompilationService` pipeline will call the binder after parsing.
> **Status:** SUBSTANTIALLY COMPLETE (2026-07-31 audit) — two-pass `TyhpBinder`, scopes/symbols, and tyhpdef loading are functional. Residual integrity-check / fixture notes in `INCOMPLETE.md`.

---

## Architecture Overview

### What the Binder Does

The binder is the bridge between parsing and type checking. It takes the flat list of parsed `SrcFileAst` trees (one per source file) and produces a unified **scope tree** rooted at a `GlobalScope`. Each source file produces a `FileScope` under the `GlobalScope`, and namespace declarations within each file create `NamespaceScope` entries (merged across files when the same namespace appears in multiple files). During this walk, it:

1. **Registers declarations** — Every namespace, class, interface, trait, enum, struct, extension, function, method, property, constant, variable, type alias, and generic parameter becomes a **symbol** stored in the appropriate scope.
2. **Builds the scope hierarchy** — Scopes nest: `GlobalScope` → `FileScope` → `NamespaceScope` → `NamespaceBlockScope` → `ObjectDeclarationScope` → method scopes → `CodeBlockScope`, etc. Each source file gets its own `FileScope` that captures file-level constructs (declare statements, use imports outside namespace blocks, file-level variables/constants) before delegating to namespace scopes.
3. **Resolves name references** — Every identifier usage (variable reads, function calls, type references, `use` imports, `self`/`static`/`parent`) is linked back to its declaring symbol.
4. **Loads external type information** — Tyhpdef files describing PHP extensions and Composer packages are parsed and their symbols are registered in the `GlobalScope` before user code is bound.

### Relationship to Existing Scaffolding

The TODO document's architectural note flagged that **"Everything from Story 02 onward is subject to complete replacement,"** and the binder directory began as early scaffolding (class shells with `// TODO` bodies). In practice the existing structure proved workable and was **extended rather than replaced**: the `BaseScope<TParent, TDeclarationSymbol, TChildScopes, TChildSymbols, TSelf>` generic base (5 type parameters, each concrete scope parameterizes all 5) and the interface hierarchy (`IBaseScope`, `IGlobalScopeChild`, `ICodeBlockScopeParent`, 20+ scope/symbol relationship interfaces) were retained. As a result the binder is now substantially implemented — see **Appendix A** for the per-file status (the scope, symbol, and built-in classes are populated and the two-pass `TyhpBinder` is functional).

**Strategy (as resolved):** The scaffolding was kept and extended. `BaseSymbol` was populated with the required properties and binding logic was added to `TyhpBinder` working with the existing class structure, rather than swapping in a simpler design. The plan below describes *what* was accomplished; Appendix A records the resulting state of each file.

### Binder Pipeline Position

```
Parse (done) → [Story 01: CompilationService] → BIND (this phase) → Check (Story 08) → Emit (Story 09)
                                                  ↑
                                            Tyhpdef loading
                                            Built-in population
```

The `CompilationService` created in Story 01 will call the binder after parsing completes. The `CompilationResult.GlobalScope` property (from Story 01) will store the bound scope tree. The `BuildAction` skeleton (from Story 01) has a `// PLACEHOLDER_STORY_02: Run binder` step that this phase fills in.

### Scope Hierarchy Diagram

The complete scope tree structure with `FileScope`:

```
GlobalScope (ScopeType.Root)
├── FileScope[] (ScopeType.File) — one per source file
│   ├── FileSymbol — FileName, FileHash, FileDeclareDirectives
│   ├── UseIncludeSymbol[] — file-level use/import statements
│   ├── ConstantSymbol[] — file-level const declarations
│   ├── VariableSymbol[] — file-level variable assignments
│   ├── CodeBlockScope[] — file-level code blocks (if/for/etc.)
│   ├── DeclareBlockScope[] — file-level declare blocks
│   ├── ObjectDeclarationScope[] — file-level classes (global namespace)
│   └── FunctionDeclarationScope[] — file-level functions (global namespace)
├── NamespaceScope[] (ScopeType.Namespace) — merged across files
│   └── NamespaceBlockScope[] (ScopeType.NamespaceBlock) — one per namespace { } block
│       ├── UseIncludeSymbol[] — namespace-level use/import statements
│       ├── TypeAliasSymbol[]
│       ├── ConstantSymbol[]
│       ├── VariableSymbol[]
│       ├── CodeBlockScope[]
│       ├── DeclareBlockScope[]
│       ├── LabelScope[]
│       ├── ObjectDeclarationScope[]
│       └── FunctionDeclarationScope[]
├── BuiltInTypeSymbol[] — int, string, float, etc.
├── MagicConstantSymbol[] — __FILE__, __LINE__, etc.
└── SuperGlobalSymbol[] — $_SESSION, $_POST, etc.
```

**Key relationships:**
- `FileScope` is a child of `GlobalScope` — one per parsed source file (`SrcFileAst`)
- `NamespaceScope` is also a child of `GlobalScope` (not `FileScope`) — this enables namespace merging across files
- Each `NamespaceBlockScope` holds a back-reference to its owning `FileScope` for file-level context (declare directives, file-level use imports)
- File-level constructs (before/outside namespace blocks) live in `FileScope`
- Namespace-level constructs (inside namespace blocks) live in `NamespaceBlockScope`

### Key Design Decisions for the Implementor

1. **Symbol hierarchy granularity:** The current scaffolding has 50+ symbol subclasses including separate classes for each PHP magic method (`ObjectMagicGetMethodSymbol`, `ObjectMagicSetMethodSymbol`, etc.). Consider consolidating: a single `ObjectMethodSymbol` with a `MagicMethodKind` enum (or nullable `SymbolType` discriminator) may be simpler. The existing `SymbolType` enum already distinguishes all these cases.

2. **Scope interface hierarchy:** The current design has 20+ scope relationship interfaces (`IGlobalScopeChild`, `INamespaceScopeChild`, `ICodeBlockScopeChild`, `ICodeBlockScopeParent`, etc.). These enforce compile-time parent/child type constraints. Evaluate whether this gives enough benefit to justify the complexity, or whether a simpler `IScope` with runtime child validation (using `SymbolTypeHelper.GetAllowedChildren()` which already exists) is sufficient.

3. **Two-pass vs. single-pass binding:** PHP allows forward references (a class can reference another class declared later in the same file or in a different file). The binder likely needs two passes: (a) a declaration pass that registers all top-level symbols, and (b) a resolution pass that resolves references. Alternatively, bind all files' declarations first, then resolve references across all files.

4. **Multi-file namespace merging:** PHP allows the same namespace to be split across multiple files. When binding multiple files, `NamespaceScope` instances with the same fully-qualified name should be merged so that declarations from different files are visible to each other.

5. **FileScope as an intermediate scope:** The binder readme already identifies "file" as a scope type with "only use imports as symbols." The `FileScope` sits between `GlobalScope` and `NamespaceScope`, one per `SrcFileAst`. It captures file-level constructs that are identifiable in the grammar and AST structure:
   - **`declare()` statements** — File-level `declare(strict_types=1)` and similar directives that appear before or outside namespace blocks. In the grammar, these are `topStatement` items (via `statementWithoutTerminal` → `PhpDeclareAst`) that precede any namespace declaration. The `EmitType.FileDeclare` enum value already recognizes this concept.
   - **File-level `use`/`import` statements** — Use statements that appear before or outside namespace blocks (valid in PHP when not using block namespace syntax). In the AST, these appear as `PhpImportDeclListAst` / `PhpImportDeclAst` children of `PhpTopStatementListAst` at the file level.
   - **File-level variables** — Variables assigned at the top level outside any namespace block (e.g., `$config = loadConfig();`).
   - **File-level constants** — Constants defined at the top level outside any namespace block (e.g., `const VERSION = '1.0';`).
   - **File-level code blocks** — Top-level if/for/while statements outside namespace blocks.
   
   The `FileScope` does NOT own `NamespaceScope` instances directly — `NamespaceScope` instances live at the `GlobalScope` level for merging purposes. Instead, the `FileScope` is the parent of `NamespaceBlockScope` instances created from namespace declarations in that file. The `FileScope` also holds a reference to the `SrcFileAst` for source location tracking. This design allows namespace merging to remain straightforward while giving each file its own scope for file-level constructs.

### File Organization Guidance

The binder implementation should be organized into multiple smaller files:

| Directory | Purpose |
|-----------|---------|
| `Tyhp/TyhpLang/Binder/` | Core binder classes (`TyhpBinder.cs`, `SymbolTree.cs`, `SymbolIdentifier.cs`) |
| `Tyhp/TyhpLang/Binder/Scopes/` | Scope class hierarchy including `FileScope` (existing or replaced) |
| `Tyhp/TyhpLang/Binder/Symbols/` | Symbol class hierarchy including `FileSymbol` (existing or replaced) |
| `Tyhp/TyhpLang/Binder/BuiltIn/` | Built-in type/constant/variable population + tyhpdef loading |
| `Tyhp/TyhpLang/Binder/Resolution/` | Name resolution logic (new — keep resolution separate from declaration) |

Keep individual files under 500 lines. If `TyhpBinder` grows large, split binding logic for different AST node types into partial classes (similar to how the visitor uses `PhpParserAstVisitor.PhpObjects.cs`, `PhpParserAstVisitor.PhpStatements.cs`, etc.).

### Safety Guidance

Before making any potentially destructive changes to existing files:

- **Back up before replacing:** If replacing the entire scope/symbol hierarchy, create timestamped backups of the existing `Scopes/` and `Symbols/` directories first (e.g., `Scopes.20260216_120000.backup/`).
- **Incremental edits preferred:** Prefer modifying `BaseSymbol.cs` to add properties over replacing the entire file. Prefer adding methods to `TyhpBinder.cs` over wholesale replacement.
- **Never use destructive git commands:** Do not `git reset`, `git checkout .`, or `git clean`. There may be uncommitted Story 01 work in the repository.
- **Backup files are sacred:** Never delete or modify backup files.

---

## Phase 1: Implement BaseSymbol Properties and Symbol Data Model




### Phase Overview

Populate `BaseSymbol` (currently an empty class with `// TODO`) with the core properties that every symbol needs: name, fully-qualified name, AST node reference, containing scope, visibility, source location, and documentation. Then implement the additional data properties on each concrete symbol subclass that the binder and later phases need.

### Deliverables

- `BaseSymbol` with all core properties populated
- All concrete symbol subclasses with their type-specific properties
- `IBaseSymbol` interface updated to expose queryable properties
- New `SymbolType` discriminator on `BaseSymbol` enabling runtime type checking without downcasting
- Decision documented (via code comments) on whether to keep the granular magic method symbol classes or consolidate

### Implementation Details

#### 1.1 Update `IBaseSymbol` Interface

**File:** `Tyhp/TyhpLang/Binder/Symbols/Interfaces/IBaseSymbol.cs`

The current interface is empty. Add the minimum contract that all consumers of symbols need:

- `string Name { get; }` — the declared name
- `string FullyQualifiedName { get; }` — namespace-qualified name (e.g., `\App\Models\User`)
- `SymbolType SymbolType { get; }` — discriminator from the existing `Tyhp.TyhpLang.Enum.SymbolType` enum
- `IBaseScope? ContainingScope { get; set; }` — the scope this symbol lives in
- `string SourceFile { get; }` — which file declared this symbol
- `int Line { get; }` — source line of declaration
- `int Column { get; }` — source column of declaration

Do NOT add AST-specific properties to the interface — keep it lean. The concrete `BaseSymbol` class can have additional properties.

#### 1.2 Populate `BaseSymbol` Properties

**File:** `Tyhp/TyhpLang/Binder/Symbols/BaseSymbol.cs`

Add the following properties and a constructor/initialization pattern:

- `Name` (string) — the declared name of the symbol
- `FullyQualifiedName` (string) — computed from `ContainingScope` namespace path + `Name`
- `DeclaringAstNode` (`IBase2Ast?`) — reference back to the AST node that declared this symbol. Nullable because built-in symbols have no AST node.
- `ContainingScope` (`IBaseScope?`) — set when the symbol is added to a scope (the existing `BaseScope.AddChildSymbol()` has a `// TODO: assign scope to symbol` comment — this is where it gets set)
- `SymbolType` (`SymbolType`) — from the existing enum, set by each subclass constructor
- `Visibility` (`MemberModifier`) — using the existing `MemberModifier` flags enum. Defaults to `MemberModifier.None` for symbols without visibility (namespaces, labels, etc.)
- `IsDeprecated` (bool) — from `deprecated` keyword in tyhpdef or attribute
- `IsObsolete` (bool) — from `obsolete` keyword in tyhpdef or attribute
- `DocComment` (string?) — associated documentation comment extracted from AST
- `SourceFile` (string) — which file this symbol was declared in, extracted from `SrcFileAst.FileName`
- `Line` (int) — source location from `IBase2Ast.Line`
- `Column` (int) — source location from `IBase2Ast.Column`

Add a protected constructor that subclasses call:

```csharp
protected BaseSymbol(string name, SymbolType symbolType, IBase2Ast? declaringNode = null, string sourceFile = "", MemberModifier visibility = MemberModifier.None)
```

The `FullyQualifiedName` should be computed lazily or set explicitly when the symbol is added to a scope.

#### 1.3 Update `BaseScope.AddChildSymbol()` to Set Containing Scope

**File:** `Tyhp/TyhpLang/Binder/Scopes/BaseScope.cs`

The existing method has `// TODO: assign scope to symbol`. Implement:

- Set `child.ContainingScope = this` (cast to `IBaseScope`)
- If the child is a `BaseSymbol`, also compute and set `FullyQualifiedName` based on the scope's namespace path

#### 1.4 Implement Concrete Symbol Subclass Properties

Each symbol subclass needs its specific data. All subclass constructors should call `base(name, symbolType, astNode, sourceFile, visibility)`.

**Core declaration symbols (most important — used in binding walk):**

- `ObjectDeclarationSymbol` — add: `PhpTypeDeclType ObjectKind` (class/interface/trait/enum, using existing enum), `bool IsStruct`, `bool IsExtension`, `List<GenericTypeParameterSymbol> GenericParameters`, `ITypeExpression? ExtendsType` (AST reference), `List<ITypeExpression> ImplementsTypes`, `Dictionary<string, IBaseSymbol> Members` (quick name→symbol lookup for member resolution)
- `FunctionDeclarationSymbol` — add: `List<ParameterInfo> Parameters` (create a simple `ParameterInfo` record: name, type AST, default value AST, is-variadic, is-by-reference, promoted-visibility), `ITypeExpression? ReturnType`, `List<GenericTypeParameterSymbol> GenericParameters`, `bool IsGenerator`, `bool IsAsync`
- `VariableSymbol` — add: `ITypeExpression? DeclaredType` (AST reference), `bool IsParameter`, `IExpression? DefaultValue` (AST reference), `bool IsDisposable` (`:=` assignment), `bool IsPromotedProperty`
- `FileSymbol` — add: `string FileName` (from `SrcFileAst.FileName`), `string FileHash` (from `SrcFileAst.FileHash`), `Dictionary<string, string> FileDeclareDirectives` (file-level declare directives like `strict_types`), `SymbolType` = `SymbolType.File`. The `FileSymbol` is the declaration symbol for `FileScope` — one per source file. It tracks file-level metadata that other scopes may reference (e.g., whether `strict_types` is active for code in this file).
- `NamespaceSymbol` — already has `Name`; ensure it has `SymbolType.Namespace` set
- `NamespaceBlockSymbol` — add: `bool IsAnonymous` (no name = anonymous namespace block)

**Object member symbols:**

- `ObjectMethodSymbol` — add: same as `FunctionDeclarationSymbol` plus `bool IsAbstract`, `bool IsStatic`, `MagicMethodKind? MagicKind` (nullable enum — null for regular methods). If consolidating magic method symbols, this replaces all 15 `ObjectMagic*MethodSymbol` classes.
- `ObjectPropertySymbol` — add: `ITypeExpression? DeclaredType`, `IExpression? DefaultValue`, `bool HasAccessor`, `AccessorType? AccessorKind` (from existing `AccessorType` enum)
- `ObjectConstantSymbol` — add: `ITypeExpression? DeclaredType`, `IExpression? ValueExpression`
- `ObjectConstructorMethodSymbol` — add: same as method plus `List<VariableSymbol> PromotedProperties`
- `ObjectDestructorMethodSymbol` — add: minimal, no params
- `ObjectAccessorMethodSymbol` — add: `AccessorType AccessorKind`, `ObjectPropertySymbol AssociatedProperty`
- `ObjectOperatorOverloadMethodSymbol` — add: `OverloadableOperator Operator` (from existing enum), parameters, return type
- `ObjectTypeAliasSymbol` — add: `ITypeExpression AliasedType`, `List<GenericTypeParameterSymbol> GenericParameters`

**Other symbols:**

- `TypeAliasSymbol` — add: `ITypeExpression AliasedType`, `List<GenericTypeParameterSymbol> GenericParameters`
- `GenericTypeParameterSymbol` — add: `ITypeExpression? Constraint` (extends clause), `bool IsCovariant`, `bool IsContravariant`, `ITypeExpression? DefaultType` (SEE: IMPLEMENTATION_PLAN_TODO_STORY_28.md for generic default support)
- `ConstantSymbol` — add: `ITypeExpression? DeclaredType`, `IExpression? ValueExpression`
- `LabelSymbol` — add: label name only (inherited from `BaseSymbol`)
- `UseIncludeSymbol` — add: `string ImportedName` (fully-qualified), `string? AliasName`, `PhpUseType UseType` (from existing enum: class/function/constant)
- `DeclareBlockSymbol` — add: `Dictionary<string, string> Directives`. Note: file-level (non-block) declare directives are stored on `FileSymbol.FileDeclareDirectives` instead.
- `AnonymousFunctionSymbol` — add: `List<VariableSymbol> CapturedVariables` (use clause), parameter list, return type, generic parameters
- `AnonymousObjectDeclarationSymbol` — add: same as `ObjectDeclarationSymbol` minus name
- `CodeBlockSymbol` — add: `ScopeType BlockType` or a new `CodeBlockKind` enum (if/else/for/foreach/while/do-while/switch/try/catch/finally/match)
- `BuiltInTypeSymbol` — already has `Name`; add `SymbolType` = `SymbolType.BuiltInType`
- `MagicConstantSymbol` — already has `Name`; add `SymbolType` = `SymbolType.MagicConstant`
- `SuperGlobalSymbol` — already has `Name` (with `$` prefix); add `SymbolType` = `SymbolType.Variable`

#### 1.5 Create Helper Types

**New file:** `Tyhp/TyhpLang/Binder/Symbols/ParameterInfo.cs`

A lightweight record for function/method parameter metadata:

- `string Name`
- `ITypeExpression? DeclaredType`
- `IExpression? DefaultValue`
- `bool IsVariadic`
- `bool IsByReference`
- `MemberModifier PromotedVisibility` (for constructor promotion — `None` if not promoted)

**New file (if consolidating magic methods):** `Tyhp/TyhpLang/Binder/Symbols/MagicMethodKind.cs`

An enum with values: `Call`, `CallStatic`, `Get`, `Set`, `Isset`, `Unset`, `Sleep`, `Wakeup`, `Serialize`, `Unserialize`, `ToString`, `Invoke`, `SetState`, `Clone`, `DebugInfo`

#### 1.6 Update `ScopeType` and `SymbolType` Enums for FileScope

**File:** `Tyhp/TyhpLang/Enum/ScopeType.cs`

Add `File` to the `ScopeType` enum (between `Root` and `Namespace`):

- `File` — represents a single source file's scope

**File:** `Tyhp/TyhpLang/Enum/SymbolType.cs`

Add `File` to the `SymbolType` enum:

- `File` — the declaration symbol type for `FileScope`

Update `SymbolTypeHelper`:
- Add `GetFileScopeTypes()` returning `{ SymbolType.File }`
- Add `IsFileScope(SymbolType)` helper
- Update `GetScopeType()` switch to map `SymbolType.File` → `ScopeType.File`
- Update `GetAllowedChildren()`:
  - `SymbolType.Root` should include `SymbolType.File` (multiple allowed) in addition to existing `Namespace`, `AnonymousObjectDeclaration`, `AnonymousFunction`
  - Add a new entry for `SymbolType.File` that allows: `UseInclude`, `Variable`, `Constant`, `CodeBlock`, `DeclareBlock`, `Label`, `ObjectDeclaration`, `FunctionDeclaration` (similar to `NamespaceBlock` but also includes file-level constructs)

### Acceptance Criteria

- [x] `BaseSymbol` has all core properties populated and is no longer an empty `// TODO` class
- [x] `IBaseSymbol` exposes `Name`, `FullyQualifiedName`, `SymbolType`, `ContainingScope`, `SourceFile`, `Line`, `Column`
- [x] `BaseScope.AddChildSymbol()` sets `ContainingScope` on the added symbol
- [x] All concrete symbol subclasses have their type-specific properties (not just empty constructors)
- [x] Each symbol's constructor sets its `SymbolType` discriminator from the existing enum
- [x] `FileSymbol` exists with `FileName`, `FileHash`, `FileDeclareDirectives`, and `SymbolType.File`
- [x] `ScopeType.File` and `SymbolType.File` are added to their respective enums
- [x] `SymbolTypeHelper.GetAllowedChildren(SymbolType.Root)` includes `SymbolType.File`
- [x] `SymbolTypeHelper.GetAllowedChildren(SymbolType.File)` returns the correct child types for file-level scope
- [x] The existing `BuiltInTypeSymbol`, `MagicConstantSymbol`, `SuperGlobalSymbol` constructors still work (backward compatible — they already have `Name`, just need `SymbolType` added)
- [x] `Types.PopulateGlobal()`, `Constants.PopulateGlobal()`, `Variables.PopulateGlobal()` still compile and work after symbol changes
- [x] A `ParameterInfo` record type exists for function/method parameter metadata
- [x] The project compiles with no errors after all symbol changes

### Dependencies

- Story 01 must be complete (the binder uses `DiagnosticBag` for error reporting)
- Existing AST interfaces (`IBase2Ast`, `ITypeExpression`, `IExpression`) must remain stable
- Existing enum types (`SymbolType`, `MemberModifier`, `OverloadableOperator`, `AccessorType`, `PhpTypeDeclType`, `PhpUseType`) are used directly — `SymbolType` and `ScopeType` must be extended with `File` entries

---

## Phase 2: Implement the Binding Walk — Declaration Pass




### Phase Overview

Create the core binding logic in `TyhpBinder` that walks AST trees and populates the scope/symbol hierarchy. This is the declaration pass: it visits every AST node that introduces a new name (namespace, class, function, variable, etc.) and creates the corresponding symbol + scope. References are NOT resolved in this pass — that happens in Phase 3.

### Deliverables

- `TyhpBinder` class with a functional `Bind()` method that accepts parsed ASTs and produces a populated `GlobalScope`
- `FileScope` creation for each source file, capturing file-level constructs
- Declaration binding for all top-level constructs: namespaces, classes, interfaces, traits, enums, structs, extensions, functions, constants
- Declaration binding for all class/object members: methods, properties, constants, type aliases, trait uses, constructor promotion
- Declaration binding for all code-block-level constructs: variables, labels, closures, anonymous classes
- Multi-file binding with namespace merging
- All binding errors reported to `DiagnosticBag` (duplicate declarations, invalid scope nesting, etc.)

### Implementation Details

#### 2.1 Redesign `TyhpBinder` Entry Point

**File:** `Tyhp/TyhpLang/Binder/TyhpBinder.cs`

Replace the current stubbed class. The binder needs:

- Constructor accepting `DiagnosticBag` (from Story 01)
- A `GlobalScope Bind(IEnumerable<SrcFileAst> parsedFiles)` method as the main entry point
- Internal state: current scope reference (stack or single mutable reference), current source file name

The binding strategy should be two-pass:
1. **Pass 1 (Declaration):** Walk all files. For each file, create a `FileScope` under `GlobalScope` with a `FileSymbol`. Then walk the file's top-level statements: file-level constructs (declare, use, variables, constants outside namespace blocks) are registered in the `FileScope`; namespace declarations create `NamespaceScope` entries (at `GlobalScope` level, for merging) and `NamespaceBlockScope` entries under the appropriate `NamespaceScope`. Declarations within namespace blocks go into their `NamespaceBlockScope`. This handles forward references — a class in file A can reference a class in file B.
2. **Pass 2 (Resolution):** Walk all files again, resolving every name reference to its symbol. (Implemented in Phase 3 of this plan.)

For Pass 1, the binder walks each `SrcFileAst`'s children and dispatches based on AST node type.

#### 2.2 Implement the GlobalScope Population and FileScope Creation

Before binding user code, populate the `GlobalScope` with built-in symbols:

- Call existing `Types.PopulateGlobal(globalScope)` — registers PHP/Tyhp built-in types
- Call existing `Constants.PopulateGlobal(globalScope)` — registers magic constants
- Call existing `Variables.PopulateGlobal(globalScope)` — registers superglobals
- `// PLACEHOLDER_PHASE_4: Load tyhpdef symbols` — tyhpdef loading is Phase 4 of this implementation plan

Then, for each `SrcFileAst`, create a `FileScope`:

- Create a `FileSymbol` with `FileName = srcFile.FileName`, `FileHash = srcFile.FileHash`
- Create a `FileScope` under `GlobalScope` with the `FileSymbol` as its declaration symbol
- Set `currentFileScope = fileScope` — all subsequent binding for this file uses the `FileScope` as the starting parent scope
- The `FileScope` corresponds 1:1 with a `SrcFileAst` (i.e., `PhpSrcFileAst` or `TyhpSrcFileAst` in the AST). The grammar rules `phpSrcFile` / `tyhpSrcFile` define the file root, and their `topStatementList` children are what the binder walks.

#### 2.3 Implement Top-Level Statement Binding

The binder needs to handle the top-level AST node types that appear as children of `SrcFileAst`. The exact AST class names come from the visitor's output. The binder should use a dispatcher pattern:

**For each `SrcFileAst` in the parsed files:**

- Create a `FileScope` for this file (see Section 2.2)
- Track `currentFileName = srcFile.FileName`
- Walk `srcFile.Children` (via `PhpTopStatementListAst` or equivalent) and dispatch based on AST node type
- Top-level statements that appear **before or outside** a namespace declaration are bound to the `FileScope`
- Top-level statements that appear **inside** a namespace block are bound to the `NamespaceBlockScope`

**File-level constructs (bound to `FileScope`):**

The grammar's `topStatement` rule allows several constructs before/outside namespace declarations. These are identifiable in the AST because they are direct children of the file's top statement list and appear before any `PhpBlockNamespaceDeclAst`:

- **Declare statements (non-block):** File-level `declare(strict_types=1)` — store as `FileSymbol.FileDeclareDirectives`. These apply to the entire file. In the grammar, these come from `statementWithoutTerminal` → `declareStatement`. The `EmitType.FileDeclare` enum value already identifies this pattern.
- **Use/Import statements:** File-level `use` statements (valid when not using block namespace syntax) — create `UseIncludeSymbol` in the `FileScope`. These are identifiable as `PhpImportDeclAst` / `PhpImportDeclListAst` nodes at the file's top statement level.
- **Variable assignments:** File-level variables like `$config = loadConfig();` — create `VariableSymbol` in the `FileScope`.
- **Constant declarations:** File-level `const VERSION = '1.0';` — create `ConstantSymbol` in the `FileScope`.
- **Code blocks:** File-level if/for/while statements — create `CodeBlockScope` under `FileScope`.
- **Declare blocks:** Block-syntax `declare(encoding='UTF-8') { ... }` — create `DeclareBlockScope` under `FileScope`.

**Namespace declarations** (e.g., `PhpBlockNamespaceDeclAst` or similar):
- Extract the namespace name from the AST
- Check if a `NamespaceScope` with this name already exists in `GlobalScope` (namespace merging)
- If not, create a new `NamespaceScope` + `NamespaceSymbol` and add to `GlobalScope`
- Create a `NamespaceBlockScope` + `NamespaceBlockSymbol` under the `NamespaceScope` for this specific block
- The `NamespaceBlockScope` should also track which `FileScope` it belongs to (for resolving file-level use imports and declare directives)
- Recursively bind the namespace block's children

**Class/Interface/Trait/Enum declarations** (AST nodes for type declarations):
- Create `ObjectDeclarationSymbol` with name, object kind, visibility/modifiers, generic parameters, extends/implements references
- Create `ObjectDeclarationScope` under the appropriate parent scope (either `NamespaceBlockScope` if inside a namespace, or `FileScope` if at file level in the global namespace)
- Bind the class body: methods, properties, constants, trait uses, type aliases (see Section 2.4)
- Handle constructor promotion: parameters with visibility modifiers create `ObjectPropertySymbol` entries

**Struct declarations** (Tyhp-specific):
- Same as class declarations but with `IsStruct = true` on the `ObjectDeclarationSymbol`
- Struct members are properties only (no methods in the traditional sense)

**Extension declarations** (Tyhp-specific):
- Same as class declarations but with `IsExtension = true`
- Extensions have a target type (the type being extended)

**Function declarations:**
- Create `FunctionDeclarationSymbol` with name, parameters, return type, generic parameters
- Create `FunctionDeclarationScope` under the appropriate parent scope
- Bind the function body (code block with variable declarations)

**Constant declarations:**
- Create `ConstantSymbol` with name, type, value expression
- Add to current scope (`FileScope` if file-level, `NamespaceBlockScope` if inside namespace)

**Use/Import statements:**
- Create `UseIncludeSymbol` with imported name, alias, and use type (class/function/constant)
- Add to current scope (`FileScope` if file-level, `NamespaceBlockScope` if inside namespace block)
- These are used during name resolution (Phase 3)

**Declare statements:**
- If file-level non-block syntax: store on `FileSymbol.FileDeclareDirectives` (applies to entire file)
- If block syntax: create `DeclareBlockScope` + `DeclareBlockSymbol` under current scope
- If non-block inside namespace: note the directive on the current namespace block scope

#### 2.4 Implement Object Body Binding

When inside an `ObjectDeclarationScope`, bind all class body members:

**Method declarations:**
- Determine if instance or static from modifiers
- Create the appropriate method symbol (`ObjectMethodSymbol` or consolidated magic method)
- Create `InstanceMethodDeclarationScope` or `StaticMethodDeclarationScope`
- Bind parameters as `VariableSymbol` entries in the method scope
- Bind the method body (code block)

**Property declarations:**
- Create `ObjectPropertySymbol` with name, type, default value, accessor info
- If the property has accessor declarations (get/set hooks), create `ObjectAccessorMethodSymbol` for each

**Class constant declarations:**
- Create `ObjectConstantSymbol` with name, type, value
- For enum cases: create as `ObjectConstantSymbol` with the enum backing type

**Trait use statements:**
- Record which traits are used
- Process trait adaptations (aliases, precedence rules) — store these on the `ObjectDeclarationSymbol` for resolution in Phase 3

**Type alias declarations (inside class):**
- Create `ObjectTypeAliasSymbol` with alias name, aliased type, generic parameters

**Constructor promotion:**
- When binding a constructor's parameter list, check each parameter for visibility modifiers
- If a parameter has `public`, `protected`, `private`, or `readonly`, also create an `ObjectPropertySymbol` on the containing class scope
- Set `IsPromotedProperty = true` on the `VariableSymbol` (parameter) and link to the property

**Operator overload declarations:**
- Create `ObjectOperatorOverloadMethodSymbol` with the overloaded operator and parameter/return types

#### 2.5 Implement Code Block and Variable Binding

**Code blocks** (if/else, for, foreach, while, switch, try/catch/finally, match):
- Create `CodeBlockScope` + `CodeBlockSymbol` under the current scope
- Recursively bind the block's statements

**Variable assignments/declarations:**
- When encountering an assignment expression where the left side is a simple variable:
  - Check if a `VariableSymbol` already exists in the current scope chain
  - If not, create a new `VariableSymbol` in the current scope
  - In Tyhp mode: variables should have explicit type declarations or be inferable
  - In PHP mode: variables are dynamically typed, just track the name
- Handle `global $var` statements — create a reference to the global scope variable
- Handle `static $var` statements — create a static local variable symbol

**Label statements:**
- Create `LabelScope` + `LabelSymbol` under the current scope

**Anonymous functions/closures:**
- Create `AnonymousFunctionSymbol` + `AnonymousFunctionScope`
- Bind `use ($var1, $var2)` captured variables
- Bind parameters and body

**Anonymous classes:**
- Create `AnonymousObjectDeclarationSymbol` + `AnonymousObjectDeclarationScope`
- Bind class body as in Section 2.4

#### 2.6 Implement Multi-File Binding and Namespace Merging

**File:** `TyhpBinder.cs` or `Tyhp/TyhpLang/Binder/NamespaceMerger.cs` (if extracted)

When binding multiple files:

- Each file gets its own `FileScope` under `GlobalScope` — there is always a 1:1 relationship between source files and `FileScope` instances
- All files are bound in Pass 1 sequentially (or in a deterministic order)
- When creating a `NamespaceScope`, check if `GlobalScope` already has a `NamespaceScope` with the same name
- If it does, merge: add the new `NamespaceBlockScope` as a child of the existing `NamespaceScope`
- `NamespaceScope` instances live at the `GlobalScope` level (not under `FileScope`) to enable merging — but each `NamespaceBlockScope` can reference back to its owning `FileScope` for file-level context (declare directives, file-level use imports)
- Declarations from different files within the same namespace are visible to each other (resolved in Phase 3)
- Track which file each symbol came from (`SourceFile` property on `BaseSymbol`, and transitively via the `FileScope`)

#### 2.7 Implement Duplicate Declaration Detection

During binding, detect and report conflicts:

- Same-name class/interface/trait/enum in the same namespace → `MessageCode.BinderDuplicateSymbolDeclaration` (3002)
- Same-name function in the same namespace → error
- Same-name constant in the same namespace → error
- Same-name member (method, property, constant) in the same class → error
- Same-name variable in the same scope → this is actually allowed in PHP (reassignment) but Tyhp may restrict it — follow language rules
- Use `DiagnosticBag.AddError()` with the AST node's file/line/column for each diagnostic

#### 2.8 Implement Scope Validation Using `SymbolTypeHelper.GetAllowedChildren()`

The existing `SymbolTypeHelper.GetAllowedChildren()` method defines which symbol types are valid children of each scope type. Use this during binding to validate scope nesting:

- Before adding a symbol to a scope, check that the symbol's `SymbolType` is in the allowed children list
- If not, report `MessageCode.BinderInvalidSymbolTypeForParent` (3004)
- This catches invalid nesting like a class declaration inside a function (which is allowed in PHP but may have restrictions in Tyhp)

### Acceptance Criteria

- [x] `TyhpBinder.Bind()` accepts a list of `SrcFileAst` and returns a populated `GlobalScope`
- [x] Built-in types, constants, and variables are populated in `GlobalScope` before user code binding
- [x] Each `SrcFileAst` produces a `FileScope` under `GlobalScope` with a `FileSymbol` containing file name, hash, and declare directives
- [x] File-level constructs (declare statements, use imports, variables, constants outside namespace blocks) are registered in the `FileScope`
- [x] Namespace declarations create `NamespaceScope` (at `GlobalScope` level) + `NamespaceBlockScope` with correct nesting
- [x] Class/interface/trait/enum declarations create `ObjectDeclarationScope` + `ObjectDeclarationSymbol` with object kind, generic params, extends/implements
- [x] Function declarations create `FunctionDeclarationScope` + `FunctionDeclarationSymbol` with params and return type
- [x] Method declarations create appropriate method scope + symbol with all metadata
- [x] Property declarations create `ObjectPropertySymbol` with type and accessor info
- [x] Variable assignments in code blocks create `VariableSymbol` in the correct scope
- [x] Constructor promotion creates both parameter `VariableSymbol` and `ObjectPropertySymbol`
- [x] Use/import statements create `UseIncludeSymbol` with alias tracking (in `FileScope` if file-level, in `NamespaceBlockScope` if inside namespace)
- [x] Code blocks (if/for/while/try/catch/etc.) create `CodeBlockScope` with correct parent nesting
- [x] Anonymous functions and classes create their respective scopes and symbols
- [x] Multi-file binding merges namespaces: two files with `namespace App\Models` share the same `NamespaceScope` at the `GlobalScope` level, while each file retains its own `FileScope`
- [x] `NamespaceBlockScope` instances can reference their owning `FileScope` for file-level context
- [x] Duplicate declarations in the same scope are reported as `MessageCode.BinderDuplicateSymbolDeclaration`
- [x] All binding errors go to `DiagnosticBag`, no exceptions are thrown for recoverable errors
- [x] The binder continues processing after encountering errors (does not abort on first error)
- [x] `UnexpectedNodeAst` / `ErrorAst` nodes from the visitor are skipped gracefully
- [x] The `CompilationService` pipeline can call `TyhpBinder.Bind()` and store the result in `CompilationResult.GlobalScope`
- [x] The project compiles with no errors

### Dependencies

- Phase 1 (Symbol Data Model) must be complete — symbols need their properties to be set during binding
- Story 01 diagnostic system (`DiagnosticBag`, `MessageCode`) must be available
- AST node types from the visitor must be understood — the binder dispatches on concrete AST types
- Existing `SymbolTypeHelper.GetAllowedChildren()` is used for scope validation

---

## Phase 3: Implement Name Resolution on SymbolTree




### Phase Overview

Add lookup and resolution methods to `SymbolTree` (or directly on scopes/binder) that enable resolving any name reference in the AST to its declaring symbol. This is the second pass of binding — after all declarations are registered, walk the AST again and link every identifier usage to its symbol.

### Deliverables

- Name resolution methods on `SymbolTree` or a new `NameResolver` class
- Simple name resolution (walk up scope chain)
- Qualified name resolution (fully-qualified `\Namespace\Class`, relative `Namespace\Class`)
- Member resolution (instance `->` and static `::` access)
- Type resolution (resolving type expressions to symbols)
- Extension method resolution
- Use/import alias resolution
- `self`, `static`, `parent` resolution within object scopes
- Trait conflict resolution (precedence and aliases)
- Unresolved reference diagnostics

### Implementation Details

#### 3.1 Design the Resolution API

**File:** `Tyhp/TyhpLang/Binder/SymbolTree.cs` (or new `Tyhp/TyhpLang/Binder/Resolution/NameResolver.cs`)

The current `SymbolTree` has only a constructor and a `// TODO: helper methods` comment. Implement:

- `IBaseSymbol? ResolveSymbol(string name, IBaseScope fromScope)` — walk up the scope chain to find a symbol by simple name. Start from `fromScope`, check its `ChildSymbols` for a match, then walk to `Parent` and repeat. The chain includes `FileScope` between namespace-level scopes and `GlobalScope`. Stop at `GlobalScope`.

- `IBaseSymbol? ResolveQualifiedName(string[] segments, IBaseScope fromScope)` — resolve fully-qualified names like `\App\Models\User`. Start from `GlobalScope`, walk through namespace scopes matching each segment.

- `IBaseSymbol? ResolveRelativeName(string[] segments, IBaseScope fromScope)` — resolve names relative to the current namespace. First determine the current namespace from `fromScope` by walking up to the nearest `NamespaceScope`, then resolve from there.

- `IBaseSymbol? ResolveMember(string memberName, ObjectDeclarationSymbol onObject)` — resolve `$obj->property` or `$obj->method()`. Search the object's `Members` dictionary, then its parent classes, then its traits, then its interfaces (for default implementations).

- `IBaseSymbol? ResolveStaticMember(string memberName, ObjectDeclarationSymbol onClass)` — resolve `MyClass::CONST`, `MyClass::$staticProp`, `MyClass::staticMethod()`. Same search order as instance members but filtered to static/constant symbols.

- `IBaseSymbol? ResolveType(ITypeExpression typeAst, IBaseScope fromScope)` — given a type AST node, resolve it to a symbol. Handle: simple types (`int`, `string`), qualified type names (`\App\Models\User`), nullable types (`?Type`), union types (`A|B`), intersection types (`A&B`), generic types (`Collection<User>`).

- `IBaseSymbol? ResolveExtensionMethod(string methodName, IBaseSymbol onType)` — find extension methods applicable to a type. Search all extension declarations in reachable scopes for methods whose first parameter type matches.

#### 3.2 Implement Scope Chain Walk

The fundamental resolution algorithm:

```
function resolve(name, scope):
    while scope is not null:
        for each symbol in scope.ChildSymbols:
            if symbol.Name == name:
                return symbol
        // also check use/import aliases in current scope
        for each useSymbol in scope.ChildSymbols where useSymbol is UseIncludeSymbol:
            if useSymbol.AliasName == name or (useSymbol.AliasName is null and last segment of useSymbol.ImportedName == name):
                return resolveQualifiedName(useSymbol.ImportedName.Split('\'), globalScope)
        scope = scope.Parent
    return null // unresolved
```

The scope chain walk traverses: `CodeBlockScope` → ... → `FunctionDeclarationScope` → `NamespaceBlockScope` → `FileScope` → `GlobalScope`. The `FileScope` is a critical part of this chain because file-level use imports and file-level variables must be reachable from code within namespace blocks in the same file.

**FileScope in the resolution chain:** When resolving names from within a `NamespaceBlockScope`, the resolver should also check the owning `FileScope` for:
- File-level `use` imports (these are available to all code in the file)
- File-level variables (accessible from file-level code and, in PHP, from `global` keyword references)
- File-level constants

The `NamespaceBlockScope` should reference its owning `FileScope` so that the resolver can walk: `NamespaceBlockScope` → `FileScope` → `GlobalScope` (in addition to the namespace chain).

Special cases:
- Variable resolution stops at function/method boundaries (variables are function-scoped in PHP, not block-scoped — though Tyhp may change this)
- `$this` resolves to a special implicit variable in instance method scopes
- Superglobals (`$_SESSION`, `$_POST`, etc.) are always in scope — they're in `GlobalScope`
- File-level `declare(strict_types=1)` on the `FileSymbol` affects type coercion behavior for all code in that file — the resolver or checker should consult `FileScope.DeclarationSymbol.FileDeclareDirectives` when needed

#### 3.3 Implement `self`, `static`, `parent` Resolution

When inside an `ObjectDeclarationScope`:

- `self` → the `ObjectDeclarationSymbol` of the immediately enclosing class
- `static` → same as `self` for type resolution purposes (late static binding is a runtime concept — the binder treats it as the current class)
- `parent` → the `ObjectDeclarationSymbol` of the parent class (from `ExtendsType`)

Walk up the scope chain from `fromScope` to find the nearest `ObjectDeclarationScope`.

#### 3.4 Implement Use/Import Alias Resolution

During name resolution, `UseIncludeSymbol` entries modify how names are resolved:

- `use App\Models\User;` — when resolving `User`, check use symbols first, find this entry, resolve to `\App\Models\User`
- `use App\Models\User as U;` — when resolving `U`, find this entry with `AliasName = "U"`, resolve to `\App\Models\User`
- `use function App\Helpers\formatDate;` — function-specific import
- `use const App\Config\MAX_SIZE;` — constant-specific import

The resolver should check use/import aliases in the current namespace block scope first, then the owning `FileScope` (for file-level use imports), before walking up to `GlobalScope`.

#### 3.5 Implement Trait Method Conflict Resolution

When a class uses multiple traits that define the same method:

- Check for `insteadof` adaptations (precedence): `A::method insteadof B;` means use A's version
- Check for `as` adaptations (aliases): `A::method as aliasMethod;` means A's method is also available as `aliasMethod`
- Check for visibility changes: `A::method as protected;`
- If no adaptation resolves the conflict, report an error diagnostic

This resolution happens when binding the trait use statement in Section 2.4, but the actual symbol lookups for inherited trait members happen here.

#### 3.6 Implement Inheritance Chain Resolution

For member resolution on objects:

1. Check the object's own members first
2. If not found, check parent class (recursively up the inheritance chain)
3. If not found, check used traits (in order of `use` declarations)
4. If not found, check implemented interfaces (for constants and default method implementations)
5. If still not found, check if there's a `__call` / `__callStatic` / `__get` / `__set` magic method (for the checker to flag as dynamic access)

#### 3.7 Implement the Resolution Pass on the Binder

Add a second pass to `TyhpBinder`:

- After Pass 1 (declaration) completes for all files, run Pass 2 (resolution)
- Walk each `SrcFileAst` again
- For every name reference in the AST (type references, variable reads, function calls, member accesses), call the appropriate resolve method
- Optionally annotate the AST node with its resolved symbol (add a `ResolvedSymbol` property to `Base2Ast` or use a side dictionary `Dictionary<IBase2Ast, IBaseSymbol>`)
- For unresolved references, report `MessageCode.BinderSymbolNotFound` (3003) to `DiagnosticBag`

The decision of how to store resolution results (annotating AST nodes vs. external dictionary) is an implementation choice. An external dictionary keeps AST nodes immutable; annotating nodes is simpler for downstream consumers (checker, emitter).

#### 3.8 Handle Generic Type Parameter Resolution

When resolving types in generic contexts:

- Inside a class `MyClass<T>`, the type `T` resolves to the `GenericTypeParameterSymbol` on `MyClass`
- Inside a method `public function foo<U>(U $param)`, the type `U` resolves to the method's `GenericTypeParameterSymbol`
- Method generic parameters shadow class generic parameters with the same name (report a warning)
- When resolving a generic type instantiation like `Collection<User>`, resolve `Collection` to its symbol and `User` to its symbol, then verify the number of type arguments matches (this is checker territory, but the binder should still resolve the names)

### Acceptance Criteria

- [x] `ResolveSymbol(name, scope)` correctly walks up the scope chain (including through `FileScope`) and returns the declaring symbol
- [x] `ResolveQualifiedName(segments, scope)` resolves fully-qualified names from `GlobalScope`
- [x] `ResolveRelativeName(segments, scope)` resolves names relative to the current namespace
- [x] `ResolveMember(name, object)` finds instance members including inherited and trait members
- [x] `ResolveStaticMember(name, class)` finds static members and constants
- [x] `ResolveType(typeAst, scope)` resolves type expressions including nullable, union, intersection, and generic types
- [x] Use/import aliases are correctly resolved during name lookup (checking both `NamespaceBlockScope` and `FileScope` use imports)
- [x] `self`, `static`, `parent` resolve correctly within class contexts
- [x] Trait method conflicts are resolved using `insteadof` and `as` adaptations
- [x] Inheritance chain is walked correctly: own members → parent → traits → interfaces
- [x] Unresolved references produce `MessageCode.BinderSymbolNotFound` diagnostics with correct file/line/column
- [x] Generic type parameters resolve within their declaring scope (class or method)
- [x] Variable resolution respects function scope boundaries (variables don't leak out of functions)
- [x] Resolution pass can be run after declaration pass, or integrated into a single walk where ordering permits
- [x] File-level variables and constants are resolvable from code within the same file's `FileScope`
- [x] File-level declare directives (e.g., `strict_types`) are accessible via `FileScope.DeclarationSymbol.FileDeclareDirectives`
- [x] Built-in types (`int`, `string`, `bool`, etc.) resolve to `BuiltInTypeSymbol` from `GlobalScope`

### Dependencies

- Phase 2 (Declaration Pass) must be complete — all symbols must be registered before resolution
- `UseIncludeSymbol` entries must be populated with correct import/alias data
- The inheritance/implements references on `ObjectDeclarationSymbol` must be set during declaration binding

---

## Phase 4: Wire Up Tyhpdef Loading into the Binder




### Phase Overview

Implement the loading of tyhpdef files — type definition files that describe the signatures of external PHP code (extensions, Composer packages, TyhpSpec built-ins). These symbols must be registered in the `GlobalScope` before user code is bound, so that references to PHP standard library functions, framework classes, etc. can be resolved.

### Deliverables

- `Tyhpdef.Get()` method (in `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs`) fully implemented
- Tyhpdef file discovery from configured paths
- Tyhpdef AST parsing using the existing parser/visitor (they already handle `.tyhpdef` language mode)
- Tyhpdef AST → symbol conversion for all tyhpdef import types
- TyhpSpec built-in types loaded (`tyhpTypes.tyhpdef`, `tyhpDisposable.tyhp`)
- Embedded tyhpdef loading from `Tyhp/TyhpLang/Binder/TyhpBuiltIn/Tyhpdef.cs` (compressed base64 data)
- All tyhpdef symbols registered in `GlobalScope` before user code binding

### Implementation Details

#### 4.1 Implement Tyhpdef File Discovery

**File:** `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs`

The currently commented-out `Get()` method needs to discover tyhpdef files from multiple sources:

1. **Embedded tyhpdef data** — The `Tyhp/TyhpLang/Binder/TyhpBuiltIn/Tyhpdef.cs` file contains a compressed, base64-encoded tyhpdef string (`ExtTypes`). This is a pre-compiled set of PHP extension type definitions. Decompress and parse this first.

2. **TyhpSpec files** — `Tyhp/TyhpSpec/tyhpTypes.tyhpdef` (358 lines defining decimal, struct, symbol name types, generic array types, etc.) and `Tyhp/TyhpSpec/tyhpDisposable.tyhp` (IsDisposable interfaces). These ship with the compiler.

3. **PHP extension tyhpdefs** — From `tyhpdef/` root directory. Currently has a subdirectory structure.

4. **Generated tyhpdefs** — From `DebugProject/tyhpdef_gen/` organized by PHP version and locale (e.g., `tyhpdef_gen/8.3.11/en/`). There are 2000+ generated tyhpdef files here.

5. **User-configured tyhpdef paths** — From `tyhp.json` configuration (`tyhpdefInclude` / `tyhpdefExclude` globs, once configuration parsing is expanded in Story 10).

For now, implement sources 1-3 (embedded, TyhpSpec, and bundled tyhpdefs). Sources 4-5 can use `// PLACEHOLDER_STORY_10: Load user-configured tyhpdefs` and `// PLACEHOLDER_STORY_20: Load generated tyhpdefs`.

The method signature should be:

```csharp
public static IEnumerable<SrcFileAst> Get(DiagnosticBag diagnostics, CompilationOptions? options = null)
```

#### 4.2 Parse Tyhpdef Files Using Existing Infrastructure

The parser and visitor already handle tyhpdef language mode. The `PhpParserAstVisitor.GetCurrentLanguageMode()` method checks for `TyhpdefBlockContext` and returns `"tyhpdef"`. The visitor has a dedicated partial class: `TyhpParserAstVisitor.Tyhpdef.cs`.

To parse a tyhpdef file:
- Use the same lexer/parser/visitor pipeline as regular source files
- The parser recognizes `<?tyhpdef` as the opening tag
- The visitor produces AST nodes specific to tyhpdef: `TyhpdefImportClassAst`, `TyhpdefImportFunctionAst`, `TyhpdefImportConstDeclAst`, `TyhpdefIdentifierAliasAst`, etc.
- For the embedded compressed data: decompress the base64 string (using the existing `Decompress()` method), then parse the resulting tyhpdef text

#### 4.3 Implement Tyhpdef AST → Symbol Conversion

Create a method (or helper class) that walks tyhpdef AST nodes and creates binder symbols:

**`TyhpdefImportClassAst`** → `ObjectDeclarationSymbol`:
- Extract the class name, namespace, generic parameters
- Extract extends/implements from the tyhpdef class body
- Extract all member declarations (methods, properties, constants)
- Set `SourceFile` to the tyhpdef file path
- Set `IsDeprecated`/`IsObsolete` from tyhpdef markers

**`TyhpdefImportFunctionAst`** → `FunctionDeclarationSymbol`:
- Extract function name, parameters (with types), return type
- Handle overloaded function signatures (multiple declarations with different parameter types — store as a list of signature variants or as separate symbols)

**`TyhpdefImportConstDeclAst`** → `ConstantSymbol`:
- Extract constant name and type

**Tyhpdef enum imports** → `ObjectDeclarationSymbol` with `ObjectKind = Enum`:
- Extract enum name, backed type, cases, implemented interfaces

**Type alias declarations in tyhpdef** (`type X = Y;`):
- Create `TypeAliasSymbol` with aliased type expression
- Handle generic type aliases (`type iterable<TKey, TValue> = ...`)

**`TyhpdefIdentifierAliasAst`** (aliased imports):
- Record the alias mapping so that when user code references the alias name, it resolves to the real PHP name

#### 4.4 Register Tyhpdef Symbols in GlobalScope

The binder should call tyhpdef loading early in the `Bind()` method:

1. Call `Tyhpdef.Get(diagnostics)` to get parsed tyhpdef ASTs
2. Walk each tyhpdef AST and create symbols using the conversion logic from 4.3
3. Register all tyhpdef symbols in the `GlobalScope` under the appropriate namespace scopes
4. This must happen BEFORE user code declaration binding (Pass 1) so that user code can reference tyhpdef-defined types

#### 4.5 Handle the Embedded TyhpBuiltIn Data

**File:** `Tyhp/TyhpLang/Binder/TyhpBuiltIn/Tyhpdef.cs`

This file contains a compressed, base64-encoded string of tyhpdef content. The `Decompress()` method already works. Integration:

- Call `TyhpBuiltIn.Tyhpdef.All` to get the list of compressed tyhpdef strings
- Decompress each string
- Parse the decompressed text as a tyhpdef source
- Convert the resulting AST to symbols and register in `GlobalScope`

The `AllKeyed` dictionary maps identifiers (like `"__tyhp_types"`) to compressed content, which can be used for selective loading or caching.

#### 4.6 Handle Versioned Tyhpdefs

The `tyhpdef_gen/` directory is organized by PHP version (e.g., `8.3.11/en/`). The binder should:

- Read the target PHP version from configuration (once `output.phpVersion` config is parsed in Story 10)
- For now, use a default PHP version or load all available tyhpdefs
- `// PLACEHOLDER_STORY_10: Filter tyhpdefs by configured PHP version`

#### 4.7 Add Tyhpdef-Specific MessageCodes

**File:** `Tyhp/Domain/Exceptions/MessageCode.cs`

Add error codes for tyhpdef-related issues (in the 8000s range as specified in the numbering scheme):

- `TyhpdefParseError = 8001` — a tyhpdef file failed to parse
- `TyhpdefDuplicateDeclaration = 8002` — a tyhpdef declares a symbol that already exists
- `TyhpdefFileNotFound = 8003` — a configured tyhpdef path doesn't exist
- `TyhpdefInvalidFormat = 8004` — a tyhpdef file has an unexpected structure

### Acceptance Criteria

- [x] `Tyhpdef.Get()` returns parsed AST trees for embedded tyhpdefs, TyhpSpec files, and bundled tyhpdef files
- [x] Embedded compressed tyhpdef data decompresses and parses without errors
- [x] `tyhpTypes.tyhpdef` parses and its type aliases (`decimal`, `struct`, symbol name types) are registered as symbols
- [x] `tyhpDisposable.tyhp` parses and its interfaces (`IsDisposable`, `AsyncIsDisposable`) are registered
- [x] Tyhpdef class imports create `ObjectDeclarationSymbol` with correct member symbols
- [x] Tyhpdef function imports create `FunctionDeclarationSymbol` with correct parameter/return types
- [x] Tyhpdef constant imports create `ConstantSymbol` with correct types
- [x] All tyhpdef symbols are registered in `GlobalScope` BEFORE user code binding begins
- [x] Tyhpdef aliases are recorded and usable during name resolution
- [x] Parse errors in tyhpdef files are reported as diagnostics (not thrown as exceptions)
- [x] Duplicate tyhpdef declarations are detected and reported
- [x] The binder can resolve user code references to PHP standard library types/functions (e.g., `\DateTime`, `array_map`, `PDO`) after tyhpdef loading
- [x] Tyhpdef-specific `MessageCode` values are added in the 8000s range

### Dependencies

- Phase 1 (Symbol Data Model) — symbols need full properties to represent tyhpdef declarations
- Phase 2 (Declaration Pass) — the binder's `Bind()` method calls tyhpdef loading before user code binding
- Phase 3 (Name Resolution) — tyhpdef symbols must be resolvable during the resolution pass
- Story 01 (`CompilationService`, `DiagnosticBag`) — for parsing tyhpdef files and reporting errors
- The existing parser/visitor must handle `.tyhpdef` files correctly (it does — `TyhpParserAstVisitor.Tyhpdef.cs` exists)

---

## Phase 5: Integration, Pipeline Wiring, and Validation




### Phase Overview

Wire the completed binder into the compilation pipeline, replace the `// PLACEHOLDER_STORY_02` markers from Story 01's `BuildAction` and `CompilationService`, and validate the entire binder with end-to-end testing against the existing example files.

### Deliverables

- `CompilationService` updated to call `TyhpBinder.Bind()` after parsing
- `BuildAction` pipeline placeholder replaced with actual binder invocation
- `CompilationResult.GlobalScope` populated after binding
- `CompilationResult.BindDuration` timing recorded
- End-to-end validation: parse all 30 example files → bind → verify no crash and expected diagnostics
- Bind duration displayed in build output

### Implementation Details

#### 5.1 Update `CompilationService` to Include Binding

**File:** `Tyhp/Domain/Services/CompilationService.cs` (created in Story 01)

After the parse step completes and `CompilationResult.ParsedFiles` is populated:

- Check `result.Diagnostics.HasErrors` — if there are parse errors, skip binding (but continue if there are only warnings)
- Create a `GlobalScope` instance
- Populate built-ins: `Types.PopulateGlobal()`, `Constants.PopulateGlobal()`, `Variables.PopulateGlobal()`
- Load tyhpdef symbols: `Tyhpdef.Get(result.Diagnostics)` → parse → register in `GlobalScope`
- Create `TyhpBinder` with the `DiagnosticBag`
- Call `binder.Bind(result.ParsedFiles)` — this performs the declaration pass and resolution pass
- Store the returned `GlobalScope` in `CompilationResult.GlobalScope`
- Record `CompilationResult.BindDuration`

#### 5.2 Update `BuildAction` Pipeline

**File:** `Tyhp/CLI/BuildAction.cs` (created in Story 01)

Replace the placeholder comments:

- `// PLACEHOLDER_STORY_02: Load tyhpdefs` → tyhpdef loading is now part of `CompilationService` or called from `BuildAction` before binding
- `// PLACEHOLDER_STORY_02: Run binder` → binder is now part of `CompilationService` or called from `BuildAction` after parsing
- Display bind duration in the timing summary
- Display symbol count or scope tree summary if verbose mode is enabled

#### 5.3 Update `LintAction` Pipeline

**File:** `Tyhp/CLI/LintAction.cs` (created in Story 01)

Same as `BuildAction` but the lint action runs parse + bind + check (Story 08), no emit. After Phase 1:

- Lint should run parse + bind and report binding diagnostics
- `// PLACEHOLDER_STORY_08: Run checker` remains for the checker phase

#### 5.4 Validate with Existing Example Files

The `Examples/` directory contains 30 files covering major language features. Run the full parse → bind pipeline on them:

- `Examples/OperatorOverloads.tyhp` — tests operator overload declarations binding
- `Examples/PropertyAccessors.tyhp` — tests property accessor binding
- `Examples/TypeGuards.tyhp` — tests type guard function binding
- `Examples/WithKeyword.tyhp` — tests `with` keyword in struct/class context
- All PHP example files (`*.php`) — tests PHP-mode binding

Expected outcome: the binder should process all files without crashing. Some unresolved reference diagnostics are expected (for references to types defined in tyhpdefs that may not be loaded yet, or for Tyhp features that are partially supported). The key validation is that the scope tree is built correctly — each file produces a `FileScope`, file-level constructs are in the `FileScope`, namespace declarations produce merged `NamespaceScope` entries, and declarations are registered in their correct scopes.

#### 5.5 Add Binder Diagnostic Summary to CLI Output

When the build/lint action completes, display:

- Number of files bound (i.e., `FileScope` count)
- Number of symbols registered
- Number of scopes created
- Number of unresolved references (if any)
- Number of duplicate declaration warnings
- Bind duration

This helps developers understand the binder's output and debug issues.

#### 5.6 Placeholder Markers for Future Work

Ensure the following placeholders exist in the codebase using the appropriate format:

**Cross-story placeholders** (`// PLACEHOLDER_STORY_N:` — for work belonging to other TODO.md stories):
- `// PLACEHOLDER_STORY_06: Load TyhpSpec type definitions` (in tyhpdef loading — for expanded TyhpSpec in Story 06 of the main TODO)
- `// PLACEHOLDER_STORY_08: Run checker` (in `BuildAction` and `LintAction`)
- `// PLACEHOLDER_STORY_09: Run emitter` (in `BuildAction`)
- `// PLACEHOLDER_STORY_10: Load user-configured tyhpdefs` (in tyhpdef discovery)
- `// PLACEHOLDER_STORY_10: Filter tyhpdefs by configured PHP version` (in tyhpdef loading)

**Note:** Use `// PLACEHOLDER_PHASE_N:` for placeholders referencing future phases *within this implementation plan*. Use `// PLACEHOLDER_STORY_N:` for placeholders referencing work from other TODO.md stories.

### Acceptance Criteria

- [x] `CompilationService` calls `TyhpBinder.Bind()` after parsing and stores the result in `CompilationResult.GlobalScope`
- [x] `CompilationResult.BindDuration` is recorded accurately
- [x] `BuildAction` invokes the binder as part of its pipeline (no more placeholder for bind step)
- [x] `LintAction` invokes parse + bind and reports all diagnostics
- [ ] Running `tyhp build` on a project with source files parses and binds without crashing
- [ ] Running the binder on all `Examples/*.tyhp` and `Examples/*.php` files produces a valid scope tree with one `FileScope` per file
- [x] Binding diagnostics (duplicates, invalid nesting, parse errors in tyhpdefs) are displayed in the CLI output
- [x] Bind timing is shown in the build summary output
- [x] Cross-story placeholder comments exist for Stories 06, 08, 09, and 10 (`// PLACEHOLDER_STORY_06/08/09/10:`) at the appropriate locations
- [x] The `DebugAction` can optionally use the binder (e.g., with a `--bind` flag) to test binding without the full build pipeline
- [ ] No regressions in existing functionality (parsing, AST caching, debug action)

### Dependencies

- All previous phases of this plan (1-4) must be complete
- Story 01 infrastructure (`CompilationService`, `BuildAction`, `LintAction`, `CompilationResult`, `DiagnosticBag`) must be functional
- The existing `DebugAction` must continue to work unchanged (it doesn't use the binder unless opted in)

---

## Appendix A: Existing File Inventory (Binder Directory)

This inventory maps existing files to their purpose and status, helping the implementor decide what to extend vs. replace.

### Core Files

| File | Status | Notes |
|------|--------|-------|
| `TyhpBinder.cs` | **Implemented** | Two-pass binder (declaration + resolution) with partial classes. |
| `SymbolTree.cs` | **Implemented** | GlobalScope, extension method index, lookup helpers, NameResolver factory. |
| `SymbolIdentifier.cs` | Minimal | Has `NamespacePath` and `Name`. May be extended or replaced. |

### Scope Files (`Scopes/`)

| File | Status | Notes |
|------|--------|-------|
| `BaseScope.cs` | **Implemented** | 5-type-parameter generic base with AddChildSymbol/AddChildScope, FQN computation, duplicate detection, namespace path caching. |
| `GlobalScope.cs` | **Implemented** | Namespace merging, FileScope management, counting helpers. |
| `FileScope.cs` | **Implemented** | One per source file. Child of `GlobalScope`. Holds declare directives, use imports, variables, constants. |
| `NamespaceScope.cs` | **Implemented** | Parent is `GlobalScope` (for namespace merging). |
| `NamespaceBlockScope.cs` | **Implemented** | References owning `FileScope` via `NamespaceBlockSymbol.OwningFileScope`. |
| `ObjectDeclarationScope.cs` | **Implemented** | Multi-parent interface implementations. |
| `FunctionDeclarationScope.cs` | **Implemented** | Functional with parent interfaces. |
| `InstanceMethodDeclarationScope.cs` | **Implemented** | Functional with parent interfaces. |
| `StaticMethodDeclarationScope.cs` | **Implemented** | Functional with parent interfaces. |
| `CodeBlockScope.cs` | **Implemented** | Parent resolution for multiple interfaces. |
| `AnonymousFunctionScope.cs` | **Implemented** | Functional scope for closures. |
| `AnonymousObjectDeclarationScope.cs` | **Implemented** | Functional. |
| `DeclareBlockScope.cs` | **Implemented** | Functional. |
| `LabelScope.cs` | **Implemented** | Functional. |

### Scope Interface Files (`Scopes/Interfaces/`)

20 interfaces defining parent/child relationships. These are used in the `BaseScope<>` type parameters. They may be simplified if the scope hierarchy is redesigned.

**New interface needed:** `IFileScopeChild` — for scopes that can be children of `FileScope` (e.g., `CodeBlockScope`, `DeclareBlockScope`, `ObjectDeclarationScope`, `FunctionDeclarationScope` when at file level). Also need `IFileScopeSymbol` for symbols that can live directly in a `FileScope` (e.g., `UseIncludeSymbol`, `ConstantSymbol`, `VariableSymbol`).

### Symbol Files (`Symbols/`)

| File | Status | Notes |
|------|--------|-------|
| `BaseSymbol.cs` | **Implemented** | All core properties: Name, FQN, SymbolType, ContainingScope, Visibility, Source location, etc. |
| `BuiltInTypeSymbol.cs` | **Implemented** | Extends BaseSymbol with SymbolType.BuiltInType. |
| `MagicConstantSymbol.cs` | **Implemented** | Extends BaseSymbol with SymbolType.MagicConstant. |
| `SuperGlobalSymbol.cs` | **Implemented** | Extends BaseSymbol with SymbolType.Variable. |
| `FileSymbol.cs` | **Implemented** | FileName, FileHash, FileDeclareDirectives with validation. |
| `NamespaceSymbol.cs` | **Implemented** | Has `Name`, trims backslashes. |
| `ObjectDeclarationSymbol.cs` | **Implemented** | ObjectKind, IsStruct, IsExtension, GenericParameters, ExtendsType, ImplementsTypes, Members, trait adaptation. |
| `FunctionDeclarationSymbol.cs` | **Implemented** | Parameters, ReturnType, GenericParameters, IsGenerator, IsAsync. |
| `VariableSymbol.cs` | **Implemented** | DeclaredType, IsParameter, DefaultValue, IsDisposable, IsPromotedProperty, IsRef. |
| `ObjectMethodSymbol.cs` | **Implemented** | Parameters, ReturnType, GenericParameters, IsAbstract, IsStatic, magic method dispatch. |
| `ObjectPropertySymbol.cs` | **Implemented** | DeclaredType, DefaultValue, HasAccessor, AccessorKind. |
| `ObjectConstantSymbol.cs` | **Implemented** | DeclaredType, ValueExpression. |
| `TypeAliasSymbol.cs` | **Implemented** | AliasedType, GenericParameters. |
| `GenericTypeParameterSymbol.cs` | **Implemented** | Constraint, Variance, DefaultType. |
| `ConstantSymbol.cs` | **Implemented** | DeclaredType, ValueExpression. |
| `LabelSymbol.cs` | **Implemented** | Name only (inherited from BaseSymbol). |
| `UseIncludeSymbol.cs` | **Implemented** | ImportedName, AliasName, UseType, ImportedNameSegments with validation. |
| All `ObjectMagic*MethodSymbol.cs` (15 files) | **Implemented** | Kept as separate classes; extend ObjectMethodSymbol. |
| All other symbol files | **Implemented** | Type-specific properties populated. |

### Symbol Interface Files (`Symbols/Interfaces/`)

11 interfaces defining which scopes can hold which symbols. These are used in the `BaseScope<>` type parameters for `TChildSymbols`. They may be simplified if the symbol hierarchy is redesigned.

### BuiltIn Files

| File | Status | Notes |
|------|--------|-------|
| `Types.cs` | **Implemented** | Populates built-in types (PopulateGlobal + PopulateObject). |
| `Constants.cs` | **Implemented** | Populates magic constants. |
| `Variables.cs` | **Implemented** | Populates superglobals. |
| `Tyhpdef.cs` | **Implemented** | Get() loads embedded, TyhpSpec, and bundled tyhpdefs with full parsing. |
| `OLD_Tyhpdef.cs` | Legacy | 280KB+ file — old tyhpdef data. Reference only. |
| `OLD_BuiltInSymbols.cs` | Legacy | Old approach. Reference only. |

### TyhpBuiltIn Files

| File | Status | Notes |
|------|--------|-------|
| `TyhpBuiltIn/Tyhpdef.cs` | Functional | Contains compressed base64 tyhpdef data with `Decompress()`. |

---

## Appendix B: AST Node Types the Binder Must Handle

The binder dispatches on AST node types. Key node types from the 145 AST files:

**Top-level / File / Namespace:**
- `PhpSrcFileAst` / `TyhpSrcFileAst` — source file roots (extend `SrcFileAst`) → maps to `FileScope` (one per file)
- `PhpTopStatementListAst` — list of top-level statements. Statements before/outside namespace declarations are file-level (bound to `FileScope`); statements inside namespace blocks are namespace-level.
- `PhpBlockNamespaceDeclAst` — namespace block declaration → creates `NamespaceScope` + `NamespaceBlockScope`
- `PhpDeclareAst` — when at file level (before namespace), maps to `FileSymbol.FileDeclareDirectives`; when block syntax, creates `DeclareBlockScope`
- `PhpImportDeclAst` / `PhpImportDeclListAst` — when at file level, creates `UseIncludeSymbol` in `FileScope`; when inside namespace block, creates in `NamespaceBlockScope`

**Object declarations:**
- `PhpClassBodyAst` — class body with members
- `TyhpExtensionDeclAst` — Tyhp extension declaration
- Object declaration AST nodes (determine exact names from visitor output)

**Members:**
- `PhpPropertyAst` — property declaration
- `PhpPropertyHookListAst` — property accessor hooks
- `PhpConstDeclAst` / `PhpConstDeclListAst` — constant declarations
- `PhpEnumCaseAst` — enum case
- Method declaration AST nodes
- `PhpAttributeAst` / `PhpAttributeListAst` — attributes

**Type expressions:**
- `PhpTypeExpressionAst` — type annotations
- `PhpBuiltinTypeAst` — built-in type references
- `PhpClassNameListAst` — class name lists (implements, catch)
- `TyhpGenericsTypeArgumentListAst` — generic type arguments

**Statements and blocks:**
- `PhpConditionalAst` — if/else
- `PhpConditionalArmAst` / `PhpConditionalArmListAst` — match arms
- Various loop AST nodes
- `PhpCatchClauseAst` / `PhpCatchListAst` — try/catch
- `PhpDeclareAst` — declare statements

**Tyhpdef-specific:**
- `TyhpdefImportConstDeclAst` — tyhpdef constant import
- `TyhpdefIdentifierAliasAst` — tyhpdef identifier alias
- `TyhpdefPropertyListAst` — tyhpdef property list
- Other `Tyhpdef*Ast` nodes from the visitor

---

## Appendix C: MessageCode Values Used by the Binder

**Existing codes (from `MessageCode.cs`):**

| Code | Name | Description |
|------|------|-------------|
| 3001 | `BinderUnknownError` | Fallback for unexpected binder errors |
| 3002 | `BinderDuplicateSymbolDeclaration` | Same name declared twice in same scope |
| 3003 | `BinderSymbolNotFound` | Reference to undeclared symbol |
| 3004 | `BinderInvalidSymbolTypeForParent` | Symbol added to wrong scope type |

**New codes to add:**

| Code | Name | Description |
|------|------|-------------|
| 3005 | `BinderCircularInheritance` | Class extends itself (directly or indirectly) |
| 3006 | `BinderTraitConflict` | Unresolved trait method conflict |
| 3007 | `BinderDuplicateUseAlias` | Two use statements with the same alias |
| 3008 | `BinderInvalidSelfReference` | `self`/`static`/`parent` used outside class context |
| 3009 | `BinderInvalidParentReference` | `parent` used in class with no parent |
| 3010 | `BinderDuplicateGenericParameter` | Same generic parameter name declared twice |
| 3011 | `BinderGenericParameterShadow` | Method generic parameter shadows class generic parameter (warning) |
| 3012 | `BinderMultipleConstructors` | Class has more than one `__construct` method |
| 3013 | `BinderNamespaceMismatch` | File declares multiple different namespaces (PHP allows this but Tyhp may restrict) |
| 8001 | `TyhpdefParseError` | Tyhpdef file failed to parse |
| 8002 | `TyhpdefDuplicateDeclaration` | Tyhpdef declares existing symbol |
| 8003 | `TyhpdefFileNotFound` | Configured tyhpdef path doesn't exist |
| 8004 | `TyhpdefInvalidFormat` | Tyhpdef file has unexpected structure |

These should be added to `Tyhp/Domain/Exceptions/MessageCode.cs` in the appropriate `#region` blocks, and corresponding resource strings should be added to the `.resx` files created in Story 01.

---

*Last updated: 2026-02-16 — Added FileScope between GlobalScope and NamespaceScope*

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the binder implementation works end-to-end. Steps can be skipped, reordered, or modified as appropriate. All commands assume you are in the repository root (`/Volumes/SAMSUNGUSB/tyhp`).

### Step 1: Verify the Project Compiles

Build the compiler and confirm no errors from the binder changes:

```bash
dotnet clean && dotnet restore && dotnet build
```

Expected: Build succeeds with zero errors. Warnings are acceptable.

### Step 2: Verify Built-in Symbol Population

Create a minimal test file that references built-in types, magic constants, and superglobals:

```tyhp
<?tyhp
namespace Test\Binder;

function testBuiltIns(): void {
    int $x = 42;
    string $name = "hello";
    float $pi = 3.14;
    bool $flag = true;
    mixed $any = null;

    string $file = __FILE__;
    int $line = __LINE__;

    array $post = $_POST;
    array $session = $_SESSION;
}
```

Save as `test_binder_builtins.tyhp` in the project directory. Run:

```bash
dotnet run --project tyhp.csproj -- build test_binder_builtins.tyhp
```

Expected: No `BinderSymbolNotFound` (3003) diagnostics for `int`, `string`, `float`, `bool`, `mixed`, `__FILE__`, `__LINE__`, `$_POST`, `$_SESSION`. These are all registered in `GlobalScope` by `Types.PopulateGlobal()`, `Constants.PopulateGlobal()`, and `Variables.PopulateGlobal()`.

### Step 3: Verify FileScope Creation and File-Level Constructs

Create a file with file-level constructs (declare, use, constants, variables outside namespace blocks):

```tyhp
<?tyhp
declare(strict_types=1);

const VERSION = "1.0.0";

namespace App\Models {
    class User {
        public string $name;
        public int $age;
    }
}

namespace App\Services {
    class UserService {
        public function getUser(): \App\Models\User {
            return new \App\Models\User();
        }
    }
}
```

Save as `test_binder_filescope.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_binder_filescope.tyhp
```

Expected:
- No crash — the file should parse and bind
- The `declare(strict_types=1)` should be stored on the `FileSymbol.FileDeclareDirectives`
- The `VERSION` constant should be in the `FileScope`
- `App\Models` and `App\Services` should produce `NamespaceScope` entries at `GlobalScope` level
- `User` and `UserService` classes should be bound in their respective `NamespaceBlockScope`

### Step 4: Verify Namespace Merging Across Files

Create two files that contribute to the same namespace:

**File `test_binder_merge_a.tyhp`:**
```tyhp
<?tyhp
namespace App\Models;

class Product {
    public string $name;
    public float $price;
}
```

**File `test_binder_merge_b.tyhp`:**
```tyhp
<?tyhp
namespace App\Models;

class Order {
    public Product $product;
    public int $quantity;
}
```

Run both through the binder (e.g., via a project `tyhp.json` that includes both, or via CLI args if supported):

```bash
dotnet run --project tyhp.csproj -- build test_binder_merge_a.tyhp test_binder_merge_b.tyhp
```

Expected:
- Both files produce their own `FileScope`
- A single `NamespaceScope` for `App\Models` exists at `GlobalScope`, with two `NamespaceBlockScope` children (one per file)
- `Product` is visible from `Order`'s context — the reference to `Product` in `Order`'s property should resolve without `BinderSymbolNotFound`

### Step 5: Verify Duplicate Declaration Detection

Create a file with deliberate duplicate declarations:

```tyhp
<?tyhp
namespace App\Duplicates;

class Foo {
    public string $name;
}

class Foo {
    public int $id;
}
```

Save as `test_binder_duplicates.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_binder_duplicates.tyhp
```

Expected: A `BinderDuplicateSymbolDeclaration` (3002) diagnostic for the second `Foo` class. The binder should continue processing after the error (not abort).

### Step 6: Verify Object Body Binding (Methods, Properties, Constants)

Create a file exercising class body features:

```tyhp
<?tyhp
namespace App\Test;

class Animal {
    public string $species;
    protected int $age;
    private bool $isWild = false;

    public const string KINGDOM = "Animalia";

    public function __construct(
        public string $name,
        protected string $sound
    ) {}

    public function speak(): string {
        return $this->name . " says " . $this->sound;
    }

    public static function create(string $name): self {
        return new self($name, "...");
    }
}

class Dog extends Animal {
    public function speak(): string {
        return parent::speak() . "!";
    }
}
```

Save as `test_binder_classbody.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_binder_classbody.tyhp
```

Expected:
- `Animal` has `ObjectDeclarationScope` with method, property, and constant symbols
- Constructor promotion: `$name` and `$sound` should create both `VariableSymbol` (parameter) and `ObjectPropertySymbol`
- `self` in `create()` return type should resolve to `Animal`
- `parent::speak()` in `Dog` should resolve to `Animal`'s `speak` method
- No errors or crashes

### Step 7: Verify Name Resolution (Use Imports and Aliases)

```tyhp
<?tyhp
namespace App\Controllers;

use App\Models\User;
use App\Models\Product as Item;

class ShopController {
    public function handle(): void {
        User $user = new User();
        Item $item = new Item();
    }
}
```

Save as `test_binder_useimports.tyhp` (and ensure the `User`/`Product` classes exist from earlier test files or create stubs). Run:

```bash
dotnet run --project tyhp.csproj -- build test_binder_useimports.tyhp test_binder_merge_a.tyhp
```

Expected:
- `User` resolves via the `use App\Models\User;` import
- `Item` resolves via the `use App\Models\Product as Item;` alias
- No `BinderSymbolNotFound` for either reference

### Step 8: Bind All Existing Example Files

Run the binder on all example files to check for crashes:

```bash
dotnet run --project tyhp.csproj -- build Examples/OperatorOverloads.tyhp
dotnet run --project tyhp.csproj -- build Examples/PropertyAccessors.tyhp
dotnet run --project tyhp.csproj -- build Examples/TypeGuards.tyhp
dotnet run --project tyhp.csproj -- build Examples/Generics.tyhp
dotnet run --project tyhp.csproj -- build Examples/AsyncAwait.tyhp
dotnet run --project tyhp.csproj -- build Examples/ClassTypes.tyhp
```

Expected: No crashes. Some unresolved reference diagnostics are acceptable (types defined in tyhpdefs may not be loaded depending on configuration). The key check is that the binder processes each file without throwing exceptions, and produces a scope tree with one `FileScope` per file.

### Step 9: Verify Tyhpdef Loading (Built-in PHP Types)

If verbose/debug output is available, check that tyhpdef symbols are loaded into `GlobalScope` before user code:

```bash
dotnet run --project tyhp.csproj -- build --verbose Examples/test.tyhp
```

Look for output like:
- Number of tyhpdef symbols registered
- Whether `\DateTime`, `array_map`, `PDO`, or other standard PHP types are resolvable
- Embedded tyhpdef decompression succeeding

If verbose mode is not yet available, create a test file referencing standard PHP types and check for unresolved references:

```tyhp
<?tyhp
namespace App\TyhpdefTest;

function testStdLib(): void {
    \DateTime $now = new \DateTime();
    string $json = \json_encode(["test" => true]);
    int $len = \strlen("hello");
}
```

Expected: References to `\DateTime`, `\json_encode`, `\strlen` should resolve if tyhpdefs are loaded correctly. If they do not resolve (producing 3003 diagnostics), tyhpdef loading may need investigation.

### Step 10: Verify Bind Timing in CLI Output

Run a build and check for bind duration in the output:

```bash
dotnet run --project tyhp.csproj -- build Examples/test.tyhp
```

Expected: The output should include a timing line for binding (e.g., "Bind: 15ms" or similar). If the bind step is wired into the pipeline correctly, `CompilationResult.BindDuration` should be recorded and displayed.

### Step 11: Clean Up Test Files

Remove any test files created during verification:

```bash
rm -f test_binder_builtins.tyhp test_binder_filescope.tyhp test_binder_merge_a.tyhp test_binder_merge_b.tyhp test_binder_duplicates.tyhp test_binder_classbody.tyhp test_binder_useimports.tyhp
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
