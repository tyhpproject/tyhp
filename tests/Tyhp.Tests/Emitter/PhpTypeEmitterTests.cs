using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 21.6 Phase 6d: <c>#[\Tyhp\PhpType]</c> is stripped on emit and replaces the PHP type
/// of the host declaration. Implementors inherit the hint from the satisfied member.
/// </summary>
[Trait("Category", "Emitter")]
[Trait("Category", "Story21.6")]
public class PhpTypeEmitterTests
{
    [Fact]
    public void Parameter_PhpType_IsStrippedAndReplacesHint()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function run(#[\Tyhp\PhpType('mixed')] string $s): void {}
            """);

        php.Should().Contain("function run(mixed $s): void");
        php.Should().NotContain("Tyhp\\PhpType");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\PhpType");
    }

    [Fact]
    public void Return_PhpType_IsStrippedAndReplacesHint()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\PhpType('mixed')]
            function run(int $n): string {
                return (string) $n;
            }
            """);

        php.Should().MatchRegex(@"function run\(int \$n\):\s*mixed");
        php.Should().NotContain("Tyhp\\PhpType");
    }

    [Fact]
    public void Property_PhpType_IsStrippedAndReplacesHint()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                #[\Tyhp\PhpType('mixed')]
                public string $name = '';
            }
            """);

        php.Should().Contain("public mixed $name = ''");
        php.Should().NotContain("Tyhp\\PhpType");
    }

    [Fact]
    public void ClassConstant_PhpType_IsStrippedAndReplacesHint()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                #[\Tyhp\PhpType('mixed')]
                public const string TAG = 'x';
            }
            """);

        php.Should().Contain("public const mixed TAG = 'x'");
        php.Should().NotContain("Tyhp\\PhpType");
    }

    [Fact]
    public void PromotedCtor_PhpType_CoversParamAndProperty()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public function __construct(
                    #[\Tyhp\PhpType('mixed')]
                    public int $id,
                ): void {}
            }
            """);

        php.Should().Contain("public mixed $id");
        php.Should().NotContain("public int $id");
        php.Should().NotContain("Tyhp\\PhpType");
    }

    [Fact]
    public void Implementor_InheritsPhpTypeFromInterface()
    {
        var php = CompileAndEmit("""
            <?tyhp
            interface Reader {
                #[\Tyhp\PhpType('mixed')]
                public function offsetGet(#[\Tyhp\PhpType('mixed')] int $offset): string;
            }

            class Config implements Reader {
                public function offsetGet(int $offset): string {
                    return '';
                }
            }
            """);

        php.Should().Contain("function offsetGet(mixed $offset): mixed");
        php.Should().NotContain("Tyhp\\PhpType");
        Regex.Matches(php, @"function offsetGet\(mixed \$offset\):\s*mixed").Count.Should().Be(2);
    }

    [Fact]
    public void Implementor_InheritsPhpTypeFromAbstractMethod()
    {
        var php = CompileAndEmit("""
            <?tyhp
            abstract class Base {
                #[\Tyhp\PhpType('mixed')]
                abstract public function read(#[\Tyhp\PhpType('mixed')] string $key): int;
            }

            class Child extends Base {
                public function read(string $key): int {
                    return 0;
                }
            }
            """);

        php.Should().Contain("function read(mixed $key): mixed");
        php.Should().NotContain("Tyhp\\PhpType");
    }

    [Fact]
    public void Closure_PhpTypeOnReturnAndParam_IsApplied()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function wrap(): void {
                $fn = #[\Tyhp\PhpType('mixed')] function (#[\Tyhp\PhpType('mixed')] int $n): string {
                    return (string) $n;
                };
            }
            """);

        php.Should().Contain("function (mixed $n): mixed");
        php.Should().NotContain("Tyhp\\PhpType");
    }

    [Fact]
    public void FileLevelConst_PhpType_IsStrippedWithoutInjectingAType()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\PhpType('mixed')]
            const TAG = 'x';
            """);

        php.Should().Contain("const TAG = 'x'");
        php.Should().NotContain("const mixed TAG");
        php.Should().NotContain("Tyhp\\PhpType");
    }

    [Fact]
    public void PhpTypeClass_CompilesAndIsNotEmitted()
    {
        // Small standalone stand-in for the real `runtime/packages/core/tyhp_src/PhpType.tyhp`
        // marker-attribute source (kept in sync by hand — self-referential `#[\Tyhp\NoEmit()]`
        // on the very class that defines `\Tyhp\PhpType`). C# unit tests must not load
        // `runtime/packages`; see `IsolatedCompilation`.
        const string phpTypeSource = """
            <?tyhp
            namespace Tyhp;

            #[\Attribute(\Attribute::TARGET_ALL & ~\Attribute::TARGET_CLASS)]
            #[\Tyhp\NoEmit()]
            final class PhpType
            {
                public function __construct(
                    public readonly string $type,
                ): void {}
            }
            """;

        // `SyntheticPhpStubs.MinimalPhp` already declares `Tyhp\PhpType` (bodiless) for
        // attribute usages elsewhere in this file, so it must be excluded here — this source
        // is the declaration itself.
        var php = CompileAndEmit(phpTypeSource, skipChecking: true, includeMinimalPhpStubs: false);
        php.Should().NotContain("class PhpType");
        php.Should().NotContain("public readonly string $type");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\NoEmit");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\PhpType");
    }

    // A minimal `\Attribute` builtin stand-in — used instead of `SyntheticPhpStubs.MinimalPhp`
    // for the test above, which redeclares `Tyhp\PhpType` itself and would otherwise collide
    // with MinimalPhp's bodiless stand-in for that same name.
    private const string AttributeOnlyStub = """
        <?tyhpdef
        #[\Attribute]
        class Attribute {
            public const int TARGET_CLASS ?? 1;
            public const int TARGET_FUNCTION ?? 2;
            public const int TARGET_METHOD ?? 4;
            public const int TARGET_PROPERTY ?? 8;
            public const int TARGET_CLASS_CONSTANT ?? 16;
            public const int TARGET_PARAMETER ?? 32;
            public const int TARGET_CONSTANT ?? 64;
            public const int TARGET_ALL ?? 63;
            public const int IS_REPEATABLE ?? 64;
            public function __construct(int $flags = 0): void;
        }
        """;

    private static string CompileAndEmit(
        string tyhp,
        IReadOnlyList<string>? tyhpdefIncludes = null,
        bool skipChecking = false,
        bool includeMinimalPhpStubs = true) =>
        string.Join('\n', CompileToFiles(tyhp, tyhpdefIncludes, skipChecking, includeMinimalPhpStubs).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(
        string tyhp,
        IReadOnlyList<string>? tyhpdefIncludes = null,
        bool skipChecking = false,
        bool includeMinimalPhpStubs = true)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "phptype.tyhp");
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
            var extras = new List<string>(tyhpdefIncludes ?? []);
            if (!includeMinimalPhpStubs)
            {
                var attributeStubPath = Path.Combine(tempDir, "attribute-only.tyhpdef");
                File.WriteAllText(attributeStubPath, AttributeOnlyStub);
                extras.Add(attributeStubPath);
            }

            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(
                    tempDir,
                    phpVersion: "8.4",
                    skipChecking: skipChecking,
                    tyhpdefIncludePaths: extras,
                    includeMinimalPhpStubs: includeMinimalPhpStubs));

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
