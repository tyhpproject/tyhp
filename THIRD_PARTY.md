# Third-party acknowledgments

Tyhp is Apache License 2.0 (`LICENSE.txt`). This file credits **upstream projects and artifact hosts** the toolchain relies on. That includes binaries, php.net HTML manuals, and stub corpora Tyhp **downloads into a local cache** (not vendored in git) as well as type information harvested into generated `.tyhpdef` overlays.

Using Tyhp does not re-license those works. Their licenses stay with them. If you redistribute a cached PHP runtime or generated overlay tree, follow **their** terms as well as Apache 2.0.

Generated output may also carry a `SOURCES.md` next to stub overlays. Each managed PHP cache directory should contain a short `ATTRIBUTION.txt` naming the provider and artifact URL that produced it.

## PHP runtimes (Track A / `generate_tyhpdef`)

These are **not** the PHP you use to run your app. Tyhp keeps private CLIs so it can reflect extensions with a known `php.ini`.

| Provider | What we use | Links |
|----------|-------------|--------|
| **The PHP Group** | Language, official Windows NTS zips, release metadata | [php.net](https://www.php.net/), [windows.php.net](https://windows.php.net/download/), [downloads.php.net/~windows/releases/](https://downloads.php.net/~windows/releases/) |
| **StaticPHP** (formerly **static-php-cli**) | Prebuilt portable/static `php` CLIs for macOS and Linux | [static-php.dev](https://static-php.dev), [dl.static-php.dev](https://dl.static-php.dev), [crazywhalecc/static-php-cli](https://github.com/crazywhalecc/static-php-cli), [static-php/hosted](https://github.com/static-php/hosted) |
| **Homebrew** | Bottle fallback (direct GHCR download + extract; Tyhp does not run `brew`) | [brew.sh](https://brew.sh), [formulae.brew.sh](https://formulae.brew.sh), [Homebrew/homebrew-core](https://github.com/Homebrew/homebrew-core), [ghcr.io/v2/homebrew/core](https://github.com/orgs/Homebrew/packages) |

PHP itself is distributed under the [PHP License](https://www.php.net/license/index.php). StaticPHP is MIT. Homebrew formulae and bottles follow each formula’s license (PHP plus dependency libraries such as OpenSSL, ICU, and others listed on the formula page).

## PHP documentation (Track A tyhpdef comments)

Tyhp may download the official HTML manuals (`php_manual_{locale}.html.gz`) from php.net to assemble `/** */` comments on generated extension tyhpdefs. That prose is **not** authored by Tyhp.

| Provider | What we use | Links |
|----------|-------------|--------|
| **The PHP Documentation Group** | Official HTML manuals (`php_manual_{locale}.html.gz`, [CC BY 3.0](https://www.php.net/manual/en/cc.license.php)) | [php.net/docs](https://www.php.net/docs.php), [php.net/license (PHP Documentation)](https://www.php.net/license/index.php) |

Credit also appears in generated `SOURCES.md` when a run used the manuals. `--no-docs` skips the download.

## Stub corpora (Layer 2 tyhpdef harvest)

Raw trees are fetched into a gitignored cache and translated into overlay `.tyhpdef` files. Credit also appears in generated headers / `SOURCES.md`. URLs are listed in `runtime/README.md`.

| Project | Role |
|---------|------|
| [Psalm stubs](https://github.com/vimeo/psalm) | Enrichment input |
| [PHPStan stubs](https://github.com/phpstan/phpstan-src) | Enrichment input |
| [Phan stubs](https://github.com/phan/phan) | Enrichment input |
| [PhpStorm stubs](https://github.com/jetbrains/phpstorm-stubs) | Enrichment input |

## Not attributed here

- **User `--php` binaries** — whatever PHP the caller pointed at; Tyhp does not redistribute that tree.
- **NuGet / Composer / docs `vendor/`** — those packages keep their own license files.
- **Private or unpublished PECL extensions** — not downloaded; use `--php` or a hand tyhpdef.
