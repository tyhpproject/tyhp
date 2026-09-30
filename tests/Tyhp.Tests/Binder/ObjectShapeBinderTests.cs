using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

[Trait("Category", "Binder")]
[Trait("Category", "Story27")]
public class ObjectShapeBinderTests
{
    [Fact]
    public void Bind_ClockShapeParameter_ResolvesToObjectShape()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type ClockShape = object {
                public function now(): \DateTimeImmutable;
            };
            function formatNow(ClockShape $c): string {
                return $c->now()->format('c');
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
        var alias = FindTypeAlias(global!, "ClockShape");
        alias.Should().NotBeNull();
        alias!.AliasedType.Should().BeOfType<TyhpObjectShapeAst>();
        var shape = ((TyhpTypeAliasAst)alias.DeclaringAstNode!).ObjectShape;
        shape.Should().NotBeNull();
        shape!.Members!.GetAllNotNull().OfType<PhpMethodDeclAst>()
            .Should().ContainSingle(m => m.Identifier == "now");

        var formatNow = FindFunction(global!, "formatNow");
        var bound = FindBoundTypeAlias(formatNow.Parameters[0].DeclaredType);
        bound.Should().NotBeNull();
        bound!.Name.Should().Be("ClockShape");
        bound.AliasedType.Should().BeOfType<TyhpObjectShapeAst>();
    }

