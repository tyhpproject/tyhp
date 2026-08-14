using System;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class OverloadEmitterTests
{
    private static string CompileAndEmit(string tyhp, string? tempRoot = null)
    {
        var tempDir = tempRoot ?? Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "overloads.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .ToList();
            unexpectedErrors.Should().BeEmpty($"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => e.Message))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Emit_OverloadSignatures_EraseSignaturesKeepImplementation()
    {
        var php = CompileAndEmit(@"
<?tyhp

// this is an overload that says when $convertToInt is true, then it will return an int
function convertNumber(string|int|float $value, true $convertToInt): int;
// this is an overload that says when $convertToInt is false, then it will return a float
function convertNumber(string|int|float $value, false $convertToInt): float;
// then this is the implementation that MUST cover all scenarios.
// this also indicates that if the $convertToInt argument is omitted, then this method will return a float because it defaults to false
function convertNumber(string|int|float $value, bool $convertToInt = false): int|float
{
    return $convertToInt ? \intval($value) : \floatval($value);
}
");

        php.Should().NotContain("function convertNumber(string|int|float $value, true $convertToInt): int;");
        php.Should().NotContain("function convertNumber(string|int|float $value, false $convertToInt): float;");
        php.Should().Contain("function convertNumber(string | int | float $value, bool $convertToInt = false): int | float");
        php.Should().Contain("return $convertToInt ? \\intval($value) : \\floatval($value);");
    }

    [Fact]
    public void Emit_ClassMethodOverloads_EraseSignaturesKeepImplementation()
    {
        var php = CompileAndEmit(@"
<?tyhp

class Calculator
{
    // overload: int + int -> int
    function add(int $a, int $b): int;

    // overload: float + float -> float
    function add(float $a, float $b): float;

    // implementation: covers both cases
    function add(int|float $a, int|float $b): int|float
    {
        return $a + $b;
    }
}
");

        php.Should().NotContain("function add(int $a, int $b): int;");
        php.Should().NotContain("function add(float $a, float $b): float;");
        php.Should().Contain("function add(int | float $a, int | float $b): int | float");
        php.Should().Contain("return $a + $b;");
    }

    [Fact]
    public void Emit_NoOverloads_EmitsUnchangedCode()
    {
        var php = CompileAndEmit(@"
<?tyhp

function hello(): void
{
    echo 'world';
}
");

        php.Should().Contain("function hello(): void");
        php.Should().Contain("echo 'world';");
    }

    [Fact]
    public void Emit_OverloadWithShortFunctionSyntax_EraseSignaturesKeepImplementation()
    {
        var php = CompileAndEmit(@"
<?tyhp

// overload signature
function convertNumber(string|int|float $value, true $convertToInt): int;

// implementation using short function syntax (`fn ... => expr`)
fn convertNumber(string|int|float $value, bool $convertToInt = false): int|float =>
    $convertToInt ? \intval($value) : \floatval($value);
");

        // The compile-time-only overload signature is erased.
        php.Should().NotContain("function convertNumber(string|int|float $value, true $convertToInt): int;");
        // PHP has no named arrow functions, so the short-function implementation emits as a normal
        // function whose body returns the expression.
        php.Should().Contain("function convertNumber(string | int | float $value, bool $convertToInt = false): int | float");
        php.Should().Contain("return $convertToInt ? \\intval($value) : \\floatval($value);");
    }

}
