# Implementation Plan: Story 06 — Built-in Types, Grammar Fixes, and Compiler Infrastructure

> **Roadmap position:** Story 06 — **Tier 0 — Spine**
> **Direct dependencies (new numbering):** 01, 02, 03, 04
> **Renumbered from:** legacy Story 2
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Source:** `TODO.md` Story 06
> **Branch:** TBD
> **Generated:** 2026-02-16
> **Prerequisites:** Story 01, Story 02, Story 03, Story 04
> **Status:** COMPLETED (2026-07-31 audit) — grammar split, built-ins, package tyhpdef loading, tagless mode, and conformance fixtures landed. Phase acceptance checkboxes in this doc were never updated; residual notes in `INCOMPLETE.md`.

---

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Phase 1: ANTLR Grammar Fixes for Generic Type Arguments](#phase-1-antlr-grammar-fixes-for-generic-type-arguments)
- [Phase 2: Tyhpdef Validation via Lint Command](#phase-2-tyhpdef-validation-via-lint-command)
- [Phase 3: Hardcode Built-in Types, Utility Types, and Compile-Time Constructs](#phase-3-hardcode-built-in-types-utility-types-and-compile-time-constructs)
- [Phase 4: Binder Integration — Package Tyhpdef Loading and Symbol Registration](#phase-4-binder-integration--package-tyhpdef-loading-and-symbol-registration)
- [Phase 5: Promise, TaskScheduler, and Async Type Verification](#phase-5-promise-taskscheduler-and-async-type-verification)
- [Phase 6: Tyhpdef Distribution Strategy and Bundling Infrastructure](#phase-6-tyhpdef-distribution-strategy-and-bundling-infrastructure)
- [Phase 7: Optional Open Tags (Extension-Driven Tagless Source Mode)](#phase-7-optional-open-tags-extension-driven-tagless-source-mode)

---

## Architecture Overview

### Context Within the Compiler Pipeline

Story 06 sits between the Binder (Story 02) and the Checker (Story 08) in the critical path. Built-in types, utility types, and compile-time constructs are hardcoded in the C# compiler code, providing the type definitions and built-in construct signatures that the Binder loads into the `GlobalScope` and that the Checker uses to validate Tyhp-specific language features. External type definitions (PHP extensions, runtime packages) are provided by tyhpdef files distributed via Composer packages. Without complete built-in type registrations and tyhpdef loading infrastructure, the Checker cannot validate `decimal` arithmetic, struct operations, generic containers, compile-time constructs, or disposable patterns.

```
Parser/AST (DONE)
    │
    ▼
Story 01: Foundation (DiagnosticBag, CompilationService, BuildAction skeleton)
    │
    ▼
Story 02: Binder (Symbols, Scopes, Name Resolution, Tyhpdef Loading)
    │
    ▼
┌─────────────────────────────────────────────────────────────┐
│  STORY 06: Built-in Types & Compiler Infrastructure          │
│                                        ◄── THIS PLAN       │
│  ┌──────────────────────────┐  ┌──────────────────────────┐ │
│  │ Hardcoded Built-in Types │  │ External Tyhpdefs        │ │
│  │ (C# compiler code:      │  │ (Composer packages:      │ │
│  │  scalar types, decimal,  │  │  tyhp/php-{ver},         │ │
│  │  struct, generics,       │  │  tyhp/core, tyhp/async,  │ │
│  │  utility types,          │  │  tyhp/decimal,           │ │
│  │  compile-time constructs)│  │  tyhp/lambda)            │ │
│  └────────┬─────────────────┘  └────────────┬─────────────┘ │
│           │                                 │               │
│  ┌────────▼─────────────────────────────────▼─────────────┐ │
│  │ Binder Integration: Load into GlobalScope              │ │
│  │ (Tyhp/TyhpLang/Binder/BuiltIn/)                       │ │
│  └────────────────────────┬───────────────────────────────┘ │
│                           │                                 │
│  ┌────────────────────────▼───────────────────────────────┐ │
│  │ Package Discovery & Symbol Registration                │ │
│  │ (vendor/*/package.tyhp.json → TyhpdefSymbolRegistrar)  │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
    │
    ▼
Story 08: Checker (uses built-in types + tyhpdef symbols to validate types)
    │
    ▼
Story 09: Emitter (uses built-in types to know what runtime calls to emit)
```

### Key File Locations

| Component | Path | Current State |
|-----------|------|---------------|
| PHP ext tyhpdefs & overlays (will become Composer package) | `runtime/php-extensions/php8.2.9/` | `.tyhpdef` declarations + `.tyhp` generic overlays |
| Binder built-in types | `Tyhp/TyhpLang/Binder/BuiltIn/Types.cs` | Registers scalar type names only |
| Binder built-in constants | `Tyhp/TyhpLang/Binder/BuiltIn/Constants.cs` | Magic constants registered |
| Binder built-in variables | `Tyhp/TyhpLang/Binder/BuiltIn/Variables.cs` | Superglobals registered |
| Binder tyhpdef loader | `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs` | ~262 lines, functional |
| Old embedded tyhpdef data | `Tyhp/TyhpLang/Binder/BuiltIn/OLD_Tyhpdef.cs` | ~1,820 lines, entirely commented out |
| Tyhp runtime packages | `runtime/packages/` (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`) | Each has a `package.tyhp.json` entry point referencing its `.tyhpdef` and `.tyhp` files |
| Promise runtime | `runtime/packages/async/src/Promise.php` | Functional |
| Tyhpdef visitor/parser | `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.Tyhpdef.cs` | ~1,178 lines, functional |
| ANTLR parser grammar | `Tyhp/TyhpLang/Grammar/TyhpParser.g4` | Contains generic type argument rules that need fixing |
| ANTLR lexer grammar | `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` | Tyhpdef mode tokens |
| Disposable interfaces | `runtime/packages/core/` (discovered via `package.tyhp.json`) | `IsDisposable`, `\Tyhp\Type`, etc. |

### Design Principles for This Phase

1. **Built-in types are hardcoded in C#.** Types like `decimal`, `struct`, generic `array<K,V>`, `iterable<K,V>`, `callable<...>`, and utility types like `\Tyhp\Readonly<T>` are registered directly in the binder's built-in type system (`Tyhp/TyhpLang/Binder/BuiltIn/`). No `.tyhpdef` file for built-in types.

2. **Utility types live in the `\Tyhp` namespace.** These are TypeScript-inspired built-in utility types hardcoded as checker operations, registered as special generic types in the binder and resolved by the checker through type transformation.

3. **External type definitions use Composer packages discovered via `package.tyhp.json`.** PHP extension types come from `tyhp/php-{phpVersion}` Composer packages, which contain both `.tyhpdef` files (PHP built-in function/class signatures) and `.tyhp` files (generic overlays on PHP classes like `Traversable<K,V>`, `Iterator<K,V>`, `Generator<K,V,S,R>`, SPL generics, and async method patterns). Runtime library types come from `tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda` Composer packages. Each package has a `package.tyhp.json` entry point with include globs that specify which `.tyhpdef` and `.tyhp` files to load.

4. **Generic overlays on PHP classes** (like `Traversable<K,V>`, `Iterator<K,V>`, `Generator<K,V,S,R>`, SPL generics like `SplStack<T>`, `SplQueue<T>`, etc.) are defined as `.tyhp` files within the PHP extension Composer packages (e.g., `tyhp/php-8.2`), NOT hardcoded. These `.tyhp` overlay files are included alongside the `.tyhpdef` declaration files via the package's `package.tyhp.json` include globs.

5. **Compile-time constructs** (`nameof()`, `typeof()`, `default()`, `variable_exists()`) are hardcoded in the binder's built-in function registry.

6. **`iterable` is treated as semantically equivalent to `array|\Traversable`** at checker time. This is a checker concern handled in Story 08, but Story 06 hardcodes `iterable` and `iterable<K,V>` as built-in generic types in the binder.

7. **`package.tyhp.json` is the package entry point.** Each Composer package containing type definitions has a `package.tyhp.json` at its root with the following structure:

   ```json
   {
     "include": [
       "*.tyhpdef",
       "src/**/*.tyhp"
     ]
   }
   ```

   The `include` member is an array of path glob strings (relative to the package root) that specify which files to load. The manifest is **JSON only**; tyhpdef syntax lives in the referenced `.tyhpdef` and `.tyhp` files, not inside `package.tyhp.json`. This replaces using a single `package.tyhpdef` file as the entry point because: (a) a single tyhpdef file gets too large for big packages, and (b) packages may need to include `.tyhp` code (not just `.tyhpdef` declarations). For library-type Tyhp projects, `package.tyhp.json` is auto-generated by `tyhp compile` — the compiler generates it in the output directory with include globs pointing to the library's type definition files.

8. **Restricted types (`void` and `never`) require explicit opt-in via generic constraints.** The types `void` and `never` are "restricted types" — they cannot be used as generic type arguments unless the generic parameter's constraint explicitly includes them. For example, `array<void>` is rejected because `array<T>` does not include `void` in its constraint, but `callable<void>` is valid because `callable`'s return-type parameter uses `TReturn extends void|never|mixed`. This system prevents nonsensical types (like `array<void>` or `SplStack<never>`) at the type-checking level while still allowing restricted types where they are semantically meaningful (as return types in callables and promises). Each generic type definition chooses which restricted types to opt in to: `callable` allows both `void` and `never` (functions can return nothing or always throw), while `Promise` allows `void` but not `never` (a promise that never resolves is not useful). Types like `array<T>`, `iterable<T>`, and SPL collections do not opt in to either.

### Dependency Map for This Phase

```
Phase 1 (Grammar Fixes)
    │
    └──► Phase 2 (Lint Command)
             │
             ├──► Phase 3 (Hardcode Built-in Types)
             │        │
             │        └──► Phase 4 (Binder Integration)
             │                 │
             │                 ├──► Phase 5 (Promise/Async Verification)
             │                 │
             │                 └──► Phase 6 (Distribution Strategy)
             │
             └──► (Fix tyhpdef parse errors found by linting)

Phase 1 (Grammar Fixes)
    │
    └──► Phase 7 (Optional Open Tags — grammar + lexer/config; independent of Phases 2–6)
```

---

## Phase 1: ANTLR Grammar Fixes for Generic Type Arguments

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

The ANTLR grammar has a critical limitation: the `tyhpGenericsTypeArgument` rule only accepts simple identifiers for generic type arguments, preventing type expressions like `array<int|string>`, `array<self|NamedType>`, `callable<string, void>`, etc. This phase splits the generic argument rules into separate declaration and usage forms and updates all grammar usage sites.

### Deliverables

1. Split `tyhpGenericsTypeArgument` into `tyhpGenericParameterDeclaration` and `tyhpGenericTypeArgument`
2. Corresponding list and wrapper rules for each
3. Updated grammar usage sites for declaration and usage contexts
4. Updated AST visitor (`TyhpParserAstVisitor.TyhpGenerics.cs`) to handle the new rule structure
5. Regenerated ANTLR parser files
6. Validation that existing tyhpdef files parse correctly with the fix

### Implementation Details

**1.1 — Split Generic Type Argument Rules**

Current rule:
```
tyhpGenericsTypeArgument
    : Identifier=name (T_EXTENDS TypeExpr=typeExpr)?
    ;
```

This works for generic DECLARATIONS (e.g., `class Foo<T extends Bar>`) but fails for generic USAGE (e.g., `array<self|NamedType>` or `callable<string, void>`).

Create two separate rules:

```
tyhpGenericParameterDeclaration
    : Identifier=name (T_EXTENDS TypeExpr=typeExpr)?
    ;

tyhpGenericTypeArgument
    : typeExpr
    ;
```

**1.2 — Create Corresponding List and Wrapper Rules**

Create list and wrapper rules for each new rule that mirror the existing `tyhpGenericsTypeArguments` and `tyhpGenericsTypeArgumentsList` patterns:

```
tyhpGenericParameterDeclarationList
    : tyhpGenericParameterDeclaration (T_COMMA tyhpGenericParameterDeclaration)*
    ;

tyhpGenericParameterDeclarations
    : T_LT tyhpGenericParameterDeclarationList T_GT
    ;

tyhpGenericTypeArgumentList
    : tyhpGenericTypeArgument (T_COMMA tyhpGenericTypeArgument)*
    ;

tyhpGenericTypeArguments
    : T_LT tyhpGenericTypeArgumentList T_GT
    ;
```

**1.3 — Update Declaration Contexts**

The following grammar rules define generic PARAMETERS (identifiers with optional constraints). They must use `tyhpGenericParameterDeclarations`:

- `tyhpTypeAlias` — `type Foo<T extends Bar> = ...`
- `classNameGrammarAddon` — `class Foo<T extends Bar>`
- `traitNameGrammarAddon`
- `interfaceNameGrammarAddon`
- `enumNameGrammarAddon`
- `functionNameGrammarAddon`

**1.4 — Update Usage Contexts**

The following grammar rules use generic TYPE ARGUMENTS (arbitrary type expressions). They must use `tyhpGenericTypeArguments`:

- `typeNameGrammarAddon` — `SomeClass<int|string>`
- `classNameIdentifierGrammarAddon` — `extends ParentClass<string>`
- `memberNameIdentifierGrammarAddon`
- `functionCallGrammarAddon` — `foo<int, string>()`
- `namespaceNameGrammarAddon`
- `legacyNamespaceNameGrammarAddon`
- `tyhpStringWithOptionalGeneric`
- `tyhpGenericIdentifier`
- `tyhpGenericIdentifierWithoutConstructor`

**1.5 — Update AST Visitor**

File: `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpGenerics.cs`

Update the visitor to handle the new rule structure:

- Add visitor methods for `tyhpGenericParameterDeclaration` and `tyhpGenericParameterDeclarationList` that produce `GenericParameterAst` nodes (with name and optional constraint)
- Add visitor methods for `tyhpGenericTypeArgument` and `tyhpGenericTypeArgumentList` that produce `TypeExpressionAst` nodes (arbitrary type expressions)
- Ensure existing visitor methods that reference the old `tyhpGenericsTypeArgument` rule are updated

Also update `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.Tyhpdef.cs` — check for any references to the old generic type argument rule names and update them.

**1.6 — Regenerate ANTLR Parser Files**

After modifying the grammar files, regenerate the ANTLR parser:

```bash
./compile_grammar.sh
dotnet clean && dotnet restore && dotnet build
```

Requires `antlr-ng` (`npm install -g antlr-ng`). Grammar regeneration is **not** triggered by `dotnet build` alone — `tyhp.csproj` only compiles the already-generated parser sources in `Tyhp/TyhpLang/Parser/`.

**1.7 — Validate Existing Tyhpdef Files**

Validate that existing tyhpdef files parse correctly with the fix by running `tyhp lint` on:

- `runtime/packages/core/package.tyhp.json` (has `array<self|NamedType>` on line 10)
- `runtime/php-extensions/php8.2.9/ExtCore.tyhpdef`
- All other runtime package tyhpdefs
- All PHP extension tyhpdefs

```bash
dotnet run --project tyhp.csproj -- lint runtime/packages/core/package.tyhp.json
dotnet run --project tyhp.csproj -- lint runtime/php-extensions/php8.2.9/
```

### Grammar Files

- `Tyhp/TyhpLang/Grammar/TyhpParser.g4`
- `Tyhp/TyhpLang/Grammar/TyhpLexer.g4`
- `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.TyhpGenerics.cs`
- `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.Tyhpdef.cs`

### Acceptance Criteria

- [ ] `tyhpGenericsTypeArgument` is split into `tyhpGenericParameterDeclaration` (for declarations) and `tyhpGenericTypeArgument` (for usage)
- [ ] Corresponding list and wrapper rules exist for both forms
- [ ] All declaration contexts use `tyhpGenericParameterDeclarations`
- [ ] All usage contexts use `tyhpGenericTypeArguments`
- [ ] The AST visitor handles both new rule forms correctly
- [ ] ANTLR parser files are regenerated successfully
- [ ] `runtime/packages/core/package.tyhp.json` parses without errors (tests `array<self|NamedType>`)
- [ ] All 16 PHP extension tyhpdef files parse without grammar-related errors
- [ ] Type expressions like `array<int|string>`, `callable<string, void>`, `SomeClass<Foo|Bar>` are accepted as generic type arguments
- [ ] Generic declarations like `class Foo<T extends Bar>`, `type Alias<T> = ...` continue to work

### Dependencies

- The ANTLR grammar files must be in a modifiable state
- The ANTLR code generation toolchain must be available (`antlr-ng` via `npm install -g antlr-ng`; run `./compile_grammar.sh` from the repository root)
- No dependency on any other phase — this is the first phase

---

## Phase 2: Tyhpdef Validation via Lint Command

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Extend the existing `tyhp lint` command to support linting `.tyhpdef` files in addition to `.tyhp` files. The lint command must accept explicit file or directory path arguments; when no paths are specified, it lints the project as it does today. This provides a repeatable validation process for all tyhpdef files (PHP extension tyhpdefs and runtime package tyhpdefs).

### Deliverables

1. Updated `LintAction` that accepts explicit file/directory paths as positional arguments
2. Support for `.tyhpdef` files in the lint pipeline (parse and report errors)
3. A report of all parse errors found in existing tyhpdef files
4. Fixes for any parse errors in the PHP extension tyhpdefs at `runtime/php-extensions/php8.2.9/`

### Implementation Details

**2.1 — Extend the Lint Command to Accept File/Directory Arguments**

File: `Tyhp/CLI/LintAction.cs`

The current `LintAction` discovers source files exclusively from the project configuration (`this._project.GetProjectSourceFiles()`). Extend it to also accept explicit file or directory paths:

- When explicit paths are provided on the command line (e.g., `tyhp lint path/to/file.tyhpdef` or `tyhp lint path/to/dir/`), lint those files directly without requiring a `tyhp.json` project
- When no explicit paths are provided, fall back to the existing project-based discovery behavior
- For directory arguments, recursively discover all `.tyhp` and `.tyhpdef` files within
- For file arguments, lint the specified file regardless of extension (`.tyhp` or `.tyhpdef`)

The CLI entry point `TyhpHostedService.cs` must be updated to pass positional arguments through to `LintAction`.

**2.2 — Ensure `.tyhpdef` Files Parse Through the Lint Pipeline**

The existing `CompilationService.ParseFiles()` uses the parser infrastructure that already supports `.tyhpdef` files via `ParseMode.Tyhpdef`. Verify that:

- The `ParseFiles()` method detects `.tyhpdef` extensions and uses the tyhpdef parse mode
- Parse errors from tyhpdef files are collected into `DiagnosticBag` the same way as `.tyhp` parse errors
- The lint summary correctly reports tyhpdef file results alongside tyhp file results

Verify that `ParseFiles()` handles `.tyhpdef` files. If it does not, extend it to detect the `.tyhpdef` extension and parse using the tyhpdef grammar rules.

**2.3 — Validate PHP Extension Tyhpdefs**

Use the lint command to validate the PHP extension tyhpdef files (located at `runtime/php-extensions/php8.2.9/`):

```bash
dotnet run --project tyhp.csproj -- lint runtime/php-extensions/php8.2.9/
```

A `tyhp.json` project file already exists at `runtime/php-extensions/php8.2.9/tyhp.json` with include patterns for `*.tyhpdef` files. Both approaches must work:

```bash
cd runtime/php-extensions/php8.2.9 && dotnet run --project ../../../tyhp.csproj -- lint
```

Files to validate (16 total):
- `ExtCore.tyhpdef` (~2,370 lines)
- `ExtStandard.tyhpdef` (large — contains most of PHP's standard library)
- `ExtSPL.tyhpdef`
- `ExtDate.tyhpdef`
- `ExtJson.tyhpdef`
- `ExtPcre.tyhpdef`
- `ExtReflection.tyhpdef`
- `ExtSession.tyhpdef`
- `ExtOpenssl.tyhpdef`
- `ExtHash.tyhpdef`
- `ExtFilter.tyhpdef`
- `ExtLibxml.tyhpdef`
- `ExtSodium.tyhpdef`
- `ExtRandom.tyhpdef`
- `ExtPcntl.tyhpdef`
- `ExtZlib.tyhpdef`

**2.4 — Fix Parse Errors in PHP Extension Tyhpdefs**

For each parse error found:
- If parsing fails, fix the tyhpdef content. Grammar issues are addressed in Phase 1 and do not block this phase.
- Apply fixes and re-validate

Priority: Fix errors in the most commonly-used extensions first:
1. `ExtCore.tyhpdef` — PHP core functions and classes
2. `ExtStandard.tyhpdef` — standard library
3. `ExtSPL.tyhpdef` — SPL classes (important for generic type overlays)
4. `ExtDate.tyhpdef` — DateTime classes
5. `ExtJson.tyhpdef` — JSON functions
6. `ExtPcre.tyhpdef` — regex functions

**2.5 — Validate Runtime Package Tyhpdefs**

Each runtime package has a `package.tyhp.json` entry point whose include globs reference the package's `.tyhpdef` and `.tyhp` files. Use the lint command to validate all files in each runtime package directory:

```bash
dotnet run --project tyhp.csproj -- lint runtime/packages/core/
dotnet run --project tyhp.csproj -- lint runtime/packages/decimal/
dotnet run --project tyhp.csproj -- lint runtime/packages/async/
dotnet run --project tyhp.csproj -- lint runtime/packages/lambda/
```

### How to Test (for AI Agent Executing This Plan)

After implementing the lint command changes, verify the implementation using these commands:

1. **Lint PHP extension tyhpdefs via project:**
   ```bash
   cd runtime/php-extensions/php8.2.9 && dotnet run --project ../../../tyhp.csproj -- lint
   ```
   Expected: parses all 16 `Ext*.tyhpdef` files, reports per-file pass/fail.

2. **Lint individual files via explicit path:**
   ```bash
   dotnet run --project tyhp.csproj -- lint runtime/packages/core/core.tyhpdef
   ```
   Expected: parses the specified file only, reports results.

3. **Lint a directory via explicit path:**
   ```bash
   dotnet run --project tyhp.csproj -- lint runtime/php-extensions/php8.2.9/
   ```
   Expected: recursively discovers and parses all `.tyhpdef` and `.tyhp` files in the directory.

4. **Lint all runtime package tyhpdefs:**
   ```bash
   dotnet run --project tyhp.csproj -- lint runtime/packages/core/ runtime/packages/decimal/ runtime/packages/async/ runtime/packages/lambda/
   ```
   Expected: discovers and parses all `.tyhpdef` and `.tyhp` files across all 4 runtime packages, reports results.

Use `dotnet run --project Tyhp -- lint <path>` for all lint operations. See `LOCAL_LLM_BUILD_GUIDE.md` for build prerequisites.

### Acceptance Criteria

- [ ] The `tyhp lint` command accepts explicit file and directory path arguments
- [x] When no explicit paths are given, the lint command falls back to project-based discovery (existing behavior)
- [x] `.tyhpdef` files are parsed using the tyhpdef parse mode and errors are reported via `DiagnosticBag`
- [ ] All 16 PHP extension tyhpdef files at `runtime/php-extensions/php8.2.9/` have been validated
- [ ] All 4 runtime package `package.tyhp.json` entry points and their referenced `.tyhpdef`/`.tyhp` files have been validated
- [ ] Parse errors in critical PHP extension tyhpdefs (`ExtCore`, `ExtStandard`, `ExtSPL`) are fixed
- [x] The lint command MUST exit with non-zero exit code if errors are found

### Dependencies

- Phase 1 (Grammar Fixes) — generic type argument grammar must be fixed before tyhpdef files using type expressions as generic arguments can parse correctly
- Story 01 (Foundation) — `CompilationService` and `DiagnosticBag` for error collection
- The existing parser and tyhpdef visitor must be functional (they are)
- No dependency on the Binder — this is purely parsing-level validation

---

## Phase 3: Hardcode Built-in Types, Utility Types, and Compile-Time Constructs

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Hardcode all built-in types, utility types in the `\Tyhp` namespace, and compile-time constructs directly in the C# compiler code. This replaces the previous approach of defining built-in types in `.tyhpdef` files. Built-in types are registered in `Tyhp/TyhpLang/Binder/BuiltIn/` and are always available without any file loading.

### Deliverables

1. Verified scalar and core built-in types in `Types.cs`
2. `decimal` and `struct` type aliases hardcoded in the binder
3. Generic parameter information for `array`, `iterable`, and `callable`
4. All `\Tyhp` namespace utility types registered as built-in checker operations
5. Compile-time construct signatures (`nameof`, `typeof`, `default`, `variable_exists`) in the built-in function registry
6. Documentation of callable generic type convention and restricted types convention

### Implementation Details

**3.1 — Scalar and Core Built-in Types (Already Exist)**

File: `Tyhp/TyhpLang/Binder/BuiltIn/Types.cs`

Current state: Already registers `int`, `string`, `float`, `bool`, `array`, `callable`, `void`, `never`, `null`, `true`, `false`, `mixed`, `object`, `iterable`, `resource`, `self`, `static`, `parent`.

Verify these are all present and correct. If any are missing or incorrect, add/fix them.

**3.2 — Hardcode `decimal` and `struct` Type Aliases**

File: `Tyhp/TyhpLang/Binder/BuiltIn/Types.cs`

Register:
- `decimal` as a special type backed by `float` (runtime uses bcmath/gmp via `tyhp/decimal` package)
- `struct` as a special type backed by `array`

These are fundamental type system aliases that must always be available. The full `\Tyhp\Decimal` class definition (methods, operators) is provided by the `tyhp/decimal` runtime package's type definition files (discovered via `package.tyhp.json`).

**3.3 — Hardcode Generic Built-in Types**

Register generic parameter information for built-in types:

- `array<TValue>` — single-parameter shorthand where key is `int|string`
- `array<TKey extends int|string, TValue>` — full two-parameter form
- `iterable<TValue>` — single-parameter shorthand
- `iterable<TKey extends int|string, TValue>` — full two-parameter form
- `callable<TArgs..., TReturn extends void|never|mixed>` — return-last convention (last param is return type, preceding params are argument types)

Note: `iterable` is semantically equivalent to `array|\Traversable` at checker time (handled in Story 08, `IMPLEMENTATION_PLAN_TODO_STORY_08.md`). The binder registers it as a built-in type with generic parameter info; the checker handles the union expansion logic.

**3.4 — Hardcode Utility Types in `\Tyhp` Namespace**

File: New file `Tyhp/TyhpLang/Binder/BuiltIn/UtilityTypes.cs`

Register the following types in the `\Tyhp` namespace. These are NOT ordinary generics — they are **built-in checker operations** recognized by the type system. The binder registers them as special generic types; the checker resolves them by transforming the type argument(s).

| Type | Generic Parameters | Checker Operation |
|------|-------------------|-------------------|
| `\Tyhp\Readonly<T>` | `T` must be class, interface, or struct | Creates copy of T with all properties marked readonly |
| `\Tyhp\Partial<T>` | `T` must be class, interface, or struct | Creates copy of T with all properties made nullable |
| `\Tyhp\Required<T>` | `T` must be class, interface, or struct | Creates copy of T with all properties made non-nullable |
| `\Tyhp\Pick<T, K>` | `T` must be class/struct; `K` is string literal union | Creates type with only properties named in K |
| `\Tyhp\Omit<T, K>` | `T` must be class/struct; `K` is string literal union | Creates type with all properties except those named in K |
| `\Tyhp\Record<K, V>` | `K extends int\|string`, `V` any type | Equivalent to `array<K, V>` |
| `\Tyhp\Exclude<T, U>` | `T` is union type, `U` any type | Removes from T members assignable to U |
| `\Tyhp\Extract<T, U>` | `T` is union type, `U` any type | Keeps from T members assignable to U |
| `\Tyhp\NonNullable<T>` | `T` any type | Removes `null` from T |
| `\Tyhp\Nullable<T>` | `T` any type | Equivalent to `?T` (adds null to T) |
| `\Tyhp\ReturnType<T>` | `T` must be callable | Extracts the return type of callable T |
| `\Tyhp\Parameters<T>` | `T` must be callable | Extracts parameter types of callable T as array |
| `\Tyhp\Awaited<T>` | `T` any type | Recursively unwraps `Promise<T>` to resolved type |

The binder registers these as `BuiltInUtilityTypeSymbol` with their generic parameter info. The checker's `TypeInferrer.ResolveTypeExpression()` detects these utility types and delegates to specific resolver methods.

`BuiltInUtilityTypeSymbol` extends `BaseSymbol` (similar to `BuiltInTypeSymbol`) with additional properties:
- `GenericParameterRequirements`: defines the expected number and constraints of generic type parameters
- `UtilityBehavior`: an enum describing the type transformation (e.g., `Partial<T>` makes all properties optional). Each enum value corresponds to a named utility type (e.g., `UtilityBehavior.Partial`, `UtilityBehavior.Required`), matching the name-based dispatch in Story 08's `UtilityTypeResolver`.
- Added to the `SymbolType` enum as `BuiltInUtilityType`
- Registered in `GlobalScope` during built-in type initialization (Phase 3.1)

**3.5 — Hardcode Compile-Time Constructs**

File: `Tyhp/TyhpLang/Binder/BuiltIn/Functions.cs` (new file)

Register the following as built-in functions with special compile-time semantics:

- `nameof(mixed $symbolReference): string` — returns string name of a symbol reference. The argument must be a valid symbol reference. The return type is a string literal at compile time.

- `typeof(mixed $typeReference): \Tyhp\Type` — returns a `\Tyhp\Type` instance representing the type. The argument must be a type name or typed expression. Note: `typeof()` is NOT always erased at compile time. When used with a generic type parameter inside a class that uses the `GenericObject` trait (see Story 11, Phase 8), `typeof(T)` compiles to a runtime call: `$this->tyhpGenericObjectGetGenericType('T')?->getUnderlyingType() ?? Type::mixed()`. For concrete types (`typeof(MyClass)`, `typeof(int)`), it compiles to `Type::of('MyClass')` or `Type::int()` respectively.

- `default(string $typeName): mixed` — returns the default value for a type. The argument must be a type name string. Return type depends on the argument:
  - `default(int)` returns `0`
  - `default(string)` returns `''`
  - `default(bool)` returns `false`
  - `default(?T)` returns `null`
  - `default(array)` returns `[]`
  - `default(float)` returns `0.0`

- `variable_exists(string $varName): bool` — checks if a variable is defined in the current scope. This is a compile-time check that verifies the variable name is a valid variable in the current scope.

These are marked as compile-time-only in the function registry. The checker validates their arguments (Story 08, Phase 6, section 6.8). The emitter evaluates/erases them.

**3.6 — Callable Generic Type Convention (Documentation)**

The `callable` type uses a **return-last** convention for its generic parameters: `callable<TArgs..., TReturn extends void|never|mixed>`. The last generic parameter is always the return type, and all preceding parameters represent the callable's parameter types. This mirrors the natural reading order of a function signature (parameters first, then return type).

Examples:

| Generic Syntax | Meaning |
|---|---|
| `callable<string, int>` | Takes `string`, returns `int` |
| `callable<int, int, bool>` | Takes `(int, int)`, returns `bool` |
| `callable<void>` | No parameters, returns `void` |
| `callable<string, void>` | Takes `string`, returns `void` |

The last generic parameter uses the constraint `TReturn extends void|never|mixed`, which explicitly opts in to the restricted types `void` and `never`. Without this constraint, generic type parameters would reject `void` and `never` as arguments (see Design Principle 8: Restricted Types). This constraint is what allows `callable<void>` and `callable<never>` to be valid.

Type aliases that wrap `callable` must propagate this constraint:

```
type Callback<TReturn extends void|never|mixed> = callable<string, TReturn>;
type BiFunction<T1, T2, TReturn extends void|never|mixed> = callable<T1, T2, TReturn>;
```

This ensures that user-defined type aliases retain the ability to use `void` and `never` as return types when appropriate.

The `Closure` type follows the same return-last convention as `callable`: `\Closure<TReturn>` is shorthand where the return type is the sole parameter (zero args), and `\Closure<TArgs..., TReturn>` extends to the multi-parameter form with argument types first and return type last.

**3.7 — Restricted Types Convention (Documentation)**

`void` and `never` cannot be used as generic type arguments unless the generic parameter's constraint explicitly includes them. For example, `array<void>` is rejected, but `callable<void>` is valid because callable's return-type parameter uses `TReturn extends void|never|mixed`.

Each generic type definition chooses which restricted types to opt in to:
- `callable` allows both `void` and `never` (functions can return nothing or always throw)
- `Promise` allows `void` but not `never` (a promise that never resolves is not useful)
- `array<T>`, `iterable<T>`, and SPL collections do not opt in to either

### Acceptance Criteria

- [x] All scalar and core built-in types are verified present in `Types.cs`
- [x] `decimal` type alias is hardcoded in the binder (backed by `float`)
- [x] `struct` type alias is hardcoded in the binder (backed by `array`)
- [ ] `array<TValue>` and `array<TKey, TValue>` generic parameter info is registered
- [ ] `iterable<TValue>` and `iterable<TKey, TValue>` generic parameter info is registered
- [ ] `callable<TArgs..., TReturn>` generic parameter info is registered with return-last convention
- [ ] All 13 `\Tyhp` namespace utility types are registered as `BuiltInUtilityTypeSymbol`
- [ ] `nameof()`, `typeof()`, `default()`, `variable_exists()` are registered as compile-time built-in functions
- [ ] Callable return-last convention is documented and enforced
- [ ] Restricted types convention is documented
- [x] No references to `.tyhpdef` files for any built-in type definitions

### Dependencies

- Phase 2 (Lint Command) — needed to validate that no existing tyhpdef parsing is broken by the new built-in types
- Story 02 (Binder) — `GlobalScope`, `BaseSymbol`, and symbol registration infrastructure must be working
- Story 01 (Foundation) — `CompilationService` and `DiagnosticBag` are already working

---

## Phase 4: Binder Integration — Package Tyhpdef Loading and Symbol Registration

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Extend the existing tyhpdef loading mechanism to support package discovery (from `vendor/*/package.tyhp.json`), implement the `TyhpdefSymbolRegistrar` that converts parsed tyhpdef ASTs into binder symbols registered in `GlobalScope`, and wire the loading pipeline into the `CompilationService` / `BuildAction` so that the checker and emitter can access external type information. This is the critical integration point — without the symbol registrar, the tyhpdef ASTs are parsed but have no effect on compilation.

**Current State:** `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs` (~262 lines) is **already implemented** with:
- `Get(DiagnosticBag, CompilationOptions?)` — main entry point returning `IEnumerable<SrcFileAst>`
- `LoadEmbeddedTyhpdefs()` — decompresses and parses `TyhpBuiltIn.Tyhpdef.AllKeyed`
- `LoadBundledTyhpdefs()` — recursively loads all `*.tyhpdef` files from a `tyhpdef/` directory (this method is removed — PHP extension tyhpdefs are now distributed via the `tyhp/php-{phpVersion}` Composer package and its responsibility is replaced by `LoadPackageTyhpdefs()`)
- `ParseContent()` — ANTLR lexer/parser/visitor pipeline for tyhpdef and tyhp parse modes
- `FindTyhpdefDirectory()` — directory discovery with parent traversal and assembly location fallback

**Still needed:** Package discovery from `vendor/*/package.tyhp.json` (covers runtime libraries AND PHP extension packages), user-configured tyhpdef paths, and the AST-to-symbol registration pipeline.

### Deliverables

1. Extended `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs` — new `LoadPackageTyhpdefs()` method for Composer dependency tyhpdef discovery
2. New `TyhpdefSymbolRegistrar` class that walks tyhpdef ASTs and registers symbols into `GlobalScope`
3. Integration with the `CompilationService` / `BuildAction` pipeline to load and register tyhpdef symbols before user code binding
4. Loading order defined: Built-in types (hardcoded) → Embedded tyhpdefs → Package tyhpdefs → User code

### Implementation Details

**4.1 — Extend `Tyhpdef.Get()` with Package Tyhpdef Loading**

File: `Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.cs`

The existing `Get()` method already loads embedded and bundled tyhpdefs. Add a new `LoadPackageTyhpdefs()` method to discover `package.tyhp.json` files from Composer-installed packages, resolve their include globs, and load all matching `.tyhpdef` and `.tyhp` files:

```csharp
private static void LoadPackageTyhpdefs(List<SrcFileAst> results, DiagnosticBag diagnostics, CompilationOptions? options)
```

The full discovery order:

1. **Embedded tyhpdefs** — already implemented via `LoadEmbeddedTyhpdefs()`
2. **Package tyhpdefs** (NEW) — scan `{projectRoot}/vendor/*/*/package.tyhp.json` for all Composer dependency types (runtime libraries AND the `tyhp/php-{phpVersion}` PHP extension package). For each `package.tyhp.json` found, read its `include` array of glob patterns and resolve them relative to the package directory to load all matching `.tyhpdef` and `.tyhp` files.
3. **User project tyhpdefs** — from paths specified in `tyhp.json` (`tyhpdefInclude`/`tyhpdefExclude` glob patterns). The configuration schema is defined in Story 10; until then, this feature is not active and the code path must exist as a no-op with a `// PLACEHOLDER_STORY_10` comment.

Update `Get()` to call `LoadPackageTyhpdefs()`. Note: PHP extension tyhpdefs are no longer bundled with the compiler — they are distributed as the `tyhp/php-{phpVersion}` Composer package and discovered via the same `vendor/*/package.tyhp.json` scan. The existing `LoadBundledTyhpdefs()` method is removed. Its responsibility is replaced by `LoadPackageTyhpdefs()`.

**4.2 — Implement Tyhpdef AST to Symbol Registration**

Create a new class `TyhpdefSymbolRegistrar` to walk tyhpdef ASTs and register symbols:

File: `Tyhp/TyhpLang/Binder/TyhpdefSymbolRegistrar.cs` (new)

For each tyhpdef AST node type, create the corresponding symbol and register it:

| Tyhpdef AST Node | Symbol to Create | Registration Target |
|------------------|-----------------|-------------------|
| `TyhpdefImportClassAst` | `ObjectDeclarationSymbol` (class kind) | `GlobalScope` or `NamespaceScope` |
| `TyhpdefImportFunctionAst` | `FunctionDeclarationSymbol` | `GlobalScope` or `NamespaceScope` |
| `TyhpdefImportConstantAst` | `ConstantSymbol` | `GlobalScope` or `NamespaceScope` |
| `TyhpdefImportEnumAst` | `ObjectDeclarationSymbol` (enum kind) | `GlobalScope` or `NamespaceScope` |
| `TyhpdefImportClassConstAst` | `ObjectConstantSymbol` | Parent `ObjectDeclarationSymbol` |
| Type alias declarations | `TypeAliasSymbol` | `GlobalScope` or `NamespaceScope` |
| Struct declarations | `ObjectDeclarationSymbol` (struct kind) | `GlobalScope` or `NamespaceScope` |
| Namespace declarations | `NamespaceSymbol` / `NamespaceScope` | `GlobalScope` |

For each symbol, populate:
- `Name` and `FullyQualifiedName` from the tyhpdef declaration
- `Visibility` (public by default for tyhpdef imports)
- `IsDeprecated` / `IsObsolete` flags from tyhpdef `deprecated`/`obsolete` keywords
- `DocComment` from the tyhpdef declaration's doc comment
- Generic parameters and constraints from generic tyhpdef declarations
- Method/property signatures for class imports

**4.3 — Wire into the Compilation Pipeline**

Modify `CompilationService` (or `BuildAction`) to call tyhpdef loading and symbol registration before user code binding:

```
Step 1:  Populate built-in types (Types.cs) — already exists
Step 2:  Populate built-in constants (Constants.cs) — already exists
Step 3:  Populate built-in variables (Variables.cs) — already exists
Step 4:  Register built-in utility types (UtilityTypes.cs) — NEW from Phase 3
Step 5:  Register compile-time constructs (Functions.cs) — NEW from Phase 3
Step 6:  Call Tyhpdef.Get() to load embedded tyhpdefs — already implemented
Step 7:  Call LoadPackageTyhpdefs() to discover vendor/*/package.tyhp.json and load all included files — NEW
Step 8:  Run TyhpdefSymbolRegistrar on all tyhpdef ASTs — NEW
Step 9:  Parse user .tyhp files
Step 10: Bind user code
```

Note: `Tyhpdef.Get()` already returns parsed `SrcFileAst` instances for embedded and bundled tyhpdefs. The missing piece is the **symbol registration** step — converting those ASTs into binder symbols in `GlobalScope`.

**4.4 — Handle Tyhpdef Symbol Conflicts**

Define conflict resolution rules:
- If a tyhpdef declares a symbol that conflicts with a built-in type from `Types.cs`, the tyhpdef augments it (adds generic parameters, methods) rather than replaces it
- If **two different Composer packages** (each discovered via a distinct `package.tyhp.json`) define the same fully-qualified type name, report a compile-time error: **`TyhpdefDuplicateFqnAcrossPackages` (8025)**. Do not resolve by silently preferring one package over the other.
- For multiple tyhpdef files **included from the same package** (or from embedded compiler tyhpdefs), apply registrar merge rules; incompatible duplicates use **`TyhpdefDuplicateDeclaration` (8002)** or other bind diagnostics as appropriate.
- If a tyhpdef symbol conflicts with user code, report an error diagnostic
- Namespace merging: if two tyhpdefs contribute symbols to the same namespace, merge them. If two tyhpdefs declare the same symbol with conflicting signatures, report an error diagnostic.

**4.5 — Handle the `deprecated` and `obsolete` Keywords**

The tyhpdef syntax supports marking declarations as deprecated or obsolete. Ensure:
- The visitor extracts these flags from the AST
- The symbol registrar sets `IsDeprecated` / `IsObsolete` on the created symbols
- The checker emits warnings when deprecated symbols are used (implemented in Story 08, Phase 6, DeprecationRule)

**4.6 — Tyhpdef Extension Auto-Inclusion**

When a tyhpdef declares extensions on a type (via `extension function`, `extension operator`, or `use extension` declarations), those extensions are automatically available in scope whenever the declared type is used in Tyhp code. No explicit `import extension` statement is required for tyhpdef-declared extensions.

**Mechanism:** During tyhpdef loading, the binder stores extension declarations as part of the type's symbol metadata (`ExtensionDeclarationSymbol` attached to the type's symbol). When the binder resolves a method call or operator on a value of that type, it checks the type's symbol for attached tyhpdef extensions and includes them in the resolution scope automatically.

**Distinction from user-defined extensions:** Extensions defined in `.tyhp` source files still require an explicit `import extension` statement. Only extensions declared in `.tyhpdef` files on a type's declaration are auto-included. This distinction exists because tyhpdef extensions represent the type's canonical extended API (like PHP built-in functions on strings), while user-defined extensions are project-specific.

**4.7 — Handle Overloaded Function Signatures in Tyhpdefs**

PHP functions like `array_map`, `str_replace`, etc., have multiple valid call signatures. The tyhpdef format supports overloaded signatures. Ensure:
- Multiple declarations of the same function name create a single `FunctionDeclarationSymbol` with multiple signature overloads
- The binder supports storing overload information
- The checker validates which overload matches a call site (implemented in Story 08, Phase 6, OverloadRule)

Function overloads are stored using an `Overloads` property (`List<FunctionDeclarationSymbol>`) on `FunctionDeclarationSymbol`. The first symbol registered for a given function name becomes the "primary" declaration. Subsequent overload signatures are added to its `Overloads` list. The binder resolves overloads by matching argument types against each signature in the `Overloads` list. The `NameResolver` returns the primary symbol; callers inspect `Overloads` to find the best match.

### Acceptance Criteria

- [x] `Tyhpdef.Get()` method discovers and parses PHP extension tyhpdef files from the `vendor/` directory (already implemented)
- [x] `ParseContent()` method handles ANTLR lexer/parser/visitor pipeline for tyhpdef and tyhp parse modes (already implemented)
- [x] Directory discovery works for tyhpdef directories (already implemented)
- [ ] `LoadPackageTyhpdefs()` is added to `Tyhpdef.cs` and discovers `vendor/*/package.tyhp.json` files, resolves their include globs, and loads all matching `.tyhpdef` and `.tyhp` files
- [ ] `TyhpdefSymbolRegistrar` is implemented and converts tyhpdef ASTs to binder symbols in `GlobalScope`
- [ ] PHP extension tyhpdef symbols (functions, classes, constants) are registered into `GlobalScope`
- [ ] Package tyhpdef symbols are registered into `GlobalScope` with correct priority
- [ ] The loading order is correct (built-in → embedded → package tyhpdefs → user)
- [ ] Symbol conflicts are handled gracefully (no crashes, error diagnostics); duplicate FQN across distinct Composer packages reports **8025** (`TyhpdefDuplicateFqnAcrossPackages`)
- [ ] `deprecated`/`obsolete` flags are correctly set on symbols
- [ ] Overloaded function signatures are stored correctly
- [ ] The `BuildAction` pipeline calls tyhpdef loading and symbol registration before user code binding
- [ ] A simple test: define a variable with type `decimal` in user code and binder resolves it to the hardcoded `decimal` type alias

### Dependencies

- Phase 3 (Hardcode Built-in Types) — built-in types must be registered before external tyhpdefs are loaded
- Story 02 (Binder) must have `GlobalScope`, `BaseSymbol`, and symbol registration infrastructure working
- Story 01 (Foundation) — `CompilationService` and `DiagnosticBag` are already working
- Phase 2 (Validation) must be complete before Phase 4 begins

---

## Phase 5: Promise, TaskScheduler, and Async Type Verification

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Verify the type signatures for Tyhp's async/await infrastructure (`Promise<T>`, `TaskScheduler`, event loop types). All async types are provided by the `tyhp/async` runtime package's type definition files (discovered via `package.tyhp.json` at `runtime/packages/async/`). This phase focuses on **verifying** that the package's type definitions provide everything the checker and emitter need, not on authoring type definitions.

### Deliverables

1. Verify that `tyhp/async`'s type definition files (discovered via `package.tyhp.json` at `runtime/packages/async/`) contain `Promise<TReturn extends void|mixed = void>` and related type definitions. Generic default `= void` syntax depends on Story 28. Until Story 28 is implemented, generic defaults are not supported. The tyhpdef declares the parameter without a default and relies on explicit type arguments at all call sites.
2. Verify type definitions for `Promise::_async()` and `Promise::_await()` (async/await desugar targets) in the package's type definitions
3. Verify type definitions for all static combinators/factories: `all<T>`, `race<T>`, `resolved<T>`, `rejected<T>`, `delay`, `timeout<T>`, `batch<TItem, TResult>`, `run<T>`, `fromGenerator`
4. Verify type definitions for instance methods: `then<TResult>`, `catch<TResult>`, `finally`
5. Verify type definitions for `EventLoop` (the event loop/task scheduler) from `runtime/packages/async/tyhp_src/EventLoop.tyhp`
6. Verify type definitions for `AsyncIterator<T>`, `AsyncIterable<T>`, and `AsyncKeyValueIterator<TKey, TValue>` interfaces for async iteration support
7. Integration with the package loading pipeline from Phase 4 (discovered from `vendor/tyhp/async/package.tyhp.json`)

### Implementation Details

**5.1 — Verify `Promise<T>` Class Type (Provided by Package Type Definitions)**

The `Promise<T>` class definition is in `tyhp/async`'s type definition files (discovered via `package.tyhp.json` at `runtime/packages/async/`). This section documents what the package's type definitions must contain for reference.

Cross-reference with the `.tyhpdef` and `.tyhp` files in `runtime/packages/async/` for the authoritative definitions:

The `Promise` class is defined as `Promise<TReturn extends void|mixed = void>` where:
- `TReturn` is the fulfillment value type
- The constraint `extends void|mixed` allows `Promise<void>` for async functions that don't return a value (e.g., fire-and-forget async operations). Note that `never` is **not** included in the constraint — `Promise<never>` is not useful because a promise that can never fulfill serves no purpose.
- The default `= void` means unparameterized `Promise` is equivalent to `Promise<void>`, matching the common case of async procedures

**Constructor and core methods:**
- `__construct(callable<TReturn> $executor)` — takes a zero-param callable returning `TReturn`
- `_async<T extends void|mixed>(callable<T> $fn): static<T>` — wraps a callable in a Promise (async keyword desugars to this)
- `_await<T>(Promise<T> $promise): T` — suspends fiber until resolved (await keyword desugars to this)

**Static combinators:**
- `all<T>(array<Promise<T>> $promises): array<T>` — waits for all promises (async method, returns `Promise<array<T>>`)
- `race<T>(array<Promise<T>> $promises): T` — first to settle wins (async method, returns `Promise<T>`)

**Static factory methods:**
- `resolved<T>(T $value): static<T>` — creates a pre-resolved promise
- `rejected<T extends void|mixed>(Throwable $error): static<T>` — creates a pre-rejected promise; generic T allows assignment to any Promise type
- `delay(int $ms): void` — resolves after delay (async method, returns `Promise<void>`)
- `timeout<T>(Promise<T> $promise, int $ms): T` — races promise against timeout (async method)
- `batch<TItem, TResult>(array<TItem> $items, callable<TItem, Promise<TResult>> $processor, int $concurrency = 5): array<TResult>` — processes items through an async processor with concurrency control
- `run<T extends void|mixed>(callable<T> $fn): T` — runs callable in event loop (async method)
- `fromGenerator(Generator $generator): mixed` — converts generator-based coroutine to promise (async method)

**Instance methods (all async, so declared return types are unwrapped):**
- `then<TResult>(?callable<TReturn, TResult> $onFulfilled = null, ?callable<Throwable, TResult> $onRejected = null): TReturn|TResult` — registers fulfillment/rejection handlers; callable params use return-last convention (see Phase 3, Section 3.6)
- `catch<TResult>(callable<Throwable, TResult> $onRejected): TReturn|TResult` — registers rejection handler; original value passes through on success
- `finally(callable<void> $onFinally): TReturn` — runs handler regardless of outcome; original value/error passes through

**Private implementation methods:**
- `resolve(mixed $value): void` — fulfills the promise (handles Promise flattening if value is itself a Promise)
- `reject(Throwable $error): void` — rejects the promise
- `processCallbacks(): void`, `queueFiber(...)`, `processQueue()`, `getLoop()` — internal scheduling

**5.2 — Verify Async/Await Function Signatures**

The `_async()` and `_await()` are static methods on `Promise` that the `async`/`await` keywords desugar to:

- `Promise::_async<T extends void|mixed>(callable<T> $fn): static<T>` — wraps a zero-param callable in a Promise. The `async` keyword before a function/closure call desugars to this. Returns `static<T>` to support subclassing.
- `Promise::_await<T>(Promise<T> $promise): T` — suspends the current fiber and returns the resolved value. The `await` keyword desugars to this.

The checker needs to know:
- `_await()` can only be called inside an `async` function context
- `_await()` unwraps `Promise<T>` to `T`
- `_async()` wraps a return type `T` in `Promise<T>` (using `static<T>` for late-static-binding support)
- The `void|mixed` constraint on `_async`'s `T` allows wrapping void-returning callables (e.g., `async function doWork(): void`)

**5.3 — Verify `EventLoop` Type**

The `EventLoop` class is defined in the `tyhp/async` package's type definition files (discovered via `package.tyhp.json` at `runtime/packages/async/`). Verify its public API:

- `start(): void` — start processing the event loop
- `isRunning(): bool` — check if the loop is currently running
- Static instance management methods

EventLoop verification is required for Phase 5 completion. Define the `EventLoop` type in the package's `.tyhpdef` files (referenced by `package.tyhp.json`) even if users do not interact with it directly.

**5.4 — Verify `AsyncIterator<T>` and `AsyncIterable<T>` Interfaces**

Tyhp supports async iteration via `foreach (await $asyncIterable as $item)`. This requires two interfaces that parallel PHP's `Iterator`/`IteratorAggregate` but with async semantics:

```
interface AsyncIterator<T> {
    async function current(): T;
    async function next(): bool;
}

interface AsyncIterable<T> {
    function getAsyncIterator(): AsyncIterator<T>;
}
```

These interfaces are defined in the `tyhp/async` package's type definition files (discovered via `package.tyhp.json` at `runtime/packages/async/`):

- `AsyncIterator<T>` — the core async iteration interface. `next()` returns `Promise<bool>` (true if there is a next value, false if exhausted). `current()` returns `Promise<T>` (the current value).
- `AsyncIterable<T>` — provides an `AsyncIterator<T>` via `getAsyncIterator()`. Any type implementing this interface can be used with `foreach (await ... as ...)`.
- `AsyncKeyValueIterator<TKey, TValue>` — required key-value variant for `foreach (await ... as $key => $value)`. Has `currentKey(): TKey` and `currentValue(): TValue` instead of `current()`.

The checker uses these interfaces to:
1. Validate that `foreach (await $expr as $item)` has an expression of type `AsyncIterable<T>` (or `Promise<Iterable<T>>` for resolve-then-iterate)
2. Infer the loop variable type `$item` as `T` from `AsyncIterable<T>`
3. Require that `await` is present when iterating an `AsyncIterable<T>` (error without it)
4. Require that the `foreach` is inside an `async` function context

The emitter desugars `foreach (await $asyncIterable as $item) { body }` to:
```php
$__asyncIter = $asyncIterable->getAsyncIterator();
while (\Tyhp\Promise::_await($__asyncIter->next())) {
    $item = \Tyhp\Promise::_await($__asyncIter->current());
    // body
}
```

**5.5 — Async Generator Types**

Tyhp does NOT support async generators. Standard `Generator<TKey, TValue, TSend, TReturn>` is used, and if async behavior is needed, the generator yields `Promise<T>` values. Document this as a design constraint in the tyhpdef.

**5.6 — Validate Against Examples**

Cross-reference with `Examples/AsyncAwait.tyhp` to ensure the type definitions support the patterns demonstrated in the example file.

### Acceptance Criteria

- [x] `tyhp/async` package's type definition files (discovered via `package.tyhp.json` at `runtime/packages/async/`) contain all Promise-related type definitions
- [x] `Promise<TReturn extends void|mixed = void>` is defined as a generic class with all methods from `Promise.php`
- [x] `Promise::_async<T extends void|mixed>(callable<T> $fn): static<T>` signature is defined
- [x] `Promise::_await<T>(Promise<T> $promise): T` signature is defined
- [x] Static combinators defined: `all<T>`, `race<T>` with method-level generics
- [x] Static factories defined: `resolved<T>`, `rejected<T extends void|mixed>`, `delay`, `timeout<T>`, `run<T extends void|mixed>` with method-level generics
- [x] `batch<TItem, TResult>(array<TItem>, callable<TItem, Promise<TResult>>, int): array<TResult>` is defined with separate item and result type parameters
- [x] Instance methods defined: `then<TResult>`, `catch<TResult>`, `finally` with proper callable generics using return-last convention
- [x] All callable type parameters use return-last convention (e.g., `callable<TReturn, TResult>` = takes TReturn, returns TResult)
- [x] `AsyncIterator<T>` interface is defined with `current(): T` and `next(): bool` (async methods)
- [x] `AsyncIterable<T>` interface is defined with `getAsyncIterator(): AsyncIterator<T>`
- [x] `AsyncKeyValueIterator<TKey, TValue>` interface is defined (required, for key-value async iteration)
- [ ] The `tyhp/async` package's type definition files parse without errors through the tyhpdef parser (use `tyhp lint runtime/packages/async/` to verify)
- [x] The definitions are consistent with the runtime implementation
- [ ] The package is discovered via `package.tyhp.json` and its included files are loaded by the binder via Composer dependency scanning (from Phase 4)

### Dependencies

- Phase 4 (Binder Integration) — the package loading pipeline must support discovering `package.tyhp.json` and loading included files
- `runtime/packages/async/package.tyhp.json` and its included files are the authoritative source for type definitions
- Generic type parameter support in tyhpdefs must be working

---

## Phase 6: Tyhpdef Distribution Strategy and Bundling Infrastructure

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Define and implement the strategy for how type definition files are distributed with the compiler and discovered at compile time. Built-in types are hardcoded in the compiler's C# code (always available). PHP extension types are distributed as a separate Composer package (`tyhp/php-{phpVersion}`) that users install alongside their project dependencies — this package contains both `.tyhpdef` files (PHP built-in function/class signatures) and `.tyhp` files (generic overlays on PHP classes and async method patterns). Runtime library types (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`) are distributed as `.tyhpdef` and `.tyhp` files within their respective Composer packages. All packages are discovered via `vendor/*/package.tyhp.json`, which specifies include globs for the files to load.

### Deliverables

1. A documented distribution strategy
2. Configuration support for tyhpdef file locations and PHP version targeting
3. Graceful fallback when package tyhpdefs are missing
4. Cleanup of legacy tyhpdef infrastructure (`OLD_Tyhpdef.cs`)
5. Build script updates

### Implementation Details

**6.1 — Distribution Strategy**

| Component | Distribution Method | Discovery |
|-----------|-------------------|-----------|
| Built-in types | Hardcoded in compiler C# code | Always available |
| PHP extension types | Layered Composer packages — a `tyhp/php-{major}` **base** package (e.g. `tyhp/php-8`) holding the items common to all `{major}.*` versions, plus per-minor `tyhp/php-{major}.{minor}` packages (e.g. `tyhp/php-8.2`, `tyhp/php-8.4`) that inherit the base and add/override version-specific declarations. Each contains `.tyhpdef` declarations + `.tyhp` generic overlays. | `vendor/tyhp/php-*/package.tyhp.json` |
| Runtime library types | `.tyhpdef` and `.tyhp` files in each Composer package (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`) | `vendor/tyhp/*/package.tyhp.json` |
| User/project tyhpdefs | Referenced from `tyhp.json` configuration | Loaded from configured include paths |

**PHP extension version resolution (target design):** Given the configured `phpVersion` (e.g. `8.4`), resolve PHP extension types as:

1. **Exact minor match** — load the `tyhp/php-{major}.{minor}` package (which itself layers on top of its `tyhp/php-{major}` base). Use it if present.
2. **Major fallback** — if no exact minor package is installed, fall back to the `tyhp/php-{major}` base package alone.
3. **Error if neither** — if neither the exact minor package nor the major base package is available, report an error.

Because a major-base fallback omits the minor-specific declarations, code that references a symbol introduced only in the minor package naturally fails to resolve (a normal "symbol not found" error) — no special handling is required for that case.

**Note (out of scope — Story 21):** Creating the layered `tyhp/php-{major}` base + `tyhp/php-{major}.{minor}` Composer packages, and implementing the exact→major→error resolution above, is tracked in Story 21 (`IMPLEMENTATION_PLAN_TODO_STORY_21.md`). Until Story 21 is complete, the PHP extension type definition files reside at `runtime/php-extensions/php8.2.9/` and are loaded directly by the binder as a development fallback; the loader currently matches an extension directory by major.minor and emits the interim warning in 6.3 when no compatible directory is found (see 6.3 for the interim-vs-target behavior).

**6.2 — Add Configuration Options for Tyhpdef Paths**

File: `Tyhp/Config/Project.cs`

Add new configuration properties to `tyhp.json`. Until Story 10 defines the full schema, use the following minimal properties: `phpVersion` (string, required).

- `TyhpdefInclude` (`List<string>`) — glob patterns for additional tyhpdef files to load
- `TyhpdefExclude` (`List<string>`) — glob patterns for tyhpdef files to exclude
- `PhpVersion` (`string`) — the `phpVersion` property determines which PHP extension tyhpdef files are loaded, via the exact→major→error resolution described in 6.1. **Target behavior (Story 21):** if neither the exact `tyhp/php-{major}.{minor}` package nor the `tyhp/php-{major}` base package can be resolved, report an error diagnostic. **Interim behavior (until Story 21 ships the packages):** this is a graceful warning instead — see 6.3.

Parse these from the `tyhp.json` configuration file and CLI arguments.

**6.3 — Implement Graceful Fallback When Tyhpdefs Are Missing**

If no compatible PHP extension package can be resolved for the configured `phpVersion` — i.e. neither an exact `tyhp/php-{major}.{minor}` package nor the `tyhp/php-{major}` base package is found (no matching `vendor/tyhp/php-*/package.tyhp.json`, and during development no matching `runtime/php-extensions/` directory):

- **Interim behavior (current — until Story 21 ships the layered packages):** emit a `DiagnosticSeverity.Warning` diagnostic (`TYHP8026`): "PHP extension package not found. Install `tyhp/php-{phpVersion}` via Composer for full PHP built-in type checking." Continue compilation without them (the binder still has the hardcoded built-in scalar types from `Types.cs`). The compiler must not crash or fail to start. A successful major-base fallback (per 6.1) suppresses this warning.
- **Target behavior (Story 21):** once the layered `tyhp/php-{major}` base + `tyhp/php-{major}.{minor}` packages exist and the exact→major resolution is implemented, failure to resolve *any* compatible package becomes an **error** (per 6.2), since a project then has no excuse for a missing PHP baseline. A major-base fallback still succeeds silently; only the case where even the major base is absent errors.

If a runtime package is missing (e.g., `tyhp/core` not installed, no `package.tyhp.json` found):
- Emit a `DiagnosticSeverity.Warning` diagnostic: "Runtime package not found for `tyhp/core`. Install the package via Composer for full type checking of disposable interfaces, `\Tyhp\Type`, etc."
- Continue compilation — built-in types from `Types.cs` and `UtilityTypes.cs` are still available

**6.4 — Clean Up `OLD_Tyhpdef.cs`**

File: `Tyhp/TyhpLang/Binder/BuiltIn/OLD_Tyhpdef.cs` (~1,820 lines, entirely commented out)

This file contains the old approach to bundling tyhpdefs (compressed Base64-encoded PHP extension tyhpdef data). It is already fully commented out and not referenced by the current code. Once the new hardcoded + Composer package approach is validated:
- Remove `OLD_Tyhpdef.cs` from the project entirely
- Verify that nothing else references this file (it is safe since it is already commented out)

**6.5 — Update Build Scripts**

Files: `build.sh`, `release_build.sh`, `local_build.sh`, `Dockerfile.build`

Ensure the build/release process:
- Does not bundle PHP extension tyhpdefs alongside the binary (they are now a Composer package)
- Does not include `DebugProject/tyhpdef_gen/` in release builds (these are development artifacts)
- Built-in types are compiled into the C# binary (no separate files needed)

**6.6 — `package.tyhp.json` Generation (Deferred to Story 20)**

Skip `package.tyhp.json` generation in this story. The full tyhpdef distribution mechanism (including `package.tyhp.json` generation) is handled by Story 20. Story 10 will wire up `ProjectType` configuration needed for this determination.

### Acceptance Criteria

- [ ] Distribution strategy is documented
- [ ] `Project.cs` has new configuration options for tyhpdef paths and PHP version
- [ ] The compiler starts and operates correctly when PHP extension package is not installed (warning, not crash)
- [ ] The compiler warns gracefully when runtime packages are missing
- [ ] `OLD_Tyhpdef.cs` is removed
- [ ] Build scripts are updated (no longer bundle PHP extension tyhpdefs alongside binary)
- [ ] Configuration allows users to specify custom tyhpdef include/exclude patterns
- [ ] `package.tyhp.json` generation is deferred to Story 20 (no generation logic in this story)

### Dependencies

- Phase 4 (Binder Integration) — the tyhpdef loading mechanism must be working before we can optimize distribution
- Phase 2 (Validation) — we need to know which tyhpdef files are valid
- Build pipeline knowledge — familiarity with `build.sh`, `Dockerfile.build`, `.csproj` configuration

---

## Phase 7: Optional Open Tags (Extension-Driven Tagless Source Mode)

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Add an opt-in project setting, `source.tagless` (boolean, default `false`), that lets a project author omit the `<?tyhp` / `<?tyhpdef` open tag from source files and rely on the file extension to select the language mode. When the setting is enabled:

- The open tag is **optional**. A `.tyhp` file is treated as a single Tyhp code block and a `.tyhpdef` file as a single Tyhpdef block, even with no open tag.
- The open tag is **still allowed**. If a file does begin with its matching open tag (`<?tyhp` for `.tyhp`, `<?tyhpdef` for `.tyhpdef`), it is consumed normally and is **not** an error.
- The closing tag `?>` is **always an error**, anywhere in the file. Tagless files are pure code; there is no transition back to inline output.

When `source.tagless` is `false` (the default), behavior is exactly as today: the open tag is required and `?>` is permitted (for inline-output interleaving in `.tyhp` files).

> **Forward note (future default flip):** A future release may flip the default of `source.tagless` to `true`, making tagless the default and requiring authors to set `source.tagless: false` to opt back into open/closing tags. This phase deliberately keeps the behavior behind a single setting so that flip is a one-line default change.

This is purely a **front-end (grammar + lexer + config)** concern. The emitter is unaffected: compiled output always begins with `<?php` + `declare(strict_types=1)` regardless of whether the input had an open tag (the open tag never appears in output anyway). `.php` files are unaffected by this setting — they are always parsed as PHP and always require `<?php`.

### Deliverables

1. `source.tagless` configuration property on `Tyhp/Config/Project.cs`, read from `tyhp.json` (and overridable by CLI), default `false`. (Config key governed by `CONVENTIONS.md` §4.)
2. Per-file plumbing in `Tyhp/Domain/Services/CompilationService.cs` that, before lexing each file, tells the lexer (a) whether tagless mode is on and (b) the extension-derived language mode (`"tyhp"` for `.tyhp`, `"tyhpdef"` for `.tyhpdef`), and that selects the dedicated tagless parser entry rule when tagless is on.
3. A dedicated `ST_TYHP_TAGLESS` lexer mode in `Tyhp/TyhpLang/Grammar/TyhpLexer.g4` (optional `<?tyhp` / `<?tyhpdef` tag → `ST_IN_SCRIPTING`) plus dedicated parser entry rules `tyhpTaglessSrcFile` / `tyhpdefTaglessSrcFile` in `Tyhp/TyhpLang/Grammar/TyhpParser.g4` (optional open tag, no inline output, no closing tag). Both require an ANTLR regeneration via `./compile_grammar.sh`.
4. Lexer start-mode selection in `Tyhp/TyhpLang/Parser/TyhpLexer.GrammarMethods.cs` (`ConfigureTagless`) that peeks the input (no consumption) for a literal Tyhp/Tyhpdef open tag and starts the lexer either in `ST_TYHP_TAGLESS` (tag present) or directly in `ST_IN_SCRIPTING` with the correct `_languageMode` (no tag). No synthetic token is injected.
5. Visitor entry points `VisitTyhpTaglessFile` / `VisitTyhpdefTaglessFile` (and `GetCurrentLanguageMode` support for the new contexts) so the tagless parse trees produce the same `SrcFileAst` shape as the tagged equivalents.
6. A new lexer-band diagnostic (1000s) raised when `?>` appears in a tagless file. The enum value lives only in `Tyhp/Domain/Exceptions/MessageCode.cs` with a matching `.resx` entry (per `CONVENTIONS.md` §1).
7. Golden fixtures (see the Golden Fixtures section) covering tagless `.tyhp`/`.tyhpdef`, the optional-but-present open tag, and the `?>`-is-an-error case.
8. Per-package tagless honoring in the tyhpdef package loader (`Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.PackageLoading.cs` + `Tyhpdef.cs`): each `package.tyhp.json` may declare its own tagless setting, applied to that package's files independently of the consuming project's `source.tagless` (see 7.5).

### Implementation Details

**7.1 — Config: `source.tagless`**

File: `Tyhp/Config/Project.cs`

- Add a `bool Tagless { get; private set; }` property (default `false`), populated in `ConfigChanged()` from the `source:tagless` configuration path (the `IConfiguration` colon-delimited form of the `source.tagless` key), using the existing `ParseBool()` extension for robust parsing (matches how `quiet` is read).
- The key is `source.tagless`. This introduces a new `source.*` config group; it is recorded in `CONVENTIONS.md` §4.

**7.2 — Per-file plumbing in `CompilationService`**

File: `Tyhp/Domain/Services/CompilationService.cs`

`ParseFile()` already branches on the file extension to pick the parser entry rule (`tyhpSrcFile` / `tyhpdefSrcFile` / `phpSrcFile`). Configure the lexer for this file and select the entry rule:

- Read the tagless flag from `CompilationOptions` (add a `bool Tagless` to `CompilationOptions`, populated from `Project.Singleton?.Tagless ?? false` by the callers that build options). Do **not** read `Project.Singleton` directly inside the parsing loop.
- Compute the extension-derived language mode: `.tyhp` → `"tyhp"`, `.tyhpdef` → `"tyhpdef"`, otherwise tagless does not apply (`.php` is always classic PHP).
- After `lexer.SetInputStream(...)` / `lexer.Reset()`, call `lexer.ConfigureTagless(bool enabled, string languageMode, DiagnosticBag diagnostics, string fileName)` (see 7.3/7.4) so the lexer can prime its start mode per file. For `.php` files, call it with `enabled: false`.
- When tagless is enabled, select the dedicated tagless entry rule instead of the classic one: `.tyhpdef` → `parser.tyhpdefTaglessSrcFile()`, `.tyhp` → `parser.tyhpTaglessSrcFile()`. When tagless is disabled, use the classic `tyhpdefSrcFile` / `tyhpSrcFile` / `phpSrcFile` rules unchanged.
- Incorporate the tagless flag into the AST cache key so identical bytes do not pick up a stale AST when the setting toggles (see 7.5).

**7.3 — Dedicated tagless lexer mode + parser entry rules (grammar change)**

Files: `Tyhp/TyhpLang/Grammar/TyhpLexer.g4`, `Tyhp/TyhpLang/Grammar/TyhpParser.g4`, `Tyhp/TyhpLang/Parser/TyhpLexer.GrammarMethods.cs`, `Tyhp/TyhpLang/Visitor/TyhpParserAstVisitor.*`

The classic entry rules (`tyhpSrcFile` → `tyhpBlock : T_TYHP_OPEN_TAG ...`, `tyhpdefSrcFile` → `tyhpdefBlock : T_TYHPDEF_OPEN_TAG ...`) require an open-tag token and allow inline output / closing tags, which is wrong for tagless source. Rather than fake the token stream, tagless mode is modeled directly in the grammar:

- **Lexer mode (`Tyhp/TyhpLang/Grammar/TyhpLexer.g4`):** add a dedicated `mode ST_TYHP_TAGLESS;` whose rules consume an optional leading-whitespace run and an optional `<?tyhp` / `<?tyhpdef` open tag (setting `_languageMode` and `mode(ST_IN_SCRIPTING)`). The mode contains **no catch-all rewind rule** — see the start-mode selection below for the no-tag case. A `<?php` tag is intentionally **not** recognized here (tagless source is pure Tyhp/Tyhpdef code).
- **Parser entry rules (`Tyhp/TyhpLang/Grammar/TyhpParser.g4`):** add `tyhpTaglessSrcFile` and `tyhpdefTaglessSrcFile`. Each has a leading action that sets the parser `_languageMode` (so semantic predicates behave the same with or without a literal tag), then an **optional** open-tag token (`T_TYHP_OPEN_TAG?` / `T_TYHPDEF_OPEN_TAG?`), then the statement list, then `EOF`. Because these rules contain no `tyhpInlineOutput` and no `T_CLOSE_TAG`, a closing `?>` is not in their follow set and is rejected as a parse error (in addition to the lexer diagnostic in 7.4).
- **Start-mode selection (`TyhpLexer.GrammarMethods.cs`, `ConfigureTagless`):** add fields `protected bool _taglessMode` and `protected string _taglessLanguageMode`. Because the lexer is reused thread-locally, `ConfigureTagless` must run per file; clear the tagless fields when `enabled` is `false`. When tagless is enabled, peek the input (lookahead only, no consumption) for a literal `<?tyhp` / `<?tyhpdef` tag allowing optional leading whitespace:
  - **If a literal Tyhp/Tyhpdef tag is present:** start the lexer in `ST_TYHP_TAGLESS` so that mode consumes the tag and transitions to `ST_IN_SCRIPTING`.
  - **If no such tag is present** (including `<?php` or bare code): set `_languageMode = _taglessLanguageMode` and start the lexer directly in `ST_IN_SCRIPTING`, so the entire file — including any leading whitespace — is lexed natively. (Lexing the whole file in scripting mode keeps line/column tracking exact; an earlier in-grammar catch-all rewind caused an off-by-one in line numbers and was removed.)
- **Visitor (`TyhpParserAstVisitor.*`):** add `VisitTyhpTaglessFile` / `VisitTyhpdefTaglessFile` that build the same `SrcFileAst` shape as the tagged `VisitTyhpFile` / `VisitTyhpdefFile`, and extend `GetCurrentLanguageMode` to return `"tyhp"` for `TyhpTaglessFileContext` and `"tyhpdef"` for `TyhpdefTaglessFileContext`.

This approach changes `TyhpLexer.g4` / `TyhpParser.g4` and therefore requires regenerating the parser/lexer via `./compile_grammar.sh`.

**7.4 — Closing tag `?>` is an error in tagless mode**

File: `Tyhp/TyhpLang/Parser/TyhpLexer.GrammarMethods.cs` (`closeTagHandler()`)

`closeTagHandler()` already runs whenever `?>` is lexed (`Tyhp/TyhpLang/Grammar/PhpLexer.g4` `T_CLOSE_TAG_WITH_NEWLINE`). When `_taglessMode` is `true`:

- Report an error diagnostic at the current line/column using the file's `DiagnosticBag` (provided in 7.2): a new `MessageCode` in the lexer (1000s) band — e.g. `LexerCloseTagNotAllowedInTaglessMode` — added to `Tyhp/Domain/Exceptions/MessageCode.cs` with a matching `ERROR_TYHP{code}` `.resx` entry (`Resources/CLI.TyhpHostedService*.resx`). The exact numeric value is allocated in `MessageCode.cs` (do not restate the number here — see `CONVENTIONS.md` §1).
- Use the message style from `CONVENTIONS.md` §2, e.g.: ``closing tag `?>` is not allowed when `source.tagless` is enabled``.
- Continue lexing (recoverable): still emit the synthetic semicolon as today so the token stream stays well-formed. Note the tagless parser entry rules (7.3) also reject `?>` as a parse error because `T_CLOSE_TAG` is not in their follow set, so a tagless file containing `?>` typically surfaces both the `TYHP1004` lexer diagnostic and a follow-on parse error / `Compilation aborted`.

**7.5 — Interaction notes / edge cases (resolve explicitly)**

- **Leading inline HTML in a tagless `.tyhp`:** if a tagless file's first non-whitespace content is neither a recognized open tag nor valid Tyhp code (e.g. raw `<h1>`), it is lexed in scripting mode and produces normal parse errors. Tagless mode is for tag-free code files; interleaving HTML requires classic mode (`source.tagless: false`). This is expected and documented.
- **`<?php` in a tagless `.tyhp`:** `<?php` is **not** treated as a tagless open tag. Tagless source is pure Tyhp/Tyhpdef code, so the start-mode peek (7.3) only recognizes `<?tyhp` / `<?tyhpdef`. A file that literally begins with `<?php` is lexed in scripting mode (no tag consumed), so the `<?php` sequence produces a normal parse error. If a project needs `<?php` honored / inline-output interleaving, it must use classic mode (`source.tagless: false`).
- **`.tyhpdef`:** the tyhpdef grammar is already single-block with no closing tag, so tagless mode for `.tyhpdef` only adds "open tag optional"; the 7.4 close-tag rule is effectively redundant there but harmless.
- **AST cache:** the file hash already keys the cache, but tagless mode changes how identical bytes lex. Incorporate the tagless flag into the cache key (or invalidate when it differs) so a file does not pick up a stale AST when the setting toggles. (`AstCacheService` — see Story 01 / Phase 2 context.)
- **Package-published files honor the *package's* tagless setting, not the consuming project's:** tagless is a property of how each source set was authored, so it must not leak across package boundaries. The consuming project's `source.tagless` (read by `CompilationService`) applies only to the project's own user code. Files loaded by the binder from a package are parsed according to that package's own setting:
  - Composer/runtime package files discovered via `package.tyhp.json` (`Tyhp/TyhpLang/Binder/BuiltIn/Tyhpdef.PackageLoading.cs`): the manifest carries an optional tagless flag (nested `source.tagless`, or a convenience top-level `tagless`; default `false`). `LoadPackageManifest` reads it and passes it through `TryLoadPackageFile` → `Tyhpdef.ParseContent`, which configures the lexer (`ConfigureTagless`) and selects the tagless entry rule exactly as `CompilationService` does for user code.
  - User-configured tyhpdef include paths (from the project's own `tyhp.json`) are part of the project, so they honor the project's `options.Tagless`.
  - Embedded built-in tyhpdefs and the development `runtime/php-extensions/` fallback are authored classic (open tags) and load with tagless `false`.
  - `Tyhpdef.ParseContent` therefore takes a `bool tagless` parameter (default `false`, so all existing callers and tagged content are unaffected) and applies tagless only to `ParseMode.Tyhp` / `ParseMode.Tyhpdef` (never raw PHP).

### Acceptance Criteria

- [ ] `source.tagless` is read from `tyhp.json` (default `false`) by `Project.cs`; `CompilationOptions` carries it to the parsing loop without touching `Project.Singleton` inside the loop.
- [ ] With `source.tagless: true`, a `.tyhp` file with **no** open tag parses as a single Tyhp code block (same AST as the tagged equivalent).
- [ ] With `source.tagless: true`, a `.tyhp` / `.tyhpdef` file that **does** begin with its matching open tag still parses with no error.
- [ ] With `source.tagless: true`, any `?>` produces the new lexer diagnostic at the correct line/column; with `source.tagless: false`, `?>` continues to work as today.
- [ ] With `source.tagless: false` (default), all existing behavior is unchanged (regression-free); `.php` files are unaffected regardless of the setting.
- [ ] Tagless and classic parses of the same code produce identical line/column reporting (no off-by-one from leading whitespace), and a tagless file with leading blank lines / comments reports diagnostics at the correct lines.
- [ ] `<?php` at the start of a tagless `.tyhp` is **not** honored as a PHP block (it is a parse error); only `<?tyhp` / `<?tyhpdef` are recognized as optional tagless open tags.
- [ ] The dedicated `ST_TYHP_TAGLESS` lexer mode and `tyhpTaglessSrcFile` / `tyhpdefTaglessSrcFile` parser entry rules are added to the grammar and the parser/lexer are regenerated via `./compile_grammar.sh`; no synthetic open-tag token is injected.
- [ ] The new diagnostic code exists only in `MessageCode.cs` with a matching `.resx` entry (per `CONVENTIONS.md` §1).
- [ ] The AST cache correctly distinguishes tagless vs classic parses of identical bytes.
- [ ] A package whose `package.tyhp.json` declares `source.tagless: true` has its tag-less `.tyhpdef`/`.tyhp` files loaded without parse errors, even when the consuming project's `source.tagless` is `false`; conversely a package without the flag (default `false`) still requires open tags. The project's own `source.tagless` does not affect how package files are parsed.

### Dependencies

- Phase 1 (Grammar Fixes) — should be complete so the lexer/parser baseline is stable before adding the tagless lexer mode and parser entry rules (this phase changes `TyhpLexer.g4` / `TyhpParser.g4` and regenerates the parser/lexer).
- Story 01 — `DiagnosticBag`, `CompilationService`, `CompilationOptions`, and the `IConfiguration`-backed `Project` config reader.
- Independent of the checker (Story 08) and emitter (Story 09): tagless affects only how the front-end produces tokens/AST.

---

## Cross-Cutting Concerns

### File Size Guidelines

| File | Target Maximum | Notes |
|------|---------------|-------|
| `Types.cs` (built-in types) | 300 lines | Contains scalar types + decimal + struct aliases |
| `UtilityTypes.cs` (utility types) | 300 lines | `\Tyhp` namespace utility type registrations |
| `Functions.cs` (compile-time constructs) | 200 lines | `nameof`, `typeof`, `default`, `variable_exists` |
| `Tyhpdef.cs` (binder integration) | 300 lines | Separate discovery logic from registration logic |
| `TyhpdefSymbolRegistrar.cs` | 500 lines | If `TyhpdefSymbolRegistrar.cs` exceeds 500 lines, split it by AST node type (e.g., `TyhpdefSymbolRegistrar.Classes.cs`, `TyhpdefSymbolRegistrar.Functions.cs`) |
| PHP extension package files (`.tyhpdef` + `.tyhp`) | No limit | Generated ANTLR files may be large. There is no size limit on generated files. |

### Testing Strategy

Each phase is validated by:

1. **Parse validation** — can the parser handle new grammar rules and tyhpdef content without errors?
2. **AST validation** — does the visitor produce correct AST nodes for the new content?
3. **Symbol registration validation** — are the correct symbols created in `GlobalScope`?
4. **Integration validation** — does user code that references built-in types and tyhpdef types resolve correctly?

Until Story 07's test infrastructure is available, validation uses: (1) Running `tyhp lint` on all tyhpdef files in `tests/tyhpdef-validation/`, (2) Running `tyhp build` on test `.tyhp` files that reference tyhpdef symbols, (3) Manual verification that symbol counts match expectations.

Verify symbol resolution by binding at least 5 sample `.tyhp` files that use types from each tyhpdef source (PHP extension, runtime packages, built-in types).

### Error Handling Conventions

All new code must follow the Story 01 diagnostic system:
- Use `DiagnosticBag.AddError()` / `AddWarning()` / `AddInfo()` for all compiler messages
- Never throw exceptions for recoverable errors (missing tyhpdef files, parse errors in tyhpdefs)
- Use appropriate `MessageCode` values in the 8000 range (reserved for tyhpdef errors per `TODO.md`)
- **Already defined `MessageCode` values** (do not redefine):
  - `8001` — `TyhpdefParseError` — a tyhpdef file failed to parse
  - `8002` — `TyhpdefDuplicateDeclaration` — a tyhpdef declares a symbol that already exists
  - `8003` — `TyhpdefFileNotFound` — a configured tyhpdef path doesn't exist
  - `8004` — `TyhpdefInvalidFormat` — a tyhpdef file has an unexpected structure
  - `8005` — `TyhpdefBindError` — a tyhpdef symbol failed during binding (semantic analysis)
  - `8010` — `TyhpdefExtensionConflict` — an extension member conflicts with a declared member on the same tyhpdef class
  - `8011` — `TyhpdefExtensionNotFound` — a `use extension` reference in tyhpdef could not be resolved
  - `8012` — `TyhpdefInlineExtensionInvalidMember` — invalid member with the `extension` qualifier in tyhpdef
  - `8025` — `TyhpdefDuplicateFqnAcrossPackages` — the same fully-qualified type name is defined in more than one Composer package (distinct `package.tyhp.json` roots)
- **Required new `MessageCode` values** for this story:
  - `8006` — Built-in type registration failure
  - `8007` — Tyhpdef generic parameter count mismatch
  - `8008` — Tyhpdef version mismatch (PHP version)
  - `8009` — Tyhpdef symbol conflicts with built-in type

### Rollback Safety

Before making changes to any existing file:
- Create a timestamped backup: `cp file.ext file.ext.bak.$(date +%Y%m%d%H%M%S)`
- This applies especially to `Types.cs`, `Tyhpdef.cs`, `Project.cs`, and any grammar files
- Never use `git reset`, `git revert`, `git checkout .`, or `git clean` to undo changes

---

*Generated: 2026-02-16 | Last updated: 2026-03-23 | Source: TODO.md Story 06 | Branch: Not Specified*

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify that grammar fixes, built-in types, tyhpdef loading, and the lint command work correctly. Steps can be skipped, reordered, or modified as needed. All commands assume you are in the repository root.

### Step 1: Verify Grammar Fixes — Generic Type Arguments in Usage Contexts

After the grammar split (`tyhpGenericParameterDeclaration` vs `tyhpGenericTypeArgument`), verify that type expressions work as generic arguments:

```tyhp
<?tyhp
namespace Test\Grammar;

function testGenericUsage(): void {
    array<int|string> $mixed = [1, "two", 3];
    array<self|null> $nullable = [];
    callable<string, int> $parser = fn(string $s): int => \intval($s);
    callable<int, int, bool> $compare = fn(int $a, int $b): bool => $a > $b;
}
```

Save as `test_grammar_generics.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_grammar_generics.tyhp
```

Expected:
- `array<int|string>` parses — union type as generic argument
- `callable<string, int>` parses — multiple type args with return-last convention
- `callable<int, int, bool>` parses — three type args
- No grammar errors about unexpected tokens in generic arguments

### Step 2: Verify Grammar Fixes — Generic Parameter Declarations Still Work

```tyhp
<?tyhp
namespace Test\GrammarDecl;

class Container<T> {
    private T $value;

    public function __construct(T $value) {
        $this->value = $value;
    }

    public function get(): T {
        return $this->value;
    }
}

class Pair<TKey extends int|string, TValue> {
    public TKey $key;
    public TValue $value;
}

type StringMap<V> = array<string, V>;
```

Save as `test_grammar_decl.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_grammar_decl.tyhp
```

Expected:
- `class Container<T>` parses (simple generic parameter)
- `class Pair<TKey extends int|string, TValue>` parses (constrained generic parameter)
- `type StringMap<V> = array<string, V>` parses (type alias with generic parameter and generic type usage)
- No regressions from the grammar split

### Step 3: Verify Lint Command Accepts Explicit Paths

Test the lint command with explicit file and directory arguments:

```bash
# Lint a single tyhpdef file
dotnet run --project tyhp.csproj -- lint runtime/php-extensions/php8.2.9/ExtCore.tyhpdef

# Lint a directory of tyhpdefs
dotnet run --project tyhp.csproj -- lint runtime/php-extensions/php8.2.9/

# Lint multiple runtime packages at once
dotnet run --project tyhp.csproj -- lint runtime/packages/core/ runtime/packages/decimal/
```

Expected:
- Single file lint: reports parse results for `ExtCore.tyhpdef` only
- Directory lint: recursively discovers and parses all `.tyhpdef` and `.tyhp` files in the directory
- Multiple paths: processes all specified directories
- Non-zero exit code if any errors are found

### Step 4: Validate All PHP Extension Tyhpdefs

Run the lint command on all PHP extension tyhpdef files:

```bash
dotnet run --project tyhp.csproj -- lint runtime/php-extensions/php8.2.9/
```

Expected: All 16 `Ext*.tyhpdef` files parse. Note any parse errors — critical ones in `ExtCore.tyhpdef`, `ExtStandard.tyhpdef`, and `ExtSPL.tyhpdef` should be fixed. Non-critical extensions (e.g., `ExtPcntl.tyhpdef`) can have known issues deferred.

### Step 5: Validate All Runtime Package Tyhpdefs

```bash
dotnet run --project tyhp.csproj -- lint runtime/packages/core/
dotnet run --project tyhp.csproj -- lint runtime/packages/decimal/
dotnet run --project tyhp.csproj -- lint runtime/packages/async/
dotnet run --project tyhp.csproj -- lint runtime/packages/lambda/
```

Expected: All tyhpdef and tyhp files in each runtime package parse without errors. If generic type argument expressions fail (e.g., `array<self|NamedType>`), ensure the grammar fix from Step 1 is in place.

### Step 6: Verify Built-in Types Are Registered

Create a test file that uses built-in types and aliases:

```tyhp
<?tyhp
namespace Test\BuiltIns;

function testScalars(): void {
    int $i = 1;
    string $s = "hello";
    float $f = 1.5;
    bool $b = true;
    null $n = null;
    mixed $m = 42;
    void $v;
    never $x;
    object $obj = new \stdClass();
}

function testTypeAliases(): void {
    decimal $price = 19.99;
    array<string> $names = ["Alice", "Bob"];
    iterable<int> $numbers = [1, 2, 3];
    callable<string, int> $strlen = fn(string $s): int => \strlen($s);
}
```

Save as `test_builtin_types.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_builtin_types.tyhp
```

Expected:
- All scalar types resolve without `BinderSymbolNotFound`
- `decimal` resolves to the hardcoded type alias
- `array<string>`, `iterable<int>`, `callable<string, int>` parse and their generic parameter info is available
- No crash or unexpected errors

### Step 7: Verify Utility Types in `\Tyhp` Namespace

```tyhp
<?tyhp
namespace Test\UtilityTypes;

class User {
    public string $name;
    public ?string $email;
    public int $age;
}

function testUtilityTypes(): void {
    \Tyhp\Readonly<User> $readonlyUser;
    \Tyhp\Partial<User> $partialUser;
    \Tyhp\Required<User> $requiredUser;
    \Tyhp\NonNullable<string|null> $nonNull;
    \Tyhp\Nullable<string> $nullable;
    \Tyhp\Record<string, int> $scores;
}
```

Save as `test_utility_types.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_utility_types.tyhp
```

Expected: Utility types in the `\Tyhp` namespace are recognized as `BuiltInUtilityTypeSymbol` entries. They should resolve without errors. The actual type transformation behavior is validated by the checker (Story 08), but the binder should register them without issues.

### Step 8: Verify Compile-Time Constructs

```tyhp
<?tyhp
namespace Test\CompileTime;

class Config {
    public string $name = "test";
}

function testCompileTime(): void {
    string $className = nameof(Config);
    \Tyhp\Type $type = typeof(Config);
    int $defaultInt = default(int);
    string $defaultStr = default(string);
    bool $exists = variable_exists('className');
}
```

Save as `test_compiletime.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_compiletime.tyhp
```

Expected:
- `nameof()`, `typeof()`, `default()`, `variable_exists()` are recognized as built-in compile-time functions
- No `BinderSymbolNotFound` for these function names
- The binder registers them in the built-in function registry

### Step 9: Verify Package Tyhpdef Loading Pipeline

If the package loading infrastructure is implemented, verify that `vendor/*/package.tyhp.json` discovery works:

```bash
# If a test project has vendor/ with tyhp packages installed:
cd DebugProject  # or another project with vendor/ dependencies
dotnet run --project ../tyhp.csproj -- build --verbose .
```

Expected:
- The binder discovers `package.tyhp.json` files from installed Composer packages
- Symbols from the packages are registered in `GlobalScope`
- User code can reference types defined in the packages

If no `vendor/` directory is available yet, verify that the binder warns gracefully:

```bash
dotnet run --project tyhp.csproj -- build test_builtin_types.tyhp
```

Expected: A warning diagnostic like "PHP extension package not found" — not a crash.

### Step 10: Verify Symbol Resolution with Tyhpdef Types

Create a file that uses types from PHP extension tyhpdefs:

```tyhp
<?tyhp
namespace Test\TyhpdefResolution;

function testPhpTypes(): void {
    \DateTime $now = new \DateTime();
    string $json = \json_encode(["key" => "value"]);
    int $len = \strlen("hello world");
    array $sorted = \array_values([3, 1, 2]);
    \PDO $db = new \PDO("sqlite::memory:");
}
```

Save as `test_tyhpdef_resolve.tyhp`. Run:

```bash
dotnet run --project tyhp.csproj -- build test_tyhpdef_resolve.tyhp
```

Expected: If PHP extension tyhpdefs are loaded, references to `\DateTime`, `\json_encode`, `\strlen`, `\array_values`, `\PDO` should resolve to their tyhpdef-defined symbols. If tyhpdefs are not yet fully loaded, the diagnostic output should show `BinderSymbolNotFound` (3003) for these — but no crash.

### Step 11: Verify Deprecated/Obsolete Flag Handling

If any tyhpdef files mark declarations as `deprecated` or `obsolete`, verify the flags are set:

Create a tyhpdef with deprecated declarations:

```tyhpdef
<?tyhpdef
namespace Test\Deprecated;

deprecated class OldWidget {
    public function render(): string;
}

class NewWidget {
    deprecated public function legacyMethod(): void;
    public function modernMethod(): void;
}
```

Save as `test_deprecated.tyhpdef`. Run:

```bash
dotnet run --project tyhp.csproj -- lint test_deprecated.tyhpdef
```

Expected: The tyhpdef parses and `IsDeprecated` flags are set on the `OldWidget` class symbol and the `legacyMethod` method symbol.

### Step 12: Verify Optional Open Tags (Tagless Source Mode)

Create a `.tyhp` file with **no** open tag:

```tyhp
namespace Test\Tagless;

function greet(string $name): string {
    return "Hello, " . $name;
}
```

Save as `test_tagless.tyhp`. With `source.tagless` enabled (via `tyhp.json` containing `{ "source": { "tagless": true } }`, or the equivalent CLI flag), run:

```bash
dotnet run --project tyhp.csproj -- build test_tagless.tyhp
```

Expected:
- The file parses as a single Tyhp code block (no open tag required) and produces the same output as the tagged equivalent.
- Adding a leading `<?tyhp` to the same file still parses with no error (open tag allowed, not required).
- Adding a `?>` anywhere produces the new "closing tag not allowed in tagless mode" diagnostic at the correct line/column.
- With `source.tagless` **disabled** (default), the tag-less file errors as before — confirming the default behavior is unchanged.

### Step 13: Clean Up Test Files

```bash
rm -f test_grammar_generics.tyhp test_grammar_decl.tyhp test_builtin_types.tyhp test_utility_types.tyhp test_compiletime.tyhp test_tyhpdef_resolve.tyhp test_deprecated.tyhpdef test_tagless.tyhp
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
