using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// An extension operator the checker accepts is rewritten; a file that does not activate the
/// extension is not rewritten to the extension's operator method.
/// </summary>
[Trait("Category", "Emitter")]
public class ExtensionOperatorScopeEmitterTests
{
    private const string Library = """
        <?tyhp
        namespace Probe;

        class Point { public int $x = 0; }

        extension PointOps extends Point {
            operator + (self $left, self $right): self => new Point();
        }

        function sameFile(Point $a, Point $b): Point {
            return $a + $b;
        }
        """;

    [Fact]
    public void Operator_InDeclaringFile_IsRewritten()
    {
        var result = SnippetEmit.Compile(Library);
        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().NotContain("$a + $b");
        result.Php.Should().Contain("Point()");
    }

    [Fact]
    public void Operator_WithUseExtension_IsRewritten()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["a.tyhp"] = Library,
            ["b.tyhp"] = """
                <?tyhp
                namespace Other;

                use extension \Probe\PointOps;

                function useIt(\Probe\Point $a, \Probe\Point $b): \Probe\Point {
                    return $a + $b;
                }
                """,
        });

        result.Errors.Should().BeEmpty(string.Join("; ", result.Errors));
        result.Php.Should().NotContain("$a + $b");
        result.Php.Should().Contain("Point()");
    }
}
