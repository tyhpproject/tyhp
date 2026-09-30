using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class ExtensionFunctionOverloadTests
{
    [Fact]
    public void SameNameSignaturesAndImplementation_DoNotReport3002()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOps extends string {
                function first(1 $n): string;
                function first(2 $n): array<int, string>;
                fn first(int $n = 1): string|array<int, string> => $this;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
    }

    [Fact]
    public void CallSite_SelectsLiteralOverloadReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension StringOps extends string {
                function first(1 $n): string;
                function first(2 $n): array<int, string>;
                fn first(int $n = 1): string|array<int, string> => $this;
            }

            use extension StringOps;

            function asString(): string {
                return "ab"->first(1);
            }

            function asArray(): array<int, string> {
                return "ab"->first(2);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void WordCount_LiteralFormatOverloads_SelectReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension WordOps extends string {
                function wordCount(0 $format = 0, ?string $characters = null): int;
                function wordCount(1 $format, ?string $characters = null): array<int, string>;
                function wordCount(2 $format, ?string $characters = null): array<int, string>;
                fn wordCount(int $format = 0, ?string $characters = null): array<int, string>|int => 0;
            }

            use extension WordOps;

            function asCount(): int {
                return "one two"->wordCount();
            }

            function asList(): array<int, string> {
                return "one two"->wordCount(1);
            }

            function asOffsetMap(): array<int, string> {
                return "one two"->wordCount(2);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void BraceBody_LiteralFlagOverloads_SelectReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension MatchOps extends string {
                function take(string $pattern, 0 $flags = 0): ?array<int|string, string>;
                function take(string $pattern, 256 $flags): ?array<int|string, int>;
                function take(
                    string $pattern,
                    int $flags = 0
                ): array<int|string, string|null>|array<int|string, int>|null {
                    return null;
                }
            }

            use extension MatchOps;

            function defaultFlags(): ?array<int|string, string> {
                return "a"->take("/a/");
            }

            function offsetCapture(): ?array<int|string, int> {
                return "a"->take("/a/", 256);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
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
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            var bindErrors = result.Diagnostics.Errors.Where(e => (int)e.Code < 4000).ToList();
            bindErrors.Should().BeEmpty(
                $"parse/bind errors: {string.Join(", ", bindErrors.Select(e => e.Message))}");

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
