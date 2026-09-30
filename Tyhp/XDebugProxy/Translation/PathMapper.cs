using Tyhp.XDebugProxy.SourceMap;

namespace Tyhp.XDebugProxy.Translation
{
    /// <summary>
    /// Converts Tyhp source paths ↔ PHP output paths and <c>file://</c> URIs ↔ filesystem
    /// paths. Also performs sourcemap line lookups at the DBGp (1-based) boundary.
    /// </summary>
    /// <remarks>
    /// PLACEHOLDER_STORY_19: Coordinate with LSP for shared debug adapter protocol — path
    /// mapping and URI normalization should stay aligned with the language server.
    /// </remarks>
    public sealed partial class PathMapper
    {
        public PathMapper(string tyhpSourceRoot, string phpOutputRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tyhpSourceRoot);
            ArgumentException.ThrowIfNullOrWhiteSpace(phpOutputRoot);

            this.TyhpSourceRoot = Normalize(tyhpSourceRoot);
            this.PhpOutputRoot = Normalize(phpOutputRoot);
        }

        /// <summary>Root directory of original <c>.tyhp</c> sources.</summary>
        public string TyhpSourceRoot { get; }

        /// <summary>Root directory of compiled <c>.php</c> output (and <c>.php.map</c> files).</summary>
        public string PhpOutputRoot { get; }

        /// <summary>True when the path (URI stripped) ends in <c>.tyhp</c>.</summary>
        public bool IsTyhpFile(string pathOrUri)
        {
            ArgumentNullException.ThrowIfNull(pathOrUri);
            if (this.IsDbgpUri(pathOrUri))
            {
                return false;
            }

            string path = this.ToFileSystemPath(pathOrUri);
            return path.EndsWith(".tyhp", SourceMapFile.PathComparison);
        }

        /// <summary>True when the path (URI stripped) ends in <c>.php</c>.</summary>
        public bool IsPhpFile(string pathOrUri)
        {
            ArgumentNullException.ThrowIfNull(pathOrUri);
            if (this.IsDbgpUri(pathOrUri))
            {
                return false;
            }

            string path = this.ToFileSystemPath(pathOrUri);
            return path.EndsWith(".php", SourceMapFile.PathComparison);
        }

        /// <summary>
        /// Normalize separators to <c>/</c>. Does not rewrite UNC (<c>//server/share</c>) or
        /// drive-letter prefixes.
        /// </summary>
        public string Normalize(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return SourceMapFile.NormalizePath(path);
        }

        /// <summary>Join <paramref name="root"/> and a relative path using <c>/</c>.</summary>
        public string Combine(string root, string relative)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(relative);

            string rel = this.Normalize(relative);
            if (IsRootedPath(rel))
            {
                return rel;
            }

            string trimmedRoot = this.Normalize(root).TrimEnd('/');
            string trimmedRel = rel.TrimStart('/');
            if (trimmedRoot.Length == 0)
            {
                return trimmedRel;
            }

            if (trimmedRel.Length == 0)
            {
                return trimmedRoot;
            }

