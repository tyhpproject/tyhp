using System;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Binder.Resolution;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Tyhpdef <c>function php_name as tyhpName</c> must register a resolvable
/// <see cref="FunctionDeclarationSymbol"/> under the Tyhp name, and checking calls
/// to generic aliased stubs must not crash on locked <see cref="CheckerState"/> snapshots.
/// </summary>
[Trait("Category", "Checker")]
public class TyhpdefFunctionAliasResolutionTests
{
    [Fact]
    public void Bind_TyhpdefFunctionAlias_RegistersFunctionSymbolUnderAliasName()
    {
        // ExtStandard: function call_user_func_array as call_user_func_array_unsafe(...)
        var (globalScope, diagnostics) = BindOnly("""
            <?tyhp
            function demo(): void {}
            """);

        diagnostics.Errors.Should().BeEmpty();
        globalScope.Should().NotBeNull();

        var resolver = new NameResolver(new SymbolTree(globalScope!), new DiagnosticBag());
        var resolved = resolver.ResolveRelativeName(["call_user_func_array_unsafe"], globalScope!);

        resolved.Should().BeOfType<FunctionDeclarationSymbol>();
        var fn = (FunctionDeclarationSymbol)resolved!;
        fn.Name.Should().Be("call_user_func_array_unsafe");
        fn.OriginalPhpName.Should().Be("call_user_func_array");
        fn.Parameters.Should().HaveCount(2);
    }

    [Fact]
    public void Check_TyhpdefFunctionAlias_ArgumentTypesAreChecked()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                \call_user_func_array_unsafe(1, 2);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_TyhpdefFunctionAlias_ValidCall_NoTypeError()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(callable $cb, array $args): mixed {
                return \call_user_func_array_unsafe($cb, $args);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Check_GenericTyhpdefFunctionAlias_InIfCondition_DoesNotCrash()
    {
        // ExtCore: class_exists_alt is a generic type-guard stub. Resolving its parameter type
        // from another file used to SnapShot a locked CheckerState (TYHP4001).
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $receiverOrClass): void {
                if (\is_object($receiverOrClass)) {
                    string $className = $receiverOrClass::class;
                    if (\class_exists_alt($className)) {
                        echo $className;
                    }
                }
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnknownError);
    }

    private static (Tyhp.TyhpLang.Binder.Scopes.GlobalScope? Scope, DiagnosticBag Diagnostics) BindOnly(string content)
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
            return (result.GlobalScope, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
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
            return result.Diagnostics;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
