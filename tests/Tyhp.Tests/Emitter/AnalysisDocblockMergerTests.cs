using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

public class AnalysisDocblockMergerTests
{
    [Fact]
    public void Merge_KeepsEveryRepeatedAuthorTag()
    {
        var author = "/**\n * @throws \\RuntimeException a\n * @throws \\LogicException b\n */";

        var merged = AnalysisDocblockMerger.Merge(author, [], includeProse: true);

        merged.Should().Contain("@throws \\RuntimeException a");
        merged.Should().Contain("@throws \\LogicException b");
    }

    [Fact]
    public void Merge_KeepsWrappedTagTextWithItsTag()
    {
        var author = "/**\n * Summary.\n *\n * @param array<int, User> $ids first line\n *        second line\n */";
        var generated = new[] { new GeneratedDocTag("return", "@return int") };

        var merged = AnalysisDocblockMerger.Merge(author, generated, includeProse: false);

        // The continuation is not prose, so it survives `output.comments: false`.
        merged.Should().Be(
            "/**\n * @return int\n * @param array<int, User> $ids first line\n * second line\n */");
    }

    [Fact]
    public void Merge_AuthorParamKeyIgnoresVariablesInDescription()
    {
        var author = "/**\n * @param int $a uses $b\n */";
        var generated = new[]
        {
            new GeneratedDocTag("param:$a", "@param int $a"),
            new GeneratedDocTag("param:$b", "@param string $b"),
        };

        var merged = AnalysisDocblockMerger.Merge(author, generated, includeProse: true);

        merged.Should().Be("/**\n * @param int $a uses $b\n * @param string $b\n */");
    }

    [Theory]
    [InlineData("array<int|string> $x", "$x")]
    [InlineData("array{a: int} $x desc $y", "$x")]
    [InlineData("callable(int): string ...$cb", "$cb")]
    [InlineData("$this $x", "$x")]
    public void ExtractVariable_TakesTheParameterName(string rest, string expected)
    {
        AnalysisDocblockMerger.ExtractVariable(rest).Should().Be(expected);
    }

    [Fact]
    public void Merge_DirectivesStayVerbatimAndBeforeGeneratedTags()
    {
        var author = "/**\n * @tyhpEmitterStart(onCall)\n * /@@ inheritFrom(\\A::b) @@/\n */";
        var generated = new[] { new GeneratedDocTag("param:$x", "@param array<int, int> $x") };

        var merged = AnalysisDocblockMerger.Merge(author, generated, includeProse: false);

        merged.Should().Be(
            "/**\n * @tyhpEmitterStart(onCall)\n * /@@ inheritFrom(\\A::b) @@/\n *\n * @param array<int, int> $x\n */");
    }
}
