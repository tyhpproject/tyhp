using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Covers FOUND_BUGS item 37 — <c>instanceof T</c> / <c>is T</c> against a generic type
/// parameter reifies to <c>\Tyhp\Type::is($value, …)</c> via Mechanism D binders / Mechanism C
/// GenericObject, rather than emitting a bare PHP class name that always evaluates <c>false</c>.
/// </summary>
[Trait("Category", "Emitter")]
public class InstanceofGenericParameterEmitterTests
{
    [Fact]
    public void Emit_InstanceofClassGeneric_ReifiesToTypeIs()
    {
        var php = CompileAndEmit("""
            <?tyhp
            final class Box<TReturn extends void|mixed = void> {
                private TReturn $value;
                public function setIfMatches(mixed $value): void {
                    if ($value instanceof TReturn) {
                        $this->value = $value;
                    }
                }
            }
            """);

        php.Should().Contain(@"use \Tyhp\Concerns\HasGenerics;");
        php.Should().Contain(
            "\\Tyhp\\Type::is($value, $this->__tyhpGeneric->resolvedType(\\Box::class, 'TReturn'))");
        php.Should().NotContain("$value instanceof TReturn");
    }

    [Fact]
    public void Emit_IsAliasAgainstClassGeneric_ReifiesToTypeIs()
    {
        var php = CompileAndEmit("""
            <?tyhp
            final class Box<TReturn extends void|mixed = void> {
                public function matches(mixed $value): bool {
                    return $value is TReturn;
                }
            }
            """);

        php.Should().Contain(
            "\\Tyhp\\Type::is($value, $this->__tyhpGeneric->resolvedType(\\Box::class, 'TReturn'))");
        php.Should().NotContain("$value instanceof TReturn");
        php.Should().NotContain("$value is TReturn");
    }

    [Fact]
    public void Emit_InstanceofMethodGeneric_ReifiesViaVariantParam()
    {
        var php = CompileAndEmit("""
            <?tyhp
            final class Box {
                public static function runIt<T extends void|mixed>(mixed $t): ?T {
                    if ($t instanceof T) {
                        return $t;
                    }
                    return null;
                }
            }
            """);

        php.Should().Contain("function runIt__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
        php.Should().Contain("\\Tyhp\\Type::is($t, $__generic_T)");
        php.Should().NotContain("$t instanceof T");
    }

    [Fact]
    public void Emit_IsAliasAgainstMethodGeneric_ReifiesViaVariantParam()
    {
        var php = CompileAndEmit("""
            <?tyhp
            final class Box {
                public static function runIt<T extends void|mixed>(mixed $t): ?T {
                    if ($t is T) {
                        return $t;
                    }
                    return null;
                }
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($t, $__generic_T)");
        php.Should().NotContain("$t instanceof T");
    }

    [Fact]
    public void Emit_InstanceofFreeFunctionGeneric_ReifiesViaVariantParam()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function pick<T>(mixed $t): ?T {
                if ($t instanceof T) {
                    return $t;
                }
                return null;
            }
            """);

        php.Should().Contain("function pick__tyhpGeneric(?\\Tyhp\\Type $__generic_T): \\Closure");
        php.Should().Contain("\\Tyhp\\Type::is($t, $__generic_T)");
        php.Should().NotContain("$t instanceof T");
    }

    [Fact]
    public void Emit_InstanceofConcreteClass_RemainsNativePhp()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Foo {}
            final class Box<TReturn extends void|mixed = void> {
                public function isFoo(mixed $value): bool {
                    return $value instanceof Foo;
                }
            }
            """);

        php.Should().Contain("$value instanceof Foo");
        php.Should().NotContain("\\Tyhp\\Type::is($value");
        php.Should().NotContain(@"use \Tyhp\Concerns\HasGenerics");
    }

    [Fact]
    public void Emit_IsAliasAgainstConcreteClass_RemainsNativeInstanceof()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Foo {}
            function isFoo(mixed $value): bool {
                return $value is Foo;
            }
            """);

