# Implementation Plan: Story 30 — Documentation & Polish

> **Roadmap position:** Story 30 — **Tier 3 — Advanced**
> **Direct dependencies (new numbering):** all earlier stories (01–29)
> **Renumbered from:** legacy Story 99
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

> **Story:** 99 — Documentation & Polish
> **Priority:** Low (ongoing, but not blocking)
> **Depends on:** Features being implemented (Stories 01–29)
> **Key files:** `README.md`, `docs/`, `CONTRIBUTING.md`, `Tyhp/Domain/Exceptions/MessageCode.cs`, `Resources/CLI.TyhpHostedService.en-US.resx`, `build.sh`, `release_build.sh`, `local_build.sh`, `Dockerfile.build`

---

## Project Context

The Tyhp compiler is a transpiler that converts Tyhp source (a strongly-typed superset of PHP) into valid PHP code. It is built in C# on .NET 9, uses ANTLR4 for parsing, and targets PHP 8.4. The project already has a working parser/AST/visitor pipeline, CLI infrastructure, AST caching, and extensive grammar coverage. The documentation site at `https://tyhplang.com/` is generated from 75+ JSON content files in `docs/content/` using a PHP-based generator (`docs/generate_docs.php`) that outputs HTML via a Bootstrap 5 template.

Story 30 covers **five sub-stories** spanning README authoring, JSON documentation content review, error message quality, CONTRIBUTING.md updates, performance profiling, and build/release pipeline hardening. This plan breaks these into actionable phases that can each be completed independently by an AI agent, with each phase leaving the project in a valid state. Note: the legacy `tyhp_guide.md` and `tyhpdef_guide.md` files are deprecated and will be removed — all documentation lives in the `docs/` folder.

---

## Architecture Overview

### Documentation Architecture

The project has three distinct documentation layers:

1. **Root-level Markdown files** — `README.md`, `CONTRIBUTING.md`, `AUTHORS.md`, `CODE_OF_CONDUCT.md`. These serve as entry points for GitHub visitors and contributors.

2. **JSON-based documentation site** — 75+ JSON files in `docs/content/` that are processed by `docs/generate_docs.php` into a Bootstrap 5 HTML site hosted at `https://tyhplang.com/`. The JSON schema supports multiple content block types: `text`, `header`, `note`, `tip`, `warning`, `danger`, `alert`, `bulletList`, `numList`, `tyhpCode`, `phpCode`, `jsonCode`, `classDef`, `functionDef`, `memberDescription`, `includeContent`, and file-based variants (`tyhpCodeFile`, `phpCodeFile`, `jsonCodeFile`). A table of contents (`toc.json`) organizes content into 10 sections: Introduction, Quick Reference, Alternative Projects, Tyhp, Compiler Diagnostics, Tyhpdef, Releases, CLI, Project Configuration, and FAQs.

3. **Localized resource strings** — `Resources/CLI.TyhpHostedService.en-US.resx` contains localized error/warning/info messages keyed by `ERROR_TYHP{code}`, `WARNING_TYHP{code}`, and `INFO_TYHP{code}`. The `Message` class in `Tyhp/CLI/Message.cs` uses `IStringLocalizer` to resolve these.

### Error Message Architecture

- `MessageCode.cs` defines an enum with codes organized by ranges: 1000s (parser), 2000s (visitor), 3000s (binder), 4000s (checker), 5000s (emitter), 6000s (configuration), 7000s (CLI), 8000s (tyhpdef), 9000s (internal).
- `Message.cs` provides `LocalizeErrorCode()`, `LocalizeWarningCode()`, `LocalizeInfoCode()` methods that construct keys like `ERROR_TYHP1001` and look them up via `IStringLocalizer`.
- The `.resx` file contains entries for error codes across multiple ranges, plus general localization strings (`"error"`, `"warning"`, `"info"`, `"Actions:"`, `"Property Accessor"`, `"Tyhp"`).
- `MessageCode.cs` already has comprehensive XML documentation, region markers for all ranges (including CLI sub-ranges 7000-7999), and populated entries for parser, visitor, binder, checker, emitter, and tyhpdef ranges. By the time this story executes (after Stories 01–29), both files will have additional entries added by those stories.

### Build & Release Architecture

- `local_build.sh` — Simple debug build via `dotnet publish -c Debug`.
- `build.sh` — Release build for `osx-arm64` (other platforms commented out). Uses `dotnet publish --sc -c Release`.
- `release_build.sh` — Full cross-platform release: compiles grammar, Docker multi-platform build for Linux (amd64 + arm64), native macOS builds (arm64 + x64), copies launcher scripts.
- `Dockerfile.build` — Docker-based build for Linux targets.
- `compile_grammar.sh` — ANTLR grammar compilation.
- `scripts/tyhp.sh` and `scripts/tyhp.bat` — Platform launcher scripts.
- `tyhp.csproj` — .NET 9, self-contained builds, ANTLR4, Goblinfactory.Konsole, Microsoft.CodeAnalysis, localization packages. The version is read dynamically from the `<Version>` element at execution time.

### Example Files

30 files in `Examples/` covering features: AsyncAwait, ClassTypes, FunctionAndMethodOverrides, FunctionAndMethodTypes, Generics, NewBuiltinTypes, OperatorOverloads, PropertyAccessors, ScalarTypesAsObjects, ShortFunctionSyntax, StaticValueTypes, Structs, TypeAliases, TypeGuards, TypedVars, WithKeyword. Many have both `.tyhp` and `.php` (expected output) pairs.

### Conventions & Patterns

