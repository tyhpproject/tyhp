using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Generic-bound erasure for intersection constraints (e.g. ObjectHelper::with
/// <c>T extends object&amp;TProperties</c> → PHP <c>object</c>, not <c>mixed</c>).
/// </summary>
[Trait("Category", "Emitter")]
public class TypeSpellingIntersectionEmitterTests
{
    [Fact]
    public void Emit_ObjectAndStructTypeParamBound_ErasesToObjectNotMixed()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class ObjectHelper {
                public static function with<TProperties extends struct, T extends object&TProperties>(
                    T $object,
                    TProperties $properties,
                ): T {
                    return $object;
                }
            }
            """);

        php.Should().Contain("function with(object $object, array $properties): object");
        php.Should().NotContain("function with(mixed $object");
        php.Should().NotContain("): mixed");
    }

    [Fact]
    public void Emit_ClassAndStructTypeParamBound_ErasesToClass()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            class Widget {}

            final class Helper {
                public static function patch<TProps extends struct, T extends Widget&TProps>(
                    T $object,
                    TProps $props,
                ): T {
                    return $object;
                }
            }
            """);

        php.Should().Contain("function patch(\\Probe\\Widget $object, array $props): \\Probe\\Widget");
        php.Should().NotContain("mixed $object");
    }

    [Fact]
    public void Emit_TwoClassIntersectionBound_KeepsPhpIntersection()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            interface A {}
            interface B {}

            final class Helper {
                public static function take<T extends A&B>(T $value): T {
                    return $value;
                }
            }
            """);

        php.Should().Contain("function take(\\Probe\\A & \\Probe\\B $value): \\Probe\\A & \\Probe\\B");
        php.Should().NotContain("mixed $value");
    }

    private static string CompileAndEmit(string tyhp)
        => string.Join('\n', CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "spell.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, CreateProject());
            return new TyhpEmitter(context).Emit(result.ParsedFiles!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.2",
            })
            .Build();
        return new Project(configuration);
    }
}
