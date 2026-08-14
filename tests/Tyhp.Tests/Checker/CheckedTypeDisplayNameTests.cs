using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Display-only normalization for nullable / union <see cref="ICheckedType.DisplayName"/>
/// (FOUND top-type audit #8). Does not change assignability.
/// </summary>
[Trait("Category", "Checker")]
public class CheckedTypeDisplayNameTests
{
    private static ICheckedType Named(string fqn) =>
        CheckedTypes.FromSymbol(new ObjectDeclarationSymbol(fqn));

    [Fact]
    public void DisplayName_RedundantT_NullableT_Null_CollapsesToQuestionT()
    {
        var t = Named(@"\Tyhp\Type");
        var union = new UnionCheckedType([t, new NullableCheckedType(t), CheckedTypes.Null]);

        union.DisplayName.Should().Be(@"?\Tyhp\Type");
    }

    [Fact]
    public void DisplayName_StringOrNull_Union_UsesQuestionForm()
    {
        var union = new UnionCheckedType([CheckedTypes.String, CheckedTypes.Null]);

        union.DisplayName.Should().Be("?string");
    }

    [Fact]
    public void DisplayName_NullableOfUnion_UsesExplicitNull()
    {
        var inner = new UnionCheckedType([CheckedTypes.Int, CheckedTypes.String]);
        var nullable = new NullableCheckedType(inner);

        nullable.DisplayName.Should().Be("int|string|null");
        nullable.DisplayName.Should().NotContain("?(");
    }

    [Fact]
    public void DisplayName_MultiMemberUnionWithNullableMember_UsesExplicitNull()
    {
        var union = new UnionCheckedType([
            CheckedTypes.Int,
            new NullableCheckedType(CheckedTypes.String),
        ]);

        union.DisplayName.Should().Be("int|string|null");
        union.DisplayName.Should().NotContain("?");
    }

    [Fact]
    public void DisplayName_DuplicateMembers_AreDeduped()
    {
        var union = new UnionCheckedType([
            CheckedTypes.String,
            CheckedTypes.String,
            CheckedTypes.Null,
            CheckedTypes.Null,
        ]);

        union.DisplayName.Should().Be("?string");
    }

    [Fact]
    public void DisplayName_NestedNullable_Flattens()
    {
        var nested = new NullableCheckedType(new NullableCheckedType(CheckedTypes.Int));

        nested.DisplayName.Should().Be("?int");
    }

    [Fact]
    public void DisplayName_PlainNullable_StillQuestionForm()
    {
        new NullableCheckedType(CheckedTypes.String).DisplayName.Should().Be("?string");
    }

    [Fact]
    public void DisplayName_UnionWithoutNull_UnchangedJoin()
    {
        var union = new UnionCheckedType([CheckedTypes.Int, CheckedTypes.String]);

        union.DisplayName.Should().Be("int|string");
    }

    [Fact]
    public void DisplayName_NestedUnionFlattened()
    {
        var nested = new UnionCheckedType([
            CheckedTypes.Int,
            new UnionCheckedType([CheckedTypes.String, CheckedTypes.Null]),
        ]);

        nested.DisplayName.Should().Be("int|string|null");
    }

    [Fact]
    public void DisplayName_GenericTypeArg_NormalizesNullableUnion()
    {
        var arrayBase = CheckedTypes.FromSymbol(new BuiltInTypeSymbol("array"));
        var generic = new GenericCheckedType(
            arrayBase,
            [new UnionCheckedType([CheckedTypes.String, CheckedTypes.Null])]);

        generic.DisplayName.Should().Be("array<?string>");
    }

    [Fact]
    public void DisplayName_NullableIntersection_ParenthesizesIntersection()
    {
        var intersection = new IntersectionCheckedType([Named(@"\A"), Named(@"\B")]);
        var nullable = new NullableCheckedType(intersection);

        nullable.DisplayName.Should().Be(@"?(\A&\B)");
    }

    [Fact]
    public void DisplayName_UnionOfIntersectionAndNull_ParenthesizesIntersection()
    {
        var intersection = new IntersectionCheckedType([Named(@"\A"), Named(@"\B")]);
        var union = new UnionCheckedType([intersection, CheckedTypes.Null]);

        union.DisplayName.Should().Be(@"?(\A&\B)");
    }

    [Fact]
    public void DisplayName_MultiMemberUnionWithIntersectionMember_NoSpuriousParens()
    {
        var intersection = new IntersectionCheckedType([Named(@"\A"), Named(@"\B")]);
        var union = new UnionCheckedType([intersection, CheckedTypes.Int, CheckedTypes.Null]);

        union.DisplayName.Should().Be(@"\A&\B|int|null");
    }
}
