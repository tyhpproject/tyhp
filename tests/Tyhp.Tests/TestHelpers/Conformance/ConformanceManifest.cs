using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tyhp.Tests.TestHelpers.Conformance;

public sealed class ConformanceManifest
{
    [JsonPropertyName("suite")]
    public string Suite { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("defaults")]
    public ConformanceDefaults? Defaults { get; set; }

    [JsonPropertyName("cases")]
    public List<ConformanceCase> Cases { get; set; } = new();
}

public sealed class ConformanceDefaults
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "lint";

    [JsonPropertyName("config")]
    public Dictionary<string, JsonElement>? Config { get; set; }
}

public sealed class ConformanceCase
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("config")]
    public Dictionary<string, JsonElement>? Config { get; set; }

    [JsonPropertyName("expect")]
    public ConformanceExpectation? Expect { get; set; }

    [JsonPropertyName("skip")]
    public string? Skip { get; set; }
}

public sealed class ConformanceExpectation
{
    [JsonPropertyName("errorCount")]
    public JsonElement? ErrorCount { get; set; }

    [JsonPropertyName("warningCount")]
    public JsonElement? WarningCount { get; set; }

    [JsonPropertyName("codes")]
    public List<int>? Codes { get; set; }

    [JsonPropertyName("noDiagnostics")]
    public bool? NoDiagnostics { get; set; }

    [JsonPropertyName("php")]
    public string? Php { get; set; }

    [JsonIgnore]
    public int? ErrorCountExact => TryReadExactCount(this.ErrorCount);

    [JsonIgnore]
    public int? ErrorCountMin => TryReadMinCount(this.ErrorCount);

    [JsonIgnore]
    public int? ErrorCountMax => TryReadMaxCount(this.ErrorCount);

    [JsonIgnore]
    public int? WarningCountExact => TryReadExactCount(this.WarningCount);

    private static int? TryReadExactCount(JsonElement? element)
    {
        if (element is not { } value)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.Object => null,
            _ => null,
        };
    }

    private static int? TryReadMinCount(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value)
        {
            return null;
        }

        if (value.TryGetProperty("min", out var min) && min.ValueKind == JsonValueKind.Number)
        {
            return min.GetInt32();
        }

        return null;
    }

    private static int? TryReadMaxCount(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } value)
        {
            return null;
        }

        if (value.TryGetProperty("max", out var max) && max.ValueKind == JsonValueKind.Number)
        {
            return max.GetInt32();
        }

        return null;
    }
}
