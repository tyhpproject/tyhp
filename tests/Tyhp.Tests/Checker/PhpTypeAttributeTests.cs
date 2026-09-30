using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.6 Phase 6d: <c>#[\Tyhp\PhpType]</c> is only legal on PHP type-declaration sites
/// and the constructor string must be a PHP type-hint spelling. Checker types stay Tyhp.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Story21.6")]
public class PhpTypeAttributeTests
{
    [Fact]
    public void Check_PhpTypeOnParameterReturnPropertyConstant_DoesNotError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\PhpType('mixed')]
            function ret(int $n): string {
                return (string) $n;
            }

            function param(#[\Tyhp\PhpType('mixed')] string $s): void {}

            class Holder {
                #[\Tyhp\PhpType('mixed')]
                public string $name = '';

                #[\Tyhp\PhpType('mixed')]
                public const string TAG = 'x';

                public function __construct(
                    #[\Tyhp\PhpType('mixed')]
                    public int $id,
                ): void {}
            }

            #[\Tyhp\PhpType('mixed')]
            const FILE_TAG = 'y';
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPhpTypeInvalidTarget
            || d.Code == MessageCode.CheckerPhpTypeInvalidSpelling);
    }

    [Fact]
    public void Check_PhpTypeNamedArgument_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(#[\Tyhp\PhpType(type: 'int|string')] int $n): void {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPhpTypeInvalidSpelling
            || d.Code == MessageCode.CheckerPhpTypeInvalidTarget);
    }

    [Fact]
    public void Check_PhpTypeOnClass_Reports4327()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\PhpType('mixed')]
            class C {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpTypeInvalidTarget);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerPhpTypeInvalidTarget).Should().Be(1);
    }

    [Theory]
    [InlineData("interface")]
    [InlineData("trait")]
    [InlineData("enum")]
    public void Check_PhpTypeOnTypeDecl_Reports4327(string kind)
    {
        var body = kind == "enum" ? "enum E { case A; }" : $"{kind} E {{}}";
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            #[\Tyhp\PhpType('mixed')]
            {{body}}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpTypeInvalidTarget);
    }

    [Fact]
    public void Check_PhpTypeOnEnumCase_Reports4327()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E {
                #[\Tyhp\PhpType('mixed')]
                case A;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpTypeInvalidTarget);
    }

    [Fact]
    public void Check_PhpTypeOnPropertyHook_Reports4327()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                private string $_name = '';
                public string $name {
                    #[\Tyhp\PhpType('mixed')]
                    get => $this->_name;
                    set(string $value) {
                        $this->_name = $value;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpTypeInvalidTarget);
        diagnostics.Errors.Single(d => d.Code == MessageCode.CheckerPhpTypeInvalidTarget)
            .Message.Should().Contain("property hook");
    }

    [Fact]
    public void Check_PhpTypeOnSetHookParameter_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                private string $_name = '';
                public string $name {
                    get => $this->_name;
                    set(#[\Tyhp\PhpType('mixed')] string $value) {
                        $this->_name = $value;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPhpTypeInvalidTarget);
    }

    [Fact]
    public void Check_InvalidPhpTypeSpelling_Reports4329()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(#[\Tyhp\PhpType('int[]')] int $n): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpTypeInvalidSpelling);
    }

    [Fact]
    public void Check_MissingPhpTypeArgument_Reports4329()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(#[\Tyhp\PhpType] int $n): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpTypeInvalidSpelling);
    }

    [Theory]
    [InlineData("?mixed")]
    [InlineData("void|int")]
    [InlineData("Foo<T>")]
    [InlineData("A&B|C")]
    public void Check_IllegalPhpTypeSpelling_Reports4329(string spelling)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            function f(#[\Tyhp\PhpType('{{spelling}}')] int $n): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPhpTypeInvalidSpelling);
    }

    [Fact]
    public void Check_PhpTypeDoesNotChangeCheckerTypes()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function takes(string $s): void {}

            function give(#[\Tyhp\PhpType('mixed')] string $x): void {
                takes($x);
            }

            function mismatch(#[\Tyhp\PhpType('mixed')] string $x): void {
                int $n = $x;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerPhpTypeInvalidTarget
            || d.Code == MessageCode.CheckerPhpTypeInvalidSpelling);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "phptype.tyhp");
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
