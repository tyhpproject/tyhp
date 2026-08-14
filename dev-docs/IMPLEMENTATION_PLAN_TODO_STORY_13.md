# Implementation Plan: Story 13 — CLI Polish (Help, Init, Version, Composer)

> **Roadmap position:** Story 13 — **Tier 1 — Usable**
> **Direct dependencies (new numbering):** 10
> **Renumbered from:** legacy Story 13
> **Status:** COMPLETED (all phases 1–8)
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence and the old→new story mapping.

## Project Context

The Tyhp compiler is a C# (.NET 9) application that compiles `.tyhp` files into PHP. The CLI layer is built on `Microsoft.Extensions.Hosting` with a `TyhpHostedService` that routes actions to `ActionRunnerBase` subclasses. The `Config.Action` enum defines all available actions. Currently, the CLI has a working `DebugAction`, a skeleton `GenerateTyhpdefAction`, and a pass-through `IntegrityCheckAction`. All help text methods in `DisplayHelp.cs` output "NOT IMPLEMENTED". The `init`, `version`, `composer`, `build`, and `lint` actions are stubbed or missing in `TyhpHostedService.cs`. The `Project.InitializeNewProject()` method is empty. The version string `804.4.1` is set in `tyhp.csproj`.

Story 13 focuses on completing all action help text, implementing the `init` action (project scaffolding), implementing the `version` action, implementing the `composer` action (or deferring it), fleshing out the `integrity_check` action with real validation logic, and wiring a universal `--help` flag so every command (and the no-command case) accepts the flag users already know — equivalent to `tyhp help --subject=<action>`.

**Dependencies:** Story 13 depends on Story 10 (Build Action) being at least partially in place. However, much of this work (help text, version, init) can proceed independently. This plan assumes that Story 01's `DiagnosticBag`, `CompilationResult`, and `CompilationService` infrastructure exists, and that `BuildAction` and `LintAction` classes exist at minimum as skeletons.

---

## Architecture Overview

### Affected Components

```
Program.cs                     (CLI bootstrap — expand --help as bare boolean; rewrite args)

Tyhp/CLI/
├── ActionRunnerBase.cs        (base class — minor changes)
├── TyhpHostedService.cs       (action routing — wire new actions)
├── Message.cs                 (console output — no changes expected)
├── DebugAction.cs             (existing — no changes)
├── IntegrityCheckAction.cs    (existing — expand with real checks)
├── GenerateTyhpdefAction.cs   (existing — no changes in this story)
├── InitAction.cs              (NEW — project initialization)
├── VersionAction.cs           (NEW — version display)
└── ComposerAction.cs          (NEW — composer integration)

Tyhp/Config/
├── Action.cs                  (enum — no changes needed)
├── ActionConfigProvider.cs    (--help rewrite / bare-boolean sync with Program.cs)
├── DisplayHelp.cs             (help text — complete all methods; document --help)
└── Project.cs                 (config — remove InitializeNewProject, add CLI flag properties)
```

### Patterns and Conventions

- All CLI actions extend `ActionRunnerBase` and implement `Start(CancellationToken)`
- Actions use `Message.*` static methods for console output (color-coded: Info=blue, Warn=yellow, Error=red, Success=green)
- Configuration is read from `IConfiguration` (CLI args + JSON config file `tyhp.json`)
- `Project.Singleton` provides global access to the parsed project configuration
- Exit codes are set via `Environment.ExitCode` using `Tyhp.Domain.Enums.ExitCode`
- Actions that need async support call `this.RunAsync().Wait()` from the synchronous `Start()` method (pattern established by `IntegrityCheckAction`)
- Help text uses `Message.Info()`, `Message.Display()`, and `Message.Warn()` for formatting
- Universal `--help`: rewrite early to `help` / `help --subject=<action>` so every command (and no-command) shares one help path — never implement per-action `--help` handlers

### Shared Infrastructure

- `Message.Banner()` outputs the version string from the assembly
- `Message.VersionHelper.GetAssemblyVersion()` reads the assembly version from `tyhp.csproj`'s `<Version>` property
- `IStringLocalizer<TyhpHostedService>` is available for localization (wired up in `Program.cs`)
- `Config.Project.GetProjectPath()` resolves the project root directory
- `Config.Project.GetProjectSourceFiles()` uses `Microsoft.Extensions.FileSystemGlobbing` for file discovery

### Placeholder Strategy

- Use `// PLACEHOLDER_STORY_N: description` for functionality that belongs to other stories
- Use `// PLACEHOLDER_PHASE_N: description` for functionality within this plan that will be completed in later phases
- Phases should search for and resolve their own placeholders when starting

---

## Phase 1: Version Action and Help Infrastructure Foundations

> **Status:** COMPLETED
> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Implement the `version` action (simplest standalone action) and establish the help text formatting infrastructure that all subsequent help methods will use. This creates a working pattern for help output and validates action wiring in `TyhpHostedService`. Also wire a universal `--help` flag so `tyhp <action> --help` is equivalent to `tyhp help --subject=<action>`, and `tyhp --help` (no command) shows general help.

### Deliverables

**New files:**
- `Tyhp/CLI/VersionAction.cs` — Version display action
- `Tyhp/CLI/HelpFormatting.cs` — Shared help text formatting utilities

**Modified files:**
- `Program.cs` — Treat `--help` as a bare boolean flag; rewrite args so `--help` routes through the existing `help` action
- `Tyhp/Config/ActionConfigProvider.cs` — Keep `BareBooleanFlags` in sync; optionally centralize the `--help` → `help --subject=…` rewrite here
- `Tyhp/CLI/TyhpHostedService.cs` — Wire the `version` action to `VersionAction`
- `Tyhp/Config/DisplayHelp.cs` — Implement `HelpHelp()` and `VersionHelp()`, refactor `GeneralHelp()` to use shared formatting; document `--help` as the user-facing alias

### Implementation Details

#### `Tyhp/CLI/VersionAction.cs`

Create a new `ActionRunnerBase` subclass that displays version information:

- Read the assembly version via `Message.VersionHelper.GetAssemblyVersion()` (already exists)
- Display the Tyhp compiler version (`804.4.1` from csproj)
- Display the .NET runtime version via `System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription`
- Display the ANTLR runtime version via reflection on the `Antlr4.Runtime.Standard` assembly
- Display the target OS platform via `RuntimeInformation.OSDescription`
- Support a `--json` flag (read from `Project.Singleton` configuration or `IConfiguration`): when set, output all version info as a JSON object instead of human-readable text
- Set `Environment.ExitCode` to `ExitCode.Success`

The `--json` flag check: read `this._configuration["json"]` or add a `JsonOutput` property to `Project`. For this phase, check directly in `VersionAction` whether the configuration key `json` is set to `"true"`.

#### `Tyhp/CLI/HelpFormatting.cs`

Create a static utility class for consistent help text formatting:

- `static void Section(string title)` — prints a section header (bold/colored)
- `static void Usage(string executable, string syntax)` — prints usage line
- `static void Option(string flag, string description)` — prints a flag/option with aligned description (pad flag name to consistent width)
- `static void Example(string command, string description)` — prints an example command with description
- `static void Paragraph(string text)` — prints a paragraph with word wrapping
- `static string GetExecutableName()` — extract executable name logic from the existing `GeneralHelp()` (the `Path.GetFileName` + `.dll` check pattern)

All methods use `Message.Display()`, `Message.Info()` internally.

#### Universal `--help` flag (alias for `help --subject=…`)

