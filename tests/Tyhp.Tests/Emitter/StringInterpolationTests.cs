using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class StringInterpolationTests
{
    [Fact]
    public void Interpolate_SimpleVariable_BindsAndEmitsValidInterpolation()
    {
        var php = CompileAndEmit("""
            <?tyhp
            $name = "world";
            echo "hi $name";
            """);

        php.Should().Contain("\"hi $name\"");
    }

    [Fact]
    public void Interpolate_ObjectMember_BindsAndEmitsValidInterpolation()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Box {
                public int $p = 1;
            }
            $o = new Box();
            echo "val $o->p";
            """);

        php.Should().Contain("\"val $o->p\"");
    }

    [Fact]
    public void Interpolate_ArrayIndex_BindsAndEmitsValidInterpolation()
    {
        var php = CompileAndEmit("""
            <?tyhp
            $arr = [1, 2, 3];
            echo "first $arr[0]";
            """);

        php.Should().Contain("\"first $arr[0]\"");
    }

    private static string CompileAndEmit(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "interp.tyhp");
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

            result.Diagnostics.Errors.Should().BeEmpty();
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
}
