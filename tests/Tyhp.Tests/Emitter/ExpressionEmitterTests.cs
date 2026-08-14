using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 16 Phase 2 — emit <c>new \Tyhp\Expression(...)</c> for Expression call arguments.
/// </summary>
[Trait("Category", "Emitter")]
public class ExpressionEmitterTests
{
    [Fact]
    public void Emit_BinaryComparison_EmitsBinaryAndPropertyAccess()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public int $age;
            }
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function demo(): void {
                take(fn ($u) => $u->age > 18);
            }
            """);

        php.Should().Contain(@"new \Tyhp\Expression(");
        php.Should().Contain(@"new \Tyhp\Expression\BinaryExpression(");
        php.Should().Contain(@"new \Tyhp\Expression\PropertyAccessExpression(");
        php.Should().Contain(@"new \Tyhp\Expression\ConstantExpression(");
        php.Should().Contain(@"new \Tyhp\Expression\ParameterExpression(");
        php.Should().Contain("'>'");
        php.Should().Contain("18");
        php.Should().Contain("callable:");
        php.Should().Contain("returnType:");
    }

    [Fact]
    public void Emit_MethodCall_EmitsMethodCallExpression()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public function getFullName(): string { return ""; }
            }
            function take(\Tyhp\Expression<User, string> $sel): void {}
            function demo(): void {
                take(fn ($u) => $u->getFullName());
            }
            """);

        php.Should().Contain(@"new \Tyhp\Expression\MethodCallExpression(");
        php.Should().Contain("'getFullName'");
        php.Should().Contain(@"new \Tyhp\Expression\ParameterExpression(");
    }

    [Fact]
    public void Emit_Capture_EmitsConstantExpressionWithVariable()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public int $age;
            }
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function demo(): void {
                int $minAge = 18;
                take(fn ($u) => $u->age > $minAge);
            }
            """);

        php.Should().Contain(@"new \Tyhp\Expression\ConstantExpression($minAge");
        php.Should().Contain(@"new \Tyhp\Expression\BinaryExpression(");
    }

    [Fact]
    public void Emit_ExpressionToClosure_ExtractsCallable()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public int $age;
            }
            function takeExpr(\Tyhp\Expression<User, bool> $pred): void {}
            function takeClosure(\Closure $c): void {}
            function demo(\Tyhp\Expression<User, bool> $pred): void {
                takeClosure($pred);
            }
            """);

        php.Should().Contain("takeClosure($pred->callable)");
        php.Should().NotContain("takeClosure($pred);");
    }

    [Fact]
    public void Emit_RequiresLambdaPackage()
    {
        var (_, required) = CompileAndEmitWithPackages("""
            <?tyhp
            class User {
                public int $age;
            }
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function demo(): void {
                take(fn ($u) => $u->age > 18);
            }
            """);

        required.Should().Contain("tyhp/lambda");
    }

    [Fact]
    public void Emit_StaticMethodCall_EmitsStaticMethodCallExpression()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public static function create(): User { return new User(); }
            }
            function take(\Tyhp\Expression<User, User> $sel): void {}
            function demo(): void {
                take(fn ($u) => User::create());
            }
            """);

        php.Should().Contain(@"new \Tyhp\Expression\StaticMethodCallExpression(");
        php.Should().Contain("'create'");
        php.Should().Contain("User::class");
    }

    [Fact]
    public void Emit_ClassConstant_EmitsConstantExpression()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public const int FLAG = 1;
            }
            function take(\Tyhp\Expression<User, int> $sel): void {}
            function demo(): void {
                take(fn ($u) => User::FLAG);
            }
            """);

        php.Should().Contain(@"new \Tyhp\Expression\ConstantExpression(");
        php.Should().Contain("User::FLAG");
    }

    [Fact]
    public void Emit_PropertyPathStillUsesPropertyPathHelper()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public string $name;
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(): void {
                take(fn ($u) => $u->name);
            }
            """);

        php.Should().Contain(@"new \Tyhp\PropertyPath(");
        php.Should().NotContain(@"new \Tyhp\Expression(");
    }

    [Fact]
    public void Emit_MultiParameterComparator_EmitsTwoParameterExpressions()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public string $lastName;
            }
            function sortBy(\Tyhp\Expression<User, User, int> $cmp): void {}
            function demo(): void {
                sortBy(fn ($a, $b) => $a->lastName <=> $b->lastName);
            }
            """);

        php.Should().Contain(@"new \Tyhp\Expression(");
        php.Should().Contain(@"new \Tyhp\Expression\ParameterExpression(");
        php.Should().Contain("'a'");
        php.Should().Contain("'b'");
        php.Should().Contain("'<=>'");
        php.Should().Contain("returnType:");
        php.Should().Contain("'int'");
    }

    [Fact]
    public void Emit_Instanceof_EmitsInstanceofExpression()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public mixed $value;
            }
            function take(\Tyhp\Expression<User, bool> $pred): void {}
            function demo(): void {
                take(fn ($u) => $u->value is int);
            }
            """);

        php.Should().Contain(@"new \Tyhp\Expression\InstanceofExpression(");
        php.Should().Contain("'int'");
        php.Should().Contain(@"\Tyhp\Type::is($u->value, \Tyhp\Type::int())");
        php.Should().NotContain("instanceof int");
        php.Should().NotContain(@"new \Tyhp\Expression\BinaryExpression(");
    }

    [Fact]
    public void Emit_InstanceofClass_EmitsClassTarget()
    {
        var php = CompileAndEmit("""
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

        php.Should().Contain(@"new \Tyhp\Expression\InstanceofExpression(");
        php.Should().Contain("Address::class");
    }

    [Fact]
    public void Emit_NamespacedFreeFunction_RewritesExpressionTree()
    {
        var php = CompileAndEmit("""
            <?tyhp
            namespace App;
            class User {
                public string $lastName;
            }
            function sortBy(\Tyhp\Expression<User, User, int> $cmp): void {}
            function demo(): void {
                sortBy(fn ($a, $b) => $a->lastName <=> $b->lastName);
            }
            """);

        php.Should().Contain(@"new \Tyhp\Expression(");
        php.Should().Contain(@"new \Tyhp\Expression\ParameterExpression('a'");
        php.Should().Contain(@"new \Tyhp\Expression\ParameterExpression('b'");
        php.Should().NotContain("sortBy(fn(");
    }

    private static string CompileAndEmit(string tyhp)
        => CompileAndEmitWithPackages(tyhp).Php;

    private static (string Php, IReadOnlyCollection<string> RequiredPackages) CompileAndEmitWithPackages(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "expression.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject();
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                inferredClosureSignatures: result.InferredClosureSignatures,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            var php = string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
            return (php, context.RequiredPackages);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.4",
            })
            .Build();
        return new Project(configuration);
    }
}