        php.Should().Contain("$value instanceof Foo");
        php.Should().NotContain("\\Tyhp\\Type::is($value");
        php.Should().NotContain("$value is Foo");
    }

    [Fact]
    public void Emit_IsInt_ReifiesToTypeIs()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function isInt(mixed $value): bool {
                return $value is int;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($value, \\Tyhp\\Type::int())");
        php.Should().NotContain("$value instanceof int");
        php.Should().NotContain("$value is int");
    }

    [Fact]
    public void Emit_InstanceofString_ReifiesToTypeIs()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function isString(mixed $value): bool {
                return $value instanceof string;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($value, \\Tyhp\\Type::string())");
        php.Should().NotContain("$value instanceof string");
    }

    [Fact]
    public void Emit_InstanceofSelf_RemainsNativePhp()
    {
        var php = CompileAndEmit("""
            <?tyhp
            final class Box<TReturn extends void|mixed = void> {
                public function isSelf(mixed $value): bool {
                    return $value instanceof self;
                }
            }
            """);

        php.Should().Contain("$value instanceof self");
        php.Should().NotContain("\\Tyhp\\Type::is($value");
    }

    [Fact]
    public void Emit_InstanceofGenericInsideLogicalAnd_Reifies()
    {
        var php = CompileAndEmit("""
            <?tyhp
            final class Box<TReturn extends void|mixed = void> {
                public function setIfMatches(mixed $value, bool $flag): void {
                    if ($flag && $value instanceof TReturn) {
                        $this->value = $value;
                    }
                }
                private TReturn $value;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($value, $this->__tyhpGeneric->resolvedType(\\Box::class, 'TReturn')");
        php.Should().NotContain("$value instanceof TReturn");
    }

    [Fact]
    public void Emit_InstanceofSameLetterAsUnrelatedClass_RemainsNativePhp()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class T {}
            final class Box<TReturn extends void|mixed = void> {
                public function isT(mixed $value): bool {
                    return $value instanceof T;
                }
            }
            """);

        php.Should().Contain("$value instanceof T");
        php.Should().NotContain("\\Tyhp\\Type::is($value");
    }

    [Fact]
    public void Emit_InstanceofGenericInsideNestedClosure_CapturesVariantParam()
    {
        var php = CompileAndEmit("""
            <?tyhp
            final class Box {
                public static function runIt<T extends void|mixed>(mixed $t): bool {
                    $check = function (mixed $value) use ($t): bool {
                        return $value instanceof T;
                    };
                    return $check($t);
                }
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($value, $__generic_T)");
        php.Should().Contain("use ($t, $__generic_T)");
        php.Should().NotContain("$value instanceof T");
    }

    [Fact]
    public void Emit_InstanceofOnlyErasingGeneric_StillFlagsVariant()
    {
        // Unlike a method that only erases T in signatures, instanceof needs the bound type.
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function matches<T>(mixed $value): bool {
                    return $value instanceof T;
                }
            }
            """);

        php.Should().Contain("__tyhpGeneric");
        php.Should().Contain("\\Tyhp\\Type::is($value, $__generic_T)");
    }

    [Fact]
    public void Emit_InstanceofParameterizedSelf_ReifiesToTypeGeneric()
    {
        // `instanceof self<TReturn>` must not erase to bare `instanceof self` — the type
        // argument is checked via Type::is + Type::generic (GenericObject bindings).
        var php = CompileAndEmit("""
            <?tyhp
            final class Promise<TReturn extends void|mixed = void> {
                public function adopt(mixed $value): bool {
                    return $value instanceof self<TReturn>;
                }
            }
            """);

        php.Should().Contain(@"use \Tyhp\Concerns\HasGenerics;");
        php.Should().Contain("\\Tyhp\\Type::is($value,");
        php.Should().Contain("\\Tyhp\\Type::generic(");
        php.Should().Contain("__tyhpGeneric->resolvedType(\\Promise::class, 'TReturn')");
        php.Should().NotContain("$value instanceof self;");
        php.Should().NotContain("$value instanceof static");
    }

    [Fact]
    public void Emit_InstanceofParameterizedClass_ReifiesToTypeGeneric()
    {
        var php = CompileAndEmit("""
            <?tyhp
            final class Box<T> {
                public function isSameBox(mixed $value): bool {
                    return $value instanceof Box<T>;
                }
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($value,");
        php.Should().Contain("\\Tyhp\\Type::generic(\\Box::class");
        php.Should().NotContain("$value instanceof Box");
    }

    [Fact]
    public void Emit_InstanceofBareStatic_KeepsNativeInstanceof()
    {
        var php = CompileAndEmit("""
            <?tyhp
            final class Promise {
                public function isPromise(mixed $value): bool {
                    return $value instanceof static;
                }
            }
            """);

        php.Should().Contain("$value instanceof static");
        php.Should().NotContain("\\Tyhp\\Type::is($value");
    }

    private static string CompileAndEmit(string tyhp) =>
        string.Join(
            '\n',
            CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "instanceof-generic.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = new Project(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = "8.4",
                })
                .Build());
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

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
                genericCallTargets: result.GenericCallTargets);
            return new TyhpEmitter(context).Emit(result.ParsedFiles!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
