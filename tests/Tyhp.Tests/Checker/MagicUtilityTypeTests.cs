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
/// Resolution of <c>__SuperType</c>, <c>__SuperTypeName</c>, <c>__CurrentScope</c>,
/// <c>__CallableThis</c>, <c>__CallableScope</c>, <c>__IndexKeys</c>,
/// <c>__IndexValueType</c>, and <c>__IndexValueTypes</c>.
/// </summary>
[Trait("Category", "Checker")]
public class MagicUtilityTypeTests
{
    [Fact]
    public void MagicUtilities_AreRegisteredInGlobalScope()
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
            var global = (IBaseScope)result.GlobalScope!;

            AssertRegistered(global, "__SuperType", UtilityBehavior.SuperType);
            AssertRegistered(global, "__SuperTypeName", UtilityBehavior.SuperTypeName);
            AssertRegistered(global, "__CurrentScope", UtilityBehavior.CurrentScope);
            AssertRegistered(global, "__CallableThis", UtilityBehavior.CallableThis);
            AssertRegistered(global, "__CallableScope", UtilityBehavior.CallableScope);
            AssertRegistered(global, "__IndexKeys", UtilityBehavior.IndexKeys);
            AssertRegistered(global, "__IndexValueType", UtilityBehavior.IndexValueType);
            AssertRegistered(global, "__IndexValueTypes", UtilityBehavior.IndexValueTypes);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Check_SuperType_IncludesTypeAndParents()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            class Animal {}
            class Dog extends Animal {}

