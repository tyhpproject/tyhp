using System;
using System.Linq;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Reserved words used as bare keys in a <c>with [...]</c> initializer name struct fields like any
/// other bare key.
/// </summary>
[Trait("Category", "Emitter")]
public class WithInitializerKeyEmitterTests
{
    [Fact]
    public void ReservedWordBareKeys_NameStructFields()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type RemoteError = struct {
                string $class = '';
                int $line = 0;
                string $function = '';
                string $list = '';
                string $default = '';
            };
            function make(): array {
                return new RemoteError() with [
                    class => 'X',
                    line => 1,
                    function => 'f',
                    list => 'l',
                    default => 'd',
                ];
            }
            """);

        php.Should().Contain("'class' => 'X'");
        php.Should().Contain("'line' => 1");
        php.Should().Contain("'function' => 'f'");
        php.Should().Contain("'list' => 'l'");
        php.Should().Contain("'default' => 'd'");
    }

    [Fact]
    public void ReservedWordBareKey_InPlaceWith_NamesStructField()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Row = struct {
                string $class = '';
            };
            function rename(Row $r): array {
                return $r with [class => 'Y'];
            }
            """);

        php.Should().Contain("'class' => 'Y'");
    }

    [Fact]
    public void ReservedWordInNestedValueArray_IsNotReclassified()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Row = struct {
                string $name = '';
                array $meta = [];
            };
            function make(): array {
                return new Row() with [name => 'n', meta => ['class' => 1]];
            }
            """);

        php.Should().Contain("'name' => 'n'");
        php.Should().Contain("'class' => 1");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "with-keys.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Select(e => $"{e.Code}: {e.Message}").Should().BeEmpty();

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                null,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
