# Implementation Plan: Story 19.5 — VS Code Extension (`vscode-tyhp`)

> **Roadmap position:** Story 19.5 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story 19, before Story 20)
> **Direct dependencies (new numbering):** 17, 18, 19
> **New story:** VS Code (and Cursor-compatible) client that packages TextMate syntax highlighting, LSP client wiring to `tyhp language_server`, XDebug-proxy debug integration, tasks, file icons, status bar, and workspace/`tyhp.json` awareness (including `tyhp init`) — the IDE-facing half of Stories 17–19.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-08-04
> **Prerequisites:** Story 17 (sourcemaps), Story 18 (`tyhp xdebug_proxy`), Story 19 (`tyhp language_server`) complete enough to launch and speak their protocols.
> **Consumers:** Story 30 (docs/polish — marketplace publish is **not** this story); end-user DX for Tyhp editing/debugging in VS Code / Cursor.

---

## Table of Contents

- [Summary](#summary)
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
- [Configuration Keys](#configuration-keys)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

This story delivers a **first-party VS Code extension** living at `vscode-tyhp/` in the monorepo. It is the **client** for the Tyhp toolchain already provided by Stories 17–19:

1. **TextMate syntax highlighting** for `.tyhp` and `.tyhpdef` (lexical highlighting; semantic tokens remain an LSP concern from Story 19 when enabled).
2. **LSP client** that launches `tyhp language_server` over stdin/stdout using the resolved CLI binary.
3. **XDebug proxy integration** — start/stop `tyhp xdebug_proxy`, wire VS Code debug launch configs so breakpoints and stacks map through sourcemaps to `.tyhp` sources.
4. **Tasks** for common CLI actions (`build`, `lint`, and related workspace commands as appropriate).
5. **File icons** for Tyhp language files.
6. **Status bar** showing binary / project / LSP (and optionally proxy) health.
7. **Workspace awareness** — detect `tyhp.json`, offer / run `tyhp init` when absent, pass project path into LSP and tasks.

**Distribution for this story:** produce a **packageable VSIX** (and local install instructions). **Do not** publish to the VS Code Marketplace or Open VSX. Cursor is **compatible** via the same VSIX; no separate Cursor product/deliverable.

---

## Motivation

Stories 17–19 ship the server-side pieces (sourcemaps, XDebug proxy, LSP). Without a client extension, users must hand-wire `settings.json`, launch configs, and PATH binaries — high friction and easy to misconfigure.

A dedicated extension:

- Makes “open a Tyhp project and get highlighting + diagnostics + debug” the default path
- Owns binary discovery / install so contributors and newcomers are not blocked on a global `tyhp` install
- Keeps Marketplace submission and docs polish out of the critical path (Story 30 / later)

---

## Scope (In / Out)

| In scope | Out of scope |
|----------|--------------|
| `vscode-tyhp/` extension in this repo | Marketplace / Open VSX submission |
| TextMate grammars for `.tyhp` / `.tyhpdef` | Separate Cursor fork or branding |
| LSP client → `tyhp language_server` | Implementing LSP features themselves (Story 19) |
| XDebug proxy lifecycle + debug launch wiring | Replacing PHP Debug / reinventing DBGp (Story 18 owns the proxy) |
| Tasks, file icons, status bar | Snippets, formatter, Tree-sitter, semantic-token theme packs as v1 requirements |
| Workspace/`tyhp.json` detection + `tyhp init` UX | JetBrains / Neovim / other editor clients |
| Binary PATH discovery, path setting, download install (global or extension-local) + extension-local auto-update / pin | Shipping compiler source as part of the VSIX; changing CLI protocols |
| Packageable VSIX + local install docs | Web playground (Story 22) |

---

## Architecture Overview

```
VS Code / Cursor
  └── vscode-tyhp extension
        ├── TextMate grammars + language + icon contributions
        ├── BinaryManager
        │     PATH probe → settings
        │     user setting tyhp.path
        │     download → global OR extension-local store
        │     (extension-local only) auto-update | pin version
        ├── LspClient ──stdio──► tyhp language_server
        ├── WorkspaceService ──► tyhp.json | tyhp init
        ├── TasksProvider ─────► tyhp build / lint / …
        ├── StatusBar
        └── DebugIntegration
              start/stop tyhp xdebug_proxy
              launch.json → PHP Debug adapter → proxy IDE port → XDebug
                    (sourcemaps via Story 17 / Story 18 translation)
```

**Layering rule:** the extension is a **thin client**. All language intelligence and source-map translation stay in the CLI (Stories 17–19). The extension resolves a binary, starts processes, and surfaces VS Code UX.

### Suggested directory layout

```
vscode-tyhp/
    package.json
    tsconfig.json
    README.md                          — local install / develop; no marketplace publish steps as “done”
    CHANGELOG.md
    media/                             — icons
    syntaxes/
        tyhp.tmLanguage.json
        tyhpdef.tmLanguage.json
    language-configuration.json
    src/
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

---

## Decisions (locked)

| Topic | Decision |
|-------|----------|
| Roadmap slot | Tier 2 additive **19.5**, after **19**, before **20** |
| Dependencies | **17**, **18**, **19** |
| Repo path | `vscode-tyhp/` in this monorepo (may move later; path is authoritative for this story) |
| Editors | VS Code primary; **Cursor compatible**, not a separate deliverable |
| Publish | **Packageable VSIX only** — no Marketplace / Open VSX submit in this story |
| Highlighting | **TextMate** for v1 (`.tyhp` + `.tyhpdef`) |
| LSP | Client only; server = `tyhp language_server` (stdio) |
| Debug | Integrate **Story 18** proxy + existing PHP Debug extension; do not reimplement DBGp |
| Binary discovery | On activate (and on demand): search `PATH` for `tyhp`; if found and setting empty/unset, **write** `tyhp.path` |
| Binary setting | User-visible `tyhp.path` (absolute path or command name) |
| Install modes | Command(s) to **download & install** either **global** (user/machine install location on PATH where appropriate) or **extension-only** (under extension storage); both set `tyhp.path` after success |
| Extension-only updates | Only when binary was installed by the extension in extension-only mode: support **auto-update** and **pin to a specific version** |
| Global installs | No silent auto-update of a global/PATH binary; user-driven update/reinstall only |
| Workspace | Detect `tyhp.json`; if missing, prompt / command to run `tyhp init`; pass project path to LSP/tasks |
| v1 feature set | TextMate, LSP, XDebug proxy integration, tasks, file icons, status bar, workspace awareness (incl. init) |
| Out of v1 | Marketplace publish, snippets-as-required, format-on-save as required, Tree-sitter |

### Setting / UX sketch (illustrative)

```json
{
    "tyhp.path": "",
    "tyhp.projectPath": "",
    "tyhp.languageServer.args": ["language_server"],
    "tyhp.languageServer.trace": "off",
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

### Phase Overview

Create the TypeScript VS Code extension project under `vscode-tyhp/`, wire activation events, and prove a **local VSIX** can be built and installed without Marketplace credentials.

### Deliverables

1. `vscode-tyhp/package.json` with publisher/name/version, engines, activation on Tyhp languages / workspace containing `tyhp.json`
2. TypeScript build (`esbuild` or `tsc` — pick one; keep simple)
3. `.vscodeignore`, extension README with **sideload** install steps (`code --install-extension …` / Cursor equivalent)
4. Script or documented command to produce `vscode-tyhp-*.vsix`

### Acceptance Criteria

- [ ] Extension loads in Extension Development Host (F5)
- [ ] `vsce package` (or equivalent) produces a VSIX that installs locally
- [ ] README states Marketplace publish is **out of scope** for this story

---

## Phase 2: Languages, TextMate grammar & file icons

### Phase Overview

Register `tyhp` and `tyhpdef` languages, associate file extensions, ship TextMate grammars good enough for day-to-day editing, and contribute file icons.

### Deliverables

1. `contributes.languages` for `.tyhp` / `.tyhpdef` (and aliases as needed)
2. TextMate grammars covering keywords, types, strings, comments, attributes, generics-ish punctuation, PHP-familiar constructs Tyhp shares
3. Language configuration (brackets, comments, auto-closing pairs)
4. File icon theme contribution or icon paths for Tyhp files

### Implementation notes

- Prefer starting from a PHP-aware TextMate base and extending for Tyhp-only syntax (`<?tyhp`, `<?tyhpdef`, Tyhp keywords, etc.) rather than inventing an unrelated grammar.
- Semantic highlighting from LSP (if Story 19 exposes it) is additive; TextMate must stand alone when LSP is down.

### Acceptance Criteria

- [ ] Opening `.tyhp` / `.tyhpdef` selects the Tyhp language mode
- [ ] Keywords, comments, and strings highlight sanely on a representative sample file
- [ ] File icons appear for Tyhp files in the explorer (with default/light/dark variants as contributed)

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

- [ ] PATH hit with empty setting → setting populated
- [ ] Explicit `tyhp.path` used for LSP/tasks/proxy
- [ ] Global install sets path; no auto-update
- [ ] Extension-only install sets path; auto-update and pin behave as locked above
- [ ] Missing/invalid binary → actionable status + command to install/fix

---

## Phase 4: LSP client

### Phase Overview

Start Story 19’s language server as a child process and register a standard VS Code Language Client for Tyhp languages.

### Deliverables

1. `vscode-languageclient` integration
2. Server command = resolved `tyhp.path` + args (`language_server` + quiet/project flags as needed)
3. Pass `--tyhp-project` when workspace project path is known
4. Midlife restart when `tyhp.path` or project path changes
5. Trace / output channel for LSP logs

### Acceptance Criteria

- [ ] Diagnostics appear for a deliberately broken `.tyhp` file when server is up
- [ ] Hover / go-to-definition smoke against a small fixture project (features that Story 19 already supports)
- [ ] Stopping the extension host stops the language server process

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

- [ ] Workspace with `tyhp.json` is detected without manual config
- [ ] Init command creates `tyhp.json` via CLI
- [ ] Build/lint tasks run against the resolved binary and project
- [ ] Status bar reflects binary missing, LSP down, and healthy states

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

- [ ] Proxy starts/stops from the extension and releases ports on stop
- [ ] Documented launch config path allows a breakpoint in `.tyhp` to hit under a minimal debug sample (manual QA)
- [ ] Misconfiguration (no maps / proxy down) yields clear guidance

---

## Phase 7: Manual QA matrix & packaging acceptance

### Phase Overview

Lock a repeatable local QA checklist; no Marketplace publish.

### Deliverables

1. QA checklist in `vscode-tyhp/README.md` or `vscode-tyhp/QA.md` (PATH binary, extension-only binary, init, LSP, tasks, proxy)
2. Smoke on macOS at minimum (primary dev platform); note Windows/Linux as follow-ups if not run
3. Confirm Cursor can sideload the same VSIX (smoke: language mode + LSP attach) — **compatibility check only**

### Acceptance Criteria

- [ ] VSIX installs cleanly in VS Code
- [ ] Checklist items for Phases 2–6 signed off
- [ ] No Marketplace publish performed as part of “done”

---

## Configuration Keys

| Key | Purpose |
|-----|---------|
| `tyhp.path` | CLI binary path or command name |
| `tyhp.projectPath` | Override path to `tyhp.json` |
| `tyhp.languageServer.args` | Extra/override args for language server |
| `tyhp.languageServer.trace` | LSP trace level |
| `tyhp.binary.installMode` | `path` \| `global` \| `extension` (recorded after install) |
| `tyhp.binary.autoUpdate` | Extension-only auto-update toggle |
| `tyhp.binary.pinnedVersion` | Extension-only pinned CLI version (empty = track latest when auto-update on) |
| `tyhp.xdebugProxy.*` | Port / sourcemap overrides when not taken from `tyhp.json` |

Align final names with `package.json` `contributes.configuration` and keep docs samples in sync when Story 30 polishes user docs.

---

## Cross-Story References

| Story | Relationship |
|-------|----------------|
| **17** | Sourcemaps required for meaningful debug translation |
| **18** | `tyhp xdebug_proxy` — extension starts/stops and points the IDE at it |
| **19** | `tyhp language_server` — extension is the LSP client |
| **10 / 13** | `build` / `lint` / `init` CLI actions used by tasks and workspace UX |
| **22** | Web playground — separate; not this extension |
| **30** | Docs polish + future Marketplace publish decision |

---

## Golden Fixtures / Tests (Acceptance)

> Standardized testing-first acceptance criteria (uniform across all stories). See `CONVENTIONS.md` and Story 07. This story is primarily a **TypeScript client**; prefer lightweight unit tests for binary resolution / settings helpers plus the manual QA matrix above.

- [ ] **Unit tests:** PATH probe, setting precedence, extension-only update/pin policy (pure functions where possible)
- [ ] **Manual / integration:** Extension Development Host checklist covering TextMate, LSP diagnostics, init, tasks, proxy start, VSIX sideload
- [ ] **No Marketplace submit** required for story completion
- [ ] **Cursor:** same VSIX sideload smoke (compatible, not a separate deliverable)
