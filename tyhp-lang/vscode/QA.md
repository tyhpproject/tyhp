# Tyhp Language — VS Code / Cursor QA matrix

Story 19.5 Phase 7. Repeatable **local** checklist for `tyhp-lang/vscode/`. **Do not** publish to the VS Code Marketplace or Open VSX as part of this matrix.

Product: **Tyhp Language** (`tyhp-lang`). Extension ID: `tyhp-lang.tyhp`. Artifact: `tyhp-<version>.vsix` (currently `tyhp-0.8.0.vsix`).

Cursor uses the **same VSIX**. There is no separate Cursor product.

PhpStorm QA is **not** this file (`tyhp-lang/phpstorm/` is a different track).

## How to read this file (honesty)

| Mark | Meaning |
|------|---------|
| **Automated** | Covered by `npm test` (Node unit tests under `src/**/*.test.ts`) or by packaging commands in this repo. No editor GUI. |
| **CLI packaging** | `npm run compile` / `npm run package` / optional `code` or `cursor --install-extension`. Proves the VSIX exists and the CLI can register the extension. Does **not** prove language mode, highlighting, or LSP inside a window. |
| **Human GUI** | Must be done in VS Code or Cursor (F5 Extension Development Host, or a sideloaded VSIX). Do not check these off from a headless agent session unless a person actually used the editor. |

Items below are **unsigned checkboxes for a human**. The “Verified in this environment” section at the bottom records what an agent or developer actually ran, without pretending GUI smoke happened.

---

## Sideload (does not publish)

From `tyhp-lang/vscode/`:

```bash
npm install
npm run compile
npm run package
# equivalent: npx vsce package
```

`vscode:prepublish` compiles TypeScript, then esbuild-bundles `src/extension.ts` into `out/extension.js`. The VSIX must ship that **bundle**, not the extra per-module `out/<folder>/*.js` files `tsc` also writes.

Install the VSIX (pick one editor):

```bash
code --install-extension tyhp-0.8.0.vsix
cursor --install-extension tyhp-0.8.0.vsix
```

Or **Extensions** view → `…` → **Install from VSIX…**.

Development alternative (not a Marketplace path): open `tyhp-lang/vscode` as the workspace and press **F5** (Extension Development Host). See README **Development (compile + F5)**.

Uninstall:

```bash
code --uninstall-extension tyhp-lang.tyhp
cursor --uninstall-extension tyhp-lang.tyhp
```

If a **symlink** install already exists (`~/.vscode/extensions/tyhp-lang.tyhp-<version>` or `~/.cursor/extensions/…`), do not blindly `--install-extension` over it — that can replace the symlink with a packed copy. Use F5 or the symlink workflow in the README instead.

---

## Packaging acceptance

Run on the machine under test. Primary platform for this story: **macOS**.

- [ ] `npm run compile` exits 0
- [ ] `npm test` exits 0 (unit tests; not a GUI substitute)
- [ ] `npm run package` writes `tyhp-0.8.0.vsix` (version from `package.json`)
- [ ] VSIX contains `out/extension.js` (esbuild bundle) and does **not** contain `out/binary/`, `out/config/`, `out/debug/`, `out/lsp/`, `out/status/`, `out/tasks/`, or `out/workspace/` JS
- [ ] `code --install-extension tyhp-0.8.0.vsix` succeeds **or** Install from VSIX works, **or** the `code` CLI is missing and install is manual (note which)
- [ ] Cursor: `cursor --install-extension tyhp-0.8.0.vsix` **or** Install from VSIX, same VSIX (compatibility only)
- [ ] After install, `code --list-extensions` / `cursor --list-extensions` includes `tyhp-lang.tyhp` (CLI proof only)
- [ ] **No** `vsce publish`, **no** Open VSX upload, **no** Marketplace submission

Windows and Linux: same commands; not required to have been run for Phase 7 if macOS packaging succeeded. Record as follow-ups at the bottom.

