# Tyhp Language — PhpStorm QA matrix

Story 19.5 Phase 14. Repeatable **local** checklist for `tyhp-lang/phpstorm/`. **Do not** publish to the JetBrains Marketplace as part of this matrix.

Product: **Tyhp Language** (`tyhp-lang`). Plugin ID: `com.tyhp.lang`. Artifact: `tyhp-lang-<version>.zip` (currently `tyhp-lang-0.6.0.zip`).

This file is the **parity counterpart** of [`tyhp-lang/vscode/QA.md`](../vscode/QA.md) (Phase 7). Same scenarios (TextMate, PATH / plugin-only binary, LSP, init, run configs, proxy, ZIP sideload, editor-side LSP feature list). Native PhpStorm UX — not a copy of VS Code menus.

Cursor uses the **VS Code VSIX**, not this plugin. There is no PhpStorm Cursor product.

## How to read this file (honesty)

| Mark | Meaning |
|------|---------|
| **Automated** | Covered by `./gradlew unitTest` (pure JVM tests under `src/unitTest/`). No PhpStorm window. |
| **CLI packaging** | `./gradlew unitTest` / `./gradlew buildPlugin`. Proves the ZIP exists and is a plugin distribution. Does **not** prove file types, highlighting, or LSP inside a window. |
| **Human GUI** | Must be done in PhpStorm (`./gradlew runIde` sandbox, or **Install Plugin from Disk** then restart). Do not check these off from a headless agent session unless a person actually used the IDE. |

Items below are **unsigned checkboxes for a human**. The “Verified in this environment” section at the bottom records what an agent or developer actually ran, without pretending GUI smoke happened.

`./gradlew runIde` launches a full PhpStorm 2026.2.1 sandbox (first run downloads the IDE + JDK 25 toolchain). It is **optional** for packaging acceptance. If it is too heavy for the machine under test, skip it and use the ZIP + Install Plugin from Disk path when a person can click the UI.

---

## Sideload (does not publish)

From `tyhp-lang/phpstorm/`:

```bash
./gradlew unitTest
./gradlew buildPlugin
# equivalent: ./package.sh
```

The task writes `build/distributions/tyhp-lang-0.6.0.zip` (version from `gradle.properties` `pluginVersion`).

### Install Plugin from Disk

A headless agent cannot easily complete this. A person should:

1. Open **PhpStorm 2026.2+** (minimum `since-build` `262`). IntelliJ IDEA is not a supported target.
2. **Settings → Plugins**.
3. Gear icon (⚙) next to the Plugins heading → **Install Plugin from Disk…**
4. Select `tyhp-lang/phpstorm/build/distributions/tyhp-lang-0.6.0.zip`.
5. Restart PhpStorm when prompted.

This sideload path does not use JetBrains Marketplace credentials. There is no supported `phpstorm --install-plugin` equivalent in this matrix.

Development alternative (not a Marketplace path): `./gradlew runIde` from this folder. See README **Development (`runIde`)**.

Uninstall: **Settings → Plugins → Installed → Tyhp Language → Uninstall**, then restart.

---

## Packaging acceptance

Run on the machine under test. Primary platform for this story: **macOS**.

- [ ] `./gradlew unitTest` exits 0 (unit tests; not a GUI substitute)
- [ ] `./gradlew buildPlugin` writes `build/distributions/tyhp-lang-0.6.0.zip`
- [ ] ZIP contains the plugin JAR / `plugin.xml` with id `com.tyhp.lang`, copied TextMate grammars under `textmate/tyhp/syntaxes/`, and file icons
- [ ] **Install Plugin from Disk** succeeds **or** the ZIP is built and install is left for a person (note which)
- [ ] After a GUI install, **Settings → Plugins → Installed** lists **Tyhp Language** (`com.tyhp.lang`)
- [ ] **No** JetBrains Marketplace upload, **no** `publishPlugin`, **no** Marketplace credentials used

Windows and Linux: same Gradle commands; not required to have been run for Phase 14 if macOS packaging succeeded. Record as follow-ups at the bottom.

---

## Phase 9 — Languages, TextMate, icons

Counterpart of VS Code Phase 2. **Human GUI** unless noted.

