using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class SourceMapStoreTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    [Fact]
    public void LoadAll_DiscoversMapsRecursivelyAndIndexesThem()
    {
        string root = CreateTempDirectory();
        string nested = Path.Combine(root, "Nested");
        Directory.CreateDirectory(nested);
        WriteMap(Path.Combine(root, "App.php.map"), file: "App.php", source: "App.tyhp");
        WriteMap(Path.Combine(nested, "User.php.map"), file: "User.php", source: "Models/User.tyhp");

        using var store = new SourceMapStore(root) { AutoReload = false };
        store.LoadAll();

        store.Warnings.Should().BeEmpty();
        store.LoadedMaps.Should().HaveCount(2);

        store.GetMapForPhpFile(Path.Combine(root, "App.php")).Should().NotBeNull();
        store.GetMapForPhpFile(Path.Combine(nested, "User.php")).Should().NotBeNull();
        store.GetMapForPhpFile("User.php").Should().NotBeNull();

        store.GetMapForTyhpFile("App.tyhp").Should().ContainSingle();
        store.GetMapForTyhpFile("Models/User.tyhp").Should().ContainSingle();
        store.GetMapForTyhpFile("Models\\User.tyhp").Should().ContainSingle();
    }

    [Fact]
    public void GetMapForPhpFile_FilenameOnlyFileField_MatchesRelativeAndBackslashPaths()
    {
        string root = CreateTempDirectory();
        WriteMap(Path.Combine(root, "App.php.map"), file: "App.php", source: "src/App.tyhp");

        using var store = new SourceMapStore(root) { AutoReload = false };
        store.LoadAll();

        store.GetMapForPhpFile("App.php")!.File.Should().Be("App.php");
        store.GetMapForPhpFile(Path.Combine(root, "App.php"))!.File.Should().Be("App.php");
        store.GetMapForPhpFile(root.Replace('/', '\\') + "\\App.php")!.File.Should().Be("App.php");
    }

    [Fact]
    public void GetMapForTyhpFile_ReturnsAllMapsThatReferenceTheSource()
    {
        string root = CreateTempDirectory();
        WriteMap(Path.Combine(root, "A.php.map"), file: "A.php", source: "Shared.tyhp");
        WriteMap(Path.Combine(root, "B.php.map"), file: "B.php", source: "Shared.tyhp");

        using var store = new SourceMapStore(root) { AutoReload = false };
        store.LoadAll();

        store.GetMapForTyhpFile("Shared.tyhp").Should().HaveCount(2);
    }

    [Fact]
    public void Lookups_WithManyMaps_StillResolveAfterIndexing()
    {
        string root = CreateTempDirectory();
        const int count = 80;
        for (int i = 0; i < count; i++)
        {
            WriteMap(
                Path.Combine(root, $"File{i}.php.map"),
                file: $"File{i}.php",
                source: $"src/File{i}.tyhp");
        }

        using var store = new SourceMapStore(root) { AutoReload = false };
        store.LoadAll();

        store.LoadedMaps.Should().HaveCount(count);
        store.GetMapForPhpFile(Path.Combine(root, "File0.php")).Should().NotBeNull();
        store.GetMapForPhpFile(Path.Combine(root, "File79.php"))!.File.Should().Be("File79.php");
        store.GetMapForPhpFile("File42.php").Should().NotBeNull();
        store.GetMapForTyhpFile("src/File0.tyhp").Should().ContainSingle();
        store.GetMapForTyhpFile("src/File79.tyhp").Should().ContainSingle();
        store.GetMapForTyhpFile("File42.tyhp").Should().ContainSingle();
        store.GetMapForPhpFile(Path.Combine(root, "Missing.php")).Should().BeNull();
        store.GetMapForTyhpFile("src/Missing.tyhp").Should().BeEmpty();
    }

    [Fact]
    public void LoadAll_MissingAndMalformedMaps_RecordWarningsAndDoNotThrow()
    {
        string root = CreateTempDirectory();
        File.WriteAllText(Path.Combine(root, "Bad.php.map"), "{not json");
        File.WriteAllText(Path.Combine(root, "Empty.php.map"), "{}");
        string missingExplicit = Path.Combine(root, "Missing.php.map");

        var callbackWarnings = new List<SourceMapLoadWarning>();
        using var store = new SourceMapStore(
            root,
            explicitMapPaths: [missingExplicit],
            onWarning: callbackWarnings.Add)
        {
            AutoReload = false,
        };

        Action act = () => store.LoadAll();
        act.Should().NotThrow();

        store.LoadedMaps.Should().BeEmpty();
        store.Warnings.Should().NotBeEmpty();
        store.Warnings.Should().Contain(w => w.Kind == SourceMapLoadWarningKind.MapFileMalformed);
        store.Warnings.Should().Contain(w => w.Kind == SourceMapLoadWarningKind.MapFileMissing);
        callbackWarnings.Should().HaveCount(store.Warnings.Count);

        store.GetMapForPhpFile(Path.Combine(root, "Bad.php")).Should().BeNull();
    }

    [Fact]
    public void LoadAll_MissingRootDirectory_WarnsAndDoesNotThrow()
    {
        string missingRoot = Path.Combine(Path.GetTempPath(), "tyhp-xdebug-proxy", Guid.NewGuid().ToString("N"));

        using var store = new SourceMapStore(missingRoot) { AutoReload = false };
        Action act = () => store.LoadAll();
        act.Should().NotThrow();

        store.LoadedMaps.Should().BeEmpty();
        store.Warnings.Should().ContainSingle(w => w.Kind == SourceMapLoadWarningKind.RootDirectoryMissing);
    }

    [Fact]
    public void GetMapForPhpFile_AutoReload_ReloadsWhenMapContentsChange()
    {
        string root = CreateTempDirectory();
        string phpPath = Path.Combine(root, "App.php");
        string mapPath = phpPath + ".map";
        WriteMap(mapPath, file: "App.php", source: "App.tyhp", mappings: "AAAA");

        using var store = new SourceMapStore(root) { AutoReload = true };
        store.LoadAll();
        store.GetMapForPhpFile(phpPath)!.FindOriginalPosition(0, 0)!.Value.Line.Should().Be(0);

        WriteMap(mapPath, file: "App.php", source: "App.tyhp", mappings: "AAAA;;;;;;;;;;AAKA");
        File.SetLastWriteTimeUtc(mapPath, DateTime.UtcNow.AddSeconds(2));

        OriginalPosition? reloaded = store.GetMapForPhpFile(phpPath)!.FindOriginalPosition(10, 0);
        reloaded.Should().NotBeNull();
        reloaded!.Value.Line.Should().Be(5);
    }

    [Fact]
    public void Constructor_ExplicitMapOutsideRoot_IsLoaded()
    {
        string root = CreateTempDirectory();
        string other = CreateTempDirectory();
        string outsideMap = Path.Combine(other, "Lib.php.map");
        WriteMap(outsideMap, file: "Lib.php", source: "Lib.tyhp");

        using var store = new SourceMapStore(root, explicitMapPaths: [outsideMap]) { AutoReload = false };
        store.LoadAll();

        store.GetMapForPhpFile("Lib.php").Should().NotBeNull();
        store.GetMapForTyhpFile("Lib.tyhp").Should().ContainSingle();
    }

    private static void WriteMap(
        string mapPath,
        string file,
        string source,
        string mappings = "AAAA;;;;;;;;;;AAKA")
    {
        string json =
            $$"""
            {
              "version": 3,
              "file": "{{file}}",
              "sourceRoot": "",
              "sources": ["{{source}}"],
              "names": [],
              "mappings": "{{mappings}}"
            }
            """;
        File.WriteAllText(mapPath, json);
    }

    private string CreateTempDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tyhp-xdebug-proxy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _tempDirectories.Add(tempDir);
        return tempDir;
    }

    public void Dispose()
    {
        foreach (string directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }

        GC.SuppressFinalize(this);
    }
}
