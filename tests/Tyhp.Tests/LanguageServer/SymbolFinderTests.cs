using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.LanguageServer.Analysis;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.Tests.LanguageServer;

[Trait("Category", "LanguageServer")]
public class SymbolFinderTests
{
    private const string Source = """
        <?tyhp

        function greet(string $name): string {
            return $name;
        }
        """;

    [Fact]
    public void FindNodeAtPosition_OnFunctionName_ReturnsFunctionDeclaration()
    {
        (SrcFileAst ast, _) = ParseAndBind(Source, "finder.tyhp");
        var finder = new SymbolFinder();
        IBase2Ast? node = finder.FindNodeAtPosition(ast, line: 3, column: 9);

        node.Should().NotBeNull();
        WalkTo<PhpFunctionDeclAst>(node).Should().NotBeNull();
    }

    [Fact]
    public void FindNodeAtPosition_OnVariable_ReturnsVariableNode()
    {
        (SrcFileAst ast, _) = ParseAndBind(Source, "finder-var.tyhp");
        PhpVariableAst? variable = FindFirst<PhpVariableAst>(ast);
        variable.Should().NotBeNull();

        var finder = new SymbolFinder();
        IBase2Ast? node = finder.FindNodeAtPosition(ast, variable!.Line, variable.Column);

        node.Should().NotBeNull();
        IsOrContains(variable, node!).Should().BeTrue();
    }

