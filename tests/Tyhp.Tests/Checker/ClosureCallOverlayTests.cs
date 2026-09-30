using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Overlay merge of <c>call</c> / <c>bindTo</c> must not duplicate harvest symbols.
/// Uses a synthetic Closure harvest+overlay, not the live Ext.Core overlay.
/// </summary>
[Trait("Category", "Checker")]
public class ClosureCallOverlayTests
{
    [Fact]
    public void CallAndBindTo_DoNotReportDuplicateBindSymbols()
    {
        using var builder = IsolatedCompilation.CreateOverlayProject(
            phpVersion: "8.5",
            includeTyhpdef: SyntheticPhpStubs.ClosureFiberHarvest,
            overlayTyhpdef: SyntheticPhpStubs.ClosureFiberOverlay,
            userTyhp: """
                <?tyhp
                function demo(\Closure $c, \stdClass $o): void {
                    $c->call($o);
                    $c->bindTo($o);
                }
                """);
        var result = IsolatedCompilation.BindProject(builder, skipChecking: true);
        result.GlobalScope.Should().NotBeNull("bind should succeed");
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);

        result.Diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderDuplicateSymbolDeclaration
            && (d.Message.Contains("`bind`", StringComparison.OrdinalIgnoreCase)
                || d.Message.Contains("`bindTo`", StringComparison.OrdinalIgnoreCase)),
            "overlay bind/bindTo overloads must not duplicate Layer 2: "
            + string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void GenericConstraintOnTyhpdefSignature_ReportsInTheTyhpdefFile()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {
                takes_bad_array([]);
            }
            """,
            """
            <?tyhpdef
            function takes_bad_array(array<mixed, int> $a): void;
            """,
            skipChecking: true);

        result.GlobalScope.Should().NotBeNull("bind should succeed");
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);

        var constraint = result.Diagnostics.Errors
            .Where(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied)
            .ToList();
        constraint.Should().NotBeEmpty("array<mixed, int> must fail KeyIntOrString");
        constraint.Should().OnlyContain(
            d => d.FileName.Replace('\\', '/').Contains(".tyhpdef", StringComparison.Ordinal),
            "TYHP4035 on a tyhpdef signature must keep the tyhpdef path, not the calling .tyhp file. Got: "
            + string.Join(", ", constraint.Select(d => $"{d.FileName}:{d.Line} {d.Message}")));
    }
}
