using System.Text.Json;

namespace Tyhp.TyhpLang.Interop
{
    /// <summary>
    /// Tyhp ↔ PHP interop contract version (Story 15). Separate from compiler semver and
    /// Composer package versions — see <c>docs/content/cli_interopContract.md</c>.
    /// </summary>
    public static class InteropContract
    {
        /// <summary>
        /// Current contract version the compiler requires of runtime packages.
        /// Bump when an emitted name or required runtime signature changes incompatibly.
        /// </summary>
        public const int CurrentVersion = 1;

        /// <summary>Composer package names that publish the interop contract surface.</summary>
        public static readonly IReadOnlyList<string> RuntimePackageNames =
        [
            "tyhp/core",
            "tyhp/decimal",
            "tyhp/async",
            "tyhp/lambda",
        ];

        /// <summary>
        /// Reads <c>extra.tyhp.interopContractVersion</c> from a package <c>composer.json</c>.
        /// </summary>
        /// <returns>
        /// <see langword="true"/> when the property is present and a JSON number;
        /// otherwise <see langword="false"/>.
        /// </returns>
        public static bool TryReadVersionFromComposerJson(string composerJsonPath, out int version)
        {
            version = 0;
            if (string.IsNullOrWhiteSpace(composerJsonPath) || !File.Exists(composerJsonPath))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(composerJsonPath));
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!document.RootElement.TryGetProperty("extra", out var extra)
                    || extra.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!extra.TryGetProperty("tyhp", out var tyhp)
                    || tyhp.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!tyhp.TryGetProperty("interopContractVersion", out var versionElement))
                {
                    return false;
                }

                if (versionElement.ValueKind == JsonValueKind.Number
                    && versionElement.TryGetInt32(out version))
                {
                    return true;
                }

                if (versionElement.ValueKind == JsonValueKind.String
                    && int.TryParse(versionElement.GetString(), out version))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
            }

            return false;
        }

        /// <summary>
        /// Resolves the on-disk <c>composer.json</c> for a runtime package: path-repo under
        /// <c>runtime/packages/</c>, else <c>vendor/&lt;name&gt;/composer.json</c> under
        /// <paramref name="outputDirectory"/>.
        /// </summary>
        public static string? ResolvePackageComposerJson(
            string packageName,
            IReadOnlyDictionary<string, string> runtimePackagePathMap,
            string? outputDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(packageName))
            {
                return null;
            }

            if (runtimePackagePathMap.TryGetValue(packageName, out var packageDir)
                && !string.IsNullOrWhiteSpace(packageDir))
            {
                var pathRepoManifest = Path.Combine(packageDir, "composer.json");
                if (File.Exists(pathRepoManifest))
                {
                    return pathRepoManifest;
                }
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                return null;
            }

            var vendorManifest = Path.Combine(
                outputDirectory,
                "vendor",
                packageName.Replace('/', Path.DirectorySeparatorChar),
                "composer.json");
            return File.Exists(vendorManifest) ? vendorManifest : null;
        }
    }
}
