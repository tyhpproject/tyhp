using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Binder;

/// <summary>
/// <c>ResolveInheritedMember</c> must terminate when heritage type resolution re-enters
/// the same lookup (class/namespace FQN collisions, true inheritance cycles).
/// </summary>
[Trait("Category", "Binder")]
public class InheritedMemberCycleTests
{
    [Fact]
    public void ResolveMember_ClassAndNestedNamespaceSharePrefix_ResolvesInheritedMethod()
    {
        // PHP allows class \App\Renderer and namespace \App\Renderer together.
        // \App\Renderer\AbstractRenderer was tried as alias AbstractRenderer on class
        // Renderer, which walked extends, which resolved that type again — unbounded
        // recursion (exit 134) before the in-flight guard.
        var (global, diagnostics, renderer) = BindAndFindClass("""
            <?tyhp

            namespace App {
                class Renderer extends \App\Renderer\AbstractRenderer {
                }
            }

            namespace App\Renderer {
                abstract class AbstractRenderer {
                    public function draw(): void {}
                }
            }

            function paint(\App\Renderer $r): void {
                $r->draw();
            }
            """, "Renderer");

        diagnostics.Errors.Should().BeEmpty(
            "class/namespace prefix collision must not overflow or error; got: "
            + Describe(diagnostics));

        var resolver = new NameResolver(global, new DiagnosticBag());
        var member = resolver.ResolveMember("draw", renderer);
        member.Should().BeOfType<ObjectMethodSymbol>();
        member!.Name.Should().Be("draw");
    }

    [Fact]
    public void ResolveConstant_ClassAndNestedNamespaceSharePrefix_ResolvesInheritedConstant()
    {
        // Same collision as the method case above, but exercised through
        // ResolveInheritedConstant's own in-flight guard (a separate lookup-kind bucket)
        // rather than relying only on the instance-member guard hit while resolving `extends`.
        var (global, diagnostics, renderer) = BindAndFindClass("""
            <?tyhp

            namespace App {
                class Renderer extends \App\Renderer\AbstractRenderer {
                }
            }

            namespace App\Renderer {
                abstract class AbstractRenderer {
                    const string TAG = "renderer";
                }
            }

            function paint(\App\Renderer $r): void {
            }
            """, "Renderer");

        diagnostics.Errors.Should().BeEmpty(
            "class/namespace prefix collision must not overflow or error; got: "
            + Describe(diagnostics));

        var resolver = new NameResolver(global, new DiagnosticBag());
        var constant = resolver.ResolveConstant("TAG", renderer);
        constant.Should().NotBeNull("TAG is declared on the resolved parent AbstractRenderer");
    }

    [Fact]
    public void ResolveMember_CyclicExtends_TerminatesWithoutOverflow()
    {
        var (global, diagnostics, classA) = BindAndFindClass("""
            <?tyhp

            class A extends B {}
            class B extends A {}

            function poke(A $a): void {
                $a->missing();
            }
            """, "A");

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderCircularInheritance);

        var resolver = new NameResolver(global, new DiagnosticBag());
        var resolved = resolver.ResolveMember("missing", classA);
        resolved.Should().BeNull("cyclic heritage must not hang; missing stays unresolved");
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics, ObjectDeclarationSymbol Class) BindAndFindClass(
        string content,
        string className)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);

            var found = FindObject(result.GlobalScope!, className);
            found.Should().NotBeNull($"class {className} should be bound");
            return (result.GlobalScope!, result.Diagnostics, found!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        var resolver = new NameResolver(global, new DiagnosticBag());
        if (resolver.ResolveQualifiedName([name]) is ObjectDeclarationSymbol unqualified)
        {
            return unqualified;
        }

        return resolver.ResolveQualifiedName(["App", name]) as ObjectDeclarationSymbol;
    }

    private static string Describe(DiagnosticBag diagnostics) =>
        string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));
}
