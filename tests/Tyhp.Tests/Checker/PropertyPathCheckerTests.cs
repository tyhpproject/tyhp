using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 16 Phase 1 — <c>PropertyPath&lt;T, R&gt;</c> call-argument validation (TYHP4320/4321).
/// </summary>
[Trait("Category", "Checker")]
public class PropertyPathCheckerTests
{
    [Fact]
    public void Check_InlinePropertyChain_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public string $name;
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(): void {
                take(fn ($u) => $u->name);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPropertyPathRequiresInlineFn
                || d.Code == MessageCode.CheckerPropertyPathInvalidBody,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_MultiSegmentChain_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Address {
                public string $city;
            }
            class User {
                public Address $address;
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(): void {
                take(fn ($u) => $u->address->city);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPropertyPathRequiresInlineFn
                || d.Code == MessageCode.CheckerPropertyPathInvalidBody,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_NullSafeChain_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Address {
                public string $city;
            }
            class User {
                public ?Address $address;
            }
            function take(\Tyhp\PropertyPath<User, ?string> $path): void {}
            function demo(): void {
                take(fn ($u) => $u?->address?->city);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPropertyPathRequiresInlineFn
                || d.Code == MessageCode.CheckerPropertyPathInvalidBody,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_ForwardedPropertyPathValue_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public string $name;
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function forward(\Tyhp\PropertyPath<User, string> $path): void {
                take($path);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPropertyPathRequiresInlineFn
                || d.Code == MessageCode.CheckerPropertyPathInvalidBody,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_NullForNullablePropertyPathParameter_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public string $name;
            }
            function take(?\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(): void {
                take(null);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPropertyPathRequiresInlineFn
                || d.Code == MessageCode.CheckerPropertyPathInvalidBody,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_UserDefinedPropertyPathClass_NotTreatedAsLambdaType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;
            class PropertyPath {
                public string $raw;
            }
            function take(\App\PropertyPath $path): void {}
            function demo(\App\PropertyPath $path): void {
                take($path);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerPropertyPathRequiresInlineFn
                || d.Code == MessageCode.CheckerPropertyPathInvalidBody,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_NonFnArgument_Reports4320()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public string $name;
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(\Closure $c): void {
                take($c);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerPropertyPathRequiresInlineFn);
    }

    [Fact]
    public void Check_StatementClosure_Reports4320()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public string $name;
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(): void {
                take(function ($u) { return $u->name; });
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerPropertyPathRequiresInlineFn);
    }

    [Fact]
    public void Check_MethodCallBody_Reports4321()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public function name(): string { return ""; }
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(): void {
                take(fn ($u) => $u->name());
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerPropertyPathInvalidBody);
    }

    [Fact]
    public void Check_WrappedFunctionCall_Reports4321()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public string $name;
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(): void {
                take(fn ($u) => \strtolower($u->name));
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerPropertyPathInvalidBody);
    }

    [Fact]
    public void Check_BinaryExpressionBody_Reports4321()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public string $first;
                public string $last;
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(): void {
                take(fn ($u) => $u->first . $u->last);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerPropertyPathInvalidBody);
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
