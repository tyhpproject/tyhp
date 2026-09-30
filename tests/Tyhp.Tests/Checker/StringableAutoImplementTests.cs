using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.7 Phase 2: a class or interface that declares <c>__toString</c> is
/// <c>\Stringable</c> (PHP auto-implement). Assignability, <c>instanceof</c>,
/// echo/concat (TYHP4120), and interpolation follow that rule. Written
/// <c>implements \Stringable</c> remains legal.
/// </summary>
[Trait("Category", "Checker")]
public class StringableAutoImplementTests
{
    [Fact]
    public void Check_ClassWithToString_AssignableToStringable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function __toString(): string { return 'x'; }
            }

            function take(\Stringable $s): void {}

            function f(Foo $foo): void {
                take($foo);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_EchoClassWithToString_DoesNotReport4120()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function __toString(): string { return 'x'; }
            }

            function f(Foo $foo): void {
                echo $foo;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConcatNonStringable);
    }

    [Fact]
    public void Check_ConcatClassWithToString_DoesNotReport4029()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function __toString(): string { return 'x'; }
            }

            function f(Foo $foo): string {
                return $foo . 'y';
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerInvalidOperatorForType);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConcatNonStringable);
    }

    [Fact]
    public void Check_InstanceofStringable_NarrowsClassWithToString()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function __toString(): string { return 'x'; }
            }

            function take(\Stringable $s): void {}

            function f(Foo $foo): void {
                if ($foo instanceof \Stringable) {
                    take($foo);
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_InterfaceWithToString_IsStringableType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Named {
                public function __toString(): string;
            }

            function take(\Stringable $s): void {}

            function f(Named $named): void {
                take($named);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_ClassImplementingInterfaceWithToString_IsStringable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Named {
                public function __toString(): string;
            }

            class Foo implements Named {
                public function __toString(): string { return 'x'; }
            }

            function take(\Stringable $s): void {}

            function f(Foo $foo): void {
                take($foo);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_ExplicitImplementsStringable_StillWorks()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo implements \Stringable {
                public function __toString(): string { return 'x'; }
            }

            function take(\Stringable $s): void {}

            function f(Foo $foo): void {
                take($foo);
                echo $foo;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConcatNonStringable);
    }

    [Fact]
    public void Check_ClassWithoutToString_NotAssignableToStringable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}

            function take(\Stringable $s): void {}

            function f(Foo $foo): void {
                take($foo);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_EchoClassWithoutToString_Reports4120()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}

            function f(Foo $foo): void {
                echo $foo;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerConcatNonStringable);
    }

    [Fact]
    public void Check_ParentToString_ChildIsStringable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public function __toString(): string { return 'x'; }
            }

            class Child extends Base {}

            function take(\Stringable $s): void {}

            function f(Child $child): void {
                take($child);
                echo $child;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConcatNonStringable);
    }

    [Fact]
    public void Check_TraitToString_UsingClassIsStringable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Named {
                public function __toString(): string { return 'x'; }
            }

            class Foo {
                use Named;
            }

            function take(\Stringable $s): void {}

            function f(Foo $foo): void {
                take($foo);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_ConvertToStringOperator_IsStringable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public function __construct(public int $amount): void {}

                operator convert(self $value): string {
                    return (string) $value->amount;
                }
            }

            function take(\Stringable $s): void {}

            function f(Money $m): void {
                take($m);
                echo $m;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConcatNonStringable);
    }

    [Fact]
    public void Check_InterpolationClassWithToString_DoesNotReport4120()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function __toString(): string { return 'x'; }
            }

            function f(Foo $foo): string {
                return "hi $foo";
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerConcatNonStringable);
    }

    [Fact]
    public void Check_InterpolationClassWithoutToString_Reports4120()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}

            function f(Foo $foo): string {
                return "hi $foo";
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerConcatNonStringable);
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
