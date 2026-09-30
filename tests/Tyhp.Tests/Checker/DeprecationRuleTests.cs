using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.11 C.1 — <c>#[\Deprecated]</c> sets <see cref="BaseSymbol.IsDeprecated"/> and
/// <see cref="Tyhp.TyhpLang.Checker.Rules.DeprecationRule"/> reuses TYHP4500.
/// </summary>
[Trait("Category", "Checker")]
public class DeprecationRuleTests
{
    [Fact]
    public void DeprecatedAttribute_FunctionCall_Warns4500IncludingLiteralMessage()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            #[\Deprecated('use bar')]
            function foo(): void {}

            function demo(): void {
                foo();
            }
            """);

        var warnings = DeprecatedWarnings(result.Diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("foo");
        warnings[0].Message.Should().Contain("use bar");
        FunctionSymbol(result, "foo").IsDeprecated.Should().BeTrue();
        FunctionSymbol(result, "foo").DeprecatedMessage.Should().Be("use bar");
    }

    [Fact]
    public void DeprecatedAttribute_WithoutMessage_KeepsOneArgText()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            #[\Deprecated]
            function foo(): void {}

            function demo(): void {
                foo();
            }
            """);

        var warnings = DeprecatedWarnings(result.Diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Be("`foo` is deprecated");
        warnings[0].Message.Should().NotContain(":");
    }

    [Fact]
    public void DeprecatedAttribute_NamedMessage_IsIncluded()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            #[\Deprecated(message: "call bar instead", since: "8.4")]
            function foo(): void {}

            function demo(): void {
                foo();
            }
            """);

        var warnings = DeprecatedWarnings(result.Diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("call bar instead");
        warnings[0].Message.Should().NotContain("8.4");
    }

    [Fact]
    public void DeprecatedAttribute_MethodCall_Warns4500()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Box {
                #[\Deprecated('use newer')]
                public function old(): void {}
            }

            function demo(Box $b): void {
                $b->old();
            }
            """);

        var warnings = DeprecatedWarnings(result.Diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("old");
        warnings[0].Message.Should().Contain("use newer");
    }

    [Fact]
    public void DeprecatedAttribute_ClassName_Warns4500()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            #[\Deprecated('use NewThing')]
            class OldThing {}

            function demo(): OldThing {
                return new OldThing();
            }
            """);

        var warnings = DeprecatedWarnings(result.Diagnostics);
        warnings.Should().NotBeEmpty();
        warnings.Should().OnlyContain(d => d.Code == MessageCode.CheckerDeprecatedUsage);
        warnings.Should().Contain(d => d.Message.Contains("OldThing", StringComparison.Ordinal)
            && d.Message.Contains("use NewThing", StringComparison.Ordinal));
        ObjectSymbol(result, "OldThing").IsDeprecated.Should().BeTrue();
    }

    [Fact]
    public void DeprecatedAttribute_ClassConstant_Warns4500()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class Flags {
                #[\Deprecated('use NEW')]
                public const int OLD = 1;
            }

            function demo(): int {
                return Flags::OLD;
            }
            """);

        var warnings = DeprecatedWarnings(result.Diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("OLD");
        warnings[0].Message.Should().Contain("use NEW");
        ObjectSymbol(result, "Flags").Constants["OLD"].Should().BeAssignableTo<BaseSymbol>()
            .Which.IsDeprecated.Should().BeTrue();
    }

    [Fact]
    public void DeprecatedAttribute_TopLevelConst_Warns4500()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            #[\Deprecated('use BAR')]
            const FOO = 1;

            function demo(): int {
                return FOO;
            }
            """);

        var warnings = DeprecatedWarnings(result.Diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("FOO");
        warnings[0].Message.Should().Contain("use BAR");
        FindSymbol<ConstantSymbol>(result, "FOO")!.IsDeprecated.Should().BeTrue();
    }

    [Fact]
    public void TyhpdefDeprecatedKeyword_StillWarns4500()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {
                baz();
            }
            """,
            """
            <?tyhpdef
            deprecated function baz(): void;
            """);

        var warnings = DeprecatedWarnings(result.Diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Be("`baz` is deprecated");
        FunctionSymbol(result, "baz").IsDeprecated.Should().BeTrue();
        FunctionSymbol(result, "baz").DeprecatedMessage.Should().BeNull();
    }

    [Fact]
    public void DeprecatedAttributeAndTyhpdefKeyword_Together_OneWarningWithMessage()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(): void {
                baz();
            }
            """,
            """
            <?tyhpdef
            #[\Deprecated('use bar')]
            deprecated function baz(): void;
            """);

        var warnings = DeprecatedWarnings(result.Diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("baz");
        warnings[0].Message.Should().Contain("use bar");
        FunctionSymbol(result, "baz").IsDeprecated.Should().BeTrue();
    }

    [Fact]
    public void DeprecatedAttribute_WarnsWhenPhpVersionIsBelow84()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            #[\Deprecated('use bar')]
            function foo(): void {}

            function demo(): void {
                foo();
            }
            """,
            phpVersion: "8.2");

        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass);
        DeprecatedWarnings(result.Diagnostics).Should().ContainSingle();
    }

    [Fact]
    public void DeprecatedAttribute_InterpolatedMessage_TreatedAsNonLiteral()
    {
        // Story 21.11 C.1 regression: `"use $argv instead"` is an encaps list with a variable
        // part, not a string literal. Extraction must omit {1} (not silently drop the variable
        // and warn with a mangled truncated string) — same rule CheckerHelpers.IsConstantExpression
        // already applies (every PhpEncapsListAst item must be a PhpEncapsStringAst).
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            #[\Deprecated("use $argv instead")]
            function foo(): void {}

            function demo(): void {
                foo();
            }
            """);

        var warnings = DeprecatedWarnings(result.Diagnostics);
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Be("`foo` is deprecated");
        FunctionSymbol(result, "foo").DeprecatedMessage.Should().BeNullOrEmpty();
    }

    [Fact]
    public void UndeprecatedCall_DoesNotWarn4500()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            function foo(): void {}

            function demo(): void {
                foo();
            }
            """);

        DeprecatedWarnings(result.Diagnostics).Should().BeEmpty();
    }

    private static List<IDiagnostic> DeprecatedWarnings(DiagnosticBag diagnostics)
        => diagnostics.Warnings.Where(d => d.Code == MessageCode.CheckerDeprecatedUsage).ToList();

    private static FunctionDeclarationSymbol FunctionSymbol(CompilationResult result, string name)
    {
        var symbol = FindSymbol<FunctionDeclarationSymbol>(result, name);
        symbol.Should().NotBeNull($"function `{name}` should be bound");
        return symbol!;
    }

    private static ObjectDeclarationSymbol ObjectSymbol(CompilationResult result, string name)
    {
        var symbol = FindSymbol<ObjectDeclarationSymbol>(result, name);
        symbol.Should().NotBeNull($"type `{name}` should be bound");
        return symbol!;
    }

    private static T? FindSymbol<T>(CompilationResult result, string name)
        where T : class
    {
        T? found = null;
        void Walk(IBaseScope scope)
        {
            if (found != null)
            {
                return;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is T match
                    && symbol is BaseSymbol named
                    && string.Equals(named.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = match;
                    return;
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                Walk(child);
            }
        }

        Walk(result.GlobalScope!);
        return found;
    }
}
