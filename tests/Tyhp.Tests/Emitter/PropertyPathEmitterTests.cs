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
/// Story 16 Phase 1 — emit <c>new \Tyhp\PropertyPath(...)</c> for PropertyPath call arguments.
/// </summary>
[Trait("Category", "Emitter")]
public class PropertyPathEmitterTests
{
    [Fact]
    public void Emit_SingleSegment_RewritesToPropertyPath()
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

        php.Should().Contain(@"new \Tyhp\PropertyPath(\User::class, 'string', ['name'], fn(");
        php.Should().Contain("$u->name");
        php.Should().NotContain("take(fn(");
    }

    [Fact]
    public void Emit_MultiSegment_EmitsPathArray()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Address {
                public string $city;
            }
            class User {
                public Address $address;
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(): void {
                take(fn ($u) => $u->address->city);
            }
            """);

        php.Should().Contain(@"new \Tyhp\PropertyPath(");
        php.Should().Contain("['address', 'city']");
        php.Should().Contain("$u->address->city");
    }

    [Fact]
    public void Emit_NullSafeChain_PreservesNullSafeInCallable()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Address {
                public string $city;
            }
            class User {
                public ?Address $address;
            }
            function take(\Tyhp\PropertyPath<User, ?string> $path): void {}
            function demo(): void {
                take(fn ($u) => $u?->address?->city);
            }
            """);

        php.Should().Contain(@"new \Tyhp\PropertyPath(");
        php.Should().Contain("['address', 'city']");
        php.Should().Contain("$u?->address?->city");
        php.Should().Contain("'?string'", "a null-safe chain must not advertise a non-nullable result type");
        php.Should().Contain(
            "nullSafeFlags: [true, true]",
            "the expression tree must record which segments were null-safe");
    }

    [Fact]
    public void Emit_GenericSourceParameter_ErasesInsteadOfSpellingTypeParameter()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public string $name;
            }
            function pick<T>(\Tyhp\PropertyPath<T, string> $path): void {}
            function demo(): void {
                pick<User>(fn ($u) => $u->name);
            }
            """);

        php.Should().Contain(@"new \Tyhp\PropertyPath(");
        php.Should().NotContain(@"\T::class");
        php.Should().NotContain("('T'");
    }

    [Fact]
    public void Emit_RequiresLambdaPackage()
    {
        var (_, required) = CompileAndEmitWithPackages("""
            <?tyhp
            class User {
                public string $name;
            }
            function take(\Tyhp\PropertyPath<User, string> $path): void {}
            function demo(): void {
                take(fn ($u) => $u->name);
            }
            """);

        required.Should().Contain("tyhp/lambda");
    }

    [Fact]
    public void Emit_PropertyPathToClosure_ExtractsCallable()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {
                public string $name;
            }
            function takePath(\Tyhp\PropertyPath<User, string> $path): void {}
            function takeClosure(\Closure $c): void {}
            function demo(\Tyhp\PropertyPath<User, string> $path): void {
                takeClosure($path);
            }
            """);

        php.Should().Contain("takeClosure($path->callable)");
        php.Should().NotContain("takeClosure($path);");
    }

    private static string CompileAndEmit(string tyhp)
        => CompileAndEmitWithPackages(tyhp).Php;

    private static (string Php, IReadOnlyCollection<string> RequiredPackages) CompileAndEmitWithPackages(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "propertypath.tyhp");
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
                inferredClosureSignatures: result.InferredClosureSignatures);
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
