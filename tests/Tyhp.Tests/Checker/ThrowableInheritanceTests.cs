using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Checker;

/// <summary>
/// Regression tests for <c>\Throwable</c> recognition through inheritance chains. These previously
/// produced spurious TYHP4039 (throw) / TYHP4040 (catch) errors because:
/// <list type="bullet">
///   <item><c>new \Fully\Qualified\Exception(...)</c> inferred to <c>unknown</c> (qualified class
///   names were not resolved in the inferrer).</item>
///   <item><c>\Throwable</c> itself resolved to <c>unknown</c> (a bare lexical lookup misses the
///   global type).</item>
///   <item>tyhpdef-imported classes never exposed their <c>extends</c>/<c>implements</c> clauses to
///   the subtype walk, so the exception hierarchy never reached <c>\Throwable</c>.</item>
/// </list>
/// The exception hierarchy is supplied through a <c>.tyhpdef</c> fixture so that the classes are
/// bound exactly as imported runtime/extension types (i.e. as <c>TyhpdefImportObjectDeclAst</c>),
/// which is the binding shape that triggered the bug.
/// </summary>
[Trait("Category", "Checker")]
public class ThrowableInheritanceTests
{
    // Mirrors the global-namespace exception hierarchy declared by the PHP core/SPL extensions:
    // BadMethodCallException -> BadFunctionCallException -> LogicException -> Exception (implements
    // \Throwable). \Throwable itself only `implements`/`extends` interfaces, so reaching it requires
    // walking both the extends chain (classes) and the implements clauses (interfaces).
    private const string ExceptionsTyhpdef = """
        <?tyhpdef

        interface Stringable
        {
            public function __toString(): string;
        }
        interface Throwable extends \Stringable
        {
            public function getMessage(): string;
        }
        class Exception implements \Stringable, \Throwable
        {
            public function __construct(string $message): void;
            public function getMessage(): string;
            public function __toString(): string;
        }
        class LogicException extends \Exception implements \Throwable, \Stringable
        {
        }
        class RuntimeException extends \Exception implements \Throwable, \Stringable
        {
        }
        class BadFunctionCallException extends \LogicException implements \Stringable, \Throwable
        {
        }
        class BadMethodCallException extends \BadFunctionCallException implements \Throwable, \Stringable
        {
        }
        """;

    [Fact]
    public void Throw_FullyQualifiedBuiltinException_NoError()
    {
        // `\BadMethodCallException` reaches `\Throwable` only by walking
        // BadMethodCallException -> BadFunctionCallException -> LogicException -> Exception
        // (which `implements \Throwable`). Every link is a tyhpdef-imported class.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Service {
                public function fail(): void {
                    throw new \BadMethodCallException('nope');
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"a fully-qualified builtin exception is a \\Throwable: {Describe(errors)}");
    }

    [Fact]
    public void Throw_UserClassExtendingBuiltinException_NoError()
    {
        // A user class extending a tyhpdef exception must inherit its \Throwable-ness.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class MyError extends \LogicException {
            }
            class Service {
                public function fail(): void {
                    throw new MyError('nope');
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"a class extending \\LogicException is a \\Throwable: {Describe(errors)}");
    }

    [Fact]
    public void Throw_NamespacedFullyQualifiedException_NoError()
    {
        // The thrown value is referenced by its absolute namespaced name; resolution must walk the
        // qualified-name segments rather than a single lexical lookup.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class DomainError extends \RuntimeException {
            }
            class Service {
                public function fail(): void {
                    throw new \Test\DomainError('nope');
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            $"a namespaced FQN exception extending \\RuntimeException is a \\Throwable: {Describe(errors)}");
    }

    [Fact]
    public void Catch_FullyQualifiedBuiltinException_NoError()
    {
        // The catch clause shares the same \Throwable check; it must accept tyhpdef exceptions.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Service {
                public function run(): void {
                    try {
                        $x = 1;
                    } catch (\RuntimeException $e) {
                    }
                }
            }
            """);

        errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerCatchNotThrowable,
            $"\\RuntimeException is a valid catch type: {Describe(errors)}");
    }

    [Fact]
    public void Catch_Variable_IsTypedAsCaughtException_NotUnresolved()
    {
        // Regression: `catchClause.Variable.Identifier` is never populated on `PhpVariableAst`
        // (the real name lives on `VariableToken`), so the catch-bound variable was always stored
        // under the empty-string key. Every read of `$e` inside the catch body then missed the
        // lookup and fell back to `unresolved` (gradually assignable to anything), silently
        // defeating type checking on the caught exception for its entire lifetime in the block.
        // Assigning `$e` to an incompatible target must now be rejected.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Service {
                public function run(): void {
                    try {
                        $x = 1;
                    } catch (\RuntimeException $e) {
                        int $bad = $e;
                    }
                }
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerTypeMismatch,
            $"a caught \\RuntimeException must not be assignable to int once correctly typed: {Describe(errors)}");
    }

    [Fact]
    public void Catch_Variable_MethodCallOnCaughtException_TypeChecksArgument()
    {
        // Same root cause as above, exercised through a method call rather than an assignment:
        // `$e->getMessage()` must resolve through the real `\RuntimeException` type (and its
        // return type used in string context), not silently pass because `$e` was `unresolved`.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Service {
                public function run(): string {
                    try {
                        $x = 1;
                    } catch (\RuntimeException $e) {
                        return $e->getMessage();
                    }
                    return '';
                }
            }
            """);

        errors.Should().BeEmpty(
            $"getMessage() on a correctly-typed caught \\RuntimeException returns string: {Describe(errors)}");
    }

    [Fact]
    public void Throw_NonThrowableClass_StillReportsError()
    {
        // Guard: now that \Throwable resolves precisely (instead of an "any object" fallback),
        // throwing a plain class must still be rejected.
        var errors = CompileAndCheck("""
            <?tyhp
            namespace Test;
            class Plain {
            }
            class Service {
                public function fail(): void {
                    throw new Plain();
                }
            }
            """);

        errors.Should().Contain(
            d => d.Code == MessageCode.CheckerThrowNotThrowable,
            "a plain class that is not in the \\Throwable hierarchy must not be throwable");
    }

    private static string Describe(IReadOnlyList<IDiagnostic> errors) =>
        string.Join("; ", errors.Select(e => $"{e.Code}: {e.Message}"));

    /// <summary>
    /// Compiles and checks a self-contained snippet together with the <see cref="ExceptionsTyhpdef"/>
    /// fixture (which supplies the global exception hierarchy as imported tyhpdef types) and returns
    /// only the diagnostics that originate from the snippet file.
    /// </summary>
    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var fileName = Guid.NewGuid().ToString("N") + ".tyhp";
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        var tyhpdefPath = Path.Combine(tempDir, "exceptions.tyhpdef");
        File.WriteAllText(tyhpdefPath, ExceptionsTyhpdef);

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
            var result = compilationService.ParseFiles([tyhpdefPath, filePath], options);
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