- [ ] Opening a `.tyhp` file selects file type **Tyhp** (not PHP)
- [ ] Opening a `.tyhpdef` file selects file type **Tyhp Definition** (same `Tyhp` language; distinct file type — native difference vs VS Code’s single language id `tyhp`)
- [ ] Keywords, comments, strings, numbers highlight on `tyhp-lang/vscode/samples/highlight-audit.tyhp` and `highlight-audit.tyhpdef`
- [ ] Markdown fenced `tyhp` / `tyhpdef` blocks highlight when the shipped TextMate injection is active (`samples/highlight-audit.md`). If PhpStorm’s Markdown editor does not apply the injection, note that; the grammar is still copied from the canonical VS Code `syntaxes/` tree
- [ ] Bracket matching for `{}`, `[]`, `()`, `<>` (`TyhpBraceMatcher` / language configuration)
- [ ] Comment toggle (macOS `Cmd+/`, Windows/Linux `Ctrl+/`) uses `//` line comments
- [ ] Distinct Project-view icons for `.tyhp` / `.tyhpdef` (light/dark variants from `tyhp-lang/vscode/media/`)
- [ ] Optional / native difference: PhpStorm does **not** offer a **PHP (Tyhp)** language mode. The shared PHP grammar is included by `source.tyhp`; `fileTypes: ["php"]` is stripped at Gradle copy time so TextMate does not steal `.php` from PhpStorm’s PHP highlighter. Confirm a `.php` file stays **PHP**

TextMate must still highlight if the language server is down (semantic tokens are additive). Optional: **Settings → Editor → TextMate Bundles** lists a **Tyhp** bundle from this plugin.

---

## Phase 10 — PATH binary, plugin-only binary

Counterpart of VS Code Phase 3 (`tyhp.path`, install modes). **Automated** coverage: `src/unitTest/kotlin/com/tyhp/lang/binary/*.kt`, `settings/SettingsCoreTest.kt` (PATH probe, install-mode policy, pin/auto-update rules, checksum helpers). **Human GUI** for **Tools → Tyhp** and the status bar.

### PATH binary

- [ ] With `tyhp.path` empty and `tyhp` on `PATH`, plugin start or **Tools → Tyhp → Refresh Tyhp binary** writes `tyhp.path`
- [ ] Explicit `tyhp.path` is used for the language server, run configurations, and XDebug proxy (`resolveTyhpBinary` — not a second PATH lookup ad hoc)
- [ ] Missing or non-file `tyhp.path` → status bar **CLI missing** + notification; **Install / Update CLI** remains available; plugin load does not crash

### Plugin-only vs global

- [ ] **Tools → Tyhp → Install / Update CLI** → **Global** installs to the documented user-global location (`~/.local/bin/tyhp` / `%LOCALAPPDATA%\Programs\tyhp\tyhp.exe`), sets `tyhp.path`, does **not** auto-update
- [ ] **Plugin only** installs under `{PhpStorm config}/tyhp-lang/cli/`, sets `tyhp.path` and `tyhp.binary.installMode` = `extension` (same setting name as VS Code)
- [ ] Plugin-only + `tyhp.binary.autoUpdate` checks GitHub Releases (debounced); pin via `tyhp.binary.pinnedVersion` skips other tags
- [ ] Global / PATH binaries are never auto-overwritten
- [ ] **Tools → Tyhp → Reveal CLI Path** shows the resolved file

GitHub may have no public compiler assets yet. Then install fails with a clear notification; set `tyhp.path` (**Settings → Tools → Tyhp**) to a locally built `tyhp` and continue the rest of the matrix.

---

## Phase 11 — LSP client (process + UX)

Counterpart of VS Code Phase 4. **Automated:** `lsp/LanguageServerArgsTest.kt`, `ProjectFileTest.kt`, `RestartBackoffTest.kt`. **Human GUI** for tool windows / Problems / editor features (next section).

- [ ] Opening a `.tyhp` / `.tyhpdef` file that a nested `tyhp.json` **includes** starts `tyhp language_server` for **that** project (`--tyhp-project` pointing at the owner file)
- [ ] Opening a `.tyhp` that matches **no** include stays TextMate-only (no language server)
- [ ] **Tyhp Language Server** tool window shows a per-project start line (set `tyhp.languageServer.trace` and/or **Help → Diagnostic Tools → Debug Log Settings…** → `#com.intellij.platform.lsp` if needed)
- [ ] Missing CLI: plugin still loads; notification + tool window explain the server was not started; status bar **CLI missing**; no crash
- [ ] Kill/crash of the language server process → auto-restart with backoff (tool window logs the delay)
- [ ] **Tools → Tyhp → Restart Language Server** restarts clients
- [ ] Changing `tyhp.path` / `tyhp.projectPath` / `tyhp.languageServer.args` restarts the server
- [ ] Closing the project / disabling the plugin stops the language server

---

## Phase 12 — Init, run configs, status bar

Counterpart of VS Code Phase 5 (tasks → **run configurations**). **Automated:** `workspace/GlobMatchTest.kt`, `ProjectIndexTest.kt`, `SelectOwnerTest.kt`, `ProjectDetectionTest.kt`, `InitGatingTest.kt`, `run/TyhpCliArgsTest.kt`, `status/StatusBarModelTest.kt`. **Human GUI** for notifications and Run.

