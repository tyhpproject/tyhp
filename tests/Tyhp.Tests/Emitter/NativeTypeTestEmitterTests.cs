using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 21.12 B — <c>$x is T</c> emit follows <c>#[\Tyhp\NativeTypeTest]</c>, then
/// class/interface/enum/trait <c>instanceof</c>, else <c>\Tyhp\Type::is</c>.
/// </summary>
[Trait("Category", "Emitter")]
public class NativeTypeTestEmitterTests
{
    [Fact]
    public void Emit_IsString_UsesIsStringWhenMarked()
    {
        var php = CompileAndEmitWithMarkedScalars("""
            <?tyhp
            function demo(mixed $x): bool {
                return $x is string;
            }
            """);

        php.Should().Contain("\\is_string($x)");
        php.Should().NotContain("\\Tyhp\\Type::is($x");
        php.Should().NotContain("$x instanceof string");
    }

    [Fact]
    public void Emit_IsInt_UsesIsIntNeverIntegerOrLong()
    {
        var php = CompileAndEmitWithMarkedScalars("""
            <?tyhp
            function demo(mixed $x): bool {
                return $x is int;
            }
            """);

        php.Should().Contain("\\is_int($x)");
        php.Should().NotContain("\\is_integer(");
        php.Should().NotContain("\\is_long(");
        php.Should().NotContain("\\Tyhp\\Type::is($x");
    }

    [Fact]
    public void Emit_IsNull_UsesIsNullWhenMarked()
    {
        var php = CompileAndEmitWithMarkedScalars("""
            <?tyhp
            function demo(mixed $x): bool {
                return $x is null;
            }
            """);

        php.Should().Contain("\\is_null($x)");
        php.Should().NotContain("\\Tyhp\\Type::is($x");
    }

