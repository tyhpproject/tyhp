using Tyhp.Domain.Services.PhpDoc;

namespace Tyhp.Tests.Domain.Services.PhpDoc;

[Trait("Category", "Domain")]
[Trait("Category", "PhpDoc")]
public class PhpDocParserTests
{
    [Fact]
    public void Parse_StandardParamReturnThrows()
    {
        var block = PhpDocParser.Parse("""
            /**
             * Adds two numbers.
             *
             * @param int $a Left operand
             * @param string $b Right operand
             * @return int The sum
             * @throws \InvalidArgumentException When an argument is invalid
             */
            """);

        block.Summary.Should().Be("Adds two numbers.");
        block.ParamTags.Should().HaveCount(2);
        block.ParamTags[0].TagName.Should().Be("param");
        block.ParamTags[0].TypeExpression.Should().Be("int");
        block.ParamTags[0].ParameterName.Should().Be("a");
        block.ParamTags[0].Description.Should().Be("Left operand");
        block.ParamTags[1].TypeExpression.Should().Be("string");
        block.ParamTags[1].ParameterName.Should().Be("b");
        block.ReturnTag.Should().NotBeNull();
        block.ReturnTag!.TypeExpression.Should().Be("int");
        block.ReturnTag.Description.Should().Be("The sum");
        block.ThrowsTags.Should().ContainSingle();
        block.ThrowsTags[0].TypeExpression.Should().Be(@"\InvalidArgumentException");
        block.ThrowsTags[0].Description.Should().Be("When an argument is invalid");
    }

    [Fact]
    public void Parse_MultiLineDescription()
    {
        var block = PhpDocParser.Parse("""
            /**
             * Short summary.
             *
             * This is a longer description that spans
             * multiple lines and has a blank line above.
             *
             * Still more description.
             *
             * @param string $name A name
             */
            """);

        block.Summary.Should().Be("Short summary.");
        block.Description.Should().Be(
            "This is a longer description that spans\nmultiple lines and has a blank line above.\n\nStill more description.");
        block.ParamTags.Should().ContainSingle();
        block.ParamTags[0].ParameterName.Should().Be("name");
    }

