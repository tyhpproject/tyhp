using Tyhp.Domain.Diagnostics;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Binder machinery for Layer 3 <c>_unsafe</c> aliases. Live call-site contracts
/// are covered by <c>runtime/packages/test-all-tyhpdef.sh</c>.
/// </summary>
[Trait("Category", "Checker")]
public class Story218Phase3bExtStandardCallablesOverlayTests
{
    [Fact]
    public void Bind_ForwardStaticCallUnsafe_AliasRemains()
    {
        var (globalScope, diagnostics) = BindOnly("""
            <?tyhp
            function demo(): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        globalScope.Should().NotBeNull();

        var resolver = new NameResolver(new SymbolTree(globalScope!), new DiagnosticBag());
        var resolved = resolver.ResolveRelativeName(["forward_static_call_unsafe"], globalScope!);

        resolved.Should().BeOfType<FunctionDeclarationSymbol>();
        var fn = (FunctionDeclarationSymbol)resolved!;
        fn.Name.Should().Be("forward_static_call_unsafe");
        fn.OriginalPhpName.Should().Be("forward_static_call");
    }

    [Fact]
    public void Bind_ForwardStaticCallArrayUnsafe_AliasRemains()
    {
        var (globalScope, diagnostics) = BindOnly("""
            <?tyhp
            function demo(): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        globalScope.Should().NotBeNull();

        var resolver = new NameResolver(new SymbolTree(globalScope!), new DiagnosticBag());
        var resolved = resolver.ResolveRelativeName(["forward_static_call_array_unsafe"], globalScope!);

        resolved.Should().BeOfType<FunctionDeclarationSymbol>();
        var fn = (FunctionDeclarationSymbol)resolved!;
        fn.Name.Should().Be("forward_static_call_array_unsafe");
        fn.OriginalPhpName.Should().Be("forward_static_call_array");
        fn.Parameters.Should().HaveCount(2);
    }

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> UserErrors(DiagnosticBag diagnostics) =>
        diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static (GlobalScope? Scope, DiagnosticBag Diagnostics) BindOnly(string content)
    {
        var result = IsolatedCompilation.ParseSnippet(
            content,
            SyntheticPhpStubs.ForwardStaticCallAliases,
            skipChecking: true);
        return (result.GlobalScope, result.Diagnostics);
    }
}
