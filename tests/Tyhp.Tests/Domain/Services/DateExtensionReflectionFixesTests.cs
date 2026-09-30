using Tyhp.Domain.Services;
using Tyhp.Tests.TestHelpers;

namespace Tyhp.Tests.Domain.Services;

[Trait("Category", "Tyhpdef")]
public class DateExtensionReflectionFixesTests
{
    [Fact]
    public void Map_DateInterval_InjectsDocumentedPublicProperties()
    {
        var text = MapDump(ClassJson("DateInterval", properties: "[]", methods: FormatMethod()));

        text.Should().Contain("public int $d;");
        text.Should().Contain("public string $date_string;");
        text.Should().Contain("public int|false $days;");
        text.Should().Contain("public float $f;");
        text.Should().Contain("public bool $from_string;");
        text.Should().Contain("public int $h;");
        text.Should().Contain("public int $i;");
        text.Should().Contain("public int $invert;");
        text.Should().Contain("public int $m;");
        text.Should().Contain("public int $s;");
        text.Should().Contain("public int $y;");
        text.Should().Contain("public function format(string $format): string;");
        AssertParses(text);
    }

    [Fact]
    public void Map_DateInterval_DoesNotDuplicateExistingProperties()
    {
        var existing = """
                {
                  "name": "y",
                  "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] },
                  "modifiers": ["public"],
                  "hasDefault": false,
                  "default": null,
                  "deprecated": false,
                  "docComment": null,
                  "attributes": [],
                  "hooks": []
                }
                """;
        var text = MapDump(ClassJson("DateInterval", properties: "[" + existing + "]", methods: ""));

        text.Should().Contain("public int $y;");
        var yCount = CountOccurrences(text, "public int $y;");
        yCount.Should().Be(1);
        text.Should().Contain("public int $m;");
        AssertParses(text);
    }

    [Fact]
    public void Map_DatePeriod_ReplacesCollapsedMixedConstructorWithThreeOverloads()
    {
        var collapsed = FormatMethod(
            name: "__construct",
            parameters: """
                    { "name": "start", "type": { "kind": "none", "text": "", "name": null, "builtin": false, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                    { "name": "interval", "type": { "kind": "none", "text": "", "name": null, "builtin": false, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                    { "name": "end", "type": { "kind": "none", "text": "", "name": null, "builtin": false, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                    { "name": "options", "type": { "kind": "none", "text": "", "name": null, "builtin": false, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] }
                    """,
            returnType: """{ "kind": "named", "text": "void", "name": "void", "builtin": true, "nullable": false, "types": [] }""");
        var text = MapDump(ClassJson("DatePeriod", properties: "[]", methods: collapsed));

        text.Should().Contain(
            "public function __construct(\\DateTimeInterface $start, \\DateInterval $interval, int $recurrences, int $options = 0): void;");
        text.Should().Contain(
            "public function __construct(\\DateTimeInterface $start, \\DateInterval $interval, \\DateTimeInterface $end, int $options = 0): void;");
        text.Should().Contain(
            "deprecated public function __construct(string $isostr, int $options = 0): void;");
        text.Should().NotContain("mixed $start");
        text.Should().NotContain("mixed $interval");
        AssertParses(text);
    }

    [Fact]
    public void Map_DatePeriod_KeepsAlreadySplitConstructors()
    {
        var recurrences = FormatMethod(
            name: "__construct",
            parameters: """
                    { "name": "start", "type": { "kind": "named", "text": "\\DateTimeInterface", "name": "DateTimeInterface", "builtin": false, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                    { "name": "interval", "type": { "kind": "named", "text": "\\DateInterval", "name": "DateInterval", "builtin": false, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                    { "name": "recurrences", "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                    { "name": "options", "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] }, "optional": true, "default": { "kind": "int", "value": 0, "constName": null }, "variadic": false, "byRef": false, "promoted": false, "attributes": [] }
                    """,
            returnType: """{ "kind": "named", "text": "void", "name": "void", "builtin": true, "nullable": false, "types": [] }""");
        var end = FormatMethod(
            name: "__construct",
            parameters: """
                    { "name": "start", "type": { "kind": "named", "text": "\\DateTimeInterface", "name": "DateTimeInterface", "builtin": false, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                    { "name": "interval", "type": { "kind": "named", "text": "\\DateInterval", "name": "DateInterval", "builtin": false, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                    { "name": "end", "type": { "kind": "named", "text": "\\DateTimeInterface", "name": "DateTimeInterface", "builtin": false, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                    { "name": "options", "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] }, "optional": true, "default": { "kind": "int", "value": 0, "constName": null }, "variadic": false, "byRef": false, "promoted": false, "attributes": [] }
                    """,
            returnType: """{ "kind": "named", "text": "void", "name": "void", "builtin": true, "nullable": false, "types": [] }""");
        var iso = FormatMethod(
            name: "__construct",
            parameters: """
                    { "name": "isostr", "type": { "kind": "named", "text": "string", "name": "string", "builtin": true, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
                    { "name": "options", "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] }, "optional": true, "default": { "kind": "int", "value": 0, "constName": null }, "variadic": false, "byRef": false, "promoted": false, "attributes": [] }
                    """,
            returnType: """{ "kind": "named", "text": "void", "name": "void", "builtin": true, "nullable": false, "types": [] }""",
            deprecated: true);
        var text = MapDump(ClassJson("DatePeriod", properties: "[]", methods: recurrences + "," + end + "," + iso));

        CountOccurrences(text, "function __construct(").Should().Be(3);
        CountOccurrences(text, "deprecated public function __construct(").Should().Be(1);
        text.Should().NotContain("mixed $start");
        AssertParses(text);
    }

