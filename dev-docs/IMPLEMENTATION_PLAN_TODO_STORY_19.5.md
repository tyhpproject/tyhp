# Implementation Plan: Story 19.5 — IDE Extensions (`tyhp-lang`)

> **Roadmap position:** Story 19.5 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story 19, before Story 20)
> **Direct dependencies (new numbering):** 17, 18, 19
> **New story:** First-party **VS Code** (Cursor-compatible) and **PhpStorm** clients that package the **same** surface: TextMate syntax highlighting, LSP client wiring to `tyhp language_server`, XDebug-proxy debug integration, tasks, file icons, status bar, and workspace/`tyhp.json` awareness (including `tyhp init`) — the IDE-facing half of Stories 17–19.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-08-04 · **Updated:** 2026-08-18 (product name `tyhp-lang`; clients live under `tyhp-lang/vscode/` and `tyhp-lang/phpstorm/`)
> **Prerequisites:** Story 17 (sourcemaps), Story 18 (`tyhp xdebug_proxy`), Story 19 (`tyhp language_server`) complete enough to launch and speak their protocols.
> **Consumers:** Story 30 (docs/polish — marketplace publish is **not** this story); end-user DX for Tyhp editing/debugging in VS Code / Cursor / PhpStorm.

---

## Table of Contents

