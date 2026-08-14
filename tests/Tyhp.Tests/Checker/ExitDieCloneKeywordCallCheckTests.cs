using System;
using System.IO;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 14.5 Phase 4 items 1–2 — keyword call forms use ExtCore tyhpdef signatures
/// for arity / named args / types; unary clone keeps the object-type check.
/// </summary>
[Trait("Category", "Checker")]
[Trait("Category", "Tyhpdef")]
public class ExitDieCloneKeywordCallCheckTests
{
    [Fact]
    public void Exit_NamedStatusArg_NoErrors()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                exit(status: 0);
                die(status: "done");
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Exit_UnknownNamedArg_Reports4081()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                exit(code: 1);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnknownNamedArgument);
    }

    [Fact]
    public void Exit_TooManyArgs_Reports4143()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                exit(0, 1);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerTooManyArguments);
    }

    [Fact]
    public void Exit_WrongArgType_Reports4010()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                exit([]);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Exit_EmptyCallAndBare_NoArityErrors()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                exit();
                exit;
                die();
                die;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingArgument);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTooManyArguments);
    }

    [Fact]
    public void Exit_FirstClassCallable_NoArityErrors()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $fn = exit(...);
                $gn = die(...);
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingArgument);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTooManyArguments);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Clone_CallMissingRequiredObject_Reports4142()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $a = clone();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMissingArgument);
    }

    [Fact]
    public void Clone_CallWithObjectAndNamedArgs_NoErrors()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {}
            function demo(C $obj): void {
                $a = clone($obj,);
                $b = clone($obj, []);
                $c = clone(object: $obj);
                $d = clone(object: $obj, withProperties: []);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Clone_CallResult_PreservesObjectArgumentType()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Widget {}
            function viaPositional(Widget $o): Widget {
                return clone($o,);
            }
            function viaNamed(Widget $o): Widget {
                return clone(object: $o, withProperties: []);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void Clone_CallWrongObjectType_Reports4010()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $a = clone(42,);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Clone_CallUnknownNamedArg_Reports4081()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class C {}
            function demo(C $obj): void {
                $a = clone(obj: $obj);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnknownNamedArgument);
    }

    [Fact]
    public void UnaryClone_StillChecksObjectType_NotTyhpdefArity()
    {
        var ok = CompileAndCheck("""
            <?tyhp
            class C {}
            function demo(C $obj): void {
                $a = clone $obj;
                $b = clone($obj);
            }
            """);
        ok.Errors.Should().BeEmpty(Describe(ok));
        ok.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingArgument);

        var bad = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $a = clone 1;
            }
            """);
        bad.Errors.Should().Contain(d => d.Code == MessageCode.CheckerCloneNonObject);
        bad.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMissingArgument);
        bad.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerTooManyArguments);
    }

    private static string Describe(DiagnosticBag diagnostics) =>
        string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));

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
