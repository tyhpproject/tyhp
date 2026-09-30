using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// FOUND_BUGS #30: unannotated <c>array $digits = []</c> plus <c>$digits[] = int</c> must
/// infer a concrete list so <c>\implode</c> / <c>\array_reverse</c> accept it. Overlay
/// signatures are local fixtures, not live php-package overlays.
/// </summary>
[Trait("Category", "Checker")]
public class ArrayAppendInferenceTests
{
    private const string OverlayTyhpdef = """
        <?tyhpdef
        function implode(string $separator, array<int|string, mixed> $array): string;
        function array_reverse<TKey extends int|string, TValue>(
            array<TKey, TValue> $array,
            bool $preserve_keys = false
        ): array<TKey, TValue>;
        """;

    [Fact]
    public void Check_BareArrayAppend_Implode_No4015()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(): string {
                array $digits = [];
                $digits[] = 1;
                return \implode('', $digits);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch,
            $"implode of appended unannotated array must type-check: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void Check_BareArrayAppend_ImplodeArrayReverse_No4015()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(): string {
                array $digits = [];
                $digits[] = 1;
                return \implode('', \array_reverse($digits));
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch
                || d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"implode(array_reverse(appended unannotated array)) must type-check: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void Check_InferredEmptyArrayAppend_ImplodeArrayReverse_No4015()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(): string {
                $digits = [];
                $digits[] = 1;
                return \implode('', \array_reverse($digits));
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch
                || d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"implode(array_reverse(inferred empty-then-append)) must type-check: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void Check_BareArrayAppend_InLoop_ImplodeArrayReverse_No4015()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(int $n): string {
                array $digits = [];
                for (int $i = 0; $i < $n; $i++) {
                    $digits[] = $i;
                }
                return \implode('', \array_reverse($digits));
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch
                || d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"IntegerScaledBackend-style loop append must type-check: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void Check_TypedArrayAppend_StillRejectsWrongElement()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                array<int, string> $digits = [];
                $digits[] = 1;
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"typed array<int, string> must still reject int append: {Describe(errors)}");
    }

    [Fact]
    public void Check_BareArrayKeyedIntWrite_Implode_No4015()
    {
        // `$digits[$k] = <int>` with a known-`int` key is the same "grow an open array"
        // operation as `$digits[] = <int>` for type purposes — it must resolve keys the same way.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(int $k): string {
                array $digits = [];
                $digits[$k] = 1;
                return \implode('', $digits);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch,
            $"implode of keyed-written unannotated array must type-check: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void Check_BareArrayKeyedStringWrite_Implode_No4015()
    {
        // A `string` key write also grows an open array — it must not be forced to `int`.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(string $k): string {
                array $map = [];
                $map[$k] = 'v';
                return \implode('', $map);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch,
            $"implode of string-keyed unannotated array must type-check: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void Check_ArrayParameterKeyedIntWrite_Implode_No4015()
    {
        // Refining from a keyed write on an `array`-typed parameter is sound for later reads in
        // the same function body — only the local narrowing changes, not the declared signature.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(array $digits, int $k): string {
                $digits[$k] = 1;
                return \implode('', $digits);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType
                || d.Code == MessageCode.CheckerTypeMismatch,
            $"implode of keyed-written array parameter must type-check: {Describe(errors)}");
        errors.Should().BeEmpty($"unexpected errors: {Describe(errors)}");
    }

    [Fact]
    public void Check_TypedArrayKeyedWrite_StillRejectsWrongElement()
    {
        // Annotated `array<int, string>` must stay exact — a keyed write must not bypass the
        // element-type check the same way a plain `[]` append must not.
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                array<int, string> $digits = [];
                $digits[0] = 1;
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"typed array<int, string> must still reject int keyed write: {Describe(errors)}");
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var result = IsolatedCompilation.ParseSnippet(
            content,
            OverlayTyhpdef,
            phpVersion: "8.2",
            skipChecking: true,
            fileName: fileName,
            includeMinimalPhpStubs: false);
        result.GlobalScope.Should().NotBeNull(
            "bind should succeed: " + Describe(result.Diagnostics.Errors.ToList()));
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);
        return result.Diagnostics.Errors
            .Where(e => e.FileName is not null
                && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
            .ToList();
    }
}
