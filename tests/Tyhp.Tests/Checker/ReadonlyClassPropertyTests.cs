using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Every instance property of a <c>readonly class</c>, declared or promoted, is readonly without
/// repeating the modifier, and the readonly rules apply to it exactly as to an explicit one.
/// Also covers readonly writes from outside the declaring class (TYHP4021).
/// </summary>
[Trait("Category", "Checker")]
public class ReadonlyClassPropertyTests
{
    [Fact]
    public void ReadonlyClass_PlainAndPromotedProperties_AreAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Point {
                public int $x;
                public readonly int $y;

                public function __construct(public int $z, protected readonly int $w) {
                    $this->x = 1;
                    $this->y = 2;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join(", ", diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("z")]
    public void ReadonlyClass_ImplicitProperty_WriteThroughOtherReceiver_Reports4021(string property)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            readonly class Point {
                public int $x;

                public function __construct(public int $z) {
                    $this->x = 1;
                }

                public function copyInto(Point $other): void {
                    $other->{{property}} = 5;
                }
            }
            """);

        var errors = Errors(diagnostics, MessageCode.CheckerReadonlyPropertyReassigned);
        errors.Should().ContainSingle();
        errors[0].Message.Should().Contain($"`{property}`");
    }

    [Fact]
    public void ReadonlyClass_ImplicitProperty_WriteFromFunctionAndFileScope_Reports4021()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Point {
                public int $x;

                public function __construct(public int $z) {
                    $this->x = 1;
                }
            }

            function move(Point $p): void {
                $p->x = 3;
                $p->z = 4;
            }

            $q = new Point(1);
            $q->z = 2;
            """);

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyReassigned)
            .Select(d => d.Message)
            .Should().HaveCount(3)
            .And.Contain(m => m.Contains("`x`"))
            .And.Contain(m => m.Contains("`z`"));
    }

    [Fact]
    public void ExplicitReadonly_WriteFromUnrelatedClass_Reports4021()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Account {
                public function __construct(public readonly int $balance) {}
            }

            class Teller {
                public function adjust(Account $account): void {
                    $account->balance = 0;
                }
            }
            """);

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyReassigned).Should().ContainSingle();
    }

    [Fact]
    public void ReadonlyWrite_FromSubclass_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Base {
                public readonly int $x;
            }

            class Child extends Base {
                public static function make(): Child {
                    $child = new Child();
                    $child->x = 1;
                    return $child;
                }
            }
            """, phpVersion: "8.4");

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyReassigned).Should().BeEmpty();
    }

    [Fact]
    public void PublicSetReadonly_WriteFromFunction_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Slot {
                public public(set) readonly int $value;
            }

            function fill(Slot $slot): void {
                $slot->value = 1;
            }
            """, phpVersion: "8.4");

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyReassigned).Should().BeEmpty();
    }

    [Fact]
    public void MutableProperty_WriteFromFunction_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Counter {
                public int $count = 0;
            }

            function bump(Counter $counter): void {
                $counter->count = $counter->count + 1;
            }
            """);

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyReassigned).Should().BeEmpty();
    }

    [Fact]
    public void ReadonlyClass_StaticProperty_Reports4109()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Registry {
                public static int $count;
            }
            """);

        var errors = Errors(diagnostics, MessageCode.CheckerReadonlyClassStaticProperty);
        errors.Should().ContainSingle();
        errors[0].Message.Should().Contain("`Registry`").And.Contain("`$count`");
    }

    [Fact]
    public void ReadonlyClass_HookedProperty_Reports4155()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Widget {
                public string $name {
                    get {
                        return 'widget';
                    }
                }
            }
            """, phpVersion: "8.4");

        Errors(diagnostics, MessageCode.CheckerHookedPropertyReadonly).Should().ContainSingle();
    }

    [Fact]
    public void ReadonlyClass_HookedPromotedParameter_Reports4155()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Widget {
                public function __construct(
                    public string $name {
                        get {
                            return $this->name;
                        }
                    }
                ) {}
            }
            """, phpVersion: "8.4");

        Errors(diagnostics, MessageCode.CheckerHookedPropertyReadonly).Should().ContainSingle();
    }

    [Fact]
    public void ReadonlyClass_ImplicitPromotedPropertyWithParameterDefault_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Pair {
                public function __construct(public int $left = 0) {}
            }
            """, phpVersion: "8.2");

        Errors(diagnostics, MessageCode.CheckerReadonlyPropertyDefaultRequiresPhp86).Should().BeEmpty();
    }

    [Fact]
    public void ReadonlyClass_UntypedProperties_AreRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Loose {
                public $x;

                public function __construct(public $y) {}
            }
            """);

        Errors(diagnostics, MessageCode.CheckerVariableTypeRequired)
            .Select(d => d.Message)
            .Should().HaveCount(2)
            .And.Contain(m => m.Contains("`$x`"))
            .And.Contain(m => m.Contains("`$y`"));
    }

    [Fact]
    public void ReadonlyClass_CloneWithImplicitProperty_BelowPhp85_Reports4401()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            readonly class Point {
                public function __construct(public int $x) {}
            }

            function shifted(Point $p): Point {
                return clone $p with [x => 2];
            }
            """, phpVersion: "8.4");

        Errors(diagnostics, MessageCode.CheckerReadonlyClassCloneWithRequiresPhp85).Should().ContainSingle();
        Errors(diagnostics, MessageCode.CheckerCloneWithReadonlyRequiresConfig).Should().BeEmpty();
    }

    [Fact]
    public void ReadonlyClass_TraitWithNonReadonlyProperty_Reports4380()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Counts {
                public int $hits;
            }

            readonly class UsesCounts {
                use Counts;
            }
            """);

        var errors = Errors(diagnostics, MessageCode.CheckerReadonlyClassTraitProperty);
        errors.Should().ContainSingle();
        errors[0].Message.Should().Contain("`UsesCounts`")
            .And.Contain("`Counts::$hits`");
    }

    [Fact]
    public void ReadonlyClass_TraitWithStaticProperty_Reports4380()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Counts {
                public static int $hits = 0;
            }

            readonly class UsesCounts {
                use Counts;
            }
            """);

        var errors = Errors(diagnostics, MessageCode.CheckerReadonlyClassTraitProperty);
        errors.Should().ContainSingle();
        errors[0].Message.Should().Contain("`Counts::$hits`");
    }

    [Fact]
    public void ReadonlyClass_TraitWithReadonlyProperty_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Counts {
                public readonly int $hits;
            }

            readonly class UsesCounts {
                use Counts;

                public function __construct() {
                    $this->hits = 1;
                }
            }
            """);

        Errors(diagnostics, MessageCode.CheckerReadonlyClassTraitProperty).Should().BeEmpty();
        diagnostics.Errors.Should().BeEmpty(
            string.Join(", ", diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void NonReadonlyClass_TraitWithNonReadonlyProperty_IsAccepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Counts {
                public int $hits = 0;
            }

            class UsesCounts {
                use Counts;
            }
            """);

        Errors(diagnostics, MessageCode.CheckerReadonlyClassTraitProperty).Should().BeEmpty();
        diagnostics.Errors.Should().BeEmpty(
            string.Join(", ", diagnostics.Errors.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void ReadonlyClass_TraitWithSeveralBadProperties_ReportsEach()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Counts {
                public int $hits;
                public static int $total = 0;
                public readonly int $kept;
            }

            readonly class UsesCounts {
                use Counts;

                public function __construct() {
                    $this->kept = 1;
                }
            }
            """);

        Errors(diagnostics, MessageCode.CheckerReadonlyClassTraitProperty)
            .Select(d => d.Message)
            .Should().HaveCount(2)
            .And.Contain(m => m.Contains("`Counts::$hits`"))
            .And.Contain(m => m.Contains("`Counts::$total`"))
            .And.NotContain(m => m.Contains("$kept"));
    }

    [Fact]
    public void ReadonlyClass_NestedTraitProperty_Reports4380()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Inner {
                public int $hits;
            }

            trait Outer {
                use Inner;
            }

            readonly class UsesCounts {
                use Outer;
            }
            """);

        var errors = Errors(diagnostics, MessageCode.CheckerReadonlyClassTraitProperty);
        errors.Should().ContainSingle();
        errors[0].Message.Should().Contain("`Inner::$hits`");
    }

    [Fact]
    public void ReadonlyClass_PromotedTraitProperty_Reports4380UnlessReadonly()
    {
        var rejected = CompileAndCheck("""
            <?tyhp
            trait Counts {
                public function __construct(public int $hits) {}
            }

            readonly class UsesCounts {
                use Counts;
            }
            """);

        Errors(rejected, MessageCode.CheckerReadonlyClassTraitProperty)
            .Should().ContainSingle()
            .Which.Message.Should().Contain("`Counts::$hits`");

        var accepted = CompileAndCheck("""
            <?tyhp
            trait Counts {
                public function __construct(public readonly int $hits) {}
            }

            readonly class UsesCounts {
                use Counts;
            }
            """);

        Errors(accepted, MessageCode.CheckerReadonlyClassTraitProperty).Should().BeEmpty();
    }

    [Fact]
    public void ReadonlyClass_TraitProperty_BelowPhp82_DoesNotReport4380()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Counts {
                public int $hits;
                public static int $total = 0;
            }

            readonly class UsesCounts {
                use Counts;
            }
            """, phpVersion: "8.1");

        Errors(diagnostics, MessageCode.CheckerReadonlyClassTraitProperty).Should().BeEmpty();
    }

    [Fact]
    public void AnonymousReadonlyClass_TraitWithNonReadonlyProperty_Reports4380()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Counts {
                public int $hits;
            }

            function make(): object {
                return new readonly class {
                    use Counts;
                };
            }
            """);

        var errors = Errors(diagnostics, MessageCode.CheckerReadonlyClassTraitProperty);
        errors.Should().ContainSingle();
        errors[0].Message.Should().Contain("`anonymous class`")
            .And.Contain("`Counts::$hits`");
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
