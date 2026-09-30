using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 21.6 Phase 6c: methods that implement native
/// <c>\ArrayAccess::{offsetExists,offsetGet,offsetSet,offsetUnset}</c> emit PHP <c>mixed</c>
/// parameters (and <c>offsetGet</c> return) for PHP contravariance.
/// </summary>
[Trait("Category", "Emitter")]
public class ArrayAccessMixedEmitTests
{
    [Fact]
    public void Emit_ArrayAccessImplementor_SpellsOffsetMethodsAsMixed()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {}
            class Bag implements \ArrayAccess<string, User> {
                public function offsetExists(mixed $offset): bool {
                    return false;
                }
                public function offsetGet(string $offset): User {
                    return new User();
                }
                public function offsetSet(mixed $offset, mixed $value): void {
                }
                public function offsetUnset(mixed $offset): void {
                }
            }
            """);

        php.Should().Contain("function offsetExists(mixed $offset): bool");
        php.Should().Contain("function offsetGet(mixed $offset): mixed");
        php.Should().Contain("function offsetSet(mixed $offset, mixed $value): void");
        php.Should().Contain("function offsetUnset(mixed $offset): void");
        php.Should().NotContain("function offsetGet(string $offset): User");
    }

    [Fact]
    public void Emit_UnrelatedOffsetGet_KeepsAuthoredTypes()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function offsetGet(string $offset): int {
                    return 0;
                }
            }
            """);

        php.Should().Contain("function offsetGet(string $offset): int");
        php.Should().NotContain("function offsetGet(mixed $offset): mixed");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "array-access-emit.tyhp");
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
