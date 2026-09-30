using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefFallbackBinderTests
{
    [Fact]
    public void SingleFallback_IsVisibleWithoutAutoloadOrder()
    {
        using var builder = Project();
        WriteTyhpdefPackage(builder, "pkg-a", "acme/pkg-a", "alpha/a", """
            <?tyhpdef
            fallback function collect(mixed $value = null): int;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().BeEmpty(Join(diagnostics));
        var function = FindFunction(global, "collect");
        function.Should().NotBeNull();
        function!.IsFallback.Should().BeTrue();
        function.SourceFile.Should().Contain("pkg-a");
    }

    [Fact]
    public void TwoFallbacks_WithoutAutoloadOrder_Error()
    {
        using var builder = Project();
        WriteTyhpdefPackage(builder, "pkg-a", "acme/pkg-a", "alpha/a", """
            <?tyhpdef
            fallback function collect(): int;
            """);
        WriteTyhpdefPackage(builder, "pkg-b", "acme/pkg-b", "alpha/b", """
            <?tyhpdef
            fallback function collect(): int;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefFallbackOrderUnknown);
        FindFunction(global, "collect").Should().BeNull();
    }

    [Fact]
    public void AutoloadOrder_EarlierPackageWins()
    {
        using var builder = Project();
        WriteTyhpdefPackage(builder, "pkg-a", "acme/pkg-a", "alpha/a", """
            <?tyhpdef
            fallback function collect(): int;
            """);
        WriteTyhpdefPackage(builder, "pkg-b", "acme/pkg-b", "alpha/b", """
            <?tyhpdef
            fallback function collect(): string;
            """);
        WriteAutoload(builder, bBeforeA: true);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().BeEmpty(Join(diagnostics));
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefFallbackSignatureMismatch);
        var function = FindFunction(global, "collect");
        function.Should().NotBeNull();
        function!.SourceFile.Should().Contain("pkg-b");
        function.IsFallback.Should().BeTrue();
    }

    [Fact]
    public void OrdinaryLoadedAfterFallback_Errors()
    {
        using var builder = Project();
        WriteTyhpdefPackage(builder, "pkg-a", "acme/pkg-a", "alpha/a", """
            <?tyhpdef
            function collect(): int;
            """);
        WriteTyhpdefPackage(builder, "pkg-b", "acme/pkg-b", "alpha/b", """
            <?tyhpdef
            fallback function collect(): int;
            """);
        WriteAutoload(builder, bBeforeA: true);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefFallbackRedeclare);
    }

    [Fact]
    public void EnginePackage_BeatsFallback()
    {
        using var builder = Project();
        WriteTyhpdefPackage(builder, "stubs", "tyhpdef/php", null, """
            <?tyhpdef
            function str_contains(string $haystack, string $needle): bool;
            """);
        WriteTyhpdefPackage(builder, "polyfill", "acme/polyfill", "acme/fill-base", """
            <?tyhpdef
            fallback function str_contains(string $haystack, string $needle): bool;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().BeEmpty(Join(diagnostics));
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefFallbackSignatureMismatch);
        var function = FindFunction(global, "str_contains");
        function.Should().NotBeNull();
        function!.IsFallback.Should().BeFalse();
        function.SourceFile.Should().Contain("stubs");
    }

    [Fact]
    public void UserFunction_DuplicatesFallback()
    {
        using var builder = Project();
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function collect(): void {}
            """);
        WriteTyhpdefPackage(builder, "pkg-a", "acme/pkg-a", "alpha/a", """
            <?tyhpdef
            fallback function collect(): int;
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
    }

    private static TestProjectBuilder Project()
    {
        var builder = new TestProjectBuilder();
        builder.WithTyhpJson("""
            {
                "include": ["src/**/*.tyhp"],
                "output": { "path": "build/", "phpVersion": "8.2" }
            }
            """);
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function app_entry(): void {}
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");
        builder.WithConfigValue("tyhpdefInclude:2", "./stubs/composer.json");
        builder.WithConfigValue("tyhpdefInclude:3", "./polyfill/composer.json");
        return builder;
    }

    private static void WriteTyhpdefPackage(
        TestProjectBuilder builder,
        string directory,
        string packageName,
        string? upstream,
        string tyhpdef)
    {
        var require = upstream is null
            ? ""
            : "\"require\": { \"" + upstream + "\": \"1.0.0\" },\n";
        builder.WithTyhpFile($"{directory}/composer.json", """
            {
                "name": "PACKAGE",
                REQUIRE
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./_tyhpdef/*.tyhpdef"]
                        }
                    }
                }
            }
            """.Replace("PACKAGE", packageName, StringComparison.Ordinal)
            .Replace("REQUIRE", require, StringComparison.Ordinal));
        builder.WithTyhpFile($"{directory}/_tyhpdef/types.tyhpdef", tyhpdef);
    }

    private static void WriteAutoload(TestProjectBuilder builder, bool bBeforeA)
    {
        var first = bBeforeA
            ? "$vendorDir . '/alpha/b/helpers.php'"
            : "$vendorDir . '/alpha/a/helpers.php'";
        var second = bBeforeA
            ? "$vendorDir . '/alpha/a/helpers.php'"
            : "$vendorDir . '/alpha/b/helpers.php'";
        builder.WithTyhpFile("vendor/composer/autoload_files.php", $$"""
            <?php
            $vendorDir = dirname(__DIR__);
            $baseDir = dirname($vendorDir);
            return array(
                'bbbb' => {{first}},
                'aaaa' => {{second}},
            );
            """);
        builder.WithTyhpFile("vendor/composer/installed.json", """
            {
                "packages": [
                    { "name": "alpha/a", "install-path": "../alpha/a" },
                    { "name": "alpha/b", "install-path": "../alpha/b" }
                ]
            }
            """);
    }

    private static (GlobalScope Global, Tyhp.Domain.Diagnostics.DiagnosticBag Diagnostics) Bind(TestProjectBuilder builder)
    {
        var result = IsolatedCompilation.BindProject(builder);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static FunctionDeclarationSymbol? FindFunction(GlobalScope global, string name)
    {
        FunctionDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is FunctionDeclarationSymbol func
                && string.Equals(func.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = func;
            }
        });
        return found;
    }

    private static void Walk(Tyhp.TyhpLang.Binder.Scopes.Interfaces.IBaseScope scope, Action<Tyhp.TyhpLang.Binder.Symbols.Interfaces.IBaseSymbol> visit)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            visit(symbol);
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            Walk(child, visit);
        }
    }

    private static string Join(Tyhp.Domain.Diagnostics.DiagnosticBag diagnostics)
        => string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
