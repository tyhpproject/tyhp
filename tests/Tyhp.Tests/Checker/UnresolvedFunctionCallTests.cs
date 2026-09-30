using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Free-function / namespaced-function calls whose name does not resolve must report TYHP4182
/// instead of silently type-checking as mixed/unresolved.
/// </summary>
[Trait("Category", "Checker")]
public class UnresolvedFunctionCallTests
{
    [Fact]
    public void Check_UnknownFreeFunction_Reports4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): void {
                this_function_does_not_exist_anywhere(1);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUndefinedFunction
            && HasParam(d, "this_function_does_not_exist_anywhere"));
    }

    [Fact]
    public void Check_UnknownFullyQualifiedFunction_Reports4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): void {
                \This\Does\Not\Exist(1);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUndefinedFunction
            && HasParam(d, "This\\Does\\Not\\Exist"));
    }

    [Fact]
    public void Check_UnknownFirstClassCallable_Reports4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): void {
                $fn = this_function_does_not_exist_anywhere(...);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUndefinedFunction
            && HasParam(d, "this_function_does_not_exist_anywhere"));
    }

    [Fact]
    public void Check_DeclaredFreeFunction_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function existing(): void {}
            function f(): void {
                existing();
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_ForwardReferencedFreeFunction_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): void {
                laterDeclared();
            }
            function laterDeclared(): void {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_DeclaredFreeFunctionDifferentCase_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function existing(): void {}
            function f(): void {
                Existing();
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_GlobalStdlibFunction_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                return \strlen("hi");
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_UnqualifiedStdlibCallInsideNamespace_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App {
                function demo(): int {
                    return strlen("hi");
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_NamespacedFunctionCall_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App {
                function helper(): void {}
                function demo(): void {
                    helper();
                    \App\helper();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_UnqualifiedSameNamespaceCallFromTopLevelStatement_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;

            function twice(int $n): int { return $n * 2; }

            int $a = twice(2);
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_UnqualifiedSameNamespaceCallFromBraceNamespaceTopLevel_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App {
                function twice(int $n): int { return $n * 2; }
                int $a = twice(2);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_UnqualifiedSameNamespaceCallFromTopLevel_WrongArgument_Reports4010Not4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;

            function twice(int $n): int { return $n * 2; }

            int $c = twice("no");
            int $d = \App\twice("no");
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_UnknownFunctionFromNamespacedTopLevel_Reports4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;

            function twice(int $n): int { return $n * 2; }

            int $a = missing_top_level_fn(2);
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUndefinedFunction
            && HasParam(d, "missing_top_level_fn"));
    }

    [Fact]
    public void Check_UnqualifiedCallFromOtherNamespaceTopLevel_Reports4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App {
                function twice(int $n): int { return $n * 2; }
            }
            namespace Other {
                int $a = twice(2);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUndefinedFunction
            && HasParam(d, "twice"));
    }

    [Fact]
    public void Check_UseFunctionImportFromTopLevel_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace Other {
                function helper(): void {}
            }
            namespace App {
                use function Other\helper;
                helper();
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_UseFunctionImport_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace Other {
                function helper(): void {}
            }
            namespace App {
                use function Other\helper;
                function demo(): void {
                    helper();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_AliasedUseFunctionImport_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace Other {
                function helper(): void {}
            }
            namespace App {
                use function Other\helper as h;
                function demo(): void {
                    h();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_VersionGatedFunctionOutsideGate_Reports4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                only_on_php85();
            }
            declare(php=">=8.5") {
                function only_on_php85(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUndefinedFunction
            && HasParam(d, "only_on_php85"));
    }

    [Fact]
    public void Check_FreeFunctionCallFromMethodBody_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function helper(): void {}
            class Foo {
                public function bar(): void {
                    helper();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_FreeFunctionCallFromClosureInMethodBody_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function helper(): void {}
            class Foo {
                public function bar(): void {
                    $fn = function (): void {
                        helper();
                    };
                    $fn();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_UnknownFunctionCallFromMethodBody_Reports4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function bar(): void {
                    this_function_does_not_exist_anywhere();
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUndefinedFunction
            && HasParam(d, "this_function_does_not_exist_anywhere"));
    }

    [Fact]
    public void Check_VersionGatedFunctionInsideActiveGate_No4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            declare(php=">=8.0") {
                function onlyOn80(): void {}
                function demo(): void {
                    onlyOn80();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
    }

    [Fact]
    public void Check_CallingClassNameAsFunction_Reports4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}
            function f(): void {
                Foo();
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerUndefinedFunction
            && HasParam(d, "Foo"));
    }

    [Fact]
    public void Check_CompactStillReportsProhibitedNotOnly4182()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $a = 'hello';
                array $data = compact('a');
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerCompactProhibited);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUndefinedFunction);
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
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
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
