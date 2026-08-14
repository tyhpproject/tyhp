using System.Text.Json;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Per-package <c>X.Y</c> source versions. Independent of the compiler
    /// <c>805.x</c> line. Packagist artifacts are <c>80N.X.Y</c>.
    /// Keep <see cref="Bundled"/> in sync with <c>runtime/packages/*/composer.json</c>.
    /// </summary>
    internal static class RuntimePackageVersions
    {
        internal const string Php = "0.0";
        internal const string Core = "0.0";
        internal const string Async = "0.0";
        internal const string Decimal = "0.0";
        internal const string Lambda = "0.0";

        internal static readonly IReadOnlyDictionary<string, string> Bundled =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tyhp/php"] = Php,
                ["tyhp/core"] = Core,
                ["tyhp/async"] = Async,
                ["tyhp/decimal"] = Decimal,
                ["tyhp/lambda"] = Lambda,
            };

        internal static string ForPackage(string packageName)
        {
            return Bundled.TryGetValue(packageName, out var version) ? version : Core;
        }

        internal static string? TryReadComposerVersion(string packageDirectory)
        {
            var path = Path.Combine(packageDirectory, "composer.json");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("version", out var version)
                    && version.ValueKind == JsonValueKind.String)
                {
                    var text = version.GetString();
                    return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }
    }
}
