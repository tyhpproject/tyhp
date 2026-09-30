using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.6 Phase 0: only <c>\Iterator</c> and <c>\IteratorAggregate</c> may extend
/// <c>\Traversable</c>. User <c>.tyhp</c> types must not list Traversable in
/// <c>extends</c>/<c>implements</c>. Harvested <c>.tyhpdef</c> shells are a different AST and are
/// skipped structurally; <c>#[\Tyhp\Php]</c> is an ordinary version gate and does not exempt a
/// real <c>.tyhp</c> declaration.
/// </summary>
[Trait("Category", "Checker")]
public class TraversableImplementsRuleTests
{
    [Fact]
    public void Check_UserClassImplementsTraversable_Reports4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C implements \Traversable {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
        error.Message.Should().Contain("C");
        error.Message.Should().Contain("Traversable");
    }

    [Fact]
    public void Check_UserClassImplementsIterator_DoesNotReport4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            abstract class C implements \Iterator {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_UserClassImplementsIteratorAggregate_DoesNotReport4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            abstract class C implements \IteratorAggregate {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_UserClassImplementsIteratorAndTraversable_Reports4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            abstract class C implements \Iterator, \Traversable {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_InterfaceExtendsTraversable_Reports4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Foo extends \Traversable {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
        error.Message.Should().Contain("Foo");
    }

    [Fact]
    public void Check_InterfaceExtendsIterator_DoesNotReport4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Foo extends \Iterator {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_InterfaceExtendsIteratorAndTraversable_Reports4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Foo extends \Iterator, \Traversable {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_NamespacedInterfaceNamedIteratorExtendsTraversable_Reports4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;
            interface Iterator extends \Traversable {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_EnumImplementsTraversable_Reports4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E implements \Traversable { case A; }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_EnumImplementsIterator_DoesNotReport4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum E implements \Iterator {
                case A;
                public function current(): mixed { return null; }
                public function key(): mixed { return null; }
                public function next(): void {}
                public function rewind(): void {}
                public function valid(): bool { return false; }
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_TraitImplementsTraversable_Reports4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T implements \Traversable {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_TraitImplementsIterator_DoesNotReport4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T implements \Iterator {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_TraitRequiresTraversableViaExtends_Reports4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait T extends \Traversable {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_AnonymousClassImplementsTraversable_Reports4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function make(): object {
                return new class implements \Traversable {};
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
        var error = diagnostics.Errors.First(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
        error.Message.Should().Contain("anonymous class");
    }

    [Fact]
    public void Check_HarvestedGeneratorImplementsIteratorAndTraversable_DoesNotReport4326()
    {
        var diagnostics = CompileTyhpdefAsSource("""
            <?tyhpdef
            interface Traversable {}
            interface Iterator extends \Traversable {
                public function current(): mixed;
                public function key(): mixed;
                public function next(): void;
                public function rewind(): void;
                public function valid(): bool;
            }
            final class Generator implements \Iterator, \Traversable {
                public function current(): mixed;
                public function key(): mixed;
                public function next(): void;
                public function rewind(): void;
                public function valid(): bool;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    /// <summary>
    /// <c>#[\Tyhp\Php]</c> is an ordinary compile-time PHP-version gate available on any user
    /// declaration (see <c>tyhp_0320_phpVersionGating.md</c>). Actual tyhpdef harvest output is
    /// always a <c>.tyhpdef</c> file, which binds to a different AST
    /// (<c>TyhpdefImportObjectDeclAst</c>) that never reaches this rule. A version-gated
    /// <c>.tyhp</c> class is still real user code, so it must still be diagnosed.
    /// </summary>
    [Fact]
    public void Check_TyhpPhpGatedUserClassImplementsTraversable_Reports4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php(">=8.0")]
            class C implements \Traversable {}
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
    }

    [Fact]
    public void Check_TyhpPhpGatedUserClassImplementsIterator_DoesNotReport4326()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Php(">=8.0")]
            abstract class C implements \Iterator {}
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.CheckerTraversableCannotBeListed);
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

    private static DiagnosticBag CompileTyhpdefAsSource(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "lib.tyhpdef");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: "8.4",
                skipChecking: true);
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
