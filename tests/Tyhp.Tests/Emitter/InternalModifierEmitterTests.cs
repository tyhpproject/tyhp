using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class InternalModifierEmitterTests
{
    [Fact]
    public void Emit_InternalClassAndMethod_StripsInternal_MembersArePublic()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            internal class Foo {
                internal function bar(): void {}
            }
            """);

        php.Should().Contain("class Foo");
        php.Should().NotContain("public class");
        php.Should().NotContain("internal");
        php.Should().Contain("public function bar()");
    }

    [Fact]
    public void Emit_InternalTopLevel_StaysUnprefixed()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            internal function helper(): void {}
            internal const TAG = 1;
            """);

        php.Should().Contain("function helper()");
        php.Should().NotContain("public function helper");
        php.Should().Contain("const TAG");
        php.Should().NotContain("public const TAG");
        php.Should().NotContain("internal");
    }

    [Fact]
    public void Emit_InternalPropertyAndClassConst_ArePublic()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            class Box {
                internal readonly int $x = 0;
                internal const C = 1;
            }
            """);

        php.Should().Contain("public readonly int $x");
        php.Should().Contain("public const C");
        php.Should().NotContain("internal");
    }

    [Fact]
    public void Emit_PublicClassModifier_IsStripped()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            public class Visible {}
            """);

        php.Should().Contain("class Visible");
        php.Should().NotContain("public class Visible");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "internal.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = "8.4",
                })
                .Build();
            var project = new Project(configuration);

            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets);
            var files = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
