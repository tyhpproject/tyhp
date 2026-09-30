using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class DiagnosticCodeParserTests
{
    [Theory]
    [InlineData("TYHP8027", MessageCode.TyhpdefRuntimePackageNotFound)]
    [InlineData("tyhp8027", MessageCode.TyhpdefRuntimePackageNotFound)]
    [InlineData("8027", MessageCode.TyhpdefRuntimePackageNotFound)]
    [InlineData("  TYHP8027  ", MessageCode.TyhpdefRuntimePackageNotFound)]
    public void TryParse_KnownWarningCode_Succeeds(string token, MessageCode expected)
    {
        DiagnosticCodeParser.TryParse(token, out var code).Should().BeTrue();
        code.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TYHP")]
    [InlineData("not-a-code")]
    [InlineData("TYHP99999")]
    [InlineData("0")]
    [InlineData("-8027")]
    public void TryParse_UnknownOrEmpty_Fails(string? token)
    {
        DiagnosticCodeParser.TryParse(token, out _).Should().BeFalse();
    }
}
