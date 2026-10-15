using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Class names imported with `use` resolve in top-level script statements and in
/// `using (...)` header variables the same way they do inside functions.
/// </summary>
[Trait("Category", "Checker")]
public class TopLevelImportResolutionTests
{
    private const string Lib = """
        <?tyhp
        namespace Lib;

        final class Handle implements \Tyhp\Contracts\IsDisposable {
            public function size(): int { return 1; }
            public function dispose(): void {}
        }

        final class File {
            public static function open(): Handle { return new Handle(); }
            public static function count(): int { return 3; }
        }
        """;

    [Fact]
    public void TopLevelStaticCall_OnImportedClass_ResolvesReturnType()
    {
        var errors = Errors("""
            <?tyhp
            use Lib\File;

            int $n = File::count();
            echo $n;
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void TopLevelStaticCall_OnImportedClass_ReturnTypeIsChecked()
    {
        var errors = Errors("""
            <?tyhp
            use Lib\File;

            string $n = File::count();
            echo $n;
            """);

        errors.Should().Contain(e => e.Contains("TYHP4008", StringComparison.Ordinal));
    }

    [Fact]
    public void TopLevelStaticCall_OnMissingClass_IsReported()
    {
        var errors = Errors("""
            <?tyhp
            Missing::go();
            """);

        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void UsingHeader_TypedVariable_IsDeclaredInBody()
    {
        var errors = Errors("""
            <?tyhp
            use Lib\File;
            use Lib\Handle;

            function run(): void {
                using (Handle $h = File::open()) {
                    echo $h->size();
                }
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void UsingHeader_InferredVariable_IsDeclaredInBody()
    {
        var errors = Errors("""
            <?tyhp
            use Lib\File;

            function run(): void {
                using ($g = File::open()) {
                    echo $g->size();
                }
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void UsingHeader_MultipleVariables_AreDeclaredInBody()
    {
        var errors = Errors("""
            <?tyhp
            use Lib\Handle;

            function run(): int {
                using (Handle $a = new Handle(), Handle $b = new Handle()) {
                    echo $a->size() + $b->size();
                }
                return 0;
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void UsingBlock_ReturnInBody_SatisfiesAllPathsReturn()
    {
        var errors = Errors("""
            <?tyhp
            use Lib\File;
            use Lib\Handle;

            function run(): int {
                using (Handle $c = File::open()) {
                    return $c->size();
                }
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void UsingHeader_TypedVariable_IsCheckedAgainstResourceType()
    {
        var errors = Errors("""
            <?tyhp
            use Lib\File;

            function run(): void {
                using (string $c = File::open()) {
                    echo 1;
                }
            }
            """);

        errors.Should().Contain(e => e.Contains("TYHP4008", StringComparison.Ordinal));
    }

    private static List<string> Errors(string main) =>
        CompileAndCheck(Lib, main)
            .Errors
            .Select(e => $"TYHP{(int)e.Code}: {e.Message}")
            .ToList();

    private static DiagnosticBag CompileAndCheck(string lib, string main)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var libPath = Path.Combine(tempDir, "lib.tyhp");
        var mainPath = Path.Combine(tempDir, "main.tyhp");
        File.WriteAllText(libPath, lib);
        File.WriteAllText(mainPath, main);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([libPath, mainPath], options);
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
