# Changelog

## 0.7.0

- One language server per owned `tyhp.json`, started lazily when a matching `.tyhp` / `.tyhpdef` file is opened
- Project membership uses `include` / `exclude` globs relative to that `tyhp.json` (same rules as `tyhp build`), including `../` overlays. Empty `include` owns nothing
- Files that match no project stay TextMate-only. Status bar shows the owner folder name or **not in a Tyhp project**
- `tyhp.projectPath` forces a single project and skips scanning others
- Init prompt only when the file has no owner and no ancestor `tyhp.json`

## 0.6.0

- Local QA matrix (`QA.md`): sideload (Install Plugin from Disk), packaging, PATH vs plugin-only CLI, init/run configs/proxy, and the editor-side LSP feature list. JetBrains Marketplace publish is out of scope
- Start/stop/restart `tyhp xdebug_proxy` from Tools → Tyhp and the status-bar popup, using `resolveTyhpBinary`
- Ports and `--sourcemap-dir`: explicit stored `tyhp.xdebugProxy.*` settings (empty UI fields are not stored) → `tyhp.json` `xdebugProxy` → CLI defaults 9003 / 9004
- Status bar shows proxy listening / error; **Tyhp XDebug Proxy** tool window logs start/stop
- Contribute/document a **PHP Remote Debug** configuration that uses PhpStorm’s built-in XDebug on the proxy IDE port; XDebug `client_port` is the proxy XDebug port
- Prerequisites messaging for missing sourcemaps (`build.generateSourcemap`) and a down proxy, with links to Story 17/18 docs

## 0.5.0

- Detect `tyhp.json` at a content root (or `tyhp.projectPath`); `--tyhp-project` is always a file
- Opening a Tyhp file with no project offers **Tyhp: Initialize Project** (`tyhp init --yes` in the content root)
- Run configurations for `tyhp build --quiet [--tyhp-project=<file>]` and `tyhp lint --quiet --format=json [--tyhp-project=<file>]` using `resolveTyhpBinary`
- Status bar shows project name, LSP ready/starting/error/stopped, and CLI health; click for Restart Language Server, Install CLI, Reveal Path, and Init (when no project)

## 0.4.0

- Start `tyhp language_server` as a child process over stdin/stdout via the 2026.2 `LspIntegrationProvider` API (`com.intellij.platform.lsp.integrationProvider`)
- Argv matches the VS Code client: `language_server --quiet --stdio [--tyhp-project=<tyhp.json file>] [extra args]`
- Restart the server when `tyhp.path` or the project file changes; auto-restart with exponential backoff on unexpected exit
- Missing CLI shows an error with Install / Refresh / Settings actions instead of crashing plugin load
- LSP start/stop/crash lines go to the **Tyhp Language Server** tool window (protocol traces: `#com.intellij.platform.lsp` in Debug Log Settings)
- `com.intellij.modules.ultimate` / LSP are an optional plugin dependency so FileType and TextMate still load without that module

## 0.3.0

- Resolve the Tyhp CLI: PATH probe writes `tyhp.path` when empty, then the setting wins
- **Tools → Tyhp → Install / Update CLI** offers Global (`~/.local/bin/tyhp` / `%LOCALAPPDATA%\Programs\tyhp`) vs Plugin only (config `tyhp-lang/cli`); checksum-verified GitHub Release downloads
- Plugin-only auto-update and `tyhp.binary.pinnedVersion`, with a drift guard so a stale `installMode=extension` never overwrites a hand-edited path
- Settings panel **Settings → Tools → Tyhp** for the same `tyhp.*` keys as the VS Code client
- Later phases call `com.tyhp.lang.binary.resolveTyhpBinary` rather than reading `tyhp.path` ad hoc

## 0.2.0

- Register `.tyhp` and `.tyhpdef` file types (PHP must not claim Tyhp files)
- Load the shared VS Code TextMate grammars (`tyhp-lang/vscode/syntaxes/`) at build time
- Comment / bracket / quote pairing matching VS Code `language-configuration.json`
- Distinct Project-view file icons copied from `tyhp-lang/vscode/media/`

## 0.1.0

- Initial PhpStorm plugin scaffold: plugin id `com.tyhp.lang`, display name **Tyhp Language**, vendor `tyhp-lang`
- Local ZIP packaging via `./gradlew buildPlugin` (Install Plugin from Disk; no JetBrains Marketplace publish)
- Gradle `runIde` against PhpStorm 2026.2.1
