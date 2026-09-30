using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Binder;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class TyhpdefOverlayStampRewriterTests
{
    [Fact]
    public void Apply_SemicolonFormNamespace_QualifiesSiblingDeclarationsWithNamespace()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-semicolon-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                namespace Foo\Bar;

                function baz(): void;
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.SymbolKey(@"Foo\Bar\baz", TyhpdefOverlayStamp.KindFunction)] = "function baz(): void;",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(1);
            var text = File.ReadAllText(path);
            text.Should().Contain("// @overlay-against: function baz(): void;");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_BlockFormNamespace_QualifiesMemberDeclarationsWithNamespace()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-block-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                namespace Foo\Bar {
                    partial class Widget {
                        public function log(mixed $level): void;
                    }
                }
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.MemberKey(@"\Foo\Bar\Widget", "log")] = "public function log(mixed $level): void;",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(1);
            var text = File.ReadAllText(path);
            text.Should().Contain("// @overlay-against: public function log(mixed $level): void;");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_ExternDeclaration_DoesNotWriteStamp()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-extern-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                extern class \ExternNs\Peer;
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.SymbolKey(@"ExternNs\Peer", TyhpdefOverlayStamp.KindType)] = "class Peer",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(0);
            File.ReadAllText(path).Should().NotContain("@overlay-against:");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_FunctionAlias_LooksUpLayer1ByPhpOriginalName()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-alias-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                function call_user_func as call_user_func_unsafe(callable $callback, mixed ...$args): mixed;
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.SymbolKey("call_user_func", TyhpdefOverlayStamp.KindFunction)] =
                    "function call_user_func(callable $callback, mixed $args): mixed",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(1);
            var text = File.ReadAllText(path);
            text.Should().Contain("// @overlay-against: function call_user_func(callable $callback, mixed $args): mixed");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_PartialFunction_WritesNameOnlyStampWhenLayer1HasKey()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-partial-fn-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                #[\Tyhp\Optimize\Pure]
                partial function array_map;
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.SymbolKey("array_map", TyhpdefOverlayStamp.KindFunction)] =
                    "function array_map(callable $callback, array $array): array",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(1);
            var text = File.ReadAllText(path);
            text.Should().Contain("// @overlay-against: function array_map");
            text.Should().NotContain("function array_map(callable");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_PartialFunction_SkipsWhenLayer1HasNoKey()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-partial-fn-missing-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                partial function missing_fn;
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.SymbolKey("array_map", TyhpdefOverlayStamp.KindFunction)] =
                    "function array_map(callable $callback, array $array): array",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(0);
            File.ReadAllText(path).Should().NotContain("@overlay-against");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_PartialFunctionMethod_WritesNameOnlyStamp()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-partial-method-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                partial class DomainException {
                    #[\Tyhp\Optimize\Pure]
                    partial function getMessage;
                }
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.MemberKey(@"\DomainException", "getMessage")] =
                    "function getMessage(): string",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(1);
            var text = File.ReadAllText(path);
            text.Should().Contain("// @overlay-against: function getMessage");
            text.Should().NotContain("function getMessage(): string");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_AttributedMember_ReplacesStampBetweenAttributeAndKeyword()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-attr-after-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                partial class Widget {
                    /**
                     * Logs a message.
                     */
                    #[\Tyhp\Php("<8.5")]
                    // @overlay-against: function log(mixed $level): void
                    public function log(mixed $level): void;
                }
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.MemberKey(@"\Widget", "log")] = "function log(string $level): void",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(1);
            var text = File.ReadAllText(path);
            var doc = text.IndexOf("Logs a message.", StringComparison.Ordinal);
            var attr = text.IndexOf("#[\\Tyhp\\Php(\"<8.5\")]", StringComparison.Ordinal);
            var stamp = text.IndexOf("// @overlay-against: function log(string $level): void", StringComparison.Ordinal);
            var decl = text.IndexOf("public function log", StringComparison.Ordinal);
            doc.Should().BeGreaterThanOrEqualTo(0);
            attr.Should().BeGreaterThan(doc);
            stamp.Should().BeGreaterThan(attr);
            decl.Should().BeGreaterThan(stamp);
            CountOccurrences(text, "@overlay-against:").Should().Be(1);
            text.Should().NotContain("// @overlay-against: function log(mixed $level): void");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_AttributedMember_ReplacesStampImmediatelyBeforeAttribute()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-attr-before-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                partial class Widget {
                    // @overlay-against: function log(mixed $level): void
                    #[\Deprecated]
                    public function log(mixed $level): void;
                }
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.MemberKey(@"\Widget", "log")] = "function log(string $level): void",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(1);
            var text = File.ReadAllText(path);
            var stamp = text.IndexOf("// @overlay-against: function log(string $level): void", StringComparison.Ordinal);
            var attr = text.IndexOf("#[\\Deprecated]", StringComparison.Ordinal);
            var decl = text.IndexOf("public function log", StringComparison.Ordinal);
            stamp.Should().BeGreaterThanOrEqualTo(0);
            attr.Should().BeGreaterThan(stamp);
            decl.Should().BeGreaterThan(attr);
            CountOccurrences(text, "@overlay-against:").Should().Be(1);
            text.Should().NotContain("// @overlay-against: function log(mixed $level): void");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_InsertsStampAfterDocblockAndAttributes()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-decorations-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                /**
                 * A widget.
                 */
                #[\Tyhp\Php(">=8.4")]
                #[\Deprecated]
                class Widget {
                }
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.SymbolKey("Widget", TyhpdefOverlayStamp.KindType)] = "class Widget",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(1);
            var text = File.ReadAllText(path);
            var doc = text.IndexOf("A widget.", StringComparison.Ordinal);
            var gate = text.IndexOf("#[\\Tyhp\\Php(\">=8.4\")]", StringComparison.Ordinal);
            var attr = text.IndexOf("#[\\Deprecated]", StringComparison.Ordinal);
            var stamp = text.IndexOf("// @overlay-against: class Widget", StringComparison.Ordinal);
            var decl = text.IndexOf("class Widget {", StringComparison.Ordinal);
            doc.Should().BeGreaterThanOrEqualTo(0);
            gate.Should().BeGreaterThan(doc);
            attr.Should().BeGreaterThan(gate);
            stamp.Should().BeGreaterThan(attr);
            decl.Should().BeGreaterThan(stamp);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_MultiOverloadMember_DoesNotClobberCorrectlyStampedOverloadsWithPrimary()
    {
        // Layer 1 recording captures every same-name overload under one key (DatePeriod-style
        // `__construct`). Re-running `tyhp overlay stamp` must not flatten each already-correct
        // per-overload `@overlay-against:` comment down to the primary (first) overload's text.
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-multi-overload-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                partial class Period {
                    // @overlay-against: function __construct(int $start, int $interval, string $end, int $options): void
                    public function __construct(mixed $start, int $interval, mixed $end, int $options = 0): void;
                    // @overlay-against: function __construct(int $start, int $interval, int $recurrences, int $options): void
                    public function __construct(mixed $start, int $interval, int $recurrences, int $options = 0): void;
                    // @overlay-against: function __construct(string $isostr, int $options): void
                    public function __construct(string $isostr, int $options = 0): void;
                }
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.MemberKey(@"\Period", "__construct")] =
                    "function __construct(int $start, int $interval, string $end, int $options): void"
                    + TyhpdefOverlayStamp.StampListSeparator
                    + "function __construct(int $start, int $interval, int $recurrences, int $options): void"
                    + TyhpdefOverlayStamp.StampListSeparator
                    + "function __construct(string $isostr, int $options): void",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(0);
            var text = File.ReadAllText(path);
            text.Should().Contain("// @overlay-against: function __construct(int $start, int $interval, string $end, int $options): void");
            text.Should().Contain("// @overlay-against: function __construct(int $start, int $interval, int $recurrences, int $options): void");
            text.Should().Contain("// @overlay-against: function __construct(string $isostr, int $options): void");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_MultiOverloadMember_UnstampedOverload_WritesPrimaryOverload()
    {
        // No existing stamp to preserve — falling back to the primary (first) overload is the
        // best available default; a human still needs to disambiguate further overloads by hand.
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-multi-overload-unstamped-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                partial class Period {
                    public function __construct(string $isostr, int $options = 0): void;
                }
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.MemberKey(@"\Period", "__construct")] =
                    "function __construct(int $start, int $interval, string $end, int $options): void"
                    + TyhpdefOverlayStamp.StampListSeparator
                    + "function __construct(string $isostr, int $options): void",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(1);
            var text = File.ReadAllText(path);
            text.Should().Contain("// @overlay-against: function __construct(int $start, int $interval, string $end, int $options): void");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_AttributedMember_RemovesStrayDuplicateStampLeftBeforeAttribute()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-dupe-cleanup-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            // Simulates the exact shape of the pre-fix duplication bug: a stray stamp sitting
            // before the attribute (written by a buggy `overlay stamp` run) alongside the
            // correctly-positioned stamp between the attribute and the keyword. Re-stamping
            // must collapse both back down to a single stamp, not just update the last one.
            File.WriteAllText(path, """
                <?tyhpdef
                partial class Widget {
                    // @overlay-against: function log(mixed $level): void
                    #[\Tyhp\Php("<8.5")]
                    // @overlay-against: function log(mixed $level): void
                    public function log(mixed $level): void;
                }
                """);

            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.MemberKey(@"\Widget", "log")] = "function log(string $level): void",
            };

            var updatedCount = TyhpdefOverlayStampRewriter.Apply(path, stamps, null);

            updatedCount.Should().Be(1);
            var text = File.ReadAllText(path);
            CountOccurrences(text, "@overlay-against:").Should().Be(1);
            text.Should().Contain("// @overlay-against: function log(string $level): void");
            text.Should().NotContain("// @overlay-against: function log(mixed $level): void");

            var attr = text.IndexOf("#[\\Tyhp\\Php(\"<8.5\")]", StringComparison.Ordinal);
            var stamp = text.IndexOf("// @overlay-against: function log(string $level): void", StringComparison.Ordinal);
            var decl = text.IndexOf("public function log", StringComparison.Ordinal);
            attr.Should().BeGreaterThanOrEqualTo(0);
            stamp.Should().BeGreaterThan(attr);
            decl.Should().BeGreaterThan(stamp);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_TargetVersion_StampsUnguardedEveryPass_AndLeavesOtherGates()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-gates-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                function stampfix_always(): void;

                declare(php="<8.4") {
                    // @overlay-against: function stampfix_gated(): KEEP_LOW
                    function stampfix_gated(): int;
                }
                declare(php=">=8.4") {
                    // @overlay-against: function stampfix_gated(): KEEP_HIGH
                    function stampfix_gated(): string;
                }

                declare(php="8.3") {
                    // @overlay-against: function stampfix_exact(): string
                    function stampfix_exact(): bool;
                }

                #[\Tyhp\Php("8.3")]
                // @overlay-against: function stampfix_attr_exact(): string
                function stampfix_attr_exact(): bool;

                #[\Tyhp\Php(">=8.4")]
                // @overlay-against: function stampfix_attr_min(): bool
                function stampfix_attr_min(): string;
                """);

            foreach (var minor in PhpRuntimeVersion.SupportedMinors)
            {
                var fresh = Path.Combine(dir, "always-" + minor + ".tyhpdef");
                File.WriteAllText(fresh, """
                    <?tyhpdef
                    function stampfix_always(): void;
                    declare(php=">=8.4") {
                        // @overlay-against: function stampfix_gated(): KEEP
                        function stampfix_gated(): string;
                    }
                    """);
                var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [TyhpdefOverlayStamp.SymbolKey("stampfix_always", TyhpdefOverlayStamp.KindFunction)] =
                        "function stampfix_always(): void",
                    [TyhpdefOverlayStamp.SymbolKey("stampfix_gated", TyhpdefOverlayStamp.KindFunction)] =
                        "function stampfix_gated(): string",
                };
                var updated = TyhpdefOverlayStampRewriter.Apply(fresh, stamps, null, minor + ".1");
                var written = File.ReadAllText(fresh);
                written.Should().Contain("// @overlay-against: function stampfix_always(): void", minor);
                updated.Should().BeGreaterThan(0, minor);
                if (minor is "8.2" or "8.3")
                {
                    written.Should().Contain("// @overlay-against: function stampfix_gated(): KEEP", minor);
                }
                else
                {
                    written.Should().Contain("// @overlay-against: function stampfix_gated(): string", minor);
                    written.Should().NotContain("KEEP", minor);
                }
            }

            var pass83 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.SymbolKey("stampfix_gated", TyhpdefOverlayStamp.KindFunction)] =
                    "function stampfix_gated(): int",
                [TyhpdefOverlayStamp.SymbolKey("stampfix_exact", TyhpdefOverlayStamp.KindFunction)] =
                    "function stampfix_exact(): bool",
                [TyhpdefOverlayStamp.SymbolKey("stampfix_attr_exact", TyhpdefOverlayStamp.KindFunction)] =
                    "function stampfix_attr_exact(): bool",
                [TyhpdefOverlayStamp.SymbolKey("stampfix_attr_min", TyhpdefOverlayStamp.KindFunction)] =
                    "function stampfix_attr_min(): float",
            };
            TyhpdefOverlayStampRewriter.Apply(path, pass83, null, "8.3.20");

            var after83 = File.ReadAllText(path);
            after83.Should().Contain("// @overlay-against: function stampfix_gated(): int");
            after83.Should().Contain("// @overlay-against: function stampfix_gated(): KEEP_HIGH");
            after83.Should().NotContain("KEEP_LOW");
            after83.Should().Contain("// @overlay-against: function stampfix_exact(): bool");
            after83.Should().Contain("// @overlay-against: function stampfix_attr_exact(): bool");
            after83.Should().Contain("// @overlay-against: function stampfix_attr_min(): bool");
            after83.Should().NotContain("stampfix_attr_min(): float");

            var pass84 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.SymbolKey("stampfix_gated", TyhpdefOverlayStamp.KindFunction)] =
                    "function stampfix_gated(): string",
                [TyhpdefOverlayStamp.SymbolKey("stampfix_exact", TyhpdefOverlayStamp.KindFunction)] =
                    "function stampfix_exact(): float",
                [TyhpdefOverlayStamp.SymbolKey("stampfix_attr_min", TyhpdefOverlayStamp.KindFunction)] =
                    "function stampfix_attr_min(): string",
            };
            TyhpdefOverlayStampRewriter.Apply(path, pass84, null, "8.4.99");

            var after84 = File.ReadAllText(path);
            after84.Should().Contain("// @overlay-against: function stampfix_gated(): int");
            after84.Should().Contain("// @overlay-against: function stampfix_gated(): string");
            after84.Should().Contain("// @overlay-against: function stampfix_exact(): bool");
            after84.Should().NotContain("stampfix_exact(): float");
            after84.Should().Contain("// @overlay-against: function stampfix_attr_min(): string");
            after84.Should().Contain("// @overlay-against: function stampfix_attr_exact(): bool");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Apply_FileLevelDeclare_GatesEveryDeclarationInTheFile()
    {
        var dir = Directory.CreateTempSubdirectory("overlay-stamp-file-declare-").FullName;
        try
        {
            var path = Path.Combine(dir, "overlay.tyhpdef");
            File.WriteAllText(path, """
                <?tyhpdef
                declare(php=">=8.4");
                function stampfix_file(): void;
                """);
            var stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [TyhpdefOverlayStamp.SymbolKey("stampfix_file", TyhpdefOverlayStamp.KindFunction)] =
                    "function stampfix_file(): void",
            };

            TyhpdefOverlayStampRewriter.Apply(path, stamps, null, "8.3.1").Should().Be(0);
            File.ReadAllText(path).Should().NotContain("@overlay-against:");

            TyhpdefOverlayStampRewriter.Apply(path, stamps, null, "8.4.2").Should().Be(1);
            File.ReadAllText(path).Should().Contain("// @overlay-against: function stampfix_file(): void");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static int CountOccurrences(string text, string marker)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
