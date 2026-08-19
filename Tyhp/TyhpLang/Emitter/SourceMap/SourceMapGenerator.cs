using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tyhp.TyhpLang.Emitter.SourceMap
{
    /// <summary>
    /// Assembles a Source Map v3 JSON document from a populated <see cref="SourceMapCollector"/>.
    /// </summary>
    /// <remarks>
    /// Self-contained JSON/VLQ assembly (not the output-file data surface), so this type is
    /// <see langword="internal"/> like <see cref="VlqEncoder"/>. <c>PHPOutputFile.SourceMap()</c>
    /// is the public caller.
    /// </remarks>
    internal sealed class SourceMapGenerator
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly string _generatedFileName;
        private readonly string _sourceRoot;

        /// <summary>
        /// Create a generator for one generated PHP file.
        /// </summary>
        /// <param name="generatedFileName">
        /// Name of the generated PHP file as it should appear in the <c>file</c> field
        /// (e.g. <c>MyClass.php</c>).
        /// </param>
        /// <param name="sourceRoot">
        /// Optional <c>sourceRoot</c> prefix. When set, registered source paths that live under
        /// this directory (or that already start with this prefix) are stored in <c>sources</c>
        /// relative to it. Emitted as an empty string when omitted.
        /// </param>
        public SourceMapGenerator(string generatedFileName, string? sourceRoot = null)
        {
            ArgumentNullException.ThrowIfNull(generatedFileName);

            _generatedFileName = generatedFileName;
            _sourceRoot = sourceRoot ?? string.Empty;
        }

        /// <summary>
        /// Build the complete Source Map v3 JSON string from <paramref name="collector"/>.
        /// </summary>
        /// <param name="collector">Collector populated during emission.</param>
        /// <param name="includeSourcesContent">
        /// When <see langword="true"/>, emit a <c>sourcesContent</c> array. When
        /// <see langword="false"/>, omit the field.
        /// </param>
        /// <param name="sourceContentProvider">
        /// Callback <c>(filePath) => fileContent</c> used only when
        /// <paramref name="includeSourcesContent"/> is true. Invoked with the original registered
        /// source path (not the relativized <c>sources</c> entry). A null return becomes a JSON
        /// <c>null</c> at that index. Ignored when content is not being embedded.
        /// </param>
        public string Generate(
            SourceMapCollector collector,
            bool includeSourcesContent = false,
            Func<string, string?>? sourceContentProvider = null)
        {
            ArgumentNullException.ThrowIfNull(collector);

            IReadOnlyList<string> originalSources = collector.GetSourceFiles();
            IReadOnlyList<string> names = collector.GetNames();
            IReadOnlyList<SourceMapping> mappings = collector.GetMappings();

            var sourcesArray = new JsonArray();
            foreach (string filePath in originalSources)
            {
                sourcesArray.Add(RelativizeSourcePath(filePath));
            }

            var namesArray = new JsonArray();
            foreach (string name in names)
            {
                namesArray.Add(name);
            }

            var root = new JsonObject
            {
                ["version"] = 3,
                ["file"] = _generatedFileName,
                ["sourceRoot"] = _sourceRoot,
                ["sources"] = sourcesArray,
            };

            if (includeSourcesContent)
            {
                var sourcesContent = new JsonArray();
                foreach (string filePath in originalSources)
                {
                    string? content = sourceContentProvider?.Invoke(filePath);
                    sourcesContent.Add(content is null ? null : JsonValue.Create(content));
                }

                root["sourcesContent"] = sourcesContent;
            }

            root["names"] = namesArray;
            root["mappings"] = BuildMappings(mappings);

            return root.ToJsonString(JsonOptions);
        }

        private static string BuildMappings(IReadOnlyList<SourceMapping> mappings)
        {
            if (mappings.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            int previousGeneratedColumn = 0;
            int previousSourceIndex = 0;
            int previousOriginalLine = 0;
            int previousOriginalColumn = 0;
            int previousNameIndex = 0;
            int currentGeneratedLine = 0;
            bool firstSegmentOnLine = true;

            foreach (SourceMapping mapping in mappings)
            {
                while (currentGeneratedLine < mapping.GeneratedLine)
                {
                    builder.Append(';');
                    currentGeneratedLine++;
                    previousGeneratedColumn = 0;
                    firstSegmentOnLine = true;
                }

                if (!firstSegmentOnLine)
                {
                    builder.Append(',');
                }

                builder.Append(EncodeMappingSegment(
                    mapping,
                    ref previousGeneratedColumn,
                    ref previousSourceIndex,
                    ref previousOriginalLine,
                    ref previousOriginalColumn,
                    ref previousNameIndex));
                firstSegmentOnLine = false;
            }

            return builder.ToString();
        }

        private static string EncodeMappingSegment(
            SourceMapping mapping,
            ref int prevGenCol,
            ref int prevSrcIdx,
            ref int prevOrigLine,
            ref int prevOrigCol,
            ref int prevNameIdx)
        {
            int generatedColumnDelta = mapping.GeneratedColumn - prevGenCol;
            int sourceIndexDelta = mapping.SourceIndex - prevSrcIdx;
            int originalLineDelta = mapping.OriginalLine - prevOrigLine;
            int originalColumnDelta = mapping.OriginalColumn - prevOrigCol;

            prevGenCol = mapping.GeneratedColumn;
            prevSrcIdx = mapping.SourceIndex;
            prevOrigLine = mapping.OriginalLine;
            prevOrigCol = mapping.OriginalColumn;

            if (mapping.NameIndex is int nameIndex)
            {
                int nameIndexDelta = nameIndex - prevNameIdx;
                prevNameIdx = nameIndex;
                return VlqEncoder.Encode(
                [
                    generatedColumnDelta,
                    sourceIndexDelta,
                    originalLineDelta,
                    originalColumnDelta,
                    nameIndexDelta,
                ]);
            }

            return VlqEncoder.Encode(
            [
                generatedColumnDelta,
                sourceIndexDelta,
                originalLineDelta,
                originalColumnDelta,
            ]);
        }

        private string RelativizeSourcePath(string filePath)
        {
            string normalized = NormalizeSourceMapPath(filePath);
            if (string.IsNullOrEmpty(_sourceRoot))
            {
                return normalized;
            }

            try
            {
                string rootFull = Path.GetFullPath(_sourceRoot);
                string fileFull = Path.GetFullPath(filePath);
                if (IsUnderDirectory(fileFull, rootFull))
                {
                    string relative = Path.GetRelativePath(rootFull, fileFull);
                    if (!Path.IsPathRooted(relative)
                        && relative != ".."
                        && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                    {
                        return NormalizeSourceMapPath(relative);
                    }
                }
            }
            catch (ArgumentException)
            {
                // Invalid path characters — fall through to string-prefix strip.
            }
            catch (NotSupportedException)
            {
            }
            catch (PathTooLongException)
            {
            }

            string rootPrefix = NormalizeSourceMapPath(_sourceRoot).TrimEnd('/');
            if (rootPrefix.Length > 0
                && (normalized.StartsWith(rootPrefix + "/", StringComparison.Ordinal)
                    || string.Equals(normalized, rootPrefix, StringComparison.Ordinal)))
            {
                return normalized.Length == rootPrefix.Length
                    ? string.Empty
                    : normalized[(rootPrefix.Length + 1)..];
            }

            return normalized;
        }

        private static bool IsUnderDirectory(string fileFullPath, string directoryFullPath)
        {
            string prefix = directoryFullPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (fileFullPath.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return fileFullPath.StartsWith(
                prefix + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSourceMapPath(string path) => path.Replace('\\', '/');
    }
}