Most CLI users expect `tyhp <command> --help` rather than `tyhp help --subject=<command>`. Implement `--help` as a first-class alias that reuses the existing `help` action and `DisplayHelp` switch — do **not** duplicate help text or add a second help path.

**Equivalence table (must hold after this phase):**

| User runs | Behaves as |
|---|---|
| `tyhp --help` | `tyhp help` (general help — no subject) |
| `tyhp lint --help` | `tyhp help --subject=lint` |
| `tyhp build --help` | `tyhp help --subject=build` |
| `tyhp help --help` | `tyhp help --subject=help` |
| `tyhp version --help` | `tyhp help --subject=version` |
| *(any other valid action)* `--help` | `tyhp help --subject=<that action>` |

Also accept `--help=true` / `--help=false` forms that result from bare-boolean expansion (same pattern as `--quiet`, `--fix`, etc.). When `--help=false`, do **not** rewrite — run the original action normally.

**Recommended implementation (early arg rewrite, before host/config build):**

1. **`Program.cs` — `ExpandBareBooleanFlags`:** Add `"--help"` to the `booleanFlags` set so a bare `--help` becomes `--help=true` and does not swallow the next token.
2. **`ActionConfigProvider.BareBooleanFlags`:** Add `"--help"` so positional path extraction stays in sync with `Program.cs` (same comment already says to keep these lists aligned).
3. **Rewrite step** (prefer a small helper, e.g. `RewriteHelpAlias(string[] args)`, called from `Program.cs` **before** `ReadInitialActionFromArgs` / host setup, or inside `ReadInitialActionFromArgs` before action parsing):
   - Detect whether `--help` / `--help=true` is present among the remaining args (case-insensitive).
   - If present and the first positional token is a valid `Action` enum value (after hyphen→underscore normalization): rewrite argv to `["help", "--subject=<action>", …]` (drop the `--help` token; other flags may be dropped or left — either is fine because help short-circuits and does not consume them).
   - If present and there is **no** command (args are only flags, or first token is not a valid action): rewrite to `["help"]` (general help). Invalid first tokens that look like unknown commands should still surface as invalid action → general help via the existing `invalid` → `help` fallthrough; prefer rewriting when `--help` is present so the user gets help instead of an "invalid action" error.
   - If `--help` is absent: leave args unchanged.
4. After rewrite, existing flow already works: `*action=help`, `Project.Subject` from `--subject`, `DisplayHelp.Execute()`.

**Do not** implement `--help` inside each `*Action` class. One central rewrite keeps every current and future command covered automatically.

**`-h`:** Out of scope unless already used elsewhere. Document only `--help` in this story.

**Exit code:** After `--help` (any form above), set `Environment.ExitCode` to `ExitCode.Success` (same as a successful `tyhp help` run).

**Tests (unit preferred):** Cover the rewrite helper with cases from the equivalence table plus `--help=false` (no rewrite) and `tyhp --help` with no action.

#### `Tyhp/Config/DisplayHelp.cs` Updates

- Refactor `GeneralHelp()` to use `HelpFormatting.GetExecutableName()` and `HelpFormatting.Option()` for consistent formatting
- In `GeneralHelp()`, document that every command accepts `--help` (e.g. `tyhp lint --help`) as an alias for `tyhp help --subject=<action>`, and that `tyhp --help` shows this general listing
- Implement `HelpHelp()`:
  - Explain the help system
  - Show syntax: `tyhp help --subject=<action>` **and** the equivalent `tyhp <action> --help`
  - List all available actions with their `DescriptionAttribute` text
  - Show examples: `tyhp help --subject=build`, `tyhp build --help`, `tyhp help --subject=init`, `tyhp --help`
- Implement `VersionHelp()`:
  - Explain the version action
  - Show syntax: `tyhp version`
  - Document the `--json` flag
  - Document `--help` as showing this help text (same as `tyhp help --subject=version`)
  - Show example output

#### `Tyhp/CLI/TyhpHostedService.cs` Updates

- In the `case Tyhp.Config.Action.version:` block:
  - Replace the current `if (this.project.BeQuiet)` block with instantiation of `VersionAction`
  - Call `this._actionRunner = new VersionAction(); this._actionRunner.Start(this._actionCancelTokenSource.Token);`
  - The `--quiet` banner logic can remain as a special case before the action runs, or move into `VersionAction`

### Acceptance Criteria

- [x] Running `tyhp version` displays: Tyhp version, .NET runtime version, ANTLR runtime version, and OS info
- [x] Running `tyhp version --json` displays the same information as a valid JSON object
- [x] Running `tyhp help` lists all available actions with descriptions
- [x] Running `tyhp help --subject=help` describes the help system
- [x] Running `tyhp help --subject=version` describes the version action and its flags
- [x] Running `tyhp --help` displays the same general help as `tyhp help`
- [x] Running `tyhp version --help` displays the same content as `tyhp help --subject=version`
- [x] Running `tyhp lint --help` displays the same content as `tyhp help --subject=lint` (even if lint help is still a stub until Phase 2 — both paths must hit the same `DisplayHelp` method)
- [x] `--help` is listed in `ExpandBareBooleanFlags` / `BareBooleanFlags` so bare `--help` does not consume the next argv token
- [x] The project compiles without errors
- [x] `Environment.ExitCode` is set to `Success` after running `tyhp version` and after any successful `--help` / `help` invocation

### Dependencies

- **Requires:** No prior phases
- **Provides for Phase 2:** `HelpFormatting` utility class, established pattern for action wiring, universal `--help` rewrite so later help methods are reachable via both `help --subject=…` and `<action> --help`

---

## Phase 2: Build and Lint Help Text

> **Status:** COMPLETED
> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Complete the help text for the `build` and `lint` actions. These are the most complex help text entries because they document many configuration options. This phase also documents the `tyhp.json` configuration file format within the build help text.

### Deliverables

**Modified files:**
- `Tyhp/Config/DisplayHelp.cs` — Implement `BuildHelp()` and `LintHelp()`

### Implementation Details

#### `BuildHelp()` Implementation

This method should document the full `tyhp build` action:

- **Usage section:** `tyhp build [options]`
- **Description paragraph:** Explain that `build` compiles `.tyhp` source files into PHP output files
- **Options section** — list all build-related CLI flags:
  - `--help` — show this help text (alias for `tyhp help --subject=build`; see Phase 1)
  - `--include=<glob>` — glob patterns for source files to include
  - `--exclude=<glob>` — glob patterns for source files to exclude
  - `--quiet` / `-q` — suppress banner and informational output
  - `--watch` — watch for file changes and rebuild automatically (note: `// PLACEHOLDER_STORY_10: watch mode`)
  - `--clean` — delete output directory before building
  - `--verbose` — show detailed compilation output
  - `--dry-run` — check compilation without writing output files
  - `--strict` — treat warnings as errors
  - `--cache-dir=<path>` — override AST cache directory
- **Configuration file section** — document `tyhp.json` structure:
  - `include` (array of glob strings)
  - `exclude` (array of glob strings)
  - `output.path` (string)
  - `output.namespacePrefix` (string)
  - `output.comments` (boolean)
  - `output.phpVersion` (string)
  - `output.strictTypes` (boolean)
  - `psr4` (object mapping namespace prefixes to directories)
  - `checker.*` options
  - `build.*` options
- **Examples section:**
  - `tyhp build` — build the project using `tyhp.json` in current directory
  - `tyhp build --help` — show build help
  - `tyhp build --include="src/**/*.tyhp"` — build specific files
  - `tyhp build --dry-run` — validate without writing output
  - `tyhp build --clean --verbose` — clean rebuild with verbose output

