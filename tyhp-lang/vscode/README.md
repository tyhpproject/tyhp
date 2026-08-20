# Tyhp Language Support for VS Code / Cursor

Syntax highlighting and a Language Server Protocol client for `.tyhp` and `.tyhpdef` files, plus a standalone PHP grammar, **Tyhp CLI** discovery / install, workspace/`tyhp.json` awareness, build/lint tasks, XDebug proxy debugging, and a status bar.

Product: **Tyhp Language** (`tyhp-lang`). Extension ID: `tyhp-lang.tyhp`.

**Marketplace / Open VSX publish is out of scope** for Story 19.5. This extension is installed locally (symlink, copy, or sideload VSIX).

**Local QA:** the repeatable sideload / packaging / editor checklist is [QA.md](./QA.md) (Story 19.5 Phase 7). It is a human matrix plus notes on what this repo’s unit tests already cover — not a claim that every GUI item was clicked in this environment.

## Features

- **Tyhp language** (`source.tyhp`) — full standalone grammar for `.tyhp` and `.tyhpdef` files:
  - Optional `<?tyhp` / `<?tyhpdef` open tags (tagless files highlight as Tyhp code from line 1)
  - Classic mode with open/close tags for inline HTML interleaving
  - `async` / `await`
  - `struct`, `extension`, `type` alias declarations
  - `operator` overload declarations (arithmetic, comparison, conversion)
  - `using` blocks and `:=` using-assignment
  - `is` / `isa` / `isan` type checks
  - `typeof`, `nameof`, `variable_exists` expressions
  - `deprecated`, `obsolete` modifiers
  - `decimal` type and `(decimal)` cast
  - `with` keyword
  - Generic type parameters (`<T>`, `<T extends Type>`, nested generics)
  - Constructor delegation (`: parent(...)`, `: void`)
  - Type interpolation in strings (`"${TypeName}"`, `"${A|B}"`, `"${Type+}"`)
- **File icons** for `.tyhp` and `.tyhpdef` (light/dark language icons; optional **Tyhp File Icons** theme with default/light/high-contrast variants and distinct `.tyhp` vs `.tyhpdef` glyphs)
- **Tyhp CLI** — find `tyhp` on `PATH`, install from GitHub Releases (global or extension-only), and keep an extension-only binary updated or pinned
- **Language server** — launches one `tyhp language_server` per owned `tyhp.json` over stdin/stdout using the resolved CLI. A project owns a file when that file matches its `include` / `exclude` globs (same rules as `tyhp build`, including `../` overlays). Servers start when you open an owned file and stop when that project has no open Tyhp documents. Files that match no project stay TextMate-only. Diagnostics, hover, go-to-definition, completion, and the rest of Story 19’s LSP features appear through the standard VS Code Language Client. Logs: Output panel > **Tyhp Language Server**. Trace: `tyhp.languageServer.trace`.
- **Workspace** — indexes every `tyhp.json` under the workspace (or the single file forced by `tyhp.projectPath`). The status bar, tasks, and XDebug proxy follow the **active editor’s owner**. Opening a Tyhp file with no owner and no ancestor `tyhp.json` offers **Tyhp: Initialize Project** (`tyhp init --yes`).
- **Tasks** — `tyhp: build` and `tyhp: lint` run against the resolved CLI and pass `--tyhp-project` when a project file is known. Lint uses `--format=json`.
- **XDebug proxy** — start/stop `tyhp xdebug_proxy` and contribute a PHP Debug launch snippet that listens on the proxy IDE port so breakpoints can hit `.tyhp` sources. Logs: Output panel > **Tyhp XDebug Proxy**.
- **Status bar** — compact project + LSP + CLI health + XDebug proxy listening state. Click for Restart Language Server, Install CLI, Initialize Project, and Start/Stop/Restart XDebug Proxy.
- **PHP (Tyhp)** (`source.tyhp.php`) — full standalone PHP grammar, manually selectable for `.php` files
- Comment toggling, bracket matching (`{}`, `[]`, `()`, `<>`), auto-closing pairs, folding, and indentation

Language light/dark file icons show in the explorer without changing your file icon theme. The optional **Tyhp File Icons** theme (`File Icon Theme` picker) maps `.tyhp` vs `.tyhpdef` with default, light, and high-contrast variants. It only defines Tyhp glyphs, so keep your existing theme selected unless you want those mappings.

Highlighting QA samples: `samples/highlight-audit.tyhp`, `samples/highlight-audit.tyhpdef`, and `samples/highlight-audit.md`.

## Development (compile + F5)

