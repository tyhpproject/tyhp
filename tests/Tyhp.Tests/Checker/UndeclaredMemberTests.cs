using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A member that exists nowhere on a fully known class / interface / <c>object</c> receiver is
/// reported (TYHP4101) unless a magic accessor can serve it.
/// </summary>
public class UndeclaredMemberTests
{
    private const string Declarations = """
        <?tyhp
        class Foo {
            public int $a = 1;
            public function m(): void {}
            public static function s(): void {}
        }
        class Magic {
            public function __get(string $n): mixed { return 1; }
            public function __call(string $n, array $a): mixed { return 1; }
            public static function __callStatic(string $n, array $a): mixed { return 1; }
        }
        class Child extends Foo {}
        interface I { public function im(): void; }
        function method_exists(object|string $objectOrClass, string $method): bool { return true; }
        """;

    private static IReadOnlyList<(MessageCode Code, string Message)> Check(string body) =>
        MixedCheckSnippet.Errors(Declarations + "\n" + body);

    [Fact]
    public void DeclaredMembers_AreAccepted()
    {
        var errors = Check("""
            function t(Foo $f, Child $c, I $i): void {
                int $a = $f->a;
                int $b = $c->a;
                $f->m();
                $c->m();
                Foo::s();
                Child::s();
                $i->im();
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void UndeclaredProperty_IsReported()
    {
        var errors = Check("function t(Foo $f): void { mixed $x = $f->nope; }");

        var error = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerSymbolNameNotFound, error.Code);
    }

    [Fact]
    public void UndeclaredInstanceMethod_IsReported()
    {
        var errors = Check("function t(Foo $f, I $i): void { $f->nope(); $i->nope(); }");

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(MessageCode.CheckerSymbolNameNotFound, error.Code));
    }

    [Fact]
    public void UndeclaredStaticMethod_IsReported()
    {
        var errors = Check("function t(): void { Foo::nope(); }");

        var error = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerSymbolNameNotFound, error.Code);
    }

    [Fact]
    public void MemberOnBareObject_IsReported()
    {
        var errors = Check("function t(object $o): void { mixed $x = $o->anything; }");

        var error = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerSymbolNameNotFound, error.Code);
    }

    [Fact]
    public void MagicAccessors_ServeUndeclaredMembers()
    {
        var errors = Check("""
            function t(Magic $m): void {
                mixed $x = $m->anything;
                $m->anyMethod('name', []);
                Magic::anyStatic('name', []);
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void FileLocalHiddenExtensionMethod_IsReportedOnClassReceiver()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Money {}
            extension MoneyFormatting extends Money {
                function extra(): string { return 'hidden'; }
            }
            use extension MoneyFormatting {
                MoneyFormatting::extra hide;
            }
            function hidden(Money $m): string {
                return $m->extra();
            }
            """);

        var error = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerSymbolNameNotFound, error.Code);
    }

    [Fact]
    public void ExtensionMethod_OnClassReceiver_IsAccepted()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Money {}
            extension MoneyFormatting extends Money {
                function extra(): string { return 'ok'; }
            }
            use extension MoneyFormatting;
            function shown(Money $m): string {
                return $m->extra();
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void PropertyExistsGuard_ProvesPropertyOnObject()
    {
        var errors = Check("""
            function t(object $o): mixed {
                if (\property_exists($o, 'tag')) {
                    return $o->tag;
                }
                return null;
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void MethodExistsGuard_ProvesMethodOnNarrowedMixed()
    {
        var errors = Check("""
            function t(mixed $bag): mixed {
                if (!\is_object($bag) || !\method_exists($bag, 'objectType')) {
                    return null;
                }
                return $bag->objectType();
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void ExistsGuard_DoesNotProveOtherMembersOrOutsideTheBranch()
    {
        var errors = Check("""
            function t(object $o): void {
                if (\method_exists($o, 'known')) {
                    $o->known();
                    $o->other();
                }
                $o->known();
            }
            """);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(MessageCode.CheckerSymbolNameNotFound, error.Code));
    }

    [Fact]
    public void ExistsGuard_IsDroppedWhenReceiverIsReassigned()
    {
        var errors = Check("""
            function t(object $o, object $p): void {
                if (\method_exists($o, 'known')) {
                    $o = $p;
                    $o->known();
                }
            }
            """);

        var error = Assert.Single(errors);
        Assert.Equal(MessageCode.CheckerSymbolNameNotFound, error.Code);
    }

    [Fact]
    public void DynamicMemberNames_AreNotJudged()
    {
        var errors = Check("""
            function t(object $o, Foo $f, string $name): void {
                mixed $a = $o->{$name};
                mixed $b = $f->$name;
                $f->$name();
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void EngineStdClass_KeepsDynamicProperties()
    {
        var errors = Check("function t(\\stdClass $s): void { mixed $x = $s->anything; }");

        Assert.Empty(errors);
    }
}
