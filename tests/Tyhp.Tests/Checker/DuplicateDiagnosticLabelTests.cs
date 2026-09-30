using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Diagnostics")]
public class DuplicateDiagnosticLabelTests
{
    private static string DeclaredHere => Message.Localize("CLI_DiagnosticLabelDeclaredHere");

    [Fact]
    public void DuplicateFunctionParameter_LabelsFirstParameter()
    {
        var diagnostic = RequireError("""
            <?tyhp
            function demo(
                int $x,
                int $x
            ): void {}
            """, MessageCode.CheckerDuplicateParameter);

        diagnostic.Line.Should().BeGreaterThan(diagnostic.Labels[0].Span.Line);
        Convert.ToString(diagnostic.FormatParams[0]).Should().Contain("x");
    }

    [Fact]
    public void DuplicateNamedArgument_LabelsFirstArgument()
    {
        var diagnostic = RequireError("""
            <?tyhp
            function foo(int $x, int $y = 0): void {}
            function demo(): void {
                foo(
                    x: 1,
                    x: 2
                );
            }
            """, MessageCode.CheckerDuplicateNamedArgument);

        diagnostic.Line.Should().BeGreaterThan(diagnostic.Labels[0].Span.Line);
    }

    [Fact]
    public void DuplicateArrayKey_LabelsFirstPair()
    {
        var diagnostic = RequireError("""
            <?tyhp
            function demo(): void {
                mixed $a = [
                    'x' => 1,
                    'x' => 2,
                ];
            }
            """, MessageCode.CheckerDuplicateArrayKey);

        diagnostic.Line.Should().BeGreaterThan(diagnostic.Labels[0].Span.Line);
    }

    [Fact]
    public void DuplicateEnumCaseValue_LabelsFirstCase()
    {
        var diagnostic = RequireError("""
            <?tyhp
            enum Size: int {
                case S = 1;
                case M = 1;
            }
            """, MessageCode.CheckerEnumCaseDuplicateValue);

        diagnostic.Line.Should().BeGreaterThan(diagnostic.Labels[0].Span.Line);
    }

    [Fact]
    public void DuplicateCatchType_LabelsFirstCatchType()
    {
        var diagnostic = RequireWarning("""
            <?tyhp
            function demo(): void {
                try {
                    throw new \Exception();
                } catch (\Exception $e) {
                    return;
                } catch (\Exception $e2) {
                    return;
                }
            }
            """, MessageCode.CheckerDuplicateCatch);

        diagnostic.Line.Should().BeGreaterThan(diagnostic.Labels[0].Span.Line);
    }

    [Fact]
    public void DuplicateImport_LabelsFirstUse()
    {
        var diagnostic = RequireWarning("""
            <?tyhp
            namespace App\Model;

            class User {}

            use App\Model\User;
            use App\Model\User;
            """, MessageCode.CheckerDuplicateImport);

        diagnostic.Line.Should().BeGreaterThan(diagnostic.Labels[0].Span.Line);
    }

    [Fact]
    public void DuplicateTypeInComposite_LabelsFirstMember()
    {
        var diagnostic = RequireError("""
            <?tyhp
            function demo(int|int $value): void {}
            """, MessageCode.CheckerDuplicateTypeInComposite);

        diagnostic.Labels[0].Span.Line.Should().Be(diagnostic.Line);
        diagnostic.Labels[0].Span.Column.Should().BeLessThanOrEqualTo(diagnostic.Column);
    }

    [Fact]
    public void PhpVersionDuplicateDeclaration_LabelsFirstGatedFunction()
    {
        var diagnostic = RequireError("""
            <?tyhp
            #[\Tyhp\Php(">=8.2")]
            function foo(): void {}
            #[\Tyhp\Php(">=8.3")]
            function foo(): void {}
            """, MessageCode.CheckerPhpVersionDuplicateDeclaration, phpVersion: "8.3");

        diagnostic.Line.Should().BeGreaterThan(diagnostic.Labels[0].Span.Line);
    }

    private static IDiagnostic RequireError(string content, MessageCode code, string phpVersion = "8.2")
    {
        var diagnostic = Compile(content, phpVersion).Errors
            .Should()
            .Contain(d => d.Code == code)
            .Which;
        AssertDeclaredHere(diagnostic);
        return diagnostic;
    }

    private static IDiagnostic RequireWarning(string content, MessageCode code, string phpVersion = "8.2")
    {
        var diagnostic = Compile(content, phpVersion).Warnings
            .Should()
            .Contain(d => d.Code == code)
            .Which;
        AssertDeclaredHere(diagnostic);
        return diagnostic;
    }

    private static void AssertDeclaredHere(IDiagnostic diagnostic)
    {
        diagnostic.Labels.Should().ContainSingle();
        diagnostic.Labels[0].Message.Should().Be(DeclaredHere);
        diagnostic.Labels[0].Span.FileName.Should().Be(diagnostic.FileName);
        diagnostic.Message.Should().NotContain(Path.GetFileName(diagnostic.FileName) + ":");
    }

    private static DiagnosticBag Compile(string content, string phpVersion)
        => IsolatedCompilation.ParseSnippet(content, phpVersion: phpVersion).Diagnostics;
}
