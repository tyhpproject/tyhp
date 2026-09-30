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

/// <summary>
/// Story 20.5 Phase 7: Composer-loaded / included tyhpdef packages filter through the
/// same binder gates as user files, so one included package can expose
/// different APIs at <c>output.phpVersion</c> 8.2 vs 8.4.
/// </summary>[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story20.5")]
public class PhpVersionGatedPackageLoadTests
{
    [Theory]
    [InlineData("vendor")]
    [InlineData("include")]
    public void GatedPackage_Php82Vs84_YieldsDifferentVisibleApis(string discovery)
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WriteGatedPackage(builder, discovery);

        var project = builder.BuildProject();
        var userFile = Path.Combine(project.GetProjectPath(), "src", "app.tyhp");

        var at82 = Bind(project, userFile, phpVersion: "8.2");
        var at84 = Bind(project, userFile, phpVersion: "8.4");

        at82.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", at82.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        at84.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", at84.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        FindFunction(at82.Global, "gated_package_always").Should().NotBeNull();
        FindFunction(at84.Global, "gated_package_always").Should().NotBeNull();
        FindObject(at82.Global, "GatedPackageAlways").Should().NotBeNull();
        FindObject(at84.Global, "GatedPackageAlways").Should().NotBeNull();

        FindFunction(at82.Global, "gated_package_84_only").Should().BeNull(
            "declaration-level #[\\Tyhp\\Php(\">=8.4\")] must be omitted at 8.2");
        FindFunction(at84.Global, "gated_package_84_only").Should().NotBeNull(
            "declaration-level #[\\Tyhp\\Php(\">=8.4\")] must bind at 8.4");
        FindObject(at82.Global, "GatedPackage84Only").Should().BeNull();
        FindObject(at84.Global, "GatedPackage84Only").Should().NotBeNull();

        FindFunction(at82.Global, "gated_package_file_84").Should().BeNull(
            "tyhpdef declare(php=\">=8.4\") { } must omit the inner function at 8.2");
        FindFunction(at84.Global, "gated_package_file_84").Should().NotBeNull(
            "tyhpdef declare(php=\">=8.4\") { } must bind the inner function at 8.4");
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

    private static void WriteGatedPackage(TestProjectBuilder builder, string discovery)
    {
        var root = discovery == "vendor" ? "vendor/tyhpdef/acme-stubs" : "packages/acme-stubs";
        if (discovery == "include")
        {
            builder.WithConfigValue("tyhpdefInclude:0", $"./{root}/composer.json");
        }

        builder.WithTyhpdefPackageComposer(root, """
            {
                "include": ["./*.tyhpdef", "./*.tyhp"]
            }
            """, packageName: "tyhpdef/acme-stubs");

        builder.WithTyhpFile($"{root}/gated.tyhpdef", """
            <?tyhpdef
            #[\Tyhp\Php(">=8.2")]
            function gated_package_always(): void;

            #[\Tyhp\Php(">=8.4")]
            function gated_package_84_only(): void;

            class GatedPackageAlways {}

            #[\Tyhp\Php(">=8.4")]
            class GatedPackage84Only {}

            declare(php=">=8.4") {
                function gated_package_file_84(): void;
            }
            """);
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) Bind(
        Project project,
        string userFile,
        string phpVersion)
    {
        using var compilationService = new CompilationService();
        var options = CompilationOptions.FromProject(project, o =>
        {
            o.PhpVersion = phpVersion;
            o.PhpVersionWasDefaulted = false;
            o.EnableAstCache = false;
            o.SkipChecking = true;
        });
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

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
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
