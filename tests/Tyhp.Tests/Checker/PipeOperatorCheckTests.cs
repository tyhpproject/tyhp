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
using Tyhp.TyhpLang.Enum;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Story 14.5 Phase 4 item 3 — PHP 8.5 pipe <c>|&gt;</c> RHS callable typing.
/// </summary>
[Trait("Category", "Checker")]
public class PipeOperatorCheckTests
{
    [Fact]
    public void Pipe_StrlenFcc_InfersIntAndAssignsCleanly()
    {
        var (checker, file, diagnostics) = Compile("""
            <?tyhp
            function demo(): void {
                int $n = "hello" |> \strlen(...);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));

        var pipe = FindPipe(file);
        pipe.Should().NotBeNull();
        var type = checker.ResolveExpressionType(pipe!, new CheckerState());
        type.DisplayName.Should().Be("int");
    }

    [Fact]
    public void Pipe_Chain_InfersThroughReturnTypes()
    {
        var (checker, file, diagnostics) = Compile("""
            <?tyhp
            function demo(): void {
                string $s = "PHP"
                    |> \htmlentities(...)
                    |> \strtoupper(...);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));

        var pipes = FindAllPipes(file);
        pipes.Should().HaveCount(2);
        // Outer pipe: strtoupper(...) → string
        checker.ResolveExpressionType(pipes[0], new CheckerState()).DisplayName.Should().Be("string");
    }

    [Fact]
    public void Pipe_ArrowFunctionRhs_InfersReturnType()
    {
        var (checker, file, diagnostics) = Compile("""
            <?tyhp
            function demo(): void {
                int $n = "hi" |> (fn(string $s): int => \strlen($s));
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
        var pipe = FindPipe(file);
        checker.ResolveExpressionType(pipe!, new CheckerState()).DisplayName.Should().Be("int");
    }

    [Fact]
    public void Pipe_NonCallableRhs_Reports4162()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $a = "hello" |> 42;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPipeRhsNotCallable);
    }

    [Fact]
    public void Pipe_TwoRequiredParams_Reports4163()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function needsTwo(string $a, string $b): string {
                return $a . $b;
            }
            function demo(): void {
                $a = "x" |> needsTwo(...);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPipeRhsInvalidArity);
    }

    [Fact]
    public void Pipe_ZeroParamCallable_Reports4163()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function noArgs(): int {
                return 1;
            }
            function demo(): void {
                $a = "x" |> noArgs(...);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPipeRhsInvalidArity);
    }

    [Fact]
    public void Pipe_ByRefFcc_Reports4164()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $a = [1, 2, 3] |> \array_pop(...);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPipeRhsByRefParameter);
    }

    [Fact]
    public void Pipe_ByRefClosure_Reports4164()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $a = [1] |> (function(array &$arr): mixed {
                    return \array_pop($arr);
                });
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerPipeRhsByRefParameter);
    }

    [Fact]
    public void Pipe_LhsTypeMismatch_Reports4010()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                $a = 1 |> \strlen(...);
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleArgumentType);
    }

    [Fact]
    public void Pipe_OptionalSecondParam_Accepted()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(): void {
                string $s = "<b>" |> \htmlentities(...);
            }
            """);

        diagnostics.Errors.Should().BeEmpty(Describe(diagnostics));
    }

    private static string Describe(DiagnosticBag diagnostics) =>
        string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}"));

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

    private static PhpBinaryOpAst? FindPipe(IBase2Ast root) =>
        FindAllPipes(root).FirstOrDefault();

    private static List<PhpBinaryOpAst> FindAllPipes(IBase2Ast root)
    {
        var pipes = new List<PhpBinaryOpAst>();
        Collect(root, pipes);
        return pipes;

        static void Collect(IBase2Ast? node, List<PhpBinaryOpAst> dest)
        {
            if (node is null)
            {
                return;
            }

            if (node is PhpBinaryOpAst binary
                && PhpBinaryOperatorExtensions.FromToken(binary.Operator?.TokenValue ?? -1)
                    == PhpBinaryOperator.Pipe)
            {
                dest.Add(binary);
            }

            foreach (var child in node.AstChildren)
            {
                Collect(child, dest);
            }
        }
    }
}
