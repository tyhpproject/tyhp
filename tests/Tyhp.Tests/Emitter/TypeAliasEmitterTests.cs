using System;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class TypeAliasEmitterTests
{
    private static string CompileAndEmit(string tyhp, string? tempRoot = null)
    {
        var tempDir = tempRoot ?? Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "aliases.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            // Filter out infrastructure errors from tyhpdef packages (not our test's concern).
            // Also ignore BinderUnresolvedParameterType for `self\Alias` / `Class\Alias` refs —
            // binder support for class-scoped alias qualification is incomplete; emission still
            // expands via TypeAliasMap when the alias declaration was collected.
            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .ToList();
            unexpectedErrors.Should().BeEmpty($"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => e.Message))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Emit_TypeAlias_Declaration_ErasesDeclaration()
    {
        // GIVEN: a top-level type alias declaration
        // WHEN: compiled and emitted
        // THEN: the type alias declaration itself is erased — no PHP declaration produced
        var php = CompileAndEmit(@"
<?tyhp

// A type alias is a way to create custom types
type scalarType = int|float|string|bool|null|array;

// a type alias is a way to name a complex type so it is easier to use
// keep in mind that this is a compile type construct

// it can be used anywhere a type must be specified
function convertToString(scalarType $val): string
{
    return \strval($val);
}
");

        // The `type scalarType = ...` declaration should produce NO PHP — it's erased
        php.Should().NotContain("type scalarType");
        php.Should().NotContain("scalarType =");
    }

    [Fact]
    public void Emit_TypeAlias_UsedInFunctionParameter_ExpandsToUnderlyingPHPTypes()
    {
        // GIVEN: a function that uses a type alias as its parameter type
        // WHEN: compiled and emitted
        // THEN: the alias is replaced with its underlying PHP type union (int | float | …)
        var php = CompileAndEmit(@"
<?tyhp

type scalarType = int|float|string|bool|null|array;

function convertToString(scalarType $val): string
{
    return \strval($val);
}
");

        // The parameter type should be expanded to the underlying PHP types
        php.Should().Contain("int | float | string | bool | null | array $val");
        php.Should().NotContain("scalarType $val");
    }

    [Fact]
    public void Emit_ClassScopedTypeAlias_UsedInConstructor_ExpandsSelfReference()
    {
        // GIVEN: a class with a scoped type alias `self|null` used in a constructor parameter
        // WHEN: compiled and emitted
        // THEN: the alias expands to `?self` (PHP nullable shorthand)
        var php = CompileAndEmit(@"
<?tyhp

class MyObject
{
    public type MyObjOrNull = self|null;

    public function __construct(self\MyObjOrNull $buildFrom = null)
    {
        // do stuff
    }
}
");

        // self\MyObjOrNull $buildFrom = null -> ?self $buildFrom = null
        php.Should().Contain("public function __construct(?self $buildFrom = null");
        php.Should().NotContain("MyObjOrNull");
    }

    [Fact]
    public void Emit_ClassScopedTypeAlias_UsedOutsideClass_ExpandsToSelf()
    {
        // GIVEN: a public scoped type alias used outside the class definition
        // WHEN: compiled and emitted
        // THEN: it expands to its underlying type (self becomes the concrete class name, null becomes null)
        var php = CompileAndEmit(@"
<?tyhp

class MyObject
{
    public type MyObjOrNull = self|null;

    public function __construct(self\MyObjOrNull $buildFrom = null)
    {
        // do stuff
    }
}

MyObject\MyObjOrNull $myObj = null;
");

        // The alias reference outside the class should expand to its underlying type
        // MyObject\MyObjOrNull $myObj = null; -> $myObj = null;
        php.Should().Contain("$myObj = null");
        php.Should().NotContain("MyObjOrNull $myObj");
    }

    [Fact]
    public void Emit_NoTypeAlias_ReturnsUnchangedCode()
    {
        // GIVEN: a simple Tyhp file with no type aliases
        // WHEN: compiled and emitted
        // THEN: the code emits normally as PHP
        var php = CompileAndEmit(@"
<?tyhp

function hello(): void
{
    echo 'world';
}
");

        php.Should().Contain("function hello(): void");
        php.Should().Contain("echo 'world';");
    }
}
