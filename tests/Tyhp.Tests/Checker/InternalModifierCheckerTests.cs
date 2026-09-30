using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class InternalModifierCheckerTests
{
    [Fact]
    public void Check_PublicInternalClass_Is4002()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            public internal class Foo {}
            """);

        result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerMultipleVisibilities,
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
    }

    [Fact]
    public void Check_InternalPublicMethod_Is4002()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Foo {
                public internal function bar(): void {}
            }
            """);

        result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerMultipleVisibilities,
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
    }

    [Fact]
    public void Check_InternalClass_DoesNotReport4054()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            internal class Foo {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedInComposite);
    }
}
