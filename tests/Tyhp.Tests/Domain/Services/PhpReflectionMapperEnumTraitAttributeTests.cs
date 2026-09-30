using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class PhpReflectionMapperEnumTraitAttributeTests
{
    private const string Json = """
    {
      "schemaVersion": 1,
      "phpVersion": "8.3.11",
      "extension": "scratch",
      "extensionVersion": "1.0",
      "constants": [],
      "functions": [],
      "classes": [
        {
          "kind": "enum",
          "name": "Suit",
          "fqn": "\\Suit",
          "modifiers": [],
          "extends": null,
          "implements": ["\\JsonSerializable"],
          "uses": [],
          "isAnonymous": false,
          "backingType": "string",
          "docComment": null,
          "deprecated": false,
          "attributes": [{ "name": "\\Attr", "args": [{ "kind": "string", "value": "x", "constName": null }] }],
          "constants": [],
          "properties": [],
          "methods": [
            {
              "name": "label",
              "params": [],
              "returnType": { "kind": "named", "text": "string", "name": "string", "builtin": true, "nullable": false, "types": [] },
              "returnByRef": false,
              "tentativeReturn": false,
              "deprecated": false,
              "docComment": null,
              "attributes": [],
              "modifiers": ["public"]
            }
          ],
          "enumCases": [
            { "name": "Hearts", "backing": { "kind": "string", "value": "H", "constName": null }, "docComment": null },
            { "name": "Spades", "backing": { "kind": "string", "value": "S", "constName": null }, "docComment": null }
          ]
        },
        {
          "kind": "trait",
          "name": "Greetable",
          "fqn": "\\Greetable",
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
          "properties": [],
          "methods": [
            {
              "name": "greet",
              "params": [],
              "returnType": { "kind": "named", "text": "string", "name": "string", "builtin": true, "nullable": false, "types": [] },
              "returnByRef": false,
              "tentativeReturn": false,
              "deprecated": false,
              "docComment": null,
              "attributes": [],
              "modifiers": ["public"]
            }
          ],
          "enumCases": []
        },
        {
          "kind": "class",
          "name": "Greeter",
          "fqn": "\\Greeter",
          "modifiers": ["final"],
          "extends": null,
          "implements": [],
          "uses": ["\\Greetable"],
          "isAnonymous": false,
          "backingType": null,
          "docComment": null,
          "deprecated": false,
          "attributes": [],
          "constants": [],
          "properties": [],
          "methods": [],
          "enumCases": []
        }
      ]
    }
    """;

    [Fact]
    public void Map_EnumTraitAndAttributes_ProducesParsableTyhpdef()
    {
        var dump = PhpReflectionJson.Deserialize(Json);
        var runtime = new PhpRuntimeInfo { Path = "/tmp/php", Version = "8.3.11", IsManaged = true };
        var options = new TyhpdefGenerationOptions { IncludeDocComments = false, IncludeDeprecated = true };

        var file = PhpReflectionMapper.Map(dump, options, runtime);
        var text = TyhpdefOutputWriter.Write(file, includeDocComments: false);

        text.Should().Contain("enum Suit: string implements \\JsonSerializable {");
        text.Should().Contain("case Hearts = 'H';");
        text.Should().Contain("case Spades = 'S';");
        text.Should().Contain("public function label(): string;");
        text.Should().Contain("#[\\Attr('x')]");
        text.Should().Contain("trait Greetable {");
        text.Should().Contain("public function greet(): string;");
        text.Should().Contain("final class Greeter {");
        text.Should().Contain("use \\Greetable;");

        var parsed = ParserTestHelper.ParseTyhpdefContent(text);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => e.Message)) + "\n" + text);
    }

    [Fact]
    public void Map_EnumCaseConstantsDuplicatedInDump_EmitsCasesOnly()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "phpVersion": "8.5.8",
          "extension": "scratch",
          "extensionVersion": "1.0",
          "constants": [],
          "functions": [],
          "classes": [
            {
              "kind": "enum",
              "name": "IntervalBoundary",
              "fqn": "\\Random\\IntervalBoundary",
              "modifiers": [],
              "extends": null,
              "implements": ["\\UnitEnum"],
              "uses": [],
              "isAnonymous": false,
              "backingType": null,
              "docComment": null,
              "deprecated": false,
              "attributes": [],
              "constants": [
                { "name": "ClosedOpen", "value": { "kind": "unavailable", "value": null, "constName": null }, "type": { "kind": "none", "text": "", "name": null, "builtin": false, "nullable": false, "types": [] }, "modifiers": ["public"], "deprecated": false, "docComment": null },
                { "name": "ClosedClosed", "value": { "kind": "unavailable", "value": null, "constName": null }, "type": { "kind": "none", "text": "", "name": null, "builtin": false, "nullable": false, "types": [] }, "modifiers": ["public"], "deprecated": false, "docComment": null },
                { "name": "RealConst", "value": { "kind": "int", "value": 1, "constName": null }, "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] }, "modifiers": ["public"], "deprecated": false, "docComment": null }
              ],
              "properties": [],
              "methods": [],
              "enumCases": [
                { "name": "ClosedOpen", "backing": null, "docComment": null },
                { "name": "ClosedClosed", "backing": null, "docComment": null }
              ]
            }
          ]
        }
        """;

        var dump = PhpReflectionJson.Deserialize(json);
        var runtime = new PhpRuntimeInfo { Path = "/tmp/php", Version = "8.5.8", IsManaged = true };
        var options = new TyhpdefGenerationOptions { IncludeDocComments = false, IncludeDeprecated = true };

        var file = PhpReflectionMapper.Map(dump, options, runtime);
        var text = TyhpdefOutputWriter.Write(file, includeDocComments: false);

        text.Should().Contain("case ClosedOpen;");
        text.Should().Contain("case ClosedClosed;");
        text.Should().Contain("public const int RealConst");
        text.Should().NotContain("public const mixed ClosedOpen");
        text.Should().NotContain("public const mixed ClosedClosed");

        var parsed = ParserTestHelper.ParseTyhpdefContent(text);
        parsed.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", parsed.Diagnostics.Errors.Select(e => e.Message)) + "\n" + text);
    }
}
