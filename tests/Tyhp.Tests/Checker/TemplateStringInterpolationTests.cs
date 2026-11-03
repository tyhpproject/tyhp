using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class TemplateStringInterpolationTests
{
    [Fact]
    public void Interpolation_MatchingStringHole_IsAssignableToTemplateReturn()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiPath = "api/${string}/items";

            function f(string $segment): ApiPath {
                return "api/{$segment}/items";
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_DifferentLiteralText_ReportsIncompatibleReturn()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiPath = "api/${string}/items";

            function f(string $segment): ApiPath {
                return "api/{$segment}/users";
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Interpolation_AssignedToLocal_KeepsPatternForReturn()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiPath = "api/${string}/items";

            function f(string $segment): ApiPath {
                $path = "api/{$segment}/items";
                return $path;
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_InferredLocal_AcceptsOtherStrings()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function f(string $segment): string {
                $path = "api/{$segment}/items";
                $path = 'other';
                return $path;
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_ReassignedLocal_DoesNotKeepPattern()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiPath = "api/${string}/items";

            function f(string $segment, string $other): ApiPath {
                $path = "api/{$segment}/items";
                $path = $other;
                return $path;
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Interpolation_StringHoleAbsorbsSeveralHolesAndText()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiPath = "api/${string}/items";

            function f(string $a, string $b): ApiPath {
                return "api/{$a}/{$b}/items";
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_AssignableToBareStringTemplate()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type AnyString = "${string}";

            function f(string $id): AnyString {
                return "user-{$id}";
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_LiteralUnionHole_AcceptsMatchingHoleType()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiMethod = "${'GET'|'POST'}";
            type Route = "${'GET'|'POST'} /${string}";

            function f(ApiMethod $method, string $path): Route {
                return "{$method} /{$path}";
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_DocumentedExamples_TypeCheck()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiMethod = 'GET'|'POST';
            type ApiPath = "api/${string}/items";
            type UserRoute = "${ApiMethod} /users/${string}";
            type Endpoint = "${ApiMethod} ${ApiPath}";

            function itemsPath(string $segment): ApiPath {
                return "api/{$segment}/items";
            }

            function routeForUser(ApiMethod $method, int $id): UserRoute {
                return "{$method} /users/{$id}";
            }

            function itemsEndpoint(ApiMethod $method, string $segment): Endpoint {
                return "{$method} api/{$segment}/items";
            }

            function broken(string $segment): ApiPath {
                return "api/{$segment}/users";
            }

            function wrongMethod(string $method, int $id): UserRoute {
                return "{$method} /users/{$id}";
            }
            """);

        errors.Should().HaveCount(2, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Interpolation_StringIntoLiteralUnionHole_IsRejected()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type Route = "${'GET'|'POST'} /${string}";

            function f(string $method, string $path): Route {
                return "{$method} /{$path}";
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void Interpolation_LiteralHoleValue_MatchesLiteralHoleType()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type Route = "${'GET'|'POST'} /${string}";

            function f(string $path): Route {
                $method = 'GET';
                return "{$method} /{$path}";
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_IntHole_MatchesStringHole()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ItemKey = "item-${string}";

            function f(int $id): ItemKey {
                return "item-{$id}";
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_TemplateTypedHole_IsSplicedIntoPattern()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiPath = "api/${string}/items";
            type PagedPath = "api/${string}/items?page=${string}";

            function f(ApiPath $path, int $page): PagedPath {
                return "{$path}?page={$page}";
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_TemplateTypedTargetHole_MatchesRunOfTextAndHoles()
    {
        var result = IsolatedCompilation.ParseSnippet("<?tyhp\nfunction demo(): void {}\n", (string?)null, skipChecking: true);
        var globalScope = result.GlobalScope!;
        var symbolTree = new SymbolTree(globalScope);

        var apiPath = new TemplateStringCheckedType(TemplateStringPatternReader.CreateFromSegments(
            [Lit("api/"), Hole(CheckedTypes.String), Lit("/items")], "\"api/${string}/items\""));
        var paged = new TemplateStringCheckedType(TemplateStringPatternReader.CreateFromSegments(
            [Hole(apiPath), Lit("?q")], "\"${ApiPath}?q\""));

        TypeComparer.IsAssignableTo(Interpolation(Lit("api/"), Hole(CheckedTypes.String), Lit("/items?q")),
            paged, symbolTree, globalScope).Should().BeTrue();
        TypeComparer.IsAssignableTo(Interpolation(Hole(apiPath), Lit("?q")),
            paged, symbolTree, globalScope).Should().BeTrue();
        TypeComparer.IsAssignableTo(Interpolation(Lit("api/"), Hole(CheckedTypes.String), Lit("?q")),
            paged, symbolTree, globalScope).Should().BeFalse();
    }

    private static TemplateStringSegment Lit(string text) => new TemplateStringSegment.LiteralSegment(text);

    private static TemplateStringSegment Hole(ICheckedType type) =>
        new TemplateStringSegment.HoleSegment(type, TemplateStringQuantifier.ExactlyOnce);

    private static ICheckedType Interpolation(params TemplateStringSegment[] segments) =>
        new SimpleCheckedType(new BuiltInTypeSymbol("string"))
        {
            InterpolationPattern = TemplateStringPatternReader.CreateFromSegments(segments, "interpolation"),
        };

    [Fact]
    public void Interpolation_AsArgument_MatchesTemplateParameter()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiPath = "api/${string}/items";

            function load(ApiPath $path): void {}

            function f(string $segment): void {
                load("api/{$segment}/items");
                load("api/{$segment}");
            }
            """);

        errors.Should().ContainSingle(e => e.Code == MessageCode.CheckerIncompatibleArgumentType,
            MixedCheckSnippet.Format(errors));
        errors.Should().HaveCount(1, MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_TemplateTypedHole_IsStringable()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiPath = "api/${string}/items";

            function f(ApiPath $path): string {
                return "GET {$path}";
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void Interpolation_StaysPlainStringForOtherChecks()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function f(string $segment): int {
                $path = "api/{$segment}/items";
                string $copy = $path;
                return \strlen($copy) + \strlen("x{$segment}");
            }
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }
}
