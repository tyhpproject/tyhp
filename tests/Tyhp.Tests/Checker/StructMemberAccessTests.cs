using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Named-struct member access and extension calls must resolve against the struct type
/// (not gradual unknown / array-offset). FOUND_BUGS #44 / #45.
/// </summary>
[Trait("Category", "Checker")]
public class StructMemberAccessTests
{
    [Fact]
    public void Check_UnknownStructProperty_Reports4101()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            function read(Money $m): int {
                return $m->doesNotExist;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerSymbolNameNotFound
                && HasParam(e, "doesNotExist"),
            "undeclared struct property must be diagnosed: " + Describe(errors));
    }

    [Fact]
    public void Check_UnknownStructMethod_Reports4101()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            function fmt(Money $m): string {
                return $m->nope();
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerSymbolNameNotFound
                && HasParam(e, "nope"),
            "undeclared struct method must be diagnosed: " + Describe(errors));
    }

    [Fact]
    public void Check_StructExtensionMethod_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            extension MoneyExt extends Money {
                function format(): string {
                    return (string)$this->cents;
                }
            }
            function fmt(Money $m): string {
                return $m->format();
            }
            """);

        errors.Should().BeEmpty(
            "in-scope struct extension method must type-check: " + Describe(errors));
    }

    [Fact]
    public void Check_KnownStructProperty_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            function read(Money $m): int {
                return $m->cents;
            }
            """);

        errors.Should().BeEmpty(
            "declared struct property must type-check: " + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static bool HasParam(IDiagnostic diagnostic, string substring)
        => diagnostic.FormatParams is { } args
            && args.Any(p => (p?.ToString() ?? string.Empty)
                .Contains(substring, StringComparison.OrdinalIgnoreCase));

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
