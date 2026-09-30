using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// FOUND_BUGS #51: typed foreach is compatibility (not narrowing). Untyped foreach
/// still infers key/value from the iterable.
/// </summary>
[Trait("Category", "Checker")]
public class TypedForeachCheckTests
{
    [Fact]
    public void TypedForeach_MatchingKeyAndValue_Accepted()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(array<int, string> $xs): void {
                foreach ($xs as int $k => string $v) {
                    int $ik = $k;
                    string $sv = $v;
                }
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void TypedForeach_ValueOnly_Accepted()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(array<int, string> $xs): void {
                foreach ($xs as string $v) {
                    string $sv = $v;
                }
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void TypedForeach_KeyOnly_Accepted()
    {
        // Key typed, value untyped (still inferred from the iterable, per #51).
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(array<int, string> $xs): void {
                foreach ($xs as int $k => $v) {
                    int $ik = $k;
                    string $sv = $v;
                }
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void TypedForeach_KeyOnlyIncompatible_Reports4196()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(array<int, string> $xs): void {
                foreach ($xs as string $k => $v) {}
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerForeachBindingTypeMismatch,
            Describe(result));
    }

    [Fact]
    public void TypedForeach_IncompatibleKey_Reports4196()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(array<int, string> $xs): void {
                foreach ($xs as string $k => string $v) {}
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerForeachBindingTypeMismatch,
            Describe(result));
    }

    [Fact]
    public void TypedForeach_MixedDoesNotNarrow()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(array<int, string> $xs): void {
                foreach ($xs as mixed $k => mixed $v) {
                    int $ik = $k;
                    string $sv = $v;
                }
            }
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "mixed annotations must not narrow to int/string: " + Describe(result));
        UserErrors(result).Should().NotContain(
            e => e.Code == MessageCode.CheckerForeachBindingTypeMismatch,
            "int/string are assignable to mixed: " + Describe(result));
    }

    [Fact]
    public void UntypedForeach_StillInfersFromArray()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function demo(array<int, string> $xs): void {
                foreach ($xs as $k => $v) {
                    int $ik = $k;
                    string $sv = $v;
                }
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(CompilationResult result) =>
        result.Diagnostics.Errors
            .Where(e => (e.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
