# Tyhp

Tyhp is a strongly typed superset of PHP. It type-checks `.tyhp` source and emits readable PHP that runs on a standard PHP 8.2–8.5 runtime.

This is **alpha** software (`805.0.0-alpha.1`). The compiler, type checker, and expression-tree (parsable lambda) surface work. Language Server, sourcemaps, Xdebug proxy, and a complete PHP-stub catalog are **not** in this alpha. Syntax and package versions may still change.

## Requirements

- PHP 8.2 or later (to run compiled output)
- Composer (to install `tyhp/php`, `tyhp/core`, and other runtime packages)
- For framework-dependent compiler binaries: [.NET 9 runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- Self-contained compiler binaries do not need .NET installed

The compiler is a .NET application. Deployment servers only need PHP.

## Install the compiler

Download a binary from [GitHub Releases](https://github.com/tyhpproject/tyhp/releases), or:

```bash
curl -fsSL https://raw.githubusercontent.com/tyhpproject/tyhp/main/scripts/install.sh | bash -s --
```

The install script picks OS/arch and prefers a self-contained build. Use `--framework-dependent` if you already have .NET 9. Verify with `tyhp version`.

There is no `composer global require tyhp/compiler` package in this alpha.

## Quick start

```bash
mkdir my-tyhp-project && cd my-tyhp-project
tyhp init
tyhp build
php build/index.php
```

`tyhp build` writes PHP under `build/` and a `composer.json` that requires the Tyhp runtime packages (`tyhp/php`, `tyhp/core`, and others the project uses). After Packagist is live:

```bash
composer install
```

Until then, clone this repository and let the compiler inject **path repositories** that point at `runtime/packages/` in the checkout.

## Runtime packages (Composer)

| Package | Role |
|---------|------|
| `tyhp/php` | Type definitions for PHP builtins (PHP 8.2 baseline) |
| `tyhp/core` | Runtime support (generics, typed variables, property accessors) |
| `tyhp/async` | Promise, event loop, cancellation |
| `tyhp/decimal` | Arbitrary-precision decimal |
| `tyhp/lambda` | Expression-tree / PropertyPath runtime |

Composer vendor is `tyhp/`. GitHub org is [`tyhpproject`](https://github.com/tyhpproject).

## Documentation

- Site: [https://tyhplang.com](https://tyhplang.com)
- Source: [`docs/content/`](docs/content/)
- Versioning: [`VERSIONING.md`](VERSIONING.md)
- Contributing: [`CONTRIBUTING.md`](CONTRIBUTING.md)
- License: [Apache License 2.0](LICENSE.txt)
- Alpha release checklist (maintainers): [`ALPHA_RELEASE.md`](ALPHA_RELEASE.md)

## What is in this alpha

Done through roadmap **Tier 1** (stories 01–16.5): parser, binder, checker, emitter, `tyhp build` / `tyhp lint` / `tyhp init`, error-message quality, interop contract, parsable lambdas, callable signature utilities.

Not in this alpha: LSP / VS Code extension, sourcemaps, Xdebug proxy, tyhpdef generator CLI, optimizer, `internal`, generic parameter defaults, web playground, and more.
