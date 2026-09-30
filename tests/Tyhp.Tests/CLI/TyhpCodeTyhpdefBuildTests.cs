using Tyhp.Domain.Exceptions;
using Tyhp.Tests.Binder;
using Tyhp.Tests.TestHelpers;
using Tyhp.TyhpLang.Binder.Scopes;
using Tyhp.TyhpLang.Binder.Scopes.Interfaces;
using Tyhp.TyhpLang.Binder.Symbols;

namespace Tyhp.Tests.CLI;

[Trait("Category", "Build")]
[Trait("Category", "Story20")]
public class TyhpCodeTyhpdefBuildTests
{
    [Fact]
    public void Library_AlwaysEmitsPackageTyhpdefAndJson()
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
            .WithTyhpFile("Box.tyhp", """
                <?tyhp
                namespace Lib;
                class Box {
                    public function get(): int {
                        return 1;
                    }
                    private function secret(): void {}
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var defPath = Path.Combine(project.ProjectDirectory, "package.tyhpdef");
        var composerPath = Path.Combine(project.ProjectDirectory, "composer.json");
        File.Exists(defPath).Should().BeTrue();
        File.Exists(composerPath).Should().BeTrue();
        File.Exists(Path.Combine(project.ProjectDirectory, "package.tyhp.json")).Should().BeFalse();

        var def = File.ReadAllText(defPath);
        def.Should().Contain("namespace Lib");
        def.Should().Contain("class Box");
        def.Should().Contain("function get");
        def.Should().NotContain("secret");
        def.Should().NotContain("use extension");
        def.Should().NotContain("global use extension");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def, defPath);
        parsed.Diagnostics.Errors.Should().BeEmpty();

        var json = File.ReadAllText(composerPath);
        json.Should().Contain("./package.tyhpdef");
        json.Should().Contain("\"tagless\": false");
        json.Should().Contain("\"package\"");

        File.Exists(Path.Combine(project.ProjectDirectory, "build", "package.tyhpdef")).Should().BeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "build", "package.tyhp.json")).Should().BeFalse();
        Directory.GetFiles(Path.Combine(project.ProjectDirectory, "build"), "*.php", SearchOption.AllDirectories)
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Application_WithFlag_EmitsDefOnly()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "application",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" },
                    "build": { "generateTyhpdef": true }
                }
                """)
            .WithTyhpFile("App.tyhp", """
                <?tyhp
                namespace App;
                class Example {
                    public function ping(): int {
                        return 1;
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        File.Exists(Path.Combine(project.ProjectDirectory, "package.tyhpdef")).Should().BeTrue();
        File.Exists(Path.Combine(project.ProjectDirectory, "package.tyhp.json")).Should().BeFalse();
        var composerPath = Path.Combine(project.ProjectDirectory, "composer.json");
        if (File.Exists(composerPath))
        {
            File.ReadAllText(composerPath).Should().NotContain("\"package\"");
        }
    }

    [Fact]
    public void Application_WithoutFlag_EmitsNothing()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "application",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("App.tyhp", """
                <?tyhp
                namespace App;
                class Example {}
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty();
        File.Exists(Path.Combine(project.ProjectDirectory, "package.tyhpdef")).Should().BeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "package.tyhp.json")).Should().BeFalse();
    }

    [Fact]
    public void LibraryEntrypoint_Reports7505_AndSkipsArtifacts()
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
            .WithTyhpFile("boot.tyhp", """
                <?tyhp
                $x = 1;
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().Contain(d => d.Code == MessageCode.TyhpdefLibraryEntrypointDetected);
        File.Exists(Path.Combine(project.ProjectDirectory, "package.tyhpdef")).Should().BeFalse();
    }

    [Fact]
    public void StandaloneExtension_EmitsBackerClassAndFnMappings()
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
            .WithTyhpFile("ops.tyhp", """
                <?tyhp
                namespace Lib;
                extension StringOps extends string {
                    function toUpper(): string {
                        return $this;
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("StringOps__tyhpExtensionBacker");
        def.Should().Contain("extension StringOps extends string");
        def.Should().Contain("fn toUpper(): string => $this;");
        def.Should().Contain("public static function toUpper(string $this_): string;");
        def.Should().Contain("=> $this;");
        def.Should().NotContain("StringOps__tyhpExtensionBacker::toUpper");
        def.Should().NotContain("use extension");
        def.Should().NotContain("#[\\Tyhp\\Optimize\\Inline]");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Library_GlobalUseExtensionAndDeclarePhp_DoesNotReport7505()
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
            .WithTyhpFile("ops.tyhp", """
                <?tyhp
                namespace Lib;
                global use extension \Lib\Ops;
                global use extension \Lib\OpsVersioned;
                extension Ops extends string {
                    function ident(): string {
                        return $this;
                    }
                }
                declare(php=">=8.2") {
                    extension OpsVersioned extends string {
                        fn padded(): string => $this;
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        result.Diagnostics.Errors.Should().NotContain(d => d.Code == MessageCode.TyhpdefLibraryEntrypointDetected);
    }

    [Fact]
    public void ClassBodyOperator_EmitsThinExtensionOperatorWithoutInlineAttribute()
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
        def.Should().NotContain("extension MoneyOps");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story20.7")]
    public void Library_HookedGetSetProperty_EmitsBodylessHookList()
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
            .WithTyhpFile("Holder.tyhp", """
                <?tyhp
                namespace Lib;
                final class Holder {
                    private string $_name = 'default';
                    public string $name {
                        get {
                            return $this->_name;
                        }
                        set(string $value) {
                            $this->_name = $value;
                        }
                    }
                    public int $count = 0;
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("public string $name { get; set; }");
        def.Should().Contain("public int $count ?? 0;");
        def.Should().NotContain("return $this->_name");
        def.Should().NotContain("$_name");
        def.Should().NotContain(@"#[\Tyhp\Php");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story20.7")]
    public void Library_ByRefGetHook_EmitsAmpersandGet()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/", "phpVersion": "8.4" }
                }
                """)
            .WithTyhpFile("Holder.tyhp", """
                <?tyhp
                namespace Lib;
                final class Holder {
                    private array $_items = [];
                    public array $items {
                        &get {
                            return $this->_items;
                        }
                    }
                    public int $count = 0;
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("public array $items { &get; }");
        def.Should().Contain("public int $count ?? 0;");
        def.Should().NotContain("$_items");
        def.Should().NotContain("return $this->_items");
        def.Should().NotContain(@"#[\Tyhp\Php("">=8.4"")]");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story20.7")]
    public void Library_HookedProperty_ConsumerBindSeesAccessorFlags()
    {
        using var library = new TestProjectBuilder();
        library
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/", "phpVersion": "8.4" }
                }
                """)
            .WithTyhpFile("Holder.tyhp", """
                <?tyhp
                namespace Lib;
                final class Holder {
                    private string $_name = 'default';
                    public string $name {
                        get {
                            return $this->_name;
                        }
                        set(string $value) {
                            $this->_name = $value;
                        }
                    }
                    private array $_items = [];
                    public array $items {
                        &get {
                            return $this->_items;
                        }
                    }
                    public int $count = 0;
                }
                """);

        var build = library.RunBuild();
        build.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", build.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var defPath = Path.Combine(library.ProjectDirectory, "package.tyhpdef");
        var def = File.ReadAllText(defPath);
        def.Should().Contain("public string $name { get; set; }");
        def.Should().Contain("public array $items { &get; }");
        def.Should().Contain("public int $count ?? 0;");

        var (global, diagnostics) = BinderTestHelper.BindContent(def, fileName: "package.tyhpdef");
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        global.Should().NotBeNull();

        var holder = FindObject(global!, "Holder");
        var name = FindProperty(holder, "name");
        name.HasAccessor.Should().BeTrue();
        name.HasGetHook.Should().BeTrue();
        name.HasSetHook.Should().BeTrue();
        name.GetHookReturnsRef.Should().BeFalse();

        var items = FindProperty(holder, "items");
        items.HasAccessor.Should().BeTrue();
        items.HasGetHook.Should().BeTrue();
        items.HasSetHook.Should().BeFalse();
        items.GetHookReturnsRef.Should().BeTrue();

        var count = FindProperty(holder, "count");
        count.HasAccessor.Should().BeFalse();
        count.HasGetHook.Should().BeFalse();
        count.HasSetHook.Should().BeFalse();
        count.GetHookReturnsRef.Should().BeFalse();

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
                namespace App;
                function take(\Lib\Holder $h): string {
                    return $h->name;
                }
                """);

        var consumed = consumer.RunBuild();
        consumed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", consumed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        consumed.GlobalScope.Should().NotBeNull(
            "the consumer build should bind successfully against the generated package.tyhpdef");

        var consumedHolder = FindObject(consumed.GlobalScope!, "Holder");
        var consumedName = FindProperty(consumedHolder, "name");
        consumedName.HasAccessor.Should().BeTrue();
        consumedName.HasGetHook.Should().BeTrue();
        consumedName.HasSetHook.Should().BeTrue();
        consumedName.GetHookReturnsRef.Should().BeFalse();

        var consumedItems = FindProperty(consumedHolder, "items");
        consumedItems.HasAccessor.Should().BeTrue();
        consumedItems.HasGetHook.Should().BeTrue();
        consumedItems.HasSetHook.Should().BeFalse();
        consumedItems.GetHookReturnsRef.Should().BeTrue();

        var consumedCount = FindProperty(consumedHolder, "count");
        consumedCount.HasAccessor.Should().BeFalse();
        consumedCount.HasGetHook.Should().BeFalse();
        consumedCount.HasSetHook.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Story20.7")]
    public void Library_HookVisibilityAndFinal_CopiedWithoutBodies()
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
            .WithTyhpFile("Holder.tyhp", """
                <?tyhp
                namespace Lib;
                class Widget {
                    private string $_name = 'x';
                    public string $name {
                        final get {
                            return $this->_name;
                        }
                        private set(string $value) {
                            $this->_name = $value;
                        }
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("public string $name { final get; private set; }");
        def.Should().NotContain("return $this->_name");
        def.Should().NotContain(@"#[\Tyhp\Php");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Story20.7")]
    public void Library_HookAttributes_CopiedButTyhpPhpGateSkipped()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/", "phpVersion": "8.4" }
                }
                """)
            .WithTyhpFile("LogAccess.tyhp", """
                <?tyhp
                namespace Lib;
                #[\Attribute]
                class LogAccess {}
                """)
            .WithTyhpFile("Holder.tyhp", """
                <?tyhp
                namespace Lib;
                final class Holder {
                    private string $_name = 'default';
                    public string $name {
                        #[LogAccess]
                        #[\Tyhp\Php(">=8.4")]
                        get {
                            return $this->_name;
                        }
                        set(string $value) {
                            $this->_name = $value;
                        }
                    }
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("#[\\Lib\\LogAccess]");
        def.Should().NotContain(@"#[\Tyhp\Php");
        def.Should().Contain("get; set; }");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
    }

    [Fact]
    [Trait("Category", "Story20.7")]
    public void Library_PromotedCtorHookedProperty_EmitsClassBodyHookedForm()
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
            .WithTyhpFile("Holder.tyhp", """
                <?tyhp
                namespace Lib;
                class Holder {
                    public function __construct(
                        public string $name {
                            get {
                                return $this->name;
                            }
                            set(string $value) {
                                $this->name = $value;
                            }
                        }
                    ) {}
                }
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var def = File.ReadAllText(Path.Combine(project.ProjectDirectory, "package.tyhpdef"));
        def.Should().Contain("public string $name { get; set; }");
        def.Should().NotContain("return $this->name");

        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty();

        var (global, diagnostics) = BinderTestHelper.BindContent(def, fileName: "package.tyhpdef");
        diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        var holder = FindObject(global!, "Holder");
        var name = FindProperty(holder, "name");
        name.HasAccessor.Should().BeTrue();
        name.HasGetHook.Should().BeTrue();
        name.HasSetHook.Should().BeTrue();
    }

    [Fact]
    public void Library_HonorsOutputPublishPath()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/", "publishPath": "artifacts/" }
                }
                """)
            .WithTyhpFile("Box.tyhp", """
                <?tyhp
                namespace Lib;
                class Box {}
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        File.Exists(Path.Combine(project.ProjectDirectory, "artifacts", "package.tyhpdef")).Should().BeTrue();
        File.Exists(Path.Combine(project.ProjectDirectory, "artifacts", "composer.json")).Should().BeTrue();
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "artifacts", "composer.json"))
            .Should().Contain("\"package\"");
        File.Exists(Path.Combine(project.ProjectDirectory, "artifacts", "package.tyhp.json")).Should().BeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "package.tyhpdef")).Should().BeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "build", "package.tyhpdef")).Should().BeFalse();
        Directory.GetFiles(Path.Combine(project.ProjectDirectory, "build"), "*.php", SearchOption.AllDirectories)
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Library_UpdateComposer_WritesComposerJsonToPublishPath()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "publish/src", "publishPath": "publish" },
                    "build": { "updateComposer": true }
                }
                """)
            .WithTyhpFile("Box.tyhp", """
                <?tyhp
                namespace Lib;
                class Box {}
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var composerPath = Path.Combine(project.ProjectDirectory, "publish", "composer.json");
        File.Exists(composerPath).Should().BeTrue();
        File.Exists(Path.Combine(project.ProjectDirectory, "publish", "src", "composer.json")).Should().BeFalse();

        var json = File.ReadAllText(composerPath);
        json.Should().Contain("autoload");
        json.Should().Contain("src/");
    }

    [Fact]
    public void Application_UpdateComposer_WritesComposerJsonToProjectRoot()
    {
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "application",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "src/" },
                    "build": { "updateComposer": true }
                }
                """)
            .WithTyhpFile("Example.tyhp", """
                <?tyhp
                namespace App;
                class Example {}
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        File.Exists(Path.Combine(project.ProjectDirectory, "composer.json")).Should().BeTrue();
        File.Exists(Path.Combine(project.ProjectDirectory, "src", "composer.json")).Should().BeFalse();
    }

    [Fact]
    public void Library_MergesExistingComposerPackageWithoutClobbering()
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
            .WithTyhpFile("composer.json", """
                {
                    "name": "acme/lib",
                    "require-dev": {
                        "tyhpdef/acme-stubs": "@dev"
                    },
                    "extra": {
                        "class": "Acme\\Plugin",
                        "tyhp": {
                            "interopContractVersion": 1,
                            "require": {
                                "tyhpdef/acme-stubs": "@dev"
                            },
                            "package": {
                                "include": [
                                    "./_tyhpdef/*.tyhpdef",
                                    "./_tyhpdef/extensions/*.tyhpdef"
                                ],
                                "overlay": [
                                    "./_tyhpdef/overlays/stubs/*.tyhpdef"
                                ],
                                "customKey": "keep-me"
                            }
                        }
                    }
                }
                """)
            .WithTyhpFile("Box.tyhp", """
                <?tyhp
                namespace Lib;
                class Box {}
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));

        var json = File.ReadAllText(Path.Combine(project.ProjectDirectory, "composer.json"));
        json.Should().Contain("./_tyhpdef/*.tyhpdef");
        json.Should().Contain("./_tyhpdef/extensions/*.tyhpdef");
        json.Should().Contain("./package.tyhpdef");
        json.Should().Contain("./_tyhpdef/overlays/stubs/*.tyhpdef");
        json.Should().Contain("\"customKey\": \"keep-me\"");
        json.Should().Contain("\"exclude\"");
        json.Should().Contain("\"tagless\"");
        json.Should().Contain("\"class\"");
        json.Should().Contain("\"interopContractVersion\"");
        json.Should().Contain("\"tyhpdef/acme-stubs\"");
        json.Should().NotContain("\"overlay\": []");
        File.Exists(Path.Combine(project.ProjectDirectory, "package.tyhp.json")).Should().BeFalse();
    }

    [Fact]
    public void Library_LeavesInvalidComposerJsonUnchanged()
    {
        const string invalidJson = "{ this is not json";
        using var project = new TestProjectBuilder();
        project
            .WithTyhpJson("""
                {
                    "type": "library",
                    "include": ["**/*.tyhp"],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("composer.json", invalidJson)
            .WithTyhpFile("Box.tyhp", """
                <?tyhp
                namespace Lib;
                class Box {}
                """);

        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().Contain(d =>
            d.Code == MessageCode.ComposerRootJsonWriteFailed
            || d.Code == MessageCode.TyhpdefInvalidFormat);
        File.ReadAllText(Path.Combine(project.ProjectDirectory, "composer.json")).Should().Be(invalidJson);
    }

    private static ObjectDeclarationSymbol FindObject(GlobalScope global, string name)
    {
        ObjectDeclarationSymbol? found = null;
        void Walk(IBaseScope scope)
        {
            if (found != null)
            {
                return;
            }

            foreach (var symbol in scope.GetAllChildSymbols())
            {
                if (symbol is ObjectDeclarationSymbol obj
                    && string.Equals(obj.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = obj;
                    return;
                }
            }

            foreach (var child in scope.GetAllChildScopes())
            {
                Walk(child);
            }
        }

        Walk(global);
        found.Should().NotBeNull($"expected to find class '{name}'");
        return found!;
    }

    private static ObjectPropertySymbol FindProperty(ObjectDeclarationSymbol type, string name)
    {
        foreach (var member in type.EnumerateMembersAndConstants())
        {
            if (member is ObjectPropertySymbol property
                && string.Equals(
                    property.Name.TrimStart('$'),
                    name.TrimStart('$'),
                    StringComparison.Ordinal))
            {
                return property;
            }
        }

        throw new InvalidOperationException($"Property '{name}' was not bound on {type.Name}.");
    }
}