- File naming: PascalCase for C# files, snake_case for CLI action names, camelCase for JSON content files with numeric ordering prefix (e.g., `tyhp_0500_generics.json`).
- JSON content files use non-standard JSON with `//` comments (parsed by a custom PHP parser in `generate_docs.php`).
- The project version (in `tyhp.csproj`) encodes PHP compatibility in the major version (e.g., `804` means PHP 8.4).
- Localization key format: `ERROR_TYHP{code}` / `WARNING_TYHP{code}` / `INFO_TYHP{code}`.

---

## Phase 1: Write the README.md

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Create a comprehensive `README.md` for the project root. The file is currently empty. This is the primary entry point for anyone discovering the project on GitHub and must convey what Tyhp is, how to get started, and where to find more information.

### Deliverables

- **Modified file:** `README.md` (currently empty, to be fully written)

### Implementation Details

The README must include these sections in order:

1. **Project Title and Tagline** — "Tyhp" with a one-line description: a strongly-typed superset of PHP that transpiles to readable PHP code. Reference the TypeScript-to-JavaScript analogy that the project uses throughout its docs (see `docs/content/intro.json`).

2. **Feature Highlights** — Brief bullet list of key Tyhp features with short code snippets (3-5 lines max each). Pull feature names from the `toc.json` Tyhp section and `Examples/` directory:
   - Strong typing (variables, parameters, return types)
   - Generics
   - Type guards and type narrowing
   - Structs (backed by associative arrays)
   - Operator overloads
   - Extension methods
   - Property accessors (get/set hooks)
   - Type aliases
   - Async/await (Fiber-based)
   - The `decimal` type
   - The `with` keyword
   - Disposable objects
   - Compile-time constructs (`nameof()`, `typeof()`, `default()`)

3. **Quick Start** — Minimal steps to install and run:
   - Prerequisites: .NET 9 SDK, PHP 8.4+
   - Clone, build (`dotnet publish`), init a project, build a `.tyhp` file
   - Reference the `build.sh` and `local_build.sh` scripts

4. **Installation** — Two methods:
   - Pre-built binaries (reference `out/` directory structure: `linux-amd64`, `linux-arm64`, `osx-arm64`, `osx-x64`, `win-amd64`, `win-arm64`)
   - Build from source (clone, `dotnet restore`, `dotnet publish`)
   - Using the dev container (reference `.devcontainer/devcontainer.json`)

5. **Project Configuration** — Brief mention of `tyhp.json` project file, link to full docs

6. **Documentation** — Link to the documentation site at `https://tyhplang.com/` and mention the `docs/` directory for contributing to documentation

7. **Examples** — Point to the `Examples/` directory, list a few key examples

8. **Contributing** — Brief paragraph linking to `CONTRIBUTING.md`

9. **Authors** — Link to `AUTHORS.md`

10. **License** — Reference the Apache License 2.0 (mentioned in `CONTRIBUTING.md` line 225)

11. **Status Badges** — Omit badges for now. Add a Markdown comment at the top of the file: `<!-- TODO: Add CI badges (build status, version, license) when GitHub Actions workflows are configured -->`

**Content rules:**
- Use the project's elevator pitch consistently: "Tyhp does for PHP what TypeScript does for JavaScript"
- Read the current version from `tyhp.csproj` (the `<Version>` element) rather than hardcoding a version number. Explain the versioning scheme briefly (the major version encodes PHP compatibility, e.g., `804` means PHP 8.4)
- Do NOT include full code implementations — only minimal snippets showing Tyhp syntax highlights
- Keep total length under 300 lines

### Acceptance Criteria

- `README.md` is non-empty and well-formatted Markdown
- All section headers are present and in logical order
- Quick start instructions reference correct build scripts (`local_build.sh` for debug, `build.sh` for release)
- Feature list matches actual implemented/planned features from the Examples directory and documentation content
- Version number matches the current value in `tyhp.csproj` and versioning scheme is explained
- Links to `CONTRIBUTING.md`, `AUTHORS.md` are correct relative paths
- No broken Markdown syntax
- File is under 300 lines

### Dependencies

- **Previous phases:** None (this is the first phase)
- **Provides for future phases:** Establishes the project narrative and terminology used in subsequent documentation phases

---

## Phase 2: Review and Update JSON Documentation Content (Part 1 — Introduction, Tyhpdef, CLI, Project, FAQ Sections)

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Review and update the JSON documentation content files in `docs/content/` for accuracy, completeness, and consistency. This phase covers the non-Tyhp-language sections: Introduction (5 files), Quick Reference (2 files), Tyhpdef (14 files), CLI (7 files), Project Configuration (2 files), FAQs (6 files), Release Planning (3 files), Alternative Projects (5 files), and Compiler Diagnostics (2 files). The Tyhp language section (33 files) is handled in Phase 3 due to its size.

### Deliverables

- **Modified files in `docs/content/`:** Up to 46 JSON content files reviewed and updated as needed
- No new files created unless a referenced but missing JSON file is discovered

### Implementation Details

**Review process for each file:**

1. Open and read the JSON file
2. Check for placeholder content (empty `"content"` arrays, `"TODO"` strings, `"TBD"` text)
3. Verify code examples use correct Tyhp/PHP/Tyhpdef syntax
4. Ensure descriptions are accurate relative to the current state of the codebase
5. Fix any JSON structure issues (missing required fields per the `item_template.json` schema)
6. Update any outdated information (version numbers, feature states, etc.)

**Files to review by section:**

*Introduction:*
- `intro.json` — Verify intro text matches current project state
- `intro_installation.json` — Verify installation steps match `build.sh`, `release_build.sh`, dev container setup
- `intro_gettingStarted.json` — Verify getting started steps
- `intro_upgradeGuides.json` — Check for placeholder content
- `intro_newSyntaxCreation.json` — Check for placeholder content

