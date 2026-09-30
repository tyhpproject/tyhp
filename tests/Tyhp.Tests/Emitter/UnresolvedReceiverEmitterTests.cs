using System;
using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Unresolved receivers must not be rewritten as struct array access or extension splices
/// (Story 21.12 Workstream C).
/// </summary>
[Trait("Category", "Emitter")]
public class UnresolvedReceiverEmitterTests
{
    [Fact]
    public void Emit_UnresolvedPropertyRead_DoesNotRewriteAsArrayAccess()
    {
        var (php, diagnostics) = CompileAndEmit("""
            <?tyhp
            function demo(): int {
                return $hole->x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        php.Should().NotContain("$hole['x']");
        php.Should().NotContain("$hole[\"x\"]");
        php.Should().Contain("->x");
    }

    [Fact]
    public void Emit_UnresolvedMethodCall_DoesNotRewriteAsArrayAccess()
    {
        var (php, diagnostics) = CompileAndEmit("""
            <?tyhp
            function demo(): void {
                $hole->foo();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        php.Should().NotContain("$hole['foo']");
        php.Should().NotContain("$hole[\"foo\"]");
        php.Should().Contain("->foo");
    }

    [Fact]
    public void Emit_MixedPropertyRead_DoesNotUseUnresolvedReceiverCode()
    {
        var (php, diagnostics) = CompileAndEmit("""
            <?tyhp
            function demo(mixed $m): void {
                $m->x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerMixedRequiresNarrowing);
        diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        php.Should().Contain("$m->x");
        php.Should().NotContain("$m['x']");
    }

    [Fact]
    public void Emit_UnresolvedExtensionCall_DoesNotSplice()
    {
        var (php, diagnostics) = CompileAndEmit("""
            <?tyhp
            extension IntExt extends int {
                fn twice(): int => $this * 2;
            }

            function demo(): int {
                return $hole->twice();
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        php.Should().Contain("$hole->twice()");
        php.Should().NotContain("$hole * 2");
        php.Should().NotContain("($hole * 2)");
    }

    [Fact]
    public void Emit_UnresolvedPropertyNextToTypedStruct_DoesNotRewriteUnresolved()
    {
        // Neighbor typed as a struct shape must not cause `$hole->x` to rewrite to `['x']`.
        var (php, diagnostics) = CompileAndEmit("""
            <?tyhp
            function demo(struct { int $x; } $p): int {
                int $typed = $p->x;
                return $hole->x;
            }
            """);

        diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.CheckerUnresolvedReceiver);
        php.Should().NotContain("$hole['x']");
        php.Should().NotContain("$hole[\"x\"]");
        php.Should().Contain("$hole->x");
    }

    private static (string Php, DiagnosticBag Diagnostics) CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "unresolved-receiver.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath],
                IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .Where(d => d.Code != MessageCode.CheckerUnresolvedReceiver)
                .Where(d => d.Code != MessageCode.CheckerMixedRequiresNarrowing)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project: null,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            var php = string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
            return (php, result.Diagnostics);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
