using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A raw generic class (`Box` without type arguments) is `Box<mixed>`: it is not assignable to an
/// instantiation such as `Box<int>`, and every instantiation is assignable to it.
/// </summary>
public class RawGenericAssignabilityTests
{
    private const string Box = """
        class Box<T> {
            public function __construct(public T $value) {}
        }

        """;

    private static IReadOnlyList<MessageCode> Codes(string source) =>
        MixedCheckSnippet.Errors("<?tyhp\n" + Box + source).Select(e => e.Code).ToList();

    [Fact]
    public void RawGeneric_ToInstantiation_IsRejected() =>
        Codes("function f(Box $b): Box<int> { return $b; }")
            .Should().Contain(MessageCode.CheckerIncompatibleReturnType);

    [Fact]
    public void RawGeneric_ToInstantiationArgument_IsRejected() =>
        Codes("""
            function take(Box<int> $b): void {}
            function f(Box $b): void { take($b); }
            """)
            .Should().Contain(MessageCode.CheckerIncompatibleArgumentType);

    [Fact]
    public void RawGeneric_ToMixedInstantiation_IsAccepted() =>
        Codes("function f(Box $b): Box<mixed> { return $b; }").Should().BeEmpty();

    [Fact]
    public void RawGeneric_ToRawGeneric_IsAccepted() =>
        Codes("function f(Box $b): Box { return $b; }").Should().BeEmpty();

    [Fact]
    public void Instantiation_ToRawGeneric_IsAccepted() =>
        Codes("function f(Box<int> $b): Box { return $b; }").Should().BeEmpty();

    [Fact]
    public void InferredConstruction_ToInstantiation_IsAccepted() =>
        Codes("function f(): Box<int> { return new Box(5); }").Should().BeEmpty();

    [Fact]
    public void ContextualConstruction_ToInstantiation_IsAccepted() =>
        Codes("""
            class Slot<T> {
                public function __construct(public ?T $value = null) {}
            }
            function f(): Slot<int> { return new Slot(); }
            """)
            .Should().BeEmpty();

    [Fact]
    public void BoundedRawGeneric_ToInstantiationWithinBound_IsAccepted() =>
        Codes("""
            class Runner<TShape extends callable> {}
            function f(Runner $r): Runner<callable> { return $r; }
            """)
            .Should().BeEmpty();

    // ---- bounded raw generics: a raw `G` widens to its real bound, never to the target's argument ----

    private static IReadOnlyList<MessageCode> CodesWithDef(string tyhp, string tyhpdef) =>
        MixedCheckSnippet.Errors("<?tyhp\n" + tyhp, tyhpdef).Select(e => e.Code).ToList();

    [Fact]
    public void BoundedRaw_SourceClass_WidensToBound_NotToTargetArgument() =>
        Codes("""
            class Runner<TShape extends callable> {}
            function narrow(Runner $r): Runner<callable(): int> { return $r; }
            """)
            .Should().Contain(MessageCode.CheckerIncompatibleReturnType);

    [Fact]
    public void BoundedRaw_Fiber_WidensToDeclaredBound()
    {
        // `\Fiber` is the bundled PHP-stub class (`Fiber<TResume = mixed, TCallableShape extends callable>`):
        // the checker never walks it, so its bound only exists through the eager bound pass.
        Codes("function ok(\\Fiber $f): \\Fiber<mixed, callable> { return $f; }")
            .Should().BeEmpty();

        Codes("function narrow(\\Fiber $f): \\Fiber<mixed, callable(): int> { return $f; }")
            .Should().Contain(MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void BareClosure_StaysGradual_ToAnyClosureInstantiation() =>
        Codes("function f(\\Closure $c): \\Closure<callable(int): bool> { return $c; }")
            .Should().BeEmpty();

    [Fact]
    public void BoundedRaw_TyhpdefClassBound_IsResolvedWithoutAWalk()
    {
        const string def = """
            <?tyhpdef
            class Holder<T extends int|string> {}
            """;

        CodesWithDef("function ok(\\Holder $h): \\Holder<int|string> { return $h; }", def)
            .Should().BeEmpty();
        CodesWithDef("function narrow(\\Holder $h): \\Holder<int> { return $h; }", def)
            .Should().Contain(MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void BoundedRaw_TyhpdefBoundDeclaredLater_IsResolved() =>
        CodesWithDef(
                "function ok(\\Gate $g): \\Gate<\\Door> { return $g; }",
                """
                <?tyhpdef
                class Gate<T extends Door> {}
                class Door {}
                """)
            .Should().BeEmpty();

    [Fact]
    public void BoundedRaw_UseBeforeDeclaration_ResolvesTheBound() =>
        Codes("""
            function ok(Early $e): Early<Late> { return $e; }
            class Early<T extends Late> {}
            class Late {}
            """)
            .Should().BeEmpty();

    [Fact]
    public void BoundedRaw_SelfReferentialBound_TerminatesAndWidensToMixedArgument()
    {
        var codes = RunWithin(() => Codes("""
            class Node<T extends Node<T>> {}
            function narrow(Node $n): Node<Node<int>> { return $n; }
            """));
        codes.Should().Contain(MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void BoundedRaw_MutuallyRecursiveBounds_Terminate() =>
        RunWithin(() => Codes("""
            class Left<T extends Right<T>> {}
            class Right<U extends Left<U>> {}
            function f(Left $l): Left<mixed> { return $l; }
            """))
            .Should().NotBeNull();

    private static T RunWithin<T>(Func<T> action)
    {
        var task = Task.Run(action);
        task.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("bound resolution must not hang on cycles");
        return task.Result;
    }

    [Fact]
    public void RedundantInstanceofGuard_KeepsInstantiation() =>
        Codes("""
            function f(Box<int>|null $b): Box<int> {
                if ($b instanceof Box) {
                    return $b;
                }
                return new Box(1);
            }
            """)
            .Should().BeEmpty();

    [Fact]
    public void InstanceofGuardOnMixed_YieldsRawGeneric_NotAssignableToInstantiation() =>
        Codes("""
            function f(mixed $m): Box<int> {
                if ($m instanceof Box) {
                    return $m;
                }
                return new Box(1);
            }
            """)
            .Should().Contain(MessageCode.CheckerIncompatibleReturnType);

    [Fact]
    public void RawReceiver_MemberTypes_UseWidenedBound()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Pair<T, U> {
                public function __construct(public T $first, public U $second) {}
                public function getFirst(): T { return $this->first; }
            }
            class Bounded<T extends int|string> {
                public function __construct(public T $value) {}
                public function get(): T { return $this->value; }
            }
            function demo(): void {
                Pair $p = new Pair<int, string>(1, "a");
                bool $a = $p->first;
                bool $b = $p->getFirst();
                Bounded $r = new Bounded<int>(1);
                bool $c = $r->value;
                bool $d = $r->get();
            }
            """);

        var messages = string.Join("\n", errors.Select(e => e.Message));
        messages.Should().NotContain("`T`");
        messages.Should().Contain("`mixed`");
        messages.Should().Contain("`int|string`");
    }

    [Fact]
    public void ExplicitReceiver_MemberTypes_StayPrecise()
    {
        MixedCheckSnippet.Errors("""
            <?tyhp
            class Pair<T, U> {
                public function __construct(public T $first, public U $second) {}
                public function getFirst(): T { return $this->first; }
            }
            function demo(): void {
                Pair<int, string> $q = new Pair<int, string>(1, "a");
                int $n = $q->first;
                int $m = $q->getFirst();
            }
            """).Should().BeEmpty();
    }
}
