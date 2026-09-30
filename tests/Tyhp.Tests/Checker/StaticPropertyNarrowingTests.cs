using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Null-check-then-assign merge for enclosing-class static properties
/// (<c>self::$instance</c> singleton <c>get()</c>) and the matching <c>$this->prop</c> join.
/// </summary>
[Trait("Category", "Checker")]
public class StaticPropertyNarrowingTests
{
    [Fact]
    public void Check_SingletonGet_NullCheckThenAssign_ReturnIsNonNull()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class S {
                private static ?self $instance = null;
                public static function get(): self {
                    if (self::$instance === null) {
                        self::$instance = new self();
                    }
                    return self::$instance;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"singleton get() should narrow self::$instance after the assign-if: {Describe(errors)}");
        errors.Should().BeEmpty($"singleton get() should type-check: {Describe(errors)}");
    }

    [Fact]
    public void Check_SingletonGet_NullCheckWithoutAssign_StillNullable()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class S {
                private static ?self $instance = null;
                public static function get(): self {
                    if (self::$instance === null) {
                    }
                    return self::$instance;
                }
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"an if that does not assign must leave self::$instance nullable: {Describe(errors)}");
    }

    [Fact]
    public void Check_InstanceProperty_NullCheckThenAssign_ReturnIsNonNull()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                private ?self $instance = null;
                public function get(): self {
                    if ($this->instance === null) {
                        $this->instance = new self();
                    }
                    return $this->instance;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"instance null-check-then-assign should narrow on merge: {Describe(errors)}");
        errors.Should().BeEmpty($"instance singleton-style get() should type-check: {Describe(errors)}");
    }

    [Fact]
    public void Check_ClassNameStatic_NullCheckThenAssign_ReturnIsNonNull()
    {
        // `S::$instance` (the enclosing class referenced by its own literal name) narrows the
        // same as `self::$instance`.
        var errors = CompileAndCheck("""
            <?tyhp
            class S {
                private static ?S $instance = null;
                public static function get(): S {
                    if (S::$instance === null) {
                        S::$instance = new S();
                    }
                    return S::$instance;
                }
            }
            """);

        errors.Should().BeEmpty($"ClassName::$instance should narrow the same as self::$instance: {Describe(errors)}");
    }

    [Fact]
    public void Check_ParentStatic_NullCheckThenAssign_StaysNullable()
    {
        // `parent::$x` must not be pulled into the enclosing class's PropertyInit map: it is a
        // distinct-looking access to a base-class slot, and narrowing it via the child's
        // control-flow state would be unsound if a sibling subclass narrowed the same slot
        // differently. The guard is conservative and leaves the read nullable.
        var errors = CompileAndCheck("""
            <?tyhp
            class Base {
                protected static ?Base $shared = null;
            }
            class Impl extends Base {
                public static function get(): Base {
                    if (parent::$shared === null) {
                        parent::$shared = new Base();
                    }
                    return parent::$shared;
                }
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"parent::$shared must not be narrowed via the child's PropertyInit map: {Describe(errors)}");
    }

    [Fact]
    public void Check_OtherClassStatic_NullCheckThenAssign_StaysNullable()
    {
        // A static property on an unrelated class must not be narrowed via this class's
        // PropertyInit map either.
        var errors = CompileAndCheck("""
            <?tyhp
            class Other {
                public static ?Other $x = null;
            }
            class Impl {
                public static function get(): Other {
                    if (Other::$x === null) {
                        Other::$x = new Other();
                    }
                    return Other::$x;
                }
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"Other::$x must not be narrowed via Impl's PropertyInit map: {Describe(errors)}");
    }

    [Fact]
    public void Check_SingletonGet_AssignOnUnrelatedCondition_OneSideUnrefined_StaysNullable()
    {
        // Merge soundness: the then-branch refines `self::$instance` (assignment), but the
        // implicit else-branch (condition false, body skipped) never touched the slot at all —
        // it carries no refinement forward. The merge must drop narrowing rather than inventing
        // an unsound non-null result from only one refined side.
        var errors = CompileAndCheck("""
            <?tyhp
            class S {
                private static ?self $instance = null;
                public static function get(bool $flag): self {
                    if ($flag) {
                        self::$instance = new self();
                    }
                    return self::$instance;
                }
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"one-sided refinement must not invent unsound non-null narrowing on merge: {Describe(errors)}");
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
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

            return result.Diagnostics.Errors
                .Where(e => e.FileName is not null
                    && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
