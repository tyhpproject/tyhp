using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class SymbolNameTypeEmitterTests
{
    [Fact]
    public void Emit_ClassNameParameterAndReturn_EraseToString()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {}
            function make(__ClassName $cls): __ClassName {
                return $cls;
            }
            """);

        php.Should().Contain("function make(string $cls): string");
        php.Should().NotContain("__ClassName");
        php.Should().NotContain("\\__ClassName");
    }

    [Fact]
    public void Emit_ParametricClassNameParameter_ErasesToString()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {}
            class Factory {
                public static function new<T extends object>(__ClassName<T> $className): mixed {
                    return null;
                }
            }
            """);

        php.Should().Contain("function new(string $className)");
        php.Should().NotContain("__ClassName");
        php.Should().NotContain("\\__ClassName");
    }

    [Fact]
    public void Emit_SymbolNameProperty_ErasesToString()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Holder {
                public __FunctionName $fn;
                public function __construct(__FunctionName $fn) {
                    $this->fn = $fn;
                }
            }
            """);

        php.Should().Contain("public string $fn");
        php.Should().Contain("function __construct(string $fn)");
        php.Should().NotContain("__FunctionName");
    }

    [Theory]
    [InlineData("__ClassName", "string")]
    [InlineData("__EnumName", "string")]
    [InlineData("__InterfaceName", "string")]
    [InlineData("__TraitName", "string")]
    [InlineData("__FunctionName", "string")]
    [InlineData("__StructName", "string")]
    [InlineData("__VarName", "string")]
    [InlineData("__ConstName", "string")]
    public void Emit_BareSymbolNameParameter_ErasesToString(string tyhpType, string phpHint)
    {
        var php = CompileAndEmit(
            "<?tyhp\nfunction take(" + tyhpType + " $name): void {}\n");

        php.Should().Contain($"function take({phpHint} $name): void");
        php.Should().NotContain(tyhpType);
    }

    [Theory]
    [InlineData("__PropertyName<User>", "User", false)]
    [InlineData("__MethodName<User>", "User", false)]
    [InlineData("__ObjectConstName<User>", "User", false)]
    [InlineData("__TypedVarName<int>", null, false)]
    [InlineData("__EnumCaseName<Status>", "Status", true)]
    [InlineData("__UsedTraitName<User>", "User", false)]
    public void Emit_ParametricSymbolNameParameter_ErasesToString(
        string tyhpType,
        string? needsType,
        bool isEnum)
    {
        var preamble = needsType is null
            ? ""
            : isEnum
                ? "enum " + needsType + " { case A; }\n"
                : "class " + needsType + " {}\n";
        var php = CompileAndEmit(
            "<?tyhp\n" + preamble + "function take(" + tyhpType + " $name): void {}\n");

        php.Should().Contain("function take(string $name): void");
        php.Should().NotContain("__PropertyName");
        php.Should().NotContain("__MethodName");
        php.Should().NotContain("__ObjectConstName");
        php.Should().NotContain("__TypedVarName");
        php.Should().NotContain("__EnumCaseName");
        php.Should().NotContain("__UsedTraitName");
    }

    [Fact]
    public void Emit_CompatibleTypeName_ErasesToString()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {}
            function take(__CompatibleTypeName<User> $name): void {}
            """);

        php.Should().Contain("function take(string $name): void");
        php.Should().NotContain("__CompatibleTypeName");
    }

    [Fact]
    public void Emit_NullableAndUnionSymbolName_CollapseToString()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function a(?__ClassName $c): void {}
            function b(__ClassName|null $c): void {}
            function c(__ClassName|__FunctionName $c): void {}
            """);

        php.Should().Contain("function a(?string $c): void");
        php.Should().Contain("function b(?string $c): void");
        php.Should().Contain("function c(string $c): void");
        php.Should().NotContain("__ClassName");
        php.Should().NotContain("__FunctionName");
    }

    [Fact]
    public void Emit_LambdaStyleOfSignature_ErasesClassNameHint()
    {
        // Mirrors tyhp/lambda Expression::of / PropertyPathBuilder::of — the live regression.
        var php = CompileAndEmit("""
            <?tyhp
            class User {}
            class PropertyPathBuilder<T> {}
            class Expression {
                public static function of<T extends object>(__ClassName<T> $sourceType, string|array ...$segments): PropertyPathBuilder<T> {
                    return new PropertyPathBuilder();
                }
            }
            """);

        php.Should().Contain("function of(string $sourceType");
        php.Should().NotContain("__ClassName");
        php.Should().NotContain("\\__ClassName");
    }

    [Theory]
    [InlineData("int")]
    [InlineData("string")]
    [InlineData("?bool")]
    public void Emit_TyhpInternalParameter_ErasesToTypeArgumentNotString(string typeArg)
    {
        // __TyhpInternal<T> is a transparent pass-through wrapper (checker resolves it directly
        // to T via UtilityTypeResolver), unlike its name-string siblings which all erase to
        // `string`. Emitting `string` here would mismatch the checker and break calls at runtime.
        var php = CompileAndEmit(
            "<?tyhp\nfunction take(__TyhpInternal<" + typeArg + "> $x): void {}\n");

        php.Should().Contain($"function take({typeArg} $x): void");
        php.Should().NotContain("__TyhpInternal");
    }

    [Fact]
    public void Emit_TyhpInternalParameter_IsCallableFromPhpWithUnderlyingType()
    {
        var files = CompileToFiles("""
            <?tyhp
            namespace Probe;

            class Holder {
                public static function echoValue(__TyhpInternal<int> $value): int {
                    return $value;
                }
            }
            """);

        var joined = string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));
        joined.Should().Contain("function echoValue(int $value): int");
        joined.Should().NotContain("__TyhpInternal");

        var output = EmittedPhpRunner.Run(files, """
            echo \Probe\Holder::echoValue(42);
            """);

        output.Trim().Should().Be("42");
    }

    [Fact]
    public void Emit_ClassNameParameter_IsCallableFromPhpWithString()
    {
        // Text assertions alone once "passed" while emitting `\__ClassName` — exercise the call.
        var files = CompileToFiles("""
            <?tyhp
            namespace Probe;

            class Factory {
                public static function echoClass(__ClassName $className): string {
                    return $className;
                }
            }
            """);

        var joined = string.Join('\n', files.Select(f => f.GeneratedContent ?? string.Empty));
        joined.Should().Contain("function echoClass(string $className): string");
        joined.Should().NotContain("__ClassName");

        var output = EmittedPhpRunner.Run(files, """
            echo \Probe\Factory::echoClass('Probe\\Factory');
            """);

        output.Trim().Should().Be("Probe\\Factory");
    }

    private static string CompileAndEmit(string tyhp)
        => string.Join('\n', CompileToFiles(tyhp).Select(f => f.GeneratedContent ?? string.Empty));

    private static IReadOnlyList<PHPOutputFile> CompileToFiles(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "symbol_names.tyhp");
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
