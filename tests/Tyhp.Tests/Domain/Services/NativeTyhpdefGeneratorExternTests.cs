using System.Text.Json;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story21.1")]
public class NativeTyhpdefGeneratorExternTests
{
    [Fact]
    public void PackagePath_RequireCatalogHit_AddsWrapperRequireAndDoesNotExtern()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-log",
            phpRequire: """{ "acme/log": "^3.0" }""",
            tyhpdef: """
                <?tyhpdef
                namespace Acme\Log {
                    interface LoggerInterface {}
                }
                """);
        env.AddPhpClass("Logger.php", """
            <?php
            namespace Acme\Lib;
            class Logger {
                public function to(\Acme\Log\LoggerInterface $logger): void {}
            }
            """);
        env.WriteTargetComposer(require: """{ "php": ">=8.2", "acme/log": "^3.0" }""");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        MainText(result).Should().Contain("\\Acme\\Log\\LoggerInterface");
        File.Exists(env.ExternsPath).Should().BeFalse();
        var wrapper = JsonDocument.Parse(env.WrapperComposerJson()).RootElement;
        wrapper.GetProperty("require").TryGetProperty("tyhpdef/acme-log", out _).Should().BeFalse();
        wrapper.GetProperty("require-dev").GetProperty("tyhpdef/acme-log").GetString().Should().Be("@dev");
        wrapper.GetProperty("extra").GetProperty("tyhp").GetProperty("require")
            .GetProperty("tyhpdef/acme-log").GetString().Should().Be("@dev");
        env.WrapperComposerJson().Should().NotContain("extern");
    }

    [Fact]
    public void PackagePath_PhpStubsCatalogHit_AddsRequireDevNotRequirePin()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-stubs",
            phpRequire: """{ "php": ">=8.2" }""",
            tyhpdef: """
                <?tyhpdef
                class DateTimeImmutable {}
                """);
        env.AddPhpClass("Clock.php", """
            <?php
            namespace Acme\Lib;
            class Clock {
                public function now(): \DateTimeImmutable {}
            }
            """);
        env.WriteTargetComposer(require: """{ "php": ">=8.2" }""");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        MainText(result).Should().Contain("\\DateTimeImmutable");
        File.Exists(env.ExternsPath).Should().BeFalse();

        var wrapper = JsonDocument.Parse(env.WrapperComposerJson()).RootElement;
        wrapper.GetProperty("require").TryGetProperty("tyhpdef/acme-stubs", out _).Should().BeFalse();
        wrapper.GetProperty("require-dev").GetProperty("tyhpdef/acme-stubs").GetString().Should().Be("@dev");
        wrapper.GetProperty("extra").GetProperty("tyhp").GetProperty("require")
            .GetProperty("tyhpdef/acme-stubs").GetString().Should().Be("@dev");
        env.WrapperComposerJson().Should().NotContain("0.0.1");
    }

    [Fact]
    public void PackagePath_SuggestCatalogHit_WritesExternsWithProvidedBy()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-search",
            phpRequire: """{ "acme/search": "^8.0" }""",
            tyhpdef: """
                <?tyhpdef
                namespace Acme\Search {
                    class Client {}
                    class Document {}
                }
                """);
        env.AddPhpClass("Handler.php", """
            <?php
            namespace Acme\Lib;
            class SearchHandler {
                public function __construct(\Acme\Search\Client $client) {}
                public function send(\Acme\Search\Document $doc): void {}
            }
            """);
        env.WriteTargetComposer(suggest: """{ "acme/search": "optional peer" }""");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        File.Exists(env.ExternsPath).Should().BeTrue();
        var externs = File.ReadAllText(env.ExternsPath);
        externs.Should().Contain("AUTO-GENERATED");
        externs.Should().Contain("// @provided-by: tyhpdef/acme-search");
        externs.Should().Contain("extern class \\Acme\\Search\\Client;");
        externs.Should().Contain("extern class \\Acme\\Search\\Document;");
        env.WrapperComposerJson().Should().NotContain("tyhpdef/acme-search");
        AssertParses(externs);
    }

    [Fact]
    public void PackagePath_RequireDevCatalogHit_WritesExtern()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-tester",
            phpRequire: """{ "acme/tester": "^10" }""",
            tyhpdef: """
                <?tyhpdef
                namespace Acme\Tester {
                    class CaseBase {}
                }
                """);
        env.AddPhpClass("Helper.php", """
            <?php
            namespace Acme\Lib;
            class TestHelper {
                public function base(): \Acme\Tester\CaseBase {}
            }
            """);
        env.WriteTargetComposer(requireDev: """{ "acme/tester": "^10" }""");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        File.ReadAllText(env.ExternsPath).Should().Contain("// @provided-by: tyhpdef/acme-tester");
        File.ReadAllText(env.ExternsPath).Should().Contain("extern class \\Acme\\Tester\\CaseBase;");
        env.WrapperComposerJson().Should().NotContain("tyhpdef/acme-tester");
    }

    [Fact]
    public void PackagePath_ExtSuggestCatalogHit_WritesExternWithExtProvidedBy()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-widget",
            phpRequire: """{ "php": ">=8.2", "ext-widget": "*" }""",
            tyhpdef: """
                <?tyhpdef
                class WidgetHandle {}
                """);
        env.AddPhpClass("Handler.php", """
            <?php
            namespace Acme\Lib;
            class Handler {
                public function open(): \WidgetHandle {}
            }
            """);
        env.WriteTargetComposer(suggest: """{ "ext-widget": "widget transport support" }""");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        File.Exists(env.ExternsPath).Should().BeTrue();
        var externs = File.ReadAllText(env.ExternsPath);
        externs.Should().Contain("// @provided-by: tyhpdef/acme-widget");
        externs.Should().Contain("extern class \\WidgetHandle;");
        env.WrapperComposerJson().Should().NotContain("tyhpdef/acme-widget");
        AssertParses(externs);
    }

    [Fact]
    public void PackagePath_CatalogMiss_DoesNotExtern()
    {
        using var env = new ExternTestEnv();
        env.AddPhpClass("Ghost.php", """
            <?php
            namespace Acme\Lib;
            class Ghost {
                public function ping(\Unknown\Origin $x): void {}
            }
            """);
        env.WriteTargetComposer(suggest: """{ "unknown/origin": "maybe" }""");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        MainText(result).Should().Contain("\\Unknown\\Origin");
        File.Exists(env.ExternsPath).Should().BeFalse();
    }

    [Fact]
    public void PackagePath_UnqualifiedAlias_IsNotExterned_AndIntraPackageLogRecordIsQualified()
    {
        using var env = new ExternTestEnv();
        env.AddPhpClass("Log.php", """
            <?php
            namespace Acme\Lib;
            class LogRecord {}
            class Logger {
                /**
                 * @param array<LogRecord> $records
                 * @param InputOptions $opts
                 */
                public function addMany(array $records, $opts): LogRecord {
                    return $records[0];
                }
            }
            """);
        env.WriteTargetComposer();

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var text = MainText(result);
        text.Should().Contain("array<\\Acme\\Lib\\LogRecord>");
        text.Should().Contain("InputOptions");
        File.Exists(env.ExternsPath).Should().BeFalse();
        if (File.Exists(env.ExternsPath))
        {
            File.ReadAllText(env.ExternsPath).Should().NotContain("InputOptions");
            File.ReadAllText(env.ExternsPath).Should().NotContain("LogRecord");
        }
    }

    [Fact]
    public void PackagePath_ClassExtendingExtern_IsOmittedWithWarning()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-tester",
            phpRequire: """{ "acme/tester": "^10" }""",
            tyhpdef: """
                <?tyhpdef
                namespace Acme\Tester {
                    class CaseBase {}
                }
                """);
        env.AddPhpClass("SuiteTest.php", """
            <?php
            namespace Acme\Lib;
            class SuiteTest extends \Acme\Tester\CaseBase {
                public function testIt(): void {}
            }
            class Keeper {
                public function ok(): int { return 1; }
            }
            """);
        env.WriteTargetComposer(requireDev: """{ "acme/tester": "^10" }""");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var text = MainText(result);
        text.Should().Contain("class Keeper");
        text.Should().NotContain("class SuiteTest");
        result.Warnings.Should().Contain(w =>
            w.Contains("SuiteTest", StringComparison.Ordinal)
            && w.Contains("Acme\\Tester\\CaseBase", StringComparison.Ordinal));
    }

    [Fact]
    public void PackagePath_DescendantOfOmittedExternBase_IsAlsoOmitted()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-tester",
            phpRequire: """{ "acme/tester": "^10" }""",
            tyhpdef: """
                <?tyhpdef
                namespace Acme\Tester {
                    class CaseBase {}
                }
                """);
        env.AddPhpClass("SuiteTest.php", """
            <?php
            namespace Acme\Lib;
            class SuiteTest extends \Acme\Tester\CaseBase {
                public function testIt(): void {}
            }
            class ChildSuite extends SuiteTest {
                public function testChild(): void {}
            }
            class GrandSuite extends ChildSuite {
                public function testGrand(): void {}
            }
            class Keeper {
                public function ok(): int { return 1; }
            }
            """);
        env.WriteTargetComposer(requireDev: """{ "acme/tester": "^10" }""");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var text = MainText(result);
        text.Should().Contain("class Keeper");
        text.Should().NotContain("class SuiteTest");
        text.Should().NotContain("class ChildSuite");
        text.Should().NotContain("class GrandSuite");
        result.Warnings.Should().Contain(w =>
            w.Contains("SuiteTest", StringComparison.Ordinal)
            && w.Contains("Acme\\Tester\\CaseBase", StringComparison.Ordinal));
        result.Warnings.Should().Contain(w =>
            w.Contains("ChildSuite", StringComparison.Ordinal)
            && w.Contains("SuiteTest", StringComparison.Ordinal));
        result.Warnings.Should().Contain(w =>
            w.Contains("GrandSuite", StringComparison.Ordinal)
            && w.Contains("ChildSuite", StringComparison.Ordinal));
    }

    [Fact]
    public void PackagePath_PhpDocBareWidgetInOtherNamespace_DoesNotStripConsumerGet()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-tester",
            phpRequire: """{ "acme/tester": "^10" }""",
            tyhpdef: """
                <?tyhpdef
                namespace Acme\Tester {
                    class CaseBase {}
                }
                """);
        env.AddPhpClass("Widget.php", """
            <?php
            namespace Acme\A;
            class Widget extends \Acme\Tester\CaseBase {
                public function testX(): void {}
            }
            """);
        env.AddPhpClass("Consumer.php", """
            <?php
            namespace Acme\B;
            class Consumer {
                /**
                 * @param Widget $thing
                 * @return Widget
                 */
                public function get($thing) { return $thing; }
                public function ok(): int { return 1; }
            }
            """);
        env.WriteTargetComposer(requireDev: """{ "acme/tester": "^10" }""");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        var text = MainText(result);
        text.Should().NotContain("class Widget");
        text.Should().Contain("class Consumer");
        text.Should().Contain("function get");
        text.Should().Contain("function ok");
        result.Warnings.Should().Contain(w =>
            w.Contains("Widget", StringComparison.Ordinal)
            && w.Contains("Acme\\Tester\\CaseBase", StringComparison.Ordinal));
        result.Warnings.Should().NotContain(w =>
            w.Contains("Consumer::get", StringComparison.Ordinal));
    }

    [Fact]
    public void PackagePath_RegenOverwritesExternsOnly_LeavesHandSibling()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-search",
            phpRequire: """{ "acme/search": "^8.0" }""",
            tyhpdef: """
                <?tyhpdef
                namespace Acme\Search {
                    class Client {}
                }
                """);
        env.AddPhpClass("Handler.php", """
            <?php
            namespace Acme\Lib;
            class Handler {
                public function __construct(\Acme\Search\Client $client) {}
            }
            """);
        env.WriteTargetComposer(suggest: """{ "acme/search": "optional" }""");
        Directory.CreateDirectory(env.OutputDir);
        var handPath = Path.Combine(env.OutputDir, "backers.extern.tyhpdef");
        const string hand = """
            <?tyhpdef
            // hand-written; regen must not touch
            extern class \Hand\Written;
            """;
        File.WriteAllText(handPath, hand);
        File.WriteAllText(env.ExternsPath, "<?tyhpdef\n/** stale */\nextern class \\Stale\\Gone;\n");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        File.ReadAllText(handPath).Should().Be(hand);
        var externs = File.ReadAllText(env.ExternsPath);
        externs.Should().Contain("extern class \\Acme\\Search\\Client;");
        externs.Should().NotContain("\\Stale\\Gone");
        externs.Should().NotContain("\\Hand\\Written");
    }

    [Fact]
    public void Catalog_SkipsExternPlaceholdersSoConsumerIsNotProvider()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-consumer",
            phpRequire: """{ "acme/consumer": "^1.0" }""",
            tyhpdef: """
                <?tyhpdef
                // @provided-by: tyhpdef/acme-search
                extern class \Acme\Search\Client;
                class Own {}
                """);
        env.AddPhpClass("Use.php", """
            <?php
            namespace Acme\Lib;
            class UseIt {
                public function f(\Acme\Search\Client $c): void {}
            }
            """);
        env.WriteTargetComposer(suggest: """{ "acme/search": "optional" }""");

        var result = env.Generate();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        File.Exists(env.ExternsPath).Should().BeFalse(
            "consumer's own extern must not make it look like the provider");
    }

    [Fact]
    public void Source_PackageShapedTree_EmitsExtern()
    {
        using var env = new ExternTestEnv();
        env.AddCatalogPackage(
            "tyhpdef/acme-search",
            phpRequire: """{ "acme/search": "^8.0" }""",
            tyhpdef: """
                <?tyhpdef
                namespace Acme\Search {
                    interface Client {}
                }
                """);
        env.AddPhpClass("Handler.php", """
            <?php
            namespace Acme\Lib;
            class Handler {
                public function __construct(\Acme\Search\Client $client) {}
            }
            """);
        env.WriteTargetComposer(suggest: """{ "acme/search": "optional" }""");

        var result = env.GenerateSource();

        result.Diagnostics.HasErrors.Should().BeFalse(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
        File.ReadAllText(env.ExternsPath).Should().Contain("extern interface \\Acme\\Search\\Client;");
        File.ReadAllText(env.ExternsPath).Should().Contain("@provided-by: tyhpdef/acme-search");
    }

    [Fact]
    public void Source_WithoutComposerJson_DoesNotEmitExtern()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-src-no-pkg-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-src-no-pkg-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "A.php"), """
                <?php
                namespace Acme;
                class A {
                    public function f(\Acme\Search\Client $c): void {}
                }
                """);
            var options = new TyhpdefGenerationOptions
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "A.php")],
                OutputDirectory = outputDir,
                IncludeDocComments = false,
                Overwrite = true,
                Split = "file",
                FetchStubCache = false,
                StubCacheDirectory = Path.Combine(outputDir, "missing-stubs"),
                CatalogRoots = [],
            };
            var result = new TyhpdefGenerationResult();
            new NativeTyhpdefGenerator().Generate(options, result, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            File.Exists(Path.Combine(outputDir, TyhpdefOutputLayout.ExternsFileName)).Should().BeFalse();
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    private static string MainText(TyhpdefGenerationResult result)
    {
        var path = result.GeneratedFiles.First(p =>
            !p.EndsWith(TyhpdefOutputLayout.ExternsFileName, StringComparison.OrdinalIgnoreCase));
        return File.ReadAllText(path);
    }

    private static void AssertParses(string tyhpdef)
    {
        var parsed = ParserTestHelper.ParseTyhpdefContent(tyhpdef);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => e.Message)) + "\n" + tyhpdef);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class ExternTestEnv : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("tyhpdef-extern-").FullName;

        public string CatalogRoot => Path.Combine(this.Root, "catalog");

        public string PhpRoot => Path.Combine(this.Root, "php-package");

        public string WrapperRoot => Path.Combine(this.Root, "wrapper");

        public string OutputDir => Path.Combine(this.WrapperRoot, "_tyhpdef");

        public string ExternsPath => Path.Combine(this.OutputDir, TyhpdefOutputLayout.ExternsFileName);

        public ExternTestEnv()
        {
            Directory.CreateDirectory(this.CatalogRoot);
            Directory.CreateDirectory(Path.Combine(this.PhpRoot, "src"));
            Directory.CreateDirectory(this.OutputDir);
            File.WriteAllText(Path.Combine(this.WrapperRoot, "composer.json"), """
                {
                    "name": "tyhpdef/acme-lib",
                    "require": {
                        "php": ">=8.2"
                    }
                }
                """);
        }

        public void AddCatalogPackage(string tyhpName, string phpRequire, string tyhpdef)
        {
            var folder = tyhpName.Replace('/', '-');
            var dir = Path.Combine(this.CatalogRoot, folder);
            var tyhpdefDir = Path.Combine(dir, "_tyhpdef");
            Directory.CreateDirectory(tyhpdefDir);
            File.WriteAllText(Path.Combine(dir, "composer.json"), $$"""
                {
                    "name": "{{tyhpName}}",
                    "version": "1.0.0",
                    "require": {{phpRequire}},
                    "extra": {
                        "tyhp": {
                            "package": {
                                "include": ["./_tyhpdef/*.tyhpdef"]
                            }
                        }
                    }
                }
                """);
            File.WriteAllText(Path.Combine(tyhpdefDir, folder + ".tyhpdef"), tyhpdef);
        }

        public void AddPhpClass(string fileName, string php)
            => File.WriteAllText(Path.Combine(this.PhpRoot, "src", fileName), php);

        public void WriteTargetComposer(
            string require = """{ "php": ">=8.2" }""",
            string? suggest = null,
            string? requireDev = null)
        {
            var extra = new System.Text.StringBuilder();
            if (suggest is not null)
            {
                extra.Append(",\n    \"suggest\": ").Append(suggest);
            }

            if (requireDev is not null)
            {
                extra.Append(",\n    \"require-dev\": ").Append(requireDev);
            }

            var json = "{\n"
                + "    \"name\": \"acme/lib\",\n"
                + "    \"require\": " + require + extra + ",\n"
                + "    \"autoload\": {\n"
                + "        \"psr-4\": { \"Acme\\\\Lib\\\\\": \"src/\" }\n"
                + "    }\n"
                + "}\n";
            File.WriteAllText(Path.Combine(this.PhpRoot, "composer.json"), json);
        }

        public TyhpdefGenerationResult Generate()
        {
            var options = BaseOptions() with
            {
                Mode = TyhpdefGenerationMode.ComposerPackage,
                PackagePath = this.PhpRoot,
            };
            var result = new TyhpdefGenerationResult();
            new NativeTyhpdefGenerator().Generate(options, result, this.PhpRoot);
            return result;
        }

        public TyhpdefGenerationResult GenerateSource()
        {
            var options = BaseOptions() with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(this.PhpRoot, "src")],
            };
            var result = new TyhpdefGenerationResult();
            new NativeTyhpdefGenerator().Generate(options, result, this.PhpRoot);
            return result;
        }

        public string WrapperComposerJson()
            => File.ReadAllText(Path.Combine(this.WrapperRoot, "composer.json"));

        private TyhpdefGenerationOptions BaseOptions()
            => new()
            {
                OutputDirectory = this.OutputDir,
                IncludeDocComments = true,
                IncludeDeprecated = true,
                Overwrite = true,
                Split = "file",
                FetchStubCache = false,
                StubCacheDirectory = Path.Combine(this.Root, "missing-stubs"),
                CatalogRoots = [this.CatalogRoot],
            };

        public void Dispose()
        {
            try
            {
                Directory.Delete(this.Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
