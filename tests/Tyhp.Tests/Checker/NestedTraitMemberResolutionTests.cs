using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class NestedTraitMemberResolutionTests
{
    [Fact]
    public void Check_MethodFromNestedTraitUse_ResolvesWithoutFallingThroughToGet()
    {
        // BindTraitUseBlock historically only recorded ITypeExpression trait names, so
        // `use Boots;` was invisible to ResolveInheritedMember; with `__get` on the same trait,
        // `$this->boot()` was arity-checked as `__get` (TYHP4142 on `$name`).
        var diagnostics = CompileAndCheck("""
            <?tyhp

            namespace Probe;

            trait Boots {
                public function boot(): void {}
            }

            trait HandlesGet {
                use Boots;

                public function tryGet(): void {
                    $this->boot();
                }

                public function __get(string $name): mixed {
                    return null;
                }
            }

            class Widget {
                use HandlesGet;
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            "nested trait method must resolve; got: "
            + string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingArgument);
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
