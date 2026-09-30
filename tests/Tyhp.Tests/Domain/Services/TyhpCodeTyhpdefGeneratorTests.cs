using Microsoft.Extensions.Configuration;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang;
using Tyhp.TyhpLang.Emitter;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story20.6")]
public class TyhpCodeTyhpdefGeneratorTests
{
    [Fact]
    public void Library_ExtensionMembers_MapByBodyForm()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("helpers.tyhp", """
                <?tyhp
                namespace Lib;
                extension StringHelpers extends string {
                    function complexStringProcess(): string {
                        string $finalString = $this;
                        return $finalString;
                    }

                    fn shortProcess(): string => ' ' . $this;

                    function simpleProcess(): string {
                        return $this . ' ';
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("class StringHelpers as StringHelpers__tyhpExtensionBacker");
        def.Should().Contain("public static function complexStringProcess(string $this_): string;");
        def.Should().Contain("public static function simpleProcess(string $this_): string;");
        def.Should().NotContain("function shortProcess(");
        def.Should().Contain("extension StringHelpers extends string {");
        def.Should().Contain(
            "fn complexStringProcess(): string => StringHelpers__tyhpExtensionBacker::complexStringProcess($this);");
        def.Should().Contain("fn shortProcess(): string => ' ' . $this;");
        def.Should().Contain("fn simpleProcess(): string => $this . ' ';");
        def.Should().NotContain(GeneratedNames.ExtensionReceiverThisAlias + " .");
        def.Should().NotContain(".' " + GeneratedNames.ExtensionReceiverThisAlias);
        def.Should().NotContain("use extension");
        def.Should().NotContain("global use extension");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Library_ExtensionMapsHelperMethodSharingPhpBuiltinName_KeepsClassMember()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "tyhpdefInclude": ["stubs.tyhpdef"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("stubs.tyhpdef", """
                <?tyhpdef
                function strlen(string $string): int;
                """)
            .WithTyhpFile("helpers.tyhp", """
                <?tyhp
                namespace Lib;
                final class StringHelper {
                    public static function strlen(string $string): int {
                        return \strlen($string);
                    }
                }
                extension StringHelpers extends string {
                    fn length(): int => \Lib\StringHelper::strlen($this);
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("extension StringHelpers extends string {");
        def.Should().Contain("fn length(): int => \\Lib\\StringHelper::strlen($this);");
        def.Should().NotContain("::\\strlen(");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Library_OwnTypesOnly_DoesNotEmitExtern()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("box.tyhp", """
                <?tyhp
                namespace Lib;
                class Box {
                    public function volume(): int {
                        return 1;
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("class Box");
        def.Should().NotContain("extern class");
        def.Should().NotContain("extern interface");
        def.Should().NotContain("@provided-by:");
        File.Exists(Path.Combine(project.ProjectDirectory, "externs.tyhpdef")).Should().BeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "_tyhpdef", TyhpdefOutputLayout.ExternsFileName))
            .Should().BeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "build", TyhpdefOutputLayout.ExternsFileName))
            .Should().BeFalse();
    }

    [Fact]
    public void Library_AllShortArrowExtension_OmitsBackerStubClass()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("pad.tyhp", """
                <?tyhp
                namespace Lib;
                extension StringPad extends string {
                    fn padLeft(): string => ' ' . $this;
                    fn padRight(): string => $this . ' ';
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("extension StringPad");
        def.Should().Contain("extension StringPad extends string {");
        def.Should().Contain("fn padLeft(): string => ' ' . $this;");
        def.Should().Contain("fn padRight(): string => $this . ' ';");
        def.Should().NotContain("StringPad__tyhpExtensionBacker");
        def.Should().NotContain("class StringPad");
        def.Should().NotContain(GeneratedNames.ExtensionReceiverThisAlias);
        def.Should().NotContain("use extension");
        def.Should().NotContain("global use extension");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Library_CopiedExpression_NeverContainsThisAlias()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("upper.tyhp", """
                <?tyhp
                namespace Lib;
                function echoString(string $s): string {
                    return $s;
                }
                extension StringUpper extends string {
                    fn toUpper(): string => echoString($this);
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("=> \\Lib\\echoString($this)");
        def.Should().NotContain(GeneratedNames.ExtensionReceiverThisAlias);
        def.Should().NotContain("StringUpper__tyhpExtensionBacker");
    }

    [Fact]
    public void Library_ShortArrowExtensionMember_UnspellableExpression_ReportsGenerationError()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("shout.tyhp", """
                <?tyhp
                namespace Lib;
                extension StringShout extends string {
                    fn shout(): mixed => (function(string $s): string {
                        return $s;
                    })($this);
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefGenerationError);

        // A short `=>` member never gets a PHP backer (Phase 5), so library tyhpdef generation must never fall
        // back to a `Backer::shout(...)` mapping here — that method was never emitted, and
        // calling it would be a runtime fatal for any consumer. Generation must fail instead
        // of writing a tyhpdef that describes a nonexistent PHP method.
        File.Exists(Path.Combine(project.ProjectDirectory, "package.tyhpdef")).Should().BeFalse();
    }

    [Fact]
    public void Library_OwnedClassOperator_MapsToSelfAddWithoutInlineAttribute()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("money.tyhp", """
                <?tyhp
                namespace Lib;
                class Money {
                    public int $cents = 0;
                    public function __construct(int $cents) {
                        $this->cents = $cents;
                    }
                    operator +(self $left, int $right): self {
                        return new Money($left->cents + $right);
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("public static function __add(");
        def.Should().Contain("extension operator +(self $left, int $right): self => self::__add($left, $right);");
        def.Should().NotContain("#[\\Tyhp\\Optimize\\Inline]");

        var php = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "build"), "*.php", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Aggregate("", (acc, next) => acc + next);
        php.Should().Contain("function __add(");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Library_OwnedClassOperatorOverloads_EmitOneCompiledBacker()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("money.tyhp", """
                <?tyhp
                namespace Lib;
                class Money {
                    public int $cents = 0;
                    operator +(self $left, self $right): self {
                        return $left;
                    }
                    operator +(self $left, int $right): self {
                        return $left;
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        System.Text.RegularExpressions.Regex.Matches(def, @"function __add\(").Count.Should().Be(1);
        def.Should().Contain("extension operator +(self $left, self $right)");
        def.Should().Contain("extension operator +(self $left, int $right)");

        using var consumer = new TestProjectBuilder();
        consumer
            .WithTyhpJson("""
                {
                    "type": "application",
                    "include": ["**/*.tyhp"],
                    "tyhpdefInclude": ["tyhpdef/**/*.tyhpdef"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("tyhpdef/package.tyhpdef", def)
            .WithTyhpFile("Use.tyhp", """
                <?tyhp
                function take(\Lib\Money $m): \Lib\Money {
                    return $m;
                }
                """);

        var consumed = consumer.RunBuild();
        consumed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", consumed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
    }

    [Fact]
    public void Library_OwnedClassConvertOperators_EmitFromAndToBackers()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("money.tyhp", """
                <?tyhp
                namespace Lib;
                class Money {
                    public int $amount = 0;
                    public function __construct(int $amount = 0) {
                        $this->amount = $amount;
                    }
                    operator convert(int $value) {
                        return new self($value);
                    }
                    operator convert(float $value) {
                        return new self((int)$value);
                    }
                    operator convert(string $value) {
                        return new self((int)$value);
                    }
                    operator convert(self $value): int {
                        return $value->amount;
                    }
                    operator convert(self $value): float {
                        return (float)$value->amount;
                    }
                    operator convert(self $value): string {
                        return (string)$value->amount;
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("public static function __from(int|float|string $from): self;");
        def.Should().Contain("public function __toInt(): int;");
        def.Should().Contain("public function __toFloat(): float;");
        def.Should().Contain("public function __toString(): string;");
        def.Should().Contain("extension operator convert(int $value): self => self::__from($value);");
        def.Should().Contain("extension operator convert(float $value): self => self::__from($value);");
        def.Should().Contain("extension operator convert(string $value): self => self::__from($value);");
        def.Should().Contain("extension operator convert(self $value): int => $value->__toInt();");
        def.Should().Contain("extension operator convert(self $value): float => $value->__toFloat();");
        def.Should().Contain("extension operator convert(self $value): string => $value->__toString();");
        def.Should().NotContain("function convert(");
        def.Should().NotContain("self::convert(");
        System.Text.RegularExpressions.Regex.Matches(def, @"function __from\(").Count.Should().Be(1);
        System.Text.RegularExpressions.Regex.Matches(def, @"function __toInt\(").Count.Should().Be(1);

        var php = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "build"), "*.php", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Aggregate("", (acc, next) => acc + next);
        php.Should().Contain("function __from(");
        php.Should().Contain("function __toInt(");
        php.Should().Contain("function __toFloat(");
        php.Should().Contain("function __toString(");
        php.Should().NotContain("function convert(");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);

        using var consumer = new TestProjectBuilder();
        consumer
            .WithTyhpJson("""
                {
                    "type": "application",
                    "include": ["**/*.tyhp"],
                    "tyhpdefInclude": ["tyhpdef/**/*.tyhpdef"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("tyhpdef/package.tyhpdef", def)
            .WithTyhpFile("Use.tyhp", """
                <?tyhp
                function asInt(\Lib\Money $m): int {
                    return (int)$m;
                }
                function asFloat(\Lib\Money $m): float {
                    return (float)$m;
                }
                function asString(\Lib\Money $m): string {
                    return (string)$m;
                }
                function takeMoney(\Lib\Money $m): \Lib\Money {
                    return $m;
                }
                function fromInt(int $n): void {
                    takeMoney($n);
                }
                function fromFloat(float $n): void {
                    takeMoney($n);
                }
                function fromString(string $n): void {
                    takeMoney($n);
                }
                """);

        var consumed = consumer.RunBuild();
        consumed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", consumed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var consumerPhp = Directory.EnumerateFiles(Path.Combine(consumer.ProjectDirectory, "build"), "*.php", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Aggregate("", (acc, next) => acc + next);
        consumerPhp.Should().Contain("__toInt(");
        consumerPhp.Should().Contain("__toFloat(");
        consumerPhp.Should().Contain("__toString(");
        consumerPhp.Should().Contain("__from(");
        consumerPhp.Should().NotContain("::convert(");
    }

    [Fact]
    public void Library_StandaloneExtensionConvertOperators_EmitFromAndToBackersOnExtension()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("money.tyhp", """
                <?tyhp
                namespace Lib;
                class Money {
                    public int $amount = 0;
                    public function __construct(int $amount = 0) {
                        $this->amount = $amount;
                    }
                }
                extension MoneyOperators extends \Lib\Money {
                    operator convert (int $value) {
                        Money $m = new Money();
                        $m->amount = $value;
                        return $m;
                    }
                    operator convert (float $value) {
                        Money $m = new Money();
                        $m->amount = (int)$value;
                        return $m;
                    }
                    operator convert (self $value): int {
                        int $n = $value->amount;
                        return $n;
                    }
                    operator convert (self $value): string {
                        string $s = (string)$value->amount;
                        return $s;
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("class MoneyOperators as MoneyOperators__tyhpExtensionBacker");
        def.Should().Contain("public static function __from(");
        def.Should().Contain("public static function __toInt(");
        def.Should().Contain("public static function __toString(");
        def.Should().Contain("MoneyOperators__tyhpExtensionBacker::__from($value)");
        def.Should().Contain("MoneyOperators__tyhpExtensionBacker::__toInt($value)");
        def.Should().Contain("MoneyOperators__tyhpExtensionBacker::__toString($value)");
        def.Should().Contain("operator convert");
        def.Should().NotContain("function convert(");
        def.Should().NotContain("::convert(");
        def.Should().NotContain("$value->__toInt()");
        def.Should().NotContain("self::__from(");
        System.Text.RegularExpressions.Regex.Matches(def, @"function __from\(").Count.Should().Be(1);
        System.Text.RegularExpressions.Regex.Matches(def, @"function __toInt\(").Count.Should().Be(1);

        var php = Directory.EnumerateFiles(Path.Combine(project.ProjectDirectory, "build"), "*.php", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Aggregate("", (acc, next) => acc + next);
        php.Should().Contain("class MoneyOperators");
        php.Should().Contain("public static function __from(");
        php.Should().Contain("public static function __toInt(");
        php.Should().Contain("public static function __toString(");
        php.Should().NotContain("function convert(");
        php.Should().NotContain("function __toInt():");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);

        using var consumer = new TestProjectBuilder();
        consumer
            .WithTyhpJson("""
                {
                    "type": "application",
                    "include": ["**/*.tyhp"],
                    "tyhpdefInclude": ["tyhpdef/**/*.tyhpdef"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("tyhpdef/package.tyhpdef", def)
            .WithTyhpFile("Use.tyhp", """
                <?tyhp
                use extension \Lib\MoneyOperators;
                function asInt(\Lib\Money $m): int {
                    return (int)$m;
                }
                function asString(\Lib\Money $m): string {
                    return (string)$m;
                }
                function takeMoney(\Lib\Money $m): \Lib\Money {
                    return $m;
                }
                function fromInt(int $n): void {
                    takeMoney($n);
                }
                function fromFloat(float $n): void {
                    takeMoney($n);
                }
                """);

        var consumed = consumer.RunBuild();
        consumed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", consumed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var consumerPhp = Directory.EnumerateFiles(Path.Combine(consumer.ProjectDirectory, "build"), "*.php", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Aggregate("", (acc, next) => acc + next);
        consumerPhp.Should().Contain("::__toInt(");
        consumerPhp.Should().Contain("::__toString(");
        consumerPhp.Should().Contain("::__from(");
        consumerPhp.Should().NotContain("$m->__toInt()");
        consumerPhp.Should().NotContain("::convert(");
    }

    /// <summary>
    /// Two different nested <c>extends&lt;T&gt;</c> targets can compile a convert operator to
    /// the *same* backer method name (<c>__toInt</c> keys off the return type; <c>__from</c> is
    /// always shared). The declared backer signature must union every colliding target's operand
    /// type — not silently keep only the first target seen — and a target's own generic
    /// parameters (e.g. <c>T</c> on <c>extends&lt;T&gt; Box&lt;T&gt;</c>) must stay declared on
    /// the merged method whenever the spelled types reference them.
    /// </summary>
    [Fact]
    public void Library_NestedTargetGroupConvertOperators_UnionCollidingBackerSignatures()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("box.tyhp", """
                <?tyhp
                namespace Lib;
                class Box<T> {
                    public function __construct(public T $value) {}
                }
                class Money {
                    public int $amount = 0;
                }
                class Widget {
                    public int $id = 0;
                }
                extension Combo {
                    extends<T> \Lib\Box<T> {
                        operator convert (T $value) {
                            return new Box($value);
                        }
                        operator convert (self $value): T {
                            return $value->value;
                        }
                    }
                    extends \Lib\Money {
                        operator convert (self $value): int {
                            return $value->amount;
                        }
                        operator convert (string $value) {
                            Money $m = new Money();
                            return $m;
                        }
                    }
                    extends \Lib\Widget {
                        operator convert (self $value): int {
                            return $value->id;
                        }
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));

        // `__toInt` collides across the Money and Widget targets — both operand types must
        // appear in the declared union, not just whichever target was mapped first.
        def.Should().Contain("public static function __toInt(\\Lib\\Money|\\Lib\\Widget $value): int;");

        // `__toT` only has one contributing target (Box<T>), so its own T stays scoped and its
        // operand keeps the full generic instantiation.
        def.Should().Contain("public static function __toT<T>(\\Lib\\Box<T> $value): T;");

        // `__from` merges a from-form out of the generic Box<T> group with one out of the plain
        // Money group: the return type must union both real targets (with Box's own <T> kept
        // intact), and the method itself must declare <T> since the return type still mentions
        // it — not spell a bare, unscoped `T` or drop the Box<T> target's generic entirely.
        def.Should().Contain("public static function __from<T>(T|string $from): \\Lib\\Box<T>|\\Lib\\Money;");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    /// <summary>
    /// A nested target group's own generic parameter can be bounded by <c>self</c> (e.g.
    /// <c>extends&lt;U extends self&gt; Target</c>, meaning "extends the target type"). When that
    /// group's convert form merges into a shared backer method with a *different* target's form
    /// (<c>__toInt</c> colliding across <c>Money</c> and <c>Widget</c>; <c>__from</c> shared
    /// across the whole extension), the constraint must still spell <c>self</c> as *that
    /// contributing form's own* target, not leak the literal keyword onto the backer class
    /// (where <c>self</c> would wrongly mean the backer itself) — mirroring what
    /// <see cref="TyhpCodeTyhpdefGenerator"/>'s single-form <c>MapBackerMethod</c> path already
    /// does via <c>RewriteBackerSelfTypes</c>.
    /// </summary>
    [Fact]
    public void Library_NestedTargetGroupOwnGenericConstrainedBySelf_RewritesConstraintPerContributingTarget()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("box.tyhp", """
                <?tyhp
                namespace Lib;
                class Money {
                    public int $amount = 0;
                }
                class Widget {
                    public int $id = 0;
                }
                extension Combo {
                    extends \Lib\Money {
                        operator convert (self $value): int {
                            return $value->amount;
                        }
                        operator convert (string $value) {
                            Money $m = new Money();
                            return $m;
                        }
                    }
                    extends<U extends self> \Lib\Widget {
                        operator convert (self $value): int {
                            return $value->id;
                        }
                        operator convert (U $value) {
                            Widget $w = new Widget();
                            return $w;
                        }
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));

        // Widget's own `U extends self` merges into the `__toInt` union alongside Money's
        // non-generic to-form on the *backer* — the constraint there must spell Widget, not the
        // backer class or the bare keyword (the block's own extension mapping below still spells
        // `extends<U extends self>`, which is correct — `self` there means the block target).
        def.Should().Contain("public static function __toInt<U extends \\Lib\\Widget>(\\Lib\\Money|\\Lib\\Widget $value): int;");

        // The same `U` merges into the shared `__from`, unioning Money's `string` source.
        def.Should().Contain("public static function __from<U extends \\Lib\\Widget>(string|U $from): \\Lib\\Money|\\Lib\\Widget;");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    /// <summary>
    /// Two sibling nested groups are free to name their own type parameter the same letter (the
    /// binder scopes each group's generic to its own subtree, so nothing stops both writing
    /// <c>extends&lt;U extends self&gt;</c>). When forms from *both* groups merge into one shared
    /// backer (<c>__toInt</c> colliding on return type; <c>__from</c> shared across the whole
    /// extension), <see cref="TyhpCodeTyhpdefGenerator"/>'s per-form <c>self</c> rewrite must not
    /// let the first-seen group's rewritten bound silently clobber the second group's — that
    /// would either drop a real target from the declared bound (unsound: a value satisfying only
    /// the second group's target would fail to type-check against the merged signature) or, if
    /// the two groups shared a target, incorrectly declare a bound the emitted PHP never needs.
    /// The merged declaration must instead union both groups' targets into one constraint, the
    /// same way a merged operand/return type already unions every contributing target. A method
    /// with only one contributing group (<c>identity</c>, only declared on <c>Money</c>'s group)
    /// must keep spelling <c>self</c> as that group's own target and must not pick up the other
    /// group's target just because both groups reuse the letter <c>U</c>.
    /// </summary>
    [Fact]
    public void Library_SiblingTargetGroupsShareOwnGenericName_UnionsSelfBoundConstraintAcrossTargets()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("box.tyhp", """
                <?tyhp
                namespace Lib;
                class Money {
                    public int $amount = 0;
                }
                class Widget {
                    public int $id = 0;
                }
                extension Combo {
                    extends<U extends self> \Lib\Money {
                        function identity(U $u): U {
                            return $u;
                        }
                        operator convert (self $value): int {
                            return $value->amount;
                        }
                        operator convert (string $value) {
                            Money $m = new Money();
                            return $m;
                        }
                    }
                    extends<U extends self> \Lib\Widget {
                        operator convert (self $value): int {
                            return $value->id;
                        }
                        operator convert (U $value) {
                            Widget $w = new Widget();
                            return $w;
                        }
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));

        // Only Money's group contributes `identity` — its own `U` stays bound to Money alone,
        // unaffected by Widget's sibling group reusing the same letter.
        def.Should().Contain(
            "public static function identity<U extends \\Lib\\Money>(\\Lib\\Money $this_, U $u): U;");

        // `__toInt` merges Money's non-generic to-form with Widget's `U extends self` group —
        // the shared `U` must union both targets, not silently keep only Money's (the first
        // group processed) or only Widget's.
        def.Should().Contain(
            "public static function __toInt<U extends \\Lib\\Money|\\Lib\\Widget>"
            + "(\\Lib\\Money|\\Lib\\Widget $value): int;");

        // `__from` merges Money's `string` from-form with Widget's `U`-typed from-form under the
        // same shared backer — same union requirement, and the parameter union must not collapse
        // to a bare, wrongly-scoped `U`.
        def.Should().Contain(
            "public static function __from<U extends \\Lib\\Money|\\Lib\\Widget>"
            + "(string|U $from): \\Lib\\Money|\\Lib\\Widget;");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    /// <summary>
    /// A sibling group whose own type parameter carries no <c>extends</c> bound at all (fully
    /// unconstrained) accepts anything under that name — merging in a second sibling group's
    /// <c>self</c>-bound constraint for the same-named parameter must not narrow the merged
    /// declaration, regardless of which group's form <see cref="TyhpCodeTyhpdefGenerator"/>
    /// processes first (<c>MergeBackerGenerics</c> walks contributing forms in declaration
    /// order, so an unconstrained bound arriving after an already-merged constrained one must
    /// still erase it, not just win when it happens to be seen first).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Library_SiblingTargetGroupsOneUnconstrained_MergedConstraintStaysUnconstrained(
        bool unconstrainedGroupFirst)
    {
        using var project = new TestProjectBuilder();
        var unconstrained = """
            extends<U> \Lib\Money {
                function identity(U $u): U {
                    return $u;
                }
                operator convert (self $value): int {
                    return $value->amount;
                }
                operator convert (string $value) {
                    Money $m = new Money();
                    return $m;
                }
            }
            """;
        var selfBound = """
            extends<U extends self> \Lib\Widget {
                operator convert (self $value): int {
                    return $value->id;
                }
                operator convert (U $value) {
                    Widget $w = new Widget();
                    return $w;
                }
            }
            """;
        var groups = unconstrainedGroupFirst
            ? unconstrained + "\n" + selfBound
            : selfBound + "\n" + unconstrained;

        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("box.tyhp", $$"""
                <?tyhp
                namespace Lib;
                class Money {
                    public int $amount = 0;
                }
                class Widget {
                    public int $id = 0;
                }
                extension Combo {
                    {{groups}}
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));

        // The merged `__toInt`/`__from` must stay unconstrained (no `extends` clause on `U`)
        // because Money's group already accepts anything under that name — re-adding Widget's
        // `self` bound would reject values Money's own real signature allows. Declaration order
        // between the two sibling groups only affects which target is spelled first in the
        // operand/return union, not whether the shared `U` stays unconstrained.
        var operandUnion = unconstrainedGroupFirst ? "\\Lib\\Money|\\Lib\\Widget" : "\\Lib\\Widget|\\Lib\\Money";
        def.Should().Contain($"public static function __toInt<U>({operandUnion} $value): int;");
        def.Should().Contain("public static function __from<U>(");
        def.Should().NotContain("__toInt<U extends");
        def.Should().NotContain("__from<U extends");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Library_SpellsTyhpTypes_NotPhpNamespaceGuesses()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("other.tyhp", """
                <?tyhp
                namespace Other;
                class Widget {
                }
                """)
            .WithTyhpFile("api.tyhp", """
                <?tyhp
                namespace Lib;
                use Other\Widget;
                class Json {
                    public static function decode<T extends struct>(T $value, int $depth = 512, int $flags = 0): T {
                        return $value;
                    }

                    public static function tryDecode<T extends struct>(string $json, T &$out, int $depth = 512): bool {
                        return false;
                    }

                    public static function isType<TTarget>(mixed $value): $value is TTarget {
                        return $value is TTarget;
                    }

                    public static function with<TProperties extends struct, T extends object&TProperties>(
                        T $object,
                        TProperties $properties,
                    ): T {
                        return $object;
                    }

                    public static function make(): Widget {
                        return new Widget();
                    }
                }
                extension Arrays<TKey extends int|string, TValue> extends array<TKey, TValue> {
                    fn keys(): array<TKey, TValue> => $this;
                    fn sortedBy(
                        callable(TValue, TValue): int $callback,
                    ): array<TKey, TValue> => $this;
                }
                interface Bag<TKey, TValue> {
                }
                interface Shape<TStruct extends struct> extends Bag<__IndexKeys<TStruct>, string> {
                    public function offsetGet<TKey extends __IndexKeys<TStruct>>(TKey $offset): __IndexValueType<TStruct, TKey>;
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("function decode<T extends struct>(T $value, int $depth = 512, int $flags = 0): T");
        def.Should().Contain("function tryDecode<T extends struct>(string $json, T &$out, int $depth = 512): bool");
        def.Should().Contain("function isType<TTarget>(mixed $value): $value is TTarget");
        def.Should().Contain("T extends object&TProperties");
        def.Should().Contain("function make(): \\Other\\Widget");
        def.Should().Contain("extension Arrays<TKey extends int|string, TValue> extends array<TKey, TValue> {");
        def.Should().Contain("fn keys(): array<TKey, TValue> => $this;");
        def.Should().Contain("callable(TValue, TValue): int");
        def.Should().Contain("array<TKey, TValue>");
        def.Should().Contain("interface Shape<TStruct extends struct> extends \\Lib\\Bag<__IndexKeys<TStruct>, string>");
        def.Should().Contain("__IndexKeys<TStruct>");
        def.Should().Contain("__IndexValueType<TStruct, TKey>");
        def.Should().NotContain("\\Lib\\struct");
        def.Should().NotContain("): \\Lib\\T");
        def.Should().NotContain("T extends \\Lib\\");
        def.Should().NotContain("\\Lib\\TTarget");
        def.Should().NotContain("\\Lib\\TKey");
        def.Should().NotContain("\\Lib\\TValue");
        def.Should().NotContain("\\Lib\\TProperties");
        def.Should().NotContain("\\Lib\\TStruct");
        def.Should().NotContain("\\Lib\\__IndexKeys");
        def.Should().NotContain("\\Lib\\Widget");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Consumer_AgainstGeneratedTyhpdef_SplicesCopiedExpression()
    {
        using var library = new TestProjectBuilder();
        library
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("pad.tyhp", """
                <?tyhp
                namespace Lib;
                extension StringPad extends string {
                    fn spacePrefix(): string => ' ' . $this;
                }
                """);

        var build = library.RunBuild();
        build.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", build.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(library.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("extension StringPad extends string {");
        def.Should().Contain("fn spacePrefix(): string => ' ' . $this;");
        def.Should().NotContain("StringPad__tyhpExtensionBacker");

        var php = EmitAgainstTyhpdef(
            def,
            """
            <?tyhp
            global use extension \Lib\StringPad;
            function pad(string $s): string {
                return $s->spacePrefix();
            }
            """);

        php.Should().Contain("' ' . $s");
        php.Should().NotContain("->spacePrefix(");
        php.Should().NotContain("StringPad::spacePrefix");
        php.Should().NotContain(GeneratedNames.ExtensionReceiverThisAlias);
    }

    private static string EmitAgainstTyhpdef(string tyhpdef, string tyhp)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "tyhp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tyhpdefPath = Path.Combine(tempDir, "package.tyhpdef");
        var tyhpPath = Path.Combine(tempDir, "main.tyhp");
        File.WriteAllText(tyhpdefPath, tyhpdef);
        File.WriteAllText(tyhpPath, tyhp);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["output:phpVersion"] = "8.4",
                })
                .Build();
            var project = new Project(configuration);
            var includes = new List<string>();
            includes.Add(tyhpdefPath);
            using var compilationService = new CompilationService();
            var result = compilationService.ParseFiles(
                [tyhpPath],
                IsolatedCompilation.CreateOptions(
                    tempDir,
                    phpVersion: "8.4",
                    tyhpdefIncludePaths: includes));

            result.Diagnostics.Errors.Should().BeEmpty(
                string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
            result.ParsedFiles.Should().NotBeNull().And.NotBeEmpty();

            var context = EmitContext.Create(
                result.GlobalScope,
                result.Diagnostics,
                project,
                result.RequiresRuntimeGenericTracking,
                requiresGenericVariant: result.RequiresGenericVariant,
                genericCallTargets: result.GenericCallTargets);
            return string.Join('\n', new TyhpEmitter(context).Emit(result.ParsedFiles!)
                .Select(f => f.GeneratedContent ?? string.Empty));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Library_AttributeContract_RoundTripsAllowUnsetPhpTypeAndPure()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("attrs.tyhp", """
                <?tyhp
                #[\Attribute]
                class Attribute {
                    public const int TARGET_CLASS = 1;
                    public const int TARGET_FUNCTION = 2;
                    public const int TARGET_METHOD = 4;
                    public const int TARGET_PROPERTY = 8;
                    public const int TARGET_CLASS_CONSTANT = 16;
                    public const int TARGET_PARAMETER = 32;
                    public const int TARGET_CONSTANT = 64;
                    public const int TARGET_ALL = 127;
                    public function __construct(int $flags = 0): void {}
                }

                namespace Tyhp {
                    #[\Attribute]
                    final class NoEmit {
                        public function __construct(): void {}
                    }
                }

                namespace Tyhp\Optimize {
                    #[\Attribute]
                    final class Pure {
                        public function __construct(): void {}
                    }
                }

                namespace Lib {
                    #[\Attribute(\Attribute::TARGET_PROPERTY | \Attribute::TARGET_PARAMETER)]
                    final class AllowUnset {}

                    #[\Attribute(\Attribute::TARGET_ALL & ~\Attribute::TARGET_CLASS)]
                    #[\Tyhp\NoEmit()]
                    final class PhpType {
                        public function __construct(public readonly string $type): void {}
                    }

                    final class Holder {
                        #[\Tyhp\Optimize\Pure]
                        public function id(int $x): int {
                            return $x;
                        }
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("TARGET_PROPERTY");
        def.Should().Contain("TARGET_PARAMETER");
        def.Should().Contain("#[\\Attribute(");
        def.Should().Contain("TARGET_ALL");
        def.Should().Contain("TARGET_CLASS");
        def.Should().Contain("#[\\Tyhp\\NoEmit]");
        def.Should().Contain("#[\\Tyhp\\Optimize\\Pure]");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Library_FileAndClassTypeAliases_CopyVisibilityAndStampAliasFactory()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("aliases.tyhp", """
                <?tyhp
                namespace Lib;
                type DecimalCoercible = float|int|string;

                final class UserService {
                    public type NameType = string;
                    protected type Hidden = int;
                    private type Secret = float;
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("type DecimalCoercible = float|int|string;");
        def.Should().Contain("aliasFactory:");
        def.Should().Contain("NameType");
        def.Should().Contain("public type NameType");
        def.Should().Contain("protected type Hidden");
        def.Should().NotContain("type Secret");
        def.Should().Contain("UserService::NameType");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Library_AuthoredGlobalUse_PersistsInPackageTyhpdef()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("box.tyhp", """
                <?tyhp
                namespace Lib;
                class Box {
                    public function get(): int {
                        return 1;
                    }
                }

                class UnusedLocal {}
                """)
            .WithTyhpFile("helpers.tyhp", """
                <?tyhp
                namespace Lib;
                global use extension \Lib\StringHelpers;
                global use \Lib\Box;
                use \Lib\UnusedLocal;

                extension StringHelpers extends string {
                    fn spacePrefix(): string => ' ' . $this;
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("global use extension \\Lib\\StringHelpers;");
        def.Should().Contain("global use \\Lib\\Box;");
        def.Should().NotContain("global use \\Lib\\UnusedLocal");
        def.Should().NotContain("use \\Lib\\UnusedLocal");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Consumer_AgainstGeneratedTyhpdef_CallsGenericBinderNotErasedCall()
    {
        using var library = new TestProjectBuilder();
        library
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("Json.tyhp", """
                <?tyhp
                namespace Lib;
                final class Json {
                    public static function decode<T>(string $json): T {
                        typeof(T);
                        return default(T);
                    }
                }
                """);

        var build = library.RunBuild();
        build.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", build.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(library.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("binder:");
        def.Should().Contain("decode__tyhpGeneric");

        var php = EmitAgainstTyhpdef(
            def,
            """
            <?tyhp
            namespace App;
            type User = struct {
                string $name;
            };
            function load(string $json): User {
                return \Lib\Json::decode<User>($json);
            }
            """);

        // Foreign compiled-library generic sites always bind through \Tyhp\Generic::bind
        // (Workstream A). The binder name is baked into the tyhpdef stamp, not into consumer PHP.
        php.Should().Contain("\\Tyhp\\Generic::bind(\\Lib\\Json::decode(...)");
        php.Should().NotContain("decode__tyhpGeneric");
        php.Should().NotContain("Json::decode($json)");
    }

    [Fact]
    public void Consumer_AgainstBodylessOverlay_DoesNotCallGenericBinder()
    {
        var php = EmitAgainstTyhpdef(
            """
            <?tyhpdef
            namespace Overlay;
            class Json {
                public static function decode<T>(string $json): mixed;
            }
            """,
            """
            <?tyhp
            namespace App;
            type User = struct {
                string $name;
            };
            function load(string $json): mixed {
                return \Overlay\Json::decode<User>($json);
            }
            """);

        php.Should().NotContain("decode__tyhpGeneric");
    }

    [Fact]
    public void Consumer_AgainstGeneratedAlias_CallsFactoryNotInlinedUnion()
    {
        using var library = new TestProjectBuilder();
        library
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("alias.tyhp", """
                <?tyhp
                namespace Lib;
                type DecimalCoercible = float|int|string;
                """);

        var build = library.RunBuild();
        build.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", build.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(library.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("aliasFactory:");

        var php = EmitAgainstTyhpdef(
            def,
            """
            <?tyhp
            function probe(mixed $x): void {
                $t = typeof(\Lib\DecimalCoercible);
                $b = $x is \Lib\DecimalCoercible;
            }
            """);

        php.Should().Contain("DecimalCoercible()");
        php.Should().NotContain("\\Tyhp\\Type::union(");
    }

    [Fact]
    public void Consumer_JsonAssocOverlayAlias_StillInlines()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "application",
                    "include": ["**/*.tyhp"],
                    "tyhpdefInclude": ["./vendor/tyhpdef/acme-stubs/composer.json"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpdefPackageComposer("vendor/tyhpdef/acme-stubs", """
                {
                    "include": ["./_tyhpdef/*.tyhpdef"],
                    "overlay": ["./_tyhpdef/overlays/*.tyhpdef"]
                }
                """, packageName: "tyhpdef/acme-stubs")
            .WithTyhpFile("vendor/tyhpdef/acme-stubs/_tyhpdef/baseline.tyhpdef", """
                <?tyhpdef
                """)
            .WithTyhpFile("vendor/tyhpdef/acme-stubs/_tyhpdef/overlays/Ext.Json.tyhpdef", """
                <?tyhpdef
                type JsonScalar = string|int|float|bool|null;
                type JsonAssoc = JsonScalar|array;
                """)
            .WithTyhpFile("probe.tyhp", """
                <?tyhp
                function probe(mixed $x): void {
                    $t = typeof(\JsonAssoc);
                    $b = $x is \JsonAssoc;
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var php = string.Join(
            '\n',
            (result.OutputFiles ?? []).Select(f => f.GeneratedContent ?? string.Empty));
        php.Should().NotContain("JsonAssoc()");
        php.Should().NotContain("typeof(");
    }

    [Fact]
    public void Consumer_JsonDecodeAsWithTypeArgs_HitsGenericBindNotWrapper()
    {
        using var library = new TestProjectBuilder();
        library
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("ext.tyhp", """
                <?tyhp
                namespace Lib;
                global use extension \Lib\StringHelpers;
                extension StringHelpers extends string {
                    function jsonDecodeAs<T>(): T {
                        typeof(T);
                        return default(T);
                    }
                }
                """);

        var build = library.RunBuild();
        build.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", build.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(library.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("jsonDecodeAs__tyhpGeneric");
        def.Should().Contain("global use extension \\Lib\\StringHelpers;");

        var php = EmitAgainstTyhpdef(
            def,
            """
            <?tyhp
            global use extension \Lib\StringHelpers;
            type User = struct {
                string $name;
            };
            function load(string $s): User {
                return $s->jsonDecodeAs<User>();
            }
            """);

        // Foreign compiled-library generic sites always bind through \Tyhp\Generic::bind
        // (Workstream A), including extension backers. The binder name lives in the tyhpdef
        // stamp, not baked into consumer PHP.
        php.Should().Contain("\\Tyhp\\Generic::bind(\\Lib\\StringHelpers::jsonDecodeAs(...)");
        php.Should().NotContain("jsonDecodeAs__tyhpGeneric");
        php.Should().NotContain("jsonDecodeAs($s)");
        php.Should().NotContain("jsonDecodeAs(null");
    }

    [Fact]
    public void Library_BackedEnum_PreservesBackingTypeAndCaseValues()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("status.tyhp", """
                <?tyhp
                namespace Lib;
                enum Status: int {
                    case Base = 5;
                    case Next = 6;
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("enum Status: int");
        def.Should().Contain("case Base = 5;");
        def.Should().Contain("case Next = 6;");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);

        var php = string.Join(
            '\n',
            (result.OutputFiles ?? []).Select(f => f.GeneratedContent ?? string.Empty));
        php.Should().Contain("enum Status: int");
        php.Should().Contain("case Base = 5");
        php.Should().Contain("case Next = 6");
    }

    [Fact]
    public void Library_UnbackedEnum_OmitsBackingTypeAndCaseValues()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("color.tyhp", """
                <?tyhp
                namespace Lib;
                enum Color {
                    case Red;
                    case Blue;
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("enum Color {");
        def.Should().NotContain("enum Color:");
        def.Should().Contain("case Red;");
        def.Should().Contain("case Blue;");
        def.Should().NotContain("case Red =");
        def.Should().NotContain("case Blue =");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }

    [Fact]
    public void Library_StringBackedEnum_PreservesBackingTypeAndCaseValues()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("suit.tyhp", """
                <?tyhp
                namespace Lib;
                enum Suit: string {
                    case Hearts = 'hearts';
                    case Spades = 'spades';
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("enum Suit: string");
        def.Should().Contain("case Hearts = 'hearts';");
        def.Should().Contain("case Spades = 'spades';");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);

        var php = string.Join(
            '\n',
            (result.OutputFiles ?? []).Select(f => f.GeneratedContent ?? string.Empty));
        php.Should().Contain("enum Suit: string");
        php.Should().Contain("case Hearts = 'hearts'");
        php.Should().Contain("case Spades = 'spades'");
    }

    [Fact]
    public void Library_GenericClass_AlwaysStampsGenericRuntime()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("box.tyhp", """
                <?tyhp
                namespace Lib;
                class Box<T> {
                    public function identity(T $value): T {
                        return $value;
                    }
                }
                class Tracked<T> {
                    public T $value;
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("#[\\Tyhp\\GenericRuntime(");
        def.Should().Contain("erased: true");
        def.Should().Contain("erased: false");
        def.Should().Contain("factory:");
        def.Should().Contain("layouts: [1]");
        def.Should().NotContain("layout: 1");
        def.Should().Contain("function identity");
    }

    [Fact]
    [Trait("Category", "Story27")]
    public void Library_NewClassReturn_WritesShapeAliasIntoPackageTyhpdef()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("clock.tyhp", """
                <?tyhp
                namespace Lib;
                function createClock(): object {
                    return new class {
                        public function now(): int {
                            return 1;
                        }
                    };
                }

                type AuthoredClock = object {
                    public function now(): int;
                };

                function take(AuthoredClock $c): int {
                    return $c->now();
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().NotContain("anonClass@");
        def.Should().Contain("type createClock_Return = object {");
        def.Should().Contain("public function now(): int;");
        def.Should().Contain("function createClock(): createClock_Return;");
        def.Should().Contain("type AuthoredClock = object {");
        def.Should().Contain("function take(");
        def.Should().Contain("AuthoredClock $c): int;");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
    }
}
