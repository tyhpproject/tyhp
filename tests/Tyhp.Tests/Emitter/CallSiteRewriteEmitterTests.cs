using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Emitter;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Emitter;

/// <summary>
/// Story 11 §12 acceptance — focused call-site rewrite contract for operator overloads and
/// extension methods. Broader edge cases live in <see cref="OperatorOverloadEmitterTests"/> and
/// <see cref="ExtensionMethodEmitterTests"/>; this suite re-asserts the core rewrite matrix in one place.
/// </summary>
[Trait("Category", "Emitter")]
public class CallSiteRewriteEmitterTests
{
    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "callsites.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject();
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .Where(d => d.Code != MessageCode.CheckerIncompatibleReturnType)
                .Where(d => d.Code != MessageCode.CheckerTypeMismatch)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, project);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.4",
            })
            .Build();
        return new Project(configuration);
    }

    // --- Operator overload call-site rewrites ---

    [Fact]
    public void Operator_BinaryAdd_RewritesToStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function add(Money $a): Money {
                return $a + 10;
            }
            """);

        php.Should().Contain("\\Money::__add($a, 10)");
        php.Should().Contain("public static function __add(");
        php.Should().NotContain("$a + 10");
    }

    [Fact]
    public void Operator_CompoundAssign_ExpandsToStaticAssignment()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self {
                    $left->amount = $left->amount + $right;
                    return $left;
                }
            }
            function grow(Money $a): void {
                $a += 10;
            }
            """);

        php.Should().Contain("$a = \\Money::__add($a, 10)");
        php.Should().NotContain("$a += 10");
    }

    [Fact]
    public void Operator_Cast_RewritesToInstanceConvertTo()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator convert(self $value): int {
                    return $value->amount;
                }
            }
            function toInt(Money $m): int {
                return (int)$m;
            }
            """);

        php.Should().Contain("$m->__toInt()");
        php.Should().Contain("function __toInt()");
        php.Should().NotContain("(int)$m");
    }

    [Fact]
    public void Operator_Cast_StandaloneExtension_RewritesToStaticConvertToOnExtension()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOperators extends Money {
                operator convert (self $value): int {
                    int $n = $value->amount;
                    return $n;
                }
            }
            function toInt(Money $m): int {
                return (int)$m;
            }
            """);

        php.Should().Contain("\\MoneyOperators::__toInt($m)");
        php.Should().Contain("public static function __toInt(");
        php.Should().NotContain("$m->__toInt()");
        php.Should().NotContain("(int)$m");
    }

    [Fact]
    public void Operator_ImplicitConvertFrom_StandaloneExtension_RewritesToStaticFromOnExtension()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyOperators extends Money {
                operator convert (int $value) {
                    Money $m = new Money();
                    $m->amount = $value;
                    return $m;
                }
            }
            function takeMoney(Money $m): void {}
            function pass(int $n): void {
                takeMoney($n);
            }
            """);

        php.Should().Contain("takeMoney(\\MoneyOperators::__from($n))");
        php.Should().Contain("public static function __from(");
        php.Should().NotContain("\\Money::__from(");
        php.Should().NotContain("takeMoney($n);");
    }

    [Fact]
    public void Operator_UnaryNot_RewritesToStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Flag {
                public bool $on = false;
                operator !(self $value): bool {
                    return !$value->on;
                }
            }
            function invert(Flag $f): bool {
                return !$f;
            }
            """);

        php.Should().Contain("\\Flag::__not($f)");
        php.Should().Contain("public static function __not(");
    }

    // --- Extension method call-site rewrites ---

    [Fact]
    public void Extension_InstanceCall_RewritesToStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyFormatting extends Money {
                function format(string $currency): string {
                    return $currency . ' ' . $this->amount;
                }
            }
            function show(Money $m): string {
                return $m->format('USD');
            }
            """);

        php.Should().Contain("$m->amount");
        php.Should().Contain("'USD'");
        php.Should().NotContain("$m->format(");
        php.Should().Contain("class MoneyFormatting");
        php.Should().Contain("function format(");
    }

    [Fact]
    public void Extension_ScalarReceiver_RewritesToStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringExtensions extends string {
                function toCamelCase(): string {
                    return $this;
                }
            }
            function convert(string $text): string {
                return $text->toCamelCase();
            }
            """);

        php.Should().Contain("return $text;");
        php.Should().NotContain("$text->toCamelCase(");
        php.Should().Contain("class StringExtensions");
        php.Should().Contain("function toCamelCase(");
    }

    [Fact]
    public void Extension_ChainedCalls_NestStaticCalls()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringExtensions extends string {
                function toSnakeCase(): string {
                    return $this;
                }
                function truncate(int $maxLength): string {
                    return $this;
                }
            }
            function pipe(string $input): string {
                return $input->toSnakeCase()->truncate(50);
            }
            """);

        php.Should().Contain("return $input;");
        php.Should().NotContain("->toSnakeCase(");
        php.Should().NotContain("->truncate(");
        php.Should().Contain("class StringExtensions");
        php.Should().Contain("function toSnakeCase(");
        php.Should().Contain("function truncate(");
    }

    // --- Tyhpdef operator: native passthrough vs mapped extension ---

    [Fact]
    public void Tyhpdef_NativeOperator_DoesNotRewriteCallSite()
    {
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            namespace Ext;
            final class NativeDec {
                operator +(self $left, self $right): self;
            }
            """,
            """
            <?tyhp
            use Ext\NativeDec;
            function sum(NativeDec $a, NativeDec $b): NativeDec {
                return $a + $b;
            }
            """);

        php.Should().Contain("$a + $b");
        php.Should().NotContain("__add");
    }

    [Fact]
    public void Tyhpdef_ExtensionOperatorWithBody_SplicesCallSite()
    {
        var php = CompileAndEmitWithTyhpdef(
            """
            <?tyhpdef
            namespace Lib;
            final class Money {
                public function plus(Money $other): Money;
                extension operator +(self $left, self $right): self => $left->plus($right);
            }
            """,
            """
            <?tyhp
            use Lib\Money;
            function sum(Money $a, Money $b): Money {
                return $a + $b;
            }
            """);

        php.Should().Contain("$a->plus($b)");
        php.Should().NotContain("$a + $b");
        php.Should().NotContain("__add");
        php.Should().NotContain("__TyhpInlineExt_");
    }

    [Fact]
    public void Tyhpdef_BodylessExtensionOperator_IsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "bad.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, "use.tyhp");
        File.WriteAllText(tyhpdefPath, """
            <?tyhpdef
            namespace Ext;
            final class Bad {
                extension operator +(self $left, self $right): self;
            }
            """);
        File.WriteAllText(tyhpPath, """
            <?tyhp
            use Ext\Bad;
            function f(Bad $a, Bad $b): Bad { return $a; }
            """);

        try
        {
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([tyhpdefPath, tyhpPath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            result.Diagnostics.Errors.Should().Contain(d =>
                d.Code == MessageCode.TyhpdefExtensionOperatorRequiresBody
                && (d.Message ?? "").Contains("thin", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static string CompileAndEmitWithTyhpdef(string tyhpdef, string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "types.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, "main.tyhp");
        File.WriteAllText(tyhpdefPath, tyhpdef);
        File.WriteAllText(tyhpPath, tyhp);

        try
        {
            var project = CreateProject();
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([tyhpdefPath, tyhpPath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .Where(d => d.Code != MessageCode.CheckerIncompatibleReturnType)
                .Where(d => d.Code != MessageCode.CheckerTypeMismatch)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(result.GlobalScope, result.Diagnostics, project);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
