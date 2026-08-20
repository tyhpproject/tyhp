using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.Tests.XDebugProxy;

[Trait("Category", "XDebugProxy")]
public class SourceMapFileTests
{
    // AAAA at generated line 0 → original (0,0); AAKA at generated line 10 → original (5,0).
    private const string Line10Mappings = "AAAA;;;;;;;;;;AAKA";

    [Fact]
    public void Parse_HandCraftedJson_ReadsEnvelopeFields()
    {
        SourceMapFile map = SourceMapFile.Parse(HandCraftedJson());

        map.Version.Should().Be(3);
        map.File.Should().Be("App.php");
        map.SourceRoot.Should().Be("src/");
        map.Sources.Should().Equal("App.tyhp");
        map.Names.Should().Equal("foo");
        map.SourcesContent.Should().Equal("<?tyhp\nclass App {}\n");
        map.Mappings.Should().Be(Line10Mappings);
        map.DecodedMappings.Should().HaveCount(11);
        map.DecodedMappings[10].Should().ContainSingle().Which.OriginalLine.Should().Be(5);
    }

    [Fact]
    public void FindOriginalPosition_GeneratedLine10_ReturnsMappedTyhpSourceAndLine()
    {
        SourceMapFile map = SourceMapFile.Parse(HandCraftedJson());

        OriginalPosition? found = map.FindOriginalPosition(10, 0);

        found.Should().NotBeNull();
        found!.Value.SourceFile.Should().Be("src/App.tyhp");
        found.Value.Line.Should().Be(5);
        found.Value.Column.Should().Be(0);
    }

    [Fact]
    public void FindGeneratedPosition_TyhpLine5_ReturnsMappedPhpLine()
    {
        SourceMapFile map = SourceMapFile.Parse(HandCraftedJson());

        GeneratedPosition? found = map.FindGeneratedPosition("src/App.tyhp", 5, 0);

        found.Should().NotBeNull();
        found!.Value.GeneratedFile.Should().Be("App.php");
        found.Value.Line.Should().Be(10);
        found.Value.Column.Should().Be(0);
    }

    [Fact]
    public void FindGeneratedPosition_FilenameOnlyAndBackslashPath_StillMatches()
    {
        SourceMapFile map = SourceMapFile.Parse(HandCraftedJson());

        map.FindGeneratedPosition("App.tyhp", 5, 0)!.Value.Line.Should().Be(10);
        map.FindGeneratedPosition("src\\App.tyhp", 5, 0)!.Value.Line.Should().Be(10);
    }

    [Fact]
    public void FindGeneratedPosition_UnmappedLine_SnapsForwardThenBackward()
    {
        SourceMapFile map = SourceMapFile.Parse(HandCraftedJson());

        map.FindGeneratedPosition("App.tyhp", 3, 0)!.Value.Line.Should().Be(10);
        map.FindGeneratedPosition("App.tyhp", 20, 0)!.Value.Line.Should().Be(10);
    }

    [Fact]
    public void FindOriginalPosition_ColumnBetweenSegments_SnapsToSegmentAtOrBefore()
    {
        SourceMapFile map = SourceMapFile.Parse(JsonWithMappings("AAAA,KACI"));

        OriginalPosition? atFirst = map.FindOriginalPosition(0, 0);
        OriginalPosition? mid = map.FindOriginalPosition(0, 3);
        OriginalPosition? second = map.FindOriginalPosition(0, 5);

        atFirst!.Value.Line.Should().Be(0);
        atFirst.Value.Column.Should().Be(0);
        // Column 3 is between the segment at column 0 and the segment at column 5 — the
        // closest segment at-or-before column 3 is the one at column 0.
        mid!.Value.Line.Should().Be(0);
        mid.Value.Column.Should().Be(0);
        second!.Value.Line.Should().Be(1);
        second.Value.Column.Should().Be(4);
    }

    [Fact]
    public void FindOriginalPosition_ColumnBeforeFirstSegmentOnLine_ReturnsNull()
    {
        // Single segment at generated column 5 (e.g. leading whitespace/indentation on the
        // generated line is unmapped). Columns 0-4 precede every mapped segment on the line and
        // must NOT snap forward to it.
        SourceMapFile map = SourceMapFile.Parse(JsonWithMappings("KAAA"));

        map.FindOriginalPosition(0, 0).Should().BeNull();
        map.FindOriginalPosition(0, 4).Should().BeNull();

        OriginalPosition? atSegment = map.FindOriginalPosition(0, 5);
        atSegment!.Value.Line.Should().Be(0);
        atSegment.Value.Column.Should().Be(0);

        OriginalPosition? afterSegment = map.FindOriginalPosition(0, 10);
        afterSegment!.Value.Line.Should().Be(0);
        afterSegment.Value.Column.Should().Be(0);
    }

