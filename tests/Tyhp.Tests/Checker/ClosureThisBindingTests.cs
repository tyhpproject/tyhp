using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Non-static closures must bind <c>$this</c> from the enclosing instance method (PHP does not
/// require <c>use ($this)</c>). Without that bind, property/index chains on <c>$this</c> fall
/// through to <c>mixed</c> and falsely report TYHP4160.
/// </summary>
[Trait("Category", "Checker")]
public class ClosureThisBindingTests
{
    [Fact]
    public void Check_ThisTypedArrayIndexMethodCall_InsideClosure_No4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Accessor {
                public function get(): mixed { return null; }
            }

            class Host {
                private array<string, Accessor> $map = [];

                public function boot(): void {
                    $fn = function (string $name, mixed &$out): bool {
                        if (!isset($this->map[$name])) {
                            return false;
                        }
                        $out = $this->map[$name]->get();
                        return true;
                    };
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_ThisTypedArrayIndexMethodCall_InsideTraitClosure_No4160()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Accessor {
                public function get(): mixed { return null; }
                public function set(mixed $value): void {}
            }

            trait Feature {
                private array<string, Accessor> $map = [];

                public function boot(): void {
                    $this->register(function (string $name, mixed &$out): bool {
                        if (!isset($this->map[$name])) {
                            return false;
                        }
                        $out = $this->map[$name]->get();
                        return true;
                    });
                    $this->registerSet(function (string $name, mixed $value): bool {
                        if (!isset($this->map[$name])) {
                            return false;
                        }
                        $this->map[$name]->set($value);
                        return true;
                    });
                }

                abstract protected function register(callable<string, mixed, bool> $handler): void;
                abstract protected function registerSet(callable<string, mixed, bool> $handler): void;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
    }

    [Fact]
    public void Check_StaticClosure_StillRejectsThis()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Host {
                private int $n = 0;

                public function boot(): void {
                    $fn = static function (): int {
                        return $this->n;
                    };
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerStaticClosureThis);
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
