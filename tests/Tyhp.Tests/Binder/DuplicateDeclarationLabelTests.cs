using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Diagnostics")]
public class DuplicateDeclarationLabelTests
{
    private static string DeclaredHere => Message.Localize("CLI_DiagnosticLabelDeclaredHere");

    [Fact]
    public void AddDuplicateFromAst_WithoutExisting_OmitsLabel()
    {
        var parse = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            class User {}
            """);
        parse.Success.Should().BeTrue();
        var classAst = FindFirst<PhpObjectTypeDeclAst>(parse.Ast!);
        classAst.Should().NotBeNull();

        var bag = new DiagnosticBag();
        bag.AddDuplicateFromAst(
            MessageCode.BinderDuplicateSymbolDeclaration,
            classAst!,
            "a.tyhp",
            existingSymbol: null,
            "User");

        var error = bag.Errors.Should().ContainSingle().Subject;
        error.Code.Should().Be(MessageCode.BinderDuplicateSymbolDeclaration);
        error.FileName.Should().Be("a.tyhp");
        error.Labels.Should().BeEmpty();
    }

    [Fact]
    public void Bind_DuplicateClassInSameFile_LabelsFirstDeclaration()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class User {}
            class User {}
            """);

        var error = diagnostics.Errors
            .Should()
            .ContainSingle(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration)
            .Subject;
        error.Line.Should().BeGreaterThan(1);
        error.Labels.Should().ContainSingle();
        error.Labels[0].Message.Should().Be(DeclaredHere);
        error.Labels[0].Span.FileName.Should().Be(error.FileName);
        error.Labels[0].Span.Line.Should().BeLessThan(error.Line);
        error.Message.Should().NotContain(":");
    }

    [Fact]
    public void Bind_DuplicateGlobalFunctionAcrossFiles_LabelUsesFirstFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var firstPath = Path.Combine(tempDir, "first.tyhp");
        var secondPath = Path.Combine(tempDir, "second.tyhp");
        File.WriteAllText(firstPath, """
            <?tyhp
            function demo(): void {}
            """);
        File.WriteAllText(secondPath, """
            <?tyhp
            function demo(): void {}
            """);

        try
        {
            var (_, diagnostics) = BinderTestHelper.BindFiles(firstPath, secondPath);
            var error = diagnostics.Errors
                .Should()
                .ContainSingle(d => d.Code == MessageCode.BinderDuplicateSymbolDeclaration)
                .Subject;
            Path.GetFileName(error.FileName).Should().Be("second.tyhp");
            error.Labels.Should().ContainSingle();
            error.Labels[0].Message.Should().Be(DeclaredHere);
            Path.GetFileName(error.Labels[0].Span.FileName).Should().Be("first.tyhp");
            error.Labels[0].Span.Line.Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Bind_DuplicateUseAlias_LabelsFirstUse()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            namespace App;
            use Foo\Bar as Alias;
            use Baz\Qux as Alias;
            """);

        var error = diagnostics.Errors
            .Should()
            .ContainSingle(d => d.Code == MessageCode.BinderDuplicateUseAlias)
            .Subject;
        error.Labels.Should().ContainSingle();
        error.Labels[0].Message.Should().Be(DeclaredHere);
        error.Labels[0].Span.FileName.Should().Be(error.FileName);
        error.Labels[0].Span.Line.Should().BeLessThan(error.Line);
        Convert.ToString(error.FormatParams[0]).Should().Be("Alias");
    }

    [Fact]
    public void Bind_DuplicateGenericParameter_LabelsFirstParameter()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            class Box<T, T> {}
            """);

        var error = diagnostics.Errors
            .Should()
            .ContainSingle(d => d.Code == MessageCode.BinderDuplicateGenericParameter)
            .Subject;
        error.Labels.Should().ContainSingle();
        error.Labels[0].Message.Should().Be(DeclaredHere);
        error.Labels[0].Span.FileName.Should().Be(error.FileName);
        error.Labels[0].Span.Line.Should().Be(error.Line);
        error.Labels[0].Span.Column.Should().BeLessThan(error.Column);
        Convert.ToString(error.FormatParams[0]).Should().Be("T");
    }

    private static T? FindFirst<T>(IBase2Ast node) where T : class, IBase2Ast
    {
        if (node is T match)
        {
            return match;
        }

        foreach (var child in node.AstChildren)
        {
            if (child == null)
            {
                continue;
            }

            var found = FindFirst<T>(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
