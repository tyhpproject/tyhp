using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;
using Tyhp.TyhpLang.Binder.Symbols.Interfaces;

namespace Tyhp.Tests.Binder;

/// <summary>
/// Compiler version-gating mechanism: <c>declare(php=…)</c> / <c>#[\Tyhp\Php]</c> on
/// synthetic stubs. Live php-package contracts are covered by
/// <c>runtime/packages/test-all-tyhpdef.sh</c>.
/// </summary>
[Trait("Category", "Binder")]
[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story21")]
public class PhpPackageVersionGateTests
{
    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    [InlineData("8.4")]
    [InlineData("8.5")]
    public void DeclareWrappedDisjointExtensions_KeepSinglePadArm(string phpVersion)
    {
        using var builder = new TestProjectBuilder();
        builder.WithTyhpJson($$"""
            {
                "include": ["src/**/*.tyhp"],
                "tyhpdefInclude": ["defs/**/*.tyhpdef"],
                "output": { "path": "build/", "phpVersion": "{{phpVersion}}" }
            }
            """);
        builder.WithTyhpFile("src/app.tyhp", """
            <?tyhp
            function app_entry(): void {}
            """);
        builder.WithTyhpFile("defs/pad.tyhpdef", """
            <?tyhpdef
            namespace Tyhp;
            declare(php="<8.3") {
                extension StringPadProbe extends string {
                    fn pad(int $length): string => \str_pad($this, $length);
                }
            }
            declare(php=">=8.3") {
                extension StringPadProbe extends string {
                    fn pad(int $length): string => \mb_str_pad($this, $length);
                }
            }
            """);

        var project = builder.BuildProject();
        var userFile = Path.Combine(project.GetProjectPath(), "src", "app.tyhp");
        var (global, diagnostics) = Bind(project, userFile, phpVersion);

        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var ext = FindObject(global, "StringPadProbe");
        ext.Should().NotBeNull();
        ext!.Members.Keys.Where(k => string.Equals(k, "pad", StringComparison.OrdinalIgnoreCase))
            .Should().HaveCount(1, "version-disjoint pad arms must leave one method");
    }

    [Theory]
    [InlineData("8.2", @"\str_pad(", @"\mb_str_pad(")]
    [InlineData("8.3", @"\mb_str_pad(", @"\str_pad(")]
    [InlineData("8.4", @"\mb_str_pad(", @"\str_pad(")]
    [InlineData("8.5", @"\mb_str_pad(", @"\str_pad(")]
    public void StringPad_EmitsNativeHelperForTarget(string phpVersion, string expected, string unexpected)
    {
        var php = EmitGatedCall(phpVersion, """
            <?tyhpdef
            namespace Tyhp;
            declare(php="<8.3") {
                extension StringVersioned extends string {
                    fn pad(int $length): string => \str_pad($this, $length);
                }
            }
            declare(php=">=8.3") {
                extension StringVersioned extends string {
                    fn pad(int $length): string => \mb_str_pad($this, $length);
                }
            }
            global use extension \Tyhp\StringVersioned;
            """, """
            <?tyhp
            function pad_it(string $s): string {
                return $s->pad(10);
            }
            """);

        php.Should().Contain(expected);
        php.Should().NotContain(unexpected);
    }

    [Theory]
    [InlineData("8.3", @"\mb_strtoupper(", @"\mb_ucfirst(")]
    [InlineData("8.4", @"\mb_ucfirst(", @"\mb_strtoupper(")]
    public void StringUcFirst_EmitsNativeHelperForTarget(string phpVersion, string expected, string unexpected)
    {
        var php = EmitGatedCall(phpVersion, """
            <?tyhpdef
            namespace Tyhp;
            declare(php="<8.4") {
                extension StringVersioned extends string {
                    fn ucFirst(): string => \mb_strtoupper(\mb_substr($this, 0, 1)) . \mb_substr($this, 1);
                }
            }
            declare(php=">=8.4") {
                extension StringVersioned extends string {
                    fn ucFirst(): string => \mb_ucfirst($this);
                }
            }
            global use extension \Tyhp\StringVersioned;
            """, """
            <?tyhp
            function title(string $s): string {
                return $s->ucFirst();
            }
            """);

        php.Should().Contain(expected);
        php.Should().NotContain(unexpected);
    }

    [Theory]
    [InlineData("8.3", "ArrayExtensions::find", @"\array_find(")]
    [InlineData("8.4", @"\array_find(", "ArrayExtensions::find")]
    [InlineData("8.5", @"\array_find(", "ArrayExtensions::find")]
    public void ArrayFind_EmitsNativeHelperForTarget(string phpVersion, string expected, string unexpected)
    {
        var php = EmitGatedCall(phpVersion, """
            <?tyhpdef
            namespace Tyhp;
            class ArrayExtensions as ArrayExtensions__tyhpExtensionBacker {
                public static function find<TKey, TValue>(array<TKey, TValue> $this_, callable(TValue, TKey): bool $callback): ?TValue;
            }
            declare(php="<8.4") {
                extension ArrayVersioned<TKey, TValue> extends array<TKey, TValue> {
                    fn find(callable(TValue, TKey): bool $callback): ?TValue
                        => ArrayExtensions__tyhpExtensionBacker::find($this, $callback);
                }
            }
            declare(php=">=8.4") {
                extension ArrayVersioned<TKey, TValue> extends array<TKey, TValue> {
                    fn find(callable(TValue, TKey): bool $callback): ?TValue
                        => \array_find($this, $callback);
                }
            }
            global use extension \Tyhp\ArrayVersioned;
            """, """
            <?tyhp
            function pick(array<int, string> $a, callable(string, int): bool $cb): mixed {
                return $a->find($cb);
            }
            """);

        php.Should().Contain(expected);
        php.Should().NotContain(unexpected);
    }

    [Theory]
    [InlineData("8.4", "ArrayExtensions::first", @"\array_first(")]
    [InlineData("8.5", @"\array_first(", "ArrayExtensions::first")]
    public void ArrayFirst_EmitsNativeHelperForTarget(string phpVersion, string expected, string unexpected)
    {
        var php = EmitGatedCall(phpVersion, """
            <?tyhpdef
            namespace Tyhp;
            class ArrayExtensions as ArrayExtensions__tyhpExtensionBacker {
                public static function first<TKey, TValue>(array<TKey, TValue> $this_): ?TValue;
            }
            declare(php="<8.5") {
                extension ArrayVersioned<TKey, TValue> extends array<TKey, TValue> {
                    fn first(): ?TValue
                        => ArrayExtensions__tyhpExtensionBacker::first($this);
                }
            }
            declare(php=">=8.5") {
                extension ArrayVersioned<TKey, TValue> extends array<TKey, TValue> {
                    fn first(): ?TValue
                        => \array_first($this);
                }
            }
            global use extension \Tyhp\ArrayVersioned;
            """, """
            <?tyhp
            function head(array $a): mixed {
                return $a->first();
            }
            """);

        php.Should().Contain(expected);
        php.Should().NotContain(unexpected);
    }

    /// <summary>
    /// A <c>#[\Tyhp\Php]</c>-gated attribute class is absent below the gate, so
    /// consumer usage reports TYHP4126.
    /// </summary>
    [Theory]
    [InlineData("8.2")]
    [InlineData("8.3")]
    public void GatedAttributeClass_AbsentBelowGate_Reports4126(string phpVersion)
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            #[\GatedAttr("old")]
            function legacy(): void {}

            function caller(): void {
                legacy();
            }
            """,
            """
            <?tyhpdef
            #[\Tyhp\Php(">=8.4")]
            #[\Attribute]
            final class GatedAttr {
                public function __construct(?string $message = null): void;
            }
            """,
            phpVersion: phpVersion);

        result.Diagnostics.Errors.Should().Contain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass,
            "gated attribute class must not resolve below its #[\\Tyhp\\Php] gate");
    }

    [Fact]
    public void NoDiscardAttribute_Synthetic_WarnsOnDiscard()
    {
        var result = IsolatedCompilation.ParseSnippet(
            """
            <?tyhp
            #[\NoDiscard("ignored")]
            function compute(): int {
                return 42;
            }

            function caller(): void {
                compute();
            }
            """,
            """
            <?tyhpdef
            #[\Attribute]
            final class NoDiscard {
                public function __construct(?string $message = null): void;
            }
            """,
            phpVersion: "8.5");

        result.Diagnostics.Errors.Should().NotContain(
            d => d.Code == MessageCode.CheckerNotAnAttributeClass,
            "#[\\NoDiscard] must resolve to the synthetic attribute class");
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        result.Diagnostics.Should().Contain(
            d => d.Code == MessageCode.CheckerNoDiscardReturnUnused,
            "discarding a #[\\NoDiscard] return must warn TYHP4165");
    }

    private static string EmitGatedCall(string phpVersion, string tyhpdef, string tyhp)
    {
        using var builder = new TestProjectBuilder();
        builder.WithTyhpJson($$"""
            {
                "include": ["src/**/*.tyhp"],
                "tyhpdefInclude": ["defs/**/*.tyhpdef"],
                "output": { "path": "build/", "phpVersion": "{{phpVersion}}" }
            }
            """);
        builder.WithTyhpFile("defs/gated.tyhpdef", tyhpdef);
        builder.WithTyhpFile("src/app.tyhp", tyhp);
        builder.WithConfigValue("no-cache", "true");
        builder.WithConfigValue("quiet", "true");

        var result = builder.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

        var buildDir = Path.Combine(builder.ProjectDirectory, "build");
        var phpFiles = Directory.GetFiles(buildDir, "*.php", SearchOption.AllDirectories);
        phpFiles.Should().NotBeEmpty("expected emitted PHP under build/");
        return string.Join('\n', phpFiles.Select(File.ReadAllText));
    }

    private static (GlobalScope Global, DiagnosticBag Diagnostics) Bind(
        Project project,
        string userFile,
        string phpVersion)
    {
        using var compilationService = new CompilationService();
        var options = CompilationOptions.FromProject(project, o =>
        {
            o.PhpVersion = phpVersion;
            o.PhpVersionWasDefaulted = false;
            o.EnableAstCache = false;
            o.SkipChecking = true;
        });
        var result = compilationService.ParseFiles([userFile], options);
        result.GlobalScope.Should().NotBeNull();
        return (result.GlobalScope!, result.Diagnostics);
    }

    private static ObjectDeclarationSymbol? FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        Walk(global, symbol =>
        {
            if (found == null
                && symbol is ObjectDeclarationSymbol obj
                && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = obj;
            }
        });
        return found;
    }

    private static void Walk(IBaseScope scope, Action<IBaseSymbol> visit)
    {
        foreach (var symbol in scope.GetAllChildSymbols())
        {
            visit(symbol);
        }

        foreach (var child in scope.GetAllChildScopes())
        {
            Walk(child, visit);
        }
    }

}
