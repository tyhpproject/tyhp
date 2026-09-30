using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// <c>\WeakReference::create</c> must infer <c>T</c> from the argument (FOUND_BUGS Story 21
/// Phase 5). Class-level <c>create(T): WeakReference&lt;T&gt;</c> left <c>T</c> unsubstituted
/// and reported TYHP4035; the factory is a method-level generic instead.
/// </summary>
[Trait("Category", "Checker")]
public class WeakReferenceGenericTests
{
    [Fact]
    public void WeakReferenceCreate_InfersStdClass_No4035()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function testWeakReference(): void {
                \stdClass $obj = new \stdClass();
                \WeakReference<\stdClass> $ref = \WeakReference::create($obj);
                ?\stdClass $gotten = $ref->get();
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"create() must not TYHP4035: {Describe(errors)}");
        errors.Should().BeEmpty($"WeakReference::create should type-check: {Describe(errors)}");
    }

    [Fact]
    public void WeakReferenceCreate_AssignedToObjectRef_No4035()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function testWeakReference(): void {
                \stdClass $obj = new \stdClass();
                \WeakReference<object> $ref = \WeakReference::create($obj);
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"FOUND_BUGS repro must not TYHP4035: {Describe(errors)}");
        // `create` infers `TIn=\stdClass`. `WeakReference<T>` is invariant, so assigning
        // that to `WeakReference<object>` is a type mismatch — not a constraint error.
        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"inferred WeakReference<stdClass> is not WeakReference<object>: {Describe(errors)}");
    }

    [Fact]
    public void WeakReferenceCreate_GetReturnMatchesArgument()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function testWeakReference(): void {
                \stdClass $obj = new \stdClass();
                $ref = \WeakReference::create($obj);
                ?\stdClass $gotten = $ref->get();
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"inferred create() must not TYHP4035: {Describe(errors)}");
        errors.Should().BeEmpty($"get() after create() should be ?stdClass: {Describe(errors)}");
    }

    [Fact]
    public void WeakReferenceCreate_FromObjectTypedValue_AssignsToObjectRef()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function testWeakReference(): void {
                object $obj = new \stdClass();
                \WeakReference<object> $ref = \WeakReference::create($obj);
                ?object $gotten = $ref->get();
            }
            """);

        errors.Should().BeEmpty(
            $"create() from object should return WeakReference<object>: {Describe(errors)}");
    }

    [Fact]
    public void WeakReference_IntTypeArgument_Reports4035()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function bad(\WeakReference<int> $ref): void {
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"WeakReference<int> must still TYHP4035: {Describe(errors)}");
    }

    [Fact]
    public void WeakReferenceGet_OnTypedParameter_StillWorks()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            function testWeakReference(\WeakReference<\stdClass> $typed): void {
                ?\stdClass $gotten = $typed->get();
            }
            """);

        errors.Should().BeEmpty($"typed WeakReference parameter get() should type-check: {Describe(errors)}");
    }

    [Fact]
    public void UserGenericStaticFactory_MethodLevelGeneric_InfersFromArgument()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Wr<T extends object = object> {
                public static function create<TIn extends object>(TIn $object): Wr<TIn> {
                    return new Wr<TIn>();
                }
                public function get(): ?T {
                    return null;
                }
            }
            function demo(): void {
                \stdClass $obj = new \stdClass();
                Wr<\stdClass> $ref = Wr::create($obj);
                ?\stdClass $gotten = $ref->get();
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerGenericConstraintNotSatisfied,
            $"method-level factory generic must not TYHP4035: {Describe(errors)}");
        errors.Should().BeEmpty($"user Wr::create should type-check: {Describe(errors)}");
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
