# Implementation Plan: Story 21.10 — `extra.tyhp.require`, Track C `extern`, and Composer plugin sync

> **Roadmap position:** Story 21.10 — **Tier 2 — DX & Ecosystem** (additive sub-story, inserted after Story **21.9**, before deferred Story **22**). **Last implemented item in Tier 2** (22 stays deferred). **Implement after 21.9.**
> **Direct dependencies (new numbering):** 04 (`tyhp/core` / `tyhp/decimal`), 13 / **21.5** (init, `tyhp/compiler`, Composer toolchain), 20 (Track C `package.tyhpdef`), **21.1** (tyhpdef `extern` occupancy, `@provided-by`, `TYHP4307`), 21 (`tyhpdef/php` + `tyhpdef/php-ext-*`)
> **New story:** compiled Tyhp libraries declare which tyhpdef packages are **ambient** for consumers (`extra.tyhp.require`) versus **author-only** (`require-dev` not listed there). Track C emits real FQNs for ambient names and name-only `extern` (including `extern function` / `extern const`) for the rest. A Composer plugin on `tyhp/core` walks the require tree **before** vendor populate and writes the full merged set onto the **root** `require-dev` in one Composer solve. `internal` is parsed and omitted from the public `package.tyhpdef`; Story 25 still owns checker enforcement and the internals overlay.
> **Conventions:** Diagnostic codes, config keys, and canonical paths are governed by `CONVENTIONS.md` (single source of truth for diagnostic codes = `Tyhp/Domain/Exceptions/MessageCode.cs`); cite it rather than restating ranges. See `ROADMAP.md` for the full tiered sequence.

