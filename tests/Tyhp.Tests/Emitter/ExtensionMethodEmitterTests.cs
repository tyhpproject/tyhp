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
    private static string CompileAndEmit(string tyhp, string optimize = "none")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "extension.tyhp");
        File.WriteAllText(filePath, tyhp);

        try
        {
            var project = CreateProject(optimize);
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles([filePath], IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4"));

            var unexpectedErrors = result.Diagnostics.Errors
                .Where(d => !(d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.Ordinal))
                .Where(d => d.Code != MessageCode.BinderUnresolvedParameterType)
                .Where(d => d.Code != MessageCode.CheckerIncompatibleReturnType)
                .ToList();
            unexpectedErrors.Should().BeEmpty(
                $"unexpected errors: {string.Join(", ", unexpectedErrors.Select(e => $"{e.Code}: {e.Message}"))}");

            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                expressionTypes: result.ExpressionTypes);
            var outputFiles = new TyhpEmitter(context).Emit(result.ParsedFiles!);
            return string.Join('\n', outputFiles.Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static Project CreateProject(string optimize = "none")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["output:phpVersion"] = "8.4",
                ["build:optimize"] = optimize,
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
            extension MoneyFormatting extends Money {
                function format(string $currency): string {
                    return $currency . ' ' . $this->amount;
                }
            }
            function show(Money $m): string {
                return $m->format('USD');
            }
            """);

        php.Should().Contain("return (('USD' . ' ') . $m->amount);");
        php.Should().Contain("class MoneyFormatting");
        php.Should().Contain("function format(");
        php.Should().NotContain("$m->format(");
        php.Should().NotContain(@"\MoneyFormatting::format($m");
    }

    [Fact]
    public void Emit_StructReceiver_ExtensionCall_NotArrayKeyCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            extension MoneyFormatting extends Money {
                function format(): string {
                    return (string)$this->cents;
                }
            }
            function show(Money $m): string {
                return $m->format();
            }
            """);

        php.Should().NotContain("$m['format']");
        php.Should().NotContain("$m->format(");
        php.Should().Contain("$m['cents']");
    }

    [Fact]
    public void Emit_ForeachArrayOfStruct_ExtensionCall_NotArrayKeyCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            type Money = struct {
                int $cents = 0;
            };
            extension MoneyFormatting extends Money {
                function format(): string {
                    return (string)$this->cents;
                }
            }
            function showAll(array<Money> $amounts): string {
                string $out = '';
                foreach ($amounts as $m) {
                    $out .= $m->format();
                }
                return $out;
            }
            """);

        php.Should().NotContain("$m['format']");
        php.Should().NotContain("$m->format(");
        php.Should().Contain("$m['cents']");
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
            extension MoneyFormatting extends Money {
                function format(string $currency): string {
                    return $currency . ' ' . $this->amount;
                }
            }
            """);

        php.Should().Contain("return (('USD' . ' ') . $this->amount);");
        php.Should().Contain("class MoneyFormatting");
        php.Should().Contain("function format(");
        php.Should().NotContain("$this->format(");
        php.Should().NotContain(@"\MoneyFormatting::format($this");
    }

    [Fact]
    public void Emit_ScalarStringReceiver_RewritesToStaticCall()
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
        php.Should().Contain("class StringExtensions");
        php.Should().Contain("function toCamelCase(");
        php.Should().NotContain("$text->toCamelCase(");
        php.Should().NotContain(@"\StringExtensions::toCamelCase(");
    }

    [Fact]
    public void Emit_StringLiteralReceiver_RewritesToStaticCall()
    {
        // Quoted string literals are PhpEncapsListAst (not PhpScalarAst); receiver typing
        // must still resolve them as `string` so `$lit->ext()` rewrites.
        var php = CompileAndEmit("""
            <?tyhp
            namespace TestEmitter;

            extension StringUtils extends string {
                function toCamelCase(): string {
                    return $this;
                }
            }

            function demo(): void {
                $result = "hello world"->toCamelCase();
            }
            """);

        // The string literal receiver is spliced into the body (`return $this;` → `return "hello world";`).
        php.Should().Contain("$result = \"hello world\";");
        php.Should().NotContain("->toCamelCase(");
        php.Should().NotContain(@"\StringUtils::toCamelCase(");
        php.Should().NotContain(@"\TestEmitter\StringUtils::toCamelCase(");
    }

    [Fact]
    public void Emit_ScalarIntReceiver_RewritesToStaticCall()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension IntExtensions extends int {
                function twice(): int {
                    return $this * 2;
                }
            }
            function grow(int $n): int {
                return $n->twice();
            }
            """);

        php.Should().Contain("return ($n * 2);");
        php.Should().Contain("class IntExtensions");
        php.Should().Contain("function twice(");
        php.Should().NotContain("$n->twice(");
        php.Should().NotContain(@"\IntExtensions::twice(");
    }

    [Fact]
    public void Emit_NullableReceiver_RewritesUsingNonNullComponent()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringExtensions extends string {
                function shout(): string {
                    return $this;
                }
            }
            function yell(?string $text): string {
                return $text->shout();
            }
            """);

        php.Should().Contain("return $text;");
        php.Should().Contain("class StringExtensions");
        php.Should().Contain("function shout(");
        php.Should().NotContain("$text->shout(");
        php.Should().NotContain(@"\StringExtensions::shout(");
    }

    [Fact]
    public void Emit_ChainedExtensionCalls_NestStaticCalls()
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

        // Both extension bodies are `return $this;`, so the chain splices to just the receiver.
        php.Should().Contain("return $input;");
        php.Should().Contain("class StringExtensions");
        php.Should().Contain("function toSnakeCase(");
        php.Should().Contain("function truncate(");
        php.Should().NotContain("->toSnakeCase(");
        php.Should().NotContain("->truncate(");
        php.Should().NotContain(@"\StringExtensions::toSnakeCase(");
        php.Should().NotContain(@"\StringExtensions::truncate(");
    }

    [Fact]
    public void Emit_ThreeHopChain_NestsInnermostFirst()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringExtensions extends string {
                function trimExt(): string {
                    return $this;
                }
                function toSnakeCase(): string {
                    return $this;
                }
                function truncate(int $maxLength): string {
                    return $this;
                }
            }
            function pipe(string $input): string {
                return $input->trimExt()->toSnakeCase()->truncate(50);
            }
            """);

        // All three extension bodies are `return $this;`, so the chain splices to just the receiver.
        php.Should().Contain("return $input;");
        php.Should().NotContain("->trimExt(");
        php.Should().NotContain("->toSnakeCase(");
        php.Should().NotContain("->truncate(");
        php.Should().NotContain(@"\StringExtensions::trimExt(");
        php.Should().NotContain(@"\StringExtensions::toSnakeCase(");
        php.Should().NotContain(@"\StringExtensions::truncate(");
    }

    [Fact]
    public void Emit_NullSafeExtensionCall_EmitsNullGuardWithTemp()
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
            extension MoneyFormatting extends Money {
                function format(): string {
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
            extension StringExtensions extends string {
                function toSnakeCase(): string {
                    return $this;
                }
                function truncate(int $maxLength): string {
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
            extension MoneyFactory extends string {
                function asMoney(): Money {
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
            extension StringExtensions extends string {
                function toSnakeCase(): string {
                    return $this;
                }
                function truncate(int $maxLength): string {
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
                extends Money {
                    function format(string $currency): string {
                        return $currency . ' ' . $this->amount;
                    }
                }

                extends string {
                    function shout(): string {
                        $fn = function () use ($this): string {
                            return $this;
                        };
                        return $fn();
                    }
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
            extension MoneyFormatting extends Money {
                function format(string $this_): string {
                    return $this_ . ' ' . $this->amount;
                }
            }
            """);

        php.Should().Contain("function format(\\Money $this__, string $this_): string");
        php.Should().Contain("$this__->amount");
        php.Should().Contain("$this_ . ' '");
        php.Should().NotMatch("*function format(\\Money $this_, string $this_)*");
    }

    [Fact]
    public void Emit_ShortSyntaxMember_OmitsPhpBackerMethodAndSplicesCallSite()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringHelpers extends string {
                fn shortProcess(): string => ' ' . $this;
            }
            function show(string $s): string {
                return $s->shortProcess();
            }
            """);

        php.Should().Contain("return (' ' . $s);");
        php.Should().NotContain("$s->shortProcess(");
        php.Should().NotContain("function shortProcess(");
        php.Should().NotContain(@"\StringHelpers::shortProcess(");
        php.Should().NotContain("class StringHelpers");
    }

    [Fact]
    public void Emit_SingleReturnBrace_EmitsBackerAndSplicesCallSite()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringHelpers extends string {
                function simpleProcess(): string {
                    return $this . ' ';
                }
            }
            function show(string $s): string {
                return $s->simpleProcess();
            }
            """);

        php.Should().Contain("return ($s . ' ');");
        php.Should().NotContain("$s->simpleProcess(");
        php.Should().NotContain(@"\StringHelpers::simpleProcess($s");
        php.Should().Contain("class StringHelpers");
        php.Should().Contain("function simpleProcess(string $this_): string");
        php.Should().Contain("return $this_ . ' ';");
    }

    [Fact]
    public void Emit_MultiStatement_EmitsBackerAndCallsIt()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringHelpers extends string {
                function complexStringProcess(): string {
                    string $finalString = \trim($this);
                    return $finalString;
                }
            }
            function show(string $s): string {
                return $s->complexStringProcess();
            }
            """);

        php.Should().Contain(@"\StringHelpers::complexStringProcess($s)");
        php.Should().NotContain("$s->complexStringProcess(");
        php.Should().Contain("class StringHelpers");
        php.Should().Contain("function complexStringProcess(string $this_): string");
        php.Should().Contain(@"\trim($this_)");
    }

    [Fact]
    public void Emit_AllShortSyntax_OmitsBackerClass()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension OnlyShort extends int {
                fn doubled(): int => $this + $this;
                fn label(): string => (string)$this;
            }
            function show(int $n): string {
                return $n->doubled()->label();
            }
            """);

        php.Should().NotContain("class OnlyShort");
        php.Should().NotContain("function doubled(");
        php.Should().NotContain("function label(");
        php.Should().NotContain(@"\OnlyShort::");
        php.Should().NotContain("->doubled(");
        php.Should().NotContain("->label(");
        php.Should().Contain("$n");
        php.Should().Contain("+");
    }

    [Fact]
    public void Emit_MixedMembers_BackerClassContainsOnlyBraceBodies()
    {
        var php = CompileAndEmit("""
            <?tyhp
            extension StringHelpers extends string {
                function complexStringProcess(): string {
                    string $finalString = \trim($this);
                    return $finalString;
                }
                fn shortProcess(): string => ' ' . $this;
                function simpleProcess(): string {
                    return $this . ' ';
                }
            }
            function demo(string $s): string {
                return $s->complexStringProcess() . $s->shortProcess() . $s->simpleProcess();
            }
            """);

        php.Should().Contain("class StringHelpers");
        php.Should().Contain("function complexStringProcess(string $this_): string");
        php.Should().Contain("function simpleProcess(string $this_): string");
        php.Should().NotContain("function shortProcess(");
        php.Should().Contain(@"\StringHelpers::complexStringProcess($s)");
        php.Should().Contain("(' ' . $s)");
        php.Should().Contain("($s . ' ')");
        php.Should().NotContain(@"\StringHelpers::shortProcess(");
        php.Should().NotContain(@"\StringHelpers::simpleProcess($s");
    }

    [Fact]
    public void Emit_ShortSyntax_SplicesAtOptimizeNone()
    {
        var php = CompileAndEmit(
            """
            <?tyhp
            extension MathHelpers extends int {
                fn doubled(): int => $this + $this;
            }
            function f(int $n): int {
                return $n->doubled();
            }
            """,
            optimize: "none");

        php.Should().Contain("return ($n + $n);");
        php.Should().NotContain("$n->doubled(");
        php.Should().NotContain(@"\MathHelpers::doubled(");
        php.Should().NotContain("class MathHelpers");
        php.Should().NotContain("function doubled(");
    }

    [Fact]
    public void Emit_SingleReturnBrace_SplicesAtOptimizeNone()
    {
        var php = CompileAndEmit(
            """
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
            """,
            optimize: "none");

        php.Should().Contain("return (('USD' . ' ') . $m->amount);");
        php.Should().NotContain("$m->format(");
        php.Should().NotContain(@"\MoneyFormatting::format($m");
        php.Should().Contain("class MoneyFormatting");
        php.Should().Contain("function format(");
    }

    [Fact]
    public void Emit_ShortExtensionOperator_OmitsPhpBackerAndSplicesCallSite()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                public function plus(Money $other): Money {
                    return $this;
                }
            }
            extension MoneyOps extends Money {
                operator + (self $left, self $right): Money => $left->plus($right);
            }
            function sum(Money $a, Money $b): Money {
                return $a + $b;
            }
            """);

        php.Should().Contain("$a->plus($b)");
        php.Should().NotContain("return $a;");
        php.Should().NotContain("$a + $b");
        php.Should().NotContain("function __add");
        php.Should().NotContain("class MoneyOps");
        php.Should().NotContain(@"\MoneyOps::__add");
    }

    [Fact]
    public void Emit_BraceExtensionOperator_EmitsBackerAndSplicesCallSite()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                public function plus(Money $other): Money {
                    return $this;
                }
            }
            extension MoneyOps extends Money {
                operator + (self $left, self $right): Money {
                    return $left->plus($right);
                }
            }
            function sum(Money $a, Money $b): Money {
                return $a + $b;
            }
            """);

        php.Should().Contain("$a->plus($b)");
        php.Should().NotContain("return $a;");
        php.Should().NotContain("$a + $b");
        php.Should().NotContain(@"\MoneyOps::__add($a, $b)");
        php.Should().Contain("class MoneyOps");
        php.Should().Contain("function __add");
    }

    [Fact]
    public void Emit_ExtensionOperator_DoesNotInlineOrdinaryMethodIgnoringOtherArg()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                public function plus(Money $other): Money {
                    return $other;
                }
            }
            extension MoneyOps extends Money {
                operator + (self $left, self $right): Money => $left->plus($right);
            }
            function sum(Money $a, Money $b): Money {
                return $a + $b;
            }
            """);

        php.Should().Contain("$a->plus($b)");
        php.Should().NotContain("return $b;");
        php.Should().NotContain("$a + $b");
        php.Should().NotContain("function __add");
    }

    [Fact]
    public void Emit_ClassOwnedShortOperator_StillEmitsPhpMethod()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
                operator +(self $left, int $right): self => $left;
            }
            function add(Money $a): Money {
                return $a + 1;
            }
            """);

        php.Should().Contain("class Money");
        php.Should().Contain("function __add");
        php.Should().Contain(@"\Money::__add($a, 1)");
        php.Should().NotContain("return $a + 1");
    }

    [Fact]
    public void Emit_FileLocalHide_BuiltinString_DoesNotSpliceHiddenToLower_StillSplicesLength()
    {
        // `string`'s declaring scope is global, so file-local `hide` must use the call-site
        // FileScope — not the receiver type's ContainingScope.
        var php = CompileAndEmit("""
            <?tyhp
            use extension \Tyhp\StringExtensions {
                StringExtensions::toLower hide;
            }
            function visibleLength(string $s): int {
                return $s->length();
            }
            """);

        php.Should().Contain(@"\mb_strlen($s)");
        php.Should().NotContain(@"\mb_strtolower");
        php.Should().NotContain("$s->length(");
    }

    [Fact]
    public void Emit_CallSiteSelectsBodylessOverload_SplicesRealImplementationBody()
    {
        // The call site's best-scoring overload (`1 $format`) has no body of its own — only
        // the catch-all implementation does. Splicing must still route to that real body
        // (bound with the call's own arguments), not skip inlining or use another stub.
        var php = CompileAndEmit("""
            <?tyhp
            extension WordOps extends string {
                function wordCount(0 $format = 0, ?string $characters = null): int;
                function wordCount(1 $format, ?string $characters = null): array<int, string>;
                function wordCount(2 $format, ?string $characters = null): array<int, string>;
                fn wordCount(int $format = 0, ?string $characters = null): array<int, string>|int => [$format];
            }

            use extension WordOps;

            function asList(string $s): array<int, string> {
                return $s->wordCount(1);
            }
            """);

        php.Should().Contain("return [1];");
        php.Should().NotContain("->wordCount(");
    }

    [Fact]
    public void Emit_FileLocalHide_UserClass_StillRewritesVisibleMember()
    {
        var php = CompileAndEmit("""
            <?tyhp
            class Money {
                public int $amount = 0;
            }
            extension MoneyFormatting extends Money {
                function format(): string {
                    return 'ok';
                }
                function extra(): string {
                    return 'hidden';
                }
            }
            use extension MoneyFormatting {
                MoneyFormatting::extra hide;
            }
            function show(Money $m): string {
                return $m->format();
            }
            function hidden(Money $m): string {
                return $m->extra();
            }
            """);

        php.Should().Contain("return 'ok';");
        php.Should().NotContain("$m->format(");
        php.Should().Contain("$m->extra(");
        php.Should().NotContain(@"\MoneyFormatting::extra(");
    }
}
