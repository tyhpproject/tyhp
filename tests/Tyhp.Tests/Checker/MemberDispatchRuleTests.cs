using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Covers FOUND_BUGS item 30: class members bypass <c>CheckNode</c>, so rules that register
/// <c>PhpMethodDeclAst</c> for free functions must be invoked explicitly from
/// <c>DeclarationRule.CheckMethod</c> (without routing through <c>CheckNode</c>, which would
/// double-fire <c>DeclarationRule</c>). Also covers the tightly related 4130 gap for imports
/// used only as attributes on class members.
/// </summary>
[Trait("Category", "Checker")]
public class MemberDispatchRuleTests
{
    [Fact]
    public void Check_MethodWithoutReturnType_ReportsTypeRequiredOnce()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function go() {
                    return;
                }
            }
            """);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerVariableTypeRequired)
            .Should().Be(1);
    }

    [Fact]
    public void Check_FunctionWithoutReturnType_StillReportsTypeRequired()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function go() {
                return;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariableTypeRequired);
    }

    [Fact]
    public void Check_AsyncMethodReturningGenerator_Reports4087Once()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public async function go(): \Generator {
                    return;
                }
            }
            """);

        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerGeneratorInvalidReturnType)
            .Should().Be(1);
    }

    [Fact]
    public void Check_AsyncFunctionReturningGenerator_StillReports4087()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            async function go(): \Generator {
                return;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerGeneratorInvalidReturnType);
    }

    [Fact]
    public void Check_UnusedVariableInMethod_Reports4200Once()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {
                public function go(): void {
                    int $unused = 1;
                }
            }
            """);

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnusedVariable)
            .Should().Be(1);
    }

    [Fact]
    public void Check_UnusedVariableInFunction_StillReports4200()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function go(): void {
                int $unused = 1;
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerUnusedVariable);
    }

    [Fact]
    public void Check_CircularDisposableLocalsInMethod_ReportsOnce()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Node implements \Tyhp\Contracts\IsDisposable {
                public mixed $other = null;
                public function dispose(): void {}
            }

            class Linker {
                public function link(): void {
                    $a := new Node();
                    $b := new Node();
                    $a->other = $b;
                    $b->other = $a;
                }
            }
            """);

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerDisposableCircularReference)
            .Should().Be(1);
    }

    [Fact]
    public void Check_ImportUsedOnlyOnMethodAttribute_DoesNotReport4130()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App\Attrs;

            #[\Attribute]
            class Marker {}

            namespace App\Models;

            use App\Attrs\Marker;

            class C {
                #[Marker]
                public function go(): void {}
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_ImportUsedOnlyOnPropertyAttribute_DoesNotReport4130()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App\Attrs;

            #[\Attribute]
            class Marker {}

            namespace App\Models;

            use App\Attrs\Marker;

            class C {
                #[Marker]
                public int $x = 0;
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
    }

    [Fact]
    public void Check_ImportUsedOnlyOnEnumCaseAttribute_DoesNotReport4130()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App\Attrs;

            #[\Attribute]
            class Marker {}

            namespace App\Models;

            use App\Attrs\Marker;

            enum E {
                #[Marker]
                case A;
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_ImportUsedOnlyOnClassConstantAttribute_DoesNotReport4130()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App\Attrs;

            #[\Attribute]
            class Marker {}

            namespace App\Models;

            use App\Attrs\Marker;

            class C {
                #[Marker]
                public const int FOO = 1;
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerUnusedImport);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
    }

    [Fact]
    public void Check_GenuinelyUnusedImport_StillReports4130AlongsideMemberAttribute()
    {
        // Walking member attributes must mark only what the attributes actually name — an
        // unrelated import in the same file has to stay reportable.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App\Attrs;

            #[\Attribute]
            class Marker {}

            class Unrelated {}

            namespace App\Models;

            use App\Attrs\Marker;
            use App\Attrs\Unrelated;

            class C {
                #[Marker]
                public function go(): void {}
            }
            """);

        diagnostics.Warnings.Count(d => d.Code == MessageCode.CheckerUnusedImport).Should().Be(1);
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
