using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 16.5 Phase 1–7: <c>__CallableReturnType</c> / <c>__CallableParametersStruct</c> /
/// <c>__CallableParametersTuple</c> / <c>__CallableParametersRest</c> registration, signature
/// reflection, return-type resolution (including inference), named-parameter bags,
/// positional-parameter bags, optional/required-key assignability, rest unpack,
/// ExtStandard <c>call_user_func</c> / <c>call_user_func_array</c> builtins, and
/// non-callable diagnostics.
/// </summary>
[Trait("Category", "Checker")]
public class CallableSignatureUtilityTests
{
    [Fact]
    public void CallableSignatureUtilities_AreRegisteredInGlobalScope()
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
            var global = (IBaseScope)result.GlobalScope!;

            AssertRegistered(global, "__CallableReturnType", UtilityBehavior.CallableReturnType);
            AssertRegistered(global, "__CallableParametersStruct", UtilityBehavior.CallableParametersStruct);
            AssertRegistered(global, "__CallableParametersTuple", UtilityBehavior.CallableParametersTuple);
            AssertRegistered(global, "__CallableParametersRest", UtilityBehavior.CallableParametersRest);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Check_CallableReturnType_ResolvesInTypePosition()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__CallableReturnType<callable<string, int>> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.DisplayName.Should().Be("int");
    }

    [Fact]
    public void Check_CallableParametersBags_AreResolvableInTypePosition()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(
                __CallableParametersStruct<callable<string, int>> $named,
                __CallableParametersTuple<callable<string, int>> $positional
            ): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var named = ResolveParameterDeclaredType(checker, file, "named");
        named.Should().BeOfType<StructCheckedType>();
        ((StructCheckedType)named).Properties.Should().BeEmpty(
            "nameless callable<> facets degrade to an empty named bag");
        var positional = ResolveParameterDeclaredType(checker, file, "positional");
        positional.Should().BeOfType<StructCheckedType>();
        var tuple = (StructCheckedType)positional;
        tuple.Properties.Should().ContainKey("$_1");
        tuple.Properties["$_1"].IntegerKeyAlias.Should().Be(0);
        tuple.Properties["$_1"].Type.DisplayName.Should().Be("string");
    }

    [Theory]
    [InlineData("__CallableReturnType")]
    [InlineData("__CallableParametersStruct")]
    [InlineData("__CallableParametersTuple")]
    [InlineData("__CallableParametersRest")]
    public void Check_NonCallableTypeArg_ReportsConstraint(string utility)
    {
        var diagnostics = CompileAndCheck($$"""
            <?tyhp
            function demo({{utility}}<int> $t): void {}
            """);

        diagnostics.Errors.Should().ContainSingle(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUtilityTypeInvalidArgument);
    }

    [Fact]
    public void Check_UnboundTypeParameter_DoesNotReportConstraint()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo<TCallable extends callable>(__CallableReturnType<TCallable> $t): void {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUtilityTypeInvalidArgument);
    }

    [Fact]
    public void Check_UnboundTypeParameter_KeepsDeferredCallableReturnType()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo<TCallable extends callable>(__CallableReturnType<TCallable> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.Should().BeOfType<GenericCheckedType>();
        var generic = (GenericCheckedType)type;
        SymbolNameTypeHelper.TryGetUtilitySymbol(generic, out var utility).Should().BeTrue();
        utility.Behavior.Should().Be(UtilityBehavior.CallableReturnType);
        generic.TypeArguments.Should().ContainSingle();
        generic.TypeArguments[0].Should().BeOfType<SimpleCheckedType>();
        ((SimpleCheckedType)generic.TypeArguments[0]).ResolvedSymbol
            .Should().BeOfType<GenericTypeParameterSymbol>();
    }

    [Fact]
    public void Check_UnboundTypeParameter_KeepsDeferredCallableParametersStruct()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo<TCallable extends callable>(__CallableParametersStruct<TCallable> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.Should().BeOfType<GenericCheckedType>();
        var generic = (GenericCheckedType)type;
        SymbolNameTypeHelper.TryGetUtilitySymbol(generic, out var utility).Should().BeTrue();
        utility.Behavior.Should().Be(UtilityBehavior.CallableParametersStruct);
        generic.TypeArguments.Should().ContainSingle();
        generic.TypeArguments[0].Should().BeOfType<SimpleCheckedType>();
        ((SimpleCheckedType)generic.TypeArguments[0]).ResolvedSymbol
            .Should().BeOfType<GenericTypeParameterSymbol>();
    }

    [Fact]
    public void Check_UnboundTypeParameter_KeepsDeferredCallableParametersTuple()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo<TCallable extends callable>(__CallableParametersTuple<TCallable> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.Should().BeOfType<GenericCheckedType>();
        var generic = (GenericCheckedType)type;
        SymbolNameTypeHelper.TryGetUtilitySymbol(generic, out var utility).Should().BeTrue();
        utility.Behavior.Should().Be(UtilityBehavior.CallableParametersTuple);
        generic.TypeArguments.Should().ContainSingle();
        generic.TypeArguments[0].Should().BeOfType<SimpleCheckedType>();
        ((SimpleCheckedType)generic.TypeArguments[0]).ResolvedSymbol
            .Should().BeOfType<GenericTypeParameterSymbol>();
    }

    [Fact]
    public void Check_UnboundTypeParameter_KeepsDeferredCallableParametersRest()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo<TCallable extends callable>(__CallableParametersRest<TCallable> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.Should().BeOfType<GenericCheckedType>();
        var generic = (GenericCheckedType)type;
        SymbolNameTypeHelper.TryGetUtilitySymbol(generic, out var utility).Should().BeTrue();
        utility.Behavior.Should().Be(UtilityBehavior.CallableParametersRest);
        generic.TypeArguments.Should().ContainSingle();
        generic.TypeArguments[0].Should().BeOfType<SimpleCheckedType>();
        ((SimpleCheckedType)generic.TypeArguments[0]).ResolvedSymbol
            .Should().BeOfType<GenericTypeParameterSymbol>();
    }

    [Fact]
    public void Check_ConcreteCallable_KeepsCallableParametersRestWrapper()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__CallableParametersRest<callable<string, int>> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.Should().BeOfType<GenericCheckedType>();
        var generic = (GenericCheckedType)type;
        SymbolNameTypeHelper.TryGetUtilitySymbol(generic, out var utility).Should().BeTrue();
        utility.Behavior.Should().Be(UtilityBehavior.CallableParametersRest);
    }

    [Fact]
    public void Check_CallableReturnType_ClosureGeneric_ResolvesToReturnType()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__CallableReturnType<\Closure<string, int>> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.DisplayName.Should().Be("int");
    }

    [Fact]
    public void Check_CallableReturnType_UnionOfCallables_ResolvesToUnionReturn()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__CallableReturnType<callable<int> | callable<string>> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.Should().BeOfType<UnionCheckedType>();
        var names = ((UnionCheckedType)type).Members.Select(m => m.DisplayName).ToList();
        names.Should().Contain("int");
        names.Should().Contain("string");
    }

    [Fact]
    public void Check_CallableReturnType_ZeroArgCallable_ResolvesToReturnType()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__CallableReturnType<callable<int>> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.DisplayName.Should().Be("int");
    }

    [Fact]
    public void Check_GenericApply_InfersReturnFromFirstClassFunction()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(TCallable $cb): __CallableReturnType<TCallable> {
                return $cb();
            }

            function getCount(): int {
                return 0;
            }

            function demo(): void {
                int $n = apply(getCount(...));
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_InfersReturnFromClosure()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(TCallable $cb): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(): void {
                int $n = apply(fn(): int => 0);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_InfersReturnFromTypedClosureVariable()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(TCallable $cb): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(): void {
                \Closure<int> $cb = fn(): int => 0;
                int $n = apply($cb);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_InfersReturnFromFirstClassMethod()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(TCallable $cb): __CallableReturnType<TCallable> {
                return $cb();
            }

            class Counter {
                public static function getCount(): int {
                    return 0;
                }
            }

            function demo(): void {
                int $n = apply(Counter::getCount(...));
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_WrongAssignmentType_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(TCallable $cb): __CallableReturnType<TCallable> {
                return $cb();
            }

            function getCount(): int {
                return 0;
            }

            function demo(): void {
                string $s = apply(getCount(...));
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            "inferred int return must not assign to string (proves the call is not unresolved)");
    }

    [Fact]
    public void Check_GenericApply_TyhpReturnType_BodyAndInferenceMatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(TCallable $cb): \Tyhp\ReturnType<TCallable> {
                return $cb();
            }

            function getCount(): int {
                return 0;
            }

            function demo(): void {
                int $n = apply(getCount(...));
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_UnionOfClosures_InfersUnionReturn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(TCallable $cb): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(bool $flag): void {
                int|string $n = apply($flag ? fn(): int => 0 : fn(): string => 'x');
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_UnionOfClosures_DoesNotAssignToSingleReturn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(TCallable $cb): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(bool $flag): void {
                int $n = apply($flag ? fn(): int => 0 : fn(): string => 'x');
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            "int|string return must not assign to int");
    }

    [Fact]
    public void Check_GenericApply_NamedBag_AssignsAndReturnsCallableReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['name' => 'Ada', 'age' => 36]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_NamedBag_WrongKey_ReportsUnknownProperty()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['nome' => 'Ada', 'age' => 36]);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_NamedBag_WrongType_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['name' => 1, 'age' => 36]);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_NamedBag_Closure_UsesParameterNames()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(): void {
                string $s = apply(
                    fn(string $name, int $age): string => $name,
                    ['name' => 'Ada', 'age' => 36]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_NamedBag_QuotedUnknownKey_HasNoBrokenSuggestion()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['nome' => 'Ada', 'age' => 36]);
            }
            """);

        // 'nome' occupies six source columns but decodes to four, so an edit span measured from
        // the decoded name would cut into the quotes and produce `namee'`.
        var unknown = diagnostics.Errors.Should()
            .ContainSingle(d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty,
                Describe(diagnostics.Errors))
            .Subject;
        unknown.Suggestion.Should().BeNull();
    }

    [Fact]
    public void Check_GenericApply_NamedBag_SpreadEntry_FallsBackWithoutPartialReport()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(array $rest): void {
                string $s = apply(greet(...), ['nome' => 'Ada', ...$rest]);
            }
            """);

        // A spread makes the literal unreadable as a named bag, so the whole literal falls back to
        // ordinary assignability. The leading bad key must not also be reported on its own.
        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_PositionalBag_ListLiteral_AssignsAndReturnsCallableReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersTuple<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['Ada', 36]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_PositionalBag_ExplicitIntKeys_Assigns()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersTuple<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), [0 => 'Ada', 1 => 36]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_PositionalBag_CallableArgsStyleKeys_Assigns()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersTuple<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['_1' => 'Ada', '_2' => 36]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_PositionalBag_WrongType_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersTuple<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['Ada', 'oops']);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_PositionalBag_ExtraIndex_ReportsUnknownProperty()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersTuple<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['Ada', 36, true]);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_PositionalBag_NamelessCallableFacet_ChecksTypes()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersTuple<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(callable<string, int, bool> $cb): void {
                bool $ok = apply($cb, ['Ada', 36]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_PositionalBag_Closure_Assigns()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersTuple<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(): void {
                string $s = apply(
                    fn(string $name, int $age): string => $name,
                    ['Ada', 36]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_PositionalBag_TypedVariable_ListLiteral()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __CallableParametersTuple<callable<string, int, bool>> $args = ['Ada', 36];
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_PositionalBag_CanonicalNumericStringKeys_Assign()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __CallableParametersTuple<callable<string, int, bool>> $args = ['0' => 'Ada', '1' => 36];
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_PositionalBag_NonCanonicalNumericStringKey_IsNotAnIntegerKey()
    {
        // PHP keeps `'00'` a string key, so this literal is not the positional bag it looks like.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __CallableParametersTuple<callable<string, int, bool>> $args = ['00' => 'Ada', '1' => 36];
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_PositionalBag_IndexAccess_YieldsParameterType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__CallableParametersTuple<callable<string, int, bool>> $args): string {
                return $args[0];
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_PositionalBag_IndexAccess_WrongAssignment_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__CallableParametersTuple<callable<string, int, bool>> $args): int {
                return $args[0];
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch
                || d.Code == MessageCode.CheckerIncompatibleReturnType,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_NamedBag_OmittingDefaultedParam_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age = 0): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['name' => 'Ada']);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_NamedBag_OmittingRequiredParam_ReportsMissingKey()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age = 0): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['age' => 36]);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerStructRequiredKeyMissing,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_NamedBag_EmptyLiteral_OmittingRequired_ReportsMissingKey()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age = 0): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), []);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerStructRequiredKeyMissing,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_NamedBag_AllOptional_EmptyLiteral_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function ping(int $n = 0): int {
                return $n;
            }

            function demo(): void {
                int $n = apply(ping(...), []);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_NamedBag_ClosureWithDefault_OmittingIsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersStruct<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(): void {
                string $s = apply(
                    fn(string $name, int $age = 0): string => $name,
                    ['name' => 'Ada']);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_PositionalBag_OmittingTrailingDefault_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersTuple<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age = 0): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), ['Ada']);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericApply_PositionalBag_OmittingRequired_ReportsMissingKey()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function apply<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersTuple<TCallable> $args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age = 0): string {
                return $name;
            }

            function demo(): void {
                string $s = apply(greet(...), []);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerStructRequiredKeyMissing,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_PositionalBag_TypedVariable_OmittingRequired_ReportsMissingKey()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __CallableParametersTuple<callable<string, int, bool>> $args = ['Ada'];
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerStructRequiredKeyMissing,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_PositionalBag_Assignment_OmittingRequired_ReportsMissingKey()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __CallableParametersTuple<callable<string, int, bool>> $args;
                $args = ['Ada'];
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerStructRequiredKeyMissing,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_PositionalBag_NullCoalescingAssignment_OmittingRequired_ReportsMissingKey()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __CallableParametersTuple<callable<string, int, bool>> $args;
                $args ??= ['Ada'];
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerStructRequiredKeyMissing,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_ParameterDefault_EmptyBag_OmittingRequired_ReportsMissingKey()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__CallableParametersTuple<callable<string, int, bool>> $args = []): void {}
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerStructRequiredKeyMissing,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericCall_DoesNotNarrowOrdinaryParametersToArgumentLiterals()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function run<TValue>(callable<TValue, TValue> $cb, TValue $seed): TValue {
                return $cb($seed);
            }

            function demo(): void {
                run(fn(int $n): int => $n + 1, 1);
            }
            """);

        // Inference binds TValue from the argument *value* `1`, i.e. the literal type `1`. Feeding
        // that back into `callable<TValue, TValue>` would demand a callback returning exactly `1`.
        // Ordinary generic parameters keep the gradual mixed policy; only deferred
        // callable-signature utilities take call-site bindings.
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_AssignsAndReturnsCallableReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = invoke(greet(...), 'Ada', 36);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_WrongType_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = invoke(greet(...), 1, 36);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_MissingRequired_ReportsMissingArgument()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = invoke(greet(...), 'Ada');
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerMissingArgument,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_OmittingTrailingDefault_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age = 0): string {
                return $name;
            }

            function demo(): void {
                string $s = invoke(greet(...), 'Ada');
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_TooMany_ReportsTooManyArguments()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = invoke(greet(...), 'Ada', 36, true);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTooManyArguments,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_ZeroArgCallable_NoExtraArgs()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function ping(): int {
                return 1;
            }

            function demo(): void {
                int $n = invoke(ping(...));
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_Closure_UsesParameterTypes()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(): void {
                string $s = invoke(
                    fn(string $name, int $age): string => $name,
                    'Ada',
                    36);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_FacetCallable_ChecksPositionalTypes()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(callable<string, int, bool> $cb): void {
                bool $ok = invoke($cb, 'Ada', 36);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_OpaqueCallable_DoesNotCheckArity()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(callable $cb): void {
                invoke($cb, 1, 2, 3);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            "opaque callable has unknown arity; extra rest args stay gradual");
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_Spread_DoesNotReportMissing()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(array $packed): void {
                string $s = invoke(greet(...), ...$packed);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            "a trailing spread is not an empty rest list; TYHP4142 must not fire");
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_SpreadThenPositional_DoesNotTypeAsFirstInnerParam()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(array $packed): void {
                string $s = invoke(greet(...), ...$packed, true);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            "true after a rest-region spread is not inner parameter 0 (string)");
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_PositionalThenSpread_StillChecksPrefix()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(array $packed): void {
                string $s = invoke(greet(...), 1, ...$packed);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_NamedRest_StillChecksCallback()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = invoke(args: 'Ada', cb: greet(...));
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            "named pack into $args must not skip $cb or invent TYHP4142");
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_NamedRest_DoesNotSkipLaterNamedArgs()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(): void {
                invoke(args: 'Ada', nope: 1);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerUnknownNamedArgument,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_UnionSameArity_ChecksArgs()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(callable<string, int> | callable<string, bool> $cb): void {
                invoke($cb, 'Ada');
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_UnionSameArity_WrongType_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(callable<string, int> | callable<string, bool> $cb): void {
                invoke($cb, 1);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_MismatchedArityUnion_StaysGradual()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function demo(callable<int> | callable<string, int> $cb): void {
                invoke($cb, 'Ada');
            }
            """);

        diagnostics.Errors.Should().BeEmpty(
            "unions whose members have different arities stay gradual");
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_InnerVariadic_AcceptsExtraArgs()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function writeLog(string $msg, string ...$extra): void {}

            function demo(): void {
                invoke(writeLog(...), 'hi', 'a', 'b');
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_GenericInvoke_RestUnpack_InnerVariadic_WrongExtraType_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function invoke<TCallable extends callable>(
                TCallable $cb,
                __CallableParametersRest<TCallable> ...$args
            ): __CallableReturnType<TCallable> {
                return $cb();
            }

            function writeLog(string $msg, string ...$extra): void {}

            function demo(): void {
                invoke(writeLog(...), 'hi', 1);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallableParametersRest_UnionOfCallables_KeepsWrapper()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__CallableParametersRest<callable<string, int> | callable<string, bool>> $t): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        var type = ResolveParameterDeclaredType(checker, file, "t");
        type.Should().BeOfType<GenericCheckedType>();
        var generic = (GenericCheckedType)type;
        SymbolNameTypeHelper.TryGetUtilitySymbol(generic, out var utility).Should().BeTrue();
        utility.Behavior.Should().Be(UtilityBehavior.CallableParametersRest);
    }

    [Fact]
    public void ExpandAfterSubstitution_ConcreteCallable_YieldsReturnType()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableReturnType",
            UtilityBehavior.CallableReturnType,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var wrapped = new GenericCheckedType(
            CheckedTypes.FromSymbol(utility),
            [new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int)]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        CheckedTypes.AreTypesEqual(expanded, CheckedTypes.Int).Should().BeTrue();
    }

    [Fact]
    public void ExpandAfterSubstitution_UnboundTypeParameter_StaysDeferred()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableReturnType",
            UtilityBehavior.CallableReturnType,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var typeParam = CheckedTypes.FromSymbol(
            new GenericTypeParameterSymbol("TCallable", SymbolType.FunctionGenericTypeParameter));
        var wrapped = new GenericCheckedType(CheckedTypes.FromSymbol(utility), [typeParam]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        expanded.Should().BeSameAs(wrapped);
    }

    [Fact]
    public void ExpandAfterSubstitution_UnionOfCallables_UnionsReturnTypes()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableReturnType",
            UtilityBehavior.CallableReturnType,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var wrapped = new GenericCheckedType(
            CheckedTypes.FromSymbol(utility),
            [CheckedTypes.UnionTypes(
                new CallableCheckedType([], CheckedTypes.Int),
                new CallableCheckedType([], CheckedTypes.String))]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        expanded.Should().BeOfType<UnionCheckedType>();
        var union = (UnionCheckedType)expanded;
        union.Members.Should().HaveCount(2);
        union.Members.Should().Contain(m => CheckedTypes.AreTypesEqual(m, CheckedTypes.Int));
        union.Members.Should().Contain(m => CheckedTypes.AreTypesEqual(m, CheckedTypes.String));
    }

    [Fact]
    public void ExpandAfterSubstitution_NonCallable_DoesNotLeakUtility()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableReturnType",
            UtilityBehavior.CallableReturnType,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var wrapped = new GenericCheckedType(
            CheckedTypes.FromSymbol(utility),
            [CheckedTypes.Int]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        TypeComparer.IsUnresolvedType(expanded).Should().BeTrue();
    }

    [Fact]
    public void ExpandAfterSubstitution_NamedCallable_YieldsNamedStructBag()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableParametersStruct",
            UtilityBehavior.CallableParametersStruct,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var wrapped = new GenericCheckedType(
            CheckedTypes.FromSymbol(utility),
            [new CallableCheckedType(
                [CheckedTypes.String, CheckedTypes.Int],
                CheckedTypes.Bool,
                ["name", "age"])]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        expanded.Should().BeOfType<StructCheckedType>();
        var bag = (StructCheckedType)expanded;
        bag.Properties.Should().ContainKey("$name");
        bag.Properties.Should().ContainKey("$age");
        CheckedTypes.AreTypesEqual(bag.Properties["$name"].Type, CheckedTypes.String).Should().BeTrue();
        CheckedTypes.AreTypesEqual(bag.Properties["$age"].Type, CheckedTypes.Int).Should().BeTrue();
        bag.Properties["$name"].IsOptional.Should().BeFalse();
        bag.Properties["$age"].IsOptional.Should().BeFalse();
    }

    [Fact]
    public void ExpandAfterSubstitution_NamelessCallable_YieldsEmptyStructBag()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableParametersStruct",
            UtilityBehavior.CallableParametersStruct,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var wrapped = new GenericCheckedType(
            CheckedTypes.FromSymbol(utility),
            [new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int)]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        expanded.Should().BeOfType<StructCheckedType>();
        ((StructCheckedType)expanded).Properties.Should().BeEmpty();
    }

    [Fact]
    public void ExpandAfterSubstitution_UnboundTypeParameter_KeepsDeferredStruct()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableParametersStruct",
            UtilityBehavior.CallableParametersStruct,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var typeParam = CheckedTypes.FromSymbol(
            new GenericTypeParameterSymbol("TCallable", SymbolType.FunctionGenericTypeParameter));
        var wrapped = new GenericCheckedType(CheckedTypes.FromSymbol(utility), [typeParam]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        expanded.Should().BeSameAs(wrapped);
    }

    [Fact]
    public void ExpandAfterSubstitution_NamelessCallable_YieldsPositionalTupleBag()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableParametersTuple",
            UtilityBehavior.CallableParametersTuple,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var wrapped = new GenericCheckedType(
            CheckedTypes.FromSymbol(utility),
            [new CallableCheckedType([CheckedTypes.String, CheckedTypes.Int], CheckedTypes.Bool)]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        expanded.Should().BeOfType<StructCheckedType>();
        var bag = (StructCheckedType)expanded;
        bag.Properties.Should().ContainKey("$_1");
        bag.Properties.Should().ContainKey("$_2");
        bag.Properties["$_1"].IntegerKeyAlias.Should().Be(0);
        bag.Properties["$_2"].IntegerKeyAlias.Should().Be(1);
        CheckedTypes.AreTypesEqual(bag.Properties["$_1"].Type, CheckedTypes.String).Should().BeTrue();
        CheckedTypes.AreTypesEqual(bag.Properties["$_2"].Type, CheckedTypes.Int).Should().BeTrue();
    }

    [Fact]
    public void ExpandAfterSubstitution_UnboundTypeParameter_KeepsDeferredTuple()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableParametersTuple",
            UtilityBehavior.CallableParametersTuple,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var typeParam = CheckedTypes.FromSymbol(
            new GenericTypeParameterSymbol("TCallable", SymbolType.FunctionGenericTypeParameter));
        var wrapped = new GenericCheckedType(CheckedTypes.FromSymbol(utility), [typeParam]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        expanded.Should().BeSameAs(wrapped);
    }

    [Fact]
    public void ExpandAfterSubstitution_ConcreteCallable_KeepsRestWrapper()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableParametersRest",
            UtilityBehavior.CallableParametersRest,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var wrapped = new GenericCheckedType(
            CheckedTypes.FromSymbol(utility),
            [new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int)]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        expanded.Should().BeSameAs(wrapped);
        SymbolNameTypeHelper.TryGetUtilitySymbol(expanded, out var expandedUtility).Should().BeTrue();
        expandedUtility.Behavior.Should().Be(UtilityBehavior.CallableParametersRest);
    }

    [Fact]
    public void ExpandAfterSubstitution_UnboundTypeParameter_KeepsDeferredRest()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableParametersRest",
            UtilityBehavior.CallableParametersRest,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var typeParam = CheckedTypes.FromSymbol(
            new GenericTypeParameterSymbol("TCallable", SymbolType.FunctionGenericTypeParameter));
        var wrapped = new GenericCheckedType(CheckedTypes.FromSymbol(utility), [typeParam]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        expanded.Should().BeSameAs(wrapped);
    }

    [Fact]
    public void ExpandAfterSubstitution_NonCallable_DoesNotLeakRestUtility()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableParametersRest",
            UtilityBehavior.CallableParametersRest,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var wrapped = new GenericCheckedType(
            CheckedTypes.FromSymbol(utility),
            [CheckedTypes.Int]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        TypeComparer.IsUnresolvedType(expanded).Should().BeTrue();
    }

    [Fact]
    public void ExpandAfterSubstitution_UnionOfCallables_KeepsRestWrapper()
    {
        var utility = new BuiltInUtilityTypeSymbol(
            "__CallableParametersRest",
            UtilityBehavior.CallableParametersRest,
            GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable));
        var wrapped = new GenericCheckedType(
            CheckedTypes.FromSymbol(utility),
            [CheckedTypes.UnionTypes(
                new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int),
                new CallableCheckedType([CheckedTypes.String], CheckedTypes.Bool))]);

        var expanded = UtilityTypeResolver.ExpandAfterSubstitution(wrapped);
        expanded.Should().BeSameAs(wrapped);
        SymbolNameTypeHelper.TryGetUtilitySymbol(expanded, out var expandedUtility).Should().BeTrue();
        expandedUtility.Behavior.Should().Be(UtilityBehavior.CallableParametersRest);
    }

    [Fact]
    public void TryReflect_UnionSameArity_MergesParameterTypes()
    {
        var union = CheckedTypes.UnionTypes(
            new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int),
            new CallableCheckedType([CheckedTypes.Int], CheckedTypes.Bool));

        CallableSignatureReflection.TryReflect(union, out var signature).Should().BeTrue();
        signature.Should().NotBeNull();
        signature!.Parameters.Should().ContainSingle();
        signature.Parameters[0].IsOptional.Should().BeFalse();
        signature.Parameters[0].Type.Should().BeOfType<UnionCheckedType>();
        var members = ((UnionCheckedType)signature.Parameters[0].Type).Members;
        members.Should().Contain(m => CheckedTypes.AreTypesEqual(m, CheckedTypes.String));
        members.Should().Contain(m => CheckedTypes.AreTypesEqual(m, CheckedTypes.Int));
    }

    [Fact]
    public void TryReflect_UnionMismatchedArity_Fails()
    {
        var union = CheckedTypes.UnionTypes(
            new CallableCheckedType([], CheckedTypes.Int),
            new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int));

        CallableSignatureReflection.TryReflect(union, out var signature).Should().BeFalse();
        signature.Should().BeNull();
    }

    [Fact]
    public void TryBuildNamedParametersStruct_SkipsVariadicParameter()
    {
        var facet = new CallableCheckedType(
            [CheckedTypes.String, CheckedTypes.Int],
            CheckedTypes.Void,
            ["name", "rest"],
            lastParameterIsVariadic: true);

        CallableSignatureReflection.TryBuildNamedParametersStruct(facet, out var bag).Should().BeTrue();
        bag.Should().NotBeNull();
        bag!.Properties.Should().ContainKey("$name");
        bag.Properties.Should().NotContainKey("$rest");
    }

    [Fact]
    public void TryBuildPositionalParametersStruct_UsesIntegerAliasesAndSkipsVariadic()
    {
        var facet = new CallableCheckedType(
            [CheckedTypes.String, CheckedTypes.Int, CheckedTypes.Bool],
            CheckedTypes.Void,
            ["name", "age", "rest"],
            lastParameterIsVariadic: true);

        CallableSignatureReflection.TryBuildPositionalParametersStruct(facet, out var bag).Should().BeTrue();
        bag.Should().NotBeNull();
        bag!.Properties.Should().ContainKey("$_1");
        bag.Properties.Should().ContainKey("$_2");
        bag.Properties.Should().NotContainKey("$_3");
        bag.Properties["$_1"].IntegerKeyAlias.Should().Be(0);
        bag.Properties["$_2"].IntegerKeyAlias.Should().Be(1);
        CheckedTypes.AreTypesEqual(bag.Properties["$_1"].Type, CheckedTypes.String).Should().BeTrue();
        CheckedTypes.AreTypesEqual(bag.Properties["$_2"].Type, CheckedTypes.Int).Should().BeTrue();
        bag.Properties["$_1"].IsOptional.Should().BeFalse();
        bag.Properties["$_2"].IsOptional.Should().BeFalse();
    }

    [Fact]
    public void TryBuildNamedParametersStruct_ArityIntersection_MarksTrailingOptional()
    {
        var intersection = new IntersectionCheckedType(
        [
            new CallableCheckedType([CheckedTypes.String], CheckedTypes.Void, ["name"]),
            new CallableCheckedType(
                [CheckedTypes.String, CheckedTypes.Int],
                CheckedTypes.Void,
                ["name", "age"]),
        ]);

        CallableSignatureReflection.TryBuildNamedParametersStruct(intersection, out var bag).Should().BeTrue();
        bag.Should().NotBeNull();
        bag!.Properties["$name"].IsOptional.Should().BeFalse();
        bag.Properties["$age"].IsOptional.Should().BeTrue();
        CheckedTypes.AreTypesEqual(bag.Properties["$age"].Type, CheckedTypes.Int).Should().BeTrue();
    }

    [Fact]
    public void TryBuildPositionalParametersStruct_ArityIntersection_MarksTrailingOptional()
    {
        var intersection = new IntersectionCheckedType(
        [
            new CallableCheckedType([CheckedTypes.String], CheckedTypes.Void),
            new CallableCheckedType([CheckedTypes.String, CheckedTypes.Int], CheckedTypes.Void),
        ]);

        CallableSignatureReflection.TryBuildPositionalParametersStruct(intersection, out var bag).Should().BeTrue();
        bag.Should().NotBeNull();
        bag!.Properties["$_1"].IsOptional.Should().BeFalse();
        bag.Properties["$_2"].IsOptional.Should().BeTrue();
        bag.Properties["$_2"].IntegerKeyAlias.Should().Be(1);
    }

    [Fact]
    public void TryBuildPositionalParametersStruct_NamelessFacet_StillHasKeys()
    {
        var facet = new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int);

        CallableSignatureReflection.TryBuildPositionalParametersStruct(facet, out var bag).Should().BeTrue();
        bag.Should().NotBeNull();
        bag!.Properties.Should().ContainKey("$_1");
        bag.Properties["$_1"].IntegerKeyAlias.Should().Be(0);
    }

    [Fact]
    public void CallableCheckedType_Equality_IgnoresParameterNames()
    {
        var named = new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int, ["name"]);
        var nameless = new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int);
        TypeComparer.AreTypesEqual(named, nameless).Should().BeTrue();
    }

    [Fact]
    public void TryGetReturnType_UnionOfCallables_UnionsReturns()
    {
        var union = CheckedTypes.UnionTypes(
            new CallableCheckedType([CheckedTypes.String], CheckedTypes.Int),
            new CallableCheckedType([CheckedTypes.String], CheckedTypes.Bool));

        CallableSignatureReflection.TryGetReturnType(union, out var returnType).Should().BeTrue();
        returnType.Should().BeOfType<UnionCheckedType>();
        var members = ((UnionCheckedType)returnType).Members;
        members.Should().Contain(m => CheckedTypes.AreTypesEqual(m, CheckedTypes.Int));
        members.Should().Contain(m => CheckedTypes.AreTypesEqual(m, CheckedTypes.Bool));
    }

    [Fact]
    public void DeferredReturnTypeUtilities_CompareEqualAcrossSpellings()
    {
        var typeParam = CheckedTypes.FromSymbol(
            new GenericTypeParameterSymbol("TCallable", SymbolType.FunctionGenericTypeParameter));
        var callableReturn = new GenericCheckedType(
            CheckedTypes.FromSymbol(new BuiltInUtilityTypeSymbol(
                "__CallableReturnType",
                UtilityBehavior.CallableReturnType,
                GenericParameterRequirements.Single("TCallable", BuiltInGenericParameterConstraint.Callable))),
            [typeParam]);
        var tyhpReturn = new GenericCheckedType(
            CheckedTypes.FromSymbol(new BuiltInUtilityTypeSymbol(
                "ReturnType",
                UtilityBehavior.ReturnType,
                GenericParameterRequirements.Single("T", BuiltInGenericParameterConstraint.Callable))),
            [typeParam]);

        TypeComparer.AreTypesEqual(callableReturn, tyhpReturn).Should().BeTrue();
    }

    [Fact]
    public void Reflect_GenericCallable_AgreesWithArityFacetArgCount()
    {
        var callable = new GenericCheckedType(
            CheckedTypes.FromSymbol(new BuiltInTypeSymbol("callable")),
            [CheckedTypes.String, CheckedTypes.Int, CheckedTypes.Bool]);

        CallableSignatureReflection.TryReflect(callable, out var signature).Should().BeTrue();
        signature.Should().NotBeNull();
        signature!.Parameters.Should().HaveCount(2);
        CheckedTypes.AreTypesEqual(signature.Parameters[0].Type, CheckedTypes.String).Should().BeTrue();
        CheckedTypes.AreTypesEqual(signature.Parameters[1].Type, CheckedTypes.Int).Should().BeTrue();
        CheckedTypes.AreTypesEqual(signature.ReturnType, CheckedTypes.Bool).Should().BeTrue();

        var facets = CallableArityFacetBuilder.GetCallableFacets(callable);
        facets.Should().ContainSingle();
        facets[0].ParameterTypes.Should().HaveCount(signature.Parameters.Count);
    }

    [Fact]
    public void Reflect_CallableCheckedType_MatchesGenericForm()
    {
        var facet = new CallableCheckedType([CheckedTypes.String, CheckedTypes.Int], CheckedTypes.Bool);
        var generic = new GenericCheckedType(
            CheckedTypes.FromSymbol(new BuiltInTypeSymbol("callable")),
            [CheckedTypes.String, CheckedTypes.Int, CheckedTypes.Bool]);

        CallableSignatureReflection.TryReflect(facet, out var fromFacet).Should().BeTrue();
        CallableSignatureReflection.TryReflect(generic, out var fromGeneric).Should().BeTrue();

        fromFacet!.Parameters.Should().HaveCount(fromGeneric!.Parameters.Count);
        CheckedTypes.AreTypesEqual(fromFacet.ReturnType, fromGeneric.ReturnType).Should().BeTrue();
    }

    [Fact]
    public void Reflect_ArityFacetIntersection_UsesLongestAndMarksOptional()
    {
        var intersection = new IntersectionCheckedType(
        [
            new CallableCheckedType([CheckedTypes.String], CheckedTypes.Void),
            new CallableCheckedType([CheckedTypes.String, CheckedTypes.Int], CheckedTypes.Void),
        ]);

        CallableSignatureReflection.TryReflect(intersection, out var signature).Should().BeTrue();
        signature!.Parameters.Should().HaveCount(2);
        signature.Parameters[0].IsOptional.Should().BeFalse();
        signature.Parameters[1].IsOptional.Should().BeTrue();
        CheckedTypes.AreTypesEqual(signature.ReturnType, CheckedTypes.Void).Should().BeTrue();
    }

    [Fact]
    public void Reflect_NonCallable_ReturnsFalse()
    {
        CallableSignatureReflection.TryReflect(CheckedTypes.Int, out var signature).Should().BeFalse();
        signature.Should().BeNull();
    }

    [Fact]
    public void Reflect_BareCallable_IsOpaqueMixedReturn()
    {
        var bare = CheckedTypes.FromSymbol(new BuiltInTypeSymbol("callable"));
        CallableSignatureReflection.TryReflect(bare, out var signature).Should().BeTrue();
        signature!.Parameters.Should().BeEmpty();
        signature.ReturnType.IsMixed.Should().BeTrue();
    }

    /// <summary>
    /// Empty <c>callable&lt;&gt;</c> / <c>\Closure&lt;&gt;</c> display as "callable" / "Closure",
    /// so a bare-name check alone would treat them as opaque callables. They are rejected by the
    /// <c>Callable</c> constraint instead and must not reflect.
    /// </summary>
    [Fact]
    public void Reflect_EmptyGenericCallable_IsNotOpaqueCallable()
    {
        foreach (var baseName in new[] { "callable", "Closure" })
        {
            var empty = new GenericCheckedType(
                CheckedTypes.FromSymbol(new BuiltInTypeSymbol(baseName)),
                []);

            CallableSignatureReflection.TryReflect(empty, out var signature)
                .Should().BeFalse($"empty {baseName}<> is not a usable callable shape");
            signature.Should().BeNull();
            GenericTypeArgumentValidator.SatisfiesCallableConstraint(empty).Should().BeFalse();
        }
    }

    [Fact]
    public void FromParameterInfos_PreservesNamesFlagsAndTypes()
    {
        var infos = new List<ParameterInfo>
        {
            new("$name", null, null, IsVariadic: false, IsByReference: false, MemberModifier.None),
            new("$times", null, DefaultValue: new PhpNameAst(), IsVariadic: false, IsByReference: true, MemberModifier.None),
            new("$rest", null, null, IsVariadic: true, IsByReference: false, MemberModifier.None),
        };
        var types = new ICheckedType[] { CheckedTypes.String, CheckedTypes.Int, CheckedTypes.Mixed };

        var signature = CallableSignatureReflection.FromParameterInfos(infos, types, CheckedTypes.Void);

        signature.Parameters.Should().HaveCount(3);
        signature.Parameters[0].Name.Should().Be("name");
        signature.Parameters[0].IsOptional.Should().BeFalse();
        signature.Parameters[1].Name.Should().Be("times");
        signature.Parameters[1].IsOptional.Should().BeTrue();
        signature.Parameters[1].IsByRef.Should().BeTrue();
        signature.Parameters[2].Name.Should().Be("rest");
        signature.Parameters[2].IsVariadic.Should().BeTrue();
        signature.Parameters[2].IsOptional.Should().BeTrue();
        CheckedTypes.AreTypesEqual(signature.ReturnType, CheckedTypes.Void).Should().BeTrue();
    }

    [Fact]
    public void SatisfiesCallableConstraint_AllowsTypeParameterAndNullable()
    {
        var typeParam = CheckedTypes.FromSymbol(
            new GenericTypeParameterSymbol("TCallable", SymbolType.FunctionGenericTypeParameter));
        GenericTypeArgumentValidator.SatisfiesCallableConstraint(typeParam).Should().BeTrue();

        var nullableCallable = new NullableCheckedType(new GenericCheckedType(
            CheckedTypes.FromSymbol(new BuiltInTypeSymbol("callable")),
            [CheckedTypes.Int]));
        GenericTypeArgumentValidator.SatisfiesCallableConstraint(nullableCallable).Should().BeTrue();

        GenericTypeArgumentValidator.SatisfiesCallableConstraint(CheckedTypes.Int).Should().BeFalse();

        var unionOfCallables = CheckedTypes.UnionTypes(
            new CallableCheckedType([], CheckedTypes.Int),
            new CallableCheckedType([], CheckedTypes.String));
        GenericTypeArgumentValidator.SatisfiesCallableConstraint(unionOfCallables).Should().BeTrue();
        GenericTypeArgumentValidator.SatisfiesCallableConstraint(
            CheckedTypes.UnionTypes(unionOfCallables, CheckedTypes.Int)).Should().BeFalse();

        var intersection = new IntersectionCheckedType(
        [
            new CallableCheckedType([CheckedTypes.String], CheckedTypes.Void),
            new CallableCheckedType([CheckedTypes.String, CheckedTypes.Int], CheckedTypes.Void),
        ]);
        GenericTypeArgumentValidator.SatisfiesCallableConstraint(intersection).Should().BeTrue();
    }

    [Fact]
    public void Check_CallUserFunc_InfersReturnAndChecksRestArgs()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func(greet(...), 'Ada', 36);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFunc_WrongRestArgType_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func(greet(...), 1, 36);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFuncArray_NamedBag_AssignsAndReturnsCallableReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func_array(greet(...), ['name' => 'Ada', 'age' => 36]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFuncArray_PositionalBag_AssignsAndReturnsCallableReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func_array(greet(...), ['Ada', 36]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFuncArray_NamedBag_WrongKey_ReportsUnknownProperty()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func_array(greet(...), ['nome' => 'Ada', 'age' => 36]);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFuncArray_PositionalBag_WrongType_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func_array(greet(...), [1, 36]);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFuncArray_NamedBag_OmittingDefaulted_IsAllowed()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age = 0): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func_array(greet(...), ['name' => 'Ada']);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFuncArray_NamedBag_OmittingRequired_ReportsMissingKey()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func_array(greet(...), ['name' => 'Ada']);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerStructRequiredKeyMissing,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFunc_ZeroExtraArgs_InfersReturn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function ping(): string {
                return 'ok';
            }

            function demo(): void {
                string $s = \call_user_func(ping(...));
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFunc_OmittingRequiredRestArg_ReportsMissing()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(string $name): string {
                return $name;
            }

            function demo(): void {
                string $s = \call_user_func(greet(...));
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerMissingArgument,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFuncArray_CallableArgs2Variable_SelectsTupleAndReturnsCallableReturnType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(\CallableArgs2<string, int> $args): void {
                string $s = \call_user_func_array(greet(...), $args);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFuncArray_NamedStructVariable_SelectsStructBag()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct GreetArgs {
                string $name;
                int $age;
            }

            function greet(string $name, int $age): string {
                return $name;
            }

            function demo(GreetArgs $args): void {
                string $s = \call_user_func_array(greet(...), $args);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_CallUserFuncArray_AnnotatedClosure_NamedBag_Assigns()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $s = \call_user_func_array(
                    fn(string $name, int $age): string => $name,
                    ['name' => 'Ada', 'age' => 36]);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_NamedStruct_IsStructurallyAssignableToCompatibleNamedStruct()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct Named {
                string $name;
            }

            struct Person {
                string $name;
                int $age;
            }

            function take(Named $n): void {}

            function demo(): void {
                Person $person = new Person() with [name => 'Ada', age => 36];
                take($person);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_NamedStruct_IsNotStructurallyAssignableWhenRequiredKeyMissing()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            struct Named {
                string $name;
            }

            struct Person {
                string $name;
                int $age;
            }

            function take(Person $p): void {}

            function demo(Named $named): void {
                take($named);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
            Describe(diagnostics.Errors));
    }

    private static void AssertRegistered(IBaseScope global, string name, UtilityBehavior behavior)
    {
        var symbol = global.FindChildSymbolByName(name);
        symbol.Should().BeOfType<BuiltInUtilityTypeSymbol>();
        ((BuiltInUtilityTypeSymbol)symbol!).Behavior.Should().Be(behavior);
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
            if (functionSymbol.GenericParameters.Count > 0)
            {
                state.FunctionGenerics = functionSymbol.GenericParameters;
            }
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

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

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