    [Fact]
    public void FindSymbolAtPosition_OnFunction_ReturnsFunctionSymbol()
    {
        (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(Source, "finder-symbol.tyhp");
        scope.Should().NotBeNull();
        var finder = new SymbolFinder();
        BaseSymbol? symbol = finder.FindSymbolAtPosition(ast, scope, line: 3, column: 9);

        symbol.Should().NotBeNull();
        symbol!.Name.Should().Be("greet");
        finder.FindDeclaringNode(symbol).Should().NotBeNull();
    }

        [Fact]
        public void FindSymbolAtPosition_OnFunctionCall_ResolvesCallee()
        {
            (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(
                """
                <?tyhp
                function greet(string $name): string { return $name; }
                function run(): void { greet("Ada"); }
                """,
                "finder-call.tyhp");
            scope.Should().NotBeNull();
            PhpNameAst? callName = FindName(ast, "greet", last: true);
            callName.Should().NotBeNull();

            var finder = new SymbolFinder();
            BaseSymbol? symbol = finder.FindSymbolAtPosition(ast, scope, callName!.Line, callName.Column);
            symbol.Should().NotBeNull();
            symbol!.Name.Should().Be("greet");
        }

        [Fact]
        public void LookupAtPosition_OnWhitespace_ReturnsNull()
        {
            (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(Source, "finder-ws.tyhp");
            var finder = new SymbolFinder();
            finder.LookupAtPosition(ast, scope, tree: null, line: 2, column: 0).Should().BeNull();
        }

        [Fact]
        public void LookupAtPosition_OnNamespacedTypeInOtherFile_ResolvesClass()
        {
            using var compilation = new CompilationService();
            var diagnostics = new DiagnosticBag();
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                ProjectPath = Path.GetTempPath(),
                SkipChecking = true,
            };
            SrcFileAst? userAst = compilation.ParseFromContent(
                "<?tyhp\nnamespace App;\nclass User {}\n",
                Path.Combine(Path.GetTempPath(), "finder-cross-user.tyhp"),
                diagnostics,
                options);
            SrcFileAst? mainAst = compilation.ParseFromContent(
                "<?tyhp\nnamespace App;\nfunction make(): User { return new User(); }\n",
                Path.Combine(Path.GetTempPath(), "finder-cross-main.tyhp"),
                diagnostics,
                options);
            userAst.Should().NotBeNull();
            mainAst.Should().NotBeNull();
            var binder = new TyhpBinder(diagnostics, options);
            GlobalScope? scope = binder.Bind([userAst!, mainAst!]);
            scope.Should().NotBeNull();

            PhpNameAst? typeName = FindName(mainAst!, "User", last: false);
            typeName.Should().NotBeNull();

            var finder = new SymbolFinder();
            SymbolLookupResult? lookup = finder.LookupAtPosition(
                mainAst!,
                scope,
                tree: null,
                typeName!.Line,
                typeName.Column);
            lookup.Should().NotBeNull();
            lookup!.Symbol.Should().NotBeNull();
            lookup.Symbol!.Name.Should().Be("User");
            lookup.Symbol.Should().BeOfType<ObjectDeclarationSymbol>();
        }

        [Fact]
        public void LookupAtPosition_OnSelf_ResolvesContainingClass()
        {
            (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(
                """
                <?tyhp
                class User {
                    public static function create(): self {
                        return new self();
                    }
                }
                """,
                "finder-self.tyhp");
            scope.Should().NotBeNull();
            PhpNameAst? selfName = FindName(ast, "self", last: false);
            selfName.Should().NotBeNull();

            var finder = new SymbolFinder();
            BaseSymbol? symbol = finder.FindSymbolAtPosition(ast, scope, selfName!.Line, selfName.Column);
            symbol.Should().NotBeNull();
            symbol!.Name.Should().Be("User");
        }

        [Fact]
        public void FindReferences_IncludesDeclarationAndUsage()
        {
            (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(Source, "finder-refs.tyhp");
            scope.Should().NotBeNull();
            var finder = new SymbolFinder();
            BaseSymbol? symbol = finder.FindSymbolAtPosition(ast, scope, line: 3, column: 26);
            symbol.Should().NotBeNull();

            IReadOnlyList<IBase2Ast> refs = finder.FindReferences(symbol!, [ast], scope);
            refs.Should().HaveCountGreaterThanOrEqualTo(2);
        }

        [Fact]
        public void FindReferences_OnFunction_IncludesDeclarationAndCallSite()
        {
            (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(
                """
                <?tyhp
                function greet(string $name): string { return $name; }
                function run(): void { greet("Ada"); }
                """,
                "finder-fn-refs.tyhp");
            scope.Should().NotBeNull();
            PhpNameAst? callName = FindName(ast, "greet", last: true);
            callName.Should().NotBeNull();

            var finder = new SymbolFinder();
            BaseSymbol? symbol = finder.FindSymbolAtPosition(ast, scope, callName!.Line, callName.Column);
            symbol.Should().NotBeNull();
            symbol!.Name.Should().Be("greet");

            IReadOnlyList<SymbolReference> refs = finder.FindReferences(symbol!, [ast], scope, tree: null);
            refs.Should().HaveCountGreaterThanOrEqualTo(2);
            refs.Count(item => item.IsDeclaration).Should().BeGreaterThanOrEqualTo(1);
            refs.Count(item => !item.IsDeclaration).Should().BeGreaterThanOrEqualTo(1);
        }

        [Fact]
        public void FindReferences_OnClass_IncludesTypeAndNewUsages()
        {
            (SrcFileAst ast, GlobalScope? scope) = ParseAndBind(
                """
                <?tyhp
                class User {}
                function make(): User { return new User(); }
                """,
                "finder-class-refs.tyhp");
            scope.Should().NotBeNull();
            PhpNameAst? typeName = FindName(ast, "User", last: false);
            typeName.Should().NotBeNull();

            var finder = new SymbolFinder();
            BaseSymbol? symbol = finder.FindSymbolAtPosition(ast, scope, typeName!.Line, typeName.Column);
            symbol.Should().NotBeNull();

            IReadOnlyList<SymbolReference> refs = finder.FindReferences(symbol!, [ast], scope, tree: null);
            refs.Should().HaveCountGreaterThanOrEqualTo(3);
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

    private static T? FindFirst<T>(IBase2Ast node) where T : class, IBase2Ast
    {
        if (node is T match)
        {
            return match;
        }

        foreach (IBase2Ast? child in node.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            T? found = FindFirst<T>(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static PhpNameAst? FindName(IBase2Ast node, string name, bool last)
    {
        PhpNameAst? match = null;
        Walk(node);
        return match;

        void Walk(IBase2Ast current)
        {
            if (current is PhpNameAst nameAst
                && string.Equals(nameAst.ValueString, name, StringComparison.Ordinal))
            {
                match = nameAst;
                if (!last)
                {
                    return;
                }
            }

            foreach (IBase2Ast? child in current.AstChildren)
            {
                if (child is null)
                {
                    continue;
                }

                Walk(child);
                if (!last && match is not null)
                {
                    return;
                }
            }
        }
    }

    private static bool IsOrContains(IBase2Ast root, IBase2Ast target)
    {
        if (ReferenceEquals(root, target))
        {
            return true;
        }

        foreach (IBase2Ast? child in root.AstChildren)
        {
            if (child is not null && IsOrContains(child, target))
            {
                return true;
            }
        }

        return false;
    }

    private static T? WalkTo<T>(IBase2Ast? node) where T : class, IBase2Ast
    {
        while (node is not null)
        {
            if (node is T match)
            {
                return match;
            }

            node = node.AstChildren.FirstOrDefault(child => child is not null);
        }

        return node as T;
    }
}
