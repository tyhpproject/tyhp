using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #444: a <c>use const</c> name is always fully qualified from the root, never relative
/// to the current namespace (<c>\Demo9\Util\LIMIT</c>, not <c>\Demo9\Demo9\Util\LIMIT</c>).
/// </summary>
[Trait("Category", "Emitter")]
public class UseConstNamespacedEmitterTests
{
    private const string Util = """
        <?tyhp
        namespace Demo9\Util;

        const LIMIT = 5;
        const OTHER = 7;
        """;

    [Fact]
    public void Emit_UseConst_InNamespacedFileTopLevel_IsRootedAtGlobalNamespace()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["util.tyhp"] = Util,
            ["entry.tyhp"] = """
                <?tyhp
                namespace Demo9;

                use const Demo9\Util\LIMIT;

                int $a = LIMIT;
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("$a = \\Demo9\\Util\\LIMIT;");
        result.Php.Should().NotContain("Demo9\\Demo9");
    }

    [Fact]
    public void Emit_UseConst_InNamespacedFunctionBody_IsRootedAtGlobalNamespace()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["util.tyhp"] = Util,
            ["entry.tyhp"] = """
                <?tyhp
                namespace Demo9;

                use const Demo9\Util\LIMIT;

                function limit(): int {
                    return LIMIT;
                }
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("return \\Demo9\\Util\\LIMIT;");
        result.Php.Should().NotContain("Demo9\\Demo9");
    }

    [Fact]
    public void Emit_UseConstAlias_InNamespacedFile_ResolvesToImportedConstant()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["util.tyhp"] = Util,
            ["entry.tyhp"] = """
                <?tyhp
                namespace Demo9;

                use const Demo9\Util\LIMIT as CAP;

                int $a = CAP;

                function cap(): int {
                    return CAP;
                }
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("$a = \\Demo9\\Util\\LIMIT;");
        result.Php.Should().Contain("return \\Demo9\\Util\\LIMIT;");
        result.Php.Should().NotContain("Demo9\\Demo9");
    }

    [Fact]
    public void Emit_GroupedUseConst_InNamespacedFile_ResolvesEveryMember()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["util.tyhp"] = Util,
            ["entry.tyhp"] = """
                <?tyhp
                namespace Demo9\G;

                use const Demo9\Util\{LIMIT as CAP, OTHER};

                int $a = CAP;
                int $b = OTHER;
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("$a = \\Demo9\\Util\\LIMIT;");
        result.Php.Should().Contain("$b = \\Demo9\\Util\\OTHER;");
        result.Php.Should().NotContain("Demo9\\G\\Demo9");
    }

    [Fact]
    public void Emit_UseConst_InGlobalEntryFile_IsRootedAtGlobalNamespace()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["util.tyhp"] = Util,
            ["entry.tyhp"] = """
                <?tyhp
                use const Demo9\Util\LIMIT;
                use const Demo9\Util\OTHER as OTH;

                int $a = LIMIT;
                int $b = OTH;
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("$a = \\Demo9\\Util\\LIMIT;");
        result.Php.Should().Contain("$b = \\Demo9\\Util\\OTHER;");
    }
}
