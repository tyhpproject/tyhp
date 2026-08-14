using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class StaticValueTypeEmitterTests
{
    [Fact]
    public void Emit_AllLiteralStringUnion_WidensToString()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function getStatus(): 'active'|'inactive' {
                return 'active';
            }
            function paint('red'|'green' $color): void {}
            """);

        php.Should().Contain("function getStatus(): string");
        php.Should().Contain("function paint(string $color): void");
        php.Should().NotContain("): 'active'|'inactive'");
        php.Should().NotContain("('red'|'green' $color)");
    }

    [Fact]
    public void Emit_AllLiteralIntUnion_WidensToInt()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function getCode(): 0|1|2 {
                return 0;
            }
            """);

        php.Should().Contain("function getCode(): int");
    }

    [Fact]
    public void Emit_MixedLiteralAndScalar_EmitsCleanUnion()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function a(false|'red'|'green' $c): void {}
            function b(int|'red' $c): void {}
            """);

        php.Should().Contain("function a(false | string $c): void");
        php.Should().Contain("function b(int | string $c): void");
    }

    [Theory]
    [InlineData("function f(): 0xFF { return 0xFF; }", "function f(): int")]
    [InlineData("function f(): 0b1010 { return 0b1010; }", "function f(): int")]
    [InlineData("function f(): 0o17 { return 0o17; }", "function f(): int")]
    [InlineData("function f(): 1_000 { return 1_000; }", "function f(): int")]
    [InlineData("function f(): 1.2e3 { return 1.2e3; }", "function f(): float")]
    [InlineData("function f(): '' { return ''; }", "function f(): string")]
    public void Emit_NumericAndEdgeCaseLiteralSpellings_WidenToScalar(string tyhp, string expectedSignature)
    {
        var php = CompileAndEmit($"<?tyhp\n{tyhp}\n");

        php.Should().Contain(expectedSignature);
    }

    [Fact]
    public void Emit_SingleLiteralParameterAndReturn_WidenToScalar()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function d('a' $c): 'a' {
                return $c;
            }
            """);

        php.Should().Contain("function d(string $c): string");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "literals.tyhp");
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