    [Fact]
    public void FindOriginalPosition_NamedSegment_ReturnsName()
    {
        SourceMapFile map = SourceMapFile.Parse(JsonWithMappings("AAAAA", names: """["foo"]"""));

        map.FindOriginalPosition(0, 0)!.Value.Name.Should().Be("foo");
    }

    [Fact]
    public void FindOriginalPosition_UnmappedGeneratedLine_ReturnsNull()
    {
        SourceMapFile map = SourceMapFile.Parse(JsonWithMappings("AAAA;;AACA"));

        map.FindOriginalPosition(1, 0).Should().BeNull();
        map.FindOriginalPosition(99, 0).Should().BeNull();
    }

    [Fact]
    public void PathsMatch_SlashVsBackslashAndRelativeVsFilename()
    {
        SourceMapFile.PathsMatch("src/App.tyhp", "src\\App.tyhp").Should().BeTrue();
        SourceMapFile.PathsMatch("App.php", "build/App.php").Should().BeTrue();
        SourceMapFile.PathsMatch("/project/src/App.tyhp", "src/App.tyhp").Should().BeTrue();
        SourceMapFile.PathsMatch("User.tyhp", "App.tyhp").Should().BeFalse();
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsFormatException()
    {
        Action act = () => SourceMapFile.Parse("{not json");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_MissingMappings_ThrowsFormatException()
    {
        Action act = () => SourceMapFile.Parse("""{"version":3,"file":"App.php","sources":[]}""");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_WrongVersion_ThrowsFormatException()
    {
        Action act = () => SourceMapFile.Parse(
            """{"version":2,"file":"App.php","sources":[],"mappings":""}""");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_SourcesContentLengthMismatch_ThrowsFormatException()
    {
        Action act = () => SourceMapFile.Parse(
            """
            {"version":3,"file":"App.php","sources":["App.tyhp","Other.tyhp"],
             "sourcesContent":["only one entry"],"mappings":""}
            """);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Parse_ConsumesStory17GeneratorOutput()
    {
        var collector = new SourceMapCollector("src/App.tyhp");
        collector.AddContent("echo 1;", Provider(1, 0));
        collector.AddNewLine();
        collector.AddContent("echo 2;", Provider(2, 0), name: "foo");

        string json = new SourceMapGenerator("App.php", "src/").Generate(collector);
        SourceMapFile map = SourceMapFile.Parse(json);

        map.Version.Should().Be(3);
        map.File.Should().Be("App.php");
        map.Sources.Should().Equal("App.tyhp");

        OriginalPosition original = map.FindOriginalPosition(0, 0)!.Value;
        original.SourceFile.Should().Be("src/App.tyhp");
        original.Line.Should().Be(0);

        GeneratedPosition generated = map.FindGeneratedPosition("src/App.tyhp", 1, 0)!.Value;
        generated.Line.Should().Be(1);
        generated.GeneratedFile.Should().Be("App.php");
    }

    [Fact]
    public void Load_ReadsMapFromDisk()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "tyhp-xdebug-proxy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string mapPath = Path.Combine(tempDir, "App.php.map");
            File.WriteAllText(mapPath, HandCraftedJson());

            SourceMapFile map = SourceMapFile.Load(mapPath);
            map.MapFilePath.Should().Be(mapPath);
            map.FindOriginalPosition(10, 0)!.Value.Line.Should().Be(5);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static string HandCraftedJson() =>
        $$"""
        {
          "version": 3,
          "file": "App.php",
          "sourceRoot": "src/",
          "sources": ["App.tyhp"],
          "sourcesContent": ["<?tyhp\nclass App {}\n"],
          "names": ["foo"],
          "mappings": "{{Line10Mappings}}"
        }
        """;

    private static string JsonWithMappings(string mappings, string names = "[]") =>
        $$"""
        {
          "version": 3,
          "file": "App.php",
          "sourceRoot": "src/",
          "sources": ["App.tyhp"],
          "names": {{names}},
          "mappings": "{{mappings}}"
        }
        """;

    private static IBase2Ast Provider(int line, int column) => new TestAst(line, column);

    private sealed class TestAst : Base2Ast
    {
        public TestAst(int line, int column)
        {
            Line = line;
            Column = column;
        }
    }

    private static void TryDeleteDirectory(string directory)
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
}
