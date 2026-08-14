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
/// Prop-init #17: relative multi-segment names must resolve against the enclosing namespace
/// (and leading <c>use</c> aliases), not merely gain a leading <c>\</c>. Also locks in the emit
/// convention that unambiguous bare names keep their written spelling once BoundSymbol is set.
/// </summary>
[Trait("Category", "Emitter")]
public class RelativeQualifiedNameEmitterTests
{
    [Fact]
    public void RelativeQualifiedNew_EmitsEnclosingNamespaceFqn()
    {
        var php = CompileAndEmitJoined(
            ("Boom.tyhp", """
                <?tyhp
                namespace Probe\Exceptions;
                final class BoomException extends \LogicException {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                class Caller {
                    public static function boom(): void {
                        throw new Exceptions\BoomException('x');
                    }
                }
                """));

        php.Should().Contain("throw new \\Probe\\Exceptions\\BoomException");
        php.Should().NotContain("throw new \\Exceptions\\BoomException");
    }

    [Fact]
    public void RelativeQualifiedStaticCall_EmitsEnclosingNamespaceFqn()
    {
        var php = CompileAndEmitJoined(
            ("Helper.tyhp", """
                <?tyhp
                namespace Probe\Util;
                final class Helper {
                    public static function id(): int { return 1; }
                }
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                class Caller {
                    public static function run(): int {
                        return Util\Helper::id();
                    }
                }
                """));

        php.Should().Contain("return \\Probe\\Util\\Helper::id()");
        php.Should().NotContain("return \\Util\\Helper::id()");
    }

    [Fact]
    public void UseAliasQualifiedPrefix_ExpandsImportedNamespace()
    {
        var php = CompileAndEmitJoined(
            ("Deep.tyhp", """
                <?tyhp
                namespace Lib\Inner;
                final class Deep {
                    public static function make(): self { return new self(); }
                }
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                use Lib\Inner;
                class Caller {
                    public static function run(): Inner\Deep {
                        return new Inner\Deep();
                    }
                }
                """));

        php.Should().Contain("new \\Lib\\Inner\\Deep()");
        php.Should().NotContain("new \\Inner\\Deep()");
        php.Should().NotContain("new \\Probe\\Inner\\Deep()");
    }

    [Fact]
    public void FullyQualifiedName_StillRootAnchoredAsWritten()
    {
        var php = CompileAndEmitJoined(
            ("Widget.tyhp", """
                <?tyhp
                namespace Other;
                final class Widget {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                class Caller {
                    public static function run(): \Other\Widget {
                        return new \Other\Widget();
                    }
                }
                """));

        php.Should().Contain("new \\Other\\Widget()");
    }

    [Fact]
    public void BareNew_GlobalNamespace_KeepsWrittenSpelling()
    {
        // Regression: Prop-init #17 sets BoundSymbol on every resolved class name; unambiguous
        // bare names must still emit without a leading `\` (restores pre-#17 emit convention).
        var php = CompileAndEmitJoined(
            ("Widget.tyhp", """
                <?tyhp
                final class Widget {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                function make(): Widget {
                    return new Widget();
                }
                """));

        php.Should().Contain("new Widget()");
        php.Should().NotContain("new \\Widget()");
    }

    [Fact]
    public void BareNew_SameNamespace_KeepsWrittenSpelling()
    {
        var php = CompileAndEmitJoined(
            ("Money.tyhp", """
                <?tyhp
                namespace App;
                final class Money {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace App;
                class Caller {
                    public static function make(): Money {
                        return new Money();
                    }
                }
                """));

        php.Should().Contain("new Money()");
        php.Should().NotContain("new \\App\\Money()");
        php.Should().NotContain("new \\Money()");
    }

    [Fact]
    public void BareNew_UseAsRename_EmitsFqn()
    {
        var php = CompileAndEmitJoined(
            ("Boom.tyhp", """
                <?tyhp
                namespace Probe\Exceptions;
                final class BoomException extends \LogicException {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                use Probe\Exceptions\BoomException as Boom;
                class Caller {
                    public static function boom(): void {
                        throw new Boom('x');
                    }
                }
                """));

        php.Should().Contain("throw new \\Probe\\Exceptions\\BoomException");
        php.Should().NotContain("throw new Boom(");
    }

