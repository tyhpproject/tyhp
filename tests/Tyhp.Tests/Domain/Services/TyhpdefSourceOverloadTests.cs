using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

/// <summary>
/// A source overload set publishes only its declared signatures to <c>package.tyhpdef</c>; the
/// implementation signature is not part of the call surface.
/// </summary>
[Trait("Category", "Tyhpdef")]
public class TyhpdefSourceOverloadTests
{
    private static string Generate(string source)
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
            .WithTyhpFile("lib.tyhp", source);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
        return def;
    }

    [Fact]
    public void Function_OverloadSet_PublishesOnlyDeclaredSignatures()
    {
        var def = Generate("""
            <?tyhp
            namespace Lib;
            function area(int $r): float;
            function area(int $w, int $h): float;
            function area(int $a, int $b = 0): float {
                return 1.0;
            }
            """);

        def.Should().Contain("area(int $r): float");
        def.Should().Contain("area(int $w, int $h): float");
        def.Should().NotContain("int $b = 0", def);
    }

    [Fact]
    public void Method_OverloadSet_PublishesOnlyDeclaredSignatures()
    {
        var def = Generate("""
            <?tyhp
            namespace Lib;
            final class Box {
                public function get(int $i): int;
                public function get(string $k): string;
                public function get(int|string $key): int|string {
                    return $key;
                }
            }
            """);

        def.Should().Contain("get(int $i): int");
        def.Should().Contain("get(string $k): string");
        def.Should().NotContain("int|string $key", def);
    }

    [Fact]
    public void AbstractOverloadSet_PublishesEverySignature()
    {
        var def = Generate("""
            <?tyhp
            namespace Lib;
            interface Repo {
                public function find(int $id): string;
                public function find(string $slug): string;
            }
            """);

        def.Should().Contain("find(int $id): string");
        def.Should().Contain("find(string $slug): string");
    }
}
