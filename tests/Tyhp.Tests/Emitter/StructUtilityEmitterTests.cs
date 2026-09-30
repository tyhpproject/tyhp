using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 08.5 Phase 5 / global <c>__</c> utility types must erase to a PHP surface in signatures
/// (not leak as <c>\__StructKey</c> / <c>\__CallableReturnType</c> class hints).
/// </summary>
[Trait("Category", "Emitter")]
public class StructUtilityEmitterTests
{
    [Theory]
    [InlineData("__StructKey<Point>", "string")]
    [InlineData("__Properties<Point>", "array")]
    [InlineData("__StructDef<__StructRecord<Point, '$x', int>>", "array")]
    [InlineData("__CallableReturnType<callable(string $s): int>", "int")]
    [InlineData("__CallableParametersTuple<callable(string $s): int>", "array")]
    [InlineData("__CallableParametersStruct<callable(string $s): int>", "array")]
    [InlineData("__CallableParametersRest<callable(string $s): int>", "mixed")]
    [InlineData("__Nullable<int>", "?int")]
    public void Emit_StructAndTyhpUtilityParameter_ErasesToPhpSurface(string tyhpType, string phpHint)
    {
        var php = CompileAndEmit($$"""
            <?tyhp
            type Point = struct { int $x = 0; string $y = ''; };
            function take({{tyhpType}} $value): void {}
            """);

        php.Should().Contain($"function take({phpHint} $value): void");
        php.Should().NotContain("__StructKey");
        php.Should().NotContain("__Properties");
        php.Should().NotContain("__StructDef");
        php.Should().NotContain("__StructRecord");
        php.Should().NotContain("\\Tyhp\\ReturnType");
        php.Should().NotContain("\\Tyhp\\Parameters");
        php.Should().NotContain("__CallableReturnType");
        php.Should().NotContain("__CallableParametersStruct");
        php.Should().NotContain("__CallableParametersTuple");
        php.Should().NotContain("__CallableParametersRest");
        php.Should().NotContain("__Nullable");
    }

    [Fact]
    public void Emit_GenericCallableReturnType_ErasesUnboundToMixed()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function apply<TCallable extends callable>(TCallable $cb): __CallableReturnType<TCallable> {
                return $cb();
            }
            """);

        php.Should().NotContain("__CallableReturnType");
        php.Should().Contain("function apply(");
        php.Should().Contain("): mixed");
    }

    [Fact]
    public void Emit_GenericCallableParametersTuple_ErasesUnboundToArray()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersTuple<TCallable> $args
            ): void {}
            """);

        php.Should().NotContain("__CallableParametersTuple");
        php.Should().Contain("function apply(");
        php.Should().Contain("array $args");
    }

    [Fact]
    public void Emit_GenericCallableParametersRest_ErasesUnboundToMixedVariadic()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): void {}
            """);

        php.Should().NotContain("__CallableParametersRest");
        php.Should().Contain("function invoke(");
        php.Should().Contain("mixed ...$args");
    }

    private static string CompileAndEmit(string tyhp)
        => string.Join('\n', CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "struct_utilities.tyhp");
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
