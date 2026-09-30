using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class Php86ReservedNameTests
{
    [Fact]
    public void ReturnInFinally_IsError()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function getConfig(): array {
                try {
                    return [];
                } finally {
                    return [];
                }
            }
            """);

        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerReturnInFinally);
    }

    [Fact]
    public void ReturnInsideClosureInFinally_IsAllowed()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function run(): int {
                try {
                    return 1;
                } finally {
                    $f = function (): int { return 2; };
                    $f();
                }
            }
            """);

        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerReturnInFinally);
    }

    [Fact]
    public void ReturnInNestedTryInsideFinally_IsError()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function run(): int {
                try {
                    return 1;
                } finally {
                    try {
                        return 2;
                    } catch (\Throwable $e) {
                        (void)$e;
                    }
                }
            }
            """);

        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerReturnInFinally);
    }

    [Fact]
    public void ClassAndFunctionNamedLet_AreErrors()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class let {}
            function let(): void {}
            """);

        result.Diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerReservedPhp86Name && d.Message.Contains("class"));
        result.Diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerReservedPhp86Name && d.Message.Contains("function"));
    }

    [Fact]
    public void TyhpdefClassNamedIs_IsError_MethodNamedIs_IsAllowed()
    {
        // Unqualified `class Is` does not parse (`is` is already the operator). A qualified
        // PHP name is one token, so the ban is on the Tyhp-facing simple name.
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {}
            """,
            """
            <?tyhpdef
            namespace Demo {
                class \Demo\Is {
                    public static function is(mixed $value): bool;
                }
            }
            """);

        result.Diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerReservedPhp86Name && d.Message.Contains("class"));
        result.Diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerReservedPhp86Name && d.Message.Contains("function"));
    }

    [Fact]
    public void FunctionReadonly_IsError_MethodReadonly_IsAllowed()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function readonly(): void {}
            class Box {
                public function readonly(): void {}
            }
            """);

        var reserved = result.Diagnostics.Errors
            .Where(d => d.Code == MessageCode.CheckerReservedPhp86Name)
            .ToList();
        reserved.Should().ContainSingle();
        reserved[0].Message.Should().Contain("function");
    }

    [Fact]
    public void ConstNamespaceAndUnderscore_AreErrors()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Foo {
                const NAMESPACE = '';
                const _ = 1;
            }
            const _ = 2;
            """);

        result.Diagnostics.Errors.Where(d => d.Code == MessageCode.CheckerReservedPhp86Name)
            .Should().HaveCount(3);
    }

    [Fact]
    public void UseAliasUnderscore_IsError_FunctionUnderscore_IsAllowed()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Box {}
            use Box as _;
            function _(): void {}
            """);

        result.Diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerReservedPhp86Name && d.Message.Contains("class alias"));
        result.Diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerReservedPhp86Name && d.Message.Contains("'_' cannot be used as a function"));
    }

    [Fact]
    public void TyhpdefFunctionNamedIs_IsError()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {}
            """,
            """
            <?tyhpdef
            function is(): void;
            """);

        result.Diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerReservedPhp86Name && d.Message.Contains("function"));
    }

}
