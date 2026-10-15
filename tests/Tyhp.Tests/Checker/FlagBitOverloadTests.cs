using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A tyhpdef integer-literal parameter matches a bitwise combination of known
/// integer constants when every bit of that literal is set. The most specific
/// matching literal wins; two equal matches fall through to the <c>int</c> signature.
/// A bare integer still has to equal the literal.
/// </summary>
[Trait("Category", "Checker")]
public class FlagBitOverloadTests
{
    private const string JsonEncodeDefs = """
        <?tyhpdef
        const int JSON_THROW_ON_ERROR ?? 4194304;
        const int JSON_UNESCAPED_SLASHES ?? 64;
        const int JSON_PRETTY_PRINT ?? 128;

        function json_encode(mixed $value, 4194304 $flags, int $depth = 512): string;
        function json_encode(mixed $value, int $flags = 0, int $depth = 512): string|false;
        """;

    [Fact]
    public void JsonEncode_ThrowBitCombinedWithOtherFlags_ReturnsString()
    {
        var result = Compile(
            """
            <?tyhp
            function demo(mixed $v): string {
                return \json_encode($v, \JSON_THROW_ON_ERROR | \JSON_UNESCAPED_SLASHES);
            }
            """,
            JsonEncodeDefs);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void JsonEncode_ExactThrowFlag_ReturnsString()
    {
        var result = Compile(
            """
            <?tyhp
            function demo(mixed $v): string {
                return \json_encode($v, \JSON_THROW_ON_ERROR);
            }
            """,
            JsonEncodeDefs);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void JsonEncode_FlagsWithoutThrowBit_StaysStringOrFalse()
    {
        var result = Compile(
            """
            <?tyhp
            function demo(mixed $v): string {
                return \json_encode($v, \JSON_PRETTY_PRINT | \JSON_UNESCAPED_SLASHES);
            }
            """,
            JsonEncodeDefs);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(result));
    }

    [Fact]
    public void BitwiseOr_ThatEqualsALiteral_SelectsThatLiteral()
    {
        var result = Compile(
            """
            <?tyhp
            function demo(): string {
                return \take(\BIT_A | \BIT_B);
            }
            """,
            """
            <?tyhpdef
            const int BIT_A ?? 1;
            const int BIT_B ?? 2;

            function take(1 $flags): string;
            function take(2 $flags): string;
            function take(3 $flags): string;
            function take(int $flags): int;
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    [Fact]
    public void TiedSingleBitMasks_UseIntSignature()
    {
        var result = Compile(
            """
            <?tyhp
            function asString(): string {
                return \take(\BIT_A | \BIT_C);
            }
            """,
            """
            <?tyhpdef
            const int BIT_A ?? 1;
            const int BIT_C ?? 4;

            function take(1 $flags): string;
            function take(4 $flags): string;
            function take(int $flags): int;
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(result));
    }

    [Fact]
    public void BareInteger_DoesNotMatchADifferentLiteralByBits()
    {
        var result = Compile(
            """
            <?tyhp
            function asString(): string {
                return \take(5);
            }
            """,
            """
            <?tyhpdef
            function take(1 $flags): string;
            function take(int $flags): int;
            """);

        UserErrors(result).Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(result));
    }

    [Fact]
    public void MoreSpecificMask_WinsOverASubset()
    {
        var result = Compile(
            """
            <?tyhp
            function demo(mixed $v): Both {
                return \decode($v, \FLAG_THROW | \FLAG_ASSOC | \FLAG_EXTRA);
            }
            """,
            """
            <?tyhpdef
            class Narrow {}
            class Wide {}
            class Both {}
            class Fallback {}
            const int FLAG_THROW ?? 4194304;
            const int FLAG_ASSOC ?? 1;
            const int FLAG_EXTRA ?? 64;

            function decode(mixed $value, 1 $flags): Narrow;
            function decode(mixed $value, 4194304 $flags): Wide;
            function decode(mixed $value, 4194305 $flags): Both;
            function decode(mixed $value, int $flags): Fallback;
            """);

        UserErrors(result).Should().BeEmpty(Describe(result));
    }

    private static CompilationResult Compile(string tyhp, string tyhpdef) =>
        IsolatedCompilation.ParseSnippet(tyhp, tyhpdef);

    private static IReadOnlyList<IDiagnostic> UserErrors(CompilationResult result) =>
        result.Diagnostics.Errors
            .Where(e => (e.FileName ?? "").EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(CompilationResult result) =>
        string.Join("; ", result.Diagnostics.Errors.Select(e => $"{(int)e.Code}: {e.Message}"));
}
