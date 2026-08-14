using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Coverage for <c>$this->prop</c> and <c>$var->prop</c> control-flow narrowing (null-check /
/// instanceof / isset / type-guard). <c>$var->prop</c> uses <c>MemberAccessNarrowing</c>
/// (IndexAccessNarrowing-style structural keys). See RESOLVED_BUGS.md.
/// </summary>
[Trait("Category", "Checker")]
public class PropertyNarrowingRuleTests
{
    private const string ExceptionsTyhpdef = """
        <?tyhpdef

        interface Stringable
        {
            public function __toString(): string;
        }
        interface Throwable extends \Stringable
        {
            public function getMessage(): string;
        }
        class Exception implements \Stringable, \Throwable
        {
            public function __construct(string $message): void;
            public function getMessage(): string;
            public function __toString(): string;
        }
        """;

    [Fact]
    public void Check_PropertyNullGuard_EarlyReturn_NarrowsFallThrough()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(): \Throwable {
                    if ($this->err === null) {
                        throw new \Exception('none');
                    }
                    return $this->err;
                }
            }
            """);
        errors.Should().BeEmpty($"early-return null guard should narrow fall-through: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyNullGuard_IfElseBothArms_NoTypeMismatch()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(): \Throwable {
                    if ($this->err !== null) {
                        return $this->err;
                    } else {
                        return new \Exception('default');
                    }
                }
            }
            """);
        errors.Should().BeEmpty($"if/else both arms: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyNarrowing_ClearedAfterMerge_StillRequiresGuard()
    {
        // After the if/else exits (no throw/return in either arm), reading `$this->err` on a
        // required-non-null target must still fail — narrowing must not leak past the merge point.
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(): void {
                    if ($this->err !== null) {
                        echo 'a';
                    } else {
                        echo 'b';
                    }
                    \Throwable $x = $this->err;
                }
            }
            """);
        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"narrowing must be cleared after merge (expected type mismatch): {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyNullGuard_ContinueInLoop_NarrowsRestOfIteration()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(array<int> $items): void {
                    foreach ($items as $item) {
                        if ($this->err === null) {
                            continue;
                        }
                        throw $this->err;
                    }
                }
            }
            """);
        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"continue guard should narrow rest of iteration: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyNullGuard_BreakInLoop_NarrowsRestOfIteration()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(array<int> $items): void {
                    foreach ($items as $item) {
                        if ($this->err === null) {
                            break;
                        }
                        throw $this->err;
                    }
                }
            }
            """);
        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"break guard should narrow rest of iteration: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyNullGuard_NestedConditions_Narrows()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public bool $flag = false;
                public function demo(): void {
                    if ($this->flag) {
                        if ($this->err !== null) {
                            throw $this->err;
                        }
                    }
                }
            }
            """);
        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"nested conditions should still narrow: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyCoalesceThenNullGuardReturn_Idiom()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(\Throwable $e): void {
                    $this->err ??= $e;
                    if ($this->err === null) {
                        return;
                    }
                    throw $this->err;
                }
            }
            """);
        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"coalesce then null-guard-return idiom: {Describe(errors)}");
    }

    [Fact]
    public void Check_NonThisProperty_NullGuard_Narrows()
    {
        // `$var->prop !== null` now narrows via MemberAccessNarrowing (IndexAccessNarrowing-style).
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
            }
            function demo(Holder $h): void {
                if ($h->err !== null) {
                    \Throwable $x = $h->err;
                }
            }
            """);
        errors.Should().BeEmpty($"$var->prop null guard should narrow: {Describe(errors)}");
    }

    [Fact]
    public void Check_NonThisProperty_TernaryNullGuard_Narrows()
    {
        // ExpressionSerializer-style: `$node->ifTrue !== null ? f($node->ifTrue) : null`
        var errors = CompileAndCheck("""
            <?tyhp
            class Node {
                public ?Node $child = null;
            }
            function take(Node $n): Node { return $n; }
            function demo(Node $node): ?Node {
                return $node->child !== null ? take($node->child) : null;
            }
            """);
        errors.Should().BeEmpty($"ternary $var->prop null guard should narrow: {Describe(errors)}");
    }

    [Fact]
    public void Check_NonThisProperty_Instanceof_Narrows()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Base {}
            class Child extends Base {}
            class Holder {
                public Base $value;
                public function __construct(Base $value) { $this->value = $value; }
            }
            function demo(Holder $h): Child {
                if ($h->value instanceof Child) {
                    return $h->value;
                }
                throw new \Exception('no');
            }
            """);
        errors.Should().BeEmpty($"$var->prop instanceof should narrow: {Describe(errors)}");
    }

    [Fact]
    public void Check_NonThisProperty_Narrowing_ClearedOnWrite()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
            }
            function demo(Holder $h): void {
                if ($h->err !== null) {
                    $h->err = null;
                    \Throwable $x = $h->err;
                }
            }
            """);
        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"write must invalidate $var->prop narrowing: {Describe(errors)}");
    }

    [Fact]
    public void Check_NonThisProperty_Narrowing_ClearedOnReceiverReassign()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
            }
            function demo(Holder $h, Holder $other): void {
                if ($h->err !== null) {
                    $h = $other;
                    \Throwable $x = $h->err;
                }
            }
            """);
        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"receiver reassign must invalidate $var->prop narrowing: {Describe(errors)}");
    }

    [Fact]
    public void Check_GenericProperty_NullGuard_Narrows()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Box<T> {
                private ?T $value = null;
                public function __construct(private T $default) {}
                public function get(): T {
                    if ($this->value !== null) {
                        return $this->value;
                    }
                    return $this->default;
                }
            }
            """);
        errors.Should().BeEmpty($"generic property null guard should narrow: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyNegatedIsset_EarlyReturn_NarrowsFallThrough()
    {
        // `if (!isset($this->err)) { return; }` — the `!` unwrap flips polarity so the isset
        // narrowing (positive-only) applies to the fall-through arm, correctly narrowing after.
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(): void {
                    if (!isset($this->err)) {
                        return;
                    }
                    throw $this->err;
                }
            }
            """);
        errors.Should().BeEmpty($"negated isset + early return should narrow fall-through: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyInstanceofNegativeBranch_StaysNullable()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Special extends \Exception {}
            class Holder {
                public ?\Exception $err = null;
                public function demo(): void {
                    if ($this->err instanceof Special) {
                        return;
                    }
                    \Exception $x = $this->err;
                }
            }
            """);
        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"negative instanceof arm should stay nullable: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyUnset_ClearsNarrowing()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                #[\Tyhp\AllowUnset]
                public \Exception $err;
                public function __construct(\Exception $e) {
                    $this->err = $e;
                }
                public function demo(): void {
                    if ($this->err instanceof \Exception) {
                        unset($this->err);
                        \Exception $x = $this->err;
                    }
                }
            }
            """);
        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerPropertyPossiblyUninitialized,
            $"unset should clear narrowing: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyReassignInsideGuard_ResetsNarrowing()
    {
        // Assigning a nullable value back inside the guarded branch must invalidate the earlier
        // narrowing for subsequent reads.
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(?\Throwable $maybe): void {
                    if ($this->err !== null) {
                        $this->err = $maybe;
                        throw $this->err;
                    }
                }
            }
            """);
        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"re-assign to nullable inside guard must invalidate narrowing: {Describe(errors)}");
    }

    [Fact]
    public void Check_InheritedPropertyNullGuard_Narrows()
    {
        // Top-type #9: PropertyInit must seed inherited base-class properties so `$this->prop`
        // narrowing works the same as for own-class properties.
        var errors = CompileAndCheck("""
            <?tyhp
            class Base {
                public ?\Throwable $err = null;
            }
            class Derived extends Base {
                public function demo(): \Throwable {
                    if ($this->err !== null) {
                        return $this->err;
                    }
                    return new \Exception('default');
                }
            }
            """);
        errors.Should().BeEmpty($"inherited property null guard should narrow: {Describe(errors)}");
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

        var tyhpdefPath = Path.Combine(tempDir, "exceptions.tyhpdef");
        File.WriteAllText(tyhpdefPath, ExceptionsTyhpdef);

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
            var result = compilationService.ParseFiles([tyhpdefPath, filePath], options);
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
