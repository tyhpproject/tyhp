using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story271")]
public class StructShapeCheckerTests
{
    [Fact]
    public void NewPointWith_OnStructShapeAlias_IsAccepted()
    {
        var errors = Check("""
            <?tyhp
            type Point = struct {
                float $x;
                float $y;
            };
            function origin(): Point {
                return new Point() with [x => 0.0, y => 0.0];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NewMapper_OnCallableAlias_StaysIllegal()
    {
        var errors = Check("""
            <?tyhp
            type Mapper = callable(int $in): string;
            function demo(): void {
                mixed $x = new Mapper();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerCannotInstantiateNonClass);
    }

    [Fact]
    public void NewClockShape_OnObjectShapeAlias_StaysIllegal()
    {
        var errors = Check("""
            <?tyhp
            type ClockShape = object {
                public function now(): int;
            };
            function demo(): void {
                mixed $x = new ClockShape();
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerObjectShapeUsedAsClass);
    }

    [Fact]
    public void InlineStruct_IsLegalReturnType()
    {
        var errors = Check("""
            <?tyhp
            function origin(): struct { float $lat; float $lng; } {
                return new struct { float $lat; float $lng; } with [lat => 0.0, lng => 0.0];
            }
            function read(struct { float $lat; float $lng; } $p): float {
                return $p->lat;
            }
            function demo(): float {
                return read(origin());
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void GroupedInlineStruct_UnionNull_IsLegal()
    {
        var errors = Check("""
            <?tyhp
            function maybeOrigin(): (struct { float $lat; float $lng; }) | null {
                return null;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void StructExtends_CopiesParentFields()
    {
        var errors = Check("""
            <?tyhp
            type Parent = struct {
                int $base;
            };
            type Child = struct extends Parent {
                int $extra;
            };
            function make(): Child {
                return new Child() with [base => 1, extra => 2];
            }
            function read(Child $c): int {
                return $c->base + $c->extra;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void GenericStructExtends_WithExplicitTypeArgument_CopiesFields()
    {
        var errors = Check("""
            <?tyhp
            type Base<T> = struct {
                T $value;
            };
            type Child<T> = struct extends Base<T> {
                int $extra;
            };
            function make(): Child<string> {
                return new Child<string>() with [value => "x", extra => 2];
            }
            function read(Child<string> $c): string {
                return $c->value;
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void GenericStructAlias_AndExtendsStructBound_Match()
    {
        var errors = Check("""
            <?tyhp
            type Box<T> = struct {
                T $value;
            };
            function pin<T extends struct>(T $s): T {
                return $s;
            }
            function demo(Box<int> $box): Box<int> {
                return pin($box);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void StructShapeAlias_BindsAsNamedStruct()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            type Point = struct {
                float $x;
                float $y;
            };
            """, skipChecking: true);

        result.GlobalScope.Should().NotBeNull();
        var point = EnumerateScopes(result.GlobalScope!)
            .SelectMany(scope => scope.GetAllChildSymbols())
            .OfType<ObjectDeclarationSymbol>()
            .FirstOrDefault(symbol => symbol.IsStruct && symbol.Name == "Point");
        point.Should().NotBeNull("type Point = struct { }; must register as a named struct");
        point!.Members.Values.OfType<ObjectPropertySymbol>().Should().HaveCount(2);
    }

    [Fact]
    public void ClassMemberStructShapeAlias_BindsAsNamedStruct()
    {
        var result = IsolatedCompilation.ParseSnippet("""
            <?tyhp
            class C {
                type Point = struct {
                    float $x;
                    float $y;
                };
            }
            """, skipChecking: true);

        result.GlobalScope.Should().NotBeNull();
        var owner = EnumerateScopes(result.GlobalScope!)
            .SelectMany(scope => scope.GetAllChildSymbols())
            .OfType<ObjectDeclarationSymbol>()
            .FirstOrDefault(symbol => symbol.Name == "C" && !symbol.IsStruct);
        owner.Should().NotBeNull();
        owner!.Members.TryGetValue("Point", out var member).Should().BeTrue();
        var point = member.Should().BeOfType<ObjectDeclarationSymbol>().Subject;
        point.IsStruct.Should().BeTrue();
        point.FullyQualifiedName.Should().Be("\\C\\Point");
        point.Members.Values.OfType<ObjectPropertySymbol>().Should().HaveCount(2);
    }

    [Fact]
    public void NewClassMemberPointWith_IsAccepted()
    {
        var errors = Check("""
            <?tyhp
            class C {
                type Point = struct {
                    float $x;
                    float $y;
                };
            }
            function origin(): C\Point {
                return new C\Point() with [x => 0.0, y => 0.0];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NewSelfMemberPointWith_InsideClass_IsAccepted()
    {
        var errors = Check("""
            <?tyhp
            class C {
                type Point = struct {
                    float $x;
                    float $y;
                };
                public static function origin(): self\Point {
                    return new self\Point() with [x => 0.0, y => 0.0];
                }
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void NewTyhpdefClassMemberPointWith_IsAccepted()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            function origin(): C\Point {
                return new C\Point() with [x => 0.0, y => 0.0];
            }
            """,
            """
            <?tyhpdef
            class C {
                type Point = struct {
                    float $x;
                    float $y;
                };
            }
            """);

        var errors = result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void PlainNonGenericStructAlias_SatisfiesExtendsStructBound()
    {
        var errors = Check("""
            <?tyhp
            type Point = struct {
                int $x;
            };
            function pin<T extends struct>(T $s): T {
                return $s;
            }
            function demo(Point $p): Point {
                return pin($p);
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void BareStructIdentifier_TypeAlias_ReferencesBuiltIn()
    {
        var errors = Check("""
            <?tyhp
            type Foo = struct;
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void StructIntersection_StructuralWidth_IsSatisfiedByInlineStruct()
    {
        var errors = Check("""
            <?tyhp
            type Base = struct {
                int $base;
            };
            function demo(): (Base & struct { int $extra; }) {
                return new struct { int $base; int $extra; } with [base => 1, extra => 2];
            }
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    private static IReadOnlyList<IDiagnostic> Check(string tyhp)
    {
        var result = IsolatedCompilation.ParseSnippet(tyhp);
        return result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IEnumerable<IBaseScope> EnumerateScopes(IBaseScope root)
    {
        yield return root;
        foreach (var child in root.GetAllChildScopes())
        {
            foreach (var descendant in EnumerateScopes(child))
            {
                yield return descendant;
            }
        }
    }
}
