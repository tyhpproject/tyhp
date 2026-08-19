# Tyhp Language Support for PhpStorm

First-party PhpStorm client for the Tyhp toolchain.

Product: **Tyhp Language** (`tyhp-lang`). Plugin ID: `com.tyhp.lang`.

**JetBrains Marketplace publish is out of scope** for Story 19.5. This plugin is installed locally (Gradle sandbox or **Install Plugin from Disk**).

**Local QA:** the repeatable sideload / packaging / editor checklist is [QA.md](./QA.md) (Story 19.5 Phase 14). It is a human matrix plus notes on what this repo’s `./gradlew unitTest` already covers — not a claim that every GUI item was clicked in this environment. Parity counterpart of `tyhp-lang/vscode/QA.md` (same scenarios, native PhpStorm UX).

Minimum IDE: **PhpStorm 2026.2** (`since-build` `262`). That baseline includes the IntelliJ Platform LSP API (`com.intellij.modules.lsp` / `com.intellij.platform.lsp`) including the 2026.1.4 client rename (`LspIntegrationProvider`). IntelliJ IDEA is not a supported target.

## Features

This 0.7.0 build starts **one language server per owned `tyhp.json`** (include/exclude membership, lazy start) and keeps XDebug-proxy debug, `tyhp init`, build/lint run configurations, a status bar, languages, TextMate highlighting, file icons, and CLI binary resolution:

- **File types:** `.tyhp` → **Tyhp**, `.tyhpdef` → **Tyhp Definition**. A file-type overrider keeps PhpStorm’s PHP highlighter from claiming `<?tyhp` files.
- **TextMate highlighting** from the canonical grammars in `tyhp-lang/vscode/syntaxes/` (copied into the plugin at Gradle build time; not forked). Works without the language server.
- **Comments / brackets / quotes** matching `tyhp-lang/vscode/language-configuration.json` where the Platform supports it (`//`, `/* */`, `{}` `[]` `()` `<>`, `'` / `"`).
- **Project-view icons** for `.tyhp` and `.tyhpdef` (light/dark variants from `tyhp-lang/vscode/media/`).
- **CLI binary resolution** — PATH probe, `tyhp.path`, Install / Update CLI (global or plugin-only), checksum-verified GitHub Release downloads, plugin-only auto-update / pin. Language server, run configurations, and the XDebug proxy call `resolveTyhpBinary` rather than reading the setting ad hoc.
- **Language server** — opening a `.tyhp` / `.tyhpdef` file starts `tyhp language_server` for the **owning** `tyhp.json` only (`LspIntegrationProvider`). Files that match no project’s `include` stay TextMate-only. `isSupportedFile` keeps each server on its own files.
- **Workspace** — indexes nested `tyhp.json` files and matches `include`/`exclude` (or forced `tyhp.projectPath`). Opening a Tyhp file with no owner and no ancestor `tyhp.json` offers **Tyhp: Initialize Project** (`tyhp init --yes`).
- **Run configurations** — **Run → Edit Configurations… → + → Tyhp** for `build` and `lint`.
- **Status bar** — compact project + LSP + CLI health + XDebug proxy listening. Click for Restart Language Server, Install CLI, Reveal Path, Initialize Project (when no `tyhp.json` is detected), and Start / Stop / Restart XDebug Proxy.
- **XDebug proxy** — start/stop `tyhp xdebug_proxy` and a documented **PHP Remote Debug** path that uses PhpStorm’s built-in XDebug as the DBGp client (proxy IDE port, typically 9003). Logs: **Tyhp XDebug Proxy** tool window.

## Development (`runIde`)

Gradle itself needs **JDK 17+** (`JAVA_HOME`). Compilation uses a **JDK 25** toolchain (provisioned automatically if missing — required by the PhpStorm 2026.2.1 platform API surface). The first run needs network access so Gradle can download PhpStorm 2026.2.1 and the JDK 25 toolchain.

If `java -version` is older than 17:

```bash
export JAVA_HOME=/opt/homebrew/opt/openjdk@17   # example: Homebrew
```

From this directory (`tyhp-lang/phpstorm/`):

```bash
./gradlew runIde
```

