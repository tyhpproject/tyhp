using System.Text.Json;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;
using Tyhp.Domain.Services;
using Tyhp.TyhpLang.Ast;
using Tyhp.TyhpLang.Enum;

namespace Tyhp.TyhpLang.Binder.BuiltIn
{
    public static partial class Tyhpdef
    {
        private const int PackageLoadOrderBase = 100;
        private const string EmbeddedPackageSource = "<embedded>";

        private static void LoadPackageTyhpdefs(
            List<TyhpdefSourceFile> results,
            DiagnosticBag diagnostics,
            TyhpdefLoadContext context,
            HashSet<string> loadedPaths
        )
        {
            var manifestPaths = DiscoverPackageManifestPaths(context.Options).ToList();
            var superseded = new Dictionary<string, TyhpdefPackageOwnership.SupersededImpl>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var item in TyhpdefPackageOwnership.FindImplsSupersededByBundledPhp(manifestPaths))
            {
                superseded[item.ManifestPath] = item;
            }

            var loadOrder = PackageLoadOrderBase;

            foreach (var manifestPath in manifestPaths)
            {
                if (superseded.TryGetValue(manifestPath, out var skipped))
                {
                    diagnostics.AddWarning(
                        MessageCode.TyhpdefBundledPackagePreferred,
                        manifestPath,
                        0,
                        0,
                        skipped.PhpPackageName,
                        skipped.ImplPackageName);
                    continue;
                }

                TrackLoadedPackage(manifestPath, context);
                LoadPackageManifest(manifestPath, loadOrder++, results, diagnostics, loadedPaths, context);
            }
        }

        private static IEnumerable<string> DiscoverPackageManifestPaths(CompilationOptions? options)
        {
            var manifests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var phpVersion = GetConfiguredPhpVersion(options);

            // Package dependencies are resolved strictly relative to the project's tyhp.json
            // location — never from the compiler binary directory or the current working
            // directory, and without walking up to ancestor monorepo roots. A project sees only:
            //   1. vendor/<vendor>/<package>/composer.json with extra.tyhp.package as an object, and
            //   2. composer.json files listed explicitly via tyhpdefInclude / include that have
            //      that same sentinel. The compiling project's composer.json is not auto-loaded.
            // There is no silent scan of runtime/packages.
            // extra.tyhp.impl on a public metapackage is not a load path. extra.tyhp.tyhpdef
            // names a sibling package; that sibling loads through its own extra.tyhp.package
            // once it is installed. When a PHP package and tyhpdef/<vendor>-<name>-impl both
            // have extra.tyhp.package, the PHP package wins (TYHP7523).
            var projectRoot = GetProjectRoot(options);

            CollectVendorPackageManifests(projectRoot, manifests, phpVersion);
            CollectIncludedPackageManifests(projectRoot, options, manifests);

            return manifests.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
        }

