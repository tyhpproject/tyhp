using Tyhp.Domain.Diagnostics;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Unqualified free-function names at file/namespace-block statement lists must resolve
/// through the current namespace (PHP current-namespace lookup), not only from method bodies.
/// </summary>
[Trait("Category", "Binder")]
public class NameResolverCallSiteScopeTests
{
    [Fact]
    public void FindCallSiteScope_StatementNamespace_ReturnsThatFilesNamespaceBlock()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            namespace App;

            function twice(int $n): int { return $n * 2; }

            int $a = twice(2);
            """, skipChecking: true);

        result.GlobalScope.Should().NotBeNull();
        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

        var resolver = new NameResolver(result.GlobalScope!, new DiagnosticBag());
        var callSite = resolver.FindCallSiteScope(
            result.ParsedFiles![0],
            lexicalScope: null,
            currentNamespaceName: "App");

        callSite.Should().BeOfType<NamespaceBlockScope>();
        var block = (NamespaceBlockScope)callSite;
        var blockSymbol = block.DeclarationSymbol;
        blockSymbol.Should().NotBeNull();
        blockSymbol!.Name.Should().Be("App");
        blockSymbol.OwningFileScope.Should().NotBeNull();
    }

    [Fact]
    public void ResolveRelativeName_FromNamespaceBlock_FindsSameNamespaceFunction()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;

            function twice(int $n): int { return $n * 2; }
            """);

        diagnostics.HasErrors.Should().BeFalse();
        global.Should().NotBeNull();

        var block = FindNamespaceBlock(global!, "App");
        block.Should().NotBeNull();

        var resolver = new NameResolver(global!, new DiagnosticBag());
        var resolved = resolver.ResolveRelativeName(["twice"], block!);

        resolved.Should().BeOfType<FunctionDeclarationSymbol>();
        resolved!.Name.Should().Be("twice");
    }

    [Fact]
    public void ResolveRelativeName_FromGlobalScope_DoesNotFindNamespacedFunction()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;

            function twice(int $n): int { return $n * 2; }
            """);

        diagnostics.HasErrors.Should().BeFalse();
        global.Should().NotBeNull();

        var resolver = new NameResolver(global!, new DiagnosticBag());
        var resolved = resolver.ResolveRelativeName(["twice"], global!);

        resolved.Should().BeNull(
            "unqualified lookup from global must not pick App\\twice");
    }

    [Fact]
    public void ResolveRelativeName_FromOtherNamespaceBlock_DoesNotFindAppFunction()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App {
                function twice(int $n): int { return $n * 2; }
            }
            namespace Other {
            }
            """);

        diagnostics.HasErrors.Should().BeFalse();
        global.Should().NotBeNull();

        var other = FindNamespaceBlock(global!, "Other");
        other.Should().NotBeNull();

        var resolver = new NameResolver(global!, new DiagnosticBag());
        var resolved = resolver.ResolveRelativeName(["twice"], other!);

        resolved.Should().BeNull(
            "Other\\twice does not exist; App\\twice must not leak");
    }

    [Fact]
    public void ResolveRelativeName_FromSiblingFileNamespaceBlock_FindsSameNamespaceFunction()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var onePath = Path.Combine(tempDir, "one.tyhp");
        var twoPath = Path.Combine(tempDir, "two.tyhp");
        File.WriteAllText(onePath, """
            <?tyhp
            namespace App;

            function twice(int $n): int { return $n * 2; }
            """);
        File.WriteAllText(twoPath, """
            <?tyhp
            namespace App;

            int $a = 1;
            """);

        try
        {
            var (global, diagnostics) = BinderTestHelper.BindFiles(onePath, twoPath);
            diagnostics.HasErrors.Should().BeFalse();
            global.Should().NotBeNull();

            var ns = global!.FindNamespaceScope("App");
            ns.Should().NotBeNull();
            var twoBlock = ns!.ChildScopes
                .OfType<NamespaceBlockScope>()
                .FirstOrDefault(b =>
                    b.DeclarationSymbol?.OwningFileScope?.FileName?.EndsWith("two.tyhp", StringComparison.Ordinal) == true
                    || b.DeclarationSymbol?.OwningFileScope?.SourceFile?.EndsWith("two.tyhp", StringComparison.Ordinal) == true);
            twoBlock.Should().NotBeNull("file two contributes a namespace block under App");

            var resolver = new NameResolver(global, new DiagnosticBag());
            var resolved = resolver.ResolveRelativeName(["twice"], twoBlock!);

            resolved.Should().BeOfType<FunctionDeclarationSymbol>();
            resolved!.Name.Should().Be("twice");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static NamespaceBlockScope? FindNamespaceBlock(GlobalScope global, string namespaceName)
    {
        var ns = global.FindNamespaceScope(namespaceName);
        return ns?.ChildScopes.OfType<NamespaceBlockScope>().FirstOrDefault();
    }
}
