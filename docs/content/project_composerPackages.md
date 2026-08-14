---
title: 'Composer Runtime Packages'
status:
  tier: 1
  story: '04'
  state: complete
---

Compiled Tyhp programs depend on small Composer packages (`tyhp/php`, `tyhp/core`, and others). Those packages **do not** share the compiler version. You can ship a new `tyhp/core` without bumping the Tyhp compiler.

The **compiler** version `805.0.0-alpha.1` only means this toolchain can *emit* PHP up through 8.5 (and still emit 8.2–8.4 when `output.phpVersion` says so).

Each **package** has its own `X.Y` (in that package’s `composer.json`). Packagist artifacts are `80N.X.Y` where `80N` is the PHP that artifact is for (`802` = 8.2, `803` = 8.3, `804` = 8.4, `805` = 8.5).

This alpha’s packages start at source `0.0`, so the Packagist pins look like `804.0.0` for PHP 8.4. If `tyhp/core` later becomes `1.4`, PHP 8.4 apps require `804.1.4` (or `804.1.*`). `tyhp/lambda` can be `2.0` at the same time.

## Version map (this alpha)

| `output.phpVersion` | Package MAJOR | Pin when the package is `0.0` | Composer `php` on that artifact |
|---------------------|---------------|-------------------------------|---------------------------------|
| `"8.2"` | `802` | `802.0.0` | `~8.2.0` |
| `"8.3"` | `803` | `803.0.0` | `~8.3.0` |
| `"8.4"` | `804` | `804.0.0` | `~8.4.0` |
| `"8.5"` | `805` | `805.0.0` | `~8.5.0` |

`tyhp init` and `tyhp build` (when not using a compiler-checkout path repository) write the pin for **each** package from that package’s own `X.Y` plus your `output.phpVersion`. They do not all have to be the same `X.Y`.

## Applications

An app targets **one** PHP version. Require that MAJOR and **that package’s** `X.Y`:

```json
{
    "require": {
        "php": "~8.4.0",
        "tyhp/php": "804.0.0",
        "tyhp/core": "804.0.0"
    }
}
```

If `tyhp/decimal` is on `1.2` and you use decimals, require `tyhp/decimal: 804.1.2` (or `804.1.*`). `tyhp build` adds those packages when the compiled code needs them.

`804.0.*` is fine for an app if you want patch updates on that package line without changing PHP MAJOR.

## Libraries

A library that supports several PHP versions should **or** the PHP majors and keep **that package’s** X (not the compiler MINOR, and not some other package’s X):

```json
{
    "require": {
        "php": ">=8.3",
        "tyhp/php": "803.0.* || 804.0.* || 805.0.*",
        "tyhp/core": "803.0.* || 804.0.* || 805.0.*"
    }
}
```

If `tyhp/core` is on `1.y` and `tyhp/lambda` is on `2.y`:

```json
"tyhp/core": "803.1.* || 804.1.* || 805.1.*",
"tyhp/lambda": "803.2.* || 804.2.* || 805.2.*"
```

Composer then installs the artifact that matches the *consumer's* PHP (`~8.3.0` vs `~8.4.0` vs `~8.5.0`).

Do **not** mix X values for the **same** package (`803.0.* || 804.1.*` for core). `803.1` and `804.1` are the same core line on different PHP; `804.0` is an older core line.

## Packages

| Package | Role |
|---------|------|
| `tyhp/php` | Type definitions for PHP builtins (8.2 baseline in this alpha) |
| `tyhp/core` | Generics, typed variables, property accessors |
| `tyhp/async` | Promise, event loop, cancellation |
| `tyhp/decimal` | Arbitrary-precision decimal |
| `tyhp/lambda` | Expression-tree / PropertyPath runtime |

Each is published with four PHP-target tags (`802.`…`805.` plus that package’s `X.Y`).

## Path repositories (compiler checkout)

If `tyhp build` can see `runtime/packages/` next to the compiler, it pins each package’s **source** `X.Y` (for example `0.0`) and adds Composer path repositories. That is only for developing against a compiler tree. Packagist consumers use the `80N.X.Y` form.

:::tip
`output.phpVersion` in `tyhp.json` chooses the emitted PHP **and** the `80N` prefix. Each `tyhp/*` package’s `X.Y` is independent.
:::
