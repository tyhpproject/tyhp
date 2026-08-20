# Implementation Plan: Story 05 — Bind Symbols to AST Nodes

> **Roadmap position:** Story 05 — **Tier 0 — Spine**
> **Direct dependencies (new numbering):** 02, 03, 04
> **Renumbered from:** legacy Story 1.6
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** Decision from Plan 6/7/8 ambiguity review
> **Branch:** TBD
> **Generated:** 2026-03-23
> **Prerequisites:** Stories 02, 03, 04 must be complete (binder, symbol tree, name resolution, tyhpdef loading, runtime packages)
> **Status:** COMPLETED (2026-07-31 audit) — `IBase2Ast.BoundSymbol` / `Base2Ast.BoundSymbol` populated during binding; used by checker and emitter.

---

## Architecture Overview

### Problem

The Tyhp binder creates symbols and resolves name references, but the results are not easily accessible to downstream compilation phases (checker, emitter). Currently:

| Direction | Mechanism | Issue |
|-----------|-----------|-------|
| Symbol → AST (declarations) | `BaseSymbol.DeclaringAstNode` | Works, but one-directional |
| AST → Symbol (references) | `NameResolver.ResolvedSymbols` dictionary | **Ephemeral** — lost after `CompilationService.BindParsedFiles()` returns |
| AST → Symbol (declarations) | None | **Missing** — AST nodes don't know what symbol they declare |

The `NameResolver` maintains a `Dictionary<IBase2Ast, IBaseSymbol>` mapping reference AST nodes to their resolved symbols. However, this dictionary lives on the `TyhpBinder` instance, which is created inside `CompilationService.BindParsedFiles()` and not stored on `CompilationResult`. After `BindParsedFiles()` returns, the mapping is lost.

The checker (Story 08) and emitter (Story 11) both need to look up symbols for AST nodes. Without this mapping, they cannot determine whether a method call is an extension method, whether a type reference is a generic parameter, what symbol a variable references, etc.

### Solution

Add a `BoundSymbol` property directly to the `IBase2Ast` interface. The binder sets this property during both the declaration pass and the resolution pass. Downstream consumers simply read `astNode.BoundSymbol` to get the associated symbol.

This eliminates the need for:
- Storing `NameResolver` on `CompilationResult`
- Passing external dictionaries through the pipeline
- Any separate AST-to-symbol lookup mechanism

### Phase Responsibilities After This Change

| Phase | BoundSymbol Access |
|-------|--------------------|
| Parser | Creates AST nodes with `BoundSymbol = null` |
| Binder | **Sets** `BoundSymbol` on declaration and reference nodes (only writer) |
| Checker | **Reads** `BoundSymbol` to access symbol info; uses its own side dictionaries for inferred types |
| Emitter | **Reads** `BoundSymbol` to determine transformation strategies |

### Key Design Decisions

1. **`BoundSymbol` is nullable.** Not all AST nodes have associated symbols (literals, operators, punctuation). Downstream consumers must null-check.

2. **The binder is the only writer.** After binding completes, `BoundSymbol` is effectively read-only for all subsequent phases. The checker and emitter never modify it.

3. **Dual meaning by context.** For declaration AST nodes, `BoundSymbol` is the symbol this node declares. For reference AST nodes, `BoundSymbol` is the symbol this reference resolves to. An AST node is contextually one or the other, not both.

4. **`NameResolver` continues to exist internally.** The binder's `NameResolver` still uses its internal dictionary during the resolution pass for bookkeeping. After resolution completes, the results are also written onto AST nodes. The `NameResolver` does not need to be exposed on `CompilationResult`.

5. **`BaseSymbol.DeclaringAstNode` is kept.** The existing symbol-to-AST direction (`BaseSymbol.DeclaringAstNode`) is preserved. It provides a convenient reverse lookup when you have a symbol and need its declaring AST node.

### OwningFile Property on IBase2Ast

