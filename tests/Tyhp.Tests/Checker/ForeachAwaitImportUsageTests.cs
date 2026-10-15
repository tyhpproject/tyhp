using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Found bug #382: a class used only as the call target inside a <c>foreach</c> iterable (with or
/// without <c>await</c>) counts as a use of its import.
/// </summary>
[Trait("Category", "Checker")]
public class ForeachAwaitImportUsageTests
{
    private const string Contracts = """
        <?tyhp
        namespace Tyhp\Contracts {
            interface AsyncIterator<T> {
                public function current(): T;
            }
            interface AsyncIterable<T> {
                public function getAsyncIterator(): AsyncIterator<T>;
            }
        }
        namespace Lib {
            use Tyhp\Contracts\AsyncIterable;
            use Tyhp\Contracts\AsyncIterator;

            final class Lines implements AsyncIterable<string> {
                public function getAsyncIterator(): AsyncIterator<string> {
                    throw new \LogicException('unused');
                }
            }
            final class File {
                public static function linesAsync(string $path): Lines {
                    return new Lines();
                }
                public static function lines(string $path): array<string> {
                    return [];
                }
            }
        }
        """;

    [Fact]
    public void ForeachAwait_StaticCallTarget_MarksImportUsed()
    {
        var result = SnippetEmit.Compile(Contracts + """

            namespace App {
                use Lib\File;

                async function count(string $path): int {
                    int $n = 0;
                    foreach (await File::linesAsync($path) as string $line) {
                        $n++;
                    }
                    return $n;
                }
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().NotContain(w => w.StartsWith("4130", StringComparison.Ordinal));
    }

    [Fact]
    public void Foreach_StaticCallTarget_MarksImportUsed()
    {
        var result = SnippetEmit.Compile(Contracts + """

            namespace App {
                use Lib\File;

                function count(string $path): int {
                    int $n = 0;
                    foreach (File::lines($path) as string $line) {
                        $n++;
                    }
                    return $n;
                }
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().NotContain(w => w.StartsWith("4130", StringComparison.Ordinal));
    }

    [Fact]
    public void UnusedImport_StillWarns()
    {
        var result = SnippetEmit.Compile(Contracts + """

            namespace App {
                use Lib\File;

                function count(): int {
                    return 1;
                }
            }
            """);

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().Contain(w => w.StartsWith("4130", StringComparison.Ordinal));
    }
}
