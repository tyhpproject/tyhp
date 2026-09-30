using System.Xml.Linq;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.SourceMap;
using Tyhp.XDebugProxy.Translation;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class DbgpMessageTranslatorTests : IDisposable
{
    /// <summary>
    /// Hand-crafted map: generated line 66 (DBGp 67) ↔ original line 41 (DBGp 42).
    /// Generated line 0 is unmapped so <see cref="SourceMapFile.FindOriginalPosition"/> returns null.
    /// </summary>
    private const int TyhpDbgpLine = 42;
    private const int PhpDbgpLine = 67;

    private readonly List<string> _tempDirectories = [];

    [Fact]
    public void BreakpointSet_TyhpLine42_TranslatesToPhpLine67()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t line -f {tyhpUri} -n {TyhpDbgpLine}");

        translator.TranslateIdeToXDebug(command);

        fx.Mapper.ToFileSystemPath(command.Filename!).Should().Be(fx.Mapper.Normalize(fx.PhpFile));
        command.Filename.Should().StartWith("file://");
        command.LineNumber.Should().Be(PhpDbgpLine.ToString());
        command.TransactionId.Should().Be("1");
        command.GetArgument("-t").Should().Be("line");
    }

    [Fact]
    public void BreakpointSet_PlainPathWithoutFileUri_StaysPlainPath()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t line -f {fx.TyhpFile} -n {TyhpDbgpLine}");

        translator.TranslateIdeToXDebug(command);

        command.Filename.Should().Be(fx.Mapper.Normalize(fx.PhpFile));
        command.Filename.Should().NotStartWith("file:");
        command.LineNumber.Should().Be(PhpDbgpLine.ToString());
    }

    [Fact]
    public void BreakpointSet_AlreadyPhp_PassesThroughUnmodified()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string phpUri = fx.Mapper.ToFileUri(fx.PhpFile);
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t line -f {phpUri} -n 10");
        string original = command.Filename!;

        translator.TranslateIdeToXDebug(command);

        command.Filename.Should().Be(original);
        command.LineNumber.Should().Be("10");
    }

    [Fact]
    public void BreakpointSet_FileWithoutSourcemap_PassesThroughUnmodified()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        const string missing = "file:///project/src/Missing.tyhp";
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t line -f {missing} -n 10");

        translator.TranslateIdeToXDebug(command);

        command.Filename.Should().Be(missing);
        command.LineNumber.Should().Be("10");
    }

    [Fact]
    public void StackGet_PhpRefs_TranslateToTyhpRefs()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string phpUri = fx.Mapper.ToFileUri(fx.PhpFile);
        DbgpResponse response = ParseResponse(
            $"""
            <response xmlns="urn:debugger_protocol_v1" command="stack_get" transaction_id="3" status="break">
              <stack where="App\Calculator-&gt;add" level="0" type="file" filename="{phpUri}" lineno="{PhpDbgpLine}"/>
              <stack where="main" level="1" type="file" filename="file:///vendor/autoload.php" lineno="15"/>
            </response>
            """);

        translator.TranslateXDebugToIde(response);

        List<XElement> frames = response.GetChildren("stack").ToList();
        fx.Mapper.ToFileSystemPath(frames[0].Attribute("filename")!.Value)
            .Should().Be(fx.Mapper.Normalize(fx.TyhpFile));
        frames[0].Attribute("lineno")!.Value.Should().Be(TyhpDbgpLine.ToString());
        frames[1].Attribute("filename")!.Value.Should().Be("file:///vendor/autoload.php");
        frames[1].Attribute("lineno")!.Value.Should().Be("15");
    }

    [Fact]
    public void BreakpointList_ContainsTyhpPathsAndLines_NotPhp()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string phpUri = fx.Mapper.ToFileUri(fx.PhpFile);
        DbgpResponse response = ParseResponse(
            $"""
            <response xmlns="urn:debugger_protocol_v1" command="breakpoint_list" transaction_id="5">
              <breakpoint type="line" filename="{phpUri}" lineno="{PhpDbgpLine}" state="enabled" id="12000"/>
            </response>
            """);

        translator.TranslateXDebugToIde(response);

        XElement bp = response.GetChild("breakpoint")!;
        fx.Mapper.ToFileSystemPath(bp.Attribute("filename")!.Value)
            .Should().Be(fx.Mapper.Normalize(fx.TyhpFile));
        bp.Attribute("lineno")!.Value.Should().Be(TyhpDbgpLine.ToString());
        bp.Attribute("filename")!.Value.Should().NotContain(".php");
    }

    [Fact]
    public void BreakpointSetResponse_RecordsId_SoLaterGetUsesSessionTable()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);
        translator.TranslateIdeToXDebug(DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t line -f {tyhpUri} -n {TyhpDbgpLine}"));
        translator.TranslateXDebugToIde(ParseResponse(
            """<response xmlns="urn:debugger_protocol_v1" command="breakpoint_set" transaction_id="1" id="99"/>"""));

        DbgpResponse get = ParseResponse(
            """
            <response xmlns="urn:debugger_protocol_v1" command="breakpoint_get" transaction_id="8">
              <breakpoint type="line" filename="file:///nowhere/nomap.php" lineno="10" id="99"/>
            </response>
            """);
        translator.TranslateXDebugToIde(get);

        XElement bp = get.GetChild("breakpoint")!;
        bp.Attribute("filename")!.Value.Should().Be(tyhpUri);
        bp.Attribute("lineno")!.Value.Should().Be(TyhpDbgpLine.ToString());
    }

    [Fact]
    public void BreakpointUpdate_NewTyhpLine_TranslatesToPhpLine_UsingSessionOrigin()
    {
        // breakpoint_update carries no -f (per the DBGp spec) — only -d <id> and optionally
        // -n <lineno>. The proxy must resolve the Tyhp file from the breakpoint-id table
        // recorded when the breakpoint was set, not from a (nonexistent) -f argument. The
        // fixture map has a single mapped line (dbgp 42 -> dbgp 67); a "moved" line number
        // with no direct mapping snaps to that same PHP line via the forward/backward
        // fallback — the important assertion is that it is translated at all, not left as
        // the raw Tyhp line number (which would silently break the breakpoint on XDebug).
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);

        translator.TranslateIdeToXDebug(DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t line -f {tyhpUri} -n {TyhpDbgpLine}"));
        translator.TranslateXDebugToIde(ParseResponse(
            """<response xmlns="urn:debugger_protocol_v1" command="breakpoint_set" transaction_id="1" id="99"/>"""));

        const int movedTyhpLine = 100;
        DbgpCommand update = DbgpMessageParser.ParseCommand($"breakpoint_update -i 2 -d 99 -n {movedTyhpLine}");

        translator.TranslateIdeToXDebug(update);

        update.GetArgument("-n").Should().Be(PhpDbgpLine.ToString());
        update.GetArgument("-n").Should().NotBe(movedTyhpLine.ToString());
        update.GetArgument("-d").Should().Be("99");
    }

    [Fact]
    public void BreakpointUpdate_UnknownId_PassesThroughUnmodified()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        DbgpCommand update = DbgpMessageParser.ParseCommand("breakpoint_update -i 2 -d 12345 -n 99 -s disabled");

        translator.TranslateIdeToXDebug(update);

        update.GetArgument("-n").Should().Be("99");
        update.GetArgument("-s").Should().Be("disabled");
    }

    [Fact]
    public void TwoTranslators_DoNotShareBreakpointIdTables()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator sessionA = fx.CreateTranslator();
        DbgpMessageTranslator sessionB = fx.CreateTranslator();
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);

        sessionA.TranslateIdeToXDebug(DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t line -f {tyhpUri} -n {TyhpDbgpLine}"));
        sessionA.TranslateXDebugToIde(ParseResponse(
            """<response xmlns="urn:debugger_protocol_v1" command="breakpoint_set" transaction_id="1" id="99"/>"""));

        const string listXml =
            """
            <response xmlns="urn:debugger_protocol_v1" command="breakpoint_get" transaction_id="8">
              <breakpoint type="line" filename="file:///nowhere/nomap.php" lineno="10" id="99"/>
            </response>
            """;
        DbgpResponse forA = ParseResponse(listXml);
        DbgpResponse forB = ParseResponse(listXml);

        sessionA.TranslateXDebugToIde(forA);
        sessionB.TranslateXDebugToIde(forB);

        forA.GetChild("breakpoint")!.Attribute("filename")!.Value.Should().Be(tyhpUri);
        forA.GetChild("breakpoint")!.Attribute("lineno")!.Value.Should().Be(TyhpDbgpLine.ToString());
        forB.GetChild("breakpoint")!.Attribute("filename")!.Value.Should().Be("file:///nowhere/nomap.php");
        forB.GetChild("breakpoint")!.Attribute("lineno")!.Value.Should().Be("10");
    }

    [Fact]
    public void InitFileUri_WithMap_TranslatesToTyhp()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string phpUri = fx.Mapper.ToFileUri(fx.PhpFile);
        DbgpResponse init = ParseResponse(
            $"""
            <init xmlns="urn:debugger_protocol_v1" fileuri="{phpUri}" language="PHP" protocol_version="1.0" appid="1">
              <engine version="3.3.0"><![CDATA[Xdebug]]></engine>
            </init>
            """);

        translator.TranslateXDebugToIde(init);

        translator.InitFileUri.Should().Be(phpUri);
        fx.Mapper.ToFileSystemPath(init.GetAttribute("fileuri")!)
            .Should().Be(fx.Mapper.Normalize(fx.TyhpFile));
        init.GetAttribute("fileuri").Should().StartWith("file://");
    }

    [Fact]
    public void UnmappedPhpLine_FindOriginalPositionNull_PassesThrough()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string phpUri = fx.Mapper.ToFileUri(fx.PhpFile);
        DbgpResponse response = ParseResponse(
            $"""
            <response xmlns="urn:debugger_protocol_v1" command="stack_get" transaction_id="3">
              <stack where="main" level="0" type="file" filename="{phpUri}" lineno="1"/>
            </response>
            """);

        translator.TranslateXDebugToIde(response);

        XElement frame = response.GetChild("stack")!;
        frame.Attribute("filename")!.Value.Should().Be(phpUri);
        frame.Attribute("lineno")!.Value.Should().Be("1");
    }

    [Fact]
    public void StatusBreak_RootFilenameLine_TranslatesToTyhp()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string phpUri = fx.Mapper.ToFileUri(fx.PhpFile);
        DbgpResponse response = ParseResponse(
            $"""
            <response xmlns="urn:debugger_protocol_v1" command="run" transaction_id="2" status="break"
                      filename="{phpUri}" lineno="{PhpDbgpLine}"/>
            """);

        translator.TranslateXDebugToIde(response);

        fx.Mapper.ToFileSystemPath(response.GetAttribute("filename")!)
            .Should().Be(fx.Mapper.Normalize(fx.TyhpFile));
        response.GetAttribute("lineno").Should().Be(TyhpDbgpLine.ToString());
        response.Status.Should().Be(DbgpConstants.Status.Break);
    }

    [Fact]
    public void InterceptCommand_UnmappedSourceAndNonSource_ReturnsNull()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();

        translator.InterceptCommand(DbgpMessageParser.ParseCommand("source -i 1 -f file.tyhp"))
            .Should().BeNull();
        translator.InterceptCommand(DbgpMessageParser.ParseCommand("run -i 2"))
            .Should().BeNull();
    }

    [Fact]
    public void UnrelatedCommand_PassesThroughUnmodified()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        DbgpCommand command = DbgpMessageParser.ParseCommand("run -i 2");
        string raw = command.RawText;

        translator.TranslateIdeToXDebug(command);

        command.CommandName.Should().Be("run");
        command.TransactionId.Should().Be("2");
        command.RawText.Should().Be(raw);
        command.Filename.Should().BeNull();
    }

    [Fact]
    public void FeatureSet_StoresNegotiationWithoutChangingBehavior()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        DbgpCommand command = DbgpMessageParser.ParseCommand("feature_set -i 1 -n encoding -v UTF-8");

        translator.TranslateIdeToXDebug(command);

        translator.Features.Should().ContainKey("encoding").WhoseValue.Should().Be("UTF-8");
        command.GetArgument("-n").Should().Be("encoding");
        command.GetArgument("-v").Should().Be("UTF-8");
    }

    [Fact]
    public void MultipleMapsForSameTyhpFile_PrefersPathClosestPhpOutput()
    {
        using Fixture fx = this.CreateFixture();
        WriteMap(
            Path.Combine(fx.Build, "Other.php.map"),
            file: "Other.php",
            source: "src/App.tyhp",
            mappings: MappingAt(generatedLine0: 3, originalLine0: 41));

        fx.Store.LoadAll();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t line -f {fx.TyhpFile} -n {TyhpDbgpLine}");

        translator.TranslateIdeToXDebug(command);

        Path.GetFileName(fx.Mapper.ToFileSystemPath(command.Filename!)).Should().Be("App.php");
        command.LineNumber.Should().Be(PhpDbgpLine.ToString());
    }

    [Fact]
    public void ContextGet_PropertyFilenameLine_ReverseMaps_NamesOtherwiseUnmodified()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string phpUri = fx.Mapper.ToFileUri(fx.PhpFile);
        DbgpResponse response = ParseResponse(
            $"""
            <response xmlns="urn:debugger_protocol_v1" command="context_get" transaction_id="4">
              <property name="$foo" fullname="$foo" type="int" filename="{phpUri}" lineno="{PhpDbgpLine}"><![CDATA[1]]></property>
            </response>
            """);

        translator.TranslateXDebugToIde(response);

        XElement property = response.GetChild("property")!;
        property.Attribute("name")!.Value.Should().Be("$foo");
        fx.Mapper.ToFileSystemPath(property.Attribute("filename")!.Value)
            .Should().Be(fx.Mapper.Normalize(fx.TyhpFile));
        property.Attribute("lineno")!.Value.Should().Be(TyhpDbgpLine.ToString());
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

    private Fixture CreateFixture()
    {
        string root = this.CreateTempDirectory();
        string src = Path.Combine(root, "src");
        string build = Path.Combine(root, "build");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(build);
        string phpFile = Path.Combine(build, "App.php");
        string tyhpFile = Path.Combine(src, "App.tyhp");
        WriteMap(
            phpFile + ".map",
            file: "App.php",
            source: "src/App.tyhp",
            mappings: MappingAt(generatedLine0: 66, originalLine0: 41));

        var store = new SourceMapStore(build) { AutoReload = false };
        store.LoadAll();
        var mapper = new PathMapper(root, build);
        return new Fixture(root, src, build, tyhpFile, phpFile, mapper, store);
    }

    private string CreateTempDirectory()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tyhp-xdebug-proxy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _tempDirectories.Add(tempDir);
        return tempDir;
    }

    private static void WriteMap(string mapPath, string file, string source, string mappings)
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

    private static string MappingAt(int generatedLine0, int originalLine0)
    {
        return new string(';', generatedLine0) + VlqEncoder.Encode([0, 0, originalLine0, 0]);
    }

    private static DbgpResponse ParseResponse(string xml)
    {
        return new DbgpResponse(XElement.Parse(xml));
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture(
            string root,
            string src,
            string build,
            string tyhpFile,
            string phpFile,
            PathMapper mapper,
            SourceMapStore store)
        {
            this.Root = root;
            this.Src = src;
            this.Build = build;
            this.TyhpFile = tyhpFile;
            this.PhpFile = phpFile;
            this.Mapper = mapper;
            this.Store = store;
        }

        public string Root { get; }
        public string Src { get; }
        public string Build { get; }
        public string TyhpFile { get; }
        public string PhpFile { get; }
        public PathMapper Mapper { get; }
        public SourceMapStore Store { get; }

        public DbgpMessageTranslator CreateTranslator() => new(this.Store, this.Mapper);

        public void Dispose()
        {
            this.Store.Dispose();
        }
    }
}
