using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

/// <summary>
/// Generated <c>package.tyhpdef</c> keeps a nullable return on a type whose name merely contains the
/// letters "null", and keeps the default value of a promoted constructor property.
/// </summary>
[Trait("Category", "Tyhpdef")]
public class TyhpdefGeneratorNullabilityAndDefaultsTests
{
    [Fact]
    public void Library_NullableReturnOfAliasNamedWithNullable_KeepsQuestionMark()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("aliases.tyhp", """
                <?tyhp
                namespace Lib;
                type MatchSet = array<int|string, string>;
                type MatchSetNullable = array<int|string, string|null>;
                extension Strs extends string {
                    function first(string $pattern, 0 $flags = 0): ?\Lib\MatchSet;
                    function first(string $pattern, 512 $flags): ?\Lib\MatchSetNullable;
                    function first(string $pattern, int $flags = 0): \Lib\MatchSet|\Lib\MatchSetNullable|null {
                        return null;
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("fn first(string $pattern, 0 $flags = 0): ?\\Lib\\MatchSet =>");
        def.Should().Contain("fn first(string $pattern, 512 $flags): ?\\Lib\\MatchSetNullable =>");
        def.Should().NotContain("512 $flags): \\Lib\\MatchSetNullable =>");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Library_PromotedConstructorArrayDefault_IsKeptOnTheConstructorParameter()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("runtime.tyhp", """
                <?tyhp
                namespace Lib;
                final class Settings {
                    public function __construct(
                        public readonly bool $erased = false,
                        public readonly ?string $binder = null,
                        public readonly array<int> $layouts = [1],
                    ) {}
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("array<int> $layouts = [1]");
        def.Should().NotContain("array<int> $layouts = null");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Theory]
    [InlineData("[1]", true)]
    [InlineData("[1, 2, 3]", true)]
    [InlineData("['a' => 1, 'b' => [2, 3]]", true)]
    [InlineData("['a,b', ']']", true)]
    [InlineData("[\\PHP_INT_MAX]", true)]
    [InlineData("[$x]", false)]
    [InlineData("[...$x]", false)]
    [InlineData("[1 + 2]", false)]
    public void SanitizeDefaultValue_ArrayLiteral_AcceptedOnlyWhenEveryElementIsALiteral(string text, bool kept)
    {
        var result = PhpAstTypeExtractor.SanitizeDefaultValue(text);
        if (kept)
        {
            result.Should().Be(text);
        }
        else
        {
            result.Should().BeNull();
        }
    }
}
