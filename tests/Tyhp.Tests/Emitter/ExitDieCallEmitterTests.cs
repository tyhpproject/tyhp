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
/// Story 14.5 Phase 5 item 4 — <c>exit</c>/<c>die</c> keyword emit (≥ 8.4 native call forms)
/// vs &lt; 8.4 positional / bare / FCC arrow lowering.
/// </summary>
[Trait("Category", "Emitter")]
public class ExitDieCallEmitterTests
{
    private static string CompileAndEmit(string tyhp, string phpVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "exit_die_call.tyhp");
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
    [InlineData("8.2")]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Emit_BareExit_OmitsParentheses(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                exit;
            }
            """, phpVersion);

        php.Should().Contain("exit;");
        php.Should().NotContain("exit()");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Emit_PositionalExit_UsesParentheses(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                exit(0);
            }
            """, phpVersion);

        php.Should().Contain("exit(0);");
        php.Should().NotContain("exit 0");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Emit_PositionalDie_UsesParentheses(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                die(1);
            }
            """, phpVersion);

        php.Should().Contain("die(1);");
        php.Should().NotContain("die 1");
    }

    [Theory]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Emit_EmptyExitCall_Php84Plus_KeepsEmptyParens(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                exit();
            }
            """, phpVersion);

        php.Should().Contain("exit();");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    public void Emit_EmptyExitCall_LowerTarget_PrefersBareKeyword(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                exit();
            }
            """, phpVersion);

        php.Should().Contain("exit;");
        php.Should().NotContain("exit()");
    }

    [Theory]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Emit_NamedExit_Php84Plus_NativeNamedArgs(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                exit(status: 0);
            }
            """, phpVersion);

        php.Should().Contain("exit(status: 0);");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    public void Emit_NamedExit_LowerTarget_BecomesPositional(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                exit(status: 0);
            }
            """, phpVersion);

        php.Should().Contain("exit(0);");
        php.Should().NotContain("status:");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    public void Emit_NamedDie_LowerTarget_BecomesPositional(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                die(status: 1);
            }
            """, phpVersion);

        php.Should().Contain("die(1);");
        php.Should().NotContain("status:");
    }

    [Theory]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Emit_ExitFirstClassCallable_Php84Plus_Native(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                $fn = exit(...);
            }
            """, phpVersion);

        php.Should().Contain("exit(...)");
        php.Should().NotContain("static fn");
        php.Should().NotContain("fromCallable");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    public void Emit_ExitFirstClassCallable_LowerTarget_UsesStaticArrow(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                $fn = exit(...);
            }
            """, phpVersion);

        php.Should().Contain("(static fn(string | int $status = 0) => exit($status))");
        php.Should().NotContain("exit(...)");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    public void Emit_DieFirstClassCallable_LowerTarget_UsesStaticArrow(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                $fn = die(...);
            }
            """, phpVersion);

        php.Should().Contain("(static fn(string | int $status = 0) => die($status))");
        php.Should().NotContain("die(...)");
    }
}
