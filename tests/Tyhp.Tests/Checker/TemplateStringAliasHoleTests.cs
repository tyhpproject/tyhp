using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class TemplateStringAliasHoleTests
{
    [Fact]
    public void LiteralUnionAliasHole_AcceptsMatchingValues_RejectsOthers()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiMethod = 'GET'|'POST';
            type Route = "${ApiMethod} /${string}";

            function literal(): Route { return 'GET /users'; }
            function interpolated(ApiMethod $m, string $path): Route { return "{$m} /{$path}"; }
            function wrongLiteral(): Route { return 'PUT /users'; }
            function wrongHole(string $m, string $path): Route { return "{$m} /{$path}"; }
            """);

        errors.Should().HaveCount(2, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
        errors.Should().Contain(e => e.Message.Contains("'PUT /users'"));
    }

    [Fact]
    public void TemplateAliasHoles_ComposeNamedTemplates()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiMethod = "${'GET'|'POST'}";
            type ApiPath = "api/${string}/items";
            type Endpoint = "${ApiMethod} ${ApiPath}";

            function literal(): Endpoint { return 'POST api/orders/items'; }
            function fromParts(ApiMethod $m, ApiPath $p): Endpoint { return "{$m} {$p}"; }
            function spelledOut(ApiMethod $m, string $segment): Endpoint { return "{$m} api/{$segment}/items"; }
            function wrongMethod(): Endpoint { return 'PUT api/orders/items'; }
            function wrongPath(ApiMethod $m, string $segment): Endpoint { return "{$m} api/{$segment}"; }
            function plainString(string $m, ApiPath $p): Endpoint { return "{$m} {$p}"; }
            """);

        errors.Should().HaveCount(3, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void AliasHole_KeepsAliasNameInTypeDisplay()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiMethod = 'GET'|'POST';
            type Route = "${ApiMethod} /${string}";

            function f(): Route { return 'PUT /x'; }
            """);

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Message.Should().Contain("\"${ApiMethod} /${string}\"");
    }

    [Fact]
    public void AliasHoles_ResolveThroughImportsAndQualifiedNames()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            namespace App\Http {
                type Method = 'GET'|'POST';
            }

            namespace App {
                type Relative = "${Http\Method}.";

                function relativeOk(): Relative { return 'GET.'; }
                function relativeBad(): Relative { return 'PUT.'; }
            }

            namespace App\Web {
                use App\Http\Method;

                type Route = "${Method} /${string}";
                type Qualified = "${\App\Http\Method}!";

                function importedOk(): Route { return 'GET /x'; }
                function importedBad(): Route { return 'PUT /x'; }
                function qualifiedOk(): Qualified { return 'POST!'; }
                function qualifiedBad(): Qualified { return 'PUT!'; }
            }

            namespace Other {
                use App\Web\Route;

                function elsewhereOk(): Route { return 'POST /y'; }
                function elsewhereBad(): Route { return 'PUT /y'; }
            }
            """);

        errors.Should().HaveCount(4, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void ClassLevelAliasHoles_Resolve()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            final class Panel {
                public type Status = 'on'|'off';

                public function label(): "${self\Status}!" { return 'on!'; }
                public function wrong(): "${self\Status}!" { return 'dim!'; }
            }

            function outside(): "${Panel\Status}." { return 'off.'; }
            function outsideWrong(): "${Panel\Status}." { return 'dim.'; }
            """);

        errors.Should().HaveCount(2, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void GenericAliasHoles_SubstituteTypeArguments()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type Tagged<T> = "<${T}>";
            type Item = "${Tagged<'a'|'b'>}!";

            function direct(): Tagged<'x'> { return '<x>'; }
            function directWrong(): Tagged<'x'> { return '<y>'; }
            function nested(): Item { return '<b>!'; }
            function nestedWrong(): Item { return '<c>!'; }
            """);

        errors.Should().HaveCount(2, MixedCheckSnippet.Format(errors));
        errors.Should().OnlyContain(e => e.Code == MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void TyhpdefAliasHoles_Resolve()
    {
        var errors = MixedCheckSnippet.Errors(
            """
            <?tyhp
            function ok(): \Lib\Route { return 'GET /x'; }
            function wrong(): \Lib\Route { return 'PUT /x'; }
            """,
            """
            <?tyhpdef
            namespace Lib {
                type Method = 'GET'|'POST';
                type Route = "${Method} /${string}";
            }
            """);

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Code.Should().Be(MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void UnknownHoleName_ReportsSymbolNotFound()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type ApiMethod = 'GET'|'POST';
            type Route = "${ApiMethd} /${string}";
            """);

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Code.Should().Be(MessageCode.BinderSymbolNotFound);
        errors[0].Message.Should().Contain("ApiMethd");
    }

    [Fact]
    public void UnknownHoleName_InInlineReturnType_ReportsSymbolNotFound()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            function f(): "${Missing}!" { return 'x!'; }
            """);

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Code.Should().Be(MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void SelfReferencingAliasHole_ReportsCircularAlias()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type Loop = "${Loop}x";

            function f(): Loop { return 'x'; }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.BinderCircularTypeAlias, MixedCheckSnippet.Format(errors));
    }

    [Fact]
    public void BuiltinHoles_StillResolve()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type Label = "${string}: ${'on'|'off'}";

            function ok(string $name): Label { return "{$name}: on"; }
            function wrong(string $name): Label { return "{$name}: dim"; }
            """);

        errors.Should().ContainSingle(MixedCheckSnippet.Format(errors));
        errors[0].Code.Should().Be(MessageCode.CheckerIncompatibleReturnType);
    }

    [Fact]
    public void GenericParameterHole_InGenericAlias_IsNotReportedUnknown()
    {
        var errors = MixedCheckSnippet.Errors("""
            <?tyhp
            type Tagged<T> = "<${T}>";
            """);

        errors.Should().BeEmpty(MixedCheckSnippet.Format(errors));
    }
}