            function demo(__SuperType<Dog> $x): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var type = ResolveParameterDeclaredType(checker, file, "x");
        UnionContains(type, "Dog").Should().BeTrue(type.DisplayName);
        UnionContains(type, "Animal").Should().BeTrue(type.DisplayName);
    }

    [Fact]
    public void Check_SuperType_AcceptsParentObject()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}
            class Dog extends Animal {}

            function take(__SuperType<Dog> $x): void {}

            function demo(): void {
                take(new Animal());
                take(new Dog());
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_SuperType_OfObjectAndNull_IsObject()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__SuperType<object> $a, __SuperType<null> $b): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var a = ResolveParameterDeclaredType(checker, file, "a");
        var b = ResolveParameterDeclaredType(checker, file, "b");
        IsObjectBuiltin(a).Should().BeTrue(a.DisplayName);
        IsObjectBuiltin(b).Should().BeTrue(b.DisplayName);
    }

    [Fact]
    public void Check_DeferredSuperType_SatisfiesClassNameObjectConstraint()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function parentName<T extends object>(T $obj): __ClassName<__SuperType<T>>|false {
                return false;
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_DeferredSuperType_SatisfiesUserObjectBound()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Holder<T extends object> {}

            function demo<T extends object>(Holder<__SuperType<T>> $h): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_SuperTypeName_AcceptsSelfAndParentClassStrings()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}
            class Dog extends Animal {}

            function demo(): void {
                __SuperTypeName<Dog> $self = 'Dog';
                __SuperTypeName<Dog> $parent = 'Animal';
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_SuperTypeName_RejectsDescendantClassString()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}
            class Dog extends Animal {}
            class Puppy extends Dog {}

            function demo(): void {
                __SuperTypeName<Dog> $n = 'Puppy';
            }
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch
            || d.Code == MessageCode.CheckerSymbolNameNotFound);
    }

    [Fact]
    public void Check_CompatibleTypeName_StillAcceptsDescendantNotParent()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Animal {}
            class Dog extends Animal {}
            class Puppy extends Dog {}

            function demo(): void {
                __CompatibleTypeName<Dog> $ok = 'Puppy';
                __CompatibleTypeName<Dog> $no = 'Animal';
            }
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch
            || d.Code == MessageCode.CheckerSymbolNameNotFound);
        UserErrors(diagnostics).Should().NotContain(d =>
            (d.Code == MessageCode.CheckerTypeMismatch || d.Code == MessageCode.CheckerSymbolNameNotFound)
            && d.Message != null
            && d.Message.Contains("Puppy", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_CurrentScope_InstanceAndStaticMethods_AreTheClass()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {
                function instance(): void {
                    __CurrentScope $s = $this;
                }

                static function st(): void {
                    Foo $f = new Foo();
                    __CurrentScope $s = $f;
                }
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_CurrentScope_TopLevel_IsNull()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}

            function demo(): void {
                __CurrentScope $s = null;
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_CurrentScope_TopLevel_RejectsClassInstance()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Foo {}

            function demo(): void {
                __CurrentScope $s = new Foo();
            }
            """);

        UserErrors(diagnostics).Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_CallableThis_BareCallable_IsObjectOrNull()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__CallableThis<callable(string): void> $t): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var type = ResolveParameterDeclaredType(checker, file, "t");
        IsObjectOrNull(type).Should().BeTrue(type.DisplayName);
    }

    [Fact]
    public void Check_CallableScope_BareCallable_IsObjectClassNameOrNull()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            function demo(__CallableScope<callable(string): void> $s): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var type = ResolveParameterDeclaredType(checker, file, "s");
        var members = Flatten(type).ToList();
        members.Any(IsObjectBuiltin).Should().BeTrue(type.DisplayName);
        members.Any(m => m is LiteralCheckedType { Value: null }
            || (m is SimpleCheckedType s && string.Equals(s.ResolvedSymbol.Name, "null", StringComparison.OrdinalIgnoreCase)))
            .Should().BeTrue(type.DisplayName);
        members.Any(m => m.DisplayName.Contains("ClassName", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue(type.DisplayName);
    }

    [Fact]
    public void Check_CallableThis_InvokableObject_IsThatClass()
    {
        var handlerType = ResolveInvokableHandler(out var symbolTree, out var global);
        var resolved = MagicUtilityTypeResolver.ResolveCallableThisCore(handlerType, symbolTree, global);
        UnionContains(resolved, "Handler").Should().BeTrue(resolved.DisplayName);
    }

    [Fact]
    public void Check_CallableScope_InvokableObject_IsThatClass()
    {
        var handlerType = ResolveInvokableHandler(out var symbolTree, out var global);
        var resolved = MagicUtilityTypeResolver.ResolveCallableScopeCore(handlerType, symbolTree, global);
        UnionContains(resolved, "Handler").Should().BeTrue(resolved.DisplayName);
    }

    [Fact]
    public void Check_CallableThis_ClosureTargetContract_ExtractsTThis()
    {
        var (_, file, global, diagnostics) = CompileForChecker("""
            <?tyhp
            class Foo {}
            function demo(): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var foo = FindDeclaredClass(file, "Foo");
        foo.Should().NotBeNull();
        var closure = new ObjectDeclarationSymbol("Closure");
        var callableShape = new CallableCheckedType([], CheckedTypes.Void);
        var thisType = CheckedTypes.FromSymbol(foo!);
        var scopeType = CheckedTypes.FromSymbol(foo!);
        var closureType = new GenericCheckedType(
            CheckedTypes.FromSymbol(closure),
            [callableShape, thisType, scopeType]);

        var utility = (BuiltInUtilityTypeSymbol)((IBaseScope)global)
            .FindChildSymbolByName("__CallableThis")!;
        var symbolTree = new SymbolTree(global);
        var resolved = MagicUtilityTypeResolver.Resolve(
            utility,
            [closureType],
            new CheckerState(),
            symbolTree,
            global,
            (_, _, _, _) => CheckedTypes.Mixed);

        CheckedTypes.AreTypesEqual(resolved, thisType).Should().BeTrue(resolved.DisplayName);
    }

    [Fact]
    public void Check_CallableScope_ClosureTargetContract_ExtractsTScope()
    {
        var (_, file, global, diagnostics) = CompileForChecker("""
            <?tyhp
            class Foo {}
            function demo(): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var foo = FindDeclaredClass(file, "Foo");
        foo.Should().NotBeNull();
        var closure = new ObjectDeclarationSymbol("Closure");
        var callableShape = new CallableCheckedType([], CheckedTypes.Void);
        var thisType = CheckedTypes.FromSymbol(foo!);
        var scopeType = CheckedTypes.FromSymbol(foo!);
        var closureType = new GenericCheckedType(
            CheckedTypes.FromSymbol(closure),
            [callableShape, thisType, scopeType]);

        var utility = (BuiltInUtilityTypeSymbol)((IBaseScope)global)
            .FindChildSymbolByName("__CallableScope")!;
        var symbolTree = new SymbolTree(global);
        var resolved = MagicUtilityTypeResolver.Resolve(
            utility,
            [closureType],
            new CheckerState(),
            symbolTree,
            global,
            (_, _, _, _) => CheckedTypes.Mixed);

        CheckedTypes.AreTypesEqual(resolved, scopeType).Should().BeTrue(resolved.DisplayName);
    }

    [Fact]
    public void Check_CallableThis_DistributesOverUnion()
    {
        var handlerType = ResolveInvokableHandler(out var symbolTree, out var global);
        var union = CheckedTypes.UnionTypes(
            handlerType,
            new CallableCheckedType([], CheckedTypes.Void));
        var resolved = MagicUtilityTypeResolver.ResolveCallableThisCore(union, symbolTree, global);
        UnionContains(resolved, "Handler").Should().BeTrue(resolved.DisplayName);
        IsObjectOrNull(resolved).Should().BeTrue(resolved.DisplayName);
    }

    [Fact]
    public void Check_IndexKeys_IncludesPropertyNamesAndAliases()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                string 'label' as $name = '';
                mixed 0 as $first = null;
            };

            function demo(__IndexKeys<Point> $k): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var type = ResolveParameterDeclaredType(checker, file, "k");
        HasStringLiteral(type, "x").Should().BeTrue(type.DisplayName);
        HasStringLiteral(type, "name").Should().BeTrue(type.DisplayName);
        HasStringLiteral(type, "label").Should().BeTrue(type.DisplayName);
        HasIntLiteral(type, 0).Should().BeTrue(type.DisplayName);
    }

    [Fact]
    public void Check_IndexValueType_LooksUpFieldAndDistributesOverKeyUnion()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                string $y = '';
            };

            function demo(
                __IndexValueType<Point, 'x'> $x,
                __IndexValueType<Point, 'x' | 'y'> $xy
            ): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var x = ResolveParameterDeclaredType(checker, file, "x");
        IsBuiltin(x, "int").Should().BeTrue(x.DisplayName);
        var xy = ResolveParameterDeclaredType(checker, file, "xy");
        UnionContains(xy, "int").Should().BeTrue(xy.DisplayName);
        UnionContains(xy, "string").Should().BeTrue(xy.DisplayName);
    }

    [Fact]
    public void Check_IndexValueTypes_UnionsFieldTypes()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                string $y = '';
            };

            function demo(__IndexValueTypes<Point> $v): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var type = ResolveParameterDeclaredType(checker, file, "v");
        UnionContains(type, "int").Should().BeTrue(type.DisplayName);
        UnionContains(type, "string").Should().BeTrue(type.DisplayName);
    }

    [Fact]
    public void Check_IndexValueType_IntegerAliasKey()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Args = struct {
                string 0 as $name = '';
            };

            function demo(__IndexValueType<Args, 0> $v): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var type = ResolveParameterDeclaredType(checker, file, "v");
        IsBuiltin(type, "string").Should().BeTrue(type.DisplayName);
    }

    private static void AssertRegistered(IBaseScope global, string name, UtilityBehavior behavior)
    {
        var symbol = global.FindChildSymbolByName(name);
        symbol.Should().BeOfType<BuiltInUtilityTypeSymbol>();
        ((BuiltInUtilityTypeSymbol)symbol!).Behavior.Should().Be(behavior);
    }

    private static ICheckedType ResolveInvokableHandler(out SymbolTree symbolTree, out GlobalScope global)
    {
        var (_, file, boundGlobal, diagnostics) = CompileForChecker("""
            <?tyhp
            class Handler {
                public function __invoke(): void {}
            }

            function demo(): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
        var handler = FindDeclaredClass(file, "Handler");
        handler.Should().NotBeNull();
        global = boundGlobal;
        symbolTree = new SymbolTree(global);
        return CheckedTypes.FromSymbol(handler!);
    }

    private static ObjectDeclarationSymbol? FindDeclaredClass(SrcFileAst file, string name)
    {
        var decl = FindAllAst<PhpObjectTypeDeclAst>(file)
            .FirstOrDefault(obj => string.Equals(obj.Identifier, name, StringComparison.Ordinal));
        return decl?.BoundSymbol as ObjectDeclarationSymbol;
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

    private static bool UnionContains(ICheckedType type, string name)
    {
        foreach (var member in Flatten(type))
        {
            var display = member.DisplayName.TrimStart('\\');
            var last = display.Contains('\\', StringComparison.Ordinal)
                ? display[(display.LastIndexOf('\\') + 1)..]
                : display;
            if (string.Equals(last, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(display, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasStringLiteral(ICheckedType type, string value) =>
        Flatten(type).OfType<LiteralCheckedType>().Any(literal =>
            literal.Value is string s && string.Equals(s, value, StringComparison.Ordinal));

    private static bool HasIntLiteral(ICheckedType type, int value) =>
        Flatten(type).OfType<LiteralCheckedType>().Any(literal =>
            literal.Value is int i && i == value
            || literal.Value is long l && l == value);

    private static bool IsBuiltin(ICheckedType type, string name) =>
        Flatten(type).Any(member =>
            member is SimpleCheckedType simple
            && string.Equals(simple.ResolvedSymbol.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool IsObjectBuiltin(ICheckedType type) => IsBuiltin(type, "object");

    private static bool IsObjectOrNull(ICheckedType type)
    {
        var members = Flatten(type).ToList();
        return members.Any(IsObjectBuiltin)
            && members.Any(m => m is LiteralCheckedType { Value: null }
                || (m is SimpleCheckedType s
                    && string.Equals(s.ResolvedSymbol.Name, "null", StringComparison.OrdinalIgnoreCase)));
    }

    private static IEnumerable<ICheckedType> Flatten(ICheckedType type)
    {
        switch (type)
        {
            case UnionCheckedType union:
                foreach (var member in union.Members)
                {
                    foreach (var inner in Flatten(member))
                    {
                        yield return inner;
                    }
                }

                break;
            case NullableCheckedType nullable:
                yield return CheckedTypes.Null;
                foreach (var inner in Flatten(nullable.InnerType))
                {
                    yield return inner;
                }

                break;
            default:
                yield return type;
                break;
        }
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(DiagnosticBag diagnostics) =>
        diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();

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
