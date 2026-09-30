using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Appends the hand-written overlay glob to <c>extra.tyhp.package.overlay</c> on
    /// <c>composer.json</c>, or to top-level <c>overlay</c> on <c>tyhp.json</c>, without
    /// duplicating it or inserting it before the stubs glob.
    /// </summary>
    public static class TyhpdefOverlayManifestEditor
    {
        public const string HandWrittenOverlayGlob = "./_tyhpdef/overlays/*.tyhpdef";
        public const string StubsOverlayGlob = "./_tyhpdef/overlays/stubs/*.tyhpdef";

        public static bool EnsureHandWrittenOverlayGlob(string manifestPath, out string? error)
        {
            error = null;
            ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

            string json;
            try
            {
                json = File.ReadAllText(manifestPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(json);
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }

            if (node is not JsonObject root)
            {
                error = manifestPath;
                return false;
            }

            var overlay = ComposerExtraTyhpPackageManifest.GetOrCreateOverlayArray(
                root,
                ComposerExtraTyhpPackageManifest.IsComposerJsonFileName(manifestPath));

            foreach (var item in overlay)
            {
                if (item is JsonValue value
                    && value.TryGetValue<string>(out var pattern)
                    && CoversHandWrittenOverlay(pattern))
                {
                    return true;
                }
            }

            overlay.Add(HandWrittenOverlayGlob);

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(manifestPath, root.ToJsonString(options) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
                return false;
            }

            return true;
        }

        public static bool CoversHandWrittenOverlay(string? pattern)
        {
            var normalized = NormalizeGlob(pattern);
            return normalized == "_tyhpdef/overlays/*.tyhpdef"
                || normalized == "_tyhpdef/overlays/**/*.tyhpdef";
        }

        public static string NormalizeGlob(string? pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return "";
            }

            var normalized = pattern.Replace('\\', '/').Trim();
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized[2..];
            }

            return normalized;
        }
    }
}
