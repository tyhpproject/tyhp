using System.Text;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.SourceMap;
using Tyhp.XDebugProxy.Translation;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class SourceCommandTranslatorTests : IDisposable
{
    private const string TyhpSourceText = "<?tyhp\nfunction add(int $a, int $b): int {\n    return $a + $b;\n}\n";

    private readonly List<string> _tempDirectories = [];

    [Fact]
    public void InterceptSource_MappedFile_ReturnsTyhpTextFromSourcesContent()
    {
        using Fixture fx = this.CreateFixture(embedSourcesContent: true, writeTyhpDisk: false);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);

        DbgpResponse? intercepted = translator.InterceptCommand(
            DbgpMessageParser.ParseCommand($"source -i 9 -f {tyhpUri}"));

        intercepted.Should().NotBeNull();
        intercepted!.Command.Should().Be("source");
        intercepted.TransactionId.Should().Be("9");
        intercepted.GetAttribute("success").Should().Be("1");
        intercepted.GetAttribute("encoding").Should().Be("base64");
        Encoding.UTF8.GetString(Convert.FromBase64String(intercepted.RootElement.Value))
            .Should().Be(TyhpSourceText);
    }

    [Fact]
    public void InterceptSource_MappedFile_ReturnsTyhpTextFromDiskWhenContentOmitted()
    {
        using Fixture fx = this.CreateFixture(embedSourcesContent: false, writeTyhpDisk: true);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);

        DbgpResponse? intercepted = translator.InterceptCommand(
            DbgpMessageParser.ParseCommand($"source -i 10 -f {tyhpUri}"));

        intercepted.Should().NotBeNull();
        Encoding.UTF8.GetString(Convert.FromBase64String(intercepted!.RootElement.Value))
            .Should().Be(TyhpSourceText);
    }

    [Fact]
    public void InterceptSource_PhpPathWithMap_ReturnsTyhpText()
    {
        using Fixture fx = this.CreateFixture(embedSourcesContent: true, writeTyhpDisk: false);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string phpUri = fx.Mapper.ToFileUri(fx.PhpFile);

        DbgpResponse? intercepted = translator.InterceptCommand(
            DbgpMessageParser.ParseCommand($"source -i 11 -f {phpUri}"));

        intercepted.Should().NotBeNull();
        Encoding.UTF8.GetString(Convert.FromBase64String(intercepted!.RootElement.Value))
            .Should().Be(TyhpSourceText);
    }

    [Fact]
    public void InterceptSource_NoMap_ReturnsNull()
    {
        using Fixture fx = this.CreateFixture(embedSourcesContent: true, writeTyhpDisk: true);
        DbgpMessageTranslator translator = fx.CreateTranslator();

        translator.InterceptCommand(DbgpMessageParser.ParseCommand("source -i 1 -f file:///project/src/Missing.tyhp"))
            .Should().BeNull();
    }

    [Fact]
    public void InterceptSource_VendorPhpFileWithNoSourcemap_ReturnsNull()
    {
        using Fixture fx = this.CreateFixture(embedSourcesContent: true, writeTyhpDisk: true);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string vendorUri = fx.Mapper.ToFileUri("/vendor/some-package/autoload.php");

        translator.InterceptCommand(DbgpMessageParser.ParseCommand($"source -i 1 -f {vendorUri}"))
            .Should().BeNull();
    }

    [Fact]
    public void InterceptSource_DbgpUri_ReturnsNull()
    {
        using Fixture fx = this.CreateFixture(embedSourcesContent: true, writeTyhpDisk: true);
        DbgpMessageTranslator translator = fx.CreateTranslator();

        translator.InterceptCommand(DbgpMessageParser.ParseCommand("source -i 1 -f dbgp://1"))
            .Should().BeNull();
    }

    [Fact]
    public void InterceptSource_CannotReadTyhpText_ReturnsNull()
    {
        using Fixture fx = this.CreateFixture(embedSourcesContent: false, writeTyhpDisk: false);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);

        translator.InterceptCommand(DbgpMessageParser.ParseCommand($"source -i 1 -f {tyhpUri}"))
            .Should().BeNull();
    }

    [Fact]
    public void InterceptEval_AlwaysReturnsNull()
    {
        using Fixture fx = this.CreateFixture(embedSourcesContent: true, writeTyhpDisk: true);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        byte[] payload = Encoding.UTF8.GetBytes("$x + 1");
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"eval -i 5 -- {Convert.ToBase64String(payload)}");

        translator.InterceptCommand(command).Should().BeNull();
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

    private Fixture CreateFixture(bool embedSourcesContent, bool writeTyhpDisk)
    {
        string root = this.CreateTempDirectory();
        string src = Path.Combine(root, "src");
        string build = Path.Combine(root, "build");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(build);
        string phpFile = Path.Combine(build, "App.php");
        string tyhpFile = Path.Combine(src, "App.tyhp");
        if (writeTyhpDisk)
        {
            File.WriteAllText(tyhpFile, TyhpSourceText);
        }

        WriteMap(
            phpFile + ".map",
            file: "App.php",
            source: "src/App.tyhp",
            mappings: MappingAt(generatedLine0: 66, originalLine0: 41),
            sourcesContent: embedSourcesContent ? TyhpSourceText : null);

        var store = new SourceMapStore(build) { AutoReload = false };
        store.LoadAll();
        var mapper = new PathMapper(root, build);
        return new Fixture(tyhpFile, phpFile, mapper, store);
    }

    private string CreateTempDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tyhp-xdebug-proxy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _tempDirectories.Add(tempDir);
        return tempDir;
    }

    private static void WriteMap(
        string mapPath,
        string file,
        string source,
        string mappings,
        string? sourcesContent)
    {
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 3);
            writer.WriteString("file", file);
            writer.WriteString("sourceRoot", "");
            writer.WriteStartArray("sources");
            writer.WriteStringValue(source);
            writer.WriteEndArray();
            writer.WriteStartArray("names");
            writer.WriteEndArray();
            writer.WriteString("mappings", mappings);
            if (sourcesContent is not null)
            {
                writer.WriteStartArray("sourcesContent");
                writer.WriteStringValue(sourcesContent);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        File.WriteAllBytes(mapPath, stream.ToArray());
    }

    private static string MappingAt(int generatedLine0, int originalLine0)
    {
        return new string(';', generatedLine0) + VlqEncoder.Encode([0, 0, originalLine0, 0]);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(string tyhpFile, string phpFile, PathMapper mapper, SourceMapStore store)
        {
            this.TyhpFile = tyhpFile;
            this.PhpFile = phpFile;
            this.Mapper = mapper;
            this.Store = store;
        }

        public string TyhpFile { get; }
        public string PhpFile { get; }
        public PathMapper Mapper { get; }
        public SourceMapStore Store { get; }

        public DbgpMessageTranslator CreateTranslator() => new(this.Store, this.Mapper);

        public void Dispose() => this.Store.Dispose();
    }
}