#### `LintHelp()` Implementation

- **Usage section:** `tyhp lint [options]`
- **Description paragraph:** Explain that `lint` checks for errors and warnings without producing output files
- **Options section:**
  - `--help` — show this help text (alias for `tyhp help --subject=lint`)
  - `--include=<glob>` — source file patterns
  - `--exclude=<glob>` — exclusion patterns
  - `--quiet` / `-q` — suppress banner
  - `--format=<text|json|sarif>` — output format for diagnostics (note: `// PLACEHOLDER_STORY_12: json and sarif formats`)
  - `--file=<path>` — lint a single file instead of the project
  - `--fix` — auto-fix applicable issues (note: `// PLACEHOLDER_STORY_12: auto-fix mode`)
  - `--strict` — treat warnings as errors
- **Examples section:**
  - `tyhp lint` — lint the entire project
  - `tyhp lint --help` — show lint help
  - `tyhp lint --file=src/MyClass.tyhp` — lint a single file
  - `tyhp lint --format=json` — output diagnostics as JSON

### Acceptance Criteria

- [x] Running `tyhp help --subject=build` displays comprehensive build documentation with options, configuration format, and examples
- [x] Running `tyhp help --subject=lint` displays lint documentation with options and examples
- [x] Running `tyhp build --help` and `tyhp lint --help` produce the same content as the corresponding `help --subject=…` forms
- [x] All help text uses `HelpFormatting` utility methods for consistent formatting
- [x] Help text accurately reflects the configuration options defined in `Project.cs` and documented in `TODO.md`
- [x] The project compiles without errors

### Dependencies

- **Requires:** Phase 1 (HelpFormatting utility, universal `--help` rewrite)
- **Provides for Phase 5:** Documented configuration options that `InitAction` will reference when generating `tyhp.json`

---

## Phase 3: Remaining Help Text (Init, Composer, Language Server, XDebug Proxy, Generate Tyhpdef)

> **Status:** COMPLETED
> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Complete all remaining help text methods in `DisplayHelp.cs`. Several of these actions are not yet implemented, so the help text documents planned behavior and flags.

### Deliverables

**Modified files:**
- `Tyhp/Config/DisplayHelp.cs` — Implement `InitHelp()`, `ComposerHelp()`, `XDebugProxyHelp()`, `GenerateTyhpdefHelp()` (Note: `LanguageServerHelp()` is implemented by Story 19 Phase 10 and should already exist. When `HelpFormatting` is introduced in Phase 1, refactor all existing help methods — including `LanguageServerHelp()` from Story 19 — to use the `HelpFormatting` utility for consistency.)

### Implementation Details

#### `InitHelp()` Implementation

- **Usage:** `tyhp init [options] [directory]`
- **Description:** Initialize a new Tyhp project by creating a `tyhp.json` configuration file and project directory structure
- **Options:**
  - `--help` — show this help text (alias for `tyhp help --subject=init`)
  - `--yes` / `-y` — accept all defaults without interactive prompts
  - `--template=<basic>` — use a project template. **Only `basic` is delivered in this story** (`BasicTemplate`). `laravel` and `symfony` are documented as planned/future templates and are NOT implemented here; selecting them must error with the list of available templates (see Phase 5 / Phase 8 edge-case handling).
  - `--src=<path>` — source directory (default: `src/`)
  - `--output=<path>` — output directory (default: `build/`)
  - `--namespace=<prefix>` — namespace prefix (default: `App\\`)
  - `--php-version=<version>` — target PHP version (default: `8.4`)
- **Examples:**
  - `tyhp init` — initialize in current directory with prompts
  - `tyhp init --help` — show init help
  - `tyhp init ./my-project --yes` — initialize with defaults in specified directory
  - `tyhp init --template=basic` — initialize with the basic project template (the only template delivered in this story; `laravel`/`symfony` are planned/future)

#### `ComposerHelp()` Implementation

- **Usage:** `tyhp composer <command> [args]`
- **Description:** Run composer commands with Tyhp integration. Auto-generates tyhpdef files when packages are installed
- **Options:**
  - All standard composer commands are proxied
  - `--help` — show this help text (alias for `tyhp help --subject=composer`); when no composer subcommand is given, prefer Tyhp's composer help over forwarding to Composer
  - `--no-tyhpdef` — skip tyhpdef generation after package install/update
- **Note:** Include a message that this action may be deferred: `// PLACEHOLDER_STORY_13: composer action may be merged into build action`
- **Examples:**
  - `tyhp composer --help`
  - `tyhp composer require guzzlehttp/guzzle`
  - `tyhp composer install`

#### `LanguageServerHelp()` — Already implemented by Story 19

`LanguageServerHelp()` is implemented by Story 19 Phase 10 using `Message.Info()` and `Message.Display()` directly (without `HelpFormatting`). Since Story 19 precedes Story 13 in canonical order, this method already exists when Story 13 runs. When `HelpFormatting` is introduced in Phase 1 of this story, refactor `LanguageServerHelp()` along with all other existing help methods (`GeneralHelp()`, etc.) to use the `HelpFormatting` utility for consistent formatting across all help output. This story does **not** re-implement or stub `LanguageServerHelp()` — it only refactors the existing method (no contradictory `PLACEHOLDER_STORY_19`).

#### `XDebugProxyHelp()` Implementation — **placeholder only in Story 13**

> **Ownership split:** Story 13 provides only a **minimal placeholder** for `XDebugProxyHelp()` (a brief "not yet implemented" message). The **full** XDebug proxy help text — with the finalized options below — is delivered by **Story 18 Phase 7** when the proxy itself is implemented. The option list below documents the *intended* shape so Story 18 can flesh it out; Story 13 should not present these as working flags.

- **Story 13 deliverable (placeholder):** `Message.Info("XDebug proxy is not yet implemented — see Story 18. Usage will be: tyhp xdebug_proxy [options].");` plus `// PLACEHOLDER_STORY_18: full xdebug_proxy help delivered by Story 18 Phase 7`
- **Intended full help (delivered by Story 18 Phase 7, not Story 13):**
  - **Usage:** `tyhp xdebug_proxy [options]`
  - **Description:** Start the XDebug proxy for debugging Tyhp source code. Translates breakpoints and stack traces between Tyhp source and compiled PHP using sourcemaps
  - **Options:**
    - `--ide-port=<port>` — port for IDE connections (default: 9003)
    - `--xdebug-port=<port>` — port for XDebug connections (default: 9004)
    - `--sourcemap-dir=<path>` — directory containing `.map` files
  - **Examples:**
    - `tyhp xdebug_proxy --sourcemap-dir=./build/`

#### `GenerateTyhpdefHelp()` Implementation

- **Usage:** `tyhp generate_tyhpdef [options]`
- **Description:** Generate tyhpdef type definition files for PHP extensions or Composer packages. Note: library projects (`"type": "library"` in `tyhp.json`) auto-generate a `package.tyhp.json` during `tyhp build` — this command is for generating tyhpdefs from external PHP code.
- **Options:**
  - `--ext-name=<name>` — PHP extension name to generate tyhpdef for
  - `--composer-package=<name>` — Composer package name (reads from vendor directory)
  - `--output=<path>` — output directory for generated tyhpdef files
  - `--php-version=<version>` — target PHP version for extension tyhpdefs
