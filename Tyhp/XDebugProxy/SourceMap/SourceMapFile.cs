using System.Text.Json;

namespace Tyhp.XDebugProxy.SourceMap
{
    /// <summary>
    /// Parsed Source Map v3 document for one generated PHP file, with bidirectional lookups.
    /// </summary>
    public sealed partial class SourceMapFile
    {
        private IReadOnlyList<IReadOnlyList<MappingEntry>>? _decodedMappings;

        private SourceMapFile(
            int version,
            string file,
            string sourceRoot,
            string[] sources,
            string?[]? sourcesContent,
            string[] names,
            string mappings,
            string? mapFilePath)
        {
            Version = version;
            File = file;
            SourceRoot = sourceRoot;
            Sources = sources;
            SourcesContent = sourcesContent;
            Names = names;
            Mappings = mappings;
            MapFilePath = mapFilePath;
        }

        /// <summary>Source Map spec version (Tyhp emits 3).</summary>
        public int Version { get; }

        /// <summary>
        /// Generated PHP file name from the JSON <c>file</c> field (often filename-only).
        /// </summary>
        public string File { get; }

        /// <summary>JSON <c>sourceRoot</c> prefix prepended to relative <c>sources</c> entries.</summary>
        public string SourceRoot { get; }

        /// <summary>Original <c>.tyhp</c> paths as stored in the map (often relative to <see cref="SourceRoot"/>).</summary>
        public IReadOnlyList<string> Sources { get; }

        /// <summary>Embedded original sources, or <see langword="null"/> when the field was omitted.</summary>
        public IReadOnlyList<string?>? SourcesContent { get; }

        /// <summary>JSON <c>names</c> array (symbol names for 5-field segments).</summary>
        public IReadOnlyList<string> Names { get; }

        /// <summary>Raw VLQ <c>mappings</c> string.</summary>
        public string Mappings { get; }

        /// <summary>Filesystem path of the <c>.php.map</c> file, when loaded from disk.</summary>
        public string? MapFilePath { get; }

        /// <summary>
        /// Decoded mapping groups, one inner list per generated line (0-based). Decoded on first
        /// access and cached.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<MappingEntry>> DecodedMappings =>
            _decodedMappings ??= SourceMapDecoder.DecodeMappings(Mappings);

        /// <summary>
        /// Parse a Source Map v3 JSON document. Does not read the filesystem.
        /// </summary>
        /// <param name="json">Complete JSON object text.</param>
        /// <param name="mapFilePath">Optional path of the <c>.map</c> file this JSON came from.</param>
        /// <exception cref="ArgumentNullException"><paramref name="json"/> is null.</exception>
        /// <exception cref="FormatException">The JSON is not a valid Source Map v3 object.</exception>
        public static SourceMapFile Parse(string json, string? mapFilePath = null)
        {
            ArgumentNullException.ThrowIfNull(json);

            try
            {
                using var document = JsonDocument.Parse(json);
                return FromElement(document.RootElement, mapFilePath);
            }
            catch (JsonException ex)
            {
                throw new FormatException("Source map JSON is invalid.", ex);
            }
        }

        /// <summary>
        /// Read <paramref name="mapFilePath"/> as UTF-8 text and parse it as Source Map v3 JSON.
        /// </summary>
        public static SourceMapFile Load(string mapFilePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(mapFilePath);
            string json = System.IO.File.ReadAllText(mapFilePath);
            return Parse(json, mapFilePath);
        }

