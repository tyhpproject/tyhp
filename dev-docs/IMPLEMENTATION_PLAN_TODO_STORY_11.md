# Implementation Plan: Story 11 — Emitter Feature Expansion

> **Roadmap position:** Story 11 — **Tier 1 — Usable**
> **Direct dependencies (new numbering):** 05, 09
> **Renumbered from:** legacy Story 8
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Story:** Emitter Feature Expansion (Story 11 from `TODO.md`)
> **Depends on:** Story 09 (basic emitter producing PHP pass-through output — TyhpEmitter with AST-walking and EmitItem generation must be functional), Story 05 (BoundSymbol on AST nodes — emitter reads BoundSymbol for transformation decisions)
> **Scope:** Expand the emitter to handle all Tyhp-specific language features, transforming them into valid PHP equivalents
> **Key Directory:** `Tyhp/TyhpLang/Emitter/`
> **Status:** SUBSTANTIALLY COMPLETE (2026-07-31 audit) — inline emitter feature modules and `tests/conformance/story11/` fixtures landed (ADR accepted). Residual gaps (PHP&lt;8.4 property-accessor rewrite; several call-site edge cases) in `INCOMPLETE.md` / `FOUND_BUGS.md`.

---

## Architecture Decision (ADR) — Inline Emitter, not Transformer Dispatch

**Status:** Accepted — the implementation intentionally diverges from the original "14 `IEmitTransformer` classes" design below.

**Context.** The original plan specified a `Transformers/` directory of 14 `IEmitTransformer` classes, with `TyhpEmitter` dispatching each AST node to the first transformer whose `CanTransform(node)` returned true. During implementation this was found to be a poor fit for an emitter:

- Emitters must **compose** features on a single node (e.g. `clone $obj with [prop => new Struct() with [...]]` touches `with`, struct, and generics at once). A "one transformer owns this node" dispatch model fights that composition; an inline walk that can call any feature's helper at any point composes cleanly.
- The inline pattern (one `EmitNode` switch + per-feature helper modules + a shared `EmitContext`) is the standard production emitter shape and is what the codebase actually uses.

**Decision.** Story 11 is implemented as an **inline emitter**: `TyhpEmitter.EmitNode` dispatches to `EmitX` methods across `TyhpEmitter.*` partials, backed by focused helper modules (`AliasConverter`, `StructEmissionHelper`, `TypeSpellingHelper`, `OperatorOverloadResolver`, `OperatorMethodNameGenerator`, `TypeNameFormatter`). There is **no `IEmitTransformer` interface, no `Transformers/` directory, and no per-node `CanTransform` dispatch.** The emit pipeline is the phased method sequence in `TyhpEmitter.Emit()` (`SplitSourceFile` → `ConvertAliasesForAll` → `BuildEmitTrees` → `PruneImportsForAll` → `MergeOutputFiles` → `GenerateAll`).

**Consequence for reading this doc.** The per-phase sections below remain the **behavioral specification** for each feature (emission patterns, naming conventions, PHP target-version rules, acceptance criteria). Read each phase's "New files to create" / "Transformer" framing as **"feature logic owned by the named inline helper / `TyhpEmitter.*` partial"** rather than as a literal standalone transformer class. The Appendix "Transformer Registration Order" is replaced (see its replacement table at the end of this doc) by a "Feature Emission Modules" map.

