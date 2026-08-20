using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Emitter;
using Tyhp.TyhpLang.Emitter.SourceMap;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class EmitItemSourceMapTests
{
    [Fact]
    public void EmitWithCollector_ReturnsTheSameStringAsEmitWithoutCollector()
    {
        var tree = NestedFunctionTree();

        tree.emit(0).Should().Be(tree.emit(0, new SourceMapCollector()));
    }

    [Fact]
    public void EmitWithCollector_SiblingStatements_MatchNonTrackingOutput()
    {
        var provider = Provider(1, 0);
        var block = EmitItem.BlockBraceNextLine(provider, EmitType.RootStatement, "function demo(): void");
        EmitItem.Line(Provider(2, 4), EmitType.FunctionStatement, "$a = 1;", block);
        EmitItem.Line(Provider(3, 4), EmitType.FunctionStatement, "$b = 2;", block);

        var expected = "function demo(): void\n{\n    $a = 1;\n    $b = 2;\n}";
        block.emit(0).Should().Be(expected);
        block.emit(0, new SourceMapCollector()).Should().Be(expected);
    }

    [Fact]
    public void EmitWithCollector_RecordsMappingsForEveryContributingNodeWithValidProvider()
    {
        var parent = Provider(1, 0);
        var child = Provider(2, 4);
        var grandchild = Provider(5, 8);

        var block = EmitItem.BlockBraceNextLine(parent, EmitType.RootStatement, "function demo(): void");
        var inner = EmitItem.BlockBraceNextLine(child, EmitType.SubBlockStatement, "if (true)", parent: block);
        EmitItem.Line(grandchild, EmitType.FunctionStatement, "$x = 1;", inner);

        var collector = new SourceMapCollector();
        string php = block.emit(0, collector);

        php.Should().Be("function demo(): void\n{\n    if (true)\n    {\n        $x = 1;\n    }\n}");

        collector.GetMappings().Should().Equal(
            new SourceMapping(0, 0, 0, OriginalLine: 0, OriginalColumn: 0),  // function demo(): void
            new SourceMapping(1, 0, 0, OriginalLine: 0, OriginalColumn: 0),  // {
            new SourceMapping(2, 4, 0, OriginalLine: 1, OriginalColumn: 4),  // if (true)
            new SourceMapping(3, 4, 0, OriginalLine: 1, OriginalColumn: 4),  // {
            new SourceMapping(4, 8, 0, OriginalLine: 4, OriginalColumn: 8),  // $x = 1;
            new SourceMapping(5, 4, 0, OriginalLine: 1, OriginalColumn: 4),  // }
            new SourceMapping(6, 0, 0, OriginalLine: 0, OriginalColumn: 0)); // }
    }

    [Fact]
    public void EmitWithCollector_GeneratedLineCountMatchesNewlineCountInReturnedString()
    {
        var tree = NestedFunctionTree();
        var collector = new SourceMapCollector();
        string php = tree.emit(0, collector);

        collector.CurrentGeneratedLine.Should().Be(php.Count(c => c == '\n'));
        collector.CurrentGeneratedColumn.Should().Be(
            php.Length - php.LastIndexOf('\n') - 1);
    }

    [Fact]
    public void EmitWithCollector_IndentIsNotMapped_ContentStartsAfterIndent()
    {
        var item = EmitItem.Line(Provider(3, 0), EmitType.FunctionStatement, "return 1;");
        var collector = new SourceMapCollector();

        item.emit(1, collector).Should().Be("    return 1;");
        collector.GetMappings().Should().Equal(
            new SourceMapping(0, 4, 0, OriginalLine: 2, OriginalColumn: 0));
        collector.CurrentGeneratedColumn.Should().Be(13);
    }

    [Fact]
    public void EmitWithCollector_InvalidProvider_EmitsTextWithoutRecordingMappings()
    {
        var item = EmitItem.Line(Provider(line: -1, column: 0), EmitType.RootStatement, "echo 1;");
        var collector = new SourceMapCollector();

        item.emit(0, collector).Should().Be("echo 1;");
        collector.GetMappings().Should().BeEmpty();
        collector.CurrentGeneratedColumn.Should().Be(7);
    }

    [Fact]
    public void EmitWithCollector_LineZeroProvider_DoesNotRecordAMapping()
    {
        var item = EmitItem.Line(Provider(line: 0, column: 0), EmitType.RootStatement, "echo 1;");
        var collector = new SourceMapCollector();

        item.emit(0, collector).Should().Be("echo 1;");
        collector.GetMappings().Should().BeEmpty();
    }

    [Fact]
    public void EmitWithCollector_EmptyChild_IsSkippedAndDoesNotAffectCollectorPosition()
    {
        var parent = Provider(1, 0);
        var block = EmitItem.BlockBraceNextLine(parent, EmitType.RootStatement, "function demo(): void");
        EmitItem.Empty(Provider(99, 0), EmitType.FunctionStatement, block);
        EmitItem.Line(Provider(2, 4), EmitType.FunctionStatement, "$a = 1;", block);

        var collector = new SourceMapCollector();
        string php = block.emit(0, collector);

        php.Should().Be(block.emit(0));
        php.Should().Be("function demo(): void\n{\n    $a = 1;\n}");
        collector.GetMappings().Select(m => m.OriginalLine).Should().Equal(0, 0, 1, 0);
        collector.CurrentGeneratedLine.Should().Be(php.Count(c => c == '\n'));
    }

    [Fact]
    public void EmitWithCollector_WhitespaceOnlyChild_IsSkippedLikeNonTrackingEmit()
    {
        var parent = Provider(1, 0);
        var block = EmitItem.BlockBraceNextLine(parent, EmitType.RootStatement, "function demo(): void");
        EmitItem.Line(Provider(2, 0), EmitType.FunctionStatement, "   ", block);
        EmitItem.Line(Provider(3, 4), EmitType.FunctionStatement, "$a = 1;", block);

        var collector = new SourceMapCollector();
        string php = block.emit(0, collector);

        php.Should().Be(block.emit(0));
        php.Should().Be("function demo(): void\n{\n    $a = 1;\n}");
        collector.GetMappings().Should().NotContain(m => m.OriginalLine == 1);
        collector.CurrentGeneratedLine.Should().Be(php.Count(c => c == '\n'));
    }

    [Fact]
    public void EmitWithCollector_MultilinePiece_IndentsEachLineAndMapsAfterIndent()
    {
        var item = EmitItem.Line(
            Provider(4, 0),
            EmitType.FunctionStatement,
            "match ($x) {\n    1 => 'a',\n}");
        var collector = new SourceMapCollector();

        string php = item.emit(1, collector);
        php.Should().Be(item.emit(1));
        php.Should().Be("    match ($x) {\n        1 => 'a',\n    }");

        // Emit indent is reported with a null provider, so every line's mapping starts at
        // column 4. Spaces already inside the fragment (the `match` arm) stay part of the
        // mapped content rather than extra indent.
        collector.GetMappings().Should().Equal(
            new SourceMapping(0, 4, 0, OriginalLine: 3, OriginalColumn: 0),
            new SourceMapping(1, 4, 0, OriginalLine: 3, OriginalColumn: 0),
            new SourceMapping(2, 4, 0, OriginalLine: 3, OriginalColumn: 0));
        collector.CurrentGeneratedLine.Should().Be(2);
    }

    [Fact]
    public void EmitWithCollector_NormalizesCarriageReturnsTheSameWayAsNonTrackingEmit()
    {
        var item = EmitItem.Line(Provider(1, 0), EmitType.RootStatement, "a\r\nb\rc");
        var collector = new SourceMapCollector();

        string tracked = item.emit(0, collector);
        tracked.Should().Be(item.emit(0));
        tracked.Should().Be("a\nb\nc");
        collector.CurrentGeneratedLine.Should().Be(2);
        collector.GetMappings().Should().HaveCount(3);
    }

    [Fact]
    public void EmitWithCollector_TraitUseGroup_InsertsBlankLineBeforeFollowingMembers()
    {
        var cls = Provider(1, 0);
        var block = EmitItem.BlockBraceNextLine(cls, EmitType.ObjectDeclaration, "class Foo");
        EmitItem.Line(Provider(2, 0), EmitType.ObjectTraitUse, "use Bar;", block);
        EmitItem.Line(Provider(3, 0), EmitType.ObjectInstanceMethods, "public function m() {}", block);

        var collector = new SourceMapCollector();
        string php = block.emit(0, collector);

        php.Should().Be(block.emit(0));
        php.Should().Be("class Foo\n{\n    use Bar;\n\n    public function m() {}\n}");
        collector.CurrentGeneratedLine.Should().Be(php.Count(c => c == '\n'));
        collector.GetMappings().Should().Contain(
            new SourceMapping(2, 4, 0, OriginalLine: 1, OriginalColumn: 0));
        collector.GetMappings().Should().Contain(
            new SourceMapping(4, 4, 0, OriginalLine: 2, OriginalColumn: 0));
    }

    [Fact]
    public void EmitWithCollector_AttachDocComment_MapsDocAndSignatureAsStartContent()
    {
        var provider = Provider(10, 2);
        var block = EmitItem.Block(provider, EmitType.ObjectDeclaration, "class Foo {", "}", null);
        EmitItem.AttachDocComment("/** doc */", block);

        var collector = new SourceMapCollector();
        string php = block.emit(0, collector);

        php.Should().Be(block.emit(0));
        php.Should().Be("/** doc */\nclass Foo {\n}");
        collector.GetMappings().Should().Equal(
            new SourceMapping(0, 0, 0, OriginalLine: 9, OriginalColumn: 2),
            new SourceMapping(1, 0, 0, OriginalLine: 9, OriginalColumn: 2),
            new SourceMapping(2, 0, 0, OriginalLine: 9, OriginalColumn: 2));
    }

    [Fact]
    public void EmitWithCollector_ResolvesSourceIndexFromOwningFile()
    {
        var firstFile = TyhpSrcFileAst.Create("First.tyhp", "hash-a");
        var secondFile = TyhpSrcFileAst.Create("Second.tyhp", "hash-b");
        var root = EmitItem.Empty(Provider(1, 0, firstFile), EmitType.FileHeader);
        EmitItem.Line(Provider(1, 0, firstFile), EmitType.RootStatement, "echo 1;", root);
        EmitItem.Line(Provider(2, 0, secondFile), EmitType.RootStatement, "echo 2;", root);

        var collector = new SourceMapCollector();
        root.emit(0, collector);

        collector.GetSourceFiles().Should().Equal(firstFile.FileName, secondFile.FileName);
        collector.GetMappings().Select(m => m.SourceIndex).Should().Equal(0, 1);
    }

    [Fact]
    public void EmitWithCollector_NullCollector_ThrowsArgumentNullException()
    {
        var item = EmitItem.Line(Provider(1, 0), EmitType.RootStatement, "echo 1;");
        Action act = () => item.emit(0, collector: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EmitWithoutCollector_IsUnchangedAndDoesNotRequireACollector()
    {
        var tree = NestedFunctionTree();
        tree.emit(0).Should().Be("function demo(): void\n{\n    $a = 1;\n    $b = 2;\n}");
        tree.emit().Should().Be(tree.emit(0));
    }

    [Fact]
    public void EmitWithCollector_SortedChildren_MatchNonTrackingOrder()
    {
        var cls = Provider(1, 0);
        var block = EmitItem.BlockBraceNextLine(cls, EmitType.ObjectDeclaration, "class Foo");
        EmitItem.Line(Provider(4, 0), EmitType.ObjectInstanceMethods, "public function m() {}", block);
        EmitItem.Line(Provider(2, 0), EmitType.ObjectTraitUse, "use Bar;", block);
        EmitItem.Line(Provider(3, 0), EmitType.ObjectConstantDeclaration, "public const X = 1;", block);

        var collector = new SourceMapCollector();
        string php = block.emit(0, collector);

        php.Should().Be(block.emit(0));
        php.Should().Be("class Foo\n{\n    use Bar;\n\n    public const X = 1;\n    public function m() {}\n}");
    }

    private static EmitItem NestedFunctionTree()
    {
        var block = EmitItem.BlockBraceNextLine(
            Provider(1, 0),
            EmitType.RootStatement,
            "function demo(): void");
        EmitItem.Line(Provider(2, 4), EmitType.FunctionStatement, "$a = 1;", block);
        EmitItem.Line(Provider(3, 4), EmitType.FunctionStatement, "$b = 2;", block);
        return block;
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
