using System;
using System.IO;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Story 14.5 Phase 3 — ExtCore tyhpdef stubs for <c>exit</c> / <c>die</c> / <c>clone</c>
/// must load as global functions with the expected signatures.
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
public class ExitDieCloneTyhpdefStubTests
{
    [Fact]
    public void Bind_ExitStub_RegistersNeverReturningGlobalFunction()
    {
        var (globalScope, diagnostics) = BindOnly("""
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var exit = RequireFunction(globalScope!, "exit");
        exit.Parameters.Should().HaveCount(1);
        exit.Parameters[0].Name.Should().Be("$status");
        exit.Parameters[0].DefaultValue.Should().NotBeNull();
        TypeText(exit.Parameters[0].DeclaredType).Should().Contain("string").And.Contain("int");
        TypeText(exit.ReturnType).Should().Be("never");
    }

    [Fact]
    public void Bind_DieStub_RegistersNeverReturningGlobalFunction()
    {
        var (globalScope, diagnostics) = BindOnly("""
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var die = RequireFunction(globalScope!, "die");
        die.Parameters.Should().HaveCount(1);
        die.Parameters[0].Name.Should().Be("$status");
        die.Parameters[0].DefaultValue.Should().NotBeNull();
        TypeText(die.Parameters[0].DeclaredType).Should().Contain("string").And.Contain("int");
        TypeText(die.ReturnType).Should().Be("never");
    }

    [Fact]
    public void Bind_CloneStub_RegistersObjectReturningGlobalFunction()
    {
        var (globalScope, diagnostics) = BindOnly("""
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var clone = RequireFunction(globalScope!, "clone");
        clone.Parameters.Should().HaveCount(2);
        clone.Parameters[0].Name.Should().Be("$object");
        clone.Parameters[0].DefaultValue.Should().BeNull();
        TypeText(clone.Parameters[0].DeclaredType).Should().Be("object");
        clone.Parameters[1].Name.Should().Be("$withProperties");
        clone.Parameters[1].DefaultValue.Should().NotBeNull();
        TypeText(clone.Parameters[1].DeclaredType).Should().Be("array");
        TypeText(clone.ReturnType).Should().Be("object");
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("die")]
    [InlineData("clone")]
    public void Parse_UserlandRedeclarationOfReservedConstruct_Fails(string name)
    {
        var result = ParserTestHelper.ParseTyhpContent(
            "<?tyhp\nfunction " + name + "(): void {}\n");

        result.Diagnostics.HasErrors.Should().BeTrue(
            $"userland `function {name}` must not parse (reserved keyword; functionName is T_STRING only)");
    }

    private static FunctionDeclarationSymbol RequireFunction(GlobalScope globalScope, string name)
    {
        var resolver = new NameResolver(new SymbolTree(globalScope), new DiagnosticBag());
        var resolved = resolver.ResolveRelativeName([name], globalScope);
        resolved.Should().BeOfType<FunctionDeclarationSymbol>(
            $"expected ExtCore stub `{name}` to resolve as a global function");
        return (FunctionDeclarationSymbol)resolved!;
    }

    private static string TypeText(ITypeExpression? type)
    {
        type.Should().NotBeNull();
        return type switch
        {
            PhpBuiltinTypeAst builtin => builtin.Identifier ?? "",
            PhpNamedTypeAst named => named.Name?.ValueString ?? named.Name?.Identifier ?? "",
            PhpNameAst name => name.ValueString ?? name.Identifier ?? "",
            PhpTypeExpressionAst composite => string.Join(
                "|",
                composite.Types?.GetAllNotNull().OfType<ITypeExpression>().Select(TypeText)
                    ?? Enumerable.Empty<string>()),
            _ => type!.GetType().Name,
        };
    }

    private static (GlobalScope? Scope, DiagnosticBag Diagnostics) BindOnly(string content)
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
            return (result.GlobalScope, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
