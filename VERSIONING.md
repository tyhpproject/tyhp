# Tyhp — Versioning, PHP Compatibility & Positioning

> **What this is:** the canonical description of how Tyhp is versioned, how it stays compatible with PHP over
> time, and how it positions itself relative to other "typed PHP" efforts. The public website mirrors this in
> `docs/content/release_planning.md`, `docs/content/intro_newSyntaxCreation.md`, and
> `docs/content/other_hack.md`; **this file is the source of truth** — keep those in sync with it.

---

## Semantic versioning with a PHP-encoded MAJOR

Tyhp uses standard three-part semantic versioning (`MAJOR.MINOR.PATCH`), e.g. the current compiler is
`805.0.0-alpha.1`.

### Pre-release (alpha / beta / RC)

A prerelease suffix (`-alpha.N`, `-beta.N`, `-rc.N`) is allowed **before** the first stable `805.0.0`.
Prereleases may still break source compatibility. Semver guarantees in this document apply from the first
stable release of a MAJOR. Composer and Git tags for this alpha use `805.0.0-alpha.1` (GitHub Release tags
may be `v805.0.0-alpha.1`).

### MAJOR — encodes the highest PHP version supported

The MAJOR part is **not** a plain incrementing number and is **not** Tyhp's breaking-change counter. It encodes
the highest PHP version this Tyhp release is explicitly compatible with, as `<php-major><php-minor-two-digits>`:

| Tyhp MAJOR | Highest supported PHP |
|-----------|-----------------------|
| `704` | PHP 7.4 and below |
| `800` | PHP 8.0 and below |
| `801` | PHP 8.1 and below |
| `804` | PHP 8.4 and below |
| `805` | PHP 8.5 and below (current) |
| `953` | PHP 9.53 and below (hypothetical future) |

MAJOR **only** changes when that PHP ceiling changes (for example PHP 8.5 → 8.6 becomes Tyhp `805` → `806`).
A MAJOR bump often includes work to parse and emit for the new PHP, which can force existing Tyhp projects to
adapt — but Tyhp-only language evolution does **not** wait for a PHP version bump, and must not be scheduled
as "the next MAJOR."

### MINOR — Tyhp language and compiler version

Bumped when Tyhp adds language features or compiler capabilities, lands significant fixes, deprecates syntax,
or — after notice — removes or replaces Tyhp syntax. This is the axis that identifies *Tyhp* versions for a
given PHP target. These changes come almost entirely from Tyhp (rarely from a PHP update). Resets to `0` on a
new MAJOR.

Breaking Tyhp syntax is allowed on MINOR after a deprecation cycle (see below). It is not reserved for MAJOR,
because MAJOR may not move for years if PHP does not.

### PATCH — safe, non-breaking fixes

Internal fixes and minor, backward-compatible changes that are safe to update at any time. Resets to `0` on a
new MINOR. An emergency compile-time flag whose **default preserves existing Tyhp behavior** is a PATCH.

### Runtime Composer packages (PHP-target MAJOR + independent X.Y)

The **compiler** version `805.0.0-alpha.1` means the compiler can emit for PHP up through 8.5. It still emits
for 8.2, 8.3, and 8.4 when `output.phpVersion` says so. That number is **not** the runtime package version.

Each of `tyhp/core`, `tyhp/async`, `tyhp/decimal`, `tyhp/lambda`, and `tyhp/php` has its **own**
`X.Y` in that package's `composer.json`. Bump a package without bumping the compiler. Published
artifacts are:

`80N.X.Y` where `80N` is the PHP target (`802` … `805`).

Example: `tyhp/core` source `1.4` → Packagist `802.1.4`, `803.1.4`, `804.1.4`, `805.1.4`. Source
`0.0` → `802.0.0` … `805.0.0`.

A library that supports several PHP versions ORs majors and keeps **that package's** X:
`803.1.* || 804.1.* || 805.1.*` for core `1.y`. `tyhp/lambda` can be `2.y` at the same time.

In-tree path repositories pin the source `X.Y` (one tree, `php: >=8.2`). Packagist consumers
use the `80N.X.Y` form. See `docs/content/project_composerPackages.md`.

### Interop contract version (separate axis)

The Tyhp ↔ PHP **interop contract** version (`extra.tyhp.interopContractVersion` on runtime packages;
`InteropContract.CurrentVersion` in the compiler) is **independent** of compiler `MAJOR.MINOR.PATCH` and of
Composer package semver. Additive runtime surface may leave the contract version unchanged; any breaking change
to an emitted name or signature bumps `interopContractVersion` on both the compiler and the affected packages.
See `docs/content/cli_interopContract.md` and `CONVENTIONS.md` §8. PHP-target compatibility remains encoded in
the compiler MAJOR (Story 21) as above — that axis and the interop contract can move in the same release, but
they answer different questions.

