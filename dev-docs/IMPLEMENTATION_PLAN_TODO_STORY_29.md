# Implementation Plan: Story 29 — Tyhp Reflection API (Sourcemap-Backed Runtime Reflection)

> **Roadmap position:** Story 29 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** 17, 23, 04, 20, 03, 19, 25, 26, 27, 28
> **Renumbered from:** legacy Story 22
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Scope:** Story 29 of the Tyhp compiler TODO
> **Branch:** TBD
> **Generated:** 2026-03-19
> **Prerequisites:** Story 17 (Sourcemap Generation), Story 23 (Compiler Optimizer MVP), Story 04 (Tyhp Runtime Library Modules), Story 20 (Tyhpdef Generator — Track C for reflection metadata emission), Story 03 (Extension operator overloads and inline extensions), Story 19 (`Base2Ast.EndLine`/`EndColumn` end-position properties — required for catalog `el`; see Phase 1), Story 25 (`IsInternal` symbol property — required for `ReflectionClass.isInternal()`), Story 26 (Null-conditional assignment features), Story 27 (`new<TArgs>` constructable type features), Story 28 (Generic parameter defaults — used in `getGenericParameters()`)

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

The Tyhp Reflection API is a set of runtime classes (written in Tyhp, compiled to PHP, distributed as part of `tyhp/core`) that provide **accurate reflection of the original Tyhp source structure**, regardless of compilation and optimization. They achieve this by loading **one dense reflection-metadata catalog per compilation** (not one file per PHP or Tyhp source). Sourcemaps stay one `.php.map` per generated PHP file (Source Map v3). Metadata is a separate catalog and is **not** embedded in those maps.

