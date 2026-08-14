using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
public class Phase08_5Phase6_7RuleTests
{
    [Fact]
    public void TypeNameAlgebraTypes_AreRegisteredInGlobalScope()
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
                .FindChildSymbolByName("__TypeName");
            symbol.Should().BeOfType<Tyhp.TyhpLang.Binder.Symbols.BuiltInUtilityTypeSymbol>();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Check_UnionTypeLiteral_MatchesUnionTypeName()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __UnionTypeName $name = 'int|string';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_InvalidUnionTypeLiteral_ReportsTypeMismatch()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __UnionTypeName $name = 'not-a-valid-type-name';
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_BaseTypeNameLiteral_MatchesTypeName()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __TypeName $name = 'int';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_CustomTemplateStringType_AcceptsMatchingLiteral()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type ApiMethod = "${'GET'|'POST'}";

            function demo(): void {
                ApiMethod $method = 'GET';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_BareTemplateStringType_AsParameterAndReturnType_DoesNotReport3019Or3020()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo("${'GET'|'POST'}" $method): "${'GET'|'POST'}" {
                return $method;
            }
            """);

        diagnostics.Errors.Should().NotContain(d =>
            d.Code == MessageCode.BinderUnresolvedParameterType ||
            d.Code == MessageCode.BinderUnresolvedReturnType);
    }

    [Fact]
    public void Check_TemplateStringType_AssignableToString()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __NullableBaseTypeName $p = '?int';
                string $s = $p;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_AsType_IntLiteral_ResolvesToInt()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(__AsType<'int'> $value): void {}
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_AsType_IntLiteral_ParameterAssignableToIntReturn()
    {
        // Suite-reds 2026-08-03 #13: `__AsType<'int'>` must resolve to `int` (not the
        // unknown-literal fallback `mixed|struct|void`) so returning it as `int` type-checks.
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function echoValue(__AsType<'int'> $value): int {
                return $value;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_AsType_BareInt_ParameterAssignableToIntReturn()
    {
        // Bare (unquoted) type argument must resolve the same way as the quoted literal
        // `__AsType<'int'>` (Suite-reds 2026-08-03 #13 follow-up gap).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function echoValue(__AsType<int> $value): int {
                return $value;
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_NullableBaseTypeName_MatchesQuestionPrefix()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                __NullableBaseTypeName $n = '?int';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_TemplateStringType_WithLiteralTextAndHole_AcceptsMatchingLiteral()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Wrapped = "pre-${'GET'|'POST'}-post";

            function demo(): void {
                Wrapped $w = 'pre-GET-post';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_TemplateStringType_WithLiteralTextAndHole_RejectsNonMatchingLiteral()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Wrapped = "pre-${'GET'|'POST'}-post";

            function demo(): void {
                Wrapped $w = 'pre-DELETE-post';
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_TemplateStringType_BareIdentifierHole_ResolvesToType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type AnyString = "${string}";

            function demo(): void {
                AnyString $s = 'anything';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_TemplateStringType_PlusQuantifier_MatchesRepeatedHole()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Repeated = "${'ab'}+";

            function demo(): void {
                Repeated $r = 'abab';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_TemplateStringType_StarQuantifier_MatchesEmpty()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Repeated = "${'ab'}*";

            function demo(): void {
                Repeated $r = '';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_TemplateStringType_BoundedRangeQuantifier_MatchesWithinRange()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Bounded = "x${'a'}{2,4}y";

            function demo(): void {
                Bounded $b = 'xaaay';
            }
            """);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_TemplateStringType_BoundedRangeQuantifier_RejectsBelowRange()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Bounded = "x${'a'}{2,4}y";

            function demo(): void {
                Bounded $b = 'xay';
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTypeMismatch);
    }

    [Fact]
    public void Check_TemplateStringMembership_WithinBudget_AcceptsMatchingLiteral()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type ApiMethod = "${'GET'|'POST'}";

            function demo(): void {
                ApiMethod $method = 'GET';
            }
            """, templateStringMaxStates: 256);

        diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Check_TemplateStringMembership_ExceedsBudget_ReportsComplexityDiagnostic()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Pathological = "${string}*${string}*${string}*${string}*";

            function demo(): void {
                Pathological $p = 'abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz';
            }
            """, templateStringMaxStates: 4);

        diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.CheckerTemplateStringMaxStatesExceeded);
    }

    [Fact]
    public void Check_TemplateStringSubtyping_ExceedsBudget_ReportsComplexityDiagnostic()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            type Wide = "${string}*${string}*${string}*${string}*";
            type Narrow = "${string}*${string}*";

            function demo(): void {
                Wide $wide = 'abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz';
                Narrow $narrow = $wide;
            }
            """, templateStringMaxStates: 4);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTemplateStringMaxStatesExceeded);
    }

    private static DiagnosticBag CompileAndCheck(string content, int? templateStringMaxStates = null)
    {
        var (_, _, _, diagnostics) = CompileForChecker(content, templateStringMaxStates);
        return diagnostics;
    }

    private static (TyhpChecker checker, SrcFileAst file, GlobalScope global, DiagnosticBag diagnostics) CompileForChecker(
        string content,
        int? templateStringMaxStates = null)
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
            var checkerOptions = templateStringMaxStates is int maxStates
                ? new CheckerOptions { TemplateStringMaxStates = maxStates }
                : null;
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!, checkerOptions);
            checker.Check(result.ParsedFiles!);
            return (checker, result.ParsedFiles![0], result.GlobalScope!, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
