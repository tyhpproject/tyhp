# Implementation Plan: Story 23 — Compiler Optimizer (MVP)

> **Roadmap position:** Story 23 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** 03, 08, 09, **20.6**
> **Renumbered from:** legacy Story 4.5
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 23 of the Tyhp compiler TODO
> **Branch:** TBD
> **Generated:** 2026-03-19
> **Last design lock:** 2026-08-21 — **all extension inlining moved to Story 20.6** (extension call sites are spliced at emit, by form, at every optimization level). This story keeps `#[\Tyhp\Optimize\Inline]` for **non-extension** functions / methods / operators and reuses Story 20.6's splice engine. Phases 2–4 are removed.
> **Design lock:** 2026-08-24 — by-reference passing is settled. A written parameter must be declared `&` and only those are by reference; a `&` argument must be referenceable in every optimize mode; repeated evaluation is hoisted into a local, and a call site that cannot be spliced faithfully keeps the real call. See *Argument passing and by-reference semantics* in [Phase 5](#phase-5-tyhpoptimizeinline-for-non-extension-members). `test.php`, `test.tyhp`, and `test.tyhpdef` at the repository root are the runnable probe and the hand-written expected output.
> **Prerequisites:** Story 08 (Checker — full type checking and validation), Story 09 (Emitter — basic PHP output), Story 03 (Extension operator overloads, tyhpdef inline extensions), **Story 20.6** (extension splicing at emit + the shared call-site splice engine this story reuses)

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Phase 1: Optimizer Framework, Configuration, and Build Profiles](#phase-1-optimizer-framework-configuration-and-build-profiles)
- [Phases 2–4: Removed (superseded by Story 20.6)](#phases-24-removed-superseded-by-story-206)
- [Phase 5: `#[\Tyhp\Optimize\Inline]` for Non-Extension Members](#phase-5-tyhpoptimizeinline-for-non-extension-members)
- [Phase 6: Basic Optimization Modules (Constant Folding and Dead Code Elimination)](#phase-6-basic-optimization-modules-constant-folding-and-dead-code-elimination)
- [Phase 7: Pipeline Integration](#phase-7-pipeline-integration)
- [Phase 8: User documentation and AIDevGuide](#phase-8-user-documentation-and-aidevguide)
- [Cross-Story References](#cross-story-references)

---

## Architecture Overview

### What the Optimizer Does

The optimizer is a new phase in the Tyhp compilation pipeline that transforms the bound, type-checked AST to improve the performance and efficiency of the emitted PHP code. It operates on the same AST that the checker has already validated, performing semantics-preserving transformations that reduce runtime overhead without changing observable behavior.

**Extension inlining is not this story.** Story 20.6 splices extension call sites at **emit**, at every optimization level, and the member's form decides whether PHP keeps a backer method (short `=>` omits it; a brace body emits it). Tyhpdef thin mappings (standalone `extension Name { }` from Story 20 and class-body `extension fn` / `extension operator` from Story 20.6) are likewise already erased at emit. There is nothing left for an optimizer to inline there, which is why Phases 2–4 of this plan are removed.

What remains for the optimizer is **`#[\Tyhp\Optimize\Inline]` on non-extension functions, methods, and class-owned operators**. Those members are part of the PHP API — they are always emitted — so form cannot express intent and an explicit attribute is required. When the attribute is present and the body is a single `return expr;`, the optimizer splices that expression into the member's Tyhp call sites (using Story 20.6's splice engine) while leaving the PHP method in place for PHP callers and for `optimize: "none"` builds.

Beyond attributed inlining, the MVP includes basic optimizations — constant folding and dead code elimination — as individually-toggled optimization modules.

### Pipeline Position

The optimizer sits between the checker and the emitter. It operates on the fully bound, fully checked AST — meaning it has complete type information and knows that the code is semantically valid. It transforms the AST before the emitter sees it, so the emitter works with an already-optimized tree.

```
Parser (Stories 01)
    │
    ▼
Binder (Story 02)
    │
    ▼
TyhpSpec (Story 06)
    │
    ▼
Checker (Story 08)
    │
    ▼
┌─────────────────────────────────────────────────────────────────┐
│  STORY 23: Optimizer (MVP)  ◄── THIS PLAN                     │
│                                                                 │
│  1. Load optimization config (level + individual overrides)     │
│  2. Resolve build profile defaults                              │
│  3. Collect enabled modules (sorted by priority)                │
│  4. Run each module against the bound AST                       │
│  5. Record optimization metrics (transformations applied)       │
│                                                                 │
│  NOTE: extra.tyhp.package (Story 20) is generated from the         │
│  UNOPTIMIZED AST, before this phase runs, to preserve the       │
│  stable public API contract.                                    │
└─────────────────────────────────────────────────────────────────┘
    │
    ▼
Emitter (Story 09)
    │
    ▼
Build Action (Story 10)
```

### Module System Architecture

Each optimization is implemented as a self-contained module with a standard interface. This enables:

- **Independent development** — each module can be built and tested in isolation.
- **Individual enable/disable** — users can toggle specific optimizations via `tyhp.json`.
- **Priority-based ordering** — modules declare their execution priority so dependencies between optimizations are respected (e.g., constant folding before dead code elimination).
- **Optimization level association** — each module declares the minimum optimization level at which it activates by default.

```
┌──────────────────────────────────────────────────────────────┐
│  TyhpOptimizer (orchestrator)                                │
│                                                              │
│  1. Reads OptimizationConfig                                 │
│  2. Collects all registered IOptimizationModule instances     │
│  3. Filters by: level >= module.MinimumLevel OR explicit on  │
│  4. Removes explicitly disabled modules                      │
│  5. Sorts remaining by Priority (ascending = runs first)     │
│  6. Runs each module in order against the AST                │
│  7. Collects metrics from each module                        │
└──────────────────────────────────────────────────────────────┘
         │
         ├── InlineAnnotatedMemberModule      (priority: 100, level: basic)
         ├── ConstantFoldingModule            (priority: 400, level: basic)
         └── DeadCodeEliminationModule        (priority: 500, level: basic)
```

### Configuration Model

The optimizer is configured through three layers in `tyhp.json`, applied in order:

**Layer 1 — Build Profile (sets defaults for all build settings):**

```json
{
    "build": {
        "profile": "release"
    }
}
```

Predefined profiles:

| Profile | `optimize` | `generateSourcemap` | Description |
|---------|-----------|---------------------|-------------|
| `debug` | `none` | `true` | No optimizations. Full sourcemaps. Unmodified output for debugging. |
| `balanced` | `basic` | `true` | Safe optimizations with sourcemap support. Good for development. |
| `release` | `aggressive` | `true` | All optimizations enabled. Sourcemaps generated for Tyhp reflection and error mapping. |

The `build.profile` is purely a convenience — it sets defaults that the explicit `build.optimize` and `build.optimizations` keys can override. If no profile is specified, the default behavior is `optimize: "none"`.

**Layer 2 — Optimization Level (overrides profile default):**

```json
{
    "build": {
        "optimize": "basic"
    }
}
```

| Level | Behavior |
|-------|----------|
| `none` | No optimization modules run. Output matches the checker's AST exactly. |
| `basic` | Modules with `MinimumLevel = basic` are enabled. Safe optimizations that do not change the observable public API shape in ways that affect PHP reflection. |
| `aggressive` | All modules enabled. May restructure internal code more aggressively. Only non-public-facing code is transformed. |

**Layer 3 — Individual Overrides (on top of the resolved level):**

```json
{
    "build": {
        "optimize": "aggressive",
        "optimizations": {
            "constantFolding": false,
            "inlineAnnotatedMembers": true
        }
    }
}
```

Individual overrides are applied **after** the level resolves the default set of modules. Setting a module to `false` disables it even if the level would enable it. Setting a module to `true` enables it even if the level would not (including when `optimize` is `"none"`).

**Resolution order:**

1. Start with the build profile's defaults (if specified).
2. Apply the explicit `build.optimize` level (if specified) — overrides the profile's optimize default.
3. Apply individual `build.optimizations` overrides — each key maps to a module's `ConfigKey`.

**CLI argument overrides:**

- `--optimize=none|basic|aggressive` → overrides `build.optimize`
- `--optimize-enable=inlineAnnotatedMembers,constantFolding` → force-enables specific modules
- `--optimize-disable=deadCodeElimination` → force-disables specific modules

### Library vs Application Behavior

The project type (`"type": "library"` or `"type": "application"` in `tyhp.json`) affects how aggressively the optimizer can transform code:

**Application projects:**

- All non-public-facing code is eligible for optimization. Since applications are not consumed as dependencies, aggressive internal restructuring is safe.
- `protected` methods on non-final classes are still treated conservatively (subclasses may rely on them).
- `protected` methods on `final` classes are optimizable (no subclasses possible).

**Library projects:**

- The public API surface must remain intact. Only `private`, `internal`, and `protected`-on-`final` members can be aggressively optimized.
- `extra.tyhp.package` is generated from the **unoptimized** AST (Story 20), guaranteeing the public API contract is not affected by any optimization.
- A `#[\Tyhp\Optimize\Inline]` member that is part of the public API is still spliced at the **call site** within the library's own code, but its PHP method always remains in the emitted output for external consumers. Consumers get the splice through the generated tyhpdef (see Phase 5), not by the method disappearing.

### Visibility-Based Safety Rules

Each member's visibility determines whether it can be optimized:

| Visibility | Final Class? | Application | Library |
|-----------|-------------|-------------|---------|
| `private` | — | Optimizable | Optimizable |
| `internal` | — | Optimizable | Optimizable (not in `extra.tyhp.package`) |
| `protected` | Yes (`final`) | Optimizable | Optimizable |
| `protected` | No | Conservative | Conservative |
| `public` | — | Conservative | Not optimizable (public API) |

"Conservative" means the member itself is preserved, but call sites that reference it may still be optimized (e.g., a public `#[Inline]` method stays in the PHP output while internal callers are spliced).

"Optimizable" means the member body can be spliced into call sites and the call site can be rewritten. Note that no optimization in this story deletes a member: a `#[\Tyhp\Optimize\Inline]` member is always emitted, and extension backer methods are decided by form at emit (Story 20.6).

This table governs the **spliced member's own** visibility. A separate rule governs the visibility of members the body *references*, because a spliced expression is evaluated in the caller's access context — see *Accessibility of a spliced body* in [Phase 5](#phase-5-tyhpoptimizeinline-for-non-extension-members) (`4179`).

### Reflection Guarantees

PHP's native reflection API (`\ReflectionClass`, `\ReflectionMethod`, etc.) is **not guaranteed** to produce expected results when optimizations are enabled. Optimizations may remove, inline, or restructure methods and classes that PHP reflection would normally see. This is a known trade-off: users who need PHP reflection guarantees must set `optimize: "none"`.

A future story will implement **Tyhp reflection classes** (`\Tyhp\Reflection\ReflectionClass`, etc.) that use the sourcemap to provide accurate reflection operations. These Tyhp reflection APIs will produce correct results regardless of optimization level, because they reflect the original Tyhp source structure rather than the compiled PHP output. This means:

- `\Tyhp\Reflection\ReflectionClass` knows about extension methods, operator overloads, generic type parameters, and the original Tyhp class structure.
- Stack traces mapped through sourcemaps will show the original Tyhp method names and line numbers, even for inlined code.
- The Tyhp reflection API is the **guaranteed** way to introspect Tyhp code. PHP reflection is a "best effort" that may break with optimizations.

This future Tyhp reflection API is documented as a cross-story reference but is NOT part of this MVP.

### Compiler Attribute Namespace

All Tyhp compiler attributes are namespaced under `\Tyhp\Optimize\` to avoid conflicts with PHP built-in attributes, third-party libraries (e.g., PHPStan's `#[Pure]`), and user-defined attributes. The `Optimize` sub-namespace signals that these attributes are compiler optimization directives. The compiler attributes defined in this story and Story 24 are:

| Attribute | Purpose | Story |
|-----------|---------|-------|
| `\Tyhp\Optimize\Inline` | Splice a single-`return` **non-extension** function / method / operator at its Tyhp call sites | 23 |
| `\Tyhp\Optimize\Pure` | Mark a function as side-effect-free, enabling memoization and loop hoisting | 24 |
| `\Tyhp\Optimize\Memoize` | Request scope-aware duplicate call elimination for expensive functions | 24 |

Developers can use `use \Tyhp\Optimize\{Inline, Pure, Memoize};` to shorten the syntax. The compiler resolves attribute names using standard PHP name resolution rules. Only attributes that resolve to `\Tyhp\Optimize\*` fully-qualified names are treated as compiler directives — all others pass through to the PHP output.

### Design Principles

1. **Semantics-preserving:** The optimizer must not change observable behavior. The "as-if" rule applies — optimized code must produce the same results as unoptimized code for all valid inputs, with the sole exception of PHP reflection output.
2. **Module isolation:** Each optimization module operates independently. Modules must not depend on the internal state of other modules (though they may benefit from another module having run first due to priority ordering).
3. **Conservative by default:** When in doubt, do not optimize. A missed optimization is a performance regression; an incorrect optimization is a bug.
4. **Diagnostic transparency:** The optimizer reports what it changed via informational diagnostics when `--verbose` is set. This helps developers understand why their compiled output differs from a naive translation.
5. **Sourcemap awareness:** All AST transformations must preserve enough provenance information for the sourcemap generator (Story 17) to produce valid mappings. Inlined code should map back to the original call site in the Tyhp source.
6. **extra.tyhp.package independence:** The `extra.tyhp.package` generator (Story 20) runs on the unoptimized AST. Optimizations never affect the public API contract of a library.

### AST Mutability and In-Place Modification

The optimizer modifies the AST in-place. The AST base class (`Base2Ast`) stores children in a mutable `List<IBase2Ast?>`, but concrete node properties (e.g., `PhpBinaryOpAst.Left`) are expression-bodied getters that read from the `Children` list by index. There is no general-purpose "replace child" API in the current AST infrastructure.

**Approach for this story:**

1. **Preferred: Add property setters** — When an AST property needs to be modified (e.g., replacing the `Left` operand of a `PhpBinaryOpAst`), convert the expression-bodied getter to a full property with both getter and setter. The setter modifies the underlying `Children` list at the correct index. This is the preferred approach because it keeps the AST API clean.

2. **Acceptable: Add helper methods** — Where a setter is awkward (e.g., replacing a child in a variable-length list), add helper methods like `ReplaceChild(int index, IBase2Ast newChild)` or `RemoveChild(int index)` to the base class or specific AST classes.

3. **Avoid: Direct Children list access** — Code outside of AST classes should NOT directly manipulate the `Children` list. All modifications should go through properties or helper methods.

As the optimizer and emitter develop, AST classes will need incremental additions of setters and helper methods. Each phase should add the mutation capabilities it needs to the AST classes it modifies. The existing `AddAttributes()` and `AddGrammarAddon()` methods on `IBase2Ast` are examples of this pattern.

### OriginalAst Provenance Property

When the optimizer replaces or transforms an AST node (e.g., splicing an annotated method call or folding a constant), the replacement node must preserve provenance information for sourcemap generation (Story 17). Add an `OriginalAst` property to `Base2Ast`:

- `public IBase2Ast? OriginalAst { get; set; }` — When set, indicates this node was created by the optimizer as a replacement for the original node. The sourcemap generator uses this to map emitted PHP code back to the original Tyhp call site rather than the inlined body.

Each optimizer module that replaces AST nodes must set `OriginalAst` on the replacement node pointing to the original pre-transformation node. The lookup pattern used by Story 17 is: `var sourceNode = (provider as Base2Ast)?.OriginalAst ?? provider;`

This property should be added to `Base2Ast` in the first optimizer phase that performs AST node replacement.

### Extension Members Are Not Optimizer Candidates

Every extension member is handled before the optimizer ever sees it:

| Member | Handled by | Optimizer role |
|--------|-----------|----------------|
| Tyhpdef thin mapping (`extension fn` / `extension operator`, standalone or class-body) | Emit splices the `=>` expression (Stories 20 / 20.6) | None — the member does not exist in PHP |
| Tyhp `extension { fn … => expr; }` | Emit splices; no PHP backer method (Story 20.6) | None |
| Tyhp `extension { function … { return expr; } }` | Emit splices **and** emits the backer method (Story 20.6) | None — already spliced at `optimize: none` |
| Tyhp `extension { function … { …statements… } }` | Emitted and called (Story 20.6) | None — statement inlining is out of scope |

The optimizer must not treat any extension member as an inlining candidate, and must never look for `__TyhpInlineExt_*` backers. `#[\Tyhp\Optimize\Inline]` on an extension member is a checker error owned by Story 20.6.

### File Organization

New and modified files for this story:

```
Tyhp/TyhpLang/Optimizer/
├── TyhpOptimizer.cs                              (~200 lines) — orchestrator
├── IOptimizationModule.cs                         (~40 lines)  — module interface
├── OptimizationContext.cs                         (~60 lines)  — shared context
├── OptimizationLevel.cs                           (~15 lines)  — enum
├── OptimizationMetrics.cs                         (~40 lines)  — per-module metrics
├── Modules/
│   ├── InlineAnnotatedMemberModule.cs             (~200 lines) — Phase 5
│   ├── ConstantFoldingModule.cs                   (~200 lines) — Phase 6
│   └── DeadCodeEliminationModule.cs               (~180 lines) — Phase 6
└── Attributes/
    └── InlineAttribute.cs                         (~30 lines)  — attribute recognition

Tyhp/Config/
├── OptimizationConfig.cs                          (~80 lines)  — new config section
├── BuildProfileConfig.cs                          (~60 lines)  — new build profile model
├── BuildConfig.cs                                 (modified — add optimize, optimizations, profile)
└── Project.cs                                     (modified — parse new config sections)

Tyhp/CLI/
└── BuildAction.cs                                 (modified — add optimizer step to pipeline)

Tyhp/Domain/Diagnostics/
└── CompilationResult.cs                           (modified — add OptimizeDuration)

Tyhp/Domain/Exceptions/
└── MessageCode.cs                                 (modified — add optimizer codes 4700-4799)
```

### Safety Notes

- Before modifying `BuildAction.cs`, create a timestamped backup
- Before modifying `Project.cs`, create a timestamped backup
- Before modifying `BuildConfig.cs`, create a timestamped backup
- The optimizer must NEVER modify the AST in a way that makes it invalid for the emitter
- If an optimization module encounters an unexpected AST structure, it must skip that node and continue (never throw)
- Backup files are sacred — never delete or modify them
- Never use destructive git commands

### MessageCode Numbering

The optimizer introduces diagnostic codes in the 4700 range:

> **Note:** Optimizer diagnostic codes use the 4700-4799 range to avoid collision with checker deprecation/obsolescence warning codes (4500-4501) in Story 08.

| Code | Name | Severity | Description |
|------|------|----------|-------------|
| 4700 | `OptimizerUnknownError` | Error | Generic optimizer error |
| 4701 | `OptimizerModuleSkipped` | Info | An optimization module was skipped (not applicable or disabled) |
| 4702 | *(retired)* | — | Was `OptimizerInlinedExtensionOperator`. Extension splicing moved to Story 20.6 — do not allocate |
| 4703 | *(retired)* | — | Was `OptimizerInlinedExtensionMethod`. Extension splicing moved to Story 20.6 — do not allocate |
| 4704 | *(retired)* | — | Was `OptimizerEliminatedSyntheticClass`. Backer existence is decided by form in Story 20.6 — do not allocate |
| 4705 | `OptimizerFoldedConstant` | Info | A constant expression was folded |
| 4706 | `OptimizerEliminatedDeadCode` | Info | Dead code after return/throw was eliminated |
| 4707 | *(reserved)* | — | Reserved for a future basic-optimizer diagnostic |
| 4708 | *(retired)* | — | Was `OptimizerInlineAttributeInvalidBody` (warning). An `#[Inline]` the compiler cannot honor is now a checker **error** (`4178`) — do not allocate |
| 4709 | `OptimizerInlineAttributeNotApplicable` | Warning | `#[\Tyhp\Optimize\Inline]` on a member the optimizer cannot splice at a given call site |
| 4710 | `OptimizerInvalidConfigValue` | Warning | An optimization config key or value is not recognized |
| 4711 | `OptimizerInlinedAnnotatedMember` | Info | A member marked with `#[\Tyhp\Optimize\Inline]` was spliced at a call site |

Info-level diagnostics (4701, 4705, 4706, 4711) are only emitted when `--verbose` is set.

**Checker codes.** `#[\Tyhp\Optimize\Inline]` is validated by the checker, not the optimizer, so its errors live in the checker band:

| Code | Name | Severity | Owner | When |
|------|------|----------|-------|------|
| 4174 | `CheckerInlineParameterMutation` | Error | 20.6 | A spliced body writes a parameter not declared `&`, declares `&` on a parameter it does not write, or mutates `$this` |
| 4175 | `CheckerInlineCycle` | Error | 20.6 | A spliced member reduces to itself |
| 4176 | `CheckerInlineAttributeOnExtensionMember` | Error | 20.6 | `#[Inline]` on an extension member |
| 4178 | `CheckerInlineAttributeInvalidBody` | Error | **23** | `#[Inline]` on a body that is not a single `return expr;`, or on a target that is not a function / method / operator |
| 4179 | `CheckerInlineInaccessibleMember` | Error | **23** | A spliced body references a member less accessible than the spliced member itself |
| 4180 | `CheckerNonReferenceableByRefArgument` | Error | **23** | A call passes a non-referenceable expression to a by-reference parameter |
| 4181 | `CheckerErasedMemberUnsafeSplice` | Error | 20.6 | An erased member's call site cannot be spliced faithfully, and has no method to fall back to |

`4180` is **not** inline-specific: it applies to every call with a by-reference parameter, at every optimization level, including calls to PHP builtins. It therefore goes in the general call checker, not the optimizer — a change to already-shipped Story 08 code, planned here rather than by editing Story 08's plan. `4181` is allocated here for continuity of numbering and implemented in Story 20.6, which owns erasure.

---

## Phase 1: Optimizer Framework, Configuration, and Build Profiles




### Phase Overview

Build the optimizer infrastructure: the module interface, the orchestrator, the configuration model, and the build profile system. After this phase, the optimizer can be wired into the pipeline and modules can be registered, even though no actual optimization modules exist yet.

### Deliverables

- `Tyhp/TyhpLang/Optimizer/IOptimizationModule.cs` — Module interface
- `Tyhp/TyhpLang/Optimizer/OptimizationLevel.cs` — Level enum
- `Tyhp/TyhpLang/Optimizer/OptimizationContext.cs` — Shared context
- `Tyhp/TyhpLang/Optimizer/OptimizationMetrics.cs` — Per-module metrics
- `Tyhp/TyhpLang/Optimizer/TyhpOptimizer.cs` — Orchestrator
- `Tyhp/Config/OptimizationConfig.cs` — Configuration section
- `Tyhp/Config/BuildProfileConfig.cs` — Build profile model
- Modified `Tyhp/Config/BuildConfig.cs` — Add optimizer config properties
- Modified `Tyhp/Config/Project.cs` — Parse optimizer config
- Modified `Tyhp/Domain/Exceptions/MessageCode.cs` — Add optimizer codes
- Modified `Tyhp/Domain/Diagnostics/CompilationResult.cs` — Add `OptimizeDuration`

### Implementation Details

**`OptimizationLevel.cs`**

```csharp
namespace Tyhp.TyhpLang.Optimizer;

public enum OptimizationLevel
{
    None = 0,
    Basic = 1,
    Aggressive = 2
}
```

**`IOptimizationModule.cs`**

```csharp
namespace Tyhp.TyhpLang.Optimizer;

public interface IOptimizationModule
{
    /// Display name for diagnostics and verbose output.
    string Name { get; }

    /// Config key used in build.optimizations (e.g., "inlineAnnotatedMembers").
    string ConfigKey { get; }

    /// Execution order. Lower values run first.
    int Priority { get; }

    /// The minimum optimization level at which this module activates by default.
    OptimizationLevel MinimumLevel { get; }

    /// Check whether this module has any work to do for the given context.
    /// Called before Optimize(). If false, the module is skipped entirely.
    bool IsApplicable(OptimizationContext context);

    /// Perform the optimization. Modifies the AST in-place via the context.
    /// Returns metrics describing what was changed.
    OptimizationMetrics Optimize(OptimizationContext context);
}
```

**`OptimizationContext.cs`**

```csharp
namespace Tyhp.TyhpLang.Optimizer;

public class OptimizationContext
{
    public IReadOnlyList<SrcFileAst> AstTrees { get; }
    public GlobalScope GlobalScope { get; }
    public DiagnosticBag Diagnostics { get; }
    public OptimizationConfig Config { get; }
    public ProjectType ProjectType { get; }
    public bool Verbose { get; }

    public OptimizationContext(
        IReadOnlyList<SrcFileAst> astTrees,
        GlobalScope globalScope,
        DiagnosticBag diagnostics,
        OptimizationConfig config,
        ProjectType projectType,
        bool verbose)
    {
        this.AstTrees = astTrees;
        this.GlobalScope = globalScope;
        this.Diagnostics = diagnostics;
        this.Config = config;
        this.ProjectType = projectType;
        this.Verbose = verbose;
    }
}
```

Note: `ProjectType` is an enum (`Application`, `Library`) that is created and parsed in Story 10, Phase 1 (`Tyhp/Config/Project.cs`). If Story 23 is implemented before Story 10, this value should default to `ProjectType.Application`. The `tyhp.json` key is `"type"` with values `"application"` (default) or `"library"`.

**`OptimizationMetrics.cs`**

```csharp
namespace Tyhp.TyhpLang.Optimizer;

public class OptimizationMetrics
{
    public string ModuleName { get; init; }
    public int TransformationsApplied { get; set; }
    public int NodesVisited { get; set; }
    public int NodesSkipped { get; set; }
    public TimeSpan Duration { get; set; }

    public static OptimizationMetrics Empty(string moduleName) => new()
    {
        ModuleName = moduleName,
        TransformationsApplied = 0,
        NodesVisited = 0,
        NodesSkipped = 0,
        Duration = TimeSpan.Zero
    };
}
```

**`TyhpOptimizer.cs`**

```csharp
namespace Tyhp.TyhpLang.Optimizer;

public class TyhpOptimizer
{
    private readonly List<IOptimizationModule> _registeredModules = new();

    public TyhpOptimizer()
    {
        RegisterBuiltInModules();
    }

    private void RegisterBuiltInModules()
    {
        // Each module is registered here. Future modules from Story 24
        // are added to this list as they are implemented.
        _registeredModules.Add(new InlineAnnotatedMemberModule());
        _registeredModules.Add(new ConstantFoldingModule());
        _registeredModules.Add(new DeadCodeEliminationModule());
    }

    public List<OptimizationMetrics> Optimize(OptimizationContext context)
    {
        var metrics = new List<OptimizationMetrics>();
        var resolvedLevel = context.Config.Level;

        var enabledModules = _registeredModules
            .Where(m => IsModuleEnabled(m, resolvedLevel, context.Config))
            .OrderBy(m => m.Priority)
            .ToList();

        foreach (var module in enabledModules)
        {
            if (!module.IsApplicable(context))
            {
                if (context.Verbose)
                {
                    context.Diagnostics.AddInfo(
                        MessageCode.OptimizerModuleSkipped,
                        "", 0, 0, module.Name, "not applicable");
                }
                metrics.Add(OptimizationMetrics.Empty(module.Name));
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var moduleMetrics = module.Optimize(context);
            stopwatch.Stop();
            moduleMetrics.Duration = stopwatch.Elapsed;
            metrics.Add(moduleMetrics);
        }

        return metrics;
    }

    private bool IsModuleEnabled(
        IOptimizationModule module,
        OptimizationLevel resolvedLevel,
        OptimizationConfig config)
    {
        // Individual override takes highest priority
        if (config.IndividualOverrides.TryGetValue(module.ConfigKey, out var explicitEnabled))
        {
            return explicitEnabled;
        }

        // Otherwise, the module is enabled if the resolved level >= module's minimum level
        return resolvedLevel >= module.MinimumLevel;
    }
}
```

**`OptimizationConfig.cs`**

```csharp
namespace Tyhp.Config;

public class OptimizationConfig
{
    /// Resolved optimization level (after profile + explicit override).
    public OptimizationLevel Level { get; set; } = OptimizationLevel.None;

    /// Individual module overrides. Key = module ConfigKey, Value = enabled/disabled.
    public Dictionary<string, bool> IndividualOverrides { get; set; } = new();
}
```

**`BuildProfileConfig.cs`**

```csharp
namespace Tyhp.Config;

public class BuildProfileConfig
{
    public string Name { get; set; }
    public OptimizationLevel OptimizeLevel { get; set; }
    public bool GenerateSourcemap { get; set; }

    public static readonly BuildProfileConfig Debug = new()
    {
        Name = "debug",
        OptimizeLevel = OptimizationLevel.None,
        GenerateSourcemap = true
    };

    public static readonly BuildProfileConfig Balanced = new()
    {
        Name = "balanced",
        OptimizeLevel = OptimizationLevel.Basic,
        GenerateSourcemap = true
    };

    public static readonly BuildProfileConfig Release = new()
    {
        Name = "release",
        OptimizeLevel = OptimizationLevel.Aggressive,
        GenerateSourcemap = true
    };

    public static BuildProfileConfig? FromName(string? name) => name?.ToLowerInvariant() switch
    {
        "debug" => Debug,
        "balanced" => Balanced,
        "release" => Release,
        _ => null
    };
}
```

**`BuildConfig.cs` Modifications**

Add new properties:

- `string? Profile { get; set; }` — build profile name (default: `null` — no profile, optimizer defaults to `none`)
- `string? Optimize { get; set; }` — optimization level string: `"none"`, `"basic"`, `"aggressive"` (default: `null` — resolved from profile or defaults to `"none"`)
- `Dictionary<string, bool>? Optimizations { get; set; }` — individual module overrides (default: `null`)

Parse from `tyhp.json` keys: `build:profile`, `build:optimize`, `build:optimizations`

CLI argument overrides: `--profile=debug|balanced|release` → `Profile`, `--optimize=none|basic|aggressive` → `Optimize`, `--optimize-enable=key1,key2` → individual `true` overrides, `--optimize-disable=key1,key2` → individual `false` overrides

**`Project.cs` — OptimizationConfig Resolution**

Add a method that resolves the final `OptimizationConfig` from the three configuration layers:

```csharp
public OptimizationConfig ResolveOptimizationConfig()
{
    var config = new OptimizationConfig();

    // Layer 1: Build profile defaults
    var profile = BuildProfileConfig.FromName(this.Build.Profile);
    if (profile != null)
    {
        config.Level = profile.OptimizeLevel;
    }

    // Layer 2: Explicit optimize level overrides profile
    if (this.Build.Optimize != null)
    {
        config.Level = this.Build.Optimize.ToLowerInvariant() switch
        {
            "none" => OptimizationLevel.None,
            "basic" => OptimizationLevel.Basic,
            "aggressive" => OptimizationLevel.Aggressive,
            _ => config.Level // keep profile default if invalid, report warning
        };
    }

    // Layer 3: Individual overrides
    if (this.Build.Optimizations != null)
    {
        foreach (var (key, value) in this.Build.Optimizations)
        {
            config.IndividualOverrides[key] = value;
        }
    }

    return config;
}
```

**`CompilationResult.cs` Modifications**

Add:

- `TimeSpan OptimizeDuration { get; set; }` — timing for the optimizer phase
- `IReadOnlyList<OptimizationMetrics>? OptimizationMetrics { get; set; }` — per-module metrics

**`MessageCode.cs` Additions**

Add optimizer diagnostic codes as listed in the MessageCode Numbering section above (4700–4711), skipping the retired codes (4702–4704, 4708) and the reserved 4707.

### Acceptance Criteria

- [ ] `OptimizationLevel` enum with `None`, `Basic`, `Aggressive` values exists
- [ ] `IOptimizationModule` interface is defined with `Name`, `ConfigKey`, `Priority`, `MinimumLevel`, `IsApplicable()`, `Optimize()`
- [ ] `TyhpOptimizer` orchestrator collects registered modules, filters by level + overrides, sorts by priority, runs in order
- [ ] `OptimizationConfig` correctly resolves from the three-layer config (profile → level → individual)
- [ ] `BuildProfileConfig` has `debug`, `balanced`, `release` presets
- [ ] `BuildConfig.cs` has `Profile`, `Optimize`, `Optimizations` properties parsed from `tyhp.json`
- [ ] Individual override `true` enables a module even when level is `none`
- [ ] Individual override `false` disables a module even when level is `aggressive`
- [ ] `CompilationResult.OptimizeDuration` tracks timing
- [ ] `CompilationResult.OptimizationMetrics` reports per-module metrics
- [ ] `MessageCode.cs` has the live codes in 4700–4711 (retired codes 4702–4704 / 4708 are not re-added)
- [ ] Info-level optimizer diagnostics are only emitted when verbose mode is active
- [ ] CLI arguments `--optimize`, `--optimize-enable`, `--optimize-disable`, `--profile` work
- [ ] Unknown config keys in `build.optimizations` emit `OptimizerInvalidConfigValue` warning
- [ ] The optimizer gracefully handles an empty module list (no-op when level is `none` and no overrides)
- [ ] The project compiles with no errors after all changes

### Dependencies

- **Requires:** Story 01 (`DiagnosticBag`, `CompilationResult`), Story 10 Phase 1 (`BuildConfig`)
- **Provides:** Optimizer framework for Phases 5–7 to build on

> **BuildConfig.cs ↔ Story 10 circular-integration note:** Story 23 and Story 10 integrate together — both wire the optimizer/config into `BuildAction`, and each provides a stub for the other when implemented first. **Neither story should claim the other is fully complete first.**
>
> - If `Tyhp/Config/BuildConfig.cs` does not yet exist (Story 10 not implemented), create a **minimal** `BuildConfig.cs` with just the **string** properties this story reads — matching the "`BuildConfig.cs` Modifications" section above: `string? Profile`, `string? Optimize`, and `Dictionary<string, bool>? Optimizations`. (There is **no** `OptimizationLevel`/`BuildProfile` enum *inside* `BuildConfig`; the level/profile are resolved from these string properties by `Project.ResolveOptimizationConfig()`. The `OptimizationLevel` enum lives in `Tyhp/TyhpLang/Optimizer/`, and `BuildProfileConfig` is its own class.)
> - Similarly, `ProjectType` (enum `Application`/`Library`) is owned by Story 10 Phase 1; if absent, default to `ProjectType.Application` (see the `OptimizationContext` note above) and let Story 10 supersede it.
> - When Story 10 is implemented, it **supersedes** these stubs with the full config and **merges into/extends** the existing files rather than recreating them.

---

## Phases 2–4: Removed (superseded by Story 20.6)

Three extension-inlining modules were planned here. All three are gone:

| Removed phase | Original module (config key) | Why it is gone |
|---|---|---|
| Phase 2 | `ExtensionOperatorInliningModule` (`extensionOperatorInlining`) | Extension operator call sites are spliced at **emit** by Story 20.6, at every optimization level. |
| Phase 3 | `ExtensionMethodInliningModule` (`extensionMethodInlining`) | Extension method call sites are spliced at **emit** by Story 20.6. |
| Phase 4 | `SyntheticClassEliminationModule` (`syntheticClassElimination`) | Whether an extension backer class exists is decided by the member's **form** (Story 20.6), not by counting inlined call sites. A brace-bodied member's PHP method is deliberately kept for PHP callers, so eliminating it would break the contract the author asked for. |

Story 20.6 also owns the shared **call-site splice engine** — substitution of receiver / arguments / defaults, parenthesization, fixpoint reduction of nested splices, and hoisting a repeated argument or receiver into a generated local — plus the safety rules (by-reference contract, no cycles, accessibility of referenced members). Phase 5 reuses that engine instead of reimplementing it. The by-reference rules are specified under *Argument passing and by-reference semantics* in Phase 5 and bind the engine in 20.6 as well as this phase; the one divergence is that 20.6's erased members have no method to fall back to, so rule 3's declination is error `4181` there instead of a warning.

Do not add config keys, modules, or diagnostics for extension inlining to this story. The phase numbers are left unused so existing references to Phases 5-7 stay valid.

---

## Phase 5: `#[\Tyhp\Optimize\Inline]` for Non-Extension Members




### Phase Overview

Support `#[\Tyhp\Optimize\Inline]` on a **non-extension** function, method, or class-owned operator: the PHP member is always emitted, and its Tyhp call sites are spliced with the body expression.

Extensions do not need the attribute — Story 20.6 decides splicing from the member's form. A non-extension member cannot use form the same way: a short `fn` function is still part of the PHP API and must exist, so intent has to be declared explicitly. That is the whole remaining job of the attribute.

Both body forms qualify, because the PHP output is the same either way:

```tyhp
#[Inline] public function myTrim(string $s): string { return \trim($s); }
#[Inline] public fn myTrim(string $s): string => \trim($s);
```

All Tyhp compiler attributes live under the `\Tyhp\Optimize\` namespace to avoid conflicts with PHP built-in attributes, third-party library attributes (e.g., PHPStan's `#[Pure]`), and user-defined attributes. Developers can use `use \Tyhp\Optimize\Inline;` to shorten the syntax. The compiler resolves attribute names using standard PHP name resolution rules.

### Deliverables

- `Tyhp/TyhpLang/Optimizer/Attributes/InlineAttribute.cs` — Attribute recognition logic
- `Tyhp/TyhpLang/Optimizer/Modules/InlineAnnotatedMemberModule.cs` — Splices annotated members via Story 20.6's engine
- Checker validation of attribute targets and bodies (`4178`) and of member accessibility inside a spliced body (`4179`)
- Track C additions so consumers of a library also splice (see **Generated tyhpdef** below)

### Implementation Details

**Syntax:**

```tyhp
use \Tyhp\Optimize\Inline;

class Account {
    #[Inline()]
    public function label(): string {
        return $this->first . ' ' . $this->last;
    }

    #[\Tyhp\Optimize\Inline()]
    operator +(self $left, self $right): self => self::merge($left, $right);
}
```

Class-owned operators take no visibility modifier (only `abstract` / `final`), matching the existing grammar.

The attribute uses standard PHP attribute syntax (`#[...]`) so it can be parsed by the existing ANTLR grammar. The Tyhp compiler recognizes `\Tyhp\Optimize\Inline` (resolved via standard PHP name resolution, including `use` imports) as a compile-time attribute — it is NOT emitted to the PHP output. Any attribute that resolves to a fully-qualified name under `\Tyhp\Optimize\` is checked against the known compiler attribute list (`Inline`, `Pure`, `Memoize`); unrecognized `\Tyhp\Optimize\*` attributes emit a warning.

**Behavior:**

1. During binding, `\Tyhp\Optimize\Inline` is recognized as a compiler-intrinsic attribute and stored on the function / method / operator symbol. It is never emitted to PHP.
2. The member is **always emitted** to PHP, unchanged. Unlike an extension `=>` member, an attributed member is part of the PHP API.
3. `InlineAnnotatedMemberModule` (`ConfigKey = "inlineAnnotatedMembers"`, `Priority = 100`, `MinimumLevel = Basic`) rewrites Tyhp call sites of annotated members through Story 20.6's splice engine, which handles substitution, parentheses, fixpoint reduction, and temporaries for repeated arguments.
4. Story 20.6's safety rules apply unchanged: a splice cycle is `4175`, a parameter written without a `&` declaration (or any mutation of `$this`) is `4174`, and a body may only reference members at least as accessible as the annotated member itself (`4179`). By-reference passing follows the three rules under *Argument passing and by-reference semantics* below.
5. When the module does not run — `optimize: "none"` with no individual override — call sites keep calling the PHP method. Behavior is identical either way; only the emitted shape differs. This is the one real difference from extension splicing, which is emit and therefore always on.
6. When a specific call site cannot be spliced (rule 3), emit `OptimizerInlineAttributeNotApplicable` and leave the call.

To force splicing at `optimize: "none"`, enable the module directly: `"optimizations": { "inlineAnnotatedMembers": true }`.

**Accessibility of a spliced body:**

A spliced expression is evaluated at the call site, not inside the declaring class, so PHP's visibility rules are applied there. The rule is that a body may only reference members **at least as accessible as the annotated member itself**:

| Annotated member | Body may reference |
|---|---|
| `public` | public members only |
| `protected` | protected and public members |
| `private` | any member of the declaring class |

Anything stricter is error `4179`. This is sound rather than conservative: every legal call site of a member is by definition a context that already holds that level of access, so the spliced expression is always legal wherever the call was legal. A `private` member's call sites are all inside the class, which is why a private body may freely read private state. Trait members work out the same way, since a trait's private members become private members of the using class.

Class-context keywords lose their declaring class when spliced, so emit must rewrite them:

| In the body | Spliced as |
|---|---|
| `self::` | the literal declaring class name |
| `parent::` | the literal parent class name |
| `static::` | `$receiver::`, preserving late static binding |

**Argument passing and by-reference semantics:**

Splicing replaces a parameter with the caller's own argument expression, which behaves like by-reference passing: a body that writes through the parameter (`$i += 2`, `\ksort($a)`) reaches the caller's variable. A real PHP method call does not, unless the parameter is declared `&`. Three rules keep `optimize: "none"` and a spliced build observably identical. `test.tyhp` is the source of record for each, with the emitted shapes in `test.php` and `test.tyhpdef`.

**Rule 1 — a written parameter is by reference, and only those are.**

A parameter is *written* when the body assigns to it, compound-assigns it, increments or decrements it in either position, or passes it to a callee's `&` parameter. Every callee's signature is known from tyhpdef, so this is decidable. The author must have declared exactly the written parameters `&`; a mismatch either way is error `4174`. Emit mirrors the declaration, so the PHP signature is what the author wrote.

Pre-increment is not exempt. `++$i` on a by-value parameter cannot be spliced faithfully: substituting it leaks the write into the caller, and rewriting it to `($i + 1)` is not type-safe, since `++` on a non-numeric string is a string increment while `+ 1` is a `TypeError`. Requiring `&` costs nothing, and an author who wants a non-mutating increment writes `$v + 1`.

Mutating `$this` remains error `4174` with no `&` escape, since a receiver expression is not a parameter.

**Rule 2 — a by-reference argument must be referenceable, in every optimize mode.**

This is a general call-site rule, not an inline one: it applies to `\ksort($holder->hookedProp)` exactly as it applies to a spliced member, and it holds at `optimize: "none"`. Error `4180`.

| Argument to a `&` parameter | Result |
|---|---|
| Local variable, plain property, static property, array element | Allowed |
| Property whose read path is by reference — `&get` hook, `&__get`, `&offsetGet` | Allowed |
| Property whose read path is by value — `get` hook, `__get`, `offsetGet` | Error `4180` |
| `readonly` property | Error `4180` |
| `private(set)` / `protected(set)` outside the writing scope | Error `4180` |
| Literal, constant, call result, or any other non-lvalue | Error `4180` |

The test is whether the read path is by reference, not whether an accessor is involved. Measured on PHP 8.5: a by-value `get` hook fatals with "Indirect modification is not allowed"; a by-value `__get` or `offsetGet` warns and **silently discards the write**; `readonly` fatals even inside its declaring class, both before and after its one permitted assignment. Their `&` counterparts all work, and `&offsetGet` does satisfy the `ArrayAccess` interface.

Two of these are rejections of code that would have worked once spliced — a by-value `__get` or `offsetGet` in a write position succeeds when the accessor pair runs. Rejecting them anyway is deliberate: the two optimize modes must agree, and the mode where it fails is the one that ships when optimization is off.

Whether a property's read path is by reference has to be visible through a tyhpdef, not just in Tyhp source. **Story 20.7** provides that — bodyless `{ get; set; }` / `{ &get; }` and the binder flags this rule reads — and lands before Story 20.6's engine. `&__get` and `&offsetGet` need nothing new, since `tyhpdefImportClassMethod` already carries `ReturnsRef`.

**Rule 3 — hoist repeated evaluation; decline the splice when hoisting cannot help.**

Substitution changes how often an argument or receiver is evaluated whenever the body mentions it a number of times other than once.

| Situation | Handling |
|---|---|
| By-value parameter used more than once | Hoist into a by-value read local |
| `&` parameter used more than once | Hoist into a **ref-bound** local (`$t = &$arg;`) |
| Receiver expression used more than once | Hoist into a by-value read local |
| Argument used zero times, or reached only past a short circuit | Decline |
| No statement slot for a needed hoist | Decline |

A by-value local is wrong for a `&` parameter: it satisfies the arity problem but severs the reference, so the write never lands. A ref-bound local is exact, and it is also what makes a side-effecting lvalue path correct — `crazy($a[$i++])` naively spliced yields 168 and advances `$i` twice against the correct 36 and once.

A reference bind is a statement, not an expression, so a ref-bound hoist needs a statement slot. Where the call site has none — inside a ternary or a loop condition — the splice is declined.

Declining means the call site keeps the real method call and emits warning `OptimizerInlineAttributeNotApplicable`. That is available here because an annotated member is always emitted to PHP. **An erased member has no method to fall back to**, so for a short `=>` extension member or a tyhpdef thin mapping the same condition is error `4181` instead, owned by Story 20.6.

**Generated tyhpdef (Track C):**

A library's consumers should splice too, otherwise the intent stops at the library boundary. Track C (Story 20) represents an annotated member as a declared PHP method under an aliased Tyhp name plus an auto-active class-body thin mapping (Story 20.6) carrying the expression:

```tyhp
<?tyhp

class MyClass {
    #[Inline]
    public function myTrim(string $s): string {
        return \trim($s);
    }
}
```

emits PHP with the method intact:

```php
<?php

class MyClass
{
    public function myTrim(string $s): string
    {
        return \trim($s);
    }
}
```

and generates:

```tyhp
<?tyhpdef

class MyClass {
    public function myTrim as myTrim__tyhpInlineBacker(string $s): string;
    extension fn myTrim(string $s): string => \trim($s);
}
```

- The alias frees the Tyhp name `myTrim` for the thin mapping while still letting Tyhp call the real method. It is **required**, not optional: rule 3 lets a consumer decline to splice, and without the declaration there is nothing to fall back to.
- `&` is mirrored onto both the aliased declaration and the mapping — onto the declaration because the emitted PHP has it, and onto the mapping so a consumer's checker can apply rule 2 before rewriting.
- Add `GeneratedNames.InlineBackerSuffix = "__tyhpInlineBacker"` (same family as `ExtensionBackerSuffix`, Tyhp-only, never in emitted PHP) and reserve it in the checker alongside the other generated-name collision checks.
- The copied expression must be valid at a consumer call site — builtins or public API only. If it is not, omit the mapping and declare the method under its own name.
- For a class-owned operator, no alias is needed: the PHP name is the mangled operator method (`__add`) and the mapping is `extension operator + … => expr`.

**Validation (checker integration):**

The checker (Story 08) validates the attribute:

| Target | Result |
|--------|--------|
| Function / method / class-owned operator whose body is a single `return expr;` (either form) | Allowed |
| Multi-statement body | Error `4178` |
| Abstract / interface member (no body) | Error `4178` |
| Class, property, constant, parameter, or other construct | Error `4178` |
| Body referencing a member less accessible than the annotated member | Error `4179` |
| Body writing a parameter not declared `&`, or `&` on a parameter it does not write | Error `4174` |
| Body mutating `$this` | Error `4174` |
| Any extension member | Error `4176` (owned by Story 20.6) |
| Tyhpdef thin mapping | Error `4176` — already erased (Stories 20 / 20.6) |

An attribute the compiler cannot honor is an error, not a warning: silently ignoring a directive hides a performance assumption the author wrote down deliberately.

Rule 2's referenceability check (`4180`) is not in this table, because it validates a **call site** rather than the attribute, and it runs whether or not the callee is annotated.

### Acceptance Criteria

- [ ] `#[\Tyhp\Optimize\Inline]` (and short form `#[Inline]` with `use \Tyhp\Optimize\Inline;`) is recognized as a compile-time attribute during binding
- [ ] The attribute is NOT emitted to PHP output
- [ ] The annotated member is always emitted to PHP
- [ ] `InlineAnnotatedMemberModule` has `ConfigKey = "inlineAnnotatedMembers"`, `Priority = 100`, `MinimumLevel = Basic`
- [ ] Tyhp call sites of an annotated method, function, and class-owned operator are spliced through Story 20.6's engine
- [ ] Both a short `=>` body and a single-`return` brace body are accepted
- [ ] Multi-statement, bodyless, and non-callable targets report `4178`
- [ ] The attribute on any extension member reports `4176`
- [ ] A `public` or `protected` body referencing a less accessible member reports `4179`; a `private` member's body may reference private state
- [ ] `self::`, `parent::`, and `static::` in a spliced body are rewritten to the declaring class, the parent class, and `$receiver::`
- [ ] `optimize: "none"` leaves the call in place; the individual module override splices it
- [ ] A written parameter not declared `&`, a `&` on an unwritten parameter, and any mutation of `$this` each report `4174`; pre-increment counts as a write
- [ ] Emitted PHP carries `&` on exactly the parameters the Tyhp declaration carries it on
- [ ] A non-referenceable argument to a by-reference parameter reports `4180` at every optimization level, including for a call to a PHP builtin; a `&get` / `&__get` / `&offsetGet` read path is accepted
- [ ] A by-value parameter or receiver used more than once is hoisted into a read local; a `&` parameter used more than once is hoisted into a ref-bound local
- [ ] A call site with no statement slot for a needed hoist, an argument the body never reads, and an argument reachable only past a short circuit each keep the real call and warn
- [ ] A spliced build and an `optimize: "none"` build are observably identical for every case in `test.php`, including argument evaluation count and order
- [ ] Track C emits the aliased backer declaration plus the thin mapping, mirrors `&` onto both, and a consumer compiling against it splices
- [ ] Unrecognized `\Tyhp\Optimize\*` attributes emit a warning

### Dependencies

- **Requires:** Phase 1 (framework), Story 20.6 (splice engine and safety rules), Story 08 (checker for validation), Story 20 (Track C generator)
- **Provides:** Developer-directed splicing for non-extension functions, methods, and class-owned operators

---

## Phase 6: Basic Optimization Modules (Constant Folding and Dead Code Elimination)




### Phase Overview

Implement two additional optimization modules that perform basic code improvements. These are simple, well-understood optimizations that most compilers implement. Each is its own module with individual enable/disable support.

### Deliverables

- `Tyhp/TyhpLang/Optimizer/Modules/ConstantFoldingModule.cs`
- `Tyhp/TyhpLang/Optimizer/Modules/DeadCodeEliminationModule.cs`

### Implementation Details

**6a. Constant Folding Module**

| Property | Value |
|----------|-------|
| `ConfigKey` | `constantFolding` |
| `Priority` | `400` |
| `MinimumLevel` | `Basic` |

Evaluates constant expressions at compile time and replaces them with their computed values. Targets:

1. **Arithmetic on literals:** `2 + 3` → `5`, `10 * 2.5` → `25.0`
2. **String concatenation of literals:** `"hello" . " " . "world"` → `"hello world"`
3. **Boolean logic on constants:** `true && false` → `false`, `!true` → `false`
4. **`nameof()` resolution:** Already handled by the emitter as a compile-time construct, but the optimizer can fold the result earlier if beneficial.
5. **Constant references:** If a `const` value is a simple literal, references to it can be folded (only for `private` or `internal` constants — public constants must remain as references for external code compatibility).

**Scope for MVP:** Only fold literal-to-literal expressions. Do not attempt cross-reference folding (constant references) in this story — that is reserved for Story 24.

**6b. Dead Code Elimination Module**

| Property | Value |
|----------|-------|
| `ConfigKey` | `deadCodeElimination` |
| `Priority` | `500` |
| `MinimumLevel` | `Basic` |

Removes code that can never execute. Targets:

1. **Statements after `return`:** Any statements following a `return` in the same block are dead code.
2. **Statements after `throw`:** Same as above.
3. **Statements after unconditional `break`/`continue`:** In loop or switch bodies.
4. **`if (false)` blocks:** When the condition is a literal `false` (or a constant that folds to `false` if constant folding ran first).
5. **`if (true)` else blocks:** The else branch is dead; the if body can be unwrapped.

**Scope for MVP:** Focus on items 1–3 (unreachable-after-terminator). Items 4–5 (constant-condition branches) are included only when the condition is a literal — not when it depends on constant folding having run.

**Note:** Unused import pruning is NOT an optimizer module. It is handled by the emitter's `PruneFileImports()` method (Story 09, Phase 6) which runs unconditionally — even when optimization is disabled (`optimize: "none"`). Import pruning must always occur to produce clean PHP output.

### Acceptance Criteria

- [ ] Constant folding evaluates arithmetic, string concatenation, and boolean logic on literals
- [ ] Constant folding produces correct results for integer overflow, float precision, and edge cases
- [ ] Dead code elimination removes statements after `return`, `throw`, unconditional `break`/`continue`
- [ ] Dead code elimination does NOT remove code after conditional `break`/`continue` (only unconditional)
- [ ] Both modules have correct `ConfigKey`, `Priority`, `MinimumLevel` values
- [ ] Both modules report metrics (transformations applied, nodes visited)
- [ ] Both modules emit verbose diagnostics for each transformation
- [ ] Enabling constant folding + dead code elimination together handles `if (2 > 3) { ... }` (constant folding makes the condition `false`, then dead code elimination can remove the block)

### Dependencies

- **Requires:** Phase 1 (framework)
- **Provides:** Basic code quality improvements

---

## Phase 7: Pipeline Integration




### Phase Overview

Wire the optimizer into the build pipeline. Update `BuildAction` to run the optimizer between the checker and emitter phases. Update `CompilationResult` to report optimizer timing and metrics. Handle the interaction with `extra.tyhp.package` generation (Story 20) — the tyhpdef must be generated from the **unoptimized** AST.

### Deliverables

- Modified `Tyhp/CLI/BuildAction.cs` — Add optimizer step to pipeline
- Modified `Tyhp/Domain/Diagnostics/CompilationResult.cs` — Add optimizer timing and metrics
- Modified `Tyhp/Config/Project.cs` — Parse optimizer config
- Modified `Tyhp/Config/DisplayHelp.cs` — Add help text for optimizer CLI arguments

### Implementation Details

**`BuildAction.cs` — Pipeline Modification**

The current pipeline flow (from Story 10 Phase 2) is:

```
Step 6: Run checker
Step 7: Error gate
Step 8: Run emitter
```

Insert the optimizer between the error gate and the emitter:

```
Step 6: Run checker
Step 7: Error gate — decide whether to continue
Step 7.5: Generate extra.tyhp.package (if library project) — BEFORE optimization
Step 8: Run optimizer
Step 9: Run emitter
```

The `extra.tyhp.package` generation step is placed **before** the optimizer to ensure the public API contract is captured from the unoptimized AST. This is critical: the `extra.tyhp.package` must reflect what external consumers see, not what the optimizer has transformed internally.

**Step 8 implementation:**

```csharp
// Step 8: Run optimizer
var optimizationConfig = project.ResolveOptimizationConfig();
if (optimizationConfig.Level != OptimizationLevel.None 
    || optimizationConfig.IndividualOverrides.Any(kv => kv.Value))
{
    var optimizeStopwatch = Stopwatch.StartNew();
    var optimizer = new TyhpOptimizer();
    var optimizationContext = new OptimizationContext(
        result.ParsedFiles,
        result.GlobalScope,
        result.Diagnostics,
        optimizationConfig,
        project.Type,
        project.Build.Verbose
    );
    var metrics = optimizer.Optimize(optimizationContext);
    optimizeStopwatch.Stop();

    result.OptimizeDuration = optimizeStopwatch.Elapsed;
    result.OptimizationMetrics = metrics;

    if (project.Build.Verbose)
    {
        // Log per-module metrics
        foreach (var m in metrics.Where(m => m.TransformationsApplied > 0))
        {
            // Log: "{moduleName}: {transformations} transformations in {duration}ms"
        }
    }
}
```

**Summary display updates:**

Add optimizer timing to the Step 10 summary:

```
Parse: 120ms
Bind: 85ms
Check: 210ms
Optimize: 45ms    ← NEW
Emit: 150ms
Total: 610ms

Optimizations: 23 transformations (3 modules active)  ← NEW
```

**Configuration parsing in `Project.cs`:**

Parse the new `build.profile`, `build.optimize`, and `build.optimizations` keys in `ConfigChanged()`:

```csharp
this.Build.Profile = this._configuration["build:profile"];
this.Build.Optimize = this._configuration["build:optimize"];

// Parse build:optimizations as a dictionary
var optimizationsSection = this._configuration.GetSection("build:optimizations");
if (optimizationsSection.Exists())
{
    this.Build.Optimizations = new Dictionary<string, bool>();
    foreach (var child in optimizationsSection.GetChildren())
    {
        if (bool.TryParse(child.Value, out var enabled))
        {
            this.Build.Optimizations[child.Key] = enabled;
        }
    }
}
```

**CLI argument parsing:**

Add handling for `--optimize`, `--optimize-enable`, `--optimize-disable`, and `--profile` arguments. These overlay onto the config section properties, taking highest priority.

**Sourcemap interaction (Story 17):**

The sourcemap generator needs to handle inlined nodes. When an AST node has been replaced by the optimizer:

1. The replacement node preserves the original node's source location via the `OriginalAst` property (defined in the "OriginalAst Provenance Property" section above).
2. The sourcemap generator maps the emitted PHP code back to the **original Tyhp source location** of the call site, not the inlined method body.
3. This means that when a developer sees a PHP error on a line that was a spliced `#[Inline]` call, the sourcemap correctly maps back to the `$f->myTrim($s)` expression in their Tyhp source — not to the `\trim($s)` body of the method. The same requirement applies to the emit-time splices Story 20.6 performs.

This interaction is documented here but the actual sourcemap modifications are part of Story 17's scope. Story 17 should handle `OriginalAst`-annotated nodes when generating mappings.

### Acceptance Criteria

- [ ] The optimizer runs between the checker error gate and the emitter in `BuildAction`
- [ ] `extra.tyhp.package` generation (Story 20 placeholder) occurs BEFORE the optimizer
- [ ] `CompilationResult.OptimizeDuration` reports correct timing
- [ ] `CompilationResult.OptimizationMetrics` contains per-module metrics
- [ ] The summary display includes optimizer timing and transformation count
- [ ] `build.profile`, `build.optimize`, `build.optimizations` are parsed from `tyhp.json`
- [ ] CLI arguments `--optimize`, `--optimize-enable`, `--optimize-disable`, `--profile` work correctly
- [ ] When optimization level is `none` and no individual overrides are set, the optimizer step is skipped entirely (no overhead)
- [ ] The build action reports optimizer metrics in verbose mode
- [ ] `DisplayHelp.cs` includes documentation for all new CLI arguments
- [ ] Replacement AST nodes preserve original source location for sourcemap accuracy
- [ ] No regressions in existing build pipeline behavior when optimizer is disabled

### Dependencies

- **Requires:** Phase 1 (framework), Phases 5–6 (modules), Story 10 (build action)
- **Provides:** Fully integrated optimizer in the build pipeline

---

## Phase 8: User documentation and AIDevGuide

### Phase Overview

The optimizer is user-facing in four ways: a `tyhp.json` section, CLI flags, an attribute authors write in source, and a set of diagnostics. All four need published documentation, and the rules an author must follow to use `#[\Tyhp\Optimize\Inline]` correctly — a written parameter must be declared `&`, a `&` argument must be referenceable — are language rules, not tuning knobs, so they belong in the language pages rather than only in a build page.

Story 24 adds the remaining modules and two more attributes. Keep this phase to what this story ships and let Story 24 extend the same pages.

### Pages to update (create a sibling page only if an existing page cannot hold the topic)

| Page | What this story adds |
|------|----------------------|
| **New** `docs/content/project_optimization.md` | The `optimize` levels and what each turns on; `optimizations` per-module overrides; `build.profile`; the guarantee that a level never changes observable behavior. Register in `docs/content/toc.json` |
| `docs/content/project_optionsList.md` | `build.profile`, `build.optimize`, `build.optimizations` keys with defaults |
| `docs/content/cli_build.md` | `--optimize`, `--optimize-enable`, `--optimize-disable`, `--profile`, and the verbose metrics output |
| **New** `docs/content/tyhp_NNNN_inlineAttribute.md` (or a section in `tyhp_2700_compileTimeConstructs.md`) | `#[\Tyhp\Optimize\Inline]`: valid targets, single-`return` bodies only, the `&` contract for written parameters, referenceable arguments, and when the compiler keeps the real call instead of splicing. Take the next free `tyhp_NNNN_` slot per `docs/readme.md` rather than assuming one |
| `docs/content/tyhp_2100_extensions.md` | Extension splicing is form-driven and always on, so `optimize` does not affect it — the contrast readers will otherwise assume |
| `docs/content/diagnostics_reference.md` | `4178`–`4181` and the `47xx` optimizer codes, including which are info-only under `--verbose` |
| `docs/content/faq_cli.md` / `docs/content/faq_project.md` | Why `optimize: none` still splices extensions; whether optimization can change behavior |
| `docs/content/quickref.md` | One `#[Inline]` line |

### AIDevGuide

`AIDevGuide/` is the bundle an agent loads to write Tyhp applications, and it is **regenerated** from the prompt in `AIDevGuide/REGEN.md`. A claim corrected only in a section file comes back the next time the bundle is regenerated, so update the prompt as well as the section.

| File | What this story changes |
|---|---|
| `AIDevGuide/guide/26-build-cli.md` | `optimize` levels, the `optimizations` map, and the new CLI flags |
| `AIDevGuide/guide/17-compile-time-helpers.md` | `#[\Tyhp\Optimize\Inline]` and its authoring rules; an agent writing library code will reach for it |
| `AIDevGuide/guide/27-diagnostics.md` | The new checker errors and the optimizer warning band |
| `AIDevGuide/guide/28-availability-gotchas.md` | Whether `#[Inline]` is "use freely" yet |
| `AIDevGuide/QUICK_GUIDE.md` | One line for the attribute |
| `AIDevGuide/REGEN.md` | Prompt item 26 lists `tyhp.json` keys and CLI commands; add the optimize keys and flags. Item 17 (compile-time helpers) should mention the attribute |

### Acceptance Criteria

- [ ] Every `tyhp.json` key and CLI flag Phase 1 and Phase 7 add is documented with its default
- [ ] The `#[Inline]` page states the `&` contract, the referenceability rule, and that an unspliceable call site keeps the real call
- [ ] Docs make clear that extension splicing is not controlled by `optimize`
- [ ] `4178`–`4181` and the `47xx` codes are in `diagnostics_reference.md`
- [ ] `docs/content/toc.json` lists any new page
- [ ] `AIDevGuide/guide/26-build-cli.md` and `27-diagnostics.md` cover the new surface, and `REGEN.md` items 17 and 26 would regenerate it

### Dependencies

- **Requires:** Phases 1, 5, 6, 7 (the shipped surface being documented)
- **Provides:** Published documentation and agent-facing guide entries matching the shipped optimizer

---

## Cross-Story References

### Stories That Must Be Updated

| Story | Update Required |
|-------|----------------|
| **Story 09 (Emitter)** | Update pipeline diagram to include optimizer between checker and emitter. Add note that the emitter receives an already-optimized AST. |
| **Story 10 (Build Action)** | Add optimizer step to the pipeline flow diagram and `BuildAction` implementation. Add `OptimizationConfig`, `BuildProfileConfig` to config parsing. Add `OptimizeDuration` to timing summary. Add CLI argument handling for `--optimize`, `--optimize-enable`, `--optimize-disable`, `--profile`. |
| **Story 11 (Emitter Feature Expansion)** | Update project context to include the optimizer in the pipeline. Note that extension method/operator transformers may produce output that has already been partially optimized. |
| **Story 17 (Sourcemaps)** | Add handling for optimizer-replaced AST nodes. Replacement nodes carry an `OriginalAst` reference for source mapping. Stack traces for inlined code should map back to the Tyhp call site. |
| **Story 20 (Tyhpdef Generator)** | Confirm that `extra.tyhp.package` / Track C tyhpdef is generated from the unoptimized AST. Document the ordering requirement: tyhpdef generation runs before the optimizer. Track C also needs the `#[Inline]` representation from Phase 5 (aliased backer declaration + thin mapping). |
| **Story 20.6 (Thin mappings + splice engine)** | Owns **all** extension splicing (at emit, by form, at every optimization level) and the shared call-site splice engine plus its safety rules. This story's Phases 2–4 are removed. Do not plan optimizer inlining, `__TyhpInlineExt_*` emit, or backer-class elimination for any extension member. **Still to mirror into 20.6's engine section:** the accessibility rule (`4179`), the `self::` / `parent::` / `static::` rewrite, the three by-reference rules from Phase 5, and `4181` for an erased member whose call site cannot be spliced faithfully. |
| **Story 08 (Checker)** | Already shipped. Rule 2's referenceability check (`4180`) is a general call-site rule covering every by-reference parameter at every optimization level, including PHP builtins, so it lands in the shipped call checker rather than the optimizer. Plan and diagnostics live here; do not edit Story 08's plan. |
| **Story 20.7 (Tyhpdef hooked properties)** | Supplies the tyhpdef syntax rule 2 depends on: bodyless `{ get; set; }` / `{ &get; }` plus the binder flags `HasAccessor` / `HasGetHook` / `HasSetHook` / `GetHookReturnsRef`, set for tyhpdef-bound properties as well as Tyhp ones. `4180` reads those flags, so a consumer compiling against `package.tyhpdef` can tell a referenceable `{ &get; }` from a by-value `{ get; }`. Implemented before Story 20.6. `&__get` and `&offsetGet` need nothing new — `tyhpdefImportClassMethod` already carries `ReturnsRef`. |
| **Story 18 (XDebug Proxy)** | Inlined method calls do not appear in PHP stack traces. When reconstructing those frames for the IDE from sourcemaps, the XDebug proxy uses `SensitiveParameterRedaction` (`Tyhp/XDebugProxy/Translation/SensitiveParameterRedaction.cs`) on any synthesized or rewritten argument list. Prefer PHP’s `\SensitiveParameterValue` wrapper when present. Wrap opaque when the parameter is marked `#[\SensitiveParameter]` or when compile/sourcemap metadata is missing. Do not unwrap `$value` the way Decimal display does. |
| **TODO.md** | Add Story 23 and Story 24 entries. |
| **MASTER_FEATURES_LIST.md** | Add optimizer to build configuration and CLI tools sections. |

### Future Stories Referenced

| Story | Description |
|-------|-------------|
| **Story 24 (Advanced Optimizations)** | Adds additional optimization modules: operator chain optimization, null-safe chain collapsing, type guard elimination, devirtualization, struct copy elision, pure function memoization, escape analysis, `#[\Tyhp\Optimize\Pure]` attribute, `#[\Tyhp\Optimize\Memoize]` attribute, and cross-reference constant folding. Also lays the foundation for the Tyhp reflection API. |
| **Story 29 (Tyhp Reflection API)** | Implements `\Tyhp\Reflection\ReflectionClass`, `\Tyhp\Reflection\ReflectionMethod`, and related classes that use sourcemaps and reflection metadata to provide accurate reflection regardless of optimization level. This is the guaranteed reflection mechanism for Tyhp code, replacing reliance on PHP's native reflection which is not guaranteed to work with optimized code. Includes stack trace reconstruction for optimized code. |

---

## Human Testing and Verification

> **Note:** These steps are meant to help a human developer manually verify the optimizer implementation. Steps can be skipped, reordered, or adapted based on what has already been tested or what is most relevant. The optimizer sits between the checker and the emitter, so verifying it requires comparing emitted output with and without optimization enabled.

### Step 1: Verify the Optimizer Compiles

Run the project build to confirm all optimizer code compiles without errors:

```bash
dotnet build
```

Confirm there are no build errors in the `Tyhp/TyhpLang/Optimizer/` directory.

### Step 2: Verify Optimization Levels via Configuration

Create a minimal `tyhp.json` and test that the optimizer responds to configuration:

```json
{
    "build": {
        "optimize": "none"
    }
}
```

Run:

```bash
tyhp build --verbose
```

**Expected:** The verbose output should indicate the optimizer was skipped (level is `none`).

Now change to:

```json
{
    "build": {
        "optimize": "basic"
    }
}
```

Run:

```bash
tyhp build --verbose
```

**Expected:** The verbose output should show the optimizer running with modules at the `basic` level (annotated-member inlining, constant folding, dead code elimination).

### Step 3: Verify Build Profiles

Test the three build profiles:

```json
{
    "build": {
        "profile": "debug"
    }
}
```

Run `tyhp build --verbose`. **Expected:** Optimizer does not run (profile sets `optimize: "none"`).

```json
{
    "build": {
        "profile": "release"
    }
}
```

Run `tyhp build --verbose`. **Expected:** Optimizer runs with `aggressive` level.

### Step 4: Verify Individual Module Overrides

Test force-enabling a module when optimization is off:

```json
{
    "build": {
        "optimize": "none",
        "optimizations": {
            "constantFolding": true
        }
    }
}
```

Run `tyhp build --verbose`. **Expected:** Only the constant folding module runs; all others are skipped.

Test force-disabling a module:

```json
{
    "build": {
        "optimize": "basic",
        "optimizations": {
            "deadCodeElimination": false
        }
    }
}
```

Run `tyhp build --verbose`. **Expected:** All basic modules run except dead code elimination.

### Step 5: Verify `#[Inline]` on a Method

Create `test_opt_inline_method.tyhp`:

```tyhp
<?tyhp

namespace App;

use \Tyhp\Optimize\Inline;

class Formatter {
    #[Inline()]
    public function myTrim(string $s): string {
        return \trim($s);
    }
}

function demo(Formatter $f): void {
    echo $f->myTrim("  hi  ");
}
```

Run without optimization:

```bash
tyhp build --optimize=none
```

Inspect the output — the call should remain `$f->myTrim('  hi  ')`, and `Formatter::myTrim()` should be present.

Run with optimization:

```bash
tyhp build --optimize=basic --verbose
```

**Expected:**

- The call site becomes `(\trim('  hi  '))`
- `Formatter::myTrim()` is **still emitted** — the attribute never removes a PHP member
- `#[Inline]` does not appear in the PHP output
- Verbose output reports `OptimizerInlinedAnnotatedMember`
- The output passes `php -l`

If the project is a library, also check the generated tyhpdef: it should declare `public function myTrim as myTrim__tyhpInlineBacker(string $s): string;` plus `extension fn myTrim(string $s): string => \trim($s);`.

### Step 6: Verify `#[Inline]` on an Operator and Repeated Parameters

Create `test_opt_inline_operator.tyhp`:

```tyhp
<?tyhp

namespace App;

use \Tyhp\Optimize\Inline;

class Point {
    public function __construct(public int $x) {}

    #[Inline()]
    operator +(self $left, self $right): self => new self($left->x + $right->x);

    #[Inline()]
    public function twice(int $n): int {
        return $n + $n;
    }
}

function demo(Point $a, Point $b): void {
    Point $c = $a + $b;
    int $d = $a->twice(\readAndAdvance());
}
```

Run:

```bash
tyhp build --optimize=basic --verbose
```

**Expected:**

- `$a + $b` becomes `(new \App\Point($a->x + $b->x))`
- `Point::__add()` is still emitted
- The repeated parameter is evaluated **once**: `(($__tyhpInlineTemp1 = \readAndAdvance()) + $__tyhpInlineTemp1)`
- The generated temp name does not collide with any variable in `demo()`
- The output passes `php -l`

### Step 7: Verify Constant Folding

Create `test_opt_constfold.tyhp`:

```tyhp
<?tyhp

function testConstantFolding(): void {
    int $x = 2 + 3;                   // Should fold to 5
    float $y = 10.0 * 2.5;            // Should fold to 25.0
    string $s = "hello" . " " . "world";  // Should fold to "hello world"
    bool $b = true && false;           // Should fold to false
    bool $c = !true;                   // Should fold to false
}
```

Run:

```bash
tyhp build --optimize=basic --verbose
```

Inspect the output PHP. **Expected:**

- `$x = 5;` (not `$x = 2 + 3;`)
- `$y = 25.0;` (not `$y = 10.0 * 2.5;`)
- `$s = 'hello world';` (not `$s = "hello" . " " . "world";`)
- `$b = false;`
- `$c = false;`
- Verbose output reports `OptimizerFoldedConstant` for each folded expression

### Step 8: Verify Dead Code Elimination

Create `test_opt_deadcode.tyhp`:

```tyhp
<?tyhp

function testDeadCode(): int {
    return 42;
    echo "This should be removed";       // Dead code after return
    int $x = 100;                         // Dead code after return
}

function testDeadCodeThrow(): void {
    throw new \RuntimeException("error");
    echo "Also dead";                     // Dead code after throw
}

function testDeadBreak(): void {
    for (int $i = 0; $i < 10; $i++) {
        break;
        echo "unreachable";              // Dead code after break
    }
}
```

Run:

```bash
tyhp build --optimize=basic --verbose
```

Inspect the output PHP. **Expected:**

- Statements after `return 42;` are removed
- Statements after `throw` are removed
- Statement after unconditional `break` is removed
- Verbose output reports `OptimizerEliminatedDeadCode` for each removal
- The output passes `php -l`

### Step 9: Verify the Optimizer Leaves Extension Members Alone

Extension splicing belongs to Story 20.6 and happens at emit, so it must be visible with the optimizer switched off.

```tyhp
<?tyhp

extension StringHelpers {
    fn shortProcess(extends string $this): string => ' ' . $this;

    function simpleProcess(extends string $this): string {
        return $this . ' ';
    }

    function complexStringProcess(extends string $this): string {
        string $result = \trim($this);
        return $result . '!';
    }
}
```

Run:

```bash
tyhp build --optimize=none --verbose
```

**Expected:**

- `$s->shortProcess()` is already spliced, and `StringHelpers::shortProcess()` does **not** exist in PHP
- `$s->simpleProcess()` is already spliced, and `StringHelpers::simpleProcess()` **does** exist in PHP
- `$s->complexStringProcess()` is a static call to the emitted method
- The optimizer reports **no** extension transformations at any level, and the backer class is never eliminated

### Step 10: Verify Invalid `#[Inline]` Targets Are Errors

```tyhp
<?tyhp

use \Tyhp\Optimize\Inline;

class Bad {
    private string $secret = "hidden";

    #[Inline()]
    public function safeUpper(string $s): string {
        if (\strlen($s) === 0) {
            return "";
        }
        return \strtoupper($s);
    }

    #[Inline()]
    public function mutates(int $n): int {
        return $n++;
    }

    #[Inline()]
    public function alsoMutates(int $n): int {
        return ++$n;
    }

    #[Inline()]
    public function unusedRef(int &$n): int {
        return $n + 1;
    }

    #[Inline()]
    public function leaks(): string {
        return $this->secret;
    }

    #[Inline()]
    private function alsoReadsSecret(): string {
        return $this->secret;
    }
}

extension AlsoBad {
    #[Inline()]
    fn shout(extends string $this): string => \strtoupper($this);
}
```

Run `tyhp build`. **Expected:** six errors — `TYHP4178` (multi-statement body), `TYHP4174` three times (`$n++` and `++$n` write a parameter not declared `&`; `unusedRef` declares `&` on a parameter it never writes), `TYHP4179` (a `public` member's body reads a `private` property), and `TYHP4176` (attribute on an extension member). None of them are warnings. `alsoReadsSecret` is **not** an error: a private member's call sites already hold private access.

### Step 10b: Verify By-Reference Argument and Hoisting Rules

```tyhp
<?tyhp

use \Tyhp\Optimize\Inline;

class Holder {
    public readonly int $frozen;
    public int $hooked {
        get => $this->frozen;
        set(int $value) { }
    }
    public array $refItems {
        &get { return $this->items; }
    }
    private array $items = [];

    public function __construct(): void { $this->frozen = 4; }
}

class Refs {
    #[Inline]
    public fn addTwo(int &$i): int => $i += 2;

    #[Inline]
    public fn twice(array &$a): int => (($a[] = 1) !== null ? \count($a) : 0);

    #[Inline]
    public fn ignoreSecond(int $a, int $b): int => $a;
}

class Uses {
    public function run(): void {
        Refs $r = new Refs();
        Holder $h = new Holder();
        int $i = 1;

        $r->addTwo($i);              // legal
        $r->addTwo($h->frozen);      // TYHP4180 — readonly
        $r->addTwo($h->hooked);      // TYHP4180 — by-value get hook
        $r->addTwo(4);               // TYHP4180 — literal
        $r->addTwo(\intval('4'));    // TYHP4180 — call result
        \ksort($h->hooked);          // TYHP4180 — not inline, same rule

        $r->twice($h->refItems);     // legal: &get is referenceable
        $r->ignoreSecond(1, \intval('2'));  // warning: arg 2 would be dropped
    }
}
```

Run `tyhp build --verbose`. **Expected:** five `TYHP4180` errors and one `OptimizerInlineAttributeNotApplicable` warning. Build with the errors removed and confirm in the emitted PHP that `twice($h->refItems)` produced a ref-bound local (`$__tyhpInlineTemp… = &$h->refItems;`) rather than a by-value copy, and that `ignoreSecond` remained a real method call. Then run the same source at `--optimize=none` and diff the runtime output: the two must be identical.

### Step 11: Verify `optimize: "none"` and the Module Override

Using the Step 5 file, run:

```bash
tyhp build --optimize=none --verbose
```

**Expected:** The call to `myTrim()` remains a real method call; the optimizer does not run.

Then run:

```bash
tyhp build --optimize=none --optimize-enable=inlineAnnotatedMembers --verbose
```

**Expected:** Only `InlineAnnotatedMemberModule` runs, and the call site is spliced. Run both outputs with `php` and confirm identical runtime behavior.

### Step 12: Verify CLI Optimizer Arguments

Test the CLI override flags:

```bash
tyhp build --optimize=aggressive --verbose
tyhp build --optimize=none --optimize-enable=constantFolding --verbose
tyhp build --optimize=basic --optimize-disable=deadCodeElimination --verbose
tyhp build --profile=release --verbose
```

For each, check the verbose output confirms the expected set of modules ran.

### Step 13: Verify Optimizer Metrics in Build Summary

Run any optimized build:

```bash
tyhp build --optimize=basic --verbose
```

**Expected:** The build summary includes:

- Optimizer timing (e.g., `Optimize: 45ms`)
- Transformation count (e.g., `Optimizations: 23 transformations (5 modules active)`)
- Per-module metrics in verbose output

### Step 14: Verify No Behavioral Changes from Optimization

For a test file that uses all basic features:

1. Build with `--optimize=none` → run the output with `php` → capture output
2. Build with `--optimize=basic` → run the output with `php` → capture output
3. Compare the two outputs

**Expected:** The runtime output is identical. Optimization is semantics-preserving — only the generated PHP code structure changes, not the behavior.

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