```
┌────────────────────────────────────────────────────────────────────┐
│  Compile Time                                                      │
│                                                                    │
│  Tyhp Source → Checker → [Reflection Metadata Emitter] → Optimizer │
│                              │                                     │
│                              ▼                                     │
│                  one catalog:                                      │
│                  application → {output.path}/tyhp.meta.json        │
│                  library     → package.tyhp.meta.json              │
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
│      ├── Loads the project/package metadata catalog (once)         │
│      ├── Looks up the class by FQN in that catalog                 │
│      ├── Loads .php.map sourcemap for stack-trace line mapping     │
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

2. **Sourcemap + metadata, not PHP introspection.** Tyhp reflection does NOT rely on PHP's reflection for structural information. It reads the metadata catalog. PHP reflection is only used optionally for runtime instance inspection (e.g., reading actual property values from a live object). Sourcemaps are for stack-trace PHP→Tyhp line mapping only; they do not carry the catalog.

3. **Works regardless of optimization level.** Whether the code was compiled with `optimize: "none"` or `optimize: "aggressive"`, the reflection metadata is identical — it always describes the original Tyhp source structure.

4. **Metadata emitted before optimization.** The reflection metadata is generated from the unoptimized, checked AST (same pipeline timing as auto-generated `package.tyhp.json` manifest generation), guaranteeing it captures the full source structure including members that the optimizer later inlines or eliminates.

5. **Lazy loading.** The catalog is loaded on first reflection use (then cached in memory and indexed by FQN). Applications that never reflect pay zero I/O.

6. **Compatible with Composer distribution.** Libraries ship one `package.tyhp.meta.json` at the package root (alongside `package.tyhp.json`), not a sidecar per PHP file. The loader walks up from the reflected class's PHP path until it finds a catalog.

7. **Dense JSON, not AST-cache binary.** The catalog is public, versioned (`v: 1`), and decoded in PHP inside `tyhp/core`. It is **not** the compiler's `AstCacheService` byte layout (node-type IDs, C# `Activator` deserialize, invalidated per compiler build). Density comes from interned strings, integer enums, omitted empty/default keys, and FQN-keyed maps — still `json_decode`.

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

**Always one catalog per compilation**, never a sidecar per PHP or Tyhp file, and never stuffed into `.php.map` (unknown `x_` keys are spec-legal but couple two different lifetimes and force the loader to parse VLQ maps to get signatures).

| Project `type` | Catalog path |
| --- | --- |
| `"application"` | `{output.path}/tyhp.meta.json` (e.g. `build/tyhp.meta.json`) |
| `"library"` | `package.tyhp.meta.json` at the package/dist root, alongside `package.tyhp.json` |

A large app is therefore **N PHP + N maps + 1 catalog**, not 3N files. Incremental builds rewrite that one catalog from the current unoptimized AST (cheap relative to emit).

**Density rules (format version `1`):**

- Intern repeated strings (source paths, type spellings, extension names) in a root `str` array; members store integer indexes into `str`.
- Key types and functions by FQN (`cls`, `fn`) — no `"name"` field on each entry.
- Integer enums: kind `k` (`0` class, `1` interface, `2` trait, `3` enum, `4` struct); visibility `vis` (`0` public, `1` protected, `2` private).
- Modifier bitflags `mod` when non-zero: `1` abstract, `2` final, `4` readonly, `8` internal, `16` static.
- **Omit** empty collections, `null` extends, default public visibility, `mod: 0`, and `isExtension: false`. Regular methods live in `m`; extension methods live in `xm` (do not duplicate a boolean on every method).
- Do **not** embed source bodies or method implementations.

```json
{
    "v": 1,
    "str": [
        "src/Models/Account.tyhp",
        "src/Extensions/AccountExtensions.tyhp",
        "decimal",
        "string",
        "self",
        "AccountExtensions"
    ],
    "cls": {
        "App\\Models\\Account": {
            "k": 0,
            "f": 0,
            "sl": 5,
            "el": 45,
            "m": {
                "getBalance": { "rt": 2, "sl": 9, "el": 12 }
            },
            "p": {
                "balance": { "vis": 2, "t": 2, "sl": 6 }
            },
            "xm": {
                "formatBalance": { "rt": 3, "sl": 15, "f": 1, "xe": 5 }
            },
            "op": [
                { "o": "+", "l": 4, "r": 4, "rt": 4, "sl": 20, "xe": 5 }
            ]
        }
    }
}
```

| Key | Meaning |
| --- | --- |
| `v` | Format version (`1`) |
| `str` | Intern table |
| `cls` / `fn` / `ta` | Types, free functions, file-level type aliases (omit if empty) |
| `k` | Kind enum |
| `f` | Source-file index into `str` |
| `sl` / `el` | 1-based Tyhp start/end line (`el` omitted if same as `sl` or unknown) |
| `g` | Generic parameters (omit if none) |
| `ext` / `impl` | Extends / implements (intern indexes; omit if none) |
| `m` / `p` / `c` / `xm` / `op` / `ta` | Methods, properties, constants, extension methods, operators, nested aliases |
| `vis` | Visibility enum (omit if `0` public) |
| `mod` | Modifier bits (omit if `0`) |
| `rt` / `t` | Return type / property type intern index |
| `o` / `l` / `r` | Operator symbol; left/right operand type intern indexes |
| `xe` | Source extension name intern index (extension members only) |

The runtime loader expands intern indexes when constructing `\Tyhp\Reflection\*` objects so the public API still returns paths and type names as strings (`getTyhpFileName()` → `src/Models/Account.tyhp`).

### File Organization

```
Tyhp/TyhpLang/Emitter/
├── ReflectionMetadataEmitter.cs          (~400 lines) — Phase 1: generates the dense catalog (tyhp.meta.json / package.tyhp.meta.json)

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
| 5023 | `ReflectionMetadataEmitFailed` | Warning | Failed to emit the reflection metadata catalog |
| 5024 | `ReflectionMetadataInvalidFormat` | Warning | Reflection metadata catalog has an unrecognized format version |

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
- The catalog must NOT contain sensitive information (source code bodies, private implementation details beyond signatures)
- The runtime metadata loader must handle a missing catalog gracefully (return empty/partial results, not crash)
- Before modifying `tyhp/core` package files, create timestamped backups
- Never use destructive git commands

---

## Phase 1: Reflection Metadata Format and Emitter Integration




### Phase Overview

