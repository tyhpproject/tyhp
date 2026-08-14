using System;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Tyhpdef <c>function/class/... as Alias</c> names must erase to the original PHP name at emit.
/// </summary>
[Trait("Category", "Emitter")]
public class TyhpdefAliasEmitterTests
{
    [Fact]
    public void Emit_RootedFunctionAlias_ErasesToOriginalPhpName()
    {
        // ExtStandard.tyhpdef: function call_user_func_array as call_user_func_array_unsafe(...)
        var php = CompileAndEmit("""
            <?tyhp
            function demo(callable $cb, array $args): mixed {
                return \call_user_func_array_unsafe($cb, $args);
            }
            """);

        php.Should().Contain(@"\call_user_func_array(");
        php.Should().NotContain("call_user_func_array_unsafe");
    }

    [Fact]
    public void Emit_UnqualifiedFunctionAlias_ErasesToOriginalPhpName()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(callable $cb, array $args): mixed {
                return call_user_func_array_unsafe($cb, $args);
            }
            """);

        php.Should().Contain("call_user_func_array(");
        php.Should().NotContain("call_user_func_array_unsafe");
    }

    [Fact]
    public void Emit_CallUserFuncUnsafeAlias_ErasesToOriginal()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(callable $cb): mixed {
                return \call_user_func_unsafe($cb, 1, 2);
            }
            """);

        php.Should().Contain(@"\call_user_func(");
        php.Should().NotContain("call_user_func_unsafe");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "alias_erase.tyhp");
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
