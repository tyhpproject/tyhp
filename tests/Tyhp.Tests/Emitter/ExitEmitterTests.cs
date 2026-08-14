using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class ExitEmitterTests
{
    private static string CompileAndEmit(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "emit.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Emit_ExitWithValue_UsesParentheses()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function main(): int { return 0; }
            exit(main());
            """);

        php.Should().Contain("exit(main());");
        php.Should().NotContain("exit main()");
    }

    [Fact]
    public void Emit_ExitWithoutValue_OmitsParentheses()
    {
        var php = CompileAndEmit("""
            <?tyhp
            exit;
            """);

        php.Should().Contain("exit;");
        php.Should().NotContain("exit()");
    }

    [Fact]
    public void Emit_DieWithValue_UsesParentheses()
    {
        var php = CompileAndEmit("""
            <?tyhp
            die(1);
            """);

        php.Should().Contain("die(1);");
        php.Should().NotContain("die 1");
    }
}