Define the dense reflection-metadata catalog format and implement the emitter that writes **one JSON file per compilation** from the checked, unoptimized AST. This emitter runs at the same pipeline position as auto-generated `package.tyhp.json` manifest generation (after checker, before optimizer) and is controlled by a new `build.generateReflectionMetadata` configuration option.

### Deliverables

- `Tyhp/TyhpLang/Emitter/ReflectionMetadataEmitter.cs` — Builds intern tables and writes the single catalog
- Modified `Tyhp/Config/BuildConfig.cs` — Add `GenerateReflectionMetadata` config option
- Modified `Tyhp/CLI/BuildAction.cs` — Wire metadata emission at the pre-optimizer step
- Modified `Tyhp/Domain/Exceptions/MessageCode.cs` — Add codes 5023–5024 (emitter range)

### Implementation Details

**Configuration:**

- `build.generateReflectionMetadata` (`bool?`, default `null`) — resolved to `true` when `build.generateSourcemap` is `true` (since reflection metadata is most useful alongside sourcemaps), `false` otherwise. Can be explicitly set to override.
- When `build.profile` is `"release"` or `"balanced"`, reflection metadata defaults to `true`.
- When `build.profile` is `"debug"`, reflection metadata defaults to `true` (sourcemaps are on).

**`ReflectionMetadataEmitter.cs`:**

Walks the bound, checked AST (pre-optimization) and produces **one** dense JSON catalog for the whole compilation (all source files). Do not emit a sidecar next to each `.php`.

1. Intern every source path, type spelling, and extension name into `str` as it is first seen; store indexes on members.
2. For each class/interface/trait/enum/struct declaration, add an FQN-keyed entry under `cls`:
   - Emit `k`, `f`, `sl`/`el`, `g` (if any), `ext`/`impl` (if any), `mod` (if any)
   - Emit methods under `m`, properties under `p`, constants under `c`, operators under `op` — omit empty maps/arrays
   - Source location uses `Base2Ast.Line` for `sl` and `Base2Ast.EndLine` for `el` (end position properties added by Story 19 Phase 1)
3. For each extension declaration:
   - Resolve the target type; add members under that type's `xm` / `op` with `xe` (and `f` when the extension file differs from the target)
4. For each standalone function, add an FQN-keyed entry under `fn`.
5. For `use extension` declarations in tyhpdef, include those members on the target type as in (3).
6. Write the catalog once after the walk:
   - application → `{output.path}/tyhp.meta.json`
   - library → `package.tyhp.meta.json` at the package root (copy into dist next to `package.tyhp.json`)

**Pipeline position:**

```
Step 7.5 in BuildAction:
  1. Generate package.tyhp.json (if library project)
  2. Generate reflection metadata (if configured)  ← NEW
Step 8: Run optimizer
Step 9: Run emitter
```

**Output file naming:**

