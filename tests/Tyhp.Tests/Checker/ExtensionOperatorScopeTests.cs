using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A project-source extension's binary and unary operators follow the same activation rule as
/// extension methods and <c>operator convert</c>: the declaring file, <c>use extension</c>, or
/// <c>global use extension</c>.
/// </summary>
[Trait("Category", "Checker")]
public class ExtensionOperatorScopeTests
{
    private const string Library = """
        <?tyhp
        namespace Probe;

        class Point { public int $x = 0; }

        extension PointOps extends Point {
            operator + (self $left, self $right): self => $left;
            operator - (self $value): self => $value;
        }

        function sameFile(Point $a, Point $b): Point {
            return -($a + $b);
        }
        """;

    private static SnippetEmit.Result Compile(string other) =>
        SnippetEmit.Compile(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.tyhp"] = Library,
            ["b.tyhp"] = other,
        });

    [Fact]
    public void Operator_InDeclaringFile_Applies()
    {
        var result = SnippetEmit.Compile(Library);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
    }

    [Fact]
    public void Operator_InOtherFileWithoutImport_IsRejected()
    {
        var result = Compile("""
            <?tyhp
            namespace Other;

            function useIt(\Probe\Point $a, \Probe\Point $b): \Probe\Point {
                return -($a + $b);
            }
            """);

        result.Errors.Should().Contain(e => e.StartsWith("4029:", StringComparison.Ordinal),
            string.Join("; ", result.Errors));
    }

    [Fact]
    public void Operator_WithUseExtension_Applies()
    {
        var result = Compile("""
            <?tyhp
            namespace Other;

            use extension \Probe\PointOps;

            function useIt(\Probe\Point $a, \Probe\Point $b): \Probe\Point {
                return -($a + $b);
            }
            """);

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
    }
}
