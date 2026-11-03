using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Declarations PHP accepts only from 8.6 (readonly property defaults, <c>__debugInfo()</c> on an
/// enum), the magic methods PHP never allows on an enum, and <c>#[\Override]</c> on class
/// constants and enum cases at the current targets.
/// </summary>
[Trait("Category", "Checker")]
public class Php86DeclarationGateTests
{
    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void ReadonlyPropertyDefault_BelowPhp86_Reports4378(string phpVersion)
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Point {
                public readonly int $x = 1;
            }
            """, phpVersion);

        var errors = Errors(diagnostics, MessageCode.CheckerReadonlyPropertyDefaultRequiresPhp86);
        errors.Should().ContainSingle();
        errors[0].Message.Should().Contain("`$x`").And.Contain($"`{phpVersion}`");
    }

    [Fact]
    public void ReadonlyPropertyDefault_EachDefaultedPropertyInTheList_Reports4378()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Point {
                public readonly int $x = 1, $y, $z = 3;

                public function __construct() {
                    $this->y = 2;
                }
            }
            """);

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyDefaultRequiresPhp86)
            .Select(d => d.Message)
            .Should().HaveCount(2)
            .And.Contain(m => m.Contains("`$x`"))
            .And.Contain(m => m.Contains("`$z`"));
    }

    [Fact]
    public void ReadonlyClass_PropertyDefault_Reports4378()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Point {
                public readonly int $x = 1;
            }
            """);

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyDefaultRequiresPhp86).Should().ContainSingle();
    }

    [Fact]
    public void ReadonlyClass_ImplicitlyReadonlyPropertyDefault_Reports4378()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Point {
                public int $x = 1;
            }
            """);

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyDefaultRequiresPhp86).Should().ContainSingle();
    }

    [Fact]
    public void TraitReadonlyPropertyDefault_Reports4378()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait HasX {
                public readonly int $x = 1;
            }
            """);

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyDefaultRequiresPhp86).Should().ContainSingle();
    }

    [Fact]
    public void ReadonlyPropertyDefault_AtPhp86_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Point {
                public readonly int $x = 1;
            }
            readonly class Other {
                public int $y = 2;
            }
            """, phpVersion: "8.6");

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyDefaultRequiresPhp86).Should().BeEmpty();
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.5")]
    public void ReadonlyWithoutDefault_PromotedDefault_AndMutableDefault_AreAccepted(string phpVersion)
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Point {
                public readonly int $x;
                public int $y = 2;
                public static int $count = 0;

                public function __construct(public readonly int $z = 3) {
                    $this->x = 1;
                }
            }
            readonly class Pair {
                public function __construct(public readonly int $left = 0) {}
            }
            """, phpVersion);

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyDefaultRequiresPhp86).Should().BeEmpty();
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.5")]
    public void EnumDebugInfo_BelowPhp86_Reports4379(string phpVersion)
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit {
                case Hearts;

                public function __debugInfo(): array<string, mixed> {
                    return [];
                }
            }
            """, phpVersion);

        var errors = Errors(diagnostics, MessageCode.CheckerEnumDebugInfoRequiresPhp86);
        errors.Should().ContainSingle();
        errors[0].Message.Should().Contain("`Suit`").And.Contain($"`{phpVersion}`");
        Errors(diagnostics, MessageCode.CheckerEnumMethodNotAllowed).Should().BeEmpty();
    }

    [Fact]
    public void EnumDebugInfo_AtPhp86_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit {
                case Hearts;

                public function __debugInfo(): array<string, mixed> {
                    return [];
                }
            }
            """, phpVersion: "8.6");

        Errors(diagnostics, MessageCode.CheckerEnumDebugInfoRequiresPhp86).Should().BeEmpty();
        Errors(diagnostics, MessageCode.CheckerEnumMethodNotAllowed).Should().BeEmpty();
    }

    [Fact]
    public void ClassDebugInfo_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Card {
                public function __debugInfo(): array<string, mixed> {
                    return [];
                }
            }
            """);

        Errors(diagnostics, MessageCode.CheckerEnumDebugInfoRequiresPhp86).Should().BeEmpty();
    }

    [Theory]
    [InlineData("public function __construct() {}")]
    [InlineData("public function __destruct() {}")]
    [InlineData("public function __clone(): void {}")]
    [InlineData("public function __get(string $name): mixed { return null; }")]
    [InlineData("public function __set(string $name, mixed $value): void {}")]
    [InlineData("public function __isset(string $name): bool { return false; }")]
    [InlineData("public function __unset(string $name): void {}")]
    [InlineData("public function __toString(): string { return ''; }")]
    [InlineData("public function __serialize(): array<string, mixed> { return []; }")]
    [InlineData("public function __unserialize(array<string, mixed> $data): void {}")]
    [InlineData("public function __sleep(): array<int, string> { return []; }")]
    [InlineData("public function __wakeup(): void {}")]
    [InlineData("public static function __set_state(array<string, mixed> $properties): object { return new \\stdClass(); }")]
    public void EnumForbiddenMagicMethod_Reports4024(string method)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            enum Suit {
                case Hearts;

                {{method}}
            }
            """);

        var methodName = method.Split("function ")[1].Split('(')[0];
        var errors = Errors(diagnostics, MessageCode.CheckerEnumMethodNotAllowed);
        errors.Should().ContainSingle();
        errors[0].Message.Should().Contain($"`{methodName}`");
    }

    [Fact]
    public void EnumAllowedMagicMethods_AreAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit {
                case Hearts;

                public function __call(string $name, array<int, mixed> $arguments): mixed { return null; }
                public static function __callStatic(string $name, array<int, mixed> $arguments): mixed { return null; }
                public function __invoke(): string { return 'hearts'; }
            }
            """);

        Errors(diagnostics, MessageCode.CheckerEnumMethodNotAllowed).Should().BeEmpty();
    }

    [Theory]
    [InlineData("8.3")]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void OverrideOnClassConstantAndEnumCase_Reports4127(string phpVersion)
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public const int C = 1;
            }
            class Child extends Base {
                #[\Override]
                public const int C = 2;
            }
            interface HasA {
                public const string A = 'a';
            }
            enum Letter implements HasA {
                #[\Override]
                case B;
            }
            """, phpVersion);

        var errors = Errors(diagnostics, MessageCode.CheckerAttributeTargetMismatch);
        errors.Should().HaveCount(2);
        errors.Should().Contain(d => d.Message.Contains("class constant"));
        errors.Should().Contain(d => d.Message.Contains("enum case"));
    }

    [Fact]
    public void DelayedTargetValidation_OverrideOnClassConstantAndEnumCase_Php85_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public const int C = 1;
            }
            class Child extends Base {
                #[\DelayedTargetValidation]
                #[\Override]
                public const int C = 2;
            }
            interface HasA {
                public const string A = 'a';
            }
            enum Letter implements HasA {
                #[\DelayedTargetValidation]
                #[\Override]
                case B;
            }
            """, phpVersion: "8.5");

        Errors(diagnostics, MessageCode.CheckerAttributeTargetMismatch).Should().BeEmpty();
        Errors(diagnostics, MessageCode.CheckerOverrideNotOverriding).Should().BeEmpty();
    }

    [Fact]
    public void DelayedTargetValidation_OverrideOnClassConstant_Php84_Reports4127()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public const int C = 1;
            }
            class Child extends Base {
                #[\DelayedTargetValidation]
                #[\Override]
                public const int C = 2;
            }
            """, phpVersion: "8.4");

        Errors(diagnostics, MessageCode.CheckerAttributeTargetMismatch).Should().ContainSingle();
    }

    private static List<IDiagnostic> Errors(DiagnosticBag diagnostics, MessageCode code)
        => diagnostics.Errors.Where(d => d.Code == code).ToList();

    private static DiagnosticBag CompileAndCheck(string content, string phpVersion = "8.5")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: phpVersion, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(
                result.Diagnostics, symbolTree, result.GlobalScope!, options.Checker);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
