using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Microsoft.Extensions.Localization;

namespace Tyhp.Tests.Diagnostics;

[Trait("Category", "Diagnostics")]
public class DiagnosticFormattingTests : IDisposable
{
    private sealed class FakeLocalizer : IStringLocalizer<TyhpHostedService>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => Enumerable.Empty<LocalizedString>();
        public IStringLocalizer WithCulture(System.Globalization.CultureInfo culture) => this;
    }

    // Both tests here assert on the resource-key plumbing rather than the catalog text, so the stub
    // is installed for the whole class — and reset afterwards, because Message.SetLocalizer is
    // process-wide state that would otherwise blank out the catalog for every later test.
    public DiagnosticFormattingTests()
    {
        Message.SetLocalizer(new FakeLocalizer());
    }

    public void Dispose() => Message.ResetLocalizer();

    [Fact]
    public void Display_DoesNotThrowForFormattedDiagnostic()
    {
        var diagnostic = Diagnostic.Error(MessageCode.ParserUnexpectedError, "test.tyhp", 1, 4, new object[] { ";" });
        var action = () => diagnostic.Display();
        action.Should().NotThrow();
    }

    [Fact]
    public void LocalizeErrorCode_UsesExpectedKeyFormat()
    {
        Message.LocalizeErrorCode((int)MessageCode.ParserUnknownError).Should().Be("ERROR_TYHP1001");
    }

    [Fact]
    public void LocalizeErrorCode_CheckerNotAnAttributeClass_FormatsNameArg()
    {
        // Real catalog (not FakeLocalizer): message template contains `{0}`.
        Message.ResetLocalizer();
        try
        {
            Message.LocalizeErrorCode((int)MessageCode.CheckerNotAnAttributeClass, "Inline")
                .Should().Be("Class `Inline` is not declared as an attribute class");
        }
        finally
        {
            Message.SetLocalizer(new FakeLocalizer());
        }
    }
}
