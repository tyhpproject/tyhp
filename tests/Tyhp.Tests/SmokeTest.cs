using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests;

public class SmokeTest
{
    [Fact]
    public void Smoke_CanReferenceMainProjectTypes()
    {
        var helper = typeof(ParserTestHelper);
        helper.Should().NotBeNull();
        typeof(Base2Ast).Should().NotBeNull();
        typeof(SrcFileAst).Should().NotBeNull();
        Enum.IsDefined(typeof(MessageCode), MessageCode.ParserUnknownError).Should().BeTrue();
    }
}
