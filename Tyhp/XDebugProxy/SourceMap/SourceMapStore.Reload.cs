namespace Tyhp.XDebugProxy.SourceMap
{
    public sealed partial class SourceMapStore
    {
        private void ReloadIfNeeded(string? phpFilePath)
        {
            bool dirty = Interlocked.CompareExchange(ref _dirty, 0, 1) == 1;
            bool stale = dirty;
            if (!stale)
            {
                foreach (KeyValuePair<string, CachedMap> pair in _cache)
                {
                    if (IsStale(pair.Key, pair.Value))
                    {
                        stale = true;
                        break;
                    }
                }
            }

            if (stale)
            {
                lock (_loadLock)
                {
                    LoadAllCore();
                }
            }

            if (phpFilePath is null)
            {
                return;
            }

            foreach (string candidate in PhpPathCandidates(phpFilePath))
            {
                string conventionMap = candidate + ".map";
                if (!File.Exists(conventionMap))
                {
                    continue;
                }

                string key = CanonicalKey(conventionMap);
                lock (_loadLock)
                {
                    TryLoadMap(key, requireExists: true);
                }
            }
        }

        private IEnumerable<string> PhpPathCandidates(string phpFilePath)
        {
            yield return phpFilePath;
            if (Path.IsPathRooted(phpFilePath))
            {
                yield break;
            }

            yield return Path.Combine(_rootDirectory, phpFilePath);
            yield return Path.Combine(_rootDirectory, Path.GetFileName(phpFilePath));
        }

        private bool TryGetByConvention(string phpFilePath, out SourceMapFile? map)
        {
            string conventionMap = phpFilePath + ".map";
            string key = CanonicalKey(conventionMap);
            if (_cache.TryGetValue(key, out CachedMap cached))
            {
                map = cached.Map;
                return true;
            }

            map = null;
            return false;
        }

        private void TryLoadMap(string path, bool requireExists)
        {
            if (!File.Exists(path))
            {
                if (_cache.TryRemove(path, out _))
                {
                    this.RebuildIndexesIfUnsuppressed();
                }

                if (requireExists)
                {
                    ReportWarning(
                        SourceMapLoadWarningKind.MapFileMissing,
                        $"Source map file is missing: '{path}'.",
                        path);
                }

                return;
            }

            DateTime lastWriteUtc;
            long length;
            try
            {
                var info = new FileInfo(path);
                lastWriteUtc = info.LastWriteTimeUtc;
                length = info.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ReportWarning(
                    SourceMapLoadWarningKind.MapFileUnreadable,
                    $"Could not read source map '{path}': {ex.Message}",
                    path);
                return;
            }

            if (_cache.TryGetValue(path, out CachedMap existing)
                && existing.LastWriteUtc == lastWriteUtc
                && existing.Length == length)
            {
                return;
            }

            try
            {
                SourceMapFile file = SourceMapFile.Load(path);
                _ = file.DecodedMappings;
                _cache[path] = new CachedMap(file, lastWriteUtc, length);
                this.RebuildIndexesIfUnsuppressed();
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                _cache.TryRemove(path, out _);
                this.RebuildIndexesIfUnsuppressed();
                ReportWarning(
                    SourceMapLoadWarningKind.MapFileMalformed,
                    $"Could not parse source map '{path}': {ex.Message}",
                    path);
            }
        }

        private void RebuildIndexesIfUnsuppressed()
        {
            if (_suppressIndexRebuild == 0)
            {
                RebuildIndexes();
            }
        }

        private static bool IsStale(string path, CachedMap cached)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return true;
                }

                var info = new FileInfo(path);
                return info.LastWriteTimeUtc != cached.LastWriteUtc || info.Length != cached.Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        private void ReportWarning(SourceMapLoadWarningKind kind, string message, string? path)
        {
            var warning = new SourceMapLoadWarning(kind, message, path);
            _warnings.Enqueue(warning);
            try
            {
                _onWarning?.Invoke(warning);
            }
            catch
            {
                // Callback failures must not break map loading.
            }
        }

        private void StartWatchers()
        {
            if (!Directory.Exists(_rootDirectory))
            {
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(_rootDirectory)
                {
                    Filter = "*.map",
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.CreationTime
                        | NotifyFilters.Size,
                    EnableRaisingEvents = _autoReload,
                };
                watcher.Created += OnWatcherEvent;
                watcher.Changed += OnWatcherEvent;
                watcher.Deleted += OnWatcherEvent;
                watcher.Renamed += OnWatcherRenamed;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                ReportWarning(
                    SourceMapLoadWarningKind.MapFileUnreadable,
                    $"Could not watch source maps under '{_rootDirectory}': {ex.Message}",
                    _rootDirectory);
            }
        }

        private void OnWatcherEvent(object sender, FileSystemEventArgs e)
        {
            if (IsPhpMapPath(e.FullPath) || IsPhpMapPath(e.Name))
            {
                Interlocked.Exchange(ref _dirty, 1);
            }
        }

        private void OnWatcherRenamed(object sender, RenamedEventArgs e)
        {
            if (IsPhpMapPath(e.FullPath) || IsPhpMapPath(e.OldFullPath) || IsPhpMapPath(e.Name))
            {
                Interlocked.Exchange(ref _dirty, 1);
            }
        }

        private static bool IsPhpMapPath(string? path)
        {
            return path is not null
                && path.EndsWith(".php.map", StringComparison.OrdinalIgnoreCase);
        }

        private static string CanonicalKey(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return path;
            }
        }

        private static StringComparer CacheKeyComparer =>
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private sealed record CachedMap(SourceMapFile Map, DateTime LastWriteUtc, long Length);
    }
}
