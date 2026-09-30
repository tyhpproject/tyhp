using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
[Trait("Category", "Story27")]
public class ObjectShapeGuardEmitterTests
{
    [Fact]
    public void Emit_IsObjectShape_UsesTypeIsAndFactory_NotNativeInstanceof()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            class User {}

            function f(object $obj, mixed $x): void
            {
                $a = $obj is ClockShape;
                $b = $obj instanceof ClockShape;
                $c = $x is User;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($obj, ClockShape())");
        php.Should().Contain("function ClockShape()");
        php.Should().Contain("\\Tyhp\\Type::objectShape('ClockShape', ['now'], [])");
        php.Should().Contain("$x instanceof User");
        php.Should().NotContain("$obj instanceof ClockShape");
        php.Should().NotContain("class ClockShape");
        php.Should().NotContain("\\Tyhp\\Type::object()");
    }

    [Fact]
    public void Emit_TyhpdefObjectShapeIs_InlinesDescriptorWithoutFactory()
    {
        var php = CompileAndEmitResult(
            """
            <?tyhp
            function f(object $obj): bool
            {
                return $obj is ClockShape;
            }
            """,
            tyhpdef: """
            <?tyhpdef
            type ClockShape = object {
                public function now(): int;
            };
            """).Php;

        php.Should().Contain("\\Tyhp\\Type::is($obj, \\Tyhp\\Type::objectShape('ClockShape', ['now'], [])");
        php.Should().NotContain("function ClockShape");
        php.Should().NotContain("ClockShape()");
        php.Should().NotContain("$obj instanceof ClockShape");
        php.Should().NotContain("class ClockShape");
    }

    [Fact]
    public void Emit_ShapeParameterErasesToObject_WithoutSyntheticClass()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            function formatNow(ClockShape $c): int {
                return $c->now();
            }
            """);

        php.Should().Contain("function formatNow(object $c)");
        php.Should().Contain("$c->now()");
        php.Should().NotContain("class ClockShape");
        php.Should().NotContain("ClockShape $c");
    }

    [Fact]
    public void Emit_NewTFactory_HasNoNewTypeSpellingOrSyntheticClass()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): string;
            };
            class Greeter {
                public function __construct(string $name): void {
                    $this->name = $name;
                }
                private string $name;
                public function ping(): string {
                    return $this->name;
                }
            }
            function make<T extends __New<Named>>(string $name): T {
                return new T($name);
            }
            function demo(): string {
                return make<Greeter>("hi")->ping();
            }
            """);

        php.Should().Contain("new ($__generic_T->getName())");
        php.Should().Contain("function make(string $name)");
        php.Should().NotContain("new<");
        php.Should().NotContain("class Named");
        php.Should().NotContain("__New");
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
                genericCallTargets: result.GenericCallTargets);
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
