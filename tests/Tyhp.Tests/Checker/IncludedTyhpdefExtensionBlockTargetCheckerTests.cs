using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Block-target declaration checks for standalone extensions in included
/// <c>.tyhpdef</c> files (<c>TyhpdefIncludePaths</c>).
/// </summary>
[Trait("Category", "Checker")]
public class IncludedTyhpdefExtensionBlockTargetCheckerTests
{
    [Fact]
    public void IncludedExtension_MembersWithoutTarget_Reports4147()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            extension Ops {
                fn label(): string => "x";
            }
            """);

        errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerExtensionMissingExtends
            && (d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IncludedExtension_HeaderAndNestedGroup_Reports4362()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            extension MoneyOps extends string {
                fn label(): string => "m";
                extends int {
                    fn len(): int => 1;
                }
            }
            """);

        errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerExtensionHeaderAndNestedTargets
            && (d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void IncludedExtension_HeaderExtends_DoesNotReport4147()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            extension StringHelpers extends string {
                fn label(): string => "ok";
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionMissingExtends);
    }

    [Fact]
    public void ClassBodyTyhpdefExtensionFn_DoesNotReport4147()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            class Box {
                extension fn len(): int => 0;
            }
            """);

        errors.Should().NotContain(d => d.Code == MessageCode.CheckerExtensionMissingExtends);
    }

    /// <summary>
    /// <see cref="TyhpChecker.TryCheckIncludedStandaloneTyhpdefExtension"/> routes an included
    /// standalone extension through <c>ExtensionRule.Check</c> instead of the old thin-mapping
    /// splice-only path, so a genuine written-parameter-without-<c>&amp;</c> violation
    /// (TYHP4174) must still be reported — exactly once — from the new path.
    /// </summary>
    [Fact]
    public void IncludedExtension_WrittenParameterWithoutRef_Reports4174Once()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            extension Mut extends int {
                fn bump(int $n): int => ++$n;
            }
            """);

        errors.Where(d => d.Code == MessageCode.CheckerInlineParameterMutation)
            .Should().ContainSingle();
    }

    /// <summary>
    /// A block-target member that writes <c>$this</c> without <c>&amp;$this</c> is TYHP4369 —
    /// the old TYHP4174 splice check for <c>$this</c> writes must not also fire for the same
    /// member (<c>InlineSpliceRule.CheckMember</c> gates on
    /// <c>ExtensionBlockTargetChecks.IsUserExtensionBlock</c>), even when reached via the
    /// included-tyhpdef path.
    /// </summary>
    [Fact]
    public void IncludedExtension_UnannotatedThisWrite_Reports4369NotDuplicate4174()
    {
        var errors = IncludeTyhpdef("""
            <?tyhpdef
            extension MutArr extends array {
                fn sortInPlace(): bool => \sort($this);
            }
            """);

        errors.Where(d => d.Code == MessageCode.CheckerInlineParameterMutation)
            .Should().BeEmpty("block-target $this writes are TYHP4369, not the thin-mapping TYHP4174");
    }

    private static IReadOnlyList<IDiagnostic> IncludeTyhpdef(string tyhpdef)
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {}
            """,
            tyhpdef,
            skipChecking: false);

        return result.Diagnostics.Errors;
    }
}
