using Microsoft.Extensions.FileSystemGlobbing;
using Tyhp.Config;
using Tyhp.Domain.Diagnostics;
using Tyhp.Domain.Exceptions;

namespace Tyhp.Domain.Services
{
    /// <summary>
    /// Copies <c>output.publishContent</c> files into <c>output.publishPath</c>.
    /// </summary>
    public static class PublishContentService
    {
        private static readonly char[] GlobChars = ['*', '?', '['];

        /// <summary>
        /// Expands and copies every <see cref="OutputConfig.PublishContent"/> entry.
        /// Returns false when any copy error was reported.
        /// </summary>
        public static bool TryCopy(
            Project project,
            DiagnosticBag diagnostics,
            bool dryRun,
            Action<string, string>? logCopy = null)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(diagnostics);

            if (project.Output.PublishContent.Count == 0)
            {
                return true;
            }

            var projectRoot = PathCanonicalizer.GetCanonicalFullPath(project.GetProjectPath());
            var publishRoot = BuildOutputCleaner.ResolvePublishDirectory(
                projectRoot,
                project.Output.PublishPath);

            var success = true;
            foreach (var entry in project.Output.PublishContent)
            {
                if (!TryCopyEntry(projectRoot, publishRoot, entry, diagnostics, dryRun, logCopy))
                {
                    success = false;
                }
            }

            return success;
        }

        internal static List<PublishContentMatch> ExpandEntry(
            string projectRoot,
            PublishContentEntry entry)
        {
            projectRoot = PathCanonicalizer.GetCanonicalFullPath(projectRoot);
            var matches = new List<PublishContentMatch>();
            foreach (var src in entry.Src)
            {
                if (string.IsNullOrWhiteSpace(src))
                {
                    continue;
                }

                matches.AddRange(ExpandSrc(projectRoot, src.Trim(), entry.Flat));
            }

            if (entry.Exclude.Count == 0)
            {
                return matches;
            }

            var excludeMatcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            var hasExclude = false;
            foreach (var exclude in entry.Exclude)
            {
                if (string.IsNullOrWhiteSpace(exclude))
                {
                    continue;
                }

                AddGlobPattern(excludeMatcher, exclude.Trim());
                hasExclude = true;
            }

            if (!hasExclude)
            {
                return matches;
            }

            var filtered = new List<PublishContentMatch>();
            foreach (var match in matches)
            {
                if (IsExcluded(match.SourceFullPath, projectRoot, excludeMatcher, entry.Exclude))
                {
                    continue;
                }

                filtered.Add(match);
            }

            return filtered;
        }

        internal static bool IsGlobPattern(string src)
        {
            return src.AsSpan().IndexOfAny(GlobChars) >= 0;
        }

