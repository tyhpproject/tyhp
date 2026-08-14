using Tyhp.Domain.Services;

namespace Tyhp.Tests.TestHelpers;

public static class TestFileManager
{
    private static readonly Lazy<string> RepoRoot = new(ResolveRepoRoot);

    public static string GetRepoRoot() => RepoRoot.Value;

    public static string GetTestProjectDirectory()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    public static string GetTestDataDirectory()
        => Path.Combine(GetTestProjectDirectory(), "TestData");

    public static string GetSnapshotsDirectory()
        => Path.Combine(GetTestProjectDirectory(), "Snapshots");

    public static string GetConformanceDirectory()
        => Path.Combine(GetRepoRoot(), "tests", "conformance");

    public static string GetExtensionTyhpdefDirectory()
        => Path.Combine(GetRepoRoot(), "runtime", "php-extensions");

    public static string GetRuntimePackagesDirectory()
        => Path.Combine(GetRepoRoot(), "runtime", "packages");

    /// <summary>
    /// Explicit <c>package.tyhp.json</c> includes for local ExtCore + runtime packages.
    /// Tests that set <see cref="CompilationOptions.ProjectPath"/> to the repo root must
    /// also set <see cref="CompilationOptions.TyhpdefIncludePaths"/> to this list — the
    /// compiler no longer auto-scans <c>runtime/</c>.
    /// </summary>
    public static IReadOnlyList<string> GetDevPackageManifestIncludes() =>
    [
        Path.Combine("runtime", "packages", "php", "package.tyhp.json"),
        Path.Combine("runtime", "packages", "core", "package.tyhp.json"),
        Path.Combine("runtime", "packages", "decimal", "package.tyhp.json"),
        Path.Combine("runtime", "packages", "async", "package.tyhp.json"),
        Path.Combine("runtime", "packages", "lambda", "package.tyhp.json"),
    ];

    /// <summary>
    /// Builds <see cref="CompilationOptions"/> rooted at the repo with explicit package
    /// includes (vendor + <see cref="GetDevPackageManifestIncludes"/>).
    /// </summary>
    public static CompilationOptions CreateRepoRootCompilationOptions(
        string phpVersion = "8.2",
        bool enableAstCache = false,
        bool skipChecking = false,
        Action<CompilationOptions>? configure = null)
    {
        var options = new CompilationOptions
        {
            EnableAstCache = enableAstCache,
            PhpVersion = phpVersion,
            ProjectPath = GetRepoRoot(),
            SkipChecking = skipChecking,
            TyhpdefIncludePaths = GetDevPackageManifestIncludes(),
        };
        configure?.Invoke(options);
        return options;
    }

    public static IEnumerable<string> GetAllExtensionTyhpdefFiles()
        => Directory.Exists(GetExtensionTyhpdefDirectory())
            ? Directory.EnumerateFiles(GetExtensionTyhpdefDirectory(), "*.tyhpdef", SearchOption.AllDirectories)
            : Enumerable.Empty<string>();

    public static IEnumerable<string> GetAllRuntimePackageTyhpdefFiles()
    {
        var packagesDir = GetRuntimePackagesDirectory();
        if (!Directory.Exists(packagesDir))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.EnumerateFiles(packagesDir, "*.tyhpdef", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> GetAllTestDataFiles(string subdirectory, string extension)
    {
        var directory = Path.Combine(GetTestDataDirectory(), subdirectory);
        if (!Directory.Exists(directory))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.EnumerateFiles(directory, $"*{extension}", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> GetAllConformanceManifests()
    {
        var root = GetConformanceDirectory();
        if (!Directory.Exists(root))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_self_host{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveRepoRoot()
    {
        var directory = new DirectoryInfo(GetTestProjectDirectory());
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tyhp.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing tyhp.csproj.");
    }
}
