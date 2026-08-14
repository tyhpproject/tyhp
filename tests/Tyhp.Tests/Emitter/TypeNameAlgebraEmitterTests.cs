using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class TypeNameAlgebraEmitterTests
{
    [Fact]
    public void Emit_TypeNameParameterAndReturn_EraseToString()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function make(__TypeName $name): __TypeName {
                return $name;
            }
            """);

        php.Should().Contain("function make(string $name): string");
        php.Should().NotContain("__TypeName");
        php.Should().NotContain("\\__TypeName");
    }

    [Theory]
    [InlineData("__BaseTypeName")]
    [InlineData("__NullableBaseTypeName")]
    [InlineData("__BaseUnionTypeName")]
    [InlineData("__UnionTypeName")]
    [InlineData("__BaseIntersectTypeName")]
    [InlineData("__IntersectTypeName")]
    [InlineData("__NotNullableUnionTypeName")]
    [InlineData("__NotNullableIntersectTypeName")]
    [InlineData("__NotNullableTypeName")]
    [InlineData("__TypeName")]
    [InlineData("__NonMatchingStringType")]
    public void Emit_BareTypeNameAlgebraParameter_ErasesToString(string tyhpType)
    {
        var php = CompileAndEmit(
            "<?tyhp\nfunction take(" + tyhpType + " $name): void {}\n");

        php.Should().Contain("function take(string $name): void");
        php.Should().NotContain(tyhpType);
        php.Should().NotContain("\\" + tyhpType);
    }

    [Theory]
    [InlineData("__AsTypeName<int>")]
    [InlineData("__AsNotNullableTypeName<'int'>")]
    [InlineData("__AsNullableTypeName<'int'>")]
    public void Emit_AsTypeNameConverters_EraseToString(string tyhpType)
    {
        var php = CompileAndEmit(
            "<?tyhp\nfunction take(" + tyhpType + " $name): void {}\n");

        php.Should().Contain("function take(string $name): void");
        php.Should().NotContain("__AsTypeName");
        php.Should().NotContain("__AsNotNullableTypeName");
        php.Should().NotContain("__AsNullableTypeName");
    }

    [Fact]
    public void Emit_TypeNameProperty_ErasesToString()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public __TypeName $name;
                public function __construct(__UnionTypeName $name) {
                    $this->name = $name;
                }
            }
            """);

        php.Should().Contain("public string $name");
        php.Should().Contain("function __construct(string $name)");
        php.Should().NotContain("__TypeName");
        php.Should().NotContain("__UnionTypeName");
    }

    [Fact]
    public void Emit_NullableAndUnionTypeName_CollapseToString()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function a(?__TypeName $t): void {}
            function b(__TypeName|null $t): void {}
            function c(__TypeName|__UnionTypeName $t): void {}
            """);

        php.Should().Contain("function a(?string $t): void");
        php.Should().Contain("function b(?string $t): void");
        php.Should().Contain("function c(string $t): void");
        php.Should().NotContain("__TypeName");
        php.Should().NotContain("__UnionTypeName");
    }

    [Theory]
    [InlineData("'int'", "int")]
    [InlineData("'string'", "string")]
    [InlineData("'bool'", "bool")]
    [InlineData("'float'", "float")]
    [InlineData("'array'", "array")]
    [InlineData("'object'", "object")]
    [InlineData("'mixed'", "mixed")]
    [InlineData("'struct'", "array")]
    [InlineData("'null'", "null")]
    public void Emit_AsTypeParameter_SpellsResolvedTypeNotString(string typeArg, string phpHint)
    {
        var php = CompileAndEmit(
            "<?tyhp\nfunction take(__AsType<" + typeArg + "> $value): void {}\n");

        php.Should().Contain($"function take({phpHint} $value): void");
        php.Should().NotContain("__AsType");
        php.Should().NotContain("\\__AsType");
    }

    [Fact]
    public void Emit_AsTypeUnknownLiteral_FallsBackToMixed()
    {
        // Checker ResolveAsType maps non-builtin literals to mixed|struct|void → emit mixed.
        var php = CompileAndEmit("""
            <?tyhp
            function take(__AsType<'NotABuiltin'> $value): void {}
            """);

        php.Should().Contain("function take(mixed $value): void");
        php.Should().NotContain("__AsType");
    }

    [Fact]
    public void Emit_AsTypeReturn_SpellsResolvedType()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function identity(__AsType<'int'> $value): __AsType<'int'> {
                return $value;
            }
            """);

        php.Should().Contain("function identity(int $value): int");
        php.Should().NotContain("__AsType");
    }

    [Fact]
    public void Emit_TypeNameParameter_IsCallableFromPhpWithString()
    {
        var files = CompileToFiles("""
            <?tyhp
            namespace Probe;

            class Names {
                public static function echoName(__TypeName $name): string {
                    return $name;
                }
            }
            """);

        var joined = string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));
        joined.Should().Contain("function echoName(string $name): string");
        joined.Should().NotContain("__TypeName");

        var output = EmittedPhpRunner.Run(files, """
            echo \Probe\Names::echoName('int');
            """);

        output.Trim().Should().Be("int");
    }

    [Fact]
    public void Emit_AsTypeParameter_IsCallableFromPhpWithResolvedType()
    {
        var files = CompileToFiles("""
            <?tyhp
            namespace Probe;

            class Holder {
                public static function echoValue(__AsType<'int'> $value): int {
                    return $value;
                }
            }
            """);

        var joined = string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));
        joined.Should().Contain("function echoValue(int $value): int");
        joined.Should().NotContain("__AsType");

        var output = EmittedPhpRunner.Run(files, """
            echo \Probe\Holder::echoValue(42);
            """);

        output.Trim().Should().Be("42");
    }

    [Fact]
    public void Emit_AsTypeParameter_BareBuiltin_IsCallableFromPhpWithResolvedType()
    {
        // Bare (unquoted) type argument must emit the same PHP typehint as the quoted literal
        // `__AsType<'int'>` (Suite-reds 2026-08-03 #13 follow-up gap).
        var files = CompileToFiles("""
            <?tyhp
            namespace Probe;

            class Holder {
                public static function echoValue(__AsType<int> $value): int {
                    return $value;
                }
            }
            """);

        var joined = string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));
        joined.Should().Contain("function echoValue(int $value): int");
        joined.Should().NotContain("__AsType");

        var output = EmittedPhpRunner.Run(files, """
            echo \Probe\Holder::echoValue(42);
            """);

        output.Trim().Should().Be("42");
    }

    private static string CompileAndEmit(string tyhp)
        => string.Join('\n', CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "type_name_algebra.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.2",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, CreateProject());
            return new TyhpEmitter(context).Emit(result.ParsedFiles!);
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
                ["output:phpVersion"] = "8.2",
            })
            .Build();
        return new Project(configuration);
    }
}