---

## When each version part changes

- **MAJOR** — only when the **highest supported PHP minor** changes (e.g. PHP 8.5 → 8.6). That is when Tyhp
  must parse and understand the new PHP syntax/features. A MAJOR bump does not, by itself, remove Tyhp syntax
  or land unrelated Tyhp features.
- **MINOR** — Tyhp feature work; deprecation of Tyhp syntax; removal or replacement of Tyhp syntax after
  notice; less commonly a breaking syntax change from a PHP **patch** release (rare); occasionally a
  significant/security fix that must restrict or add syntax.
- **PATCH** — minor bug fixes and slight, never-backward-incompatible tweaks, including opt-in flags that
  keep current defaults. Always safe to update within the same MINOR.

---

## PHP syntax conflict resolution

Because Tyhp builds on PHP's syntax first and only adds new syntax when necessary, a future PHP version can
introduce syntax that conflicts with, or overlaps, a Tyhp syntax. When that happens Tyhp works through the
following, in order (usually spread across one or more releases):

1. **Integrate & detect.** Try to adopt PHP's change while detecting each syntax (PHP's and Tyhp's) at compile
   time so both can coexist. Not always practical or possible, but always the preferred first step. *(Usually a
   MINOR change. If the conflict arrives with a new PHP minor, a MAJOR bump may ship in the same train because
   the PHP ceiling moved — the Tyhp syntax work is still MINOR.)*
2. **PHP implements the same feature differently.** Deprecate Tyhp's own syntax in favor of PHP's. Compile
   flags may enable/disable either syntax until Tyhp drops the old one. *(MINOR to deprecate; a later MINOR to
   change the default and remove the old syntax.)*
3. **PHP implements a *different* feature whose syntax conflicts with Tyhp's.** If the two usages can be
   reliably differentiated at compile time, keep both. If not, migrate Tyhp's syntax to be compatible: first
   deprecate the old syntax (with compile-time enable/disable options), then remove it. *(MINOR throughout —
   do not wait for the next PHP / MAJOR bump.)*
4. **PHP adds new functionality that doesn't affect any Tyhp syntax.** Adopt the new PHP syntax at the next
   feasible release. Tyhp tracks PHP dev releases and roadmaps to anticipate this. *(MINOR, or MAJOR only if
   that PHP version is also a new ceiling.)*

### Rollout plan for incompatible changes

This plan applies whether the incompatibility comes from a new PHP version **or** from Tyhp changing its own
syntax. Removal and default-flips are **MINOR** work. They are not held for the next MAJOR, because MAJOR only
moves when the PHP ceiling does.

- **a. [immediate]** Alert developers (via the language website) about the incompatibility.
- **b. [asap]** Emergency **PATCH** that adds compile-time option(s) to disable the new, incompatible syntax
  (PHP and/or Tyhp) and to enable/disable the corresponding alternative. Default: keep existing Tyhp behavior.
  May be delayed if parsing new PHP syntax is substantial work.
- **c. [asap, if applicable]** A **MINOR** that deprecates the old conflicting Tyhp syntax and, if PHP does not
  fully replace it, provides a new Tyhp alternative. The alternative is off by default; the option from (b)
  still applies. Gives developers time to migrate.
- **d. [later MINOR]** The old syntaxes are removed in favor of the new ones, and the compile options from (b)
  and (c) are removed. This can be `805.N.0` on the same PHP ceiling; it does not require `806.0.0`.

If the conflict is introduced by a new PHP minor, (b)–(d) may ship on the new MAJOR (`806.x`) because that
release is the one that claims support for that PHP. The version *part* that carries the Tyhp syntax change is
still MINOR.

---

## Support lifecycle

Tyhp follows the **same support schedule as the PHP version its MAJOR targets**
(see <https://www.php.net/supported-versions.php>). A Tyhp MAJOR is actively supported and updated on the same
timeline as its matching PHP version, then receives security updates on the same PHP schedule.

A MINOR is supported/updated only until the next MINOR on the same MAJOR, per Tyhp's own roadmaps (which remain
subject to PHP's roadmap changes).

Tyhp tracks PHP via:

- PHP news/posts — <https://www.php.net/>
- RFCs (new/current/confirmed) — <https://wiki.php.net/rfc>
- PHP supported versions — <https://www.php.net/supported-versions.php>
- PHP manual — <https://www.php.net/manual/en/index.php>
- PHP source — <https://github.com/php/php-src>

