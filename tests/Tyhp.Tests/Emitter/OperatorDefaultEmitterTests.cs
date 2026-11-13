using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Emit shape for <c>operator default</c>: <c>public static function __tyhpDefault(): static</c>,
/// and <c>default(Class)</c> becomes <c>\Class::__tyhpDefault()</c> only when that class declares it.
/// </summary>
[Trait("Category", "Emitter")]
public class OperatorDefaultEmitterTests
{
    [Fact]
    public void Emit_NoOperator_DefaultOfClass_IsNull()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class MyClass { public int $n = 1; }
            function f(): mixed { return default(MyClass); }
            """);

        php.Should().Contain("return null");
        php.Should().NotContain("__tyhpDefault");
    }

    [Fact]
    public void Emit_WithOperator_CallsStaticMethod()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class MyClass {
                public int $amount = 0;
                operator default(): self { return new self(4); }
                public function __construct(int $amount = 0) {
                    $this->amount = $amount;
                }
            }
            function f(): MyClass { return default(MyClass); }
            """);

        php.Should().Contain("public static function __tyhpDefault(): static");
        php.Should().Contain("MyClass::__tyhpDefault()");
        php.Should().NotContain("defaultValue(");
    }

    [Fact]
    public void Emit_NullCoalesce_RunsOperatorWhenArgumentIsNull()
    {
        var files = CompileToFiles("""
            <?tyhp
            namespace Probe;
            class MyClass {
                public int $amount;
                public function __construct(int $amount = 0) {
                    $this->amount = $amount;
                }
                operator default(): self { return new self(4); }
            }
            function myFunc(?MyClass $i = null): MyClass {
                return $i ?? default(MyClass);
            }
            function run(): void {
                echo myFunc(null)->amount;
                echo myFunc(new MyClass(9))->amount;
            }
            """);

        var php = string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));
        php.Should().Contain("MyClass::__tyhpDefault()");
        EmittedPhpRunner.Run(files, "\\Probe\\run();").Should().Be("49");
    }

    [Fact]
    public void Emit_ChildDoesNotCallParentOperator()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class BaseItem {
                operator default(): self { return new self(); }
            }
            class ChildItem extends BaseItem {}
            function child(): mixed { return default(ChildItem); }
            function fromBase(): BaseItem { return default(BaseItem); }
            """);

        php.Should().Contain("BaseItem::__tyhpDefault()");
        php.Should().NotContain("ChildItem::__tyhpDefault()");
        php.Should().Contain("return null");
    }

    [Fact]
    public void Emit_EnumOperator_CallsStaticMethod()
    {
        var php = CompileAndEmit("""
            <?tyhp
            enum Color {
                case None;
                case Red;
                operator default(): self { return self::None; }
            }
            function f(): Color { return default(Color); }
            """);

        php.Should().Contain("public static function __tyhpDefault(): static");
        php.Should().Contain("Color::__tyhpDefault()");
        php.Should().Contain("self::None");
    }

    [Fact]
    public void Emit_SelfAndStatic_InInstanceMethod_CallsOperator()
    {
        var files = CompileToFiles("""
            <?tyhp
            namespace Probe;
            class MyClass {
                public function __construct(public int $amount = 0) {}
                operator default(): self { return new self(5); }
                public function viaSelf(): MyClass { return default(self); }
                public function viaStatic(): MyClass { return default(static); }
            }
            function run(): void {
                $m = new MyClass(1);
                echo $m->viaSelf()->amount;
                echo $m->viaStatic()->amount;
            }
            """);

        var php = string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));
        php.Should().NotContain("return null");
        EmittedPhpRunner.Run(files, "\\Probe\\run();").Should().Be("55");
    }

    [Fact]
    public void Emit_NoOperator_SelfInInstanceMethod_IsNull()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class MyClass {
                public function make(): mixed { return default(self); }
            }
            """);

        php.Should().Contain("return null");
        php.Should().NotContain("__tyhpDefault");
    }

    [Fact]
    public void Emit_SelfAndStatic_InStaticMethod_CallsOperator()
    {
        var files = CompileToFiles("""
            <?tyhp
            namespace Probe;
            class MyClass {
                public function __construct(public int $amount = 0) {}
                operator default(): self { return new self(6); }
                public static function viaSelf(): MyClass { return default(self); }
                public static function viaStatic(): MyClass { return default(static); }
            }
            function run(): void {
                echo MyClass::viaSelf()->amount;
                echo MyClass::viaStatic()->amount;
            }
            """);

        var php = string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));
        php.Should().NotContain("return null");
        EmittedPhpRunner.Run(files, "\\Probe\\run();").Should().Be("66");
    }

    [Fact]
    public void Emit_SelfInChild_DoesNotUseParentOperator()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class BaseItem { operator default(): self { return new self(); } }
            class ChildItem extends BaseItem {
                public function viaSelf(): mixed { return default(self); }
                public function viaStatic(): mixed { return default(static); }
            }
            """);

        php.Should().Contain("return null");
        php.Should().NotContain("::__tyhpDefault();");
    }

    [Fact]
    public void Emit_Parent_DoesNotUseParentOperator()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class BaseItem { operator default(): self { return new self(); } }
            class ChildItem extends BaseItem {
                public function viaParent(): mixed { return default(parent); }
            }
            """);

        php.Should().Contain("return null");
        php.Should().NotContain("::__tyhpDefault();");
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("Parent")]
    [InlineData("PARENT")]
    public void Emit_Parent_AnySpelling_IsNull_InGrandchildStaticAndClosure(string spelled)
    {
        var php = CompileAndEmit($$"""
            <?tyhp
            class BaseItem { operator default(): self { return new self(); } }
            class MidItem extends BaseItem {}
            class LeafItem extends MidItem {
                public function viaMethod(): ?BaseItem { return default({{spelled}}); }
                public static function viaStatic(): ?BaseItem { return default({{spelled}}); }
                public function viaClosure(): ?BaseItem {
                    $f = function (): ?BaseItem { return default({{spelled}}); };
                    return $f();
                }
            }
            """);

        php.Should().Contain("return null");
        php.Should().NotContain("::__tyhpDefault();");
    }

    [Fact]
    public void Emit_EnumOperator_ReturnsCase_AtRuntime()
    {
        var files = CompileToFiles("""
            <?tyhp
            namespace Probe;
            enum Color {
                case None;
                case Red;
                operator default(): self { return self::None; }
            }
            function run(): void {
                $color = default(Color);
                echo $color->name;
            }
            """);

        EmittedPhpRunner.Run(files, "\\Probe\\run();").Should().Be("None");
    }

    [Fact]
    public void Emit_GenericParameter_StaysOnDefaultValue()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Sample<TValue> {
                public function zero(): mixed {
                    return default(TValue);
                }
            }
            """);

        php.Should().Contain("return $this->__tyhpGeneric->defaultValue(\\Sample::class, 'TValue');");
        php.Should().NotContain("__tyhpDefault");
    }

    private static string CompileAndEmit(string tyhp) =>
        string.Join('\n', CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "operator-default.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = "8.4",
                })
                .Build();
            var project = new Project(configuration);
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, project);
            return new TyhpEmitter(context).Emit(result.ParsedFiles!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
