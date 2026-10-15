using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #420: a call through <c>use function</c> resolves to the imported function, including
/// from a namespaced file with top-level statements.
/// </summary>
[Trait("Category", "Emitter")]
public class UseFunctionEntryFileEmitterTests
{
    private const string Usage = """
        <?tyhp
        namespace App\Usage;

        function countWithDeadline(int $n): int {
            return $n + 1;
        }
        """;

    [Fact]
    public void Emit_UseFunctionInNamespacedEntryFile_ResolvesToImportedFunction()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["usage.tyhp"] = Usage,
            ["entry.tyhp"] = """
                <?tyhp
                namespace App\E;

                use function App\Usage\countWithDeadline;

                echo countWithDeadline(2), "\n";
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain(" \\App\\Usage\\countWithDeadline(2)");
        result.Php.Should().NotContain("\\App\\E\\App\\Usage");
    }

    [Fact]
    public void Emit_UseFunctionInNamespacedFunctionBody_ResolvesToImportedFunction()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["usage.tyhp"] = Usage,
            ["entry.tyhp"] = """
                <?tyhp
                namespace App\E;

                use function App\Usage\countWithDeadline;

                function run(): int {
                    return countWithDeadline(2);
                }
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain(" \\App\\Usage\\countWithDeadline(2)");
        result.Php.Should().NotContain("\\App\\E\\App\\Usage");
    }

    [Fact]
    public void Emit_UseFunctionAlias_InNamespacedEntryFile_ResolvesToImportedFunction()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["usage.tyhp"] = Usage,
            ["entry.tyhp"] = """
                <?tyhp
                namespace App\E;

                use function App\Usage\countWithDeadline as bump;

                echo bump(2), "\n";
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain(" \\App\\Usage\\countWithDeadline(2)");
    }
}
