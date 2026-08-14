using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class TypeofEmitterTests
{
    [Fact]
    public void Emit_TypeofUnboundName_OutsideAClass_ErasesToMixed()
    {
        // Parse-only emit (no binder): class names are unbound barewords, same path as type params.
        // A generic binding is keyed by the class that declared the parameter, and the key must be a
        // literal class name — there is no enclosing class here to name, and both `static::class` and
        // `self::class` are a PHP fatal outside a class scope. So the erased answer is all that can be
        // emitted. The checker rejects this shape (TYHP4148); this only pins that the fallback stays
        // valid PHP.
        var php = EmitOnly("""
            <?tyhp
            class Sample {}
            function f(): void { $t = typeof(Sample); }
            """);

        php.Should().Contain("\\Tyhp\\Type::mixed()");
        php.Should().NotContain("tyhpGenericObjectGetGenericType");
        php.Should().NotContain("static::class");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Emit_TypeofUnboundSimpleName_OutsideAClass_ErasesToMixed()
    {
        var php = EmitOnly(@"<?tyhp function f() { $t = typeof(TValue); }");

        php.Should().Contain("\\Tyhp\\Type::mixed()");
        php.Should().NotContain("tyhpGenericObjectGetGenericType");
        php.Should().NotContain("static::class");
    }

    [Fact]
    public void Emit_BoundClassName_ReturnsShortName()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App\Models;
            class User {}
            function f(): void { $t = typeof(User); }
            """);

        php.Should().Contain("Tyhp\\Type::fromClassName('User'::class)");
        php.Should().NotContain("Tyhp\\Type::fromClassName('\\App\\Models\\User'::class");
        php.Should().NotContain("typeof(");
    }

    private static string EmitOnly(string content)
    {
        var parseResult = ParserTestHelper.ParseTyhpContent(content);
        parseResult.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", parseResult.Diagnostics)}");
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmitContext.Create(new GlobalScope(), new DiagnosticBag());
        var outputFiles = new TyhpEmitter(context).Emit([srcFile]);
        return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
    }

    private static string CompileAndEmit(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "typeof.tyhp");
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

            // The async/package.tyhpdef (and other tyhpdef packages) carry pre-existing unresolved-type
            // diagnostics (ERROR_TYHP3019 \WeakMap, 3020 \Throwable, 8010, 8002, ...) that are
            // infrastructure noise unrelated to typeof emission. Exclude all .tyhpdef-sourced errors
            // so this test can verify what it actually tests — the emitted PHP for typeof(...).
            // Errors from the user's .tyhp file under test remain visible.
            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => e.Message))}");

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
