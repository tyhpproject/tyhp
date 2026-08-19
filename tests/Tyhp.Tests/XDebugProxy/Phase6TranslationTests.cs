using System.Text;
using System.Xml.Linq;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.XDebugProxy.Dbgp;
using Tyhp.XDebugProxy.SourceMap;
using Tyhp.XDebugProxy.Translation;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class Phase6TranslationTests : IDisposable
{
    private const int TyhpDbgpLine = 42;
    private const int PhpDbgpLine = 67;

    private readonly List<string> _tempDirectories = [];

    [Fact]
    public void ConditionalBreakpoint_DoesNotCrash_IdentityWhenNamesUnknown()
    {
        using Fixture fx = this.CreateFixture(names: ["myVar"]);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        byte[] payload = Encoding.UTF8.GetBytes("$myVar > 5");
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t conditional -f {tyhpUri} -n {TyhpDbgpLine} -- {Convert.ToBase64String(payload)}");

        translator.Invoking(t => t.TranslateIdeToXDebug(command)).Should().NotThrow();

        command.GetArgument("-t").Should().Be("conditional");
        command.LineNumber.Should().Be(PhpDbgpLine.ToString());
        command.Data.Should().NotBeNull();
        Encoding.UTF8.GetString(command.Data!).Should().Be("$myVar > 5");
    }

    [Fact]
    public void ConditionalBreakpoint_BestEffortRewritesExplicitNamePair()
    {
        using Fixture fx = this.CreateFixture(names: ["myVar=$renamed"]);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        byte[] payload = Encoding.UTF8.GetBytes("$myVar > 5 && $unknown < 1");
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t conditional -f {tyhpUri} -n {TyhpDbgpLine} -- {Convert.ToBase64String(payload)}");

        translator.TranslateIdeToXDebug(command);

        Encoding.UTF8.GetString(command.Data!).Should().Be("$renamed > 5 && $unknown < 1");
    }

    [Fact]
    public void ConditionalBreakpoint_DoesNotRewriteBareWordInsideStringLiteral()
    {
        using Fixture fx = this.CreateFixture(names: ["message=field0"]);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        byte[] payload = Encoding.UTF8.GetBytes("$status == \"message\"");
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t conditional -f {tyhpUri} -n {TyhpDbgpLine} -- {Convert.ToBase64String(payload)}");

        translator.TranslateIdeToXDebug(command);

        Encoding.UTF8.GetString(command.Data!).Should().Be("$status == \"message\"");
    }

    [Fact]
    public void ExceptionBreakpoint_XArgumentUnchanged()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            "breakpoint_set -i 1 -t exception -x App\\DomainException");
        string originalX = command.GetArgument("-x")!;

        translator.TranslateIdeToXDebug(command);

        command.GetArgument("-t").Should().Be("exception");
        command.GetArgument("-x").Should().Be(originalX);
        command.GetArgument("-x").Should().Be("App\\DomainException");
        command.Filename.Should().BeNull();
    }

    [Fact]
    public void BreakpointUpdate_StillUsesIdNotFilename()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);
        translator.TranslateIdeToXDebug(DbgpMessageParser.ParseCommand(
            $"breakpoint_set -i 1 -t line -f {tyhpUri} -n {TyhpDbgpLine}"));
        translator.TranslateXDebugToIde(ParseResponse(
            """<response xmlns="urn:debugger_protocol_v1" command="breakpoint_set" transaction_id="1" id="99"/>"""));

        DbgpCommand update = DbgpMessageParser.ParseCommand("breakpoint_update -i 2 -d 99 -n 100");
        translator.TranslateIdeToXDebug(update);

        update.GetArgument("-d").Should().Be("99");
        update.GetArgument("-n").Should().Be(PhpDbgpLine.ToString());
        update.Filename.Should().BeNull();
    }

    [Fact]
    public void Eval_NotIntercepted_PreservesDashDashData()
    {
        using Fixture fx = this.CreateFixture(names: ["x"]);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        byte[] payload = Encoding.UTF8.GetBytes("$x + 1");
        DbgpCommand command = DbgpMessageParser.ParseCommand(
            $"eval -i 5 -- {Convert.ToBase64String(payload)}");

        translator.InterceptCommand(command).Should().BeNull();
        translator.TranslateIdeToXDebug(command);

        command.Data.Should().NotBeNull();
        command.Data.Should().Equal(payload);
        Encoding.UTF8.GetString(command.Data!).Should().Be("$x + 1");
    }

    [Fact]
    public void Eval_EmptyDataArray_PreservedNotNulled()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        var command = new DbgpCommand(DbgpConstants.Commands.Eval, "1", data: []);

        translator.TranslateIdeToXDebug(command);

        command.Data.Should().NotBeNull();
        command.Data.Should().BeEmpty();
    }

    [Fact]
    public void DecimalProperty_SurfacesValueAsPrimaryDisplay()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        DbgpResponse response = ParseResponse(
            """
            <response xmlns="urn:debugger_protocol_v1" command="context_get" transaction_id="4">
              <property name="$amount" fullname="$amount" type="object" classname="Tyhp\Decimal" children="1" numchildren="2">
                <property name="value" fullname="$amount-&gt;value" facet="public" type="string"><![CDATA[12.50]]></property>
                <property name="scale" fullname="$amount-&gt;scale" facet="public" type="int"><![CDATA[2]]></property>
              </property>
            </response>
            """);

        translator.TranslateXDebugToIde(response);

        XElement property = response.GetChild("property")!;
        property.Attribute("classname")!.Value.Should().Be("Tyhp\\Decimal");
        property.Attribute("type")!.Value.Should().Be("string");
        property.Value.Should().Be("12.50");
    }

    [Fact]
    public void DecimalProperty_LeadingBackslashClassname_StillSurfacesValue()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        DbgpResponse response = ParseResponse(
            """
            <response xmlns="urn:debugger_protocol_v1" command="context_get" transaction_id="4">
              <property name="$amount" type="object" classname="\Tyhp\Decimal">
                <property name="$value" type="string"><![CDATA[3.14]]></property>
              </property>
            </response>
            """);

        translator.TranslateXDebugToIde(response);

        response.GetChild("property")!.Value.Should().Be("3.14");
    }

    [Fact]
    public void DecimalProperty_UnexpectedShape_DoesNotThrow()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        DbgpResponse response = ParseResponse(
            """
            <response xmlns="urn:debugger_protocol_v1" command="context_get" transaction_id="4">
              <property name="$amount" type="object" classname="Tyhp\Decimal"/>
            </response>
            """);

        translator.Invoking(t => t.TranslateXDebugToIde(response)).Should().NotThrow();
        response.GetChild("property")!.Attribute("type")!.Value.Should().Be("object");
    }

    [Fact]
    public void OrdinaryPhpArray_IsNotRewritten()
    {
        using Fixture fx = this.CreateFixture(names: ["firstName", "age"]);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        DbgpResponse response = ParseResponse(
            """
            <response xmlns="urn:debugger_protocol_v1" command="context_get" transaction_id="4">
              <property name="$items" type="array" children="1" numchildren="2">
                <property name="0" type="int"><![CDATA[1]]></property>
                <property name="1" type="int"><![CDATA[2]]></property>
              </property>
            </response>
            """);

        translator.TranslateXDebugToIde(response);

        List<XElement> children = response.GetChild("property")!.Elements().ToList();
        children[0].Attribute("name")!.Value.Should().Be("0");
        children[1].Attribute("name")!.Value.Should().Be("1");
    }

    [Fact]
    public void StructLikeArray_KeysMatchingNames_LeftAsPropertyNames()
    {
        using Fixture fx = this.CreateFixture(names: ["firstName", "age"]);
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string phpUri = fx.Mapper.ToFileUri(fx.PhpFile);
        DbgpResponse response = ParseResponse(
            $"""
            <response xmlns="urn:debugger_protocol_v1" command="context_get" transaction_id="4">
              <property name="$user" type="array" filename="{phpUri}" lineno="{PhpDbgpLine}" children="1" numchildren="2">
                <property name="firstName" type="string"><![CDATA[Ada]]></property>
                <property name="age" type="int"><![CDATA[36]]></property>
              </property>
            </response>
            """);

        translator.TranslateXDebugToIde(response);

        List<XElement> children = response.GetChild("property")!.Elements().ToList();
        children[0].Attribute("name")!.Value.Should().Be("firstName");
        children[1].Attribute("name")!.Value.Should().Be("age");
    }

    [Fact]
    public async Task TranslationSession_ConcurrentBreakpointTraffic_DoesNotThrow()
    {
        using Fixture fx = this.CreateFixture();
        DbgpMessageTranslator translator = fx.CreateTranslator();
        string tyhpUri = fx.Mapper.ToFileUri(fx.TyhpFile);

        // DebugSession relays IDE->XDebug and XDebug->IDE as two independently-running tasks
        // against the same translator/session state; simulate that concurrency here so a
        // regression to a non-thread-safe backing dictionary surfaces as a thrown exception
        // instead of a silent corruption.
        var tasks = new List<Task>();
        for (int i = 0; i < 200; i++)
        {
            int id = i;
            tasks.Add(Task.Run(() => translator.TranslateIdeToXDebug(DbgpMessageParser.ParseCommand(
                $"breakpoint_set -i {id} -t line -f {tyhpUri} -n {TyhpDbgpLine}"))));
            tasks.Add(Task.Run(() => translator.TranslateXDebugToIde(ParseResponse(
                $"""<response xmlns="urn:debugger_protocol_v1" command="breakpoint_set" transaction_id="{id}" id="{id}"/>"""))));
            tasks.Add(Task.Run(() => translator.TranslateIdeToXDebug(DbgpMessageParser.ParseCommand(
                $"breakpoint_update -i {id + 1000} -d {id} -n {TyhpDbgpLine + 1}"))));
            tasks.Add(Task.Run(() => translator.TranslateXDebugToIde(ParseResponse(
                $"""
                <response xmlns="urn:debugger_protocol_v1" command="breakpoint_list" transaction_id="{id + 2000}">
                  <breakpoint id="{id}" type="line"/>
                </response>
                """))));
        }

        Func<Task> act = () => Task.WhenAll(tasks);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void StackGet_MultiFile_MapsEachFrame_VendorStaysPhp()
    {
        using Fixture fx = this.CreateFixture();
        WriteMap(
            Path.Combine(fx.Build, "Other.php.map"),
            file: "Other.php",
            source: "src/Other.tyhp",
            mappings: MappingAt(generatedLine0: 10, originalLine0: 3),
            names: []);
        fx.Store.LoadAll();

        DbgpMessageTranslator translator = fx.CreateTranslator();
        string phpUri = fx.Mapper.ToFileUri(fx.PhpFile);
        string otherPhp = fx.Mapper.ToFileUri(Path.Combine(fx.Build, "Other.php"));
        DbgpResponse response = ParseResponse(
            $"""
            <response xmlns="urn:debugger_protocol_v1" command="stack_get" transaction_id="3">
              <stack where="App\\add" level="0" type="file" filename="{phpUri}" lineno="{PhpDbgpLine}"/>
              <stack where="Other\\run" level="1" type="file" filename="{otherPhp}" lineno="11"/>
              <stack where="Composer\\autoload" level="2" type="file" filename="file:///vendor/autoload.php" lineno="15"/>
            </response>
            """);

        translator.TranslateXDebugToIde(response);

        List<XElement> frames = response.GetChildren("stack").ToList();
        fx.Mapper.ToFileSystemPath(frames[0].Attribute("filename")!.Value)
            .Should().Be(fx.Mapper.Normalize(fx.TyhpFile));
        frames[0].Attribute("lineno")!.Value.Should().Be(TyhpDbgpLine.ToString());
        Path.GetFileName(fx.Mapper.ToFileSystemPath(frames[1].Attribute("filename")!.Value))
            .Should().Be("Other.tyhp");
        frames[1].Attribute("lineno")!.Value.Should().Be("4");
        frames[2].Attribute("filename")!.Value.Should().Be("file:///vendor/autoload.php");
        frames[2].Attribute("lineno")!.Value.Should().Be("15");
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

    private Fixture CreateFixture(IReadOnlyList<string>? names = null)
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
            mappings: MappingAt(generatedLine0: 66, originalLine0: 41),
            names: names ?? []);

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

    private static void WriteMap(
        string mapPath,
        string file,
        string source,
        string mappings,
        IReadOnlyList<string> names)
    {
        string namesJson = string.Join(", ", names.Select(n => System.Text.Json.JsonSerializer.Serialize(n)));
        string json =
            $$"""
            {
              "version": 3,
              "file": "{{file}}",
              "sourceRoot": "",
              "sources": ["{{source}}"],
              "names": [{{namesJson}}],
              "mappings": "{{mappings}}"
            }
            """;
        File.WriteAllText(mapPath, json);
    }

    private static string MappingAt(int generatedLine0, int originalLine0)
    {
        return new string(';', generatedLine0) + VlqEncoder.Encode([0, 0, originalLine0, 0]);
    }

    private static DbgpResponse ParseResponse(string xml) => new(XElement.Parse(xml));

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

        public void Dispose() => this.Store.Dispose();
    }
}