        private static bool TryCopyEntry(
            string projectRoot,
            string publishRoot,
            PublishContentEntry entry,
            DiagnosticBag diagnostics,
            bool dryRun,
            Action<string, string>? logCopy)
        {
            List<PublishContentMatch> matches;
            try
            {
                matches = ExpandEntry(projectRoot, entry);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                diagnostics.AddError(
                    MessageCode.BuildPublishContentFailed,
                    "",
                    0,
                    0,
                    SummarizeSrc(entry),
                    ex.Message);
                return false;
            }

            if (matches.Count == 0)
            {
                var srcLabel = SummarizeSrc(entry);
                if (entry.Required)
                {
                    diagnostics.AddError(
                        MessageCode.BuildPublishContentFailed,
                        "",
                        0,
                        0,
                        srcLabel,
                        "src matched no files");
                    return false;
                }

                diagnostics.AddWarning(
                    MessageCode.BuildPublishContentUnmatched,
                    "",
                    0,
                    0,
                    srcLabel);
                return true;
            }

            var srcIsGlob = entry.Src.Any(IsGlobPattern);
            var hasFileVars = ConfigInterpolator.ContainsFileVariables(entry.Dst);
            var dstIsDirectory = !hasFileVars && IsDirectoryDestination(entry);
            if (!hasFileVars && !dstIsDirectory && (srcIsGlob || matches.Count != 1))
            {
                diagnostics.AddError(
                    MessageCode.BuildPublishContentFailed,
                    "",
                    0,
                    0,
                    SummarizeSrc(entry),
                    "`dst` is a file name but `src` is a glob or matches multiple files");
                return false;
            }

            var destByNormalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var success = true;

            foreach (var match in matches)
            {
                string destFullPath;
                try
                {
                    destFullPath = hasFileVars
                        ? ResolveFileVarDestination(publishRoot, entry, match, diagnostics)
                        : ResolveDestination(publishRoot, entry.Dst, dstIsDirectory, match);
                    if (destFullPath.Length == 0)
                    {
                        success = false;
                        continue;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    diagnostics.AddError(
                        MessageCode.BuildPublishContentFailed,
                        "",
                        0,
                        0,
                        match.SourceFullPath,
                        ex.Message);
                    success = false;
                    continue;
                }

                if (!PathCanonicalizer.IsUnderRoot(destFullPath, publishRoot))
                {
                    diagnostics.AddError(
                        MessageCode.BuildPublishContentFailed,
                        "",
                        0,
                        0,
                        match.SourceFullPath,
                        "destination is outside `output.publishPath`");
                    success = false;
                    continue;
                }

                var destKey = PathCanonicalizer.GetCanonicalFullPath(destFullPath);
                if (destByNormalized.TryGetValue(destKey, out var otherSource))
                {
                    diagnostics.AddError(
                        MessageCode.BuildPublishContentFailed,
                        "",
                        0,
                        0,
                        destFullPath,
                        $"multiple sources map to the same destination (`{otherSource}` and `{match.SourceFullPath}`)");
                    success = false;
                    continue;
                }

                destByNormalized[destKey] = match.SourceFullPath;

                if (PathsEqual(match.SourceFullPath, destFullPath))
                {
                    continue;
                }

                if (!ShouldCopy(match.SourceFullPath, destFullPath, entry))
                {
                    continue;
                }

                logCopy?.Invoke(match.SourceFullPath, destFullPath);

                if (dryRun)
                {
                    continue;
                }

                if (!TryWriteCopy(match.SourceFullPath, destFullPath, entry, diagnostics))
                {
                    success = false;
                }
            }

            return success;
        }

        internal static string ResolveDestination(
            string publishRoot,
            string dst,
            bool dstIsDirectory,
            PublishContentMatch match)
        {
            if (!dstIsDirectory)
            {
                return Path.GetFullPath(Path.Combine(publishRoot, dst));
            }

            var destDir = dst == "."
                ? publishRoot
                : Path.GetFullPath(Path.Combine(publishRoot, dst));
            return Path.GetFullPath(Path.Combine(destDir, match.RelativeDestPath));
        }

        /// <summary>
        /// Resolves <c>dst</c> after copy-time file-variable expansion. The expanded string is the
        /// destination for that file (no extra <see cref="PublishContentMatch.RelativeDestPath"/>)
        /// unless it is a directory (trailing slash or <see cref="PublishContentEntry.DstForcedDirectory"/>),
        /// in which case only the file name is appended.
        /// </summary>
        internal static string ResolveFileVarDestination(
            string publishRoot,
            PublishContentEntry entry,
            PublishContentMatch match,
            DiagnosticBag diagnostics)
        {
            var reported = false;
            Action<MessageCode, object[]> report = (code, args) =>
            {
                diagnostics.AddError(code, "", 0, 0, args);
                reported = true;
            };

            if (!ConfigInterpolator.TryExpandFileDestination(
                    entry.Dst,
                    match.SourceFullPath,
                    match.RelativeDestPath,
                    "output.publishContent.dst",
                    report,
                    out var expanded))
            {
                if (!reported)
                {
                    diagnostics.AddError(
                        MessageCode.ConfigInterpolationFailed,
                        "",
                        0,
                        0,
                        "fileName",
                        "output.publishContent.dst",
                        "file destination could not be expanded");
                }

                return "";
            }

            var forcedDirectory = entry.DstForcedDirectory
                || expanded.EndsWith('/')
                || expanded.EndsWith('\\');
            var relative = expanded.Trim().TrimStart('/', '\\');
            var dst = PublishContentEntry.NormalizeDst(relative);
            if (forcedDirectory || dst == ".")
            {
                var destDir = dst == "."
                    ? publishRoot
                    : Path.GetFullPath(Path.Combine(publishRoot, dst));
                var fileName = Path.GetFileName(match.RelativeDestPath.Replace('\\', '/'));
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = Path.GetFileName(match.SourceFullPath);
                }

                return Path.GetFullPath(Path.Combine(destDir, fileName));
            }

            return Path.GetFullPath(Path.Combine(publishRoot, dst));
        }

