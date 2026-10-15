using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// <c>typeof(Class)</c> and <c>$x is ?Class</c> reify a declared class by its fully-qualified name.
/// </summary>
[Trait("Category", "Emitter")]
public class TypeofDeclaredClassEmitterTests
{
    [Fact]
    public void Emit_TypeofDeclaredClassInNamespace_UsesFullyQualifiedName()
    {
        var (errors, php) = Compile("""
            <?tyhp
            namespace App;

            final class Widget {}

            function probe(): void {
                $t = typeof(Widget);
                \var_dump((string) $t);
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("\\Tyhp\\Type::fromClassName(\\App\\Widget::class)");
        php.Should().NotContain("'Widget'::class");
        php.Should().NotContain("fromClassName(Widget::class)");
    }

    [Fact]
    public void Emit_NullableInstanceofDeclaredClassInNamespace_UsesFullyQualifiedName()
    {
        var (errors, php) = Compile("""
            <?tyhp
            namespace App;

            final class Widget {}

            function probe(mixed $x): bool {
                return $x is ?Widget;
            }
            """);

        errors.Should().BeEmpty();
        php.Should().Contain("\\Tyhp\\Type::nullable(\\Tyhp\\Type::fromClassName(\\App\\Widget::class))");
        php.Should().NotContain("'Widget'::class");
    }

    private static (List<string> Errors, string Php) Compile(string probe)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var probePath = Path.Combine(tempDir, "probe.tyhp");
        File.WriteAllText(probePath, probe);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [probePath],
                IsolatedCompilation.CreateOptions(tempDir));

            var errors = result.Diagnostics.Errors
                .Select(d => $"{(int)d.Code}: {d.Message}")
                .ToList();
            if (errors.Count > 0)
            {
                return (errors, string.Empty);
            }

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, CreateProject());
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return (errors, string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty)));
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
