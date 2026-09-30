using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 20.6 Phase 4 review leftovers: bodyless <c>.tyhpdef</c> signatures must not report
/// missing returns, and <c>self</c> on class-body operators must resolve against the enclosing
/// tyhpdef type (not <c>CheckerRelativeTypeOutsideClass</c>).
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Story20.6")]
public class TyhpdefBodylessSignatureTests
{
    [Fact]
    public void Check_BodylessTyhpdefMethod_DoesNotReportMissingReturn()
    {
        var diagnostics = CompileAndCheck("Money.tyhpdef", """
            <?tyhpdef
            class Money {
                public function plus(Money $other): Money;
            }
            """);

        diagnostics.Errors.Where(FromMoneyTyhpdef).Should().NotContain(d =>
            d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    [Fact]
    public void Check_BodylessTyhpdefOperator_SelfDoesNotReportRelativeOutsideClass()
    {
        var diagnostics = CompileAndCheck("Money.tyhpdef", """
            <?tyhpdef
            class Money {
                operator +(self $l, self $r): self;
            }
            """);

        diagnostics.Errors.Where(FromMoneyTyhpdef).Should().NotContain(d =>
            d.Code == MessageCode.CheckerRelativeTypeOutsideClass);
    }

    [Fact]
    public void Check_BodylessTyhpdefMethodAndOperator_NoFalsePositives()
    {
        var diagnostics = CompileAndCheck("Money.tyhpdef", """
            <?tyhpdef
            class Money {
                public function plus(Money $other): Money;
                operator +(self $l, self $r): self;
            }
            """);

        var fromFile = diagnostics.Errors.Where(FromMoneyTyhpdef).ToList();
        fromFile.Should().NotContain(d =>
            d.Code == MessageCode.CheckerMissingReturnStatement
            || d.Code == MessageCode.CheckerRelativeTypeOutsideClass,
            because: string.Join("; ", fromFile.Select(d => $"{d.Code}: {d.Message}")));
    }

    [Fact]
    public void Check_MappedTyhpdefExtensionOperator_SelfDoesNotReportRelativeOutsideClass()
    {
        var diagnostics = CompileAndCheck("Money.tyhpdef", """
            <?tyhpdef
            class Money {
                public function plus(Money $other): Money;
                extension operator +(self $l, self $r): self => $l->plus($r);
            }
            """);

        diagnostics.Errors.Where(FromMoneyTyhpdef).Should().NotContain(d =>
            d.Code == MessageCode.CheckerRelativeTypeOutsideClass
            || d.Code == MessageCode.CheckerMissingReturnStatement);
    }

    [Fact]
    public void Check_ExtensionOperatorMappingToSameNamedRealMethod_DoesNotReportReservedNameConflict()
    {
        // Wiring CheckObjectBody into tyhpdef checking (for #2/#3) also turned on
        // ValidateOperatorOverloadSet for tyhpdef bodies for the first time. The standard
        // `extension operator ... => expr` thin-mapping idiom (e.g.
        // `extension operator convert(self $value): int => $value->__toInt();`) intentionally
        // calls a real, separately-declared method whose name matches the operator's generated
        // name — that is not a reserved-name collision because the mapping is always erased
        // (never synthesizes its own method).
        var diagnostics = CompileAndCheck("Convertible.tyhpdef", """
            <?tyhpdef
            class Convertible {
                extension operator convert(self $value): int => $value->__toInt();
                public function __toInt(): int;
            }
            """);

        diagnostics.Errors.Where(FromConvertibleTyhpdef).Should().NotContain(d =>
            d.Code == MessageCode.CheckerMagicMethodSignature);
    }

    [Fact]
    public void Check_NativeOperatorCollidingWithRealMethod_StillReportsReservedNameConflict()
    {
        // A native (non-`extension`) operator still synthesizes its own backing method, so it
        // must keep reserving its generated name against a same-named hand-declared method.
        var diagnostics = CompileAndCheck("Colliding.tyhpdef", """
            <?tyhpdef
            class Colliding {
                public function __add(self $other): self;
                operator +(self $l, self $r): self;
            }
            """);

        diagnostics.Errors.Where(d => (d.FileName ?? "").EndsWith("Colliding.tyhpdef", StringComparison.Ordinal))
            .Should().Contain(d => d.Code == MessageCode.CheckerMagicMethodSignature);
    }

    private static bool FromConvertibleTyhpdef(IDiagnostic d) =>
        (d.FileName ?? "").EndsWith("Convertible.tyhpdef", StringComparison.Ordinal);

    private static bool FromMoneyTyhpdef(IDiagnostic d) =>
        (d.FileName ?? "").EndsWith("Money.tyhpdef", StringComparison.Ordinal);

    private static DiagnosticBag CompileAndCheck(string fileName, string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);
        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: "8.4",
                configure: o =>
                {
                    o.Checker = new CheckerOptions { PhpVersion = "8.4" };
                });
            var result = compilationService.ParseFiles([filePath], options);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
