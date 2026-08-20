using Tyhp.CLI;
using Tyhp.Config;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
public class ComposerActionTests
{
    [Fact]
    public void FilterComposerArgs_StripsNoTyhpdefAndKeepsComposerTokens()
    {
        var filtered = ComposerAction.FilterComposerArgs(
        [
            "require",
            "guzzlehttp/guzzle",
            "--no-tyhpdef=true",
            "--prefer-dist",
        ]);

        filtered.Should().Equal("require", "guzzlehttp/guzzle", "--prefer-dist");
    }

    [Fact]
    public void FilterComposerArgs_StripsSpaceSeparatedPidFileValue()
    {
        var filtered = ComposerAction.FilterComposerArgs(
        [
            "install",
            "--pid-file",
            "/tmp/tyhp.pid",
            "--no-dev",
        ]);

        filtered.Should().Equal("install", "--no-dev");
    }

    [Fact]
    public void FilterComposerArgs_LeavesComposerVersionFlag()
    {
        ComposerAction.FilterComposerArgs(["--version"])
            .Should().Equal("--version");
    }

    [Theory]
    [InlineData(new[] { "install" }, true)]
    [InlineData(new[] { "update", "--with-dependencies" }, true)]
    [InlineData(new[] { "require", "foo/bar" }, true)]
    [InlineData(new[] { "--quiet=true", "install" }, true)]
    [InlineData(new[] { "--version" }, false)]
    [InlineData(new[] { "validate" }, false)]
    [InlineData(new string[0], false)]
    public void ShouldOfferTyhpdefHook_OnlyForInstallUpdateRequire(string[] args, bool expected)
    {
        ComposerAction.ShouldOfferTyhpdefHook(args).Should().Be(expected);
    }

    [Fact]
    public void FilterComposerArgs_KeepsSubcommandAfterBareNoTyhpdef()
    {
        // `--no-tyhpdef` takes no value, so the subcommand that follows it is Composer's.
        ComposerAction.FilterComposerArgs(["--no-tyhpdef", "require", "foo/bar"])
            .Should().Equal("require", "foo/bar");
    }

    [Fact]
    public void FilterComposerArgs_StripsExplicitBooleanLiteralAfterTyhpFlag()
    {
        ComposerAction.FilterComposerArgs(["--no-tyhpdef", "false", "install"])
            .Should().Equal("install");
    }

    [Fact]
    public void ReadInitialActionFromArgs_RemainingArgsPreserveRawComposerFlags()
    {
        string[] raw = ["composer", "install", "--no-tyhpdef", "--dry-run", "--prefer-dist"];
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(raw);

        ActionConfigProvider.ReadInitialActionFromArgs(expanded, raw).Should().BeTrue();

        // Composer's own flags must reach it exactly as typed: Symfony Console rejects
        // `--dry-run=true` on a value-less option.
        ActionConfigProvider.RemainingArgs.Should().Equal(
            "install",
            "--no-tyhpdef",
            "--dry-run",
            "--prefer-dist");
        ComposerAction.FilterComposerArgs(ActionConfigProvider.RemainingArgs)
            .Should().Equal("install", "--dry-run", "--prefer-dist");
        ActionConfigProvider.ExplicitPaths.Should().Equal("install");
    }

    [Fact]
    public void ReadInitialActionFromArgs_FallsBackToRewrittenArgsWhenVerbWasRewritten()
    {
        // `tyhp composer --help` becomes `help --subject=composer`, so the raw tokens belong to a
        // different action and must not be exposed as the proxied argv.
        string[] raw = ["composer", "--help"];
        var rewritten = ActionConfigProvider.RewriteHelpAlias(
            ActionConfigProvider.ExpandBareBooleanFlags(raw));

        ActionConfigProvider.ReadInitialActionFromArgs(rewritten, raw).Should().BeTrue();

        ActionConfigProvider.RemainingArgs.Should().Equal("--subject=composer");
    }

    [Fact]
    public void BareBooleanFlags_IncludesNoTyhpdef()
    {
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--no-tyhpdef");
    }
}
