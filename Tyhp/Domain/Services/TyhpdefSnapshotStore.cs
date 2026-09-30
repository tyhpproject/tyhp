namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Per-minor Reflection JSON under <c>tyhpdef_gen/snapshots/{minor}/</c>.
    /// </summary>
    internal static class TyhpdefSnapshotStore
    {
        public static string ResolveDirectory(TyhpdefGenerationOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!string.IsNullOrWhiteSpace(options.SnapshotDirectory))
            {
                return Path.GetFullPath(options.SnapshotDirectory);
            }

            return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "tyhpdef_gen", "snapshots"));
        }

        public static string FilePath(string snapshotDirectory, string minor, string extensionName)
        {
            var normalized = PhpRuntimeVersion.ToMinor(minor) ?? (minor ?? "").Trim();
            return Path.Combine(snapshotDirectory, normalized, Sanitize(extensionName) + ".json");
        }

        public static bool TryRead(string path, out string json)
        {
            json = "";
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            json = File.ReadAllText(path);
            return json.Length > 0;
        }

        public static void Write(string path, string json)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, json);
        }

        public static bool IsHandOverlayPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalized = Path.GetFullPath(path).Replace('\\', '/');
            var fileName = Path.GetFileName(normalized);
            if (!fileName.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            const string marker = "/_tyhpdef/overlays";
            var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            var after = normalized[(index + marker.Length)..];
            if (after.Length == 0
                || after.Equals("/stubs", StringComparison.OrdinalIgnoreCase)
                || after.StartsWith("/stubs/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return after.StartsWith('/');
        }

        private static string Sanitize(string extensionName)
        {
            var name = (extensionName ?? "").Trim();
            if (name.Length == 0)
            {
                return "unknown";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name.ToLowerInvariant();
        }
    }
}
