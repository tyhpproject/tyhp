# Contributing to Tyhp

Tyhp is in **beta** (`805.1.0-beta.1`). Issues and pull requests are welcome. Read this before opening either.

## Legal

You will need to complete a Contributor License Agreement (CLA). Briefly, this agreement testifies that you are granting us permission to use the submitted change according to the terms of the project's license, and that the work being submitted is under appropriate copyright. Upon submitting a pull request, you will automatically be given instructions on how to sign the CLA.

User-facing names, taglines, and claims follow [`BRANDING.md`](BRANDING.md). The Apache license covers the code, not the Tyhp name or logo.

## Reporting issues

1. Search [existing issues](https://github.com/tyhpproject/tyhp/issues) (open and closed).
2. Include:
   - Compiler version (`tyhp version`)
   - PHP version (`php --version`)
   - A small `.tyhp`/`.tyhpdef` repro if possible
   - What you expected vs what happened

Questions about language design belong in an issue labeled as a question or discussion, not as a bug.

## Development setup

You need:

- [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- [PHP 8.2+](https://www.php.net/downloads.php) and [Composer](https://getcomposer.org/download/) to run compiled output and runtime package tests

```bash
git clone https://github.com/tyhpproject/tyhp.git
cd tyhp
dotnet restore
dotnet test tests/Tyhp.Tests/Tyhp.Tests.csproj
```

CI runs that same test project on `ubuntu-latest` (see `.github/workflows/tests.yml`). Maintainers cutting a public alpha follow [`ALPHA_RELEASE.md`](ALPHA_RELEASE.md).

Build the compiler:

```bash
dotnet build tyhp.csproj
```

Run it from the build output, for example:

```bash
dotnet run --project tyhp.csproj -- version
```

## Pull requests

- Target `main` once that is the default public branch.
- Keep changes focused. Do not mix unrelated refactors with a bug fix.
- Add or update tests under `tests/Tyhp.Tests/` when behavior changes.
- Do not hand-edit generated PHP. Fix Tyhp first (compiler, checker, emitter, or tyhpdefs) and re-emit. Package sources live in [tyhp-runtime-src](https://github.com/tyhpproject/tyhp-runtime-src) (`TYHP_RUNTIME_SRC`, or a sibling `../tyhp-runtime-src`).
- User-facing CLI strings go through the localization `.resx` files (`Resources/CLI.TyhpHostedService.en-US.resx` and the culture-neutral sibling).

## Code of conduct

See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## License

Contributions are under the [Apache License 2.0](LICENSE.txt).
