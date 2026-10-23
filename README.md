<p align="center">
  <img src="https://tyhplang.com/assets/images/tyhp-logo-alt.svg" alt="Tyhp Logo" width="132">
</p>
<p align="center">
  <strong>A typed superset of the PHP language.</strong><br>
</p>

<p align="center">
  <a href="https://tyhplang.com"><strong>Documentation</strong></a>
  ·
  <a href="https://github.com/tyhpproject/tyhp-examples"><strong>Examples</strong></a>
  ·
  <a href="https://github.com/tyhpproject/tyhp/releases"><strong>Releases</strong></a>
  ·
  <a href="CONTRIBUTING.md"><strong>Contributing</strong></a>
</p>

<p align="center">
  <a href="https://tyhplang.com"><img src="https://img.shields.io/badge/docs-tyhplang.com-34A4F4" alt="Documentation"></a>
  <a href="https://www.php.net"><img src="https://img.shields.io/badge/PHP-8.2--8.5-777BB4?logo=php&logoColor=white" alt="PHP 8.2 to 8.5"></a>
  <a href="LICENSE.txt"><img src="https://img.shields.io/badge/license-Apache%202.0-34A4F4" alt="Apache License 2.0"></a>
  <a href="https://github.com/tyhpproject/tyhp/releases"><img src="https://img.shields.io/badge/status-805.1.1--beta.3-orange" alt="Beta 805.1.1-beta.3"></a>
</p>

---

Tyhp is a typed superset of the PHP language. You write `.tyhp` (and can keep `.php` in the same project). The compiler type-checks the Tyhp source and emits readable PHP. Required types, null safety, generics, structs, `async`/`await`, extension methods, and expression trees are part of Tyhp; the output is ordinary PHP that runs on a PHP runtime.

**Compile with Tyhp. Deploy with PHP.**

