using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Pass 2 must bind attribute class names on method/function parameters so
/// <c>AttributeRule</c> does not report TYHP4126 for a valid user-defined attribute class.
/// </summary>
[Trait("Category", "Binder")]
public class ParameterAttributeBinderTests
{
    [Fact]
    public void Bind_UserAttributeOnMethodParameter_BindsClassAndDoesNotReport4126()
    {
        var (ast, diagnostics) = BindAndCheck("""
            <?tyhp
            namespace Demo;
            #[\Attribute(\Attribute::TARGET_PARAMETER | \Attribute::TARGET_METHOD)]
            final class Mark {}
            class C {
                #[\Demo\Mark]
                public function m(#[\Demo\Mark] string $p): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var method = FindAllAst<PhpMethodDeclAst>(ast).Single(m => m.Identifier == "m");
        AssertParameterAttributeBound(method.Parameters!.GetAllNotNull().Single(), "Mark");
        AssertAttributeBound(method.AstAttributes.OfType<PhpAttributeAst>().Single(), "Mark");
    }

    [Fact]
    public void Bind_UserAttributeOnFunctionParameter_BindsClassAndDoesNotReport4126()
    {
        var (ast, diagnostics) = BindAndCheck("""
            <?tyhp
            namespace Demo;
            #[\Attribute(\Attribute::TARGET_PARAMETER | \Attribute::TARGET_FUNCTION)]
            final class Mark {}
            function f(#[\Demo\Mark] string $p): void {}
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var function = FindAllAst<PhpFunctionDeclAst>(ast).Single(f => f.Identifier == "f");
        AssertParameterAttributeBound(function.Parameters!.GetAllNotNull().Single(), "Mark");
    }

    [Fact]
    public void Bind_UserAttributeOnPromotedConstructorParameter_BindsClassAndDoesNotReport4126()
    {
        var (ast, diagnostics) = BindAndCheck("""
            <?tyhp
            namespace Demo;
            #[\Attribute(\Attribute::TARGET_PARAMETER | \Attribute::TARGET_PROPERTY)]
            final class Mark {}
            class C {
                public function __construct(#[\Demo\Mark] public string $p) {}
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var ctor = FindAllAst<PhpMethodDeclAst>(ast).Single(m => m.Identifier == "__construct");
        AssertParameterAttributeBound(ctor.Parameters!.GetAllNotNull().Single(), "Mark");
    }

    [Fact]
    public void Bind_UserAttributeOnClosureParameter_BindsAttributeClass()
    {
        var (ast, diagnostics) = BindAndCheck("""
            <?tyhp
            namespace Demo;
            #[\Attribute(\Attribute::TARGET_PARAMETER)]
            final class Mark {}
            function f(): void {
                $fn = function (#[\Demo\Mark] string $p): void {};
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var closure = FindAllAst<PhpInlineFunctionAst>(ast).Single();
        AssertParameterAttributeBound(closure.Parameters!.GetAllNotNull().Single(), "Mark");
    }

    [Fact]
    public void Bind_UserAttributeOnPropertyHookParameter_BindsClassAndDoesNotReport4126()
    {
        var (ast, diagnostics) = BindAndCheck("""
            <?tyhp
            namespace Demo;
            #[\Attribute(\Attribute::TARGET_PARAMETER)]
            final class Mark {}
            class C {
                private string $_name = '';
                public string $name {
                    get => $this->_name;
                    set(#[\Demo\Mark] string $value) {
                        $this->_name = $value;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var setHook = FindAllAst<PhpPropertyHookAst>(ast).Single(h => h.Identifier == "set");
        AssertParameterAttributeBound(setHook.Parameters!.GetAllNotNull().Single(), "Mark");
    }

    [Fact]
    public void Bind_UserAttributeOnOperatorOverloadParameter_BindsClassAndDoesNotReport4126()
    {
        var (ast, diagnostics) = BindAndCheck("""
            <?tyhp
            namespace Demo;
            #[\Attribute(\Attribute::TARGET_PARAMETER)]
            final class Mark {}
            class Box {
                operator +(#[\Demo\Mark] self $left, self $right): Box {
                    return $left;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindAllAst<TyhpOperatorOverloadAst>(ast).Single();
        AssertParameterAttributeBound(op.LeftParameter!, "Mark");
        op.RightParameter.Should().NotBeNull();
        op.RightParameter!.AstAttributes.Should().BeEmpty();
    }

    [Fact]
    public void Bind_UserAttributeOnExtensionOperatorOverloadParameter_BindsClassAndDoesNotReport4126()
    {
        var (ast, diagnostics) = BindAndCheck("""
            <?tyhp
            namespace Demo;
            #[\Attribute(\Attribute::TARGET_PARAMETER)]
            final class Mark {}
            class Money { public int $amount = 0; }
            extension MoneyOperators extends Money {
                operator + (#[\Demo\Mark] self $left, self $right): Money {
                    return $left;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var op = FindAllAst<TyhpOperatorOverloadAst>(ast).Single();
        AssertParameterAttributeBound(op.LeftParameter!, "Mark");
    }

    [Fact]
    public void Bind_RepeatedAttributeOnParameter_BindsBothAndDoesNotReportNotRepeatable()
    {
        var (ast, diagnostics) = BindAndCheck("""
            <?tyhp
            namespace Demo;
            #[\Attribute(\Attribute::TARGET_PARAMETER | \Attribute::IS_REPEATABLE)]
            final class Mark {}
            class C {
                public function m(#[\Demo\Mark] #[\Demo\Mark] string $p): void {}
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass
                || d.Code == MessageCode.CheckerAttributeNotRepeatable,
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var method = FindAllAst<PhpMethodDeclAst>(ast).Single(m => m.Identifier == "m");
        var param = method.Parameters!.GetAllNotNull().Single();
        param.AstAttributes.Should().HaveCount(2);
        foreach (var attribute in param.AstAttributes.OfType<PhpAttributeAst>())
        {
            AssertAttributeBound(attribute, "Mark");
        }
    }

    [Fact]
    public void Check_NonAttributeClassOnOperatorOverloadParameter_StillReports4126()
    {
        var (_, diagnostics) = BindAndCheck("""
            <?tyhp
            class Plain {}
            class Box {
                operator +(#[Plain] self $left, self $right): Box {
                    return $left;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerNotAnAttributeClass).Should().Be(1);
    }

    [Fact]
    public void Check_NonAttributeClassOnParameter_StillReports4126()
    {
        var (_, diagnostics) = BindAndCheck("""
            <?tyhp
            class Plain {}
            class C {
                public function m(#[Plain] string $p): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNotAnAttributeClass);
        diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerNotAnAttributeClass).Should().Be(1);
    }

    private static void AssertParameterAttributeBound(PhpParameterAst parameter, string className)
    {
        parameter.AstAttributes.Should().HaveCount(1);
        AssertAttributeBound(parameter.AstAttributes.OfType<PhpAttributeAst>().Single(), className);
    }

    private static void AssertAttributeBound(PhpAttributeAst attribute, string className)
    {
        attribute.Name.Should().BeOfType<PhpNameAst>();
        var name = (PhpNameAst)attribute.Name!;
        name.BoundSymbol.Should().BeOfType<ObjectDeclarationSymbol>(
            "Pass 2 must bind the attribute class so AttributeRule does not treat it as unbound");
        ((ObjectDeclarationSymbol)name.BoundSymbol!).Name.Should().Be(className);
    }

    private static (SrcFileAst Ast, DiagnosticBag Diagnostics) BindAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var ast = result.ParsedFiles![0];
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return (ast, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static IEnumerable<T> FindAllAst<T>(IBase2Ast root) where T : class, IBase2Ast
    {
        if (root is T match)
        {
            yield return match;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var found in FindAllAst<T>(child))
            {
                yield return found;
            }
        }
    }
}
