using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// A member name after `->` or `::` is not a type, even when an imported class has the
/// same spelling. An inlined extension body is a clone, so its member names must stay
/// protected too.
/// </summary>
[Trait("Category", "Emitter")]
public class ClonedBodyMemberNameEmitterTests
{
    private const string Declarations = """
        <?tyhp
        namespace App\Models;

        class Column {}

        class Holder {
            public string $column = 'c';

            public static function column(): string {
                return 'static';
            }
        }
        """;

    private static string Emit(string callerBody)
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["models.tyhp"] = Declarations,
            ["caller.tyhp"] = """
                <?tyhp
                namespace App\Ext;

                use App\Models\Column;
                use App\Models\Holder;

                """ + callerBody,
        });

        result.Errors.Should().BeEmpty();
        return result.Php;
    }

    [Fact]
    public void ExtensionMethodBody_InstanceMemberMatchingClass_KeepsMemberAndQualifiesType()
    {
        var php = Emit("""
            extension HolderExt extends Holder {
                function label(): string {
                    return Column::class . $this->column;
                }
            }

            function show(Holder $h): string {
                return $h->label();
            }
            """);

        php.Should().Contain("\\App\\Models\\Column::class . $h->column");
        php.Should().NotContain("->\\App\\Models\\Column");
    }

    [Fact]
    public void ExtensionMethodBody_StaticMemberMatchingClass_KeepsMemberAndQualifiesType()
    {
        var php = Emit("""
            extension HolderExt extends Holder {
                function label(): string {
                    return Column::class . Holder::column();
                }
            }

            function show(Holder $h): string {
                return $h->label();
            }
            """);

        php.Should().Contain("\\App\\Models\\Column::class . \\App\\Models\\Holder::column()");
        php.Should().NotContain("::\\App\\Models\\Column");
    }

    [Fact]
    public void ExtensionOperatorBody_InstanceMemberMatchingClass_KeepsMemberAndQualifiesType()
    {
        var php = Emit("""
            extension HolderOps extends Holder {
                operator + (self $left, self $right): string => Column::class . $left->column . $right->column;
            }

            function sum(Holder $a, Holder $b): string {
                return $a + $b;
            }
            """);

        php.Should().Contain("\\App\\Models\\Column::class . $a->column) . $b->column");
        php.Should().NotContain("->\\App\\Models\\Column");
    }
}
