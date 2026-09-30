using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 21.12 A — full erase, <c>EraseGeneric</c>, library stamps, and foreign
/// <c>Generic::bind</c> consumer emit.
/// </summary>
[Trait("Category", "Emitter")]
public class GenericEraseEmitterTests
{
    [Fact]
    public void Emit_IdentityMethod_StampsErasedGenericRuntimeWithoutBinder()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function identity<T>(T $value): T {
                    return $value;
                }
            }
            """);

        php.Should().NotContain("identity__tyhpGeneric");
        php.Should().Contain("#[\\Tyhp\\GenericRuntime(erased: true, layouts: [1]");
        php.Should().Contain("function identity(");
    }

    [Fact]
    public void Emit_BoxWithGenericProperty_EmitsMechanismC()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public T $value;
            }
            """);

        php.Should().Contain(@"use \Tyhp\Concerns\HasGenerics;");
        php.Should().Contain("new_Box__tyhpGeneric");
        php.Should().Contain("erased: false");
        php.Should().Contain("factory: \"new_Box__tyhpGeneric\"");
        php.Should().Contain("__tyhpGeneric->setPropertyType('value'");
    }

    [Fact]
    public void Emit_BoxWithClassLevelEraseGeneric_FullyErased()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\EraseGeneric]
            class Box<T> {
                public T $value;
            }
            """);

        php.Should().NotContain(@"use \Tyhp\Concerns\HasGenerics");
        php.Should().NotContain("new_Box__tyhpGeneric");
        php.Should().NotContain("setPropertyType");
        php.Should().Contain("erased: true");
        php.Should().Contain("@template T");
        php.Should().NotContain("EraseGeneric");
    }

    [Fact]
    public void Emit_TypeofWithClassLevelEraseGeneric_MixedMode()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\EraseGeneric]
            class Box<T> {
                public T $value;
                public function describe(): \Tyhp\Type {
                    return typeof(T);
                }
            }
            """);

        php.Should().Contain(@"use \Tyhp\Concerns\HasGenerics;");
        php.Should().Contain("new_Box__tyhpGeneric");
        php.Should().Contain("erased: false");
        php.Should().NotContain("setPropertyType");
        php.Should().NotContain("checkProperty('value'");
    }

    [Fact]
    public void Emit_SameCompilationTrackedNew_UsesFactory()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public T $value;
            }
            function make(): Box {
                return new Box<int>();
            }
            """);

        php.Should().Contain("return \\Box::new_Box__tyhpGeneric(\\Tyhp\\Type::int());");
        php.Should().NotContain("Generic::bind");
    }

    [Fact]
    public void Emit_BareGenericNew_InfersFactoryTypeArgumentFromReturnType()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box<T> {
                public T $value;
            }
            function make(): Box<int> {
                return new Box();
            }
            """);

        php.Should().Contain("return \\Box::new_Box__tyhpGeneric(\\Tyhp\\Type::int());");
        php.Should().NotContain("Generic::bind");
    }

    [Fact]
    public void Emit_SameCompilationErasedNew_StaysPlainNew()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Pair<A, B> {
                public function __construct(A $a, B $b): void {}
            }
            function make(): Pair {
                return new Pair<int, string>(1, 'x');
            }
            """);

        php.Should().Contain("return new Pair(1, 'x');");
        php.Should().Contain("erased: true");
        php.Should().NotContain("Generic::bind");
        php.Should().NotContain("new_Pair__tyhpGeneric");
    }

    [Fact]
    public void Emit_ForeignGenericRuntime_UsesGenericBind()
    {
        var php = CompileAndEmit(
            """
            <?tyhp
            namespace App;
            function make(): \Vendor\Box {
                return new \Vendor\Box<int>(42);
            }
            """,
            """
            <?tyhpdef
            namespace Vendor;
            #[\Tyhp\GenericRuntime(erased: false, factory: "new_Vendor_Box__tyhpGeneric", layouts: [1])]
            class Box<T> {
                public function __construct(T $v): void;
            }
            """);

        php.Should().Contain("\\Tyhp\\Generic::bind(\\Vendor\\Box::class, \\Tyhp\\Type::int())(42)");
        php.Should().NotContain("new_Vendor_Box__tyhpGeneric");
        php.Should().NotContain("new \\Vendor\\Box(42)");
    }

    [Fact]
    public void Emit_ForeignGenericCallable_UsesGenericBind()
    {
        var php = CompileAndEmit(
            """
            <?tyhp
            namespace App;
            function run(\Vendor\Holder $h, string $value): string {
                return $h->identity<string>($value);
            }
            """,
            """
            <?tyhpdef
            namespace Vendor;
            class Holder {
                #[\Tyhp\GenericRuntime(erased: true, layouts: [1])]
                public function identity<T>(T $value): T;
            }
            """);

        php.Should().Contain("\\Tyhp\\Generic::bind($h->identity(...), \\Tyhp\\Type::string())($value)");
        php.Should().NotContain("identity__tyhpGeneric");
    }

    [Fact]
    public void Emit_IteratorOverlayWithoutStamp_StaysPlainPhp()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function walk(\Iterator<int, string> $it): void {}
            """);

        php.Should().Contain("function walk(\\Iterator $it): void");
        php.Should().NotContain("Generic::bind");
    }

    [Fact]
    public void Emit_UnsupportedLayouts_Reports5023()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpPath = Path.Combine(tempDir, "main.tyhp");
        var defPath = Path.Combine(tempDir, "lib.tyhpdef");
        File.WriteAllText(tyhpPath, """
            <?tyhp
            namespace App;
            function make(): \Vendor\Box {
                return new \Vendor\Box<int>(42);
            }
            """);
        File.WriteAllText(defPath, """
            <?tyhpdef
            namespace Vendor;
            #[\Tyhp\GenericRuntime(erased: true, layouts: [99], compiler: "future")]
            class Box<T> {
                public function __construct(T $v): void;
            }
            """);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [tyhpPath],
                IsolatedCompilation.CreateOptions(
                    tempDir,
                    phpVersion: "8.4",
                    tyhpdefIncludePaths: [defPath]));
            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = "8.4",
                })
                .Build();
            var project = new Project(configuration);
            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets);
            new TyhpEmitter(context).Emit(result.ParsedFiles!);
            result.Diagnostics.Errors.Should().Contain(d =>
                d.Code == MessageCode.EmitterGenericRuntimeLayoutUnsupported);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string CompileAndEmit(string tyhp, string? tyhpdef = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "generic-erase.tyhp");
        File.WriteAllText(filePath, tyhp);
        string? tyhpdefPath = null;
        if (tyhpdef is not null)
        {
            tyhpdefPath = Path.Combine(tempDir, "lib.tyhpdef");
            File.WriteAllText(tyhpdefPath, tyhpdef);
        }

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
            var files = tyhpdefPath is null
                ? new[] { filePath }
                : new[] { filePath };
            var includes = tyhpdefPath is null ? null : new List<string> { tyhpdefPath };
            var result = compilationService.ParseFiles(
                files,
                IsolatedCompilation.CreateOptions(
                    tempDir,
                    phpVersion: "8.4",
                    tyhpdefIncludePaths: includes));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets,
                expressionTypes: result.ExpressionTypes);
            return string.Join(
                '\n',
                new TyhpEmitter(context).Emit(result.ParsedFiles!)
                    .Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