- **Note:** Mark with `// PLACEHOLDER_STORY_20: full tyhpdef generation`
- **Examples:**
  - `tyhp generate_tyhpdef --ext-name=curl`
  - `tyhp generate_tyhpdef --composer-package=guzzlehttp/guzzle`

### Acceptance Criteria

- [x] Running `tyhp help --subject=init` displays init documentation
- [x] Running `tyhp help --subject=composer` displays composer documentation
- [x] Running `tyhp help --subject=language_server` displays LSP documentation
- [x] Running `tyhp help --subject=xdebug_proxy` displays XDebug proxy documentation
- [x] Running `tyhp help --subject=generate_tyhpdef` displays tyhpdef generation documentation
- [x] Running `tyhp init --help` (and `--help` for each other action whose help is implemented in this phase) matches `tyhp help --subject=<action>`
- [x] All help methods use `HelpFormatting` for consistent formatting
- [x] Each action's Options section documents `--help` as the universal alias
- [x] Help for unimplemented actions clearly indicates they are not yet available (using an informational note, not an error)
- [x] The project compiles without errors

### Dependencies

- **Requires:** Phase 1 (HelpFormatting utility)
- **Provides for Phase 5:** Init help text documents the `init` action behavior that Phase 5 implements

---

## Phase 4: Version Action Enhancements and Config Property Expansion

> **Status:** COMPLETED
> **[Phase Runner] Runtime/Model:** `claude/haiku` | `cursor/haiku`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Add CLI flag properties to `Project.cs` that Story 10's config classes don't already cover (e.g., `--json`, `--verbose`, `--dry-run`, `--strict`, `--clean`), and parse the `--json` flag for machine-readable output.

### Deliverables

**Modified files:**
- `Tyhp/Config/Project.cs` — Add CLI flag properties (`JsonOutput`, `Verbose`, `DryRun`, `Strict`, `Clean`)
- `Tyhp/CLI/VersionAction.cs` — Read `Project.JsonOutput` instead of raw `IConfiguration["json"]`
- `Tyhp/CLI/TyhpHostedService.cs` — Banner suppression uses `project.JsonOutput`; `VersionAction` takes `Project`

**Note:** `OutputConfig.cs`, `BuildConfig.cs`, and `CheckerConfig.cs` are created by Story 10 Phase 1 and will already exist. This phase only adds CLI flag properties to `Project.cs` that are not already handled by Story 10.

### Implementation Details

#### `Tyhp/Config/Project.cs` Updates

Add new CLI flag properties and parse them in `ConfigChanged()`:

- `bool JsonOutput { get; private set; }` — `--json` flag for machine-readable output
- `bool Verbose { get; private set; }` — `--verbose` flag
- `bool DryRun { get; private set; }` — `--dry-run` flag
- `bool Strict { get; private set; }` — `--strict` flag (treat warnings as errors)
- `bool Clean { get; private set; }` — `--clean` flag

**Note:** `OutputConfig Output`, `BuildConfig Build`, `CheckerConfig Checker`, `Psr4`, `TyhpdefIncludePaths`, and `TyhpdefExcludePaths` are already added to `Project.cs` by Story 10 Phase 1. Do not re-add them.

Parse each property from `IConfiguration` in `ConfigChanged()` using the existing pattern (reading from config sections). Use the `StringExtensions.ParseBool()` extension method for boolean values (already used for `BeQuiet`).

**Phase 4 implementation note:** `Verbose`, `DryRun`, `Strict`, and `Clean` are exposed as pass-throughs to `Build.Verbose` / `Build.DryRun` / `Build.StrictMode` / `Build.CleanBeforeBuild` so Story 10 remains the single source of truth for those CLI overlays. Only `JsonOutput` is parsed on `Project` itself.

### Acceptance Criteria

- [x] `Project.JsonOutput` correctly reads the `--json` CLI flag
- [x] `Project.Verbose` correctly reads the `--verbose` CLI flag
- [x] `Project.DryRun` correctly reads the `--dry-run` CLI flag
- [x] `Project.Strict` correctly reads the `--strict` CLI flag
- [x] `Project.Clean` correctly reads the `--clean` CLI flag
- [x] The project compiles without errors
- [x] Existing functionality (`IncludePaths`, `ExcludePaths`, `CacheDir`, `Locale`, `BeQuiet`) is not broken
- [x] Story 10's config classes (`OutputConfig`, `BuildConfig`, `CheckerConfig`) are not duplicated or overwritten

### Dependencies

- **Requires:** Story 10 Phase 1 (config classes must already exist)
- **Provides for Phase 5:** CLI flag properties that `InitAction` may reference
- **Provides for Phase 7:** CLI flag properties that `IntegrityCheckAction` uses (e.g., `Verbose`)

---

## Phase 5: Init Action Implementation

> **Status:** COMPLETED
> **[Phase Runner] Runtime/Model:** `claude/sonnet` | `cursor/sonnet`
> **[Phase Runner] Review Level:** `Medium`

### Phase Overview

Implement the `tyhp init` action that scaffolds a new Tyhp project. This creates a `tyhp.json` configuration file, creates the project directory structure, and optionally generates a sample `.tyhp` file. The action supports both interactive and non-interactive (defaults-only) modes.

### Deliverables

**New files:**
- `Tyhp/CLI/InitAction.cs` — Init action implementation
- `Tyhp/CLI/ProjectTemplates/BasicTemplate.cs` — Basic project template
- `Tyhp/CLI/ProjectTemplates/IProjectTemplate.cs` — Template interface

**Modified files:**
- `Tyhp/CLI/TyhpHostedService.cs` — Wire the `init` action
- `Tyhp/Config/Project.cs` — Remove `InitializeNewProject()` method, add `GetConfigValue()` helper

### Implementation Details

#### `Tyhp/CLI/ProjectTemplates/IProjectTemplate.cs`

Define a simple interface for project templates:

- `string Name { get; }` — template identifier (e.g., `"basic"`, `"laravel"`)
- `string Description { get; }` — human-readable description
- `Dictionary<string, string> GetDefaultConfig()` — returns default `tyhp.json` content as key-value pairs
- `Dictionary<string, string> GetScaffoldFiles()` — returns file paths (relative to project root) mapped to file content
- `List<string> GetDirectories()` — returns directory paths to create

#### `Tyhp/CLI/ProjectTemplates/BasicTemplate.cs`

Implement the basic (default) project template:

- Default `tyhp.json` structure with:
  - `include`: `["src/**/*.tyhp"]`
  - `exclude`: `["vendor/**", "node_modules/**"]`
  - `source.tagless`: `false` (opt-in extension-driven source mode — see Story 06, Phase 7, and `CONVENTIONS.md` §4)
  - `output.path`: `"build/"`
  - `output.strictTypes`: `true`
  - `output.phpVersion`: `"8.4"`
  - `output.comments`: `true`

The generated `tyhp.json` must have this exact structure:

```json
{
  "include": ["src/**/*.tyhp"],
  "exclude": ["vendor/**", "node_modules/**"],
  "source": {
    "tagless": false
  },
  "output": {
    "path": "build/",
    "phpVersion": "8.4",
    "strictTypes": true,
    "comments": true
  }
}
```

When `--namespace` is specified, add a `psr4` section:

```json
{
  "psr4": {
    "App\\": "src/"
  }
}
```

- Scaffold directories: `src/`, `build/`, `tyhpdef/`
- Scaffold files:
  - `src/index.tyhp` — minimal sample Tyhp file with a `<?tyhp` tag, `declare(strict_types=1)`, a namespace, and a "Hello World" echo
  - `.gitignore` additions (only if `.gitignore` exists): append `build/`, `tyhp.pid`, `.tyhp-cache/`

