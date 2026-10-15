using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Checker-only <c>__</c> utility types used in a runtime type test (<c>is</c>, <c>typeof</c>)
/// reify into real <c>\Tyhp\Type</c> factories instead of a nonexistent utility class.
/// </summary>
[Trait("Category", "Emitter")]
public class UtilityTypeReificationEmitterTests
{
    [Fact]
    public void Emit_IsCallableReturnTypeOfConcreteShape_TestsTheReturnType()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function check(mixed $x): bool {
                return $x is __CallableReturnType<callable(): int>;
            }
            """);

        php.Should().Contain(@"\Tyhp\Type::is($x, \Tyhp\Type::int())");
        php.Should().NotContain("__CallableReturnType");
    }

    [Fact]
    public void Emit_IsCallableReturnTypeOfUnboundShape_AcceptsAnyValue()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Runner<TCallableShape> {
                public function check(mixed $result): bool {
                    return $result is __CallableReturnType<TCallableShape>;
                }
            }
            """);

        php.Should().Contain(@"\Tyhp\Type::is($result, \Tyhp\Type::mixed())");
        php.Should().NotContain("__CallableReturnType");
        php.Should().NotContain(@"Type::generic(");
    }

    [Fact]
    public void Emit_TypeofCallableReturnTypeOfConcreteShape_BuildsTheReturnType()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function describe(): \Tyhp\Type {
                return typeof(__CallableReturnType<callable(): string>);
            }
            """);

        php.Should().Contain(@"return \Tyhp\Type::string();");
        php.Should().NotContain("__CallableReturnType");
    }

    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "utility.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = "8.4",
                })
                .Build();
            var project = new Project(configuration);
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

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
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets,
                expressionTypes: result.ExpressionTypes);
            return string.Join(
                '\n',
                new TyhpEmitter(context).Emit(result.ParsedFiles!)
                    .Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
