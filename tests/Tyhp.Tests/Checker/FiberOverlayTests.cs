using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Checker internals for Fiber overlay typing (inferred display names).
/// Live <c>\Fiber</c> call-site contracts are covered by
/// <c>runtime/packages/test-all-tyhpdef.sh</c>.
/// </summary>
[Trait("Category", "Checker")]
public class FiberOverlayTests
{
    [Fact]
    public void GetCurrent_IsTwoArgFiberMixedCallable()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(): void {
                $current = \Fiber::getCurrent();
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));

        var call = FindStaticCall(file, "getCurrent");
        call.Should().NotBeNull("Fiber::getCurrent() call should parse");
        var type = checker.ResolveExpressionType(call!, new CheckerState { CurrentFileName = file.FileName });
        var display = type.DisplayName;

        display.Should().Contain("Fiber", display);
        display.Should().Contain("mixed", display);
        display.Should().Contain("callable", display);
        display.Should().MatchRegex(@"\?|null", "getCurrent is nullable");

        CountTypeArguments(display).Should().Be(2,
            $"getCurrent must be two type args, not three: {display}");
        display.Should().NotContain("callable(): void",
            $"must not default TCallableShape to callable(): void only: {display}");
        display.Should().NotContain("callable(): void", display);
        display.Should().NotContain("TSuspend", display);
        display.Should().NotContain("TStart", display);
    }

    [Fact]
    public void GetReturn_DoesNotInventNull()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(\Fiber<mixed, callable(): string> $fiber): void {
                $result = $fiber->getReturn();
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var call = FindInstanceCall(file, "getReturn");
        call.Should().NotBeNull();
        var type = checker.ResolveExpressionType(call!, new CheckerState { CurrentFileName = file.FileName });
        type.DisplayName.Should().Be("string", type.DisplayName);
        type.IsNullable.Should().BeFalse("getReturn does not add null; it throws at runtime if unfinished");
    }

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> UserErrors(DiagnosticBag diagnostics) =>
        diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static (TyhpChecker checker, SrcFileAst file, GlobalScope global, DiagnosticBag diagnostics) CompileForChecker(
        string content)
    {
        var result = IsolatedCompilation.ParseSnippet(
            content,
            SyntheticPhpStubs.Fiber,
            phpVersion: "8.2",
            skipChecking: true,
            includeMinimalPhpStubs: false);
        result.GlobalScope.Should().NotBeNull("bind should succeed");
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);
        return (checker, result.ParsedFiles![0], result.GlobalScope!, result.Diagnostics);
    }

    private static PhpDereferenceableAst? FindStaticCall(IBase2Ast root, string methodName)
    {
        foreach (var dref in FindAllAst<PhpDereferenceableAst>(root))
        {
            if (dref.Suffix is not PhpCallAst)
            {
                continue;
            }

            if (dref.Base is PhpDereferenceableAst inner
                && MemberName(inner.Suffix switch
                {
                    PhpStaticMemberAccessAst staticAccess => staticAccess.Member,
                    PhpClassConstantAccessAst classConst => classConst.Member,
                    _ => null,
                }) == methodName)
            {
                return dref;
            }
        }

        return null;
    }

    private static PhpDereferenceableAst? FindInstanceCall(IBase2Ast root, string methodName)
    {
        foreach (var dref in FindAllAst<PhpDereferenceableAst>(root))
        {
            if (dref.Suffix is not PhpCallAst)
            {
                continue;
            }

            if (dref.Base is PhpDereferenceableAst inner
                && inner.Suffix is PhpInstanceMemberAccessAst instance
                && MemberName(instance.MemberName) == methodName)
            {
                return dref;
            }
        }

        return null;
    }

    private static string? MemberName(IBase2Ast? node) =>
        node?.ValueString ?? node?.Identifier;

    private static int CountTypeArguments(string display)
    {
        var open = display.IndexOf('<');
        var close = display.LastIndexOf('>');
        if (open < 0 || close <= open)
        {
            return 0;
        }

        var inner = display[(open + 1)..close];
        var depth = 0;
        var count = 1;
        foreach (var ch in inner)
        {
            switch (ch)
            {
                case '<':
                case '(':
                    depth++;
                    break;
                case '>':
                case ')':
                    depth--;
                    break;
                case ',' when depth == 0:
                    count++;
                    break;
            }
        }

        return count;
    }

    private static IEnumerable<T> FindAllAst<T>(IBase2Ast root) where T : class, IBase2Ast
    {
        if (root is T match)
        {
            yield return match;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var found in FindAllAst<T>(child))
            {
                yield return found;
            }
        }
    }
}
