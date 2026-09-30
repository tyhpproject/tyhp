using System.Text.Json;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Per-package source versions. Independent of the compiler
    /// <c>805.x</c> line. Compiled <c>tyhp/*</c> Packagist artifacts are <c>80N.X.Y</c>;
    /// <c>tyhpdef/*</c> artifacts use the source version as-is.
    /// <see cref="Bundled"/> is a fallback table. It is not filled by reading package
    /// directories. When a resolved package directory is already known,
    /// <see cref="TryReadComposerVersion"/> may read that one <c>composer.json</c>;
    /// callers use <see cref="ForPackage"/> when that read misses.
    /// </summary>
    internal static class RuntimePackageVersions
    {
        internal const string Php = "0.0.1";
        internal const string Core = "0.1";
        internal const string Async = "0.1";
        internal const string Decimal = "0.1";
        internal const string Lambda = "0.1";

        internal static readonly IReadOnlyDictionary<string, string> Bundled =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tyhpdef/php"] = Php,
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
