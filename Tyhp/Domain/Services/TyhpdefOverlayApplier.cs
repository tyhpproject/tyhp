using System.Text.Json;
using Tyhp.CLI;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Apply overlay tyhpdefs for <c>--verify</c> comparison only. This is not Story 21 binder overlay load.
    /// Overlay order: <c>extra.tyhp.package.overlay</c> on <c>composer.json</c> when present; otherwise CLI convention
    /// (<c>overlays/stubs/*.tyhpdef</c> then <c>overlays/*.tyhpdef</c>, non-recursive).
    /// Last wins; <c>omit</c> hides a golden symbol (not a fail).
    /// </summary>
    public static class TyhpdefOverlayApplier
    {
        public const string PackageManifestFileName = ComposerExtraTyhpPackageManifest.ComposerJsonFileName;

        public static IReadOnlyList<string> ResolveOverlayPaths(string outputDirectory, DiagnosticBag diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);
            var root = Path.GetFullPath(outputDirectory);
            var manifestPath = Path.Combine(root, PackageManifestFileName);
            if (ComposerExtraTyhpPackageManifest.HasPackageObject(manifestPath))
            {
                return ExpandManifestOverlays(manifestPath, root, diagnostics);
            }

            return ExpandCliConvention(root);
        }

        public static IReadOnlyList<string> ResolveBaselinePaths(string outputDirectory, DiagnosticBag diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);
            var root = Path.GetFullPath(outputDirectory);
            var manifestPath = Path.Combine(root, PackageManifestFileName);
            if (ComposerExtraTyhpPackageManifest.HasPackageObject(manifestPath))
            {
                return ExpandManifestIncludes(manifestPath, root, diagnostics);
            }

            return EnumerateBaselineFiles(root);
        }

        public static TyhpdefApiIndex Apply(
            TyhpdefApiIndex baseline,
            IReadOnlyList<string> overlayPaths,
            DiagnosticBag diagnostics)
        {
            ArgumentNullException.ThrowIfNull(baseline);
            ArgumentNullException.ThrowIfNull(diagnostics);

            foreach (var path in overlayPaths)
            {
                TyhpdefAstApiReader.MergeOverlayFile(baseline, path, diagnostics);
            }

            return baseline;
        }

        private static List<string> ExpandCliConvention(string root)
        {
            var paths = new List<string>();
            var stubsDir = Path.Combine(root, "overlays", "stubs");
            AddNonRecursiveTyhpdefs(stubsDir, paths);

            var overlaysDir = Path.Combine(root, "overlays");
            if (Directory.Exists(overlaysDir))
            {
                foreach (var file in Directory.EnumerateFiles(overlaysDir, "*.tyhpdef", SearchOption.TopDirectoryOnly)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(Path.GetFullPath(file));
                }
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> EnumerateBaselineFiles(string root)
        {
            if (!Directory.Exists(root))
            {
                return [];
            }

            var overlays = Path.Combine(root, "overlays");
            var overlayPrefix = overlays + Path.DirectorySeparatorChar;
            return Directory.EnumerateFiles(root, "*.tyhpdef", SearchOption.AllDirectories)
                .Where(path =>
                {
                    var full = Path.GetFullPath(path);
                    return !full.StartsWith(overlayPrefix, StringComparison.OrdinalIgnoreCase);
                })
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static List<string> ExpandManifestOverlays(string manifestPath, string root, DiagnosticBag diagnostics)
            => ExpandManifestArray(manifestPath, root, "overlay", diagnostics, required: false);

        private static List<string> ExpandManifestIncludes(string manifestPath, string root, DiagnosticBag diagnostics)
        {
            var includes = ExpandManifestArray(manifestPath, root, "include", diagnostics, required: false);
            var excludes = new HashSet<string>(
                ExpandManifestArray(manifestPath, root, "exclude", diagnostics, required: false),
                StringComparer.OrdinalIgnoreCase);
            if (excludes.Count == 0)
            {
                return includes;
            }

            return includes.Where(p => !excludes.Contains(p)).ToList();
        }

        private static List<string> ExpandManifestArray(
            string manifestPath,
            string root,
            string property,
            DiagnosticBag diagnostics,
            bool required)
        {
            string json;
            try
            {
                json = File.ReadAllText(manifestPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                diagnostics.AddError(MessageCode.TyhpdefParseError, manifestPath, 0, 0, ex.Message);
                return [];
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, manifestPath, 0, 0, ex.Message);
                return [];
            }

            using (document)
            {
                if (!ComposerExtraTyhpPackageManifest.TryGetPackageElement(document.RootElement, out var package))
                {
                    return [];
                }

                if (!package.TryGetProperty(property, out var element)
                    || element.ValueKind != JsonValueKind.Array)
                {
                    if (required)
                    {
                        diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, manifestPath, 0, 0, manifestPath);
                    }

                    return [];
                }

                var paths = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var pattern = item.GetString();
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        continue;
                    }

                    ExpandGlob(root, pattern, paths);
                }

                return paths
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        private static void ExpandGlob(string root, string pattern, List<string> paths)
        {
            var normalized = pattern.Replace('\\', '/').Trim();
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized[2..];
            }

            if (normalized.Length == 0)
            {
                return;
            }

            if (!normalized.Contains('*', StringComparison.Ordinal))
            {
                var direct = Path.GetFullPath(Path.Combine(root, normalized));
                if (File.Exists(direct))
                {
                    paths.Add(direct);
                }

                return;
            }

            var recursive = normalized.Contains("**", StringComparison.Ordinal);
            var searchRoot = root;
            var filePattern = "*.tyhpdef";
            var slash = normalized.LastIndexOf('/');
            if (slash >= 0)
            {
                var directoryPart = normalized[..slash].Replace("**", "").Trim('/');
                if (directoryPart.Length > 0 && !directoryPart.Contains('*', StringComparison.Ordinal))
                {
                    searchRoot = Path.GetFullPath(Path.Combine(root, directoryPart));
                }

                filePattern = normalized[(slash + 1)..];
            }
            else
            {
                filePattern = normalized;
            }

            if (!Directory.Exists(searchRoot))
            {
                return;
            }

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            try
            {
                foreach (var file in Directory.EnumerateFiles(searchRoot, filePattern, option)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (file.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(Path.GetFullPath(file));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Message.Debug(ex.Message);
            }
        }

        private static void AddNonRecursiveTyhpdefs(string directory, List<string> paths)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*.tyhpdef", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(Path.GetFullPath(file));
            }
        }
    }
}