*Quick Reference:*
- `quickref.json` — Verify the condensed Tyhp syntax overview is accurate and complete
- `quickref_tyhpdef.json` — Verify the condensed Tyhpdef syntax overview is accurate and complete

*Alternative Projects:*
- `other_hack.json`, `other_pxp.json`, `other_reactphp.json`, `other_bref.json`, `other_typescript.json` — Verify descriptions are accurate and not empty

*Tyhpdef:*
- `tyhpdef_about.json` — Verify intro is accurate and complete
- `tyhpdef_openTag.json` — Verify `<?tyhpdef` open tag documentation
- `tyhpdef_interfaces.json` through `tyhpdef_runTimeErrorsAndExceptions.json` (12 files) — Verify syntax and examples are accurate

*CLI:* Because Story 30 is the final capstone (it runs after Stories 01–29 are complete), the `PLACEHOLDER_STORY_N` markers referenced below are expected to be **stale** by the time this phase executes — the underlying CLI actions will already exist. Treat each marker as "resolve and replace with real documentation" rather than "add a new placeholder." Only re-add a placeholder if a referenced story turns out to be genuinely incomplete.
- `cli_intro.json` — Verify CLI usage matches `Action.cs` enum values and descriptions
- `cli_build.json` — Document the build action (replace any leftover `PLACEHOLDER_STORY_10` with real content; Story 10 `BuildAction` is complete by this point)
- `cli_lint.json` — Document the lint action (replace any leftover `PLACEHOLDER_STORY_12`)
- `cli_languageServer.json` — Document the language server (replace any leftover `PLACEHOLDER_STORY_19`)
- `cli_xdebugProxy.json` — Document xdebug proxy (replace any leftover `PLACEHOLDER_STORY_18`)
- `cli_sourcemapGeneration.json` — Document sourcemap generation (replace any leftover `PLACEHOLDER_STORY_17`)
- `cli_tyhpdefGeneration.json` — Document tyhpdef generation (replace any leftover `PLACEHOLDER_STORY_20`)

*Project Configuration:*
- `project_intro.json` — Verify project file intro
- `project_optionsList.json` — Cross-reference with `Tyhp/Config/Project.cs` for currently supported options; mark unimplemented options clearly

*FAQs:*
- `faq_general.json`, `faq_tyhpSyntax.json`, `faq_tyhpdefSyntax.json`, `faq_cli.json`, `faq_project.json`, `faq_other.json` — Check for empty/placeholder content; fill in common questions where reasonable

*Release:*
- `release_planning.json` — Verify content is accurate and up to date
- `release_roadmap.json` — Update if outdated
- `release_prevList.json` — Check for placeholder content

*Compiler Diagnostics:*
- `diagnostics_overview.json` — Verify the diagnostic system overview is accurate; cross-reference with `MessageCode.cs`, `DiagnosticBag.cs`, and `Message.cs`
- `diagnostics_reference.json` — Verify the complete error code reference matches current `MessageCode.cs` enum values and `.resx` entries

**JSON format rules:**
- The JSON files use a custom format that allows `//` comments — preserve this convention
- Content block types must match one of the supported types from `item_template.json`
- String arrays in code blocks represent individual lines of source code
- The `"source"` field in `*CodeFile` types references files relative to the `docs/content/` directory

### Acceptance Criteria

- All 46 files have been read and reviewed
- No empty `"content"` arrays remain (either filled with content or marked with appropriate `PLACEHOLDER_STORY_N` comments in the JSON values)
- Code examples in tyhpdef section files are syntactically correct
- CLI section files accurately reflect the actions defined in `Action.cs`
- Project configuration options list matches `Project.cs` (noting which are implemented vs. planned)
- All JSON files parse correctly (valid JSON structure despite the comment convention)

### Dependencies

- **Previous phases:** Phase 1 (README establishes project narrative)
- **Provides for future phases:** Phase 3 (Tyhp language JSON files)

---

## Phase 3: Review and Update JSON Documentation Content (Part 2 — Tyhp Language Section)

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Review and update the 33 JSON content files in the Tyhp language section of `docs/content/`. These cover all major Tyhp language features and are the most technical and detailed documentation files. They must be cross-referenced against the example files (`Examples/`) and the grammar files (`Tyhp/TyhpLang/Grammar/`) for accuracy.

### Deliverables

- **Modified files in `docs/content/`:** Up to 33 JSON content files reviewed and updated

### Implementation Details

**Files to review (in toc.json order):**

