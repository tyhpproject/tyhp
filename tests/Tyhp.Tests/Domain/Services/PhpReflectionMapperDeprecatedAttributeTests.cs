using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class PhpReflectionMapperDeprecatedAttributeTests
{
    [Fact]
    public void Map_DeprecatedUnnamedVersionFirst_EmitsNamedCtorOrder()
    {
        var text = MapFunctionAttributes("""
            { "kind": "string", "value": "8.1", "constName": null },
            { "kind": "string", "value": "use new_fn() instead", "constName": null }
            """);

        text.Should().Contain("#[\\Deprecated(message: 'use new_fn() instead', since: '8.1')]");
        text.Should().NotContain("#[\\Deprecated('8.1'");
        AssertParses(text);
    }

    [Fact]
    public void Map_DeprecatedNamedSinceFirst_EmitsNamedCtorOrder()
    {
        var text = MapFunctionAttributes("""
            { "kind": "string", "value": "8.1", "constName": null, "argName": "since" },
            { "kind": "string", "value": "use new_fn() instead", "constName": null, "argName": "message" }
            """);

        text.Should().Contain("#[\\Deprecated(message: 'use new_fn() instead', since: '8.1')]");
        AssertParses(text);
    }

    [Fact]
    public void Map_DeprecatedUnnamedSinceOnly_EmitsNamedSince()
    {
        var text = MapFunctionAttributes("""
            { "kind": "string", "value": "8.4", "constName": null }
            """);

        text.Should().Contain("#[\\Deprecated(since: '8.4')]");
        text.Should().NotContain("#[\\Deprecated('8.4')]");
        AssertParses(text);
    }

    [Fact]
    public void Map_DeprecatedUnnamedMessageThenSince_EmitsNamedCtorOrder()
    {
        var text = MapFunctionAttributes("""
            { "kind": "string", "value": "use bar() instead", "constName": null },
            { "kind": "string", "value": "8.2", "constName": null }
            """);

        text.Should().Contain("#[\\Deprecated(message: 'use bar() instead', since: '8.2')]");
        AssertParses(text);
    }

    [Fact]
    public void Map_OtherAttributeKeepsPositionalArgs()
    {
        var text = MapJson(FunctionDumpJson(
            """{ "kind": "string", "value": "x", "constName": null }""",
            attributeName: """\\Attr"""));

        text.Should().Contain("#[\\Attr('x')]");
        AssertParses(text);
    }

    private static string MapFunctionAttributes(string argsJson)
        => MapJson(FunctionDumpJson(argsJson, attributeName: """\\Deprecated"""));

    private static string FunctionDumpJson(string argsJson, string attributeName)
        => """
        {
          "schemaVersion": 1,
          "phpVersion": "8.4.1",
          "extension": "scratch",
          "extensionVersion": "1.0",
          "constants": [],
          "functions": [
            {
              "name": "old_fn",
              "params": [],
              "returnType": { "kind": "named", "text": "void", "name": "void", "builtin": true, "nullable": false, "types": [] },
              "returnByRef": false,
              "tentativeReturn": false,
              "deprecated": true,
              "docComment": null,
              "attributes": [{
                "name": "__ATTR_NAME__",
                "args": [__ARGS__]
              }],
              "modifiers": []
            }
          ],
          "classes": []
        }
        """.Replace("__ATTR_NAME__", attributeName, StringComparison.Ordinal)
            .Replace("__ARGS__", argsJson, StringComparison.Ordinal);

    private static string MapJson(string json)
    {
        var runtime = new PhpRuntimeInfo
        {
            Path = "/tmp/php",
            Version = "8.4.1",
            IsManaged = true,
        };
        var file = PhpReflectionMapper.Map(
            json,
            new TyhpdefGenerationOptions { IncludeDeprecated = true, IncludeDocComments = false },
            runtime);
        return TyhpdefOutputWriter.Write(file, includeDocComments: false);
    }

    private static void AssertParses(string tyhpdef)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(tyhpdef);
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)) + "\n" + tyhpdef);
    }
}
