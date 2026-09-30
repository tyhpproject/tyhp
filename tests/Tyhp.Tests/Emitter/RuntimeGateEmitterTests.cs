using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Versioning;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// <c>output.phpVersion</c> is the oldest PHP the build runs on: php gates that hold for every
/// version at or above it emit no check, the rest emit <c>\PHP_VERSION_ID</c> checks, and
/// <c>declare(ext=…)</c> emits <c>\extension_loaded</c>.
/// </summary>
[Trait("Category", "Emitter")]
public class RuntimeGateEmitterTests
{
    private sealed record Compiled(string Php, IReadOnlyList<(string Path, string Content)> Files, IReadOnlyList<string> Errors);

    private static Compiled Compile(string tyhp, string phpVersion = "8.2")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "gates.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(
                    tempDir,
                    phpVersion: phpVersion,
                    configure: o => o.Checker = new CheckerOptions { PhpVersion = phpVersion }));

            var errors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .Select(d => $"{d.Code}: {d.Message}")
                .ToList();
            if (errors.Count > 0 || result.ParsedFiles is null)
            {
                return new Compiled("", [], errors);
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["output:phpVersion"] = phpVersion })
                .Build();
            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, new Project(configuration));
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles).ToList();
            var files = outputFiles
                .Select(f => (f.OutputFilePath ?? "", f.GeneratedContent ?? ""))
                .ToList();
            return new Compiled(string.Join('\n', files.Select(f => f.Item2)), files, errors);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void UpperBoundedPhpGate_EmitsVersionIdCheck()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            declare(php="<8.6") {
                function legacy(): string { return 'legacy'; }
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("if (\\PHP_VERSION_ID < 80600) {");
        compiled.Php.Should().Contain("function legacy(): string");
    }

    [Fact]
    public void PhpGateTrueForEveryVersionAtOrAboveMinimum_EmitsNoCheck()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            declare(php=">=8.1") {
                function always(): string { return 'always'; }
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("function always(): string");
        compiled.Php.Should().NotContain("PHP_VERSION_ID");
    }

    [Fact]
    public void PhpGateEntirelyBelowMinimum_EmitsNothing()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            declare(php="<8.0") {
                function ancient(): string { return 'ancient'; }
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().NotContain("ancient");
    }

    [Fact]
    public void TyhpPhpAttribute_OnFunction_EmitsVersionIdCheck()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            #[\Tyhp\Php("<8.4")]
            function older(): string { return 'older'; }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("if (\\PHP_VERSION_ID < 80400) {");
        compiled.Php.Should().Contain("function older(): string");
        compiled.Php.Should().NotContain("#[\\Tyhp\\Php");
    }

    [Fact]
    public void GatedClass_EmitsVersionIdCheckInsideItsOwnFile()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            declare(php="<8.6") {
                final class Legacy {}
            }
            """);

        compiled.Errors.Should().BeEmpty();
        var file = compiled.Files.Single(f => f.Path.EndsWith("Legacy.php", StringComparison.Ordinal));
        file.Content.Should().Contain("if (\\PHP_VERSION_ID < 80600) {");
        file.Content.Should().Contain("final class Legacy");
    }

    [Fact]
    public void SameNameDeclaredUnderAnotherVersionGate_KeepsTheCompiledDeclarationUnwrapped()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            declare(php="<8.4") {
                function pick(): string { return 'old'; }
            }

            declare(php=">=8.4") {
                function pick(): string { return 'new'; }
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("return 'old';");
        compiled.Php.Should().NotContain("PHP_VERSION_ID");
    }

    [Fact]
    public void NegativeExtGate_EmitsNegatedExtensionLoadedCheck_WhenNoLoadedPackageRequiresIt()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            declare(ext="!intl") {
                function noIntl(): string { return 'plain'; }
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("if (!\\extension_loaded('intl')) {");
        compiled.Php.Should().Contain("function noIntl(): string");
        compiled.Php.Should().NotContain("declare(ext");
    }

    [Fact]
    public void ExtGateBlockInsideFunctionBody_EmitsExtensionLoadedCheck()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            function pick(): string {
                string $value = 'plain';
                declare(ext="!intl") {
                    $value = 'no-intl';
                }
                return $value;
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("if (!\\extension_loaded('intl')) {");
        compiled.Php.Should().Contain("$value = 'no-intl';");
        compiled.Php.Should().NotContain("declare(ext");
    }

    [Fact]
    public void NegativeExtGate_CompilesEvenWhenNothingRulesOutTheExtension_AndComplementFlowsAsIfElse()
    {
        // `!intl` is the runtime fallback, so it compiles with or without an intl tyhpdef. The
        // positive `intl` arm needs the package to type-check and is dropped here, so the
        // variable is only assigned when the extension is missing: a definite-assignment error.
        var compiled = Compile("""
            <?tyhp
            namespace App;

            function pick(): string {
                ?string $value = null;
                declare(ext="intl") {
                    $value = 'intl';
                }
                declare(ext="!intl") {
                    $value = 'plain';
                }
                return $value;
            }
            """);

        compiled.Errors.Should().NotBeEmpty();
        compiled.Errors.Should().Contain(e => e.StartsWith("CheckerIncompatibleReturnType", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedNegativeExtGates_EmitNestedRuntimeChecks()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            function pick(): string {
                string $value = 'none';
                declare(ext="!intl") {
                    declare(ext="!iconv") {
                        $value = 'plain';
                    }
                }
                return $value;
            }
            """);

        compiled.Errors.Should().BeEmpty();
        var outer = compiled.Php.IndexOf("if (!\\extension_loaded('intl')) {", StringComparison.Ordinal);
        var inner = compiled.Php.IndexOf("if (!\\extension_loaded('iconv')) {", StringComparison.Ordinal);
        outer.Should().BeGreaterThan(-1);
        inner.Should().BeGreaterThan(outer);
        compiled.Php.Should().Contain("$value = 'plain';");
    }

    [Fact]
    public void PhpGateBlockInsideFunctionBody_EmitsVersionIdCheck()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            function pick(): string {
                string $value = 'new';
                declare(php="<8.6") {
                    $value = 'old';
                }
                return $value;
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("if (\\PHP_VERSION_ID < 80600) {");
        compiled.Php.Should().Contain("$value = 'old';");
    }

    [Fact]
    public void PositiveExtGate_IsNotCompiled_WhenNoLoadedPackageRequiresTheExtension()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            declare(ext="intl") {
                function withIntl(): string { return 'intl'; }
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().NotContain("withIntl");
    }

    [Fact]
    public void PolyfillPattern_NestsPhpGateExtGateAndExistenceCheck()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            declare(php="<8.6") {
                declare(ext="!intl") {
                    fallback function graphemeStrrev(string $value): string {
                        return $value;
                    }
                }
            }
            """);

        compiled.Errors.Should().BeEmpty();
        var php = compiled.Php;
        var versionCheck = php.IndexOf("if (\\PHP_VERSION_ID < 80600) {", StringComparison.Ordinal);
        var extCheck = php.IndexOf("if (!\\extension_loaded('intl')) {", StringComparison.Ordinal);
        var existsCheck = php.IndexOf("if (!\\function_exists(__NAMESPACE__ . '\\graphemeStrrev')) {", StringComparison.Ordinal);
        versionCheck.Should().BeGreaterThanOrEqualTo(0);
        extCheck.Should().BeGreaterThan(versionCheck);
        existsCheck.Should().BeGreaterThan(extCheck);
    }

    [Fact]
    public void GatedConst_EmitsDefineInsideTheCheck()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            #[\Tyhp\Php("<8.6")]
            const OLD_LIMIT = 10;
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("if (\\PHP_VERSION_ID < 80600) {");
        compiled.Php.Should().Contain("\\define(__NAMESPACE__ . '\\OLD_LIMIT', 10);");
    }

    [Theory]
    [InlineData("class Box { #[\\Tyhp\\Php(\">=8.3\")] public string $label = 'x'; }")]
    [InlineData("class Box { #[\\Tyhp\\Php(\">=8.3\")] const int LIMIT = 1; }")]
    [InlineData("enum Suit { #[\\Tyhp\\Php(\">=8.3\")] case Hearts; }")]
    [InlineData("interface Shape { #[\\Tyhp\\Php(\">=8.3\")] public function area(): float; }")]
    public void TyhpPhpAttribute_OnPropertyConstantCaseOrInterfaceMethod_IsRejected(string declaration)
    {
        var compiled = Compile($"<?tyhp\nnamespace App;\n\n{declaration}\n");

        compiled.Errors.Should().Contain(e => e.StartsWith("CheckerPhpVersionAttributeInvalidMember", StringComparison.Ordinal));
    }

    [Fact]
    public void TyhpPhpAttribute_OnClassMethod_StaysLegal()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            class Box {
                #[\Tyhp\Php(">=8.2")]
                public function label(): string { return 'x'; }
            }
            """);

        compiled.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("8.2", ">=8.1", PhpRuntimeGateKind.Always, null)]
    [InlineData("8.2", "<8.0", PhpRuntimeGateKind.Never, null)]
    [InlineData("8.2", "<8.6", PhpRuntimeGateKind.Conditional, "\\PHP_VERSION_ID < 80600")]
    [InlineData("8.2", ">=8.4", PhpRuntimeGateKind.Conditional, "\\PHP_VERSION_ID >= 80400")]
    [InlineData("8.2", ">=8.3 <8.5", PhpRuntimeGateKind.Conditional, "\\PHP_VERSION_ID >= 80300 && \\PHP_VERSION_ID < 80500")]
    [InlineData("8.2", "<8.3 || >=8.5", PhpRuntimeGateKind.Conditional, "\\PHP_VERSION_ID < 80300 || \\PHP_VERSION_ID >= 80500")]
    [InlineData("8.2", "8.3", PhpRuntimeGateKind.Conditional, "\\PHP_VERSION_ID >= 80300 && \\PHP_VERSION_ID < 80400")]
    [InlineData("8.4", ">=8.4", PhpRuntimeGateKind.Always, null)]
    [InlineData("8.4", "<8.4", PhpRuntimeGateKind.Never, null)]
    public void ClassifyAtOrAbove_DescribesTheHalfLineFromTheMinimum(
        string minimum,
        string constraint,
        PhpRuntimeGateKind expectedKind,
        string? expectedExpression)
    {
        var gate = PhpVersionConstraint.ClassifyAtOrAbove([constraint], minimum);

        gate.Kind.Should().Be(expectedKind);
        gate.Expression.Should().Be(expectedExpression);
    }

    [Fact]
    public void ClassifyAtOrAbove_CombinesAndedConstraints()
    {
        var gate = PhpVersionConstraint.ClassifyAtOrAbove(["<8.6", ">=8.3"], "8.2");

        gate.Kind.Should().Be(PhpRuntimeGateKind.Conditional);
        gate.Expression.Should().Be("\\PHP_VERSION_ID >= 80300 && \\PHP_VERSION_ID < 80600");
    }
}
