using Tyhp.Config;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
public class BinderArgSelectionTests
{
    [Fact]
    public void SelectBinderArgs_DropsAbsolutePositionalSoItCannotSwallowTheNextFlag()
    {
        // `/tmp/demo` is the Windows switch prefix to CommandLineConfigurationProvider, which would
        // otherwise read it as `--tmp/demo` with `--yes=true` as its value.
        var selected = ActionConfigProvider.SelectBinderArgs(["/tmp/demo", "--yes=true"]);

        selected.Should().Equal("--yes=true");
    }

    [Fact]
    public void SelectBinderArgs_DropsRelativePositionals()
    {
        var selected = ActionConfigProvider.SelectBinderArgs(["src/Models", "--quiet=true"]);

        selected.Should().Equal("--quiet=true");
    }

    [Fact]
    public void SelectBinderArgs_KeepsTheValueOfASpaceSeparatedLongOption()
    {
        var selected = ActionConfigProvider.SelectBinderArgs(
            ["--tyhp-project", "/tmp/demo/tyhp.json", "--locale", "en-US"]);

        selected.Should().Equal("--tyhp-project", "/tmp/demo/tyhp.json", "--locale", "en-US");
    }

    [Fact]
    public void SelectBinderArgs_KeepsInlineValuesAndShortFlags()
    {
        var selected = ActionConfigProvider.SelectBinderArgs(
            ["src/Models", "--include=src/**/*.tyhp", "-q", "--dry-run=true"]);

        selected.Should().Equal("--include=src/**/*.tyhp", "-q", "--dry-run=true");
    }

    [Fact]
    public void SelectBinderArgs_ShortFlagDoesNotConsumeTheFollowingToken()
    {
        // A single-dash flag with no switch mapping is ignored by the provider and takes no value,
        // so the flag after it must survive.
        var selected = ActionConfigProvider.SelectBinderArgs(["-q", "--json=true"]);

        selected.Should().Equal("-q", "--json=true");
    }

    [Fact]
    public void SelectBinderArgs_LeavesFlagOnlyArgvUnchanged()
    {
        var input = new[] { "--quiet=true", "--verbose=true" };

        ActionConfigProvider.SelectBinderArgs(input).Should().Equal(input);
    }

    [Fact]
    public void ExtractPositionalPaths_SpaceSeparatedFormatValueIsNotCollectedAsPath()
    {
        // Reproduces Story 14 Phase 5 #1: `tyhp lint --format json clean.tyhp` used to treat
        // `json` as an explicit path (TYHP7201) while also binding it to --format.
        string[] raw = ["lint", "--format", "json", "clean.tyhp"];
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(raw);

        ActionConfigProvider.ReadInitialActionFromArgs(expanded, raw).Should().BeTrue();

        ActionConfigProvider.ExplicitPaths.Should().Equal("clean.tyhp");
    }

    [Fact]
    public void ExtractPositionalPaths_InlineFormatValueLeavesOnlyRealPaths()
    {
        string[] raw = ["lint", "--format=json", "clean.tyhp"];
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(raw);

        ActionConfigProvider.ReadInitialActionFromArgs(expanded, raw).Should().BeTrue();

        ActionConfigProvider.ExplicitPaths.Should().Equal("clean.tyhp");
    }

    [Fact]
    public void ExtractPositionalPaths_SpaceSeparatedBuildOptionsLeaveOnlyRealPaths()
    {
        string[] raw =
        [
            "build",
            "--include",
            "src/**/*.tyhp",
            "--exclude",
            "vendor/**",
            "--cache-dir",
            "/tmp/tyhp-cache",
            "src/Models",
        ];
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(raw);

        ActionConfigProvider.ReadInitialActionFromArgs(expanded, raw).Should().BeTrue();

        ActionConfigProvider.ExplicitPaths.Should().Equal("src/Models");
    }

    [Fact]
    public void ExtractPositionalPaths_BareBooleanDoesNotSwallowFollowingPath()
    {
        string[] raw = ["lint", "--strict", "clean.tyhp"];
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(raw);

        ActionConfigProvider.ReadInitialActionFromArgs(expanded, raw).Should().BeTrue();

        ActionConfigProvider.ExplicitPaths.Should().Equal("clean.tyhp");
    }

    [Fact]
    public void ValueTakingFlags_IncludesDocumentedLintAndGlobalOptions()
    {
        ActionConfigProvider.ValueTakingFlags.Should().Contain("--format");
        ActionConfigProvider.ValueTakingFlags.Should().Contain("--file");
        ActionConfigProvider.ValueTakingFlags.Should().Contain("--include");
        ActionConfigProvider.ValueTakingFlags.Should().Contain("--exclude");
        ActionConfigProvider.ValueTakingFlags.Should().Contain("--cache-dir");
        ActionConfigProvider.ValueTakingFlags.Should().Contain("--max-fix-iterations");
        ActionConfigProvider.ValueTakingFlags.Should().Contain("--tyhp-project");
        ActionConfigProvider.ValueTakingFlags.Should().Contain("--locale");
    }
}
