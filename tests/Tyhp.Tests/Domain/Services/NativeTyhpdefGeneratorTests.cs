using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class NativeTyhpdefGeneratorTests
{
    [Fact]
    public void Generate_Source_WritesParseableTyhpdefWithMergedTypes()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-native-").FullName;
        try
        {
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(TyhpdefGenFixtures.NativeDir, "*.php")],
            };
            var result = Run(options, TyhpdefGenFixtures.NativeDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.GeneratedFiles.Should().ContainSingle(p =>
                p.EndsWith("source.tyhpdef", StringComparison.Ordinal));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().StartWith("<?tyhpdef");
            text.Should().Contain("namespace Acme\\Demo {");
            text.Should().Contain("class Widget");
            text.Should().Contain("extends \\ArrayObject");
            text.Should().Contain("implements \\Acme\\Log\\LoggerInterface");
            text.Should().Contain("function map");
            text.Should().Contain("array<string, T>");
            text.Should().Contain("array<int, T>");
            text.Should().Contain("@template T");
            text.Should().Contain("A widget.");
            text.Should().Contain("Maps items.");
            text.Should().Contain("function touch");
            text.Should().NotContain("function hide");
            text.Should().NotContain("$secret");
            text.Should().Contain("deprecated public function old");
            text.Should().Contain("function __construct");
            text.Should().Contain("string $label");
            text.Should().Contain("public string $label");
            text.Should().Contain("public array<string, int> $counts ?? [];");
            text.Should().Contain("protected int $size ?? 1;");
            text.Should().Contain("int $size = 1");
            text.Should().Contain("bool $hidden");
            text.Should().NotContain("private bool $hidden");
            text.Should().Contain("function getName");
            text.Should().Contain("int $id");
            text.Should().Contain("interface Named");
            text.Should().Contain("function helper");
            text.Should().Contain("const string KIND");
            text.Should().NotContain("overlays" + Path.DirectorySeparatorChar + "stubs");
            AssertParses(text);
            result.ClassCount.Should().BeGreaterThanOrEqualTo(2);
            result.FunctionCount.Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void Generate_InvalidPhp_Reports7502()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-parse-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-parse-src-").FullName;
        try
        {
            var php = Path.Combine(sourceDir, "Broken.php");
            File.WriteAllText(php, "<?php\nfunction (\n");
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [php],
            };
            var result = Run(options, sourceDir);

            result.Success.Should().BeFalse();
            result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefSourceParseError);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_SkipsBrokenFileAndHarvestsSibling()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-skip-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-skip-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Broken.php"), "<?php\nfunction (\n");
            File.WriteAllText(Path.Combine(sourceDir, "Ok.php"), """
                <?php
                namespace Acme;
                class Widget {
                    public function ping(): string {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.Diagnostics.Warnings.Should().Contain(d => d.Code == MessageCode.TyhpdefSourceFileSkipped);
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("class Widget");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_KeepsImportedImplementsWhenSiblingFileIsUnparseable()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-import-impl-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-import-impl-src-").FullName;
        try
        {
            var tyhpMissing = IsolatedCompilation.ParseSnippet(
                """
                <?tyhp
                use Other\HasEmbeddedView;
                class C implements HasEmbeddedView {}
                """,
                skipChecking: true,
                includeMinimalPhpStubs: false);
            var tyhpdefMissing = IsolatedCompilation.ParseSnippet(
                """
                <?tyhpdef
                use Other\HasEmbeddedView;
                class C implements HasEmbeddedView {}
                """,
                skipChecking: true,
                includeMinimalPhpStubs: false,
                fileName: "snippet.tyhpdef");
            tyhpdefMissing.Diagnostics.Errors.Should().Contain(d =>
                d.Code == MessageCode.BinderUnresolvedImplementsType
                && d.Message.Contains("HasEmbeddedView"));
            tyhpMissing.Diagnostics.Errors.Should().Contain(d =>
                d.Code == MessageCode.BinderUnresolvedImplementsType
                && d.Message.Contains("HasEmbeddedView"));

            File.WriteAllText(Path.Combine(sourceDir, "Broken.php"), "<?php\nfunction (\n");
            File.WriteAllText(Path.Combine(sourceDir, "Column.php"), """
                <?php
                namespace Acme\Tables\Columns;
                class Column {}
                """);
            File.WriteAllText(Path.Combine(sourceDir, "CanFormatState.php"), """
                <?php
                namespace Acme\Tables\Columns\Concerns;
                trait CanFormatState {}
                """);
            File.WriteAllText(Path.Combine(sourceDir, "TextColumn.php"), """
                <?php
                namespace Acme\Tables\Columns;
                use Acme\Support\Components\Contracts\HasEmbeddedView;
                use Acme\Support\Concerns\CanBeCopied;
                class TextColumn extends Column implements HasEmbeddedView {
                    use CanBeCopied;
                    use Concerns\CanFormatState;
                    public function badge(): string {}
                }
                """);
            File.WriteAllText(Path.Combine(sourceDir, "Bare.php"), """
                <?php
                namespace Acme;
                class Bare implements NotImported {}
                """);

            using (var compilation = new CompilationService())
            {
                var compiled = compilation.ParseFiles(
                    [
                        Path.Combine(sourceDir, "TextColumn.php"),
                        Path.Combine(sourceDir, "Column.php"),
                        Path.Combine(sourceDir, "CanFormatState.php"),
                        Path.Combine(sourceDir, "Bare.php"),
                    ],
                    IsolatedCompilation.CreateOptions(sourceDir, skipChecking: true, includeMinimalPhpStubs: false));
                compiled.Diagnostics.Errors.Should().NotContain(
                    d => d.Code == MessageCode.BinderUnresolvedImplementsType
                         && d.Message.Contains("HasEmbeddedView"),
                    string.Join("; ", compiled.Diagnostics.Errors.Select(e => e.Message)));
                compiled.Diagnostics.Errors.Should().NotContain(
                    d => d.Code == MessageCode.BinderUnresolvedImplementsType
                         && d.Message.Contains("CanBeCopied"));
                compiled.Diagnostics.Errors.Should().Contain(
                    d => d.Code == MessageCode.BinderUnresolvedImplementsType
                         && d.Message.Contains("NotImported"));
            }

            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.Diagnostics.Warnings.Should().Contain(d =>
                d.Code == MessageCode.TyhpdefSourceFileSkipped
                && (d.Message.Contains("Broken.php") || (d.FileName != null && d.FileName.Contains("Broken.php"))));
            result.Diagnostics.Warnings.Should().NotContain(d =>
                d.Code == MessageCode.TyhpdefSourceFileSkipped
                && (d.Message.Contains("TextColumn") || d.Message.Contains("HasEmbeddedView") || d.Message.Contains("Bare.php")));

            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("class TextColumn extends \\Acme\\Tables\\Columns\\Column implements \\Acme\\Support\\Components\\Contracts\\HasEmbeddedView");
            text.Should().Contain("use \\Acme\\Support\\Concerns\\CanBeCopied");
            text.Should().Contain("\\Acme\\Tables\\Columns\\Concerns\\CanFormatState");
            text.Should().Contain("class Bare");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_KeepsSiblingParsedAfterInlineHtmlFile()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-html-sib-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-html-sib-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "ABad.php"), """
                <?php
                namespace Acme;
                class BadView {
                    public function view( {
                    }
                }
                """);
            File.WriteAllText(Path.Combine(sourceDir, "ZGood.php"), """
                <?php
                namespace Acme;
                class Widget {
                    public function ping(): string {}
                }
                """);

            using var compilation = new CompilationService();
            var options = IsolatedCompilation.CreateOptions(sourceDir, skipChecking: true, includeMinimalPhpStubs: false);
            var badFirst = compilation.ParseFiles(
                [Path.Combine(sourceDir, "ABad.php")],
                options);
            badFirst.Diagnostics.Errors.Should().Contain(d => (int)d.Code > 0 && (int)d.Code < 3000);
            var goodSecond = compilation.ParseFiles(
                [Path.Combine(sourceDir, "ZGood.php")],
                options);
            goodSecond.Diagnostics.Errors.Should().BeEmpty(
                string.Join("; ", goodSecond.Diagnostics.Errors.Select(e => $"{e.FileName}:{e.Line} {e.Message}")));

            var generate = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(generate, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.Diagnostics.Warnings.Should().Contain(d =>
                d.Code == MessageCode.TyhpdefSourceFileSkipped
                && (d.Message.Contains("ABad.php") || (d.FileName != null && d.FileName.Contains("ABad.php"))));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("class Widget");
            text.Should().NotContain("class BadView");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_HarvestsClassWithInlineHtmlMethod()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-html-keep-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-html-keep-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "ToggleColumn.php"), """
                <?php
                namespace Acme\Tables\Columns;
                class ToggleColumn {
                    public function toEmbeddedHtml(): string {
                        $state = true;
                        ob_start(); ?>
                        <div x-bind:class="state">
                            <?php // tabindex stays client-side ?>
                            <?= $state ? 1 : 0 ?>
                            <?php if ($state) { ?>
                                x-cloak
                            <?php } ?>
                            <?php foreach ([1] as $step) { ?>
                                <span><?= $step ?></span>
                            <?php } ?>
                        </div>
                        <?php return (string) ob_get_clean();
                    }
                }
                """);

            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.Diagnostics.Warnings.Should().NotContain(d => d.Code == MessageCode.TyhpdefSourceFileSkipped);
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("class ToggleColumn");
            text.Should().Contain("function toEmbeddedHtml(): string");
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_TyhpFileReports7507()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-native-tyhp-").FullName;
        try
        {
            var tyhp = Path.Combine(TyhpdefGenFixtures.NativeDir, "not-php.tyhp");
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [tyhp],
            };
            var result = Run(options, TyhpdefGenFixtures.NativeDir);

            result.Success.Should().BeFalse();
            result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefSourceNotPhp);
            result.GeneratedFiles.Should().BeEmpty();
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void Generate_HookedPhpProperty_EmitsBodylessGetAndByRefGet()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-native-hooks-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-native-hooks-src-").FullName;
        try
        {
            var php = Path.Combine(sourceDir, "Holder.php");
            File.WriteAllText(php, """
                <?php
                class Holder {
                    public string $n {
                        get => 'x';
                    }
                    public array $items {
                        &get {
                            return $this->items;
                        }
                    }
                    public string $plain;
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [php],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("public string $n { get; }");
            text.Should().Contain("public array $items { &get; }");
            text.Should().Contain("public string $plain;");
            text.Should().NotContain("get =>");
            text.Should().NotContain("return $this->items");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_PhpPropertyDefault_EmitsCoalesceNotEquals()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-native-propdef-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-native-propdef-src-").FullName;
        try
        {
            var php = Path.Combine(sourceDir, "Bag.php");
            File.WriteAllText(php, """
                <?php
                class Bag {
                    public array $items = [];
                    protected ?\Acme\Log\LoggerInterface $logger = null;
                    public string $plain;
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [php],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("public array $items ?? [];");
            text.Should().Contain("protected ?\\Acme\\Log\\LoggerInterface $logger ?? null;");
            text.Should().Contain("public string $plain;");
            text.Should().NotContain("$items =");
            text.Should().NotContain("$logger =");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_PromotedConstructorParam_PropertyGetsCoalesce_ParamKeepsEquals()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-native-promoted-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-native-promoted-src-").FullName;
        try
        {
            var php = Path.Combine(sourceDir, "Counter.php");
            File.WriteAllText(php, """
                <?php
                class Counter {
                    public function __construct(public int $count = 0) {
                    }
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [php],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("public int $count ?? 0;");
            text.Should().Contain("function __construct(int $count = 0): void;");
            text.Should().NotContain("public int $count =");
            text.Should().NotContain("function __construct(public int $count");
            text.Should().NotContain("$count ?? 0)");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_UntypedMethodReturn_IsMixed_ConstructorIsVoid()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-native-untyped-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-native-untyped-src-").FullName;
        try
        {
            var php = Path.Combine(sourceDir, "ClosureLike.php");
            File.WriteAllText(php, """
                <?php
                final class ClosureLike {
                    public function __construct() {}
                    public function __invoke() {}
                    public function untyped() {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [php],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("function __construct(): void;");
            text.Should().Contain("function __invoke(): mixed;");
            text.Should().Contain("function untyped(): mixed;");
            text.Should().NotContain("function __invoke(mixed");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_HookedPhpProperty_OmitsExplicitPublicHookModifier()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-native-hooks-pub-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-native-hooks-pub-src-").FullName;
        try
        {
            var php = Path.Combine(sourceDir, "Holder.php");
            File.WriteAllText(php, """
                <?php
                class Holder {
                    public string $name {
                        public get => $this->name;
                        private set {
                            $this->name = $value;
                        }
                    }
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [php],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("public string $name { get; private set; }");
            text.Should().NotContain("public get");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_HookedPhpPropertyWithDefault_EmitsCoalesceBeforeHooks()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-native-hooks-def-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-native-hooks-def-src-").FullName;
        try
        {
            var php = Path.Combine(sourceDir, "Holder.php");
            File.WriteAllText(php, """
                <?php
                class Holder {
                    public string $name = 'x' {
                        get => $this->name;
                    }
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [php],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("public string $name ?? 'x' { get; }");
            text.Should().NotContain("$name =");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_PackagePath_ReadsComposerAutoloadAndSkipsNestedVendor()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-pkg-").FullName;
        try
        {
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.ComposerPackage,
                PackagePath = TyhpdefGenFixtures.PackageMiniDir,
            };
            var result = Run(options, TyhpdefGenFixtures.PackageMiniDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            result.GeneratedFiles.Should().ContainSingle(p =>
                p.EndsWith("acme.widget.tyhpdef", StringComparison.Ordinal));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("class Box");
            text.Should().Contain("function volume");
            text.Should().Contain("function widget_helper");
            text.Should().NotContain("ShouldSkip");
            Directory.Exists(Path.Combine(outputDir, "overlays", "stubs")).Should().BeFalse();
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void Generate_PackagePath_MissingComposerJson_Reports7500()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-nocomposer-").FullName;
        var packageDir = Directory.CreateTempSubdirectory("tyhpdef-empty-pkg-").FullName;
        try
        {
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.ComposerPackage,
                PackagePath = packageDir,
            };
            var result = Run(options, packageDir);

            result.Success.Should().BeFalse();
            result.Diagnostics.Errors.Should().Contain(d =>
                d.Code == MessageCode.TyhpdefGenerationError
                && d.Message.Contains("composer.json", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(packageDir);
        }
    }

    [Fact]
    public void Generate_NoDocs_OmitsCopiedComments()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-nodocs-").FullName;
        try
        {
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(TyhpdefGenFixtures.NativeDir, "Widget.php")],
                IncludeDocComments = false,
            };
            var result = Run(options, TyhpdefGenFixtures.NativeDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().NotContain("A widget.");
            text.Should().NotContain("Maps items.");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
        }
    }

    [Fact]
    public void Generate_PhpStanTypeShapes_BecomePrefixedStructsAndKeepClassConstantDefaults()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-native-phpstan-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-native-phpstan-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Handlers.php"), """
                <?php
                namespace AcmeLog\Handler;

                use AcmeLog\Level;

                /**
                 * @phpstan-type Options array{
                 *     index: string,
                 *     type: string,
                 *     ignore_error: bool
                 * }
                 * @phpstan-type InputOptions array{
                 *     index?: string,
                 *     type?: string,
                 *     ignore_error?: bool
                 * }
                 */
                class SearchHandler
                {
                    /**
                     * @var Options
                     */
                    protected array $options;

                    /**
                     * @phpstan-param InputOptions $options
                     */
                    public function __construct(array $options = [], int|string|Level $level = Level::Debug, bool $bubble = true)
                    {
                    }

                    /**
                     * @phpstan-return Options
                     */
                    public function getOptions(): array
                    {
                        return $this->options;
                    }
                }

                /**
                 * @phpstan-type Options array{host: string, port: int}
                 * @phpstan-type InputOptions array{host?: string, port?: int}
                 * @psalm-type UserId = int|string
                 */
                class StoreHandler
                {
                    /**
                     * @phpstan-param InputOptions $options
                     */
                    public function __construct(array $options = [])
                    {
                    }
                }

                /**
                 * @phpstan-import-type Options from SearchHandler as SearchOptions
                 */
                class Wrapper
                {
                    /**
                     * @phpstan-param SearchOptions $options
                     */
                    public function take(array $options): void
                    {
                    }
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("type SearchHandlerOptions = struct");
            text.Should().Contain("type SearchHandlerInputOptions = struct");
            text.Should().Contain("type StoreHandlerOptions = struct");
            text.Should().Contain("type StoreHandlerInputOptions = struct");
            text.Should().Contain("type StoreHandlerUserId = int|string;");
            text.Should().Contain("SearchHandlerInputOptions $options = []");
            text.Should().Contain("int|string|\\AcmeLog\\Level $level = \\AcmeLog\\Level::Debug");
            text.Should().Contain("getOptions()");
            text.Should().Contain("StoreHandlerInputOptions $options = []");
            text.Should().Contain("function take(");
            text.Should().Contain("SearchHandlerOptions $options");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void MergeTypes_PrefersPhpDocWhenPhpHintIsWeak()
    {
        PhpAstTypeExtractor.MergeTypes("array", "array<string, int>").Should().Be("array<string, int>");
        PhpAstTypeExtractor.MergeTypes("string", "non-empty-string").Should().Be("string");
        PhpAstTypeExtractor.MergeTypes("", "int").Should().Be("int");
        PhpAstTypeExtractor.MergeTypes(null, null).Should().Be("mixed");
        PhpAstTypeExtractor.MergeTypes("array", "list<T>").Should().Be("array<int, T>");
        PhpAstTypeExtractor.MergeTypes("mixed", "list").Should().Be("array");
        PhpAstTypeExtractor.MergeTypes("mixed", "empty").Should().Be("mixed");
        PhpAstTypeExtractor.MergeTypes("mixed", "$this").Should().Be("static");
        PhpAstTypeExtractor.MergeTypes("mixed", "DateTime/DateTimeImmutable").Should().Be("DateTime|DateTimeImmutable");
        PhpAstTypeExtractor.MergeTypes("callable", "callable(int): void").Should().Contain("callable");
        PhpAstTypeExtractor.MergeTypes("", "integer").Should().Be("int");
        PhpAstTypeExtractor.MergeTypes("", "boolean").Should().Be("bool");
        PhpAstTypeExtractor.MergeTypes("", "double").Should().Be("float");
        PhpAstTypeExtractor.MergeTypes("", "long").Should().Be("int");
        PhpAstTypeExtractor.MergeTypes("mixed", "integer|boolean").Should().Be("int|bool");
    }

    [Fact]
    public void MergeTypes_QualifiesRelativeNamespacedPhpDocNames()
    {
        var resolver = new PhpTypeNameResolver("AcmePdf", new Dictionary<string, string>());
        PhpAstTypeExtractor.MergeTypes("", @"FrameDecorator\AbstractFrameDecorator", resolver)
            .Should().Be(@"\AcmePdf\FrameDecorator\AbstractFrameDecorator");
    }

    [Fact]
    public void MergeTypes_WithResolver_QualifiesOnlyUseImportedDocNames()
    {
        // A `use` import in the source file: a bare PHPDoc reference to
        // `Document` (e.g. `@param Document[] $documents`) must resolve to the FQCN,
        // the same way a native `Document $x` parameter type already would.
        var resolver = new PhpTypeNameResolver(
            "Acme\\Handler",
            new Dictionary<string, string> { ["Document"] = "\\Acme\\Document" });

        PhpAstTypeExtractor.MergeTypes("array", "array<int, Document>", resolver)
            .Should().Be("array<int, \\Acme\\Document>");

        // `@phpstan-type Options array{...}` is a local pseudo-type with no `use` import.
        // MergeTypes leaves it as written; NativeTyhpdefGenerator rewrites it to the
        // class-prefixed struct / alias name after materializing the PHPDoc tag.
        PhpAstTypeExtractor.MergeTypes("array", "Options", resolver)
            .Should().Be("Options");
        PhpAstTypeExtractor.MergeTypes("array", "InputOptions", resolver)
            .Should().Be("InputOptions");
    }

    [Fact]
    public void ResolveTypeExpression_QualifiesUnqualifiedNameInsideGeneric()
    {
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "\\Acme\\Mapping\\ClassMetadata",
            "\\Acme\\Mapping\\ClassMetadataFactory",
        };
        var resolver = new PhpTypeNameResolver(
            "Acme\\Mapping",
            new Dictionary<string, string>(),
            knownShortNames: null,
            declaredFqns: declared)
        {
            CurrentTypeFqn = "\\Acme\\Mapping\\ClassMetadataFactory",
        };

        resolver.ResolveTypeExpression("ClassMetadata<object>")
            .Should().Be("\\Acme\\Mapping\\ClassMetadata<object>");
        resolver.ResolveTypeExpression("ClassMetadata")
            .Should().Be("\\Acme\\Mapping\\ClassMetadata");
        resolver.ResolveTypeExpression("Box<T>")
            .Should().Be("Box<T>");
    }

    [Fact]
    public void CollectSourceFiles_LiteralNonPhpIsReturnedFor7507()
    {
        var tyhp = Path.Combine(TyhpdefGenFixtures.NativeDir, "not-php.tyhp");
        var files = NativeTyhpdefGenerator.CollectSourceFiles([tyhp], TyhpdefGenFixtures.NativeDir);
        files.Should().Contain(p => p.EndsWith("not-php.tyhp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ComposerCollector_SkipsNestedVendor()
    {
        var collected = ComposerAutoloadPhpCollector.Collect(TyhpdefGenFixtures.PackageMiniDir, includeDev: false);
        collected.ErrorKey.Should().BeNull();
        collected.PackageName.Should().Be("acme/widget");
        collected.Files.Should().Contain(p => p.EndsWith("Box.php", StringComparison.OrdinalIgnoreCase));
        collected.Files.Should().NotContain(p => p.Contains($"{Path.DirectorySeparatorChar}vendor{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generate_Source_RewritesStaticValueTypesToSelf()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-static-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-static-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Bag.php"), """
                <?php
                namespace Acme;
                /**
                 * @template TKey of array-key
                 * @template TValue
                 */
                class Bag {
                    /**
                     * @var callable|static|array<TKey, TValue>
                     */
                    public $source;
                    /**
                     * @param array<TKey, TValue>|static<TKey, TValue> $value
                     * @return array<TKey, TValue>
                     */
                    public static function unwrap($value) {}
                    /**
                     * @return static<TKey, TValue>
                     */
                    public function copy() {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("callable|self|array<TKey, TValue> $source");
            text.Should().Contain("array<TKey, TValue>|self<TKey, TValue> $value");
            text.Should().Contain("function copy(): static<TKey, TValue>");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_RewritesMagicStaticThisListAndEmpty()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-magic-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-magic-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Model.php"), """
                <?php
                namespace Acme;
                /**
                 * @method Builder<static> query(Builder<static> $query)
                 * @property list<string> $tags
                 * @property empty $none
                 * @return $this
                 */
                class Model {
                    /**
                     * @param list<int> $ids
                     * @param empty $unused
                     * @return $this
                     */
                    public function fill($ids, $unused) {
                        return $this;
                    }
                    /**
                     * @return DateTime/DateTimeImmutable
                     */
                    public function when() {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("array<int, int> $ids");
            text.Should().Contain("mixed $unused");
            text.Should().Contain("function fill(");
            text.Should().Contain("): static");
            text.Should().Contain("mixed $none");
            text.Should().Contain("DateTime|DateTimeImmutable");
            text.Should().Contain("Builder<self>");
            text.Should().Contain("array<int, string> $tags");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_EmitsKeywordClassNameAsFqcn()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-is-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-is-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Is.php"), """
                <?php
                namespace Acme\Probe;
                class Is {
                    public function matches($item) {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("class \\Acme\\Probe\\Is");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_TraitAliasVisibilityOnlyDoesNotCrash()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-trait-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-trait-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Driver.php"), """
                <?php
                namespace Acme\Mapping\Driver;
                trait ColocatedMappingDriver {
                    public function addPaths(array $paths): void {}
                    public function addFileExtension(string $ext): void {}
                }
                class StaticPHPDriver {
                    use ColocatedMappingDriver {
                        addPaths as private;
                        addFileExtension as private;
                    }
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("class StaticPHPDriver");
            text.Should().Contain("addPaths as private");
            text.Should().Contain("addFileExtension as private");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_QualifiesKnownPhpAndPsrShortNames()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-psr-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-psr-src-").FullName;
        var catalogRoot = Directory.CreateTempSubdirectory("tyhpdef-psr-cat-").FullName;
        try
        {
            var phpDir = Path.Combine(catalogRoot, "acme-stubs");
            Directory.CreateDirectory(Path.Combine(phpDir, "_tyhpdef"));
            File.WriteAllText(Path.Combine(phpDir, "composer.json"), """
                {
                    "name": "tyhpdef/acme-stubs",
                    "version": "1.0.0",
                    "require": { "php": ">=8.2" },
                    "extra": { "tyhp": { "package": { "include": ["./_tyhpdef/*.tyhpdef"] } } }
                }
                """);
            File.WriteAllText(Path.Combine(phpDir, "_tyhpdef", "php.tyhpdef"), """
                <?tyhpdef
                class DateTime {}
                """);
            var psrDir = Path.Combine(catalogRoot, "acme-cache");
            Directory.CreateDirectory(Path.Combine(psrDir, "_tyhpdef"));
            File.WriteAllText(Path.Combine(psrDir, "composer.json"), """
                {
                    "name": "tyhpdef/acme-cache",
                    "version": "1.0.0",
                    "require": { "acme/cache": "^3.0" },
                    "extra": { "tyhp": { "package": { "include": ["./_tyhpdef/*.tyhpdef"] } } }
                }
                """);
            File.WriteAllText(Path.Combine(psrDir, "_tyhpdef", "acme-cache.tyhpdef"), """
                <?tyhpdef
                namespace Psr\Acme {
                    interface WidgetPool {}
                }
                """);

            File.WriteAllText(Path.Combine(sourceDir, "Repo.php"), """
                <?php
                namespace Acme;
                class Repo {
                    /**
                     * @param DateTime $when
                     * @param WidgetPool $cache
                     */
                    public function store($when, $cache) {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
                CatalogRoots = [catalogRoot],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("\\DateTime $when");
            text.Should().Contain("\\Psr\\Acme\\WidgetPool $cache");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
            TryDelete(catalogRoot);
        }
    }

    [Fact]
    public void Generate_Source_QualifiesSameNamespaceExtendsImplementsAndTraitUse()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-fqcn-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-fqcn-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Command.php"), """
                <?php
                namespace AcmeBus\Command;
                interface CommandInterface {}
                interface PrefixableCommandInterface {}
                abstract class Command implements CommandInterface {}
                abstract class PrefixableCommand extends Command implements PrefixableCommandInterface {}
                abstract class ScriptCommand extends Command {}
                """);
            File.WriteAllText(Path.Combine(sourceDir, "Pipeline.php"), """
                <?php
                namespace AcmeBus\Pipeline;
                class Pipeline {}
                class Atomic extends Pipeline {}
                """);
            File.WriteAllText(Path.Combine(sourceDir, "Consumer.php"), """
                <?php
                namespace AcmeBus\Consumer\PubSub;
                class Consumer {}
                class RelayConsumer extends Consumer {}
                """);
            File.WriteAllText(Path.Combine(sourceDir, "AcmeTraits.php"), """
                <?php
                namespace Acme\Traits;
                trait Mixin {}
                trait Options {}
                trait Macro {
                    use Mixin;
                }
                trait Date {
                    use Options;
                }
                """);
            File.WriteAllText(Path.Combine(sourceDir, "Assert.php"), """
                <?php
                namespace Acme\Check;
                trait Mixin {}
                class Assert {
                    use Mixin;
                }
                class InvalidArgumentException extends \InvalidArgumentException {}
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("abstract class PrefixableCommand extends \\AcmeBus\\Command\\Command implements \\AcmeBus\\Command\\PrefixableCommandInterface");
            text.Should().Contain("abstract class ScriptCommand extends \\AcmeBus\\Command\\Command");
            text.Should().Contain("class Atomic extends \\AcmeBus\\Pipeline\\Pipeline");
            text.Should().Contain("class RelayConsumer extends \\AcmeBus\\Consumer\\PubSub\\Consumer");
            text.Should().Contain("trait Macro {\n        use \\Acme\\Traits\\Mixin;");
            text.Should().Contain("trait Date {\n        use \\Acme\\Traits\\Options;");
            text.Should().Contain("class Assert {\n        use \\Acme\\Check\\Mixin;");
            text.Should().Contain("class InvalidArgumentException extends \\InvalidArgumentException");
            text.Should().NotContain("extends \\Command");
            text.Should().NotContain("extends \\Pipeline");
            text.Should().NotContain("extends \\Consumer");
            text.Should().NotContain("use \\Mixin;");
            text.Should().NotContain("use \\Options;");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_QualifiesSamePackageMarkerOverUniquePhpGlobal()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-marker-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-marker-src-").FullName;
        var catalogRoot = Directory.CreateTempSubdirectory("tyhpdef-marker-cat-").FullName;
        try
        {
            var phpDir = Path.Combine(catalogRoot, "acme-stubs");
            Directory.CreateDirectory(Path.Combine(phpDir, "_tyhpdef"));
            File.WriteAllText(Path.Combine(phpDir, "composer.json"), """
                {
                    "name": "tyhpdef/acme-stubs",
                    "version": "1.0.0",
                    "require": { "php": ">=8.2" },
                    "extra": { "tyhp": { "package": { "include": ["./_tyhpdef/*.tyhpdef"] } } }
                }
                """);
            File.WriteAllText(Path.Combine(phpDir, "_tyhpdef", "php.tyhpdef"), """
                <?tyhpdef
                class Exception {}
                class RuntimeException {}
                class InvalidArgumentException {}
                """);

            File.WriteAllText(Path.Combine(sourceDir, "Exceptions.php"), """
                <?php
                namespace Acme\Exceptions;
                use RuntimeException as BaseRuntimeException;
                interface Exception {}
                interface RuntimeException extends Exception {}
                class Boom extends BaseRuntimeException implements RuntimeException {}
                class CacheMiss extends BaseRuntimeException implements Exception {}
                class WrapInvalid extends \InvalidArgumentException {}
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
                CatalogRoots = [catalogRoot],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("interface RuntimeException extends \\Acme\\Exceptions\\Exception");
            text.Should().Contain("class Boom extends \\RuntimeException implements \\Acme\\Exceptions\\RuntimeException");
            text.Should().Contain("class CacheMiss extends \\RuntimeException implements \\Acme\\Exceptions\\Exception");
            text.Should().Contain("class WrapInvalid extends \\InvalidArgumentException");
            text.Should().NotContain("interface RuntimeException extends \\Exception");
            text.Should().NotContain("implements \\RuntimeException");
            text.Should().NotContain("implements \\Exception");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
            TryDelete(catalogRoot);
        }
    }

    [Fact]
    public void Generate_Source_FillsGenericConstraintTypeArguments()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-gencon-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-gencon-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Mapping.php"), """
                <?php
                namespace Acme\Mapping;
                /**
                 * @template T of object
                 */
                interface ClassMetadata {}
                /**
                 * @template T of ClassMetadata
                 */
                interface ClassMetadataFactory {}
                /**
                 * @template U
                 */
                interface Box {}
                /**
                 * @template T of Box
                 */
                interface BoxHolder {}
                """);
            File.WriteAllText(Path.Combine(sourceDir, "Event.php"), """
                <?php
                namespace Acme\Event;
                use Acme\Mapping\ClassMetadata;
                use Acme\Mapping\ObjectManager;
                /**
                 * @template TClassMetadata of ClassMetadata<object>
                 * @template TObjectManager of ObjectManager
                 */
                class LoadClassMetadataEventArgs {}
                """);
            File.WriteAllText(Path.Combine(sourceDir, "ObjectManager.php"), """
                <?php
                namespace Acme\Mapping;
                interface ObjectManager {}
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("interface ClassMetadataFactory<T extends \\Acme\\Mapping\\ClassMetadata<object>>");
            text.Should().Contain("interface BoxHolder<T extends \\Acme\\Mapping\\Box<mixed>>");
            text.Should().Contain("class LoadClassMetadataEventArgs<TClassMetadata extends \\Acme\\Mapping\\ClassMetadata<object>, TObjectManager extends \\Acme\\Mapping\\ObjectManager>");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_QualifiesSameNamespaceGenericTemplateBound()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-doccon-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-doccon-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "ClassMetadata.php"), """
                <?php
                namespace Acme\Mapping;
                /**
                 * @template T of object
                 */
                interface ClassMetadata {}
                /**
                 * @template T of ClassMetadata<object>
                 */
                interface ClassMetadataFactory {}
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("interface ClassMetadataFactory<T extends \\Acme\\Mapping\\ClassMetadata<object>>");
            text.Should().NotContain("T extends ClassMetadata<object>");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_DropsUnbalancedDefaults()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-paren-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-paren-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Generator.php"), """
                <?php
                namespace Acme;
                class Generator {
                    /**
                     * @param callable(string $name): mixed $formatter
                     */
                    public function format($formatter = null) {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("function format(");
            text.Should().NotContain("callable(");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_RewritesPsalmTypeListAlias()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-list-alias-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-list-alias-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "ErrorBag.php"), """
                <?php
                namespace Acme\Schema;
                /**
                 * @psalm-type ErrorBagErrorList = list<\Error>
                 * @phpstan-type TableHash = array<int, list<string>>
                 */
                class ErrorBag {
                    /**
                     * @return ErrorBagErrorList
                     */
                    public function all() {
                        return [];
                    }
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("type ErrorBagErrorList = array<int, \\Error>;");
            text.Should().Contain("type ErrorBagTableHash = array<int, array<int, string>>;");
            text.Should().NotContain("list<");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_DropsUnquotedAndMultilineDefaults()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-defaults-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-defaults-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Str.php"), """
                <?php
                namespace Acme\Util;
                class Str {
                    const UNVIS_RX = <<<'RX'
                /
                \\(?:
                    ((?:040)|s)
                )
                /xS
                RX;
                    const WORD = <<<'TXT'
                hi
                TXT;
                    const TAG = <<<TXT
                hi
                TXT;
                    const VERSION = '1.0.0';
                    const FLAG = 15;
                    public static $baseText = <<<'TXT'
                hello `world` ●
                TXT;
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("const string VERSION ?? '1.0.0'");
            text.Should().Contain("const int FLAG ?? 15");
            text.Should().Contain("const string UNVIS_RX;");
            text.Should().Contain("const string WORD;");
            text.Should().Contain("const string TAG;");
            text.Should().NotContain("const mixed UNVIS_RX");
            text.Should().NotContain("UNVIS_RX ??");
            text.Should().NotContain("WORD ??");
            text.Should().NotContain("TAG ??");
            text.Should().NotContain("$baseText ?? hello");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_ExcludesInternalTypeAndDropsPublicReferences()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-internal-omit-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-internal-omit-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Model.php"), """
                <?php
                namespace Acme;
                /**
                 * @internal
                 */
                class ModelInfo {
                    public string $name;
                }
                class UsesInfo extends ModelInfo {
                    public function label(): string { return 'x'; }
                }
                class Inspector {
                    public function inspect(): ModelInfo {}
                    public function ok(): int { return 1; }
                    public ModelInfo $current;
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().NotContain("class ModelInfo");
            text.Should().NotContain("class UsesInfo");
            text.Should().Contain("class Inspector");
            text.Should().Contain("function ok");
            text.Should().NotContain("function inspect");
            text.Should().NotContain("$current");
            result.Warnings.Should().Contain(w =>
                w.Contains("UsesInfo", StringComparison.Ordinal)
                && w.Contains("ModelInfo", StringComparison.Ordinal));
            result.Warnings.Should().Contain(w =>
                w.Contains("inspect", StringComparison.Ordinal)
                && w.Contains("ModelInfo", StringComparison.Ordinal));
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_IncludeInternalKeepsTypeAndReferences()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-internal-keep-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-internal-keep-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Model.php"), """
                <?php
                namespace Acme;
                /**
                 * @internal
                 */
                class ModelInfo {
                    public string $name;
                }
                class Inspector {
                    public function inspect(): ModelInfo {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
                IncludeInternal = true,
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("class ModelInfo");
            text.Should().Contain("function inspect");
            text.Should().Contain("\\Acme\\ModelInfo");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_KeepsOptionalParameterWhenDefaultIsNotLiteral()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-opt-param-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-opt-param-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Defaults.php"), """
                <?php
                namespace Acme;
                class Defaults {
                    const A = 1;
                    const B = 2;
                    public static function concat($required, $suffix = 'a'.'b') {}
                    public static function heredoc($required, $body = <<<'TXT'
                hi
                TXT) {}
                    public static function stdin($required, $stream = \STDIN) {}
                    public static function constructed($required, $obj = new \stdClass()) {}
                    public static function flags($required, $mask = self::A | self::B) {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("const int A ?? 1");
            text.Should().Contain("const int B ?? 2");
            text.Should().Contain("$suffix = null");
            text.Should().Contain("$body = null");
            text.Should().Contain("$stream = null");
            text.Should().Contain("$obj = null");
            text.Should().Contain("$mask = null");
            text.Should().NotContain("'a'.'b'");
            text.Should().NotContain("$body = <<<");
            text.Should().NotContain("$stream = \\STDIN");
            text.Should().NotContain("$obj = new");
            text.Should().NotContain("self::A | self::B");
            AssertParses(text);

            var checkedCall = IsolatedCompilation.ParseSnippet(
                """
                <?tyhp
                function demo(): void {
                    \Acme\Defaults::concat('ok');
                    \Acme\Defaults::heredoc('ok');
                    \Acme\Defaults::stdin('ok');
                    \Acme\Defaults::constructed('ok');
                    \Acme\Defaults::flags('ok');
                }
                """,
                tyhpdef: text);
            checkedCall.Diagnostics.Errors.Should().NotContain(
                d => d.Code == MessageCode.CheckerMissingArgument,
                string.Join("; ", checkedCall.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_TypedUnsafeDefault_StaysOptionalWithoutWidening()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-typed-opt-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-typed-opt-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "Defaults.php"), """
                <?php
                namespace Acme;
                class TypedDefaults {
                    public static function concat(string $required, string $suffix = 'a'.'b') {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            text.Should().Contain("string $suffix = null");
            text.Should().NotContain("?string $suffix");
            text.Should().NotContain("string|null $suffix");
            text.Should().NotContain("'a'.'b'");
            AssertParses(text);

            var omittedOptional = IsolatedCompilation.ParseSnippet(
                """
                <?tyhp
                function demo(): void {
                    \Acme\TypedDefaults::concat('ok');
                }
                """,
                tyhpdef: text);
            omittedOptional.Diagnostics.Errors.Should().NotContain(
                d => d.Code == MessageCode.CheckerMissingArgument,
                string.Join("; ", omittedOptional.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

            var wrongType = IsolatedCompilation.ParseSnippet(
                """
                <?tyhp
                function demo(): void {
                    \Acme\TypedDefaults::concat('ok', 1);
                }
                """,
                tyhpdef: text);
            wrongType.Diagnostics.Errors.Should().Contain(
                d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
                string.Join("; ", wrongType.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));

            var nullArg = IsolatedCompilation.ParseSnippet(
                """
                <?tyhp
                function demo(): void {
                    \Acme\TypedDefaults::concat('ok', null);
                }
                """,
                tyhpdef: text);
            nullArg.Diagnostics.Errors.Should().Contain(
                d => d.Code == MessageCode.CheckerIncompatibleArgumentType,
                string.Join("; ", nullArg.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
            nullArg.Diagnostics.Errors.Should().NotContain(
                d => d.Code == MessageCode.CheckerMissingArgument,
                string.Join("; ", nullArg.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_SkipsIdenticalDuplicateFqcn()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-dup-ident-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-dup-ident-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "a.php"), """
                <?php
                namespace Acme;
                class Widget {
                    public function ping(): string {}
                }
                """);
            File.WriteAllText(Path.Combine(sourceDir, "b.php"), """
                <?php
                namespace Acme;
                /**
                 * Later identical OpenAPI copy.
                 */
                class Widget {
                    public function ping(): string {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            CountDecl(text, "class Widget").Should().Be(1);
            result.Warnings.Should().Contain(w =>
                w.Contains("\\Acme\\Widget", StringComparison.Ordinal));
            AssertParses(text);

            var bound = IsolatedCompilation.ParseSnippet(
                """
                <?tyhp
                function demo(): string {
                    return (new \Acme\Widget())->ping();
                }
                """,
                tyhpdef: text);
            bound.Diagnostics.Errors.Should().NotContain(
                d => d.Code == MessageCode.TyhpdefDuplicateDeclaration,
                string.Join("; ", bound.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void Generate_Source_KeepsDivergentDuplicateFqcnFor8002()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-dup-diff-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-dup-diff-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "a.php"), """
                <?php
                namespace Acme;
                class Widget {
                    public function ping(): string {}
                }
                """);
            File.WriteAllText(Path.Combine(sourceDir, "b.php"), """
                <?php
                namespace Acme;
                class Widget {
                    public function ping(): string {}
                    public function pong(): int {}
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles[0]);
            CountDecl(text, "class Widget").Should().Be(2);
            result.Warnings.Should().NotContain(w =>
                w.Contains("\\Acme\\Widget", StringComparison.Ordinal)
                && w.Contains("identical", StringComparison.OrdinalIgnoreCase));
            AssertParses(text);

            var bound = IsolatedCompilation.ParseSnippet(
                """
                <?tyhp
                function demo(): void {}
                """,
                tyhpdef: text);
            bound.Diagnostics.Errors.Should().Contain(
                d => d.Code == MessageCode.TyhpdefDuplicateDeclaration,
                string.Join("; ", bound.Diagnostics.Errors.Select(e => $"{e.Code}: {e.Message}")));
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    [Trait("Category", "Story27")]
    public void Generate_ReturnNewClass_WritesShapeAliasAndReturnType()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-anon-return-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-anon-return-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "clock.php"), """
                <?php
                function createClock() {
                    return new class {
                        public function foo(): void {}
                        private function hidden(): void {}
                    };
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "clock.php")],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().NotContain("anonClass@");
            text.Should().NotContain("class anon");
            text.Should().Contain("type createClock_Return = object {");
            text.Should().Contain("public function foo(): void;");
            text.Should().NotContain("function hidden");
            text.Should().Contain("function createClock():");
            text.Should().Contain("createClock_Return");
            AssertParses(text);

            var checkedTyhp = IsolatedCompilation.ParseSnippet(
                """
                <?tyhp
                function demo(): void {
                    __FunctionReturnType<'createClock'> $c;
                    $c->foo();
                }
                """,
                text);
            checkedTyhp.Diagnostics.Errors.Should().BeEmpty(
                string.Join("; ", checkedTyhp.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
                + "\n--- harvested ---\n" + text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    [Trait("Category", "Story27")]
    public void Generate_TwoAnonymousReturnsSameFunction_CollisionSuffixed()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-anon-collision-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-anon-collision-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "clock.php"), """
                <?php
                function pick($flag) {
                    if ($flag) {
                        return new class {
                            public function foo(): void {}
                        };
                    }
                    return new class {
                        public function bar(): void {}
                    };
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "clock.php")],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("type pick_Return = object {");
            text.Should().Contain("type pick_Return_2 = object {");
            text.Should().Contain("public function foo(): void;");
            text.Should().Contain("public function bar(): void;");
            text.Should().Contain("pick_Return_2");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    [Trait("Category", "Story27")]
    public void Generate_AnonymousClassParentOnly_ContributesParentWithoutUnusedAlias()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-anon-parentonly-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-anon-parentonly-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "clock.php"), """
                <?php
                interface Named {}
                function createNamed() {
                    return new class implements Named {
                    };
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "clock.php")],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().NotContain("createNamed_Return");
            text.Should().Contain("function createNamed(): \\Named;");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    [Trait("Category", "Story27")]
    public void Generate_AnonymousClassWithConstructor_HarvestsConstructSignature()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-anon-ctor-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-anon-ctor-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "named.php"), """
                <?php
                function createNamed($name) {
                    return new class($name) {
                        public function __construct(public string $name) {}
                        public function ping(): void {}
                    };
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "named.php")],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("type createNamed_Return = object {");
            text.Should().Contain("public function __construct(string $name): void;");
            text.Should().Contain("public function ping(): void;");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    [Trait("Category", "Story27")]
    public void Generate_AnonymousReturnMixedWithUnrepresentableOtherReturn_DoesNotNarrow()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-anon-null-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-anon-null-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "maybe.php"), """
                <?php
                function maybeClock($flag) {
                    if ($flag) {
                        return null;
                    }
                    return new class {
                        public function now(): int { return 1; }
                    };
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "maybe.php")],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);

            // The `return null;` branch cannot be spelled as a named/anonymous arm.
            // Narrowing to just the harvested shape would silently drop that branch
            // and make the tyhpdef signature unsound, so the harvest must decline
            // and leave the original (weak) return type untouched.
            text.Should().NotContain("maybeClock_Return");
            text.Should().NotContain("type maybeClock_Return");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    [Trait("Category", "Story27")]
    public void Generate_ParseConstraintStyleUnion_NamedClassAndAnonymousClass()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-anon-union-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-anon-union-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "plugin.php"), """
                <?php
                namespace Acme;
                class Constraint {}
                interface Named {}
                class Plugin {
                    public function parseConstraint(string $pretty) {
                        if ($pretty === 'x') {
                            return new Constraint();
                        }
                        return new class implements Named {
                            public function getPrettyString(): string { return ''; }
                            public function __toString(): string { return ''; }
                        };
                    }
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "plugin.php")],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);

            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("type Plugin_parseConstraint_Return = ");
            text.Should().Contain("Named & object {");
            text.Should().Contain("public function getPrettyString(): string;");
            text.Should().Contain("function parseConstraint(string $pretty):");
            text.Should().Contain("Plugin_parseConstraint_Return");
            text.Should().Contain("Constraint");
            text.Should().NotContain("anonClass@");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    [Fact]
    public void FormatDeclaredTypeName_KeywordUsesEnclosingNamespace()
    {
        TyhpdefOutputWriter.FormatDeclaredTypeName("Is", "Acme\\Probe")
            .Should().Be("\\Acme\\Probe\\Is");
        TyhpdefOutputWriter.FormatDeclaredTypeName("isset", "Acme\\Probe")
            .Should().Be("\\Acme\\Probe\\isset");
        TyhpdefOutputWriter.FormatDeclaredTypeName("Widget", "Acme")
            .Should().Be("Widget");
    }

    [Fact]
    public void Generate_FunctionExistsGate_EmitsFallbackFunction()
    {
        var outputDir = Directory.CreateTempSubdirectory("tyhpdef-fallback-out-").FullName;
        var sourceDir = Directory.CreateTempSubdirectory("tyhpdef-fallback-src-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(sourceDir, "helpers.php"), """
                <?php
                if (!function_exists('collect')) {
                    function collect($value = null): mixed {
                        return $value;
                    }
                }

                function plain(): void {
                }
                """);
            var options = BaseOptions(outputDir) with
            {
                Mode = TyhpdefGenerationMode.PhpSourceFiles,
                SourcePaths = [Path.Combine(sourceDir, "*.php")],
                IncludeDocComments = false,
            };
            var result = Run(options, sourceDir);
            result.Diagnostics.HasErrors.Should().BeFalse(
                string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)));
            var text = File.ReadAllText(result.GeneratedFiles.Should().ContainSingle().Subject);
            text.Should().Contain("fallback function collect(");
            text.Should().Contain("function plain(");
            text.Should().NotContain("fallback function plain");
            AssertParses(text);
        }
        finally
        {
            TryDelete(outputDir);
            TryDelete(sourceDir);
        }
    }

    private static TyhpdefGenerationOptions BaseOptions(string outputDir)
        => new()
        {
            OutputDirectory = outputDir,
            IncludeDocComments = true,
            IncludeDeprecated = true,
            Overwrite = true,
            Split = "file",
            FetchStubCache = false,
            StubCacheDirectory = Path.Combine(outputDir, "missing-stubs"),
            CatalogRoots = [],
        };

    private static TyhpdefGenerationResult Run(TyhpdefGenerationOptions options, string searchRoot)
    {
        var result = new TyhpdefGenerationResult();
        new NativeTyhpdefGenerator().Generate(options, result, searchRoot);
        return result;
    }

    private static void AssertParses(string tyhpdef)
    {
        var parsed = ParserTestHelper.ParseTyhpdefContent(tyhpdef);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => e.Message)) + "\n" + tyhpdef);
    }

    private static int CountDecl(string text, string needle)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var i = text.IndexOf(needle, start, StringComparison.Ordinal);
            if (i < 0)
            {
                return count;
            }

            count++;
            start = i + needle.Length;
        }
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
