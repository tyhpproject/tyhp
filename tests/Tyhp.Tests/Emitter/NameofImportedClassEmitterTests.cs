using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #446: <c>nameof(ImportedClass::$member)</c> as a file-level statement resolves the
/// <c>use</c>-imported class the same way it does inside a function.
/// </summary>
[Trait("Category", "Emitter")]
public class NameofImportedClassEmitterTests
{
    private const string Models = """
        <?tyhp
        namespace Demo\Models;

        class User {
            public const int MAX = 3;
            public static int $count = 0;
            public function __construct(public string $name = 'x') {}
            public function label(): string { return 'l'; }
        }
        """;

    private static SnippetEmit.Result CompileApp(string body) =>
        SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["models.tyhp"] = Models,
            ["app.tyhp"] = $$"""
                <?tyhp
                namespace Demo\App;

                use Demo\Models\User;
                use Demo\Models\User as Member;

                {{body}}
                """,
        });

    [Theory]
    [InlineData("User::$name", "'name'")]
    [InlineData("Member::$name", "'name'")]
    [InlineData("User::$count", "'count'")]
    [InlineData("User::MAX", "'MAX'")]
    [InlineData("User::label", "'label'")]
    [InlineData("User", "'User'")]
    [InlineData("\\Demo\\Models\\User::$name", "'name'")]
    public void Nameof_ImportedClassAtTopLevel_FoldsToName(string operand, string expected)
    {
        var result = CompileApp($"string $n = nameof({operand});");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("$n = " + expected + ";");
    }

    [Fact]
    public void Nameof_ImportedClassInEchoAtTopLevel_FoldsToName()
    {
        var result = CompileApp("echo nameof(User::$name), \"\\n\";");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("echo 'name'");
    }

    [Fact]
    public void Nameof_ImportedClassInsideFunction_StillFoldsToName()
    {
        var result = CompileApp("""
            function field(): string {
                return nameof(User::$name);
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("return 'name';");
    }

    [Fact]
    public void Nameof_ImportConsumedOnlyByNameof_IsNotReportedUnused()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["models.tyhp"] = Models,
            ["app.tyhp"] = """
                <?tyhp
                namespace Demo\App;

                use Demo\Models\User;

                string $n = nameof(User::$name);
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().NotContain(w => w.Contains("Unused import"));
    }
}
