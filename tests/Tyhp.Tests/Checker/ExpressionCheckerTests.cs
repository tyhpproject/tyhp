using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 16 Phase 2 — <c>Expression&lt;T, R&gt;</c> call-argument validation (TYHP4322–4324).
/// </summary>
[Trait("Category", "Checker")]
public class ExpressionCheckerTests
{
    [Fact]
    public void Check_InlineBinaryExpression_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public int $age;
            }
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function demo(): void {
                take(fn ($u) => $u->age > 18);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerExpressionRequiresInlineFn
                || d.Code == MessageCode.CheckerExpressionUnsupportedNode
                || d.Code == MessageCode.CheckerExpressionCapturedVarUndefined,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_InlineMethodCall_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public function getFullName(): string { return ""; }
            }
            function take(\Tyhp\Expression<User, string> $sel): void {}
            function demo(): void {
                take(fn ($u) => $u->getFullName());
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerExpressionRequiresInlineFn
                || d.Code == MessageCode.CheckerExpressionUnsupportedNode
                || d.Code == MessageCode.CheckerExpressionCapturedVarUndefined,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_ForwardedExpressionValue_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public int $age;
            }
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function forward(\Tyhp\Expression<User, bool> $pred): void {
                take($pred);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerExpressionRequiresInlineFn
                || d.Code == MessageCode.CheckerExpressionUnsupportedNode,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_UserDefinedExpressionClass_NotTreatedAsLambdaType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;
            class Expression {
                public string $raw;
            }
            function take(\App\Expression $expr): void {}
            function demo(\App\Expression $expr): void {
                take($expr);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerExpressionRequiresInlineFn
                || d.Code == MessageCode.CheckerExpressionUnsupportedNode,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_NonFnArgument_Reports4323()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public int $age;
            }
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function demo(\Closure $c): void {
                take($c);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerExpressionRequiresInlineFn);
    }

    [Fact]
    public void Check_StatementClosure_Reports4323()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public int $age;
            }
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function demo(): void {
                take(function ($u) { return $u->age > 18; });
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerExpressionRequiresInlineFn);
    }

    [Fact]
    public void Check_AwaitBody_Reports4322()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public async function load(): \Tyhp\Promise<User> { return $this; }
            }
            function take(\Tyhp\Expression<User, mixed> $pred): void {}
            function demo(): void {
                take(fn ($u) => await $u->load());
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerExpressionUnsupportedNode);
    }

    [Fact]
    public void Check_NestedFnBody_Reports4322()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public int $age;
            }
            function take(\Tyhp\Expression<User, mixed> $pred): void {}
            function demo(): void {
                take(fn ($u) => (fn ($x) => $x->age)($u));
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerExpressionUnsupportedNode);
    }

    [Fact]
    public void Check_AssignmentBody_Reports4322()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public int $age;
            }
            function take(\Tyhp\Expression<User, int> $pred): void {}
            function demo(): void {
                take(fn ($u) => $u->age = 18);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerExpressionUnsupportedNode);
    }

    [Fact]
    public void Check_UnassignedCapture_Reports4324()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public int $age;
            }
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function demo(): void {
                int $minAge;
                take(fn ($u) => $u->age > $minAge);
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerExpressionCapturedVarUndefined);
    }

    [Fact]
    public void Check_StaticMethodCall_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public static function create(): User { return new User(); }
            }
            function take(\Tyhp\Expression<User, User> $sel): void {}
            function demo(): void {
                take(fn ($u) => User::create());
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerExpressionUnsupportedNode,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_ClassConstant_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public const int FLAG = 1;
            }
            function take(\Tyhp\Expression<User, int> $sel): void {}
            function demo(): void {
                take(fn ($u) => User::FLAG);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerExpressionUnsupportedNode,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_NullSafeMethodCall_Reports4322()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public function name(): string { return ""; }
            }
            function take(\Tyhp\Expression<?User, ?string> $sel): void {}
            function demo(): void {
                take(fn ($u) => $u?->name());
            }
            """);

        diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerExpressionUnsupportedNode);
    }

    [Fact]
    public void Check_MultiParameterComparator_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public string $lastName;
            }
            function sortBy(\Tyhp\Expression<User, User, int> $cmp): void {}
            function demo(): void {
                sortBy(fn ($a, $b) => $a->lastName <=> $b->lastName);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericArgumentCountMismatch
                || d.Code == MessageCode.CheckerExpressionRequiresInlineFn
                || d.Code == MessageCode.CheckerExpressionUnsupportedNode,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_ZeroParameterExpression_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function take(\Tyhp\Expression<int> $expr): void {}
            function demo(): void {
                take(fn () => 42);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericArgumentCountMismatch
                || d.Code == MessageCode.CheckerExpressionUnsupportedNode,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_InstanceofBody_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public mixed $value;
            }
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function demo(): void {
                take(fn ($u) => $u->value is int);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerExpressionUnsupportedNode,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_GenericQueryBuilderSelect_InfersInlineFn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public string $firstName;
            }
            class QueryBuilder<T> {
                public function select<R>(\Tyhp\Expression<T, R> $selector): static {
                    return $this;
                }
            }
            function demo(): void {
                $q = new QueryBuilder<User>();
                $q->select(fn ($u) => $u->firstName);
            }
            """);

        var unexpected = diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
        unexpected.Should().BeEmpty(
            string.Join(", ", unexpected.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_GenericQueryBuilderSelect_WithUseImport_InfersInlineFn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;
            use \Tyhp\Expression;
            class User {
                public string $firstName;
            }
            class QueryBuilder<T> {
                public function where(Expression<T, bool> $predicate): static {
                    return $this;
                }
                public function select<R>(Expression<T, R> $selector): static {
                    return $this;
                }
            }
            function demo(): void {
                $q = new QueryBuilder<User>();
                $q->select(fn ($u) => $u->firstName);
            }
            """);

        var unexpected = diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
        unexpected.Should().BeEmpty(
            string.Join(", ", unexpected.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Fact]
    public void Check_GenericQueryBuilderSelect_ChainedAfterWhere_InfersInlineFn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace App;
            use \Tyhp\Expression;
            use \Tyhp\Expression\ExpressionVisitor;
            class User {
                public string $firstName;
                public string $lastName;
            }
            class QueryBuilder<T> {
                public function where(Expression<T, bool> $predicate): static {
                    return $this;
                }
                public function select<R>(Expression<T, R> $selector): static {
                    mixed $visited = (new SqlWhereVisitor())->visit($selector->body);
                    $this->selectColumn = $visited is string ? $visited : null;
                    return $this;
                }
                public function sortBy(Expression<T, T, int> $comparator): static {
                    return $this;
                }
                private ?string $selectColumn = null;
            }
            class SqlWhereVisitor extends ExpressionVisitor {
            }
            function demo(): void {
                new QueryBuilder<User>()
                    ->where(fn ($u) => $u->firstName !== $u->firstName)
                    ->select(fn ($u) => $u->firstName)
                    ->sortBy(fn ($a, $b) => $a->lastName <=> $b->lastName);
            }
            """);

        var unexpected = diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
        unexpected.Should().BeEmpty(
            string.Join("; ", unexpected.Select(e =>
                $"{e.Code}: {e.Message} @ {e.FileName}:{e.Line}:{e.Column}")));
    }

    [Fact]
    public void Check_InstanceofClassBody_NoError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class User {
                public mixed $value;
            }
            class Address {}
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function demo(): void {
                take(fn ($u) => $u->value instanceof Address);
            }
            """);

        diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerExpressionUnsupportedNode,
            string.Join(", ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
    }

    [Theory]
    [InlineData("expression_trees/ExpressionTrees.tyhp")]
    [InlineData("expression_trees/TypeSafeQueryBuilder.tyhp")]
    public void Check_Phase3Fixtures_NoExpressionErrors(string relativePath)
    {
        var filePath = Path.Combine(TestFileManager.GetTestDataDirectory(), "ValidTyhp", relativePath);
        File.Exists(filePath).Should().BeTrue(filePath);

        var content = File.ReadAllText(filePath);
        var diagnostics = CompileAndCheck(content);

        var unexpected = diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
        unexpected.Should().BeEmpty(
            string.Join(", ", unexpected.Select(e => $"{e.Code}: {e.Message}")));
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
                PhpVersion = "8.4",
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
