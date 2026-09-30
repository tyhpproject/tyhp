# Implementation Plan: Story 27.3 — Split Runtime, Docs, AIDevGuide, and IDE Plugin into Dedicated Repos

> **Roadmap position:** Story 27.3 — **Tier 2 — DX & Ecosystem** (after **27.2**, before deferred **22** / Tier 3 **23**; **implement after 27.2**)
> **Direct dependencies (new numbering):** **27.2** (block-target extension syntax rewrites in-tree `runtime/packages`, `docs/`, and `AIDevGuide/`), **27.1** (type-language stories that still rewrite those same trees), **19.5** (IDE clients exist under `tyhp-lang/`), **21** (runtime packages exist as Composer-shaped trees)
> **Do not implement until Story 27.2 is done.** Also finish any in-flight `runtime/packages/` overlay / lint work before Phase 1 (the extract). LSP polish listed in `TODO.md` should be done before Phase 4 (IDE extract) if it is not already green from 27.1 Phase 8.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md`. This story does not allocate new `MessageCode` values. See `ROADMAP.md` for the full tiered sequence.

> **Source:** Cursor plan “Split four source repos” (2026-09-16), preserved here so the work can wait until the end of Tier 2
> **Branch:** TBD
> **Prerequisites:** Stories through **27.2**. Optimizer (23–24), `internal` enforcement (25), and `?->` assignment (26) are **not** prerequisites.

Extract four trees from the compiler monorepo into already-created GitHub repos, then leave a sibling markdown checklist for CLA / GitHub app / Packagist (manual). None of the four **must** stay in the same git repo; remaining couplings are path discovery, one live-file C# test, diagnostics-reference generation, and Cursor `@` includes. Fix those rather than keeping the trees together.

Owner-maintained tyhpdefs ([`runtime/packages/TYHPDEF_OWNERSHIP.md`](../runtime/packages/TYHPDEF_OWNERSHIP.md)) are **in scope for this story**, not a later follow-up. After the extract, that document lives at the **`tyhp-runtime-src` repository root**. The scripts, GitHub issue templates, labels, CODEOWNERS, and scheduled Packagist scan it describes are **only** for that repo — never the compiler repo. Compiler `--vendor` / `extra.tyhp.tyhpdef` changes listed in that document’s “Compiler follow-up” section land in Phase 0 so the ownership contract works once packages leave this tree.

Destination clones already exist as stub READMEs with one dummy commit:

- `~/repos/tyhp-runtime-src` → `https://github.com/tyhpproject/tyhp-runtime-src`
- `~/repos/tyhp-docs-src` → `https://github.com/tyhpproject/tyhp-docs-src`
- `~/repos/tyhp-ai-dev-guide` → `https://github.com/tyhpproject/tyhp-ai-dev-guide`
- `~/repos/tyhp-ide-plugin` → `https://github.com/tyhpproject/tyhp-ide-plugin`

Internal order (same as `TODO.md`): runtime source → (later) Packagist publish → docs → AIDevGuide → IDE plugins.

### Two GitHub organizations

