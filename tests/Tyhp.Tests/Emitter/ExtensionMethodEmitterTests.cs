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

[Trait("Category", "Emitter")]
public class ExtensionMethodEmitterTests
{
    private static string CompileAndEmit(string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "extension.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject();
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], new CompilationOptions
            {
                EnableAstCache = false,
                PhpVersion = "8.4",
                ProjectPath = TestFileManager.GetRepoRoot(),
                TyhpdefIncludePaths = TestFileManager.GetDevPackageManifestIncludes(),
            });

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .Where(d => d.Code != MessageCode.CheckerIncompatibleReturnType)
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

    [Fact]
    public void Emit_ClassReceiver_RewritesToStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyFormatting {
                function format(extends Money $this, string $currency): string {
                    return $currency . ' ' . $this->amount;
                }
            }
            function show(Money $m): string {
                return $m->format('USD');
            }
            """);

        php.Should().Contain(@"\MoneyFormatting::format($m, 'USD')");
        php.Should().NotContain("$m->format(");
    }

    [Fact]
    public void Emit_ThisReceiver_CallingExtensionFromOwnMethod_RewritesToStaticCall()
    {
        // `$this->format(...)` from inside the extended class's own method: `$this` is never
        // registered into typed-var maps / bound to a symbol, so without a class-stack-backed
        // special case in `ResolveReceiverType` this silently stayed an (invalid) instance call.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                public function describe(): string {
                    return $this->format('USD');
                }
            }
            extension MoneyFormatting {
                function format(extends Money $this, string $currency): string {
                    return $currency . ' ' . $this->amount;
                }
            }
            """);

        php.Should().Contain(@"\MoneyFormatting::format($this, 'USD')");
        php.Should().NotContain("$this->format(");
    }

    [Fact]
    public void Emit_ScalarStringReceiver_RewritesToStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringExtensions {
                function toCamelCase(extends string $this): string {
                    return $this;
                }
            }
            function convert(string $text): string {
                return $text->toCamelCase();
            }
            """);

        php.Should().Contain(@"\StringExtensions::toCamelCase($text)");
        php.Should().NotContain("$text->toCamelCase(");
    }

    [Fact]
    public void Emit_StringLiteralReceiver_RewritesToStaticCall()
    {
        // Quoted string literals are PhpEncapsListAst (not PhpScalarAst); receiver typing
        // must still resolve them as `string` so `$lit->ext()` rewrites.
        var php = CompileAndEmit("""
            <?tyhp
            namespace TestEmitter;

            extension StringUtils {
                function toCamelCase(extends string $str): string {
                    return $str;
                }
            }

            function demo(): void {
                $result = "hello world"->toCamelCase();
            }
            """);

        php.Should().Match(s =>
            s.Contains(@"\TestEmitter\StringUtils::toCamelCase(""hello world"")")
            || s.Contains(@"\TestEmitter\StringUtils::toCamelCase('hello world')")
            || s.Contains(@"\StringUtils::toCamelCase(""hello world"")")
            || s.Contains(@"\StringUtils::toCamelCase('hello world')"));
        php.Should().NotContain("->toCamelCase(");
    }

    [Fact]
    public void Emit_ScalarIntReceiver_RewritesToStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension IntExtensions {
                function twice(extends int $this): int {
                    return $this * 2;
                }
            }
            function grow(int $n): int {
                return $n->twice();
            }
            """);

        php.Should().Contain(@"\IntExtensions::twice($n)");
        php.Should().NotContain("$n->twice(");
    }

    [Fact]
    public void Emit_NullableReceiver_RewritesUsingNonNullComponent()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringExtensions {
                function shout(extends string $this): string {
                    return $this;
                }
            }
            function yell(?string $text): string {
                return $text->shout();
            }
            """);

        php.Should().Contain(@"\StringExtensions::shout($text)");
        php.Should().NotContain("$text->shout(");
    }

    [Fact]
    public void Emit_ChainedExtensionCalls_NestStaticCalls()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringExtensions {
                function toSnakeCase(extends string $this): string {
                    return $this;
                }
                function truncate(extends string $this, int $maxLength): string {
                    return $this;
                }
            }
            function pipe(string $input): string {
                return $input->toSnakeCase()->truncate(50);
            }
            """);

        php.Should().Contain(@"\StringExtensions::truncate(\StringExtensions::toSnakeCase($input), 50)");
        php.Should().NotContain("->toSnakeCase(");
        php.Should().NotContain("->truncate(");
    }

    [Fact]
    public void Emit_ThreeHopChain_NestsInnermostFirst()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringExtensions {
                function trimExt(extends string $this): string {
                    return $this;
                }
                function toSnakeCase(extends string $this): string {
                    return $this;
                }
                function truncate(extends string $this, int $maxLength): string {
                    return $this;
                }
            }
            function pipe(string $input): string {
                return $input->trimExt()->toSnakeCase()->truncate(50);
            }
            """);

        php.Should().Contain(
            @"\StringExtensions::truncate(\StringExtensions::toSnakeCase(\StringExtensions::trimExt($input)), 50)");
    }

    [Fact]
    public void Emit_NullSafeExtensionCall_EmitsNullGuardWithTemp()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyFormatting {
                function format(extends Money $this, string $currency): string {
                    return $currency . ' ' . $this->amount;
                }
            }
            function show(?Money $m): ?string {
                return $m?->format('USD');
            }
            """);

        // Null-safe must not call the extension with a null receiver.
        php.Should().Contain("=== null)");
        php.Should().Contain("? null :");
        php.Should().Contain(@"\MoneyFormatting::format($__recv");
        php.Should().Contain("$__recv");
        php.Should().NotContain("$m?->format(");
        php.Should().NotContain(@"\MoneyFormatting::format($m,");
    }

    [Fact]
    public void Emit_NullSafeExtensionCall_SideEffectingReceiver_EvaluatesOnce()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            class Wallet {
                public function current(): ?Money {
                    return null;
                }
            }
            extension MoneyFormatting {
                function format(extends Money $this): string {
                    return (string)$this->amount;
                }
            }
            function show(Wallet $w): ?string {
                return $w->current()?->format();
            }
            """);

        // Receiver `$w->current()` must be bound once, then null-checked.
        php.Should().Contain("= $w->current()) === null)");
        php.Should().Contain(@"\MoneyFormatting::format($__recv");
        php.Should().NotContain("$w->current()?->format(");
    }

    [Fact]
    public void Emit_NullSafeChainedExtensionCalls_NestNullGuards()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringExtensions {
                function toSnakeCase(extends string $this): string {
                    return $this;
                }
                function truncate(extends string $this, int $maxLength): string {
                    return $this;
                }
            }
            function pipe(?string $input): ?string {
                return $input?->toSnakeCase()?->truncate(50);
            }
            """);

        php.Should().Contain("=== null)");
        php.Should().Contain(@"\StringExtensions::toSnakeCase($__recv");
        php.Should().Contain(@"\StringExtensions::truncate($__recv");
        php.Should().NotContain("?->toSnakeCase(");
        php.Should().NotContain("?->truncate(");
        // Outer hop must not leave a dangling nullsafe accessor with an empty base.
        php.Should().NotContain("return ?->");
    }

    [Fact]
    public void Emit_NullSafeExtensionThenRegularArrowRealMethod_ShortCircuitsWholeChain()
    {
        // Native PHP's `?->` short-circuits the *entire remainder* of the chain, including a
        // plain `->` that follows it (`$s?->asMoney()->display()` never calls `display()` when
        // `$s` is null). The nullsafe extension rewrite must upgrade this trailing `->` to `?->`
        // so it keeps participating in that short-circuit instead of calling a real method on a
        // possibly-null value.
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                public function display(): string {
                    return (string)$this->amount;
                }
            }
            extension MoneyFactory {
                function asMoney(extends string $this): Money {
                    return new Money();
                }
            }
            function show(?string $s): ?string {
                return $s?->asMoney()->display();
            }
            """);

        php.Should().Contain(@"\MoneyFactory::asMoney($__recv");
        php.Should().Contain(")?->display()");
        php.Should().NotContain(")->display()");
    }

    [Fact]
    public void Emit_NullSafeExtensionThenRegularArrowExtension_NestsNullGuard()
    {
        // `$input?->toSnakeCase()->truncate(50)` uses a plain `->` for the second (extension)
        // hop, but PHP's `?->` still protects it: if `$input` is null, `truncate` must never be
        // called with a null receiver (its `extends string $this` parameter is non-nullable and
        // would otherwise blow up with a TypeError under strict_types).
        var php = CompileAndEmit("""
            <?tyhp
            extension StringExtensions {
                function toSnakeCase(extends string $this): string {
                    return $this;
                }
                function truncate(extends string $this, int $maxLength): string {
                    return $this;
                }
            }
            function pipe(?string $input): string {
                return $input?->toSnakeCase()->truncate(50);
            }
            """);

        php.Should().Contain(@"\StringExtensions::toSnakeCase($__recv");
        php.Should().Contain(@"\StringExtensions::truncate($__recv");
        // The second hop's receiver must itself be null-checked, not the raw (possibly-null)
        // first-hop result passed straight through.
        php.Should().MatchRegex(@"=== null\) \? null : \\StringExtensions::truncate\(\$__recv_\d+, 50\)");
    }

    [Fact]
    public void Emit_RegularInstanceMethod_IsNotRewritten()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Widget {
                public function label(): string {
                    return 'w';
                }
            }
            function read(Widget $w): string {
                return $w->label();
            }
            """);

        php.Should().Contain("$w->label()");
        php.Should().NotContain("::label($w");
    }

    [Fact]
    public void Emit_ReceiverNamedThis_RenamesToThisUnderscore()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyFormatting {
                function format(extends Money $this, string $currency): string {
                    return $currency . ' ' . $this->amount;
                }
                function shout(extends string $this): string {
                    $fn = function () use ($this): string {
                        return $this;
                    };
                    return $fn();
                }
            }
            """);

        // Signature + body must not keep `$this` as a static-method parameter (PHP fatal).
        php.Should().Contain("function format(\\Money $this_, string $currency): string");
        php.Should().Contain("$this_->amount");
        php.Should().Contain("function shout(string $this_): string");
        php.Should().Contain("use ($this_)");
        php.Should().Contain("return $this_;");
        php.Should().NotContain("function format(\\Money $this,");
        php.Should().NotContain("function shout(string $this)");
        php.Should().NotContain("$this->amount");
        php.Should().NotContain("use ($this)");
    }

    [Fact]
    public void Emit_ReceiverNamedThis_WithSiblingParamAlreadyNamedThisUnderscore_AvoidsCollision()
    {
        // Regression: if the author already has a real parameter literally named `$this_`
        // alongside a `$this` receiver, the receiver rename must not collide with it — that would
        // emit two PHP parameters with the same name (fatal parse error: "redefinition of
        // parameter").
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyFormatting {
                function format(extends Money $this, string $this_): string {
                    return $this_ . ' ' . $this->amount;
                }
            }
            """);

        php.Should().Contain("function format(\\Money $this__, string $this_): string");
        php.Should().Contain("$this__->amount");
        php.Should().Contain("$this_ . ' '");
        php.Should().NotMatch("*function format(\\Money $this_, string $this_)*");
    }
}
