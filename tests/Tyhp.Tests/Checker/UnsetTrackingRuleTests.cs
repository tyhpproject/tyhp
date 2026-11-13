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
    public void Check_UnsetTypedLocal_Reports4158_AndDoesNotClearAssignment()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): string {
                string $s = 'a';
                unset($s);
                return $s;
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUnsetStorageSlot
            && d.Message.Contains("$s", StringComparison.Ordinal)
            && !d.Message.Contains("AllowUnset", StringComparison.Ordinal));
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyUndefined);
    }

    [Fact]
    public void Check_NullableLocalAssignedNull_IsOk()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): ?string {
                ?string $s = 'a';
                $s = null;
                return $s;
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_UnsetParameter_Reports4158()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(string $s): void {
                unset($s);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
    }

    [Fact]
    public void Check_UnsetTypedInstanceProperty_Reports4158()
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

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUnsetStorageSlot
            && d.Message.Contains("$this->value", StringComparison.Ordinal)
            && !d.Message.Contains("AllowUnset", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_AllowUnsetAttribute_DoesNotMakeUnsetLegal()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\AllowUnset]
                private int $value = 0;
                public function clear(): void {
                    unset($this->value);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUnsetStorageSlot
            && !d.Message.Contains("AllowUnset", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_UnsetOtherInstanceProperty_Reports4158()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;
            }
            function demo(Box $b): void {
                unset($b->value);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
    }

    [Fact]
    public void Check_UnsetPromotedProperty_Reports4158()
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

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
    }

    [Fact]
    public void Check_UnsetReadonlyProperty_Reports4158()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public readonly string $name;
                public function __construct(string $name) {
                    $this->name = $name;
                    unset($this->name);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
    }

    [Fact]
    public void Check_UnsetHookedProperty_Reports4158()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public string $name {
                    get => 'a';
                }
                public function clear(): void {
                    unset($this->name);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
    }

    [Fact]
    public void Check_UnsetStaticProperty_Reports4158()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public static string $staticProp = '';
                public static function clear(): void {
                    unset(Foo::$staticProp);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUnsetStorageSlot
            && d.Message.Contains("Foo::$staticProp", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_UnsetSelfStaticProperty_Reports4158()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public static string $staticProp = '';
                public static function clear(): void {
                    unset(self::$staticProp);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
    }

    [Fact]
    public void Check_StaticPropertyWithoutInitializer_Reports4418()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public static string $x;
            }
            """);

        diagnostics.Errors.Should().ContainSingle(d =>
            d.Code == MessageCode.CheckerStaticPropertyRequiresInitializer
            && d.Message.Contains("`$x`", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_ArrayOffsetUnset_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public array<string, int> $items = [];
                public function remove(string $key): void {
                    unset($this->items[$key]);
                }
            }
            class Foo {
                public static array<string, int> $items = [];
                public static function remove(string $key): void {
                    unset(self::$items[$key]);
                    unset(Foo::$items[$key]);
                }
            }
            function demo(array<int, string> $arr, int $i): void {
                unset($arr[$i]);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_NestedArrayOffsetUnset_OnTypedProperty_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public array<string, array<string, int>> $map = [];
                public function remove(string $a, string $b): void {
                    unset($this->map[$a][$b]);
                }
            }
            function demo(Box $box, string $key): void {
                unset($box->map[$key]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_UnsetOptionalStructProperty_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Opt = struct { string $a; ?string $b; };
            function named(Opt $t): void {
                unset($t->b);
            }
            function anonymous(struct { string $a; ?string $b; } $t): void {
                unset($t->b);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
    }

    [Fact]
    public void Check_UnsetPropertyOfNullableReceiver_Reports4158()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;
            }
            function demo(?Box $b): void {
                if ($b !== null) {
                    unset($b->value);
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
    }

    [Fact]
    public void Check_UnsetPropertyWithDynamicName_IsNotAResolvedSlot()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value = 0;
                public function drop(string $name): void {
                    unset($this->{$name});
                    unset($this->$name);
                    unset($this->{'value'});
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
    }

    [Fact]
    public void Check_RejectedUnsetOfProperty_DoesNotClearPropertyInitialization()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public int $value;
                public function __construct(int $value) {
                    $this->value = $value;
                    unset($this->value);
                    echo $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnsetStorageSlot);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_UnsetMixedTargets_ReportsOnlyTheStorageSlot()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(array<int, string> $arr): void {
                string $s = 'a';
                unset($arr[0], $s);
            }
            """);

        diagnostics.Errors.Where(d => d.Code == MessageCode.CheckerUnsetStorageSlot)
            .Should().ContainSingle();
    }

    private static string Describe(DiagnosticBag diagnostics) =>
        string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"));

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
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
