using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
public class StructKeywordParseTests
{
    [Fact]
    public void Parse_ClassNamedStruct_StillParsesAsOrdinaryClass()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class struct { public function bar(): void {} }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpObjectTypeDeclAst>()
            .Should().ContainSingle(c => c.Identifier == "struct");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().BeEmpty();
        Flatten(result.Ast).OfType<TyhpStructShapeAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_ClassNamedStruct_FollowedByExtends_StillParsesAsOrdinaryClass()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class Base { public function bar(): void {} }
            class struct extends Base { public function bar(): void {} }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpObjectTypeDeclAst>()
            .Should().Contain(c => c.Identifier == "struct");
    }

    [Fact]
    public void Parse_FunctionNamedStruct_StillParsesAsOrdinaryFunction()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function struct(): void {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
            .Should().ContainSingle(f => f.Identifier == "struct");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_NewStructCall_StillParsesAsClassConstruction()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class struct {}
            $x = new struct();
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpObjectTypeDeclAst>()
            .Should().ContainSingle(c => c.Identifier == "struct");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().BeEmpty();
        var constructed = Flatten(result.Ast).OfType<PhpNewAst>().Should().ContainSingle().Subject;
        constructed.ClassName.Should().NotBeNull();
        constructed.ClassName.Should().BeOfType<PhpNameAst>()
            .Which.ValueString.Should().Be("struct");
    }

    [Fact]
    public void Parse_NewAnonymousStruct_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $p = new struct { int $x; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().ContainSingle()
            .Which.IsAnonymous.Should().BeTrue();
    }

    [Fact]
    public void Parse_NewAnonymousStruct_WithEmptyParens_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $p = new struct() { int $x; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().ContainSingle()
            .Which.IsAnonymous.Should().BeTrue();
    }

    [Fact]
    public void Parse_StructShapeAlias_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Point = struct { int $x; int $y; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Point");
        Flatten(result.Ast).OfType<TyhpStructShapeAst>().Should().ContainSingle();
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_InlineStructShapeParameter_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f(struct { int $n; } $s): void {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpStructShapeAst>().Should().ContainSingle();
        Flatten(result.Ast).OfType<PhpFunctionDeclAst>()
            .Should().ContainSingle(f => f.Identifier == "f");
    }

    [Fact]
    public void Parse_StructExtendsShape_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Point = struct { int $x; };
            type Named = struct extends Point { string $name; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpStructShapeAst>().Should().HaveCount(2);
    }

    [Fact]
    public void Parse_NewNamedStructInitializer_DesugarsToWith()
    {
        const string expr = "new Opts[name => 'ann']";
        var source = "<?tyhp\n$p = " + expr + ";\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        var with = AssertStructInitializer(result, source, "new Opts", "[name => 'ann']");
        with.StartIndex.Should().Be(source.IndexOf(expr, StringComparison.Ordinal));
        Slice(source, with).Should().Be(expr);

        var created = (PhpNewAst)with.Left!;
        created.Arguments.Should().BeNull();
        created.ClassName.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("Opts");

        var pairs = (PhpArrayPairListAst)with.Right!;
        var pair = pairs.GetAllExcludingSkippedSlots().Should().ContainSingle().Subject;
        pair.KeyExpr.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("name");
    }

    [Fact]
    public void Parse_NewEmptyStructInitializer_HasEmptyPairList()
    {
        const string expr = "new Opts[]";
        var source = "<?tyhp\n$p = " + expr + ";\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        var with = AssertStructInitializer(result, source, "new Opts", "[]");
        Slice(source, with).Should().Be(expr);
        var pairs = (PhpArrayPairListAst)with.Right!;
        pairs.GetAllNotNull().Should().BeEmpty();
        pairs.GetAllExcludingSkippedSlots().Should().BeEmpty();
    }

    [Fact]
    public void Parse_NewGenericStructInitializer_IncludesTypeArgumentsInNewSpan()
    {
        const string expr = "new Box<int>[value => 42]";
        var source = "<?tyhp\n$p = " + expr + ";\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        var with = AssertStructInitializer(result, source, "new Box<int>", "[value => 42]");
        Slice(source, with).Should().Be(expr);
        var created = (PhpNewAst)with.Left!;
        created.ClassName.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("Box");
        var pair = ((PhpArrayPairListAst)with.Right!).GetAllExcludingSkippedSlots().Should().ContainSingle().Subject;
        pair.KeyExpr.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("value");
    }

    [Fact]
    public void Parse_NewNestedGenericStructInitializer_ClosingAnglesThenBracket()
    {
        const string expr = "new Box<array<int>>[value => 42]";
        var source = "<?tyhp\n$p = " + expr + ";\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        var with = AssertStructInitializer(result, source, "new Box<array<int>>", "[value => 42]");
        Slice(source, with).Should().Be(expr);
        ((PhpNewAst)with.Left!).ClassName.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("Box");
    }

    [Fact]
    public void Parse_NewStructInitializer_KeywordKeyQuotedKeyAndTrailingComma()
    {
        const string expr = "new Opts[class => 'A', 'name' => 'ann',]";
        var source = "<?tyhp\n$p = " + expr + ";\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        var with = AssertStructInitializer(result, source, "new Opts", "[class => 'A', 'name' => 'ann',]");
        var pairs = (PhpArrayPairListAst)with.Right!;
        var real = pairs.GetAllExcludingSkippedSlots().ToList();
        real.Should().HaveCount(2);
        real[0].KeyExpr.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("class");
        Slice(source, real[1].KeyExpr!).Should().Be("'name'");
        pairs.GetAllNotNull().Last().IsSkippedSlot.Should().BeTrue();
    }

    [Fact]
    public void Parse_NewStructInitializer_NewlineBeforeBracketAndMultilineList()
    {
        const string source = """
            <?tyhp
            $p = new Opts
            [
                name => 'ann',
            ];
            """;
        var result = ParserTestHelper.ParseTyhpContent(source);

        var with = AssertStructInitializer(result, source, "new Opts", null);
        var pairs = (PhpArrayPairListAst)with.Right!;
        Slice(source, pairs).Should().StartWith("[").And.EndWith("]");
        Slice(source, pairs).Should().Contain("\n");
        Slice(source, with.Left!).Should().Be("new Opts");
        (with.Left!.EndIndex < pairs.StartIndex).Should().BeTrue();
        pairs.GetAllExcludingSkippedSlots().Should().ContainSingle()
            .Which.KeyExpr.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("name");
        pairs.GetAllNotNull().Last().IsSkippedSlot.Should().BeTrue();
    }

    [Fact]
    public void Parse_NewAnonymousStructInitializer_DesugarsToWith()
    {
        const string expr = "new struct { string $name; int $age; }[name => 'Alice', age => 30]";
        var source = "<?tyhp\n$p = " + expr + ";\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        var with = AssertStructInitializer(
            result,
            source,
            "new struct { string $name; int $age; }",
            "[name => 'Alice', age => 30]");
        Slice(source, with).Should().Be(expr);

        var decl = Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().ContainSingle().Subject;
        decl.IsAnonymous.Should().BeTrue();
        var created = (PhpNewAst)with.Left!;
        created.ClassName.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be(decl.Identifier);

        var real = ((PhpArrayPairListAst)with.Right!).GetAllExcludingSkippedSlots().ToList();
        real.Should().HaveCount(2);
        real[0].KeyExpr.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("name");
        real[1].KeyExpr.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("age");
    }

    [Fact]
    public void Parse_NewAnonymousStructInitializer_KeywordKeyAfterBody()
    {
        const string expr = "new struct { string $name; }[class => 'A']";
        var source = "<?tyhp\n$p = " + expr + ";\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        var with = AssertStructInitializer(result, source, "new struct { string $name; }", "[class => 'A']");
        ((PhpArrayPairListAst)with.Right!).GetAllExcludingSkippedSlots().Should().ContainSingle()
            .Which.KeyExpr.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("class");
    }

    [Fact]
    public void Parse_NewAnonymousStructInitializer_EmptyParensAndExtends()
    {
        const string parenExpr = "new struct() { int $x; }[x => 1]";
        var parenSource = "<?tyhp\n$p = " + parenExpr + ";\n";
        var paren = ParserTestHelper.ParseTyhpContent(parenSource);
        var parenWith = AssertStructInitializer(paren, parenSource, "new struct() { int $x; }", "[x => 1]");
        Slice(parenSource, parenWith).Should().Be(parenExpr);

        const string extendsExpr = "new struct extends Point { string $name; }[name => 'a']";
        var extendsSource = "<?tyhp\n$p = " + extendsExpr + ";\n";
        var extended = ParserTestHelper.ParseTyhpContent(extendsSource);
        var extendsWith = AssertStructInitializer(
            extended,
            extendsSource,
            "new struct extends Point { string $name; }",
            "[name => 'a']");
        Slice(extendsSource, extendsWith).Should().Be(extendsExpr);
    }

    [Fact]
    public void Parse_NewAnonymousStructInitializer_NewlineBeforeBracket()
    {
        const string source = """
            <?tyhp
            $p = new struct { string $name; }
            [name => 'Alice'];
            """;
        var result = ParserTestHelper.ParseTyhpContent(source);

        var with = AssertStructInitializer(result, source, "new struct { string $name; }", "[name => 'Alice']");
        (with.Left!.EndIndex < with.Right!.StartIndex).Should().BeTrue();
    }

    [Fact]
    public void Parse_ExplicitWith_DoesNotSetStructInitializerFlag()
    {
        const string source = "<?tyhp\n$p = new Opts() with [name => 'ann'];\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
        var with = Flatten(result.Ast).OfType<PhpBinaryOpAst>()
            .Should().ContainSingle(op => op.Operator != null && op.Operator.ValueString == "with")
            .Subject;
        with.Operator!.ValueInt64.Should().Be(TyhpParser.T_TYHP_WITH);
        Slice(source, with.Operator).Should().Be("with");
        var created = with.Left.Should().BeOfType<PhpNewAst>().Subject;
        created.IsStructInitializer.Should().BeFalse();
        Slice(source, created).Should().Be("new Opts()");
        Slice(source, with.Right!).Should().Be("name => 'ann'");
    }

    [Fact]
    public void Parse_DynamicClassNameIndex_StaysClassNameNotInitializer()
    {
        const string source = "<?tyhp\n$p = new $classes['k'];\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpBinaryOpAst>()
            .Should().NotContain(op => op.Operator != null && op.Operator.ValueString == "with");
        var created = Flatten(result.Ast).OfType<PhpNewAst>().Should().ContainSingle().Subject;
        created.IsStructInitializer.Should().BeFalse();
        created.ClassName.Should().BeOfType<PhpMemberAccessAst>();
        Slice(source, created).Should().Be("new $classes['k']");
    }

    [Fact]
    public void Parse_ConstructThenIndex_StaysArrayAccess()
    {
        const string source = "<?tyhp\n$p = new Inner()['a'];\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpBinaryOpAst>()
            .Should().NotContain(op => op.Operator != null && op.Operator.ValueString == "with");
        var deref = Flatten(result.Ast).OfType<PhpDereferenceableAst>().Should().ContainSingle().Subject;
        var created = deref.Base.Should().BeOfType<PhpNewAst>().Subject;
        created.IsStructInitializer.Should().BeFalse();
        created.ClassName.Should().BeOfType<PhpNameAst>().Which.ValueString.Should().Be("Inner");
        Slice(source, created).Should().Be("new Inner()");
        deref.Suffix.Should().BeOfType<PhpArrayAccessAst>();
        Slice(source, deref.Suffix!).Should().Be("['a']");
    }

    [Theory]
    [InlineData("new Foo\\Bar[name => 1]", "new Foo\\Bar", "[name => 1]")]
    [InlineData("new \\Foo\\Bar[name => 1]", "new \\Foo\\Bar", "[name => 1]")]
    [InlineData("new namespace\\Bar[name => 1]", "new namespace\\Bar", "[name => 1]")]
    [InlineData("new Foo\\Box<int>[value => 1]", "new Foo\\Box<int>", "[value => 1]")]
    [InlineData("new Box<Foo\\Bar>[value => 1]", "new Box<Foo\\Bar>", "[value => 1]")]
    [InlineData("new Box<int|string>[value => 1]", "new Box<int|string>", "[value => 1]")]
    [InlineData("new Box<int>\n[value => 1]", "new Box<int>", "[value => 1]")]
    [InlineData("new Box<array<int>>\n[value => 1]", "new Box<array<int>>", "[value => 1]")]
    [InlineData("new Box<array<array<int>>>[value => 1]", "new Box<array<array<int>>>", "[value => 1]")]
    [InlineData("new Pair<int, array<string>>[a => 1, b => []]", "new Pair<int, array<string>>", "[a => 1, b => []]")]
    [InlineData("new Box<array<int>>[class => 1, function => 2]", "new Box<array<int>>", "[class => 1, function => 2]")]
    [InlineData("new Box<int>[class => 1,\n  'function' => 2,\n]", "new Box<int>", "[class => 1,\n  'function' => 2,\n]")]
    [InlineData("new Opts[a => 1, b => 2, c => 3]", "new Opts", "[a => 1, b => 2, c => 3]")]
    [InlineData("new Opts[...$base]", "new Opts", "[...$base]")]
    [InlineData("new Opts[...$base, name => 'bob']", "new Opts", "[...$base, name => 'bob']")]
    [InlineData("new Opts[name => 'a', ...$base,]", "new Opts", "[name => 'a', ...$base,]")]
    [InlineData("new Opts[1.0, 2.0]", "new Opts", "[1.0, 2.0]")]
    [InlineData("new Opts [ name => 'a' ]", "new Opts", "[ name => 'a' ]")]
    [InlineData("new Outer[inner => new Inner[class => 1], other => new Inner[]]", "new Outer", "[inner => new Inner[class => 1], other => new Inner[]]")]
    [InlineData("new struct { int $x; }[class => 1]", "new struct { int $x; }", "[class => 1]")]
    [InlineData("new struct { int $x; }[]", "new struct { int $x; }", "[]")]
    [InlineData("new struct { array<int> $x; }[x => []]", "new struct { array<int> $x; }", "[x => []]")]
    [InlineData("new struct extends Base<int> { string $name; }[class => 'a']", "new struct extends Base<int> { string $name; }", "[class => 'a']")]
    [InlineData("new struct { int $x; }\n\n  [x => 1]", "new struct { int $x; }", "[x => 1]")]
    public void Parse_NewStructInitializer_Forms(string expr, string newText, string pairText)
    {
        var source = "<?tyhp\n$p = " + expr + ";\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        var withs = Flatten(result.Ast).OfType<PhpBinaryOpAst>()
            .Where(op => op.Operator != null && op.Operator.ValueInt64 == TyhpParser.T_TYHP_WITH)
            .ToList();
        var with = withs.First(op => op.Left is PhpNewAst { IsStructInitializer: true } n
            && n.StartIndex == source.IndexOf("new ", StringComparison.Ordinal));
        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
        Slice(source, with.Left!).Should().Be(newText);
        Slice(source, with.Right!).Should().Be(pairText);
    }

    [Fact]
    public void Parse_NewStructInitializer_SpreadIsExpansionPair()
    {
        const string source = "<?tyhp\n$p = new Opts[...$base, name => 'bob'];\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
        var pairs = Flatten(result.Ast).OfType<PhpArrayPairListAst>().Single();
        var real = pairs.GetAllExcludingSkippedSlots().ToList();
        real.Should().HaveCount(2);
        real[0].IsExpansion.Should().BeTrue();
        real[1].IsExpansion.Should().BeFalse();
    }

    [Fact]
    public void Parse_NewStructInitializerFollowedByWith_StillParses()
    {
        const string source = "<?tyhp\n$p = new Opts[name => 'a'] with [retries => 5];\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
        var withs = Flatten(result.Ast).OfType<PhpBinaryOpAst>()
            .Where(op => op.Operator != null && op.Operator.ValueInt64 == TyhpParser.T_TYHP_WITH)
            .ToList();
        withs.Should().HaveCount(2);
        var outer = withs.Single(op => op.Left is PhpBinaryOpAst);
        var inner = (PhpBinaryOpAst)outer.Left!;
        inner.Left.Should().BeOfType<PhpNewAst>().Which.IsStructInitializer.Should().BeTrue();
    }

    [Fact]
    public void Parse_ParenthesizedStructInitializer_AllowsMemberAccess()
    {
        const string source = "<?tyhp\n$x = (new Point[x => 1.0, y => 2.0])->x;\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpNewAst>().Should().ContainSingle()
            .Which.IsStructInitializer.Should().BeTrue();
    }

    [Fact]
    public void Parse_WithKeywordKeys_StillReclassified()
    {
        const string source = "<?tyhp\n$p = new Opts() with [class => 'A', function => 'f'];\n$q = $p with [class => 'B'];\n";
        var result = ParserTestHelper.ParseTyhpContent(source);

        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
    }

    [Fact]
    public void Parse_NewWithArgs_AfterStructInitializerStatements_Unaffected()
    {
        const string source = """
            <?tyhp
            $a = new Foo(1)['x'];
            $b = new Foo<int>(1);
            $c = new $classes['k'];
            $d = new $classes['k']();
            $e = new Foo;
            [$f, $g] = [1, 2];
            $h = lazy new Foo(1);
            $i = lazy new Foo<int>(1);
            $j = new Foo<int>(1)->bar;
            """;
        var result = ParserTestHelper.ParseTyhpContent(source);

        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpNewAst>().Should().OnlyContain(n => !n.IsStructInitializer);
        Flatten(result.Ast).OfType<PhpBinaryOpAst>()
            .Should().NotContain(op => op.Operator != null && op.Operator.ValueInt64 == TyhpParser.T_TYHP_WITH);
    }

    [Fact]
    public void Parse_NewStructWithoutBrackets_StillAnonymousInstance()
    {
        const string source = """
            <?tyhp
            $a = new struct { int $x; };
            [$b] = [1];
            $c = foo(new struct { int $y; }, [1]);
            $d = new struct { int $z; } with [z => 1];
            """;
        var result = ParserTestHelper.ParseTyhpContent(source);

        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpNewAst>().Should().OnlyContain(n => !n.IsStructInitializer);
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().HaveCount(3);
    }

    private static PhpBinaryOpAst AssertStructInitializer(
        ParseResult result,
        string source,
        string newText,
        string? pairText)
    {
        result.Diagnostics.Errors.Should().BeEmpty($"parse errors: {Describe(result)}");
        var with = Flatten(result.Ast).OfType<PhpBinaryOpAst>()
            .Should().ContainSingle(op => op.Operator != null && op.Operator.ValueString == "with")
            .Subject;
        with.Operator!.ValueInt64.Should().Be(TyhpParser.T_TYHP_WITH);
        with.Operator.ValueString.Should().Be("with");
        Slice(source, with.Operator).Should().Be("[");

        var created = with.Left.Should().BeOfType<PhpNewAst>().Subject;
        created.IsStructInitializer.Should().BeTrue();
        Slice(source, created).Should().Be(newText);

        var pairs = with.Right.Should().BeOfType<PhpArrayPairListAst>().Subject;
        if (pairText != null)
        {
            Slice(source, pairs).Should().Be(pairText);
        }

        with.Operator.StartIndex.Should().Be(pairs.StartIndex);
        return with;
    }

    private static string Slice(string source, IBase2Ast node)
    {
        if (node.StartIndex < 0 || node.EndIndex < node.StartIndex || node.EndIndex >= source.Length)
        {
            return $"<bad span {node.StartIndex}..{node.EndIndex} len {source.Length}>";
        }

        return source.Substring(node.StartIndex, node.EndIndex - node.StartIndex + 1);
    }

    private static IEnumerable<IBase2Ast> Flatten(IBase2Ast? node)
    {
        if (node is null)
        {
            yield break;
        }

        yield return node;
        foreach (var child in node.AstChildren)
        {
            foreach (var nested in Flatten(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(ParseResult result)
        => string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
