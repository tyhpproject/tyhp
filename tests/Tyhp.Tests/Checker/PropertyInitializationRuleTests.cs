using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class PropertyInitializationRuleTests
{
    [Fact]
    public void Check_UninitializedTypedPropertyRead_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_PropertyWithInitializer_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value = 0;
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_PromotedProperty_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public function __construct(private int $value) {}
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_AssignedInConstructor_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(int $v) {
                    $this->value = $v;
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_AssignedOnlyInOneConstructorBranch_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(bool $cond, int $v) {
                    if ($cond) {
                        $this->value = $v;
                    }
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_AssignedInBothConstructorBranches_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(bool $cond, int $v) {
                    if ($cond) {
                        $this->value = $v;
                    } else {
                        $this->value = 0;
                    }
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_ReadBeforeAssignInConstructor_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(int $v) {
                    int $x = $this->value;
                    $this->value = $v;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_NullCoalesceGuardsUninitializedRead_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function get(): int {
                    return $this->value ?? 0;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_NullableWithoutDefault_StillReports4157()
    {
        // PHP: `?int $x` without `= null` is still uninitialized until assigned.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private ?int $value;
                public function get(): ?int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_NullableWithNullDefault_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private ?int $value = null;
                public function get(): ?int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_HelperAssignmentInConstructor_DoesNotCount()
    {
        // Decision: assignment via constructor-called helper does not count (intraprocedural).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(int $v) {
                    $this->init($v);
                }
                private function init(int $v): void {
                    $this->value = $v;
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_SimpleAssignWriteTarget_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct(int $v) {
                    $this->value = $v;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_CompoundAssignOnUninitializedProperty_Reports4157()
    {
        // `+=` (and other read-then-write compound operators) read the property before writing it,
        // so an uninitialized target still throws in PHP — unlike plain `=` / `??=`, this must NOT
        // be treated as an initializing write (regression: it previously marked the property
        // definitely-initialized before the implicit read was checked, suppressing 4157).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value;
                public function __construct() {
                    $this->value += 1;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_CompoundAssignOnAlreadyInitializedProperty_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $value = 0;
                public function __construct() {
                    $this->value += 1;
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_InheritedAllowUnsetProperty_UnguardedRead_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                #[\Tyhp\AllowUnset]
                public int $value = 0;
            }
            class Derived extends Base {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        // AllowUnset properties are never definitely-initialized at instance-method entry (see
        // Check_AllowUnsetProperty_InstanceMethodEntry_Reports4157) — the same must hold when the
        // property is inherited.
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_InheritedPropertyWithInitializer_DoesNotReport4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value = 0;
            }
            class Derived extends Base {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_SubclassConstructorInitializesInheritedProperty_DoesNotReport4157()
    {
        // Derived ctor assigns an inherited slot that Base left uninitialized. Credit must be
        // recorded on Derived without mutating Base's shared MayBeUninitializedAfterConstruction.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value;
            }
            class Derived extends Base {
                public function __construct(int $v) {
                    $this->value = $v;
                }
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_InheritedUninitializedProperty_UnguardedRead_Reports4157()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value;
            }
            class Derived extends Base {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_GrandchildNoOwnCtor_InheritsInitializingMiddleConstructor_DoesNotReport4157()
    {
        // Leaf declares no constructor of its own, so `new Leaf()` runs Middle's inherited
        // constructor at runtime. The credit Middle earns for initializing Base's `$value` must
        // propagate down to Leaf (and any further descendant without its own constructor).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value;
            }
            class Middle extends Base {
                public function __construct(int $v) {
                    $this->value = $v;
                }
            }
            class Leaf extends Middle {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_GreatGrandchildNoOwnCtorChain_InheritsInitializingConstructor_DoesNotReport4157()
    {
        // Four levels, with two consecutive descendants (Leaf, Sprout) declaring no constructor of
        // their own — the credit must chain through each level, not just skip one.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value;
            }
            class Middle extends Base {
                public function __construct(int $v) {
                    $this->value = $v;
                }
            }
            class Leaf extends Middle {
            }
            class Sprout extends Leaf {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_GrandchildNoOwnCtor_MiddleLeavesPropertyUninitialized_StillReports4157()
    {
        // Middle has its own constructor but never assigns `$value` — Leaf (no own constructor)
        // must still be flagged; the delegation must not become unconditionally permissive.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public int $value;
            }
            class Middle extends Base {
                public function __construct() {
                }
            }
            class Leaf extends Middle {
                public function get(): int {
                    return $this->value;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
    }

    [Fact]
    public void Check_PrivateInheritedProperty_NotTrackedOnSubclass()
    {
        // Base's private $value is not visible from Derived; Derived's get() must not inherit a
        // spurious PropertyInit entry that would affect unrelated diagnostics.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                private int $value;
                public function getBase(): int {
                    return $this->value;
                }
            }
            class Derived extends Base {
                public function get(): int {
                    return 0;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized);
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
