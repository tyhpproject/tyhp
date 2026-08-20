# Changelog

## 0.8.0

- One language server per owned `tyhp.json`, started lazily when a matching `.tyhp` / `.tyhpdef` file is opened and idle-stopped when that project has no open documents
- Project membership uses `include` / `exclude` globs relative to that `tyhp.json` (same rules as `tyhp build`), including `../` overlays such as PHP extension tyhpdefs. Empty `include` owns nothing
- Files that match no project stay TextMate-only (no LSP). Status bar shows the owner folder name or **not in a Tyhp project**
- `tyhp.projectPath` forces a single project and skips scanning others; files outside its include stay TextMate-only
- Init prompt only when the file has no owner and no ancestor `tyhp.json` up to the workspace root

## 0.7.1

- Do not restart `tyhp language_server` in a loop when the CLI is missing, too old (stub `language_server`), or fails to start. One error with Install / Settings / Output; vscode-languageclient toasts are suppressed
- Detect pre-Story-19 CLIs from `tyhp help --subject=language_server` and skip starting the client

## 0.7.0

- Local QA matrix (`QA.md`): sideload, packaging, PATH vs extension-only CLI, init/tasks/proxy, and the editor-side LSP feature list. Marketplace / Open VSX publish is out of scope
- VSIX ships only the esbuild bundle (`out/extension.js`); tsc per-module JS is not packed
- Start / stop / restart `tyhp xdebug_proxy` from the Command Palette and status bar (resolved CLI, ports and sourcemap dir from settings or `tyhp.json` `xdebugProxy`)
- Contribute a PHP Debug launch snippet that listens on the proxy IDE port (default 9003); document XDebug `client_port` → proxy XDebug port (default 9004)
- Status bar shows proxy listening state; Output panel **Tyhp XDebug Proxy**
- Warn when sourcemaps are disabled/missing, the proxy is down, or PHP Debug (`xdebug.php-debug`) is not installed

## 0.6.0

- Detect `tyhp.json` at a workspace folder root (or `tyhp.projectPath`) without extra config
- Command **Tyhp: Initialize Project** runs `tyhp init --yes` via the resolved CLI; opening a Tyhp file with no project prompts to init
- Task provider for **tyhp: build** (`build --quiet`) and **tyhp: lint** (`lint --quiet --format=json`), both passing `--tyhp-project` when a project file is known
- Status bar shows project name / no project, LSP ready|starting|error, and CLI health; click opens Restart LSP / Install CLI / Init
- Missing CLI is reported once (binary manager + status bar); the language client no longer shows a second error dialog

## 0.5.0

- Start `tyhp language_server` over stdio via `vscode-languageclient` (Output panel > **Tyhp Language Server**)
- Document selector is language `tyhp` for `.tyhp` and `.tyhpdef` (there is no `tyhpdef` language id)
- Pass `--tyhp-project` when `tyhp.projectPath` or a workspace-root `tyhp.json` is available
- Restart the server when `tyhp.path`, `tyhp.projectPath`, extra LSP args, or workspace folders change
- Missing CLI shows an actionable error (**Install / Update CLI** / **Refresh Tyhp binary**) without crashing activation
- Unexpected language-server exit auto-restarts with exponential backoff
- Command **Tyhp: Restart Language Server**

## 0.4.0

- Resolve and manage the Tyhp CLI: PATH probe, `tyhp.path`, **Install / Update CLI** (global or extension-only), checksum-verified GitHub Release downloads, and extension-only auto-update / version pin
- Contribute `tyhp.*` settings used by later LSP / tasks / debug phases
- Minimal status bar item for CLI health (missing/invalid binary offers Install)

## 0.3.1

- Contribute file icons for `.tyhp` / `.tyhpdef` (language icons plus the **Tyhp File Icons** theme with default/light/high-contrast variants)
- Match `<>` in bracket pairing
- Highlight `use extension` without swallowing following code; do not treat `extension function` / `extension fn` / `extension operator` as extension type declarations
- Highlight `operator +<Type>` generic targets, `nameof(...)`, `resource` types, and `nameof`/`typeof` inside `"${...}"` interpolations

## 0.3.0

- Add a TypeScript extension host entry point (`activate` / `deactivate`)
- Add local VSIX packaging (`npm run package` / `npx vsce package`)
- Syntax highlighting for `.tyhp` / `.tyhpdef` is unchanged

## 0.2.1

- Syntax-only TextMate grammars and language configuration