That launches a PhpStorm sandbox with this plugin installed. The default `runIde` task targets PhpStorm (`platformType=PS`), not IntelliJ IDEA. The first run is **heavy** (downloads the IDE + JDK 25 toolchain). Packaging acceptance does not require `runIde`: `./gradlew unitTest` and `./gradlew buildPlugin` are enough to prove the ZIP; GUI rows stay in [QA.md](./QA.md).

Import this folder as a Gradle project in IntelliJ IDEA / PhpStorm to run the same `runIde` task from the Gradle tool window.

### Verify file types, highlighting, and icons

After `runIde` (do not need a full Tyhp project or LSP):

1. Open `tyhp-lang/vscode/samples/highlight-audit.tyhp` and `highlight-audit.tyhpdef` (File → Open, or drop them into the sandbox project).
2. Confirm the editor status bar / tab file type is **Tyhp** / **Tyhp Definition**, not PHP.
3. Confirm keywords (`struct`, `extension`, `async`), `//` / `/* */` comments, and strings are colored. Highlighting must work with the language server disabled.
4. Confirm distinct file icons in the Project tool window for the two extensions.
5. Optional: **Settings → Editor → TextMate Bundles** should list a **Tyhp** bundle loaded from this plugin.

## Package a local ZIP

Does **not** publish to the JetBrains Marketplace:

```bash
cd tyhp-lang/phpstorm
./gradlew buildPlugin
```

The task writes `build/distributions/tyhp-lang-<version>.zip` (currently `tyhp-lang-0.6.0.zip`). The ZIP includes copied grammars under `textmate/tyhp/syntaxes/` and file icons inside the plugin JAR (`icons/`).

Equivalent helper:

```bash
./package.sh
```

## Local installation (Install Plugin from Disk)

1. Build the ZIP with `./gradlew buildPlugin` (see above).
2. In PhpStorm 2026.2+, open **Settings → Plugins**.
3. Click the gear icon (⚙) next to the Plugins heading / installed-plugin search.
4. Choose **Install Plugin from Disk…**
5. Select `tyhp-lang/phpstorm/build/distributions/tyhp-lang-0.6.0.zip`.
6. Restart PhpStorm when prompted.

This sideload path does not use JetBrains Marketplace credentials.

## Tyhp CLI binary

The plugin locates a `tyhp` executable and records it in `tyhp.path` (**Settings → Tools → Tyhp**). The language server, run configurations, and XDebug proxy must use `com.tyhp.lang.binary.resolveTyhpBinary` rather than reading the setting ad hoc.

### Discovery

1. If `tyhp.path` is set, that value wins (absolute path, `~/…`, or a command name on `PATH`). A missing or non-file path shows a notification with **Install / Update CLI** / **Open Settings**.
2. If `tyhp.path` is empty, plugin/project start and **Tools → Tyhp → Refresh Tyhp binary** search `PATH` for `tyhp` / `tyhp.exe`. A hit **writes** `tyhp.path` (user-level default unless a project override already exists).

### Install / Update

**Tools → Tyhp → Install / Update CLI** downloads an official compiler binary from GitHub Releases:

| Mode | Location | Auto-update |
|------|----------|-------------|
| **Global** | Unix: `~/.local/bin/tyhp` (same as `scripts/install.sh`). Windows: `%LOCALAPPDATA%\Programs\tyhp\tyhp.exe` (same as `scripts/install.ps1`). | Never. Update only via this action. |
| **Plugin only** | `{PhpStorm config}/tyhp-lang/cli/tyhp` (or `tyhp.exe`) | Yes, when `tyhp.binary.autoUpdate` is on **and** metadata shows this plugin (`com.tyhp.lang`) installed the file **and** `tyhp.path` still points at that managed location. |

After a successful install, `tyhp.path` and `tyhp.binary.installMode` (`path` \| `global` \| `extension`) are updated. The settings value remains `extension` for plugin-only (same as VS Code).

`tyhp.binary.pinnedVersion` (plugin-only): if non-empty, install/keep that GitHub tag and do **not** auto-update to other versions. Tags may omit the leading `v` (`805.0.0-alpha.1` = `v805.0.0-alpha.1`).

Startup auto-update is delayed ~5s and skipped when a check ran in the last 6 hours (pin enforcement still runs). A stale `installMode=extension` with a hand-edited `tyhp.path` that no longer points at the plugin-managed file is **not** auto-updated.

### Release artifacts