1. `tyhp_0000_openTag.json` — `<?tyhp` and `<?tyhpdef` open tag syntax
2. `tyhp_0100_stronglyTyped.json` — Variable typing, required types everywhere
3. `tyhp_0150_newTypes.json` — `decimal` type and other new types
4. `tyhp_0151_newFunctions.json` — New built-in functions
5. `tyhp_0200_typeNarrowingAndGuards.json` — Type guards, narrowing, `is` keyword
6. `tyhp_0300_includingFiles.json` — `declare(output_file=,tag=)`, include/require changes
7. `tyhp_0350_useStatements.json` — Use statements, use extensions, generic import aliases
8. `tyhp_0400_structs.json` — Struct definition, anonymous structs, structural typing
9. `tyhp_0500_generics.json` — Class/function/method generics
10. `tyhp_0600_theMixedType.json` — `mixed` type usage and restrictions
11. `tyhp_0700_typeAliases.json` — `type` keyword, class-level aliases
12. `tyhp_0800_staticValueTypes.json` — Literal/static value types
13. `tyhp_1000_scalarPseudoObjects.json` — Scalar types as pseudo-objects
14. `tyhp_1100_shortFunctionSyntax.json` — `fn` syntax for functions/methods
15. `tyhp_1200_functionOverloads.json` — Function/method overloads
16. `tyhp_1300_newObjectDeclSyntax.json` — New/changed class/interface/enum/trait syntax
17. `tyhp_1600_operatorOverloads.json` — Operator overload declarations
18. `tyhp_2000_traitRequirements.json` — `extends`/`implements` on traits
19. `tyhp_2100_extensions.json` — Extension methods/classes
20. `tyhp_2200_withKeyword.json` — `with` keyword on new and clone
21. `tyhp_2300_disposables.json` — `:=` operator, `IsDisposable`, using blocks
22. `tyhp_2400_dynamicLanguageFeatures.json` — Dynamic vars, class names, includes, `eval` removal
23. `tyhp_2500_phpMagicMethods.json` — Magic methods and type safety
24. `tyhp_2600_asyncAndAwait.json` — Fiber-based async/await
25. `tyhp_2700_compileTimeConstructs.json` — `nameof()`, `default()`, `typeof()`
26. `tyhp_2800_importPHPVar.json` — Import raw PHP variables
27. `tyhp_2900_lostFunctionality.json` — Removed/changed PHP functionality
28. `tyhp_3000_parsableLambdas.json` — Parsable lambdas and expression trees
29. `tyhp_3100_internalModifier.json` — The `internal` visibility modifier
30. `tyhp_3200_initPropertyModifier.json` — The `init` property modifier
31. `tyhp_3300_nullConditionalAssignment.json` — Null-conditional assignment (`$obj?->prop = val`)
32. `tyhp_3400_newTypeConstraint.json` — `new<TArgs...>` constructable object type
33. `tyhp_3500_genericDefaults.json` — Generic type parameter defaults

**Note:** The documentation JSON files for features introduced in Stories 25-23 (e.g., `tyhp_3100_internalModifier.json`, `tyhp_3200_initPropertyModifier.json`, `tyhp_3300_nullConditionalAssignment.json`, `tyhp_3400_newTypeConstraint.json`, `tyhp_3500_genericDefaults.json`) are **reviewed, updated, and completed** during this phase rather than authored from scratch by their respective feature stories. Several of these files already exist in `docs/content/` (and are listed in `toc.json`), so this phase must treat them as existing stubs/drafts to verify and finish — do NOT recreate or duplicate them. For any such feature whose file is genuinely missing, create it using the existing documentation files as templates for structure and formatting conventions. Each file should cover the feature's syntax, semantics, examples, and any relevant configuration options.

**Review process for each file:**
- Read the file contents
- Cross-reference code examples against corresponding `Examples/*.tyhp` and `Examples/*.php` files
- Verify feature descriptions are accurate and match current implementation
- Check for empty/placeholder content blocks
- Fill in missing content where the language feature is well-defined
- For features that depend on unimplemented compiler components, add a note in the content indicating the feature is planned: use `PLACEHOLDER_STORY_N` format in relevant content blocks
- Ensure JSON structure follows the `item_template.json` schema

**Content accuracy checks:**
- Verify `tyhpCode` blocks use valid syntax from the Tyhp grammar
- Verify `phpCode` blocks show correct expected PHP output
- Cross-reference `classDef`, `functionDef`, and `memberDescription` blocks against the schema in `item_template.json`
- Ensure `includeContent` references point to existing files

### Acceptance Criteria

- All 33 Tyhp language JSON files have been reviewed
- No empty `"content"` arrays remain without justification
- Code examples are syntactically consistent with the grammar and example files
- All `classDef` blocks have all required fields (type, identifier, members)
- All `functionDef` blocks have all required fields (identifier, parameters, returnType)
- Features that depend on unimplemented stories use `PLACEHOLDER_STORY_N` notation
- All referenced `includeContent` source files exist

### Dependencies

- **Previous phases:** Phase 2 (establishes review methodology)
- **Provides for future phases:** Phase 4 (error messages may reference documented features)

---

## Phase 4: Error Message Quality Pass

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Ensure every `MessageCode` enum value has a corresponding localized message string in the `.resx` file, that messages are clear and actionable, and that the code numbering scheme is properly documented. This phase also adds missing message entries for ranges that are currently sparse.

### Deliverables

- **Modified file:** `Tyhp/Domain/Exceptions/MessageCode.cs` — Review and update documentation comments, verify region markers and code range coverage
- **Modified file:** `Resources/CLI.TyhpHostedService.en-US.resx` — Add any missing message entries, improve existing messages for clarity
- **Modified file:** `Resources/CLI.TyhpHostedService.resx` — Keep in sync with the `.en-US.resx` file (both must have the same entries)
- **Modified file:** `Tyhp/CLI/Message.cs` — Minor improvements if needed for message rendering

### Implementation Details

**Step 1: Review and update MessageCode documentation**

`MessageCode.cs` already has an XML documentation comment block documenting the code ranges (1000-9999), the CLI sub-ranges (7000-7999), and instructions for adding new codes. Review this existing documentation for accuracy and completeness:

- Verify all documented ranges match the actual enum values present in the file
- Verify the CLI sub-range table (7000-7999) is still accurate
- Update any stale information (e.g., if a "reserved" range now has entries, update the description)
- Ensure the "Adding New Codes" instructions are clear and complete

**Step 2: Dynamically audit MessageCode values against .resx entries**

Rather than relying on a hardcoded list, perform a live audit:

1. Read all enum values from `MessageCode.cs` (excluding `NoError = 0`)
2. Read all `<data name="ERROR_TYHP####">` entries from `Resources/CLI.TyhpHostedService.en-US.resx`
3. For each `MessageCode` enum value, check whether a corresponding `ERROR_TYHP{numeric_value}` entry exists in the `.resx` file
4. Produce a list of any enum values that are missing `.resx` entries

**Important:** By the time this phase executes, Stories 01–29 will have added many `MessageCode` values and `.resx` entries beyond the original set. Do NOT use a hardcoded audit table — always read the current state of both files dynamically.

**Step 3: Add any missing .resx entries**

For each `MessageCode` enum value identified as missing a `.resx` entry in Step 2:

1. Determine an appropriate error message based on the enum name and any XML doc comments on the enum value
2. Include `{0}`, `{1}`, etc. format parameter placeholders where appropriate (look at how the code is used in the codebase by searching for the enum value name to understand what format args are passed)
3. Add the entry to **both** `Resources/CLI.TyhpHostedService.en-US.resx` and `Resources/CLI.TyhpHostedService.resx` (both files must stay in sync)
4. Add a descriptive `<comment>` element explaining when the message is used

Follow the existing entry format and naming convention: `ERROR_TYHP{code}` for errors, `WARNING_TYHP{code}` for warnings, `INFO_TYHP{code}` for info messages.

**Step 4: Review existing message quality**

For each existing `.resx` entry, verify:
- The message includes `{0}`, `{1}`, etc. placeholders that match the format parameters passed at the call site (search the codebase for usages of each `MessageCode` to verify)
- The message is clear about what went wrong
- The message suggests what to do (where possible)
- The message uses consistent terminology across similar error types
- If a message improvement requires adding or changing placeholders, also update the corresponding call sites that emit that diagnostic to pass the correct format arguments

**Step 5: Verify region markers in MessageCode.cs**

`MessageCode.cs` already has `#region` markers for all code ranges (including CLI sub-ranges). Verify that:

- Every populated range has a corresponding `#region` / `#endregion` pair
- Region names are descriptive and include the numeric range
- Any empty reserved regions have a comment indicating which story will populate them (or that they have been populated if the story has already executed)
- No enum values exist outside their designated region

**Step 6: Verify Message.cs rendering**

- Confirm that `Message.TyhpError()`, `Message.TyhpWarn()`, and `Message.TyhpInfo()` methods that take file/line/column/code parameters all correctly format the output
- Verify the format string pattern: `fileName(lineNumber,column): severity TYHPcode: message`
- Check that when `IStringLocalizer` is not set (null), the raw key string falls through gracefully

### Acceptance Criteria

- Every `MessageCode` enum value has a corresponding `.resx` entry (except `NoError`) in both `.resx` files
- The existing XML documentation on `MessageCode.cs` is accurate and up to date
- All `#region` markers are present and correctly labeled for their code ranges
- All `.resx` messages include appropriate format parameter placeholders that match their call sites
- Messages are clear and actionable
- Both `.resx` files (`CLI.TyhpHostedService.resx` and `CLI.TyhpHostedService.en-US.resx`) are in sync
- No compilation errors after changes
- `Message.LocalizeErrorCode()` returns the expected string for all existing codes when the localizer is configured

### Dependencies

- **Previous phases:** None (can be done in parallel with other phases)
- **Provides for future phases:** Phase 5 (CONTRIBUTING.md) and Phase 6 (build pipeline) may need working error messages for testing

---

## Phase 5: Update CONTRIBUTING.md

> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

The `CONTRIBUTING.md` file was adapted from TypeScript's contributing guide and contains several `// TODO` placeholders and references to TypeScript-specific tooling (`gulp`, `.ts` files, `DiagnosticMessages.php`). This phase updates it to be accurate for the Tyhp project.

### Deliverables

- **Modified file:** `CONTRIBUTING.md` — All `// TODO` and `TBD` sections completed, TypeScript-specific references removed

### Implementation Details

**Sections needing updates:**

1. **"What You'll Need" section** (~line 52-58) — Currently correct (PHP 8.4+, .NET 9 SDK, VS Code, Composer). Verify all items are still accurate.

2. **"Get Started" section** (~line 60-70) — Steps reference `dotnet restore` and `dotnet test`. Verify `dotnet test` works (note: `tests/` directory has no actual test files per `TODO.md`). Update step 6 to note that the test infrastructure is under development.

3. **"Using local builds" section** (~line 87) — Currently references `<repo-root>/build/<your-cpu-arch>/tyhp`. Update to reference the actual `local_build.sh` script and the `build/` output directory.

4. **"Modifying generated library files" section** (~line 116-120) — Entirely `**TBD**` / `// TODO`. This references TypeScript's `.d.tyhp` and `.d.hack` files. Replace with information about:
   - ANTLR grammar files in `Tyhp/TyhpLang/Grammar/` and how to regenerate the parser (`compile_grammar.sh`)
   - Generated parser files in `Tyhp/TyhpLang/Parser/`
   - Tyhpdef generated files in `tools/tyhpdef_gen/`

5. **"Running the Tests" section** (~line 122-129) — Fix the typo `dontnet test` → `dotnet test`. Note that test infrastructure is planned (Story 07) but not yet implemented.

6. **"Debugging the tests" section** (~line 131-139) — Entirely `**TBD**` / `// TODO`. Remove TypeScript-specific `gulp runtests` references. Replace with .NET debugging instructions (VS Code launch configuration, `dotnet test --filter`).