        internal static bool IsDirectoryDestination(PublishContentEntry entry)
        {
            if (entry.DstForcedDirectory || entry.Dst == ".")
            {
                return true;
            }

            return !PublishContentEntry.LooksLikeFileName(entry.Dst);
        }

        private static IEnumerable<PublishContentMatch> ExpandSrc(
            string projectRoot,
            string src,
            bool flat)
        {
            if (!IsGlobPattern(src))
            {
                var resolved = ResolveNonGlobPath(projectRoot, src);
                if (File.Exists(resolved))
                {
                    yield return new PublishContentMatch(
                        PathCanonicalizer.GetCanonicalFullPath(resolved),
                        Path.GetFileName(resolved));
                    yield break;
                }

                if (Directory.Exists(resolved))
                {
                    foreach (var match in ExpandDirectory(resolved, flat))
                    {
                        yield return match;
                    }
                }

                yield break;
            }

            var (searchRoot, pattern, keepStructure) = SplitGlob(projectRoot, src);
            if (!Directory.Exists(searchRoot))
            {
                yield break;
            }

            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            AddGlobPattern(matcher, pattern);
            foreach (var fullPath in matcher.GetResultsInFullPath(searchRoot))
            {
                if (!File.Exists(fullPath) && !IsSymlink(fullPath))
                {
                    continue;
                }

                var relative = Path.GetRelativePath(searchRoot, fullPath);
                var destRelative = flat || !keepStructure
                    ? Path.GetFileName(fullPath)
                    : relative;
                yield return new PublishContentMatch(
                    PathCanonicalizer.GetCanonicalFullPath(fullPath),
                    destRelative.Replace('\\', '/'));
            }
        }