    [Fact]
    public void Map_DateTimeZoneGetTransitions_RewritesPhpIntMaxDefault()
    {
        var method = FormatMethod(
            name: "getTransitions",
            parameters: TransitionsParams(endKind: "const", endConst: "PHP_INT_MAX", endValue: "null"),
            returnType: """{ "kind": "named", "text": "array|false", "name": null, "builtin": false, "nullable": false, "types": [] }""");
        var text = MapDump(ClassJson("DateTimeZone", properties: "[]", methods: method));

        text.Should().Contain(
            "public function getTransitions(int $timestampBegin = PHP_INT_MIN, int $timestampEnd = 2147483647): array|false;");
        text.Should().NotContain("timestampEnd = PHP_INT_MAX");
        AssertParses(text);
    }

    [Fact]
    public void Map_TimezoneTransitionsGet_RewritesPhpIntMaxDefault()
    {
        var json = DumpJson(
            functions: $$"""
                {
                  "name": "timezone_transitions_get",
                  "params": [{{ObjectParam()}}, {{TransitionsParams(endKind: "const", endConst: "PHP_INT_MAX", endValue: "null")}}],
                  "returnType": { "kind": "named", "text": "array|false", "name": null, "builtin": false, "nullable": false, "types": [] },
                  "returnByRef": false,
                  "tentativeReturn": false,
                  "deprecated": false,
                  "docComment": null,
                  "attributes": [],
                  "modifiers": []
                }
                """,
            classes: "");
        var text = MapDump(json);

        text.Should().Contain(
            "function timezone_transitions_get(\\DateTimeZone $object, int $timestampBegin = PHP_INT_MIN, int $timestampEnd = 2147483647): array|false;");
        text.Should().NotContain("timestampEnd = PHP_INT_MAX");
        AssertParses(text);
    }

    [Fact]
    public void Map_OtherClassGetTransitions_LeavesPhpIntMaxDefault()
    {
        var method = FormatMethod(
            name: "getTransitions",
            parameters: TransitionsParams(endKind: "const", endConst: "PHP_INT_MAX", endValue: "null"),
            returnType: """{ "kind": "named", "text": "array|false", "name": null, "builtin": false, "nullable": false, "types": [] }""");
        var text = MapDump(ClassJson("NotATimezone", properties: "[]", methods: method));

        text.Should().Contain("int $timestampEnd = PHP_INT_MAX");
        text.Should().NotContain("timestampEnd = 2147483647");
        AssertParses(text);
    }

    [Fact]
    public void Merge_GetTransitionsDefault_IsUngatedWhenBothTargetsAgreeAfterFix()
    {
        var method84 = FormatMethod(
            name: "getTransitions",
            parameters: TransitionsParams(endKind: "const", endConst: "PHP_INT_MAX", endValue: "null"),
            returnType: """{ "kind": "named", "text": "array|false", "name": null, "builtin": false, "nullable": false, "types": [] }""");
        var method85 = FormatMethod(
            name: "getTransitions",
            parameters: TransitionsParams(endKind: "int", endConst: "null", endValue: "2147483647"),
            returnType: """{ "kind": "named", "text": "array|false", "name": null, "builtin": false, "nullable": false, "types": [] }""");

        var v84 = MapFile(ClassJson("DateTimeZone", properties: "[]", methods: method84, phpVersion: "8.4.23"));
        var v85 = MapFile(ClassJson("DateTimeZone", properties: "[]", methods: method85, phpVersion: "8.5.8"));
        var merged = TyhpdefMultiTargetMerger.Merge([("8.4", v84), ("8.5", v85)]);
        var text = TyhpdefOutputWriter.Write(merged, includeDocComments: false);

        CountOccurrences(text, "function getTransitions(").Should().Be(1);
        text.Should().Contain("int $timestampEnd = 2147483647");
        text.Should().NotContain("PHP_INT_MAX");
        text.Should().NotContain("#[\\Tyhp\\Php(");
        merged.DeclareBlocks.Should().BeEmpty();
        AssertParses(text);
    }

