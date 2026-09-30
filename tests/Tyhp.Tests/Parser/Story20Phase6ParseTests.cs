using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;

namespace Tyhp.Tests.Parser;

[Trait("Category", "Parser")]
[Trait("Category", "Story20")]
public class Story20Phase6ParseTests
{
    [Fact]
    public void GlobalUse_ClassFunctionConstAndExtension_Parse()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            global use App\Models\User;
            global use function App\Helpers\formatDate;
            global use const App\Config\MAX_RETRIES;
            global use extension \Tyhp\StringExtensions;
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
        var nodes = Flatten(result.Ast);
        nodes.OfType<PhpImportDeclListAst>().Should().Contain(s => s.IsGlobal);
        nodes.OfType<TyhpImportExtensionAst>().Should().Contain(s => s.IsGlobal);
    }

    [Fact]
    public void UseExtension_HideAndInsteadof_Parse()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            use extension Foo {
                length hide;
                operator * hide;
                operator *<string> hide;
                Foo::bar insteadof Baz;
                Foo::operator *<array> insteadof Baz;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
        var import = Flatten(result.Ast).OfType<TyhpImportExtensionAst>().Should().ContainSingle().Subject;
        var adaptations = import.Adaptations!.GetAllNotNull().ToList();
        adaptations.OfType<PhpTraitAliasAst>().Should().Contain(a => a.IsHide);
        adaptations.OfType<PhpTraitAliasAst>().Should().Contain(a =>
            a.IsHide
            && a.MethodReference != null
            && a.MethodReference.IsOperator
            && a.MethodReference.OperatorToken == "*");
        adaptations.OfType<PhpTraitAliasAst>().Should().Contain(a =>
            a.IsHide
            && a.MethodReference != null
            && a.MethodReference.IsOperator
            && a.MethodReference.OperatorTarget != null);
        adaptations.OfType<PhpTraitPrecedenceAst>().Should().NotBeEmpty();
        adaptations.OfType<PhpTraitPrecedenceAst>().Should().Contain(p =>
            p.MethodReference != null
            && p.MethodReference.IsOperator
            && p.MethodReference.OperatorToken == "*"
            && p.MethodReference.OperatorTarget != null);
    }

    [Fact]
    public void Tyhpdef_UseExtension_HideAndInsteadof_Parse()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            use extension Foo {
                length hide;
                operator * hide;
                operator *<string> hide;
                Foo::bar insteadof Baz;
                Foo::operator *<array> insteadof Baz;
            }
            class User {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
        var import = Flatten(result.Ast).OfType<TyhpImportExtensionAst>().Should().ContainSingle().Subject;
        import.Adaptations!.GetAllNotNull().OfType<PhpTraitAliasAst>().Should().Contain(a => a.IsHide);
    }

    [Fact]
    public void Tyhpdef_ClassBodyExtensionOperator_AllowsInlineAttribute()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            namespace Lib;
            class Money {
                public static function __add(self $a, int $b): self;
                #[\Tyhp\Optimize\Inline]
                extension operator +(self $a, int $b): self => self::__add($a, $b);
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
        result.Ast.Should().NotBeNull();
    }

    [Fact]
    public void Tyhpdef_GlobalUse_Parses()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            global use App\Models\User;
            global use extension \Tyhp\StringExtensions;
            namespace App\Models;
            class User {}
            """);

        result.Diagnostics.Errors.Should().BeEmpty();
        Flatten(result.Ast).OfType<PhpImportDeclListAst>().Should().Contain(s => s.IsGlobal);
        Flatten(result.Ast).OfType<TyhpImportExtensionAst>().Should().Contain(s => s.IsGlobal);
    }

    private static List<IBase2Ast> Flatten(IBase2Ast? root)
    {
        var result = new List<IBase2Ast>();
        if (root is null)
        {
            return result;
        }

        var stack = new Stack<IBase2Ast>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            result.Add(node);
            foreach (var child in node.AstChildren)
            {
                if (child is not null)
                {
                    stack.Push(child);
                }
            }
        }

        return result;
    }
}
