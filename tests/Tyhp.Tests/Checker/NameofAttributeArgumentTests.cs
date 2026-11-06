using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>nameof(Type::$prop)</c> is a constant <c>__PropertyName&lt;Type&gt;</c>, so an attribute
/// parameter of that type accepts it and rejects a name that is not a property of that class.
/// </summary>
[Trait("Category", "Checker")]
public class NameofAttributeArgumentTests
{
    [Fact]
    public void Check_NameofProperty_IsAcceptedByAttributeParameter()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
            }

            #[\Attribute(\Attribute::TARGET_CLASS)]
            class Label {
                public function __construct(
                    public __PropertyName<Widget> $property,
                ) {}
            }

            #[Label(nameof(Widget::$name))]
            class Ok {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_NameofPropertyOfAnotherClass_IsRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
            }

            class Other {
                public string $name;
            }

            #[\Attribute(\Attribute::TARGET_CLASS)]
            class Label {
                public function __construct(
                    public __PropertyName<Widget> $property,
                ) {}
            }

            #[Label(nameof(Other::$name))]
            class Bad {}
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_NameofMissingProperty_IsRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
            }

            #[\Attribute(\Attribute::TARGET_CLASS)]
            class Label {
                public function __construct(
                    public __PropertyName<Widget> $property,
                ) {}
            }

            #[Label(nameof(Widget::$missing))]
            class Bad {}
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    private const string GenericAttributeFixture = """
        <?tyhp
        class Widget {
            public string $name;
        }

        class Other {
            public string $name;
        }

        #[\Attribute(\Attribute::TARGET_PROPERTY)]
        class InverseOf<T> {
            public function __construct(
                public __PropertyName<T> $property,
            ) {}
        }

        """;

    [Fact]
    public void Check_GenericAttribute_AcceptsNameofOfTypeArgument()
    {
        var diagnostics = CompileAndCheck(GenericAttributeFixture + """
            class Holder {
                #[InverseOf<Widget>(nameof(Widget::$name))]
                public string $x;
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_GenericAttribute_RejectsNameofOfAnotherClass()
    {
        var diagnostics = CompileAndCheck(GenericAttributeFixture + """
            class Holder {
                #[InverseOf<Widget>(nameof(Other::$name))]
                public string $x;
            }
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_GenericAttribute_RejectsLiteralThatIsNotAProperty()
    {
        var diagnostics = CompileAndCheck(GenericAttributeFixture + """
            class Holder {
                #[InverseOf<Widget>('missing')]
                public string $x;
            }
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_GenericAttribute_AcceptsLiteralThatNamesAProperty()
    {
        var diagnostics = CompileAndCheck(GenericAttributeFixture + """
            class Holder {
                #[InverseOf<Widget>('name')]
                public string $x;
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_GenericAttribute_LambdaArgumentIsNotConstant()
    {
        var diagnostics = CompileAndCheck(GenericAttributeFixture + """
            class Holder {
                #[InverseOf<Widget>(fn (Widget $w) => $w->name)]
                public string $x;
            }
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NamespacedClassAttribute_AcceptsNameofOfItsOwnClass()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp

            namespace App\Models;

            #[\Attribute(\Attribute::TARGET_CLASS | \Attribute::IS_REPEATABLE)]
            class Index<T> {
                public function __construct(
                    public array<int, __PropertyName<T>> $properties = [],
                ) {}
            }

            #[Index<Post>([nameof(Post::$title), nameof(Post::$slug)])]
            class Post {
                public string $title = '';
                public string $slug = '';
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_NamespacedClassAttribute_RejectsNameofOfAnotherClass()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp

            namespace App\Models;

            #[\Attribute(\Attribute::TARGET_CLASS | \Attribute::IS_REPEATABLE)]
            class Index<T> {
                public function __construct(
                    public array<int, __PropertyName<T>> $properties = [],
                ) {}
            }

            class Comment {
                public string $title = '';
            }

            #[Index<Post>([nameof(Comment::$title)])]
            class Post {
                public string $title = '';
            }
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
        UserErrors(diagnostics).Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    private const string NamespacedAttributeFixture = """
        <?tyhp

        namespace App\Models;

        #[\Attribute(\Attribute::TARGET_CLASS | \Attribute::TARGET_FUNCTION)]
        class Index<T> {
            public function __construct(
                public array<int, __PropertyName<T>> $properties = [],
            ) {}
        }

        class Comment {
            public string $title = '';
        }

        """;

    [Fact]
    public void Check_NamespacedClassAttribute_RejectsMissingProperty()
    {
        var diagnostics = CompileAndCheck(NamespacedAttributeFixture + """
            #[Index<Post>([nameof(Post::$missing)])]
            class Post {
                public string $title = '';
            }
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_NamespacedClassAttribute_LambdaIsNotConstant()
    {
        var diagnostics = CompileAndCheck(NamespacedAttributeFixture + """
            #[Index<Post>([fn (Post $p) => $p->title])]
            class Post {
                public string $title = '';
            }
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NamespacedClassAttribute_LiteralMustNameAProperty()
    {
        var rejected = CompileAndCheck(NamespacedAttributeFixture + """
            #[Index<Post>(['nope'])]
            class Post {
                public string $title = '';
            }
            """);
        UserErrors(rejected).Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType
            || d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_NamespacedFunctionAttribute_AcceptsNameofOfTypeArgument()
    {
        var diagnostics = CompileAndCheck(NamespacedAttributeFixture + """
            #[Index<Comment>([nameof(Comment::$title)])]
            function indexed(): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_NamespacedFunctionAttribute_RejectsForeignAndMissingAndLambda()
    {
        var foreign = CompileAndCheck(NamespacedAttributeFixture + """
            class Other {
                public string $title = '';
            }

            #[Index<Comment>([nameof(Other::$title)])]
            function indexed(): void {}
            """);
        UserErrors(foreign).Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
        UserErrors(foreign).Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);

        var missing = CompileAndCheck(NamespacedAttributeFixture + """
            #[Index<Comment>([nameof(Comment::$missing)])]
            function indexed(): void {}
            """);
        UserErrors(missing).Should().Contain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);

        var lambda = CompileAndCheck(NamespacedAttributeFixture + """
            #[Index<Comment>([fn (Comment $c) => $c->title])]
            function indexed(): void {}
            """);
        UserErrors(lambda).Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NamespacedClassAttribute_ResolvesImportedClasses()
    {
        var diagnostics = CompileAndCheck(
            """
            <?tyhp

            namespace App\Attributes;

            #[\Attribute(\Attribute::TARGET_CLASS)]
            class Index<T> {
                public function __construct(
                    public array<int, __PropertyName<T>> $properties = [],
                ) {}
            }
            """,
            """
            <?tyhp

            namespace App\Domain;

            class Comment {
                public string $title = '';
            }
            """,
            """
            <?tyhp

            namespace App\Models;

            use App\Attributes\Index;
            use App\Domain\Comment;

            #[Index<Comment>([nameof(Comment::$title)])]
            class Post {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_NamespacedClassAttribute_ImportedTypeArgumentStillChecksProperties()
    {
        var diagnostics = CompileAndCheck(
            """
            <?tyhp

            namespace App\Attributes;

            #[\Attribute(\Attribute::TARGET_CLASS)]
            class Index<T> {
                public function __construct(
                    public array<int, __PropertyName<T>> $properties = [],
                ) {}
            }
            """,
            """
            <?tyhp

            namespace App\Domain;

            class Comment {
                public string $title = '';
            }
            """,
            """
            <?tyhp

            namespace App\Models;

            use App\Attributes\Index;
            use App\Domain\Comment;

            #[Index<Comment>([nameof(Comment::$missing)])]
            class Post {}
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    private const string ClassAttributeFixture = """
        <?tyhp
        class Widget {}

        #[\Attribute(\Attribute::TARGET_PROPERTY)]
        class Reference {
            public function __construct(
                public __ClassName $target,
                public ?string $column = null,
            ) {}
        }

        """;

    [Fact]
    public void Check_ClassAttribute_AcceptsClassConstant()
    {
        var diagnostics = CompileAndCheck(ClassAttributeFixture + """
            class Holder {
                #[Reference(Widget::class, column: 'id')]
                public int $widgetId;
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_ClassAttribute_RejectsStringThatIsNotAClass()
    {
        var diagnostics = CompileAndCheck(ClassAttributeFixture + """
            class Holder {
                #[Reference('NoSuchClass')]
                public int $widgetId;
            }
            """);

        UserErrors(diagnostics).Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType
            || d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    private const string ListAttributeFixture = """
        <?tyhp
        class Widget {
            public string $name;
            public string $slug;
        }

        class Other {
            public string $name;
        }

        #[\Attribute(\Attribute::TARGET_CLASS | \Attribute::TARGET_PROPERTY)]
        class Index<T> {
            public function __construct(
                public array<int, __PropertyName<T>> $properties = [],
                public bool $unique = false,
                public ?string $name = null,
            ) {}
        }

        """;

    [Fact]
    public void Check_ListAttribute_AcceptsNameofList()
    {
        var diagnostics = CompileAndCheck(ListAttributeFixture + """
            #[Index<Widget>([nameof(Widget::$name), nameof(Widget::$slug)], unique: true, name: 'ix')]
            class Holder {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_ListAttribute_RejectsNameofOfAnotherClass()
    {
        var diagnostics = CompileAndCheck(ListAttributeFixture + """
            #[Index<Widget>([nameof(Widget::$name), nameof(Other::$name)])]
            class Holder {}
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_ListAttribute_RejectsLiteralThatIsNotAProperty()
    {
        var diagnostics = CompileAndCheck(ListAttributeFixture + """
            #[Index<Widget>(['name', 'missing'])]
            class Holder {}
            """);

        UserErrors(diagnostics).Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleArgumentType
            || d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_ListAttribute_WithoutTypeArgument_PropertyAttributeOnlyUsesNamedFlags()
    {
        var diagnostics = CompileAndCheck(ListAttributeFixture + """
            class Holder {
                #[Index(unique: true)]
                public string $slug;
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_ListAttribute_WithoutTypeArgument_RejectsPlainStrings()
    {
        var diagnostics = CompileAndCheck(ListAttributeFixture + """
            class Holder {
                #[Index(['nonsense'])]
                public string $slug;
            }
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(DiagnosticBag diagnostics) =>
        diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static DiagnosticBag CompileAndCheck(params string[] contents)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePaths = new List<string>();
        for (var i = 0; i < contents.Length; i++)
        {
            var path = Path.Combine(tempDir, $"test{i}.tyhp");
            File.WriteAllText(path, contents[i]);
            filePaths.Add(path);
        }

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles(filePaths, options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(
                result.Diagnostics, symbolTree, result.GlobalScope!, options.Checker);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
