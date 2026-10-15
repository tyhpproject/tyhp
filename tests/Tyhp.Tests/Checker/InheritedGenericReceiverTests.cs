using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Members inherited from a generic base bind the base's type parameters through the child's
/// <c>extends</c> clause for <c>$this</c>, <c>parent::</c> and generic method overrides — also when
/// the child re-uses the base's parameter names (distinct symbols, never identical by name).
/// </summary>
public class InheritedGenericReceiverTests
{
    [Fact]
    public void ThisReceiver_BindsInheritedParameterThroughExtendsClause()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Base<T> { public function put(T $v): void {} }
            class Child<U> extends Base<U> { public function go(U $u): void { $this->put($u); } }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void ParentReceiver_BindsInheritedParameterThroughExtendsClause()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Base<T> { public function put(T $v): void {} public function __construct(T $v) {} }
            class Child<U> extends Base<U> {
                public function __construct(U $u) { parent::__construct($u); }
                public function go(U $u): void { parent::put($u); }
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void ParentReceiver_SameParameterNameAsBase_Binds()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Base<T> { public function put(T $v): void {} public function __construct(T $v) {} }
            class Child<T> extends Base<T> {
                public function __construct(T $u) { parent::__construct($u); }
                public function go(T $u): void { parent::put($u); }
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void ParentReceiver_ClosureOfInheritedShape_Binds()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Base<TShape extends callable> { public function __construct(\Closure<TShape> $c) {} }
            class Child<TShape extends callable> extends Base<TShape> {
                public function __construct(\Closure<TShape> $c) { parent::__construct($c); }
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public void ParentReceiver_ConcreteExtendsArgument_RejectsWrongType()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Base<T> { public function put(T $v): void {} }
            class Child extends Base<int> {
                public function go(): void { parent::put(1); parent::put('x'); $this->put('y'); }
            }
            """);

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(MessageCode.CheckerIncompatibleArgumentType, error.Code));
    }

    [Fact]
    public void GenericMethodOverride_SameParameterName_IsCompatible()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Base {
                public static function of<T extends object>(__ClassName<T> $c): Base { return new Base(); }
            }
            class Child extends Base {
                public static function of<T extends object>(__ClassName<T> $c): Child { return new Child(); }
            }
            """);

        Assert.DoesNotContain(errors, error => error.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }

    [Fact]
    public void GenericMethodOverride_MethodParameterShadowsClassParameter_IsCompatible()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            class Builder<T> {}
            class Base<S> {
                public static function of<T extends object>(__ClassName<T> $c, string|array ...$s): Builder<T> { return new Builder<T>(); }
            }
            class Child<T extends object> extends Base<callable(T): mixed> {
                public static function of<T extends object>(__ClassName<T> $c, string|array ...$s): Builder<T> { return new Builder<T>(); }
            }
            """);

        Assert.DoesNotContain(errors, error => error.Code == MessageCode.CheckerOverloadSignatureIncompatible);
    }
}
