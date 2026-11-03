using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class UnionExtensionNamespaceTests
{
    [Fact]
    public void UnionTarget_GlobalCaller_Resolves()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            extension Labels extends string|int {
                fn label(): string => 'label';
            }

            function f(string|int $v): string {
                return $v->label();
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void UnionTarget_CallerInFileNamespace_Resolves()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            namespace App;

            extension Labels extends string|int {
                fn label(): string => 'label';
            }

            function f(string|int $v): string {
                return $v->label();
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void UnionTarget_CallerInBracedNamespace_Resolves()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            namespace App {
                extension Labels extends string|int {
                    fn label(): string => 'label';
                }

                function f(string|int $v): string {
                    return $v->label();
                }
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void UnionTarget_OfClasses_CallerInNamespace_Resolves()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            namespace App {
                class Cat {}
                class Dog {}

                extension Pets extends Cat|Dog {
                    fn sound(): string => 'noise';
                }

                function f(Cat|Dog $pet): string {
                    return $pet->sound();
                }

                function g(Cat $cat): string {
                    return $cat->sound();
                }
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Theory]
    [InlineData("namespace App;\n", "")]
    [InlineData("namespace App {\n", "}\n")]
    [InlineData("", "")]
    public void UnionTarget_SecondMemberReceiver_Resolves(string open, string close)
    {
        var errors = MixedCheckSnippet.Errors($$"""
            <?tyhp
            {{open}}
            class Cat {}
            class Dog {}

            extension Pets extends Cat|Dog {
                fn sound(): string => 'noise';
            }

            extension Labels extends string|int {
                fn label(): string => 'label';
            }

            function f(Dog $dog, int $n): string {
                return $dog->sound() . $n->label();
            }

            $dog = new Dog();
            echo $dog->sound();
            int $count = 5;
            echo $count->label();
            {{close}}
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Theory]
    [InlineData("namespace App;\n", "")]
    [InlineData("namespace App {\n", "}\n")]
    public void UnionTarget_TopLevelUnionReceiverInNamespace_Resolves(string open, string close)
    {
        var errors = MixedCheckSnippet.Errors($$"""
            <?tyhp
            {{open}}
            class Cat {}
            class Dog {}

            extension Pets extends Cat|Dog {
                fn sound(): string => 'noise';
            }

            function pick(): Cat|Dog {
                return new Dog();
            }

            $pet = pick();
            echo $pet->sound();
            {{close}}
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void UnionTarget_DeclaredGlobally_CallerInNamespace_Resolves()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            namespace {
                extension Labels extends string|int {
                    fn label(): string => 'label';
                }
            }

            namespace App {
                function f(string|int $v): string {
                    return $v->label();
                }
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }
}
