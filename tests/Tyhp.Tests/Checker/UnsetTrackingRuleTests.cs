using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class UnsetTrackingRuleTests
{
    [Fact]
    public void Check_UnsetTypedLocal_ThenRead_Reports4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): int {
                int $x = 1;
                unset($x);
                return $x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_UnsetTypedLocal_ThenReassign_DoesNotReport4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): int {
                int $x = 1;
                unset($x);
                $x = 2;
                return $x;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_UnsetTypedLocal_IssetGuard_DoesNotReport4014()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): int {
                int $x = 1;
                unset($x);
                if (isset($x)) {
                    return $x;
                }
                return 0;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_UnsetTypedPropertyWithoutAllowUnset_Reports4158()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value = 0;
                public function clear(): void {
                    unset($this->value);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnsetTypedPropertyWithoutAllowUnset);
    }

    [Fact]
    public void Check_UnsetAllowUnsetProperty_ThenRead_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\AllowUnset]
                private int $value = 0;
                public function clearAndRead(): int {
                    unset($this->value);
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnsetTypedPropertyWithoutAllowUnset);
    }

    [Fact]
    public void Check_UnsetAllowUnsetProperty_ThenReassign_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\AllowUnset]
                private int $value = 0;
                public function clearAndSet(int $v): int {
                    unset($this->value);
                    $this->value = $v;
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnsetTypedPropertyWithoutAllowUnset);
    }

    [Fact]
    public void Check_UnsetAllowUnsetProperty_NullCoalesceGuard_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\AllowUnset]
                private int $value = 0;
                public function clearAndRead(): int {
                    unset($this->value);
                    return $this->value ?? 0;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_AllowUnsetProperty_InstanceMethodEntry_Reports4157()
    {
        // Cross-method: AllowUnset props are not definitely initialized at method entry.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\AllowUnset]
                private int $value = 0;
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_AllowUnsetProperty_IssetGuard_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\AllowUnset]
                private int $value = 0;
                public function get(): int {
                    if (isset($this->value)) {
                        return $this->value;
                    }
                    return 0;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void SCRATCH_UnsetAllowUnsetProperty_CoalesceAssignGuard_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\AllowUnset]
                private int $value = 0;
                public function clearAndCoalesceAssign(): int {
                    unset($this->value);
                    $this->value ??= 5;
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void SCRATCH_UnsetMultipleTargetsInOneStatement_BothCleared()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\AllowUnset]
                private int $a = 0;
                #[\Tyhp\AllowUnset]
                private int $b = 0;
                public function clear(): void {
                    unset($this->a, $this->b);
                }
                public function readA(): int {
                    return $this->a;
                }
            }
            """);

        // clear() itself does not read after unset, but readA at method-entry should
        // still be flagged since AllowUnset props are never definite at entry.
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void SCRATCH_UnsetPromotedAllowUnsetProperty_ThenRead_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public function __construct(
                    #[\Tyhp\AllowUnset]
                    public int $value = 0,
                ) {}
                public function clearAndRead(): int {
                    unset($this->value);
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void SCRATCH_UnsetPromotedPropertyWithoutAllowUnset_Reports4158()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public function __construct(public int $value = 0) {}
                public function clear(): void {
                    unset($this->value);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnsetTypedPropertyWithoutAllowUnset);
    }

    [Fact]
    public void SCRATCH_UnsetInLoopBody_ThenReadAfterLoop_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\AllowUnset]
                private int $value = 0;
                public function clearInLoop(array $items): int {
                    foreach ($items as $item) {
                        unset($this->value);
                    }
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void SCRATCH_UnsetInTryBlock_ThenReadInFinally_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\AllowUnset]
                private int $value = 0;
                public function clearInTry(): int {
                    try {
                        unset($this->value);
                    } finally {
                        return $this->value;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void SCRATCH_UnsetNonThisProperty_NotFlagged()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value = 0;
            }
            function demo(Box $b): void {
                unset($b->value);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnsetTypedPropertyWithoutAllowUnset);
    }

    [Fact]
    public void SCRATCH_AllowUnsetOnClass_TargetMismatchReports()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\AllowUnset]
            class Box {
                private int $value = 0;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerAttributeTargetMismatch);
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
                PhpVersion = "8.2",
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
