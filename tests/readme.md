# Tyhp test project

.NET test suite: `tests/Tyhp.Tests/`

```bash
dotnet test tyhp.sln
dotnet test tyhp.sln --filter "Category=Parser"
dotnet test tyhp.sln --filter "Category=Conformance"
dotnet test tyhp.sln --filter "Category=EndToEnd"
dotnet test tyhp.sln --filter "Category=Integration"
dotnet test tyhp.sln --filter "Category=PHP"
```

Conformance golden fixtures: `tests/conformance/` (see `tests/conformance/README.md`).

PHP runtime package tests: `cd runtime && composer install && composer test` (also wrapped by `Category=PHP` in .NET).

Update snapshots: `UPDATE_SNAPSHOTS=true dotnet test tyhp.sln --filter "Category=EndToEnd"`
