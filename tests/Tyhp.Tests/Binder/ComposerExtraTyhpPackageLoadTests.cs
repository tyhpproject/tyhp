using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class ComposerExtraTyhpPackageLoadTests
{
    [Fact]
    public void VendorComposerJson_WithPackageObject_LoadsInclude()
    {
        using var builder = new TestProjectBuilder();
        WriteApp(builder);
        builder.WithTyhpFile("vendor/acme/lib/composer.json", """
            {
                "name": "acme/lib",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./types.tyhpdef"]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/acme/lib/types.tyhpdef", """
            <?tyhpdef
            function acme_lib_probe(): void;
            """);

        var (global, diagnostics) = Bind(builder.BuildProject());

        FindFunction(global, "acme_lib_probe").Should().NotBeNull();
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void VendorComposerJson_ExtraTyhpWithoutPackage_DoesNotLoadTyhpdefFile()
    {
        using var builder = new TestProjectBuilder();
        WriteApp(builder);
        builder.WithTyhpFile("vendor/acme/lib/composer.json", """
            {
                "name": "acme/lib",
                "extra": {
                    "tyhp": {
                        "interopContractVersion": 1,
                        "require": {
                            "tyhpdef/acme-stubs": "@dev"
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/acme/lib/package.tyhpdef", """
            <?tyhpdef
            function should_not_load(): void;
            """);

        var (global, diagnostics) = Bind(builder.BuildProject());

        FindFunction(global, "should_not_load").Should().BeNull();
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void VendorComposerJson_PackageNonObject_DoesNotLoad()
    {
        using var builder = new TestProjectBuilder();
        WriteApp(builder);
        builder.WithTyhpFile("vendor/acme/lib/composer.json", """
            {
                "name": "acme/lib",
                "extra": {
                    "tyhp": {
                        "package": "./package.tyhpdef"
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/acme/lib/package.tyhpdef", """
            <?tyhpdef
            function should_not_load(): void;
            """);

        var (global, _) = Bind(builder.BuildProject());

        FindFunction(global, "should_not_load").Should().BeNull();
    }

    [Fact]
    public void VendorComposerJson_NeighborWithoutPackage_DoesNotLoad()
    {
        using var builder = new TestProjectBuilder();
        WriteApp(builder);
        builder.WithTyhpFile("vendor/acme/lib/composer.json", """
            {
                "name": "acme/lib",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./types.tyhpdef"]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/acme/lib/types.tyhpdef", """
            <?tyhpdef
            function acme_lib_probe(): void;
            """);
        builder.WithTyhpFile("vendor/acme/other/composer.json", """
            {
                "name": "acme/other",
                "extra": {
                    "tyhp": {
                        "interopContractVersion": 1
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/acme/other/package.tyhpdef", """
            <?tyhpdef
            function neighbor_should_not_load(): void;
            """);

        var (global, diagnostics) = Bind(builder.BuildProject());

        FindFunction(global, "acme_lib_probe").Should().NotBeNull();
        FindFunction(global, "neighbor_should_not_load").Should().BeNull();
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void ExplicitTyhpdefInclude_ComposerJson_LoadsWhenSentinelPresent()
    {
        using var builder = new TestProjectBuilder();
        builder.WithTyhpJson("""
            {
                "include": ["src/**/*.tyhp"],
                "tyhpdefInclude": ["./pkg/composer.json"],
                "output": { "path": "build/", "phpVersion": "8.2" }
            }
            """);
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function app_entry(): void {}
            """);
        builder.WithTyhpFile("pkg/composer.json", """
            {
                "name": "acme/pkg",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./types.tyhpdef"],
                            "overlay": ["./overlay.tyhpdef"]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("pkg/types.tyhpdef", """
            <?tyhpdef
            function pkg_include_probe(): int;
            """);
        builder.WithTyhpFile("pkg/overlay.tyhpdef", """
            <?tyhpdef
            function pkg_overlay_probe(): void;
            """);

        var project = builder.BuildProject();
        File.Exists(Path.Combine(project.GetProjectPath(), "pkg", "package.tyhp.json")).Should().BeFalse();

        var (global, diagnostics) = Bind(project);

        FindFunction(global, "pkg_include_probe").Should().NotBeNull();
        FindFunction(global, "pkg_overlay_probe").Should().NotBeNull();
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void IncludeArray_ComposerJsonPattern_IsNotCompiledAsSource()
    {
        // The target contract's `tyhp.json` example lists `composer.json` package manifests
        // directly in `include` (not just `tyhpdefInclude`). Those entries must be excluded
        // from the compiled/linted source set the same way `.tyhpdef` patterns are, or the
        // JSON file gets parsed as Tyhp source and fails.
        using var builder = new TestProjectBuilder();
        builder.WithTyhpJson("""
            {
                "include": ["src/**/*.tyhp", "./pkg/composer.json"],
                "output": { "path": "build/", "phpVersion": "8.2" }
            }
            """);
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function app_entry(): void {}
            """);
        builder.WithTyhpFile("pkg/composer.json", """
            {
                "name": "acme/pkg",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./types.tyhpdef"]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("pkg/types.tyhpdef", """
            <?tyhpdef
            function pkg_include_probe(): int;
            """);

        var project = builder.BuildProject();
        var sourceFiles = project.GetProjectSourceFiles().ToList();

        sourceFiles.Should().Contain(path => path.Replace('\\', '/').EndsWith("src/app.tyhp"));
        sourceFiles.Should().NotContain(path => path.Replace('\\', '/').EndsWith("pkg/composer.json"));

        var (global, diagnostics) = Bind(builder.BuildProject());
        FindFunction(global, "pkg_include_probe").Should().NotBeNull();
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void OwnProjectComposerJson_IsNotAutoLoaded()
    {
        using var builder = new TestProjectBuilder();
        WriteApp(builder);
        builder.WithTyhpFile("composer.json", """
            {
                "name": "app/root",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./hidden.tyhpdef"]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("hidden.tyhpdef", """
            <?tyhpdef
            function own_project_should_not_autoload(): void;
            """);

        var (global, _) = Bind(builder.BuildProject());

        FindFunction(global, "own_project_should_not_autoload").Should().BeNull();
    }

    private static void WriteApp(TestProjectBuilder builder)
    {
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
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) Bind(Project project)
    {
        using var compilationService = new CompilationService();
        var options = CompilationOptions.FromProject(project, o =>
        {
            o.PhpVersion = "8.2";
            o.PhpVersionWasDefaulted = false;
            o.EnableAstCache = false;
            o.SkipChecking = true;
        });
        var userFile = Path.Combine(project.GetProjectPath(), "src", "app.tyhp");
        var result = compilationService.ParseFiles([userFile], options);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static FunctionDeclarationSymbol? FindFunction(GlobalScope global, string name)
    {
        var resolver = new NameResolver(global, new DiagnosticBag());
        if (resolver.ResolveRelativeName([name], global) is FunctionDeclarationSymbol direct)
        {
            return direct;
        }

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

    private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
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
}