The extension has a TypeScript entry point (`src/extension.ts`). Language contributions (TextMate grammars) load from `package.json` regardless; compiling is required so `main` can activate without an error.

From this directory (`tyhp-lang/vscode/`):

```bash
npm install
npm run compile
npm test
```

`npm run package` compiles, then **esbuild-bundles** `src/extension.ts` (including `vscode-languageclient`) into `out/extension.js` so the VSIX does not need `node_modules` at runtime.

**F5 (Extension Development Host):** open **this folder** (`tyhp-lang/vscode`) as the workspace in VS Code or Cursor, then press F5. That uses `.vscode/launch.json` (`Run Extension`). The first launch compiles/watches via `.vscode/tasks.json`.

To package a local VSIX (does not publish):

```bash
npm run package
# or: npx vsce package
```

## Local Installation

### Option 1: Symlink (recommended for development)

Changes to the grammar are picked up on each window reload — no reinstall needed. Run `npm install` and `npm run compile` in this folder so `out/extension.js` exists.

**Important:** The symlink target must be an **absolute path** (e.g. `/Users/you/repos/tyhp/tyhp-lang/vscode`). Do not use a relative path like `./tyhp-lang/vscode` — `ln -s` resolves relative targets from the extensions directory, not your current shell directory, which creates a broken self-referential symlink.

The symlink must also use Cursor/VS Code's expected folder name: `{publisher}.{name}-{version}` (from `package.json`). For this extension that is `tyhp-lang.tyhp-<version>` — currently `tyhp-lang.tyhp-0.8.0`.

If you previously installed as `tyhp-lang.tyhp-language-<version>` (old folder / old `name`) or `tyhp-lang.tyhp-0.7.0` / `0.5.0`, remove that install first.

From the repo root:

```bash
rm -rf ~/.cursor/extensions/tyhp-lang.tyhp-0.8.0   # Cursor
ln -s "$(pwd)/tyhp-lang/vscode" ~/.cursor/extensions/tyhp-lang.tyhp-0.8.0

rm -rf ~/.vscode/extensions/tyhp-lang.tyhp-0.8.0  # VS Code
ln -s "$(pwd)/tyhp-lang/vscode" ~/.vscode/extensions/tyhp-lang.tyhp-0.8.0
```

Or use an explicit absolute path:

**Cursor:**

```bash
rm -rf ~/.cursor/extensions/tyhp-lang.tyhp-0.8.0
ln -s "/path/to/tyhp/tyhp-lang/vscode" ~/.cursor/extensions/tyhp-lang.tyhp-0.8.0
```

**VS Code:**

```bash
rm -rf ~/.vscode/extensions/tyhp-lang.tyhp-0.8.0
ln -s "/path/to/tyhp/tyhp-lang/vscode" ~/.vscode/extensions/tyhp-lang.tyhp-0.8.0
```

Then **fully quit and reopen** Cursor/VS Code (`Cmd+Q`, not just "Reload Window"). If you previously had a broken install, also remove the extension from `~/.cursor/extensions/.obsolete` if it lists `tyhp-lang.tyhp-0.8.0`, `tyhp-lang.tyhp-0.6.0`, `tyhp-lang.tyhp-0.5.0`, `tyhp-lang.tyhp-0.4.0`, `tyhp-lang.tyhp-0.3.1`, or `tyhp-lang.tyhp-language-0.2.1`.

### Option 2: Sideload a VSIX

Build the VSIX from `tyhp-lang/vscode/` (requires `npm install` once):

```bash
cd tyhp-lang/vscode
npm install
npm run package
```

`vsce package` writes `tyhp-0.8.0.vsix` in this folder. Sideload it — this does **not** publish to the Marketplace:

**Cursor:**

```bash
cursor --install-extension tyhp-0.8.0.vsix
```

**VS Code:**

```bash
code --install-extension tyhp-0.8.0.vsix
```

You can also use **Install from VSIX…** in the Extensions view (`…` menu).

A global `vsce` is optional; `npm run package` uses the local `@vscode/vsce` devDependency. Equivalent:

```bash
npx vsce package
```

### Option 3: Copy to extensions directory

Compile first (`npm run compile`), then copy. The folder name must be `tyhp-lang.tyhp-<version>`:

**Cursor:**

```bash
cp -r tyhp-lang/vscode ~/.cursor/extensions/tyhp-lang.tyhp-0.8.0
```

**VS Code:**

```bash
cp -r tyhp-lang/vscode ~/.vscode/extensions/tyhp-lang.tyhp-0.8.0
```

Then fully quit and reopen the editor.

## Tyhp CLI binary

