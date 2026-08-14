using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Optional trailing defaults on callables expand to an intersection of arity facets.
/// </summary>
[Trait("Category", "Checker")]
public class CallableArityFacetTests
{
    [Fact]
    public void ArityFacetExpansion_TrailingDefaults_YieldsPrefixes()
    {
        var prefixes = ArityFacetExpansion.GetValidArityPrefixes(
        [
            (HasDefault: false, IsVariadic: false),
            (HasDefault: true, IsVariadic: false),
        ]);

        prefixes.Should().Equal(1, 2);
    }

    [Fact]
    public void ArityFacetExpansion_AllDefaults_IncludesZero()
    {
        var prefixes = ArityFacetExpansion.GetValidArityPrefixes(
        [
            (HasDefault: true, IsVariadic: false),
            (HasDefault: true, IsVariadic: false),
        ]);

        prefixes.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void ArityFacetExpansion_VariadicOnly_IsZero()
    {
        var prefixes = ArityFacetExpansion.GetValidArityPrefixes(
        [
            (HasDefault: false, IsVariadic: true),
        ]);

        prefixes.Should().Equal(0);
    }

    [Fact]
    public void ArityFacetExpansion_RequiredPlusVariadic_DoesNotExplode()
    {
        var prefixes = ArityFacetExpansion.GetValidArityPrefixes(
        [
            (HasDefault: false, IsVariadic: false),
            (HasDefault: false, IsVariadic: true),
        ]);

        prefixes.Should().Equal(1);
    }

    [Fact]
    public void OptionalSecondParam_AssignableToBothArities()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function greet(string $name, int $times = 1): void {}
            function takeOne(callable<string, void> $fn): void {
                $fn("a");
            }
            function takeTwo(callable<string, int, void> $fn): void {
                $fn("a", 2);
            }
            function main(): void {
                takeOne(greet(...));
                takeTwo(greet(...));
            }
            """);

        errors.Should().BeEmpty(
            "optional trailing param should yield both arity facets: " + Describe(errors));
    }

    [Fact]
    public void AllRequired_NotAssignableToFewerArgs()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function both(string $a, int $b): void {}
            function takeOne(callable<string, void> $fn): void {
                $fn("a");
            }
            function main(): void {
                takeOne(both(...));
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerTypeMismatch
            || e.Code == MessageCode.CheckerIncompatibleArgumentType
            || e.Message.Contains("callable", StringComparison.OrdinalIgnoreCase),
            "required two-arg callable must not assign to one-arg target: " + Describe(errors));
    }

    [Fact]
    public void AllDefaults_IncludesZeroArgFacet()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function maybe(string $label = ""): void {}
            function takeZero(callable<void> $fn): void {
                $fn();
            }
            function takeOne(callable<string, void> $fn): void {
                $fn("x");
            }
            function main(): void {
                takeZero(maybe(...));
                takeOne(maybe(...));
            }
            """);

        errors.Should().BeEmpty(
            "all-default params should include zero-arg facet: " + Describe(errors));
    }

    [Fact]
    public void MoreFacetsThanTarget_SatisfiesTheTargetIntersection()
    {
        // Three optional-arity facets must satisfy a two-facet target: every arity the target
        // requires is present, even though no single facet covers both on its own.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            type Greeter = callable<string, int, void> & callable<string, void>;
            function accept(Greeter $fn): void {
                $fn("a");
            }
            function greet(string $name, int $times = 1, bool $loud = false): void {}
            function main(): void {
                accept(greet(...));
            }
            """);

        errors.Should().BeEmpty(
            "a superset of facets should satisfy the annotated intersection: " + Describe(errors));
    }

    [Fact]
    public void TrailingVariadic_KeepsTheSingleExtraArgumentFacet()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function joinAll(string ...$parts): void {}
            function takeZero(callable<void> $fn): void {
                $fn();
            }
            function takeOne(callable<string, void> $fn): void {
                $fn("x");
            }
            function main(): void {
                takeZero(joinAll(...));
                takeOne(joinAll(...));
            }
            """);

        errors.Should().BeEmpty(
            "a variadic parameter must still produce a one-argument facet: " + Describe(errors));
    }

    [Fact]
    public void HandWrittenFacets_InvokeUsesTheMatchingFacetReturnType()
    {
        // Synthesized facets share a return type, but a hand-written intersection need not, so the
        // invoke's argument count has to pick the facet.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            type Pick = callable<string, int> & callable<string, string, bool>;
            function apply(Pick $fn): void {
                int $one = $fn("a");
                bool $two = $fn("a", "b");
            }
            """);

        errors.Should().BeEmpty(
            "each arity should yield its own facet's return type: " + Describe(errors));
    }

    [Fact]
    public void UserWrittenCallableIntersection_IsAllowed()
    {
        // Prefer a type alias so each callable's generics are closed before `&` (avoids
        // ambiguous `callable<…, void & …>` parses if prediction mis-reads past `>`).
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            type Greeter = callable<string, int, void> & callable<string, void>;
            function accept(Greeter $fn): void {
                $fn("a");
            }
            function greet(string $name, int $times = 1): void {}
            function main(): void {
                accept(greet(...));
            }
            """);

        errors.Should().BeEmpty(
            "callable intersection via type alias should type-check: " + Describe(errors));
    }

    [Fact]
    public void ClosureWithDefault_AssignableToFewerArgTarget()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function takeOne(callable<string, void> $fn): void {
                $fn("a");
            }
            function main(): void {
                takeOne(function (string $name, int $n = 0): void {});
            }
            """);

        errors.Should().BeEmpty(
            "closure with default should assign to fewer-arg callable: " + Describe(errors));
    }

    [Fact]
    public void InvokeIntersectionTypedCallable_UsesMatchingFacet()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function main(): void {
                $fn = function (string $name, int $n = 0): void {};
                $fn("a");
                $fn("a", 2);
            }
            """);

        errors.Should().BeEmpty(
            "invoke should select matching arity facet: " + Describe(errors));
    }

    [Fact]
    public void ContextualClosure_PicksFacetFromIntersectionExpectedType()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            type Greeter = callable<string, int, void> & callable<string, void>;
            function take(Greeter $fn): void {
                $fn("a");
            }
            function main(): void {
                take(function ($name): void {});
            }
            """);

        errors.Should().BeEmpty(
            "closure params should infer from matching intersection facet: " + Describe(errors));
    }

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
                .Where(e => e.FileName is not null
                    && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string Describe(IEnumerable<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));
}
