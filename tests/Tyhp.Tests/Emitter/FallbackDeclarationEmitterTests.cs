using System;
using System.Linq;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// <c>fallback</c> on a <c>.tyhp</c> declaration emits the runtime existence check that a
/// hand-written <c>if (!\function_exists(...))</c> gate emits.
/// </summary>
[Trait("Category", "Emitter")]
public class FallbackDeclarationEmitterTests
{
    private sealed record Compiled(string Php, IReadOnlyList<(string Path, string Content)> Files, IReadOnlyList<string> Errors);

    private static Compiled Compile(string tyhp, string phpVersion = "8.4")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "fallback.tyhp");
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

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
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
    public void FallbackFunction_InNamespace_EmitsFunctionExistsCheck()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App\Payments;

            fallback function formatMoney(int $cents): string {
                return 'money';
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("if (!\\function_exists(__NAMESPACE__ . '\\formatMoney')) {");
        compiled.Php.Should().Contain("function formatMoney(int $cents): string");
        compiled.Php.Should().NotContain("fallback");
    }

    [Fact]
    public void FallbackFunction_IsEmittedAfterPlainDeclarations()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            fallback function early(): void {}
            function late(): void {}
            """);

        compiled.Errors.Should().BeEmpty();
        var late = compiled.Php.IndexOf("function late", StringComparison.Ordinal);
        var guard = compiled.Php.IndexOf("if (!\\function_exists", StringComparison.Ordinal);
        late.Should().BeGreaterThan(-1);
        guard.Should().BeGreaterThan(late);
    }

    [Fact]
    public void FallbackClass_EmitsClassExistsCheckInsideClassFile()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App\Payments;

            fallback final class Money {
                public function __construct(public readonly int $cents) {}
            }
            """);

        compiled.Errors.Should().BeEmpty();
        var classFile = compiled.Files.Should().ContainSingle(f => f.Path.EndsWith("Money.php", StringComparison.Ordinal)).Subject;
        classFile.Content.Should().Contain("if (!\\class_exists(__NAMESPACE__ . '\\Money')) {");
        classFile.Content.Should().Contain("final class Money");
    }

    [Theory]
    [InlineData("interface", "interface_exists")]
    [InlineData("trait", "trait_exists")]
    public void FallbackInterfaceAndTrait_UseMatchingExistsCall(string keyword, string call)
    {
        var compiled = Compile($$"""
            <?tyhp
            namespace App;

            fallback {{keyword}} Shape {
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain($"if (!\\{call}(__NAMESPACE__ . '\\Shape')) {{");
    }

    [Fact]
    public void FallbackEnum_UsesEnumExists()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            fallback enum Suit {
                case Hearts;
            }
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("if (!\\enum_exists(__NAMESPACE__ . '\\Suit')) {");
    }

    [Fact]
    public void FallbackConst_GlobalNamespace_DefinesWithoutLeadingBackslash()
    {
        var compiled = Compile("""
            <?tyhp
            fallback const int MY_CONST_VAL = 13;
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("if (!\\defined('MY_CONST_VAL')) {");
        compiled.Php.Should().Contain("\\define('MY_CONST_VAL', 13);");
        compiled.Php.Should().NotContain("'\\MY_CONST_VAL'");
    }

    [Fact]
    public void FallbackConst_InNamespace_UsesNamespaceConcat()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            fallback const MY_CONST_VAL = 13;
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("if (!\\defined(__NAMESPACE__ . '\\MY_CONST_VAL')) {");
        compiled.Php.Should().Contain("\\define(__NAMESPACE__ . '\\MY_CONST_VAL', 13);");
    }

    [Fact]
    public void FallbackConst_MultipleDeclarators_EmitOneCheckEach()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            fallback const A = 1, B = 2;
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("\\define(__NAMESPACE__ . '\\A', 1);");
        compiled.Php.Should().Contain("\\define(__NAMESPACE__ . '\\B', 2);");
    }

    [Fact]
    public void PlainTypedFileLevelConst_StillRejected()
    {
        var compiled = Compile("""
            <?tyhp
            const int MY_CONST_VAL = 13;
            """);

        compiled.Errors.Should().Contain(e => e.StartsWith(MessageCode.CheckerFileLevelTypedConstNotAllowed.ToString()));
    }

    [Fact]
    public void FunctionNamedFallback_StillParses()
    {
        var compiled = Compile("""
            <?tyhp
            namespace App;

            function fallback(): int { return 1; }
            int $x = fallback();
            """);

        compiled.Errors.Should().BeEmpty();
        compiled.Php.Should().Contain("function fallback(): int");
        compiled.Php.Should().NotContain("_exists");
    }
}
