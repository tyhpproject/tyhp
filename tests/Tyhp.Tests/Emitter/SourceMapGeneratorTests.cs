using System.Text.Json;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Emitter.SourceMap;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class SourceMapGeneratorTests
{
    [Fact]
    public void Generate_ReturnsParseableJsonObjectWithVersion3AndFileName()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("echo 1;", Provider(1, 0));

        string json = new SourceMapGenerator("App.php").Generate(collector);
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        root.ValueKind.Should().Be(JsonValueKind.Object);
        root.GetProperty("version").GetInt32().Should().Be(3);
        root.GetProperty("file").GetString().Should().Be("App.php");
        root.GetProperty("sourceRoot").GetString().Should().BeEmpty();
        root.GetProperty("sources").EnumerateArray().Select(e => e.GetString()).Should().Equal("App.tyhp");
        root.GetProperty("names").GetArrayLength().Should().Be(0);
        root.TryGetProperty("sourcesContent", out _).Should().BeFalse();
    }

    [Fact]
    public void Generate_EmptyCollector_EmitsEmptyMappingsAndSources()
    {
        var collector = new SourceMapCollector();
        string json = new SourceMapGenerator("Empty.php").Generate(collector);
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        root.GetProperty("sources").GetArrayLength().Should().Be(0);
        root.GetProperty("names").GetArrayLength().Should().Be(0);
        root.GetProperty("mappings").GetString().Should().BeEmpty();
    }

    [Fact]
    public void Generate_OriginMapping_EncodesAsAaaa()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("x", Provider(1, 0));

        MappingsOf(collector, "App.php").Should().Be("AAAA");
    }

    [Fact]
    public void Generate_EmptyGeneratedLine_ProducesConsecutiveSemicolons()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("x", Provider(1, 0));
        collector.AddNewLine();
        collector.AddNewLine();
        collector.AddContent("y", Provider(2, 0));

        MappingsOf(collector, "App.php").Should().Be("AAAA;;AACA");
    }

    [Fact]
    public void Generate_SameLineSegments_AreCommaSeparated()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("hello", Provider(1, 0));
        collector.AddContent("world", Provider(2, 4));

        MappingsOf(collector, "App.php").Should().Be("AAAA,KACI");
    }

    [Fact]
    public void Generate_NamedSegment_EncodesFifthField()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("class", Provider(1, 0), name: "MyClass");

        MappingsOf(collector, "App.php").Should().Be("AAAAA");
        NamesOf(collector, "App.php").Should().Equal("MyClass");
    }

    [Fact]
    public void Generate_RelativeEncoding_DecodesBackToAbsoluteMappings()
    {
        var collector = new SourceMapCollector();
        collector.RegisterSourceFile("a.tyhp");
        collector.RegisterSourceFile("b.tyhp");
        collector.AddContent("aaa", Provider(10, 0));
        collector.AddNewLine();
        collector.AddContent("b", Provider(11, 2));
        collector.AddContent("cc", Provider(12, 4), name: "$x");

        IReadOnlyList<SourceMapping> expected = collector.GetMappings();
        string mappings = MappingsOf(collector, "Out.php");
        DecodeMappings(mappings).Should().Equal(expected);
    }

    [Fact]
    public void Generate_VlqPlusInMappings_SurvivesJsonRoundTrip()
    {
        // 1000 encodes as "w+B"; the JSON encoder must not corrupt the '+' in the mappings string.
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("x", Provider(line: 1, column: 1000));

        string json = new SourceMapGenerator("App.php").Generate(collector);
        json.Should().Contain("w+B");

        using var document = JsonDocument.Parse(json);
        string mappings = document.RootElement.GetProperty("mappings").GetString()!;
        DecodeMappings(mappings).Should().Equal(collector.GetMappings());
    }

    [Fact]
    public void Generate_IncludeSourcesContent_EmbedsProviderResultsIncludingNull()
    {
        var collector = new SourceMapCollector();
        collector.RegisterSourceFile("HasContent.tyhp");
        collector.RegisterSourceFile("Missing.tyhp");

        string json = new SourceMapGenerator("App.php").Generate(
            collector,
            includeSourcesContent: true,
            sourceContentProvider: path => path == "HasContent.tyhp"
                ? "<?tyhp\nclass App { }\n"
                : null);

        using var document = JsonDocument.Parse(json);
        JsonElement contents = document.RootElement.GetProperty("sourcesContent");
        contents.GetArrayLength().Should().Be(2);
        contents[0].GetString().Should().Be("<?tyhp\nclass App { }\n");
        contents[1].ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Generate_IncludeSourcesContentWithoutProvider_EmitsNullEntries()
    {
        var collector = new SourceMapCollector("App.tyhp");
        string json = new SourceMapGenerator("App.php").Generate(collector, includeSourcesContent: true);

        using var document = JsonDocument.Parse(json);
        JsonElement contents = document.RootElement.GetProperty("sourcesContent");
        contents.GetArrayLength().Should().Be(1);
        contents[0].ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void Generate_OmitsSourcesContentWhenNotRequested()
    {
        var collector = new SourceMapCollector("App.tyhp");
        string json = new SourceMapGenerator("App.php").Generate(
            collector,
            includeSourcesContent: false,
            sourceContentProvider: _ => "should not be called");

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("sourcesContent", out _).Should().BeFalse();
    }

    [Fact]
    public void Generate_EscapesQuotesBackslashesAndNewlinesInSourceContent()
    {
        const string content = "line \"quoted\" and path C:\\tmp\\\nand more";
        var collector = new SourceMapCollector("App.tyhp");

        string json = new SourceMapGenerator("App.php").Generate(
            collector,
            includeSourcesContent: true,
            sourceContentProvider: _ => content);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("sourcesContent")[0].GetString().Should().Be(content);
    }

    [Fact]
    public void Generate_SourceRoot_RelativizesSourcesAndCallsProviderWithOriginalPath()
    {
        string sourceRoot = Path.Combine(Path.GetTempPath(), "tyhp-sourcemap-src");
        string originalPath = Path.Combine(sourceRoot, "Models", "User.tyhp");
        var collector = new SourceMapCollector();
        collector.RegisterSourceFile(originalPath);

        string? providerPath = null;
        string json = new SourceMapGenerator("User.php", sourceRoot).Generate(
            collector,
            includeSourcesContent: true,
            sourceContentProvider: path =>
            {
                providerPath = path;
                return "<?tyhp class User {}";
            });

        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        root.GetProperty("sourceRoot").GetString().Should().Be(sourceRoot);
        root.GetProperty("sources").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Models/User.tyhp");
        providerPath.Should().Be(originalPath);
    }

    [Fact]
    public void Generate_SourceRootPrefix_StripsMatchingStringPrefix()
    {
        var collector = new SourceMapCollector();
        collector.RegisterSourceFile("src/Models/User.tyhp");

        string json = new SourceMapGenerator("User.php", "src/").Generate(collector);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("sourceRoot").GetString().Should().Be("src/");
        document.RootElement.GetProperty("sources").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Models/User.tyhp");
    }

    [Fact]
    public void Generate_SourceRootUnrelatedPath_LeavesSourceUnchanged()
    {
        var collector = new SourceMapCollector();
        collector.RegisterSourceFile("Other.tyhp");

        string json = new SourceMapGenerator("App.php", "src/").Generate(collector);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("sources").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("Other.tyhp");
    }

    [Fact]
    public void Generate_NullCollector_ThrowsArgumentNullException()
    {
        var generator = new SourceMapGenerator("App.php");
        Action act = () => generator.Generate(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullGeneratedFileName_ThrowsArgumentNullException()
    {
        Action act = () => _ = new SourceMapGenerator(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static string MappingsOf(SourceMapCollector collector, string fileName)
    {
        string json = new SourceMapGenerator(fileName).Generate(collector);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("mappings").GetString()!;
    }

    private static string[] NamesOf(SourceMapCollector collector, string fileName)
    {
        string json = new SourceMapGenerator(fileName).Generate(collector);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("names")
            .EnumerateArray()
            .Select(e => e.GetString()!)
            .ToArray();
    }

    private static List<SourceMapping> DecodeMappings(string mappings)
    {
        var decoded = new List<SourceMapping>();
        int previousGeneratedColumn = 0;
        int previousSourceIndex = 0;
        int previousOriginalLine = 0;
        int previousOriginalColumn = 0;
        int previousNameIndex = 0;

        string[] lines = mappings.Split(';');
        for (int generatedLine = 0; generatedLine < lines.Length; generatedLine++)
        {
            previousGeneratedColumn = 0;
            string line = lines[generatedLine];
            if (line.Length == 0)
            {
                continue;
            }

            foreach (string segment in line.Split(','))
            {
                int[] fields = VlqEncoder.DecodeSegment(segment);
                fields.Length.Should().BeOneOf(4, 5);

                int generatedColumn = previousGeneratedColumn + fields[0];
                int sourceIndex = previousSourceIndex + fields[1];
                int originalLine = previousOriginalLine + fields[2];
                int originalColumn = previousOriginalColumn + fields[3];
                int? nameIndex = null;
                if (fields.Length == 5)
                {
                    nameIndex = previousNameIndex + fields[4];
                    previousNameIndex = nameIndex.Value;
                }

                previousGeneratedColumn = generatedColumn;
                previousSourceIndex = sourceIndex;
                previousOriginalLine = originalLine;
                previousOriginalColumn = originalColumn;
                decoded.Add(new SourceMapping(
                    generatedLine,
                    generatedColumn,
                    sourceIndex,
                    originalLine,
                    originalColumn,
                    nameIndex));
            }
        }

        return decoded;
    }

    private static IBase2Ast Provider(int line, int column) => new TestAst(line, column);

    private sealed class TestAst : Base2Ast
    {
        public TestAst(int line, int column)
        {
            Line = line;
            Column = column;
        }
    }
}
