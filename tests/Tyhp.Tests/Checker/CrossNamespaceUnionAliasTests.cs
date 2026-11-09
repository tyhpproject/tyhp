using FluentAssertions;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Xunit;

namespace Tyhp.Tests.Checker;

/// <summary>
/// A union alias is expanded in the namespace that declared it. A use site in another
/// namespace still sees the shared fields, and a discriminant narrows to the matching arm.
/// </summary>
[Trait("Category", "Checker")]
public class CrossNamespaceUnionAliasTests
{
    [Fact]
    public void UnionAlias_UsedFromAnotherNamespace_ExposesSharedFieldsAndNarrows()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            namespace Types {
                type Left = struct {
                    true $ok;
                    string $message;
                };
                type Right = struct {
                    false $ok;
                    int $code;
                };
                type Either = Left|Right;
            }
            namespace App {
                function flag(\Types\Either $value): bool {
                    return $value->ok;
                }
                function message(\Types\Either $value): string {
                    if ($value->ok === true) {
                        return $value->message;
                    }
                    return '';
                }
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void UnionAlias_SharedFieldWithDifferentElementTypes_NarrowsInForeach()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            namespace Types {
                type Named = struct {
                    'name' $form;
                    array<int, string> $args;
                };
                type Shaped = struct {
                    'struct'|'object' $form;
                    array<int, int> $args;
                };
                type Node = Named|Shaped;
            }
            namespace App {
                function read(\Types\Node $node): int {
                    if ($node->form === 'name') {
                        int $count = 0;
                        foreach ($node->args as $arg) {
                            $count += \strlen($arg);
                        }
                        return $count;
                    }
                    int $sum = 0;
                    foreach ($node->args as $arg) {
                        $sum += $arg;
                    }
                    return $sum;
                }
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void TyhpdefUnionAlias_UsedFromAnotherNamespace_ExposesSharedFields()
    {
        var errors = MixedCheckSnippet.Errors(
            """
            <?tyhp
            namespace App {
                function flag(\Types\Either $value): bool {
                    return $value->ok;
                }
            }
            """,
            """
            <?tyhpdef
            namespace Types {
                type Left = struct {
                    true $ok;
                    string $message;
                };
                type Right = struct {
                    false $ok;
                    int $code;
                };
                type Either = Left|Right;
            }
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.CheckerUnresolvedReceiver);
        errors.Should().NotContain(e => e.Code == MessageCode.CheckerSymbolNameNotFound);
    }
}
