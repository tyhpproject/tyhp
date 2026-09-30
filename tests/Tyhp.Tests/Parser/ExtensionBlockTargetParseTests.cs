using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Parser;

/// <summary>
/// Block-target <c>extension</c> syntax: header <c>extends Type</c>, one-level nested
/// groups, implied <c>$this</c>, and the <c>&amp;$this</c> receiver annotation.
/// </summary>
[Trait("Category", "Parser")]
[Trait("Category", "Tyhp")]
public class ExtensionBlockTargetParseTests
{
    [Fact]
    public void Parse_HeaderTarget_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension StringExtensions extends string {
                fn reverse(): string => $this;
                function match(string $pattern): string { return $this; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var decl = SingleExtension(result);
        TypeSpelling(decl.TargetType).Should().Be("string");
        decl.FunctionList!.GetAllNotNull().OfType<PhpFunctionDeclAst>().Should().HaveCount(2);
        decl.FunctionList.GetAllNotNull().OfType<PhpFunctionDeclAst>()
            .Should().Contain(fn => fn.Identifier == "reverse" && fn.IsShortSyntax && fn.Parameters!.GetAllNotNull().Count() == 0);
    }

    [Fact]
    public void Parse_NestedGroups_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension NumericHelpers {
                extends int {
                    fn abs(): int => \abs($this);
                }
                extends float {
                    fn abs(): float => \abs($this);
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var decl = SingleExtension(result);
        decl.TargetType.Should().BeNull();
        var groups = decl.FunctionList!.GetAllNotNull().OfType<TyhpExtensionDeclAst>().ToList();
        groups.Should().HaveCount(2);
        groups.Should().OnlyContain(group => group.IsTargetGroup);
        TypeSpelling(groups[0].TargetType).Should().Be("int");
        TypeSpelling(groups[1].TargetType).Should().Be("float");
        groups[0].FunctionList!.GetAllNotNull().OfType<TyhpExtensionDeclAst>().Should().BeEmpty();
    }

    [Fact]
    public void Parse_NameGenericsAppliedToHeaderTarget_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension MyClassOps<T> extends \App\MyClass<T> {
                function id(): T { return $this->id; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var decl = SingleExtension(result);
        decl.GenericParameters!.GetAllNotNull().Select(p => p.Identifier).Should().Equal("T");
        TypeSpelling(decl.TargetType).Should().Be("\\App\\MyClass");
    }

    [Fact]
    public void Parse_GenericConstraintExtendsIsNotTheTarget_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension Ops<T extends int|string> extends \App\MyClass<T> {
                function id(): T { return $this->id; }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var decl = SingleExtension(result);
        var parameter = decl.GenericParameters!.GetAllNotNull().Should().ContainSingle().Subject;
        parameter.Identifier.Should().Be("T");
        parameter.TypeConstraint.Should().NotBeNull();
        TypeSpelling(decl.TargetType).Should().Be("\\App\\MyClass");
    }

    [Fact]
    public void Parse_GroupOpenTypeParameter_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension Helpers {
                extends<T> \App\MyClass<T> {
                    function id(): T { return $this->id; }
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var group = SingleGroup(result);
        group.GenericParameters!.GetAllNotNull().Select(p => p.Identifier).Should().Equal("T");
        TypeSpelling(group.TargetType).Should().Be("\\App\\MyClass");
    }

    [Fact]
    public void Parse_GroupPartialGenericApplication_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension Helpers {
                extends<TRight> \App\Pair<string, TRight> {
                    function right(): TRight { return $this->right; }
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var group = SingleGroup(result);
        group.GenericParameters!.GetAllNotNull().Select(p => p.Identifier).Should().Equal("TRight");
        TypeSpelling(group.TargetType).Should().Be("\\App\\Pair");
    }

    [Fact]
    public void Parse_OperatorsListOperandsWithSelf_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension MoneyOps extends \App\Money {
                operator + (self $left, self $right): self => $left;
                operator + (int $left, self $right): self => $right;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var ops = SingleExtension(result).FunctionList!.GetAllNotNull().OfType<TyhpOperatorOverloadAst>().ToList();
        ops.Should().HaveCount(2);
        ops.Should().OnlyContain(op => op.ExtensionTargetType == null && op.Op!.ValueString == "+");
        ParameterSpellings(ops[0]).Should().Equal(("self", "$left"), ("self", "$right"));
        ParameterSpellings(ops[1]).Should().Equal(("int", "$left"), ("self", "$right"));
    }

    [Fact]
    public void Parse_ByRefReceiverAnnotation_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension IntHelpers extends int {
                function sort(&$this): void { \sort($this); }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var function = SingleExtension(result).FunctionList!.GetAllNotNull().OfType<PhpFunctionDeclAst>().Single();
        function.Parameters!.GetAllNotNull().Should().BeEmpty();
        function.AstGrammarAddons.Should().ContainKey(TyhpExtensionDeclAst.ByRefReceiverAddonKey);
    }

    [Fact]
    public void Parse_ShortFnImpliedThis_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension StringExtensions extends string {
                fn reverse(): string => $this;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var function = SingleExtension(result).FunctionList!.GetAllNotNull().OfType<PhpFunctionDeclAst>().Single();
        function.IsShortSyntax.Should().BeTrue();
        function.Parameters!.GetAllNotNull().Should().BeEmpty();
        function.AstGrammarAddons.Should().NotContainKey(TyhpExtensionDeclAst.ByRefReceiverAddonKey);
    }

    [Fact]
    public void Parse_HeaderAndNestedGroupTogether_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension Mixed extends int {
                extends string {
                    fn abs(): int => 0;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var decl = SingleExtension(result);
        TypeSpelling(decl.TargetType).Should().Be("int");
        decl.FunctionList!.GetAllNotNull().OfType<TyhpExtensionDeclAst>().Should().ContainSingle();
    }

    [Fact]
    public void Parse_LegacyExtendsThis_ReportsBlockTargetDiagnostic()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension StringExtensions {
                function length(extends string $this): int { return 0; }
            }
            """);

        var error = result.Diagnostics.Errors.Should().ContainSingle(item =>
            item.Code == MessageCode.ParserExtensionLegacyMemberTarget).Subject;
        error.Message.Should().Contain("extension Name extends Type");
        result.Diagnostics.Errors.Should().NotContain(item =>
            item.Code == MessageCode.ParserUnexpectedError);
    }

    [Fact]
    public void Parse_LegacyOperatorTarget_ReportsBlockTargetDiagnostic()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension MoneyOps {
                operator +<Money>(self $left, self $right): self { return $left; }
            }
            """);

        result.Diagnostics.Errors.Should().Contain(error =>
            error.Code == MessageCode.ParserExtensionLegacyMemberTarget);
        result.Diagnostics.Errors.Should().NotContain(error =>
            error.Code == MessageCode.ParserUnexpectedError);
    }

    [Fact]
    public void Parse_TyhpdefLegacyReceiver_ReportsBlockTargetDiagnostic()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extension StringExtensions {
                fn length(extends string $this): int => 0;
            }
            """);

        result.Diagnostics.Errors.Should().Contain(error =>
            error.Code == MessageCode.ParserExtensionLegacyMemberTarget);
        result.Diagnostics.Errors.Should().NotContain(error =>
            error.Code == MessageCode.ParserUnexpectedError);
    }

    [Fact]
    public void Parse_UseExtensionOperatorQualifier_StillSucceeds()
    {
        var result = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            use extension MoneyOps {
                MoneyOps::operator +<Money> hide;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var import = Flatten(result.Ast).OfType<TyhpImportExtensionAst>().Should().ContainSingle().Subject;
        import.Adaptations!.GetAllNotNull().OfType<PhpTraitAliasAst>().Should().Contain(alias =>
            alias.IsHide
            && alias.MethodReference != null
            && alias.MethodReference.IsOperator
            && alias.MethodReference.OperatorTarget != null);
    }

    [Fact]
    public void Parse_ClassBodyTyhpdefExtensionFn_Unchanged()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            class Holder {
                extension fn toUpper(): string => \strtoupper($this);
                extension operator +(self $left, self $right): self => $left;
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        Flatten(result.Ast).OfType<TyhpdefInlineExtensionFunctionAst>().Should().ContainSingle();
        Flatten(result.Ast).OfType<TyhpOperatorOverloadAst>().Should().Contain(op => op.IsInlineExtension || op.ExtensionTargetType == null);
    }

    [Fact]
    public void Parse_TyhpdefHeaderAndGroup_Succeeds()
    {
        var result = ParserTestHelper.ParseTyhpdefContent("""
            <?tyhpdef
            extension StringExtensions extends string {
                fn reverse(): string => $this;
            }
            extension NumericHelpers {
                extends<T> \App\MyClass<T> {
                    fn id(): T => $this->id;
                }
            }
            """);

        result.Diagnostics.Errors.Should().BeEmpty(Describe(result));
        var decls = Flatten(result.Ast).OfType<TyhpdefStandaloneExtensionDeclAst>().ToList();
        decls.Should().HaveCount(2);
        var strings = decls.Single(decl => decl.Identifier == "StringExtensions");
        TypeSpelling(strings.TargetType).Should().Be("string");
        var helpers = decls.Single(decl => decl.Identifier == "NumericHelpers");
        var group = helpers.FunctionList!.GetAllNotNull().OfType<TyhpExtensionDeclAst>().Should().ContainSingle().Subject;
        group.IsTargetGroup.Should().BeTrue();
        group.GenericParameters!.GetAllNotNull().Select(p => p.Identifier).Should().Equal("T");
    }

    private static TyhpExtensionDeclAst SingleExtension(ParseResult result)
        => Flatten(result.Ast).OfType<TyhpExtensionDeclAst>().Single(decl => !decl.IsTargetGroup);

    private static TyhpExtensionDeclAst SingleGroup(ParseResult result)
        => SingleExtension(result).FunctionList!.GetAllNotNull().OfType<TyhpExtensionDeclAst>().Single();

    private static IEnumerable<(string Type, string Name)> ParameterSpellings(TyhpOperatorOverloadAst op)
    {
        yield return (TypeSpelling(op.LeftParameter!.Type), op.LeftParameter.Name);
        if (op.RightParameter != null)
        {
            yield return (TypeSpelling(op.RightParameter.Type), op.RightParameter.Name);
        }
    }

    private static string TypeSpelling(ITypeExpression? type) => type switch
    {
        PhpBuiltinTypeAst builtin => builtin.Identifier ?? "",
        PhpNamedTypeAst named when named.Name is PhpNameAst name => name.ValueString ?? name.Identifier ?? "",
        PhpTypeExpressionAst { TypeKind: PhpTypeKind.Simple, Types: { } types }
            when types.GetAllNotNull().Count() == 1 =>
            TypeSpelling(types.GetAllNotNull().Single()),
        _ => type?.GetType().Name ?? "",
    };

    private static string Describe(ParseResult result)
        => string.Join("; ", result.Diagnostics.Errors.Select(error => $"{error.Code}: {error.Message}"));

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
