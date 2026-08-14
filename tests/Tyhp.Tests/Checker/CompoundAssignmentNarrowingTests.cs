using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Regressions for compound-assignment operators updating checker tracked variable types /
/// narrowing (FOUND_BUGS Elvis-review compound-assignment High — false TYHP4039 after
/// <c>??=</c> in <c>CancellationToken::cancel</c>).
/// </summary>
[Trait("Category", "Checker")]
public class CompoundAssignmentNarrowingTests
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
        class TypeError extends \Exception
        {
        }
        class ValueError extends \Exception
        {
        }
        class NotThrowable
        {
        }
        """;

    [Fact]
    public void Check_CoalesceAssignThenNullGuardThrow_DoesNotReport4039()
    {
        // Reduced CancellationToken::cancel shape: `??=` must update tracked type so
        // `!== null` narrowing yields a throwable for `throw`.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(\Throwable $e): void {
                ?\Throwable $x = null;
                $x ??= $e;
                if ($x !== null) {
                    throw $x;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"??= then !== null throw must not report TYHP4039: {Describe(errors)}");
    }

    [Fact]
    public void Check_PlainAssignThenNullGuardThrow_DoesNotReport4039()
    {
        // Control: identical shape with plain `=` must stay clean.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(\Throwable $e): void {
                ?\Throwable $x = null;
                $x = $e;
                if ($x !== null) {
                    throw $x;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"plain = then !== null throw must not report TYHP4039: {Describe(errors)}");
    }

    [Fact]
    public void Check_CoalesceAssignInCatchThenNullGuardThrow_DoesNotReport4039()
    {
        // Full CancellationToken::cancel control-flow shape (foreach + try/catch + ??=).
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(array<callable> $callbacks): void {
                ?\Throwable $firstException = null;
                foreach ($callbacks as $callback) {
                    try {
                        $callback();
                    } catch (\Throwable $e) {
                        $firstException ??= $e;
                    }
                }
                if ($firstException !== null) {
                    throw $firstException;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"CancellationToken-style ??= in catch must not report TYHP4039: {Describe(errors)}");
    }

    [Fact]
    public void Check_PlainAssignInCatchThenNullGuardThrow_DoesNotReport4039()
    {
        // Same catch/foreach shape with plain `=` — isolates merge vs ??= inference.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(array<callable> $callbacks): void {
                ?\Throwable $firstException = null;
                foreach ($callbacks as $callback) {
                    try {
                        $callback();
                    } catch (\Throwable $e) {
                        $firstException = $e;
                    }
                }
                if ($firstException !== null) {
                    throw $firstException;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"plain = in catch then !== null throw: {Describe(errors)}");
    }

    [Fact]
    public void Check_CoalesceAssignWithoutLoopThenNullGuardThrow_DoesNotReport4039()
    {
        // ??= inside catch without foreach — narrower control-flow join.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(callable $callback): void {
                ?\Throwable $firstException = null;
                try {
                    $callback();
                } catch (\Throwable $e) {
                    $firstException ??= $e;
                }
                if ($firstException !== null) {
                    throw $firstException;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"??= in catch (no loop) then !== null throw: {Describe(errors)}");
    }

    [Fact]
    public void Check_ConcatAssignThenIsStringNarrowing_DoesNotReportMismatch()
    {
        // Compound ops other than ??= must also refresh tracked type for later narrowing.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(?string $s): string {
                $s ??= '';
                $s .= 'x';
                if (\is_string($s)) {
                    return $s;
                }
                return '';
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"concat-assign then is_string narrowing: {Describe(errors)}");
        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"unexpected throw diagnostic: {Describe(errors)}");
    }

    [Fact]
    public void Check_UnionCatchType_RethrowDoesNotReport4039()
    {
        // Multi-type catch (`catch (\A|\B $e)`) must resolve $e to a real union, not `unresolved`,
        // so an unconditional rethrow of the caught variable stays clean.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(callable $callback): void {
                try {
                    $callback();
                } catch (\TypeError|\ValueError $e) {
                    throw $e;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"union catch type rethrow must not report TYHP4039: {Describe(errors)}");
    }

    [Fact]
    public void Check_CatchNonThrowableClass_ReportsCatchNotThrowable()
    {
        // Regression guard for the catch-type-resolution fix itself: before it, catch types were
        // resolved via ResolveExpressionType (always `unresolved` for a bare class name), which is
        // permissive and silently accepted catching a non-Throwable class. Resolving catch types the
        // same way as `instanceof` must still correctly flag this case.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(callable $callback): void {
                try {
                    $callback();
                } catch (\NotThrowable $e) {
                    return;
                }
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerCatchNotThrowable,
            $"catching a non-Throwable class must still report CheckerCatchNotThrowable: {Describe(errors)}");
    }

    [Fact]
    public void Check_CoalesceAssignAcrossNestedTryCatchThenNullGuardThrow_DoesNotReport4039()
    {
        // Nested try/catch (inner + outer catch both feeding the same `??=` target) must merge
        // cleanly through two levels, not just one.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(callable $callback): void {
                ?\Throwable $first = null;
                try {
                    try {
                        $callback();
                    } catch (\TypeError $e) {
                        $first ??= $e;
                    }
                } catch (\ValueError $e2) {
                    $first ??= $e2;
                }
                if ($first !== null) {
                    throw $first;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"nested try/catch ??= merge then !== null throw: {Describe(errors)}");
    }

    [Fact]
    public void Check_CoalesceAssignInForeachWithoutTry_DoesNotReport4039()
    {
        // `??=` merging purely through a loop join (no try/catch involved) must also refresh the
        // tracked type on every iteration path.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(array<?\Throwable> $maybeErrors): void {
                ?\Throwable $first = null;
                foreach ($maybeErrors as $maybeError) {
                    $first ??= $maybeError;
                }
                if ($first !== null) {
                    throw $first;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"??= merged through foreach (no try/catch) then !== null throw: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyCoalesceAssignThenNullGuardThrow_DoesNotReport4039()
    {
        // Property analogue of the resolved CancellationToken bug: same `??=`-then-`!== null`-then-
        // `throw` shape, but the accumulator is `$this->err` instead of a local variable.
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(\Throwable $e): void {
                    $this->err ??= $e;
                    if ($this->err !== null) {
                        throw $this->err;
                    }
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"property ??= then !== null throw must not report TYHP4039: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyNullGuardThrow_DoesNotReport4039()
    {
        // Null-check alone (no prior ??=) must narrow `$this->prop` for throw.
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(): void {
                    if ($this->err !== null) {
                        throw $this->err;
                    }
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"property !== null throw must not report TYHP4039: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyInstanceofGuard_NarrowsForThrow()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(): void {
                    if ($this->err instanceof \Exception) {
                        throw $this->err;
                    }
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"property instanceof throw must not report TYHP4039: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyIssetGuardThrow_DoesNotReport4039()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(): void {
                    if (isset($this->err)) {
                        throw $this->err;
                    }
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"isset($this->prop) throw must not report TYHP4039: {Describe(errors)}");
    }

    [Fact]
    public void Check_PropertyAssignThenThrowWithoutGuard_DoesNotReport4039()
    {
        // Plain `=` of a non-null Throwable should refresh tracked property type so throw is clean
        // without an extra null guard (mirrors AssignVariable for locals).
        var errors = CompileAndCheck("""
            <?tyhp
            class Holder {
                public ?\Throwable $err = null;
                public function demo(\Throwable $e): void {
                    $this->err = $e;
                    throw $this->err;
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"property = Throwable then throw must not report TYHP4039: {Describe(errors)}");
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
