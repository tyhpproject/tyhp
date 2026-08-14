using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// PHP 8.5 <c>Attribute::TARGET_CONSTANT</c> for top-level compile-time <c>const</c>,
/// and distinction from <c>TARGET_CLASS_CONSTANT</c>.
/// </summary>
[Trait("Category", "Checker")]
public class ConstAttributeTargetCheckTests
{
    [Fact]
    public void Check_TargetConstantOnTopLevelConst_DoesNotReport4127()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_CONSTANT)]
            class ConstOnly {}

            #[ConstOnly]
            const EXAMPLE = 1;
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_DefaultAttributeOnTopLevelConst_DoesNotReport4127()
    {
        // Bare #[Attribute] defaults to TARGET_ALL, which includes TARGET_CONSTANT (PHP 8.5).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute]
            class AnyTarget {}

            #[AnyTarget]
            const EXAMPLE = 1;
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_TargetMethodOnTopLevelConst_Reports4127()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_METHOD)]
            class MethodOnly {}

            #[MethodOnly]
            const EXAMPLE = 1;
            """);

        var mismatches = diagnostics.Errors
            .Where(d => d.Code == MessageCode.CheckerAttributeTargetMismatch)
            .ToList();

        mismatches.Should().ContainSingle();
        mismatches[0].Message.Should().Contain("constant").And.NotContain("Ast");
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_TargetClassConstantOnTopLevelConst_Reports4127()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_CLASS_CONSTANT)]
            class ClassConstOnly {}

            #[ClassConstOnly]
            const EXAMPLE = 1;
            """);

        var mismatches = diagnostics.Errors
            .Where(d => d.Code == MessageCode.CheckerAttributeTargetMismatch)
            .ToList();

        mismatches.Should().ContainSingle();
        mismatches[0].Message.Should().Contain("constant");
    }

    [Fact]
    public void Check_TargetConstantOnClassConstant_Reports4127()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_CONSTANT)]
            class ConstOnly {}

            class C {
                #[ConstOnly]
                public const int FOO = 1;
            }
            """);

        var mismatches = diagnostics.Errors
            .Where(d => d.Code == MessageCode.CheckerAttributeTargetMismatch)
            .ToList();

        mismatches.Should().ContainSingle();
        mismatches[0].Message.Should().Contain("class constant");
    }

    [Fact]
    public void Check_TargetClassConstantOnClassConstant_DoesNotReport4127()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Attribute(\Attribute::TARGET_CLASS_CONSTANT)]
            class ClassConstOnly {}

            class C {
                #[ClassConstOnly]
                public const int FOO = 1;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerAttributeTargetMismatch
            || d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_NonAttributeClassOnTopLevelConst_Reports4126()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Plain {}

            #[Plain]
            const EXAMPLE = 1;
            """);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerNotAnAttributeClass).Should().Be(1);
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
                PhpVersion = "8.4",
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
