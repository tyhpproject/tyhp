using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// <c>lazy new</c> emits a PHP 8.4 ghost when the class can stay lazy, and the existing
/// eager <c>new</c> or generic factory otherwise. Arguments are evaluated before the ghost.
/// </summary>
[Trait("Category", "Emitter")]
public class LazyNewEmitterTests
{
    [Fact]
    public void LazyNew_EmitsNewLazyGhost_WhenPhpVersionAtLeast84()
    {
        var php = Compile("""
            <?tyhp
            namespace App;

            class User {
                public int $id = 0;
            }

            function make(): User {
                return lazy new User();
            }
            """, "8.4");

        // `User` declares no constructor, so the initializer has nothing to call.
        php.Should().Contain("newLazyGhost");
        php.Should().Contain("static function (\\App\\User $__tyhp_obj): void {}");
        php.Should().NotContain("__construct");
        php.Should().NotContain("$__tyhpLazy");
        php.Should().NotContain("\\Tyhp\\Lazy::ghost");
    }

    [Fact]
    public void LazyNew_ClassWithConstructor_InitializerCallsItOnTheSameInstance()
    {
        var php = Compile("""
            <?tyhp
            namespace App;

            class User {
                public int $id;
                public function __construct() {
                    $this->id = 1;
                }
            }

            function make(): User {
                return lazy new User();
            }
            """, "8.4");

        php.Should().Contain(
            "(new \\ReflectionClass(\\App\\User::class))->newLazyGhost("
            + "static function (\\App\\User $__tyhp_obj): void { $__tyhp_obj->__construct(); })");
    }

    [Fact]
    public void LazyNew_StaticTarget_KeepsLateStaticBinding()
    {
        var php = Compile("""
            <?tyhp
            namespace App;

            class Model {
                public int $id = 0;
                public function __construct() {}
                public static function make(): static {
                    return lazy new static();
                }
            }
            """, "8.4");

        php.Should().Contain("(new \\ReflectionClass(static::class))->newLazyGhost(");
        php.Should().Contain("static function (object $__tyhp_obj): void");
    }

    [Fact]
    public void LazyNew_EmitsEagerNew_WhenPhpVersionBelow84()
    {
        var php = Compile("""
            <?tyhp
            namespace App;

            class User {
                public int $id;
                public function __construct(int $id) {
                    $this->id = $id;
                }
            }

            function make(int $id): User {
                return lazy new User($id);
            }
            """, "8.2");

        php.Should().NotContain("newLazyGhost");
        php.Should().NotContain("__tyhpLazy");
        php.Should().Contain("new User($id)");
    }

    [Fact]
    public void LazyNew_NoLazyProperty_EmitsEagerNew_WhenPhpVersionAtLeast84()
    {
        var php = Compile("""
            <?tyhp
            namespace App;

            class Stateless {
                public function ping(): void {}
            }

            function make(): Stateless {
                return lazy new Stateless();
            }
            """, "8.4");

        php.Should().NotContain("newLazyGhost");
        php.Should().Contain("new Stateless()");
    }

    [Fact]
    public void LazyNew_ArgumentsEvaluatedBeforeGhost()
    {
        var php = Compile("""
            <?tyhp
            namespace App;

            class User {
                public int $id;
                public function __construct(int $id) {
                    $this->id = $id;
                }
            }

            function side(): int {
                return 1;
            }

            function make(): User {
                return lazy new User(side());
            }
            """, "8.4");

        // The argument is an argument of the wrapper call, evaluated in the caller's scope. The
        // ghost closure only forwards what the wrapper captured.
        php.Should().Contain("(static function (mixed ...$__tyhpLazyArgs): object {");
        php.Should().Contain("})(side())");
        php.Should().Contain("use ($__tyhpLazyArgs)");
        php.Should().Contain("$__tyhp_obj->__construct(...$__tyhpLazyArgs);");
        php.Should().NotContain("__construct(side())");
        php.Should().NotContain("$__tyhpLazyArgs = side()");
    }

    [Fact]
    public void LazyNew_GenericClass_InitHookRunsBeforeConstructor()
    {
        var php = Compile("""
            <?tyhp
            namespace Probe;

            class Box<T> {
                public int $n;
                public function __construct(T $item) {
                    $this->n = 1;
                }

                public function kind(): string {
                    return (string) typeof(T);
                }
            }

            function make(): Box<int> {
                return lazy new Box<int>(1);
            }
            """, "8.4");

        var lazyAt = php.IndexOf("function new_Probe_Box__tyhpLazy", StringComparison.Ordinal);
        lazyAt.Should().BeGreaterThan(0);
        var eagerBody = php[..lazyAt];
        var lazyBody = php[lazyAt..];

        eagerBody.Should().Contain("function new_Probe_Box__tyhpGeneric");
        eagerBody.Should().Contain("newInstanceWithoutConstructor");
        eagerBody.Should().NotContain("newLazyGhost");

        lazyBody.Should().Contain("newLazyGhost");
        lazyBody.Should().NotContain("newInstanceWithoutConstructor");
        var hook = lazyBody.IndexOf("__initGenerics__tyhpGeneric", StringComparison.Ordinal);
        var construct = lazyBody.IndexOf("__construct", StringComparison.Ordinal);
        hook.Should().BeGreaterThanOrEqualTo(0);
        construct.Should().BeGreaterThan(hook);
        lazyBody.Should().Contain("__initGenerics__tyhpGeneric($__generic_T)");
        php.Should().NotContain("\\Tyhp\\Lazy::ghost");
    }

