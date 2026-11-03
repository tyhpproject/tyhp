using FluentAssertions;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Xunit;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A template hole must be a type with a string form, written as a name, literal, or union
/// (TYHP4199). An unsupported hole never resolves silently.
/// </summary>
[Trait("Category", "Checker")]
public class TemplateStringUnsupportedHoleTests
{
    private const string Declarations = """
        class Box<T> {}
        enum Suit { case Hearts; }
        type Flag = 'on'|'off';
        type Inner = "b${int}";
        type Wrap<T> = "<${T}>";
        """;

    private static IReadOnlyList<(MessageCode Code, string Message)> HoleErrors(string hole) =>
        MixedCheckSnippet.Errors(
            "<?tyhp\n" + Declarations + "\ntype Target = \"a${" + hole + "}\";\n"
            + "function f(): Target { return 'ax'; }\n");

    [Theory]
    [InlineData("array", "`array`")]
    [InlineData("callable", "`callable`")]
    [InlineData("iterable", "`iterable`")]
    [InlineData("mixed", "`mixed`")]
    [InlineData("bool", "`bool`")]
    [InlineData("true", "`true`")]
    [InlineData("null", "`null`")]
    [InlineData("object", "`object`")]
    [InlineData("void", "`void`")]
    [InlineData("never", "`never`")]
    [InlineData("Suit", "`\\Suit`")]
    [InlineData("Box<int>", "`\\Box<int>`")]
    [InlineData("int<0, 5>", "`int<0, 5>`")]
    [InlineData("'a'|bool", "`bool`")]
    [InlineData("$name", "a variable")]
    [InlineData("'a'|$name", "a variable")]
    [InlineData("strtoupper('a')", "a call")]
    [InlineData("Suit::Hearts", "a class constant")]
    [InlineData("1 + 2", "an expression")]
    [InlineData("'a' . 'b'", "an expression")]
    [InlineData("\"b${int}\"", "an inline template")]
    public void UnsupportedHole_ReportsOnlyTyhp4199(string hole, string found)
    {
        var errors = HoleErrors(hole);

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Code.Should().Be(MessageCode.CheckerTemplateStringUnsupportedHole);
        errors[0].Message.Should().EndWith("found " + found);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("float")]
    [InlineData("'x'|'y'")]
    [InlineData("1|2")]
    [InlineData("Flag")]
    [InlineData("Inner")]
    [InlineData("__ClassName")]
    [InlineData("Wrap<'x'>")]
    public void SupportedHole_DoesNotReport(string hole)
    {
        var errors = HoleErrors(hole);

        errors.Should().NotContain(e => e.Code == MessageCode.CheckerTemplateStringUnsupportedHole);
    }

    [Fact]
    public void UnknownHoleName_ReportsNotFoundOnly()
    {
        var errors = HoleErrors("Missing");

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Code.Should().Be(MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void UnusedAlias_StillReports()
    {
        var errors = MixedCheckSnippet.Errors("<?tyhp\ntype Lonely = \"a${array}\";\n");

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Code.Should().Be(MessageCode.CheckerTemplateStringUnsupportedHole);
    }

    [Fact]
    public void TypeParameterHole_UsesItsConstraint()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type Tagged<T> = "<${T}>";
            type Keyed<T extends 'a'|'b'> = "k-${T}";
            type Listed<T extends array> = "l-${T}";
            function tagLabel(): Tagged<'x'> { return '<x>'; }
            function keyLabel(): Keyed<'a'> { return 'k-a'; }
            """);

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Code.Should().Be(MessageCode.CheckerTemplateStringUnsupportedHole);
        errors[0].Message.Should().EndWith("found `T`");
    }

    [Fact]
    public void DocumentedExample_ReportsOnlyTheBoolHole()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type Version = "v${int}";
            type Release = "release-${Version}";
            type Flagged = "flag-${bool}";
            function currentRelease(): Release { return 'release-v3'; }
            """);

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Code.Should().Be(MessageCode.CheckerTemplateStringUnsupportedHole);
        errors[0].Message.Should().EndWith("found `bool`");
    }

    [Fact]
    public void ExplainExample_ReportsEachUnsupportedHole()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type Tagged = "tag-${bool}";
            type Keyed  = "key-${$name}";
            type Nested = "a-${"b${int}"}";

            type Flag   = 'on'|'off';
            type Toggle = "tag-${Flag}";
            type Items  = "${int} items";
            type Inner  = "b${int}";
            type Outer  = "a-${Inner}";
            """);

        errors.Should().HaveCount(3, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerTemplateStringUnsupportedHole);
    }
}