        /// <summary>
        /// True when this map describes <paramref name="phpFilePath"/>, matching the JSON
        /// <c>file</c> field, the <c>&lt;phpfile&gt;.map</c> convention, or filename-only.
        /// </summary>
        public bool MatchesGeneratedFile(string phpFilePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(phpFilePath);

            if (PathsMatch(File, phpFilePath))
            {
                return true;
            }

            if (MapFilePath is string mapPath && PathsMatch(MapPathToPhpPath(mapPath), phpFilePath))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Compare two filesystem or source-map paths, ignoring <c>/</c> vs <c>\</c> and
        /// treating a filename-only side as matching the other side's final segment.
        /// Also matches when one path is a suffix of the other (relative vs absolute).
        /// </summary>
        public static bool PathsMatch(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            string a = NormalizePath(left);
            string b = NormalizePath(right);
            StringComparison comparison = PathComparison;

            if (string.Equals(a, b, comparison))
            {
                return true;
            }

            string aName = FileName(a);
            string bName = FileName(b);
            if (string.Equals(a, bName, comparison) || string.Equals(b, aName, comparison))
            {
                return true;
            }

            if (a.EndsWith('/' + b, comparison) || b.EndsWith('/' + a, comparison))
            {
                return true;
            }

            return false;
        }

        /// <summary>Normalize separators to <c>/</c> and trim a trailing slash (except a root <c>/</c>).</summary>
        public static string NormalizePath(string path)
        {
            string normalized = path.Replace('\\', '/');
            if (normalized.Length > 1 && normalized.EndsWith('/'))
            {
                normalized = normalized.TrimEnd('/');
            }

            return normalized;
        }

        internal static StringComparison PathComparison =>
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        internal static string MapPathToPhpPath(string mapFilePath)
        {
            string normalized = NormalizePath(mapFilePath);
            const string suffix = ".php.map";
            if (normalized.EndsWith(suffix, PathComparison))
            {
                return normalized[..^".map".Length];
            }

            if (normalized.EndsWith(".map", PathComparison))
            {
                return normalized[..^".map".Length];
            }

            return normalized;
        }

        private static SourceMapFile FromElement(JsonElement root, string? mapFilePath)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Source map JSON must be an object.");
            }

            if (!root.TryGetProperty("version", out JsonElement versionElement)
                || versionElement.ValueKind != JsonValueKind.Number)
            {
                throw new FormatException("Source map 'version' must be a number.");
            }

            if (!root.TryGetProperty("mappings", out JsonElement mappingsElement)
                || mappingsElement.ValueKind != JsonValueKind.String)
            {
                throw new FormatException("Source map 'mappings' must be a string.");
            }

            if (!root.TryGetProperty("sources", out JsonElement sourcesElement)
                || sourcesElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Source map 'sources' must be an array.");
            }

            int version = versionElement.GetInt32();
            if (version != 3)
            {
                throw new FormatException(
                    $"Unsupported source map 'version': {version}. Only version 3 is supported.");
            }

            string file = ReadOptionalString(root, "file");
            string sourceRoot = ReadOptionalString(root, "sourceRoot");
            string[] sources = ReadStringArray(sourcesElement);
            string[] names = root.TryGetProperty("names", out JsonElement namesElement)
                && namesElement.ValueKind == JsonValueKind.Array
                    ? ReadStringArray(namesElement)
                    : [];
            string?[]? sourcesContent = ReadSourcesContent(root);
            if (sourcesContent is not null && sourcesContent.Length != sources.Length)
            {
                throw new FormatException(
                    $"Source map 'sourcesContent' length ({sourcesContent.Length}) does not "
                    + $"match 'sources' length ({sources.Length}).");
            }

            string mappings = mappingsElement.GetString() ?? string.Empty;

            return new SourceMapFile(
                version,
                file,
                sourceRoot,
                sources,
                sourcesContent,
                names,
                mappings,
                mapFilePath);
        }

        private static string ReadOptionalString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement element)
                || element.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            return element.GetString() ?? string.Empty;
        }

        private static string[] ReadStringArray(JsonElement array)
        {
            var values = new string[array.GetArrayLength()];
            int i = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                values[i++] = item.ValueKind == JsonValueKind.String
                    ? item.GetString() ?? string.Empty
                    : string.Empty;
            }

            return values;
        }

        private static string?[]? ReadSourcesContent(JsonElement root)
        {
            if (!root.TryGetProperty("sourcesContent", out JsonElement element)
                || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            if (element.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Source map 'sourcesContent' must be an array when present.");
            }

            var values = new string?[element.GetArrayLength()];
            int i = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                values[i++] = item.ValueKind == JsonValueKind.Null
                    ? null
                    : item.GetString();
            }

            return values;
        }

        private static bool IsRootedPath(string normalized)
        {
            if (normalized.StartsWith('/'))
            {
                return true;
            }

            return normalized.Length >= 2
                && char.IsAsciiLetter(normalized[0])
                && normalized[1] == ':';
        }

        private static string FileName(string normalizedPath)
        {
            int slash = normalizedPath.LastIndexOf('/');
            return slash < 0 ? normalizedPath : normalizedPath[(slash + 1)..];
        }
    }
}
