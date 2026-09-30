using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefFallbackParseTests
{
    [Fact]
    public void FallbackFunction_Parses()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            fallback function collect(mixed $value = null): mixed;
            deprecated fallback async function trigger_deprecation(string $package): void;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var functions = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().ToList();
        functions.Should().HaveCount(2);
        functions[0].IsFallback.Should().BeTrue();
        functions[0].IsDeprecated.Should().BeFalse();
        functions[0].IsAsync.Should().BeFalse();
        functions[1].IsFallback.Should().BeTrue();
        functions[1].IsDeprecated.Should().BeTrue();
        functions[1].IsAsync.Should().BeTrue();
    }

    [Fact]
    public void FunctionNamedFallback_StaysAName()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            function fallback(): void;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var fn = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Single();
        fn.IsFallback.Should().BeFalse();
        fn.Identifier.Should().Be("fallback");
    }

    private static IEnumerable<IBase2Ast> Flatten(IBase2Ast? node)
    {
        if (node == null)
        {
            yield break;
        }

        yield return node;
        foreach (var child in node.AstChildren)
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }
}
