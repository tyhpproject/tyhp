using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Relative types (<c>self</c>/<c>static</c>/<c>parent</c>) vs generics — FOUND_BUGS audit 2026-08-12.
/// </summary>
[Trait("Category", "Checker")]
public class RelativeStaticTypeTests
{
    [Fact]
    public void ParameterizedStatic_IsRejected_EvenOnFinalClass()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Promise<TReturn extends void|mixed = void> {
                public function __construct(callable<TReturn> $executor): void {}
                public static function async<T extends void|mixed>(callable<T> $fn): static<T> {
                    return new self<T>($fn);
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerParameterizedStaticForbidden);
    }

    [Fact]
    public void ParameterizedStatic_NewExpression_IsRejected()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Box<T> {
                public function __construct(T $value): void {}
                public static function make<U>(U $value): self<U> {
                    return new static<U>($value);
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerParameterizedStaticForbidden);
    }

    [Fact]
    public void SelfFactory_PreservesMethodGenericThroughCallSite()
    {
        // Historical gap: `: self<T>` collapsed to mixed on getResult(); `: static<T>` / `: Promise<T>` worked.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            final class Promise<TReturn extends void|mixed = void> {
                private TReturn $result;
                public function __construct(TReturn $result): void {
                    $this->result = $result;
                }
                public function getResult(): TReturn {
                    return $this->result;
                }
                public static function async<T extends void|mixed>(T $value): self<T> {
                    return new self<T>($value);
                }
            }
            function run(): int {
                Promise<int> $p = Promise::async<int>(42);
                return $p->getResult();
            }
            """);

        errors.Should().BeEmpty($"self<T> factory must preserve T: {Describe(errors)}");
    }

    [Fact]
    public void BareStatic_FluentOnGenericChild_KeepsChildTypeArgs()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Builder {
                public function tap(): static {
                    return $this;
                }
            }
            class GenericBuilder<T> extends Builder {
                public function __construct(public T $value): void {}
            }
            function demo(GenericBuilder<int> $b): GenericBuilder<int> {
                return $b->tap();
            }
            """);

        errors.Should().BeEmpty($"bare static fluent must keep GenericBuilder<int>: {Describe(errors)}");
    }

    [Fact]
    public void StaticReturn_AcceptsThis_AndRejectsNewSelf()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box {
                public function ok(): static {
                    return $this;
                }
                public function bad(): static {
                    return new self();
                }
            }
            """);

        errors.Should().Contain(d => d.Code == MessageCode.CheckerIncompatibleReturnType);
        // `$this` in ok() must not also be rejected.
        errors.Should().HaveCount(1, Describe(errors));
    }

    [Fact]
    public void StaticReturn_AcceptsOtherStaticCall_AndInstanceofStatic()
    {
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Box {
                public function again(): static {
                    return $this->ok();
                }
                public function ok(): static {
                    return $this;
                }
                public function adopt(mixed $other): static {
                    if ($other instanceof static) {
                        return $other;
                    }
                    return $this;
                }
            }
            """);

        errors.Should().BeEmpty($"static-typed values must satisfy : static: {Describe(errors)}");
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

            return result.Diagnostics.Errors
                .Where(e => e.FileName is not null
                    && e.FileName.Replace('\\', '/').EndsWith(fileName, StringComparison.Ordinal))
                .ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }
}
