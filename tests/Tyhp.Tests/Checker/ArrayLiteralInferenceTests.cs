using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// CHECKER_GAPS P1 #13: untyped locals initialized from array literals infer <c>array&lt;…&gt;</c>.
/// </summary>
[Trait("Category", "Checker")]
public class ArrayLiteralInferenceTests
{
    [Fact]
    public void Check_ListLiteral_InfersArrayOfInt_No4016()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $xs = [1, 2, 3];
                array<int> $ys = $xs;
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerVariableTypeRequired,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_MapLiteral_InfersArrayKeyValue()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $map = ['a' => 1, 'b' => 2];
                array<string, int> $typed = $map;
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerVariableTypeRequired
                || d.Code == MessageCode.CheckerTypeMismatch,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_MixedValueLiteral_InfersUnion()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $xs = [1, 'a'];
                array<int|string> $typed = $xs;
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerVariableTypeRequired
                || d.Code == MessageCode.CheckerTypeMismatch,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_EmptyLiteral_InfersArrayNever_AssignableToTyped()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $xs = [];
                array<int> $typed = $xs;
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerVariableTypeRequired
                || d.Code == MessageCode.CheckerTypeMismatch,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_EmptyLiteral_AssignableToStringKeyedArray()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}
            function demo(): void {
                array<string, Foo> $x = [];
                array<int> $y = [];
                $xs = [];
                array<string, int> $typed = $xs;
            }

            class Host {
                private static array<string, Foo> $cache = [];
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_ListAssignedToWrongElementType_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                array<string> $bad = [1, 2, 3];
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
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