> **Public beta (`805.1.1-beta.3`).** The compiler, type checker, emitter, and CLI work today. Syntax may still change. See the [roadmap](https://tyhplang.com/release_roadmap.html).

## Why Tyhp

The compiler type-checks `.tyhp`, then emits human-readable PHP 8.2–8.5. Deploy that output on a PHP runtime. Mix `.tyhp` and `.php` in one project. Describe existing PHP libraries with [`.tyhpdef` files](https://tyhplang.com/tyhpdef_about.html).

Types are part of the language. Every variable, parameter, property, and return is typed or inferred. Types are non-nullable by default. [`mixed`](https://tyhplang.com/tyhp_0600_theMixedType.html) has to be narrowed before you use it.

Tyhp-only type syntax is erased after the checker accepts the program. Structs become PHP arrays. `async`/`await` and parsable lambdas lower to calls in `tyhp/async` and `tyhp/lambda`. The usual comparison is TypeScript’s relationship to JavaScript.

More on the model: [What is Tyhp?](https://tyhplang.com/intro.html)

## Features

Each feature below links to its page on [tyhplang.com](https://tyhplang.com). Small projects that use them are in the [examples repository](https://github.com/tyhpproject/tyhp-examples). The one-page index is the [Tyhp quick reference](https://tyhplang.com/quickref.html).

### Types

| Feature | What it is |
| --- | --- |
| [Types and locals](https://tyhplang.com/tyhp_0100_typesAndLocals.html) | Required or inferred types. Non-nullable by default. |
| [Type narrowing and guards](https://tyhplang.com/tyhp_0200_typeNarrowingAndGuards.html) | `is`, `instanceof`, and custom guards narrow the type. |
| [The mixed type](https://tyhplang.com/tyhp_0600_theMixedType.html) | Narrow `mixed` before use. |
| [Type aliases](https://tyhplang.com/tyhp_0700_typeAliases.html) | Names for unions, literals, and other type expressions. |
| [Static value types](https://tyhplang.com/tyhp_0800_staticValueTypes.html) | Literal types for one specific value, such as `42` or `'hello'`. |
| [New types](https://tyhplang.com/tyhp_0150_newTypes.html) | `decimal` and other Tyhp types. |
| [Generics](https://tyhplang.com/tyhp_0500_generics.html) | Reusable classes and functions, checked at compile time. |
| [Generic defaults](https://tyhplang.com/tyhp_3500_genericDefaults.html) | Default type arguments. |
| [Structs](https://tyhplang.com/tyhp_0400_structs.html) | Named, typed data that compiles to PHP arrays. |
| [The `with` keyword](https://tyhplang.com/tyhp_2200_withKeyword.html) | Set properties on objects and structs in one expression. |
| [Object shapes](https://tyhplang.com/tyhp_3400_newTypeConstraint.html) | Structural object types and `__New<T>`. |

### Language

| Feature | What it is |
| --- | --- |
| [`async` / `await`](https://tyhplang.com/tyhp_2600_asyncAndAwait.html) | Sequential async code on PHP Fibers, through `tyhp/async`. |
| [Extensions](https://tyhplang.com/tyhp_2100_extensions.html) | Methods on types you do not own. |
| [Operator overloads](https://tyhplang.com/tyhp_1600_operatorOverloads.html) | Arithmetic and comparison on your types. |
| [Disposables](https://tyhplang.com/tyhp_2300_disposables.html) | `using` and `:=` close resources when the scope ends. |
| [Parsable lambdas](https://tyhplang.com/tyhp_3000_parsableLambdas.html) | Capture `fn` as an expression tree or property path. |
| [Compile-time constructs](https://tyhplang.com/tyhp_2700_compileTimeConstructs.html) | `nameof()`, `typeof()`, and `default()`. |
| [Scalar pseudo-objects](https://tyhplang.com/tyhp_1000_scalarPseudoObjects.html) | Methods on `string`, `array`, and other scalars. |
| [Short function syntax](https://tyhplang.com/tyhp_1100_shortFunctionSyntax.html) | A named function or method as a single expression. |
| [Overloads](https://tyhplang.com/tyhp_1200_functionOverloads.html) | Multiple signatures for one function or method. |
| [Classes and property accessors](https://tyhplang.com/tyhp_1300_newObjectDeclSyntax.html) | Constructor syntax, plus getters and setters (including PHP 8.4 property hooks). |
| [Trait requirements](https://tyhplang.com/tyhp_2000_traitRequirements.html) | A trait can require `extends` and `implements`. |
| [`internal`](https://tyhplang.com/tyhp_3100_internalModifier.html) | Visibility inside a project. |
| [Null-conditional assignment](https://tyhplang.com/tyhp_3300_nullConditionalAssignment.html) | Assign through `?->`. |
| [PHP version gating](https://tyhplang.com/tyhp_0320_phpVersionGating.html) | `declare(php=…)` and `#[\Tyhp\Php]`. |

### PHP interop

| Feature | What it is |
| --- | --- |
| [Tyhpdef](https://tyhplang.com/tyhpdef_about.html) | Declaration files that describe existing PHP to the checker. |
| [Tyhpdef quick reference](https://tyhplang.com/quickref_tyhpdef.html) | The declaration syntax on one page. |
| [Including files](https://tyhplang.com/tyhp_0300_includingFiles.html) | Project includes and Composer autoload. |

## Examples

[tyhp-examples](https://github.com/tyhpproject/tyhp-examples) is a set of small Composer projects: a todo list, contacts, expenses, async jobs, files, expression trees, and others. Each directory is its own project and has its own README.

## Install

PHP 8.2+ and Composer run compiled output. The Tyhp compiler is only needed on the machine that builds. Full steps, Windows, and the framework-dependent build: [Installing Tyhp](https://tyhplang.com/intro_installation.html).

**Composer:**

```bash
composer require --dev tyhp/compiler
vendor/bin/tyhp init
composer update
vendor/bin/tyhp build
```

**GitHub Releases** (native binary on `PATH`):

```bash
curl -fsSL https://raw.githubusercontent.com/tyhpproject/tyhp/main/scripts/install.sh | bash -s --
mkdir my-tyhp-project
cd my-tyhp-project
tyhp init
composer install
tyhp build
php build/src/index.php
```

On Windows, use [`scripts/install.ps1`](scripts/install.ps1). Check the compiler with `tyhp version`. A new project from `tyhp init` through a running program: [Getting started](https://tyhplang.com/intro_gettingStarted.html).

## Runtime packages

Some language features need a small Composer package at runtime. Others are libraries you add when you want them. Overview: [Runtime packages](https://tyhplang.com/package_intro.html). Versions and `composer.json` pins: [Composer runtime packages](https://tyhplang.com/project_composerPackages.html).

| Package | Role |
| --- | --- |
| [`tyhp/core`](https://tyhplang.com/package_core.html) | Runtime support: generics, typed variables, scalar methods |
| [`tyhp/async`](https://tyhplang.com/package_async.html) | `Promise`, the event loop, and cancellation |
| [`tyhp/decimal`](https://tyhplang.com/package_decimal.html) | Arbitrary-precision decimal |
| [`tyhp/lambda`](https://tyhplang.com/package_lambda.html) | Expression trees and `PropertyPath` |
| [`tyhp/io`](https://tyhplang.com/package_io.html) | Non-blocking I/O on the async event loop |
| [`tyhp/parallel`](https://tyhplang.com/package_parallel.html) | Worker processes for CPU-heavy or blocking work |
| [`tyhp/compiler`](https://tyhplang.com/package_compiler.html) | Composer shim that installs and runs the compiler |
| `tyhpdef/php` | Type definitions for PHP builtins. See [About Tyhpdef](https://tyhplang.com/tyhpdef_about.html). |

## Documentation

- Docs: [tyhplang.com](https://tyhplang.com)
- [Quick reference](https://tyhplang.com/quickref.html)
- [CLI](https://tyhplang.com/cli_intro.html) — `build`, `lint`, `init`, Composer
- [Language server](https://tyhplang.com/cli_languageServer.html) and [IDE plugins](https://github.com/tyhpproject/tyhp-ide-plugin)
- [Diagnostics](https://tyhplang.com/diagnostics_overview.html)
- Examples: [tyhpproject/tyhp-examples](https://github.com/tyhpproject/tyhp-examples)
- Docs source: [tyhp-docs-src](https://github.com/tyhpproject/tyhp-docs-src)
- Versioning: [`VERSIONING.md`](VERSIONING.md)
- Contributing: [`CONTRIBUTING.md`](CONTRIBUTING.md)
- License: [Apache License 2.0](LICENSE.txt)
- Branding: [`BRANDING.md`](BRANDING.md)
- Third-party acknowledgments: [`THIRD_PARTY.md`](THIRD_PARTY.md)

## Acknowledgments

PHP is a trademark of the PHP Group. Tyhp is not affiliated with, endorsed, or sponsored by the PHP Group.

Tyhp downloads official PHP Windows builds, StaticPHP (formerly static-php-cli) portable CLIs, and — if those are unavailable — Homebrew bottles, plus community stub corpora for tyhpdef enrichment. Extension generation may also download php.net HTML manuals (PHP Documentation Group) to build doc comments. Those projects remain under their own licenses. Full credits: [`THIRD_PARTY.md`](THIRD_PARTY.md).
