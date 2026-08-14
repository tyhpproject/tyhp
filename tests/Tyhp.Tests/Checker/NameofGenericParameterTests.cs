using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>nameof(T)</c> on in-scope class/method generics must be accepted (parity with
/// <c>typeof(T)</c>) — FOUND_BUGS 2026-08-12 nameof audit.
/// </summary>
[Trait("Category", "Checker")]
public class NameofGenericParameterTests
{
    [Fact]
    public void Nameof_MethodGeneric_DoesNotReport4090()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function pick<TBatchReturn>(TBatchReturn $v): string {
                return nameof(TBatchReturn);
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerNonConstantExpression,
            $"nameof(TBatchReturn) must be accepted: {Describe(errors)}");
    }

    [Fact]
    public void Nameof_ClassGeneric_DoesNotReport4090()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Box<T> {
                public function label(): string {
                    return nameof(T);
                }
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerNonConstantExpression,
            $"nameof(T) on class generic must be accepted: {Describe(errors)}");
    }

    [Fact]
    public void Nameof_MethodGeneric_OnClass_DoesNotReport4090()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Promise<TReturn> {
                public static function batch<TItem, TBatchReturn>(TItem $item): string {
                    return 'incompatible with ' . nameof(TBatchReturn);
                }
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerNonConstantExpression,
            $"nameof(TBatchReturn) must be accepted: {Describe(errors)}");
    }

    [Fact]
    public void Nameof_NonConstantExpression_StillReports4090()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(string $a, string $b): string {
                return nameof($a . $b);
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerNonConstantExpression,
            $"non-constant nameof arg must still diagnose: {Describe(errors)}");
    }

    [Fact]
    public void Nameof_UnknownBareName_StillReports4090()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(): string {
                return nameof(NotAThing);
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerNonConstantExpression,
            $"unknown nameof arg must still diagnose: {Describe(errors)}");
    }

    [Fact]
    public void Nameof_PropertyPathFn_DoesNotReport4090()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class User {
                public string $firstName;
            }
            function demo(): string {
                return nameof(fn (User $u) => $u->firstName);
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerNonConstantExpression
                || e.Code == MessageCode.CheckerPropertyPathInvalidBody,
            $"nameof(fn property chain) must be accepted: {Describe(errors)}");
    }

    [Fact]
    public void Nameof_PropertyPathFn_Nested_DoesNotReport4090()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Address {
                public string $city;
            }
            class User {
                public Address $address;
            }
            function demo(): string {
                return nameof(fn (User $u) => $u->address->city);
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerNonConstantExpression
                || e.Code == MessageCode.CheckerPropertyPathInvalidBody,
            $"nameof(fn nested chain) must be accepted: {Describe(errors)}");
    }

    [Fact]
    public void Nameof_FnWithMethodCall_Reports4321()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class User {
                public function name(): string { return ""; }
            }
            function demo(): string {
                return nameof(fn (User $u) => $u->name());
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerPropertyPathInvalidBody,
            $"nameof(fn method call) must report 4321: {Describe(errors)}");
    }

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        !errors.Any()
            ? "(no errors)"
            : string.Join("; ", errors.Select(e => $"{e.Code} L{e.Line}:{e.Column}"));

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
