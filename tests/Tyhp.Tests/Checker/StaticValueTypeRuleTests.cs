using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class StaticValueTypeRuleTests
{
    [Fact]
    public void Check_AllLiteralParamAndReturn_AcceptsMatchingLiterals()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function local(): void { 'red'|'green' $color = 'red'; }
            class Props { public 'red'|'green' $prop = 'red'; }
            function a(false|'red'|'green' $c): void {}
            function b(int|'red' $c): void {}
            function c('red'|'green' $c): void {}
            function d('a' $c): 'a' { return $c; }
            function useAll(): void {
                a(false);
                a('red');
                b(1);
                b('red');
                c('green');
                'a' $x = d('a');
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.BinderUnresolvedParameterType
                || d.Code == MessageCode.BinderUnresolvedReturnType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Theory]
    [InlineData("hex", "function f(0xFF|0x1a $c): void {}")]
    [InlineData("hexReturn", "function f(): 0xFF { return 0xFF; }")]
    [InlineData("bin", "function f(): 0b1010 { return 0b1010; }")]
    [InlineData("oct", "function f(): 0o17 { return 0o17; }")]
    [InlineData("legacyLeadingZero", "function f(): 017 { return 017; }")]
    [InlineData("underscored", "function f(): 1_000 { return 1_000; }")]
    [InlineData("underscoredUnion", "function f(1_000|2_000 $c): void {}")]
    [InlineData("floatLit", "function f(): 3.14 { return 3.14; }")]
    [InlineData("floatExp", "function f(): 1.2e3 { return 1.2e3; }")]
    [InlineData("floatLeadingDot", "function f(): .5 { return .5; }")]
    [InlineData("floatTrailingDot", "function f(): 5. { return 5.; }")]
    [InlineData("emptyStr", "function f(): '' { return ''; }")]
    [InlineData("escaped", "function f(): 'it\\'s' { return 'it\\'s'; }")]
    public void Check_NumericLiteralSpellings_DoNotReport3019Or3020(string name, string source)
    {
        var diagnostics = CompileAndCheck($"<?tyhp\n{source}\n");

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.BinderUnresolvedParameterType
                || d.Code == MessageCode.BinderUnresolvedReturnType
                || d.Code == MessageCode.CheckerIncompatibleReturnType,
            $"case '{name}' should bind and check cleanly, but got: "
                + string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_TrueFalseInParameterPositionAndCallSites_StillWork()
    {
        // Parameter-position `true`/`false` declarations bind cleanly. Call-site argument checks
        // are still unreachable in general (FOUND_BUGS Story 14 Phase 3 §1 — BoundSymbol null on
        // free-function call names), so this only asserts the declaration itself type-checks.
        // Return / typed-local / property positions are covered by TrueFalseLiteralTypeTests (#41).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function useCallSite(true $c): void {}
            function useIt(): void {
                useCallSite(true);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_LiteralReturnMismatch_ReportsIncompatibleReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function d('a' $c): 'a' {
                return 'b';
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Check_LiteralLocalMismatch_ReportsTypeError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function useIt(): void {
                'red'|'green' $color = 'blue';
            }
            """);

        diagnostics.HasErrors.Should().BeTrue(
            "assigning 'blue' to 'red'|'green' must be a type error once literals resolve");
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.BinderUnresolvedParameterType
                || d.Code == MessageCode.BinderUnresolvedReturnType);
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
