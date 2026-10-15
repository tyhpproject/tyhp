using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A non-const type parameter inferred from a literal argument is the literal's widened
/// type, so <c>boxOf('plain')</c> is <c>Box&lt;string&gt;</c>.
/// </summary>
[Trait("Category", "Checker")]
public class LiteralGenericInferenceTests
{
    [Fact]
    public void StringLiteralArgument_InfersWidenedTypeArgument()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Box<T> {
                public function __construct(public T $value) {}
            }

            function boxOf<T>(T $x): Box<T> {
                return new Box($x);
            }

            function demo(): Box<string> {
                return boxOf('plain');
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void IntLiteralArgument_InfersWidenedTypeArgument()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Box<T> {
                public function __construct(public T $value) {}
            }

            function boxOf<T>(T $x): Box<T> {
                return new Box($x);
            }

            function demo(): Box<int> {
                return boxOf(1);
            }
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(CompilationResult result) =>
        result.Diagnostics.Errors
            .Where(e => (e.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{(int)e.Code}: {e.Message}"));
}
