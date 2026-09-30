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

    public static string GetEmitAndRunDirectory()
        => Path.Combine(GetConformanceDirectory(), "emit-and-run");

    public static IEnumerable<string> GetAllConformanceManifests()
    {
        var root = GetConformanceDirectory();
        if (!Directory.Exists(root))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
            .Where(path => !IsSelfHostConformancePath(path) && !IsEmitAndRunConformancePath(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<string> GetEmitAndRunManifests()
    {
        var root = GetEmitAndRunDirectory();
        if (!Directory.Exists(root))
        {
            return Enumerable.Empty<string>();
        }

        return Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSelfHostConformancePath(string path)
        => path.Contains(
            $"{Path.DirectorySeparatorChar}_self_host{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsEmitAndRunConformancePath(string path)
        => path.Contains(
            $"{Path.DirectorySeparatorChar}emit-and-run{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                Path.GetFileName(Path.GetDirectoryName(path)),
                "emit-and-run",
                StringComparison.OrdinalIgnoreCase);

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
