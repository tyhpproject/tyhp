using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Extension <c>operator +&lt;T&gt;</c> rejects non-instantiable builtin targets
/// (<c>void</c>, <c>never</c>, <c>null</c>, <c>mixed</c>, <c>resource</c>, <c>true</c>,
/// <c>false</c>) at bind/check (TYHP3025) instead of emitting illegal <c>instanceof void</c>.
/// </summary>
[Trait("Category", "Checker")]
public class ExtensionOperatorTargetTests
{
    [Theory]
    [InlineData("void")]
    [InlineData("never")]
    [InlineData("null")]
    [InlineData("mixed")]
    [InlineData("resource")]
    [InlineData("true")]
    [InlineData("false")]
    public void Check_NonInstantiableBuiltinTarget_ReportsError(string target)
    {
        var (diagnostics, _) = CompileAndCheck($$"""
            <?tyhp
            extension TargetOperators extends {{target}} {
                operator + (self $left, int $right): int {
                    return $right;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.ExtensionOperatorTargetNotInstantiable
            && HasParam(d, target));
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.ExtensionOperatorTargetNotFound);
    }

    [Fact]
    public void Bind_VoidTarget_DoesNotContributeOntoBuiltin()
    {
        var (diagnostics, globalScope) = CompileAndCheck("""
            <?tyhp
            extension VoidOperators extends void {
                operator + (self $left, int $right): int {
                    return $right;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.ExtensionOperatorTargetNotInstantiable);

        var voidBuiltin = ((IBaseScope)globalScope).FindChildSymbolByName("void") as BuiltInTypeSymbol;
        voidBuiltin.Should().NotBeNull();
        voidBuiltin!.ExtensionContributedOperators.Should().BeEmpty();
    }

    [Theory]
    [InlineData("string")]
    [InlineData("int")]
    [InlineData("array")]
    [InlineData("bool")]
    [InlineData("object")]
    [InlineData("float")]
    [InlineData("callable")]
    [InlineData("iterable")]
    public void Check_ValueBearingBuiltinTarget_NoTargetError(string target)
    {
        var (diagnostics, _) = CompileAndCheck($$"""
            <?tyhp
            extension TargetOperators extends {{target}} {
                operator + (self $left, int $right): int {
                    return $right;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.ExtensionOperatorTargetNotInstantiable);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.ExtensionOperatorTargetNotFound);
    }

    [Fact]
    public void Check_ClassTarget_NoTargetError()
    {
        var (diagnostics, _) = CompileAndCheck("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOperators extends Money {
                operator + (self $left, self $right): self {
                    return $left;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.ExtensionOperatorTargetNotInstantiable);
        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.ExtensionOperatorTargetNotFound);
    }

    private static bool HasParam(IDiagnostic diagnostic, string substring)
        => diagnostic.FormatParams is { } args
            && args.Any(p => (p?.ToString() ?? string.Empty)
                .Contains(substring, StringComparison.OrdinalIgnoreCase));

    private static (DiagnosticBag Diagnostics, GlobalScope GlobalScope) CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return (result.Diagnostics, result.GlobalScope!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
