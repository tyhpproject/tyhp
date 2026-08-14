# Alpha release walkthrough (`805.0.0-alpha.1`)

This file is the HUMAN checklist. The compiler repo already has the AI prep (docs, `tyhp/php`, scripts, CI). **Do not skip the history wipe.** Do not run the publish/release scripts until the matching step below.

GitHub org is **`tyhpproject`**. Composer vendor is **`tyhp/`**. Those names stay different on purpose.

## 0. Packagist account (can be done anytime; submit packages last)

1. Create a [Packagist](https://packagist.org) account.
2. Log in with GitHub and grant Packagist access to the **`tyhpproject` organization** (GitHub → Settings → Applications → Packagist → Organization access).
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
2. Optional: link the same Gist to `tyhpproject/{core,async,decimal,lambda,php}` if those repos will take outside PRs. The compiler repo is the one that matters for alpha.
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
git remote add origin git@github.com:tyhpproject/tyhp.git
git push -u origin main
```

Do **not** `git push --force` rewritten history to a repo that was ever public or forked.

## 3. Publish runtime packages

Empty private sibling repos already exist: `tyhpproject/{core,async,decimal,lambda,php}`.

1. Flip those five repos to **Public**.
2. From a compiler checkout that can build:

```bash
dotnet build tyhp.csproj
scripts/publish-runtime-packages.sh
```

That script builds `runtime/packages/build-all.sh`, clones each sibling repo into a temp dir, rsyncs installable files only, commits `main`, tags **`805.0.0-alpha.1`** (no `v` prefix — Packagist version = git tag), and pushes.

3. On Packagist, submit each package repo URL:

| GitHub repo | Composer name |
|-------------|---------------|
| `https://github.com/tyhpproject/core` | `tyhp/core` |
| `https://github.com/tyhpproject/async` | `tyhp/async` |
| `https://github.com/tyhpproject/decimal` | `tyhp/decimal` |
| `https://github.com/tyhpproject/lambda` | `tyhp/lambda` |
| `https://github.com/tyhpproject/php` | `tyhp/php` |

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

`tyhpproject/tyhp-docs` is already public (CNAME `tyhplang.com`). This deploy does not wait on the compiler wipe, but running it after honest docs is enough:

```bash
scripts/publish-docs.sh
```

Needs PHP, Composer, and `sass`. It replaces the “Coming Soon” landing page with the generated docs TOC.

## Order summary

1. Packagist account + org access (no submit yet)
2. CLA Gist + install CLA Assistant on the `tyhpproject` org (link the repo only after the public compiler repo exists)
3. Merge to `main`
4. History wipe + new public `tyhpproject/tyhp`
5. Link [cla-assistant.io](https://cla-assistant.io/) to `tyhpproject/tyhp` (re-link if the repo was recreated)
6. Flip package repos public → `publish-runtime-packages.sh` → Packagist submit
7. `scripts/release.sh 805.0.0-alpha.1`
8. `scripts/publish-docs.sh` (if the live site is still stale)
