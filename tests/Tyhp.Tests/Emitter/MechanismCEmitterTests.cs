using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Mechanism C — generic plumbing emitted onto the author's own class. These tests execute the
/// emitted PHP rather than only matching its text: the shape this replaced passed its text
/// assertions precisely because nothing ever ran the output (FOUND_BUGS.md item 3).
/// </summary>
[Trait("Category", "Emitter")]
public class MechanismCEmitterTests
{
    private const string ChainSource = """
        <?tyhp

        namespace Probe;

        class Base<T, TOther = bool> {
            public array<mixed> $seen = [];

            public function __construct(bool $record = true): void {
                if ($record) {
                    $this->seen[] = 'Base';
                }
            }

            public function baseT(): string {
                return (string) typeof(T);
            }

            public function baseOther(): string {
                return (string) typeof(TOther);
            }
        }

        class Middle extends Base<string> {
            public function __construct(): parent(true) {}
        }

        class Leaf<T> extends Middle {
            public function leafT(): string {
                return (string) typeof(T);
            }
        }
        """;

    [Fact]
    public void GenericChain_RecordsEveryLevelKeyedByDeclaringClass()
    {
        var files = CompileToFiles(ChainSource);

        var output = EmittedPhpRunner.Run(files, """
            $leaf = \Probe\Leaf::new_Probe_Leaf__tyhpGeneric(\Tyhp\Type::float());
            echo 'Leaf::T=', $leaf->leafT(), "\n";
            echo 'Base::T=', $leaf->baseT(), "\n";
            echo 'Base::TOther=', $leaf->baseOther(), "\n";
            """);

        // Leaf and Base both spell a parameter `T`; keyed by declaring class they coexist rather than
        // one overwriting the other.
        output.Should().Contain("Leaf::T=float");
        output.Should().Contain("Base::T=string");

        // TOther was never spelled anywhere in the chain, so it resolves to its declared default.
        output.Should().Contain("Base::TOther=bool");
    }

    [Fact]
    public void GenericInit_RunsExactlyOncePerLevel()
    {
        var files = CompileToFiles(ChainSource);

        var output = EmittedPhpRunner.Run(files, """
            $leaf = \Probe\Leaf::new_Probe_Leaf__tyhpGeneric(\Tyhp\Type::int());
            echo 'seen=', \implode(',', $leaf->seen), "\n";
            """);

        // Middle's `: parent(true)` reaches Base's constructor once. If the gate were missing, Base's
        // own injected init would re-walk the chain on every hop.
        output.Should().Contain("seen=Base");
    }

    [Fact]
    public void ConstructorParameterList_CarriesNoHiddenGenericParameters()
    {
        var php = CompileAndEmit(ChainSource);

        // Item 3: hidden `__generic_*` constructor parameters are gone, so they can no longer collide
        // with a positional or variadic argument.
        php.Should().NotContain("function __construct(?\\Tyhp\\Type $__generic_");
    }

    [Fact]
    public void VariadicConstructor_OnGenericClass_Emits()
    {
        var files = CompileToFiles("""
            <?tyhp

            namespace Probe;

            class Bag<T> {
                public array<mixed> $items = [];

                public function __construct(mixed ...$items): void {
                    $this->items = $items;
                }

                public function describe(): string {
                    return (string) typeof(T);
                }
            }
            """);

        var output = EmittedPhpRunner.Run(files, """
            $bag = \Probe\Bag::new_Probe_Bag__tyhpGeneric(\Tyhp\Type::string(), 'a', 'b');
            echo 'T=', $bag->describe(), ' items=', \implode(',', $bag->items), "\n";
            """);

        // Item 3's original failure: a hidden parameter ahead of the variadic swallowed 'a' and 'b'.
        output.Should().Contain("T=string items=a,b");
    }

    [Fact]
    public void PromotedConstructorProperties_NeedNoLowering()
    {
        var files = CompileToFiles("""
            <?tyhp

            namespace Probe;

            class Labelled<T> {
                public function __construct(public readonly string $label): void {}

                public function describe(): string {
                    return $this->label . ':' . (string) typeof(T);
                }
            }
            """);

        var output = EmittedPhpRunner.Run(files, """
            $x = \Probe\Labelled::new_Probe_Labelled__tyhpGeneric(\Tyhp\Type::int(), 'tag');
            echo $x->describe(), "\n";
            """);

        // The factory calls __construct directly, so PHP performs promotion where it normally would.
        output.Should().Contain("tag:int");
    }

    [Fact]
    public void ClassSkippingParentConstructor_StillBindsAncestorGenerics()
    {
        var files = CompileToFiles("""
            <?tyhp

            namespace Probe;

            class Outer<T, TOther = bool> {
                public bool $ran = false;

                public function __construct(): void {
                    $this->ran = true;
                }

                public function outerT(): string {
                    return (string) typeof(T);
                }
            }

            class SkipsParent<T> extends Outer<T, int> {
                public function __construct(): void {}
            }
            """);

        var output = EmittedPhpRunner.Run(files, """
            $x = \Probe\SkipsParent::new_Probe_SkipsParent__tyhpGeneric(\Tyhp\Type::string());
            echo 'Outer::T=', $x->outerT(), ' ran=', $x->ran ? 'yes' : 'no', "\n";
            """);

        // `: void` skips the parent CONSTRUCTOR, but generic binding is not the constructor's job, so
        // Outer's parameters are still bound.
        output.Should().Contain("Outer::T=string ran=no");
    }

    [Fact]
    public void ClassWithNoDeclaredConstructor_GetsOneSynthesized()
    {
        var files = CompileToFiles("""
            <?tyhp

            namespace Probe;

            class Holder<T, TOther = bool> {
                public function __construct(string $tag): void {}

                public function holderT(): string {
                    return (string) typeof(T);
                }
            }

            class Bare extends Holder<int, string> {}
            """);

        // Bare declares no type parameters, so it has no factory: plain `new` is the call site, and its
        // synthesized constructor carries the gate.
        var output = EmittedPhpRunner.Run(files, """
            $x = new \Probe\Bare('tag');
            echo 'Holder::T=', $x->holderT(), "\n";
            """);

        // Inheriting Holder's constructor outright would run a gate pinned to Holder's level and bind
        // T to its default instead of the declared `<int, string>`.
        output.Should().Contain("Holder::T=int");
    }

    private static string CompileAndEmit(string tyhp) =>
        string.Join('\n', CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "mechanism-c.tyhp");
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
