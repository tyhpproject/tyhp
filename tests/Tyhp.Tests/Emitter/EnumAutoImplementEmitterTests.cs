using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 21.11 B.1: native PHP enums must not emit a written
/// <c>implements \UnitEnum</c> / <c>\BackedEnum</c>.
/// </summary>
[Trait("Category", "Emitter")]
public class EnumAutoImplementEmitterTests
{
    [Fact]
    public void Emit_UserUnitEnum_HasNoImplementsUnitEnum()
    {
        var php = CompileAndEmit("""
            <?tyhp
            enum Suit { case Hearts; }
            """);

        php.Should().Contain("enum Suit");
        php.Should().Contain("case Hearts;");
        php.Should().NotContain("implements");
        php.Should().NotContain("UnitEnum");
        php.Should().NotContain("BackedEnum");
    }

    [Fact]
    public void Emit_UserBackedEnum_HasNoImplementsBackedEnum()
    {
        var php = CompileAndEmit("""
            <?tyhp
            enum Size: int { case S = 1; }
            """);

        php.Should().Contain("enum Size");
        php.Should().Contain("case S = 1;");
        php.Should().NotContain("implements");
        php.Should().NotContain("UnitEnum");
        php.Should().NotContain("BackedEnum");
    }

    private static string CompileAndEmit(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "enum-auto-implement.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
