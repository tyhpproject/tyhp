using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Generic struct declarations, use-site substitution, and extends-with-args.
/// </summary>
[Trait("Category", "Checker")]
public class GenericStructTests
{
    [Fact]
    public void GenericStruct_BindsTypeParameters()
    {
        var (errors, globalScope) = CompileAndBind("""
            <?tyhp
            namespace Test;
            struct Box<T> {
                T $value;
            }
            """);

        errors.Should().BeEmpty("generic struct should bind: " + Describe(errors));
        var box = FindStruct(globalScope!, "Box");
        box.GenericParameters.Should().HaveCount(1);
        box.GenericParameters[0].Name.Should().Be("T");
    }

    [Fact]
    public void GenericStruct_PropertyAccessSubstitutesTypeArgument()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            struct Box<T> {
                T $value;
            }
            function read(Box<int> $box): int {
                return $box->value;
            }
            """);

        errors.Should().BeEmpty(
            "Box<int>->value must be int: " + Describe(errors));
    }

    [Fact]
    public void GenericStruct_WithKeywordChecksSubstitutedType()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            struct Box<T> {
                T $value;
            }
            function make(): Box<int> {
                return new Box<int>() with [value => 42];
            }
            """);

        errors.Should().BeEmpty(
            "with [value => 42] must accept int for Box<int>: " + Describe(errors));
    }

    [Fact]
    public void GenericStruct_WithKeywordRejectsIncompatibleType()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            struct Box<T> {
                T $value;
            }
            function make(): Box<string> {
                return new Box<string>() with [value => 42];
            }
            """);

        errors.Should().NotBeEmpty(
            "with [value => 42] must reject int for Box<string>: " + Describe(errors));
    }

    [Fact]
    public void GenericStruct_ExtendsSubstitutesInheritedProperty()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            struct ParentArgs<T1> {
                T1 0 as $arg1;
            }
            struct ChildArgs<T1, T2> extends ParentArgs<T1> {
                T2 1 as $arg2;
            }
            function read(ChildArgs<string, int> $args): string {
                return $args->arg1;
            }
            function readSecond(ChildArgs<string, int> $args): int {
                return $args->arg2;
            }
            """);

        errors.Should().BeEmpty(
            "ChildArgs<string,int> inherited arg1 must be string: " + Describe(errors));
    }

    [Fact]
    public void GenericStruct_ConstraintRejectsBadArgument()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            struct Foo<T extends string> {
                T $value;
            }
            function bad(Foo<int> $foo): void {}
            """);

        errors.Should().NotBeEmpty(
            "Foo<int> must fail T extends string: " + Describe(errors));
    }

    [Fact]
    public void CallableArgs_StyleNumericAliasesWork()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            struct PosArgs1<T1> {
                T1 0 as $_1;
            }
            struct PosArgs2<T1, T2> extends PosArgs1<T1> {
                T2 1 as $_2;
            }
            function call(PosArgs2<string, int> $args): string {
                int $n = $args->_2;
                return $args->_1;
            }
            """);

        errors.Should().BeEmpty(
            "positional generic structs must type-check: " + Describe(errors));
    }

    /// <summary>
    /// The <c>CallableArgs</c> chain lives in ExtStandard.tyhpdef, so its `extends` arguments are
    /// resolved in a different file's scope than the access site — the path that rebinds a
    /// cross-file annotation and must not be handed an immutable checker state.
    /// </summary>
    [Fact]
    public void GenericStruct_DeclaredInTyhpdef_SubstitutesAcrossFiles()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function read(\CallableArgs2<string, int> $args): string {
                int $second = $args->_2;
                return $args->_1;
            }
            """);

        errors.Should().BeEmpty(
            "CallableArgs2<string,int> from ExtStandard must substitute: " + Describe(errors));
    }

    [Fact]
    public void GenericStruct_DeclaredInTyhpdef_RejectsWrongPropertyType()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function read(\CallableArgs2<string, int> $args): int {
                return $args->_1;
            }
            """);

        errors.Should().NotBeEmpty(
            "CallableArgs2<string,int>->_1 is string, not int: " + Describe(errors));
    }

    /// <summary>
    /// Passing a generic struct where a bare <c>array</c> is expected materializes its shape through
    /// the whole <c>extends</c> chain. A regression in that walk manifests as a stack overflow, which
    /// takes the test host down rather than producing a diagnostic.
    /// </summary>
    [Fact]
    public void GenericStruct_ShapeForArrayAssignability_TerminatesOnExtendsChain()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            struct Chain1<T1> {
                T1 0 as $_1;
            }
            struct Chain2<T1, T2> extends Chain1<T1> {
                T2 1 as $_2;
            }
            function takesArray(array $args): void {}
            function pass(Chain2<int, string> $args): void {
                takesArray($args);
            }
            """);

        errors.Should().BeEmpty(
            "a generic struct is assignable to bare array: " + Describe(errors));
    }

    [Fact]
    public void GenericStruct_SameDeclaration_IncompatibleTypeArgs_Rejected()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            struct Box<T> {
                T $value;
            }
            function takeIntBox(Box<int> $box): void {}
            function pass(Box<string> $box): void {
                takeIntBox($box);
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerIncompatibleArgumentType,
            "Box<string> must not be assignable to Box<int>: " + Describe(errors));
    }

    [Fact]
    public void GenericStruct_SameDeclaration_MatchingTypeArgs_Accepted()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            struct Box<T> {
                T $value;
            }
            function takeIntBox(Box<int> $box): void {}
            function pass(Box<int> $box): void {
                takeIntBox($box);
            }
            """);

        errors.Should().BeEmpty(
            "Box<int> must be assignable to Box<int>: " + Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static ObjectDeclarationSymbol FindStruct(GlobalScope global, string name) =>
        EnumerateScopes(global)
            .SelectMany(scope => scope.GetAllChildSymbols())
            .OfType<ObjectDeclarationSymbol>()
            .First(symbol => symbol.IsStruct
                && string.Equals(symbol.Name, name, StringComparison.Ordinal));

    private static IEnumerable<IBaseScope> EnumerateScopes(IBaseScope root)
    {
        yield return root;
        foreach (var child in root.GetAllChildScopes())
        {
            foreach (var descendant in EnumerateScopes(child))
            {
                yield return descendant;
            }
        }
    }

    private static (IReadOnlyList<IDiagnostic> Errors, GlobalScope? Scope)
        CompileAndBind(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".tyhp");
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
            var unexpected = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            return (unexpected, result.GlobalScope);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort */ }
        }
    }

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".tyhp");
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

            return result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* best-effort */ }
        }
    }
}
