using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 21.7: <c>list()</c> / <c>[]</c> destructure keeps native PHP <c>list</c> / short-array
/// emit. A skipped slot (<c>[, $b]</c>) must round-trip to an empty element between commas —
/// still valid PHP list-assignment syntax — rather than being dropped (which would silently
/// rebind <c>$b</c> to key <c>0</c> instead of <c>1</c>).
/// </summary>
[Trait("Category", "Emitter")]
[Trait("Category", "Story21.7")]
public class ArrayAccessDestructureEmitterTests
{
    [Fact]
    public void Emit_SkippedSlot_PreservesEmptyPositionInAssignment()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<int, User> $o): User {
                [, $b] = $o;
                return $b;
            }
            """);

        php.Should().Contain("[, $b] = $o");
    }

    [Fact]
    public void Emit_NoSkippedSlots_DoesNotIntroduceExtraCommas()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<int, User> $o): User {
                [$a, $b] = $o;
                return $a;
            }
            """);

        php.Should().Contain("[$a, $b] = $o");
    }

    [Fact]
    public void Emit_SkippedSlotWithTrailingComma_PreservesInteriorSkipAndTrimsTrailingArtifact()
    {
        // Adversarial combo: an interior skip slot (`[, $b]`) alongside a genuine trailing-comma
        // artifact (`, ]`) on the same destructuring target. Only the trailing artifact — which
        // has no following element to shift — is a parser artifact; the interior skip must still
        // round-trip so `$b` binds to key `1`, not `0`.
        var php = CompileAndEmit("""
            <?tyhp
            class User {}
            function read(\ArrayAccess<int, User> $o): User {
                [, $b,] = $o;
                return $b;
            }
            """);

        php.Should().Contain("[, $b] = $o");
        php.Should().NotMatchRegex(@",\s*,");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "destructure.tyhp");
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
                IsolatedCompilation.CreateOptions(
                    tempDir,
                    phpVersion: "8.4",
                    configure: o =>
                    {
                        o.Checker = new CheckerOptions
                        {
                            PhpVersion = "8.4",
                        };
                    }));

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
}
