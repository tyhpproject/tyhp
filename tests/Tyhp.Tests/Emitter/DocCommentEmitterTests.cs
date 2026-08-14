using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Author-written docblocks reaching the emitted PHP, which is what gives PHP callers of a compiled
/// Tyhp package their IDE and static-analysis types (FOUND_BUGS.md items 19-21).
/// </summary>
[Trait("Category", "Emitter")]
public class DocCommentEmitterTests
{
    [Fact]
    public void ClassDocComment_IsEmittedWithoutSwallowingTheClassBody()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            /**
             * Widget doc.
             *
             * @template T of object
             */
            final class Widget {
                private int $count = 1;

                public function value(): int {
                    return $this->count;
                }
            }
            """);

        php.Should().Contain("@template T of object");

        // The docblock used to be attached to a wrapper that was never registered with its parent,
        // so the class emitted as an empty shell and every member vanished silently.
        php.Should().Contain("private int $count = 1;");
        php.Should().Contain("public function value(): int");
    }

    [Fact]
    public void TypedLocalInMethodBody_DoesNotStealTheClassDocComment()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            /**
             * Widget doc.
             */
            final class Widget {
                public function value(): int {
                    int $local = 1;

                    return $local;
                }
            }
            """);

        // Typed locals look up docblocks too. While the class claimed its own only after its body was
        // visited, the local advanced the shared cursor first and the docblock was re-emitted inside
        // the method body, attached to the local's assignment.
        php.Should().Contain("""
            /**
             * Widget doc.
             */
            final class Widget
            {
            """);
        php.Should().NotContain("""
                /**
                 * Widget doc.
                 */
                $local = 1;
            """);
    }

    [Fact]
    public void MemberDocComments_AreEmitted()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                /**
                 * @var non-empty-string
                 */
                public const TAG = 'w';

                /**
                 * @var array<string, int>
                 */
                private array<string, int> $items = [];

                /**
                 * @param class-string $name
                 */
                public static function make(string $name): object {
                    return new \stdClass();
                }
            }
            """);

        php.Should().Contain("@var non-empty-string");
        php.Should().Contain("@var array<string, int>");
        php.Should().Contain("@param class-string $name");
        php.Should().Contain("public const TAG = 'w';");
    }

    [Fact]
    public void ClassConstVisibility_IsEmitted()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public const TAG = 'w';
                protected const OTHER = 'o';
                private const SECRET = 's';
                final public const FINAL_TAG = 'f';
            }
            """);

        php.Should().Contain("public const TAG = 'w';");
        php.Should().Contain("protected const OTHER = 'o';");
        php.Should().Contain("private const SECRET = 's';");
        php.Should().Contain("final public const FINAL_TAG = 'f';");
        // Bare `const` (no visibility) must not appear for these class members.
        php.Should().NotMatchRegex(@"(?m)^\s*const (TAG|OTHER|SECRET|FINAL_TAG) =");
    }

    [Fact]
    public void ClassConstVisibility_AppliesToEveryConstInACommaList()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            const TOP_LEVEL = 1;

            interface Marker {
                public const IFACE = 'i';
            }

            final class Widget {
                private const A = 1, B = 2;
                const BARE = 'b';
            }
            """);

        // The modifiers belong to the shared `const` statement; each name in the list emits on its own
        // line and must repeat them.
        php.Should().Contain("private const A = 1;");
        php.Should().Contain("private const B = 2;");

        // A class member declared without visibility stays bare, and a file-scope constant must never
        // pick up a modifier (PHP has none for those).
        php.Should().Contain("const BARE = 'b';");
        php.Should().Contain("const TOP_LEVEL = 1;");
        php.Should().NotMatchRegex(@"(?m)^\s*(public|protected|private) const TOP_LEVEL");

        php.Should().Contain("public const IFACE = 'i';");
    }

    [Fact]
    public void ClassConstType_IsEmitted()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public const string TAG = 'w';
                protected const ?int COUNT = null;
                private const string|int MIXED = 1;
                final public const string FINAL_TAG = 'f';
            }
            """);

        php.Should().Contain("public const string TAG = 'w';");
        php.Should().Contain("protected const ?int COUNT = null;");
        php.Should().Contain("private const string | int MIXED = 1;");
        php.Should().Contain("final public const string FINAL_TAG = 'f';");
    }

    [Fact]
    public void ClassConstType_AppliesToEveryConstInACommaList()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                public const string A = 'a', B = 'b';
                const BARE = 'x';
            }
            """);

        // The type belongs to the shared typed-const statement; each name emits on its own line.
        php.Should().Contain("public const string A = 'a';");
        php.Should().Contain("public const string B = 'b';");
        php.Should().Contain("const BARE = 'x';");
        php.Should().NotMatchRegex(@"(?m)^\s*public const A =");
        php.Should().NotMatchRegex(@"(?m)^\s*const string BARE");
    }

    [Fact]
    public void EnumAndCaseDocComments_AreEmitted()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            /**
             * Suit doc.
             */
            enum Suit: string {
                /**
                 * Hearts doc.
                 */
                case Hearts = 'H';
            }
            """);

        php.Should().Contain("Suit doc.");
        php.Should().Contain("Hearts doc.");
        php.Should().Contain("case Hearts = 'H';");
    }

    [Fact]
    public void MembersWithoutDocComments_AreNotDecorated()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            final class Widget {
                private int $count = 1;

                public function value(): int {
                    return $this->count;
                }
            }
            """);

        // The compiler may emit a file-level "Generated by Tyhp" docblock; assert no
        // member/class docblocks were fabricated for undecorated source members.
        php.Should().NotContain("* Widget");
        php.Should().NotContain("@var");
        php.Should().NotContain("@param");
        php.Should().NotContain("@return");
    }

    [Fact]
    public void OnlyTheNearestDocComment_IsClaimed()
    {
        var php = CompileAndEmit("""
            <?tyhp

            /**
             * File header.
             */

            namespace Probe;

            /**
             * Widget doc.
             */
            final class Widget {
                private int $count = 1;
            }
            """);

        // The scan runs backwards and used to keep collecting past the first hit, concatenating every
        // earlier docblock onto this one in reverse order.
        php.Should().Contain("Widget doc.");
        php.Should().NotContain("File header.");
    }

    private static string CompileAndEmit(string tyhp) =>
        string.Join('\n', CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "doc-comments.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = "8.4",
                })
                .Build();
            var project = new Project(configuration);

            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets);
            return new TyhpEmitter(context).Emit(result.ParsedFiles!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
