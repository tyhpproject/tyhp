using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 21.6 Phase 6d: an <c>ArrayAccessShape</c> implementor emits
/// <c>offsetGet(mixed $offset): mixed</c> without repeating <c>#[\Tyhp\PhpType]</c>.
/// </summary>
[Trait("Category", "Emitter")]
[Trait("Category", "Story21.6")]
public class ArrayAccessShapeEmitTests
{
    [Fact]
    public void Emit_Implementor_SpellsOffsetGetAsMixedWithoutRepeatingPhpType()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type ConfigMap = struct {
                string $host = '';
                int $port = 0;
            };
            class Config implements \Tyhp\Contracts\ArrayAccessShape<ConfigMap> {
                public function offsetExists(mixed $offset): bool {
                    return false;
                }
                public function offsetGet(mixed $offset): mixed {
                    if ($offset === 'host') {
                        return '';
                    }
                    if ($offset === 'port') {
                        return 0;
                    }
                    throw new \RuntimeException();
                }
                public function offsetSet(mixed $offset, mixed $value): void {
                    if ($offset === 'host') {
                        $unused = $value;
                        return;
                    }
                    if ($offset === 'port') {
                        $unused = $value;
                        return;
                    }
                    throw new \RuntimeException();
                }
                public function offsetUnset(mixed $offset): void {
                }
            }
            """);

        php.Should().Contain("function offsetGet(mixed $offset): mixed");
        php.Should().NotContain("Tyhp\\PhpType");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\PhpType");
        php.Should().NotContain("function offsetGet(string $offset)");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "array-access-shape-emit.tyhp");
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
                project);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