In addition to `BoundSymbol`, add an `OwningFile` property to `IBase2Ast`:

- **Property:** `SrcFileAst? OwningFile { get; set; }`
- **Set by:** The binder, during the bind pass. `TyhpBinder` calls `SetOwningFileRecursive(srcFile, srcFile)`, which walks each `SrcFileAst` and all of its descendants, assigning `OwningFile` on every node. The visitor does **not** set it at node construction.
- **Purpose:** Enables downstream phases (checker, emitter, optimizer, sourcemap generator) to navigate from any AST node to its owning source file without requiring a parent-chain traversal or external lookup dictionary.
- **Default implementation:** Add to `Base2Ast` as an auto-property with default `null`. The binder populates it via the recursive walk at the start of the bind pass (after parsing).
- **Serialization:** Excluded (like `BoundSymbol`) — `[JsonIgnore]` or equivalent.
- **Consumers:** Story 11 (emitter needs file context), Story 17 (sourcemap collector needs source file name), Story 24 (optimizer may need file context).

### Namespace and File Organization

```
Tyhp/TyhpLang/Ast/Interfaces/
└── IBase2Ast.cs                    (modify: add BoundSymbol property)

Tyhp/TyhpLang/Binder/
├── TyhpBinder.cs                   (modify: set BoundSymbol during declaration pass)
├── TyhpBinder.TopStatements.cs     (modify: set BoundSymbol during declaration pass)
├── TyhpBinder.ObjectBody.cs        (modify: set BoundSymbol during declaration pass)
├── TyhpBinder.CodeBlocks.cs        (modify: set BoundSymbol during declaration pass)
├── TyhpBinder.Extensions.cs        (modify: set BoundSymbol during declaration pass)
├── TyhpBinder.Tyhpdef.cs           (modify: set BoundSymbol during declaration pass)
├── TyhpBinder.Resolution.cs        (modify: set BoundSymbol during resolution pass)
└── Resolution/
    └── NameResolver.cs             (modify: set BoundSymbol in RecordResolution)
```

### Safety Guidance

- **Before modifying `IBase2Ast.cs`**, create a timestamped backup
- **Before modifying binder files**, create backups
- **Never use destructive git commands** (`git reset`, `git checkout .`, `git clean`)
- **Incremental edits preferred** over wholesale file replacement
- **Backup files are sacred** — never delete or modify them

---

## Phase 1: Add BoundSymbol to AST Infrastructure




### Phase Overview

Add the `BoundSymbol` property to the `IBase2Ast` interface and implement it on all AST classes. This is a mechanical change — add the property with a default `null` value to every class implementing `IBase2Ast`.

### Deliverables

- Modified `Tyhp/TyhpLang/Ast/Interfaces/IBase2Ast.cs` — add `BoundSymbol` and `OwningFile` properties to interface
- Modified AST base class (if one exists) or all AST implementation classes — add `BoundSymbol` property implementation

### Implementation Details

**`IBase2Ast.cs` modification:**

Add the following property to the `IBase2Ast` interface:

```csharp
/// <summary>
/// The symbol bound to this AST node by the binder.
/// For declaration nodes: the symbol this node declares.
/// For reference nodes: the symbol this reference resolves to.
/// Null if no symbol is associated (e.g., literals, operators).
/// Set only by the binder; read-only for all subsequent phases.
/// </summary>
IBaseSymbol? BoundSymbol { get; set; }
```

This requires adding a `using` for `Tyhp.TyhpLang.Binder.Symbols.Interfaces`.

**AST class implementation:**

Search for all classes that implement `IBase2Ast` (directly or through derived interfaces like `IExpression`, `IStatement`, `ITopStatement`). There are two approaches depending on the codebase structure:

- **If there is a common base class** that all/most AST nodes extend, add the property there once:
  ```csharp
  public IBaseSymbol? BoundSymbol { get; set; }
  ```
- **If there is no common base class**, add the property to each implementing class individually. Use a codebase search for `IBase2Ast` and `: IExpression` and `: IStatement` to find all implementors.

