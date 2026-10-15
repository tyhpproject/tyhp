using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// PHP has no <c>resource</c> type declaration, so <c>resource</c> erases to <c>mixed</c> in
/// native hints.
/// </summary>
[Trait("Category", "Emitter")]
public class ResourceTypeEmitterTests
{
    [Fact]
    public void Emit_ResourceReturnType_ErasesToMixed()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Files {
                public static function open(resource $handle): resource {
                    return $handle;
                }

                public static function maybe(?resource $handle): ?resource {
                    return $handle;
                }

                public static function either(resource|false $handle): resource|false {
                    return $handle;
                }
            }
            """);

        php.Should().Contain("public static function open(mixed $handle): mixed");
        php.Should().Contain("public static function maybe(mixed $handle): mixed");
        php.Should().Contain("public static function either(mixed $handle): mixed");
        php.Should().NotContain("): resource");
        php.Should().NotContain("): ?resource");
        php.Should().NotContain("mixed|false");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "resource.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], IsolatedCompilation.CreateOptions(tempDir));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, CreateProject());
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
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
