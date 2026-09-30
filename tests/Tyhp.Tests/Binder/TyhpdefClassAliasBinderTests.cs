using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Tyhpdef <c>class X as Y</c> must register the object under the Tyhp-facing alias <c>Y</c>
/// so a same-namespace <c>extension X</c> does not collide (TYHP8002).
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class TyhpdefClassAliasBinderTests
{
    private const string BackerTyhpdef = """
        <?tyhpdef

        namespace Tyhp;

        class Foo as Foo__tyhpExtensionBacker {
            public static function greet(string $this_): string;
        }

        extension Foo extends string {
            fn greet(): string
                => Foo__tyhpExtensionBacker::greet($this);
        }

        global use extension \Tyhp\Foo;
        """;

    [Fact]
    public void Bind_ClassAliasAndExtensionSameShortName_NoDuplicate8002()
    {
        var (global, diagnostics) = BindFixture(BackerTyhpdef, """
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var backer = FindObject(global, "Foo__tyhpExtensionBacker");
        backer.Should().NotBeNull();
        backer!.IsExtension.Should().BeFalse();
        backer.OriginalPhpName.Should().Be("Foo");
        backer.Members.Should().ContainKey("greet");

        var extension = FindObject(global, "Foo");
        extension.Should().NotBeNull();
        extension!.IsExtension.Should().BeTrue();
        extension.Members.Should().ContainKey("greet");

        FindNonExtensionObject(global, "Foo").Should().BeNull(
            "the PHP class name must not occupy the same symbol slot as extension Foo");
    }

    [Fact]
    public void Bind_ClassAlias_RegistersOnlyTyhpFacingName()
    {
        var (global, diagnostics) = BindFixture("""
            <?tyhpdef
            class Guest as Ghost {
                public function haunt(): void;
            }
            """, """
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var ghost = FindObject(global, "Ghost");
        ghost.Should().NotBeNull();
        ghost!.IsExtension.Should().BeFalse();
        ghost.OriginalPhpName.Should().Be("Guest");
        ghost.Members.Should().ContainKey("haunt");

        FindObject(global, "Guest").Should().BeNull();
    }

    [Fact]
    public void Check_ExtensionMethodsOnAliasedBacker_Resolve()
    {
        var diagnostics = CompileAndCheck(BackerTyhpdef, """
            <?tyhp
            function demo(string $s): string {
                return $s->greet();
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefDuplicateDeclaration);
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Emit_BackerAlias_ErasesToPhpClassName()
    {
        var php = CompileAndEmit(BackerTyhpdef, """
            <?tyhp
            function demo(string $s): string {
                return $s->greet();
            }
            """);

        php.Should().Contain("Foo::greet");
        php.Should().NotContain("Foo__tyhpExtensionBacker");
        php.Should().NotContain("$s->greet(");
    }

    [Fact]
    public void Emit_ClassAlias_ErasesToOriginalPhpName()
    {
        var php = CompileAndEmit("""
            <?tyhpdef
            class Guest as Ghost {
            }
            """, """
            <?tyhp
            function make(): Ghost {
                return new Ghost();
            }
            """);

        php.Should().Contain("new Guest");
        php.Should().NotContain("new Ghost");
    }

    [Fact]
    public void Bind_TyhpdefExtensionMethodNamedPartial_IsIdentifier()
    {
        var (global, diagnostics) = BindFixture("""
            <?tyhpdef
            namespace Tyhp;
            extension ClosurePartialProbe extends mixed {
                fn partial(mixed ...$args): mixed => $this;
            }
            """, """
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var ext = FindObject(global, "ClosurePartialProbe");
        ext.Should().NotBeNull();
        ext!.IsExtension.Should().BeTrue();
        ext.Members.Should().ContainKey("partial");
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) BindFixture(
        string tyhpdef,
        string tyhp)
    {
        var result = Compile(tyhpdef, tyhp, skipChecking: true);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static DiagnosticBag CompileAndCheck(string tyhpdef, string tyhp)
    {
        var result = Compile(tyhpdef, tyhp, skipChecking: true);
        result.GlobalScope.Should().NotBeNull("bind should succeed");
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var symbolTree = new SymbolTree(result.GlobalScope!);
        var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
        checker.Check(result.ParsedFiles!);
        return result.Diagnostics;
    }

    private static string CompileAndEmit(string tyhpdef, string tyhp)
    {
        var result = Compile(tyhpdef, tyhp, skipChecking: true);
        result.GlobalScope.Should().NotBeNull();
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.2",
            })
            .Build();
        var project = new Project(configuration);
        var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, project);
        var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
        return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
    }

    private static CompilationResult Compile(string tyhpdef, string tyhp, bool skipChecking)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "backer.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, "demo.tyhp");
        File.WriteAllText(tyhpdefPath, tyhpdef);
        File.WriteAllText(tyhpPath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            return compilationService.ParseFiles([tyhpPath], IsolatedCompilation.CreateOptions(
                tempDir,
                skipChecking: skipChecking,
                tyhpdefIncludePaths: [tyhpdefPath]));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static ObjectDeclarationSymbol? FindNonExtensionObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && !obj.IsExtension
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            visit(symbol);
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            Walk(child, visit);
        }
    }
}
