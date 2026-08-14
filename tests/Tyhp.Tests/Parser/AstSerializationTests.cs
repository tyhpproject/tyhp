using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Collection("AstCache")]
public class AstSerializationTests
{
    public static IEnumerable<object[]> RoundTripFiles()
        => TestFileManager.GetAllTestDataFiles("ValidTyhp", ".tyhp")
            .Take(5)
            .Select(path => new object[] { path });

    [Fact]
    public void SerializeDeserialize_PreservesStructure_ForSimpleClass()
    {
        var parse = ParserTestHelper.ParseTyhpContent("<?tyhp\nnamespace App;\nclass Foo { public int $x = 0; }\n", "simple.tyhp");
        parse.Success.Should().BeTrue();
        parse.Ast.Should().NotBeNull();

        var bytes = parse.Ast!.Serialize();
        Base2Ast.TryDeserialize(bytes, out var roundTripped).Should().BeTrue();
        roundTripped.Should().NotBeNull();
        roundTripped!.IsValid().Should().BeTrue();
        AstAssertions.ShouldMatchStructure(parse.Ast, roundTripped);
    }

    [Theory]
    [MemberData(nameof(RoundTripFiles))]
    public void SerializeDeserialize_PreservesStructure(string filePath)
    {
        var parse = ParserTestHelper.ParseFile(filePath);
        parse.Success.Should().BeTrue();
        parse.Ast.Should().NotBeNull();

        var bytes = parse.Ast!.Serialize();
        Base2Ast.TryDeserialize(bytes, out var roundTripped).Should().BeTrue();
        roundTripped.Should().NotBeNull();
        roundTripped!.IsValid().Should().BeTrue();
        AstAssertions.ShouldMatchStructure(parse.Ast, roundTripped);
    }

    [Fact]
    public void AstCache_AddOrUpdate_RetrievesEquivalentAst()
    {
        AstCacheService.ClearMemory();
        var parse = ParserTestHelper.ParseTyhpContent("<?tyhp\nclass CacheMe {}\n", "cache_test.tyhp");
        parse.Ast.Should().NotBeNull();

        AstCacheService.AddOrUpdate(parse.Ast);
        var cached = AstCacheService.Get(parse.Ast!.FileName);
        cached.Should().NotBeNull();
        cached!.GetType().Should().Be(parse.Ast.GetType());
        cached.NodeType.Should().Be(parse.Ast.NodeType);
        AstCacheService.ClearMemory();
    }
}