**What was removed from the codebase.** Seven no-op adapter classes (`TypeAliasTransformer`, `TraitRequirementTransformer`, `OverloadTransformer`, `StructTransformer`, `AsyncAwaitTransformer`, `DisposableTransformer`, `CompileTimeTransformer`) and the `IEmitTransformer` interface. Their `CanTransform`/`TransformDeclaration`/`TransformExpression` methods were never invoked by `EmitNode` — the real logic already lived inline. The only live side effect of the adapters (`RequirePackage("tyhp/async")` in two adapters' `Initialize`) was already duplicated at the inline emission sites, so removal changed no behavior.

---

## Project Context

The Tyhp compiler is a C# application that compiles `.tyhp` source files (a superset of PHP) into valid PHP output. The compilation pipeline is: **Parse → Bind → Check → Optimize (Story 23) → Emit → Write Files**. Story 11 assumes that the basic emitter (Story 09) is already functional — it can walk a bound AST and emit PHP code for standard PHP constructs. **Story 09 must be complete before Story 11 begins.** The `TyhpEmitter` class must have a working AST-walking emission pipeline that produces valid PHP for standard PHP constructs (class declarations, function declarations, statements, expressions, etc.). Story 11 then extends this base with Tyhp-specific transformers. This story expands the emitter to handle Tyhp-specific language features that have no direct PHP equivalent and must be transformed during emission.

**Note:** The optimizer (Story 23) runs before the emitter. Extension method and operator call sites may already be inlined by the optimizer, meaning the emitter's extension method/operator transformers may see fewer synthetic static calls than expected. The emitter must handle both optimized and unoptimized ASTs gracefully — if a call site has already been rewritten by the optimizer, the emitter should emit the direct method call rather than the synthetic static dispatch.

The emitter operates on a bound, checked AST. It uses the `EmitItem` tree-based code generation primitive (which supports indentation, children, sorted output, and doc comment wrapping). Output is organized into `PHPOutputFile` instances, each representing a single PHP output file. The existing `PHP8.3/` subdirectory contains empty placeholder files for version-specific emitter logic (`ClassFile.cs`, `FunctionFile.cs`, `ObjectDefinition.cs`, `ObjectMethodMember.cs`, `ObjectPropertyMember.cs`, `ObjectConstMember.cs`).

Each Tyhp feature translates to PHP via a specific strategy:
- **Structs** → associative arrays
- **Generics** → type erasure + optional runtime checks
- **Extension methods** → static method call rewriting
- **Operator overloads** → generated PHP methods + call-site rewriting
- **Type aliases** → compile-time erasure
- **`with` keyword** → property assignment sequences
- **Disposables (`:=`)** → scope-based auto-dispose via `DisposableScope`
- **Async/await** → Promise/Fiber library calls
- **Compile-time constructs** → constant folding
- **Short function syntax** → standard function declarations
- **Function overloads** → single merged implementation
- **Type guards** → boolean functions (erasure of `$param is Type` return syntax)
- **Trait requirements** → erasure (validation-only)
- **Tyhp import/use** → PHP use statements

---

## Architecture Overview

### Emitter Module Organization

The emitter expansion is organized as a set of focused, single-responsibility **inline helper modules** under `Tyhp/TyhpLang/Emitter/`. Each Tyhp feature's emission logic lives in a dedicated helper class or `TyhpEmitter.*` partial. The central `TyhpEmitter` class drives a single `EmitNode` AST walk that calls these helpers as needed (see the ADR above for why this replaced the original per-node transformer dispatch).

**Directory structure (actual):**

```
Tyhp/TyhpLang/Emitter/
├── TyhpEmitter.cs                    # Main orchestrator: Emit() pipeline + EmitNode dispatch
├── TyhpEmitter.Declarations.cs       # Class/function/extension decl emission (incl. async fn, using block)
├── TyhpEmitter.Expressions.cs        # Expression emission (await, nameof/default/typeof, binary ops, member access)
├── TyhpEmitter.Statements.cs         # Statement emission (using block → try/finally, disposable scopes)
├── TyhpEmitter.Types.cs              # Type hint / spelling emission
├── TyhpEmitter.Helpers.cs            # Shared emit helpers
├── TyhpEmitter.OperatorOverloads.cs  # Operator-overload declaration emission (incl. convert)
├── TyhpEmitter.Disposables.cs        # WeakReference capture + try/finally circular fallback
├── TyhpEmitter.Async.cs              # Async-foreach desugaring + Promise::run auto-start + async closures
├── EmitContext.cs                    # Shared context (GlobalScope, Diagnostics, AdditionalImports, Disposables, AsyncForeachKinds, ...)
├── EmitHelpers.cs                    # Shared utilities (unique var names, IsStructType, type hints)
├── EmitItem.cs                       # Emit primitive (existing, working)
├── PHPOutputFile.cs                  # Output file model (incl. ConvertAliases, PruneFileImports)
├── PHPOutputFileSplitter.cs          # Splits one SrcFileAst into PSR-4 output files
├── OutputFileWriter.cs               # Writes generated PHP to disk
├── OutputPathResolver.cs             # Resolves output file paths
├── AstWalker.cs                      # AST traversal helpers
├── AliasConverter.cs                 # Type-alias expansion + extension-method & operator call-site rewriting
├── StructEmissionHelper.cs           # Struct → array emission (decl erasure, construction, property access, struct `with`)
├── TypeSpellingHelper.cs            # Type-alias/generic/type-guard → PHP type spelling
├── OperatorOverloadResolver.cs      # Resolves which operator overload applies at a call site
├── OperatorMethodNameGenerator.cs    # Operator overload PHP method naming (exact static names, convert names)
├── PHP8.3/                           # Version-specific emit logic (existing placeholders)
│   ├── ClassFile.cs
│   ├── FunctionFile.cs
│   ├── ObjectDefinition.cs
│   ├── ObjectMethodMember.cs
│   ├── ObjectPropertyMember.cs
│   └── ObjectConstMember.cs
└── NameGeneration/
    └── TypeNameFormatter.cs          # Type-to-string formatting for generated names
```

> **Note:** There is no `Transformers/` directory and no `IEmitTransformer` interface. Feature logic that the original plan attributed to a `XxxTransformer.cs` file is instead owned by the helper/partial listed in the "Feature Emission Modules" appendix at the end of this doc.

### Transformation Pipeline

Emission is driven by `TyhpEmitter.Emit()`, which runs a fixed phased sequence over all parsed files, then a single `EmitNode` AST walk per output file. There is no per-node transformer dispatch; ordering is enforced by the phase sequence and by the order in which `EmitNode` visits children. The ordering constraints below are still meaningful — features that must run before others are handled earlier in the phase sequence or earlier in the walk.

**Pre-emit phases (in `Emit()` order):**
1. **SplitSourceFile** — split each `SrcFileAst` into `PHPOutputFile`s (PSR-4).
2. **ConvertAliasesForAll** — resolve all type-alias references to their underlying types (must happen first so every later phase sees concrete types). Also rewrites extension-method and operator-overload call sites.
3. **BuildEmitTrees** — the `EmitNode` walk that builds `EmitItem` trees. During this walk:
   - **Type aliases / generics / type guards** — erased inline by `TypeSpellingHelper` as type hints are emitted.
   - **Trait requirements** — `extends`/`implements` stripped in `EmitObjectDeclaration`.
   - **Function overloads** — overload-signature declarations erased (binder/visitor already skip them).
   - **Structs** — declarations erased; construction/property-access emitted as arrays by `StructEmissionHelper`.
   - **Extension methods** — call sites rewritten to static calls by `AliasConverter`.
   - **Operator overloads** — declarations emitted as methods; usage rewritten to method calls by `AliasConverter`/`OperatorOverloadResolver`.
   - **`with` keyword** — emitted as property assignments / `array_replace` (struct) / `ObjectHelper::with()` / PHP 8.5 `clone(...)`.
   - **Disposables** — `DisposableScope` + `__destruct` emission; `using` block → try/finally; WeakReference `$this` capture; try/finally circular fallback; `using await` → `_await(disposeAsync())`.
   - **Async/await** — `Promise::_async` / `_await` wrapping; async-foreach desugaring; `Promise::run` auto-start on application entry points.
   - **Compile-time constructs** — `nameof`/`typeof`/`default` folded to constants; `variable_exists($v)` → `\array_key_exists('v', \get_defined_vars())`.
   - **Short function syntax** — `fn name(...) => e` expanded to `function name(...) { return e; }`.
4. **PruneImportsForAll** — drop `use` statements for erased types; consolidate `EmitContext.AdditionalImports` into the file header.
5. **MergeOutputFiles** — merge non-PSR4 fragments per output path.
6. **GenerateAll** — render `EmitItem` trees to PHP text.

### Shared Infrastructure

**`EmitContext`** — A context object shared across the emit walk, containing:
- Reference to the `GlobalScope` from the binder (for symbol lookups)
- Reference to the `DiagnosticBag` for emitter-phase diagnostics
- Reference to the `Project` configuration (for options like `build.structBacking`, `build.decimalBacking`, etc.)
- A unique name generator (for generating collision-free variable/method names)
- A map of active disposable variables per scope (for disposable emission)
- Source file information for diagnostic reporting

**`EmitHelpers`** — Shared utility methods:
- Type name formatting for generated PHP method names (following the naming conventions documented in `Examples/OperatorOverloads.tyhp`)
- Unique variable name generation (e.g., `$__scope` style names for disposable scopes)
- PHP type hint emission (stripping Tyhp-only type annotations that PHP cannot represent)
- Doc comment generation and transfer
- **Class name emission:** When emitting class name references in generated PHP code, ALWAYS use `::class` syntax (e.g., `\App\Models\User::class`) instead of quoted strings (e.g., `'App\\Models\\User'`). The `::class` syntax is autoload-safe, refactoring-friendly, and avoids double-backslash escaping. Only fall back to quoted strings for scalar type names (`'int'`, `'string'`, `'bool'`, `'float'`, `'mixed'`, `'void'`, `'array'`, `'null'`, `'callable'`) that do not support `::class`.
- **Inferred closure parameter types:** When the checker has inferred parameter types for a closure/fn expression (via contextual type inference from Story 08, Phase 5), the emitter MUST include these types in the generated PHP output. A Tyhp source `fn ($u) => $u->age` with `$u` inferred as `User` must emit as `fn (\App\Models\User $u) => $u->age`. The inferred types are stored on the closure's parameter symbols by the checker.

### Placeholder Strategy

- Use `// PLACEHOLDER_PHASE_N: description` for functionality belonging to later phases within this story
- Use `// PLACEHOLDER_STORY_N: description` for functionality belonging to other stories (e.g., Story 17 sourcemaps, Story 04 TyhpLib runtime)
- Each phase's instructions include searching for and implementing relevant placeholders from prior phases

### Safety and Backup Strategy

Before making any potentially destructive changes to existing files:
- Create timestamped backups using format `<filename>.bak.<timestamp>` (e.g. `<filename>.bak.20260216_143000`)
- Prefer incremental edits over wholesale file replacement
- Never use `git reset`, `git revert`, `git checkout .`, `git clean`, or similar destructive git commands

---

## Phase 1: Emitter Infrastructure — Context, Helpers, and Transformer Interface




### Phase Overview

Establish the shared infrastructure that all feature-specific transformers depend on. This includes the `EmitContext` class, the `IEmitTransformer` interface, the `EmitHelpers` utility class, and the `TypeNameFormatter` / `OperatorMethodNameGenerator` naming utilities. After this phase, the emitter has a clear extension point for each Tyhp feature without any feature logic yet implemented.

### Deliverables

**New files to create:**

- `Tyhp/TyhpLang/Emitter/EmitHelpers.cs` — Shared utility methods
- `Tyhp/TyhpLang/Emitter/Transformers/IEmitTransformer.cs` — Transformer interface
- `Tyhp/TyhpLang/Emitter/NameGeneration/TypeNameFormatter.cs` — Type-to-string formatting
- `Tyhp/TyhpLang/Emitter/NameGeneration/OperatorMethodNameGenerator.cs` — Operator method naming

**Modified files:**

- `Tyhp/TyhpLang/Emitter/EmitContext.cs` — Merge new properties into existing EmitContext
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Add orchestration skeleton with transformer registration

**Important:** `EmitContext.cs` already exists from Story 09 Phase 1 (with properties: `GlobalScope`, `DiagnosticBag`, `EmitConfig`, `CurrentFile`, `OutputFiles`, `UsedImports`, `TyhpdefAliasMap`, `TypeAliasMap`, and tracking state) and was further modified by Story 10 Phase 6 (adding `RequiredPackages`). Story 11 must **merge** its new properties (`UniqueNameGenerator`, `DisposableTracker`, `Project` reference) into the existing `EmitContext`, keeping all existing properties intact. Do NOT recreate the file from scratch.

### Implementation Details

**`EmitContext.cs`:**
- Properties: `GlobalScope`, `DiagnosticBag`, `Project` config reference, `SrcFileAst` current file
- Add `public HashSet<string> AdditionalImports { get; } = new();` to `EmitContext`. This property is used by transformers (Phase 4+) that generate code requiring additional imports — each transformer adds required fully-qualified names to this set, and the `ImportTransformer` (Phase 10, order 14) collects them into the final `use` statement block.
- A `UniqueNameGenerator` component that produces collision-free variable names (e.g., `$__scope` with unique suffixes for nested disposable scopes)
- A `DisposableTracker` component that tracks active disposable variables per scope depth
- A `GetSymbolForAst(IBase2Ast node)` method — simply returns `node.BoundSymbol` (Story 05 ensures all declaration and reference AST nodes have their `BoundSymbol` property set by the binder)
- **Note:** The `OwningFile` property on `IBase2Ast` (added in Story 05) provides direct access to the `SrcFileAst` for any AST node. Use `astNode.OwningFile` to retrieve the source file context.
- A method to check configuration flags (e.g., `IsStructBackedByArray()`, `GetDecimalBacking()`)

**`IEmitTransformer.cs`:**
- `void Initialize(EmitContext context)` — called once with the shared context
- `bool CanTransform(IBase2Ast node)` — check if this transformer handles a given AST node
- `EmitItem? TransformDeclaration(IBase2Ast node)` — transform a declaration-level AST node (class member, function, etc.)
- `EmitItem? TransformExpression(IBase2Ast node)` — transform an expression-level AST node (operator usage, method call, etc.)
- `void PreProcess(IEnumerable<SrcFileAst> files)` — optional pre-emit pass over all files
- `void PostProcess()` — optional cleanup after all files are emitted

**`TypeNameFormatter.cs`:**
- Implement the type-to-PHP-method-name-segment formatting rules documented in `Examples/OperatorOverloads.tyhp`:
  - `self` → `"This"`
  - Other types → first character capitalized
  - Union types → `"Or"` separator (e.g., `int|array|bool` → `"IntOrArrayOrBool"`)
  - Nullable types → omit `"Null"` from the name
  - `int|float` → `"Number"` shortcut
  - `int|string|float|bool|array` → `"Scalar"` shortcut
  - `mixed` → `""` or `"Any"` depending on context
  - Generic types → `"Of"` with underscore-separated parameters (e.g., `MyClass<int, float>` → `"MyClassOfInt_Float"`)
- Method `FormatTypeName(IType typeAst, bool isLeftOperand)` returning a string segment

**`OperatorMethodNameGenerator.cs`:** (REDESIGNED — see §8)
- Maps each `OverloadableOperator` enum value to its single exact PHP method name (`__add`,
  `__subtract`, `__isLessThan`, `__compare`, unary `__asNumeric`/`__negate`/`__not`, …)
- Convert target → method + interface (`int`→`__toInt`/`IntConvertible`, `string`→`__toString`/
  `StringConvertible`, etc.); convert-from → static `__from`
- No `__OP_*` type-specific names, no `__addThisTo` fallbacks, no `_N` collision suffix — a
  name conflict with a hand-written method is a compile-time error handled by the checker

**`EmitHelpers.cs`:**
- `string GenerateUniqueVarName(string prefix)` — generates a unique PHP variable name
- `string EmitPhpTypeHint(IType tyhpType)` — converts a Tyhp type AST to its PHP type hint string (stripping generics, expanding type aliases, handling Tyhp-only types)
- `List<string> EmitDocComment(string? docComment, IBaseAst? provider)` — formats doc comments for EmitItem
- `bool IsStructType(IBase2Ast node, EmitContext context)` — checks if `node.BoundSymbol` resolves to a struct declaration symbol
- `bool IsExtensionMethodCall(IBase2Ast node, EmitContext context)` — checks if `node.BoundSymbol` is an extension method symbol

**`TyhpEmitter.cs` modifications:**
- Add a `List<IEmitTransformer> Transformers` property
- Add a `RegisterTransformer(IEmitTransformer transformer)` method
- Add an `Emit(EmitContext context, IEnumerable<SrcFileAst> files)` method skeleton that:
  1. Calls `PreProcess()` on all transformers
  2. Walks each file's AST
  3. For each node, checks transformers via `CanTransform()` and delegates to the matching transformer
  4. Falls back to default PHP emission for non-transformed nodes
  5. Calls `PostProcess()` on all transformers
- Register all transformers in the correct order (placeholders for transformers not yet created)

### Acceptance Criteria

- All new files compile without errors
- `TyhpEmitter` has the transformer registration and orchestration skeleton
- `TypeNameFormatter` correctly formats all documented type name patterns (based on the rules in `OperatorOverloads.tyhp`)
- `OperatorMethodNameGenerator` correctly generates method names for all `OverloadableOperator` enum values
- `EmitContext` can be constructed with the required dependencies
- `EmitHelpers.GenerateUniqueVarName()` produces names that do not collide across multiple calls
- No existing functionality is broken

### Dependencies

- **Requires:** Story 09 basic emitter (functional AST-walking emission), Story 01 diagnostic system, Story 02 binder (GlobalScope, symbols), Story 05 (BoundSymbol on AST nodes)
- **Provides for:** All subsequent phases (2-10) depend on these shared utilities

---

## Phase 2: Struct Emission — Structs to Associative Arrays




### Phase Overview

Implement the `StructTransformer` that converts Tyhp struct declarations and usage into PHP associative array operations. Struct declarations are erased from output (compile-time only), struct instantiation becomes array construction, and struct property access becomes array key access. This is one of the most straightforward transformations and serves as a good first feature transformer to validate the infrastructure from Phase 1.

### Deliverables

**New files to create:**

- `Tyhp/TyhpLang/Emitter/Transformers/StructTransformer.cs` — Main struct transformation logic

**Existing files to modify:**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Register `StructTransformer`

### Implementation Details

**`StructTransformer.cs`:**

The transformer must handle several struct-related AST patterns:

1. **Struct declarations (`StructDeclarationAst`):**
   - Emit nothing — struct declarations are erased from PHP output
   - Record the struct's property schema (names, types, defaults, aliases) in the `EmitContext` for use when emitting struct instantiation and property access

2. **Anonymous struct declarations (`AnonymousStructDeclarationAst`):**
   - Same as named struct — erased from declarations
   - The instantiation is handled separately

3. **Struct instantiation (`new MyStruct()` / `new struct { ... }`):**
   - Emit as an associative array literal with all properties and their default values
   - For `new MyStructType()`: emit `['intVal' => 0, 'floatVal' => null, 'String Value 1243***' => ""]` (using defaults from struct declaration)
   - For anonymous structs: emit as inline array with the declared properties and defaults
   - Handle `with` keyword on struct construction in coordination with `WithKeywordTransformer` (Phase 6)

4. **Struct property access (`$s->prop`):**
   - Rewrite to array key access: `$s['prop']`
   - For aliased properties (e.g., `string 'String Value 1243***' as $strVal`): rewrite `$s->strVal` to `$s['String Value 1243***']`

5. **Struct property assignment (`$s->prop = value`):**
   - Rewrite to array key assignment: `$s['prop'] = value`

6. **Struct `clone`:**
   - Emit as simple array copy (arrays are value types in PHP, so assignment already copies)
   - `clone $s` → `$s` (no-op for arrays)
   - `clone $s with [...]` is handled by the `WithKeywordTransformer`

7. **Struct configurable backing:**
   - Read `build.structBacking` from `Project` config via `EmitContext`
   - Default `"array"` backing uses the array pattern described above
   - If a custom class is specified, emit instantiation using that class instead
   - `// PLACEHOLDER_STORY_10: Custom struct backing class support beyond arrays`

8. **Runtime validation (strict mode):**
   - `// PLACEHOLDER_STORY_08: Emit runtime type validation for struct properties when checker strict mode is enabled`

**Key considerations:**
- The transformer needs access to binder symbols to resolve struct type references and look up property schemas (names, aliases, defaults)
- Aliased properties are a key feature: the PHP array key is the alias string, not the property name
- Optional properties (nullable without default) may or may not be present in the array — emit them as `null` in the default construction

### Acceptance Criteria

- Struct declarations are completely erased from PHP output
- `new MyStructType()` emits as an associative array with all properties and their default values
- `$s->prop` on a struct variable emits as `$s['prop']`
- `$s->strVal` where `strVal` is an alias for `'String Value 1243***'` emits as `$s['String Value 1243***']`
- Struct property assignment `$s->prop = val` emits as `$s['prop'] = val`
- Anonymous struct instantiation emits as an inline array
- The `StructTransformer` is properly registered in `TyhpEmitter` and integrates with the Phase 1 infrastructure
- Code compiles without errors

### Dependencies

- **Requires:** Phase 1 (EmitContext, IEmitTransformer, EmitHelpers, TypeNameFormatter)
- **Provides for:** Phase 6 (`with` keyword uses struct-specific array merge logic)

---

## Phase 3: Type Aliases, Trait Requirements, and Simple Erasures




### Phase Overview

Implement the transformers for compile-time-only features that are erased or simplified during emission. These are the simplest transformations: type aliases are resolved and erased, trait `extends`/`implements` requirements are stripped, function overload signatures are removed (keeping only the implementation), and type guard return type syntax is simplified to `bool`.

### Deliverables

**New files to create:**

- `Tyhp/TyhpLang/Emitter/Transformers/TypeAliasTransformer.cs` — Type alias resolution and erasure
- `Tyhp/TyhpLang/Emitter/Transformers/TraitRequirementTransformer.cs` — Trait requirement erasure
- `Tyhp/TyhpLang/Emitter/Transformers/OverloadTransformer.cs` — Function overload signature erasure
- `Tyhp/TyhpLang/Emitter/Transformers/TypeGuardTransformer.cs` — Type guard return type simplification

**Existing files to modify:**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Register all four transformers

### Implementation Details

**`TypeAliasTransformer.cs`:**
- In `PreProcess()`: build a map of all type alias declarations (both file-level `type X = Y` and class-scoped `public type X = Y`)
- During emission: wherever a type reference appears that matches a type alias, replace it with the underlying type
- Erase type alias declarations entirely from output (they produce no PHP code)
- Handle class-scoped type alias references (e.g., `self\MyObjOrNull`, `MyObject\MyObjOrNull`)
- Handle nested type aliases (alias of alias)
- Produce a diagnostic if a circular type alias is detected (this should also be caught by the checker, but defensive emission is good)

**`TraitRequirementTransformer.cs`:**
- For trait declarations that have `extends` or `implements` clauses in Tyhp:
  - Strip the `extends` clause (PHP traits cannot extend)
  - Strip the `implements` clause (compile-time validation only)
  - Emit the trait as a standard PHP trait declaration

**`OverloadTransformer.cs`:**
- For function/method declarations that have overloaded signatures:
  - Strip all overload signature declarations (they are compile-time-only)
  - Keep only the implementation body
  - The implementation must already cover all overload variants (validated by checker)
- Handle both standalone functions and class methods

**`TypeGuardTransformer.cs`:**
- For functions/methods with return type `$param is Type` or `$param instanceof Type`:
  - Replace the return type with `bool` in the PHP output
  - The function body remains as-is (it already returns a boolean)
- Emit built-in type guard calls (`is_array()`, `is_string()`, etc.) unchanged

### Acceptance Criteria

- Type alias declarations produce no PHP output
- All references to type aliases in type hints are replaced with their underlying types
- Class-scoped type aliases (e.g., `MyObject\MyObjOrNull`) are resolved correctly
- Trait `extends`/`implements` clauses are stripped from PHP output
- Function overload signatures are stripped; only the implementation body is emitted
- Type guard `$param is Type` return types emit as `bool`
- All files compile without errors

### Dependencies

- **Requires:** Phase 1 (infrastructure), Story 02 binder (for type alias resolution, symbol lookup)
- **Provides for:** All subsequent phases benefit from type aliases being resolved before emission

---

## Phase 4: Extension Methods — Static Method Call Rewriting




### Phase Overview

Implement the `ExtensionMethodTransformer` that rewrites extension method call sites from instance method call syntax (`$value->extensionMethod($args)`) to static method calls (`ExtensionClass::extensionMethod($value, $args)`). Extension method declarations are already static methods on extension classes in Tyhp, so the declaration side needs minimal transformation. The call-site rewriting is the core of this transformer.

### Deliverables

**New files to create:**

- `Tyhp/TyhpLang/Emitter/Transformers/ExtensionMethodTransformer.cs` — Extension method call rewriting

**Existing files to modify:**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Register `ExtensionMethodTransformer`

### Implementation Details

**`ExtensionMethodTransformer.cs`:**

**Note:** Extension calls remain as regular binary expressions or method calls in the AST. They are identified by examining the `BoundSymbol` (from Story 05): an `ObjectOperatorOverloadMethodSymbol` with `IsExtensionOperator == true` indicates an extension operator call, and an `ObjectMethodSymbol` belonging to an extension class indicates an extension method call.

1. **Extension method declaration emission:**
   - Extension declarations (`ExtensionDeclarationAst`) contain static methods where the first parameter has the `extends` keyword
   - Emit the extension class as a standard PHP class with static methods
   - Strip the `extends` keyword from the first parameter (it becomes a regular parameter in PHP)
   - Ensure the extension class has appropriate namespace and is importable

2. **Extension method call-site rewriting:**
   - Detect calls in the form `$value->extensionMethod($arg1, $arg2)` where `extensionMethod` resolves to an extension method (via binder symbol lookup)
   - Rewrite to: `ExtensionClass::extensionMethod($value, $arg1, $arg2)`
   - The object the method is called on becomes the first argument
   - Use the fully-qualified class name or ensure the import is present

3. **Chained extension method calls:**
   - Handle chains like `$value->ext1()->ext2()->ext3()`
   - Each link in the chain is independently rewritten
   - The result of the previous call becomes the first argument to the next
   - Example: `ExtensionClass3::ext3(ExtensionClass2::ext2(ExtensionClass1::ext1($value)))`

4. **Extension methods on scalar types:**
   - Handle extension methods on `int`, `string`, `float`, `bool`, `array`
   - These follow the same rewriting pattern
   - Example from docs: `$str->toCamelCase()` → `StringExtensions::toCamelCase($str)`

5. **Extension methods on nullable types:**
   - If the type is nullable, the call-site rewriting still applies
   - Runtime null check behavior depends on the extension method's parameter type
   - If the first parameter is not nullable, PHP will throw a TypeError at runtime (which is correct)

6. **Import management:**
   - Ensure the extension class is imported (via `use` statement) in the output file
   - Add the required fully-qualified extension class name to `EmitContext.AdditionalImports` (`HashSet<string> AdditionalImports`). Any transformer that generates code requiring additional imports (e.g., `ExtensionMethodTransformer`, `StructTransformer`, `AsyncTransformer`) adds the required fully-qualified names to `EmitContext.AdditionalImports`. The `ImportTransformer` (which runs last at position 14) reads from this collection and generates the corresponding `use` statements. This eliminates the need for earlier transformers to directly communicate with the `ImportTransformer`.

**Key considerations:**
- The binder's symbol resolution is critical: the transformer must be able to determine whether a method call is an extension method or a regular instance method
- Extension methods are resolved based on the type of the receiver and the `extends` parameter of extension methods in scope
- If multiple extension methods match, the checker should have already validated this — the emitter uses the binder's resolved symbol

### Acceptance Criteria

- Extension class declarations emit as standard PHP classes with static methods
- The `extends` keyword is stripped from the first parameter in extension method declarations
- `$value->extensionMethod($arg1)` emits as `ExtensionClass::extensionMethod($value, $arg1)` when the method is an extension method
- Chained extension method calls are correctly nested
- Extension methods on scalar types work correctly
- The extension class is properly imported in the output file
- Regular instance method calls are not affected
- Code compiles without errors

### Dependencies

- **Requires:** Phase 1 (infrastructure), Story 02 binder (symbol resolution to identify extension methods)
- **Provides for:** Phase 5 (operator overloads may interact with extension methods)

---

## Phase 5: Operator Overloads — Declaration and Call-Site Rewriting




### Phase Overview

> **REDESIGNED (final).** The original scheme below (instance methods, `__OP_<left>_<op>_<right>`
> type-specific methods, `__add`/`__addThisTo` dual dispatch, `__castAs`, `_N` collision suffixes)
> has been REPLACED. The current design is: **all generated methods are `static`** (except
> `convert`'s to-form `__to{T}()`, which stays an instance method for `\Stringable`/`*Convertible`
> conformance); **all forms of one operator collapse into a single method** with union-typed
> operands/return that dispatches internally via `instanceof`/`is_*` and throws
> `InvalidParametersForOperatorOverloadException` for unaccepted combos; **names are exact,
> deterministic and reserved** (a conflicting hand-written method is a compile-time error — no
> suffixing, no fallback methods); the **`true`/`false`/`null` operators are removed**. See
> `AIDevGuide/guide/11-operator-overloading.md` for the authoritative naming table and rules, and
> the summary-table row 8 below. The historical detail is retained below for context only.

Implement the operator overload system. Operator overload declarations are emitted as PHP methods on the class (with generated names following the documented naming convention). Operator usage at call sites is rewritten to method calls. Additionally, a unified dispatch method is generated for each operator that routes to the correct type-specific implementation.

### Deliverables

**New files to create:**

- `Tyhp/TyhpLang/Emitter/Transformers/OperatorOverloadTransformer.cs` — Operator overload declaration and call-site rewriting

**Existing files to modify:**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Register `OperatorOverloadTransformer`

### Implementation Details

**`OperatorOverloadTransformer.cs`:**

This transformer handles three distinct aspects:

**A. Operator overload declaration emission:**

1. For each `OperatorOverloadDeclarationAst` in a class:
   - Emit the operator body as a PHP method with a generated name
   - Use `OperatorMethodNameGenerator` (Phase 1) for the method name
   - Internal method: `protected function __OP_<leftType>_<OP>_<rightType>($value): <returnType>` for binary operators
   - Internal method: `public function __<opName>(): <returnType>` for unary operators that have no overloaded variants

2. Generate unified dispatch methods for each operator:
   - For binary operators where left operand is `self`: generate `public function __add($value): <unionReturnType>` that dispatches based on `$value` type to the correct `__OP_*` method
   - For binary operators where right operand is `self` (fallback): generate `public function __addThisTo($value): <unionReturnType>`
   - Use `match(true)` or `instanceof` checks for dispatch
   - For unary operators: the unified method IS the single implementation (no dispatch needed)

3. Handle conversion (`convert`) operator overloads:
   - `convert` from other type to `self`: generate `public static function __from(<types> $value): self`
   - `convert` from `self` to other type: generate `public function __to<Type>(): <type>` for each target type
   - Generate `public function __castAs(\Tyhp\Type $type): mixed` helper if any "to" conversions exist
   - The `__castAs` method includes PHP type juggling logic for scalar types as documented

4. Handle abstract and final operator overloads:
   - `abstract operator` → emit as abstract method
   - `final operator` → emit as final method

5. Handle name conflicts:
   - If a class already has a method named `__subtract` and also defines the `-` operator overload, append `_2` (or higher) suffix
   - Use `OperatorMethodNameGenerator` to detect and resolve conflicts

**B. Call-site rewriting (operator usage):**

1. For binary expressions where one operand is an object type with operator overloads:
   - Follow the resolution order documented in `Examples/OperatorOverloads.tyhp` (the checker has already resolved which overload applies; the emitter uses the binder's resolved symbol)
   - Rewrite `$a + $b` → `$a->__add($b)` when left is the overloading type
   - Rewrite `10 + $a` → `$a->__addThisTo(10)` when right is the overloading type (fallback)

2. For unary expressions:
   - Rewrite `!$a` → `$a->__not()` if `!` is overloaded
   - Rewrite `++$a` → `$a = $a->__increment()` or `$a->__increment()` depending on semantics

3. For compound assignment operators:
   - Rewrite `$a += $b` → `$a = $a->__add($b)`
   - Handle all compound operators: `+=`, `-=`, `*=`, `/=`, `%=`, `**=`, `.=`, `&=`, `|=`, `^=`, `<<=`, `>>=`
   - **Side-effect safety:** When the left-hand side of a compound assignment is a non-simple expression (anything other than a plain variable `$var` or simple property access `$var->prop`), the emitter must extract it to a temporary variable to avoid double evaluation. For example, `$arr[$i++] += $b` must NOT emit as `$arr[$i++] = $arr[$i++]->__add($b)` (which evaluates `$i++` twice). Instead, emit: `$__tmp = &$arr[$i++]; $__tmp = $__tmp->__add($b);`. The criteria for "non-simple" expressions: array access with non-constant index, method call results, or any expression with side effects.

4. For comparison operators used in control flow:
   - Rewrite `if ($a < $b)` → `if ($a->__isLessThan($b))` if applicable
   - Handle spaceship operator `<=>` for sorting contexts

5. For conversion/cast usage:
   - Rewrite `(int)$myObj` → `$myObj->__toInt()` if convert operator exists
   - Rewrite implicit conversions (passing object where scalar expected) → explicit `__to<Type>()` call

**C. Pre-processing:**
- In `PreProcess()`: scan all class declarations for operator overloads and build a map of which classes have which operator overloads, used for call-site resolution

### Acceptance Criteria

- Operator overload declarations emit as correctly-named PHP methods following the documented naming convention
- Unified dispatch methods are generated for operators with multiple type-specific implementations
- Binary operator usage between object and scalar emits as method call on the object
- Fallback methods (`__addThisTo`, etc.) are generated when the `self` type is the right operand
- Unary operator usage emits as method call on the object
- Compound assignment operators are correctly expanded
- Conversion operators generate `__from()`, `__to<Type>()`, and `__castAs()` methods
- Abstract and final modifiers are preserved on generated methods
- Method name conflicts are resolved with numeric suffixes
- Code compiles without errors

### Dependencies

- **Requires:** Phase 1 (OperatorMethodNameGenerator, TypeNameFormatter, EmitContext), Story 02 binder (symbol resolution for determining which overload applies at call sites), Story 08 checker (operator resolution)
- **Provides for:** Phase 2 struct operations may involve operator overloads on custom backing types

---

## Phase 6: `with` Keyword and Short Function Syntax




### Phase Overview

Implement the `WithKeywordTransformer` for the `with` keyword construct and the `ShortFunctionTransformer` for the short named function syntax. These are both syntactic sugar transformations. The `with` keyword expands to property assignment sequences (with different strategies for objects, cloned objects, and structs). Short function syntax expands to standard function declarations with return statements.

### Deliverables

**New files to create:**

- `Tyhp/TyhpLang/Emitter/Transformers/WithKeywordTransformer.cs` — `with` keyword → property assignments
- `Tyhp/TyhpLang/Emitter/Transformers/ShortFunctionTransformer.cs` — Short `fn` syntax → standard functions

**Existing files to modify:**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Register both transformers

### Implementation Details

**`WithKeywordTransformer.cs`:**

The `with` keyword has three forms in Tyhp:

1. **`clone $obj with [...]`** — clone an object and modify properties on the clone
2. **`new MyClass(...) with [...]`** — create a new object and modify properties after construction
3. **`$obj with [...]`** — modify properties in-place on an existing object (no clone, no new)

The emission strategy varies by form, PHP target version, statement vs expression context, and whether readonly properties are involved.

---

**Form 1: `clone ... with`**

*PHP 8.5+ — native `clone()` function syntax:*

PHP 8.5 introduced `clone($object, $propertyOverrides)` as a function-style call (see [PHP 8.5 Release: Clone With](https://www.php.net/releases/8.5/en.php#clone-with) and [RFC: Clone with v2](https://wiki.php.net/rfc/clone_with_v2)). This handles readonly properties natively. When `output.phpVersion >= 8.5`, all `clone ... with` expressions emit using this syntax regardless of context:

```
// Tyhp: $a = clone $obj with [name => "John Doe", age => 23]
// PHP 8.5+:
$a = clone($obj, ['name' => 'John Doe', 'age' => 23]);

// Tyhp: foo(clone $obj with [name => "John"])
// PHP 8.5+:
foo(clone($obj, ['name' => 'John']));

// Tyhp: return clone $obj with [name => "John"]
// PHP 8.5+:
return clone($obj, ['name' => 'John']);
```

PHP 8.5 `clone()` works as both a statement and expression, so no context distinction is needed.

*PHP 8.2-8.4 — non-readonly properties:*

Statement context optimizes to direct property assignments:

```
// Tyhp: $a = clone $obj with [name => "John Doe"]
// PHP 8.2-8.4 (non-readonly):
$a = clone $obj;
$a->name = "John Doe";
```

Expression context uses `ObjectHelper::with()`:

```
// Tyhp: foo(clone $obj with [name => "John"])
// PHP 8.2-8.4 (non-readonly):
foo(\Tyhp\ObjectHelper::with(clone $obj, ['name' => 'John']));
```

*PHP 8.2-8.4 — readonly properties (opt-in via `build.experimentalReadonlyCloneWith`):*

When `build.experimentalReadonlyCloneWith` is `true` in `tyhp.json` and any `with` property targets a readonly field, the emitter generates an anonymous class wrapper that leverages PHP 8.2+'s `__clone()` readonly reinitializion. This approach uses `ReflectionClass::newInstanceWithoutConstructor()` to create the wrapper shell and reflection to copy properties from the source object, then clones the wrapper to trigger `__clone()`:

```
// Tyhp: $result = clone $myColor with [alpha => 128]
// Where Color has: public readonly int $alpha

// PHP 8.2-8.4 (readonly, opt-in enabled):
$result = (static function(\Color $__src, array $__overrides): \Color {
    $__wrapper = (new \ReflectionClass(new class extends \Color {
        /** @internal */ public array $__tyhp_overrides = [];

        public function __clone(): void
        {
            if (\method_exists(parent::class, '__clone')) {
                parent::__clone();
            }
            foreach ($this->__tyhp_overrides as $__k => $__v) {
                $this->$__k = $__v;
            }
            $this->__tyhp_overrides = [];
        }
    }))->newInstanceWithoutConstructor();

    foreach ((new \ReflectionObject($__src))->getProperties() as $__prop) {
        $__prop->setAccessible(true);
        if ($__prop->isInitialized($__src)) {
            $__prop->setValue($__wrapper, $__prop->getValue($__src));
        }
    }

    $__wrapper->__tyhp_overrides = $__overrides;
    return clone $__wrapper;
})($myColor, ['alpha' => 128]);
```

Key behavior:
- `parent::__clone()` runs FIRST, then overrides are applied — `with` values always win
- The anonymous class extends the concrete type known at compile time
- The source object's constructor was already called normally — `newInstanceWithoutConstructor()` only bypasses the constructor for the intermediate wrapper, not for the original object
- `$result instanceof Color` is `true`, but `\get_class($result)` returns the anonymous class name

If `build.experimentalReadonlyCloneWith` is `false` (default), the checker blocks `clone ... with` on readonly properties for PHP < 8.5 with `CheckerCloneWithReadonlyRequiresConfig` (code 4139).

**Restrictions:** `final` classes cannot be extended, so `clone ... with` on readonly properties of `final` classes is always blocked on PHP < 8.5 with `CheckerWithReadonlyFinalClass` (code 4140), even with the opt-in enabled.

---

**Form 2: `new ... with`**

*Non-readonly properties — all PHP versions:*

Statement context optimizes to direct property assignments:

```
// Tyhp: $x = new MyClass('blah', 2345.23) with [color => 'blue']
// PHP (non-readonly):
$x = new MyClass('blah', 2345.23);
$x->color = 'blue';
```

Expression context uses `ObjectHelper::with()`:

```
// Tyhp: return new MyClass() with [color => 'blue']
// PHP (non-readonly):
return \Tyhp\ObjectHelper::with(new MyClass(), ['color' => 'blue']);
```

*Readonly properties — PHP 8.5+:*

Use PHP 8.5's native `clone()` to wrap the `new` call, which handles readonly natively:

```
// Tyhp: $a = new MyObject('blah', 2345.23) with [name => 'Joe', age => 23]
// PHP 8.5+ (readonly):
$a = clone(new MyObject('blah', 2345.23), ['name' => 'Joe', 'age' => 23]);
```

*Readonly properties — PHP 8.2-8.4:*

The emitter wraps the `new` call in an anonymous class + clone. The anonymous class extends the target type and inherits its constructor, so constructor arguments pass through naturally with no reflection needed:

```
// Tyhp: $a = new MyObject('blah', 2345.23) with [name => 'Joe', age => 23]
// PHP 8.2-8.4 (readonly):
$a = clone (new class('blah', 2345.23) extends \MyObject {
    public function __clone(): void
    {
        if (\method_exists(parent::class, '__clone')) {
            parent::__clone();
        }
        $this->name = 'Joe';
        $this->age = 23;
    }
});
```

Key behavior:
- Constructor arguments (`'blah', 2345.23`) pass through to `MyObject`'s constructor via inheritance — constructor runs normally
- `clone` triggers `__clone()` which sets the readonly properties (allowed in PHP 8.2+)
- `parent::__clone()` runs FIRST, then overrides — `with` values always win
- No reflection needed (unlike the `clone ... with` variant)
- Property names and values are inlined at compile time (not runtime dynamic)
- `$a instanceof MyObject` is `true`, but `\get_class($a)` returns the anonymous class name

This approach is always enabled (no opt-in needed) because it adds minimal overhead (one extra clone) and requires no reflection.

*Mixed case (some readonly, some not):*

If ANY `with` property targets a readonly field, ALL properties are set inside `__clone()` for simplicity. The emitter does not split readonly and non-readonly properties into different assignment strategies for the same `with` expression.

**Restriction:** `final` classes cannot be extended, so `new ... with` on readonly properties of `final` classes is blocked on PHP < 8.5 with `CheckerWithReadonlyFinalClass` (code 4140).

---

**Form 3: `$obj with [...]` — in-place mutation**

This form modifies an existing object's properties without cloning or creating a new instance. The result is the same object reference:

```
// Tyhp: $a with [name => 'Sally', age => 31]
// PHP (statement context):
$a->name = 'Sally';
$a->age = 31;

// Tyhp: $b = $a with [name => 'Sally']
// PHP (expression context — $b is the SAME object as $a):
$b = \Tyhp\ObjectHelper::with($a, ['name' => 'Sally']);
```

**Readonly properties are always blocked** on in-place `with`. Since the object already exists and properties may already be initialized, there is no safe way to reinitialize readonly properties from outside the class scope. The checker emits `CheckerWithReadonlyInPlace` (code 4141): "Cannot modify readonly property '{0}' with in-place 'with'; use 'clone ... with' or 'new ... with' instead."

---

**Struct `with` (arrays) — all PHP versions, all forms:**

For struct types (backed by arrays), `with` always emits as `\array_replace()`:
```
// Tyhp: $s = $myStruct with [prop => newVal]
// PHP:
$s = \array_replace($myStruct, ['prop' => newVal]);
```
This works in both statement and expression contexts. The `ObjectHelper` is NOT used for structs. Structs do not have readonly concerns since they are value types.

---

**Nested `with` expressions:**

Handle nested `with` by processing inner `with` first, then outer. For example:
```
// Tyhp: $b = new MyClass() with [a => clone $c with [b => false]]
```
The inner `clone $c with [b => false]` is processed first (as an expression context), then the outer `with` uses the result.

---

**Configuration:**

Add `build.experimentalReadonlyCloneWith` (boolean, default `false`) to the project configuration (`tyhp.json`). When `true`, enables the anonymous class wrapper for `clone ... with` on readonly properties for PHP < 8.5. This config is read from `Project.Build` and passed to both the checker (to allow/block the pattern) and the emitter (to select the emission strategy). Note: `new ... with` on readonly does NOT require this flag — it is always enabled because the approach is simpler (no reflection).

---

**Emission strategy summary:**

| Form | Scenario | PHP 8.5+ | PHP 8.2-8.4 |
|---|---|---|---|
| `clone ... with` | non-readonly | `clone($obj, [...])` | direct assignments (stmt) / `ObjectHelper::with()` (expr) |
| `clone ... with` | readonly (non-final) | `clone($obj, [...])` | Anonymous class wrapper + reflection (opt-in) / **blocked** (default) |
| `clone ... with` | readonly + final | `clone($obj, [...])` | **Blocked by checker** (4140) |
| `new ... with` | non-readonly | direct assignments (stmt) / `ObjectHelper::with()` (expr) | same |
| `new ... with` | readonly (non-final) | `clone(new C(...), [...])` | `clone (new class(...) extends C { __clone() })` |
| `new ... with` | readonly + final | `clone(new C(...), [...])` | **Blocked by checker** (4140) |
| `$obj with [...]` | non-readonly | direct assignments (stmt) / `ObjectHelper::with()` (expr) | same |
| `$obj with [...]` | readonly | **Blocked by checker** (4141) | **Blocked by checker** (4141) |
| struct `with` | (all) | `\array_replace(...)` | `\array_replace(...)` |

**`ShortFunctionTransformer.cs`:**

1. **Short named function syntax:**
   - `fn myFunc(int $val): int => $val + 5;` → `function myFunc(int $val): int { return $val + 5; }`
   - Detect function/method declarations that use the arrow expression syntax (single expression body)
   - Expand to a standard function declaration with an explicit `return` statement wrapping the expression

2. **Short method syntax in classes:**
   - `public fn getVal(): int => 5;` → `public function getVal(): int { return 5; }`
   - Handle all method modifiers (public, protected, private, static, final, abstract)

3. **Distinction from PHP arrow functions:**
   - PHP arrow functions (`fn($x) => $x + 1`) are anonymous and already valid PHP — do NOT transform these
   - Only transform named `fn` declarations (functions and methods with the `fn` keyword and a name)

### Acceptance Criteria

- **`clone ... with`:** emits `clone($obj, [...])` on PHP 8.5+; direct assignments / `ObjectHelper::with()` on PHP < 8.5 for non-readonly; anonymous class wrapper on PHP 8.2-8.4 for readonly (opt-in via `build.experimentalReadonlyCloneWith`)
- **`new ... with`:** emits direct assignments / `ObjectHelper::with()` for non-readonly; `clone(new C(...), [...])` on PHP 8.5+ for readonly; anonymous class + clone wrapper on PHP 8.2-8.4 for readonly (always enabled)
- **`$obj with [...]`:** emits direct assignments / `ObjectHelper::with()` for non-readonly; readonly always blocked by checker
- Readonly `with` on `final` classes blocked for PHP < 8.5 (cannot subclass for anonymous wrapper)
- `with` on structs emits as `\array_replace()`
- Nested `with` expressions are handled correctly (inner first, then outer)
- Unique temporary variable names are generated for each `with` expression in statement context
- `fn myFunc(...): T => expr;` emits as `function myFunc(...): T { return expr; }`
- Short method syntax in classes emits correctly with all modifiers
- PHP arrow functions (anonymous `fn`) are NOT transformed
- Code compiles without errors

### Dependencies

- **Requires:** Phase 1 (EmitHelpers for unique names), Phase 2 (StructTransformer for struct-specific `with` handling)
- **Provides for:** Phase 7 (disposables may appear in `with` expressions)

---

## Phase 7: Disposables — Scope-Based Auto-Dispose




### Phase Overview

Implement the `DisposableTransformer` that transforms disposable variable assignments (`:=` operator) into scope-based auto-dispose using `\Tyhp\DisposableScope`. Instead of wrapping code in try/finally blocks, the transformer generates a `DisposableScope` variable. When that variable leaves scope, PHP's `__destruct()` fires and calls `dispose()` on all registered resources in reverse order. This works reliably across normal exits, exception paths, loops, and nested scopes — producing simpler output than try/finally wrapping.

### Deliverables

**New files to create:**

- `Tyhp/TyhpLang/Emitter/Transformers/DisposableTransformer.cs` — Disposable `:=` assignment → scope-based auto-dispose

**Existing files to modify:**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Register `DisposableTransformer`

### Implementation Details

**`DisposableTransformer.cs`:**

The disposable transformation is scope-aware. Instead of try/finally wrapping, it generates `DisposableScope` variables that auto-dispose via PHP's `__destruct()` mechanism.

1. **Disposable scope generation:**
   - When a scope contains any disposable assignments (`:=` or `using()`), generate a `DisposableScope` variable at the start of that scope
   - `$__scope = \Tyhp\DisposableScope::create();`
   - Resources are registered via `$__scope->using(expr)`, which returns the resource itself
   - When `$__scope` leaves scope, PHP's `__destruct()` fires and calls `dispose()` on all registered resources in reverse order

2. **`using()` function call pattern:**
   - When `using($var1 = expr1, $var2 = expr2)` is encountered:
     ```php
     $__scope = \Tyhp\DisposableScope::create();
     $var1 = $__scope->using(expr1);
     $var2 = $__scope->using(expr2);
     // ... remaining scope
     // $__scope auto-disposes when leaving scope
     ```

3. **`using()` in if-condition context:**
   - When `if (using($var = expr))` is encountered:
   - The scope is limited to the if-block:
     ```php
     $__scope = \Tyhp\DisposableScope::create();
     if ($__scope->using($var = expr)) {
         // if-block body
     }
     // $__scope auto-disposes when leaving this scope
     ```

4. **Bare `:=` assignment (without `using()`):**
   - `$resource := new MyDisposable()` without explicit `using()`
   - Generate a `DisposableScope` for the enclosing scope (if one doesn't already exist)
   - Register the resource: `$resource = $__scope->using(new MyDisposable())`
   - `$__scope` auto-disposes when the enclosing scope exits

5. **Nested disposable scopes:**
   - Each nested scope that contains disposables gets its own `$__scope` variable with a unique suffix (e.g., `$__scope_1`, `$__scope_2`)
   - Inner scopes dispose independently of outer scopes
   - Disposables within each scope are disposed in reverse order of registration

6. **Async disposables:**
   - `DisposableScope::__destruct()` handles async disposables automatically
   - `// Async dispose runtime support — resolved when Story 04 TyhpLib is complete (DisposableScope handles async disposables)`

7. **WeakReference-based closure captures:**
   - When the emitter detects a closure stored as a property on the same class, and the closure captures `$this`, it should generate a `WeakReference`-based capture instead of letting the closure capture `$this` directly
   - This prevents the most common circular reference pattern that would delay `DisposableScope.__destruct()` (closure → `$this` → property → closure)
   - The checker (Story 08) flags these closures on the scope/symbol; the emitter reads the flag and applies the transformation
   - Example — instead of:
     ```php
     $this->onReady = function() {
         $this->emit('ready');
     };
     ```
     the emitter generates:
     ```php
     $__weakSelf = \WeakReference::create($this);
     $this->onReady = function() use ($__weakSelf) {
         $__weakSelf->get()?->emit('ready');
     };
     ```

8. **try/finally fallback for unresolvable circular references:**
   - When the checker (Story 08) flags a disposable scope where circular references exist that `WeakReference` cannot resolve (e.g., bidirectional parent-child object graphs), the emitter falls back to the traditional try/finally pattern for that specific scope
   - This is a targeted fallback — only used when the checker explicitly flags the scope; most disposable scopes continue to use `DisposableScope`
   - Example fallback output:
     ```php
     $resource = $expr;
     try {
         // ... scope body ...
     } finally {
         $resource->dispose();
     }
     ```

9. **`using` block syntax (try/finally):**
    - The emitter handles the `using` block (reserved token `T_TYHP_USING`) by emitting a try/finally block — NOT `DisposableScope`
    - The `using` block does NOT use the `:=` operator — it uses standard assignment (`=`) or no assignment
    - Unlike bare `:=` assignments (which use `DisposableScope` and `__destruct()`), the `using` block guarantees deterministic disposal via try/finally regardless of circular references or GC behavior
    - Single resource example — Tyhp source:
      ```tyhp
      using (db = new DatabaseConnection()) {
          // use db
      }
      ```
      emits as:
      ```php
      $db = new \DatabaseConnection();
      try {
          // use $db
      } finally {
          if ($db instanceof \Tyhp\Contracts\IsDisposable) {
              $db->dispose();
          }
      }
      ```
    - Multiple resources emit flat try/finally with null-init and error collection:
      ```php
      $db = null;
      $cache = null;
      try {
          $db = new \DatabaseConnection();
          $cache = new \CacheConnection();
          // body
      } finally {
          $__disposeErrors = [];
          if ($cache instanceof \Tyhp\Contracts\IsDisposable) {
              try { $cache->dispose(); } catch (\Throwable $__e) { $__disposeErrors[] = $__e; }
          }
          if ($db instanceof \Tyhp\Contracts\IsDisposable) {
              try { $db->dispose(); } catch (\Throwable $__e) { $__disposeErrors[] = $__e; }
          }
          if (!empty($__disposeErrors)) {
              throw new \Tyhp\Exceptions\AggregateException($__disposeErrors, 'One or more errors during disposal');
          }
      }
      ```
    - Unassigned resources get synthetic variable names (`$__using_0`, `$__using_1`, etc.)
    - `using await (...)` emits `\Tyhp\Promise::_await($resource->disposeAsync())` in the finally block
    - Full grammar, AST, and binder design is defined in Story 04, Phase 10

### Acceptance Criteria

- `$__scope = \Tyhp\DisposableScope::create()` is generated for disposable scopes
- Resources are registered via `$__scope->using(expr)`
- `using()` in if-condition context scopes the `DisposableScope` to the if-block
- Bare `:=` assignments register with the enclosing scope's `DisposableScope`
- Nested scopes each have their own `$__scope` variable with unique suffixes
- Disposables are disposed in reverse order via `__destruct()`
- No try/finally is generated for disposable scopes unless the checker flags unresolvable circular references
- Generated variable names use `$__scope` with unique suffixes
- Closures that capture `$this` and are stored as properties emit `WeakReference`-based captures
- Disposable scopes flagged by the checker for unresolvable circular references use try/finally fallback
- `using` block syntax emits try/finally (NOT `DisposableScope`), with `dispose()` in the finally block
- `using` block with multiple resources emits flat try/finally with null-init and error collection
- Constructor throw on Nth resource still disposes resources 1..N-1
- Dispose errors are collected into `AggregateException` (not lost on first failure)
- Unassigned `using` resources get synthetic `$__using_N` variable names
- Code compiles without errors

### Dependencies

- **Requires:** Phase 1 (EmitContext disposable tracker, unique name generation), Story 02 binder (scope analysis), Story 04 TyhpLib (IsDisposable interface, DisposableScope class)
- **Provides for:** Phase 8 (async/await interacts with async disposables)

---

## Phase 8: Generics — Type Erasure and Runtime Type Tracking




### Phase Overview

Implement the `GenericTransformer` that handles Tyhp's generic type system during emission. Generic type annotations are erased from PHP output (PHP has no native generics). For classes that use generics, the emitter optionally adds runtime type tracking via the `GenericObject` trait from TyhpLib. Runtime type checks at generic boundaries are emitted as `Tyhp\Type::check()` calls.

### Deliverables

**New files to create:**

- `Tyhp/TyhpLang/Emitter/Transformers/GenericTransformer.cs` — Generic type erasure and runtime tracking

**Existing files to modify:**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Register `GenericTransformer`

### Implementation Details

**`GenericTransformer.cs`:**

1. **Generic type annotation erasure:**
   - Strip all generic type parameters from class, interface, trait, enum, function, and method declarations
   - `class MyObject<TType>` → `class MyObject`
   - `function do_stuff<TObj>(TObj $val): TObj` → `function do_stuff($val)`
   - Strip generic arguments from type references: `MyObject<int>` → `MyObject`
   - Strip generic constraints (`extends` clause on type parameters) — these are compile-time only

2. **Type hint replacement:**
   - Where generic type parameters appear as type hints, replace with the most specific PHP type available:
     - If the generic parameter has a constraint (`TType extends int|float|string`), use the constraint as the PHP type hint (union types are valid in PHP 8+)
     - If no constraint, replace with `mixed` or omit the type hint
   - Handle nullable generic types: `?TType` → `mixed` (or constrained type)
   - Handle generic return types: `TType` return → `mixed` (or constrained type)

3. **Generic class runtime tracking via `GenericObject` trait:**
   For generic classes that need runtime type information, the emitter adds runtime type tracking using the `GenericObject` trait from `tyhp/core`. This is NOT added to all generic classes — only to those the checker has flagged as requiring runtime generic tracking (Story 08, Option A).

**When `GenericObject` is required:** A generic class requires runtime generic tracking when ANY of these conditions are met:
- `instanceof T` or `is T` type checks using a generic type parameter
- `new T()` construction using a generic type parameter
- `typeof(T)` expressions referencing a generic type parameter
- Passing generic type arguments to constructors of other generic classes
- Any runtime type operation that cannot be statically erased
- Generic-typed properties requiring runtime type enforcement

When set, the emitter adds the `\Tyhp\Concerns\GenericObject` trait and emits `tyhpGenericObjectInit()` calls in the constructor.

**When `GenericObject` is omitted:** Generic classes that only use type parameters in compile-time type annotations (which are erased) do not need runtime tracking. The `GenericObject` trait, hidden constructor parameters, and `tyhpGenericObjectSetPropertyType()` calls are all skipped for these classes.

The emitter reads the `RequiresRuntimeGenericTracking` flag from the class symbol's `BoundSymbol` to decide whether to apply the `GenericObject` pattern (reference: `runtime/packages/core/src/PropertyAccessor.php`):

   **a. Add the trait:**
   - Add `use \Tyhp\Concerns\GenericObject;` to the class body

   **b. Add hidden constructor parameters:**
   - For each generic type parameter `T`, add a nullable parameter `?Type $__generic_T = null` at the end of the constructor signature
   - The parameter accepts a plain `\Tyhp\Type` (NOT `NamedType`) — the constructor wraps it internally
   - The parameter is nullable with default `null` so the class can be instantiated without generics if needed
   - Example: `class Foo<TValue, TKey>` → constructor gets `?Type $__generic_TValue = null, ?Type $__generic_TKey = null`

   **c. Initialize generic types in constructor body:**
   - Wrap each provided generic type into a `NamedType` and call `tyhpGenericObjectInit()`:
     ```php
     if ($__generic_TValue !== null) {
         $tValue = new NamedType('TValue', $__generic_TValue);
         $this->tyhpGenericObjectInit($tValue);
     }
     ```
   - For multiple generic parameters, pass all `NamedType` instances to `tyhpGenericObjectInit()`:
     ```php
     if ($__generic_TValue !== null) {
         $tValue = new NamedType('TValue', $__generic_TValue);
         $tKey = new NamedType('TKey', $__generic_TKey);
         $this->tyhpGenericObjectInit($tValue, $tKey);
     }
     ```

   **d. Register property types:**
   - For ALL properties that have generic types (even those with fixed generic arguments like `\Closure<bool>`), emit `tyhpGenericObjectSetPropertyType()` calls inside the constructor's init block:
     ```php
     $this->tyhpGenericObjectSetPropertyType('get', Type::nullable(Type::generic(\Closure::class, $tValue)));
     $this->tyhpGenericObjectSetPropertyType('isset', Type::nullable(Type::generic(\Closure::class, new NamedType('TReturn', Type::bool()))));
     ```
   - This tracks the full generic type of each property for runtime reflection and type checking

   **e. Compile `typeof(T)` for generic type parameters:**
   - When `typeof()` references a generic type parameter inside a class that uses `GenericObject`, emit:
     ```php
     $this->tyhpGenericObjectGetGenericType('T')?->getUnderlyingType() ?? Type::mixed()
     ```
   - The nullsafe operator with `?? Type::mixed()` ensures `typeof()` NEVER returns null — it always resolves to a concrete `Type`, falling back to `mixed` when the generic type wasn't provided

   **f. Emit `new T(...)` for generic type parameters:**
   - When `new T(...)` is used where `T` is a generic type parameter, the containing class has the `\Tyhp\Concerns\GenericObject` trait (since `RequiresRuntimeGenericTracking` is set). The emitter resolves `T` at runtime via the `GenericObject` trait:
     ```php
     $__T = $this->tyhpGenericObjectGetGenericType('T')->getType();
     new $__T(...$args)
     ```
   - The `tyhpGenericObjectGetGenericType()` method returns a `NamedType` whose `getType()` provides the concrete class name string, which PHP's `new` operator accepts as a variable.

   **g. Emit call-site generic arguments:**
   - When instantiating a generic class with concrete type arguments, emit the concrete types as named arguments using the `__generic_` prefix:
     ```php
     // Tyhp: new PropertyAccessor<string>(get: fn() => $this->name)
     // PHP:
     new PropertyAccessor(
         get: fn() => $this->name,
         __generic_TValue: Type::string(),
     );
     ```
   - The caller provides a plain `Type` — the constructor wraps it into a `NamedType`

4. **Runtime type checks at generic boundaries:**
   - At method parameter boundaries where a generic type is used, optionally emit `\Tyhp\Type::check($value, $expectedType)`
   - This is controlled by configuration — strict runtime generic checking may be too expensive for production
   - `// PLACEHOLDER_STORY_10: Configuration flag for runtime generic checks (build.runtimeGenericChecks)`

5. **Generic type inference:**
   - The checker resolves generic types at compile time
   - The emitter uses the resolved concrete types from the checker when available
   - When calling a generic function: `do_stuff<int>(5)` → `do_stuff(5)` (generic argument erased, but if the function is a constructor of a generic class, use the `__generic_` parameter pattern above)

6. **Generic extends/implements:**
   - `class MyNewObject<TType> extends BaseObject<int, TType>` → `class MyNewObject extends BaseObject`
   - Generic arguments on extends/implements are erased

7. **Key rules for the GenericObject emission pattern:**
   - The hidden `$__generic_*` constructor parameters accept `?Type` (nullable), NOT `NamedType` — the constructor wraps them into `NamedType` internally
   - `typeof(T)` always resolves to a non-null `Type` by falling back to `Type::mixed()`
   - ALL properties with generic types get registered via `tyhpGenericObjectSetPropertyType()`, including those with fixed generic arguments (e.g., a property typed `\Closure<bool>`)
   - Generic type parameters on methods (e.g., `function foo<T>()`) follow a similar pattern but with method-level tracking

### Acceptance Criteria

- All generic type parameters are stripped from class, interface, trait, function, and method declarations
- Generic arguments on type references (extends, implements, method calls) are erased
- Type hints for generic parameters are replaced with their constraint type or `mixed`
- Generic classes that require runtime type tracking (flagged by the checker as `RequiresRuntimeGenericTracking`) include the `GenericObject` trait with hidden `$__generic_*` constructor parameters
- For classes with `RequiresRuntimeGenericTracking`: constructor body wraps generic types into `NamedType` and calls `tyhpGenericObjectInit()`
- For classes with `RequiresRuntimeGenericTracking`: all properties with generic types are registered via `tyhpGenericObjectSetPropertyType()`
- `typeof(T)` inside a generic class emits as `$this->tyhpGenericObjectGetGenericType('T')?->getUnderlyingType() ?? Type::mixed()`
- Call sites emit concrete types as named `__generic_*` arguments (e.g., `__generic_TValue: Type::string()`)
- Runtime type checks are emitted at generic boundaries when configured
- Code compiles without errors

### Dependencies

- **Requires:** Phase 1 (EmitContext, EmitHelpers for type hint emission), Phase 3 (TypeAliasTransformer — aliases resolved before generic erasure), Story 02 binder (generic parameter symbol resolution), Story 04 TyhpLib (`GenericObject` trait, `Type` class)
- **Provides for:** Generic erasure is a prerequisite for clean PHP output in all other transformers

---

## Phase 9: Async/Await — Promise and Fiber Integration




### Phase Overview

Implement the `AsyncAwaitTransformer` that transforms Tyhp's async/await syntax into PHP code using the `Promise` runtime library (implemented in the `tyhp/async` Composer package (`runtime/packages/async/src/Promise.php`)). Async functions become functions that return `Promise<T>`, `await` expressions become `_await()` calls, and the process loop is auto-started at the lowest async call boundary.

### Deliverables

**New files to create:**

- `Tyhp/TyhpLang/Emitter/Transformers/AsyncAwaitTransformer.cs` — Async/await → Promise/Fiber code

**Existing files to modify:**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Register `AsyncAwaitTransformer`

### Implementation Details

**`AsyncAwaitTransformer.cs`:**

1. **Async function declaration:**
   - `async function myFunc(): int` → emits as a function returning `\Tyhp\Promise`
   - The function body is wrapped in `\Tyhp\Promise::_async()` which takes a callable and returns a `Promise`. Inside the callable, `await` expressions become `\Tyhp\Promise::_await()` calls.
   - Return type `int` becomes `\Tyhp\Promise` in PHP output
   - `async function myFunc(): void` → returns `\Tyhp\Promise` (resolving to `null`)

   **Concrete emission pattern:**
   ```tyhp
   // Tyhp input:
   async function fetchUser(int $id): User {
       $data = await $api->fetchAsync($id);
       return new User($data);
   }
   ```
   ```php
   // Emitted PHP:
   function fetchUser(int $id): \Tyhp\Promise {
       return \Tyhp\Promise::_async(function() use ($id): \App\Models\User {
           $data = \Tyhp\Promise::_await($api->fetchAsync($id));
           return new \App\Models\User($data);
       });
   }
   ```

   Key details:
   - The outer function return type changes to `\Tyhp\Promise`
   - The body is wrapped in `\Tyhp\Promise::_async(function() use (...): <originalReturnType> { ... })`
   - The inner closure captures outer scope variables via `use` (for standalone functions)
   - For async methods, `$this` is automatically available in the closure (no explicit `use` needed)
   - `await expr` becomes `\Tyhp\Promise::_await(expr)`

2. **Await expression:**
   - `await $promise` → `_await($promise)`
   - This is a direct function call transformation
   - `_await()` is provided by the `tyhp/async` package (`\Tyhp\Promise::_await()`) and uses PHP Fibers internally

3. **Async method declarations:**
   - `public async function fetchData(): string` → `public function fetchData(): Promise`
   - Same wrapping pattern as standalone functions
   - Handle all method modifiers (public, protected, private, static)

4. **Async closures / arrow functions:**
   - `async fn() => await $something` → closure returning Promise
   - `async function() { ... }` → anonymous function returning Promise

5. **Process loop auto-start:**
   - The emitter inserts `\Tyhp\Promise::run()` at the file-level entry point. The detection criterion is: a top-level statement (not inside a function/method declaration) that contains an `await` expression. For library code (project type `Library`), no auto-start is added — the consuming application is responsible for running the event loop. For application code, the emitter wraps the entry point file's top-level statements in `\Tyhp\Promise::run(function() { ... })` when async usage is detected at the top level.

6. **Async package dependency (handled by build):**
   - The emitter does NOT generate `require_once` for TyhpLib — it relies on Composer's `vendor/autoload.php` which the output project includes
   - The build action (Story 10) adds `tyhp/async` as a Composer dependency to the output project

7. **Async disposable interaction:**
   - When `await` is used with disposables, coordinate with `DisposableTransformer` (Phase 7)
   - `// Async dispose coordination — resolved in Phase 7 (DisposableTransformer) of this story`

8. **Async foreach (`foreach (await $expr as $item)`):**
   - The checker has already validated that `$expr` is `AsyncIterable<T>`, `Promise<Iterable<T>>`, or `Promise<AsyncIterable<T>>`. The emitter handles each case:

   **Case 1: `$expr` is `AsyncIterable<T>` (true async iteration):**
   ```php
   // Tyhp input:
   foreach (await $queue->messagesAsync() as $message) {
       process($message);
   }

   // Emitted PHP:
   $__asyncIter_1 = $queue->messagesAsync()->getAsyncIterator();
   while (\Tyhp\Promise::_await($__asyncIter_1->next())) {
       $message = \Tyhp\Promise::_await($__asyncIter_1->current());
       process($message);
   }
   ```

   **Case 2: `$expr` is `Promise<Iterable<T>>` (resolve-then-iterate):**
   ```php
   // Tyhp input:
   foreach (await $api->fetchAllAsync() as $item) {
       process($item);
   }

   // Emitted PHP:
   foreach (\Tyhp\Promise::_await($api->fetchAllAsync()) as $item) {
       process($item);
   }
   ```

   **Case 3: `$expr` is `Promise<AsyncIterable<T>>` (resolve then async-iterate):**
   ```php
   // Emitted PHP:
   $__asyncIter_1 = \Tyhp\Promise::_await($connectToStream())->getAsyncIterator();
   while (\Tyhp\Promise::_await($__asyncIter_1->next())) {
       $item = \Tyhp\Promise::_await($__asyncIter_1->current());
       // body
   }
   ```

   **Key-value async iteration (`foreach (await $expr as $key => $value)`):**
   - If the `AsyncIterator` also implements `AsyncKeyValueIterator`, use `currentKey()` and `currentValue()` instead of `current()`:
   ```php
   $__asyncIter_1 = $expr->getAsyncIterator();
   while (\Tyhp\Promise::_await($__asyncIter_1->next())) {
       $key = \Tyhp\Promise::_await($__asyncIter_1->currentKey());
       $value = \Tyhp\Promise::_await($__asyncIter_1->currentValue());
       // body
   }
   ```

   **Implementation notes:**
   - Generate unique temp variable names (`$__asyncIter_1`, `$__asyncIter_2`, etc.) to avoid collisions with user code, especially in nested async foreach loops
   - The async foreach must be inside an async function (the checker enforces this, but the emitter should also verify)
   - `break` and `continue` inside the async foreach body work naturally because the desugared form is a `while` loop

### Acceptance Criteria

- `async function myFunc(): int` emits as a function returning `Promise`
- `await $promise` emits as `_await($promise)`
- Async methods emit with correct modifiers and `Promise` return type
- Async closures emit as closures returning `Promise`
- The `tyhp/async` Composer package dependency is properly referenced in output
- The original return type annotation is stripped and replaced with `Promise`
- `foreach (await $asyncIterable as $item)` on `AsyncIterable<T>` emits as a while-loop with `_await()` calls on `getAsyncIterator()->next()` and `current()`
- `foreach (await $promise as $item)` on `Promise<Iterable<T>>` emits as `foreach (_await($promise) as $item)`
- `foreach (await $expr as $key => $value)` on `AsyncKeyValueIterator` emits with `currentKey()` and `currentValue()` calls
- Nested async foreach loops use unique temp variable names
- Code compiles without errors

### Dependencies

- **Requires:** Phase 1 (EmitContext, EmitHelpers), Story 04 (`tyhp/async` Composer package), Story 02 binder (function symbol async flag)
- **Provides for:** Async support enables testing of the full async pipeline

---

## Phase 10: Compile-Time Constructs and Final Integration




### Phase Overview

Implement the `CompileTimeTransformer` for `nameof()`, `typeof()`, and `default()` constructs (constant folding at compile time). Then integrate all transformers, verify the complete transformation pipeline works end-to-end, and add emitter-level `MessageCode` values for any emitter-specific diagnostics. This phase also handles any remaining edge cases.

### Deliverables

**New files to create:**

- `Tyhp/TyhpLang/Emitter/Transformers/CompileTimeTransformer.cs` — nameof/typeof/default → constant values
- `Tyhp/TyhpLang/Emitter/Transformers/ImportTransformer.cs` — Tyhp imports → PHP use statements (late-pass, order 14)

**Existing files to modify:**

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Final integration of all transformers, ordering verification
- `Tyhp/Domain/Exceptions/MessageCode.cs` — Add emitter-specific message codes (5000 range)

### Implementation Details

**`CompileTimeTransformer.cs`:**

1. **`nameof()` construct:**
   - `nameof($variable)` → string literal `'variable'`
   - `nameof(MyClass)` → string literal `'MyClass'`
   - `nameof($obj->method)` → string literal `'method'`
   - `nameof($obj->property)` → string literal `'property'`
   - `nameof(MyClass::CONSTANT)` → string literal `'CONSTANT'`
   - Resolve the name from the AST node — do not evaluate at runtime
   - Handle namespaced names: `nameof(\App\Models\User)` → `'User'` (short name only)

2. **`typeof()` construct:**
   - `typeof(MyClass)` → `\Tyhp\Type::of('MyClass')` (class/interface name → `Type::of()` call)
   - `typeof(int)` → `\Tyhp\Type::int()` (built-in type → `Type` factory method)
   - **Inside a generic class** (one that uses `GenericObject`), `typeof(T)` where `T` is a generic type parameter compiles to:
     ```php
     $this->tyhpGenericObjectGetGenericType('T')?->getUnderlyingType() ?? Type::mixed()
     ```
     This always resolves to a non-null `Type`, falling back to `mixed` when the generic type wasn't provided at instantiation

3. **`default()` construct:**
   - `default(int)` → `0`
   - `default(float)` → `0.0`
   - `default(string)` → `''`
   - `default(bool)` → `false`
   - `default(array)` → `[]`
   - `default(?T)` → `null` for any nullable type
   - `default(object)` → `null`
   - For struct types: emit the default array construction (coordinate with `StructTransformer`)
   - For class types: `null` (classes are nullable by default in PHP)

4. **`variable_exists()` construct:**
   - When `variable_exists()` reaches the emitter (i.e., the checker did not erase it to a constant `true`/`false`), it must perform a real runtime check for variable existence in the current scope
   - `variable_exists($var)` → `\array_key_exists('var', \get_defined_vars())`
   - This returns `true` if the variable is defined in the current scope regardless of its value (including `null`), unlike `isset()` which returns `false` for `null` values
   - The variable name is extracted as a string literal from the AST at compile time
   - This handles edge cases where variables are introduced via unsafe operations like `eval()` (if the user has manually imported `eval` via tyhpdef) and the checker cannot statically determine variable existence

**Emitter MessageCode values (Story 11 owns `5012–5019`):**

`EmitterUnknownError = 5001` already exists in `MessageCode.cs` and Story 09 owns the emitter-core block `5002–5011`; Story 11 must **NOT** reuse `5001–5008`. Story 11's NEW emitter codes are allocated contiguously starting at `5012`:

- `EmitterUnknownError = 5001` — **already defined (base/Story 09); referenced here, NOT redefined**
- `EmitterUnsupportedConstruct = 5012` — a Tyhp construct that cannot be emitted to PHP
- `EmitterNameConflict = 5013` — generated method name conflicts with existing method
- `EmitterMissingRuntime = 5014` — TyhpLib runtime required but not configured
- `EmitterStructBackingError = 5015` — configured struct backing class not found
- `EmitterDisposableError = 5016` — disposable variable type does not implement IsDisposable

> **Checker codes 4139–4141 are consumed, not defined here.** `CheckerCloneWithReadonlyRequiresConfig = 4139`, `CheckerWithReadonlyFinalClass = 4140`, and `CheckerWithReadonlyInPlace = 4141` are defined by **Story 08** (clone/with checker block). Story 11 references them when describing which `with` forms the checker rejects; it must NOT redefine them in the emitter `MessageCode` block.

**`ImportTransformer.cs`:**

ImportTransformer is NOT part of the early erasure pass (Phase 3). It is registered last (order position 14) and does its work in a **final post-emit pass** (`PostProcess()`), NOT `PreProcess()`. This is required because `EmitContext.AdditionalImports` is populated *during* the emit-time AST walk by transformers such as `ExtensionMethodTransformer`, `StructTransformer`, and `AsyncAwaitTransformer`; a `PreProcess()` pass (which runs before the walk) could not see those additions. After the walk completes, `ImportTransformer.PostProcess()` collects all `AdditionalImports` entries and generates the final consolidated `use` statement block for each output file's header.

- Convert Tyhp `import` / `using` statements to PHP `use` statements
- Handle any Tyhp-specific import syntax differences
- Remove imports for types that are erased in output (type aliases, generic type parameters, struct types)
- Remove imports for extension classes if the extension methods have been rewritten to explicit static calls with fully-qualified names
- Collect all entries from `EmitContext.AdditionalImports` (populated by `ExtensionMethodTransformer`, `StructTransformer`, `AsyncAwaitTransformer`, and other transformers that generate code requiring additional imports)
- Sort `use` statements alphabetically (PHP convention)
- Group `use` statements by type: classes, functions, constants

**Final Integration in `TyhpEmitter.cs`:**
- Verify all transformers are registered in the correct order
- Verify the pre-emit → emit-time transformation pipeline
- Ensure transformers that modify types (TypeAliasTransformer) run before transformers that read types (GenericTransformer, OperatorOverloadTransformer)
- The `ImportTransformer` is registered last (order 14) and runs its collection logic in `PostProcess()` (a final post-emit pass), so it executes after all other transformers have added/removed imports during the emit walk. Use statements are written at the top of the PHP output file header regardless of emit-time ordering.
- Add comprehensive diagnostic reporting for unsupported or partially-supported constructs
- Search for and document all `PLACEHOLDER_STORY_N` comments left by previous phases

### Acceptance Criteria

- `nameof($variable)` emits as a string literal with the variable name
- `nameof(MyClass)` emits as `'MyClass'`
- `nameof($obj->method)` emits as `'method'`
- `default(int)` emits as `0`, `default(string)` as `''`, `default(bool)` as `false`, etc.
- `default(?T)` for any nullable type emits as `null`
- Emitter MessageCode values are added in Story 11's allocated range `5012–5019` (NOT `5001–5008`, which are owned by the base/Story 09)
- All 14 transformers are registered and execute in the correct order
- Tyhp import statements emit as valid PHP `use` statements
- Imports for erased types (type aliases, generics parameters) are removed from output
- `ImportTransformer` collects all entries from `EmitContext.AdditionalImports` and generates corresponding `use` statements
- The complete transformation pipeline produces valid PHP output for all example files in `Examples/`
- Running the `TyhpEmitter` on a file containing multiple Tyhp features produces correct PHP with all features transformed
- No placeholder comments from within this story's phases remain (all `PLACEHOLDER_PHASE_N` are resolved; only `PLACEHOLDER_STORY_N` for other stories remain)
- All files compile without errors
- `ReadLints` shows no new linter errors introduced by this story

### Dependencies

- **Requires:** All previous phases (1-9), Story 02 binder, Story 08 checker, Story 04 TyhpLib
- **Provides for:** Story 17 (sourcemaps can track all transformations), Story 07 (end-to-end tests can validate emitter output). **Ordering note:** in the canonical story order, Story 10 (Build Action) runs *before* Story 11. Story 10 therefore ships with placeholders that Story 11 fills in — specifically the `EmitContext.RequiredPackages` / `RequirePackage()` scaffold (Story 10 Phase 6 defines the empty stub; Story 11 Phase 1 populates it with transformer-driven logic) and the `// PLACEHOLDER_STORY_11: Advanced emitter features` markers in `BuildAction`. Story 11 does **not** block Story 10's initial completion; once Story 11 lands, the build action can compile the full Tyhp feature set.

---

## Appendix: Feature Emission Modules (replaces "Transformer Registration Order")

> Per the ADR at the top of this doc, there is no `IEmitTransformer` dispatch and no `Transformers/` directory. The ordering constraints the original table expressed are instead enforced by the `TyhpEmitter.Emit()` phase sequence and the `EmitNode` walk order. The table below maps each originally-planned transformer to the inline module that owns its logic, and notes where in the pipeline it runs.

| Orig. order | Feature | Owning inline module | Pipeline stage | Rationale / dependency |
|---|---|---|---|---|
| 1 | Type aliases | `AliasConverter` + `TypeSpellingHelper` (via `EmitContext.TypeAliasMap`) | `ConvertAliasesForAll` (pre-emit) | Must run first so all types are concrete |
| 2 | Trait requirements | `TyhpEmitter.EmitObjectDeclaration` | `BuildEmitTrees` (emit walk) | Simple erasure of `extends`/`implements` on traits |
| 3 | Function overloads | `TyhpEmitter.EmitFunctionDeclaration` / `EmitMethodDeclaration` (+ binder/visitor skip) | `BuildEmitTrees` | Erase overload-signature decls, keep implementation |
| 4 | Type guards | `TypeSpellingHelper` (`TyhpReturnTypeGuardAst => "bool"`) | `BuildEmitTrees` (type spelling) | Replace `$param is Type` return type with `bool` |
| 5 | Structs | `StructEmissionHelper` (+ decl erasure in `EmitNode`) | `BuildEmitTrees` | Needed before `with` keyword (struct `with` → `array_replace`) |
| 6 | Generics | `TypeSpellingHelper` (erasure) + `TyhpEmitter.Generics` / `Expressions` (`typeof(T)`, `new T`, call-site `__generic_*`) | `BuildEmitTrees` | After aliases resolved. **DONE** — GenericObject runtime tracking via checker `RequiresRuntimeGenericTracking` side dictionary → EmitContext; trait + hidden ctor params + init/setPropertyType; `Type::check` when `build.runtimeGenericChecks` |
| 7 | Extension methods | `AliasConverter.TryRewriteExtensionMethodCall` | `ConvertAliasesForAll` / emit walk | After generics erased. **DONE** — single, chained, nullable, and scalar receivers rewrite to nested `\Extension::method($recv, …)` static calls. |
| 8 | Operator overloads | `TyhpEmitter.OperatorOverloads.cs` (decls) + `AliasConverter` (call sites) + `OperatorOverloadResolver` | `BuildEmitTrees` | After generics erased. **DONE (REDESIGNED)** — collapsed **static** methods with union operands + internal `instanceof`/`is_*` dispatch; exact reserved names (`__add`, `__isLessThan`, `__compare`, `__asNumeric`, `__not`, `__from`, instance `__to{T}`); left-first static call-site rewriting; `true`/`false`/`null` removed; reserved-name + distinguishability + self→self-convert checker rules; dedicated emitter + checker tests |
| 9 | `with` keyword | `StructEmissionHelper.TryRewriteStructWith` (struct only) | `BuildEmitTrees` | After struct helper. **Object `with` forms not yet implemented** |
| 10 | Short function syntax | Visitor desugar (`VisitTyhpShortFunctionOverloadDeclarationStatement` / `CreateTyhpClassGenericMethodShort` → `return expr;` body) + `TyhpEmitter.EmitFunctionDeclaration` / `EmitMethodDeclaration` (always emit `function`) | `BuildEmitTrees` (AST already desugared at visit) | Independent. **DONE** — anonymous `fn($x) => …` left as PHP arrows via `BuildInlineFunctionExpression` |
| 11 | Disposables | `TyhpEmitter.Expressions` / `Statements` / `Disposables` (`$__scope`, `using` block → try/finally, WeakReference capture, try/finally circular fallback, `using await`) | `BuildEmitTrees` | After struct/with. **DONE** — WeakReference capture + try/finally circular fallback via checker side dictionaries; `using await` → `_await(disposeAsync())` with sync fallback |
| 12 | Async/await | `TyhpEmitter.Expressions` / `Declarations` / `Async` (`Promise::_async` / `_await` / async-foreach / `Promise::run`) | `BuildEmitTrees` | After disposable |
| 13 | Compile-time constructs | `TyhpEmitter.Expressions` (`nameof`/`default`/`typeof`/`variable_exists`) | `BuildEmitTrees` | Last for cleanup. **DONE** — `variable_exists($v)` → `\array_key_exists('v', \get_defined_vars())` |
| 14 | Imports / `use` | `PHPOutputFile.PruneFileImports` (prune + consolidate + erase-drop) | `PruneImportsForAll` (post-walk) | **Done (Story 11 Phase 2): `PruneFileImports` now folds `EmitContext.AdditionalImports` into each file header, sorts/groups `use` statements (classes → functions → constants), and drops imports for erased types (type aliases, generic type params, structs) and extension-class imports rewritten to fully-qualified static calls.** |

---

## Appendix: Cross-Reference to Example Files

Each transformer should be validated against the corresponding example files in `Examples/`:

| Feature | Tyhp Source | Expected PHP Output |
|---------|------------|-------------------|
| Structs | `Examples/Structs.tyhp` | `Examples/Structs.php` |
| Generics | `Examples/Generics.tyhp` | `Examples/Generics.php` |
| Extension Methods | `Examples/ScalarTypesAsObjects.tyhp` | — |
| Operator Overloads | `Examples/OperatorOverloads.tyhp` | `Examples/OperatorOverloads.php` |
| Type Aliases | `Examples/TypeAliases.tyhp` | `Examples/TypeAliases.php` |
| With Keyword | `Examples/WithKeyword.tyhp` | `Examples/WithKeyword.php` |
| Async/Await | `Examples/AsyncAwait.tyhp` | — |
| Short Functions | `Examples/ShortFunctionSyntax.tyhp` | `Examples/ShortFunctionSyntax.php` |
| Function Overloads | `Examples/FunctionAndMethodOverrides.tyhp` | `Examples/FunctionAndMethodOverrides.php` |
| Type Guards | `Examples/TypeGuards.tyhp` | `Examples/TypeGuards.php` |
| Property Accessors | `Examples/PropertyAccessors.tyhp` | `Examples/PropertyAccessors.php` |
| Disposables | See `Tyhp/TyhpLang/Emitter/readme.md` | See readme emitted examples |

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the emitter feature expansion works end-to-end. Steps can be skipped, reordered, or adapted as needed. All commands assume you are in the project root directory and the project builds successfully. For each step, you write a `.tyhp` file, compile it, and inspect the generated `.php` output.

### Step 1: Verify Struct Emission (Structs → Arrays)

Create `test_struct.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

struct Point {
    int $x = 0;
    int $y = 0;
}

struct NamedItem {
    string 'Display Name' as $displayName = "";
    int $id = 0;
}

function main(): void {
    $p = new Point();
    $p->x = 10;
    $val = $p->y;

    $item = new NamedItem();
    $item->displayName = "Hello";
}
```

Compile and inspect the PHP output:

```bash
dotnet run -- build
```

Expected PHP output patterns:
- No `struct` keyword in the PHP output (declaration is erased)
- `new Point()` emits as an associative array literal: `['x' => 0, 'y' => 0]`
- `$p->x = 10` emits as `$p['x'] = 10`
- `$p->y` emits as `$p['y']`
- `$item->displayName` emits as `$item['Display Name']` (alias key)

### Step 2: Verify Type Alias Erasure

Create `test_type_alias.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

type UserId = int;
type UserOrNull = ?User;

class User {
    public type NameType = string;

    public NameType $name;

    public function getId(): UserId {
        return 42;
    }
}
```

Expected PHP output:
- No `type UserId = int;` line in the PHP output (declaration erased)
- The `NameType` class-level type alias is erased
- Return type `UserId` is replaced with `int`
- Property type `NameType` is replaced with `string`

### Step 3: Verify Generic Type Erasure

Create `test_generics.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

class Container<TValue> {
    private ?TValue $item = null;

    public function set(TValue $value): void {
        $this->item = $value;
    }

    public function get(): ?TValue {
        return $this->item;
    }
}

function identity<T>(T $val): T {
    return $val;
}
```

Expected PHP output:
- `class Container<TValue>` emits as `class Container` (generic param stripped)
- `TValue` type hints become `mixed` (or the constraint type if constrained)
- `function identity<T>` emits as `function identity` (generic param stripped)
- If the class does NOT require runtime generic tracking, no `use GenericObject;` trait is added
- If it DOES require tracking (e.g., uses `typeof(T)`), verify the `GenericObject` trait, hidden `$__generic_TValue` constructor param, and `tyhpGenericObjectInit()` call are present

### Step 4: Verify Extension Method Call Rewriting

Create `test_extension.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

extension StringUtils {
    public static function toCamelCase(extends string $str): string {
        return \lcfirst(\str_replace(' ', '', \ucwords($str)));
    }
}

function demo(): void {
    $result = "hello world"->toCamelCase();
}
```

Expected PHP output:
- `StringUtils` class is emitted as a standard PHP class with a static method `toCamelCase`
- The `extends` keyword is removed from the first parameter
- `"hello world"->toCamelCase()` emits as `StringUtils::toCamelCase("hello world")`
- A `use` statement for `StringUtils` is present (or fully-qualified name is used)

### Step 5: Verify Operator Overload Emission

Create `test_operator.tyhp` (reference `Examples/OperatorOverloads.tyhp` for syntax):

```tyhp
<?tyhp

namespace TestEmitter;

class Money {
    public function __construct(
        public readonly int $amount,
        public readonly string $currency
    ) {}

    operator +(self $left, self $right): self {
        return new self($left->amount + $right->amount, $left->currency);
    }
}

function demo(): void {
    $a = new Money(100, "USD");
    $b = new Money(50, "USD");
    $c = $a + $b;
}
```

Expected PHP output (REDESIGNED — static collapsed methods):
- The operator overload emits as a single static method `public static function __add(self $l, self $r)`
- `$a + $b` emits as `\Money::__add($a, $b)` (left-operand-first resolution)
- Compound assignment `$a += $b` emits as `$a = \Money::__add($a, $b)`

### Step 6: Verify `with` Keyword Emission

Create `test_with.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

class Config {
    public string $name = "";
    public int $value = 0;
}

function demo(): void {
    $cfg = new Config() with [name => "test", value => 42];
    $clone = clone $cfg with [name => "updated"];
}
```

Expected PHP output (PHP < 8.5, non-readonly):
- `new Config() with [...]` emits as separate statements: `$cfg = new Config(); $cfg->name = "test"; $cfg->value = 42;` (statement context optimization)
- `clone $cfg with [...]` emits as: `$clone = clone $cfg; $clone->name = "updated";` (statement context)

Expected PHP output (PHP 8.5+):
- `clone $cfg with [...]` emits as `$clone = clone($cfg, ['name' => 'updated']);` using the native syntax

### Step 7: Verify Short Function Syntax

Create `test_short_fn.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

fn add(int $a, int $b): int => $a + $b;

class Calculator {
    public fn multiply(int $a, int $b): int => $a * $b;
}
```

Expected PHP output:
- `fn add(...)` emits as `function add(int $a, int $b): int { return $a + $b; }`
- `public fn multiply(...)` emits as `public function multiply(int $a, int $b): int { return $a * $b; }`
- Anonymous PHP arrow functions (`fn($x) => $x + 1`) should NOT be transformed

### Step 8: Verify Disposable Emission

Create `test_disposable.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

use \Tyhp\Contracts\IsDisposable;

class DbConnection implements IsDisposable {
    public function dispose(): void {
        // close connection
    }
}

function demo(): void {
    $db := new DbConnection();
    // use $db...
}
```

Expected PHP output:
- A `$__scope = \Tyhp\DisposableScope::create();` line at the start of the scope
- `$db := new DbConnection()` emits as `$db = $__scope->using(new DbConnection());`
- No try/finally wrapping (unless the checker flagged circular references)

### Step 9: Verify Async/Await Emission

Create `test_async.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

async function fetchData(int $id): string {
    $result = await $api->getAsync($id);
    return $result;
}
```

Expected PHP output:
- `async function fetchData(int $id): string` emits as `function fetchData(int $id): \Tyhp\Promise`
- The body is wrapped in `\Tyhp\Promise::_async(function() use ($id): string { ... })`
- `await $api->getAsync($id)` emits as `\Tyhp\Promise::_await($api->getAsync($id))`

### Step 10: Verify Compile-Time Constructs

Create `test_compiletime.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

class Demo {
    public string $name = "";

    public function test(): void {
        $n = nameof($this->name);     // should be 'name'
        $d = default(int);            // should be 0
        $s = default(string);         // should be ''
        $b = default(bool);           // should be false
    }
}
```

Expected PHP output:
- `nameof($this->name)` emits as the string literal `'name'`
- `default(int)` emits as `0`
- `default(string)` emits as `''`
- `default(bool)` emits as `false`
- No runtime `nameof()` or `default()` function calls remain in the PHP

### Step 11: Verify Type Guard Return Type

Create `test_type_guard.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

class Animal {}
class Dog extends Animal {}

function isDog(Animal $a): $a is Dog {
    return $a instanceof Dog;
}
```

Expected PHP output:
- `$a is Dog` return type emits as `bool`
- The function body remains unchanged

### Step 12: Verify Import/Use Conversion

Create `test_imports.tyhp`:

```tyhp
<?tyhp

namespace TestEmitter;

use App\Models\User;
use App\Services\{AuthService, Logger};

function demo(): void {
    $u = new User();
}
```

Expected PHP output:
- `use App\Models\User;` appears in the PHP output
- `use App\Services\{AuthService, Logger};` is expanded or preserved
- Imports for erased types (type aliases, generic type parameters) are removed
- `use` statements are sorted alphabetically and grouped by type (classes, functions, constants)

### Step 13: End-to-End Multi-Feature Test

Create `test_combined.tyhp` combining several features:

```tyhp
<?tyhp

namespace TestCombined;

type Id = int;

struct Coordinate {
    float $lat = 0.0;
    float $lng = 0.0;
}

class Location<T> {
    public T $data;
    public Coordinate $position;

    fn getPosition(): Coordinate => $this->position;

    public function update(T $newData): self {
        return clone $this with [data => $newData];
    }
}

fn makeCoord(float $lat, float $lng): Coordinate =>
    new Coordinate() with [lat => $lat, lng => $lng];
```

Compile and verify that ALL transformations are applied correctly in the output:
- `type Id = int;` is erased
- `struct Coordinate` declaration is erased
- Struct usage emits as arrays
- Generic `<T>` is erased
- `fn` short syntax expands to full function
- `with` keyword emits as property assignments or `array_replace`
- `clone ... with` uses the appropriate strategy for the PHP target version
- The output is valid PHP (run `php -l` on it if PHP is available)

### Step 14: Validate PHP Syntax of All Output

If PHP is installed, validate all emitted output files:

```bash
find build/ -name "*.php" -exec php -l {} \;
```

All files should report `No syntax errors detected`.

### Step 15: Cleanup

Remove any test files created during verification:

```bash
rm -f test_struct.tyhp test_type_alias.tyhp test_generics.tyhp test_extension.tyhp
rm -f test_operator.tyhp test_with.tyhp test_short_fn.tyhp test_disposable.tyhp
rm -f test_async.tyhp test_compiletime.tyhp test_type_guard.tyhp test_imports.tyhp
rm -f test_combined.tyhp
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [x] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
      Suites under `tests/conformance/story11/`: structs, generics, operator-overloads, type-aliases, with, async, disposables, short-functions, type-guards, imports.
- [x] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
      Includes `CallSiteRewriteEmitterTests` (operator + extension call-site rewrite acceptance matrix) plus existing Story 11 emitter suites.
- [x] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [x] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
      Self-host infrastructure is green but vacuous — no runtime package self-compiles yet (`ExpectedToCompileAllowlist` empty); see `story_11_log.md` §12.
- [x] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
      (5012–5019 emitter codes landed in §1; no new codes in §12.)
