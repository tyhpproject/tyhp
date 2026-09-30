using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;

namespace Tyhp.Tests.TestHelpers;

public sealed class TestProjectBuilder : IDisposable
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _configOverrides = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public TestProjectBuilder(string? projectName = null)
    {
        ProjectDirectory = Path.Combine(
            Path.GetTempPath(),
            "tyhp-test-project",
            projectName ?? Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ProjectDirectory);
        ProjectFilePath = Path.Combine(ProjectDirectory, "tyhp.json");
    }

    public string ProjectDirectory { get; }

    public string ProjectFilePath { get; }

    public TestProjectBuilder WithTyhpJson(string json)
    {
        _files["tyhp.json"] = json;
        return this;
    }

    public TestProjectBuilder WithDefaultTyhpJson(string outputPath = "build/")
    {
        return WithTyhpJson($$"""
            {
                "include": ["**/*.tyhp"],
                "output": { "path": "{{outputPath}}" }
            }
            """);
    }

    public TestProjectBuilder WithTyhpFile(string relativePath, string content)
    {
        _files[relativePath.Replace('\\', '/')] = content;
        return this;
    }

    /// <summary>
    /// Writes <c>{relativeDir}/composer.json</c> with <c>extra.tyhp.package</c> set to
    /// <paramref name="packageObjectJson"/> (the include / overlay / exclude spec).
    /// </summary>
    public TestProjectBuilder WithTyhpdefPackageComposer(
        string relativeDir,
        string packageObjectJson,
        string? packageName = null)
    {
        var dir = relativeDir.Replace('\\', '/').Trim('/');
        var name = packageName ?? ("acme/" + (string.IsNullOrEmpty(dir) ? "pkg" : dir.Replace('/', '-')));
        var path = string.IsNullOrEmpty(dir) ? "composer.json" : dir + "/composer.json";
        return WithTyhpFile(path, $$"""
            {
                "name": "{{name}}",
                "extra": {
                    "tyhp": {
                        "package": {{packageObjectJson.Trim()}}
                    }
                }
            }
            """);
    }

    public TestProjectBuilder WithConfigValue(string key, string? value)
    {
        _configOverrides[key] = value;
        return this;
    }

    public Project BuildProject()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WriteFiles();

        var configValues = new Dictionary<string, string?>(_configOverrides)
        {
            ["*project_file_path"] = ProjectFilePath,
            ["clean"] = "true",
            ["build:dryRun"] = "false",
            ["no-cache"] = "true",
        };
        if (!configValues.ContainsKey("cache-dir"))
        {
            configValues["cache-dir"] = Path.Combine(ProjectDirectory, ".tyhp-cache");
        }

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(ProjectFilePath, optional: false)
            .AddInMemoryCollection(configValues)
            .Build();

        return new Project(configuration);
    }

    public CompilationResult RunBuild(CancellationToken cancellationToken = default)
    {
        var project = BuildProject();
        IncrementalBuildService.DeleteBuildState(IncrementalBuildService.GetBuildStatePath(project));

        // The real CLI host (TyhpHostedService) publishes the active Project to
        // Project.Singleton before compiling; AstCacheService.GetRelativePath falls back to it
        // to resolve project-relative source paths (e.g. sourcemap sourceRoot). RunBuild drives
        // BuildAction directly (bypassing the host), so it must publish/restore Singleton itself
        // or relative-path fallbacks resolve against the test runner's CWD instead of
        // ProjectDirectory.
        var previousSingleton = Project.Singleton;
        Project.Singleton = project;
        try
        {
            var action = new BuildAction(project);
            return action.Start(cancellationToken) ?? new CompilationResult();
        }
        finally
        {
            Project.Singleton = previousSingleton;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Directory.Delete(ProjectDirectory, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }

    private void WriteFiles()
    {
        if (!_files.ContainsKey("tyhp.json"))
        {
            WithDefaultTyhpJson();
        }

        SeedInstalledJsonFromComposerManifest();

        foreach (var (relativePath, content) in _files)
        {
            var fullPath = Path.Combine(ProjectDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
        }
    }

    /// <summary>
    /// Fixtures often write a synthetic root <c>composer.json</c> (including
    /// <c>extra.tyhp.require</c>) without running <c>composer install</c>. Seed a Composer 2
    /// <c>vendor/composer/installed.json</c> so <see cref="ComposerExtraRequireCheck"/> sees
    /// those pins as installed. Explicit fixture <c>installed.json</c> is left alone so stale-extra
    /// tests keep failing the production check.
    ///
    /// <para>
    /// Opt-out: a fixture that wants to exercise the real "missing vendor" diagnostic (e.g. a
    /// <c>tyhp/</c>-vendor package named in <c>require</c>/<c>require-dev</c> that is genuinely
    /// not installed) must call <c>WithTyhpFile("vendor/composer/installed.json", ...)</c> itself
    /// — even an empty <c>{ "packages": [] }</c> — before <see cref="BuildProject"/>/
    /// <see cref="RunBuild"/> runs; that disables this auto-seed. See
    /// <c>ComposerExtraRequireCheckTests.Build_MissingVendor_RealTyhpPackage_StillFailsWhenSeedIsOptedOut</c>.
    /// </para>
    /// </summary>
    private void SeedInstalledJsonFromComposerManifest()
    {
        const string installedPath = "vendor/composer/installed.json";
        if (!_files.TryGetValue("composer.json", out var composerJson)
            || _files.ContainsKey(installedPath))
        {
            return;
        }

        var seeded = TryBuildComposer2InstalledJson(composerJson);
        if (seeded is null)
        {
            return;
        }

        _files[installedPath] = seeded;
    }

    private static string? TryBuildComposer2InstalledJson(string composerJson)
    {
        JsonObject root;
        try
        {
            if (JsonNode.Parse(composerJson) is not JsonObject parsed)
            {
                return null;
            }

            root = parsed;
        }
        catch (JsonException)
        {
            return null;
        }

        var require = ComposerExtraRequireGraph.ReadConstraintMap(root["require"] as JsonObject);
        var requireDev = ComposerExtraRequireGraph.ReadConstraintMap(root["require-dev"] as JsonObject);
        var extras = ComposerExtraRequireGraph.ReadExtraTyhpRequire(root);

        var packages = new JsonArray();
        var devPackageNames = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string constraint, bool dev)
        {
            name = ComposerExtraRequireGraph.NormalizeName(name);
            if (name.Length == 0
                || ComposerPlatformPackages.IsPlatform(name)
                || name.IndexOf('/') <= 0
                || name.IndexOf('/') == name.Length - 1
                || !seen.Add(name))
            {
                return;
            }

            var slash = name.IndexOf('/');
            packages.Add(new JsonObject
            {
                ["name"] = name,
                ["version"] = InstalledVersionFromConstraint(constraint),
                ["install-path"] = $"../{name[..slash]}/{name[(slash + 1)..]}",
            });

            if (dev)
            {
                devPackageNames.Add(name);
            }
        }

        foreach (var (name, constraint) in require)
        {
            Add(name, constraint, dev: false);
        }

        foreach (var (name, constraint) in requireDev)
        {
            Add(name, constraint, dev: true);
        }

        foreach (var (name, constraint) in extras)
        {
            Add(name, constraint, dev: true);
        }

        if (packages.Count == 0)
        {
            return null;
        }

        var payload = new JsonObject
        {
            ["packages"] = packages,
            ["dev"] = true,
            ["dev-package-names"] = devPackageNames,
        };

        return payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string InstalledVersionFromConstraint(string constraint)
    {
        constraint = constraint.Trim();
        if (constraint.Length == 0
            || constraint == "*"
            || constraint == "@dev"
            || constraint.EndsWith("@dev", StringComparison.Ordinal))
        {
            return "dev-main";
        }

        if (constraint.StartsWith("dev-", StringComparison.Ordinal))
        {
            return constraint;
        }

        var stripped = constraint.TrimStart('^', '~', '=', '>', '<', ' ');
        return string.IsNullOrWhiteSpace(stripped) ? "dev-main" : stripped;
    }
}
