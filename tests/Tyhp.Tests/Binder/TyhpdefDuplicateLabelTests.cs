using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Scopes;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
[Trait("Category", "Diagnostics")]
public class TyhpdefDuplicateLabelTests
{
    private static string DeclaredHere => Message.Localize("CLI_DiagnosticLabelDeclaredHere");

    [Fact]
    public void IncludeLayerDuplicateClass_LabelsFirstTyhpdefDeclaration()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/a_first.tyhpdef", """
            <?tyhpdef
            class \DupNs\T {
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/b_second.tyhpdef", """
            <?tyhpdef
            class \DupNs\T {
            }
            """);

        var (_, diagnostics) = Bind(builder);
        var error = diagnostics.Errors
            .Should()
            .ContainSingle(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration)
            .Subject;
        AssertDeclaredHere(error);
        Path.GetFileName(error.FileName).Should().Be("b_second.tyhpdef");
        Path.GetFileName(error.Labels[0].Span.FileName).Should().Be("a_first.tyhpdef");
        error.Labels[0].Span.Line.Should().BeGreaterThanOrEqualTo(1);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateFqnAcrossPackages);
    }

    [Fact]
    public void CrossPackageDuplicateFqn_LabelsFirstPackageDeclaration()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            class \DupNs\T {
            }
            """);
        WritePackage(builder, "pkg-b", """
            <?tyhpdef
            class \DupNs\T {
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (_, diagnostics) = Bind(builder);
        var error = diagnostics.Errors
            .Should()
            .ContainSingle(d => d.Code == MessageCode.TyhpdefDuplicateFqnAcrossPackages)
            .Subject;
        AssertDeclaredHere(error);
        error.FileName.Should().Contain("pkg-b");
        error.Labels[0].Span.FileName.Should().Contain("pkg-a");
        error.Labels[0].Span.Line.Should().BeGreaterThanOrEqualTo(1);
        Convert.ToString(error.FormatParams[0]).Should().Contain("DupNs");
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
    }

    [Fact]
    public void OverlayLastWinsReplace_DoesNotReport8002()
    {
        using var builder = IsolatedCompilation.CreateOverlayProject(
            includeTyhpdef: """
                <?tyhpdef
                function overlay_target(): int;
                """,
            overlayTyhpdef: """
                <?tyhpdef
                function overlay_target(): string;
                """);

        var result = IsolatedCompilation.BindProject(builder);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateFqnAcrossPackages);
    }

    [Fact]
    public void OverlayOmit_RetractsCrossPackageDuplicateForOmittedName()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            class \DupNs\T {
            }
            class \DupNs\Keep {
            }
            """);
        WritePackageWithOverlay(builder, "pkg-b", """
            <?tyhpdef
            class \DupNs\T {
            }
            class \DupNs\Keep {
            }
            """, """
            <?tyhpdef
            omit class \DupNs\T;
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => IsCrossPackageDuplicate(d, @"\DupNs\T"));
        diagnostics.Errors.Should().Contain(d => IsCrossPackageDuplicate(d, @"\DupNs\Keep"));
        FindObject(global, "DupNs", "T").Should().BeNull();
        FindObject(global, "DupNs", "Keep").Should().NotBeNull();
    }

    [Fact]
    public void OverlayOmit_KeepsCrossPackageDuplicateWhenOmitMissesName()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            class \DupNs\T {
            }
            """);
        WritePackageWithOverlay(builder, "pkg-b", """
            <?tyhpdef
            class \DupNs\T {
            }
            class \DupNs\Other {
            }
            """, """
            <?tyhpdef
            omit class \DupNs\Other;
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => IsCrossPackageDuplicate(d, @"\DupNs\T"));
        diagnostics.Errors.Should().NotContain(d => IsCrossPackageDuplicate(d, @"\DupNs\Other"));
        FindObject(global, "DupNs", "T").Should().NotBeNull();
        FindObject(global, "DupNs", "Other").Should().BeNull();
    }

    [Fact]
    public void OverlayOmit_KeepsOtherCodesAndSameFqnConstDuplicate()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            class \DupNs\T {
            }
            const int \DupNs\T;
            """);
        WritePackageWithOverlay(builder, "pkg-b", """
            <?tyhpdef
            class \DupNs\T {
            }
            const int \DupNs\T;
            """, """
            <?tyhpdef
            omit class \DupNs\T {
            }
            omit class \DupNs\Missing {
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (_, diagnostics) = Bind(builder);
        diagnostics.Errors.Where(d => IsCrossPackageDuplicate(d, @"\DupNs\T")).Should().ContainSingle();
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOmitMissingSymbol);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefParseError);
    }

    private static bool IsCrossPackageDuplicate(IDiagnostic diagnostic, string fullyQualifiedName)
    {
        return diagnostic.Code == MessageCode.TyhpdefDuplicateFqnAcrossPackages
            && diagnostic.FormatParams.Length > 0
            && string.Equals(
                diagnostic.FormatParams[0]?.ToString(),
                fullyQualifiedName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static Tyhp.TyhpLang.Binder.Symbols.ObjectDeclarationSymbol? FindObject(
        GlobalScope global,
        string namespaceName,
        string typeName)
    {
        var nsScope = global.FindNamespaceScope(namespaceName);
        if (nsScope == null)
        {
            return null;
        }

        Tyhp.TyhpLang.Binder.Symbols.ObjectDeclarationSymbol? found = null;
        Walk(nsScope, symbol =>
        {
            if (found == null
                && symbol is Tyhp.TyhpLang.Binder.Symbols.ObjectDeclarationSymbol obj
                && string.Equals(obj.Name, typeName, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
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

    private static void AssertDeclaredHere(IDiagnostic diagnostic)
    {
        diagnostic.Labels.Should().ContainSingle();
        diagnostic.Labels[0].Message.Should().Be(DeclaredHere);
        diagnostic.Message.Should().NotContain(diagnostic.Labels[0].Span.FileName + ":");
    }

    private static TestProjectBuilder SinglePackageFixture()
    {
        var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        builder.WithTyhpdefPackageComposer("pkg", """
            {
                "include": ["./_tyhpdef/*.tyhpdef"]
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg/composer.json");
        return builder;
    }

    private static void WritePackage(TestProjectBuilder builder, string root, string tyhpdef)
    {
        builder.WithTyhpdefPackageComposer(root, """
            {
                "include": ["./_tyhpdef/*.tyhpdef"]
            }
            """);
        builder.WithTyhpFile($"{root}/_tyhpdef/types.tyhpdef", tyhpdef);
    }

    private static void WritePackageWithOverlay(
        TestProjectBuilder builder,
        string root,
        string tyhpdef,
        string overlayTyhpdef)
    {
        builder.WithTyhpdefPackageComposer(root, """
            {
                "include": ["./_tyhpdef/*.tyhpdef"],
                "overlay": ["./_tyhpdef/overlays/*.tyhpdef"]
            }
            """);
        builder.WithTyhpFile($"{root}/_tyhpdef/types.tyhpdef", tyhpdef);
        builder.WithTyhpFile($"{root}/_tyhpdef/overlays/overlay.tyhpdef", overlayTyhpdef);
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

    private static (GlobalScope Global, DiagnosticBag Diagnostics) Bind(TestProjectBuilder builder)
    {
        var result = IsolatedCompilation.BindProject(builder);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }
}
