using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class PhpReflectionMapperTests
{
    [Fact]
    public void Map_FixtureJson_ProducesParsableTyhpdef()
    {
        var json = File.ReadAllText(TyhpdefGenFixtures.JsonSchemaPath);
        var runtime = new PhpRuntimeInfo
        {
            Path = "/tmp/php",
            Version = "8.3.11",
            IsManaged = true,
            LoadedExtensions = ["json"],
        };
        var options = new TyhpdefGenerationOptions
        {
            Mode = TyhpdefGenerationMode.PhpExtension,
            ExtensionName = "json",
            IncludeDocComments = false,
            IncludeDeprecated = true,
        };

        var file = PhpReflectionMapper.Map(json, options, runtime);
        var text = TyhpdefOutputWriter.Write(file, includeDocComments: false);

        text.Should().StartWith("<?tyhpdef");
        text.Should().Contain("const int JSON_ERROR_NONE ?? 0;");
        text.Should().Contain("const int JSON_THROW_ON_ERROR ?? 4194304;");
        text.Should().Contain("function json_encode(mixed $value, int $flags = 0, int $depth = 512): string|false;");
        text.Should().Contain("function json_decode(string $json): mixed;");
        text.Should().Contain("class JsonException extends \\Exception implements \\Throwable {");
        text.Should().Contain("public const int CODE;");
        text.Should().Contain("protected string $message;");
        text.Should().Contain("public mixed $code;");
        text.Should().Contain("interface JsonSerializable {");
        text.Should().Contain("public function jsonSerialize(): mixed;");
        AssertParses(text);
    }

    [Fact]
    public void MapType_UnionAndNullable()
    {
        PhpReflectionMapper.MapType(new PhpReflectionTypeDto
        {
            Kind = "union",
            Text = "string|false",
        }).Should().Be("string|false");

        PhpReflectionMapper.MapType(new PhpReflectionTypeDto
        {
            Kind = "nullable",
            Text = "?string",
            Name = "string",
        }).Should().Be("?string");

        PhpReflectionMapper.MapType(new PhpReflectionTypeDto
        {
            Kind = "none",
            Text = "",
        }).Should().Be("");
    }

    [Fact]
    public void MapParameters_UntypedReflection_EmitsMixed()
    {
        var json = """
        {
          "schemaVersion": 1,
          "phpVersion": "8.5.8",
          "extension": "scratch",
          "extensionVersion": "1.0",
          "constants": [],
          "functions": [
            {
              "name": "readline_info",
              "params": [
                {
                  "name": "var_name",
                  "type": { "kind": "nullable", "text": "?string", "name": "string", "builtin": true, "nullable": true, "types": [] },
                  "optional": true,
                  "default": { "kind": "null", "value": null, "constName": null },
                  "variadic": false,
                  "byRef": false,
                  "promoted": false,
                  "attributes": []
                },
                {
                  "name": "value",
                  "type": { "kind": "none", "text": "", "name": null, "builtin": false, "nullable": false, "types": [] },
                  "optional": true,
                  "default": { "kind": "null", "value": null, "constName": null },
                  "variadic": false,
                  "byRef": false,
                  "promoted": false,
                  "attributes": []
                }
              ],
              "returnType": { "kind": "named", "text": "mixed", "name": "mixed", "builtin": true, "nullable": false, "types": [] },
              "returnByRef": false,
              "tentativeReturn": false,
              "deprecated": false,
              "docComment": null,
              "attributes": []
            }
          ],
          "classes": []
        }
        """;

        var (_, text) = MapJson(json, phpVersion: "8.5.8");
        text.Should().Contain("function readline_info(?string $var_name = null, mixed $value = null): mixed;");
        text.Should().NotContain(", $value = null)");
        AssertParses(text);
    }

    [Fact]
    public void Map_MissingReturnTypes_ConstructIsVoid_OthersAreMixedWithoutInventedParams()
    {
        var none = """{ "kind": "none", "text": "", "name": null, "builtin": false, "nullable": false, "types": [] }""";
        var json = $$"""
        {
          "schemaVersion": 1,
          "phpVersion": "8.5.8",
          "extension": "core",
          "extensionVersion": "8.5.8",
          "constants": [],
          "functions": [
            {
              "name": "set_error_handler",
              "params": [
                {
                  "name": "callback",
                  "type": { "kind": "nullable", "text": "?callable", "name": "callable", "builtin": true, "nullable": true, "types": [] },
                  "optional": false,
                  "default": null,
                  "variadic": false,
                  "byRef": false,
                  "promoted": false,
                  "attributes": []
                }
              ],
              "returnType": {{none}},
              "returnByRef": false,
              "tentativeReturn": false,
              "deprecated": false,
              "docComment": null,
              "attributes": []
            }
          ],
          "classes": [
            {
              "kind": "class",
              "name": "Closure",
              "fqn": "\\Closure",
              "modifiers": ["final"],
              "extends": null,
              "implements": [],
              "uses": [],
              "isAnonymous": false,
              "backingType": null,
              "docComment": null,
              "deprecated": false,
              "attributes": [],
              "constants": [],
              "properties": [],
              "methods": [
                {
                  "name": "__construct",
                  "params": [],
                  "returnType": {{none}},
                  "returnByRef": false,
                  "tentativeReturn": false,
                  "deprecated": false,
                  "docComment": null,
                  "attributes": [],
                  "modifiers": ["private"]
                },
                {
                  "name": "__invoke",
                  "params": [],
                  "returnType": {{none}},
                  "returnByRef": false,
                  "tentativeReturn": false,
                  "deprecated": false,
                  "docComment": null,
                  "attributes": [],
                  "modifiers": ["public"]
                }
              ],
              "enumCases": []
            }
          ]
        }
        """;

        var (_, text) = MapJson(json, phpVersion: "8.5.8");
        text.Should().Contain("function set_error_handler(?callable $callback): mixed;");
        text.Should().Contain("private function __construct(): void;");
        text.Should().Contain("public function __invoke(): mixed;");
        text.Should().NotContain("function __invoke(mixed");
        text.Should().NotContain("function __construct();");
        text.Should().NotContain("function __invoke();");
        AssertParses(text);
    }

    [Fact]
    public void MapValue_ConstAndSpecials()
    {
        PhpReflectionMapper.MapValue(new PhpReflectionValueDto { Kind = "const", ConstName = "JSON_THROW_ON_ERROR" })
            .Should().Be("JSON_THROW_ON_ERROR");
        PhpReflectionMapper.MapValue(new PhpReflectionValueDto { Kind = "nan" }).Should().Be("NAN");
        PhpReflectionMapper.MapValue(new PhpReflectionValueDto { Kind = "unavailable" }).Should().BeNull();
        PhpReflectionMapper.MapValue(new PhpReflectionValueDto { Kind = "null" }).Should().Be("null");
    }

    [Fact]
    public void Map_SkipsDeprecatedWhenDisabled()
    {
        var dump = PhpReflectionJson.Deserialize(File.ReadAllText(TyhpdefGenFixtures.JsonSchemaPath));
        dump.Functions[0].Deprecated = true;
        var runtime = new PhpRuntimeInfo { Path = "/tmp/php", Version = "8.3.11", IsManaged = true };
        var file = PhpReflectionMapper.Map(
            dump,
            new TyhpdefGenerationOptions { IncludeDeprecated = false, IncludeDocComments = false },
            runtime);

        file.GlobalFunctions.Should().NotContain(f => f.Name == "json_encode");
        file.GlobalFunctions.Should().Contain(f => f.Name == "json_decode");
    }

    [Fact]
    public void Map_HookedPropertyJson_FillsHooksAndWritesGetSet()
    {
        var json = HookedPropertyDumpJson(
            phpVersion: "8.4.1",
            hooks: """
            [
              { "name": "get", "returnsRef": false, "modifiers": [], "attributes": [] },
              { "name": "set", "returnsRef": false, "modifiers": [], "attributes": [] }
            ]
            """);

        var (file, text) = MapJson(json, phpVersion: "8.4.1");
        var property = file.GlobalTypes.Should().ContainSingle(t => t.Name == "Holder").Subject
            .Properties.Should().ContainSingle(p => p.Name == "hooked").Subject;

        property.Hooks.Should().HaveCount(2);
        property.Hooks[0].Name.Should().Be("get");
        property.Hooks[0].ReturnsRef.Should().BeFalse();
        property.Hooks[1].Name.Should().Be("set");
        text.Should().Contain("public string $hooked { get; set; }");
        text.Should().NotContain("public string $hooked;");
        AssertParses(text);
    }

    [Fact]
    public void Map_Php82Dump_OmitsHooksAndEmitsStorageForm()
    {
        var json = HookedPropertyDumpJson(phpVersion: "8.2.28", hooks: "[]");
        var (file, text) = MapJson(json, phpVersion: "8.2.28");
        var property = file.GlobalTypes.Should().ContainSingle(t => t.Name == "Holder").Subject
            .Properties.Should().ContainSingle(p => p.Name == "hooked").Subject;

        property.Hooks.Should().BeEmpty();
        text.Should().Contain("public string $hooked;");
        text.Should().NotContain("get;");
        text.Should().NotContain("set;");
        AssertParses(text);
    }

    [Fact]
    public void Map_ByRefGetHook_RoundTripsReturnsRefThroughJson()
    {
        var json = HookedPropertyDumpJson(
            phpVersion: "8.4.1",
            hooks: """
            [
              { "name": "get", "returnsRef": true, "modifiers": [], "attributes": [], "mystery": true },
              { "name": "set", "returnsRef": false, "modifiers": ["private"], "attributes": [] }
            ]
            """);

        var dump = PhpReflectionJson.Deserialize(json);
        dump.Classes[0].Properties[0].Hooks.Should().ContainSingle(h => h.Name == "get").Which
            .ReturnsRef.Should().BeTrue();

        var (file, text) = MapJson(json, phpVersion: "8.4.1");
        var property = file.GlobalTypes.Single(t => t.Name == "Holder").Properties.Single(p => p.Name == "hooked");
        property.Hooks.Should().ContainSingle(h => h.Name == "get").Which.ReturnsRef.Should().BeTrue();
        property.Hooks.Should().ContainSingle(h => h.Name == "set").Which.Modifiers.Should().Equal("private");
        text.Should().Contain("public string $hooked { &get; private set; }");
        AssertParses(text);
    }

    [Fact]
    public void LivePhp_HookedProperty_GetHooksJsonMapsToGetSet()
    {
        if (!PhpToolchain.IsPhpAvailable())
        {
            return;
        }

        var dir = Directory.CreateTempSubdirectory("tyhpdef-live-hooks-").FullName;
        try
        {
            var probe = Path.Combine(dir, "Probe.php");
            var driver = Path.Combine(dir, "dump-hooks.php");
            File.WriteAllText(probe, """
                <?php
                class TyhpPhase6HookProbe {
                    public string $hooked {
                        get => 'x';
                        set {
                            $this->hooked = $value;
                        }
                    }
                    public array $items {
                        &get {
                            return $this->items;
                        }
                    }
                }
                """);
            File.WriteAllText(driver, """
                <?php
                declare(strict_types=1);
                require __DIR__ . '/Probe.php';

                function dump_hooks(\ReflectionProperty $property): array
                {
                    if (!\method_exists($property, 'getHooks')) {
                        return [];
                    }
                    try {
                        $reflected = $property->getHooks();
                    } catch (\Throwable) {
                        return [];
                    }
                    $hooks = [];
                    foreach ($reflected as $name => $hook) {
                        if (!$hook instanceof \ReflectionMethod) {
                            continue;
                        }
                        $hooks[] = [
                            'name' => \is_string($name) && $name !== '' ? $name : $hook->getName(),
                            'returnsRef' => $hook->returnsReference(),
                            'modifiers' => [],
                            'attributes' => [],
                        ];
                    }
                    return $hooks;
                }

                $hooked = new \ReflectionProperty(TyhpPhase6HookProbe::class, 'hooked');
                $items = new \ReflectionProperty(TyhpPhase6HookProbe::class, 'items');
                echo \json_encode([
                    'schemaVersion' => 1,
                    'phpVersion' => \PHP_VERSION,
                    'extension' => 'scratch',
                    'extensionVersion' => '1.0',
                    'constants' => [],
                    'functions' => [],
                    'classes' => [[
                        'kind' => 'class',
                        'name' => 'TyhpPhase6HookProbe',
                        'fqn' => '\\TyhpPhase6HookProbe',
                        'properties' => [
                            [
                                'name' => 'hooked',
                                'type' => ['kind' => 'named', 'text' => 'string', 'name' => 'string', 'builtin' => true],
                                'modifiers' => ['public'],
                                'hooks' => dump_hooks($hooked),
                            ],
                            [
                                'name' => 'items',
                                'type' => ['kind' => 'named', 'text' => 'array', 'name' => 'array', 'builtin' => true],
                                'modifiers' => ['public'],
                                'hooks' => dump_hooks($items),
                            ],
                        ],
                    ]],
                ], \JSON_UNESCAPED_SLASHES);
                """);

            var ran = PhpToolchain.RunPhpScript(driver);
            ran.ExitCode.Should().Be(0, ran.StandardError + ran.StandardOutput);
            var json = ran.StandardOutput.Trim();
            json.Should().Contain("\"hooks\"");

            var dump = PhpReflectionJson.Deserialize(json);
            dump.Classes[0].Properties[0].Hooks.Select(h => h.Name).Should().BeEquivalentTo("get", "set");
            dump.Classes[0].Properties[1].Hooks.Should().ContainSingle(h => h.Name == "get").Which
                .ReturnsRef.Should().BeTrue();

            var (file, text) = MapJson(json, dump.PhpVersion);
            text.Should().Contain("public string $hooked { get; set; }");
            text.Should().Contain("public array $items { &get; }");
            AssertParses(text);
            _ = file;
        }
        finally
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

    private static (TyhpdefFile File, string Text) MapJson(string json, string phpVersion)
    {
        var runtime = new PhpRuntimeInfo
        {
            Path = "/tmp/php",
            Version = phpVersion,
            IsManaged = true,
        };
        var file = PhpReflectionMapper.Map(
            json,
            new TyhpdefGenerationOptions { IncludeDeprecated = true, IncludeDocComments = false },
            runtime);
        return (file, TyhpdefOutputWriter.Write(file, includeDocComments: false));
    }

    private static string HookedPropertyDumpJson(string phpVersion, string hooks)
        => $$"""
        {
          "schemaVersion": 1,
          "phpVersion": "{{phpVersion}}",
          "extension": "scratch",
          "extensionVersion": "1.0",
          "constants": [],
          "functions": [],
          "classes": [
            {
              "kind": "class",
              "name": "Holder",
              "fqn": "\\Holder",
              "modifiers": [],
              "extends": null,
              "implements": [],
              "uses": [],
              "isAnonymous": false,
              "backingType": null,
              "docComment": null,
              "deprecated": false,
              "attributes": [],
              "constants": [],
              "properties": [
                {
                  "name": "hooked",
                  "type": { "kind": "named", "text": "string", "name": "string", "builtin": true, "nullable": false, "types": [] },
                  "modifiers": ["public"],
                  "hasDefault": false,
                  "default": null,
                  "deprecated": false,
                  "docComment": null,
                  "attributes": [],
                  "hooks": {{hooks}}
                }
              ],
              "methods": [],
              "enumCases": []
            }
          ]
        }
        """;

    private static void AssertParses(string tyhpdef)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(tyhpdef);
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)) + "\n" + tyhpdef);
    }
}
