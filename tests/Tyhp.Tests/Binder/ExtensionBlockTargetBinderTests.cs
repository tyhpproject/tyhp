using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Block-target extension binding: header and nested <c>extends</c> groups contribute
/// members to that target, <c>self</c> and <c>$this</c> are the target, and type parameters
/// on the header or group stay in scope.
/// </summary>
[Trait("Category", "Binder")]
public class ExtensionBlockTargetBinderTests
{
    [Fact]
    public void HeaderTarget_ContributesMethodAndOperator()
    {
        var result = Compile("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOps extends Money {
                function doubled(): self { return $this; }
                operator + (self $left, self $right): self => $left;
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var money = FindObject(result.GlobalScope!, "Money");
        money.Should().NotBeNull();

        var method = ResolveExtensionMethod(result.GlobalScope!, "doubled", money!);
        method.Should().NotBeNull();
        method!.Parameters[0].Name.Should().Be("$this");
        method.Parameters[0].DeclaredType!.BoundSymbol.Should().BeSameAs(money);
        method.ReturnType!.BoundSymbol.Should().BeSameAs(money);
        FindThis(method)!.DeclaredType!.BoundSymbol.Should().BeSameAs(money);

        money!.ExtensionContributedOperators.Should().ContainSingle();
        var op = money.ExtensionContributedOperators[0];
        op.ExtensionTargetSymbol.Should().BeSameAs(money);
        op.Parameters.Should().HaveCount(2);
        op.Parameters.Should().OnlyContain(p => p.DeclaredType!.BoundSymbol == money);
    }

    [Fact]
    public void NestedGroups_EachContributeToTheirOwnTarget()
    {
        var result = Compile("""
            <?tyhp
            extension Numeric {
                extends int {
                    fn abs(): int => 0;
                }
                extends float {
                    fn abs(): float => 0;
                }
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var global = result.GlobalScope!;
        var intType = ((IBaseScope)global).FindChildSymbolByName("int");
        var floatType = ((IBaseScope)global).FindChildSymbolByName("float");
        intType.Should().NotBeNull();
        floatType.Should().NotBeNull();

        var intAbs = ResolveExtensionMethod(global, "abs", intType!);
        var floatAbs = ResolveExtensionMethod(global, "abs", floatType!);
        intAbs.Should().NotBeNull();
        floatAbs.Should().NotBeNull();
        intAbs.Should().NotBeSameAs(floatAbs);
        intAbs!.Parameters[0].DeclaredType!.BoundSymbol.Should().BeSameAs(intType);
        floatAbs!.Parameters[0].DeclaredType!.BoundSymbol.Should().BeSameAs(floatType);
        FindThis(intAbs)!.DeclaredType!.BoundSymbol.Should().BeSameAs(intType);
        FindThis(floatAbs)!.DeclaredType!.BoundSymbol.Should().BeSameAs(floatType);
    }

    [Fact]
    public void HeaderGenerics_SelfAndThisAreTheTarget_AndTIsInScope()
    {
        var result = Compile("""
            <?tyhp
            class MyClass<T> {
                public T $id;
            }
            extension MyClassOps<T> extends MyClass<T> {
                function id(): T { return $this->id; }
                function again(): self { return $this; }
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var myClass = FindObject(result.GlobalScope!, "MyClass");
        var ops = FindObject(result.GlobalScope!, "MyClassOps");
        myClass.Should().NotBeNull();
        ops.Should().NotBeNull();
        ops!.GenericParameters.Select(p => p.Name).Should().Equal("T");

        var id = ops.Members["id"] as ObjectMethodSymbol;
        id.Should().NotBeNull();
        id!.ReturnType!.BoundSymbol.Should().BeSameAs(ops.GenericParameters[0]);
        id.Parameters[0].DeclaredType!.BoundSymbol.Should().BeSameAs(myClass);
        FindThis(id)!.DeclaredType!.BoundSymbol.Should().BeSameAs(myClass);
        FirstGenericArgument(ops.PendingExtensionBlockTarget).Should().BeSameAs(ops.GenericParameters[0]);

        var again = ops.Members["again"] as ObjectMethodSymbol;
        again!.ReturnType!.BoundSymbol.Should().BeSameAs(myClass);
    }

    [Fact]
    public void GroupGenerics_SelfAndThisAreTheTarget_AndTIsInScope()
    {
        var result = Compile("""
            <?tyhp
            class MyClass<T> {
                public T $id;
            }
            extension Helpers {
                extends<T> MyClass<T> {
                    function id(): T { return $this->id; }
                    function again(): self { return $this; }
                }
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var myClass = FindObject(result.GlobalScope!, "MyClass");
        var helpers = FindObject(result.GlobalScope!, "Helpers");
        myClass.Should().NotBeNull();
        helpers.Should().NotBeNull();
        helpers!.GenericParameters.Should().BeEmpty();

        var group = FindTargetGroup(helpers);
        group.GenericParameters.Select(p => p.Name).Should().Equal("T");
        group.ExtensionBlockTargetSymbol.Should().BeSameAs(myClass);

        var id = ResolveExtensionMethod(result.GlobalScope!, "id", myClass!);
        id.Should().NotBeNull();
        id!.ReturnType!.BoundSymbol.Should().BeSameAs(group.GenericParameters[0]);
        id.Parameters[0].DeclaredType!.BoundSymbol.Should().BeSameAs(myClass);
        FindThis(id)!.DeclaredType!.BoundSymbol.Should().BeSameAs(myClass);
        FirstGenericArgument(group.PendingExtensionBlockTarget).Should().BeSameAs(group.GenericParameters[0]);

        var again = ResolveExtensionMethod(result.GlobalScope!, "again", myClass!);
        again!.ReturnType!.BoundSymbol.Should().BeSameAs(myClass);
    }

    [Fact]
    public void GroupGenerics_MixedConcreteAndVariableArguments_ResolveEachIndependently()
    {
        // `extends<TRight> Pair<string, TRight>` mixes a concrete argument (`string`) with the
        // group's own open parameter (`TRight`) — both must resolve independently on the target.
        var result = Compile("""
            <?tyhp
            class Pair<TLeft, TRight> {
                public TLeft $left;
                public TRight $right;
            }
            extension Helpers {
                extends<TRight> Pair<string, TRight> {
                    function right(): TRight { return $this->right; }
                }
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var pair = FindObject(result.GlobalScope!, "Pair");
        var stringType = ((IBaseScope)result.GlobalScope!).FindChildSymbolByName("string");
        pair.Should().NotBeNull();
        stringType.Should().NotBeNull();

        var helpers = FindObject(result.GlobalScope!, "Helpers");
        var group = FindTargetGroup(helpers!);
        group.GenericParameters.Select(p => p.Name).Should().Equal("TRight");
        group.ExtensionBlockTargetSymbol.Should().BeSameAs(pair);

        var arguments = GenericArguments(group.PendingExtensionBlockTarget);
        arguments.Should().HaveCount(2);
        arguments[0].Should().BeSameAs(stringType);
        arguments[1].Should().BeSameAs(group.GenericParameters[0]);

        var right = ResolveExtensionMethod(result.GlobalScope!, "right", pair!);
        right.Should().NotBeNull();
        right!.ReturnType!.BoundSymbol.Should().BeSameAs(group.GenericParameters[0]);
        right.Parameters[0].DeclaredType!.BoundSymbol.Should().BeSameAs(pair);
    }

    [Fact]
    public void OverloadSignature_ReceiverIsInsertedOnEveryErasedSignature()
    {
        // Bodyless overload signatures (compile-time-only, erased at emit — see
        // OverloadEmitterTests.Emit_ExtensionFunctionOverloads_EraseSignaturesKeepImplementation)
        // are attached via AttachExtensionFunctionOverload, a separate path from the
        // implementation's BindExtensionFunctionDecl. Each erased signature needs the same
        // implicit `$this` receiver as the implementation, or its parameter list is short by
        // one relative to the implementation that erases it.
        var result = Compile("""
            <?tyhp
            extension StringOps extends string {
                function first(1 $n): string;
                function first(2 $n): array<int, string>;
                fn first(int $n = 1): string|array<int, string> => $this;
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var stringOps = FindObject(result.GlobalScope!, "StringOps");
        var stringType = ((IBaseScope)result.GlobalScope!).FindChildSymbolByName("string");
        stringOps.Should().NotBeNull();

        var primary = stringOps!.Members["first"] as ObjectMethodSymbol;
        primary.Should().NotBeNull();
        primary!.Overloads.Should().HaveCount(2);

        foreach (var signature in new[] { primary }.Concat(primary.Overloads))
        {
            signature!.Parameters.Should().HaveCount(2);
            signature.Parameters[0].Name.Should().Be("$this");
            signature.Parameters[0].DeclaredType!.BoundSymbol.Should().BeSameAs(stringType);
            signature.Parameters[1].Name.Should().Be("$n");
        }
    }

    [Fact]
    public void MethodGeneric_ShadowsBlockT_AndBothAreRecorded()
    {
        var result = Compile("""
            <?tyhp
            class MyClass<T> {
                public T $id;
            }
            extension MyClassOps<T> extends MyClass<T> {
                function id<T>(): T { return $this->id; }
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var ops = FindObject(result.GlobalScope!, "MyClassOps");
        var id = ops!.Members["id"] as ObjectMethodSymbol;
        ops.GenericParameters.Select(p => p.Name).Should().Equal("T");
        id!.GenericParameters.Select(p => p.Name).Should().Equal("T");
        id.GenericParameters[0].Should().NotBeSameAs(ops.GenericParameters[0]);
        id.ReturnType!.BoundSymbol.Should().BeSameAs(id.GenericParameters[0]);
    }

    [Fact]
    public void OperatorOperands_SelfIsTheBlockTarget()
    {
        var result = Compile("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOps extends Money {
                operator + (self $left, self $right): self => $left;
                operator + (int $left, self $right): self => $right;
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var money = FindObject(result.GlobalScope!, "Money");
        var intType = ((IBaseScope)result.GlobalScope!).FindChildSymbolByName("int");
        money!.ExtensionContributedOperators.Should().HaveCount(2);
        money.ExtensionContributedOperators.Should().OnlyContain(op => op.ExtensionTargetSymbol == money);

        var bothSelf = money.ExtensionContributedOperators.Single(op =>
            op.Parameters[0].DeclaredType!.BoundSymbol == money);
        bothSelf.Parameters[1].DeclaredType!.BoundSymbol.Should().BeSameAs(money);
        bothSelf.ReturnType!.BoundSymbol.Should().BeSameAs(money);

        var intSelf = money.ExtensionContributedOperators.Single(op =>
            op.Parameters[0].DeclaredType!.BoundSymbol == intType);
        intSelf.Parameters[1].DeclaredType!.BoundSymbol.Should().BeSameAs(money);
        intSelf.ReturnType!.BoundSymbol.Should().BeSameAs(money);
    }

    [Fact]
    public void UnresolvableBlockTarget_Reports3016OnTheBlockType()
    {
        var result = Compile("""
            <?tyhp
            extension Ops extends NotAType {
                fn id(): int => 0;
            }
            """);

        var ops = FindObject(result.GlobalScope!, "Ops");
        ops.Should().NotBeNull();
        var target = ops!.PendingExtensionBlockTarget;
        target.Should().NotBeNull();
        ops.ExtensionBlockTargetSymbol.Should().BeNull();

        SnippetErrors(result).Should().Contain(d =>
            d.Code == MessageCode.ExtensionOperatorTargetNotFound
            && d.Line == target!.Line
            && d.Column == target.Column);
        SnippetErrors(result).Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
    }

    [Fact]
    public void NonInstantiableOperatorTarget_Reports3025OnTheBlockType()
    {
        var result = Compile("""
            <?tyhp
            extension Ops extends void {
                operator + (self $left, int $right): int => $right;
            }
            """);

        var ops = FindObject(result.GlobalScope!, "Ops");
        var target = ops!.PendingExtensionBlockTarget;
        target.Should().NotBeNull();

        SnippetErrors(result).Should().Contain(d =>
            d.Code == MessageCode.ExtensionOperatorTargetNotInstantiable
            && d.Line == target!.Line
            && d.Column == target.Column);

        var voidBuiltin = ((IBaseScope)result.GlobalScope!).FindChildSymbolByName("void") as BuiltInTypeSymbol;
        voidBuiltin.Should().NotBeNull();
        voidBuiltin!.ExtensionContributedOperators.Should().BeEmpty();
    }

    [Fact]
    public void SelfInsideNestedAnonymousClass_IsTheAnonymousClass_NotTheBlockTarget()
    {
        // An anonymous class declared inside an extension member's body is itself an
        // ObjectDeclarationScope. `self` inside its own methods must mean that anonymous
        // class, not the enclosing extension's block target (Money).
        var result = Compile("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOps extends Money {
                function wrap(): void {
                    $x = new class {
                        public function identify(): self { return $this; }
                    };
                }
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var money = FindObject(result.GlobalScope!, "Money");
        money.Should().NotBeNull();

        var anon = FindAnonymousClass(result.GlobalScope!);
        anon.Should().NotBeNull();
        anon.Should().NotBeSameAs(money);

        var identify = anon!.Members["identify"] as ObjectMethodSymbol;
        identify.Should().NotBeNull();
        identify!.ReturnType!.BoundSymbol.Should().BeSameAs(anon);
    }

    [Fact]
    public void ClassBodyTyhpdefExtensionFn_StillBindsImplicitThisToTheOwner()
    {
        var result = Compile(
            """
            <?tyhp
            function demo(): void {}
            """,
            """
            <?tyhpdef
            class Box {
                extension fn len(): int => 0;
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var box = FindObject(result.GlobalScope!, "Box");
        box.Should().NotBeNull();
        box!.ExtensionBlockTargetSymbol.Should().BeNull();
        var synth = box.SyntheticInlineExtension;
        synth.Should().NotBeNull();
        synth!.IsExtensionTargetGroup.Should().BeFalse();

        var method = synth.Members["len"] as ObjectMethodSymbol;
        method.Should().NotBeNull();
        method!.Parameters.Should().ContainSingle();
        method.Parameters[0].Name.Should().Be("$this");
        method.Parameters[0].DeclaredType!.BoundSymbol.Should().BeSameAs(box);
        FindThis(method)!.DeclaredType!.BoundSymbol.Should().BeSameAs(box);
    }

    [Fact]
    public void AliasToString_ResolvesAndContributesTheMember()
    {
        var result = Compile("""
            <?tyhp
            type Str = string;
            extension StrOps extends Str {
                function label(): string { return "x"; }
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var stringType = ((IBaseScope)result.GlobalScope!).FindChildSymbolByName("string");
        stringType.Should().NotBeNull();
        var ops = FindObject(result.GlobalScope!, "StrOps");
        ops.Should().NotBeNull();
        ops!.ExtensionBlockTargetSymbol.Should().BeSameAs(stringType);

        var method = ResolveExtensionMethod(result.GlobalScope!, "label", stringType!);
        method.Should().NotBeNull();
        method!.Parameters[0].Name.Should().Be("$this");
        method.Parameters[0].DeclaredType!.BoundSymbol.Should().BeSameAs(FindAlias(result.GlobalScope!, "Str"));
    }

    [Fact]
    public void AliasToNullableString_ResolvesToTheStringBuiltin()
    {
        var result = Compile("""
            <?tyhp
            type MaybeStr = ?string;
            extension StrOps extends MaybeStr {
                function label(): string { return "x"; }
            }
            """);

        SnippetErrors(result).Should().BeEmpty(Dump(result));

        var stringType = ((IBaseScope)result.GlobalScope!).FindChildSymbolByName("string");
        var ops = FindObject(result.GlobalScope!, "StrOps");
        ops.Should().NotBeNull();
        ops!.ExtensionBlockTargetSymbol.Should().BeSameAs(stringType);
        ResolveExtensionMethod(result.GlobalScope!, "label", stringType!).Should().NotBeNull();
    }

    private static CompilationResult Compile(string tyhp, string? tyhpdef = null) =>
        IsolatedCompilation.ParseSnippet(tyhp, tyhpdef, skipChecking: true);

    private static IEnumerable<IDiagnostic> SnippetErrors(CompilationResult result) =>
        result.Diagnostics.Errors.Where(error =>
            error.FileName.EndsWith("snippet.tyhp", StringComparison.Ordinal)
            || error.FileName.Contains("stubs", StringComparison.Ordinal));

    private static string Dump(CompilationResult result) =>
        string.Join("; ", SnippetErrors(result).Select(error => $"{(int)error.Code} {error.FileName}:{error.Line}: {error.Message}"));

    private static ObjectMethodSymbol? ResolveExtensionMethod(GlobalScope global, string methodName, IBaseSymbol onType)
    {
        var resolver = new NameResolver(new SymbolTree(global), new DiagnosticBag());
        return resolver.ResolveExtensionMethod(methodName, onType) as ObjectMethodSymbol;
    }

    private static ObjectDeclarationSymbol FindTargetGroup(ObjectDeclarationSymbol extension)
    {
        var extensionScope = extension.ContainingScope!
            .GetAllChildScopes()
            .First(scope => ReferenceEquals(scope.DeclarationSymbol, extension));
        return extensionScope.GetAllChildScopes()
            .Select(scope => scope.DeclarationSymbol)
            .OfType<ObjectDeclarationSymbol>()
            .Single(symbol => symbol.IsExtensionTargetGroup);
    }

    private static VariableSymbol? FindThis(ObjectMethodSymbol method)
    {
        var owner = method.ContainingScope;
        if (owner == null)
        {
            return null;
        }

        foreach (var child in owner.GetAllChildScopes())
        {
            if (ReferenceEquals(child.DeclarationSymbol, method))
            {
                return child.FindChildSymbolByName("$this") as VariableSymbol;
            }
        }

        return null;
    }

    private static IBaseSymbol? FirstGenericArgument(ITypeExpression? type) =>
        GenericArguments(type).FirstOrDefault();

    private static IReadOnlyList<IBaseSymbol?> GenericArguments(ITypeExpression? type)
    {
        if (type is PhpTypeExpressionAst { TypeKind: PhpTypeKind.Simple, Types: { } wrapped })
        {
            var inner = wrapped.GetAllNotNull().OfType<ITypeExpression>().ToList();
            if (inner.Count == 1)
            {
                return GenericArguments(inner[0]);
            }
        }

        if (type != null
            && type.AstGrammarAddons.TryGetValue("typeName", out var typeName)
            && typeName is PhpTypeExpressionListAst addonArgs)
        {
            return addonArgs.GetAllNotNull().OfType<ITypeExpression>().Select(a => a.BoundSymbol).ToList();
        }

        if (type is PhpNamedTypeAst { Name: TyhpGenericIdentifierAst { GenericArguments: PhpTypeExpressionListAst nameArgs } })
        {
            return nameArgs.GetAllNotNull().OfType<ITypeExpression>().Select(a => a.BoundSymbol).ToList();
        }

        return [];
    }

    /// <summary>
    /// An anonymous class's <see cref="ObjectDeclarationSymbol"/> is registered only as a
    /// child <em>scope</em> (via <c>AddObjectDeclarationChildScope</c>), not as a child
    /// <em>symbol</em> — so, unlike <see cref="FindObject"/>, this must walk scopes and read
    /// <c>DeclarationSymbol</c> rather than <c>GetAllChildSymbols()</c>.
    /// </summary>
    private static ObjectDeclarationSymbol? FindAnonymousClass(IBaseScope scope)
    {
        if (scope.DeclarationSymbol is ObjectDeclarationSymbol { IsExtensionTargetGroup: false, IsExtension: false } obj
            && obj.Name.StartsWith("anonClass@", StringComparison.Ordinal))
        {
            return obj;
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            var found = FindAnonymousClass(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static IBaseSymbol? FindAlias(GlobalScope global, string name)
    {
        IBaseSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is TypeAliasSymbol or ObjectTypeAliasSymbol
                && string.Equals(symbol.Name, name, StringComparison.Ordinal))
            {
                found = symbol;
            }
        });
        return found;
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && !obj.IsExtensionTargetGroup
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            visit(symbol);
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            Walk(child, visit);
        }
    }
}
