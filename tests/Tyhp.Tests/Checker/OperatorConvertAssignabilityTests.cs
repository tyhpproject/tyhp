using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 11 §8: checker accepts call/return/<c>new</c> sites that AliasConverter rewrites via
/// <c>operator convert</c> (convert-to / convert-from). Does not implement Story 31 Idea 2
/// <c>*Convertible</c> acceptance.
/// </summary>
[Trait("Category", "Checker")]
public class OperatorConvertAssignabilityTests
{
    [Fact]
    public void Check_ConvertTo_CallArgument_Accepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            function takeInt(int $n): void {}
            function pass(Money $a): void {
                takeInt($a);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_ConvertTo_Return_Accepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            function asInt(Money $a): int {
                return $a;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_ConvertFrom_CallArgument_Accepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(int $value) {
                    $newObj = new static();
                    $newObj->amount = $value;
                    return $newObj;
                }
            }
            function takeMoney(Money $m): void {}
            function pass(int $n): void {
                takeMoney($n);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_ConvertFrom_Return_Accepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(int $value) {
                    $newObj = new static();
                    $newObj->amount = $value;
                    return $newObj;
                }
            }
            function asMoney(int $n): Money {
                return $n;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_ConvertTo_ConstructorArgument_Accepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            class Wallet {
                public int $balance;
                function __construct(int $balance) {
                    $this->balance = $balance;
                }
            }
            function pass(Money $m): Wallet {
                return new Wallet($m);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_ConvertFrom_ConstructorArgument_Accepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(int $value) {
                    $newObj = new static();
                    $newObj->amount = $value;
                    return $newObj;
                }
            }
            class Wallet {
                public Money $balance;
                function __construct(Money $balance) {
                    $this->balance = $balance;
                }
            }
            function pass(int $n): Wallet {
                return new Wallet($n);
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_WithoutConvert_CallArgument_StillRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            function takeInt(int $n): void {}
            function pass(Money $a): void {
                takeInt($a);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_ConvertTo_PlainAssignment_StillRejected()
    {
        // Emit does not rewrite plain assignments — checker must not accept convert there.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            function pass(Money $a): void {
                int $n = $a;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_ConvertTo_WrongTarget_StillRejected()
    {
        // convert(self): int does not satisfy a string parameter.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            function takeString(string $s): void {}
            function pass(Money $a): void {
                takeString($a);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_ConvertTo_CallableVariableInvocation_StillRejected()
    {
        // `$fn(...)` invokes an arbitrary runtime callable value — AliasConverter cannot statically
        // resolve its declared parameters (`TryResolveCalleeParameters` only handles named
        // functions/methods), so it never inserts an implicit-convert rewrite here. The checker must
        // not accept convert at this call form either, or it would pass while emit still hands the
        // unconverted object to PHP (runtime TypeError under strict_types).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            function pass(Money $m): void {
                $fn = function(int $n): void {};
                $fn($m);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType);
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
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };
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