The extension locates a `tyhp` executable and records it in `tyhp.path`. The language server, tasks, and the XDebug proxy use the same resolution API (`resolveTyhpBinary()`) rather than reading the setting ad hoc.

### Discovery

1. If `tyhp.path` is set, that value wins (absolute path, `~/…`, or a command name on `PATH`). A missing or non-file path shows an error on the status bar; click it or run **Tyhp: Install / Update CLI**.
2. If `tyhp.path` is empty, activation and **Tyhp: Refresh Tyhp binary** search `PATH` for `tyhp` / `tyhp.exe`. A hit **writes** `tyhp.path` (User settings by default; Workspace / folder scope if that override already exists).

### Install / Update

**Tyhp: Install / Update CLI** downloads an official compiler binary from GitHub Releases:

| Mode | Location | Auto-update |
|------|----------|-------------|
| **Global** | Unix: `~/.local/bin/tyhp` (same as `scripts/install.sh`). Windows: `%LOCALAPPDATA%\Programs\tyhp\tyhp.exe` (same as `scripts/install.ps1`). | Never. Update only via this command. |
| **Extension only** | `{extension globalStorage}/cli/tyhp` (or `tyhp.exe`) | Yes, when `tyhp.binary.autoUpdate` is on **and** metadata shows this extension installed the file. |

After a successful install, `tyhp.path` and `tyhp.binary.installMode` (`path` \| `global` \| `extension`) are updated.

`tyhp.binary.pinnedVersion` (extension-only): if non-empty, install/keep that GitHub tag and do **not** auto-update to other versions. Tags may omit the leading `v` (`805.0.0-alpha.1` = `v805.0.0-alpha.1`).

Startup auto-update is delayed ~5s and skipped when a check ran in the last 6 hours (pin enforcement still runs).

### Release artifacts

