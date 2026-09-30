using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// <c>composer.json</c> <c>extra.tyhp.package</c>: sentinel, glob spec, and additive
    /// library merge. Presence of that key as a JSON object means this package contributes
    /// tyhpdefs. Omitted arrays are empty. Unknown inner keys are ignored at load.
    /// </summary>
    public static class ComposerExtraTyhpPackageManifest
    {
        public const string ComposerJsonFileName = "composer.json";
        public const string PackageTyhpdefInclude = "./package.tyhpdef";

        public const string ExtraPropertyName = "extra";
        public const string TyhpPropertyName = "tyhp";
        public const string PackagePropertyName = "package";

        private static readonly JsonSerializerOptions JsonWriteOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public enum InspectResult
        {
            Missing,
            Unreadable,
            InvalidJson,
            NoPackage,
            HasPackage,
        }

        /// <summary>
        /// Builds the <c>composer.json</c> text that should be written for a library
        /// publish path. When <paramref name="existingComposerJson"/> is null or whitespace,
        /// returns a minimal object that contains <c>extra.tyhp.package</c>. When it is
        /// present, missing generated keys and array string items are added under
        /// <c>extra.tyhp.package</c> only; other <c>extra</c> / <c>extra.tyhp</c> keys and
        /// existing package values are left unchanged.
        /// </summary>
        /// <returns>
        /// <see langword="false"/> when existing JSON cannot be parsed as an object
        /// (the caller must not overwrite the file).
        /// </returns>
        public static bool TryCompose(
            string? existingComposerJson,
            bool isTagless,
            out string outputJson,
            out bool changed,
            out string? error)
        {
            outputJson = "";
            changed = false;
            error = null;

            var generated = CreateGenerated(isTagless);
            JsonObject root;
            if (string.IsNullOrWhiteSpace(existingComposerJson))
            {
                root = new JsonObject();
                EnsurePackageObject(root);
                MergeMissing(GetPackageObject(root)!, generated);
                outputJson = root.ToJsonString(JsonWriteOptions) + Environment.NewLine;
                changed = true;
                return true;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(existingComposerJson);
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }

            if (node is not JsonObject existing)
            {
                error = "root value is not a JSON object";
                return false;
            }

            root = existing;
            var createdPackage = EnsurePackageObject(root);
            var package = GetPackageObject(root)!;
            var merged = MergeMissing(package, generated);
            changed = createdPackage || merged;
            outputJson = root.ToJsonString(JsonWriteOptions) + Environment.NewLine;
            return true;
        }

        internal static JsonObject CreateGenerated(bool isTagless)
        {
            return new JsonObject
            {
                ["include"] = new JsonArray { PackageTyhpdefInclude },
                ["exclude"] = new JsonArray(),
                ["overlay"] = new JsonArray(),
                ["source"] = new JsonObject
                {
                    ["tagless"] = isTagless,
                },
            };
        }

        public static bool IsComposerJsonFileName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return string.Equals(
                Path.GetFileName(path),
                ComposerJsonFileName,
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool PatternMayMatchComposerJson(string? includePattern)
        {
            if (string.IsNullOrWhiteSpace(includePattern))
            {
                return false;
            }

            var normalized = includePattern.Replace('\\', '/');
            var lastSegment = normalized[(normalized.LastIndexOf('/') + 1)..];
            return string.Equals(lastSegment, ComposerJsonFileName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasPackageObject(string? composerJsonPath)
            => Inspect(composerJsonPath) == InspectResult.HasPackage;

        public static bool HasPackageObjectAtInstallPath(string? installPath)
        {
            if (string.IsNullOrWhiteSpace(installPath))
            {
                return false;
            }

            return HasPackageObject(Path.Combine(installPath, ComposerJsonFileName));
        }

        /// <summary>
        /// <c>extra.tyhp.tyhpdef</c> on a PHP package: a Composer package name whose own
        /// <c>extra.tyhp.package</c> holds the types. Not a file glob.
        /// </summary>
        public static string? TryReadTyhpdefPointer(string? composerJsonPath)
            => TryReadExtraTyhpString(composerJsonPath, "tyhpdef");

        /// <summary>
        /// <c>extra.tyhp.impl</c> on a public metapackage. Documentation of the impl name.
        /// Loading uses <c>extra.tyhp.package</c> on whatever is installed.
        /// </summary>
        public static string? TryReadImplPointer(string? composerJsonPath)
            => TryReadExtraTyhpString(composerJsonPath, "impl");

        public static string? TryReadPackageName(string? composerJsonPath)
            => TryReadRootString(composerJsonPath, "name");

        public static string? TryReadExtraTyhpString(string? composerJsonPath, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            return TryReadNestedString(composerJsonPath, ExtraPropertyName, TyhpPropertyName, propertyName);
        }

        private static string? TryReadRootString(string? composerJsonPath, string propertyName)
        {
            var root = TryReadObject(composerJsonPath);
            if (root is null || root[propertyName] is not JsonValue value)
            {
                return null;
            }

            return NormalizeNonEmptyString(value);
        }

        private static string? TryReadNestedString(
            string? composerJsonPath,
            string extraKey,
            string tyhpKey,
            string propertyName)
        {
            var root = TryReadObject(composerJsonPath);
            if (root is null
                || root[extraKey] is not JsonObject extra
                || extra[tyhpKey] is not JsonObject tyhp
                || tyhp[propertyName] is not JsonValue value)
            {
                return null;
            }

            return NormalizeNonEmptyString(value);
        }

        private static JsonObject? TryReadObject(string? composerJsonPath)
        {
            if (string.IsNullOrWhiteSpace(composerJsonPath) || !File.Exists(composerJsonPath))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(File.ReadAllText(composerJsonPath)) as JsonObject;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }

        private static string? NormalizeNonEmptyString(JsonValue value)
        {
            if (!value.TryGetValue<string>(out var text))
            {
                return null;
            }

            text = text.Trim();
            return text.Length == 0 ? null : text;
        }

        public static InspectResult Inspect(string? composerJsonPath)
        {
            if (string.IsNullOrWhiteSpace(composerJsonPath) || !File.Exists(composerJsonPath))
            {
                return InspectResult.Missing;
            }

            string json;
            try
            {
                json = File.ReadAllText(composerJsonPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return InspectResult.Unreadable;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return InspectResult.InvalidJson;
            }

            return node is JsonObject root && TryGetPackageObject(root, out _)
                ? InspectResult.HasPackage
                : InspectResult.NoPackage;
        }

        public static bool TryGetPackageObject(JsonObject root, out JsonObject package)
        {
            package = null!;
            if (root[ExtraPropertyName] is not JsonObject extra)
            {
                return false;
            }

            if (extra[TyhpPropertyName] is not JsonObject tyhp)
            {
                return false;
            }

            if (tyhp[PackagePropertyName] is not JsonObject packageObject)
            {
                return false;
            }

            package = packageObject;
            return true;
        }

        public static bool TryGetPackageElement(JsonElement root, out JsonElement package)
        {
            package = default;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty(ExtraPropertyName, out var extra)
                || extra.ValueKind != JsonValueKind.Object
                || !extra.TryGetProperty(TyhpPropertyName, out var tyhp)
                || tyhp.ValueKind != JsonValueKind.Object
                || !tyhp.TryGetProperty(PackagePropertyName, out var packageElement)
                || packageElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            package = packageElement;
            return true;
        }

        public static bool TryReadTagless(JsonElement package)
        {
            if (package.ValueKind != JsonValueKind.Object
                || !package.TryGetProperty("source", out var source)
                || source.ValueKind != JsonValueKind.Object
                || !source.TryGetProperty("tagless", out var nested)
                || (nested.ValueKind != JsonValueKind.True && nested.ValueKind != JsonValueKind.False))
            {
                return false;
            }

            return nested.GetBoolean();
        }

        public static IReadOnlyList<string> ReadStringArray(JsonElement package, string propertyName)
        {
            if (package.ValueKind != JsonValueKind.Object
                || !package.TryGetProperty(propertyName, out var element)
                || element.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var patterns = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var pattern = item.GetString();
                if (!string.IsNullOrWhiteSpace(pattern))
                {
                    patterns.Add(pattern);
                }
            }

            return patterns;
        }

        public static JsonArray GetOrCreateOverlayArray(JsonObject root, bool composerJson)
        {
            if (!composerJson)
            {
                if (root["overlay"] is not JsonArray overlay)
                {
                    overlay = [];
                    root["overlay"] = overlay;
                }

                return overlay;
            }

            EnsurePackageObject(root);
            var package = GetPackageObject(root)!;
            if (package["overlay"] is not JsonArray packageOverlay)
            {
                packageOverlay = [];
                package["overlay"] = packageOverlay;
            }

            return packageOverlay;
        }

        private static JsonObject? GetPackageObject(JsonObject root)
        {
            return TryGetPackageObject(root, out var package) ? package : null;
        }

        /// <returns><see langword="true"/> when <c>extra.tyhp.package</c> was created or replaced.</returns>
        private static bool EnsurePackageObject(JsonObject root)
        {
            var changed = false;
            if (root[ExtraPropertyName] is not JsonObject extra)
            {
                extra = new JsonObject();
                root[ExtraPropertyName] = extra;
                changed = true;
            }

            if (extra[TyhpPropertyName] is not JsonObject tyhp)
            {
                tyhp = new JsonObject();
                extra[TyhpPropertyName] = tyhp;
                changed = true;
            }

            if (tyhp[PackagePropertyName] is not JsonObject)
            {
                tyhp[PackagePropertyName] = new JsonObject();
                changed = true;
            }

            return changed;
        }

        private static bool MergeMissing(JsonObject target, JsonObject desired)
        {
            var changed = false;
            foreach (var (key, desiredValue) in desired)
            {
                if (!target.ContainsKey(key))
                {
                    target[key] = desiredValue?.DeepClone();
                    changed = true;
                    continue;
                }

                var existingValue = target[key];
                if (existingValue is JsonObject existingObject && desiredValue is JsonObject desiredObject)
                {
                    changed |= MergeMissing(existingObject, desiredObject);
                }
                else if (existingValue is JsonArray existingArray && desiredValue is JsonArray desiredArray)
                {
                    changed |= AppendMissingArrayItems(existingArray, desiredArray);
                }
            }

            return changed;
        }

        private static bool AppendMissingArrayItems(JsonArray existing, JsonArray desired)
        {
            var changed = false;
            foreach (var desiredItem in desired)
            {
                if (desiredItem is not JsonValue desiredValue
                    || !desiredValue.TryGetValue<string>(out var desiredString)
                    || string.IsNullOrWhiteSpace(desiredString))
                {
                    continue;
                }

                if (ArrayContainsGlob(existing, desiredString))
                {
                    continue;
                }

                existing.Add(desiredString);
                changed = true;
            }

            return changed;
        }

        private static bool ArrayContainsGlob(JsonArray array, string wanted)
        {
            var normalizedWanted = TyhpdefOverlayManifestEditor.NormalizeGlob(wanted);
            foreach (var item in array)
            {
                if (item is JsonValue value
                    && value.TryGetValue<string>(out var existing)
                    && string.Equals(
                        TyhpdefOverlayManifestEditor.NormalizeGlob(existing),
                        normalizedWanted,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
