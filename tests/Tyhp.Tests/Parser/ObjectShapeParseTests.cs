using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Trait("Category", "Story27")]
public class ObjectShapeParseTests
{
    [Fact]
    public void Parse_ObjectShapeAlias_SucceedsInTyhp()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Foo = object { public function bar(): void; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Foo")
            .Which.ObjectShape.Should().NotBeNull();
        Flatten(result.Ast).OfType<TyhpObjectShapeAst>().Should().ContainSingle();
    }

    [Fact]
    public void Parse_ObjectShapeWithPublicConst_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type HasConst = object { public const FOO = 1; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var shape = Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "HasConst")
            .Subject.ObjectShape;
        shape.Should().NotBeNull();
        shape!.Members!.GetAllNotNull().OfType<PhpConstDeclListAst>()
            .Should().ContainSingle()
            .Which.GetAllNotNull().Should().ContainSingle(c => c.Identifier == "FOO");
    }

    [Fact]
    public void Parse_ObjectShapeWithTypedConst_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type HasConst = object { public const int FOO = 1; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var constDecl = Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "HasConst")
            .Subject.ObjectShape!.Members!.GetAllNotNull().OfType<PhpConstDeclListAst>()
            .Should().ContainSingle()
            .Subject.GetAllNotNull().Should().ContainSingle(c => c.Identifier == "FOO")
            .Subject;
        constDecl.Type.Should().NotBeNull();
    }

    [Fact]
    public void Parse_ObjectShapeWithUntypedConst_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type HasConst = object { const FOO = 1; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpConstDeclListAst>()
            .Should().ContainSingle()
            .Which.GetAllNotNull().Should().ContainSingle(c => c.Identifier == "FOO");
    }

    [Fact]
    public void Parse_ObjectShapeWithConst_SucceedsInTyhpdef()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            type HasConst = object { public const int FOO = 1; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "HasConst");
    }

    [Fact]
    public void Parse_ObjectShapeAlias_SucceedsInTyhpdef()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            type Foo = object { public function bar(): void; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Foo");
    }

    [Fact]
    public void Parse_GenericObjectShapeAlias_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Box<T> = object { public function get(): T; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Box");
    }

    [Fact]
    public void Parse_ObjectShapeWithConstructSignature_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Foo = object { public function __construct(string $value): void; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Foo");
    }

    [Fact]
    public void Parse_ObjectShapeWithExtends_DoesNotParse()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Foo = object extends Bar { };
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "object shapes have no extends/implements on the object token");
    }

    [Fact]
    public void Parse_InlineObjectShapeParameter_DoesNotParseAsShape()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f(object { } $x): void {}
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "object { } is not a typeExpr; only type-alias RHS accepts a shape");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_ExistingTypeAliasObjectParamAndClass_StillParse()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Foo = int;
            function g(object $x): void {}
            class Bar { }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Foo");
        Flatten(result.Ast).OfType<PhpObjectTypeDeclAst>()
            .Should().ContainSingle(c => c.Identifier == "Bar");
    }

    [Fact]
    public void Parse_NominalIntersectedWithObjectShape_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type TimestampedLogger = \Acme\Log\LoggerInterface & object {
                public function getLastLogAt(): \DateTimeImmutable;
            };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var alias = Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "TimestampedLogger")
            .Subject;
        var typeExpr = alias.TypeExpression.Should().BeOfType<PhpTypeExpressionAst>().Subject;
        typeExpr.TypeKind.Should().Be(PhpTypeKind.Intersection);
        typeExpr.Types.Should().NotBeNull();
        typeExpr.Types!.GetAllNotNull().Should().HaveCount(2);
        typeExpr.Types!.GetAllNotNull().OfType<TyhpObjectShapeAst>()
            .Should().ContainSingle();
    }

    [Fact]
    public void Parse_NominalIntersectedWithConstOnlyObjectShape_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Versioned = \Countable & object {
                public const string VERSION = "1.0";
            };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var alias = Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Versioned")
            .Subject;
        var typeExpr = alias.TypeExpression.Should().BeOfType<PhpTypeExpressionAst>().Subject;
        typeExpr.TypeKind.Should().Be(PhpTypeKind.Intersection);
        var shape = typeExpr.Types!.GetAllNotNull().OfType<TyhpObjectShapeAst>()
            .Should().ContainSingle().Subject;
        shape.Members!.GetAllNotNull().OfType<PhpConstDeclListAst>()
            .Should().ContainSingle()
            .Which.GetAllNotNull().Should().ContainSingle(c => c.Identifier == "VERSION");
    }

    [Fact]
    public void Parse_ObjectShapeIntersectedWithNominal_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type TimestampedLogger = object {
                public function getLastLogAt(): \DateTimeImmutable;
            } & \Acme\Log\LoggerInterface;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var alias = Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "TimestampedLogger")
            .Subject;
        var typeExpr = alias.TypeExpression.Should().BeOfType<PhpTypeExpressionAst>().Subject;
        typeExpr.TypeKind.Should().Be(PhpTypeKind.Intersection);
        typeExpr.Types.Should().NotBeNull();
        typeExpr.Types!.GetAllNotNull().Should().HaveCount(2);
        typeExpr.Types!.GetAllNotNull().OfType<TyhpObjectShapeAst>()
            .Should().ContainSingle();
    }

    [Fact]
    public void Parse_NominalIntersectedWithObjectShape_SucceedsInTyhpdef()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            type TimestampedLogger = \Acme\Log\LoggerInterface & object {
                public function getLastLogAt(): \DateTimeImmutable;
            };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "TimestampedLogger");
    }

    [Fact]
    public void Parse_ThreeWayIntersectionWithObjectShape_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Foo = \Countable & object { public function bar(): void; } & \Traversable;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var alias = Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Foo")
            .Subject;
        var typeExpr = alias.TypeExpression.Should().BeOfType<PhpTypeExpressionAst>().Subject;
        typeExpr.TypeKind.Should().Be(PhpTypeKind.Intersection);
        typeExpr.Types!.GetAllNotNull().Should().HaveCount(3);
    }

    [Fact]
    public void Parse_InlineIntersectionShapeParameter_DoesNotParseAsShape()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f(\Countable & object { } $x): void {}
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "the shape intersection is an alias-RHS-only production, not typeExpr");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_ClassNamedObject_StillParsesAsOrdinaryClass()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class object { public function bar(): void {} }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<PhpObjectTypeDeclAst>()
            .Should().ContainSingle(c => c.Identifier == "object");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_PlainNominalIntersection_StillParsesAsTypeExpr()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Combined = \Countable & \Traversable;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var alias = Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Combined")
            .Subject;
        var typeExpr = alias.TypeExpression.Should().BeOfType<PhpTypeExpressionAst>().Subject;
        typeExpr.TypeKind.Should().Be(PhpTypeKind.Intersection);
        typeExpr.Types!.GetAllNotNull().OfType<PhpBuiltinTypeAst>()
            .Should().BeEmpty("a plain nominal intersection has no object-shape item");
    }

    [Fact]
    public void Parse_BareObjectTypeAlias_StillParsesAsTypeExpr()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Foo = object;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Foo");
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
