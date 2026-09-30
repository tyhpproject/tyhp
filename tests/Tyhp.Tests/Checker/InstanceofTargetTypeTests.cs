using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// FOUND_BUGS #55 — <c>is</c> / <c>instanceof</c> (and <c>typeof</c>) must reject an
/// undeclared type-name target instead of silently accepting it.
/// </summary>
[Trait("Category", "Checker")]
public class InstanceofTargetTypeTests
{
    [Fact]
    public void Check_IsUndeclaredName_InClass_Reports3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class A {
                function demo(mixed $x): bool {
                    return $x is TotallyBogusUnresolvedName;
                }
            }
            """);

        errors.Should().Contain(
            e => NamesUnresolvedSymbol(e, "TotallyBogusUnresolvedName"),
            $"expected TYHP3003: {Describe(errors)}");
    }

    [Fact]
    public void Check_InstanceofUndeclaredName_Reports3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class A {
                function demo(mixed $x): bool {
                    return $x instanceof TotallyBogusUnresolvedName;
                }
            }
            """);

        errors.Should().Contain(
            e => NamesUnresolvedSymbol(e, "TotallyBogusUnresolvedName"),
            $"expected TYHP3003: {Describe(errors)}");
    }

    [Fact]
    public void Check_IsUndeclaredName_InFreeFunction_Reports3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): bool {
                return $x is TotallyBogusUnresolvedName;
            }
            """);

        errors.Should().Contain(
            e => NamesUnresolvedSymbol(e, "TotallyBogusUnresolvedName"),
            $"expected TYHP3003: {Describe(errors)}");
    }

    [Fact]
    public void Check_IsNullableUndeclaredName_Reports3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): bool {
                return $x is ?TotallyBogusUnresolvedName;
            }
            """);

        errors.Should().Contain(
            e => NamesUnresolvedSymbol(e, "TotallyBogusUnresolvedName"),
            $"expected TYHP3003: {Describe(errors)}");
    }

    [Fact]
    public void Check_TypeofUndeclaredName_Reports3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(): mixed {
                return typeof(TotallyBogusUnresolvedName);
            }
            """);

        errors.Should().Contain(
            e => NamesUnresolvedSymbol(e, "TotallyBogusUnresolvedName"),
            $"expected TYHP3003: {Describe(errors)}");
    }

    [Fact]
    public void Check_IsDeclaredClass_No3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class A {}
            function demo(mixed $x): bool {
                return $x is A;
            }
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_IsInt_No3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): bool {
                return $x is int;
            }
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_InstanceofDynamicVariable_No3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x, string $className): bool {
                return $x instanceof $className;
            }
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_IsSelf_InClass_NoRelativeOrUnresolvedError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class A {
                function demo(mixed $x): bool {
                    return $x is self;
                }
            }
            """);

        errors.Should().NotContain(e =>
            e.Code == MessageCode.BinderSymbolNotFound
            || e.Code == MessageCode.CheckerRelativeTypeOutsideClass);
    }

    [Fact]
    public void Check_IsSelf_InFreeFunction_Reports4064()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(mixed $x): bool {
                return $x is self;
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerRelativeTypeOutsideClass);
        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_IsParent_WithoutSuperclass_Reports4065()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class A {
                function demo(mixed $x): bool {
                    return $x is parent;
                }
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerParentWithoutParent);
    }

    [Fact]
    public void Check_IsParent_WithSuperclass_No4065()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Base {}
            class A extends Base {
                function demo(mixed $x): bool {
                    return $x is parent;
                }
            }
            """);

        errors.Should().NotContain(e =>
            e.Code == MessageCode.CheckerParentWithoutParent
            || e.Code == MessageCode.CheckerRelativeTypeOutsideClass
            || e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_IsStatic_InClass_NoError()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class A {
                function demo(mixed $x): bool {
                    return $x is static;
                }
            }
            """);

        errors.Should().NotContain(e =>
            e.Code == MessageCode.BinderSymbolNotFound
            || e.Code == MessageCode.CheckerRelativeTypeOutsideClass);
    }

    [Fact]
    public void Check_IsGenericParameter_No3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            final class Box<TReturn extends void|mixed = void> {
                public function matches(mixed $value): bool {
                    return $value is TReturn;
                }
            }
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_IsSelfAlias_InClass_No3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class A {
                public type NameType = string;
                function demo(mixed $x): bool {
                    return $x is self\NameType;
                }
            }
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_InstanceofSelfAlias_InClass_No3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class A {
                public type NameType = string;
                function demo(mixed $x): bool {
                    return $x instanceof self\NameType;
                }
            }
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_IsClassQualifiedAlias_No3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class A {
                public type NameType = string;
            }
            function demo(mixed $x): bool {
                return $x is A\NameType;
            }
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_IsParentAlias_No3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class Base {
                public type IdType = int;
            }
            class Child extends Base {
                function demo(mixed $x): bool {
                    return $x is parent\IdType;
                }
            }
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_IsStaticAlias_InClass_No3003()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            class A {
                public type NameType = string;
                function demo(mixed $x): bool {
                    return $x is static\NameType;
                }
            }
            """);

        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    [Fact]
    public void Check_TypeofSelf_InFreeFunction_Reports4064()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function demo(): mixed {
                return typeof(self);
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerRelativeTypeOutsideClass);
        errors.Should().NotContain(e => e.Code == MessageCode.BinderSymbolNotFound);
    }

    private static bool NamesUnresolvedSymbol(IDiagnostic diagnostic, string name) =>
        diagnostic.Code == MessageCode.BinderSymbolNotFound
        && diagnostic.FormatParams.Length > 0
        && string.Equals(diagnostic.FormatParams[0]?.ToString(), name, StringComparison.Ordinal);

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);

            return result.Diagnostics.Errors
                .Where(e => e.FileName is not null
                    && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
