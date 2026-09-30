# Implementation Plan: Story 21.11

> **Roadmap position:** Story 21.11 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story **21.10**, before Story **21.12**). **Implement after 21.10.**
> **Direct dependencies (new numbering):** 08 (checker, `ImplementsOrExtends`, attributes), 10 (`tyhp.json`), 13 / **21.5** (Composer toolchain), 20 (Track C `package.tyhpdef`), **21** (`tyhpdef/php` + `tyhpdef/php-ext-*`), **21.6** / **21.7** (Traversable / Stringable auto-implement pattern), **21.10** (`extra.tyhp.require`, plugin, Track C `extern`)
> **Bucket story:** 21.11 collects independent post-21.10 workstreams. Workstreams do not share a single theme. Implement (or skip) each one on its own; adding a later workstream must not force a rewrite of earlier ones. Language-hooks inventory: `PHP_LANGUAGE_HOOKS.md`.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-09-14
> **Last design lock:** 2026-09-14 — Workstream A locked (`extra.tyhp.package`). Workstreams B–D locked from `PHP_LANGUAGE_HOOKS.md` remainder (UnitEnum / BackedEnum, engine attributes, stdClass). Workstream E locked: XDebug proxy must redact `#[\SensitiveParameter]` (Story 18 will not be revisited). Workstream F locked: duplicate diagnostics carry a secondary span on the first declaration. Items listed under [Hooks remainder — not in 21.11](#hooks-remainder--not-in-2111) stay **Open** on the hooks inventory, not scheduled here.
> **Status:** **Workstreams A–F design locked.** Do **not** implement until this story is opened for implementation. Other workstreams may still be appended.
> **Prerequisites:** Story 21.10 (`extra.tyhp.require`, plugin ignores unknown `extra.tyhp` keys). Workstreams B–D need 21.6 Traversable listing + 21.7 Stringable auto-implement as the pattern to copy. Workstream E needs the shipped Story 18 proxy (`VariableTranslator` / `StackFrameTranslator`). Workstream F needs Story 14 diagnostic labels (`Diagnostic.Labels`, `CLI_DiagnosticLabelDeclaredHere`).
> **Consumers:** every Tyhp library and tyhpdef wrapper; `tyhpdefInclude` / vendor discovery; overlay CLI; runtime package scripts; Story 25 (`internalOverlay` on this same object); Story 23 (must use E’s redaction helper if it reconstructs frames); Story 30 (docs); checker users of enums, engine attributes, and `\stdClass`; debug sessions that inspect `#[\SensitiveParameter]` arguments; anyone hitting a duplicate-declaration diagnostic.

---

## How to extend this story

This file is a **bucket**. Unrelated work belongs here as a new **Workstream**, not as extra phases stuffed into Workstream A.

To add work later, before implementation:

1. Add a row to [Workstream inventory](#workstream-inventory) (`B`, `C`, …). Do **not** renumber existing letters.
2. Add a ToC link.
3. Paste a new top-level `## Workstream X — <title>` section using the [Workstream template](#workstream-template). Fill summary, decisions, phases (`X.1`, `X.2`, …), and acceptance tests inside that section.
4. Keep inner phase numbers local to that workstream (`A.1` stays `A.1` even if Workstream B is inserted in the inventory above it).
5. Shared closing sections ([Cross-Story References](#cross-story-references), repo-wide notes) may gain a bullet; do not merge two workstreams’ phase lists.

Do not fold unrelated tasks into Workstream A’s phases.

---

## Table of Contents

- [How to extend this story](#how-to-extend-this-story)
- [Workstream inventory](#workstream-inventory)
- [Workstream template](#workstream-template)
- [Workstream A — `extra.tyhp.package` (tyhpdef package manifest)](#workstream-a--extratyhppackage-tyhpdef-package-manifest)
  - [A. Summary](#a-summary)
  - [A. Motivation](#a-motivation)
  - [A. What this is not](#a-what-this-is-not)
  - [A. What already exists](#a-what-already-exists)
  - [A. Scope (In / Out)](#a-scope-in--out)
  - [A. Decisions (locked)](#a-decisions-locked)
  - [A. Target contract](#a-target-contract)
  - [A. Discovery and load](#a-discovery-and-load)
  - [A. Library emit / overlay edit](#a-library-emit--overlay-edit)
  - [A. Runtime packages and scripts](#a-runtime-packages-and-scripts)
  - [A. Greenfield hygiene](#a-greenfield-hygiene)
  - [A. Story 25 pointer](#a-story-25-pointer)
  - [A. Phases](#a-phases)
  - [A. Golden fixtures / tests](#a-golden-fixtures--tests)
- [Workstream B — UnitEnum / BackedEnum auto-implement](#workstream-b--unitenum--backedenum-auto-implement)
- [Workstream C — PHP engine attributes](#workstream-c--php-engine-attributes)
- [Workstream D — `\stdClass` contract](#workstream-d--stdclass-contract)
- [Workstream E — XDebug `#[\SensitiveParameter]` redaction](#workstream-e--xdebug-sensitiveparameter-redaction)
- [Workstream F — Duplicate diagnostics name the original location](#workstream-f--duplicate-diagnostics-name-the-original-location)
- [Hooks remainder — not in 21.11](#hooks-remainder--not-in-2111)
- [Cross-Story References](#cross-story-references)

---

## Workstream inventory

| ID | Title | Status | Notes |
|----|-------|--------|--------|
| **A** | Fold the tyhpdef package manifest into `composer.json` `extra.tyhp.package` | **Design locked** | Greenfield. Rebuild `runtime/packages/*` to migrate; no version bump; no publish. |
| **B** | `\UnitEnum` / `\BackedEnum` engine auto-implement + listing/override reject | **Design locked** | Same pattern as Traversable (21.6) and Stringable (21.7). No `match` exhaustiveness. |
| **C** | PHP engine attributes: `#[\Deprecated]`, `#[\Override]`, `#[\NoDiscard]`, `#[\DelayedTargetValidation]` | **Design locked** | Close the **Open** / remaining **Partial** rows in `PHP_LANGUAGE_HOOKS.md` §5. |
| **D** | `\stdClass` named undeclared-property gate + `(object)` → `\stdClass` | **Design locked** | Only exact `\stdClass` (not subclasses). See [Hooks remainder](#hooks-remainder--not-in-2111) if PHP-matching subclasses are wanted instead. |
| **E** | XDebug proxy redacts `#[\SensitiveParameter]` / does not unwrap `\SensitiveParameterValue` | **Design locked** | Security. Story 18 is shipped and will not be revisited; do not leave this on 18 or 23. |
| **F** | Duplicate diagnostics include the original declaration’s location | **Design locked** | Secondary span / “declared here”; do not stuff `file:line` into the short message. |
| **G+** | *(add rows here)* | Not started | Independent of A–F. Use the template below. |

---

## Workstream template

Copy this block when adding Workstream G (then H, …). Replace `X` with the letter.

```markdown
## Workstream X — <short title>

> **Status:** Draft / Design locked
> **Depends on:** <21.10, Workstream A, nothing, …>

### X. Summary

<what and why, current contract only>

### X. Decisions (locked)

| Topic | Decision |
|-------|----------|
| | |

### X. Phases

#### X.1 — <name>

- [ ] …

#### X.2 — Documentation and AIDevGuide

Per `CONVENTIONS.md` §10. Name the pages. Write "no change required" if that is true.

### X. Golden fixtures / tests

- [ ] …
```

---

## Workstream A — `extra.tyhp.package` (tyhpdef package manifest)

> **Status:** **Design locked.**
> **Depends on:** 21.10 (plugin already ignores unknown `extra.tyhp` keys).

The current tree still has `package.tyhp.json` next to `composer.json`. That file lists which tyhpdefs a **published package** contributes (`include` / `exclude` / `overlay` / `source.tagless`). Workstream A **moves that object onto `extra.tyhp.package`**, makes `composer.json` the only manifest the compiler reads or writes for that contract, deletes `package.tyhp.json` everywhere, and rewrites every lookup, script, fixture, and doc to the new key. The rest of this workstream may say “sidecar” to mean that file.

`tyhp.json` stays the **author** compiler project file. `package.tyhpdef` (the tyhpdef **source** file) is unchanged. `extra.tyhp.require` / `interopContractVersion` / `php-version` / `supported-minors` / `extensions` stay as they are.

No one is using Tyhp yet. **Greenfield:** no dual-read, no deprecation window, no “legacy filename” comments in shipped surfaces.

---

### A. Summary

A Composer package that ships tyhpdefs declares them on that package’s `composer.json`:

```json
{
    "name": "tyhpdef/php-ext-gd",
    "extra": {
        "tyhp": {
            "interopContractVersion": 1,
            "php-version": ">=8.2",
            "supported-minors": ["8.2", "8.3", "8.4", "8.5"],
            "extensions": ["gd"],
            "require": {
                "tyhpdef/php": "@dev"
            },
            "package": {
                "include": [
                    "./_tyhpdef/*.tyhpdef",
                    "./_tyhpdef/extensions/*.tyhpdef"
                ],
                "overlay": [
                    "./_tyhpdef/overlays/stubs/*.tyhpdef",
                    "./_tyhpdef/overlays/*.tyhpdef"
                ]
            }
        }
    }
}
```

A compiled library after `tyhp build`:

```json
{
    "name": "tyhp/decimal",
    "extra": {
        "tyhp": {
            "interopContractVersion": 1,
            "require": {
                "tyhpdef/php": "@dev"
            },
            "package": {
                "include": ["./package.tyhpdef"],
                "exclude": [],
                "overlay": [],
                "source": {
                    "tagless": false
                }
            }
        }
    }
}
```

- **`extra.tyhp.package`** is the tyhpdef package manifest (globs relative to the directory that contains this `composer.json`).
- **Presence of `extra.tyhp.package` as a JSON object** is the sentinel that this install contributes tyhpdefs. `extra.tyhp` without `package` is **not** a tyhpdef package (plugin metadata only).
- Vendor discovery reads `vendor/*/*/composer.json`. Explicit `tyhpdefInclude` / promoted `include` entries name `composer.json` files (or globs that match them), not a sidecar filename.
- Library `tyhp build` additive-merges generated keys into `extra.tyhp.package` on the publish-directory `composer.json`. Applications with `build.generateTyhpdef` still write **only** `package.tyhpdef` (no `extra.tyhp.package`).

---

### A. Motivation

Author compile config (`tyhp.json`) and Composer-facing metadata (`extra.tyhp.require`, interop, php-version) are already split correctly. The remaining sidecar duplicates `composer.json` for a contract that is **published with the package** and is always next to it. Putting that object on `extra.tyhp.package`:

- Gives vendor / explicit load a file that already exists (`composer.json`).
- Keeps plugin extras, interop, and tyhpdef globs in one published object.
- Leaves `tyhp.json` alone (nested test projects, PHP-minor matrix configs, conformance fixtures).

The key is **`package`**, not `tyhpdef`: `extra.tyhp.require` is Composer package names; `extra.tyhp.package` is this package’s tyhpdef load spec. Reusing `tyhpdef` on that object would collide in conversation with `package.tyhpdef` and with `tyhpdefInclude`.

---

### A. What this is not

Do not re-open or sneak in:

- Folding **`tyhp.json`** into `extra.tyhp` (author compile config, multiple projects per Composer package, language-server nested ownership).
- Naming the object `extra.tyhp.tyhpdef`.
- Dual-read of the old sidecar, a compatibility shim, or diagnostics that mention a removed filename.
- Changing `package.tyhpdef` generation, Track C classification, or `extra.tyhp.require` semantics (21.10).
- Bumping any runtime package `version`, creating git tags, `git push`, or Packagist / GitHub publish.
- Implementing Story 25 (`internalOverlay` load, `includeInternals`, checker 4054). Only retarget that plan’s key to `extra.tyhp.package` so 25 does not resurrect a sidecar.
- Teaching the Composer plugin to install from `extra.tyhp.package` (it still walks only `extra.tyhp.require` + runtime `require`).
- Auto-discovering tyhpdefs from a `composer.json` that has `extra.tyhp` but no `package` object.

---

### A. What already exists

| Surface | Today (current tree, for implementers) |
|---------|----------------------------------------|
| `tyhp.json` | Author project. Unchanged in this workstream. |
| Sidecar next to `composer.json` | `include` / `exclude` / `overlay` / `source.tagless`. Vendor sentinel filename. Explicit `tyhpdefInclude` paths. Library build additive-merges it (`PackageTyhpJsonManifest`). |
| `extra.tyhp` | `interopContractVersion`, `require`, `php-version`, `supported-minors`, `extensions`. Plugin reads **only** `require`. Unknown keys ignored. |
| `package.tyhpdef` | Public Track C / hand tyhpdef file. **Stays.** Often listed in the package manifest `include`. |
| Overlay CLI | Appends hand overlay globs to the sidecar (or to `tyhp.json` `overlay`). |
| `runtime/packages/*` | Every wrapper and compiled helper has the sidecar; tests/`tyhp.json` and conformance fixtures path-include it. |
| Scripts | `runtime/packages/build-common.sh` `copy_dist_tyhpdefs`; `new-php-ext.sh`; `new-composer-lib.sh`; `scripts/publish-runtime-packages.sh`. |

---

### A. Scope (In / Out)

| In scope | Out of scope |
|----------|----------------|
| `extra.tyhp.package` object (same inner keys as the current sidecar) | `extra.tyhp.tyhpdef`; folding `tyhp.json` |
| Compiler read + library write + overlay edit against `composer.json` | Dual-read / deprecation |
| Vendor + explicit discovery via `composer.json` that has `extra.tyhp.package` | Treating every `composer.json` as a tyhpdef package |
| All tests, conformance `tyhp.json` includes, CLI help/resx, AIDevGuide, user docs, technical guides | Story 25 implementation |
| `runtime/packages/**` scripts and `scripts/**` | Version bumps; tags; push; Packagist |
| Migrate every `runtime/packages/*/` sidecar into that package’s `composer.json`; delete sidecar; rebuild compiled packages | Publishing those packages |
| Repo-wide purge of the sidecar filename from **shipped** surfaces (see [A. Greenfield hygiene](#a-greenfield-hygiene)) | Rewriting this 21.11 file’s migration wording |

---

### A. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Key path | **`extra.tyhp.package`** — a JSON **object**. Not `extra.tyhp.tyhpdef`. |
| Inner schema | Same as the current sidecar: `include`, `exclude`, `overlay` (arrays of globs), optional `source.tagless` (bool, nested `source` object). Omitted arrays mean empty. Unknown inner keys: ignore at load (same spirit as unknown `extra.tyhp` keys). |
| Globs | Relative to the directory that contains the `composer.json` being read. |
| Sentinel | `extra.tyhp.package` is present **and** a JSON object. Missing, `null`, or a non-object → this package does not contribute tyhpdefs. |
| `extra.tyhp` without `package` | Plugin/interop-only. Do **not** load tyhpdefs. Do **not** default `include` to `./package.tyhpdef`. |
| Vendor discovery | Scan `vendor/<vendor>/<package>/composer.json` (same two-level layout as today). Load when the sentinel is true. |
| Explicit include | `tyhp.json` `tyhpdefInclude` and promoted `include` patterns that resolve to `composer.json` (path or glob). Skip files that are not `composer.json` or that lack the sentinel. Promote `include` patterns that end with `composer.json` the same way today’s patterns that end with the sidecar filename are promoted. |
| Own-project `composer.json` | Do not auto-load the compiling project’s `composer.json` as a tyhpdef package unless it is listed in `tyhpdefInclude` / vendor. Libraries still **write** `extra.tyhp.package` for **consumers**. |
| Library emit | `"type": "library"` always additive-merges generated `extra.tyhp.package` into **publish-directory** `composer.json` (`output.publishPath`, default project root). Same merge rules as today’s sidecar: add missing keys and missing array **string** items; never remove or change existing values. |
| Generated default | `{ "include": ["./package.tyhpdef"], "exclude": [], "overlay": [], "source": { "tagless": <project source.tagless> } }`. |
| Missing `composer.json` at publish path | Create a minimal object that contains `extra.tyhp.package` (and keep/merge `name` if `ComposerJsonService` already writes one). Do **not** invent `version`. |
| Other `extra` / `extra.tyhp` keys | Untouched: `require`, `interopContractVersion`, `extra.class`, `php-version`, etc. |
| Applications | `build.generateTyhpdef` writes `package.tyhpdef` only. Do **not** write `extra.tyhp.package`. |
| Overlay CLI | When the target is a package manifest, edit `extra.tyhp.package.overlay` on that `composer.json`. Project overlays still edit `tyhp.json` `"overlay"`. |
| Plugin | Still reads **only** `extra.tyhp.require`. Must not iterate `package` as if it were a require map. Add/keep a test that `package` is ignored. |
| Greenfield | Compiler never opens the old sidecar. No warning naming it. No “migrated from” comments in code or user docs. |
| Runtime migrate | Copy each package’s sidecar object onto `extra.tyhp.package` (create `extra` / `tyhp` as needed); then **delete** the sidecar. Then rebuild compiled packages so emit/merge is proven. |
| Versions / publish | **Do not** change any package `"version"`. **Do not** `git tag`, `git push`, or run publish scripts against remotes. Local dist copy via existing `build-*.sh` is allowed if that is how rebuild works today. |
| Story 25 | `internalOverlay` (when 25 lands) lives on **`extra.tyhp.package`**, not a sidecar. Update `IMPLEMENTATION_PLAN_TODO_STORY_25.md` (and 21.10 pointers) to that contract in present tense. Do not implement 25. |
| `CONVENTIONS.md` | Record `extra.tyhp.package` next to `extra.tyhp.require`. |

---

### A. Target contract

#### Shape

`extra.tyhp.package` is optional. When present it is:

```json
"package": {
    "include": ["./package.tyhpdef"],
    "exclude": [],
    "overlay": [],
    "source": { "tagless": false }
}
```

| Inner key | Role |
|-----------|------|
| `include` | Tyhpdef / `.tyhp` globs loaded as this package’s baseline (last-wins still happens in **overlay**). |
| `exclude` | Dropped after discovery. |
| `overlay` | Loaded after include, array order, last Tyhp name wins. |
| `source.tagless` | Optional; same meaning as `tyhp.json` `source.tagless` for files loaded from this package. |

`package.tyhpdef` remains the conventional Track C output filename listed in `include`. That is a **tyhpdef file**, not a JSON manifest.

#### `tyhp.json` explicit includes (tests / this repo)

Extension tests and the repo-root project currently path-include the sidecar. After A they path-include `composer.json`:

```json
"include": [
    "./test_imagick.tyhp",
    "../composer.json",
    "../../php/composer.json"
]
```

Repo root `tyhp.json` `tyhpdefInclude` lists `./runtime/packages/php/composer.json` (and core / decimal / async / lambda) instead of a sidecar path.

#### Examples

**tyhpdef-only wrapper** — see [A. Summary](#a-summary) (`tyhpdef/php-ext-gd`). Hand `include` / `overlay` stay author-edited on `extra.tyhp.package`. Track C does not overwrite them.

**compiled `tyhp/core`** — generated `include: ["./package.tyhpdef"]`; existing `extra.tyhp.require` and `extra.class` remain siblings under `extra` / `extra.tyhp`.

---

### A. Discovery and load

Replace `Tyhpdef.PackageLoading` / overlay / vendor-skip helpers that key off a sidecar **filename** with:

1. **Vendor:** `vendor/<vendor>/<package>/composer.json` with sentinel `extra.tyhp.package` object.
2. **Explicit:** `tyhpdefInclude` / promoted `include` matches whose resolved path is a `composer.json` with that sentinel.
3. **Load:** parse `extra.tyhp.package`, resolve `include` / `overlay` / `exclude` globs relative to that `composer.json`’s directory, honor `source.tagless`.
4. **Skip:** `composer.json` missing, unreadable, or `extra.tyhp.package` not an object — not a tyhpdef package (no diagnostic required for ordinary vendor packages; malformed JSON already has existing read errors if this path was an **explicit** include).

`VendorTyhpdefGenerator` “this install already ships tyhpdefs” (skip `--vendor` stub generation) uses the same sentinel, not a filename.

Rename `PackageTyhpJsonManifest` (and tests) to something that does not contain the old filename — e.g. operate on `extra.tyhp.package` inside `composer.json` (`ComposerExtraTyhpPackageManifest` or fold into `ComposerJsonService`). Constants named after the deleted file must go.

`TyhpdefOverlayManifestEditor`: if `manifestPath` is `composer.json`, mutate `extra.tyhp.package.overlay` (create `extra` / `tyhp` / `package` / `overlay` as needed). If it is `tyhp.json`, keep editing top-level `overlay`.

---

### A. Library emit / overlay edit

`TyhpCodeTyhpdefGenerator.WriteManifest` writes **`composer.json`** at `output.publishPath`, not a second JSON file.

Merge algorithm (same as today’s sidecar merge, different document):

- Parse existing publish-path `composer.json` (or start `{}`).
- Ensure `extra` object, `extra.tyhp` object, `extra.tyhp.package` object.
- Additive-merge generated `package` keys into `extra.tyhp.package` only.
- Write the full `composer.json` back. Prefer existing indent / `ComposerJsonService` JSON options so `require` / plugin keys are not casually reformatted beyond what that service already does.
- Parse failure of existing `composer.json`: **do not overwrite**; diagnostic (reuse build write / JSON parse family; do not add a code whose message names a deleted file).

Overlay action: resolve package manifests from `tyhpdefInclude` entries ending in `composer.json` (with sentinel), not a sidecar suffix.

---

### A. Runtime packages and scripts

**Order:** compiler must **read** `extra.tyhp.package` before tests that include `../composer.json` will pass. **Migrate JSON before** the first new library emit that would otherwise write a **default** `include: ["./package.tyhpdef"]` over a wrapper that needs `_tyhpdef/*.tyhpdef` + overlays. Practical sequence: implement read+write in the compiler, migrate wrapper `composer.json` files in the same change, then rebuild compiled `tyhp/*` packages.

**Per package under `runtime/packages/*/`:**

1. If a sidecar exists, copy its object to `extra.tyhp.package` on that package’s `composer.json` (do not drop `interopContractVersion` / `require` / `extensions`).
2. Delete the sidecar.
3. Point `tests/tyhp.json` / `tests/tyhp-php8.*.json` includes at `../composer.json` and `../../php/composer.json` (and parent wrappers such as `php-ext-pdo` the same way).
4. **Do not** edit `"version"`.

**Compiled packages** (`core`, `decimal`, `async`, `lambda`, and any other `tyhp_src` package): after migration, run the existing in-tree package build (`runtime/packages/base-build-all.sh` / per-package `tyhp build`) so additive merge and dist copy are proven. Empty `src/` after a failed `--clean` is acceptable; do **not** git-restore generated PHP.

**tyhpdef-only packages** (`php`, `php-ext-*`, `psr-*`, `monolog-*`, …): JSON migrate + test `tyhp.json` retarget is enough; they are not Track C libraries.

**Scripts to update (non-exhaustive — grep at implementation):**

| Location | Change |
|----------|--------|
| `runtime/packages/build-common.sh` `copy_dist_tyhpdefs` | Stop copying a sidecar. Dist already copies `composer.json`; keep copying `package.tyhpdef`. |
| `runtime/packages/new-php-ext.sh` | Write `extra.tyhp.package` on the new `composer.json`; do not write a sidecar; tests include `../composer.json`. |
| `runtime/packages/new-composer-lib.sh` | Same. |
| `scripts/publish-runtime-packages.sh` | Copy `composer.json` only for that manifest; do not copy a sidecar. **Do not run publish** as part of this workstream. |
| Any other `runtime/packages/*.sh` / `scripts/*` hit by grep | Same rule. |

---

### A. Greenfield hygiene

After Workstream A, **shipped surfaces must not mention `package.tyhp.json` at all** — not as history, not as “we used to,” not in comments, tests, help text, or other story docs.

**This 21.11 file may name `package.tyhp.json`** so implementers can grep the pre-change tree. Do not copy that wording into `docs/`, `AIDevGuide/`, code, or scripts.

Sweep (`rg -n 'package\.tyhp\.json'`; treat a hit as a fail except this file):

- `Tyhp/`, `tests/`, `Resources/*.resx`
- `docs/content/`, `AIDevGuide/`, `CONVENTIONS.md`, `VERSIONING.md`, `README.md`, `CONTRIBUTING.md`
- `runtime/packages/` (including `*.md`, `TYHPDEF_OVERLAY_STANDARDS.md`, `ASYNC_TYPES.md`)
- `scripts/`
- Other `dev-docs/IMPLEMENTATION_PLAN_TODO_STORY_*.md` (especially 20, 21, 21.5, 21.8, 21.10, 25) and `dev-docs/ROADMAP.md` — retarget to `extra.tyhp.package` / `composer.json` in **present tense**
- Language-server / IDE QA notes under `tyhp-lang/`

User-facing prose states the supported contract only (`extra.tyhp.package` on `composer.json`). Per `.cursor/rules/reader-relevant-documentation.mdc`.

`CONVENTIONS.md` §10 documentation phase: name the pages (at least `docs/content/project_intro.md`, `project_optionsList.md`, `tyhpdef_about.md`, `tyhpdef_overlays.md`, `cli_build.md`, `cli_tyhpdefGeneration.md`, `faq_*` hits, overlay/build help resx, `AIDevGuide/guide/23-tyhpdef.md`, handbook project-setup / build-cli, `AIDevGuide/REGEN.md`).

---

### A. Story 25 pointer

Story 25 still owns internals overlay behavior. When it is implemented, `"internalOverlay"` is an **`extra.tyhp.package`** array (not `"overlay"`, not a sidecar). Package Composer `name` for `includeInternals` matching is `composer.json` `"name"` (Story 25’s planned extra stamp on a sidecar is unnecessary once the manifest **is** `composer.json`). Update the 25 plan in this workstream’s hygiene pass; do not build 25.

---

### A. Phases

Inner phases are **A.1 … A.6**. Do not renumber them if Workstream B is added.

#### A.1 — Schema, read path, write path

- [ ] `CONVENTIONS.md`: `extra.tyhp.package`
- [ ] Parse `extra.tyhp.package` from `composer.json`; glob include/overlay/exclude; `source.tagless`
- [ ] Vendor scan + explicit `composer.json` includes + `include` promotion
- [ ] `--vendor` skip-if-already-a-tyhpdef-package uses the sentinel
- [ ] Library additive merge into publish-path `composer.json`; applications do not write `package`
- [ ] Overlay editor / overlay action target `extra.tyhp.package.overlay`
- [ ] Rename/remove sidecar-named types and constants
- [ ] Plugin still ignores `package` (test)

#### A.2 — Compiler tests and conformance

- [ ] Replace fixture sidecars with `composer.json` + `extra.tyhp.package`
- [ ] `tyhpdefInclude` / `include` strings in tests and `tests/conformance/**/tyhp.json` point at `composer.json`
- [ ] Root `tyhp.json` `tyhpdefInclude` lists runtime `composer.json` paths
- [ ] Overlay tests read/write `extra.tyhp.package`
- [ ] Library generate tests assert `extra.tyhp.package` on `composer.json` and assert the sidecar file is **absent**

#### A.3 — Scripts

- [ ] `runtime/packages/build-common.sh`
- [ ] `runtime/packages/new-php-ext.sh`
- [ ] `runtime/packages/new-composer-lib.sh`
- [ ] `scripts/publish-runtime-packages.sh` (edit copy rules only; do not publish)
- [ ] Any remaining script grep hits

#### A.4 — Migrate and rebuild `runtime/packages/*`

- [x] Every package: sidecar object → `extra.tyhp.package`; delete sidecar; retarget tests `tyhp.json` includes
- [x] No `"version"` field changes
- [x] Rebuild compiled packages with the existing local build scripts (no tag, no push, no Packagist)
- [x] Dist copy uses `composer.json` + `package.tyhpdef` only

#### A.5 — Documentation and AIDevGuide

- [x] Named `docs/content/` pages (see hygiene)
- [x] `AIDevGuide/` + `REGEN.md`
- [x] Help/resx strings
- [x] Technical guides (`Tyhp/Domain/Services/technical-guide.md`, Binder `TYHPDEF_DISTRIBUTION.md`, `BuiltIn/README.md`, package readmes)
- [x] Present-tense retarget of other `dev-docs` stories + `ROADMAP.md` 21.11 row

#### A.6 — Filename purge gate

- [x] Repo grep for the old sidecar filename is empty except this 21.11 plan (and nothing in `docs/`, `AIDevGuide/`, `Tyhp/`, `tests/`, `runtime/`, `scripts/`, `Resources/`, other story docs)
- [x] No comment or diagnostic that describes a migration or a former filename

---

### A. Golden fixtures / tests

- [ ] Fixture library: `composer.json` with `extra.tyhp.package.include` → symbols bind; no sidecar on disk
- [ ] `extra.tyhp` without `package` → vendor/explicit load does **not** pull `./package.tyhpdef` even if that file exists
- [ ] `extra.tyhp.package` non-object → not a tyhpdef package
- [ ] Vendor `vendor/acme/lib/composer.json` with sentinel loads; neighbor package without `package` does not
- [ ] Explicit `tyhpdefInclude`: `./pkg/composer.json`
- [ ] Library build: additive merge preserves a hand `overlay` entry; adds `./package.tyhpdef` if missing; does not strip `extra.tyhp.require`
- [ ] Application `generateTyhpdef`: `package.tyhpdef` written; `extra.tyhp.package` not added
- [ ] Overlay CLI appends `./_tyhpdef/overlays/*.tyhpdef` to `extra.tyhp.package.overlay` on a wrapper `composer.json`
- [ ] Plugin unit: `extra.tyhp.package` does not become extra-require names
- [ ] `new-php-ext.sh` / `new-composer-lib.sh` dry-run or generated tree: `extra.tyhp.package` present, sidecar absent, tests include `../composer.json`
- [x] One php-ext test `tyhp.json` and one compiled package `composer.json` after A.4 match the target examples

---

## Workstream B — UnitEnum / BackedEnum auto-implement

> **Status:** **Design locked.**
> **Depends on:** 21.6 Traversable listing (`DeclarationRule` / TYHP4326); 21.7 Stringable `ImplementsOrExtends` auto-implement. Canonical lock: `PHP_LANGUAGE_HOOKS.md` [UnitEnum / BackedEnum](PHP_LANGUAGE_HOOKS.md#unitenum--backedenum).

PHP applies `\UnitEnum` to every enumeration and `\BackedEnum` (extends `\UnitEnum`) to backed enumerations. Userland types cannot implement or extend either. Enumerations cannot override `cases` / `from` / `tryFrom`. Tyhp today types cases as the enum, but **does not** auto-implement those interfaces, so a user `.tyhp` enum is not assignable to `\UnitEnum`, does not inherit overlay `cases` / `from` / `tryFrom`, and can illegally `implements \UnitEnum`.

This is the same engine-interface pattern as Traversable (listing reject) and Stringable (auto-implement without a written `implements`).

### B. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Auto-implement | Every user `.tyhp` **enum** is `\UnitEnum` without a written `implements`. A **backed** enum is also `\BackedEnum`. Assignability, `instanceof`, and method lookup follow from that. |
| Method lookup | `cases()` from `\UnitEnum`. Backed `from` / `tryFrom` from `\BackedEnum` (Layer 3 overlay already returns `static` / `?static` when the interface is in the hierarchy). |
| Listing reject | User `.tyhp` classes, interfaces, and traits must not `implements` / `extends` `\UnitEnum` or `\BackedEnum`. Only the engine `\BackedEnum` tyhpdef may extend `\UnitEnum`. |
| Enum listing | User `.tyhp` enums must not list `\UnitEnum` / `\BackedEnum` in `implements` (already-implied / duplicate, same as Traversable). |
| Override reject | User `.tyhp` enums must not redeclare `cases`, `from`, or `tryFrom`. |
| Harvest exemption | `.tyhpdef` shells may keep `implements \UnitEnum` / `\BackedEnum`. Do **not** diagnose those (same as Traversable harvest). |
| Emit | Native PHP enums. Do **not** emit a written `implements \UnitEnum` / `\BackedEnum`. |
| Exhaustiveness | **Out.** `match` exhaustiveness on enums is not this workstream (see [Hooks remainder](#hooks-remainder--not-in-2111)). |

Copy `CheckTraversableListing` / `IsEngineStringableInterface` rather than inventing a third hierarchy walker.

### B. Phases

#### B.1 — Auto-implement

- [ ] `ImplementsOrExtends`: enum kind → `\UnitEnum`; backed enum kind → also `\BackedEnum`
- [ ] `instanceof \UnitEnum` / `\BackedEnum`; assignability to those types; `cases()` / `from` / `tryFrom` resolve on user enums without a written `implements`

#### B.2 — Listing and override reject

- [ ] `DeclarationRule` (or sibling of Traversable): user class/interface/trait listing → error; user enum listing → error; `.tyhpdef` harvest skipped
- [ ] Redeclaring `cases` / `from` / `tryFrom` on a user enum → error
- [ ] Allocate codes in `MessageCode.cs` (do not reuse 4326’s Traversable message)

#### B.3 — Documentation and AIDevGuide

- [ ] Enum / tyhpdef pages: enums are UnitEnum / BackedEnum without writing it; do not list or override engine methods
- [ ] `PHP_LANGUAGE_HOOKS.md` UnitEnum row → **Done** when tests land
- [ ] `AIDevGuide/REGEN.md` if the guide claims a written `implements` is required

### B. Golden fixtures / tests

- [ ] `enum Suit { case Hearts; }` is assignable to `\UnitEnum`; not to `\BackedEnum`; `Suit::cases()` types
- [ ] `enum Size: int { case S = 1; }` is `\UnitEnum` and `\BackedEnum`; `Size::from(1)` is `Size`
- [ ] `class Foo implements \UnitEnum` errors; `interface I extends \BackedEnum` errors
- [ ] `enum E implements \UnitEnum { case A; }` errors
- [ ] `enum E: int { public static function from(int $v): self { … } }` errors
- [ ] Harvested Core enum tyhpdef that lists `\UnitEnum` does not error
- [ ] Emitted PHP for a user enum has no `implements \UnitEnum`

---

## Workstream C — PHP engine attributes

> **Status:** **Design locked.**
> **Depends on:** 08 `AttributeRule` / `DeprecationRule` / `CheckerHelpers.ReportNoDiscardIfDiscarded`; 20.5 version gating. Canonical rows: `PHP_LANGUAGE_HOOKS.md` §5.

Close the remaining engine-attribute hooks. Tyhp already has the Core stubs and partial checker wiring. This workstream does not add new syntax.

### C. Decisions (locked)

| Topic | Decision |
|-------|----------|
| `#[\Deprecated]` | Use-site **warning** (compile-time analogue of `E_USER_DEPRECATED`). Binder sets `IsDeprecated` when this attribute is on the declaration (in addition to the tyhpdef `deprecated` keyword). Reuse **TYHP4500**; do not allocate a second use-code. |
| Deprecated message | If `$message` is a string literal, include it in the diagnostic (`{0}` name, `{1}` message). Omit `{1}` / keep current one-arg text when `$message` is absent. `$since` is documentation-only (explain text / help), not required in the short message. |
| Deprecated version | Warn even when `output.phpVersion` is `< 8.4` (same policy as NoDiscard: Tyhp still checks; PHP runtime is a no-op on older minors). The class stays gated in tyhpdef. |
| `#[\Override]` methods | Keep TYHP4129 when the method does not override a non-private ancestor or interface method. Trait bodies stay skipped. |
| `#[\Override]` properties | Legal when `output.phpVersion` is **≥ 8.5**. Same-name property must exist on a parent class or implemented interface; else TYHP4129 (or the same family). `output.phpVersion` **< 8.5** → target mismatch (today’s method-only behavior). |
| `#[\Override]` on `__construct` | **Always** an error (PHP: constructors are exempt from override semantics; the attribute is invalid there). Not 4129-for-missing-parent. |
| `#[\NoDiscard]` callee | Warn only from the **invoked** declaration. Interface and abstract method declarations do **not** warn (PHP would not). Overrides do **not** inherit the warning unless they are themselves marked. Trait-imported methods keep the attribute if bind copied it onto the using class (PHP copies it). |
| `#[\NoDiscard]` `$message` | When `$message` is a string literal, include it in TYHP4165 (add a format argument; keep the current sentence when omitted). |
| `#[\NoDiscard]` version | Keep current: not gated on `output.phpVersion`. `(void)` still suppresses. |
| `#[\DelayedTargetValidation]` | When this attribute is on a declaration **and** `output.phpVersion` is **≥ 8.5**, skip **target** (`TARGET_*`) compile-time errors for **internal** (Core) attributes on that same declaration. Do **not** skip functional checks (`#[\Override]` still errors if it does not override; `#[\NoDiscard]` unused-return still warns). `< 8.5`: ignore this attribute for the skip (targets still checked). |

### C. Phases

#### C.1 — `#[\Deprecated]`

- [ ] Binder: `#[\Deprecated]` → `IsDeprecated` on the symbol
- [ ] `DeprecationRule` already fires TYHP4500; extend message when `$message` is a literal
- [ ] Tests: function/method/class/const; tyhpdef `deprecated` still works; both together still one warning

#### C.2 — `#[\Override]` properties + `__construct`

- [ ] Remove hardcoded method-only target reject when phpVersion ≥ 8.5 and target is a property
- [ ] Property override walk (parent + interfaces), parallel to `OverridesInheritedMethod`
- [ ] `__construct` + `#[\Override]` → dedicated error (any phpVersion)
- [ ] phpVersion `< 8.5`: property still target-mismatch

#### C.3 — `#[\NoDiscard]` PHP callee rules

- [ ] No warn when the bound callee’s declaring object is an interface or the method is abstract
- [ ] Literal `$message` in TYHP4165
- [ ] Override without its own attribute does not warn; implementor with the attribute does
- [ ] `(void)` / used return still suppress

#### C.4 — `#[\DelayedTargetValidation]`

- [ ] phpVersion ≥ 8.5: skip `ValidateAttributeTarget` TARGET mismatches for Core attributes on a declaration that has this attribute
- [ ] `#[\Override]` on a non-overriding method still 4129
- [ ] phpVersion `< 8.5`: TARGET mismatches still reported

#### C.5 — Documentation and AIDevGuide

- [x] Attribute / PHP version pages; `--explain` for changed codes
- [x] `PHP_LANGUAGE_HOOKS.md` §5 rows → **Done** / remaining Partial closed
- [x] `AIDevGuide/REGEN.md`

### C. Golden fixtures / tests

- [ ] `#[\Deprecated('use bar')] function foo()` → call warns TYHP4500 including `use bar`
- [ ] tyhpdef `deprecated function baz` still TYHP4500
- [ ] `#[\Override] public int $x` errors on `< 8.5`; on 8.5 errors only when no parent `$x`; succeeds when parent has `$x`
- [ ] `#[\Override] function __construct()` errors even when a parent constructor exists
- [ ] `$iface->marked()` with `interface I { #[\NoDiscard] function marked(): int; }` does **not** TYHP4165; `$impl->marked()` does when `Impl` repeats the attribute
- [ ] `#[\DelayedTargetValidation] #[\Override] public int $x;` on 8.5 with a parent `$x` is not a target mismatch; without a parent `$x` still 4129

---

## Workstream D — `\stdClass` contract

> **Status:** **Design locked** (follows `PHP_LANGUAGE_HOOKS.md` [stdClass](PHP_LANGUAGE_HOOKS.md#stdclass) Tyhp lock).
> **Depends on:** 08 `RestrictedFeatureRule` TYHP4134; Layer 3 Core `omit` of `\AllowDynamicProperties`.

Today undeclared writes pass when some ancestor AST still has `#[\AllowDynamicProperties]` (Layer 1 leftover on `\stdClass`, so `extends \stdClass` also passes). Tyhp’s lock is: **only** `\stdClass` allows undeclared properties. Subclasses and harvest bags (`__PHP_Incomplete_Class`, …) must not.

### D. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Named gate | Undeclared **writes** are legal only when the receiver’s type is **exactly** `\stdClass` (global engine class). Not a parent-walk of `#[\AllowDynamicProperties]`. |
| Subclasses | `class Foo extends \stdClass` does **not** get undeclared writes (stricter than PHP). TYHP4134. |
| `#[\AllowDynamicProperties]` | Stays **omitted** in Layer 3. User stamps → not an attribute class (4126). The named gate must not revive the attribute as an opt-in. |
| Other harvest stamps | `__PHP_Incomplete_Class` and any other Layer 1 `#[\AllowDynamicProperties]` do **not** allow undeclared writes. Overlay-omit those stamps if they still leak through the old walk; the named gate is the source of truth. |
| `(object)` cast | Infers `\stdClass`, not built-in `object`. |
| Undeclared **reads** | Unchanged: no property and no `__get` → Unresolved. No property map in this workstream. |
| `__get` / `__set` | Unchanged: still the typed bag path for every non-stdClass type. |

If subclasses should match PHP instead, that is a lock change — do not implement “allow on descendants” without updating this table and the hooks file.

### D. Phases

#### D.1 — Named write gate

- [ ] `RestrictedFeatureRule` / `AllowsDynamicProperties`: exact `\stdClass` only
- [ ] Tests: `new \stdClass()->nope = 1` OK; user class without `__set` still 4134; `class Foo extends \stdClass { }` write 4134
- [ ] Confirm omitted `#[\AllowDynamicProperties]` on a user class cannot opt in

#### D.2 — `(object)` cast

- [ ] `InferCastType` / `T_OBJECT_CAST` → `\stdClass` (resolved engine class, not `BuiltInTypeSymbol("object")`)
- [ ] Tests: `(object)['a' => 1]` assignable to `\stdClass`; not a license for undeclared writes on other types

#### D.3 — Documentation and AIDevGuide

- [ ] Dynamic properties / stdClass: only `\stdClass`; subclasses declare properties or use `__get`/`__set`
- [ ] `(object)` produces `\stdClass`
- [ ] `PHP_LANGUAGE_HOOKS.md` stdClass row → **Done** when tests land (reads may stay Partial)

### D. Golden fixtures / tests

- [ ] `$o = new \stdClass(); $o->x = 1;` no 4134; `$o->x` still Unresolved without a declared property / `__get` (document if that stays Partial)
- [ ] `class Bag extends \stdClass {}` + `$b->x = 1` → 4134
- [ ] `#[\AllowDynamicProperties] class Opt {}` still not an opt-in
- [ ] `(object)[]` has type `\stdClass`

---

## Workstream E — XDebug `#[\SensitiveParameter]` redaction

> **Status:** **Design locked.**
> **Depends on:** shipped Story 18 proxy (`Tyhp/XDebugProxy/Translation/VariableTranslator.cs`, `StackFrameTranslator.cs`, `DbgpMessageTranslator.cs`). Canonical row: `PHP_LANGUAGE_HOOKS.md` §5 `\SensitiveParameter`.

This is a **security** hole in the shipped debugger path. PHP wraps `#[\SensitiveParameter]` arguments in `\SensitiveParameterValue` in stack traces and hides `$value` from `__debugInfo`. The proxy today remaps paths only. `VariableTranslator.SurfaceDecimalDisplay` **unwraps** `Tyhp\Decimal` by copying inner `$value` onto the property — the same walk would leak a password if applied to `\SensitiveParameterValue` (or if a future display helper does). Story 18 is **done and will not be revisited**. Do not park this on Story 23.

### E. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Fail closed | If a property’s `classname` is `\SensitiveParameterValue` (leading `\` optional), **never** copy inner `$value` / `getValue` onto the parent as Decimal does. Leave the wrapper object. Prefer hiding children over showing the secret. |
| Do not unwrap | Unlike Decimal display. A dedicated `IsSensitiveParameterValueClass` check (parallel to `IsDecimalClass`) must run **before** any display flattening. |
| PHP already wrapped | `context_get` / `property_get` / `property_value` / eval results that already have `classname="SensitiveParameterValue"` pass through as that object. Do not strip `classname`, do not scalarize. |
| Proxy-synthesized traces | If the proxy builds or rewrites a frame argument list (including any later inlined-frame reconstruction), wrap marked parameters as `\SensitiveParameterValue` the same way PHP does. Never emit the raw argument in DBGp XML. |
| Shared helper | Put wrap / “must not unwrap” in one place (`SensitiveParameterRedaction` or similar) used by `VariableTranslator` and any stack/eval path. Story **23** must call this helper if it reconstructs frames — retarget `IMPLEMENTATION_PLAN_TODO_STORY_23.md`’s XDebug note from Story 18 to this helper. Do **not** implement 23 here. |
| Identifying marked params | Prefer PHP’s wrapper when present. If reconstructing args without a wrapper, use compile/sourcemap metadata for `#[\SensitiveParameter]` on that parameter; if metadata is missing, **do not invent** a leak — omit or wrap as `SensitiveParameterValue` with an opaque payload rather than forwarding the raw value. |
| `stack_get` | DBGp frames usually have no argument values. Still run redaction on any argument payload that does appear. |
| Eval | `EvalTranslator` / property trees from `debug_backtrace` / exception traces are a leak surface. Same helper. |
| Tests | Extend `Phase6TranslationTests` (Decimal unwrap fixtures) with the negative: SensitiveParameterValue is **not** unwrapped. |

### E. Story 23 pointer

`IMPLEMENTATION_PLAN_TODO_STORY_23.md` currently says the XDebug proxy should reconstruct inlined Tyhp frames. That reconstruction must use Workstream E’s helper. Update that sentence in this workstream’s hygiene pass (present tense, no “Story 18 will redact”). Do not build inlining here.

### E. Phases

#### E.1 — Do not unwrap `\SensitiveParameterValue`

- [ ] `VariableTranslator`: detect wrapper classname; skip Decimal-style flatten; do not surface inner `$value`
- [ ] Recurse: nested properties that are wrappers get the same treatment
- [ ] `Phase6TranslationTests`: Decimal still unwraps; SensitiveParameterValue does not (with and without leading `\`)

#### E.2 — Wrap / preserve on traces and eval

- [ ] Any proxy-built argument list uses the helper
- [ ] Eval / backtrace property trees: wrappers preserved
- [ ] Missing metadata → opaque wrap or omit, never raw secret

#### E.3 — Documentation and plan retarget

- [ ] Debug / XDebug docs: marked parameters stay wrapped in the IDE
- [ ] `PHP_LANGUAGE_HOOKS.md` SensitiveParameter proxy row → **Done** when tests land
- [ ] Retarget Story 23 XDebug note to this helper
- [ ] `AIDevGuide/REGEN.md` only if the guide mentions debugger display of parameters

### E. Golden fixtures / tests

- [ ] DBGp `<property classname="SensitiveParameterValue">` with child `$value` = `secret` still has `classname` SensitiveParameterValue after translation; CDATA/text of the parent is not `secret`
- [ ] `classname="\SensitiveParameterValue"` same
- [ ] `classname="Tyhp\Decimal"` still surfaces `$value` (no regression)
- [ ] Nested: `$trace[0]['args'][0]` wrapper not flattened
- [ ] Helper unit: wrap a marked arg; unwrap is a no-op / refused

---

## Workstream F — Duplicate diagnostics name the original location

> **Status:** **Design locked.**
> **Depends on:** Story 14 diagnostic labels (`Diagnostic.Labels`, `DiagnosticExtensions.LabelFromAst`, `CLI_DiagnosticLabelDeclaredHere`, `RichDiagnosticRenderer`). Pattern already used by extern diagnostics (`TyhpBinder.Extern`, `ExternTypeUse`).

Duplicate diagnostics today point only at the **second** declaration. The first/original location is in the symbol table (`DeclaringAstNode` / `SourceFile`) but is not attached. Authors cannot compare the two sites without grepping. Story 14 already specified secondary spans (“defined here”); this workstream wires them onto every duplicate-family diagnostic and surfaces them in CLI, JSON, SARIF, and LSP.

Do **not** put `file:line` into the short `.resx` message (`CONVENTIONS.md` §2: labels, not stuffed short text). Keep existing `MessageCode` numbers.

### F. Decisions (locked)

| Topic | Decision |
|-------|----------|
| Primary span | Stays on the **duplicate** (the construct the compiler just rejected). |
| Secondary span | One label on the **first** declaration / previous occurrence, message `CLI_DiagnosticLabelDeclaredHere` (`declared here`). Same string as extern diagnostics. |
| Codes in scope | Every diagnostic whose meaning is “this name/item already exists”: `BinderDuplicateSymbolDeclaration` (3002), `BinderDuplicateUseAlias` (3008), `BinderDuplicateGenericParameter` (3011), `CheckerDuplicateParameter` (4075), `CheckerDuplicateNamedArgument` (4079), `CheckerDuplicateArrayKey` (4092), `CheckerEnumCaseDuplicateValue` (4112), `CheckerDuplicateCatch` (4124), `CheckerDuplicateImport` (4131), `CheckerDuplicateTypeInComposite` (4053), `CheckerPhpVersionDuplicateDeclaration` (4303), `TyhpdefDuplicateDeclaration` (8002), `TyhpdefDuplicateFqnAcrossPackages` (8025). Grep `Duplicate` in `MessageCode.cs` at implementation and include any sibling added since this lock. |
| Out | Unrelated “conflict” codes that are not a second declaration of the same name (e.g. trait `insteadof` missing) unless the existing symbol location is already in hand and the message is clearly “already declared.” Prefer including `BinderTraitConflict` when both members have spans. |
| Helper | One bag helper (e.g. `AddDuplicateFromAst(code, duplicateNode, duplicateFile, existingSymbolOrAst, existingFile, …formatParams)`) that builds the error and `WithLabels(LabelFromAst(…, "declared here"))`. Call sites must not copy-paste `WithLabels`. |
| Obtaining the original | Callers that only see `AddChildSymbol` returning `false` must recover the existing symbol (scope lookup by name, `out` existing, or report from `OnDuplicateChildSymbol`). Do not emit 3002 without trying. |
| Unknown original | If there is no `DeclaringAstNode` / span (synthetic, missing file), still emit the duplicate diagnostic **without** a label. Never drop the error. |
| Cross-file | Label `Span.FileName` is the original’s `SourceFile` (may differ from the duplicate file). CLI rich renderer already draws other-file labels. |
| Short message | Unchanged (`Duplicate declaration of symbol `{0}`` etc.). 8025 keeps package names in the message **and** gets location labels. |
| CLI | `ConsoleDiagnosticFormatter` / `RichDiagnosticRenderer` already print `Labels`. Prove with a fixture that the snippet includes `declared here` on the first site. |
| JSON / SARIF | Already emit `labels` / related locations. Tests must assert the original file/line is present. |
| LSP | `DiagnosticsPublisher.ToLspDiagnostic` must map `Labels` to LSP `relatedInformation` (not implemented today). Click-through in the IDE is part of this workstream. |
| Overlay last-wins | Overlay replace is **not** a duplicate error. Do not attach “declared here” to successful overlay replace. Include-layer `TYHP8002` is in scope. |

### F. Phases

#### F.1 — Helper + binder symbol duplicates

- [x] Shared `AddDuplicate*` helper on `DiagnosticBag` / `DiagnosticExtensions`
- [x] All `BinderDuplicateSymbolDeclaration` / use-alias / generic-parameter sites pass the existing declaration
- [x] Cross-file FileScope / NamespaceBlockScope duplicates label the other file

#### F.2 — Checker and tyhpdef duplicates

- [x] Remaining codes in the table (parameters, named args, array keys, enum values, catch, import, composite types, PHP-version overlap, 8002, 8025)
- [x] `TyhpdefSymbolRegistrar.TryReportCrossPackageConflict` labels the first package’s declaration span

#### F.3 — Surfaces

- [x] CLI rich output shows `declared here` at the original
- [x] `--format json` / SARIF include the original span
- [x] LSP `relatedInformation` from `Labels`
- [x] `docs/content/diagnostics_reference.md` examples for 3002 (and 8002) show both locations — generator/docs phase per `CONVENTIONS.md` §10

### F. Golden fixtures / tests

- [x] Two `class User` in one file: primary span on the second; label on the first (`declared here`)
- [x] Same global function in two files: label file/line is the first file
- [x] Duplicate function parameter: label on the first `$x`
- [x] Duplicate `use` alias: label on the first `use`
- [x] `TYHP8002` include-layer tyhpdef: label on the first tyhpdef declaration
- [x] `TYHP8025`: labels (or primary + label) identify both package declaration sites
- [x] JSON diagnostic `labels[0]` has the original file/line
- [x] LSP relatedInformation non-empty for 3002
- [x] Overlay last-wins replace still has **no** 8002

---

## Hooks remainder — not in 21.11

These rows in `PHP_LANGUAGE_HOOKS.md` stay **Open** there (not scheduled in 21.11). Do not implement them under A–F.

| Item | Why it is out | Suggested home |
|------|----------------|----------------|
| `match` exhaustiveness on enums (and other closed sets) | Product feature, not engine auto-implement. Story 08 already lists match exhaustiveness; `DESIGN_OPEN_QUESTIONS.md` ties the payoff to later match work. | Story **08** leftover, or a later dedicated story — **not** B |
| `__get` / `__set` / `__isset` / `__unset` full property map | Open-ended typed bags. Tyhp’s typed replacement is property hooks (20.7). Arity already exists. | Skip for now; revisit only if hooks are not enough |
| `__call` / `__callStatic` typed missing methods | Same: arity exists; a full map is a second type system | Skip / later |
| `__clone`, `__sleep` / `__wakeup` / `__serialize` / `__unserialize`, `__set_state`, `__debugInfo` beyond arity | Low user value; serialize round-trip typing is a project | Skip / later |
| Bind `count()` to `\Countable` | Hooks file: leave unless we later bind it | Skip |
| `\JsonSerializable`, `\Serializable`, session handler interfaces | Stubs only; no new syntax | Skip |
| SplPriorityQueue / DateTime operators / Closure alias bounds / Tuple index / WeakMap `$map[] =` / const-int overloads | Already assigned; overlays exist in tree | Story **21.8** (tick the hooks checklist there, do not duplicate here) |
| `iterable` / `\Iterator` / `\IteratorAggregate` / `\WeakReference` “Partial” leftover | Feeds from 21.6 / 21.8 generics; not a missing syntax family | No new story unless a failing fixture appears |
| Operator overloads / `*Convertible` / implicit convert | Already assigned | Story **09** / Story **31** Idea 2 |
| `(decimal)` cast | Checker-gaps backlog, not a PHP engine hook | `CHECKER_GAPS.md` / later |

---

## Cross-Story References

- Story **08** — checker; match exhaustiveness stays there (not Workstream B).
- Story **09** / **31** — operator convert-to; not this story.
- Story **10** — `tyhp.json` remains the author project file (`--tyhp-project`, nested LS ownership).
- Story **14** — diagnostic labels / rich renderer; Workstream **F** attaches them to duplicate-family codes and maps them through LSP.
- Story **18** — proxy is shipped; Workstream **E** adds SensitiveParameter redaction there (18 is not reopened as a story).
- Story **19** — language server; F extends `DiagnosticsPublisher.ToLspDiagnostic` with `relatedInformation` (19 is not reopened as a story).
- Story **23** — inlined-frame reconstruction must call E’s helper; not implemented here.
- Story **20** — Track C still emits `package.tyhpdef`; Workstream A changes only where the **package load spec** lives.
- Story **21** / **21.8** — wrapper `include` / `overlay` globs move onto `extra.tyhp.package` (A). Layer 3 overlay completeness stays 21.8.
- Story **21.6** / **21.7** — Traversable listing + Stringable auto-implement are the pattern for Workstream B.
- Story **21.10** — `extra.tyhp.require` unchanged; `package` is a sibling key; plugin ignores it.
- Story **25** — `internalOverlay` will be an `extra.tyhp.package` key; not implemented here.
- Story **30** — user docs must already match each implemented workstream; no “later sweep.”
- `PHP_LANGUAGE_HOOKS.md` — inventory; B–E tick their rows to **Done**.
- `CONVENTIONS.md` §8 — add `extra.tyhp.package`; §2 / §10 — Workstream F uses labels, not short-message `file:line`.
- `ROADMAP.md` — add a 21.11 row after 21.10 (bucket; A–F).

When adding Workstream G+, add a bullet here rather than inventing a second references section.
