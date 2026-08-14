---
title: 'Previous Releases'
---

## 805.0.0-alpha.1 (current public alpha)

First public compiler alpha. GitHub Release tag `v805.0.0-alpha.1`. MAJOR `805` means the emit ceiling is PHP 8.5 (the compiler still targets 8.2–8.4 via `output.phpVersion`). Runtime packages version independently as `80N.X.Y` (source `0.0` → `802.0.0`–`805.0.0`).

This is a **prerelease**. Semver compatibility guarantees apply from the first stable `805.0.0`, not from alpha. See [Roadmap](release_roadmap.md) and [Release Planning](release_planning.md).

## Release History

| Version | Notes |
|---------|--------|
| 805.0.0-alpha.1 | Public alpha. Tier 1 (stories 01–16.5) plus generic type-parameter defaults. LSP, sourcemaps, Xdebug proxy, and `generate_tyhpdef` are not included. |

:::note
Compiler MAJOR encodes the highest supported PHP version (`805` = PHP 8.5 and below). Tyhp syntax removal happens on MINOR. Runtime Composer packages do **not** use the compiler version string. See Release Planning.
:::
