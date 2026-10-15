using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>\is_iterable</c> true-branch is assignable to <c>iterable&lt;mixed&gt;</c>.
/// </summary>
[Trait("Category", "Checker")]
public class IsIterableNarrowingTests
{
    private const string Guard = """
        <?tyhpdef
        function is_iterable(mixed $value): $value is array|\Traversable;
        """;

    [Fact]
    public void IsIterable_TrueBranch_AssignsToIterableMixed()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(mixed $m): iterable<mixed> {
                if (\is_iterable($m)) {
                    return $m;
                }
                return [];
            }
            """,
            Guard);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(CompilationResult result) =>
        result.Diagnostics.Errors
            .Where(e => (e.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{(int)e.Code}: {e.Message}"));
}
