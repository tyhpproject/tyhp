using System.Collections.Concurrent;

namespace Tyhp.XDebugProxy.SourceMap
{
    /// <summary>Why a source map was skipped or could not be used.</summary>
    public enum SourceMapLoadWarningKind
    {
        RootDirectoryMissing,
        MapFileMissing,
        MapFileUnreadable,
        MapFileMalformed,
    }

    /// <summary>
    /// Non-localized warning collected by <see cref="SourceMapStore"/>. Later phases can map
    /// these onto <c>MessageCode</c> 7400–7499 / <c>DiagnosticBag</c>; Phase 1 does not emit CLI
    /// strings.
    /// </summary>
    public sealed class SourceMapLoadWarning
    {
        public SourceMapLoadWarning(SourceMapLoadWarningKind kind, string message, string? path = null)
        {
            Kind = kind;
            Message = message;
            Path = path;
        }

        public SourceMapLoadWarningKind Kind { get; }

        public string Message { get; }

        public string? Path { get; }
    }

    /// <summary>
    /// Discovers, caches, and indexes <c>*.php.map</c> files under a generated-output directory.
    /// Missing or malformed maps are skipped with a warning; lookups never throw because of them.
    /// </summary>
    public sealed partial class SourceMapStore : IDisposable
    {
        private readonly string _rootDirectory;
        private readonly string[] _explicitMapPaths;
        private readonly Action<SourceMapLoadWarning>? _onWarning;
        private readonly ConcurrentDictionary<string, CachedMap> _cache;
        private readonly ConcurrentQueue<SourceMapLoadWarning> _warnings = new();
        private readonly object _loadLock = new();
        private readonly List<FileSystemWatcher> _watchers = [];
        private bool _autoReload = true;
        private int _dirty;
        private bool _disposed;

        /// <summary>
        /// Create a store for maps under <paramref name="rootDirectory"/>.
        /// </summary>
        /// <param name="rootDirectory">
        /// Output directory that contains <c>.php</c> and <c>.php.map</c> files.
        /// </param>
        /// <param name="explicitMapPaths">
        /// Optional extra <c>.map</c> paths to load even when they sit outside the root.
        /// </param>
        /// <param name="onWarning">
        /// Optional callback invoked for each skipped/malformed map (in addition to
        /// <see cref="Warnings"/>).
        /// </param>
        public SourceMapStore(
            string rootDirectory,
            IEnumerable<string>? explicitMapPaths = null,
            Action<SourceMapLoadWarning>? onWarning = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

            _rootDirectory = rootDirectory;
            _explicitMapPaths = explicitMapPaths is null ? [] : [.. explicitMapPaths];
            _onWarning = onWarning;
            _cache = new ConcurrentDictionary<string, CachedMap>(CacheKeyComparer);
            StartWatchers();
        }

        /// <summary>Output directory passed to the constructor.</summary>
        public string RootDirectory => _rootDirectory;

        /// <summary>
        /// When true (default), lookups reload a map whose mtime/size changed, pick up
        /// <c>&lt;phpfile&gt;.map</c> files that appeared on disk, and honor
        /// <see cref="FileSystemWatcher"/> notifications under the root.
        /// </summary>
        public bool AutoReload
        {
            get => _autoReload;
            set
            {
                _autoReload = value;
                foreach (FileSystemWatcher watcher in _watchers)
                {
                    try
                    {
                        watcher.EnableRaisingEvents = value;
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            }
        }

        /// <summary>Warnings from the most recent <see cref="LoadAll"/> plus any later lookup skips.</summary>
        public IReadOnlyList<SourceMapLoadWarning> Warnings => _warnings.ToArray();

        /// <summary>Snapshot of currently cached maps.</summary>
        public IReadOnlyList<SourceMapFile> LoadedMaps
        {
            get
            {
                var maps = new List<SourceMapFile>(_cache.Count);
                foreach (CachedMap entry in _cache.Values)
                {
                    maps.Add(entry.Map);
                }

                return maps;
            }
        }

        /// <summary>
        /// Recursively discover <c>*.php.map</c> under the root, plus any explicit paths, and
        /// parse them. Missing/malformed files are skipped with a warning.
        /// </summary>
        public void LoadAll()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            lock (_loadLock)
            {
                LoadAllCore();
            }
        }

        /// <summary>
        /// Return the map for a generated PHP file, matching the JSON <c>file</c> field or the
        /// <c>&lt;phpfile&gt;.map</c> convention. Returns <see langword="null"/> when none is found.
        /// </summary>
        public SourceMapFile? GetMapForPhpFile(string phpFilePath)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(phpFilePath);

            if (AutoReload)
            {
                ReloadIfNeeded(phpFilePath);
            }

            foreach (string candidate in PhpPathCandidates(phpFilePath))
            {
                if (TryGetByConvention(candidate, out SourceMapFile? byConvention))
                {
                    return byConvention;
                }
            }

            SourceMapFile? indexed = LookupPhpIndex(phpFilePath);
            if (indexed is not null && indexed.MatchesGeneratedFile(phpFilePath))
            {
                return indexed;
            }

            foreach (CachedMap entry in _cache.Values)
            {
                if (entry.Map.MatchesGeneratedFile(phpFilePath))
                {
                    return entry.Map;
                }
            }

            return null;
        }

