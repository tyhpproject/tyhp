using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// <c>#[\Tyhp\NoEmit]</c> omits tagged type declarations from PHP and strips usages of those
/// types as attributes. <c>#[\Tyhp\Php]</c> / <c>#[\Tyhp\PhpType]</c> erase through this path
/// because those classes are themselves tagged — version gating and type-hint substitution stay
/// name-specific elsewhere.
/// </summary>
[Trait("Category", "Emitter")]
public class NoEmitEmitterTests
{
    [Fact]
    public void UserDefinedNoEmitClass_DeclarationIsOmitted()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            #[\Attribute(\Attribute::TARGET_CLASS)]
            #[\Tyhp\NoEmit()]
            final class CompileTimeOnly {}

            final class Keep {}
            """);

        php.Should().NotContain("class CompileTimeOnly");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\NoEmit");
        php.Should().Contain("class Keep");
    }

    [Fact]
    public void UserDefinedNoEmitAttribute_UsageIsStripped_HostStillEmits()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            #[\Attribute(\Attribute::TARGET_CLASS | \Attribute::TARGET_FUNCTION)]
            #[\Tyhp\NoEmit()]
            final class CompileTimeOnly {}

            #[CompileTimeOnly]
            #[\Deprecated]
            function keep(): void {}

            #[CompileTimeOnly]
            final class Host {}
            """);

        php.Should().NotContain("class CompileTimeOnly");
        php.Should().NotContain("CompileTimeOnly");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\NoEmit");
        php.Should().Contain("function keep()");
        php.Should().Contain("#[\\Deprecated]");
        php.Should().Contain("class Host");
    }

    [Fact]
    public void UserDefinedNoEmitAttribute_OnParameter_IsStripped()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace Probe;

            #[\Attribute(\Attribute::TARGET_PARAMETER)]
            #[\Tyhp\NoEmit()]
            final class CompileTimeOnly {}

            function keep(#[CompileTimeOnly] string $name): void {}
            """);

        php.Should().NotContain("class CompileTimeOnly");
        php.Should().NotContain("CompileTimeOnly");
        php.Should().Contain("function keep(string $name): void");
    }

    [Fact]
    public void PhpType_StillSubstitutesHintAndStripsViaNoEmit()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\PhpType('mixed')]
            function run(#[\Tyhp\PhpType('mixed')] string $s): string {
                return $s;
            }
            """);

        php.Should().MatchRegex(@"function run\(mixed \$s\):\s*mixed");
        php.Should().NotContain("Tyhp\\PhpType");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\PhpType");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\NoEmit");
    }

    [Fact]
    public void PhpGate_StillStripsAttributeWhenSatisfied()
    {
        var php = CompileAndEmit("""
            <?tyhp
            #[\Tyhp\Php(">=8.4")]
            #[\Deprecated]
            function only_on_84(): void {}
            """);

        php.Should().Contain("function only_on_84()");
        php.Should().Contain("#[\\Deprecated]");
        php.Should().NotContain("Tyhp\\Php");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\Php\(");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\NoEmit");
    }

    [Fact]
    public void CoreNoEmitClass_DeclarationIsOmitted()
    {
        // `SyntheticPhpStubs.MinimalPhp` already declares `Tyhp\NoEmit` (bodiless) for attribute
        // usages elsewhere in this file, so it must be excluded here — this source is the
        // declaration itself.
        var php = CompileAndEmit(CoreNoEmitSource, skipChecking: true, includeMinimalPhpStubs: false);
        php.Should().NotContain("class NoEmit");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\NoEmit");
    }

    [Fact]
    public void CorePhpClass_DeclarationIsOmitted()
    {
        var php = CompileAndEmit(CorePhpSource, skipChecking: true, includeMinimalPhpStubs: false);
        php.Should().NotContain("class Php");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\NoEmit");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\Php");
    }

    [Fact]
    public void CorePhpTypeClass_DeclarationIsOmitted()
    {
        var php = CompileAndEmit(CorePhpTypeSource, skipChecking: true, includeMinimalPhpStubs: false);
        php.Should().NotContain("class PhpType");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\NoEmit");
        php.Should().NotMatchRegex(@"#\[\s*\\Tyhp\\PhpType");
    }

    // A minimal `\Attribute` builtin stand-in — used instead of `SyntheticPhpStubs.MinimalPhp`
    // for the three tests above, which redeclare `Tyhp\NoEmit` / `Tyhp\Php` / `Tyhp\PhpType`
    // themselves and would otherwise collide with MinimalPhp's bodiless stand-ins for those
    // same names.
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

    // Small standalone stand-ins for the real `runtime/packages/core/tyhp_src/{NoEmit,Php,
    // PhpType}.tyhp` marker-attribute sources (kept in sync by hand — self-referential
    // `#[\Tyhp\NoEmit()]` attributes on the very class that defines `\Tyhp\NoEmit`). C# unit
    // tests must not load `runtime/packages`; see `IsolatedCompilation`.
    private const string CoreNoEmitSource = """
        <?tyhp
        namespace Tyhp;

        #[\Attribute(
            \Attribute::TARGET_CLASS
        )]
        #[\Tyhp\NoEmit()]
        final class NoEmit
        {
            public function __construct(): void {}
        }
        """;

    private const string CorePhpSource = """
        <?tyhp
        namespace Tyhp;

        #[\Attribute(\Attribute::TARGET_ALL)]
        #[\Tyhp\NoEmit()]
        final class Php
        {
            public function __construct(
                public readonly string $version,
            ): void {}
        }
        """;

    private const string CorePhpTypeSource = """
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
        var filePath = Path.Combine(tempDir, "noemit.tyhp");
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
