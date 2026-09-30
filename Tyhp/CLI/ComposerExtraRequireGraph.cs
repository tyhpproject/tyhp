using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tyhp.Domain.Services;

namespace Tyhp.CLI
{
    /// <summary>
    /// Local-only <c>extra.tyhp.require</c> closure over root <c>composer.json</c> and
    /// <c>vendor/composer/installed.json</c>. Mirrors the Phase 5 plugin merge rules without
    /// talking to Packagist.
    /// </summary>
    internal static class ComposerExtraRequireGraph
    {
        internal const string CompilerPackage = "tyhp/compiler";
        internal const string CorePackage = "tyhp/core";
        internal const string DefaultCompilerConstraint = "@dev";

        internal sealed class PackageMeta
        {
            public required string Name { get; init; }

            public IReadOnlyDictionary<string, string> Require { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyDictionary<string, string> ExtraRequire { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        internal sealed class DisjointPin
        {
            public required string Package { get; init; }

            public required string ExtraConstraint { get; init; }

            public required string ExistingConstraint { get; init; }
        }

        internal sealed class Report
        {
            public required string ComposerJsonPath { get; init; }

            public string? ParseError { get; init; }

            public IReadOnlyList<DisjointPin> Disjoint { get; init; } = [];

            public IReadOnlyList<string> MissingExtras { get; init; } = [];

            public IReadOnlyList<string> UninstalledNamedPackages { get; init; } = [];

            public bool CompilerGap { get; init; }

            public bool HasCore { get; init; }

            public IReadOnlyDictionary<string, string> RootRequire { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyDictionary<string, string> RootRequireDev { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyDictionary<string, string> MergedRequireDev { get; init; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyList<string> PackagesToUpdate { get; init; } = [];

            public bool NoComposerJson { get; init; }

            public bool HasBlockingErrors
                => this.ParseError is not null
                    || this.Disjoint.Count > 0
                    || this.MissingExtras.Count > 0
                    || this.UninstalledNamedPackages.Count > 0;

            public bool IsStale => this.HasBlockingErrors || this.CompilerGap;
        }

        public static Report Analyze(string projectRoot)
        {
            var composerPath = Path.Combine(projectRoot, "composer.json");
            if (!File.Exists(composerPath))
            {
                return new Report
                {
                    ComposerJsonPath = composerPath,
                    NoComposerJson = true,
                };
            }

            JsonObject root;
            try
            {
                var parsed = JsonNode.Parse(File.ReadAllText(composerPath));
                if (parsed is not JsonObject obj)
                {
                    return new Report
                    {
                        ComposerJsonPath = composerPath,
                        ParseError = "root is not a JSON object",
                    };
                }

                root = obj;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return new Report
                {
                    ComposerJsonPath = composerPath,
                    ParseError = ex.Message,
                };
            }

            var rootRequire = ReadConstraintMap(root["require"] as JsonObject);
            var rootRequireDev = ReadConstraintMap(root["require-dev"] as JsonObject);
            var rootExtra = ReadExtraTyhpRequire(root);
            var rootPackageName = ReadRootPackageName(root);
            var catalog = LoadInstalledCatalog(projectRoot);
            var disjoint = new List<DisjointPin>();

            Dictionary<string, string> extras;
            HashSet<string> seen;
            try
            {
                (extras, seen) = Resolve(rootRequire, rootRequireDev, rootExtra, catalog, disjoint);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return new Report
                {
                    ComposerJsonPath = composerPath,
                    ParseError = ex.Message,
                    RootRequire = rootRequire,
                    RootRequireDev = rootRequireDev,
                };
            }

            var hasCore = seen.Contains(CorePackage)
                || rootRequire.ContainsKey(CorePackage)
                || rootRequireDev.ContainsKey(CorePackage)
                || catalog.ContainsKey(CorePackage);

            if (hasCore
                && !extras.ContainsKey(CompilerPackage)
                && !IsSelfRequire(CompilerPackage, rootPackageName))
            {
                extras[CompilerPackage] = ResolveDefaultCompilerConstraint();
            }

            var mergeDisjoint = new List<DisjointPin>();
            var mergedRequireDev = MergeOntoRequireDev(
                rootRequire,
                rootRequireDev,
                extras,
                mergeDisjoint,
                rootPackageName);
            foreach (var pin in mergeDisjoint)
            {
                if (!disjoint.Any(d =>
                        string.Equals(d.Package, pin.Package, StringComparison.OrdinalIgnoreCase)))
                {
                    disjoint.Add(pin);
                }
            }

            var missingExtras = new List<string>();
            foreach (var (name, _) in extras)
            {
                if (string.Equals(name, CompilerPackage, StringComparison.OrdinalIgnoreCase)
                    || IsSelfRequire(name, rootPackageName))
                {
                    continue;
                }

                if (ComposerPlatformPackages.IsPlatform(name))
                {
                    continue;
                }

                var inRoot = rootRequire.ContainsKey(name) || rootRequireDev.ContainsKey(name);
                var installed = catalog.ContainsKey(name);
                if (!inRoot || !installed)
                {
                    missingExtras.Add(name);
                }
            }

            missingExtras.Sort(StringComparer.OrdinalIgnoreCase);

            var uninstalledNamed = new List<string>();
            foreach (var name in rootRequire.Keys.Concat(rootRequireDev.Keys))
            {
                if (!IsTyhpVendorPackage(name) || ComposerPlatformPackages.IsPlatform(name))
                {
                    continue;
                }

                if (string.Equals(name, CompilerPackage, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!catalog.ContainsKey(name)
                    && !uninstalledNamed.Contains(name, StringComparer.OrdinalIgnoreCase)
                    && !missingExtras.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    uninstalledNamed.Add(name);
                }
            }

            uninstalledNamed.Sort(StringComparer.OrdinalIgnoreCase);

            var compilerInRequireDev = rootRequireDev.ContainsKey(CompilerPackage);
            var compilerInstalled = catalog.ContainsKey(CompilerPackage);
            var compilerGap = hasCore
                && !IsSelfRequire(CompilerPackage, rootPackageName)
                && (!compilerInRequireDev || !compilerInstalled);

            var packagesToUpdate = new List<string>();
            foreach (var (name, _) in mergedRequireDev)
            {
                if (rootRequire.ContainsKey(name))
                {
                    continue;
                }

                if (!rootRequireDev.ContainsKey(name) || !catalog.ContainsKey(name))
                {
                    packagesToUpdate.Add(name);
                }
            }

            packagesToUpdate.Sort(StringComparer.OrdinalIgnoreCase);

            return new Report
            {
                ComposerJsonPath = composerPath,
                Disjoint = disjoint,
                MissingExtras = missingExtras,
                UninstalledNamedPackages = uninstalledNamed,
                CompilerGap = compilerGap,
                HasCore = hasCore,
                RootRequire = rootRequire,
                RootRequireDev = rootRequireDev,
                MergedRequireDev = mergedRequireDev,
                PackagesToUpdate = packagesToUpdate,
            };
        }

        internal static (Dictionary<string, string> Extras, HashSet<string> Seen) Resolve(
            IReadOnlyDictionary<string, string> rootRequire,
            IReadOnlyDictionary<string, string> rootRequireDev,
            IReadOnlyDictionary<string, string> rootExtra,
            IReadOnlyDictionary<string, PackageMeta> catalog,
            List<DisjointPin> disjoint)
        {
            var constraints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();

            void Enqueue(string name, string constraint)
            {
                name = NormalizeName(name);
                if (name.Length == 0)
                {
                    return;
                }

                if (!constraints.ContainsKey(name))
                {
                    constraints[name] = string.IsNullOrWhiteSpace(constraint) ? "*" : constraint;
                }

                queue.Enqueue(name);
            }

            foreach (var (name, constraint) in rootRequire)
            {
                Enqueue(name, constraint);
            }

            foreach (var (name, constraint) in rootRequireDev)
            {
                Enqueue(name, constraint);
            }

            foreach (var (name, constraint) in rootExtra)
            {
                Enqueue(name, constraint);
            }

            var extras = MergeExtras(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), rootExtra, disjoint);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (queue.Count > 0)
            {
                var pkg = queue.Dequeue();
                if (seen.Contains(pkg))
                {
                    continue;
                }

                if (ComposerPlatformPackages.IsPlatform(pkg))
                {
                    continue;
                }

                seen.Add(pkg);
                if (!catalog.TryGetValue(pkg, out var meta))
                {
                    continue;
                }

                extras = MergeExtras(extras, meta.ExtraRequire, disjoint);
                foreach (var (name, constraint) in meta.ExtraRequire)
                {
                    Enqueue(name, constraint);
                }

                foreach (var (name, constraint) in meta.Require)
                {
                    Enqueue(name, constraint);
                }
            }

            return (extras, seen);
        }

        internal static Dictionary<string, string> MergeExtras(
            Dictionary<string, string> extras,
            IReadOnlyDictionary<string, string> incoming,
            List<DisjointPin> disjoint)
        {
            foreach (var (rawName, constraint) in incoming)
            {
                var name = NormalizeName(rawName);
                if (name.Length == 0 || constraint is null)
                {
                    continue;
                }

                if (!extras.TryGetValue(name, out var existing))
                {
                    extras[name] = constraint;
                    continue;
                }

                if (ComposerConstraintCompatibility.AreCompatible(existing, constraint))
                {
                    continue;
                }

                disjoint.Add(new DisjointPin
                {
                    Package = name,
                    ExtraConstraint = constraint,
                    ExistingConstraint = existing,
                });
            }

            return extras;
        }

        internal static Dictionary<string, string> MergeOntoRequireDev(
            IReadOnlyDictionary<string, string> rootRequire,
            IReadOnlyDictionary<string, string> rootRequireDev,
            IReadOnlyDictionary<string, string> extras,
            List<DisjointPin> disjoint,
            string? rootPackageName = null)
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, constraint) in rootRequireDev)
            {
                var normalized = NormalizeName(name);
                if (IsSelfRequire(normalized, rootPackageName))
                {
                    continue;
                }

                merged[normalized] = constraint;
            }

            foreach (var (rawName, extraConstraint) in extras)
            {
                var name = NormalizeName(rawName);
                if (name.Length == 0 || extraConstraint is null)
                {
                    continue;
                }

                if (IsSelfRequire(name, rootPackageName) || rootRequire.ContainsKey(name))
                {
                    continue;
                }

                if (!merged.TryGetValue(name, out var existing))
                {
                    merged[name] = extraConstraint;
                    continue;
                }

                if (ComposerConstraintCompatibility.AreCompatible(existing, extraConstraint))
                {
                    continue;
                }

                disjoint.Add(new DisjointPin
                {
                    Package = name,
                    ExtraConstraint = extraConstraint,
                    ExistingConstraint = existing,
                });
            }

            return merged;
        }

        internal static Dictionary<string, PackageMeta> LoadInstalledCatalog(string projectRoot)
        {
            var catalog = new Dictionary<string, PackageMeta>(StringComparer.OrdinalIgnoreCase);
            var vendorDirectory = Path.Combine(projectRoot, "vendor");
            var installedPath = ComposerInstalledInventory.InstalledJsonPath(vendorDirectory);
            if (!File.Exists(installedPath))
            {
                return catalog;
            }

            try
            {
                foreach (var package in ComposerInstalledInventory.ReadInstalledPackages(vendorDirectory))
                {
                    var meta = ReadPackageMeta(package.Name, package.InstallPath);
                    catalog[NormalizeName(package.Name)] = meta;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // installed.json is optional for a first-require miss; the report still sees
                // root pins that are not installed.
            }

            return catalog;
        }

        internal static PackageMeta ReadPackageMeta(string name, string installPath)
        {
            var require = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(installPath))
            {
                return new PackageMeta { Name = name, Require = require, ExtraRequire = extra };
            }

            var composerPath = Path.Combine(installPath, "composer.json");
            if (!File.Exists(composerPath))
            {
                return new PackageMeta { Name = name, Require = require, ExtraRequire = extra };
            }

            try
            {
                if (JsonNode.Parse(File.ReadAllText(composerPath)) is JsonObject root)
                {
                    require = ReadConstraintMap(root["require"] as JsonObject);
                    extra = ReadExtraTyhpRequire(root);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
            }

            return new PackageMeta
            {
                Name = name,
                Require = require,
                ExtraRequire = extra,
            };
        }

        internal static Dictionary<string, string> ReadExtraTyhpRequire(JsonObject root)
        {
            var extra = root["extra"] as JsonObject;
            var tyhp = extra?["tyhp"] as JsonObject;
            return ReadConstraintMap(tyhp?["require"] as JsonObject);
        }

        internal static Dictionary<string, string> ReadConstraintMap(JsonObject? section)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (section is null)
            {
                return map;
            }

            foreach (var property in section)
            {
                var key = NormalizeName(property.Key);
                if (key.Length == 0)
                {
                    continue;
                }

                if (property.Value is JsonValue jsonValue
                    && jsonValue.TryGetValue<string>(out var text)
                    && text is not null)
                {
                    map[key] = text;
                }
            }

            return map;
        }

        internal static string ResolveDefaultCompilerConstraint()
        {
            var resolved = ComposerJsonService.ResolveCompilerPackageVersion();
            return string.IsNullOrWhiteSpace(resolved) ? DefaultCompilerConstraint : resolved;
        }

        internal static bool IsTyhpVendorPackage(string name)
            => name.StartsWith("tyhp/", StringComparison.OrdinalIgnoreCase);

        internal static string NormalizeName(string name)
            => name.Trim().ToLowerInvariant();

        internal static string ReadRootPackageName(JsonObject root)
        {
            if (root["name"] is JsonValue jsonValue
                && jsonValue.TryGetValue<string>(out var text)
                && text is not null)
            {
                return NormalizeName(text);
            }

            return "";
        }

        /// <summary>
        /// Composer forbids a root package from requiring itself. Empty and the
        /// Composer unnamed-root sentinel are not real package names.
        /// </summary>
        internal static bool IsSelfRequire(string packageName, string? rootPackageName)
        {
            packageName = NormalizeName(packageName);
            rootPackageName = string.IsNullOrWhiteSpace(rootPackageName)
                ? ""
                : NormalizeName(rootPackageName);
            if (packageName.Length == 0
                || rootPackageName.Length == 0
                || string.Equals(rootPackageName, "__root__", StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(packageName, rootPackageName, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Whether two Composer constraint strings have a non-empty intersection.
    /// Same classification as <c>runtime/packages/core/plugin/ConstraintCompatibility.php</c>.
    /// </summary>
    internal static class ComposerConstraintCompatibility
    {
        public static bool AreCompatible(string left, string right)
        {
            left = left.Trim();
            right = right.Trim();
            if (string.Equals(left, right, StringComparison.Ordinal))
            {
                return true;
            }

            if (left.Length == 0 || right.Length == 0
                || left == "*" || right == "*")
            {
                return true;
            }

            return HeuristicCompatible(left, right);
        }

        private static bool HeuristicCompatible(string left, string right)
        {
            if (IsDevAlias(left) || IsDevAlias(right))
            {
                return true;
            }

            var leftMajor = CaretOrTildeMajor(left);
            var rightMajor = CaretOrTildeMajor(right);
            if (leftMajor is not null && rightMajor is not null && leftMajor != rightMajor)
            {
                return false;
            }

            return true;
        }

        private static bool IsDevAlias(string constraint)
            => constraint == "@dev"
                || constraint.StartsWith("dev-", StringComparison.Ordinal)
                || constraint.EndsWith("@dev", StringComparison.Ordinal);

        private static int? CaretOrTildeMajor(string constraint)
        {
            var match = Regex.Match(constraint, @"^[\^~]\s*(\d+)");
            if (!match.Success)
            {
                return null;
            }

            return int.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.Integer);
        }
    }

    /// <summary>
    /// Composer platform packages are never walked. Same regex as
    /// <c>runtime/packages/core/plugin/PlatformPackages.php</c>.
    /// </summary>
    internal static class ComposerPlatformPackages
    {
        private static readonly Regex FallbackRegex = new(
            @"^(?:php(?:-64bit|-ipv6|-zts|-debug)?|hhvm|(?:ext|lib)-[a-z0-9](?:[_.-]?[a-z0-9]+)*|composer(?:-(?:plugin|runtime)-api)?)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static bool IsPlatform(string name)
            => FallbackRegex.IsMatch(name);
    }
}
