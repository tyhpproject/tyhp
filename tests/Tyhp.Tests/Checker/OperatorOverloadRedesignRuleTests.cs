using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

// Story 11 §8 redesign checker rules: reserved-name conflicts, mutual distinguishability of
// operator forms, and the self→self convert prohibition.
[Trait("Category", "Checker")]
public class OperatorOverloadRedesignRuleTests
{
    [Fact]
    public void Check_ReservedNameConflict_ReportsError()
    {
        // Declaring `operator +` reserves `__add`; a hand-written `__add` in the same class conflicts.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self { return $left; }
                public function __add(int $x): int { return $x; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerMagicMethodSignature
            && HasParam(d, "reserves"));
    }

    [Fact]
    public void Check_ReservedNameConflict_IsCaseInsensitive()
    {
        // PHP method names are case-insensitive, so `__Add` must also conflict with reserved `__add`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self { return $left; }
                public function __Add(int $x): int { return $x; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerMagicMethodSignature
            && HasParam(d, "reserves"));
    }

    [Fact]
    public void Check_ReservedNameNotTriggeredWithoutOperator()
    {
        // No convert(self): string operator, so hand-writing `__toString` is allowed.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                public function __toString(): string { return ''; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerMagicMethodSignature
            && HasParam(d, "reserves"));
    }

    [Fact]
    public void Check_AmbiguousOverloadForms_ReportsError()
    {
        // Two `operator +` forms whose operand type-sets both accept (Money, int) — not mutually
        // distinguishable, so the collapsed dispatch order would silently decide.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self { return $left; }
                operator +(self $left, int|string $right): self { return $left; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerMagicMethodSignature
            && HasParam(d, "ambiguous"));
    }

    [Fact]
    public void Check_DistinguishableOverloadForms_NoAmbiguityError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self { return $left; }
                operator +(int $left, self $right): int { return $left; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerMagicMethodSignature
            && HasParam(d, "ambiguous"));
    }

    [Fact]
    public void Check_ConvertSelfToSelf_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): self { return $value; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerMagicMethodSignature
            && HasParam(d, "'self' to 'self'"));
    }

    [Fact]
    public void Check_ConvertSelfToStatic_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): static { return $value; }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerMagicMethodSignature
            && HasParam(d, "'self' to 'self'"));
    }

    [Fact]
    public void Check_StandaloneExtensionOperator_SelfOperands_NoErrors()
    {
        // ExtensionRule must seed EnclosingObject / EnclosingObjectType before OperatorOverloadRule
        // runs; otherwise every `self` in a standalone extension operator fails with TYHP4064.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOperators {
                operator +<Money>(self $left, self $right): self {
                    return $left;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"))}");
    }

    [Fact]
    public void Check_StandaloneExtensionOperator_BuiltinStringTarget_NoErrors()
    {
        // Docs form: operator *<string>(self $left, int $right) — binder must accept BuiltInTypeSymbol
        // targets (not only ObjectDeclarationSymbol) and ExtensionRule must seed self → string.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOperators {
                operator *<string>(self $left, int $right): string {
                    return \str_repeat($left, $right);
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"))}");
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.ExtensionOperatorTargetNotFound);
    }

    private static bool HasParam(IDiagnostic diagnostic, string substring)
        => diagnostic.FormatParams is { } args
            && args.Any(p => (p?.ToString() ?? string.Empty)
                .Contains(substring, StringComparison.OrdinalIgnoreCase));

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
