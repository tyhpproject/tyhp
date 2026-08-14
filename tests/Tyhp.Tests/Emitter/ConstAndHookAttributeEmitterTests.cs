using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 14.5 Phase 5 item 5 — top-level <c>const</c> attributes (native ≥ 8.5) and property-hook
/// attributes (native ≥ 8.4); strip + TYHP5017 when the target PHP version cannot represent them.
/// </summary>
[Trait("Category", "Emitter")]
public class ConstAndHookAttributeEmitterTests
{
    private sealed record EmitResult(string Php, DiagnosticBag Diagnostics);

    private static EmitResult CompileAndEmit(string tyhp, string phpVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "const_hook_attrs.tyhp");
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
            var php = string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
            return new EmitResult(php, result.Diagnostics);
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

    [Fact]
    public void Emit_TopLevelConstAttribute_Php85_EmitsAttribute()
    {
        var result = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {}

            #[Marker]
            const TOP = 1;
            """, "8.5");

        result.Php.Should().MatchRegex(
            new Regex(@"#\[\\Probe\\Marker\]\s+const TOP = 1;", RegexOptions.Singleline));
        result.Diagnostics.Warnings
            .Where(d => d.Code == MessageCode.EmitterAttributeStrippedForPhpVersion)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("8.2")]
    [InlineData("8.4")]
    public void Emit_TopLevelConstAttribute_Below85_StripsWith5017(string phpVersion)
    {
        var result = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {}

            #[Marker]
            const TOP = 1;
            """, phpVersion);

        result.Php.Should().Contain("const TOP = 1;");
        result.Php.Should().NotMatchRegex(@"(?m)^\s*#\[.*\]\s*\n\s*const TOP");

        var stripped = result.Diagnostics.Warnings
            .Where(d => d.Code == MessageCode.EmitterAttributeStrippedForPhpVersion)
            .ToList();
        stripped.Should().ContainSingle();
        stripped[0].Message.Should().Contain("constant");
        stripped[0].Message.Should().Contain(phpVersion);
    }

    [Theory]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Emit_PropertyHookAttributes_Native_EmitsInline(string phpVersion)
    {
        var result = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class HookMarker {}

            final class Widget {
                public string $name {
                    #[HookMarker]
                    get {
                        return $this->name;
                    }
                    #[HookMarker]
                    set {
                        $this->name = $value;
                    }
                }
            }
            """, phpVersion);

        result.Php.Should().Contain("#[\\Probe\\HookMarker] get");
        result.Php.Should().Contain("#[\\Probe\\HookMarker] set");
        result.Diagnostics.Warnings
            .Where(d => d.Code == MessageCode.EmitterAttributeStrippedForPhpVersion)
            .Should().BeEmpty();
    }

    [Fact]
    public void Emit_PropertyHookAttributes_Php82_StripsWith5017()
    {
        var result = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class HookMarker {}

            final class Widget {
                public string $name {
                    #[HookMarker]
                    get {
                        return $this->name;
                    }
                    #[HookMarker]
                    set {
                        $this->name = $value;
                    }
                }
            }
            """, "8.2");

        // Polyfill path: no native hook block / hook attributes on the property line.
        result.Php.Should().NotContain("#[\\Probe\\HookMarker] get");
        result.Php.Should().NotContain("#[\\Probe\\HookMarker] set");
        result.Php.Should().NotContain("#[HookMarker] get");
        result.Php.Should().NotContain("#[HookMarker] set");

        var stripped = result.Diagnostics.Warnings
            .Where(d => d.Code == MessageCode.EmitterAttributeStrippedForPhpVersion)
            .ToList();
        stripped.Should().HaveCount(2);
        stripped.Should().OnlyContain(d => d.Message.Contains("property hook"));
        stripped.Should().OnlyContain(d => d.Message.Contains("8.2"));
    }

    [Fact]
    public void Emit_ClassConstantAttribute_UnchangedOnPhp84()
    {
        // Class-constant attributes remain legal on all supported targets (regression vs strip logic).
        var result = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {}

            final class Widget {
                #[Marker]
                public const string TAG = 'w';
            }
            """, "8.4");

        result.Php.Should().MatchRegex(
            new Regex(@"#\[\\Probe\\Marker\]\s+public const string TAG = 'w';", RegexOptions.Singleline));
        result.Diagnostics.Warnings
            .Where(d => d.Code == MessageCode.EmitterAttributeStrippedForPhpVersion)
            .Should().BeEmpty();
    }
}
