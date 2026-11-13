using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

/// <summary>
/// <c>lazy</c> is legal only immediately before <c>new</c>. It stays an identifier everywhere else.
/// </summary>
[Trait("Category", "Parser")]
public class LazyNewParseTests
{
    [Fact]
    public void LazyNew_WithArguments_SetsIsLazy()
    {
        var created = ParseOne("""
            <?tyhp
            class User {
                public int $id;
                public function __construct(int $id) {}
            }

            function make(int $id): User {
                return lazy new User($id);
            }
            """);

        created.IsLazy.Should().BeTrue();
        created.Arguments.Should().NotBeNull();
    }

    [Fact]
    public void LazyNew_WithoutArguments_SetsIsLazy()
    {
        var created = ParseOne("""
            <?tyhp
            class User {
                public int $id;
            }

            function make(): User {
                return lazy new User;
            }
            """);

        created.IsLazy.Should().BeTrue();
        created.Arguments.Should().BeNull();
    }

    [Fact]
    public void New_OfClassNamedLazy_IsNotLazy()
    {
        var created = ParseOne("""
            <?tyhp
            class lazy {
                public int $id;
            }

            function make(): lazy {
                return new lazy();
            }
            """);

        created.IsLazy.Should().BeFalse();
    }

    [Fact]
    public void MethodNamedLazy_ParsesAsACall()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Query {
                public function lazy(): int {
                    return 1;
                }
            }

            function demo(Query $q): int {
                return $q->lazy();
            }

            function lazy(): int {
                return 2;
            }
            """);

        result.Success.Should().BeTrue(Describe(result));
        FindNew(result.Ast!).Should().BeEmpty();
    }

    [Theory]
    [InlineData("lazy class User { public int $id; }")]
    [InlineData("lazy function make(): void {}")]
    [InlineData("function make(object $o): object { return lazy clone $o; }")]
    [InlineData("class User { public int $id; } function make(): void { $x = lazy new class {}; }")]
    [InlineData("class User { public int $id; } function make(string $name): void { $x = lazy new $name(); }")]
    public void LazyNew_RejectedForms_DoNotParse(string body)
    {
        var result = ParserTestHelper.ParseTyhpContent("<?tyhp\n" + body);
        result.Success.Should().BeFalse();
    }

    private static PhpNewAst ParseOne(string source)
    {
        var result = ParserTestHelper.ParseTyhpContent(source);
        result.Success.Should().BeTrue(Describe(result));
        return FindNew(result.Ast!).Should().ContainSingle().Subject;
    }

    private static List<PhpNewAst> FindNew(IBase2Ast root)
    {
        var found = new List<PhpNewAst>();
        Walk(root);
        return found;

        void Walk(IBase2Ast node)
        {
            if (node is PhpNewAst created)
            {
                found.Add(created);
            }

            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    Walk(child);
                }
            }
        }
    }

    private static string Describe(ParseResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(error => error.Message));
}
