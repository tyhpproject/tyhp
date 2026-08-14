using System;
using System.IO;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Ast.Interfaces;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.TyhpLang.Parser;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 14.5 Phase 4 item 4 — PHP 8.5 <c>(void)</c> cast typing / intentional discard.
/// </summary>
[Trait("Category", "Checker")]
public class VoidCastCheckTests
{
    [Fact]
    public void VoidCast_Statement_TypeChecksOperandAndInfersVoid()
    {
        var (checker, file, diagnostics) = Compile("""
            <?tyhp
            function demo(): void {
                string $s = "hi";
                (void)$s;
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));

        var voidCast = FindVoidCasts(file).Should().ContainSingle().Subject;
        checker.ResolveExpressionType(voidCast, new CheckerState()).DisplayName.Should().Be("void");
    }

    [Fact]
    public void VoidCast_OperandWithTypeError_StillReportsOperandDiagnostics()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                (void)\strlen([]);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void VoidCast_ForInitAndUpdate_TypeChecksCleanly()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                for ((void)\strlen("a"); true; (void)\strlen("b")) {
                    break;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void VoidCast_NonFinalForCondition_DoesNotRequireBoolOnVoid()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                for (; (void)\strlen("x"), true; ) {
                    break;
                }
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void VoidCast_MixedOperand_DoesNotReportMixedRequiresNarrowing()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(mixed $m): void {
                (void)$m;
            }
            """);

        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void NoDiscard_DiscardedCall_Reports4165()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\NoDiscard]
            function important(): int {
                return 1;
            }
            function demo(): void {
                important();
            }
            """);

        diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.CheckerNoDiscardReturnUnused);
    }

    [Fact]
    public void NoDiscard_VoidCast_Suppresses4165()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\NoDiscard]
            function important(): int {
                return 1;
            }
            function demo(): void {
                (void)important();
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerNoDiscardReturnUnused);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void NoDiscard_UsedReturn_DoesNotWarn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\NoDiscard]
            function important(): int {
                return 1;
            }
            function demo(): void {
                int $n = important();
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerNoDiscardReturnUnused);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    [Fact]
    public void OrdinaryDiscardedCall_WithoutNoDiscard_DoesNotWarn()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function sideEffect(): int {
                return 1;
            }
            function demo(): void {
                sideEffect();
            }
            """);

        diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.CheckerNoDiscardReturnUnused);
        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    private static string Describe(DiagnosticBag diagnostics) =>
        string.Join("; ",
            diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")
                .Concat(diagnostics.Warnings.Select(w => $"W{w.Code}: {w.Message}")));

    private static DiagnosticBag CompileAndCheck(string content)
    {
        var (_, _, diagnostics) = Compile(content);
        return diagnostics;
    }

    private static (TyhpChecker checker, SrcFileAst file, DiagnosticBag diagnostics) Compile(string content)
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
                PhpVersion = "8.5",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
                SkipChecking = true,
            };

            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var file = result.ParsedFiles![0];
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return (checker, file, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static List<PhpUnaryOpAst> FindVoidCasts(IBase2Ast root)
    {
        var found = new List<PhpUnaryOpAst>();
        Collect(root, found);
        return found;

        static void Collect(IBase2Ast? node, List<PhpUnaryOpAst> dest)
        {
            if (node is null)
            {
                return;
            }

            if (node is PhpUnaryOpAst unary
                && unary.Operator?.ValueInt64 is long token
                && (int)token == TyhpParser.T_VOID_CAST)
            {
                dest.Add(unary);
            }

            foreach (var child in node.AstChildren)
            {
                Collect(child, dest);
            }
        }
    }
}