7. **"Adding a Test" section** (~line 143-155) — Entirely `**TBD**` / `// TODO`. Remove TypeScript-specific `.ts` file references. Replace with guidance for the Tyhp test structure once Story 07 is implemented. For now, describe the planned test approach.

8. **"Tests for multiple files" section** (~line 159-175) — Remove TypeScript-specific content. Replace with relevant multi-file testing guidance for Tyhp (testing namespace merging, multi-file binder, etc.).

9. **"Managing the baselines" section** (~line 177-205) — Remove TypeScript-specific `gulp` and baseline references. Replace with Tyhp-specific test baseline approach (e.g., snapshot testing of emitter output).

10. **"Localization" section** (~line 209-213) — Remove reference to `DiagnosticMessages.php` (does not exist). Replace with accurate localization information: `.resx` files in `Resources/`, `IStringLocalizer`, `MessageCode.cs`, and the key format `ERROR_TYHP{code}`.

11. **General cleanup:**
    - Remove `// TODO` markers
    - Remove `**TBD**` markers
    - Use `https://github.com/tyhpproject/tyhp` for all GitHub URLs — this is the correct repository (currently private, will be made public at release). Do not change the org/repo name.
    - Ensure all file paths referenced actually exist in the repository

### Acceptance Criteria

- No `// TODO` or `**TBD**` markers remain
- No TypeScript-specific references remain (`gulp`, `.ts` files, `DiagnosticMessages.php`)
- The typo `dontnet` is fixed to `dotnet`
- All file paths referenced in the document exist in the repository
- Build instructions match actual build scripts
- Localization section correctly describes `.resx` / `IStringLocalizer` approach
- The file renders correctly as GitHub-flavored Markdown

### Dependencies

- **Previous phases:** Phase 1 (README may link to CONTRIBUTING.md)
- **Provides for future phases:** Phase 6 (performance and build pipeline work)

---

## Phase 6: Performance Profiling Infrastructure and Build/Release Pipeline

> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Establish performance profiling capabilities for the compiler pipeline and harden the build/release scripts. This phase creates profiling utilities that can be used to benchmark parsing, binding, checking, and emitting performance, and ensures the build scripts work correctly for all target platforms.

> **Scope note:** Although Story 30 is primarily a documentation & polish story, this phase intentionally adds real build/profiling code (`Tyhp/Helpers/PerformanceProfiler.cs`, a `--profile` flag, build-script hardening). Treat it as **build/profiling polish** — the "tooling" half of "documentation & polish" — not as a feature story. It is grouped here because it is final-stage hardening that depends on the rest of the compiler (Stories 01–29) already being in place.

### Deliverables

- **Modified file:** `build.sh` — Uncomment and enable all target platform builds, or document which are supported
- **Modified file:** `release_build.sh` — Verify and update cross-platform build process
- **Modified file:** `local_build.sh` — Ensure it works for the current .NET 9 target
- **Modified file:** `Dockerfile.build` — Verify it builds correctly on current .NET 9
- **New file:** `Tyhp/Helpers/PerformanceProfiler.cs` — A lightweight profiling utility for measuring compilation phase performance
- **Modified file:** `Tyhp/CLI/DebugAction.cs` — Integrate the new profiling utility alongside the existing ANTLR parser decision profiling (do not replace the ANTLR profiling)

### Implementation Details

**Part A: Performance Profiling Utility**

Create `Tyhp/Helpers/PerformanceProfiler.cs`:

- An **instance-based** utility that tracks named phases with start/stop timing. Use instance-based design (not static) to support multiple concurrent profiling sessions, cleaner testability via dependency injection, and to avoid shared mutable static state in a multi-threaded compiler.
- Methods: `StartPhase(string name)`, `EndPhase(string name)`, `GetPhaseDuration(string name)`, `GetAllPhases()`, `GetSummary()`
- Track memory usage per phase: capture `GC.GetTotalMemory()` at phase boundaries
- Support nested phases (e.g., "Parse" > "Parse:FileA.tyhp") using a stack-based approach — `StartPhase` pushes onto an internal stack, `EndPhase` pops and records the duration. Nested phase names should be stored as their full path (e.g., "Parse:FileA.tyhp").
- Thread-safe for use in multi-threaded parsing — use `ConcurrentDictionary` for phase storage and thread-safe timing (each thread can have independent phase stacks via `ThreadLocal` or `AsyncLocal`)
- Output format: phase name, duration, memory delta, percentage of total
- Support CSV output as well as console table output
- The existing `DebugAction` uses ANTLR parser decision profiling (`parser.Profile`) which is a different kind of profiling (ANTLR decision statistics). The new `PerformanceProfiler` provides complementary phase-level timing, not a replacement for the ANTLR profiling.

**Part B: Build Script Hardening**

Review and update `build.sh`:
- Currently only builds `osx-arm64` (all other targets are commented out)
- Uncomment and enable all supported target platforms: at minimum `linux-x64`, `osx-arm64`, `osx-x64`
- Add error checking (exit on build failure)
- Build all supported platforms by default. Add an optional `--platform <rid>` flag (e.g., `--platform osx-arm64`) to build only a specific runtime identifier when needed for faster local iteration

Review and update `release_build.sh`:
- Currently runs `compile_grammar.sh`, Docker multi-platform build, and native macOS builds
- Verify the Docker build context is correct for the current project structure
- Verify the `Dockerfile.build` builds on current .NET 9
- Ensure the output directory structure matches what `README.md` (Phase 1) documents
- Add Windows targets (`win-x64`, `win-arm64`) to the release build using `dotnet publish` cross-compilation (the .NET SDK supports cross-target publishing natively for Windows RIDs without requiring Docker or a Windows host). This is consistent with how the macOS targets are already built.

