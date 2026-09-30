using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Hidden or never-declared methods on scalar pseudo-objects (`string`, `int`, `float`,
/// `bool`, `array`, `\Closure`) must report TYHP4101 instead of typing as gradual unknown
/// and emitting invalid PHP.
/// </summary>
[Trait("Category", "Checker")]
public class ScalarPseudoObjectMethodTests
{
    [Fact]
    public void Check_UndefinedMethodOnString_Reports4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function testBogus(): string {
                string $s = "Hello";
                return $s->thisMethodDoesNotExistAtAll();
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            && HasParam(d, "thisMethodDoesNotExistAtAll"));
    }

    [Fact]
    public void Check_HiddenScalarMethod_Reports4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            use extension \Tyhp\StringExtensions {
                StringExtensions::toLower hide;
            }
            function testHidden(): string {
                string $s = "Hello";
                return $s->toLower();
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            && HasParam(d, "toLower"));
    }

    [Fact]
    public void Check_InScopeScalarMethod_No4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function testLength(): int {
                string $s = "Hello";
                return $s->length();
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_InScopeScalarMethod_TypesReturnNotUnknown()
    {
        // If length() stayed gradual unknown, assigning it to string would not be an error.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function test(): string {
                string $s = "Hello";
                return $s->length();
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_HiddenMethod_SiblingStillResolves()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            use extension \Tyhp\StringExtensions {
                StringExtensions::toLower hide;
            }
            function testLength(): int {
                string $s = "Hello";
                return $s->length();
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_UndefinedMethodOnInt_Reports4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function test(int $n): int {
                return $n->thisMethodDoesNotExistAtAll();
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            && HasParam(d, "thisMethodDoesNotExistAtAll"));
    }

    [Fact]
    public void Check_UndefinedMethodOnArray_Reports4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function test(array $a): mixed {
                return $a->thisMethodDoesNotExistAtAll();
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            && HasParam(d, "thisMethodDoesNotExistAtAll"));
    }

    [Fact]
    public void Check_UndefinedMethodOnClosure_Reports4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function test(\Closure $c): mixed {
                return $c->thisMethodDoesNotExistAtAll();
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            && HasParam(d, "thisMethodDoesNotExistAtAll"));
    }

    [Fact]
    public void Check_UndefinedMethodOnClass_DoesNotReport4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {}
            function test(Widget $w): mixed {
                return $w->thisMethodDoesNotExistAtAll();
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_LocalExtensionMethod_ResolvesWithout4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOps extends string {
                function shout(): string {
                    return $this;
                }
            }
            function test(): string {
                string $s = "hi";
                return $s->shout();
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void Check_StringLiteralReceiver_ResolvesExtendsStringWithout4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOps extends string {
                function shout(): string {
                    return $this;
                }
            }
            function test(): string {
                return "hello world"->shout();
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void Check_ParenthesizedIntReceiver_ResolvesExtendsIntWithout4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension MathOps extends int {
                function doubled(): int {
                    return $this + $this;
                }
            }
            function tick(): int {
                return 1;
            }
            function test(int $n): int {
                return ($n + tick())->doubled();
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void Check_ExtensionCall_DoesNotCountReceiverAsArgument()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOps extends string {
                function repeat(int $times): string {
                    return $this;
                }
            }
            function test(string $s): string {
                return $s->repeat(2);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerMissingArgument);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTooManyArguments);
    }

    [Fact]
    public void Check_LocalExtensionHide_Reports4101()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOps extends string {
                function shout(): string {
                    return $this;
                }
            }
            use extension StringOps {
                StringOps::shout hide;
            }
            function test(): string {
                string $s = "hi";
                return $s->shout();
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            && HasParam(d, "shout"));
    }

    private static bool HasParam(IDiagnostic diagnostic, string substring)
        => diagnostic.FormatParams is { } args
            && args.Any(p => (p?.ToString() ?? string.Empty)
                .Contains(substring, StringComparison.OrdinalIgnoreCase));

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
