using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;
using Tyhp.TyhpLang.Parser;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Trait("Category", "Story271")]
public class CallableShapeParseTests
{
    [Theory]
    [InlineData("""
        <?tyhp
        function f(callable(int $i): string $c): void {}
        """)]
    [InlineData("""
        <?tyhp
        function f($c): callable(int $i): string { return $c; }
        """)]
    [InlineData("""
        <?tyhp
        type Mapper = callable(int $i): string;
        """)]
    [InlineData("""
        <?tyhp
        function f<TCallable extends callable(int $i): string>(TCallable $c): void {}
        """)]
    public void Parse_CallableShape_SucceedsInParameterReturnAliasAndBound(string source)
    {
        var result = ParserTestHelper.ParseTyhpContent(source);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("callable(int): bool", "int")]
    [InlineData("callable(string): int", "string")]
    [InlineData("callable(float): string", "float")]
    [InlineData("callable(bool): int", "bool")]
    [InlineData("callable(array): void", "array")]
    [InlineData("callable(object): int", "object")]
    [InlineData("callable(decimal): int", "decimal")]
    public void Parse_CallableShapeUnnamedCastBuiltin_Succeeds(string shape, string expectedParamType)
    {
        var result = ParserTestHelper.ParseTyhpContent($"""
            <?tyhp
            type F = {shape};
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors for `{shape}`: {Describe(result)}");
        var callable = Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle().Subject;
        var parameter = callable.Parameters!.GetAllNotNull().Should().ContainSingle().Subject;
        parameter.Variable.Should().BeNull();
        TypeSpelling(parameter.TypeExpression).Should().Be(expectedParamType);
    }

    [Fact]
    public void Parse_CallableShapeTwoUnnamedBuiltins_ParsesAsTwoParameters()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type F = callable(int, string): bool;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var callable = Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle().Subject;
        var parameters = callable.Parameters!.GetAllNotNull().ToList();
        parameters.Should().HaveCount(2);
        parameters.Should().OnlyContain(p => p.Variable == null);
        TypeSpelling(parameters[0].TypeExpression).Should().Be("int");
        TypeSpelling(parameters[1].TypeExpression).Should().Be("string");
    }

    [Fact]
    public void Parse_NestedCallableUnnamedCastBoth_IsRightAssociative()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Curry = callable(int): callable(string): bool;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var outer = TyhpCallableShapeAst.Find(
            Flatten(result.Ast).OfType<TyhpTypeAliasAst>().Single().TypeExpression);
        outer.Should().NotBeNull();
        outer!.Parameters!.GetAllNotNull().Should().ContainSingle()
            .Which.Variable.Should().BeNull();
        var inner = TyhpCallableShapeAst.Find(outer.ReturnType);
        inner.Should().NotBeNull();
        inner!.Parameters!.GetAllNotNull().Should().ContainSingle()
            .Which.Variable.Should().BeNull();
    }

    [Fact]
    public void Parse_CallableShapeUnnamedCastBuiltin_SucceedsInReturnPosition()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f($c): callable(int): string { return $c; }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var callable = Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle().Subject;
        callable.Parameters!.GetAllNotNull().Should().ContainSingle()
            .Which.Variable.Should().BeNull();
    }

    [Fact]
    public void Parse_CallableShapeUnnamedCastBuiltin_SucceedsInGenericBound()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f<TCallable extends callable(int): string>(TCallable $c): void {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var callable = Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle().Subject;
        callable.Parameters!.GetAllNotNull().Should().ContainSingle()
            .Which.Variable.Should().BeNull();
    }

    [Fact]
    public void Parse_BareCallableParameter_DoesNotStealFollowingVariable()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f(callable $c): void {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().BeEmpty(
            "bare `callable $c` is not a callable shape");
    }

    [Fact]
    public void Parse_CallableShapeUnnamedInt_DoesNotBreakExpressionCast()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f(callable(int): string $c, mixed $x): int {
                return (int)$x;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle();
        Flatten(result.Ast).OfType<PhpUnaryOpAst>()
            .Should().Contain(u => u.Operator != null && u.Operator.ValueInt64 == TyhpParser.T_INT_CAST);
    }

    [Fact]
    public void Parse_CallableShapeUnnamedInt_SucceedsInTyhpdef()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            type Mapper = callable(int): string;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var callable = Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle().Subject;
        callable.Parameters!.GetAllNotNull().Should().ContainSingle()
            .Which.Variable.Should().BeNull();
    }

    [Fact]
    public void Parse_CallableShape_SucceedsInTyhpdef()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            type Mapper = callable(int $i): string;
            function \array_map<T, U>(callable(T $item): U $callback, array<T> $array): array<U>;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().HaveCount(2);
    }

    [Theory]
    [InlineData("callable(int $i, bool $b = false): string")]
    [InlineData("callable(int, bool = false): string")]
    [InlineData("callable(int $i, bool $b =): string")]
    [InlineData("callable(int, bool=): string")]
    public void Parse_CallableShapeOptionalParameterSpellings_Succeed(string shape)
    {
        var result = ParserTestHelper.ParseTyhpContent($"""
            <?tyhp
            type F = {shape};
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors for `{shape}`: {Describe(result)}");
        var callable = Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle().Subject;
        callable.Parameters!.GetAllNotNull().Should().HaveCount(2);
        callable.Parameters!.GetAllNotNull().ElementAt(1).IsOptional.Should().BeTrue();
    }

    [Fact]
    public void Parse_CallableShapeUnknownArity_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f<TCallable extends callable(...): bool>(TCallable $c): void {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle()
            .Which.IsUnknownArity.Should().BeTrue();
    }

    [Theory]
    [InlineData("callable(T ...$args): R")]
    [InlineData("callable(T ...): R")]
    public void Parse_CallableShapeHomogeneousVariadic_Succeeds(string shape)
    {
        var result = ParserTestHelper.ParseTyhpContent($"""
            <?tyhp
            type F<T, R> = {shape};
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors for `{shape}`: {Describe(result)}");
        var parameter = Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle()
            .Subject.Parameters!.GetAllNotNull().Should().ContainSingle().Subject;
        parameter.IsVariadic.Should().BeTrue();
        parameter.IsOptional.Should().BeFalse();
    }

    [Fact]
    public void Parse_GroupedCallableInUnion_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type F = (callable(int $x): int) | null;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var alias = Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "F").Subject;
        var typeExpr = alias.TypeExpression.Should().BeOfType<PhpTypeExpressionAst>().Subject;
        typeExpr.TypeKind.Should().Be(PhpTypeKind.Union);
        var shape = Flatten(typeExpr).OfType<TyhpCallableShapeAst>().Should().ContainSingle().Subject;
        var returnType = shape.ReturnType.Should().BeOfType<PhpTypeExpressionAst>().Subject;
        returnType.TypeKind.Should().NotBe(PhpTypeKind.Union);
    }

    [Fact]
    public void Parse_CallableShapeReturnUnion_IsReturnIntOrNull()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type F = callable(int $x): int | null;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var alias = Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "F").Subject;
        var shape = TyhpCallableShapeAst.Find(alias.TypeExpression);
        shape.Should().NotBeNull();
        var returnType = shape!.ReturnType.Should().BeOfType<PhpTypeExpressionAst>().Subject;
        returnType.TypeKind.Should().Be(PhpTypeKind.Union);
        alias.TypeExpression.Should().BeOfType<PhpTypeExpressionAst>()
            .Which.TypeKind.Should().Be(PhpTypeKind.Simple);
    }

    [Fact]
    public void Parse_StructShapeAlias_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Point = struct { float $x; float $y; };
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>()
            .Should().ContainSingle(a => a.Identifier == "Point");
        var shape = Flatten(result.Ast).OfType<TyhpStructShapeAst>().Should().ContainSingle().Subject;
        shape.PropertyList!.GetAllNotNull().Should().HaveCount(2);
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().BeEmpty(
            "type-position struct shapes are not named struct declarations");
    }

    [Fact]
    public void Parse_InlineStructShapeParameter_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f(struct { int $n; } $s): void {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpStructShapeAst>().Should().ContainSingle();
    }

    [Fact]
    public void Parse_NamedStructDeclaration_DoesNotParse()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            struct Point { }
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "struct Name { } is no longer a declaration");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().BeEmpty();
        Flatten(result.Ast).OfType<TyhpTypeAliasAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_CallableGenericArguments_DoesNotParseAsCallableType()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type F = callable<string, int>;
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "callable<…> is not a callable type");
        Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().BeEmpty();
        var alias = Flatten(result.Ast).OfType<TyhpTypeAliasAst>().FirstOrDefault();
        if (alias?.TypeExpression is not null)
        {
            TyhpCallableShapeAst.Find(alias.TypeExpression).Should().BeNull();
        }
    }

    [Fact]
    public void Parse_PhpStanClosureSpelling_DoesNotParseAsClosureSignature()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function demo(\Closure(int): string $c): void {}
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "\\Closure(int): string is not a Tyhp type");
        Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_NewAnonymousStruct_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            $p = new struct { float $x; float $y; } with [x => 1.0, y => 2.0];
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        Flatten(result.Ast).OfType<TyhpStructDeclAst>().Should().ContainSingle()
            .Which.IsAnonymous.Should().BeTrue();
    }

    [Fact]
    public void Parse_InlineObjectShapeParameter_StillDoesNotParseAsShape()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            function f(object { } $x): void {}
            """);

        result.Diagnostics.HasErrors.Should().BeTrue(
            "object { } remains alias-RHS only");
    }

    [Fact]
    public void Parse_NestedCallable_IsRightAssociative()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type Curry = callable(bool $b): callable(string $s): int;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var outer = TyhpCallableShapeAst.Find(
            Flatten(result.Ast).OfType<TyhpTypeAliasAst>().Single().TypeExpression);
        outer.Should().NotBeNull();
        var inner = TyhpCallableShapeAst.Find(outer!.ReturnType);
        inner.Should().NotBeNull();
        inner!.ReturnType.Should().NotBeNull();
        TyhpCallableShapeAst.Find(inner.ReturnType).Should().BeNull();
    }

