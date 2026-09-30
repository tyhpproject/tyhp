using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Reflection dump (schema version 1). Unknown properties are ignored.
    /// </summary>
    internal sealed class PhpReflectionDumpDto
    {
        public int SchemaVersion { get; set; }

        public string PhpVersion { get; set; } = "";

        public string Extension { get; set; } = "";

        public string? ExtensionVersion { get; set; }

        public List<PhpReflectionConstantDto> Constants { get; set; } = [];

        public List<PhpReflectionFunctionDto> Functions { get; set; } = [];

        public List<PhpReflectionClassDto> Classes { get; set; } = [];
    }

    internal sealed class PhpReflectionTypeDto
    {
        public string Kind { get; set; } = "none";

        public string Text { get; set; } = "";

        public string? Name { get; set; }

        public bool Builtin { get; set; }

        public bool Nullable { get; set; }

        public List<PhpReflectionTypeDto> Types { get; set; } = [];
    }

    internal sealed class PhpReflectionValueDto
    {
        public string Kind { get; set; } = "unavailable";

        public JsonElement? Value { get; set; }

        public string? ConstName { get; set; }

        /// <summary>
        /// Named attribute argument from <c>ReflectionAttribute::getArguments()</c>
        /// string keys. Null/omitted for positional arguments and non-attribute values.
        /// </summary>
        public string? ArgName { get; set; }
    }

    internal sealed class PhpReflectionParameterDto
    {
        public string Name { get; set; } = "";

        public PhpReflectionTypeDto? Type { get; set; }

        public bool Optional { get; set; }

        public PhpReflectionValueDto? Default { get; set; }

        public bool Variadic { get; set; }

        public bool ByRef { get; set; }

        public bool Promoted { get; set; }

        public List<PhpReflectionAttributeDto> Attributes { get; set; } = [];
    }

    internal sealed class PhpReflectionAttributeDto
    {
        public string Name { get; set; } = "";

        public List<PhpReflectionValueDto> Args { get; set; } = [];
    }

    internal sealed class PhpReflectionFunctionDto
    {
        public string Name { get; set; } = "";

        public List<PhpReflectionParameterDto> Params { get; set; } = [];

        public PhpReflectionTypeDto? ReturnType { get; set; }

        public bool ReturnByRef { get; set; }

        public bool TentativeReturn { get; set; }

        public bool Deprecated { get; set; }

        public string? DocComment { get; set; }

        public List<PhpReflectionAttributeDto> Attributes { get; set; } = [];

        public List<string> Modifiers { get; set; } = [];
    }

    internal sealed class PhpReflectionConstantDto
    {
        public string Name { get; set; } = "";

        public PhpReflectionValueDto? Value { get; set; }

        public PhpReflectionTypeDto? Type { get; set; }

        public List<string> Modifiers { get; set; } = [];

        public bool Deprecated { get; set; }

        public string? DocComment { get; set; }
    }

    internal sealed class PhpReflectionPropertyHookDto
    {
        public string Name { get; set; } = "";

        public bool ReturnsRef { get; set; }

        public List<string> Modifiers { get; set; } = [];

        public List<PhpReflectionAttributeDto> Attributes { get; set; } = [];
    }

    internal sealed class PhpReflectionPropertyDto
    {
        public string Name { get; set; } = "";

        public PhpReflectionTypeDto? Type { get; set; }

        public List<string> Modifiers { get; set; } = [];

        public bool HasDefault { get; set; }

        public PhpReflectionValueDto? Default { get; set; }

        public bool Deprecated { get; set; }

        public string? DocComment { get; set; }

        public List<PhpReflectionAttributeDto> Attributes { get; set; } = [];

        /// <summary>
        /// PHP 8.4 property hooks. Empty when PHP &lt; 8.4 or the property has no hooks.
        /// Unknown JSON fields on hook objects are ignored.
        /// </summary>
        public List<PhpReflectionPropertyHookDto> Hooks { get; set; } = [];
    }

    internal sealed class PhpReflectionEnumCaseDto
    {
        public string Name { get; set; } = "";

        public PhpReflectionValueDto? Backing { get; set; }

        public string? DocComment { get; set; }
    }

    internal sealed class PhpReflectionClassDto
    {
        public string Kind { get; set; } = "class";

        public string Name { get; set; } = "";

        public string Fqn { get; set; } = "";

        public List<string> Modifiers { get; set; } = [];

        public string? Extends { get; set; }

        public List<string> Implements { get; set; } = [];

        public List<string> Uses { get; set; } = [];

        public bool IsAnonymous { get; set; }

        public string? BackingType { get; set; }

        public string? DocComment { get; set; }

        public bool Deprecated { get; set; }

        public List<PhpReflectionAttributeDto> Attributes { get; set; } = [];

        public List<PhpReflectionConstantDto> Constants { get; set; } = [];

        public List<PhpReflectionPropertyDto> Properties { get; set; } = [];

        public List<PhpReflectionFunctionDto> Methods { get; set; } = [];

        public List<PhpReflectionEnumCaseDto> EnumCases { get; set; } = [];
    }

    internal static class PhpReflectionJson
    {
        public const int SchemaVersion = 1;

        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static PhpReflectionDumpDto Deserialize(string json)
        {
            var dto = JsonSerializer.Deserialize<PhpReflectionDumpDto>(json, Options);
            if (dto is null)
            {
                throw new JsonException("Reflection dump deserialized to null.");
            }

            return dto;
        }
    }
}
