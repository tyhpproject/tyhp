using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 21.6: <c>\Closure&lt;…&gt;</c> / <c>\Fiber&lt;…&gt;</c> and magic utilities erase
/// on emit. No runtime generic metadata on those PHP classes.
/// </summary>
[Trait("Category", "Emitter")]
[Trait("Category", "Story21.6")]
public class ClosureFiberEraseEmitterTests
{
    [Fact]
    public void Emit_ClosureAndFiberGenerics_EraseToPhpClasses()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function take(\Closure<callable(int): string> $c, \Fiber<int, callable(): void> $f): void {}
            """);

        php.Should().Contain("function take(\\Closure $c, \\Fiber $f): void");
        php.Should().NotContain("callable(int): string");
        php.Should().NotContain("callable(): void");
        php.Should().NotContain("GenericObject");
        php.Should().NotContain("__generic_");
        php.Should().NotContain("HasGenerics");
    }

    [Fact]
    public void Emit_MagicTypesInSignatures_DoNotLeakUtilityNames()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Point {}
            function take(
                __SuperType<Point> $super,
                __CurrentScope $scope,
                __CallableThis<callable(): void> $thisOf
            ): void {}
            """);

        php.Should().NotContain("__SuperType");
        php.Should().NotContain("__CurrentScope");
        php.Should().NotContain("__CallableThis");
        php.Should().Contain("function take(");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "closure_fiber_erase.tyhp");
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

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

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