**Serialization exclusion:**

The `BoundSymbol` property must NOT be serialized because:
1. Symbols contain references to scopes, other symbols, and AST nodes — serializing the full graph would be circular
2. Symbols are recreated during each bind pass
3. Cache hits skip parsing but NOT binding — the binder re-runs on cached ASTs and sets `BoundSymbol` fresh

Ensure `BoundSymbol` is excluded from `IBase2Ast.Serialize()` and any deserialization logic. If AST serialization includes all public properties automatically, add `[JsonIgnore]` or equivalent attribute, or explicitly skip it in `Serialize()`.

**`OwningFile` population:**

Set `OwningFile` on each AST node during the bind pass, not at visitor construction. `TyhpBinder.SetOwningFileRecursive(srcFile, srcFile)` walks each `SrcFileAst` and all of its descendants, assigning the owning `SrcFileAst` to every node. This ensures every AST node carries a reference to its owning `SrcFileAst` without requiring parent-chain traversal or an external lookup dictionary. Apply the same serialization exclusion (`[JsonIgnore]`) as `BoundSymbol`.

### Acceptance Criteria

- [x] `IBase2Ast` interface has a `BoundSymbol` property of type `IBaseSymbol?`
- [x] All classes implementing `IBase2Ast` (directly or transitively) have a working `BoundSymbol` property
- [x] `BoundSymbol` defaults to `null` on all AST nodes
- [x] `BoundSymbol` is NOT included in AST serialization/deserialization (cache compatibility)
- [x] Every AST node has its `OwningFile` property set by the binder's recursive walk (`SetOwningFileRecursive`) during the bind pass
- [x] The project compiles without errors
- [x] No regressions in existing functionality (parsing, binding, existing actions)

### Dependencies

- **Requires:** Story 02 complete (binder symbols and AST infrastructure exist)
- **Provides for:** Phase 2 (binder population of BoundSymbol)

---

## Phase 2: Populate BoundSymbol During Binding




### Phase Overview

Update the binder's declaration pass and resolution pass to set `BoundSymbol` on AST nodes as symbols are created and references are resolved. After this phase, any AST node that has an associated symbol will have its `BoundSymbol` property populated.

### Deliverables

- Modified `Tyhp/TyhpLang/Binder/TyhpBinder.cs` (and partial classes) — set `BoundSymbol` during declaration pass
- Modified `Tyhp/TyhpLang/Binder/Resolution/NameResolver.cs` — set `BoundSymbol` in `RecordResolution()`
- Verification that checker and emitter can access `BoundSymbol`

### Implementation Details

**Declaration pass updates (`TyhpBinder.cs` and partials):**

Wherever the binder creates a symbol and associates it with an AST node (i.e., sets `symbol.DeclaringAstNode = astNode`), also set the reverse link:

```csharp
symbol.DeclaringAstNode = declaringAstNode;
declaringAstNode.BoundSymbol = symbol;  // NEW: reverse link
```

Search for all occurrences across binder partial classes:

1. **Direct `DeclaringAstNode` assignments** — search for `.DeclaringAstNode =` across all `TyhpBinder*.cs` files
2. **`BaseSymbol` constructor calls** — search for `new FunctionDeclarationSymbol(`, `new VariableSymbol(`, `new ObjectDeclarationSymbol(`, etc. — these constructors accept a `declaringNode` parameter and set `DeclaringAstNode` internally. After the constructor call, add `astNode.BoundSymbol = newSymbol`.
3. **`AddChildSymbol` calls** — check if `BaseScope.AddChildSymbol()` has access to the declaring AST node and could set `BoundSymbol` there.

**Resolution pass updates (`NameResolver.cs`):**

In `RecordResolution(IBase2Ast astNode, IBaseSymbol symbol)`, add the `BoundSymbol` assignment:

