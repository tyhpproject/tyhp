using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Emit regressions for FOUND_BUGS #38: generic typed locals must erase to plain assignments,
/// not corrupted comparison chains like <c>((Bag &lt; int) &gt; $bag) = ...</c>.
/// </summary>
[Trait("Category", "Emitter")]
public class GenericTypedLocalEmitterTests
{
    [Fact]
    public void Emit_GenericTypedLocal_ErasesToPlainAssignment()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Bag<T> {
                public function __construct(T $v): void {}
            }
            class GenericLocalTest {
                public function run(): void {
                    Bag<int> $bag = new Bag<int>(5);
                }
            }
            """);

        php.Should().Contain("$bag =");
        php.Should().NotContain("Bag < int");
        php.Should().NotContain(") > $bag");
        php.Should().NotContain("((Bag");
    }

    [Fact]
    public void Emit_DeferredVoidTypedLocal_ErasesCleanly()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Deferred<T extends void|mixed = void> {
                public function __construct(): void {}
            }
            function delay(): void {
                Deferred<void> $deferred = new Deferred<void>();
            }
            """);

        php.Should().Contain("$deferred =");
        php.Should().NotContain("Deferred < void");
        php.Should().NotContain(") > $deferred");
    }

    [Fact]
    public void Emit_ArrayGenericTypedLocal_ErasesCleanly()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function collect(): void {
                array<int> $results = [];
            }
            """);

        php.Should().Contain("$results =");
        php.Should().NotContain("array < int");
        php.Should().NotContain(") > $results");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "generic-typed-local.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject();
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, project);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
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
                ["output:phpVersion"] = "8.4",
            })
            .Build();
        return new Project(configuration);
    }
}
