using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
[Trait("Category", "Story21.10")]
public class TyhpCodeTyhpdefExternTests
{
    [Fact]
    public void Library_AmbientOwner_SpellsFqnWithoutExtern()
    {
        using var project = new TestProjectBuilder();
        ConfigureLibrary(
            project,
            require: """{ "php": ">=8.2" }""",
            requireDev: """{ "tyhpdef/ambient": "@dev" }""",
            extras: """{ "tyhpdef/ambient": "@dev" }""",
            tyhpdefIncludes: ["./owners/ambient/composer.json"]);
        AddOwnerPackage(
            project,
            "owners/ambient",
            "tyhpdef/ambient",
            """
            <?tyhpdef
            interface \Ambient\Flag {}
            """);
        project.WithTyhpFile("src/api.tyhp", """
            <?tyhp
            namespace Lib;
            class Box implements \Ambient\Flag {
            }
            """);

        var def = BuildDef(project);
        def.Should().Contain("implements \\Ambient\\Flag");
        def.Should().NotContain("extern ");
        def.Should().NotContain("@provided-by:");
        File.Exists(Path.Combine(project.ProjectDirectory, "externs.tyhpdef")).Should().BeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "_tyhpdef", TyhpdefOutputLayout.ExternsFileName))
            .Should().BeFalse();
    }

    [Fact]
    public void Library_RequireDevOnlyOwner_EmitsExternWithProvidedBy()
    {
        using var project = new TestProjectBuilder();
        ConfigureLibrary(
            project,
            require: """{ "php": ">=8.2" }""",
            requireDev: """{ "tyhpdef/ambient": "@dev", "tyhpdef/optional": "@dev" }""",
            extras: """{ "tyhpdef/ambient": "@dev" }""",
            tyhpdefIncludes:
            [
                "./owners/ambient/composer.json",
                "./owners/optional/composer.json",
            ]);
        AddOwnerPackage(
            project,
            "owners/ambient",
            "tyhpdef/ambient",
            """
            <?tyhpdef
            interface \Ambient\Flag {}
            """);
        AddOwnerPackage(
            project,
            "owners/optional",
            "tyhpdef/optional",
            """
            <?tyhpdef
            interface \Optional\Flag {}
            """);
        project.WithTyhpFile("src/api.tyhp", """
            <?tyhp
            namespace Lib;
            class Box implements \Ambient\Flag {
                public function take(\Optional\Flag $flag): \Optional\Flag {
                    return $flag;
                }
            }
            """);

        var def = BuildDef(project);
        def.Should().Contain("implements \\Ambient\\Flag");
        def.Should().NotContain("extern interface \\Ambient\\Flag");
        def.Should().Contain("// @provided-by: tyhpdef/optional");
        def.Should().Contain("extern interface \\Optional\\Flag;");
        File.Exists(Path.Combine(project.ProjectDirectory, "externs.tyhpdef")).Should().BeFalse();
        File.Exists(Path.Combine(project.ProjectDirectory, "_tyhpdef", TyhpdefOutputLayout.ExternsFileName))
            .Should().BeFalse();
    }

    [Fact]
    public void Library_RequiredCoreType_IsNotExternedAndNotCopied()
    {
        using var project = new TestProjectBuilder();
        ConfigureLibrary(
            project,
            require: """{ "php": ">=8.2", "acme/widget": "@dev" }""",
            requireDev: """{}""",
            extras: """{}""",
            tyhpdefIncludes: ["./owners/widget/composer.json"]);
        AddOwnerPackage(
            project,
            "owners/widget",
            "acme/widget",
            """
            <?tyhpdef
            namespace Acme {
                class CoreToken {}
            }
            """);
        project.WithTyhpFile("src/api.tyhp", """
            <?tyhp
            namespace Lib;
            class Box {
                public function token(): \Acme\CoreToken {
                    return new \Acme\CoreToken();
                }
            }
            """);

        var def = BuildDef(project);
        def.Should().Contain("function token(): \\Acme\\CoreToken");
        def.Should().NotContain("extern class \\Acme\\CoreToken");
        def.Should().NotContain("class CoreToken");
        def.Should().NotContain("@provided-by: acme/widget");
    }

    [Fact]
    public void Library_PublicApiNamingGlobalClassAndFunction_EmitsTrackCExtern()
    {
        using var project = new TestProjectBuilder();
        ConfigureLibrary(
            project,
            require: """{ "php": ">=8.2" }""",
            requireDev: """{ "tyhpdef/acme-int": "@dev", "tyhpdef/acme-math": "@dev" }""",
            extras: """{}""",
            tyhpdefIncludes:
            [
                "./owners/acme-int/composer.json",
                "./owners/acme-math/composer.json",
            ]);
        AddOwnerPackage(
            project,
            "owners/acme-int",
            "tyhpdef/acme-int",
            """
            <?tyhpdef
            class \WidgetNum {}
            """);
        AddOwnerPackage(
            project,
            "owners/acme-math",
            "tyhpdef/acme-math",
            """
            <?tyhpdef
            function widget_add(string $num1, string $num2, ?int $scale = null): string;
            """);
        project.WithTyhpFile("src/api.tyhp", """
            <?tyhp
            namespace Lib;
            class Holder {
                public function wrap(\WidgetNum $value): \WidgetNum {
                    return $value;
                }
            }

            extension Bc extends string {
                fn plus(string $other): string => \widget_add($this, $other);
            }
            """);

        var def = BuildDef(project);
        def.Should().Contain("extern class \\WidgetNum;");
        def.Should().Contain("// @provided-by: tyhpdef/acme-int");
        def.Should().Contain("extern function \\widget_add;");
        def.Should().Contain("// @provided-by: tyhpdef/acme-math");
        def.Should().Contain("function wrap(\\WidgetNum $value): \\WidgetNum");
        def.Should().Contain("\\widget_add($this, $other)");
        def.Should().NotContain("class WidgetNum {");
        def.Should().NotContain("function widget_add(");
        File.Exists(Path.Combine(project.ProjectDirectory, "_tyhpdef", TyhpdefOutputLayout.ExternsFileName))
            .Should().BeFalse();
    }

    [Fact]
    public void Library_UnknownOwner_LeavesNameAndDoesNotInventExtern()
    {
        using var project = new TestProjectBuilder();
        ConfigureLibrary(
            project,
            require: """{ "php": ">=8.2" }""",
            requireDev: """{}""",
            extras: """{}""",
            tyhpdefIncludes: ["./owners/ghost/composer.json"]);
        AddOwnerPackage(
            project,
            "owners/ghost",
            "tyhpdef/ghost",
            """
            <?tyhpdef
            class \Ghost\Origin {}
            """);
        project.WithTyhpFile("src/api.tyhp", """
            <?tyhp
            namespace Lib;
            class Box {
                public function ping(\Ghost\Origin $x): \Ghost\Origin {
                    return $x;
                }
            }
            """);

        var def = BuildDef(project);
        def.Should().Contain("function ping(\\Ghost\\Origin $x): \\Ghost\\Origin");
        def.Should().NotContain("extern class \\Ghost\\Origin");
        def.Should().NotContain("@provided-by: tyhpdef/ghost");
    }

    [Fact]
    public void Library_GenericConstraintNamesRequireDevOnlyOwner_EmitsExtern()
    {
        using var project = new TestProjectBuilder();
        ConfigureLibrary(
            project,
            require: """{ "php": ">=8.2" }""",
            requireDev: """{ "tyhpdef/acme-int": "@dev" }""",
            extras: """{}""",
            tyhpdefIncludes: ["./owners/acme-int/composer.json"]);
        AddOwnerPackage(
            project,
            "owners/acme-int",
            "tyhpdef/acme-int",
            """
            <?tyhpdef
            class \WidgetNum {}
            """);
        project.WithTyhpFile("src/api.tyhp", """
            <?tyhp
            namespace Lib;
            class Box<T extends \WidgetNum> {
                public function value(): int {
                    return 1;
                }
            }
            """);

        var def = BuildDef(project);
        def.Should().Contain("T extends \\WidgetNum");
        def.Should().Contain("extern class \\WidgetNum;");
        def.Should().Contain("// @provided-by: tyhpdef/acme-int");
    }

    private static string BuildDef(TestProjectBuilder project)
    {
        var result = project.RunBuild();
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}")));
        var defPath = Path.Combine(project.ProjectDirectory, "package.tyhpdef");
        File.Exists(defPath).Should().BeTrue();
        var def = File.ReadAllText(defPath);
        var parsed = ParserTestHelper.ParseTyhpdefContent(def);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => $"{e.Code} {e.Message}"))
            + "\n--- generated ---\n" + def);
        return def;
    }

    private static void ConfigureLibrary(
        TestProjectBuilder project,
        string require,
        string requireDev,
        string extras,
        IReadOnlyList<string> tyhpdefIncludes)
    {
        var includeJson = string.Join(", ", tyhpdefIncludes.Select(p => "\"" + p + "\""));
        project
            .WithTyhpJson($$"""
                {
                    "type": "library",
                    "include": ["src/**/*.tyhp"],
                    "tyhpdefInclude": [{{includeJson}}],
                    "output": { "path": "build/" }
                }
                """)
            .WithTyhpFile("composer.json", $$"""
                {
                    "name": "acme/lib",
                    "require": {{require}},
                    "require-dev": {{requireDev}},
                    "extra": {
                        "tyhp": {
                            "interopContractVersion": 1,
                            "require": {{extras}}
                        }
                    }
                }
                """);
    }

    private static void AddOwnerPackage(
        TestProjectBuilder project,
        string relativeDir,
        string packageName,
        string tyhpdef)
    {
        project.WithTyhpFile($"{relativeDir}/composer.json", $$"""
            {
                "name": "{{packageName}}",
                "require": { "php": ">=8.2" },
                "extra": {
                    "tyhp": {
                        "package": {
                            "include": ["./_tyhpdef/*.tyhpdef"]
                        }
                    }
                }
            }
            """);
        project.WithTyhpFile($"{relativeDir}/_tyhpdef/types.tyhpdef", tyhpdef);
    }
}
