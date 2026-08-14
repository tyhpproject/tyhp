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
/// Story 14.5 Phase 5 item 3 — call-shaped <c>clone(...)</c> native emit (≥ 8.5) vs
/// WithKeywordHelper / ObjectHelper rewrite; unary <c>clone $x</c> pass-through.
/// </summary>
[Trait("Category", "Emitter")]
public class CloneCallEmitterTests
{
    private static string CompileAndEmit(string tyhp, string phpVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "clone_call.tyhp");
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
    public void Emit_CloneWith_Php85_UsesNativeCloneCall(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            class C {}
            function demo(C $o): void {
                $a = clone($o, ['x' => 1]);
            }
            """, phpVersion);

        php.Should().Contain("clone($o, ['x' => 1])");
        php.Should().NotContain("ObjectHelper");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_CloneWith_LowerTarget_UsesObjectHelper(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            class C {
                public int $x = 0;
            }
            function demo(C $o): void {
                $a = clone($o, ['x' => 1]);
            }
            """, phpVersion);

        php.Should().Contain(@"\Tyhp\ObjectHelper::with(clone $o, ['x' => 1])");
        php.Should().NotContain("clone($o,");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.5")]
    public void Emit_UnaryClone_PassThroughUnchanged(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            class C {}
            function demo(C $o): C {
                return clone $o;
            }
            """, phpVersion);

        php.Should().Contain("clone $o");
        php.Should().NotContain("clone($o");
        php.Should().NotContain("ObjectHelper");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.5")]
    public void Emit_ParenthesizedUnaryClone_RemainsUnaryNotCallRewrite(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            class C {}
            function demo(C $o): C {
                return clone($o);
            }
            """, phpVersion);

        // Ambiguity rule: clone($o) is unary + parenthesized expr, not call-shaped ArgumentList.
        php.Should().MatchRegex(@"clone\s*\(\s*\$o\s*\)|clone \$o");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(", [");
    }

    [Theory]
    [InlineData("8.5")]
    public void Emit_CloneCallSingleArgTrailingComma_Php85_Native(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            class C {}
            function demo(C $o): void {
                $a = clone($o,);
            }
            """, phpVersion);

        php.Should().Contain("clone($o)");
        php.Should().NotContain("ObjectHelper");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_CloneCallSingleArg_LowerTarget_BecomesUnary(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            class C {}
            function demo(C $o): void {
                $a = clone($o,);
            }
            """, phpVersion);

        php.Should().Contain("clone $o");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain("clone($o");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_CloneCallNamedArgs_LowerTarget_UsesObjectHelper(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            class C {
                public int $x = 0;
            }
            function demo(C $o): void {
                $a = clone(object: $o, withProperties: ['x' => 2]);
            }
            """, phpVersion);

        php.Should().Contain(@"\Tyhp\ObjectHelper::with(clone $o, ['x' => 2])");
        php.Should().NotContain("object:");
        php.Should().NotContain("withProperties:");
    }

    [Theory]
    [InlineData("8.5")]
    public void Emit_CloneFirstClassCallable_Php85_Native(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                $fn = clone(...);
            }
            """, phpVersion);

        php.Should().Contain("clone(...)");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain("static fn");
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_CloneFirstClassCallable_LowerTarget_UsesObjectHelperArrow(string phpVersion)
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                $fn = clone(...);
            }
            """, phpVersion);

        php.Should().Contain(
            "(static fn(object $object, array $withProperties = []) => "
            + @"\Tyhp\ObjectHelper::with(clone $object, $withProperties))");
        php.Should().NotContain("clone(...)");
    }
}
