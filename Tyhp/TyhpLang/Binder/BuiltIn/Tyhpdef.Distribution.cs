using Microsoft.Extensions.FileSystemGlobbing;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    public static partial class Tyhpdef
    {
        private const int UserTyhpdefLoadOrder = 300;

        private static readonly string[] ExpectedRuntimePackages =
        {
            "core",
            "decimal",
            "async",
            "lambda",
        };

        private sealed class TyhpdefLoadContext
        {
            public CompilationOptions? Options { get; init; }
            public bool PhpExtensionLoaded { get; set; }
            public HashSet<string> LoadedRuntimePackages { get; } = new(StringComparer.OrdinalIgnoreCase);
            public int NextOverlaySequence { get; set; } = 1;
        }

        private static void LoadUserTyhpdefs(
            List<TyhpdefSourceFile> results,
            DiagnosticBag diagnostics,
            TyhpdefLoadContext context,
            HashSet<string> loadedPaths
        )
        {
            var options = context.Options;
            if (options?.TyhpdefIncludePaths == null || options.TyhpdefIncludePaths.Count == 0)
            {
                return;
            }

            // Tyhpdef overlay files (e.g. PHP extension definitions) are resolved strictly
            // relative to the project's tyhp.json directory. The pattern resolver below supports
            // parent-relative paths ("../") and recursive globs, neither of which the default
            // FileSystemGlobbing matcher handles when the target lies outside the project root.
            var projectRoot = GetProjectRoot(options);

            foreach (var includePattern in options.TyhpdefIncludePaths)
            {
                foreach (var matchedPath in ResolveIncludePattern(projectRoot, includePattern))
                {
                    // composer.json package manifests are loaded via DiscoverPackageManifestPaths /
                    // LoadPackageManifest — skip them here so JSON is not parsed as tyhpdef.
                    if (IsPackageManifestPath(matchedPath))
                    {
                        continue;
                    }

                    var beforeCount = results.Count;
                    TryLoadPackageFile(
                        matchedPath,
                        "project:tyhp.json",
                        UserTyhpdefLoadOrder,
                        results,
                        diagnostics,
                        loadedPaths,
                        options.Tagless);

                    if (results.Count > beforeCount && IsPhpExtensionTyhpdefPath(matchedPath))
                    {
                        context.PhpExtensionLoaded = true;
                    }
                }
            }

            if (options.ApplyTyhpdefOverlays == false
                || options.TyhpdefOverlayPaths == null
                || options.TyhpdefOverlayPaths.Count == 0)
            {
                return;
            }

            foreach (var overlayPattern in options.TyhpdefOverlayPaths)
            {
                foreach (var matchedPath in ResolveIncludePattern(projectRoot, overlayPattern)
                    .OrderBy(static path => path, StringComparer.Ordinal))
                {
                    if (IsPackageManifestPath(matchedPath))
                    {
                        continue;
                    }

                    TryLoadPackageFile(
                        matchedPath,
                        "project:tyhp.json",
                        UserTyhpdefLoadOrder,
                        results,
                        diagnostics,
                        loadedPaths,
                        options.Tagless,
                        isOverlay: true,
                        overlaySequence: context.NextOverlaySequence++);
                }
            }
        }

        private static void ApplyTyhpdefExcludes(List<TyhpdefSourceFile> results, CompilationOptions? options)
        {
            if (options?.TyhpdefExcludePaths == null || options.TyhpdefExcludePaths.Count == 0)
            {
                return;
            }

            var projectPath = options.ProjectPath;
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                projectPath = Directory.GetCurrentDirectory();
            }

            projectPath = PathCanonicalizer.GetCanonicalFullPath(projectPath);

            var matcher = new Matcher();
            matcher.AddIncludePatterns(options.TyhpdefExcludePaths);
            var excludedPaths = new HashSet<string>(
                matcher.GetResultsInFullPath(projectPath).Select(PathCanonicalizer.GetCanonicalFullPath),
                StringComparer.OrdinalIgnoreCase);

            results.RemoveAll(source =>
            {
                var fileName = source.Ast?.FileName;
                return !string.IsNullOrEmpty(fileName)
                    && excludedPaths.Contains(PathCanonicalizer.GetCanonicalFullPath(fileName));
            });
        }

        private static void ReportMissingTyhpdefPackages(TyhpdefLoadContext context, DiagnosticBag diagnostics)
        {
            if (!context.PhpExtensionLoaded)
            {
                diagnostics.AddWarning(
                    MessageCode.TyhpdefPhpExtensionPackageNotFound,
                    "tyhp.json",
                    0,
                    0);
            }

            foreach (var runtimePackage in ExpectedRuntimePackages)
            {
                if (!context.LoadedRuntimePackages.Contains(runtimePackage))
                {
                    diagnostics.AddWarning(
                        MessageCode.TyhpdefRuntimePackageNotFound,
                        "tyhp.json",
                        0,
                        0,
                        runtimePackage);
                }
            }
        }

        private static string GetConfiguredPhpVersion(CompilationOptions? options)
            => string.IsNullOrWhiteSpace(options?.PhpVersion)
                ? CompilationOptions.DefaultPhpVersionWhenUnset
                : options.PhpVersion;

        private static bool VersionMatchesPhpTarget(string candidateVersion, string targetVersion)
        {
            var candidateParts = candidateVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var targetParts = targetVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);

            if (candidateParts.Length < 2 || targetParts.Length < 2)
            {
                return string.Equals(candidateVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(candidateParts[0], targetParts[0], StringComparison.Ordinal)
                && string.Equals(candidateParts[1], targetParts[1], StringComparison.Ordinal);
        }

        /// <summary>
        /// Legacy per-minor Composer directory <c>php-8.2</c> (not <c>php-ext-curl</c>).
        /// </summary>
        private static bool TryGetLegacyPhpVendorMinor(string packageDirName, out string minor)
        {
            minor = "";
            if (!packageDirName.StartsWith("php-", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var rest = packageDirName["php-".Length..];
            var parts = rest.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2
                || !int.TryParse(parts[0], out _)
                || !int.TryParse(parts[1], out _))
            {
                return false;
            }

            minor = rest;
            return true;
        }

        private static bool IsMatchingPhpExtensionVendorPackage(string packageDirName, string phpVersion)
            => TryGetLegacyPhpVendorMinor(packageDirName, out var minor)
                && VersionMatchesPhpTarget(minor, phpVersion);

        private static bool IsPhpExtensionVendorPackage(string packageDirName)
            => string.Equals(packageDirName, "php", StringComparison.OrdinalIgnoreCase)
                || packageDirName.StartsWith("php-", StringComparison.OrdinalIgnoreCase);

        private static bool IsPhpExtensionTyhpdefPath(string path)
        {
            var normalized = path.Replace('\\', '/');
            return normalized.Contains("/packages/php/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/tyhpdef/php/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/tyhp/php/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Composer <c>name</c> <c>tyhp/core</c> wins over directory spelling so path-repo
        /// installs such as <c>dist/tyhp-core/805.0.1</c> still count as the runtime package.
        /// </summary>
        private static string? TryGetRuntimePackageName(string manifestPath)
        {
            var fromComposer = TryReadTyhpComposerPackageShortName(manifestPath);
            if (!string.IsNullOrEmpty(fromComposer))
            {
                return fromComposer;
            }

            var packageDir = Path.GetFileName(Path.GetDirectoryName(manifestPath));
            if (string.IsNullOrEmpty(packageDir))
            {
                return null;
            }

            var parentDir = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(manifestPath)));
            if (string.Equals(parentDir, "tyhp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parentDir, "packages", StringComparison.OrdinalIgnoreCase))
            {
                return packageDir;
            }

            return null;
        }

        private static string? TryReadTyhpComposerPackageShortName(string manifestPath)
        {
            var directory = Path.GetDirectoryName(manifestPath);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            var composerJson = Path.Combine(directory, "composer.json");
            if (!File.Exists(composerJson))
            {
                return null;
            }

            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(composerJson));
                if (!document.RootElement.TryGetProperty("name", out var nameElement)
                    || nameElement.ValueKind != System.Text.Json.JsonValueKind.String)
                {
                    return null;
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name)
                    || !name.StartsWith("tyhp/", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var shortName = name["tyhp/".Length..];
                return string.IsNullOrWhiteSpace(shortName) ? null : shortName;
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
            {
                return null;
            }
        }

        private static void TrackLoadedPackage(string manifestPath, TyhpdefLoadContext context)
        {
            var packageDirName = Path.GetFileName(Path.GetDirectoryName(manifestPath));
            if (string.IsNullOrEmpty(packageDirName))
            {
                return;
            }

            var composerShortName = TryReadTyhpComposerPackageShortName(manifestPath);
            var phpVersion = GetConfiguredPhpVersion(context.Options);
            if (string.Equals(composerShortName, "php", StringComparison.OrdinalIgnoreCase)
                || string.Equals(packageDirName, "php", StringComparison.OrdinalIgnoreCase)
                || (packageDirName.StartsWith("php-", StringComparison.OrdinalIgnoreCase)
                    && IsMatchingPhpExtensionVendorPackage(packageDirName, phpVersion))
                || IsPhpExtensionTyhpdefPath(manifestPath))
            {
                context.PhpExtensionLoaded = true;
            }

            var runtimePackage = TryGetRuntimePackageName(manifestPath);
            if (!string.IsNullOrEmpty(runtimePackage)
                && !IsPhpExtensionVendorPackage(runtimePackage))
            {
                context.LoadedRuntimePackages.Add(runtimePackage);
            }
        }
    }
}
