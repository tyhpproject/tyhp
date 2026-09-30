using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Story 21.1 / 21.10: binder treats tyhpdef <c>extern</c> names (types, functions, consts)
/// as bound, merges real-wins order-independently, and reports extends/implements-extern
/// plus overlay/partial interactions.
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefExternBinderTests
{
    [Fact]
    public void Bind_TyhpdefSignatureNamingExtern_DoesNotReportUnresolved()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\T;
            class \Consumer\C {
                public function f(\ExternNs\T $x): \ExternNs\T;
            }
            """);

        var (global, diagnostics) = Bind(builder);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedReturnType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
        var type = FindObjectInNamespace(global, "ExternNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeTrue();
    }

    [Fact]
    public void Bind_HandExternWithoutProvidedBy_IsBound()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/backers.tyhpdef", """
            <?tyhpdef
            extern class \Acme\Message;
            class \Consumer\UsesGelf {
                public function wrap(\Acme\Message $m): \Acme\Message;
            }
            """);

        var (global, diagnostics) = Bind(builder);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedReturnType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
        var type = FindObjectInNamespace(global, "Acme", "Message");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeTrue();
        type.ProvidedBy.Should().BeNull();
    }

    [Fact]
    public void Bind_TyhpdefClassExtendsExtern_Reports3026()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/extends.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhpdef/acme-tester
            extern class \ExternNs\T;
            class \Consumer\C extends \ExternNs\T {
            }
            """);

        var (_, diagnostics) = Bind(builder);
        var error = diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.BinderExternExtendsType).Subject;
        Convert.ToString(error.FormatParams[0]).Should().Contain("T");
        error.Labels.Should().NotBeEmpty();
        error.Labels[0].Message.Should().Be(Message.Localize("CLI_DiagnosticLabelDeclaredHere"));
        error.Help.Should().Be("tyhpdef/acme-tester");
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedExtendsType);
    }

    [Fact]
    public void Bind_TyhpdefClassImplementsExtern_Reports3027()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/implements.tyhpdef", """
            <?tyhpdef
            extern interface \ExternNs\T;
            class \Consumer\C implements \ExternNs\T {
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderExternImplementsType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedImplementsType);
    }

    [Fact]
    public void Bind_TyhpFileExtendsExtern_Reports3026()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\T;
            """);
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            class UserChild extends \ExternNs\T {
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderExternExtendsType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedExtendsType);
    }

    [Fact]
    public void Bind_TyhpFileMentionsRealClassWhoseSignatureUsesExtern_StillBinds()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/handler.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\T;
            class \Consumer\Handler {
                public function __construct(\ExternNs\T $x);
            }
            """);
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function demo(\Consumer\Handler $h): void {}
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedReturnType);
    }

    [Fact]
    public void Merge_ExternThenReal_TwoIncludePackages_RealWins()
    {
        using var builder = TwoPackageFixture(
            externPackage: "pkg-a",
            realPackage: "pkg-b");

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var type = FindObjectInNamespace(global, "MergeNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeFalse();
        type.Members.Should().ContainKey("realMethod");
    }

    [Fact]
    public void Merge_RealThenExtern_TwoIncludePackages_RealWins()
    {
        using var builder = TwoPackageFixture(
            externPackage: "pkg-b",
            realPackage: "pkg-a");

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var type = FindObjectInNamespace(global, "MergeNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeFalse();
        type.Members.Should().ContainKey("realMethod");
    }

    [Fact]
    public void Merge_TwoRealsSamePackage_Reports8002()
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
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateFqnAcrossPackages);
    }

    [Fact]
    public void Merge_TwoRealsTwoPackages_Reports8025()
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
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefDuplicateFqnAcrossPackages);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
    }

    [Fact]
    public void Merge_ExternClassVsInterface_Reports8029()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            extern class \KindNs\T;
            """);
        WritePackage(builder, "pkg-b", """
            <?tyhpdef
            interface \KindNs\T {
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (_, diagnostics) = Bind(builder);
        var error = diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.TyhpdefExternKindMismatch).Subject;
        error.FormatParams.Should().HaveCount(3);
        error.FormatParams[1].ToString().Should().Be("extern class");
        error.FormatParams[2].ToString().Should().Be("interface");
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateFqnAcrossPackages);
    }

    [Fact]
    public void Bind_BareExtern_DoesNotReportUnresolved()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/backers.tyhpdef", """
            <?tyhpdef
            extern \Acme\Message;
            class \Consumer\UsesGelf {
                public function wrap(\Acme\Message $m): \Acme\Message;
            }
            """);

        var (global, diagnostics) = Bind(builder);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedReturnType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
        var type = FindObjectInNamespace(global, "Acme", "Message");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeTrue();
        type.IsExternKindUnspecified.Should().BeTrue();
        type.ObjectKind.Should().Be(PhpTypeDeclType.Unspecified);
    }

    [Fact]
    public void Merge_BareExternThenRealInterface_RealWins()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            extern \KindNs\T;
            """);
        WritePackage(builder, "pkg-b", """
            <?tyhpdef
            interface \KindNs\T {
                public function f(): void;
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var type = FindObjectInNamespace(global, "KindNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeFalse();
        type.ObjectKind.Should().Be(PhpTypeDeclType.Interface);
        type.Members.Should().ContainKey("f");
    }

    [Fact]
    public void Merge_RealEnumThenBareExtern_KeepsEnum()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            namespace KindNs {
                enum T {
                    A;
                }
            }
            """);
        WritePackage(builder, "pkg-b", """
            <?tyhpdef
            extern \KindNs\T;
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var type = FindObjectInNamespace(global, "KindNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeFalse();
        type.ObjectKind.Should().Be(PhpTypeDeclType.Enum);
    }

    [Fact]
    public void Merge_BareExternThenExternClass_BecomesClass()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/a_first.tyhpdef", """
            <?tyhpdef
            extern \E\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/z_second.tyhpdef", """
            <?tyhpdef
            extern class \E\T;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var type = FindObjectInNamespace(global, "E", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeTrue();
        type.IsExternKindUnspecified.Should().BeFalse();
        type.ObjectKind.Should().Be(PhpTypeDeclType.Class);
    }

    [Fact]
    public void Merge_ExternClassThenBareExtern_KeepsClass()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/a_first.tyhpdef", """
            <?tyhpdef
            extern class \E\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/z_second.tyhpdef", """
            <?tyhpdef
            extern \E\T;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var type = FindObjectInNamespace(global, "E", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeTrue();
        type.ObjectKind.Should().Be(PhpTypeDeclType.Class);
    }

    [Fact]
    public void Merge_TwoBareExterns_KeepsFirstProvidedBy()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/a_first.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhp/first
            extern \E\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/z_second.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhp/second
            extern \E\T;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var type = FindObjectInNamespace(global, "E", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeTrue();
        type.IsExternKindUnspecified.Should().BeTrue();
        type.ProvidedBy.Should().Be("tyhp/first");
    }

    [Fact]
    public void Overlay_FullInterfaceOntoBareExtern_Upgrades()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern \ExternNs\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/real_t.tyhpdef", """
            <?tyhpdef
            interface \ExternNs\T {
                public function f(): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayIncompatibleReplace);
        var type = FindObjectInNamespace(global, "ExternNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeFalse();
        type.ObjectKind.Should().Be(PhpTypeDeclType.Interface);
        type.Members.Should().ContainKey("f");
    }

    [Fact]
    public void Merge_TwoExterns_KeepsFirstProvidedBy()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/a_first.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhp/first
            extern class \E\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/z_second.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhp/second
            extern class \E\T;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var type = FindObjectInNamespace(global, "E", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeTrue();
        type.ProvidedBy.Should().Be("tyhp/first");
    }

    [Fact]
    public void Overlay_OmitRemovesExtern()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/omit_t.tyhpdef", """
            <?tyhpdef
            omit class \ExternNs\T;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOmitOutsideOverlay);
        FindObjectInNamespace(global, "ExternNs", "T").Should().BeNull();
    }

    [Fact]
    public void Overlay_PartialOntoExtern_Reports8030AndSkips()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/partial_t.tyhpdef", """
            <?tyhpdef
            partial class \ExternNs\T {
                public function f(): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPartialOnExtern);
        var type = FindObjectInNamespace(global, "ExternNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeTrue();
        type.Members.Should().NotContainKey("f");
    }

    [Fact]
    public void Include_PartialOntoExtern_Reports8030AndSkips()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/a_extern.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/b_partial.tyhpdef", """
            <?tyhpdef
            partial class \ExternNs\T {
                public function f(): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPartialOnExtern);
        var type = FindObjectInNamespace(global, "ExternNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeTrue();
        type.Members.Should().NotContainKey("f");
    }

    [Fact]
    public void Overlay_FullClassOntoExtern_UpgradesAndClearsIsExtern()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/real_t.tyhpdef", """
            <?tyhpdef
            class \ExternNs\T {
                public function f(): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefPartialOnExtern);
        var type = FindObjectInNamespace(global, "ExternNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeFalse();
        type.Members.Should().ContainKey("f");
    }

    [Fact]
    public void Overlay_FullClassOntoExtern_MatchingStamp_DoesNotReport8021()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/real_t.tyhpdef", """
            <?tyhpdef
            // @overlay-against: class T
            class \ExternNs\T {
                public function f(): void;
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        var type = FindObjectInNamespace(global, "ExternNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeFalse();
        type.Members.Should().ContainKey("f");
    }

    [Fact]
    public void Overlay_FullClassOntoExtern_StaleStamp_StillReports8021()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\T;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/real_t.tyhpdef", """
            <?tyhpdef
            // @overlay-against: interface T
            class \ExternNs\T {
                public function f(): void;
            }
            """);

        var (_, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
    }

    [Fact]
    public void Overlay_ExternOntoReal_IsNoOp()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/real.tyhpdef", """
            <?tyhpdef
            class \ExternNs\T {
                public function f(): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/extern_t.tyhpdef", """
            <?tyhpdef
            extern class \ExternNs\T;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var type = FindObjectInNamespace(global, "ExternNs", "T");
        type.Should().NotBeNull();
        type!.IsExtern.Should().BeFalse();
        type.Members.Should().ContainKey("f");
    }

    [Fact]
    public void Overlay_AddedExternWithStamp_DoesNotReport8021()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/real.tyhpdef", """
            <?tyhpdef
            class \ExternNs\T {
                public function f(): void;
            }
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/extra_extern.tyhpdef", """
            <?tyhpdef
            // @overlay-against: class Extra
            extern class \ExternNs\Extra;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOverlayStampMismatch);
        var extra = FindObjectInNamespace(global, "ExternNs", "Extra");
        extra.Should().NotBeNull();
        extra!.IsExtern.Should().BeTrue();
    }

    [Fact]
    public void Bind_ExternFunction_OccupiesAndIsBound()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhpdef/acme-math
            extern function \widget_add;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
        var function = FindFunction(global, "widget_add");
        function.Should().NotBeNull();
        function!.IsExtern.Should().BeTrue();
        function.ProvidedBy.Should().Be("tyhpdef/acme-math");
        function.Parameters.Should().BeEmpty();
        function.ReturnType.Should().BeNull();
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedReturnType);
    }

    [Fact]
    public void Bind_ExternConst_OccupiesAndIsBound()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhpdef/acme-int
            extern const \WIDGET_ROUND_PLUSINF;
            """);

        var (global, diagnostics) = Bind(builder);
        var constant = FindConstant(global, "WIDGET_ROUND_PLUSINF");
        constant.Should().NotBeNull();
        constant!.IsExtern.Should().BeTrue();
        constant.ProvidedBy.Should().Be("tyhpdef/acme-int");
        constant.DeclaredType.Should().BeNull();
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
    }

    [Fact]
    public void Bind_TyhpdefSignatureNamingExternConst_DoesNotReportUnresolved()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern const \WIDGET_ROUND_PLUSINF;
            function \round_mode(int $mode = \WIDGET_ROUND_PLUSINF): int;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
        FindConstant(global, "WIDGET_ROUND_PLUSINF").Should().NotBeNull();
        FindFunction(global, "round_mode").Should().NotBeNull();
    }

    [Fact]
    public void Bind_TyhpdefExtensionMappingNamingExternFunction_IsBound()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern function \widget_add;
            extension Bc {
                fn add(string $a, string $b): string => \widget_add($a, $b);
            }
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
        var function = FindFunction(global, "widget_add");
        function.Should().NotBeNull();
        function!.IsExtern.Should().BeTrue();
    }

    [Fact]
    public void Merge_ExternFunctionThenRealFunction_RealWins()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            extern function \MergeNs\widget_add;
            """);
        WritePackage(builder, "pkg-b", """
            <?tyhpdef
            function \MergeNs\widget_add(string $a, string $b): string;
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var function = FindFunction(global, "widget_add");
        function.Should().NotBeNull();
        function!.IsExtern.Should().BeFalse();
        function.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void Merge_RealFunctionThenExternFunction_RealWins()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            function \MergeNs\widget_add(string $a, string $b): string;
            """);
        WritePackage(builder, "pkg-b", """
            <?tyhpdef
            extern function \MergeNs\widget_add;
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var function = FindFunction(global, "widget_add");
        function.Should().NotBeNull();
        function!.IsExtern.Should().BeFalse();
        function.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void Merge_ExternConstThenRealConst_RealWins()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            extern const \MergeNs\FOO;
            """);
        WritePackage(builder, "pkg-b", """
            <?tyhpdef
            const int \MergeNs\FOO ?? 1;
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var constant = FindConstant(global, "FOO");
        constant.Should().NotBeNull();
        constant!.IsExtern.Should().BeFalse();
        constant.DeclaredType.Should().NotBeNull();
    }

    [Fact]
    public void Merge_RealConstThenExternConst_RealWins()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            const int \MergeNs\FOO ?? 1;
            """);
        WritePackage(builder, "pkg-b", """
            <?tyhpdef
            extern const \MergeNs\FOO;
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var constant = FindConstant(global, "FOO");
        constant.Should().NotBeNull();
        constant!.IsExtern.Should().BeFalse();
        constant.DeclaredType.Should().NotBeNull();
    }

    [Fact]
    public void Merge_TwoExternFunctions_KeepsFirstProvidedBy()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/a_first.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhp/first
            extern function \E\widget_add;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/z_second.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhp/second
            extern function \E\widget_add;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var function = FindFunction(global, "widget_add");
        function.Should().NotBeNull();
        function!.IsExtern.Should().BeTrue();
        function.ProvidedBy.Should().Be("tyhp/first");
    }

    [Fact]
    public void Merge_TwoExternConsts_KeepsFirstProvidedBy()
    {
        using var builder = SinglePackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/a_first.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhp/first
            extern const \E\FOO;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/z_second.tyhpdef", """
            <?tyhpdef
            // @provided-by: tyhp/second
            extern const \E\FOO;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var constant = FindConstant(global, "FOO");
        constant.Should().NotBeNull();
        constant!.IsExtern.Should().BeTrue();
        constant.ProvidedBy.Should().Be("tyhp/first");
    }

    [Fact]
    public void Merge_ExternFunctionVsInterface_Coexists()
    {
        using var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, "pkg-a", """
            <?tyhpdef
            extern function \KindNs\T;
            """);
        WritePackage(builder, "pkg-b", """
            <?tyhpdef
            interface \KindNs\T {
                public function f(): void;
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg-a/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", "./pkg-b/composer.json");

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefExternKindMismatch);
        FindFunction(global, "T").Should().NotBeNull();
        var type = FindObjectInNamespace(global, "KindNs", "T");
        type.Should().NotBeNull();
        type!.ObjectKind.Should().Be(PhpTypeDeclType.Interface);
    }

    [Fact]
    public void Overlay_PartialFunctionOntoExternFunction_Reports8030AndSkips()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern function \ExternNs\widget_add;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/partial_fn.tyhpdef", """
            <?tyhpdef
            partial function \ExternNs\widget_add;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefPartialOnExtern);
        var function = FindFunction(global, "widget_add");
        function.Should().NotBeNull();
        function!.IsExtern.Should().BeTrue();
    }

    [Fact]
    public void Overlay_OmitRemovesExternFunction()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern function \ExternNs\widget_add;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/omit_fn.tyhpdef", """
            <?tyhpdef
            omit function \ExternNs\widget_add();
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOmitOutsideOverlay);
        FindFunction(global, "widget_add").Should().BeNull();
    }

    [Fact]
    public void Overlay_RealFunctionOntoExternFunction_RealWins()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern function \ExternNs\widget_add;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/real_fn.tyhpdef", """
            <?tyhpdef
            function \ExternNs\widget_add(string $a, string $b): string;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var function = FindFunction(global, "widget_add");
        function.Should().NotBeNull();
        function!.IsExtern.Should().BeFalse();
        function.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void Overlay_OmitRemovesExternConst()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern const \ExternNs\FOO;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/omit_const.tyhpdef", """
            <?tyhpdef
            omit const int \ExternNs\FOO;
            """);

        var (global, diagnostics) = Bind(builder);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefOmitOutsideOverlay);
        FindConstant(global, "FOO").Should().BeNull();
    }

    [Fact]
    public void Overlay_RealConstOntoExternConst_RealWins()
    {
        using var builder = OverlayPackageFixture();
        builder.WithTyhpFile("pkg/_tyhpdef/externs.tyhpdef", """
            <?tyhpdef
            extern const \ExternNs\FOO;
            """);
        builder.WithTyhpFile("pkg/_tyhpdef/overlays/real_const.tyhpdef", """
            <?tyhpdef
            const int \ExternNs\FOO ?? 1;
            """);

        var (global, diagnostics) = Bind(builder);
        AssertNoDuplicateDiagnostics(diagnostics);
        var constant = FindConstant(global, "FOO");
        constant.Should().NotBeNull();
        constant!.IsExtern.Should().BeFalse();
        constant.DeclaredType.Should().NotBeNull();
    }

    private static TestProjectBuilder SinglePackageFixture()
    {
        var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg/composer.json");
        builder.WithTyhpdefPackageComposer("pkg", """
            {
                "include": ["./_tyhpdef/*.tyhpdef"]
            }
            """);
        return builder;
    }

    private static TestProjectBuilder OverlayPackageFixture()
    {
        var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        builder.WithConfigValue("tyhpdefInclude:0", "./pkg/composer.json");
        builder.WithTyhpdefPackageComposer("pkg", """
            {
                "include": ["./_tyhpdef/*.tyhpdef"],
                "overlay": ["./_tyhpdef/overlays/*.tyhpdef"]
            }
            """);
        return builder;
    }

    private static TestProjectBuilder TwoPackageFixture(string externPackage, string realPackage)
    {
        var builder = new TestProjectBuilder();
        WriteUserProject(builder);
        WritePackage(builder, externPackage, """
            <?tyhpdef
            extern class \MergeNs\T;
            """);
        WritePackage(builder, realPackage, """
            <?tyhpdef
            class \MergeNs\T {
                public function realMethod(): void;
            }
            """);
        builder.WithConfigValue("tyhpdefInclude:0", $"./{externPackage}/composer.json");
        builder.WithConfigValue("tyhpdefInclude:1", $"./{realPackage}/composer.json");
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
        var project = builder.BuildProject();
        var userFile = Path.Combine(project.GetProjectPath(), "src", "app.tyhp");
        using var compilationService = new CompilationService();
        var options = CompilationOptions.FromProject(project, o =>
        {
            o.EnableAstCache = false;
            o.SkipChecking = true;
        });
        var result = compilationService.ParseFiles([userFile], options);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static void AssertNoDuplicateDiagnostics(DiagnosticBag diagnostics)
    {
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateFqnAcrossPackages);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefExternKindMismatch);
    }

    private static ObjectDeclarationSymbol? FindObjectInNamespace(
        GlobalScope global,
        string namespaceName,
        string typeName)
    {
        var nsScope = global.FindNamespaceScope(namespaceName);
        if (nsScope == null)
        {
            return null;
        }

        ObjectDeclarationSymbol? found = null;
        Walk(nsScope, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && !obj.IsExtension
                && string.Equals(obj.Name, typeName, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static FunctionDeclarationSymbol? FindFunction(GlobalScope global, string functionName)
    {
        FunctionDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is FunctionDeclarationSymbol function
                && string.Equals(function.Name, functionName, StringComparison.OrdinalIgnoreCase))
            {
                found = function;
            }
        });
        return found;
    }

    private static ConstantSymbol? FindConstant(GlobalScope global, string constantName)
    {
        ConstantSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ConstantSymbol constant
                && string.Equals(constant.Name, constantName, StringComparison.Ordinal))
            {
                found = constant;
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
