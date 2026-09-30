# Implementation Plan: Story 21.5 — Composer Toolchain Integration

> **Roadmap position:** Story 21.5 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story **21.1**, before Story 22). **Implement after 21.1.**
> **Direct dependencies (new numbering):** 13 (CLI `init` / `composer` proxy), 10 (`ComposerJsonService` / `build.updateComposer`), 19.5 (CLI binary download — share, do not fork), **20** (`generate_tyhpdef` Track B `--package-path`), 21 (runtime packages on Packagist; compiler package can be authored before publish), **21.1** (tyhpdef `extern` — Track B `--vendor` must emit placeholders for optional peers)
> **New story:** make Composer the familiar on-ramp for PHP developers — `tyhp install composer`, Composer-aware `tyhp init` (shape A + merge), `vendor/bin/tyhp` plus a root `scripts.tyhp` entry, and a `tyhp/compiler` package that fetches the native CLI.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-08-27
> **Last design lock:** 2026-08-27 — `--vendor` and **`tyhp init`** pin `tyhpdef/php` / `tyhp/core` (and `--vendor` also `async` / `lambda` / `decimal`) as **`@dev`**; `tyhpdef/php-ext-*` `@dev` require-dev; generate into `vendor-tyhpdef/`
> **Status:** **Grill locked.** Remaining questions below are implementation details, not audience/shape. Still **do not implement** until a later pass turns phases into work.
> **Prerequisites:** Story 13 (`InitAction`, `ComposerAction`, `ExternalToolLocator`); Story 10 (`ComposerJsonService`); GitHub-release installer at `scripts/install.sh` / `scripts/install.ps1`; **Story 21.1** (`extern` Track B emit).
> **Consumers:** Story 30 (docs/polish — Packagist + install pages); PHP-first adopters; CI that already knows `composer` and not `tyhp`. **Blocked on 21.1** for loadable `--vendor` output when a library type-hints `suggest` / `require-dev` / optional `ext-*` types.

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [What already exists (do not reinvent)](#what-already-exists-do-not-reinvent)
- [Scope (In / Out)](#scope-in--out)
- [Two audiences](#two-audiences)
- [Decisions (locked)](#decisions-locked)
- [Surface](#surface)
- [Init merge rules](#init-merge-rules)
- [Compiler package (`tyhp/compiler`)](#compiler-package-tyhpcompiler)
- [Vendor tyhpdef generation](#vendor-tyhpdef-generation)
- [Out of scope](#out-of-scope)
- [Phases](#phases)
- [Grill record](#grill-record)
- [Remaining questions](#remaining-questions)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

PHP developers already live in Composer. This story is **toolchain glue**, not a new language feature.

1. **`tyhp install composer`** — download and install Composer ([official installer](https://getcomposer.org/download/)). First member of `tyhp install <something>`. Distinct from `tyhp composer install` (proxy Composer’s `install`).
2. **`tyhp init`** — **shape A:** keep the Tyhp-authored `composer.json` template; **do not** run `composer init`. Enrich it with `scripts` / `scripts-descriptions`. If `composer.json` already exists (existing PHP app), **merge** Tyhp requires and the `tyhp` script instead of skipping or overwriting the file.
3. **Invocation** — `vendor/bin/tyhp` from the compiler package’s `"bin"`, plus a root `"scripts": { "tyhp": … }` entry so `composer tyhp …` forwards. **No** Composer plugin in v1.
4. **`tyhp/compiler`** — PHP shim + native download. Composer **does not run dependency scripts**, so automatic download is a **root** `post-autoload-dump` step (`vendor/bin/tyhp --install-binary`, idempotent). A later normal shim invoke with no native compiler binary **does not** download; it errors and tells the user to run `--install-binary`.
5. **Vendor tyhpdef generation** — `tyhp generate_tyhpdef --vendor` after Composer has populated `vendor/`. Ensure runtime `tyhp/*` requires; prefer published companions / `tyhpdef/php-ext-*`; else generate into **`vendor-tyhpdef/`** (not `tyhpdef/vendor/`). Do not hand-edit generated files — use overlays.

---

## Motivation

Today the documented install path is a curl script or a GitHub Release asset. Docs already say there is **no** `composer global require tyhp/compiler` package. PHP developers expect:

```bash
composer require --dev tyhp/compiler
vendor/bin/tyhp init
vendor/bin/tyhp build
composer tyhp build
```

The inverse gap: a developer who already has the Tyhp binary still needs Composer for `tyhpdef/php` / `tyhp/core`. `tyhp composer` currently **errors** if Composer is missing. `tyhp install composer` is the Tyhp-first complement.

---

## What already exists (do not reinvent)

This story extends shipped CLI; it does not replace it.

| Surface | Today |
|---------|--------|
| `tyhp init` | Writes `tyhp.json`, `src/index.tyhp`, **and a Tyhp-authored `composer.json`** (`app/tyhp-project`, `require.php`, `tyhpdef/php`, `tyhp/core`). Skips any scaffold file that already exists. Does **not** invoke `composer init`. Aborts if `tyhp.json` already exists. |
| `tyhp composer <args>` | Proxies Composer. After `require` / `install` / `update`, **placeholder** tyhpdef message (`PLACEHOLDER_STORY_20`) — this story **fills** that hook by calling `generate_tyhpdef --vendor`. |
| `tyhp generate_tyhpdef --package-path` | Track B for **one** Composer package directory. No `--vendor` mode yet. Does not write `extra.tyhp.package`. Default output `{project}/tyhpdef/`. |
| Vendor `extra.tyhp.package` | Compiler auto-loads `vendor/*/*/composer.json` (`extra.tyhp.package`) (Story 06). Packages **without** that object are invisible to the type checker. |
| Init `tyhpdef/` | Folder is created; **`tyhpdefInclude` is not set**, so generated project stubs would not load until this story sets the glob. |
| `ComposerJsonService` | Build-output `composer.json` merge, runtime package requires, path repos. Reuse merge patterns where they fit **root** `composer.json`; do not confuse output-dir manifests with the project root file. |
| `scripts/install.sh` / `install.ps1` | Download **Tyhp** from GitHub Releases into `~/.local/bin`. |
| Story 19.5 BinaryManager | IDE clients also download the Tyhp CLI. Share checksum / asset-naming policy with the compiler package. |
| `PhpRuntimeManager` | Story 20 managed PHP for tyhpdef generation — **not** `tyhp install php`. |
| Integrity check | Reports whether Composer is found (informational). |
| Docs | `docs/content/intro_installation.md` — no Composer compiler package in this alpha. |

**`tyhp composer install`** already means “run Composer’s `install`.” **`tyhp install composer`** means “install the Composer tool.” Keep both; document the difference in help.

---

## Scope (In / Out)

| In scope | Out of scope |
|----------|----------------|
| `tyhp install` action; first target `composer` | Replacing `tyhp composer` proxy |
| Official Composer installer + SHA-384 verify | Custom Composer mirror |
| Shape A init template + merge into existing root `composer.json` | Interactive / non-interactive `composer init` |
| Root `scripts.tyhp` + `scripts-descriptions.tyhp` | Composer plugin / CommandProvider |
| Root `post-autoload-dump`: `--install-binary` (idempotent) then `generate_tyhpdef --vendor` | Root `post-install-cmd` that runs `tyhp build` / `tyhp lint` |
| `tyhp generate_tyhpdef --vendor` (scan installed packages, companions, project stubs) | Writing generated tyhpdefs into `vendor/` |
| Init sets `tyhpdefInclude` for `vendor-tyhpdef/` plus `overlay` for hand `tyhpdef/` | Replacing Story 21’s published `tyhpdef/php` / `tyhpdef/php-ext-*` **contents** (we **require** those packages when they exist) |
| `tyhp/compiler`: PHP shim, `"bin"`, `--install-binary` | Shipping the C# compiler as a `.phar`; relying on **dependency** `scripts` (Composer ignores them) |
| Shared download/verify policy with `install.sh` / Story 19.5 | `tyhp install php` |
| Docs: install page, init, FAQ, vendor tyhpdefs | Flex recipes; Tyhp Link autoload replacement (Story 31) |
| Checksums, pin, failure UX | Silent auto-update of a global Composer or Tyhp binary |

---

## Two audiences

Both are **in this story** (not a later slice).

```
Tyhp-first                         PHP-first
  (already has `tyhp` on PATH)       (already has `composer` + PHP)
         │                                    │
         ▼                                    ▼
  tyhp install composer              composer require --dev tyhp/compiler
  tyhp init                          vendor/bin/tyhp init
  composer install                   vendor/bin/tyhp build
  tyhp build                         composer tyhp build
```

The compiler package is how PHP-first people get Tyhp **without** a prior global binary. `tyhp install composer` is how Tyhp-first people get Composer **without** leaving the CLI they already learned.

---

## Decisions (locked)

| Topic | Decision |
|-------|----------|
| Audiences | **Both** in this story: `tyhp install composer` + init scripts **and** Packagist `tyhp/compiler` |
| Init vs `composer init` | **Shape A.** Tyhp writes / merges `composer.json`. Never invoke `composer init`. |
| `tyhp install` verb | Keep **`tyhp install composer`**. `tyhp composer install` stays the Composer proxy. `install` is a family for future targets. |
| Plugin | **No CommandProvider** in 21.5. Story **21.10** adds a `PluginInterface` on `tyhp/core` for `extra.tyhp.require` sync (`allow-plugins.tyhp/core`). Still no Composer commands from the plugin. |
| How to run Tyhp via Composer | **`vendor/bin/tyhp`** (package `"bin"`) **and** a root **`scripts.tyhp`** entry that forwards extra args (`composer tyhp build` → `vendor/bin/tyhp build`). Do not take `build` / `lint` as root script names (avoids clobbering webpack/phpunit on merge). |
| Native binary fetch | **`--install-binary`** is the only download path on the shim. **Automatic** means the **root** `post-autoload-dump` runs `vendor/bin/tyhp --install-binary` (idempotent if the binary is already present). Do **not** put this only on `tyhp/compiler`’s own `scripts` — Composer executes **root-package scripts only**. |
| Shim when binary missing | If the native compiler binary is **not available**, a normal shim invoke (**not** `--install-binary`) **errors and stops**. Message must tell the user to re-run with `--install-binary`. The shim must **not** download, prompt, or retry on `build` / `lint` / `init` / etc. |
| Vendor tyhpdefs | **In this story.** `generate_tyhpdef --vendor`. Ensure runtime requires; prefer `tyhpdef/<vendor>-<name>` and `tyhpdef/php-ext-*`; else generate into **`vendor-tyhpdef/`**. See [Vendor tyhpdef generation](#vendor-tyhpdef-generation). |
| Composer hook | Fill `PLACEHOLDER_STORY_20`: `tyhp composer` `require` / `install` / `update` (success, no `--no-tyhpdef`) runs the same `--vendor` mode. Init also wires root `post-autoload-dump` so plain `composer install` does it. |
| Generated vs hand tyhpdefs | Generated files live only under **`vendor-tyhpdef/`**. Developers **must not** edit them. Lasting fixes are **overlay** tyhpdefs (project `tyhp.json` `overlay` / `tyhp overlay create`). Each generated file carries a header that says so. |
| `tyhpdefInclude` / overlay | Init writes `"tyhpdefInclude": ["./vendor-tyhpdef/**/*.tyhpdef"]` and `"overlay": ["./tyhpdef/**/*.tyhpdef"]` (merge if missing). `tyhpdef/` stays the hand-authored / overlay tree (init still creates that folder). |
| Existing PHP app | If `tyhp.json` is absent and `composer.json` is present: **merge** (see [Init merge rules](#init-merge-rules)). Still abort if `tyhp.json` already exists. |
| Compiler package name | `tyhp/compiler` |
| App template `require-dev` | `tyhp/compiler` is **require-dev** (compile machine). Runtime packages stay in `require`. |
| Init runtime pins | Greenfield `composer.json` uses **`@dev`** for `tyhpdef/php` and `tyhp/core` (same as `--vendor`). The `php` language constraint (`{{PHP_CONSTRAINT}}` / `--php-version`) is unchanged. Do not use `EncodeRuntimePackageVersion` / `80N.x` for those two packages on init. |
| App compile-on-install | Root scripts do **not** run `tyhp build` / `tyhp lint`. Root **`post-autoload-dump`** does run `--install-binary` (idempotent) then `generate_tyhpdef --vendor`. |
| Plugin hooks | **21.5:** not used for vendor tyhpdefs (CLI `--vendor` + root scripts + `tyhp composer` proxy). **21.10:** `PRE_DEPENDENCIES_SOLVING` on `tyhp/core` for `extra.tyhp.require` → root `require-dev` only. |

---

## Surface

### 1. `tyhp install composer`

New `Action.install`. Subcommand in remaining argv (same pattern as `ComposerAction`):

```bash
tyhp install composer
tyhp install composer --global
tyhp install composer --local
tyhp install --help
```

Unknown targets: error + list known targets (`composer`). Future targets are help stubs, not invented commands.

Mechanics:

1. Require `php` on PATH (Composer is a PHAR).
2. Download the [documented installer](https://getcomposer.org/download/).
3. Verify installer SHA-384 — **do not skip**.
4. Run `php composer-setup.php` with `--install-dir` / `--filename`.
5. Delete the setup script.
6. Re-probe via `ExternalToolLocator.TryResolveComposerExecutable`.

`--local` vs `--global` default is still open (see remaining questions). `ExternalToolLocator` already prefers project `./composer.phar` over PATH.

### 2. `tyhp init` (shape A)

- Always write `tyhp.json` and the rest of the scaffold as today.
- **No `composer.json`:** write the Tyhp template (requires + `scripts.tyhp` + `require-dev` `tyhp/compiler`).
- **`composer.json` exists:** merge; do not skip the file and do not replace it wholesale.
- `--yes` / non-TTY: never hang on a Composer questionnaire (there isn’t one).

Greenfield template sketch:

```json
{
    "name": "app/tyhp-project",
    "description": "Tyhp project",
    "type": "project",
    "require": {
        "php": "{{PHP_CONSTRAINT}}",
        "tyhpdef/php": "@dev",
        "tyhp/core": "@dev"
    },
    "require-dev": {
        "tyhp/compiler": "{{COMPILER_PACKAGE_VERSION}}"
    },
    "minimum-stability": "alpha",
    "prefer-stable": true,
    "scripts": {
        "tyhp": "vendor/bin/tyhp",
        "post-autoload-dump": [
            "vendor/bin/tyhp --install-binary",
            "vendor/bin/tyhp generate_tyhpdef --vendor"
        ]
    },
    "scripts-descriptions": {
        "tyhp": "Forward extra args to the Tyhp CLI (e.g. composer tyhp build)"
    }
}
```

If `config.bin-dir` is not `vendor/bin`, the written script should use that bin-dir (or the package binary path Composer will generate). Implementation may use `vendor/bin/tyhp` as the default and rewrite on merge when `config.bin-dir` is set.

Composer prepends `bin-dir` to `PATH` when running scripts, so `"tyhp": "tyhp"` would also resolve after `tyhp/compiler` is installed. The locked spelling is still **`vendor/bin/tyhp`** (explicit, matches the grill).

### 3. No plugin (21.5)

Do not ship `type: composer-plugin` and do not add a CommandProvider in **this** story.

Story **21.10** adds a `PluginInterface` on `tyhp/core` (`type: composer-plugin`, `extra.class`) that only syncs `extra.tyhp.require` onto root `require-dev`. Still no Composer commands from the plugin. Composer 2 requires `type: composer-plugin` (or `composer-installer`) to activate `extra.class`; `library` never registers.

Users run Tyhp via bin/scripts:

| Invocation | Mechanism |
|------------|-----------|
| `vendor/bin/tyhp build` | Composer `"bin"` |
| `composer tyhp build` | Root `scripts.tyhp`; extra args append |
| `composer exec tyhp -- build` | Composer exec |

---

## Init merge rules

When init would have **skipped** `composer.json` because it already exists, **merge** instead. Other existing scaffold files (`src/index.tyhp`, …) still skip as today. Invalid JSON → error, do not write.

| Key | Merge |
|-----|--------|
| `require.php` | Set if missing. If present, do not weaken it below `output.phpVersion`; warn if the existing constraint cannot satisfy the chosen PHP version. |
| `require.tyhpdef/php`, `require.tyhp/core` | Add if missing as **`@dev`**. Do not overwrite an existing pin. |
| `require-dev.tyhp/compiler` | Add if missing. Do not overwrite an existing pin. |
| `scripts.tyhp` | Add if missing. If present, **leave it**. |
| `scripts.post-autoload-dump` | If missing, set the two-command array (`--install-binary` then `generate_tyhpdef --vendor`). If present, **append** those commands when they are not already in the array (do not reorder or remove user entries). |
| `scripts-descriptions.tyhp` | Add if missing and we added or already have `scripts.tyhp`. |
| `tyhp.json` `tyhpdefInclude` | Add `./vendor-tyhpdef/**/*.tyhpdef` if missing. Do not remove user globs. |
| `tyhp.json` `overlay` | Add `./tyhpdef/**/*.tyhpdef` if missing (hand / overlay tree). Do not remove user globs. |
| `.gitignore` | Append `vendor-tyhpdef/` if missing (generated; regenerates on `--vendor`). |
| `name`, `license`, `autoload`, other scripts | Untouched |

Report merged vs created vs skipped in the init summary.

---

## Compiler package (`tyhp/compiler`)

The compiler is a **native** binary, not PHP. Packagist gets a small PHP wrapper, not every OS/arch artifact.

```
tyhp/compiler
  composer.json
    name: tyhp/compiler
    type: library
    bin: ["bin/tyhp"]
    # Do NOT rely on this package's scripts for consumers — Composer
    # only runs the root package's scripts.
  bin/tyhp                 → PHP shim
  src/Installer.php        → OS/arch, GitHub Release asset, checksum, chmod
```

Shim (locked):

1. If argv requests **`--install-binary`** (plus Tyhp-owned flags if any), run the downloader and exit. Do **not** forward that flag to the native CLI.
2. Otherwise locate the downloaded native compiler binary (path TBD, e.g. under the package data dir).
3. If that binary is **not available** (missing, empty leftover, not executable, or otherwise cannot be launched): **error and exit non-zero.** Do **not** start a download. The message must tell the user to run `vendor/bin/tyhp --install-binary` to fix it (and may mention that `composer install` post-install was skipped via `--no-scripts` or failed).
4. If the binary is available: `proc_open` it with remaining argv; propagate exit code.

“Not available” here means the **native Tyhp compiler binary** the shim wraps — not `tyhp/core` / other runtime Composer packages.

Downloader:

- Pin the GitHub release tag to the Composer package version (compiler tag `805.0.0-alpha.1`, **not** runtime MAJOR `804.x`).
- Verify asset checksums (same policy as `scripts/install.sh` / Story 19.5).
- **`--install-binary` is idempotent:** if the matching native binary is already present, exit 0 and do nothing.
- Automatic download for apps is the **root** `post-autoload-dump` first command. Failure is a failed Composer script (non-zero) so CI notices.
- `--no-scripts` / air-gap: `composer install` can succeed; a later `vendor/bin/tyhp build` errors and tells the user to run `--install-binary`.

---

## Vendor tyhpdef generation

Day-1 typed use of **any** Composer PHP library, without waiting for a published `tyhpdef/*` wrapper.

This is **new Tyhp CLI work** (`generate_tyhpdef` has no vendor-scan mode today) plus Composer glue. Track B `--package-path` already generates one package; this mode loops installed packages with companion preference.

**Story 21.1:** Track B classifies foreign types against the existing `tyhpdef/*` catalog. `require` → require that wrapper (no `extern`). `suggest` / `require-dev` → `extern` with `@provided-by: tyhpdef/…`. Unknown origin stays unresolved. `--vendor` does **not** rewrite those names to `object` / `mixed`, split per-backer packages, or `require` optional-peer wrappers. If 21.1 is not done, `--vendor` output for Monolog-like packages will not load once optional types are catalogued.

### Why `post-autoload-dump`

| Event | Verdict |
|-------|---------|
| **`post-autoload-dump`** | **Locked.** Fires after `install` / `update` once `vendor/` and the autoloader exist, and also after `dump-autoload`. Generation must be **idempotent** (skip unchanged package+version). |
| `post-install-cmd` / `post-update-cmd` | Would miss `dump-autoload`; not required if dump is wired. |
| `post-package-install` | Per-package; needs a plugin. Out of scope. |
| Dependency `scripts` | Composer **does not run** them. |

Init writes (and merge appends):

```json
"post-autoload-dump": [
    "vendor/bin/tyhp --install-binary",
    "vendor/bin/tyhp generate_tyhpdef --vendor"
]
```

`--install-binary` is first so `--vendor` has a native compiler. `tyhp composer require|install|update` (success, no `--no-tyhpdef`) runs the same `--vendor` command so Tyhp-first users without the dump script still get stubs. Skip `--vendor` when `TYHP_VENDOR_TYHPDEF_RUNNING` is set (re-entry guard while requiring companions).

Honor `--no-tyhpdef` on `tyhp composer` (already a Tyhp-owned flag). Optional env `TYHP_NO_VENDOR_TYHPDEF=1` skips the dump script’s generate step (document it).

### CLI mode

Exclusive fourth `generate_tyhpdef` mode (alongside `--ext-name`, `--package-path`, `--source`):

```bash
tyhp generate_tyhpdef --vendor
tyhp generate_tyhpdef --vendor=/path/to/vendor
```

Default vendor dir: `{projectRoot}/vendor`. Requires `vendor/composer/installed.json` (Composer 2). Do **not** walk `vendor/*/*` as the inventory — path repos, `replace`, and install paths lie on the filesystem.

Inventory: every package in `installed.json` that has an install path, **plus** `ext-*` keys from the root `composer.json` `require` / `require-dev` (platform extensions are not in `vendor/`). Skip:

- The root package
- Packages with **no** autoloadable `.php` (metapackages, empty)
- Platform package `php` (not `ext-*` — those are handled in [PHP extensions](#php-extensions-ext-))
- `tyhp/compiler` (shim, not a typed PHP API)
- Extensions already covered by `tyhpdef/php` (always-present: Core, date, filter, hash, json, libxml, pcre, random, Reflection, SPL, standard — see that package’s `extra.tyhp.extensions`)

### Ensure runtime `tyhp/*` requires

If the root `composer.json` does not already name the package (in `require` or `require-dev`), `--vendor` **adds** it. Do **not** overwrite an existing pin.

**`require` (not `require-dev`), constraint `@dev` for all of these:**

| Package | Constraint to add when missing |
|---------|--------------------------------|
| `tyhp/core` | `@dev` |
| `tyhp/async` | `@dev` |
| `tyhp/lambda` | `@dev` |
| `tyhp/decimal` | `@dev` |
| `tyhpdef/php` | `@dev` |

(`tyhp/lambda` is the package name — not `tyhp/lamda`.)

`@dev` is the locked constraint (not an OR of PHP-major artifacts like `^805.0.0 \|\| ^804.0.0 \|\| …`). It stays valid across compiler updates and future PHP minors without editing this list. `minimum-stability` / `prefer-stable` on the root package (init already emits `minimum-stability: alpha` and `prefer-stable: true`) must allow these to resolve.

These are **runtime** packages (compiled PHP + tyhpdefs), so they belong in `require`. Batch them with the other Composer mutations (see [Companion install](#companion-install-re-entry)): one `require` batch and one `require-dev` batch, both `--no-scripts`, under `TYHP_VENDOR_TYHPDEF_RUNNING=1`.

### Preference order (per installed Composer library `vendor/name`)

Companion name is the locked convention: **`tyhpdef/<vendor>-<name>`** with `/` → `-` (e.g. `monolog/monolog` → `tyhpdef/monolog-monolog`). Same as `runtime/packages/new-composer-lib.sh`.

1. **Already Tyhp-ready.** Install path has `extra.tyhp.package` → **skip**. The binder already auto-loads it from `vendor/`.
2. **Companion already installed.** `tyhpdef/<vendor>-<name>` is in `installed.json` (or that companion’s `extra.tyhp.package` is loadable) → **skip** generate; **delete** any previously generated project stub for `vendor/name` so symbols do not duplicate.
3. **Companion available but not required.** A published `tyhpdef/<vendor>-<name>` exists that can type this upstream version → **add it to root `require-dev` and install**, then skip generate.
4. **Else generate.** Track B `--package-path=<install-path>` into **`vendor-tyhpdef/`**, never into the package under `vendor/` and never into `tyhpdef/`.

### PHP extensions (`ext-*`)

Scan root `composer.json` `require` and `require-dev` for keys `ext-<name>` (Composer platform packages). Map `<name>` to first-party package **`tyhpdef/php-ext-<name>`** (e.g. `ext-curl` → `tyhpdef/php-ext-curl`, `ext-pdo_mysql` → `tyhpdef/php-ext-pdo_mysql`). “Packages we have” = the first-party `tyhpdef/php-ext-*` set (in-tree `runtime/packages/php-ext-*` / published). Skip names already provided by `tyhpdef/php` (above).

1. **Have `tyhpdef/php-ext-<name>`.** If not already in root `require` / `require-dev`, add **`require-dev`** with constraint **`@dev`**, then install (same batch as other require-dev companions). Do not generate a project stub; binder loads `vendor/tyhpdef/php-ext-<name>/extra.tyhp.package`. If a generated `vendor-tyhpdef` stub for that extension exists, **delete** it.
2. **No first-party package.** Track A `generate_tyhpdef --ext-name=<name>` into **`vendor-tyhpdef/`** (managed PHP, same as today’s `--ext-name`). Filename: existing `TyhpdefOutputLayout.ExtensionFileName` (`curl` → `ExtCurl.tyhpdef`). If managed PHP is unavailable, **warn and continue** (do not fail the whole `--vendor` run solely for that).

`tyhpdef/php-ext-decimal` is the PECL Decimal extension; it is **not** `tyhp/decimal`.

### Output layout

Project directory **`vendor-tyhpdef/`** (not `tyhpdef/vendor/`):

```
vendor-tyhpdef/<vendor>.<package>.tyhpdef
vendor-tyhpdef/ExtCurl.tyhpdef
```

Composer-lib basenames stay `monolog.monolog.tyhpdef`. Overwrite when the installed **version** (or extension target) changed; skip when a stamp says that identity was already generated (`--overwrite` forces). Init **gitignores** `vendor-tyhpdef/`.

`--vendor` must **not** write into `tyhpdef/` (hand / overlay tree).

### Do not edit generated files — use overlays

`--vendor` **overwrites** files under `vendor-tyhpdef/`. Project developers must **not** modify those files. Lasting type fixes go in **overlay** tyhpdefs under `tyhpdef/` (loaded via `tyhp.json` `"overlay"`; `tyhp overlay create` / `stamp` already know project overlays).

**Every generated file** starts with a header (exact wording may be localized later; meaning is locked), for example:

```tyhpdef
// Generated by `tyhp generate_tyhpdef --vendor`. Do not edit this file.
// Re-running --vendor overwrites it. Put lasting changes in overlay tyhpdefs
// (tyhp.json "overlay", or `tyhp overlay create`), not in vendor-tyhpdef/.
```

User docs (install / tyhpdef / FAQ) repeat the same rule. Do not mention overlays only in the header.

### Companion install (re-entry)

Collect **all** missing packages first (`require` runtime `tyhp/*`, `require-dev` library companions and `tyhpdef/php-ext-*`), then Composer mutations with `--no-scripts`:

- Set `TYHP_VENDOR_TYHPDEF_RUNNING=1` for nested Composer.
- `--no-scripts` avoids infinite `post-autoload-dump`. After new packages land, **continue this `--vendor` pass**.
- Library companions: **require-dev**; constraint compatible with the installed upstream when possible (`3.10.0.0` for monolog `3.10.0`); if no matching wrapper version, fall through to Track B generate.
- Do not overwrite an existing `require` / `require-dev` pin.

How to know a **library** companion “exists”: Packagist (or configured Composer repositories) for `tyhpdef/<vendor>-<name>`. 404 / unsatisfiable → generate. First-party **php-ext** companions use the known `tyhpdef/php-ext-*` catalog, not a Packagist guess for arbitrary `ext-foo`.

### Generated stubs vs companions

| Situation | Project file `vendor-tyhpdef/…` | `vendor/tyhp/…/composer.json` (`extra.tyhp.package`) |
|-----------|----------------------------------|-------------------------------------|
| Only generated | present, loaded via `tyhpdefInclude` | absent |
| Companion / php-ext installed | **removed** (or not written) | loaded via vendor scan |
| Both would load | **illegal** (duplicate symbols) — companion wins, stub gone |

### Quality bar

Generated Track B / Track A stubs are **good enough to call the package or extension**, not a replacement for hand overlays / published wrappers. When a wrapper ships, the next `--vendor` run switches to it. Failures for a single package or extension (parse errors, empty autoload, missing managed PHP) **warn and continue** the rest; do not fail the whole Composer install unless `--strict` / a documented flag says otherwise. Straw man: warn + non-zero only if **every** candidate failed; otherwise Composer dump succeeds.

### Manual

```bash
tyhp generate_tyhpdef --vendor
```

same as the dump hook. `--no-tyhpdef` does not apply to this direct invocation.

---

## Out of scope

- Replacing Composer autoload (Story 31 Tyhp Link).
- `plugin-modifies-downloads` / `plugin-modifies-install-path`.
- `tyhp install php`.
- Flex / Symfony recipes.
- `composer create-project tyhp/skeleton` (second scaffold; keep `tyhp init` as the source of truth). Eligible later.
- Root `post-install-cmd` that compiles the app.

---

## Phases

### Phase 1: `tyhp install` + `tyhp install composer`

- `Action.install`, `InstallAction`, help, localization (both `.resx` files).
- Composer installer download + SHA-384 verify + local/global flags.
- Integrity check can point at `tyhp install composer`.
- Tests: fake HTTP / fixture installer; **no network** in `dotnet test`.

### Phase 2: Init shape A + merge

- Template gains `scripts.tyhp`, `post-autoload-dump`, `scripts-descriptions`, `require-dev` `tyhp/compiler`.
- `tyhpdef/php` and `tyhp/core` in the scaffold (and merge-if-missing) are **`@dev`**. Stop substituting `EncodeRuntimePackageVersion` for those keys. `php` (`{{PHP_CONSTRAINT}}`) still follows `--php-version`.
- `tyhp.json` gains `tyhpdefInclude` `./vendor-tyhpdef/**/*.tyhpdef` and `overlay` `./tyhpdef/**/*.tyhpdef`.
- `.gitignore` gains `vendor-tyhpdef/`.
- Existing `composer.json` / `tyhp.json` uses [Init merge rules](#init-merge-rules).
- `--yes` does not hang.
- CLI tests: greenfield JSON shape; merge adds missing keys; merge does not clobber `scripts.tyhp` or existing pins; dump script commands appended if absent.

### Phase 3: `tyhp/compiler` package

- PHP shim, `"bin"`, `--install-binary` (idempotent).
- OS/arch matrix matching GitHub assets.
- Versioning: compiler tag, not runtime MAJOR.
- Package tests: shim forwards argv/exit code; `--install-binary` intercepted; missing/unusable native binary → **error naming `--install-binary`, no download attempted**; checksum mismatch on `--install-binary` → fail, no binary left behind.

### Phase 4: `generate_tyhpdef --vendor` + Composer hooks

- Fourth exclusive generate mode; `installed.json` + `ext-*` from root composer.json; ensure `tyhp/core|async|lambda|decimal|php` **`@dev`** in **require**; `tyhpdef/php-ext-*` `@dev` in **require-dev** when we ship that extension; else Track A into `vendor-tyhpdef/`; Composer libraries: companion / Track B into `vendor-tyhpdef/`.
- Generated file header: do not edit; use overlays. Remove stubs when a companion appears.
- Re-entry guard + batched Composer require (`require` vs `require-dev`).
- `ComposerAction` fills `PLACEHOLDER_STORY_20` by invoking this mode.
- Tests: fixture `installed.json` + `ext-*`; skip `extra.tyhp.package`; skip always-present `tyhpdef/php` extensions; php-ext require-dev; generated path `vendor-tyhpdef/`; header present; overlay glob not overwritten; `--no-tyhpdef` skip.

### Phase 5: User documentation

- `docs/content/intro_installation.md` (and init / FAQ / `cli_tyhpdefGeneration.md` / tyhpdef overlay docs as needed): Composer compiler package **is** a supported install path; `--vendor` mode; `post-autoload-dump`; companion vs generated stubs; **do not edit `vendor-tyhpdef/` — use overlays**.
- Document `tyhp install composer` vs `tyhp composer install`.
- Document `vendor/bin/tyhp` and `composer tyhp …`.
- `toc.json` only if a new page is required.

---

## Grill record

2026-08-27. Answers are honor-system, not software-validated.

| # | Question | Answer |
|---|---------|--------|
| 1 | Who first, and how big? | **Both** audiences in this slice (Tyhp-first install-composer **and** PHP-first `tyhp/compiler`). |
| 2 | Init vs `composer init` | **Shape A** — Tyhp template only; never run `composer init`. |
| 3 | Naming | **`tyhp install composer`**. `tyhp composer install` already means something; `tyhp install …` is the family for later targets. |
| 4 | Plugin? | **No.** `vendor/bin/tyhp` and a **script** entry in `composer.json`. |
| 5 | Native download | **`--install-binary`** on the shim; **automatic** via **root** `post-autoload-dump` (Composer ignores dependency scripts). Plus explicit `--install-binary` if dump did not run. |
| 5b | Shim if binary missing | **Error out**; tell the user to run with `--install-binary`. Do not download on a normal invoke. |
| 6 | Existing PHP app | **Merge** into existing `composer.json`. |
| 7 | Vendor tyhpdefs | After `vendor/` exists: `generate_tyhpdef --vendor` on **`post-autoload-dump`**. Prefer companions / `tyhpdef/php-ext-*`; else generate in **`vendor-tyhpdef/`**. Ensure runtime `tyhp/core|async|lambda|decimal|php` in **require** as **`@dev`**. Do not edit generated files — overlays. |
| 8 | Generated output dir | **`vendor-tyhpdef/`**, not `tyhpdef/vendor/`. |
| 9 | Runtime package constraint | **`@dev`** for `tyhp/core`, `tyhp/async`, `tyhp/lambda`, `tyhp/decimal`, and `tyhpdef/php` (not an OR of `80N` majors). **`tyhp init`** uses `@dev` for `tyhpdef/php` and `tyhp/core` as well. |

---

## Remaining questions

Not blockers for the shape; lock during implementation planning if still ambiguous.

1. **`tyhp install composer` default location** — `--local` (`./composer.phar`, matches `ExternalToolLocator`) vs `--global` (`~/.local/bin/composer`)? Straw man: local default, `--global` opt-in. Never write `/usr/bin` without an explicit flag.
2. **Overwrite** — refuse to replace an existing Composer binary without `--force`?
3. **Init + `composer install`** — after writing/merging `composer.json`, run `composer install` under `--yes` if Composer is resolvable? Straw man: no (network + time); tell the user to run it.
4. **Windows** — in the Phase 3 OS/arch matrix for this story, or follow-up after macOS/Linux?
5. **`--install-binary` extras** — pin a tag (`--tag=`), output dir, or keep “download the version this package pin expects” only?
6. **Companion discovery** — always Packagist HTTP, Composer repositories only, or a shipped catalog plus Packagist fallback?
7. **`--vendor` failure policy** — straw man already: per-package warn + continue; non-zero only if every candidate failed. Confirm vs fail the Composer dump on any generate error.
8. **`--include-dev` for vendor scan** — straw man: whatever is in `installed.json` (respects `composer install --no-dev`).

---

## Cross-Story References

| Story | Relationship |
|-------|----------------|
| **10** | `ComposerJsonService` merge patterns; `build.updateComposer` (output dir, not this root merge) |
| **13** | `InitAction`, `ComposerAction`, `ExternalToolLocator`, help / `--yes` |
| **19.5** | IDE BinaryManager — share asset names / checksum policy with Phase 3 |
| **20** | Track B `--package-path` reused by `--vendor`. This story **fills** `PLACEHOLDER_STORY_20` on `ComposerAction`. |
| **21** | Runtime / wrapper packages on Packagist (`tyhpdef/php`, `tyhpdef/monolog-monolog`, …). `--vendor` **prefers** those companions; does not author their contents. |
| **21.1** | **Hard prerequisite.** Track B `extern` emit so generated / wrapped library tyhpdefs stay loadable when signatures name optional peers. Do not reinvent placeholders here. |
| **21.10** | `extra.tyhp.require` plugin on `tyhp/core` + Track C `extern`. 21.5 still has no CommandProvider. |
| **22** | Playground deferred; not this story |
| **30** | Remaining docs polish / publish ops |
| **31 Idea 1** | Tyhp Link — do **not** replace Composer autoload here |

---

## Golden Fixtures / Tests (Acceptance)

- [ ] **CLI:** `tyhp install composer --help`; missing PHP; already-installed no-op; `--force` once that policy is locked
- [ ] **Init greenfield:** template contains `scripts.tyhp`, `post-autoload-dump`, `require-dev.tyhp/compiler`; `tyhpdef/php` and `tyhp/core` are **`@dev`** (not `80N.x`); `tyhp.json` has `tyhpdefInclude` → `vendor-tyhpdef` and `overlay` → `tyhpdef`; `.gitignore` has `vendor-tyhpdef/`
- [ ] **Init merge:** missing keys added; existing `scripts.tyhp` and version pins preserved; dump commands appended; invalid JSON errors
- [ ] **Init:** `--yes` does not hang; still aborts when `tyhp.json` exists
- [ ] **Installer:** SHA mismatch → fail, no binary left behind
- [ ] **Shim:** forwards argv/exit code; `--install-binary` not forwarded; missing native binary **errors** with `--install-binary` instructions and does **not** download on that invoke
- [ ] **`--vendor`:** adds missing runtime `require` pins as `@dev` (`core` / `async` / `lambda` / `decimal` / `php`); `ext-curl` → `tyhpdef/php-ext-curl` `@dev` require-dev when we ship it; unknown `ext-*` → `vendor-tyhpdef/Ext*.tyhpdef`; Composer libs → companion or `vendor-tyhpdef/*.tyhpdef`; generated header forbids edits; stubs removed when companion appears; `--no-tyhpdef` skips the proxy hook
- [ ] **Docs:** `intro_installation.md` matches shipped install paths (including Composer package); overlay guidance for `--vendor` output
- [ ] **No network** in `dotnet test` (Packagist companion probes mocked)
