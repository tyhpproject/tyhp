using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class Phase5RuleTests
{
    [Fact]
    public void Check_VariableInferenceFromInitializer_InfersType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $count = 42;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_UntypedVariableDeclarationWithoutInitializer_IsSyntaxError()
    {
        // Tyhp requires `type $var` or `$var = expr` inference; bare `$var;` is an expression statement.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $name;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NullToNonNullableAssignment_ReportsTypeMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $value = null;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_FunctionParameterWithoutType_ReportsTypeRequired()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo($value): void {}
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariableTypeRequired);
    }

    [Fact]
    public void Check_FunctionWithoutReturnType_ReportsTypeRequired()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo() {
                return;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVariableTypeRequired);
    }

    [Fact]
    public void Check_InstanceofNarrowing_AllowsMemberAccessInTrueBranch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                public function bar(): void {}
            }

            function demo(mixed $value): void {
                if ($value instanceof Foo) {
                    $value->bar();
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMemberNotAccessible);
    }

    [Fact]
    public void Check_InstanceofParameterizedSelf_NarrowsToGenericInstantiation()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            final class Box<T> {
                public T $value;
                public function take(mixed $other): ?T {
                    if ($other instanceof self<T>) {
                        return $other->value;
                    }
                    return null;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_InstanceofBareStatic_NarrowsAndSatisfiesStaticReturn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public function adopt(mixed $other): static {
                    if ($other instanceof static) {
                        return $other;
                    }
                    return $this;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NullCheckNarrowing_AllowsNonNullUse()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(?string $value): void {
                if ($value !== null) {
                    string $copy = $value;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerVariablePossiblyNull);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_PossiblyNullAssignedToNonNullable_ReportsTypeMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                ?string $value = 'hello';
                string $copy = $value;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CallableVoidInParameterPosition_ReportsVoidInNonReturnPosition()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                callable<void, string> $fn;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVoidInNonReturnPosition);
    }

    [Fact]
    public void Check_ArrayVoidTypeArgument_ReportsVoidInNonReturnPosition()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                array<void> $items;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerVoidInNonReturnPosition);
    }

    [Fact]
    public void Check_ClosureParameterInference_FromCallableType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public int $age;
            }

            function apply(callable<User, bool> $predicate): void {
                $predicate(new User());
            }

            function demo(): void {
                apply(fn ($u) => $u->age > 18);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerClosureParameterTypeRequired);
    }

    [Fact]
    public void Check_ClosureWithoutContext_ReportsParameterTypeRequired()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                mixed $fn = fn ($u) => $u;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerClosureParameterTypeRequired);
    }

    [Fact]
    public void Check_StructPropertyNonNullableWithoutDefault_IsAllowed()
    {
        // Required at construction via `new Point() with [x => …]`; declaration itself is fine.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct Point {
                int $x;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerStructPropertyRequired);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerStructRequiredPropertyNotSet);
    }

    [Fact]
    public void Check_NonNullableUtilityType_ResolvesWithoutError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                \Tyhp\NonNullable<?string> $type;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUtilityTypeInvalidArgument);
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };
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