        /// <summary>
        /// Return every cached map whose <c>sources</c> array references <paramref name="tyhpFilePath"/>.
        /// </summary>
        public IReadOnlyList<SourceMapFile> GetMapForTyhpFile(string tyhpFilePath)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentException.ThrowIfNullOrWhiteSpace(tyhpFilePath);

            if (AutoReload)
            {
                ReloadIfNeeded(phpFilePath: null);
            }

            List<SourceMapFile>? indexed = LookupTyhpIndex(tyhpFilePath);
            if (indexed is not null)
            {
                var filtered = new List<SourceMapFile>(indexed.Count);
                foreach (SourceMapFile map in indexed)
                {
                    if (map.ReferencesSource(tyhpFilePath))
                    {
                        filtered.Add(map);
                    }
                }

                if (filtered.Count > 0)
                {
                    return filtered;
                }
            }

            var matches = new List<SourceMapFile>();
            foreach (CachedMap entry in _cache.Values)
            {
                if (entry.Map.ReferencesSource(tyhpFilePath))
                {
                    matches.Add(entry.Map);
                }
            }

            return matches;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (FileSystemWatcher watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Created -= OnWatcherEvent;
                watcher.Changed -= OnWatcherEvent;
                watcher.Deleted -= OnWatcherEvent;
                watcher.Renamed -= OnWatcherRenamed;
                watcher.Dispose();
            }

            _watchers.Clear();
            GC.SuppressFinalize(this);
        }

        private void LoadAllCore()
        {
            _suppressIndexRebuild++;
            try
            {
                while (!_warnings.IsEmpty)
                {
                    _warnings.TryDequeue(out _);
                }

                // Do not clear the cache up front: that would leave a window (while this method is
                // still enumerating/parsing) where every concurrent lookup misses maps that are still
                // valid on disk. Instead, compute the discovered set first and reconcile afterwards —
                // stale entries are removed and changed/new ones (re)loaded, but unaffected maps stay
                // queryable throughout.
                if (_watchers.Count == 0 && Directory.Exists(_rootDirectory))
                {
                    StartWatchers();
                }

                var paths = new HashSet<string>(CacheKeyComparer);
                if (Directory.Exists(_rootDirectory))
                {
                    try
                    {
                        foreach (string path in Directory.EnumerateFiles(
                            _rootDirectory,
                            "*.php.map",
                            SearchOption.AllDirectories))
                        {
                            paths.Add(CanonicalKey(path));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        ReportWarning(
                            SourceMapLoadWarningKind.MapFileUnreadable,
                            $"Could not enumerate source maps under '{_rootDirectory}': {ex.Message}",
                            _rootDirectory);
                    }
                }
                else
                {
                    ReportWarning(
                        SourceMapLoadWarningKind.RootDirectoryMissing,
                        $"Source map root directory does not exist: '{_rootDirectory}'.",
                        _rootDirectory);
                }

                foreach (string explicitPath in _explicitMapPaths)
                {
                    if (string.IsNullOrWhiteSpace(explicitPath))
                    {
                        continue;
                    }

                    paths.Add(CanonicalKey(explicitPath));
                }

                foreach (string existingKey in _cache.Keys)
                {
                    if (!paths.Contains(existingKey))
                    {
                        _cache.TryRemove(existingKey, out _);
                    }
                }

                foreach (string path in paths)
                {
                    TryLoadMap(path, requireExists: true);
                }

                Interlocked.Exchange(ref _dirty, 0);
            }
            finally
            {
                _suppressIndexRebuild--;
                RebuildIndexes();
            }
        }
    }
}
