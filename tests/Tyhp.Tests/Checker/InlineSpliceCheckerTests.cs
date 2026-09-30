using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang;
using Tyhp.TyhpLang.Binder;
using Tyhp.TyhpLang.Checker;

namespace Tyhp.Tests.Checker;

[Trait("Category", "Checker")]
[Trait("Category", "Story20.6")]
public class InlineSpliceCheckerTests
{
    [Fact]
    public void WrittenParameterWithoutRef_Reports4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Mut extends int {
                function bump(int $n): int {
                    return ++$n;
                }
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void PreIncrementOfThis_Reports4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Mut extends int {
                function bump(): int {
                    return ++$this;
                }
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
    }

    [Fact]
    public void RefOnUnwrittenParameter_Reports4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Mut extends int {
                function unusedRef(int &$n): int {
                    return $this;
                }
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void KsortOfParameter_Reports4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Mut extends int {
                function sortKeys(array $a): bool {
                    return \ksort($a);
                }
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void WrittenRefParameter_No4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Mut extends int {
                function bump(int &$n): int {
                    return ++$n;
                }
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void WrittenRefThis_No4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Mut extends int {
                function bump(&$this): int {
                    return ++$this;
                }
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void ByRefThisPassedToSort_No4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Mut extends array {
                function sortInPlace(&$this): bool {
                    return \sort($this);
                }
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void ByRefParameterPassedToStaticMethod_No4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                public static function fill(string &$out): bool {
                    $out = "x";
                    return true;
                }
            }

            extension Mut extends string {
                function take(string &$out): bool {
                    return Box::fill($out);
                }
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void ByRefParameterPassedToGenericStaticMethod_No4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class JsonBox {
                public static function tryDecode<T>(string $json, T &$out): bool {
                    return false;
                }
            }

            extension Mut extends string {
                function jsonTryDecodeAs<T>(T &$out): bool {
                    return JsonBox::tryDecode<T>($this, $out);
                }
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void ByRefParameterPassedToNamespacedStaticMethod_No4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            namespace Tyhp;

            final class Json {
                public static function tryDecode<T extends struct>(string $json, T &$out, int $depth = 512, int $flags = 0): bool {
                    return false;
                }
            }

            extension StringExtensions extends string {
                function jsonTryDecodeAs<T extends struct>(T &$out, int $depth = 512, int $flags = 0): bool {
                    return Json::tryDecode<T>($this, $out, $depth, $flags);
                }
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void SortOfByValueThis_Reports4174()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Mut extends array {
                function sortInPlace(): bool {
                    return \sort($this);
                }
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerExtensionByRefReceiverRequired);
    }

    [Fact]
    public void TyhpdefArrayMutatingThis_No4174()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            extension MutArray extends array {
                fn sort(&$this, int $flags = 0): bool => \sort($this, $flags);
                fn push(&$this, mixed ...$values): int => \array_push($this, ...$values);
            }
            """,
            """
            <?tyhp
            use extension MutArray;
            function f(): void {
                array $a = [3, 1, 2];
                $a->sort();
                $a->push(1);
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void LiteralReceiverForByRefThis_Reports4180()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Mut extends array {
                function sortInPlace(&$this): bool {
                    return \sort($this);
                }
            }
            function arr(): array {
                return [1, 2];
            }
            function f(): void {
                arr()->sortInPlace();
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerNonReferenceableByRefArgument);
        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerInlineParameterMutation);
    }

    [Fact]
    public void MutualSpliceCycle_Reports4175()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Cycle extends int {
                function a(): int {
                    return $this->b();
                }
                function b(): int {
                    return $this->a();
                }
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerInlineCycle);
    }

    [Fact]
    public void InlineAttributeOnExtensionMember_Reports4176()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            class Box {
                public int $n;
                #[\Tyhp\Optimize\Inline]
                extension fn id(): int => $this->n;
            }
            """,
            """
            <?tyhp
            function f(Box $b): int {
                return $b->id();
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerInlineAttributeOnExtensionMember);
    }

    [Fact]
    public void InlineAttributeOnTyhpdefThinMember_Reports4176AtDeclarationWithoutCallSite()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            class Box {
                public int $n;
                #[\Tyhp\Optimize\Inline]
                extension fn id(): int => $this->n;
            }
            """,
            """
            <?tyhp
            function f(Box $b): Box {
                return $b;
            }
            """);

        var hits = diagnostics
            .Where(d => d.Code == MessageCode.CheckerInlineAttributeOnExtensionMember)
            .ToList();
        hits.Should().NotBeEmpty();
        hits.Should().Contain(d =>
            (d.FileName ?? "").EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InlineAttributeOnNonExtensionFunction_NoFalsePositive()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            #[\Tyhp\Optimize\Inline]
            function f(): int {
                return 1;
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerInlineAttributeOnExtensionMember);
    }

    [Fact]
    public void InlineAttributeOnNonExtensionMethod_NoFalsePositive()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                #[\Tyhp\Optimize\Inline]
                function id(): int {
                    return 1;
                }
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerInlineAttributeOnExtensionMember);
    }

    [Fact]
    public void ReservedInlineTempVariable_Reports4177()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): int {
                int $__tyhpInlineTemp1 = 0;
                return $__tyhpInlineTemp1;
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerReservedInlineTempPrefix);
        GeneratedNames.StartsWithInlineTempPrefix("__tyhpInlineTemp1").Should().BeTrue();
    }

    [Fact]
    public void PublicExtensionReadingPrivateProperty_Reports4179()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            class Box {
                private int $secret = 1;
            }
            extension Leak extends Box {
                function leak(): int {
                    return $this->secret;
                }
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerInlineInaccessibleMember);
    }

    [Fact]
    public void LiteralPassedToByRef_Reports4180()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function f(): bool {
                return \ksort(1);
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerNonReferenceableByRefArgument);
    }

    [Fact]
    public void ByValueHookedPropertyPassedToByRef_Reports4180()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            class Holder {
                public array $hooked { get; }
                public array $refItems { &get; }
            }
            """,
            """
            <?tyhp
            function f(Holder $h): void {
                \ksort($h->hooked);
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerNonReferenceableByRefArgument);
    }

    [Fact]
    public void ByRefGetHookPassedToByRef_No4180()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            class Holder {
                public array $hooked { get; }
                public array $refItems { &get; }
            }
            """,
            """
            <?tyhp
            function f(Holder $h): void {
                \ksort($h->refItems);
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerNonReferenceableByRefArgument);
    }

    [Fact]
    public void ErasedThinMappingUnusedArg_Reports4181()
    {
        var diagnostics = CompileAndCheckWithTyhpdef(
            """
            <?tyhpdef
            class Box {
                public int $n;
                extension fn ignore(int $extra): int => $this->n;
            }
            """,
            """
            <?tyhp
            function tick(): int {
                return 1;
            }
            function f(Box $b): int {
                return $b->ignore(tick());
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerErasedMemberUnsafeSplice);
    }

    [Fact]
    public void PhpBackerUnusedArg_DoesNotReport4181()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Keep extends int {
                function ignore(int $n): int {
                    return $this;
                }
            }
            function tick(): int {
                return 1;
            }
            function f(int $x): int {
                return $x->ignore(tick());
            }
            """);

        diagnostics.Should().NotContain(d => d.Code == MessageCode.CheckerErasedMemberUnsafeSplice);
    }

    [Fact]
    public void ErasedShortExtensionUnusedArg_Reports4181()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Keep extends int {
                fn ignore(int $n): int => $this;
            }
            function tick(): int {
                return 1;
            }
            function f(int $x): int {
                return $x->ignore(tick());
            }
            """);

        diagnostics.Should().Contain(d => d.Code == MessageCode.CheckerErasedMemberUnsafeSplice);
    }

    [Fact]
    public void GenericTyhpdefExtensionMethods_DoNotReportTKeyTValueNotFound()
    {
        var diagnostics = CompileAndCheckWithTyhpdef("""
            <?tyhpdef
            namespace Test;
            extension Arr<TKey, TValue> extends array<TKey, TValue> {
                fn keys(): array<int, TKey>
                    => \array_keys($this);
                fn find(callable(TValue, TKey): bool $callback): ?TValue
                    => null;
            }
            global use extension \Test\Arr;
            """, """
            <?tyhp
            function demo(array $a): int {
                return \count($a->keys());
            }
            """);

        diagnostics.Should().NotContain(
            d => d.Code == MessageCode.BinderSymbolNotFound && NamesTKeyOrTValue(d),
            DescribeTKeyTValue(diagnostics));
    }

    [Fact]
    public void ParsedTyhpdefGenericExtension_DoNotReportTKeyTValueNotFound()
    {
        var diagnostics = CompileAndCheckNamed("arr.tyhpdef", """
            <?tyhpdef
            namespace Test;
            extension Arr<TKey, TValue> extends array<TKey, TValue> {
                fn keys(): array<int, TKey>
                    => \array_keys($this);
            }
            """);

        diagnostics.Should().NotContain(
            d => d.Code == MessageCode.BinderSymbolNotFound && NamesTKeyOrTValue(d),
            DescribeTKeyTValue(diagnostics));
    }

    [Fact]
    public void GenericClassBodyInlineExtension_DoNotReportTKeyTValueNotFound()
    {
        var diagnostics = CompileAndCheckWithTyhpdef("""
            <?tyhpdef
            namespace Test;
            class Box {
                extension fn keys<TKey, TValue>(): array<int, TKey> => \array_keys($this);
            }
            """, """
            <?tyhp
            function demo(\Test\Box $box): int {
                return \count($box->keys());
            }
            """);

        diagnostics.Should().NotContain(
            d => d.Code == MessageCode.BinderSymbolNotFound && NamesTKeyOrTValue(d),
            DescribeTKeyTValue(diagnostics));
    }

    [Fact]
    public void GenericTyhpExtensionMethods_DoNotReportTKeyTValueNotFound()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            extension Arr<TKey, TValue> extends array<TKey, TValue> {
                fn keys(): array<int, TKey>
                    => \array_keys($this);
            }
            function demo(array $a): int {
                return \count($a->keys());
            }
            """);

        diagnostics.Should().NotContain(
            d => d.Code == MessageCode.BinderSymbolNotFound && NamesTKeyOrTValue(d),
            DescribeTKeyTValue(diagnostics));
    }

    [Fact]
    public void ArrayTyhpdefGenericExtensionMethods_DoNotReportTKeyTValueNotFound()
    {
        var diagnostics = CompileAndCheck("""
            <?tyhp
            function demo(array $a): int {
                return $a->count();
            }
            """);

        diagnostics.Should().NotContain(
            d => d.Code == MessageCode.BinderSymbolNotFound
                && IsArrayTyhpdef(d)
                && NamesTKeyOrTValue(d),
            DescribeTKeyTValue(diagnostics));
    }

    private static bool NamesTKeyOrTValue(IDiagnostic diagnostic)
    {
        var message = diagnostic.Message ?? "";
        return message.Contains("'TKey'", StringComparison.Ordinal)
            || message.Contains("'TValue'", StringComparison.Ordinal);
    }

    private static bool IsArrayTyhpdef(IDiagnostic diagnostic)
    {
        var file = (diagnostic.FileName ?? "").Replace('\\', '/');
        return file.EndsWith("/array.tyhpdef", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith("array.tyhpdef", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeTKeyTValue(IEnumerable<IDiagnostic> diagnostics) =>
        string.Join("; ", diagnostics
            .Where(d => d.Code == MessageCode.BinderSymbolNotFound && NamesTKeyOrTValue(d))
            .Select(d => $"{d.FileName}:{d.Line}: {d.Message}"));

    private static IReadOnlyList<IDiagnostic> CompileAndCheck(string content)
        => CompileAndCheckNamed(Guid.NewGuid().ToString("N") + ".tyhp", content);

    private static IReadOnlyList<IDiagnostic> CompileAndCheckNamed(string fileName, string content)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, fileName);
        File.WriteAllText(filePath, content);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(tempDir, phpVersion: "8.4", skipChecking: true);
            var result = compilationService.ParseFiles([filePath], options);
            result.GlobalScope.Should().NotBeNull();
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics.ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static IReadOnlyList<IDiagnostic> CompileAndCheckWithTyhpdef(string tyhpdef, string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "hooks.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + ".tyhp");
        File.WriteAllText(tyhpdefPath, tyhpdef);
        File.WriteAllText(tyhpPath, tyhp);

        try
        {
            using var compilationService = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(
                tempDir,
                phpVersion: "8.4",
                skipChecking: true,
                tyhpdefIncludePaths: [tyhpdefPath]);
            var result = compilationService.ParseFiles([tyhpPath], options);
            result.GlobalScope.Should().NotBeNull();
            var symbolTree = new SymbolTree(result.GlobalScope!);
            var checker = new TyhpChecker(result.Diagnostics, symbolTree, result.GlobalScope!);
            checker.Check(result.ParsedFiles!);
            return result.Diagnostics.ToList();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}

