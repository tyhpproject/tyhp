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
using Tyhp.TyhpLang.Enum;

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
    public void FormatHover_OnExternType_ShowsExternKindAndProvidedBy()
    {
        var symbol = new ObjectDeclarationSymbol("Peer", sourceFile: "externs.tyhpdef")
        {
            ObjectKind = PhpTypeDeclType.Class,
            IsExtern = true,
            ProvidedBy = "tyhpdef/example",
        };

        string hover = SymbolFormatter.FormatHover(symbol);
        hover.Should().Contain("extern class");
        hover.Should().Contain("```tyhp");
        hover.Should().Contain("extern class Peer");
        hover.Should().Contain("`@provided-by: tyhpdef/example`");
    }

    [Fact]
    public void FormatHover_OnExternTypeWithoutProvidedBy_OmitsProvidedByLine()
    {
        var symbol = new ObjectDeclarationSymbol("Peer", sourceFile: "externs.tyhpdef")
        {
            ObjectKind = PhpTypeDeclType.Interface,
            IsExtern = true,
        };

        string hover = SymbolFormatter.FormatHover(symbol);
        hover.Should().Contain("extern interface");
        hover.Should().Contain("extern interface Peer");
        hover.Should().NotContain("@provided-by");
    }

    [Fact]
    public void FormatHover_OnBareExternType_ShowsExternWithoutKindWord()
    {
        var symbol = new ObjectDeclarationSymbol("Peer", sourceFile: "externs.tyhpdef")
        {
            ObjectKind = PhpTypeDeclType.Unspecified,
            IsExtern = true,
            ProvidedBy = "tyhpdef/example",
        };

        string hover = SymbolFormatter.FormatHover(symbol);
        hover.Should().Contain("extern");
        hover.Should().NotContain("extern class");
        hover.Should().Contain("extern Peer");
        hover.Should().Contain("`@provided-by: tyhpdef/example`");
        SymbolFormatter.KindLabel(symbol).Should().Be("extern");
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

    [Fact]
    public void FormatHover_OnObjectShapeAlias_DescribesObjectShape()
    {
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            type ClockShape = object {
                public function now(): string;
            };
            """,
            "fmt-shape.tyhp");
        scope.Should().NotBeNull();
        TypeAliasSymbol? symbol = FindTypeAlias(scope!, "ClockShape");
        symbol.Should().NotBeNull();
        symbol!.AliasedType.Should().BeOfType<TyhpObjectShapeAst>();

        string formatted = SymbolFormatter.FormatHover(symbol);
        formatted.Should().Contain("object shape");
        formatted.Should().Contain("type ClockShape = object {");
        formatted.Should().Contain("function now");
        formatted.Should().Contain(": string");
        SymbolFormatter.KindLabel(symbol).Should().Be("object shape");
    }

    [Fact]
    public void FormatHover_OnCallableShapeAlias_DescribesCallableShape()
    {
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            type Mapper = callable(int $i): string;
            """,
            "fmt-callable-shape.tyhp");
        scope.Should().NotBeNull();
        TypeAliasSymbol? symbol = FindTypeAlias(scope!, "Mapper");
        symbol.Should().NotBeNull();

        string formatted = SymbolFormatter.FormatHover(symbol!);
        formatted.Should().Contain("callable shape");
        formatted.Should().Contain("type Mapper = callable(int $i): string");
        SymbolFormatter.KindLabel(symbol!).Should().Be("callable shape");
    }

    [Fact]
    public void TryFormatTypeExpressionHover_OnInlineCallableShape_DescribesCallableShape()
    {
        (SrcFileAst ast, GlobalScope? _) = ParseAndBind(
            """
            <?tyhp
            function apply(callable(int $i): string $cb): string {
                return $cb(1);
            }
            """,
            "fmt-inline-callable.tyhp");
        TyhpCallableShapeAst? shape = FindFirst<TyhpCallableShapeAst>(ast);
        shape.Should().NotBeNull();

        string? formatted = SymbolFormatter.TryFormatTypeExpressionHover(shape!, shape!.BoundSymbol as BaseSymbol);
        formatted.Should().NotBeNull();
        formatted.Should().Contain("callable shape");
        formatted.Should().Contain("callable(int $i): string");
    }

    [Fact]
    public void FormatHover_OnStructShapeAlias_DescribesStructShape()
    {
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            type Point = struct {
                float $x;
                float $y;
            };
            """,
            "fmt-struct-shape.tyhp");
        scope.Should().NotBeNull();
        ObjectDeclarationSymbol point = FindClass(scope!, "Point");
        point.IsStruct.Should().BeTrue();

        string formatted = SymbolFormatter.FormatHover(point);
        formatted.Should().Contain("struct shape");
        formatted.Should().Contain("type Point = struct {");
        formatted.Should().Contain("float $x");
        formatted.Should().Contain("float $y");
        SymbolFormatter.KindLabel(point).Should().Be("struct shape");
    }

    [Fact]
    public void FormatMethodSignature_ExtensionMethodOmitsImpliedReceiver()
    {
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            extension StringOps extends string {
                function pad(int $width): string {
                    return $this;
                }

                function identity(): string {
                    return $this;
                }
            }

            class Box {
                public function pad(int $width): string {
                    return "x";
                }

                public static function make(int $seed): Box {
                    return new Box();
                }
            }
            """,
            "fmt-ext-sig.tyhp");
        scope.Should().NotBeNull();

        ObjectMethodSymbol pad = FindMethod(scope!, "StringOps", "pad");
        pad.Parameters.Should().NotBeEmpty();
        pad.Parameters[0].Name.Should().Be("$this");

        string padSignature = SymbolFormatter.FormatMethodSignature(pad);
        padSignature.Should().Be("function pad(int $width): string");
        padSignature.Should().NotContain("$this");
        padSignature.Should().NotContain("extends");
        padSignature.Should().NotContain("static");
        SymbolFormatter.CallerFacingParameters(pad).Select(parameter => parameter.Name)
            .Should().Equal("$width");

        ObjectMethodSymbol identity = FindMethod(scope!, "StringOps", "identity");
        identity.Parameters[0].Name.Should().Be("$this");
        SymbolFormatter.FormatMethodSignature(identity).Should().Be("function identity(): string");
        SymbolFormatter.FormatHover(identity).Should().Contain("function identity(): string");
        SymbolFormatter.FormatHover(identity).Should().NotContain("$this");

        ObjectMethodSymbol boxPad = FindMethod(scope!, "Box", "pad");
        SymbolFormatter.FormatMethodSignature(boxPad).Should().Contain("function pad(int $width): string");
        SymbolFormatter.FormatMethodSignature(boxPad).Should().Contain("$width");

        ObjectMethodSymbol make = FindMethod(scope!, "Box", "make");
        SymbolFormatter.FormatMethodSignature(make).Should().Contain("static");
        SymbolFormatter.FormatMethodSignature(make).Should().Contain("$seed");
    }

    [Fact]
    public void FormatMethodSignature_InlineTyhpdefExtensionOmitsImpliedReceiver()
    {
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhpdef
            class Money {
                extension fn label(string $locale): string => $locale;
            }
            """,
            "fmt-inline-ext.tyhpdef");
        scope.Should().NotBeNull();
        ObjectDeclarationSymbol money = FindClass(scope!, "Money");
        ObjectMethodSymbol? label = money.FindSyntheticInlineMember("label");
        label.Should().NotBeNull();
        label!.Parameters.Should().NotBeEmpty();
        label.Parameters[0].Name.Should().Be("$this");

        string signature = SymbolFormatter.FormatMethodSignature(label);
        signature.Should().Be("function label(string $locale): string");
        signature.Should().NotContain("$this");
        signature.Should().NotContain("extends");
    }

    [Fact]
    public void FormatClassSignature_OnExtensionHeader_ShowsExtendsTarget()
    {
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            extension StringOps<T> extends string {
                function id(T $value): T { return $value; }
            }
            """,
            "fmt-ext-header-sig.tyhp");
        scope.Should().NotBeNull();

        ObjectDeclarationSymbol stringOps = FindClass(scope!, "StringOps");
        string formatted = SymbolFormatter.FormatClassSignature(stringOps);
        formatted.Should().Be("extension StringOps<T> extends string");
    }

    [Fact]
    public void FormatClassSignature_OnMixedBagExtension_OmitsHeaderTarget()
    {
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            extension NumericHelpers {
                extends int {
                    function abs(): int { return $this; }
                }
            }
            """,
            "fmt-ext-mixed-sig.tyhp");
        scope.Should().NotBeNull();

        ObjectDeclarationSymbol numericHelpers = FindClass(scope!, "NumericHelpers");
        string formatted = SymbolFormatter.FormatClassSignature(numericHelpers);
        formatted.Should().Be("extension NumericHelpers");
    }

    [Fact]
    public void FormatClassSignature_OnGenericExtensionHeader_ShowsGenericExtendsTarget()
    {
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            extension ArrayOps<T> extends array<T> {
                function first(): T { return $this[0]; }
            }
            """,
            "fmt-ext-generic-header-sig.tyhp");
        scope.Should().NotBeNull();

        ObjectDeclarationSymbol arrayOps = FindClass(scope!, "ArrayOps");
        string formatted = SymbolFormatter.FormatClassSignature(arrayOps);
        formatted.Should().Be("extension ArrayOps<T> extends array<T>");
    }

    [Fact]
    public void FormatClassSignature_OnNestedExtensionGroup_ShowsGroupExtendsTarget()
    {
        // The header has no `extends`, so only the nested `extends int { }` group carries
        // a target — on its own synthetic ObjectDeclarationSymbol, not the header's. That
        // symbol's Name is an internal placeholder (`__TyhpExtTargetN`), so the formatted
        // signature must be exactly the bare group header, not `class __TyhpExtTarget1 …`.
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            extension NumericHelpers {
                extends int {
                    function abs(): int { return $this; }
                }
            }
            """,
            "fmt-ext-nested-group-sig.tyhp");
        scope.Should().NotBeNull();

        ObjectDeclarationSymbol group = FindExtensionTargetGroup(scope!);
        group.IsExtensionTargetGroup.Should().BeTrue();
        group.IsExtension.Should().BeFalse();
        group.Name.Should().StartWith("__TyhpExtTarget");

        string formatted = SymbolFormatter.FormatClassSignature(group);
        formatted.Should().Be("extends int");
        formatted.Should().NotContain("__TyhpExtTarget");
        formatted.Should().NotContain("class");
    }

    [Fact]
    public void FormatClassSignature_OnGenericNestedExtensionGroup_ShowsGroupOwnGenericTarget()
    {
        // `extends<T>` on a nested group declares the group's own generic parameter (not
        // the header's) placed right after `extends`, matching source order: `extends<T> Type`.
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            extension Boxes {
                extends<T> array<T> {
                    function first(): T { return $this[0]; }
                }
            }
            """,
            "fmt-ext-nested-generic-group-sig.tyhp");
        scope.Should().NotBeNull();

        ObjectDeclarationSymbol group = FindExtensionTargetGroup(scope!);
        group.IsExtensionTargetGroup.Should().BeTrue();

        string formatted = SymbolFormatter.FormatClassSignature(group);
        formatted.Should().Be("extends<T> array<T>");
    }

    [Fact]
    public void FormatClassSignature_OnOrdinaryClass_IgnoresPendingExtensionBlockTarget()
    {
        // A plain class must keep reading ExtendsType even when PendingExtensionBlockTarget
        // is populated (e.g. stale/unrelated binder state) — extends-target selection is
        // keyed on IsExtension/IsExtensionTargetGroup, not on which field happens to be set.
        (SrcFileAst _, GlobalScope? scope) = ParseAndBind(
            """
            <?tyhp
            class Animal {}
            class Dog extends Animal {}
            extension StringOps extends string {}
            """,
            "fmt-class-ignores-pending-target.tyhp");
        scope.Should().NotBeNull();

        ObjectDeclarationSymbol dog = FindClass(scope!, "Dog");
        ObjectDeclarationSymbol stringOps = FindClass(scope!, "StringOps");
        dog.PendingExtensionBlockTarget = stringOps.PendingExtensionBlockTarget; // bogus stray value

        string formatted = SymbolFormatter.FormatClassSignature(dog);
        formatted.Should().Be("class Dog extends Animal");
    }

    private static ObjectMethodSymbol FindMethod(GlobalScope scope, string typeName, string methodName)
    {
        ObjectDeclarationSymbol type = FindClass(scope, typeName);
        if (type.Members.TryGetValue(methodName, out IBaseSymbol? member)
            && member is ObjectMethodSymbol method)
        {
            return method;
        }

        foreach (IBaseSymbol candidate in type.Members.Values)
        {
            if (candidate is ObjectMethodSymbol named
                && string.Equals(named.Name, methodName, StringComparison.Ordinal))
            {
                return named;
            }
        }

        throw new InvalidOperationException($"Method '{typeName}::{methodName}' was not bound.");
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

    private static TypeAliasSymbol? FindTypeAlias(IBaseScope scope, string name)
    {
        foreach (IBaseSymbol child in scope.GetAllChildSymbols())
        {
            if (child is TypeAliasSymbol alias
                && string.Equals(alias.Name, name, StringComparison.Ordinal))
            {
                return alias;
            }
        }

        foreach (IBaseScope childScope in scope.GetAllChildScopes())
        {
            TypeAliasSymbol? nested = FindTypeAlias(childScope, name);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
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

    /// <summary>
    /// Finds a compiler-generated nested <c>extends Type { }</c> group's own
    /// <see cref="ObjectDeclarationSymbol"/> (<see cref="ObjectDeclarationSymbol.IsExtensionTargetGroup"/>).
    /// Unlike <see cref="FindClass"/>, the group is not registered as a named child symbol —
    /// it is only reachable as a child scope's <c>DeclarationSymbol</c>.
    /// </summary>
    private static ObjectDeclarationSymbol FindExtensionTargetGroup(GlobalScope scope)
    {
        ObjectDeclarationSymbol? found = FindInScope(scope);
        if (found is not null)
        {
            return found;
        }

        throw new InvalidOperationException("No extension target group was bound.");

        static ObjectDeclarationSymbol? FindInScope(IBaseScope current)
        {
            if (current.DeclarationSymbol is ObjectDeclarationSymbol obj && obj.IsExtensionTargetGroup)
            {
                return obj;
            }

            foreach (IBaseScope childScope in current.GetAllChildScopes())
            {
                ObjectDeclarationSymbol? nested = FindInScope(childScope);
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
