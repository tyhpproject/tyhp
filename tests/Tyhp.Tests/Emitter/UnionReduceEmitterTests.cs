using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// PHP union typehints must flatten nested alias expansions and drop duplicate
/// members. <c>JsonAssoc|JsonObject|null</c> must not emit two concatenated
/// <c>string|int|…|array</c> runs.
/// </summary>
[Trait("Category", "Emitter")]
public class UnionReduceEmitterTests
{
    [Fact]
    public void Emit_OverlappingJsonAliases_ReducesPhpUnion()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type JsonScalar = string|int|float|bool|null;
            type JsonAssoc = JsonScalar|array;
            type JsonObject = JsonScalar|array|\stdClass;

            function decode(JsonAssoc|JsonObject|null $value): JsonAssoc|JsonObject|null {
                return $value;
            }
            """);

        const string reduced = "string|int|float|bool|array|\\stdClass|null";
        php.Should().Contain($"function decode({reduced} $value): {reduced}");
        php.Should().NotContain(
            "string|int|float|bool|null|array|string|int|float|bool|null|array");
    }

    [Fact]
    public void Emit_OverlappingTyhpdefAliases_ReducesPhpUnion()
    {
        var php = CompileAndEmit(
            """
            <?tyhp
            function decode(\JsonAssoc|\JsonObject|null $value): \JsonAssoc|\JsonObject|null {
                return $value;
            }
            """,
            tyhpdef: """
            <?tyhpdef
            type JsonScalar = string|int|float|bool|null;
            type JsonAssoc = JsonScalar|array;
            type JsonObject = JsonScalar|array|\stdClass;
            """);

        const string reduced = "string|int|float|bool|array|\\stdClass|null";
        php.Should().Contain($"function decode({reduced} $value): {reduced}");
        php.Should().NotContain(
            "string|int|float|bool|null|array|string|int|float|bool|null|array");
    }

    [Fact]
    public void Emit_DuplicateAliasMember_CollapsesToSingleType()
    {
        // Checker rejects `A|A` (TYHP4053) after expanding both arms to `int`. Skip checking
        // so emit still proves `T|T` → `T`.
        var php = CompileAndEmit("""
            <?tyhp
            type A = int;

            function f(A|A $x): A|A {
                return $x;
            }
            """, skipChecking: true);

        php.Should().Contain("function f(int $x): int");
        php.Should().NotContain("int|int");
    }

    [Fact]
    public void Emit_DuplicateIntAndNull_CollapsesToNullableInt()
    {
        // Checker rejects duplicate `int` / `null` arms (TYHP4053). Skip checking so emit
        // still proves `T|T|null|null` → `?T`.
        var php = CompileAndEmit("""
            <?tyhp
            function f(int|int|null|null $x): int|int|null|null {
                return $x;
            }
            """, skipChecking: true);

        php.Should().Contain("function f(?int $x): ?int");
        php.Should().NotContain("int|int");
        php.Should().NotContain("null|null");
    }

    [Fact]
    public void Emit_OverlappingNonIdenticalAliases_MergesSharedMembers()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type A = int|string;
            type B = string|bool;

            function f(A|B $x): A|B {
                return $x;
            }
            """);

        php.Should().Contain("function f(int|string|bool $x): int|string|bool");
        php.Should().NotContain("int|string|string|bool");
    }

    [Fact]
    public void Emit_ExtensionOverlappingJsonAliases_ReducesPhpUnion()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type JsonScalar = string|int|float|bool|null;
            type JsonAssoc = JsonScalar|array;
            type JsonObject = JsonScalar|array|\stdClass;

            extension StringJson extends string {
                function jsonDecode(): JsonAssoc|JsonObject|null {
                    return null;
                }
            }
            """);

        php.Should().Contain(
            "function jsonDecode(string $this_): string|int|float|bool|array|\\stdClass|null");
        php.Should().NotContain(
            "string|int|float|bool|null|array|string|int|float|bool|null|array");
    }

    private static string CompileAndEmit(string content, string? tyhpdef = null, bool skipChecking = false)
    {
        var result = IsolatedCompilation.ParseSnippet(
            content, tyhpdef: tyhpdef, phpVersion: "8.2", skipChecking: skipChecking);

        var unexpectedErrors = result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();
        unexpectedErrors.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
        var context = EmitContext.Create(
            result.GlobalScope,
            result.Diagnostics,
            requiresGenericVariant: result.RequiresGenericVariant,
            genericCallTargets: result.GenericCallTargets);
        var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
        return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
    }
}
