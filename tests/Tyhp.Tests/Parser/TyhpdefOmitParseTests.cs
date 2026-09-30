using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefOmitParseTests
{
    [Fact]
    public void OmitFunctionSkeleton_Parses()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            omit function \array_map();
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var fn = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Single();
        fn.IsOmit.Should().BeTrue();
        fn.IsDeprecated.Should().BeFalse();
    }

    [Fact]
    public void OmitClassSkeleton_Parses()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            omit class \Closure {};
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        type.IsOmit.Should().BeTrue();
        type.IsPartial.Should().BeFalse();
    }

    [Fact]
    public void OverlayAgainstComment_AttachesToFunction()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            // @overlay-against: function array_map($callback, $array): array
            function \array_map(callable $callback, array $array): array;
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
        var fn = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Single();
        fn.AstGrammarAddons.Should().ContainKey("overlayAgainst");
        fn.AstGrammarAddons["overlayAgainst"].ValueString.Should().Be("function array_map($callback, $array): array");
    }

    [Fact]
    public void OverlayAgainstComment_AttachesToFunction_WhenAttributeSitsBetweenStampAndKeyword()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            // @overlay-against: function is_callable(mixed $value, bool $syntax_only, mixed $callable_name): bool
            #[\Tyhp\NativeTypeTest]
            function \is_callable(mixed $value, false $syntax_only = false, mixed &$callable_name = null): $value is callable;
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
        var fn = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Single();
        fn.AstGrammarAddons.Should().ContainKey("overlayAgainst");
        fn.AstGrammarAddons["overlayAgainst"].ValueString.Should().Be(
            "function is_callable(mixed $value, bool $syntax_only, mixed $callable_name): bool");
        fn.AstAttributes.Should().NotBeEmpty();
    }

    [Fact]
    public void OverlayAgainstComment_AttachesToClassMethod_WhenStampFollowsAttribute()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            partial class DOMNode {
                #[\Tyhp\Php(">=8.3")]
                // @overlay-against: function getRootNode(?array $options): DOMNode
                public function getRootNode<TOptions extends array>(?TOptions $options): DOMNode;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var method = Flatten(result.Ast).OfType<PhpMethodDeclAst>().Single();
        method.AstAttributes.Should().NotBeEmpty();
        method.AstGrammarAddons.Should().ContainKey("overlayAgainst");
        method.AstGrammarAddons["overlayAgainst"].ValueString.Should().Be(
            "function getRootNode(?array $options): DOMNode");
    }

    [Fact]
    public void OmitMemberInsidePartialClass_Parses()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            partial class \DomainException {
                omit public function getPrevious();
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        type.IsPartial.Should().BeTrue();
        var method = Flatten(result.Ast).OfType<PhpMethodDeclAst>().Single();
        method.IsOmit.Should().BeTrue();
    }

    [Fact]
    public void MethodNamedPartial_ParsesAsIdentifier_NotKeyword()
    {
        // Regression: `partial` must be a contextual keyword (only before class/trait/interface/enum/function),
        // not an unconditional reserved word, so a real PHP member named `partial` (e.g.
        // `Closure::partial()`) still parses. See FOUND_BUGS.md "Audit: Story 21 Phase 2 review".
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class ClosureExtensions {
                public static function partial(\Closure $this_, mixed ...$partialArgs): \Closure;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var method = Flatten(result.Ast).OfType<PhpMethodDeclAst>().Single();
        method.Identifier.Should().Be("partial");
    }

    [Fact]
    public void FunctionNamedPartial_ParsesAsIdentifier_NotKeyword()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            function \partial(mixed $value): mixed;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Should().ContainSingle();
    }

    [Fact]
    public void PartialClass_StillParsesAsKeyword_WhenFollowedByClass()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            partial class \DomainException {
                public function extra(): void;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var type = Flatten(result.Ast).OfType<TyhpdefImportObjectDeclAst>().Single();
        type.IsPartial.Should().BeTrue();
    }

    [Fact]
    public void MethodNamedOmit_ParsesAsIdentifier_NotKeyword()
    {
        // Same contextual-keyword pattern applies to `omit`: it must not shadow a real member
        // named `omit` either.
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            function \omit(mixed $value): void;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var fn = Flatten(result.Ast).OfType<TyhpdefImportFunctionDeclAst>().Single();
        fn.IsOmit.Should().BeFalse();
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
