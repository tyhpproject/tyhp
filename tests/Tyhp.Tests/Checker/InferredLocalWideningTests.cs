using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// An untyped local initialized with a literal locks the widened scalar as its
/// declared type (<c>$sum = 0</c> is <c>int</c>) so a later assignment of another
/// value of that scalar type-checks. The narrowed type stays the literal.
/// </summary>
[Trait("Category", "Checker")]
public class InferredLocalWideningTests
{
    [Fact]
    public void Check_LiteralInitializedLocal_AcceptsLaterScalarAssignment()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(int $item): int {
                $sum = 0;
                $sum += $item;
                $s = '';
                $s .= 'x';
                $f = 1.5;
                $f = 2.5;
                $b = false;
                $b = true;
                return $sum;
            }
            """);

        errors.Should().BeEmpty($"literal-initialized locals must accept later values of the same scalar: {Describe(errors)}");
    }

    [Fact]
    public void Check_LiteralInitializedLocal_RejectsDifferentBaseType()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $n = 0;
                $n = 'x';
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"an int local must still reject a string assignment: {Describe(errors)}");
    }

    [Fact]
    public void Check_LiteralInitializedLocal_KeepsLiteralForNarrowerTarget()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $c = 'red';
                'red'|'green' $d = $c;
            }
            """);

        errors.Should().BeEmpty($"a read of a literal-initialized local keeps the literal type: {Describe(errors)}");
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var result = IsolatedCompilation.ParseSnippet(
            content,
            "<?tyhpdef\n",
            phpVersion: "8.2",
            skipChecking: true,
            fileName: fileName,
            includeMinimalPhpStubs: false);
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
}