        private static void CollectVendorPackageManifests(
            string projectRoot,
            HashSet<string> manifests,
            string phpVersion
        )
        {
            var vendorRoot = Path.Combine(projectRoot, "vendor");
            if (!Directory.Exists(vendorRoot))
            {
                return;
            }

            foreach (var vendorDir in Directory.EnumerateDirectories(vendorRoot))
            {
                var vendorName = Path.GetFileName(vendorDir);

                foreach (var packageDir in Directory.EnumerateDirectories(vendorDir))
                {
                    var packageDirName = Path.GetFileName(packageDir);
                    // Legacy tyhp/php-8.2 or tyhpdef/php-8.2 directories only — not php-ext-* stubs.
                    var isLegacyVersionedPhp = (string.Equals(vendorName, "tyhp", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(vendorName, "tyhpdef", StringComparison.OrdinalIgnoreCase))
                        && TryGetLegacyPhpVendorMinor(packageDirName, out _);
                    if (isLegacyVersionedPhp
                        && !IsMatchingPhpExtensionVendorPackage(packageDirName, phpVersion))
                    {
                        continue;
                    }

                    var manifestPath = Path.Combine(
                        packageDir,
                        ComposerExtraTyhpPackageManifest.ComposerJsonFileName);
                    if (ComposerExtraTyhpPackageManifest.HasPackageObject(manifestPath))
                    {
                        manifests.Add(PathCanonicalizer.GetCanonicalFullPath(manifestPath));
                    }
                }
            }
        }

        /// <summary>
        /// Collects <c>composer.json</c> paths from the project's explicit
        /// <c>tyhpdefInclude</c> / <c>include</c> patterns. Direct paths and globs are both
        /// supported (e.g. <c>./runtime/packages/core/composer.json</c>). Files that are not
        /// <c>composer.json</c> or that lack <c>extra.tyhp.package</c> as an object are
        /// skipped. Raw <c>.tyhpdef</c>/<c>.tyhp</c> includes are handled separately by
        /// <see cref="LoadUserTyhpdefs"/>.
        /// </summary>
        private static void CollectIncludedPackageManifests(
            string projectRoot,
            CompilationOptions? options,
            HashSet<string> manifests
        )
        {
            if (options?.TyhpdefIncludePaths == null || options.TyhpdefIncludePaths.Count == 0)
            {
                return;
            }

            foreach (var includePattern in options.TyhpdefIncludePaths)
            {
                if (!IncludePatternMayMatchPackageManifest(includePattern))
                {
                    continue;
                }

                foreach (var matchedPath in ResolveIncludePattern(projectRoot, includePattern))
                {
                    if (ShouldLoadExplicitComposerManifest(matchedPath))
                    {
                        manifests.Add(PathCanonicalizer.GetCanonicalFullPath(matchedPath));
                    }
                }
            }
        }

        private static bool IncludePatternMayMatchPackageManifest(string includePattern)
            => ComposerExtraTyhpPackageManifest.PatternMayMatchComposerJson(includePattern);

        private static bool IsPackageManifestPath(string path)
            => ComposerExtraTyhpPackageManifest.IsComposerJsonFileName(path);

        private static bool ShouldLoadExplicitComposerManifest(string path)
        {
            if (!IsPackageManifestPath(path))
            {
                return false;
            }

            return ComposerExtraTyhpPackageManifest.Inspect(path) switch
            {
                ComposerExtraTyhpPackageManifest.InspectResult.HasPackage
                    or ComposerExtraTyhpPackageManifest.InspectResult.InvalidJson
                    or ComposerExtraTyhpPackageManifest.InspectResult.Unreadable => true,
                _ => false,
            };
        }

        private static void LoadPackageManifest(
            string manifestPath,
            int loadOrder,
            List<TyhpdefSourceFile> results,
            DiagnosticBag diagnostics,
            HashSet<string> loadedPaths,
            TyhpdefLoadContext context
        )
        {
            string manifestContent;
            try
            {
                manifestContent = File.ReadAllText(manifestPath);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.AddError(MessageCode.TyhpdefParseError, manifestPath, 0, 0, ex.Message);
                return;
            }

            string packageRoot;
            try
            {
                packageRoot = PathCanonicalizer.GetCanonicalFullPath(Path.GetDirectoryName(manifestPath)
                    ?? throw new InvalidOperationException("Package manifest has no directory."));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, manifestPath, 0, 0, ex.Message);
                return;
            }

            JsonElement package;
            try
            {
                using var document = JsonDocument.Parse(manifestContent);
                if (!ComposerExtraTyhpPackageManifest.TryGetPackageElement(document.RootElement, out package))
                {
                    return;
                }

                package = package.Clone();
            }
            catch (JsonException ex)
            {
                diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, manifestPath, 0, 0, ex.Message);
                return;
            }