    [Fact]
    public void ImportedShortName_StillEmitsFqn()
    {
        var php = CompileAndEmitJoined(
            ("Boom.tyhp", """
                <?tyhp
                namespace Probe\Exceptions;
                final class BoomException extends \LogicException {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                use Probe\Exceptions\BoomException;
                class Caller {
                    public static function boom(): void {
                        throw new BoomException('x');
                    }
                }
                """));

        php.Should().Contain("throw new \\Probe\\Exceptions\\BoomException");
    }

    [Fact]
    public void TypeHintOnly_RelativeQualifiedName_ResolvesFqn()
    {
        // A relative multi-segment name that appears only in a type-hint position (no `new`
        // alongside it) must still resolve through the binder's own NameResolver.ResolveNamedType,
        // independent of the emitter's TrackAndBuildName / checker's ResolveClassReceiverType paths.
        var php = CompileAndEmitJoined(
            ("Boom.tyhp", """
                <?tyhp
                namespace Probe\Exceptions;
                final class BoomException extends \LogicException {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                class Caller {
                    public static function make(): Exceptions\BoomException {
                        return new Exceptions\BoomException('x');
                    }
                }
                """));

        php.Should().Contain(": \\Probe\\Exceptions\\BoomException");
    }

    [Fact]
    public void InstanceOf_RelativeQualifiedName_ResolvesFqn()
    {
        var php = CompileAndEmitJoined(
            ("Boom.tyhp", """
                <?tyhp
                namespace Probe\Exceptions;
                final class BoomException extends \LogicException {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                class Caller {
                    public static function check(object $x): bool {
                        return $x instanceof Exceptions\BoomException;
                    }
                }
                """));

        php.Should().Contain("instanceof \\Probe\\Exceptions\\BoomException");
    }

    [Fact]
    public void DeepNesting_MultiLevelNamespace_ResolvesFqn()
    {
        var php = CompileAndEmitJoined(
            ("Deep.tyhp", """
                <?tyhp
                namespace Probe\A\B\C;
                final class Target {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                class Caller {
                    public static function make(): A\B\C\Target {
                        return new A\B\C\Target();
                    }
                }
                """));

        php.Should().Contain("\\Probe\\A\\B\\C\\Target");
    }

    [Fact]
    public void NamespaceBlockSyntax_RelativeQualifiedName_ResolvesFqn()
    {
        var php = CompileAndEmitJoined(
            ("Boom.tyhp", """
                <?tyhp
                namespace Probe\Exceptions {
                    final class BoomException extends \LogicException {}
                }
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe {
                    class Caller {
                        public static function boom(): void {
                            throw new Exceptions\BoomException('x');
                        }
                    }
                }
                """));

        php.Should().Contain("throw new \\Probe\\Exceptions\\BoomException");
    }

    [Fact]
    public void SameFileTwoNamespaceBlocks_UseAliasDoesNotLeakAcrossBlocks()
    {
        var php = CompileAndEmitJoined(
            ("Lib.tyhp", """
                <?tyhp
                namespace Lib\Inner {
                    final class Deep {}
                }
                namespace Other\Inner {
                    final class Deep {}
                }
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace ProbeA {
                    use Lib\Inner;
                    class CallerA {
                        public static function make(): Inner\Deep {
                            return new Inner\Deep();
                        }
                    }
                }
                namespace ProbeB {
                    class CallerB {
                        public static function make(): \Other\Inner\Deep {
                            return new \Other\Inner\Deep();
                        }
                    }
                }
                """));

        php.Should().Contain("\\Lib\\Inner\\Deep");
        php.Should().Contain("\\Other\\Inner\\Deep");
    }

