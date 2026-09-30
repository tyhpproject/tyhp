using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Enums;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Microsoft.Extensions.Configuration;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class StubHarvestAuditServiceTests
{
    [Fact]
    public void JsonStubs_Layer2MatchesConsensus_AgreesOnReturn()
    {
        var root = CreateTree(
            layer1: """
                <?tyhpdef
                function json_decode(string $json): mixed;
                """,
            layer2: """
                <?tyhpdef
                function json_decode(string $json): array|object|null;
                """,
            stubCache: TyhpdefGenFixtures.JsonStubsPath);

        try
        {
            var facts = RunCollect(root, TyhpdefGenFixtures.JsonStubsPath);
            var ret = facts.Should().ContainSingle(f =>
                f.Fqn.Equals("\\json_decode", StringComparison.OrdinalIgnoreCase)
                && f.Fact == "return").Subject;
            ret.Bucket.Should().Be(StubAuditBucket.Agree);
            StubHarvestAuditService.SameSignature(ret.Layer2, ret.Harvest).Should().BeTrue();
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PsalmPhpStanDisagree_IsItsOwnBucket()
    {
        var root = CreateTree(
            layer1: """
                <?tyhpdef
                function json_decode(string $json): mixed;
                """,
            layer2: null,
            stubCache: TyhpdefGenFixtures.DisagreeStubsPath);

        try
        {
            var facts = RunCollect(root, TyhpdefGenFixtures.DisagreeStubsPath);
            var ret = facts.Should().ContainSingle(f =>
                f.Fqn.Equals("\\json_decode", StringComparison.OrdinalIgnoreCase)
                && f.Fact == "return").Subject;
            ret.Bucket.Should().Be(StubAuditBucket.PsalmPhpStanDisagree);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void TemplateOrderMismatch_SameNamesDifferentSequence()
    {
        var stubCache = Directory.CreateTempSubdirectory("tyhp-audit-order-").FullName;
        var root = Directory.CreateTempSubdirectory("tyhp-audit-order-def-").FullName;
        try
        {
            WriteStub(stubCache, "psalm", "fiber.phpstub", """
                <?php
                /**
                 * @template TStart
                 * @template TResume
                 * @template TReturn
                 * @template TSuspend
                 */
                class Fiber {}
                """);
            WriteStub(stubCache, "phpstan", "fiber.stub", """
                <?php
                /**
                 * @template TStart
                 * @template TReturn
                 * @template TSuspend
                 * @template TResume
                 */
                class Fiber {}
                """);

            Directory.CreateDirectory(Path.Combine(root, "overlays", "stubs"));
            File.WriteAllText(Path.Combine(root, "Ext.Core.tyhpdef"), """
                <?tyhpdef
                class Fiber {
                }
                """);
            File.WriteAllText(Path.Combine(root, "overlays", "stubs", "Ext.Core.tyhpdef"), """
                <?tyhpdef
                partial class Fiber<TStart, TResume, TReturn, TSuspend>;
                """);

            var facts = RunCollect(root, stubCache);
            facts.Should().Contain(f =>
                f.Fqn.Equals("\\Fiber", StringComparison.OrdinalIgnoreCase)
                && f.Fact == "generics"
                && f.Bucket == StubAuditBucket.TemplateOrderMismatch);
        }
        finally
        {
            TryDelete(root);
            TryDelete(stubCache);
        }
    }

    [Fact]
    public void FaithfulnessMismatch_Layer2DiffersFromConsensus()
    {
        var root = CreateTree(
            layer1: """
                <?tyhpdef
                function json_decode(string $json): mixed;
                """,
            layer2: """
                <?tyhpdef
                function json_decode(string $json): array;
                """,
            stubCache: TyhpdefGenFixtures.JsonStubsPath);

        try
        {
            var facts = RunCollect(root, TyhpdefGenFixtures.JsonStubsPath);
            var ret = facts.Should().ContainSingle(f =>
                f.Fqn.Equals("\\json_decode", StringComparison.OrdinalIgnoreCase)
                && f.Fact == "return").Subject;
            ret.Bucket.Should().Be(StubAuditBucket.FaithfulnessMismatch);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Audit_WritesMarkdownAndDoesNotFailOnReviewBuckets()
    {
        var root = CreateTree(
            layer1: """
                <?tyhpdef
                function json_decode(string $json): mixed;
                """,
            layer2: """
                <?tyhpdef
                function json_decode(string $json): array|object|null;
                """,
            stubCache: TyhpdefGenFixtures.JsonStubsPath);
        var outFile = Path.Combine(root, "audit.md");
        try
        {
            var options = new TyhpdefGenerationOptions
            {
                FetchStubCache = false,
                RequireStubs = true,
                StubCacheDirectory = TyhpdefGenFixtures.JsonStubsPath,
                AuditOutPath = outFile,
            };
            var result = new TyhpdefGenerationResult();
            new StubHarvestAuditService().Audit(root, options, result, quiet: true);

            result.Success.Should().BeTrue(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            File.Exists(outFile).Should().BeTrue();
            var report = File.ReadAllText(outFile);
            report.Should().Contain("# Layer 2 stub audit");
            report.Should().Contain("`agree`");
            result.AuditReport.Should().Be(report);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static List<StubAuditFact> RunCollect(string root, string stubCache)
    {
        var cacheDir = StubCorpusCache.ResolveDirectory(new TyhpdefGenerationOptions
        {
            StubCacheDirectory = stubCache,
            FetchStubCache = false,
        });
        cacheDir.Should().NotBeNull();
        var index = StubHarvestTyhpdefEnricher.IndexCorpora(StubCorpusCache.CorpusDirectories(cacheDir!));
        var layer1 = StubAuditTyhpdefCatalog.ReadFiles(
            StubHarvestAuditService.DiscoverLayer1(root),
            new DiagnosticBag());
        var layer2 = StubAuditTyhpdefCatalog.ReadFiles(
            StubHarvestAuditService.DiscoverLayer2(root),
            new DiagnosticBag());
        return StubHarvestAuditService.CollectFacts(layer1, layer2, index);
    }

    private static string CreateTree(string layer1, string? layer2, string stubCache)
    {
        _ = stubCache;
        var root = Directory.CreateTempSubdirectory("tyhp-audit-tree-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "overlays", "stubs"));
        File.WriteAllText(Path.Combine(root, "Ext.Json.tyhpdef"), layer1);
        if (layer2 is not null)
        {
            File.WriteAllText(Path.Combine(root, "overlays", "stubs", "Ext.Json.tyhpdef"), layer2);
        }

        return root;
    }

    private static void WriteStub(string cache, string corpus, string fileName, string contents)
    {
        var dir = Path.Combine(cache, corpus);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, fileName), contents);
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
}

[Trait("Category", "CLI")]
[Collection("ProcessGlobalState")]
public class GenerateTyhpdefAuditStubsCliTests
{
    [Fact]
    public void AuditStubs_WithExtName_IsExclusive()
    {
        var result = Run(new Dictionary<string, string?>
        {
            ["audit-stubs"] = "/tmp/tyhpdefs",
            ["ext-name"] = "json",
        });

        result.Success.Should().BeFalse();
        result.Diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.TyhpdefGenerationError
            && d.Message.Contains("`--audit-stubs`", StringComparison.Ordinal));
    }

    [Fact]
    public void AuditStubs_MissingPath_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "tyhp-audit-missing-" + Guid.NewGuid().ToString("N"));
        var result = Run(new Dictionary<string, string?>
        {
            ["audit-stubs"] = missing,
        });

        result.Success.Should().BeFalse();
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefGenerationError);
    }

    private static TyhpdefGenerationResult Run(Dictionary<string, string?> settings)
    {
        var values = new Dictionary<string, string?>(settings) { ["quiet"] = "true" };
        var project = new Project(new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build());
        Environment.ExitCode = (int)ExitCode.Success;
        using var action = new GenerateTyhpdefAction(project);
        action.Start(CancellationToken.None);
        return action.LastResult!;
    }
}
