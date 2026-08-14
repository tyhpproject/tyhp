using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class MessageCodeTests
{
    [Fact]
    public void MessageCodeValues_AreUnique()
    {
        var values = Enum.GetValues<MessageCode>().Select(code => (int)code).ToList();
        values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ParserCodes_AreIn1000Range()
    {
        var parserCodes = new[]
        {
            MessageCode.ParserUnknownError,
            MessageCode.ParserUnexpectedError,
            MessageCode.ParserCompileAborted,
            MessageCode.LexerCloseTagNotAllowedInTaglessMode,
        };

        parserCodes.Select(code => (int)code).Should().AllSatisfy(v => v.Should().BeInRange(1000, 1999));
    }

    [Fact]
    public void VisitorCodes_ExistIn2000Range()
    {
        Enum.IsDefined(MessageCode.VisitorUnexpectedAlternative).Should().BeTrue();
        Enum.IsDefined(MessageCode.VisitorMissingRequiredNode).Should().BeTrue();
        Enum.IsDefined(MessageCode.VisitorUnsupportedConstruct).Should().BeTrue();
        ((int)MessageCode.VisitorUnexpectedAlternative).Should().BeInRange(2000, 2999);
    }

    [Fact]
    public void BinderCodes_AreIn3000Range()
    {
        ((int)MessageCode.BinderDuplicateSymbolDeclaration).Should().BeInRange(3000, 3999);
        ((int)MessageCode.BinderSymbolNotFound).Should().BeInRange(3000, 3999);
    }

    [Fact]
    public void ConfigCodes_AreIn6000Range()
    {
        ((int)MessageCode.ConfigUnknownError).Should().BeInRange(6000, 6999);
        ((int)MessageCode.ConfigInvalidProjectType).Should().BeInRange(6000, 6999);
    }

    [Fact]
    public void BuildCodes_AreIn7100Range()
    {
        ((int)MessageCode.BuildUnknownError).Should().BeInRange(7100, 7199);
        ((int)MessageCode.BuildRuntimePackageNotAvailable).Should().BeInRange(7100, 7199);
    }
}