            IReadOnlyList<string> includePatterns;
            IReadOnlyList<string> excludePatterns;
            try
            {
                includePatterns = ComposerExtraTyhpPackageManifest.ReadStringArray(package, "include");
                excludePatterns = ComposerExtraTyhpPackageManifest.ReadStringArray(package, "exclude");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, manifestPath, 0, 0, ex.Message);
                return;
            }

            var excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var excludePattern in excludePatterns)
            {
                foreach (var matchedPath in ResolveIncludePattern(packageRoot, excludePattern))
                {
                    excludedPaths.Add(matchedPath);
                }
            }

            // Honor the package's own tagless setting: a package may publish its type
            // definition files with or without open tags independently of the consuming
            // project's source.tagless configuration.
            var tagless = ComposerExtraTyhpPackageManifest.TryReadTagless(package);

            foreach (var includePattern in includePatterns)
            {
                foreach (var matchedPath in ResolveIncludePattern(packageRoot, includePattern))
                {
                    if (excludedPaths.Contains(matchedPath))
                    {
                        continue;
                    }

                    TryLoadPackageFile(
                        matchedPath,
                        packageRoot,
                        loadOrder,
                        results,
                        diagnostics,
                        loadedPaths,
                        tagless);
                }
            }

            if (context.Options?.ApplyTyhpdefOverlays == false)
            {
                return;
            }

            IReadOnlyList<string> overlayPatterns;
            try
            {
                overlayPatterns = ComposerExtraTyhpPackageManifest.ReadStringArray(package, "overlay");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, manifestPath, 0, 0, ex.Message);
                return;
            }

            foreach (var overlayPattern in overlayPatterns)
            {
                foreach (var matchedPath in ResolveIncludePattern(packageRoot, overlayPattern)
                    .OrderBy(static path => path, StringComparer.Ordinal))
                {
                    TryLoadPackageFile(
                        matchedPath,
                        packageRoot,
                        loadOrder,
                        results,
                        diagnostics,
                        loadedPaths,
                        tagless,
                        isOverlay: true,
                        overlaySequence: context.NextOverlaySequence++);
                }
            }
        }

        private static IEnumerable<string> ResolveIncludePattern(string packageRoot, string includePattern)
        {
            var normalizedPattern = includePattern.Replace('\\', '/').Trim();
            while (normalizedPattern.StartsWith("./", StringComparison.Ordinal))
            {
                normalizedPattern = normalizedPattern[2..];
            }

            if (string.IsNullOrWhiteSpace(normalizedPattern))
            {
                yield break;
            }

            if (!normalizedPattern.Contains('*', StringComparison.Ordinal))
            {
                var directPath = PathCanonicalizer.GetCanonicalFullPath(Path.Combine(packageRoot, normalizedPattern));
                if (File.Exists(directPath))
                {
                    yield return directPath;
                }

                yield break;
            }

            var recursive = normalizedPattern.Contains("**", StringComparison.Ordinal);
            var searchRoot = packageRoot;
            var filePattern = normalizedPattern;

            var wildcardIndex = normalizedPattern.IndexOf('*', StringComparison.Ordinal);
            if (wildcardIndex > 0)
            {
                var directoryPart = normalizedPattern[..wildcardIndex].TrimEnd('/');
                if (!string.IsNullOrEmpty(directoryPart))
                {
                    searchRoot = PathCanonicalizer.GetCanonicalFullPath(Path.Combine(packageRoot, directoryPart));
                }
            }

            if (!Directory.Exists(searchRoot))
            {
                yield break;
            }

            var enumOptions = new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            foreach (var candidatePath in Directory.EnumerateFiles(searchRoot, "*", enumOptions))
            {
                var fileName = Path.GetFileName(candidatePath);
                if (!MatchesGlobFileName(fileName, filePattern))
                {
                    continue;
                }

                // Honor the extension in the glob (e.g. `**/*.tyhpdef` must not also match `.tyhp`).
                // A bare `*`/`**` pattern may still match either definition or source overlay files.
                var patternExtension = GetGlobFileExtension(filePattern);
                if (!string.IsNullOrEmpty(patternExtension))
                {
                    if (!candidatePath.EndsWith(patternExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                else if (!candidatePath.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                    && !candidatePath.EndsWith(".tyhp", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return PathCanonicalizer.GetCanonicalFullPath(candidatePath);
            }
        }

        private static string? GetGlobFileExtension(string pattern)
        {
            var filePattern = pattern.Replace('\\', '/');
            var slash = filePattern.LastIndexOf('/');
            if (slash >= 0)
            {
                filePattern = filePattern[(slash + 1)..];
            }

            // `*.tyhpdef` / `**/*.tyhpdef` → `.tyhpdef`
            if (filePattern.StartsWith("*.", StringComparison.Ordinal))
            {
                return filePattern[1..];
            }

            var dot = filePattern.LastIndexOf('.');
            if (dot > 0 && !filePattern.Contains('*', StringComparison.Ordinal))
            {
                return filePattern[dot..];
            }

            return null;
        }

        private static bool MatchesGlobFileName(string fileName, string pattern)
        {
            pattern = pattern.Replace('\\', '/');

            // Collapse `dir/**/*.ext` (and repeated `**/`) down to the final file glob segment.
            while (pattern.Contains("**/", StringComparison.Ordinal))
            {
                var idx = pattern.IndexOf("**/", StringComparison.Ordinal);
                pattern = pattern[(idx + 3)..];
            }

            var slash = pattern.LastIndexOf('/');
            if (slash >= 0)
            {
                pattern = pattern[(slash + 1)..];
            }

            if (pattern.StartsWith("*.", StringComparison.Ordinal))
            {
                return fileName.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(pattern, "*", StringComparison.Ordinal)
                || string.Equals(pattern, "**", StringComparison.Ordinal))
            {
                return true;
            }

            if (pattern.Contains('*', StringComparison.Ordinal))
            {
                // Conservative: only accept other wildcard forms when the extension still matches.
                var expectedExt = GetGlobFileExtension(pattern);
                return expectedExt == null
                    || fileName.EndsWith(expectedExt, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static void TryLoadPackageFile(
            string filePath,
            string packageSource,
            int loadOrder,
            List<TyhpdefSourceFile> results,
            DiagnosticBag diagnostics,
            HashSet<string> loadedPaths,
            bool tagless = false,
            bool isOverlay = false,
            int overlaySequence = 0
        )
        {
            var normalizedPath = PathCanonicalizer.GetCanonicalFullPath(filePath);
            if (!loadedPaths.Add(normalizedPath))
            {
                return;
            }

            var parseMode = Path.GetExtension(normalizedPath).Equals(".tyhp", StringComparison.OrdinalIgnoreCase)
                ? ParseMode.Tyhp
                : ParseMode.Tyhpdef;

            try
            {
                var content = File.ReadAllText(normalizedPath);
                var ast = ParseContent(content, normalizedPath, parseMode, diagnostics, tagless);
                if (ast == null)
                {
                    diagnostics.AddError(MessageCode.TyhpdefInvalidFormat, normalizedPath, 0, 0, normalizedPath);
                    return;
                }

                results.Add(new TyhpdefSourceFile
                {
                    Ast = ast,
                    PackageSource = packageSource,
                    LoadOrder = loadOrder,
                    IsOverlay = isOverlay,
                    OverlaySequence = overlaySequence,
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                diagnostics.AddError(MessageCode.TyhpdefParseError, normalizedPath, 0, 0, ex.Message);
            }
        }

        /// <summary>
        /// The single project root used to resolve package dependencies: the directory that
        /// contains the project's <c>tyhp.json</c> (<see cref="CompilationOptions.ProjectPath"/>),
        /// falling back to the current directory only when no project file is configured.
        /// </summary>
        private static string GetProjectRoot(CompilationOptions? options)
        {
            var projectPath = options?.ProjectPath;
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                projectPath = Directory.GetCurrentDirectory();
            }

            try
            {
                return PathCanonicalizer.GetCanonicalFullPath(projectPath);
            }
            catch (Exception)
            {
                return projectPath;
            }
        }

        private static void AddEmbeddedSource(
            List<TyhpdefSourceFile> results,
            SrcFileAst ast,
            string embeddedKey
        )
        {
            results.Add(new TyhpdefSourceFile
            {
                Ast = ast,
                PackageSource = $"{EmbeddedPackageSource}:{embeddedKey}",
                LoadOrder = 0,
            });
        }
    }
}