---

## Phase 2 — Languages, TextMate, icons

**Human GUI** unless noted.

- [ ] Opening a `.tyhp` file selects language **Tyhp** (`tyhp`)
- [ ] Opening a `.tyhpdef` file also selects **Tyhp** (there is no separate `tyhpdef` language id)
- [ ] Keywords, comments, strings, numbers highlight on `samples/highlight-audit.tyhp` and `samples/highlight-audit.tyhpdef`
- [ ] Markdown fenced `tyhp` / `tyhpdef` blocks highlight (`samples/highlight-audit.md`)
- [ ] Bracket matching for `{}`, `[]`, `()`, `<>` (`language-configuration.json`)
- [ ] Comment toggle (macOS `Cmd+/`, Windows/Linux `Ctrl+/`) uses `//` line comments
- [ ] File icons appear for `.tyhp` / `.tyhpdef` in the explorer (language icons; optional **Tyhp File Icons** theme)
- [ ] Optional: set language mode **PHP (Tyhp)** on a `.php` file and confirm the standalone PHP grammar

TextMate must still highlight if the language server is down (semantic tokens are additive).

---

## Phase 3 — PATH binary, extension-only binary

**Automated** coverage: `src/binary/*.test.ts`, `src/config/settingsCore.test.ts` (PATH probe, install-mode policy, pin/auto-update rules, checksum helpers). **Human GUI** for the commands and status bar.

### PATH binary

- [ ] With `tyhp.path` empty and `tyhp` on `PATH`, activation or **Tyhp: Refresh Tyhp binary** writes `tyhp.path`
- [ ] Explicit `tyhp.path` is used for the language server, tasks, and XDebug proxy (not a second PATH lookup ad hoc)
- [ ] Missing or non-file `tyhp.path` → status bar error; **Tyhp: Install / Update CLI** remains available; activation does not crash

### Extension-only vs global

- [ ] **Tyhp: Install / Update CLI** → **Global** installs to the documented user-global location, sets `tyhp.path`, does **not** auto-update
- [ ] **Extension only** installs under extension `globalStorage`, sets `tyhp.path` and `tyhp.binary.installMode` = `extension`
- [ ] Extension-only + `tyhp.binary.autoUpdate` checks GitHub Releases (debounced); pin via `tyhp.binary.pinnedVersion` skips other tags
- [ ] Global / PATH binaries are never auto-overwritten
- [ ] **Tyhp: Reveal CLI Path** opens the resolved file

GitHub may have no public compiler assets yet. Then install fails with a clear error; set `tyhp.path` to a locally built `tyhp` and continue the rest of the matrix.

---

## Phase 4 — LSP client (process + UX)

**Automated:** `src/lsp/serverArgs.test.ts`, `projectPath.test.ts`, `restartBackoff.test.ts`, `sessionOwner.test.ts` (via `documentRouting.test.ts`). **Human GUI** for Output / Problems / editor features (next section).

- [ ] Opening a `.tyhp` file that a nested `tyhp.json` **includes** starts `tyhp language_server` for **that** project (`--tyhp-project` pointing at the owner file, not a workspace-root empty-include `tyhp.json`)
- [ ] Opening a `.tyhp` that matches **no** include stays TextMate-only (no language server)
- [ ] Two open files in two owned projects start **two** servers; hover/definition on each file talks to its owner only
- [ ] Closing the last Tyhp document of a project idle-stops that server
- [ ] **Output** panel → **Tyhp Language Server** shows a per-project start line and `Language server is running.` (set `tyhp.languageServer.trace` to `messages` or `verbose` if needed)
- [ ] Missing CLI: extension still activates; Output explains the server was not started; status bar **CLI missing**; no crash
- [ ] Kill/crash of a language server process that had reached Running → auto-restart with backoff (Output logs the delay). A process that never reached Running is **not** restarted in a loop
- [ ] **Tyhp: Restart Language Server** restarts running sessions and ensures the active document
- [ ] Changing `tyhp.path` / `tyhp.projectPath` / `tyhp.languageServer.args` restarts sessions
- [ ] Closing the window / disabling the extension stops language servers (`deactivate`)

