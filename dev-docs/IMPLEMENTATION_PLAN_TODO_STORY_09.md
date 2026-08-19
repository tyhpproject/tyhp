# Implementation Plan: Story 09 — Emitter (Basic PHP Output)

> **Roadmap position:** Story 09 — **Tier 0 — Spine**
> **Direct dependencies (new numbering):** 01, 02, 03, 08
> **Renumbered from:** legacy Story 4
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 09 of the Tyhp compiler TODO
> **Branch:** TBD
> **Generated:** 2026-02-16
> **Prerequisite:** Story 01 (Foundation: diagnostic system, `CompilationService`, `BuildAction` skeleton), Story 02 (Binder: symbols, scopes, name resolution, tyhpdef loading), Story 03 (Extension operator overloads, tyhpdef inline extensions), Story 08 (Checker: at least basic type checking and validation)
> **Status:** SUBSTANTIALLY COMPLETE (2026-07-31 audit) — `TyhpEmitter` PHP pass-through and `tests/conformance/story09/` fixtures landed. Phase checkboxes in this doc were never updated. Feature transforms belong to Story 11.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Phase 1: Emitter Architecture, EmitContext, and AST-to-Emit Dispatch Framework](#phase-1-emitter-architecture-emitcontext-and-ast-to-emit-dispatch-framework)
- [Phase 2: PHPOutputFile.FromAstTree() — AST Splitting into Output File Units](#phase-2-phpoutputfilefromasttree--ast-splitting-into-output-file-units)
- [Phase 3: TyhpEmitter Core — PHP Pass-Through (Declarations and Top-Level Constructs)](#phase-3-tyhpemitter-core--php-pass-through-declarations-and-top-level-constructs)
- [Phase 4: TyhpEmitter Core — PHP Pass-Through (Statements and Expressions)](#phase-4-tyhpemitter-core--php-pass-through-statements-and-expressions)
- [Phase 5: ConvertAliases() — Tyhp-to-PHP Transformations](#phase-5-convertaliases--tyhp-to-php-transformations)
- [Phase 6: Generate(), PruneFileImports(), and Merge()](#phase-6-generate-prunefileimports-and-merge)
- [Phase 7: Output File Writing and Pipeline Integration](#phase-7-output-file-writing-and-pipeline-integration)
- [Phase 8: End-to-End Validation and Emitter MessageCodes](#phase-8-end-to-end-validation-and-emitter-messagecodes)

---

## Architecture Overview

### What the Emitter Does

The emitter is the code generation phase of the Tyhp compilation pipeline. It transforms the bound, type-checked, and optionally optimized (Story 23) AST into valid PHP source code files. The emitter must:

1. **Split** the AST into individual output file units (one class per file for PSR-4 compliance, grouped functions, entry point files)
2. **Transform** Tyhp-specific constructs into their PHP equivalents (strip generics, rewrite extension method calls, convert structs to arrays, etc.)
3. **Emit** each output file unit as a formatted PHP source string using the `EmitItem` tree
4. **Write** the emitted PHP files to disk at the correct output paths

### Emitter Pipeline Position

```
Parser (DONE)
    │
    ▼
Story 01: Foundation (CompilationService, BuildAction skeleton)
    │
    ▼
Story 02: Binder (Symbols, Scopes, Name Resolution)
    │
    ▼
Story 06: TyhpSpec (Built-in types loaded)
    │
    ▼
Story 08: Checker (Type validation, diagnostics)
    │
    ▼
Story 23: Optimizer (extension inlining, constant folding, dead code elimination)
    │         NOTE: The emitter receives an optionally-optimized AST
    │         (the optimizer phase runs only when optimization is enabled).
    │         package.tyhp.json (Story 20) is generated BEFORE optimization.
    ▼
┌────────────────────────────────────────────────────────────────┐
│  STORY 09: Emitter (Basic PHP Output)  ◄── THIS PLAN           │
│                                                                │
│  Step 1: FromAstTree()   → Split AST into PHPOutputFile units  │
│  Step 2: ConvertAliases()→ Transform Tyhp → PHP constructs     │
│  Step 3: TyhpEmitter     → Walk AST, build EmitItem trees      │
│  Step 4: PruneFileImports→ Remove unused use statements        │
│  Step 5: Generate()      → Produce final PHP source strings    │
│  Step 6: Write to disk   → Create .php files at output paths   │
└────────────────────────────────────────────────────────────────┘
    │
    ▼
Story 04: Tyhp Runtime Composer Packages
    │
    ▼
Story 10: Build Action (full wiring)
```

The `BuildAction` skeleton from Story 01 has `// PLACEHOLDER_STORY_09: Run emitter` and `// PLACEHOLDER_STORY_09: Write output files` markers that this story fills in.

### Existing Scaffolding Assessment

| File | Status | Decision |
|------|--------|----------|
| `TyhpEmitter.cs` | Empty class (2 usings, empty body) | **Replace entirely** — build the emitter from scratch |
| `PHPOutputFile.cs` | **Empty shell** — every property and method is currently commented out (see the block of `// public ...` stubs in the file); there is no live property structure yet | **Define from the commented stubs** — uncomment/define the intended properties (`FilePath`, `FileDeclares`, `FileNameSpace`, `FileImports`, `Statements`, `IsPSR4ObjectDeclaration`) and implement the methods (`FromAstTree`, `PruneFileImports`, `ConvertAliases`, `Merge`, `Generate`, `SourceMap`) |
| `EmitItem.cs` | **Working** — tree-based emit with indent, doc comment wrapping, copy, sort | **Keep and extend** — this is the core emission primitive |
| `Emitter/readme.md` | Documents the emitter template system (`@tyhpEmitterStart`/`@tyhpEmitterEnd`) | **Remove or update** — the emitter template system has been removed from the design |
| `EmitType.cs` | 22 enum values defining emit ordering | **Keep and extend** — may need additional types |

### The `EmitItem` Approach

The existing `EmitItem` class implements a tree-based code generation model:
- Each `EmitItem` has `StartContent` (list of strings), `EndContent` (list of strings), `Children` (ordered list), and an `EmitType` for sorting
- The `emit(indentLevel)` method recursively generates formatted code: start content at current indent → children at indent+1 → end content at current indent
- `SortedChildren()` orders children by `EmitType` then original insertion order — this ensures declarations appear in the conventional PHP order (constants before properties before methods, etc.)
- `Provider` holds a reference to the source AST node (critical for source map generation in Story 17)

This is a sound approach. The emitter's job is to build an `EmitItem` tree for each `PHPOutputFile`, then call `emit()` to produce the final PHP source string.

### Key Design Decisions

1. **Emitter pattern:** The emitter will use a recursive switch-based walker (not a visitor pattern) that dispatches on concrete AST node types. This avoids the overhead of the full ANTLR visitor pattern and gives explicit control over how each node type is emitted.

2. **AST pre-pass then emission:** `ConvertAliases()` runs as an AST-level pre-pass that modifies the AST in-place *before* emission begins — resolving tyhpdef aliases, rewriting extension method calls to static calls, rewriting operator overloads, etc. After the AST has been transformed, the emitter walks the already-transformed AST to build `EmitItem` trees (Phase 3-4). This separation means the emitter only deals with PHP-compatible constructs and does not need to handle Tyhp-to-PHP name resolution during emission.

3. **PHP pass-through first:** The initial emitter (this story) focuses on emitting valid PHP for code that is already essentially PHP. Tyhp-specific feature emission (structs→arrays, generics→erasure, extensions→static calls, etc.) is primarily Story 11, but this story includes the `ConvertAliases()` framework and basic transformations.

4. **Output file splitting strategy:** `PHPOutputFile.FromAstTree()` splits the AST into output file units before emission. Each unit has its own declares, namespace, imports, and statements. This splitting is driven by PSR-4 conventions and `declare(output_file="")` directives.

5. **Configuration-driven:** Output paths, namespace prefixes, strict types, PHP version target, and comment inclusion are driven by `Project` configuration (expanded in Story 10). For now, sensible defaults are used.

### File Organization

New and modified files in this implementation:

```
Tyhp/TyhpLang/Emitter/
├── TyhpEmitter.cs                    (~250 lines) — main entry point, orchestrates emission
├── TyhpEmitter.Declarations.cs       (~400 lines) — partial: namespace, class, interface, trait, enum, function declarations
├── TyhpEmitter.Statements.cs         (~450 lines) — partial: if, for, foreach, while, switch, try/catch, etc.
├── TyhpEmitter.Expressions.cs        (~400 lines) — partial: binary, unary, calls, access, literals, etc.
├── TyhpEmitter.Types.cs              (~150 lines) — partial: type expression emission (strip Tyhp-only types)
├── EmitContext.cs                     (~80 lines)  — context passed during emission (current scope, file, config)
├── EmitItem.cs                        (existing, minor extensions)
├── PHPOutputFile.cs                   (existing, methods implemented)
├── PHPOutputFileSplitter.cs           (~300 lines) — extracted FromAstTree() logic
├── OutputPathResolver.cs              (~100 lines) — PSR-4 path computation
├── AliasConverter.cs                  (~250 lines) — ConvertAliases() logic extracted
├── OutputFileWriter.cs                (~150 lines) — disk writing logic
└── readme.md                          (existing, unchanged)
```

### Safety Notes

- Before replacing `TyhpEmitter.cs` (even though it's empty), create a timestamped backup
- Before modifying `PHPOutputFile.cs`, create a timestamped backup
- `EmitItem.cs` should be extended incrementally, not replaced
- Never use destructive git commands
- Backup files are sacred — never delete or modify them

---

## Phase 1: Emitter Architecture, EmitContext, and AST-to-Emit Dispatch Framework




### Phase Overview

Establish the emitter's architectural foundation: the `EmitContext` class that carries state during emission, the `TyhpEmitter` class skeleton with the main entry point and dispatch mechanism, and any extensions needed on `EmitItem`. This phase produces no PHP output yet — it creates the framework that Phases 3-4 populate with per-node-type emission logic.

### Deliverables

- `Tyhp/TyhpLang/Emitter/EmitContext.cs` — Emission context carrying current scope, file info, config, and diagnostic bag
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` — Main emitter class with entry point and top-level dispatch (replacing the empty class)
- Minor extensions to `Tyhp/TyhpLang/Emitter/EmitItem.cs` — Helper factory methods for common patterns
- `Tyhp/TyhpLang/Emitter/OutputPathResolver.cs` — PSR-4 compliant output path computation

### Implementation Details

#### 1.1 Create `EmitContext`

**New file:** `Tyhp/TyhpLang/Emitter/EmitContext.cs`

The emit context is passed through all emission methods, carrying the state needed to make emission decisions:

- `GlobalScope GlobalScope { get; }` — the bound scope tree (from Story 02)
- `DiagnosticBag Diagnostics { get; }` — for reporting emitter errors/warnings
- `SrcFileAst CurrentSourceFile { get; set; }` — the Tyhp source file currently being emitted
- `string CurrentNamespace { get; set; }` — the current PHP namespace context (for resolving relative type references)
- `EmitConfig Config { get; }` — emitter-specific configuration extracted from `Project`
- `Dictionary<string, string> TypeAliasMap { get; }` — maps Tyhp type alias names to their PHP equivalents (populated from binder)
- `Dictionary<string, string> TyhpdefAliasMap { get; }` — maps tyhpdef alias names to real PHP names (populated from binder)
- `HashSet<string> UsedImports { get; }` — tracks which `use` imports are actually referenced in the current output file (for pruning)
- `int UniqueVarCounter { get; set; }` — counter for generating unique variable names (e.g., `$__tyhp_temp_0`)
- `string GenerateUniqueVarName(string prefix = "__tyhp")` — helper method

Create a nested `EmitConfig` class (or record) with:

- `string OutputPath { get; }` — base output directory (from `output.path` config, default: `"build/"`)
- `string? NamespacePrefix { get; }` — prefix added to all namespaces (from `output.namespacePrefix` config)
- `bool StrictTypes { get; }` — add `declare(strict_types=1)` (from `output.strictTypes` config, default: `true`)
- `bool IncludeComments { get; }` — include comments in output (from `output.comments` config, default: `true`)
- `string TargetPhpVersion { get; }` — target PHP version string (from `output.phpVersion` config, default: `"8.4"`)
- `string? EntryPointAutoloader { get; }` — path to autoloader to include in entry points (from `build.entryPointAutoloader` config)

For now, `EmitConfig` should have a constructor that accepts `Project` and extracts known config values, using defaults for any config options not yet parsed (Story 10 will expand config parsing). Also provide a parameterless constructor with sensible defaults for testing.

#### 1.2 Redesign `TyhpEmitter` Entry Point

**File:** `Tyhp/TyhpLang/Emitter/TyhpEmitter.cs` (backup and replace)

The emitter class orchestrates the full emission pipeline:

- Constructor: `TyhpEmitter(EmitContext context)`
- Main entry point: `IReadOnlyList<PHPOutputFile> Emit(IEnumerable<SrcFileAst> parsedFiles)`

The `Emit()` method performs these steps:

```
1. For each SrcFileAst, call PHPOutputFile.FromAstTree() to split into output file units
2. For each PHPOutputFile, call ConvertAliases() to transform the AST in-place (resolve tyhpdef aliases, rewrite extension method calls to static calls, rewrite operator overloads to method calls, erase type aliases, etc.)
3. For each PHPOutputFile, walk its (now-transformed) Statements and build EmitItem trees
4. For each PHPOutputFile, call PruneFileImports()
5. Merge PHPOutputFiles that share the same output path (namespace-level functions)
6. For each PHPOutputFile, call Generate() to produce the final PHP string
7. Return the list of PHPOutputFiles with their generated content
```

Each step should be a separate method on `TyhpEmitter` for clarity and testability.

The core emission logic uses a dispatch method:

- `EmitItem EmitNode(IBase2Ast node, EmitItem parent)` — dispatches to the appropriate emission method based on the node's concrete type. Uses a switch on the node type (pattern matching on `node is PhpClassDeclAst`, `node is PhpFunctionDeclAst`, etc.)

This method is the central routing hub. It will be populated in Phases 3-4 with cases for each AST node type.

#### 1.3 Implement `TyhpEmitter` as Partial Class

Since the emitter will grow large, structure it as partial classes from the start:

- `TyhpEmitter.cs` — constructor, `Emit()`, `EmitNode()` dispatch, pipeline orchestration
- `TyhpEmitter.Declarations.cs` — methods for emitting declarations (Phase 3)
- `TyhpEmitter.Statements.cs` — methods for emitting statements (Phase 4)
- `TyhpEmitter.Expressions.cs` — methods for emitting expressions (Phase 4)
- `TyhpEmitter.Types.cs` — methods for emitting type expressions (Phase 3/4)

Create the partial class files with stub methods that the dispatch can call. Each stub should add a diagnostic indicating the construct is not yet emittable:

```csharp
// PLACEHOLDER_PHASE_3: Implement declaration emission
// PLACEHOLDER_PHASE_4: Implement statement and expression emission
```

#### 1.4 Add Helper Factory Methods to `EmitItem`

**File:** `Tyhp/TyhpLang/Emitter/EmitItem.cs` (extend incrementally)

Add static factory methods for common emission patterns to reduce boilerplate in the emitter:

- `EmitItem.Line(IBaseAst provider, EmitType type, string content, EmitItem? parent)` — single-line item (no end content)
- `EmitItem.Block(IBaseAst provider, EmitType type, string openLine, string closeLine, EmitItem? parent)` — block with open/close (e.g., `{` / `}`)
- `EmitItem.Empty(IBaseAst provider, EmitType type, EmitItem? parent)` — empty item (for grouping children)
- `EmitItem.MultiLine(IBaseAst provider, EmitType type, IEnumerable<string> lines, EmitItem? parent)` — multiple lines of content

These are convenience wrappers around the existing constructor that reduce repetitive `new List<string>() { ... }` boilerplate throughout the emitter.

#### 1.5 Create `OutputPathResolver`

**New file:** `Tyhp/TyhpLang/Emitter/OutputPathResolver.cs`

Compute output file paths based on PSR-4 conventions and configuration:

- `string ResolveObjectPath(string fullyQualifiedName, EmitConfig config)` — given a fully-qualified class/interface/trait/enum name, compute the output file path relative to the output directory. PSR-4: `\App\Models\User` with `output.path = "build/"` and `psr4 = { "App\\": "src/" }` → `build/src/Models/User.php`
- `string ResolveNamespaceFunctionsPath(string namespaceName, EmitConfig config)` — path for grouped namespace-level functions. Produces `_functions.php` within the namespace's directory path. Example: namespace `App\Helpers` → `build/src/Helpers/_functions.php` (assuming PSR-4 mapping `App\\ → src/`). If no namespace (global), produces `build/_functions.php`.
- `string ResolveEntryPointPath(string sourceFilePath, EmitConfig config)` — path for entry point / root code files. Mirrors the source file's path relative to the source root, replacing `.tyhp` with `.php`. Example: source file `tyhp-src/web/index.tyhp` with source root `tyhp-src/` and output path `src/` → `src/web/index.php`. The source root is derived from the project's `include` glob patterns (the common prefix of matched files) or from an explicit `output.sourceRoot` config if provided.
- `string ResolveOutputFilePath(string declaredPath, EmitConfig config)` — path for `declare(output_file="")` directives

The PSR-4 alias mapping (`psr4` config) is not yet parsed (Story 10), so provide a default behavior: namespace segments become directory segments, class name becomes filename, all under `config.OutputPath`.

Note: A project may have multiple entry points (e.g., a web handler and a CLI tool). Each source file with root-level code becomes its own entry point output file, with the path mirroring the source structure.

### Acceptance Criteria

- [ ] `EmitContext` compiles and carries all required state properties
- [ ] `EmitConfig` can be constructed from `Project` or with defaults
- [ ] `TyhpEmitter.Emit()` method exists and performs the pipeline steps (with stubs for unimplemented parts)
- [ ] `EmitNode()` dispatch method exists and handles unknown node types by adding a diagnostic and returning an empty `EmitItem`
- [ ] Partial class files for declarations, statements, expressions, and types are created with `PLACEHOLDER_PHASE_N` markers
- [ ] `EmitItem` factory methods (`Line`, `Block`, `Empty`, `MultiLine`) work correctly
- [ ] `OutputPathResolver` computes PSR-4 paths for simple cases (no alias mapping needed yet)
- [ ] All new and modified files compile without errors
- [ ] The existing `EmitItem.emit()`, `WrapDocComment()`, and `CreateCopy()` methods still work

### Dependencies

- **Requires:** Story 01 (DiagnosticBag, CompilationResult), Story 02 (GlobalScope, symbols — for type alias maps), Story 08 (Checker — emitter runs after checking)
- **Provides:** Framework for all subsequent emission phases; `EmitContext` and dispatch mechanism

---

## Phase 2: PHPOutputFile.FromAstTree() — AST Splitting into Output File Units




### Phase Overview

Implement the `PHPOutputFile.FromAstTree()` static method that takes a parsed `SrcFileAst` and splits it into one or more `PHPOutputFile` instances. Each output file unit corresponds to a single `.php` output file: one per object declaration (PSR-4), one for grouped namespace functions, one for root code (entry point), and special handling for anonymous namespace blocks and `declare(output_file="")` directives.

### Deliverables

- `Tyhp/TyhpLang/Emitter/PHPOutputFileSplitter.cs` — Extracted splitting logic (keeps `PHPOutputFile.cs` focused on single-file concerns)
- Modified `Tyhp/TyhpLang/Emitter/PHPOutputFile.cs` — `FromAstTree()` delegates to splitter; add `OutputFilePath` property and `SourceFileAst` back-reference
- Support for PSR-4 object splitting, namespace function grouping, root code extraction, anonymous namespace blocks, and `declare(output_file="")` directives

### Implementation Details

#### 2.1 Create `PHPOutputFileSplitter`

**New file:** `Tyhp/TyhpLang/Emitter/PHPOutputFileSplitter.cs`

This class encapsulates the AST-to-output-file splitting logic. The main method:

- `static IEnumerable<PHPOutputFile> Split(SrcFileAst srcFile, EmitContext context)`

The splitting algorithm walks the `SrcFileAst`'s top-level statements and classifies each:

**Step 1: Extract file-level metadata**
- Walk the top statement list (`PhpTopStatementListAst` children)
- Collect `DeclareAst` nodes at the beginning (file-level declares like `strict_types`)
- Collect `PhpImportDeclListAst` / `PhpImportDeclAst` nodes (file-level `use` imports)
- Identify the `PhpNamespaceDeclAst` or `PhpBlockNamespaceDeclAst` (namespace declaration)

**Step 2: Classify top-level statements**

For each top-level statement child of the source file (or namespace block):

| AST Node Type | Output File Strategy |
|---------------|---------------------|
| `PhpObjectTypeDeclAst` (class/interface/trait/enum) | Own file — PSR-4 path from FQN |
| `TyhpStructDeclAst` | Erased in PHP output (compile-time only) — no output file |
| `TyhpExtensionDeclAst` | Own file — treated as a regular class in PHP |
| `PhpFunctionDeclAst` (namespace-level) | Grouped with other functions in same namespace into `_functions.php` |
| `PhpConstDeclAst` / `PhpConstDeclListAst` (namespace-level) | Grouped with functions file or own file |
| `PhpDeclareAst` with `output_file` directive | Own file at the specified path |
| `PhpIfAst` wrapping an object declaration (conditional class) | Root code file (not extracted as PSR-4) |
| All other statements (assignments, expressions, echo, etc.) | Root code file (entry point) |
| `TyhpTypeAliasAst` | Erased in PHP output (compile-time only) — no output file |

**Step 3: Create `PHPOutputFile` instances**

For each classified group:

1. **Object declarations:** Create one `PHPOutputFile` per object. Set:
   - `IsPSR4ObjectDeclaration = true`
   - `FileDeclares` = file-level declares from Step 1
   - `FileNameSpace` = the namespace declaration
   - `FileImports` = the file-level `use` imports (will be pruned later)
   - `Statements` = the single object declaration AST node
   - `OutputFilePath` = computed via `OutputPathResolver.ResolveObjectPath()`

2. **Namespace-level functions/constants:** Create one `PHPOutputFile` for all functions in the same namespace, output as `_functions.php` within the namespace directory. Set:
   - `IsPSR4ObjectDeclaration = false`
   - `Statements` = all function/constant declarations
   - `OutputFilePath` = computed via `OutputPathResolver.ResolveNamespaceFunctionsPath()` (e.g., `src/Helpers/_functions.php` for namespace `App\Helpers`)
   - Note: Composer's PSR-4 does not auto-load function files. The `ComposerJsonService` (Story 10, Phase 4) must add these `_functions.php` files to `autoload.files` in the output `composer.json` for them to be auto-included.

3. **Root code (entry point):** Create one `PHPOutputFile` for remaining statements. A project may have multiple entry points (e.g., web handler, CLI tool). Set:
   - `IsPSR4ObjectDeclaration = false`
   - `Statements` = all non-declaration top-level statements
   - `OutputFilePath` = computed via `OutputPathResolver.ResolveEntryPointPath()` — mirrors the source file's path relative to the source root, replacing `.tyhp` with `.php` (e.g., `tyhp-src/web/index.tyhp` → `src/web/index.php`)

4. **`declare(output_file="")` blocks:** Create one `PHPOutputFile` per directive. Set:
   - `IsPSR4ObjectDeclaration = false`
   - `OutputFilePath` = the path specified in the declare directive

5. **Anonymous namespace blocks:** Create one `PHPOutputFile` per anonymous namespace. These get wrapped in a `namespace { }` block in the output.

#### 2.2 Handle Wrapped Declarations

The `PHPOutputFile.cs` comments specifically mention wrapped declarations like:

```php
if (!\class_exists("Foo")) { class Foo { ... } }
```

These should be treated as root code, NOT extracted as PSR-4 object declarations. The splitter should detect this pattern: if an `PhpIfAst` (or similar conditional) contains an object declaration as its body, classify the entire if-statement as root code.

Detection heuristic: Check if a statement is a conditional (`PhpIfAst`) whose body contains exactly one `PhpObjectTypeDeclAst`. If so, treat the outer if-statement as root code.

#### 2.3 Update `PHPOutputFile` Properties

**File:** `Tyhp/TyhpLang/Emitter/PHPOutputFile.cs`

Add new properties:

- `string OutputFilePath { get; set; }` — the computed output file path (relative to output root)
- `SrcFileAst? SourceFileAst { get; set; }` — back-reference to the source file for source mapping
- `string? GeneratedContent { get; set; }` — the final PHP source string (set by `Generate()`)
- `EmitItem? RootEmitItem { get; set; }` — the root `EmitItem` tree for this file (set by `TyhpEmitter`)
- `List<SourceMapping>? SourceMappings { get; set; }` — source map data (for Story 17)

Update `FromAstTree()` to delegate to `PHPOutputFileSplitter.Split()`:

```csharp
public static IEnumerable<PHPOutputFile> FromAstTree(SrcFileAst rootAstNode, EmitContext context)
{
    return PHPOutputFileSplitter.Split(rootAstNode, context);
}
```

Note: The existing signature `FromAstTree(IBaseAst rootAstNode)` should be updated to accept `SrcFileAst` and `EmitContext`. The original signature returning `IEnumerable<PHPOutputFile>` is correct.

#### 2.4 Handle Multiple Namespaces in One File

PHP allows multiple namespace blocks in a single file (block syntax):

```php
namespace App\Models {
    class User { ... }
}
namespace App\Services {
    class UserService { ... }
}
```

The splitter should handle each namespace block independently, extracting objects from each into their own output files with the correct namespace.

#### 2.5 Handle No-Namespace (Global Namespace) Code

Code without a namespace declaration is in the global namespace (`\`). The splitter should:
- Set `FileNameSpace = null` for global namespace code
- Compute output paths without namespace directory segments
- Handle the edge case where some code is namespaced and some is not in the same file

### Acceptance Criteria

- [ ] `PHPOutputFileSplitter.Split()` correctly produces one `PHPOutputFile` per object declaration
- [ ] Object declarations get `IsPSR4ObjectDeclaration = true` and correct `OutputFilePath`
- [ ] Namespace-level functions are grouped into a single output file per namespace
- [ ] Root code (entry point) is grouped into its own output file
- [ ] File-level declares (`strict_types`) are propagated to all output files from the same source file
- [ ] File-level `use` imports are propagated to all output files from the same source file
- [ ] Wrapped declarations (`if (!class_exists(...)) { class Foo { } }`) are treated as root code
- [ ] `declare(output_file="")` directives produce output files at the specified path
- [ ] Anonymous namespace blocks produce their own output files with namespace wrapping
- [ ] Multiple namespace blocks in a single file are handled correctly
- [ ] Global namespace (no namespace) code is handled correctly
- [ ] `PHPOutputFile.OutputFilePath` is set for all output files
- [ ] `PHPOutputFile.SourceFileAst` back-reference is set
- [ ] Structs and type aliases are skipped (not emitted — compile-time only)
- [ ] The project compiles without errors

### Dependencies

- **Requires:** Phase 1 (EmitContext, OutputPathResolver)
- **Provides:** Split output file units for the emitter to process in Phases 3-4

---

## Phase 3: TyhpEmitter Core — PHP Pass-Through (Declarations and Top-Level Constructs)




### Phase Overview

Implement the emission logic for PHP declarations and top-level constructs. This is the first "real" emission work — it takes AST nodes representing namespaces, classes, interfaces, traits, enums, functions, properties, methods, and constants, and produces `EmitItem` trees that generate valid PHP source code. The focus is on PHP pass-through: emit constructs that are already valid PHP syntax, stripping only Tyhp-specific annotations that PHP does not understand.

### Deliverables

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.Declarations.cs` — Emission methods for all declaration types
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.Types.cs` — Type expression emission (strip generics, convert Tyhp-only types)
- Updated `TyhpEmitter.cs` dispatch to route declaration AST nodes to their emission methods

### Implementation Details

#### 3.1 Implement Namespace Declaration Emission

Handle `PhpNamespaceDeclAst` and `PhpBlockNamespaceDeclAst`:

- **Statement namespace** (`namespace App\Models;`): Emit as `EmitItem` with `EmitType.FileNamespaceDeclaration` and content `"namespace App\\Models;"`
- **Block namespace** (`namespace App\Models { ... }`): Emit as `EmitItem` with `EmitType.BlockNamespaceDeclaration`, start content `"namespace App\\Models {"`, end content `"}"`, children are the namespace body statements
- If `EmitConfig.NamespacePrefix` is set, prepend it to the namespace name

#### 3.2 Implement Use/Import Statement Emission

Handle `PhpImportDeclListAst` and `PhpImportDeclAst`:

- Emit each `use` statement as an `EmitItem` with `EmitType.ImportUse`
- Preserve the import type: `use Foo\Bar;`, `use function Foo\bar;`, `use const Foo\BAR;`
- Preserve aliases: `use Foo\Bar as Baz;`
- Preserve group imports: `use Foo\{Bar, Baz};`
- Track each import in `EmitContext.UsedImports` for later pruning

#### 3.3 Implement Class/Interface/Trait/Enum Declaration Emission

Handle `PhpObjectTypeDeclAst`:

- Determine the object kind (class, interface, trait, enum) from the AST node
- Emit modifiers: `abstract`, `final`, `readonly` (PHP 8.2+)
- Emit the keyword: `class`, `interface`, `trait`, `enum`
- Emit the name
- **Strip generic type parameters** — PHP does not support generics. `class Collection<T>` → `class Collection`
- Emit `extends` clause (single class for classes, multiple for interfaces)
- Emit `implements` clause
- Emit backed type for enums: `enum Suit: string`
- Emit the class body as children with appropriate `EmitType` values

The class body emission should produce children sorted by `EmitType`:
1. `EmitType.ObjectTraitUse` — trait use statements
2. `EmitType.ObjectConstantDeclaration` — class constants
3. `EmitType.ObjectStaticPropertyDeclaration` — static properties
4. `EmitType.ObjectInstancePropertyDeclaration` — instance properties
5. `EmitType.ObjectConstructor` — constructor
6. `EmitType.ObjectDestructor` — destructor
7. `EmitType.ObjectStaticMethods` — static methods
8. `EmitType.ObjectInstanceMethods` — instance methods

The `EmitItem.SortedChildren()` already sorts by `EmitType` then insertion order, so assigning the correct `EmitType` is sufficient.

#### 3.4 Implement Extension Declaration Emission

Handle `TyhpExtensionDeclAst`:

- Tyhp extensions compile to regular PHP classes
- Strip the `extension` keyword and the `extends TypeName` on the first parameter
- Emit as a regular `class` or `abstract class`
- Extension methods become static methods where the first parameter is the extended type (this is handled by ConvertAliases in Phase 5, but the declaration itself is emitted here)

#### 3.5 Implement Function Declaration Emission

Handle `PhpFunctionDeclAst`:

- Emit `function functionName(` parameter list `) : returnType {` body `}`
- Strip generic type parameters from the signature
- Emit parameter list: type hint, name, default value, variadic (`...`), by-reference (`&`), promoted visibility (for constructors)
- Emit return type (may need stripping of Tyhp-only types — see 3.9)
- Handle named short function syntax: Tyhp `fn foo(): int => expr;` converts to PHP `function foo(): int { return expr; }`. This applies only to *named* function declarations using the `fn` keyword at the top/namespace level — they are syntactic sugar for standard PHP function declarations with an implicit `return`. Anonymous arrow functions (`fn($x) => $x + 1`) are a different construct and are emitted as-is (they are valid PHP 7.4+ syntax).
- `// PLACEHOLDER_STORY_11: Async/await handling` — async functions (`async function`), `await` expressions, and related constructs are entirely a Story 11 concern. The emitter should not attempt to strip `async` or handle `await` in this story. If an `async` function or `await` expression is encountered, emit a `MessageCode.EmitterTyhpConstructNotImplemented` diagnostic.

#### 3.6 Implement Method Declaration Emission

Handle `PhpMethodDeclAst`:

- Same as function declaration but with visibility modifiers (`public`, `protected`, `private`)
- Handle `static`, `abstract`, `final` modifiers
- Handle constructor promotion: parameters with visibility modifiers are also property declarations. Emit them as-is if targeting PHP 8.0+ (constructor promotion is native PHP).
- Handle property hooks (PHP 8.4): if targeting PHP 8.4+, emit hook syntax directly. If targeting older PHP, transform to `__get`/`__set` methods (Story 11 concern).
- Assign correct `EmitType`: constructor → `ObjectConstructor`, destructor → `ObjectDestructor`, static → `ObjectStaticMethods`, instance → `ObjectInstanceMethods`

#### 3.7 Implement Property Declaration Emission

Handle `PhpPropertyDeclAst`, `PhpPropertyAst`, `PhpPropertyListAst`:

- Emit visibility, `static`, `readonly` modifiers
- Emit type hint
- Emit property name with `$` prefix
- Emit default value if present
- Handle property hooks (`PhpPropertyHookAst`): emit PHP 8.4 hook syntax if targeting 8.4+, otherwise `// PLACEHOLDER_STORY_11: Property hooks for PHP < 8.4`
- Assign correct `EmitType`: static → `ObjectStaticPropertyDeclaration`, instance → `ObjectInstancePropertyDeclaration`

#### 3.8 Implement Constant Declaration Emission

Handle `PhpConstDeclAst`, `PhpConstDeclListAst`:

- Emit `const NAME = value;` for namespace-level constants
- For class constants: emit visibility modifier, `const` keyword, name, type (PHP 8.3+), value
- Handle enum cases (`PhpEnumCaseAst`): emit `case Name;` or `case Name = value;`

#### 3.9 Implement Type Expression Emission

**File:** `TyhpEmitter.Types.cs`

Handle `PhpTypeExpressionAst`, `PhpTypeExpressionListAst`, `PhpNamedTypeAst`, `PhpBuiltinTypeAst`:

- Emit PHP-valid type expressions: `int`, `string`, `float`, `bool`, `array`, `callable`, `iterable`, `object`, `mixed`, `void`, `never`, `null`, `true`, `false`, `self`, `static`, `parent`
- Emit fully-qualified type names: `\App\Models\User`
- Emit nullable types: `?Type`
- Emit union types: `Type1|Type2`
- Emit intersection types: `Type1&Type2`
- **Strip generic type arguments:** `Collection<User>` → `Collection`. Generic type arguments are compile-time only in Tyhp; PHP does not support them.
- **Strip Tyhp-only type annotations** that go beyond PHP's type system:
  - `decimal` always emits as `\Tyhp\Decimal`. The `tyhp/decimal` Composer package is REQUIRED whenever the `decimal` type is used in Tyhp source code. The checker enforces that the `tyhp/decimal` package is present as a project dependency when `decimal` usage is detected. There is no fallback to `float`.
  - Struct types → `array`
  - Type alias references → resolved underlying type
  - Generic parameter names (`T`, `TKey`, `TValue`) → `mixed` (or the constraint type if available)
  - `$param is Type` return type (type guards) → `bool`
- Preserve PHP-compatible type expressions exactly as-is

#### 3.10 Implement Trait Use Emission

Handle `PhpTraitUseAst`, `PhpTraitAdaptationListAst`, `PhpTraitAliasAst`, `PhpTraitPrecedenceAst`:

- Emit `use TraitName;` or `use TraitA, TraitB;`
- Emit trait adaptations block if present:
  ```php
  use TraitA, TraitB {
      TraitA::method insteadof TraitB;
      TraitB::method as aliasMethod;
  }
  ```

#### 3.11 Implement Attribute Emission

Handle `PhpAttributeAst`, attribute lists:

- Emit `#[AttributeName(args)]` syntax (PHP 8.0+)
- Handle multiple attributes: `#[Attr1, Attr2]`
- Handle attributes on classes, methods, properties, parameters, functions, constants

#### 3.12 Implement Declare Statement Emission

Handle `PhpDeclareAst`:

- Emit file-level: `declare(strict_types=1);`
- Emit block-level: `declare(encoding='UTF-8') { ... }`
- Filter out `declare(output_file="")` directives (these are Tyhp-specific for file splitting)

### Acceptance Criteria

- [ ] Namespace declarations (statement and block) emit correctly
- [ ] Use/import statements emit correctly with aliases and group imports
- [ ] Class declarations emit with correct modifiers, extends, implements (generics stripped)
- [ ] Interface declarations emit correctly
- [ ] Trait declarations emit correctly with trait use and adaptations
- [ ] Enum declarations emit correctly with cases and backed types
- [ ] Extension declarations emit as regular PHP classes
- [ ] Function declarations emit with parameters, return types, and body (generics stripped)
- [ ] Method declarations emit with visibility, modifiers, and correct `EmitType` assignment
- [ ] Constructor promotion parameters emit correctly
- [ ] Property declarations emit with type hints, defaults, and correct `EmitType`
- [ ] Constant declarations emit correctly (namespace-level and class-level)
- [ ] Type expressions strip generics (`Collection<User>` → `Collection`)
- [ ] Type expressions strip Tyhp-only types (`decimal` → `\Tyhp\Decimal`, type guard returns → `bool`)
- [ ] Attributes emit correctly
- [ ] Declare statements emit correctly (filtering out `output_file` directives)
- [ ] Short function syntax converts to standard function declarations
- [ ] `async` functions and `await` expressions emit a `MessageCode.EmitterTyhpConstructNotImplemented` diagnostic (deferred to Story 11)
- [ ] Doc comments are preserved via `EmitItem.WrapDocComment()` when `EmitConfig.IncludeComments` is true
- [ ] All `EmitType` values are assigned correctly for proper ordering within class bodies
- [ ] The project compiles without errors

### Dependencies

- **Requires:** Phase 1 (EmitContext, EmitItem factories, TyhpEmitter dispatch), Phase 2 (PHPOutputFile splitting — provides the statement lists to emit)
- **Provides:** Declaration emission for all top-level and class-level constructs; type expression emission for all phases

---

## Phase 4: TyhpEmitter Core — PHP Pass-Through (Statements and Expressions)




### Phase Overview

Implement emission logic for all PHP statement types (control flow, loops, try/catch, return, echo, etc.) and all expression types (binary/unary operations, function calls, method calls, array access, string literals, etc.). After this phase, the emitter can produce valid PHP output for code that uses standard PHP constructs — the full "PHP pass-through" capability.

### Deliverables

- `Tyhp/TyhpLang/Emitter/TyhpEmitter.Statements.cs` — Emission methods for all statement types
- `Tyhp/TyhpLang/Emitter/TyhpEmitter.Expressions.cs` — Emission methods for all expression types
- Updated `TyhpEmitter.cs` dispatch with complete routing for statements and expressions

### Implementation Details

#### 4.1 Implement Control Flow Statement Emission

**If/Else:**
Handle `PhpIfAst`:
- Emit `if (condition) {` body `}` with optional `elseif` and `else` clauses
- Each clause body is a child `EmitItem` with `EmitType.SubBlockStatement`
- Handle single-statement bodies (no braces) — always emit with braces for consistency in generated code

**Switch/Match:**
Handle `PhpConditionalAst`, `PhpConditionalArmAst`:
- For `switch`: emit `switch ($expr) {` with `case value:` / `default:` arms and `break;`
- For `match` (PHP 8.0+): emit `match ($expr) {` with `value => result,` arms

#### 4.2 Implement Loop Statement Emission

Handle `PhpLoopAst`:
- Determine loop type from the AST node's properties or `Identifier`
- **For loop:** `for ($i = 0; $i < $n; $i++) {` body `}`
- **Foreach:** `foreach ($array as $key => $value) {` body `}` — handle both `$value` and `$key => $value` forms
- **While:** `while (condition) {` body `}`
- **Do-while:** `do {` body `} while (condition);`

#### 4.3 Implement Try/Catch/Finally Emission

Handle `PhpTryCatchAst`, `PhpCatchClauseAst`, `PhpCatchListAst`:
- Emit `try {` body `}` with catch clauses `catch (ExceptionType $e) {` body `}` and optional `finally {` body `}`
- Handle multi-type catch: `catch (TypeA | TypeB $e)`
- Handle catch without variable (PHP 8.0+): `catch (ExceptionType)`

#### 4.4 Implement Jump Statement Emission

Handle `PhpJumpStatementAst`:
- **Return:** `return $expr;` or `return;`
- **Break:** `break;` or `break $n;`
- **Continue:** `continue;` or `continue $n;`

Handle `PhpReturnStatementAst`:
- Emit `return expression;`

Handle `PhpGotoStatementAst`:
- Emit `goto label;`

Handle `PhpLabelStatementAst`:
- Emit `label:`

#### 4.5 Implement Expression Statement Emission

Handle expression-as-statement (an expression followed by semicolon):
- Emit the expression and append `;`

Handle `PhpEchoStatementAst`:
- Emit `echo expr1, expr2, ...;`

Handle `PhpUnsetStatementAst`:
- Emit `unset($var1, $var2);`

Handle `PhpIssetStatementAst`:
- Emit `isset($var1, $var2)`

Handle `PhpEmptyStatementAst`:
- Emit `empty($expr)`

Handle `PhpGlobalStatementAst`:
- Emit `global $var1, $var2;`

Handle `PhpStaticStatementAst`:
- Emit `static $var = value;`

Handle `PhpHaltCompilerAst`:
- Emit `__halt_compiler();`

#### 4.6 Implement Binary Expression Emission

Handle `PhpBinaryOpAst`:
- Emit `leftExpr operator rightExpr` for all PHP operators:
  - Arithmetic: `+`, `-`, `*`, `/`, `%`, `**`
  - Comparison: `==`, `===`, `!=`, `!==`, `<`, `>`, `<=`, `>=`, `<=>`
  - Logical: `&&`, `||`, `and`, `or`, `xor`, `!`
  - Bitwise: `&`, `|`, `^`, `~`, `<<`, `>>`
  - String: `.` (concatenation)
  - Assignment: `=`, `+=`, `-=`, `*=`, `/=`, `.=`, `??=`, etc.
  - Null coalescing: `??`
  - Instanceof: `instanceof`
  - Ternary: `condition ? trueExpr : falseExpr`
  - Elvis: `expr ?: default`
- Handle parenthesization: add parentheses around sub-expressions when needed for precedence clarity. A simple approach is to always parenthesize sub-binary-expressions unless they are the same operator.

#### 4.7 Implement Unary Expression Emission

Handle `PhpUnaryOpAst`:
- Prefix operators: `!`, `-`, `+`, `~`, `@` (error suppression), `++`, `--`
- Postfix operators: `++`, `--`
- Cast operators: `(int)`, `(float)`, `(string)`, `(bool)`, `(array)`, `(object)` (not `(unset)` — removed; deprecated in PHP 8.5)
- Clone: `clone $expr`
- Print: `print $expr`
- Yield: `yield $expr`, `yield $key => $value`, `yield from $expr`
- Throw expression (PHP 8.0+): `throw $expr`
- Spread: `...$expr`

#### 4.8 Implement Call Expression Emission

Handle `PhpCallAst`:
- Function calls: `functionName($arg1, $arg2)`
- Static method calls: `ClassName::methodName($args)`
- Instance method calls: `$obj->methodName($args)`
- Named arguments (PHP 8.0+): `functionName(paramName: $value)`
- Spread arguments: `functionName(...$args)`

Handle argument list (`PhpArgumentListAst`, `PhpArgumentAst`):
- Positional arguments
- Named arguments
- By-reference arguments (`&$var`)
- Spread arguments (`...$array`)

#### 4.9 Implement Member Access Emission

Handle `PhpInstanceMemberAccessAst`:
- Property access: `$obj->property`
- Method call chain: `$obj->method1()->method2()`
- Nullsafe access (PHP 8.0+): `$obj?->property`

Handle `PhpStaticMemberAccessAst`:
- Static property: `ClassName::$property`
- Static method: `ClassName::method()`
- Class constant: `ClassName::CONSTANT`

Handle `PhpClassConstantAccessAst`:
- `ClassName::CONSTANT`

Handle `PhpArrayAccessAst`:
- `$array[$key]`
- `$array[]` (append)
- Nested: `$array[$key1][$key2]`

Handle `PhpMemberAccessAst`:
- General member access patterns

#### 4.10 Implement Scalar/Literal Expression Emission

Handle `PhpScalarAst`:
- Integer literals: `42`, `0x1A`, `0b1010`, `0o17`
- Float literals: `3.14`, `1.2e3`
- String literals: `'single'`, `"double $interpolation"`, heredoc, nowdoc
- Boolean literals: `true`, `false`
- Null literal: `null`

Handle `PhpStringAst`, `PhpEncapsListAst`, `PhpEncapsStringAst`:
- Simple strings: `'hello'`
- Interpolated strings: `"hello $name"` with `PhpEncapsListAst` children
- Heredoc and nowdoc syntax

Handle `PhpMagicConstantAst`:
- `__LINE__`, `__FILE__`, `__DIR__`, `__FUNCTION__`, `__CLASS__`, `__TRAIT__`, `__METHOD__`, `__NAMESPACE__`

#### 4.11 Implement Array Expression Emission

Handle `PhpArrayAst`:
- Short syntax: `[$item1, $item2]`
- Long syntax: `array($item1, $item2)`

Handle `PhpArrayPairAst`, `PhpArrayPairListAst`:
- Indexed pairs: `$value` or `$key => $value`
- Spread: `...$array`
- Nested arrays

#### 4.12 Implement Variable Expression Emission

Handle `PhpVariableAst`:
- Simple variables: `$varName`
- Variable variables: `$$varName` (emit as-is, this is valid PHP)

Handle `PhpVariableListAst`:
- List assignment: `[$a, $b, $c] = $array;` or `list($a, $b, $c) = $array;`

Handle `PhpDereferenceableAst`, `PhpDereferenceableExpressionAst`:
- Chained access: `$obj->method()->property[0]`

#### 4.13 Implement Object Creation Emission

Handle `PhpNewAst`:
- `new ClassName($args)`
- `new ClassName()` (no args)
- Anonymous classes: `new class extends Base implements Interface { ... }`
- **Strip generic type arguments:** `new Collection<User>()` → `new Collection()`

#### 4.14 Implement Closure/Arrow Function Emission

Handle `PhpInlineFunctionAst`:
- Closures: `function ($param) use ($captured) { ... }`
- Arrow functions: `fn($param) => $expr`
- Static closures: `static function ($param) { ... }` and `static fn($param) => $expr`
- Handle return type, parameter types, `use` clause

#### 4.15 Implement Tyhp Compile-Time Construct Emission

Handle Tyhp-specific AST nodes that need basic emission (full implementation in Story 11, but basic framework here):

Handle `TyhpNameofAst`:
- `nameof($variable)` → `'variable'` (string literal)
- `nameof(ClassName)` → `'ClassName'` (string literal)
- `// PLACEHOLDER_STORY_11: Full nameof implementation with all symbol types`

Handle `TyhpDefaultAst`:
- `default(int)` → `0`
- `default(string)` → `''`
- `default(bool)` → `false`
- `default(array)` → `[]`
- `default(float)` → `0.0`
- `default(?T)` → `null`
- `// PLACEHOLDER_STORY_11: Generic default() with arbitrary types`

Handle `TyhpTypeofAst`:
- `typeof(ClassName)` → `\Tyhp\Type::of('ClassName')`
- `typeof(int)` → `\Tyhp\Type::int()`
- `typeof(T)` inside a generic class → `$this->tyhpGenericObjectGetGenericType('T')?->getUnderlyingType() ?? Type::mixed()` (see Story 11, Phase 8 for full GenericObject emission pattern)
- `// PLACEHOLDER_STORY_11: Full typeof() implementation with all edge cases`

Handle `TyhpVariableExistsAst`:
- `// PLACEHOLDER_STORY_11: variable_exists() implementation`

Handle `TyhpTypedVarExprAst`:
- Typed variable expressions from Tyhp: strip the explicit type annotation, emit just the variable. The type is compile-time only.

#### 4.16 Implement Yield Expression Emission

Handle `PhpYieldAst`:
- `yield $value`
- `yield $key => $value`
- `yield from $generator`

#### 4.17 Implement Inline HTML/Output Emission

Handle `PhpInlineOutputAst`, `PhpInlineOutputListAst`:
- Inline HTML between PHP tags: emit as-is (close PHP tag, HTML content, open PHP tag)
- This is a direct pass-through

Handle `PhpNopStatementAst`:
- Empty statement: `;`

### Acceptance Criteria

- [ ] All PHP control flow statements emit correctly: if/elseif/else, switch, match
- [ ] All PHP loop statements emit correctly: for, foreach, while, do-while
- [ ] Try/catch/finally emits correctly with multi-type catch and catch-without-variable
- [ ] Jump statements emit correctly: return, break, continue, goto, labels
- [ ] Expression statements emit correctly: echo, unset, isset, empty, global, static
- [ ] Binary expressions emit correctly for all PHP operators with appropriate parenthesization
- [ ] Unary expressions emit correctly: prefix, postfix, cast, clone, print, yield, throw, spread
- [ ] Function/method calls emit correctly with named args, spread args, by-reference args
- [ ] Member access emits correctly: instance `->`, static `::`, nullsafe `?->`, array `[]`
- [ ] Scalar literals emit correctly: integers, floats, strings (all syntaxes), booleans, null
- [ ] Array expressions emit correctly with pairs, spread, nesting
- [ ] Variables emit correctly: simple, variable-variable, list assignment
- [ ] Object creation emits correctly (generic type args stripped)
- [ ] Closures and arrow functions emit correctly with `use` clause, types, return types
- [ ] Tyhp compile-time constructs have basic emission: `nameof()` → string literal, `default()` → literal values
- [ ] Typed variable expressions strip the type annotation
- [ ] Yield expressions emit correctly
- [ ] Inline HTML/output emits as-is
- [ ] The emitter can produce valid PHP for any standard PHP code structure (the "PHP pass-through" is complete)
- [ ] All new code compiles without errors

### Dependencies

- **Requires:** Phase 3 (declaration emission — provides the containing class/function context for statements/expressions)
- **Provides:** Complete PHP pass-through emission — the foundation for Tyhp-specific transformations

---

## Phase 5: ConvertAliases() — Tyhp-to-PHP Transformations




### Phase Overview

Implement the `ConvertAliases()` method on `PHPOutputFile` and the supporting `AliasConverter` class. `ConvertAliases()` is an **AST-level pre-pass** that modifies the AST in-place *before* the emitter walks it. It handles the transformation of Tyhp-specific names and constructs into their PHP equivalents by rewriting AST nodes directly. After this pass, the AST contains only PHP-compatible constructs, so the emitter (Phases 3-4) can focus purely on generating `EmitItem` trees without needing to resolve Tyhp-specific aliases or rewrite call patterns.

Note on the split between ConvertAliases and the splitter (Phase 2): The splitter handles *declaration* erasure — structs and type aliases are not added to output files. ConvertAliases handles *usage* erasure — references to type aliases are replaced with the underlying type, struct property access is rewritten to array access, etc. These are complementary, not redundant.

### Deliverables

- `Tyhp/TyhpLang/Emitter/AliasConverter.cs` — Extracted alias conversion logic
- Modified `Tyhp/TyhpLang/Emitter/PHPOutputFile.cs` — `ConvertAliases()` delegates to `AliasConverter`
- Support for tyhpdef alias resolution, type alias erasure, basic extension method rewriting, operator overload rewriting, struct property access rewriting, and magic constant translation

### Implementation Details

#### 5.1 Create `AliasConverter`

**New file:** `Tyhp/TyhpLang/Emitter/AliasConverter.cs`

This class performs AST-level transformations by modifying AST nodes in-place before emission:

- Constructor: `AliasConverter(EmitContext context)`
- Main entry: `void Convert(PHPOutputFile outputFile)` — applies all transformations to the output file

The converter needs access to the bound symbol information (from `EmitContext.GlobalScope`) to know which identifiers are tyhpdef aliases, type aliases, extension methods, operator overloads, etc.

#### 5.2 Implement Tyhpdef Alias Resolution

When tyhpdef files import types with aliases (e.g., a tyhpdef imports `\Some\Long\ClassName` as `ShortName`), the emitter must emit the real PHP name, not the alias.

- Walk the emitted code and replace alias names with their fully-qualified PHP names
- The binder's `UseIncludeSymbol` entries from tyhpdef loading provide the alias→real name mapping
- Populate `EmitContext.TyhpdefAliasMap` from the bound scope's tyhpdef symbols during emitter initialization

#### 5.3 Implement Type Alias Erasure

Tyhp `type` aliases are compile-time only and should be completely erased from the output:

- Type alias declarations (`TyhpTypeAliasAst`) should not appear in the output at all (handled in Phase 2 by the splitter — these are not added to output files)
- Usages of type aliases in type annotations should be replaced with the underlying type. The binder has resolved these — use the resolved type from the bound symbol.
- Populate `EmitContext.TypeAliasMap` from the binder's `TypeAliasSymbol` entries

#### 5.4 Implement Extension Method Call Rewriting

Extension method calls need to be rewritten from instance method syntax to static method calls:

- `$str->toCamelCase()` → `StringExtensions::toCamelCase($str)` where `StringExtensions` is the declaring extension class
- `$value->extensionMethod($arg)` → `ExtensionClass::extensionMethod($value, $arg)`

Detection and rewriting happen in the `AliasConverter` pre-pass (not during emission). The converter walks the AST looking for `PhpCallAst` nodes on instance member access, checks if the resolved symbol (from the binder) is an extension method, and rewrites the AST node to a static call with the object as the first argument.

Implementation approach:
- In `AliasConverter.Convert()`, walk all call AST nodes
- For each instance method call, check if the resolved symbol is from an extension declaration
- If so, rewrite the AST node: replace the instance call with a static call on the extension class, passing the original receiver as the first argument
- Add the extension class to the output file's imports

**Tyhpdef inline extension methods (Story 03):** Methods declared with the `extension` qualifier in tyhpdef class bodies compile to static methods on a synthetic extension class (auto-named `__TyhpInlineExt_{ClassName}` — the name the binder generates via `GetOrCreateSyntheticInlineExtensionScope()` in `TyhpBinder.Extensions.cs`). The emitter must:
- Generate the synthetic extension class PHP file (containing static methods for each inline extension function)
- Rewrite calls to these methods the same way as regular extension method calls

For this phase, implement the detection and rewriting framework. The full range of extension method scenarios (chained calls, nullable types, scalar extensions) is `// PLACEHOLDER_STORY_11: Full extension method rewriting`.

#### 5.5 Implement Operator Overload Call Rewriting

When operators are used on objects that have operator overloads, the emitter rewrites to method calls. There are two distinct cases based on whether the operator is from a class declaration or an extension (Story 03):

**Class-level operator overloads** (operator declared directly on the class):
- `$a + $b` → `$a->__add($b)` (instance method call on the left operand)
- `$a == $b` → `$a->__isEqualTo($b)`
- `(int)$a` → `$a->__toInt()` (for conversion overloads)

**Extension operator overloads** (operator from an extension or tyhpdef inline extension — Story 03):
- `$a + $b` → `ExtensionClass::__OP_Money_ADD_Money($a, $b)` (static call on the extension class)
- `$a == $b` → `ExtensionClass::__OP_Money_EQ_Money($a, $b)`
- `(int)$a` → `ExtensionClass::__OP_Money_CONVERT_TO_Int($a)`

Key difference: class-level operators compile to instance method calls on the object. Extension operators compile to static calls on the extension class (or the synthetic `__TyhpInlineExt_{ClassName}` class for tyhpdef inline extensions). The compiler resolves which method to call at compile time based on the operand types and the `<Type>` target.

**Extension operator method naming convention (Story 03):**
- Uses the actual target type name instead of `This`: `__OP_Money_ADD_Int` not `__OP_This_ADD_Int`
- This avoids conflicts when a single extension class holds operators for multiple target types
- No unified dispatch methods are generated — the compiler resolves directly to the specific static method

Detection: During binary expression emission, check if either operand's type has an operator overload for the given operator (via the binder's resolved symbols). Check both class-level overloads and extension overloads (including auto-activated extensions from tyhpdef `use extension`).

For this phase, implement the detection framework and the rewriting for the `decimal` type's operator overloads (which use tyhpdef inline `extension operator` — see Story 06). Full operator overload rewriting for user-defined overloads is `// PLACEHOLDER_STORY_11: User-defined operator overload rewriting`.

#### 5.6 Implement Struct Property Access Rewriting

Struct properties are backed by PHP arrays, so property access syntax must be rewritten:

- `$s->propertyName` → `$s['propertyName']`
- `$s->aliasedProp` → `$s['Original Key Name']` (when the struct property has an alias)

Detection: Check if the object being accessed is a struct type (via the binder's resolved type).

For this phase, implement the detection and basic rewriting. Full struct emission (construction, `with` keyword, clone) is `// PLACEHOLDER_STORY_11: Full struct emission`.

#### 5.7 Implement Property Accessor Usage Rewriting

Property accessor (get/set hooks) usage may need rewriting depending on PHP target version:

- **PHP 8.4+:** Property hooks are native — no rewriting needed
- **PHP < 8.4:** `$obj->prop` with accessor → `$obj->getProp()` (get), `$obj->prop = value` → `$obj->setProp(value)` (set)

For this phase, implement the version check. If targeting PHP 8.4+, this is a no-op. For older versions, `// PLACEHOLDER_STORY_11: Property accessor rewriting for PHP < 8.4`.

#### 5.8 Implement Magic Constant Translation

Tyhp adds custom magic constants that must be translated:

- `__TYHP_LINE__` → literal integer of the Tyhp source line number
- `__TYHP_FILE__` → literal string of the Tyhp source file path
- Standard PHP magic constants (`__LINE__`, `__FILE__`, etc.) are emitted as-is (they refer to the PHP output positions, not the Tyhp source)

#### 5.9 Implement Autoloader Inclusion

If `EmitConfig.EntryPointAutoloader` is set and the current output file is a root code (entry point) file:

- Prepend `require_once __DIR__ . '/vendor/autoload.php';` (Composer's `vendor/autoload.php`) to the file's statements
- Or insert after the `declare(strict_types=1);` line

### Acceptance Criteria

- [ ] `AliasConverter.Convert()` runs on each `PHPOutputFile` without errors
- [ ] Tyhpdef aliases are resolved to their real PHP names in the emitted code
- [ ] Type alias usages in type expressions emit the underlying type, not the alias name
- [ ] Type alias declarations do not appear in the output
- [ ] Extension method calls are detected and rewritten to static calls (basic cases)
- [ ] The extension class is added to the output file's imports when an extension method is used
- [ ] Tyhpdef inline extension methods generate a synthetic `__TyhpInlineExt_{ClassName}` PHP class
- [ ] Class-level operator overloads are rewritten to instance method calls
- [ ] Extension operator overloads are rewritten to static calls on the extension class (using target type name in method name per Story 03)
- [ ] Operator overload usage on `decimal` type is rewritten to static calls on `__TyhpInlineExt_Decimal`
- [ ] Struct property access is rewritten to array key access (basic cases)
- [ ] `__TYHP_LINE__` and `__TYHP_FILE__` are replaced with literals
- [ ] Standard PHP magic constants are emitted as-is
- [ ] Composer autoloader `require_once` (e.g., `vendor/autoload.php`) is added to entry point files when configured
- [ ] Framework/placeholders exist for all remaining Story 11 transformations
- [ ] The project compiles without errors

### Dependencies

- **Requires:** Phase 2 (splitter — provides the PHPOutputFile instances whose ASTs are transformed), Story 02 (binder — needed for symbol resolution to detect extensions, overloads, structs, aliases)
- **Provides:** Tyhp-to-PHP AST pre-pass transformation framework; after this pass, ASTs contain only PHP-compatible constructs for emission in Phases 3-4

---

## Phase 6: Generate(), PruneFileImports(), and Merge()




### Phase Overview

Implement the three remaining methods on `PHPOutputFile`: `PruneFileImports()` (remove unused `use` statements from the output), `Merge()` (combine two output files that share the same path), and `Generate()` (produce the final PHP source string from the `EmitItem` tree). After this phase, the emitter can produce complete PHP source files as strings.

### Deliverables

- Modified `Tyhp/TyhpLang/Emitter/PHPOutputFile.cs` — All three methods implemented
- `Generate()` produces correctly formatted PHP source with `<?php`, declares, namespace, imports, and body

### Implementation Details

#### 6.1 Implement `PruneFileImports()`

**File:** `Tyhp/TyhpLang/Emitter/PHPOutputFile.cs`

The `PruneFileImports()` method removes `use` imports that are not referenced in the emitted output.

Algorithm:
1. Collect all `use` import entries from `this.FileImports`
2. For each import, extract the alias or final name segment (the name that would appear in code)
3. Check if that name appears anywhere in the emitted statements — either by:
   - Walking the `EmitItem` tree and checking `StartContent`/`EndContent` strings for the name
   - Or more precisely, checking `EmitContext.UsedImports` which was populated during emission (Phase 3-4)
4. Remove any import whose name is not found in the used set
5. Also remove imports for Tyhp-only constructs that were erased (type aliases, struct types, etc.)

The `EmitContext.UsedImports` tracking approach is more reliable than string scanning. During emission, whenever a type name is emitted, the emitter adds it to `UsedImports`. Then `PruneFileImports()` simply filters `FileImports` to those present in `UsedImports`.

Additionally:
- Sort remaining imports alphabetically (convention in PHP projects)
- Group by type: classes first, then functions, then constants
- Remove duplicate imports

#### 6.2 Implement `Merge()`

**File:** `Tyhp/TyhpLang/Emitter/PHPOutputFile.cs`

The `Merge()` method combines two `PHPOutputFile` instances that should go into the same output file (e.g., namespace-level functions from different source files that share the same namespace, or multiple root code blocks).

- Validate namespaces match: `this.FileNameSpace` and `other.FileNameSpace` must declare the same namespace. If they do not, add a diagnostic error and return without merging.
- Merge `FileImports`: combine both import lists, removing duplicates (same fully-qualified name)
- Merge `FileDeclares`: combine declare lists, removing duplicates. Conflicting declares (e.g., different `strict_types` values) should produce a warning diagnostic.
- Merge `Statements`: append `other.Statements` to `this.Statements`
- Merge `EmitItem` trees: if both have `RootEmitItem`, add the other's root children to this root's children
- Update `IsPSR4ObjectDeclaration`: if either is false, the merged result is false

#### 6.3 Implement `Generate()`

**File:** `Tyhp/TyhpLang/Emitter/PHPOutputFile.cs`

The `Generate()` method produces the final PHP source string. It constructs the file in order:

1. **PHP open tag:** `<?php`
2. **File header comment** (optional): If `EmitConfig.IncludeComments`, add a comment like `// Generated by Tyhp compiler`
3. **Declare statements:** `declare(strict_types=1);` (from `FileDeclares` or `EmitConfig.StrictTypes`)
4. **Blank line**
5. **Namespace declaration:** `namespace App\Models;` (from `FileNameSpace`)
6. **Blank line**
7. **Use/import statements** (sorted, pruned): `use App\Services\UserService;`
8. **Blank line**
9. **Body:** The emitted statements using `RootEmitItem.emit(0)` or by iterating `Statements` and calling `emit()` on each

Implementation:

- Build a `StringBuilder` for the output
- Append `"<?php\n"` (always — Tyhp does not support short open tags in output)
- If config says to add strict_types and it is not already in `FileDeclares`, add `"declare(strict_types=1);\n"`
- Append each file-level declare statement
- Append a blank line
- If namespace exists: append `"namespace {namespaceName};\n"` (for statement syntax) or the block syntax
- Append a blank line
- For each import: format and append the `use` statement
- Append a blank line
- Append the body: call `RootEmitItem.emit(0)` if available, or concatenate all statement emit results
- Store the result in `this.GeneratedContent`
- Return the generated string

Handle edge cases:
- No namespace (global namespace): omit namespace line
- No imports after pruning: omit the blank line for imports
- No declare statements: omit declares section
- Block namespace syntax: wrap the body in `namespace Name { ... }`
- Multiple class declarations should NOT appear in the same file (this would be a splitter bug — emit a diagnostic warning)

#### 6.4 Handle PHP Close Tag

PHP files should NOT end with `?>` (PSR-12 convention). The generated output should end with a newline after the last statement.

#### 6.5 Handle Indentation and Formatting

The `EmitItem.emit(indentLevel)` method uses 4-space indentation. The `Generate()` method should:
- Start the body at indent level 0 for statement-namespace files
- Start the body at indent level 1 for block-namespace files (contents are indented inside the namespace block)
- Ensure consistent line endings (use `\n`, not `\r\n`)

### Acceptance Criteria

- [ ] `PruneFileImports()` removes imports that are not referenced in the emitted output
- [ ] `PruneFileImports()` sorts remaining imports alphabetically
- [ ] `PruneFileImports()` groups imports by type (classes, functions, constants)
- [ ] `PruneFileImports()` removes duplicate imports
- [ ] `Merge()` correctly combines two output files with the same namespace
- [ ] `Merge()` detects and warns about namespace mismatches
- [ ] `Merge()` deduplicates imports and declares
- [ ] `Generate()` produces a valid PHP file starting with `<?php`
- [ ] `Generate()` includes `declare(strict_types=1)` when configured
- [ ] `Generate()` includes namespace declaration when present
- [ ] `Generate()` includes sorted, pruned imports
- [ ] `Generate()` includes the body from the `EmitItem` tree
- [ ] Generated PHP files do not end with `?>`
- [ ] Generated PHP files use consistent 4-space indentation
- [ ] Generated PHP files use `\n` line endings
- [ ] `GeneratedContent` property is set after `Generate()` returns
- [ ] The project compiles without errors

### Dependencies

- **Requires:** Phase 3-4 (emission must produce `EmitItem` trees and populate `UsedImports`), Phase 5 (ConvertAliases must run before Generate)
- **Provides:** Complete PHP source generation — output files are ready to be written to disk

---

## Phase 7: Output File Writing and Pipeline Integration




### Phase Overview

Implement the final step of the emitter pipeline: writing generated PHP source files to disk. Wire the emitter into the `BuildAction` pipeline by replacing the `// PLACEHOLDER_STORY_09` markers from Story 01. After this phase, `tyhp build` can produce `.php` output files.

> **Ownership note (file writing):** The `OutputFileWriter` and the `BuildAction` write-wiring introduced in this phase are **minimal and temporary**. Story 09 only *produces output strings*; authoritative disk-writing of `.php`/`.map` files is owned by **Story 10's `OutputWriterService` (`Domain/Services/`)**, which **supersedes** this phase's writer. Treat the `tyhp build` produced here as a basic, end-to-end smoke path — not the final build command. Story 10 replaces this temporary writer with the full implementation (clean/dry-run modes, conflict policy, sourcemap coordination, etc.).

### Deliverables

- `Tyhp/TyhpLang/Emitter/OutputFileWriter.cs` — **Minimal/temporary** disk writing logic with directory creation, conflict detection, and summary reporting (superseded by Story 10's `OutputWriterService`)
- Modified `Tyhp/CLI/BuildAction.cs` — Replace emitter and output writing placeholders with actual invocations
- Modified `Tyhp/Domain/Services/CompilationService.cs` — Add emitter step to the pipeline (or called from `BuildAction`)
- `CompilationResult.OutputFiles` populated after emission
- `CompilationResult.EmitDuration` timing recorded

### Implementation Details

#### 7.1 Create `OutputFileWriter`

**New file:** `Tyhp/TyhpLang/Emitter/OutputFileWriter.cs`

This class handles writing generated PHP files to disk:

- Constructor: `OutputFileWriter(EmitConfig config, DiagnosticBag diagnostics)`
- Main entry: `WriteResult WriteAll(IReadOnlyList<PHPOutputFile> outputFiles)`

The `WriteAll()` method:

1. Validate the output directory exists; create it if not
2. For each `PHPOutputFile`:
   a. Compute the full output path: `config.OutputPath / outputFile.OutputFilePath`
   b. Check for path conflicts (two output files trying to write to the same path)
   c. Create intermediate directories as needed
   d. Write `outputFile.GeneratedContent` to the file using UTF-8 encoding without BOM
   e. Track the file as written
3. Return a `WriteResult` summary

**`WriteResult`** — small data class:
- `int FilesWritten { get; }`
- `int FilesSkipped { get; }` (due to errors)
- `int DirectoriesCreated { get; }`
- `List<string> WrittenPaths { get; }` — all paths that were written
- `List<(string Path, string Reason)> Conflicts { get; }` — paths with conflicts

#### 7.2 Handle File Path Conflicts

When two `PHPOutputFile` instances resolve to the same output path:

- If they were supposed to be merged (namespace-level functions from different source files), this indicates a `Merge()` step was missed. Add a diagnostic warning and merge them at this point.
- If they are genuinely conflicting (two classes with the same name in the same namespace), add a diagnostic error and skip the second one.
- Use the first-encountered file (deterministic ordering by source file path).

#### 7.3 Handle Clean Build Mode

If the build is configured with `--clean` flag (future Story 10 config):

- Before writing, delete all files in the output directory that are not about to be written
- This removes stale output from previous builds
- For now, `// PLACEHOLDER_STORY_10: Clean build mode` — just write without cleaning

#### 7.4 Handle Dry Run Mode

If the build is configured with `--dry-run` flag (future Story 10 config):

- Do not write any files to disk
- Instead, log what would have been written (file paths and sizes)
- For now, `// PLACEHOLDER_STORY_10: Dry run mode`

#### 7.5 Wire Emitter into `BuildAction` Pipeline

**File:** `Tyhp/CLI/BuildAction.cs` (from Story 01)

Replace the placeholder comments:

- `// PLACEHOLDER_STORY_09: Run emitter` becomes:
  ```
  1. Create EmitConfig from Project configuration
  2. Create EmitContext with GlobalScope, DiagnosticBag, and EmitConfig
  3. Create TyhpEmitter with EmitContext
  4. Call emitter.Emit(parsedFiles)
  5. Store result in CompilationResult.OutputFiles
  6. Record CompilationResult.EmitDuration
  7. If there are emitter errors, report and optionally abort
  ```

- `// PLACEHOLDER_STORY_09: Write output files` becomes:
  ```
  1. If CompilationResult.Success (no errors from any phase):
  2.   Create OutputFileWriter with EmitConfig and DiagnosticBag
  3.   Call writer.WriteAll(CompilationResult.OutputFiles)
  4.   Display write summary (files written, directories created)
  5. Else:
  6.   Log "Compilation failed, output not written" and skip writing
  ```

#### 7.6 Emitter Call Location

The emitter is called from `BuildAction`, not `CompilationService`. `CompilationService` handles parsing and binding; `BuildAction` orchestrates the remaining pipeline stages (check, optimize, emit, write). The emitter call is placed in `BuildAction` after the checker and optimizer steps.

`LintAction` does NOT call the emitter (lint is parse → bind → check only).

#### 7.7 Update Build Summary Display

After the emitter runs, display:

- Number of PHP files generated
- Total size of generated output (bytes)
- Emit duration
- Number of files written to disk
- Number of directories created
- Output directory path

If there were emitter warnings (e.g., unused imports that were pruned, tyhpdef aliases that were resolved), display them.

#### 7.8 Source Map Placeholder

After each PHP file is written, the writer should have a hook for writing the corresponding source map file:

- `// PLACEHOLDER_STORY_17: Write sourcemap for {outputPath}`
- The `PHPOutputFile.SourceMappings` list (from Phase 2 properties) would be used here

### Acceptance Criteria

- [ ] `OutputFileWriter.WriteAll()` creates the output directory structure
- [ ] Each `PHPOutputFile` with `GeneratedContent` is written to its `OutputFilePath`
- [ ] Files are written with UTF-8 encoding without BOM
- [ ] Intermediate directories are created automatically
- [ ] File path conflicts are detected and reported as diagnostics
- [ ] The `BuildAction` pipeline calls the emitter and writer (no more Story 09 placeholders)
- [ ] `CompilationResult.OutputFiles` is populated after emission
- [ ] `CompilationResult.EmitDuration` is recorded
- [ ] `LintAction` does NOT call the emitter (confirmed: emit step skipped)
- [ ] Running `tyhp build` on a project with valid PHP/Tyhp source produces `.php` output files
- [ ] Build summary displays file count, sizes, timing, and output path
- [ ] Source map placeholder exists for Story 17
- [ ] The project compiles without errors

### Dependencies

- **Requires:** Phase 6 (Generate() — output files must have `GeneratedContent`), Story 01 (BuildAction skeleton, CompilationService, CompilationResult)
- **Provides:** Complete emitter pipeline plus a **minimal/temporary** disk writer giving a basic end-to-end `tyhp build` smoke path. The authoritative build command and file-writing service arrive in Story 10 (`OutputWriterService`), which supersedes this phase's writer.

---

## Phase 8: End-to-End Validation and Emitter MessageCodes




### Phase Overview

Validate the complete emitter pipeline end-to-end by running it against the existing example files. Add all emitter-specific `MessageCode` values, add corresponding resource strings for localization, and fix any issues discovered during validation. This phase ensures the emitter is robust and produces correct output for the supported language features.

### Deliverables

- New `MessageCode` values in `Tyhp/Domain/Exceptions/MessageCode.cs` for emitter errors (5000s range)
- Corresponding resource strings in the `.resx` file from Story 01
- End-to-end validation results for all `Examples/*.php` and `Examples/*.tyhp` files
- Fixes for any bugs discovered during validation
- Documentation of known limitations and `PLACEHOLDER_STORY_11` references

### Implementation Details

#### 8.1 Add Emitter-Specific MessageCode Values

**File:** `Tyhp/Domain/Exceptions/MessageCode.cs`

Add codes in the 5000s range (reserved for emitter). **`5001 EmitterUnknownError` already exists** in `Tyhp/Domain/Exceptions/MessageCode.cs` — do NOT redefine it. Story 09's **new** emitter codes are **5002–5011**; `5001` is listed below only for context (keep as-is):

| Code | Name | Description |
|------|------|-------------|
| 5001 | `EmitterUnknownError` | **Already exists — do not redefine.** Fallback for unexpected emitter errors |
| 5002 | `EmitterUnsupportedAstNode` | AST node type has no emission implementation |
| 5003 | `EmitterOutputPathConflict` | Two output files resolve to the same path |
| 5004 | `EmitterNamespaceMismatch` | Merge attempted on files with different namespaces |
| 5005 | `EmitterInvalidOutputPath` | Computed output path is invalid or inaccessible |
| 5006 | `EmitterTypeErasureWarning` | A Tyhp type was erased to `mixed` (informational) |
| 5007 | `EmitterWriteError` | Failed to write output file to disk |
| 5008 | `EmitterTyhpConstructNotImplemented` | A Tyhp-specific construct emission is not yet implemented |
| 5009 | `EmitterInvalidDeclareDirective` | Invalid or conflicting declare directive |
| 5010 | `EmitterEmptyOutputFile` | An output file has no statements (warning) |
| 5011 | `EmitterMergeConflict` | Conflicting declarations during file merge |

#### 8.2 Add Resource Strings for Emitter Codes

**File:** `Tyhp/Resources/CLI.TyhpHostedService.resx` (or equivalent from Story 01)

Add entries (the `ERROR_TYHP5001` entry may already exist alongside the pre-existing `EmitterUnknownError` code — add it only if missing; the new strings are `5002`–`5011`):
- `ERROR_TYHP5001` = `"Unknown emitter error: {0}"` (only if not already present)
- `ERROR_TYHP5002` = `"Cannot emit AST node type '{0}' — no emission handler implemented"`
- `ERROR_TYHP5003` = `"Output path conflict: '{0}' is targeted by multiple declarations"`
- `ERROR_TYHP5004` = `"Cannot merge output files: namespace mismatch ('{0}' vs '{1}')"`
- `ERROR_TYHP5005` = `"Invalid output path: '{0}' — {1}"`
- `WARNING_TYHP5006` = `"Type '{0}' was erased to 'mixed' in PHP output"`
- `ERROR_TYHP5007` = `"Failed to write output file '{0}': {1}"`
- `WARNING_TYHP5008` = `"Tyhp construct '{0}' is not yet supported by the emitter"`
- `ERROR_TYHP5009` = `"Invalid declare directive: {0}"`
- `WARNING_TYHP5010` = `"Output file '{0}' has no statements and will not be written"`
- `WARNING_TYHP5011` = `"Conflicting declarations during merge of '{0}': {1}"`

#### 8.3 Validate Against Example PHP Files

Run the full pipeline (parse → bind → check → emit) on all `Examples/*.php` files:

The PHP examples should produce PHP output that is essentially identical to the input (since they are already valid PHP). The emitter is doing a round-trip: parse PHP → emit PHP.

Files to validate:
- `Examples/OperatorOverloads.php` — basic PHP with class definitions
- `Examples/PropertyAccessors.php` — PHP with property declarations

For each file:
1. Parse the file
2. Run the binder (may produce warnings for unresolved symbols — OK for now)
3. Run the checker (may produce warnings — OK for now)
4. Run the emitter
5. Compare the output to the input: the structure should be equivalent (whitespace/formatting differences are expected)
6. Verify the output is valid PHP (parseable by the PHP parser or at minimum by the Tyhp parser)

#### 8.4 Validate Against Example Tyhp Files

Run the pipeline on `Examples/*.tyhp` files. These exercise Tyhp-specific features:

- `Examples/OperatorOverloads.tyhp` — operator overloads on classes
- `Examples/PropertyAccessors.tyhp` — Tyhp property accessor syntax
- `Examples/TypeGuards.tyhp` — type guard functions
- `Examples/WithKeyword.tyhp` — `with` keyword for structs and objects

For Tyhp features that are `PLACEHOLDER_STORY_11`, expect:
- The emitter should produce output for the PHP-compatible parts
- Tyhp-specific constructs should produce diagnostic warnings (`EmitterTyhpConstructNotImplemented`)
- The emitter should NOT crash on any input

Expected Tyhp constructs that are basic-emittable in this story:
- Class/interface/trait/enum declarations (with generics stripped)
- Function/method declarations (with generics and Tyhp-only types stripped)
- All PHP-compatible statements and expressions
- Import/use statements (Tyhp `import` syntax → PHP `use`)
- Type guard return types (`$param is Type` → `bool`)
- `nameof()` → string literal
- `default()` → literal value for basic types

Expected Tyhp constructs that need STORY 11:
- Structs → arrays (declaration erasure, construction → array literal, property access → array access)
- Extensions → static method rewriting (full chaining, scalar types)
- Operator overloads → method call rewriting (user-defined)
- Async/await → Promise/Fiber code
- Disposable → scope-based auto-dispose via `DisposableScope`
- `with` keyword → property assignment chain
- Generics → `GenericObject` trait, hidden `$__generic_*` constructor parameters, `NamedType` wrapping, `tyhpGenericObjectInit()`, `tyhpGenericObjectSetPropertyType()`, and call-site `__generic_*` named arguments (only when the checker flags the class as `RequiresRuntimeGenericTracking` — e.g., when `typeof(T)` is used; see Story 11, Phase 8)
- Function overloads → single function with dispatch
- ~~Emitter templates (`@tyhpEmitterStart`/`@tyhpEmitterEnd`)~~ — removed from the design

#### 8.5 Fix Discovered Issues

As validation proceeds, fix any bugs found:
- Incorrect indentation or formatting
- Missing semicolons
- Incorrect operator precedence in emitted expressions
- Missing parentheses in complex expressions
- Incorrect handling of special characters in string literals
- Edge cases in heredoc/nowdoc emission
- Edge cases in anonymous class emission
- Incorrect namespace handling for global-namespace code

#### 8.6 Create a Known Limitations Document

At the end of this phase, update the emitter `readme.md` or create a section documenting:
- Which Tyhp features are fully emittable after this story
- Which features need Story 11 (with specific `PLACEHOLDER_STORY_11` references)
- Which edge cases are known to not work yet
- ~~The emitter template system (`@tyhpEmitterStart`/`@tyhpEmitterEnd`)~~ — the emitter template system has been removed from the design

### Acceptance Criteria

- [ ] The **new** emitter `MessageCode` values (**5002–5011**) are added to `MessageCode.cs` (`5001 EmitterUnknownError` already exists and is NOT redefined)
- [ ] Corresponding resource strings are added to the `.resx` file
- [ ] All `Examples/*.php` files can be round-tripped through the emitter (parse → emit) producing structurally equivalent PHP
- [ ] All `Examples/*.tyhp` files can be processed without the emitter crashing
- [ ] Tyhp constructs that are basic-emittable produce correct PHP output
- [ ] Tyhp constructs that need Story 11 produce informational diagnostics (not crashes)
- [ ] No emitter errors are produced for standard PHP constructs
- [ ] The complete `tyhp build` command works end-to-end: discovers files → parses → binds → checks → emits → writes output
- [ ] Build summary accurately reports file counts, timing, and diagnostics
- [ ] Known limitations are documented
- [ ] All `PLACEHOLDER_STORY_11` references are in place for future emitter expansion
- [ ] The project compiles without errors

### Dependencies

- **Requires:** All previous phases (1-7) complete
- **Provides:** A validated, production-ready basic emitter; documented known limitations; foundation for Story 11 feature expansion

---

## Cross-Cutting Concerns

### Placeholder Convention

This plan uses two placeholder formats:

**Within this implementation plan** — for future phases of this same plan:
```csharp
// PLACEHOLDER_PHASE_N: description of what goes here
```
When starting Phase N of this plan, search for `PLACEHOLDER_PHASE_N` and implement each placeholder.

**Cross-story references** — for work that belongs to a different TODO.md story:
```csharp
// PLACEHOLDER_STORY_N: description of what goes here
```
When starting Story N from `TODO.md`, search for `PLACEHOLDER_STORY_N` across all implementation plans and implement each placeholder.

### Story 11 Placeholders Summary

The following `PLACEHOLDER_STORY_11` markers should be placed in the emitter code during this story's implementation:

| Location | Placeholder Description |
|----------|----------------------|
| `TyhpEmitter.Declarations.cs` | Property hooks for PHP < 8.4 |
| `TyhpEmitter.Declarations.cs` | Async function body transformation |
| `TyhpEmitter.Expressions.cs` | Full `nameof()` implementation with all symbol types |
| `TyhpEmitter.Expressions.cs` | `typeof()` implementation |
| `TyhpEmitter.Expressions.cs` | `variable_exists()` implementation |
| `TyhpEmitter.Expressions.cs` | Generic `default()` with arbitrary types |
| `AliasConverter.cs` | Full extension method rewriting (chained, nullable, scalar) |
| `AliasConverter.cs` | User-defined operator overload rewriting |
| `AliasConverter.cs` | Full struct emission (construction, `with`, clone) |
| `AliasConverter.cs` | Property accessor rewriting for PHP < 8.4 |
| `AliasConverter.cs` | Disposable → scope-based auto-dispose |
| `AliasConverter.cs` | Async/await → Promise/Fiber transformation |
| `AliasConverter.cs` | Function overload → single function dispatch |
| `TyhpEmitter.cs` | ~~Emitter template system (`@tyhpEmitterStart`/`@tyhpEmitterEnd`)~~ — removed from the design |
| `PHPOutputFileSplitter.cs` | Struct declarations — erased but may need runtime validation |

### Error Recovery in the Emitter

The emitter should never crash on invalid or unexpected AST input:

1. If an AST node type has no emission handler, add a `MessageCode.EmitterUnsupportedAstNode` diagnostic and emit a PHP comment: `/* TYHP: unsupported construct */`
2. If an `ErrorAst` node is encountered (from the visitor's error recovery), skip it and add no output
3. If type resolution fails during emission (symbol not found), emit `mixed` as the type and add a `MessageCode.EmitterTypeErasureWarning` diagnostic
4. If an expression cannot be emitted, emit a placeholder expression (`null`) and add a diagnostic

### File Size Guidelines

| File | Target Maximum | Notes |
|------|---------------|-------|
| `TyhpEmitter.cs` | 300 lines | Main class, dispatch, pipeline orchestration |
| `TyhpEmitter.Declarations.cs` | 500 lines | May approach limit — split further if needed |
| `TyhpEmitter.Statements.cs` | 500 lines | May approach limit — split further if needed |
| `TyhpEmitter.Expressions.cs` | 500 lines | May approach limit — split further if needed |
| `TyhpEmitter.Types.cs` | 200 lines | Focused on type expression handling |
| `PHPOutputFile.cs` | 400 lines | Existing file, methods filled in |
| `PHPOutputFileSplitter.cs` | 350 lines | Focused splitting logic |
| `AliasConverter.cs` | 300 lines | Many placeholders for Story 11 |
| `OutputFileWriter.cs` | 200 lines | Straightforward disk I/O |
| `EmitContext.cs` | 100 lines | Data carrier |
| `OutputPathResolver.cs` | 150 lines | Path computation |

If any file exceeds 500 lines, split it into smaller focused modules (e.g., `TyhpEmitter.Statements.ControlFlow.cs`, `TyhpEmitter.Statements.Loops.cs`).

---

## Appendix A: AST Node Types the Emitter Must Handle

The emitter dispatches on concrete AST types. This is the complete catalog organized by emission category.

### Declarations (Phase 3)

| AST Node | PHP Output |
|----------|-----------|
| `PhpNamespaceDeclAst` | `namespace App\Models;` |
| `PhpBlockNamespaceDeclAst` | `namespace App\Models { ... }` |
| `PhpImportDeclListAst` | `use App\Models\User;` (group) |
| `PhpImportDeclAst` | `use App\Models\User as U;` (single) |
| `PhpObjectTypeDeclAst` | `class/interface/trait/enum Name { ... }` |
| `TyhpStructDeclAst` | Erased (PLACEHOLDER_STORY_11 for array construction) |
| `TyhpExtensionDeclAst` | `class ExtensionName { ... }` |
| `PhpFunctionDeclAst` | `function name(...) { ... }` |
| `PhpMethodDeclAst` | `public function name(...) { ... }` |
| `PhpPropertyDeclAst` | `public Type $name = default;` |
| `PhpPropertyAst` | Property definition |
| `PhpPropertyListAst` | Multiple property definitions |
| `PhpPropertyHookAst` | `get { ... }` / `set { ... }` (PHP 8.4) |
| `PhpConstDeclAst` | `const NAME = value;` |
| `PhpConstDeclListAst` | Multiple constant definitions |
| `PhpEnumCaseAst` | `case Name = value;` |
| `PhpTraitUseAst` | `use TraitName;` |
| `PhpTraitAdaptationListAst` | `{ ... }` trait adaptations block |
| `PhpTraitAliasAst` | `TraitA::method as alias;` |
| `PhpTraitPrecedenceAst` | `TraitA::method insteadof TraitB;` |
| `PhpParameterAst` | `Type $name = default` (function parameter) |
| `PhpModifierListAst` | `public static final` modifiers |
| `PhpAttributeAst` | `#[Attribute]` |
| `PhpDeclareAst` | `declare(strict_types=1);` |
| `TyhpTypeAliasAst` | Erased (compile-time only) |
| `TyhpOperatorOverloadAst` | Regular method declaration |
| `TyhpImportExtensionAst` | `use` statement for extension class |

### Statements (Phase 4)

| AST Node | PHP Output |
|----------|-----------|
| `PhpIfAst` | `if (...) { ... } elseif (...) { ... } else { ... }` |
| `PhpLoopAst` | `for/foreach/while/do-while (...) { ... }` |
| `PhpConditionalAst` | `switch/match (...) { ... }` |
| `PhpConditionalArmAst` | `case value:` / `value => result` |
| `PhpTryCatchAst` | `try { ... } catch (...) { ... } finally { ... }` |
| `PhpCatchClauseAst` | `catch (Type $e) { ... }` |
| `PhpJumpStatementAst` | `break;` / `continue;` |
| `PhpReturnStatementAst` | `return expr;` |
| `PhpGotoStatementAst` | `goto label;` |
| `PhpLabelStatementAst` | `label:` |
| `PhpEchoStatementAst` | `echo expr;` |
| `PhpUnsetStatementAst` | `unset($var);` |
| `PhpGlobalStatementAst` | `global $var;` |
| `PhpStaticStatementAst` | `static $var = val;` |
| `PhpHaltCompilerAst` | `__halt_compiler();` |
| `PhpNopStatementAst` | `;` (empty statement) |
| `PhpEmptyStatementAst` | `empty($expr)` |
| `PhpInlineOutputAst` | Inline HTML content |

### Expressions (Phase 4)

| AST Node | PHP Output |
|----------|-----------|
| `PhpBinaryOpAst` | `left op right` |
| `PhpUnaryOpAst` | `op expr` / `expr op` |
| `PhpCallAst` | `func($args)` / `$obj->method($args)` |
| `PhpArgumentListAst` | `($arg1, $arg2)` |
| `PhpArgumentAst` | `$value` / `name: $value` |
| `PhpInstanceMemberAccessAst` | `$obj->member` |
| `PhpStaticMemberAccessAst` | `Class::$member` |
| `PhpClassConstantAccessAst` | `Class::CONST` |
| `PhpArrayAccessAst` | `$array[$key]` |
| `PhpMemberAccessAst` | Member access patterns |
| `PhpScalarAst` | `42`, `3.14`, `'string'`, `true`, `null` |
| `PhpStringAst` | `'string'` / `"string"` |
| `PhpEncapsListAst` | `"hello $name"` interpolation |
| `PhpEncapsStringAst` | String part within interpolation |
| `PhpMagicConstantAst` | `__FILE__`, `__LINE__`, etc. |
| `PhpArrayAst` | `[1, 2, 3]` / `array(1, 2, 3)` |
| `PhpArrayPairAst` | `$key => $value` |
| `PhpArrayPairListAst` | Array pair list |
| `PhpVariableAst` | `$varName` |
| `PhpVariableListAst` | `[$a, $b] = ...` |
| `PhpNewAst` | `new ClassName($args)` |
| `PhpInlineFunctionAst` | `function() { ... }` / `fn() => expr` |
| `PhpIssetStatementAst` | `isset($var)` |
| `PhpYieldAst` | `yield $value` |
| `PhpNameAst` | Name reference |
| `PhpDereferenceableAst` | Dereferenceable expression |
| `PhpDereferenceableExpressionAst` | Complex dereference chain |
| `PhpExpressionListAst` | Comma-separated expressions |
| `PhpEvalStatementAst` | `eval($code)` |

### Tyhp-Specific (Phase 4 basic + Phase 5 transformations)

| AST Node | PHP Output |
|----------|-----------|
| `TyhpNameofAst` | `'symbolName'` (string literal) |
| `TyhpDefaultAst` | `0`, `''`, `false`, `null`, `[]` (literal) |
| `TyhpTypeofAst` | PLACEHOLDER_STORY_11 |
| `TyhpVariableExistsAst` | PLACEHOLDER_STORY_11 |
| `TyhpTypedVarExprAst` | Strip type, emit just the variable |
| `TyhpReturnTypeGuardAst` | `bool` (type guard return → bool) |
| `TyhpGenericsTypeArgumentAst` | Erased (stripped from output) |
| `TyhpGenericIdentifierAst` | Emit base name without generic args |
| `TyhpGenericsTypeArgumentListAst` | Erased |
| `TyhpCtorReturnTypeAst` | Erased (constructors have no return type in PHP) |
| `TyhpStructPropertyAst` | PLACEHOLDER_STORY_11 |
| `TyhpStructPropertyListAst` | PLACEHOLDER_STORY_11 |

### Type Expressions (Phase 3)

| AST Node | PHP Output |
|----------|-----------|
| `PhpTypeExpressionAst` | PHP type hint |
| `PhpTypeExpressionListAst` | Union/intersection type |
| `PhpNamedTypeAst` | `ClassName` |
| `PhpBuiltinTypeAst` | `int`, `string`, `float`, `bool`, etc. |

---

## Appendix B: EmitType Ordering Reference

The existing `EmitType` enum defines the ordering within PHP output files. The emitter must assign correct `EmitType` values to ensure conventional PHP file structure:

| EmitType Value | Used For | Phase |
|---------------|----------|-------|
| `OutsideItems` (0) | PHP blocks, inline HTML | 4 |
| `TyhpBlock` (1) | Tyhp-specific blocks | 4 |
| `FileHeader` (2) | File header comments | 3 |
| `FileDeclare` (3) | `declare(strict_types=1)` | 3 |
| `FileNamespaceDeclaration` (4) | `namespace App\Models;` | 3 |
| `BlockNamespaceDeclaration` (5) | `namespace App\Models { }` | 3 |
| `ImportUse` (6) | `use` statements | 3 |
| `RootStatement` (7) | Top-level statements, class/function decls | 3 |
| `ObjectDeclaration` (8) | Class body container | 3 |
| `ObjectTraitUse` (9) | `use TraitName;` inside class | 3 |
| `ObjectConstantDeclaration` (10) | Class constants | 3 |
| `ObjectStaticPropertyDeclaration` (11) | Static properties | 3 |
| `ObjectInstancePropertyDeclaration` (12) | Instance properties | 3 |
| `ObjectConstructor` (13) | `__construct()` | 3 |
| `ObjectDestructor` (14) | `__destruct()` | 3 |
| `ObjectStaticMethods` (15) | Static methods | 3 |
| `ObjectInstanceMethods` (16) | Instance methods | 3 |
| `FunctionGlobalReference` (17) | `global $var;` in function | 4 |
| `FunctionStatement` (18) | Statements inside function body | 4 |
| `BlockDeclare` (19) | `declare() { }` block | 3 |
| `SubBlockStatement` (20) | Statements inside sub-blocks | 4 |
| `OutputFileBlock` / `OutputFileStatement` (21) | `declare(output_file="")` | 3 |
| `Empty` (MaxInt-1) | Empty placeholder | - |
| `Group` (MaxInt) | Grouping container | - |

---

*Generated: 2026-02-16 | Source: TODO.md Story 09 | Branch: Not Specified*

---

## Human Testing and Verification

> **Note:** These steps are meant to help a human developer manually verify the emitter implementation. Steps can be skipped, reordered, or adapted based on what has already been tested or what is most relevant. The goal is to confirm the emitter produces valid, correct PHP output from Tyhp source.

### Step 1: Verify the Emitter Compiles

Run the project build to confirm all emitter code compiles without errors:

```bash
dotnet build
```

Confirm there are no build errors in the `Tyhp/TyhpLang/Emitter/` directory.

### Step 2: Verify PHP Pass-Through (Simple Class)

Create a test file `test_emit_class.tyhp`:

```tyhp
<?tyhp

namespace App\Models;

use App\Services\Logger;

class User {
    public const string ROLE_ADMIN = 'admin';

    private static int $instanceCount = 0;

    public function __construct(
        private string $name,
        private string $email,
        private int $age = 0
    ) {
        self::$instanceCount++;
    }

    public function getName(): string {
        return $this->name;
    }

    public static function getInstanceCount(): int {
        return self::$instanceCount;
    }
}
```

Run:

```bash
tyhp build
```

**Expected output file** at `build/src/Models/User.php` (or the configured output path). Verify:

1. File starts with `<?php`
2. `declare(strict_types=1);` is present
3. `namespace App\Models;` is present
4. The class body contains the constructor, methods, constant, and static property
5. Constructor promotion parameters are preserved
6. The output is valid PHP — run `php -l build/src/Models/User.php` to syntax-check it

### Step 3: Verify Interface and Enum Emission

Create a test file `test_emit_interface_enum.tyhp`:

```tyhp
<?tyhp

namespace App\Contracts;

interface Renderable {
    public function render(): string;
}

enum Color: string {
    case Red = 'red';
    case Green = 'green';
    case Blue = 'blue';

    public function label(): string {
        return \ucfirst($this->value);
    }
}
```

Run:

```bash
tyhp build
```

**Expected:** Two output files — one for `Renderable`, one for `Color`. Verify:

- Interface file has `interface Renderable` with the method signature
- Enum file has `enum Color: string` with cases and the method
- Both files pass `php -l` syntax check

### Step 4: Verify Generic Type Erasure

Create a test file `test_emit_generics.tyhp`:

```tyhp
<?tyhp

namespace App\Collections;

class TypedList<T> {
    /** @var array<T> */
    private array $items = [];

    public function add(T $item): void {
        $this->items[] = $item;
    }

    public function get(int $index): ?T {
        return $this->items[$index] ?? null;
    }
}
```

Run:

```bash
tyhp build
```

Inspect the output PHP file. **Expected:**

- Class declaration is `class TypedList` (no `<T>`)
- Parameter type `T $item` becomes `mixed $item` (or the constraint type)
- Return type `?T` becomes `?mixed` or `mixed`
- The file passes `php -l` syntax check

### Step 5: Verify Statement Emission (Control Flow)

Create a test file `test_emit_statements.tyhp`:

```tyhp
<?tyhp

function processItems(array $items): void {
    foreach ($items as string $key => mixed $value) {
        if ($value === null) {
            continue;
        }

        switch (true) {
            case $value instanceof \Stringable:
                echo (string) $value;
                break;
            default:
                echo \var_export($value, true);
                break;
        }
    }
}

function tryCatchExample(): ?string {
    try {
        return \file_get_contents('/tmp/data.txt');
    } catch (\Throwable $e) {
        echo "Error: " . $e->getMessage();
        return null;
    } finally {
        echo "Done.";
    }
}
```

Run:

```bash
tyhp build
```

Inspect the output. **Expected:**

- `foreach`, `if`, `switch`, `try/catch/finally` all emit valid PHP
- All control flow structures have braces
- The file passes `php -l` syntax check

### Step 6: Verify Expression Emission (Binary, Unary, Calls)

Create a test file `test_emit_expressions.tyhp`:

```tyhp
<?tyhp

function testExpressions(): void {
    int $a = 10;
    int $b = 20;

    int $sum = $a + $b;
    float $div = $a / $b;
    string $greeting = "Hello" . " " . "World";
    bool $check = ($a > 5) && ($b < 30);

    int $result = $a > $b ? $a : $b;
    ?string $name = null;
    string $displayName = $name ?? "Anonymous";

    int $negated = -$a;
    bool $notTrue = !true;
    $a++;

    array $items = [1, 2, ...[3, 4]];
    mixed $first = $items[0];

    $fn = fn(int $x): int => $x * 2;
    int $doubled = $fn(5);
}
```

Run:

```bash
tyhp build
```

**Expected:** All expressions emit valid PHP. The file passes `php -l`.

### Step 7: Verify PSR-4 File Splitting

Create a test file `test_emit_splitting.tyhp` with multiple declarations:

```tyhp
<?tyhp

namespace App\Shapes;

class Circle {
    public function __construct(private float $radius) {}
    public function area(): float {
        return M_PI * $this->radius ** 2;
    }
}

class Rectangle {
    public function __construct(
        private float $width,
        private float $height
    ) {}
    public function area(): float {
        return $this->width * $this->height;
    }
}

function describeShape(string $name): string {
    return "This is a {$name}";
}
```

Run:

```bash
tyhp build
```

**Expected:**

- `build/src/Shapes/Circle.php` — contains only the `Circle` class
- `build/src/Shapes/Rectangle.php` — contains only the `Rectangle` class
- `build/src/Shapes/_functions.php` — contains the `describeShape` function
- Each file has the correct namespace declaration
- Each file has `declare(strict_types=1);`
- Each file has appropriate `use` imports (unused imports pruned)

### Step 8: Verify Tyhp Compile-Time Constructs

Create a test file `test_emit_tyhp_constructs.tyhp`:

```tyhp
<?tyhp

class Example {
    public string $name;

    public function demo(): void {
        string $varName = nameof($this->name);  // Should emit 'name'
        int $zero = default(int);                // Should emit 0
        string $empty = default(string);         // Should emit ''
        bool $falsy = default(bool);             // Should emit false
        array $arr = default(array);             // Should emit []
    }
}
```

Run:

```bash
tyhp build
```

Inspect the output. **Expected:**

- `nameof($this->name)` → emits the string literal `'name'`
- `default(int)` → emits `0`
- `default(string)` → emits `''`
- `default(bool)` → emits `false`
- `default(array)` → emits `[]`

### Step 9: Verify Import Pruning

Create a test file `test_emit_pruning.tyhp`:

```tyhp
<?tyhp

namespace App\Demo;

use App\Models\User;
use App\Models\Admin;
use App\Services\Logger;

class Demo {
    public function run(): User {
        return new User("test", "test@example.com");
    }
    // Admin and Logger are NOT used
}
```

Run:

```bash
tyhp build
```

Inspect the output file. **Expected:**

- `use App\Models\User;` is present (it's used)
- `use App\Models\Admin;` is removed (unused)
- `use App\Services\Logger;` is removed (unused)

### Step 10: Verify Entry Point File Emission

Create a test file `test_emit_entrypoint.tyhp`:

```tyhp
<?tyhp

echo "Hello, World!\n";

int $result = 2 + 3;
echo "2 + 3 = {$result}\n";
```

Run:

```bash
tyhp build
```

**Expected:**

- An entry point PHP file is generated (mirroring the source path, e.g., `build/test_emit_entrypoint.php`)
- The file contains root-level PHP statements (not wrapped in a class)
- If `EntryPointAutoloader` is configured, a `require_once` for the autoloader is prepended
- The file passes `php -l` and can be executed with `php build/test_emit_entrypoint.php` to print the expected output

### Step 11: Verify Extension Method Rewriting

Create a test file `test_emit_extension.tyhp`:

```tyhp
<?tyhp

namespace App\Extensions;

extension StringExtensions for string {
    public function shout(): string {
        return \strtoupper($this) . "!";
    }
}

function demo(): void {
    string $greeting = "hello";
    echo $greeting->shout();  // Should be rewritten to static call
}
```

Run:

```bash
tyhp build
```

Inspect the output. **Expected:**

- `StringExtensions` is emitted as a regular PHP class with a static method
- The call `$greeting->shout()` is rewritten to `StringExtensions::shout($greeting)`
- The file passes `php -l`

### Step 12: Verify Output File Formatting

For any of the output files generated above, verify:

1. File starts with `<?php` (no short open tag)
2. File does NOT end with `?>` (PSR-12 convention)
3. File ends with a trailing newline
4. Indentation uses 4 spaces (not tabs)
5. Line endings are `\n` (not `\r\n`)
6. `declare(strict_types=1);` appears right after `<?php` when enabled

### Step 13: Verify Round-Trip PHP Files

Parse existing PHP example files through the emitter and verify the output is structurally equivalent:

```bash
tyhp build --file Examples/OperatorOverloads.php
tyhp build --file Examples/PropertyAccessors.php
```

**Expected:** The output PHP is structurally equivalent to the input (whitespace and formatting may differ, but the code structure, class members, and statements are preserved). Run `php -l` on each output file to confirm validity.

### Step 14: Verify the Emitter Does Not Crash on Any Input

Run the emitter on all available example files and verify no unhandled exceptions:

```bash
tyhp build
```

**Expected:** The emitter should never throw an unhandled exception. Unknown or unsupported AST nodes should produce diagnostic warnings (`TYHP5002` or `TYHP5008`) and emit a PHP comment placeholder, not crash the build process.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