    [Fact]
    public void Parse_MultiLineTagDescription()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @param string $name A long description
             *     that continues on the next line
             *     and another.
             * @return void
             */
            """);

        block.ParamTags.Should().ContainSingle();
        block.ParamTags[0].ParameterName.Should().Be("name");
        block.ParamTags[0].TypeExpression.Should().Be("string");
        block.ParamTags[0].Description.Should().Be("A long description\n    that continues on the next line\n    and another.");
    }

    [Fact]
    public void Parse_TemplateWithConstraint()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @template T
             * @template TValue of \Foo\Bar
             * @param T $item
             * @return TValue
             */
            """);

        block.TemplateTags.Should().HaveCount(2);
        block.TemplateTags[0].ParameterName.Should().Be("T");
        block.TemplateTags[0].TypeExpression.Should().BeNull();
        block.TemplateTags[1].ParameterName.Should().Be("TValue");
        block.TemplateTags[1].TypeExpression.Should().Be(@"\Foo\Bar");
        block.ParamTags[0].TypeExpression.Should().Be("T");
        block.ReturnTag!.TypeExpression.Should().Be("TValue");
    }

    [Fact]
    public void Parse_TemplateAsConstraintAndImplements()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @template TObject as object
             * @template TArrayValue
             * @template-implements ArrayAccess<TObject, TArrayValue>
             * @implements Iterator<int, TObject>
             */
            """);

        block.TemplateTags.Should().HaveCount(2);
        block.TemplateTags[0].ParameterName.Should().Be("TObject");
        block.TemplateTags[0].TypeExpression.Should().Be("object");
        block.TemplateTags[1].ParameterName.Should().Be("TArrayValue");
        block.TemplateTags[1].TypeExpression.Should().BeNull();

        var implements = block.Tags.Where(t => t.TagName is "template-implements" or "implements").ToList();
        implements.Should().HaveCount(2);
        implements[0].TypeExpression.Should().Be("ArrayAccess<TObject, TArrayValue>");
        implements[1].TypeExpression.Should().Be("Iterator<int, TObject>");
    }

    [Fact]
    public void Parse_DeprecatedAndInternal()
    {
        var block = PhpDocParser.Parse("""
            /**
             * Old API.
             *
             * @deprecated Use NewApi instead
             * @internal
             */
            """);

        block.IsDeprecated.Should().BeTrue();
        block.IsInternal.Should().BeTrue();
        block.DeprecatedTag.Should().NotBeNull();
        block.DeprecatedTag!.Description.Should().Be("Use NewApi instead");
        block.Tags.Should().Contain(t => t.TagName == "internal");
    }

    [Fact]
    public void Parse_MethodAndPropertyMagicTags()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @method string getName()
             * @method static int getCount() Fetch the count
             * @method setName(string $name) Sets the name
             * @property string $email
             * @property-read int $id The identifier
             * @property-write array $meta
             */
            """);

        block.MethodTags.Should().HaveCount(3);
        block.MethodTags[0].ParameterName.Should().Be("getName");
        block.MethodTags[0].TypeExpression.Should().Be("string");
        block.MethodTags[1].ParameterName.Should().Be("getCount");
        block.MethodTags[1].TypeExpression.Should().Be("int");
        block.MethodTags[1].Description.Should().Be("Fetch the count");
        block.MethodTags[1].RawContent.Should().StartWith("static");
        block.MethodTags[2].ParameterName.Should().Be("setName");
        block.MethodTags[2].TypeExpression.Should().BeNull();

        block.PropertyTags.Should().HaveCount(3);
        block.PropertyTags[0].TagName.Should().Be("property");
        block.PropertyTags[0].TypeExpression.Should().Be("string");
        block.PropertyTags[0].ParameterName.Should().Be("email");
        block.PropertyTags[1].TagName.Should().Be("property-read");
        block.PropertyTags[1].TypeExpression.Should().Be("int");
        block.PropertyTags[1].ParameterName.Should().Be("id");
        block.PropertyTags[1].Description.Should().Be("The identifier");
        block.PropertyTags[2].TagName.Should().Be("property-write");
        block.PropertyTags[2].ParameterName.Should().Be("meta");
    }

    [Fact]
    public void Parse_PhpStanParamTakesPrecedenceOverParam()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @param array $items
             * @phpstan-param list<string> $items
             * @param int $count
             * @return mixed
             * @phpstan-return string
             */
            """);

        block.ParamTags.Should().HaveCount(2);
        var items = block.ParamTags.Single(t => t.ParameterName == "items");
        items.TagName.Should().Be("phpstan-param");
        items.TypeExpression.Should().Be("list<string>");
        block.ParamTags.Single(t => t.ParameterName == "count").TypeExpression.Should().Be("int");
        block.ReturnTag!.TagName.Should().Be("phpstan-return");
        block.ReturnTag.TypeExpression.Should().Be("string");
        block.Tags.Should().Contain(t => t.TagName == "param" && t.ParameterName == "items");
    }

    [Fact]
    public void Parse_PsalmParamTakesPrecedenceOverParam_PhpStanWinsOverPsalm()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @param mixed $value
             * @psalm-param string $value
             * @phpstan-param int $value
             * @psalm-return bool
             * @return mixed
             */
            """);

        block.ParamTags.Should().ContainSingle();
        block.ParamTags[0].TagName.Should().Be("phpstan-param");
        block.ParamTags[0].TypeExpression.Should().Be("int");
        block.ReturnTag!.TagName.Should().Be("psalm-return");
        block.ReturnTag.TypeExpression.Should().Be("bool");
    }

    [Fact]
    public void Parse_EmptyComment_ReturnsEmptyBlock()
    {
        var blank = PhpDocParser.Parse("");
        blank.Summary.Should().BeEmpty();
        blank.Description.Should().BeEmpty();
        blank.Tags.Should().BeEmpty();

        var empty = PhpDocParser.Parse("/** */");
        empty.Summary.Should().BeEmpty();
        empty.Description.Should().BeEmpty();
        empty.Tags.Should().BeEmpty();
        empty.IsDeprecated.Should().BeFalse();
        empty.IsInternal.Should().BeFalse();
        empty.ReturnTag.Should().BeNull();

        PhpDocParser.Parse(null!).Summary.Should().BeEmpty();
    }

    [Fact]
    public void Parse_MalformedTags_DoNotThrow()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @param
             * @param $onlyName
             * @param string
             * @return
             * @template
             * @method
             * @throws
             */
            """);

        block.Tags.Should().HaveCount(7);
        block.ParamTags.Should().Contain(t => t.ParameterName == "onlyName" && t.TypeExpression == null);
        block.ParamTags.Should().Contain(t => t.TypeExpression == "string" && t.ParameterName == null);
        block.ReturnTag.Should().NotBeNull();
        block.TemplateTags.Should().ContainSingle();
        block.MethodTags.Should().ContainSingle();
        block.ThrowsTags.Should().ContainSingle();
    }

    [Fact]
    public void Parse_OneLineVarTag()
    {
        var block = PhpDocParser.Parse("/** @var string[] $items */");
        block.VarTag.Should().NotBeNull();
        block.VarTag!.TypeExpression.Should().Be("string[]");
        block.VarTag.ParameterName.Should().Be("items");
    }

    [Fact]
    public void Parse_VarWithoutName()
    {
        var block = PhpDocParser.Parse("/** @var int */");
        block.VarTag!.TypeExpression.Should().Be("int");
        block.VarTag.ParameterName.Should().BeNull();
    }

    [Fact]
    public void Parse_ParamOutAndAssertStayInTags()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @param string $path
             * @param-out string $path
             * @assert int $n
             */
            """);

        block.ParamTags.Should().ContainSingle();
        block.ParamTags[0].TagName.Should().Be("param");
        block.Tags.Should().Contain(t => t.TagName == "param-out" && t.ParameterName == "path");
        block.Tags.Should().Contain(t => t.TagName == "assert" && t.ParameterName == "n" && t.TypeExpression == "int");
    }

    [Fact]
    public void Parse_CallableAndArrayShapeParamTypes()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @param callable(int, string): bool $callback
             * @param array{name: string, age: int} $row
             */
            """);

        block.ParamTags.Should().HaveCount(2);
        block.ParamTags[0].ParameterName.Should().Be("callback");
        block.ParamTags[0].TypeExpression.Should().Be("callable(int, string): bool");
        block.ParamTags[1].ParameterName.Should().Be("row");
        block.ParamTags[1].TypeExpression.Should().Be("array{name: string, age: int}");
    }

    [Fact]
    public void Parse_MethodWithCallableReturnType()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @method callable(int): void tap(int $n)
             */
            """);

        block.MethodTags.Should().ContainSingle();
        block.MethodTags[0].ParameterName.Should().Be("tap");
        block.MethodTags[0].TypeExpression.Should().Be("callable(int): void");
    }

    [Fact]
    public void Parse_DollarSignInDescriptionIsNotMistakenForParameterName()
    {
        // No "$name" is actually given for this @param (malformed but real-world); the "$USD"
        // later in the prose must not be mistaken for the parameter name, swallowing "Price in".
        var block = PhpDocParser.Parse("""
            /**
             * @param string Price in $USD for the order
             * @return int Count of $items processed
             */
            """);

        block.ParamTags.Should().ContainSingle();
        block.ParamTags[0].ParameterName.Should().BeNull();
        block.ParamTags[0].TypeExpression.Should().Be("string");
        block.ParamTags[0].Description.Should().Be("Price in $USD for the order");
        block.ReturnTag!.Description.Should().Be("Count of $items processed");
    }

    [Fact]
    public void Parse_DollarSignAdjacentToTypeIsStillTreatedAsParameterName()
    {
        var block = PhpDocParser.Parse("/** @param string $amount Price in $USD */");

        block.ParamTags.Should().ContainSingle();
        block.ParamTags[0].ParameterName.Should().Be("amount");
        block.ParamTags[0].TypeExpression.Should().Be("string");
        block.ParamTags[0].Description.Should().Be("Price in $USD");
    }

    [Fact]
    public void Parse_PhpStanAndPsalmTypeAliases()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @phpstan-type Options array{index: string, type: string}
             * @phpstan-type InputOptions array{index?: string}
             * @psalm-type UserId = int|string
             * @phan-type Flags = array<string, bool>
             */
            """);

        block.TypeAliasTags.Should().HaveCount(4);
        var options = block.TypeAliasTags.Single(t => t.ParameterName == "Options");
        options.TagName.Should().Be("phpstan-type");
        options.TypeExpression.Should().StartWith("array{");
        block.TypeAliasTags.Single(t => t.ParameterName == "UserId").TypeExpression.Should().Be("int|string");
        block.TypeAliasTags.Single(t => t.ParameterName == "Flags").TypeExpression.Should().Be("array<string, bool>");
    }

    [Fact]
    public void Parse_PhpStanType_WinsOverPsalmAndPhanForSameName()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @phan-type Options = int
             * @psalm-type Options = string
             * @phpstan-type Options array{ok: bool}
             */
            """);

        block.TypeAliasTags.Should().ContainSingle();
        block.TypeAliasTags[0].TagName.Should().Be("phpstan-type");
        block.TypeAliasTags[0].TypeExpression.Should().StartWith("array{");
    }

    [Fact]
    public void Parse_ImportType_WithAndWithoutAs()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @phpstan-import-type Options from SearchHandler
             * @phpstan-import-type InputOptions from \Acme\Handler\StoreHandler as CouchInput
             */
            """);

        block.ImportTypeTags.Should().HaveCount(2);
        var options = block.ImportTypeTags.Single(t => t.ParameterName == "Options");
        options.TypeExpression.Should().Be("SearchHandler");
        options.Description.Should().Be("Options");
        var couch = block.ImportTypeTags.Single(t => t.ParameterName == "CouchInput");
        couch.TypeExpression.Should().Be(@"\Acme\Handler\StoreHandler");
        couch.Description.Should().Be("InputOptions");
    }

    [Fact]
    public void Parse_TagsPreserveRawContent()
    {
        var block = PhpDocParser.Parse("/** @param int $count How many */");
        block.ParamTags[0].RawContent.Should().Be("int $count How many");
        block.ParamTags[0].TagName.Should().Be("param");
    }

    [Fact]
    public void Parse_ReturnType_DoesNotTreatHtmlParagraphAsGeneric()
    {
        var block = PhpDocParser.Parse("""
            /**
             * Generates a backtrace
             * @return array <p>
             * Returns an associative array. The possible returned elements
             * are as follows:
             * </p>
             */
            """);

        block.ReturnTag.Should().NotBeNull();
        block.ReturnTag!.TypeExpression.Should().Be("array");
        block.ReturnTag.Description.Should().StartWith("<p>");
    }

    [Fact]
    public void Parse_ParamType_DoesNotTreatHtmlBoldAsGeneric()
    {
        var block = PhpDocParser.Parse("""
            /**
             * @param array $data <p>
             * The array representation of the object.
             * </p>
             * @return array <b>list of driver names</b>
             */
            """);

        block.ParamTags.Should().ContainSingle();
        block.ParamTags[0].TypeExpression.Should().Be("array");
        block.ParamTags[0].ParameterName.Should().Be("data");
        block.ReturnTag!.TypeExpression.Should().Be("array");
        block.ReturnTag.Description.Should().StartWith("<b>");
    }
}