---

## Phase 5 — Init, tasks, status bar

**Automated:** `src/workspace/globMatch.test.ts`, `projectIndex.test.ts`, `selectOwner.test.ts`, `initGating.test.ts`, `projectDetection.test.ts`, `src/tasks/taskArgs.test.ts`, `src/status/statusBarModel.test.ts`. **Human GUI** for prompts and Terminal tasks.

- [ ] Nested `runtime/packages/core/tyhp.json` owns `tyhp_src/Type.tyhp`; workspace-root `tyhp.json` with `"include": []` owns nothing
- [ ] `tyhp.projectPath` forces that one project and skips scanning others; files outside its include are TextMate-only
- [ ] Opening a Tyhp file with **no owner and no ancestor `tyhp.json`** prompts **Tyhp: Initialize Project**; an ancestor that does not include the file does **not** prompt
- [ ] Command Palette **Tyhp: Initialize Project** works without the prompt
- [ ] **Terminal → Run Task…** lists `tyhp: build` and `tyhp: lint` against the resolved binary; `--tyhp-project` is the **active file’s owner**
- [ ] Status bar shows owner folder name or **not in a Tyhp project**, LSP **ready** / **starting** / **error**, CLI health
- [ ] Click status bar → Restart Language Server, Install CLI, Reveal CLI Path, Initialize Project (when the active file has no owner), Start/Stop/Restart XDebug Proxy

---

## Phase 6 — XDebug proxy

**Automated:** `src/debug/proxyConfig.test.ts`, `tyhpJson.test.ts`, `proxyGuidance.test.ts`, `proxyLifecycle.test.ts`, `ProxyProcessController.test.ts`, `waitForTcp.test.ts`. **Human GUI** for a live debug hit.

Ports and sourcemap dir: an **explicit** `tyhp.xdebugProxy.*` setting wins; otherwise `tyhp.json` `xdebugProxy`; otherwise defaults **9003** (IDE) and **9004** (XDebug). Empty settings must **not** shadow `tyhp.json` (do not treat `package.json` defaults as “user set”).

- [ ] **Tyhp: Start XDebug Proxy** starts `tyhp xdebug_proxy` with the resolved CLI; **Output** → **Tyhp XDebug Proxy**
- [ ] Status bar shows proxy listening while it is up
- [ ] **Tyhp: Stop XDebug Proxy** stops the process and releases the ports
- [ ] **Tyhp: Restart XDebug Proxy** works
- [ ] Launch snippet / config **Listen for Tyhp (XDebug proxy)** uses type `php` and port **9003** (or the configured IDE port)
- [ ] PHP Debug (`xdebug.php-debug`) missing → actionable install guidance
- [ ] No sourcemaps / `generateSourcemap` off / proxy down → guidance (Output / messages), not a silent miss
- [ ] **Minimal debug sample (human):** `build.generateSourcemap: true`, task `tyhp: build`, start proxy, listen on IDE port, XDebug `client_port` = proxy XDebug port, breakpoint in `.tyhp` hits Tyhp source

---

## Editor-side LSP feature list

Story 19 already proves these over stdin/stdout. This list proves them **through the extension**. All **Human GUI**. Use a small fixture project with `tyhp.json` and a valid `tyhp.path`.

Story 19 **does** advertise semantic tokens (full + delta), so include that row.

