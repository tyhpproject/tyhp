using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story20")]
public class Story20Phase6BinderCheckerTests
{
    [Fact]
    public void EmptyExtension_Reports4172()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension EmptyOps {
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerEmptyExtension);
    }

    [Fact]
    public void ReservedExtensionBackerSuffix_Reports4171()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget__tyhpExtensionBacker {}
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerReservedExtensionBackerSuffix);
    }

    [Fact]
    public void OperatorAs_Reports4170()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOps extends string {
                function length(): int {
                    return 0;
                }
            }
            use extension StringOps {
                operator * as times;
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerExtensionOperatorRenameForbidden);
    }

    [Fact]
    public void NamedMethodHide_UnknownMember_Reports4172Error()
    {
        // Regression guard: AdaptationMemberKey / NameText must read the trait-member text via
        // ValueString when Identifier is unset (defaults to "" on PhpNameAst, not null), or a
        // named-method `hide` silently no-ops instead of validating the member exists.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOps extends string {
                function length(): int {
                    return 0;
                }
            }
            use extension StringOps {
                StringOps::doesNotExist hide;
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerExtensionHideUnknownMember);
    }

    [Fact]
    public void NamedMethodHide_KnownMember_NoUnknownMemberError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOps extends string {
                function length(): int {
                    return 0;
                }
            }
            use extension StringOps {
                StringOps::length hide;
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionHideUnknownMember);
    }

    [Fact]
    public void RedundantLocalUse_Warns4169()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "globals.tyhpdef");
        File.WriteAllText(tyhpdefPath, """
            <?tyhpdef
            global use App\Models\User;
            class \App\Models\User {
                public function id(): int;
            }
            """);

        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, """
            <?tyhp
            use App\Models\User;
            class Demo {
                public function take(User $u): int {
                    return $u->id();
                }
            }
            """);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: "8.2",
                skipChecking: true,
                tyhpdefIncludePaths: [tyhpdefPath]);
            var result = compilationService.ParseFiles([filePath], options);
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);

            result.Diagnostics.ToList().Should().Contain(d => d.Code == MessageCode.CheckerRedundantGlobalImport);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void RedundantLocalUse_GroupExtensionUse_WarnsOnlyForUnmutatedName()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "globals.tyhpdef");
        File.WriteAllText(tyhpdefPath, """
            <?tyhpdef
            namespace Foo;
            extension StringOps extends string {
                fn toUpper(): string => $this;
            }
            extension RepeatOps extends string {
                fn repeat(): string => $this;
            }
            global use extension \Foo\StringOps;
            global use extension \Foo\RepeatOps;
            """);

        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, """
            <?tyhp
            use extension StringOps, RepeatOps {
                StringOps::toUpper hide;
            };
            class Demo {}
            """);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: "8.2",
                skipChecking: true,
                tyhpdefIncludePaths: [tyhpdefPath]);
            var result = compilationService.ParseFiles([filePath], options);
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);

            var redundant = result.Diagnostics.ToList()
                .Where(d => d.Code == MessageCode.CheckerRedundantGlobalImport)
                .ToList();

            redundant.Should().ContainSingle(d => d.Message.Contains("RepeatOps", StringComparison.Ordinal));
            redundant.Should().NotContain(d => d.Message.Contains("StringOps", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void PartialMissingTarget_Reports8014()
    {
        var diagnostics = CompileTyhpdefs(
            ("missing.tyhpdef", """
                <?tyhpdef
                partial class \Lib\DoesNotExist {
                    public function extra(): void;
                }
                """));

        diagnostics.Should().Contain(d => d.Code == MessageCode.TyhpdefPartialTargetNotFound);
    }

    [Fact]
    public void PartialOrderIndependent_AdditiveBeforeBase_NoError()
    {
        var diagnostics = CompileTyhpdefs(
            ("partial.tyhpdef", """
                <?tyhpdef
                partial class \Lib\Box {
                    public function extra(): int;
                }
                """),
            ("base.tyhpdef", """
                <?tyhpdef
                class \Lib\Box {
                    public function get(): int;
                }
                """));

        diagnostics.Should().NotContain(d => d.Code == MessageCode.TyhpdefPartialTargetNotFound);
    }

    [Fact]
    public void PartialDuplicateMember_Reports8002()
    {
        var diagnostics = CompileTyhpdefs(
            ("base.tyhpdef", """
                <?tyhpdef
                class \Lib\Box {
                    public function get(): int;
                }
                """),
            ("partial.tyhpdef", """
                <?tyhpdef
                partial class \Lib\Box {
                    public function get(): int;
                }
                """));

        diagnostics.Should().Contain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
    }

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull();
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics.ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static IReadOnlyList<IDiagnostic> CompileTyhpdefs(params (string Name, string Content)[] files)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var paths = new List<string>();
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(tempDir, name);
            File.WriteAllText(path, content);
            paths.Add(path);
        }

        var stubPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".tyhp");
        File.WriteAllText(stubPath, "<?tyhp\nclass _Story20Stub {}\n");

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: "8.2",
                skipChecking: true,
                tyhpdefIncludePaths: paths);
            var result = compilationService.ParseFiles([stubPath], options);
            return result.Diagnostics.ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