Source: GitHub Releases for [`tyhpproject/tyhp`](https://github.com/tyhpproject/tyhp/releases) — the same assets as `scripts/install.sh` / `scripts/release.sh`.

- **API:** `GET https://api.github.com/repos/tyhpproject/tyhp/releases?per_page=20` (first non-draft, **including prereleases**). Do not use `/releases/latest` (it hides prereleases). A pin uses `/releases/tags/{tag}`.
- **Asset names:** `tyhp-{os}-{arch}` or `tyhp-{os}-{arch}-fxdependent`, with `.exe` on Windows. `os` is `osx` \| `linux` \| `win`; `arch` is `x64` \| `arm64`. Published set: `tyhp-osx-arm64`, `tyhp-osx-x64`, `tyhp-linux-x64`, `tyhp-linux-arm64`, `tyhp-win-x64.exe`, and matching `-fxdependent` variants. There is no `win-arm64` asset.
- **Variant:** Extension-only always downloads the **self-contained** asset. Global matches the install scripts: **framework-dependent** when a .NET 9 runtime is detected, otherwise self-contained.
- **Checksums:** each release includes `checksums.txt` (GNU `sha256sum` lines from `scripts/release.sh`). The download is hashed with SHA-256 and compared; a missing checksums file or mismatch **aborts** the install.
- **Auth:** optional `GITHUB_TOKEN` or `GH_TOKEN` environment variable (GitHub API rate limits).
- **Missing assets:** if the repo has no public compiler binaries yet, install fails with a clear error — set `tyhp.path` to a locally built `tyhp` instead.

### Commands

| Command | ID |
|---------|-----|
| Tyhp: Refresh Tyhp binary | `tyhp.refreshBinary` |
| Tyhp: Install / Update CLI | `tyhp.installCli` |
| Tyhp: Reveal CLI Path | `tyhp.revealBinary` |
| Tyhp: Restart Language Server | `tyhp.restartLanguageServer` |
| Tyhp: Initialize Project | `tyhp.initProject` |
| Tyhp: Start XDebug Proxy | `tyhp.startXdebugProxy` |
| Tyhp: Stop XDebug Proxy | `tyhp.stopXdebugProxy` |
| Tyhp: Restart XDebug Proxy | `tyhp.restartXdebugProxy` |

### Workspace and `tyhp init`

The extension indexes every `tyhp.json` under the workspace (`**/tyhp.json`, skipping `node_modules`, `vendor`, `.git`, `bin`, `obj`, `dist`, `build`). A file is **owned** by the project whose `include` / `exclude` globs match, resolved relative to that `tyhp.json` directory — the same Matcher rules as `tyhp build`. Empty `include` owns nothing (the repo-root `tyhp.json` with `"include": []` is not an LSP project). Nested projects such as `runtime/packages/core/tyhp.json` can own both `./tyhp_src/**/*.tyhp` and overlay paths like `../../php-extensions/php8.2.9/**/*.tyhpdef`.

If several projects match, one owner is chosen (do not merge two type worlds): nearest ancestor `tyhp.json`, else fewest path hops, else shortest then lexicographic path.

Set `tyhp.projectPath` to a `tyhp.json` **file** (or a directory that contains one) to **force a single project** and skip scanning others. Files outside that project’s include stay TextMate-only.

If you open a `.tyhp` / `.tyhpdef` file that has **no owner** and **no ancestor `tyhp.json`** up to the workspace root, a prompt offers **Initialize Project**. An ancestor that exists but does not include the file stays silent (TextMate only). `tyhp.projectPath` also suppresses the prompt. The command (also in the Command Palette) runs the resolved CLI:

```text
<resolved tyhp> init --yes
```

cwd is the workspace folder that contains the file (or the first folder). `--yes` accepts init defaults without prompting. `--tyhp-project` is **not** passed: that flag requires an existing file. After a successful init, the workspace index reloads and a language server starts when an owned file is open.

Build/lint tasks and the XDebug proxy use the **active editor’s owner** `tyhp.json` as `--tyhp-project`.

### Tasks

**Terminal → Run Task…** lists:

| Task | Argv |
|------|------|
| `tyhp: build` | `build --quiet [--tyhp-project=<file>]` |
| `tyhp: lint` | `lint --quiet --format=json [--tyhp-project=<file>]` |

Both use `resolveTyhpBinary()` (never `tyhp.path` directly). Build uses the `$tyhp` problem matcher for rustc-style `file(line,col): error TYHP####: …` headers. Lint JSON is shown in the terminal (VS Code problem matchers are line-based; matching the JSON document is optional).

You can also put a task in `.vscode/tasks.json`:

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "type": "tyhp",
            "action": "build",
            "label": "tyhp: build"
        }
    ]
}
```

### Status bar

The left status bar item shows `Tyhp`, the **owner** project folder name (or **not in a Tyhp project**), LSP **ready** / **starting** / **error**, CLI health, and **proxy** when `tyhp xdebug_proxy` is listening. Click it for Restart Language Server, Install / Update CLI, Reveal CLI Path, Initialize Project (when the active file has no owner), and Start / Stop / Restart XDebug Proxy. Proxy logs: Output panel > **Tyhp XDebug Proxy**.

### Language server

Opening a `.tyhp` / `.tyhpdef` file (language id `tyhp` — there is no separate `tyhpdef` language) starts `tyhp language_server` **for the owning `tyhp.json` only**, as a child process over stdin/stdout:

```text
<resolved tyhp> language_server --quiet --stdio --tyhp-project=<path-to-that-project's-tyhp.json>
```

A second nested project gets its own process when you open a file it owns. Each client’s document middleware keeps other projects’ files off that server. Files with no owner never start a server (TextMate highlighting still works).

`--tyhp-project` is the owner file, or the forced `tyhp.projectPath` when that setting is set. Extra tokens from `tyhp.languageServer.args` are appended after those flags.

If the CLI binary is missing, activation still succeeds. The status bar shows **CLI missing** and **Tyhp: Install / Update CLI** remains available.

If the resolved CLI is too old (Story 19 `language_server` still a stub) or the process exits before initialize completes, the client **does not** retry in a loop. You get one error with **Install / Update CLI**, **Open Settings** (`tyhp.path`), or **Show Output**. Point `tyhp.path` at a current build, then **Tyhp: Restart Language Server**.

If a healthy server later crashes, the client restarts it with exponential backoff, then stops after a few failures instead of toasting forever. Stopping the extension host stops the server (`deactivate` → `LanguageClient.stop()`).

Logs and LSP traces go to **Output** > **Tyhp Language Server**. Set `tyhp.languageServer.trace` to `messages` or `verbose` when debugging the protocol.

### Debugging `.tyhp` (XDebug proxy)

The extension starts Story 18’s `tyhp xdebug_proxy` so the [PHP Debug](https://marketplace.visualstudio.com/items?itemName=xdebug.php-debug) extension (`xdebug.php-debug`) can hit breakpoints in `.tyhp` sources. It does **not** reimplement DBGp and does not replace PHP Debug.

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

2. Install **PHP Debug** (`xdebug.php-debug`). It is the DBGp client; this extension only launches the proxy and contributes a launch snippet.

3. Point XDebug at the proxy **XDebug port** (default **9004**), not the IDE port:

```ini
xdebug.mode = debug
xdebug.client_host = 127.0.0.1
xdebug.client_port = 9004
xdebug.idekey = tyhp
```

**Start the proxy**

Command Palette / status bar: **Tyhp: Start XDebug Proxy**. Argv (resolved CLI; flags are the real CLI switches):

```text
<resolved tyhp> xdebug_proxy --tyhp-project=<path-to-tyhp.json> --ide-port=9003 --xdebug-port=9004 [--sourcemap-dir=<dir>] [--ide-key=<key>]
```

Ports: explicit `tyhp.xdebugProxy.idePort` / `xdebugPort` / `sourceMapDir` settings win; otherwise `tyhp.json` `xdebugProxy.*`; otherwise defaults **9003** (IDE) and **9004** (XDebug). `--sourcemap-dir` is omitted when unset so the CLI uses `output.path`. **Tyhp: Stop XDebug Proxy** sends SIGTERM to that process and waits for it to release the ports.

**launch.json**

Add a PHP Debug configuration that listens on the **proxy IDE port** (default **9003**). The extension contributes this snippet and a debug configuration provider:

```json
{
    "name": "Listen for Tyhp (XDebug proxy)",
    "type": "php",
    "request": "launch",
    "port": 9003
}
```

**Minimal manual QA**

1. `generateSourcemap: true` + **Terminal → Run Task… → tyhp: build**
2. Start the proxy (status bar or **Tyhp: Start XDebug Proxy**). Output > **Tyhp XDebug Proxy** should show `XDebug Proxy started` and loaded maps.
3. Install PHP Debug if prompted. Run **Listen for Tyhp (XDebug proxy)**.
4. Set a breakpoint in a `.tyhp` file, then run the compiled PHP (browser or `php`) with XDebug `client_port=9004`. The breakpoint should hit in the `.tyhp` source, not only the emitted `.php`.

**Misconfiguration**

| Symptom | What to do |
|---------|------------|
| Proxy will not start / port in use | Stop the proxy from the status bar, or change `tyhp.xdebugProxy.idePort` / `xdebugPort` (or `tyhp.json` `xdebugProxy`). See Output > **Tyhp XDebug Proxy**. |
| No sourcemaps / breakpoints stay on `.php` | Set `build.generateSourcemap` and rebuild. Docs: [sourcemaps](https://github.com/tyhpproject/tyhp/blob/main/docs/content/cli_sourcemapGeneration.md). |
| PHP Debug missing | Install `xdebug.php-debug`. Starting a Tyhp launch config offers that action. |
| Proxy down when debugging | The launch resolver offers **Start XDebug Proxy**. Docs: [xdebug_proxy](https://github.com/tyhpproject/tyhp/blob/main/docs/content/cli_xdebugProxy.md). |

### Settings (`tyhp.*`)

| Key | Role |
|-----|------|
| `tyhp.path` | CLI path or command name |
| `tyhp.projectPath` | Force a single `tyhp.json` (file or directory). Empty indexes every workspace `tyhp.json` and routes files by `include`/`exclude`. Passed as `--tyhp-project` (file path) |
| `tyhp.languageServer.args` | Extra args after `language_server` (`--quiet` / `--stdio` / `--tyhp-project` are added automatically) |
| `tyhp.languageServer.trace` | `off` \| `messages` \| `verbose` |
| `tyhp.diagnostics.enable` | Publish diagnostics to the Problems panel |
| `tyhp.completion.autoImport` | Auto-import (server-side; later UX) |
| `tyhp.binary.installMode` | `path` \| `global` \| `extension` |
| `tyhp.binary.autoUpdate` | Extension-only auto-update |
| `tyhp.binary.pinnedVersion` | Extension-only pinned release tag |
| `tyhp.xdebugProxy.idePort` | IDE listen port (PHP Debug). Explicit setting > `tyhp.json` `xdebugProxy.idePort` > `9003` |
| `tyhp.xdebugProxy.xdebugPort` | XDebug engine port (`client_port`). Explicit setting > `tyhp.json` `xdebugProxy.xdebugPort` > `9004` |
| `tyhp.xdebugProxy.sourceMapDir` | Optional `--sourcemap-dir`. Empty uses `tyhp.json` `xdebugProxy.sourceMapDir`, else CLI `output.path` |

## Uninstalling

If installed via symlink or copy, remove the folder from the extensions directory:

```bash
rm -rf ~/.cursor/extensions/tyhp-lang.tyhp-0.8.0
# or
rm -rf ~/.vscode/extensions/tyhp-lang.tyhp-0.8.0
```

If installed via VSIX:

```bash
cursor --uninstall-extension tyhp-lang.tyhp
# or
code --uninstall-extension tyhp-lang.tyhp
```
