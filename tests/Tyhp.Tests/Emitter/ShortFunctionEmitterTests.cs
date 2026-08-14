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
public class ShortFunctionEmitterTests
{
    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "short_fn.tyhp");
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

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

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
    public void Emit_TopLevelShortFunction_ExpandsToFunctionWithReturn()
    {
        var php = CompileAndEmit("""
            <?tyhp
            fn myFunc(int $val): int => $val + 5;
            $a = myFunc(5);
            """);

        php.Should().Contain("function myFunc(int $val): int");
        php.Should().Contain("return $val + 5;");
        php.Should().Contain("$a = myFunc(5);");
        php.Should().NotContain("fn myFunc");
        php.Should().NotContain("=> $val + 5");
    }

    [Fact]
    public void Emit_ShortMethods_PreserveModifiersAndExpandBodies()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class MyClass
            {
                public fn getVal(): int => 5;
                private static fn double(int $x): int => $x * 2;
                protected final fn id(string $s): string => $s;
            }
            """);

        php.Should().Contain("public function getVal(): int");
        php.Should().Contain("return 5;");
        php.Should().Contain("private static function double(int $x): int");
        php.Should().Contain("return $x * 2;");
        php.Should().Contain("protected final function id(string $s): string");
        php.Should().Contain("return $s;");
        php.Should().NotContain("fn getVal");
        php.Should().NotContain("fn double");
        php.Should().NotContain("fn id");
    }

    [Fact]
    public void Emit_AnonymousArrowFunction_RemainsArrowSyntax()
    {
        var php = CompileAndEmit("""
            <?tyhp
            $f = fn(int $x): int => $x + 1;
            """);

        php.Should().Contain("fn(int $x): int => $x + 1");
        php.Should().NotContain("function(int $x): int");
    }

    [Fact]
    public void Emit_ReturnsRefShortFunction_ExpandsCorrectly()
    {
        var php = CompileAndEmit("""
            <?tyhp
            fn &getRef(array &$arr): mixed => $arr[0];
            """);

        php.Should().Contain("function &getRef(array &$arr): mixed");
        php.Should().Contain("return $arr[0];");
        php.Should().NotContain("fn &getRef");
    }

    [Fact]
    public void Emit_AsyncTopLevelShortFunction_WrapsWithPromiseAsync()
    {
        var php = CompileAndEmit("""
            <?tyhp
            async fn fetch(): int => 1;
            """);

        php.Should().Contain("function fetch(): \\Tyhp\\Promise");
        php.Should().Contain("\\Tyhp\\Promise::_async(function (): int {");
        php.Should().Contain("return 1;");
        php.Should().NotContain("async function");
        php.Should().NotContain("fn fetch");
    }

    [Fact]
    public void Emit_ExtensionShortFunction_EmitsPublicStaticFunction()
    {
        // Parse-only emit (same pattern as AsyncAwaitEmitterTests) so extension checker rules
        // that need a fuller project context do not obscure the emission assertion.
        var parseResult = ParserTestHelper.ParseTyhpContent("""
            <?tyhp
            extension IntExt extends int {
                fn twice(): int => $this * 2;
            }
            """);
        parseResult.Diagnostics.HasErrors.Should().BeFalse(
            $"parse errors: {string.Join(", ", parseResult.Diagnostics)}");
        var srcFile = parseResult.Ast.Should().BeAssignableTo<SrcFileAst>().Subject;
        var context = EmitContext.Create(new GlobalScope(), new DiagnosticBag());
        var outputFiles = new TyhpEmitter(context).Emit([srcFile]);
        var php = string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));

        php.Should().Contain("public static function twice(): int");
        php.Should().Contain("return $this * 2;");
        php.Should().NotContain("public static twice(");
        php.Should().NotContain("fn twice");
    }
}
