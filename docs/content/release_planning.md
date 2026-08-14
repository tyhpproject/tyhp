---
title: 'Release Planning'
---

Tyhp **805.0.0-alpha.1** is a public alpha. This page describes versioning, what “alpha” means, and how later releases are expected to work.

## Current status

- Compiler version: **805.0.0-alpha.1** (MAJOR `805` = highest supported PHP is 8.5)
- PHP floor: **8.2** (not 8.1)
- Roadmap: **Tier 1 complete** (stories 01–16.5). Tier 2+ (LSP, sourcemaps, Xdebug, playground, full `tyhp/php` ecosystem) is not in this alpha.
- Semver compatibility guarantees apply from the first **stable** 805.x release, not from alpha.

A prerelease suffix (`-alpha.N`, `-beta.N`, `-rc.N`) is allowed before stable `805.0.0`. Alpha may still break source compatibility. See `VERSIONING.md` in the compiler repository.

## Versioning Strategy

Tyhp uses semantic versioning (MAJOR.MINOR.PATCH). The compiler MAJOR encodes the highest PHP version the release explicitly supports, written as the PHP major digit followed by the two-digit PHP minor. For example, compiler version 805.0.0 means "PHP 8.5 and below" — the compiler still emits for 8.2–8.4 when `output.phpVersion` is set.

Published runtime packages (`tyhp/core` and friends) version independently of the compiler. Each has its own `X.Y`; Packagist MAJOR is the PHP target (`804.1.4` means PHP 8.4 + core `1.4`). A PHP 8.3 app requires `803.{X.Y}` for that package; a library that supports 8.3+ can require `803.X.* || 804.X.* || 805.X.*` using **that package's** X. See the Composer Runtime Packages page.

- MAJOR — Encodes the highest supported PHP version as &lt;php-major&gt;&lt;php-minor-two-digits&gt;. So 704 = PHP 7.4, 800 = PHP 8.0, 804 = PHP 8.4, 805 = PHP 8.5, and (hypothetically) 953 = PHP 9.53. MAJOR **only** changes when that PHP ceiling changes. It is not Tyhp's breaking-change counter.
- MINOR — Tyhp language features, compiler capabilities, deprecations, and — after notice — removal or replacement of Tyhp syntax. This is the Tyhp version axis for a given PHP target. Resets to 0 on a new MAJOR.
- PATCH — Bug fixes and non-breaking tweaks (including opt-in flags that keep current defaults). Resets to 0 on a new MINOR.

This encoding means a Tyhp MAJOR bump only happens alongside a PHP minor version change (e.g., PHP 8.3 to 8.4). Tyhp syntax deprecation and removal happen on MINOR, even when they are not caused by a new PHP version. When PHP 9.0 is targeted, the corresponding Tyhp MAJOR would be 900.

## Release Process

After alpha, releases are expected to follow:

1. Development — New features and fixes land on the default branch.
2. Alpha / Beta — Public testing. Breaking changes are allowed.
3. Release Candidate (RC) — Feature-complete for that MAJOR; only critical fixes.
4. Stable Release — Tagged; semantic versioning guarantees apply from this point.
5. Patch Releases — Bug fixes as patch versions on the current stable MAJOR.

## Backward Compatibility

Within a stable major version, Tyhp aims to maintain backward compatibility for compiled output. Alpha and beta do **not** make that promise. When breaking changes to Tyhp syntax are necessary after stable, they follow a deprecation cycle:

1. The existing syntax is deprecated with a compiler warning in version N.
2. The deprecated syntax continues to work for at least one minor version.
3. The syntax is removed or changed in version N+1 or later.

## PHP Syntax Conflict Resolution

Because Tyhp is a superset of PHP, new PHP versions may occasionally introduce syntax that conflicts with Tyhp-specific syntax. When this occurs, Tyhp works through the steps in `VERSIONING.md` **in order**: first **integrate-and-detect** (keep both syntaxes if they can be distinguished at compile time); then deprecate Tyhp’s form with compile-time options; then remove the old syntax in a later **MINOR**. Do not wait for the next PHP ceiling / MAJOR.

## Tracking Changes

- The [roadmap](release_roadmap.md) page
- The [GitHub issue tracker](https://github.com/tyhpproject/tyhp/issues)
- GitHub Release notes
