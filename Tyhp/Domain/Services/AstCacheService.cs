using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Tyhp.CLI;
using Tyhp.Config;
using Tyhp.TyhpLang.Ast;

namespace Tyhp.Domain.Services;

public static class AstCacheService
{
    // Per-file grouping key: the lowercase hex of SHA256 over the full relative path. Grouping by the
    // full path (one cache file per source file) rather than a leading path substring eliminates the
    // write amplification of the old scheme, where editing one file rewrote the shared blob of every
    // file that happened to share its first 32 path characters.
    //
    // The key is memoized per filename (GroupingKeyByFilename) so the SHA256 runs at most once per
    // file. Previously the prefix was recomputed with a fresh SHA256 for every cache entry on every
    // flush/load scan, which scaled poorly with project size.
    private static readonly ConcurrentDictionary<string, string> GroupingKeyByFilename = new();

    // Per-group locks keyed by the grouping key STRING. The previous implementation keyed these by
    // byte[], which uses reference equality, so a freshly hashed array never matched a prior one and
    // the "lock" gave no real mutual exclusion (a latent concurrency bug during parallel parsing).
    // String keys make the lock genuinely shared across threads for the same group.
    private static readonly ConcurrentDictionary<string, object> CacheGroupingLocks = new();

    // filename (relative) -> (on-disk write time, last in-memory access time, serialized AST block).
    private static readonly ConcurrentDictionary<string, (long lastWriteTime, long lastMemoryAccessTime, byte[] data)> CacheFiles = new();

    // grouping key -> set of member filenames. Lets flush/load operate on just a group's members
    // instead of scanning every cache entry, avoiding O(entries) work per group operation.
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> GroupMembers = new();

    // Set (deduped) of grouping keys that have unflushed in-memory changes.
    private static readonly ConcurrentDictionary<string, byte> UnwrittenCacheFileGroupingKeys = new();

    private static readonly object FlushMemoryLock = new();

    public static string GetRelativePath(string fileName)
    {
        if (!string.IsNullOrWhiteSpace(fileName) && fileName != "_") {
            // Resolve unrooted paths against the project root (not CWD). Path.GetFullPath on a
            // relative string uses CWD, so a second GetRelativePath on an already-relative cache
            // key (e.g. node.FileName, which is itself GetRelativePath(Identifier)) would invent a
            // different absolute path whenever CWD ≠ project root — breaking AddOrUpdate/Get and
            // order-dependent tests that leave Project.Singleton pointing at a temp project.
            // Canonicalize both sides so symlink-aliased absolute Identifiers (e.g. /tmp/…) still
            // relativize against a physical GetProjectPath() (/private/tmp/…).
            var projectRoot = PathCanonicalizer.GetCanonicalFullPath(
                Project.Singleton?.GetProjectPath() ?? Directory.GetCurrentDirectory());
            var absolute = Path.IsPathRooted(fileName)
                ? PathCanonicalizer.GetCanonicalFullPath(fileName)
                : PathCanonicalizer.GetCanonicalFullPath(Path.Combine(projectRoot, fileName));
            return Path.GetRelativePath(projectRoot, absolute);
        }
        return fileName;
    }

    /// <summary>
    /// Content hash used as the AstCache lookup key for a source file.
    /// Includes a tagless/classic marker so identical bytes that lex differently under
    /// <c>source.tagless</c> do not share a cache entry with the other mode.
    /// Shared by <see cref="CompilationService"/> (user files) and tyhpdef loading.
    /// </summary>
    public static string ComputeContentHash(string content, bool tagless = false)
    {
        var fileBytes = Encoding.UTF8.GetBytes(
            content + (tagless ? "\0tagless" : "\0classic"));
        return Convert.ToHexString(MD5.HashData(fileBytes)).ToLowerInvariant();
    }

