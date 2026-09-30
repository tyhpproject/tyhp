using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Symbols;
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
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
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
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
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
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
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
    public void Check_MethodGenericClassNameBrand_RejectsIntArgument()
    {
        // Unbound method T in `__ClassName<T>` must not collapse the parameter
        // to mixed (phpunit Assert::assertInstanceOf on 12.5.35).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Assert {
                public static function assertInstanceOf<ExpectedType extends object>(
                    __ClassName<ExpectedType>|__InterfaceName<ExpectedType> $expected,
                    mixed $actual
                ): void {}
            }

            function demo(): void {
                Assert::assertInstanceOf(1, new \Exception('x'));
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_MethodGenericClassNameBrand_AcceptsClassConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Assert {
                public static function assertInstanceOf<ExpectedType extends object>(
                    __ClassName<ExpectedType>|__InterfaceName<ExpectedType> $expected,
                    mixed $actual
                ): void {}
            }

            function demo(): void {
                Assert::assertInstanceOf(\Exception::class, new \Exception('x'));
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_MethodGenericUnboundTypeParameter_AcceptsInt()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public static function id<T>(T $value): void {}
            }

            function demo(): void {
                Box::id(1);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_MethodGenericStringParameter_AcceptsNonClassStringAndClassConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public static function take<T>(string $className): void {}
            }

            function demo(): void {
                Box::take('not-a-class');
                Box::take(\Exception::class);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_MethodGenericStringParameter_RejectsInt()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public static function take<T>(string $className): void {}
            }

            function demo(): void {
                Box::take(1);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_MethodGenericInterfaceNameBrand_RejectsIntArgument()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Assert {
                public static function take<ExpectedType extends object>(
                    __InterfaceName<ExpectedType> $expected
                ): void {}
            }

            function demo(): void {
                Assert::take(1);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_MethodGenericInterfaceNameBrand_AcceptsInterfaceConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            interface ThrowableLike {}

            class Assert {
                public static function take<ExpectedType extends object>(
                    __InterfaceName<ExpectedType> $expected
                ): void {}
            }

            function demo(): void {
                Assert::take(\Throwable::class);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_MethodGenericTraitNameBrand_RejectsIntArgument()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Assert {
                public static function take<ExpectedType extends object>(
                    __TraitName<ExpectedType> $expected
                ): void {}
            }

            function demo(): void {
                Assert::take(1);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_MethodGenericTraitNameBrand_AcceptsTraitConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            trait Taggable {}

            class Assert {
                public static function take<ExpectedType extends object>(
                    __TraitName<ExpectedType> $expected
                ): void {}
            }

            function demo(): void {
                Assert::take(Taggable::class);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_MethodGenericEnumNameBrand_RejectsIntArgument()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Assert {
                public static function take<ExpectedType extends object>(
                    __EnumName<ExpectedType> $expected
                ): void {}
            }

            function demo(): void {
                Assert::take(1);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_MethodGenericEnumNameBrand_AcceptsEnumConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit: string { case Hearts = 'hearts'; }

            class Assert {
                public static function take<ExpectedType extends object>(
                    __EnumName<ExpectedType> $expected
                ): void {}
            }

            function demo(): void {
                Assert::take(Suit::class);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_CallableFacet_ClassNameBrand_RejectsIntArgument()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Assert {
                public static function assertInstanceOf<ExpectedType extends object>(
                    __ClassName<ExpectedType>|__InterfaceName<ExpectedType> $expected,
                    mixed $actual
                ): void {}
            }

            function demo(): void {
                $fn = Assert::assertInstanceOf(...);
                $fn(1, new \Exception('x'));
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_CallableFacet_ClassNameBrand_AcceptsClassConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Assert {
                public static function assertInstanceOf<ExpectedType extends object>(
                    __ClassName<ExpectedType>|__InterfaceName<ExpectedType> $expected,
                    mixed $actual
                ): void {}
            }

            function demo(): void {
                $fn = Assert::assertInstanceOf(...);
                $fn(\Exception::class, new \Exception('x'));
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_Pipe_ClassNameBrand_RejectsIntArgument()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Assert {
                public static function take<ExpectedType extends object>(
                    __ClassName<ExpectedType> $expected
                ): void {}
            }

            function demo(): void {
                $fn = Assert::take(...);
                1 |> $fn;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_Pipe_ClassNameBrand_AcceptsClassConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Assert {
                public static function take<ExpectedType extends object>(
                    __ClassName<ExpectedType> $expected
                ): void {}
            }

            function demo(): void {
                $fn = Assert::take(...);
                \Exception::class |> $fn;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_Pipe_WrittenEnumNameObject_RejectsEnumConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit: string { case Hearts = 'hearts'; }

            function take(__EnumName<object> $expected): void {}

            function demo(): void {
                $fn = take(...);
                Suit::class |> $fn;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_Pipe_UnboundEnumNameBrand_AcceptsEnumConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit: string { case Hearts = 'hearts'; }

            class Assert {
                public static function take<ExpectedType extends object>(
                    __EnumName<ExpectedType> $expected
                ): void {}
            }

            function demo(): void {
                $fn = Assert::take(...);
                Suit::class |> $fn;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_CallableFacet_WrittenEnumNameObject_RejectsEnumConstant()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit: string { case Hearts = 'hearts'; }

            function take(__EnumName<object> $expected): void {}

            function demo(): void {
                $fn = take(...);
                $fn(Suit::class);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_CallableFacet_UnboundTypeParameter_AcceptsInt()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public static function id<T>(T $value): void {}
            }

            function demo(): void {
                $fn = Box::id(...);
                $fn(1);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_Pipe_UnboundTypeParameter_AcceptsInt()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public static function id<T>(T $value): void {}
            }

            function demo(): void {
                $fn = Box::id(...);
                1 |> $fn;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_EnumName_DoesNotWidenToEnumNameObject()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit: string { case Hearts = 'hearts'; }

            function take(__EnumName<object> $expected): void {}

            function demo(__EnumName<Suit> $s): void {
                __EnumName<object> $wide = $s;
                take(Suit::class);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_EnumName_DoesNotWidenToDifferentEnum()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            enum Suit: string { case Hearts = 'hearts'; }
            enum Rank: string { case Ace = 'ace'; }

            function demo(__EnumName<Suit> $s): void {
                __EnumName<Rank> $other = $s;
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
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, "<?tyhp\nfunction demo(): void {}\n");

        try
        {
            var result = compilationService.ParseFiles([filePath], options);
            var scope = (Tyhp.TyhpLang.Binder.Scopes.Interfaces.IBaseScope)result.GlobalScope!;
            string[] names =
            [
                "__StructKey",
                "__Nullable",
                "__NonNullable",
                "__AsReadOnly",
                "__CallableReturnType",
                "__CallableParametersTuple",
                "__Partial",
                "__Required",
                "__Pick",
                "__Omit",
                "__Record",
                "__Exclude",
                "__Extract",
                "__Awaited",
            ];
            foreach (var name in names)
            {
                scope.FindChildSymbolByName(name)
                    .Should().BeOfType<Tyhp.TyhpLang.Binder.Symbols.BuiltInUtilityTypeSymbol>(name);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("__AsNullable")]
    [InlineData("__AsNotNullable")]
    public void RemovedUtilityAliases_AreNotRegisteredInGlobalScope(string name)
    {
        using var compilationService = new CompilationService();
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, "<?tyhp\nfunction demo(): void {}\n");

        try
        {
            var result = compilationService.ParseFiles([filePath], options);
            var scope = (Tyhp.TyhpLang.Binder.Scopes.Interfaces.IBaseScope)result.GlobalScope!;
            scope.FindChildSymbolByName(name).Should().BeNull();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Theory]
    [InlineData("\\Tyhp\\Nullable<int>")]
    [InlineData("\\Tyhp\\NonNullable<int>")]
    [InlineData("\\Tyhp\\ReturnType<callable(string): int>")]
    [InlineData("\\Tyhp\\Parameters<callable(string): int>")]
    [InlineData("\\Tyhp\\Readonly<int>")]
    [InlineData("\\Tyhp\\Partial<int>")]
    [InlineData("\\Tyhp\\Required<int>")]
    [InlineData("\\Tyhp\\Pick<int, 'x'>")]
    [InlineData("\\Tyhp\\Omit<int, 'x'>")]
    [InlineData("\\Tyhp\\Record<string, int>")]
    [InlineData("\\Tyhp\\Exclude<int|string, int>")]
    [InlineData("\\Tyhp\\Extract<int|string, int>")]
    [InlineData("\\Tyhp\\Awaited<int>")]
    public void RemovedTyhpNamespaceUtilities_DoNotBind(string tyhpType)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            function demo({{tyhpType}} $value): void {}
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.BinderUnresolvedParameterType,
            $"expected unresolved type for {tyhpType}");
    }

    [Theory]
    [InlineData("__AsNullable<int>", "__AsNullable")]
    [InlineData("__AsNotNullable<null>", "__AsNotNullable")]
    public void RemovedAsNullableSpellings_DoNotBind(string tyhpType, string removedName)
    {
        var (_, file, global, _) = CompileForChecker($$"""
            <?tyhp
            function demo({{tyhpType}} $value): void {}
            """);

        ((Tyhp.TyhpLang.Binder.Scopes.Interfaces.IBaseScope)global)
            .FindChildSymbolByName(removedName)
            .Should().BeNull();

        var function = FindAllAst<PhpFunctionDeclAst>(file)
            .FirstOrDefault(decl => string.Equals(decl.Identifier, "demo", StringComparison.Ordinal));
        function.Should().NotBeNull();
        var parameter = function!.Parameters?.GetAllNotNull().FirstOrDefault();
        parameter.Should().NotBeNull();
        var boundName = parameter!.Type?.BoundSymbol?.Name;
        boundName.Should().NotBe(removedName);
        boundName.Should().NotBe("__Nullable");
        boundName.Should().NotBe("__NonNullable");
    }

    [Fact]
    public void Check_NonNullable_NullInput_IsVoid()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(): __NonNullable<null> {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var function = FindAllAst<PhpFunctionDeclAst>(file)
            .FirstOrDefault(decl => string.Equals(decl.Identifier, "demo", StringComparison.Ordinal));
        function.Should().NotBeNull();
        function!.ReturnType.Should().NotBeNull();
        var state = new CheckerState { CurrentFileName = file.FileName };
        if (function.BoundSymbol is FunctionDeclarationSymbol functionSymbol)
        {
            state.EnclosingFunction = functionSymbol;
        }

        var type = checker.ResolveTypeAnnotation(function.ReturnType!, state, isReturnTypePosition: true);
        CheckedTypes.AreTypesEqual(type, CheckedTypes.Void).Should().BeTrue(type.DisplayName);
    }

    [Fact]
    public void Check_NonNullable_StripsNullFromUnion()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__NonNullable<int|null> $x): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "x");
        type.IsNullable.Should().BeFalse();
        type.DisplayName.Should().Be("int");
    }

    [Fact]
    public void Check_Nullable_WrapsType()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__Nullable<int> $x): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "x");
        type.IsNullable.Should().BeTrue();
        type.Should().BeOfType<NullableCheckedType>();
        type.DisplayName.Should().Be("?int");
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
            type Point = struct { int $x = 0; string $y = ''; };

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

    private static ICheckedType ResolveParameterDeclaredType(TyhpChecker checker, SrcFileAst file, string parameterName)
    {
        var function = FindAllAst<PhpFunctionDeclAst>(file)
            .FirstOrDefault(decl => string.Equals(decl.Identifier, "demo", StringComparison.Ordinal));
        function.Should().NotBeNull("demo function should exist");

        var parameter = function!.Parameters?.GetAllNotNull()
            .FirstOrDefault(param =>
                string.Equals(param.Name.TrimStart('$'), parameterName, StringComparison.Ordinal));
        parameter.Should().NotBeNull($"parameter '{parameterName}' should exist");
        parameter!.Type.Should().NotBeNull();

        var state = new CheckerState { CurrentFileName = file.FileName };
        if (function.BoundSymbol is FunctionDeclarationSymbol functionSymbol)
        {
            state.EnclosingFunction = functionSymbol;
        }

        return checker.ResolveTypeAnnotation(parameter.Type!, state);
    }

    private static IEnumerable<T> FindAllAst<T>(IBase2Ast root) where T : class, IBase2Ast
    {
        if (root is T match)
        {
            yield return match;
        }

        foreach (var child in root.AstChildren)
        {
            if (child is null)
            {
                continue;
            }

            foreach (var found in FindAllAst<T>(child))
            {
                yield return found;
            }
        }
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
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
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
