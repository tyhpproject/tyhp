using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.7 introduced <c>PhpArrayPairAst.CreateSkippedSlot</c> so <c>[, $b]</c> destructure
/// targets keep an empty slot for position 1 instead of being erased at parse time (see
/// <c>ArrayAccessDestructureSupport</c>). Because <c>[…]</c> / <c>array(…)</c> share the same
/// <c>PhpArrayPairListAst</c> node for both destructure targets and ordinary array/tuple/bag
/// <em>values</em>, an empty trailing <c>possibleArrayPair</c> — the AST shape of a plain
/// trailing comma, e.g. <c>['Ada', 36,]</c> — now also produces a skip-slot pair on literals
/// that are never destructured. These tests guard the non-destructure consumers that must keep
/// ignoring skip slots via <c>PhpArrayPairListAst.GetAllExcludingSkippedSlots()</c>.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Story21.7")]
public class SkippedSlotArrayLiteralTests
{
    [Fact]
    public void Check_CallUserFuncArray_PositionalBag_TrailingComma_StillMatches()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func_array(greet(...), ['Ada', 36,]);
            }
            """);

        errors.Should().BeEmpty(
            "a trailing comma must not turn a positional call_user_func_array bag into an "
            + "unmatched literal: " + Describe(errors));
    }

    [Fact]
    public void Check_CallUserFuncArray_NamedBag_TrailingComma_StillMatches()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func_array(greet(...), ['name' => 'Ada', 'age' => 36,]);
            }
            """);

        errors.Should().BeEmpty(
            "a trailing comma must not turn a named call_user_func_array bag into an "
            + "unmatched literal: " + Describe(errors));
    }

    [Fact]
    public void Check_ArrayCallableLiteral_TrailingComma_StillRecognized()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Widget {
                public function handle(): void {}
            }
            function register(callable $cb): void {}
            function demo(Widget $w): void {
                register([$w, 'handle',]);
            }
            """);

        errors.Should().BeEmpty(
            "a trailing comma must not stop `[$receiver, 'method',]` from being recognized as "
            + "an array-callable literal: " + Describe(errors));
    }

    [Fact]
    public void Check_ConstantArrayLiteral_TrailingComma_StaysConstant()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            #[\Attribute]
            class Attr {
                public function __construct(array $values) {}
            }
            #[Attr([1, 2,])]
            function demo(): void {}
            """);

        errors.Should().BeEmpty(
            "a trailing comma must not make an otherwise-constant array literal non-constant: "
            + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

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
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull(
                "bind should succeed: " + Describe(result.Diagnostics.Errors.ToList()));
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);

            return result.Diagnostics.Errors
                .Where(e => e.FileName is not null
                    && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
