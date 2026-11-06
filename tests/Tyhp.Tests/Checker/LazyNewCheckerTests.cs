using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>lazy new</c> reuses ordinary constructor diagnostics, rejects targets PHP cannot make lazy,
/// and warns when the constructor would run immediately.
/// </summary>
[Trait("Category", "Checker")]
public class LazyNewCheckerTests
{
    [Fact]
    public void LazyNew_ConstructorArgumentMismatch_UsesTheSameCodeAsNew()
    {
        const string body = """
            class User {
                public int $id;
                public function __construct(int $id) {}
            }

            function make(): User {
                return {0} User(1, 2);
            }
            """;

        var lazy = Compile("<?tyhp\n" + body.Replace("{0}", "lazy new"));
        var plain = Compile("<?tyhp\n" + body.Replace("{0}", "new"));

        Codes(lazy.Diagnostics.Errors).Should().NotBeEmpty();
        Codes(lazy.Diagnostics.Errors).Should().BeEquivalentTo(Codes(plain.Diagnostics.Errors));
        Codes(lazy.Diagnostics.Errors).Should().NotContain(MessageCode.CheckerLazyNewNotCapable);
    }

    [Fact]
    public void LazyNew_InternalClass_ReportsTyhp4402()
    {
        var result = CompileWithEngineStubs("""
            <?tyhp
            function make(): \DateTime {
                return lazy new \DateTime();
            }
            """);

        Codes(result.Diagnostics.Errors).Should().Contain(MessageCode.CheckerLazyNewNotCapable);
    }

    [Fact]
    public void LazyNew_ClassExtendingInternal_ReportsTyhp4402()
    {
        var result = CompileWithEngineStubs("""
            <?tyhp
            class Child extends \DateTime {
                public int $n;
            }

            function make(): Child {
                return lazy new Child();
            }
            """);

        Codes(result.Diagnostics.Errors).Should().Contain(MessageCode.CheckerLazyNewNotCapable);
    }

    [Fact]
    public void LazyNew_StdClass_IsAllowed()
    {
        var result = CompileWithEngineStubs("""
            <?tyhp
            function make(): \stdClass {
                return lazy new \stdClass();
            }
            """);

        Codes(result.Diagnostics.Errors).Should().NotContain(MessageCode.CheckerLazyNewNotCapable);
        Codes(result.Diagnostics.Warnings).Should().Contain(MessageCode.CheckerLazyNewHasNoLazyProperty);
    }

    [Fact]
    public void LazyNew_ClassExtendingStdClass_WithProperty_IsAllowed()
    {
        var result = CompileWithEngineStubs("""
            <?tyhp
            class Bag extends \stdClass {
                public int $n;
            }

            function make(): Bag {
                return lazy new Bag();
            }
            """);

        Codes(result.Diagnostics.Errors).Should().NotContain(MessageCode.CheckerLazyNewNotCapable);
        Codes(result.Diagnostics.Warnings).Should().NotContain(MessageCode.CheckerLazyNewHasNoLazyProperty);
    }

    [Fact]
    public void LazyNew_NoInstanceProperty_WarnsTyhp4403()
    {
        var result = Compile("""
            <?tyhp
            class Stateless {
                public static int $count = 0;
                public function ping(): void {}
            }

            function make(): Stateless {
                return lazy new Stateless();
            }
            """);

        Codes(result.Diagnostics.Errors).Should().NotContain(MessageCode.CheckerLazyNewNotCapable);
        Codes(result.Diagnostics.Warnings).Should().Contain(MessageCode.CheckerLazyNewHasNoLazyProperty);
    }

    [Fact]
    public void LazyNew_InstanceProperty_DoesNotWarn()
    {
        var result = Compile("""
            <?tyhp
            class User {
                public int $id;
                public function __construct(int $id) {
                    $this->id = $id;
                }
            }

            function make(): User {
                return lazy new User(1);
            }
            """);

        Codes(result.Diagnostics.Errors).Should().BeEmpty();
        Codes(result.Diagnostics.Warnings).Should().NotContain(MessageCode.CheckerLazyNewHasNoLazyProperty);
    }

