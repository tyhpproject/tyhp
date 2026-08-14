using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class Phase4RuleTests
{
    [Fact]
    public void Check_GotoStatement_ReportsProhibited()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                goto label;
            label:
                return;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerGotoProhibited);
    }

    [Fact]
    public void Check_IfConditionNotBool_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                if (1) {
                    return;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerConditionNotBool);
    }

    [Fact]
    public void Check_BreakOutsideLoop_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                break;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerBreakOutsideLoop);
    }

    [Fact]
    public void Check_NewScalarType_ReportsCannotInstantiateNonClass()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                mixed $x = new string();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerCannotInstantiateNonClass);
    }

    [Fact]
    public void Check_DuplicateUnionType_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(int|int $value): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerDuplicateTypeInComposite);
    }

    [Fact]
    public void Check_BoolFalseUnion_ReportsRedundantType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(bool|false $value): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerRedundantTypeInUnion);
    }

    [Fact]
    public void Check_VoidParameterType_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(void $value): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVoidNotAllowedHere);
    }

    [Fact]
    public void Check_MissingReturnOnAllPaths_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): int {
                if (true) {
                    return 1;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    [Fact]
    public void Check_ReturnTypeMismatch_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): string {
                return 1;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_StaticClosureThis_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Demo {
                public static function run(): void {
                    mixed $unused = static fn() => $this;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerStaticClosureThis);
    }

    [Fact]
    public void Check_ThisInStaticMethod_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Demo {
                public static function run(): void {
                    $this;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerThisInStaticContext);
    }

    [Fact]
    public void Check_ExtensionExtendsThis_DoesNotReportThisInStaticContext()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringExtensions {
                function identity(extends string $this): string {
                    return $this;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerThisInStaticContext);
    }

    [Fact]
    public void Check_VoidFunction_DoesNotRequireExplicitReturn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                mixed $x = 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
