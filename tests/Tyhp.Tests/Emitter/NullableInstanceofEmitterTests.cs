using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Covers FOUND_BUGS #21 — <c>$x is ?T</c> parses to a synthetic prefix <c>?</c> unary wrapping
/// the real target (<see cref="Tyhp.TyhpLang.Checker.Rules.CheckerHelpers.IsNullableInstanceofMarker"/>).
/// There is no PHP <c>instanceof ?ClassName</c> syntax, so this must always reify to
/// <c>\Tyhp\Type::is($x, \Tyhp\Type::nullable(&lt;target&gt;))</c> — never native <c>instanceof</c>,
/// even for a plain declared class that would otherwise stay native (see
/// <see cref="InstanceofGenericParameterEmitterTests.Emit_InstanceofConcreteClass_RemainsNativePhp"/>).
/// </summary>
[Trait("Category", "Emitter")]
public class NullableInstanceofEmitterTests
{
    [Fact]
    public void Emit_IsNullableClass_ReifiesToTypeNullable()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class A {}
            function demo(mixed $x): bool {
                return $x is ?A;
            }
            """);

        php.Should().Contain(
            "\\Tyhp\\Type::is($x, \\Tyhp\\Type::nullable(\\Tyhp\\Type::fromClassName('A'::class)))");
        php.Should().NotContain("$x instanceof ?A");
        php.Should().NotContain("$x is ?A");
        php.Should().NotContain("$x instanceof A");
    }

    [Fact]
    public void Emit_IsNullableSelf_ReifiesToTypeNullableFromClassName()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class A {
                function demo(mixed $x): bool {
                    return $x is ?self;
                }
            }
            """);

        php.Should().Contain(
            "\\Tyhp\\Type::is($x, \\Tyhp\\Type::nullable(\\Tyhp\\Type::fromClassName(self::class)))");
        php.Should().NotContain("resolvedType(");
        php.Should().NotContain("'self'");
        php.Should().NotContain("$x instanceof");
    }

    [Fact]
    public void Emit_IsNullableScalar_ReifiesToTypeNullable()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(mixed $x): bool {
                return $x is ?int;
            }
            """);

        php.Should().Contain(
            "\\Tyhp\\Type::is($x, \\Tyhp\\Type::nullable(\\Tyhp\\Type::int()))");
        php.Should().NotContain("$x instanceof ?int");
    }

    [Fact]
    public void Emit_IsNullableAlias_ReifiesToTypeNullableWrappingAliasFactory()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type UserId = int;
            function demo(mixed $x): bool {
                return $x is ?UserId;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($x, \\Tyhp\\Type::nullable(UserId()))");
        php.Should().NotContain("$x instanceof ?UserId");
        php.Should().NotContain("$x is ?UserId");
    }

    [Fact]
    public void Emit_IsThenTernary_KeepsNativeInstanceofAndTernary()
    {
        // `$x is A ? 1 : 0` must not be mistaken for `$x is ?A` — the `?` belongs to the ternary.
        var php = CompileAndEmit("""
            <?tyhp
            class A {}
            function demo(mixed $x): int {
                return $x is A ? 1 : 0;
            }
            """);

        php.Should().Contain("($x instanceof A) ? 1 : 0");
        php.Should().NotContain("\\Tyhp\\Type::is($x");
        php.Should().NotContain("\\Tyhp\\Type::nullable(");
    }

    private static string CompileAndEmit(string tyhp) =>
        string.Join(
            '\n',
            CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "nullable-instanceof.tyhp");
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
            var result = compilationService.ParseFiles([filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

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