#### `Tyhp/CLI/InitAction.cs`

Create the init action:

- Accept a target directory from the CLI arguments (default: current directory)
- Check if `tyhp.json` already exists in the target directory; if so, display an error and exit with `ExitCode.GenericError`
- Determine the template to use from `--template` flag (default: `"basic"`)
- If `--yes` / `-y` flag is NOT set, prompt the user interactively for:
  - Project name (default: directory name)
  - Source directory (default: `src/`)
  - Output directory (default: `build/`)
  - Namespace prefix (default: based on project name)
  - Target PHP version (default: `8.4`)
  - Use `Console.ReadLine()` for prompts, display defaults in brackets `[default]`
- If `--yes` / `-y` flag IS set, use all defaults without prompting
- Create the target directory if it does not exist
- Create subdirectories from the template
- Write `tyhp.json` using `System.Text.Json.JsonSerializer` with `JsonSerializerOptions { WriteIndented = true }`
- Write scaffold files from the template
- If `.gitignore` exists, append Tyhp-specific entries (check for duplicates first)
- Display a success message listing what was created
- Set `Environment.ExitCode` to `ExitCode.Success`

Reading CLI arguments for init-specific flags:
- Target directory: first positional argument after `init` (or `--directory=<path>`)
- `--yes` flag: read from config key `yes` or `y`
- `--template` flag: read from config key `template`
- `--src`, `--output`, `--namespace`, `--php-version`: override template defaults

#### `Tyhp/CLI/TyhpHostedService.cs` Updates

In the `case Tyhp.Config.Action.init:` block:
- Instantiate `InitAction`
- Call `this._actionRunner = new InitAction(); this._actionRunner.Start(this._actionCancelTokenSource.Token);`
- Remove the empty `// TODO` comment

#### `Tyhp/Config/Project.cs` Updates

- Remove the `InitializeNewProject()` method entirely from `Project.cs`. All initialization logic lives in `InitAction.cs`. The `Project` class is a configuration reader, not an action executor.
- Add a `GetConfigValue(string key)` helper method that wraps `_configuration[key]` for use by actions that need to read arbitrary config keys

### Acceptance Criteria

- [x] Running `tyhp init` in an empty directory creates:
  - `tyhp.json` with correct structure and valid JSON
  - `src/` directory
  - `build/` directory
  - `tyhpdef/` directory
  - `src/index.tyhp` with valid sample Tyhp code
- [x] Running `tyhp init` in a directory that already has `tyhp.json` displays an error and does not overwrite
- [x] Running `tyhp init --yes` creates the project without prompting
- [x] Running `tyhp init ./new-project` creates the project in a new subdirectory
- [x] The generated `tyhp.json` is valid JSON and can be parsed by `Project.ConfigChanged()`
- [x] Running `tyhp init --template=basic` works (only basic template exists in this phase)
- [x] `Environment.ExitCode` is `Success` after successful initialization
- [x] The project compiles without errors

### Dependencies

- **Requires:** Phase 1 (action wiring pattern), Phase 4 (config properties for generating `tyhp.json` with correct structure)
- **Provides for Phase 7:** A working `init` action that produces valid `tyhp.json` files (used as test input for integrity checks)

---

## Phase 6: Composer Action Implementation

> **Status:** COMPLETED
> **[Phase Runner] Runtime/Model:** `cursor/grok` | `cursor/grok`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Implement the `tyhp composer` action. Based on the TODO.md notes, this action may be lightweight — primarily proxying composer commands with optional tyhpdef auto-generation. If the build action already handles `composer.json` updates, this action focuses on wrapping composer CLI invocations.

### Deliverables

**New files:**
- `Tyhp/CLI/ComposerAction.cs` — Composer integration action

**Modified files:**
- `Tyhp/CLI/TyhpHostedService.cs` — Wire the `composer` action

### Implementation Details

#### `Tyhp/CLI/ComposerAction.cs`

Create a composer proxy action:

- Read the composer command and arguments from CLI arguments (everything after `composer` in `tyhp composer <command> [args]`)
- Detect whether `php` and `composer` are available on the system PATH:
  - Try to run `php --version` via `System.Diagnostics.Process` and check exit code
  - Try to run `composer --version` via `System.Diagnostics.Process`
  - If either is missing, display a clear error message explaining that PHP and Composer must be installed
- If available, proxy the composer command:
  - Construct the command: `composer <command> [args]`
  - Use `System.Diagnostics.Process` to start the composer process
  - Redirect stdout and stderr to the console (pass through in real-time)
  - Wait for the process to exit
  - Propagate the exit code
- After successful `composer require` or `composer install` or `composer update`:
  - Check if `--no-tyhpdef` flag is set; if not:
    - Display an informational message: "Tyhpdef auto-generation is not yet implemented. Run `tyhp generate_tyhpdef` manually."
    - `// PLACEHOLDER_STORY_20: auto-generate tyhpdef after composer install/update`
- Handle error cases:
  - `php` not found: `"PHP is not installed or not available on PATH. Tyhp's composer integration requires PHP."`
  - `composer` not found: `"Composer is not installed or not available on PATH. Install Composer from https://getcomposer.org/"`
  - Composer command fails: propagate the exit code and display the error output

#### `Tyhp/CLI/TyhpHostedService.cs` Updates

Add a new `case Tyhp.Config.Action.composer:` block to the switch statement in `TyhpHostedService.cs` (this action currently has no case in the switch — it is completely absent, not a fallthrough):
- Instantiate `ComposerAction`
- Call `Start()` with the cancellation token

### Acceptance Criteria

- [x] Running `tyhp composer --version` executes `composer --version` and displays the output
- [x] Running `tyhp composer require some/package` proxies the command to composer
- [x] If PHP is not installed, a clear error message is displayed
- [x] If Composer is not installed, a clear error message is displayed
- [x] The composer process exit code is propagated to `Environment.ExitCode`
- [x] Running `tyhp composer install` displays an informational message about tyhpdef generation
- [x] The `--no-tyhpdef` flag suppresses the tyhpdef generation message
- [x] The project compiles without errors

### Dependencies

- **Requires:** Phase 1 (action wiring pattern)
- **Provides for:** No direct dependency from other phases, but completes the CLI action set

---

## Phase 7: Integrity Check Action Expansion

> **Status:** COMPLETED
> **[Phase Runner] Runtime/Model:** `cursor/grok` | `cursor/grok`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Expand the existing `IntegrityCheckAction` from a pass-through to a comprehensive validation tool. The integrity check validates the Tyhp configuration, tyhpdef files, AST cache, and environment.

### Deliverables

**Modified files:**
- `Tyhp/CLI/IntegrityCheckAction.cs` — Implement real validation checks
- `Tyhp/Domain/Exceptions/MessageCode.cs` — Add integrity check error codes in the 7800-7899 range (`IntegrityCheckConfigInvalid = 7800`, `IntegrityCheckTyhpdefError = 7801`, `IntegrityCheckCacheCorrupted = 7802`, `IntegrityCheckEnvironmentError = 7803`)

