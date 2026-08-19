using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.LanguageServer.Analysis;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class SymbolFormatterTests
{
    [Fact]
    public void FormatFunctionSignature_IncludesParametersAndReturnType()
    {
        (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(
            "<?tyhp\nfunction greet(string $name): string { return $name; }\n",
            "fmt-fn.tyhp");
        scope.Should().NotBeNull();
        var finder = new SymbolFinder();
        BaseSymbol? symbol = finder.FindSymbolAtPosition(ast, scope, line: 2, column: 9);
        symbol.Should().BeOfType<FunctionDeclarationSymbol>();

        string formatted = SymbolFormatter.FormatFunctionSignature((FunctionDeclarationSymbol)symbol!);
        formatted.Should().Contain("function greet");
        formatted.Should().Contain("string");
        formatted.Should().Contain("$name");
    }

    [Fact]
    public void FormatHover_IncludesKindFenceAndDeprecation()
    {
        var symbol = new FunctionDeclarationSymbol("legacy", sourceFile: "a.tyhp")
        {
            IsDeprecated = true,
            DocComment = "/** Old helper. */",
        };

        string hover = SymbolFormatter.FormatHover(symbol);
        hover.Should().Contain("function");
        hover.Should().Contain("```tyhp");
        hover.Should().Contain("function legacy");
        hover.Should().Contain("Old helper.");
        hover.Should().Contain("**Deprecated**");
    }

    [Fact]
    public void FormatClassSignature_IncludesExtendsAndImplements()
    {
        (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            interface Named {}
            class Animal {}
            class Dog extends Animal implements Named {}
            """,
            "fmt-class.tyhp");
        scope.Should().NotBeNull();
        var finder = new SymbolFinder();
        PhpObjectTypeDeclAst? dog = FindFirst<PhpObjectTypeDeclAst>(ast, "Dog");
        dog.Should().NotBeNull();
        BaseSymbol? symbol = finder.FindSymbolAtPosition(ast, scope, dog!.Line, dog.Column + 6);
        symbol.Should().BeOfType<ObjectDeclarationSymbol>();

        string formatted = SymbolFormatter.FormatClassSignature((ObjectDeclarationSymbol)symbol!);
        formatted.Should().Contain("class Dog");
        formatted.Should().Contain("extends");
        formatted.Should().Contain("Animal");
        formatted.Should().Contain("implements");
        formatted.Should().Contain("Named");
    }

    [Fact]
    public void FormatPropertySignature_IncludesArrayGenericArguments()
    {
        (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            class Type {
                private static array<string, self> $singletons = [];
                private array<self|string> $members = [];
            }
            """,
            "fmt-array-generic.tyhp");
        scope.Should().NotBeNull();

        ObjectDeclarationSymbol type = FindClass(scope!, "Type");
        ObjectPropertySymbol singletons = FindProperty(type, "singletons");
        SymbolFormatter.FormatPropertySignature(singletons).Should().Contain("array<string, self>");

        ObjectPropertySymbol members = FindProperty(type, "members");
        string membersSig = SymbolFormatter.FormatPropertySignature(members);
        membersSig.Should().Contain("array<");
        membersSig.Should().Contain("self");
        membersSig.Should().Contain("string");
    }

    [Fact]
    public void FormatPropertySignature_IncludesNamedGenericArguments()
    {
        (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            class Box<T> {}
            class Holder {
                public Box<string> $box;
            }
            """,
            "fmt-named-generic.tyhp");
        scope.Should().NotBeNull();

        ObjectDeclarationSymbol holder = FindClass(scope!, "Holder");
        ObjectPropertySymbol box = FindProperty(holder, "box");
        SymbolFormatter.FormatPropertySignature(box).Should().Contain("Box<string>");
    }

    [Fact]
    public void FormatHover_OnParameter_PrefersNarrowedTypeAndShowsDeclared()
    {
        (SrcFileAst ast, GlobalScope? _) = ParseAndBind(
            "<?tyhp\nfunction greet(?string $name): void {}\n",
            "fmt-hover-narrow.tyhp");
        PhpParameterAst? param = FindFirst<PhpParameterAst>(ast);
        param.Should().NotBeNull();
        var symbol = new VariableSymbol("name")
        {
            IsParameter = true,
            DeclaredType = param!.Type,
        };

        string hover = SymbolFormatter.FormatHover(symbol, CheckedTypes.String);
        hover.Should().Contain("```tyhp\nstring $name\n```");
        hover.Should().Contain("declared `?string`");
        hover.Should().NotContain("```tyhp\n?string $name\n```");
    }

    [Fact]
    public void FormatHover_OnParameter_OmitsDeclaredNoteWhenTypesMatch()
    {
        (SrcFileAst ast, GlobalScope? _) = ParseAndBind(
            "<?tyhp\nfunction greet(?string $name): void {}\n",
            "fmt-hover-same.tyhp");
        PhpParameterAst? param = FindFirst<PhpParameterAst>(ast);
        param.Should().NotBeNull();
        var symbol = new VariableSymbol("name")
        {
            IsParameter = true,
            DeclaredType = param!.Type,
        };

        string hover = SymbolFormatter.FormatHover(symbol);
        hover.Should().Contain("?string $name");
        hover.Should().NotContain("declared `");
    }

    private static (SrcFileAst Ast, GlobalScope? Scope) ParseAndBind(string content, string fileName)
    {
        using var compilation = new CompilationService();
        var diagnostics = new DiagnosticBag();
        var options = new CompilationOptions
        {
            EnableAstCache = false,
            ProjectPath = Path.GetTempPath(),
            SkipChecking = true,
        };
        SrcFileAst? ast = compilation.ParseFromContent(content, fileName, diagnostics, options);
        ast.Should().NotBeNull();
        var binder = new TyhpBinder(diagnostics, options);
        GlobalScope? scope = binder.Bind([ast!]);
        return (ast!, scope);
    }

    private static ObjectDeclarationSymbol FindClass(GlobalScope scope, string name)
    {
        ObjectDeclarationSymbol? found = FindClassInScope(scope, name);
        if (found is not null)
        {
            return found;
        }

        throw new InvalidOperationException($"Class '{name}' was not bound.");

        static ObjectDeclarationSymbol? FindClassInScope(IBaseScope current, string name)
        {
            if (current.DeclarationSymbol is ObjectDeclarationSymbol obj
                && string.Equals(obj.Name, name, StringComparison.Ordinal))
            {
                return obj;
            }

            foreach (IBaseSymbol child in current.GetAllChildSymbols())
            {
                if (child is ObjectDeclarationSymbol nested
                    && string.Equals(nested.Name, name, StringComparison.Ordinal))
                {
                    return nested;
                }
            }

            foreach (IBaseScope childScope in current.GetAllChildScopes())
            {
                ObjectDeclarationSymbol? nested = FindClassInScope(childScope, name);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }
    }

    private static ObjectPropertySymbol FindProperty(ObjectDeclarationSymbol type, string name)
    {
        foreach (IBaseSymbol member in type.EnumerateMembersAndConstants())
        {
            if (member is ObjectPropertySymbol property
                && string.Equals(
                    property.Name.TrimStart('$'),
                    name.TrimStart('$'),
                    StringComparison.Ordinal))
            {
                return property;
            }
        }

        throw new InvalidOperationException($"Property '{name}' was not bound on {type.Name}.");
    }

    private static T? FindFirst<T>(Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast node, string? identifier = null)
        where T : class, Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast
    {
        if (node is T match
            && (identifier is null || string.Equals(match.Identifier, identifier, StringComparison.Ordinal)))
        {
            return match;
        }

        foreach (Tyhp.TyhpLang.Ast.Interfaces.IBase2Ast? child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            T? found = FindFirst<T>(child, identifier);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