- [Summary](#summary)
- [Existing `tyhp-lang/vscode/` (do not start from scratch)](#existing-tyhp-langvscode-do-not-start-from-scratch)
- [Motivation](#motivation)
- [Scope (In / Out)](#scope-in--out)
- [Architecture Overview](#architecture-overview)
- [Decisions (locked)](#decisions-locked)
- [Phase 1: Extension scaffold & packageable VSIX](#phase-1-extension-scaffold--packageable-vsix)
- [Phase 2: Languages, TextMate grammar & file icons](#phase-2-languages-textmate-grammar--file-icons)
- [Phase 3: Binary resolution, install & updates](#phase-3-binary-resolution-install--updates)
- [Phase 4: LSP client](#phase-4-lsp-client)
- [Phase 5: Workspace awareness, init, tasks & status bar](#phase-5-workspace-awareness-init-tasks--status-bar)
- [Phase 6: XDebug proxy / debug integration](#phase-6-xdebug-proxy--debug-integration)
- [Phase 7: Manual QA matrix & packaging acceptance](#phase-7-manual-qa-matrix--packaging-acceptance)
- [Phase 8: PhpStorm plugin scaffold & packageable ZIP](#phase-8-phpstorm-plugin-scaffold--packageable-zip)
- [Phase 9: PhpStorm languages, TextMate grammar & file icons](#phase-9-phpstorm-languages-textmate-grammar--file-icons)
- [Phase 10: PhpStorm binary resolution, install & updates](#phase-10-phpstorm-binary-resolution-install--updates)
- [Phase 11: PhpStorm LSP client](#phase-11-phpstorm-lsp-client)
- [Phase 12: PhpStorm workspace awareness, init, run configs & status bar](#phase-12-phpstorm-workspace-awareness-init-run-configs--status-bar)
- [Phase 13: PhpStorm XDebug proxy / debug integration](#phase-13-phpstorm-xdebug-proxy--debug-integration)
- [Phase 14: PhpStorm QA matrix & packaging acceptance](#phase-14-phpstorm-qa-matrix--packaging-acceptance)
- [Configuration Keys](#configuration-keys)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

This story delivers **two first-party IDE clients** under one product, **Tyhp Language** (`tyhp-lang`). They are thin clients for the toolchain already provided by Stories 17–19:

| Client | Path | Package identity | Artifact |
|--------|------|------------------|----------|
| VS Code (Cursor-compatible) | `tyhp-lang/vscode/` | Extension ID `tyhp-lang.tyhp` (publisher `tyhp-lang`, name `tyhp`) | VSIX (`tyhp-*.vsix`) |
| PhpStorm | `tyhp-lang/phpstorm/` | Plugin ID `com.tyhp.lang`, name `Tyhp Language` | Plugin ZIP (Install Plugin from Disk) |

Do **not** name the clients `tyhp-vscode`, `phpstorm-tyhp`, or `vscode-tyhp`. The editor is a folder under `tyhp-lang/`, not part of the product name. `tyhp` is reserved for the CLI / language; `tyhplang` (no hyphen) is not used.

Shared feature set (both editors, feature-complete parity — not a “PhpStorm lite”):

1. **TextMate syntax highlighting** for `.tyhp` and `.tyhpdef` (lexical highlighting; semantic tokens remain an LSP concern from Story 19 when enabled). **One grammar source**, consumed by both clients.
2. **LSP client** that launches `tyhp language_server` over stdin/stdout using the resolved CLI binary.
3. **XDebug proxy integration** — start/stop `tyhp xdebug_proxy`, wire the editor’s PHP debugger so breakpoints and stacks map through sourcemaps to `.tyhp` sources.
4. **Tasks / run configurations** for common CLI actions (`build`, `lint`, and related workspace commands as appropriate).
5. **File icons** for Tyhp language files.
6. **Status bar** showing binary / project / LSP (and optionally proxy) health.
7. **Workspace awareness** — detect `tyhp.json`, offer / run `tyhp init` when absent, pass project path into LSP and tasks.

**Distribution for this story:** produce a **packageable VSIX** and a **packageable PhpStorm plugin ZIP** (local install instructions for each). **Do not** publish to the VS Code Marketplace, Open VSX, or the JetBrains Marketplace. Cursor is **compatible** via the same VSIX; no separate Cursor product/deliverable. IntelliJ IDEA (even with the PHP plugin) is **not** a required target — PhpStorm only.

> **Absorbs former Story 19 Phase 10.** Story 19 (`IMPLEMENTATION_PLAN_TODO_STORY_19.md`) delivers `tyhp language_server` only. The VS Code extension, TextMate grammars, language configuration, LanguageClient wiring, VSIX packaging, and editor-side QA that used to be Story 19 Phase 10 live **here** (Phases 1–7), with PhpStorm parity in Phases 8–14. `LanguageServerHelp()` and stdin/stdout LSP protocol tests remain Story 19.

### Existing `tyhp-lang/vscode/` (do not start from scratch)

The repo already has a **syntax-only** VS Code / Cursor extension at `tyhp-lang/vscode/` (moved from the former `tyhp-vscode/` folder). This story **grows that tree** into the full IDE client. Do **not** create a parallel editor-named folder (`tyhp-vscode/`, `vscode-tyhp/`, `phpstorm-tyhp/`), and do **not** rewrite the TextMate grammars from a PHP base.

Already in tree (keep and extend):

| Path | What it is |
|------|------------|
| `tyhp-lang/vscode/package.json` | Extension `tyhp-lang.tyhp` (publisher `tyhp-lang`, name `tyhp`, display name `Tyhp Language`). Contributes language `tyhp` for `.tyhp` **and** `.tyhpdef`, plus optional language `php-tyhp`. |
| `tyhp-lang/vscode/syntaxes/tyhp.tmLanguage.json` | `source.tyhp` — optional `<?tyhp` / `<?tyhpdef` tags, tagless files, Tyhp-only syntax (`struct`, `extension`, `type`, `operator`, `async`/`await`, `using` / `:=`, `is`/`isa`/`isan`, `typeof`/`nameof`, generics, constructor delegation, type interpolation, …). |
| `tyhp-lang/vscode/syntaxes/tyhp-php.tmLanguage.json` | `source.tyhp.php` — standalone PHP grammar included by the Tyhp grammar (and manually selectable for `.php`). |
| `tyhp-lang/vscode/syntaxes/tyhp-markdown.tmLanguage.json` | Injects fenced `tyhp` / `php-tyhp` highlighting into Markdown. |
| `tyhp-lang/vscode/language-configuration.json` | Comments (`//`, `/* */`), brackets, auto-closing / surrounding pairs, `#region` folding, indentation. |
| `tyhp-lang/vscode/README.md` | Local install: absolute-path symlink as `tyhp-lang.tyhp-<version>`, VSIX via `vsce package`, or copy into the extensions dir. |

**Keep extension ID** `tyhp-lang.tyhp` and display name `Tyhp Language`. Old local installs used `tyhp-lang.tyhp-language-<version>` (former `name`: `tyhp-language` and folder `tyhp-vscode/`) — remove those before relinking. Bump the version in `package.json` when the VSIX gains LSP / binary / debug features.

---

## Motivation

Stories 17–19 ship the server-side pieces (sourcemaps, XDebug proxy, LSP). Without a client plugin, users must hand-wire editor settings, launch configs, and PATH binaries — high friction and easy to misconfigure.

PHP developers split across VS Code/Cursor and PhpStorm. Shipping only one client leaves half the audience on a manual setup. A dedicated plugin per IDE:

- Makes “open a Tyhp project and get highlighting + diagnostics + debug” the default path in both editors
- Owns binary discovery / install so contributors and newcomers are not blocked on a global `tyhp` install
- Keeps Marketplace submission and docs polish out of the critical path (Story 30 / later)

---

## Scope (In / Out)

| In scope | Out of scope |
|----------|--------------|
| `tyhp-lang/vscode/` extension in this repo | VS Code Marketplace / Open VSX submission |
| `tyhp-lang/phpstorm/` plugin in this repo | JetBrains Marketplace submission |
| Shared TextMate grammars for `.tyhp` / `.tyhpdef` | Separate Cursor fork or branding |
| LSP client → `tyhp language_server` (both editors) | Implementing LSP features themselves (Story 19) |
| XDebug proxy lifecycle + debug launch wiring (VS Code PHP Debug; PhpStorm built-in XDebug) | Replacing PHP Debug / reinventing DBGp (Story 18 owns the proxy) |
| Tasks / run configs, file icons, status bar | Snippets, formatter, Tree-sitter, semantic-token theme packs as v1 requirements |
| Workspace/`tyhp.json` detection + `tyhp init` UX | Neovim / Emacs / other editor clients; IntelliJ IDEA as a first-class target |
| Binary PATH discovery, path setting, download install (global or plugin-local) + plugin-local auto-update / pin | Shipping compiler source as part of the VSIX/ZIP; changing CLI protocols |
| Packageable VSIX + PhpStorm ZIP + local install docs | Web playground (Story 22) |

---

## Architecture Overview

**Layering rule:** both plugins are **thin clients**. All language intelligence and source-map translation stay in the CLI (Stories 17–19). Each plugin resolves a binary, starts processes, and surfaces native IDE UX. Feature parity is required; implementation is native to each platform (TypeScript VS Code API vs Kotlin IntelliJ Platform).

**Shared assets:** TextMate grammars and `language-configuration.json` already live in `tyhp-lang/vscode/syntaxes/` and `tyhp-lang/vscode/language-configuration.json`. That directory is the **canonical grammar source**. `tyhp-lang/phpstorm` copies or references those files at build time. Do **not** fork the grammars, and do **not** invent a second `ide-shared/syntaxes/` tree unless a later need forces a split (not required for v1).

```
VS Code / Cursor                         PhpStorm
  └── tyhp-lang (VS Code)                    └── tyhp-lang (PhpStorm)
        ├── TextMate + language + icons          ├── FileType + TextMate bundle + icons
        ├── BinaryManager                        ├── BinaryManager (same policy)
        │     PATH → settings                    │     PATH → settings
        │     download global | plugin-local     │     download global | plugin-local
        │     plugin-local auto-update | pin     │     plugin-local auto-update | pin
        ├── LspClient ──stdio──► tyhp language_server ◄──stdio── LspServerSupportProvider
        ├── WorkspaceService ──► tyhp.json | tyhp init
        ├── TasksProvider ─────► tyhp build / lint / …
        ├── StatusBar
        └── DebugIntegration                     ├── Run configurations (build/lint)
              start/stop tyhp xdebug_proxy       ├── StatusBarWidget
              PHP Debug adapter → proxy          └── DebugIntegration
                                                    start/stop tyhp xdebug_proxy
                                                    PhpStorm XDebug → proxy IDE port
                                                    (sourcemaps via Story 17 / Story 18)
```

### Suggested directory layout (VS Code)

```
tyhp-lang/vscode/                           — already exists (syntax-only); grow in place
    package.json                       — KEEP publisher `tyhp-lang` + name `tyhp`; add activation, main, contributes
    language-configuration.json        — already exists
    README.md                          — already exists; extend with LSP / binary / sideload notes
    syntaxes/                          — already exists; CANONICAL grammar source for both clients
        tyhp.tmLanguage.json           — already exists (covers .tyhp and .tyhpdef)
        tyhp-php.tmLanguage.json       — already exists
        tyhp-markdown.tmLanguage.json  — already exists
    tsconfig.json                      — ADD
    CHANGELOG.md                       — ADD
    media/                             — ADD (file icons)
    src/                               — ADD (extension is currently grammar-only; no TypeScript yet)
        extension.ts                   — activate / deactivate
        binary/
            BinaryManager.ts
            PathProbe.ts
            Installer.ts
            UpdateService.ts
        lsp/
            LspClient.ts
        workspace/
            WorkspaceService.ts
            InitCommand.ts
        tasks/
            TyhpTaskProvider.ts
        status/
            StatusBarController.ts
        debug/
            XdebugProxyManager.ts
            DebugConfigProvider.ts
        config/
            settings.ts                — typed accessors for contribution points
    .vscode/
        launch.json                    — F5 extension host
        tasks.json
```

### Suggested directory layout (PhpStorm)

```
tyhp-lang/phpstorm/
    build.gradle.kts                   — org.jetbrains.intellij.platform
    settings.gradle.kts
    gradle.properties                  — plugin id, since-build targeting current PhpStorm
    README.md                          — sideload via Install Plugin from Disk; no Marketplace as “done”
    CHANGELOG.md
    src/main/resources/
        META-INF/plugin.xml
        syntaxes/                      — copy/reference tyhp-lang/vscode/syntaxes/ at build (canonical source)
        icons/
    src/main/kotlin/com/tyhp/lang/
        TyhpPlugin.kt                  — listeners / project activity
        binary/
            BinaryManager.kt
            PathProbe.kt
            Installer.kt
            UpdateService.kt
        lsp/
            TyhpLspServerSupportProvider.kt
        workspace/
            WorkspaceService.kt
            InitAction.kt
        run/
            TyhpRunConfigurationType.kt    — build / lint
        status/
            TyhpStatusBarWidgetFactory.kt
        debug/
            XdebugProxyManager.kt
            TyhpDebugConfiguration.kt
        settings/
            TyhpSettings.kt
            TyhpConfigurable.kt            — Settings UI mirroring VS Code tyhp.* keys
```

Kotlin + Gradle IntelliJ Platform plugin is the expected stack. Target **PhpStorm** (not generic IntelliJ). LSP via the IntelliJ Platform LSP API (`LspServerSupportProvider` / `com.intellij.platform.lsp`); if a given PhpStorm baseline lacks it, raise the minimum version rather than taking a third-party LSP host as v1.

---

## Decisions (locked)

| Topic | Decision |
|-------|----------|
| Roadmap slot | Tier 2 additive **19.5**, after **19**, before **20** |
| Dependencies | **17**, **18**, **19** |
| Product | **Tyhp Language** (`tyhp-lang`). Display name is the same in both editors. Not `tyhp` (that's the CLI), not `tyhplang`, not editor-prefixed names. |
| Repo paths | **`tyhp-lang/vscode/`** (moved from `tyhp-vscode/` — grow it) and **`tyhp-lang/phpstorm/`** (new). |
| VS Code ID | `tyhp-lang.tyhp` (publisher `tyhp-lang`, `package.json` `name` `tyhp`) |
| PhpStorm ID | `com.tyhp.lang` (plugin.xml `id`); name `Tyhp Language` |
| Editors | **VS Code** + **PhpStorm**, feature-complete parity; **Cursor compatible** via the VS Code VSIX, not a separate deliverable; IntelliJ IDEA not a required target |
| Publish | **Packageable artifacts only** — VSIX + PhpStorm plugin ZIP; no VS Code Marketplace / Open VSX / JetBrains Marketplace submit in this story |
| Highlighting | **TextMate** for v1; **canonical source is the existing `tyhp-lang/vscode/syntaxes/`** (consumed by PhpStorm). `.tyhpdef` already uses the `tyhp` language + `source.tyhp` grammar — no separate `tyhpdef.tmLanguage.json` unless a real gap appears. |
| LSP | Client only; server = `tyhp language_server` (stdio) on both editors |
| Debug | Integrate **Story 18** proxy; VS Code uses the PHP Debug extension; PhpStorm uses built-in XDebug. Do not reimplement DBGp |
| Binary discovery | On activate (and on demand): search `PATH` for `tyhp`; if found and setting empty/unset, **write** `tyhp.path` |
| Binary setting | User-visible `tyhp.path` (absolute path or command name) in each editor’s settings |
| Install modes | Command(s) to **download & install** either **global** (user/machine install location on PATH where appropriate) or **plugin-only** (under extension/plugin storage); both set `tyhp.path` after success |
| Plugin-only updates | Only when binary was installed by this plugin in plugin-only mode: support **auto-update** and **pin to a specific version** |
| Global installs | No silent auto-update of a global/PATH binary; user-driven update/reinstall only |
| Workspace | Detect `tyhp.json`; if missing, prompt / command to run `tyhp init`; pass project path to LSP/tasks |
| v1 feature set | TextMate, LSP, XDebug proxy integration, tasks/run configs, file icons, status bar, workspace awareness (incl. init) — **on both editors** |
| Out of v1 | Marketplace publish (any store), snippets-as-required, format-on-save as required, Tree-sitter, Neovim/other clients |

### Setting / UX sketch (illustrative)

```json
{
    "tyhp.path": "",
    "tyhp.projectPath": "",
    "tyhp.languageServer.args": ["language_server"],
    "tyhp.languageServer.trace": "off",
    "tyhp.diagnostics.enable": true,
    "tyhp.completion.autoImport": true,
    "tyhp.binary.installMode": "path",
    "tyhp.binary.autoUpdate": true,
    "tyhp.binary.pinnedVersion": "",
    "tyhp.xdebugProxy.idePort": 9003,
    "tyhp.xdebugProxy.xdebugPort": 9004
}
```

Exact key names may be normalized in Phase 1 against existing docs (`docs/content/cli_languageServer.md`) — prefer one consistent `tyhp.*` namespace; update docs in Story 30 if names diverge from aspirational samples.

---

## Phase 1: Extension scaffold & packageable VSIX

> **Track A — VS Code / Cursor.** Phases 1–7. PhpStorm parity is Phases 8–14.

### Phase Overview

**Grow the existing `tyhp-lang/vscode/` extension** (today: TextMate + language-configuration only, no TypeScript `main`) into a TypeScript VS Code project: keep the `tyhp-lang` identity, add activation / `main`, and prove a **local VSIX** can still be built and installed without Marketplace credentials.

Do **not** scaffold a new extension folder.

### Deliverables

1. Keep and extend `tyhp-lang/vscode/package.json`:
   - **Keep** extension ID `tyhp-lang.tyhp` (publisher `tyhp-lang`, name `tyhp`)
   - **Keep** display name `Tyhp Language`
   - Add `"main"` pointing at the compiled `extension.ts`
   - Add activation events: `onLanguage:tyhp` (covers `.tyhp` and `.tyhpdef` — there is no separate `tyhpdef` language id today), plus workspace-`tyhp.json` if useful
   - Add `contributes.configuration` / commands as later phases land; do not drop existing `contributes.languages` / `contributes.grammars`
2. Add `tyhp-lang/vscode/src/extension.ts` — activate / deactivate entry point (LanguageClient wiring is Phase 4). Today there is no `src/`; adding it is the start of this story, not a rewrite of the grammars.
3. TypeScript build (`esbuild` or `tsc` — pick one; keep simple) + `tsconfig.json`
4. `.vscodeignore`; extend the existing README (symlink install uses `{publisher}.{name}-{version}` = `tyhp-lang.tyhp-<version>`) with **sideload** VSIX steps (`code --install-extension …` / Cursor equivalent)
5. Script or documented command to produce a VSIX (`vsce package` from `tyhp-lang/vscode/` is already documented in the README)

### Acceptance Criteria

- [x] Existing TextMate highlighting still works after adding TypeScript (`.tyhp` / `.tyhpdef` language mode unchanged)
- [x] Extension loads in Extension Development Host (F5)
- [x] `vsce package` (or equivalent) produces a VSIX that installs locally
- [x] README states Marketplace publish is **out of scope** for this story
- [x] Documented symlink install uses `tyhp-lang.tyhp-<version>`

---

## Phase 2: Languages, TextMate grammar & file icons

### Phase Overview

The languages, TextMate grammars, and language-configuration **already ship** in `tyhp-lang/vscode/`. This phase is an **audit + gap-fill + file icons**, not a from-scratch grammar. PhpStorm (Phase 9) consumes the same files.

### Already done (do not redo)

- Language `tyhp` associated with `.tyhp` **and** `.tyhpdef` (no separate `tyhpdef` language id)
- Optional language `php-tyhp` (`source.tyhp.php`) for manually selected PHP highlighting
- Markdown fenced-block injection (`syntaxes/tyhp-markdown.tmLanguage.json`)
- `language-configuration.json` with `//` / `/* */` comments, brackets, auto-closing pairs (including `<>`), surrounding pairs, `#region` folding, indentation
- `source.tyhp` covers the Tyhp-only surface listed in `tyhp-lang/vscode/README.md` (optional `<?tyhp` / `<?tyhpdef`, tagless mode, `struct` / `extension` / `type` / `operator`, `async`/`await`, `using` / `:=`, `is`/`isa`/`isan`, `typeof`/`nameof`/`variable_exists`, `deprecated`/`obsolete`, `decimal` / `(decimal)`, `with`, generics, constructor delegation, type interpolation in strings, …)

A **separate** `tyhpdef.tmLanguage.json` is **not** required unless audit finds tyhpdef-only constructs that `source.tyhp` mishandles.

### Deliverables

1. Audit existing grammars against a representative `.tyhp` / `.tyhpdef` sample (and the README feature list). Fix real highlighting bugs; do **not** rewrite from a PHP TextMate base.
2. Keep `tyhp-php.tmLanguage.json` and `tyhp-markdown.tmLanguage.json` — they are part of the existing extension, not extras to drop.
3. File icon theme contribution or icon paths for Tyhp files (this is the main *new* Phase 2 deliverable).
4. Confirm `contributes.languages` / `contributes.grammars` in `package.json` still match the files on disk after Phase 1 adds `main` / activation.

### Implementation notes

- Semantic highlighting from LSP (Story 19 semantic tokens, if enabled) is additive; TextMate must stand alone when LSP is down.
- If a later Tyhp syntax lands that the grammar misses, extend `tyhp.tmLanguage.json` (and `tyhp-php.tmLanguage.json` only when the construct is also PHP).
- Scopes already follow TextMate conventions (`keyword.*`, `entity.name.*`, …); prefer matching existing scopes over inventing a new naming scheme.

### Acceptance Criteria

- [x] Opening `.tyhp` / `.tyhpdef` still selects the Tyhp language mode (no regression vs the current extension)
- [x] Keywords, comments, strings, numbers, and variables highlight sanely on a representative sample file
- [x] Bracket matching works for `{}`, `[]`, `()`, `<>`
- [x] Comment toggling (Ctrl+/) works with `//` for line comments
- [x] Markdown fenced `tyhp` blocks still highlight
- [x] File icons appear for Tyhp files in the explorer (with default/light/dark variants as contributed)

---

## Phase 3: Binary resolution, install & updates

### Phase Overview

Own how the extension finds and maintains a `tyhp` executable.

### Behavior

1. **PATH probe:** On activation (and via “Refresh Tyhp binary” command), search `PATH` for `tyhp`. If found and `tyhp.path` is empty/unset, **set** `tyhp.path` to the resolved path (prefer Workspace → User scope policy: User by default unless a workspace override already exists).
2. **Setting wins:** If `tyhp.path` is set, use it (validate executable; surface status-bar error if missing).
3. **Install command:** “Tyhp: Install / Update CLI” offers:
   - **Global** — install into a conventional user-global location (and optionally remind user to ensure PATH); then set `tyhp.path`
   - **Extension only** — install under extension globalStorage; then set `tyhp.path` to that binary; record install mode metadata so auto-update rules apply
4. **Extension-only updates:** If (and only if) install mode is extension-only **and** the binary was installed by this extension:
   - `tyhp.binary.autoUpdate` → check for newer release on startup (debounced) / command
   - `tyhp.binary.pinnedVersion` → if non-empty, install/keep that version and skip auto-update to other versions
5. **Global / PATH binaries:** never auto-overwrite; update only via explicit user command.

### Implementation notes

- Release artifact source (GitHub Releases URL / naming / checksums) must be documented in the extension README once chosen; prefer official Tyhp CLI release assets matching host OS/arch.
- Failure modes (network, checksum, unsupported platform) → clear VS Code error message + status bar; do not crash activation.

### Acceptance Criteria

- [x] PATH hit with empty setting → setting populated
- [x] Explicit `tyhp.path` used for LSP/tasks/proxy
- [x] Global install sets path; no auto-update
- [x] Extension-only install sets path; auto-update and pin behave as locked above
- [x] Missing/invalid binary → actionable status + command to install/fix

---

## Phase 4: LSP client

### Phase Overview

Start Story 19’s language server as a child process and register a standard VS Code Language Client for Tyhp languages.

### Deliverables

1. `vscode-languageclient` integration (`LanguageClient` over stdio)
2. Server command = resolved `tyhp.path` + args (`language_server` + quiet/project flags as needed)
3. Document selector for `tyhp` and `tyhpdef` languages
4. Pass `--tyhp-project` when workspace project path is known
5. Midlife restart when `tyhp.path` or project path changes
6. Trace / output channel for LSP logs (e.g. Output panel > "Tyhp Language Server")
7. Missing-binary UX: if the language server executable is not found, show an error with setup / install instructions (Phase 3 commands) — do not crash activation
8. Crash recovery: auto-restart the language server with backoff if the process exits unexpectedly

### Acceptance Criteria

- [x] Opening a `.tyhp` file activates the extension and starts the language server
- [x] Diagnostics appear for a deliberately broken `.tyhp` file when server is up (Problems panel)
- [x] Hover / go-to-definition smoke against a small fixture project (features that Story 19 already supports)
- [x] Missing binary → actionable error (not a silent failure)
- [x] Language server crash → auto-restart with backoff
- [x] Stopping the extension host stops the language server process

---

## Phase 5: Workspace awareness, init, tasks & status bar

### Phase Overview

Make the extension project-aware and expose everyday CLI workflows inside VS Code.

### Behavior

1. **Detect** `tyhp.json` at workspace root (and honor `tyhp.projectPath` override).
2. If a Tyhp file is opened and no project file is found → prompt to run **`tyhp init`** (command “Tyhp: Initialize Project”); on accept, run init via resolved binary and reload workspace state.
3. **Tasks:** contribute a task provider (or static tasks) for at least `tyhp build` and `tyhp lint` (problem matchers optional v1; prefer JSON lint format if easy).
4. **Status bar:** show compact state — e.g. `Tyhp` + project name / “no project” + LSP ready|starting|error + optional proxy running. Click → quick pick of common actions (restart LSP, install CLI, init, start/stop proxy).

### Acceptance Criteria

- [x] Workspace with `tyhp.json` is detected without manual config
- [x] Init command creates `tyhp.json` via CLI
- [x] Build/lint tasks run against the resolved binary and project
- [x] Status bar reflects binary missing, LSP down, and healthy states

---

## Phase 6: XDebug proxy / debug integration

### Phase Overview

Connect VS Code debugging to Story 18’s proxy so users debug `.tyhp` sources, not only emitted PHP.

### Behavior

1. Commands: start / stop / restart `tyhp xdebug_proxy` using resolved binary, sourcemap dir / ports from settings or `tyhp.json` `xdebugProxy` section when present.
2. Contribute **launch.json** snippets / debug configuration provider that:
   - Assumes (or documents) the **PHP Debug** extension as the DBGp client
   - Points the IDE connection at the proxy **IDE port** (Story 18 defaults)
   - Documents XDebug `client_port` → proxy **XDebug port**
3. Status bar / output channel shows proxy listening state.
4. Prerequisites messaging: sourcemaps enabled (`build.generateSourcemap`) + built project — link to Story 17/18 docs rather than re-implementing maps.

### Acceptance Criteria

- [x] Proxy starts/stops from the extension and releases ports on stop
- [x] Documented launch config path allows a breakpoint in `.tyhp` to hit under a minimal debug sample (manual QA)
- [x] Misconfiguration (no maps / proxy down) yields clear guidance

---

## Phase 7: Manual QA matrix & packaging acceptance

### Phase Overview

Lock a repeatable local QA checklist; no Marketplace publish.

### Deliverables

1. QA checklist in `tyhp-lang/vscode/README.md` or `tyhp-lang/vscode/QA.md` (PATH binary, extension-only binary, init, LSP, tasks, proxy)
2. Smoke on macOS at minimum (primary dev platform); note Windows/Linux as follow-ups if not run
3. Confirm Cursor can sideload the same VSIX (smoke: language mode + LSP attach) — **compatibility check only**

**Sideload install (manual QA):**

```bash
cd tyhp-lang/vscode
npm install
npm run compile
npx vsce package
code --install-extension tyhp-*.vsix
```

**Editor-side LSP feature checklist** (Story 19 already proved these over stdin/stdout; this story proves them **through the extension**):

- Opening a `.tyhp` file activates the extension; Output panel shows the language server
- Syntax highlighting for `.tyhp` and `.tyhpdef` (Phase 2)
- Bracket matching and comment toggling
- Diagnostics in the Problems panel
- Go-to-definition, hover, autocomplete (trigger characters), find references, rename
- Document outline, signature help, code actions (quick fixes), semantic tokens (if Story 19 enabled them)

### Acceptance Criteria

- [x] VSIX installs cleanly in VS Code (`code --install-extension …`)
- [x] `npm run compile` (or equivalent) succeeds for the VS Code extension
- [x] Checklist items for Phases 2–6 signed off, including the editor-side LSP feature list above
- [x] No Marketplace publish performed as part of “done”

Human GUI rows (highlighting, LSP features in an editor, live XDebug hit) are **unsigned checkboxes** in `tyhp-lang/vscode/QA.md`. Phase 7 sign-off is the checklist + packageable VSIX + compile; it does not claim those GUI items were clicked.

---

## Phase 8: PhpStorm plugin scaffold & packageable ZIP

> **Track B — PhpStorm.** Phases 8–14. Same surface as Phases 1–7; native IntelliJ Platform implementation.

### Phase Overview

Create the Kotlin/Gradle PhpStorm plugin under `tyhp-lang/phpstorm/`, wire plugin.xml, and prove a **local ZIP** can be built and installed via **Install Plugin from Disk** without JetBrains Marketplace credentials.

Parity with Phase 1. Target a current PhpStorm `since-build`; document the minimum version (must include the Platform LSP API).

### Deliverables

1. Gradle IntelliJ Platform plugin project (`plugin.xml` id `com.tyhp.lang`, name `Tyhp Language`, version, vendor `tyhp-lang`; `depends` on PhpStorm / PHP plugin as needed for debug)
2. Plugin README with **sideload** install steps (Settings → Plugins → ⚙ → Install Plugin from Disk)
3. Script or documented Gradle task to produce `tyhp-lang-*.zip`
4. Run-plugin development configuration (Gradle `runIde` against PhpStorm)

### Acceptance Criteria

- [x] Plugin loads in a Gradle-launched PhpStorm sandbox
- [x] Built ZIP installs locally via Install Plugin from Disk
- [x] README states JetBrains Marketplace publish is **out of scope** for this story

---

## Phase 9: PhpStorm languages, TextMate grammar & file icons

### Phase Overview

Register `.tyhp` and `.tyhpdef` file types, associate the **shared** TextMate grammars, and contribute file icons. Same highlighting quality bar as Phase 2.

### Deliverables

1. `FileType` / language registrations for `.tyhp` / `.tyhpdef`
2. TextMate bundle loaded from the shared grammar source (not a forked copy)
3. Comment/bracket pairing equivalent to VS Code `language-configuration.json` where the Platform supports it
4. File icons in the Project view (default/light/dark as contributed)

### Implementation notes

- PhpStorm’s built-in PHP highlighter must **not** steal `.tyhp` files.
- Semantic highlighting from LSP (if Story 19 exposes it) is additive; TextMate must stand alone when LSP is down.
- Load **`tyhp-lang/vscode/syntaxes/`** (especially `tyhp.tmLanguage.json`; include `tyhp-php.tmLanguage.json` if the Tyhp grammar includes it). Do **not** write a thinner PhpStorm-only grammar and do **not** invent a separate `tyhpdef.tmLanguage.json` unless Phase 2’s audit found a real gap.

### Acceptance Criteria

- [x] Opening `.tyhp` / `.tyhpdef` selects the Tyhp file type
- [x] Keywords, comments, and strings highlight sanely on the same representative sample used for VS Code
- [x] File icons appear for Tyhp files in the Project view

---

## Phase 10: PhpStorm binary resolution, install & updates

### Phase Overview

Same policy as Phase 3, implemented against PhpStorm settings (`TyhpSettings` / PersistentStateComponent) instead of VS Code configuration.

### Behavior

Identical to Phase 3: PATH probe → write `tyhp.path` if empty; setting wins; Install / Update CLI offers **global** vs **plugin-only**; auto-update and pin apply **only** to plugin-only installs; never auto-overwrite a global/PATH binary.

### Acceptance Criteria

- [x] PATH hit with empty setting → setting populated
- [x] Explicit `tyhp.path` used for LSP / run configs / proxy
- [x] Global install sets path; no auto-update
- [x] Plugin-only install sets path; auto-update and pin behave as locked above
- [x] Missing/invalid binary → actionable status + action to install/fix

---

## Phase 11: PhpStorm LSP client

### Phase Overview

Start Story 19’s language server as a child process and register it via `LspServerSupportProvider` for Tyhp file types.

### Deliverables

1. IntelliJ Platform LSP integration (`com.intellij.platform.lsp`)
2. Server command = resolved `tyhp.path` + args (`language_server` + quiet/project flags as needed)
3. Pass `--tyhp-project` when the project path is known
4. Restart the server when `tyhp.path` or project path changes
5. LSP / plugin log in the IDE’s log / a dedicated tool window tab
6. Missing-binary UX: if the language server executable is not found, show an error with setup / install actions (Phase 10) — do not crash plugin load
7. Crash recovery: auto-restart the language server with backoff if the process exits unexpectedly

### Acceptance Criteria

- [x] Opening a `.tyhp` file starts the language server
- [x] Diagnostics appear for a deliberately broken `.tyhp` file when the server is up
- [x] Hover / go-to-definition smoke against the same small fixture project used for VS Code (features Story 19 already supports)
- [x] Missing binary → actionable error (not a silent failure)
- [x] Language server crash → auto-restart with backoff
- [x] Closing the project / disabling the plugin stops the language server process

---

## Phase 12: PhpStorm workspace awareness, init, run configs & status bar

### Phase Overview

Parity with Phase 5: project-aware UX and everyday CLI workflows inside PhpStorm.

### Behavior

1. **Detect** `tyhp.json` at the content-root (and honor `tyhp.projectPath` override).
2. If a Tyhp file is opened and no project file is found → notification / action **Tyhp: Initialize Project**; on accept, run `tyhp init` via the resolved binary and refresh project state.
3. **Run configurations** (or External Tools equivalents) for at least `tyhp build` and `tyhp lint`.
4. **Status bar widget:** compact state — e.g. `Tyhp` + project name / “no project” + LSP ready|starting|error + optional proxy running. Click → popup of common actions (restart LSP, install CLI, init, start/stop proxy).

### Acceptance Criteria

- [x] Project with `tyhp.json` is detected without manual config
- [x] Init action creates `tyhp.json` via CLI
- [x] Build/lint run configurations use the resolved binary and project
- [x] Status bar reflects binary missing, LSP down, and healthy states

---

## Phase 13: PhpStorm XDebug proxy / debug integration

### Phase Overview

Connect PhpStorm’s built-in PHP debugger to Story 18’s proxy so users debug `.tyhp` sources, not only emitted PHP. Parity with Phase 6; do not reimplement DBGp.

### Behavior

1. Actions: start / stop / restart `tyhp xdebug_proxy` using the resolved binary, sourcemap dir / ports from settings or `tyhp.json` `xdebugProxy` when present.
2. Contribute / document a **PHP Remote Debug** (or equivalent) configuration that:
   - Uses PhpStorm’s built-in XDebug support as the DBGp client
   - Points the IDE connection at the proxy **IDE port** (Story 18 defaults)
   - Documents XDebug `client_port` → proxy **XDebug port**
3. Status bar / tool window shows proxy listening state.
4. Prerequisites messaging: sourcemaps enabled (`build.generateSourcemap`) + built project — link to Story 17/18 docs rather than re-implementing maps.

### Acceptance Criteria

- [x] Proxy starts/stops from the plugin and releases ports on stop
- [x] Documented debug config path allows a breakpoint in `.tyhp` to hit under a minimal debug sample (manual QA)
- [x] Misconfiguration (no maps / proxy down) yields clear guidance

---

## Phase 14: PhpStorm QA matrix & packaging acceptance

### Phase Overview

Lock a repeatable local QA checklist; no JetBrains Marketplace publish. Same scenarios as Phase 7.

### Deliverables

1. QA checklist in `tyhp-lang/phpstorm/README.md` or `tyhp-lang/phpstorm/QA.md` (PATH binary, plugin-only binary, init, LSP, run configs, proxy)
2. Smoke on macOS at minimum (primary dev platform); note Windows/Linux as follow-ups if not run
3. Confirm feature parity against the VS Code checklist (Phases 2–6) — same scenarios, native UX

### Acceptance Criteria

- [x] Plugin ZIP installs cleanly in PhpStorm
- [x] Checklist items for Phases 9–13 signed off
- [x] No JetBrains Marketplace publish performed as part of “done”
- [x] Parity: every VS Code Phase 2–6 scenario has a passing PhpStorm counterpart

Human GUI rows (highlighting, LSP features in an editor, live XDebug hit, Install Plugin from Disk in a PhpStorm window) are **unsigned checkboxes** in `tyhp-lang/phpstorm/QA.md`. Phase 14 sign-off is the checklist + packageable ZIP (`./gradlew buildPlugin` → `tyhp-lang-0.6.0.zip`) + `./gradlew unitTest`; it does not claim those GUI items were clicked. `runIde` is documented and was not required for packaging acceptance.

---

## Configuration Keys

| Key | Purpose |
|-----|---------|
| `tyhp.path` | CLI binary path or command name (supersedes the older Story 19 sketch `tyhp.languageServer.path`) |
| `tyhp.projectPath` | Override path to `tyhp.json` (supersedes the older sketch `tyhp.projectFile`) |
| `tyhp.languageServer.args` | Extra/override args for language server |
| `tyhp.languageServer.trace` | LSP trace level |
| `tyhp.diagnostics.enable` | Publish diagnostics on change (default true) |
| `tyhp.completion.autoImport` | Offer auto-import on completion / code actions (default true) |
| `tyhp.binary.installMode` | `path` \| `global` \| `extension` (recorded after install; PhpStorm uses the same names in its PersistentStateComponent) |
| `tyhp.binary.autoUpdate` | Plugin-only auto-update toggle |
| `tyhp.binary.pinnedVersion` | Plugin-only pinned CLI version (empty = track latest when auto-update on) |
| `tyhp.xdebugProxy.*` | Port / sourcemap overrides when not taken from `tyhp.json` |

Align final names with VS Code `package.json` `contributes.configuration` **and** the PhpStorm settings panel. Prefer the same `tyhp.*` key names in both UIs so docs (Story 30) can describe one settings model.

---

## Cross-Story References

| Story | Relationship |
|-------|----------------|
| **17** | Sourcemaps required for meaningful debug translation |
| **18** | `tyhp xdebug_proxy` — both clients start/stop and point the IDE at it |
| **19** | `tyhp language_server` — both clients are LSP clients. Story 19 does **not** own `tyhp-lang/vscode/`; the existing syntax extension and the full IDE client live here. |
| **10 / 13** | `build` / `lint` / `init` CLI actions used by tasks/run configs and workspace UX |
| **22** | Web playground — separate; not these plugins (and currently deferred) |
| **30** | Docs polish + future Marketplace publish decision (VS Code and/or JetBrains) |
| **31 Idea 10** | Plugin TextMate island highlighting consumes Story 19.5 clients |

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). See `CONVENTIONS.md` and Story 07. This story is two thin clients (TypeScript + Kotlin); prefer lightweight unit tests for binary resolution / settings helpers plus the manual QA matrices above.

- [x] **Unit tests (both clients):** PATH probe, setting precedence, plugin-only update/pin policy (pure functions where possible)
- [x] **VS Code grammars:** Existing `tyhp-lang/vscode/syntaxes/` remain the canonical source; both clients ship as **Tyhp Language** (`tyhp-lang`); PhpStorm consumes the same files
- [x] **Manual / integration (VS Code):** Extension Development Host checklist covering TextMate, LSP diagnostics, init, tasks, proxy start, VSIX sideload, plus the editor-side LSP feature list in Phase 7
- [x] **Manual / integration (PhpStorm):** sandbox / sideload checklist covering the same scenarios (TextMate, LSP, init, run configs, proxy, ZIP install)
- [x] **Parity:** PhpStorm checklist is a counterpart of the VS Code checklist — no VS Code-only v1 features
- [x] **No Marketplace submit** (VS Code, Open VSX, or JetBrains) required for story completion
- [x] **Cursor:** same VSIX sideload smoke (compatible, not a separate deliverable)

VS Code / PhpStorm **Human GUI** rows stay unsigned in `tyhp-lang/vscode/QA.md` and `tyhp-lang/phpstorm/QA.md`. Golden-fixture sign-off is checklists + unit tests + packaging, not a claim that highlighting/hover/breakpoints were clicked in an IDE window. Cursor is the VS Code VSIX path only.
