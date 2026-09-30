using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.12 A — <c>#[\Tyhp\EraseGeneric]</c> targets and author-written
/// <c>#[\Tyhp\GenericRuntime]</c> warning.
/// </summary>
[Trait("Category", "Checker")]
public class EraseGenericRuleTests
{
    [Fact]
    public void Check_EraseGenericOnMethod_Reports4345()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder {
                #[\Tyhp\EraseGeneric]
                public function identity<T>(T $value): T {
                    return $value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerEraseGenericInvalidTarget);
    }

    [Fact]
    public void Check_EraseGenericOnFunction_Reports4345()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\EraseGeneric]
            function identity<T>(T $value): T {
                return $value;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerEraseGenericInvalidTarget);
    }

    [Fact]
    public void Check_AuthorGenericRuntime_Reports4346()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\GenericRuntime(erased: true, layouts: [1])]
            class Box<T> {
                public function identity(T $value): T {
                    return $value;
                }
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerGenericRuntimeAuthorWritten);
    }

    [Fact]
    public void Check_PromotedGenericProperty_MarksTracking()
    {
        var (diagnostics, result) = CompileAndCheckResult("""
            <?tyhp
            class Box<T> {
                public function __construct(public T $v): void {}
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
        result.RequiresRuntimeGenericTracking.Should().NotBeNull();
        result.RequiresRuntimeGenericTracking!.Should().Contain(s =>
            string.Equals(s.Name, "Box", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Check_ClassLevelEraseGeneric_DoesNotMarkPropertyTracking()
    {
        var (diagnostics, result) = CompileAndCheckResult("""
            <?tyhp
            #[\Tyhp\EraseGeneric]
            class Box<T> {
                public T $value;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
        result.RequiresRuntimeGenericTracking.Should().NotBeNull();
        result.RequiresRuntimeGenericTracking!.Should().NotContain(s =>
            string.Equals(s.Name, "Box", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Check_TypeofWithClassLevelEraseGeneric_StillMarksTracking()
    {
        var (diagnostics, result) = CompileAndCheckResult("""
            <?tyhp
            #[\Tyhp\EraseGeneric]
            class Box<T> {
                public T $value;
                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
        result.RequiresRuntimeGenericTracking.Should().NotBeNull();
        result.RequiresRuntimeGenericTracking!.Should().Contain(s =>
            string.Equals(s.Name, "Box", StringComparison.OrdinalIgnoreCase));
    }

    private static DiagnosticBag CompileAndCheck(string content)
        => CompileAndCheckResult(content).Diagnostics;

    private static (DiagnosticBag Diagnostics, CompilationResult Result) CompileAndCheckResult(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "erase-generic.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            result.RequiresRuntimeGenericTracking = checker.RequiresRuntimeGenericTracking;
            result.RequiresGenericVariant = checker.RequiresGenericVariant;
            return (result.Diagnostics, result);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