    [Fact]
    public void Bind_ObjectShapePublicConst_IsKeptOnShapeAst()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type HasConst = object {
                public const int FOO = 1;
            };
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var alias = FindTypeAlias(global!, "HasConst");
        var shape = alias!.AliasedType.Should().BeOfType<TyhpObjectShapeAst>().Subject;
        shape.Members!.GetAllNotNull().OfType<PhpConstDeclListAst>()
            .Should().ContainSingle()
            .Which.GetAllNotNull().Should().ContainSingle(c => c.Identifier == "FOO");
    }

    [Fact]
    public void Bind_IntersectionShape_RetainsMembersAndNominalParent()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            interface LoggerInterface { public function info(string $m): void; }
            type TimestampedLogger = LoggerInterface & object {
                public function getLastLogAt(): \DateTimeImmutable;
            };
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedParameterType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerObjectShapeEmpty);
        var alias = FindTypeAlias(global!, "TimestampedLogger");
        alias.Should().NotBeNull();
        var typeExpr = alias!.AliasedType.Should().BeOfType<PhpTypeExpressionAst>().Subject;
        typeExpr.TypeKind.Should().Be(Tyhp.TyhpLang.Enum.PhpTypeKind.Intersection);
        typeExpr.Types!.GetAllNotNull().OfType<TyhpObjectShapeAst>().Should().ContainSingle();
        typeExpr.Types!.GetAllNotNull().OfType<PhpNamedTypeAst>().Should().ContainSingle();
        alias.DeclaringAstNode.Should().BeOfType<TyhpTypeAliasAst>()
            .Which.ObjectShape.Should().NotBeNull();
    }

    [Fact]
    public void Bind_RecursiveObjectShapeAlias_DoesNotReportCircularAlias()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type Node = object {
                public function parent(): ?Node;
            };
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderCircularTypeAlias);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedReturnType);
    }

    [Fact]
    public void Bind_NonShapeCircularAliases_StillReportsTyhp3029()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type A = B;
            type B = A;
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderCircularTypeAlias);
    }

    [Fact]
    public void Bind_MixedCycle_ShapeUnionedWithNonShape_StillReportsTyhp3029()
    {
        // `B`'s RHS never bottoms out at a shape (it's a union with `int`), so the cycle is
        // not "every participant is a shape (or a union/intersection/generic wrapping one)".
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type A = B;
            type B = A|int;
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.BinderCircularTypeAlias);
    }

    [Fact]
    public void Bind_RecursiveShapeAlias_ReachedThroughBareRename_DoesNotReportCircularAlias()
    {
        // `Foo` is a bare rename of the shape alias `Box`, not a literal `object { }` itself —
        // the cycle is still exempt because every participant ultimately resolves to a shape.
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type Box = object {
                public function get(): Foo;
            };
            type Foo = Box;
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderCircularTypeAlias);
    }

    [Fact]
    public void Bind_RecursiveShapeAlias_ReachedThroughGenericInstantiation_DoesNotReportCircularAlias()
    {
        // `Foo`'s RHS is `Container<Foo>` — a generic instantiation of a shape-producing alias,
        // not a literal `object { }` on `Foo` itself. Still exempt per the plan's
        // "union/intersection/generic wrapping one" rule.
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type Container<T> = object {
                public function get(): T;
            };
            type Foo = Container<Foo>;
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderCircularTypeAlias);
    }

    [Fact]
    public void Bind_EmptyObjectShape_Reports4349()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type Hollow = object {};
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeEmpty);
    }

    [Fact]
    public void Bind_NonPublicObjectShapeMember_Reports4350()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type Hidden = object {
                private function secret(): void;
            };
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeNonPublicMember);
    }

    [Fact]
    public void Bind_PrivateConstObjectShapeMember_Reports4350()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type Hidden = object {
                private const int SECRET = 1;
            };
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeNonPublicMember);
    }

    [Fact]
    public void Bind_ProtectedUntypedConstObjectShapeMember_Reports4350()
    {
        var (_, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type Hidden = object {
                protected const SECRET = 1;
            };
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeNonPublicMember);
    }

    [Fact]
    public void Check_NewObjectShapeAlias_Reports4347()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type ClockShape = object {
                public function now(): \DateTimeImmutable;
            };
            function demo(): void {
                mixed $x = new ClockShape();
            }
            """);

        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeUsedAsClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_ExtendsObjectShapeAlias_Reports4347()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type ClockShape = object {
                public function now(): \DateTimeImmutable;
            };
            class X extends ClockShape {}
            """);

        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeUsedAsClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedExtendsType);
    }

    [Fact]
    public void Check_ImplementsObjectShapeAlias_Reports4347()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type ClockShape = object {
                public function now(): \DateTimeImmutable;
            };
            class X implements ClockShape {}
            """);

        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeUsedAsClass);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderUnresolvedImplementsType);
    }

    [Fact]
    public void Check_StaticCallAndClassConstOnObjectShape_Reports4347()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type ClockShape = object {
                public function now(): \DateTimeImmutable;
            };
            function demo(): void {
                ClockShape::foo();
                mixed $n = ClockShape::class;
            }
            """);

        result.Diagnostics.Errors.Count(d => d.Code == MessageCode.CheckerObjectShapeUsedAsClass)
            .Should().BeGreaterThanOrEqualTo(2);
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Bind_ConstructSignature_IsKeptOnShapeAst()
    {
        var (global, diagnostics) = BinderTestHelper.BindContent("""
            <?tyhp
            type Named = object {
                public function __construct(string $name): void;
                public function ping(): void;
            };
            """);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        var alias = FindTypeAlias(global!, "Named");
        var shape = alias!.AliasedType.Should().BeOfType<TyhpObjectShapeAst>().Subject;
        shape.Members!.GetAllNotNull().OfType<PhpMethodDeclAst>()
            .Should().Contain(m => m.Identifier == "__construct");
    }

    private static TypeAliasSymbol? FindTypeAlias(GlobalScope global, string name)
    {
        TypeAliasSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found is null
                && symbol is TypeAliasSymbol alias
                && string.Equals(alias.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = alias;
            }
        });
        return found;
    }

    private static FunctionDeclarationSymbol FindFunction(GlobalScope global, string name)
    {
        FunctionDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found is null
                && symbol is FunctionDeclarationSymbol fn
                && string.Equals(fn.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = fn;
            }
        });
        found.Should().NotBeNull($"expected to find function '{name}'");
        return found!;
    }

    private static TypeAliasSymbol? FindBoundTypeAlias(IBase2Ast? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.BoundSymbol is TypeAliasSymbol alias)
        {
            return alias;
        }

        foreach (var child in node.AstChildren)
        {
            var nested = FindBoundTypeAlias(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
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
