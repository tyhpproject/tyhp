using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

public class MagicCallArgumentTests
{
    private const string Bag = """
        <?tyhp
        class Bag {
            public function __call(string $name, array $args): mixed {
                return null;
            }
            public function real(int $n): void {}
        }

        """;

    [Fact]
    public void DynamicCall_DoesNotCheckArgumentsAgainstCall()
    {
        MixedCheckSnippet.Errors(Bag + """
            function demo(Bag $b): void {
                $b->anything();
                $b->anything(1);
            }
            """).Should().BeEmpty();
    }

    [Fact]
    public void DirectCall_StillChecksTheMagicSignature()
    {
        MixedCheckSnippet.Errors(Bag + """
            function demo(Bag $b): void {
                $b->__call("x");
            }
            """)
            .Select(e => e.Code)
            .Should().Contain(MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void RealMethod_StillChecksArguments()
    {
        MixedCheckSnippet.Errors(Bag + """
            function demo(Bag $b): void {
                $b->real();
            }
            """)
            .Select(e => e.Code)
            .Should().Contain(MessageCode.CheckerMissingArgument);
    }
}
