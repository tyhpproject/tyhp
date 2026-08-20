using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Emitter.SourceMap;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class SourceMapCollectorTests
{
    [Fact]
    public void SourceMapping_StoresAllFiveFields()
    {
        var mapping = new SourceMapping(
            GeneratedLine: 2,
            GeneratedColumn: 4,
            SourceIndex: 1,
            OriginalLine: 10,
            OriginalColumn: 3,
            NameIndex: 7);

        mapping.GeneratedLine.Should().Be(2);
        mapping.GeneratedColumn.Should().Be(4);
        mapping.SourceIndex.Should().Be(1);
        mapping.OriginalLine.Should().Be(10);
        mapping.OriginalColumn.Should().Be(3);
        mapping.NameIndex.Should().Be(7);
    }

    [Fact]
    public void SourceMapping_NameIndexDefaultsToNull()
    {
        var mapping = new SourceMapping(0, 0, 0, 0, 0);
        mapping.NameIndex.Should().BeNull();
    }

    [Fact]
    public void AddContent_ProviderAtLine1Column0_MapsGeneratedOriginToOriginalOrigin()
    {
        var collector = new SourceMapCollector();
        collector.AddContent("hello", Provider(line: 1, column: 0));

        collector.GetMappings().Should().Equal(
            new SourceMapping(0, 0, SourceIndex: 0, OriginalLine: 0, OriginalColumn: 0));
        collector.CurrentGeneratedLine.Should().Be(0);
        collector.CurrentGeneratedColumn.Should().Be(5);
    }

    [Fact]
    public void AddContent_MultilineFragment_AdvancesToLastLineAndTrailingColumn()
    {
        var collector = new SourceMapCollector();
        collector.AddContent("line1\nline2", Provider(line: 1, column: 0));

        collector.CurrentGeneratedLine.Should().Be(1);
        collector.CurrentGeneratedColumn.Should().Be(5);
        collector.GetMappings().Should().ContainSingle()
            .Which.Should().Be(new SourceMapping(0, 0, 0, 0, 0));
    }

    [Fact]
    public void AddContent_NullProvider_TracksPositionWithoutRecordingAMapping()
    {
        var collector = new SourceMapCollector();
        collector.AddContent("abc\ndef\nghi", provider: null);

        collector.GetMappings().Should().BeEmpty();
        collector.CurrentGeneratedLine.Should().Be(2);
        collector.CurrentGeneratedColumn.Should().Be(3);
    }

    [Fact]
    public void AddContent_InvalidProviderPositions_DoNotRecordMappings()
    {
        var collector = new SourceMapCollector();
        collector.AddContent("x", Provider(line: -1, column: 0));
        collector.AddContent("y", Provider(line: 1, column: -1));

        collector.GetMappings().Should().BeEmpty();
        collector.CurrentGeneratedColumn.Should().Be(2);
    }

    [Fact]
    public void AddContent_LineZero_DoesNotRecordAMapping()
    {
        // AST Line is 1-based (-1 is the "unknown" sentinel); 0 is never a real line and must
        // not be treated as valid, since that would produce a negative OriginalLine (-1).
        var collector = new SourceMapCollector();
        collector.AddContent("x", Provider(line: 0, column: 0));

        collector.GetMappings().Should().BeEmpty();
        collector.CurrentGeneratedColumn.Should().Be(1);
    }

    [Fact]
    public void AddContent_ConsecutiveSameLineFragments_AccumulateColumn()
    {
        var collector = new SourceMapCollector();
        collector.AddContent("hello", Provider(line: 1, column: 0));
        collector.AddContent("world", Provider(line: 2, column: 4));

        collector.GetMappings().Should().Equal(
            new SourceMapping(0, 0, 0, 0, 0),
            new SourceMapping(0, 5, 0, 1, 4));
        collector.CurrentGeneratedColumn.Should().Be(10);
    }

    [Fact]
    public void RegisterSourceFile_AssignsStableIndices()
    {
        var collector = new SourceMapCollector();

        collector.RegisterSourceFile("a.tyhp").Should().Be(0);
        collector.RegisterSourceFile("b.tyhp").Should().Be(1);
        collector.RegisterSourceFile("a.tyhp").Should().Be(0);
        collector.GetSourceFiles().Should().Equal("a.tyhp", "b.tyhp");
    }

    [Fact]
    public void Constructor_RegistersDefaultSourceFileAsIndexZero()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.GetSourceFiles().Should().Equal("App.tyhp");
        collector.RegisterSourceFile("App.tyhp").Should().Be(0);
        collector.RegisterSourceFile("Other.tyhp").Should().Be(1);
    }

    [Fact]
    public void AddContent_ResolvesSourceIndexFromOwningFile()
    {
        var firstFile = TyhpSrcFileAst.Create("First.tyhp", "hash-a");
        var secondFile = TyhpSrcFileAst.Create("Second.tyhp", "hash-b");
        var collector = new SourceMapCollector();

        collector.AddContent("$a", Provider(1, 0, firstFile));
        collector.AddContent("$b", Provider(2, 0, secondFile));
        collector.AddContent("$c", Provider(3, 0, firstFile));

        collector.GetSourceFiles().Should().Equal(firstFile.FileName, secondFile.FileName);
        collector.GetMappings().Select(m => m.SourceIndex).Should().Equal(0, 1, 0);
    }

    [Fact]
    public void AddContent_MissingOwningFile_UsesDefaultSourceIndex()
    {
        var collector = new SourceMapCollector("Default.tyhp");
        collector.AddContent("echo 1;", Provider(line: 4, column: 2));

        collector.GetMappings().Should().ContainSingle()
            .Which.SourceIndex.Should().Be(0);
        collector.GetSourceFiles().Should().Equal("Default.tyhp");
    }

    [Fact]
    public void GetMappings_ReturnsSortedByGeneratedLineThenColumn()
    {
        var collector = new SourceMapCollector();
        collector.AddContent("aaa", Provider(line: 10, column: 0));
        collector.AddNewLine();
        collector.AddContent("b", Provider(line: 11, column: 0));
        collector.AddContent("cc", Provider(line: 12, column: 0));

        collector.GetMappings().Should().Equal(
            new SourceMapping(0, 0, 0, 9, 0),
            new SourceMapping(1, 0, 0, 10, 0),
            new SourceMapping(1, 1, 0, 11, 0));
    }

    [Fact]
    public void AddNewLine_IncrementsLineAndResetsColumn()
    {
        var collector = new SourceMapCollector();
        collector.AddContent("hello", provider: null);
        collector.AddNewLine();

        collector.CurrentGeneratedLine.Should().Be(1);
        collector.CurrentGeneratedColumn.Should().Be(0);
        collector.GetMappings().Should().BeEmpty();
    }

    [Fact]
    public void SetPosition_OverridesCurrentPositionWithoutRecordingAMapping()
    {
        var collector = new SourceMapCollector();
        collector.AddContent("hello\nworld", Provider(line: 1, column: 0));

        collector.SetPosition(5, 3);

        collector.CurrentGeneratedLine.Should().Be(5);
        collector.CurrentGeneratedColumn.Should().Be(3);
        collector.GetMappings().Should().HaveCount(1, "SetPosition must not add or remove mappings");
    }

    [Fact]
    public void SetPosition_RejectsNegativeValues()
    {
        var collector = new SourceMapCollector();

        var lineAct = () => collector.SetPosition(-1, 0);
        var columnAct = () => collector.SetPosition(0, -1);

        lineAct.Should().Throw<ArgumentOutOfRangeException>();
        columnAct.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RegisterName_AssignsStableIndicesAndOptionalNameOnMapping()
    {
        var collector = new SourceMapCollector();
        collector.RegisterName("MyClass").Should().Be(0);
        collector.RegisterName("$myVar").Should().Be(1);
        collector.RegisterName("MyClass").Should().Be(0);

        collector.AddContent("class", Provider(line: 1, column: 0), name: "MyClass");

        collector.GetNames().Should().Equal("MyClass", "$myVar");
        collector.GetMappings().Should().ContainSingle()
            .Which.NameIndex.Should().Be(0);
    }

    [Fact]
    public void Reset_ClearsCollectedStateAndRestoresDefaultSource()
    {
        var collector = new SourceMapCollector("App.tyhp");
        collector.AddContent("hello\nworld", Provider(line: 1, column: 0), name: "x");
        collector.RegisterSourceFile("Other.tyhp");
        collector.RegisterName("y");

        collector.Reset();

        collector.CurrentGeneratedLine.Should().Be(0);
        collector.CurrentGeneratedColumn.Should().Be(0);
        collector.GetMappings().Should().BeEmpty();
        collector.GetNames().Should().BeEmpty();
        collector.GetSourceFiles().Should().Equal("App.tyhp");
    }

    [Fact]
    public void AddContent_NullContent_ThrowsArgumentNullException()
    {
        var collector = new SourceMapCollector();
        Action act = () => collector.AddContent(null!, Provider(1, 0));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterSourceFile_BlankPath_ThrowsArgumentException()
    {
        var collector = new SourceMapCollector();
        Action act = () => collector.RegisterSourceFile("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetMappings_TiedGeneratedPosition_PreservesRecordingOrder()
    {
        // Nested EmitItem fragments (Phase 3) can start at the same generated (line, column),
        // e.g. a parent's StartContent immediately followed by a child's StartContent with no
        // characters emitted in between. The sort must be stable so the outer node's mapping
        // still precedes the inner one, matching recording order.
        var collector = new SourceMapCollector();
        var outer = Provider(line: 1, column: 0);
        var inner = Provider(line: 2, column: 4);

        collector.AddContent(string.Empty, outer);
        collector.AddContent(string.Empty, inner);

        collector.GetMappings().Should().Equal(
            new SourceMapping(0, 0, 0, 0, 0),
            new SourceMapping(0, 0, 0, 1, 4));
    }

    [Fact]
    public void GetMappings_ReturnedListIsASnapshot()
    {
        var collector = new SourceMapCollector();
        collector.AddContent("a", Provider(1, 0));
        IReadOnlyList<SourceMapping> snapshot = collector.GetMappings();

        collector.AddContent("b", Provider(2, 0));

        snapshot.Should().HaveCount(1);
        collector.GetMappings().Should().HaveCount(2);
    }

    private static IBase2Ast Provider(int line, int column, SrcFileAst? owningFile = null)
        => new TestAst(line, column, owningFile);

    private sealed class TestAst : Base2Ast
    {
        public TestAst(int line, int column, SrcFileAst? owningFile)
        {
            Line = line;
            Column = column;
            OwningFile = owningFile;
        }
    }
}