    [Fact]
    public void LazyNew_GenericWithOnlyTheBag_UsesTheLazyFactory()
    {
        var php = Compile("""
            <?tyhp
            namespace Probe;

            class Box<T> {
                public function kind(): string {
                    return (string) typeof(T);
                }
            }

            function make(): Box<int> {
                return lazy new Box<int>();
            }
            """, "8.4");

        // The injected `$__tyhpGeneric` bag is a stored property, so the ghost stays lazy.
        php.Should().Contain("Box::new_Probe_Box__tyhpLazy(");
        php.Should().Contain("function new_Probe_Box__tyhpGeneric");
        php.Should().Contain("newLazyGhost");
    }

    [Fact]
    public void LazyNew_GenericClass_BelowPhp84_UsesTheEagerFactoryAndEmitsNoLazyFactory()
    {
        var php = Compile("""
            <?tyhp
            namespace Probe;

            class Box<T> {
                public int $n;
                public function __construct(T $item) {
                    $this->n = 1;
                }

                public function kind(): string {
                    return (string) typeof(T);
                }
            }

            function make(): Box<int> {
                return lazy new Box<int>(1);
            }
            """, "8.3");

        php.Should().Contain("Box::new_Probe_Box__tyhpGeneric(");
        php.Should().NotContain("__tyhpLazy");
        php.Should().NotContain("newLazyGhost");
    }

    [Fact]
    public void LazyNew_GenericSubclassChain_EachLevelBindsOnlyItsOwnArguments()
    {
        var php = Compile("""
            <?tyhp
            namespace Probe;

            class Base<A> {
                public string $tag;
                public function __construct(string $tag) {
                    $this->tag = $tag;
                }
                public function baseA(): string {
                    return (string) typeof(A);
                }
            }

            class Leaf<B> extends Base<string> {
                public function __construct(string $tag): parent($tag) {}
                public function leafB(): string {
                    return (string) typeof(B);
                }
            }

            function make(): Leaf<int> {
                return lazy new Leaf<int>('t');
            }
            """, "8.4");

        php.Should().Contain("Leaf::new_Probe_Leaf__tyhpLazy(");
        var lazyAt = php.IndexOf("function new_Probe_Leaf__tyhpLazy", StringComparison.Ordinal);
        lazyAt.Should().BeGreaterThan(0);
        var lazyFactory = php[lazyAt..];
        var closureEnd = lazyFactory.IndexOf("function new_", 20, StringComparison.Ordinal);
        if (closureEnd > 0)
        {
            lazyFactory = lazyFactory[..closureEnd];
        }

        lazyFactory.Should().Contain("__initGenerics__tyhpGeneric($__generic_B);");
        lazyFactory.Should().NotContain("__initGenerics__tyhpGeneric($__generic_A");
    }

    [Fact]
    public void LazyNew_ReadonlyGeneric_BootsBagInsideHook()
    {
        var php = Compile("""
            <?tyhp
            namespace Probe;

            readonly class Box<T> {
                public function __construct(public int $n) {}

                public function kind(): string {
                    return (string) typeof(T);
                }
            }

            function make(): Box<int> {
                return lazy new Box<int>(1);
            }
            """, "8.4");

        php.Should().Contain("$this->__bootGenericBag__tyhpGeneric();");
        var lazyAt = php.IndexOf("function new_Probe_Box__tyhpLazy", StringComparison.Ordinal);
        lazyAt.Should().BeGreaterThan(0);
        var lazyBody = php[lazyAt..];
        var hook = lazyBody.IndexOf("__initGenerics__tyhpGeneric", StringComparison.Ordinal);
        var construct = lazyBody.IndexOf("__construct", StringComparison.Ordinal);
        hook.Should().BeGreaterThanOrEqualTo(0);
        construct.Should().BeGreaterThan(hook);
        lazyBody.Should().Contain("static $__reflectedClass__tyhpGeneric = null;");
        php[..lazyAt].Should().NotContain("private static ?\\ReflectionClass $__reflectedClass__tyhpGeneric");
        php.Should().NotContain("\\Tyhp\\Lazy::ghost");
    }

    [Fact]
    public void MethodNamedLazy_EmitsUnchangedCall()
    {
        var php = Compile("""
            <?tyhp
            namespace App;

            class Query {
                public int $limit = 0;
                public function lazy(): int {
                    return 1;
                }
            }

            function demo(Query $q): int {
                return $q->lazy();
            }
            """, "8.4");

        php.Should().Contain("->lazy(");
        php.Should().NotContain("newLazyGhost");
        php.Should().NotContain("T_TYHP_LAZY");
    }

    private static string Compile(string tyhp, string phpVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "lazy-new.tyhp");
        File.WriteAllText(filePath, tyhp);
        try
        {
            var project = CreateProject(phpVersion);
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion));
            var unexpectedErrors = result.Diagnostics.Errors
                .Where(diagnostic => !(diagnostic.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(diagnostic => diagnostic.Code != MessageCode.BinderUnresolvedParameterType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(error => $"{error.Code}: {error.Message}"))}");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                inferredClosureSignatures: result.InferredClosureSignatures,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(file => file.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static Project CreateProject(string phpVersion)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = phpVersion,
            })
            .Build();
        return new Project(configuration);
    }
}
