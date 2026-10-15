using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.TestHelpers;

/// <summary>
/// Binds and type-checks a Tyhp snippet (with optional extra tyhpdef text) and returns the
/// reported error codes / messages. Used by the `mixed` = TypeScript `unknown` strictness tests.
/// </summary>
internal static class MixedCheckSnippet
{
    public static IReadOnlyList<(MessageCode Code, string Message)> Errors(
        string tyhp,
        string? tyhpdef = null)
    {
        var result = IsolatedCompilation.ParseSnippet(tyhp, tyhpdef, skipChecking: true);
        result.GlobalScope.Should().NotBeNull("bind should succeed");
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new SymbolTree(result.GlobalScope!);
        new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!).Check(result.ParsedFiles!);
        return result.Diagnostics.Errors.Select(e => (e.Code, e.Message)).ToList();
    }

    public static string Format(IEnumerable<(MessageCode Code, string Message)> errors) =>
        string.Join(Environment.NewLine, errors.Select(e => $"TYHP{(int)e.Code}: {e.Message}"));
}