Review `Dockerfile.build`:
- Verify the base image supports .NET 9
- Verify the build commands match current `tyhp.csproj` configuration
- Ensure the output artifacts are correctly extracted

Review `local_build.sh`:
- Currently just `dotnet publish -c Debug -o build tyhp.csproj`
- This is correct and minimal — ensure it still works
- Consider adding a `--configuration` parameter for Debug vs Release

Review `compile_grammar.sh`:
- Verify `antlr-ng` invocation is correct (requires `npm install -g antlr-ng`)
- Verify generated parser files end up in `Tyhp/TyhpLang/Parser/` and `Tyhp/TyhpLang/Tyhp/TyhpLang/Grammar/*.tokens` / `Tyhp/TyhpLang/Tyhp/TyhpLang/Grammar/*.interp` are updated
- Verify grammar regeneration is documented as `./compile_grammar.sh` followed by `dotnet clean && dotnet restore && dotnet build` (not `dotnet build` alone)

Review launcher scripts:
- `scripts/tyhp.sh` — Verify it finds and runs the correct binary
- `scripts/tyhp.bat` — Verify Windows launcher

**Part C: Integrate Profiling into Debug Action**

The `DebugAction` already has ANTLR parser decision profiling (`parser.Profile = true`) which reports decision-level statistics (prediction time, invocations, SLL/LL lookahead, ambiguities). This existing profiling must be preserved as-is. Add the new `PerformanceProfiler` **alongside** it to provide complementary phase-level timing:

- Create a `PerformanceProfiler` instance in `DebugAction` and use `StartPhase()` / `EndPhase()` calls to track high-level compilation phases: file discovery, parsing, visitor/AST generation, AST caching, binding
- When profiling output is enabled, display both the existing ANTLR decision profile **and** the new phase-level timing summary
- Add a `--profile` flag to `BuildAction` to enable the new phase-level profiling output (follow the existing CLI argument pattern established in `BuildAction` by prior stories)
- Support outputting the phase-level profile as CSV (for consistency with any existing profiling output patterns)

### Acceptance Criteria

- `PerformanceProfiler.cs` compiles without errors
- `PerformanceProfiler` can track named phases with timing and memory
- `build.sh` builds at least one platform successfully
- `local_build.sh` produces a working binary in `build/`
- `release_build.sh` has no obviously broken commands (may not be testable without Docker and macOS)
- All build scripts have proper error handling (non-zero exit on failure)
- Launcher scripts (`tyhp.sh`, `tyhp.bat`) reference correct binary paths
- The `DebugAction` still works after profiling integration (no regressions)

### Dependencies

- **Previous phases:** Phase 4 (error messages should be in place for any build-time errors)
- **Provides for future phases:** None (this is the final phase)

---

## Placeholder Management Strategy

Throughout this implementation plan, placeholders are used to mark content that depends on features from other stories:

- `PLACEHOLDER_STORY_01` — Diagnostic system, CompilationService
- `PLACEHOLDER_STORY_10` — Build action, configuration expansion
- `PLACEHOLDER_STORY_12` — Lint action
- `PLACEHOLDER_STORY_17` — Sourcemap generation
- `PLACEHOLDER_STORY_20` — Tyhpdef generator CLI
- `PLACEHOLDER_STORY_07` — Testing infrastructure
- `PLACEHOLDER_STORY_19` — Language server
- `PLACEHOLDER_STORY_13` — CLI polish (help, init, version)
- `PLACEHOLDER_STORY_18` — XDebug proxy

In documentation files (Markdown and JSON), use the format:
```
<!-- PLACEHOLDER_STORY_N: Brief description of what will go here -->
```

In C# code, use the format:
```csharp
// PLACEHOLDER_STORY_N: Brief description of what will go here
```

When the referenced story is implemented, search for its placeholder tag and replace with actual content.

Because Story 30 is the **final capstone** (it executes only after Stories 01–29 are complete per the ROADMAP order), every `PLACEHOLDER_STORY_NN` is expected to be stale by the time this story runs. The net effect for Story 30 is therefore the *removal/resolution* of these placeholders — replacing them with finished documentation — not the creation of new ones. The renumbered ROADMAP is contiguous (`01`–`30`) with no gaps: the legacy Stories 5/15/18 (superseded, renumbered, and cancelled respectively) no longer exist as separate numbers.

---

## Cross-Phase Consistency Checklist

After completing all phases, verify these cross-cutting concerns:

1. **Version consistency** — The version from `tyhp.csproj` is used consistently in README, docs, and any version references (read it dynamically, do not hardcode)
2. **Feature list consistency** — Features mentioned in README match those documented in JSON content files
3. **Tyhpdef consistency** — Tyhpdef JSON documentation files are accurate and internally consistent
4. **CLI action consistency** — CLI actions documented in JSON files match `Action.cs` enum values and descriptions
5. **Error code consistency** — `MessageCode.cs` enum values, `.resx` entries, and any documented error codes are in sync
6. **Link integrity** — All relative links in Markdown files point to files that exist
7. **Example file references** — Any referenced example files in `Examples/` actually exist
8. **Build script consistency** — Build instructions in README and CONTRIBUTING match actual build scripts

---

## Human Testing and Verification

> These steps are for a human developer to manually verify the implementation works end-to-end. Steps can be skipped or modified as needed — they are guidelines, not a rigid checklist.

### Step 1: Verify README.md Quality

Open `README.md` in a browser (e.g., via `grip README.md` or the GitHub preview).