[`tyhpproject`](https://github.com/tyhpproject) keeps the compiler and the **source** trees. [`tyhpproject-packages`](https://github.com/tyhpproject-packages) holds only the **published** Composer package repositories (`tyhp/*` and `tyhpdef/*`).

| Organization | What lives there |
| --- | --- |
| `tyhpproject` | `tyhp` (compiler, CLI, LSP), `tyhp-runtime-src`, `tyhp-docs-src`, `tyhp-ai-dev-guide`, `tyhp-ide-plugin`, and the docs site `tyhp-docs` |
| `tyhpproject-packages` | Published package repos: first-party `core`, `async`, `decimal`, `lambda`, `php`, `php-ext-*`, and Composer-lib pairs `<vendor>-<name>` plus `<vendor>-<name>-impl` |

Composer names stay `tyhp/*` and `tyhpdef/*`. Only the GitHub org for those package repos changes.

Package repos that already exist under `tyhpproject` (the same names) are **re-created** under `tyhpproject-packages`. Packagist entries that already point at `https://github.com/tyhpproject/<repo>` are **updated** to `https://github.com/tyhpproject-packages/<repo>`. Do not submit a second Packagist package for a Composer name that already exists. After that retarget, publish scripts push only to `tyhpproject-packages`. The old `tyhpproject/<package>` repos are not the Packagist remote.

---

## Architecture Overview

Logical separation is already mostly done (C# tests do not load live packages; the language server lives in the compiler; docs are a self-contained PHP site; AIDevGuide is a skill bundle). A naïve folder copy still breaks maintainer workflows. This story is the **decoupling**, not a reason to keep the trees together.

```
tyhp (compiler, CLI, LSP)                         tyhpproject/tyhp
  │  TYHP_DLL / published CLI
  ▼
tyhp-runtime-src (package source)                 tyhpproject/tyhp-runtime-src
  │  publish
  ▼
tyhpproject-packages/<repo>  ──►  Packagist (tyhp/* , tyhpdef/*)
  ▲
  └── path repos if TYHP_RUNTIME_SRC (optional, maintainers)

tyhp ── generated diagnostics_reference.md ──► tyhp-docs-src ── publish-docs.sh ──► tyhp-docs (Pages / tyhplang.com)
tyhp-ide-plugin ── spawn `tyhp language_server` ──► tyhp
tyhp-ai-dev-guide ── Cursor skill / REGEN against compiler + runtime-src
```

### Recommended conventions (all four extracts)

- **History:** `git filter-repo` from `tyhp` so blame survives. Destination remotes currently have a stub first commit; replacing them is a **one-time force-push to those new repos only** (not to `tyhp`). Confirm before that push.
- **No git submodules.** Sibling clones under `~/repos/` (`tyhp`, `tyhp-runtime-src`, …).
- **No in-tree leftover copies** after each extract (delete from `tyhp` in a follow-up commit). Temporary dual-tree during cutover is fine; do not leave forever-sync copies.
- **CLA Assistant:** not in git. Same Gist as `dev-docs/ALPHA_RELEASE.md` (`https://gist.github.com/pristinesource/2e15358ca36027e181773c01b6309872`). Linking repos, GitHub app access, and Packagist submit are **not** done during the git splits; they are spelled out in Phase 6’s follow-up file.
- **Shared legal/community files** copied and retargeted from `tyhp`: `LICENSE.txt` (Apache 2.0), `CODE_OF_CONDUCT.md`, `AUTHORS.md`, a repo-specific `CONTRIBUTING.md` and `README.md`. Skip inventing issue/PR templates **except** on `tyhp-runtime-src`, which gets the tyhpdef-ownership templates from `TYHPDEF_OWNERSHIP.md`. `SECURITY.md` is also absent today; add only if wanted as part of this setup.

### Shared repo-setup checklist (each new repo)

After the code lands:

- Replace the stub README: what the repo is, how to clone/build/test, how it relates to `tyhp`, Apache 2.0, link to tyhplang.com.
- `LICENSE.txt`, `CODE_OF_CONDUCT.md`, `AUTHORS.md`, `CONTRIBUTING.md` (CLA paragraph + how to run tests for **that** repo).
- `.gitignore` from the relevant slice of the compiler ignore file (plus `packages/dist/`, `docs/output/`, `node_modules/`, Gradle `build/`, etc.).
- GitHub: description, topics, default `main`, Issues/PRs on. CLA Assistant is linked in Phase 6.
- CI appropriate to the repo. Do not copy `dotnet test` into repos that have no C#.
- Leave `packagist.credentials` local/gitignored; do not copy secrets.
- **Exception (`tyhp-runtime-src` only):** add the ownership issue templates, labels, CODEOWNERS, and scan workflow from `TYHPDEF_OWNERSHIP.md`. Do not add those to the compiler, docs-src, ai-dev-guide, or ide-plugin repos.

### Cutover recipe (each extract)

1. Land compiler decoupling for that tree **before** deleting files (`dotnet test` must not depend on the soon-to-be-gone path).
2. `git filter-repo` into a throwaway clone; rearrange to the new layout; add setup files; add CI.
3. Force-push to the stub destination `main` (explicit approval).
4. Delete the tree from `tyhp`; update README/CONTRIBUTING/rules; push `tyhp`.
5. Smoke for that tree (see each phase).
6. After **all four** extracts (Phase 5 below): enable the local sibling-repos Cursor rule.

---

## Remaining couplings (fix these; do not keep the trees in one repo)

| Coupling | Where | After split |
| --- | --- | --- |
| Upward walk for `runtime/packages` | `Tyhp/Domain/Services/ComposerJsonService.cs` (`SearchUpwardForRuntimePackages`) | Resolve `TYHP_RUNTIME_SRC`, then sibling `../tyhp-runtime-src/packages`, then stop. Path-repo injection stays **optional** (maintainers / pre-Packagist). |
| Path string `/runtime/packages/` | `EmittedFqnHelper`, `AttributeRule` | Treat paths under the resolved runtime-src root (or `/packages/php/`) as engine/runtime sources. Otherwise package builds from the new repo mis-apply `namespacePrefix` / native-type rules. |
| Root `tyhp.json` `tyhpdefInclude` | Compiler workspace | Drop in-tree paths. After Packagist, apps use Composer. Compiler repo can use `"include": []` (already) and empty tyhpdef includes. |
| `NativeTypeTestEmitterTests.OverlayFile_StampsCanonicalIsFunctions` | `tests/Tyhp.Tests/Emitter/NativeTypeTestEmitterTests.cs` | Stop reading the live overlay. Inline a fixture snippet (same product rule as the 2026-09-15 C#↔packages split). Live overlay stays covered by `test-all-tyhpdef.sh` in runtime-src. |
| Shell `REPO_ROOT=../..` + `TYHP_DLL=$REPO_ROOT/bin/Debug/net9.0/tyhp.dll` | `runtime/packages/*.sh`, `scripts/publish-runtime-packages.sh` | `TYHP_DLL` required-or-default to sibling `../tyhp/bin/Debug/net9.0/tyhp.dll`. Package root is the new repo, not compiler root. |
| `new-proposed-composer-libs.sh` → `dev-docs/PROPOSED_TYHPDEF_PACKAGES.md` | Compiler `dev-docs/` | **Move the list** into runtime-src (`docs/` or `dev-docs/`). It is a packaging backlog, not compiler source. |
| `generate_tyhpdef` snapshots | gitignored `tyhpdef_gen/` under compiler cwd | Run generate from runtime-src so snapshots live there (still gitignored). |
| `diagnostics_reference.md` | Generator in compiler; file in `docs/content/`; asserted by `DiagnosticsReferenceGeneratorTests` | Keep the generator in `tyhp`. Compiler tests assert generator invariants only (no in-tree docs file). A small sync script writes markdown into docs-src. Language stories still update `MessageCode` in `tyhp` and sync the page. |
| `.cursor/rules/tyhp.mdc` `@AIDevGuide/QUICK_GUIDE.md` | Compiler | After the guide moves, that `@` include is a missing file. Point the compiler rule at installing the skill from `tyhp-ai-dev-guide`, and add the same skill/rule in runtime-src (that is where `.tyhp` / `.tyhpdef` live). |
| Cursor rules for packages | `.cursor/rules/runtime-package-compile-failures.mdc`, `runtime-generated-php-no-git-restore.mdc` | Move (copy then delete) into runtime-src; keep a short pointer in `tyhp` (“fix Tyhp first, packages live in tyhp-runtime-src”). |
| Local sibling-repos Cursor rule | gitignored `.cursor/rules/sibling-repos.mdc` + `~/repos/tyhp.code-workspace` | Prep exists **disabled** (`alwaysApply: false` + DISABLED banner). Phase 5 enables it after the extracts so agents may edit the sibling checkouts. |
| README path-repo sentence | `README.md` | After Packagist: Composer. Until then: clone `tyhp-runtime-src` and set `TYHP_RUNTIME_SRC`. Package-table GitHub links (`core`, `async`, `decimal`, `lambda`, `php`) point at `tyhpproject-packages`, not `tyhpproject`. |
| Owner-maintained tyhpdefs | [`runtime/packages/TYHPDEF_OWNERSHIP.md`](../runtime/packages/TYHPDEF_OWNERSHIP.md) | Move the file to **runtime-src repo root**. Build the scripts it names (`generate-meta-package.sh`, `write-package-readmes.sh`, `scan-upstream-tyhpdefs.sh`, `verify-owner-tyhpdef.sh`, `handoff-tyhpdef.sh`) and retarget `new-composer-lib.sh` / `publish-runtime-packages.sh` / `check-composer-lib-updates.sh`. GitHub templates + scan workflow live only on runtime-src. Compiler `--vendor` honors `extra.tyhp.tyhpdef`, does not auto-install the public companion when bundled types exist, and never root-requires `*-impl`. |
| CoderDocs theme license | `docs/CoderDocs-BS5-v3.0/license.txt` | Theme may not be redistributed **standalone**. Shipping it as part of the Tyhp docs project matches current use; confirm that is still OK when the tree is its own public repo. Keep footer attribution. |

**Not blockers:** compiler CI (`.github/workflows/tests.yml`) does not build packages or docs. IDE clients already spawn a published `tyhp` CLI; they do not compile `Tyhp/`. No `.gitmodules`.

---

## Pipeline

```
Story 27.2 (block-target extension syntax)
    │
    ▼
┌──────────────────────────────────────────────────────────┐
│  STORY 27.3: Split four source repos                     │
│                                                          │
│  Phase 0: Compiler decoupling for runtime packages       │
│           (+ --vendor / extra.tyhp.tyhpdef ownership)    │
│  Phase 1: Extract tyhp-runtime-src                       │
│           (+ ownership scripts, GitHub scan/templates)   │
│  Phase 2: Diagnostics decoupling + extract tyhp-docs-src │
│  Phase 3: Extract tyhp-ai-dev-guide                      │
│  Phase 4: Extract tyhp-ide-plugin (after LSP polish)     │
│  Phase 5: Enable local sibling-repos Cursor rule         │
│  Phase 6: Manual-ops checklist at ~/repos                │
└──────────────────────────────────────────────────────────┘
    │
    ▼
Skip deferred Story 22 — then Tier 3 (Story 23)
```

---

## Phase 0: Compiler decoupling for runtime packages

Land in `tyhp` **before** deleting `runtime/packages/`.

- `ComposerJsonService` discovery: `TYHP_RUNTIME_SRC`, then sibling `../tyhp-runtime-src/packages`, then stop. Comments/help in `Tyhp/Config/DisplayHelp.cs` (`--audit-stubs=` example).
- Path heuristics in `EmittedFqnHelper` / `AttributeRule`.
- Fixture `OverlayFile_StampsCanonicalIsFunctions` (do not read the live overlay).
- Empty/remove root `tyhp.json` `tyhpdefInclude` entries that point at `./runtime/packages/...`.
- README / CONTRIBUTING (“do not hand-edit generated PHP”) / `.cursor/rules` pointers.
- `RuntimePackageVersions.Bundled` stays a **fallback version table**, not a disk read.

**Compiler `--vendor` / ownership contract** (from `TYHPDEF_OWNERSHIP.md` “Compiler follow-up (not this repo)” — still **this story**, because it is compiler code):

1. Honor `extra.tyhp.tyhpdef` on an installed PHP package: require that sibling if missing; load *its* `extra.tyhp.package`.
2. When bundled `extra.tyhp.package` is present on the PHP package, do **not** auto-install `tyhpdef/<vendor>-<name>`. If `*-impl` is also installed, bind the PHP package and warn.
3. `--vendor` adds the **public** companion at the **exact** installed upstream version. It never adds `*-impl` as a root require.
4. Walk `extra.tyhp.impl` on a public metapackage only as documentation; loading stays `extra.tyhp.package` on whatever is installed (the impl).
5. Prefer the PHP package’s `extra.tyhp.package` when both that and `*-impl` would load.

These are the “To build in the compiler” rows in the ownership status table. Do not leave them for a later story; community companions after the split are public metapackage + `*-impl`.

### Acceptance Criteria

- [x] `dotnet test` does not open `runtime/packages/` on disk (the live-overlay fact is gone or fixture-based)
- [x] Discovery finds a sibling `tyhp-runtime-src/packages` or `TYHP_RUNTIME_SRC` when set; it does not require an in-tree `runtime/packages`
- [x] Root `tyhp.json` no longer lists in-tree package composer.json paths
- [x] `--vendor` skips auto-install of `tyhpdef/<vendor>-<name>` when the PHP package has `extra.tyhp.package`
- [x] `--vendor` requires `extra.tyhp.tyhpdef` (sibling) when that pointer is present, not the community companion
- [x] `--vendor` never adds `tyhpdef/<vendor>-<name>-impl` as a root require; public name only
- [x] When both bundled types and `*-impl` would bind, the PHP package wins and a warning is emitted

---

## Phase 1: Extract `tyhp-runtime-src`

**Layout after filter + rename:**

```
tyhp-runtime-src/
  README.md LICENSE.txt CONTRIBUTING.md AUTHORS.md CODE_OF_CONDUCT.md
  TYHPDEF_OWNERSHIP.md          # from today's runtime/packages/ — repo root, not under packages/
  TYHPDEF_OVERLAY_STANDARDS.md
  TYHPDEF_ORDERING.md
  generate-meta-package.sh      # new (ownership)
  write-package-readmes.sh      # new
  scan-upstream-tyhpdefs.sh     # new
  verify-owner-tyhpdef.sh       # new
  handoff-tyhpdef.sh            # new
  packages/                     # today's runtime/packages/* package trees + existing build/test/scaffold scripts
  scripts/                      # publish-runtime-packages.sh (+ notes for packagist.credentials)
  .github/
    CODEOWNERS
    ISSUE_TEMPLATE/             # tyhpdef-ownership.yml, tyhpdef-reclaim.yml, config.yml
    workflows/
      scan-upstream-tyhpdefs.yml
      test-tyhpdef.yml          # test-all-tyhpdef.sh (and optionally base-build-all.sh)
```

Keep a `packages/` directory (do not dump ~288 package folders at repo root). Do **not** keep the extra `runtime/` prefix.

Ownership scripts and `TYHPDEF_OWNERSHIP.md` live at **repository root** (CODEOWNERS, the scan workflow’s `./scan-upstream-tyhpdefs.sh`, and the ownership doc all assume that). Those scripts resolve version folders as `packages/<vendor>-<name>/<upstream>/` (accept a `packages/` prefix or add it). Existing build/scaffold scripts that today live beside the packages (`base-build-all.sh`, `new-composer-lib.sh`, `test-all-tyhpdef.sh`, …) stay under `packages/` unless a given script is easier at repo root; retarget `REPO_ROOT` / `TYHP_DLL` either way.

**Move with the tree:** everything under `runtime/packages/` (package dirs, `base-build-all.sh`, `build-common.sh`, `test-all-tyhpdef.sh`, `new-*.sh`, `check-composer-lib-updates.sh`, `ensure-existing-github-repos.sh`, `TYHPDEF_*.md`, `.php-cs-fixer.php`), `scripts/publish-runtime-packages.sh`, `dev-docs/PROPOSED_TYHPDEF_PACKAGES.md`, the two runtime Cursor rules. After the move, **lift** `TYHPDEF_OWNERSHIP.md` (and the other `TYHPDEF_*.md` files if they should sit next to it) to repo root so links in ownership READMEs (`https://github.com/tyhpproject/tyhp-runtime-src`) stay accurate.

**Stay in `tyhp`:** `scripts/release.sh`, `install.sh` / `install.ps1`. `VERSIONING.md` stays the compiler source of truth; copy the “Runtime Composer packages” section into runtime-src README or a short `VERSIONING.md` and keep them in sync. Document the public-metapackage vs `*-impl` four-part versioning from `TYHPDEF_OWNERSHIP.md` there.

**Script updates (path retarget — existing scripts):**

- Replace `REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"` with: packages dir = `packages/` (or script location), compiler = `TYHP_DLL` or `../tyhp/bin/Debug/net9.0/tyhp.dll`.
- `publish-runtime-packages.sh`: `PACKAGES_DIR` = `$PROJECT_ROOT/packages`; user-agent GitHub URL → `tyhpproject/tyhp-runtime-src` (source repo); Packagist credentials path under the new `scripts/`.
- Package-repo `GITHUB_ORG` in `publish-runtime-packages.sh`, `ensure-existing-github-repos.sh`, and `new-proposed-composer-libs.sh` becomes `tyhpproject-packages`. User-Agent URLs that cite the compiler or `tyhp-runtime-src` stay on `tyhpproject`.
- Help/comments that say `runtime/packages/...` → `packages/...`.
- `new-php-ext.sh`: invoke `generate_tyhpdef` from runtime-src root (snapshots under local `tyhpdef_gen/`).

**Script updates (owner-maintained tyhpdefs — build what `TYHPDEF_OWNERSHIP.md` lists as “to build”).** Do not invent one-off `composer.json` shapes; copy the [canonical package shapes](../runtime/packages/TYHPDEF_OWNERSHIP.md#canonical-package-shapes) exactly. Until these scripts exist the ownership doc says follow it by hand — **this story creates them.**

Composer-lib companions (not `tyhpdef/php`, not `php-ext-*`, not compiled `tyhp/*`) become **two packages**:

| | Public metapackage | Implementation |
|--|--------------------|----------------|
| Composer name | `tyhpdef/<vendor>-<name>` | `tyhpdef/<vendor>-<name>-impl` |
| GitHub repo | `tyhpproject-packages/<vendor>-<name>` | `tyhpproject-packages/<vendor>-<name>-impl` |
| Version | exact upstream (`3.14.0`) | four-part (`3.14.0.0`) |
| Source | **generated at publish** | version folder `packages/<vendor>-<name>/<upstream>/` (today’s tree) |

New scripts at runtime-src **repo root** (contracts and flags are in `TYHPDEF_OWNERSHIP.md`; do not weaken them here):

- `generate-meta-package.sh` — public `composer.json` + README/LICENSE from an impl version folder. Public `require` of impl is `~{numeric-core}.0` on stable (never an exact four-part pin); exact impl version on prerelease. Skip emitting a new public tag when that public version already exists and the generated `require` range is unchanged.
- `write-package-readmes.sh` — idempotent `<!-- tyhp-readme:start -->` / `<!-- tyhp-readme:end -->` markers. Impl README, generated public README, handed-off public README, first-party `php` / `php-ext-*` section. Skip `core`, `async`, `decimal`, `lambda`, `compiler`.
- `scan-upstream-tyhpdefs.sh` — Packagist extras for impl trees; classify bundled / sibling / replace-only / none / handed-off. `--json` / `--markdown`. User-Agent family: `tyhp-scan-upstream-tyhpdefs (+https://github.com/tyhpproject/tyhp-runtime-src)`. Exit 0 when signals exist; non-zero only on usage / HTTP / parse failures.
- `verify-owner-tyhpdef.sh` — quality gate **required** before handoff. Never called from the scheduled workflow. Composer `--no-scripts --no-plugins`; refuse `composer-plugin` / install scripts; `tyhp lint` + `generate_tyhpdef --verify`.
- `handoff-tyhpdef.sh` — stamps `extra.tyhp.ownership` in **source**, regenerates READMEs; does not retag already-published Packagist versions. Scenarios: bundled, sibling, reclaim. `--dry-run` supported.

**Wire existing scripts to the new ones:**

- `new-composer-lib.sh` scaffolds the **impl** tree (`name`: `tyhpdef/<vendor>-<name>-impl`, `extra.tyhp.public` = public name) and calls `write-package-readmes.sh`.
- `publish-runtime-packages.sh` (and `ensure-existing-github-repos.sh`) create **two** GitHub repos per Composer-lib under **`tyhpproject-packages`** (`<vendor>-<name>` and `<vendor>-<name>-impl`). The same org constant covers first-party `core`, `async`, `decimal`, `lambda`, `php`, and `php-ext-*`. Do not create or push package repos on `tyhpproject`. Publish runs `generate-meta-package.sh` then tags public (upstream version) and impl (four-part). Handed-off folders: stop tagging; generated public meta has no impl `require` (see ownership canonical shapes). First-party `php` / `php-ext-*` / compiled `tyhp/*` stay single packages (policy §7).
- `check-composer-lib-updates.sh` skips `extra.tyhp.ownership.status == handed-off` folders.
- `test-all-tyhpdef.sh` still lints impl trees (and reclaim regenerations).

**GitHub setup on `tyhpproject/tyhp-runtime-src` only** (from ownership “GitHub setup”; Issues enabled; do **not** add these to the compiler):

- Labels: `tyhpdef-ownership`, `tyhpdef-ownership-scan`, `tyhpdef-handoff-bundled`, `tyhpdef-handoff-sibling`, `tyhpdef-reclaim`, `tyhpdef-quality-failed` (colors in the ownership doc). Labels are GitHub UI — list them in Phase 6 as well if they cannot be applied from git.
- Issue templates: `.github/ISSUE_TEMPLATE/tyhpdef-ownership.yml`, `tyhpdef-reclaim.yml`, `config.yml` with `blank_issues_enabled: true` (required fields as in the ownership doc).
- `.github/CODEOWNERS` requiring a tyhpproject maintainer on the scan workflow, the five ownership scripts, and `TYHPDEF_OWNERSHIP.md`.
- `.github/workflows/scan-upstream-tyhpdefs.yml` — Monday 11:17 UTC + `workflow_dispatch`; `permissions: contents: read, issues: write`; must **not** run `handoff-tyhpdef.sh`, commit, open a PR, or publish. Updates the rolling report issue via `vars.UPSTREAM_TYHPDEF_REPORT_ISSUE`. Sequential Packagist fetches; cache HTTP 200 bodies in the job workspace only.
- Human (Phase 6): pin one rolling-report issue, set `UPSTREAM_TYHPDEF_REPORT_ISSUE`, create labels if not scripted, “Allow GitHub Actions to create and approve pull requests” is **not** required.

**CI in runtime-src:** workflow that (1) assumes a released `tyhp` CLI or downloads it, (2) runs `packages/test-all-tyhpdef.sh` (and optionally `base-build-all.sh`). Separate from the ownership scan. Do not make compiler `tests.yml` start depending on the sibling clone.

**After this phase, compiler `dotnet test` must stay green with `runtime/packages/` gone.**

### Acceptance Criteria

- [ ] `tyhp-runtime-src` has `packages/` + retargeted scripts + repo setup files
- [ ] `TYHPDEF_OWNERSHIP.md` is at the runtime-src **repository root** (not under `packages/`)
- [ ] The five ownership scripts exist at repo root and match the ownership doc’s flags / canonical `composer.json` shapes
- [ ] `new-composer-lib.sh` scaffolds an **impl** package and writes READMEs via `write-package-readmes.sh`
- [ ] `generate-meta-package.sh` emits a public metapackage whose impl `require` is a `~X.Y.Z.0` range (exact pin only on prerelease)
- [ ] `publish-runtime-packages.sh` / `ensure-existing-github-repos.sh` create package repos on `tyhpproject-packages` (both `<vendor>-<name>` and `<vendor>-<name>-impl` for Composer-libs; one repo for first-party packages)
- [ ] `check-composer-lib-updates.sh` skips `handed-off` folders
- [ ] `handoff-tyhpdef.sh --dry-run` prints composer.json + README for bundled / sibling / reclaim without rewriting Packagist tags
- [ ] Scan workflow + issue templates + CODEOWNERS are in runtime-src `.github/`; none of those files are in the compiler repo
- [ ] `TYHP_DLL=… ./packages/test-all-tyhpdef.sh` runs from runtime-src
- [ ] A sample `base-build-all.sh` package build works against a sibling compiler `tyhp.dll`
- [ ] `runtime/packages/` is deleted from `tyhp`; compiler tests green

---

## Phase 2: Diagnostics decoupling + extract `tyhp-docs-src`

**Layout:** today’s `docs/` contents at the **repo root** (`content/`, `generator-src/`, `template/`, `generate_docs.php`, `composer.json`, theme, committed `vendor/` if that policy is kept).

**Move:** entire `docs/` tree + `scripts/publish-docs.sh`. Optionally copy gitignored `tools/flip_docs_alpha_badges.py` (ignored by `tools/*` except `genTyhpdef.php`).

**Publish target unchanged:** still rsync HTML to existing `tyhpproject/tyhp-docs` (GitHub Pages / tyhplang.com). Only the **source** repo changes. Update the commit message in `publish-docs.sh` (“from docs-src”, not “from compiler repo”).

**Compiler/docs sync:**

- `DiagnosticsReferenceGeneratorTests` no longer requires `docs/content/diagnostics_reference.md` in `tyhp`.
- Add `scripts/sync-diagnostics-reference.sh` (compiler or docs-src): run generator / copy markdown into docs-src `content/diagnostics_reference.md`.
- `CONVENTIONS.md` §10: docs pages live in docs-src; diagnostic **codes** still land in `MessageCode.cs` first.

**CI in docs-src:** `composer install && php generate_docs.php` (needs `php`, `composer`, `sass`). Do not auto-push Pages from every PR unless wanted later.

**Link updates:** compiler README, BRANDING logo paths, and `tyhp-lang` GitHub blob URLs to `tyhp/.../docs/content/...` (those URLs break even before Phase 4). Prefer tyhplang.com permalinks.

**Legal:** CoderDocs + Font Awesome licenses travel with the tree; keep attribution; do not treat the theme as a standalone product.

### Acceptance Criteria

- [x] Compiler tests do not require an in-tree `docs/content/diagnostics_reference.md`
- [x] `php generate_docs.php` succeeds in docs-src
- [x] `publish-docs.sh` is retargeted; Pages destination is still `tyhpproject/tyhp-docs`
- [x] `docs/` is deleted from `tyhp`

---

## Phase 3: Extract `tyhp-ai-dev-guide`

**Layout:** today’s `AIDevGuide/` files at **repo root** so `SKILL.md` is the skill root (`ln -s /path/to/tyhp-ai-dev-guide .cursor/skills/tyhp`).

**Move:** the whole folder. No build. No tests read this tree. Optional markdown lint CI.

**Decouple:**

- Rewrite `AIDevGuide/README.md` install paths (clone this repo, not a subfolder of `tyhp`).
- `AIDevGuide/REGEN.md`: regen still needs a compiler checkout **and** runtime-src; say so (sibling clones).
- Compiler `.cursor/rules/tyhp.mdc`: remove `@AIDevGuide/QUICK_GUIDE.md`; short instruction to install the skill.
- Runtime-src: add the skill/rule so package work still loads the guide.
- `CONVENTIONS.md`: language stories update **docs-src + ai-dev-guide**, not in-tree folders.

This phase does not wait on Packagist.

### Acceptance Criteria

- [x] Guide repo root has `SKILL.md` / `guide/` / `handbook/`
- [x] Compiler `tyhp.mdc` does not `@`-include a missing `AIDevGuide/` path
- [x] `AIDevGuide/` is deleted from `tyhp`

---

## Phase 4: Extract `tyhp-ide-plugin`

Already loosely coupled. Do this **after LSP polish** (`TODO.md` / 27.1 Phase 8) if that work is still open.

Move `tyhp-lang/vscode/` and `tyhp-lang/phpstorm/` as **siblings at repo root** so PhpStorm Gradle `../vscode` copy of grammars/icons keeps working.

**Must change:**

- `package.json` `repository` → `tyhpproject/tyhp-ide-plugin` (drop `directory: tyhp-lang/vscode` or set `vscode`).
- PhpStorm vendor URL / READMEs / QA symlink examples (`$(pwd)/tyhp-lang/vscode` → new clone paths).
- Keep downloading CLI assets from `tyhpproject/tyhp` releases (`GITHUB_REPO` stays the compiler).

**Leave in `tyhp`:** `Tyhp/LanguageServer/` and `tests/Tyhp.Tests/LanguageServer/` — that **is** the server.

**CI in ide-plugin:** `npm ci && npm test && npm run package` in `vscode/`; `./gradlew unitTest buildPlugin` in `phpstorm/`. None of this exists in the compiler repo today.

Marketplace / JetBrains publish stay later (human; noted in Phase 6 / `TODO.md`). This phase only moves the source tree.

### Acceptance Criteria

- [x] `vscode/` and `phpstorm/` are siblings in `tyhp-ide-plugin`; Gradle still copies grammars from `../vscode`
- [x] VS Code `npm test` and PhpStorm `unitTest` pass in the new repo
- [x] `tyhp-lang/` is deleted from `tyhp`; compiler LSP tests still live under `tests/Tyhp.Tests/LanguageServer/`

---

## Compiler repo after all four

`tyhp` becomes compiler + CLI + LSP + C# tests + `dev-docs/` + `Resources/` + compiler scripts (`release.sh`, `install.*`). README documentation links go to tyhplang.com and the four source repos. CONTRIBUTING drops runtime-package build steps. Delete empty `runtime/`, `docs/`, `AIDevGuide/`, `tyhp-lang/` after each cutover commit.

---

## Phase 5: Enable the local sibling-repos Cursor rule

Prep (already on disk, gitignored, **disabled** until this phase):

- Rule: `tyhp/.cursor/rules/sibling-repos.mdc` (absolute paths; listed in `.gitignore`; `alwaysApply: false`, no globs, DISABLED banner so agents do not search the stub clones)
- Workspace: `~/repos/tyhp.code-workspace` (outside git; five folders)

**When the four extracts are done**, edit the gitignored rule in place (do not commit it):

1. Set frontmatter `alwaysApply: true`. Change `description` to something like `Sibling Tyhp checkouts — agent may edit these repos`.
2. **Delete** the entire `# DISABLED — Story 27.3 has not landed` section.
3. Keep `# After Story 27.3` as the live rule. Confirm the table paths still match this machine and the **landed** layouts (`packages/` under runtime-src, docs at docs-src root, `SKILL.md` at ai-dev-guide root, `vscode/` + `phpstorm/` siblings in ide-plugin).
4. Confirm `~/repos/tyhp.code-workspace` still lists all five folders. Open that workspace in Cursor for multi-root work.
5. Do **not** `git add -f` the rule. Machine paths stay local.

Until this phase, agents must keep using in-tree `runtime/packages/`, `docs/`, `AIDevGuide/`, and `tyhp-lang/`.

### Acceptance Criteria

- [x] `sibling-repos.mdc` has `alwaysApply: true` and no DISABLED banner
- [x] Paths in the rule match the extracted repos on this machine
- [x] The rule is still gitignored / untracked
- [x] `tyhp.code-workspace` opens all five folders

---

## Phase 6: Manual-ops checklist (outside git)

Write **`~/repos/tyhp-post-split-manual-steps.md`** on disk next to the sibling clones. It is **not** committed to any of the five repos.

Source of truth to distill: `dev-docs/ALPHA_RELEASE.md` plus the four new repo names. Make it a **click-through checklist** with unchecked boxes, URLs, and “done when” notes — not a design essay.

### CLA Assistant ([cla-assistant.io](https://cla-assistant.io/))

- Reuse the existing Gist: `https://gist.github.com/pristinesource/2e15358ca36027e181773c01b6309872`. Do not create a second CLA unless counsel says so (a new Gist forces every signer to re-sign).
- Hosted SAP service only (not self-hosted).
- Link the **same** CLA to each public source repo that will take outside PRs:
  - `tyhpproject/tyhp` (already; re-check after any recreate)
  - `tyhpproject/tyhp-runtime-src`
  - `tyhpproject/tyhp-docs-src`
  - `tyhpproject/tyhp-ai-dev-guide`
  - `tyhpproject/tyhp-ide-plugin`
- Optional: published package repos on **`tyhpproject-packages`** (`core`, `async`, `decimal`, `lambda`, `php`, php-ext-*, composer-lib public + `*-impl` pairs) if those will take PRs. That needs the CLA Assistant app installed on **`tyhpproject-packages`** as well as `tyhpproject`. ALPHA_RELEASE treated package-repo CLA as optional; the four **source** repos on `tyhpproject` are required.
- Dashboard: allow bots that cannot sign (`dependabot[bot]`, `github-actions[bot]` if workflows open PRs).
- Verify once per new repo: unsigned throwaway PR → CLA comment + pending status → sign → green.
- Signatures live in CLA Assistant’s DB, not git. Export CSV if you want an offline copy; do not commit signee lists.

### GitHub app / org permissions

- **CLA Assistant** GitHub App is installed on the **`tyhpproject` organization** (not only a user) for the compiler and the four source repos. If package repos will take outside PRs, install the same app on **`tyhpproject-packages`**. If the app is limited to selected repositories, add the four new source repos on `tyhpproject` and each package repo on `tyhpproject-packages`. Without org/repo access it cannot comment on PRs.
- **Packagist** GitHub access: GitHub → Settings → Applications → Packagist → **Organization access** granted to **`tyhpproject-packages`** (the org Packagist clones). Access on `tyhpproject` does not cover those package repos.
- After `--create-github-repos` creates new public package repos on `tyhpproject-packages`, if CLA Assistant or Packagist is “selected repositories” only, add each new repo (or switch the app to all repos in that org).
- GitHub Pages for `tyhpproject/tyhp-docs` (CNAME `tyhplang.com`) is **not** an app; glance at Pages settings after `publish-docs.sh` moves to docs-src.
- Actions: confirm the new source repos can run workflows (Actions enabled; no org policy blocking first-time workflows).
- **`tyhp-runtime-src` ownership (from `TYHPDEF_OWNERSHIP.md`):** Issues enabled on `tyhpproject/tyhp-runtime-src`; create the six `tyhpdef-*` labels; pin the rolling report issue `Upstream tyhpdef signals (rolling report)` (`tyhpdef-ownership-scan`); set Actions variable `UPSTREAM_TYHPDEF_REPORT_ISSUE` to that issue number; workflow permissions default **read** (the scan workflow sets `contents: read` / `issues: write` itself). Do **not** enable “Allow GitHub Actions to create and approve pull requests” for this. No Packagist token on the scan. After `--create-github-repos`, expect **pairs** of public repos on `tyhpproject-packages` (`nesbot-carbon` and `nesbot-carbon-impl`) — add them to CLA Assistant / Packagist if those apps are “selected repositories” only.

### Packagist

- Account exists; MAIN API token in gitignored `scripts/packagist.credentials` (`PACKAGIST_USERNAME` + `PACKAGIST_API_TOKEN`) — after Phase 1 this file lives under **tyhp-runtime-src** `scripts/`, not the compiler.
- Do **not** submit `tyhpproject/tyhp` (compiler). Do **not** submit `tyhp-runtime-src`, `tyhp-docs-src`, `tyhp-ai-dev-guide`, or `tyhp-ide-plugin` — those are source trees, not Composer packages.
- Submit (or `--create-packagist-packages`) only **published package** GitHub URLs on `tyhpproject-packages`. For Composer-libs that is **two** Packagist packages per library: `tyhpdef/<vendor>-<name>` (public metapackage, upstream version) and `tyhpdef/<vendor>-<name>-impl` (four-part). Also `tyhpproject-packages/core` → `tyhp/core`, `tyhpproject-packages/php` → `tyhpdef/php`, plus php-ext repos. First `tyhp/*` submit claims the vendor namespace. Do not submit `*-impl` as something apps should require; it is still a real Packagist package the public meta `require`s.
- **Recreate, then retarget.** Package repos that already exist on `tyhpproject` are created again on `tyhpproject-packages` with the same repo name, then Packagist’s repository URL for that Composer package is changed from `https://github.com/tyhpproject/<repo>` to `https://github.com/tyhpproject-packages/<repo>`. A Composer name already on Packagist is an update, not a second submit. New packages that have never been submitted are created against the new org only.
- Command reminder (post-move): from `tyhp-runtime-src`, after a compiler build **and** after `GITHUB_ORG` is `tyhpproject-packages`, `./scripts/publish-runtime-packages.sh --create-github-repos --create-packagist-packages` (or `--list` first). Until that constant changes, `--create-github-repos` would still create repos on `tyhpproject`.
- Compiler `README.md` package-table links (`tyhpdef/php`, `tyhp/core`, `tyhp/async`, `tyhp/decimal`, `tyhp/lambda`) use `https://github.com/tyhpproject-packages/<repo>` once those repos exist.
- If you already submitted URLs before a history rewrite of a **package** repo, Packagist has cloned that git; a later force-push/wipe of a package repo does not un-clone old history — only relevant if a published package repo is rewritten (the four source-repo force-pushes are stub-only and are not Packagist packages).
- Spot-check [packagist.org/packages/tyhp/](https://packagist.org/packages/tyhp/) and `tyhpdef/` after the first creates.

End the markdown with a short “order to click through” so CLA linking is not forgotten until after Packagist, and Packagist is not run against the compiler or `*-src` remotes.

### Acceptance Criteria

- [x] `~/repos/tyhp-post-split-manual-steps.md` exists with CLA, GitHub app, Packagist, **and** runtime-src ownership (labels, pinned report issue, `UPSTREAM_TYHPDEF_REPORT_ISSUE`) checklists
- [x] The checklist names `tyhpproject-packages` as the published-package org, lists recreating existing `tyhpproject` package repos there, and lists updating Packagist repository URLs rather than re-submitting existing Composer names
- [x] The file is not committed to `tyhp` or the four new repos

---

## Out of scope (v1)

- Actually signing CLA, installing GitHub apps, or submitting Packagist packages (human; Phase 6 writes the checklist)
- Publishing the VS Code Marketplace / JetBrains plugin (later `TODO.md` items)
- Compiler history wipe / `scripts/release.sh` / merging the Tier 2 branch (still `ALPHA_RELEASE.md` / `TODO.md` human tasks)
- Git submodules that re-couple the four trees into `tyhp`
- Moving `Tyhp/LanguageServer/` out of the compiler repo
- Actually handing off a live owner package on Packagist (scripts + GitHub setup must exist; a real bundled/sibling cutover can wait for a requester)
- Story 22 web playground
- Story 30 docs polish beyond retargeting links that this extract breaks

---

## Dependencies

- **Requires:** Story **27.2** (extension syntax rewrite of runtime/docs/guide finished); Story **27.1** (type-language rewrites of those trees); Story **19.5** (IDE clients); Story **21** (package trees). Finish in-flight `runtime/packages/` work before Phase 1; finish LSP polish before Phase 4 if still open.
- **Provides:** `tyhp-runtime-src`, `tyhp-docs-src`, `tyhp-ai-dev-guide`, `tyhp-ide-plugin` as standalone source repos on `tyhpproject`; published `tyhp/*` and `tyhpdef/*` repos on `tyhpproject-packages` (existing `tyhpproject` package repos recreated there; Packagist URLs retargeted); compiler repo without those trees; `TYHP_RUNTIME_SRC` discovery; diagnostics-reference sync; post-split manual checklist; public metapackage + `*-impl` publish path; `--vendor` ownership contract (`extra.tyhp.tyhpdef`, no auto-install when bundled, never root-require impl)
- **Unblocks:** Packagist publish of runtime packages including Composer-lib **pairs** from `tyhpproject-packages` (human, Phase 6 checklist); owner handoff process on runtime-src; docs site publish from docs-src; later Marketplace / JetBrains publish from ide-plugin; Story 30 polish against the split layout
- **Packages:** after Phase 1, self-host golden and `test-all-tyhpdef.sh` live in runtime-src (ROADMAP runtime-package phase 3)