        private static IEnumerable<PublishContentMatch> ExpandDirectory(string directory, bool flat)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }

            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(directory, file);
                var destRelative = flat ? Path.GetFileName(file) : relative;
                yield return new PublishContentMatch(
                    PathCanonicalizer.GetCanonicalFullPath(file),
                    destRelative.Replace('\\', '/'));
            }
        }

        private static (string SearchRoot, string Pattern, bool KeepStructure) SplitGlob(
            string projectRoot,
            string src)
        {
            var normalized = src.Replace('\\', '/');
            var globIndex = normalized.AsSpan().IndexOfAny(GlobChars);
            if (globIndex < 0)
            {
                return (projectRoot, NormalizeGlobPattern(normalized), false);
            }

            var beforeGlob = normalized[..globIndex];
            var lastSlash = beforeGlob.LastIndexOf('/');
            string literalDir;
            string pattern;
            if (lastSlash < 0)
            {
                literalDir = "";
                pattern = normalized;
            }
            else
            {
                literalDir = beforeGlob[..lastSlash];
                pattern = normalized[(lastSlash + 1)..];
            }

            pattern = NormalizeGlobPattern(pattern);
            var keepStructure = normalized.Contains("**", StringComparison.Ordinal);
            var searchRoot = string.IsNullOrEmpty(literalDir)
                ? projectRoot
                : (Path.IsPathRooted(src)
                    ? Path.GetFullPath(literalDir)
                    : Path.GetFullPath(Path.Combine(projectRoot, literalDir)));

            return (searchRoot, pattern, keepStructure);
        }

        private static void AddGlobPattern(Matcher matcher, string pattern)
        {
            var normalized = NormalizeGlobPattern(pattern);
            matcher.AddInclude(normalized);
            if (normalized == "**" || normalized.EndsWith("/**", StringComparison.Ordinal))
            {
                matcher.AddInclude(normalized.TrimEnd('/') + "/*");
            }
        }

        private static string NormalizeGlobPattern(string pattern)
        {
            var normalized = pattern.Replace('\\', '/');
            if (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized[2..];
            }

            if (normalized.EndsWith("/**/", StringComparison.Ordinal))
            {
                return normalized + "*";
            }

            return normalized;
        }

        private static string ResolveNonGlobPath(string projectRoot, string src)
        {
            return Path.IsPathRooted(src)
                ? Path.GetFullPath(src)
                : Path.GetFullPath(Path.Combine(projectRoot, src));
        }

        private static bool IsExcluded(
            string fullPath,
            string projectRoot,
            Matcher excludeMatcher,
            IReadOnlyList<string> exclude)
        {
            if (PathCanonicalizer.IsUnderRoot(fullPath, projectRoot))
            {
                var relative = Path.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
                if (excludeMatcher.Match(projectRoot, relative).HasMatches)
                {
                    return true;
                }
            }

            foreach (var pattern in exclude)
            {
                if (string.IsNullOrWhiteSpace(pattern) || !Path.IsPathRooted(pattern))
                {
                    continue;
                }

                if (!IsGlobPattern(pattern))
                {
                    if (PathsEqual(fullPath, pattern))
                    {
                        return true;
                    }

                    continue;
                }

                var (searchRoot, globPattern, _) = SplitGlob(projectRoot, pattern.Trim());
                if (!Directory.Exists(searchRoot))
                {
                    continue;
                }

                var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
                AddGlobPattern(matcher, globPattern);
                var relative = Path.GetRelativePath(searchRoot, fullPath).Replace('\\', '/');
                if (!relative.StartsWith("..", StringComparison.Ordinal)
                    && matcher.Match(searchRoot, relative).HasMatches)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ShouldCopy(string source, string dest, PublishContentEntry entry)
        {
            if (!DestinationExists(dest))
            {
                return true;
            }

            if (entry.Preserve == PublishContentPreserve.Never)
            {
                return false;
            }

            if (entry.Preserve == PublishContentPreserve.Newest)
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(source) <= File.GetLastWriteTimeUtc(dest))
                    {
                        return false;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    return true;
                }
            }

            if (entry.SkipUnchanged && FilesLookUnchanged(source, dest))
            {
                return false;
            }

            return true;
        }

        private static bool FilesLookUnchanged(string source, string dest)
        {
            try
            {
                var sourceInfo = new FileInfo(source);
                var destInfo = new FileInfo(dest);
                if (!sourceInfo.Exists || !destInfo.Exists)
                {
                    return false;
                }

                return sourceInfo.Length == destInfo.Length
                    && sourceInfo.LastWriteTimeUtc == destInfo.LastWriteTimeUtc;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool TryWriteCopy(
            string source,
            string dest,
            PublishContentEntry entry,
            DiagnosticBag diagnostics)
        {
            try
            {
                var destDirectory = Path.GetDirectoryName(dest);
                if (!string.IsNullOrWhiteSpace(destDirectory))
                {
                    Directory.CreateDirectory(destDirectory);
                }

                if (DestinationExists(dest) && entry.OverwriteReadOnly)
                {
                    ClearReadOnly(dest);
                }

                if (!entry.FollowSymlinks && TryGetSymlinkTarget(source, out var linkTarget))
                {
                    if (DestinationExists(dest))
                    {
                        File.Delete(dest);
                    }

                    File.CreateSymbolicLink(dest, linkTarget);
                    return true;
                }

                File.Copy(source, dest, overwrite: true);
                File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(source));
                return true;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException
                    or PlatformNotSupportedException)
            {
                diagnostics.AddError(
                    MessageCode.BuildPublishContentFailed,
                    "",
                    0,
                    0,
                    source,
                    $"{dest}: {ex.Message}");
                return false;
            }
        }

        private static void ClearReadOnly(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // File.Copy reports the failure if the destination stays read-only.
            }
        }

        private static bool DestinationExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path) || IsSymlink(path);
        }

        private static bool IsSymlink(string path)
        {
            try
            {
                return new FileInfo(path).LinkTarget != null
                    || new DirectoryInfo(path).LinkTarget != null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool TryGetSymlinkTarget(string path, out string target)
        {
            target = "";
            try
            {
                var info = new FileInfo(path);
                if (info.LinkTarget != null)
                {
                    target = info.LinkTarget;
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            return false;
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(
                PathCanonicalizer.GetCanonicalFullPath(left),
                PathCanonicalizer.GetCanonicalFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string SummarizeSrc(PublishContentEntry entry)
        {
            return string.Join(", ", entry.Src);
        }
    }

    /// <summary>One source file matched by a publish-content rule.</summary>
    internal readonly struct PublishContentMatch
    {
        public PublishContentMatch(string sourceFullPath, string relativeDestPath)
        {
            this.SourceFullPath = sourceFullPath;
            this.RelativeDestPath = relativeDestPath.Replace('\\', '/');
        }

        public string SourceFullPath { get; }

        public string RelativeDestPath { get; }
    }
}
