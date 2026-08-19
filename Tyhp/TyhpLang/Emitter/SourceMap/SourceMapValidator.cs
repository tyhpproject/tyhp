using System.Text.Json;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.TyhpLang.Emitter.SourceMap
{
    /// <summary>
    /// Result of <see cref="SourceMapValidator.Validate"/>. <see cref="IsValid"/> is true when
    /// <see cref="Errors"/> is empty; coverage shortfalls are warnings only.
    /// </summary>
    internal sealed class SourceMapValidationResult
    {
        public SourceMapValidationResult(
            int totalMappings,
            int generatedLineCount,
            int mappedLineCount,
            double coveragePercentage,
            List<string> errors,
            List<string> warnings,
            IReadOnlyList<SourceMapping> decodedMappings)
        {
            TotalMappings = totalMappings;
            GeneratedLineCount = generatedLineCount;
            MappedLineCount = mappedLineCount;
            CoveragePercentage = coveragePercentage;
            Errors = errors;
            Warnings = warnings;
            DecodedMappings = decodedMappings;
        }

        public bool IsValid => Errors.Count == 0;

        /// <summary>Number of decoded mapping segments (1-, 4-, and 5-field).</summary>
        public int TotalMappings { get; }

        /// <summary>
        /// Line count of generated PHP after stripping <c>sourceMappingURL</c> comment lines.
        /// </summary>
        public int GeneratedLineCount { get; }

        /// <summary>Generated lines that have at least one mapping segment.</summary>
        public int MappedLineCount { get; }

        /// <summary><see cref="MappedLineCount"/> / <see cref="GeneratedLineCount"/> × 100.</summary>
        public double CoveragePercentage { get; }

        public List<string> Errors { get; }

        public List<string> Warnings { get; }

        /// <summary>Decoded 4- and 5-field segments (1-field column-only segments are omitted).</summary>
        public IReadOnlyList<SourceMapping> DecodedMappings { get; }
    }

    /// <summary>
    /// Validates Source Map v3 JSON against the generated PHP it describes. Does not throw on
    /// malformed input — parse, VLQ, and structural failures become <see cref="SourceMapValidationResult.Errors"/>.
    /// </summary>
    /// <remarks>
    /// Tyhp's generator does not pad <c>mappings</c> with trailing <c>;</c> after the last mapped
    /// line, and the write-time <c>sourceMappingURL</c> comment is not a mapped line. Line-count
    /// checks treat extra PHP lines after the last mapping group as coverage (unmapped), not as a
    /// structural mismatch. Mapping groups that extend past the generated file are an error.
    /// </remarks>
    internal static class SourceMapValidator
    {
        /// <summary>Default mapping-coverage warning threshold (percent of generated lines).</summary>
        public const double DefaultCoverageThreshold = 50.0;

        /// <summary>
        /// Validate <paramref name="sourceMapJson"/> against <paramref name="generatedContent"/>.
        /// Mapping-level position/index failures are recorded on the result; structural failures
        /// (invalid JSON, truncated VLQ, illegal field counts, mappings past the generated file)
        /// also add <see cref="MessageCode.EmitterSourceMapInvalidMapping"/> to
        /// <paramref name="diagnostics"/>. Coverage shortfalls are warnings on the result only.
        /// </summary>
        public static SourceMapValidationResult Validate(
            string sourceMapJson,
            string generatedContent,
            DiagnosticBag diagnostics,
            double coverageThreshold = DefaultCoverageThreshold,
            Func<string, string?>? sourceContentProvider = null)
        {
            ArgumentNullException.ThrowIfNull(sourceMapJson);
            ArgumentNullException.ThrowIfNull(generatedContent);
            ArgumentNullException.ThrowIfNull(diagnostics);

            var result = new SourceMapValidationResultBuilder();
            string phpForLineCount = StripSourceMappingUrlLines(generatedContent);
            result.GeneratedLineCount = CountLines(phpForLineCount);

            if (string.IsNullOrWhiteSpace(sourceMapJson))
            {
                AddStructuralError(result, diagnostics, "(unknown)", "Source map JSON is empty.");
                return result.Build();
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(sourceMapJson);
            }
            catch (JsonException ex)
            {
                AddStructuralError(result, diagnostics, "(unknown)", $"Source map JSON is not valid: {ex.Message}");
                return result.Build();
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    AddStructuralError(result, diagnostics, "(unknown)", "Source map JSON root must be an object.");
                    return result.Build();
                }

                ValidateJsonShape(root, result, diagnostics);
                if (result.Errors.Count > 0)
                {
                    return result.Build();
                }

                string fileName = ReadString(root, "file") ?? "(unknown)";
                string sourceRoot = ReadString(root, "sourceRoot") ?? "";
                string[] sources = ReadStringArray(root, "sources");
                string[] names = root.TryGetProperty("names", out JsonElement namesElement)
                    && namesElement.ValueKind == JsonValueKind.Array
                    ? ReadStringArray(root, "names")
                    : [];
                string mappings = ReadString(root, "mappings") ?? "";

                DecodeAndCheckMappings(
                    mappings,
                    sources.Length,
                    names.Length,
                    result.GeneratedLineCount,
                    fileName,
                    result,
                    diagnostics);

                ValidateSourcesContent(root, sources, sourceRoot, sourceContentProvider, result);

                result.CoveragePercentage = result.GeneratedLineCount == 0
                    ? (result.MappedLineCount == 0 ? 100.0 : 0.0)
                    : result.MappedLineCount * 100.0 / result.GeneratedLineCount;

                if (result.GeneratedLineCount > 0
                    && result.CoveragePercentage < coverageThreshold)
                {
                    result.Warnings.Add(
                        $"Mapping coverage is {result.CoveragePercentage:0.##}% "
                        + $"({result.MappedLineCount}/{result.GeneratedLineCount} generated lines); "
                        + $"threshold is {coverageThreshold:0.##}%.");
                }
            }

            return result.Build();
        }

        private static void ValidateJsonShape(
            JsonElement root,
            SourceMapValidationResultBuilder result,
            DiagnosticBag diagnostics)
        {
            string fileName = ReadString(root, "file") ?? "(unknown)";

            if (!root.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number
                || !version.TryGetInt32(out int versionNumber)
                || versionNumber != 3)
            {
                AddStructuralError(
                    result,
                    diagnostics,
                    fileName,
                    "Source map 'version' must be the number 3.");
            }

            if (!root.TryGetProperty("file", out JsonElement file)
                || file.ValueKind != JsonValueKind.String)
            {
                AddStructuralError(
                    result,
                    diagnostics,
                    fileName,
                    "Source map 'file' must be a string.");
            }

            if (!root.TryGetProperty("sources", out JsonElement sources)
                || sources.ValueKind != JsonValueKind.Array)
            {
                AddStructuralError(
                    result,
                    diagnostics,
                    fileName,
                    "Source map 'sources' must be an array.");
            }

            if (!root.TryGetProperty("mappings", out JsonElement mappings)
                || mappings.ValueKind != JsonValueKind.String)
            {
                AddStructuralError(
                    result,
                    diagnostics,
                    fileName,
                    "Source map 'mappings' must be a string.");
            }

            if (root.TryGetProperty("names", out JsonElement names)
                && names.ValueKind != JsonValueKind.Array)
            {
                AddStructuralError(
                    result,
                    diagnostics,
                    fileName,
                    "Source map 'names' must be an array when present.");
            }
        }

        private static void DecodeAndCheckMappings(
            string mappings,
            int sourceCount,
            int nameCount,
            int generatedLineCount,
            string fileName,
            SourceMapValidationResultBuilder result,
            DiagnosticBag diagnostics)
        {
            if (string.IsNullOrEmpty(mappings))
            {
                result.TotalMappings = 0;
                result.MappedLineCount = 0;
                result.DecodedMappings = [];
                return;
            }

            string[] groups = mappings.Split(';');
            if (groups.Length > generatedLineCount)
            {
                AddStructuralError(
                    result,
                    diagnostics,
                    fileName,
                    $"Source map 'mappings' has {groups.Length} line groups but the generated file has {generatedLineCount} lines.");
            }

            int previousGeneratedColumn = 0;
            int previousSourceIndex = 0;
            int previousOriginalLine = 0;
            int previousOriginalColumn = 0;
            int previousNameIndex = 0;
            var decoded = new List<SourceMapping>();
            var mappedLines = new HashSet<int>();
            int totalSegments = 0;

            for (int generatedLine = 0; generatedLine < groups.Length; generatedLine++)
            {
                previousGeneratedColumn = 0;
                string group = groups[generatedLine];
                if (group.Length == 0)
                {
                    continue;
                }

                foreach (string segment in group.Split(','))
                {
                    int[] fields;
                    try
                    {
                        fields = VlqEncoder.DecodeSegment(segment);
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or ArgumentNullException)
                    {
                        AddStructuralError(
                            result,
                            diagnostics,
                            fileName,
                            $"Invalid VLQ segment '{segment}' on generated line {generatedLine}: {ex.Message}");
                        continue;
                    }

                    if (fields.Length is not (1 or 4 or 5))
                    {
                        AddStructuralError(
                            result,
                            diagnostics,
                            fileName,
                            $"Segment on generated line {generatedLine} has {fields.Length} fields; expected 1, 4, or 5.");
                        continue;
                    }

                    int generatedColumn = previousGeneratedColumn + fields[0];
                    previousGeneratedColumn = generatedColumn;
                    totalSegments++;
                    mappedLines.Add(generatedLine);

                    if (generatedColumn < 0)
                    {
                        result.Errors.Add(
                            $"Generated column is negative ({generatedColumn}) on generated line {generatedLine}.");
                    }

                    // Note: a segment on a generated line past the file is already caught by the
                    // `groups.Length > generatedLineCount` structural check above — every line
                    // group index here is < groups.Length, so `generatedLine >= generatedLineCount`
                    // can only be true when that check has already fired. Re-checking per-segment
                    // here would just emit duplicate 5022 warnings for the same root cause.

                    if (fields.Length == 1)
                    {
                        continue;
                    }

                    int sourceIndex = previousSourceIndex + fields[1];
                    int originalLine = previousOriginalLine + fields[2];
                    int originalColumn = previousOriginalColumn + fields[3];
                    previousSourceIndex = sourceIndex;
                    previousOriginalLine = originalLine;
                    previousOriginalColumn = originalColumn;

                    int? nameIndex = null;
                    if (fields.Length == 5)
                    {
                        nameIndex = previousNameIndex + fields[4];
                        previousNameIndex = nameIndex.Value;
                    }

                    if (sourceIndex < 0 || sourceIndex >= sourceCount)
                    {
                        result.Errors.Add(
                            $"Source index {sourceIndex} is outside sources[0..{sourceCount}).");
                    }

                    if (originalLine < 0 || originalColumn < 0)
                    {
                        result.Errors.Add(
                            $"Original position ({originalLine},{originalColumn}) is negative "
                            + $"at generated ({generatedLine},{generatedColumn}).");
                    }

                    if (nameIndex is int decodedName
                        && (decodedName < 0 || decodedName >= nameCount))
                    {
                        result.Errors.Add(
                            $"Name index {decodedName} is outside names[0..{nameCount}).");
                    }

                    decoded.Add(new SourceMapping(
                        generatedLine,
                        generatedColumn,
                        sourceIndex,
                        originalLine,
                        originalColumn,
                        nameIndex));
                }
            }

            result.TotalMappings = totalSegments;
            result.MappedLineCount = mappedLines.Count;
            result.DecodedMappings = decoded;
        }

        private static void ValidateSourcesContent(
            JsonElement root,
            string[] sources,
            string sourceRoot,
            Func<string, string?>? sourceContentProvider,
            SourceMapValidationResultBuilder result)
        {
            if (!root.TryGetProperty("sourcesContent", out JsonElement contents)
                || contents.ValueKind == JsonValueKind.Null)
            {
                return;
            }

            if (contents.ValueKind != JsonValueKind.Array)
            {
                result.Errors.Add("Source map 'sourcesContent' must be an array when present.");
                return;
            }

            if (contents.GetArrayLength() != sources.Length)
            {
                result.Errors.Add(
                    $"Source map 'sourcesContent' length ({contents.GetArrayLength()}) "
                    + $"does not match 'sources' length ({sources.Length}).");
                return;
            }

            if (sourceContentProvider == null)
            {
                return;
            }

            for (int i = 0; i < sources.Length; i++)
            {
                JsonElement entry = contents[i];
                string? embedded = entry.ValueKind == JsonValueKind.Null ? null : entry.GetString();
                string? expected = ReadSourceContent(sourceContentProvider, sources[i], sourceRoot);
                if (expected == null)
                {
                    continue;
                }

                if (!string.Equals(embedded, expected, StringComparison.Ordinal))
                {
                    result.Errors.Add(
                        $"Embedded sourcesContent[{i}] for '{sources[i]}' does not match the provided source content.");
                }
            }
        }

        private static string? ReadSourceContent(
            Func<string, string?> provider,
            string sourcesEntry,
            string sourceRoot)
        {
            string? direct = provider(sourcesEntry);
            if (direct != null)
            {
                return direct;
            }

            if (string.IsNullOrEmpty(sourceRoot))
            {
                return null;
            }

            string combined = sourceRoot.EndsWith('/') || sourceRoot.EndsWith('\\')
                ? sourceRoot + sourcesEntry
                : sourceRoot + "/" + sourcesEntry;
            return provider(combined.Replace('\\', '/'));
        }

        /// <summary>
        /// Drop <c>sourceMappingURL</c> comment lines (external-file or inline data URL). Those
        /// lines are appended at write time and are not represented in <c>mappings</c>.
        /// </summary>
        internal static string StripSourceMappingUrlLines(string content)
        {
            if (!content.Contains("sourceMappingURL=", StringComparison.Ordinal))
            {
                return content;
            }

            string[] lines = content.Split('\n');
            var kept = new List<string>(lines.Length);
            foreach (string line in lines)
            {
                if (line.Contains("sourceMappingURL=", StringComparison.Ordinal))
                {
                    continue;
                }

                kept.Add(line);
            }

            return string.Join('\n', kept);
        }

        internal static int CountLines(string content)
        {
            if (content.Length == 0)
            {
                return 0;
            }

            int lines = 1;
            foreach (char c in content)
            {
                if (c == '\n')
                {
                    lines++;
                }
            }

            return lines;
        }

        private static void AddStructuralError(
            SourceMapValidationResultBuilder result,
            DiagnosticBag diagnostics,
            string fileName,
            string message)
        {
            result.Errors.Add(message);
            diagnostics.AddWarning(
                MessageCode.EmitterSourceMapInvalidMapping,
                fileName,
                0,
                0,
                fileName,
                0,
                0,
                0,
                0);
        }

        private static string? ReadString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return value.GetString();
        }

        private static string[] ReadStringArray(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var items = new List<string>(value.GetArrayLength());
            foreach (JsonElement entry in value.EnumerateArray())
            {
                items.Add(entry.ValueKind == JsonValueKind.String ? entry.GetString() ?? "" : "");
            }

            return [.. items];
        }

        private sealed class SourceMapValidationResultBuilder
        {
            public int TotalMappings { get; set; }
            public int GeneratedLineCount { get; set; }
            public int MappedLineCount { get; set; }
            public double CoveragePercentage { get; set; }
            public List<string> Errors { get; } = [];
            public List<string> Warnings { get; } = [];
            public IReadOnlyList<SourceMapping> DecodedMappings { get; set; } = [];

            public SourceMapValidationResult Build() => new(
                TotalMappings,
                GeneratedLineCount,
                MappedLineCount,
                CoveragePercentage,
                Errors,
                Warnings,
                DecodedMappings);
        }
    }
}
