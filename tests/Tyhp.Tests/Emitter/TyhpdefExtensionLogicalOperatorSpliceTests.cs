using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// A tyhpdef extension member whose body uses <c>&amp;&amp;</c> / <c>||</c> is spliced with the
/// logical operator, never the pipe operator, whatever token id the tyhpdef lexer gave the operator.
/// </summary>
[Trait("Category", "Emitter")]
public class TyhpdefExtensionLogicalOperatorSpliceTests
{
    private const string Tyhpdef = """
        <?tyhpdef
        namespace Lib {
            extension IntOps extends int {
                fn between(int $min, int $max): bool => $this >= $min && $this <= $max;
                fn outside(int $min, int $max): bool => $this < $min || $this > $max;
            }
            extension BoolOps extends bool {
                fn both(bool $other): bool => $this && $other;
            }
        }
        """;

    [Theory]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void Splice_AndOrBodies_UseLogicalOperators(string phpVersion)
    {
        var php = CompileAndEmit(
            """
            <?tyhp
            global use extension \Lib\IntOps;
            global use extension \Lib\BoolOps;

            function probe(int $a, bool $b): void {
                bool $in = $a->between(1, 3);
                bool $out = $a->outside(1, 3);
                bool $both = $b->both($in);
            }
            """,
            phpVersion);

        php.Should().Contain("&&");
        php.Should().Contain("||");
        php.Should().NotContain("|>");
    }

    private static string CompileAndEmit(string tyhp, string phpVersion)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "ops.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, "main.tyhp");
        File.WriteAllText(tyhpdefPath, Tyhpdef);
        File.WriteAllText(tyhpPath, tyhp);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["output:phpVersion"] = phpVersion })
                .Build();
            var project = new Project(configuration);
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [tyhpdefPath, tyhpPath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion: phpVersion));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