```csharp
public void RecordResolution(IBase2Ast astNode, IBaseSymbol symbol)
{
    if (astNode != null && symbol != null)
    {
        _resolvedSymbols[astNode] = symbol;
        astNode.BoundSymbol = symbol;  // NEW: set directly on AST node
    }
}
```

This ensures that every resolved reference has its `BoundSymbol` set. The internal `_resolvedSymbols` dictionary is kept for the binder's own use during the resolution pass.

**Verification:**

After binding completes, verify by spot-checking representative cases:

| AST Node Type | Expected `BoundSymbol` |
|---------------|----------------------|
| Function declaration (`PhpFunctionDeclAst`) | `FunctionDeclarationSymbol` |
| Class declaration | `ObjectDeclarationSymbol` |
| Variable assignment (first occurrence) | `VariableSymbol` |
| Variable reference (subsequent use) | Same `VariableSymbol` |
| Type reference in type hint | The resolved type's `ObjectDeclarationSymbol` |
| Unresolved name reference | `null` (binder reports diagnostic) |
| String literal, numeric literal | `null` (no symbol association) |

**Checker compatibility (Story 08):**

Story 08's checker plan states: "The checker uses the binder's symbol resolution. It does not resolve names itself — it relies on the SymbolTree and resolved symbols from Story 02."

With `BoundSymbol` on AST nodes, the checker can directly read `astNode.BoundSymbol` instead of needing a separate `NameResolver` reference. If the checker's implementation currently uses `NameResolver.ResolvedSymbols`, update references to use `astNode.BoundSymbol` instead.

The checker's side dictionary (`Dictionary<IBase2Ast, ICheckedType>` for inferred types) is separate and unaffected by this change.

**Emitter compatibility (Story 11):**

Story 11's emitter plan defines `EmitContext.GetSymbolForAst(IBase2Ast node)`. With `BoundSymbol`, this simplifies to:

```csharp
public IBaseSymbol? GetSymbolForAst(IBase2Ast node) => node.BoundSymbol;
```

Or downstream consumers can access `node.BoundSymbol` directly without going through `EmitContext`.

### Acceptance Criteria

- [x] All symbol declarations in the binder set `BoundSymbol` on the declaring AST node
- [x] All symbol resolutions in `NameResolver.RecordResolution()` set `BoundSymbol` on the reference AST node
- [x] Function declaration AST nodes have `BoundSymbol` pointing to their `FunctionDeclarationSymbol`
- [x] Variable reference AST nodes have `BoundSymbol` pointing to their `VariableSymbol`
- [x] Type reference AST nodes have `BoundSymbol` pointing to the resolved type symbol
- [x] Unresolved references have `BoundSymbol == null` (the binder already reports diagnostics for these)
- [x] The `NameResolver.ResolvedSymbols` dictionary is still populated (for internal binder use during resolution)
- [x] The `CompilationResult` does NOT need a new `NameResolver` property
- [x] The project compiles without errors
- [x] Existing binding behavior is unchanged (no regressions in diagnostic output)
- [x] Running `tyhp build` or `tyhp lint` on example files produces the same diagnostics as before

### Dependencies

- **Requires:** Phase 1 (BoundSymbol property exists on IBase2Ast)
- **Provides for:** Story 08 (checker reads BoundSymbol), Story 11 (emitter reads BoundSymbol)

---

## Cross-Cutting Concerns

### Thread Safety

`BoundSymbol` is set during binding, which is single-threaded (per Story 02 design). After binding, it is read-only. No thread-safety concerns.

### AST Cache Compatibility

The AST cache (`AstCacheService`) serializes/deserializes AST nodes. `BoundSymbol` must NOT be serialized because:
1. Symbols contain references to scopes, other symbols, and AST nodes — serializing the full graph would be circular
2. Symbols are recreated during each bind pass
3. Cache hits skip parsing but NOT binding — the binder re-runs on cached ASTs and sets `BoundSymbol` fresh

### Impact on Existing Code

