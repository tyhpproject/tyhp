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
/// Story 14.5 Phase 5 item 1 — pipe <c>|&gt;</c> native emit (≥ 8.5) vs nested-call lowering.
/// </summary>
[Trait("Category", "Emitter")]
public class PipeOperatorEmitterTests
{
    private static string CompileAndEmit(string tyhp, string phpVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "pipe.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject(phpVersion);
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = phpVersion,
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                Checker = new CheckerOptions
                {
                    PhpVersion = phpVersion,
                },
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
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

    private static Project CreateProject(string phpVersion)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = phpVersion,
            })
            .Build();
        return new Project(configuration);
    }

    [Theory]
    [InlineData("8.5")]
    public void Emit_Pipe_Php85_UsesNativeOperator(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(string $s): int {
                return $s |> \strlen(...);
            }
            """, phpVersion);

        php.Should().Contain("$s |> \\strlen(...)");
        php.Should().NotContain("strlen($s)");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_Pipe_LowerTarget_UnwrapsFccToNestedCall(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(string $s): int {
                return $s |> \strlen(...);
            }
            """, phpVersion);

        php.Should().Contain("\\strlen($s)");
        php.Should().NotContain("|>");
        php.Should().NotContain("strlen(...)");
    }

    [Theory]
    [InlineData("8.5")]
    public void Emit_PipeChain_Php85_KeepsNativeOperators(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(string $s): string {
                return $s
                    |> \htmlentities(...)
                    |> \strtoupper(...);
            }
            """, phpVersion);

        php.Should().Contain("|>");
        php.Should().Contain("\\htmlentities(...)");
        php.Should().Contain("\\strtoupper(...)");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_PipeChain_LowerTarget_NestsLeftToRight(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(string $s): string {
                return $s
                    |> \htmlentities(...)
                    |> \strtoupper(...);
            }
            """, phpVersion);

        php.Should().Contain("\\strtoupper(\\htmlentities($s))");
        php.Should().NotContain("|>");
    }

    [Theory]
    [InlineData("8.5")]
    public void Emit_Pipe_ArrowRhs_Php85_ParenthesizesArrow(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(string $s): int {
                return $s |> (fn(string $x): int => \strlen($x));
            }
            """, phpVersion);

        php.Should().Contain("$s |> (fn(string $x): int => \\strlen($x))");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_Pipe_ArrowRhs_LowerTarget_InvokesParenthesizedArrow(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(string $s): int {
                return $s |> (fn(string $x): int => \strlen($x));
            }
            """, phpVersion);

        php.Should().Contain("(fn(string $x): int => \\strlen($x))($s)");
        php.Should().NotContain("|>");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_Pipe_VariableCallable_LowerTarget_InvokesCallable(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(string $s): int {
                $f = (fn(string $x): int => \strlen($x));
                return $s |> $f;
            }
            """, phpVersion);

        php.Should().Contain("$f($s)");
        php.Should().NotContain("|>");
    }

    [Fact]
    public void Emit_Pipe_AdditionOnLeft_Php85_ParenthesizesWhenNeeded()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function identity(int $n): int {
                return $n;
            }
            function demo(): int {
                return 5 + 2 |> identity(...);
            }
            """, "8.5");

        // Nested binary left of pipe is parenthesized for clarity / precedence safety.
        php.Should().Contain("(5 + 2) |> identity(...)");
    }
}