            return trimmedRoot + "/" + trimmedRel;
        }

        /// <summary>DBGp <c>lineno</c> (1-based) → source-map line (0-based).</summary>
        public static int ToSourceMapLine(int dbgpLine) => dbgpLine - 1;

        /// <summary>Source-map line (0-based) → DBGp <c>lineno</c> (1-based).</summary>
        public static int ToDbgpLine(int sourceMapLine) => sourceMapLine + 1;

        /// <summary>
        /// Resolve the generated PHP filesystem path for a map, preferring
        /// <see cref="SourceMapFile.MapFilePath"/> then the JSON <c>file</c> field joined with
        /// <see cref="PhpOutputRoot"/>.
        /// </summary>
        public string ResolveGeneratedPhpPath(SourceMapFile map, string? generatedFile = null)
        {
            ArgumentNullException.ThrowIfNull(map);

            if (!string.IsNullOrWhiteSpace(map.MapFilePath))
            {
                return SourceMapFile.MapPathToPhpPath(map.MapFilePath);
            }

            string file = generatedFile ?? map.File;
            if (string.IsNullOrWhiteSpace(file))
            {
                return this.PhpOutputRoot;
            }

            string normalized = this.Normalize(file);
            return IsRootedPath(normalized)
                ? normalized
                : this.Combine(this.PhpOutputRoot, normalized);
        }

        /// <summary>
        /// Resolve a Tyhp source path from a sourcemap <c>sources</c> entry (or
        /// <see cref="OriginalPosition.SourceFile"/>), joining with <see cref="TyhpSourceRoot"/>
        /// when the path is relative. <see cref="SourceMapFile"/> already applies <c>sourceRoot</c>.
        /// </summary>
        public string ResolveOriginalTyhpPath(string sourceFile)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
            string normalized = this.Normalize(sourceFile);
            return IsRootedPath(normalized)
                ? normalized
                : this.Combine(this.TyhpSourceRoot, normalized);
        }

        /// <summary>
        /// Map a Tyhp file + DBGp line to a PHP file + DBGp line. When several maps reference
        /// the same Tyhp file, prefers the path-closest PHP output; ties go to the first map
        /// that has a generated position.
        /// </summary>
        public bool TryMapTyhpToPhp(
            SourceMapStore store,
            string tyhpPathOrUri,
            int dbgpLine,
            out string phpPathOrUri,
            out int phpDbgpLine)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentException.ThrowIfNullOrWhiteSpace(tyhpPathOrUri);

            phpPathOrUri = tyhpPathOrUri;
            phpDbgpLine = dbgpLine;

            if (this.IsDbgpUri(tyhpPathOrUri) || dbgpLine < 1)
            {
                return false;
            }

            string tyhpPath = this.ToFileSystemPath(tyhpPathOrUri);
            IReadOnlyList<SourceMapFile> maps = store.GetMapForTyhpFile(tyhpPath);
            if (maps.Count == 0)
            {
                return false;
            }

            int originalLine = ToSourceMapLine(dbgpLine);
            SourceMapFile? bestMap = null;
            GeneratedPosition? bestPos = null;
            int bestScore = int.MinValue;

            foreach (SourceMapFile map in maps)
            {
                GeneratedPosition? pos = map.FindGeneratedPosition(tyhpPath, originalLine, 0);
                if (pos is null)
                {
                    continue;
                }

                int score = this.ScoreMapForTyhp(tyhpPath, map);
                if (bestMap is null || score > bestScore)
                {
                    bestMap = map;
                    bestPos = pos;
                    bestScore = score;
                }
            }

            if (bestMap is null || bestPos is not GeneratedPosition generated)
            {
                return false;
            }

            string phpFs = this.ResolveGeneratedPhpPath(bestMap, generated.GeneratedFile);
            phpPathOrUri = this.PreserveScheme(tyhpPathOrUri, phpFs);
            phpDbgpLine = ToDbgpLine(generated.Line);
            return true;
        }

        /// <summary>
        /// Reverse-map a PHP file + DBGp line to Tyhp. Returns <see langword="false"/> when
        /// there is no map or <see cref="SourceMapFile.FindOriginalPosition"/> returns null
        /// (unmapped preamble / unmapped line) — callers must pass through untranslated.
        /// </summary>
        public bool TryMapPhpToTyhp(
            SourceMapStore store,
            string phpPathOrUri,
            int dbgpLine,
            out string tyhpPathOrUri,
            out int tyhpDbgpLine,
            out string? name)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentException.ThrowIfNullOrWhiteSpace(phpPathOrUri);

            tyhpPathOrUri = phpPathOrUri;
            tyhpDbgpLine = dbgpLine;
            name = null;

            if (this.IsDbgpUri(phpPathOrUri) || dbgpLine < 1)
            {
                return false;
            }

            SourceMapFile? map = this.GetMapForPhp(store, phpPathOrUri);
            if (map is null)
            {
                return false;
            }

            OriginalPosition? found = map.FindOriginalPosition(ToSourceMapLine(dbgpLine), 0);
            if (found is not OriginalPosition original)
            {
                return false;
            }

            string tyhpFs = this.ResolveOriginalTyhpPath(original.SourceFile);
            tyhpPathOrUri = this.PreserveScheme(phpPathOrUri, tyhpFs);
            tyhpDbgpLine = ToDbgpLine(original.Line);
            name = original.Name;
            return true;
        }

        /// <summary>
        /// Resolve a Tyhp path for an init <c>fileuri</c> that has no line number: first mapped
        /// original position, else the map's first <c>sources</c> entry.
        /// </summary>
        public bool TryMapPhpFileToTyhpFile(
            SourceMapStore store,
            string phpPathOrUri,
            out string tyhpPathOrUri)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentException.ThrowIfNullOrWhiteSpace(phpPathOrUri);

            tyhpPathOrUri = phpPathOrUri;
            if (this.IsDbgpUri(phpPathOrUri))
            {
                return false;
            }

            SourceMapFile? map = this.GetMapForPhp(store, phpPathOrUri);
            if (map is null)
            {
                return false;
            }

            OriginalPosition? first = FirstMappedOriginal(map);
            string sourceFile;
            if (first is OriginalPosition original)
            {
                sourceFile = original.SourceFile;
            }
            else if (map.Sources.Count > 0 && !string.IsNullOrWhiteSpace(map.Sources[0]))
            {
                string source = map.Sources[0];
                sourceFile = string.IsNullOrEmpty(map.SourceRoot) || IsRootedPath(this.Normalize(source))
                    ? source
                    : this.Combine(map.SourceRoot, source);
            }
            else
            {
                return false;
            }

            tyhpPathOrUri = this.PreserveScheme(phpPathOrUri, this.ResolveOriginalTyhpPath(sourceFile));
            return true;
        }

        internal SourceMapFile? GetMapForPhp(SourceMapStore store, string phpPathOrUri)
        {
            string phpPath = this.ToFileSystemPath(phpPathOrUri);
            SourceMapFile? map = store.GetMapForPhpFile(phpPath);
            if (map is not null)
            {
                return map;
            }

            if (!IsRootedPath(phpPath))
            {
                map = store.GetMapForPhpFile(this.Combine(this.PhpOutputRoot, phpPath));
            }

            return map;
        }

        internal static bool IsRootedPath(string normalized)
        {
            if (normalized.StartsWith('/'))
            {
                return true;
            }

            return normalized.Length >= 2
                && char.IsAsciiLetter(normalized[0])
                && normalized[1] == ':';
        }

        private int ScoreMapForTyhp(string tyhpPath, SourceMapFile map)
        {
            string tyhpName = Path.GetFileNameWithoutExtension(tyhpPath);
            string phpName = Path.GetFileNameWithoutExtension(map.File);
            int score = 0;
            if (!string.IsNullOrEmpty(tyhpName)
                && string.Equals(tyhpName, phpName, SourceMapFile.PathComparison))
            {
                score += 1_000_000;
            }

            string phpPath = this.ResolveGeneratedPhpPath(map, map.File);
            score += CommonTrailingSegmentCount(this.Normalize(tyhpPath), this.Normalize(phpPath));
            return score;
        }

        private static int CommonTrailingSegmentCount(string left, string right)
        {
            string[] leftParts = left.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string[] rightParts = right.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int i = leftParts.Length - 1;
            int j = rightParts.Length - 1;
            int count = 0;
            if (i >= 0 && j >= 0)
            {
                string leftName = Path.GetFileNameWithoutExtension(leftParts[i]);
                string rightName = Path.GetFileNameWithoutExtension(rightParts[j]);
                if (!string.Equals(leftName, rightName, SourceMapFile.PathComparison))
                {
                    return 0;
                }

                count++;
                i--;
                j--;
            }

            while (i >= 0 && j >= 0
                && string.Equals(leftParts[i], rightParts[j], SourceMapFile.PathComparison))
            {
                count++;
                i--;
                j--;
            }

            return count;
        }

        private static OriginalPosition? FirstMappedOriginal(SourceMapFile map)
        {
            IReadOnlyList<IReadOnlyList<MappingEntry>> decoded = map.DecodedMappings;
            for (int line = 0; line < decoded.Count; line++)
            {
                foreach (MappingEntry entry in decoded[line])
                {
                    if (!entry.HasOriginalPosition)
                    {
                        continue;
                    }

                    OriginalPosition? found = map.FindOriginalPosition(line, entry.GeneratedColumn);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }
    }
}