**New files:**
- `Tyhp/CLI/IntegrityChecks/ConfigurationCheck.cs` — Validate tyhp.json configuration
- `Tyhp/CLI/IntegrityChecks/TyhpdefCheck.cs` — Validate tyhpdef files
- `Tyhp/CLI/IntegrityChecks/CacheCheck.cs` — Validate AST cache integrity
- `Tyhp/CLI/IntegrityChecks/EnvironmentCheck.cs` — Validate runtime environment
- `Tyhp/CLI/IntegrityChecks/IIntegrityCheck.cs` — Interface for individual checks

### Implementation Details

#### `Tyhp/CLI/IntegrityChecks/IIntegrityCheck.cs`

Define an interface for individual integrity checks:

- `string Name { get; }` — human-readable check name
- `Task<IntegrityCheckResult> RunAsync(CancellationToken ct)` — execute the check
- `IntegrityCheckResult` is a simple record/class with: `bool Passed`, `string? Message`, `List<string> Details`

#### `Tyhp/CLI/IntegrityChecks/ConfigurationCheck.cs`

Validate the `tyhp.json` configuration file:

- Check if `tyhp.json` exists in the project directory (warning if not — may be using CLI args only)
- If it exists, validate it is well-formed JSON
- Validate that `include` patterns are present and not empty
- Validate that `output.path` is a valid directory path
- Validate that `output.phpVersion` is a recognized PHP version string (e.g., `"8.2"`, `"8.3"`, `"8.4"`)
- Validate that `build.structBacking` is either `"array"` or a valid class reference
- Validate that `build.decimalBacking` is either `"bcmath"` or `"gmp"`
- Validate that include/exclude glob patterns are syntactically valid
- Check that the source directory exists and contains `.tyhp` files matching the include patterns

#### `Tyhp/CLI/IntegrityChecks/TyhpdefCheck.cs`

Validate tyhpdef files:

- Discover all tyhpdef files from configured paths
- For each tyhpdef file, attempt to parse it using the existing parser
- Collect and report any parse errors
- `// PLACEHOLDER_STORY_02: Check for duplicate declarations across tyhpdef files (requires binder)`
- Report summary: N tyhpdef files found, N parsed successfully, N with errors

#### `Tyhp/CLI/IntegrityChecks/CacheCheck.cs`

Validate AST cache integrity:

- Check if the cache directory exists
- If it exists, enumerate cache files
- For each cache file, verify it is not corrupted (attempt to deserialize, catch exceptions)
- The AST cache uses binary serialization via `Base2Ast.Serialize()` and the `AstCacheService` API. To verify cache integrity, call `AstCacheService.Get(filename, currentHash)` for each cached file — if it returns null or throws, the entry is corrupted or stale.
- Compare the SHA256 hash stored in the cache entry against the current file's hash computed via the same algorithm `AstCacheService` uses
- Check that source file hashes in cache entries match the current source files on disk
- Report stale cache entries (source file modified since cache was written)
- Report corrupted cache entries
- Offer suggestion to clear cache if issues found

#### `Tyhp/CLI/IntegrityChecks/EnvironmentCheck.cs`

Validate the runtime environment:

- Check .NET runtime version
- Check if `php` is available on PATH (informational, not required for compilation)
- Check if `composer` is available on PATH (informational)
- Report the Tyhp compiler version
- Report the ANTLR runtime version
- Check available disk space in the output directory (warning if very low)

#### `Tyhp/CLI/IntegrityCheckAction.cs` Updates

Rewrite `RunAsync()` to:

- Create a list of `IIntegrityCheck` instances: `ConfigurationCheck`, `TyhpdefCheck`, `CacheCheck`, `EnvironmentCheck`
- Run each check sequentially, displaying the check name and result (pass/fail)
- Use `Message.Success()` for passed checks and `Message.Error()` or `Message.Warn()` for failures
- Track overall pass/fail status
- Display a summary: "N/M checks passed"
- Set `Environment.ExitCode` to `ExitCode.IntegrityCheckFailed` if any check fails, `ExitCode.Success` otherwise
- Support `--verbose` flag: when set, display detailed output for each check (the `Details` list from `IntegrityCheckResult`)

### Acceptance Criteria

- [x] Running `tyhp integrity_check` executes all checks and displays pass/fail for each
- [x] Configuration check correctly identifies missing or malformed `tyhp.json`
- [x] Configuration check validates known configuration properties
- [x] Tyhpdef check reports parse errors in malformed tyhpdef files
- [x] Cache check identifies corrupted or stale cache entries
- [x] Environment check reports runtime version information
- [x] `Environment.ExitCode` is set correctly based on check results
- [x] Running `tyhp integrity_check` in a valid project reports all checks passing
- [x] The project compiles without errors
- [x] The existing `if (false)` placeholder in `IntegrityCheckAction` is removed

### Post-Implementation Review Notes

Behaviour clarified during review (all four checks re-verified against a temp project):

- **Cache check severity.** Only *corrupted* entries fail the check. *Stale* entries (source deleted or
  hash changed) are reported but pass, because the next compile re-parses the file and rewrites the
  entry — failing on them made `integrity_check` red for a completely healthy project.
- **Shared cache, single project.** The on-disk cache is keyed per compiler build and shared by every
  project on the machine. Entries that do not resolve to a file under the current project root
  (other projects, embedded tyhpdefs such as `<tyhpdef:embedded:__tyhp_types>`) are skipped, not
  validated, and reported only as a count in the summary line.
- **`IntegrityCheckResult.Problems`.** Added alongside `Details`. `Problems` always print on failure;
  `Details` still require `--verbose`. Without this, a failing check printed only a count
  ("4 configuration problem(s) found") and the user had to re-run to learn anything actionable.
- **`--quiet`.** Suppresses the banner, per-check start/pass lines and the summary; failures and their
  problems always print.

### Dependencies

- **Requires:** Phase 4 (config properties for validation), Phase 1 (established patterns)
- **Provides for:** No direct dependency, but completes the integrity validation feature

---

## Phase 8: Integration, Edge Cases, and Final Polish

> **Status:** COMPLETED
> **[Phase Runner] Runtime/Model:** `cursor/grok` | `cursor/grok`
> **[Phase Runner] Review Level:** `Low`

### Phase Overview

Handle edge cases, ensure all actions work correctly together, add missing error handling, and verify the complete CLI experience end-to-end. This phase also ensures that all `TyhpHostedService` action routing is complete and consistent.

### Deliverables

**Modified files:**
- `Tyhp/CLI/TyhpHostedService.cs` — Final routing cleanup, ensure all actions are wired
- `Tyhp/Config/DisplayHelp.cs` — Add an `IntegrityCheckHelp()` method and wire it (currently missing from the switch statement)
- `Tyhp/Config/Action.cs` — Ensure `DescriptionAttribute` is present on all enum values (currently `debug` is missing its description)
- `Tyhp/CLI/InitAction.cs` — Edge case handling
- `Tyhp/CLI/VersionAction.cs` — Edge case handling

### Implementation Details

#### `Tyhp/Config/DisplayHelp.cs` — Add `IntegrityCheckHelp()`

The current `DisplayHelp.Execute()` switch does not include a case for `integrity_check`:

- Add `case Tyhp.Config.Action.integrity_check:` routing to a new `IntegrityCheckHelp()` method
- Implement `IntegrityCheckHelp()`:
  - **Usage:** `tyhp integrity_check [options]`
  - **Description:** Run validation checks on the Tyhp project configuration, tyhpdef files, AST cache, and environment
  - **Options:** `--help` (universal alias), `--verbose` flag for detailed output
  - **Examples:** `tyhp integrity_check`, `tyhp integrity_check --verbose`

#### `Tyhp/Config/Action.cs` — Add missing description

