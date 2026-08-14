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
/// Story 14.5 Phase 5 item 2 — <c>(void)</c> native emit (≥ 8.5) vs omit-cast lowering.
/// </summary>
[Trait("Category", "Emitter")]
public class VoidCastEmitterTests
{
    private static string CompileAndEmit(string tyhp, string phpVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "void_cast.tyhp");
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
    public void Emit_VoidCast_Php85_UsesNativeCast(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(string $s): void {
                (void)\strlen($s);
            }
            """, phpVersion);

        php.Should().Contain("(void) \\strlen($s);");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_VoidCast_LowerTarget_OmitsCastAsDiscardedExpression(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(string $s): void {
                (void)\strlen($s);
            }
            """, phpVersion);

        php.Should().Contain("\\strlen($s);");
        php.Should().NotContain("(void)");
        php.Should().NotContain("(VOID)");
    }

    [Theory]
    [InlineData("8.5")]
    public void Emit_VoidCast_Php85_PreservesSourceCastSpelling(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(int $n): void {
                (VOID)$n;
            }
            """, phpVersion);

        php.Should().Contain("(VOID) $n;");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_VoidCast_LowerTarget_VariableOperand_IsBareExpressionStatement(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(int $n): void {
                (void)$n;
            }
            """, phpVersion);

        php.Should().Contain("$n;");
        php.Should().NotContain("(void)");
    }

    [Theory]
    [InlineData("8.5")]
    public void Emit_VoidCast_ForLists_Php85_UsesNativeCast(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                for ((void)$a; $i < 10; (void)$i++) {
                }
            }
            """, phpVersion);

        php.Should().Contain("for ((void) $a; $i < 10; (void) $i++)");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_VoidCast_ForLists_LowerTarget_OmitsCast(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                for ((void)$a; $i < 10; (void)$i++) {
                }
            }
            """, phpVersion);

        php.Should().Contain("for ($a; $i < 10; $i++)");
        php.Should().NotContain("(void)");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_VoidCast_NonFinalForCondition_LowerTarget_OmitsCast(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function side(): int { return 1; }
            function demo(bool $cond): void {
                for (; (void)side(), $cond; ) {
                }
            }
            """, phpVersion);

        php.Should().Contain("for (; side(), $cond; )");
        php.Should().NotContain("(void)");
    }

    [Fact]
    public void Emit_VoidCast_Php85_WhitespaceVariant_StillEmitsCast()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(int $n): void {
                ( void ) $n;
            }
            """, "8.5");

        // Source cast spelling preserved; space after cast per PSR-12.
        php.Should().Contain("( void ) $n;");
    }
}
