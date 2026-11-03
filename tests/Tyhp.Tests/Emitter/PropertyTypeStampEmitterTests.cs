using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;
using Xunit.Abstractions;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// <c>#[\Tyhp\PropertyType('…')]</c> on properties and promoted constructor parameters whose Tyhp
/// type is lossy in the PHP hint.
/// </summary>
[Trait("Category", "Emitter")]
public class PropertyTypeStampEmitterTests
{
    private readonly ITestOutputHelper _output;

    public PropertyTypeStampEmitterTests(ITestOutputHelper output)
    {
        this._output = output;
    }

    private const string Sample = """
        <?tyhp
        namespace App;
        type UserId = int;
        type Address = struct { string $city; };
        class Post { public string $title = ''; }
        class Table<T> { public array<T> $rows = []; public T|null $first = null; }
        class Blog {
            public array<Post> $posts = [];
            public array<string, Post> $byKey = [];
            public ?Table<Blog> $blogs = null;
            public UserId $owner = 0;
            public Address $address;
            public array<Address> $addresses = [];
            public ?Post $featured = null;
            public int|string $mixedKey = 0;
            public array $plain = [];
            public array<mixed> $anything = [];
            public array<int|string, ?Post> $sparse = [];
            public function __construct(
                public array<int> $ids = [],
                public readonly ?Table<Post> $table = null,
                array<string> $notPromoted = [],
            ) {}
        }
        """;

    [Fact]
    public void Emit_GenericAndTypedArrayProperties_AreStamped()
    {
        var php = this.CompileAndEmit(Sample);

        php.Should().Contain("#[\\Tyhp\\PropertyType('array<App\\\\Post>')]\n    public array $posts = [];");
        php.Should().Contain("#[\\Tyhp\\PropertyType('array<string, App\\\\Post>')]\n    public array $byKey = [];");
        php.Should().Contain("#[\\Tyhp\\PropertyType('?App\\\\Table<App\\\\Blog>')]\n    public ?\\App\\Table $blogs = null;");
        php.Should().Contain("#[\\Tyhp\\PropertyType('array<int|string, ?App\\\\Post>')]\n    public array $sparse = [];");
    }

    [Fact]
    public void Emit_StructAndSourceAliasProperties_AreStamped()
    {
        var php = this.CompileAndEmit(Sample);

        php.Should().Contain("#[\\Tyhp\\PropertyType('App\\\\UserId')]\n    public int $owner = 0;");
        php.Should().Contain("#[\\Tyhp\\PropertyType('struct App\\\\Address')]\n    public array $address;");
        php.Should().Contain("#[\\Tyhp\\PropertyType('array<struct App\\\\Address>')]\n    public array $addresses = [];");
    }

    [Fact]
    public void Emit_ClassLevelAliasProperty_UsesTheRegisteredName()
    {
        var php = this.CompileAndEmit("""
            <?tyhp
            namespace App;
            class User {
                public type NameType = string;
                public NameType $name = '';
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::register('App\\\\User\\\\NameType'");
        php.Should().Contain("#[\\Tyhp\\PropertyType('App\\\\User\\\\NameType')]\n    public string $name = '';");
    }

    [Fact]
    public void Emit_PromotedParameters_AreStampedInline()
    {
        var php = this.CompileAndEmit(Sample);

        php.Should().Contain("#[\\Tyhp\\PropertyType('array<int>')] public array $ids = []");
        php.Should().Contain(
            "#[\\Tyhp\\PropertyType('?App\\\\Table<App\\\\Post>')] public readonly ?\\App\\Table $table = null");
        php.Should().Contain(" array $notPromoted = []");
        php.Should().NotContain("PropertyType('array<string>')");
    }

    [Fact]
    public void Emit_LosslessAndTypeParameterProperties_AreNotStamped()
    {
        var php = this.CompileAndEmit(Sample);

        php.Should().Contain("    public ?\\App\\Post $featured = null;");
        php.Should().NotContain("PropertyType('?App\\\\Post')");
        php.Should().NotContain("PropertyType('int|string')");
        php.Should().NotContain("PropertyType('array')");
        php.Should().NotContain("PropertyType('array<mixed>')");
        php.Should().NotContain("PropertyType('array<T>')");
        php.Should().NotContain("PropertyType('T|null')");
        php.Should().NotContain("PropertyType('string')");
    }

    [Fact]
    public void Emit_AuthoredPropertyTypeAttribute_IsReplacedByCompilerStamp()
    {
        var php = this.CompileAndEmit("""
            <?tyhp
            namespace App;
            class Post {}
            class Holder {
                #[\Tyhp\PropertyType('wrong')]
                public array<Post> $posts = [];
                #[\Tyhp\PropertyType('wrong')]
                public string $name = '';
            }
            """);

        php.Should().NotContain("'wrong'");
        php.Should().Contain("#[\\Tyhp\\PropertyType('array<App\\\\Post>')]\n    public array $posts = [];");
        php.Should().Contain("    public string $name = '';");
    }

    private string CompileAndEmit(string tyhp)
    {
        var php = CompileAndEmitCore(tyhp);
        this._output.WriteLine(php);
        return php;
    }

    private static string CompileAndEmitCore(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "stamp.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject();
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                inferredClosureSignatures: result.InferredClosureSignatures,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.4",
            })
            .Build();
        return new Project(configuration);
    }
}