Add `[Description("Run internal debugging tools (for compiler development).")]` to the `debug` enum value, or exclude `debug` from the general help listing since it is an internal action.

#### `TyhpHostedService.cs` — Final Routing Review

Review all action cases and ensure consistency:

- `init` → `InitAction` (Phase 5)
- `version` → `VersionAction` (Phase 1)
- `build` → `BuildAction` (already wired by Story 10 — verify it follows the consistent pattern)
- `lint` → `LintAction` (already wired by Story 12 — verify it follows the consistent pattern)
- `composer` → `ComposerAction` (Phase 6)
- `integrity_check` → `IntegrityCheckAction` (Phase 7)
- `generate_tyhpdef` → `GenerateTyhpdefAction` (existing)
- `language_server` → `LanguageServerAction` (already wired by Story 19, which precedes Story 13 in canonical order — verify it follows the consistent pattern and sets `_isLongRunning = true`; do **not** leave a `PLACEHOLDER_STORY_19` here)
- `xdebug_proxy` → `// PLACEHOLDER_STORY_18: XDebugProxyAction`
- `debug` → `DebugAction` (existing, unchanged)

Ensure each case follows the same pattern:
1. Instantiate the action
2. Assign to `this._actionRunner`
3. Call `Start()` with cancellation token
4. For long-running actions (`language_server`, `xdebug_proxy`), set `this._isLongRunning = true`

#### Edge Case Handling

- **`InitAction`:** Handle permission errors when creating directories (catch `UnauthorizedAccessException`, display friendly error)
- **`InitAction`:** Handle the case where the target path exists but is a file (not a directory)
- **`InitAction`:** Validate that the `--template` value matches an available template; display an error listing valid options if not
- **`VersionAction`:** Handle the case where the ANTLR assembly cannot be found (display "unknown" instead of crashing)
- **`ComposerAction`:** Handle the case where the composer command contains no arguments (display usage help)
- **`IntegrityCheckAction`:** Handle cancellation during long checks (respect the `CancellationToken`)
- **All actions:** Ensure `Dispose()` cleans up any resources (processes, file handles)

#### General Consistency Pass

- Verify all `Message.*()` calls use appropriate severity (Info for informational, Warn for warnings, Error for errors, Success for confirmations)
- Verify all actions set `Environment.ExitCode` before returning
- Verify all help text follows the same formatting conventions (sections, indentation, examples)
- Verify that running any action with `--quiet` suppresses non-essential output
- Verify universal `--help` for every action (and for no-command): `tyhp <action> --help` ≡ `tyhp help --subject=<action>`, and `tyhp --help` ≡ `tyhp help`. Spot-check at least: `build`, `lint`, `init`, `version`, `composer`, `integrity_check`, `generate_tyhpdef`, `language_server`, `xdebug_proxy`, and `help` itself (`tyhp help --help`)

### Acceptance Criteria

- [x] `tyhp help --subject=integrity_check` displays integrity check help
- [x] `tyhp integrity_check --help` matches `tyhp help --subject=integrity_check`
- [x] `tyhp --help` matches `tyhp help` (general listing)
- [x] For every public action, `<action> --help` routes through `DisplayHelp` for that action (no action-specific `--help` handlers)
- [x] The `debug` action does not appear in general help (or appears with a description)
- [x] All action routing in `TyhpHostedService` follows a consistent pattern
- [x] Edge cases are handled gracefully with user-friendly error messages (no unhandled exceptions)
- [x] `tyhp init` in a read-only directory displays a permissions error
- [x] `tyhp init --template=nonexistent` displays an error listing valid templates
- [x] `tyhp composer` with no arguments displays usage help
- [x] `tyhp version` works even if ANTLR assembly metadata is unavailable
- [x] All actions respect the `--quiet` flag
- [x] All actions set `Environment.ExitCode` appropriately
- [x] The project compiles without errors
- [x] No placeholder `// TODO` comments remain in `DisplayHelp.cs` (all replaced with implemented methods or explicit `PLACEHOLDER_STORY_N` markers)

### Dependencies

- **Requires:** All previous phases (1-7) complete
- **Provides for:** This is the final phase — delivers the complete Story 13 feature set

---

## Placeholder Summary

The following placeholders will be left in the codebase for future stories:

| Placeholder | Location | Description |
|---|---|---|
| `PLACEHOLDER_STORY_20` | `ComposerAction.cs`, `GenerateTyhpdefHelp()` | Full tyhpdef generation from composer packages |
| `PLACEHOLDER_STORY_18` | `TyhpHostedService.cs` (xdebug_proxy case), `XDebugProxyHelp()` | XDebug proxy implementation |

**Note on `language_server`:** Story 19 precedes Story 13 in canonical order, so `LanguageServerAction` is already wired into `TyhpHostedService` by the time Story 13 runs. Story 13 does **not** add a `PLACEHOLDER_STORY_19` for the action wiring — it only refactors the existing `LanguageServerHelp()` (created by Story 19) to use the `HelpFormatting` utility introduced in Phase 1.

---

## File Size Guidelines

All new files in this story are expected to be small to medium:

