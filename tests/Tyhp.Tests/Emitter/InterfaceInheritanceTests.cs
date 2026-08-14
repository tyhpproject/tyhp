using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class InterfaceInheritanceTests
{
    [Fact]
    public void Emit_InterfaceExtendsMultipleInterfaces_EmitsExtendsCommaSeparated()
    {
        var php = CompileAndEmit("""
            <?tyhp
            interface B {}
            interface C {}
            interface A extends B, C {}
            """);

        php.Should().Contain("interface A extends B, C");
        php.Should().NotContain("interface A implements");
    }

    [Fact]
    public void Emit_InterfaceExtendsSingleInterface_EmitsExtends()
    {
        var php = CompileAndEmit("""
            <?tyhp
            interface B {}
            interface A extends B {}
            """);

        php.Should().Contain("interface A extends B");
    }

    [Fact]
    public void Emit_ClassExtendsAndImplements_StillEmitsBothKeywords()
    {
        var php = CompileAndEmit("""
            <?tyhp
            interface I1 {}
            interface I2 {}
            class Base {}
            class Derived extends Base implements I1, I2 {}
            """);

        php.Should().Contain("class Derived extends Base implements I1, I2");
    }

    private static string CompileAndEmit(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "iface.tyhp");
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