Always a single catalog (see [Metadata File Format](#metadata-file-format)). Never `{phpFile}.tyhp.meta.json`. Never embed the catalog in `.php.map`.

### Acceptance Criteria

- [ ] `ReflectionMetadataEmitter` produces one valid dense JSON catalog (`v: 1`, interned `str`, FQN-keyed `cls`/`fn`)
- [ ] Extension methods and operators are included on the target type (`xm` / `op`), not as a second catalog
- [ ] Generic parameters, constraints, and defaults are captured
- [ ] Source file paths (via `str` + `f`) and line numbers (`sl`/`el`) point to the original `.tyhp` source
- [ ] Metadata is generated from the UNOPTIMIZED AST (before optimizer runs)
- [ ] `build.generateReflectionMetadata` config option controls generation
- [ ] Applications write `{output.path}/tyhp.meta.json`; libraries write `package.tyhp.meta.json` (and dist copies it next to `package.tyhp.json`)
- [ ] No per-PHP or per-Tyhp sidecar metadata files are emitted
- [ ] Empty/default keys are omitted; no source code bodies are included
- [ ] Format version is `1` and parseable by the runtime loader (Phase 2)

### Dependencies

- **Requires:** Story 01 (diagnostics), Story 02 (binder/symbols), Story 23 (pipeline ordering)
- **Provides:** The single metadata catalog consumed by the runtime reflection classes (Phases 2–8)

---

## Phase 2: Runtime Metadata Loader




### Phase Overview

Implement the runtime metadata loader that discovers and parses the **single** catalog (`tyhp.meta.json` or `package.tyhp.meta.json`). This is the bridge between the compile-time metadata emitter and the runtime reflection classes.

### Deliverables

- `runtime/packages/core/tyhp_src/Reflection/MetadataLoader.tyhp`

### Implementation Details

**`MetadataLoader`:**

```tyhp
<?tyhp

namespace Tyhp\Reflection;

class MetadataLoader {
    private static array<string, mixed> $catalogCache = [];
    private static ?string $metadataBasePath = null;

    static function configure(string $basePath): void { ... }

    static function loadForClass(string $className): ?array<string, mixed> { ... }

    /// Load (or return cached) catalog that covers this compiled PHP file.
    static function loadCatalogForPhpFile(string $phpFilePath): ?array<string, mixed> { ... }

    private static function discoverCatalogPath(string $phpFilePath): ?string { ... }

    private static function parseCatalog(string $path): array<string, mixed> { ... }
}
```

**Discovery strategy:**

1. Resolve the compiled PHP path for the class (`\ReflectionClass::getFileName()`, or PSR-4 from the autoloader).
2. Walk **up** from that file's directory looking for `tyhp.meta.json` (application output dir) or `package.tyhp.meta.json` (library/package root). Stop at filesystem root / `configure()` base path.
3. Parse the catalog once; intern-expand on lookup. Cache by catalog path so a second `ReflectionClass` in the same project does not re-read disk.
4. `loadForClass` is `catalog.cls[fqn]` (or `fn` for functions) after intern expansion — not a per-class file.

Do **not** look for `{Class}.php.tyhp.meta.json` beside the PHP file.

**Fallback behavior:** If no catalog is found, the loader returns `null`. The reflection classes handle this by returning partial results (using PHP reflection as a fallback for basic information) and logging an informational diagnostic.

### Acceptance Criteria

- [ ] `MetadataLoader` finds `{output.path}/tyhp.meta.json` by walking up from a compiled PHP file
- [ ] `MetadataLoader` finds `package.tyhp.meta.json` at a Composer package root the same way
- [ ] There is no per-PHP sidecar discovery path
- [ ] Loaded catalogs are cached in memory and class lookup is by FQN
- [ ] Intern indexes (`f`, `rt`, `xe`, …) are expanded to strings for the public reflection API
- [ ] Missing catalogs return `null` (no crash)
- [ ] Invalid catalogs (wrong `v`, malformed JSON) emit a log warning and return `null`
- [ ] `MetadataLoader::configure()` allows overriding the walk root

---

## Phase 3: `\Tyhp\Reflection\ReflectionClass`




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

1. Tyhp reflection metadata catalog (`tyhp.meta.json` / `package.tyhp.meta.json`) — primary source for all structural information
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
- `getTyhpFileName()` — original `.tyhp` source file path (intern-expanded from `f`)
- `getTyhpStartLine()` — start line in the Tyhp source (from catalog `sl`)
- `getTyhpEndLine()` — end line in the Tyhp source (from catalog `el`, populated from `Base2Ast.EndLine` added by Story 19 Phase 1)

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
7. **Library package reflection** — reflect a class from a Composer-distributed Tyhp library using that package's single `package.tyhp.meta.json`
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

Run `tyhp build`. **Expected:** `{output.path}/tyhp.meta.json` exists (one file for the project, not next to each `.php`). Open it and verify it contains:
- `"v": 1` and a `str` intern table
- A `cls` object with key `App\\Account` (not a `classes` array of verbose objects)
- That entry has `m` listing `__construct`, `getBalance`, `deposit` (types as intern indexes into `str`)
- That entry has `p` listing `balance` with type intern index for `float` and `vis` `2` (private)
- `f` / `sl` / `el` resolve to the original `.tyhp` file positions
- No `{phpFile}.tyhp.meta.json` sidecars were written

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
