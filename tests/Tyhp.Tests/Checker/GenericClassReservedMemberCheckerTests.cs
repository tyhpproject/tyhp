using Tyhp.Domain.Exceptions;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Generated <c>new()</c> and <c>type()</c> reserve those names on a generic class
/// and on its ancestors.
/// </summary>
[Trait("Category", "Checker")]
public class GenericClassReservedMemberCheckerTests
{
    [Fact]
    public void GenericClassDeclaringType_ReportsTyhp4406()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Box<T> {
                public function type(): string {
                    return "no";
                }
            }
            """,
            phpVersion: "8.4");

        var error = result.Diagnostics.Errors
            .First(d => d.Code == MessageCode.CheckerGenericClassReservedMemberName);
        error.Message.Should().Contain("type");
        error.Message.Should().Contain("Box");
    }

    [Fact]
    public void GenericClassWhoseParentDeclaresNew_ReportsTyhp4406()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Base {
                public function new(): string {
                    return "base";
                }
            }

            class Box<T> extends Base {
                public function id(T $value): T {
                    return $value;
                }
            }
            """,
            phpVersion: "8.4");

        var error = result.Diagnostics.Errors
            .First(d => d.Code == MessageCode.CheckerGenericClassReservedMemberName);
        error.Message.Should().Contain("new");
        error.Message.Should().Contain("Box");
        error.Message.Should().Contain("Base");
    }

    [Fact]
    public void GenericClassDeclaringTypeWithDifferentCase_ReportsTyhp4406()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Box<T> {
                public function Type(): string {
                    return "no";
                }
            }
            """,
            phpVersion: "8.4");

        var error = result.Diagnostics.Errors
            .First(d => d.Code == MessageCode.CheckerGenericClassReservedMemberName);
        error.Message.Should().Contain("Type");
    }

    [Fact]
    public void GenericClassDeclaringNew_ReportsTyhp4406()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Box<T> {
                public static function new(): string {
                    return "no";
                }
            }
            """,
            phpVersion: "8.4");

        var error = result.Diagnostics.Errors
            .First(d => d.Code == MessageCode.CheckerGenericClassReservedMemberName);
        error.Message.Should().Contain("new");
        error.Message.Should().Contain("Box");
    }

    [Fact]
    public void GenericClassWhoseParentDeclaresTypeWithDifferentCase_ReportsTyhp4406()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Base {
                public function TYPE(): string {
                    return "base";
                }
            }

            class Middle extends Base {}

            class Box<T> extends Middle {
                public function id(T $value): T {
                    return $value;
                }
            }
            """,
            phpVersion: "8.4");

        var error = result.Diagnostics.Errors
            .First(d => d.Code == MessageCode.CheckerGenericClassReservedMemberName);
        error.Message.Should().Contain("Box");
        error.Message.Should().Contain("Base");
    }

    [Fact]
    public void NonGenericClassWhoseParentIsAlsoNonGenericAndDeclaresType_DoesNotReportTyhp4406()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Base {
                public function type(): string {
                    return "base";
                }
            }

            class Child extends Base {}
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().NotContain(MessageCode.CheckerGenericClassReservedMemberName);
    }

    [Fact]
    public void NonGenericClassDeclaringNew_DoesNotReportTyhp4406()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            class Base {
                public function new(): string {
                    return "base";
                }
            }
            """,
            phpVersion: "8.4");

        result.Diagnostics.Errors.Select(d => d.Code)
            .Should().NotContain(MessageCode.CheckerGenericClassReservedMemberName);
    }
}
