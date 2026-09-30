using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
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
public class VendorPackageDiscoveryTests
{
    [Fact]
    public void VendorPhpExtPackage_LoadsTyhpdefsFromRequireDevLayout()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePhpPackage(builder);
        WritePhpExtCurlPackage(builder);

        var (global, diagnostics) = Bind(builder.BuildProject());

        FindFunction(global, "widget_ext_probe").Should().NotBeNull(
            "tyhpdef/php-ext-* vendor packages must load; they are not php-{major}.{minor}");
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void VendorPhp84Directory_IsSkippedWhenTargeting82()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePhpPackage(builder);
        builder.WithTyhpFile("vendor/tyhpdef/php-8.4/composer.json", """
            {
                "name": "tyhpdef/php-8.4",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./*.tyhpdef"]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/tyhpdef/php-8.4/only84.tyhpdef", """
            <?tyhpdef
            function only_in_php84(): void;
            """);

        var (global, _) = Bind(builder.BuildProject(), phpVersion: "8.2");

        FindFunction(global, "only_in_php84").Should().BeNull(
            "legacy tyhpdef/php-8.4 directories stay version-gated");
        FindFunction(global, "php_always_probe").Should().NotBeNull();
    }

    [Fact]
    public void DistPathRepoCore_DoesNotWarnTyhp8027()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePhpPackage(builder);
        builder.WithTyhpFile("published/tyhp-core/805.0.1/composer.json", """
            {
                "name": "tyhp/core",
                "version": "805.0.1",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./package.tyhpdef"]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("published/tyhp-core/805.0.1/package.tyhpdef", """
            <?tyhpdef
            function core_from_dist(): void;
            """);

        var project = builder.BuildProject();
        if (!TryLinkVendorCoreToDist(project.GetProjectPath()))
        {
            return;
        }

        var (global, diagnostics) = Bind(project);

        FindFunction(global, "core_from_dist").Should().NotBeNull();
        MissingRuntimePackages(diagnostics).Should().NotContain("core");
        MissingRuntimePackages(diagnostics).Should().Contain("lambda");
    }

    [Fact]
    public void BundledPhpPackage_WinsOverInstalledImpl()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePhpPackage(builder);
        builder.WithTyhpFile("vendor/acme/calendar/composer.json", """
            {
                "name": "acme/calendar",
                "extra": {
                    "tyhp": {
                        "package": { "include": ["./*.tyhpdef"] }
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/acme/calendar/bundled.tyhpdef", """
            <?tyhpdef
            function calendar_bundled_probe(): void;
            """);
        builder.WithTyhpFile("vendor/tyhpdef/acme-calendar-impl/composer.json", """
            {
                "name": "tyhpdef/acme-calendar-impl",
                "extra": {
                    "tyhp": {
                        "package": { "include": ["./*.tyhpdef"] }
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/tyhpdef/acme-calendar-impl/impl.tyhpdef", """
            <?tyhpdef
            function calendar_impl_probe(): void;
            """);

        var (global, diagnostics) = Bind(builder.BuildProject());

        FindFunction(global, "calendar_bundled_probe").Should().NotBeNull();
        FindFunction(global, "calendar_impl_probe").Should().BeNull();
        diagnostics.Warnings.Should().Contain(w =>
            w.Code == MessageCode.TyhpdefBundledPackagePreferred);
    }

    [Fact]
    public void ImplPackage_LoadsWhenPhpPackageHasNoBundledTypes()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePhpPackage(builder);
        builder.WithTyhpFile("vendor/acme/calendar/composer.json", """
            {
                "name": "acme/calendar"
            }
            """);
        builder.WithTyhpFile("vendor/tyhpdef/acme-calendar/composer.json", """
            {
                "name": "tyhpdef/acme-calendar",
                "type": "metapackage",
                "extra": { "tyhp": { "impl": "tyhpdef/acme-calendar-impl" } }
            }
            """);
        builder.WithTyhpFile("vendor/tyhpdef/acme-calendar/decoy.tyhpdef", """
            <?tyhpdef
            function calendar_decoy_from_impl_key(): void;
            """);
        builder.WithTyhpFile("vendor/tyhpdef/acme-calendar-impl/composer.json", """
            {
                "name": "tyhpdef/acme-calendar-impl",
                "extra": {
                    "tyhp": {
                        "public": "tyhpdef/acme-calendar",
                        "package": { "include": ["./*.tyhpdef"] }
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/tyhpdef/acme-calendar-impl/impl.tyhpdef", """
            <?tyhpdef
            function calendar_impl_probe(): void;
            """);

        var (global, diagnostics) = Bind(builder.BuildProject());

        FindFunction(global, "calendar_impl_probe").Should().NotBeNull();
        FindFunction(global, "calendar_decoy_from_impl_key").Should().BeNull();
        diagnostics.Warnings.Should().NotContain(w =>
            w.Code == MessageCode.TyhpdefBundledPackagePreferred);
    }

    private static List<string> MissingRuntimePackages(DiagnosticBag diagnostics)
    {
        var names = new List<string>();
        foreach (var diagnostic in diagnostics.Warnings)
        {
            if (diagnostic.Code != MessageCode.TyhpdefRuntimePackageNotFound
                || diagnostic.FormatParams is not { Length: > 0 })
            {
                continue;
            }

            var name = diagnostic.FormatParams[0]?.ToString();
            if (!string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static void WriteUserProject(TestProjectBuilder builder)
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

    private static void WritePhpPackage(TestProjectBuilder builder)
    {
        builder.WithTyhpFile("vendor/tyhpdef/php/composer.json", """
            {
                "name": "tyhpdef/php",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./*.tyhpdef"]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/tyhpdef/php/probe.tyhpdef", """
            <?tyhpdef
            function php_always_probe(): void;
            """);
    }

    private static void WritePhpExtCurlPackage(TestProjectBuilder builder)
    {
        builder.WithTyhpFile("vendor/tyhpdef/php-ext-widget/composer.json", """
            {
                "name": "tyhpdef/php-ext-widget",
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./*.tyhpdef"]
                        }
                    }
                }
            }
            """);
        builder.WithTyhpFile("vendor/tyhpdef/php-ext-widget/widget.tyhpdef", """
            <?tyhpdef
            function widget_ext_probe(): void;
            """);
    }

    private static bool TryLinkVendorCoreToDist(string projectRoot)
    {
        var vendorTyhp = Path.Combine(projectRoot, "vendor", "tyhp");
        Directory.CreateDirectory(vendorTyhp);
        var link = Path.Combine(vendorTyhp, "core");
        var target = Path.Combine(projectRoot, "published", "tyhp-core", "805.0.1");
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return Directory.Exists(link) && File.Exists(Path.Combine(link, "composer.json"));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) Bind(
        Project project,
        string phpVersion = "8.2")
    {
        using var compilationService = new CompilationService();
        var options = CompilationOptions.FromProject(project, o =>
        {
            o.PhpVersion = phpVersion;
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
