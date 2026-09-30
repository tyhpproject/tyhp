using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 20.5 Phase 6: <c>declare(php=…)</c> and <c>#[\Tyhp\Php]</c> are compile-time only
/// and must not appear in emitted PHP (same Tyhp-only filter as <c>output_file</c>).
/// </summary>
[Trait("Category", "Emitter")]
[Trait("Category", "Story20.5")]
public class PhpVersionGateEmitterTests
{
    [Fact]
    public void FileLevel_Unsatisfied_EmitsNoSymbolsOrPhpDeclare()
    {
        var files = CompileToFiles("""
            <?tyhp
            declare(php=">=8.4");
            function gated_file_fn(): void {}
            class GatedFileClass {}
            """, phpVersion: "8.2");

        var php = JoinPhp(files);
        php.Should().NotContain("declare(php=");
        php.Should().NotContain("gated_file_fn");
        php.Should().NotContain("GatedFileClass");
        files.Should().NotContain(f =>
            (f.GeneratedContent ?? "").Contains("function gated_file_fn", StringComparison.Ordinal)
            || (f.GeneratedContent ?? "").Contains("class GatedFileClass", StringComparison.Ordinal));
    }

    [Fact]
    public void Block_Unsatisfied_InnerCodeAbsent_SiblingsPresent()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function always_present(): void {}
            declare(php=">=8.4") {
                function only_on_84(): void {}
            }
            function also_present(): void {}
            """, phpVersion: "8.2");

        php.Should().Contain("function always_present()");
        php.Should().Contain("function also_present()");
        php.Should().NotContain("only_on_84");
        php.Should().NotContain("declare(php=");
    }

    [Fact]
    public void Block_Satisfied_InnerCodePresent_NoPhpDeclareWrapper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function always_present(): void {}
            declare(php=">=8.4") {
                function only_on_84(): void {}
            }
            """, phpVersion: "8.4");

        php.Should().Contain("function always_present()");
        php.Should().Contain("function only_on_84()");
        php.Should().NotContain("declare(php=");
        php.Should().NotMatchRegex(@"declare\s*\(\s*php");
    }

    [Fact]
    public void TyhpPhpAttribute_Stripped_WhenGateIsSatisfied()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            function only_on_84(): void {}
            """, phpVersion: "8.4");

        php.Should().Contain("function only_on_84()");
        php.Should().NotContain("Tyhp\\Php");
        php.Should().NotContain("\\Tyhp\\Php");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\Php");
    }

    [Fact]
    public void TyhpPhpAttribute_NamedArgument_Stripped()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\Php(version: ">=8.4")]
            function named_gate(): void {}
            """, phpVersion: "8.4");

        php.Should().Contain("function named_gate()");
        php.Should().NotContain("Tyhp\\Php");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\Php");
    }

    [Fact]
    public void TyhpPhpAttribute_Unsatisfied_OmitsDeclaration()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            function only_on_84(): void {}
            function always_present(): void {}
            """, phpVersion: "8.2", allowedErrorCodes: [MessageCode.CheckerNotAnAttributeClass]);

        php.Should().Contain("function always_present()");
        php.Should().NotContain("only_on_84");
        php.Should().NotContain("Tyhp\\Php");
    }

    [Fact]
    public void OtherAttributes_Preserved_WhenTyhpPhpIsStripped()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {}

            #[\Tyhp\Php(">=8.2")]
            #[Marker]
            function tagged(): void {}
            """, phpVersion: "8.4");

        php.Should().Contain("function tagged()");
        php.Should().Contain("#[\\Probe\\Marker]");
        php.Should().NotContain("Tyhp\\Php");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\Php");
    }

    [Fact]
    public void ClassMethod_UnsatisfiedAttribute_Omitted_SatisfiedMethodEmitsWithoutGate()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            class Widget {
                #[\Tyhp\Php(">=8.5")]
                public function only_85(): void {}
                #[\Tyhp\Php(">=8.4")]
                public function on_84(): void {}
                public function always(): void {}
            }
            """, phpVersion: "8.4");

        php.Should().Contain("class Widget");
        php.Should().Contain("function always()");
        php.Should().Contain("function on_84()");
        php.Should().NotContain("only_85");
        php.Should().NotContain("Tyhp\\Php");
    }

    [Fact]
    public void StrictTypes_StillEmits_WithSequentialPhpDeclare()
    {
        var php = CompileAndEmit("""
            <?tyhp
            declare(strict_types=1);
            declare(php=">=8.4");
            function kept(): void {}
            """, phpVersion: "8.4");

        php.Should().Contain("declare(strict_types=1);");
        php.Should().Contain("function kept()");
        php.Should().NotContain("declare(php=");
        php.IndexOf("declare(strict_types=1);", StringComparison.Ordinal)
            .Should().BeLessThan(php.IndexOf("function kept()", StringComparison.Ordinal));
    }

    [Fact]
    public void MixedSequentialDeclares_NonPhpDirectivesStillEmit()
    {
        var php = CompileAndEmit("""
            <?tyhp
            declare(ticks=1);
            declare(php=">=8.4");
            declare(encoding="UTF-8");
            function kept(): void {}
            """, phpVersion: "8.4");

        php.Should().Contain("declare(ticks=1);");
        php.Should().Contain("declare(encoding=");
        php.Should().Contain("function kept()");
        php.Should().NotContain("declare(php=");
    }

    [Fact]
    public void FunctionBody_SatisfiedDeclareBlock_UnwrapsWithoutWrapper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function run(): void {
                echo 'keep';
                declare(php=">=8.4") {
                    echo 'gated';
                }
                echo 'after';
            }
            """, phpVersion: "8.4");

        php.Should().Contain("echo 'keep';");
        php.Should().Contain("echo 'gated';");
        php.Should().Contain("echo 'after';");
        php.Should().NotContain("declare(php=");
    }

    [Fact]
    public void FunctionBody_UnsatisfiedDeclareBlock_OmitsInnerStatements()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function run(): void {
                echo 'keep';
                declare(php=">=8.5") {
                    echo 'gated';
                }
                echo 'after';
            }
            """, phpVersion: "8.4");

        php.Should().Contain("echo 'keep';");
        php.Should().NotContain("echo 'gated';");
        php.Should().Contain("echo 'after';");
        php.Should().NotContain("declare(php=");
    }

    [Fact]
    public void FileLevel_Satisfied_EmitsNormally_NoPhpDeclare()
    {
        var php = CompileAndEmit("""
            <?tyhp
            declare(php=">=8.2");
            function gated_file_fn(): void {}
            class GatedFileClass {}
            """, phpVersion: "8.4");

        php.Should().NotContain("declare(php=");
        php.Should().Contain("function gated_file_fn()");
        php.Should().Contain("class GatedFileClass");
    }

    [Fact]
    public void FileLevel_Extension_Unsatisfied_OmitsExtensionClass()
    {
        var php = CompileAndEmit("""
            <?tyhp
            declare(php=">=8.4");
            extension GatedStringOps extends string {
                function gated_tag(): string {
                    return \strtoupper($this);
                }
            }
            """, phpVersion: "8.2");

        php.Should().NotContain("GatedStringOps");
        php.Should().NotContain("declare(php=");
    }

    [Fact]
    public void FileLevel_Extension_Satisfied_EmitsExtensionClass()
    {
        var php = CompileAndEmit("""
            <?tyhp
            declare(php=">=8.4");
            extension GatedStringOps extends string {
                function gated_tag(): string {
                    return \strtoupper($this);
                }
            }
            """, phpVersion: "8.4");

        php.Should().Contain("class GatedStringOps");
        php.Should().Contain("gated_tag(");
        php.Should().NotContain("declare(php=");
    }

    [Fact]
    public void ClassLevelAttribute_Unsatisfied_OmitsClass()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            class OnlyOn84 {}
            class AlwaysPresent {}
            """, phpVersion: "8.2", allowedErrorCodes: [MessageCode.CheckerNotAnAttributeClass]);

        php.Should().Contain("class AlwaysPresent");
        php.Should().NotContain("OnlyOn84");
        php.Should().NotContain("Tyhp\\Php");
    }

    [Fact]
    public void MixedDeclareNotAlone_StillEmitsNonPhpDirective()
    {
        // `php` mixed with another directive is a checker error (4301); the binder still
        // records `strict_types` as a normal file directive (BindFileLevelDeclare only skips
        // the `php` key when not alone). Emitter must not crash and must keep strict_types.
        var php = CompileAndEmit("""
            <?tyhp
            declare(php=">=8.3", strict_types=1);
            function kept(): void {}
            """, phpVersion: "8.4", allowedErrorCodes: [MessageCode.CheckerPhpVersionDeclareNotAlone]);

        php.Should().Contain("declare(strict_types=1)");
        php.Should().NotContain("declare(php=");
        php.Should().Contain("function kept()");
    }

    [Fact]
    public void NestedDeclareBlocks_AndSemantics_InnerUnsatisfiedOuterSatisfied()
    {
        var php = CompileAndEmit("""
            <?tyhp
            declare(php=">=8.3") {
                function outer_only(): void {}
                declare(php=">=8.4") {
                    function both_needed(): void {}
                }
            }
            function always_present(): void {}
            """, phpVersion: "8.3");

        php.Should().Contain("function outer_only()");
        php.Should().Contain("function always_present()");
        php.Should().NotContain("both_needed");
        php.Should().NotContain("declare(php=");
    }

    [Fact]
    public void NamespacedFunction_InsideSatisfiedDeclareBlock_KeepsNamespace()
    {
        var files = CompileToFiles("""
            <?tyhp

            namespace Probe;

            function always_present(): void {}

            declare(php=">=8.4") {
                function gated_ns_fn(): void {}
            }
            """, phpVersion: "8.4");

        var php = JoinPhp(files);
        php.Should().Contain("namespace Probe;");
        php.Should().Contain("function gated_ns_fn()");
        php.Should().NotContain("declare(php=");

        // Both functions must land in the SAME namespace-functions output unit, not split
        // into a separate/global bucket by the declare wrapper.
        files.Count(f => (f.GeneratedContent ?? "").Contains("function gated_ns_fn"))
            .Should().Be(1);
        var owner = files.Single(f => (f.GeneratedContent ?? "").Contains("function gated_ns_fn"));
        owner.GeneratedContent.Should().Contain("function always_present");
    }

    [Fact]
    public void NamespacedClass_InsideSatisfiedDeclareBlock_UsesCorrectFqn()
    {
        var files = CompileToFiles("""
            <?tyhp

            namespace Probe;

            declare(php=">=8.4") {
                class GatedWidget {}
            }
            """, phpVersion: "8.4");

        var owner = files.SingleOrDefault(f => (f.GeneratedContent ?? "").Contains("class GatedWidget"));
        owner.Should().NotBeNull();
        owner!.GeneratedContent.Should().Contain("namespace Probe;");
        owner.GeneratedContent.Should().NotContain("declare(php=");
        owner.OutputFilePath.Should().Contain("GatedWidget");
    }

    private static string CompileAndEmit(
        string tyhp,
        string phpVersion = "8.4",
        MessageCode[]? allowedErrorCodes = null) =>
        JoinPhp(CompileToFiles(tyhp, phpVersion, allowedErrorCodes));

    private static string JoinPhp(IReadOnlyList<PHPOutputFile> files) =>
        string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(
        string tyhp,
        string phpVersion = "8.4",
        MessageCode[]? allowedErrorCodes = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "php_version_gate.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = phpVersion,
                })
                .Build();
            var project = new Project(configuration);

            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: phpVersion));

            var allowed = allowedErrorCodes ?? [];
            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => !allowed.Contains(d.Code))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets);
            return new TyhpEmitter(context).Emit(result.ParsedFiles!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
