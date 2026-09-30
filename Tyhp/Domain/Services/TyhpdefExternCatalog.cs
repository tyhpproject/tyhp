using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// FQCN → providing <c>tyhpdef/*</c> wrapper, built from already-generated packages.
    /// Indexes real <c>class</c> / <c>interface</c> / <c>enum</c>, plus top-level
    /// <c>function</c> / <c>const</c> for compiled-library owner lookup. <c>extern</c>
    /// placeholders do not make the declaring file look like the provider.
    /// </summary>
    public sealed class TyhpdefExternCatalog
    {
        private static readonly object DefaultLock = new();
        private static string? DefaultRoot;
        private static TyhpdefExternCatalog? DefaultCache;

        private static readonly Regex TypeDeclaration = new(
            @"^\s*(?:(?:deprecated|obsolete|partial|abstract|final|readonly)\s+)*(extern\s+)?(class|interface|enum)\s+(\??\\?[A-Za-z_][A-Za-z0-9_\\]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex FunctionDeclaration = new(
            @"^\s*(?:(?:deprecated|obsolete)\s+)*(extern\s+)?function\s+&?(\??\\?[A-Za-z_][A-Za-z0-9_\\]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex ConstDeclaration = new(
            @"^\s*(?:(?:deprecated|obsolete)\s+)*(extern\s+)?const\s+(?:[A-Za-z_\\][A-Za-z0-9_\\]*\s+)?(\??\\?[A-Za-z_][A-Za-z0-9_\\]*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex NamespaceDeclaration = new(
            @"^\s*namespace\s+([A-Za-z_\\][A-Za-z0-9_\\]*)\s*([;{])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "vendor",
            "node_modules",
            ".git",
            "dist",
        };

        private readonly Dictionary<string, TyhpdefCatalogEntry> _byFqcn =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, TyhpdefCatalogEntry> _byFunction =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, TyhpdefCatalogEntry> _byConst =
            new(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _phpPackageToTyhp =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Real type FQCN (leading <c>\</c>) → catalog entry.</summary>
        public IReadOnlyDictionary<string, TyhpdefCatalogEntry> ByFqcn => this._byFqcn;

        /// <summary>Wrapped PHP package / <c>ext-*</c> / <c>php</c> → <c>tyhpdef/*</c> wrapper.</summary>
        public IReadOnlyDictionary<string, string> PhpPackageToTyhp => this._phpPackageToTyhp;

        public bool TryGet(string fqcn, out TyhpdefCatalogEntry entry)
        {
            var key = TyhpdefApiIndex.NormalizeFqn(fqcn);
            if (this._byFqcn.TryGetValue(key, out entry!))
            {
                return true;
            }

            entry = default!;
            return false;
        }

        /// <summary>Real function FQCN (leading <c>\</c>) → catalog entry. PHP-source harvest does not use this.</summary>
        public bool TryGetFunction(string fqn, out TyhpdefCatalogEntry entry)
        {
            var key = TyhpdefApiIndex.NormalizeFqn(fqn);
            if (this._byFunction.TryGetValue(key, out entry!))
            {
                return true;
            }

            entry = default!;
            return false;
        }

        /// <summary>Real const FQCN (leading <c>\</c>) → catalog entry. PHP-source harvest does not use this.</summary>
        public bool TryGetConst(string fqn, out TyhpdefCatalogEntry entry)
        {
            var key = TyhpdefApiIndex.NormalizeFqn(fqn);
            if (this._byConst.TryGetValue(key, out entry!))
            {
                return true;
            }

            entry = default!;
            return false;
        }

        public bool TryGetWrapperForPhpPackage(string phpPackage, out string tyhpPackage)
        {
            if (this._phpPackageToTyhp.TryGetValue(phpPackage.Trim(), out tyhpPackage!))
            {
                return true;
            }

            tyhpPackage = "";
            return false;
        }

        /// <summary>
        /// Unique last-segment names for global PHP types (<c>\DateTime</c>) from
        /// <c>php</c> / <c>ext-*</c> wrappers, and for <c>\Psr\*</c> types.
        /// Composer-library types are not unique globals. Colliding shorts are
        /// omitted so harvest never guesses <c>Builder</c> across packages.
        /// </summary>
        public IReadOnlyDictionary<string, string> UniqueGlobalAndPsrShortNames()
        {
            var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (fqcn, entry) in this._byFqcn)
            {
                var trimmed = (fqcn ?? "").Trim().TrimStart('\\');
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var slash = trimmed.IndexOf('\\');
                var isPsr = trimmed.StartsWith("Psr\\", StringComparison.OrdinalIgnoreCase);
                var isGlobal = slash < 0 && IsPhpBuiltinProvider(entry);
                if (!isGlobal && !isPsr)
                {
                    continue;
                }

                var shortName = slash < 0 ? trimmed : trimmed[(trimmed.LastIndexOf('\\') + 1)..];
                if (shortName.Length == 0 || PhpAstTypeExtractor.BuiltinTypes.Contains(shortName))
                {
                    continue;
                }

                if (!grouped.TryGetValue(shortName, out var list))
                {
                    list = [];
                    grouped[shortName] = list;
                }

                var normalized = "\\" + trimmed;
                if (!list.Exists(existing => existing.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Add(normalized);
                }
            }

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (shortName, list) in grouped)
            {
                if (list.Count == 1)
                {
                    result[shortName] = list[0];
                }
            }

            return result;
        }

        /// <summary>
        /// Builds a catalog from <paramref name="roots"/>, or from the resolved runtime
        /// packages root (<c>TYHP_RUNTIME_SRC</c>, then a sibling <c>tyhp-runtime-src/packages</c>)
        /// when <paramref name="roots"/> is null.
        /// An empty list yields an empty catalog.
        /// </summary>
        public static TyhpdefExternCatalog Build(IReadOnlyList<string>? roots)
        {
            if (roots is { Count: 0 })
            {
                return new TyhpdefExternCatalog();
            }

            if (roots is null)
            {
                var defaultRoot = ComposerJsonService.TryResolveRuntimePackagesRoot();
                if (string.IsNullOrWhiteSpace(defaultRoot))
                {
                    return new TyhpdefExternCatalog();
                }

                lock (DefaultLock)
                {
                    if (DefaultCache is not null
                        && string.Equals(DefaultRoot, defaultRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return DefaultCache;
                    }

                    DefaultRoot = defaultRoot;
                    DefaultCache = BuildFromRoots([defaultRoot]);
                    return DefaultCache;
                }
            }

            return BuildFromRoots(roots);
        }

        private static TyhpdefExternCatalog BuildFromRoots(IReadOnlyList<string> roots)
        {
            var catalog = new TyhpdefExternCatalog();
            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    continue;
                }

                foreach (var manifestPath in DiscoverManifests(root))
                {
                    catalog.IndexPackage(manifestPath);
                }
            }

            return catalog;
        }

        private void IndexPackage(string manifestPath)
        {
            string packageRoot;
            try
            {
                packageRoot = Path.GetFullPath(Path.GetDirectoryName(manifestPath) ?? "");
            }
            catch (Exception)
            {
                return;
            }

            if (packageRoot.Length == 0)
            {
                return;
            }

            var composerPath = Path.Combine(packageRoot, "composer.json");
            var tyhpName = ReadComposerName(composerPath);
            if (string.IsNullOrWhiteSpace(tyhpName)
                || !VendorTyhpdefLayout.IsTyhpdefPackage(tyhpName))
            {
                return;
            }

            var wrapped = ReadWrappedPhpPackages(composerPath, out var versionHint);
            foreach (var phpPackage in wrapped)
            {
                // TryAdd is itself the "first-registered wins" guard per PHP package;
                // gating the whole loop on wrapped[0] would skip later entries
                // (e.g. a second ext-* dependency) whenever an earlier one already
                // had a provider.
                this._phpPackageToTyhp.TryAdd(phpPackage, tyhpName);
            }

            // tyhpdef/php require is only "php". Always-present extensions (json, hash,
            // libxml, …) live in extra.tyhp.extensions and have no tyhpdef/php-ext-* package.
            // Register ext-<name> → this wrapper so harvest does not warn that ext-json
            // is unwrapped. Type classification still uses the require-derived list.
            if (wrapped.Count == 1
                && wrapped[0].Equals("php", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var extension in ReadAlwaysPresentTyhpExtensions(composerPath))
                {
                    this._phpPackageToTyhp.TryAdd("ext-" + extension, tyhpName);
                }
            }

            foreach (var tyhpdefPath in EnumerateManifestTyhpdefs(manifestPath, packageRoot))
            {
                this.IndexTyhpdefFile(tyhpdefPath, tyhpName, wrapped, versionHint);
            }
        }

        private void IndexTyhpdefFile(
            string path,
            string tyhpPackage,
            IReadOnlyList<string> wrappedPhpPackages,
            string versionHint)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            var currentNs = "";
            var namespaceStack = new Stack<(string Ns, int Depth)>();
            var depth = 0;
            var scanState = CodeScanState.Code;
            foreach (var rawLine in SplitLines(text))
            {
                var line = StripNonCode(rawLine, ref scanState);
                depth += CountChar(line, '{') - CountChar(line, '}');
                if (depth < 0)
                {
                    depth = 0;
                }

                while (namespaceStack.Count > 0 && depth < namespaceStack.Peek().Depth)
                {
                    namespaceStack.Pop();
                    currentNs = namespaceStack.Count == 0 ? "" : namespaceStack.Peek().Ns;
                }

                var nsMatch = NamespaceDeclaration.Match(line);
                if (nsMatch.Success)
                {
                    var name = nsMatch.Groups[1].Value.Trim().Trim('\\');
                    if (nsMatch.Groups[2].Value == ";")
                    {
                        currentNs = name;
                        namespaceStack.Clear();
                    }
                    else
                    {
                        currentNs = name;
                        namespaceStack.Push((name, depth));
                    }

                    continue;
                }

                var typeMatch = TypeDeclaration.Match(line);
                if (typeMatch.Success)
                {
                    this.TryIndexRealDeclaration(
                        typeMatch.Groups[1].Value,
                        typeMatch.Groups[2].Value.Trim().ToLowerInvariant(),
                        typeMatch.Groups[3].Value,
                        currentNs,
                        tyhpPackage,
                        wrappedPhpPackages,
                        versionHint,
                        this._byFqcn);
                    continue;
                }

                var namespaceBodyDepth = namespaceStack.Count == 0 ? 0 : namespaceStack.Peek().Depth;
                if (depth > namespaceBodyDepth)
                {
                    continue;
                }

                var functionMatch = FunctionDeclaration.Match(line);
                if (functionMatch.Success)
                {
                    this.TryIndexRealDeclaration(
                        functionMatch.Groups[1].Value,
                        "function",
                        functionMatch.Groups[2].Value,
                        currentNs,
                        tyhpPackage,
                        wrappedPhpPackages,
                        versionHint,
                        this._byFunction);
                    continue;
                }

                var constMatch = ConstDeclaration.Match(line);
                if (constMatch.Success)
                {
                    this.TryIndexRealDeclaration(
                        constMatch.Groups[1].Value,
                        "const",
                        constMatch.Groups[2].Value,
                        currentNs,
                        tyhpPackage,
                        wrappedPhpPackages,
                        versionHint,
                        this._byConst);
                }
            }
        }

        private void TryIndexRealDeclaration(
            string externToken,
            string kind,
            string rawName,
            string currentNs,
            string tyhpPackage,
            IReadOnlyList<string> wrappedPhpPackages,
            string versionHint,
            Dictionary<string, TyhpdefCatalogEntry> index)
        {
            if (!string.IsNullOrWhiteSpace(externToken)
                && externToken.Trim().StartsWith("extern", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            rawName = (rawName ?? "").Trim().TrimStart('?');
            if (rawName.Length == 0)
            {
                return;
            }

            var asAt = rawName.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
            if (asAt >= 0)
            {
                rawName = rawName[..asAt].Trim();
            }

            var fqcn = rawName.StartsWith('\\')
                ? TyhpdefApiIndex.NormalizeFqn(rawName)
                : TyhpdefApiIndex.Qualify(currentNs, rawName);
            if (fqcn.Length <= 1 || index.ContainsKey(fqcn))
            {
                return;
            }

            index[fqcn] = new TyhpdefCatalogEntry
            {
                Fqcn = fqcn,
                Kind = kind,
                TyhpPackage = tyhpPackage,
                WrappedPhpPackages = wrappedPhpPackages,
                VersionHint = versionHint,
            };
        }

        internal static List<string> ReadWrappedPhpPackages(string composerPath, out string versionHint)
        {
            versionHint = "*";
            var wrapped = new List<string>();
            JsonObject? root;
            try
            {
                if (!File.Exists(composerPath))
                {
                    return wrapped;
                }

                root = JsonNode.Parse(File.ReadAllText(composerPath))?.AsObject();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return wrapped;
            }

            if (root is null)
            {
                return wrapped;
            }

            var version = GetString(root["version"]);
            if (!string.IsNullOrWhiteSpace(version))
            {
                versionHint = version!;
            }

            var require = root["require"] as JsonObject;
            if (require is null)
            {
                return wrapped;
            }

            var ext = new List<string>();
            var phpLibs = new List<string>();
            var hasPhp = false;
            foreach (var property in require)
            {
                var key = property.Key.Trim();
                if (key.Length == 0)
                {
                    continue;
                }

                if (key.Equals("php", StringComparison.OrdinalIgnoreCase))
                {
                    hasPhp = true;
                    continue;
                }

                if (VendorTyhpdefLayout.IsFirstPartyComposerPackage(key))
                {
                    continue;
                }

                if (key.StartsWith("ext-", StringComparison.OrdinalIgnoreCase))
                {
                    ext.Add(key);
                    continue;
                }

                if (key.Contains('/', StringComparison.Ordinal))
                {
                    phpLibs.Add(key);
                }
            }

            if (ext.Count > 0)
            {
                wrapped.AddRange(ext);
            }
            else if (phpLibs.Count > 0)
            {
                wrapped.AddRange(phpLibs);
            }
            else if (hasPhp)
            {
                wrapped.Add("php");
            }

            return wrapped;
        }

        /// <summary>
        /// Always-present extension names from <c>extra.tyhp.extensions</c>
        /// (<c>json</c>, <c>hash</c>, <c>libxml</c>, …). Optional <c>php-ext-*</c>
        /// names are ignored so this map cannot steal <c>ext-curl</c>.
        /// </summary>
        internal static List<string> ReadAlwaysPresentTyhpExtensions(string composerPath)
        {
            var extensions = new List<string>();
            JsonObject? root;
            try
            {
                if (!File.Exists(composerPath))
                {
                    return extensions;
                }

                root = JsonNode.Parse(File.ReadAllText(composerPath))?.AsObject();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return extensions;
            }

            if (root?["extra"]?["tyhp"]?["extensions"] is not JsonArray array)
            {
                return extensions;
            }

            foreach (var item in array)
            {
                var name = GetString(item)?.Trim();
                if (string.IsNullOrEmpty(name)
                    || !VendorTyhpdefLayout.AlwaysPresentPhpExtensions.Contains(name))
                {
                    continue;
                }

                if (!extensions.Exists(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    extensions.Add(name);
                }
            }

            return extensions;
        }

        private static IEnumerable<string> DiscoverManifests(string root)
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(
                    root,
                    ComposerExtraTyhpPackageManifest.ComposerJsonFileName,
                    options);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                yield break;
            }

            foreach (var path in files)
            {
                if (IsInsideSkippedDirectory(root, path)
                    || !ComposerExtraTyhpPackageManifest.HasPackageObject(path))
                {
                    continue;
                }

                yield return path;
            }
        }

        private static bool IsInsideSkippedDirectory(string root, string fullPath)
        {
            string relative;
            try
            {
                relative = Path.GetRelativePath(root, fullPath);
            }
            catch (Exception)
            {
                return false;
            }

            var parts = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (SkippedDirectoryNames.Contains(parts[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> EnumerateManifestTyhpdefs(string manifestPath, string packageRoot)
        {
            JsonObject? root;
            try
            {
                root = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                yield break;
            }

            if (root is null
                || !ComposerExtraTyhpPackageManifest.TryGetPackageObject(root, out var package))
            {
                yield break;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var glob in ReadStringArray(package, "include").Concat(ReadStringArray(package, "overlay")))
            {
                foreach (var file in ExpandGlob(packageRoot, glob))
                {
                    if (seen.Add(file))
                    {
                        yield return file;
                    }
                }
            }
        }

        private static IEnumerable<string> ExpandGlob(string packageRoot, string glob)
        {
            var normalized = (glob ?? "").Replace('\\', '/').Trim();
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized[2..];
            }

            if (normalized.Length == 0)
            {
                yield break;
            }

            if (!normalized.Contains('*', StringComparison.Ordinal))
            {
                var direct = Path.GetFullPath(Path.Combine(packageRoot, normalized));
                if (File.Exists(direct)
                    && direct.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase))
                {
                    yield return direct;
                }

                yield break;
            }

            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(normalized);
            IEnumerable<string> matches;
            try
            {
                matches = matcher.GetResultsInFullPath(packageRoot);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                yield break;
            }

            foreach (var path in matches)
            {
                if (path.EndsWith(".tyhpdef", StringComparison.OrdinalIgnoreCase)
                    && !IsInsideSkippedDirectory(packageRoot, path))
                {
                    yield return path;
                }
            }
        }

        private static List<string> ReadStringArray(JsonObject root, string property)
        {
            var result = new List<string>();
            if (root[property] is not JsonArray array)
            {
                return result;
            }

            foreach (var item in array)
            {
                var text = GetString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(text!);
                }
            }

            return result;
        }

        private static string? ReadComposerName(string composerPath)
        {
            try
            {
                if (!File.Exists(composerPath))
                {
                    return null;
                }

                var root = JsonNode.Parse(File.ReadAllText(composerPath))?.AsObject();
                return GetString(root?["name"]);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return null;
            }
        }

        private static string? GetString(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                return text;
            }

            return null;
        }

        private static IEnumerable<string> SplitLines(string text)
            => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        private static bool IsPhpBuiltinProvider(TyhpdefCatalogEntry entry)
        {
            foreach (var wrapped in entry.WrappedPhpPackages)
            {
                if (wrapped.Equals("php", StringComparison.OrdinalIgnoreCase)
                    || wrapped.StartsWith("ext-", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Code-only text for namespace tracking: drop <c>//</c>, <c>/* */</c> /
        /// <c>/** */</c>, and quoted strings so braces in comments and strings
        /// are not treated as namespace delimiters.
        /// </summary>
        private static string StripNonCode(string line, ref CodeScanState state)
        {
            if (line.Length == 0)
            {
                if (state == CodeScanState.LineComment)
                {
                    state = CodeScanState.Code;
                }

                return "";
            }

            var chars = new char[line.Length];
            var n = 0;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                switch (state)
                {
                    case CodeScanState.LineComment:
                        break;
                    case CodeScanState.BlockComment:
                        if (c == '*' && i + 1 < line.Length && line[i + 1] == '/')
                        {
                            i++;
                            state = CodeScanState.Code;
                        }

                        break;
                    case CodeScanState.SingleString:
                        if (c == '\\' && i + 1 < line.Length)
                        {
                            i++;
                            break;
                        }

                        if (c == '\'')
                        {
                            state = CodeScanState.Code;
                        }

                        break;
                    case CodeScanState.DoubleString:
                        if (c == '\\' && i + 1 < line.Length)
                        {
                            i++;
                            break;
                        }

                        if (c == '"')
                        {
                            state = CodeScanState.Code;
                        }

                        break;
                    default:
                        if (c == '/' && i + 1 < line.Length)
                        {
                            var next = line[i + 1];
                            if (next == '/')
                            {
                                state = CodeScanState.LineComment;
                                i = line.Length;
                                break;
                            }

                            if (next == '*')
                            {
                                state = CodeScanState.BlockComment;
                                i++;
                                break;
                            }
                        }

                        if (c == '\'')
                        {
                            state = CodeScanState.SingleString;
                            break;
                        }

                        if (c == '"')
                        {
                            state = CodeScanState.DoubleString;
                            break;
                        }

                        chars[n++] = c;
                        break;
                }
            }

            if (state == CodeScanState.LineComment)
            {
                state = CodeScanState.Code;
            }

            return n == 0 ? "" : new string(chars, 0, n);
        }

        private enum CodeScanState
        {
            Code,
            LineComment,
            BlockComment,
            SingleString,
            DoubleString,
        }

        private static int CountChar(string text, char c)
        {
            var count = 0;
            foreach (var ch in text)
            {
                if (ch == c)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>One real type provided by an existing <c>tyhpdef/*</c> wrapper.</summary>
    public sealed class TyhpdefCatalogEntry
    {
        public string Fqcn { get; init; } = "";

        public string Kind { get; init; } = "class";

        public string TyhpPackage { get; init; } = "";

        public IReadOnlyList<string> WrappedPhpPackages { get; init; } = [];

        public string VersionHint { get; init; } = "*";
    }
}
