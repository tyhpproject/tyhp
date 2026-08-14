using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class AnonymousClassTests
{
    [Fact]
    public void Emit_AnonymousClassExtendsAndImplements_EmitsInlineClass()
    {
        var php = CompileAndEmit("""
            <?tyhp
            interface Foo {}
            class Base {}
            function make(): Base {
                return new class extends Base implements Foo { public int $x = 0; };
            }
            """);

        php.Should().NotContain("anonClass@");
        php.Should().NotContain("@");
        php.Should().Contain("new class() extends Base implements Foo");
        php.Should().Contain("public int $x");
    }

    // FOUND_BUGS #33: anonymous class inside a *method* body must also stay clean and emit.
    [Fact]
    public void Emit_AnonymousClassInsideMethod_EmitsInlineClass()
    {
        var php = CompileAndEmit("""
            <?tyhp
            interface Foo {}
            class Base {}
            class Maker {
                public function make(): Base {
                    return new class extends Base implements Foo {
                        public int $x = 0;
                    };
                }
            }
            """);

        php.Should().NotContain("anonClass@");
        php.Should().Contain("new class() extends Base implements Foo");
        php.Should().Contain("public int $x");
    }

    [Fact]
    public void Emit_AnonymousClassWithCtorArgs_EmitsArgsAfterClassKeyword()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Base {
                public function __construct(int $a, int $b) {}
            }
            function make(): Base {
                return new class(1, 2) extends Base {};
            }
            """);

        php.Should().NotContain("anonClass@");
        php.Should().Contain("new class(1, 2) extends Base");
    }

    private static string CompileAndEmit(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "anon.tyhp");
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
