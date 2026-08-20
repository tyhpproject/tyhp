# Implementation Plan: Story 23 — Compiler Optimizer (MVP)

> **Roadmap position:** Story 23 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** 03, 08, 09
> **Renumbered from:** legacy Story 4.5
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 23 of the Tyhp compiler TODO
> **Branch:** TBD
> **Generated:** 2026-03-19
> **Prerequisites:** Story 08 (Checker — full type checking and validation), Story 09 (Emitter — basic PHP output), Story 03 (Extension operator overloads, tyhpdef inline extensions)

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Phase 1: Optimizer Framework, Configuration, and Build Profiles](#phase-1-optimizer-framework-configuration-and-build-profiles)
- [Phase 2: Extension Operator Inlining Module](#phase-2-extension-operator-inlining-module)
- [Phase 3: Extension Method Inlining Module](#phase-3-extension-method-inlining-module)
- [Phase 4: Synthetic Extension Class Elimination Module](#phase-4-synthetic-extension-class-elimination-module)
- [Phase 5: `#[\Tyhp\Optimize\Inline]` Attribute Support](#phase-5-tyhpinline-attribute-support)
- [Phase 6: Basic Optimization Modules (Constant Folding and Dead Code Elimination)](#phase-6-basic-optimization-modules-constant-folding-and-dead-code-elimination)
- [Phase 7: Pipeline Integration](#phase-7-pipeline-integration)
- [Cross-Story References](#cross-story-references)

---

## Architecture Overview

### What the Optimizer Does

The optimizer is a new phase in the Tyhp compilation pipeline that transforms the bound, type-checked AST to improve the performance and efficiency of the emitted PHP code. It operates on the same AST that the checker has already validated, performing semantics-preserving transformations that reduce runtime overhead without changing observable behavior.

The MVP focuses on the most impactful optimization: **inlining single-statement extension operator and method bodies**. When a `package.tyhp.json` re-exposes operator overloads via `extension operator` syntax, the emitter would otherwise generate an extra static method layer (e.g., `__TyhpInlineExt_Decimal::__OP_Decimal_ADD_Decimal($a, $b)` calling `$a->add($b)`). The optimizer detects single-statement bodies (any return expression — method calls, operators, concatenation, etc.) and substitutes the expression directly at the call site, wrapped in parentheses to preserve precedence. Multi-statement bodies (variable declarations, conditionals, multiple return paths) are not inlined.

Beyond extension inlining, the MVP includes basic optimizations — constant folding and dead code elimination — as individually-toggled optimization modules.

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
│  NOTE: package.tyhp.json (Story 20) is generated from the         │
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
         ├── ExtensionOperatorInliningModule  (priority: 100, level: basic)
         ├── ExtensionMethodInliningModule    (priority: 200, level: basic)
         ├── SyntheticClassEliminationModule  (priority: 300, level: basic)
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
            "extensionOperatorInlining": true
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
- `--optimize-enable=extensionOperatorInlining,constantFolding` → force-enables specific modules
- `--optimize-disable=deadCodeElimination` → force-disables specific modules

### Library vs Application Behavior

The project type (`"type": "library"` or `"type": "application"` in `tyhp.json`) affects how aggressively the optimizer can transform code:

**Application projects:**

- All non-public-facing code is eligible for optimization. Since applications are not consumed as dependencies, aggressive internal restructuring is safe.
- `protected` methods on non-final classes are still treated conservatively (subclasses may rely on them).
- `protected` methods on `final` classes are optimizable (no subclasses possible).

**Library projects:**

- The public API surface must remain intact. Only `private`, `internal`, and `protected`-on-`final` members can be aggressively optimized.
- `package.tyhp.json` is generated from the **unoptimized** AST (Story 20), guaranteeing the public API contract is not affected by any optimization.
- Extension operators and methods that are part of the public API (exposed in `package.tyhp.json`) can still be inlined at the **call site** within the library's own code, but the generated static methods must remain in the emitted PHP for external consumers.

### Visibility-Based Safety Rules

Each member's visibility determines whether it can be optimized:

| Visibility | Final Class? | Application | Library |
|-----------|-------------|-------------|---------|
| `private` | — | Optimizable | Optimizable |
| `internal` | — | Optimizable | Optimizable (not in `package.tyhp.json`) |
| `protected` | Yes (`final`) | Optimizable | Optimizable |
| `protected` | No | Conservative | Conservative |
| `public` | — | Conservative | Not optimizable (public API) |

"Conservative" means the member itself is preserved, but call sites that reference it may still be optimized (e.g., a public extension method body remains, but internal callers of that method can have their calls inlined).

"Optimizable" means the member body can be inlined, the member can be eliminated if all call sites are inlined, and the call site can be rewritten.

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
| `\Tyhp\Optimize\Inline` | Request inlining of single-statement extension methods/operators | 23 |
| `\Tyhp\Optimize\Pure` | Mark a function as side-effect-free, enabling memoization and loop hoisting | 24 |
| `\Tyhp\Optimize\Memoize` | Request scope-aware duplicate call elimination for expensive functions | 24 |

Developers can use `use \Tyhp\Optimize\{Inline, Pure, Memoize};` to shorten the syntax. The compiler resolves attribute names using standard PHP name resolution rules. Only attributes that resolve to `\Tyhp\Optimize\*` fully-qualified names are treated as compiler directives — all others pass through to the PHP output.

### Design Principles

1. **Semantics-preserving:** The optimizer must not change observable behavior. The "as-if" rule applies — optimized code must produce the same results as unoptimized code for all valid inputs, with the sole exception of PHP reflection output.
2. **Module isolation:** Each optimization module operates independently. Modules must not depend on the internal state of other modules (though they may benefit from another module having run first due to priority ordering).
3. **Conservative by default:** When in doubt, do not optimize. A missed optimization is a performance regression; an incorrect optimization is a bug.
4. **Diagnostic transparency:** The optimizer reports what it changed via informational diagnostics when `--verbose` is set. This helps developers understand why their compiled output differs from a naive translation.
5. **Sourcemap awareness:** All AST transformations must preserve enough provenance information for the sourcemap generator (Story 17) to produce valid mappings. Inlined code should map back to the original call site in the Tyhp source.
6. **package.tyhp.json independence:** The `package.tyhp.json` generator (Story 20) runs on the unoptimized AST. Optimizations never affect the public API contract of a library.

### AST Mutability and In-Place Modification

The optimizer modifies the AST in-place. The AST base class (`Base2Ast`) stores children in a mutable `List<IBase2Ast?>`, but concrete node properties (e.g., `PhpBinaryOpAst.Left`) are expression-bodied getters that read from the `Children` list by index. There is no general-purpose "replace child" API in the current AST infrastructure.

**Approach for this story:**

1. **Preferred: Add property setters** — When an AST property needs to be modified (e.g., replacing the `Left` operand of a `PhpBinaryOpAst`), convert the expression-bodied getter to a full property with both getter and setter. The setter modifies the underlying `Children` list at the correct index. This is the preferred approach because it keeps the AST API clean.

2. **Acceptable: Add helper methods** — Where a setter is awkward (e.g., replacing a child in a variable-length list), add helper methods like `ReplaceChild(int index, IBase2Ast newChild)` or `RemoveChild(int index)` to the base class or specific AST classes.

3. **Avoid: Direct Children list access** — Code outside of AST classes should NOT directly manipulate the `Children` list. All modifications should go through properties or helper methods.

As the optimizer and emitter develop, AST classes will need incremental additions of setters and helper methods. Each phase should add the mutation capabilities it needs to the AST classes it modifies. The existing `AddAttributes()` and `AddGrammarAddon()` methods on `IBase2Ast` are examples of this pattern.

### OriginalAst Provenance Property

When the optimizer replaces or transforms an AST node (e.g., inlining an extension operator call), the replacement node must preserve provenance information for sourcemap generation (Story 17). Add an `OriginalAst` property to `Base2Ast`:

- `public IBase2Ast? OriginalAst { get; set; }` — When set, indicates this node was created by the optimizer as a replacement for the original node. The sourcemap generator uses this to map emitted PHP code back to the original Tyhp call site rather than the inlined body.

Each optimizer module that replaces AST nodes must set `OriginalAst` on the replacement node pointing to the original pre-transformation node. The lookup pattern used by Story 17 is: `var sourceNode = (provider as Base2Ast)?.OriginalAst ?? provider;`

This property should be added to `Base2Ast` in the first optimizer phase that performs AST node replacement.

### Tyhpdef Inline Extension Availability

The binder (Story 03) already loads tyhpdef inline `extension function`/`extension operator` declarations into synthetic extension classes named `__TyhpInlineExt_{ClassName}`. These synthetic classes are created via `GetOrCreateSyntheticInlineExtensionScope()` in `TyhpBinder.Extensions.cs`. The optimizer can access the bodies of these synthetic extension methods through the symbol table for inlining purposes. No additional binder work is needed.

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
│   ├── ExtensionOperatorInliningModule.cs         (~250 lines) — Phase 2
│   ├── ExtensionMethodInliningModule.cs           (~200 lines) — Phase 3
│   ├── SyntheticClassEliminationModule.cs         (~150 lines) — Phase 4
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
| 4702 | `OptimizerInlinedExtensionOperator` | Info | An extension operator call was inlined |
| 4703 | `OptimizerInlinedExtensionMethod` | Info | An extension method call was inlined |
| 4704 | `OptimizerEliminatedSyntheticClass` | Info | A synthetic extension class was eliminated (all members inlined) |
| 4705 | `OptimizerFoldedConstant` | Info | A constant expression was folded |
| 4706 | `OptimizerEliminatedDeadCode` | Info | Dead code after return/throw was eliminated |
| 4707 | *(reserved)* | — | Reserved for a future basic-optimizer diagnostic; intentionally unused so the 4700–4711 sequence is contiguous |
| 4708 | `OptimizerInlineAttributeInvalidBody` | Warning | `#[\Tyhp\Optimize\Inline]` used on a member that is not a single-statement body |
| 4709 | `OptimizerInlineAttributeNotApplicable` | Warning | `#[\Tyhp\Optimize\Inline]` used on a member that cannot be inlined (e.g., public API in library) |
| 4710 | `OptimizerInvalidConfigValue` | Warning | An optimization config key or value is not recognized |
| 4711 | `OptimizerInlinedAnnotatedMember` | Info | A member marked with `#[\Tyhp\Optimize\Inline]` was inlined |

Info-level diagnostics (4701–4706, 4711) are only emitted when `--verbose` is set.

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

    /// Config key used in build.optimizations (e.g., "extensionOperatorInlining").
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
        _registeredModules.Add(new ExtensionOperatorInliningModule());
        _registeredModules.Add(new ExtensionMethodInliningModule());
        _registeredModules.Add(new SyntheticClassEliminationModule());
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

Add optimizer diagnostic codes as listed in the MessageCode Numbering section above (4700–4711).

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
- [ ] `MessageCode.cs` has codes 4700–4711
- [ ] Info-level optimizer diagnostics are only emitted when verbose mode is active
- [ ] CLI arguments `--optimize`, `--optimize-enable`, `--optimize-disable`, `--profile` work
- [ ] Unknown config keys in `build.optimizations` emit `OptimizerInvalidConfigValue` warning
- [ ] The optimizer gracefully handles an empty module list (no-op when level is `none` and no overrides)
- [ ] The project compiles with no errors after all changes

### Dependencies

- **Requires:** Story 01 (`DiagnosticBag`, `CompilationResult`), Story 10 Phase 1 (`BuildConfig`)
- **Provides:** Optimizer framework for Phases 2–7 to build on

> **BuildConfig.cs ↔ Story 10 circular-integration note:** Story 23 and Story 10 integrate together — both wire the optimizer/config into `BuildAction`, and each provides a stub for the other when implemented first. **Neither story should claim the other is fully complete first.**
>
> - If `Tyhp/Config/BuildConfig.cs` does not yet exist (Story 10 not implemented), create a **minimal** `BuildConfig.cs` with just the **string** properties this story reads — matching the "`BuildConfig.cs` Modifications" section above: `string? Profile`, `string? Optimize`, and `Dictionary<string, bool>? Optimizations`. (There is **no** `OptimizationLevel`/`BuildProfile` enum *inside* `BuildConfig`; the level/profile are resolved from these string properties by `Project.ResolveOptimizationConfig()`. The `OptimizationLevel` enum lives in `Tyhp/TyhpLang/Optimizer/`, and `BuildProfileConfig` is its own class.)
> - Similarly, `ProjectType` (enum `Application`/`Library`) is owned by Story 10 Phase 1; if absent, default to `ProjectType.Application` (see the `OptimizationContext` note above) and let Story 10 supersede it.
> - When Story 10 is implemented, it **supersedes** these stubs with the full config and **merges into/extends** the existing files rather than recreating them.

---

## Phase 2: Extension Operator Inlining Module




### Phase Overview

Implement the first optimization module: inlining single-statement extension operator bodies. This is the core motivation for the entire optimizer — when a `package.tyhp.json` re-exposes operator overloads via `extension operator` syntax, the emitter generates a static method on a synthetic extension class. When that method has a single return statement, the optimizer substitutes the return expression directly at the call site (wrapped in parentheses), eliminating the extra stack frame.

### Deliverables

- `Tyhp/TyhpLang/Optimizer/Modules/ExtensionOperatorInliningModule.cs`

### Implementation Details

**What gets inlined:**

An extension operator is a candidate for inlining when:

1. The operator body is a **single return statement** containing any expression (method calls, arithmetic, string concatenation, object construction, etc.).
2. The operator is not part of the library's public API (or the call site is within the same library).
3. Each parameter is referenced **at most once** in the return expression, OR the argument at the call site is a simple variable (no side effects on re-evaluation). This prevents duplicate evaluation of side-effecting expressions.

The key distinction: **single-statement bodies are inlineable; multi-statement bodies are not.** Bodies with variable declarations, conditionals, loops, or multiple return paths cannot be inlined.

**Example 1 — simple delegation (before optimization):**

The `package.tyhp.json` for `tyhp/decimal` generates:

```
extension operator +(self $left, self $right): self {
    return $left->add($right);
}
```

Which the emitter would produce as:

```php
// Synthetic extension class
class __TyhpInlineExt_Decimal {
    public static function __OP_Decimal_ADD_Decimal(\Tyhp\Decimal $left, \Tyhp\Decimal $right): \Tyhp\Decimal {
        return $left->add($right);
    }
}

// Call site
$result = __TyhpInlineExt_Decimal::__OP_Decimal_ADD_Decimal($a, $b);
```

After optimization:

```php
$result = $a->add($b);
```

**Example 2 — expression with operators (also inlineable):**

```
extension operator +(self $left, int $right): self {
    return new self($left->value + $right);
}
```

Call site `$result = $a + 5;` inlines to:

```php
$result = (new Foo(($a)->value + 5));
```

The inlined expression is **wrapped in parentheses** to preserve operator precedence at the call site. Parameter references (`$left`, `$right`) are substituted with the actual operand expressions (also parenthesized when needed).

**Example 3 — NOT inlineable (multi-statement body):**

```
extension operator +(self $left, self $right): self {
    self $result = new self($left->value + $right->value);
    $result->normalize();
    return $result;
}
```

This has variable declarations and multiple statements — it cannot be inlined. The synthetic static method is preserved.

**Detection algorithm:**

1. Walk all bound AST trees looking for call sites that resolve to extension operator symbols.
2. For each such call site, look up the operator's body in the symbol table.
3. Check if the body consists of exactly one statement that is a `ReturnStatementAst`.
4. Check the parameter safety rule: each parameter must be referenced at most once in the return expression, unless the corresponding call-site argument is a **safe-to-duplicate expression** (no side effects on re-evaluation). This prevents duplicate evaluation of side-effecting expressions.

   **Safe-to-duplicate expressions** (can be substituted multiple times without changing behavior):
   - Simple variables: `$var`, `$this`
   - Literal values: `42`, `3.14`, `'string'`, `true`, `false`, `null`
   - Class/enum constants: `Foo::BAR`, `self::CONST`, `MyEnum::Case`

   **Unsafe expressions** (must NOT be duplicated):
   - Property access: `$this->property`, `$obj->prop` — PHP 8.4 property hooks could have side effects
   - Array access: `$array[$key]` — `ArrayAccess::offsetGet()` could have side effects
   - Method/function calls: `$obj->method()`, `strlen($x)`
   - Increment/decrement: `$i++`, `--$j`
   - Assignments or compound expressions

   This classification is deliberately conservative. A future enhancement could relax it (e.g., checking if a property has no hooks), but for the MVP, safety takes priority over optimization coverage.
5. Apply visibility rules: check if the call site is eligible for inlining based on the operator's visibility and project type.

**AST transformation:**

When a call site is inlined, the AST node at the call site is replaced with the return expression from the operator body, with parameter references substituted by the actual operand expressions. The entire inlined expression is wrapped in parentheses to preserve operator precedence. There is **no** `ParenthesizedExpressionAst` node type in the AST — a parenthesized expression `(expr)` is represented by **`PhpDereferenceableExpressionAst`** (the grammar's `#dereferenceableExpr` rule, `T_OPEN_ROUND_BRACE expr T_CLOSE_ROUND_BRACE`, wraps a single inner expression). The existing factory is `PhpDereferenceableExpressionAst.Create(innerExpression, parserRuleContext)`, but AST nodes do not retain their `ParserRuleContext`; add a small helper (e.g. `CreateFrom(innerExpression, locationSource)`) that builds the wrapper and copies the original node's location via the existing `Base2Ast.SetContext(Base2Ast)` overload. The emitter renders this node as `( ... )`. The replacement node preserves the original node's source location metadata for sourcemap accuracy.

Extension operator call sites are identified by walking all AST nodes whose `BoundSymbol` (from Story 05) is an `ObjectOperatorOverloadMethodSymbol` with `IsExtensionOperator == true`. Extension operator calls remain as regular binary expressions in the AST — the `BoundSymbol` is what identifies them as extension-resolved. There is no `ExtensionOperatorCallAst` node type; the optimizer uses the bound symbol information to detect these call sites.

```csharp
// Pseudocode for the transformation
var inlinedExpression = SubstituteParameters(
    operatorBody.ReturnExpression,
    parameterMap: {
        "$left"  => ParenthesizeIfNeeded(originalCallNode.LeftOperand),
        "$right" => ParenthesizeIfNeeded(originalCallNode.RightOperand)
    }
);

// PhpDereferenceableExpressionAst is the AST node for a parenthesized "(expr)".
// Note: AST nodes do not retain their ParserRuleContext, so source location is
// copied from the original node. The existing Base2Ast.SetContext(Base2Ast) overload
// copies Line/Column/StartIndex/LanguageMode; expose a small factory/setter that uses it
// (this story adds AST mutation helpers as needed — see "AST Mutability" above).
var replacement = PhpDereferenceableExpressionAst.CreateFrom(
    expression: inlinedExpression,
    locationSource: originalCallNode   // copy Line/Column/StartIndex from the call site
);
replacement.OriginalAst = originalCallNode; // provenance for the sourcemap (Story 17)
```

**Edge cases:**

- Chained operators (`$a + $b + $c`) — each operator call is inlined independently. The result of `$a + $b` becomes the receiver for the next operation.
- Mixed operator types — if `$a + $b` uses extension operator but `$result - $c` uses a class-level operator, only the extension operator call is inlined.
- Null-safe operators — if the underlying method call is null-safe (`$left?->add($right)`), preserve the null-safe syntax.
- Parameter used multiple times — if a parameter appears more than once in the expression and the argument has side effects (function call, increment, etc.), the call site is skipped (not inlined) to prevent duplicate evaluation.
- Parenthesization — the entire inlined expression is wrapped in parentheses to prevent operator precedence bugs. Individual parameter substitutions are also parenthesized when the argument is a complex expression.

### Acceptance Criteria

- [ ] Module has `ConfigKey = "extensionOperatorInlining"`, `Priority = 100`, `MinimumLevel = Basic`
- [ ] Single return statement bodies with any expression (method calls, operators, concatenation, object construction) are detected as inlineable
- [ ] Call sites are rewritten by substituting parameters and wrapping in parentheses
- [ ] Visibility rules are enforced: public API methods in library projects are not inlined (call site is, but the method definition remains)
- [ ] Source location metadata is preserved on replacement nodes
- [ ] Metrics report the number of inlined call sites
- [ ] Verbose diagnostics emit `OptimizerInlinedExtensionOperator` for each inlined call
- [ ] Chained operators are handled correctly
- [ ] Multi-statement bodies (variable declarations, conditionals, multiple returns) are NOT inlined
- [ ] Parameters used multiple times with side-effecting arguments are NOT inlined (safety rule)

### Dependencies

- **Requires:** Phase 1 (framework), Story 03 (extension operator AST nodes and symbols)
- **Provides:** Inlined extension operators for Phase 4 (synthetic class elimination)

---

## Phase 3: Extension Method Inlining Module




### Phase Overview

Implement inlining for extension method bodies that consist of a single statement. The pattern is the same as extension operator inlining (Phase 2) — detect single-statement bodies and rewrite call sites by substituting the body expression directly, wrapped in parentheses to preserve precedence.

### Deliverables

- `Tyhp/TyhpLang/Optimizer/Modules/ExtensionMethodInliningModule.cs`

### Implementation Details

**What gets inlined:**

An extension method is a candidate for inlining when:

1. The method body is a **single return statement** (or single expression statement for void methods) containing any expression.
2. `$this` and all parameters are each referenced **at most once** in the expression, OR the corresponding call-site argument is a safe-to-duplicate expression (simple variable, literal, or class/enum constant — see Phase 2 for the complete classification).
3. The method is not part of the library's public API surface (or the call site is within the same library).

The key distinction is the same as Phase 2: **single-statement bodies are inlineable; multi-statement bodies are not.** The expression within the return statement can contain operators, string concatenation, method calls, object construction — anything. The optimizer wraps the inlined expression in parentheses at the call site.

**Example 1 — simple delegation (inlineable):**

```
extension function getBalance(): decimal {
    return $this->balance();
}
```

Call site `$bal = $account->getBalance();` inlines to:

```php
$bal = ($account->balance());
```

**Example 2 — expression with operators (also inlineable):**

```
extension function formatCurrency(): string {
    return '$' . $this->toFixed(2);
}
```

Call site `$text = $amount->formatCurrency();` inlines to:

```php
$text = ('$' . ($amount)->toFixed(2));
```

The `$this` reference is substituted with the receiver expression (`$amount`), parenthesized when needed, and the entire inlined expression is wrapped in parentheses.

**Example 3 — NOT inlineable (multi-statement body):**

```
extension function formatCurrency(): string {
    ?string $symbol = $this->getSymbol();
    if (empty($symbol)) {
        return $this->toFixed($this->getNonCurrencyPrecision());
    }
    return $symbol . $this->toFixed(2);
}
```

This has variable declarations, conditionals, and multiple return paths — it cannot be inlined. The synthetic static method is preserved.

**Detection algorithm:**

1. Walk all bound AST trees looking for call sites that resolve to extension method symbols.
2. For each such call site, look up the method's body.
3. Check if the body consists of exactly one statement that is a `ReturnStatementAst` (or a single `ExpressionStatementAst` for void methods).
4. Check the parameter/`$this` safety rule: each must be referenced at most once, or the corresponding argument is side-effect-free.
5. Apply visibility rules.
6. Rewrite the call site by substituting `$this` with the receiver and parameters with arguments, wrapping in parentheses.

**AST transformation:**

Similar to Phase 2 — replace the call site AST node with the inlined expression, substituting `$this` with the receiver and parameters with their arguments, wrapped in parentheses.

Extension method call sites are identified by walking all AST nodes whose `BoundSymbol` (from Story 05) is an `ObjectMethodSymbol` that belongs to an extension class. Extension calls remain as regular method calls in the AST — the `BoundSymbol` is what identifies them as extension-resolved. There is no `ExtensionMethodCallAst` node type; the optimizer uses the bound symbol information to detect these call sites.

### Acceptance Criteria

- [ ] Module has `ConfigKey = "extensionMethodInlining"`, `Priority = 200`, `MinimumLevel = Basic`
- [ ] Single return statement bodies with any expression are detected as inlineable
- [ ] Single expression statement bodies (void return) are detected as inlineable
- [ ] Call sites are rewritten by substituting `$this`/parameters and wrapping in parentheses
- [ ] Visibility rules are enforced
- [ ] Multi-statement bodies (variable declarations, conditionals, multiple returns) are NOT inlined
- [ ] Parameters/`$this` used multiple times with side-effecting arguments are NOT inlined (safety rule)
- [ ] Source location metadata is preserved
- [ ] Metrics report the number of inlined call sites
- [ ] Verbose diagnostics emit `OptimizerInlinedExtensionMethod` for each inlined call

### Dependencies

- **Requires:** Phase 1 (framework), Story 03 (extension method AST nodes and symbols)
- **Provides:** Inlined extension methods for Phase 4 (synthetic class elimination)

---

## Phase 4: Synthetic Extension Class Elimination Module




### Phase Overview

After extension operator and method inlining (Phases 2–3), some synthetic extension classes may have **all** of their members inlined at every call site. In application projects (or for non-public members in library projects), these classes serve no purpose and can be eliminated from the emitted output entirely.

### Deliverables

- `Tyhp/TyhpLang/Optimizer/Modules/SyntheticClassEliminationModule.cs`

### Implementation Details

**When a synthetic class can be eliminated:**

1. The class is a compiler-generated synthetic extension class (e.g., `__TyhpInlineExt_Decimal`).
2. **All** static methods on the class have been inlined at **every** call site by Phases 2–3.
3. No remaining references to the class exist in any AST tree (no call sites, no reflection references, no type references).
4. In library projects: the class must NOT be part of the public API (i.e., none of its methods are referenced by external consumers via `package.tyhp.json`). Since `package.tyhp.json` is generated from the unoptimized AST, this effectively means: in library projects, synthetic extension classes whose methods are exposed in the `package.tyhp.json` **must be kept**.

**Detection algorithm:**

1. Collect all synthetic extension class declarations from the AST.
2. For each class, check if any method still has un-inlined call sites.
3. For library projects, check if any method is referenced in the `package.tyhp.json` manifest (i.e., has a public visibility that would be exported).
4. If no remaining references exist, mark the class for elimination.

**AST transformation:**

Remove the class declaration AST node from the file's statement list. This prevents the emitter from generating the PHP class file.

### Acceptance Criteria

- [ ] Module has `ConfigKey = "syntheticClassElimination"`, `Priority = 300`, `MinimumLevel = Basic`
- [ ] Synthetic extension classes with zero remaining call sites are eliminated
- [ ] Library projects preserve synthetic classes that have public-facing methods
- [ ] Application projects can eliminate all fully-inlined synthetic classes
- [ ] Metrics report the number of eliminated classes
- [ ] Verbose diagnostics emit `OptimizerEliminatedSyntheticClass` for each

### Dependencies

- **Requires:** Phase 2 (extension operator inlining), Phase 3 (extension method inlining) — must run after these due to `Priority = 300`
- **Provides:** Cleaner emitter output with fewer unnecessary classes

---

## Phase 5: `#[\Tyhp\Optimize\Inline]` Attribute Support




### Phase Overview

Support the `#[\Tyhp\Optimize\Inline]` attribute as a compile-time hint that a method or operator should be inlined. This attribute is parsed by the Tyhp compiler during the bind/check phase and consumed by the optimizer to force inlining of methods that the automatic detection might not catch (or to explicitly request inlining for documentation/intent purposes).

All Tyhp compiler attributes live under the `\Tyhp\Optimize\` namespace to avoid conflicts with PHP built-in attributes, third-party library attributes (e.g., PHPStan's `#[Pure]`), and user-defined attributes. Developers can use `use \Tyhp\Optimize\Inline;` to shorten the syntax. The compiler resolves attribute names using standard PHP name resolution rules.

### Deliverables

- `Tyhp/TyhpLang/Optimizer/Attributes/InlineAttribute.cs` — Attribute recognition logic
- Modifications to the extension inlining modules (Phases 2–3) to check for this attribute

### Implementation Details

**Syntax:**

```tyhp
use \Tyhp\Optimize\Inline;

#[Inline()]
extension function getBalance(): decimal {
    return $this->balance();
}

#[\Tyhp\Optimize\Inline()]
extension operator +(self $left, self $right): self {
    return $left->add($right);
}
```

The attribute uses standard PHP attribute syntax (`#[...]`) so it can be parsed by the existing ANTLR grammar. The Tyhp compiler recognizes `\Tyhp\Optimize\Inline` (resolved via standard PHP name resolution, including `use` imports) as a compile-time attribute — it is NOT emitted to the PHP output. Any attribute that resolves to a fully-qualified name under `\Tyhp\Optimize\` is checked against the known compiler attribute list (`Inline`, `Pure`, `Memoize`); unrecognized `\Tyhp\Optimize\*` attributes emit a warning.

**Behavior:**

1. During binding, `\Tyhp\Optimize\Inline` is recognized as a compiler-intrinsic attribute and stored on the method/operator symbol.
2. During the optimizer phase, the extension inlining modules check for this attribute.
3. If present, the module **requires** that the body is a single return statement (any expression is allowed — method calls, operators, concatenation, etc.). Multi-statement bodies (variable declarations, conditionals, multiple returns) do NOT qualify. If the body does not qualify, `OptimizerInlineAttributeInvalidBody` warning is emitted.
4. If the method is a public API member in a library project and cannot be inlined (external consumers need the static method), `OptimizerInlineAttributeNotApplicable` warning is emitted. The method is still inlined at **internal** call sites but the definition is preserved.
5. The `#[\Tyhp\Optimize\Inline]` attribute forces the optimizer to attempt inlining on annotated members when the optimizer runs. However, when `optimize: "none"` is specified and no individual module overrides enable the inlining modules, the optimizer does not run at all — and the Inline attribute (along with all other optimization attributes) is simply ignored. This is the expected behavior: `optimize: "none"` means "do not run the optimizer."

   To force inlining at `optimize: "none"`, the user must explicitly enable the inlining module via individual override (e.g., `"optimizations": { "extensionOperatorInlining": true }`), which causes the optimizer to run that specific module.

The `#[\Tyhp\Optimize\Inline]` attribute does NOT change the inlining rule — it simply forces the optimizer to attempt inlining on members it might otherwise skip (e.g., due to optimization level being lower than the module's minimum). The rule is always the same: single return statement = inlineable, multi-statement = not inlineable.

**Validation (checker integration):**

The checker (Story 08) should validate that `#[\Tyhp\Optimize\Inline]` is only applied to:
- Extension methods (`extension function`)
- Extension operators (`extension operator`)
- Private/internal methods with single-statement bodies

Applying it to other constructs (class declarations, properties, constants) emits a checker error. Applying it to a multi-statement body emits `OptimizerInlineAttributeInvalidBody` warning (not an error — the code still compiles, just without inlining).

### Acceptance Criteria

- [ ] `#[\Tyhp\Optimize\Inline]` (and short form `#[Inline]` with `use \Tyhp\Optimize\Inline;`) is recognized as a compile-time attribute during binding
- [ ] The attribute is NOT emitted to PHP output
- [ ] Extension methods/operators with this attribute are inlined even if they wouldn't be auto-detected
- [ ] Multi-statement bodies with the attribute emit `OptimizerInlineAttributeInvalidBody` warning
- [ ] Single-statement bodies with complex expressions (operators, concatenation) ARE inlined when this attribute is present
- [ ] Public API members in library projects with the attribute emit `OptimizerInlineAttributeNotApplicable` warning (but are still inlined at internal call sites)
- [ ] The attribute works with `optimize: "none"` when combined with individual module override
- [ ] The checker validates that the attribute is only applied to valid targets
- [ ] Unrecognized `\Tyhp\Optimize\*` attributes emit a warning

### Dependencies

- **Requires:** Phase 1 (framework), Phases 2–3 (inlining modules), Story 08 (checker for validation)
- **Provides:** Developer-directed inlining for extension methods/operators

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

Wire the optimizer into the build pipeline. Update `BuildAction` to run the optimizer between the checker and emitter phases. Update `CompilationResult` to report optimizer timing and metrics. Handle the interaction with `package.tyhp.json` generation (Story 20) — the tyhpdef must be generated from the **unoptimized** AST.

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
Step 7.5: Generate package.tyhp.json (if library project) — BEFORE optimization
Step 8: Run optimizer
Step 9: Run emitter
```

The `package.tyhp.json` generation step is placed **before** the optimizer to ensure the public API contract is captured from the unoptimized AST. This is critical: the `package.tyhp.json` must reflect what external consumers see, not what the optimizer has transformed internally.

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
3. This means that when a developer sees a PHP error on a line that was an inlined extension method call, the sourcemap correctly maps back to the `$a + $b` expression in their Tyhp source — not to the `$left->add($right)` body of the extension operator.

This interaction is documented here but the actual sourcemap modifications are part of Story 17's scope. Story 17 should handle `OriginalAst`-annotated nodes when generating mappings.

### Acceptance Criteria

- [ ] The optimizer runs between the checker error gate and the emitter in `BuildAction`
- [ ] `package.tyhp.json` generation (Story 20 placeholder) occurs BEFORE the optimizer
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

- **Requires:** Phase 1 (framework), Phases 2–6 (modules), Story 10 (build action)
- **Provides:** Fully integrated optimizer in the build pipeline

---

## Cross-Story References

### Stories That Must Be Updated

| Story | Update Required |
|-------|----------------|
| **Story 09 (Emitter)** | Update pipeline diagram to include optimizer between checker and emitter. Add note that the emitter receives an already-optimized AST. |
| **Story 10 (Build Action)** | Add optimizer step to the pipeline flow diagram and `BuildAction` implementation. Add `OptimizationConfig`, `BuildProfileConfig` to config parsing. Add `OptimizeDuration` to timing summary. Add CLI argument handling for `--optimize`, `--optimize-enable`, `--optimize-disable`, `--profile`. |
| **Story 11 (Emitter Feature Expansion)** | Update project context to include the optimizer in the pipeline. Note that extension method/operator transformers may produce output that has already been partially optimized. |
| **Story 17 (Sourcemaps)** | Add handling for optimizer-replaced AST nodes. Replacement nodes carry an `OriginalAst` reference for source mapping. Stack traces for inlined code should map back to the Tyhp call site. |
| **Story 20 (Tyhpdef Generator)** | Confirm that `package.tyhp.json` is generated from the unoptimized AST. Document the ordering requirement: tyhpdef generation runs before the optimizer. |
| **Story 18 (XDebug Proxy)** | Note that inlined method calls will not appear in PHP stack traces. The XDebug proxy should use sourcemaps to reconstruct the original Tyhp call stack when displaying to the developer. |
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

**Expected:** The verbose output should show the optimizer running with modules at the `basic` level (extension operator inlining, extension method inlining, synthetic class elimination, constant folding, dead code elimination).

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

### Step 5: Verify Extension Operator Inlining

Create a tyhpdef that declares an extension operator with a single-statement body. Then create a Tyhp source file that uses it.

For example, assuming the `tyhp/decimal` package exposes `extension operator +` with body `return $left->add($right);`:

Create `test_opt_operator.tyhp`:

```tyhp
<?tyhp

use Tyhp\Decimal;

function testOperatorInlining(): void {
    Decimal $a = \Tyhp\decimal("10.5");
    Decimal $b = \Tyhp\decimal("20.3");
    Decimal $result = $a + $b;
    echo $result;
}
```

Run without optimization:

```bash
tyhp build --optimize=none --verbose
```

Inspect the output — it should contain a static call like `__TyhpInlineExt_Decimal::__OP_Decimal_ADD_Decimal($a, $b)`.

Run with optimization:

```bash
tyhp build --optimize=basic --verbose
```

Inspect the output. **Expected:**

- The static call is replaced with the inlined expression: `$a->add($b)`
- Verbose output reports `OptimizerInlinedExtensionOperator` for the transformation
- The output PHP passes `php -l`

### Step 6: Verify Extension Method Inlining

Create a test file `test_opt_method.tyhp` that uses an extension method with a single-statement body:

```tyhp
<?tyhp

namespace App;

extension StringHelpers for string {
    public function shout(): string {
        return \strtoupper($this) . "!";
    }
}

function demo(): void {
    string $msg = "hello";
    echo $msg->shout();
}
```

Run without optimization:

```bash
tyhp build --optimize=none
```

Inspect output — should contain `StringHelpers::shout($msg)`.

Run with optimization:

```bash
tyhp build --optimize=basic --verbose
```

**Expected:**

- The call is inlined to `(\strtoupper($msg) . "!")`
- Verbose output reports `OptimizerInlinedExtensionMethod`
- Output passes `php -l`

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

### Step 9: Verify Synthetic Class Elimination

After extension operator and method inlining (Steps 5-6), verify that if ALL methods of a synthetic extension class have been inlined, the class itself is removed from the output.

Run with optimization on a file that uses extension operators:

```bash
tyhp build --optimize=basic --verbose
```

**Expected:** If all call sites of a synthetic class (e.g., `__TyhpInlineExt_Decimal`) were inlined, the class file is NOT generated in the output directory. Verbose output should report `OptimizerEliminatedSyntheticClass`.

### Step 10: Verify Multi-Statement Bodies Are NOT Inlined

Create a test with a multi-statement extension method:

```tyhp
<?tyhp

namespace App;

extension StringHelpers for string {
    public function safeUpper(): string {
        if (\strlen($this) === 0) {
            return "";
        }
        return \strtoupper($this);
    }
}

function demo(): void {
    string $msg = "hello";
    echo $msg->safeUpper();
}
```

Run:

```bash
tyhp build --optimize=basic --verbose
```

**Expected:** The `safeUpper()` method is NOT inlined because it has multiple statements (if/return/return). The output should contain the static call `StringHelpers::safeUpper($msg)` and the StringHelpers class should be generated.

### Step 11: Verify `#[\Tyhp\Optimize\Inline]` Attribute

Create `test_opt_inline_attr.tyhp`:

```tyhp
<?tyhp

namespace App;

use \Tyhp\Optimize\Inline;

extension MathHelpers for int {
    #[Inline()]
    public function doubled(): int {
        return $this * 2;
    }
}

function demo(): void {
    int $x = 5;
    int $y = $x->doubled();
}
```

Run:

```bash
tyhp build --optimize=basic --verbose
```

**Expected:**

- The method is inlined: `$y = ($x * 2);`
- The `#[Inline]` attribute does NOT appear in the PHP output
- Verbose output reports `OptimizerInlinedAnnotatedMember`

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
