using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class ShortFunctionSyntaxTests
{
    [Fact]
    public void Parse_TopLevelShortFunction_NonGeneric_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            fn myFunc(int $val): int => $val + 5;
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShortMethod_NonGeneric_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class MyClass {
                public fn getVal(): int => 5;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
    }
}
