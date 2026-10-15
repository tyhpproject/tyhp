using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Found bug #445: <c>typeof(Class)</c> for a class declared in another namespace (imported with
/// <c>use</c>, aliased, or written fully qualified) is the class type, not <c>Type::mixed()</c>.
/// </summary>
[Trait("Category", "Emitter")]
public class TypeofImportedClassEmitterTests
{
    private const string Models = """
        <?tyhp
        namespace Demo\Models;

        class User {}

        class Account {}
        """;

    private const string ExpectedUser = "\\Tyhp\\Type::fromClassName(\\Demo\\Models\\User::class)";

    private static SnippetEmit.Result CompileApp(string body) =>
        SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["models.tyhp"] = Models,
            ["app.tyhp"] = $$"""
                <?tyhp
                namespace Demo\App;

                use Demo\Models\User;
                use Demo\Models\Account as Acct;

                class Local {}

                {{body}}
                """,
        });

    [Theory]
    [InlineData("User")]
    [InlineData("\\Demo\\Models\\User")]
    public void Typeof_ClassInOtherNamespace_EmitsFullyQualifiedClassType(string spelling)
    {
        var result = CompileApp($"$t = typeof({spelling});");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("$t = " + ExpectedUser + ";");
        result.Php.Should().NotContain("Type::mixed()");
    }

    [Fact]
    public void Typeof_AliasedImport_EmitsFullyQualifiedClassType()
    {
        var result = CompileApp("$t = typeof(Acct);");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("$t = \\Tyhp\\Type::fromClassName(\\Demo\\Models\\Account::class);");
        result.Warnings.Should().NotContain(w => w.Contains("Unused import") && w.Contains("Account"));
    }

    [Fact]
    public void Typeof_ClassInSameNamespace_StillEmitsClassType()
    {
        var result = CompileApp("$t = typeof(Local);");

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("$t = \\Tyhp\\Type::fromClassName(\\Demo\\App\\Local::class);");
    }

    [Fact]
    public void Typeof_ImportedClassInsideFunction_EmitsFullyQualifiedClassType()
    {
        var result = CompileApp("""
            function userType(): \Tyhp\Type {
                return typeof(User);
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("return " + ExpectedUser + ";");
    }

    [Fact]
    public void Instanceof_NullableImportedClass_ReifiesFullyQualifiedClassType()
    {
        var result = CompileApp("""
            function check(mixed $x): bool {
                return $x is ?User;
            }

            mixed $m = null;
            bool $b = $m is ?User;
            """);

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("\\Tyhp\\Type::is($x, \\Tyhp\\Type::nullable(" + ExpectedUser + "))");
        result.Php.Should().Contain("\\Tyhp\\Type::is($m, \\Tyhp\\Type::nullable(" + ExpectedUser + "))");
    }
}
