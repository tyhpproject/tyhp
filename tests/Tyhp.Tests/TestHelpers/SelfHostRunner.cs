using Microsoft.Extensions.Configuration;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;

namespace Tyhp.Tests.TestHelpers;

public static class SelfHostRunner
{
    private static readonly string[] RuntimePackageNames = ["core", "decimal", "async", "lambda"];

    /// <summary>
    /// Runtime packages that must compile and match committed <c>src/</c> PHP output.
    /// Grow this set as packages begin to self-compile; keep empty while none build yet.
    /// </summary>
    public static IReadOnlySet<string> ExpectedToCompileAllowlist { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SelfHostPackageResult> VerifyAllPackages()
    {
        var results = new List<SelfHostPackageResult>();
        foreach (var packageName in RuntimePackageNames)
        {
            results.Add(VerifyPackage(packageName));
        }

        return results;
    }

    public static SelfHostPackageResult VerifyPackage(string packageName)
    {
        var packageDirectory = Path.Combine(TestFileManager.GetRuntimePackagesDirectory(), packageName);
        var tyhpJsonPath = Path.Combine(packageDirectory, "tyhp.json");
        var sourceDirectory = Path.Combine(packageDirectory, "tyhp_src");
        var committedOutputDirectory = Path.Combine(packageDirectory, "src");

        if (!File.Exists(tyhpJsonPath))
        {
            return SelfHostPackageResult.MissingConfig(packageName, tyhpJsonPath);
        }

        if (!Directory.Exists(sourceDirectory))
        {
            return SelfHostPackageResult.MissingSources(packageName, sourceDirectory);
        }

        if (!Directory.Exists(committedOutputDirectory))
        {
            return SelfHostPackageResult.MissingCommittedOutput(packageName, committedOutputDirectory);
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), "tyhp-self-host", packageName, Guid.NewGuid().ToString("N"));
        var generatedOutputDirectory = Path.Combine(tempDirectory, "src");

        try
        {
            Directory.CreateDirectory(tempDirectory);
            CopyDirectory(packageDirectory, tempDirectory, excludeDirectories: ["src", "tests", "vendor", "build", ".phpunit.cache"]);

            var projectFile = Path.Combine(tempDirectory, "tyhp.json");
            var projectJson = File.ReadAllText(projectFile);
            if (!projectJson.Contains("\"path\"", StringComparison.Ordinal))
            {
                projectJson = projectJson.TrimEnd().TrimEnd('}') + ",\n  \"output\": { \"path\": \"./src\" }\n}";
                File.WriteAllText(projectFile, projectJson);
            }

            var configuration = new ConfigurationBuilder()
                .AddJsonFile(projectFile, optional: false)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["*project_file_path"] = projectFile,
                    ["clean"] = "true",
                    ["build:dryRun"] = "false",
                })
                .Build();

            var project = new Project(configuration);
            var buildResult = new BuildAction(project).Start(CancellationToken.None) ?? new CompilationResult();
            if (buildResult.Diagnostics.HasErrors)
            {
                return SelfHostPackageResult.BuildFailed(packageName, buildResult.Diagnostics);
            }

            var mismatches = ComparePhpTrees(generatedOutputDirectory, committedOutputDirectory);
            return mismatches.Count == 0
                ? SelfHostPackageResult.Success(packageName)
                : SelfHostPackageResult.DiffMismatch(packageName, mismatches);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    private static List<string> ComparePhpTrees(string generatedRoot, string committedRoot)
    {
        var mismatches = new List<string>();
        var committedFiles = Directory
            .EnumerateFiles(committedRoot, "*.php", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(committedRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var relativePath in committedFiles)
        {
            var generatedPath = Path.Combine(generatedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var committedPath = Path.Combine(committedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(generatedPath))
            {
                mismatches.Add($"missing generated file: {relativePath}");
                continue;
            }

            var generated = NormalizePhp(File.ReadAllText(generatedPath));
            var committed = NormalizePhp(File.ReadAllText(committedPath));
            if (!string.Equals(generated, committed, StringComparison.Ordinal))
            {
                mismatches.Add($"content mismatch: {relativePath}");
            }
        }

        var generatedOnly = Directory
            .EnumerateFiles(generatedRoot, "*.php", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(generatedRoot, path).Replace('\\', '/'))
            .Except(committedFiles, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var relativePath in generatedOnly)
        {
            mismatches.Add($"unexpected generated file: {relativePath}");
        }

        return mismatches;
    }

    private static string NormalizePhp(string content)
        => content.Replace("\r\n", "\n").TrimEnd() + "\n";

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, IEnumerable<string> excludeDirectories)
    {
        var excluded = new HashSet<string>(excludeDirectories, StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            var topLevel = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (topLevel != null && excluded.Contains(topLevel))
            {
                continue;
            }

            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var topLevel = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (topLevel != null && excluded.Contains(topLevel))
            {
                continue;
            }

            var destination = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    public static bool IsBuildFailure(SelfHostPackageResult result)
        => result.Summary.StartsWith("build failed", StringComparison.OrdinalIgnoreCase);

    public static bool IsInfrastructureFailure(SelfHostPackageResult result)
        => result.Summary.StartsWith("missing ", StringComparison.OrdinalIgnoreCase);

    public static bool CompiledSuccessfully(SelfHostPackageResult result)
        => !IsBuildFailure(result) && !IsInfrastructureFailure(result);

    public sealed record SelfHostPackageResult(
        string PackageName,
        bool Succeeded,
        string Summary,
        IReadOnlyList<string> Details)
    {
        public static SelfHostPackageResult Success(string packageName)
            => new(packageName, true, "generated PHP matches committed output", Array.Empty<string>());

        public static SelfHostPackageResult MissingConfig(string packageName, string path)
            => new(packageName, false, $"missing tyhp.json at {path}", Array.Empty<string>());

        public static SelfHostPackageResult MissingSources(string packageName, string path)
            => new(packageName, false, $"missing tyhp_src at {path}", Array.Empty<string>());

        public static SelfHostPackageResult MissingCommittedOutput(string packageName, string path)
            => new(packageName, false, $"missing committed src at {path}", Array.Empty<string>());

        public static SelfHostPackageResult BuildFailed(string packageName, DiagnosticBag diagnostics)
            => new(
                packageName,
                false,
                $"build failed with {diagnostics.ErrorCount} error(s)",
                diagnostics.Errors
                    .Take(20)
                    .Select(d => $"{d.FileName}({d.Line},{d.Column}): {d.Code} {d.Message}")
                    .ToList());

        public static SelfHostPackageResult DiffMismatch(string packageName, IReadOnlyList<string> mismatches)
            => new(packageName, false, $"{mismatches.Count} PHP file difference(s)", mismatches);
    }
}
