using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Magic utilities must erase to a PHP surface in signatures (not leak as
/// <c>\__SuperType</c> / <c>\__IndexKeys</c> class hints).
/// </summary>
[Trait("Category", "Emitter")]
public class MagicUtilityEmitterTests
{
    [Theory]
    [InlineData("__SuperType<Point>", "object")]
    [InlineData("__SuperTypeName<Point>", "string")]
    [InlineData("__CurrentScope", "?object")]
    [InlineData("__CallableThis<callable(): void>", "?object")]
    [InlineData("__CallableScope<callable(): void>", "object|string|null")]
    [InlineData("__IndexKeys<Point>", "string|int")]
    [InlineData("__IndexValueType<Point, 'x'>", "mixed")]
    [InlineData("__IndexValueTypes<Point>", "mixed")]
    public void Emit_MagicUtilityParameter_ErasesToPhpSurface(string tyhpType, string phpHint)
    {
        var php = CompileAndEmit($$"""
            <?tyhp
            class Point {}
            function take({{tyhpType}} $value): void {}
            """);

        php.Should().Contain($"function take({phpHint} $value): void");
        php.Should().NotContain("__SuperType");
        php.Should().NotContain("__SuperTypeName");
        php.Should().NotContain("__CurrentScope");
        php.Should().NotContain("__CallableThis");
        php.Should().NotContain("__CallableScope");
        php.Should().NotContain("__IndexKeys");
        php.Should().NotContain("__IndexValueType");
        php.Should().NotContain("__IndexValueTypes");
    }

    private static string CompileAndEmit(string tyhp)
        => string.Join('\n', CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "magic_utilities.tyhp");
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
