using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.8 Phase 6 checker companions: Closure alias-bound expansion,
/// WeakMap append, and tyhpdef <c>const int NAME ?? N</c> at overload selection.
/// Tuple index and union-of-closures return live in
/// <see cref="CallableSignatureUtilityTests"/>.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Story21.8")]
public class Story218Phase6CheckerCompanionTests
{
    [Fact]
    public void Closure_TwoArgTThis_HostClass_DoesNotReport4035()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Host {}
            function demo(\Closure<callable(int): string, Host> $c): void {}
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            "Host must satisfy __ClosureThis after alias expansion: " + Describe(errors));
    }

    [Fact]
    public void Closure_ThreeArgTThisTScope_HostClass_DoesNotReport4035()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Host {}
            function demo(\Closure<callable(int): string, Host, Host> $c): void {}
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            "Host must satisfy __ClosureThis / __ClosureScope<Host>: " + Describe(errors));
    }

    [Fact]
    public void WeakMap_Append_ReportsTypeMismatch()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function append(\WeakMap<object, string> $map, object $obj): void {
                $map[] = $obj;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "$map[] = must reject null as a WeakMap key: " + Describe(errors));
    }

    [Fact]
    public void WeakMap_KeyedAssign_AcceptsObjectKey()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function write(\WeakMap<object, string> $map, object $obj): void {
                $map[$obj] = "x";
            }
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerTypeMismatch,
            "$map[$obj] = \"x\" should type-check: " + Describe(errors));
    }

    [Fact]
    public void TyhpdefConstIntLiteral_SelectsExactOverload()
    {
        var errors = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            const int PICK_ONE ?? 1;
            function pick(1 $n): string;
            function pick(-1 $n): bool;
            function pick(int $n): int;
            """,
            """
            <?tyhp
            function demo(): string {
                return \pick(\PICK_ONE);
            }
            """);

        errors.Should().BeEmpty(
            "const int PICK_ONE ?? 1 must select pick(1): string, not pick(int): int: "
            + Describe(errors));
    }

    [Fact]
    public void TyhpdefUnaryMinusLiteral_DoesNotSelectPositiveLiteralOverload()
    {
        var errors = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            function pick(1 $n): string;
            function pick(-1 $n): bool;
            function pick(int $n): int;
            """,
            """
            <?tyhp
            function demo(): bool {
                return \pick(-1);
            }
            """);

        errors.Should().BeEmpty(
            "-1 must select pick(-1): bool, not pick(1): string: " + Describe(errors));
    }

    [Fact]
    public void TyhpdefUnaryMinusFloatLiteral_SelectsNegativeLiteralOverload()
    {
        // The grammar accepts a signed float TYPE literal (`scalarTypeNegativeDNumber`),
        // so `-1.5 $n` must bind as its own overload distinct from `1.5 $n`. This exercises
        // the float-literal path of `InferUnaryNumeric`, alongside the existing integer
        // coverage in `TyhpdefUnaryMinusLiteral_DoesNotSelectPositiveLiteralOverload`.
        var errors = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            function pickFloat(1.5 $n): string;
            function pickFloat(-1.5 $n): bool;
            function pickFloat(float $n): int;
            """,
            """
            <?tyhp
            function demo(): bool {
                return \pickFloat(-1.5);
            }
            """);

        errors.Should().BeEmpty(
            "-1.5 must select pickFloat(-1.5): bool, not pickFloat(1.5): string: " + Describe(errors));
    }

    [Fact]
    public void NamedConstInt_SelectsMatchingLiteralOverload()
    {
        var errors = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            const int LOOKUP_HOST ?? 1;
            function lookup_url(string $u): array|false;
            function lookup_url(string $u, 1 $component): string|false|null;
            """,
            """
            <?tyhp
            function demo(string $u): string|false|null {
                return \lookup_url($u, \LOOKUP_HOST);
            }
            """);

        errors.Should().BeEmpty(
            "lookup_url($u, LOOKUP_HOST) must select the 1-literal string overload: "
            + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
        => CompileAndCheckWithTyhpdefs(
            content,
            SyntheticPhpStubs.ClosureGeneric,
            SyntheticPhpStubs.WeakMap);

    private static IReadOnlyList<IDiagnostic> CompileAndCheckWithTyhpdef(string tyhpdef, string tyhp)
        => CompileAndCheckWithTyhpdefs(tyhp, tyhpdef);

    private static IReadOnlyList<IDiagnostic> CompileAndCheckWithTyhpdefs(string tyhp, params string[] tyhpdefs)
    {
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var result = IsolatedCompilation.ParseSnippet(
            tyhp,
            tyhpdefs,
            phpVersion: "8.4",
            skipChecking: true,
            fileName: fileName,
            includeMinimalPhpStubs: false);
        result.GlobalScope.Should().NotBeNull(
            "bind should succeed: " + Describe(result.Diagnostics.Errors.ToList()));
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);
        return UserFileErrors(result.Diagnostics, fileName);
    }

    private static IReadOnlyList<IDiagnostic> UserFileErrors(DiagnosticBag diagnostics, string fileName) =>
        diagnostics.Errors
            .Where(e => e.FileName is not null
                && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
            .ToList();
}