    [Fact]
    public void Parse_CallableShapeUnnamedVoidCast_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type F = callable(void): int;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var callable = Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle().Subject;
        var parameter = callable.Parameters!.GetAllNotNull().Should().ContainSingle().Subject;
        parameter.Variable.Should().BeNull();
        TypeSpelling(parameter.TypeExpression).Should().Be("void");
    }

    [Fact]
    public void Parse_CallableShapeNamedVoidParameter_StillParses()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            type F = callable(void $v): int;
            """);

        result.Diagnostics.Errors.Should().BeEmpty(
            $"parse errors: {Describe(result)}");
        var callable = Flatten(result.Ast).OfType<TyhpCallableShapeAst>().Should().ContainSingle().Subject;
        callable.Parameters!.GetAllNotNull().Should().ContainSingle()
            .Which.Variable.Should().NotBeNull();
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

        foreach (var addon in node.AstGrammarAddons.Values)
        {
            foreach (var nested in Flatten(addon))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(ParseResult result)
        => string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));

    private static string TypeSpelling(ITypeExpression? type) => type switch
    {
        PhpBuiltinTypeAst builtin => builtin.Identifier ?? "",
        PhpNamedTypeAst named when named.Name is PhpNameAst name =>
            name.ValueString ?? "",
        PhpTypeExpressionAst { TypeKind: PhpTypeKind.Simple, Types: { } types }
            when types.GetAllNotNull().Count() == 1 =>
            TypeSpelling(types.GetAllNotNull().Single()),
        _ => type?.ToString() ?? "",
    };
}
