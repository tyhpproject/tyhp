---
title: 'Installing Tyhp'
status:
  tier: 1
  story: '13'
  state: complete
---

The Tyhp compiler is a .NET application targeting .NET 9. This page covers system requirements and how to install the **805.0.0-alpha.1** compiler.

## System Requirements

- PHP 8.2, 8.3, 8.4, or 8.5 — required to run compiled output
- Composer — required to install `tyhp/php`, `tyhp/core`, and other runtime packages
- Self-contained compiler binaries — no .NET install needed
- Framework-dependent binaries — [.NET 9 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

:::note
.NET is only needed on the machine that compiles Tyhp (and only for framework-dependent builds). Deployment servers need PHP only.
:::

## Installation (recommended)

Download a binary from [GitHub Releases](https://github.com/tyhpproject/tyhp/releases), or:

```
curl -fsSL https://raw.githubusercontent.com/tyhpproject/tyhp/main/scripts/install.sh | bash -s --
```

The script selects OS and architecture. It prefers a self-contained build. Pass `--framework-dependent` if .NET 9 is already installed.

There is **no** `composer global require tyhp/compiler` package in this alpha, and no `dotnet tool install --global tyhp` feed yet.

## Manual download

1. Open the [releases page](https://github.com/tyhpproject/tyhp/releases) and download the asset for your platform (`tyhp-osx-arm64`, `tyhp-linux-x64`, `tyhp-win-x64.exe`, or the `-fxdependent` variant).
2. Place the binary on your PATH (for example `/usr/local/bin/tyhp`).
3. Verify with `tyhp version`.

## Verifying Installation

```
tyhp version
```

You should see output similar to:

```
Tyhp Compiler v805.0.0-alpha.1
Target PHP: 8.5
```

## Runtime packages

Compiled projects need Composer packages such as `tyhp/php` (PHP builtin tyhpdefs) and `tyhp/core`. After those packages are on Packagist, `tyhp build` writes `composer.json` requires and you run `composer install`. Until then, a source checkout of this repository can resolve them via Composer path repositories under `runtime/packages/`.

`tyhp/php` in this alpha is a **PHP 8.2 baseline**. APIs that exist only on PHP 8.3–8.5 may be missing from the stubs.
