using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// FOUND_BUGS item 26: attributes on class constants (and the same comma-list gap for properties)
/// must survive emit; bare file-scope <c>const</c> stays attribute-free. Attributed top-level
/// <c>const</c> (PHP ≥ 8.5) is covered in <see cref="ConstAndHookAttributeEmitterTests"/>.
/// </summary>
[Trait("Category", "Emitter")]
public class AttributeEmitterTests
{
    [Fact]
    public void ClassConstant_Attribute_IsEmitted()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {}

            final class Widget {
                #[Marker]
                public const string NAMES = 'a';
            }
            """);

        php.Should().Contain("#[\\Probe\\Marker]");
        php.Should().Contain("public const string NAMES = 'a';");
        php.Should().MatchRegex(
            new Regex(@"#\[\\Probe\\Marker\]\s+public const string NAMES = 'a';", RegexOptions.Singleline));
    }

    [Fact]
    public void ClassConstant_AttributeWithArguments_IsEmitted()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {
                public function __construct(public string $label = '') {}
            }

            final class Widget {
                #[Marker('x')]
                public const int COUNT = 1;
            }
            """);

        php.Should().Contain("#[\\Probe\\Marker('x')]");
        php.Should().Contain("public const int COUNT = 1;");
    }

    [Fact]
    public void ClassConstant_CommaList_RepeatsAttributePerName()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {}

            final class Widget {
                #[Marker]
                public const string A = 'a', B = 'b';
            }
            """);

        // One attribute line per emitted constant (comma lists split to one name per line).
        var markerMatches = Regex.Matches(php, @"#\[\\Probe\\Marker\]");
        markerMatches.Should().HaveCount(2);
        php.Should().Contain("public const string A = 'a';");
        php.Should().Contain("public const string B = 'b';");
        php.Should().MatchRegex(
            new Regex(
                @"#\[\\Probe\\Marker\]\s+public const string A = 'a';\s+#\[\\Probe\\Marker\]\s+public const string B = 'b';",
                RegexOptions.Singleline));
    }

    [Fact]
    public void FileScopeConst_DoesNotEmitAttributeLines()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {}

            const TOP_LEVEL = 1;

            final class Widget {
                #[Marker]
                public const string TAG = 'w';
            }
            """);

        php.Should().Contain("const TOP_LEVEL = 1;");
        // File-scope const must stay bare; the only Marker attribute is the class constant's.
        php.Should().NotMatchRegex(@"(?m)^\s*#\[.*\]\s*\n\s*const TOP_LEVEL");
        php.Should().MatchRegex(
            new Regex(@"#\[\\Probe\\Marker\]\s+public const string TAG = 'w';", RegexOptions.Singleline));
    }

    [Fact]
    public void Property_CommaList_RepeatsAttributePerName()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {}

            final class Widget {
                #[Marker]
                public int $a = 1, $b = 2;
            }
            """);

        Regex.Matches(php, @"#\[\\Probe\\Marker\]").Should().HaveCount(2);
        php.Should().MatchRegex(
            new Regex(
                @"#\[\\Probe\\Marker\]\s+public int \$a = 1;\s+#\[\\Probe\\Marker\]\s+public int \$b = 2;",
                RegexOptions.Singleline));
    }

    [Fact]
    public void EnumCase_Attribute_IsEmitted()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {}

            enum Suit {
                #[Marker]
                case Hearts;
            }
            """);

        php.Should().MatchRegex(
            new Regex(@"#\[\\Probe\\Marker\]\s+case Hearts;", RegexOptions.Singleline));
    }

    [Fact]
    public void Class_DocCommentThenAttribute_EmitsDocBeforeAttribute()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            /**
             * Opt-in marker attribute class.
             */
            #[\Attribute]
            final class Marker {}
            """);

        php.Should().MatchRegex(
            new Regex(
                @"/\*\*.*?Opt-in marker attribute class\.\s*\*/\s*#\[\\Attribute\]\s*final class Marker",
                RegexOptions.Singleline));
        php.Should().NotMatchRegex(
            new Regex(@"#\[\\Attribute\].*?/\*\*", RegexOptions.Singleline));
    }

    [Fact]
    public void Method_DocCommentThenAttribute_EmitsDocBeforeAttribute()
    {
        var php = CompileAndEmit("""
            <?tyhp

            namespace Probe;

            #[\Attribute]
            class Marker {}

            final class Widget {
                /** Does a thing. */
                #[Marker]
                public function run(): void {}
            }
            """);

        php.Should().MatchRegex(
            new Regex(
                @"/\*\* Does a thing\. \*/\s*#\[\\Probe\\Marker\]\s*public function run\(\): void",
                RegexOptions.Singleline));
    }

    private static string CompileAndEmit(string tyhp) =>
        string.Join('\n', CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "attributes.tyhp");
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
