using System;
using System.IO;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Story 14.5 Phase 4 items 1–2 — keyword call forms attach ExtCore tyhpdef
/// <see cref="FunctionDeclarationSymbol"/>s; bare exit / unary clone stay unbound.
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class ExitDieCloneKeywordCallBindTests
{
    [Fact]
    public void Bind_ExitAndDieCallForms_AttachFunctionSymbols()
    {
        var (ast, diagnostics) = Bind("""
            <?tyhp
            function demo(): void {
                exit();
                exit(0);
                exit(status: 1);
                exit(...);
                die();
                die("bye");
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var exits = FindUnaryOps(ast!, "exit").ToList();
        var dies = FindUnaryOps(ast!, "die").ToList();
        exits.Should().HaveCount(4);
        dies.Should().HaveCount(2);

        foreach (var node in exits.Concat(dies))
        {
            node.Operand.Should().BeOfType<PhpArgumentListAst>();
            node.BoundSymbol.Should().BeOfType<FunctionDeclarationSymbol>();
            ((FunctionDeclarationSymbol)node.BoundSymbol!).Name.Should().Be(
                OperatorText(node));
        }
    }

    [Fact]
    public void Bind_CloneCallForms_AttachFunctionSymbol()
    {
        var (ast, diagnostics) = Bind("""
            <?tyhp
            class C {}
            function demo(C $obj): void {
                $a = clone();
                $b = clone($obj,);
                $c = clone($obj, []);
                $d = clone(object: $obj);
                $e = clone(...);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var clones = FindUnaryOps(ast!, "clone")
            .Where(c => c.Operand is PhpArgumentListAst)
            .ToList();
        clones.Should().HaveCount(5);
        foreach (var clone in clones)
        {
            clone.BoundSymbol.Should().BeOfType<FunctionDeclarationSymbol>();
            ((FunctionDeclarationSymbol)clone.BoundSymbol!).Name.Should().Be("clone");
        }
    }

    [Fact]
    public void Bind_BareExitAndUnaryClone_DoNotAttachFunctionSymbol()
    {
        var (ast, diagnostics) = Bind("""
            <?tyhp
            class C {}
            function demo(C $obj): void {
                exit;
                die;
                $a = clone $obj;
                $b = clone($obj);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        foreach (var node in FindUnaryOps(ast!, "exit").Concat(FindUnaryOps(ast!, "die")))
        {
            node.Operand.Should().BeNull();
            node.BoundSymbol.Should().BeNull(
                "bare exit/die must not bind to the tyhpdef call signature");
        }

        var clones = FindUnaryOps(ast!, "clone").ToList();
        clones.Should().HaveCount(2);
        foreach (var clone in clones)
        {
            clone.Operand.Should().NotBeOfType<PhpArgumentListAst>();
            clone.BoundSymbol.Should().BeNull(
                "unary clone (including parenthesized clone($x)) must stay unbound");
        }
    }

    private static IEnumerable<PhpUnaryOpAst> FindUnaryOps(IBase2Ast root, string op)
    {
        if (root is PhpUnaryOpAst unary
            && string.Equals(OperatorText(unary), op, StringComparison.OrdinalIgnoreCase))
        {
            yield return unary;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var match in FindUnaryOps(child, op))
            {
                yield return match;
            }
        }
    }

    private static string OperatorText(PhpUnaryOpAst unary) =>
        unary.Operator?.ValueString ?? unary.Operator?.Identifier ?? "";

    private static (SrcFileAst? Ast, DiagnosticBag Diagnostics) Bind(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };
            var result = compilationService.ParseFiles([filePath], options);
            return (result.ParsedFiles?.FirstOrDefault(), result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
