using System.Linq;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// TYHP4048: <c>void</c> is a type argument only when the generic parameter's
/// <c>extends</c> bound mentions it. <c>void|mixed</c> must count (Promise-shaped),
/// not collapse to a <c>mixed</c>-only rejection.
/// </summary>
[Trait("Category", "Checker")]
public class VoidInGenericPositionTests
{
    [Fact]
    public void PromiseShaped_VoidMixedConstraint_AllowsVoidTypeArgument()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Promise<TReturn extends void|mixed = mixed> {}
            interface AsyncIsDisposable {
                public function disposeAsync(): Promise<void>;
            }
            function awaitDispose(Promise<void> $promise): void {}
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerVoidInNonReturnPosition,
            $"Promise<void> must be allowed when TReturn extends void|mixed: {Describe(errors)}");
        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void BareVoidConstraint_AllowsVoidTypeArgument()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Foo<T extends void> {}
            function take(Foo<void> $foo): void {}
            """);

        errors.Should().NotContain(
            e => e.Code == MessageCode.CheckerVoidInNonReturnPosition,
            $"Foo<void> must be allowed when T extends void: {Describe(errors)}");
        errors.Should().BeEmpty(Describe(errors));
    }

    [Fact]
    public void MixedConstraint_RejectsVoidTypeArgument()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Bar<T extends mixed> {}
            function take(Bar<void> $bar): void {}
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerVoidInNonReturnPosition,
            $"Bar<void> must be rejected when T extends mixed: {Describe(errors)}");
    }

    [Fact]
    public void ArrayVoid_StillRejected()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(): void {
                array<void> $items;
            }
            """);

        errors.Should().Contain(
            e => e.Code == MessageCode.CheckerVoidInNonReturnPosition,
            $"array<void> must still be rejected: {Describe(errors)}");
    }

    [Fact]
    public void VoidReturnType_StillAllowed()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            function demo(): void {}
            """);

        errors.Should().BeEmpty(Describe(errors));
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull("bind should succeed");
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);

            return result.Diagnostics.Errors
                .Where(e => e.FileName is not null
                    && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
