using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// TYHP4092 is reported for a repeated literal key inside <c>return</c>, <c>echo</c>,
/// and <c>if</c>, the same as for an assigned literal.
/// </summary>
[Trait("Category", "Checker")]
public class DuplicateArrayKeyPositionTests
{
    [Theory]
    [InlineData("""
        <?tyhp
        function f(): array<string, int> {
            return ['a' => 1, 'a' => 2];
        }
        """)]
    [InlineData("""
        <?tyhp
        function g(): void {
            echo ['a' => 1, 'a' => 2]['a'];
        }
        """)]
    [InlineData("""
        <?tyhp
        function h(): void {
            if (['a' => 1, 'a' => 2]['a'] > 0) {}
        }
        """)]
    public void DuplicateKey_InSuppressedStatement_Reports4092(string source)
    {
        var result = IsolatedCompilation.ParseSnippet(source);
        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerDuplicateArrayKey,
            Describe(result));
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(CompilationResult result) =>
        result.Diagnostics.Errors
            .Where(e => (e.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{(int)e.Code}: {e.Message}"));
}