    [Fact]
    public void Emit_UnmarkedUserGuardOnClass_KeepsInstanceof()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {}
            function isMoney(mixed $v): $v is Money {
                return $v instanceof Money;
            }
            function demo(mixed $x): bool {
                return $x is Money;
            }
            """);

        php.Should().Contain("$x instanceof Money");
        php.Should().NotContain("\\isMoney($x)");
        php.Should().NotContain("\\Tyhp\\Type::is($x");
    }

    [Fact]
    public void Emit_UnmarkedUserGuardOnStruct_KeepsTypeIs()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Money = struct {
                int $cents;
            };
            function isMoney(mixed $v): $v is Money {
                return true;
            }
            function demo(mixed $x): bool {
                return $x is Money;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($x");
        php.Should().NotContain("\\isMoney($x)");
        php.Should().NotContain("$x instanceof Money");
    }

    [Fact]
    public void Emit_MarkedUserGuard_UsesFunctionCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class PositiveInt {}
            #[\Tyhp\NativeTypeTest]
            function isPositiveInt(mixed $v): $v is PositiveInt {
                return $v instanceof PositiveInt;
            }
            function demo(mixed $x): bool {
                return $x is PositiveInt;
            }
            """);

        php.Should().Contain("\\isPositiveInt($x)");
        php.Should().NotContain("$x instanceof PositiveInt");
        php.Should().NotContain("\\Tyhp\\Type::is($x");
    }

    [Fact]
    public void Emit_MarkedStaticMethod_UsesStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class PositiveInt {}
            class Tests {
                #[\Tyhp\NativeTypeTest]
                public static function isPositiveInt(mixed $v): $v is PositiveInt {
                    return $v instanceof PositiveInt;
                }
            }
            function demo(mixed $x): bool {
                return $x is PositiveInt;
            }
            """);

        php.Should().Contain("\\Tests::isPositiveInt($x)");
        php.Should().NotContain("$x instanceof PositiveInt");
        php.Should().NotContain("\\Tyhp\\Type::is($x");
        php.Should().NotContain("->isPositiveInt(");
    }

    [Fact]
    public void Emit_MarkedStaticMethod_OwnBodyDoesNotSelfRecurse()
    {
        // The host's own body is the real implementation of the guard: `$v instanceof
        // PositiveInt` inside `isPositiveInt` must stay native `instanceof`, not lower into a
        // call back into `isPositiveInt` itself (infinite recursion at runtime).
        var php = CompileAndEmit("""
            <?tyhp
            class PositiveInt {}
            class Tests {
                #[\Tyhp\NativeTypeTest]
                public static function isPositiveInt(mixed $v): $v is PositiveInt {
                    return $v instanceof PositiveInt;
                }
            }
            """);

        php.Should().Contain("$v instanceof PositiveInt");
        php.Should().NotContain("\\Tests::isPositiveInt($v)");
    }

    [Fact]
    public void Emit_MarkedFunction_OwnBodyDoesNotSelfRecurse()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class PositiveInt {}
            #[\Tyhp\NativeTypeTest]
            function isPositiveInt(mixed $v): $v is PositiveInt {
                return $v instanceof PositiveInt;
            }
            """);

        php.Should().Contain("$v instanceof PositiveInt");
        php.Should().NotContain("\\isPositiveInt($v)");
    }

    [Fact]
    public void Emit_UnmarkedClass_UsesInstanceof()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class User {}
            function demo(mixed $x): bool {
                return $x is User;
            }
            """);

        php.Should().Contain("$x instanceof User");
        php.Should().NotContain("\\Tyhp\\Type::is($x");
    }

    [Fact]
    public void Emit_NullableString_StillTypeIs()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function demo(mixed $x): bool {
                return $x is ?string;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($x, \\Tyhp\\Type::nullable(\\Tyhp\\Type::string()))");
        php.Should().NotContain("\\is_string($x)");
    }

    [Fact]
    public void Emit_AliasUnion_StillTypeIs()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Numeric = int|string;
            function demo(mixed $x): bool {
                return $x is Numeric;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($x, Numeric())");
        php.Should().NotContain("\\is_int($x)");
        php.Should().NotContain("\\is_string($x)");
    }

    [Fact]
    public void Emit_GenericParam_StillTypeIs()
    {
        var php = CompileAndEmit("""
            <?tyhp
            function pick<T>(mixed $t): bool {
                return $t is T;
            }
            """);

        php.Should().Contain("\\Tyhp\\Type::is($t, $__generic_T)");
        php.Should().NotContain("$t instanceof T");
    }

    [Fact]
    public void Emit_TransparentAliasOfString_UsesIsStringWhenMarked()
    {
        var php = CompileAndEmitWithMarkedScalars("""
            <?tyhp
            type UserId = string;
            function demo(mixed $x): bool {
                return $x is UserId;
            }
            """);

        php.Should().Contain("\\is_string($x)");
        php.Should().NotContain("\\Tyhp\\Type::is($x, UserId())");
    }

    [Fact]
    public void OverlayFile_StampsCanonicalIsFunctions()
    {
        const string overlay = """
            <?tyhpdef
            // @overlay-against: function is_array(mixed $value): bool
            #[\Tyhp\NativeTypeTest]
            // @overlay-against: function is_array(mixed $value): bool
            function is_array(mixed $value): $value is array;

            // @overlay-against: function is_bool(mixed $value): bool
            #[\Tyhp\NativeTypeTest]
            // @overlay-against: function is_bool(mixed $value): bool
            function is_bool(mixed $value): $value is bool;

            // @overlay-against: function is_callable(mixed $value, bool $syntax_only, mixed $callable_name): bool
            #[\Tyhp\NativeTypeTest]
            // @overlay-against: function is_callable(mixed $value, bool $syntax_only, mixed $callable_name): bool
            function is_callable(mixed $value, false $syntax_only = false, mixed &$callable_name = null): $value is callable;

            // @overlay-against: function is_callable(mixed $value, bool $syntax_only, mixed $callable_name): bool
            function is_callable(mixed $value, true $syntax_only, mixed &$callable_name = null): bool;

            // @overlay-against: function is_countable(mixed $value): bool
            function is_countable(mixed $value): $value is array|\Countable;

            // @overlay-against: function is_double(mixed $value): bool
            function is_double(mixed $value): $value is float;

            // @overlay-against: function is_float(mixed $value): bool
            #[\Tyhp\NativeTypeTest]
            // @overlay-against: function is_float(mixed $value): bool
            function is_float(mixed $value): $value is float;

            // @overlay-against: function is_int(mixed $value): bool
            #[\Tyhp\NativeTypeTest]
            // @overlay-against: function is_int(mixed $value): bool
            function is_int(mixed $value): $value is int;

            // @overlay-against: function is_integer(mixed $value): bool
            function is_integer(mixed $value): $value is int;

            // @overlay-against: function is_iterable(mixed $value): bool
            function is_iterable(mixed $value): $value is array|\Traversable;

            // @overlay-against: function is_long(mixed $value): bool
            function is_long(mixed $value): $value is int;

            // @overlay-against: function is_null(mixed $value): bool
            #[\Tyhp\NativeTypeTest]
            // @overlay-against: function is_null(mixed $value): bool
            function is_null(mixed $value): $value is null;

            // @overlay-against: function is_numeric(mixed $value): bool
            function is_numeric(mixed $value): $value is int|float|string;

            // @overlay-against: function is_object(mixed $value): bool
            #[\Tyhp\NativeTypeTest]
            // @overlay-against: function is_object(mixed $value): bool
            function is_object(mixed $value): $value is object;

            // @overlay-against: function is_resource(mixed $value): bool
            #[\Tyhp\NativeTypeTest]
            // @overlay-against: function is_resource(mixed $value): bool
            function is_resource(mixed $value): $value is resource;

            // @overlay-against: function is_scalar(mixed $value): bool
            function is_scalar(mixed $value): $value is int|float|string|bool;

            // @overlay-against: function is_string(mixed $value): bool
            #[\Tyhp\NativeTypeTest]
            // @overlay-against: function is_string(mixed $value): bool
            function is_string(mixed $value): $value is string;
            """;

        AssertNativeTypeTestStamps(overlay, "function is_string(");
        AssertNativeTypeTestStamps(overlay, "function is_int(");
        AssertNativeTypeTestStamps(overlay, "function is_null(");
        AssertNativeTypeTestStamps(overlay, "function is_float(");
        AssertNativeTypeTestStamps(overlay, "function is_bool(");
        AssertNativeTypeTestStamps(overlay, "function is_array(");
        AssertNativeTypeTestStamps(overlay, "function is_object(");
        AssertNativeTypeTestStamps(overlay, "function is_resource(");
        AssertNativeTypeTestStamps(
            overlay, "function is_callable(mixed $value, false $syntax_only = false");
        overlay.Should().NotContain("#[\\Tyhp\\NativeTypeTest]\nfunction is_integer(");
        overlay.Should().NotContain("#[\\Tyhp\\NativeTypeTest]\nfunction is_long(");
        overlay.Should().NotContain("#[\\Tyhp\\NativeTypeTest]\nfunction is_double(");
        overlay.Should().NotContain("#[\\Tyhp\\NativeTypeTest]\nfunction is_numeric(");
        overlay.Should().NotContain("#[\\Tyhp\\NativeTypeTest]\nfunction is_scalar(");
        overlay.Should().NotContain("#[\\Tyhp\\NativeTypeTest]\nfunction is_countable(");
        overlay.Should().NotContain("#[\\Tyhp\\NativeTypeTest]\nfunction is_iterable(");
        overlay.Should().NotContain("#[\\Tyhp\\NativeTypeTest]\nfunction is_callable(mixed $value, true $syntax_only");
    }

    /// <summary>
    /// The attribute sits on the function. An <c>@overlay-against</c> stamp comment may
    /// sit between them.
    /// </summary>
    private static void AssertNativeTypeTestStamps(string overlay, string functionHeader)
    {
        var pattern =
            @"#\[\\Tyhp\\NativeTypeTest\]\s*(?://[^\n]*\n\s*)*"
            + Regex.Escape(functionHeader);
        Regex.IsMatch(overlay, pattern).Should().BeTrue(
            $"expected #[\\Tyhp\\NativeTypeTest] on {functionHeader}");
    }

    private static string CompileAndEmit(string tyhp) =>
        string.Join('\n', CompileIsolated(tyhp).Select(f => f.GeneratedContent ?? ""));

    private static string CompileAndEmitWithMarkedScalars(string tyhp) =>
        string.Join('\n', CompileOverlay(tyhp, markedScalars: true).Select(f => f.GeneratedContent ?? ""));

    private static IReadOnlyList<PHPOutputFile> CompileIsolated(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "nativetypetest.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = new Project(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = "8.4",
                })
                .Build());
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets,
                inferredClosureSignatures: result.InferredClosureSignatures,
                expressionTypes: result.ExpressionTypes,
                nativeTypeTests: result.NativeTypeTests);
            return new TyhpEmitter(context).Emit(result.ParsedFiles!);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static IReadOnlyList<PHPOutputFile> CompileOverlay(string tyhp, bool markedScalars)
    {
        using var builder = IsolatedCompilation.CreateOverlayProject(
            phpVersion: "8.4",
            includeTyhpdef: MarkedScalarInclude,
            overlayTyhpdef: markedScalars ? MarkedScalarOverlay : """
                <?tyhpdef
                """,
            userTyhp: tyhp);

        var result = IsolatedCompilation.BindProject(builder, skipChecking: false);
        var unexpectedErrors = result.Diagnostics.Errors
            .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
            .ToList();
        unexpectedErrors.Should().BeEmpty(
            $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

        result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();
        var project = builder.BuildProject();
        var context = EmitContext.Create(
            result.GlobalScope,
            result.Diagnostics,
            project,
            result.RequiresRuntimeGenericTracking,
            requiresGenericVariant: result.RequiresGenericVariant,
            genericCallTargets: result.GenericCallTargets,
            inferredClosureSignatures: result.InferredClosureSignatures,
            expressionTypes: result.ExpressionTypes,
            nativeTypeTests: result.NativeTypeTests);
        return new TyhpEmitter(context).Emit(result.ParsedFiles!);
    }

    private const string MarkedScalarInclude = """
        <?tyhpdef
        #[\Attribute]
        class Attribute {
            public const int TARGET_CLASS ?? 1;
            public const int TARGET_FUNCTION ?? 2;
            public const int TARGET_METHOD ?? 4;
            public const int TARGET_ALL ?? 63;
            public function __construct(int $flags = 0): void;
        }

        namespace Tyhp {
            #[\Attribute(\Attribute::TARGET_FUNCTION | \Attribute::TARGET_METHOD)]
            final class NativeTypeTest {
                public function __construct(): void;
            }
        }

        function is_string(mixed $value): $value is string;
        function is_int(mixed $value): $value is int;
        function is_integer(mixed $value): $value is int;
        function is_long(mixed $value): $value is int;
        function is_null(mixed $value): $value is null;
        """;

    private const string MarkedScalarOverlay = """
        <?tyhpdef
        #[\Tyhp\NativeTypeTest]
        function is_string(mixed $value): $value is string;

        #[\Tyhp\NativeTypeTest]
        function is_int(mixed $value): $value is int;

        #[\Tyhp\NativeTypeTest]
        function is_null(mixed $value): $value is null;
        """;
}