Check for:
- [ ] Project title and tagline are present
- [ ] Feature highlights section lists key Tyhp features with short code snippets
- [ ] Quick start instructions are present and accurate
- [ ] Installation section mentions pre-built binaries and build from source
- [ ] Link to documentation site (`https://tyhplang.com/`) is present
- [ ] Link to `CONTRIBUTING.md` works (relative link)
- [ ] Link to `AUTHORS.md` works (relative link)
- [ ] Apache License 2.0 is referenced
- [ ] Version number matches the `<Version>` element in `tyhp.csproj`
- [ ] File is under 300 lines

### Step 2: Verify Documentation Site Generation

Run the documentation site generator:

```bash
cd docs
php generate_docs.php
```

**Expected:** The generator completes without errors. Open the generated HTML in a browser and verify:
- Navigation sidebar lists all 10 sections from `toc.json`
- Clicking through each section loads content without broken pages
- Code examples render with syntax highlighting
- No visible `PLACEHOLDER_STORY_N` text leaks into the rendered HTML (these should be in JSON comments, not rendered content)

### Step 3: Verify Error Message Coverage

Run a quick audit to check that every `MessageCode` enum value has a corresponding `.resx` entry. Use this command pattern:

```bash
# Extract all numeric values from MessageCode.cs enum
# Then check each against the .resx file
# (adjust paths as needed for your environment)
```

Alternatively, build the project and run a test that calls `Message.LocalizeErrorCode()` for each known code:

```bash
dotnet clean && dotnet restore && dotnet build
```

**Expected:** The build succeeds without warnings about missing resource strings. If the project has a test that validates message coverage, run it.

### Step 4: Verify Error Message Quality (Spot Check)

Pick 5-10 `MessageCode` values across different ranges and verify their `.resx` messages:

1. Open `Tyhp/Domain/Exceptions/MessageCode.cs` and pick codes from different ranges (parser 1000s, binder 3000s, checker 4000s, emitter 5000s)
2. Look up each code's message in `Resources/CLI.TyhpHostedService.en-US.resx`
3. Verify the message is:
   - Clear about what went wrong
   - Uses correct `{0}`, `{1}` placeholders
   - Suggests what to do when possible

For example:
- Find `CheckerTypeMismatch` (or similar) in `MessageCode.cs` and verify its `.resx` message says something like "Type '{0}' is not assignable to type '{1}'"
- Find a parser error and verify it describes the syntax issue clearly

### Step 5: Verify CONTRIBUTING.md

Open `CONTRIBUTING.md` and check:
- [ ] No `// TODO` or `**TBD**` markers remain
- [ ] No TypeScript-specific references (`gulp`, `.ts`, `DiagnosticMessages.php`)
- [ ] The typo `dontnet test` is fixed to `dotnet test`
- [ ] Build instructions reference `local_build.sh` and `build.sh` correctly
- [ ] Localization section describes `.resx` / `IStringLocalizer` approach (not `DiagnosticMessages.php`)
- [ ] All referenced file paths exist in the repository

Run a quick verification:

```bash
# Check for remaining TODOs
rg "TODO" CONTRIBUTING.md
rg "TBD" CONTRIBUTING.md
rg "gulp" CONTRIBUTING.md
rg "dontnet" CONTRIBUTING.md
```

**Expected:** No matches for any of these patterns.

### Step 6: Verify Build Scripts Work

Test the local build:

```bash
./local_build.sh
```

**Expected:** The build completes successfully. A binary is produced in the `build/` directory. Run it:

```bash
./build/tyhp --version
```

**Expected:** The version number is printed, matching `tyhp.csproj`.

Test the release build (if Docker is available):

```bash
./build.sh
```

**Expected:** At least one target platform builds successfully. The output binary is placed in the `out/` directory.

### Step 7: Verify Performance Profiler

If Phase 6 added `PerformanceProfiler.cs`, verify it works by using the debug action with profiling:

```bash
# Parse a sample Tyhp file with profiling enabled
./build/tyhp debug --profile Examples/Generics.tyhp
```

**Expected:** The output includes both the existing ANTLR parser decision profile and the new phase-level timing summary. The timing summary should show phase names, durations, and memory usage.

### Step 8: Verify JSON Documentation Content (Spot Check)

Pick 3-5 JSON documentation files from different sections and open them. Verify:

- The JSON structure is valid (use a JSON validator or `python -m json.tool` — note the files may have `//` comments that standard validators reject; the custom parser in `generate_docs.php` handles these)
- No empty `"content"` arrays remain without a `PLACEHOLDER_STORY_N` comment
- Code examples in `tyhpCode` blocks look syntactically correct
- The `classDef` and `functionDef` blocks have all required fields

Good files to spot-check:
- `docs/content/tyhp_0500_generics.json` — should have rich generic examples
- `docs/content/tyhpdef_about.json` — should describe tyhpdef syntax
- `docs/content/cli_intro.json` — should list CLI actions matching `Action.cs`
- `docs/content/diagnostics_reference.json` — should list error codes matching `MessageCode.cs`

### Step 9: Cross-Cutting Consistency Checks

Run these quick consistency verifications:

```bash
# Verify the version in README matches tyhp.csproj
rg "<Version>" tyhp.csproj

# Verify all links in README.md point to existing files
# (manually check each [text](path) link)

# Verify no stale file references
rg "runtime/php-extensions/" CONTRIBUTING.md README.md docs/content/*.json
```

**Expected:** The version matches, all links resolve, and no references to the old `runtime/php-extensions/` path exist (this was moved to `runtime/packages/php-{version}/` in Story 21).

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [ ] **Golden fixtures:** Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [ ] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07.
- [ ] **Conformance run green:** The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [ ] **Runtime self-host conformance (runtime-affecting stories only):** Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [ ] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