    [Fact]
    public void TwoFilesSameNamespace_DifferentUseAlias_DoesNotCrossContaminate()
    {
        // Guards TyhpEmitter's GetEmitResolutionScope file-matching: two files sharing the same
        // namespace but importing different aliases of the same short name must each resolve
        // against their own file's `use`, not leak into one another via a shared namespace block.
        var php = CompileAndEmitJoined(
            ("TargetA.tyhp", """
                <?tyhp
                namespace Vendor\A;
                final class Widget {}
                """),
            ("TargetB.tyhp", """
                <?tyhp
                namespace Vendor\B;
                final class Widget {}
                """),
            ("CallerA.tyhp", """
                <?tyhp
                namespace Probe;
                use Vendor\A;
                class CallerA {
                    public static function make(): A\Widget {
                        return new A\Widget();
                    }
                }
                """),
            ("CallerB.tyhp", """
                <?tyhp
                namespace Probe;
                use Vendor\B;
                class CallerB {
                    public static function make(): B\Widget {
                        return new B\Widget();
                    }
                }
                """));

        php.Should().Contain("\\Vendor\\A\\Widget");
        php.Should().Contain("\\Vendor\\B\\Widget");
        php.Should().NotContain("\\Probe\\A\\Widget");
        php.Should().NotContain("\\Probe\\B\\Widget");
    }

    [Fact]
    public void ExtendsClause_RelativeQualifiedName_ResolvesFqn()
    {
        // Regression: TypeComparer.ResolveClassNameSymbol used to treat any qualified extends
        // name as absolute (`ResolveQualifiedName` from the global root) regardless of a leading
        // `\`, so a relative extends target was looked up under a nonexistent top-level namespace
        // and spuriously reported as unresolved (3017).
        var php = CompileAndEmitJoined(
            ("Base.tyhp", """
                <?tyhp
                namespace Probe\Exceptions;
                class BaseException extends \LogicException {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                class SubException extends Exceptions\BaseException {}
                """));

        php.Should().Contain("class SubException extends \\Probe\\Exceptions\\BaseException");
    }

    [Fact]
    public void ImplementsClause_RelativeQualifiedName_ResolvesFqn()
    {
        // Same regression as above (3018) via TypeComparer.ResolveClassNameSymbol's implements path.
        var php = CompileAndEmitJoined(
            ("IFace.tyhp", """
                <?tyhp
                namespace Probe\Contracts;
                interface Marker {}
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                class Impl implements Contracts\Marker {}
                """));

        php.Should().Contain("implements \\Probe\\Contracts\\Marker");
    }

    [Fact]
    public void ExtendsClause_RelativeQualifiedName_InheritedMemberResolves()
    {
        // Exercises NameResolver.ResolveParentObject, which walks the inheritance chain for
        // member resolution independent of the checker's extends-target diagnostic path.
        // A wrong return-type declaration (`string` for a method that actually returns `int`)
        // only produces a checker diagnostic if `code()`'s return type was actually resolved
        // through the inherited base class; if `ResolveParentObject` silently fails to find the
        // relative-qualified base, the call types as gradual `mixed` and no mismatch is reported.
        var result = CompileOnly(
            ("Base.tyhp", """
                <?tyhp
                namespace Probe\Exceptions;
                class BaseException extends \LogicException {
                    public function code(): int { return 1; }
                }
                """),
            ("Caller.tyhp", """
                <?tyhp
                namespace Probe;
                class SubException extends Exceptions\BaseException {
                    public static function run(): string {
                        $e = new SubException();
                        return $e->code();
                    }
                }
                """));

        result.Diagnostics.Errors.Should().Contain(d =>
            d.Message != null && d.Message.Contains("int") && d.Message.Contains("string"));
    }

    private static Tyhp.Domain.Diagnostics.CompilationResult CompileOnly(
        params (string FileName, string Content)[] sources)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var paths = new List<string>();
        foreach (var (fileName, content) in sources)
        {
            var filePath = Path.Combine(tempDir, fileName);
            File.WriteAllText(filePath, content);
            paths.Add(filePath);
        }

        try
        {
            using var compilationService = new CompilationService();
            return compilationService.ParseFiles(paths, new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string CompileAndEmitJoined(params (string FileName, string Content)[] sources)
    {
        var files = CompileAndEmit(sources);
        return string.Join('\n', files.Select(f => f.GeneratedContent ?? ""));
    }

    private static IReadOnlyList<PHPOutputFile> CompileAndEmit(
        params (string FileName, string Content)[] sources)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var paths = new List<string>();
        foreach (var (fileName, content) in sources)
        {
            var filePath = Path.Combine(tempDir, fileName);
            File.WriteAllText(filePath, content);
            paths.Add(filePath);
        }

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
            var result = compilationService.ParseFiles(paths, new CompilationOptions
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
