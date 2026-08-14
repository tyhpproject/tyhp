using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class Phase08_5RuleTests
{
    [Fact]
    public void SymbolNameTypes_AreRegisteredInGlobalScope()
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
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, "<?tyhp\nfunction demo(): void {}\n");

        try
        {
            var result = compilationService.ParseFiles([filePath], options);
            var symbol = ((Tyhp.TyhpLang.Binder.Scopes.Interfaces.IBaseScope)result.GlobalScope!)
                .FindChildSymbolByName("__ClassName");
            symbol.Should().BeOfType<Tyhp.TyhpLang.Binder.Symbols.BuiltInUtilityTypeSymbol>();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void VerifyLiteral_UnknownClass_ReturnsFalse()
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
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, "<?tyhp\nfunction demo(): void {}\n");

        try
        {
            var result = compilationService.ParseFiles([filePath], options);
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var state = new CheckerState();
            var target = SymbolNameTypeHelper.MakeSymbolNameType(
                Tyhp.TyhpLang.Enum.UtilityBehavior.ClassName, result.GlobalScope!);
            SymbolNameTypeHelper.IsSymbolNameType(target).Should().BeTrue();
            SymbolNameExistenceVerifier.VerifyLiteral(
                "MissingClass", target, state, symbolTree, result.GlobalScope!).Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Check_KnownClassLiteralToClassName_Succeeds()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}

            function demo(): void {
                __ClassName $cls = 'User';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_UnknownClassLiteralToClassName_ReportsSymbolNotFound()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __ClassName $cls = 'MissingClass';
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_FunctionExistsNarrowing_AllowsAssignmentInTrueBranch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(): void {}

            function demo(): void {
                string $fn = 'greet';
                if (\function_exists($fn)) {
                    __FunctionName $typed = $fn;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_ClassExistsNarrowing_AllowsAssignmentInTrueBranch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}

            function demo(): void {
                string $name = 'User';
                if (\class_exists($name)) {
                    __ClassName $typed = $name;
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_BareClassName_EqualsClassNameObject_MutuallyAssignable()
    {
        // Story 08.5 / CHECKER_GAPS P0 #3: bare `__ClassName` ≡ `__ClassName<object>`.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}

            function demo(__ClassName $bare, __ClassName<object> $obj): void {
                __ClassName<object> $a = $bare;
                __ClassName $b = $obj;
            }

            function demoIface(__InterfaceName $bare, __InterfaceName<object> $obj): void {
                __InterfaceName<object> $a = $bare;
                __InterfaceName $b = $obj;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void MakeSymbolNameType_BareClassName_IsGenericWithObject()
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
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, "<?tyhp\nfunction demo(): void {}\n");

        try
        {
            var result = compilationService.ParseFiles([filePath], options);
            var bare = SymbolNameTypeHelper.MakeSymbolNameType(
                Tyhp.TyhpLang.Enum.UtilityBehavior.ClassName, result.GlobalScope!);
            var objectSym = ((Tyhp.TyhpLang.Binder.Scopes.Interfaces.IBaseScope)result.GlobalScope!)
                .FindChildSymbolByName("object")!;
            var withObject = SymbolNameTypeHelper.MakeSymbolNameType(
                Tyhp.TyhpLang.Enum.UtilityBehavior.ClassName,
                result.GlobalScope!,
                [CheckedTypes.FromSymbol(objectSym)]);

            bare.Should().BeOfType<GenericCheckedType>();
            TypeComparer.AreTypesEqual(bare, withObject).Should().BeTrue();
            SymbolNameTypeHelper.GetFullErasure(bare, result.GlobalScope!)
                .Should().Be(CheckedTypes.String);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Check_AnonymousStringToClassName_ReportsTypeMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(string $name): void {
                __ClassName $cls = $name;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_ClassNameAssignableToString_Succeeds()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}

            function demo(): void {
                __ClassName $cls = 'User';
                string $plain = $cls;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_KnownFunctionLiteralToFunctionName_Succeeds()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function myStrlen(): int { return 0; }

            function demo(): void {
                __FunctionName $fn = 'myStrlen';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NameofClass_ReturnsClassNameType()
    {
        var (checker, _, _, diagnostics) = CompileForChecker("""
            <?tyhp
            class User {}

            function demo(): void {
                $name = nameof(User);
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
        var nameofType = GetInferredType(checker, "nameof");
        SymbolNameTypeHelper.TryGetBehavior(nameofType, out var behavior).Should().BeTrue();
        behavior.Should().Be(Tyhp.TyhpLang.Enum.UtilityBehavior.ClassName);
    }

    [Fact]
    public void Check_NameofFunction_ReturnsFunctionNameType()
    {
        var (checker, _, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function greet(): void {}

            function demo(): void {
                $name = nameof(greet);
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
        var nameofType = GetInferredType(checker, "nameof");
        SymbolNameTypeHelper.TryGetBehavior(nameofType, out var behavior).Should().BeTrue();
        behavior.Should().Be(Tyhp.TyhpLang.Enum.UtilityBehavior.FunctionName);
    }

    [Fact]
    public void Check_NameofVariable_ReturnsTypedVarName()
    {
        var (checker, _, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(): void {
                User $user = new User();
                $name = nameof($user);
            }

            class User {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var nameofType = GetInferredType(checker, "nameof");
        SymbolNameTypeHelper.TryGetBehavior(nameofType, out var behavior).Should().BeTrue();
        behavior.Should().Be(Tyhp.TyhpLang.Enum.UtilityBehavior.TypedVarName);
    }

    [Fact]
    public void Check_NameofClassAssignableToClassName()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}

            function demo(): void {
                __ClassName $cls = nameof(User);
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NameofClass_BrandsAsClassNameOfType()
    {
        // CHECKER_GAPS P0 #5: nameof(TypeName) parity with TypeName::class → __ClassName<ThatType>.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}
            interface IFace {}

            function demo(): void {
                __ClassName<User> $cls = nameof(User);
                __InterfaceName<IFace> $iface = nameof(IFace);
                __ClassName<object> $wide = nameof(User);
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_ClassColonClass_BrandsAsClassNameOfType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}

            function demo(User $u): void {
                __ClassName<User> $fromName = User::class;
                __ClassName<User> $fromObj = $u::class;
                __ClassName $bare = User::class;
                string $erased = User::class;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_LiteralToParametricClassName_MustNameThatType()
    {
        var ok = CompileAndCheck("""
            <?tyhp
            class User {}
            class Other {}

            function demo(): void {
                __ClassName<User> $u = 'User';
                __ClassName<object> $any = 'Other';
            }
            """);
        ok.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        ok.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);

        var bad = CompileAndCheck("""
            <?tyhp
            class User {}
            class Other {}

            function demo(): void {
                __ClassName<User> $u = 'Other';
            }
            """);
        bad.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerSymbolNameNotFound
            || d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_ParametricClassName_InvariantBetweenDistinctTypeArgs()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}
            class Other {}

            function demo(__ClassName<User> $u): void {
                __ClassName<Other> $o = $u;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_ParametricClassName_WidensToClassNameObject()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}

            function demo(__ClassName<User> $u): void {
                __ClassName<object> $wide = $u;
                __ClassName $bare = $u;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_ClassNameObject_DoesNotNarrowToParametric()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {}

            function demo(__ClassName<object> $wide): void {
                __ClassName<User> $u = $wide;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CompatibleTypeName_AcceptsExactClassNameBrand()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}

            function demo(__ClassName<Animal> $a): void {
                __CompatibleTypeName<Animal> $c = $a;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CompatibleTypeName_AcceptsSubclassClassNameBrand()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}
            class Dog extends Animal {}

            function demo(__ClassName<Dog> $d): void {
                __CompatibleTypeName<Animal> $c = $d;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CompatibleTypeName_CovariantBetweenCompatibleBrands()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}
            class Dog extends Animal {}

            function demo(__CompatibleTypeName<Dog> $d): void {
                __CompatibleTypeName<Animal> $c = $d;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CompatibleTypeName_RejectsUnrelatedClassNameBrand()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}
            class Cat {}

            function demo(__ClassName<Cat> $c): void {
                __CompatibleTypeName<Animal> $a = $c;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CompatibleTypeName_RejectsNarrowingParentToChild()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}
            class Dog extends Animal {}

            function demo(__CompatibleTypeName<Animal> $a): void {
                __CompatibleTypeName<Dog> $d = $a;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CompatibleTypeName_AcceptsImplementingClassNameForInterface()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface IAnimal {}
            class Dog implements IAnimal {}

            function demo(__ClassName<Dog> $d): void {
                __CompatibleTypeName<IAnimal> $c = $d;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CompatibleTypeName_AcceptsImplementingInterfaceNameForInterface()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface IAnimal {}
            interface IDog extends IAnimal {}

            function demo(__InterfaceName<IDog> $d): void {
                __CompatibleTypeName<IAnimal> $c = $d;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CompatibleTypeName_AcceptsImplementingEnumNameForInterface()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface IAnimal {}
            enum Suit: string implements IAnimal { case Hearts = 'hearts'; }

            function demo(__EnumName<Suit> $s): void {
                __CompatibleTypeName<IAnimal> $c = $s;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CompatibleTypeName_AcceptsSubclassLiteral()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}
            class Dog extends Animal {}

            function demo(): void {
                __CompatibleTypeName<Animal> $c = 'Dog';
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_ClassName_RemainsInvariantForSubclassBrands()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}
            class Dog extends Animal {}

            function demo(__ClassName<Dog> $d): void {
                __ClassName<Animal> $a = $d;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_SelfColonClass_WithTypeArgs_BrandsParametricClassName()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Box<T> {
                public static function nameOfSelf(): __ClassName<self<T>> {
                    return self<T>::class;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void StructUtilityTypes_AreRegisteredInGlobalScope()
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
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, "<?tyhp\nfunction demo(): void {}\n");

        try
        {
            var result = compilationService.ParseFiles([filePath], options);
            var symbol = ((Tyhp.TyhpLang.Binder.Scopes.Interfaces.IBaseScope)result.GlobalScope!)
                .FindChildSymbolByName("__StructKey");
            symbol.Should().BeOfType<Tyhp.TyhpLang.Binder.Symbols.BuiltInUtilityTypeSymbol>();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Check_AsNotNullable_NullInput_Succeeds()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __AsNotNullable<null> $x;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_AsNullable_WrapsType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __AsNullable<int> $x;
                ?int $y = $x;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_TypeDiff_ExcludesAssignableMember()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __TypeDiff<int|string, string> $x;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_StructKey_ResolvesForStruct()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct Point { int $x = 0; string $y = ''; }

            function demo(): void {
                __StructKey<Point> $key;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_FunctionReturnType_ResolvesFromLiteral()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function getCount(): int { return 0; }

            function demo(): void {
                __FunctionReturnType<'getCount'> $t;
                int $n = $t;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    private static ICheckedType GetInferredType(TyhpChecker checker, string nodeHint)
    {
        var match = checker.ExpressionTypes.Keys
            .OfType<TyhpNameofAst>()
            .FirstOrDefault();
        match.Should().NotBeNull();
        checker.ExpressionTypes.TryGetValue(match!, out var type).Should().BeTrue();
        return type!;
    }

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var (_, _, _, diagnostics) = CompileForChecker(content);
        return diagnostics;
    }

    private static (TyhpChecker checker, SrcFileAst file, GlobalScope global, DiagnosticBag diagnostics) CompileForChecker(string content)
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
            return (checker, result.ParsedFiles![0], result.GlobalScope!, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
