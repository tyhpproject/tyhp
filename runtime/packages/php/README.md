# tyhp/php

Type definitions (`.tyhpdef`) for PHP builtins and commonly bundled extensions so the Tyhp compiler can type-check `\strlen`, `DateTime`, SPL, and similar APIs.

This alpha is a **PHP 8.2 baseline** harvested from `runtime/php-extensions/php8.2.9/`. APIs that exist only on PHP 8.3–8.5 may be missing until Story 20.5 version gating lands.

Scalar extension methods (`$str->length()` and friends) are **not** included.

## Install

```bash
composer require tyhp/php:805.0.0-alpha.1
```

Until Packagist lists this package, use a path repository that points at this directory inside a Tyhp compiler checkout.
