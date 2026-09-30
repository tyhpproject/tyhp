using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Collects PHP files for <c>--package-path</c> from a package's
    /// <c>composer.json</c> autoload (and <c>autoload-dev</c> when requested).
    /// </summary>
    public static class ComposerAutoloadPhpCollector
    {
        public sealed record Result
        {
            public IReadOnlyList<string> Files { get; init; } = [];

            public string PackageName { get; init; } = "";

            /// <summary>Localization key when collection failed; null on success.</summary>
            public string? ErrorKey { get; init; }

            public object[] ErrorArgs { get; init; } = [];
        }

        /// <summary>
        /// Reads <c>composer.json</c> under <paramref name="packagePath"/> and returns every
        /// autoloaded path. Nested <c>vendor/</c> directories inside the package are skipped.
        /// Non-<c>.php</c> collected paths are included so the caller can report TYHP7507.
        /// </summary>
        public static Result Collect(string packagePath, bool includeDev)
        {
            if (string.IsNullOrWhiteSpace(packagePath))
            {
                return Fail("CLI_TyhpdefMissingComposerJson", packagePath ?? "");
            }

            string root;
            try
            {
                root = Path.GetFullPath(packagePath.Trim());
            }
            catch (Exception)
            {
                return Fail("CLI_TyhpdefMissingComposerJson", packagePath);
            }

            if (!Directory.Exists(root))
            {
                return Fail("CLI_TyhpdefMissingComposerJson", packagePath);
            }

            var composerPath = Path.Combine(root, "composer.json");
            if (!File.Exists(composerPath))
            {
                return Fail("CLI_TyhpdefMissingComposerJson", packagePath);
            }

            JsonObject rootNode;
            try
            {
                var json = File.ReadAllText(composerPath);
                rootNode = JsonNode.Parse(json)?.AsObject()
                    ?? throw new JsonException("composer.json root is not an object");
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
                return Fail("CLI_TyhpdefMissingComposerJson", packagePath);
            }

            var packageName = GetString(rootNode["name"]) ?? "";
            var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var hadMapping = CollectAutoloadSection(root, rootNode["autoload"] as JsonObject, files);
            if (includeDev)
            {
                hadMapping |= CollectAutoloadSection(root, rootNode["autoload-dev"] as JsonObject, files);
            }

            if (!hadMapping)
            {
                return Fail("CLI_TyhpdefNoAutoloadPaths", packagePath);
            }

            return new Result
            {
                Files = files.ToList(),
                PackageName = packageName,
            };
        }

        private static Result Fail(string key, params object[] args)
            => new() { ErrorKey = key, ErrorArgs = args };

        private static bool CollectAutoloadSection(
            string packageRoot,
            JsonObject? autoload,
            SortedSet<string> files)
        {
            if (autoload is null)
            {
                return false;
            }

            var any = false;
            any |= CollectPsrMap(packageRoot, autoload["psr-4"], files);
            any |= CollectPsrMap(packageRoot, autoload["psr-0"], files);
            any |= CollectClassmap(packageRoot, autoload["classmap"] as JsonArray, files);
            any |= CollectFilesList(packageRoot, autoload["files"] as JsonArray, files);
            return any;
        }

        private static bool CollectPsrMap(string packageRoot, JsonNode? node, SortedSet<string> files)
        {
            if (node is not JsonObject map || map.Count == 0)
            {
                return false;
            }

            var any = false;
            foreach (var property in map)
            {
                foreach (var relative in EnumeratePathValues(property.Value))
                {
                    any = true;
                    CollectPhpUnder(packageRoot, relative, files);
                }
            }

            return any;
        }

        private static bool CollectClassmap(string packageRoot, JsonArray? entries, SortedSet<string> files)
        {
            if (entries is null || entries.Count == 0)
            {
                return false;
            }

            var any = false;
            foreach (var entry in entries)
            {
                var relative = GetString(entry);
                if (string.IsNullOrWhiteSpace(relative))
                {
                    continue;
                }

                any = true;
                var full = CombinePackagePath(packageRoot, relative);
                if (Directory.Exists(full))
                {
                    CollectPhpUnderDirectory(packageRoot, full, files);
                    continue;
                }

                if (File.Exists(full) || full.Length > 0)
                {
                    AddCollectedPath(packageRoot, full, files);
                }
            }

            return any;
        }

        private static bool CollectFilesList(string packageRoot, JsonArray? entries, SortedSet<string> files)
        {
            if (entries is null || entries.Count == 0)
            {
                return false;
            }

            var any = false;
            foreach (var entry in entries)
            {
                var relative = GetString(entry);
                if (string.IsNullOrWhiteSpace(relative))
                {
                    continue;
                }

                any = true;
                var full = CombinePackagePath(packageRoot, relative);
                AddCollectedPath(packageRoot, full, files);
            }

            return any;
        }

        private static void CollectPhpUnder(string packageRoot, string relative, SortedSet<string> files)
        {
            var full = CombinePackagePath(packageRoot, relative);
            if (Directory.Exists(full))
            {
                CollectPhpUnderDirectory(packageRoot, full, files);
                return;
            }

            if (File.Exists(full))
            {
                AddCollectedPath(packageRoot, full, files);
            }
        }

        private static void CollectPhpUnderDirectory(string packageRoot, string directory, SortedSet<string> files)
        {
            IEnumerable<string> phpFiles;
            try
            {
                phpFiles = Directory.EnumerateFiles(directory, "*.php", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            foreach (var path in phpFiles)
            {
                AddCollectedPath(packageRoot, path, files);
            }
        }

        private static void AddCollectedPath(string packageRoot, string path, SortedSet<string> files)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                return;
            }

            if (IsInsideNestedVendor(packageRoot, full))
            {
                return;
            }

            files.Add(full);
        }

        /// <summary>
        /// True when <paramref name="fullPath"/> sits under a <c>vendor/</c> directory that is
        /// nested inside the package (not the package root itself when the package lives in vendor).
        /// </summary>
        internal static bool IsInsideNestedVendor(string packageRoot, string fullPath)
        {
            string root;
            string path;
            try
            {
                root = Path.GetFullPath(packageRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                path = Path.GetFullPath(fullPath);
            }
            catch (Exception)
            {
                return false;
            }

            var relative = Path.GetRelativePath(root, path);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                return false;
            }

            var parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("vendor", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> EnumeratePathValues(JsonNode? node)
        {
            if (node is JsonValue)
            {
                var text = GetString(node);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    yield return text;
                }

                yield break;
            }

            if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    var text = GetString(item);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        yield return text;
                    }
                }
            }
        }

        private static string CombinePackagePath(string packageRoot, string relative)
        {
            var normalized = relative.Replace('/', Path.DirectorySeparatorChar).Trim();
            if (Path.IsPathRooted(normalized))
            {
                return normalized;
            }

            return Path.GetFullPath(Path.Combine(packageRoot, normalized));
        }

        private static string? GetString(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                return text;
            }

            return null;
        }
    }
}
