# Tyhp test project

.NET test suite: `tests/Tyhp.Tests/`

```bash
dotnet test tests/Tyhp.Tests/Tyhp.Tests.csproj
dotnet test tests/Tyhp.Tests/Tyhp.Tests.csproj --filter "Category=Parser"
dotnet test tests/Tyhp.Tests/Tyhp.Tests.csproj --filter "Category=Conformance"
dotnet test tests/Tyhp.Tests/Tyhp.Tests.csproj --filter "Category=EndToEnd"
dotnet test tests/Tyhp.Tests/Tyhp.Tests.csproj --filter "Category=Integration"
dotnet test tests/Tyhp.Tests/Tyhp.Tests.csproj --filter "Category=PHP"
```

After the test project has already been built, skip restore+build on review slices:

```bash
dotnet test tests/Tyhp.Tests/Tyhp.Tests.csproj --no-restore --no-build --filter "FullyQualifiedName~YourTestClass"
```

Prefer a class or `FullyQualifiedName~` filter over `Category=Checker` when iterating (that slice is still large). Test classes run in parallel with each other by default; the `AstCache` collection and the `ProcessGlobalState` collection (`Message` localizer, `Environment.ExitCode`, `Console.Out`/`Console.Error` redirection, CWD) stay serial because they mutate state shared by the whole process.

Conformance golden fixtures: `tests/conformance/` (see `tests/conformance/README.md`).

PHP runtime package tests: `cd runtime && composer install && composer test` (also wrapped by `Category=PHP` in .NET).

Update snapshots: `UPDATE_SNAPSHOTS=true dotnet test tests/Tyhp.Tests/Tyhp.Tests.csproj --filter "Category=EndToEnd"`
