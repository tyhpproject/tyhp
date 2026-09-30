using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 21.9 Decision 4: a type-alias factory call (<c>UserId()</c>, <c>Optional(...)</c>) takes
/// <c>\Tyhp\Type</c> value arguments only. Type arguments on the call (<c>Optional&lt;int&gt;()</c>)
/// are <see cref="MessageCode.CheckerTypeAliasFactoryTypeArguments"/> (TYHP4186) — but only for
/// alias factories. An ordinary generic function/method called with explicit type arguments
/// (<c>Holder::zero&lt;int&gt;()</c>, Mechanism D) must never trip this diagnostic just because a
/// same-named type alias exists elsewhere.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Tyhp")]
public class TypeAliasFactoryCallCheckerTests
{
    [Fact]
    public void Check_GenericAliasFactoryCallWithTypeArguments_Reports4186()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Optional<T = mixed> = T|null;

            function f(): void
            {
                $t = Optional<int>();
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeAliasFactoryTypeArguments);
    }

    [Fact]
    public void Check_ClassLevelAliasFactoryCallWithTypeArguments_Reports4186()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class UserService {
                public type Row<T = mixed> = array<string, T>;
            }

            function f(): void
            {
                $t = UserService::Row<int>();
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeAliasFactoryTypeArguments);
    }

    [Fact]
    public void Check_TypeofGenericAlias_DoesNotReport4186()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Optional<T = mixed> = T|null;

            function f(): void
            {
                $t = typeof(Optional<int>);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeAliasFactoryTypeArguments);
    }

    [Fact]
    public void Check_AliasFactoryCallWithTypeValueArgument_DoesNotReport4186()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Optional<T = mixed> = T|null;

            function f(): void
            {
                $t = Optional(typeof(int));
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeAliasFactoryTypeArguments);
    }

    /// <summary>
    /// An ordinary generic static method named like a completely unrelated type alias must
    /// resolve as the method (Mechanism D explicit type argument), never as an alias-factory
    /// call — even though the alias-factory-call lookup path runs on every callee name.
    /// </summary>
    [Fact]
    public void Check_OrdinaryGenericStaticMethodCall_DoesNotReport4186()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder {
                public static function zero<T>(): T {
                    return default(T);
                }
            }

            function f(): void
            {
                Holder::zero<int>();
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeAliasFactoryTypeArguments);
    }

    /// <summary>
    /// Regression for the alias-vs-method bug: a class with an ordinary (non-alias) static
    /// method that happens to share a name with a builtin scalar type (<c>Type::bool()</c>)
    /// is not an alias factory, so
    /// calling it plainly must never report TYHP4186 — it isn't a factory call at all.
    /// </summary>
    [Fact]
    public void Check_OrdinaryStaticMethodNamedLikeBuiltin_IsNotAliasFactory_DoesNotReport4186()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;
            class TypeLike {
                public static fn bool(): self => new self();

                public function t(): void
                {
                    self::bool();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerTypeAliasFactoryTypeArguments);
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var result = IsolatedCompilation.ParseSnippet(content, skipChecking: true);
        result.GlobalScope.Should().NotBeNull("bind should succeed");
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new Tyhp.TyhpLang.Binder.SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);
        return result.Diagnostics;
    }
}