- [ ] Nested `runtime/packages/core/tyhp.json` owns `tyhp_src/Type.tyhp`; content-root `tyhp.json` with `"include": []` owns nothing
- [ ] `tyhp.projectPath` forces that one project and skips scanning others
- [ ] Opening a Tyhp file with **no owner and no ancestor `tyhp.json`** offers **Initialize Project**; an ancestor that does not include the file does **not** prompt
- [ ] **Tools → Tyhp → Initialize Project** works without the notification
- [ ] **Run → Edit Configurations… → + → Tyhp** lists `tyhp build` and `tyhp lint` against the resolved binary; `--tyhp-project` is the **active file’s owner**
- [ ] Status bar shows owner folder name or **not in a Tyhp project**, LSP **ready** / **starting** / **error** / **stopped**, CLI health
- [ ] Click status bar → Restart Language Server, Install CLI, Reveal CLI Path, Initialize Project (when the active file has no owner), Start/Stop/Restart XDebug Proxy (gated by proxy state), Create PHP Remote Debug Configuration

---

## Phase 13 — XDebug proxy

Counterpart of VS Code Phase 6. **Automated:** `debug/ProxyConfigTest.kt`, `TyhpJsonTest.kt`, `ProxyGuidanceTest.kt`, `ProxyLifecycleTest.kt`, `ProxyProcessControllerTest.kt`, `TyhpDebugConfigurationTest.kt`. **Human GUI** for a live debug hit.

Ports and sourcemap dir: an **explicit stored** `tyhp.xdebugProxy.*` setting wins; otherwise `tyhp.json` `xdebugProxy`; otherwise defaults **9003** (IDE) and **9004** (XDebug). Empty settings fields must **not** shadow `tyhp.json`.

VS Code’s “PHP Debug (`xdebug.php-debug`) missing” row maps to PhpStorm’s **built-in** PHP Remote Debug (no marketplace adapter). Guidance still exists if the configuration type is unavailable.

- [ ] **Tools → Tyhp → Start XDebug Proxy** starts `tyhp xdebug_proxy` with the resolved CLI; **Tyhp XDebug Proxy** tool window logs start
- [ ] Status bar shows proxy listening while it is up
- [ ] **Stop XDebug Proxy** stops the process and releases the ports
- [ ] **Restart XDebug Proxy** works
- [ ] **Create PHP Remote Debug Configuration** (or **Run → Edit Configurations… → + → PHP Remote Debug**) produces **Listen for Tyhp (XDebug proxy)**; PhpStorm **Settings → PHP → Debug → Xdebug → Debug port** is the proxy **IDE port** (default **9003**)
- [ ] PHP Remote Debug type missing → actionable guidance (`phpRemoteDebugMissingGuidance`); PhpStorm normally bundles this
- [ ] No sourcemaps / `generateSourcemap` off / proxy down → guidance (notification / tool window / `proxyDownGuidance` when creating the debug config), not a silent miss
- [ ] **Minimal debug sample (human):** `build.generateSourcemap: true`, run config `tyhp build`, start proxy, listen on IDE port, XDebug `client_port` = proxy XDebug port, breakpoint in `.tyhp` hits Tyhp source

---

## Editor-side LSP feature list

Counterpart of the VS Code Phase 7 table. Story 19 already proves these over stdin/stdout. This list proves them **through the plugin**. All **Human GUI**. Use a small fixture project with `tyhp.json` and a valid `tyhp.path`.

Story 19 **does** advertise semantic tokens (full + delta), so include that row.

| Feature | How to check (PhpStorm) | Pass? |
|---------|-------------------------|-------|
| Activation | Open `.tyhp` → plugin / Language Services lists **Tyhp** | [ ] |
| Tool window | **Tyhp Language Server** has a start line / traffic | [ ] |
| Syntax highlighting | TextMate on `.tyhp` / `.tyhpdef` (Phase 9) | [ ] |
| Brackets / comments | matching + `Cmd+/` / `Ctrl+/` | [ ] |
| Diagnostics | Broken `.tyhp` → **Problems** (unless `tyhp.diagnostics.enable` is off) | [ ] |
| Go to Definition | **Go to Declaration** (`Cmd+B` / `Ctrl+B`) on a known symbol | [ ] |
| Hover | Hover a symbol → type/doc from the server | [ ] |
| Autocomplete | Basic completion (`Ctrl+Space`); trigger chars if the server sends them | [ ] |
| Find references | **Find Usages** on a symbol | [ ] |
| Rename | **Rename** (`Shift+F6`) (server `prepare` + rename) | [ ] |
| Outline | **Structure** tool window (document symbols) | [ ] |
| Signature help | **Parameter Info** (`Cmd+P` / `Ctrl+P`) inside a call | [ ] |
| Code actions | Intention bulb / `Alt+Enter` (and organize imports if offered) | [ ] |
| Semantic tokens | With LSP up, semantic highlighting is additive on top of TextMate | [ ] |

