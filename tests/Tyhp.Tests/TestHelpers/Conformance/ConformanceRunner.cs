using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;

namespace Tyhp.Tests.TestHelpers.Conformance;

public static class ConformanceRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IEnumerable<object[]> DiscoverAllCases()
    {
        foreach (var manifestPath in TestFileManager.GetAllConformanceManifests())
        {
            var manifest = LoadManifest(manifestPath);
            foreach (var testCase in manifest.Cases)
            {
                if (!string.IsNullOrWhiteSpace(testCase.Skip))
                {
                    continue;
                }

                yield return new object[] { manifest.Suite, testCase.Id };
            }
        }
    }

    public static void RunAndAssert(string suiteId, string caseId)
    {
        var manifestPath = FindManifestPath(suiteId);
        var manifest = LoadManifest(manifestPath);
        var testCase = manifest.Cases.Single(c => string.Equals(c.Id, caseId, StringComparison.Ordinal));

        var suiteDirectory = Path.GetDirectoryName(manifestPath)!;
        var inputPath = Path.Combine(suiteDirectory, testCase.File);
        File.Exists(inputPath).Should().BeTrue($"conformance input file should exist for case '{caseId}'");

        var action = testCase.Action ?? manifest.Defaults?.Action ?? "lint";
        var config = MergeConfig(manifest.Defaults?.Config, testCase.Config);

        if (string.Equals(action, "lint", StringComparison.OrdinalIgnoreCase))
        {
            var result = RunLint(inputPath, config);
            DiagnosticAssertions.AssertExpectations(result.Diagnostics, testCase.Expect);
            return;
        }

        if (string.Equals(action, "build", StringComparison.OrdinalIgnoreCase))
        {
            var result = RunBuild(suiteDirectory, config);
            DiagnosticAssertions.AssertExpectations(result.Diagnostics, testCase.Expect);
            AssertPhpExpectation(suiteDirectory, testCase.Expect);
            return;
        }

        throw new InvalidOperationException($"Unsupported conformance action '{action}'.");
    }

    public static ConformanceManifest LoadManifest(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ConformanceManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize conformance manifest: {manifestPath}");

        if (string.IsNullOrWhiteSpace(manifest.Suite))
        {
            var relative = Path.GetRelativePath(TestFileManager.GetConformanceDirectory(), Path.GetDirectoryName(manifestPath)!);
            manifest.Suite = relative.Replace(Path.DirectorySeparatorChar, '/');
        }

        return manifest;
    }

    private static string FindManifestPath(string suiteId)
    {
        var path = Path.Combine(TestFileManager.GetConformanceDirectory(), suiteId.Replace('/', Path.DirectorySeparatorChar), "manifest.json");
        File.Exists(path).Should().BeTrue($"manifest should exist for suite '{suiteId}'");
        return path;
    }

    private static CompilationResult RunLint(string inputPath, IReadOnlyDictionary<string, JsonElement> config)
    {
        var options = TestFileManager.CreateRepoRootCompilationOptions(
            phpVersion: ReadString(config, "phpVersion") ?? "8.2",
            enableAstCache: false,
            configure: o =>
            {
                o.Tagless = ReadBool(config, "source.tagless") ?? false;
            });

        using var compilationService = new CompilationService();
        return compilationService.ParseFiles(new[] { inputPath }, options);
    }

    private static CompilationResult RunBuild(string suiteDirectory, IReadOnlyDictionary<string, JsonElement> config)
    {
        var projectFile = Path.Combine(suiteDirectory, "tyhp.json");
        File.Exists(projectFile).Should().BeTrue($"build conformance suite must include tyhp.json at {projectFile}");

        var buildStatePath = Path.Combine(suiteDirectory, IncrementalBuildService.BuildStateFileName);
        if (File.Exists(buildStatePath))
        {
            File.Delete(buildStatePath);
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(projectFile, optional: false)
            .AddInMemoryCollection(BuildConfigOverrides(config, projectFile))
            .Build();

        var project = new Project(configuration);
        return new BuildAction(project).Start(CancellationToken.None) ?? new CompilationResult();
    }

    private static Dictionary<string, string?> BuildConfigOverrides(
        IReadOnlyDictionary<string, JsonElement> config,
        string projectFile)
    {
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["*project_file_path"] = projectFile,
            ["clean"] = "true",
            ["build:dryRun"] = "false",
        };

        foreach (var entry in config)
        {
            overrides[entry.Key] = entry.Value.ValueKind switch
            {
                JsonValueKind.String => entry.Value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => entry.Value.GetRawText(),
                _ => null,
            };
        }

        return overrides;
    }

    private static void AssertPhpExpectation(string suiteDirectory, ConformanceExpectation? expectation)
    {
        if (expectation?.Php is not { Length: > 0 } relativePhpPath)
        {
            return;
        }

        var expectedPath = Path.Combine(suiteDirectory, relativePhpPath);
        File.Exists(expectedPath).Should().BeTrue($"expected PHP file should exist at {relativePhpPath}");

        var projectFile = Path.Combine(suiteDirectory, "tyhp.json");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(projectFile, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["*project_file_path"] = projectFile })
            .Build();
        var project = new Project(configuration);
        var outputRoot = Path.GetFullPath(Path.Combine(suiteDirectory, project.Output.Path));
        var expectedRelative = Path.GetRelativePath(suiteDirectory, expectedPath).Replace('\\', '/');
        var generatedRelative = expectedRelative.StartsWith("expected/", StringComparison.OrdinalIgnoreCase)
            ? expectedRelative["expected/".Length..]
            : Path.GetFileName(expectedRelative);
        var generatedPath = Path.Combine(outputRoot, generatedRelative.Replace('/', Path.DirectorySeparatorChar));

        File.Exists(generatedPath).Should().BeTrue($"generated PHP should exist at {generatedPath}");
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n").TrimEnd() + "\n";
        var actual = File.ReadAllText(generatedPath).Replace("\r\n", "\n").TrimEnd() + "\n";
        actual.Should().Be(expected, $"generated PHP should match golden file {relativePhpPath}");
    }

    private static Dictionary<string, JsonElement> MergeConfig(
        Dictionary<string, JsonElement>? defaults,
        Dictionary<string, JsonElement>? overrides)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (defaults != null)
        {
            foreach (var entry in defaults)
            {
                merged[entry.Key] = entry.Value;
            }
        }

        if (overrides != null)
        {
            foreach (var entry in overrides)
            {
                merged[entry.Key] = entry.Value;
            }
        }

        return merged;
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonElement> config, string key)
    {
        if (!config.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, JsonElement> config, string key)
    {
        if (!config.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) ? parsed : null,
            _ => null,
        };
    }
}