Source: GitHub Releases for [`tyhpproject/tyhp`](https://github.com/tyhpproject/tyhp/releases) — the same assets as `scripts/install.sh` / `scripts/release.sh`.

- **API:** `GET https://api.github.com/repos/tyhpproject/tyhp/releases?per_page=20` (first non-draft, **including prereleases**). Do not use `/releases/latest` (it hides prereleases). A pin uses `/releases/tags/{tag}`.
- **Asset names:** `tyhp-{os}-{arch}` or `tyhp-{os}-{arch}-fxdependent`, with `.exe` on Windows. `os` is `osx` \| `linux` \| `win`; `arch` is `x64` \| `arm64`. Published set: `tyhp-osx-arm64`, `tyhp-osx-x64`, `tyhp-linux-x64`, `tyhp-linux-arm64`, `tyhp-win-x64.exe`, and matching `-fxdependent` variants. There is no `win-arm64` asset.
- **Variant:** Plugin-only always downloads the **self-contained** asset. Global matches the install scripts: **framework-dependent** when a .NET 9 runtime is detected, otherwise self-contained.
- **Checksums:** each release includes `checksums.txt` (GNU `sha256sum` lines from `scripts/release.sh`). The download is hashed with SHA-256 and compared; a missing checksums file or mismatch **aborts** the install.
- **Auth:** optional `GITHUB_TOKEN` or `GH_TOKEN` environment variable (GitHub API rate limits).
- **Missing assets / unsupported platform:** install fails with a clear notification — plugin load still succeeds. Set `tyhp.path` to a locally built `tyhp` instead.

### Actions

| Action | ID |
|--------|-----|
| Tyhp: Refresh Tyhp binary | `tyhp.refreshBinary` |
| Tyhp: Install / Update CLI | `tyhp.installCli` |
| Tyhp: Reveal CLI Path | `tyhp.revealBinary` |
| Tyhp: Restart Language Server | `tyhp.restartLanguageServer` |
| Tyhp: Initialize Project | `tyhp.initProject` |
| Tyhp: Start XDebug Proxy | `tyhp.startXdebugProxy` |
| Tyhp: Stop XDebug Proxy | `tyhp.stopXdebugProxy` |
| Tyhp: Restart XDebug Proxy | `tyhp.restartXdebugProxy` |
| Tyhp: Create PHP Remote Debug Configuration | `tyhp.createPhpRemoteDebug` |

### Settings (`tyhp.*`)

| Key | Role |
|-----|------|
| `tyhp.path` | CLI path or command name |
| `tyhp.projectPath` | Force a single `tyhp.json` (file or directory). Empty indexes every `tyhp.json` and routes files by `include`/`exclude`. Passed as `--tyhp-project` (file path, never a directory) |
| `tyhp.languageServer.args` | Extra args after `language_server` (`--quiet` / `--stdio` / `--tyhp-project` are added automatically) |
| `tyhp.languageServer.trace` | `off` \| `messages` \| `verbose` (sent as LSP `initialize.trace`; protocol I/O also needs `#com.intellij.platform.lsp` in Debug Log Settings) |
| `tyhp.diagnostics.enable` | Publish diagnostics from the language server (default true) |
| `tyhp.completion.autoImport` | Auto-import (server-side; later UX) |
| `tyhp.binary.installMode` | `path` \| `global` \| `extension` |
| `tyhp.binary.autoUpdate` | Plugin-only auto-update |
| `tyhp.binary.pinnedVersion` | Plugin-only pinned release tag |
| `tyhp.xdebugProxy.idePort` | IDE listen port (PhpStorm Xdebug debug port). Empty / unset → `tyhp.json` `xdebugProxy.idePort` → `9003`. UI placeholders are not stored. |
| `tyhp.xdebugProxy.xdebugPort` | XDebug engine port (`client_port`). Empty / unset → `tyhp.json` `xdebugProxy.xdebugPort` → `9004` |
| `tyhp.xdebugProxy.sourceMapDir` | Optional `--sourcemap-dir`. Empty uses `tyhp.json` `xdebugProxy.sourceMapDir`, else CLI `output.path` |

## Language server

Opening a `.tyhp` / `.tyhpdef` file starts `tyhp language_server` as a child process over stdin/stdout via `LspIntegrationProvider` (`com.intellij.platform.lsp.integrationProvider` — not the deprecated 2026.1 `LspServerSupportProvider`):

```text
<resolved tyhp> language_server --quiet --stdio [--tyhp-project=<path-to-tyhp.json-FILE>] [extra args from settings]
```

`--tyhp-project` is the owning `tyhp.json` (include/exclude match), or the forced `tyhp.projectPath` when that setting is set. The value is always a **file**, never a directory. Extra tokens from `tyhp.languageServer.args` are appended after those flags.

If the CLI binary is missing, plugin load still succeeds. An error notification offers **Install / Update CLI**, **Refresh Tyhp binary**, and **Open Settings**. If the language server process exits unexpectedly, the client restarts it with exponential backoff (1s, 2s, 4s, … capped at 30s). Closing the project or disabling the plugin stops the process.

Logs:

- **Tyhp Language Server** tool window (bottom) — start command, missing binary, crash/restart
- **idea.log** and **Language Services** status-bar widget (platform LSP client)
- Protocol traces: **Help → Diagnostic Tools → Debug Log Settings…** → add `#com.intellij.platform.lsp`

`com.intellij.modules.ultimate` (and the LSP module it brings) is an **optional** plugin dependency (`tyhp-lsp.xml`). File types and TextMate highlighting still load if that module is absent. PhpStorm 2026.2 always has it.

### Workspace and `tyhp init`

The plugin indexes every `tyhp.json` under content roots and owns a file when that project’s `include`/`exclude` globs match (same rules as `tyhp build`, including `../` overlays). Empty `include` owns nothing. Set `tyhp.projectPath` to a `tyhp.json` **file** (or a directory that contains one) to force a single project and skip scanning others.

If you open a `.tyhp` / `.tyhpdef` file that has **no owner** and **no ancestor `tyhp.json`**, a notification offers **Initialize Project**. An ancestor that exists but does not include the file stays silent (TextMate only). That action (also **Tools → Tyhp → Initialize Project**) runs the resolved CLI:

```text
<resolved tyhp> init --yes
```

cwd is the content root that contains the file (or the first content root). `--yes` accepts init defaults without prompting. `--tyhp-project` is **not** passed: that flag requires an existing file. After a successful init, project state reloads and the language server restarts if it was already running.

### Run configurations

**Run → Edit Configurations… → + → Tyhp** lists:

| Configuration | Argv |
|---------------|------|
| `tyhp build` | `build --quiet [--tyhp-project=<file>]` |
| `tyhp lint` | `lint --quiet --format=json [--tyhp-project=<file>]` |

Both use `resolveTyhpBinary(project)` (never `tyhp.path` directly). Working directory is the directory that contains `tyhp.json` when one is detected.

### Status bar

The status bar item shows `Tyhp`, the **owner** project folder name (or **not in a Tyhp project**), LSP **ready** / **starting** / **error** / **stopped**, CLI health (`CLI missing` when the binary is absent or invalid), and **proxy** when `tyhp xdebug_proxy` is listening. Click it for Restart Language Server, Install / Update CLI, Reveal CLI Path, Initialize Project (when the active file has no owner), Start / Stop / Restart XDebug Proxy, and Create PHP Remote Debug Configuration. Proxy logs: **Tyhp XDebug Proxy** tool window.

### Verify language server (`runIde`)

Requires a resolved Tyhp CLI (`tyhp.path` or PATH / Install CLI) and a small project with `tyhp.json` (the same fixture used for VS Code is fine):

1. `./gradlew runIde`, then open a folder that contains `tyhp.json` plus a `.tyhp` file.
2. Open the `.tyhp` file. **Language Services** should list **Tyhp**, and **Tyhp Language Server** should log a start line with `--quiet --stdio --tyhp-project=…/tyhp.json`.
3. Introduce a syntax/type error; diagnostics should appear while the server is up.
4. Hover a known symbol and **Go to Declaration** (the Story 19 features the CLI already supports).
5. Clear `tyhp.path` (and ensure `tyhp` is not on PATH) → error notification with Install / Refresh, plugin stays loaded.
6. With the server running, terminate the `tyhp language_server` OS process yourself (do not ask the IDE to kill it from this plugin). The log should show an unexpected exit and a delayed restart.
7. Close the project (or disable the plugin) and confirm the `language_server` process is gone.

### Debugging `.tyhp` (XDebug proxy)

The plugin starts Story 18’s `tyhp xdebug_proxy` so PhpStorm’s **built-in XDebug** (PHP Remote Debug) can hit breakpoints in `.tyhp` sources. It does **not** reimplement DBGp.

**Prerequisites**

1. Enable sourcemaps in `tyhp.json` and **build** the project (maps are `.php.map` files next to emitted PHP, usually under `output.path`, default `build/`):

```json
{
    "build": {
        "generateSourcemap": true
    }
}
```

See [Source map generation](https://github.com/tyhpproject/tyhp/blob/main/docs/content/cli_sourcemapGeneration.md) and [XDebug proxy](https://github.com/tyhpproject/tyhp/blob/main/docs/content/cli_xdebugProxy.md).

2. Point XDebug at the proxy **XDebug port** (default **9004**), not the IDE / PhpStorm debug port:

```ini
xdebug.mode = debug
xdebug.client_host = 127.0.0.1
xdebug.client_port = 9004
xdebug.idekey = tyhp
```

**Start the proxy**

Tools → Tyhp / status bar: **Start XDebug Proxy**. Argv (resolved CLI; flags are the real CLI switches):

```text
<resolved tyhp> xdebug_proxy [--tyhp-project=<path-to-tyhp.json-FILE>] --ide-port=9003 --xdebug-port=9004 [--sourcemap-dir=<dir>] [--ide-key=<key>]
```

Ports: explicit stored `tyhp.xdebugProxy.idePort` / `xdebugPort` / `sourceMapDir` settings win; otherwise `tyhp.json` `xdebugProxy.*`; otherwise defaults **9003** (IDE) and **9004** (XDebug). Leave the settings fields **empty** so they do not shadow `tyhp.json`. `--sourcemap-dir` is omitted when unset so the CLI uses `output.path`. **Stop XDebug Proxy** sends SIGTERM to that process (`Process.destroy`) and waits for it to release the ports.

**PHP Remote Debug**

PhpStorm’s built-in XDebug is the DBGp client. The plugin contributes/documents a configuration named **Listen for Tyhp (XDebug proxy)**:

1. **Tools → Tyhp → Create PHP Remote Debug Configuration** (or add **Run → Edit Configurations… → + → PHP Remote Debug** yourself).
2. Set **Settings → PHP → Debug → Xdebug → Debug port** to the proxy **IDE port** (default **9003**).
3. Run that configuration (or **Start Listening for PHP Debug Connections**).
4. XDebug `client_port` stays on the proxy **XDebug port** (default **9004**).

**Minimal manual QA**

1. `generateSourcemap: true` + **Run → tyhp build**
2. Start the proxy (status bar or **Tyhp: Start XDebug Proxy**). **Tyhp XDebug Proxy** should show `XDebug Proxy started` and loaded maps.
3. Create/run **Listen for Tyhp (XDebug proxy)** with PhpStorm debug port **9003**.
4. Set a breakpoint in a `.tyhp` file, then run the compiled PHP (browser or `php`) with XDebug `client_port=9004`. The breakpoint should hit in the `.tyhp` source, not only the emitted `.php`.

**Misconfiguration**

| Symptom | What to do |
|---------|------------|
| Proxy will not start / port in use | Stop the proxy from the status bar, or change `tyhp.xdebugProxy.idePort` / `xdebugPort` (or `tyhp.json` `xdebugProxy`). See **Tyhp XDebug Proxy**. |
| No sourcemaps / breakpoints stay on `.php` | Set `build.generateSourcemap` and rebuild. Docs: [sourcemaps](https://github.com/tyhpproject/tyhp/blob/main/docs/content/cli_sourcemapGeneration.md). |
| PHP Remote Debug missing | Use **Create PHP Remote Debug Configuration**, or add PHP Remote Debug manually. PhpStorm’s XDebug is the DBGp client. |
| Proxy down when debugging | Start the proxy first. Docs: [xdebug_proxy](https://github.com/tyhpproject/tyhp/blob/main/docs/content/cli_xdebugProxy.md). |

## Uninstalling

**Settings → Plugins → Installed → Tyhp Language → Uninstall**, then restart PhpStorm.

## License

Apache License 2.0. See `LICENSE` in this folder and `LICENSE.txt` at the repository root.
