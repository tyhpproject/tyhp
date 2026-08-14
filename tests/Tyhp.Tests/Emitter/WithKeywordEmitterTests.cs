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

[Trait("Category", "Emitter")]
public class WithKeywordEmitterTests
{
    private static string CompileAndEmit(string tyhp, string phpVersion = "8.4", Project? project = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "with.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            project ??= CreateProject(phpVersion);
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
                    ExperimentalReadonlyCloneWith = project.Build.ExperimentalReadonlyCloneWith,
                },
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .Where(d => d.Code != MessageCode.CheckerIncompatibleReturnType)
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

    private static Project CreateProject(string phpVersion, bool experimentalReadonlyCloneWith = false)
    {
        var values = new Dictionary<string, string?>
        {
            ["output:phpVersion"] = phpVersion,
            ["build:experimentalReadonlyCloneWith"] = experimentalReadonlyCloneWith ? "true" : "false",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new Project(configuration);
    }

    [Fact]
    public void Emit_CloneWith_NonReadonly_Php84_Expr_UsesObjectHelper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Config {
                public bool $enabled = true;
            }
            function copyConfig(Config $cfg): Config {
                return clone $cfg with [enabled => false];
            }
            """);

        php.Should().Contain(@"\Tyhp\ObjectHelper::with(clone $cfg, ['enabled' => false])");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_CloneWith_Php85_UsesNativeCloneCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Config {
                public bool $enabled = true;
            }
            function copyConfig(Config $cfg): Config {
                return clone $cfg with [enabled => false];
            }
            """, phpVersion: "8.5");

        php.Should().Contain("clone($cfg, ['enabled' => false])");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NewWith_NonReadonly_Stmt_UsesDirectAssignments()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Widget {
                public string $color = "red";
            }
            function make(): void {
                $x = new Widget() with [color => "blue"];
            }
            """);

        php.Should().Contain("$x = new Widget()");
        php.Should().Contain("$x->color = \"blue\"");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NewWith_NonReadonly_Expr_UsesObjectHelper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Widget {
                public string $color = "red";
            }
            function make(): Widget {
                return new Widget() with [color => "blue"];
            }
            """);

        php.Should().Contain(@"\Tyhp\ObjectHelper::with(new Widget(), ['color' => ""blue""])");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_InPlaceWith_Stmt_UsesPropertyAssignments()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Widget {
                public string $color = "red";
            }
            function paint(Widget $w): void {
                $w with [color => "green"];
            }
            """);

        php.Should().Contain("$w->color = \"green\"");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NestedWith_InnerExprOuterStmt()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Inner {
                public bool $b = true;
            }
            class MyClass {
                public Inner $a;
            }
            function demo(Inner $c): void {
                $b = new MyClass() with [
                    a => clone $c with [b => false]
                ];
            }
            """);

        php.Should().Contain("$b = new MyClass()");
        php.Should().Contain(@"\Tyhp\ObjectHelper::with(clone $c, ['b' => false])");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_StructWith_StillUsesArrayReplace()
    {
        var php = CompileAndEmit("""
            <?tyhp
            struct Point {
                int $x = 0;
                int $y = 0;
            }
            function move(Point $p): array {
                return $p with [x => 1];
            }
            """);

        php.Should().Contain(@"\array_replace($p, ['x' => 1])");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_CloneWith_AssignmentStmt_Php84_UsesDirectAssignments()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Config {
                public bool $enabled = true;
            }
            function copyConfig(Config $cfg): void {
                $a = clone $cfg with [enabled => false];
            }
            """);

        php.Should().Contain("$a = clone $cfg");
        php.Should().Contain("$a->enabled = false");
        php.Should().NotContain("ObjectHelper");
    }

    [Fact]
    public void Emit_CloneWith_Readonly_Php84_Experimental_UsesReflectionIife()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }
            function copyColor(Color $c): Color {
                return clone $c with [alpha => 128];
            }
            """,
            phpVersion: "8.4",
            project: CreateProject("8.4", experimentalReadonlyCloneWith: true));

        php.Should().Contain("newInstanceWithoutConstructor");
        php.Should().Contain("ReflectionObject");
        php.Should().Contain("__tyhp_overrides");
        php.Should().Contain("['alpha' => 128]");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NewWith_Readonly_Php84_UsesAnonymousCloneWrapper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }
            function make(): Color {
                return new Color() with [alpha => 128];
            }
            """);

        php.Should().Contain("clone (new class(");
        php.Should().Contain("extends \\Color");
        php.Should().Contain("function __clone(): void");
        php.Should().Contain("$this->alpha = 128");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain(" with ");
    }

    [Fact]
    public void Emit_NewWith_Readonly_Php85_UsesNativeCloneCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Color {
                public readonly int $alpha = 255;
            }
            function make(): Color {
                return new Color() with [alpha => 128];
            }
            """, phpVersion: "8.5");

        php.Should().Contain("clone(new Color(), ['alpha' => 128])");
        php.Should().NotContain("ObjectHelper");
        php.Should().NotContain("new class");
        php.Should().NotContain(" with ");
    }
}
