using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>operator default(): self</c> and the <c>default(Class)</c> call it rewrites.
/// A class without the operator keeps the null default.
/// </summary>
[Trait("Category", "Checker")]
public class OperatorDefaultTests
{
    [Fact]
    public void Check_NoOperator_DefaultOfClass_IsNull()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass { public int $n = 1; }
            function f(): MyClass {
                return default(MyClass);
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_NoOperator_PropertyInitializer_StaysConstantNull()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass { public int $n = 1; }
            class Holder {
                public MyClass $value = default(MyClass);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_WithOperator_ReturnCompiles()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                public int $amount = 0;
                operator default(): self {
                    return new self();
                }
            }
            function f(): MyClass {
                return default(MyClass);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_WithOperator_NullCoalesceCompiles()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                public int $amount = 0;
                operator default(): self {
                    return new self();
                }
            }
            function myFunc(?MyClass $i = null): MyClass {
                return $i ?? default(MyClass);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_ReturnNotSelf_IsRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                operator default(): int {
                    return 0;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOperatorDefaultReturnNotSelf);
    }

    [Fact]
    public void Check_StaticReturn_IsRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                operator default(): static {
                    return new self();
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOperatorDefaultReturnNotSelf);
    }

    [Fact]
    public void Check_Parameters_AreRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                operator default(int $n): self {
                    return new self();
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOperatorDefaultHasParameters);
    }

    [Fact]
    public void Check_ThisInBody_IsStaticContext()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                operator default(): self {
                    return $this;
                }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerThisInStaticContext);
    }

    [Fact]
    public void Check_TwoDeclarations_AreRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                operator default(): self { return new self(); }
                operator default(): self { return new self(); }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOperatorDefaultDuplicate);
    }

    [Fact]
    public void Check_Interface_IsRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface Mine {
                operator default(): self { return new self(); }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOperatorDefaultNotAllowed);
    }

    [Fact]
    public void Check_AbstractClass_IsRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            abstract class MyClass {
                operator default(): self { return new self(); }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOperatorDefaultNotAllowed);
    }

    [Fact]
    public void Check_ReservedMethodName_IsRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                public function __TyhpDefault(): void {}
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerReservedOperatorDefaultName);
    }

    [Fact]
    public void Check_WithOperator_PropertyInitializer_IsNotConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                operator default(): self { return new self(); }
                public MyClass $value = default(MyClass);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_WithOperator_ParameterDefault_IsNotConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                operator default(): self { return new self(); }
            }
            function f(MyClass $value = default(MyClass)): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Check_WithOperator_SelfPropertyInitializer_IsNotConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                operator default(): self { return new self(); }
                public ?MyClass $value = default(self);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Theory]
    [InlineData("self")]
    [InlineData("static")]
    public void Check_NoOperator_SelfAndStatic_AreNull(string spelled)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            class MyClass {
                public function make(): MyClass { return default({{spelled}}); }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Theory]
    [InlineData("self")]
    [InlineData("static")]
    public void Check_WithOperator_SelfAndStatic_AreTheClass(string spelled)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            class MyClass {
                operator default(): self { return new self(); }
                public function make(): MyClass { return default({{spelled}}); }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_NoOperator_ParameterDefault_StaysConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass { public int $n = 1; }
            function f(mixed $value = default(MyClass)): void {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerNonConstantExpression);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_ChildDoesNotInheritOperator()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class BaseItem {
                operator default(): self { return new self(); }
            }
            class ChildItem extends BaseItem {}
            function fromChild(): ChildItem { return default(ChildItem); }
            function fromBase(): BaseItem { return default(BaseItem); }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerOperatorDefaultRecursive);
    }

    [Theory]
    [InlineData("self")]
    [InlineData("static")]
    [InlineData("parent")]
    public void Check_ChildSelfStaticAndParent_DoNotUseParentOperator(string spelled)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            class BaseItem {
                operator default(): self { return new self(); }
            }
            class ChildItem extends BaseItem {
                public function make(): BaseItem { return default({{spelled}}); }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("Parent")]
    [InlineData("PARENT")]
    public void Check_Parent_AnySpelling_IsNull_InGrandchildStaticAndClosure(string spelled)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            class BaseItem {
                operator default(): self { return new self(); }
            }
            class MidItem extends BaseItem {}
            class LeafItem extends MidItem {
                public function viaMethod(): ?BaseItem { return default({{spelled}}); }
                public static function viaStatic(): ?BaseItem { return default({{spelled}}); }
                public function viaClosure(): ?BaseItem {
                    $f = function (): ?BaseItem { return default({{spelled}}); };
                    return $f();
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Theory]
    [InlineData("parent")]
    [InlineData("Parent")]
    [InlineData("PARENT")]
    public void Check_Parent_AnySpelling_IsNotTheParentType(string spelled)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            class BaseItem {
                operator default(): self { return new self(); }
            }
            class LeafItem extends BaseItem {
                public static function viaStatic(): BaseItem { return default({{spelled}}); }
            }
            """);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerIncompatibleReturnType
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_Parent_FullyQualified_IsRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class BaseItem {
                operator default(): self { return new self(); }
            }
            class LeafItem extends BaseItem {
                public function make(): ?BaseItem { return default(\parent); }
            }
            """);

        diagnostics.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Check_Enum_DefaultParent_DoesNotCrash()
    {
        var act = () => CompileAndCheck("""
            <?tyhp
            enum Color {
                case None;
                public static function f(): mixed { return default(parent); }
            }
            """);

        act.Should().NotThrow();
    }

    [Fact]
    public void Check_EnumOperator_Compiles()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Color {
                case None;
                case Red;
                operator default(): self { return self::None; }
            }
            function f(): Color { return default(Color); }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Check_DirectRecursion_IsRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                operator default(): self { return default(self); }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOperatorDefaultRecursive);
    }

    [Fact]
    public void Check_ConstructorParameterCycle_IsRejected()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class MyClass {
                public function __construct(MyClass $other = default(MyClass)) {}
                operator default(): self { return new self(); }
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerOperatorDefaultConstructorCycle);
    }

    private static string Describe(DiagnosticBag diagnostics) =>
        string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
