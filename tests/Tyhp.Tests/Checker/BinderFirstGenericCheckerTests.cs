using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A binder replaces the method PHP dispatches on, so magic methods and methods that
/// override a non-generic signature cannot declare type parameters.
/// </summary>
[Trait("Category", "Checker")]
public class BinderFirstGenericCheckerTests
{
    [Theory]
    [InlineData("__construct<T>(T $value)")]
    [InlineData("__get<T>(string $name): mixed")]
    [InlineData("__invoke<T>(): void")]
    public void MagicMethod_WithTypeParameters_ReportsTyhp4404(string signature)
    {
        var result = IsolatedCompilation.ParseSnippet(
            $$"""
            <?tyhp
            class Box {
                public function {{signature}} {
                    return null;
                }
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().Contain(MessageCode.CheckerGenericOnMagicMethod);
    }

    [Fact]
    public void GenericJsonSerialize_ReportsTyhp4405()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Box implements \JsonSerializable {
                public function jsonSerialize<T>(): mixed {
                    return null;
                }
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().Contain(MessageCode.CheckerGenericOverridesNonGenericMethod);
    }

    [Fact]
    public void GenericOverrideOfNonGenericParent_ReportsTyhp4405()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Base {
                public function name(): string {
                    return "base";
                }
            }

            class Child extends Base {
                public function name<T>(): string {
                    return "child";
                }
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().Contain(MessageCode.CheckerGenericOverridesNonGenericMethod);
    }

    [Fact]
    public void GenericImplementationOfNonGenericInterfaceMethod_ReportsTyhp4405()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            interface Named {
                public function name(): string;
            }

            class Holder implements Named {
                public function name<T>(): string {
                    return "holder";
                }
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().Contain(MessageCode.CheckerGenericOverridesNonGenericMethod);
    }

    [Fact]
    public void GenericImplementationOfGenericInterfaceMethod_IsAllowed()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            interface Named {
                public function name<T>(): string;
            }

            class Holder implements Named {
                public function name<T>(): string {
                    return "holder";
                }
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().NotContain(MessageCode.CheckerGenericOverridesNonGenericMethod);
    }

    [Fact]
    public void GenericOverrideOfGenericParent_IsAllowed()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Base {
                public function name<T>(): string {
                    return "base";
                }
            }

            class Child extends Base {
                public function name<T>(): string {
                    return "child";
                }
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().NotContain(MessageCode.CheckerGenericOverridesNonGenericMethod);
        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().NotContain(MessageCode.CheckerGenericOnMagicMethod);
    }

    [Fact]
    public void InferGeneric_TypeParameterOnlyInReturn_ReportsTyhp4407()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            #[\Tyhp\InferGeneric]
            function only<OnlyReturn>(): mixed {
                return default(OnlyReturn);
            }
            """,
            phpVersion: "8.4");

        var diagnostic = result.Diagnostics.Errors
            .Single(d => d.Code == MessageCode.CheckerInferGenericNotInferable);
        diagnostic.Message.Should().Contain("OnlyReturn");
    }

    [Theory]
    [InlineData("T $value")]
    [InlineData("?T $value")]
    [InlineData("callable(): T $factory")]
    [InlineData("__ClassName<T> $type")]
    [InlineData("Promise<T> $promise")]
    [InlineData("?Promise<T> $promise")]
    [InlineData("array<Promise<T>> $promises")]
    [InlineData("Promise<T> ...$promises")]
    [InlineData("null|T|Promise<T> $value = null")]
    [InlineData("T|Promise<T> $value")]
    public void InferGeneric_TypeParameterReachableFromAnArgument_DoesNotReportTyhp4407(string parameter)
    {
        var result = IsolatedCompilation.ParseSnippet(
            $$"""
            <?tyhp
            class Promise<T> {
                public function __construct(public T $value) {}
            }

            class Holder {
                #[\Tyhp\InferGeneric]
                public static function pick<T>({{parameter}}): string {
                    return '';
                }
            }

            #[\Tyhp\InferGeneric]
            function pickFree<T>({{parameter}}): string {
                return '';
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().NotContain(MessageCode.CheckerInferGenericNotInferable);
    }

    [Theory]
    [InlineData("null|string|Promise<T> $value = null")]
    [InlineData("null|T|array<Promise<T>> $value = null")]
    [InlineData("null|T|Promise<int> $value = null")]
    public void InferGeneric_UnionWithAnUnrelatedArm_ReportsTyhp4407(string parameter)
    {
        var result = IsolatedCompilation.ParseSnippet(
            $$"""
            <?tyhp
            class Promise<T> {
                public function __construct(public T $value) {}
            }

            #[\Tyhp\InferGeneric]
            function pickFree<T>({{parameter}}): string {
                return '';
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().Contain(MessageCode.CheckerInferGenericNotInferable);
    }

    [Fact]
    public void InferGeneric_ArrayKeyAndArrayItemAreNotSources_ReportTyhp4407()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Promise<T> {
                public function __construct(public T $value) {}
            }

            #[\Tyhp\InferGeneric]
            function settled<T, TKey extends int|string = int|string>(array<TKey, Promise<T>> $promises): string {
                return '';
            }

            #[\Tyhp\InferGeneric]
            function batch<TItem, TBatchReturn>(array<TItem> $items, callable(TItem): Promise<TBatchReturn> $processor): string {
                return '';
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors
            .Where(d => d.Code == MessageCode.CheckerInferGenericNotInferable)
            .Select(d => d.Message)
            .Should().HaveCount(3)
            .And.Contain(message => message.Contains("TKey"))
            .And.Contain(message => message.Contains("TItem"))
            .And.Contain(message => message.Contains("TBatchReturn"));
    }

    [Fact]
    public void InferGeneric_TypeParameterOnlyInAByRefParameter_ReportsTyhp4407()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Holder {
                #[\Tyhp\InferGeneric]
                public static function tryTake<T>(string $json, T &$out): bool {
                    return false;
                }
            }
            """,
            phpVersion: "8.4");

        var diagnostic = result.Diagnostics.Errors
            .Single(d => d.Code == MessageCode.CheckerInferGenericNotInferable);
        diagnostic.Message.Should().Contain("T");
    }

    [Fact]
    public void InferGeneric_OnNonGenericFunction_WarnsAndNeverErrors()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            #[\Tyhp\InferGeneric]
            function plain(int $value): int {
                return $value;
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().NotContain(MessageCode.CheckerInferGenericOnNonGeneric)
            .And.NotContain(MessageCode.CheckerInferGenericNotInferable);
        result.Diagnostics.Warnings.Select(d => d.Code)
            .Should().ContainSingle(code => code == MessageCode.CheckerInferGenericOnNonGeneric);
    }

    [Fact]
    public void InferGeneric_OnNonGenericMethod_WarnsTyhp4408()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Box {
                #[\Tyhp\InferGeneric]
                public function plain(int $value): int {
                    return $value;
                }
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().NotContain(MessageCode.CheckerInferGenericNotInferable);
        var diagnostic = result.Diagnostics.Warnings
            .Single(d => d.Code == MessageCode.CheckerInferGenericOnNonGeneric);
        diagnostic.Message.Should().Contain("plain");
    }
}
