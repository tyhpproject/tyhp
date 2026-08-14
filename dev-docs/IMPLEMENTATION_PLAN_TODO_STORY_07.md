# Implementation Plan: Story 07 — Testing Infrastructure

> **Roadmap position:** Story 07 — **Tier 0 — Spine** · **Testing backbone (pulled forward to the spine)**
> **Direct dependencies (new numbering):** 01 (harness scaffold); exercises every later story incrementally as it lands
> **Renumbered from:** legacy Story 11
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Story:** 11 — Testing Infrastructure
> **Depends on:** Each test category depends on the story/phase it exercises. Pulled forward to Tier 0 as the testing backbone; per-story fixtures are added incrementally as each later story lands
> **Scope:** Establish a complete testing infrastructure for the Tyhp compiler, covering the .NET test project setup, parser tests, binder tests, checker tests, emitter/end-to-end tests, integration tests, and PHP-based Tyhp runtime package tests.
> **Status:** SUBSTANTIALLY COMPLETE for Waves A/B (2026-07-31 audit) — test project, parser/diagnostics/binder/checker/emitter/conformance suites exist. Phase 10 CI/CD remains DEFERRED. Runtime self-host milestone not green (allowlist empty). See `INCOMPLETE.md`.

---

## Project Context

The Tyhp compiler is a .NET 9.0 console application (`tyhp.csproj`, targeting `net9.0`, C# 13) that compiles `.tyhp` source files into PHP. The project currently has:

- A fully functional ANTLR-based parser and visitor system producing an AST (`Tyhp/TyhpLang/Parser/`, `Tyhp/TyhpLang/Visitor/`, `Tyhp/TyhpLang/Ast/`)
- ~100+ AST node classes with serialization/deserialization via `Base2Ast`
- AST caching service (`AstCacheService`)
- A CLI infrastructure (`Tyhp/CLI/`) with `DebugAction`, `ActionRunnerBase`, `TyhpHostedService`
- Binder scaffolding (`Tyhp/TyhpLang/Binder/`) — scopes, symbols, `TyhpBinder` (placeholder logic)
- Checker scaffolding (`Tyhp/TyhpLang/Checker/`) — `TyhpChecker` (commented out), `CheckerState` (mostly TODO), `VariableState` (empty)
- Emitter scaffolding (`Tyhp/TyhpLang/Emitter/`) — `TyhpEmitter` (empty), `PHPOutputFile` (TODO stubs), `EmitItem` (working)
- 18+ `.tyhp` example files and corresponding `.php` expected outputs in `Examples/` (for reference only — tests use generated `TestData/` files, not `Examples/`)
- Bundled PHP extension tyhpdef files in `runtime/php-extensions/` (Composer package `tyhp/php-8.2`)
- Tyhp runtime packages in `runtime/packages/`
- A `tests/` directory containing only a `readme.md` saying "unit tests go here"
- A single solution file `tyhp.sln` referencing only `tyhp.csproj`

The testing infrastructure must be built as a separate .NET test project added to the solution, with clear separation between unit tests, integration tests, and end-to-end tests.

---

> **Status:** SUBSTANTIALLY COMPLETE for Waves A/B (2026-07-31 audit) — test project, parser/diagnostics/binder/checker/emitter/conformance suites exist. Phase 10 CI/CD remains DEFERRED. Runtime self-host milestone not green (allowlist empty). See `INCOMPLETE.md`.

## Architecture Overview

### Test Project Structure

```
tests/
├── conformance/                    # Golden fixture suites (project backbone — see Phase 5A)
│   ├── README.md
│   └── storyNN/<feature>/
│       ├── tyhp.json               # optional suite-level project config
│       ├── manifest.json           # machine-readable expectations (required)
│       ├── README.md               # human notes (optional)
│       ├── *.tyhp / *.tyhpdef      # inputs
│       └── expected/               # golden PHP output (Wave B — Story 09+)
├── Tyhp.Tests/
│   ├── Tyhp.Tests.csproj
│   ├── GlobalUsings.cs
│   ├── TestHelpers/
│   │   ├── ParserTestHelper.cs
│   │   ├── AstAssertions.cs
│   │   ├── DiagnosticAssertions.cs
│   │   ├── TestFileManager.cs
│   │   ├── SnapshotManager.cs
│   │   ├── ConformanceManifest.cs
│   │   └── ConformanceRunner.cs
│   ├── Conformance/
│   │   └── ConformanceSuiteTests.cs
│   ├── Parser/
│   │   ├── ExampleFileParseTests.cs
│   │   ├── TyhpdefFileParseTests.cs
│   │   ├── PhpFileParseTests.cs
│   │   ├── EdgeCaseParseTests.cs
│   │   └── AstSerializationTests.cs
│   ├── Binder/
│   │   ├── ScopeTreeTests.cs
│   │   ├── NameResolutionTests.cs
│   │   ├── DuplicateDeclarationTests.cs
│   │   ├── NamespaceMergingTests.cs
│   │   ├── TraitBindingTests.cs
│   │   ├── GenericBindingTests.cs
│   │   └── TyhpdefLoadingTests.cs
│   ├── Checker/
│   │   ├── TypeCompatibilityTests.cs
│   │   ├── TypeNarrowingTests.cs
│   │   ├── TypeGuardTests.cs
│   │   ├── GenericConstraintTests.cs
│   │   ├── VisibilityCheckTests.cs
│   │   ├── ValidCodeNoErrorsTests.cs
│   │   └── ErrorCodeCoverageTests.cs
│   ├── Emitter/
│   │   ├── PhpPassThroughTests.cs
│   │   ├── StructEmitTests.cs
│   │   ├── GenericEmitTests.cs
│   │   ├── ExtensionMethodEmitTests.cs
│   │   ├── OperatorOverloadEmitTests.cs
│   │   ├── DisposableEmitTests.cs
│   │   ├── AsyncAwaitEmitTests.cs
│   │   └── CompileTimeConstructTests.cs
│   ├── EndToEnd/
│   │   ├── FullPipelineTests.cs
│   │   ├── SnapshotTests.cs
│   │   └── PhpOutputValidationTests.cs
│   ├── Integration/
│   │   ├── BuildPipelineTests.cs
│   │   ├── IncrementalCompilationTests.cs
│   │   ├── DiagnosticReportingTests.cs
│   │   └── ConfigurationTests.cs
│   ├── Diagnostics/
│   │   ├── DiagnosticBagTests.cs
│   │   ├── DiagnosticFormattingTests.cs
│   │   └── MessageCodeTests.cs
│   ├── Snapshots/
│   │   └── (golden files stored here, organized by test category)
│   └── TestData/
│       ├── ValidTyhp/
│       ├── InvalidTyhp/
│       ├── ExpectedPhpOutput/
│       └── MinimalTyhpdef/
```

### Technology Stack

- **Test Framework:** xUnit (most common for modern .NET, wide tooling support, `[Fact]` / `[Theory]` attributes)
- **Assertions:** xUnit built-in + FluentAssertions (for readable assertion chains)
- **Mocking:** (if needed) NSubstitute — only for integration tests requiring service mocks
- **Snapshot Testing:** Custom lightweight snapshot manager (compare output against golden files)
- **PHP Validation:** Conditionally required — shell out to `php -l` for syntax checking emitted PHP; auto-skip with `[Trait("Category", "PHP")]` if PHP is not available on PATH
- **PHP Runtime Package Tests:** PHPUnit (separate from the .NET test project, run via shell)

### Conventions and Patterns

1. **Test Naming:** `MethodUnderTest_Scenario_ExpectedBehavior` (e.g., `Parse_EmptyFile_ReturnsMinimalAst`, `Bind_DuplicateClass_ProducesDiagnostic`)
2. **Test Data:** Inline for simple cases, file-based for complex `.tyhp`/`.php` comparisons
3. **Snapshot Tests:** Golden files stored in `tests/Tyhp.Tests/Snapshots/` with a `SnapshotManager` that reads/writes/compares, and supports an environment variable `UPDATE_SNAPSHOTS=true` to regenerate
4. **Test Categories:** Use xUnit `[Trait("Category", "...")]` to categorize: `Parser`, `Binder`, `Checker`, `Emitter`, `EndToEnd`, `Integration`, `Diagnostics`, `Conformance`
5. **Shared Helpers:** All reusable parsing/assertion logic in `TestHelpers/` — no test class should directly instantiate ANTLR lexer/parser
6. **Test Isolation:** Each test must be fully independent — no shared mutable state between tests
7. **Test Data Files:** Small, focused `.tyhp` snippets in `TestData/` — all test inputs are self-contained generated files, NOT references to the `Examples/` directory (which is for brainstorming only)

### Integration with Existing Codebase

- The test project references the main `tyhp.csproj` to access all compiler internals
- Tests exercise the public (and internal, via `InternalsVisibleTo`) APIs of each compiler phase
- The bundled PHP extension tyhpdef Composer package (`tyhp/php-8.2`, source at `runtime/php-extensions/`) serves as a source of extension tyhpdef parse tests
- The runtime package tyhpdef files (generated by Story 20 Track C under `runtime/packages/*/`) serve as runtime type definition parse tests
- The diagnostic system (Story 01's `DiagnosticBag`, `IDiagnostic`, `CompilationResult`) is tested early as foundation

> **Note:** Stories 04 and 07 originally referenced a monorepo `runtime/composer.json` root pattern. Story 21 establishes standalone Composer packages without a monorepo root. Test paths referencing `runtime/php-extensions/` or `runtime/packages/` should be updated when Story 21 is implemented to match the final directory layout. Until then, use the paths as established by Story 04.

---

## Phase 1: Test Project Setup and Foundation

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create the xUnit test project, add it to the solution, configure project references and dependencies, establish global usings, and create the core test helper infrastructure that all subsequent test phases will use.

### Deliverables

- `tests/Tyhp.Tests/Tyhp.Tests.csproj` — xUnit test project targeting `net9.0`
- `tests/Tyhp.Tests/GlobalUsings.cs` — shared using statements for all test files
- `tests/Tyhp.Tests/TestHelpers/ParserTestHelper.cs` — helper to parse `.tyhp`, `.php`, and `.tyhpdef` content/files into ASTs
- `tests/Tyhp.Tests/TestHelpers/TestFileManager.cs` — helper to locate test data files, example files, and tyhpdef files relative to the project
- `tests/Tyhp.Tests/TestHelpers/AstAssertions.cs` — custom assertion methods for AST node validation
- `tests/Tyhp.Tests/TestHelpers/DiagnosticAssertions.cs` — custom assertion methods for diagnostics (error codes, severities, messages)
- `tests/Tyhp.Tests/TestHelpers/SnapshotManager.cs` — snapshot testing utility (compare, update, report diffs)
- Updated `tyhp.sln` — test project added to the solution
- Updated `tyhp.csproj` — `InternalsVisibleTo` attribute added for test project access to internal types
- `tests/Tyhp.Tests/TestData/` directory with initial placeholder structure
- A single smoke test (`tests/Tyhp.Tests/SmokeTest.cs`) that verifies the test infrastructure works (project compiles, can reference main project types)

### Implementation Details

**`Tyhp.Tests.csproj` configuration:**
- Target `net9.0` to match the main project
- Reference `tyhp.csproj` via relative path (`<ProjectReference Include="../../tyhp.csproj" />`)
- Add xUnit packages: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`
- Add FluentAssertions package (latest stable version)
- Add `coverlet.collector` for code coverage
- Add NSubstitute package (latest stable version) — for integration tests only
- Set `<IsPackable>false</IsPackable>` and `<IsPublishable>false</IsPublishable>`

**`InternalsVisibleTo` on main project:**
- Add `InternalsVisibleTo` inline in the `.csproj` file (modern approach, no extra file needed):

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Tyhp.Tests" />
</ItemGroup>
```

- This allows tests to access `internal` classes in the compiler (e.g., visitor internals, binder internals)

**`ParserTestHelper` responsibilities:**
- `ParseTyhpContent(string tyhpSource, string fileName = "test.tyhp")` — parse a string of Tyhp source code into an AST, return the `SrcFileAst` and any parse errors
- `ParsePhpContent(string phpSource, string fileName = "test.php")` — parse PHP source code
- `ParseTyhpdefContent(string tyhpdefSource, string fileName = "test.tyhpdef")` — parse tyhpdef source code
- `ParseFile(string filePath)` — parse a file from disk (auto-detect language mode from extension)
- Each method should return a result object containing the AST and a collection of diagnostics/errors
- Internally creates ANTLR lexer/parser, attaches error listeners, runs the visitor
- Thread-safe (each call creates its own lexer/parser instances)

**`TestFileManager` responsibilities:**
- `GetExtensionTyhpdefDirectory()` — return the absolute path to the bundled PHP extension tyhpdefs (source at `runtime/php-extensions/`, Composer package `tyhp/php-8.2`)
- `GetRuntimePackagesDirectory()` — return the absolute path to `runtime/packages/`
- `GetTestDataDirectory()` — return the absolute path to `tests/Tyhp.Tests/TestData/`
- `GetSnapshotsDirectory()` — return the absolute path to `tests/Tyhp.Tests/Snapshots/`
- `GetAllExtensionTyhpdefFiles()` — enumerate all `.tyhpdef` files in the bundled extension tyhpdef directory
- `GetAllRuntimePackageTyhpdefFiles()` — enumerate all `.tyhpdef` files under `runtime/packages/*/`
- `GetAllTestDataFiles(string subdirectory, string extension)` — enumerate test data files by category
- Handles relative path resolution from the test project directory to the repo root

**`SnapshotManager` responsibilities:**
- `AssertMatchesSnapshot(string actualContent, string snapshotName, string category)` — compare actual output against a golden file; fail with diff if mismatch; create the golden file if it doesn't exist
- `UpdateSnapshot(string content, string snapshotName, string category)` — overwrite the golden file
- Check for `UPDATE_SNAPSHOTS` environment variable — when set to `true`, automatically update instead of failing
- Store snapshots organized by category subdirectories under `Snapshots/`

**Solution file update:**
- Add the test project to `tyhp.sln` with a unique GUID
- Place it in a `Tests` solution folder

### Acceptance Criteria

- [ ] `dotnet build` on the solution succeeds (both main project and test project compile)
- [ ] `dotnet test` discovers and runs the single smoke test, which passes
- [ ] The smoke test verifies: can instantiate `ParserTestHelper`, can reference `Base2Ast`, `SrcFileAst`, `MessageCode`, and other key types from the main project
- [ ] `TestFileManager` correctly resolves paths to `runtime/php-extensions/`, `runtime/packages/`, and `TestData/` from the test project location
- [ ] All `TestHelpers/` classes compile and are accessible from test classes

### Dependencies

- **Depends on:** Nothing — this is the foundational phase
- **Provides for:** All subsequent phases use `ParserTestHelper`, `TestFileManager`, `AstAssertions`, `DiagnosticAssertions`, and `SnapshotManager`

---

## Phase 2: Parser Tests — Generated Test Data and Tyhpdef Files

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create parser tests that verify Tyhp, PHP, and tyhpdef parsing against generated test data files and real-world tyhpdef files. All test inputs are self-contained files under `TestData/` — the `Examples/` directory is NOT used for testing. This validates the parser and visitor against a comprehensive set of inputs and establishes a regression safety net.

### Deliverables

- `tests/Tyhp.Tests/Parser/TyhpParseTests.cs` — parameterized tests for generated `.tyhp` test data files
- `tests/Tyhp.Tests/Parser/PhpParseTests.cs` — parameterized tests for generated `.php` test data files
- `tests/Tyhp.Tests/Parser/TyhpdefParseTests.cs` — parameterized tests for generated `.tyhpdef` test data files
- `tests/Tyhp.Tests/Parser/ExtensionTyhpdefParseTests.cs` — tests for bundled PHP extension tyhpdef files (Composer package `tyhp/php-8.2`, source at `runtime/php-extensions/`)
- `tests/Tyhp.Tests/Parser/RuntimePackageTyhpdefParseTests.cs` — tests for runtime package tyhpdef files generated by Story 20 Track C (under `runtime/packages/*/`)
- `tests/Tyhp.Tests/TestData/ValidTyhp/parser/` — generated `.tyhp` test files covering all major constructs
- `tests/Tyhp.Tests/TestData/ValidTyhpdef/parser/` — generated `.tyhpdef` test files covering tyhpdef grammar constructs
- `tests/Tyhp.Tests/TestData/ValidPhp/parser/` — generated `.php` test files for PHP parser path validation

### Implementation Details

**`TyhpParseTests`:**
- Use `[Theory]` with `[MemberData]` that enumerates all `.tyhp` files from `TestData/ValidTyhp/parser/` via `TestFileManager.GetAllTestDataFiles("ValidTyhp/parser", ".tyhp")`
- Each test: parse the file using `ParserTestHelper.ParseFile(path)`, assert zero parse errors
- Assert the resulting AST is not null and has at least one child node
- Test method name pattern: `ParseTyhpFile_NoErrors_ForTestDataFile(string filePath)`

**Generated `.tyhp` test files (create in `TestData/ValidTyhp/parser/`):**
- `class_declaration.tyhp` — namespace + class with properties, methods, constants
- `interface_declaration.tyhp` — interface with method signatures
- `trait_declaration.tyhp` — trait with methods and use adaptations
- `enum_declaration.tyhp` — enum with cases, backed type, methods
- `struct_declaration.tyhp` — struct with typed properties and defaults
- `function_declaration.tyhp` — top-level functions with various signatures
- `generic_class.tyhp` — generic class with constraints
- `extension_declaration.tyhp` — extension with method using `extends` parameter
- `operator_overload.tyhp` — operator overload declarations
- `type_alias.tyhp` — type alias declarations (root and class-level)
- `async_function.tyhp` — async function with await and async foreach
- `control_flow.tyhp` — if/else/for/foreach/while/switch/match
- `disposable_assignment.tyhp` — `:=` disposable assignment syntax
- `short_function_syntax.tyhp` — `fn` short function declarations
- `property_accessors.tyhp` — property get/set accessors
- `type_guard.tyhp` — type guard function with `$param is Type` return
- `with_keyword.tyhp` — `with` keyword usage on structs and objects
- `compile_time_constructs.tyhp` — `nameof()`, `typeof()`, `default()`, `variable_exists()`
- Each file should be 10-30 lines, testing exactly one construct group

**`PhpParseTests`:**
- Same pattern but for `.php` files in `TestData/ValidPhp/parser/`
- These validate the PHP parser/visitor path
- Generate 3-5 PHP test files covering: class, function, control flow, namespace, traits

**`TyhpdefParseTests`:**
- Use `[Theory]` with `[MemberData]` enumerating all `.tyhpdef` files from `TestData/ValidTyhpdef/parser/` via `TestFileManager.GetAllTestDataFiles("ValidTyhpdef/parser", ".tyhpdef")`
- Each test: parse the file, assert zero parse errors
- Use `[Trait("Category", "Parser")]` and `[Trait("Category", "Tyhpdef")]` for filtering

**Generated `.tyhpdef` test files (create in `TestData/ValidTyhpdef/parser/`):**
- `class_definition.tyhpdef` — class with typed properties, methods, constants
- `interface_definition.tyhpdef` — interface with method signatures
- `function_definitions.tyhpdef` — top-level function declarations with various parameter/return types
- `enum_definition.tyhpdef` — enum with cases and backed type
- `namespace_and_use.tyhpdef` — namespace declarations and use statements
- `generic_types.tyhpdef` — generic class/interface with type parameters and constraints
- `union_and_intersection_types.tyhpdef` — union types, intersection types, nullable types
- Each file should be 10-30 lines, testing exactly one construct group

**`ExtensionTyhpdefParseTests`:**
- Use `[Theory]` with `[MemberData]` enumerating all `.tyhpdef` files from the bundled PHP extension tyhpdef Composer package (source at `runtime/php-extensions/`)
- Each test: parse the file, assert zero parse errors
- Validates the 16 bundled extension tyhpdef files

**`RuntimePackageTyhpdefParseTests`:**
- Parse all `.tyhpdef` files found under `runtime/packages/*/` (from Story 20 Track C's `_tyhpdef/` output) — assert no parse errors
- Parse any supporting `.tyhp` files found under `runtime/packages/*/_tyhpdef/support/` — assert no parse errors
- These are the auto-generated type definitions for the Tyhp runtime libraries (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`)
- Note: TyhpSpec files (`Tyhp/TyhpSpec/`) were removed in Story 04 — runtime libraries now use `package.tyhp.json` manifests instead

**Error collection strategy:**
- The `ParserTestHelper` must capture ANTLR parse errors (via the error listener) and visitor errors
- Tests assert the error collection is empty (or contains only expected warnings/infos)
- If a file has known parse issues, document them with `[Trait("Known", "ParseIssue")]` and use `Skip` or assert the specific expected error

### Acceptance Criteria

- [ ] All generated `.tyhp` test data files parse without errors (18+ files covering all major constructs)
- [ ] All generated `.php` test data files parse without errors
- [ ] All generated `.tyhpdef` test data files parse without errors (7+ files covering tyhpdef grammar constructs)
- [ ] All 16 bundled extension tyhpdef files parse without errors
- [ ] All runtime package tyhpdef files parse without errors
- [ ] Tests are parameterized — adding a new test data file automatically includes it in the test suite
- [ ] `dotnet test --filter "Category=Parser"` runs all parser tests

### Dependencies

- **Depends on:** Phase 1 (test project, `ParserTestHelper`, `TestFileManager`)
- **Provides for:** Phase 5 (snapshot tests baseline), Phase 3 (parser edge case patterns)

---

## Phase 3: Parser Tests — Edge Cases, Error Handling, and AST Validation

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create parser tests for edge cases (empty files, minimal constructs, syntax errors, ambiguous constructs) and AST structural validation (correct node types, correct child counts, correct token values). Also test AST serialization/deserialization round-trips.

### Deliverables

- `tests/Tyhp.Tests/Parser/EdgeCaseParseTests.cs` — tests for boundary conditions and unusual inputs
- `tests/Tyhp.Tests/Parser/AstStructureTests.cs` — tests that verify specific AST node structures for known inputs
- `tests/Tyhp.Tests/Parser/AstSerializationTests.cs` — round-trip serialization tests
- `tests/Tyhp.Tests/Parser/ErrorRecoveryTests.cs` — tests that verify error recovery behavior (parser produces partial ASTs on invalid input)
- `tests/Tyhp.Tests/TestData/ValidTyhp/` — small focused `.tyhp` snippets for structural tests
- `tests/Tyhp.Tests/TestData/InvalidTyhp/` — intentionally invalid `.tyhp` snippets for error tests

### Implementation Details

**`EdgeCaseParseTests`:**
- Empty file (just `<?tyhp`) — should parse with no errors and produce a minimal AST
- File with only whitespace and comments — should parse correctly
- File with only a namespace declaration (no body) — valid, should parse
- File with deeply nested blocks (10+ levels) — verify no stack overflow
- File with very long lines (10,000+ characters) — verify no truncation
- File with every PHP keyword as a variable name context — verify no conflicts
- File with Unicode identifiers (if supported) or Unicode string content
- File with mixed language modes (`<?php` and `<?tyhp` blocks)
- File with all supported literal types (integers, floats, strings, heredocs, nowdocs, booleans, null)

**`AstStructureTests`:**
- Parse a simple class declaration, verify the AST has the correct structure: `SrcFileAst` → namespace → class declaration → properties/methods
- Parse a simple function declaration, verify parameter AST nodes have correct types/names
- Parse a struct declaration, verify struct-specific AST nodes are produced
- Parse generic type usage (`array<int, string>`), verify generic argument AST nodes
- Parse an extension method declaration, verify the `extends` keyword parameter is captured
- Parse operator overload declarations, verify the operator and operand types are in the AST
- Parse type alias declarations, verify the aliased type is captured
- Parse import/use statements, verify alias handling
- For each test, use `AstAssertions` to validate node types, child counts, and key property values
- These tests parse inline string content (not files), keeping the test data co-located

**`AstSerializationTests`:**
- Parse a `.tyhp` file → serialize the AST via `Base2Ast.Serialize()` (custom binary format) → deserialize → structurally compare the deserialized AST against the original
- **Important:** Serialization is custom binary, not JSON. Comparison must be done by walking both AST trees and comparing node types, child counts, and key property values using `AstAssertions` helpers — NOT by comparing raw byte arrays
- Verify that round-trip (serialize → deserialize) preserves: node types, child structure, `ValueString`, `ValueInt64`, `ValueDecimal`, `ValueBoolean`, `Flags`, `DocComment`
- Test with multiple test data files from `TestData/ValidTyhp/` to cover different node types
- Verify `AstCacheService.AddOrUpdate()` and cache retrieval produce structurally identical ASTs

**`ErrorRecoveryTests`:**
- Parse a file with a missing semicolon — verify an error is reported but a partial AST is still produced
- Parse a file with an unclosed brace — verify error and partial AST
- Parse a file with an invalid type annotation — verify error at the correct line/column
- Parse a file with duplicate class declarations — verify parsing succeeds (duplicate detection is the binder's job)
- Verify that `ErrorAst` nodes (from Story 01's visitor error handling refactor) are properly placed in the tree at locations of invalid syntax

**Test data files:**
- Create small, focused `.tyhp` files in `TestData/ValidTyhp/` — e.g., `simple_class.tyhp`, `simple_function.tyhp`, `struct_declaration.tyhp`, `generic_class.tyhp`
- Create invalid `.tyhp` files in `TestData/InvalidTyhp/` — e.g., `missing_semicolon.tyhp`, `unclosed_brace.tyhp`, `invalid_type.tyhp`
- Each test data file should be minimal (5-20 lines) testing exactly one construct

### Acceptance Criteria

- [ ] All edge case tests pass (empty file, whitespace, deep nesting, long lines)
- [ ] AST structure tests verify correct node types and child counts for at least 8 different Tyhp constructs
- [ ] Serialization round-trip tests pass for at least 5 test data files
- [ ] Error recovery tests demonstrate that the parser produces diagnostics for invalid input (or documents that exception-based error handling from the visitor needs Story 01 refactoring)
- [ ] All test data files are committed to `TestData/`
- [ ] `dotnet test --filter "Category=Parser"` passes

### Dependencies

- **Depends on:** Phase 1 (test helpers), Phase 2 (basic parse tests passing)
- **Provides for:** Phase 7 (emitter snapshot baselines), Phase 4 (binder test patterns)

---

## Phase 4: Diagnostics System Tests

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Test the diagnostic infrastructure established by Story 01: `DiagnosticBag`, `IDiagnostic`, `Diagnostic`, `CompilationResult`, and `DiagnosticSeverity`. Story 01 is complete before Story 07 — all diagnostic types are fully functional and available for testing.

### Deliverables

- `tests/Tyhp.Tests/Diagnostics/DiagnosticBagTests.cs` — unit tests for `DiagnosticBag` thread safety, filtering, counting, enumeration
- `tests/Tyhp.Tests/Diagnostics/DiagnosticTests.cs` — unit tests for `Diagnostic` record/class creation and factory methods
- `tests/Tyhp.Tests/Diagnostics/CompilationResultTests.cs` — unit tests for `CompilationResult` aggregation and exit code determination
- `tests/Tyhp.Tests/Diagnostics/MessageCodeTests.cs` — tests verifying `MessageCode` numbering scheme and completeness
- `tests/Tyhp.Tests/Diagnostics/DiagnosticFormattingTests.cs` — tests for diagnostic display/formatting

### Implementation Details

**`DiagnosticBagTests`:**
- Test `Add()` — adding a single diagnostic, verify `All` contains it
- Test `AddError()` / `AddWarning()` / `AddInfo()` convenience methods
- Test `HasErrors` returns `true` only when error-severity diagnostics exist
- Test `HasWarnings` returns `true` only when warning-severity diagnostics exist
- Test `ErrorCount`, `WarningCount`, `InfoCount` return correct counts
- Test `Errors` filtered view contains only errors
- Test `Warnings` filtered view contains only warnings
- Test `All` returns diagnostics ordered by file then line
- Test `AddRange()` merges another collection correctly
- Test thread safety: add diagnostics from multiple threads concurrently, verify all are captured with correct counts
- Test `IEnumerable<IDiagnostic>` implementation with LINQ queries
- Test `DisplayAll()` produces output (verify via captured console output or formatter delegate)

**`DiagnosticTests`:**
- Test `Diagnostic.Error()` factory creates a diagnostic with `DiagnosticSeverity.Error`
- Test `Diagnostic.Warning()` factory creates a diagnostic with `DiagnosticSeverity.Warning`
- Test `Diagnostic.Info()` factory creates a diagnostic with `DiagnosticSeverity.Info`
- Test that all properties (`Code`, `FileName`, `Line`, `Column`, `Message`, `FormatParams`) are correctly set
- Test `EndLine` / `EndColumn` optional properties
- Test value equality (if `record class` is used)

**`CompilationResultTests`:**
- Test `Success` returns `true` when `DiagnosticBag` has no errors
- Test `Success` returns `false` when `DiagnosticBag` has at least one error
- Test `GetExitCode()` returns `ExitCode.Success` when no diagnostics
- Test `GetExitCode()` returns `ExitCode.CompileError` when errors exist
- Test `GetExitCode()` returns `ExitCode.CompileWarning` when only warnings exist
- Test that `ParsedFiles`, `GlobalScope`, `OutputFiles` can be set after construction
- Test duration tracking properties (`ParseDuration`, `BindDuration`, etc.)

**`MessageCodeTests`:**
- Test that all `MessageCode` values follow the numbering scheme: 1000s parser, 2000s visitor, 3000s binder, 4000s checker, 5000s emitter
- Test that no two `MessageCode` values share the same numeric value (no duplicates)
- Test that ranges 6000s, 7000s, 8000s, 9000s are reserved (no codes in those ranges unless intentionally added)
- Verify the existing codes: `ParserUnknownError=1001`, `ParserUnexpectedError=1002`, `ParserCompileAborted=1003`, etc.
- Test that Story 01's visitor codes (2002, 2003, 2004) exist

**`DiagnosticFormattingTests`:**
- Test that `Display()` on a diagnostic produces the expected console output format
- Test formatting with different severities (error, warning, info)
- Test formatting with format parameters (e.g., `"Unexpected token '{0}'"` with `";"`)
- Test that `Message.LocalizeErrorCode()` produces the expected key format (`ERROR_TYHP1001`, etc.)
- If localization resource files exist (Story 01f), test that localized messages are returned

**Story 01 dependency:**
- These tests exercise Story 01's `DiagnosticBag`, `Diagnostic`, `CompilationResult`, and related types.
- When Story 01 is landed, implement these as active, running tests. If Story 01 is not yet in place, author the test bodies and gate them with `[Fact(Skip = "PLACEHOLDER_STORY_01: ...")]`, activating once it lands (per the incremental-authoring policy).

### Acceptance Criteria

- [ ] `MessageCodeTests` pass (validates `MessageCode` enum)
- [ ] All diagnostic tests pass (Story 01 is complete before Story 07)
- [ ] Thread safety test for `DiagnosticBag` exercises at least 4 concurrent threads adding 100+ diagnostics each
- [ ] No `MessageCode` duplicates detected
- [ ] `dotnet test --filter "Category=Diagnostics"` runs all diagnostic tests

### Dependencies

- **Depends on:** Phase 1 (test project), Story 01 (diagnostic system — complete before Story 07)
- **Provides for:** Phase 5 (binder tests use `DiagnosticAssertions`), Phase 6 (checker tests use diagnostic assertions)

---

## Phase 5: Binder Tests

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create tests for the binder's scope tree construction, symbol registration, and name resolution. These tests validate that the binder correctly walks the AST, builds scope hierarchies, and resolves references. Tests should be structured to be implementable incrementally as Story 02 progresses.

### Deliverables

- `tests/Tyhp.Tests/Binder/ScopeTreeTests.cs` — tests for scope hierarchy construction from various AST patterns
- `tests/Tyhp.Tests/Binder/SymbolRegistrationTests.cs` — tests for symbol creation and registration in scopes
- `tests/Tyhp.Tests/Binder/NameResolutionTests.cs` — tests for name resolution across scope chains
- `tests/Tyhp.Tests/Binder/DuplicateDeclarationTests.cs` — tests for duplicate symbol detection
- `tests/Tyhp.Tests/Binder/NamespaceMergingTests.cs` — tests for merging namespace scopes across files
- `tests/Tyhp.Tests/Binder/TraitBindingTests.cs` — tests for trait use and adaptation binding
- `tests/Tyhp.Tests/Binder/GenericBindingTests.cs` — tests for generic type parameter binding
- `tests/Tyhp.Tests/Binder/TyhpdefLoadingTests.cs` — tests for loading tyhpdef symbols into global scope
- `tests/Tyhp.Tests/TestData/ValidTyhp/binder/` — small focused `.tyhp` files for binder test inputs

### Implementation Details

**General binder test pattern:**
Each binder test follows a consistent pattern:
1. Parse a `.tyhp` string or file into an AST using `ParserTestHelper`
2. Run the binder on the AST(s) to produce a `GlobalScope` (or equivalent scope tree root)
3. Assert scope tree structure, symbol presence, and name resolution results
4. Assert diagnostics (errors for invalid patterns, no errors for valid patterns)

**`ScopeTreeTests`:**
- Parse a file with a single namespace and class — verify `GlobalScope` → `NamespaceScope` → `ObjectDeclarationScope` hierarchy
- Parse a file with nested blocks (function with if/else/for) — verify `FunctionDeclarationScope` → `CodeBlockScope` nesting
- Parse a file with multiple namespaces — verify multiple `NamespaceScope` children of `GlobalScope`
- Parse a file with anonymous functions — verify `AnonymousFunctionScope` creation
- Parse a file with anonymous classes — verify `AnonymousObjectDeclarationScope` creation
- Parse a file with declare blocks — verify `DeclareBlockScope` creation
- Parse a file with labels — verify `LabelScope` creation
- Verify scope parent references are correctly set (child's `ContainingScope` points to parent)

**`SymbolRegistrationTests`:**
- Parse a class with properties, methods, constants — verify corresponding symbols exist in the `ObjectDeclarationScope`
- Parse a function with parameters and local variables — verify `VariableSymbol` instances
- Parse a class with constructor promotion — verify promoted parameters become property symbols
- Parse an enum declaration — verify enum cases are registered as `ObjectConstant` symbols
- Parse a trait with methods — verify method symbols
- Parse an extension class — verify method symbols with `extends` parameter info
- Verify each symbol has correct: `Name`, `FullyQualifiedName`, `SymbolType`, `SourceFile`, `Line`, `Column`

**`NameResolutionTests`:**
- Simple name resolution: variable `$x` in a function that has `$x` in scope — resolves correctly
- Qualified name resolution: `\App\Models\User` resolves to the class declared in that namespace
- Relative name resolution: within `namespace App`, `Models\User` resolves to `\App\Models\User`
- Use/import alias resolution: `use App\Models\User as U;` then `U` resolves to `User`
- `self`, `static`, `parent` resolution within class methods
- Member resolution: `$obj->property` resolves to the correct property symbol
- Static member resolution: `MyClass::CONST` resolves to the correct constant symbol
- Unresolved reference: using a name that doesn't exist produces a `BinderSymbolNotFound` diagnostic
- Scope chain walking: variable defined in outer scope is visible in inner scope (except closures without `use`)

**`DuplicateDeclarationTests`:**
- Two classes with the same name in the same namespace — should produce `BinderDuplicateSymbolDeclaration`
- Two functions with the same name in the same namespace — should produce duplicate error
- **Duplicate variable declarations:** Tyhp does not have `let`/`const` declaration keywords — variables are created on first assignment (following PHP semantics). Re-assigning a variable to a value of a **type-compatible** type is allowed. Re-assigning a variable to a value of an **incompatible** type (changing the variable's type) is a checker error unless `unset()` is called on the variable first. Test expectations:
  - `$x = 'hello'; $x = 'world';` — allowed (same type, string → string)
  - `$x = 'hello'; $x = 42;` — checker error (type change, string → int, without unset)
  - `$x = 'hello'; unset($x); $x = 42;` — allowed (unset clears the type binding)
  - `$x = new Foo(); $x = new Bar();` where `Bar extends Foo` — allowed (type-compatible assignment)
- Overloaded functions (same name, different signatures) — should NOT produce duplicate error (Tyhp supports overloads)

**`NamespaceMergingTests`:**
- Two files both declaring `namespace App\Models` with different classes — after binding, the merged `NamespaceScope` should contain all classes from both files
- File-level constructs (variables, declare directives) should remain in their `FileScope`

**`TraitBindingTests`:**
- Class using a trait — verify trait methods are accessible
- Trait with alias adaptation — verify renamed methods are registered
- Trait with precedence adaptation — verify conflict resolution

**`GenericBindingTests`:**
- Class with generic type parameters — verify `GenericTypeParameterSymbol` instances
- Function with generic type parameters — verify binding
- Generic constraints (`T extends SomeType`) — verify constraint is captured

**`TyhpdefLoadingTests`:**
- Load a minimal tyhpdef file with a single class declaration — verify class symbol in global scope
- Load a tyhpdef with functions and constants — verify all symbols registered
- Load multiple tyhpdef files — verify no conflicts (or expected merge behavior)
- Verify deprecated/obsolete markers are captured on tyhpdef symbols

**Test data for binder:**
Create small `.tyhp` files in `TestData/ValidTyhp/binder/`:
- `simple_class_in_namespace.tyhp` — single namespace with single class
- `multiple_namespaces.tyhp` — two namespaces
- `class_with_members.tyhp` — class with properties, methods, constants
- `nested_scopes.tyhp` — function with nested if/for blocks
- `trait_usage.tyhp` — class using a trait with adaptations
- `generic_class.tyhp` — generic class with constraints

Create minimal `.tyhpdef` files in `TestData/MinimalTyhpdef/`:
- `simple_class.tyhpdef` — single class import
- `functions_and_constants.tyhpdef` — function and constant imports

**Story 02 dependency:**
- These tests exercise Story 02's binder, scope tree, symbol system, and name resolution.
- When Story 02 is landed, implement these as active, running tests. If the binder is not yet in place, author the test bodies and gate them with `[Fact(Skip = "PLACEHOLDER_STORY_02: ...")]`, activating once it lands (per the incremental-authoring policy).

### Acceptance Criteria

- [ ] All binder tests pass (Story 02 is complete before Story 07)
- [ ] Scope tree tests verify at least 6 different scope hierarchy patterns
- [ ] Name resolution tests cover simple, qualified, relative, aliased, and unresolved scenarios
- [ ] Duplicate declaration tests verify the `BinderDuplicateSymbolDeclaration` diagnostic is produced
- [ ] Namespace merging tests verify cross-file merging
- [ ] `dotnet test --filter "Category=Binder"` runs all binder tests

### Dependencies

- **Depends on:** Phase 1 (test helpers), Phase 2/3 (parser tests proving inputs parse correctly), Story 02 (binder implementation)
- **Provides for:** Phase 5A (conformance harness), Phase 6 (checker tests assume binding works)

---

## Phase 5A: Conformance Harness Shell (Wave A capstone)

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Establish `tests/conformance/` as the **golden conformance fixture backbone** (`CONVENTIONS.md` §5). This phase wires an automated runner in `Tyhp.Tests` that discovers fixture suites, loads machine-readable expectations, and asserts diagnostics (Wave A: **lint-level**). It replaces manual `dotnet run -- lint …` verification commands with `dotnet test --filter "Category=Conformance"`.

Wave B extensions (build output goldens, `.tyhp → .php` diffs, runtime self-host) are **documented and scaffolded here** but activated only when Stories 09–10 land (see below).

### Deliverables

- `tests/conformance/README.md` — layout conventions, how to add a new suite, manifest schema summary
- `tests/conformance/story06/tagless/manifest.json` — machine-readable expectations migrated from the existing README table
- `tests/Tyhp.Tests/TestHelpers/ConformanceManifest.cs` — deserialize `manifest.json` into typed models
- `tests/Tyhp.Tests/TestHelpers/ConformanceRunner.cs` — discover suites, run cases, assert expectations
- `tests/Tyhp.Tests/Conformance/ConformanceSuiteTests.cs` — `[Theory]` over all manifest cases; `[Trait("Category", "Conformance")]`
- `tests/Tyhp.Tests/TestHelpers/TestFileManager.cs` — extend with `GetConformanceDirectory()` and `GetAllConformanceManifests()`
- Updated `tests/readme.md` — pointer to conformance layout and `dotnet test --filter "Category=Conformance"`
- `tests/conformance/_self_host/README.md` — documents the deferred runtime self-host check (activated in Wave B after Story 10)
- `tests/Tyhp.Tests/Conformance/SelfHostRuntimeConformanceTests.cs` — `[Fact(Skip = "PLACEHOLDER_STORY_10: runtime self-host diff requires build pipeline")]`

### Fixture layout (canonical)

Every conformance suite lives under `tests/conformance/storyNN/<feature>/`:

```
tests/conformance/story06/tagless/
├── tyhp.json              # suite-level config (e.g. source.tagless, phpVersion)
├── manifest.json          # required — expectations the runner asserts
├── README.md              # optional human notes (keep in sync with manifest)
├── *.tyhp / *.tyhpdef     # input fixtures
└── expected/              # optional golden PHP (Wave B only — Story 09+)
    └── *.php
```

New stories add suites under `tests/conformance/storyNN/` as part of their "Golden Fixtures / Tests (Acceptance)" obligation.

### `manifest.json` schema (Wave A — lint / diagnostics)

```json
{
  "suite": "story06/tagless",
  "description": "Story 06 Phase 7 — tagless source mode",
  "defaults": {
    "action": "lint",
    "config": { "source.tagless": true, "phpVersion": "8.2" }
  },
  "cases": [
    {
      "id": "tagless_function",
      "file": "tagless_function.tyhp",
      "expect": { "errorCount": 0 }
    },
    {
      "id": "tagless_close_tag_error",
      "file": "tagless_close_tag_error.tyhp",
      "expect": {
        "errorCount": { "min": 1 },
        "codes": [1004]
      }
    },
    {
      "id": "tagless_function_classic_mode",
      "file": "tagless_function.tyhp",
      "config": { "source.tagless": false },
      "expect": { "errorCount": 0 }
    }
  ]
}
```

**Field rules:**
- `suite` — path relative to `tests/conformance/` (matches directory)
- `defaults.config` — merged into per-case `config` (case values override)
- `defaults.action` — `lint` (Wave A) or `build` (Wave B — requires Story 10; runner skips or fails fast if not implemented)
- `cases[].file` — input path relative to the suite directory
- `cases[].expect.errorCount` — integer exact count, or `{ "min": N }` / `{ "max": N }`
- `cases[].expect.codes` — `MessageCode` numeric values (e.g. `1004` for `ParserCloseTagNotAllowed`) that must appear among error-severity diagnostics
- `cases[].expect.warnings` / `cases[].expect.noDiagnostics` — optional additional assertions
- `cases[].skip` — optional reason string; runner treats as xUnit skip

**Wave B fields (schema reserved; runner ignores until activated):**
- `cases[].expect.php` — path under `expected/` for golden PHP output comparison
- `cases[].action: "build"` — run full build pipeline and compare output files

### `ConformanceRunner` responsibilities

- `DiscoverManifests()` — enumerate every `tests/conformance/**/manifest.json` (exclude `tests/conformance/_self_host/`)
- `LoadManifest(string manifestPath)` — parse and validate schema; fail fast on unknown `action` values
- `RunCase(ConformanceCase case, ConformanceSuite suite)` — execute one case:
  1. Resolve suite directory and input file path
  2. Build a `Tyhp.Config.Project` from suite `tyhp.json` + case `config` overrides + explicit path to the input file
  3. For `action: "lint"` — invoke the lint pipeline **in-process** via `LintAction` / `CompilationService` (do not shell out to `dotnet run`)
  4. Collect `CompilationResult.Diagnostics` and assert against `expect` using `DiagnosticAssertions`
- `RunSuite(string suitePath)` — run all non-skipped cases in a manifest
- Thread-safe: each case uses fresh project/diagnostic state

### `ConformanceSuiteTests` pattern

```csharp
[Trait("Category", "Conformance")]
public class ConformanceSuiteTests
{
    public static IEnumerable<object[]> AllCases() =>
        ConformanceRunner.DiscoverAllCases(); // yields (suiteId, caseId)

    [Theory]
    [MemberData(nameof(AllCases))]
    public void ConformanceCase_MatchesManifest(string suiteId, string caseId)
    {
        ConformanceRunner.RunAndAssert(suiteId, caseId);
    }
}
```

### Story 06 tagless suite (first wired suite)

Migrate expectations from `tests/conformance/story06/tagless/README.md` into `manifest.json`:

| Case ID | File | Config | Expected |
|---------|------|--------|----------|
| `tagless_function` | `tagless_function.tyhp` | `source.tagless: true` | 0 errors |
| `tagless_with_open_tag` | `tagless_with_open_tag.tyhp` | `source.tagless: true` | 0 errors |
| `tagless_tyhpdef` | `tagless_tyhpdef.tyhpdef` | `source.tagless: true` | 0 errors |
| `tagless_close_tag_error` | `tagless_close_tag_error.tyhp` | `source.tagless: true` | ≥1 error, code `1004` |
| `tagless_function_classic_mode` | `tagless_function.tyhp` | `source.tagless: false` | 0 errors |

Keep the existing `tyhp.json` in the suite directory; `manifest.json` `defaults.config` should align with it.

### Wave B activation (not required for Wave A done)

When Stories 09–10 land, extend `ConformanceRunner` without changing the layout:

| Capability | Activates when | Behavior |
|------------|----------------|----------|
| `action: "build"` | Story 10 | Run `BuildAction`, assert output files exist at expected paths |
| `expect.php` golden diff | Story 09 | Compare emitted PHP to `expected/*.php` via `SnapshotManager` |
| `_self_host` suite | Story 10 | Recompile `runtime/packages/*/tyhp_src/`, diff against committed `src/` |
| Checker-error fixtures | Story 08 | Lint pipeline reports checker diagnostics; `expect.codes` in 4000s range |

Until then, manifests may include Wave B cases with `"skip": "PLACEHOLDER_STORY_09: build output golden"` or the runner returns a clear skip for `action: "build"`.

### Acceptance Criteria

- [ ] `tests/conformance/README.md` documents layout, manifest schema, and how stories add new suites
- [ ] `tests/conformance/story06/tagless/manifest.json` covers all five cases from the existing README
- [ ] `dotnet test --filter "Category=Conformance"` discovers and runs Story 06 tagless cases in-process
- [ ] All Wave A conformance cases pass against the current compiler
- [ ] `SelfHostRuntimeConformanceTests` exists and is skipped with `PLACEHOLDER_STORY_10`
- [ ] Adding a new `manifest.json` under `tests/conformance/` automatically includes its cases (no test code changes required)

### Dependencies

- **Depends on:** Phase 1 (`TestFileManager`, `DiagnosticAssertions`), Phase 4 (diagnostic assertion patterns), Story 01 (`CompilationResult`, lint pipeline), Story 06 (tagless fixtures — already committed)
- **Provides for:** Every later story's golden-fixture acceptance; Wave B build/emit/self-host activation (Stories 08–10)

---

## Phase 6: Checker Tests

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create tests for the type checker, covering type compatibility, type narrowing, generic constraints, visibility checks, and feature-specific validation. Tests should cover both valid code (no errors expected) and invalid code (specific errors expected).

### Deliverables

- `tests/Tyhp.Tests/Checker/TypeCompatibilityTests.cs` — tests for `IsAssignableTo`, `IsSubtypeOf`, union/intersection types
- `tests/Tyhp.Tests/Checker/TypeNarrowingTests.cs` — tests for control flow type narrowing
- `tests/Tyhp.Tests/Checker/TypeGuardTests.cs` — tests for type guard function validation
- `tests/Tyhp.Tests/Checker/GenericConstraintTests.cs` — tests for generic type parameter constraints
- `tests/Tyhp.Tests/Checker/VisibilityCheckTests.cs` — tests for member visibility enforcement
- `tests/Tyhp.Tests/Checker/ValidCodeNoErrorsTests.cs` — tests that valid code produces zero diagnostics
- `tests/Tyhp.Tests/Checker/ErrorCodeCoverageTests.cs` — tests ensuring each checker `MessageCode` has a triggering test case
- `tests/Tyhp.Tests/Checker/VariableStateTests.cs` — tests for `VariableState` tracking (definite assignment, nullability)
- `tests/Tyhp.Tests/Checker/CheckerStateTests.cs` — tests for `CheckerState` operations (snapshot, split, merge)
- `tests/Tyhp.Tests/TestData/ValidTyhp/checker/` — valid Tyhp snippets that should produce no checker errors
- `tests/Tyhp.Tests/TestData/InvalidTyhp/checker/` — invalid Tyhp snippets that should produce specific checker errors

### Implementation Details

**General checker test pattern:**
1. Parse the input source into an AST
2. Run the binder to produce scopes and symbols
3. Run the checker on the bound AST
4. Assert the expected diagnostics (or lack thereof)

**`TypeCompatibilityTests`:**
- `int` is assignable to `int` — no error
- `int` is NOT assignable to `string` — error
- `string` is assignable to `string|int` (union) — no error
- `string|int` is NOT assignable to `string` (narrowing required) — error
- `null` is assignable to `?string` (nullable) — no error
- `null` is NOT assignable to `string` (non-nullable) — error
- `ChildClass` is assignable to `ParentClass` — no error (subtype)
- `ParentClass` is NOT assignable to `ChildClass` — error
- `mixed` is assignable to anything — no error
- `never` is a subtype of everything
- `void` is only valid as return type — error if used as variable type
- Struct compatibility: two structs with identical property shapes are compatible
- Generic type compatibility: `Collection<User>` is assignable to `Collection<User>` but not to `Collection<Admin>`

**`NonNullableByDefaultTests`:**
- Assigning `null` to `string $x` produces `CheckerTypeMismatch` (4008)
- Assigning `null` to `?string $x` produces no error (nullable opt-in)
- Passing `null` argument to a `string` parameter produces `CheckerIncompatibleArgumentType` (4010)
- Passing `null` argument to a `?string` parameter produces no error
- Returning `null` from a function declared as `: string` produces `CheckerIncompatibleReturnType` (4009)
- Returning `null` from a function declared as `: ?string` produces no error
- Using a `?string` variable where `string` is expected (without null check) produces `CheckerVariablePossiblyNull` (4015)
- Using a `?string` variable after `!== null` check where `string` is expected produces no error (smart cast)
- Variable assigned in only one branch of `if/else`: using it after merge where non-null expected produces possibly-null error
- Function returning `?string` assigned to `string $x` produces error; assigned to `?string $x` produces no error
- Chained nullable: `?MyClass` property access without null check produces error
- Null coalescing: `$x ?? 'default'` on `?string $x` produces `string` type (non-null)

**`TypeNarrowingTests` (Smart Casts):**
- After `if ($x instanceof Foo)`, `$x` is automatically smart-cast to `Foo` inside the if-block — calling `$x->fooMethod()` is valid without explicit cast
- After `if ($x is Foo)`, same smart-cast behavior as `instanceof` (Tyhp `is` keyword alias)
- After `if (is_string($x))`, `$x` is automatically smart-cast to `string` — calling `\strlen($x)` is valid
- After `if ($x !== null)`, `$x` is automatically smart-cast from `?T` to `T` (non-null)
- In the else-branch of `if ($x instanceof Foo)`, `$x` is automatically narrowed to exclude `Foo` (negative smart cast)
- In the else-branch of `if ($x !== null)`, `$x` is narrowed to `null`
- After `if/else` branches merge, type widens to union of both branches (smart cast scope ends)
- Type narrowing resets after the narrowing block ends
- Type narrowing resets when the variable is reassigned inside the narrowed block
- Compound narrowing: `if ($x !== null && $x instanceof Foo)` narrows `$x` to `Foo` (not `?Foo`)
- Smart cast through `is` alias works identically to `instanceof`
- Nested narrowing: narrowing inside already-narrowed block produces intersection of both narrows

**`TypeGuardTests` (Smart Casts via User-Defined Guards):**
- A function declared as `function isUser($param): $param is User` must return `bool`
- Usage of a type guard function automatically smart-casts the argument in the if-block: `if (isUser($x))` makes `$x` type `User` in the true branch
- In the else-branch of a type guard check, the variable's type excludes the guard target type
- A type guard function that doesn't actually narrow should produce a warning (if checkable)
- Chained type guard: calling one guard then another further narrows the type

**`AsyncIterationTests`:**
- `foreach (await $asyncIterable as $item)` inside an async function with `AsyncIterable<string>` infers `$item` as `string` — no error
- `foreach (await $asyncIterable as $item)` outside an async function produces `CheckerAwaitOutsideAsync` (4028)
- `foreach ($asyncIterable as $item)` without `await` on `AsyncIterable<T>` produces `CheckerAsyncIterableMissingAwait` (4046)
- `foreach (await $notIterable as $item)` where `$notIterable` is not `AsyncIterable<T>` or `Promise<Iterable<T>>` produces `CheckerAwaitNonAsyncIterable` (4047)
- `foreach (await $promise as $item)` where `$promise` is `Promise<array<int>>` resolves to synchronous iteration with `$item` as `int` — no error
- `foreach (await $promise as $item)` where `$promise` is `Promise<AsyncIterable<string>>` resolves then async-iterates with `$item` as `string` — no error
- `foreach (await $asyncKvIterable as $key => $value)` infers correct key and value types from `AsyncKeyValueIterator<string, int>`
- Nested async foreach: two async foreach loops inside the same function both validate correctly

**`GenericConstraintTests`:**
- `T extends Serializable` — using `T` where `Serializable` is expected should be valid
- Passing a type that doesn't satisfy the constraint should produce an error
- Multiple constraints on a generic parameter
- Generic type inference from call sites

**`VisibilityCheckTests`:**
- Accessing `private` member from outside the class — error (`CheckerNotAllowedMemberModifier` or similar)
- Accessing `protected` member from a subclass — no error
- Accessing `protected` member from an unrelated class — error
- Accessing `public` member from anywhere — no error
- `CheckerMultipleVisibilities` (code 4002) — multiple visibility modifiers on one member
- `CheckerMemberModifierConflict` (code 4005) — `abstract` and `final` on same method
- `CheckerAccessorVisibilityCannotBeMoreVisibleThanProperty` (code 4004) — accessor more visible than property

**`ValidCodeNoErrorsTests`:**
- Parse and check all `.tyhp` files in `TestData/ValidTyhp/` — assert zero checker errors
- This requires the full pipeline (parser → binder → checker) to work end-to-end
- Use `[Theory]` with `[MemberData]` enumerating all valid Tyhp test data files via `TestFileManager.GetAllTestDataFiles("ValidTyhp", ".tyhp")`
- Mark with `[Trait("Category", "Checker")]` and `[Trait("Category", "EndToEnd")]`

**`ErrorCodeCoverageTests`:**
- For each `MessageCode` in the 4000s range (checker codes), create at least one test that triggers that specific error
- `CheckerUnknownError = 4001` — may be triggered by internal error (harder to test)
- `CheckerMultipleVisibilities = 4002` — class with `public private` method
- `CheckerNotAllowedMemberModifier = 4003` — `abstract` on a non-class member
- `CheckerAccessorVisibilityCannotBeMoreVisibleThanProperty = 4004` — public accessor on private property
- `CheckerMemberModifierConflict = 4005` — `abstract final` on same member
- `CheckerInvalidPropertyAccessorType = 4006` — invalid accessor type string
- `CheckerParameterNotAllowedOnPropertyAccessorType = 4007` — parameter on a get accessor

**`CheckerStateTests`:**
- Test `SnapShot()` creates an immutable copy
- Test `Split()` for different scope types — verify correct state propagation
- Test `Merge()` combines two branch states correctly:
  - Variables in both branches → keep with union type
  - Variables in only one branch → mark as possibly-undefined
  - Type narrowing → take wider type after merge

**`VariableStateTests`:**
- Test `IsDefinitelyAssigned` tracking through control flow
- Test `IsPossiblyNull` tracking after nullable assignments (non-nullable by default enforcement)
- Test `IsPossiblyNull` is `false` after a `!== null` check in the true branch (smart cast)
- Test `IsPossiblyNull` is `true` after merging branches where only one path null-checks
- Test `IsPossiblyUndefined` tracking after conditional assignments
- Test `IsDisposable` flag for `:=` assignments
- Test `NarrowedType` updates through type guards (smart cast)
- Test `NarrowedType` resets after variable reassignment
- Test `EffectiveType` returns narrowed type when narrowing is active, declared type otherwise

**`TypeInferenceTests`:**
- Variable with no type annotation but with integer literal initializer infers `int`
- Variable with no type annotation but with string literal initializer infers `string`
- Variable with no type annotation but with `new MyClass()` initializer infers `MyClass`
- Variable with no type annotation but with function call initializer infers function return type
- Variable with no type annotation but with ternary initializer infers union of both branches
- Variable with no type annotation AND no initializer produces `CheckerVariableTypeRequired` (4016)
- Inferred type is the narrowest possible (literal `42` infers `int`, not `int|float`)

**Story 08 dependency:**
- These tests exercise Story 08's `TyhpChecker`, `CheckerState`, and `VariableState`.
- When Story 08 is landed, implement these as active, running tests. If the checker is not yet in place, author the test bodies and gate them with `[Fact(Skip = "PLACEHOLDER_STORY_08: ...")]`, activating once it lands (per the incremental-authoring policy).

### Acceptance Criteria

- [ ] All checker tests pass (Story 08 is complete before Story 07)
- [ ] Every existing checker `MessageCode` (4001–4007) has at least one test that triggers it
- [ ] Type compatibility tests cover all major type categories: scalars, nullable, union, intersection, generics, structs
- [ ] Non-nullable by default tests verify that `null` is rejected for non-nullable types and accepted for nullable types
- [ ] Smart cast tests verify that type narrowing through `instanceof`/`is`, null checks, and type guard functions automatically updates the variable's effective type without explicit casts
- [ ] Type inference tests verify that first-assignment inference works without `var`/`auto` keywords
- [ ] Valid code tests verify all `TestData/ValidTyhp/` files produce no checker errors
- [ ] `dotnet test --filter "Category=Checker"` runs all checker tests

### Dependencies

- **Depends on:** Phase 1 (test helpers), Phase 4 (diagnostic assertions), Phase 5 (binder tests — confirms binding works), Story 08 (checker implementation)
- **Provides for:** Phase 7 (end-to-end tests assume checking works)

---

## Phase 7: Emitter Tests and End-to-End Snapshot Tests

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create tests for the emitter that verify correct PHP output for various Tyhp constructs. Use snapshot testing to compare emitted PHP against golden files. Include tests for each major Tyhp feature transformation (structs, generics, extensions, operator overloads, etc.) and end-to-end pipeline tests.

### Deliverables

- `tests/Tyhp.Tests/Emitter/PhpPassThroughTests.cs` — tests for basic PHP pass-through emission (no Tyhp features)
- `tests/Tyhp.Tests/Emitter/StructEmitTests.cs` — tests for struct → array emission
- `tests/Tyhp.Tests/Emitter/GenericEmitTests.cs` — tests for generic type erasure
- `tests/Tyhp.Tests/Emitter/ExtensionMethodEmitTests.cs` — tests for extension method → static call rewriting
- `tests/Tyhp.Tests/Emitter/OperatorOverloadEmitTests.cs` — tests for operator → method call rewriting
- `tests/Tyhp.Tests/Emitter/DisposableEmitTests.cs` — tests for disposable → DisposableScope emission
- `tests/Tyhp.Tests/Emitter/AsyncAwaitEmitTests.cs` — tests for async/await → Promise emission
- `tests/Tyhp.Tests/Emitter/CompileTimeConstructTests.cs` — tests for `nameof()`, `typeof()`, `default()` constant folding
- `tests/Tyhp.Tests/Emitter/TypeAliasEmitTests.cs` — tests for type alias erasure
- `tests/Tyhp.Tests/Emitter/WithKeywordEmitTests.cs` — tests for `with` keyword emission
- `tests/Tyhp.Tests/Emitter/ImportConversionTests.cs` — tests for import/use statement conversion
- `tests/Tyhp.Tests/EndToEnd/SnapshotTests.cs` — snapshot comparison tests using `TestData/ValidTyhp/emitter/*.tyhp` inputs with golden files in `Snapshots/Emitter/`
- `tests/Tyhp.Tests/EndToEnd/FullPipelineTests.cs` — full compilation pipeline tests
- `tests/Tyhp.Tests/EndToEnd/PhpOutputValidationTests.cs` — validate emitted PHP is syntactically valid
- `tests/Tyhp.Tests/Snapshots/Emitter/` — golden files for emitter snapshot tests
- `tests/Tyhp.Tests/TestData/ValidTyhp/emitter/` — small Tyhp inputs for emitter tests
- `tests/Tyhp.Tests/TestData/ExpectedPhpOutput/` — expected PHP outputs for emitter tests

### Implementation Details

**General emitter test pattern:**
1. Parse the Tyhp input into an AST
2. Run the binder
3. Run the checker (may be optional for pure emission tests)
4. Run the emitter to produce `PHPOutputFile` instances
5. Call `Generate()` on each output file to produce PHP strings
6. Compare the PHP output against expected content (inline or snapshot)

**`PhpPassThroughTests`:**
- A pure PHP file (wrapped in `<?tyhp` / `<?php`) with no Tyhp features should emit essentially identical PHP
- Test: namespace + class + method + property — emitted PHP has `<?php`, `declare(strict_types=1)`, correct namespace, class, method
- Test: function with parameters and return type — emitted PHP preserves type annotations PHP supports
- Test: if/else/for/foreach/while/switch — emitted PHP preserves control flow
- Test: string literals, array literals, constant references — emitted unchanged
- Test: `<?tyhp` is converted to `<?php` in output

**Feature-specific emitter tests (one file per feature):**
Each test file focuses on a single Tyhp feature transformation. Tests use inline Tyhp strings (small, 5-20 lines) and assert the emitted PHP matches expected output.

- **Struct → Array:** `new MyStruct()` → `['prop' => default, ...]`, `$s->prop` → `$s['prop']`, `with` on struct → array merge
- **Generic erasure and runtime tracking:** `Collection<User>` → `Collection` (type params stripped); generic class emits `use GenericObject;` trait, hidden `$__generic_*` constructor parameters (`?Type $__generic_T = null`), `NamedType` wrapping + `tyhpGenericObjectInit()` in constructor body, `tyhpGenericObjectSetPropertyType()` for all generic-typed properties; `typeof(T)` → `$this->tyhpGenericObjectGetGenericType('T')?->getUnderlyingType() ?? Type::mixed()`; call sites emit `__generic_T: Type::string()` named arguments. **Note:** `GenericObject` is only included for classes flagged with `RequiresRuntimeGenericTracking` (e.g., when `typeof(T)` is used; see Story 11, Phase 8). Test cases should cover both scenarios: generic classes WITH runtime tracking (that emit `use GenericObject;`) and generic classes WITHOUT runtime tracking (where generics are purely erased).
> **Cross-reference:** The `RequiresRuntimeGenericTracking` flag is a property on `ObjectDeclarationSymbol` (defined in Story 08). In test fixtures, set `testClassSymbol.RequiresRuntimeGenericTracking = true` on the test class's `ObjectDeclarationSymbol` before invoking the emitter. This flag is set when ANY generic type parameter is needed at runtime (e.g., `instanceof T`, `new T()`, `typeof(T)`, passing generic arguments to other generic constructors, or generic-typed properties requiring runtime enforcement). When set, the emitter adds the `\Tyhp\Concerns\GenericObject` trait and emits `tyhpGenericObjectInit()` calls in the constructor.
- **Extension methods:** `$str->toCamelCase()` → `StringExtensions::toCamelCase($str)`, including import for extension class
- **Operator overloads:** `$a + $b` (overloaded) → `$a->__add($b)`, compound `$a += $b` → `$a = $a->__add($b)`
- **Disposables:** `:=` assignment scope → `DisposableScope` auto-dispose via `__destruct()`
- **Async/await:** `async function` → function returning `Promise`, `await $x` → `_await($x)`
- **Async foreach:** `foreach (await $asyncIterable as $item)` → while-loop with `_await()` on `getAsyncIterator()->next()` and `current()`; `foreach (await $promise as $item)` where Promise resolves to array → `foreach (_await($promise) as $item)`; nested async foreach uses unique temp variable names; key-value async iteration uses `currentKey()`/`currentValue()`
- **Compile-time constructs:** `nameof($variable)` → `'variable'`, `default(int)` → `0`, `typeof(MyClass)` → `Type::of('MyClass')`, `typeof(T)` inside generic class → `$this->tyhpGenericObjectGetGenericType('T')?->getUnderlyingType() ?? Type::mixed()`
- **Type aliases:** Type alias declarations erased entirely, usages replaced with underlying types
- **With keyword:** `new Foo() with { bar: 1 }` → `$temp = new Foo(); $temp->bar = 1;`
- **Import conversion:** Tyhp `import` → PHP `use`, unused imports pruned

**`SnapshotTests` (end-to-end):**
- For each `.tyhp` file in `TestData/ValidTyhp/emitter/`, run the full pipeline and compare the emitted PHP output against a golden file in `Snapshots/Emitter/`
- Use the `SnapshotManager` to compare, with option to update via `UPDATE_SNAPSHOTS=true`
- Golden files are auto-created on first run if they do not exist
- Create a `[Theory]` test that enumerates all emitter test data files via `TestFileManager.GetAllTestDataFiles("ValidTyhp/emitter", ".tyhp")`

**`FullPipelineTests`:**
- End-to-end: create a minimal `tyhp.json` config in a temp directory, put `.tyhp` files in it, run the full compilation pipeline, verify output files exist at expected paths
- Test that the pipeline produces correct exit codes for valid and invalid inputs
- Test that diagnostics are correctly aggregated from all phases

**`PhpOutputValidationTests`:**
- If PHP is available on the test system, shell out to `php -l <emitted_file.php>` to validate syntax
- Use `[Fact(Skip = "PHP not available")]` if PHP is not installed (detect at test runtime)
- Alternatively, parse the emitted PHP using the project's own PHP parser to verify it's valid PHP

**Story 09/11 dependency:**
- These tests exercise Story 09 (Basic Emitter) and Story 11 (Emitter Feature Expansion).
- When Stories 09/11 are landed, implement these as active, running tests. For any feature whose emitter support is not yet in place, author the test body and gate it with `[Fact(Skip = "PLACEHOLDER_STORY_09: ..."]`/`PLACEHOLDER_STORY_11`, activating once it lands (per the incremental-authoring policy).

### Acceptance Criteria

- [ ] PHP pass-through tests pass (Story 09 is complete before Story 07)
- [ ] Feature-specific emitter tests pass (Story 11 is complete before Story 07)
- [ ] Snapshot tests generate/compare golden files for all emitter test data files
- [ ] End-to-end pipeline tests verify the full parse → bind → check → emit flow
- [ ] PHP syntax validation tests pass (emitted PHP is valid PHP)
- [ ] `dotnet test --filter "Category=Emitter"` and `dotnet test --filter "Category=EndToEnd"` run respective test suites

### Dependencies

- **Depends on:** Phase 1 (test helpers, snapshot manager), Phase 5 (binder tests), Phase 6 (checker tests), Story 09 (basic emitter), Story 11 (emitter features)
- **Provides for:** Phase 8 (integration tests assume pipeline works)

---

## Phase 8: Integration Tests

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create integration tests that exercise the full build pipeline including file discovery, configuration parsing, multi-file compilation, incremental compilation, and diagnostic reporting. These tests validate that the compiler's CLI actions work correctly end-to-end.

### Deliverables

- `tests/Tyhp.Tests/Integration/BuildPipelineTests.cs` — tests for the `BuildAction` pipeline
- `tests/Tyhp.Tests/Integration/IncrementalCompilationTests.cs` — tests for incremental rebuild behavior
- `tests/Tyhp.Tests/Integration/DiagnosticReportingTests.cs` — tests for error reporting accuracy (file, line, column, message)
- `tests/Tyhp.Tests/Integration/ConfigurationTests.cs` — tests for configuration option effects on output
- `tests/Tyhp.Tests/Integration/CompilationServiceTests.cs` — tests for the shared `CompilationService` (Story 01d)
- `tests/Tyhp.Tests/Integration/AstCacheIntegrationTests.cs` — tests for AST caching behavior in the pipeline
- `tests/Tyhp.Tests/TestData/IntegrationProjects/` — minimal project directories with `tyhp.json` and source files

### Implementation Details

**Integration test infrastructure:**
- Create a `TestProjectBuilder` helper in `TestHelpers/` that constructs temp directories with `tyhp.json` and `.tyhp` source files for testing
- Each integration test creates a fresh temp directory, populates it, runs the pipeline, then cleans up
- Use `IDisposable` pattern for temp directory cleanup

**`BuildPipelineTests`:**
- Create a minimal project (single `.tyhp` file with a simple class) — run build — verify output `.php` file exists at the correct path
- Create a project with multiple files in nested namespaces — verify all output files are produced with correct PSR-4 paths
- Create a project with errors — verify the pipeline returns `ExitCode.CompileError` and reports the correct errors
- Create a project with warnings only — verify the pipeline returns `ExitCode.CompileWarning` (or `Success` depending on strictness config)
- Test the `--dry-run` equivalent (if implemented): compilation runs but no files are written
- Test the `--clean` equivalent: output directory is wiped before build

**`IncrementalCompilationTests`:**
- Build a project → modify one file → rebuild — verify only the modified file is recompiled (leveraging AST cache)
- Build a project → add a new file → rebuild — verify the new file is compiled and existing cached files are reused
- Build a project → delete a file → rebuild — verify the deleted file's output is cleaned up (if applicable)

**`DiagnosticReportingTests`:**
- Compile a file with a known error at a specific line/column — verify the diagnostic reports the correct file name, line number, and column number
- Compile multiple files with errors in different files — verify diagnostics are associated with the correct files
- Verify diagnostic messages contain the expected format parameters (e.g., symbol names, type names)

**`ConfigurationTests`:**
- Test `output.path` — verify output files go to the configured directory
- Test `output.strictTypes` — verify `declare(strict_types=1)` is included/excluded based on config
- Test `output.comments` — verify comments are included/excluded based on config
- Test include/exclude glob patterns — verify only matching files are compiled
- Test `output.namespacePrefix` — verify namespace prefixing in output

**`CompilationServiceTests`:**
- Test that `CompilationService.ParseFiles()` correctly parses a list of files and returns `CompilationResult`
- Test multi-threaded parsing: provide a list of 10+ files and verify all are parsed
- Test cancellation: start parsing, cancel via `CancellationToken`, verify partial results or clean shutdown
- Test progress reporting: verify progress callback is called with correct file counts

**`AstCacheIntegrationTests`:**
- Parse a file → cache the AST → clear in-memory cache → retrieve from file cache → verify identical AST
- Parse a file → modify the file (different hash) → verify cache miss and re-parse
- Parse a file → don't modify → verify cache hit and no re-parse
- Test cache grouping (multiple files sharing cache group prefix)

**Test data projects:**
Create minimal project directories in `TestData/IntegrationProjects/`:
- `minimal_project/` — `tyhp.json` + single `src/Main.tyhp`
- `multi_file_project/` — `tyhp.json` + multiple `.tyhp` files in nested directories
- `error_project/` — `tyhp.json` + `.tyhp` file with intentional errors
- `config_variants/` — multiple `tyhp.json` files testing different config options

**Story 01/10 dependency:**
- These tests exercise Story 01's `CompilationService` and Story 10's `BuildAction` / pipeline infrastructure.
- When Stories 01/10 are landed, implement these as active, running tests. If the build pipeline is not yet in place, author the test bodies and gate them with `[Fact(Skip = "PLACEHOLDER_STORY_10: ...")]`, activating once it lands (per the incremental-authoring policy).

### Acceptance Criteria

- [ ] Build pipeline tests verify file output at correct paths for at least a minimal project
- [ ] Incremental compilation tests demonstrate cache-based optimization
- [ ] Diagnostic reporting tests verify line/column accuracy
- [ ] Configuration tests verify at least 3 different config options affect output
- [ ] AST cache integration tests verify cache hit/miss behavior
- [ ] All integration tests clean up temp directories after execution
- [ ] `dotnet test --filter "Category=Integration"` runs all integration tests

### Dependencies

- **Depends on:** Phase 1 (test helpers), Phase 4 (diagnostic tests), Story 01 (compilation service), Story 10 (build action)
- **Provides for:** Phase 9 (Tyhp runtime package tests may reference integration test patterns)

---

## Phase 9: Tyhp Runtime Package Tests (PHPUnit)

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create a PHPUnit test suite for the Tyhp runtime packages (`runtime/packages/`). These tests are separate from the .NET test project and validate the PHP runtime components that compiled Tyhp code depends on.

### Deliverables

- Verification and expansion of existing test files created by Story 04 (13 test files already exist under `runtime/packages/*/tests/`)
- `runtime/packages/lambda/tests/PropertyPathTest.php` — NEW tests for `PropertyPath` class (missing from Story 04)
- `runtime/packages/lambda/tests/ExpressionTest.php` — NEW tests for `Expression` class (missing from Story 04)
- `runtime/packages/lambda/tests/ExpressionSerializerTest.php` — NEW tests for `ExpressionSerializer` (missing from Story 04)
- Audit and expand test coverage for existing test files where gaps are identified
- Updated `runtime/README.md` — verify documentation on how to run PHP tests is complete

### Implementation Details

**PHPUnit setup — already exists:**
- `runtime/composer.json` and `runtime/phpunit.xml` already exist with `phpunit/phpunit ^10|^11` and test scripts configured
- Tests run via `cd runtime && composer install && composer test`
- Verify the existing `phpunit.xml` configuration covers all package test directories including `lambda`
- If `runtime/phpunit.xml` does not exist, create it pointing to `packages/*/tests/` directories
- **Layout note:** This `runtime/composer.json` + `runtime/phpunit.xml` root reflects the current (pre-Story-23) monorepo-style layout. **Story 21** migrates the runtime to standalone Composer packages (and `runtime/php-extensions/php8.2.9/` → `runtime/packages/php-8.2/`) without a monorepo root; revisit these paths/scripts when Story 21 lands. Until then, use the current layout.

**`DecimalTest.php` — Existing file — audit and expand coverage:**
- Test construction: `new decimal(42)`, `new decimal("3.14")`, `new decimal(0)`, `decimal::ZERO`
- Test arithmetic: `add`, `subtract`, `multiply`, `divide`, `modulo`, `power`, `negate`
  - `decimal(10)->__add(decimal(5))` should equal `decimal(15)`
  - `decimal(10)->__divide(decimal(3))` should equal `decimal(3.33...)` with correct scale
  - Division by zero should throw or return an error
- Test comparison: `equals`, `compareTo`, `lessThan`, `greaterThan`, `lessThanOrEqual`, `greaterThanOrEqual`
- Test conversion: `__toInt()`, `__toFloat()`, `__toString()`
- Test immutability: arithmetic operations return new instances, originals unchanged
- Test configurable scale and rounding mode
- Test with both BCMATH and GMP backends (if both are available)

**`TypeTest.php` — Existing file — audit and expand coverage:**
- Test `Type::of('string')` creates a string type descriptor
- Test `Type::of('int')` creates an int type descriptor
- Test `Type::generic('array', [Type::of('int'), Type::of('string')])` creates a generic type
- Test `Type::check($value, $type)` — verify runtime type checking
  - `Type::check("hello", Type::of('string'))` → `true`
  - `Type::check(42, Type::of('string'))` → `false`
- Test `Type::typeOf($value)` — verify type inference
- Test union types, intersection types, nullable types

**`GenericObjectTest.php` — Existing file — audit and expand coverage:**
- Create a test class that uses the `GenericObject` trait with hidden `$__generic_*` constructor parameters
- Test `tyhpGenericObjectInit()` with `NamedType`-wrapped generic types
- Test `tyhpGenericObjectGetGenericType('T')` returns the correct `NamedType`
- Test `tyhpGenericObjectGetGenericType('T')?->getUnderlyingType()` returns the unwrapped `Type`
- Test `tyhpGenericObjectSetPropertyType()` registers property types correctly
- Test that unresolved generic types (no `__generic_*` argument provided) return `null` from `tyhpGenericObjectGetGenericType()`
- Test the full pattern matching `PropertyAccessor.php`: construct with `__generic_TValue: Type::string()`, verify `typeof(TValue)` resolves to `Type::string()`

**`PropertyAccessorTest.php` — Existing file — audit and expand coverage:**
- Test that property accessors dispatch correctly to get/set methods
- Test readonly accessor (get-only, set throws)
- Test computed accessor (get returns computed value)
- Test validated accessor (set with validation logic)

**`DisposableTest.php` — Existing file — audit and expand coverage:**
- Create a test class implementing `IsDisposable` interface
- Test `using()` function calls `dispose()` in finally block
- Test `using()` with exception — verify `dispose()` still called
- Test multiple disposables — verify reverse disposal order
- Test nested disposable scopes

**`PromiseTest.php` — Existing file — audit and expand coverage:**
- Test basic promise resolution: `new Promise(fn($resolve) => $resolve(42))`
- Test promise chaining: `->then(fn($v) => $v + 1)`
- Test `_async()` and `_await()` usage
- Test `Promise::all()` — multiple promises resolved
- Test `Promise::race()` — first promise wins
- Test rejection handling
- Test `delay()` function

**`PropertyPathTest.php` (NEW):**
- Test `PropertyPath::of('user.address.city')` creates a path with correct segments
- Test `PropertyPath::resolve($obj)` extracts nested property values
- Test `PropertyPath::resolve()` with null intermediate values
- Test string representation of property paths

**`ExpressionTest.php` (NEW):**
- Test expression tree construction for common lambda patterns
- Test `ExpressionNode` concrete types (binary, unary, member access, constant, parameter)
- Test `ExpressionVisitor` traversal
- Test expression evaluation

**`ExpressionSerializerTest.php` (NEW):**
- Test serialization of expression trees to/from a storable format
- Test round-trip: serialize → deserialize → compare

**Integration with .NET test suite:**
- Add a test in the .NET test project (`tests/Tyhp.Tests/Integration/TyhpLibPhpTests.cs`) that shells out to `composer install && composer test` in the `runtime/` directory
- This test checks the exit code and reports PHP test failures as .NET test failures
- Mark this test with `[Trait("Category", "PHP")]` for easy filtering
- The test should be skipped if PHP or Composer is not available on the test machine

### Acceptance Criteria

- [x] PHPUnit test suite is runnable via `cd runtime && composer install && composer test`
- [x] Decimal tests cover arithmetic, comparison, conversion, and immutability
- [ ] Type tests cover basic type creation, generic types, and runtime checking
- [x] Promise tests cover resolution, chaining, `all()`, `race()`, and error handling
- [x] Disposable tests verify dispose is called in finally blocks
- [ ] The .NET integration test can invoke PHPUnit and report results
- [x] `runtime/README.md` documents how to run PHP tests

### Dependencies

- **Depends on:** Phase 1 (for the .NET integration test runner), Story 04 (TyhpLib implementation)
- **Provides for:** Confidence that emitted PHP using TyhpLib runtime works correctly

---

## Phase 10: CI/CD Pipeline and Test Automation (DEFERRED)

> **[Phase Runner] Runtime/Model:** DEFERRED — not implemented in this story
> **[Phase Runner] Review Level:** N/A

### Phase Overview

**This phase is deferred.** CI/CD pipeline setup will be handled as a separate effort outside the story execution order. The test infrastructure from Phases 1-9 is designed to be CI-compatible (categorized tests, conditional PHP skips, standard `dotnet test` execution) so CI integration can be added at any time.

The remaining documentation below describes the intended CI/CD setup for future reference.

### Deliverables

- `.github/workflows/tests.yml` — GitHub Actions workflow for running tests
- `tests/Tyhp.Tests/xunit.runner.json` — xUnit runner configuration (parallelism, timeouts)
- Updated `runtime/composer.json` — ensure PHPUnit scripts are configured
- Code coverage configuration in `Tyhp.Tests.csproj`
- Documentation in `tests/readme.md` — updated from "unit tests go here" to comprehensive testing guide

### Implementation Details

**GitHub Actions workflow (`tests.yml`):**
- Trigger on: push to any branch, pull request to `main`/`develop`
- Jobs:
  1. **dotnet-tests** — checkout, `dotnet restore`, `dotnet build`, `dotnet test` with coverage
  2. **php-tests** — checkout, install PHP + Composer, `composer install`, `vendor/bin/phpunit`
- Use matrix strategy for testing on multiple OS (ubuntu-latest at minimum, optionally windows-latest and macos-latest)
- Cache NuGet packages and Composer packages for faster builds
- Upload test results as artifacts (for PR review)
- Upload code coverage report (optionally to Codecov or similar)

**xUnit runner configuration:**
- Set `maxParallelThreads` appropriately (default or tune based on CI runner capacity)
- Set `diagnosticMessages` to `true` for debugging test failures in CI
- Configure test timeouts (e.g., 30 seconds per test for unit tests, 120 seconds for integration tests)

**Code coverage:**
- Use `coverlet.collector` (already added in Phase 1) with `--collect:"XPlat Code Coverage"` flag
- Generate coverage report in Cobertura format
- Add a `ReportGenerator` step to produce HTML coverage report (optional, for local development)
- Define coverage thresholds (aspirational — do not enforce initially, but report)

**Test categorization for selective execution:**
- `dotnet test --filter "Category=Parser"` — fast, run on every commit
- `dotnet test --filter "Category=Binder|Category=Checker"` — medium, run on every commit
- `dotnet test --filter "Category=Emitter|Category=EndToEnd"` — slower, run on every commit
- `dotnet test --filter "Category=Integration"` — slowest, run on PRs and main branch pushes
- `dotnet test --filter "Category=PHP"` — requires PHP, run separately or in dedicated job
- Workflow should run all categories but report failures per category

**Updated `tests/readme.md`:**
- Overview of the test architecture and directory structure
- How to run all tests: `dotnet test`
- How to run specific categories: `dotnet test --filter "Category=..."`
- How to run PHP tests: `cd runtime && composer install && composer test`
- How to update snapshots: `UPDATE_SNAPSHOTS=true dotnet test --filter "Category=EndToEnd"`
- How to view code coverage reports
- Test naming conventions
- How to add new tests

### Acceptance Criteria

- [ ] GitHub Actions workflow runs on push and PR events
- [ ] All .NET tests execute in CI and results are reported
- [ ] PHP tests execute in CI (separate job) if PHP is available
- [ ] Code coverage report is generated (even if coverage is initially low)
- [ ] Test categories can be filtered independently via `--filter`
- [ ] `tests/readme.md` provides clear documentation for running and writing tests
- [ ] CI workflow completes in a reasonable amount (focus on correctness, not speed)

### Dependencies

- **Depends on:** Phase 1–9 (all test phases should be in place)
- **Provides for:** Ongoing confidence in the codebase through automated testing

---

## Dependency and Placeholder Strategy

### Story 07 Gate and Incremental Authoring

Story 07 is the Tier 0 testing backbone — sequenced early but populated incrementally. As each corresponding story lands, its test categories provide:
- **Story 01:** Diagnostic system (`DiagnosticBag`, `IDiagnostic`, `CompilationResult`, `CompilationService`)
- **Story 02:** Binder (symbols, scopes, name resolution, `TyhpBinder`, `GlobalScope`)
- **Story 04:** Runtime packages (`tyhp/core`, `tyhp/decimal`, `tyhp/async`, `tyhp/lambda`)
- **Story 06:** built-in types, tyhpdef/`package.tyhp.json` registration, tagless conformance fixtures (`tests/conformance/story06/tagless/`)
- **Story 08:** Checker (`TyhpChecker`, `CheckerState`, `VariableState`)
- **Story 09 / 23:** Emitter and Optimizer
- **Story 10:** Build Action (full pipeline)
- **Story 11:** Emitter Feature Expansion
- **Story 17:** Sourcemap Generation
- **Story 20:** Tyhpdef Generator

**Per-phase dependency policy (incremental authoring):** The "all earlier stories complete" gate is the *target* state, but the testing infrastructure may be built up incrementally as prior stories land — and the codebase today has no test project yet and only partial prior stories. Each test phase depends only on the specific story/phase it exercises (e.g., binder tests need Story 02; emitter tests need Stories 09/11), NOT on all earlier stories at once. When the story/phase a test exercises is **already landed**, implement the test as an active, running test. When it is **not yet landed**, author the full test body and gate it with `[Fact(Skip = "PLACEHOLDER_STORY_N: <what's needed>")]` (or `[Trait]`-based filtering), then activate it once the dependency is in place. This lets Phase 1 (test project setup) plus the parser/diagnostics phases proceed immediately without blocking on later stories.

### For Future Story Dependencies (Stories 19+)

If a test references functionality from a story that comes AFTER Story 07 (e.g., Story 19 Language Server), use the placeholder convention:

```csharp
[Fact(Skip = "PLACEHOLDER_STORY_N: Brief description of what's needed")]
public void TestMethod_Scenario_ExpectedResult()
{
    // Full test implementation here — ready to run once dependency is met
    // Remove the Skip attribute when Story N is implemented
}
```

### For Phase Dependencies Within This Story

When a test references infrastructure from a later phase:

```csharp
// PLACEHOLDER_PHASE_N: This test will use SnapshotManager from Phase 1
// For now, use inline string comparison
```

### Removing Placeholders

When implementing a story or phase:
1. Search for `PLACEHOLDER_STORY_N` or `PLACEHOLDER_PHASE_N` across the test project
2. Remove `Skip` attributes or replace placeholder logic with real implementations
3. Run the affected tests to verify they pass
4. Commit the placeholder removal as part of the story/phase implementation
5. **Note:** Once a given prior story is fully landed, its tests should be active (no `Skip`). Until then, a `Skip`-gated test for that not-yet-landed dependency is acceptable per the incremental-authoring policy above.

---

## Summary of Phase Dependencies

```
Phase 1: Test Project Setup
    └── Phase 2: Parser Tests (TestData/Tyhpdef)
    │       └── Phase 3: Parser Edge Cases + AST Validation
    └── Phase 4: Diagnostics Tests
    │       └── Phase 5: Binder Tests
    │               └── Phase 5A: Conformance Harness Shell (Wave A capstone)
    │                       └── Phase 6: Checker Tests
    │                               └── Phase 7: Emitter + E2E Tests
    │                                       └── Phase 8: Integration Tests
    └── Phase 9: Tyhp Runtime Package Tests (independent of .NET test phases)
    └── Phase 10: CI/CD Pipeline (DEFERRED)
```

Phases 2, 4, and 9 can be developed in parallel after Phase 1 is complete. **Phase 5A completes Wave A** (required before Story 08). Phase 10 is deferred — CI/CD integration will be handled separately.

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify that the testing infrastructure is set up correctly and all test suites run. Steps can be skipped, reordered, or adapted as needed. The primary goal is to confirm the test project compiles, tests discover and execute, and the various test categories cover the intended functionality.

### Step 1: Verify the Test Project Compiles

Build the entire solution (main project + test project):

```bash
dotnet build tyhp.sln
```

Expected:
- Both `tyhp.csproj` and `tests/Tyhp.Tests/Tyhp.Tests.csproj` compile without errors
- No build warnings related to missing references or incompatible target frameworks
- The test project successfully references the main project's types (including internal types via `InternalsVisibleTo`)

### Step 2: Verify Test Discovery and the Smoke Test

Run the test suite to discover and execute all tests:

```bash
dotnet test tyhp.sln
```

Expected:
- Tests are discovered from the `Tyhp.Tests` project
- The smoke test (`SmokeTest.cs`) passes, confirming the test infrastructure can reference main project types like `Base2Ast`, `SrcFileAst`, `MessageCode`, and `ParserTestHelper`
- A summary line shows total tests, passed, failed, skipped

### Step 3: Verify Parser Tests Run

Run only parser-category tests:

```bash
dotnet test tyhp.sln --filter "Category=Parser"
```

Expected:
- All `.tyhp` test data files in `TestData/ValidTyhp/parser/` parse without errors (18+ files)
- All `.tyhpdef` test data files in `TestData/ValidTyhpdef/parser/` parse without errors (7+ files)
- All `.php` test data files in `TestData/ValidPhp/parser/` parse without errors
- All 16 bundled extension tyhpdef files (from `runtime/php-extensions/`) parse without errors
- Runtime package tyhpdef files (from `runtime/packages/*/`) parse without errors
- Edge case tests pass (empty files, deep nesting, long lines, Unicode)
- AST serialization round-trip tests pass
- Error recovery tests demonstrate partial AST production on invalid input
- Zero test failures

### Step 4: Verify Diagnostic System Tests Run

```bash
dotnet test tyhp.sln --filter "Category=Diagnostics"
```

Expected:
- `DiagnosticBag` tests pass: adding errors/warnings/info, filtering, counting, thread safety (4+ concurrent threads adding 100+ diagnostics each)
- `Diagnostic` factory method tests pass (Error, Warning, Info)
- `CompilationResult` tests pass: exit code determination (Success, CompileError, CompileWarning)
- `MessageCode` tests pass: no duplicate numeric values, numbering scheme is respected (1000s parser, 2000s visitor, 3000s binder, 4000s checker, 5000s emitter)
- Formatting tests verify correct output format with severity, code, and format parameters

### Step 5: Verify Binder Tests Run

```bash
dotnet test tyhp.sln --filter "Category=Binder"
```

Expected:
- Scope tree construction tests pass for at least 6 hierarchy patterns (namespace, class, function, nested blocks, anonymous functions, labels)
- Symbol registration tests verify properties, methods, constants, enum cases, extension methods
- Name resolution tests cover simple, qualified, relative, aliased, `self`/`static`/`parent`, and unresolved scenarios
- Duplicate declaration tests confirm `BinderDuplicateSymbolDeclaration` diagnostic is produced for same-name classes in the same namespace
- Namespace merging tests verify cross-file merging of namespace contents
- Tyhpdef loading tests verify symbols are registered in global scope from tyhpdef files
- Zero test failures

### Step 5A: Verify Conformance Harness (Wave A)

Run conformance-category tests:

```bash
dotnet test tyhp.sln --filter "Category=Conformance"
```

Expected:
- `ConformanceSuiteTests` discovers `tests/conformance/story06/tagless/manifest.json` automatically
- All five Story 06 tagless cases pass (four with `source.tagless: true`, one classic-mode case with `false`)
- `tagless_close_tag_error` asserts at least one error with code `1004` (`LexerCloseTagNotAllowedInTaglessMode`)
- `SelfHostRuntimeConformanceTests` is skipped with `PLACEHOLDER_STORY_10`
- No manual `dotnet run -- lint …` commands required for the wired suite

### Step 6: Verify Checker Tests Run

```bash
dotnet test tyhp.sln --filter "Category=Checker"
```

Expected:
- Type compatibility tests cover scalars, nullable, union, intersection, generics, structs
- Non-nullable by default tests verify: `null` rejected for non-nullable types, accepted for `?type`
- Smart cast / type narrowing tests verify: `instanceof`, `is`, null checks, type guard functions all narrow types automatically
- Type inference tests verify first-assignment type inference without explicit type annotations
- Every checker `MessageCode` (4001-4007 at minimum) has at least one triggering test
- `ValidCodeNoErrorsTests` confirms all `TestData/ValidTyhp/` files produce zero checker errors
- `CheckerState` snapshot/split/merge tests pass
- `VariableState` tracking tests pass (definite assignment, nullable, disposable, narrowed type)
- Zero test failures

### Step 7: Verify Emitter and End-to-End Tests Run

```bash
dotnet test tyhp.sln --filter "Category=Emitter"
dotnet test tyhp.sln --filter "Category=EndToEnd"
```

Expected:
- PHP pass-through tests: pure PHP wrapped in `<?tyhp` emits as essentially identical PHP
- Feature-specific emitter tests pass for each transformation:
  - Struct → array
  - Generic type erasure (with and without `GenericObject` trait)
  - Extension method → static call rewriting
  - Operator overload → method call rewriting
  - Disposable → `DisposableScope` emission
  - Async/await → Promise wrapping
  - Compile-time constructs (nameof, typeof, default) → constant values
  - Type alias erasure
  - `with` keyword → property assignments / `array_replace`
  - Import/use conversion and pruning
- Snapshot tests generate or compare golden files in `Snapshots/Emitter/`
- Full pipeline tests verify the complete parse → bind → check → emit flow
- If PHP is available: `PhpOutputValidationTests` run `php -l` on emitted files and all pass
- Zero test failures

### Step 8: Verify Snapshot Update Mechanism

To test the snapshot update workflow:

```bash
UPDATE_SNAPSHOTS=true dotnet test tyhp.sln --filter "Category=EndToEnd"
```

Expected:
- All snapshot golden files in `Snapshots/Emitter/` are created or updated
- Subsequent runs without `UPDATE_SNAPSHOTS` pass against the updated snapshots
- If you manually change a golden file and run without `UPDATE_SNAPSHOTS`, the test should fail with a diff

### Step 9: Verify Integration Tests Run

```bash
dotnet test tyhp.sln --filter "Category=Integration"
```

Expected:
- Build pipeline tests: a minimal project builds and produces output PHP files at correct paths
- Incremental compilation tests: modifying one file and rebuilding reuses cached ASTs for unchanged files
- Diagnostic reporting tests: errors are associated with the correct file, line, and column
- Configuration tests: at least 3 config options (`output.path`, `output.strictTypes`, `output.comments`) affect the output
- AST cache integration tests: cache hit/miss behavior works correctly
- All tests clean up temp directories after execution
- Zero test failures

### Step 10: Verify PHP Runtime Package Tests (Requires PHP + Composer)

If PHP and Composer are available:

```bash
cd runtime && composer install && composer test
```

Expected:
- PHPUnit discovers and runs tests from `runtime/packages/*/tests/`
- Decimal tests: arithmetic, comparison, conversion, immutability
- Type tests: type creation, generic types, `Type::check()` runtime checking
- GenericObject tests: `tyhpGenericObjectInit()`, `tyhpGenericObjectGetGenericType()`, property type registration
- PropertyAccessor tests: get/set dispatch, readonly, computed, validated
- Disposable tests: `dispose()` called in finally blocks, reverse disposal order
- Promise tests: resolution, chaining, `_async()/_await()`, `all()`, `race()`
- PropertyPath, Expression, ExpressionSerializer tests (if new in this story)
- Zero test failures

Also verify the .NET integration test that shells out to PHPUnit:

```bash
dotnet test tyhp.sln --filter "Category=PHP"
```

Expected: The test invokes `composer test` in the runtime directory and reports PHP test results as .NET test pass/fail. If PHP is not available, the test is skipped gracefully.

### Step 11: Verify Test Category Filtering Works

Each of these commands should run only the specified category:

```bash
dotnet test tyhp.sln --filter "Category=Parser"
dotnet test tyhp.sln --filter "Category=Binder"
dotnet test tyhp.sln --filter "Category=Checker"
dotnet test tyhp.sln --filter "Category=Emitter"
dotnet test tyhp.sln --filter "Category=EndToEnd"
dotnet test tyhp.sln --filter "Category=Integration"
dotnet test tyhp.sln --filter "Category=Diagnostics"
dotnet test tyhp.sln --filter "Category=Conformance"
```

Expected: Each command runs only tests tagged with the specified `[Trait("Category", "...")]`. No overlap or missed tests between categories.

### Step 12: Verify Test Data Files Are Complete

Check that the test data directories contain the expected files:

```bash
ls tests/Tyhp.Tests/TestData/ValidTyhp/parser/
```

Expected: At least 18 `.tyhp` files covering: class, interface, trait, enum, struct, function, generic, extension, operator overload, type alias, async, control flow, disposable, short function, property accessors, type guard, with keyword, compile-time constructs.

```bash
ls tests/Tyhp.Tests/TestData/ValidTyhpdef/parser/
```

Expected: At least 7 `.tyhpdef` files covering: class, interface, function, enum, namespace, generic types, union/intersection types.

```bash
ls tests/Tyhp.Tests/TestData/InvalidTyhp/
```

Expected: Files like `missing_semicolon.tyhp`, `unclosed_brace.tyhp`, `invalid_type.tyhp` for error recovery testing.

### Step 13: Verify Code Coverage (Optional)

Run tests with code coverage collection:

```bash
dotnet test tyhp.sln --collect:"XPlat Code Coverage"
```

Expected:
- A coverage report is generated (Cobertura XML format in `TestResults/`)
- Coverage data is collected for the main project assemblies
- Review the report to identify uncovered areas (this is informational, not a pass/fail gate)

### Step 14: Verify No Placeholder Skips Remain for Already-Landed Stories

Search the test project for any remaining placeholder skips that should have been resolved:

```bash
grep -rE "PLACEHOLDER_STORY_[0-9]+" tests/Tyhp.Tests/ || echo "No story placeholders found (good!)"
```

Expected: No skip attributes remain for stories that have already landed; only `PLACEHOLDER_STORY_NN` markers for not-yet-implemented future stories should remain as skipped tests.

### Step 15: Full Test Suite Pass

Run the complete test suite one final time to confirm everything passes together:

```bash
dotnet test tyhp.sln --verbosity normal
```

Expected:
- All tests across all categories pass
- Total test count matches the sum of individual category runs
- No test failures, no unexpected skips
- Test execution completes in a reasonable time (under 5 minutes for the full suite, excluding PHP tests)

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