    public static void FlushMemory()
    {
        lock (FlushMemoryLock) {
            var groupingKeys = UnwrittenCacheFileGroupingKeys.Keys.ToArray();
            foreach (var groupingKey in groupingKeys) {
                UnwrittenCacheFileGroupingKeys.TryRemove(groupingKey, out _);
            }
            foreach (var groupingKey in groupingKeys) {
                WriteCacheFileFromMemory(groupingKey);
            }
        }
    }
    /// <summary>
    /// Add or update the cache file for the given node
    /// </summary>
    /// <param name="node">The node to add or update the cache for</param>
    public static void AddOrUpdate(SrcFileAst? node)
    {
        if (node == null || String.IsNullOrWhiteSpace(node.FileName)) {
            return;
        }

        var filename = GetRelativePath(node.FileName);
        var serialized = node.Serialize();

        CacheFiles.AddOrUpdate(
            filename,
            (-1L, DateTime.UtcNow.ToBinary(), serialized),
            (k, data) => (-1L, DateTime.UtcNow.ToBinary(), serialized)
        );

        var groupingKey = GetGroupingKey(filename);
        IndexAdd(filename, groupingKey);
        UnwrittenCacheFileGroupingKeys[groupingKey] = 0;
        if (UnwrittenCacheFileGroupingKeys.Count > 100) {
            FlushMemory();
        }
    }
    
    /// <summary>
    /// Get the cached node for the given filename and hash
    /// </summary>
    /// <param name="filename">The filename to get the cache for</param>
    /// <param name="hash">The hash to check against the cached node</param>
    /// <returns>The cached node or null if it does not exist</returns>
    public static SrcFileAst? Get(string filename, string? hash = null)
    {
        if (string.IsNullOrWhiteSpace(filename)) {
            return null;
        }

        filename = GetRelativePath(filename);

        var cachedExists = CacheFiles.TryGetValue(filename, out var cachedData);

        // In-memory fast path: validate the block's file name + content hash straight from the raw
        // bytes (no node objects are built), and only pay for a full deserialize on a confirmed match.
        // This replaces the old partial-then-full double deserialize that ran on every hit.
        if (hash != null && cachedExists && MatchesKey(cachedData.data, filename, hash)) {
            TouchAccessTime(filename, cachedData);
            return Base2Ast.Deserialize<SrcFileAst>(cachedData.data, false);
        }

        if (!cachedExists) {
            if (!LoadCacheForFilename(filename, null, true)) {
                return null;
            }
            cachedExists = CacheFiles.TryGetValue(filename, out cachedData);
        }

        if (!cachedExists) {
            return null;
        }

        // Hash provided: only return the AST when the cached block's file name + hash match.
        if (hash != null) {
            if (MatchesKey(cachedData.data, filename, hash)) {
                TouchAccessTime(filename, cachedData);
                return Base2Ast.Deserialize<SrcFileAst>(cachedData.data, false);
            }

            Base2Ast.TryReadSrcFileKey(cachedData.data, out _, out var cachedHash);
            Message.Debug($"Cache file does not match hash: {filename} -{cachedHash}- +{hash}+");
            return null;
        } else {
            TouchAccessTime(filename, cachedData);
            Base2Ast.TryDeserialize<SrcFileAst>(cachedData.data, out var node, false);
            if (node == null) {
                Message.Debug($"Cache file is invalid: {filename}");
            }
            return node;
        }
    }

    // Validates a serialized SrcFileAst block against an expected relative file name and content hash
    // by reading just its header (no node graph is constructed). Identifier in the block is absolute;
    // GetRelativePath is idempotent for project-relative keys, so applying it once (or twice) matches
    // the key AddOrUpdate stored via GetRelativePath(node.FileName).
    private static bool MatchesKey(byte[] data, string filename, string hash)
    {
        if (!Base2Ast.TryReadSrcFileKey(data, out var identifier, out var valueString)) {
            return false;
        }
        return GetRelativePath(identifier) == filename && valueString == hash;
    }

    private static void TouchAccessTime(string filename, (long lastWriteTime, long lastMemoryAccessTime, byte[] data) cachedData)
    {
        CacheFiles[filename] = (cachedData.lastWriteTime, DateTime.UtcNow.ToBinary(), cachedData.data);
    }

    /// <summary>
    /// Clear the entire file and in-memory cache
    /// </summary>
    public static void Clear()
    {
        var cacheDir = GetCacheDir();
        if (Directory.Exists(cacheDir)) {
            try {
                Directory.Delete(cacheDir, true);
            } catch {
                Message.Error("CLI_FailedClearCacheDir", cacheDir);
            }
        }

        ClearMemory();
        FlushMemory();
    }

