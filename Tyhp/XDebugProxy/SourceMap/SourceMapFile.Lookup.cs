namespace Tyhp.XDebugProxy.SourceMap
{
    public sealed partial class SourceMapFile
    {
        /// <summary>
        /// Forward lookup: generated PHP line/column → original Tyhp path, line, and column.
        /// Coordinates are 0-based. Binary-searches that line's mapped segments for the closest
        /// mapping at-or-before the requested column. A column before every mapped segment on the
        /// line (the unmapped preamble, e.g. leading whitespace) has no mapping.
        /// </summary>
        /// <returns>
        /// <see langword="null"/> when the generated line has no original mapping, or when
        /// <paramref name="generatedColumn"/> falls before the first mapped segment on the line.
        /// </returns>
        public OriginalPosition? FindOriginalPosition(int generatedLine, int generatedColumn)
        {
            IReadOnlyList<IReadOnlyList<MappingEntry>> decoded = DecodedMappings;
            if (generatedLine < 0 || generatedLine >= decoded.Count)
            {
                return null;
            }

            IReadOnlyList<MappingEntry> mapped = MappedSegments(decoded[generatedLine]);
            if (mapped.Count == 0)
            {
                return null;
            }

            MappingEntry? match = FindSegmentAtOrBefore(mapped, generatedColumn);
            if (match is not MappingEntry entry || !entry.HasOriginalPosition)
            {
                return null;
            }

            return ToOriginalPosition(entry);
        }

        /// <summary>
        /// Reverse lookup: Tyhp file + line/column → generated PHP line and column.
        /// Coordinates are 0-based. Chooses the closest mapped original line <b>at or after</b>
        /// the target (debugger snap-forward); if none exist, the closest mapped line before.
        /// </summary>
        /// <returns><see langword="null"/> when this map does not reference <paramref name="sourceFile"/>.</returns>
        public GeneratedPosition? FindGeneratedPosition(string sourceFile, int originalLine, int originalColumn)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

            int? sourceIndex = FindSourceIndex(sourceFile);
            if (sourceIndex is null)
            {
                return null;
            }

            MappingEntry? atOrAfter = null;
            MappingEntry? before = null;

            foreach (IReadOnlyList<MappingEntry> line in DecodedMappings)
            {
                foreach (MappingEntry entry in line)
                {
                    if (!entry.HasOriginalPosition || entry.OriginalSourceIndex != sourceIndex)
                    {
                        continue;
                    }

                    int entryLine = entry.OriginalLine!.Value;
                    if (entryLine >= originalLine)
                    {
                        if (IsBetterAtOrAfter(entry, atOrAfter, originalLine, originalColumn))
                        {
                            atOrAfter = entry;
                        }
                    }
                    else if (IsBetterBefore(entry, before, originalColumn))
                    {
                        before = entry;
                    }
                }
            }

            MappingEntry? chosen = atOrAfter ?? before;
            if (chosen is not MappingEntry match)
            {
                return null;
            }

            return new GeneratedPosition(File, match.GeneratedLine, match.GeneratedColumn);
        }

        /// <summary>
        /// True when <c>sources</c> (with <c>sourceRoot</c> applied) refers to
        /// <paramref name="tyhpFilePath"/>. Robust to <c>/</c> vs <c>\</c> and filename-only paths.
        /// </summary>
        public bool ReferencesSource(string tyhpFilePath)
        {
            return FindSourceIndex(tyhpFilePath) is not null;
        }

        /// <summary>
        /// Embedded Tyhp source from <c>sourcesContent</c>, aligned with <see cref="Sources"/>.
        /// Returns <see langword="null"/> when the field was omitted or that index is null.
        /// </summary>
        public string? GetEmbeddedSourceContent(string? tyhpFilePath = null)
        {
            if (SourcesContent is null || SourcesContent.Count == 0)
            {
                return null;
            }

            int index = 0;
            if (!string.IsNullOrWhiteSpace(tyhpFilePath)
                && FindSourceIndex(tyhpFilePath) is int matched)
            {
                index = matched;
            }

            if (index < 0 || index >= SourcesContent.Count)
            {
                return null;
            }

            return SourcesContent[index];
        }

        /// <summary>
        /// Paths that should be indexed for Tyhp-file lookup: raw <c>sources</c> entries,
        /// <c>sourceRoot</c>-resolved paths, and filename-only forms.
        /// </summary>
        internal IEnumerable<string> EnumerateSourceLookupKeys()
        {
            for (int i = 0; i < Sources.Count; i++)
            {
                string source = Sources[i];
                if (!string.IsNullOrWhiteSpace(source))
                {
                    yield return source;
                    string fileName = Path.GetFileName(source);
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        yield return fileName;
                    }
                }

                string resolved = ResolveSourcePath(i);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    yield return resolved;
                    string resolvedName = Path.GetFileName(resolved);
                    if (!string.IsNullOrEmpty(resolvedName))
                    {
                        yield return resolvedName;
                    }
                }
            }
        }

        private OriginalPosition ToOriginalPosition(MappingEntry entry)
        {
            int sourceIndex = entry.OriginalSourceIndex!.Value;
            string sourceFile = ResolveSourcePath(sourceIndex);
            string? name = null;
            if (entry.OriginalNameIndex is int nameIndex
                && nameIndex >= 0
                && nameIndex < Names.Count)
            {
                name = Names[nameIndex];
            }

            return new OriginalPosition(
                sourceFile,
                entry.OriginalLine!.Value,
                entry.OriginalColumn!.Value,
                name);
        }

        private string ResolveSourcePath(int sourceIndex)
        {
            if (sourceIndex < 0 || sourceIndex >= Sources.Count)
            {
                return string.Empty;
            }

            string source = Sources[sourceIndex];
            string normalizedSource = NormalizePath(source);
            if (string.IsNullOrEmpty(SourceRoot) || IsRootedPath(normalizedSource))
            {
                return normalizedSource;
            }

            string root = NormalizePath(SourceRoot).TrimEnd('/');
            if (root.Length == 0)
            {
                return normalizedSource;
            }

            return root + "/" + normalizedSource.TrimStart('/');
        }

        private int? FindSourceIndex(string tyhpFilePath)
        {
            for (int i = 0; i < Sources.Count; i++)
            {
                if (PathsMatch(Sources[i], tyhpFilePath) || PathsMatch(ResolveSourcePath(i), tyhpFilePath))
                {
                    return i;
                }
            }

            return null;
        }

        private static IReadOnlyList<MappingEntry> MappedSegments(IReadOnlyList<MappingEntry> segments)
        {
            int mappedCount = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].HasOriginalPosition)
                {
                    mappedCount++;
                }
            }

            if (mappedCount == segments.Count)
            {
                return segments;
            }

            if (mappedCount == 0)
            {
                return [];
            }

            var mapped = new List<MappingEntry>(mappedCount);
            foreach (MappingEntry entry in segments)
            {
                if (entry.HasOriginalPosition)
                {
                    mapped.Add(entry);
                }
            }

            return mapped;
        }

        /// <summary>
        /// Binary search for the segment with the greatest <c>GeneratedColumn</c> that is
        /// <c>&lt;= column</c>. Returns <see langword="null"/> when <paramref name="column"/> is
        /// before every segment (unmapped preamble) — the caller must not snap forward.
        /// </summary>
        private static MappingEntry? FindSegmentAtOrBefore(IReadOnlyList<MappingEntry> sorted, int column)
        {
            int lo = 0;
            int hi = sorted.Count - 1;
            int best = -1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) / 2);
                if (sorted[mid].GeneratedColumn <= column)
                {
                    best = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return best >= 0 ? sorted[best] : null;
        }

        private static bool IsBetterAtOrAfter(
            MappingEntry candidate,
            MappingEntry? current,
            int targetLine,
            int targetColumn)
        {
            if (current is not MappingEntry existing)
            {
                return true;
            }

            int candidateLine = candidate.OriginalLine!.Value;
            int existingLine = existing.OriginalLine!.Value;
            if (candidateLine != existingLine)
            {
                return candidateLine < existingLine;
            }

            return CloserColumn(candidate.OriginalColumn!.Value, existing.OriginalColumn!.Value, targetColumn);
        }

        private static bool IsBetterBefore(MappingEntry candidate, MappingEntry? current, int targetColumn)
        {
            if (current is not MappingEntry existing)
            {
                return true;
            }

            int candidateLine = candidate.OriginalLine!.Value;
            int existingLine = existing.OriginalLine!.Value;
            if (candidateLine != existingLine)
            {
                return candidateLine > existingLine;
            }

            return CloserColumn(candidate.OriginalColumn!.Value, existing.OriginalColumn!.Value, targetColumn);
        }

        /// <summary>
        /// Prefer the mapping whose column is at or after <paramref name="targetColumn"/> and
        /// closest to it; if none, the closest column before the target.
        /// </summary>
        private static bool CloserColumn(int candidateColumn, int existingColumn, int targetColumn)
        {
            bool candidateAtOrAfter = candidateColumn >= targetColumn;
            bool existingAtOrAfter = existingColumn >= targetColumn;
            if (candidateAtOrAfter != existingAtOrAfter)
            {
                return candidateAtOrAfter;
            }

            int candidateDistance = Math.Abs(candidateColumn - targetColumn);
            int existingDistance = Math.Abs(existingColumn - targetColumn);
            if (candidateDistance != existingDistance)
            {
                return candidateDistance < existingDistance;
            }

            return candidateColumn < existingColumn;
        }
    }
}
