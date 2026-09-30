using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
[Trait("Category", "Story271")]
public class CallableShapeGuardEmitterTests
{
    [Fact]
    public void Emit_IsCallableShape_UsesTypeIsAndFactory_NotNativeInstanceof()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Predicate<T> = callable(T $value): bool;
            class User {}

            function f(callable $fn, mixed $x): void
            {
                $a = $fn is Predicate<int>;
                $b = $fn instanceof Predicate<int>;
                $c = $x is User;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($fn, Predicate(\\Tyhp\\Type::int()))");
        php.Should().Contain("function Predicate(");
        php.Should().Contain("\\Tyhp\\Type::callableShape('Predicate')");
        php.Should().Contain("$x instanceof User");
        php.Should().NotContain("$fn instanceof Predicate");
        php.Should().NotContain("class Predicate");
        php.Should().NotContain("\\Tyhp\\Type::generic('Predicate'");
    }

    [Fact]
    public void Emit_TyhpdefCallableShapeIs_InlinesDescriptorWithoutFactory()
    {
        var php = CompileAndEmitResult(
            """
            <?tyhp
            function f(callable $fn): bool
            {
                return $fn is Predicate<int>;
            }
            """,
            tyhpdef: """
            <?tyhpdef
            type Predicate<T> = callable(T $value): bool;
            """).Php;

        php.Should().Contain("\\Tyhp\\Type::is($fn, \\Tyhp\\Type::callableShape('Predicate')");
        php.Should().NotContain("function Predicate");
        php.Should().NotContain("Predicate(");
        php.Should().NotContain("$fn instanceof Predicate");
        php.Should().NotContain("class Predicate");
        php.Should().NotContain("\\Tyhp\\Type::generic('Predicate'");
    }

    [Fact]
    public void Emit_IsStructAlias_UsesTypeIs_NotNativeInstanceof()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Point = struct {
                float $x;
                float $y;
            };
            function f(mixed $x): bool
            {
                return $x is Point;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($x");
        php.Should().Contain("\\Tyhp\\Type::struct('Point'");
        php.Should().NotContain("$x instanceof Point");
        php.Should().NotContain("class Point");
        php.Should().NotContain("function Point(");
    }

    [Fact]
    public void Emit_IsClassMemberStructAlias_UsesTypeStruct_NotNativeArrayTest()
    {
        // Class-member `type Point = struct { }` binds as a nested named struct (same as a
        // top-level struct alias). NativeTypeTest must not treat it as `array` (which would
        // collide with any `#[\Tyhp\NativeTypeTest] is_array(...)`-style registration).
        var php = CompileAndEmitResult(
            """
            <?tyhp
            class C {
                type Point = struct {
                    float $x;
                    float $y;
                };
            }
            function f(mixed $x): bool {
                return $x is C\Point;
            }
            """,
            tyhpdef: """
            <?tyhpdef
            #[\Tyhp\NativeTypeTest]
            function is_array_marker(mixed $value): $value is array;
            """).Php;

        php.Should().Contain("\\Tyhp\\Type::is($x");
        php.Should().Contain("\\Tyhp\\Type::struct('C\\\\Point'");
        php.Should().NotContain("is_array_marker($x)");
        php.Should().NotContain("$x instanceof");
        php.Should().NotContain("C::Point()");
    }

    [Fact]
    public void Emit_IsSelfMemberStructAlias_InsideMethod_UsesTypeStruct()
    {
        // `self\Point` inside a method resolves through the same
        // `NameResolver.TryResolveObjectTypeAlias` path as `C\Point`; assert the reified
        // `is` check names the owning class, not a bare `Point`/`self::Point()` call.
        var php = CompileAndEmit(
            """
            <?tyhp
            class C {
                type Point = struct {
                    float $x;
                    float $y;
                };
                public static function check(mixed $x): bool {
                    return $x is self\Point;
                }
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($x");
        php.Should().Contain("\\Tyhp\\Type::struct('C\\\\Point'");
        php.Should().NotContain("$x instanceof");
        php.Should().NotContain("self::Point()");
    }

    [Fact]
    public void Emit_ShapeParameterErasesToCallable_WithoutSyntheticClass()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Predicate<T> = callable(T $value): bool;
            function allMatch(Predicate<int> $p, int $n): bool {
                return $p($n);
            }
            """);

        php.Should().Contain("function allMatch(callable $p");
        php.Should().Contain("$p($n)");
        php.Should().NotContain("class Predicate");
        php.Should().NotContain("Predicate $p");
    }

    private static string CompileAndEmit(string tyhp) => CompileAndEmitResult(tyhp).Php;

    private static (string Php, IReadOnlyList<PHPOutputFile> Files)
        CompileAndEmitResult(string tyhp, string? tyhpdef = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "shape.tyhp");
        File.WriteAllText(filePath, tyhp);
        var includePaths = new List<string>();
        if (tyhpdef is not null)
        {
            var tyhpdefPath = Path.Combine(tempDir, "stubs.tyhpdef");
            File.WriteAllText(tyhpdefPath, tyhpdef);
            includePaths.Add(tyhpdefPath);
        }

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(
                    tempDir,
                    phpVersion: "8.4",
                    tyhpdefIncludePaths: includePaths.Count == 0 ? null : includePaths));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => e.Message))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                requiresRuntimeGenericTracking: result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets,
                nativeTypeTests: result.NativeTypeTests);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            foreach (var file in outputFiles)
            {
                AssertPhpLintClean(file.GeneratedContent ?? string.Empty);
            }

            return (
                string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty)),
                outputFiles);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void AssertPhpLintClean(string php)
    {
        if (string.IsNullOrWhiteSpace(php))
        {
            return;
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"tyhp_shape_emit_{Guid.NewGuid():N}.php");
        try
        {
            File.WriteAllText(tempFile, php);
            var result = PhpToolchain.RunPhpLint(tempFile);
            if (result.ExitCode == -1
                && result.StandardError.Contains("was not found on PATH", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            result.ExitCode.Should().Be(0, $"generated PHP must be syntactically valid:\n{result.CombinedOutput}\n---\n{php}");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
