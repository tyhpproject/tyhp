using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A <c>while</c> that exits because its condition became false narrows by that
/// negated condition when the body has no <c>break</c>.
/// </summary>
[Trait("Category", "Checker")]
public class WhileExitNarrowingTests
{
    [Fact]
    public void WhileNotIsInt_AssignsInt_ExitIsInt()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(mixed $m): int {
                while (!\is_int($m)) {
                    $m = 1;
                }
                return $m;
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void WhileWithBreak_DoesNotNarrowExit()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(mixed $m): int {
                while (!\is_int($m)) {
                    break;
                }
                return $m;
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(result));
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(CompilationResult result) =>
        result.Diagnostics.Errors
            .Where(e => (e.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{(int)e.Code}: {e.Message}"));
}
