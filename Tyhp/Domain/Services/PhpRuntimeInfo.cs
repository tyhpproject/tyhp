namespace Tyhp.Domain.Services
{
    /// <summary>
    /// A PHP CLI Tyhp can invoke for Reflection harvest.
    /// </summary>
    public sealed class PhpRuntimeInfo
    {
        public required string Path { get; init; }

        /// <summary>Full <c>PHP_VERSION</c>, e.g. <c>8.3.11</c>.</summary>
        public required string Version { get; init; }

        /// <summary>Tyhp-generated ini path. Null for a user <c>--php</c> binary.</summary>
        public string? IniPath { get; init; }

        public IReadOnlyList<string> LoadedExtensions { get; init; } = [];

        public bool IsManaged { get; init; }

        public string? Provider { get; init; }

        public string? ArtifactUrl { get; init; }

        public string? ExtensionDir { get; init; }

        /// <summary>Major.minor, e.g. <c>8.3</c>.</summary>
        public string MinorVersion
            => PhpRuntimeVersion.ToMinor(this.Version) ?? this.Version;

        public bool HasExtension(string extensionName)
        {
            if (string.IsNullOrWhiteSpace(extensionName))
            {
                return false;
            }

            return this.LoadedExtensions.Any(loaded =>
                string.Equals(loaded, extensionName, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static class PhpRuntimeVersion
    {
        public static readonly string[] SupportedMinors = ["8.2", "8.3", "8.4", "8.5"];

        public static string? ToMinor(string? version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var trimmed = version.Trim().TrimStart('v', 'V');
            var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                return null;
            }

            if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            {
                return null;
            }

            return $"{major}.{minor}";
        }

        public static bool TryParsePatch(string? version, out int major, out int minor, out int patch)
        {
            major = 0;
            minor = 0;
            patch = 0;
            if (string.IsNullOrWhiteSpace(version))
            {
                return false;
            }

            var trimmed = version.Trim().TrimStart('v', 'V');
            var dash = trimmed.IndexOf('-');
            if (dash >= 0)
            {
                trimmed = trimmed[..dash];
            }

            var parts = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2
                || !int.TryParse(parts[0], out major)
                || !int.TryParse(parts[1], out minor))
            {
                return false;
            }

            if (parts.Length >= 3)
            {
                int.TryParse(parts[2], out patch);
            }

            return true;
        }

        public static bool IsSupportedMinor(string? minor)
            => SupportedMinors.Contains(ToMinor(minor), StringComparer.Ordinal);

        public static int ComparePatch(string left, string right)
        {
            TryParsePatch(left, out var lMaj, out var lMin, out var lPat);
            TryParsePatch(right, out var rMaj, out var rMin, out var rPat);
            var cmp = lMaj.CompareTo(rMaj);
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = lMin.CompareTo(rMin);
            return cmp != 0 ? cmp : lPat.CompareTo(rPat);
        }
    }
}
