# tyhp/php

Type definitions (`.tyhpdef`) for PHP builtins and commonly bundled extensions so the Tyhp compiler can type-check `\strlen`, `DateTime`, SPL, and similar APIs.

This alpha is a **PHP 8.2 baseline** harvested from `runtime/php-extensions/php8.2.9/`. APIs that exist only on PHP 8.3–8.5 may be missing until Story 20.5 version gating lands.

Scalar extension methods (`$str->length()` and friends) are **not** included.

## Versions

Package MAJOR is the PHP this artifact is constrained for (`802` = 8.2, `803` = 8.3, `804` = 8.4, `805` = 8.5), not the Tyhp compiler version. This package’s own line is `X.Y` in `composer.json` (currently `0.0`). Packagist versions are `80N.X.Y`. Bump this package without bumping the compiler.

**Application** (one PHP version, match `output.phpVersion` in `tyhp.json`):

```bash
composer require tyhp/php:804.0.0
```

**Library** (PHP 8.3+ on this X — Composer picks the artifact for the consumer’s PHP):

```json
"tyhp/php": "803.0.* || 804.0.* || 805.0.*"
```

Until Packagist lists this package, use a path repository that points at this directory inside a Tyhp compiler checkout.
