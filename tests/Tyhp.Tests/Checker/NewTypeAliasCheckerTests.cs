using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story27")]
public class NewTypeAliasCheckerTests
{
    [Fact]
    public void Check_NewIntTypeAlias_Reports4069()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type Foo = int;
            function demo(): void {
                mixed $x = new Foo();
            }
            """);

        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerCannotInstantiateNonClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_NewClassTypeAlias_Reports4069()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class SomeClass {}
            type Foo = SomeClass;
            function demo(): void {
                mixed $x = new Foo();
            }
            """);

        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerCannotInstantiateNonClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_NewObjectShapeAlias_Reports4347()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type Shape = object {
                public function x(): void;
            };
            function demo(): void {
                mixed $x = new Shape();
            }
            """);

        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeUsedAsClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_NewRenamedObjectShapeAlias_Reports4347()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type Box = object {
                public function x(): void;
            };
            type Foo = Box;
            function demo(): void {
                mixed $x = new Foo();
            }
            """);

        // A rename of a shape alias is still shape-backed — must stay 4347, not fall back to
        // the generic non-instantiable-alias 4069.
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeUsedAsClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCannotInstantiateNonClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_NewGenericWrappedObjectShapeAlias_Reports4347()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type Box<T> = object {
                public function x(): T;
            };
            type Foo = Box<int>;
            function demo(): void {
                mixed $x = new Foo();
            }
            """);

        // A generic instantiation of a shape alias is still shape-backed — must stay 4347, not
        // fall back to the generic non-instantiable-alias 4069.
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeUsedAsClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCannotInstantiateNonClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_NewRealClass_DoesNotReportAliasOrShapeDiagnostics()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class SomeClass {}
            function demo(): void {
                mixed $x = new SomeClass();
            }
            """);

        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerCannotInstantiateNonClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerObjectShapeUsedAsClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderSymbolNotFound);
    }
}
