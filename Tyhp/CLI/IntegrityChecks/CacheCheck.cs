using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Services;

namespace Tyhp.CLI.IntegrityChecks
{
    /// <summary>
    /// Validates on-disk AST cache entries against current source file hashes.
    /// </summary>
    public sealed class CacheCheck : IIntegrityCheck
    {
        private readonly Project _project;

        public CacheCheck(Project project)
        {
            this._project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public string Name => Message.Localize("CLI_IntegrityCheckNameCache");

        public Task<IntegrityCheckResult> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var details = new List<string>();
            var cacheDir = AstCacheService.GetCacheDirectoryPath();
            details.Add(Message.Localize("CLI_IntegrityCacheDirectory", cacheDir));

            if (!Directory.Exists(cacheDir))
            {
                details.Add(Message.Localize("CLI_IntegrityCacheMissingOk"));
                return Task.FromResult(IntegrityCheckResult.Pass(
                    Message.Localize("CLI_IntegrityCacheNoneOk"),
                    details));
            }

            // The on-disk cache is shared by every project built with this compiler, so entries that
            // do not resolve to a file inside this project (other projects, embedded tyhpdefs) are
            // none of this project's business: they are neither validated nor reported as problems.
            // Canonicalize so symlink-aliased project roots (e.g. /tmp vs /private/tmp) match
            // absolute cache keys written under a different spelling of the same tree.
            var projectRoot = PathCanonicalizer.GetCanonicalFullPath(this._project.GetProjectPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            IReadOnlyList<AstCacheService.DiskCacheEntry> entries;
            try
            {
                entries = AstCacheService.EnumerateDiskCacheEntries(
                    key => TryResolveProjectSource(key, projectRoot, out _));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Task.FromResult(IntegrityCheckResult.Fail(
                    Message.Localize("CLI_IntegrityCacheEnumerateFailed", ex.Message),
                    details));
            }

            if (entries.Count == 0)
            {
                details.Add(Message.Localize("CLI_IntegrityCacheEmptyOk"));
                return Task.FromResult(IntegrityCheckResult.Pass(
                    Message.Localize("CLI_IntegrityCacheNoneOk"),
                    details));
            }

            details.Add(Message.Localize("CLI_IntegrityCacheEntryCount", entries.Count));

            var stale = 0;
            var corrupted = 0;
            var ok = 0;
            var foreign = 0;
            var problems = new List<string>();
            var tagless = this._project.Tagless;

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                if (entry.HeaderUnreadable)
                {
                    corrupted++;
                    problems.Add(Message.Localize(
                        "CLI_IntegrityCacheCorruptedEntry",
                        entry.CacheFilePath));
                    continue;
                }

                if (!TryResolveProjectSource(entry.SourceKey, projectRoot, out var absoluteSource))
                {
                    // Counted in the summary only: a shared cache routinely holds hundreds of these
                    // and listing them would bury the entries that do belong to this project.
                    foreign++;
                    continue;
                }

                var displayPath = Path.GetRelativePath(projectRoot, absoluteSource);

                if (!File.Exists(absoluteSource))
                {
                    stale++;
                    details.Add(Message.Localize("CLI_IntegrityCacheStaleMissingSource", displayPath));
                    continue;
                }

                string content;
                try
                {
                    content = File.ReadAllText(absoluteSource);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    stale++;
                    details.Add(Message.Localize(
                        "CLI_IntegrityCacheSourceReadFailed",
                        displayPath,
                        ex.Message));
                    continue;
                }

                var currentHash = AstCacheService.ComputeContentHash(content, tagless);
                if (!string.Equals(currentHash, entry.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    stale++;
                    details.Add(Message.Localize("CLI_IntegrityCacheStaleHash", displayPath));
                    continue;
                }

                // Only entries the next compile would actually reuse are worth failing over.
                if (entry.PayloadUnreadable)
                {
                    corrupted++;
                    problems.Add(Message.Localize(
                        "CLI_IntegrityCacheDeserializeFailed",
                        displayPath,
                        entry.CacheFilePath));
                    continue;
                }

                ok++;
                details.Add(Message.Localize("CLI_IntegrityCacheEntryOk", displayPath));
            }

            details.Insert(2, Message.Localize(
                "CLI_IntegrityCacheSummary",
                ok,
                stale,
                corrupted,
                foreign));

            if (corrupted > 0)
            {
                problems.Add(Message.Localize("CLI_IntegrityCacheClearSuggestion"));
                return Task.FromResult(IntegrityCheckResult.Fail(
                    Message.Localize("CLI_IntegrityCacheFailed", corrupted),
                    details,
                    problems));
            }

            if (stale > 0)
            {
                // Stale entries are self-healing: the next compile re-parses the file and rewrites
                // the entry. Report them, but do not fail the run over them.
                details.Add(Message.Localize("CLI_IntegrityCacheClearSuggestion"));
                return Task.FromResult(IntegrityCheckResult.Pass(
                    Message.Localize("CLI_IntegrityCachePassedWithStale", ok, stale),
                    details));
            }

            return Task.FromResult(IntegrityCheckResult.Pass(
                Message.Localize("CLI_IntegrityCachePassed", ok),
                details));
        }

        /// <summary>
        /// Resolves a stored cache key to an existing-or-missing path inside this project.
        /// Returns false for synthetic keys (embedded tyhpdefs) and for anything that lands
        /// outside <paramref name="projectRoot"/>.
        /// </summary>
        /// <remarks>
        /// <paramref name="projectRoot"/> must already be a canonical absolute path (no trailing
        /// separator). Absolute keys are canonicalized before the under-root check so symlink
        /// spellings of the same tree are not classified as foreign.
        /// </remarks>
        internal static bool TryResolveProjectSource(string sourceKey, string projectRoot, out string absolutePath)
        {
            absolutePath = string.Empty;

            // Embedded/built-in sources are cached under synthetic names such as
            // `<tyhpdef:embedded:__tyhp_types>` (sometimes already joined to a directory by the
            // cache's path normalization), and "_" is the placeholder for content with no file.
            if (string.IsNullOrWhiteSpace(sourceKey)
                || sourceKey == "_"
                || Path.GetFileName(sourceKey.AsSpan()) is [] or ['<', ..])
            {
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.IsPathRooted(sourceKey)
                    ? PathCanonicalizer.GetCanonicalFullPath(sourceKey)
                    : PathCanonicalizer.GetCanonicalFullPath(Path.Combine(projectRoot, sourceKey));
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
            {
                return false;
            }

            if (!PathCanonicalizer.IsUnderRoot(fullPath, projectRoot))
            {
                return false;
            }

            absolutePath = fullPath;
            return true;
        }
    }
}
