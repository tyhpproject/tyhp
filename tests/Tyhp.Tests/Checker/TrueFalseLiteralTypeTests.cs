using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Regressions for FOUND_BUGS Prop-init #41: bare <c>true</c>/<c>false</c> as return types,
/// typed locals, and properties must type-check when the value matches (and reject mismatches).
/// </summary>
[Trait("Category", "Checker")]
public class TrueFalseLiteralTypeTests
{
    [Fact]
    public void Check_TrueFalse_MatchingReturnLocalAndProperty_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function isValid(): true { return true; }
            function isBroken(): false { return false; }
            function locals(): void {
                true $ok = true;
                false $no = false;
            }
            class Flags {
                public true $always = true;
                public false $never = false;
            }
            function widen(true $flag): bool { return $flag; }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_TrueFalse_MismatchingReturnLocalAndProperty_ReportsError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function badReturn(): true { return false; }
            function badLocal(): void { true $x = false; }
            class BadProp { public true $p = false; }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
        // Declared type should spell as bare `true`, not `\true`.
        diagnostics.Errors.Should().NotContain(
            d => d.Message.Contains("\\true", StringComparison.Ordinal),
            "builtin true/false must display without a leading backslash");
    }

    [Fact]
    public void Check_NullLiteralReturnAndLocal_StillWork()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function retNull(): null { return null; }
            function localNull(): void { null $x = null; }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void IsAssignableTo_TrueLiteral_ToTrueAndBool_ReturnsTrue()
    {
        using var compilationService = new CompilationService();
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "probe.tyhp");
        File.WriteAllText(filePath, "<?tyhp\nfunction f(): void {}\n");
        try
        {
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            });
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var global = result.GlobalScope!;

            var trueLit = new LiteralCheckedType(true, new SimpleCheckedType(new BuiltInTypeSymbol("true")));
            var trueLitBoolUnderlying = new LiteralCheckedType(
                true, new SimpleCheckedType(new BuiltInTypeSymbol("bool")));
            var falseLit = new LiteralCheckedType(false, new SimpleCheckedType(new BuiltInTypeSymbol("false")));
            var targetTrue = CheckedTypes.FromSymbol(TypeComparer.ResolveBuiltIn("true", global)!);
            var targetBool = CheckedTypes.FromSymbol(TypeComparer.ResolveBuiltIn("bool", global)!);

            TypeComparer.IsAssignableTo(trueLit, targetTrue, symbolTree, global).Should().BeTrue();
            TypeComparer.IsAssignableTo(trueLitBoolUnderlying, targetTrue, symbolTree, global).Should().BeTrue();
            TypeComparer.IsAssignableTo(falseLit, targetTrue, symbolTree, global).Should().BeFalse();
            TypeComparer.IsAssignableTo(trueLit, targetBool, symbolTree, global).Should().BeTrue();
            TypeComparer.IsAssignableTo(targetTrue, targetBool, symbolTree, global).Should().BeTrue();
            TypeComparer.IsAssignableTo(targetBool, targetTrue, symbolTree, global).Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Check_NullableTrueAndArrayOfTrue_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function nullableTrue(): ?true { return null; }
            function nullableTrue2(): ?true { return true; }
            function arrayOfTrue(): array<true> { return [true, true]; }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }


    [Fact]
    public void Check_TrueFalseUnion_VsBool_NoIncompatibleReturn_StillReports4056()
    {
        // Assignability: bool ≡ true|false (FOUND #42). Spelling still gets TYHP4056.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function unionRet(bool $b): true|false { return $b; }
            function boolRet(true|false $b): bool { return $b; }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType
                || d.Code == MessageCode.CheckerTypeMismatch,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerUseBoolInsteadOfTrueFalse);
    }

    [Fact]
    public void Check_TrueFalseUnionWithExtraMember_AcceptsBool()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function widen(bool $b): true|false|string { return $b; }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleReturnType,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
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
