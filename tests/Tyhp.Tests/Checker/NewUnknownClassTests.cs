using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>new Missing()</c> on a class that is not declared, imported, or built in reports the same
/// unresolved-receiver diagnostic (TYHP4197) a static call on that name gets.
/// </summary>
[Trait("Category", "Checker")]
public class NewUnknownClassTests
{
    private const string Unresolved = "TYHP4197";

    [Fact]
    public void New_OnUndeclaredClass_ReportsUnresolvedReceiver()
    {
        var errors = Errors("""
            <?tyhp
            function f(): void {
                $x = new Missing();
            }
            """);

        errors.Should().ContainSingle(e => e.Contains(Unresolved, StringComparison.Ordinal));
    }

    [Fact]
    public void New_OnUndeclaredClassWithArguments_ReportsOnce()
    {
        var errors = Errors("""
            <?tyhp
            function f(): void {
                $x = new Missing(1, 'a');
            }
            """);

        errors.Count(e => e.Contains(Unresolved, StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void New_OnUndeclaredNamespacedClass_ReportsUnresolvedReceiver()
    {
        var errors = Errors("""
            <?tyhp
            namespace App;

            function f(): void {
                $x = new \Nope\Missing();
            }
            """);

        errors.Should().ContainSingle(e => e.Contains(Unresolved, StringComparison.Ordinal));
    }

    [Fact]
    public void New_OnImportOfUndeclaredClass_ReportsUnresolvedReceiver()
    {
        var errors = Errors("""
            <?tyhp
            use Other\Imported;
            use Other\Original as Alias;

            function f(): void {
                $x = new Imported();
                $y = new Alias();
            }
            """);

        errors.Count(e => e.Contains(Unresolved, StringComparison.Ordinal)).Should().Be(2);
    }

    [Fact]
    public void New_OnUndeclaredClassAtTopLevel_ReportsUnresolvedReceiver()
    {
        var errors = Errors("""
            <?tyhp
            $x = new Missing();
            """);

        errors.Should().Contain(e => e.Contains(Unresolved, StringComparison.Ordinal));
    }

    [Fact]
    public void New_OnClassDeclaredLaterInFile_IsAccepted()
    {
        var errors = Errors("""
            <?tyhp
            function f(): Later {
                return new Later();
            }

            class Later {}
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void New_OnClassDeclaredInAnotherFile_IsAccepted()
    {
        var errors = Errors("""
            <?tyhp
            use Lib\Widget;

            function f(): Widget {
                return new Widget();
            }
            """,
            """
            <?tyhp
            namespace Lib;

            class Widget {}
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void New_OnAliasedImportOfDeclaredClass_IsAccepted()
    {
        var errors = Errors("""
            <?tyhp
            use Lib\Widget as W;

            function f(): W {
                return new W();
            }
            """,
            """
            <?tyhp
            namespace Lib;

            class Widget {}
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void New_OnSelfStaticParent_IsAccepted()
    {
        var errors = Errors("""
            <?tyhp
            class Base {
                public static function make(): static {
                    return new static();
                }

                public function copy(): self {
                    return new self();
                }
            }

            class Child extends Base {
                public function up(): Base {
                    return new parent();
                }
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void New_OnAnonymousClass_IsAccepted()
    {
        var errors = Errors("""
            <?tyhp
            function f(): void {
                $x = new class {};
            }
            """);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void New_OnGenericTypeParameterClassString_IsNotReportedAsUnresolved()
    {
        var errors = Errors("""
            <?tyhp
            class Box<T> {
                public function __construct(public int $n) {}
            }

            function f(): Box<int> {
                return new Box<int>(1);
            }
            """);

        errors.Should().NotContain(e => e.Contains(Unresolved, StringComparison.Ordinal));
    }

    [Fact]
    public void New_OnVariableClassName_IsNotReportedAsUnresolved()
    {
        var errors = Errors("""
            <?tyhp
            class A {}

            function f(A $proto): void {
                $x = new $proto();
            }
            """);

        errors.Should().NotContain(e => e.Contains(Unresolved, StringComparison.Ordinal));
    }

    [Fact]
    public void New_OnPhpBuiltinAndTyhpdefClasses_IsAccepted()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function f(): void {
                $e = new \Exception('x');
                $o = new \stdClass();
                $d = new \DateTime();
                $w = new \Vendor\Gadget();
            }
            """,
            """
            <?tyhpdef
            class DateTime {
            }

            namespace Vendor {
                class Gadget {
                }
            }
            """);
        result.GlobalScope.Should().NotBeNull("bind should succeed");

        var symbolTree = new SymbolTree(result.GlobalScope!);
        new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!).Check(result.ParsedFiles!);

        result.Diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void StaticCall_OnUndeclaredClass_ReportsSameCode()
    {
        var errors = Errors("""
            <?tyhp
            function f(): void {
                Missing::go();
            }
            """);

        errors.Should().ContainSingle(e => e.Contains(Unresolved, StringComparison.Ordinal));
    }

    private static List<string> Errors(params string[] sources)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var paths = new List<string>();
        for (var i = 0; i < sources.Length; i++)
        {
            var path = Path.Combine(tempDir, $"file{i}.tyhp");
            File.WriteAllText(path, sources[i]);
            paths.Add(path);
        }

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles(paths, options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics.Errors
                .Select(e => $"TYHP{(int)e.Code}: {e.Message}")
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
