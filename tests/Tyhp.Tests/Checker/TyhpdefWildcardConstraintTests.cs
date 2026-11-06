using FluentAssertions;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// PHPDoc <c>Builder&lt;*&gt;</c> is emitted as <c>Builder&lt;mixed&gt;</c>. In a tyhpdef that
/// argument satisfies <c>TModel extends Model</c>, and <c>Builder&lt;Post&gt;</c> can be passed
/// where <c>Builder&lt;mixed&gt;</c> is expected. A <c>.tyhp</c> file that writes
/// <c>Builder&lt;mixed&gt;</c> still fails the bound.
/// </summary>
[Trait("Category", "Checker")]
public class TyhpdefWildcardConstraintTests
{
    [Fact]
    public void TyhpdefMixedArgument_SatisfiesClassBound_AndAcceptsASpecificInstantiation()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function demo(\Demo\Query $query, \Demo\Builder<\Demo\Post> $posts): \Demo\Query {
                $query = $query->orderBy('id');
                $query = $query->take(1);
                return $query->orderBy($posts);
            }
            """,
            """
            <?tyhpdef
            namespace Demo;
            class Model {}
            class Post extends Model {}
            class Builder<TModel extends \Demo\Model> {}
            class Box<T extends int> {}
            class Query {
                public function orderBy(\Demo\Builder<mixed>|string $column): static;
                public function take(\Demo\Box<mixed>|int $value): static;
            }
            """);

        var errors = result.Diagnostics.Errors;
        errors.Should().NotContain(e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void TyhpFileMixedArgument_StillMustSatisfyTheBound()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function bad(\Demo\Builder<mixed> $builder, \Demo\Box<mixed> $box): \Demo\Box<mixed> {
                return $box;
            }
            """,
            """
            <?tyhpdef
            namespace Demo;
            class Model {}
            class Builder<TModel extends \Demo\Model> {}
            class Box<T extends int> {}
            """);

        result.Diagnostics.Errors.Should().Contain(e => e.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }
}
