# Implementation Plan: Story 29 — Tyhp Reflection API (Sourcemap-Backed Runtime Reflection)

> **Roadmap position:** Story 29 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** 17, 23, 04, 20, 03, 19, 25, 26, 27, 28
> **Renumbered from:** legacy Story 22
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 29 of the Tyhp compiler TODO
> **Branch:** TBD
> **Generated:** 2026-03-19
> **Prerequisites:** Story 17 (Sourcemap Generation), Story 23 (Compiler Optimizer MVP), Story 04 (Tyhp Runtime Library Modules), Story 20 (Tyhpdef Generator — Track C for reflection metadata emission), Story 03 (Extension operator overloads and inline extensions), Story 19 (`Base2Ast.EndLine`/`EndColumn` end-position properties — required for `sourceEndLine` in metadata; see Phase 1), Story 25 (`IsInternal` symbol property — required for `ReflectionClass.isInternal()`), Story 26 (Null-conditional assignment features), Story 27 (`new<TArgs>` constructable type features), Story 28 (Generic parameter defaults — used in `getGenericParameters()`)

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Phase 1: Reflection Metadata Format and Emitter Integration](#phase-1-reflection-metadata-format-and-emitter-integration)
- [Phase 2: Runtime Metadata Loader](#phase-2-runtime-metadata-loader)
- [Phase 3: `\Tyhp\Reflection\ReflectionClass`](#phase-3-tyhpreflectionreflectionclass)
- [Phase 4: `\Tyhp\Reflection\ReflectionMethod` and `ReflectionFunction`](#phase-4-tyhpreflectionreflectionmethod-and-reflectionfunction)
- [Phase 5: `\Tyhp\Reflection\ReflectionProperty` and `ReflectionConstant`](#phase-5-tyhpreflectionreflectionproperty-and-reflectionconstant)
- [Phase 6: `\Tyhp\Reflection\ReflectionParameter` and `ReflectionType`](#phase-6-tyhpreflectionreflectionparameter-and-reflectiontype)
- [Phase 7: Extension Method and Operator Reflection](#phase-7-extension-method-and-operator-reflection)
- [Phase 8: Stack Trace Reconstruction](#phase-8-stack-trace-reconstruction)
- [Phase 9: Integration Testing and Documentation](#phase-9-integration-testing-and-documentation)
- [Cross-Story References](#cross-story-references)

---

## Architecture Overview

### The Problem

When the Tyhp compiler optimizes code (Story 23), the resulting PHP output may differ structurally from the original Tyhp source. Extension methods are inlined away, synthetic dispatch classes are eliminated, operator overloads become direct method calls, generic type parameters are erased, and dead code is removed. PHP's built-in reflection API (`\ReflectionClass`, `\ReflectionMethod`, etc.) inspects the compiled PHP — not the original Tyhp source — meaning it reports an incomplete or misleading picture of the code's actual structure.

For example, consider a Tyhp class with extension methods and operator overloads. After optimization:

```tyhp
// Original Tyhp source
class Account {
    private decimal $balance;

    function getBalance(): decimal {
        return $this->balance;
    }
}

extension AccountExtensions {
    extension function formatBalance(extends Account $this): string {
        return '$' . $this->getBalance()->toFixed(2);
    }
}
```

PHP reflection on the compiled output would show `Account` with only `getBalance()` — it knows nothing about `formatBalance()` because that lives on a separate `__TyhpExt_AccountExtensions` class (or was inlined away entirely). It also can't report that `Account` has generic type parameters, operator overloads, or the Tyhp-specific modifiers.

### The Solution: Tyhp Reflection API

The Tyhp Reflection API is a set of runtime classes (written in Tyhp, compiled to PHP, distributed as part of `tyhp/core`) that provide **accurate reflection of the original Tyhp source structure**, regardless of compilation and optimization. They achieve this by loading **reflection metadata** — a companion JSON file emitted alongside the sourcemap during compilation — that describes the complete Tyhp class/function/type structure as it existed in the source.

```
┌────────────────────────────────────────────────────────────────────┐
│  Compile Time                                                      │
│                                                                    │
│  Tyhp Source → Checker → [Reflection Metadata Emitter] → Optimizer │
│                              │                                     │
│                              ▼                                     │
│                  .tyhp.meta.json files                              │
│                  (per-file or per-package)                          │
│                                                                    │
│  NOTE: Metadata is emitted from the UNOPTIMIZED AST at the same   │
│  pipeline step as `package.tyhp.json`. Inlining/optimization       │
│  status is NOT stored in metadata — it is detected at runtime by   │
│  comparing metadata against PHP reflection.                        │
└────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────┐
│  Runtime                                                           │
│                                                                    │
│  \Tyhp\Reflection\ReflectionClass('App\Account')                   │
│      │                                                             │
│      ├── Loads .tyhp.meta.json for the target class                │
│      ├── Loads .php.map sourcemap for source location mapping      │
│      ├── Optionally delegates to PHP \ReflectionClass for runtime  │
│      │   instance inspection                                       │
│      │                                                             │
│      ▼                                                             │
│  Returns accurate Tyhp-level reflection:                           │
│  - All methods (including extension methods from all extensions)   │
│  - All operators (with Tyhp signatures, not __OP_* names)          │
│  - Generic type parameters with constraints                        │
│  - Original source file paths and line numbers (.tyhp, not .php)   │
│  - Visibility, modifiers, attributes                               │
│  - Inlined members (shown even if eliminated from PHP)             │
└────────────────────────────────────────────────────────────────────┘
```

### Design Principles

1. **Mirrors PHP reflection API surface.** Every Tyhp reflection class has the same method names and signatures as its PHP counterpart where applicable, plus additional Tyhp-specific methods. A developer familiar with `\ReflectionClass` can use `\Tyhp\Reflection\ReflectionClass` without learning a new API.

2. **Sourcemap + metadata, not PHP introspection.** Tyhp reflection does NOT rely on PHP's reflection for structural information. It reads metadata files. PHP reflection is only used optionally for runtime instance inspection (e.g., reading actual property values from a live object).

3. **Works regardless of optimization level.** Whether the code was compiled with `optimize: "none"` or `optimize: "aggressive"`, the reflection metadata is identical — it always describes the original Tyhp source structure.

4. **Metadata emitted before optimization.** The reflection metadata is generated from the unoptimized, checked AST (same pipeline timing as auto-generated `package.tyhp.json` manifest generation), guaranteeing it captures the full source structure including members that the optimizer later inlines or eliminates.

5. **Lazy loading.** Metadata files are loaded on demand — only when reflection is actually used. This imposes zero overhead on applications that don't use reflection.

6. **Compatible with Composer distribution.** Metadata files are included in the Composer package alongside the compiled PHP and sourcemaps. The loader discovers them using the same autoload path conventions.

### Tyhp Reflection vs PHP Reflection Comparison

| Feature | PHP Reflection | Tyhp Reflection |
|---------|---------------|-----------------|
| Class methods | Only compiled PHP methods | All methods including extension methods |
| Operator overloads | Visible as `__OP_*` / `__add` methods | Visible as typed operator declarations |
| Generic parameters | Erased (not visible) | Available with constraints and defaults |
| Extension methods | Not visible (on separate class) | Visible as methods on the target type |
| Inlined/eliminated members | Not visible | Visible (detected at runtime by comparing metadata against PHP reflection) |
| Source file location | Points to `.php` output | Points to original `.tyhp` source |
| Source line numbers | PHP output line numbers | Tyhp source line numbers |
| Type information | PHP type hints only | Full Tyhp type system (unions, intersections, generics, nullable, `decimal`, structs) |
| Struct types | Not visible (compiled to arrays) | Visible as struct declarations with properties |
| Type aliases | Erased | Visible with underlying type |
| `internal` modifier | Not visible in PHP | Visible |
| Attributes | PHP attributes only | PHP attributes + Tyhp compile-time attributes (marked as compile-time) |
| Stack traces | PHP call stack only | Reconstructed Tyhp call stack (including inlined calls) |
| Guaranteed with optimization | No | Yes |

### Runtime Package

The Tyhp reflection classes are distributed as part of the `tyhp/core` Composer package (already established in Story 04). They live under the `\Tyhp\Reflection\` namespace:

```
runtime/packages/core/tyhp_src/
├── Reflection/
│   ├── ReflectionClass.tyhp
│   ├── ReflectionMethod.tyhp
│   ├── ReflectionFunction.tyhp
│   ├── ReflectionProperty.tyhp
│   ├── ReflectionConstant.tyhp
│   ├── ReflectionParameter.tyhp
│   ├── ReflectionType.tyhp
│   ├── ReflectionNamedType.tyhp
│   ├── ReflectionUnionType.tyhp
│   ├── ReflectionIntersectionType.tyhp
│   ├── ReflectionGenericParameter.tyhp
│   ├── ReflectionOperator.tyhp
│   ├── ReflectionExtensionMethod.tyhp
│   ├── StackTrace.tyhp
│   ├── StackFrame.tyhp
│   └── MetadataLoader.tyhp
```

### Metadata File Format

The reflection metadata is a JSON file (`.tyhp.meta.json`) emitted alongside each PHP output file. For library projects, a single consolidated `package.tyhp.meta.json` can be emitted alongside `package.tyhp.json`.

```json
{
    "version": 1,
    "sourceFile": "src/Models/Account.tyhp",
    "classes": [
        {
            "name": "App\\Models\\Account",
            "kind": "class",
            "modifiers": ["public"],
            "generics": [],
            "extends": null,
            "implements": [],
            "sourceFile": "src/Models/Account.tyhp",
            "sourceLine": 5,
            "sourceEndLine": 45,
            "methods": [
                {
                    "name": "getBalance",
                    "visibility": "public",
                    "modifiers": [],
                    "returnType": "decimal",
                    "parameters": [],
                    "sourceLine": 9,
                    "sourceEndLine": 12,
                    "isExtension": false
                }
            ],
            "extensionMethods": [
                {
                    "name": "formatBalance",
                    "sourceExtension": "AccountExtensions",
                    "visibility": "public",
                    "returnType": "string",
                    "parameters": [],
                    "sourceLine": 15,
                    "sourceFile": "src/Extensions/AccountExtensions.tyhp"
                }
            ],
            "operators": [
                {
                    "operator": "+",
                    "leftType": "self",
                    "rightType": "self",
                    "returnType": "self",
                    "sourceLine": 20,
                    "isExtension": true,
                    "sourceExtension": "AccountExtensions"
                }
            ],
            "properties": [...],
            "constants": [...],
            "typeAliases": [...]
        }
    ],
    "functions": [...],
    "typeAliases": [...]
}
```

### File Organization

```
Tyhp/TyhpLang/Emitter/
├── ReflectionMetadataEmitter.cs          (~400 lines) — Phase 1: generates .tyhp.meta.json

runtime/packages/core/tyhp_src/
├── Reflection/
│   ├── MetadataLoader.tyhp               (~200 lines) — Phase 2
│   ├── ReflectionClass.tyhp              (~500 lines) — Phase 3
│   ├── ReflectionMethod.tyhp             (~250 lines) — Phase 4
│   ├── ReflectionFunction.tyhp           (~200 lines) — Phase 4
│   ├── ReflectionProperty.tyhp           (~150 lines) — Phase 5
│   ├── ReflectionConstant.tyhp           (~100 lines) — Phase 5
│   ├── ReflectionParameter.tyhp          (~150 lines) — Phase 6
│   ├── ReflectionType.tyhp               (~100 lines) — Phase 6
│   ├── ReflectionNamedType.tyhp          (~80 lines)  — Phase 6
│   ├── ReflectionUnionType.tyhp          (~80 lines)  — Phase 6
│   ├── ReflectionIntersectionType.tyhp   (~80 lines)  — Phase 6
│   ├── ReflectionGenericParameter.tyhp   (~100 lines) — Phase 6
│   ├── ReflectionOperator.tyhp           (~120 lines) — Phase 7
│   ├── ReflectionExtensionMethod.tyhp    (~120 lines) — Phase 7
│   ├── StackTrace.tyhp                   (~300 lines) — Phase 8
│   └── StackFrame.tyhp                   (~100 lines) — Phase 8
```

### MessageCode Numbering

Story 29 introduces diagnostic codes in the emitter range (5000s). Its canonical allocation is **5023–5024** (after Story 17's sourcemap codes `5020–5022`):

| Code | Name | Severity | Description |
|------|------|----------|-------------|
| 5023 | `ReflectionMetadataEmitFailed` | Warning | Failed to emit reflection metadata for a file |
| 5024 | `ReflectionMetadataInvalidFormat` | Warning | Reflection metadata file has an unrecognized format version |

Note: "Metadata not found" is a runtime concern, not a compile-time diagnostic. The runtime metadata loader handles missing files gracefully by returning `null` and using PHP reflection as a fallback. Runtime logging (e.g., via `trigger_error` or a Tyhp logging API) is used to inform developers, not the compiler's `MessageCode` enum.

### Runtime Package Compilation Strategy

The runtime packages (`tyhp/core`, `tyhp/decimal`, `tyhp/lambda`, `tyhp/async`) are written in Tyhp but are carefully authored to avoid using the very features they provide. This eliminates the circular dependency:

- **`tyhp/async`:** Uses `\Tyhp\Promise::_async(callable)` and `\Tyhp\Promise::_await(Promise)` wrapper calls directly instead of `async`/`await` syntax. The Tyhp source compiles to PHP that calls these methods without needing the async package's own syntax support.
- **`tyhp/core`:** Type system classes use standard PHP constructs. Generic tracking via `GenericObject` does not require the `GenericObject` trait itself to be generic.
- **Other packages:** Follow the same principle — implement features using lower-level PHP constructs that the compiled output would produce, avoiding any dependency on the package's own higher-level syntax.

**Bootstrap step:** Run `tyhp build` against the reflection Tyhp source files in `runtime/packages/core/tyhp_src/Reflection/` to produce the initial PHP output in the corresponding `runtime/packages/core/src/Reflection/` location. If the build command is not yet fully functional for the features used in the reflection library, manually translate the Tyhp source files to PHP:
1. Copy each `.tyhp` file to a corresponding `.php` file in `src/`
2. Convert Tyhp-specific syntax to PHP equivalents (extension methods → static helper calls, generic types → type comments, etc.)
3. Mark each manually translated file with a `// @tyhp-bootstrap: manually translated, regenerate with tyhp build` comment at the top
4. These files will be replaced by compiler output once the build command fully supports all required features

### Safety Notes

- The reflection metadata emitter runs on the UNOPTIMIZED AST (same timing as `package.tyhp.json` manifest generation)
- Metadata files must NOT contain sensitive information (source code bodies, private implementation details beyond signatures)
- The runtime metadata loader must handle missing metadata files gracefully (return empty/partial results, not crash)
- Before modifying `tyhp/core` package files, create timestamped backups
- Never use destructive git commands

---

## Phase 1: Reflection Metadata Format and Emitter Integration

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Define the reflection metadata JSON format and implement the emitter component that generates `.tyhp.meta.json` files from the checked, unoptimized AST. This emitter runs at the same pipeline position as auto-generated `package.tyhp.json` manifest generation (after checker, before optimizer) and is controlled by a new `build.generateReflectionMetadata` configuration option.

### Deliverables

- `Tyhp/TyhpLang/Emitter/ReflectionMetadataEmitter.cs` — Generates `.tyhp.meta.json` from bound AST
- Modified `Tyhp/Config/BuildConfig.cs` — Add `GenerateReflectionMetadata` config option
- Modified `Tyhp/CLI/BuildAction.cs` — Wire metadata emission at the pre-optimizer step
- Modified `Tyhp/Domain/Exceptions/MessageCode.cs` — Add codes 5023–5024 (emitter range)

### Implementation Details

**Configuration:**

- `build.generateReflectionMetadata` (`bool?`, default `null`) — resolved to `true` when `build.generateSourcemap` is `true` (since reflection metadata is most useful alongside sourcemaps), `false` otherwise. Can be explicitly set to override.
- When `build.profile` is `"release"` or `"balanced"`, reflection metadata defaults to `true`.
- When `build.profile` is `"debug"`, reflection metadata defaults to `true` (sourcemaps are on).

**`ReflectionMetadataEmitter.cs`:**

Walks the bound, checked AST (pre-optimization) and produces a JSON metadata document for each source file:

1. For each class/interface/trait/enum/struct declaration:
   - Emit name, kind, modifiers, generic parameters (with constraints and defaults), extends/implements
   - Emit all methods with signatures, parameters, return types, visibility, modifiers
   - Emit all properties with types, visibility, modifiers, defaults
   - Emit all constants with types and values
   - Emit all operator overloads with operator symbol, operand types, return type
   - Emit all type aliases with underlying type
   - Source location (file path, start line, end line) for each member — use `Base2Ast.Line` for `sourceLine` and `Base2Ast.EndLine` for `sourceEndLine` (end position properties added by Story 19 Phase 1)

2. For each extension declaration:
   - Walk all extension methods and operators
   - Resolve the target type for each extension member
   - Emit as `extensionMethods` / `operators` on the target class's metadata entry
   - Record the source extension name and file

3. For each standalone function:
   - Emit name, parameters, return type, generic parameters

4. For `use extension` declarations in tyhpdef:
   - Resolve which extensions are auto-activated for a class
   - Include their members in the class's metadata

**Pipeline position:**

```
Step 7.5 in BuildAction:
  1. Generate package.tyhp.json (if library project)
  2. Generate reflection metadata (if configured)  ← NEW
Step 8: Run optimizer
Step 9: Run emitter
```

**Output file naming:**

- Per-file: `{outputPhpFileName}.tyhp.meta.json` (alongside the corresponding `.php` output file; one metadata file per source file, matching the PHP output file naming convention)
- Per-package (library): `package.tyhp.meta.json` (consolidated, alongside `package.tyhp.json`)

### Acceptance Criteria

- [ ] `ReflectionMetadataEmitter` produces valid JSON metadata for classes, interfaces, traits, enums, structs, functions
- [ ] Extension methods and operators are included in the target class's metadata
- [ ] Generic parameters, constraints, and defaults are captured
- [ ] Source file paths and line numbers point to the original `.tyhp` source
- [ ] Metadata is generated from the UNOPTIMIZED AST (before optimizer runs)
- [ ] `build.generateReflectionMetadata` config option controls generation
- [ ] Library projects produce a consolidated `package.tyhp.meta.json`
- [ ] Metadata format version is `1` and parseable by the runtime loader (Phase 2)
- [ ] No source code bodies or implementation details are included in metadata

### Dependencies

- **Requires:** Story 01 (diagnostics), Story 02 (binder/symbols), Story 23 (pipeline ordering)
- **Provides:** Metadata files consumed by the runtime reflection classes (Phases 2–8)

---

## Phase 2: Runtime Metadata Loader

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the runtime metadata loader that discovers and parses `.tyhp.meta.json` files. This is the bridge between the compile-time metadata emitter and the runtime reflection classes.

### Deliverables

- `runtime/packages/core/tyhp_src/Reflection/MetadataLoader.tyhp`

### Implementation Details

**`MetadataLoader`:**

```tyhp
<?tyhp

namespace Tyhp\Reflection;

class MetadataLoader {
    private static array<string, mixed> $cache = [];
    private static ?string $metadataBasePath = null;

    static function configure(string $basePath): void { ... }

    static function loadForClass(string $className): ?array<string, mixed> { ... }

    static function loadForFile(string $filePath): ?array<string, mixed> { ... }

    private static function discoverMetadataPath(string $className): ?string { ... }

    private static function parseMetadata(string $path): array<string, mixed> { ... }
}
```

**Discovery strategy:**

1. Convert the class name to a PSR-4 file path (same algorithm as Composer autoloader).
2. Look for `.tyhp.meta.json` alongside the `.php` file.
3. If not found, check for `package.tyhp.meta.json` in the package root (for Composer-distributed libraries).
4. Cache loaded metadata in memory (static property) to avoid re-reading from disk.

**Fallback behavior:** If no metadata file is found, the loader returns `null`. The reflection classes handle this by returning partial results (using PHP reflection as a fallback for basic information) and logging an informational diagnostic.

### Acceptance Criteria

- [ ] `MetadataLoader` discovers `.tyhp.meta.json` files alongside compiled `.php` files
- [ ] `MetadataLoader` discovers `package.tyhp.meta.json` for Composer-distributed packages
- [ ] Loaded metadata is cached in memory
- [ ] Missing metadata files return `null` (no crash)
- [ ] Invalid metadata files (wrong version, malformed JSON) emit a log warning and return `null`
- [ ] `MetadataLoader::configure()` allows overriding the base discovery path

---

## Phase 3: `\Tyhp\Reflection\ReflectionClass`

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `High`

### Phase Overview

Implement the core reflection class that mirrors PHP's `\ReflectionClass` with Tyhp-specific enhancements.

### Deliverables

- `runtime/packages/core/tyhp_src/Reflection/ReflectionClass.tyhp`

### Implementation Details

**API surface (mirrors `\ReflectionClass` where applicable):**

```tyhp
<?tyhp

namespace Tyhp\Reflection;

class ReflectionClass {
    function __construct(string|object $objectOrClass) { ... }

    // --- Standard reflection methods (same as \ReflectionClass) ---
    function getName(): string { ... }
    function getShortName(): string { ... }
    function getNamespaceName(): string { ... }
    function isAbstract(): bool { ... }
    function isFinal(): bool { ... }
    function isInterface(): bool { ... }
    function isTrait(): bool { ... }
    function isEnum(): bool { ... }
    function isReadOnly(): bool { ... }
    function isAnonymous(): bool { ... }
    function isInstantiable(): bool { ... }
    function getParentClass(): ?ReflectionClass { ... }
    function getInterfaceNames(): array<string> { ... }
    function getInterfaces(): array<string, ReflectionClass> { ... }
    function implementsInterface(string $interface): bool { ... }
    function getTraitNames(): array<string> { ... }
    function getTraits(): array<string, ReflectionClass> { ... }
    function getMethods(?int $filter = null): array<ReflectionMethod> { ... }
    function getMethod(string $name): ReflectionMethod { ... }
    function hasMethod(string $name): bool { ... }
    function getProperties(?int $filter = null): array<ReflectionProperty> { ... }
    function getProperty(string $name): ReflectionProperty { ... }
    function hasProperty(string $name): bool { ... }
    function getConstants(?int $filter = null): array<string, mixed> { ... }
    function getConstant(string $name): mixed { ... }
    function hasConstant(string $name): bool { ... }
    function getReflectionConstants(?int $filter = null): array<ReflectionConstant> { ... }
    function getReflectionConstant(string $name): ReflectionConstant|false { ... }
    function getConstructor(): ?ReflectionMethod { ... }
    function getFileName(): string|false { ... }
    function getStartLine(): int|false { ... }
    function getEndLine(): int|false { ... }
    function getDocComment(): string|false { ... }
    function getAttributes(?string $name = null, int $flags = 0): array { ... }

    // --- Tyhp-specific methods ---

    /// Returns true if this is a Tyhp struct type.
    function isStruct(): bool { ... }

    /// Returns true if this class has the Tyhp `internal` visibility modifier.
    function isInternal(): bool { ... }

    /// Returns the original .tyhp source file path (not the compiled .php path).
    function getTyhpFileName(): string|false { ... }

    /// Returns the line number in the original .tyhp source file.
    function getTyhpStartLine(): int|false { ... }

    /// Returns the end line number in the original .tyhp source file.
    function getTyhpEndLine(): int|false { ... }

    /// Returns generic type parameters declared on this class.
    function getGenericParameters(): array<ReflectionGenericParameter> { ... }

    /// Returns true if this class has generic type parameters.
    function hasGenericParameters(): bool { ... }

    /// Returns all extension methods registered for this class
    /// (from all extensions that target this class).
    function getExtensionMethods(): array<ReflectionExtensionMethod> { ... }

    /// Returns a specific extension method by name.
    function getExtensionMethod(string $name): ?ReflectionExtensionMethod { ... }

    /// Returns true if an extension method with the given name exists.
    function hasExtensionMethod(string $name): bool { ... }

    /// Returns all operator overloads declared on this class.
    function getOperators(): array<ReflectionOperator> { ... }

    /// Returns a specific operator by symbol (e.g., "+", "-", "==").
    function getOperator(string $symbol): ?ReflectionOperator { ... }

    /// Returns true if this class has an overload for the given operator.
    function hasOperator(string $symbol): bool { ... }

    /// Returns type aliases declared within this class.
    function getTypeAliases(): array<string, string> { ... }

    /// Returns ALL methods: regular methods + extension methods.
    /// This provides the complete Tyhp-level view of available methods.
    function getAllMethods(?int $filter = null): array<ReflectionMethod> { ... }
}
```

**Data sourcing priority:**

1. Tyhp reflection metadata (`.tyhp.meta.json`) — primary source for all structural information
2. PHP reflection (`\ReflectionClass`) — fallback for basic info if metadata is unavailable; used for runtime instance inspection

**`getMethods()` behavior:**

Unlike PHP's `\ReflectionClass::getMethods()` which only returns methods compiled directly onto the PHP class, the Tyhp version also includes:
- Extension methods (from all extensions that target this class)
- Methods that were inlined or eliminated by the optimizer (detected at runtime by comparing metadata against PHP reflection — if a method exists in metadata but not in `\ReflectionClass::getMethods()`, it was inlined/eliminated)
- Methods from used traits (same as PHP)

Methods from extensions are returned as `ReflectionMethod` instances with `isExtension() === true`.

**`getFileName()`, `getStartLine()`, and `getEndLine()` behavior:**

These methods return the **compiled PHP** file path and line numbers, maintaining API compatibility with PHP's `\ReflectionClass`. This ensures that code which switches from `\ReflectionClass` to `\Tyhp\Reflection\ReflectionClass` continues to work without modification.

For original Tyhp source locations, use the Tyhp-specific methods:
- `getTyhpFileName()` — original `.tyhp` source file path
- `getTyhpStartLine()` — start line in the Tyhp source (from metadata `sourceLine`)
- `getTyhpEndLine()` — end line in the Tyhp source (from metadata `sourceEndLine`, populated from `Base2Ast.EndLine` added by Story 19 Phase 1)

### Acceptance Criteria

- [ ] `ReflectionClass` can be constructed with a class name string or an object instance
- [ ] All standard `\ReflectionClass` methods return correct results
- [ ] `getFileName()` and `getStartLine()` return PHP output locations (API-compatible with `\ReflectionClass`)
- [ ] `getTyhpFileName()`, `getTyhpStartLine()`, and `getTyhpEndLine()` return original Tyhp source locations
- [ ] `getGenericParameters()` returns generic type parameters with constraints
- [ ] `getExtensionMethods()` returns extension methods from all targeting extensions
- [ ] `getOperators()` returns operator overloads with Tyhp signatures
- [ ] `isStruct()` correctly identifies struct types
- [ ] `isInternal()` correctly identifies internal visibility
- [ ] When metadata is unavailable, falls back to PHP reflection for basic info
- [ ] `getAllMethods()` returns the union of regular methods and extension methods

---

## Phase 4: `\Tyhp\Reflection\ReflectionMethod` and `ReflectionFunction`

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement method and function reflection classes.

### Deliverables

- `runtime/packages/core/tyhp_src/Reflection/ReflectionMethod.tyhp`
- `runtime/packages/core/tyhp_src/Reflection/ReflectionFunction.tyhp`

### Implementation Details

**`ReflectionMethod` API (mirrors `\ReflectionMethod` + Tyhp extras):**

```tyhp
class ReflectionMethod {
    // Standard methods (same as \ReflectionMethod)
    function getName(): string { ... }
    function getDeclaringClass(): ReflectionClass { ... }
    function isPublic(): bool { ... }
    function isProtected(): bool { ... }
    function isPrivate(): bool { ... }
    function isStatic(): bool { ... }
    function isAbstract(): bool { ... }
    function isFinal(): bool { ... }
    function isConstructor(): bool { ... }
    function isDestructor(): bool { ... }
    function getReturnType(): ?ReflectionType { ... }
    function getParameters(): array<ReflectionParameter> { ... }
    function getNumberOfParameters(): int { ... }
    function getNumberOfRequiredParameters(): int { ... }
    function getDocComment(): string|false { ... }
    function getFileName(): string|false { ... }
    function getStartLine(): int|false { ... }

    // Tyhp-specific
    function isExtension(): bool { ... }
    function isAsync(): bool { ... }
    function isGenerator(): bool { ... }
    function isInternal(): bool { ... }
    function isInlined(): bool { ... }
    function getGenericParameters(): array<ReflectionGenericParameter> { ... }
    function hasGenericParameters(): bool { ... }
    function getTyhpFileName(): string|false { ... }
    function getTyhpStartLine(): int|false { ... }
    function getSourceExtensionName(): ?string { ... }
    function getTyhpReturnType(): ?ReflectionType { ... }
}
```

`ReflectionFunction` follows the same pattern for standalone functions.

### Acceptance Criteria

- [ ] `ReflectionMethod` reports correct visibility, modifiers, parameters, and return types
- [ ] `isExtension()` correctly identifies extension methods
- [ ] `isInlined()` correctly identifies methods that were inlined by the optimizer
- [ ] `isAsync()` correctly identifies async methods
- [ ] Generic parameters are available on generic methods
- [ ] Tyhp source locations are reported (not PHP output locations)
- [ ] `getSourceExtensionName()` returns the extension class name for extension methods

---

## Phase 5: `\Tyhp\Reflection\ReflectionProperty` and `ReflectionConstant`

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Implement property and constant reflection classes.

### Deliverables

- `runtime/packages/core/tyhp_src/Reflection/ReflectionProperty.tyhp`
- `runtime/packages/core/tyhp_src/Reflection/ReflectionConstant.tyhp`

### Implementation Details

**Tyhp-specific additions on `ReflectionProperty`:**
- `hasAccessor(): bool` — whether the property has get/set accessors
- `getAccessorType(): ?string` — `"get"`, `"set"`, `"both"`, or `null`
- `isPromoted(): bool` — whether the property was promoted from a constructor parameter
- `getTyhpType(): ?ReflectionType` — the full Tyhp type (may be richer than the PHP type hint)

**`ReflectionConstant` Tyhp-specific additions:**
- `getTyhpType(): ?ReflectionType` — the typed constant's type (Tyhp requires types on constants)

### Acceptance Criteria

- [ ] Properties report correct Tyhp types (including generic types, union types, etc.)
- [ ] Accessor information (get/set hooks) is reflected
- [ ] Promoted properties are identified
- [ ] Constants report their Tyhp-typed types

---

## Phase 6: `\Tyhp\Reflection\ReflectionParameter` and `ReflectionType`

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement parameter and type reflection classes. The type reflection is particularly important for Tyhp since the type system is richer than PHP's.

### Deliverables

- `runtime/packages/core/tyhp_src/Reflection/ReflectionParameter.tyhp`
- `runtime/packages/core/tyhp_src/Reflection/ReflectionType.tyhp`
- `runtime/packages/core/tyhp_src/Reflection/ReflectionNamedType.tyhp`
- `runtime/packages/core/tyhp_src/Reflection/ReflectionUnionType.tyhp`
- `runtime/packages/core/tyhp_src/Reflection/ReflectionIntersectionType.tyhp`
- `runtime/packages/core/tyhp_src/Reflection/ReflectionGenericParameter.tyhp`

### Implementation Details

**`ReflectionType` hierarchy:**

```
ReflectionType (abstract)
├── ReflectionNamedType       — simple named type (string, int, MyClass) AND
│                               instantiated generics (MyClass<T1, T2>) via type arguments
├── ReflectionUnionType       — A|B|C
└── ReflectionIntersectionType — A&B&C
```

Generic type-parameter *declarations* (e.g. `T extends Comparable = int`) are NOT a `ReflectionType` subtype; they are represented by the separate `ReflectionGenericParameter` class (see below and the deliverables list). Instantiated generic types such as `Promise<string>` are represented by `ReflectionNamedType` carrying its resolved type arguments — there is no separate `ReflectionGenericType` class.

**`ReflectionGenericParameter`:**

Represents a generic type parameter declaration (e.g., `T extends Comparable`):

```tyhp
class ReflectionGenericParameter {
    function getName(): string { ... }           // e.g., "T"
    function getConstraint(): ?ReflectionType { ... } // e.g., Comparable
    function getDefault(): ?ReflectionType { ... }    // e.g., void (for Promise<T = void>)
    function isCovariant(): bool { ... }
    function isContravariant(): bool { ... }
    function getDeclaringClass(): ?ReflectionClass { ... }
    function getDeclaringFunction(): ?ReflectionMethod { ... }
}
```

**`ReflectionParameter` Tyhp additions:**
- `getTyhpType(): ?ReflectionType` — full Tyhp type (richer than `getType()`)
- `isExtensionTarget(): bool` — whether this parameter uses the `extends` keyword (extension method target)

### Acceptance Criteria

- [ ] `ReflectionType` hierarchy correctly represents Tyhp's type system
- [ ] Union types, intersection types, nullable types are properly reflected
- [ ] Generic type parameters report constraints and defaults
- [ ] Generic instantiated types (e.g., `Promise<string>`) are representable
- [ ] `decimal` type is properly reflected (not as a class, but as a semi-scalar)
- [ ] Struct types are properly reflected

---

## Phase 7: Extension Method and Operator Reflection

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement specialized reflection classes for extension methods and operator overloads — features unique to Tyhp that have no PHP reflection equivalent.

### Deliverables

- `runtime/packages/core/tyhp_src/Reflection/ReflectionExtensionMethod.tyhp`
- `runtime/packages/core/tyhp_src/Reflection/ReflectionOperator.tyhp`

### Implementation Details

**`ReflectionExtensionMethod`:**

```tyhp
class ReflectionExtensionMethod extends ReflectionMethod {
    function getTargetClass(): ReflectionClass { ... }
    function getSourceExtension(): string { ... }
    function getSourceExtensionFile(): string { ... }

    /// The PHP static method name this compiles to (e.g., "AccountExtensions::formatBalance").
    function getCompiledMethodName(): string { ... }

    /// Whether the optimizer inlined this method (eliminating the static dispatch).
    /// Determined at runtime by checking if the compiled method still exists in PHP reflection.
    function isInlined(): bool { ... }
}
```

**`ReflectionOperator`:**

```tyhp
class ReflectionOperator {
    function getOperatorSymbol(): string { ... }     // e.g., "+", "-", "==", "<=>"
    function getDeclaringClass(): ReflectionClass { ... }
    function getLeftOperandType(): ReflectionType { ... }
    function getRightOperandType(): ?ReflectionType { ... }  // null for unary operators
    function getReturnType(): ReflectionType { ... }
    function isUnary(): bool { ... }
    function isBinary(): bool { ... }
    function isComparison(): bool { ... }
    function isExtension(): bool { ... }
    function getSourceExtension(): ?string { ... }

    /// The PHP method name this compiles to (e.g., "__OP_Money_ADD_Money").
    function getCompiledMethodName(): string { ... }

    /// Determined at runtime by checking if the compiled operator method still exists in PHP reflection.
    function isInlined(): bool { ... }
    function getTyhpFileName(): string|false { ... }
    function getTyhpStartLine(): int|false { ... }
}
```

### Acceptance Criteria

- [ ] `ReflectionExtensionMethod` provides access to the source extension and target class
- [ ] `ReflectionOperator` provides operator symbol, operand types, and return type
- [ ] Both classes detect whether the member was inlined by comparing metadata against PHP reflection
- [ ] Both classes report the compiled PHP method name (for debugging)
- [ ] Unary and binary operators are correctly distinguished

---

## Phase 8: Stack Trace Reconstruction

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `High`

### Phase Overview

Implement a stack trace reconstruction facility that takes a PHP exception or `debug_backtrace()` output and maps it back to the original Tyhp source — including calls that were inlined by the optimizer. This is the user-facing feature that makes optimized Tyhp code debuggable.

### Deliverables

- `runtime/packages/core/tyhp_src/Reflection/StackTrace.tyhp`
- `runtime/packages/core/tyhp_src/Reflection/StackFrame.tyhp`

### Implementation Details

**`StackFrame`:**

```tyhp
class StackFrame {
    function getFile(): string { ... }         // Tyhp source file path
    function getLine(): int { ... }            // Tyhp source line number
    function getClass(): ?string { ... }       // Tyhp class name
    function getFunction(): ?string { ... }    // Tyhp function/method name (original name, not __OP_*)
    function getPhpFile(): string { ... }      // Compiled PHP file path
    function getPhpLine(): int { ... }         // Compiled PHP line number
    function isInlined(): bool { ... }         // Whether this frame was inlined (not in PHP stack)
    function getOperator(): ?string { ... }    // If the call was an operator, the operator symbol
    function isExtensionMethod(): bool { ... }
    function __toString(): string { ... }
}
```

**`StackTrace`:**

```tyhp
class StackTrace {
    /// Create a Tyhp stack trace from a PHP exception.
    static function fromException(\Throwable $exception): StackTrace { ... }

    /// Create a Tyhp stack trace from the current call point.
    static function current(): StackTrace { ... }

    /// Create a Tyhp stack trace from a debug_backtrace() array.
    static function fromBacktrace(array $backtrace): StackTrace { ... }

    function getFrames(): array<StackFrame> { ... }
    function __toString(): string { ... }
}
```

**Reconstruction algorithm:**

1. Take each PHP stack frame (file + line + class + function).
2. Look up the sourcemap for the PHP file.
3. Map the PHP line/column back to the Tyhp source position.
4. If the function name is an internal name (e.g., `__OP_Money_ADD_Money`), use the reflection metadata to resolve it back to the original Tyhp name (e.g., `operator +`).
5. Compare the metadata members against PHP reflection to detect inlined/eliminated calls at this source position — if a method exists in metadata but not in PHP reflection, reconstruct the "virtual" frame that would have existed in the unoptimized call stack.
6. Build a `StackFrame` for each entry.

**Example output:**

```
Tyhp Stack Trace:
  #0 src/Services/PaymentService.tyhp:42 — App\Services\PaymentService::processPayment()
  #1 src/Models/Invoice.tyhp:78 — App\Models\Invoice::calculateTotal()
  #2 [inlined] src/Models/Invoice.tyhp:78 — operator + (Money + Money) via MoneyExtensions
  #3 src/Controllers/CheckoutController.tyhp:23 — App\Controllers\CheckoutController::checkout()
```

Frame #2 is a "virtual" frame — it didn't exist in the PHP stack because the operator was inlined, but the Tyhp stack trace reconstructs it for the developer.

### Acceptance Criteria

- [ ] `StackTrace::fromException()` converts PHP exception stack to Tyhp stack frames
- [ ] `StackTrace::current()` captures the current Tyhp call stack
- [ ] PHP file paths are mapped to Tyhp source file paths
- [ ] PHP line numbers are mapped to Tyhp source line numbers
- [ ] Internal method names (`__OP_*`, `__TyhpExt_*`) are resolved to Tyhp names
- [ ] Inlined calls are reconstructed as virtual frames (marked with `isInlined()`)
- [ ] `__toString()` produces a human-readable stack trace
- [ ] Missing sourcemaps or metadata fall back to PHP stack info (not crash)

---

## Phase 9: Integration Testing and Documentation

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

End-to-end integration tests and developer documentation for the Tyhp Reflection API.

### Deliverables

- Integration test suite covering all reflection classes
- Developer documentation (docs/content update)
- Updated `runtime/packages/core/tyhp_src/` with any fixes from testing

### Implementation Details

**Test scenarios:**

1. **Basic class reflection** — reflect a simple class, verify methods, properties, constants
2. **Generic class reflection** — reflect a generic class, verify type parameters and constraints
3. **Extension method reflection** — reflect a class with extension methods, verify they appear in `getAllMethods()`
4. **Operator overload reflection** — reflect a class with operator overloads, verify they appear in `getOperators()`
5. **Optimized code reflection** — compile with `optimize: "aggressive"`, verify reflection still shows the full original structure (including inlined members)
6. **Stack trace reconstruction** — throw an exception in optimized code, verify the Tyhp stack trace shows the original call chain
7. **Library package reflection** — reflect a class from a Composer-distributed Tyhp library using `package.tyhp.meta.json`
8. **Struct reflection** — reflect a struct, verify `isStruct()` and property list
9. **Type reflection** — verify union types, intersection types, generic types, nullable types
10. **Missing metadata fallback** — verify graceful degradation when metadata files are absent

### Acceptance Criteria

- [ ] All 10 test scenarios pass
- [ ] Documentation covers the API surface, examples, and differences from PHP reflection
- [ ] Documentation explains when to use Tyhp reflection vs PHP reflection
- [ ] Documentation explains the optimizer interaction and reflection guarantee

---

## Cross-Story References

### Prerequisites

| Story | Relationship |
|-------|-------------|
| Story 17 (Sourcemaps) | Sourcemaps for PHP→Tyhp line/column mapping in stack traces (external `.php.map` files; compatible with Story 17's optional inline mode — see **Stories This Affects**). |
| Story 23 (Optimizer MVP) | The optimizer motivates this feature; metadata is emitted pre-optimization |
| Story 04 (Runtime Packages) | `tyhp/core` hosts the reflection runtime classes |
| Story 03 (Extensions) | Extension methods and operators are a key part of what Tyhp reflection exposes |
| Story 20 (Tyhpdef Generator) | Track C pipeline position for metadata emission timing |

### Stories This Affects

| Story | Impact |
|-------|--------|
| Story 23 (Optimizer) | Update cross-story references to point to Story 29 instead of "future story TBD" |
| Story 24 (Advanced Optimizations) | Phase 9 references this story for the full reflection API |
| Story 17 (Sourcemaps) | Story 29 stack reconstruction loads **external** `.php.map` files beside compiled PHP. Story 17 also supports **inline** sourcemaps as an option; the two do not conflict — implementations use external maps when present, otherwise follow the build's configured Story 17 sourcemap mode. |
| Story 18 (XDebug Proxy) | Can use Tyhp stack trace reconstruction for enhanced debugging |
| Story 04 (Runtime Packages) | `tyhp/core` package gains the `\Tyhp\Reflection\` namespace |

---

## Human Testing and Verification

> These steps are for a human developer to manually verify the implementation works end-to-end. Steps can be skipped or modified as needed — they are guidelines, not a rigid checklist.

### Step 1: Verify Reflection Metadata is Emitted

Create a small Tyhp project with the following `tyhp.json`:

```json
{
    "include": ["./**/*.tyhp"],
    "output": { "path": "./build" },
    "build": {
        "generateSourcemap": true,
        "generateReflectionMetadata": true
    }
}
```

Create a file `src/Account.tyhp`:

```tyhp
<?tyhp

namespace App;

class Account {
    private float $balance = 0.0;

    public function __construct(float $initialBalance = 0.0) {
        $this->balance = $initialBalance;
    }

    public function getBalance(): float {
        return $this->balance;
    }

    public function deposit(float $amount): void {
        $this->balance += $amount;
    }
}
```

Run `tyhp build`. **Expected:** A `.tyhp.meta.json` file is generated alongside the compiled PHP output. Open it and verify it contains:
- `"version": 1`
- A `classes` array with an entry for `App\Account`
- The entry has `methods` listing `__construct`, `getBalance`, `deposit` with their parameters and return types
- The entry has `properties` listing `$balance` with type `float` and visibility `private`
- Source line numbers point to the original `.tyhp` file positions

### Step 2: Verify `ReflectionClass` Basic Usage

Create a file `test_reflection.tyhp` that uses the Tyhp Reflection API:

```tyhp
<?tyhp

use Tyhp\Reflection\ReflectionClass;

// Reflect on a class by name
ReflectionClass $ref = new ReflectionClass('App\Account');

echo 'Name: ' . $ref->getName() . "\n";
echo 'Short name: ' . $ref->getShortName() . "\n";
echo 'Namespace: ' . $ref->getNamespaceName() . "\n";
echo 'Is abstract: ' . ($ref->isAbstract() ? 'yes' : 'no') . "\n";
echo 'Is interface: ' . ($ref->isInterface() ? 'yes' : 'no') . "\n";
echo 'Is struct: ' . ($ref->isStruct() ? 'yes' : 'no') . "\n";

// Check methods
echo "\nMethods:\n";
foreach ($ref->getMethods() as $method) {
    echo '  ' . $method->getName() . '()' . "\n";
}

// Check properties
echo "\nProperties:\n";
foreach ($ref->getProperties() as $prop) {
    echo '  $' . $prop->getName() . "\n";
}

// Tyhp source location
echo "\nTyhp source: " . $ref->getTyhpFileName() . "\n";
echo "Tyhp start line: " . $ref->getTyhpStartLine() . "\n";
```

Compile and run. **Expected:** The output shows the class name, namespace, methods (`__construct`, `getBalance`, `deposit`), properties (`balance`), and the original Tyhp source file path and line number.

### Step 3: Verify Extension Method Reflection

Create files that use extension methods and verify they appear in reflection.

`src/AccountExtensions.tyhp`:

```tyhp
<?tyhp

namespace App;

extension AccountExtensions {
    extension function formatBalance(extends Account $this): string {
        return '$' . \number_format($this->getBalance(), 2);
    }
}
```

`test_ext_reflection.tyhp`:

```tyhp
<?tyhp

use Tyhp\Reflection\ReflectionClass;

ReflectionClass $ref = new ReflectionClass('App\Account');

echo "Extension methods:\n";
foreach ($ref->getExtensionMethods() as $ext) {
    echo '  ' . $ext->getName() . '() from ' . $ext->getSourceExtension() . "\n";
}

echo "\nAll methods (regular + extension):\n";
foreach ($ref->getAllMethods() as $method) {
    string $suffix = $method->isExtension() ? ' [extension]' : '';
    echo '  ' . $method->getName() . '()' . $suffix . "\n";
}
```

Compile and run. **Expected:** `formatBalance` appears as an extension method from `AccountExtensions`, and `getAllMethods()` returns both the regular methods and the extension methods.

### Step 4: Verify Generic Parameter Reflection

Create a generic class and verify its generic parameters are reflected:

```tyhp
<?tyhp

namespace App;

use Tyhp\Reflection\ReflectionClass;

class Repository<TEntity, TId extends int|string = int> {
    public function find(TId $id): ?TEntity {
        return null;
    }
}

ReflectionClass $ref = new ReflectionClass('App\Repository');

echo "Generic parameters:\n";
foreach ($ref->getGenericParameters() as $gp) {
    string $constraint = $gp->getConstraint() !== null ? ' extends ' . $gp->getConstraint()->__toString() : '';
    string $default = $gp->getDefault() !== null ? ' = ' . $gp->getDefault()->__toString() : '';
    echo '  ' . $gp->getName() . $constraint . $default . "\n";
}
```

Compile and run. **Expected output** (approximately):

```
Generic parameters:
  TEntity
  TId extends int|string = int
```

### Step 5: Verify Stack Trace Reconstruction

Create a file that throws an exception and reconstructs the Tyhp stack trace:

```tyhp
<?tyhp

use Tyhp\Reflection\StackTrace;

function innerFunction(): void {
    throw new \RuntimeException('test error');
}

function outerFunction(): void {
    innerFunction();
}

try {
    outerFunction();
} catch (\Throwable $e) {
    StackTrace $trace = StackTrace::fromException($e);
    echo $trace->__toString() . "\n";
}
```

Compile and run. **Expected:** The stack trace shows Tyhp source file paths and line numbers (not PHP output paths). Function names are displayed as their original Tyhp names.

### Step 6: Verify Graceful Fallback When Metadata is Missing

Compile a Tyhp project with `generateReflectionMetadata` set to `false`, then attempt to use `ReflectionClass` on one of its classes. **Expected:** The reflection API should not crash. It should fall back to PHP's built-in reflection for basic information (method names, properties, etc.) and return `false` for Tyhp-specific information like `getTyhpFileName()`, `getGenericParameters()` (empty array), etc.

### Step 7: Verify Reflection After Optimization

If the optimizer (Story 23) is available, compile the `Account` example with `optimize: "aggressive"` and verify:
- The reflection metadata still shows the complete original structure
- Extension methods that were inlined are still visible via `getExtensionMethods()`
- Methods marked as inlined report `isInlined() === true`
- The metadata was generated from the unoptimized AST (structure matches the source, not the optimized output)

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
