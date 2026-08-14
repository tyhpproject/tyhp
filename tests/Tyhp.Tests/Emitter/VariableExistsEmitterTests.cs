using System;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

[Trait("Category", "Emitter")]
public class VariableExistsEmitterTests
{
    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "variable_exists.tyhp");
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
    public void Emit_VariableExists_Variable_UsesArrayKeyExistsOnDefinedVars()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function f(mixed $v): bool {
                return variable_exists($v);
            }
            """);

        php.Should().Contain(@"\array_key_exists('v', \get_defined_vars())");
        php.Should().NotContain("variable_exists(");
        php.Should().NotContain("isset($v)");
    }

    [Fact]
    public void Emit_VariableExists_StringLiteral_UsesArrayKeyExistsOnDefinedVars()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function f(): bool {
                return variable_exists('customHandler');
            }
            """);

        php.Should().Contain(@"\array_key_exists('customHandler', \get_defined_vars())");
        php.Should().NotContain("variable_exists(");
    }

    [Fact]
    public void Emit_VariableExists_NullValuedVariable_StillChecksExistenceNotIsset()
    {
        // array_key_exists returns true for a defined null; isset would return false.
        var php = CompileAndEmit("""
            <?tyhp
            function f(?string $maybeNull): bool {
                return variable_exists($maybeNull);
            }
            """);

        php.Should().Contain(@"\array_key_exists('maybeNull', \get_defined_vars())");
        php.Should().NotContain("isset($maybeNull)");
        php.Should().NotContain("variable_exists(");
    }

    [Fact]
    public void Emit_VariableExists_StringLiteralWithDollarPrefix_StripsDollar()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function f(): bool {
                return variable_exists('$foo');
            }
            """);

        php.Should().Contain(@"\array_key_exists('foo', \get_defined_vars())");
        php.Should().NotContain("variable_exists(");
    }

    [Fact]
    public void Compile_VariableExists_VariableVariable_ReportsNonConstant()
    {
        var errors = CompileErrors("""
            <?tyhp
            function f(string $v): void {
                $b = variable_exists($$v);
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Compile_VariableExists_EmptyString_ReportsNonConstant()
    {
        var errors = CompileErrors("""
            <?tyhp
            function f(): void {
                $b = variable_exists('');
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Compile_VariableExists_InvalidInReturn_ReportsNonConstant()
    {
        var errors = CompileErrors("""
            <?tyhp
            function f(): bool {
                return variable_exists(1 + 2);
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerNonConstantExpression);
    }

    [Fact]
    public void Compile_VariableExists_InvalidInIfCondition_ReportsNonConstant()
    {
        var errors = CompileErrors("""
            <?tyhp
            function f(): void {
                if (variable_exists(1 + 2)) {
                }
            }
            """);

        errors.Should().Contain(e => e.Code == MessageCode.CheckerNonConstantExpression);
    }

    private static List<Tyhp.Domain.Diagnostics.IDiagnostic> CompileErrors(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "variable_exists.tyhp");
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

            return result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