Also advertised by Story 19 (optional extra GUI; not required to call Phase 14 “checklist written”): document highlight, folding range, format document, selection range, workspace symbols.

---

## Cursor compatibility

Not this plugin. Cursor sideload of `tyhp-*.vsix` is documented in [`tyhp-lang/vscode/QA.md`](../vscode/QA.md). PhpStorm has no Cursor counterpart row beyond this pointer.

---

## Windows / Linux follow-ups

Not a Phase 14 blocker when macOS packaging succeeded.

- [ ] Windows: `./gradlew unitTest` / `./gradlew buildPlugin` / Install Plugin from Disk
- [ ] Linux: same
- [ ] Windows PATH / `%LOCALAPPDATA%\Programs\tyhp\tyhp.exe` global install path (Phase 10)
- [ ] Linux `~/.local/bin/tyhp` global install path (Phase 10)

---

## VS Code Phase 2–6 scenario map

Every VS Code checklist scenario has a PhpStorm row above. Native UX names:

| VS Code (Phase 7) | PhpStorm (this file) |
|-------------------|----------------------|
| Language id `tyhp` for `.tyhp` and `.tyhpdef` | File types **Tyhp** / **Tyhp Definition** |
| Optional language **PHP (Tyhp)** on `.php` | Intentionally omitted; `.php` stays PhpStorm PHP |
| Command Palette **Tyhp: …** | **Tools → Tyhp → …** (and status-bar popup) |
| Output **Tyhp Language Server** | Tool window **Tyhp Language Server** |
| Output **Tyhp XDebug Proxy** | Tool window **Tyhp XDebug Proxy** |
| Terminal **Run Task…** `tyhp: build` / `tyhp: lint` | Run configurations **Tyhp** `tyhp build` / `tyhp lint` |
| Launch **Listen for Tyhp (XDebug proxy)** type `php` | PHP Remote Debug **Listen for Tyhp (XDebug proxy)** |
| PHP Debug extension `xdebug.php-debug` | Built-in PHP Remote Debug |
| Install from VSIX | Install Plugin from Disk |
| Extension-only CLI under `globalStorage` | Plugin-only CLI under `{PhpStorm config}/tyhp-lang/cli/` |

---

## Verified in this environment (do not fake GUI)

Recorded **2026-08-19** on macOS (darwin 25). Phase 14 “signed off” in the story plan means: this checklist exists, `unitTest` works, and a plugin ZIP can be built. It does **not** mean every GUI row above was clicked.

| Check | Result |
|-------|--------|
| Host OS | macOS (primary). Windows/Linux **not** run — follow-ups above. |
| `./gradlew unitTest` | **Pass** (115 tests, 0 failures, 0 skipped, exit 0). Policy/argv only — no IDE, no `tyhp language_server` process. Gradle JVM: Homebrew OpenJDK 17 (`JAVA_HOME=/opt/homebrew/opt/openjdk@17`). Compilation toolchain remains **JDK 25** (`jvmToolchain(25)`). |
| `./gradlew buildPlugin` | **Pass.** Artifact: `build/distributions/tyhp-lang-0.6.0.zip` (~358 KB) |
| ZIP contents | **OK.** ZIP has `tyhp-lang/lib/tyhp-lang-0.6.0.jar` (`plugin.xml` id `com.tyhp.lang`), `textmate/tyhp/syntaxes/` (`tyhp.tmLanguage.json`, `tyhp-php.tmLanguage.json`, `tyhp-markdown.tmLanguage.json`, `language-configuration.json`), and JAR `icons/` (`tyhp-file.svg`, `tyhp-file_dark.svg`, `tyhpdef-file.svg`, `tyhpdef-file_dark.svg`). |
| Install Plugin from Disk | **Not run.** No PhpStorm window was used. Packaging only proves the ZIP exists. Steps are in this file and the README. |
| `./gradlew runIde` | **Not run.** Too heavy for this headless session (downloads PhpStorm 2026.2.1). Documented in README; a person can use it for GUI rows. |
| JetBrains Marketplace publish | **Not performed** |
| Human GUI (file types, highlighting, LSP attach, hover, diagnostics, init prompt, run configs UI, live XDebug hit) | **Not run.** No IDE window was used. A person still needs `runIde` or a reload after Install Plugin from Disk. |

Unit tests are helpers for Phases 10–13 policy and argv. They do not start PhpStorm, do not spawn `tyhp language_server`, and do not prove hover or breakpoints.