| Feature | How to check | Pass? |
|---------|----------------|-------|
| Activation | Open `.tyhp` → extension activates | [ ] |
| Output panel | **Tyhp Language Server** channel has traffic / “running” | [ ] |
| Syntax highlighting | TextMate on `.tyhp` / `.tyhpdef` (Phase 2) | [ ] |
| Brackets / comments | matching + `Cmd+/` / `Ctrl+/` | [ ] |
| Diagnostics | Broken `.tyhp` → **Problems** panel (unless `tyhp.diagnostics.enable` is off) | [ ] |
| Go to Definition | `F12` / Go to Definition on a known symbol | [ ] |
| Hover | Hover a symbol → type/doc from the server | [ ] |
| Autocomplete | Trigger chars `$`, `>`, `:`, `\`, `<`, `(` or Ctrl+Space | [ ] |
| Find references | Find All References on a symbol | [ ] |
| Rename | Rename symbol (server `prepare` + rename) | [ ] |
| Outline | **Outline** view (document symbols) | [ ] |
| Signature help | Type `(` or `,` inside a call | [ ] |
| Code actions | Lightbulb / quick fix (and organize imports if offered) | [ ] |
| Semantic tokens | With LSP up, semantic highlighting is additive on top of TextMate | [ ] |

Also advertised by Story 19 (optional extra GUI; not required to call Phase 7 “checklist written”): document highlight, folding range, format document, selection range, workspace symbols.

---

## Cursor compatibility (same VSIX)

Not a second product. After sideloading `tyhp-0.8.0.vsix` in Cursor:

- [ ] `.tyhp` opens as language **Tyhp**
- [ ] Output **Tyhp Language Server** shows the client attached (same as VS Code)

F5 from this folder in Cursor is an acceptable smoke if VSIX sideload would overwrite a symlink install.

---

## Windows / Linux follow-ups

Not a Phase 7 blocker when macOS packaging succeeded.

- [ ] Windows: `npm run compile` / `npm run package` / `code --install-extension` (or Install from VSIX)
- [ ] Linux: same
- [ ] Windows PATH / `%LOCALAPPDATA%\Programs\tyhp\tyhp.exe` global install path (Phase 3)
- [ ] Linux `~/.local/bin/tyhp` global install path (Phase 3)

---

## Verified in this environment (do not fake GUI)

Recorded **2026-08-19** on macOS (darwin 25). Phase 7 “signed off” in the story plan means: this checklist exists, compile works, and a VSIX can be built. It does **not** mean every GUI row above was clicked.

| Check | Result |
|-------|--------|
| Host OS | macOS (primary). Windows/Linux **not** run — follow-ups above. |
| `npm run compile` | **Pass** (`tsc -p ./`, exit 0) for 0.8.0 |
| `npm test` | **Pass** (146 unit tests, exit 0) for 0.8.0 multi-project membership / owner / init gating / status copy. Policy/argv/index only — no editor, no `tyhp language_server` process. |
| `npm run package` | **Pass** (`tyhp-0.8.0.vsix`, 19 files, 166.54 KB). `extension/out/` contains only `extension.js` (esbuild bundle). |
| VSIX extra `out/<module>/*.js` | **Pass** for 0.8.0 — only `extension/out/extension.js`. |
| `code` / `cursor` CLI sideload | **Not re-run for 0.8.0.** Install `tyhp-0.8.0.vsix`, fully quit the editor, then **Tyhp: Restart Language Server**. |
| Marketplace / Open VSX publish | **Not performed** |
| Human GUI (language mode, highlighting, Output/LSP attach, hover, diagnostics, init prompt, tasks UI, live XDebug hit, nested `runtime/packages/core` LSP) | **Not run.** A person still needs F5 or a reload after VSIX/symlink install to confirm language mode + per-project LSP attach. |

**Cursor leftovers (not this VSIX):** `tyhp-lang.tyhp-0.3.0` (older same-id folder) and `tyhp-lang.tyhp-language` 0.2.0 / 0.2.1 (former extension `name`, symlinks to `tyhp-vscode`). Disable/remove those if they steal `.tyhp` or confuse the status bar.

Unit tests are helpers for Phases 3–6 policy, glob membership, and argv. They do not start VS Code, do not spawn `tyhp language_server`, and do not prove hover or breakpoints.
