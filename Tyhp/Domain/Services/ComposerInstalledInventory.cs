using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Composer 2 <c>vendor/composer/installed.json</c> plus root <c>ext-*</c> requires.
    /// Does not walk <c>vendor/*/*</c>.
    /// </summary>
    public static class ComposerInstalledInventory
    {
        public sealed class Package
        {
            public required string Name { get; init; }

            public string Version { get; init; } = "";

            public string InstallPath { get; init; } = "";

            public string Type { get; init; } = "";
        }

        public sealed class Snapshot
        {
            public IReadOnlyList<Package> Packages { get; init; } = [];

            public IReadOnlyList<string> ExtensionNames { get; init; } = [];

            public string? RootPackageName { get; init; }

            public IReadOnlyDictionary<string, string> RootRequire { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyDictionary<string, string> RootRequireDev { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public bool TryGetInstalled(string packageName, out Package package)
            {
                foreach (var candidate in this.Packages)
                {
                    if (string.Equals(candidate.Name, packageName, StringComparison.OrdinalIgnoreCase))
                    {
                        package = candidate;
                        return true;
                    }
                }

                package = null!;
                return false;
            }

            public bool NamesPackage(string packageName)
                => this.RootRequire.ContainsKey(packageName)
                    || this.RootRequireDev.ContainsKey(packageName);

            public bool IsInstalled(string packageName)
                => this.TryGetInstalled(packageName, out _);
        }

        public static string InstalledJsonPath(string vendorDirectory)
            => Path.Combine(vendorDirectory, "composer", "installed.json");

        /// <summary>
        /// Install paths of packages named only in root <c>require-dev</c> (not also
        /// <c>require</c>). Those packages supply tyhpdefs to the consumer; their
        /// <c>.tyhp</c> sources are not part of the consumer lint/build file set.
        /// </summary>
        public static IReadOnlyList<string> GetRequireDevOnlyInstallPaths(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return [];
            }

            var vendorDirectory = Path.Combine(projectRoot, "vendor");
            var require = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var requireDev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            TryFillRootConstraints(projectRoot, require, requireDev);
            if (requireDev.Count == 0)
            {
                return [];
            }

            IReadOnlyList<Package> installed = [];
            try
            {
                if (File.Exists(InstalledJsonPath(vendorDirectory)))
                {
                    installed = ReadInstalledPackages(vendorDirectory);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Inventory is optional; fall back to vendor/{vendor}/{name}.
            }

            var roots = new List<string>();
            foreach (var name in requireDev.Keys)
            {
                if (require.ContainsKey(name) || IsPlatformPackageName(name))
                {
                    continue;
                }

                var installPath = FindInstalledPath(installed, name)
                    ?? TryVendorLayoutPath(vendorDirectory, name);
                if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
                {
                    continue;
                }

                roots.Add(installPath);
            }

            return roots;
        }

        public static Snapshot Load(string vendorDirectory, string projectRoot)
        {
            var packages = ReadInstalledPackages(vendorDirectory);
            var rootComposer = Path.Combine(projectRoot, "composer.json");
            var require = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var requireDev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? rootName = null;
            if (File.Exists(rootComposer))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(rootComposer))?.AsObject();
                    if (root is not null)
                    {
                        rootName = root["name"]?.GetValue<string>();
                        FillConstraintMap(root["require"] as JsonObject, require);
                        FillConstraintMap(root["require-dev"] as JsonObject, requireDev);
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
                {
                    // Root composer.json is optional for inventory of installed.json; ext-* scan
                    // just yields none when the file is unreadable.
                }
            }

            var extensions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectExtensions(require.Keys, extensions);
            CollectExtensions(requireDev.Keys, extensions);

            return new Snapshot
            {
                Packages = packages,
                ExtensionNames = extensions.ToList(),
                RootPackageName = rootName,
                RootRequire = require,
                RootRequireDev = requireDev,
            };
        }

        /// <summary>
        /// Package names in the order <c>vendor/composer/autoload_files.php</c> requires files.
        /// The first file of a package fixes that package's place. Root-package files use the
        /// root <c>composer.json</c> name when it is set. Returns null when the autoload file
        /// is missing or unreadable.
        /// </summary>
        public static IReadOnlyDictionary<string, int>? TryReadFilesAutoloadPackageOrder(string? projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return null;
            }

            var autoloadFile = Path.Combine(projectRoot, "vendor", "composer", "autoload_files.php");
            if (!File.Exists(autoloadFile))
            {
                return null;
            }

            string text;
            try
            {
                text = File.ReadAllText(autoloadFile);
            }
            catch (IOException)
            {
                return null;
            }

            var vendorDir = Path.GetFullPath(Path.Combine(projectRoot, "vendor"));
            var installed = new List<Package>();
            try
            {
                if (File.Exists(InstalledJsonPath(vendorDir)))
                {
                    installed = ReadInstalledPackages(vendorDir);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                installed = [];
            }

            string? rootName = null;
            var rootComposer = Path.Combine(projectRoot, "composer.json");
            if (File.Exists(rootComposer))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(rootComposer))?.AsObject();
                    rootName = root?["name"]?.GetValue<string>();
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
                {
                    rootName = null;
                }
            }

            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in FilesAutoloadEntry.Matches(text))
            {
                var variable = match.Groups[1].Value;
                var suffix = match.Groups[2].Value.TrimStart('/');
                var filePath = string.Equals(variable, "baseDir", StringComparison.Ordinal)
                    ? Path.GetFullPath(Path.Combine(projectRoot, suffix))
                    : Path.GetFullPath(Path.Combine(vendorDir, suffix));

                var packageName = PackageForAutoloadFile(filePath, installed, rootName);
                if (string.IsNullOrWhiteSpace(packageName) || order.ContainsKey(packageName))
                {
                    continue;
                }

                order[packageName] = order.Count;
            }

            return order;
        }

        private static readonly Regex FilesAutoloadEntry = new(
            @"\$(vendorDir|baseDir)\s*\.\s*'([^']*)'",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static string? PackageForAutoloadFile(
            string filePath,
            IReadOnlyList<Package> installed,
            string? rootName)
        {
            Package? best = null;
            var bestLength = -1;
            foreach (var package in installed)
            {
                if (string.IsNullOrWhiteSpace(package.InstallPath))
                {
                    continue;
                }

                var install = Path.GetFullPath(package.InstallPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (install.Length <= bestLength)
                {
                    continue;
                }

                var prefix = install + Path.DirectorySeparatorChar;
                if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(filePath, install, StringComparison.OrdinalIgnoreCase))
                {
                    best = package;
                    bestLength = install.Length;
                }
            }

            if (best is not null)
            {
                return best.Name;
            }

            return string.IsNullOrWhiteSpace(rootName) ? null : rootName;
        }

        public static List<Package> ReadInstalledPackages(string vendorDirectory)
        {
            var path = InstalledJsonPath(vendorDirectory);
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var packages = new List<Package>();
            var composerDir = Path.Combine(vendorDirectory, "composer");

            if (root.ValueKind == JsonValueKind.Array)
            {
                AddPackages(root, packages, vendorDirectory, composerDir);
                return packages;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return packages;
            }

            if (root.TryGetProperty("packages", out var main) && main.ValueKind == JsonValueKind.Array)
            {
                AddPackages(main, packages, vendorDirectory, composerDir);
            }

            if (root.TryGetProperty("packages-dev", out var dev) && dev.ValueKind == JsonValueKind.Array)
            {
                AddPackages(dev, packages, vendorDirectory, composerDir);
            }

            return packages;
        }

        private static void AddPackages(
            JsonElement array,
            List<Package> packages,
            string vendorDirectory,
            string composerDir)
        {
            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = ReadString(element, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var installPath = ResolveInstallPath(element, name, vendorDirectory, composerDir);
                if (string.IsNullOrWhiteSpace(installPath))
                {
                    continue;
                }

                packages.Add(new Package
                {
                    Name = name,
                    Version = ReadString(element, "version") ?? "",
                    InstallPath = installPath,
                    Type = ReadString(element, "type") ?? "",
                });
            }
        }

        private static string? ResolveInstallPath(
            JsonElement element,
            string packageName,
            string vendorDirectory,
            string composerDir)
        {
            var relative = ReadString(element, "install-path");
            if (!string.IsNullOrWhiteSpace(relative))
            {
                try
                {
                    return Path.GetFullPath(Path.Combine(composerDir, relative));
                }
                catch (Exception)
                {
                    return null;
                }
            }

            var slash = packageName.IndexOf('/');
            if (slash <= 0 || slash == packageName.Length - 1)
            {
                return null;
            }

            return Path.GetFullPath(Path.Combine(
                vendorDirectory,
                packageName[..slash],
                packageName[(slash + 1)..]));
        }

        private static string? ReadString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return value.GetString();
        }

        private static void FillConstraintMap(JsonObject? section, Dictionary<string, string> map)
        {
            if (section is null)
            {
                return;
            }

            foreach (var property in section)
            {
                var key = property.Key.Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                map[key] = property.Value?.GetValue<string>() ?? "";
            }
        }

        private static void CollectExtensions(IEnumerable<string> keys, SortedSet<string> extensions)
        {
            foreach (var key in keys)
            {
                if (key.StartsWith("ext-", StringComparison.OrdinalIgnoreCase) && key.Length > 4)
                {
                    extensions.Add(key[4..]);
                }
            }
        }

        private static void TryFillRootConstraints(
            string projectRoot,
            Dictionary<string, string> require,
            Dictionary<string, string> requireDev)
        {
            var rootComposer = Path.Combine(projectRoot, "composer.json");
            if (!File.Exists(rootComposer))
            {
                return;
            }

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(rootComposer))?.AsObject();
                if (root is null)
                {
                    return;
                }

                FillConstraintMap(root["require"] as JsonObject, require);
                FillConstraintMap(root["require-dev"] as JsonObject, requireDev);
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
            }
        }

        private static string? FindInstalledPath(IReadOnlyList<Package> installed, string packageName)
        {
            foreach (var package in installed)
            {
                if (string.Equals(package.Name, packageName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(package.InstallPath))
                {
                    return package.InstallPath;
                }
            }

            return null;
        }

        private static string? TryVendorLayoutPath(string vendorDirectory, string packageName)
        {
            var slash = packageName.IndexOf('/');
            if (slash <= 0 || slash == packageName.Length - 1)
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(Path.Combine(
                    vendorDirectory,
                    packageName[..slash],
                    packageName[(slash + 1)..]));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool IsPlatformPackageName(string name)
            => string.Equals(name, "php", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ext-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("lib-", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("composer-plugin-api", StringComparison.OrdinalIgnoreCase);
    }
}
