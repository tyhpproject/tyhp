namespace Tyhp.XDebugProxy.SourceMap
{
    public sealed partial class SourceMapStore
    {
        private LookupIndexes _indexes = LookupIndexes.Empty;
        private int _suppressIndexRebuild;

        private sealed class LookupIndexes
        {
            public static readonly LookupIndexes Empty = new();

            public Dictionary<string, SourceMapFile> ByPhpPath { get; } = new(CacheKeyComparer);

            public Dictionary<string, List<SourceMapFile>> ByTyhpPath { get; } = new(CacheKeyComparer);
        }

        private void RebuildIndexes()
        {
            var next = new LookupIndexes();
            foreach (CachedMap entry in _cache.Values)
            {
                IndexPhpKeys(next.ByPhpPath, entry.Map);
                IndexTyhpKeys(next.ByTyhpPath, entry.Map);
            }

            _indexes = next;
        }

        private void IndexPhpKeys(Dictionary<string, SourceMapFile> byPhp, SourceMapFile map)
        {
            if (!string.IsNullOrWhiteSpace(map.MapFilePath))
            {
                string phpFromMap = SourceMapFile.MapPathToPhpPath(map.MapFilePath);
                AddPhpKey(byPhp, phpFromMap, map);
                AddPhpKey(byPhp, CanonicalKey(phpFromMap), map);
            }

            if (!string.IsNullOrWhiteSpace(map.File))
            {
                AddPhpKey(byPhp, map.File, map);
                string combined = Path.Combine(_rootDirectory, map.File);
                AddPhpKey(byPhp, combined, map);
                AddPhpKey(byPhp, CanonicalKey(combined), map);
            }
        }

        private static void AddPhpKey(Dictionary<string, SourceMapFile> byPhp, string key, SourceMapFile map)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            string normalized = SourceMapFile.NormalizePath(key);
            byPhp.TryAdd(key, map);
            byPhp.TryAdd(normalized, map);

            string fileName = Path.GetFileName(normalized);
            if (!string.IsNullOrEmpty(fileName))
            {
                byPhp.TryAdd(fileName, map);
            }
        }

        private static void IndexTyhpKeys(Dictionary<string, List<SourceMapFile>> byTyhp, SourceMapFile map)
        {
            foreach (string key in map.EnumerateSourceLookupKeys())
            {
                AddTyhpKey(byTyhp, key, map);
            }
        }

        private static void AddTyhpKey(
            Dictionary<string, List<SourceMapFile>> byTyhp,
            string key,
            SourceMapFile map)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            AddTyhpKeyExact(byTyhp, key, map);
            string normalized = SourceMapFile.NormalizePath(key);
            AddTyhpKeyExact(byTyhp, normalized, map);
        }

        private static void AddTyhpKeyExact(
            Dictionary<string, List<SourceMapFile>> byTyhp,
            string key,
            SourceMapFile map)
        {
            if (!byTyhp.TryGetValue(key, out List<SourceMapFile>? list))
            {
                list = [];
                byTyhp[key] = list;
            }

            if (!list.Contains(map))
            {
                list.Add(map);
            }
        }

        private SourceMapFile? LookupPhpIndex(string phpFilePath)
        {
            LookupIndexes indexes = _indexes;
            foreach (string key in PhpIndexKeys(phpFilePath))
            {
                if (indexes.ByPhpPath.TryGetValue(key, out SourceMapFile? map))
                {
                    return map;
                }
            }

            return null;
        }

        private List<SourceMapFile>? LookupTyhpIndex(string tyhpFilePath)
        {
            LookupIndexes indexes = _indexes;
            foreach (string key in TyhpIndexKeys(tyhpFilePath))
            {
                if (indexes.ByTyhpPath.TryGetValue(key, out List<SourceMapFile>? maps) && maps.Count > 0)
                {
                    return maps;
                }
            }

            return null;
        }

        private IEnumerable<string> PhpIndexKeys(string phpFilePath)
        {
            yield return phpFilePath;
            string normalized = SourceMapFile.NormalizePath(phpFilePath);
            yield return normalized;
            yield return CanonicalKey(phpFilePath);
            string fileName = Path.GetFileName(normalized);
            if (!string.IsNullOrEmpty(fileName))
            {
                yield return fileName;
            }
        }

        private IEnumerable<string> TyhpIndexKeys(string tyhpFilePath)
        {
            yield return tyhpFilePath;
            string normalized = SourceMapFile.NormalizePath(tyhpFilePath);
            yield return normalized;

            string? fullPath = TryGetFullPath(tyhpFilePath);
            if (fullPath is not null)
            {
                yield return fullPath;
            }

            string fileName = Path.GetFileName(normalized);
            if (!string.IsNullOrEmpty(fileName))
            {
                yield return fileName;
            }
        }

        private static string? TryGetFullPath(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }
    }
}
