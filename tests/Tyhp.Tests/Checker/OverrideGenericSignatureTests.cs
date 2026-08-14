using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Override signature checking must resolve base method annotations in the base class's
/// generic scope (with extends-chain substitution), not under the child's ObjectGenerics alone.
/// </summary>
[Trait("Category", "Checker")]
public class OverrideGenericSignatureTests
{
    [Fact]
    public void Override_ParentClassGenericInSignature_DoesNotReportSymbolNotFound()
    {
        // Mirrors ExpressionBuilder&lt;T&gt; extends Expression&lt;T, bool&gt; overriding and/or that
        // mention Expression&lt;TSource, …&gt; / ExpressionBuilder&lt;TSource&gt; on the base.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            class Expression<TSource, TReturn> {
                public function and(?Expression<TSource, mixed> $other = null): ExpressionBuilder<TSource> {
                    return new ExpressionBuilder<TSource>();
                }
            }

            class ExpressionBuilder<T> extends Expression<T, bool> {
                public function and(?Expression<T, mixed> $other = null): ExpressionBuilder<T> {
                    return new ExpressionBuilder<T>();
                }
            }
            """);

        errors.Should().NotContain(
            e => NamesUnresolved(e, "TSource"),
            $"parent TSource must resolve via inheritance bindings: {Describe(errors)}");

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerOverloadSignatureIncompatible,
            $"and() override must stay compatible after substitution: {Describe(errors)}");
    }

    [Fact]
    public void Override_IncompatibleAfterSubstitution_StillReports()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;

            class Base<TSource> {
                public function take(TSource $x): void {}
            }

            class Child<T> extends Base<T> {
                public function take(string $x): void {}
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerOverloadSignatureIncompatible,
            $"string vs T (from TSource) must be incompatible: {Describe(errors)}");
    }

    [Fact]
    public void MethodGeneric_InReturnAndNew_StillResolves()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box<T> {
                public static function make<TSource>(): Box<TSource> {
                    return new Box<TSource>();
                }
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.BinderSymbolNotFound,
            $"in-scope method generic TSource must not diagnose: {Describe(errors)}");
    }

    private static bool NamesUnresolved(IDiagnostic diagnostic, string name) =>
        diagnostic.Code == MessageCode.BinderSymbolNotFound
        && diagnostic.FormatParams.Length > 0
        && string.Equals(diagnostic.FormatParams[0]?.ToString(), name, StringComparison.Ordinal);

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

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code} L{e.Line}:{e.Column} [{string.Join(",", e.FormatParams)}]"));
}