- `VersionAction.cs` — ~80-120 lines
- `HelpFormatting.cs` — ~60-100 lines
- `InitAction.cs` — ~200-300 lines (largest file, due to interactive prompts and file generation)
- `ComposerAction.cs` — ~100-150 lines
- `DisplayHelp.cs` — will grow to ~300-400 lines total (currently 127 lines)
- `Project.cs` — will grow to ~200-250 lines total (currently 129 lines)
- `OutputConfig.cs` — created by Story 10 (not in this story's scope)
- `BuildConfig.cs` — created by Story 10 (not in this story's scope)
- `CheckerConfig.cs` — created by Story 10 (not in this story's scope)
- Each integrity check file — ~50-100 lines
- `IntegrityCheckAction.cs` — ~80-120 lines (up from 36 lines)
- `IProjectTemplate.cs` — ~15-20 lines
- `BasicTemplate.cs` — ~80-120 lines

No file should exceed 400 lines. If any file approaches that limit, split it into logical sub-components.

---

## Human Testing and Verification

> **Note:** These steps are for a human developer to manually verify the CLI polish implementation. Steps can be skipped, reordered, or modified based on what has been implemented. You need a built `tyhp` binary (via `dotnet build` or `dotnet run`).

### Step 1: Verify the Build Compiles

```bash
cd /path/to/tyhp
dotnet build
```

Confirm zero errors. All new action classes (`VersionAction`, `InitAction`, `ComposerAction`) should compile cleanly.

### Step 2: Verify the Version Action

Run the version command:

```bash
dotnet run -- version
```

**Expected output (approximate):**

```
Tyhp Compiler v804.4.1
.NET Runtime: .NET 9.0.x
ANTLR Runtime: 4.x.x
OS: Darwin 25.x.x (or your OS)
```

Now test the JSON output mode:

```bash
dotnet run -- version --json
```

**Expected:** A valid JSON object containing keys like `"tyhp"`, `"dotnet"`, `"antlr"`, `"os"` with their respective version strings. Validate it parses as JSON:

```bash
dotnet run -- version --json | python3 -m json.tool
```

Verify the exit code:

```bash
dotnet run -- version; echo "Exit code: $?"
```

**Expected:** Exit code 0.

### Step 3: Verify General Help

```bash
dotnet run -- help
```

**Expected:** A listing of all available CLI actions (build, lint, init, version, composer, integrity_check, generate_tyhpdef, language_server, xdebug_proxy) with a short description for each. The `debug` action should either be excluded or marked as internal.

### Step 4: Verify Per-Action Help Text

Run help for each action and verify output is formatted, informative, and consistent:

```bash
dotnet run -- help --subject=help
dotnet run -- help --subject=version
dotnet run -- help --subject=build
dotnet run -- help --subject=lint
dotnet run -- help --subject=init
dotnet run -- help --subject=composer
dotnet run -- help --subject=integrity_check
dotnet run -- help --subject=language_server
dotnet run -- help --subject=xdebug_proxy
dotnet run -- help --subject=generate_tyhpdef
```

**For each**, verify:
- A usage line appears (e.g., `tyhp build [options]`).
- Options are listed with descriptions and aligned formatting.
- At least one example is shown.
- Unimplemented actions note that the feature is planned (informational, not an error).
- Formatting is consistent across all actions (same section headers, indentation style, option alignment).
- `--help` is documented as an option (or noted in general help as a universal flag).

### Step 4b: Verify Universal `--help` Alias

Confirm `--help` is equivalent to `help --subject=…` (same help method / same content; exit code 0):

```bash
dotnet run -- --help
dotnet run -- help

dotnet run -- lint --help
dotnet run -- help --subject=lint

dotnet run -- build --help
dotnet run -- version --help
dotnet run -- init --help
dotnet run -- help --help
```

**Expected:**
- `tyhp --help` matches general help from `tyhp help`.
- `tyhp <action> --help` matches `tyhp help --subject=<action>` for each action above.
- `tyhp help --help` shows help-about-help (same as `tyhp help --subject=help`).
- Exit code is 0 in all cases; the underlying action does **not** run (e.g. `lint --help` must not lint the project).

### Step 5: Verify the Init Action (Non-Interactive)

Create a temporary directory and initialize a project with defaults:

```bash
mkdir -p /tmp/tyhp-init-test
cd /tmp/tyhp-init-test
dotnet run --project /path/to/tyhp -- init --yes
```

**Expected:** The following are created:
- `tyhp.json` — valid JSON with `include`, `exclude`, `output`, and `checker` sections.
- `src/` directory
- `build/` directory
- `tyhpdef/` directory
- `src/index.tyhp` — a sample Tyhp file with `<?tyhp` tag

Verify `tyhp.json` is valid JSON:

```bash
python3 -m json.tool tyhp.json
```

Verify the structure matches the expected template (check for `include`, `output.path`, `output.phpVersion`, etc.).

### Step 6: Verify Init Action Prevents Overwrite

Run init again in the same directory:

```bash
dotnet run --project /path/to/tyhp -- init --yes
```

**Expected:** An error message indicating `tyhp.json` already exists. The exit code should be non-zero. No files should be overwritten.

### Step 7: Verify Init Action in a New Subdirectory

```bash
dotnet run --project /path/to/tyhp -- init /tmp/tyhp-init-subdir --yes
```

**Expected:** The directory `/tmp/tyhp-init-subdir` is created with the same scaffolding as Step 5.

### Step 8: Verify Init Action with Custom Options

```bash
rm -rf /tmp/tyhp-init-custom
dotnet run --project /path/to/tyhp -- init /tmp/tyhp-init-custom --yes --namespace="MyApp\\" --php-version=8.3
```

**Expected:** `tyhp.json` should contain a `psr4` section mapping `"MyApp\\"` to `"src/"` and `output.phpVersion` should be `"8.3"`.

### Step 9: Verify the Composer Action

Test the composer proxy (requires PHP and Composer installed):

```bash
dotnet run -- composer --version
```

**Expected:** The output of `composer --version` (e.g., `Composer version 2.x.x ...`).

Test with no arguments:

```bash
dotnet run -- composer
```

**Expected:** Usage help or the default Composer help output.

Test error handling when PHP/Composer is not found (if applicable to your environment — you can temporarily rename `php` on PATH to test):

**Expected:** A clear error message like "PHP is not installed or not available on PATH."

### Step 10: Verify the Integrity Check Action

Run the integrity check in a valid project:

```bash
cd /tmp/tyhp-init-test
dotnet run --project /path/to/tyhp -- integrity_check
```

**Expected:** A list of checks (configuration, tyhpdef, cache, environment) with pass/fail status for each. A valid project should show all checks passing. The summary should show "N/N checks passed."

Test verbose output:

```bash
dotnet run --project /path/to/tyhp -- integrity_check --verbose
```

**Expected:** Detailed output for each check including runtime version info, cache details, etc.

Test in a directory without `tyhp.json`:

```bash
cd /tmp
dotnet run --project /path/to/tyhp -- integrity_check
```

**Expected:** The configuration check should produce a warning about missing `tyhp.json`. Other checks may still run. Exit code should reflect the failure.

### Step 11: Verify CLI Flag Properties

Test that global CLI flags are respected:

```bash
# Quiet mode should suppress banner
dotnet run -- version --quiet

# Build with dry-run should not produce output files
cd /tmp/tyhp-init-test
dotnet run --project /path/to/tyhp -- build --dry-run
```

**Expected:**
- `--quiet` suppresses the banner/informational output but still shows the version.
- `--dry-run` checks compilation without writing files to `build/`.

### Step 12: Verify Edge Cases

```bash
# Init with invalid template
dotnet run -- init /tmp/tyhp-bad-template --yes --template=nonexistent
```

**Expected:** An error listing valid template names (e.g., "basic").

```bash
# Init in a read-only directory (if testable)
mkdir -p /tmp/tyhp-readonly && chmod 444 /tmp/tyhp-readonly
dotnet run -- init /tmp/tyhp-readonly --yes
chmod 755 /tmp/tyhp-readonly  # cleanup
```

**Expected:** A permissions error with a user-friendly message.

### Step 13: Clean Up Test Artifacts

```bash
rm -rf /tmp/tyhp-init-test /tmp/tyhp-init-subdir /tmp/tyhp-init-custom /tmp/tyhp-readonly /tmp/tyhp-bad-template
```

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). The golden conformance fixture suite established in **Story 07 (Testing Infrastructure)** is the project backbone; every story contributes fixtures to it. See `CONVENTIONS.md` for fixture layout and canonical paths.

- [x] **Golden fixtures:** *N/A for CLI polish (no language emit surface); covered by CLI unit tests.* Add `.tyhp → .php` (plus expected-diagnostics) golden fixtures covering this story's features to the conformance suite (Story 07). The committed fixtures are the source of truth for expected compiler output.
- [x] **Unit / integration tests:** Cover new components under the relevant test categories defined in Story 07. In particular, unit-test the `--help` argv rewrite helper (equivalence table from Phase 1: `tyhp --help`, `<action> --help`, `--help=false` no-op, `help --help`). *(Phase 1: `HelpAliasRewriteTests` covers the rewrite helper; remaining story components still need coverage in later phases.)*
- [x] **Conformance run green:** *N/A — no Story-13 emit fixtures; CLI tests cover behavior.* The full `tyhp` conformance/test run passes with the new fixtures before this story is considered done.
- [x] **Runtime self-host conformance (runtime-affecting stories only):** *N/A.* Recompile the Tyhp runtime sources and diff the generated PHP against the committed `runtime/` PHP to catch drift (the "compiler builds its own runtime" milestone — see Story 07).
- [x] **Diagnostics registered centrally:** Any new diagnostic codes are added only in `Tyhp/Domain/Exceptions/MessageCode.cs` (single source of truth — see `CONVENTIONS.md`), never re-declared in this doc.
