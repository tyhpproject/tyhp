using Tyhp.Config;

namespace Tyhp.Tests.CLI;

[Trait("Category", "CLI")]
public class HelpAliasRewriteTests
{
    [Fact]
    public void ExpandBareBooleanFlags_ExpandsBareHelpToTrue()
    {
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(["version", "--help"]);

        expanded.Should().Equal("version", "--help=true");
    }

    [Fact]
    public void ExpandBareBooleanFlags_DoesNotSwallowTokenAfterBareHelp()
    {
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(["--help", "version"]);

        expanded.Should().Equal("--help=true", "version");
    }

    [Fact]
    public void ExpandBareBooleanFlags_PreservesExplicitHelpFalse()
    {
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(["version", "--help", "false"]);

        expanded.Should().Equal("version", "--help=false");
    }

    [Fact]
    public void ExpandBareBooleanFlags_ExpandsBareJson()
    {
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(["version", "--json"]);

        expanded.Should().Equal("version", "--json=true");
    }

    [Fact]
    public void BareBooleanFlags_IncludesHelpAndJson()
    {
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--help");
        ActionConfigProvider.BareBooleanFlags.Should().Contain("--json");
    }

    [Fact]
    public void ExpandBareBooleanFlags_ResolvesShortYesAliasToItsLongForm()
    {
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(["init", "-y"]);

        expanded.Should().Equal("init", "--yes=true");
    }

    [Fact]
    public void ExpandBareBooleanFlags_ResolvesShortQuietAliasToItsLongForm()
    {
        // Help text documents `--quiet / -q`, so the short spelling has to reach the `quiet` key.
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(["integrity_check", "-q"]);

        expanded.Should().Equal("integrity_check", "--quiet=true");
    }

    [Fact]
    public void ExpandBareBooleanFlags_ShortAliasStillConsumesAnExplicitBoolean()
    {
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(["init", "-y", "false"]);

        expanded.Should().Equal("init", "--yes=false");
    }

    [Fact]
    public void ExpandBareBooleanFlags_NeverEmitsSingleDashAssignments()
    {
        // CommandLineConfigurationProvider throws on `-x=value` without a switch mapping, so an
        // expanded short alias must always come out in its long `--flag=value` spelling.
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(
            [.. ActionConfigProvider.ShortBooleanFlagAliases.Keys]);

        expanded.Should().OnlyContain(arg => !arg.Contains('=') || arg.StartsWith("--"));
    }

    [Fact]
    public void ShortBooleanFlagAliases_MapOntoKnownBareBooleanFlags()
    {
        foreach (var longForm in ActionConfigProvider.ShortBooleanFlagAliases.Values)
        {
            ActionConfigProvider.BareBooleanFlags.Should().Contain(longForm);
        }
    }

    [Theory]
    [InlineData(new[] { "--help" }, new[] { "help" })]
    [InlineData(new[] { "--help=true" }, new[] { "help" })]
    [InlineData(new[] { "lint", "--help=true" }, new[] { "help", "--subject=lint" })]
    [InlineData(new[] { "build", "--help=true" }, new[] { "help", "--subject=build" })]
    [InlineData(new[] { "help", "--help=true" }, new[] { "help", "--subject=help" })]
    [InlineData(new[] { "version", "--help=true" }, new[] { "help", "--subject=version" })]
    [InlineData(new[] { "dump-ast", "--help=true" }, new[] { "help", "--subject=dump_ast" })]
    [InlineData(new[] { "unknown-command", "--help=true" }, new[] { "help" })]
    [InlineData(new[] { "--quiet=true", "--help=true" }, new[] { "help", "--quiet=true" })]
    public void RewriteHelpAlias_EquivalenceTable(string[] input, string[] expected)
    {
        ActionConfigProvider.RewriteHelpAlias(input).Should().Equal(expected);
    }

    [Fact]
    public void RewriteHelpAlias_KeepsGlobalFlagsSoTheyStillApplyToHelp()
    {
        var rewritten = ActionConfigProvider.RewriteHelpAlias(
            ["--quiet=true", "version", "--locale=en-US", "--help=true"]);

        rewritten.Should().Equal("help", "--subject=version", "--quiet=true", "--locale=en-US");
    }

    [Fact]
    public void RewriteHelpAlias_DropsPositionalPathsBecauseHelpConsumesNone()
    {
        var rewritten = ActionConfigProvider.RewriteHelpAlias(["lint", "src/User.tyhp", "--help=true"]);

        rewritten.Should().Equal("help", "--subject=lint");
    }

    [Fact]
    public void RewriteHelpAlias_CommandWins_OverUserSuppliedSubject()
    {
        var rewritten = ActionConfigProvider.RewriteHelpAlias(["help", "--subject=build", "--help=true"]);

        rewritten.Should().Equal("help", "--subject=help");
    }

    [Fact]
    public void RewriteHelpAlias_NumericTokenIsNotTreatedAsAnAction()
    {
        var rewritten = ActionConfigProvider.RewriteHelpAlias(["2", "--help=true"]);

        rewritten.Should().Equal("help");
    }

    [Theory]
    [InlineData("VERSION", "version")]
    [InlineData("dump-ast", "dump_ast")]
    [InlineData("Generate_Tyhpdef", "generate_tyhpdef")]
    public void TryParseAction_AcceptsCaseAndHyphenVariants(string text, string expected)
    {
        ActionConfigProvider.TryParseAction(text, out var action).Should().BeTrue();
        action.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nope")]
    public void TryParseAction_RejectsNumericAndUnknownTokens(string text)
    {
        ActionConfigProvider.TryParseAction(text, out var action).Should().BeFalse();
        action.ToString().Should().Be("invalid");
    }

    [Fact]
    public void RewriteHelpAlias_HelpFalse_DoesNotRewrite()
    {
        var input = new[] { "version", "--help=false" };

        ActionConfigProvider.RewriteHelpAlias(input).Should().Equal(input);
    }

    [Fact]
    public void RewriteHelpAlias_AbsentHelp_LeavesArgsUnchanged()
    {
        var input = new[] { "version", "--json=true" };

        ActionConfigProvider.RewriteHelpAlias(input).Should().Equal(input);
    }

    [Fact]
    public void RewriteHelpAlias_AfterExpand_MatchesEquivalence()
    {
        var expanded = ActionConfigProvider.ExpandBareBooleanFlags(["lint", "--help"]);
        var rewritten = ActionConfigProvider.RewriteHelpAlias(expanded);

        rewritten.Should().Equal("help", "--subject=lint");
    }
}
