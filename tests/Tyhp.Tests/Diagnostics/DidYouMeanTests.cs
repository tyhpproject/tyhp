using Microsoft.Extensions.Localization;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class DidYouMeanTests : IDisposable
{
    private sealed class FakeLocalizer : IStringLocalizer<TyhpHostedService>
    {
        public LocalizedString this[string name]
            => name switch
            {
                "CLI_DiagnosticDidYouMean" => new LocalizedString(name, "did you mean `{0}`?"),
                _ => new LocalizedString(name, name),
            };

        public LocalizedString this[string name, params object[] arguments]
            => new(name, string.Format(this[name].Value, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => [this["CLI_DiagnosticDidYouMean"]];

        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    public DidYouMeanTests()
    {
        Message.SetLocalizer(new FakeLocalizer());
    }

    // Message.SetLocalizer is process-wide state; leaving the stub installed makes every later
    // test in the run read bare resource keys instead of the real catalog.
    public void Dispose() => Message.ResetLocalizer();

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("kitten", "sitting", 3)]
    [InlineData("User", "user", 1)]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abcd", 1)]
    [InlineData("abcd", "abc", 1)]
    public void Distance_ComputesExpectedEditCount(string a, string b, int expected)
    {
        DidYouMean.Distance(a, b).Should().Be(expected);
        DidYouMean.Distance(b, a).Should().Be(expected);
    }

    [Theory]
    [InlineData("ab", 1)]
    [InlineData("abc", 1)]
    [InlineData("abcdef", 2)]
    [InlineData("abcdefgh", 3)]
    public void MaxDistanceFor_ScalesWithLength(string actual, int expected)
        => DidYouMean.MaxDistanceFor(actual).Should().Be(expected);

    [Fact]
    public void FindBestMatch_ReturnsClosestWithinBudget()
    {
        var match = DidYouMean.FindBestMatch(
            "Useer",
            ["Account", "User", "Usage", "Other"]);

        match.Should().Be("User");
    }

    [Fact]
    public void FindBestMatch_PrefersCaseCorrection()
    {
        var match = DidYouMean.FindBestMatch(
            "user",
            ["User", "userId"]);

        match.Should().Be("User");
    }

    [Fact]
    public void FindBestMatch_ReturnsNullWhenNothingClose()
    {
        DidYouMean.FindBestMatch("zzzzzzz", ["User", "Account"])
            .Should().BeNull();
    }

    [Fact]
    public void FindBestMatch_SkipsExactOrdinalMatch()
    {
        DidYouMean.FindBestMatch("User", ["User", "Users"])
            .Should().Be("Users");
    }

    [Fact]
    public void Attach_AddsSuggestionAndHelp()
    {
        var diagnostic = Diagnostic.Error(
            MessageCode.BinderSymbolNotFound,
            "a.tyhp",
            line: 2,
            column: 10,
            ["Useer"]);

        var updated = DidYouMean.Attach(diagnostic, "Useer", ["User", "Account"]);

        updated.Suggestion.Should().NotBeNull();
        updated.Suggestion!.Value.Replacement.Should().Be("User");
        updated.Suggestion.Value.Span.Column.Should().Be(10);
        updated.Suggestion.Value.Span.EndColumn.Should().Be(15); // 10 + "Useer".Length
        updated.Help.Should().Be("did you mean `User`?");
        updated.Suggestion.Value.Description.Should().Be("did you mean `User`?");
    }

    [Fact]
    public void Attach_PreservesExistingHelp()
    {
        var diagnostic = Diagnostic.Error(
                MessageCode.BinderSymbolNotFound,
                "a.tyhp",
                1,
                0,
                ["Useer"])
            .WithHelp("see the docs");

        var updated = DidYouMean.Attach(diagnostic, "Useer", ["User"]);

        updated.Help.Should().Be("see the docs");
        updated.Suggestion.Should().NotBeNull();
        updated.Suggestion!.Value.Description.Should().Be("did you mean `User`?");
    }

    [Fact]
    public void Attach_QualifiedName_ReplacesOnlyTheFinalSegment()
    {
        var diagnostic = Diagnostic.Error(
            MessageCode.BinderUnresolvedParameterType,
            "a.tyhp",
            line: 4,
            column: 14,
            ["\\App\\Useer"]);

        var updated = DidYouMean.Attach(diagnostic, "\\App\\Useer", ["User"]);

        updated.Suggestion.Should().NotBeNull();
        updated.Suggestion!.Value.Replacement.Should().Be("User");
        // `\App\` is 5 characters, so the edit starts there and covers only `Useer`.
        updated.Suggestion.Value.Span.Column.Should().Be(19);
        updated.Suggestion.Value.Span.EndColumn.Should().Be(24);
    }

    [Fact]
    public void Attach_NullableName_LeavesTheQuestionMarkInPlace()
    {
        var diagnostic = Diagnostic.Error(
            MessageCode.BinderUnresolvedParameterType,
            "a.tyhp",
            line: 3,
            column: 14,
            ["?Useer"]);

        var updated = DidYouMean.Attach(diagnostic, "?Useer", ["User"]);

        updated.Suggestion.Should().NotBeNull();
        updated.Suggestion!.Value.Span.Column.Should().Be(15);
        updated.Suggestion.Value.Span.EndColumn.Should().Be(20);
    }

    [Theory]
    [InlineData("Usr|Name")]
    [InlineData("Usr&Name")]
    [InlineData("'typ'")]
    [InlineData("array<Usr>")]
    public void Attach_CompositeOrQuotedText_SuggestsNothing(string written)
    {
        var diagnostic = Diagnostic.Error(
            MessageCode.BinderUnresolvedParameterType,
            "a.tyhp",
            3,
            14,
            [written]);

        var updated = DidYouMean.Attach(diagnostic, written, ["UserName", "type", "User"]);

        updated.Should().BeSameAs(diagnostic);
        updated.Suggestion.Should().BeNull();
        updated.Help.Should().BeNull();
    }

    [Fact]
    public void Attach_FileLevelDiagnostic_AddsHelpButNoEditSpan()
    {
        // Line 0 is the producers' "no position in this file" marker — there is nothing to edit.
        var diagnostic = Diagnostic.Error(
            MessageCode.BinderSymbolNotFound,
            "a.tyhp",
            line: 0,
            column: 0,
            ["Useer"]);

        var updated = DidYouMean.Attach(diagnostic, "Useer", ["User"]);

        updated.Suggestion.Should().BeNull();
        updated.Help.Should().Be("did you mean `User`?");
    }

    [Fact]
    public void Attach_NoMatch_ReturnsUnchanged()
    {
        var diagnostic = Diagnostic.Error(
            MessageCode.BinderSymbolNotFound,
            "a.tyhp",
            1,
            0,
            ["zzzzzzz"]);

        var updated = DidYouMean.Attach(diagnostic, "zzzzzzz", ["User"]);

        updated.Should().BeSameAs(diagnostic);
        updated.Suggestion.Should().BeNull();
        updated.Help.Should().BeNull();
    }
}
