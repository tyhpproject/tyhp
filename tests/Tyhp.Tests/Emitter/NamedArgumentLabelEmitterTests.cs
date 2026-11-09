using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// A named-argument label is an identifier, even when a class has the same
/// spelling. Class lookup is case-insensitive, so qualifying that label emits
/// PHP that does not parse.
/// </summary>
[Trait("Category", "Emitter")]
public class NamedArgumentLabelEmitterTests
{
    private const string Declarations = """
        <?tyhp
        namespace Tyhp\Rhetoric\Attributes;

        class Column {
            public function __construct(public string $name = '') {}
        }

        interface Index {}

        enum ForeignKey { case A; }

        trait Table {}

        class Operation {
            public function __construct(public string $table, public Column $column) {}

            public function with(Column $column, string $index = ''): Operation {
                return new Operation($this->table, column: $column);
            }

            public static function make(Column $column, string $table = 'posts'): Operation {
                return new Operation($table, column: $column);
            }
        }

        function build(Column $column, string $table = 'posts'): Operation {
            return new Operation($table, column: $column);
        }

        function describe(int $index = 0, int $foreignKey = 0, int $table = 0): void {}
        """;

    private static string EmitCaller(string body)
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["attrs.tyhp"] = Declarations,
            ["caller.tyhp"] = """
                <?tyhp
                namespace Tyhp\Rhetoric\Migrations;

                use Tyhp\Rhetoric\Attributes\Column;
                use Tyhp\Rhetoric\Attributes\ForeignKey;
                use Tyhp\Rhetoric\Attributes\Index;
                use Tyhp\Rhetoric\Attributes\Operation;
                use Tyhp\Rhetoric\Attributes\Table;
                use function Tyhp\Rhetoric\Attributes\build;

                """ + body,
        });

        result.Errors.Should().BeEmpty();
        return result.Php;
    }

    [Fact]
    public void NamedArgument_LabelMatchesInScopeClass_EmitsOriginalLabel()
    {
        var php = EmitCaller("""
            function add(Column $column): Operation {
                return new Operation('posts', column: $column);
            }
            """);

        php.Should().Contain("new \\Tyhp\\Rhetoric\\Attributes\\Operation('posts', column: $column)");
        php.Should().NotContain("\\Column:");
    }

    [Fact]
    public void NamedArgument_ConstructorCall_LabelStaysUnqualifiedAcrossFilesWithoutImport()
    {
        // The class is in the label's own namespace, not imported by name.
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["attrs.tyhp"] = Declarations,
            ["same.tyhp"] = """
                <?tyhp
                namespace Tyhp\Rhetoric\Attributes;

                function add(Column $column): Operation {
                    return new Operation('posts', column: $column);
                }
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("column: $column");
        result.Php.Should().NotContain("\\Column:");
        result.Php.Should().NotContain("Column: $");
    }

    [Theory]
    [InlineData("column")]
    [InlineData("Column")]
    [InlineData("COLUMN")]
    public void NamedArgument_LabelCaseInsensitivelyMatchesClass_KeepsWrittenSpelling(string label)
    {
        var php = EmitCaller($$"""
            function add(Column $column): Operation {
                return build({{label}}: $column);
            }
            """);

        php.Should().Contain($"\\Tyhp\\Rhetoric\\Attributes\\build({label}: $column)");
        php.Should().NotContain("\\Column:");
    }

    [Theory]
    [InlineData("index")]
    [InlineData("foreignKey")]
    [InlineData("table")]
    public void NamedArgument_LabelMatchesInterfaceEnumOrTrait_KeepsWrittenSpelling(string label)
    {
        var php = EmitCaller($$"""
            use function Tyhp\Rhetoric\Attributes\describe;

            function call(int $index, int $foreignKey, int $table): void {
                describe({{label}}: ${{label}});
            }
            """);

        php.Should().Contain($"describe({label}: ${label})");
        php.Should().NotContain("\\Index:");
        php.Should().NotContain("\\ForeignKey:");
        php.Should().NotContain("\\Table:");
    }

    [Fact]
    public void NamedArgument_MethodAndStaticCall_KeepLabel()
    {
        var php = EmitCaller("""
            function add(Column $column, Operation $op): void {
                $op->with(column: $column, index: 'x');
                Operation::make(column: $column);
            }
            """);

        php.Should().Contain("->with(column: $column, index: 'x')");
        php.Should().Contain("\\Tyhp\\Rhetoric\\Attributes\\Operation::make(column: $column)");
        php.Should().NotContain("\\Column:");
        php.Should().NotContain("\\Index:");
    }

    [Fact]
    public void NamedArgument_ValueExpressionStillQualifiesTypeNames()
    {
        var php = EmitCaller("""
            function add(): Operation {
                return new Operation(
                    table: Column::class,
                    column: new Column(name: 'x'),
                );
            }
            """);

        // The labels stay written; the class named in each value is qualified.
        php.Should().Contain("table: \\Tyhp\\Rhetoric\\Attributes\\Column::class");
        php.Should().Contain("column: new \\Tyhp\\Rhetoric\\Attributes\\Column(name: 'x')");
    }

    [Fact]
    public void NamedArgument_LabelAndIdenticalClassValue_QualifiesOnlyTheValue()
    {
        var php = EmitCaller("""
            function add(): Operation {
                return build(column: new Column());
            }
            """);

        php.Should().Contain("build(column: new \\Tyhp\\Rhetoric\\Attributes\\Column())");
    }

    [Fact]
    public void NamedArgument_ExtensionMethodCall_KeepsLabel()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["attrs.tyhp"] = Declarations,
            ["ext.tyhp"] = """
                <?tyhp
                namespace Tyhp\Rhetoric\Migrations;

                use Tyhp\Rhetoric\Attributes\Column;
                use Tyhp\Rhetoric\Attributes\Operation;

                extension OperationExt extends Operation {
                    function rebuild(Column $column, string $table = ''): Operation {
                        return new Operation($table, column: $column);
                    }
                }

                function add(Operation $op, Column $column): Operation {
                    return $op->rebuild(column: $column, table: 't');
                }
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("column: $column");
        result.Php.Should().NotContain("\\Column:");
    }

    [Fact]
    public void NamedArgument_ExtensionMethodWithMultiStatementBody_KeepsLabelAtCallSite()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["attrs.tyhp"] = Declarations,
            ["ext.tyhp"] = """
                <?tyhp
                namespace Tyhp\Rhetoric\Migrations;

                use Tyhp\Rhetoric\Attributes\Column;
                use Tyhp\Rhetoric\Attributes\Operation;

                extension OperationExt extends Operation {
                    function rebuild(Column $column, string $table = ''): Operation {
                        Operation $next = new Operation($table, column: $column);
                        return $next;
                    }
                }

                function add(Operation $op, Column $column): Operation {
                    return $op->rebuild(column: $column, table: 't');
                }
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("::rebuild($op, column: $column, table: 't')");
        result.Php.Should().NotContain("\\Column:");
    }

    [Fact]
    public void NamedArgument_Attribute_KeepsLabel()
    {
        var result = SnippetEmit.Compile(new Dictionary<string, string>
        {
            ["attrs.tyhp"] = """
                <?tyhp
                namespace Tyhp\Rhetoric\Attributes;

                #[\Attribute]
                class Column {
                    public function __construct(public string $name = '', public string $type = '') {}
                }
                """,
            ["model.tyhp"] = """
                <?tyhp
                namespace Tyhp\Rhetoric\Models;

                use Tyhp\Rhetoric\Attributes\Column;

                class Post {
                    #[Column(name: 'title', type: 'string')]
                    public string $title = '';
                }
                """,
        });

        result.Errors.Should().BeEmpty();
        result.Php.Should().Contain("#[\\Tyhp\\Rhetoric\\Attributes\\Column(name: 'title', type: 'string')]");
    }

    [Fact]
    public void NamedArgument_PositionalThenLabels_KeepEveryLabel()
    {
        var php = EmitCaller("""
            use function Tyhp\Rhetoric\Attributes\describe;

            function add(): void {
                describe(1, foreignKey: 2, table: 3);
            }
            """);

        php.Should().Contain("describe(1, foreignKey: 2, table: 3)");
        php.Should().NotContain("\\ForeignKey:");
        php.Should().NotContain("\\Table:");
    }
}
