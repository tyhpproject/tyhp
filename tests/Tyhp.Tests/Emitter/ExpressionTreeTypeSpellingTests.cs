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
/// Expression-tree parameter types keep type arguments and struct types in the
/// <c>\Tyhp\Type::parse()</c> spelling; plain classes stay <c>Fqn::class</c>.
/// </summary>
[Trait("Category", "Emitter")]
public class ExpressionTreeTypeSpellingTests
{
    private readonly ITestOutputHelper _output;

    public ExpressionTreeTypeSpellingTests(ITestOutputHelper output)
    {
        this._output = output;
    }

    private const string Types = """
        <?tyhp
        namespace App;
        type Address = struct { string $city; };
        class Blog { public string $title = ''; }
        class Table<T> { public string $name = ''; }

        """;

    [Fact]
    public void Emit_AuthoredGenericParameter_KeepsTypeArguments()
    {
        var php = this.CompileAndEmit(Types + """
            function take(\Tyhp\Expression<callable(Table<Blog>): bool> $pred): void {}
            function demo(): void {
                take(fn (Table<Blog> $t) => $t->name === 'blogs');
            }
            """);

        php.Should().Contain(@"\Tyhp\Expr::param('t', 'App\Table<App\Blog>')->name->eq('blogs')->backedBy(fn(");
    }

    [Fact]
    public void Emit_InferredGenericParameter_KeepsTypeArguments()
    {
        var php = this.CompileAndEmit(Types + """
            function take(\Tyhp\Expression<callable(Table<Blog>): bool> $pred): void {}
            function demo(): void {
                take(fn ($t) => $t->name === 'blogs');
            }
            """);

        php.Should().Contain(@"\Tyhp\Expr::param('t', 'App\Table<App\Blog>')->name->eq('blogs')->backedBy(fn(");
    }

    [Fact]
    public void Emit_PlainClassParameter_StaysClassConstant()
    {
        var php = this.CompileAndEmit(Types + """
            function take(\Tyhp\Expression<callable(Blog): bool> $pred): void {}
            function demo(): void {
                take(fn (Blog $b) => $b->title === 'x');
                take(fn ($b) => $b->title === 'y');
            }
            """);

        php.Should().Contain(@"\Tyhp\Expr::param('b', \App\Blog::class)->title->eq('x')->backedBy(fn(");
        php.Should().Contain(@"\Tyhp\Expr::param('b', \App\Blog::class)->title->eq('y')->backedBy(fn(");
    }

    [Fact]
    public void Emit_StructParameter_UsesStructSpelling()
    {
        var php = this.CompileAndEmit(Types + """
            function take(\Tyhp\Expression<callable(Address): bool> $pred): void {}
            function demo(): void {
                take(fn (Address $a) => $a->city === 'Oslo');
            }
            """);

        php.Should().Contain(@"\Tyhp\Expr::param('a', 'struct App\Address')");
        php.Should().NotContain(@"\App\Address::class");
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
        var filePath = Path.Combine(tempDir, "tree.tyhp");
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
