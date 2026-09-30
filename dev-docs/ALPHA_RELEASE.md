# Alpha release walkthrough (`805.0.0-alpha.1`)

This file is the HUMAN checklist. The compiler repo already has the AI prep (docs, `tyhpdef/php`, scripts, CI). **Do not skip the history wipe.** Do not run the publish/release scripts until the matching step below.

The compiler and package **source** live on GitHub org **`tyhpproject`**. Published `tyhp/*` and `tyhpdef/*` package repositories live on **[`tyhpproject-packages`](https://github.com/tyhpproject-packages)**. Composer vendors stay **`tyhp/`** (runtime + compiler) and **`tyhpdef/`** (PHP stubs, extension stubs, library companions). Those names stay different from either GitHub org on purpose.

## 0. Packagist account (can be done anytime; submit packages last)

1. Create a [Packagist](https://packagist.org) account.
2. Log in with GitHub and grant Packagist access to the **`tyhpproject-packages` organization** (GitHub → Settings → Applications → Packagist → Organization access). That is the org Packagist clones. Access on `tyhpproject` does not cover the package repos.
3. Spot-check [packagist.org/packages/tyhp/](https://packagist.org/packages/tyhp/) — it should be unused. The first submitted `tyhp/*` package claims the vendor.
4. **Do not submit any package URLs yet.** Packagist clones git; a later history wipe does not un-clone old history.

## CLA Assistant ([cla-assistant.io](https://cla-assistant.io/))

Use the **hosted** SAP service, not a self-hosted instance. [`CONTRIBUTING.md`](../CONTRIBUTING.md) already tells contributors they will be prompted on a pull request.

Signatures live in CLA Assistant’s database (not in git). A history wipe does **not** erase signed CLAs, but **re-link the new public repo** if you delete and recreate `tyhpproject/tyhp`.

### Anytime (Gist + GitHub App)

1. Write the CLA text. CLA Assistant’s FAQ points at [contributoragreements.org](https://contributoragreements.org/) for a fill-in-the-blank CLA. Have counsel review it. Tyhp is Apache-2.0 (`LICENSE.txt`); the CLA should grant permission under that license. Do not invent the legal wording in this repo.
2. Create a GitHub **Gist** at [gist.github.com](https://gist.github.com) whose body is that CLA. Keep the Gist URL; changing the Gist later forces every contributor to re-sign.
3. Optional: add a second Gist file named `metadata` (JSON) if you need extra signer fields (name, email, employer vs individual). See [CLA Assistant custom fields](https://github.com/cla-assistant/cla-assistant#request-more-information-from-the-cla-signer).
4. Sign in at [cla-assistant.io](https://cla-assistant.io/) with GitHub.
5. Install the [CLA Assistant GitHub App](https://github.com/apps/cla-assistant) on the **`tyhpproject` organization** (not only your user). In GitHub: Settings → Applications (or the org’s Installed GitHub Apps) and grant the app access to the org. Without org access it cannot comment on PRs in `tyhpproject/tyhp`.

### After the public compiler repo exists (link)

Do this **after** step 2 (new public `tyhpproject/tyhp`). Linking the old private repo is wasted work if you then delete it.

1. On [cla-assistant.io](https://cla-assistant.io/), configure/link a CLA:
   - Repository: `tyhpproject/tyhp`
   - CLA: the Gist from above (https://gist.github.com/pristinesource/2e15358ca36027e181773c01b6309872)
   - Require a signature for any file change (minimum files = 1 is the usual setting)
2. Optional: link the same Gist to published package repos on `tyhpproject-packages` (`core`, `async`, `decimal`, `lambda`, `php`, and later php-ext / composer-lib repos) if those will take outside PRs. That requires the CLA Assistant app on **`tyhpproject-packages`**, not only on `tyhpproject`. The compiler repo is the one that matters for alpha.
3. In the CLA Assistant dashboard, **allow bot users** that cannot sign: at least `dependabot[bot]` (and `github-actions[bot]` if a workflow opens PRs).
4. Open a throwaway PR from a second GitHub account (or an unsigned account) and confirm:
   - CLA Assistant comments on the PR
   - the commit status stays pending until the CLA is accepted
   - after signing, the check goes green
5. Export signees from the dashboard (CSV) if you want an offline copy; do not commit signature lists to git.

If the Gist text changes, CLA Assistant treats that as a new CLA version and asks contributors to sign again on their next PR.

## 1. Merge to `main` (still private / pre-wipe)

Current work lives on TBD.

```bash
git checkout main   # create it from this branch if it does not exist
git merge develop/anthonyrainer/initial
# do not push to a public remote until after the wipe
```

Confirm the merge includes:

- `.gitignore` entries for `.cursor/` and `dev-docs/RESOLVED_BUGS.md`
- no `DebugProject/vendor/`
- `dev-docs/ALPHA_RELEASE.md`, `scripts/release.sh`, `scripts/publish-*.sh`, `Makefile`

## 2. History wipe (last local git step)

Deleting branches does not erase objects, PRs, or Actions logs. A new empty public repo plus a new local `git init` is the only way old history never ships.

**Must not be in the first public commit** (spot-check before wipe): `.env`, credentials, USB paths, `DebugProject/vendor`, `.cursor/`, `dev-docs/RESOLVED_BUGS.md`, `docs/output`, `runtime/packages/dist`, `TestResults`, agent transcripts (those live outside this repo).

```bash
git status
# optional: git grep for secrets / personal paths
# copy the tree somewhere as a backup

cd /path/to/tyhp
rm -rf .git
git init -b main
git add -A
git commit -m "Tyhp 805.0.0-alpha.1"
```

On GitHub:

1. Delete the old private `tyhpproject/tyhp` repo (or leave it private forever and never connect it). Prefer delete + recreate.
2. Create a **new public** repo `tyhpproject/tyhp` with **no** README, license, or `.gitignore`.
3. Then:

```bash
git remote add origin https://github.com/tyhpproject/tyhp.git
git push -u origin main
```

Do **not** `git push --force` rewritten history to a repo that was ever public or forked.

## 3. Publish runtime packages

Published package repos belong on **`tyhpproject-packages`**, not `tyhpproject`. Repos that already exist as `tyhpproject/{core,async,decimal,lambda,php}` (and any other published package repo) are re-created on `tyhpproject-packages` with the same names. Packagist packages that already point at `https://github.com/tyhpproject/<repo>` are updated to `https://github.com/tyhpproject-packages/<repo>`. Do not submit a second Packagist package for a Composer name that already exists.

`publish-runtime-packages.sh`, `ensure-existing-github-repos.sh`, and `new-proposed-composer-libs.sh` still set `GITHUB_ORG=tyhpproject` until Story 27.3 changes that constant. Until then, do not pass `--create-github-repos` (it would create package repos on `tyhpproject`).

1. Create the package repos on `tyhpproject-packages` and make them **Public** (re-create any that already exist on `tyhpproject`).
2. After Story 27.3 sets package-repo `GITHUB_ORG` to `tyhpproject-packages`, from a compiler checkout that can build:

```bash
dotnet build tyhp.csproj
scripts/publish-runtime-packages.sh
```

That script runs `runtime/packages/base-build-all.sh` (PHP 8.2, 8.3, 8.4, and 8.5 dist trees) for compiled
`tyhp/*` helpers, then for **each** sibling repo commits and tags Packagist versions (no `v` prefix —
Packagist version = git tag).

Compiled packages (`core`, `async`, `decimal`, `lambda`) get **four** tags `80N.{X.Y}` from **that
package's** `composer.json`, not the compiler version. With source `0.0` that is:

| Git tag | PHP constraint |
|---------|----------------|
| `802.0.0` | `~8.2.0` |
| `803.0.0` | `~8.3.0` |
| `804.0.0` | `~8.4.0` |
| `805.0.0` | `~8.5.0` |

`main` is left on the 805 tree. Libraries OR PHP majors and keep **that package's** X
(`803.0.* || 804.0.* || 805.0.*` while it is on `0.y`). Packages can bump `X.Y` without a compiler release.

`tyhpdef/*` packages (`php`, `php-ext-*`, Composer-lib wrappers) get **one** tag: the `version` field
in that package's `composer.json` (for example `0.0.1` or `0.1`). They keep `"php": ">=8.2"` and do
not use `80N.X.Y`.

Composer-lib wrappers live at `runtime/packages/<vendor>-<name>/<upstream>/` (one folder per
upstream version; folder name is the upstream string, e.g. `psr-log/3.0.2`). They publish to a
single `tyhpproject-packages/<vendor>-<name>` repo. The git tag is the Tyhp four-part version from that
folder's `composer.json` (for example `3.0.2.0`). PHP-ext, `php`, `core`, `async`,
`decimal`, and `lambda` stay flat (no version folder). Missing GitHub repos are skipped with a
warning. `--create-github-repos` creates the missing repo on `GITHUB_ORG` (empty public `gh repo create`,
no README / license / `.gitignore`). Pass that flag only after `GITHUB_ORG` is `tyhpproject-packages`.
Pass `--package php` to publish only `tyhpdef/php`.

3. On Packagist, submit each **new** package repo URL, or pass `--create-packagist-packages` (reads
   gitignored `scripts/packagist.credentials` with the MAIN API token). For a Composer name that
   already points at `tyhpproject/<repo>`, update that package’s repository URL instead of submitting again:

| GitHub repo | Composer name |
|-------------|---------------|
| `https://github.com/tyhpproject-packages/core` | `tyhp/core` |
| `https://github.com/tyhpproject-packages/async` | `tyhp/async` |
| `https://github.com/tyhpproject-packages/decimal` | `tyhp/decimal` |
| `https://github.com/tyhpproject-packages/lambda` | `tyhp/lambda` |
| `https://github.com/tyhpproject-packages/php` | `tyhpdef/php` |

Do **not** submit `tyhpproject/tyhp` — Packagist reads the root `composer.json`, and this repo is the .NET compiler.

## 4. GitHub Release (compiler binaries)

After `origin` is the new public repo:

```bash
scripts/release.sh 805.0.0-alpha.1
```

That builds 10 artifacts (RID × self-contained / framework-dependent), tags **`v805.0.0-alpha.1`**, and runs `gh release create --prerelease`.

Install (does **not** use `/releases/latest`, which hides prereleases):

```bash
curl -fsSL https://raw.githubusercontent.com/tyhpproject/tyhp/main/scripts/install.sh | bash -s --
```

## 5. Docs site

`tyhpproject/tyhp-docs` is already public (CNAME `tyhplang.com`). The site source is [tyhp-docs-src](https://github.com/tyhpproject/tyhp-docs-src). This deploy does not wait on the compiler wipe, but running it after honest docs is enough. From that checkout:

```bash
scripts/publish-docs.sh
```

Needs PHP, Composer, `sass`, and `zip`. It replaces the “Coming Soon” landing page with the generated docs TOC.

## CI polish (optional, after the public compiler repo exists)

`.github/workflows/tests.yml` already runs `dotnet test` on push and pull_request. That is not an alpha-publish blocker. When outside contributors show up, consider adding (same workflow file, not a new one; not Story 21.12 / not Story 31):

- PHP on the runner if Story 21.12 Workstream D (emit-and-run) is not already wired
- A separate PHPUnit job (`PhpUnit_RuntimePackages_AllPass` stays skipped until FOUND #1 / #2 are decided)
- Coverage report (no coverage gate required for alpha)
- Extra OS runners (`windows-latest`, `macos-latest`) if the suite is actually green there

Do not un-skip FOUND #1 / #2 to make CI look greener.

## Order summary

1. Packagist account + org access (no submit yet)
2. CLA Gist + install CLA Assistant on the `tyhpproject` org (link the repo only after the public compiler repo exists)
3. Merge to `main`
4. History wipe + new public `tyhpproject/tyhp`
5. Link [cla-assistant.io](https://cla-assistant.io/) to `tyhpproject/tyhp` (re-link if the repo was recreated)
6. Re-create package repos on `tyhpproject-packages` (Story 27.3 retargets `GITHUB_ORG`) → `publish-runtime-packages.sh` → Packagist create or **update repository URL** to `tyhpproject-packages` (do not re-submit a Composer name that already exists)
7. `scripts/release.sh 805.0.0-alpha.1`
8. From `tyhp-docs-src`: `scripts/publish-docs.sh` (if the live site is still stale)