    private static string MapDump(string json)
        => TyhpdefOutputWriter.Write(MapFile(json), includeDocComments: false);

    private static TyhpdefFile MapFile(string json)
    {
        var dump = PhpReflectionJson.Deserialize(json);
        var runtime = new PhpRuntimeInfo
        {
            Path = "/tmp/php",
            Version = dump.PhpVersion ?? "8.5.8",
            IsManaged = true,
        };
        var options = new TyhpdefGenerationOptions
        {
            Mode = TyhpdefGenerationMode.PhpExtension,
            ExtensionName = "date",
            IncludeDocComments = false,
            IncludeDeprecated = true,
        };
        return PhpReflectionMapper.Map(dump, options, runtime);
    }

    private static void AssertParses(string tyhpdef)
    {
        var result = ParserTestHelper.ParseTyhpdefContent(tyhpdef);
        result.Diagnostics.Errors.Should().BeEmpty(
            string.Join("; ", result.Diagnostics.Errors.Select(e => e.Message)) + "\n" + tyhpdef);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static string ClassJson(
        string className,
        string properties,
        string methods,
        string phpVersion = "8.5.8")
        => DumpJson(
            functions: "",
            classes: $$"""
                {
                  "kind": "class",
                  "name": "{{className}}",
                  "fqn": "\\{{className}}",
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
                  "properties": {{properties}},
                  "methods": [{{methods}}],
                  "enumCases": []
                }
                """,
            phpVersion: phpVersion);

    private static string DumpJson(string functions, string classes, string phpVersion = "8.5.8")
    {
        var functionList = string.IsNullOrWhiteSpace(functions) ? "" : functions;
        var classList = string.IsNullOrWhiteSpace(classes) ? "" : classes;
        return $$"""
        {
          "schemaVersion": 1,
          "phpVersion": "{{phpVersion}}",
          "extension": "date",
          "extensionVersion": "{{phpVersion}}",
          "constants": [],
          "functions": [{{functionList}}],
          "classes": [{{classList}}]
        }
        """;
    }

    private static string FormatMethod(
        string name = "format",
        string parameters = """{ "name": "format", "type": { "kind": "named", "text": "string", "name": "string", "builtin": true, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] }""",
        string returnType = """{ "kind": "named", "text": "string", "name": "string", "builtin": true, "nullable": false, "types": [] }""",
        bool deprecated = false)
        => $$"""
            {
              "name": "{{name}}",
              "params": [{{parameters}}],
              "returnType": {{returnType}},
              "returnByRef": false,
              "tentativeReturn": false,
              "deprecated": {{(deprecated ? "true" : "false")}},
              "docComment": null,
              "attributes": [],
              "modifiers": ["public"]
            }
            """;

    private static string TransitionsParams(string endKind, string endConst, string endValue)
        => $$"""
            { "name": "timestampBegin", "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] }, "optional": true, "default": { "kind": "const", "value": null, "constName": "PHP_INT_MIN" }, "variadic": false, "byRef": false, "promoted": false, "attributes": [] },
            { "name": "timestampEnd", "type": { "kind": "named", "text": "int", "name": "int", "builtin": true, "nullable": false, "types": [] }, "optional": true, "default": { "kind": "{{endKind}}", "value": {{endValue}}, "constName": {{ConstNameJson(endConst)}} }, "variadic": false, "byRef": false, "promoted": false, "attributes": [] }
            """;

    private static string ObjectParam()
        => """
            { "name": "object", "type": { "kind": "named", "text": "\\DateTimeZone", "name": "DateTimeZone", "builtin": false, "nullable": false, "types": [] }, "optional": false, "default": null, "variadic": false, "byRef": false, "promoted": false, "attributes": [] }
            """;

    private static string ConstNameJson(string? constName)
        => constName is null or "null" ? "null" : "\"" + constName + "\"";
}