    [Fact]
    public void LazyNew_GenericClassWithOnlyTheBag_DoesNotWarn()
    {
        var result = Compile("""
            <?tyhp
            class Box<T> {
                public function __construct(T $item) {}
                public function kind(): string {
                    return (string) typeof(T);
                }
            }

            function make(): Box<int> {
                return lazy new Box<int>(1);
            }
            """);

        Codes(result.Diagnostics.Errors).Should().BeEmpty();
        Codes(result.Diagnostics.Warnings).Should().NotContain(MessageCode.CheckerLazyNewHasNoLazyProperty);
    }

    // The warning is reported after the walk, so a `lazy new` checked before the class it names
    // has been flagged for generic tracking still sees the bag.
    [Fact]
    public void LazyNew_GenericClassDeclaredAfterTheCall_DoesNotWarn()
    {
        var result = Compile("""
            <?tyhp
            function make(): Box<int> {
                return lazy new Box<int>(1);
            }

            class Box<T> {
                public function __construct(T $item) {}
                public function kind(): string {
                    return (string) typeof(T);
                }
            }
            """);

        Codes(result.Diagnostics.Errors).Should().BeEmpty();
        Codes(result.Diagnostics.Warnings).Should().NotContain(MessageCode.CheckerLazyNewHasNoLazyProperty);
    }

    [Fact]
    public void LazyNew_SubclassOfTrackedGenericClass_DoesNotWarn()
    {
        var result = Compile("""
            <?tyhp
            class Base<A> {
                public function kind(): string {
                    return (string) typeof(A);
                }
            }

            class IntBase extends Base<int> {}

            function make(): IntBase {
                return lazy new IntBase();
            }
            """);

        Codes(result.Diagnostics.Errors).Should().BeEmpty();
        Codes(result.Diagnostics.Warnings).Should().NotContain(MessageCode.CheckerLazyNewHasNoLazyProperty);
    }

    [Fact]
    public void LazyNew_GenericClassWithoutRuntimeUse_WarnsBecauseItStoresNothing()
    {
        var result = Compile("""
            <?tyhp
            class Box<T> {
                public function __construct(T $item) {}
            }

            function make(): Box<int> {
                return lazy new Box<int>(1);
            }
            """);

        Codes(result.Diagnostics.Errors).Should().BeEmpty();
        Codes(result.Diagnostics.Warnings).Should().Contain(MessageCode.CheckerLazyNewHasNoLazyProperty);
    }

    [Fact]
    public void LazyNew_Interface_ReportsTyhp4402()
    {
        var result = Compile("""
            <?tyhp
            interface Clock {}

            function make(): void {
                $clock = lazy new Clock();
            }
            """);

        Codes(result.Diagnostics.Errors).Should().Contain(MessageCode.CheckerLazyNewNotCapable);
    }

    private static CompilationResult Compile(string tyhp) =>
        IsolatedCompilation.ParseSnippet(tyhp, phpVersion: "8.4");

    private static CompilationResult CompileWithEngineStubs(string tyhp)
    {
        var root = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        var engineDir = Path.Combine(root, "packages", "tyhpdef", "php");
        Directory.CreateDirectory(engineDir);
        var stub = Path.Combine(engineDir, "engine.tyhpdef");
        File.WriteAllText(stub, """
            <?tyhpdef
            class stdClass {
            }

            class DateTime {
                public function __construct(string $datetime = "now"): void;
            }
            """);
        var user = Path.Combine(root, "app.tyhp");
        File.WriteAllText(user, tyhp);
        try
        {
            using var compilation = new CompilationService();
            return compilation.ParseFiles(
                [user],
                IsolatedCompilation.CreateOptions(
                    root,
                    phpVersion: "8.4",
                    tyhpdefIncludePaths: [stub],
                    includeMinimalPhpStubs: false));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static List<MessageCode> Codes(IReadOnlyList<IDiagnostic> diagnostics) =>
        diagnostics.Select(diagnostic => diagnostic.Code).ToList();
}