> **Branch:** TBD
> **Generated:** 2026-09-10
> **Last design lock:** 2026-09-10 — `extra.tyhp.require` is any Composer name (not `tyhpdef/*` only); plugin on `tyhp/core` (not a metapackage, not folded into the compiler); full tree resolve of extras **and** runtime `require` edges before vendor populate; cycles skip that branch; `tyhp/compiler` is forced onto root `require-dev` by the plugin, not listed in extras on tyhp/* packages; Track C may emit `extern`; `extern function` / `extern const` are name-only; `internal` grammar + public Track C omit (not Story 25); plugin is hand-written PHP in `plugin/`; first `composer require tyhp/core` often misses that transaction (documented); CLI check is real, no sticky flag.
> **Status:** **Design locked.** Implement after 21.9. Do **not** implement Story 25 in this story.
> **Prerequisites:** Story 21.1 (`extern` types, real-wins, `TYHP4307`); Story 20 Track C; Story 21.5 (`tyhp/compiler`, init `composer.json`); `tyhpdef/php` + optional-extension packages (`php-ext-bcmath` / `gmp` / `decimal` / `mbstring`).
> **Consumers:** `tyhp/decimal` optional backends; any published Tyhp library with author-only tyhpdefs; app roots that `require` `tyhp/core`; Story 25 (`internal` already in the grammar); Story 30 (docs).

---

## Table of Contents

- [Summary](#summary)
- [Motivation](#motivation)
- [What this is not](#what-this-is-not)
- [What already exists](#what-already-exists)
- [Scope (In / Out)](#scope-in--out)
- [Decisions (locked)](#decisions-locked)
- [Library contract (`composer.json`)](#library-contract-composerjson)
- [Track C classification](#track-c-classification)
- [`extern function` and `extern const`](#extern-function-and-extern-const)
- [`internal` slice (not Story 25)](#internal-slice-not-story-25)
- [Composer plugin](#composer-plugin)
- [Tree resolve](#tree-resolve)
- [CLI check (`tyhp build` / `lint` / `composer sync`)](#cli-check-tyhp-build--lint--composer-sync)
- [First-party proving ground](#first-party-proving-ground)
- [Phases](#phases)
- [Diagnostic codes](#diagnostic-codes)
- [Out of scope](#out-of-scope)
- [Cross-Story References](#cross-story-references)
- [Golden Fixtures / Tests (Acceptance)](#golden-fixtures--tests-acceptance)

---

## Summary

A Tyhp library can **type-check** against tyhpdef packages that its **consumers must not install**. `tyhp/decimal` is the example: authors need `tyhpdef/php-ext-bcmath`, `tyhpdef/php-ext-gmp`, and `tyhpdef/php-ext-decimal` to compile optional backends. Apps that only call `Decimal` must keep compiling with `tyhpdef/php` (and core’s mbstring pin) and must **not** be forced to install those three extension wrappers.

Composer already has the right install semantics: a dependency’s `require-dev` is never installed for the consumer. This story does **not** invent a `require-tyhp` root key and does **not** put tyhpdefs in `require`.

The missing piece is a **declared ambient set** plus toolchain that (1) classifies Track C names into real FQNs vs `extern`, and (2) installs that ambient set on the **application** root in one Composer run.

```json
"require": { "php": ">=8.2", "tyhp/core": "@dev" },
"require-dev": {
  "tyhpdef/php": "@dev",
  "tyhpdef/php-ext-bcmath": "@dev",
  "tyhpdef/php-ext-gmp": "@dev",
  "tyhpdef/php-ext-decimal": "@dev"
},
"extra": {
  "tyhp": {
    "interopContractVersion": 1,
    "require": {
      "tyhpdef/php": "@dev"
    }
  }
}
```

- **`require-dev`:** everything the **author** needs to type-check this package (lock/install).
- **`extra.tyhp.require`:** the subset that is **ambient for consumers**. Track C spells those names as real FQNs. Duplicate the same pins in `require-dev` so authors install them.
- **`require-dev` minus extras:** Track C emits name-only `extern` with `// @provided-by: <owning package>`. Using those names from a consumer `.tyhp` is `TYHP4307` until that package is included.

A Composer plugin shipped with **`tyhp/core`** (`type: composer-plugin` + `extra.class`, not a `tyhp/tyhp` metapackage) walks root `require` / `require-dev` via Composer repositories **before** vendor populate, follows runtime `require` edges and `extra.tyhp.require` edges, merges constraints, and writes the full set onto root `require-dev` together with `tyhp/compiler`. Composer 2 only activates plugins whose package type is `composer-plugin` or `composer-installer`; `PluginInstaller` still installs the package like a library. Cycles skip that branch. `--no-dev` is a no-op and does **not** rewrite `composer.json`.

---

## Motivation

Story 21.1 locked “Track C never emits `extern` — Tyhp libraries `require` the types they name.” That is correct for **runtime** PHP packages. It is wrong for **tyhpdef** packages: putting `tyhpdef/php-ext-gmp` in `require` would Composer-require that wrapper for every decimal user, and putting it only in `require-dev` currently makes Track C copy `\GMP` as an unresolved / illegally-owned name (or fail regen). Naive “every `require-dev` is extern” would also extern `\JsonSerializable` from `tyhpdef/php`, and `class Decimal implements \JsonSerializable` would then hit `TYHP3026` / `TYHP3027`.

`extra.tyhp.require` is the split: ambient vs author-only. The plugin exists because Composer will not install a dependency’s `require-dev`, and extras are not a Composer install key. Without a pre-vendor tree walk, each extra level would need another `composer update`.

---

## What this is not

Do not re-open:

- A custom Composer root key (`require-tyhp`). Not in Composer’s schema, never installed, never locked.
- Putting tyhpdef packages in `require` so consumers get them “for free.” That changes production install and couples apps to author-only wrappers.
- Folding `tyhp/compiler` into `tyhp/core`. Runtime vs toolchain; `--no-dev` would ship the compiler; `tyhp/compiler` already `require`s `tyhp/core` (a reverse `require` is a cycle).
- A `tyhp/tyhp` metapackage that `require`s compiler + core. Plugin lives on **`tyhp/core`** because that is what apps actually install.
- Listing `tyhp/compiler` in `extra.tyhp.require` on first-party `tyhp/*` packages. The plugin still ensures it on the **root** `require-dev`.
- Restricting `extra.tyhp.require` to the `tyhpdef/*` vendor. Third-party Packagist tyhpdef packages are allowed (`tyhpdef/` is owned by Tyhp; other publishers cannot use it).
- Implementing Story 25 (`includeInternals`, internals overlay, checker 4054, LSP filtering).
- Signed `extern function \bcadd(string $a, …): string` (that remains the real overlay in the providing package).
- Compiling the Composer plugin from `tyhp_src` (would need `composer/composer` tyhpdefs and must not call `tyhp/core` at plugin load).
- Auto-`composer update` on every `tyhp build`.
- A sticky “synced” flag in `composer.json` / `tyhp.json` instead of a real check.
- Walking a dependency’s **`require-dev`** (not installed for consumers; those packages are that author’s extras/externs, not this app’s).

---

## What already exists

| Surface | Today |
|---------|--------|
| Composer `require` / `require-dev` | Unchanged semantics. Dependency `require-dev` is never installed for the consumer. |
| `extra.tyhp.interopContractVersion` | Already on runtime `composer.json` files. |
| Story 21.1 `extern class` / `interface` / `enum` / bare `extern \Name;` | Occupancy, real-wins, `@provided-by`, `TYHP4307` for **types**. Lexer already yields `T_TYHPDEF_EXTERN` when the next token starts with `\` or a letter. |
| Track C (`TyhpCodeTyhpdefGenerator`) | Emits this project’s `.tyhp` public API. **Never** emits `extern` (21.1). Skips `private`. |
| `tyhp/core` `composer.json` | `require`: `php`, `ext-mbstring`. `require-dev`: `tyhpdef/php`, `tyhpdef/php-ext-mbstring`. No plugin, no `extra.tyhp.require`. |
| `tyhp/decimal` `composer.json` | `require`: `tyhp/core`. `require-dev`: `tyhpdef/php` + bcmath/gmp/decimal wrappers. Optional backends need those wrappers to compile; consumers must not. |
| `tyhp/compiler` | `require`s `tyhp/core`. Its own `require-dev` (`tyhpdef/php`) is **not** installed for apps. |
| Story 21.5 | Init pins `tyhp/compiler` + `tyhpdef/php` on greenfield roots. **No plugin** (no CommandProvider). This story adds PluginInterface only. |
| Story 25 | Full `internal` design. **Not implemented.** This story takes grammar + binder flag + public Track C omit. |

---

## Scope (In / Out)

| In scope | Out of scope |
|----------|----------------|
| `extra.tyhp.require` map (package → constraint) on any Composer package | New Composer root key; putting tyhpdefs in `require` |
| Any Composer package name in extras (Packagist or `repositories`) | Restricting extras to `tyhpdef/*` |
| Track C: ambient FQN vs `extern` + `@provided-by` for require-dev-only owners | Track B catalog rules (unchanged) |
| Name-only `extern function` / `extern const`; extend `TYHP4307` | Signed extern function/const declarations |
| `internal` parse + `IsInternal` + omit from public `package.tyhpdef`; 4002 if combined with another visibility | Checker 4054, internals overlay, `includeInternals`, LSP |
| Hand-written plugin under `runtime/packages/core/plugin/`; `tyhp/core` `"type": "composer-plugin"`; copied into core dist artifacts | Compiling the plugin from Tyhp; CommandProvider |
| `PRE_DEPENDENCIES_SOLVING` full tree resolve; persist root `require-dev` + `setDevRequires` | N-level `composer update`; rewriting `composer.json` on `--no-dev` |
| Force `tyhp/compiler` onto **root** `require-dev` | Listing compiler in tyhp/* extras; core `require` of compiler |
| CLI stale check (local installed + root `composer.json` only); default explain; `--strict` / CI fail; `--fix` / `tyhp composer sync` | Packagist from the CLI check; auto-update on every build |
| Document first-`require` miss | Changing Composer’s plugin activation order |
| `tyhp/core` extras: `tyhpdef/php` + `tyhpdef/php-ext-mbstring`; decimal extras: `tyhpdef/php` only | Adding bcmath/gmp/decimal wrappers to decimal extras |
| Init: `allow-plugins` for `tyhp/core`; keep compiler + `tyhpdef/php` convenience pins | Replacing the plugin with init-only pins |
| Docs, `--explain`, AIDevGuide | Story 25 remainder |

---

## Decisions (locked)

| Topic | Decision |
|-------|----------|
| Ambient key | `extra.tyhp.require` — object of package name → constraint, same shape as Composer `require`. Lives next to `interopContractVersion`. |
| Who may appear | **Any** Composer package name. Third-party tyhpdef packages on Packagist are first-class. Not limited to `tyhpdef/*`. |
| `tyhp/compiler` | **Not** in extras on `tyhp/*` packages. Plugin (and CLI check) still require it on the **root** `require-dev`. |
| Duplicate pins | Every extra entry is also listed in that package’s `require-dev` so **authors** lock/install it. |
| Track C ambient | Names whose owning package is in **this** package’s `extra.tyhp.require` → real FQN in `package.tyhpdef`. |
| Track C author-only | Names whose owning package is in **this** package’s `require-dev` and **not** in extras → `extern` + `@provided-by: <that package>`. |
| Track C runtime require | Names owned by a package this library `require`s (e.g. `tyhp/core`) are not copied; consumers load that package’s tyhpdef. Do not extern them. |
| Track C vs 21.1 | 21.1 “never emit extern from Track C” is **reversed** for the author-only bucket. Track B classification is unchanged. |
| `@provided-by` | Owning **tyhpdef / Tyhp package name** (same 21.1 rule), not the PHP `ext-*` / Packagist PHP library. |
| `extern function` / `const` | Tyhpdef-only, **name-only**, semicolon. Occupancy + real-wins + `TYHP4307` from `.tyhp`, same as types. |
| Signed extern functions | **Out.** The providing overlay keeps the real signature. |
| `internal` in 21.10 | Parse on the declarations Story 25 lists. Binder `IsInternal`. Combine with `public`/`protected`/`private` → existing **4002**. PHP emit still public / unprefixed. **Public Track C omits** `internal` symbols (same as `private`). |
| Story 25 remainder | Checker project-boundary errors, internals overlay file, `includeInternals`, occupancy of overlay, LSP — **not** this story. |
| Plugin host | **`tyhp/core`**. `"type": "composer-plugin"` (Composer 2 only loads `composer-plugin` / `composer-installer`; `library` + `extra.class` never activates). `extra.class` + `composer-plugin-api`. Dist/copy of published `composer.json` uses the same type. |
| Plugin language | Hand-written PHP in `runtime/packages/core/plugin/`, PSR-4 that does **not** collide with `Tyhp\\` → `src/Tyhp/`. Copied into published core artifacts. Vanilla PHP only; no `tyhp/core` runtime helpers at Composer load. |
| Plugin event | `PluginEvents::PRE_DEPENDENCIES_SOLVING` (Composer 2). Collect extras, add to the request, fixpoint, **then** one solver run. |
| `--no-dev` | **No-op.** Do not write `composer.json`. Do not `setDevRequires`. |
| Tree walk | Recurse `extra.tyhp.require` **and** each package’s runtime `require`. Skip platform (`php`, `ext-*`, `composer-plugin-api`, `composer-runtime-api`). **Do not** walk dependency `require-dev`. |
| Cycles | If a package is already on the current path / in `seen`, **skip that branch**. Do not error. Still resolve the rest of the (non-cyclic) tree. |
| Metadata | Fetch from Packagist / configured `repositories` (Composer `RepositoryManager`). Do not require `vendor/` to already contain the package. |
| Extra version | Read `extra.tyhp.require` from a version matching the constraint being considered (highest matching if not yet pinned). Packages should keep extras stable across versions. |
| Constraint merge | Missing on root `require-dev` → add the extra’s constraint. Existing pin **compatible** with the extra → **keep** the existing pin. **Disjoint** → **error** (do not silent-widen, do not clobber). Already in root `require` → satisfied; do not also add `require-dev`. |
| Persist | Write root `composer.json` `require-dev` **and** `setDevRequires` (`Link::TYPE_DEV_REQUIRE`) so this solve sees them. |
| First require | `composer require tyhp/core` often **does not** run this plugin for that same transaction. **Expected.** Document. CLI check recovers (`tyhp build` explains; `tyhp composer sync` / a second Composer run installs). |
| CLI check | **Local only** (root `composer.json` + `vendor/composer/installed.json`). No Packagist. Stale if an installed package’s extras name something not in root require-dev / not installed; if `tyhp/*` is in require but missing; if `tyhp/compiler` is missing from root require-dev. |
| Default vs CI | Default `tyhp build` / `lint`: explain + tell the user how to sync; do **not** run Composer. `--strict` (and CI) **fail**. `--fix` / `tyhp composer sync` is the opt-in write + update. |
| Init | Still convenience-pins `tyhp/compiler` and `tyhpdef/php`. Also writes `allow-plugins.tyhp/core: true`. Plugin + CLI check remain the net for skipped init / stripped pins. |
| Core extras | `tyhpdef/php` and `tyhpdef/php-ext-mbstring` (core `require`s `ext-mbstring`). |
| Decimal extras | `tyhpdef/php` only (`\JsonSerializable` and other public PHP types). **Not** bcmath / gmp / php-decimal wrappers. |
| Hash / flag | **No.** Always a real check. |

---

## Library contract (`composer.json`)

### `tyhp/core`

```json
"type": "composer-plugin",
"require": {
  "php": ">=8.2",
  "ext-mbstring": "*",
  "composer-plugin-api": "^2.3"
},
"require-dev": {
  "tyhpdef/php": "@dev",
  "tyhpdef/php-ext-mbstring": "@dev"
},
"extra": {
  "tyhp": {
    "interopContractVersion": 1,
    "require": {
      "tyhpdef/php": "@dev",
      "tyhpdef/php-ext-mbstring": "@dev"
    }
  },
  "class": "Tyhp\\Composer\\Plugin"
}
```

Autoload the plugin namespace from `plugin/` (for example `Tyhp\\Composer\\` → `plugin/`). Keep `Tyhp\\` → `src/Tyhp/` for the runtime. Dist copy must include `plugin/`, `"type": "composer-plugin"`, and the extra.class stamp.

Core does **not** `require` `tyhp/compiler`.

### `tyhp/decimal`

```json
"require": { "php": ">=8.2", "tyhp/core": "@dev" },
"require-dev": {
  "tyhpdef/php": "@dev",
  "tyhpdef/php-ext-bcmath": "@dev",
  "tyhpdef/php-ext-gmp": "@dev",
  "tyhpdef/php-ext-decimal": "@dev"
},
"extra": {
  "tyhp": {
    "interopContractVersion": 1,
    "require": {
      "tyhpdef/php": "@dev"
    }
  }
}
```

Consumers of decimal pick up `tyhpdef/php` + `tyhpdef/php-ext-mbstring` by walking `require` → `tyhp/core` → core extras, plus decimal’s own extra (`tyhpdef/php`). They do **not** pick up the three optional-extension wrappers.

### App root (after plugin)

`require-dev` contains at least `tyhp/compiler` plus the merged extras (typically `tyhpdef/php`, `tyhpdef/php-ext-mbstring`, and any third-party extras in the tree). Runtime `tyhp/core` / `tyhp/decimal` stay in `require`.

### Schema

- `extra.tyhp.require` omitted or `{}` → no ambient tyhpdefs from this package (plugin still adds `tyhp/compiler` when `tyhp/core` is in the tree).
- Values are Composer constraints (including `@dev` for in-repo path repos).
- Unknown keys under `extra.tyhp` are ignored (do not fail the plugin).

Record the key in `CONVENTIONS.md` beside `interopContractVersion`.

---

## Track C classification

`TyhpCodeTyhpdefGenerator` still emits **this** project’s public `.tyhp` symbols only (`.tyhp` not `.tyhpdef`; `IsProjectTyhpSource` unchanged).

After the public symbol set is known (and `internal` / `private` omitted):

1. Collect every foreign name that appears in the emitted tyhpdef text: signatures, `implements` / `extends`, thin-mapping bodies, attributes that survive round-trip, etc.
2. Resolve the **owning package** the same way 21.1’s catalog does for a real declaration (the package that **really** declares the name; another package’s `extern` does not own it).
3. Classify:

| Owner relative to **this** library `composer.json` | Emit |
|----------------------------------------------------|------|
| This package | Normal declaration (already the Track C subject) |
| In `require` (runtime, e.g. `tyhp/core`) | Spell FQN; do not copy; do not extern |
| In `extra.tyhp.require` (and usually also `require-dev`) | Spell FQN; consumer plugin installs that tyhpdef |
| In `require-dev` only (not extras, not `require`) | Name-only `extern` of the right kind + `// @provided-by: <owner>` |
| Unknown / not in this package’s composer graph | Leave as written; bind reports `TYHP3019` / `3020` as today (do not invent an extern) |

A **public** type must **not** `extends` / `implements` an extern type (`TYHP3026` / `3027`). That is why `\JsonSerializable` requires `tyhpdef/php` in decimal’s extras, not merely in `require-dev`.

If `internal` backends are omitted, decimal’s public tyhpdef may contain **no** author-only names. That is success. Keep a **fixture library** that *does* mention an author-only type / function in a public signature so Track C extern is tested.

Write Track C `extern` into `package.tyhpdef` (this package’s include), not a separate Track B `externs.tyhpdef`. Regen overwrites `package.tyhpdef`.

Update `docs/content/tyhpdef_extern.md` and `cli_tyhpdefGeneration.md`: `tyhp build` **does** emit `extern` for author-only owners.

---

## `extern function` and `extern const`

### Grammar

Tyhpdef-only. Extend 21.1:

```tyhp
<?tyhpdef

// @provided-by: tyhpdef/php-ext-bcmath
extern function \bcadd;
extern const \GMP_ROUND_PLUSINF;
```

- Name-only, semicolon, no signature, no body, no generics.
- `extern function` / `extern const` cannot combine with `partial` / `omit` / `deprecated` / `obsolete` on the same declaration (`TYHP8018`).
- Illegal extra syntax → `TYHP8028` (same family as types).
- `.tyhp` `extern function` is not a keyword (parse error / `T_STRING` is enough).

The lexer already classifies `extern` as `T_TYHPDEF_EXTERN` when the next token starts with a letter or `\`. Add parser alternatives (`T_FUNCTION` / `T_CONST` + name + `;`) next to the existing class/interface/enum/bare rules.

### Binder / checker

- Occupancy of the function or const FQCN.
- Real declaration of the same FQCN and kind **silently wins** (21.1 table). Kind mismatch is `TYHP8029`.
- Using the name from `.tyhp` is **`TYHP4307`**. Broaden the existing code/message from “extern type” to “extern name” (type, function, or const). Do **not** allocate a second use-code.
- Tyhpdef signatures and mapping bodies may name an extern function/const; that is why the placeholder exists.
- Calling `\bcadd(...)` from `.tyhp` is a use.

### Not in this story

`extern trait`, signed function types, `extern` of methods.

---

## `internal` slice (not Story 25)

Follow Story 25 **Phase 1 grammar** and **binder `IsInternal`** only, plus public Track C omit.

**In 21.10:**

- Lexer `T_TYHP_INTERNAL` in `tyhp` mode (not PHP, not tyhpdef). Story 25 lists the productions: top-level class/interface/trait/enum/function/const/alias/extension, and `memberModifierGrammarAddon`.
- Binder sets `IsInternal` on the symbol. Two visibilities → **4002**.
- Emitter: strip `internal`; class members emit `public`; top-level unchanged (PHP default).
- Track C: skip `IsInternal` the same way `IsPrivate` is skipped. Public `package.tyhpdef` does not mention those symbols.

**Still Story 25:**

- Checker: consumer `.tyhp` / source-to-source access errors (4054 family).
- Internals overlay file + `extra.tyhp.package` `"internalOverlay"`.
- `includeInternals` opt-in.
- Occupancy parse of internals overlay when not opted in.
- LSP autocomplete filtering.

Until Story 25, `internal` is a **generation boundary** (and a keyword that type-checks inside the defining project because the checker does not yet hide it). Cross-project **source** references are not yet rejected; **package.tyhpdef** consumers already cannot see the symbols.

Decimal backends (`GmpBackend`, `BcMathBackend`, php-decimal backend, and any helper that exists only for those extensions) are marked `internal` in this story so they drop out of the public tyhpdef.

---

## Composer plugin

### Package shape

- Host: `tyhp/core`.
- `type`: `composer-plugin` (required for Composer 2 to call `PluginManager::registerPackage()` / instantiate `extra.class`. `PluginInstaller` still installs it like a library).
- `require`: `composer-plugin-api` `^2.3` (virtual; provided by Composer).
- `extra.class`: `Tyhp\Composer\Plugin`.
- Implement `Composer\Plugin\PluginInterface` + `EventSubscriberInterface`.
- Subscribe to `PluginEvents::PRE_DEPENDENCIES_SOLVING`.
- **No** `CommandProvider`.

Story 21.5’s “no plugin in v1” is amended: **no CommandProvider**; this PluginInterface **is** in scope here.

### Code

Hand-written PHP under `runtime/packages/core/plugin/`, included in core’s dist artifacts (`build-common.sh` / package layout). Namespace `Tyhp\Composer\` so it does not sit under the runtime `Tyhp\` PSR-4 of `src/Tyhp/`.

Constraints:

- No Tyhp generics runtime, no `\Tyhp\Type`, no emitted Mechanism D/C helpers.
- Catch `\Throwable`.
- Prefix global PHP functions with `\`.
- Composer types are referenced as `\Composer\...`.

### Dev vs no-dev

If Composer is installing with `--no-dev` / `COMPOSER_NO_DEV` / `$composer->getLocker()` install flags showing no-dev: **return immediately**. Production deploys that `require` `tyhp/core` must not rewrite the app’s `composer.json`.

### Persist

When in dev mode and the merged set differs from root `require-dev`:

1. Update the in-memory `RootPackage` / request (`setDevRequires`).
2. Write `require-dev` back to the root `composer.json` (preserve other JSON; pretty-print consistent with Composer).
3. Do **not** run a nested `composer update`.

The current solve then installs the new require-dev entries.

### `allow-plugins`

Init writes:

```json
"config": {
  "allow-plugins": {
    "tyhp/core": true
  }
}
```

Document that existing apps must allow the plugin once (Composer’s prompt or this config). If the plugin is not allowed, it does not run; CLI check still reports stale extras.

---

## Tree resolve

Goal: the **definitive** root `require-dev` list **before** `vendor/` is populated, so deep `extra.tyhp.require` chains and `require` edges that lead to more extras do not need N Composer runs.

### Algorithm

```
seen = {}
extras = {}          # name -> merged constraint
queue = names in root require ∪ root require-dev
        ∪ root extra.tyhp.require

while queue is not empty:
  pkg = pop
  if pkg in seen: continue          # cycle: stop this branch
  if is_platform(pkg): continue     # php, ext-*, composer-plugin-api, composer-runtime-api
  seen.add(pkg)
  meta = fetch_from_repos(pkg, constraint_for(pkg))
  merge meta.extra.tyhp.require into extras and queue those names
  for each name, constraint in meta.require:
    queue that name (runtime require may itself have extras)
```

Do **not** enqueue `require-dev` of `meta`.

`fetch_from_repos` uses Composer’s `RepositoryManager` (Packagist + path/vcs `repositories`). Pick a package version that satisfies the constraint under consideration (highest matching when several). Read that version’s `composer.json` extras.

After the fixpoint:

- Ensure `tyhp/compiler` is in `extras` (or a dedicated compiler slot) when `tyhp/core` is anywhere in `seen` / root require. Constraint: if root already pins compiler, keep if compatible; else use the plugin’s default (init’s `@dev` until published versions exist; match 21.5).
- Merge onto root `require-dev` with the constraint rules above.
- Disjoint pin → plugin error (Composer exception / `IO->writeError`) **and** a Tyhp diagnostic code for the CLI path. Do not continue the solve with a guessed union.

### Why both extra and require edges

- `tyhp/decimal` extras `{ tyhpdef/php }` is not enough for mbstring: decimal `require`s `tyhp/core`, and **core’s extras** include `tyhpdef/php-ext-mbstring`.
- A third-party package might `require` a Tyhp library that has extras, without itself listing extras.
- An extra package may `require` another package that has extras (or may extra-require it). Follow both.

### Cycles

A extra-requires B, B extra-requires A: skip the second visit. No diagnostic unless we cannot fetch metadata at all.

### Root extras

Honor the **root** `extra.tyhp.require` as well (an application may declare ambient tyhpdefs of its own).

---

## CLI check (`tyhp build` / `lint` / `composer sync`)

The plugin can miss the first `require`, users can disable plugins, and CI images can strip `require-dev`. The compiler must **detect** a stale graph without talking to Packagist.

### Inputs (local only)

- Root `composer.json` (`require`, `require-dev`, `extra.tyhp.require`).
- `vendor/composer/installed.json` (and each installed package’s `composer.json` extras).

### Stale when

1. An **installed** package lists `extra.tyhp.require` names that are not in root `require-dev` **and** not in root `require`, or are not installed.
2. Root `require` / `require-dev` names a `tyhp/*` package that is not installed (cannot read extras — still stale / missing install).
3. `tyhp/core` is in the graph (require or installed) and `tyhp/compiler` is missing from root `require-dev` or not installed (dev mode).
4. Constraint disjoint between an extra and the existing pin (same error as the plugin).

Do **not** fetch Packagist from this check. Deep extras that are not yet installed will look stale until the user syncs — that is the recovery path for the first-require miss.

### Behavior

| Mode | Behavior |
|------|----------|
| Default `tyhp build` / `tyhp lint` | Diagnostic explaining what is missing and how to sync. Do **not** invoke Composer. Build may continue or fail — **lock: fail the compile** when extras needed for **this** compilation are missing (cannot type-check against ambient types that are not loaded). If the gap is only `tyhp/compiler` but the native CLI is already running, warn and continue. |
| `--strict` / CI (`tyhp.json` / env) | Fail on any stale extra or missing compiler pin. |
| `tyhp build --fix` / `tyhp composer sync` | Write root `require-dev` from the **installed** extra closure (plus compiler), then invoke Composer update for those packages (user-opt-in). Still local-first; if extras on not-yet-installed packages are unknown, tell the user to `composer update` so the plugin can run. |

Never auto-update on an ordinary build.

Init remains a convenience; the check is the net.

Document the first-`composer require tyhp/core` miss in the CLI Composer / getting-started pages: run `composer update` or `tyhp composer sync` once after adding core.

---

## First-party proving ground

1. **`tyhp/core`** — extras + plugin artifacts; mbstring tyhpdef is ambient.
2. **`tyhp/decimal`** — extras only `tyhpdef/php`; optional wrappers stay require-dev; backends `internal`; regen `package.tyhpdef` (currently blocked on `\bcadd` / `\gmp_*` / `\Decimal\Decimal` without extern/internal).
3. **App-shaped fixture** — root requires `tyhp/core` + `tyhp/decimal`; after one plugin solve, `require-dev` has compiler + `tyhpdef/php` + `tyhpdef/php-ext-mbstring` and **not** bcmath/gmp/decimal wrappers.
4. **Public-optional fixture** — a tiny library whose **public** API names a require-dev-only type and function; Track C emits `extern class` / `extern function` with `@provided-by`.
5. **Third-party name fixture** — `extra.tyhp.require` of a non-`tyhpdef/*` package name (can be a path repo).
6. **Cycle fixture** — A extra-requires B extra-requires A; plugin terminates; no infinite loop.
7. **Disjoint pin fixture** — root `require-dev` pin incompatible with an extra → error.
8. **`--no-dev` fixture** — plugin does not write `composer.json`.

---

## Phases

### Phase 1 — Schema and first-party manifests

Stamp `extra.tyhp.require` on `tyhp/core` and `tyhp/decimal`. Duplicate ambient pins in `require-dev`. Add `"type": "composer-plugin"` + `composer-plugin-api` + `extra.class` + plugin autoload on core (source and dist `composer.json`). `CONVENTIONS.md` key. Dist layout includes `plugin/`.

### Phase 2 — `extern function` / `extern const`

Grammar, visitor, binder occupancy, real-wins, broaden `TYHP4307`. Parser/lexer regen. Grammar `technical-guide.md`. Tests.

### Phase 3 — Track C classification

Owner lookup; ambient FQN vs extern; `@provided-by`; do not extern `require`d packages; unknown origin unchanged. Reverse 21.1 Track C tests that asserted “never extern.” Docs `tyhpdef_extern.md` / `cli_tyhpdefGeneration.md`.

### Phase 4 — `internal` grammar + Track C omit

Story 25 Phase 1 productions; `IsInternal`; 4002; emit strip; Track C skip. **No** 4054, **no** overlay. Tests: parse; public tyhpdef omits `internal class`; PHP still has the class as public.

### Phase 5 — Composer plugin (tree resolve)

Hand PHP plugin; `PRE_DEPENDENCIES_SOLVING`; algorithm above; constraint merge; cycle skip; `--no-dev` no-op; persist `composer.json` + `setDevRequires`. PHPUnit in core `tests/` for merge/cycle/platform-skip with mocked repositories (do not hit live Packagist in CI).

### Phase 6 — CLI check and `tyhp composer sync`

Local stale detection; default / `--strict` / `--fix`. Init `allow-plugins`. MessageCode + resx + `--explain`.

### Phase 7 — Decimal / core proving ground

Mark decimal backends `internal`. Regen core and decimal `package.tyhpdef`. Confirm decimal public tyhpdef has no `\GMP` / `\bcadd` / `\Decimal\Decimal`. Confirm consumer fixture type-checks `Decimal` without optional wrappers and `TYHP4307` if it names `\GMP`.

### Phase 8 — Documentation and AIDevGuide

User docs: extras, plugin, first-require miss, `tyhp composer sync`, Track C extern, `extern function` / `const`, `internal` as a keyword that hides from published tyhpdef (full semantics → Story 25). Handbook: library authors fill extras; never put author-only tyhpdefs in extras. `AIDevGuide/REGEN.md` when user-facing pages change.

---

## Diagnostic codes

Allocate in `MessageCode.cs` at implementation (7700–7799 Composer action is unused). Proposed:

| Code | Role |
|------|------|
| **TYHP4307** | Existing. Broaden to extern **function** and **const** use from `.tyhp`. |
| **TYHP4002** | Existing. `internal` + another visibility. |
| **TYHP8018 / 8028 / 8029** | Existing. Illegal combo / body / kind mismatch on `extern function` / `const`. |
| **TYHP7700** | Disjoint `extra.tyhp.require` constraint vs existing root pin. |
| **TYHP7701** | Stale extras: named package missing from root require-dev / not installed. |
| **TYHP7702** | `tyhp/compiler` missing from root require-dev while `tyhp/core` is in the graph (strict / explain). |
| **TYHP7703** | Plugin / sync could not parse or write root `composer.json`. |

Exact numbers are `MessageCode.cs` at implement time; this table is the intent.

---

## Out of scope

- Story 25 checker, internals overlay, `includeInternals`, LSP.
- Signed extern functions/consts; `extern trait`; `extern` in `.tyhp`.
- Compiling the plugin from Tyhp; CommandProvider.
- `tyhp/core` `require` of `tyhp/compiler`; metapackage.
- Walking dependency `require-dev`; Packagist from `tyhp build`.
- Auto `composer update` on every build; sticky sync flag.
- Changing Composer `require` / `require-dev` meaning.
- Hand-editing `runtime/packages/*/src` to recover regen.

---

## Cross-Story References

| Story | Relation |
|-------|----------|
| **21.1** | `extern` occupancy, real-wins, `@provided-by`, `TYHP4307`. Track C “never extern” **superseded** here for author-only owners. Track B unchanged. |
| **20** | Track C generator; `extra.tyhp.package` include. |
| **21** | `tyhpdef/php` + `tyhpdef/php-ext-*` as extra / require-dev pins. |
| **21.5** | Init compiler pin; “no plugin” amended to **no CommandProvider**. This story adds PluginInterface on `tyhp/core`. |
| **21.9** | Predecessor; 21.10 is the last *implemented* Tier 2 story (22 stays deferred). |
| **04** | `tyhp/core` / `tyhp/decimal` proving ground. |
| **25** | Full `internal`. 21.10 lands grammar + public tyhpdef omit so 25 does not have to invent the token. |
| **13** | `tyhp composer` proxy; `sync` subcommand lives next to it. |
| **22** | Deferred. |
| **30** | Packagist / getting-started polish may restyle the first-require miss docs. |

---

## Golden Fixtures / Tests (Acceptance)

**Manifest / schema**

- [ ] Core `extra.tyhp.require` has `tyhpdef/php` and `tyhpdef/php-ext-mbstring`; both also in `require-dev`.
- [ ] Decimal extras have only `tyhpdef/php`; bcmath/gmp/decimal wrappers are require-dev only.
- [ ] Core does not list `tyhp/compiler` in extras; compiler package still `require`s core.

**`extern function` / `const`**

- [ ] `extern function \bcadd;` / `extern const \FOO;` parse in tyhpdef; signatures/bodies rejected (`TYHP8028`).
- [ ] Occupancy; real function/const wins; kind mismatch `TYHP8029`.
- [ ] `.tyhp` call / const fetch is `TYHP4307` with `@provided-by` help; including the providing package clears it.

**Track C**

- [ ] Ambient owner → FQN, no `extern`.
- [ ] require-dev-only owner → `extern` + `@provided-by`.
- [ ] `require`d `tyhp/core` types not externed and not copied.
- [ ] Public `implements` of a require-dev-only interface is rejected or avoided (decimal uses extras for `JsonSerializable`).
- [ ] Fixture library public API naming `\GMP` emits `extern class \GMP`.
- [ ] Fixture public mapping / signature naming `\bcadd` emits `extern function \bcadd`.

**`internal`**

- [ ] `internal class Foo {}` parses; `public internal class` is 4002.
- [ ] Track C public tyhpdef omits `internal` types and members.
- [ ] Emitted PHP still contains the class/method (public / unprefixed).
- [ ] No 4054 / no internals overlay file in this story.

**Plugin**

- [ ] Core source and dist `composer.json` `"type"` is `composer-plugin` (Composer 2 never activates `library` + `extra.class`).
- [ ] Mocked repos: decimal → core extras yield php + mbstring + compiler on root require-dev in **one** solve; not bcmath/gmp/decimal.
- [ ] Third-party extra name (non-`tyhpdef/*`) is merged.
- [ ] Runtime `require` of a package that has extras is followed.
- [ ] Cycle A↔B extras: terminates.
- [ ] Compatible existing pin kept; disjoint → error.
- [ ] Platform packages not fetched.
- [ ] `--no-dev` does not write `composer.json`.
- [ ] Dependency `require-dev` is not walked.

**CLI**

- [ ] Installed extra not in root require-dev → 7701; `--strict` fails; default explains; `--fix` / `sync` writes.
- [ ] Missing compiler pin → 7702 (strict fails; native CLI warn-and-continue when already running).
- [ ] No Packagist calls in the check unit tests.

**Proving ground**

- [ ] Decimal `package.tyhpdef` regenerates; no `\GMP` / `\bcadd` / `\Decimal\Decimal` in the public file.
- [ ] Consumer fixture type-checks `Decimal` with only php + mbstring tyhpdefs.
- [ ] Consumer naming `\GMP` is `TYHP4307` until `tyhpdef/php-ext-gmp` is included.

**Docs**

- [ ] First-`require` miss documented.
- [ ] `extra.tyhp.require` documented for library authors.
- [ ] Track C extern sentence in `tyhpdef_extern.md` matches shipped behavior.
- [ ] AIDevGuide regenerated if user-facing pages change.