This change is additive:
- No existing properties are removed or renamed
- No existing behavior is changed
- The `NameResolver.ResolvedSymbols` dictionary continues to be populated (backward compatibility)
- `BaseSymbol.DeclaringAstNode` continues to be set (reverse direction preserved)

The only new behavior is that `BoundSymbol` is populated on AST nodes where previously it did not exist.

### Complete File Inventory

**Modified files:**
```
Tyhp/TyhpLang/Ast/Interfaces/IBase2Ast.cs          (+5 lines: property + doc comment + using)
Tyhp/TyhpLang/Ast/<base class or all AST classes>   (+1 line each: property implementation)
Tyhp/TyhpLang/Binder/Resolution/NameResolver.cs     (+1 line: in RecordResolution)
Tyhp/TyhpLang/Binder/TyhpBinder.cs                  (+N lines: BoundSymbol assignments)
Tyhp/TyhpLang/Binder/TyhpBinder.TopStatements.cs    (+N lines: BoundSymbol assignments)
Tyhp/TyhpLang/Binder/TyhpBinder.ObjectBody.cs       (+N lines: BoundSymbol assignments)
Tyhp/TyhpLang/Binder/TyhpBinder.CodeBlocks.cs       (+N lines: BoundSymbol assignments)
Tyhp/TyhpLang/Binder/TyhpBinder.Extensions.cs       (+N lines: BoundSymbol assignments)
Tyhp/TyhpLang/Binder/TyhpBinder.Tyhpdef.cs          (+N lines: BoundSymbol assignments)
Tyhp/TyhpLang/Binder/TyhpBinder.Resolution.cs       (+N lines: BoundSymbol assignments)
```

Total estimated change: ~30-50 lines across all files.

---

*Generated: 2026-03-23 | Source: Plan 6/7/8 ambiguity review | Branch: Not Specified*

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify that `BoundSymbol` and `OwningFile` are correctly populated on AST nodes after binding, and that downstream phases can access them. Steps can be skipped, reordered, or modified as needed. All commands assume you are in the repository root.

### Step 1: Verify the Project Compiles

Build the compiler after the `IBase2Ast` and binder changes:

```bash
dotnet clean && dotnet restore && dotnet build
```

Expected: Build succeeds with zero errors. The `BoundSymbol` and `OwningFile` properties compile into all AST classes without issues.

### Step 2: Verify BoundSymbol on Declaration Nodes

Create a test file with various declarations:

```tyhp
<?tyhp
namespace Test\BoundSymbol;

class Widget {
    public string $name;
    private int $count = 0;

    public function getName(): string {
        return $this->name;
    }
}

function helper(int $x): int {
    return $x * 2;
}

const MAX_WIDGETS = 100;
```

Save as `test_boundsymbol.tyhp`. Run the compiler in a mode that shows binding output (verbose/debug if available):

```bash
dotnet run --project tyhp.csproj -- build --verbose test_boundsymbol.tyhp
```

Expected (verify via debugger or diagnostic logging if verbose output is limited):
- The class declaration AST node for `Widget` should have `BoundSymbol` set to an `ObjectDeclarationSymbol`
- The function declaration AST node for `helper` should have `BoundSymbol` set to a `FunctionDeclarationSymbol`
- The property declaration AST node for `$name` should have `BoundSymbol` set to an `ObjectPropertySymbol`
- The method declaration AST node for `getName` should have `BoundSymbol` set to an `ObjectMethodSymbol`
- The constant declaration AST node for `MAX_WIDGETS` should have `BoundSymbol` set to a `ConstantSymbol`
- All of these should also have the reverse link: `symbol.DeclaringAstNode` pointing back to the AST node

### Step 3: Verify BoundSymbol on Reference Nodes

Create a test file with name references:

```tyhp
<?tyhp
namespace Test\BoundRef;

class Calculator {
    public int $value = 0;

    public function add(int $x): void {
        $this->value = $this->value + $x;
    }

    public function getValue(): int {
        return $this->value;
    }
}

function useCalculator(): void {
    Calculator $calc = new Calculator();
    $calc->add(5);
    int $result = $calc->getValue();
}
```