    /// <summary>
    /// Clear the file and in-memory cache for the given filename
    /// </summary>
    /// <param name="filename">The filename to clear the cache for</param>
    public static void Clear(string filename)
    {
        ClearMemory(filename);
        FlushMemory();
    }

    /// <summary>
    /// Clear the file and in-memory cache of items older than the given number of days
    /// </summary>
    /// <param name="daysOld">Clear the cache of items that are older than this number of days</param>
    public static void Clear(int daysOld)
    {
        if (daysOld <= 0) {
            Clear();
            return;
        }

        var cacheDir = GetCacheDir();
        if (Directory.Exists(cacheDir)) {
            foreach (var file in Directory.GetFiles(cacheDir)) {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-daysOld)) {
                    try {
                        File.Delete(file);
                    } catch {
                        Message.Error("CLI_FailedDeleteCacheFile", file);
                    }
                }
            }
        }

        // clear all memory cache
        ClearMemory();
        FlushMemory();
    }

    /// <summary>
    /// Clear the in-memory cache
    /// </summary>
    public static void ClearMemory()
    {
        CacheFiles.Clear();
        UnwrittenCacheFileGroupingKeys.Clear();
        GroupMembers.Clear();
        GroupingKeyByFilename.Clear();
    }

    /// <summary>
    /// Clear the in-memory cache for the given filename
    /// </summary>
    /// <param name="filename">The filename to clear the in-memory cache for</param>
    public static void ClearMemory(string filename)
    {
        var relative = GetRelativePath(filename);
        CacheFiles.TryRemove(relative, out _);
        var groupingKey = GetGroupingKey(relative);
        IndexRemove(relative, groupingKey);
        // Mark the group dirty so the next flush rewrites its cache file without this entry.
        UnwrittenCacheFileGroupingKeys[groupingKey] = 0;
    }

    /// <summary>
    /// Clear the in-memory cache for all items older than the given number of milliseconds
    /// </summary>
    /// <param name="millisecondsOld">The number of milliseconds to clear the in-memory cache for</param>
    public static void ClearMemory(int millisecondsOld)
    {
        foreach (var item in CacheFiles) {
            if (DateTime.UtcNow.Subtract(DateTime.FromBinary(item.Value.lastMemoryAccessTime)).TotalMilliseconds > millisecondsOld) {
                ClearMemory(item.Key);
            }
        }
    }

    // The grouping key is the lowercase hex of SHA256 over the full relative path, memoized per
    // filename so the hash is computed once per file (not re-hashed on every cache scan).
    private static string GetGroupingKey(string filename)
        => GroupingKeyByFilename.GetOrAdd(
            filename,
            static key => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant());

    private static void IndexAdd(string filename, string groupingKey)
        => GroupMembers.GetOrAdd(groupingKey, static _ => new ConcurrentDictionary<string, byte>())[filename] = 0;

    private static void IndexRemove(string filename, string groupingKey)
    {
        if (GroupMembers.TryGetValue(groupingKey, out var members)) {
            members.TryRemove(filename, out _);
        }
    }

    private static string GetCacheFilePath(string filename)
    {
        return GetGroupedCacheFilePath(GetGroupingKey(filename));
    }

    private static string GetGroupedCacheFilePath(string groupingKey)
    {
        return Path.Combine(GetCacheDir(), groupingKey + ".ast");
    }

    // Bump when the AST binary serialization format changes so that stale cache entries written
    // by an older format are not reused (the assembly version stays constant across dev builds).
    // Bumped to "4" for: EndLine / EndColumn / EndIndex after StartIndex on every node block.
    private const string CacheFormatVersion = "4";

    // Identifies the exact compiler build that produced a cached AST. The module version id changes
    // whenever this assembly's compiled output changes (deterministic builds keep it stable for
    // unchanged source), so a cache written by a different build of the parser/visitor/AST/serializer
    // logic is never reused. This auto-invalidates the cache on any compiler change without relying on
    // someone remembering to bump CacheFormatVersion or on the (dev-constant) assembly version.
    private static readonly string CompilerBuildId =
        typeof(AstCacheService).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12];

    // How many build-id cache namespaces to retain per format dir before pruning the oldest.
    private const int MaxRetainedBuildDirs = 3;
    private static readonly HashSet<string> PrunedFormatDirs = new();
    private static readonly object PruneLock = new();

    /// <summary>
    /// Root cache directory shared by every format version and compiler build. Honors a project's
    /// explicit <c>cache-dir</c>, otherwise falls back to the per-assembly-version local app data dir.
    /// </summary>
    private static string GetCacheRootDir()
    {
        var version = new Message.VersionHelper().GetAssemblyVersion();
        var cacheDir = Config.Project.Singleton?.CacheDir;
        if (String.IsNullOrWhiteSpace(cacheDir)) {
            cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tyhp" + version, "Cache");
        }
        return cacheDir;
    }

    private static string GetCacheDir()
    {
        var formatDir = Path.Combine(GetCacheRootDir(), "fmt" + CacheFormatVersion);
        var cacheDir = Path.Combine(formatDir, "build-" + CompilerBuildId);
        if (cacheDir.IndexOfAny(Path.GetInvalidPathChars()) != -1) {
            throw new Exception("Invalid cache directory: " + cacheDir);
        }
        PruneStaleBuildDirs(formatDir);
        return cacheDir;
    }

    /// <summary>
    /// Best-effort tidy-up of the format directory: removes cache namespaces from older compiler
    /// builds (keeping the most recently written <see cref="MaxRetainedBuildDirs"/>, always including
    /// the current one) and deletes loose <c>*.ast</c> files left by the pre-namespaced flat layout.
    /// Runs at most once per format dir per process and never throws.
    /// </summary>
    private static void PruneStaleBuildDirs(string formatDir)
    {
        lock (PruneLock) {
            if (!PrunedFormatDirs.Add(formatDir)) {
                return;
            }
        }

        try {
            if (!Directory.Exists(formatDir)) {
                return;
            }

            // Legacy cache files used to live directly under the format dir; they are never valid
            // under the build-namespaced layout, so remove them.
            foreach (var legacyFile in Directory.GetFiles(formatDir, "*.ast")) {
                try {
                    File.Delete(legacyFile);
                } catch {
                    // ignore
                }
            }

            var currentBuildDir = Path.Combine(formatDir, "build-" + CompilerBuildId);
            var stale = Directory.GetDirectories(formatDir, "build-*")
                .Where(d => !string.Equals(Path.GetFullPath(d), Path.GetFullPath(currentBuildDir), StringComparison.Ordinal))
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Skip(MaxRetainedBuildDirs - 1);

            foreach (var dir in stale) {
                try {
                    Directory.Delete(dir, true);
                } catch {
                    // Another process may be using or removing it; ignore.
                }
            }
        } catch {
            // Pruning is non-essential; never let it break a build.
        }
    }

    /// <summary>
    /// Removes the entire on-disk cache (every format version and compiler build) along with the
    /// in-memory cache. Escape hatch for when a stale or corrupt cache is suspected.
    /// </summary>
    /// <returns>The cache root directory that was cleared.</returns>
    public static string ClearAll()
    {
        var root = GetCacheRootDir();
        if (Directory.Exists(root)) {
            try {
                Directory.Delete(root, true);
            } catch {
                Message.Error("CLI_FailedClearCacheDir", root);
            }
        }

        ClearMemory();
        return root;
    }

    /// <summary>
    /// Absolute path of the on-disk AST cache directory for the current compiler build.
    /// </summary>
    public static string GetCacheDirectoryPath() => GetCacheDir();

    /// <summary>
    /// One source-file entry discovered inside an on-disk <c>.ast</c> cache blob.
    /// </summary>
    /// <param name="SourceKey">
    /// The source path key exactly as stored in the cache. Usually relative to the project that
    /// wrote it, but it can also be a synthetic name (e.g. <c>&lt;tyhpdef:embedded:…&gt;</c>), so
    /// callers must resolve it against a project root themselves rather than assume a real file.
    /// </param>
    /// <param name="ContentHash">Content hash stored with the entry, or null when the header was unreadable.</param>
    /// <param name="HeaderUnreadable">True when the serialized block header could not be parsed.</param>
    /// <param name="PayloadUnreadable">
    /// True when payload validation was requested and the serialized AST failed to deserialize.
    /// </param>
    /// <param name="CacheFilePath">Absolute path of the <c>.ast</c> file that contained the entry.</param>
    public readonly record struct DiskCacheEntry(
        string SourceKey,
        string? ContentHash,
        bool HeaderUnreadable,
        bool PayloadUnreadable,
        string CacheFilePath);

    /// <summary>
    /// Reads every <c>.ast</c> blob in the current build's cache directory and returns the
    /// source-file keys + hashes without fully deserializing the AST graphs.
    /// </summary>
    /// <param name="shouldValidatePayload">
    /// Optional predicate over the stored source key. When it returns true, that block is also
    /// deserialized so corrupt payloads (not just corrupt headers) are detected. The cache is
    /// shared by every project, so callers should limit this to entries they care about — a full
    /// AST deserialize per entry is not free.
    /// </param>
    public static IReadOnlyList<DiskCacheEntry> EnumerateDiskCacheEntries(
        Func<string, bool>? shouldValidatePayload = null)
    {
        var results = new List<DiskCacheEntry>();
        var cacheDir = GetCacheDir();
        if (!Directory.Exists(cacheDir))
        {
            return results;
        }

        foreach (var cacheFilePath in Directory.GetFiles(cacheDir, "*.ast"))
        {
            byte[] data;
            try
            {
                data = File.ReadAllBytes(cacheFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(UnreadableEntry(cacheFilePath));
                continue;
            }

            if (data.Length < 32)
            {
                results.Add(UnreadableEntry(cacheFilePath));
                continue;
            }

            int offset = 32;
            var addedForThisFile = false;
            while (offset + 4 <= data.Length)
            {
                int blockSize = BitConverter.ToInt32(data, offset);
                if (blockSize <= 0 || offset + blockSize > data.Length)
                {
                    results.Add(UnreadableEntry(cacheFilePath));
                    addedForThisFile = true;
                    break;
                }

                if (Base2Ast.TryReadSrcFileKey(data.AsSpan(offset, blockSize), out var identifier, out var hash))
                {
                    var payloadUnreadable = shouldValidatePayload?.Invoke(identifier) == true
                        && !Base2Ast.TryDeserialize<SrcFileAst>(data[offset..(offset + blockSize)], out _);
                    results.Add(new DiskCacheEntry(identifier, hash, false, payloadUnreadable, cacheFilePath));
                }
                else
                {
                    results.Add(UnreadableEntry(cacheFilePath));
                }

                addedForThisFile = true;
                offset += blockSize;
            }

            if (!addedForThisFile)
            {
                results.Add(UnreadableEntry(cacheFilePath));
            }
        }

        return results;
    }

    private static DiskCacheEntry UnreadableEntry(string cacheFilePath)
        => new(cacheFilePath, null, true, false, cacheFilePath);

    private static bool LoadCacheForFilename(string filename, (long lastWriteTime, long lastMemoryAccessTime, byte[] data)? existingCachedData = null, bool onlyIfOutOfDate = true)
    {
        if (UnwrittenCacheFileGroupingKeys.ContainsKey(GetGroupingKey(filename))) {
            FlushMemory();
        }

        filename = GetRelativePath(filename);
        bool cachedExists;
        if (existingCachedData == null) {
            cachedExists = CacheFiles.TryGetValue(filename, out var loadedCachedData);
            if (cachedExists) {
                existingCachedData = loadedCachedData;
            }
        } else {
            cachedExists = true;
        }

        var cacheFilePath = GetCacheFilePath(filename);

        if (!File.Exists(cacheFilePath)) {
            return false;
        }

        if (onlyIfOutOfDate && cachedExists) {
            long cacheFileLastWriteTime = File.GetLastWriteTimeUtc(cacheFilePath).ToBinary();
            if (cacheFileLastWriteTime != existingCachedData?.lastWriteTime) {
                LoadCacheFileIntoMemory(cacheFilePath);
                return true;
            }
            return false;
        } else if (!cachedExists) {
            LoadCacheFileIntoMemory(cacheFilePath);
            return true;
        } else {
            return false;
        }
    }

    private static object GetCacheGroupingLock(string groupingKey)
        => CacheGroupingLocks.GetOrAdd(groupingKey, static _ => new object());

    private static void LoadCacheFileIntoMemory(string cacheFilename)
    {
        if (!File.Exists(cacheFilename)) {
            return;
        }

        byte[] data = File.ReadAllBytes(cacheFilename);
        if (data.Length < 32) {
            return;
        }
        byte[] groupingPrefix = data[..32];
        string groupingKey = Convert.ToHexString(groupingPrefix).ToLowerInvariant();

        var cacheGroupingLock = GetCacheGroupingLock(groupingKey);

        lock (cacheGroupingLock) {
            var lastWriteTime = File.GetLastWriteTimeUtc(cacheFilename).ToBinary();
            int offset = 32;

            // Members previously in this group that are not found in the file are stale and removed
            // afterward. Using the group index avoids scanning the entire cache dictionary.
            HashSet<string> stale = GroupMembers.TryGetValue(groupingKey, out var members)
                ? new HashSet<string>(members.Keys)
                : [];

            while (offset < data.Length) {
                int blockSize = BitConverter.ToInt32(data, offset);
                if (blockSize <= 0 || offset + blockSize > data.Length) {
                    break;
                }
                // Store the original serialized block verbatim. We read only the file name from its
                // header (via TryReadSrcFileKey); re-serializing a partially deserialized node would
                // discard all children/flags/attributes (dropping every top-level declaration on a
                // cross-process cache hit), so the raw bytes are preserved as-is. Do not change this
                // to store a re-serialized node.
                var blockData = data[offset..(offset + blockSize)];
                if (Base2Ast.TryReadSrcFileKey(blockData, out var identifier, out _)) {
                    var filename = GetRelativePath(identifier);
                    CacheFiles.AddOrUpdate(
                        filename,
                        (lastWriteTime, DateTime.UtcNow.ToBinary(), blockData),
                        (k, existing) => (lastWriteTime, DateTime.UtcNow.ToBinary(), blockData)
                    );
                    IndexAdd(filename, groupingKey);
                    stale.Remove(filename);
                }
                offset += blockSize;
            }

            foreach (var key in stale) {
                Message.Debug($"Removing cache file entry: {key} (group {groupingKey})");
                CacheFiles.TryRemove(key, out _);
                IndexRemove(key, groupingKey);
            }
        }
    }

    private static void WriteCacheFileFromMemory(string groupingKey)
    {
        var cacheFileLock = GetCacheGroupingLock(groupingKey);
        lock (cacheFileLock) {
            var cacheFilePath = GetGroupedCacheFilePath(groupingKey);

            // Gather the group's present in-memory entries via the index (no full-cache scan). Prune
            // index entries that no longer exist in the cache.
            var memberNames = GroupMembers.TryGetValue(groupingKey, out var members)
                ? members.Keys.ToArray()
                : [];
            var present = new List<byte[]>(memberNames.Length);
            foreach (var name in memberNames) {
                if (CacheFiles.TryGetValue(name, out var value)) {
                    present.Add(value.data);
                } else {
                    IndexRemove(name, groupingKey);
                }
            }

            if (present.Count == 0) {
                if (File.Exists(cacheFilePath)) {
                    File.Delete(cacheFilePath);
                }
                GroupMembers.TryRemove(groupingKey, out _);
                return;
            }

            // Cache file layout: 32-byte group prefix (SHA256 of the path) followed by each member's
            // serialized block verbatim.
            byte[] groupingPrefix = Convert.FromHexString(groupingKey);
            byte[] data = [.. groupingPrefix, .. present.SelectMany(d => d)];

            var folderPath = Path.GetDirectoryName(cacheFilePath);
            if (folderPath != null && !Directory.Exists(folderPath)) {
                Directory.CreateDirectory(folderPath);
            }
            File.WriteAllBytes(cacheFilePath, data);

            var cacheFileLastWriteTime = File.GetLastWriteTimeUtc(cacheFilePath).ToBinary();
            foreach (var name in memberNames) {
                if (CacheFiles.TryGetValue(name, out var value)) {
                    CacheFiles[name] = (cacheFileLastWriteTime, value.lastMemoryAccessTime, value.data);
                }
            }
        }
    }
}