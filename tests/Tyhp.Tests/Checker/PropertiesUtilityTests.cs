using System.Linq;
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

/// <summary>
/// <c>__Properties&lt;T&gt;</c> resolves to an optional-key struct of T's instance
/// properties (objects) or record keys (structs).
/// </summary>
[Trait("Category", "Checker")]
public class PropertiesUtilityTests
{
    [Fact]
    public void Check_PropertiesOnClass_IsOptionalStructOfPropertyTypes()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            class Widget {
                public string $name;
                public int $age;
            }

            function demo(__Properties<Widget> $props): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));

        var type = ResolveParameterDeclaredType(checker, file, "props");
        type.Should().BeOfType<StructCheckedType>();
        var structType = (StructCheckedType)type;
        structType.Properties.Should().HaveCount(2);
        structType.Properties.Should().ContainKey("$name");
        structType.Properties.Should().ContainKey("$age");
        structType.Properties.Values.Should().OnlyContain(property => property.IsOptional);
        structType.Properties["$name"].Type.DisplayName.Should().Be("string");
        structType.Properties["$age"].Type.DisplayName.Should().Be("int");
        structType.Properties.Values.Should().OnlyContain(property => !property.Type.IsNullable);
    }

    [Fact]
    public void Check_PropertiesOnStruct_IsOptionalStructOfRecordTypes()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                string $y = '';
            };

            function demo(__Properties<Point> $props): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));

        var type = ResolveParameterDeclaredType(checker, file, "props");
        type.Should().BeOfType<StructCheckedType>();
        var structType = (StructCheckedType)type;
        structType.Properties.Should().ContainKey("$x");
        structType.Properties.Should().ContainKey("$y");
        structType.Properties.Values.Should().OnlyContain(property => property.IsOptional);
        structType.Properties["$x"].Type.DisplayName.Should().Be("int");
    }

    [Fact]
    public void Check_Properties_EmptyArrayAndSubsetAssign()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
                public int $age;
            }

            function demo(): void {
                __Properties<Widget> $empty = [];
                __Properties<Widget> $subset = ['name' => 'Ada'];
                __Properties<Widget> $all = ['name' => 'Ada', 'age' => 36];
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_Properties_ExtraKey_ReportsUnknownProperty()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
            }

            function demo(): void {
                __Properties<Widget> $props = ['nope' => 'x'];
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_Properties_ValueTypeMismatch_ReportsMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
            }

            function demo(): void {
                __Properties<Widget> $props = ['name' => 1];
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_Properties_StructEmptyAndSubsetAssign()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Point = struct {
                int $x = 0;
                string $y = '';
            };

            function demo(): void {
                __Properties<Point> $empty = [];
                __Properties<Point> $subset = ['x' => 1];
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_Properties_IncludesPrivateAndProtected_OmitsStatic()
    {
        var (checker, file, _, diagnostics) = CompileForChecker("""
            <?tyhp
            class Widget {
                public string $name;
                protected int $age;
                private bool $hidden;
                public static string $stat = '';
            }

            function demo(__Properties<Widget> $props): void {}
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));

        var type = (StructCheckedType)ResolveParameterDeclaredType(checker, file, "props");
        type.Properties.Should().ContainKey("$name");
        type.Properties.Should().ContainKey("$age");
        type.Properties.Should().ContainKey("$hidden");
        type.Properties.Should().NotContainKey("$stat");
    }

    [Fact]
    public void Check_Properties_StaticKey_ReportsUnknownProperty()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
                public static string $stat = '';
            }

            function demo(): void {
                __Properties<Widget> $props = ['stat' => 'x'];
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty,
            Describe(diagnostics.Errors));
    }

    [Fact]
    public void Check_Properties_UnboundTypeParameter_DefaultEmptyArrayOk()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function patch<T extends object>(T $object, __Properties<T> $withProperties = []): T {
                return $object;
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_Properties_GenericCall_InfersTAndChecksBag()
    {
        var ok = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
                public int $age;
            }

            function patch<T extends object>(T $object, __Properties<T> $withProperties = []): T {
                return $object;
            }

            function demo(Widget $w): void {
                patch($w, []);
                patch($w, ['name' => 'Ada']);
                patch($w, ['name' => 'Ada', 'age' => 36]);
            }
            """);

        UserErrors(ok).Should().BeEmpty(Describe(UserErrors(ok)));

        var extra = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
            }

            function patch<T extends object>(T $object, __Properties<T> $withProperties = []): T {
                return $object;
            }

            function demo(Widget $w): void {
                patch($w, ['nope' => 1]);
            }
            """);

        extra.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerWithKeywordInvalidProperty,
            Describe(extra.Errors));

        var mismatch = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
            }

            function patch<T extends object>(T $object, __Properties<T> $withProperties = []): T {
                return $object;
            }

            function demo(Widget $w): void {
                patch($w, ['name' => 1]);
            }
            """);

        mismatch.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            Describe(mismatch.Errors));
    }

    [Fact]
    public void Check_Properties_UnionT_DistributesAtCallSite()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class A { public int $a; }
            class B { public string $b; }

            function patch<T extends object>(T $object, __Properties<T> $withProperties = []): T {
                return $object;
            }

            function demo(A|B $obj): void {
                patch($obj, []);
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    [Fact]
    public void Check_Properties_PrimitiveTypeArg_ReportsConstraint()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__Properties<int> $props): void {}
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied);
    }

    [Fact]
    public void Check_Properties_CloneCall_AcceptsSubsetBag()
    {
        var ok = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
            }

            function demo(Widget $w): void {
                $a = clone($w, []);
                $b = clone($w, ['name' => 'Ada']);
                $c = clone(object: $w, withProperties: ['name' => 'Ada']);
            }
            """, phpVersion: "8.5");

        UserErrors(ok).Should().BeEmpty(Describe(UserErrors(ok)));
    }

    [Fact]
    public void Check_PropertyName_StillNameUnion()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {
                public string $name;
            }

            function demo(): void {
                __PropertyName<Widget> $n = 'name';
            }
            """);

        UserErrors(diagnostics).Should().BeEmpty(Describe(UserErrors(diagnostics)));
    }

    private static IReadOnlyList<IDiagnostic> UserErrors(DiagnosticBag diagnostics) =>
        diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

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

    private static DiagnosticBag CompileAndCheck(string content, string phpVersion = "8.2")
    {
        var (_, _, _, diagnostics) = CompileForChecker(content, phpVersion);
        return diagnostics;
    }

    private static (TyhpChecker checker, SrcFileAst file, GlobalScope global, DiagnosticBag diagnostics) CompileForChecker(
        string content,
        string phpVersion = "8.2")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.tyhp");
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: phpVersion, skipChecking: true);
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
