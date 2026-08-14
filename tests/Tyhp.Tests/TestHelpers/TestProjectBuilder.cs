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
        };

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(ProjectFilePath, optional: false)
            .AddInMemoryCollection(configValues)
            .Build();

        return new Project(configuration);
    }

    public CompilationResult RunBuild(CancellationToken cancellationToken = default)
    {
        var buildStatePath = Path.Combine(ProjectDirectory, IncrementalBuildService.BuildStateFileName);
        if (File.Exists(buildStatePath))
        {
            File.Delete(buildStatePath);
        }

        var project = BuildProject();
        var action = new BuildAction(project);
        return action.Start(cancellationToken) ?? new CompilationResult();
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
}
