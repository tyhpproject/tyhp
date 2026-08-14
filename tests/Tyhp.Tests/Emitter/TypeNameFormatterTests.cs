using Tyhp.TyhpLang.Emitter.NameGeneration;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class TypeNameFormatterTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("self", "This")]
    [InlineData("Self", "This")]
    [InlineData("int", "Int")]
    [InlineData("\\App\\Models\\User", "User")]
    [InlineData("int|float", "Number")]
    [InlineData("float|int", "Number")]
    [InlineData("int|string|float|bool|array", "Scalar")]
    [InlineData("int|array|bool", "IntOrArrayOrBool")]
    [InlineData("int|null", "Int")]
    [InlineData("string?", "String")]
    [InlineData("MyClass<int, float>", "MyClassOfInt_Float")]
    public void FormatTypeNameSegment_FormatsPerOperatorOverloadRules(string? input, string expected)
    {
        TypeNameFormatter.FormatTypeNameSegment(input).Should().Be(expected);
    }

    [Fact]
    public void FormatUnionSegments_JoinsFormattedPartsWithOr()
    {
        TypeNameFormatter.FormatUnionSegments(["int", "array", "bool"]).Should().Be("IntOrArrayOrBool");
    }
}