Save as `test_boundref.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_boundref.tyhp
```

Expected (verify via debugger or add temporary diagnostic output):
- The type reference `Calculator` in `Calculator $calc` should have `BoundSymbol` pointing to the `ObjectDeclarationSymbol` for `Calculator`
- The method call `$calc->add(5)` — the `add` identifier node should have `BoundSymbol` pointing to the `ObjectMethodSymbol` for `add`
- The method call `$calc->getValue()` — the `getValue` identifier node should have `BoundSymbol` pointing to the `ObjectMethodSymbol` for `getValue`
- The variable `$calc` after its initial assignment should have `BoundSymbol` pointing to the `VariableSymbol`
- `$this` references inside the class should resolve to the implicit instance variable

### Step 4: Verify BoundSymbol Is Null for Non-Symbol Nodes

Using the same test files, verify that AST nodes without associated symbols have `BoundSymbol == null`:
- String literals (e.g., if any were present)
- Numeric literals (`5`, `0`)
- Operators (`+`, `=`)
- Punctuation (parentheses, braces, semicolons)

This can be checked via a debugger breakpoint in the binder's finalization step, iterating over all AST nodes.

### Step 5: Verify OwningFile Is Set on All AST Nodes

Using the same test file, verify that every AST node has its `OwningFile` property set to the correct `SrcFileAst`:

- All AST nodes in `test_boundsymbol.tyhp` should have `OwningFile` pointing to the `SrcFileAst` for `test_boundsymbol.tyhp`
- The `OwningFile.FileName` should match `"test_boundsymbol.tyhp"` (or the full path)

If testing with multiple files, verify each file's AST nodes reference their own `SrcFileAst`.

### Step 6: Verify BoundSymbol Survives Across Pipeline

Check that `BoundSymbol` is available to later phases by running a full build pipeline (if checker or emitter phases are available):

```bash
dotnet run --project tyhp.csproj -- build test_boundsymbol.tyhp
```

Expected: No `NullReferenceException` or similar errors from downstream phases trying to access `BoundSymbol`. The checker (Story 08) should be able to read `astNode.BoundSymbol` without issues. The emitter (Story 11) should be able to use `node.BoundSymbol` for transformation decisions.

### Step 7: Verify BoundSymbol Is NOT Serialized in AST Cache

If AST caching is enabled, verify that `BoundSymbol` is excluded from serialization:

1. Run a build to populate the cache:
   ```bash
   dotnet run --project tyhp.csproj -- build test_boundsymbol.tyhp
   ```

2. Check the cached AST file (if accessible) — it should NOT contain `BoundSymbol` data.

3. Run the build again (should use the cache):
   ```bash
   dotnet run --project tyhp.csproj -- build test_boundsymbol.tyhp
   ```

Expected: The second run uses the cached AST, and the binder re-populates `BoundSymbol` on the deserialized AST nodes. No errors from missing `BoundSymbol` data in the cache.

### Step 8: Verify No Regressions on Example Files

Run the binder on existing example files to ensure the `BoundSymbol` changes do not break anything:

```bash
dotnet run --project tyhp.csproj -- build Examples/ClassTypes.tyhp
dotnet run --project tyhp.csproj -- build Examples/Generics.tyhp
dotnet run --project tyhp.csproj -- build Examples/TypeAliases.tyhp
```

Expected: Same diagnostics as before the `BoundSymbol` changes. No new errors or crashes.

### Step 9: Clean Up Test Files

```bash
rm -f test_boundsymbol.tyhp test_boundref.tyhp
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [x] **Golden fixtures:** *N/A as standalone — BoundSymbol exercised by binder/checker/emitter tests.* Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [x] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [x] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [x] **Runtime self-host conformance (runtime-affecting stories only):** *N/A.* Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [x] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
